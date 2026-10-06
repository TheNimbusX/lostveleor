using System.Collections.Generic;
using Game.View;
using NUnit.Framework;

// Крушение v4 (06.10): правила клипов по ART/characters/pelag/wreck-2026-10-03/animation-v4/timing.json.
public sealed class WreckClipRulesTests
{
    [Test]
    public void StateNamesAreTheRigs_FiveClipsOnTheBaseLayer()
    {
        var names = new HashSet<string>();
        var parameters = new HashSet<string>();
        foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
        {
            string name = PelagWreckClipRules.ClipName(clip);
            Assert.That(name, Does.StartWith("Pelag_AN_Wreck4_"));
            Assert.That(PelagWreckClipRules.StatePath(clip), Is.EqualTo(AnchorRigWreckPlan.StateName(name)), "риг сверяет слой 0 по этому имени");
            Assert.That(PelagWreckClipRules.LayerOf(clip), Is.EqualTo(PelagWreckLayer.Base));
            Assert.That(names.Add(PelagWreckClipRules.StateName(clip)), Is.True);
            Assert.That(parameters.Add(PelagWreckClipRules.PhaseParameter(clip)), Is.True, "своё время у каждого клипа");
            Assert.That(PelagWreckClipRules.Loops(clip), Is.False);
        }
        Assert.That(names.Count, Is.EqualTo(5));
        CollectionAssert.AreEquivalent(AnchorRigWreckPlan.RequiredClips,
            new List<PelagWreckClip>(PelagWreckClipRules.Required).ConvertAll(PelagWreckClipRules.ClipName), "запечка на каждый клип");
        Assert.That(PelagWreckClipRules.BindReference, Is.EqualTo("Pelag_AN_Wreck4Bind"));
        Assert.That(PelagWreckClipRules.HipLimit, Is.EqualTo(.5f));
    }

    [Test]
    public void FramesFollowTiming_Contacts5_5_8_Seams9_9_16()
    {
        Assert.That(PelagWreckClipRules.ContactFrame(PelagWreckClip.Swing1), Is.EqualTo(5));
        Assert.That(PelagWreckClipRules.ContactFrame(PelagWreckClip.Swing2), Is.EqualTo(5));
        Assert.That(PelagWreckClipRules.ContactFrame(PelagWreckClip.Lunge), Is.EqualTo(8));
        Assert.That(PelagWreckClipRules.SeamFrame(PelagWreckClip.Draw), Is.EqualTo(8));
        Assert.That(PelagWreckClipRules.SeamFrame(PelagWreckClip.Swing1), Is.EqualTo(9));
        Assert.That(PelagWreckClipRules.SeamFrame(PelagWreckClip.Swing2), Is.EqualTo(9));
        Assert.That(PelagWreckClipRules.SeamFrame(PelagWreckClip.Lunge), Is.EqualTo(16));
        Assert.That(PelagWreckClipRules.LastFrame(PelagWreckClip.Swing1), Is.EqualTo(15));
        Assert.That(PelagWreckClipRules.LastFrame(PelagWreckClip.Lunge), Is.EqualTo(24));
        Assert.That(PelagWreckClipRules.LastFrame(PelagWreckClip.Stow), Is.EqualTo(8));
        Assert.That(PelagWreckClipRules.LastFrame(PelagWreckClip.Draw), Is.EqualTo(8));
        Assert.That(PelagWreckClipRules.StageClip(0), Is.EqualTo(PelagWreckClip.Swing1));
        Assert.That(PelagWreckClipRules.StageClip(1), Is.EqualTo(PelagWreckClip.Swing2));
        Assert.That(PelagWreckClipRules.StageClip(2), Is.EqualTo(PelagWreckClip.Lunge));
        Assert.That(PelagWreckClipRules.StageClip(3), Is.EqualTo(PelagWreckClip.Lunge), "«Четвёртый удар» — своего клипа нет");
    }

    [TestCase(5, 5f, 5f)]
    [TestCase(5, 2.5f, 2.5f)]
    [TestCase(4, 4f, 5f)]          // быстрый темп: тот же клип, сжатый по времени
    [TestCase(6, 3f, 2.5f)]
    public void SwingWindupHitsContactFrameOnContactTick(int windup, float k, float frame)
        => Assert.That(PelagWreckClipRules.WindupFrame(PelagWreckClip.Swing1, windup, k), Is.EqualTo(frame).Within(1e-4f));

    [Test]
    public void DrawShare_DrawAndSwingWindupInOneStroke()
    {
        float share = PelagWreckClipRules.DrawShare;
        Assert.That(share, Is.EqualTo(7f / 12f).Within(1e-5f), "снятие с кадра 1 (7 кадров) и замах маха 1 (5) — один ход");
        Assert.That(PelagWreckClipRules.AfterContactFrame(PelagWreckClip.Swing1, 4f), Is.EqualTo(9f), "после удара — 1:1 до стыка");
        Assert.That(PelagWreckClipRules.AfterContactFrame(PelagWreckClip.Lunge, 30f), Is.EqualTo(24f), "не дальше последнего кадра");
        Assert.That(PelagWreckClipRules.Phase(PelagWreckClip.Lunge, 12f), Is.EqualTo(.5f));
    }

    [Test]
    public void PlantedFramesFollowRows()
    {
        foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
            for (int side = 0; side < 2; side++)
            {
                int[] frames = PelagWreckClipRules.PlantedFrames(clip, side);
                Assert.That(frames, Is.Not.Empty, clip + " side " + side);
                foreach (int f in frames) Assert.That(f, Is.InRange(0, PelagWreckClipRules.LastFrame(clip)));
            }
        Assert.That(PelagWreckClipRules.PlantedFrames(PelagWreckClip.Swing1, 0), Does.Contain(5), "левая стоит в контакте маха 1");
        Assert.That(PelagWreckClipRules.PlantedFrames(PelagWreckClip.Lunge, 1), Does.Contain(8), "правая стоит в ударе выпада");
    }
}
