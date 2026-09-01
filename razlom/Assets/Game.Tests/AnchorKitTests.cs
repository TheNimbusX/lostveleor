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

            sim.Step(Cast(0, aim));
            for (int i = 0; i < AnchorKit.LeapTicks + 2; i++) sim.Step(InputFrame.Empty);

            Fix64 travelled = (sim.Entities.Position[Simulation.PlayerId] - start).Length;
            Assert.Greater(travelled.ToFloat(), 4.0f,
                "рывок обязан донести игрока почти до точки прицела");
        }

        /// <summary>
        /// Клик дальше длины цепи не отменяет бросок, а укорачивает его.
        /// Отменять было бы честнее формально и хуже на практике: игрок целится
        /// примерно, и молчаливый отказ читается как «кнопка не сработала».
        /// </summary>
        [Test]
        public void AnchorLeap_ClampsToChainReachInsteadOfRefusing()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);

            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];
            FixVec2 farAway = start + new FixVec2(Fix64.FromInt(40), Fix64.Zero);

            sim.Step(Cast(0, farAway));
            for (int i = 0; i < AnchorKit.LeapTicks + 2; i++) sim.Step(InputFrame.Empty);

            Fix64 travelled = (sim.Entities.Position[Simulation.PlayerId] - start).Length;
            Assert.Greater(travelled.ToFloat(), 1f, "бросок обязан состояться");
            Assert.LessOrEqual(travelled.ToFloat(), AnchorKit.LeapRange.ToFloat() + 0.05f,
                "и не унести дальше длины цепи");
        }

        // ------------------------------------------------ Подсечка

        [Test]
        public void AnchorSweep_DragsAnOrdinaryEnemyTowardThePlayer()
        {
            Simulation sim = Arena(out int enemy,
                new FixVec2(Fix64.FromInt(5), Fix64.Zero), Fix64.One);
            sim.SetAbility(1, AbilityDefinition.AnchorSweep(), new AbilityNode[0], 0);

            Fix64 before = Distance(sim, Simulation.PlayerId, enemy);
            sim.Step(Cast(1, FixVec2.Zero));
            for (int i = 0; i < AnchorKit.SweepTicks + 2; i++) sim.Step(InputFrame.Empty);
            Fix64 after = Distance(sim, Simulation.PlayerId, enemy);

            Assert.Less(after.ToFloat(), before.ToFloat() - 2f,
                "обычного врага подсечка обязана заметно подтащить");
        }

        /// <summary>
        /// «Обычных тянет, тяжёлых нет» — это лист способностей, а не пожелание.
        /// Сопротивление берётся из того же PushWeight, что и расталкивание:
        /// враг, которого не сдвинуть плечом, не сдвигается и цепью.
        /// </summary>
        [Test]
        public void AnchorSweep_LeavesHeavyEnemiesWhereTheyStand()
        {
            Fix64 heavy = ForcedMotion.ResistThreshold + Fix64.One;
            Simulation sim = Arena(out int enemy,
                new FixVec2(Fix64.FromInt(5), Fix64.Zero), heavy);
            sim.SetAbility(1, AbilityDefinition.AnchorSweep(), new AbilityNode[0], 0);

            FixVec2 before = sim.Entities.Position[enemy];
            sim.Step(Cast(1, FixVec2.Zero));
            for (int i = 0; i < AnchorKit.SweepTicks + 2; i++) sim.Step(InputFrame.Empty);

            Fix64 moved = (sim.Entities.Position[enemy] - before).Length;
            Assert.Less(moved.ToFloat(), 0.6f,
                "тяжёлого цепь стронуть не должна: допускается только расталкивание");
        }

        /// <summary>
        /// Волочимый враг не идёт своим ходом. Иначе тяга и собственный шаг
        /// складывались бы, и подсечка приводила бы толпу вдвое быстрее, чем
        /// написано на листе.
        /// </summary>
        [Test]
        public void DraggedEnemy_DoesNotWalkOnItsOwnWhileBeingPulled()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            int enemy = sim.Entities.Spawn(
                new FixVec2(Fix64.FromInt(5), Fix64.Zero), 9000, Faction.Orvill);
            sim.Entities.NextAttackTick[enemy] = int.MaxValue;
            sim.SetAbility(1, AbilityDefinition.AnchorSweep(), new AbilityNode[0], 0);

            sim.Step(Cast(1, FixVec2.Zero));

            Assert.Greater(sim.Entities.ForcedTicksLeft[enemy], 0, "враг должен быть в тяге");
            Assert.AreEqual(0, sim.Entities.Velocity[enemy].LengthSq.Raw,
                "у волочимого тела собственной скорости быть не может");
        }

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
            sim.Step(Cast(3, FixVec2.Zero));
            for (int i = 0; i < AnchorKit.ChainTicksPerHop * AnchorKit.ChainMaxHops + 8; i++)
                sim.Step(InputFrame.Empty);

            int hurt = 0;
            for (int i = 0; i < mobs.Length; i++)
                if (sim.Entities.Health[mobs[i]] < 9000) hurt++;

            Assert.GreaterOrEqual(hurt, 2,
                "цепочка обязана задеть больше одной цели: в этом весь смысл");
            Assert.Greater((sim.Entities.Position[Simulation.PlayerId] - start).Length.ToFloat(),
                0.5f, "и переставить игрока внутрь пачки");
        }

        /// <summary>
        /// Способность рядом с пустотой обязана кончаться тихо, а не тащить
        /// игрока в никуда и не тратить кулдаун впустую посреди боя.
        /// </summary>
        [Test]
        public void ChainStep_WithNobodyAroundDoesNotMoveThePlayer()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            sim.SetAbility(3, AbilityDefinition.ChainStep(), new AbilityNode[0], 0);

            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];
            sim.Step(Cast(3, FixVec2.Zero));
            for (int i = 0; i < 20; i++) sim.Step(InputFrame.Empty);

            Assert.AreEqual(0f,
                (sim.Entities.Position[Simulation.PlayerId] - start).Length.ToFloat(), 0.001f,
                "без целей шаг по цепи никуда не ведёт");
        }

        // ------------------------------------------------ детерминизм

        /// <summary>
        /// Принудительное перемещение входит в состояние тела и в хеш. Два
        /// прогона одного сида обязаны совпасть побитово — иначе реплей
        /// разъедется на первом же крюке.
        /// </summary>
        [Test]
        public void ForcedMotion_StaysBitExactAcrossRuns()
        {
            ulong Run()
            {
                var sim = new Simulation(Seed, 64);
                sim.SetupTestArena(12);
                sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);
                sim.SetAbility(1, AbilityDefinition.AnchorSweep(), new AbilityNode[0], 0);
                sim.SetAbility(3, AbilityDefinition.ChainStep(), new AbilityNode[0], 0);

                for (int tick = 0; tick < 240; tick++)
                {
                    InputFrame f = InputFrame.Empty;
                    if (tick == 10) f = Cast(0, new FixVec2(Fix64.FromInt(4), Fix64.One));
                    else if (tick == 60) f = Cast(1, FixVec2.Zero);
                    else if (tick == 120) f = Cast(3, FixVec2.Zero);
                    sim.Step(in f);
                }
                return sim.StateHash();
            }

            Assert.AreEqual(Run(), Run(), "кит обязан быть детерминированным");
        }
    }
}
