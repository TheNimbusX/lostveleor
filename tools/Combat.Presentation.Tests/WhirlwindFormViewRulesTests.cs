using Game.Sim;
using Game.View;
using NUnit.Framework;

// Вид форм Вихря (02.10): фронт кольца Пенных волн идёт по числам Sim, кольцо гаснет после хода,
// оглушение Водоворота отличается от оглушения удара якорем, ритм оборотов Бури — свой.
public sealed class WhirlwindFormViewRulesTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void FoamRingFrontMatchesSimOnWholeSteps(int ring)
    {
        int travel = Simulation.FoamRingTravelTicks(ring);
        const int start = 100;
        for (int step = 0; step <= travel + 2; step++)
        {
            // Шаг s хода кольца — тик start + s − 1; на нём Sim прошла фронтом до FoamRingRadiusAt(ring, s).
            float now = start + step - 1;
            float expected = Simulation.FoamRingRadiusAt(ring, step).ToFloat();
            Assert.That(PelagWhirlwindFormRules.FoamRingFront(ring, start, travel, now), Is.EqualTo(expected).Within(1e-4f), "шаг " + step);
        }
    }

    [Test]
    public void FoamRingFrontIsContinuousBetweenTicks()
    {
        float a = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 13f);
        float mid = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 13.5f);
        float b = PelagWhirlwindFormRules.FoamRingFront(1, 10, 12, 14f);
        Assert.That(mid, Is.GreaterThan(a).And.LessThan(b));
    }

    [Test]
    public void FoamRingStopsTravellingAtOuterRadius()
    {
        int travel = Simulation.FoamRingTravelTicks(0);
        Assert.That(PelagWhirlwindFormRules.FoamRingTravelling(50, travel, 50 + travel - 2), Is.True);
        Assert.That(PelagWhirlwindFormRules.FoamRingTravelling(50, travel, 50 + travel - 1), Is.False);
        Assert.That(PelagWhirlwindFormRules.FoamRingLifeSeconds(travel),
            Is.EqualTo((travel + PelagWhirlwindFormRules.FoamWaveLingerTicks) / (float)Simulation.TicksPerSecond));
    }

    [Test]
    public void MaelstromStaggerNeedsThePullBeforeIt()
    {
        int contact = PelagWhirlwindFormRules.MaelstromContactTick(200, 9);
        Assert.That(contact, Is.EqualTo(210));
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks, contact), Is.True);
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(240, Simulation.MaelstromStaggerTicks, contact), Is.False, "удар якорем позже");
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks + 1, contact), Is.False);
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(210, Simulation.MaelstromStaggerTicks, -1), Is.False, "Водоворота не было");
    }

    // ---- v3 (владелец 02.10, вечер): волны «застывают», Водоворот «не плавный»

    [TestCase(0)]
    [TestCase(1)]
    public void FoamCrestStartsAndEndsWithTheSimFront(int ring)
    {
        int travel = Simulation.FoamRingTravelTicks(ring);
        const int start = 100;
        Assert.That(PelagWhirlwindFormRules.FoamCrestRadius(ring, start, travel, start - 1),
            Is.EqualTo(Simulation.FoamRingInnerRadius.ToFloat()).Within(1e-4f));
        Assert.That(PelagWhirlwindFormRules.FoamCrestRadius(ring, start, travel, start + travel - 1),
            Is.EqualTo(Simulation.FoamRingOuterRadius(ring).ToFloat()).Within(1e-4f));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void SimFrontStaysInsideTheRingWater(int ring)
    {
        int travel = Simulation.FoamRingTravelTicks(ring);
        const int start = 100;
        for (float now = start - 1; now <= start + travel - 1; now += .1f)
        {
            float crest = PelagWhirlwindFormRules.FoamCrestRadius(ring, start, travel, now);
            float sim = PelagWhirlwindFormRules.FoamRingFront(ring, start, travel, now);
            Assert.That(crest, Is.GreaterThanOrEqualTo(sim - 1e-4f), "гребень не отстаёт от удара");
            Assert.That(crest - sim, Is.LessThan(2f * PelagWhirlwindFormRules.FoamBandHalfWidth(crest) - .05f),
                "фронт Sim внутри полосы воды, t=" + now);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FoamCrestNeverFreezes(int ring)
    {
        int travel = Simulation.FoamRingTravelTicks(ring);
        const int start = 100;
        const float h = .05f;
        float Speed(float now) => (PelagWhirlwindFormRules.FoamCrestRadius(ring, start, travel, now + h)
            - PelagWhirlwindFormRules.FoamCrestRadius(ring, start, travel, now - h)) / (2f * h);
        float end = start + travel - 1;
        for (float now = start - .9f; now < end + 4f; now += .25f)
            Assert.That(Speed(now), Is.GreaterThan(.02f), "гребень стоит на t=" + now);
        // Скорость в конце хода непрерывна: нет ни рывка, ни стоп-кадра на передаче хода в дотекание.
        Assert.That(Speed(end - .15f), Is.EqualTo(Speed(end + .15f)).Within(Speed(end - .15f) * .15f));
        // Начало быстрее конца — разгон и замедление, а не ровная линия.
        Assert.That(Speed(start - .5f), Is.GreaterThan(2.5f * Speed(end - .2f)));
    }

    [Test]
    public void MaelstromSpinIsSmooth()
    {
        float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(9);
        const float dt = 1f / 60f;
        float previous = 0f, previousStep = 0f;
        for (int frame = 1; frame <= 60; frame++)
        {
            float angle = PelagWhirlwindFormRules.MaelstromSpinAngle(frame * dt, contact);
            float step = angle - previous;
            Assert.That(step, Is.GreaterThanOrEqualTo(0f), "вращение не дёргается назад");
            Assert.That(step, Is.LessThan(PelagWhirlwindFormRules.MaelstromSpinPeak * dt * 1.01f), "не быстрее пика");
            if (frame > 1) Assert.That(System.Math.Abs(step - previousStep), Is.LessThan(.015f), "без рывков скорости (разгон за 0,12 с — не больше 0,9° на кадр), кадр " + frame);
            previous = angle;
            previousStep = step;
        }
        Assert.That(PelagWhirlwindFormRules.MaelstromSpinSpeed(0f, contact), Is.EqualTo(0f), "разгон с места, без хлопка");
    }

    [Test]
    public void MaelstromArmsLieOnTheHitRadiusAtContact()
    {
        float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(9);
        Assert.That(PelagWhirlwindFormRules.MaelstromArmReach(contact, contact), Is.EqualTo(1f).Within(1e-4f));
        for (float t = 0f; t < 1f; t += .01f)
        {
            float reach = PelagWhirlwindFormRules.MaelstromArmReach(t, contact);
            Assert.That(reach, Is.InRange(.92f, 1.101f), "рукава не шире удара больше чем на 10%, t=" + t);
        }
        Assert.That(PelagWhirlwindFormRules.MaelstromHeadReveal(0f), Is.LessThan(.5f), "вода затекает к центру, а не хлопает целиком");
        Assert.That(PelagWhirlwindFormRules.MaelstromHeadReveal(.14f), Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void ChannelPulseFallsBackToTalentRhythm()
    {
        Assert.That(PelagWhirlwindFormRules.ChannelPulseTicks(null), Is.EqualTo(Simulation.WhirlwindPulseTicks));
        Assert.That(PelagWhirlwindFormRules.ChannelPulseTicks(new Simulation(1, 64)), Is.EqualTo(Simulation.WhirlwindPulseTicks),
            "без Бури — прежний период таланта");
        Assert.That(Simulation.StormPulseTicks, Is.LessThan(Simulation.WhirlwindPulseTicks));
    }

    // ---- v4 (проверка по выбранным кадрам waves-2 / vortex-1)

    [Test]
    public void FoamBandIsAboutAMetreLikeWaves2()
    {
        float atFirstOuter = 2f * PelagWhirlwindFormRules.FoamBandHalfWidth(Simulation.FoamRingOuterRadius(0).ToFloat());
        float atSecondOuter = 2f * PelagWhirlwindFormRules.FoamBandHalfWidth(Simulation.FoamRingOuterRadius(1).ToFloat());
        float atBirth = 2f * PelagWhirlwindFormRules.FoamBandHalfWidth(Simulation.FoamRingInnerRadius.ToFloat());
        Assert.That(atFirstOuter, Is.InRange(.9f, 1.15f), "у 3,5 м полоса около метра");
        Assert.That(atSecondOuter, Is.InRange(1.0f, 1.3f));
        Assert.That(atBirth, Is.LessThan(.75f), "на выходе у героя полоса уже — не накрывает его");
        Assert.That(2f * PelagWhirlwindFormRules.FoamBandHalfWidth(8f), Is.LessThanOrEqualTo(1.3f));
    }

    [Test]
    public void FirstRingStaysWaterWhileTheSecondLeaves()
    {
        int travel0 = Simulation.FoamRingTravelTicks(0), travel1 = Simulation.FoamRingTravelTicks(1);
        float secondLeaves = Simulation.FoamRingSecondDelayTicks / (float)Simulation.TicksPerSecond;
        // Белеть кольцо начинает за _Break.w (0,06) до разрыва в долях шейдера: 0,06/0,30 срока.
        float firstWhitens = PelagWhirlwindFormRules.FoamRingBreakSeconds(0, travel0) * (1f - .06f / PelagFoamRingWaterAge);
        Assert.That(firstWhitens, Is.GreaterThan(secondLeaves + .07f), "первое — чистая вода, пока второе выходит и отрывается");
        Assert.That(PelagWhirlwindFormRules.FoamRingBreakSeconds(1, travel1), Is.EqualTo(travel1 / 30f + .05f).Within(1e-4f),
            "второе рвётся как в v3");
        // До разрыва первое не замирает: гребень ещё едет.
        const int start = 100;
        float breakTick = start - 1 + PelagWhirlwindFormRules.FoamRingBreakSeconds(0, travel0) * Simulation.TicksPerSecond;
        float speed = (PelagWhirlwindFormRules.FoamCrestRadius(0, start, travel0, breakTick + .05f)
                       - PelagWhirlwindFormRules.FoamCrestRadius(0, start, travel0, breakTick - .05f)) / .1f * Simulation.TicksPerSecond;
        Assert.That(speed, Is.GreaterThan(1f), "в миг разрыва гребень первого кольца ещё бежит, м/с");
    }

    private const float PelagFoamRingWaterAge = .30f;

    [Test]
    public void MaelstromStrandsFlowInwardSmoothly()
    {
        const float duration = .23f, dt = 1f / 60f;
        Assert.That(PelagWhirlwindFormRules.MaelstromStrandTravel(0f, duration), Is.EqualTo(0f));
        Assert.That(PelagWhirlwindFormRules.MaelstromStrandTravel(duration, duration), Is.EqualTo(1f).Within(1e-5f));
        float previous = 0f, previousStep = 0f;
        for (int frame = 1; frame * dt <= duration; frame++)
        {
            float u = PelagWhirlwindFormRules.MaelstromStrandTravel(frame * dt, duration);
            float step = u - previous;
            Assert.That(step, Is.GreaterThan(0f), "голова струи всё время бежит внутрь, кадр " + frame);
            Assert.That(step, Is.GreaterThanOrEqualTo(previousStep - 1e-4f), "с разгоном — втягивает, кадр " + frame);
            Assert.That(step - previousStep, Is.LessThan(.03f), "без рывков скорости, кадр " + frame);
            previous = u;
            previousStep = step;
        }
        for (int i = 0; i < 18; i++)
            Assert.That(PelagWhirlwindFormRules.MaelstromStrandDelay(i, 18), Is.InRange(0f, .16f), "все струи стартуют, пока Sim тянет");
    }

    // ---- Длинная тяга (владелец 02.10: 9 тиков не читались → 16 с разгоном, контакт позже)

    [Test]
    public void MaelstromContactFollowsTheLongSimPull()
    {
        const int castTick = 300;
        int pull = Simulation.MaelstromPullTicks;
        int contact = PelagWhirlwindFormRules.MaelstromContactTick(castTick, pull);
        Assert.That(contact, Is.EqualTo(castTick + Simulation.MaelstromContactDelayTicks), "контакт вида — тик удара Sim");
        Assert.That(PelagWhirlwindFormRules.MaelstromContactSeconds(pull),
            Is.EqualTo(Simulation.MaelstromContactDelayTicks / (float)Simulation.TicksPerSecond).Within(1e-6f));
        Assert.That(PelagWhirlwindFormRules.MaelstromContactSeconds(pull), Is.InRange(.5f, .6f), "владелец: ~0,5–0,6 с");
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(contact, Simulation.MaelstromStaggerTicks, contact), Is.True);
        Assert.That(PelagWhirlwindFormRules.IsMaelstromStagger(castTick + Simulation.WhirlwindContactDelayTicks,
            Simulation.MaelstromStaggerTicks, contact), Is.False, "на прежнем 10-м тике оглушения Водоворота уже нет");
    }

    [Test]
    public void MaelstromArmsReachTheHitRadiusExactlyAtTheNewContact()
    {
        float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(Simulation.MaelstromPullTicks);
        Assert.That(PelagWhirlwindFormRules.MaelstromArmReach(contact, contact), Is.EqualTo(1f).Within(1e-4f), "2,3 м ровно в контакт");
        float previous = float.MaxValue;
        for (float t = 0f; t < contact - 1e-3f; t += 1f / 60f)
        {
            float reach = PelagWhirlwindFormRules.MaelstromArmReach(t, contact);
            Assert.That(reach, Is.GreaterThan(1f), "до контакта рукава ещё не легли на край удара, t=" + t);
            Assert.That(reach, Is.LessThanOrEqualTo(previous + 1e-6f), "наматываются внутрь без отката, t=" + t);
            previous = reach;
        }
        // Старая тяга: всё то же к своему контакту (правила не сломаны для короткого каста по темпу).
        float shortContact = PelagWhirlwindFormRules.MaelstromContactSeconds(9);
        Assert.That(PelagWhirlwindFormRules.MaelstromArmReach(shortContact, shortContact), Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void MaelstromWaterBreaksAfterTheContactAsAuthored()
    {
        float authored = PelagWhirlwindFormRules.MaelstromAuthoredContactSeconds;
        Assert.That(authored, Is.EqualTo(PelagWhirlwindFormRules.MaelstromContactSeconds(9)).Within(1e-6f),
            "вода v4 нарисована под тягу 9 тиков");
        // Нарисованная тяга — возраст без изменений.
        for (float t = 0f; t < 1.2f; t += .05f)
            Assert.That(PelagWhirlwindFormRules.MaelstromWaterAge(t, authored), Is.EqualTo(t).Within(1e-5f));
        float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(Simulation.MaelstromPullTicks);
        Assert.That(PelagWhirlwindFormRules.MaelstromWaterAge(contact, contact), Is.EqualTo(authored).Within(1e-5f),
            "в контакт вода того же возраста, что в принятой v4 — до удара не рвётся");
        float previous = -1f;
        for (float t = 0f; t < contact + .8f; t += 1f / 120f)
        {
            float age = PelagWhirlwindFormRules.MaelstromWaterAge(t, contact);
            Assert.That(age, Is.GreaterThan(previous), "возраст идёт вперёд, t=" + t);
            if (t <= contact) Assert.That(age, Is.LessThanOrEqualTo(authored + 1e-5f));
            else Assert.That(age - authored, Is.EqualTo(t - contact).Within(1e-5f), "после контакта распад секунда в секунду");
            previous = age;
        }
        Assert.That(PelagWhirlwindFormRules.MaelstromExtraLifeSeconds(authored), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(PelagWhirlwindFormRules.MaelstromExtraLifeSeconds(contact),
            Is.EqualTo((Simulation.MaelstromContactDelayTicks - Simulation.WhirlwindContactDelayTicks) / (float)Simulation.TicksPerSecond).Within(1e-5f),
            "объект живёт дольше ровно на сдвиг контакта");
    }

    [Test]
    public void MaelstromStrandsFlowTheWholeLongPull()
    {
        const int count = 18;
        float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(Simulation.MaelstromPullTicks);
        float pullSeconds = Simulation.MaelstromPullTicks / (float)Simulation.TicksPerSecond;
        float stretch = PelagWhirlwindFormRules.MaelstromPullStretch(contact);
        Assert.That(stretch, Is.EqualTo(Simulation.MaelstromContactDelayTicks / (float)Simulation.WhirlwindContactDelayTicks).Within(1e-5f));
        Assert.That(PelagWhirlwindFormRules.MaelstromPullStretch(PelagWhirlwindFormRules.MaelstromAuthoredContactSeconds),
            Is.EqualTo(1f).Within(1e-6f));
        Assert.That(PelagWhirlwindFormRules.MaelstromStrandDuration(0f, 1f), Is.EqualTo(.24f).Within(1e-6f), "нарисованные пути прежние");
        Assert.That(PelagWhirlwindFormRules.MaelstromStrandDuration(1f, 1f), Is.EqualTo(.30f).Within(1e-6f));

        float lastStart = 0f, lastArrival = 0f;
        for (int i = 0; i < count; i++)
        {
            float delay = PelagWhirlwindFormRules.MaelstromStrandDelay(i, count, stretch);
            Assert.That(delay, Is.EqualTo(PelagWhirlwindFormRules.MaelstromStrandDelay(i, count) * stretch).Within(1e-6f));
            lastStart = System.Math.Max(lastStart, delay);
            lastArrival = System.Math.Max(lastArrival, delay + PelagWhirlwindFormRules.MaelstromStrandDuration(1f, stretch));
        }
        Assert.That(lastStart, Is.LessThan(pullSeconds * .6f), "все струи стартуют в первой половине тяги");
        Assert.That(lastArrival, Is.GreaterThanOrEqualTo(pullSeconds), "струи текут до конца тяги, а не гаснут на её середине");
        Assert.That(PelagWhirlwindFormRules.MaelstromStrandDuration(0f, stretch), Is.LessThan(pullSeconds), "первые струи влились до контакта");
    }

    [Test]
    public void MaelstromDragTrailStartsOnTheFirstPullTick()
    {
        // Проверка 02.10: тяга x² и порог 6 см — след вставал на пятом-шестом тике.
        int pull = Simulation.MaelstromPullTicks;
        float firstShare = Simulation.MaelstromPullProgress(1, pull).ToFloat();
        Assert.That(PelagWhirlwindFormRules.MaelstromDragStart, Is.LessThan(.06f), "порог ниже прежних 6 см");
        Assert.That(1f * firstShare, Is.GreaterThanOrEqualTo(PelagWhirlwindFormRules.MaelstromDragStart),
            "путь 1 м: след — с первого тика тяги");
        Assert.That(1.6f * firstShare, Is.GreaterThan(PelagWhirlwindFormRules.MaelstromDragStart), "путь 1,6 м: тоже");
        Assert.That(.5f * Simulation.MaelstromPullProgress(2, pull).ToFloat(), Is.GreaterThan(PelagWhirlwindFormRules.MaelstromDragStart),
            "даже короткий путь 0,5 м — со второго");
        Assert.That(PelagWhirlwindFormRules.MaelstromDragStart, Is.GreaterThan(.005f), "но не от дрожи расталкивания");
    }

    [Test]
    public void MaelstromArmsAppearByWidthNotByFade()
    {
        Assert.That(PelagWhirlwindFormRules.MaelstromArmGrow(0f), Is.EqualTo(.30f).Within(1e-4f), "с первого кадра — плотная вода");
        Assert.That(PelagWhirlwindFormRules.MaelstromArmGrow(.08f), Is.EqualTo(1f).Within(1e-4f));
        float previous = 0f;
        for (float t = 0f; t <= .1f; t += 1f / 60f)
        {
            float grow = PelagWhirlwindFormRules.MaelstromArmGrow(t);
            Assert.That(grow, Is.GreaterThanOrEqualTo(previous));
            previous = grow;
        }
    }
}
