using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Бросок якоря на живой Sim: лента тела собирается из снимка и событий ровно как в
// CharacterAnimatorView.AnchorThrow (PelagAnchorThrowFeed). Выпуск — Throw 2 в тик выпуска, натяг —
// Yank 0 в тик натяга, ловля — Catch 0 в тик ловли, конец — Catch 9 в тик конца Sim; укороченный
// полёт (Гарпун) дотягивается без скачка позы; левая лодыжка стоит, пока корень доворачивает к Dir;
// рывок и ходьба обрывают показ в свой тик; кадр «якорь на спину» — Catch 3.
public sealed class AnchorThrowTimelineSimTests
{
    private const int Slot = 0;
    private const float StartYaw = 0f;

    private sealed class Run
    {
        public readonly PelagAnchorThrowFeed Feed = new PelagAnchorThrowFeed();
        public int CastTick = -1, ReleaseTick = -1, YankTick = -1, CatchTick = -1, EndTick = -1;
        public AnchorThrowEnd EndReason;
        public float MaxSeam;
        public PelagAnchorThrowTimeline Line => Feed.Timeline;
    }

    private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

    private static Simulation Arena(PelagForm form = PelagForm.None, LayoutMap map = null)
    {
        var sim = new Simulation(4321, 128);
        if (map == null) sim.SetupTestArena(0); else sim.SetupRift(map, 4321, 0, 0, 1);
        sim.Entities.Position[0] = FixVec2.Zero;
        sim.Entities.Facing[0] = new FixVec2(Fix64.One, Fix64.Zero);
        sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
        sim.Entities.RefreshStats(0);
        sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
        sim.Entities.Lavidium[0] = Fix64.FromInt(sim.Entities.MaxLavidium[0]);
        var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
        int count = PelagForms.AppendFormNodes(form, nodes, 0);
        sim.SetAbility(Slot, AbilityDefinition.AnchorThrow(), nodes, count);
        sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
        return sim;
    }

    /// <summary>Поляна 40×40 м с камнем (x, y, r) — стена на линии броска.</summary>
    private static LayoutMap Map(double x, double y, double r)
    {
        var room = new ModuleDefinition("anchor_throw.anim", 20, 20, new ModuleConnector[0], isEntrance: true);
        var map = new LayoutMap(new ModuleSet(new[] { room }));
        map.TryPlace(0, 0, -10, -10);
        map.AddTestObstacle(new LayoutObstacle(At(x, y), Fix64.FromDouble(r), 0));
        return map;
    }

    private static int Enemy(Simulation sim, int xMm, int yMm)
    {
        int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(xMm, 1000), Fix64.Ratio(yMm, 1000)), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = Fix64.Ratio(850, 1000);
        sim.Entities.NextAttackTick[id] = int.MaxValue;
        return id;
    }

    private static InputFrame Press(double x, double y, int slot = Slot)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(1 << slot);
        input.Aim = At(x, y);
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    private static InputFrame Walk(double x, double y)
    {
        var walk = InputFrame.Empty;
        walk.Flags = (byte)InputFlags.MoveOrder;
        walk.Aim = At(x, y);
        walk.AttackTarget = -1;
        walk.AbilityTarget = -1;
        return walk;
    }

    /// <summary>
    /// Шаги Sim и вид за ними: после шага тика N показ стоит на N − 1 (тело на тик позже события) — там же
    /// применяются события шага и снимок. Перед этим берётся точка цепочки в тот же тик показа: правка ленты
    /// не должна её сдвигать (позу не дёргает).
    /// </summary>
    private static Run Play(Simulation sim, double x, double y, int ticks, Func<int, InputFrame> later = null)
    {
        var run = new Run();
        for (int t = 0; t < ticks; t++)
        {
            int tick = sim.Tick;
            sim.Step(t == 0 ? Press(x, y) : later != null ? later(t) : InputFrame.Empty);
            float now = tick - 1f;
            float before = run.Line.Active ? run.Line.ChainAt(now) : 0f;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim), Is.True, "каст Броска виден в снимке");
                    run.Line.SetStartYaw(StartYaw);
                    run.CastTick = tick;
                    continue;
                }
                run.Feed.Apply(sim, e, tick, now);
                if (e.Type == SimEventType.AnchorThrowRelease) run.ReleaseTick = tick;
                else if (e.Type == SimEventType.AnchorThrowYank) run.YankTick = tick;
                else if (e.Type == SimEventType.AnchorThrowCatch) run.CatchTick = tick;
                else if (e.Type == SimEventType.AnchorThrowEnded) { run.EndTick = tick; run.EndReason = (AnchorThrowEnd)e.Amount; }
            }
            run.Feed.Track(sim, now);
            if (run.Line.Active && run.CastTick >= 0 && tick > run.CastTick)
                run.MaxSeam = Math.Max(run.MaxSeam, Math.Abs(run.Line.ChainAt(now) - before));
        }
        return run;
    }

    private static void AssertPose(Run run, float t, PelagAnchorThrowClip clip, float frame, string what)
    {
        PelagAnchorThrowPose pose = run.Line.Sample(t, 1f);
        Assert.That(pose.Clip, Is.EqualTo(clip), what);
        Assert.That(pose.Frame, Is.EqualTo(frame).Within(.01f), what);
    }

    [Test]
    public void SevenMetres_ReleaseTautCatchEnd_OnSimTicks()
    {
        Simulation sim = Arena();
        Run run = Play(sim, 3.5, 6.06, 32);
        int c = run.CastTick;
        Assert.That(run.ReleaseTick, Is.EqualTo(c + 2));
        Assert.That(run.YankTick, Is.EqualTo(c + 10));
        Assert.That(run.CatchTick, Is.EqualTo(c + 18));
        Assert.That(run.EndTick, Is.EqualTo(c + 27));
        Assert.That(run.EndReason, Is.EqualTo(AnchorThrowEnd.Done));
        AssertPose(run, c, PelagAnchorThrowClip.Throw, 0f, "каст — стойка");
        AssertPose(run, c + 2, PelagAnchorThrowClip.Throw, 2f, "выпуск — Throw 2");
        AssertPose(run, c + 6, PelagAnchorThrowClip.Fly, .6f, "полёт F = 7 — Fly 0,6");
        AssertPose(run, c + 10, PelagAnchorThrowClip.Yank, 0f, "натяг — Yank 0");
        AssertPose(run, c + 12, PelagAnchorThrowClip.Haul, 0f, "T+2 — Haul 0 (= Yank 2)");
        AssertPose(run, c + 18, PelagAnchorThrowClip.Catch, 0f, "ловля — Catch 0");
        AssertPose(run, c + 26.5f, PelagAnchorThrowClip.Catch, 8.5f, "выход");
        Assert.That(run.Line.Sample(c + 26.9f, 1f).Finished, Is.False);
        Assert.That(run.Line.Sample(c + 27f, 1f).Finished, Is.True, "конец — тик конца Sim");
        Assert.That(run.Line.Sample(c + 20.9f, 1f).StowCue, Is.False);
        Assert.That(run.Line.Sample(c + 21f, 1f).StowCue, Is.True, "якорь на спину — Catch 3");
        Assert.That(run.MaxSeam, Is.LessThan(.01f), "события и снимок не сдвигают показанную позу");
    }

    [Test]
    public void Windup_TurnsAroundTheLeftAnkle_ThenHolds()
    {
        Simulation sim = Arena();
        Run run = Play(sim, 3.5, 6.06, 32);
        int c = run.CastTick;
        float target = run.Line.TargetYaw;
        Assert.That(target, Is.EqualTo(60f).Within(.1f));
        Assert.That(run.Line.YawAt(c), Is.EqualTo(StartYaw).Within(.01f));
        Assert.That(run.Line.YawAt(c + 1), Is.EqualTo(30f).Within(.1f), "S-кривая за 2 тика замаха");
        Assert.That(run.Line.YawAt(c + 2), Is.EqualTo(target).Within(.01f), "к выпуску — по линии броска");
        Assert.That(run.Line.YawAt(c + 20), Is.EqualTo(target).Within(.01f));
        const float scale = 1.82f / 1.8f;
        PelagSquallClipRules.LeftAnkle(StartYaw, scale, out float ax, out float ay);
        foreach (float t in new[] { c + 1f, c + 2f, c + 10f, c + 18f, c + 26.5f })
        {
            PelagAnchorThrowPose pose = run.Line.Sample(t, scale);
            PelagSquallClipRules.LeftAnkle(pose.Yaw, scale, out float bx, out float by);
            Assert.That(pose.ShiftX + bx, Is.EqualTo(ax).Within(1e-4f), "левая лодыжка стоит, t=" + t);
            Assert.That(pose.ShiftY + by, Is.EqualTo(ay).Within(1e-4f), "левая лодыжка стоит, t=" + t);
        }
    }

    [Test]
    public void CursorBehind_WindupThree_EverythingPlusOne()
    {
        Simulation sim = Arena();
        Run run = Play(sim, -7, .1, 32);
        int c = run.CastTick;
        Assert.That(run.Line.WindupTicks, Is.EqualTo(3));
        AssertPose(run, c + 1, PelagAnchorThrowClip.Throw, 2f / 3f, "замах растянут на 3 тика");
        AssertPose(run, c + 3, PelagAnchorThrowClip.Throw, 2f, "выпуск C+3");
        AssertPose(run, c + 11, PelagAnchorThrowClip.Yank, 0f, "натяг C+11");
        AssertPose(run, c + 19, PelagAnchorThrowClip.Catch, 0f, "ловля C+19");
        Assert.That(run.MaxSeam, Is.LessThan(.01f));
    }

    [Test]
    public void WallAtFiveMetres_CatchAt14_ThrowIntoAWall_ShortHaul()
    {
        Simulation sim = Arena(map: Map(6, 0, .5));
        Run run = Play(sim, 7, 0, 30);
        int c = run.CastTick;
        Assert.That(run.CatchTick, Is.EqualTo(c + 14));
        AssertPose(run, c + 8, PelagAnchorThrowClip.Yank, 0f, "натяг C+8 (F = 5)");
        AssertPose(run, c + 14, PelagAnchorThrowClip.Catch, 0f, "ловля C+14");

        sim = Arena(map: Map(1.2, 0, .5));
        run = Play(sim, 7, 0, 24);
        c = run.CastTick;
        Assert.That(run.Line.FlightTicks, Is.EqualTo(1), "бросок в стену: F = 1");
        Assert.That(run.Line.ReturnTicks, Is.EqualTo(4));
        AssertPose(run, c + 3, PelagAnchorThrowClip.Throw, 3f, "F = 1: проводка за тик");
        AssertPose(run, c + 4, PelagAnchorThrowClip.Yank, 0f, "упор в натяг");
        AssertPose(run, c + 6, PelagAnchorThrowClip.Haul, 3f, "R = 4: Haul с кадра 3");
        AssertPose(run, c + 8, PelagAnchorThrowClip.Catch, 0f, "ловля C+8");
        Assert.That(run.MaxSeam, Is.LessThan(.01f));
    }

    [Test]
    public void Harpoon_BiteShortensTheFlight_WithoutAJump()
    {
        Simulation sim = Arena(PelagForm.AnchorThrowHarpoon);
        Enemy(sim, 4000, 0);
        Run run = Play(sim, 7, 0, 30);
        int c = run.CastTick;
        Assert.That(run.YankTick, Is.EqualTo(c + 6), "укус C+5, натяг C+6");
        Assert.That(run.CatchTick, Is.EqualTo(c + 10));
        AssertPose(run, c + 6, PelagAnchorThrowClip.Yank, 0f, "натяг в тик Sim");
        AssertPose(run, c + 10, PelagAnchorThrowClip.Catch, 0f, "ловля в тик Sim");
        Assert.That(run.MaxSeam, Is.LessThan(.01f), "укус известен раньше показа: полёт дотягивается без скачка");
        float previous = 0f;
        for (float t = c; t <= c + 19f; t += .25f)
        {
            float chain = run.Line.ChainAt(t);
            Assert.That(chain, Is.GreaterThanOrEqualTo(previous - 1e-4f), "цепочка не идёт назад, t=" + t);
            previous = chain;
        }
    }

    [Test]
    public void DashInFlight_FinishesOnTheEndTick()
    {
        Simulation sim = Arena();
        Run run = Play(sim, 7, 0, 20, t => t == 5 ? Press(0, 5, PelagKit.DashSlot) : InputFrame.Empty);
        Assert.That(run.EndReason, Is.EqualTo(AnchorThrowEnd.Interrupted));
        Assert.That(run.CatchTick, Is.EqualTo(-1));
        Assert.That(run.Line.Sample(run.EndTick - .1f, 1f).Finished, Is.False);
        Assert.That(run.Line.Sample(run.EndTick, 1f).Finished, Is.True, "срыв — показ кончается в тик конца Sim");
    }

    [Test]
    public void WalkingOut_FromCatchPlusFive_FinishesThere()
    {
        Simulation sim = Arena();
        Run run = Play(sim, 7, 0, 32, t => t < 20 ? InputFrame.Empty : Walk(-5, 0));
        int c = run.CastTick;
        Assert.That(run.EndReason, Is.EqualTo(AnchorThrowEnd.WalkedOut));
        Assert.That(run.EndTick, Is.EqualTo(c + 23));
        PelagAnchorThrowPose pose = run.Line.Sample(c + 22.9f, 1f);
        Assert.That(pose.Finished, Is.False);
        Assert.That(pose.Clip, Is.EqualTo(PelagAnchorThrowClip.Catch));
        Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(PelagAnchorThrowClipRules.WalkFromFrame - .2f), "ходьба — с кадра 5 клипа ловли");
        Assert.That(run.Line.Sample(c + 23f, 1f).Finished, Is.True);
    }

    [Test]
    public void Feed_IgnoresOtherCastsEvents()
    {
        Simulation sim = Arena();
        Run run = Play(sim, 7, 0, 4);
        int serial = run.Line.Serial;
        var foreign = new SimEvent(SimEventType.AnchorThrowYank, Simulation.PlayerId, -1, 4, true, FixVec2.Zero,
            DamageType.Physical, DamageOrigin.Ability, serial + 1);
        Assert.That(run.Feed.Apply(sim, foreign, run.CastTick + 3, run.CastTick + 1), Is.Null);
        Assert.That(run.Line.Yanked, Is.False);
    }
}
