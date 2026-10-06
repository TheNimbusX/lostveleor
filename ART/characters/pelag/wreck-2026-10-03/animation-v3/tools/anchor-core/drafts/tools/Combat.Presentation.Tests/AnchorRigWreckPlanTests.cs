using Game.View;
using NUnit.Framework;

/// <summary>Решение рига по снимку Крушения (anchor-core DESIGN §1.4; поля — SPEC 2.6).</summary>
public sealed class AnchorRigWreckPlanTests
{
    private static AnchorRigWreckInput Stage(int stage, int side, byte phase = AnchorRigWreckInput.PhaseWindup, int serial = 3)
        => new AnchorRigWreckInput
        {
            Serial = serial, Phase = phase, Stage = stage, Side = side, StageStartTick = 100,
            ContactTick = stage == 0 ? 107 : stage == 1 ? 106 : 109, OverheadTick = stage == 2 ? 105 : -1,
            ChargeStartTick = -1, HasImpact = stage >= 2,
        };

    [Test]
    public void NoSeriesMeansRigDoesNothing()
    {
        Assert.That(AnchorRigWreckPlan.Decide(default).Kind, Is.EqualTo(AnchorWreckBeatKind.None));
        var ended = Stage(0, 1, AnchorRigWreckInput.PhaseNone);
        Assert.That(AnchorRigWreckPlan.Decide(ended).Kind, Is.EqualTo(AnchorWreckBeatKind.Stow));
    }

    [Test]
    public void StagesPickTheirBakesAndWindupVariants()
    {
        var swing = AnchorRigWreckPlan.Decide(Stage(0, 1));
        Assert.That(swing.Kind, Is.EqualTo(AnchorWreckBeatKind.Bake));
        Assert.That(swing.Clip, Is.EqualTo(AnchorRigWreckPlan.Swing1));
        Assert.That(swing.WindupTicks, Is.EqualTo(7));
        Assert.That(swing.CorrectContact, Is.False, "мах бьёт сектором — точку не подводим");
        var back = AnchorRigWreckPlan.Decide(Stage(1, -1, AnchorRigWreckInput.PhaseFollow));
        Assert.That(back.Clip, Is.EqualTo(AnchorRigWreckPlan.Swing2));
        Assert.That(back.WindupTicks, Is.EqualTo(6));
        var slam = AnchorRigWreckPlan.Decide(Stage(2, 0, AnchorRigWreckInput.PhaseHold));
        Assert.That(slam.Clip, Is.EqualTo(AnchorRigWreckPlan.Slam));
        Assert.That(slam.CorrectContact, Is.True);
        Assert.That(slam.OverheadTick, Is.EqualTo(105));
        Assert.That(AnchorRigWreckPlan.BakeName(slam.Clip, slam.WindupTicks), Is.EqualTo("Pelag_AN_Wreck2_Slam_w9"));
        Assert.That(AnchorRigWreckPlan.BakeName(AnchorRigWreckPlan.ChargeRelease, 0, 3), Is.EqualTo("Pelag_AN_Wreck2_ChargeRelease_p3"));
        Assert.That(AnchorRigWreckPlan.BakeName(AnchorRigWreckPlan.ChargeLoop, 0), Is.EqualTo("Pelag_AN_Wreck2_ChargeLoop"));
    }

    [Test]
    public void WrongSideIsNotMirroredButLeftLive()
    {
        var beat = AnchorRigWreckPlan.Decide(Stage(1, 1));
        Assert.That(beat.Kind, Is.EqualTo(AnchorWreckBeatKind.Live));
        Assert.That(beat.SideMismatch, Is.True);
    }

    [Test]
    public void NinthWaveSpinsThenReleasesByPhase()
    {
        var charge = Stage(2, 0, AnchorRigWreckInput.PhaseCharge);
        charge.ChargeStartTick = 105;
        var loop = AnchorRigWreckPlan.Decide(charge);
        Assert.That(loop.Kind, Is.EqualTo(AnchorWreckBeatKind.Loop));
        Assert.That(loop.Clip, Is.EqualTo(AnchorRigWreckPlan.ChargeLoop));
        var release = charge;
        release.Phase = AnchorRigWreckInput.PhaseWindup;
        release.ContactTick = 130;
        var hit = AnchorRigWreckPlan.Decide(release);
        Assert.That(hit.Clip, Is.EqualTo(AnchorRigWreckPlan.ChargeRelease));
        Assert.That(hit.Segment, Is.Not.EqualTo(loop.Segment));
        int variant = AnchorRigWreckPlan.ReleaseVariant(release, 4, 12, out float start);
        Assert.That(variant, Is.InRange(0, 7));
        Assert.That(126 - start, Is.InRange(-.76f, .76f), "сдвиг старта не больше половины шага варианта");
    }

    [Test]
    public void FourthStrikeHasNoBakeYetAndStaysLive()
        => Assert.That(AnchorRigWreckPlan.Decide(Stage(3, 0)).Kind, Is.EqualTo(AnchorWreckBeatKind.Live));

    [Test]
    public void SegmentsDifferPerStageAndSeries()
    {
        int a = AnchorRigWreckPlan.Decide(Stage(0, 1)).Segment;
        int b = AnchorRigWreckPlan.Decide(Stage(1, -1)).Segment;
        int c = AnchorRigWreckPlan.Decide(Stage(0, 1, serial: 4)).Segment;
        int d = AnchorRigWreckPlan.Decide(Stage(0, 1, AnchorRigWreckInput.PhaseWindow)).Segment;
        Assert.That(new[] { a, b, c }, Is.Unique);
        Assert.That(d, Is.EqualTo(a), "окно того же этапа — та же запечка, без новой сшивки");
    }
}
