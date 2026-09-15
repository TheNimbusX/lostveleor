using System.Collections.Generic;

namespace Game.Sim
{
    // A collision footprint, independent of meshes and Unity physics.
    public readonly struct LayoutObstacle
    {
        public readonly FixVec2 Center;
        public readonly Fix64 Radius;
        public readonly int VisualKind;
        public LayoutObstacle(FixVec2 center, Fix64 radius, int visualKind)
        { Center = center; Radius = radius; VisualKind = visualKind; }

        internal static LayoutObstacle[] Generate(LayoutMap map, ulong seed)
        {
            var result = new List<LayoutObstacle>();
            for (int m = 1; m < map.PlacedCount; m++)
            {
                var p = map.GetPlaced(m);
                var rng = new Pcg32(seed ^ ((ulong)m * 0x9E3779B97F4A7C15UL), 0x524F434BUL);
                for (int corner = 0; corner < 4; corner++)
                {
                    if (rng.NextInt(0, 100) >= 65) continue;
                    int kind = rng.NextInt(0, 4) == 0 ? 1 : 0;
                    var radius = kind == 1 ? Fix64.Ratio(55, 100) : Fix64.Ratio(9, 10);
                    var inset = rng.NextFix(Fix64.Ratio(23, 10), Fix64.FromInt(3));
                    var point = new FixVec2(LayoutMap.CellSize * ((corner & 1) == 0 ? p.OriginX : p.OriginX + p.Width)
                        + ((corner & 1) == 0 ? inset : -inset),
                        LayoutMap.CellSize * ((corner & 2) == 0 ? p.OriginY : p.OriginY + p.Height)
                        + ((corner & 2) == 0 ? inset : -inset));
                    if (!map.IsWalkable(point, radius) || FixVec2.DistanceSq(point, map.EntryPoint) < Fix64.FromInt(196)
                        || map.Routes.NearRoad(point, radius + Fix64.FromInt(3))) continue;
                    bool blocked = false;
                    var definition = map.Modules.Get(p.ModuleIndex);
                    for (int c = 0; c < definition.ConnectorCount; c++)
                    {
                        var connector = definition.RotatedConnector(c, p.Quarters);
                        var center = new FixVec2(LayoutMap.CellSize * (p.OriginX + connector.X) + Fix64.One,
                            LayoutMap.CellSize * (p.OriginY + connector.Y) + Fix64.One);
                        if (FixVec2.DistanceSq(point, center) < Fix64.FromInt(16)) blocked = true;
                    }
                    foreach (var other in result)
                    {
                        var gap = radius + other.Radius + Fix64.FromInt(2);
                        if (FixVec2.DistanceSq(point, other.Center) < gap * gap) blocked = true;
                    }
                    if (!blocked) result.Add(new LayoutObstacle(point, radius, kind));
                }
            }
            return result.ToArray();
        }
    }
}
