using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Живая вода Крушения v2 на земле и по полосе — меши для шейдера Razlom/Whirlwind Form
    /// Water (тот же договор вершин, что у форм Вихря, Шквала v2 и Абордажа v2:
    /// PelagWhirlwindFormWater.cs): uv0 = (поперёк / полуширина, полуширина м, возраст
    /// распада с, до открытого конца м), uv1 = (шум, шум, сдвиг шума, лишняя пена гребня),
    /// COLOR = (поверх низких препятствий, —, —, непрозрачность). Меш пишет контроллер
    /// каждый кадр по непрерывному тику показа; корень объекта без поворота и масштаба,
    /// вершины — мир минус корень; высота земли — <see cref="FormGroundGrid"/> (вода лежит
    /// на настоящей земле, уступы арены).
    ///
    ///  • <see cref="PelagWreckCrestWater"/> — СТОЯЧИЙ гребень поперёк полосы (объём, не
    ///    наклейка): профиль от земли за спиной вверх через вершину к загнутой губе; губа —
    ///    на фронте Sim (край урона), ширина — полоса Sim, концы рваные и ниже. Вал базы
    ///    0,7 м, горб Девятого вала до 1,4 м, стена Волнореза 1,1 м с сильным загибом;
    ///  • <see cref="PelagWreckTrailWater"/> — мокрый след за гребнем по полосе (кромки = края
    ///    полосы урона), рвётся на капли через ~0,4 с после того, как прошла губа.
    /// </summary>
    public sealed class PelagWreckCrestWater : FormWaterMesh
    {
        public const string MeshName = "Крушение: гребень";
        private const int Rows = 28, Columns = 9;
        /// <summary>Профиль за краем воды, м: туда выпирают комья пены губы.</summary>
        private const float Margin = .14f;
        /// <summary>Концы гребня за краем полосы, м: рваный край (uv0.w &lt; 0 — нет воды).</summary>
        private const float EndMargin = .2f;
        private const float Lift = .02f;
        private static int[] _triangles;
        private float _seed, _p1, _p2, _p3;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f);
            _p2 = Random.Range(0f, 6.283f);
            _p3 = Random.Range(0f, 6.283f);
            if (_triangles == null) _triangles = StripTriangles(1, Rows, Columns, false);
            Allocate(Rows * Columns, _triangles);
        }

        /// <summary>Профиль: кубическая кривая (вдоль от губы, высота). t ∈ [0, 1], вне — продолжение касательной.</summary>
        private static Vector2 Profile(float t, float back, float height, float curl)
        {
            var p0 = new Vector2(-back, 0f);
            var p1 = new Vector2(-back * .55f, .35f * height);
            var p2 = new Vector2(-.30f * height * (1f + curl), (1.30f + .10f * curl) * height);
            var p3 = new Vector2(0f, (.70f + .12f * curl) * height);
            if (t < 0f) return p0 + (p1 - p0).normalized * (t * 3f * (p1 - p0).magnitude);
            if (t > 1f) return p3 + (p3 - p2).normalized * ((t - 1f) * 3f * (p3 - p2).magnitude);
            float s = 1f - t;
            return s * s * s * p0 + 3f * s * s * t * p1 + 3f * s * t * t * p2 + t * t * t * p3;
        }

        private static float ProfileLength(float back, float height, float curl)
        {
            float length = 0f;
            Vector2 previous = Profile(0f, back, height, curl);
            for (int i = 1; i <= 10; i++)
            {
                Vector2 p = Profile(i / 10f, back, height, curl);
                length += (p - previous).magnitude;
                previous = p;
            }
            return Mathf.Max(.05f, length);
        }

        /// <param name="origin">Начало полосы Sim на земле (LaneOrigin); корень объекта — <paramref name="root"/>.</param>
        /// <param name="front">Губа вдоль полосы от origin, м (PelagWreckVfxRules.WaveFront).</param>
        /// <param name="halfWidth">Полуширина полосы Sim, м.</param>
        /// <param name="age">Возраст распада (0 — живая вода).</param>
        /// <param name="flow">Пена бежит к губе, м.</param>
        public void Build(Mesh mesh, Vector3 root, Vector3 origin, Vector3 dir, float front, float halfWidth, float height,
            float back, float curl, float age, float churn, float flow, float time, float alpha, FormGroundGrid ground)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var perp = new Vector3(-dir.z, 0f, dir.x);
            float outerWidth = halfWidth + EndMargin;
            for (int r = 0; r < Rows; r++)
            {
                float w = -outerWidth + 2f * outerWidth * r / (Rows - 1);
                float ends = halfWidth - Mathf.Abs(w);
                // Концы ниже и короче (вал выкатывается из середины), высота дышит бегущими волнами.
                float taper = .45f + .55f * PelagWreckVfxRules.Smooth01((halfWidth + .1f - Mathf.Abs(w)) / Mathf.Max(.3f, halfWidth * .55f));
                float breathe = 1f + .08f * Mathf.Sin(w * 3.1f + _p1 + time * 7f) + .05f * Mathf.Sin(w * 6.7f + _p2 - time * 5f);
                float h = Mathf.Max(.02f, height * taper * breathe);
                float b = back * (.7f + .3f * taper);
                // Губа отстаёт от фронта в волнах, но вперёд края урона не выходит.
                float lag = .05f + .05f * (.5f + .5f * Mathf.Sin(w * 2.3f + _p3 + time * 4f)) + .12f * (1f - taper);
                float length = ProfileLength(b, h, curl);
                float hw = .5f * length;
                float outer = hw + Margin;
                float fed = age > 0f ? age + .012f * Mathf.Sin(w * 5f + _p2) + .04f * (1f - taper) : 0f;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float s = y * outer;
                    float t = (s + hw) / length;
                    Vector2 p = Profile(t, b, h, curl);
                    float along = front - lag + p.x;
                    Vector3 flat = origin + dir * along + perp * w;
                    float ground0 = ground != null ? ground.At(flat.x, flat.z) : origin.y;
                    int v = r * Columns + c;
                    Vertices[v] = new Vector3(flat.x, ground0 + Lift + Mathf.Max(0f, p.y), flat.z) - root;
                    Uv0[v] = new Vector4(s / hw, hw, Mathf.Max(0f, fed), ends);
                    Uv1[v] = new Vector4(s - flow, w * .8f, _seed, churn * (.4f + .6f * Mathf.Clamp01(t)));
                    Colors[v] = Vertex(t < .18f ? .5f : 0f, alpha);
                }
            }
            Write(mesh);
        }
    }

    /// <summary>Мокрый след за гребнем: полоса на земле от точки удара до заднего склона гребня, ширина полосы Sim.</summary>
    public sealed class PelagWreckTrailWater : FormWaterMesh
    {
        public const string MeshName = "Крушение: след вала";
        private const int Rows = 32, Columns = 5;
        private const float Margin = .14f, Lift = .03f;
        private static int[] _triangles;
        private float _seed, _p1, _p2;

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f);
            _p2 = Random.Range(0f, 6.283f);
            if (_triangles == null) _triangles = StripTriangles(1, Rows, Columns, false);
            Allocate(Rows * Columns, _triangles);
        }

        /// <summary>
        /// След вдоль полосы от <paramref name="from"/> до <paramref name="to"/> (м от origin). Возраст в
        /// каждом ряду — <paramref name="ageAt"/>(вдоль): сколько прошло с тех пор, как там была губа.
        /// </summary>
        public void Build(Mesh mesh, Vector3 root, Vector3 origin, Vector3 dir, float from, float to, float halfWidth,
            System.Func<float, float> ageAt, float churn, float flow, float time, float alpha, FormGroundGrid ground)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var perp = new Vector3(-dir.z, 0f, dir.x);
            if (to < from + .02f) to = from + .02f;
            Color32 color = Vertex(1f, alpha);
            for (int r = 0; r < Rows; r++)
            {
                float k = r / (float)(Rows - 1);
                float s = Mathf.Lerp(from, to, k);
                float hw = Mathf.Max(.05f, halfWidth * (1f + .06f * Mathf.Sin(s * 2.7f + _p1 + time * 3f)));
                float ends = Mathf.Min(s - from + .05f, to + .35f - s);
                float age = ageAt != null ? ageAt(s) : 0f;
                float outer = hw + Margin;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float across = y * outer;
                    Vector3 flat = origin + dir * s + perp * across;
                    float g = ground != null ? ground.At(flat.x, flat.z) : origin.y;
                    int v = r * Columns + c;
                    Vertices[v] = new Vector3(flat.x, g + Lift, flat.z) - root;
                    Uv0[v] = new Vector4(across / hw, hw, age + .01f * Mathf.Sin(across * 6f + _p2), ends);
                    Uv1[v] = new Vector4(s - flow, across, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }
}
