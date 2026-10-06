using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Махи Крушения v4 «холодное железо», вид V2 (06.10, swing1.png / swing2.png): PelagWreckSwingRules,
// PelagWreckSwingLook, PelagWreckSwingImpact. Сроки серпа и удары по задетым — только из Sim (сектор маха
// TryGetWreckSweep, события Damage / WreckStage): тики серии в тестах не зашиты. Серп и путь — WreckSwingSweepTests.
// NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckSwingRulesTests
{
    private const int P = Simulation.PlayerId;

    private static Fix64 M(double v) => Fix64.Ratio((int)Math.Round(v * 1000), 1000);

    private static Simulation Arena()
    {
        var sim = new Simulation(1234, 128);
        sim.SetupTestArena(0);
        sim.Entities.Position[P] = FixVec2.Zero;
        sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
        sim.SetAbility(0, AbilityDefinition.Wreck(), new AbilityNode[0], 0);
        sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
        return sim;
    }

    /// <summary>Хранитель на <paramref name="distance"/> под углом <paramref name="degrees"/> справа от взгляда +X.</summary>
    private static int GuardianAt(Simulation sim, double distance, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        int id = sim.Entities.Spawn(new FixVec2(M(distance * Math.Cos(rad)), M(-distance * Math.Sin(rad))), 100000, Faction.Orvill);
        sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
        sim.Entities.RefreshStats(id);
        sim.Entities.BodyRadius[id] = EnemyArchetypes.GuardianBodyRadius;
        sim.Entities.NextAttackTick[id] = int.MaxValue;
        return id;
    }

    private static InputFrame Press(bool press)
    {
        var input = InputFrame.Empty;
        input.AbilityMask = (byte)(press ? 1 : 0);
        input.AbilityHoldMask = (byte)(press ? 1 : 0);
        input.Aim = new FixVec2(M(5), Fix64.Zero);
        input.AttackTarget = -1;
        input.AbilityTarget = -1;
        return input;
    }

    private struct Hit { public int Tick, Target; public WreckVfxHit Kind; public int Stage; }

    /// <summary>
    /// Серия в самом быстром темпе (нажатие каждый тик) по толпе в секторе до удара оземь: удары как их видит вид —
    /// ClassifyHit по событиям тика, затем SweepStageAt / SweepHit по запомненным секторам махов (как NoteWreckSweep).
    /// </summary>
    private static List<Hit> SeriesHits(Simulation sim, Dictionary<int, (int start, int contact)> stages, out int from0, out int to0, out int from1, out int to1)
    {
        var hits = new List<Hit>();
        var bySwing1 = new HashSet<int>();
        from0 = from1 = int.MaxValue;
        to0 = to1 = int.MinValue;
        for (int i = 0; i < 40; i++)
        {
            int tick = sim.Tick;
            int stageBefore = sim.Wreck.Stage, serialBefore = sim.Wreck.Serial;
            sim.Step(Press(true));
            if (sim.TryGetWreckSweep(out int s, out _, out _, out int f, out int t) && s == 0) { from0 = f; to0 = t; }
            if (sim.TryGetWreckSweep(out s, out _, out _, out f, out t) && s == 1) { from1 = f; to1 = t; }
            if (sim.Wreck.Stage != stageBefore || sim.Wreck.Serial != serialBefore)
                stages[sim.Wreck.Stage] = (sim.Wreck.StageStartTick, sim.Wreck.ContactTick);
            WreckVfxCue cue = WreckVfxCue.None;
            bool slam = false;
            foreach (SimEvent e in sim.Events)
            {
                if (e.Source != P) continue;
                if (e.Type == SimEventType.WreckStage) cue = e.Amount < 2 ? WreckVfxCue.Swing : e.Amount == 2 ? WreckVfxCue.Slam : WreckVfxCue.Fourth;
                slam |= cue == WreckVfxCue.Slam;
                if (e.Type != SimEventType.Damage || cue == WreckVfxCue.Slam) continue;
                WreckVfxHit kind = PelagWreckVfxRules.ClassifyHit(cue, cue == WreckVfxCue.None ? -1 : tick, -1, tick, e.Target, false, -1, 0, false);
                int sweep = PelagWreckSwingRules.SweepStageAt(tick, from0, to0, from1, to1, bySwing1.Contains(e.Target));
                kind = PelagWreckSwingRules.SweepHit(kind, sweep, out int stage);
                if (stage == 0) bySwing1.Add(e.Target);
                hits.Add(new Hit { Tick = tick, Target = e.Target, Kind = kind, Stage = stage });
            }
            // Выпад не обрывает мах 2 (голова летит дальше) — до удара оземь; его круг и вал — не махи.
            if (slam) break;
        }
        return hits;
    }

    [Test]
    public void Weights_SwingLight_BackswingAndFourthHeavy_LungeHasNoCrescent()
    {
        Assert.That(PelagWreckSwingRules.WeightOf(PelagWreckSwingRules.StageSwing), Is.EqualTo(WreckSwingWeight.Light));
        Assert.That(PelagWreckSwingRules.WeightOf(PelagWreckSwingRules.StageBackswing), Is.EqualTo(WreckSwingWeight.Heavy));
        Assert.That(PelagWreckSwingRules.WeightOf(PelagWreckSwingRules.StageFourth), Is.EqualTo(WreckSwingWeight.Heavy), "«Четвёртый» — как мах 2");
        Assert.That(PelagWreckSwingRules.Draws(PelagWreckSwingRules.StageSlam), Is.False, "выпад — своя земля, не серп");
        Assert.That(PelagWreckSwingRules.HitWeight(WreckVfxHit.Swing, 0), Is.EqualTo(WreckSwingWeight.Light));
        Assert.That(PelagWreckSwingRules.HitWeight(WreckVfxHit.Swing, 1), Is.EqualTo(WreckSwingWeight.Heavy));
        Assert.That(PelagWreckSwingRules.HitWeight(WreckVfxHit.Fourth, 0), Is.EqualTo(WreckSwingWeight.Heavy));
        foreach (WreckVfxHit hit in new[] { WreckVfxHit.Circle, WreckVfxHit.Wave, WreckVfxHit.Wall, WreckVfxHit.Crash, WreckVfxHit.Burst, WreckVfxHit.Other })
            Assert.That(PelagWreckSwingRules.HitWeight(hit, 1), Is.EqualTo(WreckSwingWeight.None), hit + " — не мах");
    }

    [Test]
    public void FastestSeries_EveryHeadPassHit_IsASwingOfItsStage_WhileItsCrescentRecords()
    {
        Simulation sim = Arena();
        var foes = new HashSet<int>();
        foreach (double deg in new[] { 72.0, 45.0, 20.0, 0.0, -20.0, -45.0, -70.0 }) foes.Add(GuardianAt(sim, 2.2, deg));
        var stages = new Dictionary<int, (int start, int contact)>();
        List<Hit> hits = SeriesHits(sim, stages, out int from0, out int to0, out int from1, out int to1);
        Assert.That(stages.ContainsKey(0) && stages.ContainsKey(1), Is.True, "оба маха были");
        Assert.That(from0, Is.LessThan(stages[0].contact), "голова входит в сектор до удара");
        Assert.That(to1, Is.GreaterThan(stages[1].contact), "и уходит после");
        int swings = 0, offContact = 0;
        var perFoe = new Dictionary<int, List<int>>();
        foreach (Hit h in hits)
        {
            if (!foes.Contains(h.Target)) continue;
            if (!perFoe.ContainsKey(h.Target)) perFoe[h.Target] = new List<int>();
            perFoe[h.Target].Add(h.Stage);
            Assert.That(h.Kind, Is.EqualTo(WreckVfxHit.Swing), $"удар тика {h.Tick} — мах, не «прочее» (без шара и без искры старого пути)");
            Assert.That(h.Stage == 0 || h.Stage == 1, Is.True, $"тик {h.Tick}: этап маха");
            (int start, int contact) st = stages[h.Stage];
            int from = h.Stage == 0 ? from0 : from1, to = h.Stage == 0 ? to0 : to1;
            float recordFrom = PelagWreckSwingRules.RecordFrom(st.start, from), until = PelagWreckSwingRules.RecordUntil(to);
            Assert.That(PelagWreckSwingRules.Recording(h.Tick, recordFrom, until), Is.True, $"тик {h.Tick}: серп этапа {h.Stage} пишется, когда бьёт");
            swings++;
            if (h.Tick != st.contact) offContact++;
        }
        Assert.That(perFoe.Count, Is.GreaterThanOrEqualTo(4), "толпа в секторе задета");
        foreach (KeyValuePair<int, List<int>> f in perFoe)
            CollectionAssert.AreEqual(new[] { 0, 1 }, f.Value, $"тело {f.Key}: мах 1, потом мах 2 — по разу (и на стыке махов)");
        Assert.That(swings, Is.EqualTo(2 * perFoe.Count));
        Assert.That(offContact, Is.GreaterThan(0), "есть удары вне тика контакта — их и чинит SweepHit");
        // Мах 1 справа налево: правый край раньше левого; мах 2 — наоборот (сабельный ритм v4).
        Assert.That(Simulation.WreckSwingSide(0), Is.EqualTo(1));
        Assert.That(Simulation.WreckSwingSide(1), Is.EqualTo(-1));
    }

    [Test]
    public void SweepHit_TouchesOnlyOtherAndSwing_InsideASector()
    {
        Assert.That(PelagWreckSwingRules.SweepStageAt(5, 2, 9, int.MaxValue, int.MinValue, false), Is.EqualTo(0));
        Assert.That(PelagWreckSwingRules.SweepStageAt(8, 2, 9, 8, 15, false), Is.EqualTo(0), "на стыке: мах 1 его ещё не бил — добивает мах 1");
        Assert.That(PelagWreckSwingRules.SweepStageAt(8, 2, 9, 8, 15, true), Is.EqualTo(1), "на стыке: мах 1 уже бил — мах 2");
        Assert.That(PelagWreckSwingRules.SweepStageAt(7, 2, 9, 8, 15, true), Is.EqualTo(0));
        Assert.That(PelagWreckSwingRules.SweepStageAt(12, 2, 9, 8, 15, false), Is.EqualTo(1));
        Assert.That(PelagWreckSwingRules.SweepStageAt(16, 2, 9, 8, 15, false), Is.EqualTo(-1));
        Assert.That(PelagWreckSwingRules.SweepHit(WreckVfxHit.Other, 0, out int st), Is.EqualTo(WreckVfxHit.Swing));
        Assert.That(st, Is.EqualTo(0));
        Assert.That(PelagWreckSwingRules.SweepHit(WreckVfxHit.Other, -1, out st), Is.EqualTo(WreckVfxHit.Other));
        Assert.That(st, Is.EqualTo(-1));
        foreach (WreckVfxHit hit in new[] { WreckVfxHit.Circle, WreckVfxHit.Wave, WreckVfxHit.Wall, WreckVfxHit.Crash, WreckVfxHit.Burst, WreckVfxHit.Fourth })
        {
            Assert.That(PelagWreckSwingRules.SweepHit(hit, 1, out st), Is.EqualTo(hit), hit + " не трогается");
            Assert.That(st, Is.EqualTo(-1));
        }
    }

    [Test]
    public void SweepDirection_FollowsTheSimSide_Swing1RightToLeft_Swing2Mirrored()
    {
        // Взгляд +X мира (x, z), враг впереди: мах 1 справа налево — касательная к левому краю взгляда.
        PelagWreckSwingRules.SweepDirection(2f, 0f, 0, out float x1, out float z1);
        PelagWreckSwingRules.SweepDirection(2f, 0f, 1, out float x2, out float z2);
        Assert.That(x1, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(z1 * z2, Is.LessThan(0f), "мах 2 — зеркально");
        Assert.That(Math.Abs(z1), Is.EqualTo(1f).Within(1e-5f));
        // Против часовой сверху (угол atan2(z, x) растёт) у маха 1 — тот же знак, что ждёт PelagWreckSwingRules.Against.
        Assert.That(z1, Is.GreaterThan(0f));
        Assert.That(PelagWreckSwingRules.Against(Simulation.WreckSwingSide(0), -10f), Is.True, "мах 1 пошёл по часовой — назад");
        Assert.That(PelagWreckSwingRules.Against(Simulation.WreckSwingSide(0), 10f), Is.False);
        Assert.That(PelagWreckSwingRules.Against(Simulation.WreckSwingSide(1), 10f), Is.True);
        Assert.That(PelagWreckSwingRules.Against(0, 10f), Is.False, "у выпада стороны нет");
    }

    [Test]
    public void Timing_FromTheSimSector_ShortWindow_HeavyLonger_ErodeQuick()
    {
        Assert.That(PelagWreckSwingRules.RecordFrom(10, 12), Is.EqualTo(11f));
        Assert.That(PelagWreckSwingRules.RecordFrom(10, 10), Is.EqualTo(10f), "не раньше нажатия");
        Assert.That(PelagWreckSwingRules.FallbackFrom(20), Is.EqualTo(20 + Simulation.WreckSweepFirstOffset));
        Assert.That(PelagWreckSwingRules.FallbackTo(20), Is.EqualTo(20 + Simulation.WreckSweepLastOffset));
        Assert.That(PelagWreckSwingRules.WindowSeconds(WreckSwingWeight.Light), Is.InRange(.12f, .15f));
        Assert.That(PelagWreckSwingRules.WindowSeconds(WreckSwingWeight.Heavy), Is.InRange(.12f, .15f));
        Assert.That(PelagWreckSwingRules.WindowSeconds(WreckSwingWeight.Heavy), Is.GreaterThan(PelagWreckSwingRules.WindowSeconds(WreckSwingWeight.Light)));
        foreach (WreckSwingWeight w in new[] { WreckSwingWeight.Light, WreckSwingWeight.Heavy })
        {
            Assert.That(PelagWreckSwingRules.Erode(30f, 30f, w), Is.EqualTo(0f));
            float end = 30f + PelagWreckSwingRules.Ticks(PelagWreckSwingRules.ErodeSeconds(w));
            Assert.That(PelagWreckSwingRules.Erode(end, 30f, w), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PelagWreckSwingRules.Done(end + 1f, 30f, w), Is.True);
            Assert.That(PelagWreckSwingRules.ErodeSeconds(w), Is.LessThan(PelagWreckIronRules.HoldSeconds), "мах тает раньше земли выпада");
        }
    }

    [Test]
    public void FormColours_OneTable_MatchTheHudLinks_WithoutRedOrangeOrGold()
    {
        Assert.That(PelagWreckSwingLook.Tint(PelagForm.None), Is.EqualTo(0x4FA8FF));
        Assert.That(PelagWreckSwingLook.Light(PelagForm.None), Is.EqualTo(0x9CD8FF), "ядро базы #4FA8FF → #9CD8FF");
        Assert.That(PelagWreckSwingLook.Tint(PelagForm.WreckBreakwater), Is.EqualTo(0x1FB37E));
        Assert.That(PelagWreckSwingLook.Tint(PelagForm.WreckNinthWave), Is.EqualTo(0x4B3FD0));
        Assert.That(PelagWreckSwingLook.Tint(PelagForm.WreckShell), Is.EqualTo(0xE4EEF6));
        foreach (PelagForm form in new[] { PelagForm.None, PelagForm.WreckBreakwater, PelagForm.WreckNinthWave, PelagForm.WreckShell })
        {
            Assert.That(PelagWreckSwingLook.Tint(form), Is.EqualTo(HudWreckSeriesRules.FormHex(form)), form + ": тот же тон, что звенья серии в HUD");
            foreach (int hex in new[] { PelagWreckSwingLook.Tint(form), PelagWreckSwingLook.Light(form) })
            {
                PelagWreckSwingLook.HueSat(hex, out float hue, out float sat);
                Assert.That(sat < .15f || (hue > 70f && hue < 300f), Is.True, $"{form} #{hex:X6}: тон {hue:F0}° — не красный, оранжевый, золото");
                PelagWreckSwingLook.Rgb(hex, out _, out float g, out float b);
                PelagWreckSwingLook.Rgb(PelagWreckSwingLook.Tint(form), out float r0, out float g0, out float b0);
                PelagWreckSwingLook.Rgb(PelagWreckSwingLook.Light(form), out float r1, out float g1, out float b1);
                Assert.That(r1 + g1 + b1, Is.GreaterThanOrEqualTo(r0 + g0 + b0), form + ": к кромке светлее");
            }
        }
        PelagWreckSwingLook.Rgb(PelagWreckSwingLook.OutlineHex, out float ro, out float go, out float bo);
        Assert.That(Math.Max(ro, Math.Max(go, bo)), Is.LessThan(.1f), "контур тёмный");
        PelagWreckSwingLook.Rgb(PelagWreckSwingLook.IronHex, out float ri, out float gi, out float bi);
        Assert.That(Math.Max(ri, Math.Max(gi, bi)), Is.LessThan(.25f), "скол — тёмное железо");
        Assert.That(bi, Is.GreaterThanOrEqualTo(ri), "железо холодное");
    }

    [Test]
    public void Impact_StreakAndChips_NoBall_HeavyHitsHarder_RecoilSmall()
    {
        PelagWreckSwingImpact.Numbers light = PelagWreckSwingImpact.For(WreckSwingWeight.Light);
        PelagWreckSwingImpact.Numbers heavy = PelagWreckSwingImpact.For(WreckSwingWeight.Heavy);
        foreach (PelagWreckSwingImpact.Numbers n in new[] { light, heavy })
        {
            Assert.That(n.StreakSeconds, Is.LessThanOrEqualTo(.12f), "росчерк короткий");
            Assert.That(n.StreakLength / n.StreakWidth, Is.GreaterThan(4f), "росчерк вытянут по ходу — не шар");
            Assert.That(PelagWreckSwingImpact.StreakStartSize(n.StreakLength) * PelagWreckSwingImpact.StreakAspect, Is.EqualTo(n.StreakLength).Within(1e-5f));
            Assert.That(n.Chips, Is.GreaterThan(0));
            Assert.That(n.Dust, Is.GreaterThan(0));
            Assert.That(n.RecoilMeters, Is.LessThan(.5f), "отброс — вид, тело не уезжает от Sim");
        }
        Assert.That(heavy.StreakLength, Is.GreaterThan(light.StreakLength));
        Assert.That(heavy.Chips, Is.GreaterThan(light.Chips));
        Assert.That(heavy.ChipSizeMax, Is.GreaterThan(light.ChipSizeMax), "мах 2 — крупнее сколы");
        Assert.That(heavy.StreakSlivers, Is.GreaterThan(light.StreakSlivers));
        Assert.That(heavy.RecoilMeters, Is.GreaterThan(light.RecoilMeters));
        Assert.That(PelagWreckSwingImpact.RecoilScale(WreckSwingWeight.Light), Is.GreaterThan(1f), "отброс читается сильнее прежней отдачи");
        // Толчок камеры выпада — .30, «Четвёртого» — .26: махи — легче.
        Assert.That(heavy.PunchTrauma, Is.LessThan(.26f));
        Assert.That(light.PunchTrauma, Is.LessThan(heavy.PunchTrauma));
    }
}
