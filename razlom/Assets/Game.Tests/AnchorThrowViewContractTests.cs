using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AnchorThrowTests;

namespace Game.Tests
{
    /// <summary>
    /// Бросок якоря: то, что читает вид (AGENTS/specs/anchor-throw-view-contract.md) — кто чем задет,
    /// путь тела в тяге, углы сети Невода, концы полос Веера, укус Гарпуна; Гарпун по боссу.
    /// Классический NUnit (Unity старее: без Is.AnyOf).
    /// </summary>
    public sealed class AnchorThrowViewContractTests
    {
        private static Frame At(List<Frame> frames, int tick) => frames[tick - frames[0].Tick];

        [Test]
        public void HitLane_AndPull_ReadableAfterTheThrow_ClearedByTheNextCast()
        {
            Simulation sim = Arena();
            int light = Enemy(sim, 4000, 0);
            int elite = Enemy(sim, 5500, 600);
            sim.MarkElite(elite);
            int aside = Enemy(sim, 3000, 4000);
            List<Frame> frames = Run(sim, 140, t => t == 0 ? Press(7, 0) : t == 120 ? Press(-7, 0) : InputFrame.Empty,
                before: t =>
                {
                    if (t != 40) return;   // бросок кончился (C+25), следующего ещё нет
                    Assert.AreEqual(0, sim.AnchorThrowHitLane(light), "лёгкий — якорем");
                    Assert.IsTrue(sim.AnchorThrowHitPulls(light), "лёгкий — на тягу");
                    Assert.AreEqual(0, sim.AnchorThrowHitLane(elite));
                    Assert.IsFalse(sim.AnchorThrowHitPulls(elite), "элита не тянется");
                    Assert.AreEqual(-1, sim.AnchorThrowHitLane(aside), "мимо полосы");
                    Assert.IsFalse(sim.AnchorThrowHitPulls(aside));
                    Assert.AreEqual(-1, sim.AnchorThrowHitLane(9999), "чужой индекс — не задет");
                });
            Assert.AreEqual(2, Of(frames, SimEventType.AnchorThrowRelease).Count, "второй каст был");
            Assert.AreEqual(-1, sim.AnchorThrowHitLane(light), "новый каст стёр прошлые попадания");
            Assert.AreEqual(-1, sim.AnchorThrowHitLane(elite));
        }

        [Test]
        public void Hooks_AllHitInIdOrder_PulledOnlyTheLight_CountBeyondTheBuffer()
        {
            Simulation sim = Arena();
            int far = Enemy(sim, 6000, 0);
            int elite = Enemy(sim, 4500, 300);
            sim.MarkElite(elite);
            int near = Enemy(sim, 2500, -200);
            Enemy(sim, 3000, 4000);   // мимо полосы
            Assert.AreEqual(0, sim.AnchorThrowHooks(null), "броска не было — никого");
            List<Frame> frames = Cast(sim, 7, 0);
            Assert.AreEqual(3, Of(frames, SimEventType.AnchorThrowHit).Count);

            var ids = new int[4];
            Assert.AreEqual(3, sim.AnchorThrowHooks(ids), "все задетые");
            Assert.AreEqual(far, ids[0], "по возрастанию id");
            Assert.AreEqual(elite, ids[1]);
            Assert.AreEqual(near, ids[2]);
            Assert.AreEqual(2, sim.AnchorThrowHooks(ids, pulledOnly: true), "на тягу — только лёгкие");
            Assert.AreEqual(far, ids[0]);
            Assert.AreEqual(near, ids[1]);
            var one = new int[1];
            Assert.AreEqual(3, sim.AnchorThrowHooks(one), "счёт — больше буфера");
            Assert.AreEqual(far, one[0]);
            Assert.AreEqual(3, sim.AnchorThrowHooks(null), "без буфера — только счёт");
        }

        [Test]
        public void PullFrom_Length_PlannedPath_FollowTheBody_FromTautToCatch()
        {
            Simulation sim = Arena();
            int g = Enemy(sim, 4000, 0);
            List<Frame> frames = Cast(sim, 7, 0);
            AnchorThrowState s = sim.AnchorThrow;
            int taut = TickOf(frames, SimEventType.AnchorThrowYank), catchTick = TickOf(frames, SimEventType.AnchorThrowCatch);
            Assert.AreEqual(s.TautTick, taut);
            Assert.AreEqual(s.CatchTick, catchTick);
            FixVec2 from = sim.AnchorThrowPullFrom(g), spot = sim.AnchorThrowLandingSpot(g);
            Assert.Less(Dist(from, At(frames, taut).Positions[g]), 1e-6, "начало пути — где тело стояло в натяг");
            Assert.AreEqual(Dist(from, spot), sim.AnchorThrowPullLength(g).ToDouble(), 1e-3, "длина пути");
            Assert.AreEqual(from, Simulation.AnchorThrowPullAt(s, from, spot, taut), "в натяг — на месте");
            Assert.AreEqual(spot, Simulation.AnchorThrowPullAt(s, from, spot, catchTick), "в ловлю — на полукольце");
            double last = 0;
            for (int t = taut + 1; t <= catchTick; t++)
            {
                FixVec2 planned = Simulation.AnchorThrowPullAt(s, from, spot, t);
                Assert.Less(Dist(planned, At(frames, t).Positions[g]), 0.05, "план = тело, тик " + t);
                double moved = Dist(planned, from);
                Assert.Greater(moved, last, "едет к герою, тик " + t);
                last = moved;
            }
        }

        /// <summary>
        /// 07.10 владелец: тайминг Броска 25 тиков — натяг 2 тика. HoldTick…TautTick: фаза Taut, голова
        /// в конце, тело стоит; в TautTick — рывок (AnchorThrowYank, тело на крюке), едет со следующего.
        /// </summary>
        [Test]
        public void Hold_TwoTicks_HeadAtTheEnd_BodyStands_YankOnTautTick_PullFromTheNext()
        {
            Simulation sim = Arena();
            int g = Enemy(sim, 4000, 0);
            var reeledBefore = new List<bool>();
            List<Frame> frames = Cast(sim, 7, 0, before: t => reeledBefore.Add(sim.AnchorThrowReeled(g)));
            int c = frames[0].Tick;
            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(2, Simulation.AnchorThrowTautTicks);
            Assert.AreEqual(c + 10, s.HoldTick);
            Assert.AreEqual(c + 11, s.TautTick);
            Assert.AreEqual(s.LaneArriveTick(0) + 1, s.HoldTick, "натяг — со следующего тика после прихода головы");
            Assert.AreEqual(s.TautTick, TickOf(frames, SimEventType.AnchorThrowYank), "рывок — последний тик натяга");
            FixVec2 stood = At(frames, s.HoldTick - 1).Positions[g];
            for (int t = s.HoldTick; t <= s.TautTick; t++)
            {
                Assert.AreEqual(AnchorThrowPhase.Taut, At(frames, t).State.Phase, "фаза натяга, тик " + t);
                Assert.AreEqual(s.LaneEnd(0), Simulation.AnchorThrowHead(s, 0, t), "голова в конце, тик " + t);
                Assert.Less(Dist(At(frames, t).Positions[g], stood), 1e-6, "тело стоит, тик " + t);
            }
            // reeledBefore[k] — до шага k, то есть после тика c + k − 1.
            Assert.IsFalse(reeledBefore[s.HoldTick - c + 1], "в первый тик натяга тяги ещё нет");
            Assert.IsTrue(reeledBefore[s.TautTick - c + 1], "в рывок тело на крюке");
            Assert.AreEqual(AnchorThrowPhase.Return, At(frames, s.TautTick + 1).State.Phase);
            Assert.Greater(Dist(At(frames, s.TautTick + 1).Positions[g], stood), 0.01, "едет со следующего тика");
            Assert.AreEqual(s.TautTick + 6, s.CatchTick, "R = 6 при 7 м");
        }

        [Test]
        public void NetCorners_ThreeMetreStripFromHandToTheEnd_NoneForBaseOrWallThrow()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowNet);
            Run(sim, 2, t => t == 0 ? Press(7, 0) : InputFrame.Empty);
            AnchorThrowState s = sim.AnchorThrow;
            Assert.IsTrue(s.HasNet);
            Assert.IsTrue(Simulation.AnchorThrowNetCorners(s, out FixVec2 nl, out FixVec2 nr, out FixVec2 fl, out FixVec2 fr));
            AssertNear(nl, 0.5, 1.5, "ближний левый");
            AssertNear(nr, 0.5, -1.5, "ближний правый");
            AssertNear(fl, 7, 1.5, "дальний левый");
            AssertNear(fr, 7, -1.5, "дальний правый");

            sim = Arena();
            Run(sim, 2, t => t == 0 ? Press(7, 0) : InputFrame.Empty);
            Assert.IsFalse(sim.AnchorThrow.HasNet, "база — без сети");
            Assert.IsFalse(Simulation.AnchorThrowNetCorners(sim.AnchorThrow, out _, out _, out _, out _));

            sim = Arena(PelagForm.AnchorThrowNet, Map(1.2, 0, 0.5));
            Run(sim, 2, t => t == 0 ? Press(7, 0) : InputFrame.Empty);
            Assert.IsFalse(sim.AnchorThrow.HasNet, "бросок в стену — сети нет (Sim никого не ловит)");
        }

        [Test]
        public void FanLanes_EndsOnTheirLines_HeadArrivesAtLaneArriveTick()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowFan);
            Cast(sim, 7, 0);
            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(3, s.Lanes);
            for (int lane = 0; lane < 3; lane++)
            {
                Assert.AreEqual(7.0, Dist(s.LaneEnd(lane), s.Center), 1e-3, "дальность полосы " + lane);
                Assert.AreEqual(s.LaneEnd(lane), Simulation.AnchorThrowHead(s, lane, s.LaneArriveTick(lane)), "голова в конце, полоса " + lane);
                Assert.AreEqual(s.LaneEnd(lane), Simulation.AnchorThrowHead(s, lane, s.TautTick), "держит до натяга, полоса " + lane);
            }
            double between = System.Math.Acos(FixVec2.Dot(s.Dir, s.Dir1).ToDouble()) * 180 / System.Math.PI;
            Assert.AreEqual(25.0, between, 0.1, "призрак — 25° от якоря");
        }

        [Test]
        public void HarpoonBiteTick_IsTheBiteHitTick_HeadStopsAtTheBite_BaseHasNone()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowHarpoon);
            int first = Enemy(sim, 4000, 0);
            List<Frame> frames = Cast(sim, 7, 0);
            AnchorThrowState s = sim.AnchorThrow;
            var bite = HitsOf(frames, first);
            Assert.AreEqual(1, bite.Count);
            Assert.AreEqual(bite[0].frame.Tick, s.BiteTick, "тик укуса = тик попадания");
            Assert.AreEqual(s.ReleaseTick + s.Flight0, s.BiteTick);
            Assert.AreEqual(first, s.HarpoonTarget);
            Assert.AreEqual(s.LaneEnd(0), Simulation.AnchorThrowHead(s, 0, s.BiteTick), "голова — в точке укуса");
            Assert.AreEqual(bite[0].ev.Position, s.LaneEnd(0), "точка касания = конец полосы");

            sim = Arena();
            Cast(sim, 7, 0);
            Assert.AreEqual(-1, sim.AnchorThrow.BiteTick, "база не кусает");
        }

        [Test]
        public void Harpoon_Boss_DoubleDamage_NotPulled_NotStunned()
        {
            int baseDamage = BossDamage(PelagForm.None, out _, out _);
            int harpoonDamage = BossDamage(PelagForm.AnchorThrowHarpoon, out List<Frame> frames, out Simulation sim);
            const int boss = 1;
            Assert.Greater(baseDamage, 0);
            Assert.GreaterOrEqual(harpoonDamage, 2 * baseDamage - 2, "×2 (после брони)");
            var bite = HitsOf(frames, boss);
            Assert.AreEqual(1, bite.Count);
            Assert.IsFalse(bite[0].ev.Flag, "босса не тянет");
            foreach (var (_, ev) in Of(frames, SimEventType.Stun)) Assert.AreNotEqual(boss, ev.Target, "босс не оглушается");
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.AreEqual(boss, yank.ev.Target);
            Assert.AreEqual(AnchorThrowStop.Harpoon, yank.frame.State.StopKind0);
            Assert.AreEqual(0, yank.frame.State.ReeledCount);
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, boss));
        }

        private static int BossDamage(PelagForm form, out List<Frame> frames, out Simulation sim)
        {
            sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(4.5));
            const int boss = 1;
            sim.Entities.Stats[boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(boss);
            Hero(sim);
            Give(sim, form);
            FixVec2 at = sim.Entities.Position[boss];
            frames = Run(sim, 24, t => t == 0 ? Press(at.X.ToDouble(), at.Y.ToDouble()) : InputFrame.Empty);
            return DamageTo(frames, boss);
        }

        private static void AssertNear(FixVec2 p, double x, double y, string what)
        {
            Assert.AreEqual(x, p.X.ToDouble(), 1e-3, what + " X");
            Assert.AreEqual(y, p.Y.ToDouble(), 1e-3, what + " Y");
        }
    }
}
