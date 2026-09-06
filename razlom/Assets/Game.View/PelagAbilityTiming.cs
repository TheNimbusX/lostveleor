using Game.Sim;

namespace Game.View
{
    public static class PelagAbilityTiming
    {
        public const float LeapTravel = AnchorKit.LeapTicks / (float)Simulation.TicksPerSecond;
        public const float SweepTravel = AnchorKit.SweepTicks / (float)Simulation.TicksPerSecond;
        public const float ChainHop = AnchorKit.ChainTicksPerHop / (float)Simulation.TicksPerSecond;
        public const float SweepWindup = AnchorKit.SweepCastDelayTicks / (float)Simulation.TicksPerSecond;
        public const float AnchorDraw = 0.20f;
        public const float LeapWindup = AnchorKit.LeapWindupTicks / (float)Simulation.TicksPerSecond;
        public const float LeapArrival = LeapWindup + LeapTravel;
        public const float LeapRecovery = LeapArrival + 0.22f;
        public const float SweepRecovery = SweepWindup + SweepTravel + 0.20f;
    }
}
