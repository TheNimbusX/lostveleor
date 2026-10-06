using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Высота земли под лежащей водой форм Вихря (владелец 02.10: «в лагере
    /// нижние слои проваливаются сквозь землю»). Сетка 9×9 снимается один раз
    /// при рождении эффекта (лагерь — навигация, разлом — пол арены, уступы),
    /// дальше каждая вершина берёт высоту билинейно: вода ложится на неровную
    /// землю, а не плоским диском на уровне ног героя.
    /// </summary>
    public sealed class FormGroundGrid
    {
        private const int Size = 9;
        private readonly float[] _heights = new float[Size * Size];
        private float _minX, _minZ, _step = 1f;

        /// <summary>Земля в центре эффекта.</summary>
        public float Centre { get; private set; }

        public void Sample(Vector3 centre, float radius, System.Func<float, float, float> ground)
        {
            radius = Mathf.Max(.5f, radius);
            _step = 2f * radius / (Size - 1);
            _minX = centre.x - radius;
            _minZ = centre.z - radius;
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                    _heights[j * Size + i] = ground(_minX + i * _step, _minZ + j * _step);
            Centre = ground(centre.x, centre.z);
        }

        public float At(float x, float z)
        {
            float fx = Mathf.Clamp((x - _minX) / _step, 0f, Size - 1.001f);
            float fz = Mathf.Clamp((z - _minZ) / _step, 0f, Size - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            float a = Mathf.Lerp(_heights[j * Size + i], _heights[j * Size + i + 1], u);
            float b = Mathf.Lerp(_heights[(j + 1) * Size + i], _heights[(j + 1) * Size + i + 1], u);
            return Mathf.Lerp(a, b, v);
        }
    }

    /// <summary>Общая запись живого меша воды форм (шейдер Razlom/Whirlwind Form Water).</summary>
    public abstract class FormWaterMesh
    {
        protected Vector3[] Vertices;
        protected Vector4[] Uv0, Uv1;
        protected Color32[] Colors;
        private int[] _triangles;

        /// <summary>Свой меш у объекта из пула: создаётся раз и дальше переписывается.</summary>
        public static Mesh MeshFor(MeshFilter filter, string name)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh != null && mesh.name == name) return mesh;
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return mesh;
        }

        protected void Allocate(int vertices, int[] triangles)
        {
            if (Vertices != null && Vertices.Length == vertices) return;
            Vertices = new Vector3[vertices];
            Uv0 = new Vector4[vertices];
            Uv1 = new Vector4[vertices];
            Colors = new Color32[vertices];
            _triangles = triangles;
        }

        protected void Write(Mesh mesh)
        {
            bool fresh = mesh.vertexCount != Vertices.Length;
            if (fresh) mesh.Clear();
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetColors(Colors);
            if (fresh) mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Полоса: rows × columns вершин, columns поперёк; closed — последний ряд сшит с первым.</summary>
        protected static int[] StripTriangles(int strips, int rows, int columns, bool closed)
        {
            int segments = closed ? rows : rows - 1;
            var triangles = new int[strips * segments * (columns - 1) * 6];
            int at = 0;
            for (int k = 0; k < strips; k++)
                for (int r = 0; r < segments; r++)
                {
                    int r1 = closed ? (r + 1) % rows : r + 1;
                    for (int c = 0; c < columns - 1; c++)
                    {
                        int a = (k * rows + r) * columns + c, b = (k * rows + r1) * columns + c;
                        triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 1;
                        triangles[at++] = a + 1; triangles[at++] = b; triangles[at++] = b + 1;
                    }
                }
            return triangles;
        }

        /// <summary>
        /// Цвет вершины v4: r — вода ложится поверх низких препятствий (колодец, корни, трава;
        /// шейдер сверяет высоту по глубине сцены), a — непрозрачность. У ног героя r = 0:
        /// сабля не пишет трафарет тел, и вода не должна лечь на клинок.
        /// </summary>
        protected static Color32 Vertex(float over, float alpha)
            => new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(over) * 255f), 255, 255,
                (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));

        /// <summary>Сила «поверх препятствий» по расстоянию от центра эффекта, м.</summary>
        protected static float OverAt(float radius, float from, float to)
            => PelagWhirlwindFormRules.Smooth01((radius - from) / Mathf.Max(.01f, to - from));
    }

    /// <summary>
    /// Кольцо Пенных волн как живая вода (02.10, вечер). Каждый кадр по
    /// непрерывному времени вида: гребень с разгоном и замедлением
    /// (PelagWhirlwindFormRules.FoamCrestRadius — без остановки после хода),
    /// волнистый край из трёх бегущих гармоник, полоса неровной ширины, пена
    /// гребня скатывается наружу (шум едет к фронту), к концу хода вода белеет
    /// пеной и рвётся на капли. Меш в осях корня: корень — центр колец на
    /// земле, без поворота и масштаба.
    /// v4 (по waves-2): полоса около метра (FoamBandHalfWidth), поперёк — от
    /// глубокой воды внутри к светлой снаружи, по обоим краям крупные комья
    /// пены; проявляется шириной, а не полупрозрачностью; кольцо рвётся
    /// разом по всему кругу (без полукруга, который уходил раньше и вместе со
    /// вторым кольцом читался рваной спиралью), первое — когда второе уже
    /// вышло и бежит отдельно (PelagWhirlwindFormRules.FoamRingBreakSeconds).
    /// </summary>
    public sealed class PelagFoamRingWater : FormWaterMesh
    {
        public const string MeshName = "Пенные волны: кольцо";
        private const int Segments = 128, Columns = 5;
        /// <summary>Поле меша за краем воды, м: туда выпирают комья пены (_Crest.y материала до 0,14).</summary>
        private const float Margin = .17f;
        private const float Lift = .035f;
        /// <summary>Возраст, на котором шейдер рвёт воду (_Break.x материала M_Whirlwind_FoamWaveRing).</summary>
        public const float ShaderBreakAge = .30f;
        /// <summary>Скорость, с которой пена гребня скатывается наружу по воде, м/с.</summary>
        private const float RollSpeed = .8f;
        /// <summary>Полоса выходит третью ширины и раздаётся за столько секунд — без мутной полупрозрачности.</summary>
        private const float RevealSeconds = .06f;

        private static int[] _triangles;
        private int _ring, _startTick, _travel;
        /// <summary>
        /// Радиус окружности в пространстве шума, м: середина хода кольца. Узор едет
        /// вместе с водой, шва нет, а комья на середине хода — своего размера в метрах.
        /// </summary>
        private float _noiseRadius = 3f;
        private float _seed, _p1, _p2, _p3, _q1, _q2, _r1, _r2;

        public int Ring => _ring;
        public int StartTick => _startTick;
        public int Travel => _travel;

        public void Begin(int ring, int startTick, int travelTicks)
        {
            _ring = ring;
            _startTick = startTick;
            _travel = Mathf.Max(1, travelTicks);
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f); _p2 = Random.Range(0f, 6.283f); _p3 = Random.Range(0f, 6.283f);
            _q1 = Random.Range(0f, 6.283f); _q2 = Random.Range(0f, 6.283f);
            _r1 = Random.Range(0f, 6.283f); _r2 = Random.Range(0f, 6.283f);
            _noiseRadius = .5f * (Game.Sim.Simulation.FoamRingInnerRadius.ToFloat() + Game.Sim.Simulation.FoamRingOuterRadius(ring).ToFloat());
            if (_triangles == null) _triangles = StripTriangles(1, Segments, Columns, true);
            Allocate(Segments * Columns, _triangles);
        }

        /// <summary>Секунды от выхода кольца (шаг 0 хода Sim).</summary>
        public float Seconds(float now) => (now - _startTick + 1f) / Game.Sim.Simulation.TicksPerSecond;
        public float Progress(float now) => (now - _startTick + 1f) / _travel;
        public float BreakSeconds => PelagWhirlwindFormRules.FoamRingBreakSeconds(_ring, _travel);
        /// <summary>Когда последние капли растаяли (_FadeTo материала .46 при разрыве на .30).</summary>
        public float LifeSeconds => BreakSeconds * 1.6f;
        public float Crest(float now) => PelagWhirlwindFormRules.FoamCrestRadius(_ring, _startTick, _travel, now);

        /// <summary>Отклонение края от круга на угле <paramref name="angle"/>, м.</summary>
        public float Wobble(float angle, float now)
        {
            float t = Seconds(now);
            float amp = .03f + .035f * Crest(now);
            return amp * (.55f * Mathf.Sin(3f * angle + _p1 + 1.3f * t) + .30f * Mathf.Sin(5f * angle + _p2 - 1.9f * t)
                          + .15f * Mathf.Sin(9f * angle + _p3 + 2.7f * t));
        }

        public void Build(Mesh mesh, Vector3 centre, float now, FormGroundGrid ground)
        {
            float t = Seconds(now), x = Progress(now);
            float crest = Crest(now);
            // Проявление шириной: с первого кадра вода плотная, своего цвета (без полупрозрачной мути).
            float hwBase = PelagWhirlwindFormRules.FoamBandHalfWidth(crest)
                           * Mathf.Lerp(.35f, 1f, PelagWhirlwindFormRules.Smooth01(t / RevealSeconds));
            float churn = .10f + .30f * PelagWhirlwindFormRules.Smooth01((x - .2f) / .8f);
            float fedScale = ShaderBreakAge / BreakSeconds;
            float roll = RollSpeed * t;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                float hw = hwBase * (1f + .16f * Mathf.Sin(4f * angle + _q1 + 1.1f * t) + .09f * Mathf.Sin(7f * angle + _q2 - 2f * t));
                float mid = crest + Wobble(angle, now) - hw;
                // Рвётся разом по кругу: только мелкая рябь срока (раньше половина кольца уходила на 0,05 с раньше).
                float fed = t * fedScale + .010f * Mathf.Sin(5f * angle + _r2) + .006f * Mathf.Sin(9f * angle + _r1);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                float outer = hw + Margin;
                Color32 color = Vertex(OverAt(mid, .9f, 1.5f), 1f);
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float across = y * outer;
                    float r = mid + across;
                    float px = cos * r, pz = sin * r;
                    float py = ground.At(centre.x + px, centre.z + pz) + Lift - centre.y;
                    int v = i * Columns + c;
                    Vertices[v] = new Vector3(px, py, pz);
                    Uv0[v] = new Vector4(across / hw, hw, Mathf.Max(0f, fed), 1000f);
                    float nr = _noiseRadius + across - roll;
                    Uv1[v] = new Vector4(cos * nr, sin * nr, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    /// <summary>
    /// Рукава Водоворота как живая вода (02.10, вечер). Шесть рукавов ложатся
    /// на радиус удара Вихря (не на 4 м тяги): при выходе на 10% шире, к
    /// контакту наматываются ровно на край удара, вращаются по часовой с
    /// плавным разгоном и затуханием (узор бежит к центру), закручиваются туже,
    /// вода течёт по рукаву к герою. Вода затекает от края к центру за 0,14 с,
    /// проявляется шириной (MaelstromArmGrow, v4: не прозрачностью); после
    /// контакта белеет пеной и рвётся на капли от внешних хвостов к центру.
    /// Всё — по непрерывному времени вида.
    /// </summary>
    public sealed class PelagMaelstromWater : FormWaterMesh
    {
        public const string MeshName = "Водоворот: рукава";
        public const int Arms = 6;
        private const int Rows = 31, Columns = 5;
        /// <summary>Поле меша за краем воды, м: туда выпирают комья рваного гребня (_Crest.y рукава до 0,16).</summary>
        private const float Margin = .19f, Lift = .04f;
        /// <summary>Наибольшая полуширина рукава при радиусе удара 2,3 м, м (рукава vortex-1 широкие, ~0,8 м).</summary>
        private const float HalfWidth = .38f;
        /// <summary>Скорость воды вдоль рукава к центру, м/с.</summary>
        private const float FlowSpeed = 1.6f;
        /// <summary>Внешний хвост старше головы на столько — распад идёт от края к герою, с.</summary>
        private const float TailLead = .10f;

        private static int[] _triangles;
        private float _reach, _contact, _phase, _seed;

        public float Contact => _contact;

        public void Begin(float reach, float contactSeconds)
        {
            _reach = Mathf.Max(.5f, reach);
            _contact = Mathf.Max(.05f, contactSeconds);
            _phase = Random.Range(0f, Mathf.PI * 2f);
            _seed = Random.Range(0f, 8f);
            if (_triangles == null) _triangles = StripTriangles(Arms, Rows, Columns, false);
            Allocate(Arms * Rows * Columns, _triangles);
        }

        private float Scale => _reach / 2.3f;
        private float OuterRadius(float t) => _reach * PelagWhirlwindFormRules.MaelstromArmReach(t, _contact);
        private float InnerRadius(float t)
            => Mathf.Lerp(.42f, .26f, PelagWhirlwindFormRules.Smooth01(t / _contact)) * Scale;

        /// <summary>Точка рукава k на доле s (0 — внешний хвост, 1 — голова у героя), в осях корня.</summary>
        public Vector2 ArmPoint(int k, float s, float t)
        {
            float rOut = OuterRadius(t), rIn = InnerRadius(t);
            float r = Mathf.Lerp(rOut, rIn, Mathf.Pow(Mathf.Clamp01(s), .85f));
            float sweep = PelagWhirlwindFormRules.MaelstromSweepDegrees(t, _contact) * Mathf.Deg2Rad;
            // φ растёт к центру (против часовой сверху), поворот — по часовой: узор бежит внутрь.
            float angle = _phase + k * (Mathf.PI * 2f / Arms) - PelagWhirlwindFormRules.MaelstromSpinAngle(t, _contact)
                          + sweep * Mathf.Pow(Mathf.Clamp01(s), 1.1f);
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        public void Build(Mesh mesh, Vector3 centre, float t, FormGroundGrid ground)
        {
            float reveal = PelagWhirlwindFormRules.MaelstromHeadReveal(t);
            // v4: проявление шириной, а не прозрачностью — первые кадры были серо-розовой мутью
            // (полупрозрачный фиолет поверх зелёной травы). Вода плотная и фиолетовая с первого кадра.
            float grow = PelagWhirlwindFormRules.MaelstromArmGrow(t);
            float length = (OuterRadius(t) - InnerRadius(t)) * 1.7f;
            for (int k = 0; k < Arms; k++)
            {
                for (int row = 0; row < Rows; row++)
                {
                    float s = reveal * row / (Rows - 1);
                    Vector2 c = ArmPoint(k, s, t);
                    Vector2 tangent = ArmPoint(k, Mathf.Min(1f, s + .01f), t) - ArmPoint(k, Mathf.Max(0f, s - .01f), t);
                    var normal = new Vector2(-tangent.y, tangent.x).normalized;
                    if (Vector2.Dot(normal, c) < 0f) normal = -normal;
                    // Тупой хвост у края, наибольшая ширина на трети, к герою — острие.
                    float hw = grow * HalfWidth * Scale * Mathf.Max(.04f, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Lerp(.14f, 1f, s))), .75f));
                    float outer = hw + Margin;
                    // У ног героя вода не ложится поверх препятствий (там сабля), дальше — ложится.
                    Color32 color = Vertex(OverAt(c.magnitude, .7f, 1.3f), 1f);
                    float end = Mathf.Min(s * length + .06f, (reveal - s) * length);
                    // Возраст для распада — от контакта (длинная тяга не рвёт воду до удара).
                    float fed = PelagWhirlwindFormRules.MaelstromWaterAge(t, _contact) + (1f - s) * TailLead;
                    float churn = .30f * PelagWhirlwindFormRules.Smooth01((s - .62f) / .38f);
                    float along = s * length - FlowSpeed * t + k * 7.31f;
                    for (int col = 0; col < Columns; col++)
                    {
                        float y = -1f + 2f * col / (Columns - 1);
                        float across = y * outer;
                        Vector2 p = c + normal * across;
                        float py = ground.At(centre.x + p.x, centre.z + p.y) + Lift - centre.y;
                        int v = (k * Rows + row) * Columns + col;
                        Vertices[v] = new Vector3(p.x, py, p.y);
                        Uv0[v] = new Vector4(across / hw, hw, fed, end);
                        Uv1[v] = new Vector4(along, across + k * 2.17f, _seed, churn);
                        Colors[v] = color;
                    }
                }
            }
            Write(mesh);
        }
    }

    /// <summary>
    /// Струи тяги Водоворота (v4; проверка: «струи с 4 м читаются штрихами
    /// дождя»). Вместо прямых вытянутых капель — изогнутые струи воды на
    /// земле: каждая лежит на той же спирали, что рукава (угол растёт к
    /// центру, всё поле вращается вместе с рукавами), голова бежит с края тяги
    /// внутрь с разгоном — вода втягивается, — хвост тянется следом и
    /// догоняет её у рукавов. Голова толще и в пене, хвост сходит в нитку:
    /// куда течёт вода, видно и на стоп-кадре. Меш в осях корня Водоворота.
    /// </summary>
    public sealed class PelagMaelstromStrands : FormWaterMesh
    {
        public const string MeshName = "Водоворот: струи тяги";
        public const int Count = 18;
        private const int Rows = 14, Columns = 3;
        private const float Lift = .05f;
        /// <summary>Полуширина у головы, м: струя заметно тоньше рукава (0,38).</summary>
        private const float HeadHalfWidth = .11f;
        /// <summary>Хвост отстаёт от головы на столько секунд пути.</summary>
        private const float TailLag = .08f;

        private static int[] _triangles;
        private readonly float[] _angle = new float[Count], _delay = new float[Count], _duration = new float[Count];
        private readonly float[] _from = new float[Count], _to = new float[Count], _sweep = new float[Count];
        private float _contact, _seed;

        public void Begin(float pullRadius, float reach, float contactSeconds)
        {
            _contact = Mathf.Max(.05f, contactSeconds);
            _seed = Random.Range(0f, 8f);
            float start = Random.Range(0f, Mathf.PI * 2f);
            // Старты и пути струй растянуты вместе с тягой Sim: струи текут всю тягу.
            float stretch = PelagWhirlwindFormRules.MaelstromPullStretch(_contact);
            for (int i = 0; i < Count; i++)
            {
                _angle[i] = start + i * (Mathf.PI * 2f / Count) + Random.Range(-.12f, .12f);
                _delay[i] = PelagWhirlwindFormRules.MaelstromStrandDelay(i, Count, stretch) + Random.Range(0f, .015f) * stretch;
                _duration[i] = PelagWhirlwindFormRules.MaelstromStrandDuration(Random.value, stretch);
                _from[i] = pullRadius * Random.Range(.96f, 1.04f);
                _to[i] = reach * Random.Range(.45f, .62f);
                _sweep[i] = Random.Range(1.45f, 1.85f);
            }
            if (_triangles == null) _triangles = StripTriangles(Count, Rows, Columns, false);
            Allocate(Count * Rows * Columns, _triangles);
        }

        /// <summary>Все струи влились в рукава.</summary>
        public bool Done(float t)
        {
            for (int i = 0; i < Count; i++)
                if (t < _delay[i] + _duration[i] + TailLag + .02f) return false;
            return true;
        }

        private Vector2 Point(int i, float u, float spin)
        {
            float r = Mathf.Lerp(_from[i], _to[i], u);
            float a = _angle[i] + _sweep[i] * u - spin;
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        public void Build(Mesh mesh, Vector3 centre, float t, FormGroundGrid ground)
        {
            float spin = PelagWhirlwindFormRules.MaelstromSpinAngle(t, _contact);
            for (int i = 0; i < Count; i++)
            {
                float head = PelagWhirlwindFormRules.MaelstromStrandTravel(t - _delay[i], _duration[i]);
                float tail = PelagWhirlwindFormRules.MaelstromStrandTravel(t - _delay[i] - TailLag, _duration[i]);
                float span = head - tail;
                float pathLength = Mathf.Sqrt((_from[i] - _to[i]) * (_from[i] - _to[i])
                                              + _sweep[i] * _sweep[i] * .25f * (_from[i] + _to[i]) * (_from[i] + _to[i]));
                float arc = span * pathLength;
                bool visible = span > .004f;
                // Струя рождается тонкой и раздаётся, пока не вытянется на 0,4 м.
                float grow = Mathf.Min(1f, arc / .4f);
                for (int row = 0; row < Rows; row++)
                {
                    float v = row / (float)(Rows - 1);
                    float u = tail + span * v;
                    Vector2 c = Point(i, u, spin);
                    Vector2 tangent = Point(i, u + .01f, spin) - Point(i, u - .01f, spin);
                    var normal = new Vector2(-tangent.y, tangent.x).normalized;
                    if (Vector2.Dot(normal, c) < 0f) normal = -normal;
                    // Комета: у головы толще и скруглена, к хвосту — нитка.
                    float profile = Mathf.Pow(v, .8f) * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(v, 8f)));
                    float hw = visible ? Mathf.Max(.004f, HeadHalfWidth * grow * Mathf.Max(.10f, profile)) : .004f;
                    float end = Mathf.Min(v, 1f - v) * arc;
                    float churn = 1.1f * PelagWhirlwindFormRules.Smooth01((v - .62f) / .30f);
                    for (int col = 0; col < Columns; col++)
                    {
                        float y = -1f + 2f * col / (Columns - 1);
                        float across = y * hw * 1.6f;
                        Vector2 p = c + normal * across;
                        float py = ground.At(centre.x + p.x, centre.z + p.y) + Lift - centre.y;
                        int vtx = (i * Rows + row) * Columns + col;
                        Vertices[vtx] = new Vector3(p.x, py, p.y);
                        Uv0[vtx] = new Vector4(across / hw, hw, 0f, end);
                        Uv1[vtx] = new Vector4(u * pathLength + i * 3.7f, across * 3f + i * 1.3f, _seed, churn);
                        Colors[vtx] = Vertex(1f, visible ? 1f : 0f);
                    }
                }
            }
            Write(mesh);
        }
    }
}
