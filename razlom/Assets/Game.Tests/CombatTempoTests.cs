using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class CombatTempoTests
    {
        static Simulation Arena()
        {
            var s = new Simulation(123, 64); s.SetupTestArena(0);
            s.Entities.Position[0] = FixVec2.Zero;
            s.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            s.RefreshPlayerStats(true);
            for (int i = 0; i < 4; i++) s.SetAbility(i, new[] { AbilityDefinition.AnchorSlam(),
                AbilityDefinition.Whirlwind(), AbilityDefinition.Skewer(), AbilityDefinition.Backblast() }[i], Array.Empty<AbilityNode>(), 0);
            s.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            return s;
        }
        static InputFrame Cast(int slot) => new InputFrame { AbilityMask = (byte)(1 << slot),
            Aim = new FixVec2(Fix64.FromInt(10), Fix64.Zero), AttackTarget = -1, AbilityTarget = -1 };
        static void Until(Simulation s, int tick) { while (s.Tick < tick) s.Step(InputFrame.Empty); }
        static int Target(Simulation s, int x)
        {
            int id = s.Entities.Spawn(new FixVec2(Fix64.FromInt(x), Fix64.Zero), 1000, Faction.Orvill);
            s.Entities.Stats[id].SetBase(StatType.MaxHealth, Fix64.FromInt(1000));
            s.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            s.Entities.Stats[id].SetBase(StatType.AttackSpeed, Fix64.Zero);
            s.Entities.RefreshStats(id); s.Entities.Health[id] = 1000;
            s.Entities.PushWeight[id] = Fix64.Zero;
            return id;
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void AvailableEvadeImmediatelyCancelsWindupWithoutRefundOrGhostImpact(int slot)
        {
            var s = Arena(); int victim = Target(s, 4);
            s.Step(Cast(0)); int ready = s.AbilityReadyTick(0);
            Until(s, 4); s.Step(Cast(slot));
            Assert.That(s.AnchorSlamActive, Is.False);
            Assert.That(s.PlayerAction.Slot, Is.EqualTo(slot));
            Assert.That(s.AbilityReadyTick(0), Is.EqualTo(ready));
            Assert.That(s.Entities.Lavidium[0].ToDouble(), Is.LessThan(182));
            for (int i = 0; i < 35; i++)
            {
                s.Step(InputFrame.Empty);
                foreach (var e in s.Events) Assert.That(e.Type, Is.Not.EqualTo(SimEventType.AnchorSlamImpact));
            }
            if (slot != 2) Assert.That(s.Entities.Health[victim], Is.EqualTo(1000));
        }
        [Test]
        public void UnavailableEvadeDoesNotCancelAndStunWinsOverAnAvailableEvade()
        {
            var s = Arena(); s.Step(Cast(2)); Until(s, 10); s.Step(Cast(0));
            int serial = s.PlayerAction.Serial; s.Step(Cast(2));
            Assert.That(s.PlayerAction.Serial, Is.EqualTo(serial)); Assert.That(s.AnchorSlamActive, Is.True);
            s.Statuses.ApplyStun(0, s.Tick + 20); s.Step(Cast(4));
            Assert.That(s.AnchorSlamActive, Is.False); Assert.That(s.Entities.ForcedTicksLeft[0], Is.Zero);
            Assert.That(s.PlayerAction.Interrupted, Is.True);
        }
        [TestCase(10, true)] [TestCase(9, false)]
        public void LastPressWaitsForContactAndExpiresAfterSixTicks(int pressTick, bool casts)
        {
            var s = Arena(); s.Step(Cast(0)); Until(s, pressTick); s.Step(Cast(1));
            Assert.That(s.PlayerAction.Slot, Is.Zero);
            Until(s, 17);
            Assert.That(s.PlayerAction.Slot, Is.EqualTo(casts ? 1 : 0));
            Assert.That(s.AbilityReadyTick(1) > 0, Is.EqualTo(casts));
        }
        [Test]
        public void BufferedPressIsReplacedAndCannotRepeat()
        {
            var s = Arena(); s.SetAbility(2, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
            s.Step(Cast(0)); Until(s, 11); s.Step(Cast(1)); s.Step(Cast(2)); Until(s, 17);
            Assert.That(s.PlayerAction.Slot, Is.EqualTo(2)); Assert.That(s.AbilityReadyTick(1), Is.Zero);
            int serial = s.PlayerAction.Serial; Until(s, 90); Assert.That(s.PlayerAction.Serial, Is.EqualTo(serial));
        }
        [Test]
        public void ExecutionAndCooldownApplyAfterTalentsAndRespectCapsAndFloors()
        {
            var s = Arena();
            var node = AbilityNode.StatMod("test.fast_windup", AbilityStatType.WindupTicks, ModifierOp.Increased, Fix64.Ratio(-2, 5));
            s.SetAbility(0, AbilityDefinition.AnchorSlam(), new[] { node }, 1);
            s.Entities.Stats[0].SetBase(StatType.AbilitySpeed, Fix64.FromInt(5));
            s.Entities.Stats[0].SetBase(StatType.CooldownRecovery, Fix64.FromInt(5));
            s.Step(Cast(0));
            Assert.That(s.PlayerAction.ContactTick, Is.EqualTo(4));
            Assert.That(s.AbilityReadyTick(0), Is.EqualTo(54));
            Assert.That(s.AbilityExecutionTicks(1), Is.EqualTo(2));
            var b = new AbilityBuild(); b.Rebuild(AbilityDefinition.Skewer().Set(AbilityStatType.CooldownTicks, 2), Array.Empty<AbilityNode>(), 0);
            Assert.That(s.AbilityCooldownTicks(b), Is.EqualTo(6));
            Assert.That(s.GetAbility(0).Get(AbilityStatType.StunTicks).ToInt(), Is.EqualTo(15));
        }
        [Test]
        public void AttackSpeedScalesBothWindupAndCycle()
        {
            var s = Arena(); int cycle = s.Entities.AttackCooldown[0];
            s.Entities.Stats[0].SetBase(StatType.AttackSpeed, Fix64.FromInt(3)); s.RefreshPlayerStats(false);
            Assert.That(s.PlayerAttackWindupTicks, Is.EqualTo(4));
            Assert.That(s.Entities.AttackCooldown[0], Is.EqualTo(cycle / 2));
        }
        [Test]
        public void SkewerPassesBodiesHitsEachOnceAndPreservesSixMeterRangeAtHighSpeed()
        {
            var s = Arena(); int a = Target(s, 2), b = Target(s, 4);
            CombatTempoPreset.Apply(s, 2); s.Step(Cast(2)); Until(s, 11);
            Assert.That(s.Entities.Position[0].X.ToDouble(), Is.EqualTo(6).Within(.001));
            Assert.That(s.Entities.Health[a], Is.EqualTo(910)); Assert.That(s.Entities.Health[b], Is.EqualTo(910));
            Until(s, 40); Assert.That(s.Entities.Health[a], Is.EqualTo(910));
        }
        [Test]
        public void BackblastHitsAtTwoTicksAndRetreatsAwayFromCursorWithoutPool()
        {
            var s = Arena(); int victim = Target(s, 1); s.Step(Cast(3));
            s.Step(InputFrame.Empty); Assert.That(s.Entities.Health[victim], Is.EqualTo(1000));
            s.Step(InputFrame.Empty); Assert.That(s.Entities.Health[victim], Is.EqualTo(940));
            Assert.That(s.Events, Has.Some.Matches<SimEvent>(e => e.Type == SimEventType.BackblastBurst));
            Until(s, 9); Assert.That(s.Entities.Position[0].X.ToDouble(), Is.EqualTo(-3).Within(.001));
            for (int i = 0; i < 8; i++) Assert.That(s.FirePoolActive(i), Is.False);
        }
        [Test]
        public void DirectMovementHasNormalizedDiagonalStopsAndNeverChasesAttackTarget()
        {
            var s = Arena(); int target = Target(s, 8);
            var input = InputFrame.Empty; input.Flags = (byte)(InputFlags.DirectMovement | InputFlags.Attack);
            input.AttackTarget = target; input.Aim = s.Entities.Position[target];
            input.MoveDirection = new FixVec2(Fix64.One, Fix64.One);
            for (int i = 0; i < 5; i++) s.Step(input);
            Assert.That(s.Entities.Velocity[0].Length.ToDouble(), Is.LessThanOrEqualTo(s.Entities.MoveStep[0].ToDouble() + .00001));
            Assert.That(s.AttackTarget, Is.EqualTo(-1));
            input.MoveDirection = FixVec2.Zero; for (int i = 0; i < 5; i++) s.Step(input);
            Assert.That(s.Entities.Velocity[0], Is.EqualTo(FixVec2.Zero));
        }
        [Test]
        public void FastBuildPerformsEightAttacksAndFourMobilityCastsInTenSeconds()
        {
            var s = Arena(); s.SetAbility(0, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
            CombatTempoPreset.Apply(s, 2);
            int attacks = 0, moves = 0;
            for (int t = 0; t < 300; t++)
            {
                var input = t % 15 == 0 ? Cast((t / 15) % 4) : InputFrame.Empty;
                s.Step(input);
                foreach (var e in s.Events) if (e.Type == SimEventType.AbilityCast)
                { if (e.Amount < 2) attacks++; else moves++; }
                Assert.That(s.Entities.Lavidium[0].Raw, Is.GreaterThanOrEqualTo(0));
            }
            Assert.That(attacks, Is.GreaterThanOrEqualTo(8)); Assert.That(moves, Is.GreaterThanOrEqualTo(4));
            TestContext.WriteLine($"10 seconds: attacks={attacks}, mobility={moves}, lavidium={s.Entities.Lavidium[0]}");
        }
        [Test]
        public void NewSkillsNeverOfferTalentsAndInputDirectionParticipatesInReplayHash()
        {
            var l = new RunLoadout(); l.Add(8); l.Add(9);
            Assert.That(l.CanTakeTalent(8), Is.False); Assert.That(l.CanTakeTalent(9), Is.False);
            var a = InputFrame.Empty; var b = a; b.MoveDirection = new FixVec2(Fix64.One, Fix64.Zero);
            ulong ah = Hashing.Offset, bh = Hashing.Offset; a.HashInto(ref ah); b.HashInto(ref bh);
            Assert.That(ah, Is.Not.EqualTo(bh));
        }

        [TestCase(2, 1)] [TestCase(3, -1)]
        public void MobilityStopsAtWallWithoutSlidingOrDamagingThroughIt(int slot, int sign)
        {
            var s=Arena(); var cells=new bool[80*80];
            for(int y=0;y<80;y++)for(int x=0;x<80;x++)cells[y*80+x]=x!=48 && x!=32;
            s.SetupCamp(FixVec2.Zero,new CampWalkMap(new FixVec2(Fix64.FromInt(-5),Fix64.FromInt(-5)),Fix64.Ratio(1,8),80,80,cells));
            s.SetAbility(slot,slot==2?AbilityDefinition.Skewer():AbilityDefinition.Backblast(),Array.Empty<AbilityNode>(),0);
            var press=Cast(slot);press.Aim=new FixVec2(Fix64.FromInt(10),Fix64.FromInt(4));
            int victim=Target(s,2*sign);s.Step(press);Until(s,15);
            double xPos=s.Entities.Position[0].X.ToDouble(),yPos=s.Entities.Position[0].Y.ToDouble();
            Assert.That(Math.Abs(xPos),Is.LessThan(1.15));
            Assert.That(yPos,Is.EqualTo(xPos*.4).Within(.001));
            Assert.That(s.Entities.Health[victim],Is.EqualTo(1000));
        }
        [Test]
        public void EvadeReplacesExistingTravelBeforeThatTravelTakesAnotherStep()
        {
            var s=Arena();s.Step(Cast(2));s.Step(InputFrame.Empty);
            var before=s.Entities.Position[0];s.Step(Cast(3));
            Assert.That(s.MobilityOrigin,Is.EqualTo(before));
            Assert.That(s.PlayerAction.DefinitionId,Is.EqualTo(AbilityDefinition.BackblastId));
        }
        [Test]
        public void WreckStagesHaveDistinctClocksAndPresentationEventsWithOnePayment()
        {
            var s=Arena();s.SetAbility(0,AbilityDefinition.Wreck(),Array.Empty<AbilityNode>(),0);
            s.Step(Cast(0));int first=s.PlayerAction.Serial;Until(s,15);
            var resource=s.Entities.Lavidium[0];s.Step(Cast(0));
            Assert.That(s.PlayerAction.Serial,Is.GreaterThan(first));
            Assert.That(s.Events,Has.Some.Matches<SimEvent>(e=>e.Type==SimEventType.ActionStageStarted && e.ActionVariant==1));
            Assert.That(s.Entities.Lavidium[0],Is.GreaterThanOrEqualTo(resource));
            Until(s,30);s.Step(Cast(0));
            Assert.That(s.Events,Has.Some.Matches<SimEvent>(e=>e.Type==SimEventType.ActionStageStarted && e.ActionVariant==2));
        }
        [Test]
        public void DeathAndArenaResetDiscardBufferedSkillsAndPendingExplosions()
        {
            var s=Arena();s.Step(Cast(3));s.Entities.Alive[0]=false;
            s.Step(Cast(0));for(int i=0;i<12;i++)
            {s.Step(InputFrame.Empty);Assert.That(s.Events,Has.None.Matches<SimEvent>(e=>e.Type==SimEventType.BackblastBurst));}
            s.SetupTestArena(0);Assert.That(s.PlayerAction.Serial,Is.Zero);
            for(int i=0;i<12;i++)s.Step(InputFrame.Empty);
            Assert.That(s.PlayerAction.Serial,Is.Zero);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void TenSkillsAndTheirTalentRanksReplayIdenticallyAtThirtySixtyAndOneTwentyFrames(int pool)
        {
            for(int rank=0;rank<=SabreTalents.TalentsPerLine;rank++)
            {
                if(pool>=8&&rank>0)break;
                ulong expected=Replay(pool,rank,30);
                Assert.That(Replay(pool,rank,60),Is.EqualTo(expected),$"pool {pool} rank {rank} at 60");
                Assert.That(Replay(pool,rank,120),Is.EqualTo(expected),$"pool {pool} rank {rank} at 120");
            }
        }
        static ulong Replay(int pool,int rank,int fps)
        {
            var s=Arena();Target(s,4);var nodes=new AbilityNode[SabreTalents.TalentsPerLine];int count=0;
            if(SabreTalents.TryLineOf(pool,out var line))count=SabreTalents.AppendNodes(line,rank,nodes,0);
            s.SetAbility(0,PelagKit.PoolDefinition(pool),nodes,count);CombatTempoPreset.Apply(s,2);
            int credit=0;
            for(int frame=0;frame<fps*6;frame++)
            {
                credit+=Simulation.TicksPerSecond;
                while(credit>=fps)
                {
                    credit-=fps;int tick=s.Tick;
                    var input=tick%30==0?Cast(0):InputFrame.Empty;
                    input.AbilityTarget=1;input.Aim=s.Entities.Position[1];
                    input.Flags=(byte)InputFlags.DirectMovement;
                    input.MoveDirection=tick%60<20?new FixVec2(Fix64.Zero,Fix64.One):FixVec2.Zero;
                    if(tick==47)input=Cast(4);
                    s.Step(input);
                }
            }
            return s.StateHash();
        }
    }
}
