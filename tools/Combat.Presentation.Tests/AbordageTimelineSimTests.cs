using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Абордаж v2/v3 на живой Sim: лента вида собирается из снимка и событий ровно как в
// CharacterAnimatorView.Abordage (PelagAbordageFeed). Порядок клипов и стыки без смешивания,
// выпуск — кадр 3 в тик выпуска, натяг — кадр 9 в тик зацепа, контакт — кадр 12 Pull (тяга
// 4–5 тиков — кадр 5 PullShort) в тик удара (тело ровно в точке Sim), левая стопа стоит, пока
// корень доворачивает к цели, в конце — взгляд Sim; без цели — бросок назад к стойке; рывок и
// ходьба обрывают показ в свой тик. Формы — AbordageFormClipTests.
public sealed class AbordageTimelineSimTests
{
    private const int Slot = 0;
    private const float Step = .25f;
    private const float StartYaw = 90f;

    private sealed class Run
    {
        public readonly PelagAbordageFeed Feed = new PelagAbordageFeed();
        public readonly Dictionary<int, FixVec2> Position = new Dictionary<int, FixVec2>();
        public int CastTick = -1, ReleaseTick = -1, BiteTick = -1, PunchTick = -1, EndTick = -1, PullTicks;
        public AbordageEnd EndReason;
        public float EndYaw;
        public PelagAbordageTimeline Line => Feed.Timeline;
    }

    private static Simulation Arena(params AbilityNode[] extra)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
        int count = 0;
        foreach (AbilityNode node in extra) nodes[count++] = node;
        sim.SetAbility(Slot, AbilityDefinition.AnchorLeap(), nodes, count);
        sim.SetAbility(1, AbilityDefinition.Dash(), new AbilityNode[0], 0);
        sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(sim.Entities.MaxLavidium[Simulation.PlayerId]);
        return sim;
    }

    private static int Enemy(Simulation sim, int xMm, int yMm, int radiusMm = 850)
    {
        int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(xMm, 1000), Fix64.Ratio(yMm, 1000)), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = Fix64.Ratio(radiusMm, 1000);
        sim.Entities.NextAttackTick[id] = int.MaxValue;
        return id;
    }

    private static InputFrame Press(int target, int slot = Slot)
    {
        InputFrame input = InputFrame.Empty;
        input.AbilityMask = (byte)(1 << slot);
        input.AttackTarget = -1;
        input.AbilityTarget = target;
        return input;
    }

    private static float YawOf(FixVec2 v) => PelagSquallClipRules.YawOf(v.X.ToFloat(), v.Y.ToFloat());

    private static Run Play(Simulation sim, int target, int ticks, Func<int, InputFrame> later = null, Action<int> before = null)
    {
        var run = new Run();
        run.Position[sim.Tick - 1] = sim.Entities.Position[Simulation.PlayerId];
        for (int t = 0; t < ticks; t++)
        {
            before?.Invoke(t);
            int tick = sim.Tick;
            sim.Step(t == 0 ? Press(target) : later != null ? later(t) : InputFrame.Empty);
            run.Position[tick] = sim.Entities.Position[Simulation.PlayerId];
            // События шага доходят до вида, когда показ ещё на тике раньше (тело рисуется с отставанием).
            float now = tick - .5f;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim), Is.True, "каст Абордажа виден в снимке");
                    run.Line.SetStartYaw(StartYaw, now);
                    run.CastTick = tick;
                }
                if (e.Type == SimEventType.AbordageThrow) run.ReleaseTick = tick;
                if (e.Type == SimEventType.AbordageHook) { run.BiteTick = tick; run.PullTicks = e.Amount; }
                if (e.Type == SimEventType.AbordagePunch) run.PunchTick = tick;
                if (e.Type == SimEventType.AbordageEnded)
                {
                    run.EndTick = tick;
                    run.EndReason = (AbordageEnd)e.Amount;
                    run.EndYaw = YawOf(sim.Entities.Facing[Simulation.PlayerId]);
                }
                run.Feed.Apply(sim, e, tick, now);
            }
            run.Feed.Track(sim, now);
        }
        Assert.That(run.CastTick, Is.GreaterThanOrEqualTo(0), "каст был");
        Assert.That(run.EndTick, Is.GreaterThan(run.CastTick), "Абордаж кончился");
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

    private static List<PelagAbordageClip> Sweep(Run run, float scale, bool endYaw = true)
    {
        var clips = new List<PelagAbordageClip>();
        PelagAbordagePose last = run.Line.Sample(run.CastTick - 1, scale);
        clips.Add(last.Clip);
        for (float t = run.CastTick - 1 + Step; t <= run.EndTick + 1; t += Step)
        {
            PelagAbordagePose pose = run.Line.Sample(t, scale);
            if (pose.Finished) { last = pose; break; }
            Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(pose.Yaw - last.Yaw)), Is.LessThanOrEqualTo(30f), "взгляд без скачка, τ=" + t);
            float jump = (float)Math.Sqrt(Math.Pow(pose.ShiftX - last.ShiftX, 2) + Math.Pow(pose.ShiftY - last.ShiftY, 2));
            Assert.That(jump, Is.LessThanOrEqualTo(.25f), "сдвиг тела без скачка, τ=" + t);
            if (pose.Clip != last.Clip)
            {
                Assert.That(PelagAbordageClipRules.IsSeam(last.Clip, pose.Clip), Is.True, $"{last.Clip}->{pose.Clip} τ={t}");
                clips.Add(pose.Clip);
            }
            else if (!run.Line.Recalling)
                Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(last.Frame - 1e-4f), $"{pose.Clip} не идёт назад, τ={t}");
            last = pose;
        }
        Assert.That(last.Finished, Is.True, "показ кончился к концу Абордажа");
        if (endYaw)
            Assert.That(PelagSquallClipRules.WrapDeg(run.Line.Sample(run.EndTick, scale).Yaw - run.EndYaw), Is.EqualTo(0f).Within(1f),
                "в конце взгляд = взгляд Sim");
        return clips;
    }

    /// <summary>Левая лодыжка стоит в окне [from, to], пока корень доворачивает (и на входе — к цели).</summary>
    private static void AssertAnklePlanted(Run run, float scale, float from, float to, string what)
    {
        (float x, float y) start = (0f, 0f);
        for (float t = from; t <= to + 1e-3f; t += Step)
        {
            PelagAbordagePose pose = run.Line.Sample(t, scale);
            Assert.That(pose.Planted, Is.True, $"{what}: стопа стоит, τ={t}");
            (float px, float py) = Render(run, t);
            PelagSquallClipRules.LeftAnkle(pose.Yaw, scale, out float ax, out float ay);
            (float x, float y) ankle = (px + pose.ShiftX + ax, py + pose.ShiftY + ay);
            if (t == from) start = ankle;
            Assert.That(Math.Abs(ankle.x - start.x) + Math.Abs(ankle.y - start.y), Is.LessThan(.01f), $"{what}: стопа не едет, τ={t}");
        }
    }

    private static void AssertBaseCast(Run run, float scale)
    {
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.Done));
        List<PelagAbordageClip> clips = Sweep(run, scale);
        PelagAbordageClip pull = PelagAbordageClipRules.PullClip(run.PullTicks);
        Assert.That(run.Line.PullClip, Is.EqualTo(pull), "клип тяги по её тикам");
        Assert.That(clips, Is.EqualTo(new[]
        {
            PelagAbordageClip.Throw, pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover,
        }));

        PelagAbordagePose release = run.Line.Sample(run.ReleaseTick, scale);
        Assert.That(release.Clip, Is.EqualTo(PelagAbordageClip.Throw));
        Assert.That(release.Frame, Is.EqualTo(PelagAbordageClipRules.ReleaseFrame).Within(1e-3f), "выпуск — кадр 3 в тик выпуска");
        Assert.That(run.Line.Sample(run.ReleaseTick - .01f, scale).AnchorInHand, Is.True, "до выпуска якорь в правой");
        Assert.That(release.AnchorInHand, Is.False, "с выпуска якорь летит");

        PelagAbordagePose taut = run.Line.Sample(run.BiteTick - .001f, scale);
        Assert.That(taut.Clip, Is.EqualTo(PelagAbordageClip.Throw));
        Assert.That(taut.Frame, Is.EqualTo(PelagAbordageClipRules.BiteFrame).Within(.02f), "натяг — кадр 9 в тик зацепа");
        Assert.That(run.Line.Sample(run.BiteTick, scale).Clip, Is.EqualTo(pull));

        Assert.That(run.PunchTick, Is.EqualTo(run.BiteTick + run.PullTicks), "удар — в тик прибытия");
        PelagAbordagePose fist = run.Line.Sample(run.PunchTick - .001f, scale);
        Assert.That(fist.Clip, Is.EqualTo(pull));
        Assert.That(fist.Frame, Is.EqualTo((float)PelagAbordageClipRules.LastFrame(pull)).Within(.02f),
            "контакт — кадр 12 Pull / 5 PullShort в тик удара");
        PelagAbordagePose contact = run.Line.Sample(run.PunchTick, scale);
        Assert.That(contact.Clip, Is.EqualTo(PelagAbordageClip.Punch));
        Assert.That(contact.Frame, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(Math.Abs(contact.ShiftX) + Math.Abs(contact.ShiftY), Is.LessThan(1e-3f), "к удару тело в точке Sim");
        (float cx, float cy) = Render(run, run.PunchTick);
        FixVec2 landed = run.Position[run.PunchTick];
        Assert.That(Math.Abs(cx - landed.X.ToFloat()) + Math.Abs(cy - landed.Y.ToFloat()), Is.LessThan(1e-4f), "тело — в точке удара");

        Assert.That(run.Line.Sample(run.PunchTick + PelagAbordageClipRules.HoldTicks, scale).Clip, Is.EqualTo(PelagAbordageClip.Recover));
        Assert.That(run.EndTick, Is.EqualTo(run.PunchTick + PelagAbordageClipRules.HoldTicks + PelagAbordageClipRules.ExitTicks));
        Assert.That(run.Line.Sample(run.EndTick, scale).Finished, Is.True, "показ кончается в тик конца");
        Assert.That(run.Line.Sample(run.EndTick - .01f, scale).Finished, Is.False);

        // Бросок: стопа стоит, пока корень поворачивает с 90° на цель; после удара — тоже.
        AssertAnklePlanted(run, scale, run.CastTick, run.BiteTick, "бросок");
        AssertAnklePlanted(run, scale, run.PunchTick, run.EndTick - .01f, "удар и выход");
    }

    [TestCase(1500)]
    [TestCase(2000)]
    [TestCase(3500)]
    [TestCase(5000)]
    [TestCase(7000)]
    public void Cast_ThrowPullPunchRecover_ContactOnTheStrikeTick(int distanceMm)
    {
        Simulation sim = Arena();
        int target = Enemy(sim, distanceMm, 0);
        Run run = Play(sim, target, 40);
        AssertBaseCast(run, PelagSquallClipRules.TimingToWorld(1.82f));
        Assert.That(PelagSquallClipRules.WrapDeg(run.Line.Sample(run.ReleaseTick, 1f).Yaw), Is.EqualTo(0f).Within(1f),
            "к выпуску корень смотрит на цель");
    }

    [Test]
    public void LongChain_NineMetres_LongestLayout()
    {
        var talent = new AbilityNode[1];
        SabreTalents.AppendNode(SabreTalentLine.Boarding, 0, talent, 0);
        Simulation sim = Arena(talent[0]);
        int target = Enemy(sim, 9000, 0);
        Run run = Play(sim, target, 45);
        AssertBaseCast(run, PelagSquallClipRules.TimingToWorld(1.82f));
        Assert.That(run.PullTicks, Is.EqualTo(Simulation.AbordageMaxPullTicks), "раскладка кадр = тик");
        Assert.That(run.BiteTick - run.ReleaseTick, Is.EqualTo(Simulation.AbordageMaxHookTicks));
    }

    [Test]
    public void TargetGoneBeforeTheHook_ThrowRunsBackToStance()
    {
        Simulation sim = Arena();
        int target = Enemy(sim, 5000, 0);
        Run run = Play(sim, target, 20, before: t => { if (t == 3) sim.Entities.Alive[target] = false; });
        float scale = PelagSquallClipRules.TimingToWorld(1.82f);
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.NoTarget));
        Assert.That(run.Line.Recalling, Is.True, "возврат якоря виден по снимку");
        List<PelagAbordageClip> clips = Sweep(run, scale);
        Assert.That(clips, Is.EqualTo(new[] { PelagAbordageClip.Throw }));
        Assert.That(run.Line.Sample(run.EndTick - .01f, scale).Frame, Is.LessThan(.05f), "к концу возврата — стойка");
        Assert.That(run.Line.Sample(run.EndTick, scale).Finished, Is.True);
        AssertAnklePlanted(run, scale, run.CastTick, run.EndTick - .01f, "возврат");
    }

    [Test]
    public void TargetGoneBeforeTheHook_RetargetsNearby_ShowStaysContinuous()
    {
        Simulation sim = Arena();
        int first = Enemy(sim, 5000, 0);
        Enemy(sim, 5400, 1600, radiusMm: 450);
        Run run = Play(sim, first, 30, before: t => { if (t == 3) sim.Entities.Alive[first] = false; });
        // На 5 м якорь летит 3 тика; перевыбор (без события) отодвинул зацеп — лента перестроила полёт по снимку.
        Assert.That(run.BiteTick - run.ReleaseTick, Is.GreaterThan(3), "зацеп сдвинулся");
        AssertBaseCast(run, PelagSquallClipRules.TimingToWorld(1.82f));
    }

    [Test]
    public void DashInThePull_ShowEndsOnThatTick()
    {
        Simulation sim = Arena();
        int target = Enemy(sim, 5000, 0);
        Run run = Play(sim, target, 20, t =>
        {
            if (t != 7) return InputFrame.Empty;
            InputFrame dash = Press(-1, slot: 1);
            dash.Aim = sim.Entities.Position[Simulation.PlayerId] + new FixVec2(Fix64.Zero, Fix64.FromInt(3));
            return dash;
        });
        float scale = PelagSquallClipRules.TimingToWorld(1.82f);
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.Interrupted));
        List<PelagAbordageClip> clips = Sweep(run, scale, endYaw: false);
        Assert.That(clips, Is.EqualTo(new[] { PelagAbordageClip.Throw, PelagAbordageClipRules.PullClip(run.PullTicks) }));
        Assert.That(run.Line.Sample(run.EndTick, scale).Finished, Is.True, "рывок забирает тело в свой тик");
    }

    [Test]
    public void WalkingOutOfTheExit_EndsTheShowOnTheWalkTick()
    {
        Simulation sim = Arena();
        int target = Enemy(sim, 5000, 0);
        InputFrame walk = InputFrame.Empty;
        walk.Flags = (byte)InputFlags.MoveOrder;
        walk.Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(-5));
        walk.AttackTarget = walk.AbilityTarget = -1;
        Run run = Play(sim, target, 25, t => t > 10 ? walk : InputFrame.Empty);
        float scale = PelagSquallClipRules.TimingToWorld(1.82f);
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.WalkedOut));
        Assert.That(run.EndTick, Is.EqualTo(run.PunchTick + Simulation.AbordageHoldTicks + Simulation.AbordageExitLockedTicks));
        List<PelagAbordageClip> clips = Sweep(run, scale, endYaw: false);
        Assert.That(clips[clips.Count - 1], Is.EqualTo(PelagAbordageClip.Recover));
        Assert.That(run.Line.Sample(run.EndTick, scale).Finished, Is.True, "ходьба срывает выход в свой тик");
    }
}
