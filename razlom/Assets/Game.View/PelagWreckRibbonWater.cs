using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Живая вода Крушения v2 в воздухе (тот же шейдер и договор вершин, что PelagWreckWater.cs):
    ///  • <see cref="PelagWreckArcWater"/> — пенная дуга за головой якоря по её НАСТОЯЩЕМУ пути
    ///    (точки пишет вид кадр за кадром с головы, которую ведёт риг якоря: «след за когтем, не
    ///    после»), лента повёрнута к камере, к голове толще, хвост рвётся на капли; у Девятого
    ///    вала — индиговая лента за головой, крутящейся над героем;
    ///  • <see cref="PelagWreckShellWater"/> — Водяной панцирь: жемчужные ленты воды кружат вокруг
    ///    героя по силуэту (тела пишут трафарет — по телу вода не рисуется, герой виден насквозь и
    ///    не ярче), рябь от ударов, лопается наружу.
    /// </summary>
    public sealed class PelagWreckArcWater : FormWaterMesh
    {
        public const string MeshName = "Крушение: дуга";
        public const int MaxPoints = 64;
        private const int Rows = 28, Columns = 5;
        private const float Margin = .05f;
        private static int[] _triangles;
        private readonly float[] _lengths = new float[MaxPoints];
        private float _seed, _p1;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f);
            if (_triangles == null) _triangles = StripTriangles(1, Rows, Columns, false);
            Allocate(Rows * Columns, _triangles);
        }

        /// <summary>
        /// Лента по точкам пути головы <paramref name="points"/>[0 … count − 1] (от хвоста к голове).
        /// Полуширина — от <paramref name="tailHalf"/> у хвоста до <paramref name="headHalf"/> у головы;
        /// возраст распада — от <paramref name="tailAge"/> до <paramref name="headAge"/>.
        /// </summary>
        public void Build(Mesh mesh, Vector3 root, Vector3[] points, int count, Vector3 camera, float tailHalf, float headHalf,
            float tailAge, float headAge, float churn, float flow, float time, float alpha)
        {
            count = Mathf.Clamp(count, 0, MaxPoints);
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) total += (points[i] - points[i - 1]).magnitude;
                _lengths[i] = total;
            }
            Color32 color = Vertex(0f, count >= 2 && total > .02f ? alpha : 0f);
            int segment = 0;
            for (int r = 0; r < Rows; r++)
            {
                float k = r / (float)(Rows - 1);
                float s = total * k;
                Vector3 centre, tangent;
                if (count < 2)
                {
                    centre = count == 1 ? points[0] : root;
                    tangent = Vector3.forward;
                }
                else
                {
                    while (segment < count - 2 && _lengths[segment + 1] < s) segment++;
                    float span = Mathf.Max(1e-5f, _lengths[segment + 1] - _lengths[segment]);
                    float u = Mathf.Clamp01((s - _lengths[segment]) / span);
                    centre = Vector3.Lerp(points[segment], points[segment + 1], u);
                    tangent = points[segment + 1] - points[segment];
                    tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
                }
                Vector3 across = Vector3.Cross(tangent, camera - centre);
                if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(tangent, Vector3.up);
                across = across.sqrMagnitude > 1e-8f ? across.normalized : Vector3.right;
                // Толще к голове, дышит вдоль — вода, а не жёсткая полоса.
                float hw = Mathf.Max(.006f, Mathf.Lerp(tailHalf, headHalf, k * k) * (1f + .14f * Mathf.Sin(s * 9f + _p1 - time * 11f)));
                float ends = Mathf.Min(s, total - s) + .02f;
                float age = Mathf.Lerp(tailAge, headAge, k);
                float outer = hw + Margin;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float off = y * outer;
                    int v = r * Columns + c;
                    Vertices[v] = centre + across * off - root;
                    Uv0[v] = new Vector4(off / hw, hw, Mathf.Max(0f, age), ends);
                    Uv1[v] = new Vector4(s + flow, off, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    public sealed class PelagWreckShellWater : FormWaterMesh
    {
        public const string MeshName = "Крушение: панцирь";
        private const int Bands = PelagWreckVfxRules.ShellBands, Segments = 40, Columns = 5;
        /// <summary>Лента обходит героя не целиком: открытая дуга (кружит, а не кольцо).</summary>
        private const float ArcSpan = 1.45f * Mathf.PI;
        private const float Margin = .06f;
        private const int MaxRipples = 4;
        private static int[] _triangles;
        private float _seed;
        private readonly float[] _phase = new float[Bands], _tilt = new float[Bands];
        private readonly float[] _rippleAngle = new float[MaxRipples], _rippleAt = new float[MaxRipples];
        private int _rippleNext;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            for (int k = 0; k < Bands; k++)
            {
                _phase[k] = Random.Range(0f, 6.283f);
                _tilt[k] = Random.Range(.10f, .20f);
            }
            for (int i = 0; i < MaxRipples; i++) _rippleAt[i] = -100f;
            if (_triangles == null) _triangles = StripTriangles(Bands, Segments, Columns, false);
            Allocate(Bands * Segments * Columns, _triangles);
        }

        /// <summary>Удар по оболочке: рябь со стороны <paramref name="angle"/> (рад, мир x/z), в момент <paramref name="clock"/> с.</summary>
        public void Ripple(float angle, float clock)
        {
            _rippleAngle[_rippleNext] = angle;
            _rippleAt[_rippleNext] = clock;
            _rippleNext = (_rippleNext + 1) % MaxRipples;
        }

        /// <param name="centre">Ноги героя на земле (= корень).</param>
        /// <param name="radius">Радиус оболочки у пояса (лопается — растёт).</param>
        /// <param name="grow">0…1 — проявление шириной лент (рождение, таяние).</param>
        public void Build(Mesh mesh, Vector3 centre, float radius, float grow, float clock, float age, float churn, float alpha)
        {
            grow = Mathf.Clamp01(grow);
            for (int k = 0; k < Bands; k++)
            {
                float height = .35f + .6f * k;
                float bandHalf = (.12f + .06f * (k == 1 ? 1f : 0f)) * Mathf.Lerp(.25f, 1f, grow);
                float spin = PelagWreckVfxRules.ShellSpin * (k % 2 == 0 ? 1f : -1.25f);
                float start = _phase[k] + spin * clock;
                for (int r = 0; r < Segments; r++)
                {
                    float u = r / (float)(Segments - 1);
                    float angle = start + ArcSpan * u;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    float h = height + _tilt[k] * Mathf.Sin(angle * 2f + _phase[(k + 1) % Bands] + clock * 3f);
                    float dome = 1f - .22f * Mathf.Pow((h - 1f) / 1.1f, 2f);
                    float bulge = 0f;
                    for (int i = 0; i < MaxRipples; i++)
                    {
                        float d = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, _rippleAngle[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        bulge += PelagWreckVfxRules.ShellRipple(clock - _rippleAt[i]) * Mathf.Exp(-d * d / (2f * .45f * .45f));
                    }
                    float rr = radius * dome + bulge;
                    float ends = Mathf.Min(u, 1f - u) * ArcSpan * rr + .01f;
                    float outer = bandHalf + Margin;
                    for (int c = 0; c < Columns; c++)
                    {
                        float y = -1f + 2f * c / (Columns - 1);
                        float off = y * outer;
                        int v = (k * Segments + r) * Columns + c;
                        Vertices[v] = new Vector3(cos * rr, h + off, sin * rr);
                        Uv0[v] = new Vector4(off / Mathf.Max(.01f, bandHalf), Mathf.Max(.01f, bandHalf), age, ends);
                        Uv1[v] = new Vector4(angle * rr, h + off, _seed + k, churn);
                        Colors[v] = Vertex(0f, alpha);
                    }
                }
            }
            Write(mesh);
        }
    }
}
