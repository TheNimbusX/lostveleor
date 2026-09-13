using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class BlazeTimingTests
    {
        private static Simulation Arena()
        {
            var sim=new Simulation(112,32);sim.SetupTestArena(0);
            sim.SetAbility(0,AbilityDefinition.Blaze(),new AbilityNode[0],0);
            sim.SetAbility(1,AbilityDefinition.Dash(),new AbilityNode[0],0);
            return sim;
        }
        private static InputFrame Cast(int slot)
        {var i=InputFrame.Empty;i.AbilityMask=(byte)(1<<slot);i.Aim=new FixVec2(Fix64.FromInt(8),Fix64.Zero);return i;}
        private static void Wait(Simulation sim,int n){for(int k=0;k<n;k++)sim.Step(InputFrame.Empty);}
        [Test] public void IgnitionIsDelayedAndEmittedOnce()
        {
            var sim=Arena();sim.Step(Cast(0));int events=0;
            for(int tick=1;tick<=Simulation.BlazeIgnitionDelayTicks+90;tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach(var e in sim.Events)if(e.Type==SimEventType.BlazeBegin)
                {events++;Assert.AreEqual(Simulation.BlazeIgnitionDelayTicks,tick);Assert.AreEqual(90,e.Amount);}
                if(tick<Simulation.BlazeIgnitionDelayTicks)Assert.IsFalse(sim.BlazeActive);
            }
            Assert.AreEqual(1,events);Assert.IsFalse(sim.BlazeActive);
        }
        [Test] public void PouringDoesNotSlowMovement()
        {
            var pouring=Arena();var control=Arena();
            var move=Cast(0);move.Flags=(byte)InputFlags.MoveOrder;
            pouring.Step(move);move.AbilityMask=0;control.Step(move);
            for(int i=0;i<Simulation.BlazeGestureTicks;i++)
            {pouring.Step(move);control.Step(move);
                Assert.AreEqual(control.Entities.Position[0].X.Raw,pouring.Entities.Position[0].X.Raw,$"X tick {i}");
                Assert.AreEqual(control.Entities.Position[0].Y.Raw,pouring.Entities.Position[0].Y.Raw,$"Y tick {i}");}
        }
        [Test] public void RollBeforeIgnitionCancelsPendingBuff()
        {
            var sim=Arena();sim.Step(Cast(0));Wait(sim,10);sim.Step(Cast(1));
            Wait(sim,100);Assert.IsFalse(sim.BlazeActive);Assert.IsFalse(sim.BlazeCasting);
        }
        [Test] public void RollAfterIgnitionKeepsRemainingBuff()
        {
            var sim=Arena();sim.Step(Cast(0));Wait(sim,Simulation.BlazeIgnitionDelayTicks);
            Assert.IsTrue(sim.BlazeActive);sim.Step(Cast(1));Assert.IsTrue(sim.BlazeActive);
            Wait(sim,90);Assert.IsFalse(sim.BlazeActive);
        }
        [Test] public void StunBeforeIgnitionCancelsPendingBuff()
        {
            var sim=Arena();sim.Step(Cast(0));sim.Statuses.StunUntilTick[0]=sim.Tick+3;
            Wait(sim,100);Assert.IsFalse(sim.BlazeActive);Assert.IsFalse(sim.BlazeCasting);
        }
        [Test] public void AttackHeldWaitsForTheBottleGesture()
        {
            var sim=Arena();var attack=Cast(0);attack.Flags=(byte)InputFlags.Attack;sim.Step(attack);
            attack.AbilityMask=0;int swings=0;
            for(int tick=1;tick<=Simulation.BlazeGestureTicks+2;tick++)
            {
                sim.Step(attack);
                foreach(var e in sim.Events)if(e.Type==SimEventType.Attack && e.Source==0)
                {Assert.GreaterOrEqual(tick,Simulation.BlazeGestureTicks);swings++;}
            }
            Assert.Greater(swings,0);
        }
    }
}
