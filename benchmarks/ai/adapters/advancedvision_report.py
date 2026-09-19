#!/usr/bin/env python3
"""Render 2+ Lumine Advanced Vision benchmark results as Markdown."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


QUALITY_ROWS = [
    ("Deep scene", "advanced-deep-scene-001"),
    ("Anime action", "advanced-anime-action-001"),
    ("Multi-image diff", "advanced-multi-diff-001"),
    ("Reverse-prompt support", "advanced-reverse-prompt-001"),
    ("Japanese instruction", "advanced-japanese-instruction-001"),
    ("OCR / context", "advanced-ocr-context-001"),
    ("Adult-only robustness", "advanced-adult-robustness-001"),
    ("Structured JSON", "advanced-structured-json-001"),
]

PERF_ROWS = [
    ("CPU latency", "advanced-perf-cpu-001", "latencyMs", " ms"),
    ("Tokens / sec", "advanced-perf-cpu-001", "tokensPerSecond", ""),
    ("RAM RSS", "advanced-perf-ram-001", "ramMb", " MB"),
    ("Cold start", "advanced-perf-cold-start-001", "coldStartMs", " ms"),
    ("Model + mmproj", "advanced-perf-model-size-001", "modelSizeMb", " MB"),
    ("Windows success rate", "advanced-windows-stability-001", "runtimeSuccessRate", ""),
]


def load(path: str) -> dict[str, Any]:
    with Path(path).open("r", encoding="utf-8") as handle:
        return json.load(handle)


def cases(result: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {str(item["fixtureId"]): item for item in result.get("cases", [])}


def score(case: dict[str, Any] | None) -> str:
    if not case:
        return "missing"
    if case.get("status") != "ok":
        return str(case.get("status"))
    return f'{float(case.get("score", 0)):.3f}'


def metric(case: dict[str, Any] | None, name: str, suffix: str = "") -> str:
    if not case or case.get("status") != "ok":
        return "—"
    value = (case.get("metrics") or {}).get(name)
    if value in (None, 0):
        return "—"
    return f"{float(value):.2f}{suffix}"


def main() -> None:
    if len(sys.argv) < 3:
        raise SystemExit("usage: advancedvision_report.py RESULT_A RESULT_B [RESULT_C ...]")

    results = [load(path) for path in sys.argv[1:]]
    first = results[0]
    for result in results[1:]:
        for field in ("catalogPack", "evaluatorVersion"):
            if result.get(field) != first.get(field):
                raise SystemExit(f"{field} mismatch")
        if (result.get("environment") or {}).get("hardwareId") != (first.get("environment") or {}).get("hardwareId"):
            raise SystemExit("hardwareId mismatch")

    names: list[str] = []
    unpinned: list[str] = []
    for index, result in enumerate(results):
        model = result.get("model") or {}
        name = str(model.get("id", f"candidate {index+1}"))
        version = str(model.get("version") or "")
        artifact_hash = str(model.get("artifactSha256") or "")
        if version in {"", "main"} or len(artifact_hash) != 64:
            name += " [UNPINNED]"
            unpinned.append(name)
        names.append(name)
    mapped = [cases(result) for result in results]

    print("# Advanced Vision benchmark comparison")
    print()
    print(f"- catalog: `{first.get('catalogPack')}`")
    print(f"- evaluator: `{first.get('evaluatorVersion')}`")
    print(f"- hardware: `{(first.get('environment') or {}).get('hardwareId', '')}`")
    print()
    print("| Measurement | " + " | ".join(names) + " |")
    print("| --- | " + " | ".join(["---:"] * len(names)) + " |")

    for label, fixture_id in QUALITY_ROWS:
        values = [score(case_map.get(fixture_id)) for case_map in mapped]
        print("| " + label + " | " + " | ".join(values) + " |")

    for label, fixture_id, name, suffix in PERF_ROWS:
        values = [metric(case_map.get(fixture_id), name, suffix) for case_map in mapped]
        print("| " + label + " | " + " | ".join(values) + " |")

    print()
    if unpinned:
        print("WARNING: unpinned candidate results are exploratory only and MUST NOT be used as adoption evidence: " + ", ".join(unpinned))
        print()
    print("This table is descriptive evidence only. Adoption must also record license, redistribution, integration complexity, quantization, and any quality change caused by Heretic/Abliterated modification.")


if __name__ == "__main__":
    main()
