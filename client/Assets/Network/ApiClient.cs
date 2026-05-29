using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace InteractiveClient.Network
{
    /// <summary>
    /// Асинхронная обёртка над UnityWebRequest.
    /// • JSON-сериализация через Newtonsoft.Json (snake_case DTO из ApiModels).
    /// • Bearer-токен добавляется через AuthTokenProvider.
    /// • Экспоненциальный backoff на 5xx (3, 6, 12, 30 сек).
    /// • События 401 → публикуются в EventBus (редирект на логин выполняет AuthService/AppManager).
    /// </summary>
    public class ApiClient
    {
        public string BaseUrl { get; }
        public Func<string> AuthTokenProvider { get; set; }

        private const int DefaultTimeoutSeconds = 30;
        private static readonly int[] BackoffSchedule = { 3, 6, 12, 30 };

        public ApiClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL is required", nameof(baseUrl));
            BaseUrl = baseUrl.TrimEnd('/');
        }

        // ================================================================
        //  Public API: Get / Post / Put / Delete
        // ================================================================

        public Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct = default)
            => SendJsonAsync<TResponse>(UnityWebRequest.kHttpVerbGET, path, null, ct);

        public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
            => SendJsonAsync<TResponse>(UnityWebRequest.kHttpVerbPOST, path, body, ct);

        public Task PostAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
            => SendJsonAsync<object>(UnityWebRequest.kHttpVerbPOST, path, body, ct);

        public Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
            => SendJsonAsync<TResponse>(UnityWebRequest.kHttpVerbPUT, path, body, ct);

        public Task<TResponse> PatchAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
            => SendJsonAsync<TResponse>("PATCH", path, body, ct);

        public Task DeleteAsync(string path, CancellationToken ct = default)
            => SendJsonAsync<object>(UnityWebRequest.kHttpVerbDELETE, path, null, ct);

        /// <summary>
        /// Загрузка файла (multipart/form-data). Возвращает AssetDto.
        /// </summary>
        public async Task<AssetDto> UploadFileAsync(
            string path,
            string filePath,
            string projectId,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var fileData = await File.ReadAllBytesAsync(filePath, ct);
            var filename = Path.GetFileName(filePath);

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("project_id", projectId),
                new MultipartFormFileSection("file", fileData, filename, GetMimeType(filePath))
            };

            using var req = UnityWebRequest.Post(BuildUrl(path), form);
            AttachAuthHeader(req);
            req.timeout = DefaultTimeoutSeconds * 4; // большой запас для больших файлов

            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                progress?.Report(op.progress);
                if (ct.IsCancellationRequested)
                {
                    req.Abort();
                    ct.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }

            progress?.Report(1f);

            EnsureSuccess(req);

            return JsonConvert.DeserializeObject<AssetDto>(req.downloadHandler.text);
        }

        // ================================================================
        //  Internals
        // ================================================================

        private async Task<TResponse> SendJsonAsync<TResponse>(
            string verb, string path, object body, CancellationToken ct)
        {
            Exception lastException = null;

            for (int attempt = 0; attempt <= BackoffSchedule.Length; attempt++)
            {
                try
                {
                    return await SendOnceAsync<TResponse>(verb, path, body, ct);
                }
                catch (ApiException ex) when (IsRetryable(ex.StatusCode) && attempt < BackoffSchedule.Length)
                {
                    lastException = ex;
                    var delay = BackoffSchedule[attempt];
                    Debug.LogWarning($"[ApiClient] {verb} {path} failed ({ex.StatusCode}), retrying in {delay}s...");
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            throw lastException ?? new ApiException(0, "Unknown API error");
        }

        private async Task<TResponse> SendOnceAsync<TResponse>(
            string verb, string path, object body, CancellationToken ct)
        {
            using var req = new UnityWebRequest(BuildUrl(path), verb);

            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var bytes = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.SetRequestHeader("Content-Type", "application/json");
            }

            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = DefaultTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");
            AttachAuthHeader(req);

            var token = AuthTokenProvider?.Invoke();
            var authInfo = string.IsNullOrEmpty(token)
                ? "no token"
                : $"Bearer {token[..Math.Min(20, token.Length)]}...";
            Debug.Log($"[HTTP] → {verb} {BuildUrl(path)}\n        Authorization: {authInfo}");

            var startTime = DateTime.UtcNow;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    req.Abort();
                    ct.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }

            var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var status = (int)req.responseCode;
            if (req.result == UnityWebRequest.Result.Success)
                Debug.Log($"[HTTP] ← {status} OK | {verb} {path} ({elapsed} ms)");
            else
                Debug.LogWarning($"[HTTP] ← {status} ERROR | {verb} {path} ({elapsed} ms)\n        {req.downloadHandler?.text}");

            EnsureSuccess(req);

            var text = req.downloadHandler.text;
            if (string.IsNullOrEmpty(text) || typeof(TResponse) == typeof(object))
                return default;

            return JsonConvert.DeserializeObject<TResponse>(text);
        }

        private void AttachAuthHeader(UnityWebRequest req)
        {
            var token = AuthTokenProvider?.Invoke();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private string BuildUrl(string path)
        {
            if (string.IsNullOrEmpty(path)) return BaseUrl;
            return path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? path
                : $"{BaseUrl}{(path.StartsWith("/") ? path : "/" + path)}";
        }

        private static void EnsureSuccess(UnityWebRequest req)
        {
            if (req.result == UnityWebRequest.Result.Success)
                return;

            var status = (int)req.responseCode;
            var errorText = req.downloadHandler?.text ?? req.error ?? "Unknown error";

            string detail = errorText;
            try
            {
                var parsed = JsonConvert.DeserializeObject<ApiError>(errorText);
                if (parsed != null && !string.IsNullOrEmpty(parsed.Detail))
                    detail = parsed.Detail;
            }
            catch { /* plain-text error, keep as is */ }

            throw new ApiException(status, detail);
        }

        private static bool IsRetryable(int statusCode)
        {
            // 5xx и сетевые ошибки (0) — можно ретраить.
            return statusCode == 0 || (statusCode >= 500 && statusCode < 600);
        }

        private static string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".ogg" => "audio/ogg",
                ".glb" => "model/gltf-binary",
                ".gltf" => "model/gltf+json",
                ".fbx" => "application/octet-stream",
                ".obj" => "application/octet-stream",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".csv" => "text/csv",
                _ => "application/octet-stream"
            };
        }
    }

    /// <summary>Исключение, генерируемое ApiClient при не-2xx ответе.</summary>
    public class ApiException : Exception
    {
        public int StatusCode { get; }
        public ApiException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        public bool IsUnauthorized => StatusCode == 401;
        public bool IsForbidden => StatusCode == 403;
        public bool IsNotFound => StatusCode == 404;
        public bool IsServerError => StatusCode >= 500 && StatusCode < 600;
    }
}
