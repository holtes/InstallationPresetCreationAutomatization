using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Mapping;
using InteractiveClient.Network;

namespace InteractiveClient.Presets
{
    /// <summary>
    /// Описание одного пресета, собранное из PresetDto (метаданные)
    /// и PresetSchemaDto (слоты + параметры), подгружаемого лениво.
    /// </summary>
    public class PresetInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public List<string> Tags { get; set; } = new();
        public string ThumbnailUrl { get; set; }

        /// <summary>Схема (слоты + параметры). null, пока не подгружена.</summary>
        public PresetSchemaDto Schema { get; set; }

        /// <summary>Распаршенные слоты (заполняются после загрузки схемы).</summary>
        public List<SlotDefinition> Slots { get; set; } = new();

        /// <summary>Ссылка на параметры (удобнее таскать вместе с пресетом).</summary>
        public List<ParameterSchemaDto> Parameters => Schema?.Parameters ?? new List<ParameterSchemaDto>();

        public bool IsSchemaLoaded => Schema != null;

        public int RequiredSlotsCount
        {
            get
            {
                int n = 0;
                foreach (var s in Slots) if (s.Required) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// Реестр доступных пресетов. Список тянется с сервера (/api/presets),
    /// схема — лениво по запросу (/api/presets/{id}/schema).
    /// </summary>
    public class PresetRegistry
    {
        private readonly ApiClient api;
        private readonly Dictionary<string, PresetInfo> byId = new();

        public IReadOnlyCollection<PresetInfo> All => byId.Values;

        public PresetRegistry(ApiClient api) { this.api = api; }

        public async Task LoadListAsync(CancellationToken ct = default)
        {
            var dtos = await api.GetAsync<List<PresetDto>>(ApiEndpoints.Presets, ct);
            byId.Clear();

            if (dtos == null) return;

            foreach (var dto in dtos)
            {
                byId[dto.Id] = new PresetInfo
                {
                    Id = dto.Id,
                    Name = dto.Name,
                    Description = dto.Description,
                    Version = dto.Version,
                    Tags = dto.Tags ?? new List<string>(),
                    ThumbnailUrl = dto.ThumbnailUrl
                };
            }
        }

        public PresetInfo Get(string id) => byId.TryGetValue(id, out var p) ? p : null;

        /// <summary>Лениво подгружает JSON-схему (слоты + параметры) в PresetInfo.</summary>
        public async Task<PresetInfo> LoadSchemaAsync(string presetId, CancellationToken ct = default)
        {
            if (!byId.TryGetValue(presetId, out var preset))
                throw new System.InvalidOperationException($"Preset {presetId} not found. Call LoadListAsync first.");

            if (preset.IsSchemaLoaded)
                return preset;

            var schema = await api.GetAsync<PresetSchemaDto>(ApiEndpoints.PresetSchema(presetId), ct);
            preset.Schema = schema;

            preset.Slots.Clear();
            if (schema?.Slots != null)
                foreach (var slotDto in schema.Slots)
                    preset.Slots.Add(SlotDefinition.FromDto(slotDto));

            return preset;
        }
    }
}
