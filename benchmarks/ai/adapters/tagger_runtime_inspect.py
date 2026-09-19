#!/usr/bin/env python3
"""Inspect pinned Tagger candidates for Lumine product-runtime integration.

The benchmark tells us which model should win. This inspector records the
remaining mechanical details needed by the native tagger-onnx engine:
immutable artifact hashes/sizes plus actual ONNX input/output names and shapes.

It deliberately does not convert non-ONNX models or guess tensor names.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
from typing import Any

import onnxruntime as ort
from huggingface_hub import hf_hub_download


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parameter(profile: dict[str, Any], name: str, default: str = "") -> str:
    return str((profile.get("parameters") or {}).get(name, default))


def immutable_url(repo_id: str, revision: str, filename: str) -> str:
    return f"https://huggingface.co/{repo_id}/resolve/{revision}/{filename}"


def file_info(repo_id: str, revision: str, filename: str) -> dict[str, Any]:
    path = Path(hf_hub_download(repo_id=repo_id, filename=filename, revision=revision))
    return {
        "path": filename,
        "url": immutable_url(repo_id, revision, filename),
        "sizeBytes": path.stat().st_size,
        "sha256": sha256_file(path),
        "cachePath": str(path),
    }


def json_map(value: Any) -> dict[str, Any] | None:
    return value if isinstance(value, dict) else None


def count_camie_tags(path: Path) -> int:
    root = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(root, dict):
        raise ValueError("Camie metadata must contain an object")

    dataset_info = json_map(root.get("dataset_info")) or {}
    tag_mapping = json_map(dataset_info.get("tag_mapping"))
    if tag_mapping is None:
        tag_mapping = json_map(root.get("tag_mapping"))
    if tag_mapping is None:
        tag_mapping = root

    idx_to_tag = json_map(tag_mapping.get("idx_to_tag"))
    if idx_to_tag is None:
        idx_to_tag = json_map(root.get("idx_to_tag"))
    if not idx_to_tag:
        raise ValueError("Camie metadata is missing idx_to_tag")

    indices = sorted(int(key) for key in idx_to_tag)
    if indices != list(range(len(indices))):
        raise ValueError("Camie output indices are not contiguous")
    return len(indices)


def count_csv_tags(path: Path) -> int:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        if not reader.fieldnames:
            raise ValueError("tag CSV has no header")
        count = 0
        for row in reader:
            name = (row.get("name") or row.get("tag") or row.get("tag_name") or "").strip()
            if name:
                count += 1
    if count <= 0:
        raise ValueError("tag CSV contains no tags")
    return count


def normalize_shape(shape: list[Any]) -> list[Any]:
    result: list[Any] = []
    for value in shape:
        if isinstance(value, (int, str)) or value is None:
            result.append(value)
        else:
            result.append(str(value))
    return result


def display_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return path.name


def inspect_profile(path: Path) -> dict[str, Any]:
    profile = json.loads(path.read_text(encoding="utf-8"))
    repo_id = parameter(profile, "repoId")
    revision = str(profile.get("version") or "")
    family = parameter(profile, "family")
    engine = str(profile.get("engine") or "")
    model_file = parameter(profile, "modelFile")
    tags_file = parameter(profile, "tagsFile")
    categories_file = parameter(profile, "categoriesFile")

    base: dict[str, Any] = {
        "profilePath": display_path(path),
        "id": profile.get("id"),
        "version": revision,
        "benchmarkEngine": engine,
        "family": family,
        "nativeOnnxEligible": engine == "python-onnxruntime-cpu",
    }

    if engine != "python-onnxruntime-cpu":
        base["reason"] = (
            "candidate does not publish the benchmarked artifact as ONNX; "
            "native tagger-onnx integration requires a separately verified export/runtime path"
        )
        base["benchmarkArtifact"] = {
            "path": model_file,
            "sizeBytes": profile.get("modelSizeBytes"),
            "sha256": profile.get("artifactSha256"),
        }
        return base

    if not repo_id or not revision or not model_file or not tags_file:
        raise ValueError(f"{path}: ONNX candidate profile is missing repoId/version/modelFile/tagsFile")

    model = file_info(repo_id, revision, model_file)
    tags = file_info(repo_id, revision, tags_file)
    categories = (
        file_info(repo_id, revision, categories_file)
        if categories_file
        else None
    )

    expected_size = int(profile.get("modelSizeBytes") or 0)
    expected_hash = str(profile.get("artifactSha256") or "").lower()
    if expected_size and model["sizeBytes"] != expected_size:
        raise ValueError(
            f"{path}: model size mismatch {model['sizeBytes']} != {expected_size}"
        )
    if expected_hash and model["sha256"].lower() != expected_hash:
        raise ValueError(
            f"{path}: model SHA-256 mismatch {model['sha256']} != {expected_hash}"
        )

    tags_path = Path(tags["cachePath"])
    tag_count = (
        count_camie_tags(tags_path)
        if family == "camie-v2"
        else count_csv_tags(tags_path)
    )

    session = ort.InferenceSession(
        model["cachePath"],
        providers=["CPUExecutionProvider"],
    )
    inputs = [
        {
            "name": value.name,
            "shape": normalize_shape(list(value.shape)),
            "type": value.type,
        }
        for value in session.get_inputs()
    ]
    outputs = [
        {
            "name": value.name,
            "shape": normalize_shape(list(value.shape)),
            "type": value.type,
        }
        for value in session.get_outputs()
    ]
    if len(inputs) != 1:
        raise ValueError(f"{path}: expected exactly one image input, got {len(inputs)}")
    if not outputs:
        raise ValueError(f"{path}: model exposes no outputs")

    selected_output_index = 1 if family == "camie-v2" and len(outputs) > 1 else 0
    selected_output = outputs[selected_output_index]
    selected_shape = selected_output["shape"]
    if selected_shape and isinstance(selected_shape[-1], int):
        if selected_shape[-1] != tag_count:
            raise ValueError(
                f"{path}: selected output width {selected_shape[-1]} != tag count {tag_count}"
            )

    activation = "probabilities" if family == "wd-v3" else "sigmoid"
    product_parameters = {
        "family": family,
        "image_size": parameter(profile, "imageSize"),
        "input_name": inputs[0]["name"],
        "output_name": selected_output["name"],
        "general_threshold": parameter(profile, "generalThreshold", "0"),
        "character_threshold": parameter(profile, "characterThreshold", "0"),
        "rating_threshold": parameter(profile, "ratingThreshold", "0"),
        "rating_supported": parameter(profile, "ratingSupported", "true"),
        "activation": activation,
    }

    model.pop("cachePath", None)
    tags.pop("cachePath", None)
    if categories is not None:
        categories.pop("cachePath", None)

    base.update(
        {
            "tagCount": tag_count,
            "inputs": inputs,
            "outputs": outputs,
            "selectedOutputIndex": selected_output_index,
            "productParameters": product_parameters,
            "productFiles": {
                "model": {**model, "role": "tagger-model"},
                "tags": {**tags, "role": "tagger-tags"},
                **(
                    {"categories": {**categories, "role": "tagger-categories"}}
                    if categories is not None
                    else {}
                ),
            },
        }
    )
    return base


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("profiles", nargs="+", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    result = {
        "schemaVersion": 1,
        "onnxRuntimeInspectionVersion": ort.__version__,
        "candidates": [inspect_profile(path) for path in args.profiles],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
