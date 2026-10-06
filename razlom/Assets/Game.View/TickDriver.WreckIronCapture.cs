using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Съёмка Крушения «холодное железо» (06.10): -capture-wreck-series N (1–4) — N полных серий
    /// подряд (мах, обратный мах, удар оземь), каждая через 5 с после прошлой (кулдаун серии —
    /// 120 тиков от удара оземь); без ключа — одна серия, как раньше. Пока серий больше одной,
    /// герой между ними не бьёт врага саблей (кадр — только Крушение).
    ///
    /// V6: -capture-wreck-lane — враги (Хранители на стенде -Encounter forest-guardian) стоят вдоль
    /// полосы удара, как в целевом кадре base-v1: у краёв полосы и на её конце, мимо кратера (он не
    /// закрыт телом), один — сбоку от кратера. Стоят смирно (без хода и атак); между сериями
    /// возвращаются на свои места шагом (их сдвигает отброс вала). Только под -razlom-capture.
    /// </summary>
    public sealed partial class TickDriver
    {
        private static int _wreckCaptureSeries = -1;
        private static int _wreckCaptureLane = -1;

        private static int WreckCaptureSeries
        {
            get
            {
                if (_wreckCaptureSeries >= 0) return _wreckCaptureSeries;
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-wreck-series");
                int value = 1;
                if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int parsed)) value = Mathf.Clamp(parsed, 1, 6);
                return _wreckCaptureSeries = value;
            }
        }

        private static string[] _wreckCaptureTempo;

        /// <summary>
        /// Сдвиг нажатия press (0–2) серии series от её начала, тиков. -capture-wreck-tempo fast,fast,fast,pause,turn
        /// (v4 06.10): fast и turn — 0 / 6 / 12 (удары 5 / 11 / 20, нажатия тиком после удара), pause — 0 / 20 / 26 (мах 1,
        /// 15 тиков якорь висит и качается, мах 2 и сразу выпад). Без ключа — через 15 тиков, как раньше.
        /// </summary>
        private static int WreckCapturePressOffset(int press, int series)
        {
            string kind = WreckCaptureTempoKind(series);
            if (kind == null) return press * 15;
            int[] offsets = kind == "pause" ? new[] { 0, 20, 26 } : new[] { 0, 6, 12 };
            return offsets[Mathf.Clamp(press, 0, 2)];
        }

        /// <summary>
        /// Разворот нажатия press серии series относительно направления каста, градусы: у серии turn — вперёд, вбок (90°
        /// влево), назад (180°); у остальных — 0.
        /// </summary>
        private static float WreckCapturePressYaw(int press, int series)
            => WreckCaptureTempoKind(series) == "turn" ? new[] { 0f, -90f, 180f }[Mathf.Clamp(press, 0, 2)] : 0f;

        private static string WreckCaptureTempoKind(int series)
        {
            if (_wreckCaptureTempo == null)
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-wreck-tempo");
                _wreckCaptureTempo = at >= 0 && at + 1 < args.Length ? args[at + 1].Split(',') : new string[0];
            }
            if (_wreckCaptureTempo.Length == 0) return null;
            return _wreckCaptureTempo[Mathf.Min(series, _wreckCaptureTempo.Length - 1)].Trim();
        }

        private static bool WreckCaptureLane
        {
            get
            {
                if (_wreckCaptureLane < 0) _wreckCaptureLane = Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-wreck-lane") >= 0 ? 1 : 0;
                return _wreckCaptureLane == 1;
            }
        }

        /// <summary>
        /// Места врагов: вдоль полосы от героя и поперёк (влево +), м. Полоса — 2,2…6 м, полуширина 0,75: тела
        /// (радиус ~0,5) заходят на край полосы, но не закрывают кратер и звенья: камера смотрит с юга, тело
        /// с южной (−) стороны тянется на экране вверх через полосу — поэтому южные стоят дальше и у конца.
        /// </summary>
        private static readonly Vector2[] WreckLaneSpots =
        {
            new Vector2(3.9f, 1.15f), new Vector2(5.2f, -1.25f), new Vector2(6.3f, 1f), new Vector2(1.3f, 2.2f),
            new Vector2(7f, -1.6f), new Vector2(7.4f, 1.6f)
        };

        private FixVec2 _wreckLaneFace;

        private FixVec2 WreckLaneHome(int enemy)
        {
            Vector2 spot = WreckLaneSpots[(enemy - 1) % WreckLaneSpots.Length];
            var side = new FixVec2(-_wreckLaneFace.Y, _wreckLaneFace.X);
            return Sim.Entities.Position[Simulation.PlayerId] + _wreckLaneFace * Fix64.FromDouble(spot.x) + side * Fix64.FromDouble(spot.y);
        }

        /// <summary>Расстановка в начале съёмки (TickDriver, elapsed 0, после штатной расстановки живого навыка).</summary>
        private void PlaceWreckCaptureLane(FixVec2 face)
        {
            if (!WreckCaptureLane || Sim == null) return;
            _wreckLaneFace = face;
            EntityStore entities = Sim.Entities;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                entities.Position[i] = WreckLaneHome(i);
                entities.Facing[i] = (entities.Position[Simulation.PlayerId] - entities.Position[i]).Normalized();
                entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                entities.RefreshStats(i);
                entities.NextAttackTick[i] = int.MaxValue;
            }
            Sim.Grid.Rebuild(entities);
            Debug.Log($"[wreck-capture] lane spots for {entities.Count - 1} enemies");
        }

        /// <summary>Между сериями (после оседания кусков, до первого маха) — шагом 2 м/с на свои места.</summary>
        private void HoldWreckCaptureLane(int elapsed)
        {
            if (!WreckCaptureLane || Sim == null || _wreckLaneFace.LengthSq == Fix64.Zero) return;
            int phase = (elapsed - 18) % 150;
            if (elapsed < 18 || phase < 105 || phase > 146) return;
            EntityStore entities = Sim.Entities;
            Fix64 step = Fix64.Ratio(2, 30);
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                FixVec2 to = WreckLaneHome(i) - entities.Position[i];
                Fix64 length = to.Length;
                if (length <= Fix64.Ratio(1, 100)) continue;
                entities.Position[i] += length > step ? to * (step / length) : to;
                entities.Facing[i] = (entities.Position[Simulation.PlayerId] - entities.Position[i]).Normalized();
            }
            Sim.Grid.Rebuild(entities);
        }
    }
}
