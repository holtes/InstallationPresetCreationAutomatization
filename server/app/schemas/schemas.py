from datetime import datetime

from pydantic import BaseModel, EmailStr, Field


# ==================== Auth ====================

class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"


class LoginRequest(BaseModel):
    email: str
    password: str


# ==================== User ====================

class UserCreate(BaseModel):
    email: EmailStr
    display_name: str = Field(min_length=1, max_length=255)
    password: str = Field(min_length=6)
    role: str = "editor"


class UserUpdate(BaseModel):
    display_name: str | None = None
    role: str | None = None
    is_active: bool | None = None


class UserOut(BaseModel):
    id: int
    email: str
    display_name: str
    role: str
    is_active: bool
    created_at: datetime

    model_config = {"from_attributes": True}


# ==================== Hardware Profile ====================

class HardwareProfileCreate(BaseModel):
    name: str = Field(min_length=1, max_length=255)
    cpu_class: str | None = None
    gpu_class: str | None = None
    memory_limit_mb: int | None = None
    texture_memory_mb: int | None = None
    target_browser: str | None = None
    screen_width: int | None = None
    screen_height: int | None = None
    notes: str | None = None


class HardwareProfileUpdate(BaseModel):
    name: str | None = None
    cpu_class: str | None = None
    gpu_class: str | None = None
    memory_limit_mb: int | None = None
    texture_memory_mb: int | None = None
    target_browser: str | None = None
    screen_width: int | None = None
    screen_height: int | None = None
    notes: str | None = None


class HardwareProfileOut(BaseModel):
    id: int
    name: str
    cpu_class: str | None
    gpu_class: str | None
    memory_limit_mb: int | None
    texture_memory_mb: int | None
    target_browser: str | None
    screen_width: int | None
    screen_height: int | None
    notes: str | None
    created_at: datetime

    model_config = {"from_attributes": True}


# ==================== Preset ====================

class PresetCreate(BaseModel):
    name: str = Field(min_length=1, max_length=255)
    description: str | None = None
    schema_: dict = Field(default_factory=dict, alias="schema")
    thumbnail_url: str | None = None
    version: str = "1.0.0"

    model_config = {"populate_by_name": True}


class PresetUpdate(BaseModel):
    name: str | None = None
    description: str | None = None
    schema_: dict | None = Field(default=None, alias="schema")
    thumbnail_url: str | None = None
    version: str | None = None

    model_config = {"populate_by_name": True}


class PresetOut(BaseModel):
    id: int
    name: str
    description: str | None
    schema_: dict = Field(validation_alias="schema", serialization_alias="schema")
    thumbnail_url: str | None
    version: str
    created_at: datetime

    model_config = {"from_attributes": True, "populate_by_name": True}


# ==================== Project ====================

class ProjectCreate(BaseModel):
    title: str = Field(min_length=1, max_length=255)
    description: str | None = None
    target_profile_id: int | None = None


class ProjectUpdate(BaseModel):
    title: str | None = None
    description: str | None = None
    status: str | None = None
    target_profile_id: int | None = None


class ProjectOut(BaseModel):
    id: int
    owner_id: int
    title: str
    description: str | None
    status: str
    target_profile_id: int | None
    created_at: datetime
    updated_at: datetime

    model_config = {"from_attributes": True}


# ==================== Asset ====================

class AssetOut(BaseModel):
    id: int
    uploader_id: int
    filename: str
    content_type: str | None
    size_bytes: int | None
    hash: str | None
    width: int | None
    height: int | None
    duration_seconds: float | None
    polygon_count: int | None
    metadata_json: dict | None
    status: str
    created_at: datetime

    model_config = {"from_attributes": True}


# ==================== ProjectAsset ====================

class ProjectAssetCreate(BaseModel):
    asset_id: int
    role_in_project: str | None = None
    slot_key: str | None = None


class ProjectAssetOut(BaseModel):
    id: int
    project_id: int
    asset_id: int
    role_in_project: str | None
    slot_key: str | None

    model_config = {"from_attributes": True}


# ==================== Configuration ====================

class ConfigurationCreate(BaseModel):
    preset_id: int
    config_json: dict = Field(default_factory=dict)


class ConfigurationUpdate(BaseModel):
    config_json: dict | None = None
    status: str | None = None


class ConfigurationOut(BaseModel):
    id: int
    project_id: int
    preset_id: int
    config_json: dict
    created_by: int
    status: str
    created_at: datetime

    model_config = {"from_attributes": True}


# ==================== Build ====================

class BuildJobCreate(BaseModel):
    configuration_id: int
    target_profile_id: int | None = None
    priority: int = 0


class BuildJobOut(BaseModel):
    id: int
    configuration_id: int
    requested_by: int
    target_profile_id: int | None
    status: str
    started_at: datetime | None
    finished_at: datetime | None
    logs_summary: str | None
    attempts: int
    priority: int
    celery_task_id: str | None
    created_at: datetime

    model_config = {"from_attributes": True}


class BuildArtifactOut(BaseModel):
    id: int
    build_job_id: int
    artifact_url: str
    size_bytes: int | None
    bundle_hash: str | None
    build_time_seconds: float | None
    optimizations_applied: dict | None
    iframe_code: str | None
    created_at: datetime

    model_config = {"from_attributes": True}


class BuildLogOut(BaseModel):
    id: int
    build_job_id: int
    log_type: str
    message: str
    timestamp: datetime

    model_config = {"from_attributes": True}


# ==================== LLM ====================

class LLMMapAssetsRequest(BaseModel):
    project_id: int
    preset_id: int


class LLMGenerateMetadataRequest(BaseModel):
    project_id: int


class LLMSuggestParamsRequest(BaseModel):
    project_id: int
    preset_id: int


class LLMRequestOut(BaseModel):
    id: int
    project_id: int
    request_type: str | None
    response_payload: dict | None
    confidence: float | None
    tokens_used: int | None
    created_at: datetime

    model_config = {"from_attributes": True}


# ==================== Webhook ====================

class WebhookCreate(BaseModel):
    name: str = Field(min_length=1, max_length=255)
    endpoint_url: str
    secret: str | None = None
    event_type: str


class WebhookUpdate(BaseModel):
    name: str | None = None
    endpoint_url: str | None = None
    secret: str | None = None
    event_type: str | None = None
    is_active: bool | None = None


class WebhookOut(BaseModel):
    id: int
    name: str
    endpoint_url: str
    event_type: str
    is_active: bool
    last_status: int | None
    last_triggered_at: datetime | None
    created_at: datetime

    model_config = {"from_attributes": True}
