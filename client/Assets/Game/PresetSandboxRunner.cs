using InteractiveClient.Game.Presets.Puzzle;
using InteractiveClient.Game.Presets.SequencePreset;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Game
{
    /// <summary>
    /// Локальный runtime-тест: вешается на пустой GameObject с UIDocument,
    /// запускает указанный пресет с пустым контекстом (цветные плитки-fallback,
    /// без ассетов и сервера). Нужен для дымового теста Слоя 5.
    ///
    /// Не используется в финальной сборке.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PresetSandboxRunner : MonoBehaviour
    {
        public enum PresetKind { Sequence, Puzzle }

        [SerializeField] private PresetKind preset = PresetKind.Sequence;
        [SerializeField] private int tileCount = 4;
        [SerializeField] private int startLength = 3;
        [SerializeField] private int puzzleCols = 3;
        [SerializeField] private int puzzleRows = 3;

        private PresetHost host;

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;

            // подложка под весь экран
            var stage = new VisualElement { name = "sandbox-root" };
            stage.style.position = Position.Absolute;
            stage.style.left = 0; stage.style.top = 0; stage.style.right = 0; stage.style.bottom = 0;
            stage.style.backgroundColor = new StyleColor(new Color(0.10f, 0.10f, 0.14f));
            root.Add(stage);

            host = new PresetHost(stage);
            host.ReplayRequested += OnReplay;
            host.ExitRequested += () => { host?.Cleanup(); };

            Launch();
        }

        private void Launch()
        {
            var parameters = new JObject
            {
                ["tile_count"] = tileCount,
                ["start_length"] = startLength,
                ["flash_duration_ms"] = 400,
                ["gap_ms"] = 200,
                ["speedup_factor"] = 0.95f
            };
            var ctx = new PresetContext(null, parameters);

            switch (preset)
            {
                case PresetKind.Sequence:
                    host.Launch<SequencePreset>(ctx, "Повтори последовательность");
                    break;
                case PresetKind.Puzzle:
                    var puzzleParams = new JObject
                    {
                        ["cols"] = puzzleCols,
                        ["rows"] = puzzleRows
                    };
                    var puzzleCtx = new PresetContext(null, puzzleParams);
                    host.Launch<PuzzlePreset>(puzzleCtx, "Собери пазл");
                    break;
            }
        }

        private void OnReplay()
        {
            host.HideResult();
            Launch();
        }

        private void OnDestroy()
        {
            host?.Dispose();
        }
    }
}
