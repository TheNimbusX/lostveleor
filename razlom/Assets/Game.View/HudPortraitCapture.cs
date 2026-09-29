using System;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Проверка живого портрета боевого HUD в собранном плеере (capture.ps1 -Hud -ExtraArgs
    /// '-capture-portrait-moments'), не трогая редактор: покой (дыхание), удар, лечение, низкое
    /// здоровье (тревога), снова лечение и новый уровень — по очереди, с кадрами сразу после каждого
    /// события (ui-p*.png) и строкой [portrait-moments] в журнале плеера. Здоровье и опыт меняются
    /// напрямую, мимо боя: враги в обычной съёмке стоят, а зелье и уровень в коротком прогоне сами не
    /// случаются. Только под -razlom-capture; сохранение под съёмкой выключено (CampSaveStore).
    /// </summary>
    internal sealed class HudPortraitCapture : MonoBehaviour
    {
        enum Beat { Hit, Heal, Low, LevelUp }

        // Время — по часам интерфейса от входа в Разлом (шаг не больше 0,1 с, как у UiMomentsCapture).
        static readonly (float at, Beat beat)[] Beats =
        {
            (4.5f, Beat.Hit), (6.5f, Beat.Heal), (8.5f, Beat.Low), (11f, Beat.Heal), (13f, Beat.LevelUp),
        };
        static readonly (float at, string name)[] Idle = { (3f, "p0-idle-a"), (3.9f, "p0-idle-b") };
        // Кадры после события: 1–2 — красный тон удара, дальше подъём и спад света.
        static readonly int[] After = { 1, 2, 4, 10, 25 };

        TickDriver _driver;
        string _directory;
        float _riftAt = -1f, _clock;
        int _beat, _idle, _eventFrame = -1, _after;
        string _eventName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-razlom-capture") < 0 || Array.IndexOf(args, "-capture-portrait-moments") < 0) return;
            var capture = new GameObject("Portrait moments capture").AddComponent<HudPortraitCapture>();
            int output = Array.IndexOf(args, "-capture-out");
            capture._directory = output >= 0 && output + 1 < args.Length ? args[output + 1] : Application.temporaryCachePath;
        }

        void Update()
        {
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            GameSession session = _driver != null ? _driver.Session : null;
            RiftRun run = _driver != null ? _driver.Run : null;
            if (session == null || session.Mode != GameMode.Rift || run == null) return;
            if (_riftAt < 0f)
            {
                _riftAt = Time.unscaledTime;
                Log("rift at frame " + Time.frameCount);
            }
            _clock += Mathf.Min(Time.unscaledDeltaTime, .1f);

            if (_eventFrame >= 0 && _after < After.Length && Time.frameCount - _eventFrame >= After[_after])
            {
                Shot(_eventName + "-f" + After[_after].ToString("00"));
                _after++;
            }
            if (_idle < Idle.Length && _clock >= Idle[_idle].at)
            {
                Shot(Idle[_idle].name);
                _idle++;
            }
            if (_beat >= Beats.Length || _clock < Beats[_beat].at) return;
            Apply(Beats[_beat].beat, run.Sim.Entities, session.Camp);
            _eventName = "p" + (_beat + 1) + "-" + Beats[_beat].beat.ToString().ToLowerInvariant();
            _eventFrame = Time.frameCount;
            _after = 0;
            _beat++;
        }

        void Apply(Beat beat, EntityStore entities, Camp camp)
        {
            int id = Simulation.PlayerId, max = entities.MaxHealth[id];
            switch (beat)
            {
                case Beat.Hit: entities.Health[id] = Mathf.Max(1, entities.Health[id] - 45); break;
                case Beat.Heal: entities.Health[id] = max; break;
                case Beat.Low: entities.Health[id] = Mathf.Max(1, max / 5); break;
                case Beat.LevelUp:
                    if (camp != null) camp.GainExperience(camp.ExperienceToNextLevel - camp.Experience);
                    break;
            }
            Log(beat + " t=" + _clock.ToString("0.00") + " frame=" + Time.frameCount + " hp=" + entities.Health[id] + "/" + max
                + (camp != null ? " level=" + camp.Level + " xp=" + camp.Experience + "/" + camp.ExperienceToNextLevel : ""));
        }

        void Shot(string name)
        {
            FrameCost.Note("снимок");
            ScreenCapture.CaptureScreenshot(Path.Combine(_directory, "ui-" + name + ".png"));
            Log("shot " + name + " t=" + _clock.ToString("0.00") + " frame=" + Time.frameCount);
        }

        static void Log(string message) => Debug.Log("[portrait-moments] " + message);
    }
}
