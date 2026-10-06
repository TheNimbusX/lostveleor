using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шквал v2 (02.10): живая вода вдоль пути прыжка — шейдер Razlom/Whirlwind
    /// Form Water (язык следа рывка: плоская бирюза, комковатые валики пены по
    /// обоим краям, тонкий тёмный обвод, распад на круглые капли без тёмного
    /// кружева). Без MonoBehaviour: меш пишет контроллер каждый кадр по
    /// непрерывному времени вида; тем же кодом может пользоваться проба в редакторе.
    ///
    /// Три вида одной воды:
    ///  • Streak — база: короткая струя за каждым прыжком. Растёт за героем по его
    ///    настоящему пути (стена обрывает и струю), голова — под передней (левой)
    ///    ногой и вспенена; после удара тает фронтом от старта к ногам за ~0,4 с
    ///    (кадры trail-wavy-1 / trail-zigzag-2 и след рывка, кадр А);
    ///  • Trail — форма «Пенный след»: та же струя на посадке за 0,2 с плавно
    ///    растекается в полосу ширины урона (Simulation.FoamTrailHalfWidth) и живёт,
    ///    пока живёт полоса Sim (TryGetSquallFoamStrip). Живая весь срок: вода течёт
    ///    с замедлением (рывок → покой), края дышат бегущими гармониками, ось чуть
    ///    извивается, к концу гребни гуще; в конце вода белеет пеной и рвётся на
    ///    капли от старта к посадке — без застывания и без линейного угасания
    ///    (владелец 02.10 про Вихрь: «линейные… застывает… не SMOOTH»);
    ///  • Return — дуга возврата (талант «Возврат», форма Неуловимый): струя по
    ///    сглаженной кривой From → Via0 → Via1 → точка каста (elusive-return-3).
    ///
    /// Меш — в осях корня (корень = начало пути на земле, без поворота и масштаба),
    /// высота каждой строки — земля под осью (лагерь неровный, уступы арены).
    /// Данные шейдеру (PelagWhirlwindFormWater.cs описывает тот же договор):
    ///   uv0 = (поперёк / полуширина, полуширина м, возраст распада с, до открытого конца м),
    ///   uv1 = (шум вдоль м, шум поперёк м, сдвиг шума, лишняя пена гребня), COLOR.a — проявление.
    /// </summary>
    public sealed class PelagSquallWater
    {
        public enum Kind : byte { Streak = 0, Trail = 1, Return = 2 }

        public const string MeshName = "Шквал: вода";
        /// <summary>_Break.x / _DropLife материала M_Squall_Water — PelagSquallFoamRules.</summary>
        public const float ShaderBreakAge = PelagSquallFoamRules.ShaderBreakAge;
        public const float ShaderDropLife = PelagSquallFoamRules.ShaderDropLife;

        private const int MaxPath = 40, Rows = 40, Columns = 5;
        private const int VertexCount = (Rows + 1) * Columns;
        /// <summary>Поле меша за краем воды, м: туда выпирают комья пены.</summary>
        private const float Margin = .10f;
        private const float Lift = .035f;

        // ---- форма, м
        public const float StreakTailHalfWidth = .09f, StreakHeadHalfWidth = .30f;
        /// <summary>
        /// Полуширина воды полосы Пенного следа. Урон Sim — 0,45 м от оси
        /// (FoamTrailHalfWidth); комья гребня выпирают ещё на ~0,09 м — видимый
        /// край совпадает с краем урона (правило владельца для наземных эффектов).
        /// </summary>
        public const float TrailHalfWidth = .38f;
        /// <summary>Голова струи под передней (левой) ногой: лодыжка на контакте ~0,44 м впереди корня + рваный край.</summary>
        public const float HeadLead = .62f;
        public const float TailBack = .10f, HeadRound = .30f;
        /// <summary>Концы полосы заходят за старт и посадку: углы зигзага сходятся, а не рвутся.</summary>
        public const float TrailOverhang = .16f;

        // ---- время, с
        /// <summary>После удара последний участок (у ног) тает к этому сроку.</summary>
        public const float StreakLifeAfterEnd = ShaderBreakAge + ShaderDropLife + .06f;
        /// <summary>Страховка: струя без удара (сброс арены, обрыв серии без события).</summary>
        public const float MaxLife = 1.6f;
        /// <summary>Струя растекается в полосу за это время (выход, без рывка ширины).</summary>
        public const float TrailSpread = .20f;
        // Распад полосы, ход воды, хвост струи — PelagSquallFoamRules (там же тесты).

        private readonly Vector3[] _path = new Vector3[MaxPath];
        private readonly float[] _pathS = new float[MaxPath];
        private int _pathCount;
        private float _pathLength;
        private Vector3 _root;

        private readonly float[] _historyAge = new float[64];
        private readonly float[] _historyLength = new float[64];
        private int _historyCount;

        private readonly Vector3[] _vertices = new Vector3[VertexCount];
        private readonly Vector4[] _uv0 = new Vector4[VertexCount];
        private readonly Vector4[] _uv1 = new Vector4[VertexCount];
        private readonly Color32[] _colors = new Color32[VertexCount];
        private static int[] _triangles;
        private static readonly Vector3[] _curve = new Vector3[MaxPath];

        private float _seed, _p1, _p2, _p3, _noiseAlong, _noiseAcross;

        public Kind Mode { get; private set; }
        /// <summary>Секунды от старта прыжка (время вида: тик Sim + доля).</summary>
        public float Age { get; private set; }
        /// <summary>Сколько герой прошёл вдоль пути, м.</summary>
        public float Length { get; private set; }
        public float EndedAt { get; private set; } = -1f;
        public float FinalLength { get; private set; }
        public bool Ended => EndedAt >= 0f;
        /// <summary>Возраст, на котором струя стала полосой Sim (−1 — ещё нет).</summary>
        public float BoundAt { get; private set; } = -1f;
        /// <summary>Возраст, к которому полоса Sim кончается.</summary>
        public float UntilAge { get; private set; }
        public bool Bound => BoundAt >= 0f;
        public Vector3 Root => _root;
        public float PathLength => _pathLength;

        public bool Done
        {
            get
            {
                if (Mode == Kind.Trail) return Bound ? Age >= UntilAge + .05f : Age >= MaxLife;
                return Age >= MaxLife || (Ended && Age >= EndedAt + StreakLifeAfterEnd);
            }
        }

        /// <summary>
        /// Новый путь. <paramref name="smooth"/> — сгладить ломаную (дуга возврата
        /// через Via0/Via1); иначе прямая от первой точки к последней.
        /// <paramref name="ground"/> — высота земли в точке (x, z); null — высота точек.
        /// </summary>
        public void Begin(Kind mode, Vector3[] points, int count, bool smooth, System.Func<float, float, float> ground)
        {
            Mode = mode;
            Age = 0f;
            Length = 0f;
            EndedAt = -1f;
            FinalLength = 0f;
            BoundAt = -1f;
            UntilAge = 0f;
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f); _p2 = Random.Range(0f, 6.283f); _p3 = Random.Range(0f, 6.283f);
            _noiseAlong = Random.Range(0f, 40f);
            _noiseAcross = Random.Range(0f, 40f);
            SetPath(points, count, smooth, ground);
            _historyCount = 0;
            Record();
            if (_triangles == null) _triangles = Triangles();
        }

        /// <summary>Полоса Sim легла (SquallFoamStrip): путь — ровно полоса урона, срок — до конца полосы.</summary>
        public void Bind(Vector3 from, Vector3 to, float untilAge, System.Func<float, float, float> ground)
        {
            _curve[0] = from;
            _curve[1] = to;
            SetPath(_curve, 2, false, ground);
            if (!Ended) End(_pathLength);
            FinalLength = _pathLength;
            BoundAt = Age;
            UntilAge = Mathf.Max(Age + .2f, untilAge);
        }

        /// <summary>Пенный след без полосы Sim (удар сорвался): доживает обычной струёй.</summary>
        public void Demote() { if (Mode == Kind.Trail && !Bound) Mode = Kind.Streak; }

        /// <summary>Кадр: <paramref name="age"/> — секунды от старта, <paramref name="heroAlong"/> — где герой вдоль пути, м.</summary>
        public void Advance(float age, float heroAlong)
        {
            Age = Mathf.Max(Age, age);
            float length = Mathf.Max(Length, heroAlong);
            if (Ended)
            {
                if (Age - EndedAt >= .10f) length = FinalLength;
                length = Mathf.Min(length, FinalLength);
            }
            else length = Mathf.Min(length, _pathLength);
            Length = Mathf.Max(0f, length);
            Record();
        }

        /// <summary>Удар (прибытие): дальше струя не растёт; <paramref name="traveled"/> — настоящий путь, м.</summary>
        public void End(float traveled)
        {
            if (Ended) return;
            EndedAt = Age;
            FinalLength = Mathf.Max(0f, traveled);
        }

        /// <summary>Где на пути ближайшая к точке позиция, м от начала (по земле).</summary>
        public float Project(Vector3 world)
        {
            if (_pathCount < 2) return 0f;
            float best = float.MaxValue, bestS = 0f;
            int last = _pathCount - 2;
            for (int i = 0; i <= last; i++)
            {
                Vector3 a = _path[i], b = _path[i + 1];
                var ab = new Vector2(b.x - a.x, b.z - a.z);
                var ap = new Vector2(world.x - a.x, world.z - a.z);
                float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                // Крайние отрезки продолжаются за концы: позади старта — меньше нуля, за посадкой — больше длины.
                float u = Vector2.Dot(ap, ab) / len2;
                u = Mathf.Clamp(u, i == 0 ? -4f : 0f, i == last ? 4f : 1f);
                float d = (ap - ab * u).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bestS = _pathS[i] + (_pathS[i + 1] - _pathS[i]) * u;
            }
            return bestS;
        }

        /// <summary>Точка на оси пути в <paramref name="s"/> м (за концами — продолжение крайних отрезков), y — земля.</summary>
        public Vector3 Sample(float s, out Vector3 tangent)
        {
            if (_pathCount < 2) { tangent = Vector3.forward; return _root; }
            int n = _pathCount;
            if (s <= 0f) { tangent = Flat(_path[1] - _path[0]); return _path[0] + tangent * s; }
            if (s >= _pathLength) { tangent = Flat(_path[n - 1] - _path[n - 2]); return _path[n - 1] + tangent * (s - _pathLength); }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_pathS[mid] <= s) lo = mid; else hi = mid;
            }
            float span = Mathf.Max(1e-5f, _pathS[hi] - _pathS[lo]);
            float u = (s - _pathS[lo]) / span;
            tangent = Flat(_path[hi] - _path[lo]);
            return Vector3.Lerp(_path[lo], _path[hi], u);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        private static readonly Vector3[] _smooth = new Vector3[MaxPath];

        private void SetPath(Vector3[] points, int count, bool smooth, System.Func<float, float, float> ground)
        {
            count = Mathf.Clamp(count, 1, points != null ? points.Length : 0);
            if (count <= 0) { _pathCount = 0; _pathLength = 0f; return; }
            int n;
            if (smooth && count >= 3)
            {
                // Катмулл-Ром через все точки: дуга проходит через Via0/Via1, где идёт и тело.
                int segments = count - 1;
                int perSegment = Mathf.Max(2, (MaxPath - 1) / segments);
                n = 0;
                for (int seg = 0; seg < segments && n < MaxPath; seg++)
                {
                    Vector3 p0 = points[Mathf.Max(0, seg - 1)], p1 = points[seg];
                    Vector3 p2 = points[seg + 1], p3 = points[Mathf.Min(count - 1, seg + 2)];
                    for (int k = 0; k < perSegment && n < MaxPath - 1; k++)
                    {
                        float t = k / (float)perSegment;
                        _smooth[n++] = CatmullRom(p0, p1, p2, p3, t);
                    }
                }
                _smooth[n++] = points[count - 1];
            }
            else
            {
                // Прямая: точки через ~0,35 м, чтобы земля под струёй бралась по пути, а не по концам.
                Vector3 a = points[0], b = points[count - 1];
                float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                n = Mathf.Clamp(Mathf.CeilToInt(length / .35f) + 1, 2, MaxPath);
                for (int i = 0; i < n; i++) _smooth[i] = Vector3.Lerp(a, b, i / (float)(n - 1));
            }
            _pathCount = n;
            _pathLength = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _smooth[i];
                if (ground != null) p.y = ground(p.x, p.z);
                _path[i] = p;
                if (i > 0) _pathLength += new Vector2(p.x - _path[i - 1].x, p.z - _path[i - 1].z).magnitude;
                _pathS[i] = _pathLength;
            }
            _root = _path[0];
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        private void Record()
        {
            if (_historyCount > 0 && Length <= _historyLength[_historyCount - 1]) return;
            if (_historyCount == _historyAge.Length)
            {
                for (int i = 1; i < _historyCount / 2; i++)
                {
                    _historyAge[i] = _historyAge[i * 2];
                    _historyLength[i] = _historyLength[i * 2];
                }
                _historyCount /= 2;
            }
            _historyAge[_historyCount] = Age;
            _historyLength[_historyCount] = Length;
            _historyCount++;
        }

        /// <summary>Сколько секунд назад герой прошёл точку <paramref name="s"/>; у ног — сколько прошло с конца роста.</summary>
        public float SegmentAge(float s)
        {
            if (_historyCount == 0) return 0f;
            if (s >= Length) return Age - _historyAge[_historyCount - 1];
            if (s <= _historyLength[0]) return Age - _historyAge[0];
            for (int i = 1; i < _historyCount; i++)
            {
                if (_historyLength[i] < s) continue;
                float span = _historyLength[i] - _historyLength[i - 1];
                float t = span > 1e-5f ? (s - _historyLength[i - 1]) / span : 1f;
                return Age - Mathf.Lerp(_historyAge[i - 1], _historyAge[i], t);
            }
            return 0f;
        }

        /// <summary>Сдвиг рисунка воды к старту, м: у полосы — рывок и плавное замедление (без излома скорости).</summary>
        private float Flow() => PelagSquallFoamRules.Flow(Mode == Kind.Trail && Bound, Age, BoundAt);

        /// <summary>Доля «полосы» (0 — струя, 1 — полоса Пенного следа), с выходом.</summary>
        public float Spread => Mode == Kind.Trail && Bound ? Smooth01((Age - BoundAt) / TrailSpread) : 0f;

        /// <summary>Полуширина воды в <paramref name="s"/> без дыхания — для частиц по краю.</summary>
        public float HalfWidthAt(float s)
        {
            float spread = Spread;
            Span(spread, out float tailS, out float headS);
            float k = Mathf.Clamp01((s - tailS) / Mathf.Max(.02f, headS - tailS));
            float streak = Mathf.Lerp(StreakTailHalfWidth, StreakHeadHalfWidth, Mathf.Pow(k, .9f));
            float ends = Mathf.Min(s - tailS, headS - s);
            float trail = TrailHalfWidth * Mathf.Lerp(.55f, 1f, Mathf.Sqrt(Mathf.Clamp01(ends / .35f)));
            return Mathf.Lerp(streak, trail, spread);
        }

        private void Span(float spread, out float tailS, out float headS)
        {
            float lane = Ended ? FinalLength : Length;
            headS = Mathf.Lerp(Length + HeadLead, lane + TrailOverhang, spread);
            tailS = Mathf.Lerp(-TailBack, -TrailOverhang, spread);
        }

        /// <summary>Пересобрать меш по текущему времени (после Advance).</summary>
        public void Build(Mesh mesh)
        {
            float spread = Spread;
            Span(spread, out float tailS, out float headS);
            float span = Mathf.Max(.02f, headS - tailS);
            float lifeT = Bound ? Age - BoundAt : 0f;
            float life = Bound ? Mathf.Max(.1f, UntilAge - BoundAt) : 1f;
            float remaining = Bound ? UntilAge - Age : 99f;
            // Дыхание краёв входит с выходом, а не включается кадром.
            float amp = spread * Smooth01(lifeT / .5f);
            float shift = Flow();
            Color32 color = Alpha(Smooth01(Age / .03f));
            for (int r = 0; r <= Rows; r++)
            {
                float k = r / (float)Rows;
                float s = tailS + span * k;
                Vector3 centre = Sample(s, out Vector3 tangent);
                var normal = new Vector3(-tangent.z, 0f, tangent.x);
                float ends = Mathf.Max(0f, Mathf.Min(s - tailS, headS - s));

                // Струя: узкий хвост, широкая голова, нос под ногой скруглён (как след рывка).
                float hw = Mathf.Lerp(StreakTailHalfWidth, StreakHeadHalfWidth, Mathf.Pow(k, .9f))
                           * Mathf.Lerp(.70f, 1f, Mathf.Sqrt(Mathf.Clamp01((headS - s) / HeadRound)));
                float meander = 0f;
                if (spread > 0f)
                {
                    // Полоса: ширина урона, круглые концы; две бегущие гармоники — края дышат, ось чуть извивается.
                    float trail = TrailHalfWidth * Mathf.Lerp(.55f, 1f, Mathf.Sqrt(Mathf.Clamp01(ends / .35f)));
                    float breathe = 1f + amp * (.08f * Mathf.Sin(6.2832f * s / 1.4f + 1.3f * lifeT + _p1)
                                                + .05f * Mathf.Sin(6.2832f * s / .75f - 2.1f * lifeT + _p2));
                    hw = Mathf.Lerp(hw, trail * breathe, spread);
                    meander = .05f * amp * Mathf.Sin(6.2832f * s / 1.9f + .7f * lifeT + _p3) * Mathf.Clamp01(ends / .6f);
                }

                // Возраст распада: у струи — сколько назад герой здесь прошёл (+ хвост старше);
                // у полосы — до конца её срока в Sim (хвост рвётся первым), до того — ноль.
                float fed;
                if (Mode == Kind.Trail)
                    fed = Bound ? PelagSquallFoamRules.TrailBreakAge(remaining, k) : 0f;
                else fed = SegmentAge(Mathf.Min(s, Length)) + (1f - k) * PelagSquallFoamRules.StreakTailLead;

                // Пена гребня: у струи голова вспенена (резкий передний край), у полосы — к концу
                // срока гребни гуще, на углах зигзага (концах) — пенные завихрения, как на кадрах.
                float churnStreak = .05f + .40f * Smooth01((s - (headS - .7f)) / .7f);
                float churnTrail = .06f + .16f * Smooth01(lifeT / life) + .22f * (1f - Smooth01(ends / .5f));
                float churn = Mathf.Lerp(churnStreak, churnTrail, spread);

                float along = s + shift + _noiseAlong;
                float outer = hw + Margin;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float across = y * outer;
                    Vector3 p = centre + normal * (across + meander);
                    int v = r * Columns + c;
                    _vertices[v] = new Vector3(p.x - _root.x, centre.y + Lift - _root.y, p.z - _root.z);
                    _uv0[v] = new Vector4(across / hw, hw, fed, ends);
                    _uv1[v] = new Vector4(along, across + _noiseAcross, _seed, churn);
                    _colors[v] = color;
                }
            }
            bool fresh = mesh.vertexCount != VertexCount;
            if (fresh) mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetColors(_colors);
            if (fresh) mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Свой меш у объекта из пула: создаётся раз и дальше переписывается.</summary>
        public static Mesh MeshFor(MeshFilter filter)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh != null && mesh.name == MeshName) return mesh;
            mesh = new Mesh { name = MeshName };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return mesh;
        }

        private static int[] Triangles()
        {
            var triangles = new int[Rows * (Columns - 1) * 6];
            int at = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns - 1; c++)
                {
                    int a = r * Columns + c, b = (r + 1) * Columns + c;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 1;
                    triangles[at++] = a + 1; triangles[at++] = b; triangles[at++] = b + 1;
                }
            return triangles;
        }

        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static Color32 Alpha(float alpha) => new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
    }
}
