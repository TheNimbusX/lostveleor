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
    }
}
