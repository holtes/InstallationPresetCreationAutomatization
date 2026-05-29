using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Network;

namespace InteractiveClient.AssetsModule
{
    /// <summary>
    /// Каталог загруженных ассетов в рамках текущего проекта.
    /// Обновляется через LoadForProject(), добавляется через Add().
    /// Обеспечивает фильтрацию по типу.
    /// </summary>
    public class AssetLibrary
    {
        private readonly ApiClient api;
        private readonly Dictionary<string, AssetModel> byId = new();
        private string currentProjectId;

        public event Action OnChanged;

        public IReadOnlyCollection<AssetModel> All => byId.Values;
        public int Count => byId.Count;
        public string CurrentProjectId => currentProjectId;

        public AssetLibrary(ApiClient api) { this.api = api; }

        /// <summary>
        /// Загружает с сервера список ассетов.
        /// NOTE: серверный эндпоинт /api/assets/ возвращает ВСЕ ассеты текущего пользователя
        /// (привязка к проекту хранится в отдельной ProjectAsset-таблице).
        /// Точечная фильтрация по projectId — отдельная итерация.
        /// </summary>
        public async Task LoadForProjectAsync(string projectId, CancellationToken ct = default)
        {
            currentProjectId = projectId;
            byId.Clear();

            var dtos = await api.GetAsync<List<AssetDto>>(ApiEndpoints.AssetsByProject(projectId), ct);
            if (dtos != null)
            {
                foreach (var dto in dtos)
                {
                    var model = AssetModel.FromDto(dto);
                    if (model != null && !string.IsNullOrEmpty(model.Id))
                    {
                        // Url не приходит с сервера — собираем сами на основе /api/assets/{id}/file
                        if (string.IsNullOrEmpty(model.Url))
                            model.Url = $"{api.BaseUrl}{ApiEndpoints.AssetFile(model.Id)}";
                        byId[model.Id] = model;
                    }
                }
            }

            OnChanged?.Invoke();
        }

        /// <summary>Добавляет ассет в библиотеку (например, только что загруженный).</summary>
        public void Add(AssetModel asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.Id)) return;
            if (string.IsNullOrEmpty(asset.Url))
                asset.Url = $"{api.BaseUrl}{ApiEndpoints.AssetFile(asset.Id)}";
            byId[asset.Id] = asset;
            OnChanged?.Invoke();
        }

        public bool Remove(string assetId)
        {
            if (byId.Remove(assetId))
            {
                OnChanged?.Invoke();
                return true;
            }
            return false;
        }

        public async Task DeleteAsync(string assetId, CancellationToken ct = default)
        {
            await api.DeleteAsync(ApiEndpoints.AssetById(assetId), ct);
            Remove(assetId);
        }

        public AssetModel Get(string id) => byId.TryGetValue(id, out var a) ? a : null;

        public IEnumerable<AssetModel> FilterByType(AssetType? type)
        {
            if (type == null || type == AssetType.Unknown)
                return byId.Values;
            return byId.Values.Where(a => a.Type == type);
        }

        public void Clear()
        {
            byId.Clear();
            currentProjectId = null;
            OnChanged?.Invoke();
        }
    }
}
