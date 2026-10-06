using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Чистые правила вида форм Вихря (02.10) — без Unity, проверяются тестами
    /// представления: где фронт кольца Пенных волн на времени вида, сколько
    /// кольцо живёт, какие оглушения — Водоворота, в каком ритме крутится
    /// удержание. Время вида — непрерывный тик now = Tick − 1 + Alpha, как у
    /// остальных эффектов Пелага; тик события — тик Sim, на котором оно
    /// родилось (FrameEventContext.SimulationTick − 1).
    /// </summary>
    public static class PelagWhirlwindFormRules
    {
        /// <summary>Сколько тиков кольцо пены рассыпается на внешнем радиусе после хода.</summary>
        public const int FoamWaveLingerTicks = 8;

        /// <summary>
        /// Фронт кольца, м. startTick — тик события WhirlwindFoamRing (шаг 1 хода),
        /// travelTicks — его ActionVariant. Совпадает с Simulation.FoamRingRadiusAt
        /// на целых шагах, между ними — по прямой (кольцо Sim тоже идёт линейно).
        /// </summary>
        public static float FoamRingFront(int ring, int startTick, int travelTicks, float now)
        {
            float inner = Simulation.FoamRingInnerRadius.ToFloat();
            float outer = Simulation.FoamRingOuterRadius(ring).ToFloat();
            if (travelTicks <= 0) return outer;
            float step = now - startTick + 1f;
            float t = step <= 0f ? 0f : step >= travelTicks ? 1f : step / travelTicks;
            return inner + (outer - inner) * t;
        }

        /// <summary>Кольцо ещё идёт (брызги по фронту летят только пока идёт).</summary>
        public static bool FoamRingTravelling(int startTick, int travelTicks, float now)
            => now - startTick + 1f < travelTicks;

        /// <summary>Жизнь кольца на экране, с: ход и рассыпание.</summary>
        public static float FoamRingLifeSeconds(int travelTicks)
            => (travelTicks + FoamWaveLingerTicks) / (float)Simulation.TicksPerSecond;

        /// <summary>
        /// Контакт Водоворота после последнего шага тяги, тиков — правило Sim
        /// (MaelstromContactDelayTicks − MaelstromPullTicks: контакт тиком после тяги).
        /// </summary>
        private const int MaelstromContactAfterPullTicks = Simulation.MaelstromContactDelayTicks - Simulation.MaelstromPullTicks;

        /// <summary>Тик контакта Водоворота по событию тяги: тяга идёт pullTicks и кончается к контакту.</summary>
        public static int MaelstromContactTick(int pullTick, int pullTicks) => pullTick + pullTicks + MaelstromContactAfterPullTicks;

        /// <summary>
        /// Оглушение — Водоворота: длительность его (MaelstromStaggerTicks) и тик
        /// контакта после тяги (±1 — короткий каст по темпу). Удар якорем оглушает
        /// на столько же тиков, но без тяги перед ним.
        /// </summary>
        public static bool IsMaelstromStagger(int stunTick, int stunTicks, int contactTick)
            => contactTick >= 0 && stunTicks == Simulation.MaelstromStaggerTicks
               && stunTick >= contactTick - 1 && stunTick <= contactTick + 1;

        /// <summary>Период оборотов удержания: у Бури — свой (StormPulseTicks), у таланта — прежний.</summary>
        public static int ChannelPulseTicks(Simulation sim)
            => sim != null ? sim.WhirlwindChannelPulseTicks : Simulation.WhirlwindPulseTicks;

        // ---- Пенные волны v3 (владелец 02.10, вечер: «слишком линейные… застывает на пару кадров»)

        /// <summary>Доля разгона гребня: фронт выходит быстрее Sim и к концу хода замедляется.</summary>
        public const float FoamCrestEase = .55f;
        /// <summary>После хода гребень не встаёт, а дотекает с затуханием скорости, с.</summary>
        public const float FoamCrestDriftSeconds = .09f;
        /// <summary>
        /// Первое кольцо (v4) дотекает дольше: оно держится чистой водой, пока второе
        /// выходит и бежит отдельно, и не должно на это время замереть.
        /// </summary>
        public const float FoamFirstCrestDriftSeconds = .12f;

        /// <summary>Сколько гребень кольца дотекает после хода, с (постоянная затухания).</summary>
        public static float FoamCrestDrift(int ring) => ring == 0 ? FoamFirstCrestDriftSeconds : FoamCrestDriftSeconds;

        /// <summary>
        /// Внешний край гребня кольца, м, на непрерывном времени вида. На ходу —
        /// inner + D·(x + e·x·(1 − x)): быстрый выход и замедление вместо ровной
        /// линии, на концах хода совпадает с фронтом Sim (FoamRingFront), между
        /// ними впереди него не больше чем на e·D/4 — фронт Sim остаётся внутри
        /// полосы воды (FoamBandHalfWidth). После хода край не замирает:
        /// скорость конца хода гаснет экспонентой, кольцо дотекает на
        /// v·τ (0,3–0,35 м) и рассыпается на ходу.
        /// </summary>
        public static float FoamCrestRadius(int ring, int startTick, int travelTicks, float now)
        {
            float inner = Simulation.FoamRingInnerRadius.ToFloat();
            float outer = Simulation.FoamRingOuterRadius(ring).ToFloat();
            if (travelTicks <= 0) return outer;
            float x = (now - startTick + 1f) / travelTicks;
            if (x <= 0f) return inner;
            float d = outer - inner;
            if (x <= 1f) return inner + d * (x + FoamCrestEase * x * (1f - x));
            float travelSeconds = travelTicks / (float)Simulation.TicksPerSecond;
            float endSpeed = d * (1f - FoamCrestEase) / travelSeconds;
            float after = (x - 1f) * travelSeconds;
            float drift = FoamCrestDrift(ring);
            return outer + endSpeed * drift * (1f - (float)System.Math.Exp(-after / drift));
        }

        /// <summary>
        /// Полуширина воды кольца, м (v4, по waves-2: толстая полоса около метра).
        /// На выходе (1,3 м) полоса 0,7 м, на 3,5 м — метр, у 5 м — 1,2 м; шире 1,3 м не бывает.
        /// </summary>
        public static float FoamBandHalfWidth(float crestRadius)
        {
            float half = .24f + .075f * crestRadius;
            return half < .64f ? half : .64f;
        }

        /// <summary>
        /// Когда кольцо рвётся на капли, с от выхода. Второе — вскоре после хода.
        /// Первое (v4) — на 0,1 с после хода: пока второе выходит (через 0,2 с) и
        /// бежит отдельно, первое — чистая вода; белеет пеной, когда второе его
        /// догоняет, и рвётся, пока второе проходит сквозь. Раньше первое рвалось
        /// в миг выхода второго, и вместе они читались рваной спиралью.
        /// </summary>
        public static float FoamRingBreakSeconds(int ring, int travelTicks)
            => travelTicks / (float)Simulation.TicksPerSecond + (ring == 0 ? .10f : .05f);

        // ---- Водоворот v3 (владелец 02.10: «больше по радиусу, чем бьёт», «не плавная, резаная, линейная»)

        /// <summary>Пик вращения рукавов, рад/с (по часовой сверху — узор бежит к центру).</summary>
        public const float MaelstromSpinPeak = 3.2f;

        /// <summary>Контакт после события тяги, с: тяга идёт pullTicks, контакт — следующим тиком.</summary>
        public static float MaelstromContactSeconds(int pullTicks)
            => (System.Math.Max(0, pullTicks) + MaelstromContactAfterPullTicks) / (float)Simulation.TicksPerSecond;

        // ---- Длинная тяга (владелец 02.10: 9 тиков не читались → Simulation.MaelstromPullTicks = 16)

        /// <summary>
        /// Контакт, под который нарисована вода v3/v4, с: тяга 9 тиков, контакт Вихря
        /// на 10-м (Simulation.WhirlwindContactDelayTicks). От него отсчитаны распад
        /// рукава в материале (_Break 0,62 с, Editor/PelagWhirlwindFoamVfxSetup.Forms)
        /// и пути струй.
        /// </summary>
        public static float MaelstromAuthoredContactSeconds
            => Simulation.WhirlwindContactDelayTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Во сколько раз тяга длиннее нарисованной: 1 — прежние 0,33 с, 1,7 — тяга 16 тиков.</summary>
        public static float MaelstromPullStretch(float contact)
            => contact > 0f ? contact / MaelstromAuthoredContactSeconds : 1f;

        /// <summary>
        /// Возраст воды рукава для материала, с: до контакта нарисованные 0,33 с
        /// растянуты на всю тягу, после — секунда в секунду. Вода белеет и рвётся
        /// после контакта так же, как в принятой v4, а не посреди длинной тяги.
        /// </summary>
        public static float MaelstromWaterAge(float t, float contact)
        {
            if (contact <= 0f) return t;
            float authored = MaelstromAuthoredContactSeconds;
            return t <= contact ? t * authored / contact : authored + (t - contact);
        }

        /// <summary>
        /// Сколько тело должно сдвинуться к герою, чтобы за ним встал след тяги, м.
        /// Тяга v2 (Simulation.MaelstromPullProgress) везёт с первого тика — 1,5–2,5 см
        /// на путях 1–1,6 м, — и след встаёт с первого же шага, а не после прежних 6 см.
        /// </summary>
        public const float MaelstromDragStart = .015f;

        /// <summary>На сколько дольше нарисованного живёт объект Водоворота, с: распад сдвинут вместе с контактом.</summary>
        public static float MaelstromExtraLifeSeconds(float contact)
            => System.Math.Max(0f, contact - MaelstromAuthoredContactSeconds);

        /// <summary>
        /// Путь головы струи, с: 0,24–0,30 под нарисованную тягу (random01 — доля
        /// разброса), растянут вместе с тягой — струи текут всю тягу, а не кончаются на её трети.
        /// </summary>
        public static float MaelstromStrandDuration(float random01, float stretch)
            => (.24f + .06f * (random01 <= 0f ? 0f : random01 >= 1f ? 1f : random01)) * stretch;

        /// <summary>Гладкая ступень 0 → 1 (smoothstep).</summary>
        public static float Smooth01(float x)
        {
            x = x <= 0f ? 0f : x >= 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Скорость вращения рукавов, рад/с: плавный разгон за 0,12 с, держится до
        /// контакта, после — плавно гаснет до трети. Ни скачков, ни ровной линии.
        /// </summary>
        public static float MaelstromSpinSpeed(float t, float contact)
            => MaelstromSpinPeak * Smooth01(t / .12f) * (1f - .7f * Smooth01((t - contact) / .40f));

        /// <summary>Угол поворота рукавов к моменту t, рад (интеграл скорости, Симпсон по 32 шагам).</summary>
        public static float MaelstromSpinAngle(float t, float contact)
        {
            if (t <= 0f) return 0f;
            const int steps = 32;
            float h = t / steps, sum = MaelstromSpinSpeed(0f, contact) + MaelstromSpinSpeed(t, contact);
            for (int i = 1; i < steps; i++) sum += (i % 2 == 1 ? 4f : 2f) * MaelstromSpinSpeed(i * h, contact);
            return sum * h / 3f;
        }

        /// <summary>
        /// Доля радиуса удара у внешних концов рукавов: 1,10 при выходе → 1,00 к
        /// контакту (рукава наматываются внутрь и ложатся ровно на край удара) →
        /// 0,93 при распаде. Плавно, без излома в контакт.
        /// </summary>
        public static float MaelstromArmReach(float t, float contact)
        {
            float pull = Smooth01(contact > 0f ? t / contact : 1f);
            float after = Smooth01((t - contact) / .35f);
            return 1.10f - .10f * pull - .07f * after;
        }

        /// <summary>Закрутка рукава от края к центру, градусы: туже к контакту — видно, что наматывается.</summary>
        public static float MaelstromSweepDegrees(float t, float contact)
            => 95f + 40f * Smooth01(contact > 0f ? t / contact : 1f) + 10f * Smooth01((t - contact) / .4f);

        /// <summary>
        /// Ширина рукавов при проявлении, доля (v4): с первого кадра 30% ширины плотной
        /// фиолетовой воды, за 0,08 с — полная. Прозрачностью больше не проявляем:
        /// полупрозрачный фиолет на траве давал серо-розовую муть.
        /// </summary>
        public static float MaelstromArmGrow(float t) => .30f + .70f * Smooth01(t / .08f);

        /// <summary>
        /// Струи тяги (v4): доля пути, которую прошла голова струи за t с от своего
        /// старта, путь — duration с. С места, с разгоном к центру (втягивает):
        /// x²·(1,4 − 0,4x): скорость в начале ноль и растёт до конца пути (в конце — 1,6 средней).
        /// </summary>
        public static float MaelstromStrandTravel(float t, float duration)
        {
            if (t <= 0f || duration <= 0f) return 0f;
            float x = t >= duration ? 1f : t / duration;
            return x * x * (1.4f - .4f * x);
        }

        /// <summary>Старт струи i из count, с: вперемешку по кругу за первые 0,16 с нарисованной тяги (× растяжение тяги).</summary>
        public static float MaelstromStrandDelay(int i, int count, float stretch = 1f)
            => count <= 1 ? 0f : (i * 7 % count) / (float)count * .16f * stretch;

        /// <summary>Докуда рукав дорос от края к центру (0 — край, 1 — у героя): вода затекает внутрь за 0,14 с.</summary>
        public static float MaelstromHeadReveal(float t)
        {
            float x = t <= 0f ? 0f : t >= .14f ? 1f : t / .14f;
            float ease = 1f - (1f - x) * (1f - x) * (1f - x);
            return .35f + .65f * ease;
        }
    }
}
