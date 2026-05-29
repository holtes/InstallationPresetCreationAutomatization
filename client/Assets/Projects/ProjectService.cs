using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Network;

namespace InteractiveClient.Projects
{
    /// <summary>CRUD проектов через REST API.</summary>
    public class ProjectService
    {
        private readonly ApiClient api;

        public ProjectService(ApiClient api)
        {
            this.api = api;
        }

        public async Task<List<ProjectModel>> ListAsync(CancellationToken ct = default)
        {
            var dtos = await api.GetAsync<List<ProjectDto>>(ApiEndpoints.Projects, ct);
            var result = new List<ProjectModel>(dtos?.Count ?? 0);
            if (dtos != null)
                foreach (var d in dtos)
                    result.Add(ProjectModel.FromDto(d));
            return result;
        }

        public async Task<ProjectModel> GetAsync(string id, CancellationToken ct = default)
        {
            var dto = await api.GetAsync<ProjectDto>(ApiEndpoints.ProjectById(id), ct);
            return ProjectModel.FromDto(dto);
        }

        public async Task<ProjectModel> CreateAsync(
            string name, string description, int? targetProfileId, CancellationToken ct = default)
        {
            var dto = await api.PostAsync<ProjectCreateRequest, ProjectDto>(
                ApiEndpoints.Projects,
                new ProjectCreateRequest
                {
                    Title = name,
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    TargetProfileId = targetProfileId
                },
                ct);
            return ProjectModel.FromDto(dto);
        }

        public async Task<ProjectModel> UpdateAsync(ProjectModel project, CancellationToken ct = default)
        {
            var dto = await api.PatchAsync<ProjectUpdateRequest, ProjectDto>(
                ApiEndpoints.ProjectById(project.Id),
                project.ToUpdateRequest(),
                ct);

            var updated = ProjectModel.FromDto(dto);
            updated?.MarkSaved();
            return updated;
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
            => api.DeleteAsync(ApiEndpoints.ProjectById(id), ct);
    }
}
