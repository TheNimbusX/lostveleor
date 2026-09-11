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
        /// Зажатая атака сама разворачивает героя на курсор.
        ///
        /// Враг стоит сбоку, вне узкого сектора старта взмаха. Без доворота
        /// кнопка молчала бы вечно: цели нет, приказа идти нет, корпус стоит.
        /// </summary>
        [Test]
        public void HeldAttackTurnsTowardTheCursor()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.Zero, Fix64.FromInt(2)), out int enemy);

            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(8)),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 90; i++) sim.Step(in attack);

            Assert.Less(sim.Entities.Health[enemy], before,
                "герой обязан довернуться на курсор и достать врага сбоку");
        }

        /// <summary>
        /// На бегу с зажатой ЛКМ герой достаёт того, мимо кого пробегает.
        ///
        /// Главная жалоба владельца: приказ идти забирал направление корпуса
        /// себе, окно старта взмаха узкое, и пробегающий мимо враг успевал
        /// выйти из него за пару тиков. Кнопка выглядела нажатой и молчала.
        /// </summary>
        [Test]
        public void RunningWithHeldAttackHitsWhatItPassesBy()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(4), Fix64.Ratio(15, 10)), out int enemy);

            // Бежим мимо: точка приказа лежит за врагом и в стороне от него.
            var runAndSwing = new InputFrame
            {
                Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack),
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(10), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 120; i++) sim.Step(in runAndSwing);

            Assert.Less(sim.Entities.Health[enemy], before,
                "пробегая в полутора метрах, зажатая атака обязана достать врага");
        }

        /// <summary>
        /// Отошёл — ударил. Враг остаётся за спиной, и герой обязан
        /// довернуться к нему сам.
        ///
        /// Владелец снял это на видео: с той же точки, но стоя на месте, удары
        /// проходили, а после отхода — нет. Разница была в направлении корпуса:
        /// отходя, герой смотрит ОТ врага, а цель искалась в лобовом секторе,
        /// то есть поворот требовал, чтобы игрок довернулся раньше героя.
        /// </summary>
        [Test]
        public void SteppingBackThenAttackingStillReaches()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.Ratio(13, 10), Fix64.Zero), out int enemy);

            // Шаг назад: враг остаётся в 2.3 м, то есть В ДАЛЬНОСТИ удара
            // (2.5), но ЗА СПИНОЙ — корпус развёрнут от него. Точка приказа
            // вынесена за мёртвую зону разворота на месте (0.5 м).
            var stepBack = new InputFrame
            {
                Flags = (byte)InputFlags.MoveOrder,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(-1), Fix64.Zero),
            };
            for (int i = 0; i < 30; i++) sim.Step(in stepBack);
            Assert.Less(sim.Entities.Position[Simulation.PlayerId].X.ToFloat(), -0.5f,
                "герой должен был отойти назад");

            // Теперь бьём, не трогая мышь: курсор остался там, куда отходили.
            var swing = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(-1), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 90; i++) sim.Step(in swing);

            Assert.Less(sim.Entities.Health[enemy], before,
                "после отхода герой обязан развернуться к врагу и достать его");
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
        [Test]
        public void HeldAttackSwingsWithNothingInRange()
        {
            var sim = new Simulation(Seed, 16);
            sim.SetupTestArena(0);

            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero),
            };

            int swings = 0;
            for (int i = 0; i < 60; i++)
            {
                sim.Step(in attack);
                for (int e = 0; e < sim.Events.Count; e++)
                    if (sim.Events[e].Type == SimEventType.Attack
                        && sim.Events[e].Source == Simulation.PlayerId) swings++;
            }

            Assert.Greater(swings, 0, "зажатая кнопка обязана давать взмах и в пустоте");
            Assert.AreEqual(-1, sim.Entities.PendingAttackTarget[Simulation.PlayerId],
                "пустой взмах никого не назначает целью");
        }

        /// <summary>
        /// Пустой взмах не бьёт своих и вообще никого: урона в пустоте нет.
        /// </summary>
        [Test]
        public void EmptySwingDealsNoDamage()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.FromInt(12), Fix64.Zero), out int enemy);

            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(12), Fix64.Zero),
            };

            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 60; i++) sim.Step(in attack);

            Assert.AreEqual(before, sim.Entities.Health[enemy],
                "враг в двенадцати метрах не должен получать урон от взмахов в пустоте");
        }

        /// <summary>
        /// Дальность героя шире, чем у моба. Проверяется не число, а ПОРЯДОК:
        /// одна константа обслуживала обоих, и «шире игроку» молча удлиняло бы
        /// удар врага — то есть его телеграф.
        /// </summary>
        [Test]
        public void PlayerReachesFurtherThanEnemies()
        {
            Simulation sim = ArenaWithDummy(
                new FixVec2(Fix64.Ratio(23, 10), Fix64.Zero), out int enemy);

            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = -1,
                Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero),
            };

            // 2.3 м — за прежней дальностью 2.0 и внутри новой 2.5. Герой
            // достаёт, СТОЯ НА МЕСТЕ: приказа идти нет, значит попадание
            // доказывает именно дальность, а не подход.
            int before = sim.Entities.Health[enemy];
            for (int i = 0; i < 60; i++) sim.Step(in attack);

            Assert.Less(sim.Entities.Health[enemy], before,
                "с 2.3 м герой обязан доставать: его дальность 2.5");
            Assert.AreEqual(2.5f, Simulation.AutoAttackRange.ToFloat(), 0.001f);
        }
    }
}
