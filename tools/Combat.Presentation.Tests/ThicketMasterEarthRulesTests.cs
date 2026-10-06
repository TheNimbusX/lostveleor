using System;
using Game.Sim;
using NUnit.Framework;
using Earth = Game.View.ThicketMasterEarthRules;

/// <summary>
/// Хозяин Чащи — земля нырка и выхода (V14, владелец 04.10: «земля странно выглядит, как кольцо какое-то… нужно, чтобы
/// земля была фактурная, а не гладкая плоская»). Правила ThicketMasterEarthRules: сроки слоёв по тикам Sim, раскладка
/// по контуру тела — рваная полоса с разрывами (не кольцо), выброс выхода вдоль корпуса, пыль низкая и короткая,
/// бюджет, пулы на нырки подряд, вспышка света из ямы. Ход под землёй (V17, владелец 08.10: «как будто холмик просто
/// скользит по полу»): вырванные плиты падают, где встали, рябь впереди чуть приподнимает пол, траншея — губы выше дна,
/// над головой вспучено, частота следа — по метрам на ходу Sim 7 м/с, бюджет и пул 2.
/// </summary>
public sealed class ThicketMasterEarthRulesTests
{
    private const float Tau = (float)(2.0 * Math.PI);

    [Test]
    public void Timings_FollowSimTicks()
    {
        Assert.That(Earth.BurrowSeconds, Is.EqualTo(Simulation.ThicketDiveBurrowTicks / 30f).Within(1e-6), "плиты валятся в яму весь уход (12 тиков)");
        Assert.That(Earth.BulgeSeconds, Is.EqualTo(Simulation.ThicketDiveLockTicks / 30f).Within(1e-6), "вздутие — от круга до удара");
        Assert.That(Earth.BulgeSeconds * Simulation.TicksPerSecond,
            Is.EqualTo(Simulation.ThicketDiveLockTicks).Within(1e-4), "вздутие кончается ровно в тик выхода (T+54)");
        Assert.That(Earth.EruptSeconds * Simulation.TicksPerSecond, Is.LessThanOrEqualTo(12f), "выброс закрывает тело ~10 тиков, как принятый");
        Assert.That(Earth.RubbleLifeMax, Is.LessThanOrEqualTo(Earth.EmergeLife), "щебень уходит до конца жизни экземпляра выхода");
        Assert.That(Earth.EruptLightSeconds, Is.LessThan(Earth.EruptSeconds + .1f), "свет из ямы — только на выбросе");
    }

    [Test]
    public void Dust_StaysLowAndShort()
    {
        Assert.That(Earth.DiveDustLife, Is.LessThanOrEqualTo(Earth.DustMaxLife));
        Assert.That(Earth.EmergeShockLife, Is.LessThanOrEqualTo(Earth.DustMaxLife));
        Assert.That(Earth.EmergeLowDustLife, Is.LessThanOrEqualTo(Earth.DustMaxLife));
        Assert.That(Earth.DiveDustClearSeconds, Is.LessThanOrEqualTo(1.3f), "пыль ухода гаснет к ~1,25 с — не закрывает героя");
        Assert.That(Earth.DustMaxHeight, Is.LessThanOrEqualTo(1f), "клуб ниже 1 м (ревью 02.10, вечер: «вата» закрывала героя)");
    }

    [TestCase(16, 1411)]
    [TestCase(7, 1401)]
    [TestCase(48, 1421)]
    [TestCase(10, 1471)]
    public void ContourBand_IsBrokenIntoArcs_NotAClosedRing(int count, int salt)
    {
        var angles = new float[count];
        for (int i = 0; i < count; i++)
        {
            Earth.ContourSpot(i, count, salt, .8f, 1.05f, out angles[i], out float d);
            Assert.That(d, Is.InRange(.8f, 1.05f), "место — на полосе контура");
        }
        float mean = Tau / count;
        var sorted = (float[])angles.Clone();
        for (int i = 0; i < sorted.Length; i++) sorted[i] = ((sorted[i] % Tau) + Tau) % Tau;
        Array.Sort(sorted);
        int wide = 0;
        for (int i = 0; i < count; i++)
        {
            float gap = i + 1 < count ? sorted[i + 1] - sorted[i] : sorted[0] + Tau - sorted[count - 1];
            if (gap > 1.15f * mean) wide++;
        }
        Assert.That(wide, Is.GreaterThanOrEqualTo(Math.Max(2, count / 4)), "полоса рвётся на дуги — разрывы шире среднего шага");
        Assert.That(Earth.LargestGap(angles), Is.GreaterThan(1.2f * mean), "есть явный разрыв — не замкнутое кольцо");
    }

    [Test]
    public void EmergeSpots_GatherAlongTheBody()
    {
        const int count = 24;
        int alongBody = 0;
        for (int i = 0; i < count; i++)
        {
            Earth.AlongBodySpot(i, count, 1611, .5f, 1f, out float angle, out float d);
            Assert.That(d, Is.InRange(.5f, 1f));
            if (Math.Abs(Math.Cos(angle)) > .866) alongBody++;
        }
        // Равномерно по кругу в ±30° от носа и хвоста — треть; вдоль тела — больше половины.
        Assert.That(alongBody, Is.GreaterThanOrEqualTo(count / 2), "плиты рвутся вдоль длины тела, а не ровным кольцом");
    }

    [Test]
    public void BodyContour_NormalPointsOutward()
    {
        for (int k = 0; k < 16; k++)
        {
            float angle = k / 16f * Tau;
            Earth.BodyPoint(angle, 1f, out float x, out float z);
            Earth.BodyNormal(angle, out float nx, out float nz);
            Assert.That(Math.Sqrt(nx * nx + nz * nz), Is.EqualTo(1.0).Within(1e-5));
            Assert.That(nx * x + nz * (z - Earth.BodyShift), Is.GreaterThan(0f), "нормаль контура — наружу");
        }
        Earth.BodyPoint(0f, 1f, out float noseX, out float noseZ);
        Assert.That(noseX, Is.EqualTo(0f).Within(1e-5));
        Assert.That(noseZ, Is.EqualTo(Earth.BodyHalfLength + Earth.BodyShift).Within(1e-5), "угол 0 — нос тела (+Z)");
    }

    [Test]
    public void EruptLight_ShortAmberPulse()
    {
        Assert.That(Earth.EruptLight(-.01f), Is.EqualTo(0f));
        Assert.That(Earth.EruptLight(0f), Is.EqualTo(0f));
        Assert.That(Earth.EruptLight(Earth.EruptLightRise), Is.EqualTo(1f).Within(1e-5), "пик сразу после удара");
        Assert.That(Earth.EruptLight(Earth.EruptLightSeconds), Is.EqualTo(0f));
        Assert.That(Earth.EruptLight(1f), Is.EqualTo(0f), "дальше не горит");
        float last = 1f;
        for (float t = Earth.EruptLightRise; t < Earth.EruptLightSeconds; t += .01f)
        {
            float k = Earth.EruptLight(t);
            Assert.That(k, Is.LessThanOrEqualTo(last + 1e-6f), "гаснет без вспышек");
            last = k;
        }
    }

    [Test]
    public void Budget_FitsOnePrefabPeak()
    {
        Assert.That(Earth.DiveTrianglesPeak, Is.LessThanOrEqualTo(16000), "уход — все обломки в воздухе разом");
        Assert.That(Earth.EmergeTrianglesPeak, Is.LessThanOrEqualTo(22000), "выход — все обломки в воздухе разом");
        int dive = Earth.DivePatches + 1 + Earth.DiveSlabs + Earth.DiveClods + Earth.DiveBigClods + Earth.DiveRocks + Earth.DiveGrains
            + Earth.DiveDust + Earth.DiveTufts;
        int emerge = Earth.EmergePatches + 1 + Earth.EmergeSlabs + Earth.EmergeRocksA + Earth.EmergeRocksB + Earth.EmergeClods
            + Earth.EmergeBigClods + Earth.EmergeGrains + Earth.EmergeShock + Earth.EmergeLowDust + Earth.EmergeTufts;
        Assert.That(dive, Is.LessThanOrEqualTo(260));
        Assert.That(emerge, Is.LessThanOrEqualTo(360));
        Assert.That(Earth.BulgeSlabs + Earth.BulgeGrains + Earth.BulgePebbles, Is.LessThanOrEqualTo(40));
    }

    [Test]
    public void Pools_CoverBackToBackDives()
    {
        int every = Math.Min(Simulation.ThicketDiveEveryPhase2Ticks, Simulation.ThicketDiveEveryPhase3Ticks);
        // Нырок длится 90 тиков (уход 12 + бугор 18 + круг 24 + стойка 36): следующий начинается не раньше.
        int dive = Simulation.ThicketDiveBurrowTicks + Simulation.ThicketDiveTravelTicks + Simulation.ThicketDiveLockTicks
            + Simulation.ThicketDiveStandTicks;
        float interval = Math.Max(every, dive) / (float)Simulation.TicksPerSecond;
        Assert.That(Earth.DivePool, Is.GreaterThanOrEqualTo(2));
        Assert.That(Earth.EmergePool, Is.GreaterThanOrEqualTo(2));
        Assert.That(Earth.BulgePool, Is.GreaterThanOrEqualTo(1));
        Assert.That(Earth.DivePool * interval, Is.GreaterThan(Earth.DiveLife), "уход не перезапускает живой экземпляр");
        Assert.That(Earth.EmergePool * interval, Is.GreaterThan(Earth.EmergeLife), "выход не перезапускает живой экземпляр");
        Assert.That(dive / (float)Simulation.TicksPerSecond, Is.GreaterThan(Earth.BulgeSeconds), "вздутие кончается до следующего нырка");
    }

    // ---------------------------------------------------------------- ход под землёй (V17, владелец 08.10: «как будто холмик просто
    // скользит по полу… без вау-эффекта пробуривания земли»): кучи головы нет, земля рвётся на месте, за головой — траншея

    [Test]
    public void Burst_FallsBackWhereItRose_AndStaysBelowTheHero()
    {
        Assert.That(Earth.BurstDrift, Is.LessThanOrEqualTo(Earth.HeadHalfWidth), "вырванная плита падает туда, где встала (снос ≤ полуширины разлома)");
        Assert.That(Earth.BurstApex, Is.InRange(.25f, .7f), "плиты рвутся из пола заметно, но ниже пояса героя");
        Assert.That(Earth.BurstSpeedMin, Is.LessThan(Earth.BurstSpeedMax));
        Assert.That(Earth.BurstLifeMax, Is.LessThanOrEqualTo(Earth.MoundTrailMaxLife), "лежат и уходят до конца хвоста бугра");
        Assert.That(Earth.LipSlabLifeMax, Is.LessThanOrEqualTo(Earth.MoundTrailMaxLife));
        Assert.That(Earth.LipSlabTiltMin, Is.GreaterThan(.35f), "плиты на губах вздыблены, а не лежат плашмя");
        Assert.That(Earth.LipSlabTiltMax, Is.LessThan(1.3f), "и не стоят торчком");
    }

    [Test]
    public void Ripple_LiftsTheGroundSlightly_AheadOfTheHead()
    {
        Assert.That(Earth.RipplePeak, Is.GreaterThan(0f), "плита ряби выходит над полом");
        Assert.That(Earth.RipplePeak, Is.LessThanOrEqualTo(Earth.RippleLift), "чуть приподнимается, а не взлетает");
        Assert.That(Earth.RippleNear, Is.GreaterThan(Earth.HeadAhead), "рябь — впереди разлома, а не в нём");
        Assert.That(Earth.RippleNearHalf, Is.LessThan(Earth.RippleFarHalf), "конус расширяется вперёд");
        Assert.That(Earth.RippleFar / Earth.MoundRefSpeed, Is.LessThanOrEqualTo(.45f), "голова доходит до ряби меньше чем за полсекунды");
        // К концу жизни плита ряби — снова под полом (столкновения нет: прячется сама).
        float g = Earth.Gravity * Earth.RippleGravity, t = Earth.RippleLifeMin;
        foreach (float v in new[] { Earth.RippleSpeedMin, Earth.RippleSpeedMax })
            Assert.That(-Earth.RippleDepth + v * t - .5f * g * t * t, Is.LessThan(-Earth.RippleDepth + .01f), $"плита {v} м/с уходит под пол");
    }

    [Test]
    public void Furrow_IsATornTrench_NotAHill()
    {
        // За головой (вспучивания нет): губы дёрна выше земли внутри даже с самым большим навалом — траншея, не вал V15.
        float lipLow = Earth.FurrowBody(3) * .9f;
        for (int c = 5; c <= 9; c++)
            Assert.That(Earth.FurrowBody(c) * Earth.FurrowRoughMax(c), Is.LessThan(lipLow), $"столбец {c} — ниже губ");
        Assert.That(Earth.FurrowBody(3), Is.EqualTo(Earth.FurrowBody(11)));
        // Самая высокая точка (над головой, с комьями и перелётом) — ниже вала V9–V14 (0,78 м) и кучи V15 (0,9 м).
        Assert.That(Earth.FurrowPeak, Is.LessThan(.75f), "разлом, а не холмик");
        Assert.That(Earth.FurrowHeight * Earth.FurrowBody(3), Is.InRange(.3f, .5f), "губы ~0,4 м — траншея видна с камеры боя");
        Assert.That(2f * Earth.FurrowHalfWidth, Is.InRange(1.8f, 2.4f), "ширина — под тело 4,14 м");
    }

    [Test]
    public void Furrow_HeavesOverTheHead_SettlesBehind()
    {
        Assert.That(Earth.FurrowHeave(0f), Is.EqualTo(0f));
        Assert.That(Earth.FurrowHeave(-1f), Is.EqualTo(0f));
        Assert.That(Earth.FurrowHeave(float.NaN), Is.EqualTo(0f));
        Assert.That(Earth.FurrowHeave(Earth.FurrowHeaveRise), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(Earth.FurrowHeave(Earth.FurrowHeaveRise + Earth.FurrowHeaveFall), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(Earth.FurrowHeave(5f), Is.EqualTo(0f));
        for (float b = 0f; b < 3f; b += .05f) Assert.That(Earth.FurrowHeave(b), Is.InRange(0f, 1f));
        // Над головой середина вспучена почти до губ — земля над телом поднята и порвана; за головой — низкое дно.
        float centreHeaved = Earth.FurrowBody(7) + Earth.FurrowHeadExtra(7), lipHeaved = Earth.FurrowBody(3) + Earth.FurrowHeadExtra(3);
        Assert.That(centreHeaved, Is.GreaterThan(.75f * lipHeaved), "земля над телом поднята к губам");
        Assert.That(centreHeaved, Is.LessThan(lipHeaved), "и порвана — губы выше");
        Assert.That(Earth.FurrowBody(7), Is.LessThan(.35f * Earth.FurrowBody(3)), "за головой — низкое дно траншеи");
        for (int c = 0; c < Earth.FurrowColumns; c++)
            Assert.That(Earth.FurrowHeadExtra(c), Is.EqualTo(Earth.FurrowHeadExtra(Earth.FurrowColumns - 1 - c)), "вспучено симметрично");
    }

    [Test]
    public void Furrow_ShowsTheTravel_ThenCollapses()
    {
        Assert.That(Earth.FurrowLift(0f), Is.EqualTo(0f));
        Assert.That(Earth.FurrowLift(.1f), Is.GreaterThan(1f), "встаёт с перелётом");
        Assert.That(Earth.FurrowLift(Earth.FurrowHold - .05f), Is.EqualTo(1f).Within(1e-5), "держится до FurrowHold");
        Assert.That(Earth.FurrowLift(Earth.FurrowLife - .05f), Is.EqualTo(0f).Within(1e-5), "к концу жизни точки — в земле");
        // На ходу 7 м/с траншея ~11 м — виден весь ход «под героя» и почти весь длинный.
        Assert.That(Earth.FurrowLife * Earth.MoundRefSpeed, Is.InRange(9f, 13f));
        Assert.That(Earth.FurrowLife, Is.LessThanOrEqualTo(Earth.MoundTrailMaxLife), "траншея уходит не позже плит на губах");
        Assert.That(Earth.MoundTrailMaxLife, Is.LessThanOrEqualTo(2f), "хвост бугра — не дольше 2 с после головы");
    }

    [Test]
    public void TrailRate_FollowsDistanceNotTime()
    {
        Assert.That(Earth.MoundRefSpeed, Is.EqualTo(Simulation.ThicketMoundSpeed.ToFloat() * Simulation.TicksPerSecond).Within(1e-3),
            "частота префаба — на ходу Sim (7 м/с)");
        Assert.That(Earth.TrailRateScale(Earth.MoundRefSpeed), Is.EqualTo(1f).Within(1e-6));
        Assert.That(Earth.TrailRateScale(3.5f), Is.EqualTo(.5f).Within(1e-6), "вдвое медленнее — плит вдвое реже по времени");
        float catchUp = Simulation.ThicketMoundMaxStep.ToFloat() * Simulation.TicksPerSecond;
        Assert.That(Earth.TrailRateMax * Earth.MoundRefSpeed, Is.GreaterThanOrEqualTo(catchUp - 1e-3f), "догон убегающего (12 м/с) — тоже по метрам");
        foreach (float speed in new[] { 1.5f, 3.5f, 5f, 7f, 10f, catchUp })
            Assert.That(Earth.TrailRateScale(speed) / speed, Is.EqualTo(1f / Earth.MoundRefSpeed).Within(1e-5f), $"ход {speed} м/с — по метрам");
        Assert.That(Earth.TrailRateMin * Earth.MoundRefSpeed, Is.LessThanOrEqualTo(1.5f), "по метрам — до хода ≤ 1,5 м/с");
        Assert.That(Earth.TrailRateScale(0f), Is.EqualTo(Earth.TrailRateMin));
        Assert.That(Earth.TrailRateScale(float.NaN), Is.EqualTo(Earth.TrailRateMin));
    }

    [Test]
    public void Furrow_ColumnsMonotonic_TurfOutside_SoilInside()
    {
        foreach (float tear in new[] { .45f, .55f, .65f })
        {
            float last = float.NegativeInfinity;
            for (int c = 0; c < Earth.FurrowColumns; c++)
            {
                float u = Earth.FurrowAcross(c, tear, tear);
                Assert.That(u, Is.GreaterThanOrEqualTo(last), $"столбец {c} не заходит за соседа");
                if (u == last) Assert.That(Earth.FurrowSeam(c - 1), "совпадают только точки шва дёрн | земля");
                last = u;
            }
        }
        for (int c = 0; c < Earth.FurrowColumns; c++)
        {
            bool outside = Math.Abs(Earth.FurrowAcross(c, .55f, .55f)) > .55f + 1e-4f;
            if (outside) Assert.That(Earth.FurrowSoil(c), Is.False, "за линией разрыва — дёрн");
            if (Math.Abs(Earth.FurrowAcross(c, .55f, .55f)) < .55f - 1e-4f) Assert.That(Earth.FurrowSoil(c), "внутри — каменистая земля");
            Assert.That(Earth.FurrowBody(c), Is.EqualTo(Earth.FurrowBody(Earth.FurrowColumns - 1 - c)), "сечение симметрично");
        }
        Assert.That(Earth.FurrowBody(3), Is.GreaterThan(Earth.FurrowBody(7)), "губы дёрна выше дна траншеи");
        Assert.That(Earth.FurrowBody(5), Is.LessThan(Earth.FurrowBody(3)), "за губой — желоб");
        for (float s = 0f; s < 20f; s += .13f)
            Assert.That(Earth.FurrowTear(s, false), Is.InRange(.45f, .65f));
    }

    [Test]
    public void AtlasUv_StaysInsideItsHalf()
    {
        for (float x = -30f; x <= 30f; x += .37f)
            for (float z = -30f; z <= 30f; z += 1.9f)
            {
                Earth.AtlasUv(false, x, z, out float u, out float v);
                Assert.That(u, Is.InRange(0f, .5f), "дёрн — левая половина атласа");
                Assert.That(v, Is.InRange(0f, 1f));
                Earth.AtlasUv(true, x, z, out u, out v);
                Assert.That(u, Is.InRange(.5f, 1f), "земля — правая половина атласа");
                Assert.That(v, Is.InRange(0f, 1f));
            }
        // Без скачка: соседние точки ленты (0,24 м) — соседние UV.
        Earth.AtlasUv(false, 5.19f, 0f, out float a, out _);
        Earth.AtlasUv(false, 5.43f, 0f, out float b, out _);
        Assert.That(Math.Abs(a - b), Is.LessThan(.05f));
    }

    [Test]
    public void Mound_BudgetAndPool()
    {
        Assert.That(Earth.MoundParticlesPeak, Is.LessThanOrEqualTo(320));
        Assert.That(Earth.MoundTrianglesPeak, Is.LessThanOrEqualTo(32000), "все обломки разом и траншея — порядка V15 (~28 тыс.)");
        Assert.That(Earth.FurrowTriangles, Is.EqualTo((Earth.FurrowMaxSamples - 1) * 12 * 2), "лента: 12 четырёхугольников поперёк без швов");
        int every = Math.Min(Simulation.ThicketDiveEveryPhase2Ticks, Simulation.ThicketDiveEveryPhase3Ticks) * 85 / 100;
        int dive = Simulation.ThicketDiveBurrowTicks + Simulation.ThicketDiveTravelTicks + Simulation.ThicketDiveLockTicks
            + Simulation.ThicketDiveStandTicks;
        float interval = Math.Max(every, dive) / (float)Simulation.TicksPerSecond;
        Assert.That(Earth.MoundPool, Is.GreaterThanOrEqualTo(2), "новый нырок не рвёт хвост прошлого");
        Assert.That(Earth.MoundPool * interval, Is.GreaterThan(Earth.MoundLifeSeconds), "экземпляр с хвостом уходит до того, как пул вернётся к нему");
        Assert.That(Earth.MoundLifeSeconds, Is.EqualTo((Simulation.ThicketDiveTravelMaxTicks + Simulation.ThicketDiveLockTicks)
            / (float)Simulation.TicksPerSecond + Earth.MoundTrailMaxLife).Within(1e-5f), "самый длинный ход (60 тиков) + круг + хвост");
    }
}
