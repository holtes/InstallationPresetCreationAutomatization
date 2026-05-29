from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user, require_admin
from app.models.models import Preset, User
from app.schemas.schemas import PresetCreate, PresetOut, PresetUpdate

router = APIRouter(prefix="/presets", tags=["presets"])


@router.get("/", response_model=list[PresetOut])
async def list_presets(
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    result = await db.execute(select(Preset))
    return result.scalars().all()


@router.post("/", response_model=PresetOut, status_code=201)
async def create_preset(
    data: PresetCreate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    preset = Preset(
        name=data.name,
        description=data.description,
        schema=data.schema_ if data.schema_ else {},
        thumbnail_url=data.thumbnail_url,
        version=data.version,
    )
    db.add(preset)
    await db.commit()
    await db.refresh(preset)
    return preset


@router.get("/{preset_id}", response_model=PresetOut)
async def get_preset(
    preset_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    preset = await db.get(Preset, preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")
    return preset


@router.patch("/{preset_id}", response_model=PresetOut)
async def update_preset(
    preset_id: int,
    data: PresetUpdate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    preset = await db.get(Preset, preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")

    update_data = data.model_dump(exclude_unset=True)
    if "schema_" in update_data:
        update_data["schema"] = update_data.pop("schema_")
    for field, value in update_data.items():
        setattr(preset, field, value)

    await db.commit()
    await db.refresh(preset)
    return preset


@router.delete("/{preset_id}", status_code=204)
async def delete_preset(
    preset_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    preset = await db.get(Preset, preset_id)
    if not preset:
        raise HTTPException(status_code=404, detail="Preset not found")
    await db.delete(preset)
    await db.commit()
