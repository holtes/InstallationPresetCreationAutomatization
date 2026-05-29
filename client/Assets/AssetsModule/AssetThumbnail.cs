using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace InteractiveClient.AssetsModule
{
    /// <summary>
    /// Кэш и генерация превью для ассетов.
    /// • Изображения — загружаются по thumbnail_url (или url) как Texture2D.
    /// • Видео/3D — пока возвращают null (серверный thumbnail_url должен их отдавать).
    /// • Аудио/Text — UI показывает иконку по типу (Texture2D не нужен).
    ///
    /// Дальнейшее расширение: локальная генерация первого кадра видео (VideoPlayer)
    /// и рендер 3D-модели во ViewportTexture — вне MVP.
    /// </summary>
    public class AssetThumbnail
    {
        private readonly Dictionary<string, Texture2D> cache = new();

        public Texture2D TryGetCached(string assetId)
            => cache.TryGetValue(assetId, out var tex) ? tex : null;

        public async Task<Texture2D> LoadAsync(AssetModel asset, CancellationToken ct = default)
        {
            if (asset == null) return null;

            if (cache.TryGetValue(asset.Id, out var cached))
                return cached;

            var url = !string.IsNullOrEmpty(asset.ThumbnailUrl) ? asset.ThumbnailUrl : asset.Url;
            if (string.IsNullOrEmpty(url)) return null;

            // Только картинки грузим как текстуру; остальное — по серверному thumbnail_url,
            // который, если есть, тоже будет изображением.
            using var req = UnityWebRequestTexture.GetTexture(url);
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

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AssetThumbnail] Failed to load thumbnail for {asset.Id}: {req.error}");
                return null;
            }

            var tex = DownloadHandlerTexture.GetContent(req);
            cache[asset.Id] = tex;
            return tex;
        }

        public void Invalidate(string assetId)
        {
            if (cache.TryGetValue(assetId, out var tex))
            {
                Object.Destroy(tex);
                cache.Remove(assetId);
            }
        }

        public void Clear()
        {
            foreach (var tex in cache.Values)
                Object.Destroy(tex);
            cache.Clear();
        }
    }
}
