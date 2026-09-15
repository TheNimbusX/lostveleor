using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Числовые таланты сабельной ветки: взятый талант меняет число своей
    /// способности, а не взятый — не меняет ничего.
    /// </summary>
    public class SabreTalentNodeTests
    {
        private static AbilityBuild Build(SabreTalentLine line, int rank)
        {
            var sim = new Simulation(1234, 64);
            sim.SetupTestArena(0);
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = SabreTalents.AppendNodes(line, rank, buffer, 0);
            sim.SetAbility(0, PelagKit.PoolDefinition(SabreTalents.PoolIndexOf(line)), buffer, count);
            return sim.GetAbility(0);
        }

        private static float Stat(AbilityBuild build, AbilityStatType stat) => build.Get(stat).ToFloat();

        [Test]
        public void WhirlwindWiderCircleAddsAQuarterRadius()
        {
            Assert.AreEqual(2.3f, Stat(Build(SabreTalentLine.Whirlwind, 0), AbilityStatType.Radius), 0.001f);
            Assert.AreEqual(2.875f, Stat(Build(SabreTalentLine.Whirlwind, 1), AbilityStatType.Radius), 0.001f);
        }

        [Test]
        public void SquallCheaperCostsTwentyEight()
        {
            Assert.AreEqual(40, Simulation.LavidiumCostOf(Build(SabreTalentLine.Squall, 1)));
            Assert.AreEqual(28, Simulation.LavidiumCostOf(Build(SabreTalentLine.Squall, 2)));
        }

        /// <summary>
        /// Одиночный узел меню разработчика — тот же, что стоит на его месте
        /// в ранговой цепочке: отладка визуала обязана включать ровно игровой талант.
        /// </summary>
        [Test]
        public void SingleTalentNodeMatchesItsPlaceInTheLine()
        {
            var ranked = new AbilityNode[SabreTalents.TalentsPerLine];
            var single = new AbilityNode[1];
            for (int line = 0; line < SabreTalents.LineCount; line++)
            {
                var id = (SabreTalentLine)line;
                Assert.AreEqual(SabreTalents.TalentsPerLine,
                    SabreTalents.AppendNodes(id, SabreTalents.TalentsPerLine, ranked, 0));
                for (int index = 0; index < SabreTalents.TalentsPerLine; index++)
                {
                    Assert.AreEqual(1, SabreTalents.AppendNode(id, index, single, 0), $"{id} {index + 1}");
                    Assert.AreEqual(ranked[index].Id, single[0].Id, $"{id} {index + 1}");
                }
            }
            Assert.AreEqual(0, SabreTalents.AppendNode(SabreTalentLine.Whirlwind, SabreTalents.TalentsPerLine, single, 0));
        }

    }
}
