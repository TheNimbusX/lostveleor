using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Звенья над героем на ударах Крушения (06.10, нижний ряд series-ui.png): вскакивают и гаснут за 0,5 с, последний
// удар сцепляет их в короткую цепь со вспышкой. Вид рождает их от события Sim WreckStage — здесь проверено, что
// событие несёт ровно то, что вид читает (этап → число звеньев, Flag — последний удар), на живой серии.
public sealed class PelagWreckSeriesMarkRulesTests
{
    const int P = Simulation.PlayerId;

    [Test]
    public void Mark_PopsThenFadesWithinHalfASecond()
    {
        Assert.That(PelagWreckSeriesMarkRules.Alpha(-.01f), Is.EqualTo(0f));
        Assert.That(PelagWreckSeriesMarkRules.Alpha(0f), Is.EqualTo(1f));
        Assert.That(PelagWreckSeriesMarkRules.Alpha(PelagWreckSeriesMarkRules.FadeFrom), Is.EqualTo(1f));
        Assert.That(PelagWreckSeriesMarkRules.Alpha(.35f), Is.InRange(.01f, .99f));
        Assert.That(PelagWreckSeriesMarkRules.Alpha(.5f), Is.EqualTo(0f), "гаснет за 0,5 с");
        Assert.That(PelagWreckSeriesMarkRules.MarkSeconds, Is.EqualTo(.5f));

        Assert.That(PelagWreckSeriesMarkRules.Pop(0f), Is.EqualTo(PelagWreckSeriesMarkRules.PopFrom));
        Assert.That(PelagWreckSeriesMarkRules.Pop(PelagWreckSeriesMarkRules.PopSeconds), Is.EqualTo(PelagWreckSeriesMarkRules.PopPeak).Within(1e-5f));
        Assert.That(PelagWreckSeriesMarkRules.Pop(PelagWreckSeriesMarkRules.PopSeconds * 2f), Is.EqualTo(1f));
        // Вскакивает только новое звено; старые стоят.
        Assert.That(PelagWreckSeriesMarkRules.Scale(0, 2, .05f, false), Is.EqualTo(1f));
        Assert.That(PelagWreckSeriesMarkRules.Scale(1, 2, .05f, false), Is.Not.EqualTo(1f));
        Assert.That(PelagWreckSeriesMarkRules.Hot(1, 2, 0f, false), Is.EqualTo(1f));
        Assert.That(PelagWreckSeriesMarkRules.Hot(0, 2, 0f, false), Is.EqualTo(0f));
        Assert.That(PelagWreckSeriesMarkRules.BurstAlpha(.15f, false), Is.EqualTo(0f), "лучи — только у последнего удара");
    }

    [Test]
    public void FinalStrike_SpreadSnapsIntoAShortChain_ThenBursts()
    {
        float snap = PelagWreckSeriesMarkRules.SnapSeconds;
        float spread = PelagWreckSeriesMarkRules.Offset(2, 3, 0f, true) - PelagWreckSeriesMarkRules.Offset(1, 3, 0f, true);
        float chain = PelagWreckSeriesMarkRules.Offset(2, 3, snap, true) - PelagWreckSeriesMarkRules.Offset(1, 3, snap, true);
        Assert.That(spread, Is.EqualTo(PelagWreckSeriesMarkRules.SpreadPitch).Within(1e-5f), "вылетают разлётом");
        Assert.That(chain, Is.EqualTo(PelagWreckSeriesMarkRules.ChainPitch).Within(1e-5f), "сходятся в цепь");
        Assert.That(chain, Is.LessThan(PelagWreckSeriesMarkRules.LinkWidth), "в цепи звенья заходят друг в друга");
        Assert.That(PelagWreckSeriesMarkRules.Offset(1, 3, snap * .5f, true), Is.EqualTo(0f), "цепь по центру головы");
        Assert.That(PelagWreckSeriesMarkRules.BurstAlpha(snap - .01f, true), Is.EqualTo(0f), "до сцепки лучей нет");
        Assert.That(PelagWreckSeriesMarkRules.BurstAlpha(snap + .06f, true), Is.GreaterThan(.5f), "в сцепку — вспышка");
        Assert.That(PelagWreckSeriesMarkRules.BurstAlpha(snap + PelagWreckSeriesMarkRules.BurstSeconds, true), Is.EqualTo(0f));
        Assert.That(snap + PelagWreckSeriesMarkRules.BurstSeconds, Is.LessThanOrEqualTo(PelagWreckSeriesMarkRules.MarkSeconds), "вспышка внутри полсекунды");
        Assert.That(PelagWreckSeriesMarkRules.Hot(0, 3, snap, true), Is.EqualTo(1f).Within(1e-5f), "сцепка раскаляет все звенья");
        Assert.That(PelagWreckSeriesMarkRules.Links(0), Is.EqualTo(1));
        Assert.That(PelagWreckSeriesMarkRules.Links(9), Is.EqualTo(HudWreckSeriesRules.MaxLinks));
    }

    [Test]
    public void WreckStageEvent_CarriesTheLinkCountAndTheLastStrike_OnALiveSeries()
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[P] = FixVec2.Zero;
        sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
        sim.SetAbility(0, AbilityDefinition.Wreck(), new AbilityNode[0], 0);
        sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
        var marks = new List<(int links, bool last, int tick, int strikes, int contact)>();
        for (int i = 0; i < 240 && marks.Count < 3; i++)
        {
            WreckState w = sim.Wreck;
            // Следующее нажатие — как только окно открыто (тики серии не зашиты: её переписывают под клипы).
            bool press = i == 0 || (sim.WreckComboOpen && w.Phase == WreckPhase.Window);
            var input = InputFrame.Empty;
            input.AbilityMask = input.AbilityHoldMask = (byte)(press ? 1 : 0);
            input.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            input.AttackTarget = input.AbilityTarget = -1;
            int tick = sim.Tick;
            sim.Step(input);
            foreach (SimEvent e in sim.Events)
                if (e.Type == SimEventType.WreckStage && e.Source == P)
                    marks.Add((PelagWreckSeriesMarkRules.Links(e.Amount + 1), e.Flag, tick, sim.Wreck.Strikes, sim.Wreck.ContactTick));
        }
        Assert.That(marks.Count, Is.EqualTo(3), "по звеньям на каждый удар серии");
        for (int k = 0; k < marks.Count; k++)
        {
            Assert.That(marks[k].links, Is.EqualTo(k + 1), "на удар N — N звеньев");
            Assert.That(marks[k].links, Is.EqualTo(marks[k].strikes), "число звеньев = ударов в снимке");
            Assert.That(marks[k].last, Is.EqualTo(k == 2), "сцепка — только на последнем ударе");
            Assert.That(marks[k].tick, Is.EqualTo(marks[k].contact), "событие — в тик удара");
        }
    }
}
