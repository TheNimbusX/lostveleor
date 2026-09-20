using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.EditorTools
{
    /// <summary>
    /// Меню «Разлом → Запустить актуальный EXE»: полноценная игра, а не Play Mode.
    ///
    /// Сборка лежит в artifacts/game-build (папка в .gitignore) и пересобирается,
    /// только если что-то в Assets, ProjectSettings или Packages новее exe, —
    /// иначе игра стартует сразу. Сборка обычная, без Development-водяного знака
    /// и без флагов съёмки: игра открывается главным меню, как у игрока.
    ///
    /// Отличие от RazlomCaptureBuild: тот собирает копию проекта в пакетном
    /// режиме и сам прогоняет установщики; здесь открытый редактор уже всё
    /// подготовил при загрузке, и повторный прогон установщиков трогал бы
    /// рабочие ассеты владельца.
    /// </summary>
    public static class RazlomGameLauncher
    {
        const string ExeName = "Razlom.exe";

        [MenuItem("Разлом/Запустить актуальный EXE", priority = 0)]
        static void LaunchLatest() => Launch(force: false);

        [MenuItem("Разлом/Пересобрать и запустить EXE", priority = 1)]
        static void RebuildAndLaunch() => Launch(force: true);

        [MenuItem("Разлом/Открыть папку сборки", priority = 2)]
        static void RevealBuild()
        {
            Directory.CreateDirectory(BuildDirectory);
            EditorUtility.RevealInFinder(File.Exists(ExePath) ? ExePath : BuildDirectory);
        }

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string BuildDirectory => Path.Combine(Directory.GetParent(ProjectRoot).FullName, "artifacts", "game-build");
        static string ExePath => Path.Combine(BuildDirectory, ExeName);

        static void Launch(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Разлом", "Сначала выйди из Play Mode.", "Ок");
                return;
            }
            // Сборка берёт сцены с диска: несохранённые правки в неё не попадут.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string stale = force ? "пересборка по запросу" : StaleReason();
            if (stale != null)
            {
                if (!CloseRunningGame()) return;
                Debug.Log("[launcher] Сборка игры: " + stale);
                if (!Build()) return;
            }
            else Debug.Log("[launcher] Сборка актуальна, запускаю " + ExePath);

            Process.Start(new ProcessStartInfo(ExePath) { WorkingDirectory = BuildDirectory, UseShellExecute = true });
        }

        /// <summary>Почему нужна сборка; null — exe новее всех исходников.</summary>
        static string StaleReason()
        {
            if (!File.Exists(ExePath)) return "сборки ещё нет";
            DateTime built = File.GetLastWriteTimeUtc(ExePath);
            foreach (string folder in new[] { "Assets", "ProjectSettings", "Packages" })
            {
                string root = Path.Combine(ProjectRoot, folder);
                if (!Directory.Exists(root)) continue;
                string newer = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(file => File.GetLastWriteTimeUtc(file) > built);
                if (newer != null) return "изменён " + newer.Substring(ProjectRoot.Length + 1);
            }
            return null;
        }

        /// <summary>Запущенная игра держит файлы сборки — перезаписать их нельзя.</summary>
        static bool CloseRunningGame()
        {
            string target = Path.GetFullPath(ExePath);
            var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName))
                .Where(process => SamePath(process, target)).ToArray();
            if (running.Length == 0) return true;
            if (!EditorUtility.DisplayDialog("Разлом", "Игра из папки сборки уже запущена. Закрыть её и пересобрать?", "Закрыть и собрать", "Отмена"))
                return false;
            foreach (Process process in running)
            {
                try { process.Kill(); process.WaitForExit(5000); }
                catch (Exception error) { Debug.LogWarning("[launcher] Не удалось закрыть игру: " + error.Message); }
            }
            return true;
        }

        static bool SamePath(Process process, string target)
        {
            try { return string.Equals(Path.GetFullPath(process.MainModule.FileName), target, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static bool Build()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Разлом", "В Build Settings нет ни одной включённой сцены.", "Ок");
                return false;
            }
            Directory.CreateDirectory(BuildDirectory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[launcher] Собрано за " + summary.totalTime.TotalSeconds.ToString("0") + " с: " + ExePath);
                return true;
            }
            EditorUtility.DisplayDialog("Разлом", "Сборка не удалась: " + summary.result + ", ошибок " + summary.totalErrors + ". Подробности в Console.", "Ок");
            return false;
        }
    }
}
