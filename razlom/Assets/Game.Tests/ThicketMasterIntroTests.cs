using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Хозяин Чащи — вступление-кат-сцена (владелец 02.10, Simulation.ForestBoss.Intro):
    /// герой идёт снизу по тропе, босс спит, пока герой не ступит на пол поляны
    /// (или не ранит его); с этого тика 105 тиков вступления — подлёт камеры 24,
    /// пробуждение 30, рёв 36 + 15. Всё окно герой не слушается и неуязвим, босс
    /// стоит; в тик конца рёва — сразу первая атака (лапа в досягаемости, иначе
    /// нырок), без отдыха и без ожидания удара героя. Стенд без поляны — по-старому.
    ///
    /// Арена — настоящий уровень босса (ArenaEncounterTests.ForestLocation, уровень 9)
    /// через «К боссу» (RiftRun.StartTestAtLevel(9, true)).
    /// </summary>
    public sealed class ThicketMasterIntroTests
    {
        private const int Hero = Simulation.PlayerId;

        private static RiftRun Jump(ulong seed)
        {
            var location = ArenaEncounterTests.ForestLocation();
            var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartTestAtLevel(9, true);
            return run;
        }

        /// <summary>Шаг прямым ходом (WASD) к точке to.</summary>
        private static InputFrame Walk(Simulation sim, FixVec2 to)
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.DirectMovement;
            input.MoveDirection = (to - sim.Entities.Position[Hero]).Normalized();
            input.Aim = to;
            return input;
        }

        /// <summary>Всё сразу: идёт на босса, жмёт атаку, все способности, артефакт.</summary>
        private static InputFrame Fight(Simulation sim, int boss)
        {
            var input = Walk(sim, sim.Entities.Position[boss]);
            input.Flags |= (byte)(InputFlags.Attack | InputFlags.AttackPressed | InputFlags.UseArtifact);
            input.AbilityMask = 0xF;
            input.AbilityHoldMask = 0xF;
            input.AttackTarget = boss;
            input.AbilityTarget = boss;
            return input;
        }

        private static int Count(Simulation sim, SimEventType type, EnemyActionKind kind, int amount = -1)
        {
            int n = 0;
            foreach (var e in sim.Events)
                if (e.Type == type && e.ActionVariant == (int)kind && (amount < 0 || e.Amount == amount)) n++;
            return n;
        }

        /// <summary>Ведёт героя по тропе на босса; тик начала вступления (герой ступил на пол).</summary>
        private static int EnterClearing(RiftRun run, int limit = 150)
        {
            var sim = run.Sim;
            var glade = run.Map.GetGlade(0);
            for (int k = 0; k < limit; k++)
            {
                Assert.That(glade.Field(sim.Entities.Position[Hero]), Is.GreaterThan(Fix64.One), "вошёл без вступления");
                int tick = sim.Tick;
                run.Step(Walk(sim, sim.Entities.Position[run.BossId]));
                if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro) > 0) return tick;
            }
            Assert.Fail("за " + limit + " тиков герой не дошёл до поляны");
            return -1;
        }

        [Test]
        public void Trail_BossSleeps_IntroStartsTheTickTheHeroStepsOntoTheFloor()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                string where = "сид " + seed;
                var run = Jump(seed);
                var sim = run.Sim;
                var e = sim.Entities;
                int boss = run.BossId;
                var glade = run.Map.GetGlade(0);
                Assert.IsTrue(sim.ThicketWakesOnClearing(boss), where);
                var start = e.Position[Hero];
                Assert.That(glade.Field(start), Is.GreaterThan(Fix64.One), "«К боссу» — на тропе, " + where);
                Assert.That(FixVec2.Distance(start, e.Position[boss]).ToDouble(), Is.InRange(10.0, 13.0), where);

                // Стоит на тропе 30 с — босс спит, вступления нет.
                for (int k = 0; k < 900; k++)
                {
                    run.Step(InputFrame.Empty);
                    Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro), where);
                    Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketWake), where);
                }
                Assert.IsFalse(sim.ThicketMasterAwake(boss), where);
                Assert.IsFalse(sim.TryGetThicketIntro(boss, out _, out _, out _), where);
                Assert.IsFalse(sim.ThicketIntroHoldsHero, where);

                int entered = EnterClearing(run);
                Assert.That(glade.Field(e.Position[Hero]), Is.LessThanOrEqualTo(Fix64.One), "в тик входа — на полу, " + where);
                Assert.IsTrue(sim.TryGetThicketIntro(boss, out int begin, out int wake, out int end), where);
                Assert.AreEqual(entered, begin, where);
                Assert.AreEqual(begin + Simulation.ThicketIntroLeadTicks, wake, where);
                Assert.AreEqual(begin + Simulation.ThicketIntroTicks, end, where);
                Assert.AreEqual(105, Simulation.ThicketIntroTicks);
                Assert.IsFalse(sim.ThicketMasterAwake(boss), "камера ещё летит — спит, " + where);
                Assert.IsTrue(sim.ThicketIntroHoldsHero, where);
            }
        }

        [Test]
        public void Window_HeroIgnoresInputAndTakesNoDamage_BossStandsWakesRoars_ThenAttacksTheTickTheRoarEnds()
        {
            var run = Jump(5);
            var sim = run.Sim;
            var e = sim.Entities;
            int boss = run.BossId;
            var home = e.Position[boss];
            EnterClearing(run);
            Assert.IsTrue(sim.TryGetThicketIntro(boss, out int begin, out int wake, out int end));
            int health = e.Health[Hero];
            FixVec2 stood = default;
            int wakeAt = -1, roarAt = -1, blastAt = -1;
            while (sim.Tick < end)
            {
                int tick = sim.Tick;
                Assert.IsTrue(sim.ThicketIntroHoldsHero, "тик " + tick);
                if (tick % 10 == 0)
                {
                    sim.ApplyAbilityDamage(boss, Hero, 50, -1, DamageType.Physical);
                    sim.ApplyAbilityDamage(boss, Hero, 5, -1, DamageType.Fire, overTime: true);
                }
                run.Step(Fight(sim, boss));
                Assert.AreEqual(health, e.Health[Hero], "урон во вступлении, тик " + tick);
                Assert.AreEqual(home, e.Position[boss], "босс стоит, тик " + tick);
                Assert.IsFalse(ForcedMotion.IsActive(e, Hero), "отброс во вступлении, тик " + tick);
                // Тормозит своим шагом (разгон 3 тика) и стоит.
                if (tick == begin + 6) stood = e.Position[Hero];
                if (tick > begin + 6) Assert.AreEqual(stood, e.Position[Hero], "герой стоит, тик " + tick);
                foreach (var ev in sim.Events)
                {
                    if (ev.Source == Hero)
                        Assert.That(ev.Type == SimEventType.AbilityCast || ev.Type == SimEventType.SabreContact
                            || ev.Type == SimEventType.DashStarted || ev.Type == SimEventType.ArtifactUsed, Is.False,
                            "герой действует во вступлении: " + ev.Type + ", тик " + tick);
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketWake) wakeAt = tick;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketRoar) roarAt = tick;
                    if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.ThicketRoar) blastAt = tick;
                    Assert.That(ev.Type == SimEventType.EnemyActionStarted && ev.Amount == 0
                        && (ev.ActionVariant == (int)EnemyActionKind.ThicketPaw || ev.ActionVariant == (int)EnemyActionKind.ThicketDive
                            || ev.ActionVariant == (int)EnemyActionKind.ThicketStomp), Is.False, "атака раньше конца рёва, тик " + tick);
                }
            }
            Assert.AreEqual(wake, wakeAt, "пробуждение после подлёта камеры");
            Assert.AreEqual(wake + Simulation.ThicketWakeTicks, roarAt, "рёв сразу за пробуждением");
            Assert.AreEqual(roarAt + Simulation.ThicketRoarWindupTicks, blastAt, "кольцо рёва");
            Assert.AreEqual(end, roarAt + Simulation.ThicketRoarWindupTicks + Simulation.ThicketRoarRecoveryTicks, "конец окна — конец рёва");
            Assert.IsTrue(sim.ThicketMasterAwake(boss));

            // Тик конца рёва: окно снято — герой снова слушается, босс сразу начинает атаку.
            Assert.IsFalse(sim.ThicketIntroHoldsHero);
            Assert.That(FixVec2.Distance(e.Position[Hero], home).ToDouble(), Is.GreaterThan(Simulation.ThicketPawStartRange.ToDouble()));
            run.Step(Walk(sim, home));
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive, 0),
                "герой у кромки, лапа не достаёт — нырок к нему в тик конца рёва");
            Assert.IsTrue(sim.TryGetThicketMasterAction(boss, out var dive));
            Assert.AreEqual(ThicketMasterAction.Dive, dive.Action);
            Assert.AreEqual(end, dive.StartTick);
            Assert.AreNotEqual(stood, e.Position[Hero], "ввод героя снова читается");
            sim.ApplyAbilityDamage(boss, Hero, 40, -1, DamageType.Physical);
            Assert.That(e.Health[Hero], Is.LessThan(health), "неуязвимость кончилась с окном");
        }

        /// <summary>
        /// Бил и жал всё до самого пола: удар серии, начатый в тик входа или ждущий в буфере
        /// нажатия, рывок, прыжок цепи, бросок — ничего не доигрывает в окне (CancelPlayerAction
        /// в тик начала), и ничто начатое до окна не ранит босса (ThicketIntroShields).
        /// </summary>
        [Test]
        public void PressedRightBeforeTheFloor_NothingOfTheHeroLeaksIntoTheWindow_BossTakesNoDamage()
        {
            for (ulong seed = 3; seed <= 6; seed++)
            {
                string where = "сид " + seed;
                var run = Jump(seed);
                var sim = run.Sim;
                var e = sim.Entities;
                int boss = run.BossId;
                // Тропа шагом, последние метры до кромки — бой: каждый тик удар, все способности, артефакт.
                int begin = -1;
                for (int k = 0; k < 600 && begin < 0; k++)
                {
                    int tick = sim.Tick;
                    bool close = FixVec2.Distance(e.Position[Hero], e.Position[boss]) < Fix64.FromInt(10);
                    run.Step(close ? Fight(sim, boss) : Walk(sim, e.Position[boss]));
                    if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro) > 0) begin = tick;
                }
                Assert.That(begin, Is.GreaterThanOrEqualTo(0), "дошёл до поляны, " + where);
                Assert.IsTrue(sim.TryGetThicketIntro(boss, out int start, out _, out int end), where);
                Assert.AreEqual(begin, start, where);
                int bossHealth = e.Health[boss];
                FixVec2 stood = default;
                while (sim.Tick < end)
                {
                    int tick = sim.Tick;
                    run.Step(Fight(sim, boss));
                    foreach (var ev in sim.Events)
                        if (ev.Source == Hero)
                            Assert.That(ev.Type == SimEventType.Attack || ev.Type == SimEventType.SabreContact
                                || ev.Type == SimEventType.AbilityCast || ev.Type == SimEventType.DashStarted
                                || ev.Type == SimEventType.ArtifactUsed, Is.False,
                                "герой действует во вступлении: " + ev.Type + ", тик " + tick + ", " + where);
                    Assert.AreEqual(bossHealth, e.Health[boss], "босс во вступлении не теряет здоровья, тик " + tick + ", " + where);
                    // Тормозит своим шагом и стоит: ни выпада, ни полёта цепи, ни рывка.
                    if (tick == start + 8) stood = e.Position[Hero];
                    if (tick > start + 8) Assert.AreEqual(stood, e.Position[Hero], "герой стоит, тик " + tick + ", " + where);
                }
                Assert.IsFalse(sim.ThicketIntroHoldsHero, where);
            }
        }

        [Test]
        public void HeroAlreadyInReach_OpensWithThePawSeries_TheTickTheRoarEnds()
        {
            var run = Jump(7);
            var sim = run.Sim;
            var e = sim.Entities;
            int boss = run.BossId;
            // Вбежал рывком прямо к морде: первый же тик на полу — вступление.
            e.Position[Hero] = run.Map.ClampToWalkable(e.Position[boss] - new FixVec2(Fix64.Zero, Fix64.FromInt(3)),
                e.BodyRadius[Hero]);
            int tick = sim.Tick;
            run.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro));
            Assert.IsTrue(sim.TryGetThicketIntro(boss, out int begin, out _, out int end));
            Assert.AreEqual(tick, begin);
            while (sim.Tick < end) run.Step(InputFrame.Empty);
            // Конец рёва: босс к герою лицом, герой у морды — в досягаемости лапы.
            e.Position[Hero] = e.Position[boss] + e.Facing[boss].Normalized() * Fix64.Ratio(5, 2);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw, 0), "лапа в тик конца рёва");
            Assert.IsTrue(sim.TryGetThicketMasterAction(boss, out var paw));
            Assert.AreEqual(ThicketMasterAction.Paw, paw.Action);
            Assert.AreEqual(end, paw.StartTick);
            int health = e.Health[boss];
            sim.ApplyAbilityDamage(Hero, boss, 100, -1, DamageType.Physical);
            Assert.That(e.Health[boss], Is.LessThan(health), "с концом окна босса снова бьют");
        }

        [Test]
        public void HurtFromTheTrail_StartsTheIntroWithoutStepping()
        {
            var run = Jump(2);
            var sim = run.Sim;
            int boss = run.BossId;
            for (int k = 0; k < 30; k++) run.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetThicketIntro(boss, out _, out _, out _));
            sim.ApplyAbilityDamage(Hero, boss, 10, -1, DamageType.Physical);
            int tick = sim.Tick;
            run.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketIntro), "ранен — просыпается");
            Assert.IsTrue(sim.TryGetThicketIntro(boss, out int begin, out _, out _));
            Assert.AreEqual(tick, begin);
            Assert.That(run.Map.GetGlade(0).Field(sim.Entities.Position[Hero]), Is.GreaterThan(Fix64.One), "герой на тропе");
        }

        [Test]
        public void BossIsUntouchableInTheIntro_ItsDeathStillReleasesTheHero()
        {
            var run = Jump(4);
            var sim = run.Sim;
            var e = sim.Entities;
            int boss = run.BossId;
            EnterClearing(run);
            Assert.IsTrue(sim.ThicketIntroHoldsHero);
            // Кат-сцена для обеих сторон: ни удар, ни горение по боссу не проходят.
            int health = e.Health[boss];
            sim.ApplyAbilityDamage(Hero, boss, e.MaxHealth[boss] * 2, -1, DamageType.Physical);
            sim.ApplyAbilityDamage(Hero, boss, 50, -1, DamageType.Fire, overTime: true);
            Assert.IsTrue(e.Alive[boss], "босса во вступлении не бьют");
            Assert.AreEqual(health, e.Health[boss]);
            Assert.IsTrue(sim.ThicketIntroHoldsHero);
            // Смерть в окне (страховка: урон сюда не доходит) — удержание гаснет сразу.
            e.Alive[boss] = false;
            Assert.IsFalse(sim.ThicketIntroHoldsHero, "окно гаснет со смертью босса");
        }

        [Test]
        public void Stand_WithoutClearing_KeepsTheOldWake_NoWindow()
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
            const int boss = 1;
            Assert.IsFalse(sim.ThicketWakesOnClearing(boss));
            while (sim.Tick < Simulation.ThicketMinSleepTicks) sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.ThicketMasterAwake(boss), "стенд: 90 тиков сна, как было");
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.ThicketMasterAwake(boss), "стенд: герой в 6 м — проснулся");
            Assert.IsFalse(sim.TryGetThicketIntro(boss, out _, out _, out _));
            Assert.IsFalse(sim.ThicketIntroHoldsHero);
        }

        [Test]
        public void TwoRuns_SameHashAndIntroEveryTick()
        {
            var a = Jump(11);
            var b = Jump(11);
            Assert.AreEqual(a.Sim.StateHash(), b.Sim.StateHash(), "разошлись уже при расстановке");
            int boss = a.BossId;
            for (int t = 0; t < 420; t++)
            {
                // Минута на тропе, потом в бой: идёт на босса и бьёт.
                InputFrame ia = t < 60 ? InputFrame.Empty : Fight(a.Sim, boss);
                InputFrame ib = t < 60 ? InputFrame.Empty : Fight(b.Sim, boss);
                a.Step(ia);
                b.Step(ib);
                Assert.AreEqual(a.Sim.StateHash(), b.Sim.StateHash(), "StateHash разошёлся на тике " + t);
            }
            Assert.IsTrue(a.Sim.TryGetThicketIntro(boss, out int sa, out _, out int ea));
            Assert.IsTrue(b.Sim.TryGetThicketIntro(boss, out int sb, out _, out int eb));
            Assert.AreEqual(sa, sb);
            Assert.AreEqual(ea, eb);
            Assert.That(a.Sim.Tick, Is.GreaterThan(ea), "сценарий дошёл до боя");
        }
    }
}
