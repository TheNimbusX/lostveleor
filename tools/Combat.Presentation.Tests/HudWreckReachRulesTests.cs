using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Подсказки Крушения на полу, ритм v4 (владелец 06.10): до первого нажатия — один тонкий контур следа серии, в серии —
// ничего, кроме линии полосы выпада до контакта. На живой Sim (поле как WreckTests.Arena: герой в нуле, взгляд +X);
// тиков в тестах нет — нажатия по окну Sim (серию переписывают под клипы v4), геометрия — из WreckLanePreview.
public sealed class HudWreckReachRulesTests
{
    const int P = Simulation.PlayerId;
    const float Near = 2e-3f;

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

    static FixVec2 At(double x, double y) => new FixVec2(Fix64.Ratio((int)Math.Round(x * 1000), 1000), Fix64.Ratio((int)Math.Round(y * 1000), 1000));

    static InputFrame Input(bool press, bool hold, FixVec2 aim)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press || hold ? 1 : 0);
        input.Aim = aim;
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    static HudWreckHint HintOf(Simulation sim) => HudWreckReachRules.Hint(sim.Wreck, sim.WreckActive && sim.Wreck.Slot == 0);

    static List<HudWreckOutlinePiece> OutlineOf(Simulation sim, FixVec2 cursor, out HudWreckFootprint f, out FixVec2 direction)
    {
        Assert.That(HudWreckReachRules.FootprintOf(sim, 0, cursor, out f, out direction), Is.True);
        var pieces = new List<HudWreckOutlinePiece>();
        HudWreckReachRules.Outline(f, pieces);
        return pieces;
    }

    // Точное расстояние от точки до куска контура: до отрезка — проекцией, до дуги — по радиусу внутри её углов.
    static float Distance(in HudWreckOutlinePiece piece, float x, float y)
    {
        piece.At(0f, out float ax, out float ay);
        piece.At(1f, out float bx, out float by);
        double ends = Math.Min(Math.Sqrt((ax - x) * (ax - x) + (ay - y) * (ay - y)), Math.Sqrt((bx - x) * (bx - x) + (by - y) * (by - y)));
        if (piece.Arc)
        {
            double dx = x - piece.Cx, dy = y - piece.Cy, angle = Math.Atan2(dy, dx);
            double lo = Math.Min(piece.From, piece.To), hi = Math.Max(piece.From, piece.To);
            while (angle < lo) angle += Math.PI * 2;
            bool within = angle <= hi;
            return (float)(within ? Math.Min(ends, Math.Abs(Math.Sqrt(dx * dx + dy * dy) - piece.Radius)) : ends);
        }
        double ux = bx - ax, uy = by - ay, length2 = ux * ux + uy * uy;
        double t = length2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * ux + (y - ay) * uy) / length2)) : 0;
        double px = ax + ux * t - x, py = ay + uy * t - y;
        return (float)Math.Sqrt(px * px + py * py);
    }

    static bool OnOutline(List<HudWreckOutlinePiece> pieces, float x, float y, int skip = -1)
    {
        for (int i = 0; i < pieces.Count; i++)
            if (i != skip && Distance(pieces[i], x, y) < Near) return true;
        return false;
    }

    // Один контур: ни один кусок не лежит внутри следа, концы кусков смыкаются, куски не идут друг по другу.
    static void AssertOneContour(in HudWreckFootprint f, List<HudWreckOutlinePiece> pieces, string what)
    {
        Assert.That(pieces.Count, Is.GreaterThan(0), what);
        for (int i = 0; i < pieces.Count; i++)
        {
            HudWreckOutlinePiece piece = pieces[i];
            for (int k = 1; k < 8; k++)
            {
                piece.At(k / 8f, out float x, out float y);
                Assert.That(HudWreckReachRules.Inside(f, x, y), Is.False, what + ": кусок " + i + " внутри следа");
            }
            piece.At(.5f, out float mx, out float my);
            Assert.That(OnOutline(pieces, mx, my, i), Is.False, what + ": кусок " + i + " задвоен");
            bool full = piece.Arc && Math.Abs(piece.To - piece.From) >= Math.PI * 2 - 1e-4;
            if (full) continue;
            piece.At(0f, out float ax, out float ay);
            piece.At(1f, out float bx, out float by);
            Assert.That(OnOutline(pieces, ax, ay, i), Is.True, what + ": начало куска " + i + " висит");
            Assert.That(OnOutline(pieces, bx, by, i), Is.True, what + ": конец куска " + i + " висит");
        }
    }

    [Test]
    public void BeforeTheFirstPress_OneThinContour_OfTheSwingSectorAndTheLungeLane()
    {
        var sim = Arena();
        Assert.That(HintOf(sim), Is.EqualTo(HudWreckHint.Footprint));
        List<HudWreckOutlinePiece> pieces = OutlineOf(sim, At(0, 5), out HudWreckFootprint f, out FixVec2 direction);
        Assert.That(direction.X.ToFloat(), Is.EqualTo(0f).Within(1e-3f), "след по курсору, не по взгляду");
        Assert.That(direction.Y.ToFloat(), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(f.SweepHalf, Is.LessThan(Math.PI * .5), "махи — сектор, не круг");
        Assert.That(f.CrashRadius, Is.EqualTo(0f));
        AssertOneContour(f, pieces, "база");
        // Конец полосы — край следа; тыл круга удара, начало полосы и дуга сектора через круг — внутри: не рисуются.
        Assert.That(OnOutline(pieces, f.LaneTo, 0f), Is.True, "торец полосы");
        Assert.That(OnOutline(pieces, f.LaneTo - .2f, f.LaneHalf), Is.True, "бок полосы");
        Assert.That(OnOutline(pieces, f.Impact - f.ImpactRadius, 0f), Is.False, "тыл круга удара внутри сектора");
        Assert.That(OnOutline(pieces, f.LaneFrom, 0f), Is.False, "начало полосы внутри круга");
        Assert.That(OnOutline(pieces, f.SweepRadius, 0f), Is.False, "дуга сектора через круг удара не рисуется");
        float c = (float)Math.Cos(f.SweepHalf), s = (float)Math.Sin(f.SweepHalf), r = f.SweepRadius * .5f;
        Assert.That(OnOutline(pieces, c * r, s * r) && OnOutline(pieces, c * r, -s * r), Is.True, "края сектора от героя");
    }

    [Test]
    public void Series_FloorIsSilent_ExceptTheLungeLineBeforeContact_InTheDirectionSimTook()
    {
        var sim = Arena();
        var seen = new HashSet<string>();
        Assert.That(HintOf(sim), Is.EqualTo(HudWreckHint.Footprint), "до первого нажатия — контур");
        seen.Add("before");
        FixVec2 aim = At(5, 0), expected = default;
        int pressed = 0;
        for (int i = 0; i < 240; i++)
        {
            WreckState before = sim.Wreck;
            bool press = pressed < 3 && (i == 0 || (sim.WreckComboOpen && before.Phase == WreckPhase.Window && before.Strikes == pressed));
            if (press && ++pressed == 3)
            {
                aim = At(3, 4);
                expected = (aim - sim.Entities.Position[P]).Normalized();
            }
            else if (pressed == 3) aim = At(-4, -3);   // после третьего нажатия курсор уходит — линия стоит по Sim
            sim.Step(Input(press, false, aim));
            WreckState w = sim.Wreck;
            HudWreckHint hint = HintOf(sim);
            string at = " (тик " + (sim.Tick - 1) + ", фаза " + w.Phase + ", этап " + w.Stage + ")";
            if (!sim.WreckActive)
            {
                Assert.That(hint, Is.EqualTo(HudWreckHint.Footprint), "серии нет — снова контур" + at);
                seen.Add("after");
                continue;
            }
            bool lunge = w.Stage == HudWreckReachRules.LungeStage && (w.Phase == WreckPhase.Windup || w.Phase == WreckPhase.Charge);
            Assert.That(hint, Is.EqualTo(lunge ? HudWreckHint.LungeLine : HudWreckHint.None), "в серии пол молчит" + at);
            if (!lunge) { seen.Add(w.Strikes >= 3 ? "silent-after-lunge" : "silent"); continue; }
            Assert.That(pressed, Is.EqualTo(3), "линия — только после третьего нажатия" + at);
            Assert.That(sim.Tick - 1, Is.LessThan(w.ContactTick), "линия гаснет к контакту" + at);
            Assert.That(HudWreckReachRules.LungeLineOf(sim, 0, aim, out FixVec2 axis, out float from, out float to), Is.True, at);
            Assert.That(axis.X.ToFloat(), Is.EqualTo(expected.X.ToFloat()).Within(2e-3f), "ось — курсор третьего нажатия" + at);
            Assert.That(axis.Y.ToFloat(), Is.EqualTo(expected.Y.ToFloat()).Within(2e-3f), at);
            Assert.That(from, Is.GreaterThan(0f), "от руки, не из центра героя");
            Assert.That(to, Is.GreaterThan(from + 1f));
            seen.Add("line");
        }
        Assert.That(seen, Is.EquivalentTo(new[] { "before", "silent", "line", "silent-after-lunge", "after" }));
    }

    /// <summary>Девятый вал с 06.10 вечером не держат: фазы заряда нет, линия выпада — как у базы, ось ведёт Sim.</summary>
    [Test]
    public void NinthWave_NoChargePhase_LungeLineFollowsSim()
    {
        var sim = Arena(PelagForm.WreckNinthWave);
        int pressed = 0;
        int lineTicks = 0;
        for (int i = 0; i < 260; i++)
        {
            WreckState before = sim.Wreck;
            bool press = pressed < 3 && (i == 0 || (sim.WreckComboOpen && before.Phase == WreckPhase.Window && before.Strikes == pressed));
            if (press) pressed++;
            FixVec2 aim = pressed == 3 ? At(5 * Math.Cos(i * .05), 5 * Math.Sin(i * .05)) : At(5, 0);
            sim.Step(Input(press, pressed == 3, aim));
            Assert.That(sim.Wreck.Phase, Is.Not.EqualTo(WreckPhase.Charge), "удержание не заряжает");
            if (!sim.WreckActive || sim.Wreck.Stage != HudWreckReachRules.LungeStage || sim.Wreck.Phase != WreckPhase.Windup) continue;
            lineTicks++;
            Assert.That(HintOf(sim), Is.EqualTo(HudWreckHint.LungeLine), "выпад до контакта");
            Assert.That(HudWreckReachRules.LungeLineOf(sim, 0, At(0, -5), out FixVec2 axis, out _, out _), Is.True);
            Assert.That(axis.X.Raw, Is.EqualTo(sim.Wreck.Direction.X.Raw), "ось ведёт Sim, не курсор HUD");
            Assert.That(axis.Y.Raw, Is.EqualTo(sim.Wreck.Direction.Y.Raw));
        }
        Assert.That(lineTicks, Is.EqualTo(8), "линия — весь замах выпада, 8 тиков");
    }

    [Test]
    public void Breakwater_ContourGoesRoundTheCrashCircle_LaneEndInsideIt()
    {
        var sim = Arena(PelagForm.WreckBreakwater);
        List<HudWreckOutlinePiece> pieces = OutlineOf(sim, At(5, 0), out HudWreckFootprint f, out _);
        Assert.That(f.CrashRadius, Is.EqualTo(Simulation.WreckBreakwaterCrashRadius.ToFloat()));
        AssertOneContour(f, pieces, "Волнорез");
        Assert.That(OnOutline(pieces, f.LaneTo + f.CrashRadius, 0f), Is.True, "дальний край круга обрушения");
        Assert.That(OnOutline(pieces, f.LaneTo, 0f), Is.False, "торец полосы внутри круга обрушения");
    }

    [Test]
    public void FourthStrike_SweepIsACircleAroundTheHero_NoSectorEdges()
    {
        var sim = Arena(fourth: true);
        List<HudWreckOutlinePiece> pieces = OutlineOf(sim, At(5, 0), out HudWreckFootprint f, out _);
        Assert.That(HudWreckReachRules.FullSweep(f), Is.True);
        AssertOneContour(f, pieces, "Четвёртый удар");
        Assert.That(OnOutline(pieces, -f.SweepRadius, 0f), Is.True, "круг и за спиной");
        Assert.That(OnOutline(pieces, 0f, f.SweepRadius * .5f), Is.False, "краёв сектора нет");
    }

    [Test]
    public void Outline_ShapesApart_EachDrawnWhole_ShapesInside_NotDrawn()
    {
        // Полоса шире круга и круг, целиком лежащий в секторе, — контур всё равно один и без задвоений.
        var wide = new HudWreckFootprint { SweepRadius = 2.8f, SweepHalf = 1.2f, Impact = 2.2f, ImpactRadius = 1f, LaneFrom = 2.2f, LaneTo = 6f, LaneHalf = 1.5f };
        var pieces = new List<HudWreckOutlinePiece>();
        HudWreckReachRules.Outline(wide, pieces);
        AssertOneContour(wide, pieces, "полоса шире круга");
        var swallowed = new HudWreckFootprint { SweepRadius = 2.8f, SweepHalf = 1.2f, Impact = 1.2f, ImpactRadius = .5f };
        HudWreckReachRules.Outline(swallowed, pieces);
        AssertOneContour(swallowed, pieces, "круг в секторе");
        Assert.That(pieces.Count, Is.EqualTo(3), "только сектор: два края и дуга");
    }
}
