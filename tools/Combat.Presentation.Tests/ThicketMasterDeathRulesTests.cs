using Game.View;
using NUnit.Framework;
using Death = Game.View.ThicketMasterDeathRules;

/// <summary>
/// Хозяин Чащи — смерть «цветущий холм» (владелец 02.10, вечер: «смерть надо доработать»): наезд камеры
/// отпускает героя, тело уходит под холм раньше, чем ArenaView вернёт его в пул, цветы встают на уже
/// выросший холм по очереди и холм потом стоит. Секунды — от тика Death; касание боком — LandsAt профиля
/// (Death 90 кадров, касание на 65-м: 65/90 × 3 с), тело уходит в пул на BodyGoneAt (0,035 + 0,075 + 5,4).
/// </summary>
public sealed class ThicketMasterDeathRulesTests
{
    private const float Land = 65f / 90f * 3f;
    private const float BodyGoneAt = .035f + .075f + 5.4f;

    [Test]
    public void Push_GentleIn_HoldsThroughLanding_ThenReleases()
    {
        Assert.That(Death.Push(0f, Land), Is.EqualTo(0f));
        Assert.That(Death.Push(Death.PushInSeconds, Land), Is.EqualTo(1f).Within(1e-5), "наезд за 0,7 с");
        Assert.That(Death.Push(Land, Land), Is.EqualTo(1f).Within(1e-5), "касание боком — в наезде");
        Assert.That(Death.Push(Land + .5f, Land), Is.EqualTo(1f).Within(1e-5), "ревью 02.10, вечер: держится до касания + 0,5 с");
        Assert.That(Death.Push(Land + Death.PushHoldAfterLand + Death.PushOutSeconds, Land), Is.EqualTo(0f).Within(1e-5));
        Assert.That(Death.PushDone(Land + Death.PushHoldAfterLand + Death.PushOutSeconds, Land), Is.True);
        Assert.That(Death.PushDone(Land, Land), Is.False);
        float done = Land + Death.PushHoldAfterLand + Death.PushOutSeconds;
        Assert.That(done, Is.LessThan(4f), "наезд и плавный отпуск — к 4 с камера снова за героем");
        Assert.That(Death.PushOutSeconds, Is.GreaterThanOrEqualTo(1f), "отпуск плавный, не рывком");

        float last = 0f;
        for (float t = 0f; t <= Death.PushInSeconds; t += 1f / 30f)
        {
            float push = Death.Push(t, Land);
            Assert.That(push, Is.GreaterThanOrEqualTo(last - 1e-6f), "вход без рывков назад, " + t);
            last = push;
        }
    }

    [Test]
    public void Camera_ZoomsTo48_AndKeepsHeroInFrame()
    {
        // Ревью 02.10, вечер: «наезд почти не заметен» (6,2 → 5,2 и 65 % пути) — теперь 6,2 → 4,8 и 80 %.
        Assert.That(Death.CameraZoom(0f), Is.EqualTo(1f));
        Assert.That(Death.CameraZoom(1f) * Death.CombatSize, Is.EqualTo(4.8f).Within(1e-4), "6,2 → 4,8");
        Assert.That(Death.CameraBlend(1f, 3f), Is.EqualTo(Death.PushBlend).Within(1e-5), "герой рядом — 80 % пути к боссу");
        Assert.That(Death.PushBlend, Is.GreaterThanOrEqualTo(.75f));
        Assert.That(Death.CameraBlend(1f, 20f) * 20f, Is.LessThanOrEqualTo(Death.KeepHeroWithin + 1e-4), "далёкий герой не уходит за край");
        Assert.That(Death.CameraBlend(0f, 5f), Is.EqualTo(0f));

        // Полвысоты кадра в наезде по земле — 4,8 / sin 48° ≈ 6,46 м; герой (ноги на земле, голова 1,8 м) и
        // нижняя полоса HUD (~1,4 единицы кадра) — центр кадра не дальше 4,6 м от героя по вертикали экрана.
        float sin = (float)System.Math.Sin(48.0 * System.Math.PI / 180.0);
        Assert.That(Death.KeepHeroWithin * sin, Is.LessThanOrEqualTo(Death.PushSize - 1.4f), "ноги героя выше полосы HUD");
        Assert.That(Death.KeepHeroWithin * sin + 1.8f * (float)System.Math.Cos(48.0 * System.Math.PI / 180.0),
            Is.LessThanOrEqualTo(Death.PushSize - .2f), "голова героя в кадре сверху");
    }

    [Test]
    public void Body_SinksUnderRisingHill_BeforePoolTakesIt()
    {
        Assert.That(Death.HillRise(Land + Death.HillRiseDelay, Land), Is.EqualTo(0f));
        Assert.That(Death.HillRise(Land + Death.HillRiseDelay + Death.HillRiseSeconds, Land), Is.EqualTo(1f).Within(1e-5));
        Assert.That(Death.HillRiseSeconds, Is.InRange(1.5f, 2f), "владелец: тело уходит в холм за 1,5–2 с");
        Assert.That(Death.BodySink(Land, Land), Is.EqualTo(0f), "лежит, пока не коснулся");
        Assert.That(Death.SinkDelay, Is.GreaterThanOrEqualTo(Death.HillRiseDelay), "тело уходит, когда холм уже встаёт");
        float sunk = Land + Death.SinkDelay + Death.SinkSeconds;
        Assert.That(Death.BodySink(sunk, Land), Is.EqualTo(1f).Within(1e-5));
        Assert.That(sunk, Is.LessThan(BodyGoneAt - .5f), "тело под землёй задолго до ухода в пул — не пропадает кадром");

        float hill = 0f, body = 0f;
        for (float t = Land; t <= sunk + .5f; t += 1f / 30f)
        {
            float h = Death.HillRise(t, Land), b = Death.BodySink(t, Land);
            Assert.That(h, Is.GreaterThanOrEqualTo(hill - 1e-6f));
            Assert.That(b, Is.GreaterThanOrEqualTo(body - 1e-6f));
            Assert.That(h, Is.GreaterThanOrEqualTo(b - 1e-4f), "холм не отстаёт от тела: тело уходит под землю, а не сквозь пол " + t);
            hill = h; body = b;
        }
    }

    [Test]
    public void ContactShadow_LeavesWithTheBody_NoDarkEdgeBesideTheHill()
    {
        // Тень 3,9 м стоит у корня (ThicketMasterAnimatorView.DefaultContactShadowMetres), холм — у середины тела,
        // до 1,8 м вбок от корня (ThicketMasterCombatView.HillCentroidReach).
        const float shadowRadius = 3.9f / 2f, centroidReach = 1.8f;
        Assert.That(Death.ShadowLeft(Death.BodySink(0f, Land)), Is.EqualTo(1f), "до касания тень целая");
        Assert.That(Death.ShadowLeft(Death.BodySink(Land, Land)), Is.EqualTo(1f), "лежит — тень под ним");
        float sunk = Land + Death.SinkDelay + Death.SinkSeconds;
        Assert.That(Death.ShadowLeft(Death.BodySink(sunk, Land)), Is.EqualTo(0f).Within(1e-5), "тело ушло — тени нет");
        Assert.That(sunk, Is.LessThan(BodyGoneAt - .5f), "задолго до ухода тела в пул");
        Assert.That(Death.ShadowLeft(-1f), Is.EqualTo(1f));
        Assert.That(Death.ShadowLeft(2f), Is.EqualTo(0f));

        float grown = Land + Death.HillRiseDelay + Death.HillRiseSeconds;
        float edge = centroidReach + shadowRadius * Death.ShadowLeft(Death.BodySink(grown, Land));
        Assert.That(edge, Is.LessThanOrEqualTo(Death.HillHalfWidth), "холм в полный рост — край тени под ним и при сдвиге вбок");

        float last = 1f;
        for (float t = 0f; t <= sunk + .5f; t += 1f / 30f)
        {
            float left = Death.ShadowLeft(Death.BodySink(t, Land));
            Assert.That(left, Is.LessThanOrEqualTo(last + 1e-6f), "тень только сжимается, " + t);
            last = left;
        }
    }

    [Test]
    public void Flowers_BloomOneByOne_OnGrownHill()
    {
        const int count = 40;
        float first = Death.FlowerStart(0, count, Land), lastStart = Death.FlowerStart(count - 1, count, Land);
        Assert.That(first, Is.EqualTo(Land + Death.BloomDelay).Within(1e-5));
        Assert.That(lastStart - first, Is.EqualTo(Death.BloomSpread).Within(1e-5));
        Assert.That(Death.HillRise(first, Land), Is.GreaterThan(.99f), "цветок встаёт на выросший холм, не висит над ним");
        for (int i = 1; i < count; i++)
            Assert.That(Death.FlowerStart(i, count, Land), Is.GreaterThan(Death.FlowerStart(i - 1, count, Land)), "по очереди, " + i);

        Assert.That(Death.FlowerGrow(0f), Is.EqualTo(0f));
        Assert.That(Death.FlowerGrow(Death.FlowerGrowSeconds), Is.EqualTo(1f));
        Assert.That(Death.FlowerGrow(Death.FlowerGrowSeconds * .75f), Is.EqualTo(1.1f).Within(1e-4), "раскрывается с перелётом");
        Assert.That(Death.FlowerGrow(10f), Is.EqualTo(1f), "и стоит");
    }

    [Test]
    public void Hill_SettlesAfterEverythingGrewAndFell()
    {
        float bloomed = Death.FlowerStart(39, 40, Land) + Death.FlowerGrowSeconds;
        float petals = Land + Death.BloomDelay + Death.PetalDriftSeconds + 3f;
        Assert.That(Death.Settled(bloomed, Land), Is.False);
        Assert.That(Land + Death.SettledAfterLand, Is.GreaterThanOrEqualTo(petals), "последние лепестки долетели до остановки частиц");
        Assert.That(Death.Settled(Land + Death.SettledAfterLand, Land), Is.True);
        Assert.That(Death.HillLifeSeconds, Is.GreaterThan(3600f), "холм живёт до смены арены, а не таймером");
        Assert.That(Death.HillHalfWidth * 2f, Is.InRange(5f, 7f), "V13: пригорок шире (6,2 м) — ниже и положе при той же форме");
        Assert.That(Death.HillHalfLength * 2f, Is.InRange(6.5f, 8.5f));
        Assert.That(Death.HillHeight, Is.InRange(1.2f, 1.4f), "владелец 03.10, утро: по холму ходят — 1,2–1,4 м");
        Assert.That(Death.HillBaseHeight, Is.LessThanOrEqualTo(.5f), "основание низкое — края у героя не по пояс");
    }

    /// <summary>
    /// V11 (03.10): в тёмной половине поляны цветение было без света. Тёплый точечный свет над пригорком загорается с
    /// цветением, на пике — пока цветы раскрываются, потом садится до ровного слабого и горит, пока лежит холм.
    /// </summary>
    [Test]
    public void KnollLight_RisesWithTheBloom_ThenSettlesToALowSteadyGlow()
    {
        float first = Death.FlowerStart(0, 40, Land), lastStart = Death.FlowerStart(39, 40, Land);
        Assert.That(Death.KnollLight(0f, Land), Is.EqualTo(0f), "на ударе света нет");
        Assert.That(Death.KnollLight(Land + Death.HillRiseDelay + Death.HillRiseSeconds * .5f, Land), Is.EqualTo(0f), "пока холм встаёт — тоже");
        Assert.That(Death.KnollLight(first, Land), Is.GreaterThan(0f), "с первым цветком свет уже загорается");

        float peak = 0f, peakAt = 0f, last = 0f;
        bool falling = false;
        for (float t = first - 1f; t <= lastStart + Death.KnollLightSettle + 2f; t += 1f / 30f)
        {
            float k = Death.KnollLight(t, Land);
            Assert.That(k, Is.InRange(0f, 1f), "доля пика, " + t);
            if (k > peak) { peak = k; peakAt = t; }
            if (k < last - 1e-6f) falling = true;
            else if (falling) Assert.That(k, Is.LessThanOrEqualTo(last + 1e-6f), "после пика только садится, " + t);
            last = k;
        }
        Assert.That(peak, Is.EqualTo(1f).Within(.01f));
        Assert.That(peakAt, Is.InRange(first, lastStart + .1f), "пик — пока цветы раскрываются");
        float settled = lastStart + Death.KnollLightSettle;
        Assert.That(Death.KnollLight(settled + .01f, Land), Is.EqualTo(Death.KnollLightRest).Within(1e-4));
        Assert.That(Death.KnollLight(Land + 600f, Land), Is.EqualTo(Death.KnollLightRest).Within(1e-4), "и так горит, пока лежит холм");
        Assert.That(Death.KnollLightRest, Is.InRange(.3f, .7f), "ровный свет слабее пика, но не гаснет");
        Assert.That(Death.KnollLightToCamera, Is.InRange(0f, 1.5f), "свет над пригорком, а не у героя");
    }

    [Test]
    public void ClearedBanner_WaitsForTheHill_NotOnTheKill()
    {
        // Ревью 02.10, вечер: «АРЕНА ЗАЧИЩЕНА» вылезала в первую секунду смерти и наступала на момент.
        float at = Death.ClearedBannerAt(Land);
        Assert.That(at, Is.EqualTo(4f).Within(.2f), "≈ 4 с после удара");
        Assert.That(Death.HillRise(at, Land), Is.EqualTo(1f).Within(1e-5), "холм уже встал");
        Assert.That(Death.BodySink(at, Land), Is.EqualTo(1f).Within(1e-5), "тело уже под холмом");
        float released = Land + Death.PushHoldAfterLand + Death.PushOutSeconds;
        Assert.That(at, Is.GreaterThanOrEqualTo(released), "плашка — когда камера уже вернулась к герою");
    }

    /// <summary>
    /// V13 (владелец 03.10, утро: «холм после смерти, если по нему пройтись, оч коряво выглядит»): по пригорку ходят.
    /// Рельеф — один для сеток сборки и для пола тел: край — ноль и сходит к полу без ступеньки, вершина — ровно
    /// HillHeight, склон не круче KnollMaxSlope (~35°), кромка мха — ступенька в 2–4 см.
    /// </summary>
    [Test]
    public void Knoll_IsAWalkableMound_EdgeZeroPeakExactSlopeBounded()
    {
        float peak = 0f, steepest = 0f, lip = 0f, surfaceSteepest = 0f;
        const float e = .05f, f = .1f;
        for (float x = -Death.HillHalfWidth * 1.15f; x <= Death.HillHalfWidth * 1.15f; x += .05f)
            for (float z = -Death.HillHalfLength * 1.15f; z <= Death.HillHalfLength * 1.15f; z += .05f)
            {
                float top = Death.KnollTop(x, z), surface = Death.KnollSurface(x, z);
                peak = System.Math.Max(peak, surface);
                Assert.That(surface, Is.LessThanOrEqualTo(top + 1e-5f), "мох не выше верха");
                lip = System.Math.Max(lip, top - surface);
                float gx = (Death.KnollTop(x + e, z) - Death.KnollTop(x - e, z)) / (2f * e);
                float gz = (Death.KnollTop(x, z + e) - Death.KnollTop(x, z - e)) / (2f * e);
                steepest = System.Math.Max(steepest, (float)System.Math.Sqrt(gx * gx + gz * gz));
                float sx = (Death.KnollSurface(x + f, z) - Death.KnollSurface(x - f, z)) / (2f * f);
                float sz = (Death.KnollSurface(x, z + f) - Death.KnollSurface(x, z - f)) / (2f * f);
                surfaceSteepest = System.Math.Max(surfaceSteepest, (float)System.Math.Sqrt(sx * sx + sz * sz));
                if (Death.KnollDistance(x, z) >= 1f) Assert.That(surface, Is.EqualTo(0f), "за краем — пол");
                if (Death.OffKnoll(x, z)) Assert.That(Death.KnollDistance(x, z), Is.GreaterThanOrEqualTo(1f), "быстрый отказ — только вне холма");
            }
        Assert.That(peak, Is.EqualTo(Death.HillHeight).Within(.005f), "вершина — HillHeight");
        Assert.That(Death.HillHeight, Is.InRange(1.2f, 1.4f), "владелец 03.10: по холму ходят — 1,2–1,4 м, а не гора 1,8");
        Assert.That(steepest, Is.LessThanOrEqualTo(Death.KnollMaxSlope), "склон не круче ~35°");
        Assert.That(Death.KnollMaxSlope, Is.LessThanOrEqualTo((float)System.Math.Tan(35.0 * System.Math.PI / 180.0) + 1e-3f));
        Assert.That(lip, Is.LessThanOrEqualTo(.04f), "кромка мха над кольцом земли — ступенька не выше 4 см");
        Assert.That(surfaceSteepest, Is.LessThanOrEqualTo((float)System.Math.Tan(38.0 * System.Math.PI / 180.0)), "видимый верх под стопой (±10 см) не круче 38°");

        // Подножие сходит к полу без ступеньки: у края (последние 3 % радиуса) ниже сантиметра и почти плоско.
        for (float a = 0f; a < 6.28f; a += .1f)
        {
            float rx = (float)System.Math.Sin(a), rz = (float)System.Math.Cos(a);
            float edge = 0f;
            for (float r = 0f; r < 6f; r += .005f)
                if (Death.KnollDistance(rx * r, rz * r) < 1f) edge = r;
            float inside = edge * .97f;
            Assert.That(Death.KnollSurface(rx * inside, rz * inside), Is.LessThan(.01f), "у края почти пол, " + a);
            float step = (Death.KnollSurface(rx * inside, rz * inside) - Death.KnollSurface(rx * (inside - .05f), rz * (inside - .05f))) / .05f;
            Assert.That(System.Math.Abs(step), Is.LessThan(.12f), "у края почти плоско, " + a);
            Assert.That(Death.KnollSurface(rx * (edge + .01f), rz * (edge + .01f)), Is.EqualTo(0f));
        }

        // Середина — на пригорке, основание (земля) ниже верха и не выше HillBaseHeight.
        Assert.That(Death.KnollSurface(0f, 0f), Is.GreaterThan(1f));
        Assert.That(Death.KnollGround(0f, 0f), Is.LessThanOrEqualTo(Death.HillBaseHeight));
        Assert.That(Death.KnollGround(0f, 0f), Is.LessThan(Death.KnollSurface(0f, 0f)));
    }

    /// <summary>
    /// Тела встают на пригорок вместе с ним: до роста его нет (пол поляны выше), в рост — ровно видимая сетка
    /// (опущена на HillSink, растёт по высоте и вширь по ключам кривых частицы), в полный рост — KnollSurface.
    /// </summary>
    [Test]
    public void KnollFloor_RisesWithTheMeshes_NothingBeforeFullAfter()
    {
        float start = Land + Death.HillRiseDelay, grown = start + Death.HillRiseSeconds;
        Assert.That(Death.KnollRiseFraction(Land, Land), Is.EqualTo(0f));
        Assert.That(Death.KnollRiseFraction(start, Land), Is.EqualTo(0f).Within(1e-5));
        Assert.That(Death.KnollRiseFraction(grown, Land), Is.EqualTo(1f).Within(1e-5));
        Assert.That(Death.KnollRiseFraction(grown + 100f, Land), Is.EqualTo(1f));

        // Кривые роста — те же, что у сборки: ключи RiseCurve, линейно между ними.
        Assert.That(Death.HillRiseHeight(0f), Is.EqualTo(0f));
        Assert.That(Death.HillRiseHeight(1f), Is.EqualTo(1f).Within(1e-6));
        Assert.That(Death.HillRiseWidth(0f), Is.EqualTo(Death.HillRiseWidthFrom).Within(1e-6));
        Assert.That(Death.HillRiseWidth(1f), Is.EqualTo(1f).Within(1e-6));
        for (int k = 0; k < Death.HillRiseHeightKeys; k++)
        {
            float x = k / (Death.HillRiseHeightKeys - 1f);
            Assert.That(Death.HillRiseHeight(x), Is.EqualTo(Death.RiseCurve(x)).Within(1e-5), "ключ " + k);
        }
        float mid = .5f / (Death.HillRiseHeightKeys - 1f);
        Assert.That(Death.HillRiseHeight(mid), Is.EqualTo((Death.RiseCurve(0f) + Death.RiseCurve(2f * mid)) * .5f).Within(1e-5), "между ключами — линейно");

        // До роста и вне пригорка — ниже пола; в полный рост — видимый верх.
        Assert.That(Death.KnollFloor(0f, 0f, 0f), Is.LessThanOrEqualTo(0f));
        Assert.That(Death.KnollFloor(0f, 0f, 1f), Is.EqualTo(Death.KnollSurface(0f, 0f)).Within(1e-5));
        Assert.That(Death.KnollFloor(1f, -1.5f, 1f), Is.EqualTo(Death.KnollSurface(1f, -1.5f)).Within(1e-5));
        float last = -1f, fastest = 0f;
        for (float t = Land; t <= grown + .5f; t += 1f / 60f)
        {
            float rise = Death.KnollRiseFraction(t, Land);
            for (float x = -4f; x <= 4f; x += .5f)
                Assert.That(Death.KnollFloor(x, Death.HillHalfLength * 1.2f, rise), Is.LessThanOrEqualTo(0f), "за краем — пол, " + t);
            float centre = Death.KnollFloor(0f, 0f, rise);
            Assert.That(centre, Is.GreaterThanOrEqualTo(last - 1e-5f), "середина только поднимается, " + t);
            if (last > -1f) fastest = System.Math.Max(fastest, (centre - last) * 60f);
            last = centre;
        }
        Assert.That(fastest, Is.LessThan(3f), "подъём не рывком: не быстрее 3 м/с (5 см за кадр 60 Гц)");
        Assert.That(Death.KnollFloor(0f, 0f, Death.KnollRiseFraction(grown, Land)), Is.EqualTo(Death.KnollSurface(0f, 0f)).Within(1e-5));
    }

    /// <summary>
    /// V13, находка ревью: по пригорку ходят, а свет висит 2,9 м над корнем — голова героя на вершине (~3,1 м) доходила
    /// до него, и точечный свет в упор пересвечивал героя (высветлять героя владелец запретил 24.09). Рядом с героем свет
    /// поднимается над головой и гаснет на остаток сближения: на героя падает не больше, чем от света в
    /// KnollLightHeroClear м (как стоящему под светом до V13); дальше KnollLightHeroReach свет как в сборке; пока герой
    /// идёт через пригорок, подъём и яркость меняются гладко и свет не гаснет.
    /// </summary>
    [Test]
    public void KnollLight_LiftsOverTheHeroOnTheKnoll_NeverBlowsHimOut()
    {
        const float clear2 = Death.KnollLightHeroClear * Death.KnollLightHeroClear;
        Assert.That(Death.KnollLightHeroClear, Is.InRange(1.4f, 2f), "как до V13: свет 3,4 м над полом, голова ~1,8 м");
        Assert.That(Death.KnollLightHeroHeight, Is.InRange(1.8f, 2.2f), "рост героя до макушки");
        float dimmest = 1f;
        // Свет сборки — 2,9 м над корнем (с запасом 2,5–3,5), сдвинут к камере на KnollLightToCamera в любую сторону;
        // герой идёт прямыми через точку под светом по 1 см.
        foreach (float lightY in new[] { 2.5f, 2.9f, 3.5f })
            for (int a = 0; a < 8; a++)
            {
                float lx = Death.KnollLightToCamera * (float)System.Math.Sin(a * System.Math.PI / 4);
                float lz = Death.KnollLightToCamera * (float)System.Math.Cos(a * System.Math.PI / 4);
                for (int w = 0; w < 4; w++)
                {
                    float wx = (float)System.Math.Sin(w * System.Math.PI / 4), wz = (float)System.Math.Cos(w * System.Math.PI / 4);
                    float lastLift = float.NaN, lastNear = float.NaN;
                    for (int step = -500; step <= 500; step++)
                    {
                        float s = step * .01f, x = lx + wx * s, z = lz + wz * s, horizontal = System.Math.Abs(s);
                        float feet = Death.KnollSurface(x, z);
                        float lift = Death.KnollLightLift(horizontal, lightY, feet);
                        float y = lightY + lift, near = Death.KnollLightNear(horizontal, y, feet);
                        string at = $"свет {lightY} м, сторона {a}, путь {w}, {s:0.00} м";
                        Assert.That(lift, Is.GreaterThanOrEqualTo(0f), at);
                        Assert.That(near, Is.InRange(0f, 1f), at);
                        float head = feet + Death.KnollLightHeroHeight;
                        float dy = y > head ? y - head : y < feet ? feet - y : 0f;
                        float d2 = horizontal * horizontal + dy * dy;
                        Assert.That(near * clear2, Is.LessThanOrEqualTo(d2 + 1e-4f), "на героя не больше, чем от света в KnollLightHeroClear м: " + at);
                        if (d2 >= clear2) Assert.That(near, Is.EqualTo(1f), "не ближе KnollLightHeroClear — яркость как была: " + at);
                        if (horizontal >= Death.KnollLightHeroReach) Assert.That(lift, Is.EqualTo(0f), "далеко от героя свет на месте: " + at);
                        if (!float.IsNaN(lastLift))
                        {
                            Assert.That(System.Math.Abs(lift - lastLift), Is.LessThan(.03f), "свет поднимается гладко (≤ 3 см на 1 см шага): " + at);
                            Assert.That(System.Math.Abs(near - lastNear), Is.LessThan(.03f), "и гладко гаснет: " + at);
                        }
                        lastLift = lift;
                        lastNear = near;
                        if (lightY == 2.9f) dimmest = System.Math.Min(dimmest, near);
                    }
                }
            }
        Assert.That(dimmest, Is.GreaterThan(.5f), "работу делает подъём: свет 2,9 м у героя не гаснет больше чем вдвое");

        // Прямо под светом на вершине: свет над макушкой ровно на KnollLightHeroClear и горит в полную силу.
        float top = Death.KnollSurface(0f, -Death.KnollLightToCamera);
        float under = Death.KnollLightLift(0f, 2.9f, top);
        Assert.That(2.9f + under, Is.EqualTo(top + Death.KnollLightHeroHeight + Death.KnollLightHeroClear).Within(1e-4));
        Assert.That(Death.KnollLightNear(0f, 2.9f + under, top), Is.EqualTo(1f).Within(1e-4));
        // На полу у подножия (дальше KnollLightHeroReach) — свет как в сборке.
        Assert.That(Death.KnollLightLift(Death.KnollLightHeroReach, 2.9f, 0f), Is.EqualTo(0f));
        Assert.That(Death.KnollLightNear(Death.KnollLightHeroReach, 2.9f, 0f), Is.EqualTo(1f));
    }
}
