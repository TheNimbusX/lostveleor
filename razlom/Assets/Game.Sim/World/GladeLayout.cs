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
        /// <summary>Уступ между сегментами: полуширина линии обрыва и высота, метры.</summary>
        public static readonly Fix64 LedgeHalfWidth = Fix64.FromInt(6), LedgeDrop = Fix64.FromInt(3);
        /// <summary>Доля проходов между сегментами с уступом, %.</summary>
        public const int LedgeChancePercent = 50;
        public static int ClearingCount(int targetModules) => targetModules >= 18 ? 5 : targetModules >= 14 ? 4 : 3;
        public static int RequiredModules(int targetModules, bool boss) => boss ? BossModules : ClearingCount(targetModules) * 5 + 7;

        // ---- поляна босса (владелец, 02.10.2026) ----

        /// <summary>
        /// Полуоси пола поляны босса, метры: 20 × 15 м. X — поперёк экрана,
        /// Y — от входа (низ экрана, −Y) к выходу.
        /// </summary>
        public static readonly Fix64 BossFloorHalfWidth = Fix64.FromInt(10), BossFloorHalfDepth = Fix64.Ratio(15, 2);

        /// <summary>
        /// Радиусы области поляны босса (GladeRegion.Radii). Форма Rounded
        /// заполняет 0,86 своего прямоугольника (GladeRegion.Field), поэтому
        /// радиусы — полуоси пола / 0,86 ≈ 11,63 × 8,72. Оформление (LayoutView)
        /// кладёт кайму и камни на 0,86–0,96 радиуса — как раз у кромки пола.
        /// </summary>
        public static readonly FixVec2 BossClearingRadii = new FixVec2(
            BossFloorHalfWidth / Fix64.Ratio(86, 100), BossFloorHalfDepth / Fix64.Ratio(86, 100));

        /// <summary>Залов поперёк и вдоль поляны босса: средний зал — её центр, в нём встаёт босс.</summary>
        public const int BossHallsAcross = 3;

        /// <summary>Модулей у поляны босса: вход, 3 × 3 зала, выход.</summary>
        public const int BossModules = BossHallsAcross * BossHallsAcross + 2;

        /// <summary>
        /// Обычная поляна — по сиду (форма, поворот, вода, ветки к тайникам). Босс (boss) — всегда одна
        /// и та же поляна, без сида (GenerateBossClearing). segments — сколько полян-сегментов у обычной
        /// арены (владелец, 2 октября): 2–3 поляны по arenaSize модулей в поперечнике, цепочкой от входа
        /// к выходу. У босса и старого уровня не учитывается.
        /// </summary>
        public static void Generate(ModuleSet modules, LayoutMap map, ulong seed, int targetModules, bool boss = false, int arenaSize = 0,
            int segments = 1)
        {
            int Find(string key)
            {
                for (int i = 0; i < modules.Count; i++) if (modules.Get(i).Id == StableId.Of(key)) return i;
                throw new ArgumentException("Glade layout requires " + key);
            }
            int hall = Find("module.hall"), pocket = hall, entrance = modules.FindEntrance();
            if (entrance < 0) throw new ArgumentException("Glade layout requires an entrance.");
            if (boss) { GenerateBossClearing(modules, map, hall, entrance); return; }
            var rng = new Pcg32(seed, 0x474C414445UL);
            bool singleArena = boss || arenaSize > 0;
            int turn = rng.NextInt(0, 4), count = singleArena ? boss ? 1 : Math.Max(1, segments) : ClearingCount(targetModules),
                across = arenaSize > 0 ? arenaSize : boss ? 3 : 2;
            var shapeRng = new Pcg32(seed, 0x534841504553UL);
            int firstShape = shapeRng.NextInt(0, 6);
            var branchRng = new Pcg32(seed, 0x5349444550415448UL);
            int firstBranch = singleArena ? -1 : branchRng.NextInt(0, count - 1);
            int secondBranch = singleArena ? -1 : branchRng.NextInt(firstBranch + 1, count);
            bool firstLeft = branchRng.NextInt(0, 2) == 0;
            int w = modules.Get(hall).Width, h = modules.Get(hall).Height;
            int width = w * across, height = h * across;
            int ew = modules.Get(entrance).Width, eh = modules.Get(entrance).Height;
            int pw = modules.Get(pocket).Width, ph = modules.Get(pocket).Height;
            var regions = new GladeRegion[count];
            var links = new List<(FixVec2 A, FixVec2 B, Fix64 Radius)>();
            var pockets = new List<int>();
            var segmentLinks = new List<int>();
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
            // Вода обычной арены: река с каменным бродом (у арены из сегментов — поперёк прохода между
            // ними, у одной поляны — перед выходом) и одно озеро у края. Река не у входа: враги не ищут
            // путь и упирались бы в берег, пока герой стоит за рекой на старте. Отдельный поток
            // не сдвигает остальные броски.
            var waterKind = new Pcg32(seed, 0x4C414B45UL);
            bool riverArena = singleArena && waterKind.NextInt(0, 3) != 0;
            Place(entrance, middle, -eh, -1);
            int previous = 0;
            FixVec2 previousCenter = map.CenterOf(0);
            for (int g = 0; g < count; g++)
            {
                int originY = g * (height + eh);
                // Small alternating shifts change the approach angle without folding the whole route into a snake.
                int originX = boss || g == 0 ? 0 : rng.NextInt(-1, 2);
                if (g > 0) { previous = Place(entrance, middle, originY - eh, previous); segmentLinks.Add(previous); }
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
                // Арена чуть теснее блока модулей: бой собраннее, по краю больше леса.
                if (singleArena) worldRadii = worldRadii * Fix64.Ratio(85, 100);
                // Different neighbouring silhouettes, selected independently of layout and spawns.
                regions[g] = new GladeRegion(center, worldRadii,
                    (GladeShape)((firstShape + g) % 6), shapeRng.NextInt(0, 4));
                links.Add((previousCenter, center, Fix64.Ratio(22, 10)));
                previousCenter = center;
                previous = body[across / 2, across - 1];
                if (!singleArena && (g == firstBranch || g == secondBranch))
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
            // Речной арене из одной поляны между ней и выходом нужен коридор-модуль: по нему идёт река с
            // бродом. У арены из сегментов река пересекает проход между ними — посередине локации
            // (владелец, 5 октября), а коридора у выхода нет.
            int riverLink = riverArena && segmentLinks.Count > 0 ? new Pcg32(seed, 0x524956454C4E4BUL).NextInt(0, segmentLinks.Count) : -1;
            int exitParent = previous, exitRow = (count - 1) * (height + eh) + height, ford = -1;
            if (riverArena && riverLink < 0) { ford = Place(entrance, middle, exitRow, exitParent); exitParent = ford; exitRow += eh; }
            int exit = Place(entrance, middle, exitRow, exitParent);
            links.Add((previousCenter, map.CenterOf(exit), Fix64.Ratio(22, 10)));
            // Water is a hole in the actual floor, so movement, navigation and rendering agree.
            var rivers = new List<LayoutRiver>();
            var riverRng = new Pcg32(seed, 0x5249564552UL);
            var acrossRiver = Rotate(new FixVec2(Fix64.One, Fix64.Zero), turn);
            // Река между полянами — только у старого уровня: сегменты арены связаны сухим проходом.
            for (int g = 1; g < count && !singleArena; g += 2)
                rivers.Add(new LayoutRiver((regions[g - 1].Center + regions[g].Center) / Fix64.FromInt(2),
                    acrossRiver, riverRng.NextFix(Fix64.One, Fix64.FromInt(3))));
            // Река арены пересекает проход между сегментами или коридор выхода: брод шириной 6 м лежит
            // ровно на тропе.
            if (riverArena)
                rivers.Add(new LayoutRiver(riverLink >= 0 ? PassagePoint(regions, riverLink) : map.CenterOf(ford), acrossRiver,
                    riverRng.NextFix(Fix64.One, Fix64.FromInt(2)), Fix64.FromInt(3)));
            map.SetRivers(rivers.ToArray());
            var water = new List<LayoutObstacle>();
            var waterRng = new Pcg32(seed, 0x5741544552UL);
            // Озеро у сегментированной арены одно — у случайного сегмента, а не у каждого.
            int lakeSegment = count == 1 ? 0 : new Pcg32(seed, 0x4C414B4553454755UL).NextInt(0, count);
            for (int segment = 0; segment < count; segment++)
            {
                var region = regions[segment];
                // У каждой обычной арены, и речной тоже, — одно озеро, врезанное в край поляны (владелец,
                // 2 октября: «используй озеро как край арены»); у босса — одно озеро посреди поляны
                // с сухим кольцом вокруг.
                bool lake = singleArena && !boss;
                int wanted = boss ? 1 : singleArena ? (lake && segment == lakeSegment ? 1 : 0) : 2;
                for (int attempt = 0, placed = 0; attempt < 200 && placed < wanted; attempt++)
                {
                    Fix64 radius; FixVec2 point; bool clear;
                    if (lake)
                    {
                        // Озеро — край арены (владелец, 29 сентября): крупное, по размеру поляны. Центр за
                        // настоящей кромкой пола (форма поляны уже её радиусов), вода заходит на пол на 60–85%
                        // своего радиуса — берег забирает у поляны длинную дугу, остальное в лесу (2 октября: крупнее).
                        radius = Fix64.Min(region.Radii.X, region.Radii.Y) * waterRng.NextFix(Fix64.Ratio(62, 100), Fix64.Ratio(78, 100));
                        var angle = waterRng.NextFix(Fix64.Zero, Fix64.TwoPi);
                        var outside = waterRng.NextFix(Fix64.Ratio(15, 100), Fix64.Ratio(40, 100));
                        var direction = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle));
                        var edge = Fix64.Zero;
                        while (edge < region.Radii.X + region.Radii.Y && region.Field(region.Center + direction * edge) <= Fix64.One)
                            edge += Fix64.Ratio(1, 4);
                        point = region.Center + direction * (edge + radius * outside);
                        // Озеро — край арены, а не пруд в лесной траве рядом (владелец, 2 октября): не меньше
                        // 30% берега лежит на полу поляны. Во впадине полумесяца или между половинами
                        // «близнецов» луч выходил из пола рано, и вода касалась пола коротким отрезком.
                        int onFloor = 0;
                        for (int s = 0; s < 24; s++)
                        {
                            var around = Fix64.TwoPi * Fix64.Ratio(s, 24);
                            if (region.Field(point + new FixVec2(Fix64.Cos(around), Fix64.Sin(around)) * radius) <= Fix64.One) onFloor++;
                        }
                        clear = onFloor >= 7;
                    }
                    else
                    {
                        // Озёра, а не лужи: было 1.6-2.6, стало заметно крупнее.
                        radius = waterRng.NextFix(Fix64.Ratio(22, 10), Fix64.Ratio(38, 10));
                        point = region.Center + new FixVec2(
                            waterRng.NextFix(-region.Radii.X, region.Radii.X) * Fix64.Ratio(7, 10),
                            waterRng.NextFix(-region.Radii.Y, region.Radii.Y) * Fix64.Ratio(7, 10));
                        clear = FixVec2.DistanceSq(point, region.Center) > Fix64.FromInt(25);
                        // A dry ring wide enough for enemies must remain around the entire pond.
                        var margin = radius + Fix64.FromInt(3);
                        for (int d = 0; d < 16 && clear; d++)
                        {
                            var angle = Fix64.TwoPi * Fix64.Ratio(d, 16);
                            var offset = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * margin;
                            if (region.Field(point + offset) > Fix64.One) clear = false;
                        }
                    }
                    foreach (var link in links)
                        if (Corridor(point, link.A, link.B, radius + link.Radius + Fix64.FromInt(2))) clear = false;
                    foreach (var river in rivers)
                        if (river.ContainsWater(point, radius + Fix64.FromInt(3))) clear = false;
                    foreach (var other in water)
                        if (FixVec2.Distance(point, other.Center) < radius + other.Radius + Fix64.FromInt(4)) clear = false;
                    if (!clear) continue;
                    water.Add(new LayoutObstacle(point, radius, 2)); placed++;
                }
            }
            Func<FixVec2, bool> contour = point =>
            {
                foreach (var river in rivers) if (river.Blocks(point)) return false;
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
            map.IsArena = singleArena;
            // Уступ — примерно на половине проходов между сегментами арены (владелец, 2 октября: «не нужно
            // каждый переход делать таким»): следующий сегмент ниже, проход — склон, по нему ходят в обе
            // стороны. Остальные проходы ровные. Свой поток: прочие броски карты не сдвигаются.
            if (singleArena && !boss && segmentLinks.Count > 0)
            {
                var ledgeRng = new Pcg32(seed, 0x4C45444745UL);
                var ledges = new List<LayoutLedge>();
                for (int i = 0; i < segmentLinks.Count; i++)
                {
                    // Бросок — до проверки реки: у карт без реки уступы те же, что раньше.
                    if (ledgeRng.NextInt(0, 100) >= LedgeChancePercent || i == riverLink) continue;
                    ledges.Add(new LayoutLedge(PassagePoint(regions, i),
                        (regions[i + 1].Center - regions[i].Center).Normalized(), LedgeHalfWidth, LedgeDrop));
                }
                map.SetLedges(ledges.ToArray());
            }
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

        /// <summary>
        /// Поляна Хозяина Чащи (владелец, 02.10.2026): ВСЕГДА ОДНА И ТА ЖЕ —
        /// без сида, без поворота, без воды, без тайников. Скруглённый пол
        /// 20 × 15 м (BossFloorHalfWidth/Depth), вход снизу экрана (−Y), выход
        /// с порталом после победы — сверху, продолжая путь входа.
        ///
        /// Модули: вход, под ним 3 × 3 зала, над ними выход. Центр среднего зала
        /// — центр поляны: босс встаёт в «комнате перед выходом»
        /// (Simulation.SetupBossArena / SetupThicketMasterArena берут центр
        /// родителя выхода), т. е. посреди поляны. Пол вне поляны — лес; от
        /// входа и к выходу ведут тропы шириной 4,4 м, как у обычных арен.
        /// Бросков случайности здесь нет — карты прочих уровней не сдвигаются.
        /// </summary>
        private static void GenerateBossClearing(ModuleSet modules, LayoutMap map, int hall, int entrance)
        {
            const int across = BossHallsAcross;
            int w = modules.Get(hall).Width, h = modules.Get(hall).Height;
            int width = w * across, height = h * across;
            int ew = modules.Get(entrance).Width, eh = modules.Get(entrance).Height;
            // Залы обязаны вместить прямоугольник формы, иначе пол поляны обрежут стены модулей.
            if (LayoutMap.CellSize * width < BossClearingRadii.X * 2 || LayoutMap.CellSize * height < BossClearingRadii.Y * 2)
                throw new ArgumentException("Boss clearing does not fit into " + across + "×" + across + " halls.");
            map.Clear();
            int Place(int index, int x, int y, int parent)
            {
                int placed = map.TryPlace(index, 0, x, y, parent);
                if (placed < 0) throw new InvalidOperationException("Insufficient boss clearing capacity or overlapping authoring.");
                return placed;
            }
            int middle = (width - ew) / 2;
            int start = Place(entrance, middle, -eh, -1);
            var body = new int[across, across];
            for (int y = 0; y < across; y++)
                for (int x = 0; x < across; x++)
                    body[x, y] = Place(hall, x * w, y * h, y > 0 ? body[x, y - 1] : start);
            int arena = body[across / 2, across / 2];
            int exit = Place(entrance, middle, height, arena);
            var region = new GladeRegion(map.CenterOf(arena), BossClearingRadii, GladeShape.Rounded);
            var trailIn = (A: map.CenterOf(start), B: region.Center);
            var trailOut = (A: region.Center, B: map.CenterOf(exit));
            var trail = Fix64.Ratio(22, 10);
            Func<FixVec2, bool> contour = point => region.Field(point) <= Fix64.One
                || Corridor(point, trailIn.A, trailIn.B, trail) || Corridor(point, trailOut.A, trailOut.B, trail);
            map.SetGlades(new[] { region });
            while (map.OpenCount > 0) map.CloseOpen(map.OpenCount - 1);
            map.AddExit(exit);
            map.SetNaturalOutline(contour); map.BuildRoutes();
            map.Routes.MarkMainRoutes(map); map.Routes.MarkBranchRoutes(map);
        }

        /// <summary>Проход между сегментами i и i + 1 — прямая между центрами полян; его середина между их краями.</summary>
        private static FixVec2 PassagePoint(GladeRegion[] regions, int i)
        {
            FixVec2 a = regions[i].Center, b = regions[i + 1].Center, step = (b - a) / Fix64.FromInt(200);
            int leave = 0, enter = 200;
            while (leave < 200 && regions[i].Field(a + step * Fix64.FromInt(leave)) <= Fix64.One) leave++;
            while (enter > 0 && regions[i + 1].Field(a + step * Fix64.FromInt(enter)) <= Fix64.One) enter--;
            return a + step * Fix64.FromInt((leave + enter) / 2);
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
