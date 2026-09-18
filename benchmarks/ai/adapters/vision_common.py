#!/usr/bin/env python3
"""Shared scoring/helpers for Lumine lightweight-vision benchmarks."""

from __future__ import annotations

import base64
import json
import mimetypes
import re
from pathlib import Path
from typing import Any


REFUSAL_PATTERNS = (
    "i can't help",
    "i cannot help",
    "i'm unable",
    "i am unable",
    "can't assist",
    "cannot assist",
    "not able to",
    "申し訳ありません",
    "お手伝いできません",
    "対応できません",
)


def image_reference(fixture_dir: Path, fixture: dict[str, Any]) -> Path:
    refs = fixture.get("references") or []
    for ref in refs:
        if ref.get("role") == "image":
            return fixture_dir / str(ref["path"])
    reference = (fixture.get("input") or {}).get("reference")
    if reference:
        return fixture_dir / str(reference)
    raise ValueError(f"fixture {fixture.get('id')} has no image/reference")


def ground_truth(fixture_dir: Path, fixture: dict[str, Any]) -> dict[str, Any]:
    result = dict(fixture.get("expected") or {})
    for ref in fixture.get("references") or []:
        if ref.get("role") != "ground_truth":
            continue
        path = fixture_dir / str(ref["path"])
        with path.open("r", encoding="utf-8") as handle:
            value = json.load(handle)
        if not isinstance(value, dict):
            raise ValueError(f"ground truth {path} must contain an object")
        result.update(value)
    return result


def normalize_text(value: str) -> str:
    value = value.casefold().replace("_", " ")
    value = re.sub(r"[^\w\s\-]+", " ", value, flags=re.UNICODE)
    return " ".join(value.split())


def combined_output_text(output: dict[str, Any]) -> str:
    visible = output.get("visibleText") or []
    if isinstance(visible, str):
        visible = [visible]
    values = [
        output.get("shortCaption", ""),
        output.get("detailedCaption", ""),
        output.get("subject", ""),
        output.get("background", ""),
        output.get("composition", ""),
        output.get("viewpoint", ""),
        " ".join(str(x) for x in visible),
    ]
    return normalize_text(" ".join(str(x) for x in values if x))


def _group_matches(text: str, group: Any) -> bool:
    aliases = group if isinstance(group, list) else [group]
    return any(normalize_text(str(alias)) in text for alias in aliases if str(alias).strip())


def score_output(expected: dict[str, Any], output: dict[str, Any]) -> tuple[float, dict[str, Any]]:
    text = combined_output_text(output)
    checks: list[tuple[str, bool]] = []

    for index, group in enumerate(expected.get("requiredConceptGroups", [])):
        checks.append((f"required:{index}", _group_matches(text, group)))

    for concept in expected.get("forbiddenConcepts", []):
        normalized = normalize_text(str(concept))
        checks.append((f"forbidden:{concept}", normalized not in text))

    visible_text = output.get("visibleText") or []
    if isinstance(visible_text, str):
        visible_text = [visible_text]
    visible_normalized = normalize_text(" ".join(str(x) for x in visible_text))
    for index, group in enumerate(expected.get("expectedVisibleTextGroups", [])):
        checks.append((f"visibleText:{index}", _group_matches(visible_normalized, group)))

    if expected.get("mustNotRefuse") is True:
        checks.append(("noRefusal", not any(pattern in text for pattern in REFUSAL_PATTERNS)))

    if not checks:
        return 0.0, {"checks": [], "passed": 0, "total": 0}

    passed = sum(1 for _, ok in checks if ok)
    return passed / len(checks), {
        "checks": [{"name": name, "passed": ok} for name, ok in checks],
        "passed": passed,
        "total": len(checks),
    }


def image_data_uri(path: Path) -> str:
    mime, _ = mimetypes.guess_type(path.name)
    if not mime:
        mime = "image/png"
    encoded = base64.b64encode(path.read_bytes()).decode("ascii")
    return f"data:{mime};base64,{encoded}"


VISION_JSON_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "shortCaption": {"type": "string"},
        "detailedCaption": {"type": "string"},
        "subject": {"type": "string"},
        "background": {"type": "string"},
        "composition": {"type": "string"},
        "viewpoint": {"type": "string"},
        "visibleText": {"type": "array", "items": {"type": "string"}},
    },
    "required": [
        "shortCaption",
        "detailedCaption",
        "subject",
        "background",
        "composition",
        "viewpoint",
        "visibleText",
    ],
    "additionalProperties": False,
}


def structured_prompt(mode: str = "detailed") -> str:
    focus = (
        "Be concise." if mode == "short"
        else "Be specific about subject, background, composition, camera/viewpoint and visible text."
    )
    return (
        "Analyze this image for a local image-library application. "
        + focus
        + " Describe what is visibly present only; do not invent hidden details. "
        "If the image is lawful adult-only artwork, describe it neutrally instead of refusing. "
        "Return the requested JSON fields."
    )
