using Game.View;
using NUnit.Framework;

// Отклик лечения Корнехвата (ревью 01.10): зелёный отрезок на полоске врага — растёт от прежней
// заливки, вспыхивает на приходе и тает; зелёная «+N» над вылеченным. Логика — HealFeedbackRules.cs.
public sealed class HealFeedbackTests
{
    const float Grow = HealBarFeedback.GrowSeconds;
    const float Hold = HealBarFeedback.HoldSeconds;

    static BarHealState Healed(float before, float after)
    {
        var s = default(BarHealState);
        HealBarFeedback.Begin(ref s, before, after);
        return s;
    }

    static void Run(ref BarHealState s, float seconds, float dt = 1f / 60f)
    {
        for (float t = 0f; t < seconds - 1e-6f; t += dt) HealBarFeedback.Step(ref s, dt);
    }

    // ---- полоска ----

    [Test]
    public void FillStartsAtTheOldHealthAndGrowsToTheNew()
    {
        var s = Healed(.4f, .55f);
        Assert.That(HealBarFeedback.ShownFill(in s, .55f), Is.EqualTo(.4f).Within(1e-5f));
        Run(ref s, Grow * .5f);
        float mid = HealBarFeedback.ShownFill(in s, .55f);
        Assert.That(mid, Is.GreaterThan(.4f).And.LessThan(.55f));
        // С торможением: к половине времени пройдено больше половины пути.
        Assert.That(mid, Is.GreaterThan(.475f));
        Run(ref s, Grow * .5f + .01f);
        Assert.That(HealBarFeedback.ShownFill(in s, .55f), Is.EqualTo(.55f).Within(1e-5f));
    }

    [Test]
    public void GreenSegmentCoversExactlyTheHealedPart()
    {
        var s = Healed(.4f, .55f);
        Run(ref s, Grow + .05f);
        Assert.IsTrue(HealBarFeedback.Segment(in s, .55f, out float from, out float to));
        Assert.That(from, Is.EqualTo(.4f).Within(1e-5f));
        Assert.That(to, Is.EqualTo(.55f).Within(1e-5f));
    }

    [Test]
    public void SegmentHoldsThenFadesAndEnds()
    {
        var s = Healed(.2f, .35f);
        Run(ref s, Grow + Hold * .9f);
        Assert.That(HealBarFeedback.Alpha(in s), Is.EqualTo(1f));
        Run(ref s, Hold * .1f + HealBarFeedback.FadeSeconds * .5f);
        float fading = HealBarFeedback.Alpha(in s);
        Assert.That(fading, Is.GreaterThan(0f).And.LessThan(1f));
        Run(ref s, HealBarFeedback.FadeSeconds);
        Assert.IsFalse(s.Active);
        Assert.That(HealBarFeedback.Alpha(in s), Is.EqualTo(0f));
        Assert.IsFalse(HealBarFeedback.Segment(in s, .35f, out _, out _));
        Assert.That(HealBarFeedback.ShownFill(in s, .35f), Is.EqualTo(.35f));
    }

    [Test]
    public void FlashPeaksWhenTheFrontArrivesAndDiesQuickly()
    {
        var s = Healed(.5f, .65f);
        Assert.That(HealBarFeedback.Flash(in s), Is.EqualTo(0f).Within(1e-5f));
        Run(ref s, Grow * .5f);
        float rising = HealBarFeedback.Flash(in s);
        Run(ref s, Grow * .5f);
        float peak = HealBarFeedback.Flash(in s);
        Assert.That(rising, Is.GreaterThan(0f).And.LessThan(peak));
        Assert.That(peak, Is.GreaterThan(.9f));
        Run(ref s, HealBarFeedback.FlashSeconds + .02f);
        Assert.That(HealBarFeedback.Flash(in s), Is.EqualTo(0f));
        // Отрезок ещё виден, вспышка уже нет: короткая.
        Assert.That(HealBarFeedback.Alpha(in s), Is.EqualTo(1f));
    }

    [Test]
    public void DamageDuringTheHealCutsTheSegmentFromAbove()
    {
        var s = Healed(.4f, .55f);
        Run(ref s, Grow + .1f);
        // Удар снял больше, чем вылечили сверх начала: зелёного почти не остаётся.
        Assert.IsTrue(HealBarFeedback.Segment(in s, .47f, out float from, out float to));
        Assert.That(from, Is.EqualTo(.4f).Within(1e-5f));
        Assert.That(to, Is.EqualTo(.47f).Within(1e-5f));
        // Ниже начала лечения — зелёного нет совсем, заливка честная.
        Assert.IsFalse(HealBarFeedback.Segment(in s, .3f, out _, out _));
        Assert.That(HealBarFeedback.ShownFill(in s, .3f), Is.EqualTo(.3f).Within(1e-5f));
    }

    [Test]
    public void FillNeverShowsMoreThanTheRealHealth()
    {
        var s = Healed(.4f, .55f);
        for (int i = 0; i < 40; i++)
        {
            HealBarFeedback.Step(ref s, 1f / 60f);
            Assert.That(HealBarFeedback.ShownFill(in s, .5f), Is.LessThanOrEqualTo(.5f + 1e-6f));
        }
    }

    [Test]
    public void SecondHealInASeriesKeepsTheStartAndGrowsOnWithoutJumpingBack()
    {
        var s = Healed(.3f, .45f);
        Run(ref s, Grow * .5f);
        float front = HealBarFeedback.Front(in s);
        HealBarFeedback.Begin(ref s, .45f, .6f);
        Assert.That(s.From, Is.EqualTo(.3f).Within(1e-5f));
        Assert.That(HealBarFeedback.Front(in s), Is.EqualTo(front).Within(1e-4f));
        Assert.That(HealBarFeedback.Flash(in s), Is.EqualTo(0f).Within(1e-5f));
        Run(ref s, Grow + .01f);
        Assert.That(HealBarFeedback.Front(in s), Is.EqualTo(.6f).Within(1e-5f));
        Assert.IsTrue(HealBarFeedback.Segment(in s, .6f, out float from, out _));
        Assert.That(from, Is.EqualTo(.3f).Within(1e-5f));
    }

    [Test]
    public void EmptyOrBackwardsHealStartsNothing()
    {
        var s = Healed(.5f, .5f);
        Assert.IsFalse(s.Active);
        s = Healed(.6f, .4f);
        Assert.IsFalse(s.Active);
        s = Healed(float.NaN, .4f);
        Assert.IsTrue(s.Active);
        Assert.That(s.From, Is.EqualTo(0f));
        // Вне полоски — прижато к краям.
        s = Healed(-.2f, 1.4f);
        Assert.That(s.From, Is.EqualTo(0f));
        Assert.That(s.To, Is.EqualTo(1f));
    }

    [Test]
    public void FifteenPercentHealIsAVisibleSegment()
    {
        // Хил Корнехвата после ревью — 15% здоровья за раз: отрезок ~16 пикселей обычной полоски.
        var s = Healed(.30f, .45f);
        Run(ref s, Grow);
        Assert.IsTrue(HealBarFeedback.Segment(in s, .45f, out float from, out float to));
        Assert.That(to - from, Is.EqualTo(.15f).Within(1e-4f));
    }

    // ---- цифра «+N» ----

    [Test]
    public void HealNumberHasAPlusAndNoSeparators()
    {
        Assert.That(HealNumberRules.Text(15), Is.EqualTo("+15"));
        Assert.That(HealNumberRules.Text(1), Is.EqualTo("+1"));
        Assert.That(HealNumberRules.Text(1240), Is.EqualTo("+1240"));
        Assert.That(HealNumberRules.Text(0), Is.EqualTo(string.Empty));
        Assert.That(HealNumberRules.Text(-7), Is.EqualTo(string.Empty));
    }

    [Test]
    public void HealsOfOneTargetMergeOnlyInsideTheWindow()
    {
        Assert.IsTrue(HealNumberRules.Merges(0f));
        Assert.IsTrue(HealNumberRules.Merges(HealNumberRules.MergeWindow - .01f));
        Assert.IsFalse(HealNumberRules.Merges(HealNumberRules.MergeWindow));
        Assert.IsFalse(HealNumberRules.Merges(-.01f));
        Assert.IsFalse(HealNumberRules.Merges(float.NaN));
        Assert.IsTrue(HealNumberRules.Merges(.9f, 1f));
    }

    [Test]
    public void MergedHealSumsWithoutOverflowAndIgnoresNegatives()
    {
        Assert.That(HealNumberRules.Add(15, 12), Is.EqualTo(27));
        Assert.That(HealNumberRules.Add(15, -5), Is.EqualTo(15));
        Assert.That(HealNumberRules.Add(-3, 4), Is.EqualTo(4));
        Assert.That(HealNumberRules.Add(int.MaxValue - 1, 10), Is.EqualTo(int.MaxValue));
        Assert.That(HealNumberRules.Text(HealNumberRules.Add(150, 150)), Is.EqualTo("+300"));
    }
}
