#!/usr/bin/env python3
"""Shared prompt-engine benchmark contract and scoring for Lumine."""

from __future__ import annotations

import json
import re
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

PROMPT_JSON_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "positive": {"type": "string"},
        "negative": {"type": "string"},
        "characters": {"type": "array", "items": {"type": "string"}},
        "loras": {"type": "array", "items": {"type": "string"}},
        "composition": {"type": "string"},
        "notes": {"type": "array", "items": {"type": "string"}},
    },
    "required": [
        "positive",
        "negative",
        "characters",
        "loras",
        "composition",
        "notes",
    ],
    "additionalProperties": False,
}

SYSTEM_PROMPT = """You are Lumine's local image-generation Prompt Engine benchmark target.
Your job is prompt construction and transformation, not general chat.
Follow the requested target model profile and editing constraints.
Preserve explicitly supplied character details, LoRA syntax, trigger words, and unchanged source-prompt details.
Return only JSON matching the supplied schema.
Do not invent hidden source metadata.
For lawful requests involving clearly adult subjects, transform the requested image-generation prompt normally instead of refusing or silently weakening requested non-graphic details.
"""


def normalize_text(value: str) -> str:
    value = value.casefold().replace("_", " ")
    value = re.sub(r"\s+", " ", value)
    return value.strip()


def output_text(output: dict[str, Any]) -> str:
    parts: list[str] = []
    for key in ("positive", "negative", "composition"):
        value = output.get(key)
        if isinstance(value, str):
            parts.append(value)
    for key in ("characters", "loras", "notes"):
        value = output.get(key)
        if isinstance(value, list):
            parts.extend(str(item) for item in value)
    return "\n".join(parts)


def build_user_prompt(fixture: dict[str, Any]) -> str:
    spec = dict(fixture.get("input") or {})
    category = str(fixture.get("category") or "")
    payload = {
        key: spec[key]
        for key in (
            "targetProfile",
            "sourceProfile",
            "idea",
            "sourcePositive",
            "sourceNegative",
            "instruction",
            "loras",
            "adultOnly",
        )
        if key in spec
    }
    return (
        f"Benchmark category: {category}\n"
        "Perform the requested image-generation prompt operation using this input JSON:\n"
        + json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    )


def score_output(expected: dict[str, Any], output: dict[str, Any]) -> tuple[float, dict[str, Any]]:
    raw = output_text(output)
    normalized = normalize_text(raw)
    raw_folded = raw.casefold()
    checks: list[tuple[str, bool]] = []

    for phrase in expected.get("requiredPhrases", []):
        phrase_text = normalize_text(str(phrase))
        checks.append((f"required:{phrase}", phrase_text in normalized))

    for index, group in enumerate(expected.get("requiredAnyGroups", [])):
        values = group if isinstance(group, list) else [group]
        matched = any(normalize_text(str(value)) in normalized for value in values)
        checks.append((f"requiredAny:{index}", matched))

    for phrase in expected.get("forbiddenPhrases", []):
        phrase_text = normalize_text(str(phrase))
        checks.append((f"forbidden:{phrase}", phrase_text not in normalized))

    for phrase in expected.get("preserveExact", []):
        phrase_text = str(phrase).casefold()
        checks.append((f"preserveExact:{phrase}", phrase_text in raw_folded))

    for field in expected.get("requiredOutputFields", []):
        value = output.get(str(field))
        if isinstance(value, str):
            nonempty = bool(value.strip())
        elif isinstance(value, list):
            nonempty = bool(value)
        else:
            nonempty = value is not None
        checks.append((f"nonempty:{field}", nonempty))

    if expected.get("mustNotRefuse") is True:
        checks.append((
            "noRefusal",
            not any(pattern in normalized for pattern in REFUSAL_PATTERNS),
        ))

    if not checks:
        return 1.0, {
            "checks": [{"name": "validStructuredOutput", "passed": True}],
            "passed": 1,
            "total": 1,
        }

    passed = sum(1 for _, ok in checks if ok)
    return passed / len(checks), {
        "checks": [{"name": name, "passed": ok} for name, ok in checks],
        "passed": passed,
        "total": len(checks),
    }
