using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampWalkMapTests
    {
        static FixVec2 Point(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        [Test]
        public void ClickOnDisconnectedSideEndsAtNearestReachableEdge()
        {
            const int size = 12;
            var cells = new bool[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) cells[y * size + x] = x != 5;
            var map = new CampWalkMap(FixVec2.Zero, Fix64.One, size, size, cells);
            var start = Point(1.5, 6.5); var click = Point(8.5, 6.5);
            Assert.True(map.TryNearestReachable(start, click, out var goal));
            Assert.AreEqual(Point(4.5, 6.5), goal);
            Assert.AreEqual(2, map.ComponentCount);
            var path = map.FindPath(start, click);
            Assert.AreEqual(goal, path[path.Length - 1]);
            for (int i = 1; i < path.Length; i++) Assert.True(map.CanTravel(path[i - 1], path[i]));
        }

        [Test]
        public void EmptyMapDoesNotInventAReachableDestination()
        {
            var map = new CampWalkMap(FixVec2.Zero, Fix64.One, 2, 2, new bool[4]);
            Assert.False(map.TryNearestReachable(Point(.5, .5), Point(1, 1), out _));
            Assert.IsEmpty(map.FindPath(Point(.5, .5), Point(1, 1)));
        }

        [Test]
        public void CampMotionSlidesAlongAngledBoundaryWithoutCrossingOrSpeedBoost()
        {
            const int size = 80;
            var cells = new bool[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                cells[y * size + x] = x + y < 70;
            var map = new CampWalkMap(FixVec2.Zero, Fix64.Ratio(1, 8), size, size, cells);
            var at = Point(4.2, 4.4); var start = at; var delta = Point(.09, 0);
            var trail = new System.Text.StringBuilder();
            for (int i = 0; i < 24; i++)
            {
                var next = map.Slide(at, delta);
                trail.AppendLine(i + ": " + next);
                Assert.True(map.Contains(next), "tick=" + i);
                Assert.True(map.CanTravel(at, next), "tick=" + i);
                Assert.LessOrEqual((next - at).Length.ToDouble(), delta.Length.ToDouble() + .00001, "tick=" + i);
                at = next;
            }
            Assert.Greater((at.X - start.X).ToDouble(), .5, "motion stuck on the stepped angled edge\n" + trail);
            Assert.Less((at.Y - start.Y).ToDouble(), -.4, "motion did not follow the boundary tangent");
        }

        [Test]
        public void LargeMovementCannotTunnelAcrossAnObstacle()
        {
            const int size = 12;
            var cells = new bool[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) cells[y * size + x] = x != 5;
            var map = new CampWalkMap(FixVec2.Zero, Fix64.One, size, size, cells);
            var start = Point(4.5, 6.5);
            var result = map.Slide(start, Point(4, 0));
            Assert.True(map.Contains(result));
            Assert.Less(result.X.ToDouble(), 5);
        }
    }
}
