using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Темп Лесного хранителя. Числа закреплены здесь одним местом; остальные
    /// тесты берут константы.
    ///
    /// 29.09 (владелец): весь взмах на 10% медленнее — замах 21 → 23, стойка
    /// 15 → 17, цикл 48 → 53. 01.10 (ревью владельца): удар ещё медленнее —
    /// замах 23 → 28, стойка 17 → 21, цикл 53 → 65; ход 3,1 → 2,8 м/с;
    /// здоровье 500 → 270 («убивать за 5–6 обычных ударов»); и после удара
    /// не отходит назад, чтобы потом опять подойти и ударить.
    ///
    /// Замедление только Хранителя и моба без вида (стенды живут по его
    /// правилам). Окна и ход Корнеполза, Расщепеня и детёныша — их собственные.
    /// </summary>
    public class GuardianTempoTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena()
        {
            var sim = new Simulation(2909UL, 32);
            sim.SetupTestArena(0);
            sim.Entities.Stats[Simulation.PlayerId].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.RefreshStats(Simulation.PlayerId);
            sim.Entities.Health[Simulation.PlayerId] = 10000;
            return sim;
        }

        /// <summary>Моб вида kind с настоящей настройкой вида, лицом к герою, без критов.</summary>
        private static int Enemy(Simulation sim, FixVec2 at, EnemyKind kind = EnemyKind.ForestGuardian,
            bool stationary = true)
        {
            int id = sim.SpawnEnemy(at, 1000, kind);
            sim.Entities.Stats[id].SetBase(StatType.CritChance, Fix64.Zero);
            if (stationary) sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            return id;
        }

        /// <summary>Шагает до тика until; возвращает тики, на которых source ударил героя.</summary>
        private static List<int> Until(Simulation sim, int until, int source)
        {
            var hits = new List<int>();
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == source && e.Target == Simulation.PlayerId)
                        hits.Add(tick);
            }
            return hits;
        }

        private static double HeroDistance(Simulation sim, int id)
            => FixVec2.Distance(sim.Entities.Position[id], sim.Entities.Position[Simulation.PlayerId]).ToDouble();

        // ---- числа ----

        [Test]
        public void Windows_AreTheOwnersSlowerSwing()
        {
            Assert.AreEqual(28, Simulation.GuardianSwingWindupTicks, "замах 23 → 28 (01.10)");
            Assert.AreEqual(21, Simulation.GuardianSwingRecoveryTicks, "стойка после удара 17 → 21 (01.10)");
            Assert.AreEqual(65, Simulation.GuardianSwingCycleTicks, "цикл 53 → 65 (01.10)");
            Assert.AreEqual(16, Simulation.GuardianSwingCycleTicks - Simulation.GuardianSwingWindupTicks
                - Simulation.GuardianSwingRecoveryTicks, "свободных тиков в цикле");
            // «Замедлить заметно»: каждое окно не меньше чем на 20% длиннее чисел 29.09 (23/17/53).
            Assert.GreaterOrEqual(Simulation.GuardianSwingWindupTicks * 100, 23 * 120, "замах медленнее на 20%+");
            Assert.GreaterOrEqual(Simulation.GuardianSwingRecoveryTicks * 100, 17 * 120, "стойка медленнее на 20%+");
            Assert.GreaterOrEqual(Simulation.GuardianSwingCycleTicks * 100, 53 * 120, "цикл медленнее на 20%+");
            Assert.AreEqual(Simulation.GuardianSwingWindupTicks, Simulation.EnemyAttackWindupTicks,
                "EnemyAttackWindupTicks — прежнее имя того же замаха, а не второе число");

            // Строка Хранителя в таблице видов повторяет тот же темп.
            var row = EnemyArchetypes.Get(EnemyKind.ForestGuardian);
            Assert.AreEqual(28, row.WindupTicks, "таблица видов: замах");
            Assert.AreEqual(21, row.RecoveryTicks, "таблица видов: стойка");
            Assert.AreEqual(65, row.CycleTicks, "таблица видов: цикл");
        }

        [TestCase(EnemyKind.ForestGuardian)]
        [TestCase(EnemyKind.None)]
        public void ProfileAndAttackSpeed_GiveTheSameWindows(EnemyKind kind)
        {
            var profile = Simulation.MeleeProfileOf(kind);
            Assert.AreEqual(28, profile.WindupTicks);
            Assert.AreEqual(21, profile.RecoveryTicks);

            // Скорость атаки со стата переводится обратно в тики без потерь:
            // 30/65 атаки в секунду — ровно 65 тиков, а не 64 или 66.
            var sim = Arena();
            int id = sim.SpawnEnemy(At(4, 0), 1000, kind);
            Assert.AreEqual(65, sim.Entities.AttackCooldown[id]);
        }

        [TestCase(EnemyKind.ForestGuardian)]
        [TestCase(EnemyKind.None)]
        public void Walk_IsTenPercentSlower_OnlyForTheGuardian(EnemyKind kind)
        {
            Assert.AreEqual(Fix64.Ratio(28, 10), Simulation.GuardianMoveSpeed, "ход 3,1 → 2,8 м/с (01.10)");
            double ratio = Simulation.GuardianMoveSpeed.ToDouble() / Simulation.EnemyBaseMoveSpeed.ToDouble();
            Assert.That(ratio, Is.InRange(0.88, 0.92), "«немного медленнее» — около 10%");

            var sim = Arena();
            int id = sim.SpawnEnemy(At(4, 0), 1000, kind);
            Assert.AreEqual(CombatStats.MoveStepPerTick(Simulation.GuardianMoveSpeed), sim.Entities.MoveStep[id]);

            // Остальные виды ходят своими числами.
            int swarm = sim.SpawnEnemy(At(-4, 0), 100, EnemyKind.ForestRootSwarm);
            Assert.AreEqual(CombatStats.MoveStepPerTick(Simulation.RootSwarmMoveSpeed), sim.Entities.MoveStep[swarm]);
            int splitter = sim.SpawnEnemy(At(0, 4), 100, EnemyKind.ForestSplitter);
            Assert.AreEqual(CombatStats.MoveStepPerTick(Simulation.SplitterMoveSpeed), sim.Entities.MoveStep[splitter]);
        }

        // ---- живой замах ----

        [Test]
        public void Swing_LandsOnTickTwentyEight_StandsTwentyOne_RepeatsEverySixtyFive()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);

            Assert.IsTrue(sim.TryGetEnemySwing(g, out var swing));
            Assert.AreEqual(0, swing.StartTick);
            Assert.AreEqual(28, swing.ImpactTick);
            Assert.AreEqual(49, swing.RecoverUntil);
            Assert.AreEqual(65, sim.Entities.NextAttackTick[g]);
            // AttackImpactTick врага читает TickDriver.Stonehoof (уворот в съёмке).
            Assert.AreEqual(swing.ImpactTick, sim.Entities.AttackImpactTick[g],
                "контакт в сущности совпадает с замахом");
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var mark));
            Assert.AreEqual(28, mark.ImpactTick, "метка гаснет в контакт, а не по старому окну");

            // Три цикла подряд: контакт на 28, 93 и 158 — ни тиком раньше.
            var hits = Until(sim, 3 * 65, g);
            CollectionAssert.AreEqual(new[] { 28, 93, 158 }, hits);
        }

        [Test]
        public void AfterContact_StandsTwentyOneTicks_ThenWalksAfterTheHero()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0), stationary: false);
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            Until(sim, 29, g);
            // Герой ушёл: моб рвался бы за ним, но окно наказания держит тело.
            sim.Entities.Position[Simulation.PlayerId] = At(-5, 0);
            while (sim.Tick < 49)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(swing.Origin, sim.Entities.Position[g], "стойка, тик " + sim.Tick);
            }
            double before = HeroDistance(sim, g);
            sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(swing.Origin, sim.Entities.Position[g], "после 21 тика моб снова идёт");
            for (int t = 0; t < 10; t++) sim.Step(InputFrame.Empty);
            Assert.Less(HeroDistance(sim, g), before - 0.5, "идёт за героем вперёд, а не назад");
        }

        // Отход до правки — 0,07–0,09 м за тик, 0,42 м за паузу. Допуск — на
        // скольжение по дуге к своему месту: шаг вбок на радиусе 2 м отдаляет
        // от героя на шаг²/2r ≈ 2 мм, это не шаг назад.
        private const double StepBackTolerance = 0.003, DriftTolerance = 0.05;

        /// <summary>
        /// Ревью владельца 01.10: «после атаки зачем-то отходит назад, а потом
        /// опять бьёт». Причина — кольцо ожидания 3 м в свободные тики цикла
        /// (Simulation.HoldsGroundBetweenSwings): трасса до правки — 0,42 м задом
        /// за 7 тиков после каждой стойки, потом столько же вперёд и новый замах.
        /// Теперь от первого замаха до конца ни один тик не уводит Хранителя от
        /// героя — ни в замахе, ни в стойке, ни в паузе до следующего удара.
        /// </summary>
        [Test]
        public void AfterASwing_NeverStepsBackFromTheHero_AndKeepsSwinging()
        {
            var sim = Arena();
            sim.PlayerInvulnerable = true;
            int g = Enemy(sim, At(6, 0), stationary: false);
            int swings = 0, recoveryTicks = 0;
            bool swung = false;
            double last = HeroDistance(sim, g), drift = 0;
            FixVec2 origin = default;
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source == g) { swings++; swung = true; }
                double now = HeroDistance(sim, g);
                if (swung)
                {
                    Assert.LessOrEqual(now - last, StepBackTolerance, "Хранитель отошёл от героя, тик " + sim.Tick);
                    if (now > last) drift += now - last;
                }
                last = now;
                if (sim.TryGetEnemySwing(g, out var swing) && swing.HitResolved && sim.Tick <= swing.RecoverUntil)
                {
                    if (sim.Tick == swing.ImpactTick + 1) origin = sim.Entities.Position[g];
                    else Assert.AreEqual(origin, sim.Entities.Position[g], "стойка держит тело, тик " + sim.Tick);
                    recoveryTicks++;
                }
            }
            Assert.GreaterOrEqual(swings, 8, "за 20 с Хранитель бьёт раз в цикл, а не ходит туда-обратно");
            Assert.Greater(recoveryTicks, 0);
            Assert.Less(drift, DriftTolerance, "суммарно от героя, м");
        }

        /// <summary>
        /// Пачка леса — не больше двух Хранителей: жетонов два, ждать некому, и
        /// оба держат место между ударами. Ждущий жетона (третий ближник) по-прежнему
        /// отходит на кольцо ожидания — это EnemyBrainTests и AttackTokenTests.
        /// </summary>
        [Test]
        public void TwoGuardians_NeitherStepsBackBetweenSwings()
        {
            var sim = Arena();
            sim.PlayerInvulnerable = true;
            int a = Enemy(sim, At(6, 0), stationary: false), b = Enemy(sim, At(-6, 0.5), stationary: false);
            var last = new Dictionary<int, double> { [a] = HeroDistance(sim, a), [b] = HeroDistance(sim, b) };
            var swung = new HashSet<int>();
            var swings = new Dictionary<int, int> { [a] = 0, [b] = 0 };
            var drift = new Dictionary<int, double> { [a] = 0, [b] = 0 };
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && (e.Source == a || e.Source == b)) { swung.Add(e.Source); swings[e.Source]++; }
                foreach (int id in new[] { a, b })
                {
                    double now = HeroDistance(sim, id);
                    if (swung.Contains(id))
                    {
                        Assert.LessOrEqual(now - last[id], StepBackTolerance, "Хранитель " + id + " отошёл от героя, тик " + sim.Tick);
                        if (now > last[id]) drift[id] += now - last[id];
                    }
                    last[id] = now;
                }
            }
            Assert.GreaterOrEqual(swings[a], 7);
            Assert.GreaterOrEqual(swings[b], 7);
            Assert.Less(drift[a], DriftTolerance, "суммарно от героя, м");
            Assert.Less(drift[b], DriftTolerance, "суммарно от героя, м");
        }

        // ---- здоровье: «убивать за 5–6 обычных ударов» ----

        /// <summary>Сколько ударов обычной атаки (54 и тяжёлый 68 по очереди) уходит на health.</summary>
        private static int BasicHitsToKill(int health, bool heavyFirst)
        {
            int light = Progression.ReferenceHeroDamage;
            int heavy = CombatStats.RoundToInt(Fix64.FromInt(light) * Fix64.Ratio(5, 4));
            int dealt = 0, hits = 0;
            bool nextHeavy = heavyFirst;
            while (dealt < health) { dealt += nextHeavy ? heavy : light; nextHeavy = !nextHeavy; hits++; }
            return hits;
        }

        /// <summary>
        /// Живой бой: эталонный герой (270/54) без критов бьёт Хранителя первой
        /// арены обычной атакой. Удары чередуются — 54 и тяжёлый 68 (×1,25), и
        /// с какого из двух начнёт серия, зависит от прошлых взмахов, поэтому
        /// проверены обе фазы: 5 ударов (298 и 312 против 270).
        /// </summary>
        [TestCase(false, 5)]
        [TestCase(true, 5)]
        public void ArenaOneGuardian_DiesToFiveOrSixBasicHits(bool heavyFirst, int expectedHits)
        {
            var sim = new Simulation(101UL, 32);
            sim.ApplyHeroBaseline();
            sim.SetupTestArena(0);
            sim.PlayerInvulnerable = true;
            var e = sim.Entities;
            Assert.AreEqual(Progression.ReferenceHeroDamage, e.Damage[Simulation.PlayerId], "эталонный удар 54");
            e.Stats[Simulation.PlayerId].SetBase(StatType.CritChance, Fix64.Zero);
            e.RefreshStats(Simulation.PlayerId);

            int guardian = sim.AddKindTestEnemy(EnemyKind.ForestGuardian, At(2, 0), 100);
            e.Stats[guardian].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.RefreshStats(guardian);
            Assert.AreEqual(EnemyArchetypes.ScaleHealth(EnemyArchetypes.Get(EnemyKind.ForestGuardian).BaseHealth,
                EnemyArchetypes.DepthHealthPercent(1)), e.MaxHealth[guardian], "здоровье Хранителя первой арены");
            Assert.AreEqual(e.MaxHealth[guardian], e.Health[guardian]);

            // Тяжёлый первым — один обычный удар по манекену сзади сдвигает очередь.
            if (heavyFirst)
            {
                int decoy = sim.SpawnEnemy(At(-2, 0), 1000, EnemyKind.ForestGuardian);
                e.Stats[decoy].SetBase(StatType.MoveSpeed, Fix64.Zero);
                e.RefreshStats(decoy);
                for (int t = 0; t < 120; t++)
                {
                    sim.Step(new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = decoy,
                        Aim = e.Position[decoy], AbilityTarget = -1 });
                    bool landed = false;
                    foreach (var ev in sim.Events)
                        if (ev.Type == SimEventType.Damage && ev.Source == Simulation.PlayerId && ev.Target == decoy) landed = true;
                    if (landed) break;
                }
                Assert.AreEqual(1000 - Progression.ReferenceHeroDamage, e.Health[decoy], "по манекену ушёл один обычный удар");
            }

            var dealt = new List<int>();
            for (int t = 0; t < 600 && e.Alive[guardian]; t++)
            {
                sim.Step(new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = guardian,
                    Aim = e.Position[guardian], AbilityTarget = -1 });
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.Damage && ev.Source == Simulation.PlayerId && ev.Target == guardian)
                    {
                        Assert.IsFalse(ev.Flag, "крит выключен");
                        dealt.Add(ev.Amount);
                    }
            }
            Assert.IsFalse(e.Alive[guardian], "Хранитель жив после " + dealt.Count + " ударов");
            Assert.AreEqual(heavyFirst ? 68 : 54, dealt[0], "первый удар серии");
            Assert.AreEqual(expectedHits, dealt.Count, "ударов до смерти: " + string.Join(", ", dealt));
            Assert.AreEqual(BasicHitsToKill(e.MaxHealth[guardian], heavyFirst), dealt.Count, "счёт по формуле совпал с боем");
            Assert.That(dealt.Count, Is.InRange(5, 6), "правило владельца: 5–6 обычных ударов");
        }

        /// <summary>
        /// С глубиной здоровье растёт на 7% за арену: до шестой арены — всё ещё
        /// 5–6 обычных ударов с любой фазы серии, на седьмой и восьмой — 7.
        /// </summary>
        [Test]
        public void DepthScaling_KeepsFiveToSixHitsThroughArenaSix()
        {
            int baseHealth = EnemyArchetypes.Get(EnemyKind.ForestGuardian).BaseHealth;
            Assert.AreEqual(270, baseHealth, "здоровье 500 → 270 (01.10)");
            for (int arena = 1; arena <= 8; arena++)
            {
                int health = EnemyArchetypes.ScaleHealth(baseHealth, EnemyArchetypes.DepthHealthPercent(arena));
                int min = System.Math.Min(BasicHitsToKill(health, false), BasicHitsToKill(health, true));
                int max = System.Math.Max(BasicHitsToKill(health, false), BasicHitsToKill(health, true));
                TestContext.WriteLine("арена " + arena + ": " + health + " HP, " + min + "–" + max + " ударов");
                if (arena <= 6) Assert.That(min >= 5 && max <= 6, Is.True, "арена " + arena + ": " + min + "–" + max);
                else Assert.AreEqual(7, max, "арена " + arena);
            }
            Assert.AreEqual(327, EnemyArchetypes.ScaleHealth(baseHealth, EnemyArchetypes.DepthHealthPercent(4)));
            Assert.AreEqual(402, EnemyArchetypes.ScaleHealth(baseHealth, EnemyArchetypes.DepthHealthPercent(8)));
        }

        // ---- остальные виды не задеты ----

        [Test]
        public void OtherMeleeKinds_KeepTheirOwnWindows()
        {
            var swarm = Simulation.MeleeProfileOf(EnemyKind.ForestRootSwarm);
            Assert.AreEqual(Simulation.RootSwarmAttackWindupTicks, swarm.WindupTicks);
            Assert.AreEqual(Simulation.RootSwarmRecoveryTicks, swarm.RecoveryTicks);
            var splitter = Simulation.MeleeProfileOf(EnemyKind.ForestSplitter);
            Assert.AreEqual(Simulation.SplitterSwingWindupTicks, splitter.WindupTicks);
            Assert.AreEqual(Simulation.SplitterSwingRecoveryTicks, splitter.RecoveryTicks);
            var splitling = Simulation.MeleeProfileOf(EnemyKind.ForestSplitling);
            Assert.AreEqual(Simulation.SplitlingBiteWindupTicks, splitling.WindupTicks);
            Assert.AreEqual(Simulation.SplitlingBiteRecoveryTicks, splitling.RecoveryTicks);
            Assert.IsFalse(Simulation.HoldsGroundBetweenSwings(EnemyKind.ForestSplitter), "Расщепень ходит как раньше");
            Assert.IsFalse(Simulation.HoldsGroundBetweenSwings(EnemyKind.ForestRootSwarm), "рой ходит как раньше");

            var sim = Arena();
            Assert.AreEqual(Simulation.RootSwarmAttackCooldownTicks,
                sim.Entities.AttackCooldown[sim.SpawnEnemy(At(4, 0), 100, EnemyKind.ForestRootSwarm)]);
            Assert.AreEqual(Simulation.SplitterSwingCycleTicks,
                sim.Entities.AttackCooldown[sim.SpawnEnemy(At(-4, 0), 100, EnemyKind.ForestSplitter)]);

            // Живой укус Корнеполза: контакт на его собственном замахе, не на замахе Хранителя.
            var live = Arena();
            int s = Enemy(live, At(1.2, 0), EnemyKind.ForestRootSwarm);
            live.Step(InputFrame.Empty);
            Assert.IsTrue(live.TryGetEnemySwing(s, out var bite));
            Assert.AreEqual(Simulation.RootSwarmAttackWindupTicks, bite.ImpactTick - bite.StartTick);
            Assert.AreEqual(Simulation.RootSwarmRecoveryTicks, bite.RecoverUntil - bite.ImpactTick);
            Assert.AreEqual(bite.ImpactTick, live.Entities.AttackImpactTick[s],
                "контакт укуса в сущности совпадает с замахом");
        }
    }
}
