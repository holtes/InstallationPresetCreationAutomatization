using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Network;
using UnityEngine;

namespace InteractiveClient.AssetsModule
{
    /// <summary>Состояние одной задачи загрузки (для UI-отображения прогресса).</summary>
    public class UploadTask
    {
        public string Filename { get; set; }
        public long SizeBytes { get; set; }
        public float Progress { get; set; }  // 0..1
        public UploadStatus Status { get; set; } = UploadStatus.Pending;
        public string ErrorMessage { get; set; }
        public AssetModel ResultAsset { get; set; }

        public event Action<UploadTask> OnChanged;
        public void NotifyChanged() => OnChanged?.Invoke(this);
    }

    public enum UploadStatus { Pending, Uploading, Success, Failed, Cancelled }

    /// <summary>
    /// Загружает файлы на сервер с ограничением параллелизма (3 одновременно, §9 ТЗ).
    /// Валидация — через AssetValidator перед постановкой в очередь.
    /// </summary>
    public class AssetUploader
    {
        private readonly ApiClient api;
        private readonly SemaphoreSlim concurrency;

        public const int MaxParallelUploads = 3;

        public event Action<UploadTask> OnTaskQueued;
        public event Action<UploadTask> OnTaskCompleted;   // Success | Failed | Cancelled

        public AssetUploader(ApiClient api)
        {
            this.api = api;
            concurrency = new SemaphoreSlim(MaxParallelUploads, MaxParallelUploads);
        }

        /// <summary>
        /// Валидирует и ставит файл в очередь. Возвращает UploadTask для подписки на прогресс.
        /// Если файл не валиден — вернёт task со Status=Failed.
        /// </summary>
        public UploadTask Enqueue(string filePath, string projectId, CancellationToken ct = default)
        {
            var task = new UploadTask
            {
                Filename = Path.GetFileName(filePath)
            };

            var validation = AssetValidator.Validate(filePath);
            if (!validation.IsValid)
            {
                task.Status = UploadStatus.Failed;
                task.ErrorMessage = validation.ErrorMessage;
                task.NotifyChanged();
                OnTaskQueued?.Invoke(task);
                OnTaskCompleted?.Invoke(task);
                return task;
            }

            task.SizeBytes = validation.SizeBytes;
            OnTaskQueued?.Invoke(task);

            _ = RunAsync(task, filePath, projectId, ct); // fire-and-forget
            return task;
        }

        /// <summary>Загружает набор файлов. Возвращает список успешно загруженных AssetModel.</summary>
        public async Task<List<AssetModel>> UploadManyAsync(
            IEnumerable<string> filePaths, string projectId, CancellationToken ct = default)
        {
            var tasks = new List<UploadTask>();
            foreach (var path in filePaths)
                tasks.Add(Enqueue(path, projectId, ct));

            // Ждём завершения всех.
            while (true)
            {
                bool allDone = true;
                foreach (var t in tasks)
                {
                    if (t.Status == UploadStatus.Pending || t.Status == UploadStatus.Uploading)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone) break;
                await Task.Delay(100, ct);
            }

            var successful = new List<AssetModel>();
            foreach (var t in tasks)
                if (t.Status == UploadStatus.Success && t.ResultAsset != null)
                    successful.Add(t.ResultAsset);

            return successful;
        }

        private async Task RunAsync(UploadTask task, string filePath, string projectId, CancellationToken ct)
        {
            await concurrency.WaitAsync(ct);

            try
            {
                task.Status = UploadStatus.Uploading;
                task.NotifyChanged();

                var progress = new Progress<float>(p =>
                {
                    task.Progress = p;
                    task.NotifyChanged();
                });

                var dto = await api.UploadFileAsync(ApiEndpoints.AssetUpload, filePath, projectId, progress, ct);
                task.ResultAsset = AssetModel.FromDto(dto);
                if (task.ResultAsset != null)
                    task.ResultAsset.LocalPath = filePath; // для мгновенного превью без сервера
                task.Progress = 1f;
                task.Status = UploadStatus.Success;

                EventBus.Publish(new AssetUploadedEvent(task.ResultAsset?.Id));
            }
            catch (OperationCanceledException)
            {
                task.Status = UploadStatus.Cancelled;
                task.ErrorMessage = "Загрузка отменена.";
            }
            catch (Exception ex)
            {
                task.Status = UploadStatus.Failed;
                task.ErrorMessage = ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                concurrency.Release();
                task.NotifyChanged();
                OnTaskCompleted?.Invoke(task);
            }
        }
    }
}
