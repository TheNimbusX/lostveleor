using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AnchorThrowTests;

namespace Game.Tests
{
    /// <summary>Бросок якоря: тяга на полукольцо, тяжёлые и босс, оглушение приземления, срывы (спека §2.9, 3–6).</summary>
    public sealed class AnchorThrowPullTests
    {
        private static int FrameAt(List<Frame> frames, int tick) => tick - frames[0].Tick;

        // ---------- 3. тяга ----------

        [Test]
        public void Pull_ThreeGuardians_2_4_6_AllOn1_60_NoOverlap_ArriveExactlyAtCatch()
        {
            Simulation sim = Arena();
            int[] ids = { Enemy(sim, 2000, 0), Enemy(sim, 4000, 0), Enemy(sim, 6000, 0) };
            List<Frame> frames = Cast(sim, 7, 0);
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.AreEqual(3, yank.frame.State.ReeledCount);
            Assert.AreEqual(1.60, yank.frame.State.RingRadius.ToDouble(), 1e-3);
            int catchTick = TickOf(frames, SimEventType.AnchorThrowCatch);
            Frame atCatch = frames[FrameAt(frames, catchTick)], before = frames[FrameAt(frames, catchTick - 1)];
            foreach (int id in ids)
            {
                Assert.AreEqual(1.60, Dist(atCatch.Positions[id], atCatch.Positions[0]), 0.02, "на полукольце " + id);
                Assert.Less(Dist(atCatch.Positions[id], sim.AnchorThrowLandingSpot(id)), 0.02, "доехал к ловле " + id);
                Assert.Greater(Dist(before.Positions[id], sim.AnchorThrowLandingSpot(id)), 0.02, "не раньше ловли " + id);
            }
            for (int a = 0; a < ids.Length; a++)
                for (int b = a + 1; b < ids.Length; b++)
                    Assert.GreaterOrEqual(Dist(atCatch.Positions[ids[a]], atCatch.Positions[ids[b]]), 1.70 - 0.02, "без наложений");
            Assert.AreEqual(3, Of(frames, SimEventType.AnchorThrowCatch)[0].ev.Amount, "доехали трое");
        }

        [Test]
        public void Pull_FiveGuardians_Ring2_36_RootSwarmWeightTwoIsPulledToo()
        {
            Simulation sim = Arena();
            int[] ids =
            {
                Enemy(sim, 1500, 0), Enemy(sim, 3100, 1000), Enemy(sim, 3100, -1000), Enemy(sim, 4800, 0), Enemy(sim, 6500, 600),
            };
            List<Frame> frames = Cast(sim, 7, 0);
            Assert.AreEqual(5, Of(frames, SimEventType.AnchorThrowYank)[0].frame.State.ReeledCount);
            Frame atCatch = frames[FrameAt(frames, TickOf(frames, SimEventType.AnchorThrowCatch))];
            foreach (int id in ids)
                Assert.AreEqual(2.36, Dist(atCatch.Positions[id], atCatch.Positions[0]), 0.02, "ρ = S/π, тело " + id);

            sim = Arena();
            int swarm = Mob(sim, EnemyKind.ForestRootSwarm, 4, 0);
            Assert.AreEqual(2.0, sim.Entities.PushWeight[swarm].ToDouble(), 1e-9, "рой весом 2 — волок Dragged его не тянул");
            frames = Cast(sim, 7, 0);
            Assert.IsTrue(HitsOf(frames, swarm)[0].ev.Flag);
            atCatch = frames[FrameAt(frames, TickOf(frames, SimEventType.AnchorThrowCatch))];
            Assert.AreEqual(0.45 + 0.3 + 0.45, Dist(atCatch.Positions[swarm], atCatch.Positions[0]), 0.02, "рой у ног");
        }

        [Test]
        public void Pull_BodyBehindATree_StaysPut_NoTeleport()
        {
            LayoutMap map = Map();
            Simulation sim = Arena(map: map);
            int g = Enemy(sim, 5000, 0);
            FixVec2 last = FixVec2.Zero;
            List<Frame> frames = Cast(sim, 7, 0, 30, before: t =>
            {
                if (t == 8) Rock(map, 3.2, 0, 0.6);   // дерево встало на пути тяги после попадания
            });
            int catchTick = TickOf(frames, SimEventType.AnchorThrowCatch);
            for (int k = 1; k < frames.Count; k++)
            {
                FixVec2 at = frames[k].Positions[g];
                Assert.GreaterOrEqual(Dist(at, At(3.2, 0)), 0.6 + 0.85 - 0.01, "не сквозь дерево, тик " + frames[k].Tick);
                Assert.Less(Dist(at, frames[k - 1].Positions[g]), 1.4, "без телепорта, тик " + frames[k].Tick);
                if (frames[k].Tick > catchTick) Assert.Less(Dist(at, frames[k - 1].Positions[g]), 0.06, "после ловли не догоняет");
                last = at;
            }
            Assert.Greater(Dist(last, sim.AnchorThrowLandingSpot(g)), 2.0, "упёрлось — стоит, где стоит");
        }

        // ---------- 4. тяжёлые и босс ----------

        [Test]
        public void Heavy_WendigoAndElite_NotMoved_Damage_Stun15()
        {
            Simulation sim = Arena();
            int wendigo = Mob(sim, EnemyKind.ForestWendigo, 3, 0);
            int elite = Enemy(sim, 5500, 600);
            sim.MarkElite(elite);
            FixVec2 w0 = sim.Entities.Position[wendigo], e0 = sim.Entities.Position[elite];
            List<Frame> frames = Cast(sim, 7, 0);
            foreach (int id in new[] { wendigo, elite })
            {
                var hit = HitsOf(frames, id);
                Assert.AreEqual(1, hit.Count);
                Assert.IsFalse(hit[0].ev.Flag, "тяжёлый не тянется");
                Assert.IsTrue(sim.Statuses.IsStunned(id, hit[0].frame.Tick + 14), "оглушён 15 тиков");
                Assert.IsFalse(sim.Statuses.IsStunned(id, hit[0].frame.Tick + 15));
            }
            Assert.Greater(DamageTo(frames, wendigo), 0);
            Assert.AreEqual(60, DamageTo(frames, elite));
            Assert.Less(Dist(sim.Entities.Position[wendigo], w0), 0.02, "Вендиго стоит");
            Assert.Less(Dist(sim.Entities.Position[elite], e0), 0.02, "элита стоит");
            Assert.AreEqual(0, Of(frames, SimEventType.AnchorThrowYank)[0].frame.State.ReeledCount);
        }

        [Test]
        public void Boss_Damage_NoStun_HeadSticksInTheHull_NobodyBehind()
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(4.5));
            const int boss = 1;
            sim.Entities.Stats[boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(boss);
            Hero(sim);
            Give(sim, PelagForm.None);
            FixVec2 hero = sim.Entities.Position[0], at = sim.Entities.Position[boss];
            FixVec2 dir = (at - hero).Normalized();
            FixVec2 far = hero + dir * Fix64.FromDouble(6.8);
            int behind = Enemy(sim, (int)(far.X.ToDouble() * 1000), (int)(far.Y.ToDouble() * 1000), radiusMm: 450);
            List<Frame> frames = Run(sim, 24, t => t == 0 ? Press(at.X.ToDouble(), at.Y.ToDouble()) : InputFrame.Empty);
            Assert.Greater(DamageTo(frames, boss), 0, "урон по боссу");
            foreach (var (_, ev) in Of(frames, SimEventType.Stun)) Assert.AreNotEqual(boss, ev.Target, "босс не оглушается");
            Assert.AreEqual(0, HitsOf(frames, behind).Count, "за корпусом никто");
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.IsTrue(yank.ev.Flag, "полёт оборван корпусом");
            Assert.AreEqual(AnchorThrowStop.Boss, yank.frame.State.StopKind0);
            Assert.Less(yank.frame.State.Reach0.ToDouble(), 4.5, "голова в корпусе, не за ним");
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, boss));
            Assert.Less(Dist(sim.Entities.Position[boss], at), 1e-6, "босс не сдвинут");
        }

        // ---------- 5. оглушение приземления ----------

        [Test]
        public void LandingStun15_ForTheReeled_KnockedOutOfTheTowGetsNone()
        {
            Simulation sim = Arena();
            int a = Enemy(sim, 2000, 0), b = Enemy(sim, 4000, 0), c = Enemy(sim, 6000, 0);
            List<Frame> frames = Cast(sim, 7, 0, 30, before: t =>
            {
                if (t == 13) ForcedMotion.Begin(sim.Entities, b, sim.Entities.Position[b] + At(0, 3), 4, ForcedMotionKind.Knockback);
            });
            int catchTick = TickOf(frames, SimEventType.AnchorThrowCatch);
            Frame atCatch = frames[FrameAt(frames, catchTick)];
            Assert.AreEqual(2, Of(frames, SimEventType.AnchorThrowCatch)[0].ev.Amount, "доехали двое");
            var stuns = new List<int>();
            bool afterCatch = false;
            foreach (SimEvent ev in atCatch.Events)
            {
                if (ev.Type == SimEventType.AnchorThrowCatch) afterCatch = true;
                if (ev.Type != SimEventType.Stun) continue;
                Assert.IsTrue(afterCatch, "Stun — следом за ловлей");
                Assert.AreEqual(15, ev.Amount);
                stuns.Add(ev.Target);
            }
            CollectionAssert.AreEquivalent(new[] { a, c }, stuns);
            Assert.IsTrue(sim.Statuses.IsStunned(a, catchTick + 14));
            Assert.IsFalse(sim.Statuses.IsStunned(a, catchTick + 15));
            Assert.IsFalse(sim.Statuses.IsStunned(b, catchTick), "выпал из тяги — без оглушения");
        }
    }
}
