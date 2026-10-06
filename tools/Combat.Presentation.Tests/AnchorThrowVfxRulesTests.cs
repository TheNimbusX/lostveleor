using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// Вид Броска якоря без Unity (PelagAnchorThrowVfxRules): голова полосы — формула Sim в целых тиках и отрезок
    /// между ними (спека 2.9 п. 8), фазы рига anchor-core по тикам Sim, видимый край сети = край урона, высота и
    /// дёрг головы, проявление призраков Веера. Снимок собран как в Sim (Simulation.AnchorThrow.Flow, бросок 7 м).
    /// </summary>
    public sealed class AnchorThrowVfxRulesTests
    {
        private const int Cast = 100;

        private static AnchorThrowState Throw(Fix64 reach, int lanes = 1)
        {
            int flight = Simulation.AnchorThrowFlightTicks(reach);
            Fix64 step = (reach - Simulation.AbordageHandReach) / Fix64.FromInt(flight);
            var s = new AnchorThrowState
            {
                Serial = 1, Slot = 0, Phase = AnchorThrowPhase.Flight, HarpoonTarget = -1, Lanes = lanes,
                Center = new FixVec2(Fix64.Zero, Fix64.Zero), Dir = new FixVec2(Fix64.One, Fix64.Zero),
                Reach0 = reach, Step0 = step, Flight0 = flight, HalfWidth = Fix64.Ratio(45, 100),
                CastTick = Cast, WindupTicks = 2, ReleaseTick = Cast + 2,
            };
            s.Origin = s.Center + s.Dir * Simulation.AbordageHandReach;
            if (lanes == 3)
            {
                s.Dir1 = new FixVec2(Simulation.AnchorThrowFanCos, Simulation.AnchorThrowFanSin);
                s.Dir2 = new FixVec2(Simulation.AnchorThrowFanCos, -Simulation.AnchorThrowFanSin);
                s.Reach1 = s.Reach2 = reach; s.Step1 = s.Step2 = step; s.Flight1 = s.Flight2 = flight;
            }
            s.FlightTicks = flight;
            s.TautTick = s.ReleaseTick + flight + 1;
            s.ReturnTicks = Simulation.AnchorThrowReturnTicks(reach);
            s.CatchTick = s.TautTick + s.ReturnTicks;
            return s;
        }

        private static AnchorThrowState Throw7(int lanes = 1) => Throw(Fix64.FromInt(7), lanes);

        [Test]
        public void Timeline_7m_MatchesSpec()
        {
            AnchorThrowState s = Throw7();
            Assert.That(s.ReleaseTick, Is.EqualTo(Cast + 2));
            Assert.That(PelagAnchorThrowVfxRules.LaneArriveTick(s, 0), Is.EqualTo(Cast + 9));
            Assert.That(s.TautTick, Is.EqualTo(Cast + 10));
            Assert.That(s.CatchTick, Is.EqualTo(Cast + 18));
        }

        [Test]
        public void Head_AtWholeTicks_IsTheSimFormula()
        {
            AnchorThrowState s = Throw7();
            for (int tick = Cast - 1; tick <= Cast + 21; tick++)
            {
                FixVec2 sim = Simulation.AnchorThrowHead(s, 0, tick);
                PelagAnchorThrowVfxRules.Head(s, 0, tick, out float x, out float z);
                Assert.That(x, Is.EqualTo(sim.X.ToFloat()).Within(1e-4f), "x, тик " + tick);
                Assert.That(z, Is.EqualTo(sim.Y.ToFloat()).Within(1e-4f), "z, тик " + tick);
            }
        }

        [Test]
        public void Head_BetweenTicks_IsOnTheSegment()
        {
            AnchorThrowState s = Throw7();
            for (int tick = s.ReleaseTick; tick < s.CatchTick; tick++)
            {
                float a = Simulation.AnchorThrowHead(s, 0, tick).X.ToFloat();
                float b = Simulation.AnchorThrowHead(s, 0, tick + 1).X.ToFloat();
                PelagAnchorThrowVfxRules.Head(s, 0, tick + .25f, out float x, out _);
                Assert.That(x, Is.EqualTo(a + (b - a) * .25f).Within(1e-4f), "тик " + tick);
            }
        }

        [Test]
        public void Head_IsAtTheEndOnTaut_AndInTheHandOnCatch()
        {
            AnchorThrowState s = Throw7();
            Assert.That(PelagAnchorThrowVfxRules.Along(s, 0, s.TautTick), Is.EqualTo(7f).Within(1e-3f));
            Assert.That(PelagAnchorThrowVfxRules.Along(s, 0, s.CatchTick), Is.EqualTo(.5f).Within(1e-3f));
            Assert.That(PelagAnchorThrowVfxRules.Along(s, 0, s.ReleaseTick), Is.EqualTo(.5f).Within(1e-3f));
        }

        [Test]
        public void RigPhase_FollowsSimTicks()
        {
            AnchorThrowState s = Throw7();
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, Cast), Is.EqualTo(AnchorThrowRigPhase.Windup));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, s.ReleaseTick), Is.EqualTo(AnchorThrowRigPhase.Fly));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, s.TautTick - .01f), Is.EqualTo(AnchorThrowRigPhase.Fly));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, s.TautTick), Is.EqualTo(AnchorThrowRigPhase.Hold));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, s.TautTick + 1), Is.EqualTo(AnchorThrowRigPhase.Yank));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(s, s.CatchTick), Is.EqualTo(AnchorThrowRigPhase.Done));
            Assert.That(PelagAnchorThrowVfxRules.RigPhase(default, Cast), Is.EqualTo(AnchorThrowRigPhase.None));
        }

        [Test]
        public void RigAlong_FromTheHand_AlongTheSimLine()
        {
            AnchorThrowState s = Throw7();
            Assert.That(PelagAnchorThrowVfxRules.RigAlong(s, s.ReleaseTick), Is.EqualTo(0f).Within(1e-3f));
            // Центр головы рига — в точке Sim: кольцо ближе к руке на 0,38 м.
            Assert.That(PelagAnchorThrowVfxRules.RigAlong(s, s.TautTick), Is.EqualTo(6.5f - PelagAnchorThrowVfxRules.RingToCentre).Within(1e-3f));
            Assert.That(PelagAnchorThrowVfxRules.RigAlong(s, s.CatchTick), Is.EqualTo(0f).Within(1e-3f));
            // Полёт ≈ 28 м/с от руки, возврат — к руке.
            Assert.That(PelagAnchorThrowVfxRules.RigAlongSpeed(s, s.ReleaseTick + 3.5f), Is.EqualTo(6.5f / 7f * 30f).Within(.05f));
            Assert.That(PelagAnchorThrowVfxRules.RigAlongSpeed(s, s.TautTick + 4.5f), Is.LessThan(-10f));
        }

        [Test]
        public void NetVisibleEdge_IsTheDamageEdge()
        {
            float edge = PelagAnchorThrowVfxRules.NetWaterHalfWidth + PelagAnchorThrowVfxRules.NetCrest;
            Assert.That(edge, Is.EqualTo(Simulation.AnchorThrowNetHalfWidth.ToFloat()).Within(1e-5f));
        }

        [Test]
        public void Net_OpensAroundTaut_AndGathersBehindTheHead()
        {
            AnchorThrowState s = Throw7();
            Assert.That(PelagAnchorThrowVfxRules.NetOpen(s.TautTick - 1.01f, s.TautTick), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowVfxRules.NetOpen(s.TautTick, s.TautTick), Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.NetOpen(s.TautTick + 1f, s.TautTick), Is.EqualTo(1f));
            Assert.That(PelagAnchorThrowVfxRules.NetFar(s, s.TautTick), Is.EqualTo(7f).Within(1e-3f));
            float mid = PelagAnchorThrowVfxRules.NetFar(s, s.TautTick + 4);
            Assert.That(mid, Is.LessThan(7f));
            Assert.That(mid, Is.GreaterThan(.5f));
            Assert.That(PelagAnchorThrowVfxRules.NetFar(s, s.CatchTick), Is.EqualTo(.5f).Within(1e-3f));
        }

        [Test]
        public void HeadHeight_FlatThrow_LowReturn_IntoTheHand()
        {
            AnchorThrowState s = Throw7();
            const float release = 1.3f, hand = 1.1f;
            Assert.That(PelagAnchorThrowVfxRules.HeadHeight(s, 0, s.ReleaseTick, release, hand), Is.EqualTo(release).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.HeadHeight(s, 0, Cast + 9, release, hand), Is.EqualTo(PelagAnchorThrowVfxRules.FlyEndHeight).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.HeadHeight(s, 0, s.TautTick + 3, release, hand), Is.EqualTo(PelagAnchorThrowVfxRules.ReturnHeight).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.HeadHeight(s, 0, s.CatchTick, release, hand), Is.EqualTo(hand).Within(1e-4f));
        }

        [Test]
        public void Jerk_OnlyInTheTautTick()
        {
            Assert.That(PelagAnchorThrowVfxRules.Jerk(109.9f, 110), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowVfxRules.Jerk(110.5f, 110), Is.EqualTo(PelagAnchorThrowVfxRules.JerkAmplitude).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.Jerk(111f, 110), Is.EqualTo(0f));
        }

        [Test]
        public void FanGhostLanes_EndOnTheirOwnLines()
        {
            AnchorThrowState s = Throw7(3);
            for (int lane = 1; lane <= 2; lane++)
            {
                FixVec2 end = s.Center + s.LaneDir(lane) * s.LaneReach(lane);
                PelagAnchorThrowVfxRules.Head(s, lane, s.TautTick, out float x, out float z);
                Assert.That(x, Is.EqualTo(end.X.ToFloat()).Within(1e-3f), "полоса " + lane);
                Assert.That(z, Is.EqualTo(end.Y.ToFloat()).Within(1e-3f), "полоса " + lane);
            }
            Assert.That(PelagAnchorThrowVfxRules.LaneCount(s), Is.EqualTo(3));
            Assert.That(PelagAnchorThrowVfxRules.GhostOpacity(s, s.ReleaseTick - .5f), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowVfxRules.GhostOpacity(s, s.TautTick), Is.EqualTo(1f));
            Assert.That(PelagAnchorThrowVfxRules.GhostOpacity(s, s.CatchTick + 4), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(PelagAnchorThrowVfxRules.GhostDissolve(s, s.CatchTick + 2), Is.EqualTo(.5f).Within(1e-4f));
        }

        [Test]
        public void Tension_HandFlightTautCatch()
        {
            AnchorThrowState s = Throw7();
            Assert.That(PelagAnchorThrowVfxRules.Tension(s, Cast), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowVfxRules.Tension(s, Cast + 5), Is.EqualTo(PelagAnchorThrowVfxRules.TensionFlight));
            Assert.That(PelagAnchorThrowVfxRules.Tension(s, s.TautTick + 2), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PelagAnchorThrowVfxRules.Tension(s, s.CatchTick), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowVfxRules.SleeveHalfWidth(1f, PelagForm.AnchorThrowHarpoon),
                Is.LessThan(PelagAnchorThrowVfxRules.SleeveHalfWidth(1f, PelagForm.None)));
        }

        [Test]
        public void ShownTick_IsTwoTicksBehindTheSim()
        {
            Assert.That(PelagAnchorThrowVfxRules.ShownTick(120, .25f), Is.EqualTo(118.25f).Within(1e-5f));
            Assert.That(PelagAnchorThrowVfxRules.ClosestOnSegment(0f, 0f, 4f, 0f, 1f, 3f), Is.EqualTo(.25f).Within(1e-5f));
            Assert.That(PelagAnchorThrowVfxRules.ClosestOnSegment(0f, 0f, 4f, 0f, -2f, 1f), Is.EqualTo(0f));
        }
    }
}
