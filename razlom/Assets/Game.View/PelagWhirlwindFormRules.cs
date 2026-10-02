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

        /// <summary>Тик контакта Водоворота по событию тяги: тяга идёт pullTicks и кончается к контакту.</summary>
        public static int MaelstromContactTick(int pullTick, int pullTicks) => pullTick + pullTicks + 1;

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
    }
}
