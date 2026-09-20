using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public sealed partial class Simulation
    {
        // Десять мест на владельца покрывают два перекрывающихся залпа даже при ускорении атаки.
        private const int ForestFruitSlotsPerEnemy = 10;
        private readonly ForestBudAttackState[] _forestBudAttacks;
        private readonly ForestFruitState[] _forestFruits;
        private int _forestSerial, _forestFruitHighWater, _forestFruitActiveCount;
        public ForestBudSettings ForestBudConfig { get; }
        public int ForestFruitCapacity => _forestFruits.Length;
        public int ForestFruitActiveCount => _forestFruitActiveCount;

        public bool TryGetForestFruit(int slot, out ForestFruitState state)
        {
            state = (uint)slot < (uint)_forestFruits.Length ? _forestFruits[slot] : default;
            return state.Serial != 0;
        }

        public bool TryGetForestBudAttack(int entity, out ForestBudAttackState state)
        {
            state = (uint)entity < (uint)_forestBudAttacks.Length ? _forestBudAttacks[entity] : default;
            return state.Serial != 0;
        }

        private void ResetForestBud()
        {
            Array.Clear(_forestBudAttacks, 0, _forestBudAttacks.Length);
            Array.Clear(_forestFruits, 0, _forestFruits.Length);
            _forestSerial = _forestFruitHighWater = _forestFruitActiveCount = 0;
        }

        private void ConfigureForestBud(int id)
        {
            var config = ForestBudConfig;
            Entities.BodyRadius[id] = config.BodyRadius;
            Entities.PushWeight[id] = Fix64.One;
            var sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, Fix64.FromInt(config.Damage));
            sheet.SetBase(StatType.AttackSpeed, Fix64.Ratio(TicksPerSecond, config.AttackCooldownTicks));
            sheet.SetBase(StatType.MoveSpeed, config.MoveSpeed);
            sheet.SetBase(StatType.CritChance, Fix64.Zero);
            sheet.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
        }

        private void MoveForestBud(int id, FixVec2 toPlayer)
        {
            if (_forestBudAttacks[id].Serial != 0)
            { Entities.Velocity[id] = FixVec2.Zero; return; }
            var config = ForestBudConfig;
            Fix64 speed = Entities.MoveStep[id];
            Fix64 distance = toPlayer.Length;
            Fix64 rearRange = config.RetreatRange;
            var awayDirection = distance.Raw > 0 ? -toPlayer.Normalized() : -Entities.Facing[id];
            FixVec2 bypass = FixVec2.Zero;
            // Держим ближников между героем и бутоном, но не уходим за дальность залпа.
            for (int ally = 1; ally < Entities.Count; ally++)
            {
                if (ally == id || !Entities.Alive[ally] || Entities.Kind[ally] == EnemyKind.ForestBud) continue;
                var relative = Entities.Position[ally] - Entities.Position[PlayerId];
                var ahead = FixVec2.Dot(relative, awayDirection);
                var lateral = relative - awayDirection * ahead;
                if (ahead <= Fix64.Zero || ahead > config.PreferredRange || lateral.LengthSq > Fix64.FromInt(9)) continue;
                rearRange = Fix64.Max(rearRange, Fix64.Min(config.PreferredRange, ahead + Fix64.FromInt(2)));
                var clearance = Entities.BodyRadius[id] + Entities.BodyRadius[ally] + Fix64.Ratio(3, 10);
                if (ahead > distance - Fix64.Ratio(1, 2) && ahead < distance + Fix64.FromInt(2)
                    && lateral.LengthSq < clearance * clearance && bypass.LengthSq == Fix64.Zero)
                {
                    var side = new FixVec2(-awayDirection.Y, awayDirection.X);
                    Fix64 sign = FixVec2.Dot(lateral, side);
                    bypass = side * (sign > Fix64.Zero || (sign == Fix64.Zero && (id & 1) == 0)
                        ? -Fix64.One : Fix64.One);
                }
            }
            FixVec2 wanted = FixVec2.Zero;
            if (distance > config.PreferredRange)
            {
                Fix64 ramp = Fix64.Min(Fix64.One, (distance - config.PreferredRange) / Fix64.FromInt(2));
                wanted = toPlayer.Normalized() * (speed * ramp);
            }
            else if (distance < rearRange)
            {
                var away = distance.Raw > 0 ? -toPlayer.Normalized() : -Entities.Facing[id];
                // Обходим прикрытие сбоку, иначе два тела толкаются назад и стрелок остаётся впереди.
                wanted = (away + bypass * Fix64.Ratio(3, 2)).Normalized() * speed;
            }
            Entities.Velocity[id] = Approach(Entities.Velocity[id], wanted, speed);
            FixVec2 from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, Entities.Velocity[id]);
            if (Entities.Position[id].Equals(from)) Entities.Velocity[id] = FixVec2.Zero;
        }

        private void UpdateForestBud()
        {
            var config = ForestBudConfig;
            // Сначала завершаем уже летевшие плоды, освобождая места для нового выстрела этого тика.
            for (int slot = 0; slot < _forestFruitHighWater; slot++)
            {
                var fruit = _forestFruits[slot];
                if (fruit.Serial == 0 || Tick < fruit.ImpactTick) continue;
                _forestFruits[slot] = default;
                _forestFruitActiveCount--;
                _events.Add(new SimEvent(SimEventType.ForestFruitImpact, fruit.Source, PlayerId,
                    slot, false, fruit.Target, actionVariant: fruit.ShotIndex));
                // Радиус описывает реальный диск поражения; тело героя должно полностью выйти из него.
                Fix64 limit = fruit.Radius + Entities.BodyRadius[PlayerId];
                if (Entities.Alive[PlayerId] &&
                    (Entities.Position[PlayerId] - fruit.Target).LengthSq <= limit * limit)
                    ApplyAbilityDamage(fruit.Source, PlayerId, fruit.Damage, -1, DamageType.Physical);
            }

            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestBud) continue;
                var attack = _forestBudAttacks[id];
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick))
                {
                    if (attack.Serial != 0)
                        _events.Add(new SimEvent(SimEventType.ForestBudVolleyCancelled, id, PlayerId,
                            attack.Serial, false, Entities.Position[id], actionVariant: attack.ShotsFired));
                    _forestBudAttacks[id] = default;
                    continue;
                }
                if (attack.Serial != 0 && Tick >= attack.EndTick)
                { _forestBudAttacks[id] = default; attack = default; }
                if (attack.Serial == 0)
                {
                    if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id]) continue;
                    FixVec2 delta = Entities.Position[PlayerId] - Entities.Position[id];
                    if (delta.LengthSq > config.AttackRange * config.AttackRange) continue;
                    if (!FixVec2.WithinArc(Entities.Facing[id], delta, AttackCommitCos)) continue;
                    int first = Tick + config.WindupTicks;
                    attack = new ForestBudAttackState(++_forestSerial, Tick, first, Tick + config.ActionTicks, 0);
                    _forestBudAttacks[id] = attack;
                    Entities.NextAttackTick[id] = Tick + Math.Max(config.ActionTicks, Entities.AttackCooldown[id]);
                    Entities.Velocity[id] = FixVec2.Zero;
                    _events.Add(new SimEvent(SimEventType.ForestBudVolleyStarted, id, PlayerId,
                        attack.Serial, false, Entities.Position[id]));
                }
                if (attack.ShotsFired >= config.ShotCount ||
                    Tick < attack.FirstShotTick + attack.ShotsFired * config.ShotIntervalTicks) continue;
                // Уход за десять метров прерывает оставшиеся выстрелы; уже выпущенные плоды сохраняют цель.
                if ((Entities.Position[PlayerId] - Entities.Position[id]).LengthSq > config.AttackRange * config.AttackRange)
                {
                    _events.Add(new SimEvent(SimEventType.ForestBudVolleyCancelled, id, PlayerId,
                        attack.Serial, false, Entities.Position[id], actionVariant: attack.ShotsFired));
                    _forestBudAttacks[id] = default;
                    continue;
                }
                LaunchForestFruit(id, attack.ShotsFired);
                _forestBudAttacks[id] = attack.WithShot();
            }
        }

        private void LaunchForestFruit(int source, int shotIndex)
        {
            int firstSlot = source * ForestFruitSlotsPerEnemy;
            int slot = firstSlot;
            while (slot < firstSlot + ForestFruitSlotsPerEnemy && _forestFruits[slot].Serial != 0) slot++;
            if (slot == firstSlot + ForestFruitSlotsPerEnemy)
                throw new InvalidOperationException("Forest fruit pool capacity invariant failed.");
            var config = ForestBudConfig;
            // Цель берётся заново для каждого плода; после этого ни движение героя, ни смерть стрелка её не меняют.
            _forestFruits[slot] = new ForestFruitState(++_forestSerial, source, shotIndex, Tick,
                Tick + config.FlightTicks, Entities.Position[source], Entities.Position[PlayerId],
                config.ImpactRadius, Entities.Damage[source]);
            _forestFruitHighWater = Math.Max(_forestFruitHighWater, slot + 1);
            _forestFruitActiveCount++;
            _events.Add(new SimEvent(SimEventType.ForestFruitLaunched, source, PlayerId, slot,
                false, Entities.Position[PlayerId], actionVariant: shotIndex));
        }

        private void HashForestBud(ref ulong hash)
        {
            bool hasBud = _forestSerial != 0;
            for (int id = 1; id < Entities.Count && !hasBud; id++) hasBud = Entities.Kind[id] == EnemyKind.ForestBud;
            if (!hasBud) return;
            Hashing.Mix(ref hash, 0x425544);
            ForestBudConfig.HashInto(ref hash);
            Hashing.Mix(ref hash, _forestSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                var attack = _forestBudAttacks[id];
                if (attack.Serial == 0) continue;
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, attack.Serial);
                Hashing.Mix(ref hash, attack.StartTick); Hashing.Mix(ref hash, attack.FirstShotTick);
                Hashing.Mix(ref hash, attack.EndTick); Hashing.Mix(ref hash, attack.ShotsFired);
            }
            for (int slot = 0; slot < _forestFruitHighWater; slot++)
            {
                var fruit = _forestFruits[slot];
                if (fruit.Serial == 0) continue;
                Hashing.Mix(ref hash, slot); Hashing.Mix(ref hash, fruit.Serial); Hashing.Mix(ref hash, fruit.Source);
                Hashing.Mix(ref hash, fruit.ShotIndex); Hashing.Mix(ref hash, fruit.LaunchTick);
                Hashing.Mix(ref hash, fruit.ImpactTick); Hashing.Mix(ref hash, fruit.Damage);
                Hashing.Mix(ref hash, fruit.Origin.X); Hashing.Mix(ref hash, fruit.Origin.Y);
                Hashing.Mix(ref hash, fruit.Target.X); Hashing.Mix(ref hash, fruit.Target.Y);
                Hashing.Mix(ref hash, fruit.Radius);
            }
        }

        /// <summary>Один и тот же настоящий бой для F8, игровых съёмок и тестов.</summary>
        public EncounterPlan SetupForestBudEncounter(LayoutMap map, ulong spawnSeed, int count = 1)
        {
            if (count < 1 || count >= Entities.Capacity) throw new ArgumentOutOfRangeException(nameof(count));
            if (map == null) SetupTestArena(0);
            else SetupRift(map, spawnSeed, 0, 0, ForestBudConfig.Health);
            _campWalkMap = null;
            _events.Clear();
            FindForestBudTestStage(map, out FixVec2 hero, out FixVec2 enemy, out int module);
            Entities.Position[PlayerId] = hero;
            Entities.Facing[PlayerId] = (enemy - hero).Normalized();
            var side = new FixVec2(-Entities.Facing[PlayerId].Y, Entities.Facing[PlayerId].X);
            for (int n = 0; n < count; n++)
            {
                var point = enemy + side * Fix64.Ratio((n % 5 - Math.Min(4, count - 1) / 2) * 16, 10)
                    + Entities.Facing[PlayerId] * Fix64.Ratio(n / 5 * 16, 10);
                if (map != null) point = map.ClampToWalkable(point, ForestBudConfig.BodyRadius);
                int id = Entities.Spawn(point, ForestBudConfig.Health, Faction.Orvill);
                ConfigureEnemy(id, EnemyKind.ForestBud);
                Entities.Facing[id] = (hero - point).Normalized();
                Entities.Aggro[id] = true;
                Entities.NextAttackTick[id] = Tick + TicksPerSecond + n * ForestBudConfig.ShotIntervalTicks;
                _events.Add(SimEvent.Spawn(id, point));
            }
            Grid.Rebuild(Entities);
            var elite = new bool[Entities.Capacity];
            _eliteMask = elite;
            return new EncounterPlan(new List<EncounterPlacement> { new EncounterPlacement(
                EncounterRole.MainPath, module, -1, StableId.Of("encounter.forest-bud.test"),
                enemy, 1, count) }, elite, Fix64.FromInt(5), 0);
        }

        private void FindForestBudTestStage(LayoutMap map, out FixVec2 hero, out FixVec2 enemy, out int module)
        {
            hero = FixVec2.Zero; enemy = new FixVec2(Fix64.FromInt(6), Fix64.Zero); module = 0;
            if (map == null) return;
            // Длинная площадка нужна и для залпа, и для отхода: моб не должен начинать у края карты.
            for (int m = 1; m < map.PlacedCount; m++)
            {
                if (map.IsExit(m)) continue;
                var center = map.CenterOf(m);
                for (int axis = 0; axis < 4; axis++)
                {
                    var direction = axis % 2 == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : new FixVec2(Fix64.Zero, Fix64.One);
                    if (axis >= 2) direction = -direction;
                    var a = center - direction * Fix64.Ratio(19, 4);
                    var b = a + direction * Fix64.FromInt(6);
                    if (!ForestBudTestStageHasRoom(map, a, b)) continue;
                    hero = a; enemy = b; module = m; return;
                }
            }
            // У природной комнаты геометрический центр может оказаться вне тропы.
            // В таком случае берём две настоящие клетки маршрута с нужной дистанцией.
            if (map.Routes != null)
                for (int pass = 0; pass < 2; pass++)
                    for (int a = 0; a < map.Routes.CellCount; a++)
                    {
                        var cell = map.Routes.GetCell(a);
                        if (map.IsExit(cell.Module) || !map.IsWalkable(cell.Center, ForestBudConfig.BodyRadius)) continue;
                        if (FixVec2.DistanceSq(cell.Center, map.EntryPoint) < Fix64.FromInt(16)) continue;
                        int best = -1; Fix64 bestError = Fix64.MaxValue;
                        for (int b = 0; b < map.Routes.CellCount; b++)
                        {
                            var candidate = map.Routes.GetCell(b);
                            if (pass == 0 && candidate.Module != cell.Module) continue;
                            if (map.IsExit(candidate.Module) || !map.IsWalkable(candidate.Center, ForestBudConfig.BodyRadius)) continue;
                            Fix64 distance = FixVec2.DistanceSq(cell.Center, candidate.Center);
                            if (distance < Fix64.Ratio(121, 4) || distance > Fix64.Ratio(225, 4)) continue;
                            Fix64 error = Fix64.Abs(distance - Fix64.FromInt(36));
                            if (error >= bestError) continue;
                            if (!ForestBudTestStageHasRoom(map, cell.Center, candidate.Center)) continue;
                            bestError = error; best = b;
                        }
                        if (best < 0) continue;
                        hero = cell.Center; enemy = map.Routes.GetCell(best).Center; module = cell.Module; return;
                    }
            throw new InvalidOperationException("The selected map has no safe ranged Forest Bud test stage.");
        }

        private bool ForestBudTestStageHasRoom(LayoutMap map, FixVec2 hero, FixVec2 enemy)
        {
            var direction = (enemy - hero).Normalized();
            var retreat = enemy + direction * Fix64.Ratio(7, 2);
            var side = new FixVec2(-direction.Y, direction.X);
            Fix64 clearance = ForestBudConfig.BodyRadius + Fix64.Ratio(1, 2);
            // Три дорожки дают два метра поперечного манёвра; каждая учитывает тело и запас 0,5 м.
            for (int lane = -1; lane <= 1; lane++)
            {
                var offset = side * Fix64.FromInt(lane);
                if (!map.IsWalkable(hero + offset, clearance)
                    || !map.CanTravel(hero + offset, retreat + offset, clearance)) return false;
            }
            return true;
        }
    }
}
