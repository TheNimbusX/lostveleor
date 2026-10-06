using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Бросок якоря (Simulation.AnchorThrow, спека artifacts/anchor-throw/plan/SPEC.md §2.9):
    /// таймлайн, полоса, голова. Тяга и срывы — AnchorThrowPullTests, формы, полукольцо,
    /// пул и хеш — AnchorThrowFormTests. Классический NUnit (Unity старее: без Is.AnyOf).
    /// </summary>
    public sealed class AnchorThrowTests
    {
        internal const int Slot = 0;
        internal const int Health = 100000;

        internal static Simulation Arena(PelagForm form = PelagForm.None, LayoutMap map = null, ulong seed = 4321)
        {
            var sim = new Simulation(seed, 128);
            if (map == null) sim.SetupTestArena(0); else sim.SetupRift(map, seed, 0, 0, 1);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Facing[0] = new FixVec2(Fix64.One, Fix64.Zero);
            Hero(sim);
            Give(sim, form);
            return sim;
        }

        internal static void Hero(Simulation sim)
        {
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(Health));
            sim.Entities.RefreshStats(0);
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            sim.Entities.Lavidium[0] = Fix64.FromInt(sim.Entities.MaxLavidium[0]);
        }

        internal static void Give(Simulation sim, PelagForm form)
        {
            var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = PelagForms.AppendFormNodes(form, nodes, 0);
            sim.SetAbility(Slot, AbilityDefinition.AnchorThrow(), nodes, count);
        }

        /// <summary>Поляна 40×40 м (центр — герой) с камнями (x, y, r).</summary>
        internal static LayoutMap Map(params double[] rocks)
        {
            var room = new ModuleDefinition("anchor_throw.test", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }));
            map.TryPlace(0, 0, -10, -10);
            for (int k = 0; k + 2 < rocks.Length; k += 3) Rock(map, rocks[k], rocks[k + 1], rocks[k + 2]);
            return map;
        }

        internal static void Rock(LayoutMap map, double x, double y, double r)
            => map.AddTestObstacle(new LayoutObstacle(At(x, y), Fix64.FromDouble(r), 0));

        internal static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Неподвижный враг без удара (лёгкий: вид None, вес 1); тело — Хранитель 0,85 м.</summary>
        internal static int Enemy(Simulation sim, int xMm, int yMm, int radiusMm = 850)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(xMm, 1000), Fix64.Ratio(yMm, 1000)), Health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(radiusMm, 1000);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        /// <summary>Моб настоящего вида (вес, тело, правила вида), неподвижный и без удара.</summary>
        internal static int Mob(Simulation sim, EnemyKind kind, double x, double y)
        {
            int id = sim.SpawnEnemy(At(x, y), Health, kind);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Health[id] = sim.Entities.MaxHealth[id];
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            sim.Entities.Aggro[id] = false;
            return id;
        }

        internal static InputFrame Press(double x, double y, int slot = Slot)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = At(x, y);
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            return input;
        }

        internal struct Frame
        {
            public int Tick;
            public SimEvent[] Events;
            public AnchorThrowState State;
            public FixVec2[] Positions;
            public ulong Hash;
        }

        internal static List<Frame> Run(Simulation sim, int ticks, System.Func<int, InputFrame> input, System.Action<int> before = null)
        {
            var frames = new List<Frame>();
            for (int t = 0; t < ticks; t++)
            {
                before?.Invoke(t);
                int tick = sim.Tick;
                sim.Step(input(t));
                var events = new SimEvent[sim.Events.Count];
                for (int i = 0; i < events.Length; i++) events[i] = sim.Events[i];
                var positions = new FixVec2[sim.Entities.Count];
                for (int i = 0; i < positions.Length; i++) positions[i] = sim.Entities.Position[i];
                frames.Add(new Frame { Tick = tick, Events = events, State = sim.AnchorThrow, Positions = positions, Hash = sim.StateHash() });
            }
            return frames;
        }

        internal static List<Frame> Cast(Simulation sim, double x, double y, int ticks = 32, System.Action<int> before = null)
            => Run(sim, ticks, t => t == 0 ? Press(x, y) : InputFrame.Empty, before);

        internal static List<(Frame frame, SimEvent ev)> Of(List<Frame> frames, SimEventType type)
        {
            var found = new List<(Frame, SimEvent)>();
            foreach (Frame f in frames)
                foreach (SimEvent ev in f.Events)
                    if (ev.Type == type) found.Add((f, ev));
            return found;
        }

        internal static int TickOf(List<Frame> frames, SimEventType type)
        {
            var found = Of(frames, type);
            Assert.AreEqual(1, found.Count, type + " — ровно одно событие");
            return found[0].frame.Tick;
        }

        internal static int DamageTo(List<Frame> frames, int target)
        {
            int total = 0;
            foreach (var (_, ev) in Of(frames, SimEventType.Damage))
                if (ev.Target == target) total += ev.Amount;
            return total;
        }

        internal static List<(Frame frame, SimEvent ev)> HitsOf(List<Frame> frames, int target)
            => Of(frames, SimEventType.AnchorThrowHit).FindAll(h => h.ev.Target == target);

        internal static double Dist(FixVec2 a, FixVec2 b) => FixVec2.Distance(a, b).ToDouble();

        // ---------- 1. таймлайн ----------

        [Test]
        public void Timeline_SevenMetresIntoTheVoid()
        {
            Simulation sim = Arena();
            List<Frame> frames = Cast(sim, 7, 0);
            int c = frames[0].Tick;
            var release = Of(frames, SimEventType.AnchorThrowRelease)[0];
            Assert.AreEqual(c + 2, release.frame.Tick, "выпуск");
            Assert.AreEqual(7, release.ev.Amount, "F = 7");
            Assert.IsFalse(release.ev.Flag);
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.AreEqual(c + 10, yank.frame.Tick, "натяг");
            Assert.AreEqual(8, yank.ev.Amount, "R = 8");
            Assert.IsFalse(yank.ev.Flag, "полёт не оборван");
            Assert.AreEqual(c + 18, TickOf(frames, SimEventType.AnchorThrowCatch), "ловля");
            var ended = Of(frames, SimEventType.AnchorThrowEnded)[0];
            Assert.AreEqual(c + 27, ended.frame.Tick, "конец");
            Assert.AreEqual((int)AnchorThrowEnd.Done, ended.ev.Amount);
            Assert.AreEqual(c + 23, frames[21].State.ExitWalkTick, "ходьба");
            Assert.AreEqual(AnchorThrowPhase.Exit, frames[21].State.Phase);

            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(7.0, s.Reach0.ToDouble(), 1e-6);
            Assert.AreEqual(AnchorThrowStop.Full, s.StopKind0);
            FixVec2 end = s.Center + s.Dir * s.Reach0;
            Assert.AreEqual(end, Simulation.AnchorThrowHead(s, 0, c + 9), "голова в конце — тик 9");
            Assert.Less(Dist(Simulation.AnchorThrowHead(s, 0, c + 8), s.Center), 6.9);
            for (int t = 0; t < 27; t++)
                Assert.IsTrue(frames[t].Positions[0].Equals(FixVec2.Zero), "герой стоит, тик " + (c + t));
        }

        [Test]
        public void Timeline_CursorBehind_EverythingOneTickLater()
        {
            Simulation sim = Arena();
            List<Frame> frames = Cast(sim, -7, 0);
            Assert.AreEqual(3, TickOf(frames, SimEventType.AnchorThrowRelease));
            Assert.AreEqual(11, TickOf(frames, SimEventType.AnchorThrowYank));
            Assert.AreEqual(19, TickOf(frames, SimEventType.AnchorThrowCatch));
            Assert.AreEqual(28, TickOf(frames, SimEventType.AnchorThrowEnded));
            Assert.AreEqual(-1.0, sim.Entities.Facing[0].X.ToDouble(), 1e-6, "взгляд Sim — на Dir");
        }

        [Test]
        public void Timeline_WallAtFiveMetres_CatchAt14_ThrowIntoAWall_F1_R4_Nobody()
        {
            Simulation sim = Arena(map: Map(6, 0, 0.5));
            List<Frame> frames = Cast(sim, 7, 0);
            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(5.0, s.Reach0.ToDouble(), 1e-6, "последняя проходимая проба");
            Assert.AreEqual(AnchorThrowStop.Wall, s.StopKind0);
            Assert.AreEqual(5, s.Flight0);
            Assert.AreEqual(6, s.ReturnTicks);
            Assert.AreEqual(14, TickOf(frames, SimEventType.AnchorThrowCatch));
            Assert.IsTrue(Of(frames, SimEventType.AnchorThrowYank)[0].ev.Flag, "полёт оборван стеной");

            sim = Arena(map: Map(1.2, 0, 0.5));
            int near = Enemy(sim, 600, -900, radiusMm: 450);
            frames = Cast(sim, 7, 0);
            s = sim.AnchorThrow;
            Assert.AreEqual(1, s.Flight0, "бросок в стену: F = 1");
            Assert.AreEqual(4, s.ReturnTicks, "R = 4");
            Assert.AreEqual(8, TickOf(frames, SimEventType.AnchorThrowCatch));
            Assert.AreEqual(0, Of(frames, SimEventType.AnchorThrowHit).Count, "никого");
            Assert.AreEqual(0, DamageTo(frames, near));
        }

        // ---------- 2. полоса ----------

        [Test]
        public void Lane_GuardianOnTheAxisAtFourMetres_HitOnTick5_Once()
        {
            Simulation sim = Arena();
            int g = Enemy(sim, 4000, 0);
            List<Frame> frames = Cast(sim, 7, 0);
            var hits = HitsOf(frames, g);
            Assert.AreEqual(1, hits.Count, "один раз за бросок");
            Assert.AreEqual(5, hits[0].frame.Tick);
            Assert.AreEqual(0, hits[0].ev.Amount, "полоса якоря");
            Assert.IsTrue(hits[0].ev.Flag, "лёгкий — на тягу");
            Assert.AreEqual(4.0 - 0.85, hits[0].ev.Position.X.ToDouble(), 1e-3, "точка касания — фронт тела");
            Assert.AreEqual(60, DamageTo(frames, g));
        }

        [Test]
        public void Lane_Across_1_30_Hits_1_35_DoesNot_NobodyBehindAWall()
        {
            Simulation sim = Arena(map: Map(6, 0, 0.5));
            int edge = Enemy(sim, 3000, 1300);
            int outside = Enemy(sim, 3500, -1350);
            int behind = Enemy(sim, 7500, 0);
            List<Frame> frames = Cast(sim, 7, 0);
            Assert.AreEqual(1, HitsOf(frames, edge).Count, "1,30 м — край полосы 0,45 + тело 0,85");
            Assert.AreEqual(0, HitsOf(frames, outside).Count, "1,35 — мимо");
            Assert.AreEqual(0, HitsOf(frames, behind).Count, "за стеной никто");
            Assert.AreEqual(0, DamageTo(frames, behind));
        }

        // ---------- 8. голова ----------

        [Test]
        public void Head_HandBeforeRelease_EndAtTaut_HandAtCatch_Monotonic()
        {
            Simulation sim = Arena();
            Cast(sim, 0, 7);
            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(s.Origin, Simulation.AnchorThrowHead(s, 0, s.ReleaseTick), "выпуск — рука");
            Assert.AreEqual(s.Center + s.Dir * s.Reach0, Simulation.AnchorThrowHead(s, 0, s.TautTick), "натяг — конец");
            Assert.AreEqual(s.Origin, Simulation.AnchorThrowHead(s, 0, s.CatchTick), "ловля — рука");
            double last = 0;
            for (int t = s.ReleaseTick; t <= s.TautTick; t++)
            {
                double at = Dist(Simulation.AnchorThrowHead(s, 0, t), s.Center);
                Assert.GreaterOrEqual(at, last - 1e-9, "полёт вперёд, тик " + t);
                last = at;
            }
            for (int t = s.TautTick + 1; t <= s.CatchTick; t++)
            {
                double at = Dist(Simulation.AnchorThrowHead(s, 0, t), s.Center);
                Assert.Less(at, last, "возврат к руке, тик " + t);
                last = at;
            }
        }
    }
}
