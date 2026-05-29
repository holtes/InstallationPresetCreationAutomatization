using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Network;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InteractiveClient.Build
{
    public enum BuildType { Light, Standard, Full }

    public static class BuildTypeExtensions
    {
        public static string ToApiString(this BuildType t) => t switch
        {
            BuildType.Light => "light",
            BuildType.Standard => "standard",
            BuildType.Full => "full",
            _ => "standard"
        };
    }

    /// <summary>
    /// Запуск сборки. Серверный пайплайн:
    ///  1. POST /api/projects/{project_id}/configurations/generate
    ///     body: { preset_id: int, asset_mapping: dict[str,int], params: dict }
    ///     response: ConfigurationOutDto (с id: int)
    ///  2. POST /api/builds/
    ///     body: { configuration_id: int, target_profile_id?: int, priority: int }
    ///     response: BuildJobOutDto (с id: int)
    ///
    /// Маппинг типа билда (light/standard/full) сервер пока не использует —
    /// складываем его в params, чтобы будущий генератор мог учесть.
    /// </summary>
    public class BuildService
    {
        private readonly ApiClient api;

        public BuildService(ApiClient api) { this.api = api; }

        public async Task<string> StartBuildAsync(
            ProjectModel project, BuildType buildType, CancellationToken ct = default)
        {
            if (project == null || string.IsNullOrEmpty(project.Id))
                throw new Exception("Проект не сохранён — нет id.");
            if (string.IsNullOrEmpty(project.PresetId))
                throw new Exception("Пресет не выбран.");

            // 1. Резолвим клиентский preset_id (string) → серверный preset.id (int)
            int serverPresetId = await ResolveServerPresetIdAsync(project.PresetId, ct);

            // 2. Конвертируем asset_mapping: server ждёт dict[str, int]
            var assetMapping = new Dictionary<string, int>();
            foreach (var kv in project.AssetMapping)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (int.TryParse(kv.Value, out var aid))
                    assetMapping[kv.Key] = aid;
            }

            // 3. Параметры + build_type
            var paramsObj = project.Parameters != null
                ? (JObject)project.Parameters.DeepClone()
                : new JObject();
            paramsObj["build_type"] = buildType.ToApiString();

            // 4. POST /api/projects/{id}/configurations/generate
            var configReq = new ConfigurationGenerateRequest
            {
                PresetId     = serverPresetId,
                AssetMapping = assetMapping,
                Params       = paramsObj
            };

            var config = await api.PostAsync<ConfigurationGenerateRequest, ConfigurationOutDto>(
                ApiEndpoints.ConfigurationGenerate(project.Id), configReq, ct);

            if (config == null || config.Id <= 0)
                throw new Exception("Сервер не вернул конфигурацию.");

            // 5. POST /api/builds/
            var buildReq = new BuildJobCreateDto
            {
                ConfigurationId  = config.Id,
                TargetProfileId  = project.TargetProfileId,
                Priority         = 0
            };

            var buildJob = await api.PostAsync<BuildJobCreateDto, BuildJobOutDto>(
                ApiEndpoints.Builds, buildReq, ct);

            if (buildJob == null || buildJob.Id <= 0)
                throw new Exception("Сервер не вернул build_id.");

            return buildJob.Id.ToString();
        }

        /// <summary>
        /// На сервере у каждого пресета свой DB int-id. На клиенте мы в качестве
        /// project.PresetId храним строку («puzzle» / «memory» / …) — это либо
        /// ID серверного пресета, либо локальный псевдо-id из LocalPresetSchemas.
        /// Здесь сначала пробуем распарсить как int, потом — поискать серверный
        /// пресет по имени, которое отдаёт LocalPresetSchemas.
        /// </summary>
        private async Task<int> ResolveServerPresetIdAsync(string clientPresetId, CancellationToken ct)
        {
            if (int.TryParse(clientPresetId, out var directId)) return directId;

            // Подгружаем актуальный список с сервера (если ещё не загружен)
            var registry = ServiceLocator.Get<PresetRegistry>();
            try
            {
                if (registry.All.Count == 0)
                    await registry.LoadListAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildService] PresetRegistry недоступен: {ex.Message}");
            }

            // Имя локального пресета (например "puzzle" → "Пазл")
            var local = LocalPresetSchemas.Get(clientPresetId);
            string targetName = local?.Name ?? clientPresetId;

            // Ищем серверный пресет по точному совпадению имени
            var serverPreset = registry.All.FirstOrDefault(p =>
                string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (serverPreset != null && int.TryParse(serverPreset.Id, out var resolvedId))
                return resolvedId;

            throw new Exception(
                $"На сервере не найден пресет с именем «{targetName}». " +
                "Возможно, серверный пресет-словарь рассинхронизирован с клиентским.");
        }

        // ── Status / result ────────────────────────────────────────────────────

        public async Task<BuildStatusResponse> GetStatusAsync(string buildId, CancellationToken ct = default)
        {
            var job = await api.GetAsync<BuildJobOutDto>(ApiEndpoints.BuildJob(buildId), ct);
            if (job == null) return null;

            // Сервер не отдаёт прогресс/этап/лог — синтезируем по статусу.
            float progress = job.Status switch
            {
                "queued"  => 0.05f,
                "running" => 0.5f,
                "success" => 1f,
                "failed"  => 1f,
                _         => 0f
            };
            string stage = job.Status switch
            {
                "queued"  => "В очереди",
                "running" => "Сборка идёт",
                "success" => "Готово",
                "failed"  => "Ошибка",
                _         => job.Status ?? ""
            };

            return new BuildStatusResponse
            {
                BuildId      = job.Id.ToString(),
                Status       = NormalizeStatus(job.Status),
                Stage        = stage,
                Progress     = progress,
                Log          = job.LogsSummary,
                ErrorMessage = job.Status == "failed" ? (job.LogsSummary ?? "Сборка завершилась с ошибкой.") : null
            };
        }

        public async Task<BuildResultResponse> GetResultAsync(string buildId, CancellationToken ct = default)
        {
            var artifacts = await api.GetAsync<List<BuildArtifactOutDto>>(
                ApiEndpoints.BuildArtifacts(buildId), ct);

            // Берём первый артефакт — обычно это собранный WebGL-бандл.
            var main = artifacts?.FirstOrDefault();
            if (main == null) return new BuildResultResponse { BuildId = buildId };

            // artifact_url может быть относительным («/builds/abc.zip») — превращаем в абсолютный
            string url = main.ArtifactUrl;
            if (!string.IsNullOrEmpty(url) &&
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = $"{api.BaseUrl}{(url.StartsWith("/") ? url : "/" + url)}";
            }

            return new BuildResultResponse
            {
                BuildId           = buildId,
                Url               = url,
                DownloadUrl       = url,
                IframeCode        = main.IframeCode,
                SizeBytes         = main.SizeBytes ?? 0,
                BuildTimeSeconds  = main.BuildTimeSeconds.HasValue
                    ? Mathf.RoundToInt(main.BuildTimeSeconds.Value) : 0,
                ExpectedFps       = 60,
                Optimizations     = new List<string>()
            };
        }

        /// <summary>
        /// Сервер использует «failed», клиент-трекер исторически ждёт «error» —
        /// нормализуем чтобы не дублировать switch'и.
        /// </summary>
        private static string NormalizeStatus(string s) => s switch
        {
            "failed" => "error",
            _        => s ?? "queued"
        };
    }
}
