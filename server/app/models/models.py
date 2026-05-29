import enum
from datetime import datetime

from sqlalchemy import (
    Boolean,
    Column,
    DateTime,
    Enum,
    Float,
    ForeignKey,
    Integer,
    String,
    Text,
    func,
)
from sqlalchemy.dialects.postgresql import JSON
from sqlalchemy.orm import relationship

from app.core.database import Base


# ---------- Enums ----------

class UserRole(str, enum.Enum):
    admin = "admin"
    editor = "editor"


class ProjectStatus(str, enum.Enum):
    draft = "draft"
    configuring = "configuring"
    ready = "ready"
    building = "building"
    published = "published"
    archived = "archived"


class AssetStatus(str, enum.Enum):
    uploading = "uploading"
    validating = "validating"
    ready = "ready"
    error = "error"


class ConfigurationStatus(str, enum.Enum):
    draft = "draft"
    validated = "validated"
    building = "building"
    built = "built"
    error = "error"


class BuildStatus(str, enum.Enum):
    queued = "queued"
    running = "running"
    failed = "failed"
    success = "success"


class BuildLogType(str, enum.Enum):
    info = "info"
    warning = "warning"
    error = "error"


class WebhookType(str, enum.Enum):
    build_started = "build_started"
    build_completed = "build_completed"
    build_failed = "build_failed"


# ---------- Models ----------

class User(Base):
    __tablename__ = "users"

    id = Column(Integer, primary_key=True, index=True)
    email = Column(String(255), unique=True, nullable=False, index=True)
    display_name = Column(String(255), nullable=False)
    password_hash = Column(String(255), nullable=False)
    role = Column(Enum(UserRole), nullable=False, default=UserRole.editor)
    is_active = Column(Boolean, default=True)
    created_at = Column(DateTime, server_default=func.now())
    updated_at = Column(DateTime, server_default=func.now(), onupdate=func.now())

    projects = relationship("Project", back_populates="owner", foreign_keys="Project.owner_id")
    assets = relationship("Asset", back_populates="uploader")


class HardwareProfile(Base):
    __tablename__ = "hardware_profiles"

    id = Column(Integer, primary_key=True, index=True)
    name = Column(String(255), unique=True, nullable=False)
    cpu_class = Column(String(100))
    gpu_class = Column(String(100))
    memory_limit_mb = Column(Integer)
    texture_memory_mb = Column(Integer)
    target_browser = Column(String(100))
    screen_width = Column(Integer)
    screen_height = Column(Integer)
    notes = Column(Text)
    created_at = Column(DateTime, server_default=func.now())

    projects = relationship("Project", back_populates="target_profile")
    build_jobs = relationship("BuildJob", back_populates="target_profile")


class Preset(Base):
    __tablename__ = "presets"

    id = Column(Integer, primary_key=True, index=True)
    name = Column(String(255), unique=True, nullable=False)
    description = Column(Text)
    schema = Column(JSON, nullable=False, default=dict)
    thumbnail_url = Column(String(512))
    version = Column(String(50), default="1.0.0")
    created_at = Column(DateTime, server_default=func.now())

    configurations = relationship("Configuration", back_populates="preset")


class Project(Base):
    __tablename__ = "projects"

    id = Column(Integer, primary_key=True, index=True)
    owner_id = Column(Integer, ForeignKey("users.id"), nullable=False)
    title = Column(String(255), nullable=False)
    description = Column(Text)
    status = Column(Enum(ProjectStatus), default=ProjectStatus.draft)
    target_profile_id = Column(Integer, ForeignKey("hardware_profiles.id"), nullable=True)
    created_at = Column(DateTime, server_default=func.now())
    updated_at = Column(DateTime, server_default=func.now(), onupdate=func.now())

    owner = relationship("User", back_populates="projects", foreign_keys=[owner_id])
    target_profile = relationship("HardwareProfile", back_populates="projects")
    project_assets = relationship("ProjectAsset", back_populates="project", cascade="all, delete-orphan")
    configurations = relationship("Configuration", back_populates="project", cascade="all, delete-orphan")
    llm_requests = relationship("LLMRequest", back_populates="project", cascade="all, delete-orphan")


class Asset(Base):
    __tablename__ = "assets"

    id = Column(Integer, primary_key=True, index=True)
    uploader_id = Column(Integer, ForeignKey("users.id"), nullable=False)
    filename = Column(String(512), nullable=False)
    stored_path = Column(String(1024), nullable=False)
    content_type = Column(String(100))
    size_bytes = Column(Integer)
    hash = Column(String(128))
    width = Column(Integer)
    height = Column(Integer)
    duration_seconds = Column(Float)
    polygon_count = Column(Integer)
    metadata_json = Column(JSON, default=dict)
    status = Column(Enum(AssetStatus), default=AssetStatus.uploading)
    created_at = Column(DateTime, server_default=func.now())

    uploader = relationship("User", back_populates="assets")
    project_assets = relationship("ProjectAsset", back_populates="asset")


class ProjectAsset(Base):
    __tablename__ = "project_assets"

    id = Column(Integer, primary_key=True, index=True)
    project_id = Column(Integer, ForeignKey("projects.id", ondelete="CASCADE"), nullable=False)
    asset_id = Column(Integer, ForeignKey("assets.id"), nullable=False)
    role_in_project = Column(String(100))
    slot_key = Column(String(255))

    project = relationship("Project", back_populates="project_assets")
    asset = relationship("Asset", back_populates="project_assets")


class Configuration(Base):
    __tablename__ = "configurations"

    id = Column(Integer, primary_key=True, index=True)
    project_id = Column(Integer, ForeignKey("projects.id", ondelete="CASCADE"), nullable=False)
    preset_id = Column(Integer, ForeignKey("presets.id"), nullable=False)
    config_json = Column(JSON, nullable=False, default=dict)
    created_by = Column(Integer, ForeignKey("users.id"), nullable=False)
    status = Column(Enum(ConfigurationStatus), default=ConfigurationStatus.draft)
    created_at = Column(DateTime, server_default=func.now())

    project = relationship("Project", back_populates="configurations")
    preset = relationship("Preset", back_populates="configurations")
    creator = relationship("User", foreign_keys=[created_by])
    build_jobs = relationship("BuildJob", back_populates="configuration", cascade="all, delete-orphan")


class BuildJob(Base):
    __tablename__ = "build_jobs"

    id = Column(Integer, primary_key=True, index=True)
    configuration_id = Column(Integer, ForeignKey("configurations.id", ondelete="CASCADE"), nullable=False)
    requested_by = Column(Integer, ForeignKey("users.id"), nullable=False)
    target_profile_id = Column(Integer, ForeignKey("hardware_profiles.id"), nullable=True)
    status = Column(Enum(BuildStatus), default=BuildStatus.queued)
    started_at = Column(DateTime)
    finished_at = Column(DateTime)
    logs_summary = Column(Text)
    attempts = Column(Integer, default=0)
    priority = Column(Integer, default=0)
    celery_task_id = Column(String(255))
    created_at = Column(DateTime, server_default=func.now())

    configuration = relationship("Configuration", back_populates="build_jobs")
    requester = relationship("User", foreign_keys=[requested_by])
    target_profile = relationship("HardwareProfile", back_populates="build_jobs")
    artifacts = relationship("BuildArtifact", back_populates="build_job", cascade="all, delete-orphan")
    logs = relationship("BuildLog", back_populates="build_job", cascade="all, delete-orphan")


class BuildArtifact(Base):
    __tablename__ = "build_artifacts"

    id = Column(Integer, primary_key=True, index=True)
    build_job_id = Column(Integer, ForeignKey("build_jobs.id", ondelete="CASCADE"), nullable=False)
    artifact_url = Column(String(1024), nullable=False)
    artifact_path = Column(String(1024), nullable=False)
    size_bytes = Column(Integer)
    bundle_hash = Column(String(128))
    build_time_seconds = Column(Float)
    optimizations_applied = Column(JSON, default=dict)
    iframe_code = Column(Text)
    created_at = Column(DateTime, server_default=func.now())

    build_job = relationship("BuildJob", back_populates="artifacts")


class BuildLog(Base):
    __tablename__ = "build_logs"

    id = Column(Integer, primary_key=True, index=True)
    build_job_id = Column(Integer, ForeignKey("build_jobs.id", ondelete="CASCADE"), nullable=False)
    log_type = Column(Enum(BuildLogType), default=BuildLogType.info)
    message = Column(Text, nullable=False)
    timestamp = Column(DateTime, server_default=func.now())

    build_job = relationship("BuildJob", back_populates="logs")


class LLMRequest(Base):
    __tablename__ = "llm_requests"

    id = Column(Integer, primary_key=True, index=True)
    project_id = Column(Integer, ForeignKey("projects.id", ondelete="CASCADE"), nullable=False)
    request_type = Column(String(100))
    request_payload = Column(JSON, nullable=False)
    response_payload = Column(JSON)
    confidence = Column(Float)
    tokens_used = Column(Integer)
    created_at = Column(DateTime, server_default=func.now())

    project = relationship("Project", back_populates="llm_requests")


class Webhook(Base):
    __tablename__ = "webhooks"

    id = Column(Integer, primary_key=True, index=True)
    name = Column(String(255), nullable=False)
    endpoint_url = Column(String(1024), nullable=False)
    secret = Column(String(255))
    event_type = Column(Enum(WebhookType), nullable=False)
    is_active = Column(Boolean, default=True)
    last_status = Column(Integer)
    last_triggered_at = Column(DateTime)
    created_at = Column(DateTime, server_default=func.now())
