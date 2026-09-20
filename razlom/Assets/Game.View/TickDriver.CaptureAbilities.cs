using System;
using Game.Sim;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        private void ConfigureCapturedAbilities()
        {
            var mode=CaptureRig.VfxShowcase;
            if(mode==PelagVfxShowcase.Rotation)
            {
                var pool=new[]{6,4,0,3};
                for(int i=0;i<4;i++)Sim.SetAbility(i,PelagKit.PoolDefinition(pool[i]),Array.Empty<AbilityNode>(),0);
            }
            else
            {
                int pool=mode==PelagVfxShowcase.AnchorLeap?6:mode==PelagVfxShowcase.AnchorSweep?4:
                    mode==PelagVfxShowcase.ChainStep?3:mode==PelagVfxShowcase.Cleave?1:
                    mode==PelagVfxShowcase.Blaze?2:mode==PelagVfxShowcase.Wreck?5:
                    mode==PelagVfxShowcase.FireFlask?7:mode==PelagVfxShowcase.Skewer?8:
                    mode==PelagVfxShowcase.Backblast?9:0;
                Sim.SetAbility(0,PelagKit.PoolDefinition(pool),Array.Empty<AbilityNode>(),0);
            }
            Sim.SetAbility(PelagKit.DashSlot,AbilityDefinition.Dash(),Array.Empty<AbilityNode>(),0);
        }
    }
}
