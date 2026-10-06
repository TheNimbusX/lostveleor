using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Земля под полосой броска: сетка в осях полосы (вдоль × поперёк), снимается раз при
    /// раскрытии сети — дальше вершины берут высоту билинейно (уступы арены, кочки лагеря).
    /// Вдоль — шаг ≈ 0,23 м: ступень уступа не размазывается на метр, как у сетки 9×9 форм Вихря.
    /// </summary>
    public sealed class AnchorThrowLaneGround
    {
        private const int Along = 33, Across = 5;
        private readonly float[] _heights = new float[Along * Across];
        private Vector3 _origin, _dir, _right;
        private float _length = 1f, _half = 1f;

        public void Sample(Vector3 origin, Vector3 dir, float length, float half, System.Func<float, float, float> ground)
        {
            _origin = origin;
            _dir = dir;
            _right = new Vector3(dir.z, 0f, -dir.x);
            _length = Mathf.Max(.5f, length);
            _half = Mathf.Max(.25f, half);
            for (int i = 0; i < Along; i++)
                for (int j = 0; j < Across; j++)
                {
                    Vector3 p = origin + dir * (_length * i / (Along - 1)) + _right * (_half * (-1f + 2f * j / (Across - 1)));
                    _heights[i * Across + j] = ground(p.x, p.z);
                }
        }

        /// <summary>Высота земли в точке полосы: <paramref name="along"/> м от начала, <paramref name="across"/> м вправо.</summary>
        public float At(float along, float across)
        {
            float fi = Mathf.Clamp(along / _length * (Along - 1), 0f, Along - 1.001f);
            float fj = Mathf.Clamp((across / _half + 1f) * .5f * (Across - 1), 0f, Across - 1.001f);
            int i = (int)fi, j = (int)fj;
            float u = fi - i, v = fj - j;
            float a = Mathf.Lerp(_heights[i * Across + j], _heights[i * Across + j + 1], v);
            float b = Mathf.Lerp(_heights[(i + 1) * Across + j], _heights[(i + 1) * Across + j + 1], v);
            return Mathf.Lerp(a, b, u);
        }
    }

    /// <summary>
    /// Невод (кадр B-net, кобальт): лист воды сети на земле от руки до головы. Договор вершин —
    /// шейдер Razlom/Whirlwind Form Water (PelagWhirlwindFormWater.cs). Поперёк — полуширина
    /// воды <see cref="PelagAnchorThrowVfxRules.NetWaterHalfWidth"/>: с гребнем видимый край = край
    /// урона Sim (1,5 м + тело). В возврате лист стягивается к герою: длина сжимается за головой,
    /// у дальнего края растёт валик пены (лишняя пена гребня), ширина чуть сходится, как кошель.
    /// Меш в осях корня (корень — рука на земле, без поворота и масштаба).
    /// </summary>
    public sealed class PelagAnchorThrowNetWater : FormWaterMesh
    {
        public const string MeshName = "Бросок якоря: сеть";
        private const int Rows = 28, Columns = 7;
        private const float Margin = .16f, Lift = .04f;
        private static int[] _triangles;
        private float _seed, _p1, _p2;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f);
            _p2 = Random.Range(0f, 6.283f);
            if (_triangles == null) _triangles = StripTriangles(1, Rows + 1, Columns, false);
            Allocate((Rows + 1) * Columns, _triangles);
        }

        /// <param name="root">Корень объекта (рука на земле).</param>
        /// <param name="near">Ближний край от корня, м (0).</param>
        /// <param name="far">Дальний край от корня, м (за головой).</param>
        /// <param name="half">Полуширина воды, м (без гребня).</param>
        /// <param name="roll">Валик пены у дальнего края, 0…1 (сеть собрана).</param>
        public void Build(Mesh mesh, Vector3 root, Vector3 dir, float near, float far, float half, float roll,
            float age, float flow, float time, float alpha, AnchorThrowLaneGround ground, float groundOffset)
        {
            far = Mathf.Max(near + .05f, far);
            var right = new Vector3(dir.z, 0f, -dir.x);
            float length = far - near;
            for (int r = 0; r <= Rows; r++)
            {
                float k = r / (float)Rows;
                float s = near + length * k;
                // Края сети дышат двумя бегущими волнами — лист живой, не ковёр.
                float hw = Mathf.Max(.05f, half * (1f + .05f * Mathf.Sin(s * 2.1f + _p1 - time * 6f) + .03f * Mathf.Sin(s * 4.7f + _p2 + time * 4f)));
                float ends = Mathf.Min(s - near, far - s);
                float churn = .35f + 1.4f * Mathf.Clamp01(roll) * PelagAnchorThrowVfxRules.Smooth01(1f - (far - s) / 1.2f);
                // У руки вода рвётся первой: оттуда сеть начинают выбирать.
                float fed = age > 0f ? age + (1f - k) * .08f : 0f;
                Color32 color = Vertex(OverAt(s, .6f, 1.4f), alpha);
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float across = y * (hw + Margin);
                    Vector3 p = root + dir * s + right * across;
                    float py = (ground != null ? ground.At(s + groundOffset, across) : root.y) + Lift;
                    int v = r * Columns + c;
                    Vertices[v] = new Vector3(p.x - root.x, py - root.y, p.z - root.z);
                    Uv0[v] = new Vector4(across / hw, hw, fed, ends);
                    Uv1[v] = new Vector4(s - flow, across, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    /// <summary>
    /// Невод: нити сети и узлы поверх листа (кадр B-net — светлая ромбическая сетка с белыми
    /// комьями пены на узлах). Нити — узкие полосы той же воды (белый материал нитей), две
    /// семьи диагоналей ромбом; узлы — короткие круглые полосы на пересечениях (комья пены
    /// с тёмным обводом). Сеть сжимается вместе с листом (решётка масштабируется по длине).
    /// </summary>
    public sealed class PelagAnchorThrowNetStrands : FormWaterMesh
    {
        public const string MeshName = "Бросок якоря: нити сети";
        /// <summary>Шаг ромба вдоль полосы и полуширина нити и узла, м.</summary>
        public const float Pitch = 1.1f, StrandHalf = .045f, KnotHalf = .10f;
        private const int Families = 2, PerFamily = 12, StrandRows = 7, Columns = 3, Knots = 40, KnotRows = 3;
        private const int StrandCount = Families * PerFamily;
        private const float Lift = .06f;
        private static int[] _triangles;
        private float _seed;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            if (_triangles == null)
            {
                int[] strands = StripTriangles(StrandCount, StrandRows, Columns, false);
                int[] knots = StripTriangles(Knots, KnotRows, Columns, false);
                int offset = StrandCount * StrandRows * Columns;
                _triangles = new int[strands.Length + knots.Length];
                strands.CopyTo(_triangles, 0);
                for (int i = 0; i < knots.Length; i++) _triangles[strands.Length + i] = knots[i] + offset;
            }
            Allocate(StrandCount * StrandRows * Columns + Knots * KnotRows * Columns, _triangles);
        }

        /// <param name="fullLength">Длина сети, когда она легла (решётка строится на ней и сжимается до far − near).</param>
        public void Build(Mesh mesh, Vector3 root, Vector3 dir, float near, float far, float fullLength, float half,
            float age, float alpha, AnchorThrowLaneGround ground, float groundOffset)
        {
            var right = new Vector3(dir.z, 0f, -dir.x);
            float length = Mathf.Max(.05f, far - near);
            float squeeze = length / Mathf.Max(.05f, fullLength);
            int v = 0;
            // Нить семьи f, номер n: вдоль a ∈ [−P + nP, P + nP], поперёк от −half до +half (у второй семьи — наоборот).
            for (int f = 0; f < Families; f++)
            {
                float sign = f == 0 ? 1f : -1f;
                for (int n = 0; n < PerFamily; n++)
                {
                    float start = (n - 1) * Pitch;
                    for (int r = 0; r < StrandRows; r++)
                    {
                        float k = r / (float)(StrandRows - 1);
                        float a = start + Pitch * 2f * k;
                        float y = sign * (-1f + 2f * k) * half;
                        bool inside = a >= 0f && a <= fullLength;
                        float s = near + Mathf.Clamp(a, 0f, fullLength) * squeeze;
                        float ends = Mathf.Min(k, 1f - k) * Pitch * 2f + .3f;
                        for (int c = 0; c < Columns; c++)
                        {
                            float x = (c - 1) * 1.6f;
                            v = Point(v, root, dir, right, s, y + x * StrandHalf, x, StrandHalf, ends, age,
                                inside ? alpha : 0f, a * 1.7f + f * 9f, .9f, ground, groundOffset);
                        }
                    }
                }
            }
            // Узлы — на пересечениях семей: a = k·P/2; чётные k — на оси, нечётные — на ±half/2.
            int knot = 0;
            for (int k = 1; knot < Knots; k++)
            {
                float a = k * Pitch * .5f;
                if (a > fullLength - .15f) break;
                int count = k % 2 == 0 ? 1 : 2;
                for (int q = 0; q < count && knot < Knots; q++, knot++)
                {
                    float y = count == 1 ? 0f : (q == 0 ? -.5f : .5f) * half;
                    float s = near + a * squeeze;
                    for (int r = 0; r < KnotRows; r++)
                        for (int c = 0; c < Columns; c++)
                        {
                            float x = (c - 1) * 1.6f;
                            float along = (r - 1) * KnotHalf;
                            float ends = (1f - Mathf.Abs(r - 1)) * KnotHalf;
                            v = Point(v, root, dir, right, s + along, y + x * KnotHalf, x, KnotHalf, ends, age,
                                alpha, a + along + 40f, 1.6f, ground, groundOffset);
                        }
                }
            }
            // Лишние узлы — невидимы (сеть короче полной решётки).
            for (; knot < Knots; knot++)
                for (int k = 0; k < KnotRows * Columns; k++)
                    v = Point(v, root, dir, right, near, 0f, 0f, KnotHalf, 0f, age, 0f, 0f, 0f, ground, groundOffset);
            Write(mesh);
        }

        private int Point(int v, Vector3 root, Vector3 dir, Vector3 right, float s, float across, float x, float hw, float ends,
            float age, float alpha, float noise, float churn, AnchorThrowLaneGround ground, float groundOffset)
        {
            Vector3 p = root + dir * s + right * across;
            float py = (ground != null ? ground.At(s + groundOffset, across) : root.y) + Lift;
            Vertices[v] = new Vector3(p.x - root.x, py - root.y, p.z - root.z);
            Uv0[v] = new Vector4(x, hw, age, ends);
            Uv1[v] = new Vector4(noise, across, _seed, churn);
            Colors[v] = Vertex(OverAt(s, .6f, 1.4f), alpha);
            return v + 1;
        }
    }
}
