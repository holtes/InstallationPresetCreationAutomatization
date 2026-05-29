using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Projects;
using InteractiveClient.UI.Screens.Editor.Steps;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor
{
    /// <summary>
    /// Шелл редактора: topbar, stepper, контейнер шага, нижняя навигация,
    /// автосохранение по таймеру (AppManager.AutosaveIntervalSeconds).
    /// Шаги — IEditorStep, лениво инициализируются при первом открытии.
    /// </summary>
    public class EditorScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Editor/EditorScreen";
        public override ScreenId Id => ScreenId.Editor;

        // Шапка
        private Button breadcrumbProjects;
        private Label breadcrumbCurrent;
        private Label saveStatusText;
        private Button saveBtn;
        private Button previewBtn;
        private Button buildBtn;

        // Stepper
        private readonly VisualElement[] stepNodes = new VisualElement[6];
        private readonly Label[] stepCircles = new Label[6];
        private readonly Label[] stepLabels = new Label[6];

        // Content
        private VisualElement stepContent;
        private Button prevBtn;
        private Button nextBtn;

        // Steps
        private readonly Dictionary<EditorStep, IEditorStep> steps = new();
        private readonly Dictionary<EditorStep, VisualElement> stepRoots = new();

        private ProjectModel project;
        private EditorStep currentStep = EditorStep.Assets;
        private EditorStep maxReachedStep = EditorStep.Assets;

        private IVisualElementScheduledItem autosaveTick;
        private CancellationTokenSource saveCts;
        private bool saving;

        protected override void OnInitialize()
        {
            breadcrumbProjects = Root.Q<Button>("breadcrumb-projects");
            breadcrumbCurrent  = Root.Q<Label>("breadcrumb-current");
            saveStatusText     = Root.Q<Label>("save-status-text");
            saveBtn            = Root.Q<Button>("save-btn");
            previewBtn         = Root.Q<Button>("preview-btn");
            buildBtn           = Root.Q<Button>("build-btn");
            stepContent        = Root.Q<VisualElement>("step-content");
            prevBtn            = Root.Q<Button>("prev-step-btn");
            nextBtn            = Root.Q<Button>("next-step-btn");

            for (int i = 0; i < 6; i++)
            {
                stepNodes[i]   = Root.Q<VisualElement>($"step-{i + 1}");
                stepCircles[i] = Root.Q<Label>($"step-{i + 1}-circle");
                stepLabels[i]  = stepNodes[i]?.Q<Label>(className: "stepper__label");
                var idx = i;
                if (stepNodes[i] != null)
                    stepNodes[i].RegisterCallback<ClickEvent>(_ => GoToStep((EditorStep)(idx + 1)));
            }

            if (breadcrumbProjects != null) breadcrumbProjects.clicked += OnBackToProjects;
            if (saveBtn != null) saveBtn.clicked += () => _ = SaveAsync(showToast: true);
            if (previewBtn != null) previewBtn.clicked += () => GoToStep(EditorStep.Preview);
            if (buildBtn != null) buildBtn.clicked += () => GoToStep(EditorStep.Build);
            if (prevBtn != null) prevBtn.clicked += OnPrev;
            if (nextBtn != null) nextBtn.clicked += OnNext;

            // Создаём (но не инициализируем UI) инстансы шагов
            steps[EditorStep.Assets]     = new Step1AssetsController();
            steps[EditorStep.Preset]     = new Step2PresetsController();
            steps[EditorStep.Mapping]    = new Step3MappingController();
            steps[EditorStep.Parameters] = new Step4ParametersController();
            steps[EditorStep.Preview]    = new Step5PreviewController();
            steps[EditorStep.Build]      = new Step6BuildController();
        }

        protected override void OnShow(object data)
        {
            // Возврат с другого экрана (например, PreviewScreen) — данных нет,
            // но проект уже открыт. Просто возобновляем работу на текущем шаге.
            if (data == null && project != null)
            {
                if (breadcrumbCurrent != null) breadcrumbCurrent.text = project.Name ?? "Проект";
                UpdateSaveStatus();
                _ = EnterStepAsync(currentStep);

                int intervalSecResume = AppManager.Instance?.AutosaveIntervalSeconds ?? 60;
                autosaveTick?.Pause();
                autosaveTick = Root.schedule.Execute(() => _ = SaveAsync(showToast: false))
                    .Every(intervalSecResume * 1000);
                return;
            }

            project = data as ProjectModel;
            if (project == null)
            {
                Debug.LogError("[EditorScreen] No project passed.");
                Toast.Error("Не удалось открыть проект.");
                AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList);
                return;
            }

            if (breadcrumbCurrent != null) breadcrumbCurrent.text = project.Name ?? "Проект";
            UpdateSaveStatus();

            // Стартовый шаг — всегда первый
            currentStep = EditorStep.Assets;
            maxReachedStep = EditorStep.Assets;
            _ = EnterStepAsync(currentStep);

            // Запускаем автосохранение
            int intervalSec = AppManager.Instance?.AutosaveIntervalSeconds ?? 60;
            autosaveTick?.Pause();
            autosaveTick = Root.schedule.Execute(() => _ = SaveAsync(showToast: false))
                .Every(intervalSec * 1000);
        }

        protected override async void OnHide()
        {
            autosaveTick?.Pause();
            autosaveTick = null;

            if (steps.TryGetValue(currentStep, out var cur))
            {
                try { await cur.LeaveAsync(); } catch (Exception e) { Debug.LogException(e); }
            }

            // Финальное сохранение
            if (project != null && project.IsDirty)
            {
                try { await SaveAsync(showToast: false); } catch { /* ignore */ }
            }
        }

        protected override void OnDispose()
        {
            foreach (var s in steps.Values) s.Dispose();
            steps.Clear();
            stepRoots.Clear();
        }

        // ---------- Навигация между шагами ----------

        private async void OnPrev()
        {
            var idx = (int)currentStep;
            if (idx > 1) await SwitchStep((EditorStep)(idx - 1), validate: false);
        }

        private async void OnNext()
        {
            // Validate текущий
            if (steps.TryGetValue(currentStep, out var cur))
            {
                var err = cur.Validate();
                if (!string.IsNullOrEmpty(err))
                {
                    Toast.Warning(err);
                    return;
                }
            }
            var idx = (int)currentStep;
            if (idx < 6)
            {
                var next = (EditorStep)(idx + 1);
                if (next > maxReachedStep) maxReachedStep = next;
                await SwitchStep(next, validate: true);
            }
        }

        private async void GoToStep(EditorStep target)
        {
            if (target == currentStep) return;
            if (target > maxReachedStep) return;
            await SwitchStep(target, validate: false);
        }

        private async Task SwitchStep(EditorStep target, bool validate)
        {
            if (steps.TryGetValue(currentStep, out var cur))
            {
                try { await cur.LeaveAsync(); } catch (Exception e) { Debug.LogException(e); }
                if (stepRoots.TryGetValue(currentStep, out var curRoot))
                    curRoot.style.display = DisplayStyle.None;
            }

            var fromStep = (int)currentStep;
            currentStep = target;
            UpdateStepperVisuals();
            EventBus.Publish(new StepChangedEvent(fromStep, (int)target));
            await EnterStepAsync(target);
        }

        private async Task EnterStepAsync(EditorStep step)
        {
            if (!steps.TryGetValue(step, out var ctrl)) return;

            if (!stepRoots.TryGetValue(step, out var root))
            {
                root = new VisualElement { name = $"step-{(int)step}-root" };
                root.style.flexGrow = 1;
                stepContent.Add(root);
                stepRoots[step] = root;

                var asset = Resources.Load<VisualTreeAsset>(ctrl.UxmlResourcePath);
#if UNITY_EDITOR
                if (asset == null)
                    asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"Assets/{ctrl.UxmlResourcePath}.uxml");
#endif
                if (asset != null) asset.CloneTree(root);
                else Debug.LogError($"[EditorScreen] UXML not found: {ctrl.UxmlResourcePath}");

                try { ctrl.Initialize(root, project); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            root.style.display = DisplayStyle.Flex;
            UpdateStepperVisuals();

            try { await ctrl.EnterAsync(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void UpdateStepperVisuals()
        {
            for (int i = 0; i < 6; i++)
            {
                var idx = i + 1;

                var circle = stepCircles[i];
                if (circle != null)
                {
                    circle.RemoveFromClassList("stepper__circle--active");
                    circle.RemoveFromClassList("stepper__circle--done");

                    if (idx < (int)currentStep) circle.AddToClassList("stepper__circle--done");
                    else if (idx == (int)currentStep) circle.AddToClassList("stepper__circle--active");
                }

                var label = stepLabels[i];
                if (label != null)
                {
                    label.RemoveFromClassList("stepper__label--active");
                    label.RemoveFromClassList("stepper__label--completed");

                    if (idx == (int)currentStep) label.AddToClassList("stepper__label--active");
                    else if (idx < (int)currentStep) label.AddToClassList("stepper__label--completed");
                }
            }

            if (prevBtn != null) prevBtn.SetEnabled((int)currentStep > 1);
            if (nextBtn != null)
            {
                nextBtn.SetEnabled((int)currentStep < 6);
                nextBtn.text = (int)currentStep == 6 ? "Готово" : "Далее →";
            }
        }

        // ---------- Автосохранение ----------

        private async Task SaveAsync(bool showToast)
        {
            if (saving || project == null || !project.IsDirty) return;
            saving = true;
            SetSaveStatus("Сохранение…");

            saveCts?.Cancel();
            saveCts = new CancellationTokenSource();

            try
            {
                var service = ServiceLocator.Get<ProjectService>();
                var updated = await service.UpdateAsync(project, saveCts.Token);
                if (updated != null)
                {
                    project.MarkSaved();
                    EventBus.Publish(new ProjectSavedEvent(project.Id, true));
                }
                UpdateSaveStatus();
                if (showToast) Toast.Success("Проект сохранён.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetSaveStatus("Ошибка сохранения");
                if (showToast) Toast.Error("Не удалось сохранить проект.");
            }
            finally
            {
                saving = false;
            }
        }

        private void UpdateSaveStatus()
        {
            if (project == null) { SetSaveStatus(""); return; }
            SetSaveStatus(project.IsDirty ? "Есть несохранённые изменения" : "Сохранено");
        }

        private void SetSaveStatus(string text)
        {
            if (saveStatusText != null) saveStatusText.text = text;
        }

        // ---------- Назад к списку ----------

        private void OnBackToProjects()
        {
            if (project != null && project.IsDirty)
            {
                // Глобальный ModalService (#modal-host); локальный new ModalService(Root)
                // при Close() очищает весь Root экрана.
                ServiceLocator.Get<ModalService>().Confirm(
                    "Несохранённые изменения",
                    "Выйти без сохранения?",
                    "Выйти", "Остаться",
                    onConfirm: () => AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList));
                return;
            }
            AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList);
        }
    }
}
