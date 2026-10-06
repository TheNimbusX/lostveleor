using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи — «Терновник», вид (razlom/Assets/Game.View/ThicketMasterSeedRules.cs, ThicketMasterClipRules, контракт
/// artifacts/tools/wf/boss-tempo-contract.md § 17.2; заменил веер § 16). Жест — клип каста прорастания тем же отрезком, без
/// шагов разворота; крест вида — ровно линии Sim (и поворот корня Unity); сроки куста по часам босса держат Часы; рост,
/// дрожь, блик, отдача и увядание — в своих окнах и до конца (увядший куст под землёй); остриё шипа в целые тики — ровно
/// остриё Sim, без скачков; вставший шип доходит до точки Sim.
/// </summary>
public sealed class ThicketMasterSeedRulesTests
{
    private const int Boss = 1;
    private const int IntroDone = Simulation.ThicketMinSleepTicks + Simulation.ThicketWakeTicks
        + Simulation.ThicketRoarWindupTicks + Simulation.ThicketRoarRecoveryTicks;

    // ------------------------------------------------------------ события и клип

    [Test]
    public void SeedsKind_IsReadByTheCombatView_ButIsNotABodyContact()
    {
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketSeeds), Is.True);
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketIntro), Is.False, "вступление читает вид кат-сцены");
        Assert.That(ThicketMasterClipRules.IsThicketKind(EnemyActionKind.ThicketPaw), Is.True);
        Assert.That(ThicketMasterClipRules.TryContact(EnemyActionKind.ThicketSeeds, 7, out _, out _), Is.False,
            "шипы бьют сами — их удар не кадр тела");
    }

    /// <summary>Жест каста так, как его ставит Sim (BeginThicketCast): 18 тиков, без контакта.</summary>
    private static ThicketMasterState Cast(ThicketMasterAction action, int start)
    {
        int end = start + Simulation.ThicketCastGestureTicks;
        return new ThicketMasterState
        {
            Serial = 5, Action = action, StartTick = start, StageStartTick = start, ImpactTick = end, LastImpactTick = end, EndTick = end,
            Stage = 0, Stages = 1, Tag = 2, HitResolved = true,
        };
    }

    private static ThicketClipPose Pose(in ThicketMasterState a, float tick) => ThicketMasterClipRules.Action(a, tick);

    [Test]
    public void Clip_BushCast_IsTheSproutGesture_NoTail_NoTurnSteps()
    {
        var seeds = Cast(ThicketMasterAction.Seeds, 100);
        var sprout = Cast(ThicketMasterAction.Sprout, 100);
        for (float t = 100f; t <= 118f; t += .25f)
        {
            var a = Pose(seeds, t);
            var b = Pose(sprout, t);
            Assert.That(a.Clip, Is.EqualTo(ThicketClip.SproutCast), $"тик {t}: жест терновника — каст прорастания, не Idle");
            Assert.That(a.Frame, Is.EqualTo(b.Frame).Within(1e-5), $"тик {t}: тот же отрезок, что у прорастания");
            Assert.That(a.Burrowed, Is.False);
        }
        Assert.That(ThicketMasterClipRules.TailEndTick(seeds), Is.EqualTo(seeds.EndTick), "хвоста нет — кусты идут сами");
        Assert.That(ThicketMasterClipRules.TailEndTick(seeds), Is.EqualTo(ThicketMasterClipRules.TailEndTick(sprout)));
        Assert.That(ThicketMasterClipRules.TurnStepsUnder(ThicketClip.SproutCast, true, out _), Is.False, "корпус не доворачивает");
        Assert.That(ThicketMasterClipRules.TurnStepsUnder(ThicketClip.BerryVolley, true, out _), Is.False, "клипа ливня у терновника нет");
        Assert.That(ThicketMasterClipRules.TurnStepsUnder(ThicketClip.PawR, false, out bool right), Is.True);
        Assert.That(right, Is.True);
        Assert.That(ThicketMasterClipRules.TurnStepsUnder(ThicketClip.PawL, false, out right), Is.True);
        Assert.That(right, Is.False);
    }

    [TestCase(104)]
    [TestCase(113)]
    public void Clip_HourglassHoldsTheGestureFrame(int at)
    {
        const int shift = Simulation.ThicketHourglassShiftTicks;
        var a = Cast(ThicketMasterAction.Seeds, 100);
        var track = new ThicketHourglassTrack();
        float before = Pose(track.Apply(a), at).Frame;
        // Сдвиг Часов так, как его делает Sim: начало шага и конец — всегда, удар — только впереди.
        var shifted = a;
        shifted.StageStartTick += shift;
        if (shifted.ImpactTick >= at + 1) shifted.ImpactTick += shift;
        if (shifted.LastImpactTick >= at + 1) shifted.LastImpactTick += shift;
        shifted.EndTick += shift;
        Assert.That(Pose(track.Apply(shifted), at + shift).Frame, Is.EqualTo(before).Within(1e-3), "после Часов — тот же кадр");
    }

    // ------------------------------------------------------------ крест: вид = линии Sim

    [Test]
    public void Cross_LanesOfTheView_AreTheSimLanes_AndTheUnityYawOfTheRoot()
    {
        foreach (bool diagonal in new[] { false, true })
        {
            FixVec2 axis = Simulation.ThicketBushAxis(diagonal);
            float ax = axis.X.ToFloat(), ay = axis.Y.ToFloat();
            float yaw = ThicketMasterSeedRules.AxisYawDegrees(ax, ay);
            for (int lane = 0; lane < Simulation.ThicketBushLanes; lane++)
            {
                FixVec2 sim = Simulation.ThicketBushLaneDirection(axis, lane);
                ThicketMasterSeedRules.LaneDirection(ax, ay, lane, out float x, out float y);
                Assert.That(x, Is.EqualTo(sim.X.ToFloat()).Within(1e-4), $"{(diagonal ? "×" : "+")} линия {lane}: x");
                Assert.That(y, Is.EqualTo(sim.Y.ToFloat()).Within(1e-4), $"{(diagonal ? "×" : "+")} линия {lane}: y");
                // Корень куста повёрнут на yaw, линия k в префабе — на LaneYaw(k): мировое направление — та же линия.
                ThicketMasterSeedRules.YawDirection(yaw + ThicketMasterSeedRules.LaneYawDegrees(lane), out float wx, out float wz);
                Assert.That(wx, Is.EqualTo(sim.X.ToFloat()).Within(1e-4), $"линия {lane}: мир x");
                Assert.That(wz, Is.EqualTo(sim.Y.ToFloat()).Within(1e-4), $"линия {lane}: мир z (Sim y)");
            }
        }
        for (int index = 0; index < Simulation.ThicketSeedSlots; index++)
        {
            Assert.That(ThicketMasterSeedRules.BushOf(index) * Simulation.ThicketBushLanes + ThicketMasterSeedRules.LaneOf(index), Is.EqualTo(index));
            Assert.That(ThicketMasterSeedRules.BushOf(index), Is.LessThan(Simulation.ThicketBushSlots));
        }
        Assert.That(ThicketMasterSeedRules.MouthRadius, Is.GreaterThan(Simulation.ThicketBushThornStart.ToFloat()), "устье — на линии, за её началом");
        Assert.That(ThicketMasterSeedRules.MouthRadius, Is.LessThan(Simulation.ThicketBushRadius.ToFloat()), "устье — в круге куста Sim");
    }

    // ------------------------------------------------------------ сроки куста

    [Test]
    public void BushClock_EachSpanCountsFromTheNextShiftedTick_HourglassKeepsItContinuous()
    {
        const int sprout = 200, launch = sprout + Simulation.ThicketBushWindupTicks;
        const int wither = launch + Simulation.ThicketBushStandTicks, gone = wither + Simulation.ThicketBushWitherTicks;
        const int shift = Simulation.ThicketHourglassShiftTicks;
        Assert.That(ThicketMasterSeedRules.GrowAge(sprout, launch), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.GrowAge(launch, launch), Is.EqualTo(ThicketMasterSeedRules.WindupTicks));
        Assert.That(ThicketMasterSeedRules.LaunchAge(launch, wither), Is.EqualTo(0f), "после выпуска — от увядания назад");
        Assert.That(ThicketMasterSeedRules.WitherAge(wither, gone), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.WitherAge(gone, gone), Is.EqualTo(ThicketMasterSeedRules.WitherTicks));
        // Часы: часы босса прыгают на заморозку вперёд, Sim сдвигает сроки впереди на столько же — отрезок тот же.
        for (float t = sprout; t < launch; t += .5f)
            Assert.That(ThicketMasterSeedRules.GrowAge(t + shift, launch + shift), Is.EqualTo(ThicketMasterSeedRules.GrowAge(t, launch)).Within(1e-4));
        for (float t = launch; t < wither; t += .5f)
            Assert.That(ThicketMasterSeedRules.LaunchAge(t + shift, wither + shift), Is.EqualTo(ThicketMasterSeedRules.LaunchAge(t, wither)).Within(1e-4));
        for (float t = wither; t < gone; t += .5f)
            Assert.That(ThicketMasterSeedRules.WitherAge(t + shift, gone + shift), Is.EqualTo(ThicketMasterSeedRules.WitherAge(t, gone)).Within(1e-4));
        Assert.That(ThicketMasterSeedRules.Stir(sprout - ThicketMasterSeedRules.StirTicks, sprout), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.Stir(sprout - 1f, sprout), Is.GreaterThan(.5f), "к прорастанию земля шевелится сильнее");
        Assert.That(ThicketMasterSeedRules.Stir(sprout, sprout), Is.EqualTo(0f), "проросло — шевеления нет");
    }

    [Test]
    public void Bush_GrowsOutOfTheGround_SwellsBristlesQuivers_GlintsBeforeLaunch_Recoils()
    {
        float w = ThicketMasterSeedRules.WindupTicks, sproutTicks = ThicketMasterSeedRules.SproutTicks;
        Assert.That(ThicketMasterSeedRules.Emerge(0f), Is.EqualTo(0f), "появляется, а не возникает");
        Assert.That(ThicketMasterSeedRules.Emerge(sproutTicks), Is.EqualTo(1f));
        float peak = 0f;
        for (float g = 0f; g <= sproutTicks; g += .1f) peak = Math.Max(peak, ThicketMasterSeedRules.Emerge(g));
        Assert.That(peak, Is.InRange(1.01f, 1.12f), "выход с перелётом, без раздува");
        for (int cane = 0; cane < 10; cane++)
        {
            Assert.That(ThicketMasterSeedRules.CaneEmerge(0f, cane, 10), Is.EqualTo(0f));
            Assert.That(ThicketMasterSeedRules.CaneEmerge(sproutTicks + ThicketMasterSeedRules.CaneStaggerTicks, cane, 10), Is.EqualTo(1f),
                $"стебель {cane} вышел целиком");
        }
        float swell = 0f, bristle = 0f, pod = 0f;
        for (float g = 0f; g < w; g += .25f)
        {
            Assert.That(ThicketMasterSeedRules.Swell(g), Is.GreaterThanOrEqualTo(swell - 1e-6f), $"куст не сдувается на {g}");
            Assert.That(ThicketMasterSeedRules.Bristle(g), Is.GreaterThanOrEqualTo(bristle - 1e-6f), $"шипы не втягиваются на {g}");
            Assert.That(ThicketMasterSeedRules.PodWindupScale(g), Is.GreaterThanOrEqualTo(pod - 1e-6f), $"стручок не сдувается на {g}");
            swell = ThicketMasterSeedRules.Swell(g); bristle = ThicketMasterSeedRules.Bristle(g); pod = ThicketMasterSeedRules.PodWindupScale(g);
        }
        Assert.That(ThicketMasterSeedRules.Swell(w - 1e-3f), Is.EqualTo(ThicketMasterSeedRules.SwellMax).Within(1e-3));
        Assert.That(ThicketMasterSeedRules.Bristle(0f), Is.EqualTo(ThicketMasterSeedRules.BristleStart));
        Assert.That(ThicketMasterSeedRules.Bristle(w), Is.EqualTo(1f).Within(1e-5), "к выпуску шипы во всю длину");
        Assert.That(ThicketMasterSeedRules.PodWindupScale(ThicketMasterSeedRules.PodAppearTicks), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.PodWindupScale(w), Is.EqualTo(1f).Within(1e-5), "к выпуску стручок полный — как в полёте");
        Assert.That(ThicketMasterSeedRules.Quiver(w - ThicketMasterSeedRules.QuiverTicks), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.Quiver(w - .01f), Is.GreaterThan(.95f), "дрожь — к выпуску");
        Assert.That(ThicketMasterSeedRules.Quiver(w), Is.EqualTo(0f), "выпустил — не дрожит");
        const int launch = 300;
        int glint = ThicketMasterSeedRules.GlintTicks;
        Assert.That(ThicketMasterSeedRules.Glint(launch - glint, launch), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.Glint(launch - glint * .5f, launch), Is.EqualTo(1f).Within(1e-5), "пик блика — за полокна до выпуска");
        Assert.That(ThicketMasterSeedRules.Glint(launch, launch), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.Recoil(0f), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.Recoil(ThicketMasterSeedRules.RecoilPeakTicks), Is.EqualTo(1f).Within(1e-5));
        Assert.That(ThicketMasterSeedRules.Recoil(ThicketMasterSeedRules.RecoilTicks), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.LaneSnap(0f), Is.EqualTo(1f), "на выпуске шипы ещё во всю длину — без скачка");
        Assert.That(ThicketMasterSeedRules.LaneSnap(ThicketMasterSeedRules.SnapTicks), Is.EqualTo(ThicketMasterSeedRules.SpentExtension).Within(1e-5));
    }

    [Test]
    public void Bush_WithersDroopsAndSinks_UnderTheGroundAtTheEnd_LostBushWithersFaster()
    {
        ThicketMasterSeedRules.Wither(0f, out float dry, out float droop, out float sink, out float scale);
        Assert.That(dry, Is.EqualTo(0f)); Assert.That(droop, Is.EqualTo(0f)); Assert.That(sink, Is.EqualTo(0f)); Assert.That(scale, Is.EqualTo(1f));
        float lastDry = 0f, lastDroop = 0f, lastSink = 0f, lastScale = 1f;
        for (float t = 0f; t <= ThicketMasterSeedRules.WitherTicks; t += .25f)
        {
            ThicketMasterSeedRules.Wither(t, out dry, out droop, out sink, out scale);
            Assert.That(dry, Is.GreaterThanOrEqualTo(lastDry - 1e-6f), $"сохнет без отката на {t}");
            Assert.That(droop, Is.GreaterThanOrEqualTo(lastDroop - 1e-6f));
            Assert.That(sink, Is.GreaterThanOrEqualTo(lastSink - 1e-6f));
            Assert.That(scale, Is.LessThanOrEqualTo(lastScale + 1e-6f));
            lastDry = dry; lastDroop = droop; lastSink = sink; lastScale = scale;
        }
        Assert.That(dry, Is.EqualTo(1f)); Assert.That(droop, Is.EqualTo(1f)); Assert.That(sink, Is.EqualTo(1f));
        // Самое высокое — стоячие стебли ~1,3 м: съёжившись (scale), куст целиком уходит под пол.
        Assert.That(ThicketMasterSeedRules.SinkDepth, Is.GreaterThan(1.4f * scale), "к уходу куста над землёй нет");
        Assert.That(ThicketMasterSeedRules.Withered(ThicketMasterSeedRules.WitherTicks), Is.True);
        Assert.That(ThicketMasterSeedRules.LostWitherAge(ThicketMasterSeedRules.LostWitherTicks), Is.EqualTo(ThicketMasterSeedRules.WitherTicks).Within(1e-4),
            "пропавший куст вянет за LostWitherTicks");
    }

    // ------------------------------------------------------------ правила стручка

    [Test]
    public void Pod_LeavesTheMouthOntoTheLine_AboveTheGround_StopsAndShrinks()
    {
        float e = ThicketMasterSeedRules.LaunchEaseMetres;
        Assert.That(ThicketMasterSeedRules.LaunchShare(0f), Is.EqualTo(1f));
        Assert.That(ThicketMasterSeedRules.LaunchShare(e), Is.EqualTo(0f).Within(1e-6));
        Assert.That(ThicketMasterSeedRules.PopArc(e * .5f), Is.EqualTo(ThicketMasterSeedRules.PopLift).Within(1e-5));
        for (float d = 0f; d <= 9f; d += .05f)
            Assert.That(ThicketMasterSeedRules.Lift(d), Is.GreaterThanOrEqualTo(ThicketMasterSeedRules.PodRadius), "стручок не режет землю");
        for (int k = 1; k <= 5; k++)
            Assert.That(ThicketMasterSeedRules.Lift(ThicketMasterSeedRules.HopTouch(k)), Is.EqualTo(ThicketMasterSeedRules.FlightLift).Within(1e-4));
        // Герой в начале каста — не ближе 2,5 м к кусту: сход из устья кончается раньше, чем шип может его коснуться.
        float firstContact = Simulation.ThicketBushHeroGap.ToFloat() - Simulation.ThicketBushThornStart.ToFloat() - .45f;
        Assert.That(e, Is.LessThan(firstContact));
        Assert.That(ThicketMasterSeedRules.PodRadius * 2f, Is.LessThan(.5f), "ядро стручка — в ширину линии 0,5 м");
        Assert.That(ThicketMasterSeedRules.ShatterScale(ThicketMasterSeedRules.ShatterTicks), Is.EqualTo(0f));
        ThicketMasterSeedRules.Drop(ThicketMasterSeedRules.DropTicks, out float pitch, out float sink, out float scale);
        Assert.That(pitch, Is.GreaterThan(30f));
        Assert.That(sink, Is.EqualTo(ThicketMasterSeedRules.FlightLift + ThicketMasterSeedRules.PodRadius).Within(1e-5));
        Assert.That(scale, Is.EqualTo(0f).Within(1e-5));
        Assert.That(ThicketMasterSeedRules.GoneScale(ThicketMasterSeedRules.GoneTicks), Is.EqualTo(0f));
        Assert.That(ThicketMasterSeedRules.PodPool, Is.GreaterThanOrEqualTo(2 * Simulation.ThicketSeedSlots), "два каста шипов");
        Assert.That(ThicketMasterSeedRules.BushPool, Is.GreaterThanOrEqualTo(2 * Simulation.ThicketBushSlots), "два каста кустов");
        Assert.That(ThicketMasterSeedRules.StopTick(100, 3f, 105), Is.EqualTo(Math.Max(105f, 100f + 3f / ThicketMasterSeedRules.Speed)).Within(1e-4));
        Assert.That(ThicketMasterSeedRules.StoppedTip(100, 3f, 200f), Is.EqualTo(3f), "дошёл — стоит");
    }

    // ------------------------------------------------------------ живая симуляция

    private static Simulation Arena(double distance)
    {
        var sim = new Simulation(77, 64);
        sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(distance));
        var e = sim.Entities;
        e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
        e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
        e.RefreshStats(0);
        e.Health[0] = e.MaxHealth[0];
        e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
        e.RefreshStats(Boss);
        foreach (var action in new[] { ThicketMasterAction.Dive, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen,
                     ThicketMasterAction.Rain, ThicketMasterAction.Storm })
            sim.SetThicketReadyTick(Boss, action, int.MaxValue / 2);
        sim.SetThicketDiveDueTick(Boss, int.MaxValue / 2);
        sim.SetThicketStompPickReadyTick(Boss, int.MaxValue / 2);
        return sim;
    }

    /// <summary>Шагает до начала терновника (Started(ThicketSeeds, 0)); тик начала.</summary>
    private static int RunUntilCast(Simulation sim)
    {
        for (int k = 0; k < IntroDone + 60; k++)
        {
            sim.Step(InputFrame.Empty);
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds && e.Amount == 0)
                    return sim.Tick - 1;
        }
        return -1;
    }

    /// <summary>
    /// Живой терновник фазы 1 (герой в 7 м: куст 0 ставит его на свою линию): тело в жесте — каст прорастания; у каждого куста
    /// крест вида — линии Sim (направление и начало пути каждого шипа, место шипа = куст × 4 + линия), устье стручка — на
    /// его линии; рост вида от прорастания до выпуска 0 → Windup; куст пропадает из Sim, когда вид уже увял его целиком;
    /// остриё вида при Alpha 0 и 1 — ровно Travelled Sim до и после шага, между тиками без скачка; вставший шип доходит до
    /// точки события не позже следующего тика.
    /// </summary>
    [TestCase(7.0)]
    [TestCase(6.5)]
    [TestCase(8.0)]
    public void LiveBushes_CrossLifetimeAndThornTipsMatchTheSim(double distance)
    {
        var sim = Arena(distance);
        int start = RunUntilCast(sim);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "терновник не начался");
        Assert.That(sim.TryGetThicketMasterAction(Boss, out var cast) && cast.Action == ThicketMasterAction.Seeds, Is.True);
        float speed = ThicketMasterSeedRules.Speed;
        float thornStart = Simulation.ThicketBushThornStart.ToFloat();
        var shownAtOne = new Dictionary<int, float>();
        var paths = new Dictionary<int, ThicketSeedState>();
        var lastBush = new Dictionary<int, ThicketBushState>();
        var gone = new HashSet<int>();
        int crossChecked = 0, stopped = 0, hits = 0, flights = 0, gestureTicks = 1;
        Assert.That(ThicketMasterClipRules.Action(cast, start).Clip, Is.EqualTo(ThicketClip.SproutCast), "тик начала — жест каста");
        for (int k = 0; k < 200; k++)
        {
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            sim.Step(InputFrame.Empty);
            float tick = sim.Tick - 1;
            if (sim.TryGetThicketMasterAction(Boss, out var now) && now.Serial == cast.Serial)
            {
                Assert.That(ThicketMasterClipRules.Action(now, tick).Clip, Is.EqualTo(ThicketClip.SproutCast), $"тело на тике {tick} — жест каста");
                gestureTicks++;
            }
            var present = new HashSet<int>();
            for (int slot = 0; slot < Simulation.ThicketBushSlots; slot++)
            {
                if (!sim.TryGetThicketBush(Boss, slot, out var b)) continue;
                present.Add(b.Serial);
                lastBush[b.Serial] = b;
                Assert.That(b.Order, Is.EqualTo(slot));
                if (!b.Sprouted) { Assert.That(ThicketMasterSeedRules.GrowAge(tick, b.LaunchTick), Is.LessThan(0f), "до прорастания рост вида не идёт"); continue; }
                float grow = ThicketMasterSeedRules.GrowAge(tick, b.LaunchTick);
                Assert.That(grow, Is.GreaterThanOrEqualTo(0f));
                if ((int)tick == b.SproutTick) Assert.That(grow, Is.EqualTo(0f), "рост вида — с тика прорастания");
                if ((int)tick == b.LaunchTick) Assert.That(grow, Is.EqualTo(ThicketMasterSeedRules.WindupTicks), "к выпуску — весь замах");
                float ax = b.Axis.X.ToFloat(), ay = b.Axis.Y.ToFloat(), cx = b.Center.X.ToFloat(), cy = b.Center.Y.ToFloat();
                for (int lane = 0; lane < Simulation.ThicketBushLanes; lane++)
                {
                    if (!sim.TryGetThicketSeed(Boss, b.Order * Simulation.ThicketBushLanes + lane, out var s)) continue;
                    Assert.That(ThicketMasterSeedRules.BushOf(s.Index), Is.EqualTo(b.Order));
                    Assert.That(ThicketMasterSeedRules.LaneOf(s.Index), Is.EqualTo(lane));
                    ThicketMasterSeedRules.LaneDirection(ax, ay, lane, out float dx, out float dy);
                    Assert.That(dx, Is.EqualTo(s.Direction.X.ToFloat()).Within(2e-3), $"куст {b.Order} линия {lane}: направление вида = путь шипа");
                    Assert.That(dy, Is.EqualTo(s.Direction.Y.ToFloat()).Within(2e-3));
                    Assert.That(cx + dx * thornStart, Is.EqualTo(s.Origin.X.ToFloat()).Within(2e-3), "начало пути — от центра куста по линии");
                    Assert.That(cy + dy * thornStart, Is.EqualTo(s.Origin.Y.ToFloat()).Within(2e-3));
                    if (!s.Released) Assert.That(s.ReleaseTick, Is.EqualTo(b.LaunchTick), "стручок в устье набухает к выпуску куста");
                    crossChecked++;
                }
            }
            foreach (var pair in lastBush)
                if (!present.Contains(pair.Key) && gone.Add(pair.Key))
                    Assert.That(ThicketMasterSeedRules.Withered(ThicketMasterSeedRules.WitherAge(tick, pair.Value.GoneTick)), Is.True,
                        $"куст {pair.Value.Order} ушёл из Sim, когда вид увял его целиком");
            for (int slot = 0; slot < Simulation.ThicketSeedSlots; slot++)
            {
                if (!sim.TryGetThicketSeed(Boss, slot, out var s) || !s.Released) continue;
                paths[s.Serial] = s;
                float travelled = s.Travelled.ToFloat(), length = s.Length.ToFloat();
                float atOne = ThicketMasterSeedRules.TipDistance(s.ReleaseTick, travelled, length, tick + 1f);
                float atZero = ThicketMasterSeedRules.TipDistance(s.ReleaseTick, travelled, length, tick);
                Assert.That(atOne, Is.EqualTo(travelled).Within(1e-4), $"шип {s.Serial}: Alpha 1 — остриё Sim");
                Assert.That(atZero, Is.EqualTo(Math.Max(0f, travelled - speed)).Within(1e-4), $"шип {s.Serial}: Alpha 0 — тик назад");
                if (shownAtOne.TryGetValue(s.Serial, out float previous))
                    Assert.That(atZero, Is.EqualTo(previous).Within(1e-4), $"шип {s.Serial}: без скачка между тиками");
                shownAtOne[s.Serial] = atOne;
                var tip = Simulation.ThicketSeedTip(in s);
                Assert.That(s.Origin.X.ToFloat() + s.Direction.X.ToFloat() * atOne, Is.EqualTo(tip.X.ToFloat()).Within(2e-3));
                Assert.That(s.Origin.Y.ToFloat() + s.Direction.Y.ToFloat() * atOne, Is.EqualTo(tip.Y.ToFloat()).Within(2e-3));
                flights++;
            }
            foreach (var e in sim.Events)
            {
                if (e.ActionVariant != (int)EnemyActionKind.ThicketSeeds || e.Type != SimEventType.EnemyActionImpact) continue;
                stopped++;
                if (e.Flag) hits++;
                if (!paths.TryGetValue(e.Amount, out var s)) continue;
                // Остановка: вид знает начало пути и направление по последнему кадру полёта.
                float px = e.Position.X.ToFloat() - s.Origin.X.ToFloat(), py = e.Position.Y.ToFloat() - s.Origin.Y.ToFloat();
                float along = px * s.Direction.X.ToFloat() + py * s.Direction.Y.ToFloat();
                float off = Math.Abs(px * s.Direction.Y.ToFloat() - py * s.Direction.X.ToFloat());
                Assert.That(off, Is.LessThan(.01f), "точка остановки — на линии шипа");
                float arrive = ThicketMasterSeedRules.StopTick(s.ReleaseTick, along, (int)tick);
                Assert.That(arrive, Is.InRange(tick, tick + 1f + 1e-4f), $"шип {e.Amount}: доходит до точки в тик события");
                Assert.That(ThicketMasterSeedRules.StoppedTip(s.ReleaseTick, along, arrive), Is.EqualTo(Math.Max(0f, along)).Within(1e-3));
            }
            if (!sim.TryGetThicketSeedVolley(Boss, out _) && lastBush.Count > 0 && gone.Count == lastBush.Count) break;
        }
        TestContext.WriteLine($"терновник {start}: кустов {lastBush.Count}, проверок креста {crossChecked}, полётов-тиков {flights}, встало {stopped}, попаданий {hits}");
        Assert.That(gestureTicks, Is.EqualTo(Simulation.ThicketCastGestureTicks), "жест — 18 тиков");
        Assert.That(lastBush.Count, Is.EqualTo(Simulation.ThicketBushesPhase1), "фаза 1 — два куста");
        Assert.That(gone.Count, Is.EqualTo(lastBush.Count), "все кусты ушли");
        Assert.That(crossChecked, Is.GreaterThan(0));
        Assert.That(stopped, Is.GreaterThanOrEqualTo(4));
        Assert.That(hits, Is.LessThanOrEqualTo(1), "одно попадание на каст");
    }

    /// <summary>
    /// Часы в росте куста: часы босса прыгают на заморозку вперёд, Sim сдвигает выпуск — рост вида стоит, пока босс стоит, и
    /// идёт дальше без скачка; стручок в устье ждёт того же выпуска.
    /// </summary>
    [Test]
    public void LiveHourglass_HoldsTheBushGrowth_ThenItGoesOnWithoutAJump()
    {
        var sim = Arena(7.0);
        int start = RunUntilCast(sim);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        sim.SetArtifact(RunArtifact.Hourglass);
        while (sim.Tick < start + 6) sim.Step(InputFrame.Empty);
        Assert.That(sim.TryGetThicketBush(Boss, 0, out var before), Is.True);
        float grow0 = ThicketMasterSeedRules.GrowAge(ThicketMasterClipRules.BossClock(sim, Boss, sim.Tick - 1), before.LaunchTick);
        sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
        Assert.That(sim.ThicketMasterFrozenTicksLeft(Boss), Is.GreaterThan(0), "Часы держат босса");
        Assert.That(sim.TryGetThicketBush(Boss, 0, out var held), Is.True);
        Assert.That(held.LaunchTick, Is.EqualTo(before.LaunchTick + Simulation.ThicketHourglassShiftTicks));
        float last = ThicketMasterSeedRules.GrowAge(ThicketMasterClipRules.BossClock(sim, Boss, sim.Tick - 1), held.LaunchTick);
        Assert.That(last - grow0, Is.InRange(-1e-4f, 1f + 1e-4f), "на включении Часов рост не прыгает");
        bool thawed = false;
        for (int k = 0; k < Simulation.ThicketHourglassShiftTicks + 10; k++)
        {
            bool frozen = sim.ThicketMasterFrozenTicksLeft(Boss) > 0;
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThicketBush(Boss, 0, out var b), Is.True);
            float grow = ThicketMasterSeedRules.GrowAge(ThicketMasterClipRules.BossClock(sim, Boss, sim.Tick - 1), b.LaunchTick);
            Assert.That(grow - last, Is.InRange(-1e-4f, 1f + 1e-4f), $"тик {sim.Tick - 1}: рост без скачка");
            if (frozen && sim.ThicketMasterFrozenTicksLeft(Boss) > 0) Assert.That(grow, Is.EqualTo(last).Within(1e-4), "под Часами куст стоит");
            if (sim.ThicketMasterFrozenTicksLeft(Boss) == 0) thawed = true;
            if (sim.TryGetThicketSeed(Boss, 0, out var s) && !s.Released) Assert.That(s.ReleaseTick, Is.EqualTo(b.LaunchTick));
            last = grow;
            if (grow >= ThicketMasterSeedRules.WindupTicks) break;
        }
        Assert.That(thawed, Is.True, "Часы кончились до выпуска");
    }
}
