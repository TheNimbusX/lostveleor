using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;
using Rules = Game.View.ThicketMasterIntroRules;

/// <summary>
/// Хозяин Чащи — кат-сцена вступления (razlom/Assets/Game.View/ThicketMasterIntroRules.cs, вид —
/// ThicketMasterIntroView, окно — Simulation.ForestBoss.Intro, контракт artifacts/tools/wf/boss-tempo-contract.md,
/// «Вступление»). Главное: всё время — из окна Sim. Полосы въезжают и HUD гаснет с тика S, камера к
/// пробуждению W у босса, медленный наезд до контакта рёва, с начала рёва — титр (догорает к E), на контакте
/// — тряска, после рёва
/// камера домой, в тик E (герой свободен, босс бьёт) полосы уезжают, HUD и полоса босса возвращаются.
/// Кривые непрерывны, пауза не трясёт, смерть босса в окне отпускает всё от тика смерти, Часы двигают
/// рёв и титр. Живая сцена: настоящий уровень босса — события Sim ложатся ровно на тики правил.
/// </summary>
public sealed class ThicketMasterIntroRulesTests
{
    private const int S = 300;
    private const int W = S + Simulation.ThicketIntroLeadTicks;
    private const int E = S + Simulation.ThicketIntroTicks;
    private const float Eps = 1e-4f;

    private static Rules.Look At(float now) => Rules.LookAt(S, W, E, E, now);

    // ------------------------------------------------------------ окно

    [Test]
    public void Timeline_MatchesTheSimWindow()
    {
        Assert.That(Rules.GlideTicks, Is.EqualTo(Simulation.ThicketIntroLeadTicks), "к пробуждению камера у босса");
        Assert.That(Rules.RoarContact(E), Is.EqualTo(W + Simulation.ThicketWakeTicks + Simulation.ThicketRoarWindupTicks),
            "контакт рёва — кольцо Sim");
        Assert.That(Rules.ArriveZoom * Rules.CombatSize, Is.EqualTo(4.8f).Within(Eps));
        Assert.That(Rules.RoarZoom * Rules.CombatSize, Is.EqualTo(4.6f).Within(Eps), "договорённость: 6,2 → ~4,6");
        Assert.That(Rules.TitleStart(W), Is.EqualTo(W + Simulation.ThicketWakeTicks), "титр — от Started(Roar) (контракт)");
        Assert.That(Rules.TitleEnd(E, E), Is.EqualTo(E), "титр догорает к первой атаке босса, не поверх неё");
        Assert.That(Rules.TitleFadeStart(E, E) - (Rules.TitleStart(W) + Rules.TitleInTicks), Is.GreaterThanOrEqualTo(24),
            "титр стоит в полную силу не меньше 0,8 с");
        Assert.That(Rules.TitleFadeStart(E, E), Is.GreaterThanOrEqualTo(Rules.RoarContact(E)), "через кольцо рёва титр стоит");

        int back = Rules.ReturnStart(E, E);
        Assert.That(back, Is.GreaterThan(Rules.RoarContact(E)), "рёв — у босса");
        Assert.That(back, Is.LessThan(E), "возврат начинается до того, как босс бьёт");
        Assert.That(back + Rules.ReturnTicks, Is.LessThanOrEqualTo(E + Rules.BarsOutTicks), "камера дома, пока уходят полосы");
        Assert.That(Rules.DoneTick(E, E), Is.EqualTo(E + Math.Max(Rules.BarsOutTicks, Rules.HudInTicks)),
            "последними уходят полосы и HUD");
        for (int release = S; release <= E; release++)
            Assert.That(Rules.TitleEnd(E, release), Is.LessThanOrEqualTo(Rules.DoneTick(E, release)),
                "титр догас к концу кат-сцены, отпуск " + release);
    }

    [Test]
    public void Start_BarsSlideIn_HudFades_CameraGlidesToTheBoss()
    {
        var first = At(S);
        Assert.That(first.Done, Is.False);
        Assert.That(first.Camera, Is.EqualTo(0f).Within(Eps));
        Assert.That(first.Zoom, Is.EqualTo(1f).Within(Eps));
        Assert.That(first.Bars, Is.EqualTo(0f).Within(Eps));
        Assert.That(first.Hud, Is.EqualTo(1f).Within(Eps));
        Assert.That(first.Title, Is.EqualTo(0f));

        var half = At(S + Rules.GlideTicks / 2f);
        Assert.That(half.Camera, Is.InRange(.2f, .8f), "летит, а не прыгает");
        Assert.That(half.Zoom, Is.InRange(Rules.ArriveZoom, 1f));

        Assert.That(At(S + Rules.BarsInTicks).Bars, Is.EqualTo(1f).Within(Eps));
        Assert.That(At(S + Rules.HudOutTicks).Hud, Is.EqualTo(0f).Within(Eps));
        var wake = At(W);
        Assert.That(wake.Camera, Is.EqualTo(1f).Within(Eps), "пробуждение — камера у босса");
        Assert.That(wake.Zoom, Is.EqualTo(Rules.ArriveZoom).Within(Eps));
        Assert.That(wake.Title, Is.EqualTo(0f));
    }

    [Test]
    public void WakeAndRoar_SlowPushIn_TitleFromTheRoarStart_GoneByTheFirstAttack()
    {
        int contact = Rules.RoarContact(E);
        int titleAt = Rules.TitleStart(W);
        float zoom = At(W).Zoom;
        for (float t = W; t <= contact; t += .25f)
        {
            var look = At(t);
            Assert.That(look.Zoom, Is.LessThanOrEqualTo(zoom + Eps), "наезд не отъезжает, тик " + t);
            Assert.That(look.Camera, Is.EqualTo(1f).Within(Eps), "у босса, тик " + t);
            Assert.That(look.Bars, Is.EqualTo(1f).Within(Eps));
            Assert.That(look.Hud, Is.EqualTo(0f).Within(Eps));
            if (t <= titleAt) Assert.That(look.Title, Is.EqualTo(0f), "титр — с начала рёва, тик " + t);
            zoom = look.Zoom;
        }
        Assert.That(At(contact).Zoom, Is.EqualTo(Rules.RoarZoom).Within(Eps));
        Assert.That(At(titleAt + Rules.TitleInTicks).Title, Is.EqualTo(1f).Within(Eps), "проявился, пока босс набирает рёв");
        Assert.That(At(titleAt + Rules.TitleSettleTicks).TitleSettle, Is.EqualTo(1f).Within(Eps), "буквы сошлись");
        Assert.That(At(contact).Title, Is.EqualTo(1f).Within(Eps), "кольцо рёва — титр в полную силу");
        Assert.That(At(E - .01f).Title, Is.LessThan(.01f), "к первой атаке титр догорел");
        Assert.That(At(E).Title, Is.EqualTo(0f));
        Assert.That(At(E + 5).Title, Is.EqualTo(0f), "первая атака — по чистому экрану");
        Assert.That(At(contact + Rules.ReturnDelayTicks).Camera, Is.EqualTo(1f).Within(Eps), "на рёве кадр ещё у босса");
    }

    [Test]
    public void End_HeroFree_BarsLeave_HudReturns_CameraHome_ThenDone()
    {
        var end = At(E);
        Assert.That(end.Bars, Is.EqualTo(1f).Within(Eps), "полосы уходят с тика E");
        Assert.That(end.Hud, Is.EqualTo(0f).Within(Eps), "HUD возвращается с тика E");
        Assert.That(end.Camera, Is.InRange(.05f, .95f), "камера уже едет к герою");
        Assert.That(At(E + Rules.BarsOutTicks).Bars, Is.EqualTo(0f).Within(Eps));
        Assert.That(At(E + Rules.HudInTicks).Hud, Is.EqualTo(1f).Within(Eps));
        var home = At(Rules.ReturnStart(E, E) + Rules.ReturnTicks);
        Assert.That(home.Camera, Is.EqualTo(0f).Within(Eps));
        Assert.That(home.Zoom, Is.EqualTo(1f).Within(Eps));

        int done = Rules.DoneTick(E, E);
        Assert.That(At(done - .01f).Done, Is.False);
        Assert.That(At(done).Done, Is.True);
        var finished = At(done + 100);
        Assert.That(finished.Done, Is.True);
        Assert.That(finished.Camera, Is.EqualTo(0f));
        Assert.That(finished.Zoom, Is.EqualTo(1f));
        Assert.That(finished.Hud, Is.EqualTo(1f));
        Assert.That(At(float.NaN).Done, Is.True);
    }

    [Test]
    public void Curves_StayInRange_AndNeverJump()
    {
        var before = At(S - 2f);
        for (float t = S - 2f; t <= Rules.DoneTick(E, E) + 2f; t += .05f)
        {
            var look = At(t);
            foreach (float v in new[] { look.Camera, look.Bars, look.Hud, look.Title, look.TitleSettle })
                Assert.That(v, Is.InRange(0f, 1f), "тик " + t);
            Assert.That(look.Zoom, Is.InRange(Rules.RoarZoom - Eps, 1f + Eps), "тик " + t);
            // Шаг 0,05 тика: самая крутая кривая (титр, 8 тиков) меняется меньше чем на 0,02.
            Assert.That(Math.Abs(look.Camera - before.Camera), Is.LessThan(.02f), "камера, тик " + t);
            Assert.That(Math.Abs(look.Zoom - before.Zoom), Is.LessThan(.01f), "размер, тик " + t);
            Assert.That(Math.Abs(look.Bars - before.Bars), Is.LessThan(.02f), "полосы, тик " + t);
            Assert.That(Math.Abs(look.Hud - before.Hud), Is.LessThan(.02f), "HUD, тик " + t);
            Assert.That(Math.Abs(look.Title - before.Title), Is.LessThan(.02f), "титр, тик " + t);
            before = look;
        }
    }

    // ------------------------------------------------------------ обрыв, Часы, тряска

    [Test]
    public void BossDiesBeforeTheRoar_EverythingLeavesFromTheDeathTick_NoTitle()
    {
        int death = W + 10;
        Assert.That(Rules.RoarShown(E, death), Is.False);
        Assert.That(Rules.TitleShown(W, death), Is.False, "рёв не начался — титра нет");
        Assert.That(Rules.ReturnStart(E, death), Is.EqualTo(death));
        for (float t = S; t < Rules.DoneTick(E, death); t += .5f)
            Assert.That(Rules.LookAt(S, W, E, death, t).Title, Is.EqualTo(0f), "титра нет, тик " + t);
        var after = Rules.LookAt(S, W, E, death, death + Rules.HudInTicks);
        Assert.That(after.Hud, Is.EqualTo(1f).Within(Eps));
        Assert.That(after.Bars, Is.EqualTo(0f).Within(Eps));
        Assert.That(Rules.DoneTick(E, death), Is.EqualTo(death + Rules.ReturnTicks));
        Assert.That(Rules.LookAt(S, W, E, death, Rules.DoneTick(E, death)).Done, Is.True);
        Assert.That(Rules.DoneTick(E, death), Is.LessThan(E), "кат-сцена не тянется до конца окна");
        Assert.That(Rules.RoarShake(Rules.RoarContact(E) - 1f, Rules.RoarContact(E) + 1f, E, death), Is.False);

        // Умер посреди рёва: титр уже стоит — гаснет от тика смерти, без скачка.
        int titleAt = Rules.TitleStart(W);
        int late = titleAt + 20;
        Assert.That(Rules.TitleShown(W, late), Is.True);
        Assert.That(Rules.RoarShown(E, late), Is.False, "кольца не было — тряски нет");
        Assert.That(Rules.LookAt(S, W, E, late, titleAt + Rules.TitleInTicks).Title, Is.EqualTo(1f).Within(Eps));
        Assert.That(Rules.LookAt(S, W, E, late, late).Title, Is.EqualTo(1f).Within(Eps), "в тик смерти ещё стоит");
        Assert.That(Rules.LookAt(S, W, E, late, late + Rules.TitleOutTicks).Title, Is.EqualTo(0f));
        Assert.That(Rules.TitleEnd(E, late), Is.EqualTo(late + Rules.TitleOutTicks));
        // Умер в последние тики рёва: титр уже гаснет к E — смерть его не возвращает.
        int last = E - 5;
        Assert.That(Rules.TitleFadeStart(E, last), Is.EqualTo(E - Rules.TitleOutTicks));
        Assert.That(Rules.LookAt(S, W, E, last, E - 6).Title, Is.EqualTo(Rules.LookAt(S, W, E, E, E - 6).Title).Within(Eps));
    }

    [Test]
    public void Hourglass_ShiftsWakeAndEnd_PushInRoarAndTitleFollow()
    {
        const int shift = 60;
        int wake = W + shift, end = E + shift;
        int titleAt = Rules.TitleStart(wake);
        Assert.That(Rules.LookAt(S, wake, end, end, W + 30).Camera, Is.EqualTo(1f).Within(Eps), "камера ждёт у босса");
        Assert.That(Rules.LookAt(S, wake, end, end, W + 30).Zoom, Is.EqualTo(Rules.ArriveZoom).Within(Eps), "наезд ждёт пробуждения");
        Assert.That(Rules.LookAt(S, wake, end, end, titleAt - .5f).Title, Is.EqualTo(0f));
        Assert.That(Rules.LookAt(S, wake, end, end, titleAt + Rules.TitleInTicks).Title, Is.EqualTo(1f).Within(Eps));
        Assert.That(Rules.LookAt(S, wake, end, end, Rules.RoarContact(end)).Title, Is.EqualTo(1f).Within(Eps));
        Assert.That(Rules.TitleEnd(end, end), Is.EqualTo(end), "титр гаснет к сдвинутому E");
        Assert.That(Rules.LookAt(S, wake, end, end, E + 1).Bars, Is.EqualTo(1f).Within(Eps), "полосы держатся до сдвинутого E");
        Assert.That(Rules.LookAt(S, wake, end, end, end + Rules.BarsOutTicks).Bars, Is.EqualTo(0f).Within(Eps));
    }

    [Test]
    public void RoarShake_OncePerCrossing_PauseAndFirstFrameDoNot()
    {
        int contact = Rules.RoarContact(E);
        int shakes = 0;
        float before = float.NaN;
        for (float t = S; t < E + 30; t += .37f)
        {
            if (Rules.RoarShake(before, t, E, E)) shakes++;
            before = t;
        }
        Assert.That(shakes, Is.EqualTo(1));
        Assert.That(Rules.RoarShake(contact, contact, E, E), Is.False, "пауза на тике контакта");
        Assert.That(Rules.RoarShake(float.NaN, contact, E, E), Is.False, "первый кадр вида");
        Assert.That(Rules.RoarShake(contact - .2f, contact, E, E), Is.True);
        Assert.That(Rules.RoarShake(contact - 3f, contact + 4f, E, E), Is.True, "кадр длиннее тика не теряет тряску");
    }

    [Test]
    public void Texts_AndTitleSpacing()
    {
        Assert.That(Rules.Title, Is.EqualTo("Хозяин Чащи".ToUpperInvariant()), "имя — EnemyTexts.BossName");
        Assert.That(Rules.Subtitle, Is.Not.Empty);
        Assert.That(Rules.TitleSpacing(0f), Is.EqualTo(Rules.TitleSpacingFrom));
        Assert.That(Rules.TitleSpacing(1f), Is.EqualTo(Rules.TitleSpacingTo));
        Assert.That(Rules.TitleSpacingFrom, Is.GreaterThan(Rules.TitleSpacingTo), "буквы сходятся, а не разъезжаются");
        Assert.That(Rules.BarHeight + Rules.FeatherHeight, Is.LessThan(Rules.TitleY - .05f), "титр над нижней полосой");
    }

    // ------------------------------------------------------------ живая сцена

    private const int Hero = Simulation.PlayerId;

    /// <summary>Лесная локация без Unity (как ArenaEncounterTests.ForestLocation): 8 арен и босс.</summary>
    private static LocationDefinition ForestLocation(int arenas = 8)
    {
        var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
        var levels = new RiftLevelSettings[arenas + 1];
        for (int i = 0; i < levels.Length; i++)
        {
            int arena = i + 1;
            bool boss = i == arenas;
            var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                new[] { new EncounterPack(4, 100, new[] { guardian }) },
                1, 0, EnemyArchetypes.DepthDamagePercent(arena), Fix64.FromInt(5));
            levels[i] = new RiftLevelSettings(boss ? 20 : 11 + i, 1, 1, 2, 1, 3, EnemyArchetypes.DepthHealthPercent(arena),
                settings, boss, playerHealth: 150, entryClearance: 14, solidEnvironment: true, naturalGlade: true)
                .WithArenaSize(boss ? 4 : 3);
        }
        return new LocationDefinition(StableId.Of("location.test-forest"), PrototypeContent.Modules(), levels,
            64, completeAtEnd: true);
    }

    private static InputFrame Walk(Simulation sim, FixVec2 to)
    {
        var input = InputFrame.Empty;
        input.Flags = (byte)InputFlags.DirectMovement;
        input.MoveDirection = (to - sim.Entities.Position[Hero]).Normalized();
        input.Aim = to;
        return input;
    }

    private static bool Saw(Simulation sim, int boss, SimEventType type, EnemyActionKind kind)
    {
        foreach (var e in sim.Events)
            if (e.Type == type && e.Source == boss && e.ActionVariant == (int)kind) return true;
        return false;
    }

    [Test]
    public void LiveBossLevel_SimEventsLandOnTheRuleTicks_AndTheBossBarWaitsForTheEnd()
    {
        for (ulong seed = 1; seed <= 2; seed++)
        {
            string where = "сид " + seed;
            var location = ForestLocation();
            var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartTestAtLevel(9, true);
            var sim = run.Sim;
            int boss = run.BossId;
            Assert.That(sim.ThicketWakesOnClearing(boss), Is.True, where);

            // Тропа: босс спит, полоса босса ждёт.
            int start = -1;
            for (int k = 0; k < 200 && start < 0; k++)
            {
                Assert.That(Rules.HoldsBossBar(sim, boss), Is.True, "спит — полосы нет, " + where);
                int tick = sim.Tick;
                run.Step(Walk(sim, sim.Entities.Position[boss]));
                if (Saw(sim, boss, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro)) start = tick;
            }
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "дошёл до поляны, " + where);
            Assert.That(sim.TryGetThicketIntro(boss, out int s, out int wake, out int end), Is.True, where);
            Assert.That(s, Is.EqualTo(start), where);

            int wakeAt = -1, roarAt = -1, firstAttack = -1;
            int contact = Rules.RoarContact(end);
            while (sim.Tick <= end)
            {
                int tick = sim.Tick;
                // Вид в кадре сразу после шага tick (доля кадра 0): now = Tick − 1.
                run.Step(Walk(sim, sim.Entities.Position[boss]));
                float now = sim.Tick - 1;
                var look = Rules.LookAt(s, wake, end, end, now);
                Assert.That(look.Done, Is.False, "тик " + tick + ", " + where);
                if (Saw(sim, boss, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketWake)) wakeAt = tick;
                if (Saw(sim, boss, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketRoar)) roarAt = tick;
                if (tick < end)
                {
                    // Держит до тика E: после шага E − 1 следующий шаг героя уже свободен.
                    Assert.That(sim.ThicketIntroHoldsHero, Is.EqualTo(sim.Tick < end), "окно держит героя, тик " + tick);
                    Assert.That(Rules.HoldsBossBar(sim, boss), Is.True, "полоса ждёт конца, тик " + tick);
                    if (tick >= s + Rules.BarsInTicks) Assert.That(look.Bars, Is.EqualTo(1f).Within(Eps), "тик " + tick);
                    if (tick >= s + Rules.HudOutTicks) Assert.That(look.Hud, Is.EqualTo(0f).Within(Eps), "тик " + tick);
                    Assert.That(look.Title > 0f, Is.EqualTo(tick > Rules.TitleStart(wake)), "титр с начала рёва, тик " + tick);
                    if (Saw(sim, boss, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketRoar))
                        Assert.That(tick, Is.EqualTo(Rules.TitleStart(wake)), "начало рёва Sim — тик титра правил, " + where);
                }
                else
                {
                    Assert.That(sim.ThicketIntroHoldsHero, Is.False, "в тик E герой свободен, " + where);
                    Assert.That(Rules.HoldsBossBar(sim, boss), Is.False, "в тик E полоса встаёт, " + where);
                    if (Saw(sim, boss, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive)
                        || Saw(sim, boss, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw)) firstAttack = tick;
                    Assert.That(look.Bars, Is.EqualTo(1f).Within(Eps), "полосы уходят с тика E");
                    Assert.That(look.Hud, Is.EqualTo(0f).Within(Eps), "HUD возвращается с тика E");
                    Assert.That(look.Title, Is.EqualTo(0f), "первая атака — без титра, " + where);
                }
            }
            Assert.That(wakeAt, Is.EqualTo(wake), "пробуждение Sim — на тике W правил, " + where);
            Assert.That(roarAt, Is.EqualTo(contact), "кольцо рёва — на контакте правил (тряска и титр), " + where);
            Assert.That(firstAttack, Is.EqualTo(end), "первая атака — в тик E, " + where);
        }
    }

    [Test]
    public void BossBar_IsNotHeldOnStandsWithoutAClearing_OrForOtherBosses()
    {
        Assert.That(Rules.HoldsBossBar(null, 1), Is.False);
        var sim = new Simulation(7, 64);
        Assert.That(Rules.HoldsBossBar(sim, 1), Is.False, "нет сущности — нечего держать");
        Assert.That(Rules.HoldsBossBar(sim, -1), Is.False);
    }
}
