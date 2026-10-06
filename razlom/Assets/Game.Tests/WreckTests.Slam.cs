using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>Крушение v2: удар оземь, вал, стена на пути (SPEC 2.10, тесты 4–5).</summary>
    public partial class WreckTests
    {
        /// <summary>Урон цели за самую быструю серию по тикам (ключ — тик, значение — урон).</summary>
        private static Dictionary<int, int> SeriesDamage(Simulation sim, int id, int ticks = 57)
        {
            var hits = new Dictionary<int, int>();
            for (int i = 0; i < ticks; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 20));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Target == id)
                        hits[tick] = (hits.TryGetValue(tick, out int d) ? d : 0) + e.Amount;
            }
            return hits;
        }

        // ---- 4. Удар оземь и вал ----

        [Test]
        public void Slam_CircleAtTwoPointTwo_Hits140AndStuns15_WaveSkipsIt()
        {
            var sim = Arena();
            int foe = Guardian(sim, 3.5, 0);
            var stuns = new List<int>();
            var hits = new Dictionary<int, int>();
            for (int i = 0; i < 45; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 20));
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.Damage && e.Target == foe) hits[tick] = e.Amount;
                    if (e.Type == SimEventType.Stun && e.Target == foe) stuns.Add(e.Amount);
                }
            }
            // 3,5 м — ещё в секторе махов (2,8 + 0,85): 70, 70, потом круг 140, валом — ничего.
            CollectionAssert.AreEquivalent(new[] { 5, 11, 20 }, hits.Keys);
            Assert.AreEqual(140, hits[20], "выпад v4 — геометрия прежнего удара оземь");
            CollectionAssert.AreEqual(new[] { 15 }, stuns);
        }

        // Выпад v4 везёт героя на 0,6 м (Simulation.Wreck.Lunge): полоса от героя после шага — цели на 0,6 дальше.
        [TestCase(5.1, 22)]
        [TestCase(6.1, 24)]
        [TestCase(7.1, 26)]
        [TestCase(7.44, 27)]
        public void Wave_HitsOnceWhenTheFrontTouches(double x, int tick)
        {
            var sim = Arena();
            int foe = Guardian(sim, x, 0);
            var hits = SeriesDamage(sim, foe);
            Assert.AreEqual(1, hits.Count, "вал бьёт один раз");
            Assert.IsTrue(hits.ContainsKey(tick), "фронт дошёл в тик " + string.Join(",", hits.Keys));
            Assert.AreEqual(70, hits[tick]);
        }

        [Test]
        public void Wave_MissesBeyondTheLaneEnd()
        {
            var sim = Arena();
            int foe = Guardian(sim, 7.5, 0);
            Assert.AreEqual(0, SeriesDamage(sim, foe).Count, "конец полосы — 6 м от героя после выпада (0,6)");
        }

        [TestCase(1.55, true)]
        [TestCase(1.7, false)]
        public void Wave_LaneHalfWidth075PlusBody(double across, bool hit)
        {
            var sim = Arena();
            int foe = Guardian(sim, 5.0, across);
            Assert.AreEqual(hit ? 1 : 0, SeriesDamage(sim, foe).Count);
        }

        [Test]
        public void Wave_KnocksLightAlongTheLane_EliteStands()
        {
            var light = Arena();
            int a = Guardian(light, 5.0, 0);
            SeriesDamage(light, a);
            Assert.AreEqual(5.8f, light.Entities.Position[a].X.ToFloat(), 0.02f, "лёгкого отбросило на 0,8 м по полосе");

            var elite = Arena();
            int b = Guardian(elite, 5.0, 0);
            elite.MarkElite(b);
            SeriesDamage(elite, b);
            Assert.AreEqual(5.0f, elite.Entities.Position[b].X.ToFloat(), 0.01f, "элита стоит");
        }

        /// <summary>Толпа замера (3 в ряд 1,8/3,6/5,2, два сбоку (0,9; ±1,5), один сзади): 910 за серию (v4 с шагом выпада).</summary>
        [Test]
        public void MeasuredCrowd_Takes910PerSeries()
        {
            var sim = Arena();
            var ids = new[]
            {
                Guardian(sim, 1.8, 0), Guardian(sim, 3.6, 0), Guardian(sim, 5.2, 0),
                Guardian(sim, 0.9, 1.5), Guardian(sim, 0.9, -1.5), Guardian(sim, -1.6, 0),
            };
            int total = 0;
            var per = new int[ids.Length];
            for (int i = 0; i < 60; i++)
            {
                sim.Step(Press(i < 20, 6, 0));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage)
                        for (int k = 0; k < ids.Length; k++)
                            if (e.Target == ids[k]) { per[k] += e.Amount; total += e.Amount; }
            }
            // Выпад v4 (Simulation.Wreck.Lunge): герой шагает до касания первого (0,5 м), круг уходит вперёд — двое сбоку
            // (0,9; ±1,5) получают только махи. Было 1190 (280, 280, 70, 280, 280, 0) до шага выпада.
            CollectionAssert.AreEqual(new[] { 280, 280, 70, 140, 140, 0 }, per, string.Join(",", per));
            Assert.AreEqual(0.5f, sim.Entities.Position[P].X.ToFloat(), 1e-3f, "шаг выпада упёрся в тело первого");
            Assert.AreEqual(910, total);
        }

        // ---- 5. Стена на пути ----

        /// <summary>Комната 20×20 с центром в нуле и камнем; герой в нуле, Крушение в слоте 0.</summary>
        internal static Simulation RockArena(double rockX, double rockY, double rockRadius, params AbilityNode[] nodes)
        {
            var room = new ModuleDefinition("wreck.rock", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }));
            map.TryPlace(0, 0, -10, -10);
            map.AddTestObstacle(new LayoutObstacle(new FixVec2(M(rockX), M(rockY)), M(rockRadius), 0));
            map.BuildRoutes();
            var sim = new Simulation(1234, 128);
            sim.SetupRift(map, 1234UL, 0, 0, 100);
            sim.PlayerInvulnerable = true;
            sim.Entities.Position[P] = FixVec2.Zero;
            sim.Entities.Facing[P] = new FixVec2(Fix64.One, Fix64.Zero);
            for (int i = 1; i < sim.Entities.Count; i++) sim.Entities.Alive[i] = false;
            sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, nodes.Length);
            sim.Entities.Lavidium[P] = Fix64.FromInt(sim.Entities.MaxLavidium[P]);
            return sim;
        }

        [Test]
        public void Wave_StopsAtARockAcrossTheLane_NobodyBehindIsHit()
        {
            var sim = RockArena(4.5, 0, 0.5);
            int behind = Guardian(sim, 6.0, 0.6);
            var hits = SeriesDamage(sim, behind);
            Assert.IsTrue(sim.Wreck.WallStopped, "вал упёрся в камень");
            // Полоса — от героя после выпада (0,6 м): край камня 4,0 → 3,4 от него.
            Assert.AreEqual(3.4f, sim.Wreck.WallEnd.ToFloat(), 0.11f, "вал гаснет на краю камня");
            Assert.AreEqual(3, sim.Wreck.WaveTravelTicks, "⌈(3,4 − 2,2) / 0,5⌉");
            Assert.AreEqual(0, hits.Count, "за камнем никто не задет");

            var open = RockArena(-8, -8, 0.5);
            int same = Guardian(open, 6.0, 0.6);
            Assert.AreEqual(1, SeriesDamage(open, same).Count, "без камня та же цель задета валом");
        }

        /// <summary>Камень ближе 2,2 м (1,1–2,1 на оси): якорь ложится перед ним, круг за камень не достаёт.</summary>
        [Test]
        public void Slam_RockCloserThanTheImpactPoint_AnchorLandsInFrontOfIt()
        {
            var sim = RockArena(1.6, 0, 0.5);
            int behind = Guardian(sim, 3.4, 0);
            var hits = SeriesDamage(sim, behind);
            Assert.IsTrue(sim.Wreck.WallStopped);
            // Выпад упирается в камень телом героя (0,6 м — до касания), точка — от героя после шага.
            float hero = sim.Entities.Position[P].X.ToFloat();
            Assert.AreEqual(hero + sim.Wreck.WallEnd.ToFloat(), sim.Wreck.ImpactPoint.X.ToFloat(), 1e-3f, "точка удара — перед камнем");
            Assert.AreEqual(1.1f, sim.Wreck.ImpactPoint.X.ToFloat(), 0.11f);
            Assert.AreEqual(0, sim.Wreck.WaveTravelTicks, "вала нет");
            Assert.IsFalse(hits.ContainsKey(20), "круг у камня (1,1 + 1,2 + тело) до 3,4 м не достаёт");
        }
    }
}
