from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.core.deps import require_admin
from app.models.models import User, Webhook, WebhookType
from app.schemas.schemas import WebhookCreate, WebhookOut, WebhookUpdate

router = APIRouter(prefix="/webhooks", tags=["webhooks"])


@router.get("/", response_model=list[WebhookOut])
async def list_webhooks(
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    result = await db.execute(select(Webhook))
    return result.scalars().all()


@router.post("/", response_model=WebhookOut, status_code=201)
async def create_webhook(
    data: WebhookCreate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    webhook = Webhook(
        name=data.name,
        endpoint_url=data.endpoint_url,
        secret=data.secret,
        event_type=WebhookType(data.event_type),
    )
    db.add(webhook)
    await db.commit()
    await db.refresh(webhook)
    return webhook


@router.get("/{webhook_id}", response_model=WebhookOut)
async def get_webhook(
    webhook_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    webhook = await db.get(Webhook, webhook_id)
    if not webhook:
        raise HTTPException(status_code=404, detail="Webhook not found")
    return webhook


@router.patch("/{webhook_id}", response_model=WebhookOut)
async def update_webhook(
    webhook_id: int,
    data: WebhookUpdate,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    webhook = await db.get(Webhook, webhook_id)
    if not webhook:
        raise HTTPException(status_code=404, detail="Webhook not found")

    if data.name is not None:
        webhook.name = data.name
    if data.endpoint_url is not None:
        webhook.endpoint_url = data.endpoint_url
    if data.secret is not None:
        webhook.secret = data.secret
    if data.event_type is not None:
        webhook.event_type = WebhookType(data.event_type)
    if data.is_active is not None:
        webhook.is_active = data.is_active

    await db.commit()
    await db.refresh(webhook)
    return webhook


@router.delete("/{webhook_id}", status_code=204)
async def delete_webhook(
    webhook_id: int,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(require_admin),
):
    webhook = await db.get(Webhook, webhook_id)
    if not webhook:
        raise HTTPException(status_code=404, detail="Webhook not found")
    await db.delete(webhook)
    await db.commit()
