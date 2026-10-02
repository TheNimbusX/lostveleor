using System;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи — сквозь босса видно героя (razlom/Assets/Game.View/ThicketMasterSeeThroughRules.cs,
/// владелец 02.10: «прозрачность должна быть, чтоб было видно»): сетка включается, только когда тело
/// стоит между героем и камерой боя (орто, наклон 48°), появляется и уходит за 0,15 с, в середине
/// круга выбито 60–75 % пикселей.
/// </summary>
public sealed class ThicketMasterSeeThroughRulesTests
{
    // Рамка тела в осях корня: след ≈ 4,9 × 6,5 м, рост 4,14 м, плюс запас.
    private const float M = ThicketMasterSeeThroughRules.BoxMarginMetres;
    private const float MinX = -2.45f - M, MaxX = 2.45f + M, MinY = 0f - M, MaxY = 4.14f + M, MinZ = -3.25f - M, MaxZ = 3.25f + M;

    // Камера боя смотрит вдоль +Z и вниз на 48°: луч к камере — вверх и к −Z.
    private static readonly float Up = (float)Math.Sin(48 * Math.PI / 180), Back = -(float)Math.Cos(48 * Math.PI / 180);

    private static bool Covered(float x, float z, float height = ThicketMasterSeeThroughRules.HeroCentreHeight)
        => ThicketMasterSeeThroughRules.RayHitsBox(x, height, z, 0f, Up, Back, MinX, MinY, MinZ, MaxX, MaxY, MaxZ, float.PositiveInfinity);

    // ------------------------------------------------------------ когда

    [Test]
    public void HeroRightBehindTheBody_IsCovered()
    {
        Assert.IsTrue(Covered(0f, 4.5f), "за крупом, в двух метрах от тела");
        Assert.IsTrue(Covered(1.5f, 3.8f), "за боком");
    }

    [Test]
    public void HeroUnderTheCrownOrBetweenThePaws_IsCovered()
    {
        Assert.IsTrue(Covered(0f, 0f), "начало луча внутри рамки");
        Assert.IsTrue(Covered(0f, -2.5f, .3f));
    }

    [Test]
    public void HeroInFrontOrBesideOrFarBehind_IsNotCovered()
    {
        Assert.IsFalse(Covered(0f, -5f), "перед боссом — тело за героем");
        Assert.IsFalse(Covered(4.5f, 0f), "сбоку");
        Assert.IsFalse(Covered(-4.5f, 2f), "сбоку слева");
        Assert.IsFalse(Covered(0f, 11f), "далеко за боссом: на экране герой выше кроны");
    }

    [Test]
    public void RayParallelToASlab_HitsOnlyInsideIt()
    {
        Assert.IsTrue(ThicketMasterSeeThroughRules.RayHitsBox(0f, 1f, -10f, 0f, 0f, 1f, -1f, 0f, -1f, 1f, 2f, 1f, float.PositiveInfinity));
        Assert.IsFalse(ThicketMasterSeeThroughRules.RayHitsBox(3f, 1f, -10f, 0f, 0f, 1f, -1f, 0f, -1f, 1f, 2f, 1f, float.PositiveInfinity));
    }

    [Test]
    public void PerspectiveSegment_StopsAtTheCamera()
    {
        // Отрезок к камере (t ≤ 1) кончается до рамки — тело за камерой не закрывает героя.
        Assert.IsFalse(ThicketMasterSeeThroughRules.RayHitsBox(0f, 1f, -10f, 0f, 0f, 5f, -1f, 0f, -1f, 1f, 2f, 1f, 1f));
        Assert.IsTrue(ThicketMasterSeeThroughRules.RayHitsBox(0f, 1f, -10f, 0f, 0f, 20f, -1f, 0f, -1f, 1f, 2f, 1f, 1f));
    }

    [Test]
    public void RayLeavingTheBox_DoesNotCountBoxBehindTheHero()
    {
        // Рамка целиком позади начала луча (t < 0) — не перед героем.
        Assert.IsFalse(ThicketMasterSeeThroughRules.RayHitsBox(0f, 1f, 5f, 0f, 0f, 1f, -1f, 0f, -1f, 1f, 2f, 1f, float.PositiveInfinity));
    }

    [Test]
    public void SamplePoints_CoverFeetHeadAndSides()
    {
        Assert.AreEqual(ThicketMasterSeeThroughRules.SampleHeights.Length, ThicketMasterSeeThroughRules.SampleSides.Length);
        float low = float.MaxValue, high = float.MinValue, left = 0f, right = 0f;
        for (int i = 0; i < ThicketMasterSeeThroughRules.SampleHeights.Length; i++)
        {
            low = Math.Min(low, ThicketMasterSeeThroughRules.SampleHeights[i]);
            high = Math.Max(high, ThicketMasterSeeThroughRules.SampleHeights[i]);
            left = Math.Min(left, ThicketMasterSeeThroughRules.SampleSides[i]);
            right = Math.Max(right, ThicketMasterSeeThroughRules.SampleSides[i]);
        }
        Assert.That(low, Is.LessThanOrEqualTo(.4f));
        Assert.That(high, Is.GreaterThanOrEqualTo(1.5f));
        Assert.That(left, Is.LessThan(0f));
        Assert.That(right, Is.GreaterThan(0f));
    }

    // ------------------------------------------------------------ как

    [Test]
    public void Fade_TakesFifteenHundredthsOfASecond_BothWays()
    {
        Assert.AreEqual(.15f, ThicketMasterSeeThroughRules.FadeSeconds, 1e-6f);
        float half = ThicketMasterSeeThroughRules.Step(0f, 1f, .075f);
        Assert.AreEqual(.5f, half, 1e-4f);
        Assert.AreEqual(1f, ThicketMasterSeeThroughRules.Step(half, 1f, .075f), 1e-4f);
        Assert.AreEqual(1f, ThicketMasterSeeThroughRules.Step(.9f, 1f, 1f), "без перелёта");
        Assert.AreEqual(.5f, ThicketMasterSeeThroughRules.Step(1f, 0f, .075f), 1e-4f);
        Assert.AreEqual(0f, ThicketMasterSeeThroughRules.Step(.1f, 0f, 1f), "без перелёта вниз");
    }

    [Test]
    public void Fade_HoldsOnPause_AndRecoversFromGarbage()
    {
        Assert.AreEqual(.4f, ThicketMasterSeeThroughRules.Step(.4f, 1f, 0f), 1e-6f);
        Assert.AreEqual(.4f, ThicketMasterSeeThroughRules.Step(.4f, 0f, -1f), 1e-6f);
        Assert.AreEqual(0f, ThicketMasterSeeThroughRules.Step(float.NaN, 0f, .1f), 1e-6f);
    }

    [Test]
    public void Shown_IsZeroWhenHidden_AndSixtyToSeventyFivePercentAtFull()
    {
        Assert.AreEqual(0f, ThicketMasterSeeThroughRules.Shown(0f), 1e-6f);
        float full = ThicketMasterSeeThroughRules.Shown(1f);
        Assert.That(full, Is.InRange(.6f, .75f));
        float previous = 0f;
        for (int i = 1; i <= 20; i++)
        {
            float x = ThicketMasterSeeThroughRules.Shown(i / 20f);
            Assert.That(x, Is.GreaterThanOrEqualTo(previous), "плавно и только вверх");
            previous = x;
        }
        Assert.AreEqual(full, ThicketMasterSeeThroughRules.Shown(3f), 1e-6f);
    }

    [Test]
    public void Circle_IsAboutTwoMetres_AroundTheHeroMiddle()
    {
        Assert.That(ThicketMasterSeeThroughRules.RadiusMetres, Is.InRange(1.6f, 2f));
        Assert.That(ThicketMasterSeeThroughRules.HeroCentreHeight, Is.InRange(.7f, 1.2f));
        Assert.That(ThicketMasterSeeThroughRules.DepthRampMetres, Is.GreaterThan(0f));
    }
}
