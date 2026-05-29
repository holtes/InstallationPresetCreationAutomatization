namespace InteractiveClient.Game
{
    /// <summary>
    /// Итоговый результат прохождения пресета.
    /// Передаётся в OnGameEnded, отправляется на сервер как телеметрия и
    /// показывается на ResultPanel.
    /// </summary>
    public struct PresetResult
    {
        public bool Success;
        public int Score;
        public int MaxScore;
        public int Mistakes;
        public float DurationSeconds;
        public string Summary;          // короткая фраза для UI ("Длина 7 — отлично!")
    }
}
