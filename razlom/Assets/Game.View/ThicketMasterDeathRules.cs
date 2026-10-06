using System;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — СМЕРТЬ «ЦВЕТУЩИЙ ХОЛМ»: чистые правила вида (без UnityEngine, тесты —
    /// tools/Combat.Presentation.Tests/ThicketMasterDeathRulesTests.cs). Владелец 02.10, вечер: «смерть надо
    /// доработать» — в игре смерть читалась тёмным комом, цветов с камеры боя (48°) не было видно: они росли
    /// под лежащим телом и вяли раньше, чем оно уходило.
    ///
    /// Всё время — секунды от тика события Death (тики Sim с долей кадра: пауза держит кадр), land —
    /// касание земли боком (EnemyKillBeat.LandsAt, у босса 2,17 с — кадр 65 клипа Death):
    ///  • 0 — добивающий удар: такт убийства как был (стоп-кадр тела 0,075 с, тряска на залпе коры —
    ///    EnemyDeathFxView), розово-золотая вспышка в кусте и кольцо лепестков с листьями от тела;
    ///    камера заметно наезжает на босса (<see cref="Push"/>: 6,2 → 4,8 за 0,7 с, кадр на 80 % пути к телу);
    ///  • 0 … land — клип Death (шатается, лапы подламываются, валится на бок);
    ///  • land — волна цветения: светящаяся кромка и стоячая стена лепестков и листьев бегут до 6 м, низкая
    ///    короткая пыль; камера держится до land + 0,5 с и плавно отпускает героя к land + 1,6 с;
    ///  • land + 0,2 … + 1,7 — из земли встаёт пригорок 6,2 × 7,8 м, 1,3 м (<see cref="HillRise"/>, рельеф —
    ///    <see cref="KnollSurface"/>): мшистый, с тремя сливающимися горбами, земля — кольцом у подножия; тело
    ///    уходит под него (<see cref="BodySink"/>, land + 0,3 … + 1,8) — к уходу тела из пула (BodyGoneAt, 5,5 с)
    ///    оно давно под землёй, тело не пропадает кадром;
    ///  • land + 1,8 — холм встал: только теперь «АРЕНА ЗАЧИЩЕНА» (<see cref="ClearedBannerAt"/>; ревью
    ///    02.10, вечер: плашка в первую секунду смерти наступала на момент);
    ///  • land + 1,5 … + 3 — холм зацветает: цветы кучками по 3–5 встают по очереди (<see cref="FlowerStart"/>),
    ///    листья и трава между ними, редкие лепестки ~4 с (тёплого облака над холмом нет — читалось мазком тумана);
    ///    тёплый точечный свет над пригорком загорается с цветением и садится до ровного слабого (<see cref="KnollLight"/>);
    ///  • холм с цветами лежит на поляне до смены арены (вид снимает его со сменой глубины, поколения или
    ///    симуляции); с <see cref="SettledAfterLand"/> частицы холма больше не шагают — стоят как есть.
    ///
    /// V13 (владелец 03.10, утро: «холм после смерти, если по нему пройтись, оч коряво выглядит»): по пригорку
    /// ходят. Он ниже (1,3 м) и положе (склон не круче ~34°, подножие сходит к полу без ступеньки), тела встают на
    /// его поверхность — <see cref="KnollFloor"/> (рельеф и рост те же, что у сеток сборки ThicketMasterVfxSetup),
    /// вид кладёт его поверх пола (LayoutView.ShownFloorLevel ← ThicketMasterCombatView.KnollFloor). Ячейки мха,
    /// которые оседали ямой вокруг героя, убраны: мох — одна сетка, цветы и трава стоят, герой идёт сквозь них.
    /// </summary>
    public static class ThicketMasterDeathRules
    {
        // ---------------------------------------------------------------- камера

        /// <summary>
        /// Размер ортокамеры боя (CameraFollow.CombatSize) и размер в наезде. Ревью 02.10, вечер: «наезд почти
        /// не заметен» (5,2 — минус 16 %) — теперь 4,8 (минус 23 %) и кадр ближе к телу.
        /// </summary>
        public const float CombatSize = 6.2f, PushSize = 4.8f;

        /// <summary>Наезд — множитель к размеру камеры до него (CameraFollow.SetCinematic).</summary>
        public const float PushZoom = PushSize / CombatSize;

        /// <summary>Наезд за столько секунд от удара; держится до land + PushHoldAfterLand; отпускает за PushOutSeconds.</summary>
        public const float PushInSeconds = .7f, PushHoldAfterLand = .5f, PushOutSeconds = 1.1f;

        /// <summary>Доля пути кадра от героя к боссу на пике (CameraFollow: blend) — герой остаётся в кадре (KeepHeroWithin).</summary>
        public const float PushBlend = .8f;

        /// <summary>
        /// Центр кадра не дальше стольких метров от героя: далёкий герой не уходит за край. При 4,8 полвысоты кадра —
        /// 4,8 / sin 48° ≈ 6,5 м земли; минус нижняя полоса HUD и рост героя — ~4,3 м.
        /// </summary>
        public const float KeepHeroWithin = 4.3f;

        /// <summary>Точка кадра — над телом на столько метров (лежащий босс ниже 2,3 м).</summary>
        public const float FocusLift = .8f;

        // ---------------------------------------------------------------- холм и тело

        /// <summary>Холм встаёт через столько после касания и растёт столько секунд.</summary>
        public const float HillRiseDelay = .2f, HillRiseSeconds = 1.5f;

        /// <summary>Тело уходит под холм через столько после касания и за столько секунд.</summary>
        public const float SinkDelay = .3f, SinkSeconds = 1.5f;

        /// <summary>
        /// Пригорок, м: полуоси вбок и вдоль взгляда тела, высота вершины и потолок земли основания. V13 (03.10, утро:
        /// по холму 1,8 м ходить было коряво): 1,3 м и шире — 6,2 × 7,8 м (было 5 × 6,5), чтобы склон при той же
        /// форме был не круче ~34° (<see cref="KnollMaxSlope"/>), а лежащее тело всё так же уходило под него.
        /// </summary>
        public const float HillHalfWidth = 3.1f, HillHalfLength = 3.9f, HillHeight = 1.3f, HillBaseHeight = .42f;

        // ---------------------------------------------------------------- пригорок: рельеф (общий у сборки и вида)

        /// <summary>Склон рельефа не круче стольких метров на метр (tan 35°): по пригорку ходят (проверяет тест).</summary>
        public const float KnollMaxSlope = .7f;

        /// <summary>Неровный край: радиус по углу гуляет не больше чем на столько долей (сумма амплитуд <see cref="KnollRim"/>).</summary>
        public const float KnollRimSwing = .095f;

        /// <summary>Неровный край холма: множитель радиуса по углу (контур не циркульный, без правильного эллипса).</summary>
        public static float KnollRim(float angle)
            => 1f + .045f * Sin(3f * angle + 1.1f) + .03f * Sin(5f * angle + 2.3f) + .02f * Sin(9f * angle + .4f);

        /// <summary>
        /// Доля радиуса пригорка в точке (x — вбок, z — вдоль взгляда тела): 0 — середина, 1 — край (неровный,
        /// <see cref="KnollRim"/>). Неровность края к середине сходит на нет (у середины — эллипс): иначе рельеф
        /// рисовал звезду лучевых складок от вершины.
        /// </summary>
        public static float KnollDistance(float x, float z)
        {
            float u = x / HillHalfWidth, v = z / HillHalfLength;
            float r = (float)Math.Sqrt(u * u + v * v);
            return r / (1f + (KnollRim((float)Math.Atan2(u, v)) - 1f) * Smooth(Clamp01((r - .15f) / .6f)));
        }

        /// <summary>
        /// Купол по доле радиуса d: скруглённая вершина до KnollRoundTop, ровный склон, скруглённое подножие с
        /// KnollRoundFoot до края — наклон непрерывен, у края и на вершине 0 (подножие сходит к полу без ступеньки).
        /// Ровный склон при той же высоте положе колокола (1 − d²)² (1,27 против 1,54 на долю радиуса).
        /// </summary>
        public static float KnollProfile(float d)
        {
            const float a = KnollRoundTop, b = KnollRoundFoot, s = 1f / (1f - (a + b) * .5f);
            if (d <= 0f) return 1f;
            if (d >= 1f) return 0f;
            if (d < a) return 1f - s * d * d / (2f * a);
            if (d <= 1f - b) return 1f - s * a * .5f - s * (d - a);
            float f = 1f - d;
            return s * f * f / (2f * b);
        }

        private const float KnollRoundTop = .3f, KnollRoundFoot = .12f;

        /// <summary>
        /// Горбы пригорка: три мягких горба складываются (не max — без складок) поверх купола KnollDomeLift: главный
        /// чуть позади середины, второй к носу тела и вбок — плечом, третий — низкое плечо с другого бока. X, Z —
        /// место, м; SX, SZ — ширина гаусса, м; H — высота до растяжки.
        /// </summary>
        private static readonly float[] KnollSwellX = { -.36f, .95f, -1.35f }, KnollSwellZ = { -.54f, 1.85f, .95f },
            KnollSwellSX = { 2f, 1.25f, 1.3f }, KnollSwellSZ = { 2.3f, 1.35f, 1.3f }, KnollSwellH = { .9f, .5f, .3f };

        private const float KnollDomeLift = .65f, KnollLumpiness = .025f;

        /// <summary>Рельеф до растяжки: купол × (подъём + горбы), рыхлость ±2,5 % крупным шумом; за краем — 0.</summary>
        public static float KnollRelief(float x, float z)
        {
            float d = KnollDistance(x, z);
            if (d >= 1f) return 0f;
            float s = KnollDomeLift;
            for (int i = 0; i < KnollSwellH.Length; i++)
            {
                float dx = (x - KnollSwellX[i]) / KnollSwellSX[i], dz = (z - KnollSwellZ[i]) / KnollSwellSZ[i];
                s += KnollSwellH[i] * (float)Math.Exp(-(dx * dx + dz * dz));
            }
            return KnollProfile(d) * s * (1f + KnollLumpiness * (Noise2(x * 1.1f + 3f, z * 1.1f, 31) * 2f - 1f));
        }

        /// <summary>Верх пригорка T, м: рельеф, растянутый так, что вершина — ровно <see cref="HillHeight"/>.</summary>
        public static float KnollTop(float x, float z) => KnollRelief(x, z) * KnollReliefScale;

        private static float _knollReliefScale;

        /// <summary>
        /// HillHeight / вершина рельефа (сетка 10 см по холму, уточнение 1 см и 1 мм у лучшей точки; считается раз —
        /// рельеф постоянный, у сборки и вида один и тот же счёт).
        /// </summary>
        public static float KnollReliefScale
        {
            get
            {
                if (_knollReliefScale > 0f) return _knollReliefScale;
                float peak = 0f, px = 0f, pz = 0f;
                for (int i = -31; i <= 31; i++)
                    for (int j = -39; j <= 39; j++)
                    {
                        float t = KnollRelief(i * .1f, j * .1f);
                        if (t > peak) { peak = t; px = i * .1f; pz = j * .1f; }
                    }
                for (float step = .01f; step > .0005f; step *= .1f)
                {
                    float bx = px, bz = pz;
                    for (int i = -10; i <= 10; i++)
                        for (int j = -10; j <= 10; j++)
                        {
                            float t = KnollRelief(bx + i * step, bz + j * step);
                            if (t > peak) { peak = t; px = bx + i * step; pz = bz + j * step; }
                        }
                }
                _knollReliefScale = peak > .01f ? HillHeight / peak : 1f;
                return _knollReliefScale;
            }
        }

        /// <summary>
        /// Основание (земля) по верху t: cap·(1 − e^(−0,85·t/cap)) — у края почти весь верх (земля кольцом у
        /// подножия), к середине не выше <see cref="HillBaseHeight"/>; гладко. Мох несёт разницу T − основание.
        /// </summary>
        public static float KnollBase(float t) => HillBaseHeight * (1f - (float)Math.Exp(-.85f * t / HillBaseHeight));

        /// <summary>Основание (земля) в точке, м; вне края — 0.</summary>
        public static float KnollGround(float x, float z) => KnollBase(KnollTop(x, z));

        /// <summary>Мох 0…1 по верху t: мох с верха выше ~0,1 м ± 0,04 шумом — неровная живая кромка; ниже — земля основания.</summary>
        public static float KnollMoss(float x, float z, float t)
        {
            float edge = .1f + .04f * (Noise2(x * 1.7f + 5f, z * 1.7f - 2f, 41) * 2f - 1f);
            return Smooth(Clamp01((t - (edge - .04f)) / .08f));
        }

        /// <summary>Край сетки мха уходит под основание на столько метров (без щели у кромки мха).</summary>
        public const float KnollMossTuck = .035f;

        /// <summary>Высота сетки мха над основанием в точке с верхом t, м: на мху — ровно T, без мха — под основанием.</summary>
        public static float KnollMossLift(float x, float z, float t)
            => t <= 0f ? -KnollMossTuck : (t - KnollBase(t) + KnollMossTuck) * KnollMoss(x, z, t) - KnollMossTuck;

        /// <summary>
        /// Видимый верх пригорка в полный рост, м над корнем: основание и мох над ним — ровно то, что рисуют сетки
        /// сборки (на мху — <see cref="KnollTop"/>, в кольце земли — основание, кромка мха — ступенька 2–4 см).
        /// По нему ходят тела, на него встают цветы, листья, трава и ростки; за краем — 0.
        /// </summary>
        public static float KnollSurface(float x, float z)
        {
            float t = KnollTop(x, z);
            if (t <= 0f) return 0f;
            float ground = KnollBase(t);
            return ground + Math.Max(0f, KnollMossLift(x, z, t));
        }

        // ---------------------------------------------------------------- пригорок: рост (сетки и тела вместе)

        /// <summary>
        /// Основание и мох собраны на столько метров выше и стоят на столько же ниже корня: рост по высоте идёт
        /// из-под земли (край выходит к нулю только в полный рост), без мерцания плоского диска в полу.
        /// </summary>
        public const float HillSink = .25f;

        /// <summary>
        /// Рост сеток пригорка — кривые размера частицы по доле её жизни (линейные отрезки между ключами):
        /// по высоте — <see cref="RiseCurve"/> в HillRiseHeightKeys ключах, вширь — от HillRiseWidthFrom к 1 по
        /// ней же в HillRiseWidthKeys ключах. Сборка пишет ровно эти ключи, вид поднимает по ним тела.
        /// </summary>
        public const int HillRiseHeightKeys = 7, HillRiseWidthKeys = 5;

        public const float HillRiseWidthFrom = .82f;

        /// <summary>Ширина сеток пригорка по доле роста x (то, что сэмплирует сборка): от HillRiseWidthFrom к 1.</summary>
        public static float HillRiseWidthCurve(float x) => HillRiseWidthFrom + (1f - HillRiseWidthFrom) * RiseCurve(x);

        /// <summary>Множитель высоты сеток на доле роста x — как кривая частицы (линейно между ключами).</summary>
        public static float HillRiseHeight(float x)
        {
            x = Clamp01(x);
            float s = x * (HillRiseHeightKeys - 1);
            int i = Math.Min(HillRiseHeightKeys - 2, (int)s);
            float a = RiseCurve(i / (HillRiseHeightKeys - 1f)), b = RiseCurve((i + 1) / (HillRiseHeightKeys - 1f));
            return a + (b - a) * (s - i);
        }

        /// <summary>Множитель ширины сеток на доле роста x — как кривая частицы (линейно между ключами).</summary>
        public static float HillRiseWidth(float x)
        {
            x = Clamp01(x);
            float s = x * (HillRiseWidthKeys - 1);
            int i = Math.Min(HillRiseWidthKeys - 2, (int)s);
            float a = HillRiseWidthCurve(i / (HillRiseWidthKeys - 1f)), b = HillRiseWidthCurve((i + 1) / (HillRiseWidthKeys - 1f));
            return a + (b - a) * (s - i);
        }

        /// <summary>Доля роста пригорка 0…1 (доля жизни частиц основания и мха) на seconds от удара.</summary>
        public static float KnollRiseFraction(float seconds, float land) => Clamp01((seconds - land - HillRiseDelay) / HillRiseSeconds);

        /// <summary>
        /// Пол пригорка — высота видимого верха над корнем в точке (x, z) осей холма на доле роста rise
        /// (<see cref="KnollRiseFraction"/>): сетки стоят на −HillSink и растут по осям (HillRiseHeight, HillRiseWidth),
        /// поэтому тело на пригорке встаёт ровно с землёй под ним — не висит и не тонет. До роста и за краем — ниже
        /// нуля (пол поляны выше: вид берёт большее из пола и этого).
        /// </summary>
        public static float KnollFloor(float x, float z, float rise)
        {
            if (rise <= 0f) return -HillSink;
            float h = HillRiseHeight(rise), w = HillRiseWidth(rise);
            return -HillSink + h * (KnollSurface(x / w, z / w) + HillSink);
        }

        /// <summary>Точка (x, z) осей холма точно не на пригорке (дальше края при любой неровности) — пол без счёта рельефа.</summary>
        public static bool OffKnoll(float x, float z)
        {
            float reach = 1f + KnollRimSwing;
            return x > HillHalfWidth * reach || -x > HillHalfWidth * reach || z > HillHalfLength * reach || -z > HillHalfLength * reach;
        }

        // ---------------------------------------------------------------- плашка зачистки

        /// <summary>
        /// «АРЕНА ЗАЧИЩЕНА» на смерти босса — через столько после касания боком: холм встал
        /// (HillRiseDelay + HillRiseSeconds) и ещё 0,1 с. Плашка не наступает на смерть (ревью 02.10, вечер).
        /// </summary>
        public const float ClearedBannerAfterLand = HillRiseDelay + HillRiseSeconds + .1f;

        /// <summary>Когда показать «АРЕНА ЗАЧИЩЕНА», с от удара (≈ 4 с при касании на 2,17 с).</summary>
        public static float ClearedBannerAt(float land) => land + ClearedBannerAfterLand;

        // ---------------------------------------------------------------- цветение

        /// <summary>Первый цветок встаёт через столько после касания (холм уже почти в росте), последний — через BloomSpread после первого.</summary>
        public const float BloomDelay = 1.5f, BloomSpread = 1.5f;

        /// <summary>Цветок раскрывается за столько секунд (с перелётом ~10 %).</summary>
        public const float FlowerGrowSeconds = .55f;

        /// <summary>Лепестки над холмом сыплются столько секунд с начала цветения.</summary>
        public const float PetalDriftSeconds = 4f;

        // ---------------------------------------------------------------- свет над пригорком (V11)

        /// <summary>
        /// Тёплый точечный свет над пригорком (V11: в тёмной половине поляны цветение было без света): загорается
        /// за KnollLightLead до первого цветка, к KnollLightPeakAt доле цветения — пик, за KnollLightSettle после
        /// последнего цветка садится до KnollLightRest пика и так горит, пока лежит холм. Без ореола и дымки — только свет.
        /// </summary>
        public const float KnollLightLead = .25f, KnollLightPeakAt = .8f, KnollLightSettle = 2.5f, KnollLightRest = .5f;

        /// <summary>Свет пригорка сдвинут от его середины к камере по земле на столько метров: светит склон, который видно.</summary>
        public const float KnollLightToCamera = .8f;

        /// <summary>Доля пиковой яркости света пригорка 0…1 на seconds от удара (land — касание боком).</summary>
        public static float KnollLight(float seconds, float land)
        {
            float start = land + BloomDelay - KnollLightLead;
            float peak = land + BloomDelay + BloomSpread * KnollLightPeakAt;
            if (seconds <= start) return 0f;
            if (seconds < peak) return Smooth(Clamp01((seconds - start) / (peak - start)));
            float settle = Smooth(Clamp01((seconds - peak) / (land + BloomDelay + BloomSpread + KnollLightSettle - peak)));
            return 1f - (1f - KnollLightRest) * settle;
        }

        /// <summary>
        /// Свет пригорка и герой на нём (V13: по пригорку ходят). Свет висит 2,9 м над корнем, а голова героя на вершине —
        /// на ~3,1 м: точечный свет в упор пересвечивал бы его (высветлять героя владелец запретил 24.09). Поэтому ближе
        /// KnollLightHeroReach м по земле свет поднимается над головой (<see cref="KnollLightLift"/>), а остаток сближения
        /// гасит яркость (<see cref="KnollLightNear"/>): на героя падает не больше, чем на стоящего прямо под светом до
        /// V13 (свет 3,4 м над полом, голова ~1,8 м — KnollLightHeroClear). Рост героя до макушки — KnollLightHeroHeight.
        /// Далеко от света (и на полу у подножия) свет тот же, что в сборке.
        /// </summary>
        public const float KnollLightHeroClear = 1.6f, KnollLightHeroHeight = 2f, KnollLightHeroReach = 2.5f;

        /// <summary>
        /// Подъём света пригорка над героем, м: свет на высоте lightY, ноги героя — на feetY, по земле до света horizontal м.
        /// Под самым светом — до макушки + KnollLightHeroClear, к KnollLightHeroReach гладко сходит к 0; низко стоящему
        /// герою (макушка + KnollLightHeroClear не выше света) — 0.
        /// </summary>
        public static float KnollLightLift(float horizontal, float lightY, float feetY)
        {
            float need = feetY + KnollLightHeroHeight + KnollLightHeroClear - lightY;
            if (need <= 0f) return 0f;
            return need * Smooth(1f - Clamp01(horizontal / KnollLightHeroReach));
        }

        /// <summary>
        /// Доля яркости света пригорка рядом с героем 0…1: (d / KnollLightHeroClear)², где d — от света на высоте lightY
        /// (уже поднятого, <see cref="KnollLightLift"/>) до героя (отрезок от ног feetY до макушки), horizontal — по земле.
        /// Освещённость героя не выше, чем от света в KnollLightHeroClear м; дальше — 1 (свет как в сборке).
        /// </summary>
        public static float KnollLightNear(float horizontal, float lightY, float feetY)
        {
            float head = feetY + KnollLightHeroHeight;
            float dy = lightY > head ? lightY - head : lightY < feetY ? feetY - lightY : 0f;
            float d2 = horizontal * horizontal + dy * dy;
            const float clear2 = KnollLightHeroClear * KnollLightHeroClear;
            return d2 >= clear2 ? 1f : d2 / clear2;
        }

        /// <summary>Жизнь частиц холма, с: «вечные» до смены арены (вид снимает холм сам).</summary>
        public const float HillLifeSeconds = 36000f;

        /// <summary>С этого времени после касания холм стоит: всё выросло, лепестки долетели — частицы не шагают.</summary>
        public const float SettledAfterLand = BloomDelay + PetalDriftSeconds + 3.5f;

        // ---------------------------------------------------------------- время

        /// <summary>Наезд камеры 0…1 на seconds от удара: сглаженный вход, держится, сглаженный выход.</summary>
        public static float Push(float seconds, float land)
        {
            if (seconds <= 0f) return 0f;
            float into = Smooth(Clamp01(seconds / PushInSeconds));
            float hold = Math.Max(PushInSeconds, land + PushHoldAfterLand);
            float release = Smooth(Clamp01((seconds - hold) / PushOutSeconds));
            return into * (1f - release);
        }

        /// <summary>Наезд кончился — камеру можно отпустить.</summary>
        public static bool PushDone(float seconds, float land)
            => seconds >= Math.Max(PushInSeconds, land + PushHoldAfterLand) + PushOutSeconds;

        /// <summary>Доля пути кадра к боссу: пик PushBlend, не дальше KeepHeroWithin м от героя.</summary>
        public static float CameraBlend(float push, float heroDistance)
        {
            float blend = PushBlend * Clamp01(push);
            if (heroDistance > 1e-3f) blend = Math.Min(blend, KeepHeroWithin / heroDistance);
            return Clamp01(blend);
        }

        /// <summary>Множитель размера камеры: 1 → PushZoom.</summary>
        public static float CameraZoom(float push) => 1f - (1f - PushZoom) * Clamp01(push);

        /// <summary>Рост холма 0…1 на seconds от удара: быстро встаёт и дотягивает (ease-out).</summary>
        public static float HillRise(float seconds, float land) => RiseCurve((seconds - land - HillRiseDelay) / HillRiseSeconds);

        /// <summary>Кривая роста холма по доле его роста x: 1 − (1 − x)³.</summary>
        public static float RiseCurve(float x)
        {
            x = Clamp01(x);
            float y = 1f - x;
            return 1f - y * y * y;
        }

        /// <summary>Тело под холмом 0…1 (доля глубины погружения) на seconds от удара.</summary>
        public static float BodySink(float seconds, float land) => Smooth(Clamp01((seconds - land - SinkDelay) / SinkSeconds));

        /// <summary>
        /// Доля поперечника контактной тени корня при погружении тела sink (<see cref="BodySink"/>): тень стоит
        /// у корня, холм — у середины лежащего тела, поэтому она уходит вместе с телом и к его уходу под холм
        /// сжата в ноль — тёмный край не торчит из-под холма до ухода тела в пул.
        /// </summary>
        public static float ShadowLeft(float sink) => 1f - Clamp01(sink);

        /// <summary>Когда встаёт цветок index из count (с от удара): первые — у середины холма, дальше к краям.</summary>
        public static float FlowerStart(int index, int count, float land)
            => land + BloomDelay + (count > 1 ? BloomSpread * index / (count - 1f) : 0f);

        /// <summary>Раскрытие цветка 0…~1,1 на age с от его начала: перелёт и возврат к 1.</summary>
        public static float FlowerGrow(float age)
        {
            if (age <= 0f) return 0f;
            float x = age / FlowerGrowSeconds;
            if (x >= 1f) return 1f;
            if (x < .75f) return 1.1f * Smooth(x / .75f);
            return 1.1f - .1f * Smooth((x - .75f) / .25f);
        }

        /// <summary>Холм дошёл до покоя: дальше частицы не шагают (seconds — от удара).</summary>
        public static bool Settled(float seconds, float land) => seconds >= land + SettledAfterLand;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private static float Sin(float x) => (float)Math.Sin(x);

        // ---------------------------------------------------------------- шум (как у сборки: Hash01 / Noise2)

        /// <summary>Значение шума 0…1 на решётке (детерминированно), сглаженная интерполяция.</summary>
        private static float Noise2(float x, float z, int salt)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fz = fz * fz * (3f - 2f * fz);
            float a = Lattice(ix, iz, salt), b = Lattice(ix + 1, iz, salt), c = Lattice(ix, iz + 1, salt), d = Lattice(ix + 1, iz + 1, salt);
            float low = a + (b - a) * fx, high = c + (d - c) * fx;
            return low + (high - low) * fz;
        }

        private static float Lattice(int x, int z, int salt) => Hash01(unchecked(x * 92837111 ^ z * 689287499), salt);

        private static float Hash01(int i, int salt)
        {
            uint h = unchecked((uint)(i * 747796405 + salt * 2891336453u));
            h = unchecked(((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u);
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
