using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum StonehoofPhase : byte { None, Windup, Charge, Brake, WallImpact }
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

    public sealed partial class Simulation
    {
        public const int StonehoofWindupTicks = 30, StonehoofAccelerationTicks = 6,
            StonehoofBrakeTicks = 18, StonehoofWallTicks = 36, StonehoofRestTicks = 120;
        public static readonly Fix64 StonehoofRadius = Fix64.Ratio(7, 10);
        private static readonly Fix64 StonehoofStep = Fix64.Ratio(12, TicksPerSecond);
        private readonly StonehoofActionState[] _stonehoofActions;
        private readonly int[] _stonehoofArena;
        private int _stonehoofSerial;
        private FixVec2 _stonehoofHeroBeforeMove;

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
        }

        private void ConfigureStonehoof(int id)
        {
            Entities.BodyRadius[id] = StonehoofRadius;
            Entities.PushWeight[id] = Fix64.One;
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, Fix64.Ratio(5, 2));
            s.SetBase(StatType.Damage, Fix64.FromInt(30));
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            _stonehoofArena[id] = FindStonehoofArena(Entities.Position[id]);
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

        private bool InsideStonehoofArena(int id, FixVec2 point)
        {
            int arena = _stonehoofArena[id]; var radius = Entities.BodyRadius[id];
            // Unmapped combat test has an explicit 24 m square, never an infinite charge.
            if (_layout == null || arena == -1)
                return Fix64.Abs(point.X) <= Fix64.FromInt(12) - radius
                    && Fix64.Abs(point.Y) <= Fix64.FromInt(12) - radius;
            for (int k = 0; k < 9; k++)
            {
                var offset = k == 0 ? FixVec2.Zero : new FixVec2(
                    k == 1 || k == 5 || k == 8 ? radius : k == 2 || k == 6 || k == 7 ? -radius : Fix64.Zero,
                    k == 3 || k == 5 || k == 6 ? radius : k == 4 || k == 7 || k == 8 ? -radius : Fix64.Zero);
                var p = point + offset;
                if (arena >= 0 ? _layout.GetGlade(arena).Field(p) > Fix64.One : !_layout.ContainsWorld(-2 - arena, p)) return false;
            }
            return true;
        }

        private bool StonehoofBlocked(int id, FixVec2 from, FixVec2 to)
            => _layout != null && !_layout.CanTravel(from, to, Entities.BodyRadius[id]);

        private FixVec2 StonehoofEndpoint(int id, FixVec2 origin, FixVec2 direction, out StonehoofStop reason)
        {
            var previous = origin; reason = StonehoofStop.ArenaEdge;
            // Dense samples plus LayoutMap's swept obstacle test also catch thin obstacles.
            for (int k = 1; k <= 1280; k++)
            {
                var candidate = origin + direction * Fix64.Ratio(k, 20);
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

        private static Fix64 StonehoofTravel(int age)
        {
            if (age <= 0) return Fix64.Zero;
            if (age <= StonehoofAccelerationTicks) return Fix64.Ratio(age * age, 30);
            return Fix64.Ratio(6, 5) + StonehoofStep * (age - StonehoofAccelerationTicks);
        }

        private void StartStonehoof(int id, FixVec2 direction)
        {
            var origin = Entities.Position[id];
            var target = StonehoofEndpoint(id, origin, direction, out var reason);
            var distance = (target - origin).Length;
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
            _events.Add(new SimEvent(SimEventType.StonehoofStarted, id, PlayerId, _stonehoofSerial, false, origin));
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
            Entities.NextAttackTick[id] = Math.Max(Entities.NextAttackTick[id], Tick + StonehoofRestTicks);
            Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(new SimEvent(SimEventType.StonehoofCancelled, id, PlayerId, a.Serial, false, Entities.Position[id]));
        }

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
            if (!UpdateAggro(id, toPlayer)) return;
            var distance = toPlayer.Length; FixVec2 wanted = FixVec2.Zero;
            if (Tick >= Entities.NextAttackTick[id] && distance <= Fix64.FromInt(7))
            { Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin); return; }
            if (distance > Fix64.FromInt(7)) wanted = toPlayer.Normalized();
            else if (distance < Fix64.FromInt(4)) wanted = -toPlayer.Normalized();
            if (wanted.LengthSq == Fix64.Zero)
            { Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin); return; }
            // Turn feet and body first; never translate sideways while looking at the hero.
            Entities.Facing[id] = TurnToward(Entities.Facing[id], wanted, EnemyTurnStepCos, EnemyTurnStepSin);
            if (FixVec2.Dot(Entities.Facing[id], wanted) < Fix64.Ratio(95, 100)) return;
            var nextPosition = from + Entities.Facing[id] * Entities.MoveStep[id];
            if (InsideStonehoofArena(id, nextPosition) && !StonehoofBlocked(id, from, nextPosition))
            { Entities.Position[id] = nextPosition; Entities.Velocity[id] = nextPosition - from; }
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
                        if (a.StopReason == StonehoofStop.Obstacle)
                        { a.Phase = StonehoofPhase.WallImpact; Statuses.ApplyStun(id, a.EndTick); }
                    }
                    _stonehoofActions[id] = Tick >= a.EndTick ? default : a;
                    continue;
                }
                if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id]) continue;
                var offset = Entities.Position[PlayerId] - Entities.Position[id];
                if (offset.LengthSq > Fix64.FromInt(49) || offset.LengthSq < Fix64.Ratio(1, 10000)) continue;
                var direction = offset.Normalized();
                Entities.Facing[id] = TurnToward(Entities.Facing[id], direction, EnemyTurnStepCos, EnemyTurnStepSin);
                if (FixVec2.Dot(Entities.Facing[id], direction) >= Fix64.Ratio(97, 100)) StartStonehoof(id, direction);
            }
        }

        public EncounterPlan SetupStonehoofEncounter(LayoutMap map, ulong seed, int count = 1, bool obstacle = false)
        {
            if (count < 1 || count > 3) throw new ArgumentOutOfRangeException(nameof(count));
            if (map == null) SetupTestArena(0); else SetupRift(map, seed, 0, 0, 180);
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
                int id = Entities.Spawn(p, 180, Faction.Orvill); ConfigureEnemy(id, EnemyKind.ForestStonehoof);
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
            }
        }
    }
}
