using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum WendigoAction : byte { None, Claw, Leap }

    public readonly struct WendigoActionState
    {
        public readonly int Serial, StartTick, LaunchTick, ImpactTick, EndTick;
        public readonly WendigoAction Kind;
        public readonly FixVec2 Origin, Target, Direction;
        public readonly bool HitResolved;
        public WendigoActionState(int serial, WendigoAction kind, int start, FixVec2 origin,
            FixVec2 target, FixVec2 direction, bool hitResolved = false)
        {
            Serial = serial; Kind = kind; StartTick = start; Origin = origin; Target = target;
            Direction = direction; HitResolved = hitResolved;
            LaunchTick = start + (kind == WendigoAction.Leap ? 24 : 18);
            ImpactTick = start + (kind == WendigoAction.Leap ? 33 : 18);
            EndTick = ImpactTick + (kind == WendigoAction.Leap ? 18 : 12);
        }
        public WendigoActionState Resolve() => new WendigoActionState(Serial, Kind, StartTick, Origin, Target, Direction, true);
    }

    public sealed partial class Simulation
    {
        public const int WendigoClawDamage = 45;
        public const int WendigoClawRestTicks = 15;
        public const int WendigoLeapCooldownTicks = 195;
        public static readonly Fix64 WendigoBodyRadius = Fix64.Ratio(95, 100);
        public static readonly Fix64 WendigoClawRange = Fix64.Ratio(27, 10);
        public static readonly Fix64 WendigoLeapRadius = Fix64.Ratio(125, 100);
        private static readonly Fix64 WendigoClawCos = Fix64.Ratio(34202, 100000);
        private readonly WendigoActionState[] _wendigoActions;
        private readonly int[] _wendigoNextLeap;
        private int _wendigoSerial;

        public bool TryGetWendigoAction(int entity, out WendigoActionState action)
        {
            action = (uint)entity < (uint)_wendigoActions.Length ? _wendigoActions[entity] : default;
            return action.Serial != 0;
        }

        public bool IsWendigoAirborne(int id)
        {
            var a = _wendigoActions[id];
            return a.Kind == WendigoAction.Leap && Tick >= a.LaunchTick && Tick <= a.ImpactTick
                && Entities.Alive[id] && !Statuses.IsStunned(id, Tick);
        }

        private void ResetWendigo()
        {
            Array.Clear(_wendigoActions, 0, _wendigoActions.Length);
            Array.Clear(_wendigoNextLeap, 0, _wendigoNextLeap.Length); _wendigoSerial = 0;
        }

        private void ConfigureWendigo(int id)
        {
            Entities.BodyRadius[id] = WendigoBodyRadius;
            Entities.PushWeight[id] = Fix64.Ratio(1, 2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, Fix64.FromInt(3));
            s.SetBase(StatType.Damage, Fix64.FromInt(WendigoClawDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.One);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = Progression.EliteKillXp;
            // Сначала сближается пешком; первое появление не даёт бесплатный мгновенный прыжок.
            _wendigoNextLeap[id] = Tick + WendigoLeapCooldownTicks;
        }

        private void MoveWendigo(int id, FixVec2 toPlayer)
        {
            var a = _wendigoActions[id];
            if (a.Serial != 0)
            {
                Entities.Facing[id] = a.Direction;
                Entities.Velocity[id] = FixVec2.Zero;
                if (a.Kind == WendigoAction.Leap && Tick >= a.LaunchTick && Tick <= a.ImpactTick)
                {
                    var t = Fix64.Clamp(Fix64.Ratio(Tick - a.LaunchTick, 9), Fix64.Zero, Fix64.One);
                    var next = a.Origin + (a.Target - a.Origin) * t;
                    Entities.Velocity[id] = next - Entities.Position[id];
                    Entities.Position[id] = next;
                }
                return;
            }
            Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return; }
            var wanted = toPlayer.LengthSq > WendigoClawRange * WendigoClawRange
                ? toPlayer.Normalized() * Entities.MoveStep[id] : FixVec2.Zero;
            Entities.Velocity[id] = Approach(Entities.Velocity[id], wanted, Entities.MoveStep[id]);
            var from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, Entities.Velocity[id]);
            Entities.Velocity[id] = Entities.Position[id] - from;
        }

        private bool WendigoLeapPathClear(int id, FixVec2 target)
        {
            var start = Entities.Position[id];
            // Проверяем всю полосу тела, а не одну конечную точку за стеной.
            for (int k = 1; k <= 28; k++)
            {
                var next = start + (target - start) * Fix64.Ratio(k, 28);
                var from = start + (target - start) * Fix64.Ratio(k - 1, 28);
                if ((MoveInsideLayout(id, from, next - from) - next).LengthSq > Fix64.Ratio(1, 10000)) return false;
            }
            return true;
        }

        private void UpdateWendigo()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestWendigo) continue;
                var a = _wendigoActions[id];
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                    || ForcedMotion.IsActive(Entities, id))
                {
                    if (a.Serial != 0) _events.Add(new SimEvent(SimEventType.WendigoCancelled, id, PlayerId,
                        a.Serial, false, Entities.Position[id], actionVariant: (int)a.Kind));
                    _wendigoActions[id] = default;
                    continue;
                }
                if (a.Serial != 0 && Tick >= a.EndTick) { _wendigoActions[id] = default; a = default; }
                if (a.Serial == 0)
                {
                    if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id]) continue;
                    var delta = Entities.Position[PlayerId] - Entities.Position[id];
                    var distance = delta.Length;
                    if (distance < Fix64.Ratio(1, 100)) continue;
                    WendigoAction kind = WendigoAction.None;
                    if (distance >= Fix64.FromInt(3) && distance <= Fix64.FromInt(7)
                        && Tick >= _wendigoNextLeap[id] && WendigoLeapPathClear(id, Entities.Position[PlayerId]))
                        kind = WendigoAction.Leap;
                    else if (distance <= WendigoClawRange) kind = WendigoAction.Claw;
                    if (kind == WendigoAction.None) continue;
                    a = new WendigoActionState(++_wendigoSerial, kind, Tick, Entities.Position[id],
                        Entities.Position[PlayerId], delta.Normalized());
                    _wendigoActions[id] = a; Entities.Facing[id] = a.Direction; Entities.Velocity[id] = FixVec2.Zero;
                    // Пауза не продлевает позу восстановления: в ней можно идти и разворачиваться.
                    Entities.NextAttackTick[id] = a.EndTick + (kind == WendigoAction.Claw ? WendigoClawRestTicks : 0);
                    if (kind == WendigoAction.Leap) _wendigoNextLeap[id] = Tick + WendigoLeapCooldownTicks;
                    _events.Add(new SimEvent(SimEventType.WendigoStarted, id, PlayerId, a.Serial,
                        false, kind == WendigoAction.Leap ? a.Target : a.Origin, actionVariant: (int)kind));
                }
                if (a.HitResolved || Tick < a.ImpactTick) continue;
                _wendigoActions[id] = a.Resolve();
                _events.Add(new SimEvent(SimEventType.WendigoImpact, id, PlayerId, a.Serial,
                    false, a.Kind == WendigoAction.Leap ? a.Target : a.Origin, actionVariant: (int)a.Kind));
                var offset = Entities.Position[PlayerId] - (a.Kind == WendigoAction.Leap ? a.Target : a.Origin);
                var limit = (a.Kind == WendigoAction.Leap ? WendigoLeapRadius : WendigoClawRange) + Entities.BodyRadius[PlayerId];
                bool hit = offset.LengthSq <= limit * limit && (a.Kind == WendigoAction.Leap
                    || FixVec2.WithinArc(a.Direction, offset, WendigoClawCos));
                if (hit && _layout != null)
                {
                    var from = a.Kind == WendigoAction.Leap ? a.Target : a.Origin;
                    var ray = Entities.Position[PlayerId] - from;
                    int steps = Math.Max(1, (ray.Length / Fix64.Ratio(1, 5)).ToInt() + 1);
                    for (int step = 1; step <= steps && hit; step++)
                        hit = _layout.IsWalkable(from + ray * Fix64.Ratio(step, steps), Fix64.Ratio(1, 10));
                }
                if (hit) ApplyAbilityDamage(id, PlayerId, a.Kind == WendigoAction.Leap ? 60 : WendigoClawDamage, -1, DamageType.Physical);
            }
        }

        public EncounterPlan SetupWendigoEncounter(LayoutMap map, ulong seed, bool withPack = false)
        {
            if (map == null) SetupTestArena(0); else SetupRift(map, seed, 0, 0, 420);
            _campWalkMap = null; _events.Clear();
            FindForestBudTestStage(map, out var hero, out var enemy, out int module);
            // Середина поляны удобнее края тропы: кроны не заслоняют телеграф.
            if (map != null)
                for (int m = 1; m < map.PlacedCount; m++)
                {
                    if (map.IsExit(m)) continue;
                    var center = map.CenterOf(m);
                    var axis = new FixVec2(Fix64.FromInt(3), Fix64.Zero);
                    var a = center - axis; var b = center + axis;
                    if (!map.CanTravel(a, b, Fix64.FromInt(2))
                        || !map.CanTravel(center - new FixVec2(Fix64.Zero, Fix64.FromInt(3)),
                            center + new FixVec2(Fix64.Zero, Fix64.FromInt(3)), Fix64.FromInt(2))) continue;
                    hero = a; enemy = b; module = m; break;
                }
            var direction = (enemy - hero).Normalized();
            enemy = hero + direction * Fix64.FromInt(6);
            if (map != null) enemy = map.ClampToWalkable(enemy, WendigoBodyRadius);
            Entities.Position[PlayerId] = hero; Entities.Facing[PlayerId] = direction;
            int id = Entities.Spawn(enemy, 420, Faction.Orvill); ConfigureEnemy(id, EnemyKind.ForestWendigo);
            Entities.Aggro[id] = true; Entities.Facing[id] = -direction; Entities.NextAttackTick[id] = Tick;
            _events.Add(SimEvent.Spawn(id, enemy));
            var elite = new bool[Entities.Capacity]; elite[id] = true; _eliteMask = elite;
            if (withPack)
                for (int n = 0; n < 2; n++)
                {
                    var side = new FixVec2(-direction.Y, direction.X) * Fix64.FromInt(n == 0 ? -2 : 2);
                    var point = enemy + side;
                    if (map != null) point = map.ClampToWalkable(point, Fix64.Ratio(45, 100));
                    int ally = Entities.Spawn(point, 60, Faction.Orvill); ConfigureEnemy(ally, EnemyKind.ForestRootSwarm);
                    Entities.Aggro[ally] = true; _events.Add(SimEvent.Spawn(ally, point));
                }
            Grid.Rebuild(Entities);
            return new EncounterPlan(new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.MainPath,
                module, -1, StableId.Of("encounter.forest-wendigo.test"), enemy, 1, withPack ? 3 : 1) }, elite, Fix64.FromInt(5), 0);
        }

        private void HashWendigo(ref ulong hash)
        {
            bool present = _wendigoSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++) present = Entities.Kind[id] == EnemyKind.ForestWendigo;
            if (!present) return;
            Hashing.Mix(ref hash, 0x57454E44); Hashing.Mix(ref hash, _wendigoSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestWendigo) continue;
                var a = _wendigoActions[id]; Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, _wendigoNextLeap[id]);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Kind); Hashing.Mix(ref hash, a.StartTick);
                Hashing.Mix(ref hash, a.Origin.X.Raw); Hashing.Mix(ref hash, a.Origin.Y.Raw);
                Hashing.Mix(ref hash, a.Target.X.Raw); Hashing.Mix(ref hash, a.Target.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
            }
        }
    }
}
