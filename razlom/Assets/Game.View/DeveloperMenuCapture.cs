#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Проверка отрисовки меню разработчика (F8) в собранном плеере, не трогая редактор:
    /// capture.ps1 -Hud -ExtraArgs '-capture-dev-menu'. Только под -razlom-capture, по умолчанию выключена.
    ///
    /// Ждёт первую арену забега (лагерь не снимается), открывает меню и снимает каждую вкладку
    /// (devmenu-1-run … devmenu-4-visual), затем взведённое подтверждение «К боссу» (devmenu-5-armed), «Пелаг» в
    /// окне 1280×720 (devmenu-6-720p) и тестовый забег с бессмертием и тремя талантами (devmenu-7-test-run). На каждом
    /// шаге — строка [dev-menu-capture] в журнале плеера. После — меню закрывается, съёмка идёт дальше по расписанию.
    /// </summary>
    internal sealed class DeveloperMenuCapture : MonoBehaviour
    {
        public const string Flag = "-capture-dev-menu";
        private string _directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-razlom-capture") < 0 || Array.IndexOf(args, Flag) < 0) return;
            var capture = new GameObject("Developer menu capture").AddComponent<DeveloperMenuCapture>();
            DontDestroyOnLoad(capture.gameObject);
            int output = Array.IndexOf(args, "-capture-out");
            capture._directory = output >= 0 && output + 1 < args.Length ? args[output + 1] : Application.temporaryCachePath;
        }

        private IEnumerator Start()
        {
            TickDriver driver = null;
            DeveloperMenu menu = null;
            while (true)
            {
                if (driver == null) driver = FindAnyObjectByType<TickDriver>();
                if (driver != null && menu == null) menu = driver.GetComponent<DeveloperMenu>();
                if (menu != null && driver.Session != null && driver.Session.Mode == GameMode.Rift && driver.Run != null
                    && driver.Run.Phase == RunPhase.Clearing && !CampTransition.Covering)
                    break;
                yield return null;
            }
            // Арена собрана и завеса сошла: ещё немного, чтобы HUD проявился.
            float until = Time.unscaledTime + 2.5f;
            while (Time.unscaledTime < until) yield return null;

            menu.CaptureOpen();
            Log("open tab=" + menu.CurrentTab + " screen=" + Screen.width + "x" + Screen.height);
            string[] names = { "1-run", "2-combat", "3-pelag", "4-visual" };
            for (int tab = 0; tab < DevMenuRules.TabCount; tab++)
            {
                menu.CaptureSelectTab((DevTab)tab);
                yield return Shot(names[tab]);
            }

            menu.CaptureSelectTab(DevTab.Run);
            yield return null;
            menu.CaptureArm("run.boss");
            yield return Shot("5-armed");

            // Узкое окно: панель по высоте экрана, масштаб 0,75 — всё ли читается и не съезжает.
            int width = Screen.width, height = Screen.height;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            for (int i = 0; i < 4; i++) yield return null;
            menu.CaptureSelectTab(DevTab.Pelag);
            yield return Shot("6-720p");
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            for (int i = 0; i < 4; i++) yield return null;

            // Тестовый забег: бессмертие и три таланта Вихря — чипы «ТЕСТОВЫЙ», «БЕССМЕРТИЕ», «ВКЛ», «Пелаг · 3», «В лагерь».
            driver.Session.SetDeveloperInvulnerable(true);
            foreach (int index in new[] { 0, 1, 4 }) DeveloperTalents.Set(SabreTalentLine.Whirlwind, index, true);
            driver.RefreshAbilityBuild();
            menu.CaptureSelectTab(DevTab.Pelag);
            yield return Shot("7-test-run");

            menu.CaptureClose();
            DeveloperTalents.Clear();
            driver.RefreshAbilityBuild();
            Log("done open=" + menu.IsOpen + " timeScale=" + Time.timeScale);
        }

        private IEnumerator Shot(string name)
        {
            // Два кадра: смена вкладки применяется в Update, раскладка IMGUI — в следующем OnGUI.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_directory, "devmenu-" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log("shot " + name + " -> " + path);
            yield return null;
        }

        private static void Log(string message) => Debug.Log("[dev-menu-capture] " + message);
    }
}
#endif
