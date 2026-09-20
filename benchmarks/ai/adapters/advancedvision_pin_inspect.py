#!/usr/bin/env python3
"""Resolve immutable Hugging Face pins for Lumine Advanced Vision candidates.

This metadata-only tool resolves the exact repository revision plus model/mmproj
GGUF LFS SHA-256 values and byte sizes without downloading model weights.

With --apply, the supplied profile is updated explicitly. Normal inspection is
read-only.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from huggingface_hub import HfApi


def parameter(profile: dict[str, Any], name: str, default: str = "") -> str:
    return str((profile.get("parameters") or {}).get(name, default))


def display_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return path.name


def sibling_name(sibling: Any) -> str:
    return str(getattr(sibling, "rfilename", "") or "")


def available_files(info: Any) -> list[str]:
    names = [
        sibling_name(item)
        for item in list(getattr(info, "siblings", None) or [])
        if sibling_name(item)
    ]
    ggufs = [name for name in names if name.lower().endswith(".gguf")]
    return sorted(ggufs or names)


def resolve_exact_file(info: Any, requested_file: str, label: str) -> Any:
    if not requested_file:
        raise ValueError(f"{label} is required")
    siblings = list(getattr(info, "siblings", None) or [])
    matches = [item for item in siblings if sibling_name(item) == requested_file]
    if len(matches) != 1:
        raise ValueError(
            f"{label} {requested_file!r} was not found exactly once; "
            f"found {len(matches)} matches; available files: {available_files(info)}"
        )
    return matches[0]


def lfs_metadata(sibling: Any) -> tuple[str, int]:
    lfs = getattr(sibling, "lfs", None)
    sha256 = str(getattr(lfs, "sha256", "") or "").strip().lower() if lfs else ""
    size = int(
        (getattr(lfs, "size", 0) if lfs else 0)
        or getattr(sibling, "size", 0)
        or 0
    )
    if len(sha256) != 64:
        raise ValueError(
            f"{sibling_name(sibling)} does not expose a 64-character LFS SHA-256; "
            "refusing to invent an artifact hash"
        )
    if size <= 0:
        raise ValueError(
            f"{sibling_name(sibling)} does not expose a positive exact file size"
        )
    return sha256, size


def inspect_profile(api: HfApi, path: Path) -> dict[str, Any]:
    profile = json.loads(path.read_text(encoding="utf-8"))
    repo_id = parameter(profile, "repoId")
    requested_revision = str(profile.get("version") or "main").strip() or "main"
    model_file = parameter(profile, "modelFile")
    mmproj_file = parameter(profile, "mmprojFile")
    if not repo_id:
        raise ValueError(f"{path}: parameters.repoId is required")

    info = api.model_info(
        repo_id,
        revision=requested_revision,
        files_metadata=True,
    )
    resolved_revision = str(getattr(info, "sha", "") or "").strip()
    if len(resolved_revision) != 40:
        raise ValueError(
            f"{repo_id}@{requested_revision}: expected immutable 40-char revision, "
            f"got {resolved_revision!r}"
        )

    model_sibling = resolve_exact_file(info, model_file, "modelFile")
    mmproj_sibling = resolve_exact_file(info, mmproj_file, "mmprojFile")
    model_sha256, model_size = lfs_metadata(model_sibling)
    mmproj_sha256, mmproj_size = lfs_metadata(mmproj_sibling)
    total_size = model_size + mmproj_size

    return {
        "profilePath": display_path(path),
        "repoId": repo_id,
        "requestedRevision": requested_revision,
        "resolvedRevision": resolved_revision,
        "modelFile": sibling_name(model_sibling),
        "modelSha256": model_sha256,
        "modelSizeBytes": model_size,
        "mmprojFile": sibling_name(mmproj_sibling),
        "mmprojSha256": mmproj_sha256,
        "mmprojSizeBytes": mmproj_size,
        "totalSizeBytes": total_size,
        "runtime": profile.get("runtime"),
        "llamaRelease": parameter(profile, "llamaRelease"),
        "runtimeArchiveSha256": parameter(profile, "llamaWindowsCpuArchiveSha256"),
        "alreadyPinned": (
            requested_revision == resolved_revision
            and str(profile.get("artifactSha256") or "").strip().lower() == model_sha256
            and int(profile.get("modelSizeBytes") or 0) == total_size
            and parameter(profile, "modelSha256").strip().lower() == model_sha256
            and int(parameter(profile, "modelSizeBytes", "0")) == model_size
            and parameter(profile, "mmprojSha256").strip().lower() == mmproj_sha256
            and int(parameter(profile, "mmprojSizeBytes", "0")) == mmproj_size
            and model_file == sibling_name(model_sibling)
            and mmproj_file == sibling_name(mmproj_sibling)
        ),
    }


def apply_pin(path: Path, pin: dict[str, Any]) -> None:
    profile = json.loads(path.read_text(encoding="utf-8"))
    params = dict(profile.get("parameters") or {})

    profile["version"] = pin["resolvedRevision"]
    profile["artifactSha256"] = pin["modelSha256"]
    profile["modelSizeBytes"] = pin["totalSizeBytes"]

    params["modelFile"] = pin["modelFile"]
    params["modelSha256"] = pin["modelSha256"]
    params["modelSizeBytes"] = str(pin["modelSizeBytes"])
    params["mmprojFile"] = pin["mmprojFile"]
    params["mmprojSha256"] = pin["mmprojSha256"]
    params["mmprojSizeBytes"] = str(pin["mmprojSizeBytes"])
    params["pinStatus"] = "pinned-immutable-hf-revision-model-mmproj-sha256"
    profile["parameters"] = params

    path.write_text(
        json.dumps(profile, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("profiles", nargs="+", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument(
        "--apply",
        action="store_true",
        help="explicitly update supplied profiles with resolved immutable pins",
    )
    args = parser.parse_args()

    api = HfApi()
    pins: list[dict[str, Any]] = []
    for path in args.profiles:
        pin = inspect_profile(api, path)
        pins.append(pin)
        if args.apply:
            apply_pin(path, pin)

    result = {
        "schemaVersion": 1,
        "source": "huggingface-model-info-files-metadata",
        "applied": bool(args.apply),
        "candidates": pins,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"wrote {args.out}")
    for pin in pins:
        print(
            f"{pin['repoId']}@{pin['resolvedRevision']} "
            f"model={pin['modelFile']} {pin['modelSizeBytes']} bytes "
            f"sha256={pin['modelSha256']} "
            f"mmproj={pin['mmprojFile']} {pin['mmprojSizeBytes']} bytes "
            f"sha256={pin['mmprojSha256']}"
        )


if __name__ == "__main__":
    main()
