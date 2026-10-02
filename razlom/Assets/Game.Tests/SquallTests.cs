using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Шквал после переделки 02.10 (Simulation.Squall): полёт по длине, опора на
    /// цели, замах, посадка по радиусам тел, выбор цели с конусом и зигзагом,
    /// облёт одинокой цели, взгляд из Sim, выход. Формы — SquallFormTests.
    /// </summary>
    public sealed class SquallTests
    {
        internal const int Slot = 0;
        internal const int Health = 100000;

        internal static Simulation Arena(PelagForm form = PelagForm.None, ulong seed = 1234, params AbilityNode[] extra)
        {
            var sim = new Simulation(seed, 128);
            sim.SetupTestArena(0);
            Give(sim, form, extra);
            return sim;
        }

        internal static void Give(Simulation sim, PelagForm form, params AbilityNode[] extra)
        {
            var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = PelagForms.AppendFormNodes(form, nodes, 0);
            foreach (AbilityNode node in extra) nodes[count++] = node;
            sim.SetAbility(Slot, AbilityDefinition.ChainStep(), nodes, count);
        }

        /// <summary>Узел таланта линии Шквала (5 — «Возврат», 2 — «Неуязвимость»).</summary>
        internal static AbilityNode Talent(int index)
        {
            var buffer = new AbilityNode[1];
            SabreTalents.AppendNode(SabreTalentLine.Squall, index, buffer, 0);
            return buffer[0];
        }

        internal static Fix64 Mm(int mm) => Fix64.Ratio(mm, 1000);

        /// <summary>Неподвижный враг без удара в (x, y) мм от начала координат (там герой).</summary>
        internal static int Enemy(Simulation sim, int xMm, int yMm, int health = Health, int radiusMm = 450)
        {
            int id = sim.Entities.Spawn(new FixVec2(Mm(xMm), Mm(yMm)), health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Mm(radiusMm);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        internal static InputFrame Press(int target)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = 1 << Slot;
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        internal static InputFrame Walk(FixVec2 to, bool attack = false)
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)(InputFlags.MoveOrder | (attack ? InputFlags.Attack : 0));
            input.Aim = to;
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            return input;
        }

        /// <summary>Тик шага, его события и снимок героя после шага.</summary>
        internal struct Frame
        {
            public int Tick;
            public SimEvent[] Events;
            public FixVec2 Position, Facing;
            public SquallState Squall;
            public int Health;
            public FixVec2[] Positions;
        }

        internal static List<Frame> Run(Simulation sim, int ticks, System.Func<int, InputFrame> input)
        {
            var frames = new List<Frame>();
            for (int t = 0; t < ticks; t++)
            {
                int tick = sim.Tick;
                sim.Step(input(t));
                var events = new SimEvent[sim.Events.Count];
                for (int i = 0; i < events.Length; i++) events[i] = sim.Events[i];
                frames.Add(new Frame
                {
                    Tick = tick, Events = events, Position = sim.Entities.Position[Simulation.PlayerId],
                    Facing = sim.Entities.Facing[Simulation.PlayerId], Squall = sim.Squall,
                    Health = sim.Entities.Health[Simulation.PlayerId],
                    Positions = (FixVec2[])sim.Entities.Position.Clone(),
                });
            }
            return frames;
        }

        internal static List<Frame> Cast(Simulation sim, int target, int ticks = 60)
            => Run(sim, ticks, t => t == 0 ? Press(target) : InputFrame.Empty);

        internal static List<(Frame frame, SimEvent ev)> Of(List<Frame> frames, SimEventType type)
        {
            var found = new List<(Frame, SimEvent)>();
            foreach (Frame f in frames)
                foreach (SimEvent ev in f.Events)
                    if (ev.Type == type) found.Add((f, ev));
            return found;
        }

        internal static double Dist(FixVec2 a, FixVec2 b) => FixVec2.Distance(a, b).ToDouble();

        internal static double Dot(FixVec2 a, FixVec2 b)
            => FixVec2.Dot(a.Normalized(), b.Normalized()).ToDouble();

        internal static double Cross(FixVec2 a, FixVec2 b) => (a.X * b.Y - a.Y * b.X).ToDouble();

        private static double SegmentDistance(FixVec2 a, FixVec2 b, FixVec2 p)
        {
            double ax = a.X.ToDouble(), ay = a.Y.ToDouble(), bx = b.X.ToDouble(), by = b.Y.ToDouble();
            double px = p.X.ToDouble(), py = p.Y.ToDouble();
            double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
            double t = len == 0 ? 0 : System.Math.Max(0, System.Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len));
            double qx = ax + dx * t - px, qy = ay + dy * t - py;
            return System.Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary>Тройка: первая цель впереди, две дальше — слева и справа на равных.</summary>
        private static Simulation Trio(out int first, out int left, out int right)
        {
            Simulation sim = Arena();
            first = Enemy(sim, 3000, 0);
            left = Enemy(sim, 5000, 1500);
            right = Enemy(sim, 5000, -1500);
            return sim;
        }

        // ---------- сроки ----------

        [Test]
        public void FlightTicks_FollowTheDistanceAtTwentyMetresPerSecond()
        {
            Assert.AreEqual(2, Simulation.SquallFlightTicks(Mm(300)));
            Assert.AreEqual(3, Simulation.SquallFlightTicks(Mm(1700)));
            Assert.AreEqual(5, Simulation.SquallFlightTicks(Mm(3000)));
            Assert.AreEqual(6, Simulation.SquallFlightTicks(Mm(4600)));
            Assert.AreEqual(6, Simulation.SquallFlightTicks(Fix64.FromInt(10)), "потолок 6 тиков");
            Assert.AreEqual(2, Simulation.SquallWindupTicks);
            Assert.AreEqual(2, Simulation.SquallStopTicks);
        }

        [Test]
        public void Series_WindupThenFlightByLengthThenTwoTickStopOnEachHit()
        {
            Simulation sim = Trio(out int first, out _, out _);
            int cast = sim.Tick;
            List<Frame> frames = Cast(sim, first, 40);
            var jumps = Of(frames, SimEventType.SquallJump);
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(4, jumps.Count, "прыжков прежние 4");
            Assert.AreEqual(4, strikes.Count);

            Assert.AreEqual(cast + Simulation.SquallWindupTicks, jumps[0].frame.Tick, "замах 2 тика");
            FixVec2 from = FixVec2.Zero;
            for (int i = 0; i < jumps.Count; i++)
            {
                SimEvent jump = jumps[i].ev;
                Assert.AreEqual(i, jump.ActionVariant);
                Assert.AreEqual(i % 2 == 1, jump.Flag, "прямой и обратный удар строго чередуются");
                Assert.AreEqual(Simulation.SquallFlightTicks(FixVec2.Distance(from, jump.Position)), jump.Amount,
                    "тики полёта — по длине прыжка " + i);
                Assert.AreEqual(jumps[i].frame.Tick + jump.Amount, strikes[i].frame.Tick, "удар в тик прибытия " + i);
                Assert.Less(Dist(strikes[i].frame.Position, jump.Position), 1e-3, "прибыл в точку посадки " + i);
                Assert.AreEqual(3 - i, strikes[i].ev.Amount);
                Assert.IsTrue(strikes[i].ev.Flag, "удар дошёл " + i);
                if (i + 1 < jumps.Count)
                    Assert.AreEqual(strikes[i].frame.Tick + Simulation.SquallStopTicks, jumps[i + 1].frame.Tick, "опора 2 тика");
                from = jump.Position;
            }

            // Опора: тело стоит, пока не начался следующий полёт.
            foreach (Frame f in frames)
                if (f.Squall.Phase == SquallPhase.Stop || f.Squall.Phase == SquallPhase.Windup || f.Squall.Phase == SquallPhase.Hold)
                    Assert.Less(Dist(f.Position, f.Squall.Phase == SquallPhase.Windup ? FixVec2.Zero : f.Squall.To), 1e-3,
                        "в опоре герой стоит, тик " + f.Tick);

            int last = strikes[3].frame.Tick;
            var ended = Of(frames, SimEventType.SquallEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((int)SquallEnd.Done, ended[0].ev.Amount);
            Assert.AreEqual(last + Simulation.SquallFinalHoldTicks + Simulation.SquallExitTicks, ended[0].frame.Tick);
            // Медиана 1,7 м: 2 + 4×(3+2) + 1 + 6 ≈ 29 тиков — здесь прыжки 3/4/3/2.
            Assert.AreEqual(cast + 29, ended[0].frame.Tick);
            Assert.IsFalse(sim.SquallActive);
            Assert.AreEqual(ended[0].frame.Tick, sim.PlayerAction.EndTick, "часы действия кончились с выходом");
            Assert.AreEqual(strikes[0].frame.Tick, sim.PlayerAction.ContactTick, "контакт часов — первый удар");
        }

        [Test]
        public void Damage_IsTheSameAsBefore_EveryHit85()
        {
            Simulation sim = Trio(out int first, out int left, out int right);
            Cast(sim, first, 40);
            int lost = 3 * Health - sim.Entities.Health[first] - sim.Entities.Health[left] - sim.Entities.Health[right];
            Assert.AreEqual(4 * 85, lost);
        }

        // ---------- посадка ----------

        [Test]
        public void Landing_IsBodyRadiiPlusTenCentimetres_AndNothingPushesTheHeroAfter()
        {
            Simulation sim = Arena();
            int big = Enemy(sim, 4000, 0, radiusMm: 850);
            List<Frame> frames = Cast(sim, big, 8);
            var strike = Of(frames, SimEventType.SquallStrike)[0];
            double expected = (sim.Entities.BodyRadius[0] + Mm(850) + Simulation.SquallLandingGap).ToDouble();
            Assert.AreEqual(1.4, expected, 1e-3, "Хранитель: 0,45 + 0,85 + 0,1");
            Assert.AreEqual(expected, Dist(strike.frame.Position, strike.frame.Positions[big]), 1e-3);
            int at = frames.IndexOf(strike.frame);
            Assert.Less(Dist(frames[at + 1].Position, strike.frame.Position), 1e-6, "расталкивание не сдвинуло героя в опоре");
        }

        [Test]
        public void LoneEnemy_IsCircledAround_NeverFlownThrough()
        {
            Simulation sim = Arena();
            int lone = Enemy(sim, 3000, 0);
            List<Frame> frames = Cast(sim, lone, 40);
            var jumps = Of(frames, SimEventType.SquallJump);
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(4, jumps.Count);
            double bodies = (sim.Entities.BodyRadius[0] + sim.Entities.BodyRadius[lone]).ToDouble();
            for (int i = 1; i < jumps.Count; i++)
            {
                // Хорда облёта задевает тело на сантиметры — цель сдвигается расталкиванием чуть-чуть.
                FixVec2 center = jumps[i].frame.Positions[lone];
                Assert.AreEqual(lone, jumps[i].ev.Target);
                FixVec2 from = strikes[i - 1].frame.Position, to = jumps[i].ev.Position;
                Assert.Greater(SegmentDistance(from, to, center), bodies - 0.1, "прыжок " + i + " прошёл сквозь тело");
                Assert.AreEqual((sim.Entities.BodyRadius[0] + sim.Entities.BodyRadius[lone] + Simulation.SquallLandingGap).ToDouble(),
                    Dist(to, center), 1e-3, "облёт по окружности посадки");
                Assert.IsTrue(strikes[i].ev.Flag, "удар облёта дошёл " + i);
            }
            // Облёт идёт в одну сторону: каждый следующий прыжок дальше по кругу.
            for (int i = 2; i < jumps.Count; i++)
            {
                FixVec2 center = jumps[i].frame.Positions[lone];
                FixVec2 a = jumps[i - 1].ev.Position - center, b = jumps[i].ev.Position - center;
                FixVec2 c = jumps[i - 2].ev.Position - center;
                Assert.AreEqual(System.Math.Sign(Cross(c, a)), System.Math.Sign(Cross(a, b)), "облёт сменил сторону");
            }
        }

        // ---------- цели ----------

        [Test]
        public void Targets_Zigzag_ForehandTurnsLeftThenBackhandTurnsRight()
        {
            Simulation sim = Trio(out int first, out int left, out int right);
            List<Frame> frames = Cast(sim, first, 40);
            var jumps = Of(frames, SimEventType.SquallJump);
            Assert.AreEqual(first, jumps[0].ev.Target);
            Assert.AreEqual(left, jumps[1].ev.Target, "после прямого удара — налево");
            Assert.AreEqual(right, jumps[2].ev.Target, "после обратного — направо");
            Assert.AreEqual(left, jumps[3].ev.Target, "посещённые тоже зигзагом, текущую не облетает, пока есть другие");
        }

        [Test]
        public void Targets_ATurnWithinTheConeBeatsANearerOneBehind()
        {
            Simulation sim = Arena();
            int first = Enemy(sim, 3000, 0);
            int behind = Enemy(sim, -300, 0);     // 2,3 м за спиной после посадки
            int ahead = Enemy(sim, 6000, 1500);    // 3,3 м впереди
            List<Frame> frames = Cast(sim, first, 12);
            Assert.AreEqual(ahead, Of(frames, SimEventType.SquallJump)[1].ev.Target);
            Assert.AreNotEqual(behind, sim.Squall.Target);
        }

        // ---------- взгляд ----------

        [Test]
        public void Facing_IsDrivenBySim_TurnsAtTheHit_AndDoesNotSnapAtTheEnd()
        {
            Simulation sim = Trio(out int first, out _, out _);
            List<Frame> frames = Cast(sim, first, 40);
            var jumps = Of(frames, SimEventType.SquallJump);
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.Greater(Dot(frames[0].Facing, sim.Entities.Position[first]), 0.999, "замах смотрит на первую цель");
            for (int i = 0; i < jumps.Count; i++)
            {
                FixVec2 from = i == 0 ? FixVec2.Zero : strikes[i - 1].frame.Position;
                Assert.Greater(Dot(jumps[i].frame.Facing, jumps[i].ev.Position - from), 0.9999, "старт прыжка — взгляд по прыжку " + i);
                if (i + 1 < jumps.Count)
                    Assert.Greater(Dot(strikes[i].frame.Facing, jumps[i + 1].ev.Position - strikes[i].frame.Position), 0.999,
                        "в тик удара взгляд уже на следующую цель " + i);
            }
            FixVec2 lastFacing = strikes[3].frame.Facing;
            foreach (Frame f in frames)
                if (f.Tick > strikes[3].frame.Tick)
                    Assert.Less(Dist(f.Facing, lastFacing), 1e-6, "после последнего удара взгляд не прыгает, тик " + f.Tick);
        }

        // ---------- окна ----------

        [Test]
        public void Walking_IsHeldThroughTheSeries_AndCancelsTheExitFromItsSecondTick()
        {
            Simulation sim = Arena();
            int lone = Enemy(sim, 3000, 0);
            var far = new FixVec2(Fix64.FromInt(-20), Fix64.Zero);
            List<Frame> frames = Run(sim, 40, t => t == 0 ? Press(lone) : Walk(far));
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(4, strikes.Count, "ходьба не сорвала прыжки");

            int last = strikes[3].frame.Tick;
            int walk = last + Simulation.SquallFinalHoldTicks + Simulation.SquallExitLockedTicks;
            for (int i = 1; i < frames.Count; i++)
            {
                Frame f = frames[i];
                if (f.Tick <= last || f.Tick >= walk) continue;
                Assert.Less(Dist(f.Position, frames[i - 1].Position), 1e-6, "удержание и начало выхода — стоит, тик " + f.Tick);
            }
            var ended = Of(frames, SimEventType.SquallEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((int)SquallEnd.WalkedOut, ended[0].ev.Amount);
            Assert.AreEqual(walk, ended[0].frame.Tick, "со второго тика выхода шаг срывает выход");
            Frame walked = frames.Find(f => f.Tick == walk);
            Assert.Greater(Dist(walked.Position, strikes[3].frame.Position), 0.01, "и герой пошёл");
        }

        [Test]
        public void Sabre_IsHeldUntilTheExitCanBeWalkedOutOf()
        {
            Simulation sim = Arena();
            int lone = Enemy(sim, 3000, 0);
            List<Frame> frames = Run(sim, 40, t => t == 0 ? Press(lone) : Walk(sim.Entities.Position[lone], attack: true));
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(4, strikes.Count, "зажатая атака не сорвала прыжки");
            int walk = strikes[3].frame.Tick + Simulation.SquallFinalHoldTicks + Simulation.SquallExitLockedTicks;
            var swings = Of(frames, SimEventType.Attack);
            Assert.Greater(swings.Count, 0);
            Assert.AreEqual(walk, swings[0].frame.Tick, "первый удар сабли — с хвоста выхода, не раньше");
            Assert.AreEqual((int)SquallEnd.Interrupted, Of(frames, SimEventType.SquallEnded)[0].ev.Amount);
        }

        [Test]
        public void Dash_CutsTheSeriesAnyTime()
        {
            Simulation sim = Arena();
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            int lone = Enemy(sim, 3000, 0);
            var dash = InputFrame.Empty;
            dash.AbilityMask = 1 << 4;
            dash.Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(5));
            dash.AttackTarget = dash.AbilityTarget = -1;
            List<Frame> frames = Run(sim, 20, t => t == 0 ? Press(lone) : t == 3 ? dash : InputFrame.Empty);
            var ended = Of(frames, SimEventType.SquallEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((int)SquallEnd.Interrupted, ended[0].ev.Amount);
            Assert.AreEqual(0, Of(frames, SimEventType.SquallStrike).Count, "рывок в полёте — удара нет");
            Assert.IsFalse(sim.SquallActive);
        }

        [Test]
        public void TargetDeadInTheWindup_IsReplacedOrTheCastEndsQuietly()
        {
            Simulation alone = Arena();
            int only = Enemy(alone, 3000, 0);
            List<Frame> frames = Run(alone, 10, t =>
            {
                if (t == 1) alone.Entities.Alive[only] = false;
                return t == 0 ? Press(only) : InputFrame.Empty;
            });
            Assert.AreEqual((int)SquallEnd.NoTarget, Of(frames, SimEventType.SquallEnded)[0].ev.Amount);
            Assert.AreEqual(0, Of(frames, SimEventType.SquallJump).Count);

            Simulation pair = Arena();
            int a = Enemy(pair, 3000, 0), b = Enemy(pair, 3500, 1000);
            frames = Run(pair, 10, t =>
            {
                if (t == 1) pair.Entities.Alive[a] = false;
                return t == 0 ? Press(a) : InputFrame.Empty;
            });
            Assert.AreEqual(b, Of(frames, SimEventType.SquallJump)[0].ev.Target, "цель умерла в замахе — первый прыжок к другой");
        }
    }
}
