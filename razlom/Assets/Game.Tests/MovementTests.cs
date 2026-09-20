using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Управление персонажем: приказ идти в точку, разворот на месте
    /// и ограниченная скорость поворота.
    ///
    /// Отдельная проверка на то, что придя в точку персонаж ВСТАЁТ, а не дрожит
    /// вокруг неё: это классическая ошибка движения к цели, и заметна она только
    /// на глаз, если её не ловить тестом.
    /// </summary>
    public class MovementTests
    {
        private const ulong Seed = 0xA11CE5UL;

        [Test]
        public void SharedCampMovement_MatchesRiftAccelerationBrakingAndTurns()
        {
            var sim = SoloArena();
            var position = sim.Entities.Position[Simulation.PlayerId];
            var facing = sim.Entities.Facing[Simulation.PlayerId];
            var velocity = FixVec2.Zero;
            var fullStep = sim.Entities.MoveStep[Simulation.PlayerId];
            for (int tick = 0; tick < 180; tick++)
            {
                var frame = tick < 55 ? Order(5, 0) : tick < 110 ? Order(-3, 4) : Order(0, -2);
                var delta = frame.Aim - position;
                velocity = Simulation.Approach(velocity, Simulation.PlayerTravelStep(delta, fullStep), fullStep).ClampLength(fullStep);
                position += velocity;
                facing = Simulation.PlayerFacingStep(facing, delta);
                sim.Step(in frame);
                Assert.That(position, Is.EqualTo(sim.Entities.Position[Simulation.PlayerId]), $"position tick {tick}");
                Assert.That(velocity, Is.EqualTo(sim.Entities.Velocity[Simulation.PlayerId]), $"velocity tick {tick}");
                Assert.That(facing, Is.EqualTo(sim.Entities.Facing[Simulation.PlayerId]), $"facing tick {tick}");
            }
        }

        /// <summary>Арена без врагов: проверяем движение, а не бой.</summary>
        private static Simulation SoloArena()
        {
            var sim = new Simulation(Seed);
            sim.SetupTestArena(0);
            return sim;
        }

        private static InputFrame Order(Fix64 x, Fix64 y, InputFlags flags)
            => new InputFrame
            {
                Aim = new FixVec2(x, y),
                AbilityMask = 0,
                Flags = (byte)flags
            };

        private static InputFrame Order(int x, int y)
            => Order(Fix64.FromInt(x), Fix64.FromInt(y), InputFlags.MoveOrder);

        private static void Run(Simulation sim, in InputFrame frame, int ticks)
        {
            for (int t = 0; t < ticks; t++) sim.Step(in frame);
        }

        [Test]
        public void MoveOrder_WalksToTheAimPoint()
        {
            var sim = SoloArena();
            FixVec2 target = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            InputFrame frame = Order(Fix64.FromInt(5), Fix64.Zero, InputFlags.MoveOrder);

            // Скорость 4.5 м/с при 30 Гц — это 0.15 м за тик до торможения.
            Run(sim, in frame, 40);

            FixVec2 pos = sim.Entities.Position[Simulation.PlayerId];

            // «Дойти» значит встать в точку телом: недоход на радиус мёртвой
            // зоны — это не ошибка, а то, как приход в точку и устроен.
            Assert.That(FixVec2.Distance(pos, target).ToDouble(), Is.LessThanOrEqualTo(0.5));
            Assert.That(pos.Y.Raw, Is.EqualTo(0L));
            Assert.That(sim.Entities.Velocity[Simulation.PlayerId].LengthSq.Raw, Is.EqualTo(0L));
        }

        [Test]
        public void Turning_IsRateLimited()
        {
            var sim = SoloArena();

            // Разворот на 180° не должен произойти за один тик.
            InputFrame frame = Order(Fix64.FromInt(-10), Fix64.Zero, InputFlags.MoveOrder);
            sim.Step(in frame);

            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Assert.That(facing.X.ToDouble(), Is.InRange(0.765, 0.767),
                "за один тик развернулись больше чем на шаг");
        }

        [Test]
        public void MoveOrder_CannotCrossAClosedLayoutWall()
        {
            var room = new ModuleDefinition("module.test_room", 4, 4,
                new ModuleConnector[0], weight: 0, isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }), 2);
            Assert.That(map.TryPlace(0, 0, 0, 0), Is.EqualTo(0));
            Assert.That(map.TryPlace(0, 0, 8, 0, parent: -1), Is.EqualTo(1));

            var sim = new Simulation(Seed);
            sim.SetupRift(map, Seed, minEnemiesPerRoom: 0, maxEnemiesPerRoom: 0, enemyHealth: 100);

            // Вторая комната сама по себе walkable, поэтому приказ не
            // клампится к первой. Между комнатами остаётся закрытый разрыв:
            // реальное перемещение обязано остановиться у наружной стены.
            FixVec2 target = map.CenterOf(1);
            InputFrame click = Order(target.X, target.Y, InputFlags.MoveOrder);
            sim.Step(in click);
            InputFrame released = InputFrame.Empty;
            Run(sim, in released, 240);

            FixVec2 position = sim.Entities.Position[Simulation.PlayerId];
            Fix64 radius = sim.Entities.BodyRadius[Simulation.PlayerId];
            Fix64 firstRoomMaxX = LayoutMap.CellSize * Fix64.FromInt(4) - radius;

            Assert.That(map.IsWalkable(position, radius), Is.True,
                "движение вывело физическое тело за walkable-контур");
            Assert.That(position.X, Is.LessThanOrEqualTo(firstRoomMaxX),
                "герой пересёк закрытую стену и попал во вторую комнату");
        }

        private static ulong RunScript(InputFrame[] script)
        {
            var sim = new Simulation(0xFEEDUL);
            sim.SetupTestArena(30);
            for (int t = 0; t < script.Length; t++) sim.Step(in script[t]);
            return sim.StateHash();
        }
    }
}
