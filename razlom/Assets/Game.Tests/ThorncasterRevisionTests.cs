using System.Collections.Generic;
using System.Reflection;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed partial class ThorncasterTests
    {
        private static ThorncasterState[] ThornStates(Simulation sim)
            => (ThorncasterState[])typeof(Simulation).GetField("_thorncasters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);

        private static Simulation ShotSequenceArena(LayoutMap map = null)
        {
            var sim = ShotArena(map);
            ThornStates(sim)[1].NextLineTick = 100000;
            ThornStates(sim)[1].NextBurstTick = 100000;
            sim.Entities.MoveStep[1] = Fix64.Zero;
            return sim;
        }

        private static void CollectLaunches(Simulation sim, int until, List<int> ticks, List<int> ids = null)
        {
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyProjectileLaunched && e.Source == 1)
                    { ticks.Add(tick); ids?.Add(e.Amount); }
            }
        }

        [Test]
        public void NormalShotsRepeatOneOneTwo_WithUniqueProjectileIdsAndUnchangedCooldown()
        {
            var sim = ShotSequenceArena();
            var releases = new List<int>(); var ids = new List<int>(); var starts = new List<int>();
            var impacts = new List<int>();
            for (int tick = 0; tick < 370; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Source != 1 || e.ActionVariant != (int)EnemyActionKind.ThornShot) continue;
                    if (e.Type == SimEventType.EnemyActionStarted) starts.Add(tick);
                    if (e.Type == SimEventType.EnemyProjectileLaunched) { releases.Add(tick); ids.Add(e.Amount); }
                    if (e.Type == SimEventType.EnemyActionImpact) impacts.Add(e.Amount);
                }
                if (sim.TryGetThorncasterAction(1, out var a))
                    Assert.That(a.NextShotTick, Is.EqualTo(a.StartTick + 60));
            }
            // Раскладка 1-1-2 из констант: выпуск через замах от начала, следующий выстрел —
            // через перезарядку; у каждого третьего второй выпуск через шаг двойного, стойка
            // после него, и следующее начало не раньше конца этой стойки.
            var expectedStarts = new List<int>(); var expectedReleases = new List<int>();
            for (int start = 0, n = 0; start < 370; n++)
            {
                expectedStarts.Add(start);
                int release = start + Simulation.ThornShotWindupTicks;
                if (release < 370) expectedReleases.Add(release);
                if (n % 3 == 2)
                {
                    release += Simulation.ThornDoubleShotReleaseSpacingTicks;
                    if (release < 370) expectedReleases.Add(release);
                }
                start = System.Math.Max(start + Simulation.ThornShotCooldownTicks, release + Simulation.ThornShotRecoveryTicks);
            }
            Assert.That(releases, Is.EqualTo(expectedReleases));
            Assert.That(starts, Is.EqualTo(expectedStarts));
            Assert.That(new HashSet<int>(ids).Count, Is.EqualTo(ids.Count));
            Assert.That(impacts, Is.EqualTo(ids));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - releases.Count * sim.ThornShotDamageOf(1)));
        }

        [Test]
        public void DoubleShotReacquiresAtNineTicks_ThenKeepsFullWindupFacingFrozen()
        {
            var sim = ShotSequenceArena();
            Until(sim, 142);
            Assert.That(sim.TryGetThorncasterAction(1, out var first), Is.True);
            Assert.That(first.SecondShotWindupTick, Is.EqualTo(150));
            Assert.That(first.SecondShotReleaseTick, Is.EqualTo(171));
            Assert.That(first.EndTick, Is.EqualTo(183));
            var target = first.Origin + At(0, 9);
            sim.Entities.Position[0] = target;
            Until(sim, 150);
            Assert.That(sim.Entities.Facing[1], Is.EqualTo(first.Direction), "no tracking during first recovery");
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var second);
            var direction = (target - second.Origin).Normalized();
            Assert.That(second.SecondShotAimed, Is.True);
            Assert.That(second.Direction, Is.EqualTo(direction));
            Assert.That(sim.Entities.Facing[1], Is.EqualTo(direction));
            sim.Entities.Position[0] = second.Origin + At(9, 0);
            while (sim.Tick < 172)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Facing[1], Is.EqualTo(direction));
            }
            Assert.That(sim.TryGetThornShot(1, out var shot), Is.True);
            Assert.That(shot.ReleaseTick, Is.EqualTo(171));
            Assert.That(shot.Direction, Is.EqualTo(direction));
            Assert.That(shot.Serial, Is.EqualTo(4));
        }

        [Test]
        public void CancelBeforeFirstReleaseDoesNotAdvanceOneOneTwoCounter()
        {
            var sim = ShotSequenceArena();
            Until(sim, 10);
            sim.Statuses.ApplyStun(1, 25);
            var releases = new List<int>();
            CollectLaunches(sim, 240, releases);
            Assert.That(releases, Is.EqualTo(new[] { 81, 141, 201, 231 }));
        }

        // Оглушение и смерть — по разу, в два разных тика.
        [TestCase(146, false)]
        [TestCase(160, true)]
        public void StunOrDeathCancelsUnreleasedSecond_ButLaunchedFirstKeepsFlying(int cancelTick, bool kill)
        {
            var sim = ShotSequenceArena();
            Until(sim, 142);
            sim.Entities.Position[0] = sim.Entities.Position[1] + At(0, 9);
            Until(sim, cancelTick);
            bool firstInFlight = sim.TryGetThornShot(1, out var first);
            if (kill) sim.ApplyAbilityDamage(0, 1, 1000000, -1, DamageType.Physical);
            else sim.Statuses.ApplyStun(1, 1000);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False);
            if (firstInFlight)
            {
                Assert.That(sim.TryGetThornShot(1, out var after), Is.True);
                Assert.That(after.Serial, Is.EqualTo(first.Serial));
                Assert.That(after.Travelled, Is.GreaterThan(first.Travelled));
            }
            var releases = new List<int>();
            CollectLaunches(sim, 185, releases);
            Assert.That(releases, Is.Empty);
        }

        [TestCase(2.0)]
        [TestCase(11.0)]
        public void SecondShotIsSkippedWhenHeroLeavesAllowedRange(double distance)
        {
            var sim = ShotSequenceArena();
            Until(sim, 149);
            sim.Entities.Position[0] = sim.Entities.Position[1] + At(0, distance);
            var releases = new List<int>();
            CollectLaunches(sim, 175, releases);
            Assert.That(releases, Is.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False);
            Assert.That(ThornStates(sim)[1].ShotsSinceDouble, Is.Zero);
        }

        [Test]
        public void SecondShotIsSkippedWhenNewTargetIsBehindCover()
        {
            var map = Glade();
            var sim = ShotSequenceArena(map);
            Until(sim, 149);
            var origin = sim.Entities.Position[1];
            sim.Entities.Position[0] = origin + At(0, 6);
            map.AddTestObstacle(new LayoutObstacle(origin + At(0, 3), Fix64.FromDouble(.5), 0));
            var releases = new List<int>();
            CollectLaunches(sim, 175, releases);
            Assert.That(releases, Is.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False);
        }

        [Test]
        public void DoubleShotTimersCounterAndProjectileSerialParticipateInHash()
        {
            var a = ShotSequenceArena(); var b = ShotSequenceArena();
            for (int tick = 0; tick < 240; tick++)
            {
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + tick);
            }
            var sim = ShotSequenceArena(); Until(sim, 151);
            var states = ThornStates(sim); var cleanState = states[1]; var cleanHash = sim.StateHash();
            states[1].ShotsSinceDouble++;
            Assert.That(sim.StateHash(), Is.Not.EqualTo(cleanHash)); states[1] = cleanState;
            states[1].SecondShotWindupTick++;
            Assert.That(sim.StateHash(), Is.Not.EqualTo(cleanHash)); states[1] = cleanState;
            states[1].SecondShotReleaseTick++;
            Assert.That(sim.StateHash(), Is.Not.EqualTo(cleanHash)); states[1] = cleanState;
            states[1].SecondShotAimed = !states[1].SecondShotAimed;
            Assert.That(sim.StateHash(), Is.Not.EqualTo(cleanHash)); states[1] = cleanState;
            var serial = typeof(Simulation).GetField("_thornShotSerial", BindingFlags.Instance | BindingFlags.NonPublic);
            int value = (int)serial.GetValue(sim); serial.SetValue(sim, value + 1);
            Assert.That(sim.StateHash(), Is.Not.EqualTo(cleanHash)); serial.SetValue(sim, value);
            Assert.That(sim.StateHash(), Is.EqualTo(cleanHash));
            sim.SetupKindTestArena(EnemyKind.ForestThorncaster);
            Assert.That(ThornStates(sim)[1].ShotsSinceDouble, Is.Zero);
            Assert.That((int)serial.GetValue(sim), Is.Zero);
        }

        [Test]
        public void BurstPushesOutwardOnePointFiveMetresOverEightTicks_WithoutStun()
        {
            var sim = Arena(distance: 2);
            Until(sim, 21);
            var from = sim.Entities.Position[0]; var origin = sim.Entities.Position[1];
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[0], Is.EqualTo(from));
            Assert.That(sim.Entities.ForcedTicksLeft[0], Is.EqualTo(8));
            Assert.That(sim.Entities.ForcedKind[0], Is.EqualTo((byte)ForcedMotionKind.Knockback));
            var target = from + (from - origin).Normalized() * Fix64.Ratio(3, 2);
            for (int step = 1; step <= 8; step++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Distance(sim.Entities.Position[0], from).ToDouble(), Is.EqualTo(1.5 * step / 8).Within(.002));
                Assert.That(sim.Statuses.IsStunned(0, sim.Tick), Is.False);
            }
            Assert.That(FixVec2.Distance(sim.Entities.Position[0], target).ToDouble(), Is.LessThan(.002));
            Assert.That(ForcedMotion.IsActive(sim.Entities, 0), Is.False);
        }

        [Test]
        public void BurstPushStopsBeforeObstacleAndDoesNotSlideSideways()
        {
            var map = Glade(-1.6, .3);
            var sim = Arena(map: map, distance: 2);
            Until(sim, 21);
            var from = sim.Entities.Position[0];
            sim.Step(InputFrame.Empty);
            var target = sim.Entities.ForcedTarget[0];
            Assert.That(FixVec2.Distance(from, target).ToDouble(), Is.InRange(.5, 1.0));
            for (int step = 0; step < 8; step++)
            {
                var previous = sim.Entities.Position[0]; sim.Step(InputFrame.Empty);
                Assert.That(map.CanTravel(previous, sim.Entities.Position[0], sim.Entities.BodyRadius[0]), Is.True);
                Assert.That(sim.Entities.Position[0].Y, Is.EqualTo(from.Y));
            }
            Assert.That(sim.Entities.Position[0], Is.EqualTo(target));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BurstRespectsImmunityAndExistingForcedMotion(bool immune)
        {
            var sim = Arena(distance: 2);
            Until(sim, 21);
            var target = sim.Entities.Position[0] + At(0, .1);
            if (immune) sim.PlayerInvulnerable = true;
            else ForcedMotion.Begin(sim.Entities, 0, target, 20, ForcedMotionKind.Lunge);
            sim.Step(InputFrame.Empty);
            if (immune)
            {
                Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
                Assert.That(ForcedMotion.IsActive(sim.Entities, 0), Is.False);
            }
            else
            {
                Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 22));
                Assert.That(sim.Entities.ForcedKind[0], Is.EqualTo((byte)ForcedMotionKind.Lunge));
                Assert.That(sim.Entities.ForcedTarget[0], Is.EqualTo(target));
            }
        }
    }
}
