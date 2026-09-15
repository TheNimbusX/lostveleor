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

    }
}
