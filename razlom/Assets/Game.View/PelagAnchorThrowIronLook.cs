using System;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — БАЗА «холодное железо» якорной семьи (07.10; целевые кадры ART/characters/pelag/anchor-throw-2026-10-07/
    /// chatgpt-results/base-hit-v1.png и base-yank-v1.png; ассеты — Editor/PelagWreckComboVfxSetup.ThrowIron*, вид —
    /// PelagVfxController.AnchorThrowIron*). Чистые числа без Unity (тесты — Combat.Presentation.Tests/AnchorThrowIronLookTests).
    /// Палитра — та же, что у Крушения на стеке сабли (PelagWreckComboLook): глубина #0A1F4A, кобальт #2E6FE0, сталь #8FC4FF,
    /// почти белая холодная сердцевина; край светящихся вещей — тёмный насыщенный кобальт, не чёрные чернила.
    /// Размеры — от Sim (линия 7 м), не от кадра (на кадре линия ~12 м).
    /// </summary>
    public static class PelagAnchorThrowIronLook
    {
        public const float TicksPerSecond = 30f;

        // ---------------------------------------------------------------- палитра (sRGB)

        public const int DeepHex = 0x0A1F4A, MidHex = 0x2E6FE0, LightHex = 0x8FC4FF;
        /// <summary>Светящийся край ленты, звёзд и искр: тёмный насыщенный кобальт (не чернила).</summary>
        public const int EdgeHex = 0x1A44B8;
        /// <summary>
        /// Край сколов железа: тёмный кобальт, светлее чернил Крушения (не «чёрное конфетти»), но не яркий — V1 с #16307A
        /// в игре читался синими квадратиками, V5 с #10265E — синими ободками.
        /// </summary>
        public const int ChipEdgeHex = 0x0C1A44;

        // ---------------------------------------------------------------- струна вдоль цепи

        /// <summary>Полуширина у головы в полёте, м; к хвосту (TailMeters позади головы) — TailHalf.</summary>
        public const float HeadHalf = .15f, TailHalf = .065f, TailMeters = 4.5f;
        /// <summary>У хвата лента сходит на нет на этой длине, м; у кольца головы — скругление.</summary>
        public const float GripTaper = .45f, HeadCap = .14f;
        /// <summary>Натянутая цепь (держит, рывок, возврат): полуширина, м, и прибавка в импульс натяга.</summary>
        public const float TautHalf = .085f, PulseHalf = .07f;
        /// <summary>Импульс натяга бежит от головы к руке, м/с (7 м за ~0,1 с), ширина пятна, м.</summary>
        public const float PulseSpeed = 70f, PulseWidth = .8f;
        /// <summary>Ореол — шире ленты во столько раз (глубокий кобальт полупрозрачно), его непрозрачность.</summary>
        public const float HaloScale = 2.1f, HaloOpacity = .88f;
        /// <summary>Шаг ленты вдоль цепи, м; рисунок шейдера: u = метры от кольца / столько.</summary>
        public const float SegmentMeters = .10f, MetersPerU = 4f;
        /// <summary>Жар натянутой цепи (ширина сердцевины по всей длине): «держит» и рывок светятся целиком.</summary>
        public const float TautHeat = .72f;

        /// <summary>Сглаженная ступень 0…1.</summary>
        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        /// <summary>Сход на нет у хвата: 0 в хвате, 1 через GripTaper.</summary>
        public static float GripFactor(float fromGrip) => Smooth01(fromGrip / GripTaper);

        /// <summary>Скругление у кольца головы: 0 в кольце, 1 через HeadCap (голова закрывает конец).</summary>
        public static float HeadFactor(float behindHead) => (float)Math.Sqrt(Clamp01(behindHead / HeadCap));

        /// <summary>
        /// Полёт (base-hit-v1): «комета» за головой — широкая и горячая у головы, к хвосту тоньше, у хвата сходит на нет.
        /// <paramref name="behind"/> — метры от кольца назад по цепи, <paramref name="fromGrip"/> — метры от хвата.
        /// </summary>
        public static float FlightHalf(float behind, float fromGrip)
            => Lerp(HeadHalf, TailHalf, (float)Math.Pow(Clamp01(behind / TailMeters), .7)) * HeadFactor(behind) * GripFactor(fromGrip);

        /// <summary>Жар в полёте: 1 у головы, к хвосту 0,25 (сердцевина сходит в нитку).</summary>
        public static float FlightHeat(float behind) => Lerp(1f, .25f, behind / TailMeters);

        /// <summary>Импульс натяга: пятно света бежит от головы к руке с рывка (секунды <paramref name="sinceYank"/>).</summary>
        public static float Pulse(float behind, float sinceYank)
        {
            if (sinceYank < 0f) return 0f;
            float at = sinceYank * PulseSpeed;
            float d = (behind - at) / PulseWidth;
            float fade = 1f - Smooth01((sinceYank - .12f) / .10f);
            return (float)Math.Exp(-d * d) * fade;
        }

        /// <summary>Натянутая цепь: ровная полуширина (+ импульс), у хвата — сход, у кольца — скругление.</summary>
        public static float TautHalfAt(float behind, float fromGrip, float pulse)
            => (TautHalf + PulseHalf * pulse) * HeadFactor(behind) * GripFactor(fromGrip);

        /// <summary>Полуширина ленты на переходе «полёт → натянута» (<paramref name="tight"/> 0…1).</summary>
        public static float HalfAt(float behind, float fromGrip, float tight, float pulse)
            => Lerp(FlightHalf(behind, fromGrip), TautHalfAt(behind, fromGrip, pulse), tight);

        /// <summary>Жар на переходе: комета полёта → ровный жар натяга, импульс добавляет.</summary>
        public static float HeatAt(float behind, float tight, float pulse)
            => Clamp01(Lerp(FlightHeat(behind), TautHeat, tight) + .45f * pulse);

        /// <summary>
        /// Доля перехода «полёт → натянута» на тик показа: 0 до HoldTick, к рывку (TautTick) — 1. В позе «держит»
        /// цепь стягивается в ровную струну и разгорается по всей длине.
        /// </summary>
        public static float Tighten(float shown, int holdTick, int tautTick)
            => shown <= holdTick ? 0f : Smooth01((shown - holdTick) / Math.Max(1f, tautTick - holdTick));

        /// <summary>Шевроны рывка: с TautTick появляются за 1 тик, в ловлю гаснут.</summary>
        public static float Chevrons(float shown, int tautTick, int catchTick)
            => shown < tautTick ? 0f : Smooth01(shown - tautTick) * (1f - Smooth01(shown - catchTick + 1f));

        /// <summary>Лента проявляется от руки за тик выпуска.</summary>
        public static float Appear(float shown, int releaseTick) => Smooth01((shown - releaseTick + .15f) / 1.0f);

        /// <summary>Распад струны после ловли или срыва, с: ступень шейдера (_ErodeFrom … _FadeTo ≈ 0,24 с).</summary>
        public static float BreakSeconds(float shown, float fromTick) => shown < fromTick ? 0f : (shown - fromTick) / TicksPerSecond;

        /// <summary>Струна видна до этого возраста распада, с (потом объект в пул).</summary>
        public const float StreakGoneSeconds = .26f;

        // ---------------------------------------------------------------- голова в полёте

        /// <summary>Вспышка-хвост за головой: новая каждые столько секунд (каждый тик), жизнь, размер, м.</summary>
        public const float FlareEvery = 1f / 30f, FlareLife = .10f, FlareWidth = 1.45f, FlareHeight = .62f;
        /// <summary>Центр головы — дальше кольца по цепи на столько, м (вспышки и искры головы).</summary>
        public const float HeadFromRing = .30f;
        /// <summary>Искр с головы в секунду полёта; на рывке — шевроны-искры по цепи к руке в секунду.</summary>
        public const float HeadSparkRate = 110f, ChevronSparkRate = 34f;
        /// <summary>Звезда головы в натяг (поза «держит»: цепь щёлкнула), м; на рывке — меньше.</summary>
        public const float SnapStar = 1.9f, YankStar = 1.15f;
        /// <summary>Шевроны-искры: пары под углом к цепи (остриё — к руке), разлёт в стороны к скорости вдоль цепи.</summary>
        public const float ChevronSpread = .40f;
        /// <summary>Залп шевронов в тик рывка (пар).</summary>
        public const int YankChevronPairs = 8;
        /// <summary>Где рождаются шевроны: доля цепи от головы к руке (у рук гуще — кадр рывка), скорость к руке, м/с.</summary>
        public const float ChevronFrom = .40f, ChevronTo = .94f, ChevronSpeedMin = 6f, ChevronSpeedMax = 9f;

        /// <summary>Доля длины цепи от головы для шеврона: случайное r 0…1 → к руке гуще (квадратный корень).</summary>
        public static float ChevronAt(float r) => Lerp(ChevronFrom, ChevronTo, (float)Math.Sqrt(Clamp01(r)));

        // ---------------------------------------------------------------- знак на задетом

        /// <summary>Масштаб знака: лёгкий (поедет), тяжёлый/элита (стоит, оглушён), сеть Невода, укус Гарпуна.</summary>
        public const float HitLight = 1f, HitHeavy = 1.3f, HitNet = .8f, HitBite = 1.2f;
        /// <summary>Стоп-кадр тела: тяжёлый — 2 кадра, лёгкий — кадр (удар читается до тяги).</summary>
        public const float HoldHeavy = .066f, HoldLight = .033f;
        /// <summary>Настоящих камней из-под ног задетого: лёгкий, тяжёлый.</summary>
        public const int HitRocksLight = 3, HitRocksHeavy = 5;

        /// <summary>Знак у груди: высота над землёй по радиусу тела, м.</summary>
        public static float HitHeight(float bodyRadius) => Math.Min(1.3f, Math.Max(.75f, .5f + .45f * bodyRadius));

        /// <summary>Тяжёлый, элита (не тянется, Sim сразу оглушает) — крупнее и со знаком оглушения.</summary>
        public static float HitScale(bool pulled, bool net) => net ? HitNet : pulled ? HitLight : HitHeavy;

        // ---------------------------------------------------------------- знак оглушения

        /// <summary>Над макушкой, м; радиус орбиты трёх звёзд, м; оборот, с; размер звезды, м.</summary>
        public const float StunAbove = .22f, StunOrbit = .42f, StunPeriod = .7f, StunStar = .30f;
        /// <summary>Сколько звёзд видно: 0 до оглушения, 1 пока оно идёт, за последние 4 тика гаснут.</summary>
        public static float StunOpacity(float shown, int stunUntilTick)
            => Smooth01((stunUntilTick - shown) / 4f);

        // ---------------------------------------------------------------- борозда волока

        /// <summary>Полуширина борозды по радиусу тела, м (шириной в тело, как на кадре рывка).</summary>
        public static float FurrowHalf(float bodyRadius) => Math.Min(.55f, Math.Max(.28f, bodyRadius * .85f));

        /// <summary>Шаг записи пути тела, м; начало борозды скруглено на столько, перед телом — нос.</summary>
        public const float FurrowStep = .12f, FurrowStartTaper = .35f, FurrowNose = .18f;

        /// <summary>Комья у переднего края: спрайт-ком каждые, м; 3D-ком каждые, м; клуб пыли каждые, м.</summary>
        public const float ClodEvery = .22f, RockEvery = .55f, DustEvery = .75f;

        /// <summary>Борозда живёт после остановки тела, с (распад шейдера — по возрасту точки до ~1,8 с).</summary>
        public const float FurrowAfterSeconds = 1.6f;

        /// <summary>Ширина у точки борозды: скругление начала, нос перед телом.</summary>
        public static float FurrowWidthAt(float fromStart, float toNose)
            => (float)Math.Sqrt(Clamp01(fromStart / FurrowStartTaper)) * (float)Math.Sqrt(Clamp01(toNose / FurrowNose + .35f));

        /// <summary>Сколько кусков выпустить на пройденном отрезке: накопитель + метры / шаг.</summary>
        public static int Spend(ref float carry, float meters, float every)
        {
            if (every <= 0f || meters <= 0f) return 0;
            carry += meters / every;
            int n = (int)carry;
            carry -= n;
            return n;
        }

        // ---------------------------------------------------------------- ловля

        /// <summary>Звезда ловли у кулака, м (маленькая: якорь в руке, не взрыв).</summary>
        public const float CatchStar = .62f;
        /// <summary>Доехавший встал: комьев, клубов, камней у ног.</summary>
        public const int LandClods = 5, LandDust = 2, LandRocks = 2;
    }
}
