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
                Assert.That(a.RiverCount, Is.GreaterThan(0));
                for (int r = 0; r < a.RiverCount; r++)
                {
                    var river = a.GetRiver(r);
                    var approach = river.Along * (river.HalfWidth + Fix64.One);
                    Assert.That(a.CanTravel(river.Center - approach, river.Center + approach,
                        Fix64.Ratio(85, 100)), Is.True, $"Bridge blocked, seed {seed}");
                    Assert.That(a.IsWalkable(river.Point(Fix64.FromInt(4)), Fix64.Half), Is.False);
                }
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
                    int parent = a.Routes.ParentCell(c);
                    if (parent >= 0)
                        Assert.That(a.CanTravel(a.Routes.GetCell(parent).Center, a.Routes.GetCell(c).Center,
                            Fix64.Ratio(85, 100)), Is.True, $"Blocked route: seed {seed}, cell {c}, parent {parent}");
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

        /// <summary>
        /// Поляна босса (владелец, 02.10.2026): ВСЕГДА ОДНА И ТА ЖЕ. Скруглённый
        /// пол 20 × 15 м без воды, рек и тайников; вход снизу экрана (−Y), выход
        /// сверху; комната перед выходом (там встаёт босс) — центр поляны. Ни сид,
        /// ни размер арены (ArenaFlow даёт боссу 4), ни число модулей её не меняют —
        /// и лесная локация (уровень 9, сплошной лес) собирает ту же карту.
        /// </summary>
        [Test]
        public void BossGlade_IsFixed_TwentyByFifteen_Dry_EntranceAtTheBottom()
        {
            var modules = PrototypeContent.Modules();
            var reference = new LayoutMap(modules, 64);
            GladeLayout.Generate(modules, reference, 1, 20, true);
            var glade = reference.GetGlade(0);
            var forest = ArenaEncounterTests.ForestLocation();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                for (int size = 0; size <= 4; size++)
                {
                    if (size == 1) continue;
                    var map = new LayoutMap(modules, 64);
                    GladeLayout.Generate(modules, map, seed * 7919 + (ulong)size, 11 + (int)(seed % 10), true, size);
                    SameBossGlade(reference, map, "seed " + seed + ", size " + size);
                }
                SameBossGlade(reference, ArenaEncounterTests.ArenaMap(forest, 9, seed), "forest level 9, seed " + seed);
            }

            Assert.That(reference.GladeCount, Is.EqualTo(1));
            Assert.That(glade.Shape, Is.EqualTo(GladeShape.Rounded));
            Assert.That(glade.Turn, Is.Zero);
            Assert.That(reference.WaterCount, Is.Zero, "без озера");
            Assert.That(reference.RiverCount, Is.Zero, "без реки");
            Assert.That(reference.RewardBranchCount, Is.Zero, "без тайников");
            Assert.That(reference.ObstacleCount, Is.Zero);
            Assert.That(reference.PlacedCount, Is.EqualTo(GladeLayout.RequiredModules(20, true)));

            // Пол: 20 м поперёк через центр; 15 м вдоль — в стороне от троп входа и выхода.
            Assert.That(FloorSpan(reference, glade.Center, 1, 0), Is.EqualTo(20).Within(0.1));
            foreach (int side in new[] { -5, -3, 3, 5 })
                Assert.That(FloorSpan(reference, glade.Center + new FixVec2(Fix64.FromInt(side), Fix64.Zero), 0, 1),
                    Is.EqualTo(15).Within(0.6), "вдоль поляны в " + side + " м от оси");
            Assert.That(glade.Field(glade.Center + new FixVec2(Fix64.Ratio(98, 10), Fix64.Zero)), Is.LessThanOrEqualTo(Fix64.One));
            Assert.That(glade.Field(glade.Center + new FixVec2(Fix64.Ratio(102, 10), Fix64.Zero)), Is.GreaterThan(Fix64.One));

            // Вход снизу экрана, выход сверху: оба на оси поляны, тропа ведёт вверх.
            var entry = reference.EntryPoint;
            var exit = reference.ExitPoint(0);
            Assert.That(entry.X, Is.EqualTo(glade.Center.X));
            Assert.That(exit.X, Is.EqualTo(glade.Center.X));
            Assert.That(entry.Y, Is.LessThan(glade.Center.Y - GladeLayout.BossFloorHalfDepth - Fix64.FromInt(8)), "старт в тропе входа");
            Assert.That(exit.Y, Is.GreaterThan(glade.Center.Y + GladeLayout.BossFloorHalfDepth + Fix64.FromInt(8)));
            Assert.That(reference.Routes.EntryFacing, Is.EqualTo(new FixVec2(Fix64.Zero, Fix64.One)));
            Assert.That(reference.CenterOf(reference.GetPlaced(reference.GetExit(0)).Parent), Is.EqualTo(glade.Center),
                "комната перед выходом — центр поляны: там встаёт босс");

            // Вся поляна открыта: из центра телом Хозяина Чащи по прямой до любой точки пола.
            var body = EnemyArchetypes.ThicketMasterBodyRadius;
            for (int y = -7; y <= 7; y++)
                for (int x = -10; x <= 10; x++)
                {
                    var point = glade.Center + new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));
                    if (!reference.IsWalkable(point, body)) continue;
                    Assert.That(reference.CanTravel(glade.Center, point, body), Is.True, x + ", " + y);
                }
            for (int c = 0; c < reference.Routes.CellCount; c++)
                Assert.That(reference.Routes.DistanceFromEntry(c), Is.GreaterThanOrEqualTo(0), "cell " + c);
            Assert.That(reference.CanTravel(entry, glade.Center, Fix64.Ratio(85, 100)), Is.True, "тропа входа прямая");
            Assert.That(reference.CanTravel(glade.Center, exit, Fix64.Ratio(85, 100)), Is.True, "тропа выхода прямая");
        }

        private static void SameBossGlade(LayoutMap reference, LayoutMap map, string where)
        {
            Assert.That(map.Hash(), Is.EqualTo(reference.Hash()), where);
            Assert.That(map.GladeCount, Is.EqualTo(1), where);
            var a = reference.GetGlade(0); var b = map.GetGlade(0);
            Assert.That(b.Center, Is.EqualTo(a.Center), where);
            Assert.That(b.Radii, Is.EqualTo(a.Radii), where);
            Assert.That(b.Shape, Is.EqualTo(a.Shape), where);
            Assert.That(b.Turn, Is.EqualTo(a.Turn), where);
            Assert.That(map.WaterCount, Is.Zero, where);
            Assert.That(map.ObstacleCount, Is.Zero, where);
            Assert.That(map.EntryPoint, Is.EqualTo(reference.EntryPoint), where);
            Assert.That(map.ExitPoint(0), Is.EqualTo(reference.ExitPoint(0)), where);
        }

        /// <summary>Длина пола по прямой через точку, метры: шаг 5 см в обе стороны.</summary>
        private static double FloorSpan(LayoutMap map, FixVec2 through, int dx, int dy)
        {
            var step = new FixVec2(Fix64.FromInt(dx), Fix64.FromInt(dy)) * Fix64.Ratio(1, 20);
            int forward = 0, back = 0;
            while (forward < 2000 && map.ContainsWorld(through + step * Fix64.FromInt(forward + 1))) forward++;
            while (back < 2000 && map.ContainsWorld(through - step * Fix64.FromInt(back + 1))) back++;
            return (forward + back + 1) / 20.0;
        }
    }
}
