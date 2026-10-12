using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения на стеке серии сабли (06.10 вечер; ассеты — Editor/PelagWreckComboVfxSetup, рождение —
    /// PelagVfxController.WreckCombo). Чистые числа вида без Unity: палитра формы (кобальт и сталь у базы, тот же
    /// стек другой краской у форм) и раскладка полумесяца маха.
    ///
    /// Якорная семья отличается от сабли МАТЕРИАЛОМ, не техникой: кобальт и сталь вместо бирюзы, горячая холодная
    /// кромка вместо пены, грани и штрихи вместо пузырьков, искры, сколы и комья вместо капель и клочьев.
    /// Звеньев цепи в волне нет (владелец: нечитаемо, «мыльно») — цепь читается настоящей цепью в 3D.
    /// </summary>
    public static class PelagWreckComboLook
    {
        /// <summary>Палитра волны: глубина у героя, основной тон, светлая сталь, горячая кромка (sRGB, 0…1+), чернила.</summary>
        public struct Palette
        {
            public float DeepR, DeepG, DeepB, MidR, MidG, MidB, LightR, LightG, LightB, HotR, HotG, HotB;
        }

        /// <summary>Чернила обвода — тёмно-синие, как у волны сабли и контуров врагов, не чистый чёрный.</summary>
        public const int InkHex = 0x060B22;

        /// <summary>
        /// Основной тон формы: база — кобальт #2E6FE0; Волнорез #1FB37E, Девятый вал #4B3FD0, Призрачный якорь —
        /// фиолетовый его якоря #8E7BFF (07.10; раньше бледный #A99CF0 — махи читались жемчугом «Якорной брони»).
        /// </summary>
        public static int MidHex(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WreckBreakwater: return 0x1FB37E;
                case PelagForm.WreckNinthWave: return 0x4B3FD0;
                case PelagForm.WreckGhostAnchor: return PelagWreckGhostLook.BandHex;
                default: return 0x2E6FE0;
            }
        }

        /// <summary>Палитра формы: у базы — заданные цвета (#0A1F4A, #2E6FE0, #8FC4FF, почти белый холодный).</summary>
        public static Palette For(PelagForm form)
        {
            var p = new Palette();
            switch (form)
            {
                case PelagForm.WreckBreakwater:
                    Set(0x06302A, 0x1FB37E, 0x8FF0CF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.02f; p.HotG = 1.24f; p.HotB = 1.14f;
                    break;
                case PelagForm.WreckNinthWave:
                    Set(0x120C40, 0x4B3FD0, 0xA9A0FF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.10f; p.HotG = 1.08f; p.HotB = 1.32f;
                    break;
                case PelagForm.WreckGhostAnchor:
                    // Принятый выпад Призрачного якоря (всплеск, знак задетого выпадом) — в этой палитре; махи — ForSwing.
                    Set(0x2C2458, 0xA99CF0, 0xE2DCFF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.22f; p.HotG = 1.20f; p.HotB = 1.32f;
                    break;
                default:
                    Set(0x0A1F4A, 0x2E6FE0, 0x8FC4FF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = .92f; p.HotG = 1.06f; p.HotB = 1.40f;
                    break;
            }
            return p;
        }

        /// <summary>
        /// Палитра МАХОВ (волна, эхо, искры, знак задетого махом). 07.10: Призрачный якорь — палитра его якоря
        /// (PelagWreckGhostLook): тень #5A48D8, полосы #8E7BFF, тело #D9CCFF, кромка — холодная белая около 1 (раньше
        /// бледный #A99CF0 — махи читались жемчугом «Якорной брони»). Выпад формы остаётся в For — он принят как был.
        /// </summary>
        public static Palette ForSwing(PelagForm form)
        {
            if (form != PelagForm.WreckGhostAnchor) return For(form);
            var p = new Palette();
            Set(PelagWreckGhostLook.ShadowHex, PelagWreckGhostLook.BandHex, PelagWreckGhostLook.CoreHex, out p.DeepR, out p.DeepG,
                out p.DeepB, out p.MidR, out p.MidG, out p.MidB, out p.LightR, out p.LightG, out p.LightB);
            p.HotR = .94f; p.HotG = .92f; p.HotB = 1.26f;
            return p;
        }

        /// <summary>
        /// Призрачный якорь (07.10): у его махов чернил нет, как у самого якоря, — край волны, искр и сколов светится
        /// фиолетовым (sRGB, альфа обвода), второй тон горячей кромки — сиреневый, не голубой материала.
        /// </summary>
        public const int GhostOutlineHex = 0x6E5BF2, GhostHotShadeHex = 0xB4A6FF;
        public const float GhostOutlineAlpha = .85f;

        /// <summary>Тень у края искр и вспышки знака Призрачного якоря (вместо голубой тени материала).</summary>
        public const int GhostBitShadeHex = 0xD2C8FF;

        // ---------------------------------------------------------------- сколы с края полумесяца

        /// <summary>Сколы с края волны (07.10: «чёрное конфетти» — реже, крупнее, светлее): число (мах 1 / мах 2), размер, м.</summary>
        public static readonly int[] ChipCount = { 3, 4 };
        public const float ChipSizeMin = .22f, ChipSizeMax = .36f;

        /// <summary>
        /// Сколы знака на задетом МАХОМ (были 3 по .15–.24 м, тёмные): число и размер, м; грань и скос — как у края
        /// (контроллер ставит их только знаку маха; у задетых выпадом знак прежний — Mark*).
        /// </summary>
        public const int HitChipCount = 2;
        public const float HitChipSizeMin = .19f, HitChipSizeMax = .30f;

        /// <summary>
        /// Комья земли знака маха (были 3 по .16–.26 м почти чёрные — лежали на полумесяце тёмными дырами): число, размер,
        /// м, грань — светлее бурая земля (линейные; материал комьев общий с выпадом — не трогается).
        /// </summary>
        public const int HitClodCount = 2;
        public const float HitClodSizeMin = .19f, HitClodSizeMax = .30f;
        public static readonly float[] HitClodDark = { .20f, .13f, .07f }, HitClodLight = { .28f, .19f, .11f };

        /// <summary>
        /// Принятый знак задетого выпадом (и префаб знака по умолчанию): 3 скола .15–.24 м тёмной стали, 3 кома .16–.26 м
        /// тёмной земли (линейные), материал сколов M_WreckCombo_Chip. Махи их не трогают — свои Hit*.
        /// </summary>
        public const int MarkChipCount = 3, MarkClodCount = 3;
        public const float MarkChipSizeMin = .15f, MarkChipSizeMax = .24f, MarkClodSizeMin = .16f, MarkClodSizeMax = .26f;
        public static readonly float[] MarkChipDark = { .06f, .075f, .11f }, MarkChipLight = { .10f, .13f, .20f };
        public static readonly float[] MarkClodDark = { .12f, .07f, .035f }, MarkClodLight = { .17f, .10f, .05f };

        /// <summary>Грань скола — сталь средней светлоты (линейные цвета частиц: шейдер их не переводит), тёмная и светлая.</summary>
        public static readonly float[] ChipFaceDark = { .13f, .16f, .22f }, ChipFaceLight = { .19f, .23f, .31f };

        /// <summary>Скос скола — добавка к цвету грани ×1,9 (sRGB материала): светлая сталь.</summary>
        public static readonly float[] ChipBevelAdd = { .56f, .62f, .74f };

        /// <summary>Грань скола Призрачного якоря — фиолетовая сталь (линейные).</summary>
        public static readonly float[] GhostChipDark = { .20f, .16f, .42f }, GhostChipLight = { .28f, .23f, .55f };

        private static void Set(int deep, int mid, int light, out float dr, out float dg, out float db,
            out float mr, out float mg, out float mb, out float lr, out float lg, out float lb)
        {
            PelagWreckSwingLook.Rgb(deep, out dr, out dg, out db);
            PelagWreckSwingLook.Rgb(mid, out mr, out mg, out mb);
            PelagWreckSwingLook.Rgb(light, out lr, out lg, out lb);
        }

        // ---------------------------------------------------------------- раскладка маха

        /// <summary>
        /// Высота плоскости маха над землёй, м (индекс 0 — мах 1, 1 — мах 2 и «Четвёртый»): голова якоря на цепи идёт
        /// чуть ниже плеч на махе 1 и у пояса на обратном махе 2.
        /// </summary>
        public static readonly float[] Height = { 1.02f, .90f };

        /// <summary>Крен плоскости вокруг оси удара, град (+ — голова полумесяца ниже хвоста), как у волны сабли.</summary>
        public static readonly float[] Roll = { 8f, 6f };

        /// <summary>
        /// Наружный край — доля сектора махов Sim (Radius сборки, 2,8 м): на 0,82 сталь идёт перед телами задетых и
        /// касается их (как у сабли); мах 2 чуть больше.
        /// </summary>
        public static readonly float[] Reach = { .82f, .87f };

        /// <summary>Центр полумесяца позади героя, м: тупые концы обнимают его бока.</summary>
        public const float Back = .15f;

        /// <summary>Наружный радиус полумесяца, м, по сектору Sim.</summary>
        public static float Radius(int side, float sector) => sector * Reach[side] + Back;

        /// <summary>Знак на задетом: масштаб лёгкого (мах 1) и тяжёлого (мах 2, «Четвёртый»).</summary>
        public static readonly float[] HitScale = { 1f, 1.25f };

        // ---------------------------------------------------------------- заход и голова маха (07.10)

        /// <summary>
        /// Заход до контакта, тиков: голова Sim входит в сектор за 3 тика до контакта (Simulation.WreckSweepFirstOffset) и
        /// бьёт тех, чей угол проходит, — волна рождается тогда же и идёт головой от края входа к середине к тику контакта.
        /// Без захода тех, кто стоял на стороне входа, вспышка накрывала раньше волны (мах 2 «мимо цели»).
        /// </summary>
        public const int LeadTicks = -Simulation.WreckSweepFirstOffset;

        public static float LeadSeconds => LeadTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Голова в начале захода — хвост полумесяца (u 0, 75° от удара на стороне входа).</summary>
        public const float LeadFrom = 0f;

        /// <summary>Размах полумесяца, град: u → азимут от удара в сторону головы = SpanDegrees · (u − ½).</summary>
        public const float SpanDegrees = 150f;

        /// <summary>Доворот по ходу маха после контакта, град (мах 1 / мах 2), и его срок, с.</summary>
        public static readonly float[] SpinDegrees = { 12f, 14f };
        public const float SpinSeconds = .26f;

        /// <summary>
        /// Азимут головы полумесяца, град от удара в сторону головы, через <paramref name="t"/> с от рождения частицы: заход
        /// <paramref name="lead"/> с — линейно от leadFrom к headFrom; после контакта — к 1,02 за headSeconds с замедлением
        /// (как шейдер «Razlom/Wreck Combo Wave») плюс доворот.
        /// </summary>
        public static float HeadAzimuth(float t, float lead, float leadFrom, float headFrom, float headSeconds, float spinDegrees)
        {
            float age = t - lead;
            float u;
            if (age < 0f) u = leadFrom + (headFrom - leadFrom) * Clamp01(1f + age / (lead > .001f ? lead : .001f));
            else
            {
                float k = Clamp01(age / (headSeconds > .01f ? headSeconds : .01f));
                u = headFrom + (1.02f - headFrom) * (1f - (float)System.Math.Pow(1f - k, 2.2));
            }
            return SpanDegrees * (u - .5f) + Spin(age, spinDegrees);
        }

        /// <summary>
        /// Доворот к <paramref name="age"/> с после контакта: интеграл угловой скорости кривой сборки (пик в контакт, к
        /// половине срока — треть, к концу — 0; у ключей с плоскими касательными интеграл как у ломаной).
        /// </summary>
        public static float Spin(float age, float degrees)
        {
            if (age <= 0f) return 0f;
            float e = SpinSeconds, h = e * .5f, peak = degrees / (e * .45f), mid = peak * .33f;
            if (age < h) return (peak + peak + (mid - peak) * age / h) * .5f * age;
            float a = (age < e ? age : e) - h;
            return (peak + mid) * .5f * h + (mid + mid * (1f - a / (e - h))) * .5f * a;
        }

        /// <summary>
        /// Через сколько секунд от рождения частицы голова дойдёт до азимута <paramref name="azimuth"/> (град, + — к голове):
        /// 0 — цель накрыта уже при рождении; дальше конца маха — конец раскрытия.
        /// </summary>
        public static float ArrivalSeconds(float azimuth, float lead, float leadFrom, float headFrom, float headSeconds, float spinDegrees)
        {
            float end = lead + headSeconds + .05f;
            for (float t = 0f; t < end; t += .002f)
                if (HeadAzimuth(t, lead, leadFrom, headFrom, headSeconds, spinDegrees) >= azimuth) return t;
            return end;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
