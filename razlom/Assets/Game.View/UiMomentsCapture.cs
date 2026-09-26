using System;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Проверка моментов забега в собранном плеере (capture.ps1 -Hud -ExtraArgs '-capture-ui-moments'),
    /// не трогая редактор: вход на арену («Разлом 1 из N»), карта Разлома, зачистка («Разлом зачищен»),
    /// смерть (замедление, обесцвечивание, пауза перед итогами) и итоги с досчётом. На каждом шаге —
    /// кадр в папку съёмки и строка [ui-moments] в журнале плеера. Только под -razlom-capture.
    ///
    /// С -capture-ui-menu (и -MainMenu у capture.ps1) сначала снимает главное меню: раскрытие трещины,
    /// проявление лого и пунктов, наведение на пункт. PLAY жмётся позже обычного (<see cref="MenuReady"/>).
    /// С -capture-ui-film вместо отдельных кадров — плёнка: кадр каждые 1/15 с на открытии меню и на
    /// появлении HUD в арене (film-menu-000…, film-hud-000…), из неё собирается видео для владельца.
    /// С -capture-ui-route (и -capture-smoke) после зачистки герой встаёт на выход: награда, выбор
    /// маршрута (баг 26 сентября — экрана не было), плёнка дымной завесы (film-smoke-000…), вход во
    /// вторую арену, пауза по Esc — и только потом смерть и итоги.
    /// </summary>
    internal sealed class UiMomentsCapture : MonoBehaviour
    {
        static readonly (float at, string name)[] Shots =
        {
            (.4f, "01a-arena-intro"), (.8f, "01-arena-intro"), (1.3f, "01b-arena-intro"), (2.2f, "02-rift-map"), (3.1f, "02b-damage-numbers"), (4.3f, "03-arena-cleared"),
        };
        const float ClearAt = 3.4f, DieAt = 7.5f;

        /// <summary>
        /// Пора ли съёмке жать PLAY в главном меню (MainMenuView): обычно через 3 с после запуска,
        /// под -capture-ui-menu — когда сняты все кадры меню.
        /// </summary>
        internal static bool MenuReady => _menuCapture ? _menuDone : Time.unscaledTime >= 3f;
        static bool _menuCapture, _menuDone;
        static readonly (float at, string name)[] MenuShots =
        {
            (.15f, "m1-open"), (.45f, "m2-crack"), (.8f, "m3-crack"), (1.2f, "m4-logo"), (1.8f, "m5-menu"), (2.6f, "m6-rest"), (3.6f, "m7-rest"),
            (4.25f, "m8-hover"), (4.6f, "m9-hover"),
        };
        bool _menu, _film, _route;
        // Шаги сценария маршрута: когда увидели фазу и что уже сделали.
        float _routeClock = -1f, _phaseAt, _smokeNext, _arenaAt = -1f;
        RunPhase _seenPhase = RunPhase.Idle;
        int _routeStep, _smokeFrame, _firstDepth = -1;
        bool _atExit, _routeChosen;
        const float FilmStep = 1f / 15f, FilmMenuEnd = 5.2f, FilmHover = 3.4f, FilmHudEnd = 2f;
        float _filmNext;
        int _filmFrame, _hudFrame;
        float _hudNext;
        float _menuStart = -1f;
        int _menuShot;
        MainMenuInkItem _hovered;

        TickDriver _driver;
        string _directory;
        float _riftAt = -1f, _deathAt = -1f, _clock;
        int _shot;
        bool _singed, _cleared, _killed, _deathShots, _toasts;
        int _deathStep;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-razlom-capture") < 0 || Array.IndexOf(args, "-capture-ui-moments") < 0) return;
            var capture = new GameObject("UI moments capture").AddComponent<UiMomentsCapture>();
            capture._menu = _menuCapture = Array.IndexOf(args, "-capture-ui-menu") >= 0;
            capture._film = Array.IndexOf(args, "-capture-ui-film") >= 0;
            capture._route = Array.IndexOf(args, "-capture-ui-route") >= 0;
            int output = Array.IndexOf(args, "-capture-out");
            capture._directory = output >= 0 && output + 1 < args.Length ? args[output + 1] : Application.temporaryCachePath;
        }

        void Update()
        {
            if (_menu && MainMenuView.IsOpen) { Menu(); return; }
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            GameSession session = _driver != null ? _driver.Session : null;
            if (session == null) return;
            if (_riftAt < 0f)
            {
                if (session.Mode != GameMode.Rift || _driver.Run == null) return;
                _riftAt = Time.unscaledTime;
                Log("rift depth=" + _driver.Run.Depth + "/" + _driver.Run.TotalLevels + " boss=" + _driver.Run.BossId);
            }
            // Время забега — шагом не больше 0,1 с за кадр, как у плашки: первые кадры арены
            // длятся секунды, и по настоящим часам кадры снимались подряд за один миг игры.
            if (Time.unscaledTime > _riftAt) _clock += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float t = _clock;
            if (t < 2f)
            {
                var b = FindAnyObjectByType<HudLevelBanner>(FindObjectsInactive.Include);
                Log("frame " + Time.frameCount + " t=" + t.ToString("0.00") + " dt=" + Time.unscaledDeltaTime.ToString("0.000")
                    + " phase=" + (_driver.Run != null ? _driver.Run.Phase.ToString() : "-")
                    + " banner=" + (b != null && b.gameObject.activeInHierarchy) + " alpha=" + (b != null && b.Group != null ? b.Group.alpha.ToString("0.00") : "-")
                    + " shownFor=" + (b != null ? b.ShownFor.ToString("0.00") : "-") + " scale=" + (b != null && b.Plate != null ? b.Plate.localScale.x.ToString("0.00") : "-"));
            }
            if (_film && t >= _hudNext && t <= FilmHudEnd)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(_directory, "film-hud-" + _hudFrame.ToString("000") + ".png"));
                _hudFrame++;
                _hudNext += FilmStep;
            }
            else if (_shot < Shots.Length && t >= Shots[_shot].at) { Shot(Shots[_shot].name); _shot++; }

            RiftRun run = _driver.Run;
            // Две всплывашки для кадров 02 и 02b: вещь редкости и золото (без подбора на арене их не видно).
            if (!_toasts && t >= 1.9f)
            {
                _toasts = true;
                var toasts = FindAnyObjectByType<HudToasts>(FindObjectsInactive.Include);
                if (toasts != null)
                {
                    toasts.Push(Resources.Load<Texture2D>("UI/Items/potion_health_small"), UiTheme.Role.Rare, "Кожаная куртка", "Редкая · ур. 4", UiTheme.Role.Rare);
                    toasts.Push(null, UiTheme.Role.Coins, "+45 золота", null, UiTheme.Role.Coins);
                    Log("toasts pushed");
                }
            }
            // Слабое горение перед зачисткой — чтобы на кадре были обычные цифры урона.
            if (!_singed && t >= 2.6f && session.Mode == GameMode.Rift && run != null)
            {
                _singed = true;
                for (int id = 1; id < run.Sim.Entities.Count; id++)
                    if (run.Sim.Entities.Alive[id]) run.Sim.Statuses.ApplyBurn(id, Fix64.FromInt(23), 30, 0, 0);
            }
            if (!_cleared && t >= ClearAt && session.Mode == GameMode.Rift && run != null)
            {
                _cleared = true;
                int burned = 0;
                for (int id = 1; id < run.Sim.Entities.Count; id++)
                    if (run.Sim.Entities.Alive[id]) { run.Sim.Statuses.ApplyBurn(id, Fix64.FromInt(1000000), 1, 0, 0); burned++; }
                Log("burned " + burned + " enemies");
            }
            if (_route && !_killed && session.Mode == GameMode.Rift && run != null && !Route(run, t)) return;
            if (!_killed && (_route || t >= DieAt) && session.Mode == GameMode.Rift && run != null)
            {
                _killed = true;
                if (session.DeveloperInvulnerable) session.SetDeveloperInvulnerable(false);
                run.Sim.Statuses.ApplyBurn(Simulation.PlayerId, Fix64.FromInt(1000000), 1, 0, 0);
                Log("burning the hero; phase=" + run.Phase);
            }
            if (_deathAt < 0f && session.Mode == GameMode.Summary)
            {
                _deathAt = Time.unscaledTime;
                Log("summary mode, outcome=" + session.LastRun.Outcome + " holding=" + RunEndBeat.Holding + " timeScale=" + Time.timeScale.ToString("0.00"));
            }
            if (_deathAt < 0f || _deathShots) return;
            float d = Time.unscaledTime - _deathAt;
            // Итоги встают через 1,2 с после смерти (RunEndBeat) и тлеют медленно (26 сентября): дым — 1,2 с,
            // счёт — с 0,9 с после показа, до ~1,9 с. 06 — дым в пути, 06b — буквы и счёт, 07 — всё встало.
            float[] at = { .35f, 1f, 1.7f, 2.3f, 3.4f };
            string[] names = { "04-death-beat", "05-death-hold", "06-summary", "06b-summary-smolder", "07-summary-counted" };
            if (_deathStep < at.Length && d >= at[_deathStep])
            {
                Log(names[_deathStep] + ": holding=" + RunEndBeat.Holding + " timeScale=" + Time.timeScale.ToString("0.00"));
                Shot(names[_deathStep]);
                if (++_deathStep == at.Length) _deathShots = true;
            }
        }

        /// <summary>
        /// Сценарий маршрута. Возвращает true, когда пора жечь героя (всё снято). Часы шагов — шагом
        /// не больше 0,1 с за кадр: сборка новой арены под дымом длится секунды.
        /// </summary>
        bool Route(RiftRun run, float t)
        {
            if (_firstDepth < 0) _firstDepth = run.Depth;
            float step = Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (run.Phase != _seenPhase) { _seenPhase = run.Phase; _phaseAt = 0f; Log("phase " + run.Phase + " depth=" + run.Depth); }
            else _phaseAt += step;

            if (!_atExit && t >= 5f && run.Phase == RunPhase.SeekingExit && run.Map.ExitCount > 0)
            {
                _atExit = true;
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                Log("hero moved to exit 0");
            }
            if (run.Phase == RunPhase.ChoosingReward)
            {
                if (_routeStep == 0 && _phaseAt >= 1.1f) { Shot("08-reward"); _routeStep = 1; }
                if (_routeStep == 1 && _phaseAt >= 1.6f) { _driver.QueueRunCommand(RunCommand.ChooseReward1); _routeStep = 2; Log("reward 1 taken"); }
            }
            if (run.Phase == RunPhase.ReplacingAbility)
            {
                if (_routeStep < 3 && _phaseAt >= 1.1f) { Shot("08b-replace"); _routeStep = 3; }
                if (_routeStep == 3 && _phaseAt >= 1.6f) { _driver.QueueRunCommand(RunCommand.SalvageAbility); _routeStep = 4; Log("ability salvaged"); }
            }
            if (run.Phase == RunPhase.ChoosingRoute && !_routeChosen)
            {
                if (_routeStep < 5 && _phaseAt >= .5f) { Shot("09a-route"); _routeStep = 5; }
                if (_routeStep == 5 && _phaseAt >= 1.4f) { Shot("09-route"); _routeStep = 6; }
                if (_routeStep == 6 && _phaseAt >= 2f)
                {
                    _routeChosen = true;
                    _routeClock = 0f;
                    _driver.QueueRunCommand(RunCommand.ChooseRoute1);
                    Log("route 1 chosen, smoke=" + CampTransition.Running);
                }
            }
            // Плёнка завесы: от выбора маршрута до 4,5 с (накрыло, сборка, рассеялось).
            if (_routeClock >= 0f && _routeClock <= 4.5f)
            {
                _routeClock += step;
                if (_routeClock >= _smokeNext)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(_directory, "film-smoke-" + _smokeFrame.ToString("000") + ".png"));
                    _smokeFrame++;
                    _smokeNext += FilmStep;
                }
            }
            if (_arenaAt < 0f && run.Depth != _firstDepth && run.Phase == RunPhase.Clearing && !CampTransition.Covering)
            {
                _arenaAt = 0f;
                Log("arena 2 depth=" + run.Depth);
            }
            if (_arenaAt < 0f) return false;
            _arenaAt += step;
            float a = _arenaAt;
            if (_routeStep == 6 && a >= .6f) { Shot("10a-arena2"); _routeStep = 7; }
            if (_routeStep == 7 && a >= 1.4f) { Shot("10-arena2"); _routeStep = 8; }
            if (_routeStep == 8 && a >= 5f) { Pause(true); _routeStep = 9; }
            if (_routeStep == 9 && a >= 5.12f) { Shot("11a-pause"); _routeStep = 10; }
            if (_routeStep == 10 && a >= 5.3f) { Shot("11b-pause"); _routeStep = 11; }
            if (_routeStep == 11 && a >= 5.8f) { Shot("11-pause"); _routeStep = 12; }
            if (_routeStep == 12 && a >= 6.3f) { Pause(false); _routeStep = 13; }
            return _routeStep == 13 && a >= 7f;
        }

        /// <summary>Пауза как по Esc: закрытые методы меню — съёмке можно, игроку это не нужно.</summary>
        void Pause(bool open)
        {
            var menu = FindAnyObjectByType<PauseMenu>();
            if (menu == null) { Log("no pause menu"); return; }
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            if (open)
            {
                var method = typeof(PauseMenu).GetMethod("Open", flags);
                var page = method.GetParameters()[0].ParameterType;
                method.Invoke(menu, new[] { Enum.Parse(page, "Main") });
            }
            else typeof(PauseMenu).GetMethod("Close", flags).Invoke(menu, null);
            Log("pause " + (open ? "open" : "closed") + " isOpen=" + menu.IsOpen);
        }

        /// <summary>
        /// Кадры главного меню от его открытия; на 4 с — мышь на «Новую игру». Часы — шагом не больше
        /// 0,1 с за кадр, как у появления: первые кадры после загрузки длятся секунды.
        /// </summary>
        void Menu()
        {
            if (_menuStart < 0f) { _menuStart = 0f; Log("menu open"); }
            else _menuStart += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float t = _menuStart;
            if (_film)
            {
                if (_hovered == null && t >= FilmHover) Hover();
                if (t >= _filmNext && t <= FilmMenuEnd)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(_directory, "film-menu-" + _filmFrame.ToString("000") + ".png"));
                    _filmFrame++;
                    _filmNext += FilmStep;
                }
                if (t > FilmMenuEnd) _menuDone = true;
                return;
            }
            if (_hovered == null && t >= 4f)
            {
                Hover();
            }
            if (_menuShot >= MenuShots.Length || t < MenuShots[_menuShot].at) return;
            string path = Path.Combine(_directory, "ui-" + MenuShots[_menuShot].name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log("shot " + MenuShots[_menuShot].name + " t=" + t.ToString("0.00"));
            _menuShot++;
            if (_menuShot == MenuShots.Length) _menuDone = true;
        }

        /// <summary>Мышь на первый не главный пункт меню («Новая игра» или «Настройки»).</summary>
        void Hover()
        {
            foreach (MainMenuInkItem item in FindObjectsByType<MainMenuInkItem>())
                if (!item.Featured && item.gameObject.activeInHierarchy) { _hovered = item; break; }
            if (_hovered != null) { _hovered.OnPointerEnter(null); Log("hover " + _hovered.name); }
        }

        void Shot(string name)
        {
            string path = Path.Combine(_directory, "ui-" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            var banner = FindAnyObjectByType<HudLevelBanner>(FindObjectsInactive.Include);
            string lines = string.Empty;
            if (banner != null) foreach (var line in banner.GainLines) lines += "[" + (line != null ? line.text : "") + "]";
            Log("shot " + name + " banner=" + (banner != null && banner.gameObject.activeInHierarchy)
                + " caption=" + (banner != null && banner.Caption != null ? banner.Caption.text : "-")
                + " number=" + (banner != null && banner.Number != null ? banner.Number.text : "-") + " " + lines
                + " phase=" + (_driver.Run != null ? _driver.Run.Phase.ToString() : "-"));
        }

        static void Log(string message) => Debug.Log("[ui-moments] " + message);
    }
}
