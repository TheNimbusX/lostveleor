using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Усиления способностей без порядка (владелец, 24 сентября): «уровня таланта» больше нет,
    /// любое из усилений может выпасть первым, карточка берёт ровно то, что на ней написано.
    /// </summary>
    public class UpgradeOrderTests
    {
        private static RiftRun NewRun(ulong seed)
        {
            var run = new RiftRun(new Simulation(seed, 1024), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            return run;
        }

        private static void ReachReward(RiftRun run)
        {
            for (int i = 0; i < run.Sim.Entities.Count; i++)
                if (run.Sim.Entities.Side[i] != Faction.Wole) run.Sim.Entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);
        }

        private static InputFrame Choice(int card)
            => new InputFrame { Command = (byte)((int)RunCommand.ChooseReward1 + card) };

        [Test]
        public void AnyUpgradeCanBeTakenFirst()
        {
            var loadout = new RunLoadout();
            Assert.IsTrue(loadout.TakeTalent(0, 3), "четвёртое усиление первым");
            Assert.IsTrue(loadout.HasTalent(0, 3));
            Assert.IsFalse(loadout.HasTalent(0, 0), "взялось лишнее");
            Assert.AreEqual(1, loadout.TalentCount(0));
            Assert.IsFalse(loadout.TakeTalent(0, 3), "одно усиление взято дважды");
            Assert.IsTrue(loadout.TakeTalent(0, 1));
            Assert.AreEqual(2, loadout.TalentCount(0));
        }

        [Test]
        public void NodesFollowTheTakenSetNotAPrefix()
        {
            var loadout = new RunLoadout();
            loadout.TakeTalent(0, 2);
            var nodes = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = loadout.AppendTalentNodes(0, nodes, 0);

            var expected = new AbilityNode[SabreTalents.TalentsPerLine];
            SabreTalents.TryLineOf(0, out SabreTalentLine line);
            int expectedCount = SabreTalents.AppendNode(line, 2, expected, 0);

            Assert.AreEqual(expectedCount, count);
            for (int i = 0; i < count; i++) Assert.AreEqual(expected[i].Id, nodes[i].Id);
        }

        [Test]
        public void TalentCardsOfferUpgradesOutOfOrder()
        {
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 400 && seen.Count < 3; seed++)
            {
                var run = NewRun(seed);
                ReachReward(run);
                for (int c = 0; c < RiftRun.RewardChoices; c++)
                {
                    RewardOffer offer = run.GetOffer(c);
                    if (offer.Kind == RewardKind.Talent) seen.Add(offer.TalentIndex);
                }
            }
            Assert.GreaterOrEqual(seen.Count, 3, "карточки усилений по-прежнему идут строго по порядку");
        }

        [Test]
        public void ChoosingATalentCardTakesExactlyThatUpgrade()
        {
            for (ulong seed = 1; seed <= 400; seed++)
            {
                var run = NewRun(seed);
                ReachReward(run);
                for (int c = 0; c < RiftRun.RewardChoices; c++)
                {
                    RewardOffer offer = run.GetOffer(c);
                    if (offer.Kind != RewardKind.Talent || offer.TalentIndex == 0) continue;
                    run.Step(Choice(c));
                    Assert.IsTrue(run.Loadout.HasTalent(offer.PoolIndex, offer.TalentIndex), "взято не то усиление");
                    Assert.AreEqual(1, run.Loadout.TalentCount(offer.PoolIndex), "взялось больше одного");
                    return;
                }
            }
            Assert.Fail("за 400 сидов не выпало карточки усиления не первым номером");
        }

        [Test]
        public void TakenUpgradesAreNeverOfferedAgain()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var run = NewRun(seed);
                for (int index = 0; index < SabreTalents.TalentsPerLine - 1; index++)
                    run.Loadout.TakeTalent(PelagKit.StarterPoolIndex, index);
                ReachReward(run);
                for (int c = 0; c < RiftRun.RewardChoices; c++)
                {
                    RewardOffer offer = run.GetOffer(c);
                    if (offer.Kind == RewardKind.Talent)
                        Assert.AreEqual(SabreTalents.TalentsPerLine - 1, offer.TalentIndex, "предложено уже взятое усиление");
                }
            }
        }
    }
}
