using Game.Sim;
using Game.View;
using NUnit.Framework;

// Вид форм Вихря (02.10): фронт кольца Пенных волн идёт по числам Sim, кольцо гаснет после хода,
// оглушение Водоворота отличается от оглушения удара якорем, ритм оборотов Бури — свой.
public sealed class WhirlwindFormViewRulesTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void FoamRingFrontMatchesSimOnWholeSteps(int ring)
    {
        int travel = Simulation.FoamRingTravelTicks(ring);
        const int start = 100;
        for (int step = 0; step <= travel + 2; step++)
        {
            // Шаг s хода кольца — тик start + s − 1; на нём Sim прошла фронтом до FoamRingRadiusAt(ring, s).
            float now = start + step - 1;
            float expected = Simulation.FoamRingRadiusAt(ring, step).ToFloat();
            Assert.That(PelagWhirlwindFormRules.FoamRingFront(ring, start, travel, now), Is.EqualTo(expected).Within(1e-4f), "шаг " + step);
        }
    }

    [Test]
    public void FoamRingFrontIsContinuousBetweenTicks()
    {
        float a = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 13f);
        float mid = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 13.5f);
        float b = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 14f);
        Assert.That(mid, Is.GreaterThan(a).And.LessThan(b));
    }

    [Test]
    public void FoamRingStopsTravellingAtOuterRadius()
    {
        int travel = Simulation.FoamRingTravelTicks(0);
        Assert.That(PelagWhirlwindFormRules.FoamRingTravelling(50, travel, 50 + travel - 2), Is.True);
        Assert.That(PelagWhirlwindFormRules.FoamRingTravelling(50, travel, 50 + travel - 1), Is.False);
        Assert.That(PelagWhirlwindFormRules.FoamRingLifeSeconds(travel),
            Is.EqualTo((travel + PelagWhirlwindFormRules.FoamWaveLingerTicks) / (float)Simulation.TicksPerSecond));
    }

    [Test]
    public void MaelstromStaggerNeedsThePullBeforeIt()
    {
        int contact = PelagWhirlwindFormRules.MaelstromContactTick(200, 9);
        Assert.That(contact, Is.EqualTo(210));
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks, contact), Is.True);
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(240, Simulation.MaelstromStaggerTicks, contact), Is.False, "удар якорем позже");
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks + 1, contact), Is.False);
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks, -1), Is.False, "Водоворота не было");
    }

    [Test]
    public void ChannelPulseFallsBackToTalentRhythm()
    {
        Assert.That(PelagWhirlwindFormRules.ChannelPulseTicks(null), Is.EqualTo(Simulation.WhirlwindPulseTicks));
        Assert.That(PelagWhirlwindFormRules.ChannelPulseTicks(new Simulation(1, 64)), Is.EqualTo(Simulation.WhirlwindPulseTicks),
            "без Бури — прежний период таланта");
        Assert.That(Simulation.StormPulseTicks, Is.LessThan(Simulation.WhirlwindPulseTicks));
    }
}
