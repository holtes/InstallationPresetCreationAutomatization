using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InteractiveClient.AssetsModule;
using InteractiveClient.Core;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 2 — выбор пресета механики. При клике по карточке загружаем схему
    /// и записываем presetId в ProjectModel (с MarkDirty).
    /// Кнопка «Выбрать» активна только если загружено достаточно ассетов нужного типа.
    /// </summary>
    public class Step2PresetsController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step2_Presets";

        // ── Структура требований для пресета ──────────────────────────────────
        private readonly struct PresetRequirement
        {
            public readonly AssetType Type;
            public readonly int MinCount;
            public readonly string HumanHint; // «Нужно: ...» при нехватке ассетов

            public PresetRequirement(AssetType type, int minCount, string humanHint)
            {
                Type = type;
                MinCount = minCount;
                HumanHint = humanHint;
            }
        }

        // ── Карта: NodeId → (PresetId, Требование) ────────────────────────────
        private static readonly (string Node, string PresetId, PresetRequirement Req)[] CardMap =
        {
            ("preset-puzzle",    "puzzle",
                new PresetRequirement(AssetType.Image,   1, "Нужно хотя бы 1 изображение")),
            ("preset-memory",    "memory",
                new PresetRequirement(AssetType.Image,   4, "Нужно хотя бы 4 изображения")),
            ("preset-quiz",      "quiz",
                new PresetRequirement(AssetType.Text,    1, "Нужен хотя бы 1 JSON/TXT файл")),
            ("preset-video-quiz","video_quiz",
                new PresetRequirement(AssetType.Video,   2, "Нужно хотя бы 2 видео + 1 JSON")),
            ("preset-scene-3d",  "scene_3d",
                new PresetRequirement(AssetType.Model3D, 1, "Нужна хотя бы 1 3D-модель")),
        };

        // ── Локальные метаданные пресетов (не зависят от сервера) ─────────────
        private static readonly Dictionary<string, string> LocalPresetNames = new()
        {
            ["puzzle"]     = "Пазл",
            ["memory"]     = "Мемори",
            ["quiz"]       = "Викторина",
            ["video_quiz"] = "Видео-угадайка",
            ["scene_3d"]   = "3D Осмотр",
        };

        private ProjectModel project;
        private VisualElement root;
        private VisualElement selectedInfo;
        private Label selectedName;
        private Label selectedSlots;
        private AssetLibrary assetLibrary;

        // ── IEditorStep ────────────────────────────────────────────────────────

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.root = root;
            this.project = project;
            assetLibrary = ServiceLocator.Get<AssetLibrary>();

            selectedInfo  = root.Q<VisualElement>("selected-preset-info");
            selectedName  = root.Q<Label>("selected-preset-name");
            selectedSlots = root.Q<Label>("selected-preset-slots");

            foreach (var (node, presetId, _) in CardMap)
            {
                var card = root.Q<VisualElement>(node);
                if (card == null) continue;

                // Кнопка по имени (например "preset-puzzle-btn")
                var btn = root.Q<Button>($"{node}-btn");

                void Select() => _ = SelectPresetAsync(presetId);

                if (btn != null) btn.clicked += Select;
                card.RegisterCallback<ClickEvent>(ev =>
                {
                    if (ev.target is Button) return;
                    Select();
                });
            }

            // Обновлять доступность при изменении библиотеки ассетов
            assetLibrary.OnChanged += RefreshAvailability;
        }

        public async Task EnterAsync()
        {
            try { await ServiceLocator.Get<PresetRegistry>().LoadListAsync(); }
            catch (Exception ex) { Debug.LogException(ex); }

            RefreshAvailability();
            HighlightSelected();
            await ShowSelectedInfo();
        }

        public Task LeaveAsync() => Task.CompletedTask;

        public string Validate()
            => string.IsNullOrEmpty(project.PresetId) ? "Выберите пресет механики." : null;

        public void Dispose()
        {
            if (assetLibrary != null)
                assetLibrary.OnChanged -= RefreshAvailability;
        }

        // ── Availability ───────────────────────────────────────────────────────

        /// <summary>
        /// Обходит все карточки и включает/отключает кнопку «Выбрать»
        /// в зависимости от наличия необходимых ассетов в библиотеке.
        /// </summary>
        private void RefreshAvailability()
        {
            if (root == null) return;

            foreach (var (node, _, req) in CardMap)
            {
                var btn   = root.Q<Button>($"{node}-btn");
                var avail = root.Q<Label>($"{node}-avail");

                var actualCount = assetLibrary.FilterByType(req.Type).Count();

                // Для video_quiz ещё нужен JSON — делаем дополнительную проверку
                bool extraOk = true;
                if (node == "preset-video-quiz")
                    extraOk = assetLibrary.FilterByType(AssetType.Text).Any();

                bool ok = actualCount >= req.MinCount && extraOk;

                if (btn != null)
                {
                    btn.SetEnabled(ok);
                    btn.tooltip = ok ? "" : req.HumanHint;

                    // Inline-стили перебивают CSS :disabled — гасим accent на disabled-кнопке вручную.
                    // ВАЖНО: borderXxxColor / color принимают StyleColor, у Color32→StyleColor нет
                    // implicit-конверсии — приводим к Color.
                    bool selected = project.PresetId != null
                        && CardMap.Any(c => c.Node == node && c.PresetId == project.PresetId);
                    Color borderCol = (!ok && !selected)
                        ? (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF)
                        : (Color)new Color32(0x4A, 0x9E, 0xFF, 0xFF);
                    btn.style.borderTopColor    = borderCol;
                    btn.style.borderBottomColor = borderCol;
                    btn.style.borderLeftColor   = borderCol;
                    btn.style.borderRightColor  = borderCol;
                    if (!ok && !selected)
                        btn.style.color = (Color)new Color32(0x4B, 0x55, 0x63, 0xFF);
                }

                if (avail != null)
                {
                    if (ok)
                    {
                        avail.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        avail.text = BuildAvailabilityText(node, req, actualCount, extraOk);
                        avail.style.display = DisplayStyle.Flex;
                    }
                }
            }
        }

        private string BuildAvailabilityText(
            string node, PresetRequirement req, int actualCount, bool extraOk)
        {
            if (node == "preset-video-quiz" && !extraOk && actualCount >= req.MinCount)
                return "Нет JSON-файла с вопросами";
            if (actualCount == 0)
                return req.HumanHint;
            return $"{req.HumanHint} (сейчас: {actualCount})";
        }

        // ── Выбор пресета ──────────────────────────────────────────────────────

        private async Task SelectPresetAsync(string presetId)
        {
            if (project.PresetId == presetId)
            {
                Toast.Info("Пресет уже выбран.");
                return;
            }

            // Проверяем наличие ассетов прямо перед выбором (дублируем RefreshAvailability)
            var entry = CardMap.FirstOrDefault(c => c.PresetId == presetId);
            if (!string.IsNullOrEmpty(entry.Node))
            {
                var count = assetLibrary.FilterByType(entry.Req.Type).Count();
                if (count < entry.Req.MinCount)
                {
                    Toast.Warning(entry.Req.HumanHint);
                    return;
                }
            }

            // ── Сохраняем выбор сразу, не ждём ответа сервера ─────────────
            project.PresetId = presetId;
            project.AssetMapping.Clear();
            project.MarkDirty();

            EventBus.Publish(new PresetSelectedEvent(presetId));
            HighlightSelected();

            // Отображаемое имя — из локального словаря, registry обогащает если доступен
            string displayName = LocalPresetNames.TryGetValue(presetId, out var n) ? n : presetId;

            try
            {
                var registry = ServiceLocator.Get<PresetRegistry>();
                var info = registry.Get(presetId);
                if (info != null)
                {
                    if (!info.IsSchemaLoaded)
                        await registry.LoadSchemaAsync(presetId);
                    displayName = info.Name ?? displayName;
                    project.PresetVersion = info.Version ?? project.PresetVersion;
                }
                // Если сервер не знает о пресете — просто игнорируем (пресет локальный)
            }
            catch (Exception ex)
            {
                // Registry недоступен — не критично, пресет уже выбран локально
                Debug.LogWarning($"[Step2] PresetRegistry lookup skipped for '{presetId}': {ex.Message}");
            }

            await ShowSelectedInfo(displayName);
            Toast.Success($"Пресет «{displayName}» выбран.");
        }

        // ── Подсветка выбранного ───────────────────────────────────────────────

        // Цвета для двух состояний — обычная и выбранная карточка.
        // Дублируем то что есть в CSS, потому что у нас inline-стили в UXML
        // и CSS-класс .preset-card--selected их не перебивает.
        private static readonly Color BorderNormal       = new Color32(0x3F, 0x3F, 0x5C, 0xFF);
        private static readonly Color BorderSelected     = new Color32(0x4A, 0x9E, 0xFF, 0xFF);
        private static readonly Color CardBgNormal       = new Color32(0x2A, 0x2A, 0x3D, 0xFF);
        private static readonly Color CardBgSelected     = new Color(0.29f, 0.62f, 1.00f, 0.06f);
        private static readonly Color IconWrapBgNormal   = new Color(0.29f, 0.62f, 1.00f, 0.12f);
        private static readonly Color IconWrapBgSelected = new Color(0.29f, 0.62f, 1.00f, 0.22f);
        private static readonly Color IconWrapBordNormal = new Color(0.29f, 0.62f, 1.00f, 0.35f);
        private static readonly Color BtnFillSelected    = new Color32(0x4A, 0x9E, 0xFF, 0xFF);
        private static readonly Color BtnTextSelected    = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color BtnFillNormal      = new Color(0, 0, 0, 0);
        private static readonly Color BtnTextNormal      = new Color32(0x4A, 0x9E, 0xFF, 0xFF);

        private void HighlightSelected()
        {
            foreach (var (node, presetId, _) in CardMap)
            {
                var card = root.Q<VisualElement>(node);
                if (card == null) continue;

                bool selected = project.PresetId == presetId;
                card.EnableInClassList("preset-card--selected", selected);

                // Карточка
                card.style.borderTopColor    = selected ? BorderSelected : BorderNormal;
                card.style.borderBottomColor = selected ? BorderSelected : BorderNormal;
                card.style.borderLeftColor   = selected ? BorderSelected : BorderNormal;
                card.style.borderRightColor  = selected ? BorderSelected : BorderNormal;
                card.style.backgroundColor   = selected ? new StyleColor(CardBgSelected) : new StyleColor(CardBgNormal);

                // Кружок-иконка
                var iconWrap = card.Q<VisualElement>(className: "preset-card__icon-wrap");
                if (iconWrap != null)
                {
                    iconWrap.style.backgroundColor   = selected ? new StyleColor(IconWrapBgSelected) : new StyleColor(IconWrapBgNormal);
                    iconWrap.style.borderTopColor    = selected ? BorderSelected : IconWrapBordNormal;
                    iconWrap.style.borderBottomColor = selected ? BorderSelected : IconWrapBordNormal;
                    iconWrap.style.borderLeftColor   = selected ? BorderSelected : IconWrapBordNormal;
                    iconWrap.style.borderRightColor  = selected ? BorderSelected : IconWrapBordNormal;
                }

                // Кнопка «Выбрать» / «✓ Выбрано»
                var btn = card.Q<Button>(className: "preset-select-btn");
                if (btn != null)
                {
                    btn.style.backgroundColor = selected ? new StyleColor(BtnFillSelected) : new StyleColor(BtnFillNormal);
                    btn.style.color           = selected ? BtnTextSelected : BtnTextNormal;
                    btn.text = selected ? "✓ Выбрано" : "Выбрать";
                }
            }
        }

        private async Task ShowSelectedInfo(string overrideName = null)
        {
            if (selectedInfo == null) return;
            if (string.IsNullOrEmpty(project.PresetId))
            {
                selectedInfo.AddToClassList("hidden");
                selectedInfo.style.display = DisplayStyle.None;
                return;
            }

            // Имя: переданное > registry > локальный словарь > raw id
            string name = overrideName
                ?? (LocalPresetNames.TryGetValue(project.PresetId, out var ln) ? ln : project.PresetId);
            string slotsText = "";

            try
            {
                var registry = ServiceLocator.Get<PresetRegistry>();
                var preset = registry.Get(project.PresetId);
                if (preset != null)
                {
                    if (!preset.IsSchemaLoaded)
                        await registry.LoadSchemaAsync(project.PresetId);
                    name = preset.Name ?? name;
                    slotsText = $"Слотов ассетов: {preset.Slots.Count} (обязательных: {preset.RequiredSlotsCount})";
                }
            }
            catch { /* Registry недоступен — показываем что есть */ }

            if (selectedName  != null) selectedName.text  = name;
            if (selectedSlots != null) selectedSlots.text = slotsText;

            selectedInfo.RemoveFromClassList("hidden");
            selectedInfo.style.display = DisplayStyle.Flex;
        }
    }
}
