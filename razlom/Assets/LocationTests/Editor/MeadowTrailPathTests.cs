using Game.Sim;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.LocationTests
{
    public sealed class MeadowTrailPathTests
    {
        private static LayoutMap Room(int size)
        {
            var modules = new ModuleSet(new[] { new ModuleDefinition("test.trail", size, size,
                new ModuleConnector[0], isEntrance: true) });
            var map = new LayoutMap(modules, 4);
            map.TryPlace(0, 0, 0, 0);
            return map;
        }

        [Test]
        public void OpenMeadow_RemovesStaircaseAndUsesDirectDiagonal()
        {
            var map = Room(10);
            var path = new[] { new Vector2(2, 2), new Vector2(2, 6), new Vector2(6, 6),
                new Vector2(6, 12), new Vector2(14, 12), new Vector2(14, 16) };
            var result = MeadowTrailPath.Simplify(map, path, .8f);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(path[0]));
            Assert.That(result[1], Is.EqualTo(path[path.Length - 1]));
        }

        [Test]
        public void ConcaveBoundary_DoesNotShortcutAcrossMissingFloor()
        {
            var map = Room(4);
            map.TryPlace(0, 0, 0, 4);
            map.TryPlace(0, 0, 4, 4);
            var path = new[] { new Vector2(2, 2), new Vector2(2, 10), new Vector2(6, 10), new Vector2(14, 10) };
            Assert.That(MeadowTrailPath.IsClear(map, path[0], path[3], .8f), Is.False);
            var result = MeadowTrailPath.Simplify(map, path, .8f);
            Assert.That(result.Count, Is.GreaterThan(2));
            for (int i = 1; i < result.Count; i++)
                Assert.That(MeadowTrailPath.IsClear(map, result[i - 1], result[i], .8f), Is.True);
        }
    }
}
