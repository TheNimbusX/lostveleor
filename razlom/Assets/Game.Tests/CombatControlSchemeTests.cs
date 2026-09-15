using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Схема управления от 11 сентября: ПКМ ведёт, ЛКМ бьёт.
    ///
    /// Пока обе команды приходили с одной кнопки, они не могли встретиться в
    /// одном тике, и симуляция считала их взаимоисключающими. Теперь зажать их
    /// вместе — обычное дело, и здесь проверяется именно то, что от этого
    /// ломалось: приказ идти, съеденный автоцелью.
    /// </summary>
    public class CombatControlSchemeTests
    {
        private const ulong Seed = 0x5CE3EUL;

        private static Simulation ArenaWithDummy(FixVec2 enemyPosition, out int enemy)
        {
            var sim = new Simulation(Seed, 16);
            sim.SetupTestArena(0);
            enemy = sim.Entities.Spawn(enemyPosition, 5000, Faction.Orvill);

            // Манекен: не ходит, не бьёт. Тест измеряет только решение игрока.
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[enemy].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(enemy);
            sim.Entities.NextAttackTick[enemy] = int.MaxValue;
            return sim;
        }

        /// <summary>
        /// Зажатая атака по назначенной цели не отменяет приказ идти.
        ///
        /// Игрок держит ЛКМ на враге и уводит героя ПКМ в другую сторону —
        /// герой обязан пойти туда, куда указали. Раньше защёлкнутая цель
        /// подменяла точку движения собой, и приказ пропадал молча.
        /// </summary>
        [Test]
        public void MoveOrderWinsOverLatchedAttackTarget()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(3), Fix64.Zero), out int enemy);

            var attackOnly = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = enemy,
            };
            sim.Step(in attackOnly);

            var both = new InputFrame
            {
                Flags = (byte)(InputFlags.Attack | InputFlags.MoveOrder),
                Aim = new FixVec2(Fix64.FromInt(-6), Fix64.Zero),
                AttackTarget = enemy,
            };
            for (int i = 0; i < 30; i++) sim.Step(in both);

            Assert.Less(sim.Entities.Position[Simulation.PlayerId].X.ToFloat(), 0f,
                "ПКМ обязан увести героя от цели, пока зажата ЛКМ");
        }

        /// <summary>
        /// Удержание ЛКМ по пустому месту всё равно бьёт: цель выбирает
        /// симуляция поиском ближайшего в лобовом секторе. Курсор задаёт
        /// направление, а не разрешение на удар.
        /// </summary>
        [Test]
        public void AttackWithoutTargetHitsNearestInFront()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);

            var attackNoTarget = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 90; i++) sim.Step(in attackNoTarget);

            Assert.Less(sim.Entities.Health[enemy], before,
                "зажатая атака без наведения обязана достать врага перед героем");
        }

        /// <summary>
        /// «Отошёл — ударил»: враг за спиной в радиусе удара, кнопка зажата.
        ///
        /// После шага назад корпус смотрит по направлению отхода, и курсор
        /// часто там же. Поиск цели только в лобовом секторе никогда не
        /// находил врага позади, и зажатая ЛКМ молчала, хотя стоя на той же
        /// точке лицом к врагу герой бил. Доворот обязан найти его с любой
        /// стороны.
        /// </summary>
        [Test]
        public void HeldAttackTurnsToEnemyBehind()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);

            // Корпус и курсор смотрят ОТ врага — ровно поза после отхода.
            sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(Fix64.FromInt(-1), Fix64.Zero);
            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(-5), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 60; i++) sim.Step(in attack);

            Assert.Less(sim.Entities.Health[enemy], before,
                "враг в двух метрах за спиной: зажатая атака обязана развернуть героя и достать");
        }

        /// <summary>
        /// Бить некого — взмах всё равно есть.
        ///
        /// Пустой взмах отвечает на нажатие и тратит такт атаки, но никого не
        /// назначает целью: молчащая кнопка читается как залипший ввод.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FirstSwingAfterRetreat_HitsWithoutWastingACooldown(bool explicitTarget)
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);
            sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(-Fix64.One, Fix64.Zero);
            var attack = InputFrame.Empty;
            attack.Flags = (byte)InputFlags.Attack;
            attack.AttackTarget = explicitTarget ? enemy : -1;
            attack.Aim = new FixVec2(Fix64.FromInt(-5), Fix64.Zero);
            int health = sim.Entities.Health[enemy];
            int swings = 0;
            int deadline = sim.Entities.AttackCooldown[Simulation.PlayerId];
            while (sim.Tick < deadline && sim.Entities.Health[enemy] == health)
            {
                // Клик по силуэту отпускаем сразу: цель должна пережить доворот.
                InputFrame frame = explicitTarget && sim.Tick > 0 ? InputFrame.Empty : attack;
                sim.Step(in frame);
                foreach (SimEvent ev in sim.Events)
                {
                    if (ev.Type != SimEventType.Attack || ev.Source != Simulation.PlayerId) continue;
                    Assert.AreEqual(enemy, ev.Target, "первый взмах не должен записываться в пустоту при довороте");
                    swings++;
                }
            }
            Assert.AreEqual(1, swings);
            Assert.Less(sim.Entities.Health[enemy], health,
                "первая тычка должна попасть раньше, чем истёк бы кулдаун пустого взмаха");
        }

    }
}
