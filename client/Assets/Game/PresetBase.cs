using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Game
{
    /// <summary>
    /// Базовый класс игрового пресета. Все 5 пресетов наследуются от него.
    ///
    /// Жизненный цикл:
    ///   1. PresetHost создаёт GameObject + MonoBehaviour конкретного пресета.
    ///   2. PresetHost вызывает Initialize(context, gameRoot).
    ///   3. Когда пользователь жмёт «Старт» (или сразу) — StartGame().
    ///   4. Пресет зовёт RaiseScoreChanged(...) по ходу игры.
    ///   5. На завершении — RaiseGameEnded(result), хост ловит и показывает ResultPanel.
    ///
    /// Пресет рисует свой UI в gameRoot (это VisualElement в #preset-stage).
    /// HUD и ResultPanel — это уже зона ответственности PresetHost.
    /// </summary>
    public abstract class PresetBase : MonoBehaviour
    {
        protected PresetContext Context { get; private set; }
        protected VisualElement GameRoot { get; private set; }

        public event Action<int> ScoreChanged;
        public event Action<PresetResult> GameEnded;

        public bool IsRunning { get; private set; }

        public void Initialize(PresetContext context, VisualElement gameRoot)
        {
            Context = context;
            GameRoot = gameRoot;
            OnInitialize();
        }

        public void StartGame()
        {
            if (IsRunning) return;
            IsRunning = true;
            OnStartGame();
        }

        public void StopGame()
        {
            if (!IsRunning) return;
            IsRunning = false;
            OnStopGame();
        }

        protected void RaiseScoreChanged(int newScore) => ScoreChanged?.Invoke(newScore);

        protected void RaiseGameEnded(PresetResult result)
        {
            IsRunning = false;
            GameEnded?.Invoke(result);
        }

        // ---- хуки для наследников ----
        protected virtual void OnInitialize() { }
        protected virtual void OnStartGame() { }
        protected virtual void OnStopGame() { }

        protected virtual void OnDestroy()
        {
            ScoreChanged = null;
            GameEnded = null;
        }
    }
}
