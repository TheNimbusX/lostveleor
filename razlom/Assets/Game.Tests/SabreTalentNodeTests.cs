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
            sim.SetAbility(0, PelagKit.Definition(CombatBranch.Sabre, SabreTalents.SlotOf(line)), buffer, count);
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
        public void WhirlwindMoreOftenCutsAQuarterOfTheCooldown()
        {
            Assert.AreEqual(72, Build(SabreTalentLine.Whirlwind, 1).CooldownTicks, "второй талант ещё не взят");
            Assert.AreEqual(54, Build(SabreTalentLine.Whirlwind, 2).CooldownTicks);
        }

        [Test]
        public void CleaveLongBladeReachesHalfFarther()
        {
            Assert.AreEqual(1.5f, Stat(Build(SabreTalentLine.Cleave, 1), AbilityStatType.Radius), 0.001f);
            Assert.AreEqual(2.25f, Stat(Build(SabreTalentLine.Cleave, 2), AbilityStatType.Radius), 0.001f);
        }

        [Test]
        public void BlazeBurnsFiveSecondsInsteadOfThree()
        {
            Assert.AreEqual(3 * Simulation.TicksPerSecond,
                Build(SabreTalentLine.Blaze, 1).Get(AbilityStatType.DurationTicks).ToInt());
            Assert.AreEqual(5 * Simulation.TicksPerSecond,
                Build(SabreTalentLine.Blaze, 2).Get(AbilityStatType.DurationTicks).ToInt());
        }

        [Test]
        public void SquallCheaperCostsTwentyEight()
        {
            Assert.AreEqual(40, Simulation.LavidiumCostOf(Build(SabreTalentLine.Squall, 1)));
            Assert.AreEqual(28, Simulation.LavidiumCostOf(Build(SabreTalentLine.Squall, 2)));
        }

        [Test]
        public void NoTalentsMeansNoNodes()
        {
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            for (int line = 0; line < SabreTalents.LineCount; line++)
                Assert.AreEqual(0, SabreTalents.AppendNodes((SabreTalentLine)line, 0, buffer, 0));
        }

        /// <summary>Ранг выше пяти не даёт узлов сверх ветки и не выходит за буфер.</summary>
        [Test]
        public void RankAboveFiveIsClamped()
        {
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            int full = SabreTalents.AppendNodes(SabreTalentLine.Whirlwind, SabreTalents.TalentsPerLine, buffer, 0);
            Assert.AreEqual(full, SabreTalents.AppendNodes(SabreTalentLine.Whirlwind, 99, buffer, 0));
        }
    }
}
