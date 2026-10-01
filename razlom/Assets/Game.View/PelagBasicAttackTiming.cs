using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Frozen action clocks map to an authored clip's contact marker, never a mutable attack-speed stat.</summary>
    public static class PelagBasicAttackTiming
    {
        public static float Phase(in PelagBasicAttackState action, float tick, float contactPhase)
        {
            contactPhase = Clamp01(contactPhase);
            if (tick <= action.ContactTick)
                return contactPhase * Progress(action.StartTick, action.ContactTick, tick);
            return contactPhase + (1f - contactPhase) * Progress(action.ContactTick, action.EndTick, tick);
        }

        public static float WhooshTick(in PelagBasicAttackState action) =>
            action.StartTick + (action.ContactTick - action.StartTick) * (action.Stage == 2 ? .50f : .55f);

        // Interpolated bodies lag Sim by one tick. A processed contact cannot be shown before its confirmed blade pose.
        public static float ObservedTick(in PelagBasicAttackState action, float renderTick) =>
            action.ContactProcessed ? Math.Max(renderTick, action.ContactTick) : renderTick;

        public static bool Heavy(int stage, bool combo) => stage == (combo ? 2 : 1);

        private static float Progress(float start, float end, float tick) =>
            end > start ? Clamp01((tick - start) / (end - start)) : tick >= end ? 1f : 0f;

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
