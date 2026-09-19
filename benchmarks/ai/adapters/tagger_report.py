#!/usr/bin/env python3
"""Render two or more Lumine tagger benchmark result files as Markdown."""

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
    if value is None:
        return "—"
    return f"{float(value):.2f}{suffix}"


def nested_metric(case: dict[str, Any] | None, name: str) -> str:
    if not case or case.get("status") != "ok":
        return "—"
    value = ((case.get("output") or {}).get("referenceMetrics") or {}).get(name)
    return "—" if value is None else f"{float(value):.3f}"


def ensure_comparable(results: list[dict[str, Any]]) -> None:
    reference = results[0]
    ref_pack = reference.get("catalogPack")
    ref_evaluator = reference.get("evaluatorVersion")
    ref_hardware = (reference.get("environment") or {}).get("hardwareId")
    for index, result in enumerate(results[1:], start=2):
        if result.get("catalogPack") != ref_pack:
            raise SystemExit(f"catalogPack mismatch at input {index}")
        if result.get("evaluatorVersion") != ref_evaluator:
            raise SystemExit(f"evaluatorVersion mismatch at input {index}")
        if (result.get("environment") or {}).get("hardwareId") != ref_hardware:
            raise SystemExit(f"hardwareId mismatch at input {index}")


def row(
    label: str,
    fixture_id: str,
    cases: list[dict[str, dict[str, Any]]],
    *,
    nested: str | None = None,
    metric_name: str | None = None,
    suffix: str = "",
) -> list[str]:
    values: list[str] = []
    for candidate in cases:
        case = candidate.get(fixture_id)
        if nested is not None:
            values.append(nested_metric(case, nested))
        elif metric_name is not None:
            values.append(metric(case, metric_name, suffix))
        else:
            values.append(fmt_score(case))
    return [label, *values]


def main() -> None:
    if len(sys.argv) < 3:
        raise SystemExit("usage: tagger_report.py RESULT_A RESULT_B [RESULT_C ...]")

    results = [load(path) for path in sys.argv[1:]]
    ensure_comparable(results)
    candidates = [case_map(result) for result in results]
    names = [str((result.get("model") or {}).get("id", f"candidate {index + 1}")) for index, result in enumerate(results)]

    rows: list[list[str]] = [
        row("Danbooru basic score", "tagger-danbooru-basic-001", candidates),
        row("Danbooru basic F1", "tagger-danbooru-basic-001", candidates, nested="f1"),
        row("Fine-grained score", "tagger-danbooru-finegrained-001", candidates),
        row("Fine-grained F1", "tagger-danbooru-finegrained-001", candidates, nested="f1"),
        row("Known character top-1", "tagger-character-known-001", candidates),
        row("Recent character top-1", "tagger-character-recent-001", candidates),
        row("Adult/R18 detail score", "tagger-adult-explicit-001", candidates),
        row("Adult/R18 detail F1", "tagger-adult-explicit-001", candidates, nested="f1"),
        row("Rating accuracy", "tagger-rating-spectrum-001", candidates),
        row("CPU latency", "perf-cpu-001", candidates, metric_name="latencyMs", suffix=" ms"),
        row("RAM RSS", "perf-ram-001", candidates, metric_name="ramMb", suffix=" MB"),
        row("Cold start", "perf-cold-start-001", candidates, metric_name="coldStartMs", suffix=" ms"),
        row("Model size", "perf-model-size-001", candidates, metric_name="modelSizeMb", suffix=" MB"),
        row("Windows success rate", "windows-stability-001", candidates, metric_name="runtimeSuccessRate"),
    ]

    reference = results[0]
    print("# Tagger benchmark comparison")
    print()
    print(f"- catalog: `{reference.get('catalogPack')}`")
    print(f"- evaluator: `{reference.get('evaluatorVersion')}`")
    print(f"- hardware: `{(reference.get('environment') or {}).get('hardwareId', '')}`")
    print()
    print("| Measurement | " + " | ".join(names) + " |")
    print("| --- | " + " | ".join("---:" for _ in names) + " |")
    for values in rows:
        print("| " + " | ".join(values) + " |")
    print()
    print("This table is descriptive evidence only. Adoption must also record runtime/integration and license/redistribution considerations.")


if __name__ == "__main__":
    main()
