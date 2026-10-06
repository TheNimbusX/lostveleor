using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение v4 на живой Sim: лента вида собирается из снимка WreckState и событий ровно как в
// CharacterAnimatorView.Wreck2 (PelagWreckFeed). Кадры контакта — в тики ударов Sim; снятие сжато в замах маха 1;
// быстрые нажатия (удар + 1) — «догон» хвоста прошлого клипа до стыка без смешивания; выпад в кадре 16 — стыком в Stow;
// пауза — поза маха и смешивание в новый клип; окно прошло — Stow со смешиванием; ходьба в окне отпускает ноги.
public sealed partial class WreckTimelineSimTests
{
    private const float Step = .25f;
    private const float Scale = 1.82f / 1.8f;

    private sealed class Run
    {
        public readonly PelagWreckFeed Feed = new PelagWreckFeed();
        public readonly Dictionary<int, FixVec2> Position = new Dictionary<int, FixVec2>();
        public readonly List<int> Contact = new List<int>();
        public readonly List<int> StageStart = new List<int>();
        public int CastTick = -1, EndTick = -1, ExitEnd = -1, ExitWalk = -1, ChargeStart = -1, Release = -1;
        public WreckEnd EndReason;
        public PelagWreckTimeline Line => Feed.Timeline;
    }

    private static Fix64 M(double v) => Fix64.Ratio((int)Math.Round(v * 1000), 1000);

    private static Simulation Arena(PelagForm form = PelagForm.None)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[Simulation.PlayerId] = FixVec2.Zero;
        sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(Fix64.One, Fix64.Zero);
        var nodes = new AbilityNode[4];
        int count = form == PelagForm.None ? 0 : PelagForms.AppendFormNodes(form, nodes, 0);
        sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, count);
        return sim;
    }

    private static InputFrame Input(bool press, bool hold = false, double aimX = 5, double aimY = 0, bool walk = false)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press || hold ? 1 : 0);
        input.Aim = new FixVec2(M(aimX), M(aimY));
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        if (walk) input.Flags = (byte)InputFlags.MoveOrder;
        return input;
    }

    /// <summary>Шаги Sim и показ как в виде: события шага приходят, когда показ ещё на тике раньше; взгляд этапа — показанный в прошлом кадре.</summary>
    private static Run Play(Simulation sim, int ticks, Func<int, InputFrame> input, float startYaw = 0f)
    {
        var run = new Run();
        float lastYaw = startYaw;
        run.Position[sim.Tick - 1] = sim.Entities.Position[Simulation.PlayerId];
        for (int i = 0; i < ticks; i++)
        {
            int tick = sim.Tick;
            sim.Step(input(i));
            run.Position[tick] = sim.Entities.Position[Simulation.PlayerId];
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim, true), Is.True, "серия видна в снимке");
                    run.CastTick = tick;
                }
                if (e.Type == SimEventType.WreckStage) run.Contact.Add(tick);
                if (e.Type == SimEventType.WreckChargeStarted) run.ChargeStart = tick;
                if (e.Type == SimEventType.WreckEnded) { run.EndTick = tick; run.EndReason = (WreckEnd)e.Amount; }
                run.Feed.Apply(sim, e, tick);
            }
            WreckState s = sim.Wreck;
            if (s.Phase != WreckPhase.None && (run.StageStart.Count == 0 || run.StageStart[run.StageStart.Count - 1] != s.StageStartTick))
                run.StageStart.Add(s.StageStartTick);
            if (s.ExitEndTick > 0) { run.ExitEnd = s.ExitEndTick; run.ExitWalk = s.ExitWalkTick; }
            if (s.ChargeStartTick >= 0 && s.Phase != WreckPhase.Charge && run.Release < 0) run.Release = s.ContactTick;
            run.Feed.Track(sim);
            for (float f = tick - 1.5f; f <= tick - 1f + 1e-3f; f += .5f)
            {
                if (!run.Line.Active) break;
                if (run.Line.NeedsStartYaw(f)) run.Line.SetStageStartYaw(lastYaw, f);
                lastYaw = run.Line.Sample(f, Scale).Yaw;
            }
        }
        Assert.That(run.CastTick, Is.GreaterThanOrEqualTo(0), "каст был");
        Assert.That(run.EndTick, Is.GreaterThan(run.CastTick), "серия кончилась");
        return run;
    }

    /// <summary>Показ от каста до конца уборки: смены клипов, кадр в клипе не идёт назад, взгляд без скачка, сдвига тела нет.</summary>
    private static List<PelagWreckClip> Sweep(Run run, out PelagWreckPose last)
    {
        var clips = new List<PelagWreckClip>();
        last = run.Line.Sample(run.CastTick, Scale);
        clips.Add(last.Clip);
        for (float t = run.CastTick + Step; t <= run.EndTick + PelagWreckClipRules.StowLast + 1; t += Step)
        {
            PelagWreckPose pose = run.Line.Sample(t, Scale);
            if (pose.Finished) { last = pose; break; }
            Assert.That(Math.Abs(PelagSquallClipRules.WrapDeg(pose.Yaw - last.Yaw)), Is.LessThanOrEqualTo(60f), "взгляд без скачка, τ=" + t);
            Assert.That(pose.ShiftX, Is.EqualTo(0f), "v4: тело без сдвига от точки Sim");
            Assert.That(pose.ShiftY, Is.EqualTo(0f));
            Assert.That(pose.Rate, Is.InRange(0f, 4f), "темп кадра в пределах");
            if (pose.Clip != last.Clip) clips.Add(pose.Clip);
            else Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(last.Frame - 1e-4f), $"{pose.Clip} не идёт назад, τ={t}");
            last = pose;
        }
        Assert.That(last.Finished, Is.True, "показ кончился уборкой");
        return clips;
    }

    [Test]
    public void FastestSeries_DrawSwingSwingLungeStow_ContactFramesOnStrikeTicks()
    {
        var sim = Arena();
        Run run = Play(sim, 57, i => Input(i < 20));
        Assert.That(run.EndReason, Is.EqualTo(WreckEnd.Done));
        Assert.That(run.Contact, Is.EqualTo(new[] { run.CastTick + 5, run.CastTick + 11, run.CastTick + 20 }), "удары 5 / 11 / 20 (Sim v4)");
        List<PelagWreckClip> clips = Sweep(run, out _);
        Assert.That(clips, Is.EqualTo(new[]
        {
            PelagWreckClip.Draw, PelagWreckClip.Swing1, PelagWreckClip.Swing2, PelagWreckClip.Lunge, PelagWreckClip.Stow,
        }));
        PelagWreckClip[] strike = { PelagWreckClip.Swing1, PelagWreckClip.Swing2, PelagWreckClip.Lunge };
        for (int k = 0; k < 3; k++)
        {
            PelagWreckPose hit = run.Line.Sample(run.Contact[k], Scale);
            Assert.That(hit.Clip, Is.EqualTo(strike[k]), "удар " + k);
            Assert.That(hit.Frame, Is.EqualTo(PelagWreckClipRules.ContactFrame(strike[k])).Within(1e-3f), "кадр контакта — в тик удара Sim");
        }
        // Быстрые нажатия (удар + 1): хвост прошлого до стыка 9 и замах нового — одним ходом, без смешивания.
        for (int k = 1; k < 3; k++)
        {
            Assert.That(run.StageStart[k], Is.EqualTo(run.Contact[k - 1] + 1), "нажатие из буфера — тиком после удара");
            PelagWreckPose at = run.Line.Sample(run.StageStart[k], Scale);
            Assert.That(at.Clip, Is.EqualTo(strike[k - 1]), "в тик нажатия ещё хвост прошлого клипа");
            Assert.That(at.Frame, Is.EqualTo(6f).Within(1e-3f), "удар + 1");
            Assert.That(at.NextStarted, Is.True, "риг: следующий уже нажат — голова не уходит в маятник");
            float seamTick = -1f;
            for (float t = run.StageStart[k]; t < run.Contact[k]; t += .01f)
                if (run.Line.Sample(t, Scale).Clip == strike[k]) { seamTick = t; break; }
            Assert.That(seamTick, Is.GreaterThan(run.StageStart[k]));
            PelagWreckPose seam = run.Line.Sample(seamTick, Scale);
            Assert.That(seam.EntryBlendTicks, Is.EqualTo(0f), "стык без смешивания");
            Assert.That(seam.Frame, Is.LessThan(.1f), "новый клип — с кадра 0 (= прошлый@9)");
        }
        // Выпад последнего этапа: в кадре 16 — Stow стыком, ещё до события конца (выход доигран в удар + 11).
        int stow = run.Contact[2] + PelagWreckClipRules.LungeSeam - PelagWreckClipRules.LungeContact;
        Assert.That(run.EndTick, Is.EqualTo(run.Contact[2] + 11));
        Assert.That(run.Line.Sample(stow - .01f, Scale).Clip, Is.EqualTo(PelagWreckClip.Lunge));
        PelagWreckPose s0 = run.Line.Sample(stow, Scale);
        Assert.That(s0.Clip, Is.EqualTo(PelagWreckClip.Stow));
        Assert.That(s0.EntryBlendTicks, Is.EqualTo(0f), "Stow@0 = Lunge@16");
        Assert.That(s0.LegsFree, Is.True);
        Assert.That(run.Line.Sample(stow + PelagWreckClipRules.StowLast, Scale).Finished, Is.True);
        Assert.That(run.Line.Sample(run.ExitWalk - .01f, Scale).LegsFree, Is.False, "первые тики выхода героя держат");
    }

    [Test]
    public void Draw_IsSqueezedIntoTheFirstWindup_SwingContactOnTheStrike()
    {
        var sim = Arena();
        Run run = Play(sim, 40, i => Input(i == 0));
        PelagWreckPose first = run.Line.Sample(run.CastTick, Scale);
        Assert.That(first.Clip, Is.EqualTo(PelagWreckClip.Draw));
        Assert.That(first.Frame, Is.EqualTo(PelagWreckClipRules.DrawEntryFrame).Within(1e-3f));
        Assert.That(first.EntryBlendTicks, Is.EqualTo(PelagWreckClipRules.CastBlendTicks));
        float seam = run.CastTick + PelagWreckClipRules.DrawShare * (run.Contact[0] - run.CastTick);
        Assert.That(run.Line.Sample(seam - .02f, Scale).Clip, Is.EqualTo(PelagWreckClip.Draw));
        PelagWreckPose swing = run.Line.Sample(seam + .02f, Scale);
        Assert.That(swing.Clip, Is.EqualTo(PelagWreckClip.Swing1));
        Assert.That(swing.EntryBlendTicks, Is.EqualTo(0f), "Draw@8 = Swing1@0");
        Assert.That(run.Line.Sample(run.Contact[0], Scale).Frame, Is.EqualTo(5f).Within(1e-3f));
        Assert.That(first.Rate, Is.EqualTo(12f / 5f).Within(.05f), "снятие и замах — 12 кадров за 5 тиков");
    }

    [Test]
    public void Lunge_SimStepsTheHeroSixTenths_TimelineKeepsTheBodyOnSim()
    {
        var sim = Arena();
        Run run = Play(sim, 57, i => Input(i < 20));
        int contact = run.Contact[2];
        float before = run.Position[contact - 4].X.ToFloat(), after = run.Position[contact].X.ToFloat();
        Assert.That(after - before, Is.EqualTo(.6f).Within(1e-3f), "шаг выпада Sim — 0,6 м в последние 4 тика замаха");
        Assert.That(run.Position[contact + 3].X.ToFloat(), Is.EqualTo(after).Within(1e-4f), "после удара стоит");
        for (float t = run.StageStart[2]; t <= contact + 4; t += Step)
        {
            PelagWreckPose pose = run.Line.Sample(t, Scale);
            Assert.That(pose.ShiftX, Is.EqualTo(0f));
            Assert.That(pose.ShiftY, Is.EqualTo(0f));
        }
    }
}
