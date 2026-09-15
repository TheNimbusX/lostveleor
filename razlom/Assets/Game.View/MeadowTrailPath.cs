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
    }
}
