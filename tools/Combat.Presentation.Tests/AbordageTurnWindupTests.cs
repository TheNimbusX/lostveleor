using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Абордаж, цель за спиной (владелец 03.10: «да удлини, ничего страшного»): Sim даёт замах на тик
// дольше (v3: 4 вместо 3 — замах через плечо сам стал на тик длиннее), вид крутит корень той же
// S-кривой на все 4 тика и растягивает кадры 0→3 Throw на них же (throw_retime_turn: замах по прямой
// на тики замаха). Лента собирается из живой Sim так же, как в CharacterAnimatorView.Abordage
// (PelagAbordageFeed). Шаг взгляда — за кадр 60/30 к/с.
public sealed class AbordageTurnWindupTests
{
    private const int Slot = 0;
    private const float Scale = 1f;

    private sealed class Run
    {
        public readonly PelagAbordageFeed Feed = new PelagAbordageFeed();
        public int CastTick = -1, ReleaseTick = -1, BiteTick = -1, PunchTick = -1, EndTick = -1;
        public AbordageEnd EndReason;
        public float StartYaw, EndYaw;
        public PelagAbordageTimeline Line => Feed.Timeline;
    }

    private static Simulation Arena()
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.SetAbility(Slot, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);
        sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(sim.Entities.MaxLavidium[Simulation.PlayerId]);
        return sim;
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

    private static float YawOf(FixVec2 v) => PelagSquallClipRules.YawOf(v.X.ToFloat(), v.Y.ToFloat());

    /// <summary>Цель по +X в 5 м, герой смотрит под углом turnDeg от неё; показ начинает поворот с того же взгляда.</summary>
    private static Run Play(double turnDeg, int ticks = 25)
    {
        Simulation sim = Arena();
        int target = Enemy(sim, 5000, 0);
        double r = turnDeg * Math.PI / 180.0;
        sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(
            Fix64.Ratio((int)Math.Round(Math.Cos(r) * 10000), 10000), Fix64.Ratio((int)Math.Round(Math.Sin(r) * 10000), 10000));
        var run = new Run { StartYaw = YawOf(sim.Entities.Facing[Simulation.PlayerId]) };
        for (int t = 0; t < ticks; t++)
        {
            int tick = sim.Tick;
            InputFrame input = InputFrame.Empty;
            if (t == 0)
            {
                input.AbilityMask = (byte)(1 << Slot);
                input.AttackTarget = -1;
                input.AbilityTarget = target;
            }
            sim.Step(input);
            // События шага доходят до вида, когда показ ещё на тике раньше (тело рисуется с отставанием).
            float now = tick - .5f;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim), Is.True, "каст Абордажа виден в снимке");
                    run.Line.SetStartYaw(run.StartYaw, now);
                    run.CastTick = tick;
                }
                if (e.Type == SimEventType.AbordageThrow) run.ReleaseTick = tick;
                if (e.Type == SimEventType.AbordageHook) run.BiteTick = tick;
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
        Assert.That(run.CastTick, Is.EqualTo(0), "каст был");
        Assert.That(run.EndTick, Is.GreaterThan(run.PunchTick), "Абордаж кончился");
        return run;
    }

    /// <summary>Наибольший поворот корня за кадр длиной frameTicks тиков на [from, to], кадры со сдвигом phase.</summary>
    private static float MaxStep(PelagAbordageTimeline line, float from, float to, float frameTicks, float phase)
    {
        float max = 0f, last = line.Sample(from + phase, Scale).Yaw;
        for (float t = from + phase + frameTicks; t <= to + frameTicks + 1e-4f; t += frameTicks)
        {
            float yaw = line.Sample(t, Scale).Yaw;
            max = Math.Max(max, Math.Abs(PelagSquallClipRules.WrapDeg(yaw - last)));
            last = yaw;
        }
        return max;
    }

    /// <summary>Худший кадр по всем сдвигам кадров относительно тиков (кадр не обязан стоять на тике).</summary>
    private static float WorstStep(PelagAbordageTimeline line, float from, float to, float frameTicks)
    {
        float worst = 0f;
        for (float phase = 0f; phase < frameTicks - 1e-4f; phase += frameTicks / 16f)
            worst = Math.Max(worst, MaxStep(line, from - frameTicks, to, frameTicks, phase));
        return worst;
    }

    /// <summary>Тот же разворот при замахе windup тиков (лента без Sim) — для сравнения: 2 — принятый v2, 3 — замах v3 к цели впереди.</summary>
    private static PelagAbordageTimeline TurnOver(float startYaw, int windup)
    {
        var line = new PelagAbordageTimeline();
        line.Begin(1, 0, windup, windup + 3, windup + 8, 0f);
        line.SetStartYaw(startYaw, -.5f);
        return line;
    }

    private static float Smooth(float u) => PelagSquallClipRules.Smooth(u);

    [TestCase(91.0)]
    [TestCase(135.0)]
    [TestCase(180.0)]
    public void TargetBehind_FourTickWindup_TurnSpansAllFour_StepPerFrameDrops(double turnDeg)
    {
        Run run = Play(turnDeg);
        PelagAbordageTimeline line = run.Line;
        float turn = Math.Abs(PelagSquallClipRules.WrapDeg(0f - run.StartYaw));
        if (turn < 1f) turn = 180f;
        Assert.That(run.ReleaseTick - run.CastTick, Is.EqualTo(PelagAbordageClipRules.TurnWindupTicks), "выпуск на тик позже");
        Assert.That(line.WindupTicks, Is.EqualTo(Simulation.AbordageWindupTicks + Simulation.AbordageTurnWindupTicks));
        Assert.That(run.BiteTick - run.CastTick, Is.EqualTo(7), "зацеп 6 → 7");
        Assert.That(run.PunchTick, Is.EqualTo(12), "удар 11 → 12");
        Assert.That(run.EndTick, Is.EqualTo(21), "конец 20 → 21");
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.Done));

        // Поворот: с тика каста, ровно до выпуска, той же S-кривой.
        Assert.That(PelagSquallClipRules.WrapDeg(line.YawAt(run.CastTick) - run.StartYaw), Is.EqualTo(0f).Within(.01f), "с тика каста");
        Assert.That(PelagSquallClipRules.WrapDeg(line.YawAt(run.ReleaseTick)), Is.EqualTo(0f).Within(.5f), "к выпуску на цели");
        Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(line.YawAt(run.ReleaseTick - .5f))), Is.GreaterThan(1f), "поворот идёт все 4 тика");
        Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(line.YawAt(run.CastTick + 2f) - run.StartYaw)), Is.EqualTo(turn * .5f).Within(.5f),
            "середина S-кривой — на полпути замаха");

        // Кадры Throw: 0→3 растянуты на 4 тика (кадр 2 — через плечо — на 2,67 тика), выпуск — кадр 3 в тик выпуска, якорь в правой до него.
        Assert.That(line.Sample(run.CastTick + 2f, Scale).Frame, Is.EqualTo(1.5f).Within(1e-3f));
        Assert.That(line.Sample(run.CastTick + 8f / 3f, Scale).Frame, Is.EqualTo(2f).Within(1e-3f));
        Assert.That(line.Sample(run.ReleaseTick, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.ReleaseFrame).Within(1e-3f));
        Assert.That(line.Sample(run.BiteTick - .001f, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.BiteFrame).Within(.02f));
        Assert.That(line.Sample(run.ReleaseTick - .01f, Scale).AnchorInHand, Is.True, "до выпуска якорь в правой");
        Assert.That(line.Sample(run.ReleaseTick, Scale).AnchorInHand, Is.False);

        // Левая лодыжка стоит весь замах и полёт якоря (тело Sim стоит в нуле до тика после зацепа).
        float ax0 = 0f, ay0 = 0f;
        for (float t = run.CastTick; t <= run.BiteTick + 1e-3f; t += .125f)
        {
            PelagAbordagePose pose = line.Sample(t, Scale);
            Assert.That(pose.Planted, Is.True, "стопа стоит, τ=" + t);
            PelagSquallClipRules.LeftAnkle(pose.Yaw, Scale, out float ax, out float ay);
            ax += pose.ShiftX;
            ay += pose.ShiftY;
            if (t == run.CastTick) { ax0 = ax; ay0 = ay; }
            Assert.That(Math.Abs(ax - ax0) + Math.Abs(ay - ay0), Is.LessThan(.01f), "стопа не едет, τ=" + t);
        }

        // Шаг за кадр: 60 к/с — кадр полтика, 30 к/с — тик. S-кривая на 4 тика: на тиках 60 к/с
        // пик Δ·(S(5/8) − S(4/8)), в худшей фазе Δ·(S(9/16) − S(7/16)); 30 к/с — Δ·(S(3/4) − S(2/4)).
        float from = run.CastTick, to = run.ReleaseTick;
        float on60 = MaxStep(line, from, to, .5f, 0f), worst60 = WorstStep(line, from, to, .5f);
        float on30 = MaxStep(line, from, to, 1f, 0f), worst30 = WorstStep(line, from, to, 1f);
        Assert.That(on60, Is.EqualTo(turn * (Smooth(5f / 8f) - Smooth(.5f))).Within(.05f), "60 к/с, кадры на тиках");
        Assert.That(worst60, Is.LessThanOrEqualTo(turn * (Smooth(9f / 16f) - Smooth(7f / 16f)) + .05f), "60 к/с, худшая фаза");
        Assert.That(on30, Is.EqualTo(turn * (Smooth(3f / 4f) - Smooth(.5f))).Within(.05f), "30 к/с, кадры на тиках");

        // Против принятого v2 (замах 2) — вдвое мягче; против замаха v3 к цели впереди (3) — на четверть.
        PelagAbordageTimeline v2 = TurnOver(run.StartYaw, 2), ahead = TurnOver(run.StartYaw, PelagAbordageClipRules.WindupTicks);
        float old60 = MaxStep(v2, 0f, 2f, .5f, 0f), oldWorst60 = WorstStep(v2, 0f, 2f, .5f);
        float old30 = MaxStep(v2, 0f, 2f, 1f, 0f), oldWorst30 = WorstStep(v2, 0f, 2f, 1f);
        Assert.That(on60, Is.LessThan(old60 * .55f), "60 к/с: шаг почти вдвое меньше v2");
        Assert.That(worst60, Is.LessThan(oldWorst60 * .55f));
        Assert.That(worst30, Is.LessThan(oldWorst30 * .55f));
        float ahead60 = MaxStep(ahead, 0f, 3f, .5f, 0f), aheadWorst60 = WorstStep(ahead, 0f, 3f, .5f);
        Assert.That(on60, Is.LessThan(ahead60 * .8f), "60 к/с: мягче замаха в 3 тика");
        Assert.That(worst60, Is.LessThan(aheadWorst60 * .8f));
        TestContext.WriteLine($"turn {turn:F0}°: 60fps v2 {old60:F1} / W3 {ahead60:F1} → {on60:F1} (worst {oldWorst60:F1}/{aheadWorst60:F1}→{worst60:F1}); "
                              + $"30fps v2 {old30:F1}→{on30:F1} (worst {oldWorst30:F1}→{worst30:F1})");
    }

    [TestCase(0.0)]
    [TestCase(60.0)]
    [TestCase(89.0)]
    public void TargetAhead_WindupIsThreeTicks_TurnDoneByTheRelease(double turnDeg)
    {
        Run run = Play(turnDeg);
        Assert.That(run.ReleaseTick - run.CastTick, Is.EqualTo(PelagAbordageClipRules.WindupTicks));
        Assert.That(run.PunchTick, Is.EqualTo(11), "удар в тик 11 (v2 — 10: замах через плечо на тик дольше)");
        Assert.That(PelagSquallClipRules.WrapDeg(run.Line.YawAt(run.ReleaseTick)), Is.EqualTo(0f).Within(.5f), "к выпуску на цели");
        Assert.That(run.Line.Sample(run.CastTick + 1f, Scale).Frame, Is.EqualTo(1f).Within(1e-3f), "замах 0→3 за 3 тика");
        Assert.That(run.Line.Sample(run.CastTick + 2f, Scale).Frame, Is.EqualTo(2f).Within(1e-3f), "через плечо — тик 2");
        Assert.That(run.Line.Sample(run.ReleaseTick, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.ReleaseFrame).Within(1e-3f));
    }

    [Test]
    public void TargetBehind_FullCast_ClipsInOrder_NoJumps_EndsOnTheSimFacing()
    {
        Run run = Play(180.0, 30);
        PelagAbordageTimeline line = run.Line;
        var clips = new List<PelagAbordageClip>();
        PelagAbordagePose last = line.Sample(run.CastTick - 1, Scale);
        clips.Add(last.Clip);
        for (float t = run.CastTick - 1 + .25f; t <= run.EndTick + 1; t += .25f)
        {
            PelagAbordagePose pose = line.Sample(t, Scale);
            if (pose.Finished) { last = pose; break; }
            Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(pose.Yaw - last.Yaw)), Is.LessThanOrEqualTo(30f), "взгляд без скачка, τ=" + t);
            float jump = (float)Math.Sqrt(Math.Pow(pose.ShiftX - last.ShiftX, 2) + Math.Pow(pose.ShiftY - last.ShiftY, 2));
            Assert.That(jump, Is.LessThanOrEqualTo(.25f), "сдвиг тела без скачка, τ=" + t);
            if (pose.Clip != last.Clip)
            {
                Assert.That(PelagAbordageClipRules.IsSeam(last.Clip, pose.Clip), Is.True, $"{last.Clip}->{pose.Clip} τ={t}");
                clips.Add(pose.Clip);
            }
            else Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(last.Frame - 1e-4f), $"{pose.Clip} не идёт назад, τ={t}");
            last = pose;
        }
        Assert.That(last.Finished, Is.True, "показ кончился к концу Абордажа");
        PelagAbordageClip pull = PelagAbordageClipRules.PullClip(line.PullTicks);
        Assert.That(clips, Is.EqualTo(new[]
        {
            PelagAbordageClip.Throw, pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover,
        }));
        Assert.That(line.Sample(run.PunchTick - .001f, Scale).Frame, Is.EqualTo((float)PelagAbordageClipRules.LastFrame(pull)).Within(.02f),
            "контакт — последний кадр тяги (12 Pull / 5 PullShort) в тик удара");
        Assert.That(PelagSquallClipRules.WrapDeg(line.Sample(run.EndTick, Scale).Yaw - run.EndYaw), Is.EqualTo(0f).Within(1f),
            "в конце взгляд = взгляд Sim");
    }

    [Test]
    public void ClipRules_TurnWindupMatchesTheSim()
    {
        Assert.That(PelagAbordageClipRules.TurnWindupTicks,
            Is.EqualTo(Simulation.AbordageWindupTicks + Simulation.AbordageTurnWindupTicks));
        Assert.That(PelagAbordageClipRules.ThrowTurnTicks(PelagAbordageClipRules.WindupTicks), Is.EqualTo(3f));
        Assert.That(PelagAbordageClipRules.ThrowTurnTicks(PelagAbordageClipRules.TurnWindupTicks), Is.EqualTo(4f));
        // throw_retime_turn (замах 4): кадры 0, .75, 1.5, 2.25, 3, дальше полёт как при замахе 3.
        for (int hook = 1; hook <= 6; hook++)
        {
            Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 1f), Is.EqualTo(.75f).Within(1e-5f));
            Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 2f), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 3f), Is.EqualTo(2.25f).Within(1e-5f));
            Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 4f), Is.EqualTo(PelagAbordageClipRules.ReleaseFrame));
            Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 4f + hook), Is.EqualTo(PelagAbordageClipRules.BiteFrame));
            for (int j = 0; j <= hook; j++)
                Assert.That(PelagAbordageClipRules.ThrowFrame(4, hook, 4 + j),
                    Is.EqualTo(PelagAbordageClipRules.ThrowFrame(3, hook, 3 + j)).Within(1e-5f), "полёт якоря тот же, A=" + hook);
        }
    }
}
