using System;
using System.Collections.Generic;
using UnityEngine;

namespace InteractiveClient.UI.Services
{
    /// <summary>
    /// Обёртка над системным диалогом выбора файлов.
    /// • В Unity Editor — использует EditorUtility.OpenFilePanelWithFilters.
    /// • В standalone Windows-сборке — через Win32 GetOpenFileName (требует нативной интеграции).
    ///   Заглушка ниже возвращает пустой массив и логирует предупреждение.
    ///
    /// Drag-and-drop из проводника в рантайме — реализован в DragAndDropController
    /// через UnityEngine.InputSystem и native file-drop callback.
    /// </summary>
    public static class FileDialog
    {
        public struct Filter
        {
            public string Label;
            /// <summary>Список расширений без точки, напр. ["png","jpg"].</summary>
            public string[] Extensions;
        }

        /// <summary>
        /// Открывает системный диалог выбора файлов. Возвращает абсолютные пути.
        /// </summary>
        public static string[] OpenFiles(string title, IEnumerable<Filter> filters, bool multiple = true)
        {
#if UNITY_EDITOR
            var filterArray = ToEditorFilter(filters);
            if (multiple)
            {
                // Editor API does not support multi-select with filters directly;
                // For simplicity, open single-file and encourage drag-and-drop for multi.
                var path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "", filterArray);
                return string.IsNullOrEmpty(path) ? System.Array.Empty<string>() : new[] { path };
            }
            else
            {
                var path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "", filterArray);
                return string.IsNullOrEmpty(path) ? System.Array.Empty<string>() : new[] { path };
            }
#else
            // TODO: нативная интеграция с GetOpenFileName или сторонний пакет
            // (например, StandaloneFileBrowser). Пока — placeholder.
            Debug.LogWarning("[FileDialog] Native file dialog not implemented in standalone build. " +
                             "Use drag-and-drop or integrate StandaloneFileBrowser package.");
            return System.Array.Empty<string>();
#endif
        }

#if UNITY_EDITOR
        private static string[] ToEditorFilter(IEnumerable<Filter> filters)
        {
            var list = new List<string>();
            foreach (var f in filters)
            {
                list.Add(f.Label);
                list.Add(string.Join(",", f.Extensions));
            }
            return list.ToArray();
        }
#endif

        // ======= Предустановленные фильтры по типам ассетов =======
        public static readonly Filter ImageFilter = new() { Label = "Изображения", Extensions = new[] { "png", "jpg", "jpeg", "webp" } };
        public static readonly Filter VideoFilter = new() { Label = "Видео",       Extensions = new[] { "mp4", "webm" } };
        public static readonly Filter AudioFilter = new() { Label = "Аудио",       Extensions = new[] { "wav", "mp3", "ogg" } };
        public static readonly Filter ModelFilter = new() { Label = "3D-модели",   Extensions = new[] { "glb", "gltf", "fbx", "obj" } };
        public static readonly Filter TextFilter  = new() { Label = "Текст",       Extensions = new[] { "txt", "json", "csv" } };

        public static readonly Filter[] AllAssetFilters = { ImageFilter, VideoFilter, AudioFilter, ModelFilter, TextFilter };
    }
}
