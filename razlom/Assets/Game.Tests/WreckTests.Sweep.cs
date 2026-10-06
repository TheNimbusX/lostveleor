using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Махи по ходу головы (06.10, Simulation.Wreck.Sweep): каждого в секторе бьёт тик, когда
    /// голова проходит его угол; мах 1 справа налево, мах 2 слева направо (сабельный ритм v4 06.10).
    /// Углы — градусы от взгляда, + — справа (взгляд +X: справа — −Y). Таблица — пока запечки
    /// Swing1_w7 / Swing2_w14 клипов v3, у маха 2 — зеркально; удары 5 / 11 (v4).
    /// </summary>
    public partial class WreckTests
    {
        /// <summary>Хранитель на distance под углом degrees справа от взгляда +X.</summary>
        private static int GuardianAt(Simulation sim, double distance, double degrees, int hp = 100000)
        {
            double rad = degrees * System.Math.PI / 180;
            return Guardian(sim, distance * System.Math.Cos(rad), -distance * System.Math.Sin(rad), hp);
        }

        private static FixVec2 Toward(double degrees, double distance = 2.5)
        {
            double rad = degrees * System.Math.PI / 180;
            return new FixVec2(M(distance * System.Math.Cos(rad)), M(-distance * System.Math.Sin(rad)));
        }

        // ---- таблица угол → сдвиг от контакта ----

        [TestCase(0, 72.5, -3)] [TestCase(0, 70.2, -3)] [TestCase(0, 70.0, -2)] [TestCase(0, 44.4, -2)] [TestCase(0, 44.2, -1)]
        [TestCase(0, 20.3, -1)] [TestCase(0, 20.1, 0)] [TestCase(0, 0, 0)] [TestCase(0, -1.2, 0)] [TestCase(0, -1.4, 1)]
        [TestCase(0, -21.3, 1)] [TestCase(0, -21.5, 2)] [TestCase(0, -40.5, 2)] [TestCase(0, -40.7, 3)]
        [TestCase(0, -59.2, 3)] [TestCase(0, -59.5, 4)] [TestCase(0, -72.5, 4)]
        [TestCase(1, -72.5, -3)] [TestCase(1, -67.7, -3)] [TestCase(1, -67.5, -2)] [TestCase(1, -43.1, -2)] [TestCase(1, -42.9, -1)]
        [TestCase(1, -20.4, -1)] [TestCase(1, -20.2, 0)] [TestCase(1, 0, 0)] [TestCase(1, 0.6, 0)] [TestCase(1, 0.8, 1)]
        [TestCase(1, 20.9, 1)] [TestCase(1, 21.1, 2)] [TestCase(1, 41.0, 2)] [TestCase(1, 41.2, 3)]
        [TestCase(1, 61.2, 3)] [TestCase(1, 61.5, 4)] [TestCase(1, 72.5, 4)]
        [TestCase(0, 120, -3)] [TestCase(0, -120, 4)] [TestCase(1, -120, -3)] [TestCase(1, 120, 4)]
        public void SweepTable_AngleToTickOffset(int stage, double degrees, int offset)
        {
            var facing = new FixVec2(Fix64.One, Fix64.Zero);
            Assert.AreEqual(offset, Simulation.WreckSweepOffset(stage, facing, Toward(degrees)));
            Assert.AreEqual(0, Simulation.WreckSweepOffset(stage, facing, FixVec2.Zero), "тело в центре — в тик контакта");
        }

        [Test]
        public void SweepTable_TurnsWithTheStageDirection()
        {
            // Взгляд +Y: справа от него +X — справа налево голова идёт от +X к −X.
            var up = new FixVec2(Fix64.Zero, Fix64.One);
            Assert.AreEqual(-3, Simulation.WreckSweepOffset(0, up, new FixVec2(M(2.4), M(0.5))));
            Assert.AreEqual(0, Simulation.WreckSweepOffset(0, up, new FixVec2(M(0.1), M(2.5))));
            Assert.AreEqual(4, Simulation.WreckSweepOffset(0, up, new FixVec2(M(-2.4), M(0.5))));
        }

        // ---- удары в игре ----

        private static List<int> HitTicks(Simulation sim, int id, int ticks, System.Func<int, bool> press)
        {
            var at = new List<int>();
            DamageTo(sim, id, ticks, press, at);
            return at;
        }

        /// <summary>Первый мах (контакт 5): справа — раньше, слева — позже; весь сектор — тики 2…9.</summary>
        [TestCase(72, 2)] [TestCase(60, 3)] [TestCase(30, 4)] [TestCase(10, 5)] [TestCase(0, 5)]
        [TestCase(-10, 6)] [TestCase(-30, 7)] [TestCase(-50, 8)] [TestCase(-70, 9)]
        public void Swing1_HitsWhenTheHeadPassesTheAngle(double degrees, int tick)
        {
            var sim = Arena();
            int foe = GuardianAt(sim, 2.2, degrees);
            CollectionAssert.AreEqual(new[] { tick }, HitTicks(sim, foe, 16, i => i == 0));
        }

        /// <summary>
        /// Второй мах (нажатие 6, контакт 11) — слева направо: тики 8…15. Слева на 70° голова маха 1 дошла бы в 9,
        /// но мах 2 входит в сектор в 8 — остаток маха 1 добивается разом (8).
        /// </summary>
        [TestCase(72, 2, 15)] [TestCase(60, 3, 14)] [TestCase(30, 4, 13)] [TestCase(10, 5, 12)]
        [TestCase(-10, 6, 11)] [TestCase(-30, 7, 10)] [TestCase(-50, 8, 9)] [TestCase(-70, 8, 8)]
        public void Swing2_SweepsLeftToRight(double degrees, int first, int second)
        {
            var sim = Arena();
            int foe = GuardianAt(sim, 2.2, degrees);
            CollectionAssert.AreEqual(new[] { first, second }, HitTicks(sim, foe, 30, i => i == 0 || i == 6));
            Assert.AreEqual(-1, sim.Wreck.Side, "второй мах — слева направо");
        }

        [Test]
        public void SweepWindow_IsOpenToTheView()
        {
            var sim = Arena();
            Assert.IsFalse(sim.TryGetWreckSweep(out _, out _, out _, out _, out _));
            for (int i = 0; i < 12; i++) sim.Step(Press(i == 0));
            Assert.IsTrue(sim.TryGetWreckSweep(out int stage, out int serial, out int contact, out int from, out int to));
            CollectionAssert.AreEqual(new[] { 0, 1, 5, 2, 9 }, new[] { stage, serial, contact, from, to });
        }

        [Test]
        public void SecondPressRightAfterTheFirstStrike_FirstSweepIsFlushedNotCut()
        {
            var sim = Arena();
            int left = GuardianAt(sim, 2.2, -70);
            // Нажатия каждый тик: второй этап с тика 6, мах 2 входит в сектор в 8 и первым бьёт левый край (8);
            // голова маха 1 дошла бы туда в 9 — её остаток добивается разом в 8, удар не теряется.
            CollectionAssert.AreEqual(new[] { 8, 8 }, HitTicks(sim, left, 30, i => i < 9));
        }

        [Test]
        public void DamageAndSectorUnchanged_EveryoneInTheSectorOnce()
        {
            var sim = Arena();
            int[] foes = { GuardianAt(sim, 3.4, 60), GuardianAt(sim, 1.6, 30), GuardianAt(sim, 3.4, -20), GuardianAt(sim, 1.6, -50) };
            int outside = GuardianAt(sim, 2.5, 100);
            var dealt = new int[foes.Length];
            var at = new int[foes.Length];
            for (int i = 0; i < 16; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i == 0));
                foreach (var e in sim.Events)
                    for (int k = 0; k < foes.Length; k++)
                        if (e.Type == SimEventType.Damage && e.Target == foes[k]) { dealt[k] += e.Amount; at[k] = tick; }
                foreach (var e in sim.Events)
                    Assert.IsFalse(e.Type == SimEventType.Damage && e.Target == outside, "за краем сектора не бьёт");
            }
            CollectionAssert.AreEqual(new[] { 70, 70, 70, 70 }, dealt);
            CollectionAssert.AreEqual(new[] { 3, 4, 6, 8 }, at, "справа налево");
        }

        [Test]
        public void DashBeforeTheContact_StopsTheHeadWhereItWas()
        {
            var sim = Arena();
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            int right = GuardianAt(sim, 2.2, 72), left = GuardianAt(sim, 2.2, -50);
            int toRight = 0, toLeft = 0;
            for (int i = 0; i < 16; i++)
            {
                sim.Step(Slots(i == 0 ? 1 : i == 4 ? 1 << 4 : 0));
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.Damage) continue;
                    if (e.Target == right) toRight += e.Amount;
                    if (e.Target == left) toLeft += e.Amount;
                }
            }
            Assert.AreEqual(70, toRight, "справа голова прошла в тик 2");
            Assert.AreEqual(0, toLeft, "до левого края мах не дошёл");
        }

        [Test]
        public void AbilitySpeed_CompressesTheWindupPartOnly()
        {
            var sim = Arena();
            sim.Entities.Stats[P].SetBase(StatType.AbilitySpeed, Fix64.One);
            int right = GuardianAt(sim, 2.2, 72);
            int contact = sim.AbilityExecutionTicks(Simulation.WreckSwingWindupTicks);
            int baseWindup = Simulation.WreckSwingWindupTicks;
            CollectionAssert.AreEqual(new[] { contact - (3 * contact + baseWindup / 2) / baseWindup }, HitTicks(sim, right, 16, i => i == 0));
            Assert.AreEqual(contact, sim.Wreck.ContactTick);
            var again = Arena();
            again.Entities.Stats[P].SetBase(StatType.AbilitySpeed, Fix64.One);
            int far = GuardianAt(again, 2.2, -70);
            CollectionAssert.AreEqual(new[] { contact + 4 }, HitTicks(again, far, 16, i => i == 0), "проводка — без темпа");
        }

        // ---- детерминизм ----

        private static List<string> SweepRun()
        {
            var sim = Arena(Talent(6));   // «Сотрясение»: второй мах ещё и оглушает
            for (int k = 0; k < 9; k++) GuardianAt(sim, k % 2 == 0 ? 1.7 : 3.3, 72 - k * 18, 2000);
            var log = new List<string>();
            for (int i = 0; i < 48; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 24, 6, i * 0.05));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage || e.Type == SimEventType.Stun || e.Type == SimEventType.WreckStage)
                        log.Add($"{tick}:{e.Type}:{e.Target}:{e.Amount}");
                log.Add(sim.StateHash().ToString("X16"));
            }
            return log;
        }

        [Test]
        public void SweepRunTwice_IdenticalEventsAndHashes()
            => CollectionAssert.AreEqual(SweepRun(), SweepRun());
    }
}
