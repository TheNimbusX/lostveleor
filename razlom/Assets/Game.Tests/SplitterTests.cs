using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Расщепень (план новых мобов леса от 26.09): настоящая смерть — ровно два
    /// детёныша на 12-м тике смерти, по бокам от родителя, со здоровьем и уроном от
    /// него, 5 опыта, не элита; выброс на метр за 8 тиков и 15 тиков без укуса.
    /// Уход в землю, Alive = false и смерть детёныша не делят. Волна «0 живых»
    /// и зачистка забега не видят ложного нуля, дети стража тайника остаются
    /// стражами тайника. Всё — одинаково от прогона к прогону.
    /// </summary>
    public sealed class SplitterTests
    {
        private static Simulation Stand(ulong seed = 11, int count = 1, int arena = 1, int hard = 100,
            Fix64 distance = default, int capacity = 64)
        {
            var sim = new Simulation(seed, capacity);
            sim.SetupKindTestArena(EnemyKind.ForestSplitter, count, null, seed, arena, hard, distance);
            sim.PlayerInvulnerable = true;
            return sim;
        }

        /// <summary>
        /// Смертельный поджиг: моб умрёт в TickBurning следующего шага — посреди
        /// тика и через Kill, как от настоящего удара.
        /// </summary>
        private static void Doom(Simulation sim, int id)
            => sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, Simulation.PlayerId, -1);

        /// <summary>Настоящая смерть между шагами — тоже через Kill.</summary>
        private static void KillNow(Simulation sim, int id)
            => sim.ApplyAbilityDamage(Simulation.PlayerId, id, 1000000, -1, DamageType.Physical);

        private static void ReleasePending(Simulation sim)
        {
            int deadline = sim.Tick + Simulation.SplitterDeathReleaseTicks + 1;
            while (sim.HasPendingSplits && sim.Tick <= deadline) sim.Step(InputFrame.Empty);
            Assert.That(sim.HasPendingSplits, Is.False, "queued split must release once");
        }

        private static int Count(Simulation sim, SimEventType type)
        {
            int n = 0;
            foreach (var e in sim.Events) if (e.Type == type) n++;
            return n;
        }

        /// <summary>Лесная арена по шаблону, как у ArenaEncounterTests: поляна размера 3 с тропами и выходом.</summary>
        private static Simulation Arena(ArenaEncounterTemplate template, ulong seed, int arena = 3)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                new[] { new EncounterPack(4, 100, new[] { guardian }) },
                1, 0, EnemyArchetypes.DepthDamagePercent(arena), Fix64.FromInt(5));
            var level = new RiftLevelSettings(10 + arena, 1, 1, 2, 1, 3, EnemyArchetypes.DepthHealthPercent(arena), settings,
                playerHealth: 150, entryClearance: 14, solidEnvironment: true, naturalGlade: true, arenaSize: 3);
            var modules = PrototypeContent.Modules();
            var map = new LayoutMap(modules, 64);
            level.Generate(new LayoutGenerator(), modules, map, seed);
            var sim = new Simulation(seed, 512);
            level.Spawn(sim, map, seed ^ 0x5151UL, template, arena);
            sim.PlayerInvulnerable = true;
            return sim;
        }

        // Урок распада в миниатюре: один Расщепень, потом волна «0 живых».
        private static ArenaEncounterTemplate SplitThenWave() => new ArenaEncounterTemplate("forest.T-split-wave",
            ArenaEncounterType.Normal, 1, 8, 2, EnemyKind.ForestSplitter, new[]
            {
                new EncounterWave(WaveTrigger.Start, new WaveGroup(EnemyKind.ForestSplitter, 1, 1, WavePlacement.Front)),
                new EncounterWave(WaveTrigger.AliveAtMost(0), new WaveGroup(EnemyKind.ForestGuardian, 1, 1, WavePlacement.Front)),
            });

        private static ArenaEncounterTemplate SplitSurvival() => new ArenaEncounterTemplate("forest.T-split-survival",
            ArenaEncounterType.Survival, 1, 8, 2, EnemyKind.None, new[]
            {
                new EncounterWave(WaveTrigger.Start, new WaveGroup(EnemyKind.ForestSplitter, 2, 2, WavePlacement.Front)),
            }, survivalTicks: 60);

        // ---------- распад ----------

        [Test]
        public void TrueDeath_SpawnsExactlyTwoChildren_OnReleaseTick_BesideTheParent()
        {
            var sim = Stand();
            Assert.That(Simulation.SplitChildren, Is.EqualTo(2));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);

            Assert.That(sim.Entities.Alive[1], Is.False);
            Assert.That(Count(sim, SimEventType.Death), Is.EqualTo(1));
            Assert.That(sim.PendingSplitCount, Is.EqualTo(1));
            for (int t = sim.Tick; t <= Simulation.SplitterDeathReleaseTicks; t++)
            {
                Assert.That(sim.Entities.Count, Is.EqualTo(2), "no hidden live children before the crack");
                Assert.That(Count(sim, SimEventType.SplitterSplit), Is.Zero);
                sim.Step(InputFrame.Empty);
            }
            Assert.That(sim.HasPendingSplits, Is.False);
            Assert.That(sim.Tick - 1, Is.EqualTo(Simulation.SplitterDeathReleaseTicks));
            Assert.That(sim.Entities.Count, Is.EqualTo(2 + Simulation.SplitChildren));
            Assert.That(Count(sim, SimEventType.SplitterSplit), Is.EqualTo(1));
            Assert.That(Count(sim, SimEventType.Spawn), Is.EqualTo(2));
            var at = sim.Entities.Position[1];
            foreach (var e in sim.Events)
            {
                if (e.Type == SimEventType.SplitterSplit)
                {
                    Assert.That(e.Source, Is.EqualTo(1));
                    Assert.That(e.Target, Is.EqualTo(2), "дети идут подряд сразу за последним");
                    Assert.That(e.Amount, Is.EqualTo(2));
                    Assert.That(e.Position, Is.EqualTo(at), "где умер родитель");
                }
                if (e.Type == SimEventType.Spawn)
                {
                    Assert.That(e.Target, Is.InRange(2, 3));
                    Assert.That(e.Flag, Is.False, "не выход из-под земли");
                }
            }

            var facing = sim.Entities.Facing[1];
            var side = new FixVec2(-facing.Y, facing.X);
            for (int c = 2; c <= 3; c++)
            {
                Assert.That(sim.Entities.Kind[c], Is.EqualTo(EnemyKind.ForestSplitling));
                Assert.That(sim.Entities.Alive[c], Is.True);
                Assert.That(sim.Entities.Side[c], Is.EqualTo(Faction.Orvill));
                Assert.That(sim.Entities.Aggro[c], Is.True, "герой уже в бою — дети не ждут обнаружения");
                Assert.That(sim.SplitParentOf(c), Is.EqualTo(1));
                Assert.That((sim.Entities.Position[c] - at).Length.ToDouble(), Is.EqualTo(0.1).Within(0.002));
                // Первый — влево от взгляда родителя, второй — вправо.
                Assert.That(FixVec2.Dot(sim.Entities.Position[c] - at, side).ToDouble(),
                    Is.EqualTo(c == 2 ? 0.1 : -0.1).Within(0.002));
            }
            Assert.That(sim.SplitParentOf(1), Is.EqualTo(-1));
            Assert.That(sim.SplitParentOf(0), Is.EqualTo(-1));
            Assert.That(sim.CountAliveEnemies(), Is.EqualTo(2));
        }

        [Test]
        public void Children_TakeHealthAndDamageFromTheParent_FiveXp_NeverElite()
        {
            // Шестая арена на «Сложно»: 560 × 135% × 125% = 945, урон 12 × 140% × 125% = 21.
            var sim = Stand(arena: 6, hard: EnemyArchetypes.HardRoutePercent);
            int parentHealth = sim.Entities.MaxHealth[1], parentDamage = sim.Entities.Damage[1];
            Assert.That(parentHealth, Is.EqualTo(945));
            Assert.That(parentDamage, Is.EqualTo(21));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
            ReleasePending(sim);

            for (int c = 2; c <= 3; c++)
            {
                // 945 × 160 / 560 = 270; 21 × 4 / 12 = 7.
                Assert.That(sim.Entities.MaxHealth[c], Is.EqualTo(270));
                Assert.That(sim.Entities.MaxHealth[c], Is.EqualTo(EnemyArchetypes.Share(parentHealth,
                    EnemyArchetypes.SplitlingHealth, EnemyArchetypes.SplitterHealth)));
                Assert.That(sim.Entities.Health[c], Is.EqualTo(sim.Entities.MaxHealth[c]));
                Assert.That(sim.Entities.Damage[c], Is.EqualTo(7));
                Assert.That(sim.Entities.Damage[c], Is.EqualTo(EnemyArchetypes.Share(parentDamage,
                    EnemyArchetypes.SplitlingDamage, EnemyArchetypes.SplitterDamage)));
                Assert.That(sim.Entities.XpReward[c], Is.EqualTo(Simulation.SplitlingKillXp));
                Assert.That(Simulation.SplitlingKillXp, Is.EqualTo(5));
                Assert.That(sim.IsElite(c), Is.False);
                Assert.That(sim.Entities.BodyRadius[c], Is.EqualTo(EnemyArchetypes.SplitlingBodyRadius));
                Assert.That(sim.Entities.CritChance[c], Is.EqualTo(Fix64.Zero));
            }

            // На первой арене — ровно строка таблицы детёныша.
            var plain = Stand();
            Doom(plain, 1);
            plain.Step(InputFrame.Empty);
            ReleasePending(plain);
            Assert.That(plain.Entities.MaxHealth[2], Is.EqualTo(EnemyArchetypes.SplitlingHealth));
            Assert.That(plain.Entities.Damage[2], Is.EqualTo(EnemyArchetypes.SplitlingDamage));
        }

        [Test]
        public void Children_PopOneMetreSidewaysOverEightTicks()
        {
            var sim = Stand();
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
            ReleasePending(sim);
            var at = sim.Entities.Position[1];
            var facing = sim.Entities.Facing[1];
            var side = new FixVec2(-facing.Y, facing.X);
            var targets = new FixVec2[2];
            for (int k = 0; k < 2; k++)
            {
                int c = 2 + k;
                Assert.That(sim.Entities.ForcedKind[c], Is.EqualTo((byte)ForcedMotionKind.SplitPop));
                Assert.That(sim.Entities.ForcedTicksLeft[c], Is.EqualTo(Simulation.SplitPopTicks));
                Assert.That(ForcedMotion.IsInterrupting(sim.Entities, c), Is.False, "выброс — не помеха укусу");
                targets[k] = sim.Entities.ForcedTarget[c];
                Assert.That((targets[k] - sim.Entities.Position[c]).Length.ToDouble(), Is.EqualTo(1.0).Within(0.002));
                Assert.That(FixVec2.Dot(targets[k] - at, side).ToDouble(), Is.EqualTo(k == 0 ? 1.1 : -1.1).Within(0.003));
                // Оглушение гасит собственный ход — остаётся только выброс.
                sim.Statuses.ApplyStun(c, sim.Tick + 30);
            }
            for (int t = 1; t <= Simulation.SplitPopTicks; t++)
            {
                sim.Step(InputFrame.Empty);
                for (int k = 0; k < 2; k++)
                    Assert.That(sim.Entities.ForcedTicksLeft[2 + k], Is.EqualTo(Simulation.SplitPopTicks - t), "tick " + t);
            }
            for (int k = 0; k < 2; k++)
                Assert.That(sim.Entities.Position[2 + k], Is.EqualTo(targets[k]), "за 8 тиков ровно на метр");
        }

        [Test]
        public void Children_HoldTheBiteForFifteenTicks_ThenBite()
        {
            // Расщепень вплотную: дети рождаются на дистанции укуса и лицом к
            // герою — без паузы укусили бы на следующем же тике.
            var sim = Stand(distance: Fix64.Ratio(6, 5));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
            ReleasePending(sim);
            int split = sim.Tick - 1;
            Assert.That(sim.Entities.NextAttackTick[2], Is.EqualTo(split + Simulation.SplitlingSpawnGuardTicks));
            Assert.That((sim.Entities.Position[2] - sim.Entities.Position[0]).Length.ToDouble(),
                Is.LessThan(Simulation.SplitlingBiteRange.ToDouble()));

            int firstBite = -1;
            while (sim.Tick < split + 120 && firstBite < 0)
            {
                int executing = sim.Tick;
                sim.Step(InputFrame.Empty);
                if (executing < split + Simulation.SplitlingSpawnGuardTicks)
                {
                    Assert.That(sim.TryGetEnemySwing(2, out _), Is.False, "no bite windup before child age 15");
                    Assert.That(sim.TryGetEnemySwing(3, out _), Is.False, "no bite windup before child age 15");
                }
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && (e.Source == 2 || e.Source == 3)) { firstBite = sim.Tick - 1; break; }
            }
            Assert.That(firstBite, Is.GreaterThanOrEqualTo(0), "потом кусают");
            Assert.That(firstBite - split, Is.GreaterThan(Simulation.SplitlingSpawnGuardTicks), "15 тиков без укуса");
        }

        // ---------- когда распада нет ----------

        [Test]
        public void PendingSplit_SnapshotsDeathState_AndHashesItsFutureChildren()
        {
            var a = Stand();
            var b = Stand();
            int original = a.Entities.MaxHealth[1];
            a.Entities.MaxHealth[1] = original * 2;
            KillNow(a, 1);
            KillNow(b, 1);
            a.Entities.MaxHealth[1] = original;
            Assert.That(a.Entities.Count, Is.EqualTo(2));
            Assert.That(a.PendingSplitCount, Is.EqualTo(1));
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()), "future child stats are part of the state");
            var deathAt = a.Entities.Position[1];
            a.Entities.Position[1] += new FixVec2(Fix64.FromInt(30), Fix64.Zero);
            ReleasePending(a);
            ReleasePending(b);
            Assert.That(a.Entities.MaxHealth[2], Is.EqualTo(b.Entities.MaxHealth[2] * 2));
            Assert.That((a.Entities.Position[2] - deathAt).Length.ToDouble(), Is.EqualTo(.1).Within(.002));
        }

        [Test]
        public void PendingSplit_ResetBeforeRelease_DoesNotLeakChildrenIntoTheNextArena()
        {
            var sim = Stand();
            KillNow(sim, 1);
            Assert.That(sim.HasPendingSplits, Is.True);
            sim.SetupKindTestArena(EnemyKind.ForestSplitter);
            Assert.That(sim.HasPendingSplits, Is.False);
            for (int t = 0; t <= Simulation.SplitterDeathReleaseTicks; t++) sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Count, Is.EqualTo(2));
            Assert.That(Count(sim, SimEventType.SplitterSplit), Is.Zero);
        }

        [Test]
        public void MarkedDead_OrSplitlingDeath_NeverSplits()
        {
            // Тесты снимают мобов Alive = false мимо Kill — это не смерть.
            var sim = Stand();
            sim.Entities.Alive[1] = false;
            for (int t = 0; t < 5; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(Count(sim, SimEventType.SplitterSplit), Is.Zero);
            }
            Assert.That(sim.Entities.Count, Is.EqualTo(2));

            // Детёныш сам не делится — ни посреди тика, ни между шагами.
            var kids = Stand();
            Doom(kids, 1);
            kids.Step(InputFrame.Empty);
            ReleasePending(kids);
            Doom(kids, 2);
            KillNow(kids, 3);
            Assert.That(kids.Entities.Alive[3], Is.False);
            kids.Step(InputFrame.Empty);
            Assert.That(kids.Entities.Alive[2], Is.False);
            Assert.That(Count(kids, SimEventType.SplitterSplit), Is.Zero);
            Assert.That(kids.Entities.Count, Is.EqualTo(4));
            Assert.That(kids.CountAliveEnemies(), Is.Zero);
        }

        [Test]
        public void SurvivalBurrow_DoesNotSplit()
        {
            var sim = Arena(SplitSurvival(), 3);
            Assert.That(sim.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestSplitter));
            Assert.That(sim.Entities.Kind[2], Is.EqualTo(EnemyKind.ForestSplitter));
            int count = sim.Entities.Count, splits = 0, burrows = 0;
            for (int t = 0; t < 90; t++)
            {
                sim.Step(InputFrame.Empty);
                splits += Count(sim, SimEventType.SplitterSplit);
                burrows += Count(sim, SimEventType.Burrowed);
            }
            Assert.That(burrows, Is.EqualTo(2), "оба ушли в землю по концу выживания");
            Assert.That(splits, Is.Zero);
            Assert.That(sim.Entities.Count, Is.EqualTo(count));
            Assert.That(sim.CountAliveEnemies(), Is.Zero);
        }

        [Test]
        public void FullPool_SkipsTheSplit_InsteadOfOverflowing()
        {
            // Пул на пять мест: герой и два Расщепеня. Дети первого встают
            // впритык, второму места уже нет — распад пропускается, а не
            // выходит за массив.
            var sim = Stand(count: 2, capacity: 5);
            Doom(sim, 1);
            Doom(sim, 2);
            Assert.DoesNotThrow(() => sim.Step(InputFrame.Empty));
            ReleasePending(sim);
            Assert.That(sim.Entities.Alive[1], Is.False);
            Assert.That(sim.Entities.Alive[2], Is.False);
            Assert.That(sim.Entities.Count, Is.EqualTo(5));
            Assert.That(Count(sim, SimEventType.SplitterSplit), Is.EqualTo(1));
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.SplitterSplit) Assert.That(e.Source, Is.EqualTo(1), "первым умер младший");
            Assert.That(sim.CountAliveEnemies(), Is.EqualTo(2));
            Assert.DoesNotThrow(() => sim.Step(InputFrame.Empty));
        }

        // ---------- волны и забег ----------

        [Test]
        public void AliveAtMostZeroWave_WaitsForTheChildren()
        {
            var sim = Arena(SplitThenWave(), 5);
            Assert.That(sim.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestSplitter));
            Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(1));

            // Без распада (Alive = false) волна вышла бы в этот же тик.
            var control = Arena(SplitThenWave(), 5);
            control.Entities.Alive[1] = false;
            control.Step(InputFrame.Empty);
            Assert.That(control.EncounterWavesSpawned, Is.EqualTo(2));

            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
            ReleasePending(sim);
            Assert.That(sim.Entities.Alive[1], Is.False);
            Assert.That(sim.CountAliveEnemies(), Is.EqualTo(2));
            Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(1), "дети живы — ложного нуля нет");
            Assert.That(sim.EncounterWavesPending, Is.True);

            Doom(sim, 2);
            Doom(sim, 3);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(2), "дети легли — волна в тот же тик");
            Assert.That(Count(sim, SimEventType.SplitterSplit), Is.Zero);
        }

        [Test]
        public void Run_DoesNotClearUntilTheChildrenAreDead_AndCacheGuardsPassTheirBranchOn()
        {
            // Маршрутный уровень, где каждая пачка — один Расщепень: вход,
            // основной путь, тайники и выход.
            var splitter = new EncounterGroup(EnemyKind.ForestSplitter, 1, 1);
            var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { splitter }) },
                new[] { new EncounterPack(2, 100, new[] { splitter }) }, new[] { new EncounterPack(3, 100, new[] { splitter }) },
                new[] { new EncounterPack(4, 100, new[] { splitter }) }, 2, 0, 100, Fix64.FromInt(4));
            var modules = PrototypeContent.Modules();
            var level = new RiftLevelSettings(16, 1, 1, 2, 0, 0, 100, settings);
            var location = new LocationDefinition(1, modules, new[] { level, level });

            bool branchChecked = false;
            for (ulong seed = 1; seed <= 40 && !branchChecked; seed++)
            {
                var run = new RiftRun(new Simulation(seed, 512), modules, PrototypeContent.Items(),
                    PrototypeContent.ItemBaseIds(), location: location);
                run.StartRun();
                var sim = run.Sim;
                sim.PlayerInvulnerable = true;
                int parents = sim.Entities.Count - 1, required = run.CountRequiredEnemies();
                var branchOf = new int[sim.Entities.Count];
                var guards = new int[run.Map.RewardBranchCount];
                for (int i = 1; i <= parents; i++)
                {
                    Assert.That(sim.Entities.Kind[i], Is.EqualTo(EnemyKind.ForestSplitter));
                    branchOf[i] = run.Encounters.Get(run.Encounters.ForEntity(i)).Branch;
                    if (branchOf[i] >= 0) guards[branchOf[i]]++;
                }
                Assert.That(required, Is.GreaterThan(0));

                // Все родители мертвы — между шагами, через Kill.
                for (int i = 1; i <= parents; i++) KillNow(sim, i);
                run.Step(InputFrame.Empty);
                Assert.That(sim.HasPendingSplits, Is.True);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "pending crack holds completion");
                for (int b = 0; b < guards.Length; b++)
                    Assert.That(run.BranchGuardsAlive(b), Is.EqualTo(guards[b]), "pending parent still guards its cache");
                while (sim.HasPendingSplits) run.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Count, Is.EqualTo(1 + parents * 3), "seed " + seed);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "дети живы — арена не зачищена, seed " + seed);
                Assert.That(run.CountRequiredEnemies(), Is.EqualTo(required * Simulation.SplitChildren), "seed " + seed);
                for (int b = 0; b < guards.Length; b++)
                {
                    Assert.That(run.BranchGuardsAlive(b), Is.EqualTo(guards[b] * Simulation.SplitChildren),
                        "тайник " + b + " охраняют дети его стража, seed " + seed);
                    if (guards[b] > 0) branchChecked = true;
                }

                // Легли дети обязательных — зачищено, дети стражей тайников не держат.
                for (int c = parents + 1; c < sim.Entities.Count; c++)
                    if (branchOf[sim.SplitParentOf(c)] < 0) KillNow(sim, c);
                run.Step(InputFrame.Empty);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit), "seed " + seed);
                for (int b = 0; b < guards.Length; b++)
                    Assert.That(run.BranchGuardsAlive(b), Is.EqualTo(guards[b] * Simulation.SplitChildren));
            }
            Assert.That(branchChecked, Is.True, "ни одного тайника под охраной Расщепеня");
        }

        // ---------- детерминизм ----------

        [Test]
        public void SplitsAndChildren_AreDeterministic()
        {
            var a = Stand(21, 3);
            var b = Stand(21, 3);
            var attack = new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = -1, AbilityTarget = -1 };
            int splits = 0, bites = 0;
            for (int t = 0; t < 600; t++)
            {
                // Раз в полторы секунды младший живой Расщепень гибнет от поджига.
                if (t % 45 == 10)
                    for (int i = 1; i < a.Entities.Count; i++)
                        if (a.Entities.Alive[i] && a.Entities.Kind[i] == EnemyKind.ForestSplitter)
                        { Doom(a, i); Doom(b, i); break; }
                a.Step(attack);
                b.Step(attack);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t);
                foreach (var e in a.Events)
                {
                    if (e.Type == SimEventType.SplitterSplit) splits++;
                    if (e.Type == SimEventType.Attack && a.Entities.Kind[e.Source] == EnemyKind.ForestSplitling) bites++;
                }
            }
            Assert.That(splits, Is.EqualTo(3));
            Assert.That(bites, Is.GreaterThan(0), "детёныши дрались");

            // Новая расстановка стирает и детей, и очередь распада.
            a.SetupKindTestArena(EnemyKind.ForestSplitter);
            Assert.That(a.Entities.Count, Is.EqualTo(2));
            Assert.That(a.SplitParentOf(1), Is.EqualTo(-1));
        }

        // ---------- перекат клубком ----------

        private const int RollT0 = Simulation.SplitterRollFirstDelayTicks;

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>
        /// Стенд переката: герой в нуле с запасом здоровья и без брони,
        /// Расщепень в distance м под углом angle (градусы от +X) и смотрит на
        /// героя. Скорость хода — ноль: моб не подходит сам, и перекат (у него
        /// своя скорость) начинается ровно на 60-м тике.
        /// </summary>
        private static Simulation RollStand(double distance = 4, double angle = 0, LayoutMap map = null, ulong seed = 11)
        {
            var sim = new Simulation(seed, 64);
            sim.SetupKindTestArena(EnemyKind.ForestSplitter, 1, map, seed, 1, 100, Fix64.FromDouble(distance));
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0); sim.Entities.Health[0] = 10000;
            if (angle != 0)
            {
                double r = angle * Math.PI / 180;
                var hero = sim.Entities.Position[0];
                sim.Entities.Position[1] = hero + At(Math.Cos(r) * distance, Math.Sin(r) * distance);
                sim.Entities.Facing[1] = (hero - sim.Entities.Position[1]).Normalized();
            }
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(1);
            return sim;
        }

        /// <summary>Поляна 20×20 м с центром в нуле; rock — камень на оси (x, 0).</summary>
        private static LayoutMap Glade(double rockX = double.NaN, double rockRadius = 0.5)
        {
            var room = new ModuleDefinition("splitter.test", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
            if (!double.IsNaN(rockX))
                map.AddTestObstacle(new LayoutObstacle(At(rockX, 0), Fix64.FromDouble(rockRadius), 0));
            return map;
        }

        private sealed class RollLog
        {
            public readonly List<int> Started = new List<int>(), Cancelled = new List<int>(), Damage = new List<int>();
            public readonly List<int> ImpactTicks = new List<int>(), Stages = new List<int>(), DamageAmounts = new List<int>();
            public readonly List<bool> Hits = new List<bool>();
            public readonly List<FixVec2> ImpactAt = new List<FixVec2>(), StartedAt = new List<FixVec2>();
        }

        /// <summary>Шагает до тика until и пишет события переката моба 1 и его урон по герою; input — ввод на тик.</summary>
        private static void RunRoll(Simulation sim, int until, RollLog log, Func<int, InputFrame> input = null,
            Action<int> before = null)
        {
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                before?.Invoke(tick);
                sim.Step(input != null ? input(tick) : InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    bool roll = e.ActionVariant == (int)EnemyActionKind.SplitterRoll;
                    if (e.Type == SimEventType.EnemyActionStarted && roll && e.Source == 1)
                    { log.Started.Add(tick); log.StartedAt.Add(e.Position); }
                    if (e.Type == SimEventType.EnemyActionCancelled && roll && e.Source == 1) log.Cancelled.Add(tick);
                    if (e.Type == SimEventType.EnemyActionImpact && roll && e.Source == 1)
                    { log.ImpactTicks.Add(tick); log.Stages.Add(e.Amount); log.Hits.Add(e.Flag); log.ImpactAt.Add(e.Position); }
                    if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == Simulation.PlayerId)
                    { log.Damage.Add(tick); log.DamageAmounts.Add(e.Amount); }
                }
            }
        }

        /// <summary>Метки моба source в пуле.</summary>
        private static List<EnemyTelegraph> Marks(Simulation sim, int source)
        {
            var list = new List<EnemyTelegraph>();
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.Source == source) list.Add(t);
            return list;
        }

        private static int MeleeTokens(Simulation sim)
            => (int)typeof(Simulation).GetMethod("CountMeleeAttackTokens", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sim, new object[] { -1 });

        private static InputFrame MoveTo(FixVec2 point) => new InputFrame
        { Aim = point, Flags = (byte)InputFlags.MoveOrder, AttackTarget = -1, AbilityTarget = -1 };

        [Test]
        public void Roll_CurlLockLaunch_OnExactTicks_HitsOnce()
        {
            var sim = RollStand(4);
            var log = new RollLog();
            RunRoll(sim, RollT0, log);
            Assert.That(log.Started, Is.Empty, "первый перекат — не раньше 60 тиков после агро");
            var start = sim.Entities.Position[1];

            RunRoll(sim, RollT0 + 1, log);
            Assert.That(log.Started, Is.EqualTo(new[] { RollT0 }));
            Assert.That(log.StartedAt[0], Is.EqualTo(start));
            Assert.That(sim.TryGetSplitterRoll(1, out var curl), Is.True);
            Assert.That(curl.Phase, Is.EqualTo(SplitterRollPhase.Curl));
            Assert.That(curl.StartTick, Is.EqualTo(RollT0));
            Assert.That(curl.LockTick, Is.EqualTo(RollT0 + 12));
            Assert.That(curl.LaunchTick, Is.EqualTo(RollT0 + 30));
            Assert.That(sim.SplitterRollHoldsMeleeToken(1), Is.True, "жетон — с тика сжатия");
            Assert.That(MeleeTokens(sim), Is.EqualTo(1));

            RunRoll(sim, RollT0 + 12, log);
            Assert.That(Marks(sim, 1), Is.Empty, "в сжатии метки нет");
            Assert.That(sim.Entities.Velocity[1], Is.EqualTo(FixVec2.Zero));

            RunRoll(sim, RollT0 + 13, log);
            var marks = Marks(sim, 1);
            Assert.That(marks.Count, Is.EqualTo(1));
            var lane = marks[0];
            Assert.That(sim.TryGetSplitterRoll(1, out var locked), Is.True);
            Assert.That(locked.Phase, Is.EqualTo(SplitterRollPhase.Locked));
            Assert.That(lane.Shape, Is.EqualTo(TelegraphShape.Lane));
            Assert.That(lane.SharedView, Is.False, "борозду рисует свой вид");
            Assert.That(lane.StartTick, Is.EqualTo(RollT0 + 12));
            Assert.That(lane.ImpactTick, Is.EqualTo(RollT0 + 30));
            Assert.That(lane.Width, Is.EqualTo(Simulation.SplitterRollLaneWidth));
            Assert.That(lane.Width.ToDouble(), Is.EqualTo(1.6).Within(1e-6));
            Assert.That(lane.Serial, Is.EqualTo(locked.TelegraphSerial));
            Assert.That(lane.Origin, Is.EqualTo(locked.Origin));
            Assert.That(lane.Direction, Is.EqualTo(locked.Direction));
            double heroDistance = FixVec2.Distance(locked.Origin, sim.Entities.Position[0]).ToDouble();
            Assert.That(lane.Length.ToDouble(), Is.EqualTo(Math.Min(heroDistance + 2, 6.5)).Within(1e-3),
                "до героя + 2 м");
            Assert.That(locked.Length, Is.EqualTo(lane.Length));
            Assert.That(FixVec2.Dot(locked.Direction, (sim.Entities.Position[0] - locked.Origin).Normalized()).ToDouble(),
                Is.GreaterThan(0.9999), "полоса смотрит на героя");
            int rollTicks = (int)Math.Ceiling(lane.Length.ToDouble() / 0.4 - 1e-9);
            Assert.That(locked.StopTick, Is.EqualTo(RollT0 + 30 + rollTicks - 1));

            RunRoll(sim, RollT0 + 30, log);
            Assert.That(log.ImpactTicks, Is.Empty);
            Assert.That(sim.Entities.Position[1], Is.EqualTo(locked.Origin), "до пуска стоит");
            RunRoll(sim, RollT0 + 31, log);
            Assert.That(log.ImpactTicks, Is.EqualTo(new[] { RollT0 + 30 }), "пуск на 30-м");
            Assert.That(log.Stages, Is.EqualTo(new[] { 0 }));
            Assert.That(log.Hits, Is.EqualTo(new[] { false }));
            Assert.That(log.ImpactAt[0], Is.EqualTo(locked.Origin));
            Assert.That(sim.Entities.Velocity[1].Length.ToDouble(), Is.EqualTo(0.4).Within(1e-3), "0,4 м за тик");
            Assert.That(sim.TryGetSplitterRoll(1, out var rolling), Is.True);
            Assert.That(rolling.Phase, Is.EqualTo(SplitterRollPhase.Rolling));

            RunRoll(sim, locked.StopTick + 1, log);
            Assert.That(log.ImpactTicks, Is.EqualTo(new[] { RollT0 + 30, locked.StopTick }));
            Assert.That(log.Stages, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(log.Hits, Is.EqualTo(new[] { false, true }), "стоп говорит, что задел");
            Assert.That(log.Damage.Count, Is.EqualTo(1), "удар один на перекат");
            Assert.That(log.DamageAmounts, Is.EqualTo(new[] { 18 }), "18/12 урона листа");
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 18));
            var end = locked.Origin + locked.Direction * lane.Length;
            Assert.That(FixVec2.Distance(sim.Entities.Position[1], end).ToDouble(), Is.LessThan(1e-3), "встал в конце полосы");
            Assert.That(log.ImpactAt[1], Is.EqualTo(sim.Entities.Position[1]));
            Assert.That(sim.TryGetSplitterRoll(1, out var uncurl), Is.True);
            Assert.That(uncurl.Phase, Is.EqualTo(SplitterRollPhase.Uncurl));
            Assert.That(uncurl.HitResolved, Is.True);
            Assert.That(uncurl.EndTick, Is.EqualTo(locked.StopTick + Simulation.SplitterRollUncurlTicks));
            Assert.That(sim.SplitterRollHoldsMeleeToken(1), Is.False, "жетон свободен с остановки");
            Assert.That(sim.Entities.Velocity[1], Is.EqualTo(FixVec2.Zero));

            RunRoll(sim, uncurl.EndTick, log);
            Assert.That(sim.TryGetSplitterRoll(1, out _), Is.False);
            Assert.That(log.Damage.Count, Is.EqualTo(1));
            Assert.That(log.Cancelled, Is.Empty);
        }

        [Test]
        public void Roll_HitKnocksTheHeroSideways()
        {
            var sim = RollStand(4);
            var log = new RollLog();
            while (log.Damage.Count == 0 && sim.Tick < RollT0 + 60) RunRoll(sim, sim.Tick + 1, log);
            Assert.That(log.Damage.Count, Is.EqualTo(1));
            Assert.That(sim.Entities.ForcedKind[0], Is.EqualTo((byte)ForcedMotionKind.Knockback));
            var push = sim.Entities.ForcedTarget[0] - sim.Entities.Position[0];
            Assert.That(push.Length.ToDouble(), Is.EqualTo(1.2).Within(0.01));
            Assert.That(sim.TryGetSplitterRoll(1, out var roll), Is.True);
            Assert.That(roll.Phase, Is.EqualTo(SplitterRollPhase.Rolling), "катится дальше сквозь героя");
            Assert.That(Math.Abs(FixVec2.Dot(push.Normalized(), roll.Direction).ToDouble()), Is.LessThan(1e-3), "вбок от полосы");
        }

        [Test]
        public void Roll_BotSteppingAsideTwelveTicksAfterTheLane_IsNeverHit()
        {
            for (int k = 0; k < 20; k++)
            {
                double distance = 3.3 + 0.18 * k;
                double angle = (k * 37) % 360;
                double side = (k & 1) == 0 ? 1.5 : -1.5;
                var sim = RollStand(distance, angle, null, (ulong)(100 + k));
                var log = new RollLog();
                RunRoll(sim, RollT0 + 13, log);
                Assert.That(log.Started, Is.EqualTo(new[] { RollT0 }), "setup " + k);
                Assert.That(sim.TryGetSplitterRoll(1, out var roll), Is.True, "setup " + k);
                Assert.That(roll.Phase, Is.EqualTo(SplitterRollPhase.Locked), "setup " + k);
                var hero = sim.Entities.Position[0];
                var aside = hero + new FixVec2(-roll.Direction.Y, roll.Direction.X) * Fix64.FromDouble(side);
                int dodge = roll.LockTick + 12;
                RunRoll(sim, roll.EndTick + 1, log, tick => tick >= dodge ? MoveTo(aside) : InputFrame.Empty);
                Assert.That(log.Stages, Is.EqualTo(new[] { 0, 1 }), "пуск и стоп, setup " + k);
                Assert.That(log.Hits, Is.EqualTo(new[] { false, false }), "setup " + k);
                Assert.That(log.Damage, Is.Empty, "setup " + k);
                Assert.That(sim.Entities.Health[0], Is.EqualTo(10000), "setup " + k);
                double lateral = Math.Abs((sim.Entities.Position[0].X - hero.X).ToDouble() * -roll.Direction.Y.ToDouble()
                    + (sim.Entities.Position[0].Y - hero.Y).ToDouble() * roll.Direction.X.ToDouble());
                // Приказ мышью тормозит у точки: бот уходит на ~1,2 м из заказанных 1,5.
                Assert.That(lateral, Is.GreaterThan(1.0), "бот действительно отошёл, setup " + k);

                // Тот же стенд без шага в сторону — удар есть: проверка не пустая.
                var still = RollStand(distance, angle, null, (ulong)(100 + k));
                var control = new RollLog();
                RunRoll(still, RollT0 + 60, control);
                Assert.That(control.Damage.Count, Is.EqualTo(1), "стоящего бьёт, setup " + k);
            }
        }

        [Test]
        public void Roll_IntoAWall_StunsFor45_AsDizzy()
        {
            // Камень на оси между героем (0) и Расщепенем (6,5): тело (0,7) упирается
            // в камень (0,5 в 1,5 м) на 2,7 — полоса 3,8 м, в её конце стена.
            var sim = RollStand(6.5, 0, Glade(1.5));
            Assert.That(sim.Entities.Position[0], Is.EqualTo(FixVec2.Zero), "герой в центре поляны");
            Assert.That(sim.Entities.Position[1], Is.EqualTo(At(6.5, 0)));
            var log = new RollLog();
            RunRoll(sim, RollT0 + 13, log);
            Assert.That(sim.TryGetSplitterRoll(1, out var roll), Is.True);
            Assert.That(roll.WallStop, Is.True, "стена впереди видна с фиксации");
            Assert.That(roll.Length.ToDouble(), Is.EqualTo(6.5 - 2.7).Within(0.01));
            Assert.That(Marks(sim, 1)[0].Length, Is.EqualTo(roll.Length), "полоса обрезана стеной");

            RunRoll(sim, roll.StopTick + 1, log);
            Assert.That(log.Stages, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(log.ImpactTicks[1], Is.EqualTo(roll.StopTick));
            Assert.That(log.Hits, Is.EqualTo(new[] { false, false }));
            Assert.That(sim.Entities.Position[1].X.ToDouble(), Is.EqualTo(2.7).Within(0.01), "у камня, не в нём");
            Assert.That(sim.TryGetSplitterRoll(1, out var dizzy), Is.True);
            Assert.That(dizzy.Phase, Is.EqualTo(SplitterRollPhase.Dizzy));
            Assert.That(dizzy.EndTick, Is.EqualTo(roll.StopTick + 45));
            Assert.That(sim.Statuses.StunUntilTick[1], Is.EqualTo(roll.StopTick + Simulation.SplitterRollDizzyTicks));
            Assert.That(sim.Statuses.IsStunned(1, sim.Tick), Is.True);

            // Последний тик оглушения — StopTick + 44; с EndTick перекат кончен, как у раскрытия.
            RunRoll(sim, roll.StopTick + 44, log);
            Assert.That(sim.TryGetSplitterRoll(1, out var still), Is.True);
            Assert.That(still.Phase, Is.EqualTo(SplitterRollPhase.Dizzy));
            RunRoll(sim, roll.StopTick + 45, log);
            Assert.That(sim.TryGetSplitterRoll(1, out _), Is.False);
            Assert.That(sim.Statuses.IsStunned(1, sim.Tick), Is.False);
            Assert.That(log.Cancelled, Is.Empty, "своё оглушение перекат не снимает");
            Assert.That(log.Damage, Is.Empty);
        }

        [Test]
        public void Roll_RockAppearingOnTheLockedLane_StopsItThereAsAWallHit()
        {
            var map = Glade();
            var sim = RollStand(5, 0, map);
            var log = new RollLog();
            RunRoll(sim, RollT0 + 13, log);
            Assert.That(sim.TryGetSplitterRoll(1, out var roll), Is.True);
            Assert.That(roll.WallStop, Is.False);
            // Камень встал поперёк уже зафиксированной полосы: клубок упирается в него, не скользит.
            map.AddTestObstacle(new LayoutObstacle(At(2, 0), Fix64.FromDouble(0.5), 0));
            RunRoll(sim, RollT0 + 60, log);
            Assert.That(log.Stages, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(log.ImpactTicks[1], Is.LessThan(roll.StopTick), "встал раньше конца полосы");
            Assert.That(sim.Entities.Position[1].X.ToDouble(), Is.EqualTo(3.2).Within(0.01));
            Assert.That(sim.Entities.Position[1].Y.ToDouble(), Is.EqualTo(0).Within(1e-3), "без скольжения");
            Assert.That(sim.TryGetSplitterRoll(1, out var dizzy), Is.True);
            Assert.That(dizzy.Phase, Is.EqualTo(SplitterRollPhase.Dizzy));
            Assert.That(sim.Statuses.StunUntilTick[1], Is.EqualTo(log.ImpactTicks[1] + 45));
        }

        [Test]
        public void Roll_StunAtFifteen_Cancels_FreesTheToken_NoDamage()
        {
            var sim = RollStand(4);
            var log = new RollLog();
            RunRoll(sim, RollT0 + 15, log);
            Assert.That(sim.TryGetSplitterRoll(1, out var roll), Is.True);
            Assert.That(roll.Phase, Is.EqualTo(SplitterRollPhase.Locked));
            Assert.That(MeleeTokens(sim), Is.EqualTo(1));
            var lane = Marks(sim, 1)[0];
            Assert.That(lane.IsActive, Is.True);

            sim.Statuses.ApplyStun(1, sim.Tick + 30);
            RunRoll(sim, RollT0 + 16, log);
            Assert.That(log.Cancelled, Is.EqualTo(new[] { RollT0 + 15 }));
            Assert.That(sim.TryGetSplitterRoll(1, out _), Is.False);
            Assert.That(sim.SplitterRollHoldsMeleeToken(1), Is.False);
            Assert.That(MeleeTokens(sim), Is.Zero, "жетон свободен");
            var marks = Marks(sim, 1);
            Assert.That(marks.Count, Is.EqualTo(1));
            Assert.That(marks[0].Serial, Is.EqualTo(lane.Serial));
            Assert.That(marks[0].State, Is.EqualTo(TelegraphState.Cancelled), "полоса гаснет");

            RunRoll(sim, RollT0 + 90, log);
            Assert.That(log.ImpactTicks, Is.Empty, "пуска нет");
            Assert.That(log.Damage, Is.Empty);
            Assert.That(log.Started.Count, Is.EqualTo(1), "перезарядка осталась — 180 от сжатия");
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [Test]
        public void Splitlings_NeverRoll()
        {
            var sim = Stand(distance: Fix64.FromInt(5));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
            ReleasePending(sim);
            Assert.That(sim.Entities.Kind[2], Is.EqualTo(EnemyKind.ForestSplitling));
            var hero = sim.Entities.Position[0];
            int rolls = 0;
            for (int t = 0; t < 300; t++)
            {
                // Детёныши держатся в 5 м лицом к герою — ровно там, где Расщепень катится.
                for (int c = 2; c <= 3; c++)
                {
                    sim.Entities.Position[0] = hero;
                    sim.Entities.Position[c] = hero + At(5, c == 2 ? 0.8 : -0.8);
                    sim.Entities.Facing[c] = (sim.Entities.Position[0] - sim.Entities.Position[c]).Normalized();
                }
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.ActionVariant == (int)EnemyActionKind.SplitterRoll
                        && (e.Type == SimEventType.EnemyActionStarted || e.Type == SimEventType.EnemyActionImpact)) rolls++;
                for (int c = 2; c <= 3; c++)
                {
                    Assert.That(sim.TryGetSplitterRoll(c, out _), Is.False);
                    Assert.That(sim.SplitterRollHoldsMeleeToken(c), Is.False);
                }
            }
            Assert.That(rolls, Is.Zero);
        }

        [TestCase(2.8, false)]
        [TestCase(3.2, true)]
        [TestCase(6.8, true)]
        [TestCase(7.2, false)]
        public void Roll_StartsOnlyBetweenThreeAndSevenMetres(double distance, bool rolls)
        {
            var sim = RollStand(distance);
            var log = new RollLog();
            RunRoll(sim, RollT0 + 150, log);
            Assert.That(log.Started.Count, Is.EqualTo(rolls ? 1 : 0));
            if (rolls) Assert.That(log.Started[0], Is.EqualTo(RollT0));
            else Assert.That(sim.TryGetSplitterRoll(1, out _), Is.False);
        }

        [Test]
        public void Roll_NotWhileFacingAway()
        {
            var sim = RollStand(5);
            // Смотрит поперёк линии на героя: за тик общий доворот (12°) до ±30° не доводит.
            var aside = new FixVec2(Fix64.Zero, Fix64.One);
            var log = new RollLog();
            RunRoll(sim, RollT0 + 30, log, null, tick => sim.Entities.Facing[1] = aside);
            Assert.That(log.Started, Is.Empty, "герой вне ±30°");
            // Отпустили — доворачивается и катится.
            RunRoll(sim, RollT0 + 40, log);
            Assert.That(log.Started.Count, Is.EqualTo(1));
        }

        [Test]
        public void Roll_ReadyInMelee_BacksOffToRollRange_ThenRolls()
        {
            // Вплотную (1,6 м) и на ходу: готовый перекат уводит Расщепеня на кольцо
            // 4,5 м, и оттуда он катится — без отхода перекат в ближнем бою не случался бы.
            var sim = RollStand(1.6);
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed, Simulation.SplitterMoveSpeed);
            sim.Entities.RefreshStats(1);
            var log = new RollLog();
            RunRoll(sim, RollT0 + 150, log);
            Assert.That(log.Started.Count, Is.GreaterThanOrEqualTo(1), "перекат так и не начался");
            Assert.That(log.Started[0], Is.GreaterThanOrEqualTo(RollT0));
        }

        [Test]
        public void Roll_IsDeterministic()
        {
            Simulation Make()
            {
                var s = new Simulation(33, 64);
                s.SetupKindTestArena(EnemyKind.ForestSplitter, 3, Glade(-2.5), 33, 2, 100, Fix64.FromInt(7));
                s.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
                s.Entities.RefreshStats(0); s.Entities.Health[0] = 100000;
                return s;
            }
            var a = Make();
            var b = Make();
            int started = 0, stops = 0, hits = 0;
            for (int t = 0; t < 600; t++)
            {
                // Герой ходит между четырьмя точками: то под перекатом, то в стороне.
                int leg = (t / 40) % 4;
                var input = MoveTo(At(leg == 1 || leg == 2 ? -4 : 3, leg >= 2 ? -4 : 4));
                a.Step(input);
                b.Step(input);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t);
                foreach (var e in a.Events)
                {
                    if (e.ActionVariant != (int)EnemyActionKind.SplitterRoll) continue;
                    if (e.Type == SimEventType.EnemyActionStarted) started++;
                    if (e.Type == SimEventType.EnemyActionImpact && e.Amount == 1) { stops++; if (e.Flag) hits++; }
                }
            }
            Assert.That(started, Is.GreaterThan(0), "катались");
            Assert.That(stops, Is.GreaterThan(0));
            TestContext.WriteLine("rolls " + started + ", stops " + stops + ", hits " + hits);
        }
    }
}
