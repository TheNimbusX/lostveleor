using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение «холодное железо» (база 06.10), вид — PelagWreckIronRules. Правило владельца 02.10:
// видимый край = край урона Sim. Камни круга — внутри ImpactRadius, камни, осколки и звенья
// полосы — внутри полуширины и до конца полосы; кусок полосы встаёт не раньше, чем до него
// дошёл фронт Sim (TryGetWreckWave), и не позже того же тика; всё оседает за ~1,35 с.
// NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckIronRulesTests
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

    /// <summary>Серия до удара оземь: тик события удара (тик до шага) и снимок Sim после шага.</summary>
    private static (int tick, SimEvent slam, WreckState w) RunToSlam(Simulation sim)
    {
        for (int i = 0; i < 60; i++)
        {
            int tick = sim.Tick;
            sim.Step(Press(i < 20));
            foreach (SimEvent e in sim.Events)
                if (e.Type == SimEventType.WreckSlam) return (tick, e, sim.Wreck);
        }
        Assert.Fail("удара оземь не было");
        return default;
    }

    private static List<PelagWreckIronRules.Stone> Lane(int serial, WreckState w, int waveTick)
    {
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.LaneStones(serial, w.WaveStart.ToFloat(), w.WallEnd.ToFloat(), w.LaneHalfWidth.ToFloat(),
            waveTick, w.WaveStep.ToFloat(), into, 0);
        var list = new List<PelagWreckIronRules.Stone>();
        for (int i = 0; i < n; i++) list.Add(into[i]);
        return list;
    }

    [Test]
    public void LanePieces_RiseInTheTickTheSimFrontReachesThem()
    {
        Simulation sim = Arena();
        (int slamTick, SimEvent slam, WreckState w) = RunToSlam(sim);
        Assert.That(w.WaveTick, Is.EqualTo(slamTick), "фронт — с тика удара");
        float start = w.WaveStart.ToFloat(), step = w.WaveStep.ToFloat();
        var pieces = Lane(w.Serial, w, slamTick);
        int links = PelagWreckIronRules.LinkCount(start, w.WallEnd.ToFloat());
        var along = new List<(float along, float arrival)>();
        foreach (var s in pieces) along.Add((s.Along, s.ArrivalTick));
        for (int i = 0; i < links; i++)
            along.Add((PelagWreckIronRules.LinkAlong(i, start), PelagWreckIronRules.LinkArrivalTick(i, slamTick, start, step)));
        Assert.That(along.Count, Is.GreaterThan(10));

        // Шаги Sim после удара: фронт этого шага (TryGetWreckWave) и тик показа этого шага (= тик события шага).
        int shownOfStep = slamTick;
        for (int k = 0; k < slam.Amount + 2; k++)
        {
            if (!sim.TryGetWreckWave(out _, out _, out _, out Fix64 reachNow, out _))
            {
                Assert.That(k, Is.GreaterThanOrEqualTo(slam.Amount), "фронт погас раньше своих шагов");
                break;
            }
            float reach = reachNow.ToFloat();
            foreach ((float a, float arrival) in along)
            {
                bool shown = shownOfStep >= arrival - 1e-4f;
                if (shown) Assert.That(reach, Is.GreaterThanOrEqualTo(a - 1e-3f), $"кусок на {a:F2} м виден раньше фронта ({reach:F2} м)");
                else Assert.That(reach, Is.LessThan(a + 1e-3f), $"фронт дошёл до {a:F2} м, а кусок не встал");
            }
            shownOfStep = sim.Tick;
            sim.Step(Press(false));
        }
    }

    [Test]
    public void LanePieces_StayInsideTheDamageLane()
    {
        Simulation sim = Arena();
        (int slamTick, _, WreckState w) = RunToSlam(sim);
        float start = w.WaveStart.ToFloat(), end = w.WallEnd.ToFloat(), half = w.LaneHalfWidth.ToFloat();
        Assert.That(half, Is.EqualTo(.75f).Within(.01f), "полоса 1,5 м");
        foreach (var s in Lane(w.Serial, w, slamTick))
        {
            Assert.That(Math.Abs(s.Across) + s.Size * .5f, Is.LessThanOrEqualTo(half + 1e-3f), "камень за краем полосы");
            Assert.That(s.Along + s.Size * .5f, Is.LessThanOrEqualTo(end + 1e-3f), "камень за концом полосы");
            Assert.That(s.Along, Is.GreaterThanOrEqualTo(start), "камень полосы позади точки удара");
        }
        int links = PelagWreckIronRules.LinkCount(start, end);
        Assert.That(links, Is.InRange(3, 4), "3–4 крупных звена");
        Assert.That(PelagWreckIronRules.LinkAlong(links - 1, start) + PelagWreckIronRules.LinkLength * .5f, Is.LessThanOrEqualTo(end + 1e-3f));
        Assert.That(PelagWreckIronRules.LinkLength * PelagWreckIronRules.LinkWidthOfLength * .5f, Is.LessThan(half), "звено уже полосы");
    }

    [TestCase(1.2f), TestCase(.9f), TestCase(1.8f)]
    public void CraterStones_StayInsideTheImpactCircle_AndLeaveTheLaneExitOpen(float radius)
    {
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.CraterStones(7, radius, 100, into, 0);
        Assert.That(n, Is.EqualTo(PelagWreckIronRules.CraterRimStones + PelagWreckIronRules.CraterInnerStones));
        for (int i = 0; i < n; i++)
        {
            var s = into[i];
            float r = (float)Math.Sqrt(s.Along * s.Along + s.Across * s.Across);
            Assert.That(r + s.Size * .5f, Is.LessThanOrEqualTo(radius + 1e-3f), "камень за кругом удара");
            if (i < PelagWreckIronRules.CraterRimStones)
            {
                float deg = Math.Abs((float)(Math.Atan2(s.Across, s.Along) * 180.0 / Math.PI));
                Assert.That(deg, Is.GreaterThanOrEqualTo(PelagWreckIronRules.RimGapDegrees - 1f), "кромка закрыла выход полосы");
                Assert.That(r, Is.GreaterThan(radius * .5f), "кромка — у края круга");
            }
            Assert.That(s.ArrivalTick, Is.InRange(99.4f, 101.1f), "круг раскрывается за полтора тика");
        }
    }

    [Test]
    public void CraterArrival_IsTheInverseOfTheCraterCrest()
    {
        foreach (float q in new[] { .1f, .4f, .7f, 1f })
        {
            float tick = PelagWreckIronRules.CraterArrivalTick(q * 1.2f, 50, 1.2f);
            Assert.That(PelagWreckVfxRules.CraterCrest(tick, 50, 1.2f), Is.EqualTo(q * 1.2f).Within(.01f));
        }
    }

    [Test]
    public void Pieces_RiseWithOvershoot_HoldAndSinkWithinOneAndAHalfSeconds()
    {
        Assert.That(PelagWreckIronRules.Rise(0f), Is.EqualTo(0f));
        Assert.That(PelagWreckIronRules.Rise(PelagWreckIronRules.RiseSeconds), Is.GreaterThan(1.05f), "камень выбило — перелёт");
        Assert.That(PelagWreckIronRules.Rise(.5f), Is.EqualTo(1f));
        Assert.That(PelagWreckIronRules.Sink(.9f), Is.EqualTo(0f), "лежит до секунды");
        float gone = PelagWreckIronRules.HoldSeconds + PelagWreckIronRules.FadeSeconds;
        Assert.That(gone, Is.InRange(1f, 1.5f), "оседает за 1–1,5 с");
        Assert.That(PelagWreckIronRules.Sink(gone), Is.EqualTo(1f));
        Assert.That(PelagWreckIronRules.LinkGlow(gone), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(PelagWreckIronRules.StoneGlow(gone), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(PelagWreckIronRules.LinkGlow(.01f), Is.GreaterThan(PelagWreckIronRules.LinkGlow(.5f) * 2f), "вспышка в миг впечатывания");
    }

    [Test]
    public void SlamObject_OutlivesItsLastPiece()
    {
        Simulation sim = Arena();
        (int slamTick, SimEvent slam, WreckState w) = RunToSlam(sim);
        float life = PelagWreckIronRules.LifeSeconds(slam.Amount);
        float lastArrival = slamTick;
        foreach (var s in Lane(w.Serial, w, slamTick)) lastArrival = Math.Max(lastArrival, s.ArrivalTick);
        float lastLocal = life - PelagWreckIronRules.Seconds(lastArrival - slamTick);
        Assert.That(PelagWreckIronRules.Sink(lastLocal), Is.EqualTo(1f), "последний кусок осел до возврата в пул");
        Assert.That(life, Is.LessThan(2f));
    }

    [TestCase(2.2f, 2.9f, 0), TestCase(2.2f, 4.0f, 1), TestCase(2.2f, 4.2f, 2), TestCase(2.2f, 6.0f, 4)]
    public void LinkCount_FitsTheLaneLength(float start, float end, int expected)
    {
        Assert.That(PelagWreckIronRules.LinkCount(start, end), Is.EqualTo(expected));
    }

    [Test]
    public void Links_SitInAGroove_AndLaneRubbleLeavesThemClear()
    {
        // V6: звено вдавлено в паз — плашмя центр прута не выше земли, но верх виден; ребром над землёй только верхний прут.
        float tube = PelagWreckIronRules.LinkTube;
        float half = PelagWreckIronRules.LinkLength * PelagWreckIronRules.LinkWidthOfLength * .5f;
        Assert.That(PelagWreckIronRules.LinkLift(false), Is.LessThanOrEqualTo(0f), "плашмя — утоплено больше чем наполовину");
        Assert.That(PelagWreckIronRules.LinkLift(false) + tube, Is.GreaterThan(.05f), "верх прута над землёй");
        Assert.That(PelagWreckIronRules.LinkLift(true) + half, Is.InRange(tube, tube * 2f), "ребром — один брусок над землёй");
        Assert.That(PelagWreckIronRules.LinkAxisRadius + tube, Is.EqualTo(half).Within(1e-4f), "осевая линия паза — по мешу звена");
        Assert.That(PelagWreckIronRules.LinkAxisHalfStraight + PelagWreckIronRules.LinkAxisRadius + tube,
            Is.EqualTo(PelagWreckIronRules.LinkLength * .5f).Within(1e-4f));

        // Щебень внутри полосы — между краем и звеном, звено не закрывает.
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.LaneStones(5, 2.2f, 6f, .75f, 10, .5f, into, 0);
        int rubble = 0;
        for (int i = 0; i < n; i++)
        {
            if (into[i].Size > PelagWreckIronRules.LaneRubbleMax + 1e-3f) continue;
            if (Math.Abs(into[i].Across) > .75f - into[i].Size * .5f - .06f) continue;
            rubble++;
            Assert.That(Math.Abs(into[i].Across) - into[i].Size * .5f, Is.GreaterThanOrEqualTo(half), "щебень на звене");
        }
        Assert.That(rubble, Is.GreaterThan(3), "щебень между краем и звеньями");
    }

    [TestCase(11), TestCase(3), TestCase(42)]
    public void CraterRim_IsUnevenClusters_NotARing(int serial)
    {
        // V7 (06.10 «форма слишком чёткая»): кромка — кучки разного размера с промежутками, а не ровное кольцо плит.
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.CraterStones(serial, 1.2f, 100, into, 0);
        Assert.That(n, Is.LessThanOrEqualTo(PelagWreckIronRules.MaxStones));
        var angles = new List<float>();
        float minSize = float.MaxValue, maxSize = 0f, minR = float.MaxValue, maxR = 0f;
        int slabs = 0;
        for (int i = 0; i < PelagWreckIronRules.CraterRimStones; i++)
        {
            var s = into[i];
            float deg = (float)(Math.Atan2(s.Across, s.Along) * 180.0 / Math.PI);
            angles.Add(deg < 0f ? deg + 360f : deg);
            float r = (float)Math.Sqrt(s.Along * s.Along + s.Across * s.Across);
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
            minSize = Math.Min(minSize, s.Size); maxSize = Math.Max(maxSize, s.Size);
            if (s.Look == PelagWreckIronRules.LookSlab) slabs++;
        }
        angles.Sort();
        float widest = 0f;
        for (int i = 1; i < angles.Count; i++) widest = Math.Max(widest, angles[i] - angles[i - 1]);
        Assert.That(widest, Is.GreaterThan(24f), "между кучками кромки — промежуток разбитой земли");
        Assert.That(maxSize / minSize, Is.GreaterThan(2.5f), "крупные плиты вперемешку с мелочью");
        Assert.That(maxR - minR, Is.GreaterThan(.2f), "кромка не по одной окружности");
        Assert.That(slabs, Is.GreaterThanOrEqualTo(PelagWreckIronRules.RimClusters), "в каждой кучке — плита");
    }

    [TestCase(5), TestCase(11), TestCase(77)]
    public void LaneEdges_AreJaggedClusters_NotRows(int serial)
    {
        // V7: край полосы ходит (LaneEdge), шаг неровный, размеры разные — не прямоугольник с рядами плит.
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.LaneStones(serial, 2.2f, 6f, .75f, 10, .5f, into, 0);
        Assert.That(n, Is.LessThanOrEqualTo(PelagWreckIronRules.MaxStones));
        float minEdge = 1f, maxEdge = 0f;
        for (float a = 2.2f; a <= 6f; a += .1f)
        {
            float e = PelagWreckIronRules.LaneEdge(serial, 1, a);
            Assert.That(e, Is.InRange(PelagWreckIronRules.LaneEdgeNarrow, PelagWreckIronRules.LaneEdgeWide));
            minEdge = Math.Min(minEdge, e); maxEdge = Math.Max(maxEdge, e);
        }
        Assert.That(maxEdge - minEdge, Is.GreaterThan(.12f), "край полосы ходит, а не прямая");
        var gaps = new List<float>();
        float minSize = float.MaxValue, maxSize = 0f;
        for (int side = -1; side <= 1; side += 2)
        {
            var along = new List<float>();
            for (int i = 0; i < n; i++)
            {
                var s = into[i];
                minSize = Math.Min(minSize, s.Size); maxSize = Math.Max(maxSize, s.Size);
                if (Math.Sign(s.Across) == side && s.Size >= .2f) along.Add(s.Along);
            }
            along.Sort();
            for (int i = 1; i < along.Count; i++) gaps.Add(along[i] - along[i - 1]);
        }
        Assert.That(maxSize / minSize, Is.GreaterThan(2.5f), "куски разного размера");
        Assert.That(gaps.Count, Is.GreaterThan(2));
        float mean = 0f, spread = 0f;
        foreach (float g in gaps) mean += g / gaps.Count;
        foreach (float g in gaps) spread += (g - mean) * (g - mean) / gaps.Count;
        Assert.That(Math.Sqrt(spread), Is.GreaterThan(.06f), "шаг кусков вдоль края неровный");
    }

    [Test]
    public void ShortLane_ByARock_StillKeepsPiecesBeforeTheRock()
    {
        var into = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        int n = PelagWreckIronRules.LaneStones(3, 2.2f, 3.4f, .75f, 10, .5f, into, 0);
        Assert.That(n, Is.GreaterThan(0));
        for (int i = 0; i < n; i++) Assert.That(into[i].Along + into[i].Size * .5f, Is.LessThanOrEqualTo(3.4f + 1e-3f));
    }
}
