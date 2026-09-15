using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Texture2D _clearingMask;
        private Texture2D _wearMask;
        private readonly byte[] _wearPixels = new byte[TrailResolution * TrailResolution];
        private readonly byte[] _clearingPixels = new byte[TrailResolution * TrailResolution];

        private void BuildClearings(LayoutMap map)
        {
            BuildGroundWear(map);
            if (!_style.ForestClearings)
            {
                _roomMaterial.SetTexture("_ClearingMask", Texture2D.blackTexture);
                return;
            }
            if (_clearingMask == null)
                _clearingMask = new Texture2D(TrailResolution, TrailResolution, TextureFormat.R8, false, true)
                { name = "Лесные поляны", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            System.Array.Clear(_clearingPixels, 0, _clearingPixels.Length);
            if (map.Outline != null) PaintWholeGlade(map);
            for (int m = 1; map.Outline == null && m < map.PlacedCount; m++)
            {
                var module = map.GetPlaced(m);
                float width = module.Width * LayoutMap.CellSize.ToFloat();
                float height = module.Height * LayoutMap.CellSize.ToFloat();
                if (Mathf.Min(width, height) < 8) continue; // Narrow connectors remain trails.
                Vector2 center = TrailPoint(map.CenterOf(m));
                // Anchor the clearing to the visible, shortened trail, not the old room centre.
                Vector2 nearest = center;
                float nearestDistance = float.MaxValue;
                float cellSize = LayoutMap.CellSize.ToFloat();
                for (float z = module.OriginY * cellSize + .5f; z < (module.OriginY + module.Height) * cellSize; z += .35f)
                    for (float x = module.OriginX * cellSize + .5f; x < (module.OriginX + module.Width) * cellSize; x += .35f)
                    {
                        float u = (x - _trailBounds.x) / _trailBounds.z, v = (z - _trailBounds.y) / _trailBounds.w;
                        if (_trailMask.GetPixelBilinear(u, v).r < .7f) continue;
                        var candidate = new Vector2(x, z);
                        float distance = (candidate - center).sqrMagnitude;
                        if (distance < nearestDistance) { nearestDistance = distance; nearest = candidate; }
                    }
                if (nearestDistance == float.MaxValue) continue;
                center = Vector2.Lerp(center, nearest, .85f);
                Vector2 radii = new Vector2(width, height) * (.44f * _style.ClearingSize);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - radii.x * 1.15f - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + radii.x * 1.15f - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((center.y - radii.y * 1.15f - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((center.y + radii.y * 1.15f - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                            _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                        Vector2 delta = p - center;
                        float angle = Mathf.Atan2(delta.y, delta.x);
                        float irregular = 1 + .07f * Mathf.Sin(angle * 3 + m * 1.7f) + .04f * Mathf.Sin(angle * 5 - m);
                        float edge = Mathf.Sqrt(delta.x * delta.x / (radii.x * radii.x) + delta.y * delta.y / (radii.y * radii.y)) / irregular;
                        float coverage = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - edge) / .4f));
                        if (coverage <= 0) continue;
                        // Keep the actual portal approaches narrow, even in a large exit module.
                        float portalDistance = Vector2.Distance(p, TrailPoint(map.EntryPoint));
                        for (int e = 0; e < map.ExitCount; e++)
                            portalDistance = Mathf.Min(portalDistance, Vector2.Distance(p, TrailPoint(map.ExitPoint(e))));
                        coverage *= Mathf.SmoothStep(0, 1, Mathf.Clamp01((portalDistance - 2.5f) / 3f));
                        if (coverage <= 0 || !map.IsWalkable(new FixVec2(Fix64.FromDouble(p.x), Fix64.FromDouble(p.y)), Fix64.Ratio(15, 100))) continue;
                        int index = y * TrailResolution + x;
                        _clearingPixels[index] = (byte)Mathf.Max(_clearingPixels[index], Mathf.RoundToInt(coverage * 255));
                    }
            }
            _clearingMask.SetPixelData(_clearingPixels, 0);
            _clearingMask.Apply(false, false);
            _roomMaterial.SetTexture("_ClearingMask", _clearingMask);
        }

        private void PaintWholeGlade(LayoutMap map)
        {
            var entry = TrailPoint(map.EntryPoint); var exit = TrailPoint(map.ExitPoint(0));
            for (int y = 0; y < TrailResolution; y++)
                for (int x = 0; x < TrailResolution; x++)
                {
                    var p = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                        _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                    var point = new FixVec2(Fix64.FromDouble(p.x), Fix64.FromDouble(p.y));
                    if (!map.IsWalkable(point, Fix64.Ratio(15, 100))) continue;
                    float clearing = 0;
                    for (int g = 0; g < map.GladeCount; g++)
                    {
                        var glade = map.GetGlade(g);
                        clearing = Mathf.Max(clearing, Mathf.Clamp01((1 - glade.Field(point).ToFloat()) / .4f));
                    }
                    for (int b = 0; b < map.RewardBranchCount; b++)
                    {
                        var glade = GladeRegion.ForBranch(map, map.GetRewardBranch(b));
                        clearing = Mathf.Max(clearing, Mathf.Clamp01((1 - glade.Field(point).ToFloat()) / .4f));
                    }
                    if (clearing <= 0) continue;
                    float approach = Mathf.Clamp01((Mathf.Min(Vector2.Distance(p, entry), Vector2.Distance(p, exit)) - 3) / 8);
                    // Leave a grassy rim, with open mixed ground covering the connected interior.
                    bool interior = true;
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float angle = direction * Mathf.PI / 4;
                        var offset = new FixVec2(Fix64.FromDouble(Mathf.Cos(angle) * 2), Fix64.FromDouble(Mathf.Sin(angle) * 2));
                        if (!map.Outline.Contains(point + offset)) { interior = false; break; }
                    }
                    float rim = interior ? 1 : .2f;
                    _clearingPixels[y * TrailResolution + x] = (byte)Mathf.RoundToInt(255 * approach * rim * clearing);
                }
        }

        private void BuildGroundWear(LayoutMap map)
        {
            if (_wearMask == null)
                _wearMask = new Texture2D(TrailResolution, TrailResolution, TextureFormat.R8, false, true)
                { name = "Вытоптанный грунт", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            System.Array.Clear(_wearPixels, 0, _wearPixels.Length);
            StampGroundWear(map, TrailPoint(map.EntryPoint), 3.8f);
            for (int e = 0; e < map.ExitCount; e++) StampGroundWear(map, TrailPoint(map.ExitPoint(e)), 3.8f);
            for (int b = 0; b < map.RewardBranchCount; b++)
                StampGroundWear(map, TrailPoint(map.CenterOf(map.GetRewardBranch(b))), 2.5f);
            for (int o = 0; o < map.ObstacleCount; o++)
            {
                var obstacle = map.GetObstacle(o);
                if (obstacle.VisualKind == 0)
                    StampGroundWear(map, TrailPoint(obstacle.Center), obstacle.Radius.ToFloat() + 1.8f);
            }
            // Каменистые поляны отличаются плоским грунтом у края, центр остаётся травяным.
            for (int g = 0; g < map.GladeCount; g++)
            {
                if (CharacterOf(map, g) != GladeCharacter.Rocky) continue;
                var glade = map.GetGlade(g); var rng = DecorRandom(g, 613);
                for (int patch = 0; patch < 5; patch++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                    var center = TrailPoint(glade.Center) + new Vector2(Mathf.Cos(angle) * glade.Radii.X.ToFloat(),
                        Mathf.Sin(angle) * glade.Radii.Y.ToFloat()) * .68f;
                    StampGroundWear(map, center, 2.4f + (float)rng.NextDouble() * 1.4f);
                }
            }
            _wearMask.SetPixelData(_wearPixels, 0);
            _wearMask.Apply(false, false);
            _roomMaterial.SetTexture("_WearMask", _wearMask);
        }

        private void StampGroundWear(LayoutMap map, Vector2 center, float radius)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - radius - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + radius - _trailBounds.x) / _trailBounds.z * TrailResolution), 0, TrailResolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((center.y - radius - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((center.y + radius - _trailBounds.y) / _trailBounds.w * TrailResolution), 0, TrailResolution - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(_trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z,
                        _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w);
                    float distance = Vector2.Distance(p, center) / radius;
                    float strength = Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - distance));
                    if (strength <= 0 || !map.ContainsWorld(new FixVec2(Fix64.FromDouble(p.x), Fix64.FromDouble(p.y)))) continue;
                    int index = y * TrailResolution + x;
                    _wearPixels[index] = (byte)Mathf.Max(_wearPixels[index], Mathf.RoundToInt(strength * 255));
                }
        }
    }
}
