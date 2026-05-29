using System;
using InteractiveClient.Game.Presets.Puzzle;
using InteractiveClient.Game.Presets.SequencePreset;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Game
{
    /// <summary>
    /// Управляет жизненным циклом одного запущенного пресета: создаёт MonoBehaviour,
    /// размещает игровой UI, HUD и ResultPanel в указанном корне.
    ///
    /// Используется и в Step5Preview (внутри редактора), и в WebGL-билде (через ту же
    /// структуру корня — preset-stage / hud-host / result-host).
    ///
    /// Корень должен содержать три VisualElement-а:
    ///   #preset-stage  — здесь пресет рисует игровой контент (полный размер)
    ///   #hud-host      — оверлей под HUD (поверх игры, под ResultPanel)
    ///   #result-host   — оверлей под финальный экран
    /// Если этих элементов нет — PresetHost создаёт их сам.
    /// </summary>
    public class PresetHost : IDisposable
    {
        private readonly VisualElement root;
        private readonly VisualElement presetStage;
        private readonly VisualElement hudHost;
        private readonly VisualElement resultHost;

        private GameObject presetGo;
        private PresetBase preset;

        private VisualElement hudInstance;
        private Label hudTitle, hudScore, hudTimer;

        public event Action<PresetResult> GameEnded;
        public event Action ReplayRequested;
        public event Action ExitRequested;

        public PresetBase ActivePreset => preset;

        public PresetHost(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            presetStage = EnsureChild(root, "preset-stage", absolute: true);
            hudHost = EnsureChild(root, "hud-host", absolute: true);
            resultHost = EnsureChild(root, "result-host", absolute: true);

            // resultHost изначально скрыт.
            resultHost.style.display = DisplayStyle.None;
            resultHost.pickingMode = PickingMode.Position;
            hudHost.pickingMode = PickingMode.Ignore;
            presetStage.pickingMode = PickingMode.Position;
        }

        private static VisualElement EnsureChild(VisualElement parent, string name, bool absolute)
        {
            var existing = parent.Q<VisualElement>(name);
            if (existing != null) return existing;
            var v = new VisualElement { name = name };
            if (absolute)
            {
                v.style.position = Position.Absolute;
                v.style.left = 0; v.style.top = 0; v.style.right = 0; v.style.bottom = 0;
            }
            v.style.flexGrow = 1;
            parent.Add(v);
            return v;
        }

        /// <summary>
        /// Создаёт MonoBehaviour указанного типа, прокидывает контекст и поднимает HUD.
        /// </summary>
        public void Launch<T>(PresetContext context, string title) where T : PresetBase
        {
            Cleanup();

            presetGo = new GameObject($"Preset_{typeof(T).Name}");
            preset = presetGo.AddComponent<T>();

            preset.ScoreChanged += OnScoreChanged;
            preset.GameEnded += OnGameEnded;

            BuildHud(title);

            preset.Initialize(context, presetStage);
            preset.StartGame();
        }

        private void BuildHud(string title)
        {
            hudHost.Clear();
            var asset = Resources.Load<VisualTreeAsset>("Game/GameHud");
#if UNITY_EDITOR
            if (asset == null)
                asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Game/GameHud.uxml");
#endif
            if (asset == null) { Debug.LogWarning("[PresetHost] GameHud.uxml not found"); return; }

            hudInstance = asset.CloneTree();
            hudInstance.style.flexGrow = 1;
            // CloneTree() оборачивает дерево в TemplateContainer, который по
            // умолчанию имеет pickingMode=Position и занимает ВСЮ площадь stage.
            // Хотя сам hudHost помечен Ignore, эта обёртка перехватывает клики
            // и не даёт им дойти до пресета. Делаем её и все дочерние элементы Ignore,
            // только нужные интерактивные части HUD'а можно поднять отдельно.
            hudInstance.pickingMode = PickingMode.Ignore;
            foreach (var child in hudInstance.Query<VisualElement>().ToList())
                child.pickingMode = PickingMode.Ignore;
            hudHost.Add(hudInstance);

            hudTitle = hudInstance.Q<Label>("hud-title");
            hudScore = hudInstance.Q<Label>("hud-score");
            hudTimer = hudInstance.Q<Label>("hud-timer");
            if (hudTitle != null) hudTitle.text = title ?? string.Empty;
            if (hudScore != null) hudScore.text = "0";
            if (hudTimer != null) hudTimer.text = string.Empty;
        }

        public void SetTimerText(string text)
        {
            if (hudTimer != null) hudTimer.text = text ?? string.Empty;
        }

        private void OnScoreChanged(int score)
        {
            if (hudScore != null) hudScore.text = score.ToString();
        }

        private void OnGameEnded(PresetResult result)
        {
            ShowResult(result);
            GameEnded?.Invoke(result);
        }

        private void ShowResult(PresetResult result)
        {
            resultHost.Clear();
            var asset = Resources.Load<VisualTreeAsset>("Game/ResultPanel");
#if UNITY_EDITOR
            if (asset == null)
                asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Game/ResultPanel.uxml");
#endif
            if (asset == null) { Debug.LogWarning("[PresetHost] ResultPanel.uxml not found"); return; }

            var tree = asset.CloneTree();
            tree.style.flexGrow = 1;
            resultHost.Add(tree);
            resultHost.style.display = DisplayStyle.Flex;

            var title = tree.Q<Label>("result-title");
            var score = tree.Q<Label>("result-score");
            var summary = tree.Q<Label>("result-summary");
            var btnReplay = tree.Q<Button>("btn-replay");
            var btnExit = tree.Q<Button>("btn-exit");

            if (title != null) title.text = result.Success ? "Победа" : "Конец игры";
            if (score != null) score.text = result.Score.ToString();
            if (summary != null) summary.text = result.Summary ?? string.Empty;
            if (btnReplay != null) btnReplay.clicked += () => ReplayRequested?.Invoke();
            if (btnExit != null) btnExit.clicked += () => ExitRequested?.Invoke();
        }

        public void HideResult()
        {
            resultHost.style.display = DisplayStyle.None;
            resultHost.Clear();
        }

        public void Cleanup()
        {
            if (preset != null)
            {
                preset.ScoreChanged -= OnScoreChanged;
                preset.GameEnded -= OnGameEnded;
                preset.StopGame();
            }
            if (presetGo != null) UnityEngine.Object.Destroy(presetGo);
            preset = null;
            presetGo = null;

            presetStage.Clear();
            hudHost.Clear();
            HideResult();
        }

        /// <summary>
        /// Запускает пресет по строковому ID (как в ProjectModel.PresetId).
        /// Возвращает false если ID неизвестен.
        /// </summary>
        public bool LaunchByPresetId(string presetId, PresetContext context)
        {
            switch (presetId)
            {
                case "puzzle":
                    Launch<PuzzlePreset>(context, "Собери пазл");
                    return true;
                case "sequence":
                    Launch<SequencePreset>(context, "Повтори последовательность");
                    return true;
                default:
                    Debug.LogWarning($"[PresetHost] Неизвестный presetId: '{presetId}'");
                    return false;
            }
        }

        public void Dispose() => Cleanup();
    }
}
