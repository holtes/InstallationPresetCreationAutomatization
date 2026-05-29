using System.Collections.Generic;
using System.IO;

namespace InteractiveClient.AssetsModule
{
    /// <summary>Результат валидации файла перед загрузкой.</summary>
    public class AssetValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public AssetType DetectedType { get; set; }
        public long SizeBytes { get; set; }

        public static AssetValidationResult Ok(AssetType type, long size)
            => new() { IsValid = true, DetectedType = type, SizeBytes = size };

        public static AssetValidationResult Fail(string msg)
            => new() { IsValid = false, ErrorMessage = msg };
    }

    /// <summary>
    /// Локальная валидация файлов перед отправкой на сервер.
    /// Ограничения по форматам и размерам — из ТЗ §3 Шаг 1.
    /// </summary>
    public static class AssetValidator
    {
        // ======= Ограничения (МБ) =======
        private const long MB = 1024L * 1024L;
        private const long ImageMax = 20 * MB;
        private const long VideoMax = 200 * MB;
        private const long AudioMax = 50 * MB;
        private const long ModelMax = 100 * MB;
        private const long TextMax = 5 * MB;

        // ======= Расширения по типам =======
        private static readonly Dictionary<string, AssetType> ExtensionMap = new(System.StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = AssetType.Image,
            [".jpg"] = AssetType.Image,
            [".jpeg"] = AssetType.Image,
            [".webp"] = AssetType.Image,

            [".mp4"] = AssetType.Video,
            [".webm"] = AssetType.Video,

            [".wav"] = AssetType.Audio,
            [".mp3"] = AssetType.Audio,
            [".ogg"] = AssetType.Audio,

            [".glb"] = AssetType.Model3D,
            [".gltf"] = AssetType.Model3D,
            [".fbx"] = AssetType.Model3D,
            [".obj"] = AssetType.Model3D,

            [".txt"] = AssetType.Text,
            [".json"] = AssetType.Text,
            [".csv"] = AssetType.Text
        };

        public static AssetValidationResult Validate(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return AssetValidationResult.Fail("Файл не найден.");

            var ext = Path.GetExtension(filePath);
            if (!ExtensionMap.TryGetValue(ext, out var type))
                return AssetValidationResult.Fail(
                    $"Неподдерживаемый формат: {ext}. Допустимы: PNG/JPEG/WebP, MP4/WEBM, WAV/MP3/OGG, GLB/GLTF/FBX/OBJ, TXT/JSON/CSV.");

            long size;
            try { size = new FileInfo(filePath).Length; }
            catch (System.Exception ex) { return AssetValidationResult.Fail($"Не удалось прочитать файл: {ex.Message}"); }

            var limit = GetSizeLimit(type);
            if (size > limit)
                return AssetValidationResult.Fail(
                    $"Файл слишком большой ({FormatBytes(size)}). Лимит для {type.ToDisplayString()}: {FormatBytes(limit)}.");

            return AssetValidationResult.Ok(type, size);
        }

        public static long GetSizeLimit(AssetType type) => type switch
        {
            AssetType.Image => ImageMax,
            AssetType.Video => VideoMax,
            AssetType.Audio => AudioMax,
            AssetType.Model3D => ModelMax,
            AssetType.Text => TextMax,
            _ => ImageMax
        };

        public static string GetAllowedExtensionsFilter(AssetType? type = null)
        {
            // Для системного диалога выбора файла
            if (type == null)
                return "image,png,jpg,jpeg,webp,video,mp4,webm,audio,wav,mp3,ogg,model,glb,gltf,fbx,obj,text,txt,json,csv";

            return type switch
            {
                AssetType.Image => "png,jpg,jpeg,webp",
                AssetType.Video => "mp4,webm",
                AssetType.Audio => "wav,mp3,ogg",
                AssetType.Model3D => "glb,gltf,fbx,obj",
                AssetType.Text => "txt,json,csv",
                _ => ""
            };
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} МБ";
            return $"{bytes / (1024.0 * 1024 * 1024):0.#} ГБ";
        }
    }
}
