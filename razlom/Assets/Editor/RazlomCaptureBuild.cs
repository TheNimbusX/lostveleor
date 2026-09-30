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
    /// Повторяемая съёмка собранной игры сохраняет авторскую сцену и профиль
    /// изображения; различия камеры и качества сверяются с живым редактором.
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
            CampModelImport.Prepare();
            // Боевой HUD на Canvas. Зеркало несёт префаб из живого проекта со
            // всеми ручными правками; собирается он здесь, только если его нет.
            CombatHudBuilder.EnsureBuilt(false);
            // Боевой HUD «Ночная акварель»: готовый префаб не пересобирается, а доводится миграциями
            // до CombatHudView.LayoutVersion — в batchmode сам [InitializeOnLoad] её не запускает.
            CombatHudWcBuilder.Build(false);
            // Так же меню паузы с настройками и окна кузнеца/торговца: без миграции в зеркале
            // осталась бы раскладка v0 (у торговца v0 нет плиток товара — только продажа).
            PauseMenuWcBuilder.Build(false);
            CampShopsWcBuilder.Build(false);
            CampPolishUiBuilder.BuildAll();
            // HUD забега (строка добычи — миграция v1): Build(false) на готовом префабе зовёт EnsureMigrated.
            RunHudWcBuilder.Build(false);
            PauseMenuBuilder.EnsureBuilt(false);
            CampTentBuilder.EnsureBuilt(false);
            // Дымная завеса («Карта тушью» — миграция v1): Build(false) на готовом префабе тоже зовёт EnsureMigrated.
            SmokeTransitionBuilder.Build(false);
            // Тлеющие метки у края экрана (2a, 30.09): нет префаба — собирается, есть — доводится миграциями.
            WorldEdgeMarksBuilder.Build(false);
            // Проверка нового UI в зеркале, пока общий редактор занят: RAZLOM_REBUILD_UI=1 пересобирает
            // префабы боевого HUD, забега, меню, паузы, меток мира и окон лагеря из сборщиков (в живом проекте они не меняются).
            if (Environment.GetEnvironmentVariable("RAZLOM_REBUILD_UI") == "1")
            {
                CombatHudWcBuilder.Build(true);
                RunHudWcBuilder.Build(true);
                MainMenuWcBuilder.Build(true);
                PauseMenuWcBuilder.Build(true);
                RunWorldWcBuilder.Build(true);
                CampTentWcBuilder.Build(true);
                CampShopsWcBuilder.Build(true);
                CampTrainingWcBuilder.Build(true);
                CampGuideWcBuilder.Build(true);
                CampRiftConfirmWcBuilder.Build(true);
                SmokeTransitionBuilder.Build(true);
            }
            // Controller is generated from imported FBXs. Rebuild it explicitly
            // in batch mode as delayCall order is not a reliable build contract.
            global::RazlomPelagV5AnimatorBuilder.Build();
            global::PelagTempoValidation.Validate();
            // То же и по мобам: их контроллер тоже собирается из клипов, и без
            // этой строки съёмка показывала контроллер, собранный до того, как
            // приехали новые клипы, — то есть врала про то, что в игре.
            global::RazlomMobAnimatorBuilder.Build();
            global::ForestBudCombatBuilder.Build();
            global::ForestWendigoBuilder.Build();
            // Версии эффектов мобов проверяются до сериализации плеера,
            // независимо от порядка InitializeOnLoad в зеркале проекта.
            global::ThorncasterVfxSetup.Install();
            global::SplitterVfxSetup.Install();
            global::ForestPuddleVfxSetup.Install();
            global::CombatPresentationSetup.EnsureProfiles();
            global::PelagAudioImport.Install();
            // Съёмка должна сохранять авторскую цветокоррекцию. Повторная
            // генерация заменяла настройки Inspector значениями из шаблона.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/Settings/CombatLook.asset") == null)
                global::RazlomSceneAuthoring.BuildLookProfile();
            global::RazlomPelagVfxAssetBuilder.BuildAnchorLeapOnly();
            global::PelagAnchorSlamContactSetup.Install();
            global::PelagAnchorSlamContactSetup.Validate();
            global::CommonFootstepVfxSetup.Install();
            global::PelagWhirlwindVfxSetup.Install();
            global::PelagSquallVfxSetup.Install();
            global::PelagRollVfxSetup.Install();
            global::PelagEvadeVfxSetup.Install();
            global::PelagCleaveVfxSetup.Install();
            global::PelagBlazeVfxSetup.Install();
            global::PelagOrdnanceVfxSetup.Install();
            global::PelagOrdnanceVfxSetup.ValidateProductionAssets();
            global::CampFlameProSetup.Install();

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
            if(Application.isBatchMode)
            {
                // Только теневая сцена съёмки: авторский редактор сохраняет свою открытую работу.
                foreach(string path in scenes)
                {
                    var captureScene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                    Debug.Log(CampDepthAuthoring.LoadedSceneInstall());
                    Debug.Log(CampNavigationAuthoring.InstallLoadedScene());
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(captureScene);
                }
            }

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
