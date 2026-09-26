using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Общие метки на земле и читаемый ближний бой.
    ///
    /// Приёмка: удар Хранителя попадает только по нарисованной фигуре и ровно
    /// один раз; направление зафиксировано на весь замах, после удара моб
    /// стоит; оглушение, смерть и волок снимают и удар, и метку.
    /// </summary>
    public class EnemyTelegraphTests
    {
        private static readonly FixVec2 East = new FixVec2(Fix64.One, Fix64.Zero);

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena()
        {
            var sim = new Simulation(4242UL, 32);
            sim.SetupTestArena(0);
            sim.Entities.Stats[Simulation.PlayerId].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.RefreshStats(Simulation.PlayerId);
            sim.Entities.Health[Simulation.PlayerId] = 10000;
            return sim;
        }

        /// <summary>Хранитель с настоящей настройкой вида, лицом к герою, без критов.</summary>
        private static int Enemy(Simulation sim, FixVec2 at, EnemyKind kind = EnemyKind.ForestGuardian,
            bool stationary = true)
        {
            int id = sim.SpawnEnemy(at, 1000, kind);
            sim.Entities.Stats[id].SetBase(StatType.CritChance, Fix64.Zero);
            if (stationary) sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            return id;
        }

        /// <summary>Шагает до тика until; возвращает тики, на которых source ударил героя.</summary>
        private static List<int> Until(Simulation sim, int until, int source)
        {
            var hits = new List<int>();
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == source && e.Target == Simulation.PlayerId)
                        hits.Add(tick);
            }
            return hits;
        }

        // ---- геометрия ----

        [Test]
        public void Sector_CountsTheBodyOnItsEdges_ButNothingOutside()
        {
            var sector = EnemyTelegraph.Sector(FixVec2.Zero, East, Fix64.Ratio(12, 5), Fix64.Ratio(1, 2));
            Fix64 body = Fix64.Ratio(45, 100);
            Assert.IsTrue(Simulation.TelegraphContains(in sector, At(2.8, 0), body), "край по дальности");
            Assert.IsFalse(Simulation.TelegraphContains(in sector, At(2.9, 0), body), "за дальностью");
            // 70° от оси при раскрытии ±60°: центр вне угла, плечо на кромке.
            Assert.IsTrue(Simulation.TelegraphContains(in sector, At(0.547, 1.503), body), "кромка");
            Assert.IsFalse(Simulation.TelegraphContains(in sector, At(0.547, 1.503), Fix64.Zero), "точка без тела");
            Assert.IsFalse(Simulation.TelegraphContains(in sector, At(0, 1.6), body), "сбоку");
            Assert.IsFalse(Simulation.TelegraphContains(in sector, At(-1, 0), body), "за спиной");
        }

        [Test]
        public void CircleRingAndLane_UseTheSameBodyRule()
        {
            Fix64 body = Fix64.Ratio(1, 2);
            var circle = EnemyTelegraph.Circle(FixVec2.Zero, Fix64.One);
            Assert.IsTrue(Simulation.TelegraphContains(in circle, At(1.49, 0), body));
            Assert.IsFalse(Simulation.TelegraphContains(in circle, At(1.51, 0), body));

            var ring = EnemyTelegraph.Ring(FixVec2.Zero, Fix64.FromInt(2), Fix64.FromInt(5));
            Assert.IsFalse(Simulation.TelegraphContains(in ring, At(1.4, 0), body), "в дыре кольца");
            Assert.IsTrue(Simulation.TelegraphContains(in ring, At(1.6, 0), body));
            Assert.IsTrue(Simulation.TelegraphContains(in ring, At(0, -5.4), body));
            Assert.IsFalse(Simulation.TelegraphContains(in ring, At(0, -5.6), body));

            var lane = EnemyTelegraph.Lane(FixVec2.Zero, East, Fix64.FromInt(6), Fix64.FromInt(2));
            Assert.IsTrue(Simulation.TelegraphContains(in lane, At(3, 1.4), body));
            Assert.IsFalse(Simulation.TelegraphContains(in lane, At(3, 1.6), body));
            Assert.IsTrue(Simulation.TelegraphContains(in lane, At(6.4, 0), body));
            Assert.IsFalse(Simulation.TelegraphContains(in lane, At(-0.6, 0), body));
        }

        // ---- Хранитель ----

        [Test]
        public void GuardianSwing_OpensSharedSectorThatLandsAfterTwentyOneTicks()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);

            Assert.IsTrue(sim.TryGetEnemySwing(g, out var swing));
            Assert.AreEqual(0, swing.StartTick);
            Assert.AreEqual(21, swing.ImpactTick - swing.StartTick);
            Assert.AreEqual(15, swing.RecoverUntil - swing.ImpactTick);
            Assert.AreEqual(48, sim.Entities.NextAttackTick[g] - swing.StartTick);
            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.TelegraphOpened && e.Source == g && e.Flag && e.Amount == swing.Telegraph));

            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var t));
            Assert.AreEqual(TelegraphShape.Sector, t.Shape);
            Assert.AreEqual(TelegraphState.Active, t.State);
            Assert.IsTrue(t.SharedView);
            Assert.AreEqual(swing.ImpactTick, t.ImpactTick);
            Assert.AreEqual(Simulation.GuardianSwingRadius, t.Radius);
            Assert.AreEqual(Fix64.Ratio(1, 2), t.ArcCos);
            Assert.AreEqual(swing.Origin, t.Origin);
            Assert.AreEqual(swing.Direction, t.Direction);
        }

        // Сектор 2,2 м плюс тело героя 0,45: попадание до 2,65 м от Хранителя.
        [TestCase(2.6, 0, true)]
        [TestCase(2.7, 0, false)]
        [TestCase(2.0, 55, true)]
        [TestCase(1.6, 70, true)]
        [TestCase(1.6, 90, false)]
        public void GuardianSwing_HitsOnlyInsideTheDrawnShape(double distance, double degrees, bool hit)
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);
            // Ось удара смотрит на запад: герой стоял в нуле.
            double radians = degrees * System.Math.PI / 180;
            sim.Entities.Position[Simulation.PlayerId] =
                At(2 - distance * System.Math.Cos(radians), distance * System.Math.Sin(radians));

            var hits = Until(sim, 22, g);
            sim.TryGetEnemySwing(g, out var swing);
            Assert.IsTrue(swing.HitResolved);
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var t));
            Assert.AreEqual(TelegraphState.Resolved, t.State, "метка сработала и промахом");
            hits.AddRange(Until(sim, 40, g));
            CollectionAssert.AreEqual(hit ? new[] { 21 } : new int[0], hits);
        }

        [Test]
        public void GuardianSwing_HitsExactlyOncePerTelegraph_EveryFortyEightTicks()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            CollectionAssert.AreEqual(new[] { 21, 69 }, Until(sim, 96, g));
        }

        [Test]
        public void GuardianSwing_DoesNotTurnOrStepDuringWindup()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0), stationary: false);
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            FixVec2[] detours = { At(0, 3), At(2, 3), At(4.5, 0), At(-1, -2) };
            while (sim.Tick < swing.ImpactTick)
            {
                sim.Entities.Position[Simulation.PlayerId] = detours[sim.Tick % detours.Length];
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(swing.Direction, sim.Entities.Facing[g], "доворот на тике " + sim.Tick);
                Assert.AreEqual(swing.Origin, sim.Entities.Position[g], "шаг на тике " + sim.Tick);
            }
        }

        [Test]
        public void GuardianSwing_StandsForFifteenTicksAfterImpact()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0), stationary: false);
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            Until(sim, swing.ImpactTick + 1, g);
            sim.Entities.Position[Simulation.PlayerId] = At(-5, 0);
            while (sim.Tick < swing.RecoverUntil)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(swing.Origin, sim.Entities.Position[g], "окно наказания, тик " + sim.Tick);
            }
            sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(swing.Origin, sim.Entities.Position[g], "после окна моб снова идёт");
        }

        // ---- помехи ----

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void StunDeathAndDragCancelTheHitAndTheTelegraph(int reason)
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            Until(sim, 10, g);

            if (reason == 0) sim.Statuses.ApplyStun(g, 30);
            else if (reason == 1) sim.ApplyAbilityDamage(Simulation.PlayerId, g, 100000, -1, DamageType.Physical);
            else Assert.IsTrue(ForcedMotion.Begin(sim.Entities, g, At(3, 0), 4, ForcedMotionKind.Dragged));
            if (reason != 1) sim.Step(InputFrame.Empty);

            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.TelegraphCancelled && e.Source == g && e.Amount == swing.Telegraph));
            Assert.IsFalse(sim.TryGetEnemySwing(g, out _));
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out var t));
            Assert.AreEqual(TelegraphState.Cancelled, t.State);
            Assert.AreEqual(-1, sim.Entities.PendingAttackTarget[g]);
            CollectionAssert.IsEmpty(Until(sim, 47, g), "снятый замах ударил");
        }

        /// <summary>
        /// Старый баг: оглушённый моб пропускал тики с висящим замахом и бил
        /// в тот же тик, когда оглушение кончалось.
        /// </summary>
        [Test]
        public void StunOverTheImpactTick_DoesNotLandWhenTheStunEnds()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            Until(sim, 15, g);
            sim.Statuses.ApplyStun(g, 22);
            // Следующий замах — в свой срок 48, контакт 69.
            CollectionAssert.AreEqual(new[] { 69 }, Until(sim, 70, g));
        }

        [Test]
        public void AfterALongStun_NextSwingWaitsTwelveTicks()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            Until(sim, 5, g);
            sim.Statuses.ApplyStun(g, 60);
            Until(sim, 71, g);
            Assert.IsFalse(sim.TryGetEnemySwing(g, out _), "замах раньше паузы");
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(g, out var next));
            Assert.AreEqual(60 + Simulation.EnemySwingInterruptPauseTicks - 1, next.StartTick);
        }

        [Test]
        public void TelegraphExpiresAfterLinger_AndArenaResetClearsEverything()
        {
            var sim = Arena();
            int g = Enemy(sim, At(2, 0));
            sim.Step(InputFrame.Empty);
            sim.TryGetEnemySwing(g, out var swing);
            Until(sim, swing.ImpactTick + Simulation.TelegraphLingerTicks, g);
            Assert.IsTrue(sim.TryGetTelegraph(swing.Telegraph, out _), "вспышка ещё видна");
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetTelegraph(swing.Telegraph, out _));
            Assert.AreEqual(0, sim.TelegraphHighWater);

            Until(sim, 50, g);
            Assert.IsTrue(sim.TryGetEnemySwing(g, out _));
            sim.SetupTestArena(0);
            Assert.IsFalse(sim.TryGetEnemySwing(g, out _));
            Assert.AreEqual(0, sim.TelegraphHighWater);
            for (int slot = 0; slot < sim.TelegraphCapacity; slot++)
                Assert.IsFalse(sim.TryGetTelegraph(slot, out _));
        }

        // ---- Корнеполз ----

        [Test]
        public void RootSwarm_BitesWithoutTelegraph_LungesHalfMetre_AndHoldsEightTicks()
        {
            var sim = Arena();
            int s = Enemy(sim, At(1.2, 0), EnemyKind.ForestRootSwarm, stationary: false);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(s, out var swing));
            Assert.AreEqual(12, swing.ImpactTick - swing.StartTick);
            Assert.AreEqual(8, swing.RecoverUntil - swing.ImpactTick);
            Assert.AreEqual(Simulation.RootSwarmAttackCooldownTicks, sim.Entities.NextAttackTick[s] - swing.StartTick);
            Assert.AreEqual(-1, swing.Telegraph);
            Assert.That(sim.Events, Has.None.Matches<SimEvent>(e => e.Type == SimEventType.TelegraphOpened));

            // Герой ушёл вбок: укус мимо, но выпад во всю длину.
            sim.Entities.Position[Simulation.PlayerId] = At(0, 3);
            CollectionAssert.IsEmpty(Until(sim, swing.ImpactTick + 1, s));
            Assert.AreEqual((byte)ForcedMotionKind.EnemyLunge, sim.Entities.ForcedKind[s]);
            Assert.IsTrue(sim.TryGetEnemySwing(s, out _), "свой выпад не срывает замах");
            while (sim.Tick < swing.RecoverUntil) sim.Step(InputFrame.Empty);
            Assert.AreEqual(0.7, sim.Entities.Position[s].X.ToDouble(), 1e-4);
            Assert.AreEqual(0.0, sim.Entities.Position[s].Y.ToDouble(), 1e-4);
            var landed = sim.Entities.Position[s];
            sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(landed, sim.Entities.Position[s], "после восстановления снова бежит");
        }

        [Test]
        public void RootSwarm_BiteHitsOnce_AndLungeStopsAtTheHeroBody()
        {
            var sim = Arena();
            int s = Enemy(sim, At(1.2, 0), EnemyKind.ForestRootSwarm);
            CollectionAssert.AreEqual(new[] { 12 }, Until(sim, 20, s));
            Fix64 contact = sim.Entities.BodyRadius[s] + sim.Entities.BodyRadius[Simulation.PlayerId];
            Assert.GreaterOrEqual(sim.Entities.Position[s].X.ToDouble(), contact.ToDouble() - 1e-4,
                "выпад прошёл сквозь героя");
        }

        [Test]
        public void RootSwarm_LungeStopsAtTheWall()
        {
            var room = new ModuleDefinition("module.swarm_wall", 8, 8, new ModuleConnector[0],
                weight: 0, isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }), 2);
            Assert.That(map.TryPlace(0, 0, 0, 0), Is.EqualTo(0));
            var sim = new Simulation(4242UL, 16);
            sim.SetupRift(map, 4242UL, 0, 0, 100);
            int s = sim.SpawnEnemy(At(15.4, 8), 30, EnemyKind.ForestRootSwarm);
            sim.Entities.NextAttackTick[s] = int.MaxValue;
            ForcedMotion.Begin(sim.Entities, s, At(16.4, 8), Simulation.RootSwarmLungeTicks,
                ForcedMotionKind.EnemyLunge);
            for (int i = 0; i < 6; i++) sim.Step(InputFrame.Empty);
            Fix64 radius = sim.Entities.BodyRadius[s];
            Assert.IsTrue(map.IsWalkable(sim.Entities.Position[s], radius));
            Assert.LessOrEqual(sim.Entities.Position[s].X.ToDouble(), 16 - radius.ToDouble() + 1e-4);
        }

        // ---- Вендиго и Камнекопыт ----

        private static bool TelegraphOf(Simulation sim, int source, out EnemyTelegraph telegraph)
        {
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out telegraph) && telegraph.Source == source) return true;
            telegraph = default;
            return false;
        }

        private static Simulation WendigoAtClawRange()
        {
            var sim = new Simulation(55, 64); sim.SetupWendigoEncounter(null, 55);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed, Fix64.Zero); sim.Entities.RefreshStats(1);
            return sim;
        }

        [Test]
        public void WendigoClaw_RegistersItsOwnTelegraph_AndResolvesItOnContact()
        {
            var sim = WendigoAtClawRange();
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetWendigoAction(1, out var claw));
            Assert.IsTrue(TelegraphOf(sim, 1, out var t));
            Assert.AreEqual(TelegraphShape.Sector, t.Shape);
            Assert.IsFalse(t.SharedView, "коготь пока рисует собственный вид");
            Assert.AreEqual(claw.ImpactTick, t.ImpactTick);
            while (sim.Tick <= claw.ImpactTick) sim.Step(InputFrame.Empty);
            TelegraphOf(sim, 1, out t);
            Assert.AreEqual(TelegraphState.Resolved, t.State);
        }

        [Test]
        public void WendigoStun_CancelsItsTelegraph()
        {
            var sim = WendigoAtClawRange();
            sim.Step(InputFrame.Empty);
            sim.Statuses.ApplyStun(1, 100);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(TelegraphOf(sim, 1, out var t));
            Assert.AreEqual(TelegraphState.Cancelled, t.State);
        }

        [Test]
        public void StonehoofLane_IsRegistered_AndCancelledByStun()
        {
            var sim = new Simulation(76, 64); sim.SetupStonehoofEncounter(null, 76);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetStonehoofAction(1, out var charge));
            Assert.IsTrue(TelegraphOf(sim, 1, out var t));
            Assert.AreEqual(TelegraphShape.Lane, t.Shape);
            Assert.IsFalse(t.SharedView);
            Assert.AreEqual(charge.LaunchTick, t.ImpactTick);
            Assert.AreEqual(charge.Distance, t.Length);
            Assert.AreEqual(charge.Direction, t.Direction);

            sim.Statuses.ApplyStun(1, 100);
            sim.Step(InputFrame.Empty);
            TelegraphOf(sim, 1, out t);
            Assert.AreEqual(TelegraphState.Cancelled, t.State);
        }

        // ---- пулы общего вида ----
        //
        // GroundTelegraphView держит заранее собранные метки по фигурам (план
        // от 26.09: полосы 4 → 12, круги 8 → 16). Метка сверх пула не рисуется,
        // но бьёт, поэтому столько меток разом не должно быть ни в одной встрече.

        private const int ViewLanes = 12, ViewCircles = 16, ViewSectors = 16, ViewRings = 4;

        /// <summary>
        /// Сколько общих меток держит одна особь вида разом, с запасом. Шипомёт —
        /// линию из ThornLineSegments полос или круг всплеска (действие у него
        /// одно, считаем оба); Корнехват — свой круг: перезарядка длиннее круга
        /// с угасанием; Хранитель и Расщепень — сектор замаха; вендиго — кольцо
        /// воя. Коготь, прыжок и таран рисуют собственные виды мобов.
        /// </summary>
        private static void SharedMarksOf(EnemyKind kind, out int lanes, out int circles, out int sectors, out int rings)
        {
            lanes = circles = sectors = rings = 0;
            switch (kind)
            {
                case EnemyKind.ForestThorncaster: lanes = Simulation.ThornLineSegments; circles = 1; break;
                case EnemyKind.ForestRootSnarer: circles = 1; break;
                case EnemyKind.ForestGuardian: case EnemyKind.ForestSplitter: sectors = 1; break;
                case EnemyKind.ForestWendigo: rings = 1; break;
            }
        }

        [Test]
        public void WorstTemplate_FitsTheSharedViewPools_ByComposition()
        {
            int lanes = 0, circles = 0, sectors = 0, rings = 0;
            string worstLanes = "-", worstCircles = "-";
            foreach (var t in ArenaEncounterTests.EveryTemplate())
            {
                // Грубо сверху: все волны шаблона живы разом, каждая особь — с полным набором меток.
                int l = 0, c = 0, s = 0, r = 0;
                for (int w = 0; w < t.WaveCount; w++)
                {
                    var wave = t.GetWave(w);
                    for (int g = 0; g < wave.GroupCount; g++)
                    {
                        var group = wave.GetGroup(g);
                        SharedMarksOf(group.Kind, out int gl, out int gc, out int gs, out int gr);
                        l += group.Max * gl; c += group.Max * gc; s += group.Max * gs; r += group.Max * gr;
                    }
                }
                if (l > lanes) { lanes = l; worstLanes = t.Key; }
                if (c > circles) { circles = c; worstCircles = t.Key; }
                sectors = System.Math.Max(sectors, s);
                rings = System.Math.Max(rings, r);
            }
            TestContext.WriteLine("worst lanes " + lanes + " (" + worstLanes + "), circles " + circles + " (" + worstCircles
                + "), sectors " + sectors + ", rings " + rings);
            Assert.That(lanes, Is.LessThanOrEqualTo(ViewLanes));
            Assert.That(circles, Is.LessThanOrEqualTo(ViewCircles));
            Assert.That(sectors, Is.LessThanOrEqualTo(ViewSectors));
            Assert.That(rings, Is.LessThanOrEqualTo(ViewRings));
        }

        /// <summary>
        /// Живой прогон шаблонов новых видов на их последней арене: герой стоит
        /// у входа бессмертным, все сразу идут на него. Раз в полторы секунды он
        /// кладёт самого позднего из живых, кроме Шипомёта и Корнехвата; их —
        /// последними и не чаще раза в десять секунд, чтобы они успели дойти и
        /// ударить. Волны выходят, Расщепни распадаются. Каждый тик — счёт общих
        /// меток по фигурам против пулов вида.
        /// </summary>
        [Test]
        public void StagedTemplates_LiveRun_KeepSharedMarksInsideTheViewPools()
        {
            var location = ArenaEncounterTests.ForestLocation();
            int lanes = 0, circles = 0, sectors = 0, rings = 0;
            foreach (var t in ForestEncounterTemplates.Staged)
                for (ulong seed = 1; seed <= 2; seed++)
                {
                    int arena = t.MaxArena;
                    var map = ArenaEncounterTests.ArenaMap(location, arena, seed, System.Math.Max(3, t.MinArenaSize));
                    var sim = new Simulation(seed, 512);
                    location.GetLevel(arena).Spawn(sim, map, seed ^ 0x5151UL, t, arena);
                    sim.PlayerInvulnerable = true;
                    for (int tick = 0; tick < 3600 && (sim.EncounterWavesPending || sim.CountAliveEnemies() > 0); tick++)
                    {
                        int victim = -1, caster = -1;
                        for (int i = 1; i < sim.Entities.Count; i++)
                        {
                            if (!sim.Entities.Alive[i] || sim.Entities.Side[i] == Faction.Wole) continue;
                            sim.Entities.Aggro[i] = true;
                            var kind = sim.Entities.Kind[i];
                            if (kind == EnemyKind.ForestThorncaster || kind == EnemyKind.ForestRootSnarer) caster = i;
                            else victim = i;
                        }
                        if (victim > 0 ? tick % 45 == 44 : caster > 0 && tick % 300 == 299)
                            sim.ApplyAbilityDamage(Simulation.PlayerId, victim > 0 ? victim : caster, 1000000, -1,
                                DamageType.Physical);
                        sim.Step(InputFrame.Empty);
                        int l = 0, c = 0, s = 0, r = 0;
                        for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                        {
                            if (!sim.TryGetTelegraph(slot, out var mark) || !mark.SharedView) continue;
                            if (mark.Shape == TelegraphShape.Lane) l++;
                            else if (mark.Shape == TelegraphShape.Circle) c++;
                            else if (mark.Shape == TelegraphShape.Sector) s++;
                            else if (mark.Shape == TelegraphShape.Ring) r++;
                        }
                        Assert.That(l, Is.LessThanOrEqualTo(ViewLanes), t.Key + " seed " + seed + " tick " + tick);
                        Assert.That(c, Is.LessThanOrEqualTo(ViewCircles), t.Key + " seed " + seed + " tick " + tick);
                        Assert.That(s, Is.LessThanOrEqualTo(ViewSectors), t.Key + " seed " + seed + " tick " + tick);
                        Assert.That(r, Is.LessThanOrEqualTo(ViewRings), t.Key + " seed " + seed + " tick " + tick);
                        lanes = System.Math.Max(lanes, l); circles = System.Math.Max(circles, c);
                        sectors = System.Math.Max(sectors, s); rings = System.Math.Max(rings, r);
                    }
                }
            TestContext.WriteLine("live max: lanes " + lanes + ", circles " + circles + ", sectors " + sectors + ", rings " + rings);
            // Прогон что-то проверил: линии Шипомёта и круги Корнехвата правда падали.
            Assert.That(lanes, Is.GreaterThan(0), "ни одной линии шипов");
            Assert.That(circles, Is.GreaterThan(0), "ни одного круга");
        }

        // ---- детерминизм ----

        private static List<ulong> Brawl(ulong seed)
        {
            var sim = new Simulation(seed, 32);
            sim.SetupTestArena(0);
            sim.PlayerInvulnerable = true;
            for (int i = 0; i < 6; i++)
            {
                double a = i * System.Math.PI / 3;
                Enemy(sim, At(2.2 * System.Math.Cos(a), 2.2 * System.Math.Sin(a)), stationary: false);
            }
            for (int i = 0; i < 4; i++)
            {
                double a = i * System.Math.PI / 2 + 0.4;
                Enemy(sim, At(4 * System.Math.Cos(a), 4 * System.Math.Sin(a)), EnemyKind.ForestRootSwarm, false);
            }
            var hashes = new List<ulong>();
            for (int t = 0; t < 400; t++)
            {
                if (t == 40) sim.Statuses.ApplyStun(1, 70);
                var input = new InputFrame { Flags = (byte)InputFlags.MoveOrder,
                    Aim = At(t / 60 % 2 == 0 ? -3 : 3, t / 90 % 2 == 0 ? 2 : -2) };
                sim.Step(input);
                hashes.Add(sim.StateHash());
            }
            return hashes;
        }

        [Test]
        public void SameSeedSameInputs_GiveTheSameTelegraphsAndSwingsEveryTick()
        {
            CollectionAssert.AreEqual(Brawl(0xBEEFUL), Brawl(0xBEEFUL));
        }

        [Test]
        public void StateHash_SeesTelegraphs()
        {
            var a = Arena(); var b = Arena();
            Assert.AreEqual(a.StateHash(), b.StateHash());
            a.OpenTelegraph(0, EnemyTelegraph.Circle(FixVec2.Zero, Fix64.One), 10, 16, TelegraphFlags.None);
            Assert.AreNotEqual(a.StateHash(), b.StateHash());
        }
    }
}
