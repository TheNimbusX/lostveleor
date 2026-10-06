using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Шквал v2 на живой Sim: лента вида собирается из событий и снимка ровно как в
// CharacterAnimatorView.Squall (PelagSquallFeed). Порядок клипов, стыки без смешивания,
// кадр 6 в тик удара, взгляд по прыжку, стопа стоит в опоре, в конце — взгляд Sim.
public sealed class SquallTimelineSimTests
{
    private const int Slot = 0;
    private const float Step = .25f;

    private sealed class Run
    {
        public readonly PelagSquallFeed Feed = new PelagSquallFeed();
        public readonly Dictionary<int, FixVec2> Position = new Dictionary<int, FixVec2>();
        public readonly List<(int tick, int index, float yaw)> Jumps = new List<(int, int, float)>();
        public readonly List<(int tick, int index)> Strikes = new List<(int, int)>();
        public int EndTick = -1, ReturnTick = -1, ReturnTicks;
        public SquallEnd EndReason;
        public float EndYaw;
        public PelagSquallTimeline Line => Feed.Timeline;
    }

    private static Simulation Arena(PelagForm form)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
        int count = PelagForms.AppendFormNodes(form, nodes, 0);
        sim.SetAbility(Slot, AbilityDefinition.ChainStep(), nodes, count);
        return sim;
    }

    private static int Enemy(Simulation sim, int xMm, int yMm)
    {
        int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(xMm, 1000), Fix64.Ratio(yMm, 1000)), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = Fix64.Ratio(450, 1000);
        sim.Entities.NextAttackTick[id] = int.MaxValue;
        return id;
    }

    private static float YawOf(FixVec2 v) => PelagSquallClipRules.YawOf(v.X.ToFloat(), v.Y.ToFloat());

    private static Run Play(Simulation sim, int target, int ticks)
    {
        var run = new Run();
        run.Position[sim.Tick - 1] = sim.Entities.Position[Simulation.PlayerId];
        for (int t = 0; t < ticks; t++)
        {
            int tick = sim.Tick;
            InputFrame input = InputFrame.Empty;
            if (t == 0) { input.AbilityMask = 1 << Slot; input.AttackTarget = -1; input.AbilityTarget = target; }
            sim.Step(input);
            run.Position[tick] = sim.Entities.Position[Simulation.PlayerId];
            // События шага доходят до вида, когда показ ещё на тике раньше (тело рисуется с отставанием).
            float now = tick - .5f;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim), Is.True, "каст Шквала виден в снимке");
                    run.Line.SetStartYaw(-90f, now);
                }
                if (e.Type == SimEventType.SquallJump) run.Jumps.Add((tick, e.ActionVariant, YawOf(e.Position - sim.Squall.From)));
                if (e.Type == SimEventType.SquallStrike) run.Strikes.Add((tick, e.ActionVariant));
                if (e.Type == SimEventType.SquallReturn) { run.ReturnTick = tick; run.ReturnTicks = e.Amount; }
                if (e.Type == SimEventType.SquallEnded)
                {
                    run.EndTick = tick;
                    run.EndReason = (SquallEnd)e.Amount;
                    run.EndYaw = YawOf(sim.Entities.Facing[Simulation.PlayerId]);
                }
                run.Feed.Apply(sim, e, tick, now);
            }
            run.Feed.DetectLateFinish(sim, now);
        }
        return run;
    }

    /// <summary>Тело на тике показа: между концами двух шагов, как TickDriver.GetRenderPosition.</summary>
    private static (float x, float y) Render(Run run, float t)
    {
        int a = (int)Math.Floor(t);
        FixVec2 p0 = run.Position[a], p1 = run.Position.TryGetValue(a + 1, out FixVec2 next) ? next : p0;
        float u = t - a;
        return (p0.X.ToFloat() + (p1.X.ToFloat() - p0.X.ToFloat()) * u, p0.Y.ToFloat() + (p1.Y.ToFloat() - p0.Y.ToFloat()) * u);
    }

    private static List<PelagSquallClip> Sweep(Run run, float scale)
    {
        var clips = new List<PelagSquallClip>();
        PelagSquallPose last = run.Line.Sample(run.Line.CastTick - 1, scale);
        clips.Add(last.Clip);
        for (float t = run.Line.CastTick - 1 + Step; t <= run.EndTick + 1; t += Step)
        {
            PelagSquallPose pose = run.Line.Sample(t, scale);
            Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(pose.Yaw - last.Yaw)), Is.LessThanOrEqualTo(30f), "взгляд без скачка, τ=" + t);
            float jump = (float)Math.Sqrt(Math.Pow(pose.ShiftX - last.ShiftX, 2) + Math.Pow(pose.ShiftY - last.ShiftY, 2));
            Assert.That(jump, Is.LessThanOrEqualTo(.25f), "сдвиг тела без скачка, τ=" + t);
            if (pose.Clip != last.Clip && !pose.Finished)
            {
                Assert.That(PelagSquallClipRules.IsSeam(last.Clip, pose.Clip) && !pose.Crossfade, Is.True, $"{last.Clip}->{pose.Clip} τ={t}");
                clips.Add(pose.Clip);
            }
            last = pose;
        }
        Assert.That(last.Finished, Is.True, "показ кончился к концу Шквала");
        Assert.That(PelagSquallClipRules.WrapDeg(last.Yaw - run.EndYaw), Is.EqualTo(0f).Within(1f), "в конце взгляд = взгляд Sim");
        return clips;
    }

    private static void CheckStrikes(Run run, float scale, bool lastHolds)
    {
        foreach (var (tick, index) in run.Strikes)
        {
            PelagSquallPose pose = run.Line.Sample(tick, scale);
            Assert.That(pose.Clip, Is.EqualTo(PelagSquallClipRules.StrikeClip((index & 1) == 1)), "удар #" + index);
            Assert.That(pose.Frame, Is.EqualTo(PelagSquallClipRules.ContactFrame).Within(1e-3f), "кадр 6 в тик удара #" + index);
            Assert.That(Math.Abs(pose.ShiftX) + Math.Abs(pose.ShiftY), Is.LessThan(1e-3f), "к удару тело в точке Sim");
            float yaw = run.Jumps.Find(j => j.index == index).yaw;
            Assert.That(PelagSquallClipRules.WrapDeg(pose.Yaw - yaw), Is.EqualTo(0f).Within(1f), "взгляд удара = прыжок #" + index);
            if (index == run.Strikes[run.Strikes.Count - 1].index && !lastHolds) continue;
            // Опора: левая лодыжка стоит, пока корень доворачивается к следующей цели.
            (float x, float y) start = (0f, 0f);
            for (float t = tick; t <= tick + PelagSquallClipRules.SupportTicks + 1e-3f; t += Step)
            {
                PelagSquallPose hold = run.Line.Sample(t, scale);
                (float px, float py) = Render(run, t);
                PelagSquallClipRules.LeftAnkle(hold.Yaw, scale, out float ax, out float ay);
                (float x, float y) ankle = (px + hold.ShiftX + ax, py + hold.ShiftY + ay);
                if (t == tick) start = ankle;
                Assert.That(Math.Abs(ankle.x - start.x) + Math.Abs(ankle.y - start.y), Is.LessThan(.01f), $"стопа стоит, удар #{index} τ={t}");
            }
        }
    }

    [Test]
    public void PlainCast_AlternatesSeamlessClips_ContactOnTheStrikeTick()
    {
        Simulation sim = Arena(PelagForm.None);
        int first = Enemy(sim, 2500, 0);
        Enemy(sim, 4200, 1500);
        Enemy(sim, 5800, -300);
        Enemy(sim, 7400, 1400);
        Enemy(sim, 9000, -200);
        Run run = Play(sim, first, 90);
        float scale = PelagSquallClipRules.TimingToWorld(1.82f);

        Assert.That(run.Strikes.Count, Is.GreaterThanOrEqualTo(3));
        Assert.That(run.EndReason, Is.EqualTo(SquallEnd.Done));
        List<PelagSquallClip> clips = Sweep(run, scale);
        Assert.That(clips[0], Is.EqualTo(PelagSquallClip.Load));
        for (int i = 0; i < run.Strikes.Count; i++)
            Assert.That(clips[1 + i], Is.EqualTo(PelagSquallClipRules.StrikeClip((i & 1) == 1)), "прыжок #" + i);
        bool lastBackhand = ((run.Strikes.Count - 1) & 1) == 1;
        Assert.That(clips[clips.Count - 1], Is.EqualTo(PelagSquallClipRules.FinishClip(lastBackhand)));
        Assert.That(clips.Count, Is.EqualTo(run.Strikes.Count + 2));
        CheckStrikes(run, scale, lastHolds: false);
    }

    [Test]
    public void Elusive_ReturnsWithTheReturnClip_AndLandsOnTheSimPoint()
    {
        Simulation sim = Arena(PelagForm.SquallElusive);
        int lone = Enemy(sim, 3000, 0);
        Run run = Play(sim, lone, 120);
        float scale = PelagSquallClipRules.TimingToWorld(1.82f);

        Assert.That(run.ReturnTick, Is.GreaterThan(0), "форма Неуловимый прыгает назад");
        Assert.That(run.EndReason, Is.EqualTo(SquallEnd.Done));
        List<PelagSquallClip> clips = Sweep(run, scale);
        bool lastBackhand = ((run.Strikes.Count - 1) & 1) == 1;
        Assert.That(clips[clips.Count - 1], Is.EqualTo(PelagSquallClipRules.ReturnClip(lastBackhand)));
        CheckStrikes(run, scale, lastHolds: true);
        int land = run.ReturnTick + run.ReturnTicks;
        PelagSquallPose pose = run.Line.Sample(land, scale);
        Assert.That(pose.Frame, Is.EqualTo(PelagSquallClipRules.ContactFrame).Within(1e-3f), "посадка — кадр 6");
        Assert.That(Math.Abs(pose.ShiftX) + Math.Abs(pose.ShiftY), Is.LessThan(1e-3f), "к посадке тело в точке Sim");
    }
}
