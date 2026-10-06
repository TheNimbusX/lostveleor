using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Вид Шквала v2 (02.10): полоса Пенного следа живёт весь срок Sim и тает до его конца, ход воды
// без излома, последний удар не путается с лишним прыжком Охоты, кольцо пены — по телу цели.
public sealed class SquallFoamViewRulesTests
{
    private const float Life = Simulation.FoamTrailLifeTicks / (float)Simulation.TicksPerSecond;

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(1f)]
    public void TrailDoesNotWhitenOrBreakForMostOfItsLife(float k)
    {
        // До последних ~0,6 с полоса — живая вода: ни побеления, ни трещин.
        float whitenFrom = PelagSquallFoamRules.TrailRemainingAt(
            PelagSquallFoamRules.ShaderBreakAge - PelagSquallFoamRules.ShaderFoamUp, k);
        Assert.That(whitenFrom, Is.LessThan(.65f), "побеление начинается слишком рано");
        for (float remaining = Life; remaining > whitenFrom + .01f; remaining -= .05f)
            Assert.That(PelagSquallFoamRules.TrailBreakAge(remaining, k),
                Is.LessThan(PelagSquallFoamRules.ShaderBreakAge - PelagSquallFoamRules.ShaderFoamUp), "осталось " + remaining);
    }

    [TestCase(0f)]
    [TestCase(1f)]
    public void TrailDropletsAreGoneBeforeSimStripEnds(float k)
    {
        // Последняя капля: трещины с запасом разброса + жизнь капель — раньше конца полосы в Sim.
        float gone = PelagSquallFoamRules.ShaderBreakAge + PelagSquallFoamRules.ShaderBreakJitter + PelagSquallFoamRules.ShaderDropLife;
        Assert.That(PelagSquallFoamRules.TrailRemainingAt(gone, k), Is.GreaterThanOrEqualTo(0f));
        Assert.That(PelagSquallFoamRules.TrailBreakAge(0f, k), Is.GreaterThanOrEqualTo(gone));
    }

    [Test]
    public void TrailBreaksFromJumpStartTowardLanding()
    {
        // Хвост (старт прыжка, k = 0) трескается раньше головы (посадка, k = 1).
        float tail = PelagSquallFoamRules.TrailRemainingAt(PelagSquallFoamRules.ShaderBreakAge, 0f);
        float head = PelagSquallFoamRules.TrailRemainingAt(PelagSquallFoamRules.ShaderBreakAge, 1f);
        Assert.That(tail, Is.GreaterThan(head));
        Assert.That(head, Is.EqualTo(PelagSquallFoamRules.TrailEndLead).Within(1e-5f));
    }

    [Test]
    public void TrailBreakAgeGrowsSmoothly()
    {
        float previous = -1f;
        for (float remaining = 1f; remaining >= 0f; remaining -= .01f)
        {
            float age = PelagSquallFoamRules.TrailBreakAge(remaining, .5f);
            Assert.That(age, Is.GreaterThanOrEqualTo(previous));
            if (previous > 0f) Assert.That(age - previous, Is.LessThanOrEqualTo(.01f * PelagSquallFoamRules.TrailEndRate + 1e-5f));
            previous = age;
        }
    }

    [Test]
    public void FlowKeepsSpeedWhenStreakBecomesTrail()
    {
        const float boundAt = .18f;
        float before = PelagSquallFoamRules.FlowSpeed(false, boundAt, -1f);
        float after = PelagSquallFoamRules.FlowSpeed(true, boundAt, boundAt);
        Assert.That(after, Is.EqualTo(before).Within(1e-4f), "скорость воды прыгнула на привязке");
        Assert.That(PelagSquallFoamRules.Flow(true, boundAt, boundAt),
            Is.EqualTo(PelagSquallFoamRules.Flow(false, boundAt, -1f)).Within(1e-4f), "рисунок воды прыгнул на привязке");
    }

    [Test]
    public void TrailWaterKeepsFlowingUntilTheEnd()
    {
        // Без застывания: вода течёт весь срок полосы, к концу — спокойно, но не ноль.
        float late = PelagSquallFoamRules.FlowSpeed(true, Life, .2f);
        Assert.That(late, Is.GreaterThan(.3f));
        Assert.That(late, Is.LessThan(PelagSquallFoamRules.FlowSpeed(true, .3f, .2f)));
    }

    [TestCase(0, 3, -1, true)]
    [TestCase(1, 2, -1, false)]
    [TestCase(0, 3, 3, false)]
    [TestCase(0, 4, 3, true)]
    public void FinalStrikeIsNotTheOneThatGaveAnExtraJump(int left, int index, int huntKill, bool final)
        => Assert.That(PelagSquallFoamRules.IsFinalStrike(left, index, huntKill), Is.EqualTo(final));

    [Test]
    public void RingFollowsBodyRadiusWithinLimits()
    {
        float defaultBody = EntityStore.DefaultBodyRadius.ToFloat();
        Assert.That(PelagSquallFoamRules.RingScale(defaultBody), Is.EqualTo((defaultBody + .15f) / .5f).Within(1e-4f));
        Assert.That(PelagSquallFoamRules.RingScale(.05f), Is.EqualTo(PelagSquallFoamRules.RingMinScale));
        Assert.That(PelagSquallFoamRules.RingScale(5f), Is.EqualTo(PelagSquallFoamRules.RingMaxScale));
    }

    // Раунд 3 (проверка 02.10, Охота): капли всплеска, короны и волны-толчка (принятые префабы
    // семьи) оставались бирюзово-белыми в любой форме — тело капли — цвет частицы, блок красил
    // только кромку. Бирюзовый отлив цвета частицы уходит в Shallow формы, белое остаётся белым.
    // Сдвиги — Shallow формы минус Shallow базы (.42; .90; .92), как в PelagSquallFormLook.For.
    private static readonly float[] HuntShift = { .96f - .42f, .36f - .90f, .76f - .92f };
    private static readonly float[] FoamTrailShift = { .58f - .42f, .74f - .90f, 1f - .92f };
    private static readonly float[] ElusiveShift = { .94f - .42f, .95f - .90f, 1f - .92f };

    private static float[] Recast(float r, float g, float b, float[] shift)
    {
        PelagSquallFoamRules.RecastDrop(ref r, ref g, ref b, shift[0], shift[1], shift[2]);
        return new[] { r, g, b };
    }

    [Test]
    public void FamilyDropletsTakeTheSquallFormColourAndWhiteStaysWhite()
    {
        // Цвета частиц префабов семьи: капля DropAqua, белый комок и капля, чистый белый (брызги, волна).
        float[][] authored = { new[] { .50f, .92f, .95f }, new[] { 1.08f, 1.18f, 1.18f }, new[] { .86f, 1.04f, 1.06f }, new[] { 1f, 1f, 1f } };
        Assert.That(PelagSquallFoamRules.DropCast(.50f, .92f, .95f), Is.EqualTo(1f).Within(1e-3f), "DropAqua — бирюзовая целиком");
        Assert.That(PelagSquallFoamRules.DropCast(1f, 1f, 1f), Is.EqualTo(0f));
        Assert.That(PelagSquallFoamRules.DropCast(1.08f, 1.18f, 1.18f), Is.InRange(.2f, .26f), "белый с лёгким бирюзовым отливом");

        foreach (float[] c in authored)
        {
            // База — авторский цвет.
            float[] same = Recast(c[0], c[1], c[2], new[] { 0f, 0f, 0f });
            for (int i = 0; i < 3; i++) Assert.That(same[i], Is.EqualTo(c[i]), "база");
            foreach (float[] shift in new[] { HuntShift, FoamTrailShift, ElusiveShift })
            {
                float[] x = Recast(c[0], c[1], c[2], shift);
                for (int i = 0; i < 3; i++) Assert.That(x[i], Is.GreaterThanOrEqualTo(0f));
                // Белые капли и комья остаются светлыми в любой форме.
                if (c != authored[0]) Assert.That(Math.Min(x[0], Math.Min(x[1], x[2])), Is.GreaterThan(.75f), "белое — светлое");
            }
        }

        // Бирюзовая капля в форме ближе к Shallow формы, чем к бирюзе базы.
        float[] baseShallow = { .42f, .90f, .92f };
        foreach (float[] shift in new[] { HuntShift, FoamTrailShift, ElusiveShift })
        {
            float[] x = Recast(.50f, .92f, .95f, shift);
            float toForm = 0f, toBase = 0f;
            for (int i = 0; i < 3; i++)
            {
                float form = baseShallow[i] + shift[i];
                toForm += (x[i] - form) * (x[i] - form);
                toBase += (x[i] - baseShallow[i]) * (x[i] - baseShallow[i]);
            }
            Assert.That(toForm, Is.LessThan(toBase), "капля — цвет формы, а не бирюза");
        }

        // Чистый белый — белый в любой форме.
        float[] white = Recast(1f, 1f, 1f, HuntShift);
        Assert.That(white[0], Is.EqualTo(1f));
        Assert.That(white[1], Is.EqualTo(1f));
        Assert.That(white[2], Is.EqualTo(1f));

        // Охота: бирюзовая капля — маджента (красный и синий над зелёным, как Shallow формы).
        float[] hunt = Recast(.50f, .92f, .95f, HuntShift);
        Assert.That(hunt[0], Is.EqualTo(.96f + .08f).Within(1e-4f));
        Assert.That(hunt[0], Is.GreaterThan(hunt[2]));
        Assert.That(hunt[2], Is.GreaterThan(hunt[1] + .3f));
        // Белый комок Охоты — светлый, с отливом формы (красный не ниже зелёного), не маджента.
        float[] huntWhite = Recast(1.08f, 1.18f, 1.18f, HuntShift);
        Assert.That(Math.Min(huntWhite[0], Math.Min(huntWhite[1], huntWhite[2])), Is.GreaterThan(1f));
        Assert.That(huntWhite[0], Is.GreaterThanOrEqualTo(huntWhite[1]));

        // Пенный след: капля — светлый кобальт (синий выше всех); Неуловимый — жемчуг (почти без оттенка).
        float[] trail = Recast(.50f, .92f, .95f, FoamTrailShift);
        Assert.That(trail[2], Is.GreaterThan(trail[1]));
        Assert.That(trail[1], Is.GreaterThan(trail[0]));
        float[] elusive = Recast(.50f, .92f, .95f, ElusiveShift);
        Assert.That(Math.Max(elusive[0], Math.Max(elusive[1], elusive[2])) - Math.Min(elusive[0], Math.Min(elusive[1], elusive[2])), Is.LessThan(.1f));
    }
}
