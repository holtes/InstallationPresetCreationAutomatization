using System;
using System.Collections.Generic;
using InteractiveClient.AssetsModule;
using InteractiveClient.Game;
using InteractiveClient.Projects;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Универсальный launcher live-превью пресета. Используется и в Step4 (панель «Живой превью»),
    /// и в Step5 (полноэкранное превью). Берёт ProjectModel + AssetLibrary, собирает PresetContext,
    /// запускает PresetHost внутри указанного VisualElement.
    ///
    /// Если пресет не выбран / не реализован — показывает стилизованную «пустую» подсказку.
    /// </summary>
    public sealed class PreviewSession : IDisposable
    {
        private VisualElement host;
        private PresetHost presetHost;

        /// <summary>
        /// Срабатывает когда пользователь жмёт «Выйти» на экране результата.
        /// PreviewScreen подписывается на это событие чтобы вернуться в редактор.
        /// В реальном WebGL-билде потребитель может подписать сюда, например,
        /// «закрыть страницу» или редирект на referrer-URL.
        /// </summary>
        public event Action OnExitRequested;

        public bool IsRunning => presetHost != null && presetHost.ActivePreset != null;

        /// <summary>
        /// Перезапускает превью внутри <paramref name="root"/>.
        /// Безопасно вызывать многократно: предыдущий запуск всегда чистится.
        /// </summary>
        public void Launch(VisualElement root, ProjectModel project, AssetLibrary library)
        {
            Cleanup();
            if (root == null) return;
            host = root;

            // Готовим контейнер: убираем старое содержимое и обеспечиваем corret геометрию
            root.Clear();
            root.style.overflow      = Overflow.Hidden;
            root.style.flexGrow      = 1;
            root.style.flexDirection = FlexDirection.Column;

            if (project == null || string.IsNullOrEmpty(project.PresetId))
            {
                ShowPlaceholder(root, "🎮", "Превью недоступно",
                    "Выберите пресет на шаге 2 — и превью появится здесь.");
                return;
            }

            // Собираем slot_id → AssetModel из маппинга проекта
            var assets = new Dictionary<string, AssetModel>();
            if (library != null)
            {
                foreach (var kv in project.AssetMapping)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    var a = library.Get(kv.Value);
                    if (a != null) assets[kv.Key] = a;
                }
            }

            // PresetHost сам создаст preset-stage / hud-host / result-host внутри root
            presetHost = new PresetHost(root);
            presetHost.ReplayRequested += () =>
            {
                presetHost.HideResult();
                Launch(root, project, library);
            };
            presetHost.ExitRequested += () => OnExitRequested?.Invoke();

            var ctx = new PresetContext(assets, project.Parameters);
            bool ok = presetHost.LaunchByPresetId(project.PresetId, ctx);

            if (!ok)
            {
                Cleanup();
                ShowPlaceholder(root, "🛠",
                    $"Превью «{project.PresetId}» пока не реализовано",
                    "Этот пресет появится в следующей версии.");
            }
        }

        /// <summary>
        /// Ставит превью на паузу. ВАЖНО: НЕ уничтожаем GameObject пресета
        /// и не удаляем piece-VEs — UI Toolkit держит draw-call'ы со ссылками
        /// на текстуры ещё пару кадров после удаления, и панель валится в
        /// RepaintPanels с Assertion failed. Поэтому просто прячем preset-stage
        /// и hud-host через display:none и кладём поверх затемнённый оверлей.
        /// </summary>
        public void ShowPaused()
        {
            if (host == null) return;

            var presetStage = host.Q<VisualElement>("preset-stage");
            var hudHost     = host.Q<VisualElement>("hud-host");
            if (presetStage != null) presetStage.style.display = DisplayStyle.None;
            if (hudHost     != null) hudHost.style.display     = DisplayStyle.None;

            // Не плодим оверлеи если уже есть
            if (host.Q<VisualElement>("preview-paused-overlay") != null) return;

            var overlay = BuildPlaceholder("⏸", "Превью на паузе",
                "Нажмите «Продолжить» чтобы возобновить.");
            overlay.name = "preview-paused-overlay";
            overlay.style.position = Position.Absolute;
            overlay.style.left   = 0; overlay.style.top    = 0;
            overlay.style.right  = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = new UnityEngine.Color(0.05f, 0.05f, 0.08f, 0.85f);
            host.Add(overlay);
        }

        /// <summary>Снимает паузу: убирает оверлей, возвращает preset-stage.</summary>
        public void Resume()
        {
            if (host == null) return;

            var overlay = host.Q<VisualElement>("preview-paused-overlay");
            if (overlay != null) host.Remove(overlay);

            var presetStage = host.Q<VisualElement>("preset-stage");
            var hudHost     = host.Q<VisualElement>("hud-host");
            if (presetStage != null) presetStage.style.display = DisplayStyle.Flex;
            if (hudHost     != null) hudHost.style.display     = DisplayStyle.Flex;
        }

        public void Cleanup()
        {
            presetHost?.Dispose();
            presetHost = null;

            // Полностью вычищаем host: PresetHost создаёт preset-stage / hud-host /
            // result-host как absolute-positioned дети — их Clear() пресета не
            // удаляет, и они могут оставаться видимыми после смены шага.
            if (host != null)
            {
                host.Clear();
                host.style.overflow = StyleKeyword.Null;
            }
            host = null;
        }

        public void Dispose() => Cleanup();

        // ── Stylized placeholder ─────────────────────────────────────────────

        private static void ShowPlaceholder(
            VisualElement root, string icon, string title, string sub)
        {
            root.Clear();
            root.Add(BuildPlaceholder(icon, title, sub));
        }

        private static VisualElement BuildPlaceholder(string icon, string title, string sub)
        {
            var pane = new VisualElement { name = "preview-empty" };
            pane.style.flexGrow       = 1;
            pane.style.flexDirection  = FlexDirection.Column;
            pane.style.alignItems     = Align.Center;
            pane.style.justifyContent = Justify.Center;

            var iconLbl = new Label(icon);
            iconLbl.AddToClassList("preview-pane__icon");
            pane.Add(iconLbl);

            var titleLbl = new Label(title);
            titleLbl.AddToClassList("preview-pane__text");
            pane.Add(titleLbl);

            if (!string.IsNullOrEmpty(sub))
            {
                var subLbl = new Label(sub);
                subLbl.AddToClassList("preview-pane__sub");
                pane.Add(subLbl);
            }
            return pane;
        }
    }
}
