#!/usr/bin/env python3
"""Text-only llama.cpp Prompt Engine benchmark adapter for Lumine."""

from __future__ import annotations

import hashlib
import json
import os
import re
import socket
import subprocess
import sys
import time
import zipfile
from pathlib import Path
from typing import Any

from prompt_common import PROMPT_JSON_SCHEMA, SYSTEM_PROMPT, build_user_prompt, score_output


def fail(message: str) -> None:
    print(json.dumps({"status": "error", "score": 0.0, "error": message}, ensure_ascii=False))
    raise SystemExit(0)


try:
    import psutil
    import requests
    from huggingface_hub import hf_hub_download, model_info
except Exception as exc:
    fail(f"Prompt Engine adapter dependency error: {exc}")


QUALITY_CATEGORIES = {
    "ja_to_prompt",
    "il_prompt",
    "model_profile_conversion",
    "structured_json",
    "partial_edit_constraints",
    "lora_trigger_preservation",
    "adult_content_robustness",
}


def param(model: dict[str, Any], key: str, default: str = "") -> str:
    return str((model.get("parameters") or {}).get(key, default))


def bool_param(model: dict[str, Any], key: str, default: bool = False) -> bool:
    return param(model, key, "true" if default else "false").strip().lower() in {
        "1", "true", "yes", "on",
    }


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_file(path: Path, expected_hash: str, expected_size: int = 0) -> str:
    if expected_size and path.stat().st_size != expected_size:
        raise ValueError(f"{path.name} size mismatch: got {path.stat().st_size}, want {expected_size}")
    actual = hash_file(path)
    expected_hash = expected_hash.lower().strip()
    if expected_hash and actual != expected_hash:
        raise ValueError(f"{path.name} sha256 mismatch: got {actual}, want {expected_hash}")
    return actual


def resolve_model(model: dict[str, Any]) -> tuple[Path, str, str]:
    repo_id = param(model, "repoId")
    revision = str(model.get("version") or "")
    requested_file = param(model, "modelFile")
    file_regex = param(model, "modelFileRegex")
    if not repo_id or not revision or (not requested_file and not file_regex):
        raise ValueError("profile requires repoId/version and modelFile or modelFileRegex")

    info = model_info(repo_id, revision=revision)
    resolved_revision = str(info.sha or "")
    if not resolved_revision:
        raise ValueError(f"could not resolve immutable revision for {repo_id}@{revision}")

    model_file = requested_file
    if not model_file:
        matcher = re.compile(file_regex)
        matches = [
            str(sibling.rfilename)
            for sibling in (info.siblings or [])
            if sibling.rfilename and matcher.fullmatch(str(sibling.rfilename))
        ]
        if len(matches) != 1:
            raise ValueError(
                f"modelFileRegex must match exactly one file at {repo_id}@{resolved_revision}; "
                f"matched {matches}"
            )
        model_file = matches[0]

    model_path = Path(hf_hub_download(
        repo_id=repo_id,
        filename=model_file,
        revision=resolved_revision,
    ))
    actual_hash = verify_file(
        model_path,
        param(model, "modelSha256"),
        int(param(model, "modelSizeBytes", "0")),
    )
    return model_path, resolved_revision, actual_hash


def safe_extract_zip(archive: Path, target: Path) -> None:
    target.mkdir(parents=True, exist_ok=True)
    root = target.resolve()
    with zipfile.ZipFile(archive) as zf:
        for member in zf.infolist():
            destination = (target / member.filename).resolve()
            if root != destination and root not in destination.parents:
                raise ValueError(f"unsafe llama.cpp archive path: {member.filename}")
            mode = (member.external_attr >> 16) & 0o170000
            if mode == 0o120000:
                raise ValueError(f"unsupported symlink in llama.cpp archive: {member.filename}")
        zf.extractall(target)


def resolve_server(model: dict[str, Any]) -> Path:
    override = os.environ.get("LUMINE_PROMPT_LLAMA_SERVER", "").strip()
    if override:
        path = Path(override)
        if not path.is_file():
            raise ValueError(f"LUMINE_PROMPT_LLAMA_SERVER not found: {path}")
        return path

    release = param(model, "llamaRelease")
    expected_hash = param(model, "llamaWindowsCpuArchiveSha256")
    if not release or not expected_hash:
        raise ValueError("profile does not pin an upstream llama.cpp runtime")

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


def server_extra_args(model: dict[str, Any]) -> list[str]:
    raw = param(model, "serverArgsJson", "[]").strip()
    value = json.loads(raw or "[]")
    if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
        raise ValueError("serverArgsJson must be a JSON array of strings")
    protected = {
        "-m", "--model", "--host", "--port", "--ctx-size", "--threads",
        "--parallel", "-ngl", "--n-gpu-layers",
    }
    for item in value:
        option = item.split("=", 1)[0]
        if option in protected:
            raise ValueError(f"serverArgsJson may not override protected option {option}")
    return list(value)


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


class Server:
    def __init__(
        self,
        executable: Path,
        model_path: Path,
        threads: int,
        context: int,
        extra_args: list[str] | None = None,
    ) -> None:
        self.executable = executable
        self.model_path = model_path
        self.threads = threads
        self.context = context
        self.extra_args = list(extra_args or [])
        self.port = free_port()
        self.process: subprocess.Popen[bytes] | None = None

    @property
    def base_url(self) -> str:
        return f"http://127.0.0.1:{self.port}"

    def start(self) -> None:
        args = [
            str(self.executable), "-m", str(self.model_path),
            "--host", "127.0.0.1", "--port", str(self.port),
            "--ctx-size", str(self.context), "--threads", str(self.threads),
            "--parallel", "1", "-ngl", "0", "--no-webui",
        ]
        args.extend(self.extra_args)
        self.process = subprocess.Popen(
            args,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        deadline = time.monotonic() + 180.0
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
                process.wait(timeout=15)
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


def strip_thinking(text: str) -> str:
    marker = "[End thinking]"
    if marker in text:
        text = text.rsplit(marker, 1)[1].strip()
    fence = chr(96) * 3
    if text.startswith(fence):
        text = text.removeprefix(fence + "json").removeprefix(fence + "JSON").removeprefix(fence)
        text = text.removesuffix(fence).strip()
    return text


def infer(server: Server, fixture: dict[str, Any], profile: dict[str, Any]) -> tuple[dict[str, Any], float, int]:
    payload: dict[str, Any] = {
        "model": "local",
        "temperature": 0,
        "max_tokens": max(256, int(param(profile, "maxTokens", "1024"))),
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": build_user_prompt(fixture)},
        ],
        "response_format": {"type": "json_schema", "schema": PROMPT_JSON_SCHEMA},
    }
    if bool_param(profile, "disableThinking", False):
        payload["chat_template_kwargs"] = {"enable_thinking": False}

    started = time.perf_counter()
    response = requests.post(server.base_url + "/v1/chat/completions", json=payload, timeout=300)
    latency_ms = (time.perf_counter() - started) * 1000.0
    response.raise_for_status()
    body = response.json()
    choices = body.get("choices") or []
    if not choices:
        raise ValueError("llama.cpp response contains no choices")
    raw_content = ((choices[0].get("message") or {}).get("content"))
    if isinstance(raw_content, list):
        raw_content = "".join(
            str(item.get("text", "")) if isinstance(item, dict) else str(item)
            for item in raw_content
        )
    text = strip_thinking(str(raw_content or "").strip())
    output = json.loads(text)
    if not isinstance(output, dict):
        raise ValueError("Prompt Engine response must be a JSON object")
    usage = body.get("usage") or {}
    tokens = int(usage.get("completion_tokens") or 0)
    return output, latency_ms, tokens


def perf_fixture(fixture: dict[str, Any]) -> dict[str, Any]:
    spec = fixture.get("input") or {}
    return {
        **fixture,
        "input": {
            "targetProfile": "Illustrious / ILXL",
            "idea": str(spec.get("benchmarkIdea") or (
                "雨上がりの夜の路地で、赤い傘を持つ成人女性。青いネオン、ローアングル。"
            )),
            "instruction": "画像生成用のpositive/negative promptへ構造化してください。",
        },
    }


def emit(value: dict[str, Any]) -> None:
    print(json.dumps(value, ensure_ascii=False, separators=(",", ":")))


def main() -> None:
    try:
        request = json.load(sys.stdin)
        profile = request.get("model") or {}
        fixture = request.get("fixture") or {}
        category = str(fixture.get("category") or "")

        model_path, resolved_revision, actual_hash = resolve_model(profile)

        if category == "model_size":
            emit({
                "status": "ok",
                "score": 1.0,
                "metrics": {"modelSizeMb": model_path.stat().st_size / (1024.0 * 1024.0)},
                "output": {
                    "modelBytes": model_path.stat().st_size,
                    "modelSha256": actual_hash,
                    "requestedRevision": str(profile.get("version") or ""),
                    "resolvedRevision": resolved_revision,
                    "profilePinned": bool(param(profile, "modelSha256"))
                    and str(profile.get("version") or "") not in {"", "main"},
                },
            })
            return

        server_exe = resolve_server(profile)
        threads = max(1, int(param(profile, "threads", "8")))
        context = max(2048, int(param(profile, "context", "8192")))
        extra_args = server_extra_args(profile)

        effective_fixture = (
            perf_fixture(fixture)
            if category in {"cpu_latency_tokens_sec", "ram", "cold_start", "windows_runtime_stability"}
            else fixture
        )

        def new_server() -> Server:
            return Server(server_exe, model_path, threads, context, extra_args)

        if category == "cold_start":
            runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 2))
            samples: list[float] = []
            for _ in range(runs):
                server = new_server()
                started = time.perf_counter()
                try:
                    server.start()
                    infer(server, effective_fixture, profile)
                    samples.append((time.perf_counter() - started) * 1000.0)
                finally:
                    server.stop()
            emit({
                "status": "ok",
                "score": 1.0,
                "metrics": {"coldStartMs": sum(samples) / len(samples)},
                "output": {"measureRuns": runs, "samplesMs": samples},
            })
            return

        if category == "windows_runtime_stability":
            cycles = max(1, int((fixture.get("input") or {}).get("cycles") or 10))
            success = 0
            errors: list[str] = []
            for _ in range(cycles):
                server = new_server()
                try:
                    server.start()
                    infer(server, effective_fixture, profile)
                    success += 1
                except Exception as exc:
                    errors.append(str(exc))
                finally:
                    server.stop()
            rate = success / cycles
            value: dict[str, Any] = {
                "status": "ok" if success else "error",
                "score": rate,
                "metrics": {"runtimeSuccessRate": rate},
                "output": {
                    "cycles": cycles,
                    "successfulCycles": success,
                    "errors": errors[:5],
                },
            }
            if not success:
                value["error"] = "all runtime stability cycles failed"
            emit(value)
            return

        server = new_server()
        server.start()
        try:
            if category == "ram":
                runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
                rss = [server.rss_mb()]
                latencies: list[float] = []
                for _ in range(runs):
                    _, latency, _ = infer(server, effective_fixture, profile)
                    latencies.append(latency)
                    rss.append(server.rss_mb())
                emit({
                    "status": "ok",
                    "score": 1.0,
                    "metrics": {
                        "ramMb": max(rss),
                        "latencyMs": sum(latencies) / len(latencies),
                    },
                    "output": {"measureRuns": runs, "rssSamplesMb": rss},
                })
                return

            if category == "cpu_latency_tokens_sec":
                spec = fixture.get("input") or {}
                warmups = max(0, int(spec.get("warmupRuns") or 1))
                runs = max(1, int(spec.get("measureRuns") or 3))
                for _ in range(warmups):
                    infer(server, effective_fixture, profile)
                latencies: list[float] = []
                tokens = 0
                for _ in range(runs):
                    _, latency, count = infer(server, effective_fixture, profile)
                    latencies.append(latency)
                    tokens += count
                total_seconds = sum(latencies) / 1000.0
                emit({
                    "status": "ok",
                    "score": 1.0,
                    "metrics": {
                        "latencyMs": sum(latencies) / len(latencies),
                        "tokensPerSecond": tokens / total_seconds if total_seconds > 0 else 0.0,
                    },
                    "output": {
                        "measureRuns": runs,
                        "latenciesMs": latencies,
                        "completionTokens": tokens,
                    },
                })
                return

            if category in QUALITY_CATEGORIES:
                output, latency, tokens = infer(server, fixture, profile)
                score, scoring = score_output(dict(fixture.get("expected") or {}), output)
                emit({
                    "status": "ok",
                    "score": score,
                    "metrics": {"latencyMs": latency},
                    "output": {
                        **output,
                        "scoring": scoring,
                        "generatedTokens": tokens,
                        "resolvedRevision": resolved_revision,
                        "modelSha256": actual_hash,
                    },
                })
                return
        finally:
            server.stop()

        emit({
            "status": "skipped",
            "score": 0.0,
            "notes": f"Prompt Engine adapter does not support category {category}",
        })
    except Exception as exc:
        fail(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
