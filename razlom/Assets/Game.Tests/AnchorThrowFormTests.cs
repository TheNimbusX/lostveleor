using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AnchorThrowTests;

namespace Game.Tests
{
    /// <summary>Бросок якоря: Невод, Веер, Гарпун; полукольцо (чистая функция); пул, таблица форм, хеш (спека §2.9, 7, 9–11).</summary>
    public sealed class AnchorThrowFormTests
    {
        // ---------- Невод ----------

        [Test]
        public void Net_CatchesTheThreeMetreStripOnTaut_HalfDamage_LightReeled_EliteStunned()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowNet);
            int lane = Enemy(sim, 4000, 0);
            int side = Enemy(sim, 2500, 1900);          // вне полосы якоря (1,30), в сети (1,5 + 0,85)
            int elite = Enemy(sim, 5600, -2000);
            sim.MarkElite(elite);
            int outside = Enemy(sim, 2500, -2600);      // вне сети
            List<Frame> frames = Cast(sim, 7, 0);
            int taut = TickOf(frames, SimEventType.AnchorThrowYank);
            Assert.AreEqual(60, DamageTo(frames, lane), "якорь — полный урон, сетью второй раз не бьётся");
            Assert.AreEqual(1, HitsOf(frames, lane).Count);
            foreach (int id in new[] { side, elite })
            {
                var hit = HitsOf(frames, id);
                Assert.AreEqual(1, hit.Count);
                Assert.AreEqual(3, hit[0].ev.Amount, "полоса 3 — сеть");
                Assert.AreEqual(taut, hit[0].frame.Tick, "сеть ловит в натяг");
                Assert.AreEqual(30, DamageTo(frames, id), "50 %");
            }
            Assert.IsTrue(HitsOf(frames, side)[0].ev.Flag, "лёгкий из сети — на тягу");
            Assert.IsFalse(HitsOf(frames, elite)[0].ev.Flag);
            Assert.IsTrue(sim.Statuses.IsStunned(elite, taut + 14));
            Assert.AreEqual(0, HitsOf(frames, outside).Count);
            // Порядок событий тика: натяг, потом сеть.
            var events = new List<SimEventType>();
            foreach (SimEvent ev in frames[taut - frames[0].Tick].Events) events.Add(ev.Type);
            Assert.Less(events.IndexOf(SimEventType.AnchorThrowYank), events.IndexOf(SimEventType.AnchorThrowHit));
            Assert.AreEqual(2, frames[taut - frames[0].Tick].State.ReeledCount, "якорь + сеть на одно полукольцо");
        }

        // ---------- Веер ----------

        [Test]
        public void Fan_ThreeLanes_EachEnemyOnce_GhostsFullDamage_OneRing()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowFan);
            int main = Enemy(sim, 3000, 0);
            int left = Enemy(sim, 3464, 2000);           // +30°, 4 м
            int right = Enemy(sim, 4330, -2500);         // −30°, 5 м
            List<Frame> frames = Cast(sim, 7, 0);
            var release = Of(frames, SimEventType.AnchorThrowRelease)[0];
            Assert.IsTrue(release.ev.Flag, "Веер — три полосы");
            Assert.AreEqual(3, release.frame.State.Lanes);
            Assert.AreEqual(0, HitsOf(frames, main)[0].ev.Amount);
            Assert.AreEqual(1, HitsOf(frames, left)[0].ev.Amount, "призрак +30°");
            Assert.AreEqual(2, HitsOf(frames, right)[0].ev.Amount, "призрак −30°");
            foreach (int id in new[] { main, left, right })
            {
                Assert.AreEqual(1, HitsOf(frames, id).Count, "один раз на все полосы");
                Assert.AreEqual(60, DamageTo(frames, id));
            }
            Assert.AreEqual(3, Of(frames, SimEventType.AnchorThrowYank)[0].frame.State.ReeledCount);
            Assert.AreEqual(3, Of(frames, SimEventType.AnchorThrowCatch)[0].ev.Amount);
            AnchorThrowState s = sim.AnchorThrow;
            Assert.AreEqual(30.0, System.Math.Atan2(s.Dir1.Y.ToDouble(), s.Dir1.X.ToDouble()) * 180 / System.Math.PI, 0.1);
            Assert.AreEqual(System.Math.PI / 6, s.GhostAngle.ToDouble(), 1e-6);
        }

        // ---------- Гарпун ----------

        [Test]
        public void Harpoon_BitesTheFirst_Double_PullsWithStun_UntilCatchPlusOneSecond()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowHarpoon);
            int first = Enemy(sim, 4000, 0);
            int second = Enemy(sim, 6000, 0);
            sim.MarkElite(first);                         // Гарпун тянет и элиту
            List<Frame> frames = Cast(sim, 7, 0, 45);
            var bite = HitsOf(frames, first);
            Assert.AreEqual(1, bite.Count);
            Assert.AreEqual(5, bite[0].frame.Tick, "укус C+5");
            Assert.IsTrue(bite[0].ev.Flag, "тянется");
            Assert.AreEqual(120, DamageTo(frames, first), "×2");
            Assert.AreEqual(0, HitsOf(frames, second).Count, "дальше не летит");
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.AreEqual(6, yank.frame.Tick);
            Assert.AreEqual(first, yank.ev.Target);
            Assert.IsTrue(yank.ev.Flag);
            Assert.IsTrue(yank.frame.State.Stuck);
            Assert.AreEqual(10, TickOf(frames, SimEventType.AnchorThrowCatch), "R по дальности укуса, мин. 4");
            Assert.IsTrue(sim.Statuses.IsStunned(first, 39), "оглушён до C+40");
            Assert.IsFalse(sim.Statuses.IsStunned(first, 40));
            Frame atCatch = frames[10];
            Assert.AreEqual(0.45 + 0.3 + 0.85, Dist(atCatch.Positions[first], atCatch.Positions[0]), 0.02, "в центр полукольца");
        }

        [Test]
        public void Harpoon_WeightZero_WendigoThorncaster_DoubleAndStun_NotMoved()
        {
            foreach (EnemyKind kind in new[] { EnemyKind.ForestWendigo, EnemyKind.ForestThorncaster })
            {
                Simulation sim = Arena(PelagForm.AnchorThrowHarpoon);
                int mob = Mob(sim, kind, 4, 0);
                FixVec2 at = sim.Entities.Position[mob];
                List<Frame> frames = Cast(sim, 7, 0);
                var bite = HitsOf(frames, mob);
                Assert.AreEqual(1, bite.Count, kind.ToString());
                Assert.IsFalse(bite[0].ev.Flag, kind + ": вес 0 — не тянется");
                int damage = DamageTo(frames, mob);
                Assert.Greater(damage, 0);
                Assert.IsTrue(sim.Statuses.IsStunned(mob, bite[0].frame.Tick + 20), kind + ": оглушён");
                Assert.Less(Dist(sim.Entities.Position[mob], at), 0.02, kind + ": стоит");
            }
        }

        [Test]
        public void Harpoon_Nobody_FullFlight_EmptyReturn()
        {
            Simulation sim = Arena(PelagForm.AnchorThrowHarpoon);
            List<Frame> frames = Cast(sim, 7, 0);
            var yank = Of(frames, SimEventType.AnchorThrowYank)[0];
            Assert.AreEqual(10, yank.frame.Tick);
            Assert.AreEqual(-1, yank.ev.Target);
            Assert.IsFalse(yank.ev.Flag);
            Assert.AreEqual(18, TickOf(frames, SimEventType.AnchorThrowCatch));
        }
    }
}
