using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Крушение v2 (03.10, artifacts/wreck/plan/SPEC.md 2.10): мах влево, мах вправо и выпад
    /// с валом; сроки v4 06.10 (удары 5 / 11 / 20) — сабельный ритм по клипам v4, SPEC 2.2.
    /// Хранитель — тело 0,85, герой в нуле, взгляд +X. Сроки — тики Sim.
    /// </summary>
    public partial class WreckTests
    {
        internal const int P = Simulation.PlayerId;

        internal static Fix64 M(double v) => Fix64.Ratio((int)System.Math.Round(v * 1000), 1000);

        internal static Simulation Arena(params AbilityNode[] nodes)
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.Entities.Position[P] = FixVec2.Zero;
            sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
            sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, nodes.Length);
            return sim;
        }

        internal static AbilityNode[] Form(PelagForm form)
        {
            var buffer = new AbilityNode[4];
            int count = PelagForms.AppendFormNodes(form, buffer, 0);
            System.Array.Resize(ref buffer, count);
            return buffer;
        }

        internal static AbilityNode[] Talent(int index)
        {
            var buffer = new AbilityNode[2];
            int count = SabreTalents.AppendNode(SabreTalentLine.Wreck, index, buffer, 0);
            System.Array.Resize(ref buffer, count);
            return buffer;
        }

        internal static int Guardian(Simulation sim, double x, double y, int hp = 100000)
        {
            int id = sim.Entities.Spawn(new FixVec2(M(x), M(y)), hp, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = EnemyArchetypes.GuardianBodyRadius;
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        internal static InputFrame Press(bool press, double aimX = 5, double aimY = 0, bool hold = false, bool walk = false)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(press ? 1 : 0);
            input.AbilityHoldMask = (byte)(press || hold ? 1 : 0);
            input.Aim = new FixVec2(M(aimX), M(aimY));
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            if (walk) input.Flags = (byte)InputFlags.MoveOrder;
            return input;
        }

        /// <summary>Самый быстрый темп: нажатие каждый тик, пока идут три удара.</summary>
        internal static void Fastest(Simulation sim, int ticks, bool walk = false)
        {
            for (int i = 0; i < ticks; i++) sim.Step(Press(i < 20, walk ? 30 : 5, 0, walk: walk));
        }

        internal static List<int> StrikeTicks(Simulation sim, int ticks, System.Func<int, bool> press)
        {
            var strikes = new List<int>();
            for (int i = 0; i < ticks; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(press(i)));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.WreckStage) strikes.Add(tick);
            }
            return strikes;
        }

        internal static int DamageTo(Simulation sim, int id, int ticks, System.Func<int, bool> press, List<int> at = null)
        {
            int total = 0;
            for (int i = 0; i < ticks; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(press(i)));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Target == id) { total += e.Amount; at?.Add(tick); }
            }
            return total;
        }

        // ---- 1. Таймлайн ----

        [Test]
        public void FastestSeries_StrikesAt5_11_20_WaveTo27_CycleIs116()
        {
            var sim = Arena();
            Guardian(sim, 1.8, 0);
            var strikes = StrikeTicks(sim, 21, i => i < 20);
            CollectionAssert.AreEqual(new[] { 5, 11, 20 }, strikes, "сроки v4 06.10 — контакты клипов 5 / 5 / 8");
            Assert.AreEqual(20, sim.Wreck.WaveTick, "вал — с тика выпада");
            Assert.AreEqual(8, sim.Wreck.WaveTravelTicks, "вал: (6 − 2,2) / 0,5 → 8 шагов");
            Assert.AreEqual(116, sim.AbilityReadyTick(0), "кулдаун 96 от выпада");
            while (sim.Tick < 28) sim.Step(Press(false));
            Assert.IsTrue(sim.TryGetWreckWave(out _, out _, out Fix64 from, out Fix64 reach, out _), "после шага 27 край ещё виден");
            Assert.AreEqual(2.2f, from.ToFloat(), 1e-3f);
            Assert.AreEqual(6f, reach.ToFloat(), 1e-3f, "фронт дошёл до конца полосы");
            sim.Step(Press(false));
            Assert.IsFalse(sim.TryGetWreckWave(out _, out _, out _, out _, out _), "вал кончился");
        }

        /// <summary>
        /// Буфер Tempo (6 тиков) длиннее замаха маха (5): любое второе нажатие в замахе выходит
        /// тиком после удара — в 6 (SPEC 2.10, тест 1).
        /// </summary>
        [TestCase(1)] [TestCase(3)] [TestCase(5)]
        public void SecondPressBeforeTheStrike_IsBufferedToTick6(int second)
        {
            var sim = Arena();
            var stages = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i == 0 || i == second));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.ActionStageStarted && e.ActionVariant == 1) stages.Add(tick);
            }
            CollectionAssert.AreEqual(new[] { 6 }, stages);
            Assert.AreEqual(11, sim.Wreck.ContactTick, "второй мах: 6 + 5");
        }

        /// <summary>Третье нажатие в замахе второго маха (7–11) тоже не гаснет: выпад с 12, удар 20.</summary>
        [TestCase(7)] [TestCase(11)]
        public void ThirdPressInTheSecondWindup_IsBufferedToTick12(int third)
        {
            var sim = Arena();
            var strikes = StrikeTicks(sim, 24, i => i == 0 || i == 5 || i == third);
            CollectionAssert.AreEqual(new[] { 5, 11, 20 }, strikes);
            Assert.AreEqual(12, sim.Wreck.StageStartTick);
        }

        [Test]
        public void NoPressInTheWindow_SeriesEndsWindowExpired_CooldownFromWindowEnd()
        {
            var sim = Arena();
            int ended = -1, reason = -1;
            for (int i = 0; i < 40; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i == 0));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.WreckEnded) { ended = tick; reason = e.Amount; }
            }
            Assert.AreEqual(5 + 24 + 1, ended);
            Assert.AreEqual((int)WreckEnd.WindowExpired, reason);
            Assert.AreEqual(30 + 96, sim.AbilityReadyTick(0));
            Assert.IsFalse(sim.WreckActive);
        }

        // ---- 2. Разворот ----

        /// <summary>Как серия сабли: курсор за спиной замах не удлиняет, корпус встаёт по курсору в тик нажатия.</summary>
        [TestCase(-5, 0.01)]
        [TestCase(0, 5)]
        [TestCase(5, 0)]
        public void AimBehindTheHero_NoExtraWindup_FacingSnapsAtThePress(double aimX, double aimY)
        {
            var sim = Arena();
            var strikes = new List<int>();
            for (int i = 0; i < 12; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i == 0, aimX, aimY));
                if (i == 0)
                {
                    FixVec2 facing = sim.Entities.Facing[P];
                    double length = System.Math.Sqrt(aimX * aimX + aimY * aimY);
                    Assert.AreEqual(aimX / length, facing.X.ToFloat(), 1e-3, "взгляд — по курсору сразу");
                    Assert.AreEqual(aimY / length, facing.Y.ToFloat(), 1e-3);
                }
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.WreckStage) strikes.Add(tick);
            }
            CollectionAssert.AreEqual(new[] { 5 }, strikes);
        }

        /// <summary>
        /// Каждое нажатие берёт свой курсор (из буфера — курсор в тик нажатия): махи разворачиваются,
        /// выпад бьёт туда, куда показал курсор третьего нажатия, а не куда курсор ушёл потом.
        /// </summary>
        [Test]
        public void EachPressTakesItsCursor_LungeGoesWhereTheThirdPressPointed()
        {
            var sim = Arena();
            for (int i = 0; i < 22; i++)
            {
                InputFrame input;
                if (i == 0) input = Press(true, 5, 0);
                else if (i == 3) input = Press(true, 0, 5);          // в буфер: мах 2 — вверх (+Y)
                else if (i == 9) input = Press(true, -5, 0.01);      // в буфер: выпад — назад (−X)
                else input = Press(false, 0, -5);                    // курсор между нажатиями — вниз
                int tick = sim.Tick;
                sim.Step(input);
                var w = sim.Wreck;
                if (tick == 6)
                {
                    Assert.AreEqual(1, w.Stage);
                    Assert.AreEqual(1f, w.Direction.Y.ToFloat(), 1e-3f, "мах 2 — по курсору нажатия 3");
                }
                if (tick == 12)
                {
                    Assert.AreEqual(2, w.Stage);
                    Assert.AreEqual(-1f, w.Direction.X.ToFloat(), 1e-3f, "выпад — по курсору третьего нажатия");
                    Assert.AreEqual(20, w.ContactTick, "разворот назад замах не удлиняет");
                }
            }
            Assert.AreEqual(-0.6f, sim.Entities.Position[P].X.ToFloat(), 1e-3f, "выпад v4 — шаг 0,6 м по курсору третьего нажатия");
            Assert.AreEqual(-2.8f, sim.Wreck.ImpactPoint.X.ToFloat(), 1e-3f, "точка выпада — 2,2 м перед героем после шага, за спиной прежнего взгляда");
            Assert.AreEqual(-1f, sim.Wreck.LaneDir.X.ToFloat(), 1e-3f);
        }

        // ---- 3. Махи ----

        [TestCase(3.6, 0, true)]
        [TestCase(3.6, 72, true)]
        [TestCase(3.7, 0, false)]
        [TestCase(2.0, 75, false)]
        public void Swing_HitsSector28PlusBody_Within72Degrees(double distance, double degrees, bool hit)
        {
            var sim = Arena();
            double rad = degrees * System.Math.PI / 180;
            int foe = Guardian(sim, distance * System.Math.Cos(rad), distance * System.Math.Sin(rad));
            // Слева на 72° голова проходит в контакт + 4 (Simulation.Wreck.Sweep) — тик 9, смотрим до тика 12.
            int dealt = DamageTo(sim, foe, 13, i => i == 0);
            Assert.AreEqual(hit ? 70 : 0, dealt);
        }

        [TestCase(4.2, true)]
        [TestCase(4.3, false)]
        public void WiderSwing_ReachesFourPointTwo(double distance, bool hit)
        {
            var sim = Arena(Talent(0));
            int foe = Guardian(sim, distance, 0);
            Assert.AreEqual(hit ? 70 : 0, DamageTo(sim, foe, 10, i => i == 0));
        }
    }
}
