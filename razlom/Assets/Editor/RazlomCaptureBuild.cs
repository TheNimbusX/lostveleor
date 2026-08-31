using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Точка входа для пакетной сборки плеера, из которого снимаются кадры.
    ///
    /// Снимок делается из СОБРАННОЙ игры, а не из окна редактора. Причина
    /// простая: Game-вью в редакторе показывает не то, что увидит игрок —
    /// другое разрешение, гизмо, свой набор качества. Сравнивать «до и после»
    /// по таким картинкам нельзя.
    /// </summary>
    public static class RazlomCaptureBuild
    {
        private const string OutputFlag = "-razlom-build-out";
        private const string RequestFile = "request-capture-build";

        [InitializeOnLoadMethod]
        // Capture requests are consumed after the editor domain reloads and
        // from the editor update loop while the project stays open.
        private static void BuildRequestedFromOpenEditor()
        {
            EditorApplication.update += PollBuildRequest;
            EditorApplication.delayCall += PollBuildRequest;
        }

        private static void PollBuildRequest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string request = Path.Combine(RepositoryRoot(), "artifacts", RequestFile);
            if (!File.Exists(request)) return;

            // Маркер снимается до старта: если сборка упадёт, следующий
            // domain reload не зациклит тяжёлый BuildPipeline.
            File.Delete(request);
            EditorApplication.update -= PollBuildRequest;
            BuildFromMenu();
        }

        [MenuItem("Разлом/Собрать плеер для съёмки")]
        public static void BuildFromMenu()
        {
            Build(Path.Combine(RepositoryRoot(), "artifacts", "capture-build"));
        }

        /// <summary>
        /// Вызывается из командной строки: -executeMethod
        /// Game.EditorTools.RazlomCaptureBuild.Build
        /// </summary>
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, OutputFlag);
            string output = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : Path.Combine(RepositoryRoot(), "artifacts", "capture-build");

            Build(output);
        }

        private static void Build(string outputDirectory)
        {
            // Controller is generated from imported FBXs. Rebuild it explicitly
            // in batch mode as delayCall order is not a reliable build contract.
            global::RazlomPelagV5AnimatorBuilder.Build();
            global::RazlomPelagVfxAssetBuilder.Build();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("В настройках сборки нет ни одной включённой сцены.");
                return;
            }

            Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDirectory, "Razlom.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,

                // Development-плеер оставляет Debug.Log в файле лога — по нему
                // видно, какие кадры реально записались, и упал ли запуск.
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Сборка не удалась: {summary.result}, ошибок {summary.totalErrors}.");
                return;
            }

            Debug.Log($"[build] {summary.outputPath}  {summary.totalSize} байт");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[build] {message}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Корень репозитория — родитель папки Unity-проекта. Артефакты сборки
        /// не должны падать внутрь Assets: там их подберёт импортёр.
        /// </summary>
        private static string RepositoryRoot()
        {
            return Directory.GetParent(Application.dataPath).Parent.FullName;
        }
    }
}
