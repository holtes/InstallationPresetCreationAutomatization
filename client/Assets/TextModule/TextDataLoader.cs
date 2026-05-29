using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace InteractiveClient.TextModule
{
    /// <summary>
    /// Загружает текстовый ассет (txt/json/csv) по URL и десериализует.
    /// Используется пресетами для опциональных слотов с данными:
    ///  • Memory.card_titles, SpotHunt.target_descriptions — JSON-массив объектов;
    ///  • Quiz.questions_data — JSON со списком вопросов;
    ///  • Puzzle.intro_text, Sequence.intro_text — plain text.
    /// </summary>
    public static class TextDataLoader
    {
        public static async Task<string> LoadStringAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url)) return null;

            using var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TextDataLoader] Failed to load {url}: {req.error}");
                return null;
            }

            return req.downloadHandler.text;
        }

        public static async Task<T> LoadJsonAsync<T>(string url, CancellationToken ct = default) where T : class
        {
            var text = await LoadStringAsync(url, ct);
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TextDataLoader] JSON parse failed for {url}: {ex.Message}");
                return null;
            }
        }
    }
}
