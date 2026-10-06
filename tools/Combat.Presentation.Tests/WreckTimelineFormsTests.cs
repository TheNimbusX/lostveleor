using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение v4: пауза между нажатиями, окно прошло, выход сорван ходьбой, Девятый вал, поворот к направлению этапа.
public sealed partial class WreckTimelineSimTests
{
    [Test]
    public void PauseBeforeSecondPress_SwingHoldsItsTail_BlendsIntoSwing2()
    {
        var sim = Arena();
        int second = 5 + 15;
        Run run = Play(sim, 60, i => Input(i == 0 || (i >= second && i < second + 25)));
        Assert.That(run.EndReason, Is.EqualTo(WreckEnd.Done));
        int contact = run.Contact[0];
        Assert.That(run.Line.Sample(contact + 1.5f, Scale).LegsFree, Is.False, "2 тика после удара героя держат");
        PelagWreckPose follow = run.Line.Sample(contact + 3, Scale);
        Assert.That(follow.Clip, Is.EqualTo(PelagWreckClip.Swing1));
        Assert.That(follow.LegsFree, Is.True, "окно: ходьба с удар + 2");
        Assert.That(follow.NextStarted, Is.False, "нажатия ещё нет — риг отпускает голову маятником");
        PelagWreckPose hold = run.Line.Sample(contact + 12, Scale);
        Assert.That(hold.Clip, Is.EqualTo(PelagWreckClip.Swing1));
        Assert.That(hold.Frame, Is.EqualTo(PelagWreckClipRules.SwingLast).Within(1e-3f), "хвост доигран — стойка с якорем держится");
        PelagWreckPose at = run.Line.Sample(run.StageStart[1], Scale);
        Assert.That(at.Clip, Is.EqualTo(PelagWreckClip.Swing2));
        Assert.That(at.Frame, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(at.EntryBlendTicks, Is.EqualTo(PelagWreckClipRules.SeamBlendTicks), "после стыка — смешивание в кадр 0");
        Assert.That(run.Line.Sample(run.Contact[1], Scale).Frame, Is.EqualTo(5f).Within(1e-3f));
    }

    [Test]
    public void NoSecondPress_WindowExpires_AnchorIsStowed()
    {
        var sim = Arena();
        Run run = Play(sim, 45, i => Input(i == 0));
        Assert.That(run.EndReason, Is.EqualTo(WreckEnd.WindowExpired));
        List<PelagWreckClip> clips = Sweep(run, out _);
        Assert.That(clips, Is.EqualTo(new[] { PelagWreckClip.Draw, PelagWreckClip.Swing1, PelagWreckClip.Stow }));
        PelagWreckPose stow = run.Line.Sample(run.EndTick, Scale);
        Assert.That(stow.Clip, Is.EqualTo(PelagWreckClip.Stow));
        Assert.That(stow.EntryBlendTicks, Is.EqualTo(PelagWreckClipRules.SeamBlendTicks), "из стойки маха — смешиванием");
        Assert.That(stow.LegsFree, Is.True);
        Assert.That(run.Line.Sample(run.EndTick + PelagWreckClipRules.StowLast, Scale).Finished, Is.True);
    }

    [Test]
    public void WalkOutOfTheLungeExit_StowStartsAtTheEnd_WithBlend()
    {
        var sim = Arena();
        Run run = Play(sim, 57, i => Input(i < 20, aimX: 30, walk: true));
        Assert.That(run.EndReason, Is.EqualTo(WreckEnd.WalkedOut));
        int seam = run.Contact[2] + PelagWreckClipRules.LungeSeam - PelagWreckClipRules.LungeContact;
        Assert.That(run.EndTick, Is.LessThan(seam), "ходьба срывает выход раньше стыка 16");
        PelagWreckPose stow = run.Line.Sample(run.EndTick, Scale);
        Assert.That(stow.Clip, Is.EqualTo(PelagWreckClip.Stow));
        Assert.That(stow.EntryBlendTicks, Is.EqualTo(PelagWreckClipRules.SeamBlendTicks));
        Assert.That(run.Line.Sample(run.EndTick - .01f, Scale).Clip, Is.EqualTo(PelagWreckClip.Lunge));
    }

    /// <summary>Девятый вал с 06.10 вечером не держат (заряды — задевшие махи): удержание кнопки ничего не меняет, выпад базовый.</summary>
    [Test]
    public void NinthWave_HoldIsIgnored_LungeIsTheBaseOne()
    {
        var sim = Arena(PelagForm.WreckNinthWave);
        Run run = Play(sim, 80, i => Input(i < 13, hold: i < 13 + Simulation.WreckSlamUpTicks + 20));
        Assert.That(run.ChargeStart, Is.EqualTo(-1), "заряда удержанием нет");
        Assert.That(run.Release, Is.EqualTo(-1));
        int strike = run.Contact[2];
        Assert.That(strike - run.StageStart[2], Is.EqualTo(8), "замах выпада 8 тиков, как у базы");
        PelagWreckPose hit = run.Line.Sample(strike, Scale);
        Assert.That(hit.Clip, Is.EqualTo(PelagWreckClip.Lunge));
        Assert.That(hit.Frame, Is.EqualTo(PelagWreckClipRules.LungeContact).Within(1e-3f));
        Assert.That(run.EndReason, Is.EqualTo(WreckEnd.Done));
    }

    [TestCase(5, 0, 0f)]
    [TestCase(0, 5, 0f)]
    [TestCase(-5, .3, 0f)]      // курсор за спиной: замах тот же, разворот за первые 3 тика
    public void Swing1Turn_FacesStageDirectionWithinThreeTicks_NoBodyShift(double aimX, double aimY, float startYaw)
    {
        var sim = Arena();
        Run run = Play(sim, 45, i => Input(i == 0, aimX: aimX, aimY: aimY), startYaw);
        Assert.That(run.Contact[0] - run.StageStart[0], Is.EqualTo(5));
        float dir = PelagSquallClipRules.YawOf((float)aimX, (float)aimY);
        float turned = run.StageStart[0] + PelagWreckClipRules.TurnTicks;
        for (float t = turned; t <= run.Contact[0] + 3; t += Step)
        {
            PelagWreckPose pose = run.Line.Sample(t, Scale);
            Assert.That(PelagSquallClipRules.WrapDeg(pose.Yaw - dir), Is.EqualTo(0f).Within(.5f), "за 3 тика корень по направлению этапа, τ=" + t);
            Assert.That(pose.ShiftX, Is.EqualTo(0f));
            Assert.That(pose.ShiftY, Is.EqualTo(0f));
        }
        float prev = run.Line.Sample(run.StageStart[0], Scale).Yaw;
        for (float t = run.StageStart[0] + Step; t <= turned; t += Step)
        {
            float yaw = run.Line.Sample(t, Scale).Yaw;
            Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(yaw - prev)), Is.LessThanOrEqualTo(60f), "поворот без прыжка, τ=" + t);
            prev = yaw;
        }
    }
}
