#!/usr/bin/env python3
"""Resolve immutable Hugging Face pins for Lumine Prompt Engine candidates.

This tool is intentionally metadata-only by default: it queries the official
Hugging Face model API with file metadata enabled, resolves an exact GGUF file,
and records the immutable repository revision, LFS SHA-256 and exact byte size.

With --apply, the checked-in profile is updated explicitly. No model weights are
downloaded and no profile is mutated without that flag.
"""

from __future__ import annotations

import argparse
import json
import re
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


def resolve_sibling(info: Any, requested_file: str, file_regex: str) -> Any:
    siblings = list(getattr(info, "siblings", None) or [])
    if requested_file:
        matches = [item for item in siblings if sibling_name(item) == requested_file]
        if len(matches) != 1:
            raise ValueError(
                f"modelFile {requested_file!r} was not found exactly once; "
                f"found {len(matches)} matches"
            )
        return matches[0]

    if not file_regex:
        raise ValueError("profile requires modelFile or modelFileRegex")
    matcher = re.compile(file_regex)
    matches = [
        item for item in siblings
        if sibling_name(item) and matcher.fullmatch(sibling_name(item))
    ]
    if len(matches) != 1:
        raise ValueError(
            f"modelFileRegex must match exactly one file; "
            f"matched {[sibling_name(item) for item in matches]}"
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
    requested_file = parameter(profile, "modelFile")
    file_regex = parameter(profile, "modelFileRegex")
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

    sibling = resolve_sibling(info, requested_file, file_regex)
    model_file = sibling_name(sibling)
    sha256, size = lfs_metadata(sibling)

    return {
        "profilePath": display_path(path),
        "repoId": repo_id,
        "requestedRevision": requested_revision,
        "resolvedRevision": resolved_revision,
        "modelFile": model_file,
        "modelSha256": sha256,
        "modelSizeBytes": size,
        "runtime": profile.get("runtime"),
        "llamaRelease": parameter(profile, "llamaRelease"),
        "runtimeArchiveSha256": parameter(profile, "llamaWindowsCpuArchiveSha256"),
        "alreadyPinned": (
            requested_revision == resolved_revision
            and str(profile.get("artifactSha256") or "").strip().lower() == sha256
            and int(profile.get("modelSizeBytes") or 0) == size
            and parameter(profile, "modelSha256").strip().lower() == sha256
            and int(parameter(profile, "modelSizeBytes", "0")) == size
            and requested_file == model_file
        ),
    }


def apply_pin(path: Path, pin: dict[str, Any]) -> None:
    profile = json.loads(path.read_text(encoding="utf-8"))
    params = dict(profile.get("parameters") or {})

    profile["version"] = pin["resolvedRevision"]
    profile["artifactSha256"] = pin["modelSha256"]
    profile["modelSizeBytes"] = pin["modelSizeBytes"]

    params["modelFile"] = pin["modelFile"]
    params.pop("modelFileRegex", None)
    params["modelSha256"] = pin["modelSha256"]
    params["modelSizeBytes"] = str(pin["modelSizeBytes"])
    params["pinStatus"] = "pinned-immutable-hf-revision-and-sha256"
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
        help="explicitly update the supplied profile JSON files with resolved pins",
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
            f"{pin['modelFile']} {pin['modelSizeBytes']} bytes "
            f"sha256={pin['modelSha256']}"
        )


if __name__ == "__main__":
    main()
