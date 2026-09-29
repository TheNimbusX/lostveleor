using NUnit.Framework;
using Game.View;

// Полоса элиты (поток J «Мобов леса v2», владелец 29.09): цифры в полосе и высота полоски над макушкой.
public sealed class EliteBarLayoutTests
{
    [Test]
    public void NumbersShowCurrentOverMax() => Assert.That(EliteBarLayout.Numbers(1234, 2000), Is.EqualTo("1234 / 2000"));

    [Test]
    public void NumbersHaveNoThousandsSeparator() => Assert.That(EliteBarLayout.Numbers(12400, 20000), Is.EqualTo("12400 / 20000"));

    [Test]
    public void NumbersClampHealthIntoBar()
    {
        Assert.That(EliteBarLayout.Numbers(-5, 2000), Is.EqualTo("0 / 2000"));
        Assert.That(EliteBarLayout.Numbers(2500, 2000), Is.EqualTo("2000 / 2000"));
    }

    [Test]
    public void BarBottomClearsTheMeasuredTop()
    {
        // Вендиго с макушкой 3,1 м: полоса элиты 0,3 м — середина на 3,1 + 0,12 + 0,15.
        float center = EliteBarLayout.Target(3.1f, true, 2.235f, .12f, .15f);
        Assert.That(center - .15f, Is.GreaterThan(3.1f));
        Assert.That(center, Is.EqualTo(3.37f).Within(1e-4f));
    }

    [Test]
    public void WithoutMeasureTheTableHeightStays()
    {
        Assert.That(EliteBarLayout.Target(0f, false, 2.15f, .12f, .065f), Is.EqualTo(2.15f));
        Assert.That(EliteBarLayout.Target(float.NaN, true, 2.15f, .12f, .065f), Is.EqualTo(2.15f));
        // Тело ещё в земле — не прижимать полоску к траве.
        Assert.That(EliteBarLayout.Target(.1f, true, 2.15f, .12f, .065f), Is.EqualTo(2.15f));
    }

    [Test]
    public void InflatedSkinBoundsAreCapped()
    {
        // Раздутые границы кожи рантайм-префаба (центр 1,8 + 4,5) не уносят полоску на 6 м.
        float center = EliteBarLayout.Target(6.3f, true, 2.15f, .12f, .065f);
        Assert.That(center, Is.EqualTo(2.15f * 2f + .12f + .065f).Within(1e-4f));
    }

    [Test]
    public void AnchorRisesFastAndFallsSlowly()
    {
        const float dt = 1f / 60f;
        float up = EliteBarLayout.Follow(2f, 3f, dt, 18f, 3f);
        float down = EliteBarLayout.Follow(3f, 2f, dt, 18f, 3f);
        Assert.That(up - 2f, Is.GreaterThan((3f - down) * 4f));

        // За треть секунды подъём почти догоняет макушку.
        float h = 2f;
        for (int i = 0; i < 20; i++) h = EliteBarLayout.Follow(h, 3f, dt, 18f, 3f);
        Assert.That(h, Is.GreaterThan(2.99f));
    }

    [Test]
    public void AnchorHoldsWhenTimeStands() => Assert.That(EliteBarLayout.Follow(2f, 3f, 0f, 18f, 3f), Is.EqualTo(2f));
}
