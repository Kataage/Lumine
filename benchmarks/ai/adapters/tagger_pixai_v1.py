#!/usr/bin/env python3
"""Lumine PixAI Tagger v1.0 reproducible CPU benchmark adapter.

This adapter intentionally uses the upstream Transformers custom pipeline instead
of pretending the newly released model has an official ONNX path. It therefore
measures the real integration cost Lumine would inherit unless/until a verified
ONNX export is adopted.

Reads one aibench.AdapterRequest from stdin and writes one AdapterResponse to
stdout. Diagnostics go to stderr.
"""

from __future__ import annotations

import gc
import hashlib
import json
import os
import sys
import time
from pathlib import Path
from typing import Any


def _fail(message: str) -> None:
    print(json.dumps({"status": "error", "score": 0.0, "error": message}, ensure_ascii=False))
    raise SystemExit(0)


try:
    import psutil
    from huggingface_hub import hf_hub_download
    from PIL import Image
    from transformers import pipeline
except Exception as exc:  # pragma: no cover - benchmark host
    _fail(f"PixAI v1.0 adapter dependency error: {exc}")


PERFORMANCE_CATEGORIES = {
    "cpu_latency_tokens_sec",
    "ram",
    "cold_start",
    "model_size",
    "windows_runtime_stability",
}

RATING_ALIASES = {
    "g": "general",
    "safe": "general",
    "general": "general",
    "s": "sensitive",
    "sensitive": "sensitive",
    "q": "questionable",
    "questionable": "questionable",
    "e": "explicit",
    "explicit": "explicit",
}


def _parameter(model: dict[str, Any], name: str, default: str = "") -> str:
    return str((model.get("parameters") or {}).get(name, default))


def _normalize_tag(value: str) -> str:
    return "_".join(value.strip().lower().replace("(", " ").replace(")", " ").split())


def _canonical_rating(value: str) -> str:
    normalized = _normalize_tag(value)
    normalized = normalized.removeprefix("rating:")
    normalized = normalized.removeprefix("rating_")
    return RATING_ALIASES.get(normalized, normalized)


def _verify_weight(model: dict[str, Any]) -> Path:
    repo_id = _parameter(model, "repoId")
    revision = str(model.get("version") or "")
    model_file = _parameter(model, "modelFile", "model.safetensors")
    if not repo_id or not revision:
        raise ValueError("PixAI v1.0 profile requires repoId and immutable version")
    path = Path(hf_hub_download(repo_id=repo_id, filename=model_file, revision=revision))
    expected_size = int(model.get("modelSizeBytes") or 0)
    if expected_size and path.stat().st_size != expected_size:
        raise ValueError(f"model size mismatch: got {path.stat().st_size}, want {expected_size}")
    expected_hash = str(model.get("artifactSha256") or "").strip().lower()
    if expected_hash:
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
                digest.update(chunk)
        actual = digest.hexdigest()
        if actual != expected_hash:
            raise ValueError(f"model sha256 mismatch: got {actual}, want {expected_hash}")
    return path


def _thresholds(model: dict[str, Any]) -> dict[str, float]:
    return {
        "general": float(_parameter(model, "generalThreshold", "0.17")),
        "character": float(_parameter(model, "characterThreshold", "0.27")),
        "style": float(_parameter(model, "styleThreshold", "0.15")),
        "copyright": float(_parameter(model, "copyrightThreshold", "0.24")),
        "meta": float(_parameter(model, "metaThreshold", "0.17")),
        "rating": float(_parameter(model, "ratingThreshold", "0.41")),
    }


def _new_tagger(model: dict[str, Any]):
    _verify_weight(model)
    repo_id = _parameter(model, "repoId")
    revision = str(model.get("version") or "")
    # Force CPU. The benchmark is specifically the user-visible CPU fallback
    # requirement from #165, not a server/GPU leaderboard reproduction.
    return pipeline(
        "image-classification",
        model=repo_id,
        image_processor=repo_id,
        revision=revision,
        trust_remote_code=True,
        device=-1,
    )


def _as_score_map(value: Any) -> dict[str, float]:
    if isinstance(value, dict):
        return {str(k): float(v) for k, v in value.items()}
    if isinstance(value, list):
        result: dict[str, float] = {}
        for item in value:
            if not isinstance(item, dict):
                continue
            label = item.get("label") or item.get("tag") or item.get("name")
            score = item.get("score")
            if label is not None and score is not None:
                result[str(label)] = float(score)
        return result
    return {}


def _predict(tagger, image_path: Path, model: dict[str, Any]) -> tuple[dict[str, dict[str, float]], float]:
    with Image.open(image_path) as source:
        image = source.convert("RGB")
        started = time.perf_counter()
        raw = tagger(image, threshold=_thresholds(model))
        latency_ms = (time.perf_counter() - started) * 1000.0

    if isinstance(raw, dict) and isinstance(raw.get("results"), dict):
        raw = raw["results"]
    if not isinstance(raw, dict):
        raise ValueError(f"unexpected PixAI v1.0 pipeline output type {type(raw).__name__}")

    categories: dict[str, dict[str, float]] = {}
    for category, values in raw.items():
        categories[str(category).lower()] = _as_score_map(values)
    return categories, latency_ms


def _flat_general(categories: dict[str, dict[str, float]]) -> list[tuple[str, float]]:
    # Lumine's existing tag benchmark treats descriptive/copyright/meta tags as
    # searchable general metadata. Style is also useful search metadata.
    merged: dict[str, float] = {}
    for category in ("general", "style", "copyright", "meta"):
        for name, score in categories.get(category, {}).items():
            merged[name] = max(score, merged.get(name, 0.0))
    return sorted(merged.items(), key=lambda item: item[1], reverse=True)


def _characters(categories: dict[str, dict[str, float]]) -> list[tuple[str, float]]:
    return sorted(categories.get("character", {}).items(), key=lambda item: item[1], reverse=True)


def _ratings(categories: dict[str, dict[str, float]]) -> list[tuple[str, float]]:
    return sorted(categories.get("rating", {}).items(), key=lambda item: item[1], reverse=True)


def _fixture_expected(fixture_dir: Path, fixture: dict[str, Any]) -> dict[str, Any]:
    expected = dict(fixture.get("expected") or {})
    for ref in fixture.get("references") or []:
        if ref.get("role") != "ground_truth":
            continue
        with (fixture_dir / str(ref["path"])).open("r", encoding="utf-8") as handle:
            value = json.load(handle)
        if not isinstance(value, dict):
            raise ValueError("ground-truth sidecar must contain a JSON object")
        expected.update(value)
    return expected


def _ref_path(fixture_dir: Path, fixture: dict[str, Any], role: str | None = None) -> Path:
    for ref in fixture.get("references") or []:
        if role is None or ref.get("role") == role:
            return fixture_dir / str(ref["path"])
    raise ValueError(f"fixture {fixture.get('id')} has no reference for role {role}")


def _constraint_score(expected: dict[str, Any], general: list[tuple[str, float]]) -> tuple[float, dict[str, float]]:
    actual = {_normalize_tag(name) for name, _ in general}
    required = [_normalize_tag(str(x)) for x in expected.get("requiredTags", [])]
    forbidden = [_normalize_tag(str(x)) for x in expected.get("forbiddenTags", [])]
    checks = [(tag, tag in actual) for tag in required] + [(tag, tag not in actual) for tag in forbidden]
    score = sum(1 for _, ok in checks if ok) / len(checks) if checks else 0.0

    reference = {_normalize_tag(str(x)) for x in expected.get("referenceTags", [])}
    metrics: dict[str, float] = {}
    if reference:
        true_positive = len(actual & reference)
        precision = true_positive / len(actual) if actual else 0.0
        recall = true_positive / len(reference)
        f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
        metrics = {"precision": precision, "recall": recall, "f1": f1}
    return score, metrics


def _quality_case(tagger, fixture_dir: Path, fixture: dict[str, Any], model: dict[str, Any]) -> dict[str, Any]:
    category = str(fixture["category"])
    if category == "rating_tagging":
        roles = list((fixture.get("expected") or {}).get("roles") or [])
        if not roles:
            raise ValueError("rating fixture has no expected roles")
        correct = 0
        outputs: list[dict[str, Any]] = []
        latencies: list[float] = []
        for role in roles:
            categories, latency = _predict(tagger, _ref_path(fixture_dir, fixture, str(role)), model)
            ratings = _ratings(categories)
            predicted = ratings[0][0] if ratings else ""
            correct += int(_canonical_rating(predicted) == _canonical_rating(str(role)))
            latencies.append(latency)
            outputs.append({"role": role, "predicted": predicted, "ratingScores": ratings})
        return {
            "status": "ok",
            "score": correct / len(roles),
            "metrics": {"latencyMs": sum(latencies) / len(latencies)},
            "output": {"images": outputs},
        }

    categories, latency = _predict(tagger, _ref_path(fixture_dir, fixture, "image"), model)
    expected = _fixture_expected(fixture_dir, fixture)
    general = _flat_general(categories)
    characters = _characters(categories)
    output = {
        "generalTags": [{"name": name, "score": score} for name, score in general],
        "characterTags": [{"name": name, "score": score} for name, score in characters],
        "ratingScores": [{"name": name, "score": score} for name, score in _ratings(categories)],
    }
    if category == "danbooru_tagging":
        score, metrics = _constraint_score(expected, general)
        if metrics:
            output["referenceMetrics"] = metrics
    elif category == "character_tagging":
        wanted = _normalize_tag(str(expected.get("expectedCharacter", "")))
        top = _normalize_tag(characters[0][0]) if characters else ""
        score = 1.0 if wanted and top == wanted else 0.0
        output["expectedCharacter"] = expected.get("expectedCharacter", "")
    else:
        raise ValueError(f"unsupported quality category {category}")
    return {"status": "ok", "score": score, "metrics": {"latencyMs": latency}, "output": output}


def _canonical_perf_image(fixture_dir: Path) -> Path:
    for candidate in (
        fixture_dir / "tagging" / "danbooru-basic-001.png",
        fixture_dir / "vision" / "lightweight-scene-001.png",
    ):
        if candidate.is_file():
            return candidate
    raise ValueError("performance fixture requires tagging/danbooru-basic-001.png")


def _rss_mb() -> float:
    return psutil.Process(os.getpid()).memory_info().rss / (1024.0 * 1024.0)


def _drop_tagger(tagger: Any) -> None:
    del tagger
    gc.collect()


def _performance_case(fixture_dir: Path, fixture: dict[str, Any], model: dict[str, Any]) -> dict[str, Any]:
    category = str(fixture["category"])
    image_path = _canonical_perf_image(fixture_dir)

    if category == "model_size":
        weight = _verify_weight(model)
        size = weight.stat().st_size
        return {"status": "ok", "score": 1.0, "metrics": {"modelSizeMb": size / (1024.0 * 1024.0)}, "output": {"bytes": size}}

    if category == "cold_start":
        runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
        samples: list[float] = []
        for _ in range(runs):
            started = time.perf_counter()
            tagger = _new_tagger(model)
            _predict(tagger, image_path, model)
            samples.append((time.perf_counter() - started) * 1000.0)
            _drop_tagger(tagger)
        mean_ms = sum(samples) / len(samples)
        return {"status": "ok", "score": 1.0, "metrics": {"coldStartMs": mean_ms}, "output": {"measureRuns": runs, "coldStartSamplesMs": samples}}

    if category == "windows_runtime_stability":
        cycles = max(1, int((fixture.get("input") or {}).get("cycles") or 20))
        success = 0
        errors: list[str] = []
        for _ in range(cycles):
            tagger = None
            try:
                tagger = _new_tagger(model)
                _predict(tagger, image_path, model)
                success += 1
            except Exception as exc:  # pragma: no cover - benchmark host
                errors.append(str(exc))
            finally:
                if tagger is not None:
                    _drop_tagger(tagger)
        rate = success / cycles
        return {
            "status": "ok" if success else "error",
            "score": rate,
            "metrics": {"runtimeSuccessRate": rate},
            "output": {"cycles": cycles, "successfulCycles": success, "errors": errors[:5]},
            **({"error": "all runtime stability cycles failed"} if not success else {}),
        }

    tagger = _new_tagger(model)
    try:
        _predict(tagger, image_path, model)  # warm-up
        if category == "ram":
            runs = max(1, int((fixture.get("input") or {}).get("measureRuns") or 3))
            samples = [_rss_mb()]
            latencies: list[float] = []
            for _ in range(runs):
                _, latency = _predict(tagger, image_path, model)
                latencies.append(latency)
                samples.append(_rss_mb())
            return {
                "status": "ok",
                "score": 1.0,
                "metrics": {"ramMb": max(samples), "latencyMs": sum(latencies) / len(latencies)},
                "output": {"measureRuns": runs, "rssSamplesMb": samples},
            }
        if category == "cpu_latency_tokens_sec":
            request = fixture.get("input") or {}
            warmups = max(0, int(request.get("warmupRuns") or 2))
            runs = max(1, int(request.get("measureRuns") or 5))
            for _ in range(warmups):
                _predict(tagger, image_path, model)
            latencies = [_predict(tagger, image_path, model)[1] for _ in range(runs)]
            mean_ms = sum(latencies) / len(latencies)
            return {
                "status": "ok",
                "score": 1.0,
                "metrics": {"latencyMs": mean_ms},
                "output": {"measureRuns": runs, "latenciesMs": latencies, "imagesPerSecond": 1000.0 / mean_ms if mean_ms else 0.0},
                "notes": "CPU benchmark uses the upstream Transformers custom pipeline; no unofficial ONNX conversion is assumed.",
            }
    finally:
        _drop_tagger(tagger)
    raise ValueError(f"unsupported performance category {category}")


def main() -> None:
    try:
        request = json.load(sys.stdin)
        model = request.get("model") or {}
        fixture = request.get("fixture") or {}
        fixture_dir = Path(str(request.get("fixtureDir") or ""))
        if not fixture_dir.is_dir():
            raise ValueError("fixtureDir does not exist")
        if _parameter(model, "family") != "pixai-v1.0":
            raise ValueError("this adapter only supports family=pixai-v1.0")

        category = str(fixture.get("category") or "")
        if category in PERFORMANCE_CATEGORIES:
            response = _performance_case(fixture_dir, fixture, model)
        elif category in {"danbooru_tagging", "character_tagging", "rating_tagging"}:
            tagger = _new_tagger(model)
            try:
                response = _quality_case(tagger, fixture_dir, fixture, model)
            finally:
                _drop_tagger(tagger)
        else:
            response = {"status": "skipped", "score": 0.0, "notes": f"PixAI v1.0 adapter does not support category {category}"}
        print(json.dumps(response, ensure_ascii=False, separators=(",", ":")))
    except Exception as exc:
        _fail(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
