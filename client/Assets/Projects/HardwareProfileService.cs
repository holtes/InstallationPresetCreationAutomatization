using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Network;

namespace InteractiveClient.Projects
{
    /// <summary>
    /// Чтение списка hardware-профилей с сервера. Используется в модалке
    /// «Новый проект» для подстановки реальных id вместо локальных строк.
    /// Кеш в памяти на время сессии — список редко меняется.
    /// </summary>
    public class HardwareProfileService
    {
        private readonly ApiClient api;
        private List<HardwareProfileDto> cache;

        public HardwareProfileService(ApiClient api)
        {
            this.api = api;
        }

        public async Task<List<HardwareProfileDto>> ListAsync(
            bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && cache != null) return cache;
            var dtos = await api.GetAsync<List<HardwareProfileDto>>(ApiEndpoints.HardwareProfiles, ct);
            cache = dtos ?? new List<HardwareProfileDto>();
            return cache;
        }

        public void InvalidateCache() => cache = null;
    }
}
