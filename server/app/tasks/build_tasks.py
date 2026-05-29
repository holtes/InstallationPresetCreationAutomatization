"""
Celery-задачи для асинхронной сборки WebGL.

Задача выполняет полный pipeline:
1. Подготовка рабочей директории (копирование ассетов, конфига)
2. Запуск Unity CLI для экспорта WebGL
3. Применение пост-оптимизаций
4. Сохранение артефактов в БД
5. Уведомление через вебхуки
"""

import logging
import os
from datetime import datetime, timezone

from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session

from app.core.config import settings
from app.models.models import (
    BuildArtifact,
    BuildJob,
    BuildLog,
    BuildLogType,
    BuildStatus,
    Configuration,
    ProjectAsset,
    Asset,
    Webhook,
    WebhookType,
)
from app.services.build_service import (
    generate_iframe_code,
    get_build_size,
    prepare_build_directory,
    run_unity_build,
)
from app.tasks.celery_app import celery_app

logger = logging.getLogger(__name__)

# Synchronous engine for Celery tasks
sync_engine = create_engine(settings.DATABASE_URL_SYNC)


def _add_log(session: Session, build_job_id: int, log_type: BuildLogType, message: str):
    log = BuildLog(
        build_job_id=build_job_id,
        log_type=log_type,
        message=message,
    )
    session.add(log)
    session.flush()


@celery_app.task(bind=True, max_retries=2, default_retry_delay=30)
def run_build_task(self, build_job_id: int):
    """Execute a WebGL build asynchronously."""
    with Session(sync_engine) as session:
        # Load build job
        job = session.get(BuildJob, build_job_id)
        if not job:
            logger.error("BuildJob %d not found", build_job_id)
            return {"error": "BuildJob not found"}

        # Update status
        job.status = BuildStatus.running
        job.started_at = datetime.now(timezone.utc)
        job.attempts += 1
        job.celery_task_id = self.request.id
        session.commit()

        _add_log(session, build_job_id, BuildLogType.info, "Build started")

        try:
            # Load configuration
            config = session.get(Configuration, job.configuration_id)
            if not config:
                raise ValueError("Configuration not found")

            _add_log(session, build_job_id, BuildLogType.info, f"Using configuration #{config.id}")

            # Gather asset paths
            project_assets = session.execute(
                select(ProjectAsset, Asset)
                .join(Asset, ProjectAsset.asset_id == Asset.id)
                .where(ProjectAsset.project_id == config.project_id)
            ).all()

            asset_paths = [a.Asset.stored_path for a in project_assets if os.path.exists(a.Asset.stored_path)]
            _add_log(session, build_job_id, BuildLogType.info, f"Collected {len(asset_paths)} assets")

            # Get hardware profile
            hw_profile = None
            if job.target_profile_id:
                from app.models.models import HardwareProfile
                hw = session.get(HardwareProfile, job.target_profile_id)
                if hw:
                    hw_profile = {
                        "cpu_class": hw.cpu_class,
                        "gpu_class": hw.gpu_class,
                        "memory_limit_mb": hw.memory_limit_mb,
                        "texture_memory_mb": hw.texture_memory_mb,
                        "target_browser": hw.target_browser,
                    }

            # Prepare build directory
            workspace = prepare_build_directory(build_job_id, config.config_json, asset_paths)
            _add_log(session, build_job_id, BuildLogType.info, f"Workspace prepared: {workspace}")

            # Run build
            build_result = run_unity_build(workspace, build_job_id, hw_profile)

            for log_line in build_result.get("logs", []):
                _add_log(session, build_job_id, BuildLogType.info, log_line)

            if build_result["success"]:
                # Create artifact
                output_dir = build_result["output_dir"]
                build_id_path = os.path.basename(output_dir)
                artifact_url = f"{settings.BASE_URL}/builds/{build_id_path}/index.html"
                iframe_code = generate_iframe_code(artifact_url)

                artifact = BuildArtifact(
                    build_job_id=build_job_id,
                    artifact_url=artifact_url,
                    artifact_path=output_dir,
                    size_bytes=get_build_size(output_dir),
                    build_time_seconds=build_result["build_time_seconds"],
                    optimizations_applied={
                        "items": build_result.get("optimizations", [])
                    },
                    iframe_code=iframe_code,
                )
                session.add(artifact)

                job.status = BuildStatus.success
                job.finished_at = datetime.now(timezone.utc)
                job.logs_summary = f"Build succeeded in {build_result['build_time_seconds']:.1f}s"

                _add_log(session, build_job_id, BuildLogType.info,
                         f"Build completed: {artifact_url}")

                session.commit()

                # Trigger webhooks (fire and forget)
                _fire_webhooks_sync(session, WebhookType.build_completed, {
                    "build_job_id": build_job_id,
                    "artifact_url": artifact_url,
                    "iframe_code": iframe_code,
                    "status": "success",
                })

                return {
                    "status": "success",
                    "artifact_url": artifact_url,
                    "iframe_code": iframe_code,
                    "build_time": build_result["build_time_seconds"],
                }

            else:
                # Build failed
                job.status = BuildStatus.failed
                job.finished_at = datetime.now(timezone.utc)
                job.logs_summary = "Build failed"
                _add_log(session, build_job_id, BuildLogType.error, "Build failed")
                session.commit()

                _fire_webhooks_sync(session, WebhookType.build_failed, {
                    "build_job_id": build_job_id,
                    "status": "failed",
                })

                # Retry if possible
                if self.request.retries < self.max_retries:
                    raise self.retry(exc=Exception("Build failed"))

                return {"status": "failed"}

        except self.MaxRetriesExceededError:
            job.status = BuildStatus.failed
            job.finished_at = datetime.now(timezone.utc)
            job.logs_summary = "Max retries exceeded"
            session.commit()
            return {"status": "failed", "reason": "max_retries_exceeded"}

        except Exception as e:
            logger.exception("Build task error for job %d", build_job_id)
            _add_log(session, build_job_id, BuildLogType.error, str(e))
            job.status = BuildStatus.failed
            job.finished_at = datetime.now(timezone.utc)
            job.logs_summary = str(e)[:500]
            session.commit()
            return {"status": "error", "reason": str(e)}


def _fire_webhooks_sync(session: Session, event_type: WebhookType, payload: dict):
    """Synchronous webhook trigger for use in Celery tasks."""
    import httpx
    import hashlib
    import hmac
    import json

    webhooks = session.execute(
        select(Webhook).where(
            Webhook.event_type == event_type,
            Webhook.is_active == True,
        )
    ).scalars().all()

    json_payload = json.dumps(payload, ensure_ascii=False, default=str)

    for wh in webhooks:
        headers = {"Content-Type": "application/json"}
        if wh.secret:
            sig = hmac.new(wh.secret.encode(), json_payload.encode(), hashlib.sha256).hexdigest()
            headers["X-Webhook-Signature"] = sig

        try:
            resp = httpx.post(wh.endpoint_url, content=json_payload, headers=headers, timeout=10)
            wh.last_status = resp.status_code
        except Exception as e:
            logger.error("Webhook %s failed: %s", wh.id, e)
            wh.last_status = 0

        wh.last_triggered_at = datetime.now(timezone.utc)

    session.commit()
