#!/usr/bin/env python3
"""Render a side-by-side Markdown summary for Prompt Engine benchmark results."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

QUALITY = [
    "ja_to_prompt",
    "il_prompt",
    "model_profile_conversion",
    "structured_json",
    "partial_edit_constraints",
    "lora_trigger_preservation",
    "adult_content_robustness",
]


def load(path: str) -> dict[str, Any]:
    with Path(path).open("r", encoding="utf-8") as handle:
        return json.load(handle)


def avg_score(result: dict[str, Any], category: str) -> str:
    values = [
        float(case.get("score", 0))
        for case in result.get("cases", [])
        if case.get("category") == category and case.get("status") == "ok"
    ]
    return f"{sum(values) / len(values):.3f}" if values else "—"


def metric(result: dict[str, Any], category: str, key: str, suffix: str = "") -> str:
    for case in result.get("cases", []):
        if case.get("category") != category or case.get("status") != "ok":
            continue
        value = (case.get("metrics") or {}).get(key)
        if value is not None:
            return f"{float(value):.2f}{suffix}"
    return "—"


def main() -> None:
    if len(sys.argv) < 3:
        raise SystemExit("usage: prompt_report.py RESULT_A RESULT_B [RESULT_C ...]")

    results = [load(path) for path in sys.argv[1:]]
    names = [str((result.get("model") or {}).get("id") or Path(path).stem)
             for result, path in zip(results, sys.argv[1:])]

    print("| Metric | " + " | ".join(names) + " |")
    print("|---|" + "|".join("---:" for _ in names) + "|")
    for category in QUALITY:
        print("| " + category + " | " + " | ".join(avg_score(r, category) for r in results) + " |")
    rows = [
        ("CPU latency", "cpu_latency_tokens_sec", "latencyMs", " ms"),
        ("Tokens/sec", "cpu_latency_tokens_sec", "tokensPerSecond", ""),
        ("RAM", "ram", "ramMb", " MB"),
        ("Cold start", "cold_start", "coldStartMs", " ms"),
        ("Model size", "model_size", "modelSizeMb", " MB"),
        ("Runtime success", "windows_runtime_stability", "runtimeSuccessRate", ""),
    ]
    for label, category, key, suffix in rows:
        print("| " + label + " | " + " | ".join(
            metric(result, category, key, suffix) for result in results
        ) + " |")


if __name__ == "__main__":
    main()
