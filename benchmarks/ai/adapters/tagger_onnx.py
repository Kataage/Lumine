#!/usr/bin/env python3
"""Lumine reproducible ONNX tagger benchmark adapter.

Reads one aibench.AdapterRequest from stdin and writes one AdapterResponse to
stdout. Diagnostics go to stderr so the Go harness can parse stdout strictly.
"""

from __future__ import annotations

import csv
import hashlib
import json
import math
import os
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


def _fail(message: str) -> None:
    print(json.dumps({"status": "error", "score": 0.0, "error": message}, ensure_ascii=False))
    raise SystemExit(0)


try:
    import numpy as np
    import onnxruntime as ort
    import psutil
    from huggingface_hub import hf_hub_download
    from PIL import Image
except Exception as exc:  # pragma: no cover - exercised on benchmark host
    _fail(f"tagger adapter dependency error: {exc}")


RATING_NAMES = {"general", "sensitive", "questionable", "explicit"}
PERFORMANCE_CATEGORIES = {
    "cpu_latency_tokens_sec",
    "ram",
    "cold_start",
    "model_size",
    "windows_runtime_stability",
}


@dataclass(frozen=True)
class TagRow:
    name: str
    category: int


@dataclass
class RuntimeConfig:
    family: str
    repo_id: str
    revision: str
    model_file: str
    tags_file: str
    model_path: Path
    tags_path: Path
    image_size: int
    general_threshold: float
    character_threshold: float
    threads: int
    inter_op_threads: int
    rating_supported: bool
    artifact_sha256: str
    model_size_bytes: int


def _parameter(model: dict[str, Any], name: str, default: str = "") -> str:
    value = (model.get("parameters") or {}).get(name, default)
    return str(value)


def _bool_parameter(model: dict[str, Any], name: str, default: bool = False) -> bool:
    raw = _parameter(model, name, "true" if default else "false").strip().lower()
    return raw in {"1", "true", "yes", "on"}


def _resolve_runtime(model: dict[str, Any]) -> RuntimeConfig:
    repo_id = _parameter(model, "repoId")
    revision = str(model.get("version") or "")
    model_file = _parameter(model, "modelFile", "model.onnx")
    tags_file = _parameter(model, "tagsFile")
    family = _parameter(model, "family")
    if not repo_id or not revision or not tags_file or not family:
        raise ValueError("model profile is missing repoId/version/tagsFile/family")

    model_path = Path(
        hf_hub_download(repo_id=repo_id, filename=model_file, revision=revision)
    )
    tags_path = Path(
        hf_hub_download(repo_id=repo_id, filename=tags_file, revision=revision)
    )
    expected_size = int(model.get("modelSizeBytes") or 0)
    if expected_size and model_path.stat().st_size != expected_size:
        raise ValueError(
            f"model size mismatch: got {model_path.stat().st_size}, want {expected_size}"
        )

    expected_hash = str(model.get("artifactSha256") or "").lower()
    _verify_hash_once(model_path, expected_hash)

    return RuntimeConfig(
        family=family,
        repo_id=repo_id,
        revision=revision,
        model_file=model_file,
        tags_file=tags_file,
        model_path=model_path,
        tags_path=tags_path,
        image_size=int(_parameter(model, "imageSize", "448")),
        general_threshold=float(_parameter(model, "generalThreshold", "0.35")),
        character_threshold=float(_parameter(model, "characterThreshold", "0.85")),
        threads=max(1, int(_parameter(model, "threads", "8"))),
        inter_op_threads=max(1, int(_parameter(model, "interOpThreads", "1"))),
        rating_supported=_bool_parameter(model, "ratingSupported"),
        artifact_sha256=expected_hash,
        model_size_bytes=expected_size,
    )


def _verify_hash_once(path: Path, expected: str) -> None:
    if not expected:
        return
    stamp = path.with_name(path.name + f".lumine-{expected[:16]}.sha256")
    try:
        if stamp.read_text(encoding="utf-8").strip().lower() == expected:
            return
    except OSError:
        pass

    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    actual = digest.hexdigest()
    if actual != expected:
        raise ValueError(f"model sha256 mismatch: got {actual}, want {expected}")
    try:
        stamp.write_text(expected + "\n", encoding="utf-8")
    except OSError:
        # Read-only HF caches are valid; the cost is only a re-hash next run.
        pass


def _load_tags(path: Path) -> list[TagRow]:
    rows: list[TagRow] = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        if not reader.fieldnames:
            raise ValueError("tag CSV has no header")
        for row in reader:
            name = (row.get("name") or row.get("tag") or row.get("tag_name") or "").strip()
            category_raw = row.get("category") or row.get("type") or "0"
            if not name:
                continue
            try:
                category = int(float(category_raw))
            except (TypeError, ValueError):
                category = 0
            rows.append(TagRow(name=name, category=category))
    if not rows:
        raise ValueError("tag CSV contains no tags")
    return rows


def _session_options(config: RuntimeConfig) -> ort.SessionOptions:
    options = ort.SessionOptions()
    options.intra_op_num_threads = config.threads
    options.inter_op_num_threads = config.inter_op_threads
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return options


def _new_session(config: RuntimeConfig) -> ort.InferenceSession:
    providers = ["CPUExecutionProvider"]
    return ort.InferenceSession(
        str(config.model_path),
        sess_options=_session_options(config),
        providers=providers,
    )


def _alpha_to_white(image: Image.Image) -> Image.Image:
    if image.mode in {"RGBA", "LA"} or "transparency" in image.info:
        rgba = image.convert("RGBA")
        background = Image.new("RGBA", rgba.size, "WHITE")
        background.alpha_composite(rgba)
        return background.convert("RGB")
    return image.convert("RGB")


def _preprocess_wd(path: Path, size: int) -> np.ndarray:
    with Image.open(path) as source:
        image = _alpha_to_white(source)
        width, height = image.size
        side = max(width, height)
        canvas = Image.new("RGB", (side, side), "WHITE")
        canvas.paste(image, ((side - width) // 2, (side - height) // 2))
        canvas = canvas.resize((size, size), Image.Resampling.BICUBIC)
        array = np.asarray(canvas, dtype=np.float32)
    # Official WD ONNX demo uses OpenCV BGR NHWC input.
    array = array[:, :, ::-1]
    return np.expand_dims(np.ascontiguousarray(array), axis=0)


def _preprocess_pixai(path: Path, size: int) -> np.ndarray:
    with Image.open(path) as source:
        image = _alpha_to_white(source)
        image = image.resize((size, size), Image.Resampling.BICUBIC)
        array = np.asarray(image, dtype=np.float32) / 255.0
    array = (array - 0.5) / 0.5
    array = np.transpose(array, (2, 0, 1))
    return np.expand_dims(np.ascontiguousarray(array), axis=0)


def _preprocess(path: Path, config: RuntimeConfig) -> np.ndarray:
    if config.family == "wd-v3":
        return _preprocess_wd(path, config.image_size)
    if config.family == "pixai-v0.9":
        return _preprocess_pixai(path, config.image_size)
    raise ValueError(f"unsupported tagger family {config.family!r}")


def _sigmoid(values: np.ndarray) -> np.ndarray:
    clipped = np.clip(values, -80.0, 80.0)
    return 1.0 / (1.0 + np.exp(-clipped))


def _infer(
    session: ort.InferenceSession,
    image_path: Path,
    config: RuntimeConfig,
) -> tuple[np.ndarray, float]:
    tensor = _preprocess(image_path, config)
    input_name = session.get_inputs()[0].name
    started = time.perf_counter()
    outputs = session.run(None, {input_name: tensor})
    latency_ms = (time.perf_counter() - started) * 1000.0
    if not outputs:
        raise ValueError("ONNX tagger returned no outputs")
    values = np.asarray(outputs[0], dtype=np.float32).reshape(-1)
    if config.family == "pixai-v0.9":
        values = _sigmoid(values)
    if not np.all(np.isfinite(values)):
        raise ValueError("ONNX tagger returned non-finite scores")
    return values, latency_ms


def _normalize_tag(value: str) -> str:
    return "_".join(value.strip().lower().replace("(", " ").replace(")", " ").split())


def _classify(
    probabilities: np.ndarray,
    tags: list[TagRow],
    config: RuntimeConfig,
) -> dict[str, Any]:
    if len(probabilities) != len(tags):
        raise ValueError(
            f"prediction/tag count mismatch: {len(probabilities)} != {len(tags)}"
        )

    general: list[tuple[str, float]] = []
    characters: list[tuple[str, float]] = []
    ratings: list[tuple[str, float]] = []
    for row, score_value in zip(tags, probabilities, strict=True):
        score = float(score_value)
        if row.category == 9 or _normalize_tag(row.name) in RATING_NAMES:
            ratings.append((row.name, score))
        elif row.category == 4:
            if score >= config.character_threshold:
                characters.append((row.name, score))
        elif score >= config.general_threshold:
            general.append((row.name, score))

    general.sort(key=lambda item: item[1], reverse=True)
    characters.sort(key=lambda item: item[1], reverse=True)
    ratings.sort(key=lambda item: item[1], reverse=True)
    return {
        "general": general,
        "characters": characters,
        "ratings": ratings,
        "rating": ratings[0][0] if ratings else "",
    }


def _ref_path(fixture_dir: Path, fixture: dict[str, Any], role: str | None = None) -> Path:
    references = fixture.get("references") or []
    for ref in references:
        if role is None or ref.get("role") == role:
            return fixture_dir / str(ref["path"])
    raise ValueError(
        f"fixture {fixture.get('id')} has no reference"
        + (f" for role {role}" if role else "")
    )


def _fixture_expected(fixture_dir: Path, fixture: dict[str, Any]) -> dict[str, Any]:
    expected = dict(fixture.get("expected") or {})
    for ref in fixture.get("references") or []:
        if ref.get("role") != "ground_truth":
            continue
        path = fixture_dir / str(ref["path"])
        with path.open("r", encoding="utf-8") as handle:
            ground_truth = json.load(handle)
        if not isinstance(ground_truth, dict):
            raise ValueError(f"ground truth {path} must contain a JSON object")
        expected.update(ground_truth)
    return expected


def _constraint_score(expected: dict[str, Any], predicted: dict[str, Any]) -> tuple[float, dict[str, float]]:
    actual = {_normalize_tag(name) for name, _ in predicted["general"]}
    required = [_normalize_tag(str(x)) for x in expected.get("requiredTags", [])]
    forbidden = [_normalize_tag(str(x)) for x in expected.get("forbiddenTags", [])]
    constraints = [(tag, tag in actual) for tag in required]
    constraints += [(tag, tag not in actual) for tag in forbidden]
    constraint_score = (
        sum(1 for _, ok in constraints if ok) / len(constraints)
        if constraints
        else 0.0
    )

    metrics: dict[str, float] = {}
    reference_tags = {
        _normalize_tag(str(x)) for x in expected.get("referenceTags", [])
    }
    if reference_tags:
        true_positive = len(actual & reference_tags)
        precision = true_positive / len(actual) if actual else 0.0
        recall = true_positive / len(reference_tags)
        f1 = (
            2.0 * precision * recall / (precision + recall)
            if precision + recall > 0
            else 0.0
        )
        metrics = {"precision": precision, "recall": recall, "f1": f1}
    return constraint_score, metrics


def _character_score(expected: dict[str, Any], predicted: dict[str, Any]) -> float:
    expected_character = _normalize_tag(str(expected.get("expectedCharacter", "")))
    if not expected_character:
        return 0.0
    if not predicted["characters"]:
        return 0.0
    top = _normalize_tag(predicted["characters"][0][0])
    return 1.0 if top == expected_character else 0.0


def _tag_output(predicted: dict[str, Any]) -> dict[str, Any]:
    return {
        "generalTags": [
            {"name": name, "score": round(score, 8)}
            for name, score in predicted["general"]
        ],
        "characterTags": [
            {"name": name, "score": round(score, 8)}
            for name, score in predicted["characters"]
        ],
        "ratingScores": [
            {"name": name, "score": round(score, 8)}
            for name, score in predicted["ratings"]
        ],
        "rating": predicted["rating"],
    }


def _run_quality_case(
    session: ort.InferenceSession,
    fixture_dir: Path,
    fixture: dict[str, Any],
    config: RuntimeConfig,
    tags: list[TagRow],
) -> dict[str, Any]:
    category = str(fixture["category"])
    if category == "rating_tagging":
        if not config.rating_supported:
            return {
                "status": "skipped",
                "score": 0.0,
                "notes": "candidate exposes no rating output category",
            }
        roles = list((fixture.get("expected") or {}).get("roles") or [])
        if not roles:
            raise ValueError("rating fixture has no expected roles")
        correct = 0
        outputs: list[dict[str, Any]] = []
        latencies: list[float] = []
        for role in roles:
            path = _ref_path(fixture_dir, fixture, str(role))
            probabilities, latency_ms = _infer(session, path, config)
            predicted = _classify(probabilities, tags, config)
            predicted_rating = _normalize_tag(predicted["rating"])
            expected_rating = _normalize_tag(str(role))
            correct += int(predicted_rating == expected_rating)
            latencies.append(latency_ms)
            outputs.append(
                {
                    "role": role,
                    "file": path.name,
                    "predicted": predicted["rating"],
                    "ratingScores": _tag_output(predicted)["ratingScores"],
                }
            )
        return {
            "status": "ok",
            "score": correct / len(roles),
            "metrics": {"latencyMs": sum(latencies) / len(latencies)},
            "output": {"images": outputs},
        }

    path = _ref_path(fixture_dir, fixture, "image")
    expected = _fixture_expected(fixture_dir, fixture)
    probabilities, latency_ms = _infer(session, path, config)
    predicted = _classify(probabilities, tags, config)
    output = _tag_output(predicted)
    if category == "danbooru_tagging":
        score, tag_metrics = _constraint_score(expected, predicted)
        if tag_metrics:
            output["referenceMetrics"] = {
                key: round(value, 8) for key, value in tag_metrics.items()
            }
    elif category == "character_tagging":
        score = _character_score(expected, predicted)
        output["expectedCharacter"] = expected.get("expectedCharacter", "")
    else:
        raise ValueError(f"unsupported tagger quality category {category}")
    return {
        "status": "ok",
        "score": score,
        "metrics": {"latencyMs": latency_ms},
        "output": output,
    }


def _canonical_perf_image(fixture_dir: Path) -> Path:
    candidates = [
        fixture_dir / "tagging" / "danbooru-basic-001.png",
        fixture_dir / "vision" / "lightweight-scene-001.png",
    ]
    for path in candidates:
        if path.is_file():
            return path
    raise ValueError("performance fixture requires tagging/danbooru-basic-001.png")


def _measure_rss_mb(process: psutil.Process) -> float:
    return float(process.memory_info().rss) / (1024.0 * 1024.0)


def _run_performance_case(
    fixture_dir: Path,
    fixture: dict[str, Any],
    config: RuntimeConfig,
    tags: list[TagRow],
) -> dict[str, Any]:
    category = str(fixture["category"])
    image_path = _canonical_perf_image(fixture_dir)
    process = psutil.Process(os.getpid())

    if category == "model_size":
        size = config.model_size_bytes or config.model_path.stat().st_size
        return {
            "status": "ok",
            "score": 1.0,
            "metrics": {"modelSizeMb": size / (1024.0 * 1024.0)},
            "output": {"bytes": size},
        }

    if category == "cold_start":
        started = time.perf_counter()
        session = _new_session(config)
        try:
            probabilities, _ = _infer(session, image_path, config)
            _classify(probabilities, tags, config)
        finally:
            del session
        cold_ms = (time.perf_counter() - started) * 1000.0
        return {
            "status": "ok",
            "score": 1.0,
            "metrics": {"coldStartMs": cold_ms},
        }

    if category == "windows_runtime_stability":
        cycles = max(1, int((fixture.get("input") or {}).get("cycles") or 20))
        success = 0
        errors: list[str] = []
        for _ in range(cycles):
            try:
                session = _new_session(config)
                try:
                    probabilities, _ = _infer(session, image_path, config)
                    _classify(probabilities, tags, config)
                finally:
                    del session
                success += 1
            except Exception as exc:  # pragma: no cover - benchmark host
                errors.append(str(exc))
        rate = success / cycles
        return {
            "status": "ok" if success else "error",
            "score": rate,
            "metrics": {"runtimeSuccessRate": rate},
            "output": {"cycles": cycles, "successfulCycles": success, "errors": errors[:5]},
            **({"error": "all runtime stability cycles failed"} if not success else {}),
        }

    session = _new_session(config)
    try:
        # One untimed warm-up gives ORT graph/runtime caches a fair chance.
        probabilities, _ = _infer(session, image_path, config)
        _classify(probabilities, tags, config)

        if category == "ram":
            before = _measure_rss_mb(process)
            probabilities, latency_ms = _infer(session, image_path, config)
            _classify(probabilities, tags, config)
            after = _measure_rss_mb(process)
            return {
                "status": "ok",
                "score": 1.0,
                "metrics": {"ramMb": max(before, after), "latencyMs": latency_ms},
                "output": {"rssBeforeMb": before, "rssAfterMb": after},
                "notes": "RAM is process RSS after model load and inference; OS/runtime allocator behavior may retain memory.",
            }

        if category == "cpu_latency_tokens_sec":
            request = fixture.get("input") or {}
            warmups = max(0, int(request.get("warmupRuns") or 2))
            runs = max(1, int(request.get("measureRuns") or 5))
            for _ in range(warmups):
                _infer(session, image_path, config)
            latencies: list[float] = []
            for _ in range(runs):
                _, latency_ms = _infer(session, image_path, config)
                latencies.append(latency_ms)
            mean_ms = sum(latencies) / len(latencies)
            return {
                "status": "ok",
                "score": 1.0,
                "metrics": {"latencyMs": mean_ms},
                "output": {
                    "measureRuns": runs,
                    "latenciesMs": [round(value, 4) for value in latencies],
                    "imagesPerSecond": 1000.0 / mean_ms if mean_ms > 0 else 0.0,
                },
                "notes": "tokensPerSecond is not applicable to image taggers; imagesPerSecond is recorded in output.",
            }
    finally:
        del session

    raise ValueError(f"unsupported performance category {category}")


def main() -> None:
    try:
        request = json.load(sys.stdin)
        model = request.get("model") or {}
        fixture = request.get("fixture") or {}
        fixture_dir = Path(str(request.get("fixtureDir") or ""))
        if not fixture_dir.is_dir():
            raise ValueError("fixtureDir does not exist")

        config = _resolve_runtime(model)
        tags = _load_tags(config.tags_path)
        category = str(fixture.get("category") or "")

        if category in PERFORMANCE_CATEGORIES:
            response = _run_performance_case(fixture_dir, fixture, config, tags)
        elif category in {"danbooru_tagging", "character_tagging", "rating_tagging"}:
            session = _new_session(config)
            try:
                response = _run_quality_case(session, fixture_dir, fixture, config, tags)
            finally:
                del session
        else:
            response = {
                "status": "skipped",
                "score": 0.0,
                "notes": f"tagger adapter does not support category {category}",
            }
        print(json.dumps(response, ensure_ascii=False, separators=(",", ":")))
    except Exception as exc:
        _fail(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
