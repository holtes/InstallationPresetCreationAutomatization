using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace InteractiveClient.AudioModule
{
    /// <summary>
    /// Кэш AudioClip-ов, загруженных по URL. Используется пресетами для подгрузки
    /// озвучки из ассет-маппинга.
    /// </summary>
    public class AudioLoader
    {
        private readonly Dictionary<string, AudioClip> cache = new();

        public AudioClip TryGetCached(string url)
            => cache.TryGetValue(url ?? string.Empty, out var c) ? c : null;

        public async Task<AudioClip> LoadAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (cache.TryGetValue(url, out var cached)) return cached;

            var type = GuessAudioType(url);
            using var req = UnityWebRequestMultimedia.GetAudioClip(url, type);
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AudioLoader] Failed to load {url}: {req.error}");
                return null;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip != null) cache[url] = clip;
            return clip;
        }

        private static AudioType GuessAudioType(string url)
        {
            var lower = url.ToLowerInvariant();
            if (lower.EndsWith(".wav")) return AudioType.WAV;
            if (lower.EndsWith(".ogg")) return AudioType.OGGVORBIS;
            return AudioType.MPEG;
        }

        public void Clear()
        {
            foreach (var c in cache.Values) UnityEngine.Object.Destroy(c);
            cache.Clear();
        }
    }
}
