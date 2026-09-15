using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Кит Пелага на якоре и цепи. Проверяется главное свойство, ради которого
    /// он и писался: способности ДВИГАЮТ ТЕЛА, а не только считают урон.
    /// </summary>
    public class AnchorKitTests
    {
        private const ulong Seed = 0xA9C40BEEUL;

        private static Simulation Arena(out int enemy, FixVec2 at, Fix64 weight)
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            enemy = sim.Entities.Spawn(at, 9000, Faction.Orvill);
            sim.Entities.PushWeight[enemy] = weight;
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[enemy].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(enemy);
            sim.Entities.NextAttackTick[enemy] = int.MaxValue;
            return sim;
        }

        private static InputFrame Cast(int slot, FixVec2 aim)
        {
            var f = InputFrame.Empty;
            f.AbilityMask = (byte)(1 << slot);
            f.Aim = aim;
            return f;
        }

        private static Fix64 Distance(Simulation sim, int a, int b)
            => (sim.Entities.Position[a] - sim.Entities.Position[b]).Length;

        // ------------------------------------------------ Бросок якоря

        [Test]
        public void AnchorLeap_MovesThePlayerTowardTheAimedPoint()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);

            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];
            FixVec2 aim = start + new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(-Fix64.One, Fix64.Zero);

            sim.Step(Cast(0, aim));
            for (int i = 0; i < AnchorKit.LeapWindupTicks + AnchorKit.LeapTicks + 2; i++) sim.Step(InputFrame.Empty);

            Fix64 travelled = (sim.Entities.Position[Simulation.PlayerId] - start).Length;
            Assert.Greater(travelled.ToFloat(), 4.0f,
                "рывок обязан донести игрока почти до точки прицела");
            Assert.Greater(sim.Entities.Facing[Simulation.PlayerId].X.ToFloat(), 0.99f,
                "после посадки герой смотрит по направлению полёта");
        }

        // ------------------------------------------------ Подсечка









        /// <summary>
        /// «Обычных тянет, тяжёлых нет» — это лист способностей, а не пожелание.
        /// Сопротивление берётся из того же PushWeight, что и расталкивание:
        /// враг, которого не сдвинуть плечом, не сдвигается и цепью.
        /// </summary>


        // ------------------------------------------------ Шаг по цепи

        [Test]
        public void ChainStep_HopsBetweenTargetsAndHurtsThemOnTheWay()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            sim.SetAbility(3, AbilityDefinition.ChainStep(), new AbilityNode[0], 0);

            int[] mobs = new int[3];
            for (int i = 0; i < mobs.Length; i++)
            {
                mobs[i] = sim.Entities.Spawn(
                    new FixVec2(Fix64.FromInt(2 + i), Fix64.FromInt(i)), 9000, Faction.Orvill);
                sim.Entities.Stats[mobs[i]].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.RefreshStats(mobs[i]);
                sim.Entities.NextAttackTick[mobs[i]] = int.MaxValue;
            }

            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];
            var selected = Cast(3, FixVec2.Zero);
            selected.AbilityTarget = 1;
            sim.Step(selected);
            for (int i = 0; i < AnchorKit.ChainTicksPerHop * AnchorKit.ChainMaxHops + 8; i++)
                sim.Step(InputFrame.Empty);

            int hurt = 0;
            for (int i = 0; i < mobs.Length; i++)
                if (sim.Entities.Health[mobs[i]] < 9000) hurt++;

            Assert.AreEqual(3, hurt,
                "цепочка обязана задеть больше одной цели: в этом весь смысл");
            Assert.Greater((sim.Entities.Position[Simulation.PlayerId] - start).Length.ToFloat(),
                0.5f, "и переставить игрока внутрь пачки");
        }

        // ------------------------------------------------ детерминизм

    }
}
