using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Живая вода Абордажа v2 — меши для шейдера Razlom/Whirlwind Form Water (язык
    /// следа рывка: плоская вода, комья пены по краям, тонкий тёмный обвод, распад
    /// на круглые капли). Договор вершин — тот же, что у форм Вихря и Шквала v2
    /// (PelagWhirlwindFormWater.cs): uv0 = (поперёк / полуширина, полуширина м,
    /// возраст распада с, до открытого конца м), uv1 = (шум, шум, сдвиг шума,
    /// лишняя пена гребня), COLOR = (поверх низких препятствий, —, —, непрозрачность).
    /// Меш пишет контроллер каждый кадр по непрерывному времени показа; корень
    /// объекта без поворота и масштаба, вершины — мир минус корень.
    ///
    ///  • <see cref="PelagAbordageRibbon"/> — лента в воздухе, повёрнутая к камере:
    ///    вода вдоль натянутой цепи (кадр A: бирюзовая нить по цепи, с неё сыплются
    ///    капли) и короткий росчерк за головой летящего якоря;
    ///  • <see cref="PelagAbordageRingWater"/> — кольцо на земле: кольцо падения и розетка
    ///    у основания Гейзера (кадр F, морская зелень);
    ///  • <see cref="PelagAbordageQuakeWater"/> — всплеск Обвала (кадр D, кобальт; круг 4 — свой
    ///    шейдер Razlom/Abordage Quake Splash: лопасти воды, мокрая земля у героя, распад дырами);
    ///  • <see cref="PelagAbordageJetWater"/> — веер-конус на земле за целью:
    ///    струя Пробоины (кадр G, маджента);
    ///  • <see cref="PelagAbordageColumnWater"/> — вертикальный столб к камере:
    ///    столб Гейзера, на шапке — подброшенная цель (тела пишут трафарет, вода
    ///    по ним не рисуется — цель видна поверх столба).
    /// </summary>
    public sealed class PelagAbordageRibbon : FormWaterMesh
    {
        public const string MeshName = "Абордаж: лента";
        private const int Rows = 24, Columns = 5;
        /// <summary>Поле за краем воды, м: тонкие комья пены ленты.</summary>
        private const float Margin = .04f;
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

        /// <summary>
        /// Лента от <paramref name="a"/> (хвост: рука или конец росчерка) к <paramref name="b"/>
        /// (голова: якорь). Поперёк — к камере; ось сдвинута на <paramref name="drop"/> вниз по
        /// экрану, чтобы вода шла под звеньями, а не закрывала цепь (вода рисуется поверх всего,
        /// кроме тел). <paramref name="flow"/> — сдвиг рисунка к хвосту, м: вода бежит по цепи к герою.
        /// </summary>
        public void Build(Mesh mesh, Vector3 root, Vector3 a, Vector3 b, Vector3 camera, Vector3 screenDown, float drop,
            float halfA, float halfB, float ageA, float ageB, float flow, float churn, float alpha, float time)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;
            Vector3 tangent = length > 1e-4f ? axis / length : Vector3.forward;
            Color32 color = Vertex(0f, alpha);
            for (int r = 0; r <= Rows; r++)
            {
                float k = r / (float)Rows;
                float s = length * k;
                Vector3 centre = a + axis * k + screenDown * drop;
                Vector3 view = camera - centre;
                Vector3 across = Vector3.Cross(tangent, view);
                if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(tangent, Vector3.up);
                across.Normalize();
                // Вода дышит вдоль ленты двумя бегущими волнами — не застывшая нить.
                float breathe = 1f + .18f * Mathf.Sin(s * 7.1f + _p1 - time * 9f) + .10f * Mathf.Sin(s * 13.3f + _p2 + time * 6f);
                float hw = Mathf.Max(.004f, Mathf.Lerp(halfA, halfB, k) * breathe);
                float ends = Mathf.Min(s, length - s);
                float age = Mathf.Lerp(ageA, ageB, k);
                float outer = hw + Margin;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float off = y * outer;
                    int v = r * Columns + c;
                    Vertices[v] = centre + across * off - root;
                    Uv0[v] = new Vector4(off / hw, hw, age, ends);
                    Uv1[v] = new Vector4(s + flow, off, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    /// <summary>
    /// Кольцо воды на земле (падение Гейзера, розетка Гейзера): полоса [inner, crest] с волнистым
    /// краем. Вид — <see cref="Style"/> в Begin:
    ///  • Plain — ровное кольцо (падение Гейзера);
    ///  • Rosette — розетка у основания Гейзера (кадр F): круглые лепестки по внешнему краю. Круг 4
    ///    (ревью: тёмное пятно пола у основания ствола) — сплошной диск: внутренний край за центром
    ///    (<see cref="PelagAbordageVfxRules.GeyserRosetteFill"/>), у ствола дыры нет.
    /// Обвал с круга 4 — свой всплеск <see cref="PelagAbordageQuakeWater"/>.
    /// </summary>
    public sealed class PelagAbordageRingWater : FormWaterMesh
    {
        public enum Style : byte { Plain = 0, Rosette = 2 }

        public const string MeshName = "Абордаж: кольцо";
        private const int Segments = 180, Columns = 5, MaxFingers = 24;
        private const float Margin = .16f, Lift = .035f;
        private static int[] _triangles;
        private float _seed, _p1, _p2, _p3, _q1, _r1;
        private int _fingers;
        private Style _style;
        private readonly float[] _fingerAt = new float[MaxFingers], _fingerHalf = new float[MaxFingers], _fingerLength = new float[MaxFingers];

        /// <param name="style">Вид кольца (см. класс).</param>
        /// <param name="fingers">Лепестков по кругу (Plain — без них).</param>
        public void Begin(Style style = Style.Plain, int fingers = 0)
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f); _p2 = Random.Range(0f, 6.283f); _p3 = Random.Range(0f, 6.283f);
            _q1 = Random.Range(0f, 6.283f); _r1 = Random.Range(0f, 6.283f);
            _style = style;
            _fingers = style == Style.Plain ? 0 : Mathf.Clamp(fingers, 0, MaxFingers);
            float step = Mathf.PI * 2f / Mathf.Max(1, _fingers);
            float turn = Random.Range(0f, step);
            for (int i = 0; i < _fingers; i++)
            {
                // Лепестки полные и ровные: круглая розетка, а не звезда; каждый третий — до самого края.
                _fingerAt[i] = turn + step * (i + Random.Range(-.21f, .21f));
                _fingerHalf[i] = step * Random.Range(.62f, .85f);
                _fingerLength[i] = i % 3 == 0 ? 1f : Random.Range(.78f, 1f);
            }
            if (_triangles == null) _triangles = StripTriangles(1, Segments, Columns, true);
            Allocate(Segments * Columns, _triangles);
        }

        /// <summary>Лепесток под углом <paramref name="angle"/>: 0 — выемка, 1 — край лепестка (без лепестков — 1).</summary>
        private float PetalAt(float angle)
        {
            if (_fingers == 0) return 1f;
            float petal = 0f;
            for (int i = 0; i < _fingers; i++)
            {
                float u = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, _fingerAt[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad / _fingerHalf[i];
                if (u <= -1f || u >= 1f) continue;
                petal = Mathf.Max(petal, _fingerLength[i] * PelagAbordageVfxRules.FingerSoft(u));
            }
            return petal;
        }

        /// <summary>Отклонение края от круга, м: три бегущие гармоники (край живой, не циркуль).</summary>
        public float Wobble(float angle, float crest, float t)
        {
            float amp = .03f + .03f * crest;
            return amp * (.55f * Mathf.Sin(3f * angle + _p1 + 1.6f * t) + .30f * Mathf.Sin(5f * angle + _p2 - 2.2f * t)
                          + .15f * Mathf.Sin(9f * angle + _p3 + 3.1f * t));
        }

        /// <summary>
        /// <paramref name="centre"/> — центр на земле (= корень объекта); <paramref name="age"/> — возраст
        /// распада (0 — живая вода); <paramref name="roll"/> — пена гребня скатывается наружу, м.
        /// </summary>
        /// <param name="notch">Глубина выемок между лепестками, м (0 — ровное кольцо).</param>
        public void Build(Mesh mesh, Vector3 centre, float inner, float crest, float age, float churn, float roll,
            float time, float alpha, FormGroundGrid ground, float notch = 0f)
        {
            inner = Mathf.Clamp(inner, 0f, Mathf.Max(0f, crest - .06f));
            float hwBase = Mathf.Max(.03f, .5f * (crest - inner));
            float noiseRadius = Mathf.Max(.8f, .5f * (inner + crest));
            bool rosette = _style == Style.Rosette && _fingers > 0;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                float breathe = 1f + .14f * Mathf.Sin(4f * angle + _q1 + 1.3f * time);
                float hw = hwBase * breathe;
                float mid = crest + Wobble(angle, crest, time) - hw;
                float finger = 1f;
                if (rosette)
                {
                    // Розетка (кадр F): круглые лепестки по внешнему краю; внутренний край — за центром,
                    // диск сплошной (круг 4: без дыры с тёмным полом у ствола).
                    finger = PetalAt(angle);
                    float petal = PelagAbordageVfxRules.RaggedOuter(crest, finger, notch) + Wobble(angle, crest, time) * finger;
                    float innerEdge = -PelagAbordageVfxRules.GeyserRosetteFill * petal;
                    hw = Mathf.Max(.02f, .5f * (petal - innerEdge));
                    mid = petal - hw;
                }
                // Рвётся разом по кругу, с мелкой рябью срока; у розетки выемки — первыми.
                float fed = age > 0f ? age + .012f * Mathf.Sin(5f * angle + _r1) + (rosette ? .06f * (1f - finger) : 0f) : 0f;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                float outer = hw + Margin;
                Color32 color = Vertex(OverAt(mid, .9f, 1.5f), alpha);
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    // Вершины за центром сходятся в центр; поперёк — по настоящему радиусу (центр диска — внутри воды).
                    float r = Mathf.Max(0f, mid + y * outer);
                    float across = r - mid;
                    float px = cos * r, pz = sin * r;
                    float py = (ground != null ? ground.At(centre.x + px, centre.z + pz) : centre.y) + Lift - centre.y;
                    int v = i * Columns + c;
                    Vertices[v] = new Vector3(px, py, pz);
                    Uv0[v] = new Vector4(across / hw, hw, Mathf.Max(0f, fed), 1000f);
                    float nr = noiseRadius + across - roll;
                    Uv1[v] = new Vector4(cos * nr, sin * nr, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    /// <summary>
    /// Всплеск Обвала (круг 4, кадр D-quake) — диск на земле для своего шейдера Razlom/Abordage Quake
    /// Splash. Круг 3 (кольцо воды форм) в игре читался ровным «солнцем»: одинаковые шипы с пилой пены и
    /// чёрным обводом, сиреневая полоса у героя. Здесь — неровный всплеск: крупные круглые лопасти воды
    /// разной длины (<see cref="PelagAbordageVfxRules.QuakeLobeEdge"/>, кончик длинной — на крае урона Sim),
    /// под героем — непрозрачная мокрая земля: ядро, круглые лепестки и тонкие лучи в просветах между
    /// лопастями (<see cref="PelagAbordageVfxRules.QuakeEarthEdge"/>). Полярная сетка: <see cref="Segments"/>
    /// углов × кольца от центра до края меша (край лопасти или луча земли + поле под комья пены).
    /// Вершины: uv0 = (r / край лопасти, край лопасти м, возраст распада, граница земли / край),
    /// uv1 = (полярный шум ×2, сдвиг шума, r м), COLOR = (поверх препятствий, —, вес пены кончика, непрозрачность).
    /// </summary>
    public sealed class PelagAbordageQuakeWater : FormWaterMesh
    {
        public const string MeshName = "Абордаж: всплеск Обвала";
        private const int Segments = 160, MaxLobes = 32, MaxPetals = 32, MaxRays = 16;
        /// <summary>Поле за краем лопасти, м: круглые комья пены выпирают наружу.</summary>
        private const float Margin = .22f, Lift = .035f;
        /// <summary>Кольца вершин в долях края меша: гуще у края лопасти.</summary>
        private static readonly float[] Rings = { 0f, .07f, .15f, .24f, .33f, .43f, .53f, .62f, .70f, .77f, .83f, .88f, .92f, .96f, 1f };
        private static int[] _triangles;
        private float _seed, _p1, _p2, _p3, _c1, _c2;
        private int _lobes, _petals, _rays, _big;
        private readonly float[] _lobeAt = new float[MaxLobes], _lobeHalf = new float[MaxLobes], _lobeLength = new float[MaxLobes];
        private readonly float[] _petalAt = new float[MaxPetals], _petalHalf = new float[MaxPetals], _petalLength = new float[MaxPetals];
        private readonly float[] _rayAt = new float[MaxRays], _rayHalf = new float[MaxRays], _rayLength = new float[MaxRays];

        public void Begin()
        {
            _seed = Random.Range(0f, 8f);
            _p1 = Random.Range(0f, 6.283f); _p2 = Random.Range(0f, 6.283f); _p3 = Random.Range(0f, 6.283f);
            _c1 = Random.Range(0f, 6.283f); _c2 = Random.Range(0f, 6.283f);
            // Крупные лопасти: каждая третья — до края урона Sim, остальные короче; соседние перекрываются.
            _big = Mathf.Clamp(PelagAbordageVfxRules.QuakeLobes, 3, MaxLobes / 3);
            float step = Mathf.PI * 2f / _big, turn = Random.Range(0f, step);
            _lobes = 0;
            for (int i = 0; i < _big; i++)
                AddLobe(turn + step * (i + Random.Range(-.25f, .25f)), step * Random.Range(.55f, .90f),
                    i % 3 == 0 ? 1f : Random.Range(PelagAbordageVfxRules.QuakeLobeShortest, .95f));
            // Средние лопасти где придётся (раздвоенные концы, горбы на боках) и тонкие струи — край неровный,
            // всплеск, а не ромашка по циркулю.
            for (int i = 0; i < 12 && _lobes < MaxLobes; i++)
                AddLobe(Random.Range(0f, Mathf.PI * 2f), step * Random.Range(.18f, .32f), Random.Range(.62f, 1f));
            for (int i = 0; i < 6 && _lobes < MaxLobes; i++)
                AddLobe(Random.Range(0f, Mathf.PI * 2f), step * Random.Range(.07f, .12f), Random.Range(.6f, .92f));
            // Лучи земли — в просветах между крупными лопастями, с круглым концом, чуть дальше лепестков.
            _rays = 0;
            for (int i = 0; i < _big && _rays < MaxRays; i++)
            {
                _rayAt[_rays] = turn + step * (i + .5f + Random.Range(-.15f, .15f));
                _rayHalf[_rays] = step * Random.Range(.18f, .28f);
                _rayLength[_rays] = Random.Range(PelagAbordageVfxRules.QuakeRayMin, PelagAbordageVfxRules.QuakeRayMax);
                _rays++;
            }
            // Лепестки земли у героя (бурый «цветок» кадра D): полные, с круглым концом, вода — клиньями между ними.
            _petals = Mathf.Clamp(PelagAbordageVfxRules.QuakePetals, 0, MaxPetals);
            float pstep = Mathf.PI * 2f / Mathf.Max(1, _petals), pturn = Random.Range(0f, pstep);
            for (int i = 0; i < _petals; i++)
            {
                _petalAt[i] = pturn + pstep * (i + Random.Range(-.2f, .2f));
                _petalHalf[i] = pstep * Random.Range(.36f, .48f);
                _petalLength[i] = Random.Range(PelagAbordageVfxRules.QuakePetalMin, PelagAbordageVfxRules.QuakePetalMax);
            }
            if (_triangles == null) _triangles = StripTriangles(1, Segments, Rings.Length, true);
            Allocate(Segments * Rings.Length, _triangles);
        }

        private void AddLobe(float at, float half, float length)
        {
            _lobeAt[_lobes] = at;
            _lobeHalf[_lobes] = half;
            _lobeLength[_lobes] = length;
            _lobes++;
        }

        /// <summary>Вид горба: лопасть воды — гладкий, лепесток и луч земли — круглый конец (клин — про запас).</summary>
        private enum Hump : byte { Soft, Round, Wedge }

        private static float Profile(float angle, float[] at, float[] half, float[] length, int count, Hump hump)
        {
            float best = 0f;
            for (int i = 0; i < count; i++)
            {
                float u = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, at[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad / half[i];
                if (u <= -1f || u >= 1f) continue;
                float h = hump == Hump.Soft ? PelagAbordageVfxRules.FingerSoft(u)
                    : hump == Hump.Round ? PelagAbordageVfxRules.FingerRound(u) : PelagAbordageVfxRules.FingerWedge(u);
                best = Mathf.Max(best, length[i] * h);
            }
            return best;
        }

        /// <summary>Профиль лопастей под углом: 0 — глубина просвета, 1 — кончик длинной лопасти.</summary>
        public float Lobe(float angle) => Profile(angle, _lobeAt, _lobeHalf, _lobeLength, _lobes, Hump.Soft);

        /// <summary>Край воды под углом, м: лопасть плюс лёгкая бегущая рябь, не дальше гребня (края урона Sim).</summary>
        public float Edge(float angle, float crest, float time)
        {
            float lobe = Lobe(angle);
            float wobble = (.02f + .012f * crest) * (.6f * Mathf.Sin(3f * angle + _p1 + 1.4f * time) + .4f * Mathf.Sin(7f * angle + _p2 - 2.1f * time));
            return Mathf.Min(crest, PelagAbordageVfxRules.QuakeLobeEdge(crest, lobe) + wobble * (1f - .8f * lobe));
        }

        /// <summary>Угол на кончике случайной крупной лопасти (кадр D: капли летят с кончиков).</summary>
        public float TipAngle()
        {
            int i = Random.Range(0, Mathf.Max(1, _big));
            return _lobeAt[i] + _lobeHalf[i] * Random.Range(-.45f, .45f);
        }

        /// <summary>
        /// <paramref name="centre"/> — центр на земле (= корень); <paramref name="crest"/> — гребень (край урона Sim
        /// на ходу фронта); <paramref name="age"/> — возраст распада (0 — живая вода; шейдер уводит воду дырами
        /// от центра и рвёт кайму пены на капли).
        /// </summary>
        public void Build(Mesh mesh, Vector3 centre, float crest, float age, float time, float alpha, FormGroundGrid ground)
        {
            crest = Mathf.Max(.05f, crest);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                float lobe = Lobe(angle);
                float edge = Mathf.Max(.04f, Edge(angle, crest, time));
                // Ядро земли не циркуль: две гармоники; лепестки — круглые, лучи — острые клинья.
                float petal = Profile(angle, _petalAt, _petalHalf, _petalLength, _petals, Hump.Round)
                              * (1f + .08f * Mathf.Sin(5f * angle + _c1));
                float ray = Profile(angle, _rayAt, _rayHalf, _rayLength, _rays, Hump.Round);
                float earth = PelagAbordageVfxRules.QuakeEarthEdge(crest, petal, ray)
                              * (1f + .10f * Mathf.Sin(7f * angle + _c2) + .06f * Mathf.Sin(13f * angle + _p3));
                float outerMesh = Mathf.Max(edge, earth) + Margin;
                float tip = PelagAbordageVfxRules.QuakeTipFoam(lobe);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                for (int c = 0; c < Rings.Length; c++)
                {
                    float r = Rings[c] * outerMesh;
                    float px = cos * r, pz = sin * r;
                    float py = (ground != null ? ground.At(centre.x + px, centre.z + pz) : centre.y) + Lift - centre.y;
                    int v = i * Rings.Length + c;
                    Vertices[v] = new Vector3(px, py, pz);
                    Uv0[v] = new Vector4(r / edge, edge, age, earth / edge);
                    float nr = PelagAbordageVfxRules.StreakNoiseRadius(r);
                    Uv1[v] = new Vector4(cos * nr, sin * nr, _seed, r);
                    Colors[v] = new Color32((byte)Mathf.RoundToInt(OverAt(r, .9f, 1.5f) * 255f), 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(tip) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            Write(mesh);
        }
    }

    /// <summary>Струя Пробоины на земле: от вершины (спина цели) к фронту, ширина — край конуса Sim.</summary>
    public sealed class PelagAbordageJetWater : FormWaterMesh
    {
        public const string MeshName = "Абордаж: струя";
        private const int Rows = 28, Columns = 7;
        private const float Margin = .14f, Lift = .045f;
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

        /// <summary>
        /// <paramref name="apex"/> — вершина на земле (= корень), <paramref name="dir"/> — ось струи;
        /// <paramref name="front"/> — докуда дошла вода, м. Рисунок воды бежит от вершины
        /// (<paramref name="flow"/>, м), у вершины вода старше — рвётся первой. <paramref name="apexInset"/> —
        /// первые метры от вершины (прокол сквозь тело цели) струя узкая, конус расходится после них.
        /// Ревью 03.10, круг 2 (кадр G): дальний конец — не прямой срез. Ряды гнутся дугой вокруг
        /// вершины Sim (сектор, как зона урона), нос на ходу белеет пеной и рвётся на капли
        /// (<see cref="PelagAbordageVfxRules.BreachTipAge"/>), рваный конец шейдера — шире.
        /// </summary>
        public void Build(Mesh mesh, Vector3 apex, Vector3 dir, float front, float age, float flow, float churn,
            float time, float alpha, FormGroundGrid ground, float apexInset = 0f)
        {
            front = Mathf.Max(.05f, front);
            var right = new Vector3(dir.z, 0f, -dir.x);
            for (int r = 0; r <= Rows; r++)
            {
                float k = r / (float)Rows;
                float s = front * k;
                // Края конуса дышат — струя живая, не треугольник.
                float hw = PelagAbordageVfxRules.BreachHalfWidth(s - apexInset)
                           * (1f + .08f * Mathf.Sin(s * 2.6f + _p1 - time * 7f) + .05f * Mathf.Sin(s * 5.3f + _p2 + time * 5f));
                // Нос чуть уже (последние 0,6 м) — дугу дальнего конца дают ряды, не сужение.
                hw *= Mathf.Lerp(.78f, 1f, Mathf.Sqrt(Mathf.Clamp01((front - s) / .6f)));
                float fed = age > 0f ? age + (1f - k) * .10f : 0f;
                fed = Mathf.Max(fed, PelagAbordageVfxRules.BreachTipAge(front - s));
                float outer = hw + Margin;
                Color32 color = Vertex(OverAt(s, .3f, .9f), alpha);
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float across = y * outer;
                    float along = s - PelagAbordageVfxRules.BreachArcBack(across, s - apexInset);
                    Vector3 p = apex + dir * along + right * across;
                    float py = (ground != null ? ground.At(p.x, p.z) : apex.y) + Lift;
                    int v = r * Columns + c;
                    Vertices[v] = new Vector3(p.x - apex.x, py - apex.y, p.z - apex.z);
                    Uv0[v] = new Vector4(across / hw, hw, fed, front - s);
                    Uv1[v] = new Vector4(s - flow, across, _seed, churn);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }

    /// <summary>Столб Гейзера: вертикальная полоса воды, повёрнутая к камере, шапка пены сверху.</summary>
    public sealed class PelagAbordageColumnWater : FormWaterMesh
    {
        public const string MeshName = "Абордаж: столб";
        private const int Rows = 30, Columns = 5;
        /// <summary>
        /// Шум воды по высоте сжат: комья пены и струи шейдера вытянуты вверх — вода бьёт
        /// вертикальными струями (круг 2, кадр F), а не пятнами.
        /// </summary>
        private const float NoiseUp = .45f, NoiseAcross = 1.3f;
        private const float Margin = .12f;
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

        /// <summary>
        /// <paramref name="foot"/> — основание на земле (= корень). Рисунок бежит вверх
        /// (<paramref name="flow"/>, м); верх — открытый рваный конец; у основания столб
        /// шире (вода бьёт из земли), у шапки раздаётся пеной. Ревью 03.10 (кадр F, «плоский
        /// ящик»): верх — купол, а не срез; верхняя четверть раздаётся вдвое и белеет пеной (крона).
        /// Круг 2 (с камеры 48° всё ещё низкий широкий «шатёр»): узкий ствол <paramref name="half"/>,
        /// ширина по высоте — <see cref="PelagAbordageVfxRules.GeyserProfile"/> (раструб у земли,
        /// крона ×2,5 в верхних 30 %), шум вытянут вверх — вертикальные струи.
        /// </summary>
        public void Build(Mesh mesh, Vector3 foot, float height, Vector3 camera, float half,
            float age, float flow, float churn, float time, float alpha)
        {
            height = Mathf.Max(.05f, height);
            Vector3 view = camera - (foot + Vector3.up * height * .5f);
            view.y = 0f;
            Vector3 across = view.sqrMagnitude > 1e-6f ? new Vector3(view.z, 0f, -view.x).normalized : Vector3.right;
            Color32 color = Vertex(0f, alpha);
            // Низкий (растущий) столб — без кроны и раструба: они проявляются к 1,2 м.
            float grown = PelagAbordageVfxRules.Smooth01(height / 1.2f);
            // Купол шапки: середина выше краёв (низкий столб — почти плоский).
            float dome = Mathf.Min(.40f, half * 1.1f) * PelagAbordageVfxRules.Smooth01(height / .6f);
            for (int r = 0; r <= Rows; r++)
            {
                float k = r / (float)Rows;
                float h = height * k;
                float profile = 1f + (PelagAbordageVfxRules.GeyserProfile(k) - 1f) * grown;
                float hw = half * profile
                           * (1f + .07f * Mathf.Sin(h * 6.3f + _p1 - time * 13f) + .05f * Mathf.Sin(h * 11.7f + _p2 + time * 9f));
                // Крона (верхние 30 %) белеет пеной — цель сидит в шапке пены (кадр F);
                // раструб (нижние 14 %) — пена удара из земли, низ рваный, а не ровный срез.
                float crown = PelagAbordageVfxRules.Smooth01((k - .70f) / .30f) * grown;
                float burst = 1f - PelagAbordageVfxRules.Smooth01(k / .14f);
                float sway = .03f * Mathf.Sin(h * 2.1f + _p2 + time * 5f) * k;
                float outer = hw + Margin * (1f + .8f * crown);
                float fed = age > 0f ? age + k * .06f : 0f;
                float foam = churn * (.35f + .5f * k) + 1.2f * crown + 1.0f * burst;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    float off = y * outer;
                    float top = height + dome * (1f - y * y);
                    float hv = top * k;
                    int v = r * Columns + c;
                    Vertices[v] = across * (off + sway) + Vector3.up * hv;
                    Uv0[v] = new Vector4(off / hw, hw, fed, Mathf.Min(top - hv, hv + .03f));
                    Uv1[v] = new Vector4(hv * NoiseUp - flow, off * NoiseAcross, _seed, foam);
                    Colors[v] = color;
                }
            }
            Write(mesh);
        }
    }
}
