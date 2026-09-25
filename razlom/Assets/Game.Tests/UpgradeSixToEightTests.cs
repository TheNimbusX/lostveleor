using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Усиления 6–8 каждой линии — выбор владельца 24 сентября. Как в тестах талантов:
    /// одна сцена с усилением и без, проверяется разница, которую оно даёт. Усиление
    /// включается одно, без остальных линии — они берутся в любом порядке.
    /// </summary>
    public class UpgradeSixToEightTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            return sim;
        }

        /// <summary>Способность линии в слот 0; with — с одним усилением index, иначе голая.</summary>
        private static Simulation With(SabreTalentLine line, int index, bool with)
        {
            var sim = Arena();
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = with ? SabreTalents.AppendNode(line, index, buffer, 0) : 0;
            sim.SetAbility(0, PelagKit.PoolDefinition(SabreTalents.PoolIndexOf(line)), buffer, count);
            return sim;
        }

        private static int Enemy(Simulation sim, float x, float y, int health = 10000)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio((int)(x * 1000), 1000), Fix64.Ratio((int)(y * 1000), 1000)),
                health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Press(int slot, float aimX = 2, float aimY = 0, int target = -1)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.Ratio((int)(aimX * 1000), 1000), Fix64.Ratio((int)(aimY * 1000), 1000));
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        private static int Lost(Simulation sim, int id, int health = 10000) => health - sim.Entities.Health[id];

        // ---- числа ----

        [Test]
        public void NumericUpgrades()
        {
            Assert.AreEqual(6f, With(SabreTalentLine.AnchorSlam, 6, true).GetAbility(0).Get(AbilityStatType.Radius).ToFloat(), .001f, "Дальний удар");
            Assert.AreEqual(10f, With(SabreTalentLine.Flask, 6, true).GetAbility(0).Get(AbilityStatType.Radius).ToFloat(), .001f, "Дальний бросок");
        }

        [Test]
        public void EveryLineHasEightUpgrades()
        {
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            for (int line = 0; line < SabreTalents.LineCount; line++)
                for (int index = 0; index < SabreTalents.TalentsPerLine; index++)
                    Assert.AreEqual(1, SabreTalents.AppendNode((SabreTalentLine)line, index, buffer, 0), "у линии " + line + " нет усиления " + (index + 1));
        }

        // ---- Вихрь ----

        [Test]
        public void WhirlwindPullDragsFarEnemyIntoTheSpin()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Whirlwind, 5, with);
                int far = Enemy(sim, 3.2f, 0);
                sim.Step(Press(0));
                Idle(sim, 20);
                return Lost(sim, far);
            }

            Assert.AreEqual(0, Hit(false), "враг в 3,2 м задет без усиления");
            Assert.Greater(Hit(true), 0, "затягивание не притянуло врага под удар");
        }

        [Test]
        public void WhirlwindWaveHitsBeyondTheSpin()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Whirlwind, 6, with);
                int far = Enemy(sim, 3.5f, 0);
                sim.Step(Press(0));
                Idle(sim, 25);
                return Lost(sim, far);
            }

            Assert.AreEqual(0, Hit(false));
            Assert.Greater(Hit(true), 0, "кольцо не дошло до 3,5 м");
        }

        [Test]
        public void WhirlwindCocoonCutsIncomingDamage()
        {
            int Taken(bool with)
            {
                var sim = With(SabreTalentLine.Whirlwind, 7, with);
                int foe = Enemy(sim, 6f, 0);
                sim.Step(Press(0));
                Idle(sim, 3);
                int before = sim.Entities.Health[Simulation.PlayerId];
                sim.ApplyAbilityDamage(foe, Simulation.PlayerId, 100, -1, DamageType.Physical);
                return before - sim.Entities.Health[Simulation.PlayerId];
            }

            Assert.Less(Taken(true), Taken(false), "кокон не уменьшил урон по герою");
        }

        // ---- Рассекающий удар ----

        [Test]
        public void CleaveSunderAmplifiesTheNextHit()
        {
            int Next(bool with)
            {
                var sim = With(SabreTalentLine.Cleave, 5, with);
                int foe = Enemy(sim, 1f, 0);
                sim.Step(Press(0));
                Idle(sim, 30);
                int before = sim.Entities.Health[foe];
                sim.ApplyAbilityDamage(Simulation.PlayerId, foe, 100, 0, DamageType.Physical);
                return before - sim.Entities.Health[foe];
            }

            Assert.Greater(Next(true), Next(false), "раскол брони не усилил следующий удар");
        }

        [Test]
        public void CleaveDoubleStrikesAgain()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Cleave, 6, with);
                int foe = Enemy(sim, 1f, 0);
                sim.Step(Press(0));
                Idle(sim, 40);
                return Lost(sim, foe);
            }

            int single = Hit(false);
            Assert.Greater(single, 0);
            Assert.AreEqual(single + single / 2, Hit(true), 2, "второй удар не прошёл или не половина");
        }

        [Test]
        public void CleaveWaveReachesPastTheBlade()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Cleave, 7, with);
                int far = Enemy(sim, 3.5f, 0);
                sim.Step(Press(0));
                Idle(sim, 30);
                return Lost(sim, far);
            }

            Assert.AreEqual(0, Hit(false));
            Assert.Greater(Hit(true), 0, "волна клинка не задела врага в 3,5 м");
        }

        // ---- «Ладно смазал» ----

        [Test]
        public void BlazeFlareBurnsAroundOnIgnition()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Blaze, 5, with);
                int foe = Enemy(sim, 1.5f, 0);
                sim.Step(Press(0));
                Idle(sim, 45);
                return Lost(sim, foe);
            }

            Assert.AreEqual(0, Hit(false));
            Assert.Greater(Hit(true), 0, "вспышка при поджоге не ударила");
        }

        [Test]
        public void BlazeHasteSpeedsUpAttacksWhileBurning()
        {
            float Speed(bool with)
            {
                var sim = With(SabreTalentLine.Blaze, 6, with);
                sim.Step(Press(0));
                Idle(sim, 45);
                Assert.IsTrue(sim.BlazeActive);
                return sim.Entities.Stats[Simulation.PlayerId].Get(StatType.AttackSpeed).ToFloat();
            }

            Assert.AreEqual(Speed(false) * 1.2f, Speed(true), .01f, "жар клинка не дал +20% к скорости атак");
        }

        [Test]
        public void BlazeStokeExtendsTheFireOnKill()
        {
            bool BurningLate(bool with)
            {
                var sim = With(SabreTalentLine.Blaze, 7, with);
                int weak = Enemy(sim, 4f, 0, health: 5);
                sim.Step(Press(0));
                Idle(sim, 40);
                sim.ApplyAbilityDamage(Simulation.PlayerId, weak, 100, 0, DamageType.Physical);
                Assert.IsFalse(sim.Entities.Alive[weak]);
                // Огонь зажёгся на 36-м тике и горит 90 — к 130-му без усиления уже погас.
                Idle(sim, 130 - 41);
                return sim.BlazeActive;
            }

            Assert.IsFalse(BurningLate(false));
            Assert.IsTrue(BurningLate(true), "убийство не продлило огонь");
        }

        // ---- Шквал ----

        private static int Squall(bool with, int index, out Simulation sim)
        {
            sim = With(SabreTalentLine.Squall, index, with);
            int foe = Enemy(sim, 3f, 0);
            sim.Step(Press(0, 3, 0, foe));
            Idle(sim, 40);
            return Lost(sim, foe);
        }

        [Test]
        public void SquallOpenerDoublesTheFirstHop() => Assert.Greater(Squall(true, 7, out _), Squall(false, 7, out _));

        [Test]
        public void SquallRepeatHitsTheSameTargetHarder() => Assert.Greater(Squall(true, 6, out _), Squall(false, 6, out _));

        [Test]
        public void SquallReturnBringsPelagBack()
        {
            Squall(false, 5, out Simulation stays);
            Squall(true, 5, out Simulation back);
            float Away(Simulation sim) => sim.Entities.Position[Simulation.PlayerId].Length.ToFloat();
            Assert.Greater(Away(stays), 1f, "без возврата Пелаг остался на старте");
            Assert.Less(Away(back), .3f, "возврат не привёл Пелага на место старта");
        }

        // ---- Удар якорем ----

        [Test]
        public void SlamCrackStunsWhoeverStepsOnIt()
        {
            bool Stunned(bool with)
            {
                var sim = With(SabreTalentLine.AnchorSlam, 5, with);
                int late = Enemy(sim, 2f, 3f);
                sim.Step(Press(0));
                Idle(sim, 30);
                sim.Entities.Position[late] = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
                Idle(sim, 2);
                return sim.Statuses.IsStunned(late, sim.Tick);
            }

            Assert.IsFalse(Stunned(false));
            Assert.IsTrue(Stunned(true), "трещина не оглушила вставшего на неё");
        }

        [Test]
        public void SlamRecoilShortensTheCooldown()
        {
            int Ready(bool with)
            {
                var sim = With(SabreTalentLine.AnchorSlam, 7, with);
                Enemy(sim, 1.5f, 0);
                Enemy(sim, 2.5f, 0);
                sim.Step(Press(0));
                Idle(sim, 25);
                return sim.AbilityReadyTick(0);
            }

            Assert.AreEqual(Ready(false) - 18, Ready(true), "два задетых не сократили перезарядку на 0,6 с");
        }

        // ---- Крушение ----

        private static Simulation Wreck(int index, bool with, out int foe)
        {
            var sim = With(SabreTalentLine.Wreck, index, with);
            foe = Enemy(sim, 1.5f, 0);
            return sim;
        }

        [Test]
        public void WreckMomentumGrowsEachStrike()
        {
            int Total(bool with)
            {
                var sim = Wreck(7, with, out int foe);
                for (int p = 0; p < 3; p++)
                {
                    sim.Step(Press(0));
                    Idle(sim, 10);
                }
                Idle(sim, 40);
                return Lost(sim, foe);
            }

            Assert.AreEqual(70 + 70 + 140, Total(false));
            Assert.AreEqual(70 + 80 + 182, Total(true), "раскрутка: 70, +15%, +30%");
        }

        [Test]
        public void WreckConcussStunsOnTheSecondStrike()
        {
            bool StunnedAfterSecond(bool with)
            {
                var sim = Wreck(6, with, out int foe);
                sim.Step(Press(0));
                Idle(sim, 10);
                sim.Step(Press(0));
                Idle(sim, 8);
                return sim.Statuses.IsStunned(foe, sim.Tick);
            }

            Assert.IsFalse(StunnedAfterSecond(false));
            Assert.IsTrue(StunnedAfterSecond(true), "второй удар не оглушил");
        }

        [Test]
        public void WreckUnstoppableCutsIncomingDamage()
        {
            int Taken(bool with)
            {
                var sim = Wreck(5, with, out int foe);
                sim.Step(Press(0));
                Idle(sim, 3);
                int before = sim.Entities.Health[Simulation.PlayerId];
                sim.ApplyAbilityDamage(foe, Simulation.PlayerId, 100, -1, DamageType.Physical);
                return before - sim.Entities.Health[Simulation.PlayerId];
            }

            Assert.Less(Taken(true), Taken(false), "неудержимый не уменьшил урон по герою");
        }

        // ---- Абордаж ----

        [Test]
        public void BoardingMomentumHitsHarderFromAfar()
        {
            int Fist(bool with)
            {
                var sim = With(SabreTalentLine.Boarding, 5, with);
                int foe = Enemy(sim, 6f, 0);
                sim.Step(Press(0, 6, 0, foe));
                Idle(sim, 60);
                return Lost(sim, foe);
            }

            Assert.Greater(Fist(true), Fist(false), "разгон не усилил кулак после полёта");
        }

        [Test]
        public void BoardingInterruptCancelsTheWindup()
        {
            bool Cancelled(bool with)
            {
                var sim = With(SabreTalentLine.Boarding, 6, with);
                int foe = Enemy(sim, 5f, 0);
                sim.Entities.PendingAttackTarget[foe] = Simulation.PlayerId;
                sim.Entities.AttackImpactTick[foe] = 100000;
                sim.Step(Press(0, 5, 0, foe));
                Idle(sim, 18);
                return sim.Entities.PendingAttackTarget[foe] == -1;
            }

            Assert.IsFalse(Cancelled(false));
            Assert.IsTrue(Cancelled(true), "зацеп не сбил замах");
        }

        // ---- Взрывная смесь ----

        [Test]
        public void FlaskTwoChargesThrowsAgainAtOnce()
        {
            int Wait(bool with)
            {
                var sim = With(SabreTalentLine.Flask, 5, with);
                sim.Step(Press(0, 4, 0));
                return sim.AbilityReadyTick(0) - sim.Tick;
            }

            Assert.Less(Wait(true), Wait(false) / 3, "второй заряд не вернул кнопку сразу");
        }

        [Test]
        public void FlaskShrapnelHitsBeyondTheBlast()
        {
            int Hit(bool with)
            {
                var sim = With(SabreTalentLine.Flask, 7, with);
                int far = Enemy(sim, 4f, 3f);
                sim.Step(Press(0, 4, 0));
                Idle(sim, 30);
                return Lost(sim, far);
            }

            Assert.AreEqual(0, Hit(false));
            Assert.Greater(Hit(true), 0, "осколок не долетел");
        }
    }
}
