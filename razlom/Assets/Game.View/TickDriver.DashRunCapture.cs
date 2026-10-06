using System.Globalization;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ТОЛЬКО СЪЁМКА: стык рывок → бег (06.10). Ключ -capture-dash-run к обходу рывка
    /// (-capture-enemy-case dash, TickDriver.DashCapture.cs) заменяет восьмиугольник
    /// серией случаев с одной точки поляны Хранителей, рывок всегда вправо по экрану:
    ///   still-stop — рывок с места, приказа нет;   still-fwd — с места, ПКМ зажата по ходу рывка;
    ///   run-fwd / run-right / run-back / run-left — разбег 1 с, рывок по ходу бега, затем ПКМ
    ///                зажата вперёд / вправо / назад / влево от рывка 1,5 с;
    ///   run-stop  — разбег, рывок, точка приказа — конец рывка: герой встаёт.
    /// Отпускание — клик под ноги (приказ гаснет), стойка, телепорт на старт следующего случая.
    /// Каждый кадр — строка [dash-run-probe] (TickDriver.DashRunProbe.cs).
    ///   capture.ps1 -Encounter forest-guardian -Enemies 1 -Video -VideoStart 0 -VideoDuration 44
    ///     -ExtraArgs '-capture-enemy-case','dash','-capture-dash-run'
    /// </summary>
    public sealed partial class TickDriver
    {
        private readonly struct DashRunCase
        {
            public readonly string Label;
            public readonly bool RunUp, Hold, StopAtEnd;
            public readonly float HoldAngle;
            public DashRunCase(string label, bool runUp, bool hold, float holdAngle, bool stopAtEnd = false)
            { Label = label; RunUp = runUp; Hold = hold; HoldAngle = holdAngle; StopAtEnd = stopAtEnd; }
        }

        // Угол удержания — от направления рывка, против часовой сверху: +90 — влево по ходу.
        private static readonly DashRunCase[] DashRunCases =
        {
            new DashRunCase("still-stop", false, false, 0f),
            new DashRunCase("run-fwd", true, true, 0f),
            new DashRunCase("run-right", true, true, -90f),
            new DashRunCase("run-back", true, true, 180f),
            new DashRunCase("run-left", true, true, 90f),
            new DashRunCase("run-stop", true, false, 0f, stopAtEnd: true),
            new DashRunCase("still-fwd", false, true, 0f),
        };

        private const int DashRunSettleTicks = 40, DashRunUpTicks = 30, DashRunHoldTicks = 45, DashRunRestTicks = 24;
        private const float DashRunAimReach = 8f, DashRunRange = 4f;

        private static int _dashRunFlag = -1;
        /// <summary>Ключ -capture-dash-run в командной строке плеера съёмки.</summary>
        private static bool DashRunRequested
        {
            get
            {
                if (_dashRunFlag < 0)
                    _dashRunFlag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-dash-run") >= 0 ? 1 : 0;
                return _dashRunFlag == 1;
            }
        }

        private int _dashRunGeneration = -1, _dashRunCase, _dashRunStage, _dashRunStageTick;
        private int _dashRunPressTick = -1, _dashRunLatchTick = -1, _dashRunReleaseTick = -1;
        private FixVec2 _dashRunReleaseAt;
        private byte _dashRunLatchFlags;
        private bool _dashRunReady;
        private Vector2 _dashRunStart, _dashRunDir, _dashRunLatchAim;

        private string DashRunLabel => _dashRunReady && _dashRunCase < DashRunCases.Length ? DashRunCases[_dashRunCase].Label : "-";

        private void CaptureDashRunTour(int tick)
        {
            var entities = Sim.Entities;
            if (_dashRunGeneration != Generation)
            {
                _dashRunGeneration = Generation;
                _dashRunReady = SetupDashRun();
                _dashRunCase = 0; _dashRunStage = 0; _dashRunStageTick = tick;
                _dashRunPressTick = _dashRunLatchTick = _dashRunReleaseTick = -1;
                if (_dashRunReady) { DashRunTeleport(); StartCoroutine(DashRunProbe(Generation)); }
            }
            if (!_dashRunReady || _dashRunCase >= DashRunCases.Length) return;
            // Нажатие держится на всех кадрах своего тика (защёлку сбрасывают каждый кадр).
            if (tick == _dashRunLatchTick)
            {
                _pending.Aim = DashTourFix(_dashRunLatchAim); _pending.Flags = _dashRunLatchFlags;
                _abilityLatch = (byte)(1 << PelagKit.DashSlot);
                return;
            }
            DashRunCase c = DashRunCases[_dashRunCase];
            Vector2 hero = DashTourFlat(entities.Position[Simulation.PlayerId]);
            int since = tick - _dashRunStageTick;
            switch (_dashRunStage)
            {
                case 0:
                    // Стойка после телепорта, рывок готов.
                    if (since < DashRunSettleTicks || Sim.AbilityReadyTick(PelagKit.DashSlot) > tick || Sim.PelagDash.Moving) return;
                    DashRunStage(c.RunUp ? 1 : 2, tick);
                    return;
                case 1:
                    // Разбег по направлению рывка: ПКМ зажата, курсор впереди.
                    MoveCaptured(DashTourFix(hero + _dashRunDir * DashRunAimReach));
                    if (since >= DashRunUpTicks) DashRunStage(2, tick);
                    return;
                case 2:
                {
                    Vector2 aim = hero + _dashRunDir * DashRunAimReach;
                    byte flags = c.RunUp || c.Hold ? (byte)InputFlags.MoveOrder : (byte)0;
                    if (c.StopAtEnd)
                    {
                        // Точка приказа — конец рывка: шаг тика нажатия (штраф каста 3/4) и дальность.
                        aim = hero + _dashRunDir * (DashRunRange + entities.MoveStep[Simulation.PlayerId].ToFloat() * .75f);
                        flags = (byte)InputFlags.MoveOrder;
                    }
                    entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(entities.MaxLavidium[Simulation.PlayerId]);
                    _dashRunLatchTick = tick; _dashRunLatchAim = aim; _dashRunLatchFlags = flags;
                    _pending.Aim = DashTourFix(aim); _pending.Flags = flags;
                    _abilityLatch = (byte)(1 << PelagKit.DashSlot);
                    _dashRunPressTick = tick;
                    Debug.Log($"[dash-run] press {c.Label} tick={tick} t={Time.time.ToString("F3", CultureInfo.InvariantCulture)}"
                        + $" hero={hero.ToString("F2")} aim={aim.ToString("F2")} dir={_dashRunDir.ToString("F2")}"
                        + $" hold={(c.Hold ? c.HoldAngle.ToString("F0", CultureInfo.InvariantCulture) : "-")} flags={flags}");
                    DashRunStage(3, tick);
                    return;
                }
                case 3:
                    if (c.Hold && since < DashRunHoldTicks)
                    {
                        // ПКМ зажата: курсор в стороне случая от тела (камера идёт за героем).
                        MoveCaptured(DashTourFix(hero + DashTourRotate(_dashRunDir, c.HoldAngle) * DashRunAimReach));
                        return;
                    }
                    if (since < (c.Hold ? DashRunHoldTicks : 14)) return;
                    // Отпускание: клик под ноги — приказ гаснет, тело тормозит (держится весь тик, как нажатие).
                    _dashRunReleaseTick = c.Hold ? tick : -1; _dashRunReleaseAt = entities.Position[Simulation.PlayerId];
                    if (c.Hold) MoveCaptured(_dashRunReleaseAt);
                    Debug.Log($"[dash-run] release {c.Label} tick={tick} hero={hero.ToString("F2")}");
                    DashRunStage(4, tick);
                    return;
                case 4:
                    if (tick == _dashRunReleaseTick) { MoveCaptured(_dashRunReleaseAt); return; }
                    if (since < DashRunRestTicks || Sim.PelagDash.Moving
                        || entities.Velocity[Simulation.PlayerId].LengthSq.Raw != 0) return;
                    _dashRunCase++;
                    if (_dashRunCase >= DashRunCases.Length)
                    {
                        Debug.Log($"[dash-run] done tick={tick}");
                        return;
                    }
                    DashRunTeleport();
                    DashRunStage(0, tick);
                    return;
            }
        }

        private void DashRunStage(int stage, int tick) { _dashRunStage = stage; _dashRunStageTick = tick; }

        private void DashRunTeleport()
        {
            DashTourTeleport(_dashRunStart, _dashRunDir, "dash-run " + DashRunLabel);
            Sim.Entities.Velocity[Simulation.PlayerId] = FixVec2.Zero;
        }

        /// <summary>
        /// Старт и направление: рывок вправо по экрану, старт — так, чтобы разбег, рывок и бег
        /// в любую из четырёх сторон шли по проходимой земле карты. Враги заморожены за спиной.
        /// </summary>
        private bool SetupDashRun()
        {
            var sim = Sim; var entities = sim.Entities; var map = Run != null ? Run.Map : null;
            if (sim.GetAbility(PelagKit.DashSlot)?.DefinitionId != AbilityDefinition.DashId)
                sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), System.Array.Empty<AbilityNode>(), 0);
            Camera cam = Camera.main;
            Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up) : Vector3.right;
            _dashRunDir = right.sqrMagnitude > .01f ? new Vector2(right.x, right.z).normalized : Vector2.right;
            Vector2 d = _dashRunDir, side = new Vector2(-d.y, d.x);
            Fix64 radius = entities.BodyRadius[Simulation.PlayerId];
            bool Travel(Vector2 a, Vector2 b) => map == null || map.CanTravel(DashTourFix(a), DashTourFix(b), radius);
            bool Fits(Vector2 s) => Travel(s, s + d * 19f) && Travel(s + d * 11f, s + d * 11f + side * 8f)
                && Travel(s + d * 11f, s + d * 11f - side * 8f) && Travel(s + d * 11f, s + d * 2f);
            Vector2 spawn = DashTourFlat(entities.Position[Simulation.PlayerId]);
            Vector2 start = spawn - d * 8f;
            if (!Fits(start))
            {
                float best = float.MaxValue; bool found = false;
                for (int x = -20; x <= 20; x++)
                    for (int y = -20; y <= 20; y++)
                    {
                        Vector2 candidate = spawn + new Vector2(x, y);
                        float distance = (candidate - start).sqrMagnitude;
                        if (distance >= best || !Fits(candidate)) continue;
                        best = distance; start = candidate; found = true;
                    }
                if (!found) { Debug.LogWarning("[dash-run] нет места для разбега"); return false; }
            }
            _dashRunStart = start;
            int frozen = 0;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                entities.Position[i] = DashTourFix(start - d * (14f + 2f * frozen) + side * 3f);
                entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                entities.RefreshStats(i);
                entities.NextAttackTick[i] = int.MaxValue;
                frozen++;
            }
            sim.Grid.Rebuild(entities);
            Debug.Log($"[dash-run] layout start={start.ToString("F2")} dir={d.ToString("F2")} spawn={spawn.ToString("F2")}"
                + $" map={(map != null)} frozen={frozen} cases={DashRunCases.Length}");
            return true;
        }
    }
}
