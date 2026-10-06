using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Абордаж v3 (владелец 03.10) на живой Sim: свои клипы форм и короткая тяга. Гейзер — Uppercut,
// Обвал — Slam: с тика B+P−1 (кадр 0 = Pull 11 / PullShort 4, стык без смешивания), контакт —
// кадр 1 ровно в тик удара Sim, кадр 4 = Recover 0. Пробоина и база — Punch. Тяга 4–5 тиков —
// PullShort (нырок — кадр 2 на целом тике). Форма известна со снимка раньше события удара.
// Лента собирается так же, как в CharacterAnimatorView.Abordage (PelagAbordageFeed).
public sealed class AbordageFormClipTests
{
    private const int Slot = 0;
    private const float Step = .25f;
    private static readonly float Scale = PelagSquallClipRules.TimingToWorld(1.82f);

    private sealed class Run
    {
        public readonly PelagAbordageFeed Feed = new PelagAbordageFeed();
        public readonly Dictionary<int, FixVec2> Position = new Dictionary<int, FixVec2>();
        public int CastTick = -1, BiteTick = -1, PunchTick = -1, EndTick = -1, PullTicks;
        public PelagForm PunchForm;
        public PelagAbordagePose BeforePunchEvent;
        public AbordageEnd EndReason;
        public PelagAbordageTimeline Line => Feed.Timeline;
    }

    private static Simulation Arena(PelagForm form)
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
        int count = PelagForms.AppendFormNodes(form, nodes, 0);
        sim.SetAbility(Slot, AbilityDefinition.AnchorLeap(), nodes, count);
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

    private static Run Play(PelagForm form, int distanceMm, bool shortPull = true, bool formClips = true)
    {
        Simulation sim = Arena(form);
        int target = Enemy(sim, distanceMm, 0);
        var run = new Run();
        run.Line.ShortPullAvailable = shortPull;
        run.Line.FormClipsAvailable = formClips;
        run.Position[sim.Tick - 1] = sim.Entities.Position[Simulation.PlayerId];
        for (int t = 0; t < 40 && run.EndTick < 0; t++)
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
            run.Position[tick] = sim.Entities.Position[Simulation.PlayerId];
            // События шага доходят до вида, когда показ ещё на тике раньше (тело рисуется с отставанием).
            float now = tick - .5f;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast && !run.Line.Active)
                {
                    Assert.That(run.Feed.Begin(sim), Is.True, "каст Абордажа виден в снимке");
                    run.Line.SetStartYaw(90f, now);
                    run.CastTick = tick;
                }
                if (e.Type == SimEventType.AbordageHook) { run.BiteTick = tick; run.PullTicks = e.Amount; }
                if (e.Type == SimEventType.AbordagePunch)
                {
                    // Поза за тик до удара — ДО события удара: форма уже со снимка.
                    run.BeforePunchEvent = run.Line.Sample(tick - 1, Scale);
                    run.PunchTick = tick;
                    run.PunchForm = (PelagForm)e.ActionVariant;
                }
                if (e.Type == SimEventType.AbordageEnded) { run.EndTick = tick; run.EndReason = (AbordageEnd)e.Amount; }
                run.Feed.Apply(sim, e, tick, now);
            }
            run.Feed.Track(sim, now);
        }
        Assert.That(run.EndReason, Is.EqualTo(AbordageEnd.Done), "Абордаж дошёл до конца");
        Assert.That(run.PunchTick, Is.EqualTo(run.BiteTick + run.PullTicks), "удар — в тик прибытия");
        return run;
    }

    /// <summary>Клипы по порядку; каждая смена — стык без смешивания, кадр внутри клипа не идёт назад, тело без скачков.</summary>
    private static List<PelagAbordageClip> Sweep(Run run)
    {
        var clips = new List<PelagAbordageClip>();
        PelagAbordagePose last = run.Line.Sample(run.CastTick, Scale);
        clips.Add(last.Clip);
        for (float t = run.CastTick + Step; t <= run.EndTick + 1; t += Step)
        {
            PelagAbordagePose pose = run.Line.Sample(t, Scale);
            if (pose.Finished) break;
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
        return clips;
    }

    [TestCase(PelagForm.AbordageGeyser, PelagAbordageClip.Uppercut, 2000)]
    [TestCase(PelagForm.AbordageGeyser, PelagAbordageClip.Uppercut, 5000)]
    [TestCase(PelagForm.AbordageGeyser, PelagAbordageClip.Uppercut, 7000)]
    [TestCase(PelagForm.AbordageQuake, PelagAbordageClip.Slam, 2000)]
    [TestCase(PelagForm.AbordageQuake, PelagAbordageClip.Slam, 5000)]
    [TestCase(PelagForm.AbordageQuake, PelagAbordageClip.Slam, 7000)]
    public void FormArrival_FromTheTickBeforeTheStrike_ContactFrameOnTheStrikeTick(PelagForm form, PelagAbordageClip arrival, int distanceMm)
    {
        Run run = Play(form, distanceMm);
        PelagAbordageTimeline line = run.Line;
        PelagAbordageClip pull = PelagAbordageClipRules.PullClip(run.PullTicks);
        Assert.That(run.PunchForm, Is.EqualTo(form));
        Assert.That(Sweep(run), Is.EqualTo(new[] { PelagAbordageClip.Throw, pull, arrival, PelagAbordageClip.Recover }));

        // Тик B+P−1: тяга отдаёт кадр 11 Pull / 4 PullShort — клип формы берёт с кадра 0 (та же поза).
        int start = run.PunchTick - PelagAbordageClipRules.FormLeadTicks;
        PelagAbordagePose last = line.Sample(start - .001f, Scale);
        Assert.That(last.Clip, Is.EqualTo(pull));
        Assert.That(last.Frame, Is.EqualTo(PelagAbordageClipRules.LastFrame(pull) - 1f).Within(.02f), "кадр перед формой");
        Assert.That(run.BeforePunchEvent.Clip, Is.EqualTo(arrival), "форма — со снимка, до события удара");
        Assert.That(run.BeforePunchEvent.Frame, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(run.BeforePunchEvent.Planted, Is.False, "кадр 0 — ещё в воздухе");

        // Контакт — кадр 1 в тик удара, тело ровно в точке Sim, левая плашмя.
        PelagAbordagePose contact = line.Sample(run.PunchTick, Scale);
        Assert.That(contact.Clip, Is.EqualTo(arrival));
        Assert.That(contact.Frame, Is.EqualTo(PelagAbordageClipRules.FormContactFrame).Within(1e-4f), "контакт — кадр 1 в тик удара");
        Assert.That(contact.Planted, Is.True);
        Assert.That(Math.Abs(contact.ShiftX) + Math.Abs(contact.ShiftY), Is.LessThan(1e-3f), "к удару тело в точке Sim");
        Assert.That(line.Sample(run.PunchTick - .001f, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.FormContactFrame).Within(.002f));

        // Кадр 4 = Recover 0 в B+P+3; показ кончается в тик конца Sim.
        Assert.That(line.Sample(run.PunchTick + PelagAbordageClipRules.HoldTicks - .001f, Scale).Frame,
            Is.EqualTo((float)PelagAbordageClipRules.LastFrame(arrival)).Within(.01f));
        PelagAbordagePose recover = line.Sample(run.PunchTick + PelagAbordageClipRules.HoldTicks, Scale);
        Assert.That(recover.Clip, Is.EqualTo(PelagAbordageClip.Recover));
        Assert.That(recover.Frame, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(run.EndTick, Is.EqualTo(run.PunchTick + PelagAbordageClipRules.HoldTicks + PelagAbordageClipRules.ExitTicks));
        Assert.That(line.Sample(run.EndTick, Scale).Finished, Is.True);
    }

    [TestCase(PelagForm.AbordageBreach, 5000)]
    [TestCase(PelagForm.None, 5000)]
    [TestCase(PelagForm.AbordageBreach, 7000)]
    public void BreachAndBase_KeepThePunch(PelagForm form, int distanceMm)
    {
        Run run = Play(form, distanceMm);
        PelagAbordageClip pull = PelagAbordageClipRules.PullClip(run.PullTicks);
        Assert.That(Sweep(run), Is.EqualTo(new[] { PelagAbordageClip.Throw, pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover }));
        Assert.That(run.Line.Sample(run.PunchTick - 1, Scale).Clip, Is.EqualTo(pull), "Punch — только с тика удара");
        PelagAbordagePose contact = run.Line.Sample(run.PunchTick, Scale);
        Assert.That(contact.Clip, Is.EqualTo(PelagAbordageClip.Punch));
        Assert.That(contact.Frame, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void ShortPull_FourToFiveTicks_DiveOnAWholeTick_ContactOnTheStrikeTick()
    {
        var seen = new HashSet<int>();
        foreach (int distanceMm in new[] { 3500, 4000, 4500, 5000, 5500, 6000 })
        {
            Run run = Play(PelagForm.None, distanceMm);
            int p = run.PullTicks;
            if (p < PelagAbordageClipRules.ShortPullMinTicks || p > PelagAbordageClipRules.ShortPullMaxTicks) continue;
            seen.Add(p);
            Assert.That(run.Line.PullClip, Is.EqualTo(PelagAbordageClip.PullShort), $"{distanceMm} мм, P={p}");
            Assert.That(run.Line.Sample(run.BiteTick, Scale).Clip, Is.EqualTo(PelagAbordageClip.PullShort));
            Assert.That(run.Line.Sample(run.BiteTick, Scale).Frame, Is.EqualTo(0f).Within(1e-4f), "натяг");
            PelagAbordagePose dive = run.Line.Sample(run.PunchTick - 3, Scale);
            Assert.That(dive.Clip, Is.EqualTo(PelagAbordageClip.PullShort));
            Assert.That(dive.Frame, Is.EqualTo(2f).Within(1e-4f), "нырок (поза 9) — тик B+P−3");
            Assert.That(dive.Planted, Is.False, "в нырке обе стопы в воздухе");
            Assert.That(run.Line.Sample(run.PunchTick - .001f, Scale).Frame,
                Is.EqualTo(PelagAbordageClipRules.ShortContactFrame).Within(.02f), "контакт — кадр 5 в тик удара");
            Assert.That(run.Line.Sample(run.PunchTick, Scale).Clip, Is.EqualTo(PelagAbordageClip.Punch));
        }
        Assert.That(seen, Does.Contain(4).And.Contain(5), "обе раскладки короткой тяги прошли на живой Sim");
    }

    [Test]
    public void TimelineOnly_NoPull_FormClipFromTheBite_AndTheStrikeEventDecidesTheForm()
    {
        // Тяги нет (P = 1, цель вплотную): клип формы с тика зацепа, контакт — кадр 1 в тик удара.
        // Стыка Throw → Uppercut нет (timing.json: «натяг → кадр 1 за тик») — вид смешивает за AbordageCancelBlend.
        var line = new PelagAbordageTimeline();
        line.Begin(1, 0, 3, 4, 5, 0f, PelagForm.AbordageGeyser);
        line.Hook(4, 1, 0f, 3.5f);
        Assert.That(line.ArrivalStartTick, Is.EqualTo(4));
        Assert.That(line.Sample(3.999f, Scale).Clip, Is.EqualTo(PelagAbordageClip.Throw));
        Assert.That(line.Sample(4f, Scale).Clip, Is.EqualTo(PelagAbordageClip.Uppercut));
        Assert.That(line.Sample(4.5f, Scale).Frame, Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(line.Sample(5f, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.FormContactFrame).Within(1e-4f));
        Assert.That(PelagAbordageClipRules.IsSeam(PelagAbordageClip.Throw, PelagAbordageClip.Uppercut), Is.False);

        // Форму сменили в мини-меню до удара — снимок; удар пришёл с другой — верит удару.
        line.ExpectForm(PelagForm.AbordageQuake);
        Assert.That(line.Sample(4.5f, Scale).Clip, Is.EqualTo(PelagAbordageClip.Slam));
        line.Punch(5, true, PelagForm.AbordageBreach, 0f, 4.5f);
        Assert.That(line.Sample(5f, Scale).Clip, Is.EqualTo(PelagAbordageClip.Punch));
        line.ExpectForm(PelagForm.AbordageGeyser);
        Assert.That(line.Form, Is.EqualTo(PelagForm.AbordageBreach), "после удара снимок форму не меняет");
    }

    [Test]
    public void ControllerWithoutV3Clips_FallsBackToPullAndPunch()
    {
        Run run = Play(PelagForm.AbordageGeyser, 5000, shortPull: false, formClips: false);
        Assert.That(PelagAbordageClipRules.PullClip(run.PullTicks), Is.EqualTo(PelagAbordageClip.PullShort), "раскладка — короткая");
        Assert.That(Sweep(run), Is.EqualTo(new[]
        {
            PelagAbordageClip.Throw, PelagAbordageClip.Pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover,
        }));
        Assert.That(run.Line.Sample(run.PunchTick - .001f, Scale).Frame, Is.EqualTo(PelagAbordageClipRules.ContactFrame).Within(.02f));
    }
}
