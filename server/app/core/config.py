from pathlib import Path
from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    # Database
    DATABASE_URL: str = "postgresql+asyncpg://postgres:postgres@localhost:5432/installations_platform"
    DATABASE_URL_SYNC: str = "postgresql+psycopg2://postgres:postgres@localhost:5432/installations_platform"

    # Redis
    REDIS_URL: str = "redis://localhost:6379/0"

    # JWT
    SECRET_KEY: str = "change-me-to-a-long-random-string"
    ALGORITHM: str = "HS256"
    ACCESS_TOKEN_EXPIRE_MINUTES: int = 1440

    # Google Gemini (free tier)
    GEMINI_API_KEY: str = ""
    GEMINI_MODEL: str = "gemini-2.0-flash"

    # Unity
    UNITY_EXECUTABLE: str = "/opt/unity/Editor/Unity"
    UNITY_PROJECT_PATH: str = "/opt/unity-project"

    # Storage
    BUILDS_DIR: str = "/app/storage/builds"
    ASSETS_DIR: str = "/app/storage/assets"

    # Server
    SERVER_HOST: str = "0.0.0.0"
    SERVER_PORT: int = 8000
    BASE_URL: str = "http://localhost:8000"
    CORS_ORIGINS: list[str] = ["*"]

    # Admin seed
    ADMIN_EMAIL: str = "admin@example.com"
    ADMIN_PASSWORD: str = "admin"

    model_config = {"env_file": ".env", "extra": "ignore"}


settings = Settings()

# Ensure storage dirs exist
Path(settings.ASSETS_DIR).mkdir(parents=True, exist_ok=True)
Path(settings.BUILDS_DIR).mkdir(parents=True, exist_ok=True)
