using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Звенья призрачной цепи Веера (кадр form-fan-ghost-v1): узкая лента от хвата к кольцу призрака, повёрнутая к камере, на
    /// шейдере «Razlom/Anchor Throw Form Chain» в режиме только звеньев (_GlowAlpha 0) — свет вокруг даёт струна базы
    /// (AnchorThrowIronStreak) в краске Веера. u — метры от кольца к руке (звенья едут с головой), v — ±1; z — секунды распада,
    /// w — часы; TEXCOORD1: x — метры поперёк, y — видимость звеньев. Меш свой у объекта пула, вершины — в мире.
    /// </summary>
    public sealed class AnchorThrowLinkStrip
    {
        public const string MeshName = "AnchorThrowLinkStrip";
        private const float Step = .25f;
        private const int MaxPoints = 64;
        private readonly List<Vector3> _v = new List<Vector3>(MaxPoints * 2);
        private readonly List<Vector4> _uv0 = new List<Vector4>(MaxPoints * 2), _uv1 = new List<Vector4>(MaxPoints * 2);
        private readonly List<Color32> _c = new List<Color32>(MaxPoints * 2);
        private readonly List<int> _t = new List<int>(MaxPoints * 6);

        /// <summary>Хват, кольцо, взгляд камеры; видимость звеньев 0…1, непрозрачность, секунды распада, часы.</summary>
        public void Build(Mesh mesh, Vector3 grip, Vector3 ring, Vector3 viewForward, float links, float opacity, float breakSeconds,
            float clock)
        {
            _v.Clear(); _uv0.Clear(); _uv1.Clear(); _c.Clear(); _t.Clear();
            Vector3 axis = grip - ring;
            float length = axis.magnitude;
            if (length < .08f || opacity <= .001f || links <= .001f) { mesh.Clear(); return; }
            Vector3 t = axis / length;
            Vector3 side = Vector3.Cross(t, -viewForward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(t, Vector3.up);
            side.Normalize();
            float half = PelagThrowFormLook.LinkStripHalf;
            int n = Mathf.Clamp(Mathf.CeilToInt(length / Step) + 1, 2, MaxPoints);
            var color = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(opacity) * 255f));
            for (int i = 0; i < n; i++)
            {
                float behind = length * i / (n - 1);
                Vector3 p = ring + t * behind;
                // У хвата звенья сходят на нет (кулак), у кольца — первое звено целое.
                float vis = links * Mathf.SmoothStep(0f, 1f, (length - behind) / .3f);
                for (int s = -1; s <= 1; s += 2)
                {
                    _v.Add(p + side * (half * s));
                    _uv0.Add(new Vector4(behind, s, breakSeconds, clock));
                    _uv1.Add(new Vector4(half * s, vis, 0f, 0f));
                    _c.Add(color);
                }
                if (i == 0) continue;
                int b = (i - 1) * 2;
                _t.Add(b); _t.Add(b + 2); _t.Add(b + 1);
                _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0, true);
        }
    }
}
