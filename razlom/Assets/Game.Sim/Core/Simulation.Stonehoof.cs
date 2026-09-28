using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum StonehoofPhase : byte { None, Windup, Charge, Brake, WallImpact }

    /// <summary>
    /// Чем кончается таран. ArenaEdge — открытая остановка с торможением: край
    /// поляны или конец задуманного пути (герой плюс StonehoofChargeOvershoot).
    /// Obstacle — удар о ствол или камень и оглушение.
    /// </summary>
    public enum StonehoofStop : byte { ArenaEdge, Obstacle }

    public struct StonehoofActionState
    {
        public int Serial, StartTick, LaunchTick, BrakeTick, StopTick, EndTick;
        public StonehoofPhase Phase;
        public StonehoofStop StopReason;
        public FixVec2 Origin, Direction, Target, PreviousPosition;
        public Fix64 Distance, BrakeDistance;
        public bool HitResolved;
    }

    /// <summary>
    /// КАМНЕКОПЫТ, ИИ v2 (27.09, «самый глупый — постоянно где-то застревает»).
    ///
    /// Что было не так: шагал, только глядя почти ровно на цель, и перед
    /// деревом вставал намертво; ближе 4 м к герою разворачивался спиной и
    /// уходил; таран летел до края поляны без проверки, что полоса чиста, и
    /// бился о тот же камень; за единственный крупный жетон спорил со всеми и
    /// крутился на месте.
    ///
    /// Теперь: ходит по полю пути (Simulation.EnemyBrain) и скользит вдоль
    /// препятствий, шагая уже при доводе до ~53°. Перед разгоном проверяет
    /// полосу: она обязана тянуться дальше героя на StonehoofLaneMargin. Нет
    /// такой — заходит на точку кольца вокруг героя, откуда полоса чиста.
    /// Таран кончается в пяти метрах за героем, а не у края поляны. На отдыхе
    /// герой ближе 3 м — пятится, глядя на него, до 5 м; разворота спиной нет.
    /// Толпа двигает его вдвое слабее прежнего.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int StonehoofWindupTicks = 30, StonehoofAccelerationTicks = 6,
            StonehoofBrakeTicks = 18, StonehoofWallTicks = 36, StonehoofRestTicks = 120;
        public static readonly Fix64 StonehoofRadius = EnemyArchetypes.StonehoofBodyRadius;
        private static readonly Fix64 StonehoofStep = Fix64.Ratio(12, TicksPerSecond);

        /// <summary>
        /// Разворот Камнекопыта — свой, вдвое медленнее прочих мобов: 6° за
        /// тик, полный оборот за две секунды. Тяжёлый кабан переступает на
        /// месте, а не крутится юлой; и именно под этот темп рисуются клипы
        /// TurnLeft/TurnRight (фаза клипа — накопленный поворот / 90°).
        /// </summary>
        public const int StonehoofTurnDegreesPerTick = 6;
        private static readonly Fix64 StonehoofTurnStep = Fix64.TwoPi / (360 / StonehoofTurnDegreesPerTick);
        private static readonly Fix64 StonehoofTurnStepCos = Fix64.Cos(StonehoofTurnStep);
        private static readonly Fix64 StonehoofTurnStepSin = Fix64.Sin(StonehoofTurnStep);

        /// <summary>
        /// С какого довода корпус уже шагает: cos 0,6 (≈53°). Скорость растёт с
        /// доводом и полная при точном совпадении — поворот и шаг вместе дают
        /// дугу, а не «стоит и крутится, потом идёт». Боком не ходит никогда:
        /// шаг только вдоль взгляда.
        /// </summary>
        public static readonly Fix64 StonehoofWalkAlignCos = Fix64.Ratio(6, 10);

        /// <summary>Дальность, с которой готовый к тарану кабан целится, а не идёт.</summary>
        public static readonly Fix64 StonehoofEngageRange = Fix64.FromInt(7);

        /// <summary>Таран кончается на столько метров дальше героя (и не длиннее StonehoofChargeMax).</summary>
        public static readonly Fix64 StonehoofChargeOvershoot = Fix64.FromInt(5);
        public static readonly Fix64 StonehoofChargeMax = Fix64.FromInt(14);

        /// <summary>Полоса тарана обязана быть чистой дальше героя на столько и не короче StonehoofLaneMin.</summary>
        public static readonly Fix64 StonehoofLaneMargin = Fix64.Ratio(3, 2);
        public static readonly Fix64 StonehoofLaneMin = Fix64.FromInt(3);

        /// <summary>Кольцо заходов вокруг героя, откуда полоса чиста: радиус и число точек.</summary>
        public static readonly Fix64 StonehoofRingRadius = Fix64.Ratio(11, 2);
        public const int StonehoofRingPoints = 10;

        /// <summary>Заход на точку — не дольше; потом полоса нужна хоть бы до героя.</summary>
        public const int StonehoofRepositionTicks = 90;

        /// <summary>Отдых: ближе 3 м начинает пятиться, дальше 5 м перестаёт; пятится 1,5 м/с.</summary>
        public static readonly Fix64 StonehoofBackoffStart = Fix64.FromInt(3), StonehoofBackoffStop = Fix64.FromInt(5);
        private static readonly Fix64 StonehoofBackoffStep = Fix64.Ratio(3, 2 * TicksPerSecond);

        /// <summary>Целится дольше этого — таран уже при доводе до 15°: кружащего героя иначе не поймать.</summary>
        public const int StonehoofAimRelaxTicks = 45;
        private static readonly Fix64 StonehoofAimRelaxCos = Fix64.Ratio(96593, 100000);

        /// <summary>Как часто пересчитывается чистота полосы к герою (со сдвигом по индексу).</summary>
        private const int StonehoofLaneProbeTicks = 10;
        private static readonly Fix64 StonehoofLaneProbeStep = Fix64.Ratio(1, 4);

        private readonly StonehoofActionState[] _stonehoofActions;
        private readonly int[] _stonehoofArena;
        private int _stonehoofSerial;
        private FixVec2 _stonehoofHeroBeforeMove;

        // ИИ v2: полоса, заход, прицел, отход. Заводятся при первой расстановке.
        private int[] _stonehoofLaneTick, _stonehoofAimSince, _stonehoofRepositionSince, _stonehoofPointUntil;
        private bool[] _stonehoofLaneOk, _stonehoofBackoff;
        private FixVec2[] _stonehoofPoint;

        private void EnsureStonehoofBrain()
        {
            if (_stonehoofLaneTick != null && _stonehoofLaneTick.Length == Entities.Capacity) return;
            int n = Entities.Capacity;
            _stonehoofLaneTick = new int[n]; _stonehoofAimSince = new int[n];
            _stonehoofRepositionSince = new int[n]; _stonehoofPointUntil = new int[n];
            _stonehoofLaneOk = new bool[n]; _stonehoofBackoff = new bool[n];
            _stonehoofPoint = new FixVec2[n];
        }

        public bool TryGetStonehoofAction(int id, out StonehoofActionState action)
        {
            action = (uint)id < (uint)_stonehoofActions.Length ? _stonehoofActions[id] : default;
            return action.Serial != 0;
        }

        private bool StonehoofOwnsPosition(int id) => (uint)id < (uint)Entities.Count
            && Entities.Kind[id] == EnemyKind.ForestStonehoof && _stonehoofActions[id].Serial != 0;

        private void ResetStonehoof()
        {
            Array.Clear(_stonehoofActions, 0, _stonehoofActions.Length);
            for (int i = 0; i < _stonehoofArena.Length; i++) _stonehoofArena[i] = -1;
            _stonehoofSerial = 0;
            EnsureStonehoofBrain();
            Array.Clear(_stonehoofLaneTick, 0, _stonehoofLaneTick.Length);
            Array.Clear(_stonehoofLaneOk, 0, _stonehoofLaneOk.Length);
            Array.Clear(_stonehoofBackoff, 0, _stonehoofBackoff.Length);
            Array.Clear(_stonehoofPoint, 0, _stonehoofPoint.Length);
            Array.Clear(_stonehoofPointUntil, 0, _stonehoofPointUntil.Length);
            for (int i = 0; i < _stonehoofAimSince.Length; i++) { _stonehoofAimSince[i] = -1; _stonehoofRepositionSince[i] = -1; }
        }

        private void ConfigureStonehoof(int id)
        {
            Entities.BodyRadius[id] = StonehoofRadius;
            // Вдвое тяжелее для толпы, чем Хранитель: рой не таскает кабана по поляне.
            Entities.PushWeight[id] = Fix64.Ratio(1, 2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, Fix64.Ratio(5, 2));
            // Урон тарана — строка таблицы видов; отброс идёт сверху, без урона.
            s.SetBase(StatType.Damage, Fix64.FromInt(EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BaseDamage));
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            _stonehoofArena[id] = FindStonehoofArena(Entities.Position[id]);
            EnsureStonehoofBrain();
            _stonehoofLaneTick[id] = 0; _stonehoofLaneOk[id] = false; _stonehoofBackoff[id] = false;
            _stonehoofAimSince[id] = -1; _stonehoofRepositionSince[id] = -1; _stonehoofPointUntil[id] = 0;
            _stonehoofPoint[id] = Entities.Position[id];
        }

        // Non-negative indices identify a clearing; negative ones identify a placed room.
        private int FindStonehoofArena(FixVec2 point)
        {
            if (_layout == null) return -1;
            for (int i = 0; i < _layout.GladeCount; i++)
                if (_layout.GetGlade(i).Field(point) <= Fix64.One) return i;
            for (int i = 0; i < _layout.PlacedCount; i++)
                if (_layout.ContainsWorld(i, point)) return -2 - i;
            return -1;
        }

        /// <summary>
        /// Помещается ли тело в поляну кабана. Пробы — те же, что у
        /// LayoutMap.IsWalkable (диагонали на 0,7 радиуса): прежние диагонали
        /// на полный радиус были строже пола, и кабан у края замирал там, где
        /// пол есть. Поляны нет (карта без полян, тело вне всех) — просто пол.
        /// Без карты — стенд 24 × 24 м.
        /// </summary>
        private bool InsideStonehoofArena(int id, FixVec2 point)
        {
            var radius = Entities.BodyRadius[id];
            if (_layout == null)
                return Fix64.Abs(point.X) <= Fix64.FromInt(12) - radius
                    && Fix64.Abs(point.Y) <= Fix64.FromInt(12) - radius;
            int arena = _stonehoofArena[id];
            if (arena == -1) return _layout.IsWalkable(point, radius);
            var diagonal = radius * Fix64.Ratio(7, 10);
            for (int k = 0; k < 9; k++)
            {
                var r = k >= 5 ? diagonal : radius;
                var offset = k == 0 ? FixVec2.Zero : new FixVec2(
                    k == 1 || k == 5 || k == 8 ? r : k == 2 || k == 6 || k == 7 ? -r : Fix64.Zero,
                    k == 3 || k == 5 || k == 6 ? r : k == 4 || k == 7 || k == 8 ? -r : Fix64.Zero);
                var p = point + offset;
                if (arena >= 0 ? _layout.GetGlade(arena).Field(p) > Fix64.One : !_layout.ContainsWorld(-2 - arena, p)) return false;
            }
            return true;
        }

        private bool StonehoofBlocked(int id, FixVec2 from, FixVec2 to)
            => _layout != null && !_layout.CanTravel(from, to, Entities.BodyRadius[id]);

        /// <summary>
        /// Где кончится таран из origin вдоль direction не дальше maxDistance:
        /// у первого препятствия (Obstacle), у края поляны или в конце пути
        /// (ArenaEdge — оба тормозят юзом). Пробы по 5 см и деление пополам у края.
        /// </summary>
        private FixVec2 StonehoofEndpoint(int id, FixVec2 origin, FixVec2 direction, Fix64 maxDistance, out StonehoofStop reason)
        {
            var previous = origin; reason = StonehoofStop.ArenaEdge;
            int samples = Math.Max(1, (maxDistance * 20).ToInt());
            for (int k = 1; k <= samples; k++)
            {
                var candidate = k == samples ? origin + direction * maxDistance : origin + direction * Fix64.Ratio(k, 20);
                bool edge = !InsideStonehoofArena(id, candidate);
                bool wall = StonehoofBlocked(id, previous, candidate);
                if (edge || wall)
                {
                    reason = !edge && wall ? StonehoofStop.Obstacle : StonehoofStop.ArenaEdge;
                    var low = previous; var high = candidate;
                    for (int j = 0; j < 10; j++)
                    {
                        var middle = (low + high) * Fix64.Ratio(1, 2);
                        if (InsideStonehoofArena(id, middle) && !StonehoofBlocked(id, low, middle)) low = middle;
                        else high = middle;
                    }
                    return low;
                }
                previous = candidate;
            }
            return previous;
        }

        /// <summary>
        /// Длина чистой полосы тарана из origin вдоль direction, не больше
        /// maxDistance: грубо, шагом 25 см. Для решений «где встать»; сам таран
        /// меряет точно (StonehoofEndpoint).
        /// </summary>
        private Fix64 StonehoofLaneLength(int id, FixVec2 origin, FixVec2 direction, Fix64 maxDistance)
        {
            if (!InsideStonehoofArena(id, origin)) return Fix64.Zero;
            var clear = Fix64.Zero;
            var previous = origin;
            while (clear < maxDistance)
            {
                var next = clear + StonehoofLaneProbeStep;
                if (next > maxDistance) next = maxDistance;
                var point = origin + direction * next;
                if (!InsideStonehoofArena(id, point) || StonehoofBlocked(id, previous, point)) return clear;
                clear = next; previous = point;
            }
            return clear;
        }

        /// <summary>Сколько полосы нужно для тарана по герою на расстоянии distance.</summary>
        private Fix64 StonehoofLaneNeed(int id, Fix64 distance)
        {
            // Заход затянулся — хватит полосы хотя бы до героя: лучше короткий
            // честный таран, чем вечный обход вокруг.
            bool relaxed = _stonehoofRepositionSince[id] >= 0 && Tick - _stonehoofRepositionSince[id] >= StonehoofRepositionTicks;
            return Fix64.Max(StonehoofLaneMin, distance + (relaxed ? Fix64.Zero : StonehoofLaneMargin));
        }

        private static Fix64 StonehoofTravel(int age)
        {
            if (age <= 0) return Fix64.Zero;
            if (age <= StonehoofAccelerationTicks) return Fix64.Ratio(age * age, 30);
            return Fix64.Ratio(6, 5) + StonehoofStep * (age - StonehoofAccelerationTicks);
        }

        /// <summary>
        /// Таран по direction. false — полоса короче нужного (StonehoofLaneNeed):
        /// кабан не бьётся в камень, а идёт на другую точку.
        /// </summary>
        private bool StartStonehoof(int id, FixVec2 direction)
        {
            var origin = Entities.Position[id];
            var heroDistance = (Entities.Position[PlayerId] - origin).Length;
            var planned = Fix64.Min(heroDistance + StonehoofChargeOvershoot, StonehoofChargeMax);
            var target = StonehoofEndpoint(id, origin, direction, planned, out var reason);
            var distance = (target - origin).Length;
            if (distance < StonehoofLaneNeed(id, heroDistance))
            { _stonehoofLaneOk[id] = false; _stonehoofLaneTick[id] = Tick; return false; }
            // A nearby edge still gets the full warning; use a slower short launch/brake.
            var brake = reason == StonehoofStop.ArenaEdge ? Fix64.Min(Fix64.Ratio(18, 5), distance * Fix64.Ratio(3, 4)) : Fix64.Zero;
            var cruise = distance - brake; int travelTicks = 0;
            while (StonehoofTravel(travelTicks) < cruise && travelTicks < 600) travelTicks++;
            int brakeTick = Tick + StonehoofWindupTicks + travelTicks;
            int stopTick = brakeTick + (reason == StonehoofStop.ArenaEdge ? StonehoofBrakeTicks : 0);
            _stonehoofActions[id] = new StonehoofActionState {
                Serial = ++_stonehoofSerial, Phase = StonehoofPhase.Windup, StartTick = Tick,
                LaunchTick = Tick + StonehoofWindupTicks, BrakeTick = brakeTick, StopTick = stopTick,
                EndTick = stopTick + (reason == StonehoofStop.Obstacle ? StonehoofWallTicks : 0),
                Origin = origin, Target = target, Direction = direction, PreviousPosition = origin,
                Distance = distance, BrakeDistance = brake, StopReason = reason
            };
            Entities.Facing[id] = direction; Entities.Velocity[id] = FixVec2.Zero;
            Entities.NextAttackTick[id] = stopTick + StonehoofRestTicks;
            _stonehoofAimSince[id] = -1; _stonehoofRepositionSince[id] = -1; _stonehoofBackoff[id] = false;
            _events.Add(new SimEvent(SimEventType.StonehoofStarted, id, PlayerId, _stonehoofSerial, false, origin));
            // Полоса тарана в общем списке без SharedView: рисует её пока свой вид.
            // Удар считается прежней протяжкой тела; полоса — её след шириной в тело.
            OpenTelegraph(id, EnemyTelegraph.Lane(origin, direction, distance, StonehoofRadius * 2),
                Tick + StonehoofWindupTicks, stopTick + TelegraphLingerTicks, TelegraphFlags.None);
            return true;
        }

        private void CancelInvalidStonehooves()
        {
            _stonehoofHeroBeforeMove = Entities.Position[PlayerId];
            for (int id = 1; id < Entities.Count; id++)
            {
                var a = _stonehoofActions[id]; if (a.Serial == 0) continue;
                bool externalStun = Statuses.IsStunned(id, Tick)
                    && (a.Phase != StonehoofPhase.WallImpact || Statuses.StunUntilTick[id] > a.EndTick);
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || externalStun || ForcedMotion.IsActive(Entities, id)) CancelStonehoof(id);
            }
        }

        private void CancelStonehoof(int id)
        {
            var a = _stonehoofActions[id]; if (a.Serial == 0) return;
            _stonehoofActions[id] = default;
            CancelTelegraphsOf(id);
            Entities.NextAttackTick[id] = Math.Max(Entities.NextAttackTick[id], Tick + StonehoofRestTicks);
            Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(new SimEvent(SimEventType.StonehoofCancelled, id, PlayerId, a.Serial, false, Entities.Position[id]));
        }

        /// <summary>
        /// Раз в StonehoofLaneProbeTicks (со сдвигом по индексу): чиста ли
        /// полоса отсюда к герою настолько, чтобы таран прошёл сквозь него.
        /// </summary>
        private void RefreshStonehoofLane(int id, FixVec2 toPlayer, Fix64 distance)
        {
            if (_stonehoofLaneTick[id] != 0 && Tick - _stonehoofLaneTick[id] < StonehoofLaneProbeTicks
                && (Tick + id) % StonehoofLaneProbeTicks != 0) return;
            _stonehoofLaneTick[id] = Tick == 0 ? -1 : Tick;
            if (distance.Raw == 0) { _stonehoofLaneOk[id] = false; return; }
            var need = StonehoofLaneNeed(id, distance);
            _stonehoofLaneOk[id] = StonehoofLaneLength(id, Entities.Position[id], toPlayer / distance, need) >= need;
        }

        /// <summary>
        /// Точка захода: из десяти на кольце 5,5 м вокруг героя (сдвиг по
        /// индексу, чтобы два кабана не шли в одну) — ближайшая к кабану,
        /// которая в его поляне, на полу и откуда полоса к герою чиста. Нет
        /// такой — стоит, где стоит, и ждёт, пока герой сдвинется.
        /// </summary>
        private FixVec2 ChooseStonehoofPoint(int id)
        {
            var hero = Entities.Position[PlayerId];
            var from = Entities.Position[id];
            var radius = Entities.BodyRadius[id];
            var need = StonehoofRingRadius + StonehoofLaneMargin;
            FixVec2 best = from;
            Fix64 bestDistance = Fix64.MaxValue;
            var offset = Fix64.TwoPi * Fix64.Ratio(id * 7 % StonehoofRingPoints, StonehoofRingPoints * 2);
            for (int k = 0; k < StonehoofRingPoints; k++)
            {
                var direction = FixVec2.FromAngle(offset + Fix64.TwoPi * Fix64.Ratio(k, StonehoofRingPoints));
                var point = hero + direction * StonehoofRingRadius;
                if (!InsideStonehoofArena(id, point)) continue;
                if (_layout != null && !_layout.IsWalkable(point, radius)) continue;
                if (StonehoofLaneLength(id, point, -direction, need) < need) continue;
                var d = FixVec2.DistanceSq(from, point);
                if (d < bestDistance) { bestDistance = d; best = point; }
            }
            return best;
        }

        /// <summary>
        /// Ход кабана без тарана. Решение — одно из четырёх:
        /// отдых — пятиться от прижавшего героя (лицом к нему) или стоять и
        /// следить; готов и далеко — идти к герою по пути; готов, рядом и
        /// полоса чиста — стоять и доворачиваться (таран начнёт UpdateStonehooves);
        /// готов, рядом, полосы нет — заходить на точку кольца с чистой полосой.
        /// </summary>
        private void MoveStonehoof(int id, FixVec2 toPlayer)
        {
            var a = _stonehoofActions[id]; var from = Entities.Position[id];
            Entities.Velocity[id] = FixVec2.Zero;
            if (a.Serial != 0)
            {
                a.PreviousPosition = from; Entities.Facing[id] = a.Direction;
                if (Tick >= a.LaunchTick && Tick <= a.StopTick)
                {
                    Fix64 travel;
                    if (a.StopReason == StonehoofStop.ArenaEdge && Tick >= a.BrakeTick)
                    {
                        var t = Fix64.Clamp(Fix64.Ratio(Tick - a.BrakeTick, StonehoofBrakeTicks), Fix64.Zero, Fix64.One);
                        travel = a.Distance - a.BrakeDistance + a.BrakeDistance * (t * 2 - t * t);
                        a.Phase = StonehoofPhase.Brake;
                    }
                    else { travel = Fix64.Min(StonehoofTravel(Tick - a.LaunchTick), a.Distance - a.BrakeDistance); a.Phase = StonehoofPhase.Charge; }
                    var next = a.Origin + a.Direction * travel;
                    if (StonehoofBlocked(id, from, next)) { CancelStonehoof(id); return; }
                    Entities.Position[id] = next; Entities.Velocity[id] = next - from;
                }
                _stonehoofActions[id] = a; return;
            }
            if (!UpdateAggro(id, toPlayer))
            { Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, StonehoofTurnStepCos, StonehoofTurnStepSin); return; }
            var distance = toPlayer.Length;
            bool ready = Tick >= Entities.NextAttackTick[id];

            // Отдых: прижатый пятится лицом к герою (гистерезис 3 → 5 м), далёкий подходит.
            if (!ready)
            {
                _stonehoofAimSince[id] = -1; _stonehoofRepositionSince[id] = -1;
                if (distance < StonehoofBackoffStart) _stonehoofBackoff[id] = true;
                else if (distance > StonehoofBackoffStop) _stonehoofBackoff[id] = false;
                // Один доворот за тик: далеко — шаг с доворотом к пути, рядом — доворот к герою.
                if (distance > StonehoofEngageRange && !_stonehoofBackoff[id])
                { WalkStonehoof(id, SteerHeading(id, toPlayer)); return; }
                Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, StonehoofTurnStepCos, StonehoofTurnStepSin);
                if (_stonehoofBackoff[id] && distance.Raw != 0)
                {
                    var back = EnemyStep(id, from, -toPlayer / distance * StonehoofBackoffStep);
                    Entities.Position[id] = back; Entities.Velocity[id] = back - from;
                }
                return;
            }
            _stonehoofBackoff[id] = false;
            if (distance > StonehoofEngageRange)
            {
                _stonehoofAimSince[id] = -1;
                WalkStonehoof(id, SteerHeading(id, toPlayer));
                return;
            }
            RefreshStonehoofLane(id, toPlayer, distance);
            if (_stonehoofLaneOk[id])
            {
                // Прицел: стоит и доворачивает на героя. Это единственный доворот
                // за тик — UpdateStonehooves только проверяет, что корпус дошёл.
                if (_stonehoofAimSince[id] < 0) _stonehoofAimSince[id] = Tick;
                Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, StonehoofTurnStepCos, StonehoofTurnStepSin);
                return;
            }
            // Полосы нет — заход на точку кольца, откуда она чиста.
            _stonehoofAimSince[id] = -1;
            if (_stonehoofRepositionSince[id] < 0) _stonehoofRepositionSince[id] = Tick;
            if (Tick >= _stonehoofPointUntil[id] || FixVec2.DistanceSq(from, _stonehoofPoint[id]) < Fix64.Ratio(1, 4))
            {
                _stonehoofPoint[id] = ChooseStonehoofPoint(id);
                _stonehoofPointUntil[id] = Tick + TicksPerSecond;
            }
            var toPoint = _stonehoofPoint[id] - from;
            if (toPoint.LengthSq < Fix64.Ratio(1, 4))
            {
                // На точке (или точки нет): доворачивается к герою и ждёт, пока полоса откроется.
                Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, StonehoofTurnStepCos, StonehoofTurnStepSin);
                return;
            }
            WalkStonehoof(id, SteerToward(id, _stonehoofPoint[id]));
        }

        /// <summary>
        /// Шаг кабана вдоль взгляда к heading: поворот 6° за тик и шаг, как
        /// только довод больше StonehoofWalkAlignCos; скорость — доля довода.
        /// Скользит вдоль препятствий (EnemyStep) и не выходит на непроходимое.
        /// </summary>
        private void WalkStonehoof(int id, FixVec2 heading)
        {
            if (heading.LengthSq.Raw == 0) return;
            Entities.Facing[id] = TurnToward(Entities.Facing[id], heading, StonehoofTurnStepCos, StonehoofTurnStepSin);
            var dot = FixVec2.Dot(Entities.Facing[id], heading.Normalized());
            if (dot < StonehoofWalkAlignCos) return;
            var share = Fix64.Min(Fix64.One, (dot - StonehoofWalkAlignCos) / (Fix64.One - StonehoofWalkAlignCos) + Fix64.Ratio(1, 4));
            var from = Entities.Position[id];
            var next = EnemyStep(id, from, Entities.Facing[id] * (Entities.MoveStep[id] * share));
            Entities.Position[id] = next; Entities.Velocity[id] = next - from;
        }

        private void UpdateStonehooves()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestStonehoof) continue;
                var a = _stonehoofActions[id];
                bool stunned = Statuses.IsStunned(id, Tick) && a.Phase != StonehoofPhase.WallImpact;
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || stunned || ForcedMotion.IsActive(Entities, id))
                { CancelStonehoof(id); continue; }
                if (a.Serial != 0)
                {
                    if (!a.HitResolved && Tick >= a.LaunchTick && Tick <= a.StopTick)
                    {
                        // Relative sweep catches the hero crossing the charge between two ticks.
                        var relativeFrom = a.PreviousPosition - _stonehoofHeroBeforeMove;
                        var relativeTo = Entities.Position[id] - Entities.Position[PlayerId];
                        var delta = relativeTo - relativeFrom;
                        var t = delta.LengthSq == Fix64.Zero ? Fix64.Zero : Fix64.Clamp(-FixVec2.Dot(relativeFrom, delta) / delta.LengthSq, Fix64.Zero, Fix64.One);
                        var reach = Entities.BodyRadius[id] + Entities.BodyRadius[PlayerId];
                        if ((relativeFrom + delta * t).LengthSq <= reach * reach)
                        {
                            a.HitResolved = true;
                            int health = Entities.Health[PlayerId];
                            ApplyAbilityDamage(id, PlayerId, Entities.Damage[id], -1, DamageType.Physical);
                            if (Entities.Alive[PlayerId] && Entities.Health[PlayerId] < health)
                            {
                                var side = new FixVec2(-a.Direction.Y, a.Direction.X);
                                if (FixVec2.Dot(Entities.Position[PlayerId] - Entities.Position[id], side) < Fix64.Zero) side = -side;
                                var target = Entities.Position[PlayerId];
                                for (int j = 0; j < 20; j++)
                                {
                                    var next = target + side * Fix64.Ratio(1, 20);
                                    if (_layout != null && !_layout.CanTravel(target, next, Entities.BodyRadius[PlayerId])) break;
                                    target = next;
                                }
                                ForcedMotion.Begin(Entities, PlayerId, target, 6, ForcedMotionKind.Knockback);
                            }
                        }
                    }
                    if (Tick >= a.StopTick && a.Phase != StonehoofPhase.WallImpact)
                    {
                        Entities.Position[id] = a.Target; Entities.Velocity[id] = FixVec2.Zero;
                        _events.Add(new SimEvent(SimEventType.StonehoofStopped, id, PlayerId, a.Serial, a.StopReason == StonehoofStop.Obstacle, a.Target));
                        ResolveTelegraphsOf(id);
                        if (a.StopReason == StonehoofStop.Obstacle)
                        { a.Phase = StonehoofPhase.WallImpact; Statuses.ApplyStun(id, a.EndTick); }
                    }
                    _stonehoofActions[id] = Tick >= a.EndTick ? default : a;
                    continue;
                }
                if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id] || !_stonehoofLaneOk[id]) continue;
                var offset = Entities.Position[PlayerId] - Entities.Position[id];
                if (offset.LengthSq > StonehoofEngageRange * StonehoofEngageRange || offset.LengthSq < Fix64.Ratio(1, 10000)) continue;
                if (!BigAttackTokenFree(id, 1, Tick + StonehoofWindupTicks)) continue;
                var direction = offset.Normalized();
                // Доворот уже сделан в MoveStonehoof. Разбег — когда корпус смотрит
                // на героя с точностью до одного шага; прицелившийся дольше 1,5 с —
                // с точностью до 15°: StartStonehoof всё равно ставит взгляд точно
                // по линии, так что метка и таран совпадают.
                var dot = FixVec2.Dot(Entities.Facing[id], direction);
                bool relaxed = _stonehoofAimSince[id] >= 0 && Tick - _stonehoofAimSince[id] >= StonehoofAimRelaxTicks;
                if (dot >= StonehoofTurnStepCos || (relaxed && dot >= StonehoofAimRelaxCos)) StartStonehoof(id, direction);
            }
        }

        public EncounterPlan SetupStonehoofEncounter(LayoutMap map, ulong seed, int count = 1, bool obstacle = false)
        {
            if (count < 1 || count > 3) throw new ArgumentOutOfRangeException(nameof(count));
            // Здоровье стенда — табличное: стенд показывает того же Камнекопыта, что забег.
            int health = ArchetypeHealth(EnemyKind.ForestStonehoof);
            if (map == null) SetupTestArena(0); else SetupRift(map, seed, 0, 0, health);
            _campWalkMap = null; _events.Clear();
            int module = 0;
            var center = map == null ? FixVec2.Zero : map.GladeCount > 0 ? map.GetGlade(0).Center : map.CenterOf(0);
            FixVec2 hero, enemy;
            if (map != null) for (int m = 0; m < map.PlacedCount; m++)
                if (map.ContainsWorld(m, center)) { module = m; break; }
            var axis = new FixVec2(Fix64.One, Fix64.Zero);
            hero = center - axis * Fix64.FromInt(3); enemy = center + axis * Fix64.FromInt(3);
            if (map != null)
            { hero = map.ClampToWalkable(hero, Entities.BodyRadius[PlayerId]); enemy = map.ClampToWalkable(enemy, StonehoofRadius); }
            if (obstacle && map != null)
            {
                var rock = center - axis * Fix64.FromInt(5);
                if (map.IsWalkable(rock, Fix64.One)) map.AddTestObstacle(new LayoutObstacle(rock, Fix64.One, 0));
            }
            Entities.Position[PlayerId] = hero; Entities.Facing[PlayerId] = axis;
            for (int n = 0; n < count; n++)
            {
                var p = enemy + new FixVec2(Fix64.Zero, Fix64.FromInt(n * 2));
                if (map != null) p = map.ClampToWalkable(p, StonehoofRadius);
                int id = Entities.Spawn(p, health, Faction.Orvill); ConfigureEnemy(id, EnemyKind.ForestStonehoof);
                Entities.Facing[id] = (hero - p).Normalized(); Entities.Aggro[id] = true;
                Entities.NextAttackTick[id] = Tick + 30 + n * 15;
                _events.Add(SimEvent.Spawn(id, p));
            }
            _eliteMask = new bool[Entities.Capacity]; Grid.Rebuild(Entities);
            return new EncounterPlan(new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.MainPath,
                module, -1, StableId.Of("encounter.forest-stonehoof.test"), enemy, 1, count) }, _eliteMask, Fix64.FromInt(5), 0);
        }

        private void HashStonehooves(ref ulong hash)
        {
            bool present = _stonehoofSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++) present = Entities.Kind[id] == EnemyKind.ForestStonehoof;
            if (!present) return;
            Hashing.Mix(ref hash, 0x53544F4E); Hashing.Mix(ref hash, _stonehoofSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestStonehoof) continue;
                var a = _stonehoofActions[id]; Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, _stonehoofArena[id]);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Phase); Hashing.Mix(ref hash, (int)a.StopReason);
                Hashing.Mix(ref hash, a.StartTick); Hashing.Mix(ref hash, a.LaunchTick); Hashing.Mix(ref hash, a.BrakeTick);
                Hashing.Mix(ref hash, a.StopTick); Hashing.Mix(ref hash, a.EndTick); Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
                Hashing.Mix(ref hash, a.Origin.X.Raw); Hashing.Mix(ref hash, a.Origin.Y.Raw);
                Hashing.Mix(ref hash, a.Target.X.Raw); Hashing.Mix(ref hash, a.Target.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.PreviousPosition.X.Raw); Hashing.Mix(ref hash, a.PreviousPosition.Y.Raw);
                Hashing.Mix(ref hash, a.Distance.Raw); Hashing.Mix(ref hash, a.BrakeDistance.Raw);
                // ИИ v2: полоса, прицел, заход, отход.
                Hashing.Mix(ref hash, _stonehoofLaneTick[id]); Hashing.Mix(ref hash, _stonehoofLaneOk[id] ? 1 : 0);
                Hashing.Mix(ref hash, _stonehoofAimSince[id]); Hashing.Mix(ref hash, _stonehoofRepositionSince[id]);
                Hashing.Mix(ref hash, _stonehoofPointUntil[id]); Hashing.Mix(ref hash, _stonehoofBackoff[id] ? 1 : 0);
                Hashing.Mix(ref hash, _stonehoofPoint[id].X); Hashing.Mix(ref hash, _stonehoofPoint[id].Y);
            }
        }
    }
}
