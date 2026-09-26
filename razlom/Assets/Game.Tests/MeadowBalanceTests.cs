#if UNITY_5_3_OR_NEWER
using System;
using Game.Data;
using Game.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// Баланс v1 на живом ассете Meadow.
    ///
    /// Прежняя проверка «обычный моб умирает за ≤4 базовых удара» описывала
    /// ровно ту беду, от которой ушла стадия 2: мобы гибли в разы быстрее
    /// цели. Теперь здоровье и урон — строка вида в EnemyArchetypes × рост с
    /// глубиной, а мерило — время убийства эталонным героем 5-го уровня
    /// лагеря (270 здоровья, 54 урона, удар раз в 20 тиков) ЦЕЛЫМИ базовыми
    /// ударами, без критов и способностей. Все пять видов вне ассета
    /// проверяет EnemyArchetypeTests.
    ///
    /// С стадии 6 арены ставят шаблоны встреч из плана забега (пачки ассета
    /// в потоке арен не выбираются): здесь — что встаёт на живом ассете на
    /// А1, А4 и А8 по плану каждого сида. Сами шаблоны и план — ArenaEncounterTests.
    /// </summary>
    public sealed class MeadowBalanceTests
    {
        private const int ReferenceCampLevel = 5;
        private const int BossLevel = 9;

        private static LocationDefinition Meadow()
            => Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();

        /// <summary>Полосы времени убийства эталонным героем, секунды целых базовых ударов.</summary>
        private static void KillBand(EnemyKind kind, out double min, out double max)
        {
            switch (kind)
            {
                case EnemyKind.ForestRootSwarm: min = 1; max = 3; break;
                // 550 здоровья (стенд баланса, 26.09): 11 ударов на первой арене, 16 на восьмой.
                case EnemyKind.ForestGuardian: min = 4; max = 12; break;
                case EnemyKind.ForestBud: min = 3; max = 7; break;
                case EnemyKind.ForestStonehoof: min = 6; max = 13; break;
                case EnemyKind.ForestWendigo: min = 20; max = 40; break;
                default: min = 0; max = 0; Assert.Fail("Нет полосы для " + kind); break;
            }
        }

        private static double KillSeconds(Simulation sim, int enemy)
        {
            int damage = sim.Entities.Damage[Simulation.PlayerId];
            int hits = (sim.Entities.MaxHealth[enemy] + damage - 1) / damage;
            return hits * Simulation.PlayerBaseAttackCycleTicks / (double)Simulation.TicksPerSecond;
        }

        /// <summary>Арена level по плану сида: карта уровня и стартовая волна его шаблона.</summary>
        private static EncounterPlan SpawnPlanned(LocationDefinition profile, int level, ulong seed, Simulation sim,
            out ArenaEncounterTemplate template, out LayoutMap map)
        {
            var settings = profile.GetLevel(level);
            template = ArenaRunPlan.Roll(seed, profile).TemplateFor(level);
            Assert.That(template, Is.Not.Null);
            var seeds = RiftLevelSeeds.ForLevel(seed, level);
            settings = settings.WithArenaSize(Math.Max(3, template.MinArenaSize));
            map = new LayoutMap(profile.Modules, profile.MaxModules);
            settings.Generate(new LayoutGenerator(), profile.Modules, map, seeds.Layout);
            return settings.Spawn(sim, map, seeds.Spawns, template, level);
        }

        [Test]
        public void PlannedArenas_GrowWithDepth_WithArchetypeStats_AndAtMostTwoGuardians()
        {
            var profile = Meadow();
            Assert.That(profile.LevelCount, Is.EqualTo(BossLevel), "восемь арен и босс");
            // Мерило роста — здоровье стартовой волны, а не число голов: с
            // глубиной бюджет добирают плотные виды, а не рой.
            long[] population = new long[3];
            int[] levels = { 1, 4, 8 };
            for (int tier = 0; tier < levels.Length; tier++)
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    int level = levels[tier];
                    var settings = profile.GetLevel(level);
                    // EnemyHealth уровня — процент глубины, а не очки.
                    Assert.That(settings.EnemyHealth, Is.EqualTo(EnemyArchetypes.DepthHealthPercent(level)));
                    Assert.That(settings.Encounters.DamagePercent, Is.EqualTo(EnemyArchetypes.DepthDamagePercent(level)));
                    var sim = new Simulation(seed, 512);
                    var plan = SpawnPlanned(profile, level, seed, sim, out var template, out var map);
                    for (int i = 1; i < sim.Entities.Count; i++)
                        Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint) >= Fix64.FromInt(196), Is.True);
                    for (int i = 1; i < sim.Entities.Count; i++) population[tier] += sim.Entities.MaxHealth[i];
                    Assert.That(sim.Entities.MaxHealth[0], Is.EqualTo(150));
                    Assert.That(plan.OmittedEnemies, Is.Zero, template.Key + " seed " + seed);
                    for (int e = 0; e < plan.Count; e++)
                    {
                        var site = plan.Get(e);
                        int guardians = 0;
                        for (int i = site.FirstEntity; i < site.FirstEntity + site.EnemyCount; i++)
                        {
                            var kind = sim.Entities.Kind[i];
                            if (kind == EnemyKind.ForestGuardian) guardians++;
                            var archetype = EnemyArchetypes.Get(kind);
                            // Подстройки групп у шаблонов нет: строка вида × глубина, ровно.
                            Assert.That(sim.Entities.MaxHealth[i], Is.EqualTo(
                                EnemyArchetypes.ScaleHealth(sim.ArchetypeHealth(kind), settings.EnemyHealth)), kind + " seed " + seed);
                            Assert.That(sim.Entities.Damage[i], Is.EqualTo(CombatStats.RoundToInt(Fix64.FromInt(archetype.BaseDamage)
                                * Fix64.Ratio(settings.Encounters.DamagePercent, 100))), kind + " seed " + seed);
                            Assert.That(sim.Entities.BodyRadius[i], Is.EqualTo(sim.ArchetypeBodyRadius(kind)));
                            Assert.That(sim.Entities.CritChance[i], Is.EqualTo(Fix64.Zero), "враги не критуют");
                            // Элита в потоке арен — только Вендиго элитной встречи.
                            Assert.That(plan.IsElite(i), Is.EqualTo(kind == EnemyKind.ForestWendigo), kind + " seed " + seed);
                        }
                        // Правило владельца: не больше двух Хранителей в пачке — волне.
                        Assert.That(guardians, Is.LessThanOrEqualTo(2), template.Key + " seed " + seed);
                    }
                }
            TestContext.WriteLine("Start-wave enemy health across 30 seeds, arenas 1/4/8: " + string.Join(", ", population));
            Assert.That(population[1], Is.GreaterThan(population[0]));
            Assert.That(population[2], Is.GreaterThan(population[1]));
            Assert.That(population[2], Is.GreaterThan(population[0] * 3 / 2));
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(8)]
        public void ReferenceHero_KillTimesAndHitSizesStayInBands(int level)
        {
            var profile = Meadow();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sim = new Simulation(seed, 512);
                sim.SetPlayerLevel(ReferenceCampLevel);
                var plan = SpawnPlanned(profile, level, seed, sim, out _, out _);
                int heroHealth = sim.Entities.MaxHealth[0];
                Assert.That(heroHealth, Is.EqualTo(150 + 4 * Progression.HealthPerLevel));
                Assert.That(sim.Entities.Damage[0], Is.EqualTo(34 + 4 * Progression.DamagePerLevel));
                for (int i = 1; i < sim.Entities.Count; i++)
                {
                    var kind = sim.Entities.Kind[i];
                    KillBand(kind, out double min, out double max);
                    Assert.That(KillSeconds(sim, i), Is.InRange(min, max),
                        kind + (plan.IsElite(i) ? " (elite)" : "") + " at level " + level + ", seed " + seed);
                    int hit = sim.Entities.Damage[i];
                    if (kind == EnemyKind.ForestStonehoof || kind == EnemyKind.ForestWendigo)
                    {
                        // Крупные фигуры (таран, коготь) — по правилу окон 10–16% на
                        // первых аренах; к А8 рост урона выводит коготь Вендиго к 15%.
                        Assert.That(hit * 100, Is.LessThanOrEqualTo(20 * heroHealth), kind + " hit at level " + level);
                        continue;
                    }
                    // Обычный удар моба — не больше 10% эталонного героя даже на
                    // восьмой арене; на первой Хранитель и плод — в окне 4–8%.
                    Assert.That(hit * 100, Is.LessThanOrEqualTo(10 * heroHealth), kind + " hit at level " + level);
                    if (level == 1 && kind != EnemyKind.ForestRootSwarm)
                        Assert.That(hit * 100, Is.InRange(4 * heroHealth, 8 * heroHealth), kind + " hit at level 1");
                }
            }
        }

        [Test]
        public void InterimBoss_HealthAndKillTimeForReferenceHero()
        {
            var profile = Meadow();
            var final = profile.GetLevel(BossLevel);
            Assert.That(final.Boss, Is.True);
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var seeds = RiftLevelSeeds.ForLevel(seed, BossLevel);
                var map = new LayoutMap(profile.Modules, profile.MaxModules);
                final.Generate(new LayoutGenerator(), profile.Modules, map, seeds.Layout);
                var sim = new Simulation(seed, 512);
                sim.SetPlayerLevel(ReferenceCampLevel);
                var plan = final.Spawn(sim, map, seeds.Spawns);
                int boss = plan.BossId;
                Assert.That(sim.Entities.MaxHealth[boss], Is.EqualTo(EnemyArchetypes.ScaleHealth(
                    EnemyArchetypes.InterimBossHealth, final.EnemyHealth)));
                // 6800 × 156% девятого уровня.
                Assert.That(sim.Entities.MaxHealth[boss], Is.EqualTo(10608));
                // Одними базовыми ударами — две-три минуты; с критами и
                // способностями — в окно дока 2:30–3:15 и быстрее.
                Assert.That(KillSeconds(sim, boss), Is.InRange(120.0, 240.0));
            }
        }

        [Test]
        public void DeveloperImmortality_BlocksAttacksAbilitiesAndBurn_AndCanBeDisabled()
        {
            var profile = Meadow();
            var session = new GameSession(1, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            Assert.Throws<InvalidOperationException>(() => session.SetDeveloperInvulnerable(true));
            session.StartDeveloperRift(profile, BossLevel, true, 42);
            var sim = session.Run.Sim;
            int health = sim.Entities.Health[0];
            ulong normalHash = sim.StateHash();
            session.SetDeveloperInvulnerable(true);
            Assert.That(sim.StateHash(), Is.Not.EqualTo(normalHash));
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Physical);
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Fire, overTime: true);
            for (int tick = 0; tick < 600; tick++) session.Step(InputFrame.Empty);
            Assert.That(session.Mode, Is.EqualTo(GameMode.Rift));
            Assert.That(sim.Entities.Alive[0], Is.True);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(health));
            session.SetDeveloperInvulnerable(false);
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Physical);
            Assert.That(sim.Entities.Alive[0], Is.False);
        }

    }
}
#endif
