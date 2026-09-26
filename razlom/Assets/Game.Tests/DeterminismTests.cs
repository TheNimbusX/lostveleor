using System.Collections.Generic;
using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// САМЫЙ ВАЖНЫЙ ТЕСТ ПРОЕКТА.
    ///
    /// Он ловит нарушение детерминизма в день, когда оно появилось, а не через
    /// полгода, когда игрок пожалуется на битый реплей и придётся искать причину
    /// в трёхстах коммитах.
    ///
    /// Если он покраснел — не чинить тест. Чинить симуляцию.
    /// </summary>
    public class DeterminismTests
    {
        private const int TickCount = 600;   // 20 секунд при 30 Гц
        private const ulong Seed = 0xC0FFEE123456789UL;

        /// <summary>
        /// Один и тот же поток вводов, детерминированно сгенерированный.
        /// Отдельный генератор, чтобы ввод не зависел от состояния симуляции.
        /// </summary>
        private static List<InputFrame> BuildInputScript(int ticks)
        {
            var script = new List<InputFrame>(ticks);
            var scriptRng = new Pcg32(999UL, 42UL);

            for (int t = 0; t < ticks; t++)
            {
                // Ввод — это точка приказа и сам приказ: направления с клавиатуры
                // в игре нет. Точки берутся по всей арене, приказ то есть, то нет,
                // чтобы поток покрывал и ходьбу, и остановки.
                Fix64 ax = scriptRng.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30));
                Fix64 ay = scriptRng.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30));
                script.Add(new InputFrame
                {
                    Aim = new FixVec2(ax, ay),
                    AbilityMask = (byte)scriptRng.NextInt(0, 16),
                    Flags = (byte)(scriptRng.NextInt(0, 4) == 0 ? 0 : (int)InputFlags.MoveOrder)
                });
            }
            return script;
        }

        private static List<ulong> RunAndHash(ulong seed, List<InputFrame> script)
        {
            var sim = new Simulation(seed);
            sim.SetupTestArena(40);

            var hashes = new List<ulong>(script.Count);
            for (int t = 0; t < script.Count; t++)
            {
                var frame = script[t];
                sim.Step(in frame);
                hashes.Add(sim.StateHash());
            }
            return hashes;
        }

        [Test]
        public void SameSeedSameInput_ProducesIdenticalStateEveryTick()
        {
            var script = BuildInputScript(TickCount);

            var runA = RunAndHash(Seed, script);
            var runB = RunAndHash(Seed, script);

            Assert.AreEqual(runA.Count, runB.Count);
            for (int t = 0; t < runA.Count; t++)
            {
                Assert.AreEqual(runA[t], runB[t],
                    $"Расхождение на тике {t}. Симуляция перестала быть детерминированной.");
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentState()
        {
            var script = BuildInputScript(TickCount);

            var runA = RunAndHash(Seed, script);
            var runB = RunAndHash(Seed + 1, script);

            CollectionAssert.AreNotEqual(runA, runB,
                "Разные сиды дали одинаковый результат — сид где-то не используется.");
        }

        [Test]
        public void RngStreams_AreIndependent()
        {
            var a = new RngStreams(Seed);
            var b = new RngStreams(Seed);

            // Прокручиваем боевой поток у первого набора.
            for (int i = 0; i < 1000; i++) a.Combat.NextUInt();

            // Поток лута обязан остаться нетронутым.
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(b.Loot.NextUInt(), a.Loot.NextUInt(),
                    "Потоки случайности не независимы: расход одного сдвинул другой.");
            }
        }

        [Test]
        public void Replay_FromSeedAndInputs_ReproducesFinalState()
        {
            // Модель проверки топ-100: сервер получает сид и поток вводов
            // и обязан прийти ровно в то же состояние, что и клиент.
            var script = BuildInputScript(TickCount);

            var live = new Simulation(Seed);
            live.SetupTestArena(40);
            for (int t = 0; t < script.Count; t++) { var f = script[t]; live.Step(in f); }

            var replay = new Simulation(Seed);
            replay.SetupTestArena(40);
            for (int t = 0; t < script.Count; t++) { var f = script[t]; replay.Step(in f); }

            Assert.AreEqual(live.StateHash(), replay.StateHash());
        }

        // ---- смешанная лесная арена (план новых мобов от 26.09) ----

        /// <summary>
        /// Поляна 40×40 м с камнем. Шипомёт (стенд вида, арена 6), Корнехват,
        /// два Расщепеня и старые виды: Хранитель, рой, Плюй-плод, Камнекопыт.
        /// У героя запас здоровья, чтобы бой шёл все 20 секунд и удары
        /// доходили: замедление героя тоже в хеше.
        /// </summary>
        private static Simulation MixedForestArena(ulong seed)
        {
            var room = new ModuleDefinition("determinism.mixed_forest", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
            map.AddTestObstacle(new LayoutObstacle(new FixVec2(Fix64.FromInt(3), Fix64.One), Fix64.Ratio(1, 2), 0));

            var sim = new Simulation(seed, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThorncaster, 1, map, seed, 6, 100, Fix64.FromInt(7));
            sim.Entities.Stats[Simulation.PlayerId].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
            sim.Entities.RefreshStats(Simulation.PlayerId);
            sim.Entities.Health[Simulation.PlayerId] = 100000;

            Mob(sim, EnemyKind.ForestRootSnarer, -5, 2);
            Mob(sim, EnemyKind.ForestSplitter, 2, -4);
            Mob(sim, EnemyKind.ForestSplitter, -3, -5);
            Mob(sim, EnemyKind.ForestGuardian, 4, 4);
            Mob(sim, EnemyKind.ForestRootSwarm, -2, 4);
            Mob(sim, EnemyKind.ForestRootSwarm, -1, 6);
            Mob(sim, EnemyKind.ForestRootSwarm, 1, 6);
            Mob(sim, EnemyKind.ForestBud, -9, -2);
            Mob(sim, EnemyKind.ForestStonehoof, -8, 6);
            return sim;
        }

        private static void Mob(Simulation sim, EnemyKind kind, int x, int y)
        {
            var at = new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));
            int id = sim.SpawnEnemy(at, EnemyArchetypes.Get(kind).BaseHealth, kind);
            sim.Entities.Aggro[id] = true;
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
        }

        /// <summary>
        /// Герой держится у мобов: точка приказа в ±8 м меняется раз в полсекунды,
        /// он то идёт, то бьёт, изредка жмёт способности.
        /// </summary>
        private static List<InputFrame> BuildBrawlScript(int ticks)
        {
            var script = new List<InputFrame>(ticks);
            var scriptRng = new Pcg32(4242UL, 7UL);
            var aim = FixVec2.Zero;
            for (int t = 0; t < ticks; t++)
            {
                if (t % 15 == 0)
                    aim = new FixVec2(scriptRng.NextFix(Fix64.FromInt(-8), Fix64.FromInt(8)),
                        scriptRng.NextFix(Fix64.FromInt(-8), Fix64.FromInt(8)));
                bool move = scriptRng.NextInt(0, 3) == 0;
                script.Add(new InputFrame
                {
                    Aim = aim, AttackTarget = -1, AbilityTarget = -1,
                    AbilityMask = (byte)(scriptRng.NextInt(0, 8) == 0 ? scriptRng.NextInt(1, 16) : 0),
                    Flags = (byte)(move ? InputFlags.MoveOrder : InputFlags.Attack),
                });
            }
            return script;
        }

        /// <summary>Младший живой Расщепень или -1.</summary>
        private static int LivingSplitter(Simulation sim)
        {
            for (int i = 1; i < sim.Entities.Count; i++)
                if (sim.Entities.Alive[i] && sim.Entities.Kind[i] == EnemyKind.ForestSplitter) return i;
            return -1;
        }

        [Test]
        public void MixedForestArena_WithNewMobsAndSplits_IdenticalStateEveryTick()
        {
            var script = BuildBrawlScript(TickCount);
            var a = MixedForestArena(Seed);
            var b = MixedForestArena(Seed);
            Assert.AreEqual(a.StateHash(), b.StateHash(), "Расстановка разошлась.");

            int lines = 0, slams = 0, impacts = 0, splits = 0;
            for (int t = 0; t < script.Count; t++)
            {
                // Расщепени гибнут наверняка, если герой не успел сам: один — между
                // шагами (распад в конце следующего), другой — посреди шага от
                // смертельного поджига. В обоих прогонах одинаково.
                if (t == 90 || t == 240)
                {
                    int id = LivingSplitter(a);
                    Assert.AreEqual(id, LivingSplitter(b), $"Живые Расщепени разошлись на тике {t}.");
                    if (id > 0 && t == 90)
                    {
                        a.ApplyAbilityDamage(Simulation.PlayerId, id, 1000000, -1, DamageType.Physical);
                        b.ApplyAbilityDamage(Simulation.PlayerId, id, 1000000, -1, DamageType.Physical);
                    }
                    else if (id > 0)
                    {
                        a.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, Simulation.PlayerId, -1);
                        b.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, Simulation.PlayerId, -1);
                    }
                }
                var frame = script[t];
                a.Step(in frame);
                b.Step(in frame);
                Assert.AreEqual(a.StateHash(), b.StateHash(),
                    $"Расхождение на тике {t}. Смешанная лесная арена перестала быть детерминированной.");
                foreach (var e in a.Events)
                {
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThornLine) lines++;
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.SnarerSlam) slams++;
                    if (e.Type == SimEventType.EnemyActionImpact) impacts++;
                    if (e.Type == SimEventType.SplitterSplit) splits++;
                }
            }

            int splitlings = 0;
            for (int i = 1; i < a.Entities.Count; i++) if (a.Entities.Kind[i] == EnemyKind.ForestSplitling) splitlings++;
            Assert.That(lines, Is.GreaterThan(0), "Шипомёт ни разу не начал линию — тест ничего не проверил.");
            Assert.That(slams, Is.GreaterThan(0), "Корнехват ни разу не ударил корнями.");
            Assert.That(impacts, Is.GreaterThan(0), "Ни одного контакта шипов или корней.");
            Assert.That(splits, Is.EqualTo(2), "Оба Расщепеня распались.");
            Assert.That(splitlings, Is.EqualTo(2 * Simulation.SplitChildren));
        }
    }
}
