using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Game;
using InteractiveClient.Network;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace InteractiveClient.Game.Presets.Puzzle
{
    /// <summary>
    /// Пазл-пресет: изображение разбивается на N×M кусочков — игрок перетаскивает
    /// их на правильные места. Один ассет-слот «puzzle_image» (обязательный, Image).
    ///
    /// Параметры:
    ///   cols    int 2..6, default 3 — столбцов
    ///   rows    int 2..6, default 3 — строк
    ///
    /// Механика:
    ///   • Кусочки раскладываются в случайном порядке по сетке слотов.
    ///   • Перетаскивание: PointerDown → захват, PointerMove → следует за курсором,
    ///     PointerUp → snap к ближайшему слоту (swap с занимающим кусочком).
    ///   • Победа: все кусочки на своих местах. Счёт зависит от числа ходов.
    /// </summary>
    public class PuzzlePreset : PresetBase
    {
        // ── Параметры ──────────────────────────────────────────────────────────
        private int cols;
        private int rows;
        private int pieceCount;
        private const float PieceSize = 150f; // px

        // ── Состояние игры ────────────────────────────────────────────────────
        /// <summary>pieceSlot[pieceIdx] = slotIdx (куда поставлен этот кусочек)</summary>
        private int[] pieceSlot;
        /// <summary>slotPiece[slotIdx] = pieceIdx (какой кусочек стоит в слоте)</summary>
        private int[] slotPiece;
        private int moveCount;
        private bool gameWon;

        // ── Визуальные элементы ───────────────────────────────────────────────
        private VisualElement pieceLayer;
        private Label hintLabel;
        private VisualElement[] pieceElements;

        // ── Текстуры ──────────────────────────────────────────────────────────
        // Используем единую source-текстуру и показываем нужный фрагмент в каждом
        // куске через вложенный VisualElement с overflow:hidden + offset.
        // Раньше делали GetPixels/SetPixels по N мелких текстур — это валило
        // UI Toolkit Repaint в Assertion failed.
        private Texture2D sourceTexture;

        // ── Drag state ────────────────────────────────────────────────────────
        private int dragPieceIdx = -1;
        private Vector2 dragOffset;
        private VisualElement dragElement;
        // Сохранённые ссылки на лямбды per-piece, чтобы их можно было отписать.
        private EventCallback<PointerDownEvent>[] pieceDownCallbacks;

        // ── Async ─────────────────────────────────────────────────────────────
        private readonly CancellationTokenSource cts = new();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void OnInitialize()
        {
            cols = Mathf.Clamp(Context.GetInt("cols", 3), 2, 6);
            rows = Mathf.Clamp(Context.GetInt("rows", 3), 2, 6);
            pieceCount = cols * rows;

            BuildLayout();
            _ = LoadImageAsync();
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            GameRoot.Clear();

            // ── Outer wrapper ──
            var wrapper = new VisualElement { name = "puzzle-wrapper" };
            wrapper.style.flexGrow = 1;
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.alignItems = Align.Center;
            wrapper.style.justifyContent = Justify.Center;
            wrapper.style.paddingTop = 24;
            wrapper.style.paddingBottom = 24;

            // ── Title ──
            var title = new Label("Собери пазл");
            title.style.fontSize = 22;
            title.style.color = new StyleColor(new Color(0.94f, 0.94f, 0.96f));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 16;
            wrapper.Add(title);

            // ── Board container ──
            float boardW = cols * PieceSize;
            float boardH = rows * PieceSize;

            var boardContainer = new VisualElement { name = "puzzle-board" };
            boardContainer.style.position = Position.Relative;
            boardContainer.style.width = boardW;
            boardContainer.style.height = boardH;
            boardContainer.style.flexShrink = 0; // не давать flex-родителю сжимать доску — иначе hit-test уезжает
            boardContainer.style.overflow = Overflow.Hidden;
            boardContainer.style.borderTopLeftRadius = 8;
            boardContainer.style.borderTopRightRadius = 8;
            boardContainer.style.borderBottomLeftRadius = 8;
            boardContainer.style.borderBottomRightRadius = 8;
            boardContainer.style.borderTopWidth = 2;
            boardContainer.style.borderBottomWidth = 2;
            boardContainer.style.borderLeftWidth = 2;
            boardContainer.style.borderRightWidth = 2;
            var boardBorder = new StyleColor(new Color(0.25f, 0.25f, 0.37f));
            boardContainer.style.borderTopColor = boardBorder;
            boardContainer.style.borderBottomColor = boardBorder;
            boardContainer.style.borderLeftColor = boardBorder;
            boardContainer.style.borderRightColor = boardBorder;

            // ── Slot layer (background: grid of empty cells) ──
            var slotLayer = new VisualElement { name = "slot-layer" };
            slotLayer.style.position = Position.Absolute;
            slotLayer.style.left = 0; slotLayer.style.top = 0;
            slotLayer.style.width = boardW; slotLayer.style.height = boardH;

            for (int i = 0; i < pieceCount; i++)
            {
                int col = i % cols;
                int row = i / cols;

                var slot = new VisualElement { name = $"slot-{i}" };
                slot.style.position = Position.Absolute;
                slot.style.left = col * PieceSize;
                slot.style.top = row * PieceSize;
                slot.style.width = PieceSize;
                slot.style.height = PieceSize;
                slot.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.18f));

                var slotBorder = new StyleColor(new Color(1f, 1f, 1f, 0.07f));
                slot.style.borderTopWidth = 1;    slot.style.borderTopColor = slotBorder;
                slot.style.borderBottomWidth = 1; slot.style.borderBottomColor = slotBorder;
                slot.style.borderLeftWidth = 1;   slot.style.borderLeftColor = slotBorder;
                slot.style.borderRightWidth = 1;  slot.style.borderRightColor = slotBorder;

                slotLayer.Add(slot);
            }

            // ── Piece layer (draggable pieces live here) ──
            pieceLayer = new VisualElement { name = "piece-layer" };
            pieceLayer.style.position = Position.Absolute;
            pieceLayer.style.left = 0; pieceLayer.style.top = 0;
            pieceLayer.style.width = boardW; pieceLayer.style.height = boardH;
            pieceLayer.pickingMode = PickingMode.Position;

            boardContainer.Add(slotLayer);
            boardContainer.Add(pieceLayer);
            wrapper.Add(boardContainer);

            // ── Hint / status label ──
            // Счётчик ходов отображается через HUD (в шапке) — RaiseScoreChanged(moveCount).
            // Здесь оставляем только подсказку.
            hintLabel = new Label("Загрузка изображения…");
            hintLabel.style.marginTop = 16;
            hintLabel.style.fontSize = 13;
            hintLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.6f));
            wrapper.Add(hintLabel);

            GameRoot.Add(wrapper);
        }

        // ── Загрузка и нарезка ────────────────────────────────────────────────

        private async Task LoadImageAsync()
        {
            var asset = Context.GetAsset("puzzle_image");
            if (asset == null)
            {
                SetHint("Ассет «puzzle_image» не задан.", error: true);
                return;
            }

            // Приоритет: локальный файл (мгновенно, без сервера) → серверный URL
            string url = null;
            bool needsAuth = false;
            if (!string.IsNullOrEmpty(asset.LocalPath) && System.IO.File.Exists(asset.LocalPath))
            {
                // Безопасно кодируем путь (на случай кириллицы / пробелов)
                url = new System.Uri(asset.LocalPath).AbsoluteUri;
            }
            else if (!string.IsNullOrEmpty(asset.Url))
            {
                url = asset.Url;
                needsAuth = url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                SetHint("У ассета нет ни локального пути, ни серверного URL.", error: true);
                return;
            }

            Debug.Log($"[PuzzlePreset] Загружаем картинку: {url}");
            using var req = UnityWebRequestTexture.GetTexture(url);

            // Bearer-токен только для серверных HTTP(S) URL
            if (needsAuth)
            {
                try
                {
                    var api = ServiceLocator.Get<ApiClient>();
                    var token = api?.AuthTokenProvider?.Invoke();
                    if (!string.IsNullOrEmpty(token))
                        req.SetRequestHeader("Authorization", $"Bearer {token}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PuzzlePreset] Не удалось добавить токен авторизации: {ex.Message}");
                }
            }

            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (cts.IsCancellationRequested) { req.Abort(); return; }
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetHint($"Ошибка загрузки: {req.error}", error: true);
                return;
            }

            sourceTexture = DownloadHandlerTexture.GetContent(req);
            if (sourceTexture == null)
            {
                SetHint("Не удалось декодировать изображение.", error: true);
                return;
            }

            SetHint("Перетащи кусочки на правильные места", error: false);

            // Если StartGame уже был вызван до загрузки текстуры —
            // достраиваем игровое состояние сейчас.
            if (IsRunning && pieceElements == null)
                SetupBoard();
        }

        // ── Gameplay ──────────────────────────────────────────────────────────

        protected override void OnStartGame()
        {
            // Текстура может ещё качаться — это нормально:
            // когда LoadImageAsync завершится, он сам поднимет SetupBoard().
            if (sourceTexture == null) return;
            SetupBoard();
        }

        /// <summary>
        /// Реальная инициализация игрового состояния — вызывается когда
        /// и StartGame, и загрузка текстур завершены.
        /// </summary>
        private void SetupBoard()
        {
            moveCount = 0;
            gameWon = false;
            UpdateMovesLabel();

            // Инициализация состояния — Fisher-Yates перемешивание
            pieceSlot = new int[pieceCount];
            slotPiece = new int[pieceCount];

            var shuffled = Enumerable.Range(0, pieceCount).ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            for (int pieceIdx = 0; pieceIdx < pieceCount; pieceIdx++)
            {
                pieceSlot[pieceIdx] = shuffled[pieceIdx];
                slotPiece[shuffled[pieceIdx]] = pieceIdx;
            }

            BuildPieceElements();
            RegisterDragHandlers();
            Debug.Log($"[PuzzlePreset] Доска готова: {pieceCount} кусочков, drag-handlers зарегистрированы.");
        }

        private void BuildPieceElements()
        {
            pieceLayer.Clear();
            pieceElements = new VisualElement[pieceCount];

            float boardW = cols * PieceSize;
            float boardH = rows * PieceSize;

            for (int i = 0; i < pieceCount; i++)
            {
                int col = i % cols;
                int row = i / cols;

                // Кусочек — единый элемент 150×150 без вложений (важно для hit-testing'а
                // drag-handlers'а на pieceLayer).
                // Картинку показываем как backgroundImage всей доски, смещённую
                // в нужную позицию: backgroundSize=board, backgroundPosition=(-col, -row)*PieceSize.
                var piece = new VisualElement { name = $"piece-{i}" };
                piece.style.position = Position.Absolute;
                piece.style.width    = PieceSize;
                piece.style.height   = PieceSize;
                piece.pickingMode    = PickingMode.Position;

                piece.style.backgroundImage = new StyleBackground(sourceTexture);
                piece.style.backgroundSize  = new StyleBackgroundSize(new BackgroundSize(
                    new Length(boardW, LengthUnit.Pixel),
                    new Length(boardH, LengthUnit.Pixel)));
                piece.style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(
                    BackgroundPositionKeyword.Left,
                    new Length(-col * PieceSize, LengthUnit.Pixel)));
                piece.style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(
                    BackgroundPositionKeyword.Top,
                    new Length(-row * PieceSize, LengthUnit.Pixel)));

                var pieceBorder = new StyleColor(new Color(0f, 0f, 0f, 0.5f));
                piece.style.borderTopWidth = 1;    piece.style.borderTopColor = pieceBorder;
                piece.style.borderBottomWidth = 1; piece.style.borderBottomColor = pieceBorder;
                piece.style.borderLeftWidth = 1;   piece.style.borderLeftColor = pieceBorder;
                piece.style.borderRightWidth = 1;  piece.style.borderRightColor = pieceBorder;

                // Расставляем по перемешанным слотам
                PositionPieceAtSlot(piece, pieceSlot[i]);

                pieceElements[i] = piece;
                pieceLayer.Add(piece);
            }
        }

        private void PositionPieceAtSlot(VisualElement piece, int slotIdx)
        {
            int col = slotIdx % cols;
            int row = slotIdx / cols;
            piece.style.left = col * PieceSize;
            piece.style.top = row * PieceSize;
        }

        // ── Drag & Drop ───────────────────────────────────────────────────────

        private void RegisterDragHandlers()
        {
            // PointerDown — регистрируем ОТДЕЛЬНО НА КАЖДОМ кусочке.
            // Per-piece подписка надёжнее чем callback на pieceLayer:
            // ev.target однозначно равен piece, ev.localPosition сразу в его системе.
            if (pieceElements == null) return;

            pieceDownCallbacks = new EventCallback<PointerDownEvent>[pieceElements.Length];
            for (int idx = 0; idx < pieceElements.Length; idx++)
            {
                if (pieceElements[idx] == null) continue;
                int captured = idx; // closure
                EventCallback<PointerDownEvent> cb = ev => OnPiecePointerDown(captured, ev);
                pieceDownCallbacks[idx] = cb;
                pieceElements[idx].RegisterCallback(cb);
            }

            // Move/Up/Cancel — на pieceLayer (он CapturePointer'ит, поэтому события
            // во время drag'а гарантированно идут к нему).
            pieceLayer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            pieceLayer.RegisterCallback<PointerUpEvent>(OnPointerUp);
            pieceLayer.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        private void UnregisterDragHandlers()
        {
            if (pieceElements != null && pieceDownCallbacks != null)
            {
                for (int i = 0; i < pieceElements.Length && i < pieceDownCallbacks.Length; i++)
                {
                    if (pieceElements[i] != null && pieceDownCallbacks[i] != null)
                        pieceElements[i].UnregisterCallback(pieceDownCallbacks[i]);
                }
            }
            pieceDownCallbacks = null;

            if (pieceLayer == null) return;
            pieceLayer.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            pieceLayer.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            pieceLayer.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        /// <summary>
        /// PointerDown на конкретном куске (idx). ev.target == piece, поэтому
        /// ev.localPosition уже в системе координат piece (0..PieceSize).
        /// </summary>
        private void OnPiecePointerDown(int idx, PointerDownEvent ev)
        {
            if (!IsRunning || gameWon || dragPieceIdx >= 0) return;
            if (pieceElements == null || idx < 0 || idx >= pieceElements.Length) return;

            var piece = pieceElements[idx];
            if (piece == null) return;

            dragPieceIdx = idx;
            // Смещение от верхнего-левого угла piece до точки клика (внутри piece, 0..PieceSize)
            dragOffset = (Vector2)ev.localPosition;
            dragElement = piece;

            // Поднять наверх (рендерится поверх остальных)
            pieceLayer.Remove(dragElement);
            pieceLayer.Add(dragElement);

            // Визуальный эффект «подъёма»
            dragElement.style.opacity = 0.85f;

            pieceLayer.CapturePointer(ev.pointerId);
            ev.StopPropagation();

            Debug.Log($"[PuzzlePreset] Захват кусочка #{idx}, dragOffset={dragOffset}");
        }

        private void OnPointerMove(PointerMoveEvent ev)
        {
            if (dragPieceIdx < 0 || dragElement == null) return;

            // Во время CapturePointer на pieceLayer — target == pieceLayer,
            // поэтому ev.localPosition уже в системе координат pieceLayer.
            var pos = (Vector2)ev.localPosition - dragOffset;
            // Ограничиваем в пределах доски
            pos.x = Mathf.Clamp(pos.x, 0f, cols * PieceSize - PieceSize);
            pos.y = Mathf.Clamp(pos.y, 0f, rows * PieceSize - PieceSize);

            dragElement.style.left = pos.x;
            dragElement.style.top  = pos.y;
        }

        private void OnPointerUp(PointerUpEvent ev)
        {
            if (dragPieceIdx < 0 || dragElement == null) return;
            pieceLayer.ReleasePointer(ev.pointerId);

            // Находим ближайший слот по центру кусочка
            float cx = dragElement.style.left.value.value + PieceSize * 0.5f;
            float cy = dragElement.style.top.value.value  + PieceSize * 0.5f;

            int nearestSlot = FindNearestSlot(cx, cy);
            DropPiece(dragPieceIdx, nearestSlot);

            dragElement.style.opacity = 1f;
            dragPieceIdx = -1;
            dragElement = null;
        }

        private void OnPointerCancel(PointerCancelEvent ev)
        {
            if (dragPieceIdx < 0) return;
            pieceLayer.ReleasePointer(ev.pointerId);

            // Вернуть кусочек на место
            if (dragElement != null)
            {
                PositionPieceAtSlot(dragElement, pieceSlot[dragPieceIdx]);
                dragElement.style.opacity = 1f;
            }

            dragPieceIdx = -1;
            dragElement = null;
        }

        private int FindNearestSlot(float cx, float cy)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int s = 0; s < pieceCount; s++)
            {
                float scx = (s % cols) * PieceSize + PieceSize * 0.5f;
                float scy = (s / cols) * PieceSize + PieceSize * 0.5f;
                float dist = (cx - scx) * (cx - scx) + (cy - scy) * (cy - scy);
                if (dist < bestDist) { bestDist = dist; best = s; }
            }
            return best;
        }

        // ── Логика хода ───────────────────────────────────────────────────────

        private void DropPiece(int pieceIdx, int targetSlot)
        {
            int currentSlot = pieceSlot[pieceIdx];

            if (currentSlot == targetSlot)
            {
                // Snap обратно на своё место без счёта хода
                PositionPieceAtSlot(pieceElements[pieceIdx], currentSlot);
                return;
            }

            moveCount++;
            UpdateMovesLabel();

            // Swap кусочков между слотами
            int occupant = slotPiece[targetSlot]; // кусочек, что сейчас занимает targetSlot

            pieceSlot[pieceIdx]   = targetSlot;
            slotPiece[targetSlot] = pieceIdx;

            pieceSlot[occupant]   = currentSlot;
            slotPiece[currentSlot] = occupant;

            // Визуальное перемещение
            PositionPieceAtSlot(pieceElements[pieceIdx], targetSlot);
            PositionPieceAtSlot(pieceElements[occupant], currentSlot);

            CheckWin();
        }

        private void CheckWin()
        {
            for (int i = 0; i < pieceCount; i++)
                if (pieceSlot[i] != i) return;

            gameWon = true;
            UnregisterDragHandlers();

            SetHint("🎉 Пазл собран!", error: false, success: true);

            int minMoves = pieceCount - 1; // минимально возможное число ходов
            int maxScore = pieceCount * 100;
            int penalty  = Mathf.Max(0, moveCount - minMoves) * 5;
            int score    = Mathf.Max(pieceCount * 10, maxScore - penalty);

            RaiseGameEnded(new PresetResult
            {
                Success         = true,
                Score           = score,
                MaxScore        = maxScore,
                Mistakes        = Mathf.Max(0, moveCount - minMoves),
                Summary         = $"Собран за {moveCount} {MoveWord(moveCount)}!"
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateMovesLabel()
        {
            // Счётчик ходов идёт через HUD в шапке (PresetHost подписывается на ScoreChanged).
            RaiseScoreChanged(moveCount);
        }

        private void SetHint(string text, bool error, bool success = false)
        {
            if (hintLabel == null) return;
            hintLabel.text = text;
            if (success)
                hintLabel.style.color = new StyleColor(new Color(0.4f, 0.88f, 0.5f));
            else if (error)
                hintLabel.style.color = new StyleColor(new Color(0.96f, 0.44f, 0.44f));
            else
                hintLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.6f));
        }

        private static string MoveWord(int n)
        {
            int mod10  = n % 10;
            int mod100 = n % 100;
            if (mod100 is >= 11 and <= 14) return "ходов";
            return mod10 switch { 1 => "ход", 2 or 3 or 4 => "хода", _ => "ходов" };
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        protected override void OnStopGame()
        {
            UnregisterDragHandlers();
            dragPieceIdx = -1;
            dragElement = null;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cts.Cancel();
            cts.Dispose();

            // ВАЖНО: нельзя уничтожать sourceTexture сразу — UI Toolkit
            // может ещё пару кадров держать ссылку в render-queue и валить
            // RepaintPanels с Assertion failed. Откладываем на 4 кадра.
            if (sourceTexture != null)
            {
                DeferredTextureDestroy.Schedule(sourceTexture, frames: 4);
                sourceTexture = null;
            }
        }
    }

    /// <summary>
    /// Откладывает Destroy для Texture2D на N кадров, чтобы UI Toolkit
    /// успел очистить ссылки в render-queue. Без этого RepaintPanels валит
    /// Assertion failed при уничтожении пресета.
    /// </summary>
    internal static class DeferredTextureDestroy
    {
        public static void Schedule(Texture2D tex, int frames)
        {
            if (tex == null) return;
            var go = new GameObject("__DeferredTextureDestroy");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var helper = go.AddComponent<DeferredTextureDestroyHelper>();
            helper.Begin(tex, frames);
        }
    }

    internal class DeferredTextureDestroyHelper : MonoBehaviour
    {
        public void Begin(Texture2D tex, int frames) => StartCoroutine(Run(tex, frames));

        private IEnumerator Run(Texture2D tex, int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
            if (tex != null) UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(gameObject);
        }
    }
}
