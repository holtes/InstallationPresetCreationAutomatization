using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InteractiveClient.Network
{
    // ============================================================
    // DTO для REST API. Используется Newtonsoft.Json (snake_case).
    // ============================================================

    // --- Общие ---

    public class ApiError
    {
        [JsonProperty("detail")] public string Detail;
        [JsonProperty("code")] public string Code;
    }

    // --- Auth ---

    public class LoginRequest
    {
        [JsonProperty("email")] public string Email;
        [JsonProperty("password")] public string Password;
    }

    public class LoginResponse
    {
        [JsonProperty("access_token")] public string AccessToken;
        [JsonProperty("token_type")] public string TokenType;
        [JsonProperty("user")] public UserDto User;
    }

    public class UserDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("email")] public string Email;
        [JsonProperty("name")] public string Name;
        [JsonProperty("display_name")] public string DisplayName;
        [JsonProperty("role")] public string Role;
    }

    public class RegisterRequest
    {
        [JsonProperty("email")] public string Email;
        [JsonProperty("display_name")] public string DisplayName;
        [JsonProperty("password")] public string Password;
        [JsonProperty("role")] public string Role = "editor";
    }

    public class UserUpdateRequest
    {
        [JsonProperty("display_name", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName;
    }

    // --- Projects ---

    // Проектные DTO синхронизированы со схемой сервера (app/schemas/schemas.py).
    // Сервер использует поле `title` (не `name`) и числовой `target_profile_id`
    // (FK в hardware-profiles). Поля preset_id / thumbnail_url / asset_mapping /
    // parameters сервер не возвращает в /api/projects — их хранение/синк идёт через
    // /api/configurations и связанные эндпоинты, поэтому в DTO их нет.

    public class ProjectDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("description")] public string Description;
        [JsonProperty("status")] public string Status;             // draft | ready | published
        [JsonProperty("target_profile_id")] public int? TargetProfileId;
        [JsonProperty("created_at")] public DateTime CreatedAt;
        [JsonProperty("updated_at")] public DateTime UpdatedAt;
        [JsonProperty("owner_id")] public string OwnerId;
    }

    public class ProjectCreateRequest
    {
        [JsonProperty("title")] public string Title;
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
        [JsonProperty("target_profile_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetProfileId;
    }

    public class ProjectUpdateRequest
    {
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title;
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status;
        [JsonProperty("target_profile_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetProfileId;
    }

    // --- Assets ---
    // Синхронизировано с серверной схемой AssetOut (server/app/schemas/schemas.py).
    // Сервер не возвращает project_id/url/thumbnail_url/type/mime_type/llm_metadata —
    // тип определяется на клиенте по content_type, привязка к проекту идёт через
    // отдельную ProjectAsset-таблицу.

    public class AssetDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("uploader_id")] public string UploaderId;
        [JsonProperty("filename")] public string Filename;
        [JsonProperty("content_type")] public string ContentType;
        [JsonProperty("size_bytes")] public long SizeBytes;
        [JsonProperty("hash")] public string Hash;
        [JsonProperty("width")] public int? Width;
        [JsonProperty("height")] public int? Height;
        [JsonProperty("duration_seconds")] public double? DurationSeconds;
        [JsonProperty("polygon_count")] public int? PolygonCount;
        [JsonProperty("metadata_json")] public JObject MetadataJson;
        [JsonProperty("status")] public string Status;
        [JsonProperty("created_at")] public DateTime CreatedAt;
    }

    // --- Presets ---

    public class PresetDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("version")] public string Version;
        [JsonProperty("tags")] public List<string> Tags;
        [JsonProperty("thumbnail_url")] public string ThumbnailUrl;
    }

    /// <summary>JSON-схема параметров пресета (слоты + параметры).</summary>
    public class PresetSchemaDto
    {
        [JsonProperty("preset_id")] public string PresetId;
        [JsonProperty("version")] public string Version;
        [JsonProperty("slots")] public List<SlotSchemaDto> Slots;
        [JsonProperty("parameters")] public List<ParameterSchemaDto> Parameters;
    }

    public class SlotSchemaDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;              // human-readable
        [JsonProperty("type")] public string Type;              // image/video/audio/model_3d/text
        [JsonProperty("required")] public bool Required;
        [JsonProperty("group")] public string Group;            // "Основные", "Аудио", ...
        [JsonProperty("description")] public string Description;
        [JsonProperty("dynamic")] public bool Dynamic;          // если N слотов (event_media_1..N)
        [JsonProperty("min_count")] public int? MinCount;
    }

    public class ParameterSchemaDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("type")] public string Type;              // int/float/bool/string/enum/color/vector2/vector3/range/text_area
        [JsonProperty("default")] public JToken Default;
        [JsonProperty("min")] public double? Min;
        [JsonProperty("max")] public double? Max;
        [JsonProperty("choices")] public List<string> Choices;  // для enum
        [JsonProperty("group")] public string Group;            // "Геймплей", "Визуал", "Таймеры", "Продвинутые"
    }

    // --- Configurations ---

    public class ConfigurationRequest
    {
        [JsonProperty("project_id")] public string ProjectId;
        [JsonProperty("preset_id")] public string PresetId;
        [JsonProperty("preset_version")] public string PresetVersion;
        [JsonProperty("hardware_profile_id")] public string HardwareProfileId;
        [JsonProperty("build_type")] public string BuildType;    // light | standard | full
        [JsonProperty("asset_mapping")] public Dictionary<string, string> AssetMapping;
        [JsonProperty("parameters")] public JObject Parameters;
    }

    public class ConfigurationResponse
    {
        [JsonProperty("configuration_id")] public string ConfigurationId;
    }

    // --- Builds ---

    public class BuildRequest
    {
        [JsonProperty("configuration_id")] public string ConfigurationId;
    }

    public class BuildResponse
    {
        [JsonProperty("build_id")] public string BuildId;
    }

    public class BuildStatusResponse
    {
        [JsonProperty("build_id")] public string BuildId;
        [JsonProperty("status")] public string Status;          // queued | running | success | error | cancelled
        [JsonProperty("stage")] public string Stage;            // текущий этап
        [JsonProperty("progress")] public float Progress;       // 0..1
        [JsonProperty("log")] public string Log;
        [JsonProperty("error_message")] public string ErrorMessage;
    }

    public class BuildResultResponse
    {
        [JsonProperty("build_id")] public string BuildId;
        [JsonProperty("url")] public string Url;
        [JsonProperty("download_url")] public string DownloadUrl;
        [JsonProperty("iframe_code")] public string IframeCode;
        [JsonProperty("size_bytes")] public long SizeBytes;
        [JsonProperty("build_time_seconds")] public int BuildTimeSeconds;
        [JsonProperty("expected_fps")] public int ExpectedFps;
        [JsonProperty("optimizations")] public List<string> Optimizations;
    }

    // --- Server-shape DTO для /projects/{id}/configurations/generate и /builds/ ---
    // Они отличаются от исторических ConfigurationRequest / BuildRequest, которые
    // соответствовали старой клиентской модели. Сервер сейчас принимает именно эти.

    public class ConfigurationGenerateRequest
    {
        [JsonProperty("preset_id")]     public int PresetId;
        [JsonProperty("asset_mapping")] public Dictionary<string, int> AssetMapping;
        [JsonProperty("params")]        public JObject Params;
    }

    public class ConfigurationOutDto
    {
        [JsonProperty("id")]          public int Id;
        [JsonProperty("project_id")]  public int ProjectId;
        [JsonProperty("preset_id")]   public int PresetId;
        [JsonProperty("config_json")] public JObject ConfigJson;
        [JsonProperty("status")]      public string Status;
    }

    public class BuildJobCreateDto
    {
        [JsonProperty("configuration_id")]   public int ConfigurationId;
        [JsonProperty("target_profile_id")]  public int? TargetProfileId;
        [JsonProperty("priority")]           public int Priority;
    }

    public class BuildJobOutDto
    {
        [JsonProperty("id")]                 public int Id;
        [JsonProperty("configuration_id")]   public int ConfigurationId;
        [JsonProperty("requested_by")]       public int RequestedBy;
        [JsonProperty("target_profile_id")]  public int? TargetProfileId;
        [JsonProperty("status")]             public string Status;       // queued | running | success | failed
        [JsonProperty("started_at")]         public DateTime? StartedAt;
        [JsonProperty("finished_at")]        public DateTime? FinishedAt;
        [JsonProperty("logs_summary")]       public string LogsSummary;
    }

    public class BuildArtifactOutDto
    {
        [JsonProperty("id")]                  public int Id;
        [JsonProperty("build_job_id")]        public int BuildJobId;
        [JsonProperty("artifact_url")]        public string ArtifactUrl;
        [JsonProperty("size_bytes")]          public long? SizeBytes;
        [JsonProperty("bundle_hash")]         public string BundleHash;
        [JsonProperty("build_time_seconds")]  public float? BuildTimeSeconds;
        [JsonProperty("iframe_code")]         public string IframeCode;
    }

    // --- LLM ---

    public class LlmSuggestMappingRequest
    {
        [JsonProperty("preset_id")] public string PresetId;
        [JsonProperty("asset_ids")] public List<string> AssetIds;
    }

    public class LlmSuggestMappingResponse
    {
        [JsonProperty("mapping")] public Dictionary<string, string> Mapping;
        [JsonProperty("confidence")] public Dictionary<string, float> Confidence;
        [JsonProperty("explanation")] public string Explanation;
    }

    public class LlmMetadataRequest
    {
        [JsonProperty("asset_id")] public string AssetId;
    }

    public class LlmMetadataResponse
    {
        [JsonProperty("description")] public string Description;
        [JsonProperty("tags")] public List<string> Tags;
        [JsonProperty("suggested_role")] public string SuggestedRole;
    }

    // --- Hardware profiles ---

    public class HardwareProfileDto
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("cpu_class")] public string CpuClass;
        [JsonProperty("gpu_class")] public string GpuClass;
        [JsonProperty("memory_limit_mb")] public int? MemoryLimitMb;
        [JsonProperty("texture_memory_mb")] public int? TextureMemoryMb;
        [JsonProperty("target_browser")] public string TargetBrowser;
        [JsonProperty("screen_width")] public int? ScreenWidth;
        [JsonProperty("screen_height")] public int? ScreenHeight;
        [JsonProperty("notes")] public string Notes;
    }
}
