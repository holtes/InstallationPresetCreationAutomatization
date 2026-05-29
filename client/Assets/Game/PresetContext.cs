using System.Collections.Generic;
using InteractiveClient.AssetsModule;
using Newtonsoft.Json.Linq;

namespace InteractiveClient.Game
{
    /// <summary>
    /// Всё, что нужно пресету для запуска: ассеты по slot_id и параметры.
    /// Передаётся в PresetBase.Initialize().
    /// </summary>
    public class PresetContext
    {
        /// <summary>slot_id → AssetModel (с url, локальный путь и т.п.). Может быть null для незаполненных опциональных слотов.</summary>
        public IReadOnlyDictionary<string, AssetModel> Assets { get; }

        /// <summary>Параметры пресета (динамические, см. ParameterSchemaDto).</summary>
        public JObject Parameters { get; }

        public PresetContext(IReadOnlyDictionary<string, AssetModel> assets, JObject parameters)
        {
            Assets = assets ?? new Dictionary<string, AssetModel>();
            Parameters = parameters ?? new JObject();
        }

        public AssetModel GetAsset(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return null;
            Assets.TryGetValue(slotId, out var a);
            return a;
        }

        public bool HasAsset(string slotId) => GetAsset(slotId) != null;

        // Удобные геттеры для параметров
        public int GetInt(string key, int fallback = 0)
            => (Parameters[key] is JValue v && v.Type != JTokenType.Null) ? v.Value<int?>() ?? fallback : fallback;

        public float GetFloat(string key, float fallback = 0f)
            => (Parameters[key] is JValue v && v.Type != JTokenType.Null) ? v.Value<float?>() ?? fallback : fallback;

        public bool GetBool(string key, bool fallback = false)
            => (Parameters[key] is JValue v && v.Type != JTokenType.Null) ? v.Value<bool?>() ?? fallback : fallback;

        public string GetString(string key, string fallback = null)
            => (Parameters[key] is JValue v && v.Type != JTokenType.Null) ? v.Value<string>() ?? fallback : fallback;
    }
}
