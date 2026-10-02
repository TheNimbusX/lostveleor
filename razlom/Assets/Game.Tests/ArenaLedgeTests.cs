using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Уступ между сегментами арены (владелец, 2 октября): следующий сегмент ниже, проход — склон.
    /// Уступ есть не у каждого прохода, и по склону ходят в обе стороны.
    /// </summary>
    public sealed class ArenaLedgeTests
    {
        private static LayoutMap Arena(int size, ulong seed)
        {
            var location = ArenaEncounterTests.ForestLocation();
            return ArenaEncounterTests.ArenaMap(location, 2, seed, size);
        }

        [Test]
        public void SomePassagesHaveALedge_NotEvery()
        {
            int passages = 0, ledges = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var map = Arena(4, seed);
                Assert.That(map.LedgeCount, Is.LessThanOrEqualTo(map.GladeCount - 1));
                passages += map.GladeCount - 1;
                ledges += map.LedgeCount;
            }
            Assert.That(ledges, Is.GreaterThan(passages / 4), "уступы встречаются");
            Assert.That(ledges, Is.LessThan(passages * 3 / 4), "но не на каждом проходе");
        }

        [Test]
        public void LedgeLiesInThePassage_AndCanBeWalkedBothWays()
        {
            int checkedLedges = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var map = Arena(4, seed);
                for (int i = 0; i < map.LedgeCount; i++)
                {
                    var ledge = map.GetLedge(i);
                    FixVec2 above = ledge.Point - ledge.Down, below = ledge.Point + ledge.Down;
                    Fix64 body = Fix64.Ratio(1, 2);
                    Assert.That(map.IsWalkable(above, body) && map.IsWalkable(below, body), Is.True, "уступ лежит в проходе");
                    Assert.That(map.CanTravel(above, below, body), Is.True, "спуститься можно");
                    Assert.That(map.CanTravel(below, above, body), Is.True, "подняться обратно можно");
                    checkedLedges++;
                }
            }
            Assert.That(checkedLedges, Is.GreaterThan(0));
        }
    }
}
