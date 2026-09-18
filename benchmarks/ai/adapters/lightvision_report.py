#!/usr/bin/env python3
"""Render two Lumine lightweight-vision benchmark results as Markdown."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


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
    if len(sys.argv) != 3:
        raise SystemExit("usage: lightvision_report.py FLORENCE_RESULT SMOLVLM_RESULT")
    left = load(sys.argv[1])
    right = load(sys.argv[2])
    for field in ("catalogPack", "evaluatorVersion"):
        if left.get(field) != right.get(field):
            raise SystemExit(f"{field} mismatch")
    if (left.get("environment") or {}).get("hardwareId") != (right.get("environment") or {}).get("hardwareId"):
        raise SystemExit("hardwareId mismatch")

    a, b = cases(left), cases(right)
    left_name = str((left.get("model") or {}).get("id", "candidate A"))
    right_name = str((right.get("model") or {}).get("id", "candidate B"))
    rows: list[tuple[str, str, str]] = []

    for label, fixture_id in [
        ("Short caption", "lightvision-short-general-001"),
        ("Detailed composition", "lightvision-detailed-composition-001"),
        ("Anime illustration", "lightvision-anime-001"),
        ("Visible text / OCR", "lightvision-visible-text-001"),
        ("Adult-only robustness", "lightvision-adult-001"),
    ]:
        rows.append((label, score(a.get(fixture_id)), score(b.get(fixture_id))))

    for label, fixture_id, name, suffix in [
        ("CPU latency", "lightvision-perf-cpu-001", "latencyMs", " ms"),
        ("Tokens / sec", "lightvision-perf-cpu-001", "tokensPerSecond", ""),
        ("RAM RSS", "lightvision-perf-ram-001", "ramMb", " MB"),
        ("Cold start", "lightvision-perf-cold-start-001", "coldStartMs", " ms"),
        ("Model artifacts", "lightvision-perf-model-size-001", "modelSizeMb", " MB"),
        ("Windows success rate", "lightvision-windows-stability-001", "runtimeSuccessRate", ""),
    ]:
        rows.append((label, metric(a.get(fixture_id), name, suffix), metric(b.get(fixture_id), name, suffix)))

    print("# Lightweight Vision benchmark comparison")
    print()
    print(f"- catalog: `{left.get('catalogPack')}`")
    print(f"- evaluator: `{left.get('evaluatorVersion')}`")
    print(f"- hardware: `{(left.get('environment') or {}).get('hardwareId', '')}`")
    print()
    print(f"| Measurement | {left_name} | {right_name} |")
    print("| --- | ---: | ---: |")
    for label, av, bv in rows:
        print(f"| {label} | {av} | {bv} |")
    print()
    print("The table is descriptive evidence. Adoption must also consider license, runtime footprint, integration complexity, and feature coverage such as Florence OCR/grounding versus a general VLM.")


if __name__ == "__main__":
    main()
