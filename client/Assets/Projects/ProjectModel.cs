using System;
using System.Collections.Generic;
using InteractiveClient.Network;
using Newtonsoft.Json.Linq;

namespace InteractiveClient.Projects
{
    /// <summary>
    /// Доменная модель проекта. Оборачивает ProjectDto,
    /// держит локальное "грязное" состояние для автосохранения.
    /// </summary>
    public class ProjectModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PresetId { get; set; }
        public string PresetVersion { get; set; } = "1.0";
        public int? TargetProfileId { get; set; }
        public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
        public string ThumbnailUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string OwnerId { get; set; }

        /// <summary>Маппинг slot_id → asset_id.</summary>
        public Dictionary<string, string> AssetMapping { get; set; } = new();

        /// <summary>Динамические параметры пресета (JSON-объект).</summary>
        public JObject Parameters { get; set; } = new();

        /// <summary>True, если были локальные изменения, ещё не отправленные на сервер.</summary>
        public bool IsDirty { get; private set; }
        public DateTime LastSavedAt { get; private set; }

        public void MarkDirty() => IsDirty = true;
        public void MarkSaved()
        {
            IsDirty = false;
            LastSavedAt = DateTime.UtcNow;
        }

        public static ProjectModel FromDto(ProjectDto dto)
        {
            if (dto == null) return null;
            return new ProjectModel
            {
                Id = dto.Id,
                Name = dto.Title,
                Description = dto.Description,
                TargetProfileId = dto.TargetProfileId,
                // PresetId / ThumbnailUrl / AssetMapping / Parameters не возвращаются
                // эндпоинтом /api/projects. Синхронизируются через /api/configurations при сборке.
                Status = ParseStatus(dto.Status),
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt,
                OwnerId = dto.OwnerId
            };
        }

        public ProjectUpdateRequest ToUpdateRequest() => new()
        {
            Title = Name,
            Description = Description,
            TargetProfileId = TargetProfileId
        };

        private static ProjectStatus ParseStatus(string raw) => raw switch
        {
            "ready" => ProjectStatus.Ready,
            "published" => ProjectStatus.Published,
            "error" => ProjectStatus.Error,
            _ => ProjectStatus.Draft
        };
    }

    public enum ProjectStatus { Draft, Ready, Published, Error }

    public static class ProjectStatusExtensions
    {
        public static string ToDisplayString(this ProjectStatus s) => s switch
        {
            ProjectStatus.Draft => "Черновик",
            ProjectStatus.Ready => "Готов",
            ProjectStatus.Published => "Опубликован",
            ProjectStatus.Error => "Ошибка",
            _ => s.ToString()
        };

        public static string ToBadgeClass(this ProjectStatus s) => s switch
        {
            ProjectStatus.Draft => "badge-draft",
            ProjectStatus.Ready => "badge-ready",
            ProjectStatus.Published => "badge-published",
            ProjectStatus.Error => "badge-error",
            _ => "badge-draft"
        };
    }
}
