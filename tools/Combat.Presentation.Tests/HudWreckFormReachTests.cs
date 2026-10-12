using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Превью Крушения на полу по формам (06.10 вечером, AGENTS/DESIGN.md «Формы Крушения»): база — полоса LaneLength
// сборки; Волнорез — полоса стены WreckBreakwaterLength × WreckBreakwaterHalfWidth; Девятый вал — полоса по
// текущим зарядам и бледный контур полного заряда (WreckNinthPercent, +WreckNinthLengthGain); Призрачный якорь —
// полосы нет, круг WreckGhostRadius у точки выпада (и в замахе выпада вместо линии полосы — тот же круг). Числа —
// константы Sim и сборки; каждая фигура сверяется с тем,
// что Sim строит в настоящей серии (поле как WreckTests.Arena: герой в нуле, взгляд и курсор +X).
public sealed class HudWreckFormReachTests
{
    const int P = Simulation.PlayerId;
    const float Near = 2e-3f;

    static Simulation Arena(PelagForm form)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[P] = FixVec2.Zero;
        sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
        var nodes = new AbilityNode[8];
        int count = form == PelagForm.None ? 0 : PelagForms.AppendFormNodes(form, nodes, 0);
        sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, count);
        sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
        return sim;
    }

    static FixVec2 At(double x, double y) => new FixVec2(Fix64.Ratio((int)Math.Round(x * 1000), 1000), Fix64.Ratio((int)Math.Round(y * 1000), 1000));

    static readonly FixVec2 Aim = At(5, 0);

    // Враг, который стоит и не бьёт (как WreckVfxRulesTests.Guardian): в секторе обоих махов — два заряда Девятого вала.
    static void Guardian(Simulation sim, double x, double y)
    {
        int id = sim.Entities.Spawn(At(x, y), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = EnemyArchetypes.GuardianBodyRadius;
        sim.Entities.NextAttackTick[id] = int.MaxValue;
    }

    static InputFrame Input(bool press)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press ? 1 : 0);
        input.Aim = Aim;
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    /// <summary>Вся серия по курсору +X (нажатие — в открытое окно); после каждого шага — <paramref name="each"/>.</summary>
    static void PlaySeries(Simulation sim, Action<Simulation> each)
    {
        int pressed = 0;
        for (int i = 0; i < 160; i++)
        {
            WreckState before = sim.Wreck;
            bool press = pressed < 3 && (i == 0 || (sim.WreckComboOpen && before.Phase == WreckPhase.Window && before.Strikes == pressed));
            if (press) pressed++;
            sim.Step(Input(press));
            each(sim);
        }
    }

    /// <summary>Снимок Sim в тик удара выпада (событие WreckSlam).</summary>
    static WreckState SlamOf(Simulation sim)
    {
        WreckState slam = default;
        bool seen = false;
        PlaySeries(sim, s =>
        {
            foreach (SimEvent e in s.Events)
                if (e.Type == SimEventType.WreckSlam && !seen) { slam = s.Wreck; seen = true; }
        });
        Assert.That(seen, Is.True, "выпад ударил");
        return slam;
    }

    static HudWreckFootprint Footprint(Simulation sim)
    {
        Assert.That(HudWreckReachRules.FootprintOf(sim, 0, Aim, out HudWreckFootprint f, out FixVec2 direction), Is.True);
        Assert.That(direction.X.ToFloat(), Is.EqualTo(1f).Within(1e-3f), "след по курсору +X");
        return f;
    }

    static List<HudWreckOutlinePiece> Outline(in HudWreckFootprint f)
    {
        var pieces = new List<HudWreckOutlinePiece>();
        HudWreckReachRules.Outline(f, pieces);
        return pieces;
    }

    static List<HudWreckOutlinePiece> FullCharge(in HudWreckFootprint f)
    {
        var pieces = new List<HudWreckOutlinePiece>();
        HudWreckReachRules.FullChargeOutline(f, pieces);
        return pieces;
    }

    /// <summary>Вынос выпада v4: точка удара — WreckSlamReach от места, куда выпад довёз героя.</summary>
    static float Lunge(in HudWreckFootprint f) => f.Impact - Simulation.WreckSlamReach.ToFloat();

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
            return (float)(angle <= hi ? Math.Min(ends, Math.Abs(Math.Sqrt(dx * dx + dy * dy) - piece.Radius)) : ends);
        }
        double ux = bx - ax, uy = by - ay, length2 = ux * ux + uy * uy;
        double t = length2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * ux + (y - ay) * uy) / length2)) : 0;
        double px = ax + ux * t - x, py = ay + uy * t - y;
        return (float)Math.Sqrt(px * px + py * py);
    }

    static bool OnOutline(List<HudWreckOutlinePiece> pieces, float x, float y)
    {
        foreach (HudWreckOutlinePiece piece in pieces)
            if (Distance(piece, x, y) < Near) return true;
        return false;
    }

    /// <summary>Полоса превью (от героя в нуле, ось +X) — та, что Sim построил в тик удара: конец и полуширина.</summary>
    static void AssertLaneIsSims(float laneTo, float laneHalf, in WreckState slam, string what)
    {
        float end = slam.LaneOrigin.X.ToFloat() + slam.LaneDir.X.ToFloat() * slam.WallEnd.ToFloat();
        Assert.That(laneTo, Is.EqualTo(end).Within(Near), what + ": конец полосы — WallEnd Sim");
        Assert.That(laneHalf, Is.EqualTo(slam.LaneHalfWidth.ToFloat()).Within(Near), what + ": полуширина — Sim");
    }

    [Test]
    public void Base_LaneIsTheBuildLaneLength_FromTheLungePoint_AsSimBuildsIt()
    {
        Simulation sim = Arena(PelagForm.None);
        HudWreckFootprint f = Footprint(sim);
        AbilityBuild build = sim.GetAbility(0);
        Assert.That(Lunge(f), Is.GreaterThan(0f), "выпад v4 везёт героя вперёд");
        Assert.That(f.LaneTo - Lunge(f), Is.EqualTo(build.Get(AbilityStatType.LaneLength).ToFloat()).Within(Near), "вал — LaneLength сборки");
        Assert.That(f.LaneHalf * 2f, Is.EqualTo(build.Get(AbilityStatType.Width).ToFloat()).Within(Near), "ширина — Width сборки");
        Assert.That(f.ImpactRadius, Is.EqualTo(Simulation.WreckSlamRadius.ToFloat()).Within(Near));
        Assert.That(f.CrashRadius, Is.EqualTo(0f));
        Assert.That(f.GhostRadius, Is.EqualTo(0f));
        Assert.That(f.FullLaneTo, Is.EqualTo(0f), "бледного контура у базы нет");
        Assert.That(FullCharge(f), Is.Empty);
        Assert.That(OnOutline(Outline(f), f.LaneTo, 0f), Is.True, "торец полосы");
        AssertLaneIsSims(f.LaneTo, f.LaneHalf, SlamOf(Arena(PelagForm.None)), "база");
    }

    [Test]
    public void Breakwater_WallLaneLengthAndWidth_CrashCircleAtItsEnd()
    {
        Simulation sim = Arena(PelagForm.WreckBreakwater);
        HudWreckFootprint f = Footprint(sim);
        Assert.That(f.LaneTo - Lunge(f), Is.EqualTo(Simulation.WreckBreakwaterLength.ToFloat()).Within(Near), "стена ~8 м");
        Assert.That(f.LaneHalf, Is.EqualTo(Simulation.WreckBreakwaterHalfWidth.ToFloat()).Within(Near), "ширина стены");
        Assert.That(f.CrashRadius, Is.EqualTo(Simulation.WreckBreakwaterCrashRadius.ToFloat()));
        Assert.That(f.GhostRadius, Is.EqualTo(0f));
        Assert.That(FullCharge(f), Is.Empty);
        List<HudWreckOutlinePiece> pieces = Outline(f);
        Assert.That(OnOutline(pieces, f.LaneTo + f.CrashRadius, 0f), Is.True, "дальний край круга обрушения");
        float side = (f.Impact + f.ImpactRadius + f.LaneTo - f.CrashRadius) * .5f;
        Assert.That(OnOutline(pieces, side, f.LaneHalf) && OnOutline(pieces, side, -f.LaneHalf), Is.True, "бока стены по её ширине");
        AssertLaneIsSims(f.LaneTo, f.LaneHalf, SlamOf(Arena(PelagForm.WreckBreakwater)), "Волнорез");
    }

    [Test]
    public void NinthWave_BeforeTheSeries_BaseLane_PlusFaintFullChargeLane()
    {
        HudWreckFootprint basis = Footprint(Arena(PelagForm.None));
        Simulation sim = Arena(PelagForm.WreckNinthWave);
        HudWreckFootprint f = Footprint(sim);
        // Зарядов до серии нет: сплошной след — как у базы.
        Assert.That(HudWreckReachRules.NinthCharges(sim, 0), Is.EqualTo(0));
        Assert.That(f.LaneTo, Is.EqualTo(basis.LaneTo).Within(Near));
        Assert.That(f.LaneHalf, Is.EqualTo(basis.LaneHalf).Within(Near));
        // Бледно поверх — полный заряд: ширина × WreckNinthPercent(2) / WreckNinthPercent(0), длина + WreckNinthLengthGain.
        float widen = Simulation.WreckNinthPercent(Simulation.WreckNinthMaxCharges) / (float)Simulation.WreckNinthPercent(0);
        Assert.That(f.FullLaneHalf, Is.EqualTo(f.LaneHalf * widen).Within(Near), "полный заряд шире");
        Assert.That(f.FullLaneTo, Is.EqualTo(f.LaneTo + Simulation.WreckNinthLengthGain.ToFloat()).Within(Near), "и длиннее");
        // Один заряд — та же геометрия Sim: шире ×WreckNinthPercent(1), длина прежняя.
        Assert.That(sim.WreckLanePreview(0, Aim, 1, out _, out _, out _, out Fix64 oneTo, out Fix64 oneHalf, out _, out _), Is.True);
        Assert.That(oneHalf.ToFloat(), Is.EqualTo(f.LaneHalf * Simulation.WreckNinthPercent(1) / Simulation.WreckNinthPercent(0)).Within(Near));
        Assert.That(oneTo.ToFloat(), Is.EqualTo(f.LaneTo).Within(Near));

        List<HudWreckOutlinePiece> solid = Outline(f), faint = FullCharge(f);
        Assert.That(faint, Is.Not.Empty, "бледный контур есть");
        foreach (HudWreckOutlinePiece piece in faint)
            for (int k = 1; k < 8; k++)
            {
                piece.At(k / 8f, out float x, out float y);
                Assert.That(HudWreckReachRules.Inside(f, x, y), Is.False, "бледный контур — снаружи следа");
                bool onFull = Math.Abs(Math.Abs(y) - f.FullLaneHalf) < Near || Math.Abs(x - f.FullLaneTo) < Near || Math.Abs(x - f.LaneFrom) < Near;
                Assert.That(onFull, Is.True, "бледный контур — по краю полной полосы");
            }
        Assert.That(OnOutline(faint, f.FullLaneTo, 0f), Is.True, "торец полной полосы");
        float mid = (f.LaneTo + f.FullLaneTo) * .5f;
        Assert.That(OnOutline(faint, mid, f.FullLaneHalf) && OnOutline(faint, mid, -f.FullLaneHalf), Is.True, "бока полной полосы");
        Assert.That(OnOutline(solid, f.LaneTo, 0f), Is.True, "сплошной — торец полосы без зарядов");
        Assert.That(OnOutline(solid, f.FullLaneTo, 0f), Is.False, "полный заряд сплошным не рисуется");
    }

    [Test]
    public void NinthWave_TwoChargesInTheSeries_LungeLineAndLaneFollowCurrentCharges_AsSimStrikes()
    {
        Simulation sim = Arena(PelagForm.WreckNinthWave);
        Guardian(sim, 1.5, 1.2);   // в секторе обоих махов, не на оси выпада
        HudWreckFootprint before = Footprint(sim);
        WreckState slam = default;
        bool struck = false;
        int lineTicks = 0;
        PlaySeries(sim, s =>
        {
            foreach (SimEvent e in s.Events)
                if (e.Type == SimEventType.WreckSlam && !struck) { slam = s.Wreck; struck = true; }
            WreckState w = s.Wreck;
            if (!s.WreckActive || w.Stage != HudWreckReachRules.LungeStage || w.Phase != WreckPhase.Windup) return;
            lineTicks++;
            Assert.That(HudWreckReachRules.NinthCharges(s, 0), Is.EqualTo(Simulation.WreckNinthMaxCharges), "оба маха задели");
            float hero = s.Entities.Position[P].X.ToFloat();
            Assert.That(HudWreckReachRules.LungeLineOf(s, 0, Aim, out _, out _, out float to), Is.True);
            Assert.That(hero + to, Is.EqualTo(before.FullLaneTo).Within(Near), "линия выпада — до конца полосы двух зарядов");
            Assert.That(HudWreckReachRules.FootprintOf(s, 0, Aim, out HudWreckFootprint now, out _), Is.True);
            Assert.That(now.LaneHalf, Is.EqualTo(before.FullLaneHalf).Within(Near), "сплошная полоса — по текущим зарядам");
            Assert.That(hero + now.LaneTo, Is.EqualTo(before.FullLaneTo).Within(Near));
            Assert.That(now.FullLaneTo, Is.EqualTo(0f), "заряды полные — бледного контура нет");
        });
        Assert.That(lineTicks, Is.GreaterThan(0), "замах выпада был");
        Assert.That(struck, Is.True);
        Assert.That(slam.NinthCharges, Is.EqualTo(Simulation.WreckNinthMaxCharges));
        AssertLaneIsSims(before.FullLaneTo, before.FullLaneHalf, slam, "Девятый вал, два заряда");
    }

    [Test]
    public void GhostAnchor_NoLane_CircleOfTheSimRadiusAtTheLungePoint()
    {
        HudWreckFootprint basis = Footprint(Arena(PelagForm.None));
        Simulation sim = Arena(PelagForm.WreckGhostAnchor);
        HudWreckFootprint f = Footprint(sim);
        Assert.That(f.LaneHalf, Is.EqualTo(0f), "вала нет — полосы нет");
        Assert.That(f.LaneTo, Is.EqualTo(f.LaneFrom));
        Assert.That(f.GhostRadius, Is.EqualTo(Simulation.WreckGhostRadius.ToFloat()), "круг падения — радиус Sim");
        Assert.That(f.Impact, Is.EqualTo(basis.Impact).Within(Near), "у точки выпада");
        Assert.That(f.CrashRadius, Is.EqualTo(0f));
        Assert.That(FullCharge(f), Is.Empty);

        List<HudWreckOutlinePiece> pieces = Outline(f);
        Assert.That(OnOutline(pieces, f.Impact + f.GhostRadius, 0f), Is.True, "дальний край круга падения");
        Assert.That(OnOutline(pieces, f.Impact, f.GhostRadius) && OnOutline(pieces, f.Impact, -f.GhostRadius), Is.True, "бока круга");
        Assert.That(OnOutline(pieces, basis.LaneTo, 0f), Is.False, "торца полосы базы нет");
        Assert.That(OnOutline(pieces, basis.LaneTo - .3f, basis.LaneHalf), Is.False, "бока полосы базы нет");
        foreach (HudWreckOutlinePiece piece in pieces)
            for (int k = 1; k < 8; k++)
            {
                piece.At(k / 8f, out float x, out float y);
                Assert.That(HudWreckReachRules.Inside(f, x, y), Is.False, "контур — край следа");
                float fromImpact = (float)Math.Sqrt((x - f.Impact) * (x - f.Impact) + y * y);
                float fromHero = (float)Math.Sqrt(x * x + y * y);
                // Всё, что дальше сектора махов, — дуга круга падения: ни полосы, ни круга удара поверх.
                if (fromHero > f.SweepRadius + Near)
                    Assert.That(fromImpact, Is.EqualTo(f.GhostRadius).Within(Near), "за сектором — только круг падения");
            }

        // Sim: якорь падает в точку, где HUD кладёт центр круга, с тем же радиусом. Между третьим нажатием и контактом
        // линии полосы нет — тем же штрихом круг падения (LungeDropOf) в ту же точку.
        bool fell = false;
        int dropTicks = 0;
        var drop = new List<HudWreckOutlinePiece>();
        PlaySeries(sim, s =>
        {
            WreckState w = s.Wreck;
            if (s.WreckActive && HudWreckReachRules.Hint(w, w.Slot == 0) == HudWreckHint.LungeLine)
            {
                dropTicks++;
                float hero = s.Entities.Position[P].X.ToFloat();
                Assert.That(HudWreckReachRules.LungeLineOf(s, 0, Aim, out _, out _, out _), Is.False, "линии полосы у Призрачного якоря нет");
                Assert.That(HudWreckReachRules.LungeDropOf(s, 0, Aim, out FixVec2 axis, out float impact, out float radius), Is.True);
                Assert.That(axis.X.ToFloat(), Is.EqualTo(1f).Within(1e-3f), "ось — направление Sim");
                Assert.That(hero + impact, Is.EqualTo(f.Impact).Within(Near), "центр — точка выпада");
                Assert.That(radius, Is.EqualTo(Simulation.WreckGhostRadius.ToFloat()), "радиус — Sim");
                HudWreckReachRules.DropOutline(impact, radius, drop);
                Assert.That(drop, Has.Count.EqualTo(1), "один замкнутый круг");
                Assert.That(drop[0].Arc && Math.Abs(drop[0].To - drop[0].From - 2 * Math.PI) < 1e-4, Is.True);
                Assert.That(OnOutline(drop, impact + radius, 0f) && OnOutline(drop, impact - radius, 0f), Is.True, "края круга");
                Assert.That(OnOutline(drop, impact, radius) && OnOutline(drop, impact, -radius), Is.True);
            }
            foreach (SimEvent e in s.Events)
            {
                if (e.Type != SimEventType.WreckGhostAnchor) continue;
                fell = true;
                Assert.That(e.Position.X.ToFloat(), Is.EqualTo(f.Impact).Within(Near), "центр круга — точка падения Sim");
                Assert.That(e.Position.Y.ToFloat(), Is.EqualTo(0f).Within(Near));
                Assert.That(e.Amount, Is.EqualTo((int)Math.Round(f.GhostRadius * 100f)), "радиус — тот, которым бьёт Sim");
            }
        });
        Assert.That(dropTicks, Is.GreaterThan(0), "замах выпада был");
        Assert.That(fell, Is.True, "призрачный якорь упал");
    }

    [Test]
    public void LungeDrop_OnlyForGhostAnchor_OtherFormsKeepTheLaneLine()
    {
        foreach (PelagForm form in new[] { PelagForm.None, PelagForm.WreckBreakwater, PelagForm.WreckNinthWave })
        {
            Simulation sim = Arena(form);
            Assert.That(HudWreckReachRules.LungeDropOf(sim, 0, Aim, out _, out _, out _), Is.False, form + ": круга падения нет");
            Assert.That(HudWreckReachRules.LungeLineOf(sim, 0, Aim, out _, out _, out float to), Is.True, form + ": линия полосы");
            Assert.That(to, Is.EqualTo(Footprint(sim).LaneTo).Within(Near), form + ": линия — до конца полосы следа");
        }
        var empty = new List<HudWreckOutlinePiece> { HudWreckOutlinePiece.Segment(0f, 0f, 1f, 0f) };
        HudWreckReachRules.DropOutline(2f, 0f, empty);
        Assert.That(empty, Is.Empty, "нулевой круг — пусто, список очищен");
    }
}
