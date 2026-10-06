using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение v2, вид (PelagWreckVfxRules): видимый край = край урона Sim (правило владельца 02.10).
// Гребень вала стоит на краю шага Sim в тик шага и не уходит за конец полосы; задетый валом —
// не раньше, чем гребень дошёл до его тела; кто бит — тем же правилом, что у контроллера; капли
// взрыва Панциря и комья круга падают внутри радиуса Sim. NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckVfxRulesTests
{
    private const int P = Simulation.PlayerId;

    private static Fix64 M(double v) => Fix64.Ratio((int)Math.Round(v * 1000), 1000);

    private static Simulation Arena(PelagForm form = PelagForm.None)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[P] = FixVec2.Zero;
        sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
        var nodes = new AbilityNode[4];
        int count = form == PelagForm.None ? 0 : PelagForms.AppendFormNodes(form, nodes, 0);
        Array.Resize(ref nodes, count);
        sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, nodes.Length);
        sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
        return sim;
    }

    private static int Guardian(Simulation sim, double x, double y)
    {
        int id = sim.Entities.Spawn(new FixVec2(M(x), M(y)), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = EnemyArchetypes.GuardianBodyRadius;
        sim.Entities.NextAttackTick[id] = int.MaxValue;
        return id;
    }

    private static InputFrame Press(bool press, bool hold = false)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press || hold ? 1 : 0);
        input.Aim = new FixVec2(M(5), Fix64.Zero);
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    /// <summary>Событие шага с тиком события (= тик до шага, как FrameEventContext.SimulationTick − 1).</summary>
    private struct Seen
    {
        public int Tick;
        public SimEvent Event;
        public WreckState Wreck;
    }

    private static List<Seen> Play(Simulation sim, int ticks, Func<int, InputFrame> input)
    {
        var seen = new List<Seen>();
        for (int i = 0; i < ticks; i++)
        {
            int tick = sim.Tick;
            sim.Step(input(i));
            foreach (SimEvent e in sim.Events) seen.Add(new Seen { Tick = tick, Event = e, Wreck = sim.Wreck });
        }
        return seen;
    }

    /// <summary>Тот же разбор «кто бит», что PelagVfxController.Wreck (ConsumeWreckEvent + TakeWreckDamage).</summary>
    private static Dictionary<int, List<(int tick, WreckVfxHit hit)>> Classify(Simulation sim, List<Seen> seen)
    {
        var hits = new Dictionary<int, List<(int, WreckVfxHit)>>();
        WreckVfxCue cue = WreckVfxCue.None;
        int cueTick = -1, cueTarget = -1, waveTick = -1, travel = 0;
        float ix = 0f, iy = 0f, radius = 0f;
        bool wall = false;
        foreach (Seen s in seen)
        {
            SimEvent e = s.Event;
            switch (e.Type)
            {
                case SimEventType.WreckStage:
                    cue = e.Amount < 2 ? WreckVfxCue.Swing : e.Amount == 2 ? WreckVfxCue.Slam : WreckVfxCue.Fourth; cueTick = s.Tick; cueTarget = -1; break;
                case SimEventType.WreckSlam:
                    cue = WreckVfxCue.Slam; cueTick = s.Tick; cueTarget = -1;
                    waveTick = s.Tick; travel = e.Amount; ix = e.Position.X.ToFloat(); iy = e.Position.Y.ToFloat();
                    radius = s.Wreck.ImpactRadius.ToFloat(); wall = (PelagForm)e.ActionVariant == PelagForm.WreckBreakwater; break;
                case SimEventType.WreckBreakwaterCatch: cue = WreckVfxCue.Catch; cueTick = s.Tick; cueTarget = e.Target; break;
                case SimEventType.WreckBreakwaterCrash: cue = WreckVfxCue.Crash; cueTick = s.Tick; cueTarget = -1; break;
                case SimEventType.WreckShellBurst: cue = WreckVfxCue.Burst; cueTick = s.Tick; cueTarget = -1; break;
                case SimEventType.Damage:
                    if (e.Source != P || e.DamageOrigin != DamageOrigin.Ability) break;
                    float dx = e.Position.X.ToFloat() - ix, dy = e.Position.Y.ToFloat() - iy;
                    bool inCircle = waveTick == s.Tick && PelagWreckVfxRules.InSlamCircle((float)Math.Sqrt(dx * dx + dy * dy), radius,
                        sim.Entities.BodyRadius[e.Target].ToFloat());
                    WreckVfxHit hit = PelagWreckVfxRules.ClassifyHit(cue, cueTick, cueTarget, s.Tick, e.Target, inCircle, waveTick, travel, wall);
                    if (!hits.TryGetValue(e.Target, out var list)) hits[e.Target] = list = new List<(int, WreckVfxHit)>();
                    list.Add((s.Tick, hit));
                    break;
            }
        }
        return hits;
    }

    private static Seen First(List<Seen> seen, SimEventType type)
    {
        foreach (Seen s in seen) if (s.Event.Type == type) return s;
        Assert.Fail("нет события " + type);
        return default;
    }

    [Test]
    public void ShownTick_IsTwoTicksBehindTheSim()
    {
        Assert.That(PelagWreckVfxRules.ShownTick(30, .25f), Is.EqualTo(28.25f).Within(1e-5f));
    }

    // Выпад v4 везёт героя на 0,6 м (Simulation.Wreck.Lunge): цели на 0,6 дальше — та же геометрия полосы, что была.
    [TestCase(5.1), TestCase(6.1), TestCase(7.1), TestCase(7.44)]
    public void WaveCrest_ReachesTheBodyInTheTickOfItsHit_NotEarlier(double along)
    {
        Simulation sim = Arena();
        int foe = Guardian(sim, along, 0);
        List<Seen> seen = Play(sim, 52, i => Press(i < 20));
        Seen slam = First(seen, SimEventType.WreckSlam);
        WreckState w = slam.Wreck;
        float start = w.WaveStart.ToFloat(), step = w.WaveStep.ToFloat(), end = w.WallEnd.ToFloat(), body = .85f;
        // Фронт — от героя в тик удара (LaneOrigin): выпад v4 везёт его на 0,6 м, цель — в осях полосы.
        along -= w.LaneOrigin.X.ToFloat();
        int hitTick = -1;
        foreach (Seen s in seen)
            if (s.Event.Type == SimEventType.Damage && s.Event.Target == foe && s.Tick > slam.Tick - 1) hitTick = s.Tick;
        Assert.That(hitTick, Is.GreaterThanOrEqualTo(slam.Tick), "вал задел цель");
        float atHit = PelagWreckVfxRules.WaveFront(hitTick, slam.Tick, start, step, slam.Event.Amount, end);
        float before = PelagWreckVfxRules.WaveFront(hitTick - 1, slam.Tick, start, step, slam.Event.Amount, end);
        Assert.That(atHit, Is.GreaterThanOrEqualTo((float)along - body - 1e-3f), "гребень дошёл до тела в тик урона");
        Assert.That(before, Is.LessThan((float)along - body + 1e-3f), "за тик до урона гребень ещё не у тела");
    }

    [Test]
    public void WaveCrest_EndsOnTheLaneEnd_AndNeverPassesIt()
    {
        Simulation sim = Arena();
        List<Seen> seen = Play(sim, 52, i => Press(i < 20));
        Seen slam = First(seen, SimEventType.WreckSlam);
        WreckState w = slam.Wreck;
        float start = w.WaveStart.ToFloat(), step = w.WaveStep.ToFloat(), end = w.WallEnd.ToFloat();
        Assert.That(end, Is.EqualTo(6f).Within(1e-3f), "полоса 6 м");
        Assert.That(slam.Event.Amount, Is.EqualTo(8), "8 шагов от 2,2 м");
        float last = PelagWreckVfxRules.WaveFront(PelagWreckVfxRules.WaveEndTick(slam.Tick, slam.Event.Amount), slam.Tick, start, step, slam.Event.Amount, end);
        Assert.That(last, Is.EqualTo(end).Within(1e-3f), "последний шаг — край полосы");
        for (float shown = slam.Tick - 2; shown < slam.Tick + 20; shown += .25f)
            Assert.That(PelagWreckVfxRules.WaveFront(shown, slam.Tick, start, step, slam.Event.Amount, end), Is.LessThanOrEqualTo(end + 1e-4f));
    }

    [Test]
    public void WaveCrest_OnTheShownTickOfAStep_EqualsTryGetWreckWaveRightAfterThatStep()
    {
        Simulation sim = Arena();
        int waveTick = -1, travel = 0, checkedSteps = 0;
        for (int i = 0; i < 52; i++)
        {
            int tick = sim.Tick;
            sim.Step(Press(i < 20));
            foreach (SimEvent e in sim.Events)
                if (e.Type == SimEventType.WreckSlam) { waveTick = tick; travel = e.Amount; }
            if (waveTick < 0 || !sim.TryGetWreckWave(out _, out _, out Fix64 from, out Fix64 reach, out _)) continue;
            WreckState w = sim.Wreck;
            float shown = tick;   // тело этого шага показ рисует на тике шага
            Assert.That(PelagWreckVfxRules.WaveFront(shown, waveTick, from.ToFloat(), w.WaveStep.ToFloat(), travel, w.WallEnd.ToFloat()),
                Is.EqualTo(reach.ToFloat()).Within(1e-3f), "гребень = фронт Sim (TryGetWreckWave) на тике " + tick);
            checkedSteps++;
        }
        Assert.That(checkedSteps, Is.GreaterThanOrEqualTo(8), "все шаги вала сверены");
    }

    [Test]
    public void Hits_SwingsCircleAndWave_AreToldApart()
    {
        Simulation sim = Arena();
        int near = Guardian(sim, 3.5, 0), far = Guardian(sim, 5.5, 0);
        List<Seen> seen = Play(sim, 52, i => Press(i < 20));
        var hits = Classify(sim, seen);
        Assert.That(hits[near].Count, Is.EqualTo(3), "мах, обратный мах, круг");
        Assert.That(hits[near][0].hit, Is.EqualTo(WreckVfxHit.Swing));
        Assert.That(hits[near][1].hit, Is.EqualTo(WreckVfxHit.Swing));
        Assert.That(hits[near][2].hit, Is.EqualTo(WreckVfxHit.Circle));
        Assert.That(hits[far][hits[far].Count - 1].hit, Is.EqualTo(WreckVfxHit.Wave));
    }

    [Test]
    public void Breakwater_CatchAndCrash_AreWallAndCrash_LiftFollowsCatchToCrash()
    {
        Simulation sim = Arena(PelagForm.WreckBreakwater);
        int foe = Guardian(sim, 5.0, 0);
        List<Seen> seen = Play(sim, 62, i => Press(i < 20));
        Seen caught = First(seen, SimEventType.WreckBreakwaterCatch);
        Seen crash = First(seen, SimEventType.WreckBreakwaterCrash);
        Assert.That(crash.Tick, Is.EqualTo(caught.Tick + caught.Event.Amount), "обрушение — через Amount тиков после подхвата");
        var hits = Classify(sim, seen);
        bool wall = false, crashed = false;
        foreach (var h in hits[foe]) { wall |= h.hit == WreckVfxHit.Wall; crashed |= h.hit == WreckVfxHit.Crash; }
        Assert.That(wall, Is.True, "удар стены");
        Assert.That(crashed, Is.True, "обрушение");
        int catchTick = caught.Tick, crashTick = caught.Tick + caught.Event.Amount;
        Assert.That(PelagWreckVfxRules.CarriedLiftAt(catchTick - .5f, catchTick, crashTick), Is.EqualTo(0f));
        Assert.That(PelagWreckVfxRules.CarriedLiftAt(crashTick, catchTick, crashTick), Is.EqualTo(PelagWreckVfxRules.CarriedLift).Within(1e-4f));
        Assert.That(PelagWreckVfxRules.CarriedLiftAt(crashTick + PelagWreckVfxRules.CarriedDropTicks, catchTick, crashTick), Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void NinthWave_TwoCharges_DoubleCrestHeightAndHumpMatchesSimLaneWidth()
    {
        // 06.10 вечером: заряды — задевшие махи (удержания нет); цель в секторе обоих махов — два заряда, ×2.
        Simulation sim = Arena(PelagForm.WreckNinthWave);
        Guardian(sim, 2.0, 0);
        List<Seen> seen = Play(sim, 72, i => Press(i < 20));
        Seen slam = First(seen, SimEventType.WreckSlam);
        Assert.That(slam.Event.Flag, Is.True, "два заряда");
        Assert.That(slam.Wreck.NinthCharges, Is.EqualTo(2));
        WreckState w = slam.Wreck;
        Assert.That(PelagWreckVfxRules.CrestHeight(PelagForm.WreckNinthWave, w.DamagePercent),
            Is.EqualTo(2f * PelagWreckVfxRules.CrestBaseHeight).Within(1e-4f));
        Assert.That(PelagWreckVfxRules.HumpHalfWidth(.75f, Simulation.WreckChargeMaxTicks), Is.EqualTo(w.LaneHalfWidth.ToFloat()).Within(1e-3f));
        Assert.That(PelagWreckVfxRules.CrestHeight(PelagForm.None, 100), Is.EqualTo(.7f).Within(1e-4f));
        Assert.That(PelagWreckVfxRules.CrestHeight(PelagForm.WreckBreakwater, 100), Is.EqualTo(1.1f).Within(1e-4f));
    }

    [Test]
    public void ShellBurst_DropsLandInsideTheSimRadius()
    {
        float radius = Simulation.WreckShellBurstRadius.ToFloat(), g = 9.81f * 1.6f;
        foreach (float share in new[] { PelagWreckVfxRules.BurstReachMin, PelagWreckVfxRules.BurstReachMax })
            foreach (float height in new[] { .4f, 1.7f })
                foreach (float flight in new[] { .32f, .5f })
                {
                    float reach = radius * share - PelagWreckVfxRules.ShellRadius;
                    PelagWreckVfxRules.Launch(reach, flight, height, g, out float h, out float v);
                    float y = height + v * flight - .5f * g * flight * flight;
                    Assert.That(y, Is.EqualTo(0f).Within(1e-3f), "падает на землю к концу полёта");
                    Assert.That(PelagWreckVfxRules.ShellRadius + h * flight, Is.LessThanOrEqualTo(radius + 1e-3f), "внутри радиуса взрыва");
                }
    }

    [Test]
    public void Crater_OpensToTheSimCircleByTheNextTick_AndDriftsNoFurther()
    {
        const int slam = 24;
        const float r = 1.2f;
        Assert.That(PelagWreckVfxRules.CraterCrest(slam - .6f, slam, r), Is.EqualTo(0f));
        Assert.That(PelagWreckVfxRules.CraterCrest(slam + 1f, slam, r), Is.GreaterThanOrEqualTo(r - 1e-4f));
        for (float shown = slam; shown < slam + 30; shown += .5f)
            Assert.That(PelagWreckVfxRules.CraterCrest(shown, slam, r), Is.LessThanOrEqualTo(r + PelagWreckVfxRules.FrontDrift + 1e-4f));
    }

    [Test]
    public void Swing_TangentAndRecoil()
    {
        // Взгляд +X: мах справа налево уводит цель влево (+Z), обратный — вправо.
        PelagWreckVfxRules.SwingTangent(2f, 0f, 1, out float x, out float z);
        Assert.That(x, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(z, Is.EqualTo(1f).Within(1e-5f));
        PelagWreckVfxRules.SwingTangent(2f, 0f, -1, out _, out z);
        Assert.That(z, Is.EqualTo(-1f).Within(1e-5f));
        Assert.That(PelagWreckVfxRules.Recoil(PelagWreckVfxRules.RecoilSeconds * .25f), Is.EqualTo(PelagWreckVfxRules.RecoilMeters).Within(1e-4f));
        Assert.That(PelagWreckVfxRules.Recoil(PelagWreckVfxRules.RecoilSeconds), Is.EqualTo(0f));
        Assert.That(PelagWreckVfxRules.ArcBreakAge(7f, 7), Is.EqualTo(0f), "до удара дуга живая");
        Assert.That(PelagWreckVfxRules.ArcHeadHalfWidth(0, 1f), Is.GreaterThan(PelagWreckVfxRules.ArcHeadHalfWidth(0, 0f)), "к удару толще");
    }

    [Test]
    public void Sweep_HeadGoesWhereTheAnchorFlies_OnTheHeadRadius_BeforeTheContact()
    {
        Simulation sim = Arena();
        Play(sim, 2, i => Press(i == 0));
        Assert.That(sim.Wreck.Stage, Is.EqualTo(0), "первый мах идёт");
        Assert.That(sim.Wreck.Side, Is.EqualTo(1), "первый мах Sim — справа налево");
        Assert.That(PelagWreckVfxRules.SweepHeadSide(0f, sim.Wreck.Side), Is.EqualTo(-1), "без скорости головы — голова серпа слева");
        Assert.That(PelagWreckVfxRules.SweepHeadSide(0f, -1), Is.EqualTo(1), "обратный мах — справа");
        Assert.That(PelagWreckVfxRules.SweepHeadSide(9f, 1), Is.EqualTo(1), "настоящая голова летит вправо — серп за ней");
        Assert.That(PelagWreckVfxRules.SweepHeadSide(-9f, -1), Is.EqualTo(-1));
        Assert.That(PelagWreckVfxRules.SweepRadius(2.4f), Is.EqualTo(2.4f).Within(1e-5f), "край серпа — радиус головы");
        Assert.That(PelagWreckVfxRules.SweepRadius(.3f), Is.EqualTo(1.8f).Within(1e-5f));
        Assert.That(PelagWreckVfxRules.SweepRadius(9f), Is.EqualTo(3f).Within(1e-5f));
        Assert.That(PelagWreckVfxRules.SweepLeadTicks, Is.GreaterThan(0f).And.LessThan(Simulation.WreckSwingWindupTicks),
            "серп рождается в замахе, до удара");
    }

    [Test]
    public void SlamCircle_Boss_UsesTheHullGap_LikeTheSim()
    {
        Assert.That(PelagWreckVfxRules.InSlamCircleHull(1.1f, 1.2f), Is.True, "корпус в круге");
        Assert.That(PelagWreckVfxRules.InSlamCircleHull(1.3f, 1.2f), Is.False, "корпус за кругом — это вал");
        Assert.That(PelagWreckVfxRules.InSlamCircle(2f, 1.2f, .85f), Is.True);
        Assert.That(PelagWreckVfxRules.CueQueueMax, Is.GreaterThan(PelagWreckVfxRules.CueQueueStart), "очередь растёт с толпой");
    }

    [Test]
    public void Crack_StartsInsideTheCircle_CarriedTiltFollowsTheLift()
    {
        Assert.That(PelagWreckVfxRules.CrackBackOfRadius, Is.LessThan(1f), "трещина начинается внутри круга удара");
        Assert.That(PelagWreckVfxRules.CrackLengthOfRadius - PelagWreckVfxRules.CrackBackOfRadius, Is.GreaterThan(1f), "и выходит за его край по полосе");
        int catchTick = 40, crashTick = 52;
        Assert.That(PelagWreckVfxRules.CarriedTiltAt(PelagWreckVfxRules.CarriedLiftAt(catchTick - 1f, catchTick, crashTick)), Is.EqualTo(0f));
        Assert.That(PelagWreckVfxRules.CarriedTiltAt(PelagWreckVfxRules.CarriedLiftAt(crashTick, catchTick, crashTick)),
            Is.EqualTo(PelagWreckVfxRules.CarriedTiltDegrees).Within(1e-3f), "на груди стены — полный наклон");
        Assert.That(PelagWreckVfxRules.CarriedTiltAt(PelagWreckVfxRules.CarriedLiftAt(crashTick + PelagWreckVfxRules.CarriedDropTicks, catchTick, crashTick)),
            Is.EqualTo(0f).Within(1e-3f), "после обрушения стоит прямо");
    }
}
