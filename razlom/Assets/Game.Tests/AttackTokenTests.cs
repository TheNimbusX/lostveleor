using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Жетоны атак: «разносить крупные телеграфы по времени».
    ///
    /// Приёмка: не больше двух замахов Хранителя разом, лишний кружит, а не
    /// стоит; Корнеполз жетонов не берёт; крупная атака (залп, таран, прыжок)
    /// одна на аренах 1–4 и две с пятой; раздача по возрастанию индекса.
    /// Новые мобы леса: Расщепень берёт ближний жетон, его детёныш — жетон
    /// укуса; Шипомёт и Корнехват общим замахом не бьют вовсе.
    /// </summary>
    public class AttackTokenTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena()
        {
            var sim = new Simulation(777UL, 32);
            sim.SetupTestArena(0);
            sim.PlayerInvulnerable = true;
            return sim;
        }

        private static int Enemy(Simulation sim, FixVec2 at, EnemyKind kind = EnemyKind.ForestGuardian,
            bool stationary = true)
        {
            int id = sim.SpawnEnemy(at, 1000, kind);
            if (stationary) sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            return id;
        }

        private static bool InWindup(Simulation sim, int id)
            => sim.TryGetEnemySwing(id, out var swing) && !swing.HitResolved;

        [Test]
        public void ThirdGuardian_WaitsForAToken_AndLowerIndicesGoFirst()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-1, 1.732)), c = Enemy(sim, At(-1, -1.732));
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(InWindup(sim, a));
            Assert.IsTrue(InWindup(sim, b));
            Assert.IsFalse(sim.TryGetEnemySwing(c, out _), "третий замах поверх двух");

            while (sim.Tick < 22) sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(c, out var third));
            Assert.AreEqual(21, third.StartTick, "жетон переходит в тот же тик, когда первый ударил");

            for (int t = 0; t < 300; t++)
            {
                sim.Step(InputFrame.Empty);
                int windups = (InWindup(sim, a) ? 1 : 0) + (InWindup(sim, b) ? 1 : 0) + (InWindup(sim, c) ? 1 : 0);
                Assert.LessOrEqual(windups, Simulation.MeleeAttackTokenLimit, "тик " + sim.Tick);
            }
        }

        [Test]
        public void GuardianWithoutAToken_CirclesInsteadOfStanding()
        {
            var sim = Arena();
            Enemy(sim, At(2, 0)); Enemy(sim, At(-2, 0));
            int waiting = Enemy(sim, At(0, 2), stationary: false);
            FixVec2 start = sim.Entities.Position[waiting];
            for (int t = 0; t < 10; t++) sim.Step(InputFrame.Empty);

            Assert.IsFalse(sim.TryGetEnemySwing(waiting, out _));
            Assert.AreNotEqual(start, sim.Entities.Position[waiting], "без жетона стоит столбом");
            double distance = sim.Entities.Position[waiting].Length.ToDouble();
            Assert.That(distance, Is.InRange(1.5, 3.2), "кружит на дистанции удара");
        }

        [Test]
        public void RootSwarm_DoesNotTakeMeleeTokens()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-2, 0));
            int s1 = Enemy(sim, At(0, 1.2), EnemyKind.ForestRootSwarm);
            int s2 = Enemy(sim, At(0, -1.2), EnemyKind.ForestRootSwarm);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(InWindup(sim, a));
            Assert.IsTrue(InWindup(sim, b));
            Assert.IsTrue(InWindup(sim, s1), "рой ждёт жетона Хранителей");
            Assert.IsTrue(InWindup(sim, s2));
        }

        private static Simulation TwoBuds(int limit)
        {
            var sim = new Simulation(123, 64);
            sim.SetupForestBudEncounter(null, 123, 2);
            sim.PlayerInvulnerable = true;
            sim.BigAttackTokenLimit = limit;
            for (int i = 1; i < sim.Entities.Count; i++)
            {
                sim.Entities.NextAttackTick[i] = 0;
                sim.Entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.RefreshStats(i);
            }
            return sim;
        }

        [Test]
        public void TwoBuds_VolleyOneAtATime_UntilTheLimitIsRaised()
        {
            var sim = TwoBuds(1);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetForestBudAttack(1, out var first));
            Assert.IsFalse(sim.TryGetForestBudAttack(2, out _), "второй залп поверх первого");
            while (sim.Tick <= first.EndTick) sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetForestBudAttack(2, out var second));
            Assert.AreEqual(first.EndTick, second.StartTick);

            var wide = TwoBuds(2);
            wide.Step(InputFrame.Empty);
            Assert.IsTrue(wide.TryGetForestBudAttack(1, out _));
            Assert.IsTrue(wide.TryGetForestBudAttack(2, out _), "с пятой арены залпов два");
        }

        [Test]
        public void StonehoofWaitsForTheBudVolley_AndNeverChargesDuringIt()
        {
            var sim = new Simulation(76, 64);
            sim.SetupStonehoofEncounter(null, 76);
            sim.PlayerInvulnerable = true;
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            int bud = Enemy(sim, At(0, 6), EnemyKind.ForestBud);

            int charged = -1;
            for (int t = 0; t < 240; t++)
            {
                sim.Step(InputFrame.Empty);
                bool volley = sim.TryGetForestBudAttack(bud, out _);
                bool charging = sim.TryGetStonehoofAction(1, out var charge) && sim.Tick - 1 <= charge.StopTick;
                Assert.IsFalse(volley && charging, "залп и таран разом, тик " + (sim.Tick - 1));
                if (charged < 0 && charging) charged = charge.StartTick;
            }
            Assert.AreEqual(sim.ForestBudConfig.ActionTicks, charged, "таран — сразу после залпа");
        }

        [Test]
        public void BigTokenLimit_FollowsArenaNumber_AndIsPartOfTheState()
        {
            Assert.AreEqual(1, Simulation.BigAttackTokensForArena(1));
            Assert.AreEqual(1, Simulation.BigAttackTokensForArena(4));
            Assert.AreEqual(2, Simulation.BigAttackTokensForArena(5));
            Assert.AreEqual(2, Simulation.BigAttackTokensForArena(8));

            var a = Arena(); var b = Arena();
            Assert.AreEqual(1, a.BigAttackTokenLimit);
            a.BigAttackTokenLimit = 0;
            Assert.AreEqual(1, a.BigAttackTokenLimit, "без крупных атак арены не бывает");
            Assert.AreEqual(a.StateHash(), b.StateHash());
            a.BigAttackTokenLimit = 2;
            Assert.AreNotEqual(a.StateHash(), b.StateHash());
            a.SetupTestArena(0);
            Assert.AreEqual(2, a.BigAttackTokenLimit, "лимит задаёт забег, расстановка его не сбрасывает");
        }

        [Test]
        public void FourthRootSwarm_WaitsForABiteToken_AndLowerIndicesGoFirst()
        {
            var sim = Arena();
            var bites = new int[5];
            for (int k = 0; k < bites.Length; k++)
            {
                double angle = k * 2 * System.Math.PI / bites.Length;
                bites[k] = Enemy(sim, At(1.2 * System.Math.Cos(angle), 1.2 * System.Math.Sin(angle)), EnemyKind.ForestRootSwarm);
            }
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(3, Simulation.SwarmBiteTokenLimit);
            for (int k = 0; k < bites.Length; k++)
                Assert.AreEqual(k < Simulation.SwarmBiteTokenLimit, InWindup(sim, bites[k]), "укус " + k + " в тик 0");

            // Первые три укуса упали — жетоны переходят к ждущим в тот же тик.
            while (sim.Tick <= Simulation.RootSwarmAttackWindupTicks) sim.Step(InputFrame.Empty);
            for (int k = Simulation.SwarmBiteTokenLimit; k < bites.Length; k++)
            {
                Assert.IsTrue(sim.TryGetEnemySwing(bites[k], out var late), "укус " + k);
                Assert.AreEqual(Simulation.RootSwarmAttackWindupTicks, late.StartTick, "укус " + k);
            }

            for (int t = 0; t < 300; t++)
            {
                sim.Step(InputFrame.Empty);
                int windups = 0;
                foreach (int id in bites) if (InWindup(sim, id)) windups++;
                Assert.LessOrEqual(windups, Simulation.SwarmBiteTokenLimit, "тик " + sim.Tick);
            }
        }

        [Test]
        public void BiteTokens_AndMeleeTokens_AreCountedSeparately()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-2, 0));
            int s1 = Enemy(sim, At(0, 1.2), EnemyKind.ForestRootSwarm);
            int s2 = Enemy(sim, At(0, -1.2), EnemyKind.ForestRootSwarm);
            int s3 = Enemy(sim, At(0.85, 0.85), EnemyKind.ForestRootSwarm);
            int c = Enemy(sim, At(-1.4, 1.4));
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(InWindup(sim, a));
            Assert.IsTrue(InWindup(sim, b));
            Assert.IsFalse(sim.TryGetEnemySwing(c, out _), "третий Хранитель ждёт: укусы жетон Хранителей не освобождают");
            Assert.IsTrue(InWindup(sim, s1));
            Assert.IsTrue(InWindup(sim, s2));
            Assert.IsTrue(InWindup(sim, s3), "два замаха Хранителей не отнимают жетонов укуса");
        }

        // ---------- новые мобы леса ----------

        [Test]
        public void SwingTable_OnlyMeleeKindsSwing_AndBitesAreSwarmLike()
        {
            var swing = new[] { EnemyKind.None, EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm,
                EnemyKind.ForestSplitter, EnemyKind.ForestSplitling };
            var own = new[] { EnemyKind.ForestBud, EnemyKind.ForestWendigo, EnemyKind.ForestStonehoof,
                EnemyKind.ForestThorncaster, EnemyKind.ForestRootSnarer };
            foreach (var kind in swing) Assert.IsTrue(Simulation.UsesEnemySwing(kind), kind.ToString());
            foreach (var kind in own) Assert.IsFalse(Simulation.UsesEnemySwing(kind), kind + " стал бы Хранителем");
            foreach (var kind in swing)
                Assert.AreEqual(kind == EnemyKind.ForestRootSwarm || kind == EnemyKind.ForestSplitling,
                    Simulation.IsSwarmLike(kind), kind.ToString());

            // Хранитель и моб без вида — ровно прежние числа.
            foreach (var kind in new[] { EnemyKind.None, EnemyKind.ForestGuardian })
            {
                var p = Simulation.MeleeProfileOf(kind);
                Assert.AreEqual(Simulation.EnemyAttackWindupTicks, p.WindupTicks);
                Assert.AreEqual(Simulation.GuardianSwingRecoveryTicks, p.RecoveryTicks);
                Assert.AreEqual(Simulation.GuardianSwingRadius, p.Radius);
                Assert.AreEqual(Simulation.GuardianSwingArcCos, p.ArcCos);
                Assert.AreEqual(Simulation.GuardianSwingRadius, p.StartRange);
            }
            var swarm = Simulation.MeleeProfileOf(EnemyKind.ForestRootSwarm);
            Assert.AreEqual(Simulation.RootSwarmAttackWindupTicks, swarm.WindupTicks);
            Assert.AreEqual(Simulation.RootSwarmRecoveryTicks, swarm.RecoveryTicks);
            Assert.AreEqual(Simulation.RootSwarmBiteArcCos, swarm.ArcCos);
        }

        [Test]
        public void Splitter_WaitsForAMeleeToken_AndSwingsItsOwnSector()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-2, 0));
            int splitter = Enemy(sim, At(0, 1.5), EnemyKind.ForestSplitter);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(InWindup(sim, a));
            Assert.IsTrue(InWindup(sim, b));
            Assert.IsFalse(sim.TryGetEnemySwing(splitter, out _), "Расщепень берёт ближний жетон, как третий Хранитель");

            while (sim.Tick < 22) sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(splitter, out var swing));
            Assert.AreEqual(21, swing.StartTick, "жетон переходит в тот же тик, когда Хранитель ударил");
            Assert.AreEqual(18, swing.ImpactTick - swing.StartTick);
            Assert.AreEqual(12, swing.RecoverUntil - swing.ImpactTick);
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var sector));
            Assert.AreEqual(swing.TelegraphSerial, sector.Serial);
            Assert.AreEqual(TelegraphShape.Sector, sector.Shape);
            Assert.IsTrue(sector.SharedView);
            Assert.AreEqual(Simulation.SplitterSwingRadius, sector.Radius);
            Assert.AreEqual(Simulation.SplitterSwingArcCos, sector.ArcCos);
            Assert.AreEqual(Fix64.Ratio(9, 5), Simulation.SplitterSwingRadius);
        }

        [Test]
        public void Splitling_BitesOnTheSwarmToken_WithoutAMark()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-2, 0));
            int s1 = Enemy(sim, At(0, 1.2), EnemyKind.ForestRootSwarm);
            int s2 = Enemy(sim, At(0, -1.2), EnemyKind.ForestRootSwarm);
            int k1 = Enemy(sim, At(0.85, 0.85), EnemyKind.ForestSplitling);
            int k2 = Enemy(sim, At(-0.85, -0.85), EnemyKind.ForestSplitling);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(InWindup(sim, a));
            Assert.IsTrue(InWindup(sim, b), "детёныш ближнего жетона не занимает");
            Assert.IsTrue(InWindup(sim, s1));
            Assert.IsTrue(InWindup(sim, s2));
            Assert.IsTrue(sim.TryGetEnemySwing(k1, out var bite), "третий жетон укуса — детёнышу");
            Assert.AreEqual(-1, bite.Telegraph, "укус детёныша меткой не рисуется");
            Assert.AreEqual(Simulation.SplitlingBiteWindupTicks, bite.ImpactTick - bite.StartTick);
            Assert.IsFalse(sim.TryGetEnemySwing(k2, out _), "четвёртый укус ждёт, как у роя");
        }

        [Test]
        public void KindsWithTheirOwnAttacks_NeverTakeTheGuardianSwing()
        {
            var sim = Arena();
            int thorn = Enemy(sim, At(1.6, 0), EnemyKind.ForestThorncaster, stationary: false);
            int snarer = Enemy(sim, At(-1.6, 0), EnemyKind.ForestRootSnarer, stationary: false);
            for (int t = 0; t < 90; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetEnemySwing(thorn, out _), "Шипомёт с сектором Хранителя, тик " + (sim.Tick - 1));
                Assert.IsFalse(sim.TryGetEnemySwing(snarer, out _), "Корнехват с сектором Хранителя, тик " + (sim.Tick - 1));
            }
        }
    }
}
