using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение v4 на рисованных текстурах (06.10, PelagWreckPaintedLook): цвет формы трогает только синие места рисунка
// (база — свои цвета текстуры), полоса выпада держит рисованную вспышку в точке удара и соотношение рисунка, голова
// полосы — фронт Sim, звезда раскрывается за ~3 кадра. NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckPaintedLookTests
{
    [Test]
    public void BaseKeepsTextureColours()
    {
        PelagWreckPaintedLook.TintMul(PelagForm.None, out float r, out float g, out float b);
        Assert.AreEqual(1f, r, 1e-5f);
        Assert.AreEqual(1f, g, 1e-5f);
        Assert.AreEqual(1f, b, 1e-5f);
        Assert.AreEqual(0f, PelagWreckPaintedLook.TintWhite(PelagForm.None));
    }

    [Test]
    public void FormTintTurnsPaintedBlueIntoTheFormColour()
    {
        // Синий рисунка = база #4FA8FF; умноженный на множитель формы он даёт ровно цвет формы.
        PelagWreckSwingLook.Rgb(PelagWreckPaintedLook.BaseHex, out float br, out float bg, out float bb);
        foreach (PelagForm form in new[] { PelagForm.WreckBreakwater, PelagForm.WreckNinthWave, PelagForm.WreckGhostAnchor })
        {
            PelagWreckPaintedLook.TintMul(form, out float r, out float g, out float b);
            PelagWreckSwingLook.Rgb(PelagWreckPaintedLook.TintHex(form), out float tr, out float tg, out float tb);
            Assert.AreEqual(tr, br * r, 1e-4f, form.ToString());
            Assert.AreEqual(tg, bg * g, 1e-4f, form.ToString());
            Assert.AreEqual(tb, bb * b, 1e-4f, form.ToString());
            // Без красного, оранжевого и золота: тон формы — зелень, индиго или бледный фиолетовый.
            PelagWreckSwingLook.HueSat(PelagWreckPaintedLook.TintHex(form), out float hue, out float sat);
            Assert.IsTrue(hue > 130f && hue < 290f, form + " hue " + hue);
        }
        Assert.Greater(PelagWreckPaintedLook.TintWhite(PelagForm.WreckGhostAnchor), 0f);
    }

    [Test]
    public void LungeQuadPutsThePaintedBurstOnTheImpactAndReachesTheWaveEnd()
    {
        float start = Simulation.WreckSlamReach.ToFloat(), end = 6f;
        PelagWreckPaintedLook.LungeQuad(start, end, .75f, out float from, out float length, out float half);
        Assert.AreEqual(start, from + PelagWreckPaintedLook.LungeBurstU * length, 1e-4f, "вспышка рисунка — в точке удара");
        Assert.AreEqual(end, from + length, 1e-4f, "конец рисунка — конец вала");
        Assert.Less(from, start);
        Assert.AreEqual(length / PelagWreckPaintedLook.LungeAspect, 2f * half, .25f * length / PelagWreckPaintedLook.LungeAspect,
            "рисунок не растянут больше чем на четверть");
        Assert.GreaterOrEqual(half, .7f * .75f - 1e-4f);
        Assert.LessOrEqual(half, 1.2f * .75f + 1e-4f);
    }

    [Test]
    public void LungeFrontFollowsTheSimFrontAndNeverHidesTheBurst()
    {
        PelagWreckPaintedLook.LungeQuad(2.2f, 6f, .75f, out float from, out float length, out _);
        float last = 0f;
        for (float front = 2.2f; front <= 6.001f; front += .5f)
        {
            float u = PelagWreckPaintedLook.LungeFrontU(front, from, length);
            Assert.GreaterOrEqual(u, last);
            Assert.GreaterOrEqual(u, PelagWreckPaintedLook.LungeBurstU);
            last = u;
        }
        Assert.AreEqual(1f, PelagWreckPaintedLook.LungeFrontU(6f, from, length), 1e-4f);
        Assert.AreEqual(PelagWreckPaintedLook.LungeBurstU, PelagWreckPaintedLook.LungeFrontU(0f, from, length), 1e-6f);
    }

    [Test]
    public void StarPopsInAboutThreeFramesThenHolds()
    {
        Assert.Less(PelagWreckPaintedLook.StarPop(0f), .6f);
        Assert.Greater(PelagWreckPaintedLook.StarPop(3f / 60f), 1f, "перелёт на ~3-м кадре");
        Assert.AreEqual(1f, PelagWreckPaintedLook.StarPop(.2f), 1e-5f);
        Assert.AreEqual(1f, PelagWreckPaintedLook.StarPop(.5f), 1e-5f);
    }

    [Test]
    public void SwingCrescentIsTheChainReachAndSwingTwoIsBigger()
    {
        for (int k = 0; k < 2; k++)
        {
            Assert.IsTrue(PelagWreckPaintedLook.SwingRadius[k] >= 1.2f && PelagWreckPaintedLook.SwingRadius[k] <= 1.9f);
            Assert.IsTrue(PelagWreckPaintedLook.SwingHeight[k] > .7f && PelagWreckPaintedLook.SwingHeight[k] < 1.1f, "пояс");
        }
        Assert.Greater(PelagWreckPaintedLook.SwingRadius[1], PelagWreckPaintedLook.SwingRadius[0]);
        float span = PelagWreckPaintedLook.SwingTailBack + PelagWreckPaintedLook.SwingHeadPast;
        Assert.IsTrue(span > 150f && span < 260f, "полумесяц, не кольцо");
        float inner = PelagWreckPaintedLook.SwingInner(span);
        Assert.IsTrue(inner > .4f && inner < .8f, "полоса " + inner);
    }
}
