using System.Collections.Generic;
using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Главный приём при замене простого алгоритма на быстрый: простой остаётся
    /// в коде как эталон, а тест доказывает, что оба дают ОДИН И ТОТ ЖЕ результат.
    ///
    /// Именно этот тест поймал ошибку, которая иначе жила бы месяцами:
    /// сетка не проверяла Alive, и уже убитая в этом же тике цель оставалась
    /// выбираемой. В игре это выглядело бы как «персонаж иногда бьёт труп».
    /// </summary>
    public class SpatialHashTests
    {
        private static List<InputFrame> Script(int ticks, ulong seed)
        {
            var s = new List<InputFrame>(ticks);
            var r = new Pcg32(seed, 42UL);
            for (int t = 0; t < ticks; t++)
                s.Add(new InputFrame
                {
                    Aim = new FixVec2(r.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30)),
                                      r.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30))),
                    AbilityMask = (byte)r.NextInt(0, 16),
                    Flags = (byte)InputFlags.MoveOrder
                });
            return s;
        }

        private static List<ulong> Run(ulong seed, int enemies, List<InputFrame> script, bool naive)
        {
            var sim = new Simulation(seed, 4096) { DebugUseNaiveTargeting = naive };
            sim.SetupTestArena(enemies);
            var hashes = new List<ulong>(script.Count);
            for (int t = 0; t < script.Count; t++)
            {
                var f = script[t];
                sim.Step(in f);
                hashes.Add(sim.StateHash());
            }
            return hashes;
        }

        [Test]
        [TestCase(5,   0xC0FFEEUL)]
        [TestCase(40,  0xC0FFEEUL)]
        [TestCase(40,  0xBEEF1234UL)]
        [TestCase(40,  7UL)]
        [TestCase(120, 0xBEEF1234UL)]
        [TestCase(400, 0xBEEF1234UL)]
        public void Grid_MatchesNaiveTargeting_ExactlyEveryTick(int enemies, ulong seed)
        {
            var script = Script(400, seed);
            var naive = Run(seed, enemies, script, naive: true);
            var grid  = Run(seed, enemies, script, naive: false);

            for (int t = 0; t < naive.Count; t++)
            {
                Assert.AreEqual(naive[t], grid[t],
                    $"Сетка разошлась с прямым перебором на тике {t} " +
                    $"({enemies} врагов, сид {seed:X}). Оптимизация поменяла поведение.");
            }
        }

        [Test]
        public void Grid_TieBreaksByLowestIndex()
        {
            // Две цели ровно на одинаковом расстоянии. Побеждать обязан меньший
            // индекс, иначе результат зависел бы от раскладки по ячейкам.
            var sim = new Simulation(1UL, 64);
            sim.Entities.Spawn(FixVec2.Zero, 100, Faction.Wole);
            int a = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 100, Faction.Orvill);
            int b = sim.Entities.Spawn(new FixVec2(-Fix64.One, Fix64.Zero), 100, Faction.Orvill);

            sim.Grid.Rebuild(sim.Entities);

            // Сектор отключён (-1): тест про разрыв ничьей, и фильтр по
            // направлению не должен выкидывать одного из кандидатов раньше,
            // чем дело дойдёт до сравнения индексов.
            int found = sim.Grid.FindNearestEnemy(sim.Entities, 0, Fix64.FromInt(2), -Fix64.One);

            Assert.AreEqual(a < b ? a : b, found, "при равном расстоянии выбирается меньший индекс");
        }

    }
}
