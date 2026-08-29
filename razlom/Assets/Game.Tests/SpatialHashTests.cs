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
        public void Grid_IsDeterministicUnderLoad()
        {
            var script = Script(600, 0xC0FFEEUL);
            var a = Run(0xC0FFEE123456789UL, 200, script, naive: false);
            var b = Run(0xC0FFEE123456789UL, 200, script, naive: false);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Grid_QueryRadius_FindsOnlyLivingInRange()
        {
            var sim = new Simulation(123UL, 64);
            sim.Entities.Spawn(FixVec2.Zero, 100, Faction.Wole);
            int near = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(1), Fix64.Zero), 100, Faction.Orvill);
            int far  = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(20), Fix64.Zero), 100, Faction.Orvill);
            int dead = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(1), Fix64.One), 100, Faction.Orvill);
            sim.Entities.Alive[dead] = false;

            sim.Grid.Rebuild(sim.Entities);

            var buffer = new int[32];
            int count = sim.Grid.QueryRadius(sim.Entities, FixVec2.Zero, Fix64.FromInt(3), exclude: 0, buffer);

            Assert.AreEqual(1, count, "в радиусе должен быть ровно один живой сосед");
            Assert.AreEqual(near, buffer[0]);
            Assert.AreNotEqual(far, buffer[0]);
            Assert.AreNotEqual(dead, buffer[0]);
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

        [Test]
        public void Grid_DoesNotTargetBehind()
        {
            // Бить за спину нельзя, и отсекается это на выборе цели.
            var sim = new Simulation(1UL, 64);
            sim.Entities.Spawn(FixVec2.Zero, 100, Faction.Wole);
            int behind = sim.Entities.Spawn(new FixVec2(-Fix64.One, Fix64.Zero), 100, Faction.Orvill);
            int ahead = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(2), Fix64.Zero), 100, Faction.Orvill);

            // Смотрим в +X: ближний враг сзади, дальний спереди.
            sim.Entities.Facing[0] = new FixVec2(Fix64.One, Fix64.Zero);
            sim.Grid.Rebuild(sim.Entities);

            Fix64 arc = Fix64.Ratio(1, 2); // 120° перед собой
            int found = sim.Grid.FindNearestEnemy(sim.Entities, 0, Fix64.FromInt(3), arc);

            Assert.AreEqual(ahead, found, "выбран враг за спиной");
            Assert.AreNotEqual(behind, found);
        }

        [Test]
        public void Arc_MatchesNaiveCheck()
        {
            // Эталон для WithinArc: честный угол через Atan2 и косинус.
            // Формула без корня обязана давать тот же ответ на всём круге.
            FixVec2 facing = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 minCos = Fix64.Ratio(1, 2);

            for (int deg = 0; deg < 360; deg++)
            {
                // Пропускаем границу сектора: там приближённый косинус Fix64
                // и точная формула законно расходятся в последнем разряде.
                if (deg == 60 || deg == 300) continue;

                Fix64 radians = Fix64.TwoPi * Fix64.Ratio(deg, 360);
                FixVec2 to = FixVec2.FromAngle(radians) * Fix64.FromInt(2);

                bool expected = Fix64.Cos(radians) >= minCos;
                bool actual = FixVec2.WithinArc(facing, to, minCos);

                Assert.AreEqual(expected, actual, $"угол {deg}°");
            }
        }
    }
}
