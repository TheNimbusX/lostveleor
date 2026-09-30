using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Темп Лесного хранителя. Решение владельца от 29.09: весь взмах на 10%
    /// медленнее — замах 21 → 23 тика, стойка после удара 15 → 17, цикл
    /// 48 → 53. Числа закреплены здесь одним местом; остальные тесты берут
    /// константы.
    ///
    /// Замедление только Хранителя и моба без вида (стенды живут по его
    /// правилам). Окна Корнеполза, Расщепеня и детёныша — их собственные.
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

        // ---- числа ----

        [Test]
        public void Windows_AreTheOwnersTenPercentSlowerSwing()
        {
            Assert.AreEqual(23, Simulation.GuardianSwingWindupTicks, "замах 21 → 23");
            Assert.AreEqual(17, Simulation.GuardianSwingRecoveryTicks, "стойка после удара 15 → 17");
            Assert.AreEqual(53, Simulation.GuardianSwingCycleTicks, "цикл 48 → 53");
            Assert.AreEqual(13, Simulation.GuardianSwingCycleTicks - Simulation.GuardianSwingWindupTicks
                - Simulation.GuardianSwingRecoveryTicks, "свободных тиков в цикле");
            Assert.AreEqual(Simulation.GuardianSwingWindupTicks, Simulation.EnemyAttackWindupTicks,
                "EnemyAttackWindupTicks — прежнее имя того же замаха, а не второе число");

            // Строка Хранителя в таблице видов повторяет тот же темп.
            var row = EnemyArchetypes.Get(EnemyKind.ForestGuardian);
            Assert.AreEqual(23, row.WindupTicks, "таблица видов: замах");
            Assert.AreEqual(17, row.RecoveryTicks, "таблица видов: стойка");
            Assert.AreEqual(53, row.CycleTicks, "таблица видов: цикл");
        }

        [TestCase(EnemyKind.ForestGuardian)]
        [TestCase(EnemyKind.None)]
        public void ProfileAndAttackSpeed_GiveTheSameWindows(EnemyKind kind)
        {
            var profile = Simulation.MeleeProfileOf(kind);
            Assert.AreEqual(23, profile.WindupTicks);
            Assert.AreEqual(17, profile.RecoveryTicks);

            // Скорость атаки со стата переводится обратно в тики без потерь:
            // 30/53 атаки в секунду — ровно 53 тика, а не 52 или 54.
            var sim = Arena();
            int id = sim.SpawnEnemy(At(4, 0), 1000, kind);
            Assert.AreEqual(53, sim.Entities.AttackCooldown[id]);
        }

        // ---- живой замах ----

        [Test]
        public void Swing_LandsOnTickTwentyThree_StandsSeventeen_RepeatsEveryFiftyThree()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);

            Assert.IsTrue(sim.TryGetEnemySwing(g, out var swing));
            Assert.AreEqual(0, swing.StartTick);
            Assert.AreEqual(23, swing.ImpactTick);
            Assert.AreEqual(40, swing.RecoverUntil);
            Assert.AreEqual(53, sim.Entities.NextAttackTick[g]);
            // AttackImpactTick врага читает TickDriver.Stonehoof (уворот в съёмке).
            Assert.AreEqual(swing.ImpactTick, sim.Entities.AttackImpactTick[g],
                "контакт в сущности совпадает с замахом");
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var mark));
            Assert.AreEqual(23, mark.ImpactTick, "метка гаснет в контакт, а не по старому окну");

            // Три цикла подряд: контакт на 23, 76 и 129 — ни тиком раньше.
            var hits = Until(sim, 3 * 53, g);
            CollectionAssert.AreEqual(new[] { 23, 76, 129 }, hits);
        }

        [Test]
        public void AfterContact_StandsSeventeenTicks_ThenWalksAgain()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0), stationary: false);
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            Until(sim, 24, g);
            // Герой ушёл: моб рвался бы за ним, но окно наказания держит тело.
            sim.Entities.Position[Simulation.PlayerId] = At(-5, 0);
            while (sim.Tick < 40)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(swing.Origin, sim.Entities.Position[g], "стойка, тик " + sim.Tick);
            }
            sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(swing.Origin, sim.Entities.Position[g], "после 17 тиков моб снова идёт");
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
