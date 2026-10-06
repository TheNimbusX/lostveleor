using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// Вид Абордажа v2 (02.10) без Unity: якорь приходит к укусу в тик зацепа показа, цепь
    /// натянута с зацепа, Damage слота разбирается по событиям Sim, видимый край воды форм —
    /// край урона Sim, подъём Гейзера кончается ровно в тик падения воды.
    /// </summary>
    public sealed class AbordageVfxRulesTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void ShownTick_IsTwoTicksBehindTheSim()
        {
            Assert.That(PelagAbordageVfxRules.ShownTick(10, .5f), Is.EqualTo(8.5f).Within(Eps));
        }

        [Test]
        public void Anchor_LeavesTheHandOnRelease_AndBitesExactlyOnTheHookTick()
        {
            Assert.That(PelagAbordageVfxRules.AnchorTravel(4f, 4, 7), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.AnchorTravel(7f, 4, 7), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.AnchorTravel(3f, 4, 7), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.AnchorTravel(9f, 4, 7), Is.EqualTo(1f).Within(Eps));
            // Бросок резкий: к середине срока пройдено больше половины, и путь не идёт назад.
            Assert.That(PelagAbordageVfxRules.AnchorTravel(5.5f, 4, 7), Is.GreaterThan(.5f));
            float previous = 0f;
            for (float s = 4f; s <= 7f; s += .1f)
            {
                float travel = PelagAbordageVfxRules.AnchorTravel(s, 4, 7);
                Assert.That(travel, Is.GreaterThanOrEqualTo(previous - Eps));
                previous = travel;
            }
        }

        [Test]
        public void Retract_TakesSixTicks()
        {
            Assert.That(PelagAbordageVfxRules.Retract(20f, 20f, PelagAbordageVfxRules.RetractTicks), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.Retract(26f, 20f, PelagAbordageVfxRules.RetractTicks), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.Seconds(PelagAbordageVfxRules.RetractTicks), Is.EqualTo(.2f).Within(.001f));
        }

        [Test]
        public void Chain_IsPaidOutInFlight_TautFromTheHook_AndSlackAfterThePunch()
        {
            Assert.That(PelagAbordageVfxRules.Tension(5f, 7, -1f), Is.EqualTo(PelagAbordageVfxRules.TensionFlight).Within(Eps));
            Assert.That(PelagAbordageVfxRules.Tension(9f, 7, -1f), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.Tension(15f, 7, 12f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.ChainPayout(3f, true), Is.EqualTo(3f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.ChainPayout(3f, false), Is.GreaterThan(3f));
            Assert.That(PelagAbordageVfxRules.SleeveBreakAge(11f, 12f), Is.EqualTo(0f));
            Assert.That(PelagAbordageVfxRules.SleeveBreakAge(18f, 12f), Is.GreaterThan(.30f), "лента рвётся до конца смотки цепи");
        }

        [Test]
        public void Streak_IsShortAndFadesAfterTheBite()
        {
            Assert.That(PelagAbordageVfxRules.StreakLength(10f), Is.EqualTo(PelagAbordageVfxRules.StreakMax).Within(Eps));
            Assert.That(PelagAbordageVfxRules.StreakLength(1f), Is.EqualTo(.6f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.StreakAge(5f, 7, 1f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.StreakAge(12f, 7, 1f), Is.GreaterThan(.30f));
        }

        [Test]
        public void Hits_AreSortedByTheLastAbordageEventOfTheirTick()
        {
            const int fist = 4;
            Assert.That(Classify(AbordageVfxCue.Punch, 30, 30, fist), Is.EqualTo(AbordageVfxHit.Fist));
            Assert.That(Classify(AbordageVfxCue.Punch, 30, 30, 9), Is.EqualTo(AbordageVfxHit.Sweep));
            Assert.That(Classify(AbordageVfxCue.Quake, 30, 30, 9), Is.EqualTo(AbordageVfxHit.Quake));
            Assert.That(Classify(AbordageVfxCue.Breach, 30, 30, 9), Is.EqualTo(AbordageVfxHit.Breach));
            Assert.That(Classify(AbordageVfxCue.GeyserFall, 30, 30, fist), Is.EqualTo(AbordageVfxHit.Fall));
            // Фронт бьёт и в следующих тиках без своего события — по окну хода.
            Assert.That(PelagAbordageVfxRules.ClassifyHit(AbordageVfxCue.Punch, 30, fist, 33, 9, AbordageVfxCue.Quake, 30, 5),
                Is.EqualTo(AbordageVfxHit.Quake));
            Assert.That(PelagAbordageVfxRules.ClassifyHit(AbordageVfxCue.Punch, 30, fist, 35, 9, AbordageVfxCue.Quake, 30, 5),
                Is.EqualTo(AbordageVfxHit.Other));
            Assert.That(PelagAbordageVfxRules.ClassifyHit(AbordageVfxCue.None, -1, -1, 32, 9, AbordageVfxCue.Breach, 30, 4),
                Is.EqualTo(AbordageVfxHit.Breach));
            // Подъём Гейзера урона не наносит: Damage того же тика — не опознан (не корона падения).
            Assert.That(Classify(AbordageVfxCue.GeyserLift, 30, 30, fist), Is.EqualTo(AbordageVfxHit.Other));
        }

        private static AbordageVfxHit Classify(AbordageVfxCue cue, int cueTick, int hitTick, int target)
            => PelagAbordageVfxRules.ClassifyHit(cue, cueTick, 4, hitTick, target, AbordageVfxCue.None, -1, 0);

        [Test]
        public void QuakeCrest_ReachesTheDamageRadius_AndDriftsNoFurther()
        {
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            int travel = Simulation.AbordageQuakeTravelTicks;
            const int start = 40;
            float end = start + travel - PelagAbordageVfxRules.FrontLeadTicks;
            Assert.That(PelagAbordageVfxRules.QuakeCrest(end, start, travel, radius), Is.EqualTo(radius).Within(.001f));
            float late = PelagAbordageVfxRules.QuakeCrest(end + 60f, start, travel, radius);
            Assert.That(late, Is.LessThanOrEqualTo(radius + PelagAbordageVfxRules.FrontDrift + Eps));
            Assert.That(late, Is.GreaterThan(radius));
            float previous = 0f;
            for (float s = start - 1f; s <= end + 20f; s += .05f)
            {
                float crest = PelagAbordageVfxRules.QuakeCrest(s, start, travel, radius);
                Assert.That(crest, Is.GreaterThanOrEqualTo(previous - Eps), "гребень не идёт назад и не стоит рывком");
                previous = crest;
            }
        }

        [Test]
        public void QuakeCrest_TracksTheSimFront_AndKeepsItsSpeedThroughTheEnd()
        {
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            float step = Simulation.AbordageQuakeFrontStep.ToFloat();
            int travel = Simulation.AbordageQuakeTravelTicks;
            const int start = 40;
            for (int k = 1; k <= travel; k++)
            {
                // Шаг k фронта Sim бьёт в тик start + k − 1; знак задетого встаёт в этот тик показа.
                float crest = PelagAbordageVfxRules.QuakeCrest(start + k - 1, start, travel, radius);
                Assert.That(System.Math.Abs(crest - step * k), Is.LessThan(.45f), $"шаг {k}");
            }
            float end = start + travel - PelagAbordageVfxRules.FrontLeadTicks;
            const float h = .02f;
            float left = (PelagAbordageVfxRules.QuakeCrest(end, start, travel, radius) - PelagAbordageVfxRules.QuakeCrest(end - h, start, travel, radius)) / h;
            float right = (PelagAbordageVfxRules.QuakeCrest(end + h, start, travel, radius) - PelagAbordageVfxRules.QuakeCrest(end, start, travel, radius)) / h;
            Assert.That(right, Is.EqualTo(left).Within(left * .1f + .01f), "без излома скорости — не застывает");
        }

        [Test]
        public void WaterBreaks_OnlyAfterTheFrontIsDone()
        {
            const int start = 40, travel = 5;
            Assert.That(PelagAbordageVfxRules.FrontBreakAge(start + 2f, start, travel, .02f), Is.EqualTo(0f));
            Assert.That(PelagAbordageVfxRules.FrontBreakAge(start + travel + 9f, start, travel, .02f), Is.GreaterThan(.30f));
        }

        [Test]
        public void BreachJet_EdgeIsTheSimCone()
        {
            Assert.That(PelagAbordageVfxRules.BreachHalfAngleDegrees, Is.EqualTo(25f).Within(.05f));
            float length = Simulation.AbordageBreachLength.ToFloat();
            float expected = PelagAbordageVfxRules.BreachApexHalf + length * (float)System.Math.Tan(25.0 * System.Math.PI / 180.0);
            Assert.That(PelagAbordageVfxRules.BreachHalfWidth(length), Is.EqualTo(expected).Within(.01f));
            Assert.That(PelagAbordageVfxRules.BreachFront(40 + 4 - PelagAbordageVfxRules.FrontLeadTicks, 40, 4, length),
                Is.EqualTo(length).Within(.001f));
        }

        [Test]
        public void GeyserLift_StartsAndLandsOnTheGround_OnTheSimTicks()
        {
            Assert.That(PelagAbordageVfxRules.GeyserLift01(0f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserLift01(1f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserLift01(PelagAbordageVfxRules.GeyserRiseShare), Is.EqualTo(1f).Within(.01f));
            float peak = 0f;
            for (float t = 0f; t <= 1f; t += .01f) peak = System.Math.Max(peak, PelagAbordageVfxRules.GeyserLift01(t));
            Assert.That(peak, Is.LessThanOrEqualTo(1.05f + Eps));
            foreach (float joint in new[] { PelagAbordageVfxRules.GeyserRiseShare, PelagAbordageVfxRules.GeyserFallShare })
                Assert.That(System.Math.Abs(PelagAbordageVfxRules.GeyserLift01(joint - .001f) - PelagAbordageVfxRules.GeyserLift01(joint + .001f)),
                    Is.LessThan(.01f), "без скачка на стыке");
            int lift = 50, fall = lift + Simulation.AbordageGeyserLiftTicks;
            Assert.That(PelagAbordageVfxRules.GeyserT(fall, lift, fall), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserCollapse(fall + 10f, fall), Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void GeyserColumn_HoldsTheLiftedTarget_HeavyOnesStayInside()
        {
            float lifted = PelagAbordageVfxRules.GeyserColumn(.5f, true);
            Assert.That(lifted, Is.GreaterThan(PelagAbordageVfxRules.GeyserApex * PelagAbordageVfxRules.GeyserLift01(.5f)),
                "шапка столба под ногами подброшенной цели");
            Assert.That(PelagAbordageVfxRules.GeyserColumn(0f, true), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserColumn(.5f, false), Is.LessThanOrEqualTo(PelagAbordageVfxRules.GeyserHeavyColumn + Eps));
            Assert.That(PelagAbordageVfxRules.GeyserColumn(.5f, false), Is.GreaterThan(1.5f));
        }

        [Test]
        public void GeyserColumn_IsAboutTwoHeroesTall_FromTheGameCamera_LikeFrameF()
        {
            // Круг 2 (03.10): 2,45 м при ширине ~1,7 м с камеры 48° (вертикаль ×0,67) читались «шатром».
            // Кадр F: узкий столб ~в два роста героя, цель сидит в кроне пены.
            float apex = 0f;
            for (float t = 0f; t <= 1f; t += .01f) apex = System.Math.Max(apex, PelagAbordageVfxRules.GeyserColumn(t, true));
            Assert.That(apex, Is.InRange(3.1f, 3.7f));
            Assert.That(PelagAbordageVfxRules.GeyserColumn(.3f, false), Is.InRange(2.8f, 3.4f));
            double squash = System.Math.Cos(48.0 * System.Math.PI / 180.0);
            const float hero = 1.82f;
            Assert.That(apex * squash, Is.GreaterThan(1.8f * hero * squash), "на экране — около двух ростов героя");
            float guardian = EnemyArchetypes.GuardianBodyRadius.ToFloat();
            float trunk = 2f * PelagAbordageVfxRules.GeyserHalfWidth(guardian);
            Assert.That(apex * squash / trunk, Is.GreaterThan(3f), "высокий столб, не шатёр");
            // Ствол не шире тела цели (кадр F): объём — раструбом у земли и кроной вокруг цели.
            foreach (float r in new[] { .45f, .85f, .95f })
                Assert.That(PelagAbordageVfxRules.GeyserHalfWidth(r), Is.InRange(.22f, System.Math.Min(.45f, r)));
            // Цель сидит в кроне: шапка выше её ног.
            Assert.That(PelagAbordageVfxRules.GeyserColumn(.5f, true) - PelagAbordageVfxRules.GeyserApex * PelagAbordageVfxRules.GeyserLift01(.5f),
                Is.InRange(.5f, .7f));
        }

        [Test]
        public void GeyserProfile_FlaresAtTheGround_EvenTrunk_RoundCrownOnTop()
        {
            // Круг 3: раструб скромнее (дальше — розетка), крона — округлая шапка ×1,6, не веер ×2,6.
            Assert.That(PelagAbordageVfxRules.GeyserProfile(0f), Is.InRange(1.3f, 1.5f), "раструб удара из земли");
            for (float k = .2f; k <= .70f; k += .05f)
                Assert.That(PelagAbordageVfxRules.GeyserProfile(k), Is.InRange(.92f, 1.02f), $"ствол на {k:0.00}");
            Assert.That(PelagAbordageVfxRules.GeyserProfile(1f), Is.InRange(1.5f, 1.8f), "крона");
            for (float k = 0f; k < 1f; k += .01f)
                Assert.That(System.Math.Abs(PelagAbordageVfxRules.GeyserProfile(k + .01f) - PelagAbordageVfxRules.GeyserProfile(k)),
                    Is.LessThan(.12f), "без ступенек");
        }

        [Test]
        public void GeyserSkirt_SplashesAroundTheBase_InsideTheFallRing()
        {
            float fall = Simulation.AbordageGeyserFallRadius.ToFloat();
            foreach (float r in new[] { .45f, .85f, 1.1f })
            {
                float skirt = PelagAbordageVfxRules.GeyserSkirt(1f, r);
                Assert.That(skirt, Is.LessThan(fall - .5f), "юбка не путается с кольцом падения (край урона)");
                Assert.That(skirt, Is.GreaterThan(2f * PelagAbordageVfxRules.GeyserHalfWidth(r) * PelagAbordageVfxRules.GeyserProfile(0f) * .55f));
                Assert.That(PelagAbordageVfxRules.GeyserSkirt(0f, r), Is.LessThan(skirt * .5f), "выбрасывается от ствола наружу");
            }
        }

        [Test]
        public void QuakeLobes_AreRoundAndUneven_ButNeverPastTheSimEdge()
        {
            // Круг 4 (ревью в игре: ровное «солнце» с одинаковыми шипами): лопасти — круглые горбы разной длины.
            Assert.That(PelagAbordageVfxRules.FingerSoft(0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.FingerSoft(1f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.Finger(-1.5f), Is.EqualTo(0f).Within(Eps));
            // Лопасть круглая (горб), луч земли — острый клин.
            Assert.That(PelagAbordageVfxRules.Finger(.3f), Is.LessThan(PelagAbordageVfxRules.FingerSoft(.3f) - .2f));
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            Assert.That(PelagAbordageVfxRules.QuakeLobes, Is.InRange(8, 12), "крупные лопасти, не 22 одинаковых шипа");
            Assert.That(PelagAbordageVfxRules.QuakeLobeShortest, Is.InRange(.6f, .8f), "лопасти разной длины");
            for (float lobe = 0f; lobe <= 1f; lobe += .05f)
                Assert.That(PelagAbordageVfxRules.QuakeLobeEdge(radius, lobe), Is.LessThanOrEqualTo(radius + Eps), "видимый край = край урона Sim");
            Assert.That(PelagAbordageVfxRules.QuakeLobeEdge(radius, 1f), Is.EqualTo(radius).Within(Eps), "кончик длинной лопасти на гребне");
            float valley = PelagAbordageVfxRules.QuakeLobeEdge(radius, 0f);
            Assert.That(valley, Is.InRange(radius * .4f, radius * .6f), "просвет между лопастями — глубокий, всплеск неровный");
            // Пена — на кончиках: у кончика длинной лопасти втрое гуще, чем в просвете.
            Assert.That(PelagAbordageVfxRules.QuakeTipFoam(1f), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.QuakeTipFoam(0f), Is.LessThan(.4f));
        }

        [Test]
        public void QuakeEarth_IsOpaqueWetEarthAtTheHero_WithRaysBetweenLobes()
        {
            // Круг 4 (кадр D): под героем — мокрая земля (ядро и круглые лепестки), в просветах между лопастями —
            // тонкие лучи земли почти до края; вода начинается от земли, а не полупрозрачной полосой поверх пола.
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            float core = PelagAbordageVfxRules.QuakeEarthCore(radius);
            Assert.That(core, Is.InRange(.5f, .7f), "ядро у ног героя — шире тела героя на экране");
            Assert.That(PelagAbordageVfxRules.QuakeEarthCore(.5f), Is.LessThan(.15f), "у кулака земля почти точкой");
            Assert.That(PelagAbordageVfxRules.QuakeEarthEdge(radius, 0f, 0f), Is.EqualTo(core).Within(Eps));
            float petal = PelagAbordageVfxRules.QuakeEarthEdge(radius, PelagAbordageVfxRules.QuakePetalMax, 0f);
            Assert.That(petal, Is.InRange(radius * .3f, radius * .45f), "лепестки земли — треть радиуса, как в D");
            float ray = PelagAbordageVfxRules.QuakeEarthEdge(radius, 0f, PelagAbordageVfxRules.QuakeRayMax);
            Assert.That(ray, Is.GreaterThan(petal), "луч земли выходит за лепестки — между лопастями");
            Assert.That(ray, Is.LessThan(PelagAbordageVfxRules.QuakeLobeEdge(radius, 0f) * 1.1f), "но не звездой до края — не дальше просвета");
            // На оси длинной лопасти воды больше двух третей радиуса.
            Assert.That(radius - petal, Is.GreaterThan(radius * .55f));
            Assert.That(PelagAbordageVfxRules.QuakePetals, Is.InRange(10, 20), "лепестки полные, не тонкие шипы");
        }

        [Test]
        public void QuakeDecay_HolesFromTheCentre_FoamRimBreaksIntoDropsLast_TimingKept()
        {
            // Круг 3 белил весь диск в кремовую звезду на +0,37…+0,50 с. Круг 4: дыры от центра наружу, кайма пены
            // рвётся на капли последней; целиком всплеск стоит до ~+0,3 с, к +0,62 с его нет (как в круге 3).
            int travel = Simulation.AbordageQuakeTravelTicks;
            float full = PelagAbordageVfxRules.QuakeAgeAt(.28f, travel);
            Assert.That(PelagAbordageVfxRules.QuakeHoleFront(full) + PelagAbordageVfxRules.QuakeHoleNoise + PelagAbordageVfxRules.QuakeHoleCut,
                Is.LessThanOrEqualTo(0f), "до +0,28 с всплеск целый: фронт с шумом и срезом ещё за центром");
            float mid = PelagAbordageVfxRules.QuakeAgeAt(.40f, travel);
            float front = PelagAbordageVfxRules.QuakeHoleFront(mid);
            Assert.That(front, Is.InRange(.05f, .9f), "на +0,40 с дыры у центра, кайма и кончики ещё стоят");
            // Фронт дыр идёт от центра наружу и проходит край лопасти раньше, чем кайма пены рвётся до конца.
            Assert.That(PelagAbordageVfxRules.QuakeHoleFront(PelagAbordageVfxRules.QuakeRimDropsGone), Is.GreaterThan(1f));
            for (float a = 0f; a < .5f; a += .01f)
                Assert.That(PelagAbordageVfxRules.QuakeHoleFront(a + .01f), Is.GreaterThanOrEqualTo(PelagAbordageVfxRules.QuakeHoleFront(a) - Eps));
            Assert.That(PelagAbordageVfxRules.QuakeRimDropsFrom, Is.GreaterThan(PelagAbordageVfxRules.QuakeHoleStart), "кайма рвётся после начала дыр");
            Assert.That(PelagAbordageVfxRules.QuakeRimDropsGone, Is.LessThanOrEqualTo(PelagAbordageVfxRules.QuakeFadeFrom));
            float gone = PelagAbordageVfxRules.QuakeAgeAt(.62f, travel);
            Assert.That(gone, Is.GreaterThanOrEqualTo(PelagAbordageVfxRules.QuakeFadeTo), "к +0,62 с всплеска нет");
            Assert.That(PelagAbordageVfxRules.QuakeAgeAt(.15f, travel), Is.LessThan(.05f), "в конце хода фронта распада нет");
        }

        [Test]
        public void QuakeNoise_StretchesIntoRaysFromTheCentre()
        {
            // Кадр D: струи и пятна воды — лучами от центра: по радиусу шум в разы реже, чем по кругу.
            for (float r = 1f; r <= 3.01f; r += .25f)
                Assert.That(PelagAbordageVfxRules.StreakStretch(r), Is.InRange(5f, 25f), $"на {r:0.00} м");
            Assert.That(PelagAbordageVfxRules.StreakNoiseRadius(3f) - PelagAbordageVfxRules.StreakNoiseRadius(0f),
                Is.EqualTo(3f * PelagAbordageVfxRules.StreakNoiseRate).Within(Eps));
        }

        [Test]
        public void QuakeClods_ReadAsSolidStonesAtGameZoom()
        {
            // Круг 3: видимый камень ≥ 0,16 м (≥ 14 px при ~91 px/м в 1080p) и не крупнее 0,3 м.
            Assert.That(PelagAbordageVfxRules.QuakeClodSizeMin * PelagAbordageVfxRules.QuakeClodFill, Is.GreaterThanOrEqualTo(.16f));
            Assert.That(PelagAbordageVfxRules.QuakeClodSizeMax * PelagAbordageVfxRules.QuakeClodFill, Is.LessThanOrEqualTo(.3f));
        }

        [Test]
        public void GeyserTrunk_IsAboutNineTenthsOfTheHero_AndGreenAcross()
        {
            // Круг 3 (ревью 03.10): ствол ≈0,35 ширины героя и бледный; в кадре F ≈0,9 и зелёный.
            foreach (float r in new[] { .42f, .45f, .65f, .85f, .95f })
            {
                float share = PelagAbordageVfxRules.GeyserTrunkWidth(r) / PelagAbordageVfxRules.HeroScreenWidth;
                Assert.That(share, Is.InRange(.78f, .92f), $"ствол при r {r}");
                Assert.That(PelagAbordageVfxRules.GeyserTrunkWater(r), Is.GreaterThan(PelagAbordageVfxRules.GeyserTrunkWidth(r) * .78f),
                    "зелёная вода во всю ширину, белое — тонкой каймой");
            }
            Assert.That(PelagAbordageVfxRules.GeyserTrunkChurn, Is.LessThan(.2f), "бурление не белит треть ствола");
        }

        [Test]
        public void GeyserCrown_HugsTheLiftedBody()
        {
            // Цель сидит В пене (кадр F): клубы от чуть ниже ног до пояса, вплотную к телу — в ширину шапки.
            Assert.That(PelagAbordageVfxRules.GeyserCrownHeight(0f), Is.LessThan(0f));
            Assert.That(PelagAbordageVfxRules.GeyserCrownHeight(1f), Is.InRange(.4f, .7f));
            foreach (float r in new[] { .42f, .45f, .85f, .95f })
            {
                float visible = System.Math.Max(.25f, PelagAbordageVfxRules.VisibleBodyRadius(r));
                Assert.That(PelagAbordageVfxRules.GeyserCrownReach(r, 0f), Is.LessThan(visible), "часть клубов перед телом");
                Assert.That(PelagAbordageVfxRules.GeyserCrownReach(r, 1f), Is.InRange(visible,
                    PelagAbordageVfxRules.GeyserHalfWidth(r) * PelagAbordageVfxRules.GeyserProfile(1f)), "не дальше шапки");
            }
        }

        [Test]
        public void GeyserFallRing_IsThin_AndOpaqueFromTheSecondFrame()
        {
            // Круг 3: кольцо 2 м забивало кадр, первый кадр серо-хаки (толстая пена на малой непрозрачности).
            float fall = Simulation.AbordageGeyserFallRadius.ToFloat();
            Assert.That(2f * PelagAbordageVfxRules.GeyserRingHalf, Is.LessThan(fall * .08f), "тонкая полоса");
            Assert.That(PelagAbordageVfxRules.GeyserRingAlpha(0f), Is.EqualTo(0f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserRingAlpha(2f / Simulation.TicksPerSecond), Is.EqualTo(1f).Within(Eps), "со 2-го кадра");
            Assert.That(PelagAbordageVfxRules.GeyserRingGrow(0f), Is.InRange(.3f, .4f));
            Assert.That(PelagAbordageVfxRules.GeyserRingGrow(.15f), Is.EqualTo(1f).Within(Eps));
            Assert.That(PelagAbordageVfxRules.GeyserRosettePetals, Is.InRange(9, 13), "круглая розетка, не звезда");
            Assert.That(PelagAbordageVfxRules.GeyserRosetteNotch, Is.LessThan(PelagAbordageVfxRules.GeyserSkirt(1f, .45f) * .35f));
        }

        [Test]
        public void QuakeClods_LandInsideTheCircle_OnALowFastArc()
        {
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            double g = PelagAbordageVfxRules.QuakeClodGravity * 9.81;
            // Круг 4: ком вылетает снаружи тела героя (видимое тело ≈0,35 м + полкома), не кучей на ногах.
            float hero = .35f;
            Assert.That(PelagAbordageVfxRules.QuakeClodStartMin - .5f * PelagAbordageVfxRules.QuakeClodSizeMax * PelagAbordageVfxRules.QuakeClodFill,
                Is.GreaterThanOrEqualTo(hero), "ком стартует снаружи тела героя");
            foreach (float flight in new[] { .30f, .46f })
            {
                PelagAbordageVfxRules.ClodLaunch(radius * PelagAbordageVfxRules.QuakeClodReachMax, flight, out float horizontal, out float vertical);
                Assert.That(horizontal * flight + PelagAbordageVfxRules.QuakeClodStartMax, Is.LessThan(radius), "комья падают внутри круга");
                Assert.That(vertical * flight - .5 * g * flight * flight, Is.EqualTo(0.0).Within(1e-3), "на земле в конце полёта");
                Assert.That(vertical * vertical / (2 * g), Is.LessThan(.65), "низкая дуга");
            }
        }

        [Test]
        public void BreachJet_FarEndIsTheSimArc_AndBreaksUp()
        {
            float length = Simulation.AbordageBreachLength.ToFloat();
            float half = PelagAbordageVfxRules.BreachHalfWidth(length);
            float back = PelagAbordageVfxRules.BreachArcBack(half, length);
            Assert.That(System.Math.Sqrt((length - back) * (length - back) + half * half), Is.EqualTo(length).Within(.01),
                "угол дальнего конца на дуге сектора Sim, не прямой срез");
            Assert.That(PelagAbordageVfxRules.BreachArcBack(0f, length), Is.EqualTo(0f).Within(Eps));
            foreach (float along in new[] { 0f, .1f, .5f, 1f })
                Assert.That(PelagAbordageVfxRules.BreachArcBack(2f, along), Is.LessThanOrEqualTo(.4f * along + Eps), "у вершины ряды не схлопываются");
            Assert.That(PelagAbordageVfxRules.BreachTipAge(0f), Is.GreaterThan(.30f), "нос рвётся на капли");
            Assert.That(PelagAbordageVfxRules.BreachTipAge(PelagAbordageVfxRules.BreachTipLength), Is.EqualTo(0f).Within(Eps), "дальше — живая вода");
        }

        [Test]
        public void Bite_SitsOnTheVisibleBody_NotOnTheSimEdge()
        {
            // Хранитель (r 0,85): укус на r висел в ~0,5 м перед грудью модели.
            float guardian = EnemyArchetypes.GuardianBodyRadius.ToFloat();
            Assert.That(PelagAbordageVfxRules.VisibleBodyRadius(guardian), Is.InRange(.30f, .40f));
            Assert.That(PelagAbordageVfxRules.BodyInset(guardian), Is.InRange(.45f, .55f));
            foreach (float r in new[] { 0f, .42f, .45f, .7f, .95f })
            {
                Assert.That(PelagAbordageVfxRules.VisibleBodyRadius(r), Is.LessThanOrEqualTo(r + Eps));
                Assert.That(PelagAbordageVfxRules.VisibleBodyRadius(r) + PelagAbordageVfxRules.BodyInset(r), Is.EqualTo(r).Within(Eps));
            }
        }

        [Test]
        public void GeyserFoam_PuffsMergeIntoMasses_NotSeparateEggs()
        {
            // Круг 4 (ревью в игре: россыпь кремовых «яиц», у каждого свой чёрный обвод): клубы крупнее, кроны
            // вплотную к телу — соседи перекрываются, обвод (силуэт, раздутый на долю маски) — по краю массы.
            Assert.That(PelagAbordageVfxRules.GeyserFoamOutline, Is.InRange(.05f, .14f));
            Assert.That(PelagAbordageVfxRules.GeyserPuffMin, Is.GreaterThanOrEqualTo(.28f));
            Assert.That(PelagAbordageVfxRules.GeyserPuffMax, Is.LessThanOrEqualTo(.5f));
            Assert.That(PelagAbordageVfxRules.GeyserRimPuffMin, Is.LessThanOrEqualTo(PelagAbordageVfxRules.GeyserRimPuffMax));
            float life = .48f, rate = PelagAbordageVfxRules.GeyserPuffRate;
            foreach (float r in new[] { .42f, .85f })
            {
                // Живых клубов кроны на поясе вокруг тела (окружность × высота пояса), м² на клуб.
                float alive = rate * PelagAbordageVfxRules.GeyserCrownPuffShare * life;
                float reach = PelagAbordageVfxRules.GeyserCrownReach(r, 1f);
                float band = 2f * (float)System.Math.PI * reach * (PelagAbordageVfxRules.GeyserCrownHeight(1f) - PelagAbordageVfxRules.GeyserCrownHeight(0f));
                float puff = PelagAbordageVfxRules.GeyserPuffMin * PelagAbordageVfxRules.GeyserPuffMin;
                Assert.That(alive * puff, Is.GreaterThan(band * 1.2f), $"клубы кроны перекрываются (r {r})");
            }
            // Кромка розетки: клубы по окружности кромки идут внахлёст.
            float rim = 2f * (float)System.Math.PI * PelagAbordageVfxRules.GeyserSkirt(1f, .85f) * .9f;
            float rimAlive = PelagAbordageVfxRules.GeyserPuffRate * (1f - PelagAbordageVfxRules.GeyserCrownPuffShare) * .46f;
            Assert.That(rimAlive * PelagAbordageVfxRules.GeyserRimPuffMin, Is.GreaterThan(rim), "кромка розетки — сплошная пена");
        }

        [Test]
        public void GeyserRosette_IsASolidDisc_NoFloorHoleAtTheTrunk()
        {
            // Круг 4 (ревью: тёмное пятно пола у основания ствола): внутренний край розетки — за центром.
            Assert.That(PelagAbordageVfxRules.GeyserRosetteFill, Is.InRange(.2f, .6f));
            float petal = PelagAbordageVfxRules.GeyserSkirt(1f, .85f);
            float inner = -PelagAbordageVfxRules.GeyserRosetteFill * petal;
            float half = .5f * (petal - inner), mid = petal - half;
            Assert.That(-mid / half, Is.GreaterThan(-.9f), "центр диска — внутри воды, не на кромке с обводом");
        }

        [Test]
        public void GeyserColumn_StandsUnderTheLiftedPelvis_NotUnderTheBodyPosition()
        {
            // Круг 5 (каст 0, лёгкая цель с 5 м: поднятая модель висела на ~0,4 м ближе к герою, чем столб): сдвиг таза
            // от корня вида до ~0,4–0,6 м берётся, сломанный риг (кость в нуле мира, NaN) — нет.
            Assert.That(PelagAbordageVfxRules.GeyserPelvisReach, Is.InRange(.6f, 1.5f));
            Assert.That(PelagAbordageVfxRules.GeyserPelvisUsable(-.4f, 0f), Is.True);
            Assert.That(PelagAbordageVfxRules.GeyserPelvisUsable(.3f, -.35f), Is.True);
            Assert.That(PelagAbordageVfxRules.GeyserPelvisUsable(0f, 0f), Is.True);
            Assert.That(PelagAbordageVfxRules.GeyserPelvisUsable(4f, 2f), Is.False);
            Assert.That(PelagAbordageVfxRules.GeyserPelvisUsable(float.NaN, 0f), Is.False);
            // Подброшенная сидит в кроне: смещённый на 0,4 м столб с полушириной ствола всё ещё под тазом,
            // а прежний (в позиции тела) — нет: ствол уже 0,4 м в половину.
            Assert.That(PelagAbordageVfxRules.GeyserHalfWidth(.42f), Is.LessThan(.4f));
        }

        [Test]
        public void GeyserFall_IsMergedFoam_NotSeparateEggs()
        {
            // Круг 5 (проверка круга 4: всплеск падения на +0,87…1,00 с — россыпь отдельных кремовых «яиц»): горка клубов
            // там, где осел столб, и вал клубов внахлёст — пара силуэт/заливка, обвод только по краю массы.
            float puffMin = PelagAbordageVfxRules.GeyserPuffMin;
            // Горка: клубы (×1,1) перекрывают свой круг — площадь клубов больше площади горки.
            float mound = PelagAbordageVfxRules.GeyserFallMoundReach + .55f * puffMin;
            float moundPuffs = PelagAbordageVfxRules.GeyserFallMoundPuffs * (float)System.Math.PI * .3025f * puffMin * puffMin;
            Assert.That(moundPuffs, Is.GreaterThan(1.1f * (float)System.Math.PI * mound * mound), "горка — сплошная пена");
            // Вал: стартует за горкой и встаёт внутри кольца падения Sim; на самом дальнем радиусе клубы ещё внахлёст.
            Assert.That(PelagAbordageVfxRules.GeyserFallWaveStart, Is.GreaterThan(PelagAbordageVfxRules.GeyserFallMoundReach));
            float far = PelagAbordageVfxRules.GeyserFallWaveReach(PelagAbordageVfxRules.GeyserFallWaveSpeedMax) * 1.1f;
            Assert.That(far, Is.LessThan(Simulation.AbordageGeyserFallRadius.ToFloat()), "вал внутри кольца падения");
            float across = PelagAbordageVfxRules.GeyserFallWavePuffs * puffMin;
            Assert.That(across, Is.GreaterThan(1.3f * 2f * (float)System.Math.PI * far), "вал — сплошная кромка, не россыпь");
            Assert.That(PelagAbordageVfxRules.GeyserFallWaveReach(PelagAbordageVfxRules.GeyserFallWaveSpeedMin),
                Is.LessThan(PelagAbordageVfxRules.GeyserFallWaveReach(PelagAbordageVfxRules.GeyserFallWaveSpeedMax)));
            // Задетым — пена у ног внахлёст по кругу тела (Хранитель 0,85 м).
            float ring = 2f * (float)System.Math.PI * System.Math.Max(.25f, PelagAbordageVfxRules.VisibleBodyRadius(.85f)) * .9f * 1.1f;
            Assert.That(PelagAbordageVfxRules.GeyserFallKnockPuffs * PelagAbordageVfxRules.GeyserRimPuffMin, Is.GreaterThan(1.2f * ring),
                "пена у ног задетого — кольцом внахлёст");
            // Слой клубов вмещает живую крону и всю пену падения с пятью задетыми: пара силуэт/заливка не рвётся.
            float crownAlive = PelagAbordageVfxRules.GeyserPuffRate * .55f;
            int fall = PelagAbordageVfxRules.GeyserFallMoundPuffs + PelagAbordageVfxRules.GeyserFallWavePuffs
                       + 5 * PelagAbordageVfxRules.GeyserFallKnockPuffs;
            Assert.That(crownAlive + fall, Is.LessThanOrEqualTo(PelagAbordageVfxRules.GeyserPuffLayerMax));
        }

        [Test]
        public void QuakeEarth_IsMottledWetMud_NotPlanksWithStripes()
        {
            // Круг 5 (проверка круга 4: лучи земли — длинные гладкие «доски» со светлыми полосами вдоль): пятна грязи
            // короткие и круглые — в разы короче луча земли; капли мелкие; три тона по порядку.
            float radius = Simulation.AbordageQuakeRadius.ToFloat();
            float patch = PelagAbordageVfxRules.QuakeMudPatchSize(PelagAbordageVfxRules.QuakeMudScale);
            Assert.That(patch, Is.InRange(.12f, .32f), "пятно грязи — короткое, как мазки кадра D");
            Assert.That(PelagAbordageVfxRules.QuakeEarthEdge(radius, 0f, PelagAbordageVfxRules.QuakeRayMax) / patch, Is.GreaterThan(4f),
                "луч земли — несколько пятен, а не одна доска");
            Assert.That(PelagAbordageVfxRules.QuakeMudPatchSize(PelagAbordageVfxRules.QuakeMudFineScale), Is.LessThan(patch));
            float drop = PelagAbordageVfxRules.QuakeMudDropSize(PelagAbordageVfxRules.QuakeMudDropScale);
            Assert.That(drop, Is.InRange(.03f, .09f), "белые капли — мелкие");
            Assert.That(PelagAbordageVfxRules.QuakeMudDark, Is.LessThan(PelagAbordageVfxRules.QuakeMudLight));
            Assert.That(PelagAbordageVfxRules.QuakeMudDropCut, Is.InRange(.55f, .75f), "капель немного — порог в верхушках пузырей");

            // Шейдер: цвет земли — шум по месту (w), не полярный (n, ray, ray2 — полосы вдоль луча).
            string shader = QuakeShader();
            Assert.That(shader, Does.Contain("float mud = Soft(w"));
            foreach (string line in shader.Split('\n'))
                if (line.Contains("earthCol") || line.Contains("float mud") || line.Contains("float droplet"))
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(ray|ray2|n)\b"), Is.False, line.Trim());
        }

        [Test]
        public void QuakeDecay_HoleEdgeIsAHardCut_NoSemiTransparentCobalt()
        {
            // Круг 5 (проверка круга 4: ~3 кадра сиреневые и рыжие точки по кромке дыр — полупрозрачный кобальт и земля
            // поверх охры): кромка дыр и страховка — срез Aa (1 px), без мягкой ступени; срок дыр прежний (срез — на
            // середине прежней мягкой кромки 0,22).
            Assert.That(PelagAbordageVfxRules.QuakeHoleCut, Is.EqualTo(.11f).Within(Eps));
            string shader = QuakeShader();
            Assert.That(shader, Does.Contain("float holeKeep = Aa(hv - _Hole.z);"));
            Assert.That(shader, Does.Not.Contain("smoothstep(0, max(_Hole.z"));
            Assert.That(shader, Does.Not.Contain("1 - smoothstep(_FadeFrom"), "страховка — дырами, не прозрачностью");
            Assert.That(shader, Does.Contain("float fade = Aa("));
        }

        private static string QuakeShader()
        {
            string path = System.IO.Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Shaders", "RazlomAbordageQuakeSplash.shader");
            // Без комментариев: проверяется код, не пояснения.
            return System.Text.RegularExpressions.Regex.Replace(System.IO.File.ReadAllText(path), @"//[^\n]*", "");
        }
    }
}
