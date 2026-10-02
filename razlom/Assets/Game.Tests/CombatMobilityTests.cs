using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Граница между locomotion и боевыми действиями. Замах обязан оставаться
    /// читаемым, но приказ игрока и активная способность не должны превращать
    /// recovery в неуправляемую остановку.
    /// </summary>
    public class CombatMobilityTests
    {
        private const ulong Seed = 0xC017B1EUL;

        private static Simulation ArenaWithStationaryEnemy(
            FixVec2 enemyPosition, out int enemy)
        {
            var sim = new Simulation(Seed, 16);
            sim.SetupTestArena(0);
            enemy = sim.Entities.Spawn(enemyPosition, 5000, Faction.Orvill);

            // Манекен не двигается и не атакует: тест измеряет только решение
            // игрока, без вмешательства enemy AI и расталкивания толпой.
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[enemy].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(enemy);
            sim.Entities.NextAttackTick[enemy] = int.MaxValue;
            return sim;
        }

        /// <summary>
        /// Серия сабли (01.10) к цели сама не ходит: клик по врагу в четырёх
        /// метрах — удар в его сторону с места, без подхода. Подходит игрок
        /// сам, и на ходу замах режет скорость до 75%.
        /// </summary>
        [Test]
        public void AttackOnFarEnemy_DoesNotChase_AndWindupWalksAtThreeQuarterSpeed()
        {
            Simulation sim = ArenaWithStationaryEnemy(
                new FixVec2(Fix64.FromInt(4), Fix64.Zero), out int enemy);
            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = enemy,
                Aim = sim.Entities.Position[enemy],
            };
            for (int i = 0; i < 30; i++) sim.Step(in attack);
            Assert.AreEqual(Simulation.SabreLungeDistance.ToDouble(),
                sim.Entities.Position[Simulation.PlayerId].X.ToDouble(), .002,
                "погони за целью нет — за всю серию герой сместился только выпадом добивающего");
            Assert.AreEqual(5000, sim.Entities.Health[enemy], "четыре метра — вне сектора");

            // Отпустил, разогнался к врагу и на бегу снова зажал атаку.
            var walk = new InputFrame { Flags = (byte)InputFlags.MoveOrder, Aim = new FixVec2(Fix64.FromInt(9), Fix64.Zero) };
            for (int i = 0; i < 20; i++) sim.Step(in walk);
            var walkAndSwing = walk;
            walkAndSwing.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
            sim.Step(in walkAndSwing);
            Assert.AreEqual(sim.Tick - 1, sim.SabreSwing.StartTick, "удар начат на бегу");
            for (int i = 0; i < Simulation.SabreBaseContactTicks(0) - 1; i++)
            {
                sim.Step(in walkAndSwing);
                Assert.LessOrEqual(
                    sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat(),
                    (sim.Entities.MoveStep[Simulation.PlayerId] * Fix64.Ratio(3, 4)).ToFloat() + 0.0001f,
                    "замах серии не разгоняется выше 75%");
            }
        }

        /// <summary>
        /// Приказ идти во время замаха двигает героя с первого тика, а
        /// направление удара остаётся тем, что задано на старте: сектор
        /// проверяется на контакте из новой точки по прежнему направлению.
        /// </summary>
        [Test]
        public void GroundMoveOrder_DuringWindupMovesWithoutTurningTheSwing()
        {
            Simulation sim = ArenaWithStationaryEnemy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);
            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.AttackPressed,
                Aim = sim.Entities.Position[enemy],
            };
            sim.Step(in attack);
            Assert.AreEqual(1, sim.SabreSwing.Serial, "клик начал удар");

            int healthBefore = sim.Entities.Health[enemy];
            FixVec2 beforeMove = sim.Entities.Position[Simulation.PlayerId];
            var move = new InputFrame
            {
                Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(5)),
                Flags = (byte)InputFlags.MoveOrder,
                AttackTarget = -1,
            };
            sim.Step(in move);
            Assert.Greater(sim.Entities.Position[Simulation.PlayerId].Y.Raw, beforeMove.Y.Raw,
                "замах не съедает первый тик нового движения");
            Assert.AreEqual(new FixVec2(Fix64.One, Fix64.Zero), sim.Entities.Facing[Simulation.PlayerId],
                "корпус держит направление удара, пока удар идёт");

            InputFrame released = InputFrame.Empty;
            while (sim.Tick <= sim.SabreSwing.ContactTick) sim.Step(in released);
            Assert.Less(sim.Entities.Health[enemy], healthBefore,
                "враг остался в секторе — удар лёг");
        }

        [Test]
        public void ActiveWhirlwind_UsesThreeQuarterSpeedThenRestoresFullMovement()
        {
            Simulation sim = ArenaWithStationaryEnemy(
                new FixVec2(Fix64.Ratio(6, 5), Fix64.Zero), out int enemy);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(),
                new AbilityNode[0], 0);

            var castWhileMoving = new InputFrame
            {
                Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(5)),
                AbilityMask = 1,
                Flags = (byte)InputFlags.MoveOrder,
                AttackTarget = -1,
            };
            int healthBefore = sim.Entities.Health[enemy];
            sim.Step(in castWhileMoving);

            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.AbilityCast
                && e.Source == Simulation.PlayerId));
            FixVec2 castPosition = sim.Entities.Position[Simulation.PlayerId];

            InputFrame released = InputFrame.Empty;
            bool impactResolved = false;
            Fix64 previousY = castPosition.Y;
            for (int i = 1; i < Simulation.AbilityMovePenaltyTicks; i++)
            {
                sim.Step(in released);
                Fix64 currentY = sim.Entities.Position[Simulation.PlayerId].Y;
                Assert.Greater(currentY.Raw, previousY.Raw,
                    "активная фаза способности не должна вставлять стоп-тики в locomotion");
                Assert.LessOrEqual(
                    sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat(),
                    (sim.Entities.MoveStep[Simulation.PlayerId] * (sim.Tick - 1 < sim.PlayerAction.ContactTick ? Fix64.Ratio(3, 4) : Fix64.One)).ToFloat() + 0.0001f,
                    "способность сохраняет динамичное движение на 75% скорости");
                previousY = currentY;
                if (sim.Entities.Health[enemy] < healthBefore) impactResolved = true;
            }

            Assert.IsTrue(impactResolved,
                "контрольный delayed-impact Вихря должен разрешиться во время движения");
            Assert.Greater(
                FixVec2.Distance(castPosition,
                    sim.Entities.Position[Simulation.PlayerId]).ToFloat(),
                0.5f,
                "за время активной способности герой должен продолжить путь по одному клику");

            // После 0.8-секундной action-фазы persistent order остаётся жив и
            // за два тика возвращает обычную скорость через тот же acceleration.
            sim.Step(in released);
            sim.Step(in released);
            Assert.That(sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat(),
                Is.EqualTo(sim.Entities.MoveStep[Simulation.PlayerId].ToFloat()).Within(0.0001f),
                "после способности нельзя навсегда оставить скрытый slow");
        }

    }
}
