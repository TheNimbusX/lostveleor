using Game.View;
using NUnit.Framework;

// Урон и отклик удара (этап 4; выбор владельца 30.09 — кадр 1a доски hud-polish): кромка экрана со
// стороны удара, виньетка низкого здоровья, микростоп, след урона на полосках врагов, слияние и лесенка
// цифр урона. Логика — HeroHitFeedbackRules.cs (HeroHitFeedbackCurves, HealthBarTrail, DamageNumberRules).
public sealed class HeroHitFeedbackTests
{
    const int L = HeroHitFeedbackCurves.EdgeLeft, R = HeroHitFeedbackCurves.EdgeRight;
    const int B = HeroHitFeedbackCurves.EdgeBottom, T = HeroHitFeedbackCurves.EdgeTop;

    // ---- кромка: сторона и место ----

    [Test]
    public void HitFromTheLeftLightsOnlyTheLeftEdge()
    {
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-120f, 0f, L), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-120f, 0f, R), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-120f, 0f, T), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-120f, 0f, B), Is.EqualTo(0f).Within(1e-5f));
        // Сверху (y экрана вверх) — верхний край.
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(0f, 40f, T), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(0f, 40f, B), Is.EqualTo(0f));
    }

    [Test]
    public void DiagonalHitSplitsBetweenTwoNeighbourEdges()
    {
        float left = HeroHitFeedbackCurves.EdgeWeight(-1f, 1f, L);
        float top = HeroHitFeedbackCurves.EdgeWeight(-1f, 1f, T);
        Assert.That(left, Is.EqualTo(top).Within(1e-5f));
        Assert.That(left, Is.GreaterThan(.4f).And.LessThan(.8f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-1f, 1f, R), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-1f, 1f, B), Is.EqualTo(0f));
        // Чем ближе к оси, тем сильнее этот край.
        Assert.That(HeroHitFeedbackCurves.EdgeWeight(-3f, 1f, L), Is.GreaterThan(left));
    }

    [Test]
    public void NoDirectionLightsNothing()
    {
        for (int edge = 0; edge < HeroHitFeedbackCurves.EdgeCount; edge++)
        {
            Assert.That(HeroHitFeedbackCurves.EdgeWeight(0f, 0f, edge), Is.EqualTo(0f));
            Assert.That(HeroHitFeedbackCurves.EdgeWeight(float.NaN, 1f, edge), Is.EqualTo(0f));
        }
    }

    [Test]
    public void EdgeSpotSitsWhereTheRayFromTheHeroLeavesTheScreen()
    {
        const float aspect = 16f / 9f;
        // Герой в середине, удар строго слева: пятно на середине левого края.
        Assert.That(HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, -1f, 0f, aspect, L), Is.EqualTo(.5f).Within(1e-5f));
        // Слева и чуть сверху: луч проходит aspect/2 по x и поднимается на столько же × наклон.
        float along = HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, -1f, .2f, aspect, L);
        Assert.That(along, Is.EqualTo(.5f + .2f * aspect * .5f).Within(1e-4f));
        // Сверху-справа на верхнем крае: x выхода в долях ширины.
        float top = HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, .3f, 1f, aspect, T);
        Assert.That(top, Is.EqualTo((.5f * aspect + .3f * .5f) / aspect).Within(1e-4f));
        // Крутой луч уходит за угол — место прижимается к концу края, а не улетает.
        Assert.That(HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, -1f, 5f, aspect, L), Is.EqualTo(1f));
    }

    [Test]
    public void EdgeSpotDependsOnAspect()
    {
        // Тот же угол на 16:10 выходит на левый край выше середины меньше, чем на 16:9 (экран уже).
        float wide = HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, -1f, .3f, 16f / 9f, L);
        float narrow = HeroHitFeedbackCurves.EdgeAlong(.5f, .5f, -1f, .3f, 16f / 10f, L);
        Assert.That(narrow, Is.LessThan(wide));
        Assert.That(narrow, Is.GreaterThan(.5f));
    }

    [Test]
    public void RayAwayFromAnEdgeKeepsTheHeroPlaceOnIt()
    {
        // Удар справа, а спрашивают левый край — луч туда не идёт: место героя по вертикали.
        Assert.That(HeroHitFeedbackCurves.EdgeAlong(.4f, .3f, 1f, 0f, 16f / 9f, L), Is.EqualTo(.3f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.EdgeAlong(.4f, .3f, 0f, -1f, 16f / 9f, T), Is.EqualTo(.4f).Within(1e-5f));
    }

    // ---- кромка: сила и огибающая ----

    [Test]
    public void HitStrengthGrowsWithTheShareOfHealth()
    {
        Assert.That(HeroHitFeedbackCurves.HitStrength(0, 270), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.HitStrength(10, 0), Is.EqualTo(0f));
        float small = HeroHitFeedbackCurves.HitStrength(5, 270);
        float big = HeroHitFeedbackCurves.HitStrength(38, 270);
        Assert.That(small, Is.GreaterThanOrEqualTo(.4f), "даже мелкий удар виден");
        Assert.That(big, Is.GreaterThan(small));
        Assert.That(HeroHitFeedbackCurves.HitStrength(54, 270), Is.EqualTo(1f).Within(1e-5f), "20 % здоровья — полная");
        Assert.That(HeroHitFeedbackCurves.HitStrength(500, 270), Is.EqualTo(1f));
    }

    [Test]
    public void FlashRisesHoldsAndFadesWithinHalfASecond()
    {
        const float rise = .05f, hold = .05f, fade = .4f;
        Assert.That(HeroHitFeedbackCurves.Flash(-.01f, rise, hold, fade), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.Flash(0f, rise, hold, fade), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.Flash(.025f, rise, hold, fade), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(HeroHitFeedbackCurves.Flash(.07f, rise, hold, fade), Is.EqualTo(1f));
        Assert.That(HeroHitFeedbackCurves.Flash(.3f, rise, hold, fade), Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(HeroHitFeedbackCurves.Flash(.5f, rise, hold, fade), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.Flash(float.NaN, rise, hold, fade), Is.EqualTo(0f));
    }

    [Test]
    public void RetriggerNeverDropsTheCurrentBrightness()
    {
        const float rise = .05f, hold = .05f, fade = .4f;
        // Сильная кромка гаснет, приходит слабый удар: яркость продолжается с нынешней.
        float current = .9f * HeroHitFeedbackCurves.Flash(.25f, rise, hold, fade);
        HeroHitFeedbackCurves.Retrigger(.9f, .25f, .45f, rise, hold, fade, out float peak, out float age);
        Assert.That(peak * HeroHitFeedbackCurves.Flash(age, rise, hold, fade), Is.EqualTo(Brighter(current, .45f)).Within(2e-3f));

        // Удар сильнее нынешнего: пик — новый, подъём с нынешнего уровня, а не с нуля.
        HeroHitFeedbackCurves.Retrigger(.3f, .2f, 1f, rise, hold, fade, out peak, out age);
        float before = .3f * HeroHitFeedbackCurves.Flash(.2f, rise, hold, fade);
        Assert.That(peak, Is.EqualTo(1f));
        Assert.That(HeroHitFeedbackCurves.Flash(age, rise, hold, fade) * peak, Is.EqualTo(before).Within(2e-3f));

        // Погасшая кромка — обычный старт.
        HeroHitFeedbackCurves.Retrigger(.8f, 5f, .6f, rise, hold, fade, out peak, out age);
        Assert.That(peak, Is.EqualTo(.6f));
        Assert.That(age, Is.EqualTo(0f).Within(1e-4f));
    }

    static float Brighter(float current, float strength) => current > strength ? current : strength;

    // ---- виньетка ----

    [Test]
    public void VignetteAppearsBelowThirtyPercentAndIsFullAtTen()
    {
        Assert.That(HeroHitFeedbackCurves.LowHealth(1f), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.LowHealth(.3f), Is.EqualTo(0f));
        Assert.That(HeroHitFeedbackCurves.LowHealth(.2f), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(HeroHitFeedbackCurves.LowHealth(.1f), Is.EqualTo(1f));
        Assert.That(HeroHitFeedbackCurves.LowHealth(.02f), Is.EqualTo(1f));
        // Мёртвый — не виньетка: смерть показывает RunEndBeat.
        Assert.That(HeroHitFeedbackCurves.LowHealth(0f), Is.EqualTo(0f));
        // Портрет краснеет с 25 % — виньетка к этому моменту уже проявляется.
        Assert.That(HeroHitFeedbackCurves.LowHealth(.25f), Is.GreaterThan(0f));
    }

    [Test]
    public void VignetteBreathesAtHalfThePortraitPaceAndPeaksWithIt()
    {
        const float period = 1.05f;
        for (int k = 0; k < 4; k++)
        {
            // Каждый вдох виньетки — на вдохе дымки портрета (0,5 + 0,5·sin(t·2π/период) = 1).
            float peak = period * .25f + 2f * k * period;
            Assert.That(HeroHitFeedbackCurves.VignetteBreath(peak, period), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(HudPortraitCurves.PulseBreath(peak, period), Is.EqualTo(1f).Within(1e-4f));
            // Через период — выдох виньетки: дышит вдвое медленнее.
            Assert.That(HeroHitFeedbackCurves.VignetteBreath(peak + period, period), Is.EqualTo(0f).Within(1e-4f));
        }
    }

    [Test]
    public void ApproachRisesAndFallsAtTheirOwnPace()
    {
        Assert.That(HeroHitFeedbackCurves.Approach(0f, 1f, .3f, .6f, .9f), Is.EqualTo(.5f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.Approach(1f, 0f, .3f, .6f, .9f), Is.EqualTo(2f / 3f).Within(1e-5f));
        Assert.That(HeroHitFeedbackCurves.Approach(.9f, 1f, .3f, .6f, .9f), Is.EqualTo(1f));
        Assert.That(HeroHitFeedbackCurves.Approach(.4f, 1f, 0f, .6f, .9f), Is.EqualTo(.4f));
    }

    // ---- микростоп ----

    [Test]
    public void MicroStopOnlyOnBigHitsAndStunsAndNotInARow()
    {
        Assert.That(HeroHitFeedbackCurves.MicroStop(41, 270, false, 5f), Is.True, "≥ 15 % здоровья");
        Assert.That(HeroHitFeedbackCurves.MicroStop(40, 270, false, 5f), Is.False);
        Assert.That(HeroHitFeedbackCurves.MicroStop(0, 270, true, 5f), Is.True, "оглушение");
        Assert.That(HeroHitFeedbackCurves.MicroStop(100, 270, true, .2f), Is.False, "толпа не держит кадр");
        Assert.That(HeroHitFeedbackCurves.MicroStop(100, 0, false, 5f), Is.False);
    }

    // ---- след урона на полосках ----

    [Test]
    public void TrailHoldsThePreviousHealthThenDrainsToTheNewValue()
    {
        var s = new BarTrailState();
        HealthBarTrail.Step(ref s, 1f, .016f, .35f, 1.2f);
        Assert.That(HealthBarTrail.Visible(in s), Is.False, "первый кадр — без следа");

        HealthBarTrail.Step(ref s, .6f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(1f));
        Assert.That(HealthBarTrail.Visible(in s), Is.True);

        // Пауза ~0,35 с: след стоит.
        for (int i = 0; i < 20; i++) HealthBarTrail.Step(ref s, .6f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(1f));

        // Потом стекает к новому и не проваливается ниже.
        for (int i = 0; i < 60; i++) HealthBarTrail.Step(ref s, .6f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(.6f).Within(1e-5f));
        Assert.That(HealthBarTrail.Visible(in s), Is.False);
    }

    [Test]
    public void NewHitWhileDrainingKeepsTheHighestTrailAndRestartsThePause()
    {
        var s = new BarTrailState();
        HealthBarTrail.Step(ref s, 1f, .016f, .35f, 1.2f);
        HealthBarTrail.Step(ref s, .7f, .016f, .35f, 1.2f);
        for (int i = 0; i < 30; i++) HealthBarTrail.Step(ref s, .7f, .016f, .35f, 1.2f);
        float draining = s.Trail;
        Assert.That(draining, Is.LessThan(1f).And.GreaterThan(.7f));

        HealthBarTrail.Step(ref s, .5f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(draining), "след не прыгает ни вверх, ни вниз");
        Assert.That(s.Hold, Is.GreaterThan(.3f), "пауза заново");
    }

    [Test]
    public void HealsDoNotTrail()
    {
        var s = new BarTrailState();
        HealthBarTrail.Step(ref s, .4f, .016f, .35f, 1.2f);
        HealthBarTrail.Step(ref s, .8f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(.8f));
        Assert.That(HealthBarTrail.Visible(in s), Is.False);

        // Лечение посреди следа: здоровье поднимается над следом — след не тянется вверх.
        HealthBarTrail.Step(ref s, .5f, .016f, .35f, 1.2f);
        HealthBarTrail.Step(ref s, .9f, .016f, .35f, 1.2f);
        Assert.That(s.Trail, Is.EqualTo(.9f));
        Assert.That(HealthBarTrail.Visible(in s), Is.False);
    }

    // ---- цифры урона ----

    [Test]
    public void FastHitsMergeButCritsStandAlone()
    {
        Assert.That(DamageNumberRules.Merges(.1f, .3f, false), Is.True);
        Assert.That(DamageNumberRules.Merges(.3f, .3f, false), Is.False, "окно — от прошлого попадания");
        Assert.That(DamageNumberRules.Merges(.1f, .3f, true), Is.False, "крит — своей цифрой");
        Assert.That(DamageNumberRules.Merges(-1f, .3f, false), Is.False);
    }

    [Test]
    public void MergedNumberGrowsAndWarmsUpToACap()
    {
        Assert.That(DamageNumberRules.MergedScale(1), Is.EqualTo(1f));
        Assert.That(DamageNumberRules.MergedScale(2), Is.EqualTo(1.14f).Within(1e-5f));
        Assert.That(DamageNumberRules.MergedScale(5), Is.GreaterThan(DamageNumberRules.MergedScale(3)));
        Assert.That(DamageNumberRules.MergedScale(50), Is.EqualTo(1.6f).Within(1e-5f));
        // Слитое «211» крупнее крита «126»: крит ×1,5 от обычной, серия из пяти — ×1,56.
        Assert.That(DamageNumberRules.MergedScale(5), Is.GreaterThan(1.5f));

        Assert.That(DamageNumberRules.MergedWarmth(1), Is.EqualTo(0f));
        Assert.That(DamageNumberRules.MergedWarmth(3), Is.GreaterThan(DamageNumberRules.MergedWarmth(2)));
        Assert.That(DamageNumberRules.MergedWarmth(100), Is.EqualTo(.45f).Within(1e-5f));
    }

    [Test]
    public void NumbersOfOneTargetStackUpInsteadOfPilingUp()
    {
        DamageNumberRules.Stack(0, .36f, out float x0, out float y0);
        DamageNumberRules.Stack(1, .36f, out float x1, out float y1);
        DamageNumberRules.Stack(2, .36f, out float x2, out float y2);
        DamageNumberRules.Stack(3, .36f, out float x3, out float y3);
        Assert.That(x0, Is.EqualTo(0f));
        Assert.That(y0, Is.EqualTo(0f));
        Assert.That(y1, Is.EqualTo(.36f).Within(1e-5f));
        Assert.That(y2, Is.GreaterThan(y1));
        Assert.That(y3, Is.GreaterThan(y2));
        Assert.That(x1, Is.Not.EqualTo(0f));
        Assert.That(x2, Is.EqualTo(-x1).Within(1e-5f), "ступени попеременно в стороны");
        // Лесенка короткая: после трёх ступеней — снова снизу.
        DamageNumberRules.Stack(4, .36f, out float x4, out float y4);
        Assert.That(y4, Is.EqualTo(0f));
        Assert.That(x4, Is.EqualTo(0f));
    }

    [Test]
    public void CritPopsHarderThanANormalNumber()
    {
        float normalPeak = 0f, critPeak = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float age = .11f * i / 100f;
            normalPeak = System.Math.Max(normalPeak, DamageNumberRules.Pop(age, .11f));
            critPeak = System.Math.Max(critPeak, DamageNumberRules.Pop(age, .11f, 3.2f));
        }
        Assert.That(normalPeak, Is.EqualTo(1.1f).Within(.01f));
        Assert.That(critPeak, Is.GreaterThan(1.22f).And.LessThan(1.35f));
        Assert.That(DamageNumberRules.Pop(0f, .11f), Is.EqualTo(0f));
        Assert.That(DamageNumberRules.Pop(.2f, .11f), Is.EqualTo(1f));
    }

    [Test]
    public void BumpAndFadeStayInTheirRanges()
    {
        Assert.That(DamageNumberRules.Bump(0f, .14f), Is.EqualTo(.22f).Within(1e-5f));
        Assert.That(DamageNumberRules.Bump(.14f, .14f), Is.EqualTo(0f));
        Assert.That(DamageNumberRules.Bump(.07f, .14f), Is.LessThan(.22f).And.GreaterThan(0f));

        Assert.That(DamageNumberRules.Fade(.5f, .26f), Is.EqualTo(1f));
        Assert.That(DamageNumberRules.Fade(.13f, .26f), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(DamageNumberRules.Fade(0f, .26f), Is.EqualTo(0f));
    }
}
