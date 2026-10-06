using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — «ТЕРНОВНИК», ВИД (контракт artifacts/tools/wf/boss-tempo-contract.md § 17.2, владелец 08.10: «шиповые
    /// семена надо сделать не от босса, а чтобы появлялось 2-3 куста на арене и от них исходило по 4 шипа… а потом кусты
    /// исчезали»). Заменил вид «Веера шипов-семян» (§ 16, V16). Без UnityEngine: правила проверяют тесты вне Unity
    /// (tools/Combat.Presentation.Tests/ThicketMasterSeedRulesTests.cs), в том числе на живой симуляции — крест вида ровно
    /// линии Sim, остриё вида в целые тики ровно остриё Sim (Simulation.ThicketSeedTip), сроки куста держат Часы.
    ///
    /// Время — тики Sim с долей кадра (Tick − 1 + Alpha), часы босса ThicketMasterClipRules.BossClock: Песочные Часы держат
    /// кусты и шипы вместе с боссом. Под Часами часы босса прыгают на всю заморозку вперёд и стоят, а Sim сдвигает только
    /// сроки впереди — поэтому каждый отрезок жизни куста считается от СЛЕДУЮЩЕГО срока, который Sim ещё сдвигает:
    /// рост — от выпуска (<see cref="GrowAge"/>), стойка — от увядания (<see cref="LaunchAge"/>), увядание — от ухода
    /// (<see cref="WitherAge"/>). Кадр под Часами стоит, после них идёт дальше без скачка.
    ///
    /// • Жест — клип каста прорастания (ThicketMasterClipRules: Seeds — тот же отрезок, что Sprout), лапы в землю.
    /// • До прорастания (кусты 1–2 прорастают через 9 / 18 тиков) земля на месте куста шевелится (<see cref="Stir"/>).
    /// • Прорастание (SproutTick): земля рвётся, куст выходит из неё с перелётом (<see cref="Emerge"/>), стебли с шипами —
    ///   вразнобой (<see cref="CaneEmerge"/>); на 4 сторонах креста в кусте набухают стручки-шипы (<see cref="PodWindupScale"/>).
    /// • Замах (30 тиков до выпуска): куст пухнет (<see cref="Swell"/>), 4 больших шипа по линиям встают дыбом и вытягиваются
    ///   (<see cref="Bristle"/>), к концу куст дрожит (<see cref="Quiver"/>), за <see cref="GlintTicks"/> до выпуска — блик.
    /// • Выпуск (LaunchTick): куст отдаёт (<see cref="Recoil"/>), шипы по линиям опадают (<see cref="LaneSnap"/>), стручки
    ///   срываются по линиям — вдоль пути ровно остриё Sim (<see cref="TipDistance"/>).
    /// • Увядание (WitherTick … GoneTick): сохнет, никнет, листья сыплются, уходит в землю (<see cref="Wither"/>). Куст пропал
    ///   из Sim раньше (смерть, рестарт) — вянет сам за <see cref="LostWitherTicks"/>.
    /// </summary>
    public static class ThicketMasterSeedRules
    {
        // ================================================================ шип в полёте (стручок)

        /// <summary>Скорость шипа Sim, м за тик (13/30).</summary>
        public static readonly float Speed = Simulation.ThicketSeedSpeed.ToFloat();

        /// <summary>Мест в пуле стручков вида: два каста по ThicketSeedSlots (3 куста × 4) — прошлый ещё доигрывает остановку.</summary>
        public const int PodPool = Simulation.ThicketSeedSlots * 2;

        /// <summary>
        /// Масштаб стручка (сетка ThicketSeedPod: ядро 1 × 1 × 1,2, с шипами ~1,8): ядро 0,36 м, с шипами ~0,65 м — шип куста
        /// (у веера было 0,46: на поле разом до 12 шипов, линия 0,5 м).
        /// </summary>
        public const float PodScale = .36f;

        /// <summary>Полупоперечник ядра стручка в мире, м: ниже высоты полёта — стручок не режет землю.</summary>
        public const float PodRadius = .5f * PodScale;

        /// <summary>Высота середины стручка в полёте, м, и подскок над ней (касание земли раз в HopLength м).</summary>
        public const float FlightLift = .26f, HopHeight = .12f, HopLength = 1.5f;

        /// <summary>За столько метров пути стручок сходит из устья куста на линию (высота полёта, ось линии).</summary>
        public const float LaunchEaseMetres = .6f;

        /// <summary>Дуга схода: подъём над прямой устье → линия посередине схода, м.</summary>
        public const float PopLift = .12f;

        /// <summary>Кувырок стручка, градусов на метр пути (13 м/с — ~900°/с).</summary>
        public const float SpinDegreesPerMetre = 70f;

        /// <summary>Попал — лопается за ShatterTicks; конец линии — клюёт и уходит в землю за DropTicks; снят — GoneTicks.</summary>
        public const float ShatterTicks = 3f, DropTicks = 10f, GoneTicks = 4f;

        /// <summary>
        /// Сколько остриё прошло от начала пути к моменту tick на часах Sim (тик − 1 + Alpha): в тик выпуска — от 0 до
        /// Speed, дальше по Speed за тик, но не дальше насчитанного Sim (travelled — к концу прошлого шага) и не ближе
        /// одного тика позади него (Часы сдвигают выпуск вперёд — остриё не отскакивает назад дальше тика), не дальше
        /// длины пути. В целые тики (Alpha 0 и 1) — ровно Travelled Sim до и после шага.
        /// </summary>
        public static float TipDistance(int releaseTick, float travelled, float length, float tick)
        {
            float lead = Speed * (tick - releaseTick);
            float low = Math.Max(0f, travelled - Speed);
            float d = lead < low ? low : lead > travelled ? travelled : lead;
            return Math.Max(0f, Math.Min(d, length));
        }

        /// <summary>Остриё вставшего шипа: идёт по своему расписанию до точки остановки stop (м пути) и там стоит.</summary>
        public static float StoppedTip(int releaseTick, float stop, float tick)
            => Math.Max(0f, Math.Min(stop, Speed * (tick - releaseTick)));

        /// <summary>Когда остриё вставшего шипа приходит в точку остановки: не раньше тика события.</summary>
        public static float StopTick(int releaseTick, float stop, int eventTick)
            => Math.Max(eventTick, releaseTick + Math.Max(0f, stop) / Speed);

        /// <summary>Доля сдвига от устья куста на distance метров пути: 1 на выпуске, 0 с LaunchEaseMetres (плавно).</summary>
        public static float LaunchShare(float distance)
        {
            float s = Clamp01(distance / LaunchEaseMetres);
            return 1f - s * s * (3f - 2f * s);
        }

        /// <summary>Дуга схода на distance метров пути: 0 на выпуске и с LaunchEaseMetres, PopLift посередине.</summary>
        public static float PopArc(float distance)
        {
            float s = Clamp01(distance / LaunchEaseMetres);
            return PopLift * 4f * s * (1f - s);
        }

        /// <summary>Высота середины стручка над землёй на distance метров пути (без сдвига от устья): подскоки после схода.</summary>
        public static float Lift(float distance)
        {
            if (distance <= LaunchEaseMetres) return FlightLift;
            return FlightLift + HopHeight * Math.Abs((float)Math.Sin(Math.PI * (distance - LaunchEaseMetres) / HopLength));
        }

        /// <summary>Метр пути k-го касания земли (k ≥ 1): там подскок встаёт на линию и бьёт пыль.</summary>
        public static float HopTouch(int k) => LaunchEaseMetres + k * HopLength;

        /// <summary>Кувырок стручка, градусов, на distance метров пути (по пути, а не по времени: стоит со стручком).</summary>
        public static float SpinDegrees(float distance) => distance * SpinDegreesPerMetre;

        /// <summary>Попал: стручок лопается о тело героя — после остановки after тиков (0 — исчез).</summary>
        public static float ShatterScale(float after)
        {
            if (after <= 0f) return 1f;
            float k = Clamp01(after / ShatterTicks);
            return 1f - k * k;
        }

        /// <summary>
        /// Конец линии без попадания: стручок клюёт вперёд (pitch, градусы), опускается до земли и в неё (sink — м вниз от
        /// высоты полёта), к концу сжимается (scale).
        /// </summary>
        public static void Drop(float after, out float pitch, out float sink, out float scale)
        {
            float k = Clamp01(Math.Max(0f, after) / DropTicks);
            pitch = 55f * (float)Math.Sqrt(k);
            sink = (FlightLift + PodRadius) * k * k;
            scale = k < .7f ? 1f : 1f - (k - .7f) / .3f;
        }

        /// <summary>Снят без удара: сжимается за GoneTicks (плавно), 0 — исчез.</summary>
        public static float GoneScale(float after)
        {
            float k = Clamp01(Math.Max(0f, after) / GoneTicks);
            return 1f - k * k * (3f - 2f * k);
        }

        // ================================================================ куст: место и крест

        /// <summary>Мест в пуле кустов вида: два каста по ThicketBushSlots — кусты прошлого каста ещё могут вянуть сами.</summary>
        public const int BushPool = Simulation.ThicketBushSlots * 2;

        /// <summary>Куст шипа (место куста, ThicketBushState.Order) по месту шипа в касте ThicketSeedState.Index.</summary>
        public static int BushOf(int index) => index / Simulation.ThicketBushLanes;

        /// <summary>Линия куста (0–3) по месту шипа в касте ThicketSeedState.Index.</summary>
        public static int LaneOf(int index) => index % Simulation.ThicketBushLanes;

        /// <summary>
        /// Направление линии lane в осях Sim (x, y) по оси креста (axisX, axisY): ось, повёрнутая на lane × 90° против часовой —
        /// то же, что Simulation.ThicketBushLaneDirection.
        /// </summary>
        public static void LaneDirection(float axisX, float axisY, int lane, out float x, out float y)
        {
            switch (lane & 3)
            {
                case 1: x = -axisY; y = axisX; break;
                case 2: x = -axisX; y = -axisY; break;
                case 3: x = axisY; y = -axisX; break;
                default: x = axisX; y = axisY; break;
            }
        }

        /// <summary>
        /// Поворот корня куста вокруг вертикали (градусы Unity: 0 — мировая +Z = ось Sim +Y, 90 — +X): +Z префаба — линия 0
        /// (ось креста ThicketBushState.Axis).
        /// </summary>
        public static float AxisYawDegrees(float axisX, float axisY) => (float)(Math.Atan2(axisX, axisY) * 180.0 / Math.PI);

        /// <summary>
        /// Поворот линии lane в префабе куста (градусы Unity вокруг +Y от +Z): линия k — линия 0, повёрнутая против часовой
        /// в осях Sim, а в Unity положительный поворот вокруг +Y — по часовой, если смотреть сверху: −90° × k.
        /// </summary>
        public static float LaneYawDegrees(int lane) => -90f * (lane & 3);

        /// <summary>Мировое направление (x, z) поворота Unity yaw вокруг +Y (Quaternion.Euler(0, yaw, 0) · +Z).</summary>
        public static void YawDirection(float yawDegrees, out float x, out float z)
        {
            double a = yawDegrees * Math.PI / 180.0;
            x = (float)Math.Sin(a);
            z = (float)Math.Cos(a);
        }

        /// <summary>
        /// Устье линии в кусте: стручок набухает здесь, на краю листвы под большим шипом линии (м от центра по линии и над
        /// землёй) — снаружи куста виден со всех сторон камеры боя; линия Sim начинается ближе (ThicketBushThornStart 0,3).
        /// </summary>
        public const float MouthRadius = .5f, MouthLift = .3f;

        // ================================================================ куст: время

        /// <summary>Отрезок роста от выпуска назад (рост идёт ThicketBushWindupTicks): 0 — прорастание, Windup — выпуск.</summary>
        public static float GrowAge(float clock, int launchTick) => clock - (launchTick - Simulation.ThicketBushWindupTicks);

        /// <summary>Сколько прошло с выпуска — от увядания назад (Часы сдвигают WitherTick, а прошедший выпуск — нет).</summary>
        public static float LaunchAge(float clock, int witherTick) => clock - (witherTick - Simulation.ThicketBushStandTicks);

        /// <summary>Сколько куст вянет — от ухода назад (Часы сдвигают GoneTick).</summary>
        public static float WitherAge(float clock, int goneTick) => clock - (goneTick - Simulation.ThicketBushWitherTicks);

        /// <summary>Рост замаха, тиков: Simulation.ThicketBushWindupTicks (30).</summary>
        public const float WindupTicks = Simulation.ThicketBushWindupTicks;

        /// <summary>Куст выходит из земли за столько тиков (с перелётом); стебли — вразнобой ещё CaneStaggerTicks.</summary>
        public const float SproutTicks = 7f, CaneStaggerTicks = 4f;

        /// <summary>Земля на месте куста шевелится столько тиков до прорастания (кусты 1–2: 9 и 18 тиков после жеста).</summary>
        public const float StirTicks = 9f;

        /// <summary>Куст пухнет к выпуску до SwellMax от своего размера.</summary>
        public const float SwellMax = 1.14f;

        /// <summary>Шипы линий: доля длины при выходе из земли (дальше вытягиваются к выпуску), после выпуска — опали.</summary>
        public const float BristleStart = .3f, SpentExtension = .7f;

        /// <summary>Дрожь куста — последние QuiverTicks замаха (нарастает к выпуску).</summary>
        public const float QuiverTicks = 10f;

        /// <summary>Блик перед выпуском: за столько тиков до выпуска, пик — посередине.</summary>
        public const int GlintTicks = 6;

        /// <summary>Стручок в устье: появляется после PodAppearTicks роста (куст уже вышел), почка PodBud → полный к выпуску.</summary>
        public const float PodAppearTicks = 4f, PodBud = .3f;

        /// <summary>Отдача куста на выпуске: пик к RecoilPeakTicks, обратно к RecoilTicks.</summary>
        public const float RecoilPeakTicks = 1.5f, RecoilTicks = 7f;

        /// <summary>Шипы линий опадают за столько тиков после выпуска.</summary>
        public const float SnapTicks = 4f;

        /// <summary>Увядание Sim — ThicketBushWitherTicks (24); куст пропал из Sim раньше — вянет сам за LostWitherTicks.</summary>
        public const float WitherTicks = Simulation.ThicketBushWitherTicks, LostWitherTicks = 12f;

        /// <summary>Увядший куст уходит в землю на столько метров (стебли с шипами — до ~1,4 м над землёй, к концу ×0,6).</summary>
        public const float SinkDepth = 1.2f;

        /// <summary>Никнет: стебли и шипы клонятся наружу и вниз до стольких градусов.</summary>
        public const float DroopDegrees = 38f;

        /// <summary>Земля шевелится до прорастания: 0 раньше StirTicks до SproutTick, к прорастанию — 1.</summary>
        public static float Stir(float clock, int sproutTick)
        {
            float x = clock - (sproutTick - StirTicks);
            if (x <= 0f || clock >= sproutTick) return 0f;
            float u = Clamp01(x / StirTicks);
            return u * u;
        }

        /// <summary>
        /// Куст выходит из земли (доля роста, с перелётом ~8 % и возвратом — ease-out back): 0 до прорастания, 1 с SproutTicks.
        /// </summary>
        public static float Emerge(float growAge) => Rise(growAge / SproutTicks);

        /// <summary>Стебель cane из canes выходит со своей задержкой (до CaneStaggerTicks), за SproutTicks.</summary>
        public static float CaneEmerge(float growAge, int cane, int canes)
        {
            float delay = canes <= 1 ? 0f : CaneStaggerTicks * ((cane * 7) % canes) / (canes - 1f);
            return Rise((growAge - delay) / SproutTicks);
        }

        /// <summary>Куст пухнет: 1 до выхода из земли, к выпуску — SwellMax (быстрее к концу); после выпуска — 1.</summary>
        public static float Swell(float growAge)
        {
            if (growAge >= WindupTicks) return 1f;
            float u = Clamp01((growAge - SproutTicks) / (WindupTicks - SproutTicks));
            return 1f + (SwellMax - 1f) * (float)Math.Pow(u, 1.6);
        }

        /// <summary>Шипы линий встают дыбом: доля длины BristleStart при выходе из земли → 1 к выпуску (ускоряясь).</summary>
        public static float Bristle(float growAge)
        {
            float u = Clamp01((growAge - SproutTicks * .5f) / (WindupTicks - SproutTicks * .5f));
            return BristleStart + (1f - BristleStart) * u * u * (3f - 2f * u);
        }

        /// <summary>Дрожь куста 0…1: последние QuiverTicks замаха, нарастает к выпуску; вне окна — 0.</summary>
        public static float Quiver(float growAge)
        {
            if (growAge >= WindupTicks) return 0f;
            float u = Clamp01((growAge - (WindupTicks - QuiverTicks)) / QuiverTicks);
            return u * u;
        }

        /// <summary>Блик перед выпуском 0…1: GlintTicks до launch, пик посередине, вне окна — 0.</summary>
        public static float Glint(float tick, int launch)
        {
            float x = tick - (launch - GlintTicks);
            if (x <= 0f || x >= GlintTicks) return 0f;
            return (float)Math.Sin(Math.PI * x / GlintTicks);
        }

        /// <summary>
        /// Размер стручка в устье (доля полного) на отрезке роста: появляется с PodAppearTicks за 3 тика, растёт от PodBud к 1
        /// (быстрее к концу), к выпуску — полный, как в полёте (без скачка размера на выпуске).
        /// </summary>
        public static float PodWindupScale(float growAge)
        {
            float x = growAge - PodAppearTicks;
            if (x <= 0f) return 0f;
            float u = Clamp01(x / (WindupTicks - PodAppearTicks));
            float swell = PodBud + (1f - PodBud) * (float)Math.Pow(u, 1.4);
            float appear = Clamp01(x / 3f);
            appear = appear * appear * (3f - 2f * appear);
            return swell * appear;
        }

        /// <summary>Отдача куста на выпуске 0…1…0: пик к RecoilPeakTicks, обратно к RecoilTicks; до выпуска и после — 0.</summary>
        public static float Recoil(float launchAge)
        {
            if (launchAge <= 0f || launchAge >= RecoilTicks) return 0f;
            if (launchAge < RecoilPeakTicks)
            {
                float a = launchAge / RecoilPeakTicks;
                return a * (2f - a);
            }
            float u = (launchAge - RecoilPeakTicks) / (RecoilTicks - RecoilPeakTicks);
            return (1f - u) * (1f - u);
        }

        /// <summary>Шипы линий после выпуска: доля длины 1 → SpentExtension за SnapTicks (выпустили — опали).</summary>
        public static float LaneSnap(float launchAge)
        {
            float u = Clamp01(launchAge / SnapTicks);
            return 1f - (1f - SpentExtension) * u * (2f - u);
        }

        /// <summary>
        /// Увядание на witherAge тиков (0 — WitherTick, WitherTicks — GoneTick): dry — сохнет (тон к сухому, первые 45 %),
        /// droop — никнет (стебли клонятся до DroopDegrees, к 70 %), sink — уходит в землю (доля SinkDepth, с 30 %, плавно),
        /// scale — съёживается к ×0,6 (с 45 %). В конце (sink 1, scale 0,6) куста над землёй нет.
        /// </summary>
        public static void Wither(float witherAge, out float dry, out float droop, out float sink, out float scale)
        {
            float w = Clamp01(witherAge / WitherTicks);
            dry = Smooth(Clamp01(w / .45f));
            droop = Smooth(Clamp01(w / .7f));
            sink = Smooth(Clamp01((w - .3f) / .7f));
            scale = 1f - .4f * Smooth(Clamp01((w - .45f) / .55f));
        }

        /// <summary>Куст пропал из Sim раньше GoneTick (смерть, рестарт): то же увядание, но за LostWitherTicks от пропажи.</summary>
        public static float LostWitherAge(float sinceLost) => Math.Max(0f, sinceLost) * (WitherTicks / LostWitherTicks);

        /// <summary>Куст увял целиком — вид снимает его.</summary>
        public static bool Withered(float witherAge) => witherAge >= WitherTicks;

        /// <summary>Выход из земли: быстрый, с перелётом ~8 % и возвратом (ease-out back); 0 при x ≤ 0, 1 при x ≥ 1.</summary>
        public static float Rise(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float c1 = 1.25f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
