using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Крушение · ПРИЗРАЧНЫЙ ЯКОРЬ (форма 13; владелец 06.10 вечером; целевой кадр
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-ghost-anchor.png). Чистые числа вида без Unity
    /// (ассеты — Editor/PelagWreckComboVfxSetup.Ghost, рождение — PelagVfxController.WreckGhost).
    ///
    /// Сроки — из Sim: удар выпада (WreckSlam) → через WreckGhostDelayTicks событие WreckGhostAnchor (77) в точке выпада, круг
    /// WreckGhostRadius. Вид: в тик удара высоко над точкой проявляется НАСТОЯЩИЙ 3D якорь Пелага (меш Tripo, 3,5 м),
    /// призрачный — бледно-фиолетово-белый, полосы фиолетового текут вниз, белая горячая кромка, чернильный обвод; падает
    /// с разгоном и входит в землю РОВНО в тик события 77 (сплющивание на 2–3 кадра), стоит ~0,45 с и рассыпается вверх
    /// осколками. Удар: кольцо по земле до радиуса Sim (рваная тонкая линия, шипы разной длины и наклона, белое →
    /// бледно-фиолетовое → фиолетовое), свет земли внутри круга, стоячая полузвезда от земли и лучи вверх, трещины с
    /// фиолетовым светом, тёмные освещённые камни (десятки, от кулака до валуна) и комья с тяжестью, искры, свет.
    /// 07.10 после отзыва владельца («обводка херовая»): у якоря и кольца ЧЕРНИЛ НЕТ — светятся: белая кромка (HDR,
    /// лёгкий bloom), ореол ступенями фиолетового, полосы внутри. Красного, оранжевого и золотого нет; мягких прозрачных
    /// затуханий нет — всё режется порогом.
    /// </summary>
    public static class PelagWreckGhostLook
    {
        // ---------------------------------------------------------------- палитра (sRGB)

        /// <summary>Тело якоря — бледно-фиолетовый белый.</summary>
        public const int CoreHex = 0xD9CCFF;

        /// <summary>Полосы, текущие вниз по якорю, и основной тон кольца — фиолетовый.</summary>
        public const int BandHex = 0x8E7BFF;

        /// <summary>Теневая сторона «стекла» якоря и эхо кольца — глубокий фиолетовый.</summary>
        public const int ShadowHex = 0x5A48D8;

        /// <summary>Контур осколков и искр вместо чернил — тёмный фиолетовый (не чёрный).</summary>
        public const int ContourHex = 0x2A1F6B;

        // ---------------------------------------------------------------- падение (сроки — тики Sim)

        /// <summary>Тиков от удара выпада до падения — ровно Sim (событие 77 приходит в этот тик).</summary>
        public const int FallTicks = Simulation.WreckGhostDelayTicks;

        /// <summary>Падение, с (≈0,27 с).</summary>
        public static float FallSeconds => FallTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Высота низа якоря над землёй в миг проявления, м (по оси якоря): над точкой, в кадре.</summary>
        public const float StartHeight = 5.2f;

        /// <summary>Разгон: высота — StartHeight × (1 − p^FallPower), p — доля падения (0 — проявился, 1 — удар).</summary>
        public const float FallPower = 2.2f;

        /// <summary>Высота низа якоря над землёй по доле падения <paramref name="progress"/> (0…1).</summary>
        public static float FallHeight(float progress)
        {
            float p = Clamp01(progress);
            return StartHeight * (1f - (float)Math.Pow(p, FallPower));
        }

        /// <summary>Якорь высоко «далеко» — мельче: масштаб по доле падения (0,72 → 1).</summary>
        public static float FallScale(float progress) => Lerp(.72f, 1f, Smooth01(Clamp01(progress) * 1.4f));

        /// <summary>Проявление из граней, с: силуэт собирается порогом, не прозрачностью.</summary>
        public const float AppearSeconds = .08f;

        // ---------------------------------------------------------------- якорь

        /// <summary>Высота якоря, м (меш Пелага — к 1 м, низ в начале координат).</summary>
        public const float AnchorHeight = 3.5f;

        /// <summary>Якорь откинут назад от камеры, град: лицом к экрану, а не к небу (на 48° камеры стоячий якорь короче).</summary>
        public const float TiltDegrees = 28f;

        /// <summary>Поворот якоря от лица к камере, град: виден объём лап и ствола.</summary>
        public const float YawDegrees = 14f;

        /// <summary>Лапы в земле в покое, м; в миг удара — глубже (вбит), потом отдача к покою.</summary>
        public const float Sink = .24f, ImpactSink = .40f, SinkSettleSeconds = .07f;

        /// <summary>Сколько лапы ниже земли, м, через <paramref name="t"/> с после удара.</summary>
        public static float SinkAt(float t)
        {
            if (t <= 0f) return ImpactSink;
            return Lerp(ImpactSink, Sink, Smooth01(t / SinkSettleSeconds));
        }

        /// <summary>
        /// Сплющивание удара по высоте якоря через <paramref name="t"/> с после удара: кадр удара .80, через ~2 кадра
        /// перелёт 1.05, к ~4-му кадру 1. Ширина — обратно (объём сохраняется).
        /// </summary>
        public static float SquashY(float t)
        {
            if (t < 0f) return 1f;
            if (t < .035f) return Lerp(.80f, 1.05f, t / .035f);
            if (t < .075f) return Lerp(1.05f, 1f, Smooth01((t - .035f) / .04f));
            return 1f;
        }

        public static float SquashXZ(float t) => 1f / (float)Math.Sqrt(Math.Max(.5f, SquashY(t)));

        /// <summary>Белая вспышка всего якоря в миг удара, с.</summary>
        public const float FlashSeconds = .05f;

        public static float FlashAt(float t) => t < 0f ? 0f : 1f - Smooth01(t / FlashSeconds);

        /// <summary>Стоит в земле, с; потом рассыпается вверх (снизу) за DissolveSeconds.</summary>
        public const float HoldSeconds = .45f, DissolveSeconds = .30f;

        /// <summary>Доля распада якоря через <paramref name="t"/> с после удара (0 — цел, 1 — нет).</summary>
        public static float DissolveAt(float t) => Clamp01((t - HoldSeconds) / DissolveSeconds);

        /// <summary>Якорь целиком прожил после удара, с.</summary>
        public static float AfterLandSeconds => HoldSeconds + DissolveSeconds;

        /// <summary>Не упал (серия сброшена до тика падения): тает в воздухе, с.</summary>
        public const float FizzleSeconds = .16f;

        // ---------------------------------------------------------------- удар оземь

        /// <summary>Кольцо по земле: середина полосы кольца — ровно радиус Sim (событие 77: Amount, см).</summary>
        public static float RingRadius(int amountCentimetres) => amountCentimetres > 0 ? amountCentimetres / 100f : WreckGhostRadiusMeters;

        public static float WreckGhostRadiusMeters => Simulation.WreckGhostRadius.ToFloat();

        /// <summary>Префаб удара собран под этот радиус, м (контроллер масштабирует корень по радиусу события).</summary>
        public const float AuthoredRadius = 3f;

        /// <summary>Кольцо доходит до радиуса за столько секунд (с замедлением, без перелёта за круг Sim).</summary>
        public const float RingExpandSeconds = .12f;

        /// <summary>Доля радиуса кольца через <paramref name="t"/> с после удара: .30 → 1, ease-out, не больше 1.</summary>
        public static float RingFraction(float t)
        {
            if (t <= 0f) return .30f;
            float k = Clamp01(t / RingExpandSeconds);
            return Lerp(.30f, 1f, 1f - (1f - k) * (1f - k) * (1f - k));
        }

        /// <summary>Свет удара (фиолетовый, точечный): вспышка и спад за ~0,5 с.</summary>
        public static float LightAt(float t)
        {
            if (t < 0f) return 0f;
            if (t < .04f) return Lerp(.4f, 1f, t / .04f);
            return 1f - Smooth01((t - .04f) / .55f);
        }

        /// <summary>Толчок камеры на удар (больше выпада .30).</summary>
        public const float CameraPunch = .42f, CameraZoom = .07f;

        /// <summary>Тёмные камни из кратера (десятки, как на целевом кадре): крупные и мелкие, размеры, м.</summary>
        public const int RocksBig = 12, RocksSmall = 28;
        public const float RockBigMin = .42f, RockBigMax = .78f, RockSmallMin = .16f, RockSmallMax = .36f;

        // ---------------------------------------------------------------- геометрия кольца (меш) и маска

        /// <summary>
        /// Кольцо v2 — диск в XZ, u — угол целиком (маска без повторов: шипы не встают зубьями шестерёнки), v = r / DiscOuter
        /// (маска WreckGhost_RingShock, генератор artifacts/wreck/v4/forms-vfx/ghost/tools/gen_ring.py). Линия кольца — на
        /// радиусе 1 (масштаб частицы = радиус Sim), шипы наружу до DiscOuter, свет земли — внутри.
        /// </summary>
        public const float DiscOuter = 1.45f;

        public static float RingBandV => 1f / DiscOuter;

        public static float RingRadiusAtV(float v) => v * DiscOuter;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>sRGB-цвет 0xRRGGBB → компоненты 0…1.</summary>
        public static void Rgb(int hex, out float r, out float g, out float b)
        {
            r = ((hex >> 16) & 255) / 255f;
            g = ((hex >> 8) & 255) / 255f;
            b = (hex & 255) / 255f;
        }
    }
}
