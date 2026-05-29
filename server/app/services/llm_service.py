"""
Модуль интеграции с LLM (Google Gemini).

Используется для:
- Автоматического маппинга ассетов к слотам пресетов
- Генерации метаданных для ассетов
- Предложений по параметрам конфигурации

Бесплатный тариф Gemini: 15 запросов/мин, 1M токенов/день.
"""

import json
import logging

from google import genai

from app.core.config import settings

logger = logging.getLogger(__name__)


def _get_client() -> genai.Client:
    return genai.Client(api_key=settings.GEMINI_API_KEY)


async def _generate_json(prompt: str) -> dict:
    """Send prompt to Gemini and parse JSON response."""
    client = _get_client()
    response = client.models.generate_content(
        model=settings.GEMINI_MODEL,
        contents=prompt,
        config=genai.types.GenerateContentConfig(
            temperature=0.2,
            response_mime_type="application/json",
        ),
    )

    content = response.text
    # Strip markdown code fences if present
    if content.startswith("```"):
        content = content.split("\n", 1)[1]
        content = content.rsplit("```", 1)[0]

    result = json.loads(content)

    tokens_used = 0
    if response.usage_metadata:
        tokens_used = (
            (response.usage_metadata.prompt_token_count or 0)
            + (response.usage_metadata.candidates_token_count or 0)
        )
    result["tokens_used"] = tokens_used
    return result


async def map_assets_to_preset(
    assets: list[dict],
    preset_schema: dict,
    preset_name: str,
) -> dict:
    """
    Use LLM to map uploaded assets to preset slots.

    Returns: {
        "mapping": {"slot_key": asset_id, ...},
        "confidence": float,
        "explanation": str,
        "tokens_used": int,
    }
    """
    slots = preset_schema.get("slots", {})

    prompt = f"""You are helping map user-uploaded assets to slots in a game mechanic preset.

Preset: "{preset_name}"
Available slots:
{json.dumps(slots, indent=2, ensure_ascii=False)}

Uploaded assets:
{json.dumps(assets, indent=2, ensure_ascii=False)}

For each slot, pick the most appropriate asset based on its type, name, and metadata.
Return JSON with:
- "mapping": dict of slot_key -> asset_id (int)
- "confidence": float 0-1
- "explanation": brief reasoning (in Russian)

Only return valid JSON, no markdown."""

    try:
        return await _generate_json(prompt)

    except Exception as e:
        logger.error("LLM map_assets_to_preset failed: %s", e)
        return {
            "mapping": {},
            "confidence": 0.0,
            "explanation": f"LLM error: {e}",
            "tokens_used": 0,
        }


async def generate_asset_metadata(
    assets: list[dict],
) -> dict:
    """
    Use LLM to generate descriptive metadata for assets.

    Returns: {
        "assets_metadata": {asset_id: {"tags": [...], "description": str, "suggested_role": str}},
        "tokens_used": int,
    }
    """
    prompt = f"""Analyze these uploaded assets and generate metadata for each.

Assets:
{json.dumps(assets, indent=2, ensure_ascii=False)}

For each asset (by id), return:
- "tags": list of descriptive tags (in Russian)
- "description": short description (in Russian)
- "suggested_role": one of [background, character, icon, button, audio_sfx, audio_music, model_main, model_decoration, text_content]

Return JSON with:
- "assets_metadata": dict of asset_id (as string) -> metadata object
Only return valid JSON, no markdown."""

    try:
        return await _generate_json(prompt)

    except Exception as e:
        logger.error("LLM generate_asset_metadata failed: %s", e)
        return {
            "assets_metadata": {},
            "tokens_used": 0,
        }


async def suggest_preset_params(
    preset_schema: dict,
    preset_name: str,
    assets_info: list[dict],
    hardware_profile: dict | None = None,
) -> dict:
    """
    Use LLM to suggest parameter values for a preset configuration.

    Returns: {
        "suggested_params": dict,
        "confidence": float,
        "explanation": str,
        "tokens_used": int,
    }
    """
    hw_info = json.dumps(hardware_profile, indent=2, ensure_ascii=False) if hardware_profile else "Not specified"

    prompt = f"""You are configuring a game mechanic preset for an interactive installation.

Preset: "{preset_name}"
Schema (parameters and their types):
{json.dumps(preset_schema, indent=2, ensure_ascii=False)}

Available assets:
{json.dumps(assets_info, indent=2, ensure_ascii=False)}

Hardware profile:
{hw_info}

Suggest optimal parameter values considering:
1. The available assets and their characteristics
2. Hardware limitations (if specified)
3. Best practices for interactive installations

Return JSON with:
- "suggested_params": dict matching the schema keys with suggested values
- "confidence": float 0-1
- "explanation": reasoning in Russian

Only return valid JSON, no markdown."""

    try:
        return await _generate_json(prompt)

    except Exception as e:
        logger.error("LLM suggest_preset_params failed: %s", e)
        return {
            "suggested_params": {},
            "confidence": 0.0,
            "explanation": f"LLM error: {e}",
            "tokens_used": 0,
        }
