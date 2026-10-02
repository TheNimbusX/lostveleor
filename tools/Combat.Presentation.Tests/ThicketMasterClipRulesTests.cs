using System;
using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи: действие Sim → клип и кадр (razlom/Assets/Game.View/ThicketMasterClipRules.cs,
/// контракт клипов artifacts/tools/wf/boss-clip-spec.md, темп artifacts/tools/wf/boss-tempo-contract.md).
/// Главное — кадр контакта клипа ровно в тик удара тела: на синтетических действиях (серия
/// П/Л/П, тяжёлый замах, топот с кольцом, жесты кастов) и на живой симуляции босса — в тик
/// каждого EnemyActionImpact тела поза даёт кадр контакта, тело прячется ровно на время
/// ThicketUnderground, кадры одного шага не идут назад, хвост после конца действия
/// продолжает клип без скачка.
/// </summary>
public sealed class ThicketMasterClipRulesTests
{
    private const int Boss = 1;

    /// <summary>Сколько левых лап и третьих ударов серии проверено в живой сцене.</summary>
    private static int LeftPaws, ThirdPaws, StompRings, Tails;

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

        // Клипы, что идут сами за собой (П серии фазы 3 → П следующей, топот → топот), — вторая копия состояния.
        var alternates = ThicketMasterClipRules.All.Where(ThicketMasterClipRules.HasAlternate).ToArray();
        Assert.That(alternates, Is.EquivalentTo(new[] { ThicketClip.PawR, ThicketClip.PawL, ThicketClip.Stomp }));
        Assert.That(ThicketMasterClipRules.AlternateName(ThicketClip.PawR), Is.EqualTo("PawRAlt"));
        Assert.That(ThicketMasterClipRules.AlternatePhaseParameter(ThicketClip.PawR), Is.EqualTo("PawRAltPhase"));
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
        Assert.That(ThicketMasterClipRules.PawIsRight(2), Is.True, "серия П/Л/П");
        Assert.That(ThicketMasterClipRules.PawClip(1), Is.EqualTo(ThicketClip.PawL));
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketPaw), Is.True);
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketStorm), Is.True);
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.WendigoSweep), Is.False);
    }

    [Test]
    public void Blends_PawChainIsExact_LeftToRightIsShort_TurnsFourToSix()
    {
        Assert.That(ThicketMasterClipRules.ActionBlend(ThicketClip.PawR, ThicketClip.PawL, sameAction: true), Is.EqualTo(0),
            "PawL кадр 0 = PawR кадр 25 — без смеси");
        int leftToRight = ThicketMasterClipRules.ActionBlend(ThicketClip.PawL, ThicketClip.PawR, sameAction: true);
        Assert.That(leftToRight, Is.InRange(3, 4));
        Assert.That(ThicketMasterClipRules.ActionBlend(ThicketClip.PawL, ThicketClip.PawR, sameAction: false),
            Is.EqualTo(ThicketMasterClipRules.ActionBlendTicks));
        Assert.That(ThicketMasterClipRules.ActionBlend(ThicketClip.Idle, ThicketClip.PawR, sameAction: false),
            Is.EqualTo(ThicketMasterClipRules.ActionBlendTicks));
        Assert.That(ThicketMasterClipRules.TurnBlendTicks, Is.InRange(4, 6));
    }

    // ------------------------------------------------------------ синтетика

    private static ThicketMasterState State(ThicketMasterAction action, int start, int impact, int last, int end,
        int stage = 0, int stages = 1, bool resolved = false)
        => new ThicketMasterState
        {
            Serial = 1, Action = action, StartTick = start, StageStartTick = start, ImpactTick = impact,
            LastImpactTick = last, EndTick = end, Stage = stage, Stages = stages, HitResolved = resolved,
        };

    /// <summary>Шаг stage серии лапы так, как его ставит Sim: знак в тик прошлого удара, удар через 9.</summary>
    private static ThicketMasterState PawStage(ThicketMasterState first, int stage)
    {
        var a = first;
        a.Stage = stage;
        a.StageStartTick = first.ImpactTick + Simulation.ThicketPawSeriesGapTicks * (stage - 1);
        a.ImpactTick = first.ImpactTick + Simulation.ThicketPawSeriesGapTicks * stage;
        a.HitResolved = false;
        return a;
    }

    private static float Frame(in ThicketMasterState a, float tick, ThicketClip clip)
    {
        var pose = ThicketMasterClipRules.Action(a, tick);
        Assert.That(pose.Clip, Is.EqualTo(clip), $"{a.Action} шаг {a.Stage} на тике {tick}");
        return pose.Frame;
    }

    [Test]
    public void PawSeries_RightLeftRight_ContactOnEveryImpact_RecoveryInTheTail()
    {
        int windup = Simulation.ThicketPawWindupTicks, gap = Simulation.ThicketPawSeriesGapTicks;
        Assert.That(windup, Is.EqualTo(15));
        Assert.That(gap, Is.EqualTo(9));
        // Серия фазы 3 с тика 100: удары 115 / 124 / 133, действие снимается на 134.
        var first = State(ThicketMasterAction.Paw, 100, 115, 133, 134, stage: 0, stages: 3);
        Assert.That(Frame(first, 100f, ThicketClip.PawR), Is.EqualTo(0f));
        Assert.That(Frame(first, 107.5f, ThicketClip.PawR), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(first, 115f, ThicketClip.PawR), Is.EqualTo(24f));

        // Удар 0 и знак 1 — один тик: он ещё контакт правой (24→25), левая — со следующего, с кадра 0 (= PawR 25).
        var second = PawStage(first, 1);
        Assert.That(Frame(second, 115f, ThicketClip.PawR), Is.EqualTo(24f));
        Assert.That(Frame(second, 115.5f, ThicketClip.PawR), Is.EqualTo(24.5f).Within(1e-4));
        Assert.That(Frame(second, 116f, ThicketClip.PawL), Is.EqualTo(0f));
        Assert.That(Frame(second, 120f, ThicketClip.PawL), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(second, 124f, ThicketClip.PawL), Is.EqualTo(24f));

        var third = PawStage(first, 2);
        Assert.That(Frame(third, 124f, ThicketClip.PawL), Is.EqualTo(24f));
        Assert.That(Frame(third, 125f, ThicketClip.PawR), Is.EqualTo(0f));
        Assert.That(Frame(third, 133f, ThicketClip.PawR), Is.EqualTo(24f));
        // Последняя лапа отходит 24→49 за PawRecoveryTicks — дальше конца действия (134), в хвосте.
        int tail = 133 + ThicketMasterClipRules.PawRecoveryTicks;
        Assert.That(ThicketMasterClipRules.TailEndTick(third), Is.EqualTo(tail));
        Assert.That(Frame(third, 134f, ThicketClip.PawR), Is.GreaterThan(24f));
        Assert.That(Frame(third, tail, ThicketClip.PawR), Is.EqualTo(49f));
        Assert.That(Frame(third, tail + 20, ThicketClip.PawR), Is.EqualTo(49f));

        // «Сложно» и ярость: первый замах 30 — контакт всё равно на тике удара.
        var heavy = State(ThicketMasterAction.Paw, 100, 130, 139, 140, stage: 0, stages: 2);
        Assert.That(Frame(heavy, 115f, ThicketClip.PawR), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(heavy, 130f, ThicketClip.PawR), Is.EqualTo(24f));
        Assert.That(Frame(PawStage(heavy, 1), 139f, ThicketClip.PawL), Is.EqualTo(24f));
    }

    [Test]
    public void Stomp_SlamOnTheCircle_CrouchUntilTheRing_RiseInTheTail()
    {
        int windup = Simulation.ThicketStompWindupTicks, ring = Simulation.ThicketStompRingDelayTicks;
        // Топот с 0: круг на 24, кольцо на 39, действие снимается на 41.
        var stomp = State(ThicketMasterAction.Stomp, 0, windup, windup + ring, windup + ring + 2, stage: 0, stages: 2);
        Assert.That(Frame(stomp, 12f, ThicketClip.Stomp), Is.EqualTo(16.5f).Within(1e-4));
        Assert.That(Frame(stomp, windup, ThicketClip.Stomp), Is.EqualTo(33f));
        var ringStage = stomp;
        ringStage.Stage = 1; ringStage.StageStartTick = windup; ringStage.ImpactTick = windup + ring;
        Assert.That(Frame(ringStage, windup, ThicketClip.Stomp), Is.EqualTo(33f), "тик круга — лапы в земле");
        Assert.That(Frame(ringStage, windup + 2, ThicketClip.Stomp), Is.EqualTo(35f));
        Assert.That(Frame(ringStage, windup + ring, ThicketClip.Stomp), Is.EqualTo(ThicketMasterClipRules.StompRingFrame));
        int tail = windup + ring + (71 - ThicketMasterClipRules.StompRingFrame);
        Assert.That(ThicketMasterClipRules.TailEndTick(ringStage), Is.EqualTo(tail));
        Assert.That(Frame(ringStage, tail, ThicketClip.Stomp), Is.EqualTo(71f));
        Assert.That(ThicketMasterClipRules.TryContact(EnemyActionKind.ThicketStomp, 1, out var clip, out float frame), Is.True);
        Assert.That(clip, Is.EqualTo(ThicketClip.Stomp));
        Assert.That(frame, Is.EqualTo((float)ThicketMasterClipRules.StompRingFrame));
    }

    [Test]
    public void Roar_Wake_Storm_KeyFrames()
    {
        var roar = State(ThicketMasterAction.Roar, 10, 46, 46, 61);
        Assert.That(Frame(roar, 10f, ThicketClip.Roar), Is.EqualTo(0f));
        Assert.That(Frame(roar, 46f, ThicketClip.Roar), Is.EqualTo(45f));
        Assert.That(Frame(roar, 61f, ThicketClip.Roar), Is.EqualTo(60f));

        var wake = State(ThicketMasterAction.Wake, 90, 120, 120, 120, resolved: true);
        Assert.That(Frame(wake, 110f, ThicketClip.Wake), Is.EqualTo(20f).Within(1e-4));
        Assert.That(Frame(wake, 120f, ThicketClip.Wake), Is.EqualTo(30f));

        // Буря 60 / 45, стойка 12: крона раскрывается, волны на 75 и 135, закрывается к 159.
        var storm = State(ThicketMasterAction.Storm, 0, 60, 105, 117, stages: 2);
        Assert.That(Frame(storm, 60f, ThicketClip.Storm), Is.EqualTo(75f));
        Assert.That(Frame(storm, 105f, ThicketClip.Storm), Is.EqualTo(135f));
        Assert.That(Frame(storm, 117f, ThicketClip.Storm), Is.EqualTo(159f));
    }

    [Test]
    public void Dive_BurrowsAfterDiveIn_AndEmergesOnTheImpactTick()
    {
        int impact = 100 + Simulation.ThicketDiveBurrowTicks + Simulation.ThicketDiveTravelTicks + Simulation.ThicketDiveLockTicks;
        var dive = State(ThicketMasterAction.Dive, 100, impact, impact, impact + Simulation.ThicketDiveStandTicks);
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
        // Стойка 36 (было 24): Emerge целиком (36 кадров) 1:1 — к связке фаз 2–3 тело уже стряхнуло землю.
        Assert.That(Frame(dive, impact + Simulation.ThicketDiveStandTicks, ThicketClip.Emerge), Is.EqualTo(36f));
    }

    [Test]
    public void CastGestures_PressAndRelease_ShakeThenSettle_PumpsOnEveryVolley()
    {
        int gesture = Simulation.ThicketCastGestureTicks;
        Assert.That(gesture, Is.EqualTo(18));
        // Прорастание: нажим 0–8, короткое удержание, вырывание 80–90 за последние 8 тиков жеста.
        var sprout = State(ThicketMasterAction.Sprout, 0, gesture, gesture, gesture, resolved: true);
        Assert.That(Frame(sprout, 4f, ThicketClip.SproutCast), Is.EqualTo(4f).Within(1e-4));
        Assert.That(Frame(sprout, 8f, ThicketClip.SproutCast), Is.EqualTo(8f));
        Assert.That(Frame(sprout, 9.5f, ThicketClip.SproutCast), Is.EqualTo(9.5f).Within(1e-4));
        Assert.That(Frame(sprout, gesture - 8, ThicketClip.SproutCast), Is.EqualTo(80f));
        Assert.That(Frame(sprout, gesture, ThicketClip.SproutCast), Is.EqualTo(90f));
        Assert.That(ThicketMasterClipRules.TailEndTick(sprout), Is.EqualTo(gesture));
        Assert.That(ThicketMasterClipRules.TryContact(EnemyActionKind.ThicketSprout, 0, out _, out _), Is.False,
            "круги бьют сами — не кадр тела");

        // Пыльца: тряска 0–24 в жест, оседание 24–48 — хвост 1:1.
        var pollen = State(ThicketMasterAction.Pollen, 0, gesture, gesture, gesture, resolved: true);
        Assert.That(Frame(pollen, 9f, ThicketClip.PollenShake), Is.EqualTo(12f).Within(1e-4));
        Assert.That(Frame(pollen, gesture, ThicketClip.PollenShake), Is.EqualTo(24f));
        Assert.That(Frame(pollen, gesture + 12, ThicketClip.PollenShake), Is.EqualTo(36f).Within(1e-4));
        Assert.That(ThicketMasterClipRules.TailEndTick(pollen), Is.EqualTo(gesture + 24));
        Assert.That(ThicketMasterClipRules.TryContact(EnemyActionKind.ThicketPollen, 0, out _, out _), Is.False);

        // Ливень: 5 толчков раз в 12, пик толчка (3 / 18 / 33 клипа) — на знаке залпа.
        int every = Simulation.ThicketRainEveryTicks;
        Assert.That(every, Is.EqualTo(12));
        Assert.That(Simulation.ThicketRainVolleys, Is.EqualTo(5));
        var rain = State(ThicketMasterAction.Rain, 0, gesture, gesture, gesture, resolved: true);
        for (int v = 0; v < Simulation.ThicketRainVolleys; v++)
        {
            float pop = Frame(rain, every * v, ThicketClip.BerryVolley);
            Assert.That(pop, Is.EqualTo(v == 0 ? 3f : v == Simulation.ThicketRainVolleys - 1 ? 33f : 18f), "толчок " + v);
        }
        Assert.That(Frame(rain, every * 1.5f, ThicketClip.BerryVolley), Is.EqualTo(25.5f).Within(1e-4), "отдача между толчками");
        int lastPop = ThicketMasterClipRules.VolleyLastPopTick(0);
        Assert.That(lastPop, Is.EqualTo(48));
        Assert.That(Frame(rain, lastPop + Simulation.ThicketRainImpactTicks, ThicketClip.BerryVolley), Is.EqualTo(60f));
        Assert.That(ThicketMasterClipRules.TailEndTick(rain), Is.EqualTo(lastPop + 30 + ThicketMasterClipRules.VolleySettleTicks));
        Assert.That(Frame(rain, ThicketMasterClipRules.TailEndTick(rain), ThicketClip.BerryVolley), Is.EqualTo(120f));
        Assert.That(ThicketMasterClipRules.TryContact(EnemyActionKind.ThicketRain, 0, out _, out _), Is.False);
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

        // 2,0 м/с Sim (владелец 02.10, было 2,6) за секунду при шаге 1,7857 м (замер после правки
        // 02.10) — 1,12 цикла: клип 30 кадров (цикл 1 с) играет ×1,12. Рост ×1,15 — шаг тоже ×1,15
        // (масштаб тела): ×0,974, почти 1:1. В export.json пакета замер записан ещё при 2,6 (×1,456).
        float speed = Simulation.ThicketMasterMoveSpeed.ToFloat();
        Assert.That(ThicketMasterClipRules.DefaultWalkStride, Is.EqualTo(1.7857f).Within(.001f));
        Assert.That(ThicketMasterClipRules.WalkCycles(speed, ThicketMasterClipRules.DefaultWalkStride),
            Is.EqualTo(speed / ThicketMasterClipRules.DefaultWalkStride).Within(1e-5));
        Assert.That(ThicketMasterClipRules.WalkCycles(speed, ThicketMasterClipRules.DefaultWalkStride) * 30f / 30f,
            Is.EqualTo(1.12f).Within(.01f), "скорость клипа Walk против Sim: 2,0 / 1,7857");
        Assert.That(ThicketMasterClipRules.WalkCycles(speed, ThicketMasterClipRules.DefaultWalkStride * 1.15f),
            Is.EqualTo(1.12f / 1.15f).Within(.01f), "тело ×1,15 — шаг длиннее, клип медленнее");
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
        // Кольцо топота ударило на 39, Часы на 40 (прошедший контакт Sim не сдвигает).
        var stomp = State(ThicketMasterAction.Stomp, 0, 39, 39, 41, stage: 1, stages: 2, resolved: true);
        stomp.StageStartTick = 24;
        var track = new ThicketHourglassTrack();
        float before = ThicketMasterClipRules.Action(track.Apply(stomp), 40f).Frame;
        var shifted = Hourglass(stomp, 41);
        float raw = ThicketMasterClipRules.Action(shifted, 40f + shift).Frame;
        float after = ThicketMasterClipRules.Action(track.Apply(shifted), 40f + shift).Frame;
        Assert.That(raw - before, Is.GreaterThan(10f), "без трекера подъём прыгает вперёд — трекер нужен");
        Assert.That(after, Is.EqualTo(before).Within(1e-3), "с трекером — тот же кадр");

        // Последняя лапа серии: отход после удара, Часы через полтика.
        var paw = State(ThicketMasterAction.Paw, 100, 115, 124, 125, stage: 0, stages: 2);
        var left = PawStage(paw, 1);
        left.HitResolved = true;
        track = new ThicketHourglassTrack();
        before = ThicketMasterClipRules.Action(track.Apply(left), 124.5f).Frame;
        after = ThicketMasterClipRules.Action(track.Apply(Hourglass(left, 125)), 124.5f + shift).Frame;
        Assert.That(after, Is.EqualTo(before).Within(1e-3));

        // Посреди замаха Sim сдвигает и удар: трекеру добавлять нечего.
        track = new ThicketHourglassTrack();
        before = ThicketMasterClipRules.Action(track.Apply(paw), 110f).Frame;
        var pawShifted = Hourglass(paw, 111);
        Assert.That(track.Apply(pawShifted).ImpactTick, Is.EqualTo(115 + shift));
        Assert.That(ThicketMasterClipRules.Action(track.Apply(pawShifted), 110f + shift).Frame, Is.EqualTo(before).Within(1e-3));
        Assert.That(ThicketMasterClipRules.Action(track.Apply(pawShifted), 115f + shift).Frame, Is.EqualTo(24f), "контакт — на сдвинутом ударе");

        // Новое действие сдвиг не наследует.
        var next = State(ThicketMasterAction.Stomp, 300, 324, 339, 341, stages: 2);
        next.Serial = 2;
        Assert.That(track.Apply(next).ImpactTick, Is.EqualTo(324));
    }

    [Test]
    public void HourglassTrack_NewStageKeepsOnlyTheSharedLastImpactShift()
    {
        const int shift = Simulation.ThicketHourglassShiftTicks;
        var first = State(ThicketMasterAction.Paw, 100, 115, 124, 125, stage: 0, stages: 2, resolved: true);
        var track = new ThicketHourglassTrack();
        track.Apply(first);
        var shifted = Hourglass(first, 116);
        Assert.That(track.Apply(shifted).ImpactTick, Is.EqualTo(115 + shift), "прошедший удар первой догнал Часы");
        var second = shifted;
        second.Stage = 1; second.StageStartTick = 116 + shift; second.ImpactTick = 124 + shift; second.HitResolved = false;
        var shown = track.Apply(second);
        Assert.That(shown.ImpactTick, Is.EqualTo(124 + shift), "удар второй лапы — как в Sim");
        Assert.That(shown.LastImpactTick, Is.EqualTo(second.LastImpactTick));
    }

    [Test]
    public void Tail_ShiftMovesTheWholeTimeline()
    {
        var stomp = State(ThicketMasterAction.Stomp, 0, 39, 39, 41, stage: 1, stages: 2, resolved: true);
        stomp.StageStartTick = 24;
        var moved = ThicketMasterClipRules.Shift(stomp, 60);
        Assert.That(ThicketMasterClipRules.TailEndTick(moved), Is.EqualTo(ThicketMasterClipRules.TailEndTick(stomp) + 60));
        Assert.That(ThicketMasterClipRules.Action(moved, 110f).Frame,
            Is.EqualTo(ThicketMasterClipRules.Action(stomp, 50f).Frame).Within(1e-4));
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
    /// Шагает до until и сверяет каждый тик: удар тела — кадр контакта, тело спрятано ровно под
    /// землёй, кадры одного шага действия не идут назад; после конца действия хвост (вид играет
    /// его, пока босс стоит) продолжает тот же клип без скачка до TailEndTick.
    /// </summary>
    private static void Drive(Simulation sim, int until, Dictionary<EnemyActionKind, int> checkedImpacts,
        Action<Simulation> perTick = null)
    {
        int serial = 0, stage = -1;
        ThicketClip clip = ThicketClip.Idle;
        float lastFrame = -1f;
        ThicketMasterState last = default;
        bool tail = false;
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
                last = a;
                tail = true;
            }
            else if (tail && tick < ThicketMasterClipRules.TailEndTick(last) && last.Action != ThicketMasterAction.Rain)
            {
                // Хвост: та же функция по запомненному действию — клип тот же, кадр не назад.
                var pose = ThicketMasterClipRules.Action(last, tick);
                Assert.That(pose.Clip, Is.EqualTo(clip), $"{last.Action}: хвост сменил клип на тике {tick}");
                Assert.That(pose.Frame, Is.GreaterThanOrEqualTo(lastFrame - 1e-4f), $"{last.Action}: хвост назад на тике {tick}");
                lastFrame = pose.Frame;
                if (tick + 1 >= ThicketMasterClipRules.TailEndTick(last))
                {
                    float end = ThicketMasterClipRules.Action(last, ThicketMasterClipRules.TailEndTick(last)).Frame;
                    Assert.That(end, Is.EqualTo((float)ThicketMasterClipRules.Frames(pose.Clip)).Within(1e-3),
                        $"{last.Action}: хвост доигрывает клип");
                    Tails++;
                }
            }
            else tail = false;
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
                if (kind == EnemyActionKind.ThicketPaw && e.Amount == 2) ThirdPaws++;
                if (kind == EnemyActionKind.ThicketStomp && e.Amount == 1) StompRings++;
            }
        }
    }

    [Test]
    public void LiveBoss_EveryImpactLandsOnTheContactFrame()
    {
        var impacts = new Dictionary<EnemyActionKind, int>();
        LeftPaws = ThirdPaws = StompRings = Tails = 0;

        // Фаза 1: вступление (рёв), серии лапы и топот вплотную, нырок по дальнему герою.
        var near = Arena();
        HeroInFront(near, 2.6);
        Drive(near, 900, impacts, sim =>
        {
            if (sim.Tick == 600) HeroInFront(sim, 8.5);
        });

        // Фаза 3: рёв на пороге → буря, касты, серии П/Л/П и связки.
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

        TestContext.WriteLine(string.Join(", ", impacts.Select(p => p.Key + "×" + p.Value))
            + $", левых лап {LeftPaws}, третьих {ThirdPaws}, колец топота {StompRings}, хвостов {Tails}");
        foreach (var kind in new[] { EnemyActionKind.ThicketPaw, EnemyActionKind.ThicketStomp, EnemyActionKind.ThicketRoar,
                     EnemyActionKind.ThicketDive, EnemyActionKind.ThicketStorm })
            Assert.That(impacts.ContainsKey(kind), Is.True, kind + " ни разу не ударил — сцена не проверила его кадр");
        Assert.That(LeftPaws, Is.GreaterThan(0), "левая лапа серии не проверена");
        Assert.That(ThirdPaws, Is.GreaterThan(0), "третий удар серии (П после Л) не проверен");
        Assert.That(StompRings, Is.GreaterThan(0), "второе кольцо топота не проверено");
        Assert.That(Tails, Is.GreaterThan(0), "ни один хвост не доигран");
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
    /// стоит на доле до Часов и дальше растёт к удару (новый удар серии — свой уголь с нуля).
    /// </summary>
    [TestCase(ThicketMasterAction.Paw, 3, false)]
    [TestCase(ThicketMasterAction.Paw, -10, false)]
    [TestCase(ThicketMasterAction.Stomp, 3, false)]
    [TestCase(ThicketMasterAction.Stomp, 16, false)]
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
        if (fromImpact < 0) Assert.That(EmberShare(sim, a, sim.Tick - 1), Is.EqualTo(emberBefore).Within(1.01f / 15f));

        ThicketClip clip = pose.Clip;
        float last = pose.Frame, ember = emberBefore;
        int emberStage = stage;
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
                // Новый удар серии — свой уголь от его знака.
                if (a.Stage != emberStage) { emberStage = a.Stage; ember = 0f; }
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
