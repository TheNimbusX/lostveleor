using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class StonehoofTests
    {
        private static Simulation Arena(bool wall = false, int count = 1)
        {
            LayoutMap map = null;
            if (wall)
            {
                var room = new ModuleDefinition("stonehoof.test", 20, 20, new ModuleConnector[0], isEntrance: true);
                map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
                map.AddTestObstacle(new LayoutObstacle(new FixVec2(Fix64.FromInt(-5), Fix64.Zero), Fix64.One, 0));
            }
            var sim = new Simulation(76, 64); sim.SetupStonehoofEncounter(map, 76, count);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0); sim.Entities.Health[0] = 10000;
            return sim;
        }
        private static void Until(Simulation sim, int tick) { while (sim.Tick < tick) sim.Step(InputFrame.Empty); }
        [Test]
        public void FullWarningLocksDirectionAndEndWhileHeroMoves()
        {
            var s = Arena(); s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out var a), Is.True);
            Assert.That(a.LaunchTick - a.StartTick, Is.EqualTo(30));
            s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(4));
            Until(s, a.LaunchTick); Assert.That(s.Entities.Position[1], Is.EqualTo(a.Origin));
            s.TryGetStonehoofAction(1, out var b); Assert.That(b.Target, Is.EqualTo(a.Target));
            Assert.That(b.Direction, Is.EqualTo(a.Direction)); Until(s, a.StopTick + 1);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }
        [Test]
        public void HitsOnceAndPushesSidewaysThenSkidsAtOpenEdge()
        {
            var s = Arena(); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            int hits = 0; Until(s, a.LaunchTick);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
            while (s.Tick <= a.StopTick + 10)
            {
                s.Step(InputFrame.Empty);
                foreach (var e in s.Events) if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == 0) hits++;
            }
            Assert.That(hits, Is.EqualTo(1)); Assert.That(s.Entities.Health[0], Is.EqualTo(9970));
            Assert.That(Fix64.Abs(s.Entities.Position[0].Y).ToFloat(), Is.InRange(.95f, 1.05f));
            Assert.That(a.StopReason, Is.EqualTo(StonehoofStop.ArenaEdge));
            Assert.That(a.StopTick - a.BrakeTick, Is.EqualTo(18));
            Assert.That(s.Entities.NextAttackTick[1], Is.EqualTo(a.StopTick + 120));
        }
        [Test]
        public void AcceleratesForSixTicksThenKeepsTwelveMetresPerSecond()
        {
            var s = Arena(); s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(3));
            s.Step(InputFrame.Empty); Until(s, 15); s.TryGetStonehoofAction(1, out var a);
            Assert.That(a.Serial, Is.GreaterThan(0)); Until(s, a.LaunchTick + 7);
            Assert.That((s.Entities.Position[1] - a.Origin).Length.ToFloat(), Is.EqualTo(1.2f).Within(.002f));
            var previous = s.Entities.Position[1]; s.Step(InputFrame.Empty);
            Assert.That((s.Entities.Position[1] - previous).Length.ToFloat(), Is.EqualTo(.4f).Within(.002f));
        }
        [Test]
        public void WallStopsSweptBodyAndStunsFor36Ticks()
        {
            var s = Arena(true); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            Assert.That(a.StopReason, Is.EqualTo(StonehoofStop.Obstacle));
            Assert.That(a.Target.X.ToFloat(), Is.EqualTo(-3.3f).Within(.003f));
            Until(s, a.StopTick + 1); Assert.That(s.Statuses.IsStunned(1, s.Tick), Is.True);
            Assert.That(s.Statuses.StunUntilTick[1], Is.EqualTo(a.StopTick + 36));
            Assert.That(s.Entities.Position[1], Is.EqualTo(a.Target)); Until(s, a.EndTick);
            Assert.That(s.Entities.Position[1], Is.EqualTo(a.Target));
        }
        [TestCase(4, 0)] [TestCase(29, 1)] [TestCase(33, 2)]
        public void DeathStunAndOneTickForcedMotionCancelBeforeContact(int time, int reason)
        {
            var s = Arena(); s.Step(InputFrame.Empty); Until(s, time);
            if (reason == 0) s.Entities.Alive[1] = false;
            else if (reason == 1) s.Statuses.ApplyStun(1, s.Tick + 3);
            else { ForcedMotion.Begin(s.Entities, 1, s.Entities.Position[1], 2, ForcedMotionKind.Dragged); s.Entities.ForcedTicksLeft[1] = 1; }
            s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out _), Is.False);
            Until(s, 110); Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }
        [Test]
        public void OrdinaryDamageDoesNotCancelCharge()
        {
            var s = Arena(); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            Until(s, 32); s.Entities.Health[1] -= 5; s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofAction(1, out var b), Is.True); Assert.That(b.Serial, Is.EqualTo(a.Serial));
        }
        [Test]
        public void AlliesCannotDeflectLockedLineOrReceiveChargeDamage()
        {
            var s = Arena(false, 3); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(6));
            for (int i = 2; i <= 3; i++) { s.Entities.Position[i] = a.Origin - new FixVec2(Fix64.FromInt(i), Fix64.Zero); s.Statuses.ApplyStun(i, 999); }
            while (s.Tick < a.StopTick)
            { s.Step(InputFrame.Empty); Assert.That(s.Entities.Position[1].Y.ToFloat(), Is.EqualTo(a.Origin.Y.ToFloat()).Within(.001f)); }
            Assert.That(s.Entities.Health[2], Is.EqualTo(180)); Assert.That(s.Entities.Health[3], Is.EqualTo(180));
        }
        [Test]
        public void RepeatClearsActionsAndRandomnessIsDeterministic()
        {
            var a = Arena(); var b = Arena();
            for (int t = 0; t < 1500; t++)
            { a.Step(InputFrame.Empty); b.Step(InputFrame.Empty); Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t); }
            a.SetupStonehoofEncounter(null, 76);
            Assert.That(a.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestStonehoof));
            Assert.That(a.Entities.Health[1], Is.EqualTo(180)); Assert.That(a.TryGetStonehoofAction(1, out _), Is.False);
        }
        [Test]
        public void AFailedRetreatStillAllowsFullWarningUpClose()
        {
            var s = Arena(); s.Entities.Position[0] = new FixVec2(Fix64.FromInt(4), Fix64.Zero);
            s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out var a), Is.True);
            Assert.That(a.LaunchTick - a.StartTick, Is.EqualTo(30)); Until(s, 30);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }
    }
}
