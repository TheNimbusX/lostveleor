using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class EncounterTests
    {
        // Проценты групп — 100: здоровье и урон дают таблица видов и глубина.
        // Камнекопыт с баланса v1 допущен в пачки (стадия 0 плана).
        private static EncounterSettings Settings(int level, int branchCount = 2)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var swarm = new EncounterGroup(EnemyKind.ForestRootSwarm, 3, 4, growWithDepth: true);
            var stonehoof = new EncounterGroup(EnemyKind.ForestStonehoof, 1, 1);
            return new EncounterSettings(
                new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian, swarm }),
                    new EncounterPack(3, 100, new[] { swarm }), new EncounterPack(4, 100, new[] { guardian, guardian }),
                    new EncounterPack(7, 100, new[] { stonehoof, swarm }) },
                new[] { new EncounterPack(5, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, branchCount, branchCount) }) },
                new[] { new EncounterPack(6, 100, new[] {
                    new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, elite: true), swarm }) },
                Math.Min(5, 3 + (level - 1) / 3), (level - 1) / 4, EnemyArchetypes.DepthDamagePercent(level), Fix64.FromInt(4));
        }

        private static LayoutMap MakeMap(ulong seed, int level)
        {
            var modules = PrototypeContent.Modules();
            var map = new LayoutMap(modules, 64);
            new LayoutGenerator().Generate(modules, seed, map, 10 + level);
            return map;
        }

        [Test]
        public void Packs_FollowRoute_ProtectCaches_AndGuaranteeEliteAtExit()
        {
            int stonehooves = 0;
            foreach (int level in new[] { 1, 5, 10 })
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    var map = MakeMap(seed, level);
                    var sim = new Simulation(seed, 512);
                    EncounterPlan plan = null;
                    int percent = EnemyArchetypes.DepthHealthPercent(level);
                    var settings = Settings(level);
                    Assert.DoesNotThrow(() => plan = sim.SetupEncounters(map, seed, percent, settings), $"seed {seed}, level {level}");
                    int exits = 0, branches = 0;
                    for (int e = 0; e < plan.Count; e++)
                    {
                        var encounter = plan.Get(e);
                        Assert.That(encounter.EnemyCount, Is.GreaterThan(0));
                        if (encounter.Role == EncounterRole.RewardBranch)
                        {
                            branches++;
                            Assert.That(encounter.Module, Is.EqualTo(map.GetRewardBranch(encounter.Branch)));
                        }
                        else Assert.That(map.Routes.IsMainModule(encounter.Module), Is.True);
                        if (encounter.Role == EncounterRole.Introduction) Assert.That(encounter.EnemyCount, Is.EqualTo(1));
                        if (encounter.Role == EncounterRole.ExitGuard)
                        {
                            exits++;
                            Assert.That(map.IsExit(encounter.Module), Is.True);
                            Assert.That(plan.IsElite(encounter.FirstEntity), Is.True);
                            Assert.That(sim.Entities.Kind[encounter.FirstEntity], Is.EqualTo(EnemyKind.ForestGuardian));
                            // Элита — строка вида без надбавки пачки.
                            Assert.That(sim.Entities.MaxHealth[encounter.FirstEntity], Is.EqualTo(EnemyArchetypes.ScaleHealth(
                                EnemyArchetypes.Get(EnemyKind.ForestGuardian).BaseHealth, percent)));
                        }
                    }
                    Assert.That(exits, Is.EqualTo(map.ExitCount));
                    Assert.That(branches, Is.EqualTo(map.RewardBranchCount));
                    for (int i = 1; i < sim.Entities.Count; i++)
                    {
                        // Здоровье, урон и тело — строка вида × глубина.
                        var archetype = EnemyArchetypes.Get(sim.Entities.Kind[i]);
                        if (archetype.Kind == EnemyKind.ForestStonehoof) stonehooves++;
                        Assert.That(sim.Entities.MaxHealth[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(archetype.BaseHealth, percent)));
                        Assert.That(sim.Entities.Damage[i], Is.EqualTo(CombatStats.RoundToInt(Fix64.FromInt(archetype.BaseDamage)
                            * Fix64.Ratio(100 * settings.DamagePercent, 10000))));
                        Assert.That(sim.Entities.BodyRadius[i], Is.EqualTo(archetype.BodyRadius));
                        Assert.That(sim.Entities.CritChance[i], Is.EqualTo(Fix64.Zero));
                        Assert.That(plan.ForEntity(i), Is.GreaterThanOrEqualTo(0));
                        Assert.That(map.IsWalkable(sim.Entities.Position[i], sim.Entities.BodyRadius[i]), Is.True);
                        Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint),
                            Is.GreaterThanOrEqualTo(LayoutRoutes.SafeSpawnRadius * LayoutRoutes.SafeSpawnRadius));
                        for (int j = 1; j < i; j++)
                        {
                            var radius = sim.Entities.BodyRadius[i] + sim.Entities.BodyRadius[j];
                            Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], sim.Entities.Position[j]), Is.GreaterThanOrEqualTo(radius * radius));
                        }
                    }
                }
            Assert.That(stonehooves, Is.GreaterThan(0), "Камнекопыт должен реально вставать в пачки");
        }

        [Test]
        public void Groups_AcceptEveryArchetypeKind_AndRejectUnknownOnes()
        {
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                // Детёныш Расщепеня встаёт только из распада — в пачку его не поставить.
                if (EnemyArchetypes.IsPlaceable(kind))
                    Assert.DoesNotThrow(() => new EncounterGroup(kind, 1, 2), kind.ToString());
                else
                    Assert.Throws<ArgumentException>(() => new EncounterGroup(kind, 1, 2), kind.ToString());
            }
            Assert.Throws<ArgumentException>(() => new EncounterGroup(EnemyKind.None, 1, 1));
            Assert.Throws<ArgumentException>(() => new EncounterGroup((EnemyKind)99, 1, 1));
        }

        [Test]
        public void RequiredFightsUnlockExit_WithoutKillingCacheGuards()
        {
            var modules = PrototypeContent.Modules();
            var location = new LocationDefinition(1, modules,
                new[] { new RiftLevelSettings(16, 1, 1, 2, 0, 0, 150, Settings(1)) });
            var run = new RiftRun(new Simulation(17, 512), modules, PrototypeContent.Items(), PrototypeContent.ItemBaseIds(), location: location);
            run.StartRun();
            Assert.That(run.Encounters, Is.Not.Null);
            for (int e = 0; e < run.Encounters.Count; e++)
            {
                var encounter = run.Encounters.Get(e);
                if (encounter.Role == EncounterRole.RewardBranch) continue;
                for (int i = 0; i < encounter.EnemyCount; i++) run.Sim.Entities.Alive[encounter.FirstEntity + i] = false;
            }
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0));
            run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            Assert.That(run.CountRequiredEnemies(), Is.GreaterThan(0));
        }


        /// <summary>
        /// Босс встаёт на пол. Поляна босса (02.10) неизменна, и её центр —
        /// центр комнаты перед выходом: босс стоит ровно посреди поляны, герой —
        /// в тропе входа под ней. Запасной путь (центр комнаты за контуром —
        /// сессия с сидом 10 ставила босса в (−30, 35), вне пола) проверяется
        /// той же картой с вырезанным под центром полом: тогда — ближайшая
        /// клетка маршрута, до которой можно дойти от входа.
        /// </summary>
        [Test]
        public void BossArena_PutsTheBossOnTheFloor_EvenWhenTheRoomCentreIsOffTheGlade()
        {
            var modules = PrototypeContent.Modules();
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                new[] { new EncounterPack(4, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, elite: true) }) },
                1, 0, 100, Fix64.FromInt(5));
            var level = new RiftLevelSettings(20, 1, 1, 2, 4, 8, 100, settings, boss: true, playerHealth: 150,
                entryClearance: 14, solidEnvironment: true, naturalGlade: true).WithArenaSize(4);
            for (ulong seed = 1; seed <= 60; seed++)
            {
                foreach (bool thicket in new[] { true, false })
                {
                    var seeds = RiftLevelSeeds.ForLevel(seed, 9);
                    var map = new LayoutMap(modules, 64);
                    level.Generate(new LayoutGenerator(), modules, map, seeds.Layout);
                    var glade = map.GetGlade(0);
                    var centre = map.CenterOf(map.GetPlaced(map.GetExit(0)).Parent);
                    Assert.That(centre, Is.EqualTo(glade.Center), "сид " + seed);
                    bool hole = seed % 3 == 0;
                    if (hole)
                    {
                        // Пол под центром вырезан (как озеро прежней поляны): босс уходит на ближайшую клетку.
                        var floor = map.Outline;
                        map.SetNaturalOutline(p => floor.Contains(p) && FixVec2.DistanceSq(p, centre) > Fix64.FromInt(4));
                        map.BuildRoutes();
                        map.Routes.MarkMainRoutes(map);
                    }
                    var sim = new Simulation(seed, 512) { ThicketMasterBossEnabled = thicket };
                    var plan = level.Spawn(sim, map, seeds.Spawns);
                    var boss = sim.Entities.Position[plan.BossId];
                    var radius = sim.Entities.BodyRadius[plan.BossId];
                    string where = "сид " + seed + (thicket ? ", Хозяин Чащи" : ", Хранитель");
                    Assert.That(sim.Entities.Kind[plan.BossId], Is.EqualTo(thicket ? EnemyKind.ForestThicketMaster : EnemyKind.ForestGuardian));
                    Assert.That(map.IsWalkable(boss, radius), Is.True, "босс вне пола, " + where);
                    Assert.That(sim.Entities.Position[Simulation.PlayerId], Is.EqualTo(map.EntryPoint), where);
                    Assert.That(map.EntryPoint.Y, Is.LessThan(glade.Center.Y - GladeLayout.BossFloorHalfDepth), "герой в тропе входа, " + where);
                    if (!hole)
                    {
                        Assert.That(boss, Is.EqualTo(centre), "босс посреди поляны, " + where);
                        continue;
                    }
                    Assert.That(map.IsWalkable(centre, radius), Is.False, where);
                    int cell = map.Routes.CellAt(boss);
                    Assert.That(cell, Is.GreaterThanOrEqualTo(0), where);
                    Assert.That(map.Routes.DistanceFromEntry(cell), Is.GreaterThanOrEqualTo(0), "до босса не дойти, " + where);
                    Assert.That(FixVec2.Distance(boss, centre), Is.LessThan(Fix64.FromInt(6)), where);
                    Assert.That(glade.Field(boss), Is.LessThanOrEqualTo(Fix64.One), "запасная точка — на поляне, " + where);
                }
            }
        }

        /// <summary>
        /// Малая поляна босса (20 × 15 м) вмещает всё, что ставится вокруг него:
        /// тестовый переход «К боссу» — герой на поляне не ближе 4 м, подмога на
        /// 66% — на полу поляны, не ближе 6 м к герою, вся волна.
        /// </summary>
        [Test]
        public void BossArena_TestJumpAndAdds_FitTheSmallClearing()
        {
            var location = ArenaEncounterTests.ForestLocation();
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                    PrototypeContent.ItemBaseIds(), location: location);
                run.StartTestAtLevel(9, true);
                var sim = run.Sim;
                var e = sim.Entities;
                var glade = run.Map.GetGlade(0);
                int boss = run.BossId;
                string where = "сид " + seed;
                Assert.That(e.Position[boss], Is.EqualTo(glade.Center), where);
                var hero = e.Position[Simulation.PlayerId];
                Assert.That(glade.Field(hero), Is.LessThanOrEqualTo(Fix64.One), "герой на поляне, " + where);
                Assert.That(FixVec2.Distance(hero, e.Position[boss]), Is.GreaterThanOrEqualTo(Fix64.FromInt(4)), where);

                sim.PlayerInvulnerable = true;
                e.Health[boss] = e.MaxHealth[boss] * 66 / 100;
                int before = e.Count;
                run.Step(InputFrame.Empty);
                Assert.That(sim.BossAddWavesSpawned, Is.EqualTo(1), where);
                Assert.That(e.Count - before, Is.GreaterThanOrEqualTo(5), "волна целиком, " + where);
                for (int i = before; i < e.Count; i++)
                {
                    Assert.That(glade.Field(e.Position[i]), Is.LessThanOrEqualTo(Fix64.One), "подмога на поляне, " + where);
                    Assert.That(run.Map.IsWalkable(e.Position[i], e.BodyRadius[i]), Is.True, where);
                    Assert.That(FixVec2.DistanceSq(e.Position[i], e.Position[Simulation.PlayerId]),
                        Is.GreaterThanOrEqualTo(Fix64.FromInt(36)), where);
                }
            }
        }

        // Правило владельца (26.09): в одной пачке не больше двух лесных хранителей.
        [Test]
        public void Pack_HoldsAtMostTwoForestGuardians()
        {
            Assert.DoesNotThrow(() => new EncounterPack(1, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2) }));
            Assert.Throws<ArgumentException>(() => new EncounterPack(1, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 3) }));
            Assert.Throws<ArgumentException>(() => new EncounterPack(1, 100, new[] {
                new EncounterGroup(EnemyKind.ForestGuardian, 1, 2), new EncounterGroup(EnemyKind.None, 0, 1) }));
            Assert.Throws<ArgumentException>(() => new EncounterPack(1, 100, new[] {
                new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, growWithDepth: true) }));
        }
    }
}
