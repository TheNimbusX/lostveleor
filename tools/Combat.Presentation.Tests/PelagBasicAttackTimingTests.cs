using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Contact confirmation may reach presentation while interpolated bodies are
/// still one tick behind. These tests replay render samples across that exact
/// boundary, rather than asserting a second copy of the timing formula.
/// </summary>
public sealed class PelagBasicAttackTimingTests
{
    const float AuthoredContact = .42f;

    static PelagBasicAttackState Action(int stage, bool fast = false)
    {
        int start = 11;
        int windup = fast ? 2 : stage == 2 ? 6 : 4;
        int duration = fast ? 4 : stage == 2 ? 12 : 9;
        return new PelagBasicAttackState { Serial = 31, Stage = stage, StartTick = start,
            ContactTick = start + windup, EndTick = start + duration,
            Direction = new FixVec2(Fix64.One, Fix64.Zero) };
    }

    [TestCase(30, 0)] [TestCase(30, 1)] [TestCase(30, 2)]
    [TestCase(60, 0)] [TestCase(60, 1)] [TestCase(60, 2)]
    [TestCase(120, 0)] [TestCase(120, 1)] [TestCase(120, 2)]
    public void ConfirmedDamageFrameAlreadyShowsTheAuthoredContactPose(int fps, int stage)
        => CheckRenderSamples(fps, stage, false);

    [TestCase(30, 0)] [TestCase(30, 2)]
    [TestCase(60, 0)] [TestCase(60, 2)]
    [TestCase(120, 0)] [TestCase(120, 2)]
    public void TwoTickFastWindupStillAlignsContactWithoutSkippingOrRewinding(int fps, int stage)
        => CheckRenderSamples(fps, stage, true);

    static void CheckRenderSamples(int fps, int stage, bool fast)
    {
        var action = Action(stage, fast);
        double firstDamageTime = -1, firstBladeTime = -1;
        float previous = 0;
        bool checkedConfirmation = false;
        int endFrame = (int)Math.Ceiling((action.EndTick + 2) * fps / 30.0);
        for (int frame = 0; frame <= endFrame; frame++)
        {
            double time = frame / (double)fps;
            float simulationClock = (float)(time * Simulation.TicksPerSecond);
            // A damage event confirms the current simulation contact. The
            // body interpolation clock can still sample the preceding tick.
            action.ContactProcessed = simulationClock + .00001f >= action.ContactTick;
            float interpolatedTick = simulationClock - 1f;
            float observed = PelagBasicAttackTiming.ObservedTick(action, interpolatedTick);
            float phase = PelagBasicAttackTiming.Phase(action, observed, AuthoredContact);
            Assert.That(phase, Is.InRange(0f, 1f));
            Assert.That(phase + .00001f, Is.GreaterThanOrEqualTo(previous), "recovery rewound on render frame " + frame);
            if (!action.ContactProcessed)
                Assert.That(phase, Is.LessThan(AuthoredContact), "blade hit before its simulation contact");
            if (action.ContactProcessed && firstDamageTime < 0) firstDamageTime = time;
            if (phase + .00001f >= AuthoredContact && firstBladeTime < 0) firstBladeTime = time;
            if (action.ContactProcessed && !checkedConfirmation)
            {
                Assert.That(phase, Is.EqualTo(AuthoredContact).Within(.00001f),
                    "processed damage is visible while the blade is still in windup");
                checkedConfirmation = true;
            }
            previous = phase;
        }
        Assert.That(checkedConfirmation, Is.True);
        Assert.That(firstBladeTime - firstDamageTime, Is.InRange(0.0, 1.0 / fps + .000001),
            "the old uncorrected interpolation lag delays contact by four frames at 120 FPS");
        Assert.That(previous, Is.EqualTo(1f));
    }

    [TestCase(.17f)] [TestCase(.42f)] [TestCase(.73f)]
    public void AuthoredMarkerIsHonoredForBothLongAndMinimumLengthActions(float marker)
    {
        foreach (bool fast in new[] { false, true })
        foreach (int stage in new[] { 0, 1, 2 })
        {
            var action = Action(stage, fast);
            Assert.That(PelagBasicAttackTiming.Phase(action, action.ContactTick, marker),
                Is.EqualTo(marker).Within(.000001f), "contact must use the clip's authored marker");
            Assert.That(PelagBasicAttackTiming.Phase(action, action.ContactTick - .01f, marker), Is.LessThan(marker));
            Assert.That(PelagBasicAttackTiming.Phase(action, action.ContactTick + .01f, marker), Is.GreaterThan(marker));
            Assert.That(PelagBasicAttackTiming.Phase(action, action.StartTick - 10, marker), Is.Zero);
            Assert.That(PelagBasicAttackTiming.Phase(action, action.EndTick + 10, marker), Is.EqualTo(1f));
        }
    }

    [Test]
    public void HitConfirmationDoesNotRewindRecoveryWhenTheRenderClockCatchesUp()
    {
        var action = Action(2); action.ContactProcessed = true;
        float previous = AuthoredContact;
        // Includes one frame before contact, the confirmation clamp, and the
        // transition back to normal interpolation during follow-through.
        for (float tick = action.ContactTick - 1; tick <= action.EndTick + 1; tick += .25f)
        {
            float observed = PelagBasicAttackTiming.ObservedTick(action, tick);
            float phase = PelagBasicAttackTiming.Phase(action, observed, AuthoredContact);
            Assert.That(phase, Is.GreaterThanOrEqualTo(previous));
            previous = phase;
        }
        Assert.That(previous, Is.EqualTo(1f));
    }

    [Test]
    public void ConfirmationClampRequiresRealConfirmationAndNeverPullsALateClockBackward()
    {
        var action = Action(0);
        float previousTick = action.ContactTick - .75f;
        Assert.That(PelagBasicAttackTiming.Phase(action,
            PelagBasicAttackTiming.ObservedTick(action, previousTick), AuthoredContact), Is.LessThan(AuthoredContact));
        action.ContactProcessed = true;
        Assert.That(PelagBasicAttackTiming.Phase(action,
            PelagBasicAttackTiming.ObservedTick(action, previousTick), AuthoredContact), Is.EqualTo(AuthoredContact));
        float alreadyLate = action.ContactTick + .75f;
        float progressed = PelagBasicAttackTiming.Phase(action, alreadyLate, AuthoredContact);
        Assert.That(PelagBasicAttackTiming.Phase(action,
            PelagBasicAttackTiming.ObservedTick(action, alreadyLate), AuthoredContact), Is.EqualTo(progressed));
        Assert.That(progressed, Is.GreaterThan(AuthoredContact));
    }

    [TestCase(0, false, false)] [TestCase(1, false, true)] [TestCase(2, false, false)]
    [TestCase(0, true, false)] [TestCase(1, true, false)] [TestCase(2, true, true)]
    public void LegacySecondHitAndComboFinisherUseTheirOwnHeavySoundClassification(int stage, bool combo, bool heavy)
        => Assert.That(PelagBasicAttackTiming.Heavy(stage, combo), Is.EqualTo(heavy));

    [Test]
    public void AttackSpeedChangeAndNextSwingCannotRetimingTheAlreadyPublishedAction()
    {
        var sim = new Simulation(834, 32); sim.SetupTestArena(0); sim.EnablePelagBasicCombo();
        var input = InputFrame.Empty;
        input.Aim = new FixVec2(Fix64.FromInt(10), Fix64.Zero);
        input.Flags = (byte)InputFlags.AttackPressed;
        sim.Step(input);
        PelagBasicAttackState frozen = default;
        foreach (var e in sim.Events) if (e.Type == SimEventType.Attack) frozen = e.BasicAttackState;
        Assert.That(frozen.Serial, Is.GreaterThan(0));
        float beforeSpeedChange = PelagBasicAttackTiming.Phase(frozen, frozen.ContactTick, AuthoredContact);
        float whoosh = PelagBasicAttackTiming.WhooshTick(frozen);
        sim.Entities.Stats[0].SetBase(StatType.AttackSpeed, Fix64.FromInt(300)); sim.RefreshPlayerStats(false);
        while (sim.Tick < frozen.EndTick) sim.Step(InputFrame.Empty);
        sim.Step(input);
        Assert.That(sim.PelagBasicAttack.Serial, Is.GreaterThan(frozen.Serial));
        Assert.That(sim.PelagBasicAttack.ContactTick - sim.PelagBasicAttack.StartTick, Is.EqualTo(2),
            "the speed change really affected a later swing");
        Assert.That(frozen.ContactTick - frozen.StartTick, Is.EqualTo(4), "the published action retained its original windup");
        Assert.That(frozen.EndTick - frozen.StartTick, Is.EqualTo(9));
        Assert.That(PelagBasicAttackTiming.Phase(frozen, frozen.ContactTick, AuthoredContact), Is.EqualTo(beforeSpeedChange));
        Assert.That(PelagBasicAttackTiming.WhooshTick(frozen), Is.EqualTo(whoosh));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void WhooshHappensInsideEachWindupBeforeContactEvenAtMinimumDuration(int stage)
    {
        foreach (bool fast in new[] { false, true })
        {
            var action = Action(stage, fast);
            float whoosh = PelagBasicAttackTiming.WhooshTick(action);
            float pose = PelagBasicAttackTiming.Phase(action, whoosh, AuthoredContact);
            Assert.That(whoosh, Is.GreaterThan(action.StartTick).And.LessThan(action.ContactTick));
            Assert.That(pose, Is.GreaterThan(0f).And.LessThan(AuthoredContact),
                "swing sound must precede the confirmed hit sound");
        }
    }
}
