using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using InteractiveClient.AssetsModule;
using InteractiveClient.AudioModule;
using InteractiveClient.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Game.Presets.SequencePreset
{
    /// <summary>
    /// Simon-style: серия подсветок плиток, игрок повторяет.
    ///
    /// Ассет-слоты:
    ///   tile_1..N            (image, required)         — иконки кнопок
    ///   tile_sound_1..N      (audio, optional)             — звук на нажатие/подсветку
    ///   bgm                  (audio, optional)             — фоновая петля
    ///   intro_text           (text, optional)          — текст-вступление (не используется в MVP)
    ///   reward_model         (model_3d, optional)      — реворд после milestone (не используется в MVP)
    ///
    /// Параметры:
    ///   tile_count           enum 4|6, default 4
    ///   start_length         int 1..10, default 3
    ///   flash_duration_ms    int 100..1500, default 400
    ///   gap_ms               int 50..1000, default 200
    ///   speedup_factor       float 0.7..1.0, default 0.95 (множитель к flash/gap каждый раунд)
    /// </summary>
    public class SequencePreset : PresetBase
    {
        // ---- runtime ----
        private readonly List<VisualElement> tiles = new();
        private readonly List<int> sequence = new();
        private int playerIndex;
        private int score;
        private bool acceptingInput;

        // tile assets
        private readonly List<Texture2D> tileTextures = new();
        private readonly List<AudioClip> tileSounds = new();
        private AudioBank audioBank;
        private readonly AudioLoader audioBankLoader = new();
        private readonly System.Threading.CancellationTokenSource cts = new();

        // params
        private int tileCount;
        private int startLength;
        private int flashMs;
        private int gapMs;
        private float speedupFactor;

        // current round timings (mutated by speedup)
        private int curFlashMs;
        private int curGapMs;

        protected override void OnInitialize()
        {
            tileCount = Mathf.Clamp(Context.GetInt("tile_count", 4), 4, 6);
            if (tileCount != 4 && tileCount != 6) tileCount = 4;
            startLength = Mathf.Max(1, Context.GetInt("start_length", 3));
            flashMs = Mathf.Clamp(Context.GetInt("flash_duration_ms", 400), 100, 1500);
            gapMs = Mathf.Clamp(Context.GetInt("gap_ms", 200), 50, 1000);
            speedupFactor = Mathf.Clamp(Context.GetFloat("speedup_factor", 0.95f), 0.7f, 1.0f);

            curFlashMs = flashMs;
            curGapMs = gapMs;

            audioBank = new AudioBank("SequencePreset_Audio");

            BuildLayout();
            _ = LoadAssetsAsync();
        }

        private void BuildLayout()
        {
            GameRoot.Clear();
            var stage = new VisualElement { name = "seq-stage" };
            stage.style.flexGrow = 1;
            stage.style.alignItems = Align.Center;
            stage.style.justifyContent = Justify.Center;
            stage.style.paddingTop = 80;

            var grid = new VisualElement { name = "seq-grid" };
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 520;
            grid.style.justifyContent = Justify.Center;

            for (int i = 0; i < tileCount; i++)
            {
                int idx = i;
                var tile = new VisualElement { name = $"tile-{i}" };
                tile.style.width = 240;
                tile.style.height = 240;
                tile.style.marginLeft = 8;
                tile.style.marginRight = 8;
                tile.style.marginTop = 8;
                tile.style.marginBottom = 8;
                tile.style.borderTopLeftRadius = 16;
                tile.style.borderTopRightRadius = 16;
                tile.style.borderBottomLeftRadius = 16;
                tile.style.borderBottomRightRadius = 16;
                tile.style.borderTopWidth = 2;
                tile.style.borderBottomWidth = 2;
                tile.style.borderLeftWidth = 2;
                tile.style.borderRightWidth = 2;
                tile.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
                tile.style.borderBottomColor = tile.style.borderTopColor;
                tile.style.borderLeftColor = tile.style.borderTopColor;
                tile.style.borderRightColor = tile.style.borderTopColor;
                tile.style.backgroundColor = TileFallbackColor(i, dim: true);

                tile.RegisterCallback<ClickEvent>(_ => OnTileClick(idx));
                tiles.Add(tile);
                grid.Add(tile);
            }

            stage.Add(grid);

            var hint = new Label("Смотри последовательность, потом повтори");
            hint.style.marginTop = 24;
            hint.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.8f));
            hint.style.fontSize = 14;
            stage.Add(hint);

            GameRoot.Add(stage);
        }

        private static Color TileFallbackColor(int i, bool dim)
        {
            // Палитра по индексу — на случай отсутствия image-ассетов.
            Color[] palette = {
                new (0.95f, 0.40f, 0.40f),  // red
                new (0.40f, 0.85f, 0.50f),  // green
                new (0.40f, 0.65f, 0.95f),  // blue
                new (0.95f, 0.85f, 0.40f),  // yellow
                new (0.75f, 0.50f, 0.95f),  // purple
                new (0.95f, 0.65f, 0.40f),  // orange
            };
            var c = palette[i % palette.Length];
            return dim ? new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f) : c;
        }

        private async Task LoadAssetsAsync()
        {
            // Параллельно: tile images и tile sounds.
            for (int i = 0; i < tileCount; i++)
            {
                var imgAsset = Context.GetAsset($"tile_{i + 1}");
                tileTextures.Add(null);
                tileSounds.Add(null);
                int idx = i;

                if (imgAsset != null && !string.IsNullOrEmpty(imgAsset.Url))
                {
                    _ = LoadTileTexture(idx, imgAsset);
                }

                var soundAsset = Context.GetAsset($"tile_sound_{i + 1}");
                if (soundAsset != null && !string.IsNullOrEmpty(soundAsset.Url))
                {
                    _ = LoadTileSound(idx, soundAsset.Url);
                }
            }

            // BGM
            var bgmAsset = Context.GetAsset("bgm");
            if (bgmAsset != null && !string.IsNullOrEmpty(bgmAsset.Url))
            {
                var clip = await audioBankLoader.LoadAsync(bgmAsset.Url, cts.Token);
                if (clip != null)
                {
                    audioBank.Register("bgm", clip);
                    audioBank.PlayLoop("bgm");
                }
            }
        }

        private async Task LoadTileTexture(int idx, AssetModel asset)
        {
            using var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(asset.Url);
            var op = req.SendWebRequest();
            while (!op.isDone) { if (cts.IsCancellationRequested) { req.Abort(); return; } await Task.Yield(); }
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) return;

            var tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
            if (tex == null || idx >= tiles.Count) return;
            tileTextures[idx] = tex;
            tiles[idx].style.backgroundImage = new StyleBackground(tex);
            tiles[idx].style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
        }

        private async Task LoadTileSound(int idx, string url)
        {
            var clip = await audioBankLoader.LoadAsync(url, cts.Token);
            if (clip == null || idx >= tileSounds.Count) return;
            tileSounds[idx] = clip;
            audioBank.Register($"tile_{idx}", clip);
        }

        // ---------- gameplay ----------

        protected override void OnStartGame()
        {
            sequence.Clear();
            score = 0;
            playerIndex = 0;
            curFlashMs = flashMs;
            curGapMs = gapMs;
            RaiseScoreChanged(0);

            for (int i = 0; i < startLength; i++) AddRandomStep();
            StartCoroutine(PlaybackThenInput());
        }

        private void AddRandomStep() => sequence.Add(Random.Range(0, tileCount));

        private IEnumerator PlaybackThenInput()
        {
            acceptingInput = false;
            // короткая пауза перед стартом
            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < sequence.Count; i++)
            {
                yield return FlashTile(sequence[i]);
                yield return new WaitForSeconds(curGapMs / 1000f);
            }

            playerIndex = 0;
            acceptingInput = true;
        }

        private IEnumerator FlashTile(int idx)
        {
            if (idx < 0 || idx >= tiles.Count) yield break;
            var t = tiles[idx];

            // подсветка: меняем фон и обводку
            var hasTexture = tileTextures[idx] != null;
            if (!hasTexture) t.style.backgroundColor = TileFallbackColor(idx, dim: false);
            t.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.85f));
            t.style.borderBottomColor = t.style.borderTopColor;
            t.style.borderLeftColor = t.style.borderTopColor;
            t.style.borderRightColor = t.style.borderTopColor;

            // звук плитки или дефолтный «бип»
            if (tileSounds[idx] != null) audioBank.Play($"tile_{idx}");

            yield return new WaitForSeconds(curFlashMs / 1000f);

            if (!hasTexture) t.style.backgroundColor = TileFallbackColor(idx, dim: true);
            t.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            t.style.borderBottomColor = t.style.borderTopColor;
            t.style.borderLeftColor = t.style.borderTopColor;
            t.style.borderRightColor = t.style.borderTopColor;
        }

        private void OnTileClick(int idx)
        {
            if (!acceptingInput || !IsRunning) return;

            // короткая визуальная отдача
            StartCoroutine(FlashTile(idx));

            int expected = sequence[playerIndex];
            if (idx != expected)
            {
                acceptingInput = false;
                EndWithFail();
                return;
            }

            playerIndex++;
            if (playerIndex >= sequence.Count)
            {
                // раунд пройден
                acceptingInput = false;
                score = sequence.Count;
                RaiseScoreChanged(score);

                AddRandomStep();
                curFlashMs = Mathf.Max(80, Mathf.RoundToInt(curFlashMs * speedupFactor));
                curGapMs = Mathf.Max(40, Mathf.RoundToInt(curGapMs * speedupFactor));
                StartCoroutine(NextRound());
            }
        }

        private IEnumerator NextRound()
        {
            yield return new WaitForSeconds(0.6f);
            yield return PlaybackThenInput();
        }

        private void EndWithFail()
        {
            audioBank.StopLoop();
            RaiseGameEnded(new PresetResult
            {
                Success = false,
                Score = score,
                MaxScore = score,
                Mistakes = 1,
                Summary = score == 0 ? "Попробуй ещё раз" : $"Длина последовательности: {score}"
            });
        }

        protected override void OnStopGame()
        {
            StopAllCoroutines();
            acceptingInput = false;
            audioBank.StopLoop();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cts.Cancel();
            cts.Dispose();
            audioBank?.Dispose();
            audioBankLoader?.Clear();
        }
    }
}
