using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Network;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 4 — настройка параметров пресета.
    /// Форма строится динамически из схемы (сервер → локальный fallback).
    /// Каждый параметр сохраняется в project.Parameters (JObject) при изменении.
    /// Превью пресета здесь намеренно отсутствует — оно живёт на полноэкранном
    /// PreviewScreen, потому что часть пресетов (3D, видео и т.п.) нельзя
    /// корректно показать в маленькой превью-панели.
    /// </summary>
    public class Step4ParametersController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step4_Parameters";

        private ProjectModel project;
        private VisualElement root;
        private VisualElement container;

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.root      = root;
            this.project   = project;
            this.container = root.Q<VisualElement>("params-container");
        }

        public async Task EnterAsync()
        {
            if (string.IsNullOrEmpty(project.PresetId))
            {
                ShowMessage("Сначала выберите пресет на шаге 2.");
                return;
            }

            var preset = await TryLoadPresetSchemaAsync(project.PresetId);

            if (preset == null || preset.Parameters.Count == 0)
            {
                ShowMessage("У выбранного пресета нет настраиваемых параметров.");
                return;
            }

            BuildDynamicForm(preset.Parameters);
        }

        public Task LeaveAsync() => Task.CompletedTask;

        public string Validate() => null; // параметры опциональны

        public void Dispose() { }

        // ── Загрузка схемы (сервер → локальный fallback) ───────────────────────

        private static async Task<PresetInfo> TryLoadPresetSchemaAsync(string presetId)
        {
            try
            {
                var registry = ServiceLocator.Get<PresetRegistry>();
                var info = registry.Get(presetId);
                if (info != null)
                {
                    if (!info.IsSchemaLoaded)
                        await registry.LoadSchemaAsync(presetId);
                    if (info.Parameters.Count > 0)
                        return info;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Step4] PresetRegistry недоступен для '{presetId}': {ex.Message}");
            }

            return LocalPresetSchemas.Get(presetId);
        }

        // ── Построение формы ──────────────────────────────────────────────────

        private void BuildDynamicForm(List<ParameterSchemaDto> parameters)
        {
            if (container == null) return;
            container.Clear();

            var groups = parameters.GroupBy(p =>
                string.IsNullOrEmpty(p.Group) ? "Параметры" : p.Group);

            bool firstGroup = true;
            foreach (var g in groups)
            {
                container.Add(BuildCollapsibleGroup(g.Key, g.ToList(), openByDefault: firstGroup));
                firstGroup = false;
            }
        }

        private VisualElement BuildCollapsibleGroup(
            string title, List<ParameterSchemaDto> parameters, bool openByDefault)
        {
            // ── Wrapper-карточка группы (bg-secondary с рамкой) ──
            var wrap = new VisualElement();
            wrap.AddToClassList("collapsible");
            wrap.style.flexDirection             = FlexDirection.Column;
            wrap.style.backgroundColor           = (Color)new Color32(0x2A, 0x2A, 0x3D, 0xFF);
            wrap.style.borderTopLeftRadius       = 10;
            wrap.style.borderTopRightRadius      = 10;
            wrap.style.borderBottomLeftRadius    = 10;
            wrap.style.borderBottomRightRadius   = 10;
            wrap.style.borderTopWidth    = 1;
            wrap.style.borderBottomWidth = 1;
            wrap.style.borderLeftWidth   = 1;
            wrap.style.borderRightWidth  = 1;
            Color groupBorder = (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            wrap.style.borderTopColor    = groupBorder;
            wrap.style.borderBottomColor = groupBorder;
            wrap.style.borderLeftColor   = groupBorder;
            wrap.style.borderRightColor  = groupBorder;
            wrap.style.marginBottom      = 10;
            wrap.style.overflow          = Overflow.Hidden;

            // ── Header (кликабельная шапка) ──
            var header = new VisualElement();
            header.AddToClassList("collapsible__header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.height        = 44;
            header.style.paddingLeft   = 14;
            header.style.paddingRight  = 14;
            header.style.backgroundColor = (Color)new Color32(0x2A, 0x2A, 0x3D, 0xFF);

            var arrow = new Label(openByDefault ? "▼" : "▶");
            arrow.AddToClassList("collapsible__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            arrow.style.width    = 20;
            arrow.style.color    = (Color)new Color32(0x4A, 0x9E, 0xFF, 0xFF);
            arrow.style.fontSize = 11;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.flexShrink     = 0;

            var lbl = new Label(title);
            lbl.AddToClassList("collapsible__title");
            lbl.pickingMode = PickingMode.Ignore;
            lbl.style.flexGrow   = 1;
            lbl.style.fontSize   = 13;
            lbl.style.color      = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginLeft = 6;

            header.Add(arrow);
            header.Add(lbl);
            wrap.Add(header);

            // ── Body (содержимое, сворачиваемое) ──
            var body = new VisualElement();
            body.AddToClassList("collapsible__body");
            body.style.flexDirection   = FlexDirection.Column;
            body.style.paddingTop      = 4;
            body.style.paddingBottom   = 14;
            body.style.paddingLeft     = 14;
            body.style.paddingRight    = 14;
            body.style.display         = openByDefault ? DisplayStyle.Flex : DisplayStyle.None;
            if (openByDefault) body.AddToClassList("collapsible__body--open");

            foreach (var p in parameters)
                body.Add(BuildParamField(p));

            wrap.Add(body);

            // Инлайновое управление display — без CSS-зависимости
            header.RegisterCallback<ClickEvent>(_ =>
            {
                bool isOpen = body.style.display == DisplayStyle.Flex;
                body.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
                body.EnableInClassList("collapsible__body--open", !isOpen);
                arrow.text = !isOpen ? "▼" : "▶";
            });

            return wrap;
        }

        private VisualElement BuildParamField(ParameterSchemaDto p)
        {
            // ── Param-row card (bg-tertiary с padding) ──
            var row = new VisualElement();
            row.AddToClassList("param-row");
            row.style.flexDirection            = FlexDirection.Column;
            row.style.backgroundColor          = (Color)new Color32(0x36, 0x36, 0x50, 0xFF);
            row.style.borderTopLeftRadius      = 7;
            row.style.borderTopRightRadius     = 7;
            row.style.borderBottomLeftRadius   = 7;
            row.style.borderBottomRightRadius  = 7;
            row.style.paddingTop    = 10;
            row.style.paddingBottom = 10;
            row.style.paddingLeft   = 12;
            row.style.paddingRight  = 12;
            row.style.marginBottom  = 8;

            // ── Head: имя + reset кнопка ──
            var head = new VisualElement();
            head.AddToClassList("param-row__head");
            head.style.flexDirection  = FlexDirection.Row;
            head.style.alignItems     = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.marginBottom   = 6;

            var nameLbl = new Label(p.Name ?? p.Id);
            nameLbl.AddToClassList("param-row__name");
            nameLbl.style.flexGrow = 1;
            nameLbl.style.fontSize = 13;
            nameLbl.style.color    = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(nameLbl);

            var resetBtn = new Button { text = "↻" };
            resetBtn.AddToClassList("param-row__reset");
            resetBtn.style.width        = 26;
            resetBtn.style.height       = 26;
            resetBtn.style.flexShrink   = 0;
            resetBtn.style.flexGrow     = 0;
            resetBtn.style.alignSelf    = Align.Center;
            resetBtn.style.marginLeft   = 8;
            resetBtn.style.marginTop    = 0;
            resetBtn.style.marginRight  = 0;
            resetBtn.style.marginBottom = 0;
            resetBtn.style.paddingLeft  = 0;
            resetBtn.style.paddingRight = 0;
            resetBtn.style.backgroundColor = new Color(0, 0, 0, 0);
            resetBtn.style.borderTopWidth = 1;
            resetBtn.style.borderBottomWidth = 1;
            resetBtn.style.borderLeftWidth = 1;
            resetBtn.style.borderRightWidth = 1;
            Color resetBorder = (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            resetBtn.style.borderTopColor    = resetBorder;
            resetBtn.style.borderBottomColor = resetBorder;
            resetBtn.style.borderLeftColor   = resetBorder;
            resetBtn.style.borderRightColor  = resetBorder;
            resetBtn.style.borderTopLeftRadius = 5;
            resetBtn.style.borderTopRightRadius = 5;
            resetBtn.style.borderBottomLeftRadius = 5;
            resetBtn.style.borderBottomRightRadius = 5;
            resetBtn.style.color    = (Color)new Color32(0x9C, 0xA3, 0xAF, 0xFF);
            resetBtn.style.fontSize = 13;
            resetBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            head.Add(resetBtn);
            row.Add(head);

            if (!string.IsNullOrEmpty(p.Description))
            {
                var desc = new Label(p.Description);
                desc.AddToClassList("param-row__desc");
                desc.style.fontSize    = 11;
                desc.style.color       = (Color)new Color32(0x6B, 0x72, 0x80, 0xFF);
                desc.style.marginBottom = 6;
                desc.style.whiteSpace  = WhiteSpace.Normal;
                row.Add(desc);
            }

            var current = project.Parameters[p.Id] ?? p.Default ?? JValue.CreateNull();

            VisualElement control = null;
            switch ((p.Type ?? "").ToLowerInvariant())
            {
                case "int":
                {
                    var min = p.Min.HasValue ? (int)p.Min.Value : 0;
                    var max = p.Max.HasValue ? (int)p.Max.Value : 100;
                    var val = current.Type == JTokenType.Integer ? (int)current
                              : current.Type == JTokenType.Float  ? (int)(float)current
                              : min;
                    var (sr, slider) = MakeSliderInt(min, max, val);
                    slider.RegisterValueChangedCallback(ev => SetValue(p.Id, ev.newValue));
                    resetBtn.clicked += () => { slider.value = p.Default?.Type == JTokenType.Integer
                        ? (int)p.Default : min; };
                    control = sr;
                    break;
                }
                case "float":
                case "number":
                {
                    var min = p.Min.HasValue ? (float)p.Min.Value : 0f;
                    var max = p.Max.HasValue ? (float)p.Max.Value : 1f;
                    var val = current.Type == JTokenType.Float || current.Type == JTokenType.Integer
                              ? (float)current : min;
                    var (sr, slider) = MakeSliderFloat(min, max, val);
                    slider.RegisterValueChangedCallback(ev => SetValue(p.Id, ev.newValue));
                    resetBtn.clicked += () => { slider.value = p.Default != null
                        ? (float)p.Default : min; };
                    control = sr;
                    break;
                }
                case "bool":
                case "boolean":
                {
                    var val  = current.Type == JTokenType.Boolean && (bool)current;
                    var wrap = new VisualElement();
                    wrap.style.flexDirection  = FlexDirection.Row;
                    wrap.style.alignItems     = Align.Center;
                    wrap.style.justifyContent = Justify.SpaceBetween;

                    var tg = new Toggle { value = val };
                    tg.AddToClassList("param-toggle");
                    tg.RegisterValueChangedCallback(ev => SetValue(p.Id, ev.newValue));
                    resetBtn.clicked += () => { tg.value = p.Default?.Type == JTokenType.Boolean
                        && (bool)p.Default; };
                    wrap.Add(tg);
                    control = wrap;
                    break;
                }
                case "enum":
                case "choice":
                {
                    var choices = p.Choices?.ToList() ?? new List<string>();
                    var val     = current.Type == JTokenType.String
                                  ? (string)current : (choices.FirstOrDefault() ?? "");
                    var dd = new DropdownField(choices, val);
                    dd.AddToClassList("param-dropdown");
                    dd.style.height    = 34;
                    dd.style.marginTop = 4;
                    dd.RegisterValueChangedCallback(ev => SetValue(p.Id, ev.newValue));
                    resetBtn.clicked += () => { dd.value = p.Default?.Type == JTokenType.String
                        ? (string)p.Default : (choices.FirstOrDefault() ?? ""); };
                    control = dd;
                    break;
                }
                case "color":
                {
                    var val = current.Type == JTokenType.String ? (string)current : "#FFFFFF";
                    var (cr, tf, swatch) = MakeColorRow(val);
                    tf.RegisterValueChangedCallback(ev =>
                    {
                        SetValue(p.Id, ev.newValue);
                        ApplyColorToSwatch(swatch, ev.newValue);
                    });
                    resetBtn.clicked += () =>
                    {
                        var def = p.Default?.Type == JTokenType.String ? (string)p.Default : "#FFFFFF";
                        tf.value = def;
                        ApplyColorToSwatch(swatch, def);
                    };
                    control = cr;
                    break;
                }
                default:
                {
                    var val = current.Type == JTokenType.String ? (string)current : "";
                    var tf  = new TextField { value = val, multiline = p.Type == "text_area" };
                    tf.AddToClassList("param-textfield");
                    tf.style.height    = 34;
                    tf.style.marginTop = 4;
                    ApplyTextFieldStyles(tf);
                    tf.RegisterValueChangedCallback(ev => SetValue(p.Id, ev.newValue));
                    resetBtn.clicked += () => { tf.value = p.Default?.Type == JTokenType.String
                        ? (string)p.Default : ""; };
                    control = tf;
                    break;
                }
            }

            if (control != null)
                row.Add(control);

            return row;
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static (VisualElement row, SliderInt slider) MakeSliderInt(int min, int max, int val)
        {
            var valLbl = MakeSliderValueLabel(val.ToString());

            var slider = new SliderInt(min, max) { value = val };
            slider.AddToClassList("slider");
            slider.style.flexGrow    = 1;
            slider.style.marginTop    = 4;
            slider.style.marginBottom = 4;
            slider.RegisterValueChangedCallback(ev => valLbl.text = ev.newValue.ToString());

            var row = MakeRowInline("slider-row");
            row.Add(slider);
            row.Add(valLbl);
            return (row, slider);
        }

        private static (VisualElement row, Slider slider) MakeSliderFloat(float min, float max, float val)
        {
            var fmt    = val >= 10 ? "0.#" : "0.##";
            var valLbl = MakeSliderValueLabel(val.ToString(fmt));

            var slider = new Slider(min, max) { value = val };
            slider.AddToClassList("slider");
            slider.style.flexGrow     = 1;
            slider.style.marginTop    = 4;
            slider.style.marginBottom = 4;
            slider.RegisterValueChangedCallback(ev =>
            {
                var f       = ev.newValue >= 10 ? "0.#" : "0.##";
                valLbl.text = ev.newValue.ToString(f);
            });

            var row = MakeRowInline("slider-row");
            row.Add(slider);
            row.Add(valLbl);
            return (row, slider);
        }

        // Маленький бокс с текущим значением справа от слайдера.
        private static Label MakeSliderValueLabel(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("slider-value");
            lbl.style.minWidth      = 42;
            lbl.style.backgroundColor = (Color)new Color32(0x1E, 0x1E, 0x2E, 0xFF);
            lbl.style.borderTopLeftRadius     = 4;
            lbl.style.borderTopRightRadius    = 4;
            lbl.style.borderBottomLeftRadius  = 4;
            lbl.style.borderBottomRightRadius = 4;
            lbl.style.borderTopWidth    = 1;
            lbl.style.borderBottomWidth = 1;
            lbl.style.borderLeftWidth   = 1;
            lbl.style.borderRightWidth  = 1;
            Color slBorder = (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            lbl.style.borderTopColor    = slBorder;
            lbl.style.borderBottomColor = slBorder;
            lbl.style.borderLeftColor   = slBorder;
            lbl.style.borderRightColor  = slBorder;
            lbl.style.color = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
            lbl.style.fontSize = 12;
            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            lbl.style.marginLeft = 10;
            lbl.style.paddingTop = 3;
            lbl.style.paddingBottom = 3;
            lbl.style.paddingLeft = 6;
            lbl.style.paddingRight = 6;
            lbl.style.flexShrink = 0;
            return lbl;
        }

        private static (VisualElement row, TextField tf, VisualElement swatch) MakeColorRow(string hexVal)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("color-swatch");
            swatch.style.width       = 32;
            swatch.style.height      = 32;
            swatch.style.flexShrink  = 0;
            swatch.style.marginRight = 10;
            swatch.style.borderTopLeftRadius     = 6;
            swatch.style.borderTopRightRadius    = 6;
            swatch.style.borderBottomLeftRadius  = 6;
            swatch.style.borderBottomRightRadius = 6;
            swatch.style.borderTopWidth    = 1;
            swatch.style.borderBottomWidth = 1;
            swatch.style.borderLeftWidth   = 1;
            swatch.style.borderRightWidth  = 1;
            Color swBorder = (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            swatch.style.borderTopColor    = swBorder;
            swatch.style.borderBottomColor = swBorder;
            swatch.style.borderLeftColor   = swBorder;
            swatch.style.borderRightColor  = swBorder;
            ApplyColorToSwatch(swatch, hexVal);

            var tf = new TextField { value = hexVal };
            tf.AddToClassList("param-textfield");
            tf.AddToClassList("color-hex-input");
            tf.style.flexGrow = 1;
            tf.style.height   = 34;
            tf.style.marginTop = 0;
            ApplyTextFieldStyles(tf);

            var row = MakeRowInline("color-picker-row");
            row.style.marginTop = 2;
            row.Add(swatch);
            row.Add(tf);
            return (row, tf, swatch);
        }

        // Общая стилизация TextField: рамка, фон, скругление.
        private static void ApplyTextFieldStyles(TextField tf)
        {
            tf.style.backgroundColor = (Color)new Color32(0x1E, 0x1E, 0x2E, 0xFF);
            tf.style.borderTopLeftRadius     = 6;
            tf.style.borderTopRightRadius    = 6;
            tf.style.borderBottomLeftRadius  = 6;
            tf.style.borderBottomRightRadius = 6;
            tf.style.borderTopWidth    = 1;
            tf.style.borderBottomWidth = 1;
            tf.style.borderLeftWidth   = 1;
            tf.style.borderRightWidth  = 1;
            Color tfBorder = (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
            tf.style.borderTopColor    = tfBorder;
            tf.style.borderBottomColor = tfBorder;
            tf.style.borderLeftColor   = tfBorder;
            tf.style.borderRightColor  = tfBorder;
            tf.style.color    = (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);
            tf.style.fontSize = 13;
        }

        private static VisualElement MakeRowInline(string cls)
        {
            var row = new VisualElement();
            row.AddToClassList(cls);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            return row;
        }

        private static void ApplyColorToSwatch(VisualElement swatch, string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c))
                swatch.style.backgroundColor = c;
        }

        // ── Save value ────────────────────────────────────────────────────────

        private void SetValue(string id, object value)
        {
            project.Parameters[id] = JToken.FromObject(value);
            project.MarkDirty();
        }

        // ── Empty / error state ───────────────────────────────────────────────

        private void ShowMessage(string text)
        {
            if (container == null) return;
            container.Clear();
            var lbl = new Label(text);
            lbl.AddToClassList("step-subtitle");
            lbl.style.paddingTop  = 24;
            lbl.style.paddingLeft = 8;
            lbl.style.whiteSpace  = WhiteSpace.Normal;
            container.Add(lbl);
        }
    }
}
