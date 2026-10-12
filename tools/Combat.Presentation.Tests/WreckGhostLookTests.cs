using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение · Призрачный якорь (07.10, PelagWreckGhostLook): якорь падает ровно за тики Sim до события 77, с разгоном;
// входит в землю, сплющивается на 2–3 кадра и стоит; кольцо доходит ровно до круга Sim без перелёта; палитра без
// тёплых тонов. NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckGhostLookTests
{
    [Test]
    public void FallTakesExactlyTheSimDelayAndAccelerates()
    {
        Assert.AreEqual(Simulation.WreckGhostDelayTicks, PelagWreckGhostLook.FallTicks);
        Assert.AreEqual(Simulation.WreckGhostDelayTicks / (float)Simulation.TicksPerSecond, PelagWreckGhostLook.FallSeconds, 1e-6f);
        Assert.AreEqual(PelagWreckGhostLook.StartHeight, PelagWreckGhostLook.FallHeight(0f), 1e-5f);
        Assert.AreEqual(0f, PelagWreckGhostLook.FallHeight(1f), 1e-5f, "низ якоря у земли ровно в тик падения");
        // Разгон: каждый следующий отрезок падения длиннее прошлого.
        float last = 0f;
        for (int i = 1; i <= 8; i++)
        {
            float drop = PelagWreckGhostLook.FallHeight((i - 1) / 8f) - PelagWreckGhostLook.FallHeight(i / 8f);
            Assert.Greater(drop, last, "отрезок " + i);
            last = drop;
        }
        Assert.Less(PelagWreckGhostLook.FallScale(0f), 1f, "вверху мельче");
        Assert.AreEqual(1f, PelagWreckGhostLook.FallScale(1f), 1e-5f);
    }

    [Test]
    public void LandingSquashesForFewFramesThenStandsThenDissolves()
    {
        Assert.Less(PelagWreckGhostLook.SquashY(0f), .9f, "кадр удара сплющен");
        Assert.Greater(PelagWreckGhostLook.SquashY(.035f), 1f, "перелёт через ~2 кадра");
        Assert.AreEqual(1f, PelagWreckGhostLook.SquashY(.08f), 1e-5f, "к ~5-му кадру — покой");
        Assert.Greater(PelagWreckGhostLook.SquashXZ(0f), 1f, "сплющенный — шире");
        Assert.AreEqual(1f, PelagWreckGhostLook.FlashAt(0f), 1e-5f);
        Assert.AreEqual(0f, PelagWreckGhostLook.FlashAt(PelagWreckGhostLook.FlashSeconds), 1e-5f);
        Assert.AreEqual(0f, PelagWreckGhostLook.DissolveAt(.4f), 1e-5f, "стоит в земле ~0,45 с");
        Assert.Greater(PelagWreckGhostLook.DissolveAt(.6f), 0f);
        Assert.AreEqual(1f, PelagWreckGhostLook.DissolveAt(PelagWreckGhostLook.AfterLandSeconds), 1e-5f);
        Assert.Greater(PelagWreckGhostLook.SinkAt(0f), PelagWreckGhostLook.SinkAt(.2f), "в миг удара вбит глубже");
        Assert.Greater(PelagWreckGhostLook.AnchorHeight, 2.9f);
        Assert.Less(PelagWreckGhostLook.AnchorHeight, 3.6f);
    }

    [Test]
    public void RingReachesTheSimCircleExactlyWithoutOvershoot()
    {
        Assert.AreEqual(Simulation.WreckGhostRadius.ToFloat(), PelagWreckGhostLook.RingRadius(300), 1e-5f);
        Assert.AreEqual(PelagWreckGhostLook.WreckGhostRadiusMeters, PelagWreckGhostLook.RingRadius(0), 1e-5f);
        // Середина полосы кольца в меше — на радиусе 1 (масштаб частицы = радиус Sim).
        Assert.AreEqual(1f, PelagWreckGhostLook.RingRadiusAtV(PelagWreckGhostLook.RingBandV), .01f);
        float last = 0f;
        for (float t = 0f; t <= .5f; t += .01f)
        {
            float f = PelagWreckGhostLook.RingFraction(t);
            Assert.GreaterOrEqual(f, last - 1e-6f, "растёт на " + t);
            Assert.LessOrEqual(f, 1f + 1e-6f, "не выходит за круг Sim на " + t);
            last = f;
        }
        Assert.AreEqual(1f, PelagWreckGhostLook.RingFraction(PelagWreckGhostLook.RingExpandSeconds), 1e-5f);
    }

    [Test]
    public void PaletteIsColdVioletWithoutWarmHues()
    {
        foreach (int hex in new[] { PelagWreckGhostLook.CoreHex, PelagWreckGhostLook.BandHex, PelagWreckGhostLook.ShadowHex })
        {
            PelagWreckGhostLook.Rgb(hex, out float r, out float g, out float b);
            Assert.Greater(b, r, "синий больше красного: " + hex.ToString("X6"));
            Assert.Greater(b, g, "синий больше зелёного: " + hex.ToString("X6"));
            Assert.Greater(r, g - .02f, "фиолетовый, не бирюза: " + hex.ToString("X6"));
        }
    }
}
