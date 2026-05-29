using System.Collections.Generic;
using System.Linq;
using InteractiveClient.AssetsModule;
using InteractiveClient.Network;

namespace InteractiveClient.Mapping
{
    /// <summary>
    /// Описание одного слота пресета (куда назначается ассет).
    /// Строится из SlotSchemaDto при загрузке схемы пресета.
    ///
    /// Поддерживает несколько допустимых типов: в schema поле "type" может быть
    /// либо "image", либо "image|video" (pipe-separated). Это нужно, например,
    /// для Spot Hunt, где scene_background принимает картинку или видео.
    /// </summary>
    public class SlotDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Все допустимые типы. Никогда не пуст: минимум {Unknown}.</summary>
        public List<AssetType> AcceptedTypes { get; set; } = new() { AssetType.Unknown };

        /// <summary>Первый из допустимых типов (для UI-бейджа и т.п.).</summary>
        public AssetType AcceptedType => AcceptedTypes.Count > 0 ? AcceptedTypes[0] : AssetType.Unknown;

        public bool Required { get; set; }
        public string Group { get; set; }
        public string Description { get; set; }

        /// <summary>Динамический слот (например, event_media_1..N).</summary>
        public bool Dynamic { get; set; }

        /// <summary>Минимальное количество для динамических слотов.</summary>
        public int MinCount { get; set; }

        public static SlotDefinition FromDto(SlotSchemaDto dto)
        {
            if (dto == null) return null;
            return new SlotDefinition
            {
                Id = dto.Id,
                Name = dto.Name,
                AcceptedTypes = ParseTypes(dto.Type),
                Required = dto.Required,
                Group = string.IsNullOrEmpty(dto.Group) ? "Основные" : dto.Group,
                Description = dto.Description,
                Dynamic = dto.Dynamic,
                MinCount = dto.MinCount ?? 0
            };
        }

        private static List<AssetType> ParseTypes(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return new List<AssetType> { AssetType.Unknown };

            var parts = raw.Split('|');
            var list = new List<AssetType>(parts.Length);
            foreach (var p in parts)
            {
                var t = AssetTypeExtensions.FromApiString(p.Trim());
                if (!list.Contains(t)) list.Add(t);
            }
            if (list.Count == 0) list.Add(AssetType.Unknown);
            return list;
        }

        /// <summary>Проверяет, подходит ли ассет этому слоту.</summary>
        public bool Accepts(AssetModel asset)
        {
            if (asset == null) return false;
            if (AcceptedTypes.Contains(AssetType.Unknown)) return true;
            return AcceptedTypes.Contains(asset.Type);
        }

        /// <summary>Строка вида "image" или "image|video" для отображения в UI.</summary>
        public string DisplayTypeString()
            => string.Join("|", AcceptedTypes.Select(t => t.ToApiString()));
    }
}
