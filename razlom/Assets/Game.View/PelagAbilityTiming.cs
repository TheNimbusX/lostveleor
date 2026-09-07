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
        public const float LeapArrival = LeapWindup + LeapTravel;
        public const float LeapRecovery = 34f / 30f;
        public const float SweepRecovery = SweepWindup + SweepTravel + 0.20f;
    }
}
