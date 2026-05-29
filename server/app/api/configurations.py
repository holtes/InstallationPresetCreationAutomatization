from fastapi import APIRouter, Body, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import (
    Asset,
    Configuration,
    ConfigurationStatus,
    HardwareProfile,
    Preset,
    Project,
    ProjectAsset,
    User,
)
from app.schemas.schemas import ConfigurationCreate, ConfigurationOut, ConfigurationUpdate
from app.services.config_generator import generate_project_config, validate_config

router = APIRouter(prefix="/projects/{project_id}/configurations", tags=["configurations"])


async def _check_project_access(project_id: int, user: User, db: AsyncSession) -> Project:
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    return project


@router.get("/", response_model=list[ConfigurationOut])
async def list_configurations(
    project_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    await _check_project_access(project_id, user, db)
    result = await db.execute(
        select(Configuration).where(Configuration.project_id == project_id)
    )
    return result.scalars().all()


@router.post("/", response_model=ConfigurationOut, status_code=201)
async def create_configuration(
    project_id: int,
    data: ConfigurationCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await _check_project_access(project_id, user, db)

    preset = await db.get(Preset, data.preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")

    config = Configuration(
        project_id=project_id,
        preset_id=data.preset_id,
        config_json=data.config_json,
        created_by=user.id,
    )
    db.add(config)
    await db.commit()
    await db.refresh(config)
    return config


@router.post("/generate", response_model=ConfigurationOut, status_code=201)
async def generate_configuration(
    project_id: int,
    preset_id: int = Body(...),
    asset_mapping: dict[str, int] | None = Body(default=None),
    params: dict | None = Body(default=None),
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Auto-generate configuration using config_generator service."""
    project = await _check_project_access(project_id, user, db)

    preset = await db.get(Preset, preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")

    # Gather project assets info
    result = await db.execute(
        select(ProjectAsset, Asset)
        .join(Asset, ProjectAsset.asset_id == Asset.id)
        .where(ProjectAsset.project_id == project_id)
    )
    rows = result.all()
    assets_info = {}
    for pa, asset in rows:
        assets_info[asset.id] = {
            "filename": asset.filename,
            "content_type": asset.content_type,
            "stored_path": asset.stored_path,
            "size_bytes": asset.size_bytes,
            "metadata": asset.metadata_json or {},
            "role": pa.role_in_project,
            "slot_key": pa.slot_key,
        }

    # Build mapping from project_assets if not provided
    if not asset_mapping:
        asset_mapping = {}
        for pa, asset in rows:
            if pa.slot_key:
                asset_mapping[pa.slot_key] = asset.id

    # Get hardware profile
    hw_profile = None
    if project.target_profile_id:
        hw = await db.get(HardwareProfile, project.target_profile_id)
        if hw:
            hw_profile = {
                "memory_limit_mb": hw.memory_limit_mb,
                "texture_memory_mb": hw.texture_memory_mb,
                "gpu_class": hw.gpu_class,
            }

    config_json = generate_project_config(
        preset_schema=preset.schema or {},
        asset_mapping=asset_mapping,
        assets_info=assets_info,
        params=params or {},
        hardware_profile=hw_profile,
    )

    config = Configuration(
        project_id=project_id,
        preset_id=preset_id,
        config_json=config_json,
        created_by=user.id,
        status=ConfigurationStatus.validated,
    )
    db.add(config)
    await db.commit()
    await db.refresh(config)
    return config


@router.get("/{config_id}", response_model=ConfigurationOut)
async def get_configuration(
    project_id: int,
    config_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    await _check_project_access(project_id, user, db)
    config = await db.get(Configuration, config_id)
    if not config or config.project_id != project_id:
        raise HTTPException(status_code=404, detail="Configuration not found")
    return config


@router.patch("/{config_id}", response_model=ConfigurationOut)
async def update_configuration(
    project_id: int,
    config_id: int,
    data: ConfigurationUpdate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    await _check_project_access(project_id, user, db)
    config = await db.get(Configuration, config_id)
    if not config or config.project_id != project_id:
        raise HTTPException(status_code=404, detail="Configuration not found")

    if data.config_json is not None:
        config.config_json = data.config_json
    if data.status is not None:
        config.status = ConfigurationStatus(data.status)

    await db.commit()
    await db.refresh(config)
    return config


@router.post("/{config_id}/validate")
async def validate_configuration(
    project_id: int,
    config_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Validate configuration against preset schema."""
    await _check_project_access(project_id, user, db)
    config = await db.get(Configuration, config_id)
    if not config or config.project_id != project_id:
        raise HTTPException(status_code=404, detail="Configuration not found")

    preset = await db.get(Preset, config.preset_id)
    errors = validate_config(config.config_json, preset.schema or {})

    if not errors:
        config.status = ConfigurationStatus.validated
        await db.commit()

    return {"valid": len(errors) == 0, "errors": errors}
