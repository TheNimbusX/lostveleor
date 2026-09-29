using Game.View;
using NUnit.Framework;

// Живой портрет боевого HUD и подпись опыта (ревью владельца 29.09: «рисованный + реакции»; цифры HP и
// лавидия всегда, опыт — «Ур. N · X / Y» рядом с тонкой полосой). Кривые — HudPortraitCurves.
public sealed class HudPortraitCurvesTests
{
    [Test]
    public void BreathGoesFromExhaleToInhaleAndBack()
    {
        Assert.That(HudPortraitCurves.Breath(0f), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(HudPortraitCurves.Breath(.5f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(HudPortraitCurves.Breath(1f), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(HudPortraitCurves.Breath(.25f), Is.EqualTo(.5f).Within(1e-6f));
    }

    [Test]
    public void BreathIsAboutOnePercentAndNeverShrinks()
    {
        Assert.That(HudPortraitCurves.BreathScale(1f, .01f), Is.EqualTo(1.01f).Within(1e-6f));
        Assert.That(HudPortraitCurves.BreathScale(0f, .01f), Is.EqualTo(1f));
        // Кривая за пределами 0..1 и отрицательная глубина не сжимают рисунок.
        Assert.That(HudPortraitCurves.BreathScale(-.5f, .01f), Is.EqualTo(1f));
        Assert.That(HudPortraitCurves.BreathScale(2f, .01f), Is.EqualTo(1.01f).Within(1e-6f));
        Assert.That(HudPortraitCurves.BreathScale(1f, -.02f), Is.EqualTo(1f));
    }

    [Test]
    public void BreathWindowKeepsTheBottomAndCentreAndStaysInsideTheArt()
    {
        HudPortraitCurves.BreathWindow(1.02f, out float x, out float y, out float w, out float h);
        Assert.That(y, Is.EqualTo(0f), "низ рисунка стоит — грудь поднимается вверх");
        Assert.That(x + w * .5f, Is.EqualTo(.5f).Within(1e-6f), "середина по x не уезжает");
        Assert.That(w, Is.EqualTo(h));
        Assert.That(w, Is.EqualTo(1f / 1.02f).Within(1e-6f));
        Assert.That(x, Is.GreaterThanOrEqualTo(0f));
        Assert.That(x + w, Is.LessThanOrEqualTo(1f + 1e-6f));
        Assert.That(y + h, Is.LessThanOrEqualTo(1f + 1e-6f));

        // Масштаб меньше 1 окно не раздувает: за краями выреза тянулись бы крайние пиксели.
        HudPortraitCurves.BreathWindow(.9f, out x, out y, out w, out h);
        Assert.That(x, Is.EqualTo(0f));
        Assert.That(w, Is.EqualTo(1f));
    }

    [Test]
    public void PulseBreathMatchesTheDangerHaze()
    {
        // HudPulse: 0,5 + 0,5·sin(t·2π/период) — портрет и красная дымка дышат вместе.
        const float period = 1.05f;
        foreach (float t in new[] { 0f, .1f, .26f, .7f, 3.3f })
            Assert.That(HudPortraitCurves.PulseBreath(t, period),
                Is.EqualTo(.5f + .5f * (float)System.Math.Sin(t * System.Math.PI * 2.0 / period)).Within(1e-5f));
        Assert.That(float.IsNaN(HudPortraitCurves.PulseBreath(1f, 0f)), Is.False, "нулевой период не ломает кривую");
    }

    [Test]
    public void JoltKicksAtOnceAndSettlesToZero()
    {
        Assert.That(HudPortraitCurves.Jolt(0f, .24f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(HudPortraitCurves.Jolt(.24f, .24f), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Jolt(5f, .24f), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Jolt(-.01f, .24f), Is.EqualTo(0f), "удара ещё не было");
        Assert.That(HudPortraitCurves.Jolt(float.PositiveInfinity, .24f), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Jolt(float.NaN, .24f), Is.EqualTo(0f));
        // Затухает: к концу размах меньше, чем в начале.
        Assert.That(System.Math.Abs(HudPortraitCurves.Jolt(.2f, .24f)), Is.LessThan(.1f));
    }

    [Test]
    public void HitTintHoldsForAFewFramesAndLeavesNothingBehind()
    {
        const float hold = .045f, fade = .07f;
        Assert.That(HudPortraitCurves.HitTint(0f, hold, fade), Is.EqualTo(1f));
        // 2–3 кадра при 60 кадрах в секунду — тон держится полностью.
        Assert.That(HudPortraitCurves.HitTint(2f / 60f, hold, fade), Is.EqualTo(1f));
        Assert.That(HudPortraitCurves.HitTint(hold + fade * .5f, hold, fade), Is.EqualTo(.5f).Within(1e-5f));
        // После — ноль: портрет не остаётся ни красным, ни светлее.
        Assert.That(HudPortraitCurves.HitTint(hold + fade, hold, fade), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.HitTint(10f, hold, fade), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.HitTint(float.PositiveInfinity, hold, fade), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.HitTint(-1f, hold, fade), Is.EqualTo(0f));
    }

    [Test]
    public void WarmthRisesFastAndFadesOut()
    {
        const float rise = .12f, duration = .75f;
        Assert.That(HudPortraitCurves.Warmth(0f, rise, duration), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Warmth(rise * .5f, rise, duration), Is.EqualTo(.5f).Within(1e-5f));
        Assert.That(HudPortraitCurves.Warmth(rise, rise, duration), Is.EqualTo(1f).Within(1e-5f));
        float mid = HudPortraitCurves.Warmth((rise + duration) * .5f, rise, duration);
        Assert.That(mid, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(HudPortraitCurves.Warmth(duration, rise, duration), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Warmth(float.PositiveInfinity, rise, duration), Is.EqualTo(0f));
        Assert.That(HudPortraitCurves.Warmth(-.1f, rise, duration), Is.EqualTo(0f));
    }

    [Test]
    public void EvenASmallHealIsVisibleAndABigOneIsBrighter()
    {
        float small = HudPortraitCurves.HealStrength(.02f), big = HudPortraitCurves.HealStrength(.4f);
        Assert.That(small, Is.GreaterThan(.4f));
        Assert.That(big, Is.GreaterThan(small));
        Assert.That(HudPortraitCurves.HealStrength(5f), Is.EqualTo(1f));
        Assert.That(HudPortraitCurves.HealStrength(-1f), Is.EqualTo(.45f).Within(1e-6f));
    }

    [Test]
    public void ExperienceLabelShowsLevelAndProgress() =>
        Assert.That(HudPortraitCurves.ExperienceLabel(4, 120, 300), Is.EqualTo("Ур. 4 · 120 / 300"));

    [Test]
    public void ExperienceLabelHasNoThousandsSeparatorAndClamps()
    {
        Assert.That(HudPortraitCurves.ExperienceLabel(12, 1340, 2500), Is.EqualTo("Ур. 12 · 1340 / 2500"));
        Assert.That(HudPortraitCurves.ExperienceLabel(0, -5, 0), Is.EqualTo("Ур. 1 · 0 / 1"));
        Assert.That(HudPortraitCurves.ExperienceLabel(3, 900, 300), Is.EqualTo("Ур. 3 · 300 / 300"));
    }
}
