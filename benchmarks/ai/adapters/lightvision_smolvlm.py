#!/usr/bin/env python3
"""SmolVLM-500M Q8 llama.cpp CPU adapter for Lumine vision benchmarks."""

from __future__ import annotations

import hashlib
import json
import os
import socket
import subprocess
import sys
import time
import zipfile
from pathlib import Path
from typing import Any

from vision_common import (
    VISION_JSON_SCHEMA,
    ground_truth,
    image_data_uri,
    image_reference,
    score_output,
    structured_prompt,
)


def fail(message: str) -> None:
    print(json.dumps({"status": "error", "score": 0.0, "error": message}, ensure_ascii=False))
    raise SystemExit(0)


try:
    import psutil
    import requests
    from huggingface_hub import hf_hub_download
except Exception as exc:
    fail(f"SmolVLM adapter dependency error: {exc}")


def param(model: dict[str, Any], key: str, default: str = "") -> str:
    return str((model.get("parameters") or {}).get(key, default))


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_file(path: Path, expected_hash: str, expected_size: int = 0) -> None:
    if expected_size and path.stat().st_size != expected_size:
        raise ValueError(f"{path.name} size mismatch: {path.stat().st_size} != {expected_size}")
    expected_hash = expected_hash.lower()
    if not expected_hash:
        return
    stamp = path.with_name(path.name + f".lumine-{expected_hash[:16]}.sha256")
    try:
        if stamp.read_text(encoding="utf-8").strip().lower() == expected_hash:
            return
    except OSError:
        pass
    actual = hash_file(path)
    if actual != expected_hash:
        raise ValueError(f"{path.name} sha256 mismatch: {actual} != {expected_hash}")
    try:
        stamp.write_text(expected_hash + "\n", encoding="utf-8")
    except OSError:
        pass


def resolve_models(model: dict[str, Any]) -> tuple[Path, Path]:
    repo_id = param(model, "repoId")
    revision = str(model.get("version") or "")
    model_file = param(model, "modelFile")
    mmproj_file = param(model, "mmprojFile")
    if not all((repo_id, revision, model_file, mmproj_file)):
        raise ValueError("SmolVLM profile is missing repo/revision/model/mmproj")

    model_path = Path(hf_hub_download(repo_id=repo_id, filename=model_file, revision=revision))
    mmproj_path = Path(hf_hub_download(repo_id=repo_id, filename=mmproj_file, revision=revision))
    verify_file(model_path, param(model, "modelSha256"), int(param(model, "modelSizeBytes", "0")))
    verify_file(mmproj_path, param(model, "mmprojSha256"), int(param(model, "mmprojSizeBytes", "0")))
    return model_path, mmproj_path


def safe_extract_zip(archive: Path, target: Path) -> None:
    target.mkdir(parents=True, exist_ok=True)
    root = target.resolve()
    with zipfile.ZipFile(archive) as zf:
        for member in zf.infolist():
            destination = (target / member.filename).resolve()
            if root != destination and root not in destination.parents:
                raise ValueError(f"unsafe llama.cpp archive path: {member.filename}")
        zf.extractall(target)


def resolve_server(model: dict[str, Any]) -> Path:
    override = os.environ.get("LUMINE_LLAMA_SERVER", "").strip()
    if override:
        path = Path(override)
        if not path.is_file():
            raise ValueError(f"LUMINE_LLAMA_SERVER not found: {path}")
        return path

    release = param(model, "llamaRelease")
    expected_hash = param(model, "llamaWindowsCpuArchiveSha256")
    if not release or not expected_hash:
        raise ValueError("profile does not pin llama.cpp runtime")

    cache = Path.home() / ".cache" / "lumine" / "benchmarks" / "llama.cpp" / release
    archive = cache / f"llama-{release}-bin-win-cpu-x64.zip"
    extracted = cache / "runtime"
    candidates = list(extracted.rglob("llama-server.exe")) if extracted.exists() else []
    if candidates:
        return candidates[0]

    cache.mkdir(parents=True, exist_ok=True)
    if not archive.is_file():
        url = f"https://github.com/ggml-org/llama.cpp/releases/download/{release}/{archive.name}"
        partial = archive.with_suffix(archive.suffix + ".part")
        with requests.get(url, stream=True, timeout=(20, 120)) as response:
            response.raise_for_status()
            with partial.open("wb") as handle:
                for chunk in response.iter_content(chunk_size=1024 * 1024):
                    if chunk:
                        handle.write(chunk)
        partial.replace(archive)
    verify_file(archive, expected_hash)

    if extracted.exists():
        import shutil
        shutil.rmtree(extracted)
    safe_extract_zip(archive, extracted)
    candidates = list(extracted.rglob("llama-server.exe"))
    if not candidates:
        raise ValueError("llama-server.exe not found in pinned runtime archive")
    return candidates[0]


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


class Server:
    def __init__(self, executable: Path, model_path: Path, mmproj_path: Path, threads: int, context: int):
        self.executable = executable
        self.model_path = model_path
        self.mmproj_path = mmproj_path
        self.threads = threads
        self.context = context
        self.port = free_port()
        self.process: subprocess.Popen[bytes] | None = None

    @property
    def base_url(self) -> str:
        return f"http://127.0.0.1:{self.port}"

    def start(self) -> None:
        args = [
            str(self.executable),
            "-m", str(self.model_path),
            "--mmproj", str(self.mmproj_path),
            "--host", "127.0.0.1",
            "--port", str(self.port),
            "--ctx-size", str(self.context),
            "--threads", str(self.threads),
            "--parallel", "1",
            "--no-mmproj-offload",
            "-ngl", "0",
            "--no-webui",
        ]
        self.process = subprocess.Popen(
            args,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        deadline = time.monotonic() + 120.0
        last_error = ""
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(f"llama-server exited during startup: {self.process.returncode}")
            try:
                response = requests.get(self.base_url + "/health", timeout=1)
                if response.ok:
                    return
                last_error = f"HTTP {response.status_code}"
            except requests.RequestException as exc:
                last_error = str(exc)
            time.sleep(0.2)
        self.stop()
        raise TimeoutError(f"llama-server health timeout: {last_error}")

    def stop(self) -> None:
        if self.process is None:
            return
        process = self.process
        self.process = None
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

    def rss_mb(self) -> float:
        if self.process is None or self.process.poll() is not None:
            return 0.0
        root = psutil.Process(self.process.pid)
        rss = root.memory_info().rss
        for child in root.children(recursive=True):
            try:
                rss += child.memory_info().rss
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                pass
        return rss / (1024.0 * 1024.0)


def infer(server: Server, image_path: Path, mode: str) -> tuple[dict[str, Any], float, int]:
    payload = {
        "model": "local",
        "temperature": 0,
        "max_tokens": 512,
        "messages": [{
            "role": "user",
            "content": [
                {"type": "text", "text": structured_prompt(mode)},
                {"type": "image_url", "image_url": {"url": image_data_uri(image_path)}},
            ],
        }],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "lumine_lightweight_vision",
                "strict": True,
                "schema": VISION_JSON_SCHEMA,
            },
        },
    }
    started = time.perf_counter()
    response = requests.post(
        server.base_url + "/v1/chat/completions",
        json=payload,
        timeout=180,
    )
    latency_ms = (time.perf_counter() - started) * 1000.0
    response.raise_for_status()
    body = response.json()
    content = body["choices"][0]["message"]["content"]
    if isinstance(content, list):
        content = "".join(str(item.get("text", "")) if isinstance(item, dict) else str(item) for item in content)
    output = json.loads(str(content))
    usage = body.get("usage") or {}
    tokens = int(usage.get("completion_tokens") or 0)
    return output, latency_ms, tokens


def main() -> None:
    try:
        request = json.load(sys.stdin)
        profile = request.get("model") or {}
        fixture = request.get("fixture") or {}
        category = str(fixture.get("category") or "")
        fixture_dir = Path(str(request.get("fixtureDir") or ""))
        if not fixture_dir.is_dir():
            raise ValueError("fixtureDir does not exist")

        model_path, mmproj_path = resolve_models(profile)
        server_exe = resolve_server(profile)
        threads = max(1, int(param(profile, "threads", "8")))
        context = max(1024, int(param(profile, "context", "4096")))

        if category == "model_size":
            size = model_path.stat().st_size + mmproj_path.stat().st_size
            print(json.dumps({
                "status": "ok", "score": 1.0,
                "metrics": {"modelSizeMb": size / (1024.0 * 1024.0)},
                "output": {
                    "modelBytes": model_path.stat().st_size,
                    "mmprojBytes": mmproj_path.stat().st_size,
                }
            }, ensure_ascii=False))
            return

        image_path = image_reference(fixture_dir, fixture)

        def new_server() -> Server:
            return Server(server_exe, model_path, mmproj_path, threads, context)

        if category == "cold_start":
            runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
            samples: list[float] = []
            for _ in range(runs):
                server = new_server()
                started = time.perf_counter()
                try:
                    server.start()
                    infer(server, image_path, "short")
                    samples.append((time.perf_counter() - started) * 1000.0)
                finally:
                    server.stop()
            print(json.dumps({
                "status": "ok", "score": 1.0,
                "metrics": {"coldStartMs": sum(samples) / len(samples)},
                "output": {"samplesMs": samples},
            }, ensure_ascii=False))
            return

        if category == "windows_runtime_stability":
            cycles = max(1, int((fixture.get("input") or {}).get("cycles") or 20))
            success = 0
            errors: list[str] = []
            for _ in range(cycles):
                server = new_server()
                try:
                    server.start()
                    infer(server, image_path, "short")
                    success += 1
                except Exception as exc:
                    errors.append(str(exc))
                finally:
                    server.stop()
            rate = success / cycles
            print(json.dumps({
                "status": "ok" if success else "error",
                "score": rate,
                "metrics": {"runtimeSuccessRate": rate},
                "output": {"cycles": cycles, "successfulCycles": success, "errors": errors[:5]},
                **({"error": "all stability cycles failed"} if not success else {}),
            }, ensure_ascii=False))
            return

        server = new_server()
        server.start()
        try:
            if category == "ram":
                runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
                rss = [server.rss_mb()]
                latencies: list[float] = []
                for _ in range(runs):
                    _, latency, _ = infer(server, image_path, "short")
                    latencies.append(latency)
                    rss.append(server.rss_mb())
                print(json.dumps({
                    "status": "ok", "score": 1.0,
                    "metrics": {"ramMb": max(rss), "latencyMs": sum(latencies) / len(latencies)},
                    "output": {"rssSamplesMb": rss},
                }, ensure_ascii=False))
                return

            if category == "cpu_latency_tokens_sec":
                spec = fixture.get("input") or {}
                warmups = max(0, int(spec.get("warmupRuns") or 2))
                runs = max(1, int(spec.get("measureRuns") or 5))
                for _ in range(warmups):
                    infer(server, image_path, "short")
                latencies: list[float] = []
                tokens = 0
                for _ in range(runs):
                    _, latency, count = infer(server, image_path, "short")
                    latencies.append(latency)
                    tokens += count
                total_seconds = sum(latencies) / 1000.0
                print(json.dumps({
                    "status": "ok", "score": 1.0,
                    "metrics": {
                        "latencyMs": sum(latencies) / len(latencies),
                        "tokensPerSecond": tokens / total_seconds if total_seconds > 0 else 0.0,
                    },
                    "output": {"measureRuns": runs, "latenciesMs": latencies},
                }, ensure_ascii=False))
                return

            if category == "lightweight_vision":
                mode = str((fixture.get("input") or {}).get("mode") or "detailed")
                output, latency, tokens = infer(server, image_path, mode)
                expected = ground_truth(fixture_dir, fixture)
                score, scoring = score_output(expected, output)
                print(json.dumps({
                    "status": "ok",
                    "score": score,
                    "metrics": {"latencyMs": latency},
                    "output": {**output, "scoring": scoring, "generatedTokens": tokens},
                }, ensure_ascii=False))
                return
        finally:
            server.stop()

        print(json.dumps({
            "status": "skipped", "score": 0.0,
            "notes": f"SmolVLM adapter does not support category {category}",
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
