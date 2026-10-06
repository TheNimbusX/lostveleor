using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи — внешность по фазам (razlom/Assets/Game.View/ThicketMasterPhaseRules.cs,
/// план artifacts/tools/wf/boss-vfx-plan.md §4.5): смена одежды — в тик удара рёва порога,
/// не по HP и не в начале рёва; привязка с середины боя — сразу нужный уровень; яркость
/// рун — вспышка, дыхание от тика Sim, гаснет после смерти. На живой симуляции — уровень
/// меняется ровно в тик EnemyActionImpact рёва 66 и 33.
/// </summary>
public sealed class ThicketMasterPhaseRulesTests
{
    private const int Boss = 1;
    private const int R66 = Simulation.ThicketRoar66Bit, R50 = Simulation.ThicketRoar50Bit, R33 = Simulation.ThicketRoar33Bit;

    private static ThicketMasterMemory Memory(int roarsDone) => new ThicketMasterMemory { Awake = true, Phase = 1, RoarsDone = roarsDone };

    private static ThicketMasterState Roar(int tag, int impact) => new ThicketMasterState
    {
        Serial = 7, Action = ThicketMasterAction.Roar, Tag = tag, StartTick = impact - Simulation.ThicketRoarWindupTicks,
        StageStartTick = impact - Simulation.ThicketRoarWindupTicks, ImpactTick = impact, LastImpactTick = impact,
        EndTick = impact + Simulation.ThicketRoarRecoveryTicks, Stages = 1,
    };

    // ------------------------------------------------------------ уровень

    [Test]
    public void Roar66_ChangesTheDressAtItsImpact_NotAtItsStart()
    {
        var m = Memory(R66 | Simulation.ThicketRoarIntroBit);
        var a = Roar(R66, 300);
        int before = ThicketMasterPhaseRules.ShownBits(m, true, a, 299.9f);
        int after = ThicketMasterPhaseRules.ShownBits(m, true, a, 300f);
        Assert.AreEqual(1, ThicketMasterPhaseRules.Level(before));
        Assert.AreEqual(2, ThicketMasterPhaseRules.Level(after));
        Assert.AreEqual(ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.ChangeTick(true, a, 299.9f));
        Assert.AreEqual(300, ThicketMasterPhaseRules.ChangeTick(true, a, 300f));
        Assert.AreEqual(300, ThicketMasterPhaseRules.ChangeTick(true, a, 312.5f));
        // Рёв кончился — смена давно прошла, биты видны без вычитания.
        Assert.AreEqual(2, ThicketMasterPhaseRules.Level(ThicketMasterPhaseRules.ShownBits(m, false, default, 400f)));
        Assert.AreEqual(ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.ChangeTick(false, default, 400f));
    }

    [Test]
    public void Roar33_AfterLived66_IsLevelThree()
    {
        var m = Memory(R66 | R50 | R33);
        var a = Roar(R33, 900);
        int before = ThicketMasterPhaseRules.ShownBits(m, true, a, 880f);
        Assert.AreEqual(2, ThicketMasterPhaseRules.Level(before));
        Assert.IsTrue(ThicketMasterPhaseRules.Enraged(before));
        int after = ThicketMasterPhaseRules.ShownBits(m, true, a, 900f);
        Assert.AreEqual(3, ThicketMasterPhaseRules.Level(after));
        Assert.IsTrue(ThicketMasterPhaseRules.UsesPhase3Map(3));
        Assert.IsTrue(!ThicketMasterPhaseRules.UsesPhase3Map(2));
    }

    [Test]
    public void SkippedThreshold_OneRoarClosesBoth_LevelTwoAndEnraged()
    {
        var m = Memory(R66 | R50);
        var a = Roar(R66 | R50, 500);
        Assert.AreEqual(1, ThicketMasterPhaseRules.Level(ThicketMasterPhaseRules.ShownBits(m, true, a, 499f)));
        Assert.IsTrue(!ThicketMasterPhaseRules.Enraged(ThicketMasterPhaseRules.ShownBits(m, true, a, 499f)));
        int bits = ThicketMasterPhaseRules.ShownBits(m, true, a, 500f);
        Assert.AreEqual(2, ThicketMasterPhaseRules.Level(bits));
        Assert.IsTrue(ThicketMasterPhaseRules.Enraged(bits));
        Assert.AreEqual(ThicketMasterPhaseRules.EnragedGlow, ThicketMasterPhaseRules.TargetGlow(2, true));
    }

    [Test]
    public void IntroRoar_AndSleep_KeepLevelOne_RunesDark()
    {
        var sleeping = new ThicketMasterMemory();
        Assert.AreEqual(1, ThicketMasterPhaseRules.Level(ThicketMasterPhaseRules.ShownBits(sleeping, false, default, 10f)));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Glow(1, false, 10f, ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.None));
        var intro = Roar(Simulation.ThicketRoarIntroBit, 150);
        var m = Memory(Simulation.ThicketRoarIntroBit);
        Assert.AreEqual(0, ThicketMasterPhaseRules.ShownBits(m, true, intro, 160f));
        Assert.AreEqual(ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.ChangeTick(true, intro, 160f));
    }

    // ------------------------------------------------------------ яркость

    [Test]
    public void Glow_FlashesOnTheRoar_ThenSettles()
    {
        const int r = 300;
        float target = ThicketMasterPhaseRules.Phase2Glow;
        for (int t = 0; t <= ThicketMasterPhaseRules.FlashTicks; t++)
        {
            float k = ThicketMasterPhaseRules.Glow(2, false, r + t, r, ThicketMasterPhaseRules.None);
            Assert.That(k, Is.InRange(target * ThicketMasterPhaseRules.FlashGain * (1f - ThicketMasterPhaseRules.BreathDepth) - 1e-4f,
                target * ThicketMasterPhaseRules.FlashGain * (1f + ThicketMasterPhaseRules.BreathDepth) + 1e-4f), $"вспышка на R+{t}");
        }
        float settled = ThicketMasterPhaseRules.Glow(2, false, r + 40, r, ThicketMasterPhaseRules.None);
        Assert.That(settled, Is.InRange(target * (1f - ThicketMasterPhaseRules.BreathDepth) - 1e-4f,
            target * (1f + ThicketMasterPhaseRules.BreathDepth) + 1e-4f));
        float mid = ThicketMasterPhaseRules.Glow(2, false, r + 10, r, ThicketMasterPhaseRules.None);
        float peak = ThicketMasterPhaseRules.Glow(2, false, r + 2, r, ThicketMasterPhaseRules.None);
        Assert.Less(mid / (1f + ThicketMasterPhaseRules.BreathDepth), peak / (1f - ThicketMasterPhaseRules.BreathDepth));
        // Привязка с середины боя: без вспышки.
        float bound = ThicketMasterPhaseRules.Glow(3, true, r + 2, ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.None);
        Assert.That(bound, Is.InRange(ThicketMasterPhaseRules.Phase3Glow * (1f - ThicketMasterPhaseRules.BreathDepth) - 1e-4f,
            ThicketMasterPhaseRules.Phase3Glow * (1f + ThicketMasterPhaseRules.BreathDepth) + 1e-4f));
    }

    [Test]
    public void Glow_BreathsFromTheSimTick_SameTickSameValue()
    {
        float a = ThicketMasterPhaseRules.Glow(2, true, 1234.5f, ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.None);
        float b = ThicketMasterPhaseRules.Glow(2, true, 1234.5f, ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.None);
        Assert.AreEqual(a, b);
        float quarter = ThicketMasterPhaseRules.Glow(2, true, ThicketMasterPhaseRules.BreathPeriodTicks / 4f,
            ThicketMasterPhaseRules.None, ThicketMasterPhaseRules.None);
        Assert.That(quarter, Is.EqualTo(ThicketMasterPhaseRules.EnragedGlow * (1f + ThicketMasterPhaseRules.BreathDepth)).Within(1e-4));
    }

    [Test]
    public void Glow_FadesToZero_OneAndAHalfSecondsAfterDeath()
    {
        const int death = 2000;
        float alive = ThicketMasterPhaseRules.Glow(3, true, death, ThicketMasterPhaseRules.None, death);
        Assert.Greater(alive, 1f);
        float half = ThicketMasterPhaseRules.Glow(3, true, death + ThicketMasterPhaseRules.DeathFadeTicks / 2f, ThicketMasterPhaseRules.None, death);
        Assert.Less(half, alive);
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Glow(3, true, death + 45, ThicketMasterPhaseRules.None, death));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Glow(3, true, death + 90, ThicketMasterPhaseRules.None, death));
    }

    // ------------------------------------------------------------ рост накладок

    [Test]
    public void Bloom_And_Swell_GrowFromTheChange()
    {
        Assert.AreEqual(1f, ThicketMasterPhaseRules.Bloom(10f, ThicketMasterPhaseRules.None, .2f, .5f));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Bloom(300f, 300, .1f, .5f));
        Assert.AreEqual(1f, ThicketMasterPhaseRules.Bloom(300f + 30f * .6f, 300, .1f, .5f));
        Assert.That(ThicketMasterPhaseRules.Bloom(300f + 30f * .35f, 300, .1f, .5f), Is.EqualTo(.5f).Within(1e-4));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Swell(0f));
        Assert.AreEqual(1f, ThicketMasterPhaseRules.Swell(1f));
        Assert.That(ThicketMasterPhaseRules.Swell(.7f), Is.EqualTo(1.15f).Within(1e-4));
        float last = 0f;
        for (int i = 1; i <= 70; i++)
        {
            float s = ThicketMasterPhaseRules.Swell(i / 100f);
            Assert.That(s, Is.GreaterThanOrEqualTo(last - 1e-5f), "набухание растёт до пика");
            last = s;
        }
        Assert.AreEqual(1f, ThicketMasterPhaseRules.EyeAppear(5f, ThicketMasterPhaseRules.None));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.EyeAppear(300f, 300));
        Assert.AreEqual(1f, ThicketMasterPhaseRules.EyeAppear(306f, 300));
    }

    // ------------------------------------------------------------ лунная кромка (ревью 02.10 вечер, находка 9)

    [Test]
    public void Rim_SameInEveryPhase_FadesWithDeath()
    {
        float alive = ThicketMasterPhaseRules.Rim(1234.5f, ThicketMasterPhaseRules.None);
        Assert.AreEqual(ThicketMasterPhaseRules.RimStrength, alive);
        Assert.Greater(alive, 0f, "кромка есть и во сне, и в Ф1");
        const int death = 2000;
        Assert.AreEqual(alive, ThicketMasterPhaseRules.Rim(death, death));
        float half = ThicketMasterPhaseRules.Rim(death + ThicketMasterPhaseRules.DeathFadeTicks / 2f, death);
        Assert.That(half, Is.EqualTo(alive * .5f).Within(1e-4));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Rim(death + ThicketMasterPhaseRules.DeathFadeTicks, death));
        Assert.AreEqual(0f, ThicketMasterPhaseRules.Rim(death + 600, death));
    }

    [Test]
    public void Rim_IsACoolSubtleEdge_NotAWhitening()
    {
        // Голубой: синий канал сильнее красного и зелёного — луна, а не белая подсветка.
        Assert.Greater(ThicketMasterPhaseRules.RimBlue, ThicketMasterPhaseRules.RimGreen);
        Assert.Greater(ThicketMasterPhaseRules.RimGreen, ThicketMasterPhaseRules.RimRed);
        // На самом краю в полной тени — не ярче ~0,15 линейной яркости (sRGB ≈ 0,42): край, а не свет на теле.
        float peak = ThicketMasterPhaseRules.RimPeakLuminance(ThicketMasterPhaseRules.RimStrength);
        Assert.That(peak, Is.InRange(.08f, .15f));
    }

    // ------------------------------------------------------------ живая симуляция

    [Test]
    public void LiveFight_DressChangesExactlyOnThresholdRoarImpacts()
    {
        var sim = new Simulation(77, 64);
        sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
        var e = sim.Entities;
        e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
        e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
        e.RefreshStats(0);
        e.Health[0] = e.MaxHealth[0];

        int level = 1, changes66 = -1, changes33 = -1;
        int impact66 = -1, impact33 = -1;
        bool hurt66 = false, hurt33 = false;
        while (sim.Tick < 3000 && (impact66 < 0 || impact33 < 0))
        {
            // Ранить после вступления: порог 66, потом — когда одежда сменилась — 33.
            if (!hurt66 && sim.ThicketMasterAwake(Boss) && sim.Tick > 400)
            {
                e.Health[Boss] = e.MaxHealth[Boss] * 60 / 100;
                hurt66 = true;
            }
            if (!hurt33 && level == 2 && sim.Tick > changes66 + 60)
            {
                e.Health[Boss] = e.MaxHealth[Boss] * 30 / 100;
                hurt33 = true;
            }
            e.Health[0] = e.MaxHealth[0];
            sim.Step(InputFrame.Empty);
            float tick = sim.Tick - 1;
            sim.TryGetThicketMasterMemory(Boss, out var m);
            bool acting = sim.TryGetThicketMasterAction(Boss, out var a);
            int now = ThicketMasterPhaseRules.Level(ThicketMasterPhaseRules.ShownBits(m, acting, a, tick));
            foreach (var ev in sim.Events)
            {
                if (ev.Type != SimEventType.EnemyActionImpact || ev.Source != Boss
                    || (EnemyActionKind)ev.ActionVariant != EnemyActionKind.ThicketRoar) continue;
                if (acting && (a.Tag & R66) != 0 && impact66 < 0) impact66 = (int)tick;
                if (acting && (a.Tag & R33) != 0 && impact33 < 0) impact33 = (int)tick;
            }
            if (now > level)
            {
                if (now == 2) changes66 = (int)tick;
                if (now == 3) changes33 = (int)tick;
                Assert.AreEqual((int)tick, ThicketMasterPhaseRules.ChangeTick(acting, a, tick), "тик смены — тик удара рёва");
            }
            Assert.That(now, Is.GreaterThanOrEqualTo(level), "одежда назад не возвращается");
            level = now;
        }
        Assert.That(impact66, Is.GreaterThan(0), "рёв 66 ударил");
        Assert.That(impact33, Is.GreaterThan(0), "рёв 33 ударил");
        Assert.AreEqual(impact66, changes66, "Ф2 — в тик удара рёва 66");
        Assert.AreEqual(impact33, changes33, "Ф3 — в тик удара рёва 33");
        Assert.AreEqual(3, level);
    }
}
