using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение · Волнорез (07.10, PelagWreckBreakwaterLook): плиты стены — внутри полосы Sim (от точки удара до конца стены,
// полуширина Sim), высота главных 0,9–1,6 м, встают ровно когда фронт Sim доходит до их места, выход с перелётом и
// покоем, хвост крошится раньше, остальное — в тик обрушения Sim (ближние к концу раньше). NUnit без Is.AnyOf.
public sealed class WreckBreakwaterLookTests
{
    private static float Start => Simulation.WreckSlamReach.ToFloat();
    private static float End => Simulation.WreckBreakwaterLength.ToFloat();
    private static float Half => Simulation.WreckBreakwaterHalfWidth.ToFloat();
    private static float Step => Simulation.WreckBreakwaterStep.ToFloat();

    [Test]
    public void SlabsStayInsideTheSimWall()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            PelagWreckBreakwaterLook.Slab[] slabs = PelagWreckBreakwaterLook.Layout(Start, End, Half, seed);
            Assert.Greater(slabs.Length, 8, "стена 5,8 м — не меньше девяти плит");
            Assert.LessOrEqual(slabs.Length, PelagWreckBreakwaterLook.PoolSize);
            foreach (var s in slabs)
            {
                Assert.GreaterOrEqual(s.Along, Start, "не раньше точки удара");
                Assert.LessOrEqual(s.Along, End, "не дальше конца стены Sim");
                Assert.LessOrEqual(System.Math.Abs(s.Across) + s.Width * .5f, Half + .121f, "край плиты — в полосе Sim");
                if (s.Main)
                {
                    Assert.GreaterOrEqual(s.Height, PelagWreckBreakwaterLook.MinHeight - 1e-4f);
                    Assert.LessOrEqual(s.Height, PelagWreckBreakwaterLook.MaxHeight + 1e-4f);
                }
                Assert.Greater(s.Lean, 10f, "наклон вперёд");
            }
        }
    }

    [Test]
    public void ChildrenAreUniqueAndLayoutIsDeterministic()
    {
        var a = PelagWreckBreakwaterLook.Layout(Start, End, Half, 7);
        var b = PelagWreckBreakwaterLook.Layout(Start, End, Half, 7);
        Assert.AreEqual(a.Length, b.Length);
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < a.Length; i++)
        {
            Assert.AreEqual(a[i].Along, b[i].Along);
            Assert.IsTrue(seen.Add(a[i].Child), "одна плита пула — одна плита стены");
        }
        Assert.AreEqual(0, PelagWreckBreakwaterLook.Layout(5f, 5f, Half, 1).Length, "стены нет — плит нет");
    }

    [Test]
    public void SlabRisesWhenTheSimFrontReachesIt()
    {
        const int tick = 100;
        foreach (float along in new[] { Start + .1f, 4f, 6.3f, End })
        {
            float pass = PelagWreckBreakwaterLook.PassTick(along, tick, Start, Step);
            int travel = Simulation.WreckWaveSteps(Simulation.WreckSlamReach, Simulation.WreckBreakwaterLength, Simulation.WreckBreakwaterStep);
            float front = PelagWreckVfxRules.WaveFront(pass, tick, Start, Step, travel, End);
            Assert.AreEqual(System.Math.Min(along, End), front, 1e-3f, "фронт Sim в тик выхода — на месте плиты " + along);
        }
    }

    [Test]
    public void PopOvershootsThenSettlesFromUnderground()
    {
        Assert.Less(PelagWreckBreakwaterLook.PopLift(0f), -.5f, "выходит из-под земли");
        Assert.Greater(PelagWreckBreakwaterLook.PopLift(PelagWreckBreakwaterLook.PopSeconds), .05f, "перелёт");
        Assert.AreEqual(0f, PelagWreckBreakwaterLook.PopLift(PelagWreckBreakwaterLook.PopSeconds + PelagWreckBreakwaterLook.SettleSeconds), 1e-4f);
        Assert.LessOrEqual(PelagWreckBreakwaterLook.PopSeconds, 3f / 60f + 1e-4f, "2–3 кадра при 60 к/с");
        Assert.Greater(PelagWreckBreakwaterLook.LeanAt(PelagWreckBreakwaterLook.PopSeconds, 20f), 20f, "наклон с перелётом");
        Assert.Greater(PelagWreckBreakwaterLook.GlowAt(.01f), PelagWreckBreakwaterLook.GlowAt(1f), "вспышка выхода ярче покоя");
    }

    [Test]
    public void TailCrumblesEarlyRestCollapsesOnTheSimCrashEndFirst()
    {
        const int tick = 0;
        int travel = Simulation.WreckWaveSteps(Simulation.WreckSlamReach, Simulation.WreckBreakwaterLength, Simulation.WreckBreakwaterStep);
        float crash = tick + travel;
        float tail = PelagWreckBreakwaterLook.CrumbleTick(PelagWreckBreakwaterLook.PassTick(Start + .2f, tick, Start, Step), crash, Start + .2f, Start, End);
        Assert.Less(tail, crash, "хвост у якоря крошится до обрушения");
        float nearEnd = PelagWreckBreakwaterLook.CrumbleTick(PelagWreckBreakwaterLook.PassTick(7.8f, tick, Start, Step), crash, 7.8f, Start, End);
        float middle = PelagWreckBreakwaterLook.CrumbleTick(PelagWreckBreakwaterLook.PassTick(6f, tick, Start, Step), crash, 6f, Start, End);
        Assert.GreaterOrEqual(nearEnd, crash, "у конца — не раньше обрушения Sim");
        Assert.Less(nearEnd, middle, "у конца раньше середины");
        Assert.LessOrEqual(middle, crash + PelagWreckBreakwaterLook.CollapseRippleTicks + 1e-3f);
        Assert.Greater(PelagWreckBreakwaterLook.CrumbleCut(1f), .5f, "к концу распада плита уходит кусками");
        Assert.Less(PelagWreckBreakwaterLook.CrumbleScale(1f), .5f, "тело плиты ушло в обломки");
        Assert.AreEqual(1f, PelagWreckBreakwaterLook.CrumbleScale(PelagWreckBreakwaterLook.Break), 1e-5f);
        Assert.AreEqual(0f, PelagWreckBreakwaterLook.CrumbleCut(0f), 1e-5f);
    }

    [Test]
    public void CrashSizesFollowTheSimCircle()
    {
        float r = Simulation.WreckBreakwaterCrashRadius.ToFloat();
        // Лёжащий билборд виден примерно на 0,72 своего размера: видимый край — у круга Sim.
        Assert.AreEqual(2f * r, PelagWreckBreakwaterLook.CrashGroundDiameter(r) * .72f, .1f);
        Assert.Greater(PelagWreckBreakwaterLook.CrashHalfWidth(r, true), PelagWreckBreakwaterLook.CrashHalfWidth(r, false), "о преграду крупнее");
        Assert.Greater(PelagWreckBreakwaterLook.CrashRocks(true), PelagWreckBreakwaterLook.CrashRocks(false));
    }
}
