using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Уступ между сегментами арены (владелец, 2 октября): с верхнего сегмента на нижний спрыгнуть
    /// можно, обратно — нет. Это одна проверка карты для любого пути (LayoutMap.CanTravel).
    /// </summary>
    public sealed class ArenaLedgeTests
    {
        private static LayoutMap Arena(int size, ulong seed)
        {
            var location = ArenaEncounterTests.ForestLocation();
            return ArenaEncounterTests.ArenaMap(location, 2, seed, size);
        }

        [TestCase(2, 7UL)]
        [TestCase(4, 11UL)]
        public void EveryPassageBetweenSegments_HasALedge_DownOnly(int size, ulong seed)
        {
            var map = Arena(size, seed);
            Assert.That(map.GladeCount, Is.EqualTo(RiftLevelSettings.SegmentCount(size)));
            Assert.That(map.LedgeCount, Is.EqualTo(map.GladeCount - 1));
            for (int i = 0; i < map.LedgeCount; i++)
            {
                var ledge = map.GetLedge(i);
                FixVec2 above = ledge.Point - ledge.Down, below = ledge.Point + ledge.Down;
                Fix64 body = Fix64.Ratio(1, 2);
                Assert.That(map.IsWalkable(above, body) && map.IsWalkable(below, body), Is.True, "уступ лежит в проходе");
                Assert.That(map.CanTravel(above, below, body), Is.True, "спрыгнуть можно");
                Assert.That(map.CanTravel(below, above, body), Is.False, "забраться нельзя");
                // Наискосок вдоль прохода край тоже не обойти.
                var side = new FixVec2(-ledge.Down.Y, ledge.Down.X);
                Assert.That(map.CanTravel(below + side, above - side, body), Is.False);
            }
        }

        [Test]
        public void HeroDropsDown_AndCannotWalkBack()
        {
            var map = Arena(2, 7);
            var ledge = map.GetLedge(0);
            var sim = new Simulation(7, 64);
            sim.SetupRift(map, 7, 0, 0, 1);
            sim.PlayerInvulnerable = true;
            sim.Entities.Position[Simulation.PlayerId] = ledge.Point - ledge.Down * Fix64.FromInt(2);
            WalkTowards(sim, ledge.Point + ledge.Down * Fix64.FromInt(4), 90);
            Assert.That(ledge.Side(sim.Entities.Position[Simulation.PlayerId]).Raw, Is.GreaterThan(0), "герой внизу");
            WalkTowards(sim, ledge.Point - ledge.Down * Fix64.FromInt(4), 120);
            Assert.That(ledge.Side(sim.Entities.Position[Simulation.PlayerId]).Raw, Is.GreaterThanOrEqualTo(0), "наверх не вернуться");
        }

        private static void WalkTowards(Simulation sim, FixVec2 target, int ticks)
        {
            for (int t = 0; t < ticks; t++)
                sim.Step(new InputFrame { Aim = target, Flags = (byte)InputFlags.MoveOrder });
        }
    }
}
