from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user, require_admin
from app.models.models import HardwareProfile, User
from app.schemas.schemas import HardwareProfileCreate, HardwareProfileOut, HardwareProfileUpdate

router = APIRouter(prefix="/hardware-profiles", tags=["hardware_profiles"])


@router.get("/", response_model=list[HardwareProfileOut])
async def list_profiles(
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    result = await db.execute(select(HardwareProfile))
    return result.scalars().all()


@router.post("/", response_model=HardwareProfileOut, status_code=201)
async def create_profile(
    data: HardwareProfileCreate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    profile = HardwareProfile(**data.model_dump())
    db.add(profile)
    await db.commit()
    await db.refresh(profile)
    return profile


@router.get("/{profile_id}", response_model=HardwareProfileOut)
async def get_profile(
    profile_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    profile = await db.get(HardwareProfile, profile_id)
    if not profile:
        raise HTTPException(status_code=404, detail="Hardware profile not found")
    return profile


@router.patch("/{profile_id}", response_model=HardwareProfileOut)
async def update_profile(
    profile_id: int,
    data: HardwareProfileUpdate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    profile = await db.get(HardwareProfile, profile_id)
    if not profile:
        raise HTTPException(status_code=404, detail="Hardware profile not found")

    for field, value in data.model_dump(exclude_unset=True).items():
        setattr(profile, field, value)

    await db.commit()
    await db.refresh(profile)
    return profile


@router.delete("/{profile_id}", status_code=204)
async def delete_profile(
    profile_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    profile = await db.get(HardwareProfile, profile_id)
    if not profile:
        raise HTTPException(status_code=404, detail="Hardware profile not found")
    await db.delete(profile)
    await db.commit()
