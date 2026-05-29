using System;
using InteractiveClient.Network;
using Newtonsoft.Json.Linq;

namespace InteractiveClient.AssetsModule
{
    /// <summary>Тип ассета (нормализованный enum).</summary>
    public enum AssetType
    {
        Unknown,
        Image,
        Video,
        Audio,
        Model3D,
        Text
    }

    public static class AssetTypeExtensions
    {
        public static string ToApiString(this AssetType t) => t switch
        {
            AssetType.Image => "image",
            AssetType.Video => "video",
            AssetType.Audio => "audio",
            AssetType.Model3D => "model_3d",
            AssetType.Text => "text",
            _ => "unknown"
        };

        public static AssetType FromApiString(string s) => s switch
        {
            "image" => AssetType.Image,
            "video" => AssetType.Video,
            "audio" => AssetType.Audio,
            "model_3d" => AssetType.Model3D,
            "text" => AssetType.Text,
            _ => AssetType.Unknown
        };

        public static string ToDisplayString(this AssetType t) => t switch
        {
            AssetType.Image => "Изображение",
            AssetType.Video => "Видео",
            AssetType.Audio => "Аудио",
            AssetType.Model3D => "3D-модель",
            AssetType.Text => "Текст",
            _ => "Неизвестно"
        };
    }

    /// <summary>
    /// Доменная модель ассета. Обёртка над AssetDto + локальные поля
    /// (путь на диске до загрузки, превью-текстура и т.п.).
    /// </summary>
    public class AssetModel
    {
        public string Id { get; set; }
        public string Filename { get; set; }
        public AssetType Type { get; set; }
        public string MimeType { get; set; }
        public long SizeBytes { get; set; }
        public string Url { get; set; }
        public string ThumbnailUrl { get; set; }
        public JObject Metadata { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Локальный путь (до загрузки на сервер).</summary>
        public string LocalPath { get; set; }

        /// <summary>Загружен на сервер.</summary>
        public bool IsUploaded => !string.IsNullOrEmpty(Id);

        public static AssetModel FromDto(AssetDto dto)
        {
            if (dto == null) return null;
            return new AssetModel
            {
                Id = dto.Id,
                Filename = dto.Filename,
                Type = TypeFromContentType(dto.ContentType, dto.Filename),
                MimeType = dto.ContentType,
                SizeBytes = dto.SizeBytes,
                // Url / ThumbnailUrl не возвращаются эндпоинтом /api/assets/ — на клиенте остаются null,
                // фактический предпросмотр пока не реализован.
                Metadata = dto.MetadataJson,
                CreatedAt = dto.CreatedAt
            };
        }

        /// <summary>
        /// Определяет тип ассета по MIME (приоритет) или расширению имени файла.
        /// Сервер возвращает только content_type — выводим Type локально.
        /// </summary>
        public static AssetType TypeFromContentType(string contentType, string filename)
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return AssetType.Image;
                if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return AssetType.Video;
                if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return AssetType.Audio;
                if (contentType.StartsWith("model/",  StringComparison.OrdinalIgnoreCase)) return AssetType.Model3D;
                if (contentType.StartsWith("text/",   StringComparison.OrdinalIgnoreCase)) return AssetType.Text;
                if (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) return AssetType.Text;
            }

            if (!string.IsNullOrEmpty(filename))
            {
                var ext = System.IO.Path.GetExtension(filename).ToLowerInvariant();
                switch (ext)
                {
                    case ".png": case ".jpg": case ".jpeg": case ".webp": case ".gif": return AssetType.Image;
                    case ".mp4": case ".webm": case ".mov":                              return AssetType.Video;
                    case ".wav": case ".mp3": case ".ogg": case ".m4a":                  return AssetType.Audio;
                    case ".glb": case ".gltf": case ".fbx": case ".obj":                 return AssetType.Model3D;
                    case ".txt": case ".json": case ".csv": case ".md":                  return AssetType.Text;
                }
            }
            return AssetType.Unknown;
        }

        public string SizeHumanReadable()
        {
            if (SizeBytes < 1024) return $"{SizeBytes} Б";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.#} КБ";
            if (SizeBytes < 1024L * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024):0.#} МБ";
            return $"{SizeBytes / (1024.0 * 1024 * 1024):0.#} ГБ";
        }
    }
}
