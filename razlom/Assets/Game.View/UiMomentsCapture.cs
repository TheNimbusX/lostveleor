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
    /// </summary>
    internal sealed class UiMomentsCapture : MonoBehaviour
    {
        static readonly (float at, string name)[] Shots =
        {
            (.4f, "01a-arena-intro"), (.8f, "01-arena-intro"), (1.3f, "01b-arena-intro"), (2.2f, "02-rift-map"), (3.1f, "02b-damage-numbers"), (4.3f, "03-arena-cleared"),
        };
        const float ClearAt = 3.4f, DieAt = 7.5f;

        TickDriver _driver;
        string _directory;
        float _riftAt = -1f, _deathAt = -1f, _clock;
        int _shot;
        bool _singed, _cleared, _killed, _deathShots;
        int _deathStep;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-razlom-capture") < 0 || Array.IndexOf(args, "-capture-ui-moments") < 0) return;
            var capture = new GameObject("UI moments capture").AddComponent<UiMomentsCapture>();
            int output = Array.IndexOf(args, "-capture-out");
            capture._directory = output >= 0 && output + 1 < args.Length ? args[output + 1] : Application.temporaryCachePath;
        }

        void Update()
        {
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
            if (_shot < Shots.Length && t >= Shots[_shot].at) { Shot(Shots[_shot].name); _shot++; }

            RiftRun run = _driver.Run;
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
            if (!_killed && t >= DieAt && session.Mode == GameMode.Rift && run != null)
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
            float[] at = { .35f, 1f, 1.7f, 2.6f };
            string[] names = { "04-death-beat", "05-death-hold", "06-summary", "07-summary-counted" };
            if (_deathStep < at.Length && d >= at[_deathStep])
            {
                Log(names[_deathStep] + ": holding=" + RunEndBeat.Holding + " timeScale=" + Time.timeScale.ToString("0.00"));
                Shot(names[_deathStep]);
                if (++_deathStep == at.Length) _deathShots = true;
            }
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
