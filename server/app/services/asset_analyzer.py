"""
Модуль анализа ассетов.

Определяет характеристики загруженных ресурсов (размер, формат, разрешение,
количество полигонов и т.д.), проверяет соответствие требованиям целевой платформы
и формирует рекомендации по оптимизации.
"""

import hashlib
import mimetypes
from pathlib import Path

from PIL import Image

from app.core.config import settings

ALLOWED_IMAGE_TYPES = {"image/png", "image/jpeg", "image/jpg", "image/webp"}
ALLOWED_VIDEO_TYPES = {"video/mp4", "video/webm"}
ALLOWED_AUDIO_TYPES = {"audio/wav", "audio/mpeg", "audio/mp3"}
ALLOWED_3D_TYPES = {"model/gltf-binary", "model/gltf+json", "application/octet-stream"}
ALLOWED_3D_EXTENSIONS = {".glb", ".gltf", ".fbx", ".obj"}

MAX_IMAGE_SIZE = 50 * 1024 * 1024       # 50 MB
MAX_VIDEO_SIZE = 500 * 1024 * 1024      # 500 MB
MAX_AUDIO_SIZE = 100 * 1024 * 1024      # 100 MB
MAX_3D_SIZE = 200 * 1024 * 1024         # 200 MB
MAX_TEXT_SIZE = 10 * 1024 * 1024        # 10 MB


def compute_file_hash(file_path: str) -> str:
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


def detect_content_type(filename: str) -> str:
    mime, _ = mimetypes.guess_type(filename)
    return mime or "application/octet-stream"


def classify_asset(filename: str, content_type: str) -> str:
    ext = Path(filename).suffix.lower()
    if content_type in ALLOWED_IMAGE_TYPES:
        return "image"
    if content_type in ALLOWED_VIDEO_TYPES:
        return "video"
    if content_type in ALLOWED_AUDIO_TYPES:
        return "audio"
    if ext in ALLOWED_3D_EXTENSIONS:
        return "model_3d"
    if content_type.startswith("text/") or ext in {".txt", ".json", ".csv"}:
        return "text"
    return "unknown"


def validate_asset(filename: str, content_type: str, size_bytes: int) -> list[str]:
    """Validate asset and return list of errors (empty = valid)."""
    errors = []
    category = classify_asset(filename, content_type)

    limits = {
        "image": MAX_IMAGE_SIZE,
        "video": MAX_VIDEO_SIZE,
        "audio": MAX_AUDIO_SIZE,
        "model_3d": MAX_3D_SIZE,
        "text": MAX_TEXT_SIZE,
    }

    if category == "unknown":
        errors.append(f"Unsupported file type: {content_type} ({filename})")
        return errors

    max_size = limits.get(category, MAX_TEXT_SIZE)
    if size_bytes > max_size:
        errors.append(
            f"File too large: {size_bytes / 1024 / 1024:.1f} MB "
            f"(max {max_size / 1024 / 1024:.0f} MB for {category})"
        )

    return errors


def analyze_image(file_path: str) -> dict:
    """Extract image metadata."""
    try:
        with Image.open(file_path) as img:
            return {
                "width": img.width,
                "height": img.height,
                "format": img.format,
                "mode": img.mode,
                "megapixels": round(img.width * img.height / 1_000_000, 2),
            }
    except Exception:
        return {}


def analyze_3d_model(file_path: str) -> dict:
    """Extract basic 3D model metadata for glTF/GLB."""
    ext = Path(file_path).suffix.lower()
    if ext not in {".glb", ".gltf"}:
        return {"format": ext.lstrip(".")}

    try:
        from pygltflib import GLTF2
        gltf = GLTF2().load(file_path)

        total_vertices = 0
        for mesh in gltf.meshes:
            for primitive in mesh.primitives:
                if primitive.attributes.POSITION is not None:
                    accessor = gltf.accessors[primitive.attributes.POSITION]
                    total_vertices += accessor.count

        return {
            "format": "glTF",
            "meshes_count": len(gltf.meshes),
            "materials_count": len(gltf.materials) if gltf.materials else 0,
            "textures_count": len(gltf.textures) if gltf.textures else 0,
            "animations_count": len(gltf.animations) if gltf.animations else 0,
            "total_vertices": total_vertices,
            "estimated_polygons": total_vertices // 3,
        }
    except Exception:
        return {"format": ext.lstrip(".")}


def analyze_asset(file_path: str, filename: str, content_type: str) -> dict:
    """Full analysis of an uploaded asset."""
    category = classify_asset(filename, content_type)
    size_bytes = Path(file_path).stat().st_size
    file_hash = compute_file_hash(file_path)

    result = {
        "category": category,
        "size_bytes": size_bytes,
        "hash": file_hash,
        "content_type": content_type,
        "width": None,
        "height": None,
        "duration_seconds": None,
        "polygon_count": None,
        "metadata": {},
    }

    if category == "image":
        img_info = analyze_image(file_path)
        result["width"] = img_info.get("width")
        result["height"] = img_info.get("height")
        result["metadata"] = img_info

    elif category == "model_3d":
        model_info = analyze_3d_model(file_path)
        result["polygon_count"] = model_info.get("estimated_polygons")
        result["metadata"] = model_info

    return result


def check_compatibility(asset_metadata: dict, hardware_profile: dict) -> dict:
    """Check if asset is compatible with target hardware profile."""
    warnings = []
    recommendations = []

    memory_limit = hardware_profile.get("memory_limit_mb", 0)
    texture_memory = hardware_profile.get("texture_memory_mb", 0)

    if asset_metadata.get("category") == "image" and texture_memory:
        width = asset_metadata.get("width", 0)
        height = asset_metadata.get("height", 0)
        estimated_vram = (width * height * 4) / (1024 * 1024)  # RGBA
        if estimated_vram > texture_memory * 0.25:
            warnings.append(
                f"Image uses ~{estimated_vram:.0f} MB VRAM "
                f"({texture_memory * 0.25:.0f} MB budget per texture)"
            )
            recommendations.append("Consider reducing image resolution or using texture compression")

    if asset_metadata.get("category") == "model_3d":
        polygons = asset_metadata.get("polygon_count", 0)
        if polygons and polygons > 100_000:
            warnings.append(f"High polygon count: {polygons:,}")
            recommendations.append("Consider using LOD or reducing polygon count")

    size_mb = (asset_metadata.get("size_bytes", 0)) / (1024 * 1024)
    if memory_limit and size_mb > memory_limit * 0.1:
        warnings.append(f"Large file: {size_mb:.1f} MB (memory budget: {memory_limit} MB)")
        recommendations.append("Consider compressing the asset")

    return {
        "compatible": len(warnings) == 0,
        "warnings": warnings,
        "recommendations": recommendations,
    }
