using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InteractiveClient.AssetsModule;
using InteractiveClient.Core;
using InteractiveClient.Mapping;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 3 — маппинг ассетов в слоты пресета.
    /// Drag-and-drop в UI Toolkit реализован через pointer-события:
    /// на asset-card запоминаем selected; по клику на слот — присваиваем.
    /// (Полноценный DnD с visual-ghost — задача отдельного контроллера.)
    /// </summary>
    public class Step3MappingController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step3_Mapping";

        private ProjectModel project;
        private VisualElement root;

        private VisualElement assetsGrid;
        private VisualElement slotsList;
        private VisualElement filters;
        private Label statusLbl;
        private Button autoMapBtn;

        private AssetType? assetFilter;
        private string selectedAssetId;
        private PresetInfo preset;

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.root = root;
            this.project = project;

            assetsGrid = root.Q<VisualElement>("mapping-assets-grid");
            slotsList  = root.Q<VisualElement>("mapping-slots-list");
            filters    = root.Q<VisualElement>(className: "asset-filters");
            statusLbl  = root.Q<Label>("mapping-status");
            autoMapBtn = root.Q<Button>("auto-mapping-btn");

            assetsGrid?.Clear();
            slotsList?.Clear();

            if (autoMapBtn != null) autoMapBtn.clicked += () => _ = RunAutoMappingAsync();

            if (filters != null)
            {
                var buttons = filters.Query<Button>().ToList();
                AssetType?[] map = { null, AssetType.Image, AssetType.Video, AssetType.Audio, AssetType.Model3D };
                for (int i = 0; i < buttons.Count && i < map.Length; i++)
                {
                    var type = map[i];
                    var btn = buttons[i];
                    btn.clicked += () =>
                    {
                        assetFilter = type;
                        foreach (var b in buttons) b.RemoveFromClassList("library-filter-btn--active");
                        btn.AddToClassList("library-filter-btn--active");
                        RenderAssets();
                    };
                }
            }
        }

        public async Task EnterAsync()
        {
            if (string.IsNullOrEmpty(project.PresetId))
            {
                Toast.Warning("Сначала выберите пресет на шаге 2.");
                return;
            }

            preset = await TryLoadPresetSchemaAsync(project.PresetId);

            if (preset == null)
            {
                Toast.Error("Не удалось получить схему пресета.");
                return;
            }

            RenderAssets();
            RenderSlots();
            UpdateValidation();
        }

        /// <summary>
        /// Загружает схему пресета: сначала пробует серверный PresetRegistry,
        /// при ошибке (пресет не зарегистрирован на сервере) берёт локальную схему.
        /// </summary>
        private static async Task<PresetInfo> TryLoadPresetSchemaAsync(string presetId)
        {
            try
            {
                var registry = ServiceLocator.Get<PresetRegistry>();
                var info = registry.Get(presetId);
                if (info != null)
                {
                    // Пресет есть в registry — подгружаем схему если ещё не загружена
                    if (!info.IsSchemaLoaded)
                        await registry.LoadSchemaAsync(presetId);
                    return info;
                }
                // Пресета нет в registry (сервер не знает о нём) — используем локальный fallback
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Step3] PresetRegistry недоступен для '{presetId}': {ex.Message}");
            }

            // Локальная схема (hardcoded для 5 встроенных пресетов)
            var local = LocalPresetSchemas.Get(presetId);
            if (local == null)
                Debug.LogWarning($"[Step3] Локальная схема не найдена для presetId='{presetId}'");
            return local;
        }

        public Task LeaveAsync() => Task.CompletedTask;

        public string Validate()
        {
            if (preset == null) return "Не загружена схема пресета.";
            var library = ServiceLocator.Get<AssetLibrary>();
            var res = MappingValidator.Validate(preset.Slots, project.AssetMapping, library);
            if (!res.IsComplete)
                return $"Заполните обязательные слоты ({res.RequiredFilled}/{res.RequiredTotal}).";
            return null;
        }

        public void Dispose() { }

        // ---------- Assets panel ----------
        private void RenderAssets()
        {
            if (assetsGrid == null) return;
            assetsGrid.Clear();

            // Контейнер карточек: row + wrap (важно — на случай если CSS не подхвачен)
            assetsGrid.style.flexDirection = FlexDirection.Row;
            assetsGrid.style.flexWrap = Wrap.Wrap;

            var library = ServiceLocator.Get<AssetLibrary>();
            foreach (var a in library.FilterByType(assetFilter))
            {
                bool selected = selectedAssetId == a.Id;

                // ── card ──
                var card = new VisualElement();
                card.AddToClassList("mapping-asset-card");
                if (selected) card.AddToClassList("mapping-asset-card--selected");
                card.style.width            = new Length(48, LengthUnit.Percent);
                card.style.minWidth         = 130;
                card.style.height           = 120;
                card.style.marginTop        = 4;
                card.style.marginLeft       = 4;
                card.style.marginRight      = 4;
                card.style.marginBottom     = 4;
                card.style.paddingTop       = 6;
                card.style.paddingLeft      = 6;
                card.style.paddingRight     = 6;
                card.style.paddingBottom    = 6;
                card.style.flexDirection    = FlexDirection.Column;
                card.style.backgroundColor  = (Color)new Color32(0x2A, 0x2A, 0x3D, 0xFF);
                card.style.borderTopLeftRadius     = 8;
                card.style.borderTopRightRadius    = 8;
                card.style.borderBottomLeftRadius  = 8;
                card.style.borderBottomRightRadius = 8;
                card.style.borderTopWidth    = 1;
                card.style.borderBottomWidth = 1;
                card.style.borderLeftWidth   = 1;
                card.style.borderRightWidth  = 1;
                Color borderCol = selected
                    ? (Color)new Color32(0x4A, 0x9E, 0xFF, 0xFF)
                    : (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
                card.style.borderTopColor    = borderCol;
                card.style.borderBottomColor = borderCol;
                card.style.borderLeftColor   = borderCol;
                card.style.borderRightColor  = borderCol;
                if (selected)
                    card.style.backgroundColor = new Color(0.29f, 0.62f, 1.00f, 0.10f);

                // ── preview (большая зона с эмодзи-иконкой) ──
                var preview = new VisualElement();
                preview.AddToClassList("mapping-asset-card__preview");
                preview.style.flexGrow        = 1;
                preview.style.backgroundColor = (Color)new Color32(0x36, 0x36, 0x50, 0xFF);
                preview.style.borderTopLeftRadius     = 5;
                preview.style.borderTopRightRadius    = 5;
                preview.style.borderBottomLeftRadius  = 5;
                preview.style.borderBottomRightRadius = 5;
                preview.style.alignItems      = Align.Center;
                preview.style.justifyContent  = Justify.Center;
                preview.style.marginBottom    = 4;

                var iconLbl = new Label(IconForAssetType(a.Type));
                iconLbl.style.fontSize       = 28;
                iconLbl.style.color          = (Color)new Color32(0xC0, 0xC0, 0xCC, 0xFF);
                iconLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                iconLbl.pickingMode          = PickingMode.Ignore;
                preview.Add(iconLbl);
                card.Add(preview);

                // ── name + type ──
                var name = new Label(a.Filename ?? "");
                name.AddToClassList("mapping-asset-card__name");
                name.style.fontSize     = 11;
                name.style.color        = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
                name.style.overflow     = Overflow.Hidden;
                name.style.whiteSpace   = WhiteSpace.NoWrap;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.tooltip            = a.Filename;

                var type = new Label(a.Type.ToDisplayString());
                type.AddToClassList("mapping-asset-card__type");
                type.style.fontSize = 10;
                type.style.color    = (Color)new Color32(0x6B, 0x72, 0x80, 0xFF);

                card.Add(name);
                card.Add(type);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    selectedAssetId = a.Id;
                    RenderAssets();
                });
                assetsGrid.Add(card);
            }
        }

        private static string IconForAssetType(AssetType t) => t switch
        {
            AssetType.Image   => "🖼",
            AssetType.Video   => "🎬",
            AssetType.Audio   => "🎵",
            AssetType.Model3D => "🧊",
            AssetType.Text    => "📄",
            _                  => "📁"
        };

        // ---------- Slots panel ----------
        private void RenderSlots()
        {
            if (slotsList == null || preset == null) return;
            slotsList.Clear();

            var library = ServiceLocator.Get<AssetLibrary>();
            var groups = preset.Slots.GroupBy(s => s.Group ?? "Основные");

            foreach (var g in groups)
            {
                var title = new Label(g.Key);
                title.AddToClassList("mapping-group-title");
                title.style.fontSize    = 12;
                title.style.color       = (Color)new Color32(0x9C, 0xA3, 0xAF, 0xFF);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.marginTop    = 12;
                title.style.marginBottom = 6;
                slotsList.Add(title);

                foreach (var slot in g)
                    slotsList.Add(BuildSlotItem(slot, library));
            }
        }

        private VisualElement BuildSlotItem(SlotDefinition slot, AssetLibrary library)
        {
            project.AssetMapping.TryGetValue(slot.Id, out var assignedId);
            var asset = string.IsNullOrEmpty(assignedId) ? null : library.Get(assignedId);
            bool filled = asset != null;

            // ── slot-row ──
            var el = new VisualElement();
            el.AddToClassList("slot-row");
            if (filled) el.AddToClassList("slot-row--filled");
            el.style.flexDirection            = FlexDirection.Column;
            el.style.backgroundColor          = filled
                ? new Color(0.29f, 0.87f, 0.50f, 0.06f)
                : (Color)new Color32(0x2A, 0x2A, 0x3D, 0xFF);
            el.style.borderTopLeftRadius      = 8;
            el.style.borderTopRightRadius     = 8;
            el.style.borderBottomLeftRadius   = 8;
            el.style.borderBottomRightRadius  = 8;
            el.style.borderTopWidth    = 2;
            el.style.borderBottomWidth = 2;
            el.style.borderLeftWidth   = 2;
            el.style.borderRightWidth  = 2;
            Color slotBorder = filled
                ? (Color)new Color32(0x4A, 0xDE, 0x80, 0xFF)
                : (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            el.style.borderTopColor    = slotBorder;
            el.style.borderBottomColor = slotBorder;
            el.style.borderLeftColor   = slotBorder;
            el.style.borderRightColor  = slotBorder;
            el.style.paddingTop    = 12;
            el.style.paddingBottom = 12;
            el.style.paddingLeft   = 14;
            el.style.paddingRight  = 14;
            el.style.marginBottom  = 8;

            // ── head: title + (required) + (✕ remove) ──
            var head = new VisualElement();
            head.AddToClassList("slot-row__head");
            head.style.flexDirection  = FlexDirection.Row;
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems     = Align.Center;
            head.style.marginBottom   = 4;

            var titleWrap = new VisualElement();
            titleWrap.AddToClassList("slot-row__title");
            titleWrap.style.flexDirection = FlexDirection.Row;
            titleWrap.style.alignItems    = Align.Center;

            var name = new Label(slot.Name ?? slot.Id);
            name.AddToClassList("slot-row__name");
            name.style.fontSize = 13;
            name.style.color    = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleWrap.Add(name);

            if (slot.Required)
            {
                var req = new Label("*");
                req.AddToClassList("slot-row__required");
                req.style.fontSize    = 13;
                req.style.color       = (Color)new Color32(0xF8, 0x71, 0x71, 0xFF);
                req.style.marginLeft  = 4;
                req.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleWrap.Add(req);
            }
            head.Add(titleWrap);

            if (filled)
            {
                var rm = new Button(() => { project.AssetMapping.Remove(slot.Id); project.MarkDirty(); RenderSlots(); UpdateValidation(); })
                    { text = "✕" };
                rm.AddToClassList("slot-row__remove");
                rm.style.width  = 26;
                rm.style.height = 26;
                rm.style.marginTop = 0;
                rm.style.marginBottom = 0;
                rm.style.marginLeft = 0;
                rm.style.marginRight = 0;
                rm.style.paddingTop = 0;
                rm.style.paddingBottom = 0;
                rm.style.paddingLeft = 0;
                rm.style.paddingRight = 0;
                rm.style.backgroundColor = new Color(0.97f, 0.44f, 0.44f, 0.12f);
                rm.style.borderTopWidth = 1;
                rm.style.borderBottomWidth = 1;
                rm.style.borderLeftWidth = 1;
                rm.style.borderRightWidth = 1;
                rm.style.borderTopColor    = new Color(0.97f, 0.44f, 0.44f, 0.4f);
                rm.style.borderBottomColor = new Color(0.97f, 0.44f, 0.44f, 0.4f);
                rm.style.borderLeftColor   = new Color(0.97f, 0.44f, 0.44f, 0.4f);
                rm.style.borderRightColor  = new Color(0.97f, 0.44f, 0.44f, 0.4f);
                rm.style.borderTopLeftRadius = 5;
                rm.style.borderTopRightRadius = 5;
                rm.style.borderBottomLeftRadius = 5;
                rm.style.borderBottomRightRadius = 5;
                rm.style.color = (Color)new Color32(0xF8, 0x71, 0x71, 0xFF);
                rm.style.fontSize = 14;
                rm.style.unityFontStyleAndWeight = FontStyle.Bold;
                rm.style.unityTextAlign = TextAnchor.MiddleCenter;
                head.Add(rm);
            }
            el.Add(head);

            // ── type label ──
            var typeLbl = new Label($"Тип: {slot.DisplayTypeString()}");
            typeLbl.AddToClassList("slot-row__type");
            typeLbl.style.fontSize    = 11;
            typeLbl.style.color       = (Color)new Color32(0x6B, 0x72, 0x80, 0xFF);
            typeLbl.style.marginBottom = 6;
            el.Add(typeLbl);

            // ── filled / placeholder ──
            if (filled)
            {
                var filledLbl = new Label($"✓ {asset.Filename}");
                filledLbl.AddToClassList("slot-row__filled");
                filledLbl.style.backgroundColor = (Color)new Color32(0x36, 0x36, 0x50, 0xFF);
                filledLbl.style.borderTopLeftRadius = 4;
                filledLbl.style.borderTopRightRadius = 4;
                filledLbl.style.borderBottomLeftRadius = 4;
                filledLbl.style.borderBottomRightRadius = 4;
                filledLbl.style.paddingTop = 6;
                filledLbl.style.paddingBottom = 6;
                filledLbl.style.paddingLeft = 8;
                filledLbl.style.paddingRight = 8;
                filledLbl.style.fontSize = 11;
                filledLbl.style.color = (Color)new Color32(0x4A, 0xDE, 0x80, 0xFF);
                el.Add(filledLbl);
            }
            else
            {
                var ph = new Label(string.IsNullOrEmpty(selectedAssetId)
                    ? $"Выберите {slot.DisplayTypeString().ToLower()} слева и кликните здесь"
                    : "Кликните, чтобы назначить выбранный ассет");
                ph.AddToClassList("slot-row__placeholder");
                ph.style.fontSize    = 11;
                ph.style.color       = (Color)new Color32(0x6B, 0x72, 0x80, 0xFF);
                ph.style.whiteSpace  = WhiteSpace.Normal;
                el.Add(ph);

                el.RegisterCallback<ClickEvent>(_ => AssignSelectedToSlot(slot));
            }

            return el;
        }

        private void AssignSelectedToSlot(SlotDefinition slot)
        {
            if (string.IsNullOrEmpty(selectedAssetId))
            {
                Toast.Warning("Сначала выберите ассет слева.");
                return;
            }
            var asset = ServiceLocator.Get<AssetLibrary>().Get(selectedAssetId);
            if (asset == null) return;
            if (!slot.Accepts(asset))
            {
                Toast.Warning($"Тип ассета не подходит для слота «{slot.Name}».");
                return;
            }

            project.AssetMapping[slot.Id] = asset.Id;
            project.MarkDirty();
            RenderSlots();
            UpdateValidation();
        }

        private void UpdateValidation()
        {
            if (statusLbl == null || preset == null) return;
            var library = ServiceLocator.Get<AssetLibrary>();
            var res = MappingValidator.Validate(preset.Slots, project.AssetMapping, library);
            statusLbl.text = $"Заполнено {res.RequiredFilled} из {res.RequiredTotal} обязательных слотов"
                             + (res.TypeMismatchSlotIds.Count > 0 ? $"  •  Несовпадений типов: {res.TypeMismatchSlotIds.Count}" : "");
            statusLbl.RemoveFromClassList("text-warning");
            statusLbl.RemoveFromClassList("text-success");
            statusLbl.AddToClassList(res.IsComplete ? "text-success" : "text-warning");

            // Inline-цвет — CSS класс не перебьёт inline color из UXML.
            statusLbl.style.color = res.IsComplete
                ? (Color)new Color32(0x4A, 0xDE, 0x80, 0xFF)   // success green
                : (Color)new Color32(0xFB, 0xBF, 0x24, 0xFF);  // warning amber
        }

        // ---------- Auto-mapping (client-side, type-based) ----------
        private async Task RunAutoMappingAsync()
        {
            if (preset == null) return;

            autoMapBtn?.SetEnabled(false);
            try
            {
                var library = ServiceLocator.Get<AssetLibrary>();
                var allAssets = library.All.ToList();
                if (allAssets.Count == 0) { Toast.Warning("Нет ассетов."); return; }

                // Track which asset IDs have already been assigned in this run
                var used = new HashSet<string>(project.AssetMapping.Values
                    .Where(v => !string.IsNullOrEmpty(v)));

                int assigned = 0;

                // First pass: required slots
                foreach (var slot in preset.Slots.Where(s => s.Required))
                {
                    if (project.AssetMapping.TryGetValue(slot.Id, out var existing)
                        && !string.IsNullOrEmpty(existing))
                        continue; // already mapped

                    var match = allAssets.FirstOrDefault(
                        a => !used.Contains(a.Id) && slot.Accepts(a));
                    if (match != null)
                    {
                        project.AssetMapping[slot.Id] = match.Id;
                        used.Add(match.Id);
                        assigned++;
                    }
                }

                // Second pass: optional slots
                foreach (var slot in preset.Slots.Where(s => !s.Required))
                {
                    if (project.AssetMapping.TryGetValue(slot.Id, out var existing)
                        && !string.IsNullOrEmpty(existing))
                        continue;

                    var match = allAssets.FirstOrDefault(
                        a => !used.Contains(a.Id) && slot.Accepts(a));
                    if (match != null)
                    {
                        project.AssetMapping[slot.Id] = match.Id;
                        used.Add(match.Id);
                        assigned++;
                    }
                }

                if (assigned == 0)
                {
                    Toast.Warning("Подходящих ассетов не найдено.");
                    return;
                }

                project.MarkDirty();
                RenderSlots();
                UpdateValidation();
                Toast.Success($"Авто-маппинг применён: назначено {assigned} слотов.");

                await Task.CompletedTask; // keep async signature
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Ошибка авто-маппинга.");
            }
            finally
            {
                autoMapBtn?.SetEnabled(true);
            }
        }
    }
}
