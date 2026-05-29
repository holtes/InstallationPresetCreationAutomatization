namespace InteractiveClient.Network
{
    /// <summary>Константы маршрутов REST API (относительно BaseUrl).</summary>
    public static class ApiEndpoints
    {
        // --- Auth ---
        public const string Login = "/api/auth/login";
        public const string Register = "/api/auth/register";
        public const string Logout = "/api/auth/logout";
        public const string Me = "/api/auth/me";

        // --- Users ---
        public static string UserById(string id) => $"/api/users/{id}";

        // --- Projects ---
        public const string Projects = "/api/projects";
        public static string ProjectById(string id) => $"/api/projects/{id}";

        // --- Assets ---
        public const string AssetUpload = "/api/assets/upload";
        // NOTE: серверный GET /api/assets/ возвращает все ассеты пользователя списком.
        // project_id query-параметр сервер сейчас игнорирует — оставлен на будущее.
        public static string AssetsByProject(string projectId) => "/api/assets/";
        public static string AssetById(string id) => $"/api/assets/{id}";
        public static string AssetFile(string id) => $"/api/assets/{id}/file";

        // --- Presets ---
        public const string Presets = "/api/presets";
        public static string PresetSchema(string presetId) => $"/api/presets/{presetId}/schema";

        // --- Configurations ---
        // Сервер: /projects/{project_id}/configurations/* — конфигурации привязаны к проекту.
        // /generate — авто-сборка config_json из проекта/маппинга/параметров.
        public static string ConfigurationGenerate(string projectId)
            => $"/api/projects/{projectId}/configurations/generate";

        // --- Builds ---
        // Trailing slash обязателен — FastAPI '/' эндпоинт не редиректит UnityWebRequest.
        public const string Builds = "/api/builds/";
        public static string BuildJob(string buildId)        => $"/api/builds/{buildId}";
        public static string BuildArtifacts(string buildId)  => $"/api/builds/{buildId}/artifacts";
        public static string BuildLogs(string buildId)       => $"/api/builds/{buildId}/logs";

        // --- LLM ---
        public const string LlmSuggestMapping = "/api/llm/suggest-mapping";
        public const string LlmGenerateMetadata = "/api/llm/generate-metadata";

        // --- Hardware profiles ---
        // NOTE: trailing slash is required — FastAPI 307-редирект на POST/GET без слэша
        // не сохраняет тело/метод в UnityWebRequest. Бьём напрямую в /api/hardware-profiles/.
        public const string HardwareProfiles = "/api/hardware-profiles/";
    }
}
