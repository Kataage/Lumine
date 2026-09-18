#!/usr/bin/env python3
"""Render two Lumine tagger benchmark result files as a Markdown comparison."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


def load(path: str) -> dict[str, Any]:
    with Path(path).open("r", encoding="utf-8") as handle:
        return json.load(handle)


def case_map(result: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {str(case["fixtureId"]): case for case in result.get("cases", [])}


def fmt_score(case: dict[str, Any] | None) -> str:
    if not case:
        return "missing"
    status = case.get("status", "")
    if status != "ok":
        return str(status)
    return f'{float(case.get("score", 0)):.3f}'


def metric(case: dict[str, Any] | None, name: str, suffix: str = "") -> str:
    if not case or case.get("status") != "ok":
        return "—"
    value = (case.get("metrics") or {}).get(name)
    if value in (None, 0):
        return "—"
    return f"{float(value):.2f}{suffix}"


def nested_metric(case: dict[str, Any] | None, name: str) -> str:
    if not case or case.get("status") != "ok":
        return "—"
    value = ((case.get("output") or {}).get("referenceMetrics") or {}).get(name)
    return "—" if value is None else f"{float(value):.3f}"


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit("usage: tagger_report.py WD_RESULT PIXAI_RESULT")

    left = load(sys.argv[1])
    right = load(sys.argv[2])
    if left.get("catalogPack") != right.get("catalogPack"):
        raise SystemExit("catalogPack mismatch")
    if left.get("evaluatorVersion") != right.get("evaluatorVersion"):
        raise SystemExit("evaluatorVersion mismatch")
    if (left.get("environment") or {}).get("hardwareId") != (right.get("environment") or {}).get("hardwareId"):
        raise SystemExit("hardwareId mismatch")

    a = case_map(left)
    b = case_map(right)
    left_name = str((left.get("model") or {}).get("id", "candidate A"))
    right_name = str((right.get("model") or {}).get("id", "candidate B"))

    rows: list[tuple[str, str, str]] = []
    quality_ids = [
        ("Danbooru basic score", "tagger-danbooru-basic-001"),
        ("Danbooru basic F1", "tagger-danbooru-basic-001"),
        ("Fine-grained score", "tagger-danbooru-finegrained-001"),
        ("Fine-grained F1", "tagger-danbooru-finegrained-001"),
        ("Known character top-1", "tagger-character-known-001"),
        ("Recent character top-1", "tagger-character-recent-001"),
        ("Adult/R18 detail score", "tagger-adult-explicit-001"),
        ("Adult/R18 detail F1", "tagger-adult-explicit-001"),
        ("Rating accuracy", "tagger-rating-spectrum-001"),
    ]
    for label, fixture_id in quality_ids:
        if label.endswith("F1"):
            rows.append((label, nested_metric(a.get(fixture_id), "f1"), nested_metric(b.get(fixture_id), "f1")))
        else:
            rows.append((label, fmt_score(a.get(fixture_id)), fmt_score(b.get(fixture_id))))

    perf = [
        ("CPU latency", "perf-cpu-001", "latencyMs", " ms"),
        ("RAM RSS", "perf-ram-001", "ramMb", " MB"),
        ("Cold start", "perf-cold-start-001", "coldStartMs", " ms"),
        ("Model size", "perf-model-size-001", "modelSizeMb", " MB"),
        ("Windows success rate", "windows-stability-001", "runtimeSuccessRate", ""),
    ]
    for label, fixture_id, name, suffix in perf:
        rows.append((label, metric(a.get(fixture_id), name, suffix), metric(b.get(fixture_id), name, suffix)))

    print("# Tagger benchmark comparison")
    print()
    print(f"- catalog: `{left.get('catalogPack')}`")
    print(f"- evaluator: `{left.get('evaluatorVersion')}`")
    print(f"- hardware: `{(left.get('environment') or {}).get('hardwareId', '')}`")
    print()
    print(f"| Measurement | {left_name} | {right_name} |")
    print("| --- | ---: | ---: |")
    for label, left_value, right_value in rows:
        print(f"| {label} | {left_value} | {right_value} |")
    print()
    print("This table is descriptive evidence only. Adoption must also record runtime/integration and license/redistribution considerations.")


if __name__ == "__main__":
    main()
