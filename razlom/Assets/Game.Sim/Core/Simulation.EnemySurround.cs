using System;

namespace Game.Sim
{
    /// <summary>
    /// Места подхода и ожидания выбираются каждым врагом отдельно.
    /// Направления не вращаются по часам: переступание должно приближать
    /// к доступной атаке, а не заставлять всю пачку танцевать вокруг героя.
    /// Удержание подходящей позиции и разнесённый пересчёт убирают метания.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int SurroundAssignTicks = 15;
        public const int MeleeSurroundSlots = 8, SwarmSurroundSlots = 6;

        /// <summary>Кольцо ожидания: Хранитель — за краем своего сектора (2,2 м), Расщепень — дальше.</summary>
        public static readonly Fix64 GuardianWaitRadius = Fix64.FromInt(3);
        public static readonly Fix64 SplitterWaitRadius = Fix64.Ratio(16, 5);
        public static readonly Fix64 SwarmSlotRadius = Fix64.Ratio(13, 10);
        public static readonly Fix64 SwarmWaitRadius = Fix64.Ratio(13, 5);

        /// <summary>Место держится, пока другое не ближе на столько метров.</summary>
        private static readonly Fix64 SurroundKeepMargin = Fix64.Ratio(3, 2);

        /// <summary>Цена места внутри чужой метки, метры.</summary>
        private static readonly Fix64 SurroundDangerPenalty = Fix64.FromInt(3);

        private static readonly Fix64 Cos45 = Fix64.Ratio(70711, 100000);
        private static readonly Fix64 Cos60 = Fix64.Ratio(1, 2), Sin60 = Fix64.Ratio(86603, 100000);

        /// <summary>За сколько тиков до готовности удара ближник уже идёт на кольцо удара.</summary>
        private const int EngageLeadTicks = 6;

        /// <summary>Ближе этого к кольцу моб правит ход по дуге вокруг героя, дальше — идёт к месту.</summary>
        private static readonly Fix64 SurroundNearBand = Fix64.Ratio(5, 2);

        private int[] _surroundSlot;
        private bool[] _engaged;
        private int[] _surroundNextPick;
        private int[] _surroundWaitSince;
        private FixVec2[] _rangedGoal;
        private int[] _rangedGoalUntil;
        private bool[] _rangedGoalMoving;
        private readonly FixVec2[] _meleeSlotDirections = new FixVec2[MeleeSurroundSlots];
        private readonly FixVec2[] _swarmSlotDirections = new FixVec2[SwarmSurroundSlots];

        private void ResetSurround()
        {
            Array.Clear(_surroundNextPick, 0, _surroundNextPick.Length);
            FixVec2 d = new FixVec2(Fix64.One, Fix64.Zero);
            for (int k = 0; k < MeleeSurroundSlots; k++)
            {
                _meleeSlotDirections[k] = d;
                d = new FixVec2(d.X * Cos45 - d.Y * Cos45, d.X * Cos45 + d.Y * Cos45).Normalized();
            }
            d = new FixVec2(Fix64.One, Fix64.Zero);
            for (int k = 0; k < SwarmSurroundSlots; k++)
            {
                _swarmSlotDirections[k] = d;
                d = new FixVec2(d.X * Cos60 - d.Y * Sin60, d.X * Sin60 + d.Y * Cos60).Normalized();
            }
        }

        private static bool UsesSurround(EnemyKind kind) => UsesEnemySwing(kind);

        private Fix64 WaitRadiusOf(EnemyKind kind)
            => IsSwarmLike(kind) ? SwarmWaitRadius : kind == EnemyKind.ForestSplitter ? SplitterWaitRadius : GuardianWaitRadius;

        private bool CanHoldSurroundSlot(int id)
            => Entities.Alive[id] && Entities.Aggro[id] && !IsEmerging(id) && UsesSurround(Entities.Kind[id])
                && Entities.NextAttackTick[id] != int.MaxValue && !Statuses.IsStunned(id, Tick)
                && !ForcedMotion.IsInterrupting(Entities, id);

        /// <summary>Готовность обновляется каждый тик, поиск места — раз в полсекунды.</summary>
        private void UpdateSurround()
        {
            AssignSurroundSlots();
        }

        private void AssignSurroundSlots()
        {
            FixVec2 hero = Entities.Position[PlayerId];
            bool meleeTaken = CountMeleeAttackTokens(-1) >= MeleeAttackTokenLimit;
            bool biteTaken = CountSwarmBiteTokens(-1) >= SwarmBiteTokenLimit;
            // Сначала снимаем ушедших владельцев и запоминаем начало ожидания.
            // Это отдельный проход: номер сущности не даёт права обгонять очередь.
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!CanHoldSurroundSlot(i))
                { _surroundSlot[i] = -1; _surroundWaitSince[i] = -1; continue; }
                if (_surroundSlot[i] >= 0) _surroundWaitSince[i] = -1;
                else if (_surroundWaitSince[i] < 0) _surroundWaitSince[i] = Tick;
            }
            for (int i = 1; i < Entities.Count; i++)
            {
                EnemyKind kind = Entities.Kind[i];
                if (!CanHoldSurroundSlot(i))
                { _surroundSlot[i] = -1; _engaged[i] = false; continue; }
                bool swarm = IsSwarmLike(kind);
                bool ready = Tick >= Entities.NextAttackTick[i] - EngageLeadTicks;
                _engaged[i] = ready && !(swarm ? biteTaken : meleeTaken);
                Fix64 radius = SlotRadius(i, kind, swarm);
                FixVec2[] directions = swarm ? _swarmSlotDirections : _meleeSlotDirections;
                if (EnemySwingHoldsBody(i) || SplitterOwnsPosition(i)) continue;
                if (_surroundSlot[i] >= 0 && Tick < _surroundNextPick[i]
                    && SurroundPointUsable(i, hero + directions[_surroundSlot[i]] * radius)) continue;
                _surroundNextPick[i] = Tick + SurroundAssignTicks - (Tick + i) % SurroundAssignTicks;
                int best = -1, kept = _surroundSlot[i];
                Fix64 bestCost = Fix64.MaxValue, keptCost = Fix64.MaxValue;
                for (int k = 0; k < directions.Length; k++)
                {
                    if (SurroundSlotOccupied(i, k, swarm)) continue;
                    FixVec2 point = hero + directions[k] * radius;
                    if (!SurroundPointUsable(i, point)) continue;
                    Fix64 cost = FixVec2.Distance(point, Entities.Position[i]);
                    // Прямой открытый подход полезнее равного по длине пути сквозь камень.
                    if (_layout != null && !_layout.CanTravel(Entities.Position[i], point, Entities.BodyRadius[i]))
                        cost += Fix64.FromInt(2);
                    if (InAllyDanger(i, point, out _)) cost += SurroundDangerPenalty;
                    if (k == kept) keptCost = cost;
                    if (cost < bestCost) { bestCost = cost; best = k; }
                }
                if (kept >= 0 && keptCost != Fix64.MaxValue && keptCost <= bestCost + SurroundKeepMargin)
                    best = kept;
                _surroundSlot[i] = best;
            }
        }

        /// <summary>
        /// Закончивший замах уступает своё место самому давнему ожидающему
        /// того же класса. Остальные места не двигаются; при свободном кольце
        /// позиция сохраняется. Передача только после восстановления, поэтому
        /// закреплённая атака и её положение не меняются.
        /// </summary>
        private void YieldSurroundSlot(int id)
        {
            int slot = _surroundSlot[id];
            if (slot < 0) return;
            bool swarm = IsSwarmLike(Entities.Kind[id]);
            int oldest = -1;
            for (int other = 1; other < Entities.Count; other++)
            {
                if (other == id || _surroundSlot[other] >= 0 || _surroundWaitSince[other] < 0
                    || !Entities.Alive[other] || !Entities.Aggro[other] || IsEmerging(other)
                    || !UsesSurround(Entities.Kind[other]) || IsSwarmLike(Entities.Kind[other]) != swarm
                    || Entities.NextAttackTick[other] == int.MaxValue || EnemySwingHoldsBody(other)
                    || SplitterOwnsPosition(other) || Statuses.IsStunned(other, Tick)
                    || ForcedMotion.IsInterrupting(Entities, other)) continue;
                if (oldest < 0 || _surroundWaitSince[other] < _surroundWaitSince[oldest]) oldest = other;
            }
            if (oldest < 0) return;
            _surroundSlot[oldest] = slot;
            _surroundWaitSince[oldest] = -1;
            _surroundNextPick[oldest] = 0;
            _surroundSlot[id] = -1;
            _surroundWaitSince[id] = Tick;
        }

        private bool SurroundPointUsable(int id, FixVec2 point)
            => _layout == null || _layout.IsWalkable(point, Entities.BodyRadius[id]);

        private bool SurroundSlotOccupied(int id, int slot, bool swarm)
        {
            for (int other = 1; other < Entities.Count; other++)
                if (other != id && Entities.Alive[other] && Entities.Aggro[other]
                    && UsesSurround(Entities.Kind[other]) && IsSwarmLike(Entities.Kind[other]) == swarm
                    && _surroundSlot[other] == slot) return true;
            return false;
        }

        private Fix64 SlotRadius(int id, EnemyKind kind, bool swarm)
        {
            // Готовый к перекату Расщепень держит дистанцию переката, а не укуса.
            if (SplitterWantsRoll(id)) return SplitterRollStandoff;
            if (!_engaged[id]) return WaitRadiusOf(kind);
            return swarm ? SwarmSlotRadius : AttackRangeFor(id) - Fix64.Ratio(1, 10);
        }

        /// <summary>
        /// Желаемая скорость ближника с местом. Далеко — к месту (с путём в
        /// обход); у кольца — по дуге вокруг героя к своему месту, не сквозь
        /// героя. facing — куда смотреть: при заметном боковом ходе — по ходу
        /// (клипы боком не ходят), иначе на героя.
        /// </summary>
        private FixVec2 SurroundWanted(int i, FixVec2 toPlayer, Fix64 speed, Fix64 attackRange, out FixVec2 facing)
        {
            facing = toPlayer;
            EnemyKind kind = Entities.Kind[i];
            bool swarm = IsSwarmLike(kind);
            Fix64 distance = toPlayer.Length;
            if (distance.Raw == 0) return FixVec2.Zero;
            FixVec2 inward = toPlayer / distance;
            FixVec2 bearing = -inward;
            int slot = _surroundSlot[i];
            bool engaged = _engaged[i] && !SplitterWantsRoll(i);
            Fix64 ring = SlotRadius(i, kind, swarm);
            FixVec2 slotDirection = slot >= 0 ? (swarm ? _swarmSlotDirections[slot] : _meleeSlotDirections[slot]) : bearing;
            // Без места (мест не хватило): ждёт на метр дальше кольца ожидания там, где стоит.
            if (slot < 0) { engaged = false; ring = WaitRadiusOf(kind) + Fix64.One; }

            // Готов и в досягаемости — стоит: замах начнёт ResolveAttacks.
            if (engaged && distance <= attackRange) return FixVec2.Zero;

            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 goal = hero + slotDirection * ring;
            if (distance > ring + SurroundNearBand)
            {
                FixVec2 heading = SteerToward(i, goal);
                facing = heading;
                return heading * speed;
            }

            // У кольца: радиальная поправка плюс дуга к месту.
            Fix64 radial = Fix64.Clamp(distance - ring, -Fix64.One, Fix64.One);
            FixVec2 onRing = hero + bearing * ring;
            Fix64 gap = FixVec2.Distance(onRing, goal);
            Fix64 cross = bearing.X * slotDirection.Y - bearing.Y * slotDirection.X;
            var tangent = new FixVec2(-bearing.Y, bearing.X);
            if (cross.Raw < 0) tangent = -tangent;
            Fix64 along = Fix64.Min(Fix64.One, gap / Fix64.Ratio(6, 5)) * Fix64.Ratio(4, 5);
            FixVec2 wanted = inward * (radial * speed) + tangent * (along * speed);
            wanted = wanted.ClampLength(speed);
            if (Tick < _unstickUntil[i] || NavActive(i))
            {
                FixVec2 steer = SteerToward(i, goal);
                wanted = steer * wanted.Length;
            }
            // Боком клипы не ходят: заметный ход в сторону — лицом по ходу.
            if (!engaged && wanted.LengthSq > (speed * Fix64.Ratio(35, 100)) * (speed * Fix64.Ratio(35, 100))
                && FixVec2.Dot(wanted.Normalized(), inward) < Fix64.Ratio(1, 2))
                facing = wanted;
            return wanted;
        }

        // ---------- чужие метки ----------

        /// <summary>
        /// Стоит ли тело id в точке point внутри чужой действующей метки:
        /// круг, кольцо, полоса другого моба или кислая лужа. Сектор ближнего
        /// удара не в счёт — он короткий и бьёт только героя. escape — куда
        /// выходить: из круга — от центра, из кольца — к ближней кромке, из
        /// полосы — поперёк от оси.
        /// </summary>
        private bool InAllyDanger(int id, FixVec2 point, out FixVec2 escape)
        {
            escape = FixVec2.Zero;
            Fix64 body = Entities.BodyRadius[id];
            for (int slot = 0; slot < _telegraphHighWater; slot++)
            {
                var t = _telegraphs[slot];
                if (t.Serial == 0 || !t.IsActive || t.Source == id || t.Shape == TelegraphShape.Sector) continue;
                if (!TelegraphContains(in t, point, body)) continue;
                FixVec2 offset = point - t.Origin;
                switch (t.Shape)
                {
                    case TelegraphShape.Lane:
                    {
                        var across = new FixVec2(-t.Direction.Y, t.Direction.X);
                        Fix64 side = FixVec2.Dot(offset, across);
                        escape = side.Raw >= 0 ? across : -across;
                        break;
                    }
                    case TelegraphShape.Ring:
                    {
                        Fix64 d = offset.Length;
                        bool inner = d < (t.InnerRadius + t.Radius) / 2;
                        escape = d.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : inner ? -offset / d : offset / d;
                        break;
                    }
                    default:
                        escape = offset.LengthSq.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : offset.Normalized();
                        break;
                }
                return true;
            }
            return InPuddleDanger(point, body, out escape);
        }

        // ---------- крупные атакующие не с одной стороны ----------

        /// <summary>Кто бьёт крупными метками издали: их держим врозь по углу вокруг героя.</summary>
        private static bool IsBigAttacker(EnemyKind kind)
            => kind == EnemyKind.ForestBud || kind == EnemyKind.ForestRootSnarer
                || kind == EnemyKind.ForestThorncaster || kind == EnemyKind.ForestStonehoof;

        private static readonly Fix64 BigAttackerSpreadCos = Fix64.Ratio(1, 2);

        /// <summary>
        /// Выбираем свободную огневую позицию и держим её до следующей оценки.
        /// Угол к соседу влияет на цену, но не заставляет бесконечно кружить.
        /// </summary>
        private bool BigAttackerSpread(int id, out FixVec2 tangent)
        {
            tangent = FixVec2.Zero;
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 at = Entities.Position[id], mine = at - hero;
            if (mine.LengthSq.Raw == 0) return false;
            if (Tick >= _rangedGoalUntil[id] || !SurroundPointUsable(id, _rangedGoal[id]))
            {
                Fix64 radius = mine.Length;
                FixVec2 best = at;
                Fix64 bestCost = RangedPositionCost(id, at, hero);
                FixVec2 direction = mine / radius;
                for (int k = 0; k < 8; k++)
                {
                    FixVec2 point = hero + direction * radius;
                    if (SurroundPointUsable(id, point)
                        && (_layout == null || _layout.CanTravel(at, point, Entities.BodyRadius[id])))
                    {
                        Fix64 cost = FixVec2.Distance(at, point) / 3 + RangedPositionCost(id, point, hero);
                        if (cost + Fix64.Ratio(1, 2) < bestCost) { bestCost = cost; best = point; }
                    }
                    direction = new FixVec2(direction.X * Cos45 - direction.Y * Cos45,
                        direction.X * Cos45 + direction.Y * Cos45).Normalized();
                }
                _rangedGoal[id] = best;
                _rangedGoalMoving[id] = !best.Equals(at);
                _rangedGoalUntil[id] = Tick + SurroundAssignTicks - (Tick + id) % SurroundAssignTicks;
            }
            if (!_rangedGoalMoving[id]) return false;
            FixVec2 delta = _rangedGoal[id] - at;
            if (delta.LengthSq <= Fix64.Ratio(1, 25)) { _rangedGoalMoving[id] = false; return false; }
            tangent = delta.Normalized();
            return true;
        }

        private Fix64 RangedPositionCost(int id, FixVec2 point, FixVec2 hero)
        {
            Fix64 cost = Fix64.Zero;
            FixVec2 mine = (point - hero).Normalized();
            if (_layout != null && !_layout.CanTravel(point, hero, ThornGroundProbe)) cost += Fix64.FromInt(6);
            if (InAllyDanger(id, point, out _)) cost += SurroundDangerPenalty;
            for (int j = 1; j < Entities.Count; j++)
            {
                if (j == id || !Entities.Alive[j] || !Entities.Aggro[j] || !IsBigAttacker(Entities.Kind[j])) continue;
                FixVec2 other = Entities.Position[j] - hero;
                if (other.LengthSq.Raw == 0) continue;
                other = other.Normalized();
                Fix64 crowded = FixVec2.Dot(mine, other) - BigAttackerSpreadCos;
                if (crowded > Fix64.Zero) cost += crowded * 8;
                Fix64 clearance = Entities.BodyRadius[id] + Entities.BodyRadius[j] + Fix64.One;
                if (FixVec2.DistanceSq(point, Entities.Position[j]) < clearance * clearance)
                    cost += Fix64.FromInt(3);
            }
            return cost;
        }

        /// <summary>Вендиго занимает свободный подход к когтю, сохраняя хорошую точку.</summary>
        private FixVec2 CloseApproachPosition(int id, Fix64 range)
        {
            FixVec2 hero = Entities.Position[PlayerId], at = Entities.Position[id];
            FixVec2 away = at - hero;
            if (away.LengthSq.Raw == 0) return at;
            if (Tick >= _rangedGoalUntil[id] || !SurroundPointUsable(id, _rangedGoal[id]))
            {
                Fix64 radius = Fix64.Min(away.Length, range - Fix64.Ratio(1, 10));
                FixVec2 direction = away.Normalized();
                FixVec2 best = at;
                Fix64 bestCost = Fix64.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    FixVec2 point = hero + direction * radius;
                    if (SurroundPointUsable(id, point))
                    {
                        Fix64 cost = ClosePositionCost(id, point, hero) + FixVec2.Distance(at, point) / 3;
                        if (_layout != null && !_layout.CanTravel(at, point, Entities.BodyRadius[id]))
                            cost += Fix64.FromInt(2);
                        if (cost + Fix64.Ratio(1, 2) < bestCost) { best = point; bestCost = cost; }
                    }
                    direction = new FixVec2(direction.X * Cos45 - direction.Y * Cos45,
                        direction.X * Cos45 + direction.Y * Cos45).Normalized();
                }
                _rangedGoal[id] = best;
                _rangedGoalUntil[id] = Tick + SurroundAssignTicks - (Tick + id) % SurroundAssignTicks;
            }
            return _rangedGoal[id];
        }

        private Fix64 ClosePositionCost(int id, FixVec2 point, FixVec2 hero)
        {
            Fix64 cost = SurroundPointUsable(id, point) ? Fix64.Zero : Fix64.FromInt(20);
            if (_layout != null && !_layout.CanTravel(point, hero, Entities.BodyRadius[id])) cost += Fix64.FromInt(6);
            if (InAllyDanger(id, point, out _)) cost += SurroundDangerPenalty;
            for (int other = 1; other < Entities.Count; other++)
            {
                if (other == id || !Entities.Alive[other] || !Entities.Aggro[other]) continue;
                Fix64 clearance = Entities.BodyRadius[id] + Entities.BodyRadius[other] + Fix64.Ratio(3, 10);
                Fix64 distance = FixVec2.Distance(point, Entities.Position[other]);
                if (distance < clearance) cost += (clearance - distance) * 6;
            }
            return cost;
        }

        private void HashSurround(ref ulong hash)
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                Hashing.Mix(ref hash, _surroundNextPick[id]);
                Hashing.Mix(ref hash, _surroundWaitSince[id]);
                Hashing.Mix(ref hash, _rangedGoalUntil[id]);
                Hashing.Mix(ref hash, _rangedGoalMoving[id] ? 1 : 0);
                Hashing.Mix(ref hash, _rangedGoal[id].X); Hashing.Mix(ref hash, _rangedGoal[id].Y);
            }
        }
    }
}
