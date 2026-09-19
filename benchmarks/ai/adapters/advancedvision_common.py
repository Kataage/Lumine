#!/usr/bin/env python3
"""Shared contract and scoring for Lumine Advanced Vision benchmarks."""

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
    "その内容には対応",
)


ADVANCED_JSON_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "summary": {"type": "string"},
        "subjects": {"type": "array", "items": {"type": "string"}},
        "environment": {"type": "string"},
        "composition": {"type": "string"},
        "viewpoint": {"type": "string"},
        "actions": {"type": "array", "items": {"type": "string"}},
        "relationships": {"type": "array", "items": {"type": "string"}},
        "context": {"type": "string"},
        "differences": {"type": "array", "items": {"type": "string"}},
        "commonalities": {"type": "array", "items": {"type": "string"}},
        "reversePromptHints": {"type": "array", "items": {"type": "string"}},
        "visibleText": {"type": "array", "items": {"type": "string"}},
        "notes": {"type": "array", "items": {"type": "string"}},
    },
    "required": [
        "summary",
        "subjects",
        "environment",
        "composition",
        "viewpoint",
        "actions",
        "relationships",
        "context",
        "differences",
        "commonalities",
        "reversePromptHints",
        "visibleText",
        "notes",
    ],
    "additionalProperties": False,
}


def normalize_text(value: str) -> str:
    value = value.casefold().replace("_", " ")
    value = re.sub(r"[^\w\s\-]+", " ", value, flags=re.UNICODE)
    return " ".join(value.split())


def _flatten_strings(value: Any) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        result: list[str] = []
        for item in value:
            result.extend(_flatten_strings(item))
        return result
    if isinstance(value, dict):
        result: list[str] = []
        for item in value.values():
            result.extend(_flatten_strings(item))
        return result
    return []


def combined_text(output: dict[str, Any]) -> str:
    return normalize_text(" ".join(_flatten_strings(output)))


def group_matches(text: str, group: Any) -> bool:
    aliases = group if isinstance(group, list) else [group]
    return any(
        normalize_text(str(alias)) in text
        for alias in aliases
        if str(alias).strip()
    )


def ground_truth(fixture_dir: Path, fixture: dict[str, Any]) -> dict[str, Any]:
    expected = dict(fixture.get("expected") or {})
    for ref in fixture.get("references") or []:
        if ref.get("role") != "ground_truth":
            continue
        path = fixture_dir / str(ref["path"])
        with path.open("r", encoding="utf-8") as handle:
            extra = json.load(handle)
        if not isinstance(extra, dict):
            raise ValueError(f"ground truth {path} must contain a JSON object")
        expected.update(extra)
    return expected


def score_output(expected: dict[str, Any], output: dict[str, Any]) -> tuple[float, dict[str, Any]]:
    text = combined_text(output)
    checks: list[tuple[str, bool]] = []

    for index, group in enumerate(expected.get("requiredConceptGroups", [])):
        checks.append((f"required:{index}", group_matches(text, group)))

    for index, group in enumerate(expected.get("expectedDifferenceGroups", [])):
        differences = normalize_text(" ".join(_flatten_strings(output.get("differences", []))))
        checks.append((f"difference:{index}", group_matches(differences, group)))

    for index, group in enumerate(expected.get("expectedCommonalityGroups", [])):
        common = normalize_text(" ".join(_flatten_strings(output.get("commonalities", []))))
        checks.append((f"commonality:{index}", group_matches(common, group)))

    for index, group in enumerate(expected.get("expectedVisibleTextGroups", [])):
        visible = normalize_text(" ".join(_flatten_strings(output.get("visibleText", []))))
        checks.append((f"visibleText:{index}", group_matches(visible, group)))

    for concept in expected.get("forbiddenConcepts", []):
        normalized = normalize_text(str(concept))
        checks.append((f"forbidden:{concept}", normalized not in text))

    for field in expected.get("requiredNonEmptyFields", []):
        value = output.get(str(field))
        nonempty = bool(_flatten_strings(value)) and any(
            normalize_text(part) for part in _flatten_strings(value)
        )
        checks.append((f"nonempty:{field}", nonempty))

    if expected.get("mustNotRefuse") is True:
        checks.append((
            "noRefusal",
            not any(pattern in text for pattern in REFUSAL_PATTERNS),
        ))

    if not checks:
        # Parsing a schema-constrained response is itself the structured JSON
        # success criterion when no semantic assertions were provided.
        return 1.0, {"checks": [{"name": "validStructuredOutput", "passed": True}], "passed": 1, "total": 1}

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


def fixture_images(fixture_dir: Path, fixture: dict[str, Any]) -> list[tuple[str, Path]]:
    images: list[tuple[str, Path]] = []
    for ref in fixture.get("references") or []:
        role = str(ref.get("role") or "")
        if role == "image" or role.startswith("image_"):
            images.append((role or "image", fixture_dir / str(ref["path"])))
    if images:
        return images

    for index, value in enumerate((fixture.get("input") or {}).get("references") or []):
        images.append((f"image_{index + 1}", fixture_dir / str(value)))
    if not images:
        raise ValueError(f"fixture {fixture.get('id')} has no image reference")
    return images


def analysis_prompt(fixture: dict[str, Any]) -> str:
    spec = fixture.get("input") or {}
    instruction = str(spec.get("instruction") or "").strip()
    mode = str(spec.get("mode") or "deep")
    base = (
        "Analyze the supplied image or images for a local image-library and image-generation workflow. "
        "Use only visible evidence; do not invent hidden metadata or identities. "
        "Return only JSON matching the supplied schema. "
        "For clearly adult lawful artwork, analyze visible details neutrally instead of refusing. "
    )
    if mode == "compare":
        base += "Compare all supplied images and fill both differences and commonalities with concrete visual evidence. "
    elif mode == "reverse_prompt_support":
        base += "Focus on visual attributes useful to a later image-generation prompt while still separating observation from inference. "
    else:
        base += "Give a deep analysis of subjects, environment, composition, viewpoint, actions, relationships and context. "
    if instruction:
        base += "Follow this user instruction exactly: " + instruction
    return base
