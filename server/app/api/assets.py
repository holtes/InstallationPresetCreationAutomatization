import os
import uuid

from fastapi import APIRouter, Depends, HTTPException, UploadFile
from fastapi.responses import FileResponse
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import settings
from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import Asset, AssetStatus, Project, ProjectAsset, User
from app.schemas.schemas import AssetOut, ProjectAssetCreate, ProjectAssetOut
from app.services.asset_analyzer import (
    analyze_asset,
    classify_asset,
    detect_content_type,
    validate_asset,
)

router = APIRouter(prefix="/assets", tags=["assets"])


@router.post("/upload", response_model=AssetOut, status_code=201)
async def upload_asset(
    file: UploadFile,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Upload and analyze an asset file."""
    filename = file.filename or "unnamed"
    content_type = file.content_type or detect_content_type(filename)

    # Read file to get size
    content = await file.read()
    size_bytes = len(content)

    # Validate
    errors = validate_asset(filename, content_type, size_bytes)
    if errors:
        raise HTTPException(status_code=400, detail="; ".join(errors))

    # Save to disk
    ext = os.path.splitext(filename)[1]
    stored_name = f"{uuid.uuid4().hex}{ext}"
    category = classify_asset(filename, content_type)
    category_dir = os.path.join(settings.ASSETS_DIR, category)
    os.makedirs(category_dir, exist_ok=True)
    stored_path = os.path.join(category_dir, stored_name)

    with open(stored_path, "wb") as f:
        f.write(content)

    # Analyze
    analysis = analyze_asset(stored_path, filename, content_type)

    # Save to DB
    asset = Asset(
        uploader_id=user.id,
        filename=filename,
        stored_path=stored_path,
        content_type=content_type,
        size_bytes=analysis["size_bytes"],
        hash=analysis["hash"],
        width=analysis.get("width"),
        height=analysis.get("height"),
        duration_seconds=analysis.get("duration_seconds"),
        polygon_count=analysis.get("polygon_count"),
        metadata_json=analysis.get("metadata", {}),
        status=AssetStatus.ready,
    )
    db.add(asset)
    await db.commit()
    await db.refresh(asset)
    return asset


@router.get("/", response_model=list[AssetOut])
async def list_assets(
    skip: int = 0,
    limit: int = 50,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    query = select(Asset)
    if user.role.value != "admin":
        query = query.where(Asset.uploader_id == user.id)
    result = await db.execute(query.offset(skip).limit(limit))
    return result.scalars().all()


@router.get("/{asset_id}/file")
async def download_asset_file(
    asset_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Stream the raw asset file (image/audio/video/etc.) by id."""
    asset = await db.get(Asset, asset_id)
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    if user.role.value != "admin" and asset.uploader_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    if not asset.stored_path or not os.path.exists(asset.stored_path):
        raise HTTPException(status_code=404, detail="File missing on disk")

    return FileResponse(
        path=asset.stored_path,
        media_type=asset.content_type or "application/octet-stream",
        filename=asset.filename,
    )


@router.get("/{asset_id}", response_model=AssetOut)
async def get_asset(
    asset_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    asset = await db.get(Asset, asset_id)
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    if user.role.value != "admin" and asset.uploader_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    return asset


@router.delete("/{asset_id}", status_code=204)
async def delete_asset(
    asset_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    asset = await db.get(Asset, asset_id)
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    if user.role.value != "admin" and asset.uploader_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    # Remove file
    if os.path.exists(asset.stored_path):
        os.remove(asset.stored_path)

    await db.delete(asset)
    await db.commit()


# ---------- Project-Asset linking ----------

@router.post("/projects/{project_id}/assets", response_model=ProjectAssetOut, status_code=201)
async def link_asset_to_project(
    project_id: int,
    data: ProjectAssetCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    asset = await db.get(Asset, data.asset_id)
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    pa = ProjectAsset(
        project_id=project_id,
        asset_id=data.asset_id,
        role_in_project=data.role_in_project,
        slot_key=data.slot_key,
    )
    db.add(pa)
    await db.commit()
    await db.refresh(pa)
    return pa


@router.get("/projects/{project_id}/assets", response_model=list[ProjectAssetOut])
async def list_project_assets(
    project_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    result = await db.execute(
        select(ProjectAsset).where(ProjectAsset.project_id == project_id)
    )
    return result.scalars().all()


@router.delete("/projects/{project_id}/assets/{link_id}", status_code=204)
async def unlink_asset_from_project(
    project_id: int,
    link_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    pa = await db.get(ProjectAsset, link_id)
    if not pa or pa.project_id != project_id:
        raise HTTPException(status_code=404, detail="Link not found")
    await db.delete(pa)
    await db.commit()
