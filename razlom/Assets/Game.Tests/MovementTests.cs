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
        public void MoveOrder_StandsStillAfterArriving()
        {
            var sim = SoloArena();
            InputFrame frame = Order(Fix64.FromInt(5), Fix64.Zero, InputFlags.MoveOrder);

            Run(sim, in frame, 40);
            FixVec2 arrived = sim.Entities.Position[Simulation.PlayerId];

            // Приказ продолжает поступать — персонаж обязан стоять, а не
            // перепрыгивать точку туда-сюда с амплитудой в один шаг.
            Run(sim, in frame, 60);

            FixVec2 after = sim.Entities.Position[Simulation.PlayerId];
            Assert.That(after.X.Raw, Is.EqualTo(arrived.X.Raw));
            Assert.That(after.Y.Raw, Is.EqualTo(arrived.Y.Raw));
            Assert.That(sim.Entities.Velocity[Simulation.PlayerId].LengthSq.Raw, Is.EqualTo(0L));
        }

        [Test]
        public void OrderNextToSelf_TurnsWithoutMoving()
        {
            var sim = SoloArena();
            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];

            // Дотовский разворот на месте: клик рядом с собой не двигает,
            // а только доворачивает. Отдельной кнопки для этого нет.
            InputFrame frame = Order(Fix64.Zero, Fix64.Ratio(3, 10), InputFlags.MoveOrder);
            Run(sim, in frame, 30);

            FixVec2 pos = sim.Entities.Position[Simulation.PlayerId];
            Assert.That(pos.X.Raw, Is.EqualTo(start.X.Raw));
            Assert.That(pos.Y.Raw, Is.EqualTo(start.Y.Raw));

            // Девяносто градусов при шаге 20° за тик — пяти тиков достаточно.
            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Assert.That(facing.Y.ToDouble(), Is.EqualTo(1.0).Within(0.001));
            Assert.That(facing.X.ToDouble(), Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void SingleNearbyClick_FinishesTurnAfterButtonWasReleased()
        {
            var sim = SoloArena();
            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];

            // В реальной игре MoveOrder приходит одним кадром. Старый тест
            // держал флаг 30 тиков и скрывал баг: после отпускания приказ
            // снимался внутри мёртвой зоны уже на первом шаге поворота.
            InputFrame click = Order(Fix64.Zero, Fix64.Ratio(3, 10), InputFlags.MoveOrder);
            sim.Step(in click);
            InputFrame released = InputFrame.Empty;
            Run(sim, in released, 8);

            Assert.That(sim.Entities.Position[Simulation.PlayerId].X.Raw, Is.EqualTo(start.X.Raw));
            Assert.That(sim.Entities.Position[Simulation.PlayerId].Y.Raw, Is.EqualTo(start.Y.Raw));
            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Assert.That(facing.Y.ToDouble(), Is.EqualTo(1.0).Within(0.001));
            Assert.That(facing.X.ToDouble(), Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void OrderBeyondDeadZone_ActuallyWalks()
        {
            var sim = SoloArena();

            // Граница мёртвой зоны — один метр. Приказ за ней обязан двигать,
            // иначе персонаж залипнет рядом с собой.
            InputFrame frame = Order(Fix64.FromInt(3), Fix64.Zero, InputFlags.MoveOrder);
            Run(sim, in frame, 5);

            Assert.That(sim.Entities.Position[Simulation.PlayerId].X.ToDouble(),
                Is.GreaterThan(0.5));
        }

        [Test]
        public void Turning_IsRateLimited()
        {
            var sim = SoloArena();

            // Разворот на 180° не должен произойти за один тик.
            InputFrame frame = Order(Fix64.FromInt(-10), Fix64.Zero, InputFlags.MoveOrder);
            sim.Step(in frame);

            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Assert.That(facing.X.ToDouble(), Is.GreaterThan(0.9),
                "за один тик развернулись больше чем на шаг");
        }

        [Test]
        public void WithoutOrder_PlayerNeitherMovesNorTurns()
        {
            var sim = SoloArena();

            // Курсор ездит по столу, приказа нет: персонаж обязан стоять
            // и не крутиться. Взгляд — состояние, а не отражение мыши.
            Run(sim, Order(Fix64.FromInt(10), Fix64.FromInt(10), InputFlags.None), 30);

            Assert.That(sim.Entities.Position[Simulation.PlayerId].LengthSq.Raw, Is.EqualTo(0L));
            Assert.That(sim.Entities.Facing[Simulation.PlayerId].X, Is.EqualTo(Fix64.One));
        }

        [Test]
        public void Facing_StaysUnitLength_OverManyTurns()
        {
            var sim = SoloArena();

            // Гоняем взгляд по кругу: если нормализация после поворота потерялась,
            // длина уползёт и конус способности начнёт врать по дальности.
            for (int i = 0; i < 200; i++)
            {
                Fix64 x = Fix64.FromInt((i % 4) < 2 ? 10 : -10);
                Fix64 y = Fix64.FromInt((i % 4) % 2 == 0 ? 7 : -7);
                InputFrame frame = Order(x, y, InputFlags.MoveOrder);
                sim.Step(in frame);
            }

            double length = sim.Entities.Facing[Simulation.PlayerId].Length.ToDouble();
            Assert.That(length, Is.EqualTo(1.0).Within(0.0005));
        }

        [Test]
        public void EnemiesTurnTowardsPlayer()
        {
            var sim = new Simulation(Seed);
            sim.SetupTestArena(20);

            InputFrame frame = InputFrame.Empty;
            Run(sim, in frame, 120);

            // Каждый живой враг смотрит примерно на игрока: разворот входит
            // в состояние симуляции, а не решается анимацией.
            FixVec2 playerPos = sim.Entities.Position[Simulation.PlayerId];
            int checkedCount = 0;

            for (int i = 1; i < sim.Entities.Count; i++)
            {
                if (!sim.Entities.Alive[i]) continue;

                FixVec2 toPlayer = (playerPos - sim.Entities.Position[i]).Normalized();
                if (toPlayer.LengthSq.Raw == 0) continue;

                Fix64 dot = FixVec2.Dot(sim.Entities.Facing[i], toPlayer);
                Assert.That(dot.ToDouble(), Is.GreaterThan(0.9),
                    $"враг {i} смотрит мимо игрока");
                checkedCount++;
            }

            Assert.That(checkedCount, Is.GreaterThan(0), "проверять оказалось нечего");
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
            sim.SetupRift(map, Seed, enemiesPerRoom: 0, enemyHealth: 100);

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

        [Test]
        public void MovementFlags_DoNotBreakDeterminism()
        {
            // Тот же приём, что в DeterminismTests, но ввод теперь включает
            // и флаги, и точку прицела: новые поля обязаны быть частью реплея.
            var script = new InputFrame[300];
            var rng = new Pcg32(777UL, 9UL);
            for (int t = 0; t < script.Length; t++)
                script[t] = new InputFrame
                {
                    Aim = new FixVec2(rng.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30)),
                                      rng.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30))),
                    AbilityMask = (byte)rng.NextInt(0, 16),
                    Flags = (byte)rng.NextInt(0, 2)
                };

            ulong first = RunScript(script);
            ulong second = RunScript(script);

            Assert.That(second, Is.EqualTo(first));
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
