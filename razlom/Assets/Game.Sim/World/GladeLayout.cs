using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum GladeShape { Oval, WideOval, Pear, Twin, Rounded, Crescent }

    public readonly struct GladeRegion
    {
        public readonly FixVec2 Center, Radii;
        public readonly GladeShape Shape;
        public readonly int Turn;
        public GladeRegion(FixVec2 center, FixVec2 radii, GladeShape shape = GladeShape.Oval, int turn = 0)
        { Center = center; Radii = radii; Shape = shape; Turn = turn; }

        // Shared implicit contour for simulation and ground painting: <= 1 is inside.
        public Fix64 Field(FixVec2 point)
        {
            var delta = point - Center;
            var x = delta.X / Radii.X; var y = delta.Y / Radii.Y;
            if (Fix64.Abs(x) > Fix64.One || Fix64.Abs(y) > Fix64.One) return Fix64.FromInt(2);
            var oldX = x;
            if (Turn == 1) { x = -y; y = oldX; }
            else if (Turn == 2) { x = -x; y = -y; }
            else if (Turn == 3) { x = y; y = -oldX; }
            switch (Shape)
            {
                case GladeShape.WideOval: return Oval(x, y, 96, 66);
                case GladeShape.Pear:
                    x /= Fix64.Ratio(72, 100) + y * Fix64.Ratio(22, 100);
                    y /= Fix64.Ratio(94, 100);
                    return x * x + y * y;
                case GladeShape.Twin:
                    return Fix64.Min(Oval(x - Fix64.Ratio(32, 100), y, 61, 76),
                        Oval(x + Fix64.Ratio(32, 100), y, 61, 76));
                case GladeShape.Rounded:
                    x /= Fix64.Ratio(86, 100); y /= Fix64.Ratio(86, 100);
                    return x * x * x * x + y * y * y * y;
                case GladeShape.Crescent:
                    x += Fix64.Ratio(28, 100) * (Fix64.One - y * y);
                    return Oval(x, y, 64, 94);
                default: return Oval(x, y, 76, 96);
            }
        }

        public static GladeRegion ForBranch(LayoutMap map, int placement)
        {
            var room = map.GetPlaced(placement);
            return new GladeRegion(map.CenterOf(placement),
                new FixVec2(Fix64.FromInt(room.Width), Fix64.FromInt(room.Height)),
                (GladeShape)(placement % 6), room.Quarters);
        }

        private static Fix64 Oval(Fix64 x, Fix64 y, int width, int height)
        { x /= Fix64.Ratio(width, 100); y /= Fix64.Ratio(height, 100); return x * x + y * y; }
    }

    public static class GladeLayout
    {
        public static int ClearingCount(int targetModules) => targetModules >= 18 ? 5 : targetModules >= 14 ? 4 : 3;
        public static int RequiredModules(int targetModules, bool boss) => boss ? 12 : ClearingCount(targetModules) * 5 + 7;

        public static void Generate(ModuleSet modules, LayoutMap map, ulong seed, int targetModules, bool boss = false)
        {
            int Find(string key)
            {
                for (int i = 0; i < modules.Count; i++) if (modules.Get(i).Id == StableId.Of(key)) return i;
                throw new ArgumentException("Glade layout requires " + key);
            }
            int hall = Find("module.hall"), pocket = hall, entrance = modules.FindEntrance();
            if (entrance < 0) throw new ArgumentException("Glade layout requires an entrance.");
            var rng = new Pcg32(seed, 0x474C414445UL);
            int turn = rng.NextInt(0, 4), count = boss ? 1 : ClearingCount(targetModules), across = boss ? 3 : 2;
            var shapeRng = new Pcg32(seed, 0x534841504553UL);
            int firstShape = shapeRng.NextInt(0, 6);
            var branchRng = new Pcg32(seed, 0x5349444550415448UL);
            int firstBranch = boss ? -1 : branchRng.NextInt(0, count - 1);
            int secondBranch = boss ? -1 : branchRng.NextInt(firstBranch + 1, count);
            bool firstLeft = branchRng.NextInt(0, 2) == 0;
            int w = modules.Get(hall).Width, h = modules.Get(hall).Height;
            int width = w * across, height = h * across;
            int ew = modules.Get(entrance).Width, eh = modules.Get(entrance).Height;
            int pw = modules.Get(pocket).Width, ph = modules.Get(pocket).Height;
            var regions = new GladeRegion[count];
            var links = new List<(FixVec2 A, FixVec2 B, Fix64 Radius)>();
            var pockets = new List<int>();
            map.Clear();
            int Place(int index, int x, int y, int parent)
            {
                int rw = modules.Get(index).Width, rh = modules.Get(index).Height;
                int rx = x, ry = y;
                if (turn == 1) { rx = -y - rh; ry = x; }
                if (turn == 2) { rx = -x - rw; ry = -y - rh; }
                if (turn == 3) { rx = y; ry = -x - rw; }
                int placed = map.TryPlace(index, turn, rx, ry, parent);
                if (placed < 0) throw new InvalidOperationException("Insufficient glade capacity or overlapping authoring.");
                return placed;
            }
            int middle = (width - ew) / 2;
            Place(entrance, middle, -(boss ? 2 : 1) * eh, -1);
            int previous = boss ? Place(entrance, middle, -eh, 0) : 0;
            FixVec2 previousCenter = map.CenterOf(0);
            int arena = -1;
            for (int g = 0; g < count; g++)
            {
                int originY = g * (height + eh);
                // Small alternating shifts change the approach angle without folding the whole route into a snake.
                int originX = boss || g == 0 ? 0 : rng.NextInt(-1, 2);
                if (g > 0) previous = Place(entrance, middle, originY - eh, previous);
                int[,] body = new int[across, across];
                for (int y = 0; y < across; y++)
                    for (int x = 0; x < across; x++)
                        body[x, y] = Place(hall, originX + x * w, originY + y * h, y > 0 ? body[x, y - 1] : previous);
                var center = Rotate(new FixVec2(Fix64.FromInt(originX * 2 + width), Fix64.FromInt(originY * 2 + height)), turn);
                // Preserve the old contour draws so later module positions do not shift.
                rng.NextFix(Fix64.FromInt(-1), Fix64.One);
                rng.NextFix(Fix64.FromInt(-1), Fix64.One);
                var worldRadii = turn % 2 == 0
                    ? new FixVec2(Fix64.FromInt(width), Fix64.FromInt(height))
                    : new FixVec2(Fix64.FromInt(height), Fix64.FromInt(width));
                // Different neighbouring silhouettes, selected independently of layout and spawns.
                regions[g] = new GladeRegion(center, worldRadii,
                    boss ? GladeShape.Rounded : (GladeShape)((firstShape + g) % 6), shapeRng.NextInt(0, 4));
                links.Add((previousCenter, center, Fix64.Ratio(22, 10)));
                previousCenter = center;
                arena = body[across / 2, across / 2];
                previous = body[across / 2, across - 1];
                if (!boss && (g == firstBranch || g == secondBranch))
                {
                    bool left = g == firstBranch ? firstLeft : !firstLeft;
                    int length = branchRng.NextInt(1, 3);
                    int parent = body[left ? 0 : across - 1, across / 2];
                    for (int step = 0; step < length; step++)
                        parent = Place(entrance, left ? originX - (step + 1) * ew : originX + width + step * ew,
                            originY + height / 2 - eh / 2, parent);
                    int p = Place(pocket, left ? originX - length * ew - pw : originX + width + length * ew,
                        originY + height / 2 - ph / 2, parent);
                    pockets.Add(p); links.Add((map.CenterOf(p), center, Fix64.Ratio(19, 10)));
                }
            }
            int exit = Place(entrance, middle, (count - 1) * (height + eh) + height, boss ? arena : previous);
            links.Add((previousCenter, map.CenterOf(exit), Fix64.Ratio(22, 10)));
            // Water is a hole in the actual floor, so movement, navigation and rendering agree.
            var water = new List<LayoutObstacle>();
            var waterRng = new Pcg32(seed, 0x5741544552UL);
            foreach (var region in regions)
            {
                int wanted = boss ? 2 : 1;
                for (int attempt = 0, placed = 0; attempt < 120 && placed < wanted; attempt++)
                {
                    var radius = waterRng.NextFix(Fix64.FromInt(2), Fix64.FromInt(3));
                    var point = region.Center + new FixVec2(
                        waterRng.NextFix(-region.Radii.X, region.Radii.X) * Fix64.Ratio(7, 10),
                        waterRng.NextFix(-region.Radii.Y, region.Radii.Y) * Fix64.Ratio(7, 10));
                    bool clear = FixVec2.DistanceSq(point, region.Center) > Fix64.FromInt(boss ? 81 : 25);
                    // A dry ring wide enough for enemies must remain around the entire pond.
                    var margin = radius + Fix64.FromInt(3);
                    for (int d = 0; d < 16 && clear; d++)
                    {
                        var angle = Fix64.TwoPi * Fix64.Ratio(d, 16);
                        var offset = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * margin;
                        if (region.Field(point + offset) > Fix64.One) clear = false;
                    }
                    foreach (var link in links)
                        if (Corridor(point, link.A, link.B, radius + link.Radius + Fix64.FromInt(2))) clear = false;
                    foreach (var other in water)
                        if (FixVec2.Distance(point, other.Center) < radius + other.Radius + Fix64.FromInt(4)) clear = false;
                    if (!clear) continue;
                    water.Add(new LayoutObstacle(point, radius, 2)); placed++;
                }
            }
            Func<FixVec2, bool> contour = point =>
            {
                foreach (var pond in water)
                    if (FixVec2.DistanceSq(point, pond.Center) <= pond.Radius * pond.Radius) return false;
                for (int i = 0; i < regions.Length; i++)
                    if (regions[i].Field(point) <= Fix64.One) return true;
                foreach (var link in links) if (Corridor(point, link.A, link.B, link.Radius)) return true;
                foreach (int p in pockets)
                    if (GladeRegion.ForBranch(map, p).Field(point) <= Fix64.One) return true;
                return false;
            };
            map.SetGlades(regions);
            while (map.OpenCount > 0) map.CloseOpen(map.OpenCount - 1);
            map.AddExit(exit);
            foreach (int p in pockets) map.AddRewardBranch(p);
            while (true)
            {
                map.SetNaturalOutline(contour); map.BuildRoutes();
                bool connected = true;
                for (int c = 0; c < map.Routes.CellCount; c++)
                    if (map.Routes.DistanceFromEntry(c) < 0) { connected = false; break; }
                if (connected || water.Count == 0) break;
                // На дискретной сетке берег может отрезать клетку: откатываем последний водоём.
                water.RemoveAt(water.Count - 1);
            }
            map.SetWater(water.ToArray());
            map.Routes.MarkMainRoutes(map); map.Routes.MarkBranchRoutes(map);
        }

        private static FixVec2 Rotate(FixVec2 p, int q)
            => q == 1 ? new FixVec2(-p.Y, p.X) : q == 2 ? -p : q == 3 ? new FixVec2(p.Y, -p.X) : p;
        private static bool Corridor(FixVec2 p, FixVec2 a, FixVec2 b, Fix64 radius)
        {
            var d = b - a; var offset = p - a;
            var t = d.LengthSq == Fix64.Zero ? Fix64.Zero : Fix64.Clamp((offset.X * d.X + offset.Y * d.Y) / d.LengthSq, Fix64.Zero, Fix64.One);
            return FixVec2.DistanceSq(p, a + d * t) <= radius * radius;
        }
    }
}
