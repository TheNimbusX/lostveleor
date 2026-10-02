using System;
using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи: действие Sim → клип и кадр (razlom/Assets/Game.View/ThicketMasterClipRules.cs,
/// контракт artifacts/tools/wf/boss-clip-spec.md). Главное — кадр контакта клипа ровно в тик
/// удара: на синтетических действиях (замах 24 и 30, стойки фаз, двойная лапа) и на живой
/// симуляции босса — в тик каждого EnemyActionImpact поза даёт кадр контакта контракта, тело
/// прячется ровно на время ThicketUnderground, кадры одного шага действия не идут назад.
/// </summary>
public sealed class ThicketMasterClipRulesTests
{
    private const int Boss = 1;

    /// <summary>Сколько вторых (левых) лап двойной проверено в живой сцене.</summary>
    private static int LeftPaws;

    // ------------------------------------------------------------ контракт

    [Test]
    public void ClipTable_MatchesTheContract()
    {
        var frames = new Dictionary<ThicketClip, int>
        {
            [ThicketClip.Idle] = 90, [ThicketClip.Sleep] = 90, [ThicketClip.Wake] = 30, [ThicketClip.Walk] = 0,
            [ThicketClip.TurnL] = 30, [ThicketClip.TurnR] = 30, [ThicketClip.PawR] = 49, [ThicketClip.PawL] = 49,
            [ThicketClip.Stomp] = 71, [ThicketClip.Roar] = 60, [ThicketClip.DiveIn] = 12, [ThicketClip.Emerge] = 36,
            [ThicketClip.SproutCast] = 90, [ThicketClip.PollenShake] = 48, [ThicketClip.BerryVolley] = 120,
            [ThicketClip.Storm] = 159, [ThicketClip.Death] = 90,
        };
        Assert.That(ThicketMasterClipRules.All.Length, Is.EqualTo(17));
        Assert.That(ThicketMasterClipRules.All.Distinct().Count(), Is.EqualTo(17));
        foreach (var clip in ThicketMasterClipRules.All)
        {
            Assert.That(ThicketMasterClipRules.Frames(clip), Is.EqualTo(frames[clip]), clip.ToString());
            Assert.That(ThicketMasterClipRules.Name(clip), Is.EqualTo(clip.ToString()));
            Assert.That(ThicketMasterClipRules.Take(clip), Is.EqualTo("ThicketMaster_" + clip));
            Assert.That(ThicketMasterClipRules.PhaseParameter(clip), Is.EqualTo(clip + "Phase"));
        }
        var loops = ThicketMasterClipRules.All.Where(ThicketMasterClipRules.Loops).ToArray();
        Assert.That(loops, Is.EquivalentTo(new[] { ThicketClip.Idle, ThicketClip.Sleep, ThicketClip.Walk }));
        Assert.That(ThicketMasterClipRules.FramesPerSecond, Is.EqualTo(Simulation.TicksPerSecond));
    }

    [Test]
    public void Phase_HoldsTheLastFrameOfOneShots_AndWrapsLoops()
    {
        Assert.That(ThicketMasterClipRules.PhaseOf(ThicketClip.PawR, 49f), Is.EqualTo(1f));
        Assert.That(ThicketMasterClipRules.PhaseOf(ThicketClip.PawR, 60f), Is.EqualTo(1f));
        Assert.That(ThicketMasterClipRules.PhaseOf(ThicketClip.PawR, 24f), Is.EqualTo(24f / 49f).Within(1e-6));
        Assert.That(ThicketMasterClipRules.PhaseOf(ThicketClip.Idle, 135f), Is.EqualTo(.5f).Within(1e-6));
        Assert.That(ThicketMasterClipRules.PawIsRight(0), Is.True);
        Assert.That(ThicketMasterClipRules.PawIsRight(1), Is.False);
        Assert.That(ThicketMasterClipRules.PawClip(1), Is.EqualTo(ThicketClip.PawL));
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketPaw), Is.True);
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketStorm), Is.True);
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.WendigoSweep), Is.False);
    }

    // ------------------------------------------------------------ синтетика

    private static ThicketMasterState State(ThicketMasterAction action, int start, int impact, int last, int end,
        int stage = 0, int stages = 1, bool resolved = false)
        => new ThicketMasterState
        {
            Serial = 1, Action = action, StartTick = start, StageStartTick = start, ImpactTick = impact,
            LastImpactTick = last, EndTick = end, Stage = stage, Stages = stages, HitResolved = resolved,
        };

    private static float Frame(in ThicketMasterState a, float tick, ThicketClip clip)
    {
        var pose = ThicketMasterClipRules.Action(a, tick);
        Assert.That(pose.Clip, Is.EqualTo(clip), $"{a.Action} на тике {tick}");
        return pose.Frame;
    }

    [Test]
    public void Paw_ContactOnImpact_ForNormalAndHeavyWindup_AndRecoveryOfEveryPhase()
    {
        // Фаза 1: 24 / 1 / 24.
        var paw = State(ThicketMasterAction.Paw, 100, 124, 124, 149);
        Assert.That(Frame(paw, 100f, ThicketClip.PawR), Is.EqualTo(0f));
        Assert.That(Frame(paw, 112f, ThicketClip.PawR), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(paw, 124f, ThicketClip.PawR), Is.EqualTo(24f));
        Assert.That(Frame(paw, 149f, ThicketClip.PawR), Is.EqualTo(49f));
        Assert.That(Frame(paw, 160f, ThicketClip.PawR), Is.EqualTo(49f));
        // «Сложно» и ярость: замах 30 — контакт всё равно на тике удара.
        var heavy = State(ThicketMasterAction.Paw, 100, 130, 130, 155);
        Assert.That(Frame(heavy, 115f, ThicketClip.PawR), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(heavy, 130f, ThicketClip.PawR), Is.EqualTo(24f));
        // Фаза 3: стойка 12 тиков — отход 24–49 сжат в неё.
        var short3 = State(ThicketMasterAction.Paw, 100, 124, 124, 137);
        Assert.That(Frame(short3, 137f, ThicketClip.PawR), Is.EqualTo(49f));
    }

    [Test]
    public void DoublePaw_RightThenLeft_FirstOnlyOneRecoveryFrame()
    {
        var first = State(ThicketMasterAction.Paw, 100, 124, 149, 162, stage: 0, stages: 2, resolved: false);
        Assert.That(Frame(first, 124f, ThicketClip.PawR), Is.EqualTo(24f));
        Assert.That(Frame(first, 124.5f, ThicketClip.PawR), Is.EqualTo(24.5f).Within(1e-4));
        var second = first;
        second.Stage = 1; second.StageStartTick = 125; second.ImpactTick = 149; second.HitResolved = false;
        Assert.That(Frame(second, 125f, ThicketClip.PawL), Is.EqualTo(0f));
        Assert.That(Frame(second, 149f, ThicketClip.PawL), Is.EqualTo(24f));
        Assert.That(Frame(second, 162f, ThicketClip.PawL), Is.EqualTo(49f));
    }

    [Test]
    public void Stomp_Roar_Pollen_Wake_KeyFrames()
    {
        var stomp = State(ThicketMasterAction.Stomp, 0, 33, 33, 71);
        Assert.That(Frame(stomp, 33f, ThicketClip.Stomp), Is.EqualTo(33f));
        Assert.That(Frame(stomp, 35f, ThicketClip.Stomp), Is.EqualTo(35f));
        Assert.That(Frame(stomp, 71f, ThicketClip.Stomp), Is.EqualTo(71f));
        var stomp3 = State(ThicketMasterAction.Stomp, 0, 33, 33, 53); // фаза 3: стойка 36 × 50%
        Assert.That(Frame(stomp3, 53f, ThicketClip.Stomp), Is.EqualTo(71f));
        Assert.That(Frame(stomp3, 44f, ThicketClip.Stomp), Is.EqualTo(53f).Within(1e-4));

        var roar = State(ThicketMasterAction.Roar, 10, 55, 55, 70);
        Assert.That(Frame(roar, 25f, ThicketClip.Roar), Is.EqualTo(15f).Within(1e-4));
        Assert.That(Frame(roar, 55f, ThicketClip.Roar), Is.EqualTo(45f));
        Assert.That(Frame(roar, 70f, ThicketClip.Roar), Is.EqualTo(60f));

        var pollen = State(ThicketMasterAction.Pollen, 0, 24, 24, 48);
        Assert.That(Frame(pollen, 24f, ThicketClip.PollenShake), Is.EqualTo(24f));
        Assert.That(Frame(pollen, 48f, ThicketClip.PollenShake), Is.EqualTo(48f));

        var wake = State(ThicketMasterAction.Wake, 90, 120, 120, 120, resolved: true);
        Assert.That(Frame(wake, 110f, ThicketClip.Wake), Is.EqualTo(20f).Within(1e-4));
        Assert.That(Frame(wake, 120f, ThicketClip.Wake), Is.EqualTo(30f));
    }

    [Test]
    public void Dive_BurrowsAfterDiveIn_AndEmergesOnTheImpactTick()
    {
        int impact = 100 + Simulation.ThicketDiveBurrowTicks + Simulation.ThicketDiveTravelTicks + Simulation.ThicketDiveLockTicks;
        var dive = State(ThicketMasterAction.Dive, 100, impact, impact, impact + 36);
        Assert.That(ThicketMasterClipRules.BurrowEndTick(dive), Is.EqualTo(112));
        Assert.That(Frame(dive, 106f, ThicketClip.DiveIn), Is.EqualTo(6f).Within(1e-4));
        Assert.That(ThicketMasterClipRules.Action(dive, 106f).Burrowed, Is.False);
        dive.Stage = 1; dive.StageStartTick = 112;
        var under = ThicketMasterClipRules.Action(dive, 112f);
        Assert.That(under.Burrowed, Is.True);
        Assert.That(under.Frame, Is.EqualTo(12f));
        dive.Stage = 3; dive.StageStartTick = impact; dive.HitResolved = true;
        var up = ThicketMasterClipRules.Action(dive, impact);
        Assert.That(up.Clip, Is.EqualTo(ThicketClip.Emerge));
        Assert.That(up.Frame, Is.EqualTo(0f));
        Assert.That(up.Burrowed, Is.False);
        Assert.That(Frame(dive, impact + 36, ThicketClip.Emerge), Is.EqualTo(36f));
    }

    [Test]
    public void Casts_KeyFramesFollowTheSimSchedule()
    {
        // Ливень (фаза 3): удары залпов 30/45/60, стойка 60 × 50%.
        var rain = State(ThicketMasterAction.Rain, 0, 30, 60, 90, stage: 3, stages: 3);
        Assert.That(Frame(rain, 30f, ThicketClip.BerryVolley), Is.EqualTo(30f));
        Assert.That(Frame(rain, 45f, ThicketClip.BerryVolley), Is.EqualTo(45f));
        Assert.That(Frame(rain, 60f, ThicketClip.BerryVolley), Is.EqualTo(60f));
        Assert.That(Frame(rain, 90f, ThicketClip.BerryVolley), Is.EqualTo(120f));
        // Буря: волны 75 и 135, закрытие кроны в стойку 12.
        var storm = State(ThicketMasterAction.Storm, 0, 75, 135, 147, stages: 2);
        Assert.That(Frame(storm, 12f, ThicketClip.Storm), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(storm, 75f, ThicketClip.Storm), Is.EqualTo(75f));
        Assert.That(Frame(storm, 135f, ThicketClip.Storm), Is.EqualTo(135f));
        Assert.That(Frame(storm, 147f, ThicketClip.Storm), Is.EqualTo(159f));
        // Прорастание фазы 1 (стенд): 1 к 1 — лапы в земле к 8, вырывает 80–90.
        var sprout = State(ThicketMasterAction.Sprout, 0, 30, 75, 90, stage: 6, stages: 6);
        Assert.That(Frame(sprout, 8f, ThicketClip.SproutCast), Is.EqualTo(8f));
        Assert.That(Frame(sprout, 80f, ThicketClip.SproutCast), Is.EqualTo(80f));
        Assert.That(Frame(sprout, 90f, ThicketClip.SproutCast), Is.EqualTo(90f));
        // Фаза 3: стойка после последнего круга 7 тиков — вырывание всё равно в конце.
        var sprout3 = State(ThicketMasterAction.Sprout, 0, 30, 75, 82, stage: 6, stages: 6);
        Assert.That(Frame(sprout3, 82f, ThicketClip.SproutCast), Is.EqualTo(90f));
        Assert.That(Frame(sprout3, 72f, ThicketClip.SproutCast), Is.EqualTo(80f));
    }

    [Test]
    public void Death_TurnAndWalk_Helpers()
    {
        float hold = .075f, fall = 65f / 30f, rest = 25f / 30f;
        Assert.That(ThicketMasterClipRules.DeathFrame(0f, hold, fall, rest), Is.EqualTo(0f));
        Assert.That(ThicketMasterClipRules.DeathFrame(hold, hold, fall, rest), Is.EqualTo(0f));
        Assert.That(ThicketMasterClipRules.DeathFrame(fall, hold, fall, rest), Is.EqualTo(65f).Within(1e-3));
        Assert.That(ThicketMasterClipRules.DeathFrame(fall + rest, hold, fall, rest), Is.EqualTo(90f).Within(1e-3));
        Assert.That(ThicketMasterClipRules.DeathFrame(60f, hold, fall, rest), Is.EqualTo(90f), "последний кадр держится");

        Assert.That(ThicketMasterClipRules.TurnPhase(0f), Is.EqualTo(0f));
        Assert.That(ThicketMasterClipRules.TurnPhase(45f), Is.EqualTo(.5f).Within(1e-5));
        Assert.That(ThicketMasterClipRules.TurnPhase(90f), Is.EqualTo(1f), "ровно 90° — конец клипа");
        Assert.That(ThicketMasterClipRules.TurnPhase(-135f), Is.EqualTo(.5f).Within(1e-5));

        // 2,6 м/с Sim за секунду при шаге 1,7857 м (замер после правки 02.10) — 1,456 цикла:
        // клип 30 кадров (цикл 1 с) играет ×1,456.
        float speed = Simulation.ThicketMasterMoveSpeed.ToFloat();
        Assert.That(ThicketMasterClipRules.DefaultWalkStride, Is.EqualTo(1.7857f).Within(.001f));
        Assert.That(ThicketMasterClipRules.WalkCycles(speed, ThicketMasterClipRules.DefaultWalkStride),
            Is.EqualTo(speed / ThicketMasterClipRules.DefaultWalkStride).Within(1e-5));
        Assert.That(ThicketMasterClipRules.WalkCycles(speed, ThicketMasterClipRules.DefaultWalkStride) * 30f / 30f,
            Is.EqualTo(1.456f).Within(.01f), "скорость клипа Walk против Sim — как в export.json пакета");
    }

    // ------------------------------------------------------------ Песочные Часы

    /// <summary>Сдвиг Часов так, как его делает Sim: замах и стойка всегда, удар — только впереди.</summary>
    private static ThicketMasterState Hourglass(ThicketMasterState a, int simTick)
    {
        const int shift = Simulation.ThicketHourglassShiftTicks;
        a.StageStartTick += shift;
        if (a.ImpactTick >= simTick) a.ImpactTick += shift;
        if (a.LastImpactTick >= simTick) a.LastImpactTick += shift;
        a.EndTick += shift;
        return a;
    }

    [Test]
    public void HourglassTrack_RecoveryContinuesFromTheSameFrame()
    {
        const int shift = Simulation.ThicketHourglassShiftTicks;
        // Топот фазы 1, Часы на 6-м тике стойки (прошедший контакт Sim не сдвигает).
        var stomp = State(ThicketMasterAction.Stomp, 0, 33, 33, 71);
        var track = new ThicketHourglassTrack();
        float before = ThicketMasterClipRules.Action(track.Apply(stomp), 39f).Frame;
        var shifted = Hourglass(stomp, 40);
        float raw = ThicketMasterClipRules.Action(shifted, 39f + shift).Frame;
        float after = ThicketMasterClipRules.Action(track.Apply(shifted), 39f + shift).Frame;
        Assert.That(raw - before, Is.GreaterThan(10f), "без трекера стойка прыгает вперёд — трекер нужен");
        Assert.That(after, Is.EqualTo(before).Within(1e-3), "с трекером — тот же кадр");
        Assert.That(ThicketMasterClipRules.Action(track.Apply(shifted), 71f + shift).Frame, Is.EqualTo(71f), "стойка доигрывает к концу");

        // Ливень фазы 3 — стойка от последнего залпа (LastImpactTick), Часы через тик после него.
        var rain = State(ThicketMasterAction.Rain, 0, 30, 60, 90, stage: 3, stages: 3);
        track = new ThicketHourglassTrack();
        before = ThicketMasterClipRules.Action(track.Apply(rain), 61f).Frame;
        after = ThicketMasterClipRules.Action(track.Apply(Hourglass(rain, 62)), 61f + shift).Frame;
        Assert.That(after, Is.EqualTo(before).Within(1e-3));

        // Посреди замаха Sim сдвигает и удар: трекеру добавлять нечего.
        var paw = State(ThicketMasterAction.Paw, 100, 124, 124, 149);
        track = new ThicketHourglassTrack();
        before = ThicketMasterClipRules.Action(track.Apply(paw), 110f).Frame;
        var pawShifted = Hourglass(paw, 111);
        Assert.That(track.Apply(pawShifted).ImpactTick, Is.EqualTo(124 + shift));
        Assert.That(ThicketMasterClipRules.Action(track.Apply(pawShifted), 110f + shift).Frame, Is.EqualTo(before).Within(1e-3));
        Assert.That(ThicketMasterClipRules.Action(track.Apply(pawShifted), 124f + shift).Frame, Is.EqualTo(24f), "контакт — на сдвинутом ударе");

        // Новое действие сдвиг не наследует.
        var next = State(ThicketMasterAction.Stomp, 300, 333, 333, 371);
        next.Serial = 2;
        Assert.That(track.Apply(next).ImpactTick, Is.EqualTo(333));
    }

    [Test]
    public void HourglassTrack_NewStageKeepsOnlyTheSharedLastImpactShift()
    {
        const int shift = Simulation.ThicketHourglassShiftTicks;
        // Двойная лапа: Часы в стойке первой — вторая лапа приходит со своим ударом, его не трогаем.
        var first = State(ThicketMasterAction.Paw, 100, 124, 149, 162, stage: 0, stages: 2, resolved: true);
        var track = new ThicketHourglassTrack();
        track.Apply(first);
        var shifted = Hourglass(first, 125);
        Assert.That(track.Apply(shifted).ImpactTick, Is.EqualTo(124 + shift), "прошедший удар первой догнал Часы");
        var second = shifted;
        second.Stage = 1; second.StageStartTick = 125 + shift; second.ImpactTick = 149 + shift; second.HitResolved = false;
        var shown = track.Apply(second);
        Assert.That(shown.ImpactTick, Is.EqualTo(149 + shift), "удар второй лапы — как в Sim");
        Assert.That(shown.LastImpactTick, Is.EqualTo(second.LastImpactTick));
    }

    [Test]
    public void BodyFacing_SimFacing_AndCommittedDuringActions()
    {
        Assert.That(EnemyBodyFacingRules.PolicyOf(EnemyKind.ForestThicketMaster), Is.EqualTo(EnemyBodyPolicy.SimFacing));
        var sim = Arena();
        HeroInFront(sim, 4);
        bool sawCommitted = false;
        while (sim.Tick < 200)
        {
            Step(sim);
            bool acting = sim.TryGetThicketMasterAction(Boss, out _);
            Assert.That(EnemyBodyFacingRules.IsCommitted(sim, Boss), Is.EqualTo(acting), $"тик {sim.Tick}");
            sawCommitted |= acting;
        }
        Assert.That(sawCommitted, Is.True, "вступление (пробуждение и рёв) — действие");
    }

    // ------------------------------------------------------------ живая симуляция

    private static Simulation Arena()
    {
        var sim = new Simulation(77, 64);
        sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
        var e = sim.Entities;
        e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
        e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
        e.RefreshStats(0);
        e.Health[0] = e.MaxHealth[0];
        return sim;
    }

    private static void HeroInFront(Simulation sim, double distance)
    {
        var boss = sim.Entities.Position[Boss];
        sim.Entities.Position[0] = boss + new FixVec2(-Fix64.FromDouble(distance), Fix64.Zero);
    }

    private static void Step(Simulation sim)
    {
        sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
        sim.Step(InputFrame.Empty);
    }

    /// <summary>
    /// Шагает до until и сверяет каждый тик: удар — кадр контакта, тело спрятано ровно под
    /// землёй, кадры одного шага действия не идут назад. Возвращает виды проверенных ударов.
    /// </summary>
    private static void Drive(Simulation sim, int until, Dictionary<EnemyActionKind, int> checkedImpacts,
        Action<Simulation> perTick = null)
    {
        int serial = 0, stage = -1;
        ThicketClip clip = ThicketClip.Idle;
        float lastFrame = -1f;
        while (sim.Tick < until)
        {
            perTick?.Invoke(sim);
            Step(sim);
            float tick = sim.Tick - 1; // кадр отрисовки с Alpha = 0 — ровно тик шага
            bool acting = sim.TryGetThicketMasterAction(Boss, out var a);
            if (acting)
            {
                var pose = ThicketMasterClipRules.Action(a, tick);
                Assert.That(pose.Burrowed, Is.EqualTo(sim.ThicketUnderground(Boss)),
                    $"тело спрятано ровно под землёй: тик {tick}, шаг {a.Stage}");
                if (a.Serial == serial && a.Stage == stage && pose.Clip == clip)
                    Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(lastFrame - 1e-4f),
                        $"{a.Action}: кадр назад на тике {tick} ({lastFrame} → {pose.Frame})");
                serial = a.Serial; stage = a.Stage; clip = pose.Clip; lastFrame = pose.Frame;
                Assert.That(pose.Frame, Is.InRange(0f, Math.Max(1, ThicketMasterClipRules.Frames(pose.Clip))));
            }
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyActionImpact || e.Source != Boss) continue;
                var kind = (EnemyActionKind)e.ActionVariant;
                if (!ThicketMasterClipRules.TryContact(kind, e.Amount, out var contactClip, out float contactFrame)) continue;
                Assert.That(acting, Is.True, $"{kind}: в тик удара действие ещё идёт");
                var pose = ThicketMasterClipRules.Action(a, tick);
                Assert.That(pose.Clip, Is.EqualTo(contactClip), $"{kind} #{e.Amount} на тике {tick}");
                Assert.That(pose.Frame, Is.EqualTo(contactFrame).Within(1e-3), $"{kind} #{e.Amount}: кадр контакта на тике {tick}");
                checkedImpacts.TryGetValue(kind, out int n);
                checkedImpacts[kind] = n + 1;
                if (kind == EnemyActionKind.ThicketPaw && e.Amount == 1) LeftPaws++;
            }
        }
    }

    [Test]
    public void LiveBoss_EveryImpactLandsOnTheContactFrame()
    {
        var impacts = new Dictionary<EnemyActionKind, int>();
        LeftPaws = 0;

        // Фаза 1: вступление (рёв), лапа и топот вплотную, нырок по дальнему герою.
        var near = Arena();
        HeroInFront(near, 2.6);
        Drive(near, 900, impacts, sim =>
        {
            if (sim.Tick == 600) HeroInFront(sim, 8.5);
        });

        // Фаза 3: рёв на пороге → буря, касты, двойная лапа и связки.
        var late = Arena();
        HeroInFront(late, 3.0);
        Drive(late, 181, impacts);
        late.Entities.Health[Boss] = late.Entities.MaxHealth[Boss] * 30 / 100;
        Drive(late, 2400, impacts, sim =>
        {
            // Герой ходит вокруг: то вплотную (лапа, топот), то дальше (касты, нырок).
            int t = sim.Tick % 360;
            if (t == 0) HeroInFront(sim, 2.6);
            else if (t == 180) HeroInFront(sim, 6.0);
        });

        TestContext.WriteLine(string.Join(", ", impacts.Select(p => p.Key + "×" + p.Value)) + ", левых лап " + LeftPaws);
        foreach (var kind in new[] { EnemyActionKind.ThicketPaw, EnemyActionKind.ThicketStomp, EnemyActionKind.ThicketRoar,
                     EnemyActionKind.ThicketDive, EnemyActionKind.ThicketStorm })
            Assert.That(impacts.ContainsKey(kind), Is.True, kind + " ни разу не ударил — сцена не проверила его кадр");
        Assert.That(impacts.Keys.Any(k => k == EnemyActionKind.ThicketRain || k == EnemyActionKind.ThicketPollen),
            Is.True, "ни одного каста фазы 3 с кадром контакта");
        Assert.That(LeftPaws, Is.GreaterThan(0), "вторая (левая) лапа двойной фазы 3 не проверена");
    }

    /// <summary>Доля замаха угля лапы по часам босса (EnemyBodyTelegraphView.PawClock).</summary>
    private static float EmberShare(Simulation sim, in ThicketMasterState a, float tick)
    {
        float clock = ThicketMasterClipRules.BossClock(sim, Boss, tick);
        return Math.Clamp((clock - a.StageStartTick) / Math.Max(1, a.ImpactTick - a.StageStartTick), 0f, 1f);
    }

    /// <summary>
    /// Живой босс под Песочными Часами: в стойке после удара (прошедший контакт Sim не
    /// сдвигает) и посреди замаха. Поза вида с трекером после заморозки — тот же кадр, дальше
    /// кадры шага не идут назад, контакт — на сдвинутом ударе; доля угля лапы всю заморозку
    /// стоит на доле до Часов и дальше растёт к удару.
    /// </summary>
    [TestCase(ThicketMasterAction.Paw, 3, false)]
    [TestCase(ThicketMasterAction.Paw, -10, false)]
    [TestCase(ThicketMasterAction.Stomp, 3, false)]
    [TestCase(ThicketMasterAction.Storm, 3, true)]
    public void LiveBoss_Hourglass_PoseAndPawEmberContinue(ThicketMasterAction action, int fromImpact, bool phase3)
    {
        var sim = Arena();
        sim.SetArtifact(RunArtifact.Hourglass);
        HeroInFront(sim, phase3 ? 3.0 : 2.6);
        ThicketMasterState a;
        while (!(sim.TryGetThicketMasterAction(Boss, out a) && a.Action == action))
        {
            Assert.That(sim.Tick, Is.LessThan(2400), action + " так и не начался");
            if (phase3 && sim.Tick == 181) sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            Step(sim);
        }
        int serial = a.Serial, stage = a.Stage;
        int fire = (action == ThicketMasterAction.Storm ? a.LastImpactTick : a.ImpactTick) + fromImpact;
        var track = new ThicketHourglassTrack();
        track.Apply(a);
        while (sim.Tick - 1 < fire)
        {
            Step(sim);
            Assert.That(sim.TryGetThicketMasterAction(Boss, out a) && a.Serial == serial, Is.True, "действие идёт до Часов");
            track.Apply(a);
        }
        stage = a.Stage;
        float before = ThicketMasterClipRules.Action(track.Apply(a), sim.Tick - 1).Frame;
        float emberBefore = EmberShare(sim, a, sim.Tick - 1);

        sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
        sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
        Assert.That(sim.TimeStopped, Is.True);
        Assert.That(sim.ThicketMasterFrozenTicksLeft(Boss), Is.GreaterThan(0), "босс стоит под Часами");
        // Заморозка: аниматор вида стоит; часы угля — тоже.
        while (sim.ThicketMasterFrozenTicksLeft(Boss) > 0)
        {
            Assert.That(sim.TryGetThicketMasterAction(Boss, out a) && a.Serial == serial && a.Stage == stage, Is.True);
            if (fromImpact < 0)
                Assert.That(EmberShare(sim, a, sim.Tick - 1), Is.EqualTo(emberBefore).Within(1e-4), $"уголь стоит, тик {sim.Tick}");
            Step(sim);
        }

        Assert.That(sim.TryGetThicketMasterAction(Boss, out a) && a.Serial == serial && a.Stage == stage, Is.True);
        float raw = ThicketMasterClipRules.Action(a, sim.Tick - 1).Frame;
        var pose = ThicketMasterClipRules.Action(track.Apply(a), sim.Tick - 1);
        TestContext.WriteLine($"{action} {fromImpact:+0;-0}: кадр до Часов {before:0.##}, после {pose.Frame:0.##}, без трекера {raw:0.##}");
        Assert.That(pose.Frame, Is.EqualTo(before).Within(.51f), "после Часов — тот же кадр");
        if (fromImpact < 0) Assert.That(EmberShare(sim, a, sim.Tick - 1), Is.EqualTo(emberBefore).Within(1.01f / 24f));

        ThicketClip clip = pose.Clip;
        float last = pose.Frame, ember = emberBefore;
        bool landed = false;
        while (true)
        {
            Step(sim);
            if (!sim.TryGetThicketMasterAction(Boss, out a) || a.Serial != serial) break;
            float tick = sim.Tick - 1;
            pose = ThicketMasterClipRules.Action(track.Apply(a), tick);
            if (a.Stage == stage && pose.Clip == clip)
                Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(last - 1e-4f), $"{action}: кадр назад на тике {tick}");
            stage = a.Stage; clip = pose.Clip; last = pose.Frame;
            if (fromImpact < 0 && !a.HitResolved)
            {
                float share = EmberShare(sim, a, tick);
                Assert.That(share, Is.GreaterThanOrEqualTo(ember - 1e-4f), $"уголь назад на тике {tick}");
                ember = share;
            }
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyActionImpact || e.Source != Boss) continue;
                if (!ThicketMasterClipRules.TryContact((EnemyActionKind)e.ActionVariant, e.Amount, out var contactClip, out float contactFrame))
                    continue;
                Assert.That(pose.Clip, Is.EqualTo(contactClip));
                Assert.That(pose.Frame, Is.EqualTo(contactFrame).Within(1e-3), $"контакт после Часов на тике {tick}");
                landed = true;
            }
        }
        if (fromImpact < 0) Assert.That(landed, Is.True, "лапа ударила после Часов");
    }
}
