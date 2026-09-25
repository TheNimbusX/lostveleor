using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ForestWendigoTests
    {
        private static Simulation Arena(int distance=6, bool leapReady=true)
        {
            var sim=new Simulation(55,64);sim.SetupWendigoEncounter(null,55);
            sim.Entities.Position[0]=FixVec2.Zero;
            sim.Entities.Position[1]=new FixVec2(Fix64.FromInt(distance),Fix64.Zero);
            sim.Entities.Facing[1]=new FixVec2(-Fix64.One,Fix64.Zero);
            sim.Entities.NextAttackTick[1]=0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth,Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor,Fix64.Zero);
            sim.Entities.RefreshStats(0);sim.Entities.Health[0]=sim.Entities.MaxHealth[0];
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            if(leapReady && distance>=3) Until(sim,Simulation.WendigoLeapCooldownTicks);
            return sim;
        }
        private static void Until(Simulation sim,int tick){while(sim.Tick<tick)sim.Step(InputFrame.Empty);}
        [TestCase(2,WendigoAction.Claw,18,45)]
        [TestCase(6,WendigoAction.Leap,33,60)]
        public void OneContactAtAuthoredTick(int distance,WendigoAction kind,int contact,int damage)
        {
            var sim=Arena(distance);int start=sim.Tick;int health=sim.Entities.Health[0];var impacts=new List<int>();
            for(int t=0;t<=contact+3;t++)
            {
                sim.Step(InputFrame.Empty);
                foreach(var e in sim.Events)if(e.Type==SimEventType.WendigoImpact)impacts.Add(t);
                if(t<contact)Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
            }
            Assert.That(impacts,Is.EqualTo(new[]{contact}));Assert.That(health-sim.Entities.Health[0],Is.EqualTo(damage));
            Assert.That(sim.TryGetWendigoAction(1,out var action),Is.True);Assert.That(action.Kind,Is.EqualTo(kind));
        }
        [Test]
        public void LeapLocksTargetAndDoesNotHitDuringFlight()
        {
            var sim=Arena();sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            var target=a.Target;sim.Entities.Position[0]=new FixVec2(Fix64.Zero,Fix64.FromInt(5));int health=sim.Entities.Health[0];
            Until(sim,a.ImpactTick+1);
            Assert.That(sim.Entities.Health[0],Is.EqualTo(health));Assert.That(sim.Entities.Position[1],Is.EqualTo(target));
        }
        [Test]
        public void ClawDoesNotTurnTowardPlayerDuringWindup()
        {
            var sim=Arena(2);sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            sim.Entities.Position[0]=new FixVec2(Fix64.FromInt(4),Fix64.Zero);int health=sim.Entities.Health[0];
            Until(sim,20);Assert.That(sim.Entities.Facing[1],Is.EqualTo(a.Direction));Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [TestCase(2,5,false)] [TestCase(2,17,true)]
        [TestCase(6,10,false)] [TestCase(6,27,false)] [TestCase(6,32,true)]
        public void CancellationRemovesFutureContact(int distance,int at,bool death)
        {
            var sim=Arena(distance);int start=sim.Tick;Until(sim,start+at);int health=sim.Entities.Health[0];
            if(death)sim.Entities.Alive[1]=false;else sim.Statuses.ApplyStun(1,1000);
            Until(sim,start+65);Assert.That(sim.Entities.Health[0],Is.EqualTo(health));Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
        }
        [Test]
        public void DeathAfterContactDoesNotReplayDamage()
        {
            var sim=Arena();int start=sim.Tick;Until(sim,start+34);int health=sim.Entities.Health[0];sim.Entities.Alive[1]=false;
            Until(sim,start+70);Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [Test]
        public void RepeatSetupClearsPendingActionsAndLeapCooldown()
        {
            var sim=Arena();Until(sim,sim.Tick+15);sim.SetupWendigoEncounter(null,55);
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            Assert.That(sim.Entities.Count,Is.EqualTo(2));Assert.That(sim.Entities.Health[1],Is.EqualTo(420));
        }
        [Test]
        public void ClawCannotDamageAcrossClosedGapBetweenRooms()
        {
            var room=new ModuleDefinition("module.wendigo_wall",8,8,new ModuleConnector[0],weight:0,isEntrance:true);
            var map=new LayoutMap(new ModuleSet(new[]{room}),2);
            map.TryPlace(0,0,0,0);map.TryPlace(0,0,9,0,parent:-1);
            var sim=Arena(2);sim.SetupWendigoEncounter(map,55);
            sim.Entities.Position[1]=new FixVec2(Fix64.Ratio(158,10),Fix64.FromInt(8));
            sim.Entities.Position[0]=new FixVec2(Fix64.Ratio(182,10),Fix64.FromInt(8));
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            int health=sim.Entities.Health[0];Until(sim,20);
            Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [Test]
        public void FirstLeapWaitsSixAndHalfSecondsAndRepeatRestartsDelay()
        {
            var sim=Arena(6,leapReady:false);
            Until(sim,195);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var first),Is.True);
            Assert.That(first.Kind,Is.EqualTo(WendigoAction.Leap));Assert.That(first.StartTick,Is.EqualTo(195));
            sim.SetupWendigoEncounter(null,55);
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            int start=sim.Tick;Until(sim,start+195);
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var repeat),Is.True);
            Assert.That(repeat.StartTick,Is.EqualTo(start+195));
        }
        [Test]
        public void ConsecutiveLeapsAreAtLeast195TicksApart()
        {
            var sim=Arena();sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var first);
            Until(sim,first.EndTick+1);
            sim.Entities.Position[0]=sim.Entities.Position[1]+new FixVec2(Fix64.FromInt(6),Fix64.Zero);
            Until(sim,first.StartTick+195);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var second),Is.True);
            Assert.That(second.Kind,Is.EqualTo(WendigoAction.Leap));
            Assert.That(second.StartTick-first.StartTick,Is.EqualTo(195));
        }
        [Test]
        public void ClawHasFreeMovementWindowAnd45DamageEvery45Ticks()
        {
            var sim=Arena(2);int health=sim.Entities.Health[0];
            Until(sim,31);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            Assert.That(health-sim.Entities.Health[0],Is.EqualTo(45));
            Until(sim,45);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var second),Is.True);
            Assert.That(second.StartTick,Is.EqualTo(45));
        }
        [Test]
        public void WalkSpeedIsThreeMetresAndCooldownDoesNotFreezeMovement()
        {
            var sim=new Simulation(55,64);sim.SetupWendigoEncounter(null,55);
            Assert.That(sim.Entities.MoveStep[1].ToFloat()*30,Is.EqualTo(3).Within(.002));
            sim.Entities.Position[0]=FixVec2.Zero;
            sim.Entities.Position[1]=new FixVec2(Fix64.FromInt(2),Fix64.Zero);
            Until(sim,31);var before=sim.Entities.Position[1];
            sim.Entities.Position[0]=new FixVec2(Fix64.FromInt(-4),Fix64.Zero);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[1],Is.Not.EqualTo(before));
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
        }
        [Test]
        public void SameInputsProduceSameDeterministicState()
        {
            var a=Arena();var b=Arena();
            for(int tick=0;tick<400;tick++)
            {
                a.Step(InputFrame.Empty);b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(),Is.EqualTo(b.StateHash()),"tick "+tick);
            }
        }
    }
}
