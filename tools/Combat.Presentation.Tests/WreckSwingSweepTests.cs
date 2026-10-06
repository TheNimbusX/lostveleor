using System;
using System.Numerics;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Серп маха Крушения V2 (06.10): путь рукояти и головы кадр за кадром (PelagWreckSwingSweep), ряды между кадрами —
// круглая сплошная дуга при любом FPS (V1 рвался на штрихи), полоса от кулака до носа головы, звенья у кромки.
// Путь — круг, как голова на короткой цепи (0,45 м от кулака) вокруг героя: радиус головы 1,35 м, кулак 0,6 м,
// 650°/с (20–23° за тик Sim). NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckSwingSweepTests
{
    private const float HeadRadius = 1.35f, GripRadius = .6f, DegreesPerSecond = 650f, Hip = .95f;

    /// <summary>Рядов серпа — как PelagWreckSwingRibbon.Rows (меш вида, Unity).</summary>
    private const int Rows = 41;

    private static Vector3 OnCircle(float radius, float radians, float y) => new Vector3(radius * (float)Math.Cos(radians), y, radius * (float)Math.Sin(radians));

    private static float AngleAt(float seconds, int side) => side * DegreesPerSecond * seconds * (float)Math.PI / 180f;

    /// <summary>Путь кадрами показа с частотой <paramref name="fps"/> за <paramref name="seconds"/>; возвращает тик показа конца.</summary>
    private static float Feed(PelagWreckSwingSweep sweep, float fps, float seconds, int side, float windowTicks)
    {
        float t = 0f;
        for (int f = 0; t <= seconds + 1e-5f; f++)
        {
            t = f / fps;
            float a = AngleAt(t, side);
            float shown = PelagWreckSwingRules.Ticks(t);
            sweep.Add(shown, Vector3.Zero, OnCircle(GripRadius, a, 1.1f), OnCircle(HeadRadius, a, Hip));
            sweep.Trim(shown - windowTicks - 1f);
        }
        return PelagWreckSwingRules.Ticks(seconds);
    }

    [TestCase(24f)] [TestCase(30f)] [TestCase(60f)] [TestCase(144f)]
    public void Rows_StayOnTheRealArc_AndNeverBreakIntoStrokes(float fps)
    {
        var sweep = new PelagWreckSwingSweep();
        float window = PelagWreckSwingRules.WindowTicks(WreckSwingWeight.Heavy);
        float end = Feed(sweep, fps, .3f, 1, window);
        var grips = new Vector3[Rows];
        var heads = new Vector3[Rows];
        var times = new float[Rows];
        int n = sweep.Rows(end, window, grips, heads, times);
        Assert.That(n, Is.EqualTo(Rows));
        Assert.That(times[0] - times[n - 1], Is.EqualTo(window).Within(.05f), "серп — последние WindowSeconds хода");
        float maxStep = 0f;
        for (int k = 0; k < n; k++)
        {
            float r = (float)Math.Sqrt(heads[k].X * heads[k].X + heads[k].Z * heads[k].Z);
            Assert.That(r, Is.EqualTo(HeadRadius).Within(.015f), $"{fps} к/с, ряд {k}: голова на дуге, не на хорде");
            float g = (float)Math.Sqrt(grips[k].X * grips[k].X + grips[k].Z * grips[k].Z);
            Assert.That(g, Is.EqualTo(GripRadius).Within(.015f));
            Assert.That(heads[k].Y, Is.EqualTo(Hip).Within(1e-3f), "на высоте бедра");
            float expected = AngleAt(PelagWreckSwingRules.Seconds(times[k]), 1);
            float actual = (float)Math.Atan2(heads[k].Z, heads[k].X);
            float diff = (float)Math.IEEERemainder(actual - expected, 2 * Math.PI);
            Assert.That(Math.Abs(diff) * 180f / Math.PI, Is.LessThan(1.5f), $"{fps} к/с, ряд {k}: голова там, где была в этот миг");
            if (k > 0) maxStep = Math.Max(maxStep, Vector3.Distance(heads[k], heads[k - 1]));
        }
        float arc = DegreesPerSecond * PelagWreckSwingRules.WindowSeconds(WreckSwingWeight.Heavy) * (float)Math.PI / 180f * HeadRadius;
        Assert.That(maxStep, Is.LessThan(arc / (n - 1) * 1.2f), $"{fps} к/с: ряды ровным шагом — без дыр между кадрами");
    }

    [Test]
    public void Window_CoversTheLastSweep_AndAShortHistoryStillTapers()
    {
        var sweep = new PelagWreckSwingSweep();
        var grips = new Vector3[Rows];
        var heads = new Vector3[Rows];
        var times = new float[Rows];
        Assert.That(sweep.Rows(1f, 4f, grips, heads, times), Is.EqualTo(0), "кадров нет — серпа нет");
        sweep.Add(0f, Vector3.Zero, OnCircle(GripRadius, 0f, 1.1f), OnCircle(HeadRadius, 0f, Hip));
        Assert.That(sweep.Rows(1f, 4f, grips, heads, times), Is.EqualTo(0), "один кадр — серпа нет");
        sweep.Add(.5f, Vector3.Zero, OnCircle(GripRadius, .2f, 1.1f), OnCircle(HeadRadius, .2f, Hip));
        int n = sweep.Rows(.5f, 4f, grips, heads, times);
        Assert.That(n, Is.EqualTo(Rows), "короткий путь — серп по всему записанному");
        Assert.That(times[n - 1], Is.EqualTo(0f).Within(1e-5f));
        // Повтор того же кадра показа переписывает его, а не ломает путь.
        sweep.Add(.5f, Vector3.Zero, OnCircle(GripRadius, .21f, 1.1f), OnCircle(HeadRadius, .21f, Hip));
        Assert.That(sweep.Count, Is.EqualTo(2));
        sweep.Add(.4f, Vector3.Zero, OnCircle(GripRadius, .3f, 1.1f), OnCircle(HeadRadius, .3f, Hip));
        Assert.That(sweep.Count, Is.EqualTo(2), "кадр из прошлого — пропуск");
    }

    [Test]
    public void AngleUnwraps_AcrossMinusPi_AndOmegaFollowsTheSide()
    {
        foreach (int side in new[] { 1, -1 })
        {
            var sweep = new PelagWreckSwingSweep();
            for (int f = 0; f < 12; f++)
            {
                float a = (float)Math.PI - side * .3f + side * .1f * f;   // через ±π
                sweep.Add(f * .5f, Vector3.Zero, OnCircle(GripRadius, a, 1.1f), OnCircle(HeadRadius, a, Hip));
            }
            float omega = sweep.HeadOmega(Simulation.TicksPerSecond);
            Assert.That(omega * side, Is.EqualTo(.1f / .5f * Simulation.TicksPerSecond).Within(1e-3f), "угол развёрнут, без скачка на 2π");
            Assert.That(PelagWreckSwingRules.Against(side, omega), Is.False);
            Assert.That(PelagWreckSwingRules.Against(-side, omega), Is.True);
            Assert.That(sweep.Sample(2.75f, out _, out Vector3 head), Is.True);
            Assert.That(Math.Sqrt(head.X * head.X + head.Z * head.Z), Is.EqualTo(HeadRadius).Within(.01f));
        }
    }

    [Test]
    public void Band_FromNearTheFistToPastTheHead_ThickAtTheHead_NothingAtTheTail_Swing2Heavier()
    {
        foreach (WreckSwingWeight w in new[] { WreckSwingWeight.Light, WreckSwingWeight.Heavy })
        {
            Assert.That(PelagWreckSwingRules.Thickness(1f, w), Is.EqualTo(0f).Within(1e-6f), "хвост сходит на нет");
            Assert.That(PelagWreckSwingRules.Thickness(0f, w), Is.GreaterThan(.6f), "у головы — почти вся полоса");
            float last = 2f;
            for (float u = PelagWreckSwingRules.HeadRound; u <= 1f; u += .05f)
            {
                float t = PelagWreckSwingRules.Thickness(u, w);
                Assert.That(t, Is.LessThanOrEqualTo(last + 1e-5f), "от головы к хвосту только тоньше");
                last = t;
            }
            // Полоса у головы: от кулака (+InnerFromGrip) до носа головы (+OuterPastHead) — радиусы 0,6 → 1,35 м.
            float full = HeadRadius + PelagWreckSwingRules.OuterPastHead(w) - (GripRadius + PelagWreckSwingRules.InnerFromGrip);
            float atHead = full * PelagWreckSwingRules.Thickness(PelagWreckSwingRules.HeadRound, w);
            Assert.That(atHead, Is.GreaterThan(.55f), w + ": серп толстый (V1 — 0,40–0,46 м)");
            Assert.That(HeadRadius + PelagWreckSwingRules.OuterPastHead(w), Is.InRange(1.2f, 1.7f), "наружная кромка — дуга 1,2–1,5 м и лапы");
            Assert.That(PelagWreckSwingRules.InnerShare(.5f, w), Is.EqualTo(1f - PelagWreckSwingRules.Thickness(.5f, w)).Within(1e-6f));
        }
        Assert.That(PelagWreckSwingRules.Thickness(.5f, WreckSwingWeight.Heavy), Is.GreaterThan(PelagWreckSwingRules.Thickness(.5f, WreckSwingWeight.Light)),
            "мах 2 — тяжелее");
        Assert.That(PelagWreckSwingRules.OuterPastHead(WreckSwingWeight.Heavy), Is.GreaterThan(PelagWreckSwingRules.OuterPastHead(WreckSwingWeight.Light)));
        Assert.That(PelagWreckSwingRules.Glow(WreckSwingWeight.Heavy), Is.GreaterThan(PelagWreckSwingRules.Glow(WreckSwingWeight.Light)));
        Assert.That(PelagWreckSwingRules.SpeedAlpha(1f), Is.EqualTo(0f), "медленный ход не виден");
        float headSpeed = DegreesPerSecond * (float)Math.PI / 180f * HeadRadius;
        Assert.That(PelagWreckSwingRules.SpeedAlpha(headSpeed), Is.EqualTo(1f), "хлёст головы — во всю силу");
        Assert.That(PelagWreckSwingRules.TailAlpha(0f), Is.EqualTo(1f));
        Assert.That(PelagWreckSwingRules.TailAlpha(1f), Is.GreaterThan(0f), "конец рвёт маска пака, не прозрачность");
    }

    [Test]
    public void GhostLinks_TwoOrThree_RideTheOuterEdge_AndFitTheBand()
    {
        Assert.That(PelagWreckSwingRules.LinkCount(WreckSwingWeight.Light), Is.EqualTo(2));
        Assert.That(PelagWreckSwingRules.LinkCount(WreckSwingWeight.Heavy), Is.EqualTo(3));
        Assert.That(PelagWreckSwingRules.LinkCount(WreckSwingWeight.None), Is.EqualTo(0));
        foreach (WreckSwingWeight w in new[] { WreckSwingWeight.Light, WreckSwingWeight.Heavy })
        {
            float last = 0f;
            for (int i = 0; i < PelagWreckSwingRules.LinkCount(w); i++)
            {
                float u = PelagWreckSwingRules.LinkU(w, i);
                Assert.That(u, Is.GreaterThan(last + .15f), "звенья врозь, не у самой головы");
                Assert.That(u, Is.LessThan(.75f), "не на рваном хвосте");
                last = u;
                float full = HeadRadius + PelagWreckSwingRules.OuterPastHead(w) - (GripRadius + PelagWreckSwingRules.InnerFromGrip);
                Assert.That(PelagWreckSwingRules.LinkFit(full * PelagWreckSwingRules.Thickness(u, w), w), Is.GreaterThan(.5f), $"{w}, звено {i}: влезает в полосу");
            }
            Assert.That(PelagWreckSwingRules.LinkFit(.05f, w), Is.EqualTo(0f), "на тонком хвосте звена нет");
            Assert.That(PelagWreckSwingRules.LinkInset, Is.LessThan(.5f * PelagWreckSwingRules.LinkLength(w) + .05f), "ось звена у самой кромки");
        }
        Assert.That(PelagWreckSwingRules.LinkLength(WreckSwingWeight.Heavy), Is.GreaterThan(PelagWreckSwingRules.LinkLength(WreckSwingWeight.Light)));
        Assert.That(PelagWreckSwingRules.LinkRoll(0), Is.EqualTo(-PelagWreckSwingRules.LinkRoll(1)), "через одно — в другую сторону");
    }
}
