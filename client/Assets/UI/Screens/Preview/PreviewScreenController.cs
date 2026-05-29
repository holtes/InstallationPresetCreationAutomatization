using InteractiveClient.AssetsModule;
using InteractiveClient.Core;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Screens.Editor.Steps;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Preview
{
    /// <summary>
    /// Аргументы перехода на полноэкранный превью пресета.
    /// </summary>
    public class PreviewScreenArgs
    {
        public ProjectModel Project;
        /// <summary>Строка вида "1920×1080" из дропдауна Step5.</summary>
        public string Resolution;
    }

    /// <summary>
    /// Полноэкранный экран live-превью пресета.
    /// • Top-bar: «Выйти», заголовок, FPS/память, «Пауза».
    /// • Stage в центре с фиксированным разрешением (выбранным на Step5).
    /// • Реальный запуск пресета через PreviewSession (PresetHost).
    /// </summary>
    public class PreviewScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Preview/PreviewScreen";
        public override ScreenId Id => ScreenId.Preview;

        private VisualElement stage;
        private Label titleLbl, subtitleLbl, fpsLbl, memoryLbl, resolutionInfoLbl;
        private Button pauseBtn, restartBtn, exitBtn;

        private readonly PreviewSession session = new();
        private IVisualElementScheduledItem metricsTick;
        private bool isPaused;

        private ProjectModel currentProject;
        private string currentResolution = "1280×720";

        protected override void OnInitialize()
        {
            stage             = Root.Q<VisualElement>("preview-stage");
            titleLbl          = Root.Q<Label>("preview-title");
            subtitleLbl       = Root.Q<Label>("preview-subtitle");
            fpsLbl            = Root.Q<Label>("preview-fps");
            memoryLbl         = Root.Q<Label>("preview-memory");
            resolutionInfoLbl = Root.Q<Label>("preview-resolution-info");
            pauseBtn          = Root.Q<Button>("preview-pause-btn");
            restartBtn        = Root.Q<Button>("preview-restart-btn");
            exitBtn           = Root.Q<Button>("preview-exit-btn");

            if (pauseBtn   != null) pauseBtn.clicked   += TogglePause;
            if (restartBtn != null) restartBtn.clicked += RestartPreview;
            if (exitBtn    != null) exitBtn.clicked    += ExitPreview;

            // Кнопка «Выйти» на экране результата (показывается когда игрок выиграл/проиграл)
            // делает то же что и кнопка «Выйти из превью» в top-bar.
            session.OnExitRequested += ExitPreview;
        }

        protected override void OnShow(object data)
        {
            // Аргументы навигации
            if (data is PreviewScreenArgs args)
            {
                currentProject    = args.Project;
                currentResolution = string.IsNullOrEmpty(args.Resolution) ? "1280×720" : args.Resolution;
            }

            // Подписи
            if (titleLbl != null) titleLbl.text = "Превью механики";
            if (subtitleLbl != null)
            {
                if (currentProject == null || string.IsNullOrEmpty(currentProject.PresetId))
                    subtitleLbl.text = "Пресет не выбран";
                else
                {
                    var info = LocalPresetSchemas.Get(currentProject.PresetId);
                    subtitleLbl.text = info != null
                        ? $"{info.Name}  •  {currentResolution}"
                        : $"{currentProject.PresetId}  •  {currentResolution}";
                }
            }

            if (resolutionInfoLbl != null) resolutionInfoLbl.text = currentResolution;

            // Применяем выбранное разрешение к stage (с масштабированием по доступному месту)
            ApplyStageResolution();

            // Сбрасываем состояние паузы
            isPaused = false;
            UpdatePauseButton();

            // Запускаем пресет
            LaunchPreview();

            // Метрики FPS / памяти каждые 500мс. Сразу вызываем один раз, чтобы
            // не висело «— FPS» / «— MB» полсекунды до первого тика.
            metricsTick?.Pause();
            UpdateMetrics();
            metricsTick = Root.schedule.Execute(UpdateMetrics).Every(500);
        }

        protected override void OnHide()
        {
            metricsTick?.Pause();
            metricsTick = null;
            session.Cleanup();
            isPaused = false;
        }

        protected override void OnDispose() => session.Dispose();

        // ── Layout ────────────────────────────────────────────────────────────

        private void ApplyStageResolution()
        {
            if (stage == null) return;

            // Парсим "WxH" / "W×H"
            int w = 1280, h = 720;
            if (!string.IsNullOrEmpty(currentResolution))
            {
                var s = currentResolution.Replace('×', 'x').Replace(" ", "");
                var parts = s.Split('x');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var pw) &&
                    int.TryParse(parts[1], out var ph))
                {
                    w = pw; h = ph;
                }
            }

            // Оставляем место под top-bar + padding (примерно 56+40)
            // и не выходим за пределы окна. UI Toolkit сам сожмёт через max-*.
            stage.style.width  = w;
            stage.style.height = h;
            stage.style.maxWidth  = new StyleLength(new Length(96, LengthUnit.Percent));
            stage.style.maxHeight = new StyleLength(new Length(92, LengthUnit.Percent));
        }

        // ── Preview lifecycle ─────────────────────────────────────────────────

        private void LaunchPreview()
        {
            if (stage == null) return;
            var library = ServiceLocator.Get<AssetLibrary>();
            session.Launch(stage, currentProject, library);
        }

        private void TogglePause()
        {
            isPaused = !isPaused;
            UpdatePauseButton();
            if (isPaused) session.ShowPaused();
            else          session.Resume();
        }

        private void RestartPreview()
        {
            // Полная пересборка: убиваем GameObject пресета и поднимаем заново
            // — параметры и состояние сбрасываются.
            isPaused = false;
            UpdatePauseButton();
            LaunchPreview();
        }

        private void UpdatePauseButton()
        {
            if (pauseBtn == null) return;
            pauseBtn.text = isPaused ? "▶  Продолжить" : "⏸  Пауза";
        }

        private void ExitPreview()
        {
            // OnHide() сам почистит session/metrics
            AppManager.Instance?.Router?.Navigate(ScreenId.Editor);
        }

        // ── Metrics ───────────────────────────────────────────────────────────

        private void UpdateMetrics()
        {
            var dt = Time.unscaledDeltaTime;
            int f  = dt > 0 ? Mathf.RoundToInt(1f / dt) : 0;
            if (fpsLbl != null) fpsLbl.text = $"{f} FPS";

            if (memoryLbl != null)
            {
                long mb = -1;
                try { mb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024); }
                catch { /* ignore */ }
                memoryLbl.text = mb >= 0 ? $"{mb} MB" : "— MB";
            }
        }
    }
}
