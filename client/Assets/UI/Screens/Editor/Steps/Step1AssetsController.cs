using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InteractiveClient.AssetsModule;
using InteractiveClient.Core;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 1 — загрузка ассетов. Drop-zone (клик = FileDialog), список прогресса,
    /// сетка библиотеки с фильтрами по типу.
    /// </summary>
    public class Step1AssetsController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step1_Assets";

        private ProjectModel project;
        private VisualElement root;

        private VisualElement dropZone;
        private ScrollView uploadList;
        private Label assetCount;
        private VisualElement libraryGrid;
        private VisualElement filters;
        private AssetType? currentFilter; // null == все

        private readonly Dictionary<UploadTask, VisualElement> uploadItems = new();

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.root = root;
            this.project = project;

            dropZone     = root.Q<VisualElement>("asset-drop-zone");
            uploadList   = root.Q<ScrollView>("upload-progress-list");
            assetCount   = root.Q<Label>("asset-count");
            libraryGrid  = root.Q<VisualElement>("asset-library-grid");
            filters      = root.Q<VisualElement>("asset-filters");

            uploadList?.Clear();
            libraryGrid?.Clear();

            if (dropZone != null)
                dropZone.RegisterCallback<ClickEvent>(_ => OpenFilePicker());

            // Биндим кнопки фильтров (имя класса --active соответствует новому Steps.uss)
            if (filters != null)
            {
                var buttons = filters.Query<Button>().ToList();
                AssetType?[] map = { null, AssetType.Image, AssetType.Video, AssetType.Audio, AssetType.Model3D, AssetType.Text };
                for (int i = 0; i < buttons.Count && i < map.Length; i++)
                {
                    var type = map[i];
                    var btn = buttons[i];
                    btn.clicked += () =>
                    {
                        currentFilter = type;
                        foreach (var b in buttons) b.RemoveFromClassList("library-filter-btn--active");
                        btn.AddToClassList("library-filter-btn--active");
                        RenderLibrary();
                    };
                }
            }

            var uploader = ServiceLocator.Get<AssetUploader>();
            uploader.OnTaskQueued    += OnTaskQueued;
            uploader.OnTaskCompleted += OnTaskCompleted;

            var library = ServiceLocator.Get<AssetLibrary>();
            library.OnChanged += RenderLibrary;
        }

        public async Task EnterAsync()
        {
            var library = ServiceLocator.Get<AssetLibrary>();
            try
            {
                if (library.CurrentProjectId != project.Id)
                    await library.LoadForProjectAsync(project.Id);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Не удалось загрузить библиотеку ассетов.");
            }
            RenderLibrary();
        }

        public Task LeaveAsync() => Task.CompletedTask;

        public string Validate()
        {
            var library = ServiceLocator.Get<AssetLibrary>();
            return library.Count == 0 ? "Загрузите хотя бы один ассет." : null;
        }

        public void Dispose()
        {
            if (ServiceLocator.TryGet<AssetUploader>(out var uploader))
            {
                uploader.OnTaskQueued    -= OnTaskQueued;
                uploader.OnTaskCompleted -= OnTaskCompleted;
            }
            if (ServiceLocator.TryGet<AssetLibrary>(out var library))
                library.OnChanged -= RenderLibrary;
        }

        // ---------- Upload ----------
        private void OpenFilePicker()
        {
            var paths = FileDialog.OpenFiles("Выберите файлы", FileDialog.AllAssetFilters, multiple: true);
            if (paths == null || paths.Length == 0) return;

            var uploader = ServiceLocator.Get<AssetUploader>();
            foreach (var p in paths)
                uploader.Enqueue(p, project.Id);
        }

        private void OnTaskQueued(UploadTask t)
        {
            var item = BuildUploadItem(t);
            uploadItems[t] = item;
            uploadList?.Add(item);
            t.OnChanged += RefreshUploadItem;
        }

        private void OnTaskCompleted(UploadTask t)
        {
            t.OnChanged -= RefreshUploadItem;
            if (t.Status == UploadStatus.Success && t.ResultAsset != null)
            {
                ServiceLocator.Get<AssetLibrary>().Add(t.ResultAsset);
                root.schedule.Execute(() =>
                {
                    if (uploadItems.TryGetValue(t, out var el)) { el.RemoveFromHierarchy(); uploadItems.Remove(t); }
                }).StartingIn(800);
            }
            else if (t.Status == UploadStatus.Failed)
            {
                Toast.Error($"{t.Filename}: {t.ErrorMessage}");
            }
        }

        private VisualElement BuildUploadItem(UploadTask t)
        {
            // Структура соответствует .upload-progress-row в Steps.uss
            var el = new VisualElement(); el.AddToClassList("upload-progress-row");

            var name = new Label($"{t.Filename}  ({FormatSize(t.SizeBytes)})");
            name.AddToClassList("upload-progress-row__name");
            el.Add(name);

            var bar = new VisualElement(); bar.AddToClassList("upload-progress-row__progress");
            var fill = new VisualElement { name = "fill" };
            fill.style.width = new Length(0, LengthUnit.Percent);
            bar.Add(fill);
            el.Add(bar);

            return el;
        }

        private void RefreshUploadItem(UploadTask t)
        {
            if (!uploadItems.TryGetValue(t, out var el)) return;
            var fill = el.Q<VisualElement>("fill");
            if (fill != null) fill.style.width = new Length(Mathf.Clamp01(t.Progress) * 100f, LengthUnit.Percent);
        }

        // ---------- Library ----------
        private void RenderLibrary()
        {
            if (libraryGrid == null) return;
            libraryGrid.Clear();

            var library = ServiceLocator.Get<AssetLibrary>();
            var items = library.FilterByType(currentFilter).ToList();

            if (assetCount != null) assetCount.text = $"Библиотека ({items.Count})";

            foreach (var a in items)
            {
                // ── card ──
                // Inline-стили дублируют CSS — на случай если Steps.uss закэширован Unity
                // и не подхватил новые правила. Гарантируем нормальную верстку даже без USS.
                var card = new VisualElement();
                card.AddToClassList("asset-card");
                card.style.width            = new Length(33, LengthUnit.Percent);
                card.style.minWidth         = 110;
                card.style.height           = 160;
                card.style.marginTop        = 4;
                card.style.marginLeft       = 4;
                card.style.marginRight      = 4;
                card.style.marginBottom     = 4;
                card.style.backgroundColor  = new StyleColor(new Color32(0x2A, 0x2A, 0x3D, 0xFF));
                card.style.borderTopLeftRadius = 8;
                card.style.borderTopRightRadius = 8;
                card.style.borderBottomLeftRadius = 8;
                card.style.borderBottomRightRadius = 8;
                card.style.borderTopWidth    = 1;
                card.style.borderBottomWidth = 1;
                card.style.borderLeftWidth   = 1;
                card.style.borderRightWidth  = 1;
                card.style.borderTopColor    = new StyleColor(new Color32(0x3F, 0x3F, 0x5C, 0xFF));
                card.style.borderBottomColor = new StyleColor(new Color32(0x3F, 0x3F, 0x5C, 0xFF));
                card.style.borderLeftColor   = new StyleColor(new Color32(0x3F, 0x3F, 0x5C, 0xFF));
                card.style.borderRightColor  = new StyleColor(new Color32(0x3F, 0x3F, 0x5C, 0xFF));
                card.style.overflow          = Overflow.Hidden;
                card.style.flexDirection     = FlexDirection.Column;

                // ── preview (большая зона с эмодзи-иконкой посередине) ──
                var preview = new VisualElement();
                preview.AddToClassList("asset-card__preview");
                preview.style.flexGrow         = 1;
                preview.style.backgroundColor  = new StyleColor(new Color32(0x36, 0x36, 0x50, 0xFF));
                preview.style.alignItems       = Align.Center;
                preview.style.justifyContent   = Justify.Center;

                var icon = new Label(IconForType(a.Type));
                icon.AddToClassList("asset-card__icon");
                icon.style.fontSize    = 38;
                icon.style.color       = new StyleColor(new Color32(0xC0, 0xC0, 0xCC, 0xFF));
                icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                icon.pickingMode       = PickingMode.Ignore;
                preview.Add(icon);
                card.Add(preview);

                // ── info (имя + размер внизу) ──
                var info = new VisualElement();
                info.AddToClassList("asset-card__info");
                info.style.paddingTop    = 6;
                info.style.paddingBottom = 6;
                info.style.paddingLeft   = 8;
                info.style.paddingRight  = 8;
                info.style.backgroundColor = new StyleColor(new Color32(0x2A, 0x2A, 0x3D, 0xFF));
                info.style.flexShrink    = 0;

                var nameLbl = new Label(a.Filename ?? "");
                nameLbl.AddToClassList("asset-card__name");
                nameLbl.style.fontSize  = 11;
                nameLbl.style.color     = new StyleColor(new Color32(0xF0, 0xF0, 0xF5, 0xFF));
                nameLbl.style.overflow  = Overflow.Hidden;
                nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
                nameLbl.style.textOverflow = TextOverflow.Ellipsis;
                nameLbl.tooltip = a.Filename;

                var sizeLbl = new Label(a.SizeHumanReadable());
                sizeLbl.AddToClassList("asset-card__size");
                sizeLbl.style.fontSize = 10;
                sizeLbl.style.color    = new StyleColor(new Color32(0x6B, 0x72, 0x80, 0xFF));

                info.Add(nameLbl);
                info.Add(sizeLbl);
                card.Add(info);

                // ── delete button (absolute top-right) ──
                var delBtn = new Button(() => DeleteAsset(a)) { text = "✕" };
                delBtn.AddToClassList("asset-card__remove");
                delBtn.style.position = Position.Absolute;
                delBtn.style.top      = 6;
                delBtn.style.right    = 6;
                delBtn.style.width    = 24;
                delBtn.style.height   = 24;
                delBtn.style.marginTop = 0;
                delBtn.style.marginBottom = 0;
                delBtn.style.marginLeft = 0;
                delBtn.style.marginRight = 0;
                delBtn.style.paddingTop = 0;
                delBtn.style.paddingBottom = 0;
                delBtn.style.paddingLeft = 0;
                delBtn.style.paddingRight = 0;
                delBtn.style.borderTopLeftRadius = 12;
                delBtn.style.borderTopRightRadius = 12;
                delBtn.style.borderBottomLeftRadius = 12;
                delBtn.style.borderBottomRightRadius = 12;
                delBtn.style.borderTopWidth = 1;
                delBtn.style.borderBottomWidth = 1;
                delBtn.style.borderLeftWidth = 1;
                delBtn.style.borderRightWidth = 1;
                delBtn.style.borderTopColor    = new StyleColor(new Color(0.97f, 0.44f, 0.44f, 0.4f));
                delBtn.style.borderBottomColor = new StyleColor(new Color(0.97f, 0.44f, 0.44f, 0.4f));
                delBtn.style.borderLeftColor   = new StyleColor(new Color(0.97f, 0.44f, 0.44f, 0.4f));
                delBtn.style.borderRightColor  = new StyleColor(new Color(0.97f, 0.44f, 0.44f, 0.4f));
                delBtn.style.backgroundColor   = new StyleColor(new Color(0.06f, 0.06f, 0.10f, 0.85f));
                delBtn.style.color             = new StyleColor(new Color(0.97f, 0.44f, 0.44f, 1f));
                delBtn.style.fontSize          = 12;
                delBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                delBtn.style.unityTextAlign    = TextAnchor.MiddleCenter;
                card.Add(delBtn);

                libraryGrid.Add(card);
            }
        }

        private static string IconForType(AssetType t) => t switch
        {
            AssetType.Image   => "🖼",
            AssetType.Video   => "🎬",
            AssetType.Audio   => "🎵",
            AssetType.Model3D => "📦",
            AssetType.Text    => "📄",
            _                 => "?"
        };

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} МБ";
            return $"{bytes / (1024.0 * 1024 * 1024):0.#} ГБ";
        }

        private async void DeleteAsset(AssetModel a)
        {
            try { await ServiceLocator.Get<AssetLibrary>().DeleteAsync(a.Id); Toast.Info("Ассет удалён."); }
            catch (Exception ex) { Debug.LogException(ex); Toast.Error("Не удалось удалить ассет."); }
        }
    }
}
