using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InteractiveClient.Editor
{
    /// <summary>
    /// CLI-точка для сборки WebGL-бандла пресета «Пазл».
    /// Вызывается:
    ///   Unity -quit -batchmode -nographics
    ///         -projectPath &lt;client&gt;
    ///         -executeMethod InteractiveClient.Editor.BuildScript.BuildWebGL
    ///         -outputPath &lt;dir&gt;
    /// Сцена и StreamingAssets уже зафиксированы в проекте.
    /// </summary>
    public static class BuildScript
    {
        private const string PuzzleScenePath = "Assets/Scenes/PuzzleWebGL.unity";

        public static void BuildWebGL()
        {
            string outputPath = ParseArg("-outputPath") ?? "Build/WebGL/Puzzle";

            if (!Path.IsPathRooted(outputPath))
                outputPath = Path.GetFullPath(outputPath);

            Directory.CreateDirectory(outputPath);

            if (!File.Exists(PuzzleScenePath))
            {
                Debug.LogError($"[BuildScript] Сцена не найдена: {PuzzleScenePath}");
                EditorApplication.Exit(2);
                return;
            }

            // Переключаемся на WebGL, если не на нём
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[BuildScript] Переключение платформы на WebGL...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[BuildScript] Не удалось переключить платформу на WebGL");
                    EditorApplication.Exit(3);
                    return;
                }
            }

            // WebGL-оптимальные настройки: brotli/gzip и compression detection
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);

            var options = new BuildPlayerOptions
            {
                scenes           = new[] { PuzzleScenePath },
                locationPathName = outputPath,
                target           = BuildTarget.WebGL,
                targetGroup      = BuildTargetGroup.WebGL,
                options          = BuildOptions.None
            };

            Debug.Log($"[BuildScript] Сборка WebGL → {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] УСПЕХ: {summary.totalSize} байт за {summary.totalTime}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BuildScript] ОШИБКА: {summary.result}, ошибок: {summary.totalErrors}");
                EditorApplication.Exit(1);
            }
        }

        private static string ParseArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
