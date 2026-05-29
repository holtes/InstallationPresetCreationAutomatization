using System.Collections.Generic;
using InteractiveClient.AssetsModule;

namespace InteractiveClient.Mapping
{
    public class MappingValidationResult
    {
        /// <summary>Можно переходить к следующему шагу.</summary>
        public bool IsComplete { get; set; }

        /// <summary>id обязательных слотов, которые не заполнены.</summary>
        public List<string> MissingRequiredSlotIds { get; set; } = new();

        /// <summary>id слотов с несовпадающим типом ассета.</summary>
        public List<string> TypeMismatchSlotIds { get; set; } = new();

        public int RequiredTotal { get; set; }
        public int RequiredFilled { get; set; }
    }

    /// <summary>
    /// Проверяет корректность и полноту маппинга: слоты пресета × назначенные ассеты.
    /// </summary>
    public static class MappingValidator
    {
        public static MappingValidationResult Validate(
            IEnumerable<SlotDefinition> slots,
            IReadOnlyDictionary<string, string> mapping,        // slot_id → asset_id
            AssetLibrary library)
        {
            var result = new MappingValidationResult();

            foreach (var slot in slots)
            {
                bool filled = mapping != null
                              && mapping.TryGetValue(slot.Id, out var assetId)
                              && !string.IsNullOrEmpty(assetId);

                if (slot.Required)
                {
                    result.RequiredTotal++;
                    if (filled) result.RequiredFilled++;
                    else result.MissingRequiredSlotIds.Add(slot.Id);
                }

                if (filled)
                {
                    var asset = library?.Get(mapping[slot.Id]);
                    if (asset != null && !slot.Accepts(asset))
                        result.TypeMismatchSlotIds.Add(slot.Id);
                }
            }

            result.IsComplete = result.MissingRequiredSlotIds.Count == 0
                                && result.TypeMismatchSlotIds.Count == 0;
            return result;
        }
    }
}
