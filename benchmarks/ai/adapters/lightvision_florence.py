#!/usr/bin/env python3
"""Florence-2-base CPU adapter for Lumine lightweight-vision benchmarks."""

from __future__ import annotations

import gc
import hashlib
import json
import os
import sys
import time
from pathlib import Path
from typing import Any

from vision_common import ground_truth, image_reference, score_output


def fail(message: str) -> None:
    print(json.dumps({"status": "error", "score": 0.0, "error": message}, ensure_ascii=False))
    raise SystemExit(0)


try:
    import psutil
    import torch
    from huggingface_hub import snapshot_download
    from PIL import Image
    from transformers import AutoModelForMultimodalLM, AutoProcessor
except Exception as exc:
    fail(f"Florence adapter dependency error: {exc}")


def param(model: dict[str, Any], key: str, default: str = "") -> str:
    return str((model.get("parameters") or {}).get(key, default))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def prepare_snapshot(model: dict[str, Any]) -> Path:
    repo_id = param(model, "repoId")
    revision = str(model.get("version") or "")
    if not repo_id or not revision:
        raise ValueError("profile requires repoId and version")
    root = Path(snapshot_download(
        repo_id=repo_id,
        revision=revision,
        ignore_patterns=["pytorch_model.bin"],
    ))
    weights = root / "model.safetensors"
    expected_size = int(model.get("modelSizeBytes") or 0)
    expected_hash = str(model.get("artifactSha256") or "").lower()
    if expected_size and weights.stat().st_size != expected_size:
        raise ValueError(f"model size mismatch: {weights.stat().st_size} != {expected_size}")
    if expected_hash:
        stamp = weights.with_name(weights.name + f".lumine-{expected_hash[:16]}.sha256")
        cached = ""
        try:
            cached = stamp.read_text(encoding="utf-8").strip().lower()
        except OSError:
            pass
        if cached != expected_hash:
            actual = sha256(weights)
            if actual != expected_hash:
                raise ValueError(f"model sha256 mismatch: {actual} != {expected_hash}")
            try:
                stamp.write_text(expected_hash + "\n", encoding="utf-8")
            except OSError:
                pass
    return root


def load_runtime(root: Path, threads: int):
    torch.set_num_threads(max(1, threads))
    processor = AutoProcessor.from_pretrained(
        str(root), trust_remote_code=True, local_files_only=True
    )
    model = AutoModelForMultimodalLM.from_pretrained(
        str(root),
        trust_remote_code=True,
        local_files_only=True,
        torch_dtype=torch.float32,
    ).to("cpu")
    model.eval()
    return processor, model


def run_task(processor, model, image: Image.Image, task: str) -> tuple[Any, float, int]:
    inputs = processor(text=task, images=image, return_tensors="pt")
    started = time.perf_counter()
    with torch.inference_mode():
        generated_ids = model.generate(
            input_ids=inputs["input_ids"],
            pixel_values=inputs["pixel_values"],
            max_new_tokens=512,
            num_beams=3,
            do_sample=False,
        )
    latency_ms = (time.perf_counter() - started) * 1000.0
    generated_text = processor.batch_decode(generated_ids, skip_special_tokens=False)[0]
    parsed = processor.post_process_generation(
        generated_text,
        task=task,
        image_size=image.size,
    )
    value = parsed.get(task, parsed) if isinstance(parsed, dict) else parsed
    return value, latency_ms, int(generated_ids.shape[-1])


def to_text(value: Any) -> str:
    if isinstance(value, str):
        return value.strip()
    if isinstance(value, dict):
        # OCR / region tasks may return structured values.
        texts = value.get("labels") or value.get("text") or value.get("texts")
        if isinstance(texts, list):
            return " ".join(str(x) for x in texts)
        if texts:
            return str(texts)
    if isinstance(value, list):
        return " ".join(str(x) for x in value)
    return str(value or "")


def analyze(processor, model, image_path: Path, mode: str) -> tuple[dict[str, Any], float, int]:
    with Image.open(image_path) as source:
        image = source.convert("RGB")
        short, l1, t1 = run_task(processor, model, image, "<CAPTION>")
        detailed, l2, t2 = run_task(processor, model, image, "<MORE_DETAILED_CAPTION>")
        visible, l3, t3 = run_task(processor, model, image, "<OCR>")
    short_text = to_text(short)
    detailed_text = to_text(detailed)
    visible_text = to_text(visible)
    output = {
        "shortCaption": short_text,
        "detailedCaption": detailed_text,
        "subject": "",
        "background": "",
        "composition": "",
        "viewpoint": "",
        "visibleText": [visible_text] if visible_text else [],
    }
    if mode == "short":
        latency = l1
        tokens = t1
    else:
        latency = l1 + l2 + l3
        tokens = t1 + t2 + t3
    return output, latency, tokens


def rss_mb() -> float:
    return psutil.Process(os.getpid()).memory_info().rss / (1024.0 * 1024.0)


def unload(processor, model) -> None:
    del processor
    del model
    gc.collect()


def main() -> None:
    try:
        request = json.load(sys.stdin)
        model_profile = request.get("model") or {}
        fixture = request.get("fixture") or {}
        category = str(fixture.get("category") or "")
        fixture_dir = Path(str(request.get("fixtureDir") or ""))
        if not fixture_dir.is_dir():
            raise ValueError("fixtureDir does not exist")

        root = prepare_snapshot(model_profile)
        threads = max(1, int(param(model_profile, "threads", "8")))
        image_path = image_reference(fixture_dir, fixture)

        if category == "model_size":
            size = int(model_profile.get("modelSizeBytes") or (root / "model.safetensors").stat().st_size)
            print(json.dumps({
                "status": "ok", "score": 1.0,
                "metrics": {"modelSizeMb": size / (1024.0 * 1024.0)},
                "output": {"primaryModelBytes": size},
                "notes": "Metric records the primary safetensors weight artifact; tokenizer/processor code is reported separately by the profile/runtime."
            }, ensure_ascii=False))
            return

        if category == "cold_start":
            runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
            samples: list[float] = []
            for _ in range(runs):
                started = time.perf_counter()
                processor, runtime_model = load_runtime(root, threads)
                analyze(processor, runtime_model, image_path, "short")
                samples.append((time.perf_counter() - started) * 1000.0)
                unload(processor, runtime_model)
            print(json.dumps({
                "status": "ok", "score": 1.0,
                "metrics": {"coldStartMs": sum(samples) / len(samples)},
                "output": {"samplesMs": samples}
            }, ensure_ascii=False))
            return

        if category == "windows_runtime_stability":
            cycles = max(1, int((fixture.get("input") or {}).get("cycles") or 20))
            success = 0
            errors: list[str] = []
            for _ in range(cycles):
                try:
                    processor, runtime_model = load_runtime(root, threads)
                    analyze(processor, runtime_model, image_path, "short")
                    unload(processor, runtime_model)
                    success += 1
                except Exception as exc:
                    errors.append(str(exc))
            rate = success / cycles
            print(json.dumps({
                "status": "ok" if success else "error",
                "score": rate,
                "metrics": {"runtimeSuccessRate": rate},
                "output": {"cycles": cycles, "successfulCycles": success, "errors": errors[:5]},
                **({"error": "all stability cycles failed"} if not success else {})
            }, ensure_ascii=False))
            return

        processor, runtime_model = load_runtime(root, threads)
        try:
            if category == "ram":
                runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
                samples = [rss_mb()]
                latencies: list[float] = []
                for _ in range(runs):
                    _, latency, _ = analyze(processor, runtime_model, image_path, "short")
                    samples.append(rss_mb())
                    latencies.append(latency)
                print(json.dumps({
                    "status": "ok", "score": 1.0,
                    "metrics": {"ramMb": max(samples), "latencyMs": sum(latencies) / len(latencies)},
                    "output": {"rssSamplesMb": samples}
                }, ensure_ascii=False))
                return

            if category == "cpu_latency_tokens_sec":
                spec = fixture.get("input") or {}
                warmups = max(0, int(spec.get("warmupRuns") or 2))
                runs = max(1, int(spec.get("measureRuns") or 5))
                for _ in range(warmups):
                    analyze(processor, runtime_model, image_path, "short")
                latencies: list[float] = []
                tokens = 0
                for _ in range(runs):
                    _, latency, count = analyze(processor, runtime_model, image_path, "short")
                    latencies.append(latency)
                    tokens += count
                total_seconds = sum(latencies) / 1000.0
                print(json.dumps({
                    "status": "ok", "score": 1.0,
                    "metrics": {
                        "latencyMs": sum(latencies) / len(latencies),
                        "tokensPerSecond": tokens / total_seconds if total_seconds > 0 else 0.0
                    },
                    "output": {"measureRuns": runs, "latenciesMs": latencies}
                }, ensure_ascii=False))
                return

            if category == "lightweight_vision":
                mode = str((fixture.get("input") or {}).get("mode") or "detailed")
                output, latency, tokens = analyze(processor, runtime_model, image_path, mode)
                expected = ground_truth(fixture_dir, fixture)
                score, scoring = score_output(expected, output)
                print(json.dumps({
                    "status": "ok",
                    "score": score,
                    "metrics": {"latencyMs": latency},
                    "output": {**output, "scoring": scoring, "generatedTokens": tokens}
                }, ensure_ascii=False))
                return
        finally:
            unload(processor, runtime_model)

        print(json.dumps({
            "status": "skipped", "score": 0.0,
            "notes": f"Florence adapter does not support category {category}"
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
