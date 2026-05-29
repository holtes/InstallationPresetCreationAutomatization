using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Network;

namespace InteractiveClient.LLM
{
    /// <summary>
    /// Запросы к LLM через бэкенд (проксирует на Gemini).
    /// </summary>
    public class LLMService
    {
        private readonly ApiClient api;

        public LLMService(ApiClient api) { this.api = api; }

        /// <summary>Предложение маппинга ассетов в слоты пресета.</summary>
        public Task<LlmSuggestMappingResponse> SuggestMappingAsync(
            string presetId,
            IEnumerable<string> assetIds,
            CancellationToken ct = default)
        {
            return api.PostAsync<LlmSuggestMappingRequest, LlmSuggestMappingResponse>(
                ApiEndpoints.LlmSuggestMapping,
                new LlmSuggestMappingRequest
                {
                    PresetId = presetId,
                    AssetIds = new List<string>(assetIds)
                },
                ct);
        }

        /// <summary>Генерация метаданных (описание, теги, предлагаемая роль) для ассета.</summary>
        public Task<LlmMetadataResponse> GenerateMetadataAsync(
            string assetId, CancellationToken ct = default)
        {
            return api.PostAsync<LlmMetadataRequest, LlmMetadataResponse>(
                ApiEndpoints.LlmGenerateMetadata,
                new LlmMetadataRequest { AssetId = assetId },
                ct);
        }
    }
}
