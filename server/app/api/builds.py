from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import (
    BuildArtifact,
    BuildJob,
    BuildLog,
    BuildStatus,
    Configuration,
    Project,
    User,
    WebhookType,
)
from app.schemas.schemas import BuildArtifactOut, BuildJobCreate, BuildJobOut, BuildLogOut
from app.services.webhook_service import trigger_webhooks
from app.tasks.build_tasks import run_build_task

router = APIRouter(prefix="/builds", tags=["builds"])


@router.post("/", response_model=BuildJobOut, status_code=201)
async def create_build(
    data: BuildJobCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Queue a new WebGL build job."""
    config = await db.get(Configuration, data.configuration_id)
    if not config:
        raise HTTPException(status_code=404, detail="Configuration not found")

    project = await db.get(Project, config.project_id)
    if not project:
        raise HTTPException(status_code=404, detail="Project not found")
    if user.role.value != "admin" and project.owner_id != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    # Determine target profile
    target_profile_id = data.target_profile_id or project.target_profile_id

    job = BuildJob(
        configuration_id=data.configuration_id,
        requested_by=user.id,
        target_profile_id=target_profile_id,
        priority=data.priority,
        status=BuildStatus.queued,
    )
    db.add(job)
    await db.commit()
    await db.refresh(job)

    # Trigger webhook
    await trigger_webhooks(db, WebhookType.build_started, {
        "build_job_id": job.id,
        "project_id": project.id,
        "configuration_id": config.id,
        "status": "queued",
    })

    # Queue Celery task
    task = run_build_task.delay(job.id)
    job.celery_task_id = task.id
    await db.commit()
    await db.refresh(job)

    return job


@router.get("/", response_model=list[BuildJobOut])
async def list_builds(
    skip: int = 0,
    limit: int = 50,
    status: str | None = None,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    query = select(BuildJob)
    if user.role.value != "admin":
        query = query.where(BuildJob.requested_by == user.id)
    if status:
        query = query.where(BuildJob.status == BuildStatus(status))
    query = query.order_by(BuildJob.created_at.desc()).offset(skip).limit(limit)
    result = await db.execute(query)
    return result.scalars().all()


@router.get("/{job_id}", response_model=BuildJobOut)
async def get_build(
    job_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    job = await db.get(BuildJob, job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Build job not found")
    if user.role.value != "admin" and job.requested_by != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    return job


@router.get("/{job_id}/artifacts", response_model=list[BuildArtifactOut])
async def list_artifacts(
    job_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    job = await db.get(BuildJob, job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Build job not found")
    if user.role.value != "admin" and job.requested_by != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    result = await db.execute(
        select(BuildArtifact).where(BuildArtifact.build_job_id == job_id)
    )
    return result.scalars().all()


@router.get("/{job_id}/logs", response_model=list[BuildLogOut])
async def list_build_logs(
    job_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    job = await db.get(BuildJob, job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Build job not found")
    if user.role.value != "admin" and job.requested_by != user.id:
        raise HTTPException(status_code=403, detail="Access denied")

    result = await db.execute(
        select(BuildLog)
        .where(BuildLog.build_job_id == job_id)
        .order_by(BuildLog.timestamp)
    )
    return result.scalars().all()


@router.post("/{job_id}/retry", response_model=BuildJobOut)
async def retry_build(
    job_id: int,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Retry a failed build."""
    job = await db.get(BuildJob, job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Build job not found")
    if user.role.value != "admin" and job.requested_by != user.id:
        raise HTTPException(status_code=403, detail="Access denied")
    if job.status != BuildStatus.failed:
        raise HTTPException(status_code=400, detail="Can only retry failed builds")

    # Reset and re-queue
    job.status = BuildStatus.queued
    job.started_at = None
    job.finished_at = None
    job.logs_summary = None
    await db.commit()

    task = run_build_task.delay(job.id)
    job.celery_task_id = task.id
    await db.commit()
    await db.refresh(job)
    return job
