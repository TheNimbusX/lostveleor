using Game.Sim;
using NV2 = System.Numerics.Vector2;

namespace Game.View
{
    // Единственный файл рига, который знает WreckState (SPEC 2.6, поток Sim Крушения).
    // apply.py кладёт его, только если в Game.Sim есть «public WreckState Wreck»; без него риг
    // Крушение не берёт (ClaimWreck → false) и работает прежний PelagAnchorSlamView.BeginWreck.
    public sealed partial class PelagAnchorRig
    {
        static partial void ReadWreckSnapshot(Simulation sim, ref AnchorRigWreckInput input, ref bool ok)
        {
            WreckState w = sim.Wreck;
            input.Serial = w.Serial;
            input.Phase = (byte)w.Phase;
            input.Stage = w.Stage;
            input.Side = w.Side;
            input.StageStartTick = w.StageStartTick;
            input.ContactTick = w.ContactTick;
            input.OverheadTick = w.OverheadTick;
            input.ChargeStartTick = w.ChargeStartTick;
            input.Charge = w.Charge;
            input.WindowEndTick = w.WindowEndTick;
            input.HoldEndTick = w.HoldEndTick;
            input.ExitEndTick = w.ExitEndTick;
            input.Direction = new NV2(w.Direction.X.ToFloat(), w.Direction.Y.ToFloat());
            input.ImpactPoint = new NV2(w.ImpactPoint.X.ToFloat(), w.ImpactPoint.Y.ToFloat());
            input.HasImpact = w.Stage >= 2 && w.ImpactRadius.ToFloat() > 0f;
            ok = true;
        }
    }
}
