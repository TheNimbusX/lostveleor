using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ЗЕМЛЯ НЫРКА И ВЫХОДА (V14, владелец 04.10: «мне совершенно не нравится VFX закапывания под землю и
    /// выкапывания. именно земля. она странно выглядит, как кольцо какое-то… нужно прям красивый мощнейший такой эффект,
    /// чтобы земля была фактурная, а не гладкая плоская»). Чистые правила вида без UnityEngine (тесты —
    /// tools/Combat.Presentation.Tests/ThicketMasterEarthRulesTests.cs): сроки слоёв от тиков Sim, раскладка мест по
    /// контуру тела (рваная полоса с разрывами — не замкнутое кольцо), выброс вдоль корпуса, бюджет частиц и вспышка
    /// света выхода. Сборка — ThicketMasterVfxSetup (SaveDiveBurst, SaveEmergeBulge, SaveEmerge), вид —
    /// ThicketMasterCombatView.Vfx (OnDiveBurrow, OnDiveLocked, OnEmerge).
    ///
    /// Раньше (V9–V13): гладкий вал-тор LipMesh по контуру тела на фактуре TCom_Sand_Muddy2 (разброс яркости 5 из 255 —
    /// «пластилин») и пара симметричных декалей Crater40/Crater19 — это и было «кольцо». Теперь земля — из паков и
    /// своей арены: плиты дёрна (Cobble01–06 пака RPG Tiny Fantasy Forest с дёрном пола CampTurf_v3 сверху и каменистой
    /// землёй CampStonyEarth_v1 снизу), камни арены (MeadowPebble0/1 — Tripo-камень поляны), комья на каменистой земле
    /// (свои ClodMesh и NoiseSphere1 Hovl), зерно Debris2, пыль SmokeAnim2 (Hovl, 8 × 8 кадров), рваные пятна
    /// разрытой земли (dirt_0–5 и пятно 9e02a550 лагеря), трещины Crater43 / Crack4 / Crack6, крошка Crater18.
    ///
    /// V15 (владелец 07.10: «нырок и выход отличный. а движение под землей — нет. все еще плоский шарик земляной») — голова
    /// бугра стала каменистой кучей (RubbleSurface) с бороздой-навалом за ней. V17 (владелец 08.10: «всё ещё как будто
    /// холмик просто скользит по полу… без вау-эффекта пробуривания земли») — кучи нет: ничего жёсткого с головой не едет,
    /// земля ломается на месте по пути (раздел «ход под землёй»), за головой — рваная траншея (FurrowTrail: губы дёрна
    /// вверх, тёмная земля внутри, над головой вспучено — FurrowHeave), частота следа — по пройденным метрам на 7 м/с
    /// (TrailRateScale), UV атласа плит (AtlasUv), бюджет и пул бугра. Сборка — ThicketMasterVfxSetup.SaveMound, вид —
    /// FurrowTrail и PlaceVfx (Mound).
    /// </summary>
    public static class ThicketMasterEarthRules
    {
        // ---------------------------------------------------------------- контур тела

        /// <summary>
        /// Контур тела 4,14 м сверху (корпус Sim, § 8 контракта): полуоси вбок и вдоль взгляда, сдвиг центра вперёд, м.
        /// </summary>
        public const float BodyHalfWidth = 2.45f, BodyHalfLength = 2.9f, BodyShift = .35f;

        // ---------------------------------------------------------------- сроки (секунды от события)

        /// <summary>Уход в землю: тело уходит за ThicketDiveBurrowTicks (12 тиков, 0,4 с) — плиты валятся внутрь всё это время.</summary>
        public static float BurrowSeconds => Simulation.ThicketDiveBurrowTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Круг лёг → выход: ThicketDiveLockTicks (24 тика, 0,8 с) — «вздутие» у точки выхода идёт ровно столько.</summary>
        public static float BulgeSeconds => Simulation.ThicketDiveLockTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Выход: столько секунд из земли бьёт выброс (плиты, комья, камни), тело встаёт за ним.</summary>
        public const float EruptSeconds = .36f;

        /// <summary>Сколько живёт экземпляр: уход — 2,6 с (как было), выход — 3 с (как было), вздутие — до выхода.</summary>
        public const float DiveLife = 2.6f, EmergeLife = 3f;

        /// <summary>
        /// Пыль (ревью 02.10, вечер: «вата» закрывала героя и босса): верх клуба не выше DustMaxHeight над землёй, жизнь
        /// клуба не больше DustMaxLife, последняя пыль ухода гаснет к DiveDustClearSeconds — до того, как бугор отъедет.
        /// </summary>
        public const float DustMaxHeight = .9f, DustMaxLife = .9f;

        /// <summary>Пыль ухода: поток идёт весь уход (+0,1 с), клуб живёт ≤ 0,75 с.</summary>
        public const float DiveDustLife = .75f;

        public static float DiveDustClearSeconds => BurrowSeconds + .1f + DiveDustLife;

        /// <summary>Пыль выхода: ударная волна 0,6–0,85 с, низкие клубы у контура — первые EruptSeconds.</summary>
        public const float EmergeShockLife = .85f, EmergeLowDustLife = .7f;

        /// <summary>Обломки выхода лежат щебнем и уходят в землю к концу жизни (≤ EmergeLife).</summary>
        public const float RubbleLifeMin = 2.2f, RubbleLifeMax = 2.8f;

        // ---------------------------------------------------------------- бюджет (частиц на слой)

        public const int DivePatches = 7, DiveSlabs = 16, DiveClods = 80, DiveBigClods = 7, DiveRocks = 10, DiveGrains = 70,
            DiveDust = 24, DiveTufts = 10;

        public const int BulgeSlabs = 7, BulgeGrains = 18, BulgePebbles = 6;

        public const int EmergePatches = 7, EmergeSlabs = 20, EmergeRocksA = 10, EmergeRocksB = 8, EmergeClods = 115, EmergeBigClods = 10,
            EmergeGrains = 100, EmergeShock = 48, EmergeLowDust = 14, EmergeTufts = 12;

        /// <summary>
        /// Треугольников на меш (замер сборки пака, см. отчёт): плита Cobble LOD0 ≤ 192, камень арены MeadowPebble ≤ 192,
        /// ком ClodMesh 80, большой ком NoiseSphere1 320, пучок травы ~40. Пик выхода — все обломки разом в воздухе.
        /// </summary>
        public const int SlabTriangles = 192, RockTriangles = 192, ClodTriangles = 80, BigClodTriangles = 320, TuftTriangles = 40;

        public static int DiveTrianglesPeak => DiveSlabs * SlabTriangles + DiveRocks * RockTriangles + DiveClods * ClodTriangles
            + DiveBigClods * BigClodTriangles + DiveTufts * TuftTriangles;

        public static int EmergeTrianglesPeak => EmergeSlabs * SlabTriangles + (EmergeRocksA + EmergeRocksB) * RockTriangles
            + EmergeClods * ClodTriangles + EmergeBigClods * BigClodTriangles + EmergeTufts * TuftTriangles;

        /// <summary>Пулы вида: экземпляров на нырок/выход/вздутие (нырок «под героя» в фазах 2–3 — раз в 6 с от начала).</summary>
        public const int DivePool = 2, EmergePool = 2, BulgePool = 2;

        // ---------------------------------------------------------------- раскладка по контуру

        /// <summary>
        /// Место index из count на рваной полосе вокруг тела: угол (0 — нос тела, +Z) и доля радиуса эллипса контура
        /// [dMin, dMax]. Места идут дугами по 2–4 с разрывом между дугами (не замкнутое кольцо): полоса —
        /// рваные куски земли, а не вал по кругу.
        /// </summary>
        public static void ContourSpot(int index, int count, int salt, float dMin, float dMax, out float angle, out float d)
        {
            count = Math.Max(1, count);
            // Дуги: каждая 3-я позиция пропускается со сдвигом — между группами остаются разрывы ~1,5 шага.
            int slots = count + (count + 2) / 3;
            int slot = index + index / 3;
            float step = (float)(2.0 * Math.PI) / slots;
            float start = Hash01(salt, 11) * step * 3f;
            angle = start + (slot + (Hash01(index, salt) - .5f) * .55f) * step;
            d = dMin + (dMax - dMin) * Hash01(index, salt + 1);
        }

        /// <summary>
        /// Место выброса выхода index из count: внутри эллипса тела (доля радиуса [dMin, dMax]), углы тянутся к носу и
        /// хвосту корпуса (вдоль длины тела — тело рвёт землю вдоль себя), а не равномерно по кругу.
        /// </summary>
        public static void AlongBodySpot(int index, int count, int salt, float dMin, float dMax, out float angle, out float d)
        {
            count = Math.Max(1, count);
            float u = (index + Hash01(index, salt) * .9f) / count;
            float a = u * (float)(2.0 * Math.PI);
            // θ = a − k·sin 2a: dθ/da = 1 − 2k·cos 2a — у носа (0) и хвоста (π) шаг по углу в 6 раз мельче, места гуще.
            angle = a - .42f * (float)Math.Sin(2.0 * a);
            d = dMin + (dMax - dMin) * (float)Math.Sqrt(Hash01(index, salt + 1));
        }

        /// <summary>Точка на эллипсе контура тела: угол (0 — нос, +Z), доля d; x — вбок, z — вдоль взгляда (со сдвигом).</summary>
        public static void BodyPoint(float angle, float d, out float x, out float z)
        {
            x = (float)Math.Sin(angle) * BodyHalfWidth * d;
            z = (float)Math.Cos(angle) * BodyHalfLength * d + BodyShift;
        }

        /// <summary>Наружная нормаль эллипса контура в точке угла angle (единичная, в плоскости земли).</summary>
        public static void BodyNormal(float angle, out float nx, out float nz)
        {
            float x = (float)Math.Sin(angle) / BodyHalfWidth, z = (float)Math.Cos(angle) / BodyHalfLength;
            float l = (float)Math.Sqrt(x * x + z * z);
            nx = x / l; nz = z / l;
        }

        /// <summary>Самый большой разрыв между соседними местами по углу, радианы (для проверки «не кольцо»).</summary>
        public static float LargestGap(float[] angles)
        {
            if (angles == null || angles.Length < 2) return (float)(2.0 * Math.PI);
            var a = new float[angles.Length];
            for (int i = 0; i < a.Length; i++)
            {
                double w = angles[i] % (2.0 * Math.PI);
                a[i] = (float)(w < 0 ? w + 2.0 * Math.PI : w);
            }
            Array.Sort(a);
            float gap = a[0] + (float)(2.0 * Math.PI) - a[a.Length - 1];
            for (int i = 1; i < a.Length; i++) gap = Math.Max(gap, a[i] - a[i - 1]);
            return gap;
        }

        // ---------------------------------------------------------------- свет выхода

        /// <summary>
        /// Короткая янтарная вспышка из ямы на выходе (тёмная арена — обломки ловят свет): доля пика по возрасту, с.
        /// Встаёт за 0,04 с, гаснет к EruptLightSeconds; героя не высветляет (Razlom/Texture Toon берёт только главный свет).
        /// </summary>
        public const float EruptLightRise = .04f, EruptLightSeconds = .38f;

        public static float EruptLight(float age)
        {
            if (age <= 0f || age >= EruptLightSeconds) return 0f;
            if (age < EruptLightRise) return age / EruptLightRise;
            float t = (age - EruptLightRise) / (EruptLightSeconds - EruptLightRise);
            return (1f - t) * (1f - t);
        }

        // ---------------------------------------------------------------- ход под землёй — земля рвётся на месте (V17)

        // Владелец 08.10: «само перемещение под землёй оч быстрое и всё ещё как будто холмик просто скользит по полу. без
        // какой-то фактуры и вау-эффекта пробуривания земли». Ход замедлил Sim (§ 17.1: 7 м/с, 18–60 тиков). Вид: жёсткой
        // кучи головы V15 (каменистая сетка 2,9 × 4,1 × 0,9 м, тёмное пятно под ней, плиты на её плечах), которая ехала с
        // бугром, больше нет — с головой не едет ничего, что несёт облик. Земля ломается НА МЕСТЕ по пути (частицы — в мире,
        // там, где голова была): из пола вырываются плиты дёрна и камни и падают туда, где встали; носовая волна земли — в
        // обе стороны; фонтан комьев и зерна; трещины разбегаются от головы; низкий короткий занавес пыли; впереди пол
        // «ходит» — плиты в конусе перед головой чуть приподнимаются и уходят обратно. За головой — рваная траншея
        // (FurrowTrail): губы дёрна вывернуты вверх, внутри тёмная земля комьями, над самой головой земля вспучена
        // (FurrowHeave); на губах — вздыбленные плиты; всё оседает и уходит. Голову на тёмной арене видно по углю трещин у
        // неё, без пятен-ореолов. Пока бугор стоит (круг лёг, Песочные Часы), у головы земля только «кипит» (Still …).

        /// <summary>
        /// Где у головы рвётся земля (места частиц, м; +Z — ход): полуширина, вперёд и назад от точки бугра. Тело 4,14 м под
        /// землёй — разлом шириной ~2 м, как траншея (FurrowHalfWidth).
        /// </summary>
        public const float HeadHalfWidth = 1f, HeadAhead = .7f, HeadBehind = .5f;

        /// <summary>
        /// Плиты и камни, вырванные у головы: скорость вверх, м/с, доля наклона наружу (горизонталь к вертикали) и тяжесть
        /// (×g) — падают туда, где встали (BurstDrift ≤ HeadHalfWidth); жизнь, с — взлёт ≤ ~0,6 с, лежат и уходят в землю.
        /// </summary>
        public const float BurstSpeedMin = 2.6f, BurstSpeedMax = 4.2f, BurstLean = .35f, BurstGravity = 1.5f;

        public const float BurstLifeMin = 1.2f, BurstLifeMax = 1.6f;

        /// <summary>Наибольший снос плиты от места, где она вырвалась, м (баллистика: v² · sin 2θ / g).</summary>
        public static float BurstDrift
        {
            get
            {
                double theta = Math.Atan(BurstLean);
                return (float)(BurstSpeedMax * BurstSpeedMax * Math.Sin(2.0 * theta) / (Gravity * BurstGravity));
            }
        }

        /// <summary>Наибольший взлёт середины плиты, м (v² · cos² θ / 2g).</summary>
        public static float BurstApex
        {
            get
            {
                double c = Math.Cos(Math.Atan(BurstLean));
                return (float)(BurstSpeedMax * BurstSpeedMax * c * c / (2.0 * Gravity * BurstGravity));
            }
        }

        /// <summary>Плиты на губах траншеи: встают за ~0,2 с, лежат вздыбленными, оседают и к концу жизни уходят в землю, с.</summary>
        public const float LipSlabLifeMin = 1.5f, LipSlabLifeMax = 1.8f;

        /// <summary>Наклон плит на губах, рад (вздыблены, не стоят торчком и не лежат плашмя).</summary>
        public const float LipSlabTiltMin = .55f, LipSlabTiltMax = 1.05f;

        /// <summary>
        /// Рябь впереди (тяжёлый гул): конус от RippleNear до RippleFar м перед головой, полуширина у ближнего края
        /// RippleNearHalf, у дальнего — RippleFarHalf. Плита ряби лежит на RippleDepth м под полом (середина), подскакивает
        /// RippleSpeedMin…Max м/с при тяжести RippleGravity и уходит обратно под пол за RippleLifeMin…Max с — верх встаёт над
        /// полом на несколько сантиметров (RipplePeak ≤ RippleLift). На 7 м/с голова доходит до ряби за 0,14–0,4 с и рвёт
        /// уже «раскачанную» землю.
        /// </summary>
        public const float RippleNear = 1f, RippleFar = 2.8f, RippleNearHalf = .3f, RippleFarHalf = 1.1f;

        public const float RippleDepth = .07f, RippleSpeedMin = .9f, RippleSpeedMax = 1.5f, RippleGravity = 1f, RippleLift = .12f;

        public const float RippleLifeMin = .32f, RippleLifeMax = .42f;

        /// <summary>Наибольший подъём середины плиты ряби над полом, м: v² / 2g − глубина.</summary>
        public static float RipplePeak => RippleSpeedMax * RippleSpeedMax / (2f * Gravity * RippleGravity) - RippleDepth;

        /// <summary>Ускорение свободного падения Unity по умолчанию, м/с² (gravityModifier частиц — доли его).</summary>
        public const float Gravity = 9.81f;

        /// <summary>
        /// След пускается по ходу (вид гасит его, пока бугор стоит, и пускает «кипение» Still …) с частотой префаба на ходу
        /// MoundRefSpeed — ход Sim бугра (ThicketMoundSpeed, 7 м/с); на другом ходу — по пройденным метрам: множитель
        /// частоты = ход / MoundRefSpeed в [TrailRateMin; TrailRateMax] (догон убегающего — до 12 м/с, ThicketMoundMaxStep).
        /// Пол 0,15 — ход ~1 м/с; ниже — путь короче полуметра, частиц на нём единицы (бюджет держат maxParticles).
        /// </summary>
        public const float MoundRefSpeed = 7f, TrailRateMin = .15f, TrailRateMax = 1.75f;

        public static float TrailRateScale(float speed)
        {
            if (!(speed > 0f)) return TrailRateMin;
            return Math.Max(TrailRateMin, Math.Min(TrailRateMax, speed / MoundRefSpeed));
        }

        /// <summary>Самая долгая жизнь частиц следа, с (плиты на губах): после выхода бугор доживает столько.</summary>
        public const float MoundTrailMaxLife = LipSlabLifeMax;

        /// <summary>
        /// Бюджет бугра (наибольшее число частиц в системе; частота — на MoundRefSpeed). Head — всегда (уголь трещин и
        /// угольки у головы), Trail — на ходу, Still — пока стоит.
        /// </summary>
        public const int MoundHeadGlow = 6, MoundHeadEmbers = 10, MoundBurstSlabs = 36, MoundBigSlabs = 6, MoundRocksA = 10, MoundRocksB = 6,
            MoundFountain = 8, MoundClods = 30, MoundBow = 34, MoundGrains = 48, MoundDust = 8, MoundLipSlabs = 26, MoundRipple = 10,
            MoundRippleGrains = 12, MoundCracks = 12, MoundPatches = 14, MoundGrass = 8, MoundBoil = 6, MoundBoilGrains = 12;

        /// <summary>Частиц бугра разом, наибольшее.</summary>
        public static int MoundParticlesPeak => MoundHeadGlow + MoundHeadEmbers + MoundBurstSlabs + MoundBigSlabs + MoundRocksA + MoundRocksB
            + MoundFountain + MoundClods + MoundBow + MoundGrains + MoundDust + MoundLipSlabs + MoundRipple + MoundRippleGrains + MoundCracks
            + MoundPatches + MoundGrass + MoundBoil + MoundBoilGrains;

        /// <summary>Треугольников бугра, если всё разом в воздухе и на земле (плиты, камни, комья, трава, траншея).</summary>
        public static int MoundTrianglesPeak => (MoundBurstSlabs + MoundBigSlabs + MoundLipSlabs + MoundRipple + MoundBoil) * SlabTriangles
            + (MoundRocksA + MoundRocksB) * RockTriangles + MoundFountain * BigClodTriangles + (MoundClods + MoundBow) * ClodTriangles
            + MoundGrass * TuftTriangles + FurrowTriangles;

        /// <summary>
        /// Пул бугра: экземпляр живёт от хода (T+12) до выхода (≤ T+12+60+24) и ещё MoundTrailMaxLife; нырок «под героя» в
        /// фазах 2–3 — раз в 6 с от начала, два экземпляра — новый нырок не рвёт хвост прошлого.
        /// </summary>
        public const int MoundPool = 2;

        public static float MoundLifeSeconds => (Simulation.ThicketDiveTravelMaxTicks + Simulation.ThicketDiveLockTicks) / (float)Simulation.TicksPerSecond
            + MoundTrailMaxLife;

        // ---------------------------------------------------------------- траншея (FurrowTrail, V17)

        /// <summary>
        /// Точка траншеи: встаёт за 0,08 с, держится до FurrowHold, к FurrowLife − 0,05 — в земле. На 7 м/с траншея за головой
        /// ~11 м — ход виден целиком, как и плиты на губах (LipSlabLife).
        /// </summary>
        public const float FurrowLife = 1.6f, FurrowHold = .55f;

        /// <summary>Полуширина и высота губ траншеи, м (тело 4,14 м: траншея ~2,1 м, губы ~0,4 м).</summary>
        public const float FurrowHalfWidth = 1.05f, FurrowHeight = .4f;

        /// <summary>Наибольшее число точек ленты (вид, FurrowTrail.MaxSamples) — на треугольники бюджета.</summary>
        public const int FurrowMaxSamples = 96;

        /// <summary>Треугольников ленты траншеи: (точки − 1) × четырёхугольники поперёк без швов × 2.</summary>
        public static int FurrowTriangles
        {
            get
            {
                int quads = 0;
                for (int c = 0; c < FurrowColumns - 1; c++) if (!FurrowSeam(c)) quads++;
                return (FurrowMaxSamples - 1) * quads * 2;
            }
        }

        /// <summary>
        /// Поперёк ленты 15 точек слева направо: 0 юбка, 1 край, 2 бок дёрна, 3 рваная губа дёрна | 4 срез (та же точка, но
        /// уже каменистая земля — у атласа свои половины, треугольник между 3 и 4 не строится), 5 желоб у губы, 6 земля,
        /// 7 середина, 8–14 — зеркально. Дёрн — 0–3 и 11–14, земля — 4–10.
        /// </summary>
        public const int FurrowColumns = 15;

        public static bool FurrowSoil(int c) => c >= 4 && c <= 10;

        /// <summary>Шов дёрн | земля: четырёхугольник между столбцами c и c + 1 не строится (нулевой, а UV — с разных половин атласа).</summary>
        public static bool FurrowSeam(int c) => c == 3 || c == 10;

        /// <summary>Линия разрыва дёрна (доля полуширины) по пройденному пути s, м: рваная, у каждого бока своя; траншея ~1,1 м в просвете.</summary>
        public static float FurrowTear(float s, bool right)
        {
            int salt = right ? 71 : 67;
            return .55f + .07f * Wave(s * 1.7f, salt) + .03f * Wave(s * 4.3f, salt + 2);
        }

        /// <summary>Поперечная доля полуширины столбца c (−1,12 … 1,12) при линиях разрыва tearLeft / tearRight.</summary>
        public static float FurrowAcross(int c, float tearLeft, float tearRight)
        {
            switch (c)
            {
                case 0: return -1.12f;
                case 1: return -1f;
                case 2: return -.8f;
                case 3: case 4: return -tearLeft;
                case 5: return -tearLeft + .08f;
                case 6: return -tearLeft * .5f;
                case 7: return 0f;
                case 8: return tearRight * .5f;
                case 9: return tearRight - .08f;
                case 10: case 11: return tearRight;
                case 12: return .8f;
                case 13: return 1f;
                default: return 1.12f;
            }
        }

        /// <summary>
        /// Высота столбца c в долях FurrowHeight за головой (юбка — под землёй, её ставит вид): губы дёрна вывернуты вверх
        /// выше всего, за губой — желоб, внутри — низкая тёмная земля комьями: траншея, а не вал.
        /// </summary>
        public static float FurrowBody(int c)
        {
            switch (c)
            {
                case 2: case 12: return .42f;
                case 3: case 4: case 10: case 11: return 1f;
                case 5: case 9: return .45f;
                case 6: case 8: return .32f;
                case 7: return .24f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Прибавка столбца c над головой (× FurrowHeave): земля в середине вспучена почти до губ — тело под ней рвёт пол, —
        /// губы чуть выше; за головой опадает в траншею.
        /// </summary>
        public static float FurrowHeadExtra(int c)
        {
            switch (c)
            {
                case 2: case 12: return .08f;
                case 3: case 4: case 10: case 11: return .2f;
                case 5: case 9: return .45f;
                case 6: case 8: return .65f;
                case 7: return .75f;
                default: return 0f;
            }
        }

        /// <summary>Вспучивание над головой: растёт до 1 за FurrowHeaveRise м позади головы и опадает за FurrowHeaveFall м.</summary>
        public const float FurrowHeaveRise = .45f, FurrowHeaveFall = 1.4f;

        /// <summary>Доля вспучивания точки траншеи в behind м позади головы (по пройденному пути).</summary>
        public static float FurrowHeave(float behind)
        {
            if (!(behind > 0f)) return 0f;
            return Smooth(behind / FurrowHeaveRise) * (1f - Smooth((behind - FurrowHeaveRise) / FurrowHeaveFall));
        }

        /// <summary>Подъём точки траншеи по её возрасту a, с: встаёт за 0,08 с с перелётом, держится до FurrowHold, к FurrowLife — в земле.</summary>
        public static float FurrowLift(float a)
        {
            if (a <= 0f) return 0f;
            float rise = a < .08f ? FurrowOvershoot * Smooth(a / .08f)
                : a < .14f ? FurrowOvershoot - (FurrowOvershoot - 1f) * Smooth((a - .08f) / .06f) : 1f;
            float fall = FurrowFall(a);
            return rise * (1f - fall) * (1f - fall);
        }

        /// <summary>Осыпание точки 0…1 по возрасту: с FurrowHold к FurrowLife − 0,05.</summary>
        public static float FurrowFall(float a) => Smooth((a - FurrowHold) / (FurrowLife - .05f - FurrowHold));

        /// <summary>
        /// Земля внутри траншеи (вид, FurrowTrail.Build): множитель heap = база ± размах шума по пути, комья поперёк —
        /// ± FurrowCrumbSwing (губы дёрна — ±0,1). Наибольшая высота ленты — FurrowPeak.
        /// </summary>
        public const float FurrowHeapBase = .75f, FurrowHeapSwing = .45f, FurrowCrumbSwing = .3f;

        /// <summary>Перелёт подъёма траншеи (FurrowLift в 0,08 с).</summary>
        public const float FurrowOvershoot = 1.08f;

        /// <summary>
        /// Самый грубый множитель столбца c: земля внутри (5–9) — навал с комьями; дёрн и срез на шве (4, 10 — та же точка,
        /// что губа) — ровнее.
        /// </summary>
        public static float FurrowRoughMax(int c) => c >= 5 && c <= 9
            ? (FurrowHeapBase + FurrowHeapSwing) * (1f + FurrowCrumbSwing) : 1.1f;

        /// <summary>Самая высокая точка траншеи, м: над головой, с наибольшим навалом, комьями и перелётом.</summary>
        public static float FurrowPeak
        {
            get
            {
                float top = 0f;
                for (int c = 0; c < FurrowColumns; c++)
                    top = Math.Max(top, (FurrowBody(c) + FurrowHeadExtra(c)) * FurrowRoughMax(c));
                return FurrowHeight * top * FurrowOvershoot;
            }
        }

        /// <summary>
        /// UV атласа плит (M_Thicket_Turf: слева дёрн пола, справа каменистая земля) для точки земли x, z, м: дёрн — плиткой
        /// пола 5,2 м, земля — 0,3 текстуры на метр (как бока плит). Атлас без повтора — зеркальная «пила» (без скачка UV
        /// внутри треугольника), с отступом от края клетки.
        /// </summary>
        public static void AtlasUv(bool soil, float x, float z, out float u, out float v)
        {
            if (soil)
            {
                u = .5f + .5f * Inset(PingPong(x * .3f + .07f));
                v = Inset(PingPong(z * .3f + .29f));
            }
            else
            {
                u = .5f * Inset(PingPong(x / 5.2f + .13f));
                v = Inset(PingPong(z / 5.2f + .41f));
            }
        }

        /// <summary>Зеркальная «пила» 0…1…0 с периодом 2.</summary>
        public static float PingPong(float t)
        {
            float m = t - 2f * (float)Math.Floor(t / 2f);
            return m <= 1f ? m : 2f - m;
        }

        private static float Inset(float t) => .02f + .96f * t;

        private static float Smooth(float x)
        {
            x = Math.Max(0f, Math.Min(1f, x));
            return x * x * (3f - 2f * x);
        }

        /// <summary>Гладкий шум −1…1 по одной оси (решётка с хешем) — рваные края без «плывущих» точек.</summary>
        public static float Wave(float x, int salt)
        {
            int i = (int)Math.Floor(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            float a = Hash01(i, salt) * 2f - 1f, b = Hash01(i + 1, salt) * 2f - 1f;
            return a + (b - a) * f;
        }

        // ---------------------------------------------------------------- служебное

        /// <summary>Детерминированный разброс (тот же, что Hash01 сборщика): сборка даёт одинаковые ассеты на любой машине.</summary>
        public static float Hash01(int i, int salt)
        {
            uint h = unchecked((uint)(i * 747796405 + salt * 2891336453u));
            h = unchecked(((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u);
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
