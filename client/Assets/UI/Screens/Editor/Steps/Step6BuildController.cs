using System;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.AssetsModule;
using InteractiveClient.Build;
using InteractiveClient.Core;
using InteractiveClient.Network;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor.Steps
{
    /// <summary>
    /// Шаг 6 — сборка и публикация. Четыре состояния: Pre / Progress / Success / Error.
    /// Использует BuildService (POST config + POST build) и BuildStatusTracker (poll).
    /// </summary>
    public class Step6BuildController : IEditorStep
    {
        public string UxmlResourcePath => "UI/Screens/Editor/Steps/Step6_Build";

        private ProjectModel project;
        private VisualElement root;

        // Panels
        private VisualElement pre, progress, success, error;

        // Pre
        private Label presetName, assetCount, hwProfile;
        private Button typeLight, typeStandard, typeFull, startBtn;
        private BuildType selectedType = BuildType.Standard;

        // Progress
        private Label stageLbl, percentLbl, buildLog;
        private VisualElement progressFill;
        private Button cancelBtn;

        // Success
        private TextField urlField, iframeField;
        private Button copyUrlBtn, copyIframeBtn, openBrowserBtn, downloadBtn, newProjectBtn;
        private Label reportSize, reportTime, reportFps;

        // Error
        private Label errorMessage, errorLog;
        private Button retryBtn;

        private CancellationTokenSource cts;

        public void Initialize(VisualElement root, ProjectModel project)
        {
            this.root = root;
            this.project = project;

            pre      = root.Q<VisualElement>("build-pre");
            progress = root.Q<VisualElement>("build-progress");
            success  = root.Q<VisualElement>("build-success");
            error    = root.Q<VisualElement>("build-error");

            presetName  = root.Q<Label>("build-preset-name");
            assetCount  = root.Q<Label>("build-asset-count");
            hwProfile   = root.Q<Label>("build-hw-profile");

            typeLight    = root.Q<Button>("build-type-light");
            typeStandard = root.Q<Button>("build-type-standard");
            typeFull     = root.Q<Button>("build-type-full");
            startBtn     = root.Q<Button>("start-build-btn");

            if (typeLight != null)    typeLight.clicked    += () => SelectType(BuildType.Light);
            if (typeStandard != null) typeStandard.clicked += () => SelectType(BuildType.Standard);
            if (typeFull != null)     typeFull.clicked     += () => SelectType(BuildType.Full);
            if (startBtn != null)     startBtn.clicked     += () => _ = StartBuildAsync();

            stageLbl     = root.Q<Label>("build-stage");
            percentLbl   = root.Q<Label>("build-percent");
            buildLog     = root.Q<Label>("build-log");
            progressFill = root.Q<VisualElement>("build-progress-fill");
            cancelBtn    = root.Q<Button>("cancel-build-btn");
            if (cancelBtn != null) cancelBtn.clicked += CancelBuild;

            urlField      = root.Q<TextField>("build-url");
            iframeField   = root.Q<TextField>("build-iframe");
            copyUrlBtn    = root.Q<Button>("copy-url-btn");
            copyIframeBtn = root.Q<Button>("copy-iframe-btn");
            openBrowserBtn= root.Q<Button>("open-browser-btn");
            downloadBtn   = root.Q<Button>("download-btn");
            newProjectBtn = root.Q<Button>("new-project-btn");
            reportSize    = root.Q<Label>("report-size");
            reportTime    = root.Q<Label>("report-time");
            reportFps     = root.Q<Label>("report-fps");

            if (copyUrlBtn != null)    copyUrlBtn.clicked    += () => CopyToClipboard(urlField?.value);
            if (copyIframeBtn != null) copyIframeBtn.clicked += () => CopyToClipboard(iframeField?.value);
            if (openBrowserBtn != null) openBrowserBtn.clicked += OpenInBrowser;
            if (downloadBtn != null) downloadBtn.clicked += () => { if (!string.IsNullOrEmpty(urlField?.value)) Application.OpenURL(urlField.value); };
            if (newProjectBtn != null) newProjectBtn.clicked += () => AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList);

            errorMessage = root.Q<Label>("build-error-message");
            errorLog     = root.Q<Label>("build-error-log");
            retryBtn     = root.Q<Button>("retry-build-btn");
            if (retryBtn != null) retryBtn.clicked += () => { ShowState(pre); };
        }

        public Task EnterAsync()
        {
            var preset = ServiceLocator.Get<PresetRegistry>().Get(project.PresetId);
            if (presetName != null) presetName.text = preset?.Name ?? project.PresetId ?? "—";

            var lib = ServiceLocator.Get<AssetLibrary>();
            if (assetCount != null) assetCount.text = $"{lib.Count} файлов";
            if (hwProfile != null)
                hwProfile.text = project.TargetProfileId.HasValue
                    ? $"#{project.TargetProfileId.Value}"
                    : "не указан";

            ShowState(pre);
            return Task.CompletedTask;
        }

        public Task LeaveAsync()
        {
            cts?.Cancel();
            return Task.CompletedTask;
        }

        public string Validate() => null; // финальный шаг

        public void Dispose() { cts?.Cancel(); }

        // ---------- UI state ----------
        private void ShowState(VisualElement target)
        {
            foreach (var p in new[] { pre, progress, success, error })
            {
                if (p == null) continue;
                if (p == target) { p.RemoveFromClassList("hidden"); p.style.display = DisplayStyle.Flex; }
                else { p.AddToClassList("hidden"); p.style.display = DisplayStyle.None; }
            }
        }

        private void SelectType(BuildType t)
        {
            selectedType = t;

            var active = t switch
            {
                BuildType.Light => typeLight,
                BuildType.Full  => typeFull,
                _               => typeStandard
            };

            // Inline-стили — CSS .build-type-btn--active не перебивает inline из UXML.
            foreach (var b in new[] { typeLight, typeStandard, typeFull })
            {
                if (b == null) continue;
                bool isActive = b == active;
                b.EnableInClassList("build-type-btn--active", isActive);

                Color bg = isActive
                    ? new Color(0.29f, 0.62f, 1.00f, 0.15f)
                    : (Color)new Color32(0x2A, 0x2A, 0x3D, 0xFF);
                Color border = isActive
                    ? (Color)new Color32(0x4A, 0x9E, 0xFF, 0xFF)
                    : (Color)new Color32(0x3F, 0x3F, 0x5C, 0xFF);
                Color textCol = isActive
                    ? (Color)new Color32(0xFF, 0xFF, 0xFF, 0xFF)
                    : (Color)new Color32(0xF0, 0xF0, 0xF5, 0xFF);

                b.style.backgroundColor    = bg;
                b.style.borderTopColor     = border;
                b.style.borderBottomColor  = border;
                b.style.borderLeftColor    = border;
                b.style.borderRightColor   = border;
                b.style.color              = textCol;
                b.style.unityFontStyleAndWeight = isActive ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // ---------- Build flow ----------
        private async Task StartBuildAsync()
        {
            // Перед сборкой — сохранить проект
            try { await ServiceLocator.Get<ProjectService>().UpdateAsync(project); project.MarkSaved(); }
            catch (Exception ex) { Debug.LogException(ex); Toast.Warning("Не удалось сохранить перед сборкой."); }

            ShowState(progress);
            SetProgress(0, "Старт...", "");

            cts = new CancellationTokenSource();
            try
            {
                var buildService = ServiceLocator.Get<BuildService>();
                string buildId;
                try
                {
                    buildId = await buildService.StartBuildAsync(project, selectedType, cts.Token);
                }
                catch (Exception ex)
                {
                    ShowError("Не удалось запустить сборку.", ex.Message);
                    return;
                }

                if (string.IsNullOrEmpty(buildId))
                {
                    ShowError("Сервер не вернул build_id.", "");
                    return;
                }

                var tracker = ServiceLocator.Get<BuildStatusTracker>();
                tracker.OnStatusUpdated += HandleStatus;
                tracker.OnSuccess       += HandleSuccess;
                tracker.OnError         += HandleError;
                tracker.OnCancelled     += HandleCancelled;

                try { await tracker.TrackAsync(buildId, cts.Token); }
                finally
                {
                    tracker.OnStatusUpdated -= HandleStatus;
                    tracker.OnSuccess       -= HandleSuccess;
                    tracker.OnError         -= HandleError;
                    tracker.OnCancelled     -= HandleCancelled;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowError("Ошибка сборки.", ex.Message);
            }
        }

        private void CancelBuild()
        {
            cts?.Cancel();
            Toast.Info("Сборка отменена.");
            ShowState(pre);
        }

        private void HandleStatus(BuildStatusResponse s)
        {
            SetProgress(s.Progress, s.Stage, s.Log);
        }

        private void HandleSuccess(BuildResultResponse r)
        {
            if (urlField != null) urlField.value = r.Url ?? "";
            if (iframeField != null) iframeField.value = r.IframeCode ?? "";
            if (reportSize != null) reportSize.text = FormatSize(r.SizeBytes);
            if (reportTime != null) reportTime.text = FormatDuration(r.BuildTimeSeconds);
            if (reportFps != null) reportFps.text = r.ExpectedFps > 0 ? $"{r.ExpectedFps}+" : "—";
            ShowState(success);
            Toast.Success("Сборка завершена!");
        }

        private void HandleError(string msg) => ShowError("Ошибка сборки.", msg);
        private void HandleCancelled() => ShowState(pre);

        private void SetProgress(float p, string stage, string log)
        {
            var pct = Mathf.RoundToInt(Mathf.Clamp01(p) * 100f);
            if (progressFill != null) progressFill.style.width = new Length(pct, LengthUnit.Percent);
            if (percentLbl != null) percentLbl.text = $"{pct}%";
            if (stageLbl != null && !string.IsNullOrEmpty(stage)) stageLbl.text = stage;
            if (buildLog != null && !string.IsNullOrEmpty(log)) buildLog.text = log;
        }

        private void ShowError(string msg, string log)
        {
            if (errorMessage != null) errorMessage.text = msg;
            if (errorLog != null) errorLog.text = log ?? "";
            ShowState(error);
            Toast.Error(msg);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} МБ";
            return $"{bytes / (1024.0 * 1024 * 1024):0.#} ГБ";
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "—";
            int m = seconds / 60, s = seconds % 60;
            return m > 0 ? $"{m}m {s}s" : $"{s}s";
        }

        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIUtility.systemCopyBuffer = text;
            Toast.Info("Скопировано в буфер обмена.");
        }

        private void OpenInBrowser()
        {
            var url = urlField?.value;
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }
    }
}
