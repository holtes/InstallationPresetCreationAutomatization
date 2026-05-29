import logging

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from sqlalchemy import select

from app.api import auth, users, projects, assets, configurations, builds, presets, hardware_profiles, webhooks, llm, demo
from app.core.config import settings
from app.core.database import engine, Base, async_session_factory
from app.core.security import hash_password
from app.models.models import User, UserRole

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(
    title="Interactive Installations Platform",
    description=(
        "Платформа быстрого прототипирования игровых механик "
        "для интерактивных каталогов и инсталляций"
    ),
    version="1.0.0",
)

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.CORS_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# API routers
app.include_router(auth.router, prefix="/api")
app.include_router(users.router, prefix="/api")
app.include_router(projects.router, prefix="/api")
app.include_router(assets.router, prefix="/api")
app.include_router(presets.router, prefix="/api")
app.include_router(hardware_profiles.router, prefix="/api")
app.include_router(configurations.router, prefix="/api")
app.include_router(builds.router, prefix="/api")
app.include_router(webhooks.router, prefix="/api")
app.include_router(llm.router, prefix="/api")
app.include_router(demo.router, prefix="/api")

# Static hosting for WebGL builds
app.mount("/builds", StaticFiles(directory=settings.BUILDS_DIR, html=True), name="builds")


@app.on_event("startup")
async def on_startup():
    # Create tables
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    # Seed admin user
    async with async_session_factory() as session:
        result = await session.execute(select(User).where(User.email == settings.ADMIN_EMAIL))
        if not result.scalar_one_or_none():
            admin = User(
                email=settings.ADMIN_EMAIL,
                display_name="Admin",
                password_hash=hash_password(settings.ADMIN_PASSWORD),
                role=UserRole.admin,
            )
            session.add(admin)
            await session.commit()
            logger.info("Admin user created: %s", settings.ADMIN_EMAIL)

    # Seed default presets
    await _seed_default_presets()

    # Seed default hardware profiles
    await _seed_default_hardware_profiles()

    logger.info("Platform started at %s", settings.BASE_URL)


async def _seed_default_presets():
    """Seed default game mechanic presets if none exist.

    Также гарантирует наличие пресета «Пазл» по имени (даже если таблица не пуста),
    т.к. клиентский BuildService.ResolveServerPresetIdAsync ищет серверный preset.id
    именно по точному совпадению name == «Пазл».
    """
    from app.models.models import Preset

    async with async_session_factory() as session:
        # Если хотя бы один пресет уже есть — отдельно убедимся, что «Пазл» среди них
        existing = await session.execute(select(Preset))
        if existing.scalars().first():
            puzzle_row = await session.execute(select(Preset).where(Preset.name == "Пазл"))
            if puzzle_row.scalar_one_or_none() is None:
                session.add(Preset(
                    name="Пазл",
                    description="Интерактивный пазл из загруженного изображения",
                    schema={
                        "type": "puzzle",
                        "slots": {"puzzle_image": {"type": "image", "required": True}},
                        "default_params": {"cols": 3, "rows": 3},
                    },
                    version="1.0.0",
                ))
                await session.commit()
                logger.info("Seeded missing Пазл preset")
            return

        presets = [
            Preset(
                name="Интерактивный каталог",
                description="Каталог товаров/экспонатов с возможностью навигации, фильтрации и детального просмотра",
                schema={
                    "type": "catalog",
                    "slots": {
                        "background": {"type": "image", "required": False, "description": "Фоновое изображение"},
                        "items": {"type": "image", "required": True, "multiple": True, "description": "Изображения элементов каталога"},
                        "item_descriptions": {"type": "text", "required": False, "description": "Описания элементов (JSON)"},
                        "music": {"type": "audio", "required": False, "description": "Фоновая музыка"},
                    },
                    "default_params": {
                        "columns": 3,
                        "animation_speed": 0.5,
                        "enable_search": True,
                        "enable_zoom": True,
                        "transition_type": "fade",
                    },
                    "parameters_schema": {
                        "columns": {"type": "int", "min": 1, "max": 6},
                        "animation_speed": {"type": "float", "min": 0.1, "max": 2.0},
                        "enable_search": {"type": "bool"},
                        "enable_zoom": {"type": "bool"},
                        "transition_type": {"type": "enum", "values": ["fade", "slide", "zoom"]},
                    },
                },
                version="1.0.0",
            ),
            Preset(
                name="Пазл",
                description="Интерактивный пазл из загруженного изображения с настраиваемой сложностью",
                schema={
                    "type": "puzzle",
                    "slots": {
                        "puzzle_image": {"type": "image", "required": True, "description": "Изображение для пазла"},
                        "background": {"type": "image", "required": False, "description": "Фон"},
                        "complete_sound": {"type": "audio", "required": False, "description": "Звук завершения"},
                        "music": {"type": "audio", "required": False, "description": "Фоновая музыка"},
                    },
                    "default_params": {
                        "grid_size": 4,
                        "shuffle_intensity": 0.8,
                        "show_preview": True,
                        "time_limit_seconds": 0,
                        "snap_distance": 30,
                    },
                    "parameters_schema": {
                        "grid_size": {"type": "int", "min": 2, "max": 10, "required": True},
                        "shuffle_intensity": {"type": "float", "min": 0.1, "max": 1.0},
                        "show_preview": {"type": "bool"},
                        "time_limit_seconds": {"type": "int", "min": 0, "max": 600},
                        "snap_distance": {"type": "int", "min": 10, "max": 100},
                    },
                },
                version="1.0.0",
            ),
            Preset(
                name="Мини-игра с таймером",
                description="Игровая механика с таймером: нужно собрать/нажать объекты за ограниченное время",
                schema={
                    "type": "timer_game",
                    "slots": {
                        "background": {"type": "image", "required": True, "description": "Фон игрового поля"},
                        "targets": {"type": "image", "required": True, "multiple": True, "description": "Изображения целей"},
                        "click_sound": {"type": "audio", "required": False, "description": "Звук нажатия"},
                        "win_sound": {"type": "audio", "required": False, "description": "Звук победы"},
                        "lose_sound": {"type": "audio", "required": False, "description": "Звук проигрыша"},
                        "music": {"type": "audio", "required": False, "description": "Фоновая музыка"},
                    },
                    "default_params": {
                        "time_limit_seconds": 60,
                        "target_count": 10,
                        "spawn_interval": 2.0,
                        "target_lifetime": 3.0,
                        "score_per_target": 10,
                        "difficulty_ramp": True,
                    },
                    "parameters_schema": {
                        "time_limit_seconds": {"type": "int", "min": 10, "max": 300, "required": True},
                        "target_count": {"type": "int", "min": 3, "max": 50},
                        "spawn_interval": {"type": "float", "min": 0.5, "max": 10.0},
                        "target_lifetime": {"type": "float", "min": 1.0, "max": 15.0},
                        "score_per_target": {"type": "int", "min": 1, "max": 100},
                        "difficulty_ramp": {"type": "bool"},
                    },
                },
                version="1.0.0",
            ),
            Preset(
                name="3D-витрина",
                description="Интерактивная 3D-витрина для просмотра моделей с возможностью вращения и масштабирования",
                schema={
                    "type": "3d_viewer",
                    "slots": {
                        "model": {"type": "model_3d", "required": True, "description": "3D-модель (glTF/GLB)"},
                        "environment": {"type": "image", "required": False, "description": "HDRI или фон окружения"},
                        "info_text": {"type": "text", "required": False, "description": "Описание модели"},
                        "music": {"type": "audio", "required": False, "description": "Фоновая музыка"},
                    },
                    "default_params": {
                        "auto_rotate": True,
                        "rotation_speed": 0.5,
                        "enable_zoom": True,
                        "min_zoom": 0.5,
                        "max_zoom": 3.0,
                        "enable_pan": False,
                        "show_info_panel": True,
                        "lighting_preset": "studio",
                    },
                    "parameters_schema": {
                        "auto_rotate": {"type": "bool"},
                        "rotation_speed": {"type": "float", "min": 0.1, "max": 5.0},
                        "enable_zoom": {"type": "bool"},
                        "min_zoom": {"type": "float", "min": 0.1, "max": 1.0},
                        "max_zoom": {"type": "float", "min": 1.0, "max": 10.0},
                        "enable_pan": {"type": "bool"},
                        "show_info_panel": {"type": "bool"},
                        "lighting_preset": {"type": "enum", "values": ["studio", "outdoor", "dramatic", "neutral"]},
                    },
                },
                version="1.0.0",
            ),
            Preset(
                name="Викторина",
                description="Интерактивная викторина с вопросами, вариантами ответов и подсчётом баллов",
                schema={
                    "type": "quiz",
                    "slots": {
                        "background": {"type": "image", "required": False, "description": "Фон викторины"},
                        "question_images": {"type": "image", "required": False, "multiple": True, "description": "Изображения к вопросам"},
                        "correct_sound": {"type": "audio", "required": False, "description": "Звук правильного ответа"},
                        "wrong_sound": {"type": "audio", "required": False, "description": "Звук неправильного ответа"},
                        "questions_data": {"type": "text", "required": True, "description": "Вопросы и ответы (JSON)"},
                    },
                    "default_params": {
                        "time_per_question": 30,
                        "show_correct_answer": True,
                        "shuffle_questions": True,
                        "shuffle_answers": True,
                        "questions_count": 10,
                    },
                    "parameters_schema": {
                        "time_per_question": {"type": "int", "min": 5, "max": 120},
                        "show_correct_answer": {"type": "bool"},
                        "shuffle_questions": {"type": "bool"},
                        "shuffle_answers": {"type": "bool"},
                        "questions_count": {"type": "int", "min": 1, "max": 100},
                    },
                },
                version="1.0.0",
            ),
        ]

        for p in presets:
            session.add(p)
        await session.commit()
        logger.info("Seeded %d default presets", len(presets))


async def _seed_default_hardware_profiles():
    """Seed baseline hardware profiles so the «Новый проект» dropdown is non-empty."""
    from app.models.models import HardwareProfile

    async with async_session_factory() as session:
        result = await session.execute(select(HardwareProfile))
        if result.scalars().first():
            return

        profiles = [
            HardwareProfile(
                name="Стандартный ПК",
                cpu_class="Intel Core i5 / Ryzen 5",
                gpu_class="GTX 1060 / RX 580",
                memory_limit_mb=8192,
                texture_memory_mb=2048,
                target_browser="Chromium 120",
                screen_width=1920,
                screen_height=1080,
                notes="Базовый профиль для большинства десктопных стендов",
            ),
            HardwareProfile(
                name="Слабый киоск",
                cpu_class="Intel Celeron / Atom",
                gpu_class="Integrated graphics",
                memory_limit_mb=4096,
                texture_memory_mb=512,
                target_browser="Chromium 120",
                screen_width=1280,
                screen_height=720,
                notes="Минимальные требования: малый объём памяти, мобильный чипсет",
            ),
            HardwareProfile(
                name="Мощный стенд",
                cpu_class="Intel Core i7+ / Ryzen 7+",
                gpu_class="RTX 3060 / RX 6600 и выше",
                memory_limit_mb=16384,
                texture_memory_mb=4096,
                target_browser="Chromium 120",
                screen_width=3840,
                screen_height=2160,
                notes="Премиум-стенд: 4K, тяжёлые 3D-сцены и видео",
            ),
        ]

        for p in profiles:
            session.add(p)
        await session.commit()
        logger.info("Seeded %d default hardware profiles", len(profiles))


@app.get("/api/health")
async def health():
    return {"status": "ok", "version": "1.0.0"}
