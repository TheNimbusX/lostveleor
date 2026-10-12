using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — ФОРМЫ НА СТЕКЕ БАЗЫ (08.10; кадры ART/characters/pelag/anchor-throw-2026-10-07/chatgpt-results:
    /// Невод form-net-v1, Веер form-fan-ghost-v1, Гарпун form-harpoon-v2). Формы — это база «холодное железо»
    /// (PelagAnchorThrowIronLook, PelagVfxController.AnchorThrowIron*) с ПЕРЕКРАШЕННОЙ палитрой плюс свои элементы формы:
    /// сеть из цепей Невода, призрачные якоря Веера на призрачных цепях, укус и долгая борозда Гарпуна. Здесь — только числа
    /// без Unity (тесты — Combat.Presentation.Tests/AnchorThrowFormLookTests): палитры (материалы — sRGB-числа, частицы —
    /// линейные цвета), сеть по снимку Sim, призраки, звенья призрачной цепи, борозда Гарпуна. Ассеты —
    /// Editor/PelagWreckComboVfxSetup.ThrowForm (перекраска базы тем же сетапом). Размеры — от Sim, не от кадра.
    /// </summary>
    public static class PelagThrowFormLook
    {
        public const float TicksPerSecond = 30f;

        // ---------------------------------------------------------------- палитры (sRGB)

        /// <summary>
        /// Палитра стека: светящийся край (не чернила), цвет, светлый, почти белая сердцевина; ореол струны — глубина и светлый.
        /// Числа сняты с кадров форм (яркие насыщенные пиксели по ступеням яркости), край — их тёмная насыщенная ступень.
        /// </summary>
        public readonly struct Palette
        {
            public readonly int Edge, Mid, Light, Hot, HaloDeep, HaloLight;
            public Palette(int edge, int mid, int light, int hot, int haloDeep, int haloLight)
            {
                Edge = edge; Mid = mid; Light = light; Hot = hot; HaloDeep = haloDeep; HaloLight = haloLight;
            }
        }

        /// <summary>База — кобальт и сталь Крушения (как PelagAnchorThrowIronLook и сетап ThrowIron).</summary>
        public static readonly Palette Base = new Palette(0x1A44B8, 0x2E6FE0, 0x8FC4FF, 0xEAF4FF, 0x2050D0, 0x6FA8FF);
        /// <summary>Невод — морская зелень с бирюзой кадра (#41BEAE…#6BEFE4 на form-net-v1), сердцевина мятно-белая.</summary>
        public static readonly Palette Net = new Palette(0x0E7A62, 0x22B595, 0x6BEFDA, 0xE6FFF8, 0x14A07E, 0x62E6CC);
        /// <summary>Веер — индиго-фиолет (#6033BF…#8C5FF5 на form-fan-ghost-v1), сердцевина холодно-белая.</summary>
        public static readonly Palette Fan = new Palette(0x4A26B8, 0x7448E0, 0xA88CFF, 0xF2EEFF, 0x5A34D0, 0x9C80FF);
        /// <summary>Гарпун — маджента «Пробоины» Абордажа (#BF1B6C…#ED49A9 на form-harpoon-v2), сердцевина розово-белая.</summary>
        public static readonly Palette Harpoon = new Palette(0xA8136A, 0xE0228C, 0xFF7AD0, 0xFFE8F6, 0xC81C7E, 0xFF6CC4);

        public static Palette For(PelagForm form)
            => form == PelagForm.AnchorThrowNet ? Net : form == PelagForm.AnchorThrowFan ? Fan
                : form == PelagForm.AnchorThrowHarpoon ? Harpoon : Base;

        /// <summary>Форма Броска со своей краской и своими элементами (у остальных — база как есть).</summary>
        public static bool IsThrowForm(PelagForm form)
            => form == PelagForm.AnchorThrowNet || form == PelagForm.AnchorThrowFan || form == PelagForm.AnchorThrowHarpoon;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Smooth01(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }

        // ---------------------------------------------------------------- цвета частиц (линейные: шейдер Bit не переводит)

        /// <summary>Линейный цвет частицы (HDR допускается).</summary>
        public readonly struct Rgb
        {
            public readonly float R, G, B;
            public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
            public float Max => Math.Max(R, Math.Max(G, B));
            public Rgb Scaled(float k) => new Rgb(R * k, G * k, B * k);
            /// <summary>Тот же оттенок, самый яркий канал = <paramref name="max"/>.</summary>
            public Rgb Normalized(float max) => Max > 1e-5f ? Scaled(max / Max) : this;
            public static Rgb Mix(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        private static float ToLinear(int channel)
        {
            double c = channel / 255.0;
            return (float)(c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4));
        }

        /// <summary>sRGB-число палитры → линейный цвет.</summary>
        public static Rgb Linear(int hex) => new Rgb(ToLinear((hex >> 16) & 255), ToLinear((hex >> 8) & 255), ToLinear(hex & 255));

        /// <summary>
        /// Цвета частиц стека: искры (горячая и в цвете), вспышки головы (основная и светлая), эхо знака, звезда оглушения.
        /// База — ровно числа «холодного железа» (PelagVfxController.AnchorThrowIron); формы — тот же расклад из своей палитры.
        /// </summary>
        public readonly struct Tint
        {
            public readonly Rgb SparkHot, SparkTint, Flare, FlareLight, Echo, Stun;
            public Tint(Rgb sparkHot, Rgb sparkTint, Rgb flare, Rgb flareLight, Rgb echo, Rgb stun)
            {
                SparkHot = sparkHot; SparkTint = sparkTint; Flare = flare; FlareLight = flareLight; Echo = echo; Stun = stun;
            }
        }

        public static readonly Tint BaseTint = new Tint(new Rgb(1.12f, 1.2f, 1.32f), new Rgb(.56f, .80f, 1.10f), new Rgb(.20f, .45f, 1.0f),
            new Rgb(.42f, .66f, 1.08f), new Rgb(.42f, .70f, 1.15f), new Rgb(.20f, .46f, .98f));

        /// <summary>
        /// Цвета частиц формы. Вспышка = светлая сторона цвета (как кобальт базы (.20,.45,1.0) = цвет→светлый 70 %, ярчайший
        /// канал 1); светлая вспышка и эхо — светлый; искры — сердцевина ×1,2 и светлый; звезда оглушения — как вспышка.
        /// </summary>
        public static Tint TintFor(PelagForm form)
        {
            if (!IsThrowForm(form)) return BaseTint;
            Palette p = For(form);
            Rgb mid = Linear(p.Mid), light = Linear(p.Light), hot = Linear(p.Hot);
            Rgb flare = Rgb.Mix(mid, light, .7f).Normalized(1f);
            return new Tint(hot.Scaled(1.2f), light.Normalized(1.1f), flare, light.Normalized(1.08f), light.Normalized(1.15f),
                flare.Scaled(.98f));
        }

        // ---------------------------------------------------------------- струна и вспышки формы

        /// <summary>
        /// Ширина струны и ореола формы к базе (08.10 V7, игра 1:1 против кадров форм: на form-net/fan/harpoon лучи тоньше
        /// базового кадра — цепь Невода ~8 px, лучи Веера ~11, Гарпуна ~9 px в масштабе игры; база V6 ~20 px).
        /// </summary>
        public static float StreakScale(PelagForm form)
            => form == PelagForm.AnchorThrowNet ? .62f : form == PelagForm.AnchorThrowFan ? .74f
                : form == PelagForm.AnchorThrowHarpoon ? .62f : 1f;

        /// <summary>
        /// Вспышки головы формы к базе (колючая звезда полёта, щелчок, рывок). Веер — три головы: звёзды меньше, иначе
        /// закрывают призрачные якоря (игра V6: якорей за звёздами не видно); Гарпун — укус читается знаком на теле.
        /// </summary>
        public static float FlareScale(PelagForm form, int lane)
            => form == PelagForm.AnchorThrowFan ? (lane == 0 ? .7f : .5f) : form == PelagForm.AnchorThrowHarpoon ? .6f
                : form == PelagForm.AnchorThrowNet ? .8f : 1f;

        /// <summary>Звезда полёта каждый тик: у боковых полос Веера нет (их голова — призрачный якорь, искры остаются).</summary>
        public static bool FlightFlares(PelagForm form, int lane) => !(form == PelagForm.AnchorThrowFan && lane > 0);

        // ---------------------------------------------------------------- тяга (кривая Sim между тиками)

        /// <summary>Доля пути тяги на тике показа: та же кривая, что у головы и тел (MaelstromPullProgress), между тиками — отрезок.</summary>
        public static float Pull(float shown, int tautTick, int returnTicks)
        {
            if (returnTicks <= 0) return shown > tautTick ? 1f : 0f;
            float k = shown - tautTick;
            if (k <= 0f) return 0f;
            if (k >= returnTicks) return 1f;
            int k0 = (int)Math.Floor(k);
            float a = Simulation.MaelstromPullProgress(k0, returnTicks).ToFloat();
            float b = Simulation.MaelstromPullProgress(k0 + 1, returnTicks).ToFloat();
            return a + (b - a) * (k - k0);
        }

        // ---------------------------------------------------------------- НЕВОД: сеть из цепей

        /// <summary>Ближний край сети от центра героя в каст, м (горло на цепи перед рукой, не на теле героя), подъём, м.</summary>
        public const float NetNear = 1.1f, NetLift = .14f;
        /// <summary>Горло у ближнего края (сеть «висит» на цепи у руки), м; ширится до полной на столько метров.</summary>
        public const float NetThroatHalf = .3f, NetWiden = 2.4f;
        /// <summary>Дальний край за концом полосы, м, и рваность хвостового края ±, м.</summary>
        public const float NetFarBeyond = .2f, NetRag = .38f;
        /// <summary>
        /// Шаг ромба вдоль и поперёк, м. V7 по кадру (~85 px/м на form-net-v1: ромб ~100 × 65 px): ромб вытянут вдоль линии,
        /// ~4 ряда узлов на ширину 3 м, между цепями видна земля (V6 .74/.62 в игре — мелкая частая сетка-«забор»).
        /// </summary>
        public const float NetCellAlong = 1.15f, NetCellAcross = .78f;
        /// <summary>Раскрытие: с HoldTick за столько тиков (сеть ложится к рывку), полный размах — через тик после рывка.</summary>
        public const float NetOpenTicks = 2f;
        /// <summary>Сеть стянута к руке после ловли за столько тиков; узлы гаснут искрами.</summary>
        public const float NetCollapseTicks = 5f;
        /// <summary>Сеть над пойманным телом: горб высотой, м, и радиусом, м (цепи облегают тело).</summary>
        public const float NetBumpHeight = .62f, NetBumpRadius = .62f;
        /// <summary>
        /// Узел: размер в покое (светящаяся точка с белым ядром) и во вспышке (звезда), м; полуширина ленты цепи сети, м
        /// (звено железа ±.065 по центру, свет морской зелени — по краям ленты).
        /// </summary>
        public const float KnotSize = .52f, KnotFlareSize = .70f, StrandHalf = .12f;

        /// <summary>Раскрытие сети на тике показа: 0 до HoldTick, 1 через NetOpenTicks.</summary>
        public static float NetOpen(float shown, int holdTick) => Smooth01((shown - holdTick) / NetOpenTicks);

        /// <summary>Стягивание после ловли: 0 → 1 за NetCollapseTicks.</summary>
        public static float NetCollapse(float shown, float catchTick) => Clamp01((shown - catchTick) / NetCollapseTicks);

        /// <summary>
        /// Края сети вдоль полосы, м от центра героя: <paramref name="near"/> — ближний (горло у руки), <paramref name="far"/> —
        /// хвостовой край (до рывка — за концом полосы, в возврате — по кривой тяги к полукольцу ловли, сеть «гребёт»
        /// всех к Пелагу; после ловли — к руке). <paramref name="restFar"/> — хвост до рывка (сеть в покое).
        /// </summary>
        public static void NetSpan(in AnchorThrowState s, float shown, float catchFrom, out float near, out float far, out float restFar)
        {
            restFar = s.Reach0.ToFloat() + NetFarBeyond;
            float ring = s.RingRadius.Raw > 0 ? s.RingRadius.ToFloat() : 1.2f;
            float raked = ring + .35f;
            far = Lerp(restFar, raked, Pull(shown, s.TautTick, s.ReturnTicks));
            near = Math.Min(NetNear, far - .5f);
            float c = NetCollapse(shown, catchFrom);
            if (c > 0f)
            {
                far = Lerp(far, NetNear, Smooth01(c));
                near = Math.Min(near, far - .2f * (1f - c));
            }
        }

        /// <summary>Полуширина сети на расстоянии <paramref name="fromThroat"/> м от горла (до полной ширины на NetWiden).</summary>
        public static float NetHalfAt(float fromThroat, float fullHalf)
            => Lerp(NetThroatHalf, fullHalf, (float)Math.Sqrt(Smooth01(fromThroat / NetWiden)));

        /// <summary>Сжатие поперёк, когда сеть стянута: длина сейчас / в покое → множитель полуширины.</summary>
        public static float NetGatherHalf(float length, float restLength)
            => Lerp(.5f, 1f, (float)Math.Sqrt(Clamp01(length / Math.Max(.1f, restLength))));

        /// <summary>Рваный хвостовой край: смещение, м, у поперечной доли b (−1…1) — детерминированный «шум» по номеру броска.</summary>
        public static float NetRagAt(float b, int serial)
        {
            double x = b * 3.1 + serial * .731;
            double n = Math.Sin(x * 2.3) * .55 + Math.Sin(x * 5.9 + 1.7) * .3 + Math.Sin(x * 11.3 + .4) * .15;
            return (float)(n * NetRag);
        }

        // ---------------------------------------------------------------- ВЕЕР: призраки на призрачных цепях

        /// <summary>
        /// Призрак проявляется за 1,2 тика с выпуска, распадается за 3 тика (снизу — с лап — вверх) — с подлёта к руке
        /// (GhostDissolveBeforeCatch до ловли) или со срыва (игра V8: два призрака ×1,4 распадались у руки 5 тиков после ловли —
        /// лавандовое пятно на герое).
        /// </summary>
        public static float GhostAppear(float shown, int releaseTick) => Smooth01((shown - releaseTick) / 1.2f);
        public static float GhostDissolve(float shown, float fromTick) => shown < fromTick ? 0f : Clamp01((shown - fromTick) / 3f);
        public const float GhostDissolveBeforeCatch = 1.5f;

        /// <summary>Рост призрака у руки: в последние GhostShrinkMeters до кулака — к размеру настоящей головы.</summary>
        public const float GhostShrinkMeters = 2.2f;
        public static float GhostGrow(float toHand) => Lerp(1f, GhostScale, Smooth01(toHand / GhostShrinkMeters));

        /// <summary>Высота призрака, если настоящую голову не найти, м (размер головы рига ~0,9).</summary>
        public const float GhostFallbackHeight = .9f;
        /// <summary>
        /// Призрак крупнее настоящей головы (вокруг кольца цепи): на кадре form-fan-ghost-v1 призрачные якоря размером с
        /// железный и читаются издалека; в игре V6 голова рига ~45 px — призрак терялся в звёздах.
        /// </summary>
        public const float GhostScale = 1.4f;
        /// <summary>Звенья призрачной цепи (лента со звеньями шейдера «Form Chain» поверх струны базы): шаг, полуширина звена, м.</summary>
        public const float LinkPitch = .105f, LinkHalf = .042f, LinkStripHalf = .062f;

        // ---------------------------------------------------------------- ГАРПУН

        /// <summary>Оглушение цели: 2 тика натяга + возврат + 30 (контракт §5), тиков от укуса.</summary>
        public static int HarpoonStunTicks(int returnTicks) => Simulation.AnchorThrowTautTicks + returnTicks + 30;

        /// <summary>
        /// Долгая борозда Гарпуна (кадр form-harpoon-v2): шире базы во столько раз, комьев и камней гуще во столько (V8: игра V7 —
        /// широкая тёмная канава ×1,3 без каменных бортиков; на кадре канава уже, борта завалены камнями).
        /// </summary>
        public const float HarpoonFurrowWidth = 1.12f, HarpoonFurrowSpray = 2.4f;

        /// <summary>Полуширина борозды тянутого: база по радиусу тела, у цели Гарпуна — шире.</summary>
        public static float FurrowHalf(float bodyRadius, bool harpoon)
            => PelagAnchorThrowIronLook.FurrowHalf(bodyRadius) * (harpoon ? HarpoonFurrowWidth : 1f);
    }
}
