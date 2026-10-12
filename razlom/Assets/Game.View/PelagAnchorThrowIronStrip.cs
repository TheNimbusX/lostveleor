using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Струна Броска якоря (база «холодное железо», шейдер «Razlom/Anchor Throw Streak»): лента от хвата к кольцу головы,
    /// повёрнутая к камере. u — метры от кольца к руке / MetersPerU (рисунок едет с головой), v — ±1 поперёк; z — секунды
    /// распада, w — секунды с выпуска. Вершинный цвет: r — жар (ширина сердцевины), g — шевроны, b — импульс, a — видимость.
    /// Ореол — та же лента шире (HaloScale) на материале ореола. Меш свой у объекта пула, вершины — в мире.
    /// </summary>
    public sealed class AnchorThrowIronStreak
    {
        public const string MeshName = "AnchorThrowIronStreak";
        private const int MaxPoints = 128;
        private readonly List<Vector3> _v = new List<Vector3>(MaxPoints * 2);
        private readonly List<Vector4> _uv = new List<Vector4>(MaxPoints * 2);
        private readonly List<Color32> _c = new List<Color32>(MaxPoints * 2);
        private readonly List<int> _t = new List<int>(MaxPoints * 6);

        /// <summary>Кадр струны.</summary>
        public struct Frame
        {
            public Vector3 Grip, Ring, ViewForward;
            /// <summary>0…1 «полёт → натянута»; секунды после рывка (&lt;0 — до); шевроны 0…1; видимость; распад, с; часы, с.</summary>
            public float Tight, SinceYank, Chevrons, Opacity, BreakSeconds, Clock;
            /// <summary>Множитель ширины (ореол — HaloScale).</summary>
            public float Scale;
        }

        public void Build(Mesh mesh, in Frame f)
        {
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            Vector3 axis = f.Grip - f.Ring;
            float length = axis.magnitude;
            if (length < .08f || f.Opacity <= .001f) { mesh.Clear(); return; }
            Vector3 t = axis / length;
            Vector3 side = Vector3.Cross(t, -f.ViewForward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(t, Vector3.up);
            side.Normalize();
            int n = Mathf.Clamp(Mathf.CeilToInt(length / PelagAnchorThrowIronLook.SegmentMeters) + 1, 2, MaxPoints);
            float scale = f.Scale > 0f ? f.Scale : 1f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                float behind = length * k, fromGrip = length - behind;
                Vector3 p = f.Ring + t * behind;
                float pulse = PelagAnchorThrowIronLook.Pulse(behind, f.SinceYank);
                float half = PelagAnchorThrowIronLook.HalfAt(behind, fromGrip, f.Tight, pulse) * scale;
                half = Mathf.Max(half, .003f);
                float heat = PelagAnchorThrowIronLook.HeatAt(behind, f.Tight, pulse);
                var color = new Color32((byte)(heat * 255f), (byte)(Mathf.Clamp01(f.Chevrons) * 255f),
                    (byte)(Mathf.Clamp01(pulse) * 255f), (byte)(Mathf.Clamp01(f.Opacity) * 255f));
                float u = behind / PelagAnchorThrowIronLook.MetersPerU;
                for (int s = -1; s <= 1; s += 2)
                {
                    _v.Add(p + side * (half * s));
                    _uv.Add(new Vector4(u, s, f.BreakSeconds, f.Clock));
                    _c.Add(color);
                }
                if (i == 0) continue;
                int b = (i - 1) * 2;
                _t.Add(b); _t.Add(b + 2); _t.Add(b + 1);
                _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _uv);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0, true);
        }
    }

    /// <summary>
    /// Борозда волока базы (шейдер «Razlom/Anchor Throw Furrow»): настоящий путь ног тянутого тела, точка каждые FurrowStep м с
    /// мигом прохода (свежая у тела темнее, старая рассыпается с краёв и гаснет). u — метры вдоль / 2, v — ±1 поперёк,
    /// z — возраст точки, w — часы; a — видимость. Начало скруглено, перед телом — нос (PelagAnchorThrowIronLook.FurrowWidthAt).
    /// </summary>
    public sealed class AnchorThrowIronFurrow
    {
        public const string MeshName = "AnchorThrowIronFurrow";
        private const int MaxPoints = 128;
        private readonly Vector3[] _points = new Vector3[MaxPoints];
        private readonly float[] _times = new float[MaxPoints];
        private int _count;
        private readonly List<Vector3> _v = new List<Vector3>(MaxPoints * 2 + 2);
        private readonly List<Vector4> _uv = new List<Vector4>(MaxPoints * 2 + 2);
        private readonly List<Color32> _c = new List<Color32>(MaxPoints * 2 + 2);
        private readonly List<int> _t = new List<int>(MaxPoints * 6 + 6);

        public int Count => _count;
        public Vector3 Last => _count > 0 ? _points[_count - 1] : Vector3.zero;

        public void Begin() { _count = 0; }

        /// <summary>Точка пути (на земле); true — записана (тело ушло на шаг дальше).</summary>
        public bool Add(Vector3 ground, float time)
        {
            float step = PelagAnchorThrowIronLook.FurrowStep;
            if (_count > 0 && (ground - _points[_count - 1]).sqrMagnitude < step * step) return false;
            if (_count == MaxPoints)
            {
                System.Array.Copy(_points, 1, _points, 0, MaxPoints - 1);
                System.Array.Copy(_times, 1, _times, 0, MaxPoints - 1);
                _count--;
            }
            _points[_count] = ground;
            _times[_count] = time;
            _count++;
            return true;
        }

        /// <summary>Лента от первой точки до тела <paramref name="head"/> (нос), полуширина <paramref name="half"/>, м.</summary>
        public void Build(Mesh mesh, Vector3 head, float half, float now, float clock, float opacity)
        {
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            if (_count < 1 || opacity <= .001f) { mesh.Clear(); return; }
            bool withHead = (head - _points[_count - 1]).sqrMagnitude > 1e-4f;
            int n = _count + (withHead ? 1 : 0);
            if (n < 2) { mesh.Clear(); return; }
            float total = 0f;
            for (int i = 1; i < n; i++) total += Vector3.Distance(At(i - 1, head), At(i, head));
            float along = 0f;
            var color = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(opacity) * 255f));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = At(i, head);
                Vector3 prev = At(Mathf.Max(0, i - 1), head), next = At(Mathf.Min(n - 1, i + 1), head);
                if (i > 0) along += Vector3.Distance(p, prev);
                Vector3 dir = next - prev;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                Vector3 side = new Vector3(dir.z, 0f, -dir.x).normalized;
                float age = i < _count ? now - _times[i] : 0f;
                float w = half * PelagAnchorThrowIronLook.FurrowWidthAt(along, total - along);
                for (int s = -1; s <= 1; s += 2)
                {
                    _v.Add(p + side * (w * s) + Vector3.up * .03f);
                    _uv.Add(new Vector4(along * .5f, s, age, clock));
                    _c.Add(color);
                }
                if (i == 0) continue;
                int b = (i - 1) * 2;
                _t.Add(b); _t.Add(b + 2); _t.Add(b + 1);
                _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _uv);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0, true);
        }

        private Vector3 At(int i, Vector3 head) => i < _count ? _points[i] : head;

        /// <summary>Самая молодая точка старше <paramref name="seconds"/> — борозда целиком рассыпалась.</summary>
        public bool Gone(float now, float seconds) => _count == 0 || now - _times[_count - 1] > seconds;
    }
}
