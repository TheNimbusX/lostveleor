using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AnchorThrowTests;

namespace Game.Tests
{
    /// <summary>Бросок якоря: полукольцо (чистая функция), пул и таблица форм, хеш и превью (спека §2.9, 7, 9, 10).</summary>
    public sealed class AnchorThrowRingTests
    {
        // ---------- 7. полукольцо ----------

        private static double Key(FixVec2 dir, FixVec2 v) => System.Math.Atan2(
            (dir.X * v.Y - dir.Y * v.X).ToDouble(), FixVec2.Dot(dir, v).ToDouble());

        [Test]
        public void Ring_OrderByAngle_NoOverlap_ArcAtMost288_RadiusCappedForEightGuardians()
        {
            var rng = new Pcg32(20261003UL, 7UL);
            FixVec2 hero = At(1.5, -2);
            FixVec2 dir = At(0.6, 0.8).Normalized();
            var side = new FixVec2(-dir.Y, dir.X);
            Fix64 heroRadius = Fix64.Ratio(45, 100);
            Fix64[] radiiSet = { Fix64.Ratio(42, 100), Fix64.Ratio(45, 100), Fix64.Ratio(65, 100), Fix64.Ratio(85, 100), Fix64.Ratio(95, 100) };
            for (int n = 1; n <= 20; n++)
            {
                for (int trial = 0; trial < 6; trial++)
                {
                    bool guardians = trial == 0;
                    var positions = new FixVec2[n];
                    var radii = new Fix64[n];
                    var ids = new int[n];
                    var spots = new FixVec2[n];
                    for (int k = 0; k < n; k++)
                    {
                        // Вдоль от −0,4 (чуть позади начала полосы) до 7 м, поперёк до ±2,3 (сеть Невода).
                        Fix64 along = rng.NextFix(Fix64.Ratio(-4, 10), Fix64.FromInt(7));
                        Fix64 across = rng.NextFix(Fix64.Ratio(-23, 10), Fix64.Ratio(23, 10));
                        positions[k] = hero + dir * along + side * across;
                        radii[k] = guardians ? Fix64.Ratio(85, 100) : radiiSet[rng.NextInt(0, radiiSet.Length)];
                        ids[k] = 1 + (k * 7) % 23;
                    }
                    if (trial == 1 && n > 1) positions[1] = positions[0];      // ничья по углу — по id
                    Fix64 rho = Simulation.AnchorThrowRing(hero, dir, heroRadius, positions, radii, ids, n, spots);
                    string at = "n " + n + ", проба " + trial;

                    for (int k = 1; k < n; k++)
                    {
                        double a = Key(dir, positions[k - 1] - hero), b = Key(dir, positions[k] - hero);
                        // Atan2 Sim — приближение (±0,005 рад): порядок сверяется с допуском, ничья — по id.
                        Assert.LessOrEqual(a, b + 0.012, at + ": порядок по углу");
                        if (positions[k - 1].Equals(positions[k])) Assert.Less(ids[k - 1], ids[k], at + ": ничья — меньший id");
                        Assert.Greater(Key(dir, spots[k] - hero), Key(dir, spots[k - 1] - hero), at + ": места в том же порядке");
                    }
                    for (int k = 0; k < n; k++)
                    {
                        Assert.AreEqual(rho.ToDouble(), Dist(spots[k], hero), 2e-3, at + ": на кольце");
                        for (int m = k + 1; m < n; m++)
                            Assert.GreaterOrEqual(Dist(spots[k], spots[m]), (radii[k] + radii[m]).ToDouble() - 2e-3, at + ": без наложений " + k + "/" + m);
                    }
                    double arc = Key(dir, spots[n - 1] - hero) - Key(dir, spots[0] - hero);
                    if (n > 1) Assert.LessOrEqual(System.Math.Abs(arc) * 180 / System.Math.PI, 288.5, at + ": дуга");
                    for (int k = 0; k < n; k++)
                        Assert.GreaterOrEqual(Dist(spots[k], hero) - radii[k].ToDouble() - heroRadius.ToDouble(), 0.3 - 2e-3, at + ": до героя");
                    if (guardians && n <= 8) Assert.LessOrEqual(rho.ToDouble(), 2.6 + 1e-6, at + ": ρ ≤ 2,6");
                }
            }
        }

        // ---------- 10. пул и таблица форм ----------

        [Test]
        public void Pool_Index10_OutOfRewards_NoTalentLine_FormsFourteenToSixteen()
        {
            Assert.AreEqual(11, PelagKit.PoolSize);
            Assert.AreEqual(10, PelagKit.PoolIndexOf(AbilityDefinition.AnchorThrowId));
            Assert.AreEqual(AbilityDefinition.AnchorThrowId, PelagKit.PoolDefinition(10).Id);
            Assert.IsFalse(PelagKit.InRewardPool(10), "в награды — по слову владельца");
            Assert.IsFalse(SabreTalents.TryLineOf(10, out _), "таланты — вторая очередь");
            Assert.AreEqual(0, RunLoadout.LineTalentCount(10));
            Assert.AreEqual(16, PelagForms.Count);
            Assert.AreEqual(14, (int)PelagForm.AnchorThrowNet);
            Assert.AreEqual(15, (int)PelagForm.AnchorThrowFan);
            Assert.AreEqual(16, (int)PelagForm.AnchorThrowHarpoon);
            foreach (PelagForm form in new[] { PelagForm.AnchorThrowNet, PelagForm.AnchorThrowFan, PelagForm.AnchorThrowHarpoon })
            {
                Assert.AreEqual(10, PelagForms.LineOf(form), form.ToString());
                Assert.IsTrue(PelagForms.IsReady(form), form + ": механика написана");
            }
            Assert.AreEqual("form.anchor_throw.net", PelagForms.KeyOf(PelagForm.AnchorThrowNet));
            Assert.AreEqual(3, PelagForms.FormCount(10, readyOnly: true));
            Assert.AreEqual(11, (int)ForcedMotionKind.Reeled);
            Assert.AreEqual(72, (int)SimEventType.AnchorThrowRelease);
            Assert.AreEqual(76, (int)SimEventType.AnchorThrowEnded);
        }

        [Test]
        public void LoadoutHash_PoolIndexTenMixedOnlyWhenNonZero()
        {
            var loadout = new RunLoadout();
            ulong hash = Hashing.Offset, expected = Hashing.Offset;
            loadout.HashInto(ref hash);
            // Прежнее правило: слоты и ровно десять масок талантов — набор без Броска хешируется как до него.
            for (int i = 0; i < RunLoadout.Slots; i++) Hashing.Mix(ref expected, loadout.PoolIndexAt(i));
            for (int i = 0; i < 10; i++) Hashing.Mix(ref expected, loadout.TalentMask(i));
            Assert.AreEqual(expected, hash);

            Assert.IsTrue(loadout.Add(10));
            Assert.IsTrue(loadout.DebugSetForm(10, PelagForm.AnchorThrowNet));
            ulong withThrow = Hashing.Offset;
            loadout.HashInto(ref withThrow);
            Assert.AreNotEqual(hash, withThrow);
        }

        // ---------- 9. хеш и детерминизм; превью ----------

        private static List<ulong> Scenario(PelagForm form)
        {
            Simulation sim = Arena(form, Map(7.2, 1.5, 0.5));
            Enemy(sim, 2000, 300); Enemy(sim, 4000, -900); Enemy(sim, 6000, 400);
            Mob(sim, EnemyKind.ForestRootSwarm, 3.2, 1.6);
            int elite = Enemy(sim, 5200, 1900);
            sim.MarkElite(elite);
            var hashes = new List<ulong>();
            foreach (Frame f in Cast(sim, 7, 1, 34)) hashes.Add(f.Hash);
            return hashes;
        }

        [Test]
        public void Hash_TwoIdenticalRunsMatch_EveryForm()
        {
            foreach (PelagForm form in new[] { PelagForm.None, PelagForm.AnchorThrowNet, PelagForm.AnchorThrowFan, PelagForm.AnchorThrowHarpoon })
                CollectionAssert.AreEqual(Scenario(form), Scenario(form), form.ToString());
        }

        [Test]
        public void Preview_IsPure_AndMatchesTheCast()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowFan, Map(6, 0, 0.5));
            ulong before = sim.StateHash();
            Assert.IsTrue(sim.AnchorThrowPreview(Slot, At(7, 0), out AnchorThrowState plan));
            Assert.AreEqual(before, sim.StateHash(), "превью ничего не пишет");
            Assert.AreEqual(3, plan.Lanes);
            Assert.AreEqual(5.0, plan.Reach0.ToDouble(), 1e-6, "стена режет и превью");
            Assert.AreEqual(0.45, plan.HalfWidth.ToDouble(), 1e-6);
            Assert.IsFalse(sim.AnchorThrowPreview(1, At(7, 0), out _), "в слоте не Бросок");
            Cast(sim, 7, 0, 4);
            Assert.AreEqual(plan.Reach0, sim.AnchorThrow.Reach0);
            Assert.AreEqual(plan.Reach1, sim.AnchorThrow.Reach1);
            Assert.AreEqual(plan.Dir2, sim.AnchorThrow.Dir2);
        }
    }
}
