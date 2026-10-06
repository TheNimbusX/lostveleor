using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Индикатор серии Крушения (06.10, целевой кадр series-ui.png): звенья под плиткой и кольцо окна — на живой Sim
// (поле как HudWreckReachRulesTests: герой в нуле, взгляд +X); сроки серии — только из снимка WreckState, тиков в
// тестах нет (серию переписывают по новым клипам). Звенья над героем — правила и события WreckStage.
public sealed class HudWreckSeriesRulesTests
{
    const int P = Simulation.PlayerId;

    static Simulation Arena(PelagForm form = PelagForm.None, bool fourth = false)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[P] = FixVec2.Zero;
        sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
        var nodes = new AbilityNode[8];
        int count = 0;
        if (form != PelagForm.None) count = PelagForms.AppendFormNodes(form, nodes, count);
        if (fourth) count = SabreTalents.AppendNode(SabreTalentLine.Wreck, 4, nodes, count);
        sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, count);
        sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
        return sim;
    }

    static InputFrame Input(bool press)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press ? 1 : 0);
        input.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    static int Links(Simulation sim) => HudWreckSeriesRules.LinkCount(sim.GetAbility(0).Has(AbilityFlag.WreckFourthStrike));

    /// <summary>
    /// Серия целиком: первое нажатие, следующие — когда окна осталось меньше половины (по кольцу, не по тикам).
    /// Каждый тик — снимок в трекер; <paramref name="check"/> видит Sim после шага и кадр индикатора.
    /// </summary>
    static void RunSeries(Simulation sim, HudWreckSeriesTracker tracker, int presses, System.Action<Simulation, HudWreckSeriesShow> check, int ticks = 240)
    {
        int links = Links(sim), pressed = 0;
        for (int i = 0; i < ticks; i++)
        {
            WreckState w = sim.Wreck;
            float left = HudWreckSeriesRules.WindowLeft(w, links, sim.Tick - 1);
            bool press = pressed < presses && (i == 0 || (sim.WreckComboOpen && w.Phase == WreckPhase.Window && left < .5f && w.Strikes == pressed));
            if (press) pressed++;
            sim.Step(Input(press));
            check(sim, tracker.Observe(sim.Wreck, 0, links, sim.Tick - 1, 0f));
        }
    }

    [Test]
    public void EachStrikeLightsOneLink_RingDrainsExactlyWhileTheWindowIsOpen()
    {
        var sim = Arena();
        var tracker = new HudWreckSeriesTracker();
        var seen = new HashSet<string>();
        float lastLeft = 2f;
        int lastStrikes = 0;
        RunSeries(sim, tracker, 3, (s, show) =>
        {
            WreckState w = s.Wreck;
            string at = " (тик " + (s.Tick - 1) + ", фаза " + w.Phase + ")";
            Assert.That(show.Links, Is.EqualTo(3), "три звена без таланта" + at);
            if (s.WreckActive) Assert.That(show.Lit, Is.EqualTo(w.Strikes), "горит по звену на удар" + at);
            Assert.That(show.Window > 0f, Is.EqualTo(s.WreckComboOpen), "кольцо горит ровно пока Sim примет нажатие" + at);
            if (w.Strikes != lastStrikes) { lastLeft = 2f; lastStrikes = w.Strikes; }
            if (show.Window > 0f)
            {
                seen.Add("window");
                if (s.Tick - 1 == w.ContactTick) { Assert.That(show.Window, Is.EqualTo(1f).Within(1e-5f), "в тик удара кольцо полное" + at); seen.Add("full"); }
                Assert.That(show.Window, Is.LessThan(lastLeft), "кольцо только убывает" + at);
                lastLeft = show.Window;
            }
            if (s.WreckActive && w.Strikes > 0 && s.Tick - 1 == w.ContactTick && w.Stage == w.Strikes - 1)
            {
                Assert.That(show.Newest, Is.EqualTo(w.Strikes - 1), "зажглось звено этого удара" + at);
                Assert.That(show.NewestAge, Is.EqualTo(0f).Within(1e-5f), "вскакивает с удара" + at);
                Assert.That(HudWreckSeriesRules.LinkHot(show.Newest, show), Is.EqualTo(1f).Within(1e-5f), "в миг удара звено белое" + at);
                seen.Add("pop" + w.Strikes);
            }
            if (show.Final && s.WreckActive)
            {
                Assert.That(show.Lit, Is.EqualTo(3));
                Assert.That(show.Window, Is.LessThan(0f), "после последнего удара окна нет");
                if (HudWreckSeriesRules.BurstAlpha(show) > 0f) seen.Add("burst");
            }
            if (!s.WreckActive && s.Wreck.Serial > 0) seen.Add(show.Glow <= 0f ? "dark" : "fading");
        });
        Assert.That(seen, Is.SupersetOf(new[] { "window", "full", "pop1", "pop2", "pop3", "burst", "fading", "dark" }));
        Assert.That(sim.Wreck.Strikes, Is.EqualTo(3));
    }

    [Test]
    public void WindowExpires_LinkGoesDarkWithoutTheFinalFlash()
    {
        var sim = Arena();
        var tracker = new HudWreckSeriesTracker();
        bool lit = false, faded = false;
        RunSeries(sim, tracker, 1, (s, show) =>
        {
            Assert.That(show.Final, Is.False);
            Assert.That(HudWreckSeriesRules.BurstAlpha(show), Is.EqualTo(0f), "без последнего удара лучей нет");
            lit |= HudWreckSeriesRules.LinkGlow(0, show) >= 1f;
            if (!s.WreckActive && s.Wreck.Serial > 0 && show.Glow <= 0f) faded = true;
            if (!s.WreckActive && s.Wreck.Serial > 0) Assert.That(show.Window, Is.LessThan(0f));
        }, 120);
        Assert.That(lit, Is.True, "первое звено горело");
        Assert.That(faded, Is.True, "окно истекло — звено погасло");
    }

    [Test]
    public void FourthStrike_FourLinks_RingStaysForTheFourthAfterTheSlam()
    {
        var sim = Arena(fourth: true);
        var tracker = new HudWreckSeriesTracker();
        bool ringAfterSlam = false, final = false;
        RunSeries(sim, tracker, 4, (s, show) =>
        {
            Assert.That(show.Links, Is.EqualTo(4));
            Assert.That(show.Window > 0f, Is.EqualTo(s.WreckComboOpen), "тик " + (s.Tick - 1));
            if (s.Wreck.Strikes == 3 && show.Window > 0f) { ringAfterSlam = true; Assert.That(show.Final, Is.False); }
            final |= show.Final && show.Lit == 4;
        }, 300);
        Assert.That(ringAfterSlam, Is.True, "после удара оземь окно на четвёртый удар");
        Assert.That(final, Is.True, "четвёртый удар — финал серии");
    }

    [Test]
    public void SeriesInAnotherSlot_OrSeenAlreadyOver_LightsNothing()
    {
        var sim = Arena();
        var other = new HudWreckSeriesTracker();
        RunSeries(sim, new HudWreckSeriesTracker(), 3, (s, show) =>
        {
            HudWreckSeriesShow alien = other.Observe(s.Wreck, 1, 3, s.Tick - 1, 0f);
            Assert.That(alien.Lit, Is.EqualTo(0), "серия чужого слота звенья этой плитки не зажигает");
            Assert.That(alien.Window, Is.LessThan(0f));
        });
        // HUD открыли после серии: звенья тёмные, вспышки нет.
        HudWreckSeriesShow late = new HudWreckSeriesTracker().Observe(sim.Wreck, 0, 3, sim.Tick - 1, 0f);
        Assert.That(sim.WreckActive, Is.False);
        Assert.That(late.Glow, Is.EqualTo(0f));
        Assert.That(HudWreckSeriesRules.BurstAlpha(late), Is.EqualTo(0f));
        Assert.That(HudWreckSeriesRules.LinkScale(late.Newest, late), Is.EqualTo(1f));
    }

    [Test]
    public void Slot_InSeries_IconWithLinksNotTheCooldownNumber_NumberOnlyAfterTheSeries()
    {
        foreach (int presses in new[] { 3, 1 })
        {
            var sim = Arena();
            var seen = new HashSet<string>();
            RunSeries(sim, new HudWreckSeriesTracker(), presses, (s, show) =>
            {
                string at = " (" + presses + " наж., тик " + (s.Tick - 1) + ", фаза " + s.Wreck.Phase + ")";
                bool series = HudWreckSeriesRules.InSeries(s.Wreck, 0);
                Assert.That(series, Is.EqualTo(s.WreckActive), "серия плитки — ровно пока идёт серия Sim" + at);
                Assert.That(HudWreckSeriesRules.InSeries(s.Wreck, 1), Is.False, "чужая плитка не в серии" + at);
                // Кулдаун от каста тикает и в серии: без правила плитка показала бы цифру (CombatHudView.RefreshSlot).
                bool cooling = HudAbilityAvailability.Of(s, 0, s.GetAbility(0)).Block == HudAbilityBlock.Cooldown;
                if (series && cooling) seen.Add("hidden");
                if (!series && cooling) seen.Add("number");
                if (series) Assert.That(show.Links, Is.EqualTo(3), "в серии — звенья" + at);
            }, 200);
            Assert.That(seen, Is.EquivalentTo(new[] { "hidden", "number" }), presses + " наж.: цифра только после конца серии");
        }
    }

    [Test]
    public void FormColours_TableOfTheBrief_NoRedOrangeOrGold()
    {
        Assert.That(HudWreckSeriesRules.FormHex(PelagForm.None), Is.EqualTo(0x4FA8FF));
        Assert.That(HudWreckSeriesRules.FormHex(PelagForm.WreckBreakwater), Is.EqualTo(0x1FB37E));
        Assert.That(HudWreckSeriesRules.FormHex(PelagForm.WreckNinthWave), Is.EqualTo(0x4B3FD0));
        Assert.That(HudWreckSeriesRules.FormHex(PelagForm.WreckGhostAnchor), Is.EqualTo(0xE4EEF6));
        foreach (PelagForm form in new[] { PelagForm.None, PelagForm.WreckBreakwater, PelagForm.WreckNinthWave, PelagForm.WreckGhostAnchor })
        {
            HudWreckSeriesRules.FormColour(form, 0f, out float r, out float g, out float b);
            Assert.That(r, Is.LessThan(b), form + ": красный не ведёт");
            HudWreckSeriesRules.FormColour(form, 1f, out float hr, out float hg, out float hb);
            Assert.That(hr, Is.GreaterThanOrEqualTo(r)); Assert.That(hg, Is.GreaterThanOrEqualTo(g)); Assert.That(hb, Is.GreaterThanOrEqualTo(b));
            Assert.That(hr, Is.LessThanOrEqualTo(1f)); Assert.That(hb, Is.LessThanOrEqualTo(1f));
        }
        HudWreckSeriesRules.FormColour(PelagForm.None, 0f, out float br, out float bg, out float bb);
        Assert.That(br, Is.EqualTo(0x4F / 255f).Within(1e-5f));
        Assert.That(bg, Is.EqualTo(0xA8 / 255f).Within(1e-5f));
        Assert.That(bb, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Row_CentredUnderTheTile_ThreeOrFourLinks()
    {
        Assert.That(HudWreckSeriesRules.LinkX(1, 3), Is.EqualTo(0f));
        Assert.That(HudWreckSeriesRules.LinkX(0, 3), Is.EqualTo(-HudWreckSeriesRules.LinkPitch));
        Assert.That(HudWreckSeriesRules.LinkX(0, 4) + HudWreckSeriesRules.LinkX(3, 4), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(HudWreckSeriesRules.LinkPitch, Is.GreaterThan(HudWreckSeriesRules.LinkWidth), "звенья под плиткой не слипаются");
        Assert.That(HudWreckSeriesRules.LinkCount(false), Is.EqualTo(3));
        Assert.That(HudWreckSeriesRules.LinkCount(true), Is.EqualTo(4));
        Assert.That(HudWreckSeriesRules.MaxLinks, Is.EqualTo(HudWreckSeriesRules.LinkCount(true)));
    }
}
