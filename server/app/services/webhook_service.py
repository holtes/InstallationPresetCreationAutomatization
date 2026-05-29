"""
Модуль вебхуков для интеграции с системами управления стендами.

Отправляет уведомления на внешние endpoint'ы при:
- Начале сборки (build_started)
- Завершении сборки (build_completed)
- Ошибке сборки (build_failed)
"""

import hashlib
import hmac
import json
import logging
from datetime import datetime, timezone

import httpx
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.models import Webhook, WebhookType

logger = logging.getLogger(__name__)


def _sign_payload(payload: str, secret: str) -> str:
    """Create HMAC-SHA256 signature for webhook payload."""
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()


async def trigger_webhooks(
    db: AsyncSession,
    event_type: WebhookType,
    payload: dict,
) -> list[dict]:
    """
    Fire all active webhooks matching the event type.
    Returns list of {webhook_id, status_code, success}.
    """
    result = await db.execute(
        select(Webhook).where(
            Webhook.event_type == event_type,
            Webhook.is_active == True,
        )
    )
    webhooks = result.scalars().all()

    results = []
    json_payload = json.dumps(payload, ensure_ascii=False, default=str)

    async with httpx.AsyncClient(timeout=10.0) as client:
        for wh in webhooks:
            headers = {"Content-Type": "application/json"}
            if wh.secret:
                sig = _sign_payload(json_payload, wh.secret)
                headers["X-Webhook-Signature"] = sig

            try:
                response = await client.post(
                    wh.endpoint_url,
                    content=json_payload,
                    headers=headers,
                )
                status_code = response.status_code
                success = 200 <= status_code < 300
            except Exception as e:
                logger.error("Webhook %s failed: %s", wh.id, e)
                status_code = 0
                success = False

            # Update webhook status
            await db.execute(
                update(Webhook)
                .where(Webhook.id == wh.id)
                .values(
                    last_status=status_code,
                    last_triggered_at=datetime.now(timezone.utc),
                )
            )

            results.append({
                "webhook_id": wh.id,
                "status_code": status_code,
                "success": success,
            })

    await db.commit()
    return results
