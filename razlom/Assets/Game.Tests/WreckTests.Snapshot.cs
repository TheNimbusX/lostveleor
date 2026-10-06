using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>Крушение v2: босс по корпусу, события и снимок для вида, детерминизм (SPEC 2.10, тесты 8–9).</summary>
    public partial class WreckTests
    {
        // ---- 8. Босс ----

        private static Simulation BossArena(params AbilityNode[] nodes)
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
            e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            e.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            e.Stats[1].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.RefreshStats(1);
            sim.SetAbility(0, AbilityDefinition.Wreck(), nodes, nodes.Length);
            e.Lavidium[0] = Fix64.FromInt(e.MaxLavidium[0]);
            return sim;
        }

        [TestCase(0)] [TestCase(3)] [TestCase(6)]
        public void Boss_FromAnyHullCircle_70_70_140_NeverStunned(int circle)
        {
            var sim = BossArena();
            var e = sim.Entities;
            FixVec2 center = sim.ThicketHullCircleCenter(1, circle);
            Simulation.ThicketHullLocal(circle, out _, out _, out Fix64 radius);
            FixVec2 outward = (center - e.Position[1]).Normalized();
            e.Position[0] = center + outward * (radius + e.BodyRadius[0] + M(0.05));
            var hits = new List<int>();
            bool stunned = false;
            for (int k = 0; k < 60; k++)
            {
                var input = Press(k == 0 || k == 5 || k == 11);
                input.Aim = e.Position[1];
                sim.Step(input);
                foreach (var ev in sim.Events)
                {
                    if (ev.Type == SimEventType.Damage && ev.Target == 1) hits.Add(ev.Amount);
                    if (ev.Type == SimEventType.Stun && ev.Target == 1) stunned = true;
                }
            }
            CollectionAssert.AreEqual(new[] { 70, 70, 140 }, hits, "корпус в круге удара — валом второй раз не бьётся");
            Assert.IsFalse(stunned, "босс не оглушается");
        }

        /// <summary>
        /// Волнорез по боссу: задетый стеной корпус — 70 один раз, обрушение его не бьёт;
        /// корпус за концом стены (стена не дошла), но в 2 м обрушения — 35.
        /// </summary>
        [TestCase(10.5, 70)]
        [TestCase(11.5, 35)]
        public void Breakwater_Boss_WallOnceOrTheCrashIfTheWallMissed(double distance, int expected)
        {
            var sim = BossArena(Form(PelagForm.WreckBreakwater));
            var e = sim.Entities;
            FixVec2 boss = e.Position[1];
            e.Position[0] = boss - new FixVec2(M(distance), Fix64.Zero);
            var hits = new List<int>();
            int crashes = 0;
            for (int k = 0; k < 72; k++)
            {
                var input = Press(k == 0 || k == 5 || k == 11);
                input.Aim = boss;
                sim.Step(input);
                foreach (var ev in sim.Events)
                {
                    if (ev.Type == SimEventType.Damage && ev.Target == 1) hits.Add(ev.Amount);
                    if (ev.Type == SimEventType.WreckBreakwaterCrash) crashes++;
                }
            }
            Assert.AreEqual(1, crashes);
            CollectionAssert.AreEqual(new[] { expected }, hits);
        }

        // ---- снимок удара оземь до удара (вид ведёт голову якоря, HUD замирает) ----

        [Test]
        public void SlamGeometry_InTheSnapshotFromTheThirdPress_NotThePreviousSeries()
        {
            var sim = Arena();
            for (int i = 0; i < 37; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 20));
                if (tick < 12 || tick > 19) continue;
                var w = sim.Wreck;
                Assert.AreEqual(WreckPhase.Windup, w.Phase, "тик " + tick);
                // Выпад v4: шаг героя в тиках 17–20 — точка от героя этого тика.
                Assert.AreEqual(sim.Entities.Position[P].X.ToFloat() + 2.2f, w.ImpactPoint.X.ToFloat(), 1e-3f, "тик " + tick + ": герой + 2,2 м");
                Assert.AreEqual(1.2f, w.ImpactRadius.ToFloat(), 1e-3f);
                Assert.AreEqual(1f, w.LaneDir.X.ToFloat(), 1e-3f);
                Assert.AreEqual(0.75f, w.LaneHalfWidth.ToFloat(), 1e-3f);
            }
            // Вторая серия вверх: точка прошлой серии в замахе не висит.
            while (sim.Tick < 117) sim.Step(Press(false));
            int seen = 0;
            double hx = sim.Entities.Position[P].X.ToFloat();   // после выпада первой серии герой на 0,6 м по +X
            for (int i = 0; i < 40; i++)
            {
                sim.Step(Press(i < 20, hx, 5));
                var w = sim.Wreck;
                if (w.Stage != 2 || w.Phase != WreckPhase.Windup) continue;
                seen++;
                Assert.AreEqual(sim.Entities.Position[P].X.ToFloat(), w.ImpactPoint.X.ToFloat(), 1e-3f);
                Assert.AreEqual(sim.Entities.Position[P].Y.ToFloat() + 2.2f, w.ImpactPoint.Y.ToFloat(), 1e-3f);
                Assert.AreEqual(1f, w.LaneDir.Y.ToFloat(), 1e-3f);
            }
            Assert.AreEqual(8, seen, "замах выпада: 8 (v4)");
        }

        /// <summary>
        /// Девятый вал (06.10 вечером): в замахе выпада превью HUD — направление и заряды Sim; точка удара, круг,
        /// ширина и конец полосы — те, что лягут в удар (оба маха задели — ×2 и +2 м).
        /// </summary>
        [Test]
        public void NinthWave_LungeWindup_PreviewEqualsTheSlamGeometry()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            Guardian(sim, 1.5, 1.2);   // в секторе обоих махов, шагу выпада не мешает
            var impacts = new List<FixVec2>();
            var ends = new List<FixVec2>();
            Fix64 lastHalf = default;
            int windup = 0;
            for (int i = 0; i < 40; i++)
            {
                var input = Press(i == 0 || i == 6 || i == 12, 5, 0);
                if (i > 12) input.Aim = new FixVec2(M(0), M(5));   // курсор ушёл — превью стоит по Sim
                sim.Step(input);
                var w = sim.Wreck;
                if (w.Stage != 2 || w.Phase != WreckPhase.Windup) continue;
                windup++;
                Assert.AreEqual(2, w.NinthCharges, "оба маха задели до выпада");
                Assert.IsTrue(sim.WreckLanePreview(0, input.Aim, out FixVec2 origin, out FixVec2 dir, out _, out Fix64 to,
                    out Fix64 half, out FixVec2 impact, out Fix64 radius));
                Assert.AreEqual(w.Direction, dir, "полоса замерла по Sim, а не по курсору");
                Assert.AreEqual(w.ImpactRadius, radius);
                Assert.AreEqual(w.LaneHalfWidth, half);
                impacts.Add(impact);
                ends.Add(origin + dir * to);
                lastHalf = half;
            }
            Assert.AreEqual(8, windup, "замах выпада 8 — удержания нет");
            var s = sim.Wreck;
            Assert.AreEqual(1.5f, lastHalf.ToFloat(), 1e-3f, "полоса ×2");
            Assert.AreEqual(8f, s.LaneLength.ToFloat(), 1e-3f, "6 + 2 м");
            FixVec2 end = s.LaneOrigin + s.LaneDir * s.WallEnd;
            for (int k = 0; k < impacts.Count; k++)
            {
                Assert.AreEqual(s.ImpactPoint.X.ToFloat(), impacts[k].X.ToFloat(), 1e-3f, "удар — в точке превью до удара");
                Assert.AreEqual(s.ImpactPoint.Y.ToFloat(), impacts[k].Y.ToFloat(), 1e-3f);
                Assert.AreEqual(end.X.ToFloat(), ends[k].X.ToFloat(), 1e-3f, "конец полосы — тот же");
                Assert.AreEqual(end.Y.ToFloat(), ends[k].Y.ToFloat(), 1e-3f);
            }
            Assert.AreEqual(lastHalf, s.LaneHalfWidth);
        }

        /// <summary>Призрачный якорь по боссу: корпус в круге — 70, 70, 140 и якорь 210; босс не оглушается.</summary>
        [Test]
        public void GhostAnchor_Boss_HitByTheRing_NeverStunned()
        {
            var sim = BossArena(Form(PelagForm.WreckGhostAnchor));
            var e = sim.Entities;
            FixVec2 center = sim.ThicketHullCircleCenter(1, 0);
            Simulation.ThicketHullLocal(0, out _, out _, out Fix64 radius);
            FixVec2 outward = (center - e.Position[1]).Normalized();
            e.Position[0] = center + outward * (radius + e.BodyRadius[0] + M(0.05));
            var hits = new List<int>();
            bool stunned = false;
            int ghosts = 0;
            for (int k = 0; k < 60; k++)
            {
                var input = Press(k == 0 || k == 5 || k == 11);
                input.Aim = e.Position[1];
                sim.Step(input);
                foreach (var ev in sim.Events)
                {
                    if (ev.Type == SimEventType.Damage && ev.Target == 1) hits.Add(ev.Amount);
                    if (ev.Type == SimEventType.Stun && ev.Target == 1) stunned = true;
                    if (ev.Type == SimEventType.WreckGhostAnchor) ghosts++;
                }
            }
            Assert.AreEqual(1, ghosts);
            CollectionAssert.AreEqual(new[] { 70, 70, 140, 210 }, hits, "якорь — ×1,5 круга выпада");
            Assert.IsFalse(stunned, "босс не оглушается");
        }

        // ---- события и снимок ----

        [Test]
        public void Snapshot_And_Events_OfTheFastestSeries()
        {
            var sim = Arena();
            Guardian(sim, 3.0, 0);
            var clocks = new Dictionary<int, (int contact, int end)>();
            SimEvent slam = default, ended = default;
            var stages = new List<SimEvent>();
            int slamTick = -1;
            for (int i = 0; i < 52; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 20));
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.WreckStage) stages.Add(e);
                    if (e.Type == SimEventType.WreckSlam) { slam = e; slamTick = tick; }
                    if (e.Type == SimEventType.WreckEnded) ended = e;
                    if (e.Type == SimEventType.AbilityCast || e.Type == SimEventType.ActionStageStarted)
                        clocks[tick] = (sim.PlayerAction.ContactTick, sim.PlayerAction.EndTick);
                }
                var w = sim.Wreck;
                if (tick == 0)
                {
                    Assert.AreEqual(WreckPhase.Windup, w.Phase);
                    Assert.AreEqual(1, w.Serial);
                    Assert.AreEqual(0, w.Stage);
                    Assert.AreEqual(1, w.Side, "первый мах — справа налево");
                    Assert.AreEqual(0, w.CastTick);
                    Assert.AreEqual(5, w.ContactTick);
                    Assert.AreEqual(-1, w.OverheadTick);
                }
                if (tick == 6)
                {
                    Assert.AreEqual(-1, sim.Wreck.Side, "второй мах — слева направо (сабельный ритм v4)");
                    Assert.AreEqual(11, w.ContactTick, "второй мах: нажатие + 5");
                }
                if (tick == 12)
                {
                    Assert.AreEqual(2, w.Stage);
                    Assert.AreEqual(0, w.Side, "выпад — без стороны");
                    Assert.AreEqual(-1, w.OverheadTick, "у выпада базы тика «над головой» нет");
                    Assert.AreEqual(20, w.ContactTick, "выпад: нажатие + 8");
                }
                if (tick == 20)
                {
                    Assert.AreEqual(WreckPhase.Hold, w.Phase);
                    Assert.AreEqual(3, w.Strikes);
                    Assert.AreEqual(23, w.HoldEndTick);
                    Assert.AreEqual(6f, w.LaneLength.ToFloat(), 1e-3f);
                    Assert.AreEqual(0.75f, w.LaneHalfWidth.ToFloat(), 1e-3f);
                    Assert.AreEqual(1.2f, w.ImpactRadius.ToFloat(), 1e-3f);
                    Assert.AreEqual(2.8f, w.ImpactPoint.X.ToFloat(), 1e-3f, "выпад v4: герой 0,6 + 2,2");
                    Assert.AreEqual(100, w.DamagePercent);
                    Assert.AreEqual(0.5f, w.WaveStep.ToFloat(), 1e-3f);
                    Assert.IsTrue(sim.WreckHoldsHero);
                }
                if (tick == 23)
                {
                    Assert.AreEqual(WreckPhase.Exit, w.Phase);
                    Assert.AreEqual(26, w.ExitWalkTick);
                    Assert.AreEqual(31, w.ExitEndTick);
                }
            }
            Assert.AreEqual(3, stages.Count);
            for (int k = 0; k < 3; k++) { Assert.AreEqual(k, stages[k].Amount); Assert.AreEqual(k == 2, stages[k].Flag); }
            Assert.AreEqual(20, slamTick);
            Assert.AreEqual(8, slam.Amount, "шагов вала");
            Assert.IsFalse(slam.Flag);
            Assert.AreEqual((int)PelagForm.None, slam.ActionVariant);
            Assert.AreEqual(2.8f, slam.Position.X.ToFloat(), 1e-3f);
            Assert.AreEqual((int)WreckEnd.Done, ended.Amount);
            Assert.AreEqual(1, ended.ActionVariant, "номер серии");
            // Часы: контакт — удар этапа, конец — удар + 2 (махи), удар + 3 + 8 (выпад).
            Assert.AreEqual((5, 7), clocks[0]);
            Assert.AreEqual((11, 13), clocks[6]);
            Assert.AreEqual((20, 31), clocks[12]);
            Assert.IsFalse(sim.WreckActive);
            Assert.AreEqual(Fix64.Zero, sim.WreckDirection.X, "прежний доступ: вне серии — ноль");
        }

        // ---- 9. Хеш ----

        private static ulong[] HashRun(PelagForm form)
        {
            var nodes = new AbilityNode[SabreTalents.TalentsPerLine + 4];
            int count = SabreTalents.AppendNodes(SabreTalentLine.Wreck, SabreTalents.TalentsPerLine, nodes, 0);
            count = PelagForms.AppendFormNodes(form, nodes, count);
            System.Array.Resize(ref nodes, count);
            var sim = Arena(nodes);
            for (int k = 0; k < 8; k++) Guardian(sim, 1.5 + k * 0.9, (k % 3 - 1) * 0.7, 2000);
            var hashes = new ulong[160];
            for (int i = 0; i < hashes.Length; i++)
            {
                var input = Press(i < 26 || i > 140, 6, i * 0.02, hold: i < 60);
                if (i == 50) sim.ApplyAbilityDamage(1, P, 30, -1, DamageType.Physical);
                sim.Step(input);
                hashes[i] = sim.StateHash();
            }
            return hashes;
        }

        [TestCase(PelagForm.None)] [TestCase(PelagForm.WreckBreakwater)]
        [TestCase(PelagForm.WreckNinthWave)] [TestCase(PelagForm.WreckGhostAnchor)]
        public void TwoIdenticalRuns_HaveIdenticalHashes(PelagForm form)
            => CollectionAssert.AreEqual(HashRun(form), HashRun(form));

        [Test]
        public void SeriesChangesTheHash_WithoutItTheOldFieldsHashAsConstants()
        {
            var a = Arena();
            var b = Arena();
            for (int i = 0; i < 5; i++) { a.Step(Press(false)); b.Step(Press(false)); }
            Assert.AreEqual(a.StateHash(), b.StateHash());
            b.Step(Press(true));
            a.Step(Press(false));
            Assert.AreNotEqual(a.StateHash(), b.StateHash(), "серия в хеше");
        }
    }
}
