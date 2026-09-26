using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Обнаружение игрока врагом.
    ///
    /// Приёмка задачи: враг вне радиуса обнаружения не гонится за игроком и не
    /// сдвигается с места; попавший в радиус агрится не мгновенно, а с
    /// небольшой случайной задержкой; заметив однажды — не забывает, даже
    /// оказавшись впоследствии далеко.
    /// </summary>
    public class EnemyAiTests
    {
        private const ulong Seed = 0xA6720UL;
        private const int Enemy = 1;

        /// <summary>
        /// Один враг с боевыми статами из ConfigureEnemy (через SetupTestArena),
        /// но на известной позиции — случайный разброс спавна тут только мешал бы.
        /// </summary>
        private static Simulation ArenaWithEnemyAt(FixVec2 enemyPosition)
        {
            var sim = new Simulation(Seed);
            sim.SetupTestArena(1);
            sim.Entities.Position[Enemy] = enemyPosition;
            return sim;
        }

        private static void Run(Simulation sim, in InputFrame frame, int ticks)
        {
            for (int t = 0; t < ticks; t++) sim.Step(in frame);
        }

        [Test]
        public void EnemyOutsideDetectRange_StaysPutAndNeverAggroes()
        {
            var sim = ArenaWithEnemyAt(new FixVec2(Fix64.FromInt(20), Fix64.Zero));
            FixVec2 start = sim.Entities.Position[Enemy];

            Run(sim, InputFrame.Empty, 90);

            Assert.That(sim.Entities.Aggro[Enemy], Is.False,
                "враг агрится через всю арену, не заметив игрока");
            Assert.That(sim.Entities.Position[Enemy].X.ToDouble(), Is.EqualTo(start.X.ToDouble()));
            Assert.That(sim.Entities.Position[Enemy].Y.ToDouble(), Is.EqualTo(start.Y.ToDouble()));
        }

        [Test]
        public void EnemyInsideDetectRange_EventuallyNoticesAndCloses()
        {
            var sim = ArenaWithEnemyAt(new FixVec2(Fix64.FromInt(4), Fix64.Zero));
            double startDistanceSq = sim.Entities.Position[Enemy].LengthSq.ToDouble();

            // С запасом больше максимальной задержки обнаружения (15 тиков).
            Run(sim, InputFrame.Empty, 60);

            Assert.That(sim.Entities.Aggro[Enemy], Is.True, "враг так и не заметил игрока в упор");
            Assert.That(sim.Entities.Position[Enemy].LengthSq.ToDouble(), Is.LessThan(startDistanceSq),
                "заметив игрока, враг не пошёл на сближение");
        }

        /// <summary>
        /// Хранитель начинает замах, только когда смотрит на героя (±37°):
        /// замах боком нарисовал бы сектор мимо героя.
        /// </summary>
        [Test]
        public void Guardian_StartsSwingOnlyWhenFacingThePlayer()
        {
            var sim = ArenaWithEnemyAt(new FixVec2(Fix64.FromInt(2), Fix64.Zero));
            sim.Entities.Stats[Enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(Enemy);
            sim.Entities.Facing[Enemy] = new FixVec2(Fix64.Zero, Fix64.One);

            for (int t = 0; t < 30 && !sim.TryGetEnemySwing(Enemy, out _); t++)
                sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetEnemySwing(Enemy, out var swing), Is.True, "замах так и не начался");
            // Доворот 12° за тик от 90°: на тике 3 остаётся 42° (мимо окна),
            // на тике 4 — 30°. Старое правило ±120° дало бы замах на тике 0.
            Assert.That(swing.StartTick, Is.EqualTo(4), "замах начался боком");
            Assert.That(sim.Entities.Facing[Enemy], Is.EqualTo(swing.Direction),
                "в начале замаха корпус встаёт по направлению удара");
        }

        // Радиус сектора 2,4 → 2,2 м (стенд баланса, 26.09): замах начинается на его дальности.
        [TestCase(220, true)]
        [TestCase(230, false)]
        public void Guardian_StartsSwingOnlyWithinTwoPointTwoMetres(int centimetres, bool swings)
        {
            var sim = ArenaWithEnemyAt(new FixVec2(Fix64.Ratio(centimetres, 100), Fix64.Zero));
            sim.Entities.Stats[Enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(Enemy);
            sim.Entities.Facing[Enemy] = new FixVec2(-Fix64.One, Fix64.Zero);
            Run(sim, InputFrame.Empty, 5);
            Assert.That(sim.TryGetEnemySwing(Enemy, out _), Is.EqualTo(swings));
        }
    }
}
