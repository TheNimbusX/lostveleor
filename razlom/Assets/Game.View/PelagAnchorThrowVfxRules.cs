using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Фаза рига для броска по линии — те же числа, что AnchorLinePhase системы якоря на цепи
    /// (artifacts/anchor-core, PelagAnchorRig.Throw): 1 замах, 2 полёт, 3 натяг (тик T), 4 возврат, 5 поймал.
    /// </summary>
    public enum AnchorThrowRigPhase : byte { None = 0, Windup = 1, Fly = 2, Hold = 3, Yank = 4, Done = 5 }

    /// <summary>
    /// Числа вида Броска якоря без Unity — проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/AnchorThrowVfxRulesTests.cs). Время — тик показа
    /// sim.Tick − 2 + Alpha (тело героя рисуется на нём). Голова каждой полосы — ТА ЖЕ
    /// формула, что в Sim (Simulation.AnchorThrowHead), между тиками — отрезком: вид не
    /// заводит своей кривой полёта и возврата, попадание и натяг встают в тик Sim.
    /// Высота головы, дёрг натяга, сеть Невода и призраки Веера — только вид.
    /// </summary>
    public static class PelagAnchorThrowVfxRules
    {
        public const int TicksPerSecond = Simulation.TicksPerSecond;

        /// <summary>Тик, на котором нарисовано тело (как PelagAbordageVfxRules.ShownTick).</summary>
        public static float ShownTick(int simTick, float alpha) => simTick - 2 + alpha;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Smooth01(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }
        public static float Seconds(float ticks) => ticks / TicksPerSecond;

        /// <summary>Рука в выпуск: центр героя + 0,5 м по полосе (AbordageHandReach).</summary>
        public static readonly float HandReach = Simulation.AbordageHandReach.ToFloat();

        // ------------------------------------------------------------------ голова

        /// <summary>Голова полосы <paramref name="lane"/> в тик показа: формула Sim в целых тиках, между ними — отрезок.</summary>
        public static void Head(in AnchorThrowState s, int lane, float shown, out float x, out float z)
        {
            int t0 = (int)Math.Floor(shown);
            float f = shown - t0;
            FixVec2 a = Simulation.AnchorThrowHead(s, lane, t0);
            x = a.X.ToFloat();
            z = a.Y.ToFloat();
            if (f <= 1e-5f) return;
            FixVec2 b = Simulation.AnchorThrowHead(s, lane, t0 + 1);
            x += (b.X.ToFloat() - x) * f;
            z += (b.Y.ToFloat() - z) * f;
        }

        /// <summary>Дальность головы от центра героя в каст вдоль своей полосы, м.</summary>
        public static float Along(in AnchorThrowState s, int lane, float shown)
        {
            Head(s, lane, shown, out float x, out float z);
            FixVec2 dir = s.LaneDir(lane);
            return (x - s.Center.X.ToFloat()) * dir.X.ToFloat() + (z - s.Center.Y.ToFloat()) * dir.Y.ToFloat();
        }

        /// <summary>Тик, когда голова полосы встала в конце (укус Гарпуна, корпус босса, стена — раньше натяга).</summary>
        public static int LaneArriveTick(in AnchorThrowState s, int lane) => s.ReleaseTick + s.LaneFlight(lane);

        // ------------------------------------------------------------------ риг (anchor-core §1.4)

        public static AnchorThrowRigPhase RigPhase(in AnchorThrowState s, float shown)
        {
            if (s.Serial == 0) return AnchorThrowRigPhase.None;
            if (shown < s.ReleaseTick) return AnchorThrowRigPhase.Windup;
            if (shown < s.TautTick) return AnchorThrowRigPhase.Fly;
            if (shown < s.TautTick + 1) return AnchorThrowRigPhase.Hold;
            if (shown < s.CatchTick) return AnchorThrowRigPhase.Yank;
            return AnchorThrowRigPhase.Done;
        }

        /// <summary>Кольцо головы над её центром, м (anchor-core DESIGN §0.1: Anchor_Attachment × 0,94).</summary>
        public const float RingToCentre = .38f;

        /// <summary>
        /// Риг: кольцо на линии Origin + Dir · Along (Origin — рука в выпуск), голова — дальше кольца на 0,38 м:
        /// Along = дальность Sim − рука − 0,38, и центр головы рига стоит в точке Sim. Along от Sim, не своя кривая.
        /// </summary>
        public static float RigAlong(in AnchorThrowState s, float shown) => Math.Max(0f, Along(s, 0, shown) - HandReach - RingToCentre);

        /// <summary>Скорость кольца по линии, м/с (центральная разность в четверть тика; натяг — 0).</summary>
        public static float RigAlongSpeed(in AnchorThrowState s, float shown)
        {
            const float h = .25f;
            return (RigAlong(s, shown + h) - RigAlong(s, shown - h)) / (2f * h) * TicksPerSecond;
        }

        // ------------------------------------------------------------------ высота и дёрг (только вид)

        /// <summary>Конец полёта и возврат: плоский бросок без навеса, на возврате голова низко над полом.</summary>
        public const float FlyEndHeight = .9f, ReturnHeight = .42f;
        /// <summary>Дёрг натяга: голова отскакивает к руке на 0,12 м и возвращается за тик (anchor-core «дёрг, а не замирание»).</summary>
        public const float JerkAmplitude = .12f;
        /// <summary>Ошибка выпуска (кулак → начало линии) гаснет за столько тиков, как сшивка рига (DESIGN §1.3).</summary>
        public const float ReleaseSeamTicks = 2f;

        /// <summary>
        /// Высота головы над землёй, м: в выпуск — высота кулака <paramref name="release"/>, к концу полёта — 0,9;
        /// натяг — 0,9; возврат за 2 тика опускается к 0,42, за 2 тика до ловли поднимается к руке <paramref name="hand"/>.
        /// </summary>
        public static float HeadHeight(in AnchorThrowState s, int lane, float shown, float release, float hand)
        {
            if (shown <= s.ReleaseTick) return release;
            float arrive = LaneArriveTick(s, lane);
            if (shown < arrive) return Lerp(release, FlyEndHeight, Smooth01((shown - s.ReleaseTick) / Math.Max(1f, arrive - s.ReleaseTick)));
            if (shown <= s.TautTick) return FlyEndHeight;
            if (shown >= s.CatchTick) return hand;
            float low = Lerp(FlyEndHeight, ReturnHeight, Smooth01((shown - s.TautTick) / 2f));
            return Lerp(hand, low, Smooth01((s.CatchTick - shown) / 2f));
        }

        /// <summary>Отскок к руке в натяг, м (0 вне тика T…T+1).</summary>
        public static float Jerk(float shown, int tautTick)
        {
            float u = shown - tautTick;
            return u <= 0f || u >= 1f ? 0f : JerkAmplitude * (float)Math.Sin(Math.PI * u);
        }

        /// <summary>Доля ошибки выпуска, которая ещё видна (1 в кадр выпуска <paramref name="from"/> → 0 через 2 тика).</summary>
        public static float ReleaseSeam(float shown, float from) => 1f - Smooth01((shown - from) / ReleaseSeamTicks);

        // ------------------------------------------------------------------ вода на цепи

        /// <summary>Натяжение цепи для воды на ней: 0 — в руке, 0,45 — полёт (выдаётся), 1 — натяг и возврат.</summary>
        public static float Tension(in AnchorThrowState s, float shown)
        {
            if (s.Serial == 0 || shown < s.ReleaseTick || shown >= s.CatchTick) return 0f;
            if (shown < s.TautTick) return TensionFlight;
            return TensionFlight + (1f - TensionFlight) * Smooth01((shown - s.TautTick) / 1.5f);
        }

        public const float TensionFlight = .45f;

        /// <summary>Полуширина воды вдоль цепи, м; у Гарпуна цепь «звенит тонкой струной» — уже.</summary>
        public static float SleeveHalfWidth(float tension, PelagForm form)
            => (form == PelagForm.AnchorThrowHarpoon ? .6f : 1f) * (.022f + .03f * Clamp01(tension));

        /// <summary>Капель с цепи в секунду на метр.</summary>
        public static float SleeveDropRate(float tension) => 10f + 40f * Clamp01(tension);

        /// <summary>Возраст распада воды после ловли, с (вода на цепи рвётся на капли за ~0,2 с).</summary>
        public static float BreakAge(float shown, float fromTick) => shown < fromTick ? 0f : Seconds(shown - fromTick) * 1.6f;

        // ------------------------------------------------------------------ Невод

        /// <summary>Комья гребня сети выпирают за воду на столько, м: видимый край сети = край урона Sim.</summary>
        public const float NetCrest = .08f;

        /// <summary>Полуширина воды сети без гребня, м (1,5 − 0,08).</summary>
        public static float NetWaterHalfWidth => Simulation.AnchorThrowNetHalfWidth.ToFloat() - NetCrest;

        /// <summary>Сеть раскрывается за тик до натяга и полностью ложится через тик после него (Sim ловит в T).</summary>
        public const float NetOpenFrom = -1f, NetOpenTicks = 2f;

        public static float NetOpen(float shown, int tautTick) => Smooth01((shown - (tautTick + NetOpenFrom)) / NetOpenTicks);

        /// <summary>Дальний край сети: до натяга — дальность главной полосы, в возврате — за головой (сеть стягивается).</summary>
        public static float NetFar(in AnchorThrowState s, float shown)
            => shown <= s.TautTick ? s.Reach0.ToFloat() : Math.Max(HandReach, Along(s, 0, shown));

        // ------------------------------------------------------------------ Веер

        /// <summary>Призрак проявляется за тик после выпуска и тает за 4 тика после ловли.</summary>
        public static float GhostOpacity(in AnchorThrowState s, float shown)
        {
            if (shown < s.ReleaseTick) return 0f;
            float appear = Smooth01(shown - s.ReleaseTick);
            return shown < s.CatchTick ? appear : appear * (1f - Smooth01((shown - s.CatchTick) / 4f));
        }

        public static float GhostDissolve(in AnchorThrowState s, float shown) => shown < s.CatchTick ? 0f : Clamp01((shown - s.CatchTick) / 4f);

        // ------------------------------------------------------------------ геометрия

        /// <summary>Доля отрезка a→b до ближайшей к точке p точки (плоскость пола).</summary>
        public static float ClosestOnSegment(float ax, float az, float bx, float bz, float px, float pz)
        {
            float dx = bx - ax, dz = bz - az;
            float len = dx * dx + dz * dz;
            return len < 1e-8f ? 0f : Clamp01(((px - ax) * dx + (pz - az) * dz) / len);
        }

        /// <summary>Сколько полос рисовать: Веер — три (якорь и два призрака), иначе одна.</summary>
        public static int LaneCount(in AnchorThrowState s) => s.Lanes == 3 ? 3 : 1;

        /// <summary>Полоса оборвана (стена, корпус босса, укус Гарпуна) — у превью засечка, у вида всплеск об стену.</summary>
        public static bool LaneCut(in AnchorThrowState s, int lane) => s.LaneStop(lane) != AnchorThrowStop.Full;
    }
}
