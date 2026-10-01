using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Сундук зачистки (владелец, 1 октября): после зачистки арены перед выходом лежит вещь,
    /// редкость 50/30/15/5 — обычная, редкая, эпическая, уникальная.
    /// </summary>
    public sealed class ArenaChestTests
    {
        private static GameSession ClearedSession(ulong seed)
        {
            var session = new GameSession(seed, new Camp(PrototypeContent.Items()), PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            session.EnterRift();
            Assert.That(session.Run.ChestDrop, Is.EqualTo(-1), "До зачистки сундука нет");
            for (int i = 1; i < session.Run.Sim.Entities.Count; i++) session.Run.Sim.Entities.Alive[i] = false;
            session.Step(InputFrame.Empty);
            Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            return session;
        }

        [Test]
        public void ClearingTheArenaPutsAnItemChestBeforeTheExit()
        {
            var run = ClearedSession(81).Run;
            Assert.That(run.ChestDrop, Is.GreaterThanOrEqualTo(0));
            RunDrop chest = run.GetDrop(run.ChestDrop);
            Assert.That(chest.Claimed, Is.False);
            Assert.That(chest.Offer.Kind, Is.EqualTo(RewardKind.Item));
            Assert.That(chest.Offer.Item.IsEmpty, Is.False);
            var map = run.Map;
            Fix64 nearest = Fix64.FromInt(1000);
            for (int i = 0; i < map.ExitCount; i++)
            {
                Fix64 distance = FixVec2.Distance(chest.Position, map.ExitPoint(i));
                if (distance < nearest) nearest = distance;
            }
            Assert.That(nearest.ToFloat(), Is.InRange(2.9f, 3.1f), "Сундук в трёх метрах от выхода, ближе к поляне");
        }

        [Test]
        public void HeroPicksTheChestUpByWalkingToIt()
        {
            var session = ClearedSession(82);
            var run = session.Run;
            RunDrop chest = run.GetDrop(run.ChestDrop);
            run.Sim.Entities.Position[Simulation.PlayerId] = chest.Position;
            session.Step(InputFrame.Empty);
            Assert.That(run.GetDrop(run.ChestDrop).Claimed, Is.True);
        }

        [Test]
        public void ChestRarityFollowsFiftyThirtyFifteenFive()
        {
            const int runs = 400;
            var counts = new int[4];
            for (int s = 0; s < runs; s++)
            {
                var run = ClearedSession(1000 + (ulong)s).Run;
                counts[(int)run.GetDrop(run.ChestDrop).Offer.Item.Rarity]++;
            }
            for (int r = 0; r < counts.Length; r++)
            {
                float share = counts[r] * 100f / runs;
                Assert.That(share, Is.EqualTo(RiftRun.ChestRarityPercent[r]).Within(6f),
                    $"Редкость {(ItemRarity)r}: {counts[r]} из {runs}");
            }
            Assert.That(counts[(int)ItemRarity.Unique], Is.GreaterThan(0), "Уникальная выпадает");
        }
    }
}
