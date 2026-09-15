using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class GladeLayoutTests
    {
        [Test]
        public void Shapes_HaveDifferentSilhouettes_AndKeepTheirCentersOpen()
        {
            var signatures = new System.Collections.Generic.HashSet<string>();
            foreach (GladeShape shape in System.Enum.GetValues(typeof(GladeShape)))
            {
                var region = new GladeRegion(FixVec2.Zero, new FixVec2(Fix64.FromInt(12), Fix64.FromInt(12)), shape);
                var signature = new System.Text.StringBuilder();
                for (int y = -12; y <= 12; y++)
                    for (int x = -12; x <= 12; x++)
                    {
                        bool inside = region.Field(new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y))) <= Fix64.One;
                        signature.Append(inside ? '1' : '0');
                        if (x * x + y * y <= 9) Assert.That(inside, Is.True, $"Blocked center: {shape}");
                        if (System.Math.Abs(x) == 12 || System.Math.Abs(y) == 12)
                            Assert.That(inside, Is.False, $"Shape touches module edge: {shape}");
                    }
                Assert.That(signatures.Add(signature.ToString()), Is.True, $"Duplicate silhouette: {shape}");
            }
        }

        [Test]
        public void Glades_KeepEveryZoneReachable_AndReproduceTheirContour()
        {
            var modules = PrototypeContent.Modules();
            int ponds = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var a = new LayoutMap(modules, 64); var b = new LayoutMap(modules, 64);
                GladeLayout.Generate(modules, a, seed, seed % 2 == 0 ? 16 : 24);
                GladeLayout.Generate(modules, b, seed, seed % 2 == 0 ? 16 : 24);
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
                ponds += a.WaterCount;
                for (int w = 0; w < a.WaterCount; w++)
                {
                    var pond = a.GetWater(w);
                    Assert.That(a.IsWalkable(pond.Center, Fix64.Ratio(85, 100)), Is.False);
                    var offset = new FixVec2(pond.Radius + Fix64.One, Fix64.Zero);
                    Assert.That(a.CanTravel(pond.Center - offset, pond.Center + offset, Fix64.Half), Is.False);
                    Assert.That(a.IsWalkable(a.ClampToWalkable(pond.Center, Fix64.Half), Fix64.Half), Is.True);
                }
                Assert.That(a.GladeCount, Is.EqualTo(GladeLayout.ClearingCount(seed % 2 == 0 ? 16 : 24)));
                for (int g = 1; g < a.GladeCount; g++)
                {
                    var first = a.GetGlade(g - 1); var second = a.GetGlade(g);
                    Assert.That(first.Shape, Is.Not.EqualTo(second.Shape));
                    var middle = (first.Center + second.Center) * Fix64.Ratio(1, 2);
                    Assert.That(a.IsWalkable(middle, Fix64.Ratio(85, 100)), Is.True, "Connector blocked");
                    var axis = (second.Center - first.Center).Normalized();
                    var side = new FixVec2(-axis.Y, axis.X) * Fix64.FromInt(4);
                    Assert.That(a.ContainsWorld(middle + side), Is.False, "Clearings merged across the connector");
                }
                Assert.That(a.IsWalkable(a.EntryPoint, Fix64.Ratio(85, 100)), Is.True);
                Assert.That(a.IsWalkable(a.ExitPoint(0), Fix64.Ratio(85, 100)), Is.True);
                for (int c = 0; c < a.Routes.CellCount; c++)
                {
                    Assert.That(a.Routes.DistanceFromEntry(c), Is.GreaterThanOrEqualTo(0), $"seed {seed}, cell {c}");
                    Assert.That(a.IsWalkable(a.Routes.GetCell(c).Center, Fix64.Ratio(85, 100)), Is.True);
                }
                for (int m = 0; m < a.PlacedCount; m++)
                    Assert.That(a.Routes.DistanceToModule(m), Is.GreaterThanOrEqualTo(0));
                Assert.That(a.RewardBranchCount, Is.EqualTo(2));
                for (int branch = 0; branch < a.RewardBranchCount; branch++)
                {
                    int module = a.GetRewardBranch(branch);
                    Assert.That(a.Routes.IsMainModule(module), Is.False, "Cache must remain optional");
                    Assert.That(a.HasChild(module), Is.False);
                    int cursor = a.Routes.CellAt(a.CenterOf(module)), detour = 0;
                    Assert.That(cursor, Is.GreaterThanOrEqualTo(0));
                    while (cursor >= 0 && !a.Routes.IsMainModule(a.Routes.GetCell(cursor).Module))
                    {
                        Assert.That(a.IsWalkable(a.Routes.GetCell(cursor).Center, Fix64.Ratio(85, 100)), Is.True);
                        cursor = a.Routes.ParentCell(cursor); detour++;
                    }
                    Assert.That(cursor, Is.GreaterThanOrEqualTo(0), "Side trail is disconnected");
                    Assert.That(detour, Is.GreaterThanOrEqualTo(6), "Cache needs a real exploration detour");
                }
                var point = new FixVec2(Fix64.FromInt(-100), Fix64.FromInt(-100));
                Assert.That(a.IsWalkable(a.ClampToWalkable(point, Fix64.Ratio(85, 100)), Fix64.Ratio(85, 100)), Is.True);
            }
            Assert.That(ponds, Is.GreaterThan(0));
        }

        [Test]
        public void BossGlade_IsLarger_AndHasAWalkableApproach()
        {
            var modules = PrototypeContent.Modules();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var map = new LayoutMap(modules, 64);
                GladeLayout.Generate(modules, map, seed, 19, true);
                Assert.That(map.GladeCount, Is.EqualTo(1));
                var center = map.CenterOf(map.GetPlaced(map.GetExit(0)).Parent);
                Assert.That(center, Is.EqualTo(map.GetGlade(0).Center));
                Assert.That(FixVec2.Distance(center, map.EntryPoint), Is.GreaterThan(Fix64.FromInt(25)));
                Assert.That(map.GetGlade(0).Radii.X * map.GetGlade(0).Radii.Y, Is.GreaterThan(Fix64.FromInt(300)));
                for (int c = 0; c < map.Routes.CellCount; c++) Assert.That(map.Routes.DistanceFromEntry(c), Is.GreaterThanOrEqualTo(0));
                Assert.That(map.RewardBranchCount, Is.Zero);
            }
        }
    }
}
