from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import Project, ProjectStatus, User
from app.schemas.schemas import ProjectCreate, ProjectOut, ProjectUpdate

router = APIRouter(prefix="/projects", tags=["projects"])


@router.get("/", response_model=list[ProjectOut])
async def list_projects(
    skip: int = 0,
    limit: int = 50,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    query = select(Project)
    if user.role.value != "admin":
        query = query.where(Project.owner_id == user.id)
    query = query.order_by(Project.updated_at.desc()).offset(skip).limit(limit)
    result = await db.execute(query)
    return result.scalars().all()


@router.post("/", response_model=ProjectOut, status_code=201)
async def create_project(
    data: ProjectCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = Project(
        owner_id=user.id,
        title=data.title,
        description=data.description,
        target_profile_id=data.target_profile_id,
    )
    db.add(project)
    await db.commit()
    await db.refresh(project)
    return project


@router.get("/{project_id}", response_model=ProjectOut)
async def get_project(
    project_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    return project


@router.patch("/{project_id}", response_model=ProjectOut)
async def update_project(
    project_id: int,
    data: ProjectUpdate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    if data.title is not None:
        project.title = data.title
    if data.description is not None:
        project.description = data.description
    if data.status is not None:
        project.status = ProjectStatus(data.status)
    if data.target_profile_id is not None:
        project.target_profile_id = data.target_profile_id

    await db.commit()
    await db.refresh(project)
    return project


@router.delete("/{project_id}", status_code=204)
async def delete_project(
    project_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    project = await db.get(Project, project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    await db.delete(project)
    await db.commit()
