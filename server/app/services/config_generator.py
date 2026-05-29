"""
Модуль генерации конфигурации проекта.

Формирует структуру будущего интерактивного приложения на основе:
- Выбранного пресета игровой механики
- Результатов анализа ассетов
- Маппинга ассетов к слотам (вручную или через LLM)
- Аппаратного профиля целевого стенда
"""

import json
import logging

logger = logging.getLogger(__name__)


def generate_project_config(
    preset_schema: dict,
    asset_mapping: dict[str, int],
    assets_info: dict[int, dict],
    params: dict,
    hardware_profile: dict | None = None,
) -> dict:
    """
    Generate a complete project configuration for Unity build.

    Args:
        preset_schema: Schema from preset (defines slots, parameters, behaviors)
        asset_mapping: {slot_key: asset_id}
        assets_info: {asset_id: {filename, content_type, stored_path, metadata, ...}}
        params: User-defined parameters for the mechanic
        hardware_profile: Target hardware constraints

    Returns:
        Full configuration dict ready for Unity build.
    """
    config = {
        "version": "1.0",
        "mechanic": {
            "type": preset_schema.get("type", "generic"),
            "name": preset_schema.get("name", "Unnamed"),
        },
        "assets": {},
        "parameters": {},
        "optimization": {},
        "ui": preset_schema.get("ui_template", {}),
    }

    # Map assets to slots
    slots = preset_schema.get("slots", {})
    for slot_key, slot_def in slots.items():
        asset_id = asset_mapping.get(slot_key)
        if asset_id and asset_id in assets_info:
            asset = assets_info[asset_id]
            config["assets"][slot_key] = {
                "asset_id": asset_id,
                "filename": asset.get("filename"),
                "content_type": asset.get("content_type"),
                "path": f"StreamingAssets/{asset.get('filename')}",
                "slot_type": slot_def.get("type", "any"),
                "metadata": asset.get("metadata", {}),
            }
        elif slot_def.get("required", False):
            logger.warning("Required slot '%s' has no asset assigned", slot_key)

    # Apply parameters (merge user params over defaults)
    default_params = preset_schema.get("default_params", {})
    merged_params = {**default_params, **params}
    config["parameters"] = merged_params

    # Apply hardware-aware optimizations
    if hardware_profile:
        config["optimization"] = _build_optimization_config(
            hardware_profile, assets_info, preset_schema
        )

    return config


def _build_optimization_config(
    hw: dict,
    assets_info: dict[int, dict],
    preset_schema: dict,
) -> dict:
    """Generate optimization settings based on hardware profile."""
    opt = {
        "target_fps": 30,
        "texture_compression": "auto",
        "lod_enabled": True,
        "lod_bias": 1.0,
        "shader_quality": "medium",
        "audio_compression": "vorbis",
        "max_texture_size": 2048,
    }

    memory = hw.get("memory_limit_mb", 0)
    texture_mem = hw.get("texture_memory_mb", 0)

    # Adjust for limited hardware
    if memory and memory < 4096:
        opt["shader_quality"] = "low"
        opt["max_texture_size"] = 1024
        opt["lod_bias"] = 2.0
        opt["target_fps"] = 30

    if texture_mem and texture_mem < 1024:
        opt["max_texture_size"] = 512
        opt["texture_compression"] = "etc2"

    if memory and memory >= 8192:
        opt["shader_quality"] = "high"
        opt["max_texture_size"] = 4096
        opt["target_fps"] = 60

    # Check total asset size vs memory budget
    total_asset_size = sum(a.get("size_bytes", 0) for a in assets_info.values())
    if memory and total_asset_size > memory * 1024 * 1024 * 0.5:
        opt["aggressive_compression"] = True
        opt["max_texture_size"] = min(opt["max_texture_size"], 1024)

    return opt


def validate_config(config: dict, preset_schema: dict) -> list[str]:
    """
    Validate a configuration against preset schema.
    Returns list of validation errors (empty = valid).
    """
    errors = []

    # Check required slots
    slots = preset_schema.get("slots", {})
    for slot_key, slot_def in slots.items():
        if slot_def.get("required", False) and slot_key not in config.get("assets", {}):
            errors.append(f"Missing required asset for slot: {slot_key}")

    # Check required parameters
    params_schema = preset_schema.get("parameters_schema", {})
    for param_key, param_def in params_schema.items():
        if param_def.get("required", False) and param_key not in config.get("parameters", {}):
            errors.append(f"Missing required parameter: {param_key}")

    return errors
