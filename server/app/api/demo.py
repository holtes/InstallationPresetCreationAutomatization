"""
Демо-эндпоинт для создания тестовых данных.

Создаёт полный набор: проект → ассеты → конфигурацию → сборку
с артефактами, логами и реальной WebGL-страницей для скриншотов.
"""

import os
import uuid
from datetime import datetime, timedelta

from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import settings
from app.core.database import get_db
from app.core.deps import get_current_user
from app.models.models import (
    LLMRequest,
    Asset,
    AssetStatus,
    BuildArtifact,
    BuildJob,
    BuildLog,
    BuildLogType,
    BuildStatus,
    Configuration,
    ConfigurationStatus,
    HardwareProfile,
    Project,
    ProjectAsset,
    ProjectStatus,
    User,
)

router = APIRouter(prefix="/demo", tags=["demo"])


def _create_webgl_page(build_dir: str, build_job_id: int) -> str:
    """Create a realistic-looking WebGL placeholder page."""
    os.makedirs(build_dir, exist_ok=True)

    html = f"""<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Интерактивный каталог — WebGL Build</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            background: #0a0a1a;
            color: #e0e0e0;
            font-family: 'Segoe UI', Arial, sans-serif;
            overflow: hidden;
            height: 100vh;
        }}

        .header {{
            background: linear-gradient(135deg, #1a1a3e 0%, #2d1b69 100%);
            padding: 12px 24px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 2px solid #e94560;
        }}
        .header h1 {{ font-size: 18px; color: #fff; }}
        .header .badge {{
            background: #e94560;
            color: #fff;
            padding: 4px 12px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: 600;
        }}

        .canvas-container {{
            display: flex;
            justify-content: center;
            align-items: center;
            height: calc(100vh - 110px);
            padding: 20px;
        }}
        canvas {{
            border-radius: 12px;
            box-shadow: 0 0 40px rgba(233, 69, 96, 0.15);
        }}

        .footer {{
            position: fixed;
            bottom: 0;
            width: 100%;
            background: rgba(10, 10, 26, 0.95);
            border-top: 1px solid #2a2a4a;
            padding: 8px 24px;
            display: flex;
            justify-content: space-between;
            font-size: 12px;
            color: #666;
        }}
        .footer .fps {{ color: #4ade80; font-weight: 600; }}
    </style>
</head>
<body>
    <div class="header">
        <h1>Интерактивный каталог экспонатов</h1>
        <span class="badge">WebGL Build #{build_job_id}</span>
    </div>

    <div class="canvas-container">
        <canvas id="glCanvas" width="960" height="540"></canvas>
    </div>

    <div class="footer">
        <span>Interactive Installations Platform v1.0.0</span>
        <span>WebGL 2.0 | Текстуры: ASTC | Шейдеры: Medium</span>
        <span class="fps" id="fpsCounter">60 FPS</span>
    </div>

    <script>
        const canvas = document.getElementById('glCanvas');
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;

        // Catalog items (simulated exhibit cards)
        const items = [
            {{ title: 'Античная ваза', color: '#e94560', icon: '🏺' }},
            {{ title: 'Звёздная карта', color: '#4a9eff', icon: '🗺️' }},
            {{ title: 'Кристалл кварца', color: '#a855f7', icon: '💎' }},
            {{ title: 'Механические часы', color: '#f59e0b', icon: '⚙️' }},
            {{ title: 'Древний свиток', color: '#10b981', icon: '📜' }},
            {{ title: 'Золотая маска', color: '#eab308', icon: '🎭' }},
        ];

        let time = 0;
        let selectedItem = -1;
        let hoverItem = -1;
        let particles = [];

        // Generate particles
        for (let i = 0; i < 50; i++) {{
            particles.push({{
                x: Math.random() * W,
                y: Math.random() * H,
                size: Math.random() * 2 + 0.5,
                speed: Math.random() * 0.3 + 0.1,
                opacity: Math.random() * 0.5 + 0.1,
            }});
        }}

        function drawCard(x, y, w, h, item, index, hover) {{
            const scale = hover ? 1.05 : 1.0;
            const sw = w * scale, sh = h * scale;
            const sx = x - (sw - w) / 2, sy = y - (sh - h) / 2;

            // Card shadow
            ctx.shadowColor = hover ? item.color + '66' : 'rgba(0,0,0,0.3)';
            ctx.shadowBlur = hover ? 20 : 10;

            // Card background
            const grad = ctx.createLinearGradient(sx, sy, sx, sy + sh);
            grad.addColorStop(0, '#1e1e3a');
            grad.addColorStop(1, '#151530');
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.roundRect(sx, sy, sw, sh, 10);
            ctx.fill();

            // Border
            ctx.shadowBlur = 0;
            ctx.strokeStyle = hover ? item.color : '#2a2a4a';
            ctx.lineWidth = hover ? 2 : 1;
            ctx.beginPath();
            ctx.roundRect(sx, sy, sw, sh, 10);
            ctx.stroke();

            // Icon
            ctx.font = '36px serif';
            ctx.textAlign = 'center';
            ctx.fillText(item.icon, sx + sw / 2, sy + sh / 2 - 10);

            // Title
            ctx.font = '13px Segoe UI, Arial';
            ctx.fillStyle = '#ccc';
            ctx.fillText(item.title, sx + sw / 2, sy + sh - 20);

            // Accent line
            ctx.fillStyle = item.color;
            ctx.beginPath();
            ctx.roundRect(sx + 10, sy + 8, sw - 20, 3, 2);
            ctx.fill();
        }}

        function draw() {{
            // Background
            const bg = ctx.createRadialGradient(W/2, H/2, 100, W/2, H/2, W);
            bg.addColorStop(0, '#12122a');
            bg.addColorStop(1, '#0a0a1a');
            ctx.fillStyle = bg;
            ctx.fillRect(0, 0, W, H);

            // Particles
            particles.forEach(p => {{
                p.y -= p.speed;
                if (p.y < 0) {{ p.y = H; p.x = Math.random() * W; }}
                ctx.fillStyle = `rgba(233, 69, 96, ${{p.opacity}})`;
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
                ctx.fill();
            }});

            // Grid title
            ctx.textAlign = 'center';
            ctx.fillStyle = '#fff';
            ctx.font = 'bold 22px Segoe UI, Arial';
            ctx.fillText('Каталог экспонатов', W / 2, 50);
            ctx.font = '13px Segoe UI, Arial';
            ctx.fillStyle = '#888';
            ctx.fillText('Выберите экспонат для детального просмотра', W / 2, 74);

            // Cards grid (3x2)
            const cols = 3, rows = 2;
            const cardW = 180, cardH = 150;
            const gapX = 30, gapY = 25;
            const startX = (W - cols * cardW - (cols - 1) * gapX) / 2;
            const startY = 100;

            items.forEach((item, i) => {{
                const col = i % cols, row = Math.floor(i / cols);
                const x = startX + col * (cardW + gapX);
                const y = startY + row * (cardH + gapY);
                const animY = y + Math.sin(time * 2 + i * 0.8) * 3;
                const hover = (i === hoverItem);
                drawCard(x, animY, cardW, cardH, item, i, hover);
            }});

            // Animated selection indicator
            hoverItem = Math.floor((time / 2) % items.length);

            // Status bar
            ctx.fillStyle = 'rgba(30, 30, 60, 0.8)';
            ctx.beginPath();
            ctx.roundRect(W / 2 - 160, H - 60, 320, 36, 8);
            ctx.fill();
            ctx.font = '12px Segoe UI, Arial';
            ctx.fillStyle = '#888';
            ctx.textAlign = 'center';
            ctx.fillText('🖱️ Наведите для просмотра  |  👆 Нажмите для деталей  |  🔍 Масштабирование', W / 2, H - 38);

            time += 0.016;
            requestAnimationFrame(draw);
        }}

        // FPS counter
        let frames = 0, lastFps = performance.now();
        function countFps() {{
            frames++;
            const now = performance.now();
            if (now - lastFps >= 1000) {{
                document.getElementById('fpsCounter').textContent = frames + ' FPS';
                frames = 0;
                lastFps = now;
            }}
            requestAnimationFrame(countFps);
        }}

        draw();
        countFps();
    </script>
</body>
</html>"""

    index_path = os.path.join(build_dir, "index.html")
    with open(index_path, "w", encoding="utf-8") as f:
        f.write(html)

    return index_path


@router.post("/seed")
async def seed_demo_data(
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """
    Создаёт полный набор демо-данных для скриншотов:
    - Проект с привязанным профилем оборудования
    - 3 ассета (изображение, аудио, текст)
    - Конфигурация на основе пресета «Интерактивный каталог»
    - Завершённая сборка с артефактом (URL + iframe) и логами
    - Реальная WebGL-страница в /builds/
    """
    now = datetime.utcnow()

    # 1. Hardware profile
    hw = HardwareProfile(
        name="Демо-стенд (сенсорный киоск)",
        cpu_class="Intel Core i5-10400",
        gpu_class="NVIDIA GeForce GTX 1650",
        memory_limit_mb=8192,
        texture_memory_mb=2048,
        target_browser="Chromium 120",
        screen_width=1920,
        screen_height=1080,
        notes="Стандартный демонстрационный стенд для выставок",
    )
    db.add(hw)
    await db.flush()

    # 2. Project
    project = Project(
        owner_id=user.id,
        title="Каталог экспонатов музея",
        description="Интерактивный каталог экспонатов для сенсорного стенда в музее",
        status=ProjectStatus.published,
        target_profile_id=hw.id,
    )
    db.add(project)
    await db.flush()

    # 3. Assets (metadata only, no real files needed for demo)
    assets_data = [
        {
            "filename": "museum_background.png",
            "content_type": "image/png",
            "size_bytes": 2_458_624,
            "width": 1920,
            "height": 1080,
            "metadata": {"format": "PNG", "mode": "RGBA", "megapixels": 2.07},
            "role": "background",
            "slot_key": "background",
        },
        {
            "filename": "ambient_music.mp3",
            "content_type": "audio/mpeg",
            "size_bytes": 4_812_390,
            "duration": 180.0,
            "metadata": {"bitrate": 192, "channels": 2, "sample_rate": 44100},
            "role": "audio",
            "slot_key": "music",
        },
        {
            "filename": "exhibits_data.json",
            "content_type": "text/json",
            "size_bytes": 15_240,
            "metadata": {"items_count": 6, "encoding": "utf-8"},
            "role": "text",
            "slot_key": "item_descriptions",
        },
    ]

    asset_ids = []
    for ad in assets_data:
        asset = Asset(
            uploader_id=user.id,
            filename=ad["filename"],
            stored_path=f"/app/storage/assets/demo/{ad['filename']}",
            content_type=ad["content_type"],
            size_bytes=ad["size_bytes"],
            hash=uuid.uuid4().hex,
            width=ad.get("width"),
            height=ad.get("height"),
            duration_seconds=ad.get("duration"),
            metadata_json=ad.get("metadata", {}),
            status=AssetStatus.ready,
        )
        db.add(asset)
        await db.flush()
        asset_ids.append(asset.id)

        pa = ProjectAsset(
            project_id=project.id,
            asset_id=asset.id,
            role_in_project=ad["role"],
            slot_key=ad["slot_key"],
        )
        db.add(pa)

    # 4. Get first preset (Интерактивный каталог)
    from sqlalchemy import select
    from app.models.models import Preset

    result = await db.execute(select(Preset).limit(1))
    preset = result.scalar_one_or_none()
    preset_id = preset.id if preset else 1

    # 5. Configuration
    config = Configuration(
        project_id=project.id,
        preset_id=preset_id,
        config_json={
            "version": "1.0",
            "mechanic": {"type": "catalog", "name": "Интерактивный каталог"},
            "assets": {
                "background": {
                    "asset_id": asset_ids[0],
                    "filename": "museum_background.png",
                    "path": "StreamingAssets/museum_background.png",
                },
                "music": {
                    "asset_id": asset_ids[1],
                    "filename": "ambient_music.mp3",
                    "path": "StreamingAssets/ambient_music.mp3",
                },
                "item_descriptions": {
                    "asset_id": asset_ids[2],
                    "filename": "exhibits_data.json",
                    "path": "StreamingAssets/exhibits_data.json",
                },
            },
            "parameters": {
                "columns": 3,
                "animation_speed": 0.5,
                "enable_search": True,
                "enable_zoom": True,
                "transition_type": "fade",
            },
            "optimization": {
                "target_fps": 60,
                "texture_compression": "ASTC",
                "lod_enabled": True,
                "lod_bias": 1.0,
                "shader_quality": "medium",
                "max_texture_size": 2048,
            },
        },
        created_by=user.id,
        status=ConfigurationStatus.built,
    )
    db.add(config)
    await db.flush()

    # 6. Build job (success)
    started = now - timedelta(seconds=47)
    finished = now - timedelta(seconds=5)

    job = BuildJob(
        configuration_id=config.id,
        requested_by=user.id,
        target_profile_id=hw.id,
        status=BuildStatus.success,
        started_at=started,
        finished_at=finished,
        logs_summary="Build succeeded in 42.3s",
        attempts=1,
        priority=0,
        celery_task_id=f"demo-task-{uuid.uuid4().hex[:12]}",
    )
    db.add(job)
    await db.flush()

    # 7. Create real WebGL page
    build_dir_name = f"build_{job.id}"
    build_dir = os.path.join(settings.BUILDS_DIR, build_dir_name)
    _create_webgl_page(build_dir, job.id)

    artifact_url = f"{settings.BASE_URL}/builds/{build_dir_name}/index.html"
    iframe_code = (
        f'<iframe src="{artifact_url}" width="960" height="600" '
        f'frameborder="0" allowfullscreen allow="autoplay; fullscreen; gamepad"></iframe>'
    )

    # 8. Build artifact
    artifact = BuildArtifact(
        build_job_id=job.id,
        artifact_url=artifact_url,
        artifact_path=build_dir,
        size_bytes=18_742_528,
        bundle_hash=uuid.uuid4().hex,
        build_time_seconds=42.3,
        optimizations_applied={
            "items": [
                "Texture compression: ASTC",
                "LOD bias: 1.0",
                "Shader quality: medium",
                "Target FPS: 60",
                "gzip: framework.js",
                "gzip: data.wasm",
            ]
        },
        iframe_code=iframe_code,
    )
    db.add(artifact)

    # 9. Build logs (realistic sequence)
    logs = [
        (BuildLogType.info, started, "Build started"),
        (BuildLogType.info, started + timedelta(seconds=1), f"Using configuration #{config.id}"),
        (BuildLogType.info, started + timedelta(seconds=2), "Collected 3 assets"),
        (BuildLogType.info, started + timedelta(seconds=3), f"Workspace prepared: /app/storage/builds/workspace_{job.id}"),
        (BuildLogType.info, started + timedelta(seconds=4), "Starting Unity build: WebGL target"),
        (BuildLogType.info, started + timedelta(seconds=5), "Resolving asset references..."),
        (BuildLogType.info, started + timedelta(seconds=8), "Compiling scripts... (Assembly-CSharp.dll)"),
        (BuildLogType.info, started + timedelta(seconds=15), "Building scenes... (MainScene)"),
        (BuildLogType.info, started + timedelta(seconds=20), "Processing textures: ASTC compression applied"),
        (BuildLogType.warning, started + timedelta(seconds=22), "Texture 'museum_background.png' downscaled from 4096 to 2048 (budget limit)"),
        (BuildLogType.info, started + timedelta(seconds=25), "Generating LOD meshes..."),
        (BuildLogType.info, started + timedelta(seconds=30), "Optimizing shaders (quality: medium)"),
        (BuildLogType.info, started + timedelta(seconds=35), "Packaging WebGL build..."),
        (BuildLogType.info, started + timedelta(seconds=38), "Post-build: gzip compression applied to framework.js, data.wasm"),
        (BuildLogType.info, started + timedelta(seconds=40), "Unity exit code: 0"),
        (BuildLogType.info, started + timedelta(seconds=41), f"Build size: 17.9 MB"),
        (BuildLogType.info, started + timedelta(seconds=42), f"Build completed: {artifact_url}"),
    ]

    for log_type, ts, msg in logs:
        db.add(BuildLog(
            build_job_id=job.id,
            log_type=log_type,
            message=msg,
            timestamp=ts,
        ))

    await db.commit()

    # 10. LLM map-assets mock record
    llm_response = {
        "mapping": {
            "background": asset_ids[0],
            "music": asset_ids[1],
            "item_descriptions": asset_ids[2],
        },
        "confidence": 0.94,
        "explanation": (
            "Файл museum_background.png является изображением высокого разрешения (1920x1080), "
            "что идеально подходит для слота background. Файл ambient_music.mp3 — аудиофайл "
            "длительностью 180 секунд, назначен в слот music для фонового сопровождения. "
            "Файл exhibits_data.json содержит текстовые данные с описаниями 6 экспонатов, "
            "что соответствует слоту item_descriptions."
        ),
        "tokens_used": 847,
    }

    llm_req = LLMRequest(
        project_id=project.id,
        request_type="map_assets",
        request_payload={
            "preset_id": preset_id,
            "preset_name": "Интерактивный каталог",
            "asset_count": 3,
        },
        response_payload=llm_response,
        confidence=0.94,
        tokens_used=847,
    )
    db.add(llm_req)
    await db.commit()
    await db.refresh(llm_req)

    return {
        "message": "Demo data created successfully",
        "project_id": project.id,
        "build_job_id": job.id,
        "llm_request_id": llm_req.id,
        "artifact_url": artifact_url,
        "iframe_code": iframe_code,
        "instructions": {
            "artifacts_screenshot": f"GET /api/builds/{job.id}/artifacts",
            "logs_screenshot": f"GET /api/builds/{job.id}/logs",
            "webgl_page": artifact_url,
            "llm_map_assets": f"POST /api/demo/llm-map-assets (project_id={project.id}, preset_id={preset_id})",
        },
    }


@router.post("/llm-map-assets")
async def demo_llm_map_assets(
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """
    Заглушка POST /api/llm/map-assets — возвращает реалистичный
    результат LLM-маппинга ассетов к слотам пресета без обращения к Gemini.

    Требует предварительного вызова POST /api/demo/seed.
    """
    from sqlalchemy import select
    from app.models.models import Preset

    # Find demo project
    result = await db.execute(
        select(Project).where(
            Project.owner_id == user.id,
            Project.title == "Каталог экспонатов музея",
        ).order_by(Project.id.desc()).limit(1)
    )
    project = result.scalar_one_or_none()
    if not project:
        from fastapi import HTTPException
        raise HTTPException(status_code=404, detail="Demo project not found. Run POST /api/demo/seed first.")

    # Get project assets
    result = await db.execute(
        select(ProjectAsset, Asset)
        .join(Asset, ProjectAsset.asset_id == Asset.id)
        .where(ProjectAsset.project_id == project.id)
    )
    rows = result.all()

    assets_summary = []
    asset_id_map = {}
    for pa, asset in rows:
        assets_summary.append({
            "id": asset.id,
            "filename": asset.filename,
            "content_type": asset.content_type,
            "size_bytes": asset.size_bytes,
            "role_in_project": pa.role_in_project,
        })
        asset_id_map[pa.slot_key] = asset.id

    # Get first preset
    result = await db.execute(select(Preset).limit(1))
    preset = result.scalar_one_or_none()
    preset_id = preset.id if preset else 1
    preset_name = preset.name if preset else "Интерактивный каталог"

    # Build realistic LLM response
    llm_response = {
        "mapping": asset_id_map,
        "confidence": 0.94,
        "explanation": (
            "Файл museum_background.png является изображением высокого разрешения (1920x1080), "
            "что идеально подходит для слота background. Файл ambient_music.mp3 — аудиофайл "
            "длительностью 180 секунд, назначен в слот music для фонового сопровождения. "
            "Файл exhibits_data.json содержит текстовые данные с описаниями 6 экспонатов, "
            "что соответствует слоту item_descriptions."
        ),
        "tokens_used": 847,
    }

    # Save to DB
    llm_req = LLMRequest(
        project_id=project.id,
        request_type="map_assets",
        request_payload={
            "preset_id": preset_id,
            "preset_name": preset_name,
            "asset_count": len(assets_summary),
            "assets": assets_summary,
        },
        response_payload=llm_response,
        confidence=0.94,
        tokens_used=847,
    )
    db.add(llm_req)
    await db.commit()
    await db.refresh(llm_req)

    return {
        "id": llm_req.id,
        "project_id": llm_req.project_id,
        "request_type": llm_req.request_type,
        "response_payload": llm_req.response_payload,
        "confidence": llm_req.confidence,
        "tokens_used": llm_req.tokens_used,
        "created_at": llm_req.created_at.isoformat() if llm_req.created_at else None,
    }
