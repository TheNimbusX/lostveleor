using System;

namespace Game.View
{
    /// <summary>
    /// Числа вида Крушения «холодное железо» (база, 06.10) без Unity — проверяются тестами
    /// представления (tools/Combat.Presentation.Tests/WreckIronRulesTests.cs). Целевой кадр —
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/base-v1.webp, но в числах игры
    /// (на кадре полоса втрое длиннее): круг удара ImpactRadius вокруг ImpactPoint, полоса
    /// шириной 2 × LaneHalfWidth от WaveStart до WallEnd. Видимый край = край урона Sim
    /// (правило владельца 02.10): камни круга не выходят за ImpactRadius, камни и звенья полосы —
    /// за её полуширину и конец; кусок полосы встаёт в тик показа, когда до него дошёл фронт
    /// (тот же счёт, что PelagWreckVfxRules.WaveFront). Каждый кусок лежит HoldSeconds,
    /// потом за FadeSeconds оседает в землю и гаснет. Время — тик показа sim.Tick − 2 + Alpha.
    /// </summary>
    public static class PelagWreckIronRules
    {
        // ---------------------------------------------------------------- время куска

        /// <summary>Выход из земли, с; лежит после выхода, с; оседает и гаснет, с; остывание жара (белый → голубой), с.</summary>
        public const float RiseSeconds = .08f, HoldSeconds = 1.0f, FadeSeconds = .35f, CoolSeconds = .22f;

        /// <summary>Перелёт выхода из земли: доля высоты сверх покоя на пике.</summary>
        public const float RiseOvershoot = .28f;

        public static float Seconds(float ticks) => ticks / Game.Sim.Simulation.TicksPerSecond;

        public static float Smooth01(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }

        /// <summary>Выход из земли: 0 — под землёй, 1 — в покое; на подъёме короткий перелёт (камень выбило ударом).</summary>
        public static float Rise(float local)
        {
            if (local <= 0f) return 0f;
            float u = local / RiseSeconds;
            if (u >= 1.6f) return 1f;
            if (u <= 1f) return (1f + RiseOvershoot) * (1f - (1f - u) * (1f - u));
            return 1f + RiseOvershoot * (1f - Smooth01((u - 1f) / .6f));
        }

        /// <summary>Оседание: 0 — лежит, 1 — ушёл в землю и погас.</summary>
        public static float Sink(float local) => Smooth01((local - HoldSeconds) / FadeSeconds);

        /// <summary>Жар трещин и звеньев: 1 в миг появления, остывает экспонентой.</summary>
        public static float Heat(float local) => local <= 0f ? 0f : (float)Math.Exp(-local / CoolSeconds);

        /// <summary>Свечение звена (HDR-множитель): вспышка в миг впечатывания, ровный голубой, гаснет с оседанием.</summary>
        public static float LinkGlow(float local)
        {
            if (local <= 0f) return 0f;
            float steady = .85f - .35f * Smooth01((local - .3f) / (HoldSeconds - .3f));
            return (steady + 1.4f * Heat(local)) * (1f - Sink(local));
        }

        /// <summary>
        /// Свечение подошвы камня из трещины: короткий жар в миг выхода, дальше едва заметно — камень
        /// остаётся бурым/серым (V5: голубые камни читались галькой, свет — в трещинах земли).
        /// </summary>
        public static float StoneGlow(float local)
        {
            if (local <= 0f) return 0f;
            return (.08f + .8f * Heat(local)) * (1f - Sink(local));
        }

        /// <summary>Сколько живёт объект удара оземь после удара, с: ход фронта + жизнь последнего куска + запас.</summary>
        public static float LifeSeconds(int travelTicks) => Seconds(Math.Max(1, travelTicks)) + HoldSeconds + FadeSeconds + .15f;

        // ---------------------------------------------------------------- когда кусок встаёт

        /// <summary>
        /// Тик показа, когда фронт дошёл до точки полосы (вдоль от LaneOrigin): шаг k (тик WaveTick + k − 1)
        /// бьёт до start + k·step, между тиками — линейно, как PelagWreckVfxRules.WaveFront. Точки не дальше
        /// первого шага встают сразу с ударом.
        /// </summary>
        public static float LaneArrivalTick(float along, int waveTick, float start, float step)
            => waveTick - 1f + Math.Max(0f, along - start) / Math.Max(1e-3f, step);

        /// <summary>
        /// Тик показа, когда кольцо круга удара дошло до радиуса <paramref name="r"/> (обратное к
        /// PelagWreckVfxRules.CraterCrest: x + 0,7·x·(1 − x) = r / R).
        /// </summary>
        public static float CraterArrivalTick(float r, int slamTick, float radius)
        {
            float q = radius > 1e-3f ? Math.Max(0f, Math.Min(1f, r / radius)) : 1f;
            float e = Game.View.PelagWreckVfxRules.CraterEase;
            // e·x² − (1 + e)·x + q = 0, меньший корень.
            float b = 1f + e, disc = b * b - 4f * e * q;
            float x = (b - (float)Math.Sqrt(Math.Max(0f, disc))) / (2f * e);
            return slamTick - .5f + Game.View.PelagWreckVfxRules.CraterOpenTicks * x;
        }

        // ---------------------------------------------------------------- звенья в полосе

        /// <summary>Звено: длина, шаг вдоль полосы (звенья цепи заходят друг в друга), центр первого — за точкой удара, м.</summary>
        public const float LinkLength = 1.25f, LinkPitch = .84f, LinkFirst = .5f;

        /// <summary>Ширина звена — доля длины (свой меш WreckIronLink: овал 1 × 0,52, прут 0,15).</summary>
        public const float LinkWidthOfLength = .52f;

        /// <summary>Радиус прута — доля длины звена (меш WreckIronLink; паз под звеном в шейдере земли).</summary>
        public const float LinkTubeOfLength = .075f;

        public const int MaxLinks = 4;

        /// <summary>Радиус прута звена в игре, м.</summary>
        public static float LinkTube => LinkLength * LinkTubeOfLength;

        /// <summary>
        /// Высота центра звена над землёй, м (V6 — звено вдавлено в паз, не лежит на траве): плашмя —
        /// прут утоплен чуть больше чем наполовину; ребром — над землёй только верхний прут (брусок).
        /// </summary>
        public static float LinkLift(bool edge)
            => edge ? -(LinkLength * LinkWidthOfLength * .5f - LinkTube * 1.3f) : -LinkTube * .15f;

        /// <summary>
        /// Осевая линия звена в плане (метры игры): полукружия радиуса A с центрами на ±C вдоль звена,
        /// прямые прутья на ±A поперёк — паз и свет из щели в шейдере земли идут по ней.
        /// </summary>
        public static float LinkAxisRadius => LinkLength * (LinkWidthOfLength * .5f - LinkTubeOfLength);
        public static float LinkAxisHalfStraight => LinkLength * (.5f - LinkTubeOfLength) - LinkAxisRadius;

        /// <summary>Сколько звеньев ляжет в полосу до её конца (дальний край последнего — не дальше конца).</summary>
        public static int LinkCount(float start, float end)
        {
            float room = end - start - LinkFirst - LinkLength * .5f;
            if (room < 0f) return 0;
            return Math.Min(MaxLinks, 1 + (int)Math.Floor(room / LinkPitch + 1e-4f));
        }

        public static float LinkAlong(int index, float start) => start + LinkFirst + index * LinkPitch;

        /// <summary>Звенья лежат как цепь: плашмя и ребром через одно (нечётные — ребром).</summary>
        public static bool LinkOnEdge(int index) => index % 2 == 1;

        /// <summary>Звено впечатывается, когда фронт дошёл до его середины.</summary>
        public static float LinkArrivalTick(int index, int waveTick, float start, float step)
            => LaneArrivalTick(LinkAlong(index, start), waveTick, start, step);

        // ---------------------------------------------------------------- камни

        /// <summary>Детерминированное «случайное» 0…1 по номеру удара и куска (вид одинаков в каждом показе).</summary>
        public static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + 0x9E3779B9u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// Вид куска (V6): глыба-блок (гранёная, коренастая), плита-осколок (торчком, наклон наружу — их
        /// большинство, как кольцо плит вокруг кратера и вдоль полосы в base-v1), ком земли (тёмный, плоский).
        /// </summary>
        public const byte LookBoulder = 0, LookSlab = 1, LookClod = 2;

        /// <summary>Камень: размер (м, наибольшая ось), где лежит и как наклонён наружу, когда встаёт.</summary>
        public struct Stone
        {
            /// <summary>Вдоль полосы от LaneOrigin и поперёк (влево +), м; у камней круга — от центра удара по углу.</summary>
            public float Along, Across, Size, Tilt, Spin, ArrivalTick;
            /// <summary>Наружу: поперёк полосы (±1) или радиально (круг); 0 — лежит плашмя.</summary>
            public int Side;
            public bool Iron, Crater;
            /// <summary>LookBoulder / LookSlab / LookClod (у железа не используется).</summary>
            public byte Look;
        }

        /// <summary>
        /// Камни круга (V7 — разбор владельца 06.10 «форма слишком чёткая»: V6 — ровное кольцо плит одного
        /// размера): кромка — неровные кучки (крупная плита, круто вывернутая наружу, и 1–4 куска помельче
        /// рядом, кто как лёг) с пустыми промежутками разбитой земли между кучками; внутри — щебень,
        /// комья и пара железных осколков вразброс.
        /// </summary>
        public const int CraterRimStones = 17, CraterInnerStones = 10;

        /// <summary>Кучек на кромке круга (неравные, между ними — промежутки).</summary>
        public const int RimClusters = 5;

        /// <summary>Кромка круга не ставится перед выходом полосы: ±столько градусов от её направления.</summary>
        public const float RimGapDegrees = 22f;

        /// <summary>Крупная плита кучки, м; мелочь рядом с ней, м (до масштаба круга).</summary>
        public const float RimSizeMin = .5f, RimSizeMax = .86f, RimSmallMin = .14f, RimSmallMax = .42f;

        /// <summary>Наклон наружу по виду куска, град: блок, плита, ком.</summary>
        public static float TiltFor(byte look, float h)
            => look == LookSlab ? Lerp(24f, 48f, h) : look == LookClod ? Lerp(4f, 18f, h) : Lerp(10f, 28f, h);

        /// <summary>Вид куска по случайному h: плита до slabUpTo, блок до boulderUpTo, дальше ком земли.</summary>
        public static byte PickLook(float h, float slabUpTo, float boulderUpTo)
            => h < slabUpTo ? LookSlab : h < boulderUpTo ? LookBoulder : LookClod;

        /// <summary>
        /// Камни круга удара: наружный край каждого — не дальше <paramref name="radius"/> от центра
        /// (видимый край = край урона), перед выходом полосы (угол 0 — вдоль полосы) кромки нет.
        /// Along/Across — смещение от центра удара вдоль и поперёк полосы; Side −1 — кусок клонится внутрь.
        /// </summary>
        public static int CraterStones(int serial, float radius, int slamTick, Stone[] into, int at)
        {
            int n = at, placed = 0;
            float scale = Math.Min(1f, radius / 1.2f);
            float span = 360f - 2f * RimGapDegrees;
            for (int c = 0; c < RimClusters && n < into.Length; c++)
            {
                // Центры кучек — с разбросом ±35 % своего шага; кусков в кучке 1…5, всего — CraterRimStones.
                float centre = RimGapDegrees + span * (c + .5f + (Hash01(serial, 10 + c) - .5f) * .7f) / RimClusters;
                int left = CraterRimStones - placed, clustersLeft = RimClusters - c;
                int count = clustersLeft == 1 ? left
                    : Math.Max(1, Math.Min(left - (clustersLeft - 1), (int)Math.Round(left / (float)clustersLeft + (Hash01(serial, 15 + c) - .5f) * 3f)));
                for (int j = 0; j < count && n < into.Length; j++, placed++)
                {
                    int id = placed;
                    bool big = j == 0;
                    float hs = Hash01(serial, 30 + id);
                    float size = (big ? Lerp(RimSizeMin, RimSizeMax, hs) : Lerp(RimSmallMin, RimSmallMax, hs * hs)) * scale;
                    // Мелочь — по дуге то с одной, то с другой стороны плиты и глубже внутрь круга.
                    float spread = big ? (Hash01(serial, 40 + id) - .5f) * 8f
                        : (j % 2 == 1 ? 1f : -1f) * Lerp(8f, 24f, Hash01(serial, 40 + id));
                    float deg = Math.Max(RimGapDegrees, Math.Min(360f - RimGapDegrees, centre + spread));
                    float inward = big ? Lerp(0f, .08f, Hash01(serial, 60 + id)) : Lerp(.04f, .3f, Hash01(serial, 60 + id));
                    float r = Math.Max(.2f, radius * (1f - inward) - size * .5f);
                    byte look = big ? LookSlab : PickLook(Hash01(serial, 20 + id), .45f, .8f);
                    float th = Hash01(serial, 50 + id);
                    float rad = deg * (float)Math.PI / 180f;
                    into[n++] = new Stone
                    {
                        Along = r * (float)Math.Cos(rad), Across = r * (float)Math.Sin(rad), Size = size,
                        // Плита вывернута наружу круто; мелочь — как легла: больше наружу, часть внутрь.
                        Tilt = big ? Lerp(34f, 60f, th) : TiltFor(look, th),
                        Spin = 360f * Hash01(serial, 70 + id), Side = big || Hash01(serial, 80 + id) < .65f ? 1 : -1,
                        Crater = true, Look = look, ArrivalTick = CraterArrivalTick(r, slamTick, radius)
                    };
                }
            }
            for (int i = 0; i < CraterInnerStones && n < into.Length; i++)
            {
                // Внутри круга вразброс, но не на оси полосы: там лежит первое звено; середина — якорь, свободна.
                float deg = RimGapDegrees + span * Hash01(serial, 90 + i);
                float rad = deg * (float)Math.PI / 180f;
                float r = radius * Lerp(.3f, .66f, Hash01(serial, 110 + i));
                bool iron = i % 4 == 1;
                byte look = Hash01(serial, 120 + i) < .5f ? LookBoulder : LookClod;
                float h = Hash01(serial, 130 + i);
                float size = Lerp(.12f, .36f, h * h) * scale;
                into[n++] = new Stone
                {
                    Along = r * (float)Math.Cos(rad), Across = r * (float)Math.Sin(rad), Size = size,
                    Tilt = TiltFor(iron ? LookSlab : look, Hash01(serial, 150 + i)), Spin = 360f * Hash01(serial, 170 + i),
                    Side = Hash01(serial, 180 + i) < .7f ? 1 : -1, Crater = true, Iron = iron, Look = look,
                    ArrivalTick = CraterArrivalTick(r, slamTick, radius)
                };
            }
            return n - at;
        }

        /// <summary>
        /// Край полосы (V7): шаг кучек вдоль, м — неровный, с редкими пустыми промежутками; край ходит
        /// от узкого (доля полуширины) к широкому плавным шумом ячейками ~0,8 м — полоса не прямоугольник.
        /// </summary>
        public const float LaneStepMin = .12f, LaneStepMax = .4f, LaneGap = .45f, LaneGapChance = .1f;
        public const float LaneEdgeNarrow = .7f, LaneEdgeWide = 1f;

        /// <summary>Куски края полосы, м: от мелочи до крупной плиты (размер с перекосом, не ряд одинаковых).</summary>
        public const float LaneSizeMin = .16f, LaneSizeMax = .7f;

        /// <summary>Щебень внутри полосы между краем и звеньями: шаг вдоль, м; размер, м.</summary>
        public const float LaneRubbleSpacing = .34f, LaneRubbleMin = .14f, LaneRubbleMax = .27f;

        /// <summary>Каждый такой по счёту кусок края полосы — железный осколок, а не камень (железа немного).</summary>
        public const int IronEvery = 6;

        /// <summary>Неровный край полосы: доля полуширины в точке along со стороны side (плавный шум ячейками 0,8 м).</summary>
        public static float LaneEdge(int serial, int side, float along)
        {
            float x = Math.Max(0f, along) / .8f;
            int cell = (int)Math.Floor(x);
            float f = Smooth01(x - cell);
            int salt = side > 0 ? 700 : 760;
            float a = Hash01(serial, salt + (cell & 31)), b = Hash01(serial, salt + ((cell + 1) & 31));
            return Lerp(LaneEdgeNarrow, LaneEdgeWide, Lerp(a, b, f));
        }

        /// <summary>
        /// Камни и железные осколки по краям полосы от start до end: наружный край — не дальше полуширины
        /// (видимый край = край урона), встают, когда до них дошёл фронт. V7: по каждой стороне — неровные
        /// кучки (кусок разного размера, рядом иногда мелочь) с неровным шагом и пустыми промежутками;
        /// крупные — плиты, круто вывернутые наружу (землю выперло), мелочь лежит как легла; край ходит
        /// шумом (LaneEdge). Внутри полосы — щебень между краем и звеньями (звенья не закрываются).
        /// </summary>
        public static int LaneStones(int serial, float start, float end, float halfWidth, int waveTick, float step, Stone[] into, int at)
        {
            int n = at, id = 0;
            float scale = Math.Min(1f, halfWidth / .75f);
            float linkHalf = LinkLength * LinkWidthOfLength * .5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float along = start + .12f + .3f * Hash01(serial, 600 + s);
                while (along < end - .1f && n < into.Length)
                {
                    float hs = Hash01(serial, 200 + id), hg = Hash01(serial, 220 + id);
                    float size = (hg < .36f ? Lerp(.46f, LaneSizeMax, hs) : hg < .76f ? Lerp(.28f, .46f, hs) : Lerp(LaneSizeMin, .28f, hs)) * scale;
                    bool iron = (id + serial) % IronEvery == 0 && size < .46f * scale;
                    byte look = size > .44f * scale ? LookSlab : PickLook(Hash01(serial, 240 + id), .45f, .85f);
                    float a = Math.Max(start, Math.Min(end - size * .5f, along + (Hash01(serial, 260 + id) - .5f) * .16f));
                    float across = s * LaneAcross(halfWidth, LaneEdge(serial, s, a), size, linkHalf, Hash01(serial, 320 + id));
                    float th = Hash01(serial, 380 + id);
                    bool shoved = look == LookSlab && size > .4f * scale;
                    into[n++] = new Stone
                    {
                        Along = a, Across = across, Size = size,
                        Tilt = iron ? TiltFor(LookSlab, th) : shoved ? Lerp(30f, 62f, th) : TiltFor(look, th),
                        Spin = 360f * Hash01(serial, 440 + id), Side = shoved || Hash01(serial, 460 + id) < .7f ? s : -s,
                        Iron = iron, Look = look, ArrivalTick = LaneArrivalTick(a, waveTick, start, step)
                    };
                    // Рядом с крупным куском — иногда мелочь (ком, обломок): кучка, а не одиночки по линейке.
                    if (size > .26f * scale && Hash01(serial, 480 + id) < .72f && n < into.Length)
                    {
                        float small = Lerp(.12f, .24f, Hash01(serial, 500 + id)) * scale;
                        float sa = Math.Max(start, Math.Min(end - small * .5f, a + (Hash01(serial, 520 + id) < .5f ? -1f : 1f) * Lerp(.14f, .3f, Hash01(serial, 540 + id))));
                        byte sl = Hash01(serial, 560 + id) < .55f ? LookClod : LookBoulder;
                        into[n++] = new Stone
                        {
                            Along = sa, Across = s * LaneAcross(halfWidth, LaneEdge(serial, s, sa), small, linkHalf, Hash01(serial, 580 + id)),
                            Size = small, Tilt = TiltFor(sl, Hash01(serial, 590 + id)), Spin = 360f * Hash01(serial, 610 + id),
                            Side = Hash01(serial, 620 + id) < .6f ? s : -s, Look = sl, ArrivalTick = LaneArrivalTick(sa, waveTick, start, step)
                        };
                    }
                    along += Lerp(LaneStepMin, LaneStepMax, Hash01(serial, 640 + id)) + size * .4f
                             + (Hash01(serial, 660 + id) < LaneGapChance ? LaneGap : 0f);
                    id++;
                }
            }
            // Щебень внутри полосы: неровным шагом, сторона — как выпадет, между краем и звеном.
            int k = 0;
            for (float along = start + .4f; along < end - .2f && n < into.Length; k++)
            {
                float size = Lerp(LaneRubbleMin, LaneRubbleMax, Hash01(serial, 680 + k)) * scale;
                int side = Hash01(serial, 690 + k) < .5f ? -1 : 1;
                float inner = linkHalf + .05f + size * .5f;
                float across = side * Math.Min(halfWidth - size * .5f, inner + .1f * Hash01(serial, 710 + k));
                float a = Math.Min(end - size * .5f, along + (Hash01(serial, 730 + k) - .5f) * .2f);
                byte look = Hash01(serial, 750 + k) < .5f ? LookBoulder : LookClod;
                into[n++] = new Stone
                {
                    Along = a, Across = across, Size = size, Tilt = TiltFor(look, Hash01(serial, 770 + k)),
                    Spin = 360f * Hash01(serial, 790 + k), Side = Hash01(serial, 810 + k) < .6f ? side : -side, Look = look,
                    ArrivalTick = LaneArrivalTick(a, waveTick, start, step)
                };
                along += LaneRubbleSpacing * Lerp(.6f, 1.5f, Hash01(serial, 830 + k));
            }
            return n - at;
        }

        /// <summary>
        /// Поперёк полосы для куска размера size: наружный край — у неровного края (edge — доля полуширины) с
        /// разбросом внутрь, но не на звене (мелочь — целиком между звеном и краем) и не за полушириной.
        /// </summary>
        private static float LaneAcross(float halfWidth, float edge, float size, float linkHalf, float h)
        {
            float outer = Math.Min(halfWidth, edge * halfWidth + .06f) - size * .5f - .14f * h;
            float inner = linkHalf + size * (size <= LaneRubbleMax ? .5f : .3f) + .01f;
            return Math.Min(halfWidth - size * .5f, Math.Max(inner, outer));
        }

        /// <summary>Наибольшее число камней на один удар оземь (круг + полоса до 6,5 м).</summary>
        public const int MaxStones = CraterRimStones + CraterInnerStones + 64;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ---------------------------------------------------------------- свет и мах

        /// <summary>Холодный свет удара (HDR интенсивность точечного света): вспышка в удар, держится, пока бежит фронт, гаснет.</summary>
        public static float LightAt(float seconds, int travelTicks)
        {
            if (seconds < 0f) return 0f;
            float run = Seconds(Math.Max(1, travelTicks));
            float flash = 1.8f * (float)Math.Exp(-seconds / .07f);
            float body = seconds <= run ? .9f : .9f * (float)Math.Exp(-(seconds - run) / .22f);
            return flash + body;
        }

        /// <summary>Росчерк маха у головы якоря: длина по скорости головы, м.</summary>
        public static float StreakLength(float headSpeed) => Math.Max(.45f, Math.Min(1.1f, headSpeed * .045f));
    }
}
