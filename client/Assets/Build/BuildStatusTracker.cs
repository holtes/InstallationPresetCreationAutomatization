using System;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Network;
using UnityEngine;

namespace InteractiveClient.Build
{
    /// <summary>
    /// Отслеживает статус сборки через polling GET /api/builds/{id}/status (каждые 3 сек, §3 ТЗ).
    /// Событиями уведомляет подписчика о прогрессе, лог-строках и финальном статусе.
    /// По завершении — сам достаёт /result для success.
    /// </summary>
    public class BuildStatusTracker
    {
        private readonly BuildService buildService;

        public event Action<BuildStatusResponse> OnStatusUpdated;
        public event Action<BuildResultResponse> OnSuccess;
        public event Action<string> OnError;       // error_message
        public event Action OnCancelled;

        public const float PollIntervalSeconds = 3f;

        public BuildStatusTracker(BuildService buildService)
        {
            this.buildService = buildService;
        }

        /// <summary>
        /// Бесконечный polling до терминального статуса.
        /// Отмена через CancellationToken — завершает цикл и публикует OnCancelled.
        /// </summary>
        public async Task TrackAsync(string buildId, CancellationToken ct = default)
        {
            string lastLogSnapshot = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    BuildStatusResponse status;
                    try
                    {
                        status = await buildService.GetStatusAsync(buildId, ct);
                    }
                    catch (ApiException ex)
                    {
                        Debug.LogWarning($"[BuildStatusTracker] Status fetch failed: {ex.StatusCode} {ex.Message}");
                        // Пропускаем итерацию, ApiClient сам ретраит 5xx.
                        await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct);
                        continue;
                    }

                    if (status == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct);
                        continue;
                    }

                    // Отсылаем обновление только если что-то изменилось (progress/stage/log).
                    if (HasMeaningfulDelta(status, lastLogSnapshot))
                    {
                        lastLogSnapshot = status.Log;
                        OnStatusUpdated?.Invoke(status);
                    }

                    switch (status.Status)
                    {
                        case "success":
                            try
                            {
                                var result = await buildService.GetResultAsync(buildId, ct);
                                OnSuccess?.Invoke(result);
                            }
                            catch (Exception ex)
                            {
                                OnError?.Invoke($"Сборка успешна, но не удалось получить результат: {ex.Message}");
                            }
                            return;

                        case "error":
                            OnError?.Invoke(status.ErrorMessage ?? "Сборка завершилась с ошибкой.");
                            return;

                        case "cancelled":
                            OnCancelled?.Invoke();
                            return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct);
                }

                if (ct.IsCancellationRequested)
                    OnCancelled?.Invoke();
            }
            catch (OperationCanceledException)
            {
                OnCancelled?.Invoke();
            }
        }

        private static bool HasMeaningfulDelta(BuildStatusResponse s, string prevLog)
        {
            // Публикуем каждое обновление — UI сам решит, что с ним делать.
            // Лёгкая оптимизация: не публиковать, если log полностью совпадает и progress не менялся,
            // но для простоты пока публикуем всегда.
            return true;
        }
    }
}
