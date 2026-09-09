using Game.Sim;

namespace Game.View
{
    public static class PelagAbilityTiming
    {
        public const float LeapTravel = AnchorKit.LeapTicks / (float)Simulation.TicksPerSecond;
        public const float SweepTravel = 9 / (float)Simulation.TicksPerSecond;
        public const float ChainHop = AnchorKit.ChainTicksPerHop / (float)Simulation.TicksPerSecond;
        public const float SweepWindup = 6 / (float)Simulation.TicksPerSecond;
        public const float AnchorDraw = 0.20f;
        public const float LeapWindup = AnchorKit.LeapWindupTicks / (float)Simulation.TicksPerSecond;

        /// <summary>
        /// Момент, когда якорь покидает руку, внутри замаха.
        ///
        /// ОДНО ЧИСЛО НА ДВОИХ. Его читает и сборщик клипа — чтобы посадить сюда
        /// найденный в мокапе пик скорости кисти, — и представление, чтобы
        /// отсюда же запустить якорь. Пока это были две независимые величины,
        /// якорь стартовал на четверть секунды раньше, чем рука его отпускала.
        ///
        /// 0.4 замаха: до выпуска остаётся достаточно на замах, после — 60%
        /// замаха на полёт якоря, а это и есть то, ради чего замах удлиняли.
        /// </summary>
        public const float LeapRelease = LeapWindup * 0.40f;
        public const float LeapArrival = LeapWindup + LeapTravel;
        /// <summary>
        /// Полная длина показа Броска якоря: до прибытия плюс посадка.
        ///
        /// Посадка — 13 тиков после прибытия. Раньше здесь стояло 34/30, что
        /// давало ровно 10 тиков при замахе в 9. Замах менялся дважды, и
        /// константа бы молча съела посадку, поэтому она выражена через
        /// прибытие, а не числом.
        ///
        /// 13, а не 10: посадка читается лучше свободного падения, и ей дано
        /// больше экранного времени.
        /// </summary>
        public const float LeapRecovery = LeapArrival + 13 / (float)Simulation.TicksPerSecond;
        public const float SweepRecovery = SweepWindup + SweepTravel + 0.20f;
    }
}
