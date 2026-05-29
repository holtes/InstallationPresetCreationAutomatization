using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Screens.Preview;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 5 — лобби превью. Здесь только выбираем разрешение и жмём
    /// «Посмотреть превью» — реальный запуск пресета происходит на отдельном
    /// полноэкранном экране (PreviewScreen), потому что не все пресеты
    /// (3D, видео и т.п.) можно отрисовать в маленькой панели.
    /// </summary>
    public class Step5PreviewController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step5_Preview";

        private ProjectModel project;
        private DropdownField resolutionDd;
        private Label presetNameLbl;

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.project = project;

            resolutionDd  = root.Q<DropdownField>("preview-resolution");
            presetNameLbl = root.Q<Label>("preview-preset-name");
            var launchBtn = root.Q<Button>("preview-launch-btn");
            if (launchBtn != null) launchBtn.clicked += LaunchFullscreenPreview;
        }

        public Task EnterAsync()
        {
            if (presetNameLbl != null)
            {
                if (string.IsNullOrEmpty(project.PresetId))
                    presetNameLbl.text = "Пресет: не выбран";
                else
                {
                    var info = LocalPresetSchemas.Get(project.PresetId);
                    presetNameLbl.text = info != null
                        ? $"Пресет: {info.Name}"
                        : $"Пресет: {project.PresetId}";
                }
            }
            return Task.CompletedTask;
        }

        public Task LeaveAsync() => Task.CompletedTask;

        public string Validate() => null;

        public void Dispose() { }

        private void LaunchFullscreenPreview()
        {
            if (string.IsNullOrEmpty(project.PresetId))
            {
                Toast.Warning("Сначала выберите пресет на шаге 2.");
                return;
            }

            var args = new PreviewScreenArgs
            {
                Project    = project,
                Resolution = resolutionDd?.value ?? "1280×720"
            };

            AppManager.Instance?.Router?.Navigate(ScreenId.Preview, args);
        }
    }
}
