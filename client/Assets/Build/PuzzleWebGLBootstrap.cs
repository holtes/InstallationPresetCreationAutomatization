using System.Collections;
using System.Collections.Generic;
using InteractiveClient.AssetsModule;
using InteractiveClient.Game;
using InteractiveClient.Game.Presets.Puzzle;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace InteractiveClient.Build
{
    /// <summary>
    /// Точка входа собранного WebGL-бандла «Пазл».
    /// Читает StreamingAssets/project_config.json, формирует PresetContext,
    /// поднимает PresetHost и запускает PuzzlePreset.
    ///
    /// Структура project_config.json (см. server/app/services/config_generator.py):
    ///   {
    ///     "assets":     { "puzzle_image": { "filename": "photo.jpg", "content_type": "image/jpeg" } },
    ///     "parameters": { "cols": 3, "rows": 3 }
    ///   }
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PuzzleWebGLBootstrap : MonoBehaviour
    {
        private PresetHost host;
        private Label statusLabel;

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;

            // Подложка во весь экран
            var stage = new VisualElement { name = "webgl-root" };
            stage.style.position = Position.Absolute;
            stage.style.left = 0; stage.style.top = 0; stage.style.right = 0; stage.style.bottom = 0;
            stage.style.backgroundColor = new StyleColor(new Color(0.10f, 0.10f, 0.14f));
            root.Add(stage);

            statusLabel = new Label("Загрузка конфигурации…");
            statusLabel.style.position = Position.Absolute;
            statusLabel.style.left = 16; statusLabel.style.top = 16;
            statusLabel.style.fontSize = 12;
            statusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.7f));
            stage.Add(statusLabel);

            host = new PresetHost(stage);
            host.ReplayRequested += OnReplay;
            host.ExitRequested   += () => host?.HideResult();

            StartCoroutine(LoadAndLaunch());
        }

        private IEnumerator LoadAndLaunch()
        {
            var configUrl = Application.streamingAssetsPath + "/project_config.json";
            using (var req = UnityWebRequest.Get(configUrl))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    SetStatus($"Ошибка загрузки project_config.json: {req.error}", error: true);
                    yield break;
                }

                JObject cfg;
                try { cfg = JObject.Parse(req.downloadHandler.text); }
                catch (System.Exception ex)
                {
                    SetStatus($"Не удалось распарсить config: {ex.Message}", error: true);
                    yield break;
                }

                LaunchFromConfig(cfg);
            }
        }

        private void LaunchFromConfig(JObject cfg)
        {
            var assets = new Dictionary<string, AssetModel>();

            var imageNode = cfg["assets"]?["puzzle_image"] as JObject;
            if (imageNode == null)
            {
                SetStatus("В конфигурации нет слота puzzle_image.", error: true);
                return;
            }

            var filename = imageNode["filename"]?.Value<string>();
            if (string.IsNullOrEmpty(filename))
            {
                SetStatus("В слоте puzzle_image не указан filename.", error: true);
                return;
            }

            // В WebGL streamingAssetsPath — это URL вида "<base>/StreamingAssets".
            // Файл уже лежит рядом с index.html в /StreamingAssets/<filename>.
            var imageUrl = Application.streamingAssetsPath + "/" + filename;

            assets["puzzle_image"] = new AssetModel
            {
                Id       = imageNode["asset_id"]?.Value<string>() ?? "0",
                Filename = filename,
                Type     = AssetType.Image,
                MimeType = imageNode["content_type"]?.Value<string>(),
                Url      = imageUrl
            };

            var parameters = cfg["parameters"] as JObject ?? new JObject();

            // Дефолты на случай если параметры не пришли
            if (parameters["cols"] == null) parameters["cols"] = 3;
            if (parameters["rows"] == null) parameters["rows"] = 3;

            SetStatus("");

            var ctx = new PresetContext(assets, parameters);
            host.Launch<PuzzlePreset>(ctx, "Собери пазл");
        }

        private void OnReplay()
        {
            host.HideResult();
            StartCoroutine(LoadAndLaunch());
        }

        private void SetStatus(string text, bool error = false)
        {
            if (statusLabel == null) return;
            statusLabel.text = text ?? "";
            statusLabel.style.color = error
                ? new StyleColor(new Color(0.96f, 0.44f, 0.44f))
                : new StyleColor(new Color(0.6f, 0.6f, 0.7f));
            statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnDestroy() => host?.Dispose();
    }
}
