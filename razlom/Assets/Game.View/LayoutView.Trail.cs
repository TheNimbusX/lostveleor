using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private const int TrailResolution = 1024;
        private Texture2D _trailMask;
        private readonly byte[] _trailPixels = new byte[TrailResolution * TrailResolution];
        private Vector4 _trailBounds;

        private void BuildNaturalTrail(LayoutMap map)
        {
            if (_trailMask == null)
                _trailMask = new Texture2D(TrailResolution, TrailResolution, TextureFormat.R8, false, true)
                { name = "Плавная тропа", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            System.Array.Clear(_trailPixels, 0, _trailPixels.Length);
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var p = map.GetPlaced(m);
                float cell = LayoutMap.CellSize.ToFloat();
                minX = Mathf.Min(minX, p.OriginX * cell); minZ = Mathf.Min(minZ, p.OriginY * cell);
                maxX = Mathf.Max(maxX, (p.OriginX + p.Width) * cell); maxZ = Mathf.Max(maxZ, (p.OriginY + p.Height) * cell);
            }
            _trailBounds = new Vector4(minX - 2, minZ - 2, maxX - minX + 4, maxZ - minZ + 4);
            var routes = map.Routes;
            var anchors = new HashSet<int> { routes.CellAt(map.EntryPoint) };
            for (int e = 0; e < map.ExitCount; e++) anchors.Add(routes.CellAt(map.ExitPoint(e)));
            for (int b = 0; b < map.RewardBranchCount; b++) anchors.Add(routes.CellAt(map.CenterOf(map.GetRewardBranch(b))));
            var neighbors = new List<int>[routes.CellCount];
            for (int i = 0; i < routes.CellCount; i++)
            {
                if (!routes.IsRoadCell(i)) continue;
                int parent = routes.ParentCell(i);
                if (parent < 0 || !routes.IsRoadCell(parent)) continue;
                if (neighbors[i] == null) neighbors[i] = new List<int>(3);
                if (neighbors[parent] == null) neighbors[parent] = new List<int>(3);
                neighbors[i].Add(parent); neighbors[parent].Add(i);
            }
            var visited = new HashSet<long>();
            for (int i = 0; i < routes.CellCount; i++)
            {
                if (!routes.IsRoadCell(i)) continue;
                var adjacent = neighbors[i];
                if (adjacent == null) { PaintTrailDisc(map, TrailPoint(routes.GetCell(i).Center), .8f); continue; }
                // Preserve junctions and endpoints, not every intermediate cell.
                if (adjacent.Count == 2 && !anchors.Contains(i)) continue;
                foreach (int next in adjacent)
                {
                    if (!visited.Add(TrailEdge(i, next))) continue;
                    var path = new List<Vector2> { TrailPoint(routes.GetCell(i).Center) };
                    int previous = i, current = next;
                    bool main = true;
                    while (true)
                    {
                        path.Add(TrailPoint(routes.GetCell(current).Center));
                        main &= routes.IsMainModule(routes.GetCell(current).Module);
                        if (neighbors[current].Count != 2 || anchors.Contains(current)) break;
                        int following = neighbors[current][0] == previous ? neighbors[current][1] : neighbors[current][0];
                        visited.Add(TrailEdge(current, following));
                        previous = current; current = following;
                    }
                    float width = main ? 1 : .7f;
                    float clearance = Mathf.Min(.8f, _style.RouteWidth * .4f);
                    var curved = MeadowTrailPath.Curve(map, MeadowTrailPath.Simplify(map, path, clearance),
                        clearance, _style.TrailBend, DecorRandom(i, 733 + next));
                    for (int p = 1; p < curved.Count; p++)
                        PaintTrailCurve(map, curved[p - 1], (curved[p - 1] + curved[p]) * .5f, curved[p], width);
                }
            }
            PaintTrailDisc(map, TrailPoint(map.EntryPoint), .9f);
            for (int i = 0; i < map.ExitCount; i++) PaintTrailDisc(map, TrailPoint(map.ExitPoint(i)), .85f);
            for (int i = 0; i < map.RewardBranchCount; i++)
            {
                var target = map.CenterOf(map.GetRewardBranch(i));
                int cell = routes.CellAt(target);
                if (cell < 0) continue;
                Vector2 start = TrailPoint(routes.GetCell(cell).Center), end = TrailPoint(target);
                PaintTrailCurve(map, start, (start + end) * .5f, end, .7f);
                PaintTrailDisc(map, end, .75f);
            }
            _trailMask.SetPixelData(_trailPixels, 0);
            _trailMask.Apply(false, false);
            _roomMaterial.SetTexture("_TrailMask", _trailMask);
            _roomMaterial.SetVector("_TrailBounds", _trailBounds);
            BuildClearings(map);
        }

        private static Vector2 TrailPoint(FixVec2 point) => new Vector2(point.X.ToFloat(), point.Y.ToFloat());

        private bool NearNaturalTrail(float x, float z, float radius)
        {
            if (_trailMask == null) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(radius * 2 / .35f));
            for (int iy = 0; iy <= steps; iy++)
                for (int ix = 0; ix <= steps; ix++)
                {
                    float dx = Mathf.Lerp(-radius, radius, ix / (float)steps);
                    float dz = Mathf.Lerp(-radius, radius, iy / (float)steps);
                    if (dx * dx + dz * dz > radius * radius) continue;
                    float u = (x + dx - _trailBounds.x) / _trailBounds.z;
                    float v = (z + dz - _trailBounds.y) / _trailBounds.w;
                    if (u >= 0 && u <= 1 && v >= 0 && v <= 1 && (_trailMask.GetPixelBilinear(u, v).r > .1f
                        || (_style.ForestClearings && _clearingMask != null && _clearingMask.GetPixelBilinear(u, v).r > .3f))) return true;
                }
            return false;
        }
        private static long TrailEdge(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        private void PaintSimplifiedTrail(LayoutMap map, List<Vector2> points, float width)
        {
            Vector2 last = points[0];
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 corner = points[i];
                float trim = Mathf.Min(3, Vector2.Distance(points[i - 1], corner) * .4f, Vector2.Distance(corner, points[i + 1]) * .4f);
                Vector2 a = corner, b = corner;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    a = corner + (points[i - 1] - corner).normalized * trim;
                    b = corner + (points[i + 1] - corner).normalized * trim;
                    // The chord bounds the inside of this convex quadratic bend.
                    if (MeadowTrailPath.IsClear(map, a, b, Mathf.Min(.8f, _style.RouteWidth * .4f))) break;
                    trim *= .5f;
                    if (attempt == 5) a = b = corner;
                }
                PaintTrailCurve(map, last, (last + a) * .5f, a, width);
                PaintTrailCurve(map, a, corner, b, width);
                last = b;
            }
            Vector2 end = points[points.Count - 1];
            PaintTrailCurve(map, last, (last + end) * .5f, end, width);
        }

        private void PaintTrailCurve(LayoutMap map, Vector2 a, Vector2 control, Vector2 b, float width = 1)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((Vector2.Distance(a, control) + Vector2.Distance(control, b)) / .12f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector2 point = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
                float variation = .82f + .32f * Mathf.PerlinNoise(point.x * .13f + 17, point.y * .13f + 41);
                PaintTrailDisc(map, point, _style.RouteWidth * .5f * variation * width);
            }
        }

        private void PaintTrailDisc(LayoutMap map, Vector2 point, float radius)
        {
            // Include a texel of padding for bilinear filtering; never cut across an outside corner.
            float padding = Mathf.Max(_trailBounds.z, _trailBounds.w) / TrailResolution * 1.5f;
            var fixedPoint = new FixVec2(Fix64.FromDouble(point.x), Fix64.FromDouble(point.y));
            while (radius > .2f && !map.IsWalkable(fixedPoint, Fix64.FromDouble(radius + padding))) radius *= .8f;
            if (radius <= .2f) return;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((point.x - radius - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((point.x + radius - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((point.y - radius - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((point.y + radius - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var world = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                        _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                    float distance = Vector2.Distance(world, point) / radius;
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01((1 - distance) / .45f) * 255);
                    int index = y * TrailResolution + x;
                    if (value > _trailPixels[index]) _trailPixels[index] = value;
                }
        }
    }
}
