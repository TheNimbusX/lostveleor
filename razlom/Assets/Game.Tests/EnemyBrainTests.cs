using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// ИИ мобов v2 (27.09): путь в обход препятствий, места вокруг героя и
    /// устойчивые позиции и очередь атак, выход из чужих меток, агро пачки, бюджет крупных меток
    /// на земле, Камнекопыт без застреваний.
    /// </summary>
    public class EnemyBrainTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Комната 20×20 с центром в нуле; камни — список (x, y, радиус).</summary>
        private static LayoutMap Room(params double[] rocks)
        {
            var room = new ModuleDefinition("brain.test", 10, 10, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }));
            map.TryPlace(0, 0, -5, -5);
            for (int k = 0; k + 2 < rocks.Length; k += 3)
                map.AddTestObstacle(new LayoutObstacle(At(rocks[k], rocks[k + 1]), Fix64.FromDouble(rocks[k + 2]), 0));
            map.BuildRoutes();
            return map;
        }

        private static Simulation Sim(LayoutMap map, FixVec2 hero)
        {
            var sim = new Simulation(4242UL, 64);
            if (map == null) sim.SetupTestArena(0);
            else sim.SetupRift(map, 4242UL, 0, 0, 100);
            sim.PlayerInvulnerable = true;
            sim.Entities.Position[Simulation.PlayerId] = hero;
            return sim;
        }

        private static int Enemy(Simulation sim, FixVec2 at, EnemyKind kind = EnemyKind.ForestGuardian)
        {
            int id = sim.SpawnEnemy(at, 5000, kind);
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            sim.Grid.Rebuild(sim.Entities);
            return id;
        }

        private static double Dist(Simulation sim, int a, int b)
            => FixVec2.Distance(sim.Entities.Position[a], sim.Entities.Position[b]).ToDouble();

        // ---------- путь ----------

        [Test]
        public void GuardianWalksAroundAWallOfRocks_InsteadOfJitteringBehindIt()
        {
            // Стена камней поперёк прямой к герою: от y = −5 до 5 при комнате ±10.
            var map = Room(0, -4, 1, 0, -2, 1, 0, 0, 1, 0, 2, 1, 0, 4, 1);
            var sim = Sim(map, At(-5, 0));
            int guardian = Enemy(sim, At(4, 0.3));
            int reached = -1;
            for (int t = 0; t < 240 && reached < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                if (Dist(sim, guardian, 0) < 3.2) reached = t;
            }
            Assert.That(reached, Is.GreaterThanOrEqualTo(0), "так и не обошёл стену");
            Assert.That(reached, Is.LessThan(200));
        }

        [Test]
        public void HealerWalksAroundRockTowardItsAllyInsteadOfTheHero()
        {
            var sim = Sim(Room(0, 2.3, .65), At(-5, 0));
            int healer = Enemy(sim, FixVec2.Zero, EnemyKind.ForestRootSnarer);
            int ally = Enemy(sim, At(0, 4.6));
            sim.Entities.Stats[ally].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(ally);
            sim.Entities.NextAttackTick[ally] = int.MaxValue;
            sim.Entities.Health[ally] = sim.Entities.MaxHealth[ally] * 7 / 10;
            bool mending = false;
            for (int tick = 0; tick < 240; tick++)
            {
                sim.Step(InputFrame.Empty);
                if (!sim.TryGetRootSnarerAction(healer, out var action) || action.Action != RootSnarerAction.Mend) continue;
                mending = true;
                Assert.That(Dist(sim, healer, ally), Is.LessThan(2.7), "упёрся в камень вместо обхода к союзнику");
                Assert.That(sim.Entities.Position[healer].Y.ToDouble(), Is.GreaterThan(2), "маршрут подменён героем слева");
                break;
            }
            Assert.That(mending, Is.True);
        }

        [Test]
        public void NavigationTargetCacheDoesNotChangeTheChosenRoute()
        {
            var map = Room(0, -2, .8, 0, 0, .8, 0, 2, .8);
            var reused = new EnemyNavGrid(map);
            var from = At(5, 0);
            for (int pass = 0; pass < 2; pass++)
                for (int goalIndex = 0; goalIndex < 36; goalIndex++)
                {
                    var goal = At(-8 + 2 * (goalIndex % 6), -6 + 2 * (goalIndex / 6));
                    var fresh = new EnemyNavGrid(map);
                    bool expected = fresh.TryHeading(from, goal, Fix64.Ratio(1, 2), out var expectedHeading);
                    Assert.That(reused.TryHeading(from, goal, Fix64.Ratio(1, 2), out var actual), Is.EqualTo(expected));
                    Assert.That(actual, Is.EqualTo(expectedHeading), "вытеснение поля изменило путь к цели " + goalIndex);
                }
        }

        // ---------- места вокруг героя ----------

        [Test]
        public void GuardiansSpreadAroundTheHero_AndWaitOutsideTheSector()
        {
            var sim = Sim(null, FixVec2.Zero);
            // Шесть Хранителей одной кучей с одной стороны.
            int first = Enemy(sim, At(7, 0));
            for (int k = 1; k < 6; k++) Enemy(sim, At(7 + 0.4 * k, (k % 2 == 0 ? 1 : -1) * 0.5 * k));
            int spreadTicks = 0, measured = 0;
            for (int t = 0; t < 300; t++)
            {
                sim.Step(InputFrame.Empty);
                if (t < 120) continue;
                measured++;
                // Кольцо разошлось: у героя не меньше трёх разных сторон занято.
                int sides = 0;
                bool north = false, south = false, east = false, west = false;
                for (int i = first; i < first + 6; i++)
                {
                    var p = sim.Entities.Position[i];
                    if (p.X.ToDouble() > 1) east = true;
                    if (p.X.ToDouble() < -1) west = true;
                    if (p.Y.ToDouble() > 1) north = true;
                    if (p.Y.ToDouble() < -1) south = true;
                }
                sides = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);
                if (sides >= 3) spreadTicks++;
            }
            Assert.That(spreadTicks, Is.GreaterThan(measured * 8 / 10), "стоят кучей с одной стороны");
        }

        [Test]
        public void AtMostTwoGuardiansSwingAtOnce_AndWaitersKeepApart()
        {
            var sim = Sim(null, FixVec2.Zero);
            int first = Enemy(sim, At(5, 0));
            for (int k = 1; k < 6; k++) Enemy(sim, FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 6)) * Fix64.FromInt(5));
            int close = 0, samples = 0;
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                int swinging = 0;
                for (int i = first; i < first + 6; i++)
                    if (sim.TryGetEnemySwing(i, out var s) && !s.HitResolved) swinging++;
                Assert.That(swinging, Is.LessThanOrEqualTo(Simulation.MeleeAttackTokenLimit), "tick " + t);
                if (t < 60) continue;
                for (int i = first; i < first + 6; i++)
                    for (int j = i + 1; j < first + 6; j++)
                    {
                        samples++;
                        if (Dist(sim, i, j) < 1.2) close++;
                    }
            }
            Assert.That(close, Is.LessThan(samples / 20), "ждущие стоят вплотную");
        }

        [Test]
        public void WaitingPackSettlesInsteadOfOrbitingOnATimer()
        {
            var sim = Sim(null, FixVec2.Zero);
            int first = Enemy(sim, At(3, 0));
            for (int k = 1; k < 5; k++) Enemy(sim, FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 5)) * Fix64.FromInt(3));
            for (int i = first; i < first + 5; i++) sim.Entities.NextAttackTick[i] = int.MaxValue;
            for (int t = 0; t < 180; t++) sim.Step(InputFrame.Empty);
            var before = new FixVec2[5];
            for (int k = 0; k < 5; k++) before[k] = sim.Entities.Position[first + k];
            for (int t = 0; t < 240; t++) sim.Step(InputFrame.Empty);
            for (int k = 0; k < 5; k++)
                Assert.That(FixVec2.Distance(before[k], sim.Entities.Position[first + k]).ToDouble(),
                    Is.LessThan(.35), "ожидающий моб продолжает бесцельно кружить");
        }

        [Test]
        public void EveryReadyGuardianGetsAnAttackOpportunity()
        {
            var sim = Sim(null, FixVec2.Zero);
            int[] ids = new int[6];
            var attacked = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < ids.Length; k++)
                ids[k] = Enemy(sim, FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, ids.Length)) * Fix64.FromInt(3));
            for (int tick = 0; tick < 600; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source != Simulation.PlayerId) attacked.Add(e.Source);
            }
            foreach (int id in ids) Assert.That(attacked.Contains(id), Is.True, "враг голодает в очереди: " + id);
        }

        [Test]
        public void GoodBudPositionDoesNotOrbitWithoutAReason()
        {
            var sim = Sim(null, FixVec2.Zero);
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud);
            sim.Entities.NextAttackTick[bud] = int.MaxValue;
            var before = sim.Entities.Position[bud];
            for (int tick = 0; tick < 180; tick++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(before, sim.Entities.Position[bud]).ToDouble(), Is.LessThan(.1));
        }

        [TestCase(EnemyKind.ForestGuardian, 9)]
        [TestCase(EnemyKind.ForestRootSwarm, 7)]
        public void PackLargerThanItsRingLetsEveryEnemyAttack(EnemyKind kind, int count)
        {
            var sim = Sim(null, FixVec2.Zero);
            int[] ids = new int[count];
            var attacks = new int[sim.Entities.Capacity];
            for (int k = 0; k < count; k++)
                ids[k] = Enemy(sim, FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, count)) * Fix64.FromInt(5), kind);
            for (int tick = 0; tick < 1800; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source != Simulation.PlayerId) attacks[e.Source]++;
            }
            foreach (int id in ids)
                Assert.That(attacks[id], Is.GreaterThanOrEqualTo(2), "место так и не уступили врагу " + id);
        }

        [Test]
        public void StunnedOwnersDoNotBlockTheReadyEnemyOutsideTheRing()
        {
            var sim = Sim(null, FixVec2.Zero);
            int first = Enemy(sim, At(5, 0));
            for (int k = 1; k < 9; k++)
                Enemy(sim, FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 9)) * Fix64.FromInt(5));
            sim.Step(InputFrame.Empty);
            for (int id = first; id < first + 8; id++) sim.Statuses.ApplyStun(id, 600);
            int ready = first + 8;
            bool attacked = false;
            for (int tick = 0; tick < 300 && !attacked; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source == ready) attacked = true;
            }
            Assert.That(attacked, Is.True, "оглушённые владельцы удержали все восемь мест");
        }

        // ---------- чужие метки ----------

        [Test]
        public void MobsStepOutOfAnAllysCircle()
        {
            var sim = Sim(null, FixVec2.Zero);
            int owner = Enemy(sim, At(0, 8), EnemyKind.ForestGuardian);
            int bystander = Enemy(sim, At(3, 0));
            sim.Entities.NextAttackTick[bystander] = int.MaxValue;
            sim.Step(InputFrame.Empty);
            sim.OpenTelegraph(owner, EnemyTelegraph.Circle(sim.Entities.Position[bystander], Fix64.FromInt(2)),
                sim.Tick + 40, sim.Tick + 46, TelegraphFlags.SharedView);
            var center = sim.Entities.Position[bystander];
            for (int t = 0; t < 30; t++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(sim.Entities.Position[bystander], center).ToDouble(), Is.GreaterThan(2.0),
                "стоит в чужом круге");
        }

        // ---------- агро пачки ----------

        [Test]
        public void PackMateNoticesWhenANeighbourDoes()
        {
            var sim = Sim(null, FixVec2.Zero);
            int near = sim.SpawnEnemy(At(6, 0), 5000, EnemyKind.ForestGuardian);
            int far = sim.SpawnEnemy(At(11, 0), 5000, EnemyKind.ForestGuardian);
            sim.Grid.Rebuild(sim.Entities);
            int farAggro = -1;
            for (int t = 0; t < 60 && farAggro < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.Entities.Aggro[far]) farAggro = t;
            }
            Assert.That(sim.Entities.Aggro[near], Is.True);
            Assert.That(farAggro, Is.GreaterThanOrEqualTo(0), "сосед в 5 м так и не заметил");
        }

        // ---------- Камнекопыт ----------

        [Test]
        public void StonehoofWalksToAClearLane_InsteadOfChargingTheRock()
        {
            // Камень между кабаном и героем: прямая полоса упирается в него.
            var map = Room(2.5, 0, 1.2);
            var sim = Sim(map, At(-2, 0));
            int boar = Enemy(sim, At(5.5, 0), EnemyKind.ForestStonehoof);
            sim.Entities.NextAttackTick[boar] = 0;
            int started = -1;
            StonehoofActionState charge = default;
            for (int t = 0; t < 450 && started < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.TryGetStonehoofAction(boar, out charge)) started = charge.StartTick;
            }
            Assert.That(started, Is.GreaterThanOrEqualTo(0), "так и не нашёл чистую полосу");
            Assert.That(charge.StopReason, Is.EqualTo(StonehoofStop.ArenaEdge), "таран в камень");
            Assert.That(charge.Distance.ToDouble(), Is.GreaterThanOrEqualTo(3.0));
        }

        [Test]
        public void StonehoofChargeEndsFiveMetresPastTheHero()
        {
            var sim = Sim(null, FixVec2.Zero);
            int boar = Enemy(sim, At(4, 0), EnemyKind.ForestStonehoof);
            sim.Entities.NextAttackTick[boar] = 0;
            sim.Entities.Facing[boar] = At(-1, 0);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetStonehoofAction(boar, out var charge), Is.True);
            Assert.That(charge.Distance.ToDouble(), Is.EqualTo(9.0).Within(0.06), "герой в 4 м плюс 5");
        }

        [Test]
        public void RestingStonehoofBacksOffFacingTheHero()
        {
            // 2,5 м: ближе порога отхода (3 м), но между телами 1,35 м — клыкам не
            // достать (метр от тела, ревью 29.09). Ближе кабан сперва бьёт клыками и
            // стоит до конца взмаха — это StonehoofTests.Tusk_*; здесь только отход.
            var sim = Sim(null, FixVec2.Zero);
            int boar = Enemy(sim, At(2.5, 0), EnemyKind.ForestStonehoof);
            sim.Entities.NextAttackTick[boar] = 10000;
            sim.Entities.Facing[boar] = At(-1, 0);
            for (int t = 0; t < 45; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Dot(sim.Entities.Facing[boar], At(-1, 0)).ToDouble(), Is.GreaterThan(0.9),
                    "повернулся спиной, тик " + t);
            }
            Assert.That(Dist(sim, boar, 0), Is.GreaterThan(3.5), "не отошёл от прижавшего героя");
        }

        [Test]
        public void StonehoofNeverFreezesOnAForestArena()
        {
            var location = ArenaEncounterTests.ForestLocation();
            int frozenWindows = 0, windows = 0, charges = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var map = ArenaEncounterTests.ArenaMap(location, 3, seed);
                var sim = new Simulation(seed, 64);
                sim.SetupStonehoofEncounter(map, seed);
                sim.PlayerInvulnerable = true;
                // Герой уходит по кругу вокруг центра поляны — кабан обязан догонять.
                var center = map.GetGlade(0).Center;
                FixVec2 last = sim.Entities.Position[1];
                int lastSerial = 0;
                for (int t = 0; t < 900; t++)
                {
                    var angle = Fix64.TwoPi * Fix64.Ratio(t, 600);
                    var hero = map.ClampToWalkable(center + FixVec2.FromAngle(angle) * Fix64.FromInt(7), sim.Entities.BodyRadius[0]);
                    sim.Entities.Position[0] = hero;
                    sim.Step(InputFrame.Empty);
                    if (sim.TryGetStonehoofAction(1, out var a) && a.Serial != lastSerial) { lastSerial = a.Serial; charges++; }
                    if (t % 120 != 119) continue;
                    bool acting = sim.TryGetStonehoofAction(1, out _);
                    bool resting = sim.Tick < sim.Entities.NextAttackTick[1];
                    double far = FixVec2.Distance(sim.Entities.Position[1], hero).ToDouble();
                    if (!acting && !resting && far > 7.5)
                    {
                        windows++;
                        if (FixVec2.Distance(sim.Entities.Position[1], last).ToDouble() < 1.0) frozenWindows++;
                    }
                    last = sim.Entities.Position[1];
                }
            }
            Assert.That(frozenWindows, Is.LessThanOrEqualTo(windows / 10), "застрял в " + frozenWindows + " из " + windows);
            Assert.That(charges, Is.GreaterThanOrEqualTo(12), "таранов почти нет");
        }

        // ---------- бюджет крупных меток ----------

        [Test]
        public void BigMarksStayWithinTheBudget_AndStartStaggered()
        {
            var location = ArenaEncounterTests.ForestLocation();
            string[] keys = { "forest.E05", "forest.E09", "forest.E10", "forest.E14", "forest.E06" };
            int checkedRuns = 0;
            foreach (var template in ForestEncounterTemplates.All)
            {
                if (System.Array.IndexOf(keys, template.Key) < 0) continue;
                foreach (int arena in new[] { template.MinArena, template.MaxArena })
                    for (ulong seed = 1; seed <= 4; seed++)
                    {
                        var map = ArenaEncounterTests.ArenaMap(location, arena, seed, template.MinArenaSize < 3 ? 3 : template.MinArenaSize);
                        var sim = new Simulation(seed, 512);
                        sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(arena);
                        sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(arena);
                        location.GetLevel(arena).Spawn(sim, map, seed ^ 0x5151UL, template, arena, 100);
                        sim.PlayerInvulnerable = true;
                        sim.Entities.Position[0] = map.ClampToWalkable(map.GetGlade(0).Center, sim.Entities.BodyRadius[0]);
                        int previousStart = int.MinValue;
                        for (int t = 0; t < 900; t++)
                        {
                            sim.Step(InputFrame.Empty);
                            int load = sim.BigMarkLoad(out int lastStart);
                            // Всплеск Шипомёта — самозащита, он бюджет может превысить на единицу.
                            Assert.That(load, Is.LessThanOrEqualTo(sim.BigMarkBudget + 1), template.Key + " A" + arena + " seed " + seed + " tick " + t);
                            if (lastStart != int.MinValue && lastStart != previousStart && previousStart != int.MinValue
                                && lastStart > previousStart && lastStart >= sim.Tick - 2)
                                Assert.That(lastStart - previousStart, Is.GreaterThanOrEqualTo(Simulation.BigMarkStaggerTicks - 1),
                                    template.Key + " tick " + t);
                            if (lastStart != int.MinValue) previousStart = lastStart;
                        }
                        checkedRuns++;
                    }
            }
            Assert.That(checkedRuns, Is.GreaterThan(10));
        }

        // ---------- детерминизм ----------

        [Test]
        public void SameSeedGivesTheSameFight()
        {
            var location = ArenaEncounterTests.ForestLocation();
            var template = ForestEncounterTemplates.All[ForestEncounterTemplates.All.Length - 1];
            Simulation Make()
            {
                var map = ArenaEncounterTests.ArenaMap(location, template.MaxArena, 9, template.MinArenaSize < 3 ? 3 : template.MinArenaSize);
                var sim = new Simulation(9, 512);
                location.GetLevel(template.MaxArena).Spawn(sim, map, 9 ^ 0x5151UL, template, template.MaxArena, 100);
                sim.PlayerInvulnerable = true;
                return sim;
            }
            var a = Make(); var b = Make();
            for (int t = 0; t < 600; t++)
            {
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t);
            }
        }
    }
}
