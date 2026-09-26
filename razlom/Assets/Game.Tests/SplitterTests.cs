using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Расщепень (план новых мобов леса от 26.09): настоящая смерть — ровно два
    /// детёныша в тот же тик, по бокам от родителя, со здоровьем и уроном от
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
        public void TrueDeath_SpawnsExactlyTwoChildren_InTheSameTick_BesideTheParent()
        {
            var sim = Stand();
            Assert.That(Simulation.SplitChildren, Is.EqualTo(2));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);

            Assert.That(sim.Entities.Alive[1], Is.False);
            Assert.That(sim.Entities.Count, Is.EqualTo(2 + Simulation.SplitChildren));
            Assert.That(Count(sim, SimEventType.Death), Is.EqualTo(1), "смерть родителя — в этом же тике");
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
            // Шестая арена на «Сложно»: 420 × 135% × 125% = 709, урон 12 × 140% × 125% = 21.
            var sim = Stand(arena: 6, hard: EnemyArchetypes.HardRoutePercent);
            int parentHealth = sim.Entities.MaxHealth[1], parentDamage = sim.Entities.Damage[1];
            Assert.That(parentHealth, Is.EqualTo(709));
            Assert.That(parentDamage, Is.EqualTo(21));
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);

            for (int c = 2; c <= 3; c++)
            {
                // 709 × 120 / 420 = 202,57 → 203; 21 × 4 / 12 = 7.
                Assert.That(sim.Entities.MaxHealth[c], Is.EqualTo(203));
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
            Assert.That(plain.Entities.MaxHealth[2], Is.EqualTo(EnemyArchetypes.SplitlingHealth));
            Assert.That(plain.Entities.Damage[2], Is.EqualTo(EnemyArchetypes.SplitlingDamage));
        }

        [Test]
        public void Children_PopOneMetreSidewaysOverEightTicks()
        {
            var sim = Stand();
            Doom(sim, 1);
            sim.Step(InputFrame.Empty);
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
            int split = sim.Tick - 1;
            Assert.That(sim.Entities.NextAttackTick[2], Is.EqualTo(split + 1 + Simulation.SplitlingSpawnGuardTicks));
            Assert.That((sim.Entities.Position[2] - sim.Entities.Position[0]).Length.ToDouble(),
                Is.LessThan(Simulation.SplitlingBiteRange.ToDouble()));

            int firstBite = -1;
            while (sim.Tick < split + 120 && firstBite < 0)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && (e.Source == 2 || e.Source == 3)) { firstBite = sim.Tick - 1; break; }
            }
            Assert.That(firstBite, Is.GreaterThanOrEqualTo(0), "потом кусают");
            Assert.That(firstBite - split, Is.GreaterThan(Simulation.SplitlingSpawnGuardTicks), "15 тиков без укуса");
        }

        // ---------- когда распада нет ----------

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
    }
}
