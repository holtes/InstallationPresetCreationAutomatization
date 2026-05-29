from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import (
    Asset,
    HardwareProfile,
    LLMRequest,
    Preset,
    Project,
    ProjectAsset,
    User,
)
from app.schemas.schemas import (
    LLMGenerateMetadataRequest,
    LLMMapAssetsRequest,
    LLMRequestOut,
    LLMSuggestParamsRequest,
)
from app.services.llm_service import (
    generate_asset_metadata,
    map_assets_to_preset,
    suggest_preset_params,
)

router = APIRouter(prefix="/llm", tags=["llm"])


async def _get_project_assets(project_id: int, db: AsyncSession) -> list[dict]:
    result = await db.execute(
        select(ProjectAsset, Asset)
        .join(Asset, ProjectAsset.asset_id == Asset.id)
        .where(ProjectAsset.project_id == project_id)
    )
    assets = []
    for pa, asset in result.all():
        assets.append({
            "id": asset.id,
            "filename": asset.filename,
            "content_type": asset.content_type,
            "size_bytes": asset.size_bytes,
            "width": asset.width,
            "height": asset.height,
            "polygon_count": asset.polygon_count,
            "metadata": asset.metadata_json or {},
            "role_in_project": pa.role_in_project,
            "slot_key": pa.slot_key,
        })
    return assets


async def _check_access(project_id: int, user: User, db: AsyncSession) -> Project:
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    return project


@router.post("/map-assets", response_model=LLMRequestOut)
async def llm_map_assets(
    data: LLMMapAssetsRequest,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Use LLM to automatically map project assets to preset slots."""
    await _check_access(data.project_id, user, db)

    preset = await db.get(Preset, data.preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")

    assets = await _get_project_assets(data.project_id, db)
    if not assets:
        raise HTTPException(status_code=400, detail="No assets in project")

    result = await map_assets_to_preset(assets, preset.schema or {}, preset.name)

    llm_req = LLMRequest(
        project_id=data.project_id,
        request_type="map_assets",
        request_payload={"preset_id": data.preset_id, "asset_count": len(assets)},
        response_payload=result,
        confidence=result.get("confidence"),
        tokens_used=result.get("tokens_used"),
    )
    db.add(llm_req)
    await db.commit()
    await db.refresh(llm_req)
    return llm_req


@router.post("/generate-metadata", response_model=LLMRequestOut)
async def llm_generate_metadata(
    data: LLMGenerateMetadataRequest,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Use LLM to generate metadata for project assets."""
    await _check_access(data.project_id, user, db)

    assets = await _get_project_assets(data.project_id, db)
    if not assets:
        raise HTTPException(status_code=400, detail="No assets in project")

    result = await generate_asset_metadata(assets)

    llm_req = LLMRequest(
        project_id=data.project_id,
        request_type="generate_metadata",
        request_payload={"asset_count": len(assets)},
        response_payload=result,
        tokens_used=result.get("tokens_used"),
    )
    db.add(llm_req)
    await db.commit()
    await db.refresh(llm_req)
    return llm_req


@router.post("/suggest-params", response_model=LLMRequestOut)
async def llm_suggest_params(
    data: LLMSuggestParamsRequest,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Use LLM to suggest preset parameter values."""
    project = await _check_access(data.project_id, user, db)

    preset = await db.get(Preset, data.preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")

    assets = await _get_project_assets(data.project_id, db)

    hw_profile = None
    if project.target_profile_id:
        hw = await db.get(HardwareProfile, project.target_profile_id)
        if hw:
            hw_profile = {
                "cpu_class": hw.cpu_class,
                "gpu_class": hw.gpu_class,
                "memory_limit_mb": hw.memory_limit_mb,
                "texture_memory_mb": hw.texture_memory_mb,
            }

    result = await suggest_preset_params(
        preset_schema=preset.schema or {},
        preset_name=preset.name,
        assets_info=assets,
        hardware_profile=hw_profile,
    )

    llm_req = LLMRequest(
        project_id=data.project_id,
        request_type="suggest_params",
        request_payload={"preset_id": data.preset_id},
        response_payload=result,
        confidence=result.get("confidence"),
        tokens_used=result.get("tokens_used"),
    )
    db.add(llm_req)
    await db.commit()
    await db.refresh(llm_req)
    return llm_req


@router.get("/history/{project_id}", response_model=list[LLMRequestOut])
async def llm_history(
    project_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Get LLM request history for a project."""
    await _check_access(project_id, user, db)
    result = await db.execute(
        select(LLMRequest)
        .where(LLMRequest.project_id == project_id)
        .order_by(LLMRequest.created_at.desc())
    )
    return result.scalars().all()
