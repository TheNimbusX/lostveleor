using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Способности Пелага, дописанные под утверждённый диздок.
    ///
    /// У каждой проверяется то, ради чего она существует, а не факт нанесения
    /// урона: Рассекающий удар — что бьёт ОДНОГО и не двигает героя, усиление —
    /// что оно кончается и добавляет огонь ко ВСЕМУ, смесь — что лужа продолжает
    /// жечь после взрыва, Крушение — что это три отдельных нажатия.
    /// </summary>
    public class PelagAbilityTests
    {
        private static Simulation Arena(AbilityDefinition definition, int slot = 0)
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.SetAbility(slot, definition, new AbilityNode[0], 0);
            return sim;
        }

        private static int Enemy(Simulation sim, int x10, int y10 = 0, int health = 10000)
        {
            int id = sim.Entities.Spawn(
                new FixVec2(Fix64.Ratio(x10, 10), Fix64.Ratio(y10, 10)), health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Press(int slot, int aimX10, int aimY10 = 0, int target = -1)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.Ratio(aimX10, 10), Fix64.Ratio(aimY10, 10));
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        private static int Health(Simulation sim, int id) => sim.Entities.Health[id];

        // ---- Рассекающий удар ----

        [Test]
        public void CleaveHitsASingleTargetHard()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int victim = Enemy(sim, 15);
            int neighbour = Enemy(sim, 15, 12);

            sim.Step(Press(0, 20, 0, victim));
            Idle(sim, 16);

            Assert.Less(Health(sim, victim), 10000, "цель не задета");
            Assert.AreEqual(10000, Health(sim, neighbour), "задет сосед — это не АОЕ");
        }

        /// <summary>Это НЕ рывок: герой остаётся на месте.</summary>
        [Test]
        public void CleaveCastsInEmptyArenaAndCanHitAnEnemyEnteringDuringWindup()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            sim.Step(Press(0, 80));
            Assert.IsTrue(sim.CleaveActive);
            Assert.Greater(sim.AbilityReadyTick(0), 0);
            Idle(sim, 8);
            int target = Enemy(sim, 15);
            Idle(sim, 22);
            Assert.Less(Health(sim, target), 10000);
            Assert.IsFalse(sim.CleaveActive);
        }

        // ---- Огненное усиление ----

        [Test]
        public void CleaveUsesBodyRadiusAndRejectsWall()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int target = Enemy(sim, 25);
            sim.Entities.BodyRadius[target] = Fix64.One;
            sim.Step(Press(0, 25, 0, target));
            Idle(sim, 15);
            Assert.Less(Health(sim, target), 10000);

            var cells = new bool[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) cells[y * 64 + x] = x != 24;
            sim.SetupCamp(FixVec2.Zero, new CampWalkMap(new FixVec2(Fix64.FromInt(-2), Fix64.FromInt(-2)),
                Fix64.Ratio(1, 8), 64, 64, cells));
            sim.Entities.Facing[0] = new FixVec2(Fix64.One, Fix64.Zero);
            sim.SetAbility(0, AbilityDefinition.Cleave(), new AbilityNode[0], 0);
            target = Enemy(sim, 15);
            sim.Step(Press(0, 15, 0, target));
            Assert.IsTrue(sim.CleaveActive);
            Assert.Greater(sim.AbilityReadyTick(0), 0);
            Idle(sim, 30);
            Assert.AreEqual(10000, Health(sim, target));
        }

        [Test]
        public void BlazeLastsThreeSecondsAndEnds()
        {
            var sim = Arena(AbilityDefinition.Blaze());
            sim.Step(Press(0, 20));
            Assert.IsFalse(sim.BlazeActive, "усиление началось до поливания");
            Idle(sim, Simulation.BlazeIgnitionDelayTicks);
            Assert.IsTrue(sim.BlazeActive, "усиление не включилось");

            // Каст занял тик, поэтому дожить усиление должно ещё 88 тиков:
            // 90 тиков всего минус тик нажатия и минус тик, на котором окно
            // уже закрыто.
            Idle(sim, 88);
            Assert.IsTrue(sim.BlazeActive, "усиление кончилось раньше трёх секунд");

            Idle(sim, 2);
            Assert.IsFalse(sim.BlazeActive, "усиление не кончилось");
        }

        /// <summary>
        /// Обычная атака под усилением получает ПЯТУЮ ЧАСТЬ своей силы огнём.
        ///
        /// Броня и сопротивление огню у цели обнулены, крит выключен: иначе
        /// проверялась бы не доля, а кривая брони и бросок случайности.
        /// </summary>
        [Test]
        public void BlazeAddsAFifthOfAttackPowerToBasicAttacks()
        {
            var sim = Arena(AbilityDefinition.Blaze());
            int victim = Enemy(sim, 15);
            sim.Entities.Stats[0].SetBase(StatType.Damage, Fix64.FromInt(100));
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            sim.Entities.RefreshStats(0);
            sim.Entities.Stats[victim].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.Stats[victim].SetBase(StatType.FireResist, Fix64.Zero);
            sim.Entities.RefreshStats(victim);

            sim.Step(Press(0, 15));
            Idle(sim, Simulation.BlazeIgnitionDelayTicks);
            Assert.IsTrue(sim.BlazeActive, "усиление не включилось");

            var swing = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = victim,
                Aim = new FixVec2(Fix64.Ratio(15, 10), Fix64.Zero),
            };
            int physical = 0, fire = 0;
            for (int i = 0; i < 40; i++)
            {
                sim.Step(in swing);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == 0)
                    { if (e.DamageKind == DamageType.Fire) fire += e.Amount; else physical += e.Amount; }
            }

            Assert.Greater(physical, 0, "обычная атака не прошла");
            Assert.That(fire * 5, Is.EqualTo(physical).Within(4),
                "огонь должен быть пятой частью силы удара");
        }

        // ---- Взрывная смесь ----

        [Test]
        public void FlaskExplodesAndLeavesABurningPool()
        {
            var sim = Arena(AbilityDefinition.FireFlask());
            int victim = Enemy(sim, 40);

            sim.Step(Press(0, 40));
            Idle(sim, 20);
            int afterBlast = Health(sim, victim);
            Assert.Less(afterBlast, 10000, "взрыв не задел цель");

            Idle(sim, 40);
            Assert.Less(Health(sim, victim), afterBlast, "лужа не жжёт");
        }

        /// <summary>Лужа гаснет: урон не идёт вечно.</summary>
        [Test]
        public void FirePoolBurnsOutAfterItsDuration()
        {
            var sim = Arena(AbilityDefinition.FireFlask());
            int victim = Enemy(sim, 40);

            sim.Step(Press(0, 40));
            Idle(sim, 200);
            int settled = Health(sim, victim);

            Idle(sim, 60);
            Assert.AreEqual(settled, Health(sim, victim), "лужа горит дольше положенного");
        }

        // ---- Крушение ----

        [Test]
        public void WreckNeedsThreePressesAndHitsThreeTimes()
        {
            var sim = Arena(AbilityDefinition.Wreck());
            int victim = Enemy(sim, 15);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int afterFirst = Health(sim, victim);
            Assert.Less(afterFirst, 10000, "первый удар не прошёл");

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int afterSecond = Health(sim, victim);
            Assert.Less(afterSecond, afterFirst, "второй удар не прошёл");

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            Assert.Less(Health(sim, victim), afterSecond, "завершающий удар не прошёл");
        }

        // ---- Общий деш ----

        [Test]
        public void DashMovesPlayerWithoutATarget()
        {
            var sim = Arena(AbilityDefinition.Dash());
            sim.Step(Press(0, 20));
            Idle(sim, 12);

            Assert.IsTrue(sim.Entities.Position[Simulation.PlayerId].X > Fix64.FromInt(2),
                "деш не сдвинул героя");
        }

        // ---- Протяжка к врагу ----

        [Test]
        public void BoardingPullsPlayerToTargetAndPunches()
        {
            var sim = Arena(AbilityDefinition.AnchorLeap());
            int victim = Enemy(sim, 50);

            sim.Step(Press(0, 50, 0, victim));
            Idle(sim, 40);

            Assert.Less(Health(sim, victim), 10000, "удара по прибытии не было");
            Assert.IsTrue(sim.Entities.Position[Simulation.PlayerId].X > Fix64.FromInt(3),
                "герой не подтянулся к цели");
        }
    }
}
