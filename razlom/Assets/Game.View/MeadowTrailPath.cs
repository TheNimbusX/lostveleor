using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Presentation-only string pulling: keep a corridor wide enough for the visible trail.
    public static class MeadowTrailPath
    {
        public static bool IsClear(LayoutMap map, Vector2 a, Vector2 b, float radius)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / .2f));
            var clearance = Fix64.FromDouble(radius + .1f);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                if (!map.IsWalkable(new FixVec2(Fix64.FromDouble(p.x), Fix64.FromDouble(p.y)), clearance)) return false;
            }
            return true;
        }

        public static List<Vector2> Simplify(LayoutMap map, IReadOnlyList<Vector2> path, float radius)
        {
            var result = new List<Vector2>();
            if (path.Count == 0) return result;
            int current = 0;
            result.Add(path[0]);
            while (current < path.Count - 1)
            {
                int next = current + 1;
                for (int candidate = path.Count - 1; candidate > next; candidate--)
                    if (IsClear(map, path[current], path[candidate], radius)) { next = candidate; break; }
                result.Add(path[next]); current = next;
            }
            return result;
        }

        public static List<Vector2> Curve(LayoutMap map, IReadOnlyList<Vector2> path, float radius,
            float bend, System.Random rng)
        {
            var result = new List<Vector2>();
            if (path.Count == 0) return result;
            result.Add(path[0]);
            var sections = new List<Vector2> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(path[i - 1], path[i]) / 12));
                for (int s = 1; s <= count; s++) sections.Add(Vector2.Lerp(path[i - 1], path[i], s / (float)count));
            }
            for (int i = 1; i < sections.Count; i++)
            {
                var a = sections[i - 1]; var b = sections[i];
                var delta = b - a;
                var normal = new Vector2(-delta.y, delta.x).normalized;
                float amplitude = Mathf.Min(bend, delta.magnitude * .22f)
                    * Mathf.Lerp(.25f, 1, (float)rng.NextDouble()) * (rng.Next(2) == 0 ? -1 : 1);
                int steps = Mathf.Max(2, Mathf.CeilToInt(delta.magnitude / .4f));
                var curve = new List<Vector2>();
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    curve.Clear(); var previous = a; bool clear = true;
                    for (int s = 1; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        var p = Vector2.Lerp(a, b, t) + normal * (Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2) * amplitude);
                        if (!IsClear(map, previous, p, radius)) { clear = false; break; }
                        curve.Add(p); previous = p;
                    }
                    if (clear) break;
                    amplitude *= .5f;
                    curve.Clear();
                }
                if (curve.Count == steps) result.AddRange(curve);
                else result.Add(b);
            }
            return result;
        }
    }
}
