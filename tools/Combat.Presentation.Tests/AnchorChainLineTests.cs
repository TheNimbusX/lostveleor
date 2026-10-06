using System;
using System.Numerics;
using Game.View;
using NUnit.Framework;

/// <summary>Цепь рига (anchor-core DESIGN §4.1): прямая в натяг, решатель в провис, стык без скачка.</summary>
public sealed class AnchorChainLineTests
{
    [Test]
    public void TautChainIsPracticallyStraight()
    {
        Assert.That(AnchorChainLine.Sag(1.6f, 0), Is.EqualTo(.032f).Within(1e-5f));
        // Мах 24 м/с на радиусе 2,1 м: натяг ≈ 12·24²/2,1 ≈ 3300 Н — стрела около миллиметра.
        Assert.That(AnchorChainLine.Sag(1.6f, 3300), Is.LessThan(.0015f));
        Assert.That(AnchorChainLine.Sag(1.6f, 300), Is.LessThan(.015f));
        Assert.That(AnchorChainLine.Sag(1.6f, 300), Is.GreaterThan(AnchorChainLine.Sag(1.6f, 600)));
    }

    [Test]
    public void LinksAreCountedFromRingAndPartialLinkSitsAtGrip()
    {
        var nodes = new Vector3[64];
        Vector3 ring = new Vector3(0, .5f, 2), grip = new Vector3(0, 1.2f, .3f);
        int count = AnchorChainLine.Layout(grip, ring, 0, nodes, out bool stretched);
        float span = Vector3.Distance(grip, ring);
        Assert.That(stretched, Is.False);
        Assert.That(count, Is.EqualTo((int)Math.Ceiling(span / AnchorChainLine.Pitch) + 1));
        Assert.That(nodes[0], Is.EqualTo(ring));
        Assert.That(nodes[count - 1], Is.EqualTo(grip));
        for (int i = 0; i < count - 2; i++)
            Assert.That(Vector3.Distance(nodes[i], nodes[i + 1]), Is.EqualTo(AnchorChainLine.Pitch).Within(1e-4f));
        Assert.That(Vector3.Distance(nodes[count - 2], grip), Is.LessThanOrEqualTo(AnchorChainLine.Pitch + 1e-4f));
    }

    [Test]
    public void PayingOutAddsLinksAtGripAndHeadLinksStayPut()
    {
        // Бросок: голова улетает, хват на месте — в осях кольца звенья у головы не «едут», новое растёт у рукояти.
        var a = new Vector3[64]; var b = new Vector3[64];
        Vector3 grip = Vector3.Zero, dir = Vector3.Normalize(new Vector3(0, .1f, 1));
        int n1 = AnchorChainLine.Layout(grip, grip + dir * 3.00f, 0, a, out _);
        int n2 = AnchorChainLine.Layout(grip, grip + dir * 3.20f, 0, b, out _);
        Assert.That(n2, Is.GreaterThan(n1));
        for (int k = 0; k < n1 - 1; k++)
            Assert.That(Vector3.Distance(a[k] - a[0], b[k] - b[0]), Is.LessThan(1e-4f), "звено " + k);
    }

    [Test]
    public void TooLongChainStretchesAndSaysSo()
    {
        var nodes = new Vector3[8];
        int count = AnchorChainLine.Layout(Vector3.Zero, new Vector3(0, 0, 3), 0, nodes, out bool stretched);
        Assert.That(stretched, Is.True);
        Assert.That(count, Is.EqualTo(8));
        Assert.That(nodes[7], Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void DrawStateSwitchesWithoutFlicker()
    {
        var state = new AnchorChainDrawState();
        state.Update(true, 1.6f, 1.6f, 0);
        Assert.That(state.UseLine, Is.True);
        state.Update(false, 1.3f, 1.6f, 0);
        Assert.That(state.UseLine, Is.False);
        Assert.That(state.SeedSolver, Is.True);
        state.Update(false, 1.58f, 1.6f, .002f);
        Assert.That(state.SeedSolver, Is.False);
        Assert.That(state.UseLine, Is.False, "прямой, но без натяга — не раньше выдержки");
        for (int i = 0; i < AnchorChainDrawState.MinSolverFrames; i++) state.Update(false, 1.58f, 1.6f, .002f);
        Assert.That(state.UseLine, Is.True);
        state.Update(false, 1.2f, 1.6f, 0);
        state.Update(true, 1.6f, 1.6f, .3f);
        Assert.That(state.UseLine, Is.True, "натяг — сразу прямая");
    }

    [Test]
    public void SlackChainSeedsExactlyFromTheDrawnLine()
    {
        var nodes = new Vector3[64];
        Vector3 ring = new Vector3(.2f, .6f, 1.4f), grip = new Vector3(0, 1.15f, .25f);
        float span = Vector3.Distance(ring, grip), sag = AnchorChainLine.Sag(span, 40);
        int count = AnchorChainLine.Layout(grip, ring, sag, nodes, out _);
        var slack = new AnchorSlackChain();
        slack.Configure(1.6f);
        slack.Seed(ring, grip, Vector3.Zero, Vector3.Zero, sag);
        Assert.That(slack.Count, Is.EqualTo(13));
        Assert.That(slack.SegmentLength(11), Is.EqualTo(1.6f - 11 * AnchorChainLine.Pitch).Within(1e-5f));
        for (int k = 0; k < Math.Min(count, slack.Count) - 1; k++)
            Assert.That(Vector3.Distance(slack[k], nodes[k]), Is.LessThan(1e-4f), "узел " + k);
    }

    [Test]
    public void SlackChainKeepsLengthAndStaysOutOfBodyAndGround()
    {
        var slack = new AnchorSlackChain();
        slack.Configure(1.6f);
        Vector3 ring = new Vector3(.3f, .25f, .6f), grip = new Vector3(.15f, 1.05f, .2f);
        slack.Seed(ring, grip, Vector3.Zero, Vector3.Zero, .03f);
        var capsules = new[] { new AnchorCapsule(new Vector3(0, .9f, 0), new Vector3(0, 1.3f, 0), .2f),
            new AnchorCapsule(new Vector3(.1f, .9f, 0), new Vector3(.1f, .05f, 0), .1f) };
        for (int f = 0; f < 90; f++)
            slack.Advance(1f / 60, ring, ring, grip, grip, p => 0f, capsules, capsules.Length);
        Assert.That(slack.MaxStrain, Is.LessThanOrEqualTo(.008f));
        for (int i = 1; i < slack.Count - 1; i++)
        {
            Assert.That(slack[i].Y, Is.GreaterThanOrEqualTo(.017f));
            foreach (var c in capsules)
            {
                Vector3 ab = c.B - c.A;
                float t = Math.Clamp(Vector3.Dot(slack[i] - c.A, ab) / ab.LengthSquared(), 0, 1);
                Assert.That(Vector3.Distance(slack[i], c.A + ab * t), Is.GreaterThanOrEqualTo(c.Radius + AnchorSlackChain.LinkRadius - .012f));
            }
        }
    }
}
