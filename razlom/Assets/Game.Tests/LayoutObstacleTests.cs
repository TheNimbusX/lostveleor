using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class LayoutObstacleTests
    {
        [Test]
        public void Obstacles_AreRepeatable_KeepRoadsOpen_AndBlockSweptMovement()
        {
            var modules = PrototypeContent.Modules();
            var settings = new RiftLevelSettings(24, 1, 1, 2, 1, 3, 100, solidEnvironment: true);
            int total = 0, sweeps = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var map = new LayoutMap(modules, 64);
                var repeated = new LayoutMap(modules, 64);
                settings.Generate(new LayoutGenerator(), modules, map, seed);
                settings.Generate(new LayoutGenerator(), modules, repeated, seed);
                Assert.That(map.Hash(), Is.EqualTo(repeated.Hash()));
                var body = Fix64.Ratio(62, 100);
                Assert.That(map.IsWalkable(map.EntryPoint, body), Is.True);
                Assert.That(map.IsWalkable(map.ExitPoint(0), body), Is.True);
                for (int c = 0; c < map.Routes.CellCount; c++)
                    if (map.Routes.IsRoadCell(c)) Assert.That(map.IsWalkable(map.Routes.GetCell(c).Center, body), Is.True);
                total += map.ObstacleCount;
                for (int i = 0; i < map.ObstacleCount; i++)
                {
                    var obstacle = map.GetObstacle(i);
                    Assert.That(map.IsWalkable(obstacle.Center, body), Is.False);
                    Assert.That(map.IsWalkable(map.ClampToWalkable(obstacle.Center, body), body), Is.True);
                    var offset = new FixVec2(obstacle.Radius + body + Fix64.Ratio(1, 10), Fix64.Zero);
                    var a = obstacle.Center - offset; var b = obstacle.Center + offset;
                    if (map.IsWalkable(a, body) && map.IsWalkable(b, body))
                    { Assert.That(map.CanTravel(a, b, body), Is.False); sweeps++; }
                }
                var sim = new Simulation(seed, 512);
                settings.Spawn(sim, map, seed);
                for (int i = 0; i < sim.Entities.Count; i++)
                    Assert.That(map.IsWalkable(sim.Entities.Position[i], sim.Entities.BodyRadius[i]), Is.True, $"spawn seed {seed}, entity {i}");
            }
            Assert.That(total, Is.GreaterThan(100));
            Assert.That(sweeps, Is.GreaterThan(0));
        }
    }
}
