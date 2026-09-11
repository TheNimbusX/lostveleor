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

        [Test]
        public void CleaveWithoutEnemiesCompletesAndKeepsFacing()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            var facing = sim.Entities.Facing[0];
            sim.Step(Press(0, -80, 80));
            Assert.IsTrue(sim.CleaveActive);
            Idle(sim, 30);
            Assert.IsFalse(sim.CleaveActive);
            Assert.AreEqual(facing, sim.Entities.Facing[0]);
        }

        [Test]
        public void CleaveDoesNotMoveThePlayer()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            Enemy(sim, 15);
            FixVec2 before = sim.Entities.Position[Simulation.PlayerId];

            sim.Step(Press(0, 20));
            Idle(sim, 16);

            Assert.AreEqual(before.X.Raw, sim.Entities.Position[Simulation.PlayerId].X.Raw);
            Assert.AreEqual(before.Y.Raw, sim.Entities.Position[Simulation.PlayerId].Y.Raw);
        }

        /// <summary>Бьёт сильнее Вихря: он достаётся одному, а не всем вокруг.</summary>
        [Test]
        public void CleaveHitsHarderThanWhirlwind()
        {
            Assert.Greater(
                AbilityDefinition.Cleave().GetBase(AbilityStatType.Damage).ToInt(),
                AbilityDefinition.Whirlwind().GetBase(AbilityStatType.Damage).ToInt());
        }

        /// <summary>Промах курсора мимо тела не съедает нажатие.</summary>
        [Test]
        public void CleaveWithoutChosenTargetStillHitsTheNearest()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int victim = Enemy(sim, 15);

            sim.Step(Press(0, 20));
            Idle(sim, 16);

            Assert.Less(Health(sim, victim), 10000);
        }

        // ---- Огненное усиление ----

        [TestCase(80, 0)]
        [TestCase(-15, 0)]
        [TestCase(0, 15)]
        public void CleaveCastsDespiteInvalidTargetAndDoesNotHitIt(int x, int y)
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int target = Enemy(sim, x, y);
            sim.Step(Press(0, x, y, target));
            Assert.IsTrue(sim.CleaveActive);
            Assert.Greater(sim.AbilityReadyTick(0), 0);
            Idle(sim, 30);
            Assert.AreEqual(10000, Health(sim, target));
        }

        [Test]
        public void CleaveDoesNotRetargetDeadOrEscapedVictim()
        {
            foreach (bool dead in new[] { false, true })
            {
                var sim = Arena(AbilityDefinition.Cleave());
                int victim = Enemy(sim, 15), neighbour = Enemy(sim, 16, 6);
                sim.Step(Press(0, 15, 0, victim));
                if (dead) sim.Entities.Alive[victim] = false;
                else sim.Entities.Position[victim] = new FixVec2(Fix64.FromInt(8), Fix64.Zero);
                Idle(sim, 30);
                Assert.AreEqual(10000, Health(sim, neighbour));
                Assert.AreEqual(10000, Health(sim, victim));
            }
        }

        [Test]
        public void CleaveWindowHitsOnceAndSweepsBetweenTicks()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int target = Enemy(sim, 15);
            sim.Step(Press(0, 15, 0, target));
            Idle(sim, 9);
            sim.Entities.Position[target] = new FixVec2(Fix64.Ratio(15, 10), Fix64.One);
            Idle(sim, 2);
            sim.Entities.Position[target] = new FixVec2(Fix64.Ratio(15, 10), -Fix64.One);
            int physical = 0;
            for (int i = 0; i < 20; i++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == 0 && e.DamageKind == DamageType.Physical) physical++;
            }
            Assert.AreEqual(1, physical);
        }

        [Test]
        public void CleaveRollDeathAndNewArenaClearAction()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var sim = Arena(AbilityDefinition.Cleave());
                int target = Enemy(sim, 15);
                sim.Step(Press(0, 15, 0, target));
                if (mode == 0)
                {
                    sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
                    sim.Step(Press(4, -15));
                }
                else if (mode == 1) { sim.Entities.Alive[0] = false; sim.Step(InputFrame.Empty); }
                else sim.SetupTestArena(0);
                Assert.IsFalse(sim.CleaveActive);
                Assert.AreEqual(-1, sim.CleavePresentationTarget);
            }
        }

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
        public void CleaveBlazeAddsOneExistingFireBonus()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            sim.SetAbility(1, AbilityDefinition.Blaze(), new AbilityNode[0], 0);
            int target = Enemy(sim, 15);
            sim.Step(Press(1, 15));
            sim.Step(Press(0, 15, 0, target));
            int physical = 0, fire = 0;
            for (int i = 0; i < 30; i++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == 0)
                    { if (e.DamageKind == DamageType.Fire) fire++; else physical++; }
            }
            Assert.AreEqual(1, physical);
            Assert.AreEqual(1, fire);
        }

        [Test]
        public void CleaveLocksFacingAtSwingAndRejectsRearSoftTarget()
        {
            var sim = Arena(AbilityDefinition.Cleave());
            int target = Enemy(sim, 15);
            sim.Step(Press(0, 15, 0, target));
            Idle(sim, 9);
            var facing = sim.Entities.Facing[0];
            sim.Entities.Position[target] = new FixVec2(Fix64.FromInt(-2), Fix64.Zero);
            Idle(sim, 14);
            Assert.AreEqual(facing, sim.Entities.Facing[0]);
            Assert.AreEqual(10000, Health(sim, target));
            sim = Arena(AbilityDefinition.Cleave());
            Enemy(sim, -10);
            sim.Step(Press(0, 20));
            Assert.IsTrue(sim.CleaveActive);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void CleaveRepeatedCastsAreIndependentOfRenderBatching(int ticksPerFrame)
        {
            var a = Arena(AbilityDefinition.Cleave());
            var b = Arena(AbilityDefinition.Cleave());
            int target = Enemy(a, 15);
            Enemy(b, 15);
            int contacts = 0;
            for (int frame = 0; frame < 120; frame += ticksPerFrame)
                for (int sub = 0; sub < ticksPerFrame && frame + sub < 120; sub++)
                {
                    int tick = frame + sub;
                    var input = tick == 0 || tick == 90 ? Press(0, 15, 0, target) : InputFrame.Empty;
                    a.Step(input); b.Step(input);
                    Assert.AreEqual(a.StateHash(), b.StateHash());
                    foreach (var e in a.Events)
                        if (e.Type == SimEventType.Damage && e.Source == 0 && e.DamageOrigin == DamageOrigin.Ability) contacts++;
                }
            Assert.AreEqual(2, contacts);
            Assert.IsFalse(a.CleaveActive);
        }

        [Test]
        public void BlazeLastsThreeSecondsAndEnds()
        {
            var sim = Arena(AbilityDefinition.Blaze());
            sim.Step(Press(0, 20));
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
        /// Добавка огнём относится ко ВСЕМ атакам. Проверяется на способности,
        /// а не на автоатаке: именно это отличает диздок от «усиления автоатаки».
        /// </summary>
        [Test]
        public void BlazeAddsFireDamageToAbilities()
        {
            int plain;
            {
                var sim = Arena(AbilityDefinition.Whirlwind());
                int victim = Enemy(sim, 15);
                sim.Step(Press(0, 20));
                Idle(sim, 16);
                plain = 10000 - Health(sim, victim);
            }

            var lit = Arena(AbilityDefinition.Whirlwind());
            lit.SetAbility(1, AbilityDefinition.Blaze(), new AbilityNode[0], 0);
            int burned = Enemy(lit, 15);

            lit.Step(Press(1, 20));
            lit.Step(Press(0, 20));
            Idle(lit, 16);

            Assert.Greater(10000 - Health(lit, burned), plain,
                "под усилением урон не вырос");
        }

        /// <summary>Урон по времени уклонением не гасится и добавку не получает.</summary>
        [Test]
        public void BlazeBonusIsASingleExtraHitNotABurn()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            sim.SetAbility(1, AbilityDefinition.Blaze(), new AbilityNode[0], 0);
            int victim = Enemy(sim, 15);

            sim.Step(Press(1, 20));
            sim.Step(Press(0, 20));
            Idle(sim, 16);
            int afterHit = Health(sim, victim);

            Idle(sim, 60);
            Assert.AreEqual(afterHit, Health(sim, victim), "цель продолжила гореть — это не поджиг");
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

        /// <summary>Стоящий в стороне не задет ни взрывом, ни лужей.</summary>
        [Test]
        public void FlaskSparesEnemiesOutsideThePool()
        {
            var sim = Arena(AbilityDefinition.FireFlask());
            int aside = Enemy(sim, 40, 40);

            sim.Step(Press(0, 40));
            Idle(sim, 120);

            Assert.AreEqual(10000, Health(sim, aside));
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

        /// <summary>Без второго нажатия серия обрывается — она не играет сама.</summary>
        [Test]
        public void WreckStopsWithoutTheSecondPress()
        {
            var sim = Arena(AbilityDefinition.Wreck());
            int victim = Enemy(sim, 15);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int afterFirst = Health(sim, victim);

            Idle(sim, 40);
            Assert.AreEqual(afterFirst, Health(sim, victim), "серия доиграла без игрока");
            Assert.AreEqual(0, sim.WreckStage, "комбо не сброшено");
        }

        /// <summary>Завершающий удар тяжелее первого: прерывать серию должно быть жаль.</summary>
        [Test]
        public void WreckFinisherHitsHarderThanTheFirst()
        {
            var sim = Arena(AbilityDefinition.Wreck());
            int victim = Enemy(sim, 15);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int first = 10000 - Health(sim, victim);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int beforeFinisher = Health(sim, victim);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            int finisher = beforeFinisher - Health(sim, victim);

            Assert.Greater(finisher, first);
        }

        /// <summary>Кулдаун отсчитывается от конца серии, а не от первого нажатия.</summary>
        [Test]
        public void WreckCooldownStartsAfterTheCombo()
        {
            var sim = Arena(AbilityDefinition.Wreck());
            Enemy(sim, 15);

            sim.Step(Press(0, 30));
            Idle(sim, 8);
            sim.Step(Press(0, 30));
            Idle(sim, 8);
            sim.Step(Press(0, 30));
            Idle(sim, 8);

            int cooldown = AbilityDefinition.Wreck().GetBase(AbilityStatType.CooldownTicks).ToInt();
            Assert.Greater(sim.AbilityReadyTick(0), sim.Tick + cooldown - 12,
                "кулдаун начался раньше конца серии");
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

        /// <summary>
        /// Идёт на полную дальность, а не до курсора: деш отвечает на «уйти
        /// отсюда», и короткий рывок из-за близкого курсора — случайность,
        /// а не решение игрока.
        /// </summary>
        [Test]
        public void DashCoversFullRangeEvenWithCloseAim()
        {
            var sim = Arena(AbilityDefinition.Dash());
            sim.Step(Press(0, 3));
            Idle(sim, 12);

            Fix64 range = AbilityDefinition.Dash().GetBase(AbilityStatType.Radius);
            Assert.IsTrue(sim.Entities.Position[Simulation.PlayerId].X > range - Fix64.Ratio(3, 10),
                "деш остановился у курсора вместо полной дальности");
        }

        /// <summary>Никого не бьёт: урона у деша нет по диздоку.</summary>
        [Test]
        public void DashDamagesNobody()
        {
            var sim = Arena(AbilityDefinition.Dash());
            int bystander = Enemy(sim, 15);

            sim.Step(Press(0, 30));
            Idle(sim, 12);

            Assert.AreEqual(10000, Health(sim, bystander));
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
