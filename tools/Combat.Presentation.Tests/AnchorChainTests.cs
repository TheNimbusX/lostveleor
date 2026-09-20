using System;
using System.Numerics;
using Game.View;
using NUnit.Framework;

public sealed class AnchorChainTests
{
    [TestCase(30)] [TestCase(60)] [TestCase(120)]
    public void RecordedSlamPayoutAndRetractionKeepTheTwoPercentBudget(int fps)
    {
        var lines = System.IO.File.ReadAllLines(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures/AnchorSlamPath.csv"));
        var rows = new float[lines.Length][];
        for (int i = 0; i < rows.Length; i++) rows[i] = Array.ConvertAll(lines[i].Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
        var chain = new AnchorChainSolver();
        float physicsTime = 0;
        int peakIterations = 0;
        for (int frame = 0; frame <= (int)(rows[rows.Length - 1][0] * fps); frame++)
        {
            float t = frame / (float)fps;
            while (physicsTime < t)
            {
                physicsTime = Math.Min(t, physicsTime + AnchorChainSolver.Step);
                SampleRecorded(chain, rows, physicsTime, true);
            }
            SampleRecorded(chain, rows, t, false);
            peakIterations = Math.Max(peakIterations, chain.Iterations);
            Assert.That(chain.MaxStrain, Is.LessThanOrEqualTo(.02f), $"t={t} fps={fps}");
            Assert.That(chain.AttachmentError, Is.LessThan(.0001f));
        }
        TestContext.WriteLine($"fps={fps}, peak iterations={peakIterations}");
    }

    private static void SampleRecorded(AnchorChainSolver chain, float[][] rows, float time, bool integrate)
    {
        int k = 0;
        while (k < rows.Length - 2 && rows[k + 1][0] < time) k++;
        var a = rows[k]; var b = rows[k + 1];
        float u = Math.Clamp((time - a[0]) / (b[0] - a[0]), 0, 1);
        var ring = Vector3.Lerp(new Vector3(a[2], a[3], a[4]), new Vector3(b[2], b[3], b[4]), u);
        var grip = Vector3.Lerp(new Vector3(a[5], a[6], a[7]), new Vector3(b[5], b[6], b[7]), u);
        chain.Advance(grip, ring, a[1] + (b[1] - a[1]) * u, 0, new Vector3(-13,.91f,8), new Vector3(-13,1.28f,8), .2f, integrate);
    }

    [TestCase(30)] [TestCase(60)] [TestCase(120)]
    public void PaidOutChainKeepsAttachmentsAndLength(int fps)
    {
        var chain = new AnchorChainSolver();
        Vector3 grip = new Vector3(-.4f, 1.1f, .25f);
        float accumulator = 0;
        for (int frame = 0; frame < fps * 2; frame++)
        {
            float t = frame / (float)fps;
            Vector3 ring = new Vector3(-.4f, .32f, 2.7f + .5f * (float)Math.Sin(t * 3));
            accumulator += 1f / fps;
            while (accumulator >= AnchorChainSolver.Step)
            {
                chain.Advance(grip, ring, 3.5f, 0, new Vector3(0,.5f,0), new Vector3(0,1.3f,0), .24f, true);
                accumulator -= AnchorChainSolver.Step;
            }
            Assert.That(chain.AttachmentError, Is.LessThan(.0001f));
            Assert.That(chain.MaxStrain, Is.LessThanOrEqualTo(.02f), $"frame {frame}");
            for (int i = 1; i < chain.Count - 1; i++) Assert.That(chain[i].Y, Is.GreaterThanOrEqualTo(.017f));
        }
    }

    [Test]
    public void ReleasingAndRecastingDoesNotRetainOldPoints()
    {
        var chain = new AnchorChainSolver();
        chain.Advance(Vector3.UnitY, new Vector3(4,0,0), 4.6f, -.1f, Vector3.Zero, Vector3.Zero, 0, true);
        chain.Clear();
        Assert.That(chain.Count, Is.Zero);
        var grip = new Vector3(100,1,0); var ring = new Vector3(100,1,1);
        chain.Advance(grip, ring, 1.2f, 0, Vector3.Zero, Vector3.Zero, 0, false);
        Assert.That(chain[0], Is.EqualTo(grip));
        Assert.That(chain[chain.Count-1], Is.EqualTo(ring));
        Assert.That(chain.MaxStrain, Is.LessThan(.02f));
    }
}
