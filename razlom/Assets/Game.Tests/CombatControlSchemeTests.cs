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
        /// «Отошёл — ударил» (серия сабли, 01.10): удар уходит туда, куда
        /// показывает курсор, а не на ближайшего. Враг за спиной, курсор от
        /// него — зажатая атака бьёт пустоту и его не задевает; курсор на нём —
        /// корпус встаёт к нему сразу, без доворота по 20° за тик.
        /// </summary>
        [Test]
        public void HeldAttackSwingsWhereTheCursorPoints_NotAtTheNearestBehind()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);

            sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(Fix64.FromInt(-1), Fix64.Zero);
            var away = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = enemy,
                Aim = new FixVec2(Fix64.FromInt(-5), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 60; i++) sim.Step(in away);

            Assert.AreEqual(before, sim.Entities.Health[enemy],
                "курсор от врага: серия бьёт туда, куда показали, и цель под курсором не подменяет направление");
        }

        /// <summary>
        /// Первый удар после отхода: корпус смотрит от врага, курсор — на него.
        /// Удар встаёт по курсору сразу и ложится в свой тик контакта (4).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FirstSwingAfterRetreat_LandsOnItsContactTick(bool tap)
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(2), Fix64.Zero), out int enemy);
            sim.Entities.Facing[Simulation.PlayerId] = new FixVec2(-Fix64.One, Fix64.Zero);
            var attack = InputFrame.Empty;
            attack.Flags = (byte)(tap ? InputFlags.AttackPressed : InputFlags.Attack);
            attack.Aim = sim.Entities.Position[enemy];
            int health = sim.Entities.Health[enemy];
            int swings = 0, landed = -1;
            for (int t = 0; t < 8; t++)
            {
                int tick = sim.Tick;
                InputFrame frame = tap && t > 0 ? InputFrame.Empty : attack;
                sim.Step(in frame);
                foreach (SimEvent ev in sim.Events)
                {
                    if (ev.Source != Simulation.PlayerId) continue;
                    if (ev.Type == SimEventType.Attack) swings++;
                    if (ev.Type == SimEventType.Damage && ev.Target == enemy && landed < 0) landed = tick;
                }
            }
            Assert.AreEqual(1, swings);
            Assert.AreEqual(Simulation.SabreBaseContactTicks(0), landed, "контакт первого удара");
            Assert.Less(sim.Entities.Health[enemy], health);
        }

        /// <summary>
        /// Прямое управление (стик): удержание атаки бьёт по прицелу, а враг за
        /// спиной не перехватывает взмах. Это другой режим, чем доворот ЛКМ выше.
        /// </summary>
        [Test]
        public void DirectAimDoesNotSilenceAttackForEnemyBehindCursor()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(-2), Fix64.Zero), out int enemy);

            var input = InputFrame.Empty;
            input.Flags = (byte)(InputFlags.DirectMovement | InputFlags.Attack);
            input.Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero);
            int emptySwings = 0;
            for (int tick = 0; tick < 45; tick++)
            {
                sim.Step(in input);
                foreach (SimEvent e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source == Simulation.PlayerId && e.Target < 0)
                        emptySwings++;
            }
            Assert.Greater(emptySwings, 0, "Удержание атаки отвечает взмахом даже без цели по направлению прицела.");
            Assert.AreEqual(5000, sim.Entities.Health[enemy], "Враг за спиной не перехватывает прямое прицеливание.");
        }
    }
}
