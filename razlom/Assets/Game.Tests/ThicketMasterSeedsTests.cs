using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Хозяин Чащи — «Терновник» (владелец 08.10, Simulation.ForestBoss.Seeds.cs; заменил «Веер шипов-семян»):
    /// жест каста, 2 / 3 куста в случайных местах пола через 9 тиков, у каждого 4 линии крестом (+ / ×) с
    /// прорастания, через 30 — по шипу на линию; одно попадание на каст; кусты вянут и уходят. Честные места
    /// (пол, герой, корпус, друг от друга, метки), линии = пути шипов, попадание только на линии, бюджет,
    /// наслоение, Часы, смерть босса, детерминизм.
    ///
    /// Стенд — SetupKindTestArena на арене 9 (без карты: путь шипа — все 9 м): герой в (0, 0), босс в distance м
    /// по +X лицом к герою, стоит (ход 0); из кастов готов только терновник (Only). Места на настоящем полу —
    /// «К боссу» (RiftRun.StartTestAtLevel(9, true)).
    /// </summary>
    public sealed class ThicketMasterSeedsTests
    {
        private const int Boss = 1;
        private const int IntroDone = Simulation.ThicketMinSleepTicks + Simulation.ThicketWakeTicks
            + Simulation.ThicketRoarWindupTicks + Simulation.ThicketRoarRecoveryTicks;   // 180

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena(double distance)
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(distance));
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.RefreshStats(Boss);
            return sim;
        }

        private static readonly ThicketMasterAction[] Specials =
        {
            ThicketMasterAction.Dive, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen, ThicketMasterAction.Rain,
            ThicketMasterAction.Storm, ThicketMasterAction.Seeds,
        };

        /// <summary>Из кастов, нырка и терновника готовы только allowed; топот в жребии закрыт.</summary>
        private static void Only(Simulation sim, params ThicketMasterAction[] allowed) => Only(sim, Boss, allowed);

        private static void Only(Simulation sim, int boss, params ThicketMasterAction[] allowed)
        {
            foreach (var action in Specials)
                if (System.Array.IndexOf(allowed, action) < 0) sim.SetThicketReadyTick(boss, action, int.MaxValue / 2);
            if (System.Array.IndexOf(allowed, ThicketMasterAction.Dive) < 0) sim.SetThicketDiveDueTick(boss, int.MaxValue / 2);
            sim.SetThicketStompPickReadyTick(boss, int.MaxValue / 2);
        }

        private static void Until(Simulation sim, int tick)
        {
            while (sim.Tick < tick) sim.Step(InputFrame.Empty);
        }

        /// <summary>Шагает, пока не начнётся действие kind (Amount 0); тик начала, −1 — не началось за limit.</summary>
        private static int RunUntilStarted(Simulation sim, EnemyActionKind kind, int limit, System.Action<Simulation> before = null)
        {
            for (int k = 0; k < limit; k++)
            {
                int tick = sim.Tick;
                before?.Invoke(sim);
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)kind && e.Amount == 0) return tick;
            }
            return -1;
        }

        /// <summary>Шагает до конца каста (шипы встали), держа героя через hold; события терновника — {тип, тик, Amount, Flag}.</summary>
        private static List<int[]> FlyOut(Simulation sim, System.Action<Simulation> hold, int limit = 160)
        {
            var log = new List<int[]>();
            for (int k = 0; k < limit; k++)
            {
                hold?.Invoke(sim);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketSeeds && e.Source == Boss)
                        log.Add(new[] { (int)e.Type, tick, e.Amount, e.Flag ? 1 : 0 });
                if (!sim.TryGetThicketSeedVolley(Boss, out _)) break;
            }
            return log;
        }

        private static int CountOf(List<int[]> log, SimEventType type, int flag = -1)
        {
            int n = 0;
            foreach (var r in log) if (r[0] == (int)type && (flag < 0 || r[3] == flag)) n++;
            return n;
        }

        private static List<ThicketBushState> Bushes(Simulation sim, int boss = Boss)
        {
            var list = new List<ThicketBushState>();
            for (int k = 0; k < Simulation.ThicketBushSlots; k++)
                if (sim.TryGetThicketBush(boss, k, out var b)) list.Add(b);
            return list;
        }

        /// <summary>Линия lane куста b полной длины (без карты — 9 м): фигура, по которой бьёт шип.</summary>
        private static EnemyTelegraph FullLane(in ThicketBushState b, int lane)
        {
            FixVec2 dir = Simulation.ThicketBushLaneDirection(b.Axis, lane);
            return Simulation.ThicketSeedLane(b.Center + dir * Simulation.ThicketBushThornStart, dir, Simulation.ThicketBushLaneLength);
        }

        /// <summary>Тело героя (0,45) в point задето хоть одной линией кустов (с запасом margin).</summary>
        private static bool OnAnyLane(List<ThicketBushState> bushes, FixVec2 point, double margin)
        {
            var body = Fix64.FromDouble(0.45 + margin);
            foreach (var b in bushes)
                for (int lane = 0; lane < 4; lane++)
                    if (Simulation.TelegraphContains(FullLane(b, lane), point, body)) return true;
            return false;
        }

        /// <summary>Фаза 1, герой в 7 м перед мордой (средняя полоса): терновник в тик конца вступления.</summary>
        private static Simulation StartPhaseOne(double distance, out int start, out ThicketMasterState a)
        {
            var sim = Arena(distance);
            Only(sim, ThicketMasterAction.Seeds);
            start = RunUntilStarted(sim, EnemyActionKind.ThicketSeeds, IntroDone + 10);
            Assert.AreEqual(IntroDone, start, "терновник — первым после вступления: герой в средней полосе");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out a));
            return sim;
        }

        // ---------- жест, кусты, линии = пути ----------

        [Test]
        public void PhaseOne_GroundCast18_TwoBushes9Apart_FourCrossLanesFromTheSprout_ExactlyTheThornPaths_Launch30_Wither()
        {
            var sim = StartPhaseOne(7, out int start, out var a);
            Assert.AreEqual(ThicketMasterAction.Seeds, a.Action);
            Assert.AreEqual(1, a.Stages);
            Assert.IsTrue(a.HitResolved, "жест каста — без контакта, как прорастание");
            Assert.AreEqual(start + Simulation.ThicketCastGestureTicks, a.EndTick, "жест 18");
            Assert.AreEqual(a.EndTick, a.ImpactTick);
            Assert.AreEqual(2, a.Tag, "фаза 1 — два куста");
            int sproutEvents = 0;
            var bushes = Bushes(sim);
            Assert.AreEqual(2, bushes.Count);
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds && e.Amount == 1)
                {
                    sproutEvents++;
                    Assert.AreEqual(bushes[0].Center, e.Position, "Started(Seeds, 1) — центр куста 0");
                }
            Assert.AreEqual(1, sproutEvents, "куст 0 прорастает в тик каста");
            Assert.AreNotEqual(bushes[0].Diagonal, bushes[1].Diagonal, "кресты по очереди: + и ×");
            for (int k = 0; k < 2; k++)
            {
                var b = bushes[k];
                Assert.AreEqual(k, b.Order);
                Assert.AreEqual(a.Serial, b.Cast);
                Assert.AreEqual(start + 9 * k, b.SproutTick, "через 9 тиков друг за другом");
                Assert.AreEqual(b.SproutTick + 30, b.LaunchTick, "растёт 30 тиков");
                Assert.AreEqual(b.LaunchTick + 12, b.WitherTick, "стоит 12 после выпуска");
                Assert.AreEqual(b.WitherTick + 24, b.GoneTick, "вянет 24");
                Assert.AreEqual(Simulation.ThicketBushAxis(b.Diagonal), b.Axis);
            }
            Assert.IsTrue(bushes[0].Sprouted);
            Assert.IsFalse(bushes[1].Sprouted, "куст 1 ещё не пророс");
            int launch0 = bushes[0].LaunchTick;
            for (int lane = 0; lane < 4; lane++)
            {
                Assert.IsTrue(sim.TryGetThicketSeed(Boss, lane, out var s), "шип " + lane);
                FixVec2 dir = Simulation.ThicketBushLaneDirection(bushes[0].Axis, lane);
                Assert.AreEqual(lane, s.Index, "место пула = Index = куст × 4 + линия");
                Assert.AreEqual(a.Serial, s.Volley);
                Assert.IsFalse(s.Released, "куст растёт — только линия");
                Assert.AreEqual(launch0, s.ReleaseTick);
                Assert.AreEqual(dir, s.Direction);
                Assert.AreEqual(bushes[0].Center + dir * Simulation.ThicketBushThornStart, s.Origin, "путь — от 0,3 м от центра куста");
                Assert.AreEqual(Simulation.ThicketBushLaneLength, s.Length, "без карты — все 9 м");
                Assert.IsTrue(sim.TryGetTelegraph(sim.FindTelegraph(s.TelegraphSerial), out var lane0), "линия на земле");
                Assert.AreEqual(TelegraphShape.Lane, lane0.Shape);
                Assert.IsTrue(lane0.SharedView, "общая метка — рисует GroundTelegraphView");
                Assert.AreEqual(TelegraphState.Active, lane0.State);
                Assert.AreEqual(start, lane0.StartTick, "с прорастания");
                Assert.AreEqual(launch0, lane0.ImpactTick, "заполнение — к выпуску");
                Assert.AreEqual(s.Origin, lane0.Origin);
                Assert.AreEqual(s.Direction, lane0.Direction);
                Assert.AreEqual(s.Length, lane0.Length);
                Assert.AreEqual(Simulation.ThicketSeedRadius * 2, lane0.Width);
            }
            for (int i = 4; i < Simulation.ThicketSeedSlots; i++) Assert.IsFalse(sim.TryGetThicketSeed(Boss, i, out _), "место " + i);
            Assert.AreEqual(start + Simulation.ThicketSeedCooldownPhase1Ticks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Seeds),
                "перезарядка фазы 1 — 195 от начала");

            Until(sim, start + 9);
            sim.Step(InputFrame.Empty);
            int second = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds && e.Amount == 2)
                {
                    second++;
                    Assert.AreEqual(bushes[1].Center, e.Position);
                }
            Assert.AreEqual(1, second, "куст 1 пророс через 9: Started(Seeds, 2)");
            for (int lane = 0; lane < 4; lane++)
            {
                Assert.IsTrue(sim.TryGetThicketSeed(Boss, 4 + lane, out var s));
                Assert.AreEqual(4 + lane, s.Index);
                Assert.AreEqual(Simulation.ThicketBushLaneDirection(bushes[1].Axis, lane), s.Direction);
                Assert.IsTrue(sim.TryGetTelegraph(sim.FindTelegraph(s.TelegraphSerial), out var mark));
                Assert.AreEqual(start + 9, mark.StartTick);
                Assert.AreEqual(bushes[1].LaunchTick, mark.ImpactTick);
            }

            Until(sim, launch0);
            sim.Step(InputFrame.Empty);
            int launched = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyProjectileLaunched && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds) launched++;
            Assert.AreEqual(4, launched, "куст 0 выпустил 4 шипа");
        }

        [Test]
        public void HeroOffEveryLane_NotHit_EveryThornFliesItsWholePath_LinesFlashAtTheEnd()
        {
            var sim = StartPhaseOne(7, out int start, out _);
            var bushes = Bushes(sim);
            FixVec2 hero = sim.Entities.Position[0];
            FixVec2 safe = hero;
            bool found = false;
            for (int r = 1; r <= 12 && !found; r++)
                for (int k = 0; k < 36 && !found; k++)
                {
                    double ang = k * System.Math.PI / 18;
                    FixVec2 p = hero + At(System.Math.Cos(ang), System.Math.Sin(ang)) * Fix64.FromDouble(r * 0.5);
                    bool clear = !OnAnyLane(bushes, p, 0.3);
                    foreach (var b in bushes) if (FixVec2.Distance(p, b.Center).ToDouble() < 1.5) clear = false;
                    if (clear) { safe = p; found = true; }
                }
            Assert.IsTrue(found, "место между линиями есть");
            Until(sim, start + 10);
            var lanes = new int[8];
            for (int i = 0; i < 8; i++)
            {
                Assert.IsTrue(sim.TryGetThicketSeed(Boss, i, out var s), "шип " + i);
                lanes[i] = s.TelegraphSerial;
            }
            int health = sim.Entities.Health[0];
            var log = FlyOut(sim, s => s.Entities.Position[0] = safe);
            Assert.AreEqual(health, sim.Entities.Health[0], "вне линий — цел");
            Assert.AreEqual(8, CountOf(log, SimEventType.EnemyProjectileLaunched));
            Assert.AreEqual(8, CountOf(log, SimEventType.EnemyActionImpact, 0), "все 8 шипов долетели до конца линий");
            Assert.AreEqual(0, CountOf(log, SimEventType.EnemyActionImpact, 1));
            int flight = Simulation.ThicketSeedFlightTicks(Simulation.ThicketBushLaneLength);
            Assert.AreEqual(21, flight, "9 м за 21 тик");
            foreach (var r in log)
                if (r[0] == (int)SimEventType.EnemyActionImpact)
                    Assert.That(r[1], Is.EqualTo(bushes[0].LaunchTick + flight - 1).Or.EqualTo(bushes[1].LaunchTick + flight - 1),
                        "шип встаёт в конце своей линии");
            for (int i = 4; i < 8; i++)
            {
                Assert.IsTrue(sim.TryGetTelegraph(sim.FindTelegraph(lanes[i]), out var lane), "линия доживает вспышку");
                Assert.AreEqual(TelegraphState.Resolved, lane.State);
            }
            Assert.IsFalse(sim.TryGetThicketSeedVolley(Boss, out _), "каст кончился");
            Assert.AreEqual(2, Bushes(sim).Count, "кусты ещё вянут");
            Until(sim, bushes[1].GoneTick);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, Bushes(sim).Count, "увядшие кусты ушли");
        }

        [Test]
        public void NearBush_HeroStandsOnItsLane_HitOnce_AtTheContactTick_OneHitPerCast_EvenOnTheSecondBushLane()
        {
            var sim = StartPhaseOne(7, out _, out _);
            var bushes = Bushes(sim);
            var b0 = bushes[0];
            var b1 = bushes[1];
            FixVec2 hero = sim.Entities.Position[0];
            Assert.That(FixVec2.Distance(hero, b0.Center).ToDouble(), Is.InRange(3.0, 5.5), "куст 0 — в 3–5,5 м от героя");
            Assert.IsTrue(OnAnyLane(new List<ThicketBushState> { b0 }, hero, 0), "герой стоит на линии куста 0 — надо сойти");
            var serials0 = new List<int>();
            for (int lane = 0; lane < 4; lane++)
                if (sim.TryGetThicketSeed(Boss, lane, out var s)) serials0.Add(s.Serial);
            int health = sim.Entities.Health[0];
            int damage = sim.ThicketSeedDamageOf(Boss);
            Assert.That(damage, Is.InRange(8, 10), "на арене 9 — около второго удара серии лапы (8)");
            FixVec2 onSecond = b1.Center + Simulation.ThicketBushLaneDirection(b1.Axis, 0) * Fix64.FromInt(3);
            var log = FlyOut(sim, s => s.Entities.Position[0] = s.Tick <= b0.ContactTick ? hero : onSecond);
            Assert.AreEqual(health - damage, sim.Entities.Health[0], "одно попадание на каст");
            Assert.AreEqual(1, CountOf(log, SimEventType.EnemyActionImpact, 1));
            Assert.AreEqual(7, CountOf(log, SimEventType.EnemyActionImpact, 0), "шип куста 1 по его линии летит мимо героя");
            foreach (var r in log)
                if (r[0] == (int)SimEventType.EnemyActionImpact && r[3] == 1)
                {
                    Assert.AreEqual(b0.ContactTick, r[1], "контакт — в расчётный тик куста 0");
                    Assert.Contains(r[2], serials0, "попал шип куста 0");
                }
        }

        // ---------- честные места ----------

        private static double Metres(FixVec2 a, FixVec2 b) => FixVec2.Distance(a, b).ToDouble();

        /// <summary>
        /// Места каста, что сейчас стоит у босса boss, при герое hero: 2–3 куста, каждый не ближе 2,5 м к герою и
        /// к кромке корпуса, на полу с запасом 1 м (map), не ближе 4 м друг к другу, кресты + / × по очереди;
        /// куст 0 — в 5,5 м от героя, и герой стоит на его линии, до которой шип дотягивается.
        /// </summary>
        private static void CheckPlacement(Simulation sim, int boss, FixVec2 hero, LayoutMap map, string why)
        {
            var bushes = Bushes(sim, boss);
            Assert.That(bushes.Count, Is.InRange(Simulation.ThicketBushesMin, sim.ThicketBushCountOf(boss)), why);
            for (int i = 0; i < bushes.Count; i++)
            {
                var b = bushes[i];
                Assert.AreEqual(i, b.Order, why);
                Assert.That(Metres(b.Center, hero), Is.GreaterThanOrEqualTo(2.5 - 1e-3), why + ": куст от героя");
                Assert.That(sim.ThicketHullGap(boss, b.Center).ToDouble(), Is.GreaterThanOrEqualTo(2.5 - 1e-3), why + ": куст от корпуса");
                if (map != null) Assert.IsTrue(map.IsWalkable(b.Center, Simulation.ThicketBushFloorMargin), why + ": на полу с запасом 1 м");
                Assert.AreEqual(Simulation.ThicketBushAxis(b.Diagonal), b.Axis, why);
                if (i > 0) Assert.AreNotEqual(bushes[i - 1].Diagonal, b.Diagonal, why + ": кресты по очереди");
                for (int j = 0; j < i; j++)
                    Assert.That(Metres(b.Center, bushes[j].Center), Is.GreaterThanOrEqualTo(4 - 1e-3), why + ": кусты врозь");
            }
            Assert.That(Metres(bushes[0].Center, hero), Is.LessThanOrEqualTo(5.5 + 1e-3), why + ": куст 0 рядом");
            bool onLane = false;
            for (int lane = 0; lane < 4; lane++)
                if (sim.TryGetThicketSeed(boss, lane, out var s) && sim.TryGetTelegraph(sim.FindTelegraph(s.TelegraphSerial), out var mark)
                    && Simulation.TelegraphContains(in mark, hero, sim.Entities.BodyRadius[0])) onLane = true;
            Assert.IsTrue(onLane, why + ": герой на линии куста 0 (линия до него дотягивается)");
        }

        [Test]
        public void Placement_StandArena_TwentyCasts_FairSpots_NearBushOnTheHerosLane()
        {
            var sim = Arena(7);
            Only(sim, ThicketMasterAction.Seeds);
            Until(sim, IntroDone);
            FixVec2 boss = sim.Entities.Position[Boss];
            for (int k = 0; k < 20; k++)
            {
                double ang = k * 47 * System.Math.PI / 180, r = 5 + (k % 4) * 1.8;
                FixVec2 hero = boss + At(System.Math.Cos(ang) * r, System.Math.Sin(ang) * r);
                System.Action<Simulation> hold = s => { s.Entities.Position[0] = hero; s.Entities.Health[0] = s.Entities.MaxHealth[0]; };
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, 0);
                Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketSeeds, 120, hold), "каст " + k);
                CheckPlacement(sim, Boss, hero, null, "каст " + k);
                FlyOut(sim, hold);
                for (int t = 0; t < 120 && Bushes(sim).Count > 0; t++) { hold(sim); sim.Step(InputFrame.Empty); }
            }
        }

        private static RiftRun Jump(ulong seed)
        {
            var location = ArenaEncounterTests.ForestLocation();
            var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartTestAtLevel(9, true);
            return run;
        }

        [Test]
        public void Placement_RealBossClearing_OnTheFloorWithMargin_AwayFromHeroAndHull_NearBushOnTheHerosLane()
        {
            int casts = 0;
            foreach (ulong seed in new ulong[] { 3, 7 })
            {
                var run = Jump(seed);
                var sim = run.Sim;
                var e = sim.Entities;
                int boss = run.BossId;
                var map = run.Map;
                var glade = map.GetGlade(0);
                // Вступление — герой ступил на пол (стенд ставит его прямо на поляну).
                for (int k = 0; k < 200 && !sim.TryGetThicketIntro(boss, out _, out _, out _); k++)
                {
                    var walk = InputFrame.Empty;
                    walk.Flags = (byte)InputFlags.DirectMovement;
                    walk.MoveDirection = (e.Position[boss] - e.Position[0]).Normalized();
                    run.Step(walk);
                }
                Assert.IsTrue(sim.TryGetThicketIntro(boss, out _, out _, out int end), "вступление, семя " + seed);
                Only(sim, boss, ThicketMasterAction.Seeds);
                while (sim.Tick <= end) run.Step(InputFrame.Empty);
                for (int gx = -4; gx <= 4; gx++)
                    for (int gy = -3; gy <= 3; gy++)
                    {
                        FixVec2 hero = glade.Center + At(gx * 2.6, gy * 2.4);
                        if (!map.IsWalkable(hero, e.BodyRadius[0]) || sim.ThicketHullGap(boss, hero).ToDouble() < 4) continue;
                        sim.SetThicketReadyTick(boss, ThicketMasterAction.Seeds, 0);
                        int start = -1;
                        for (int t = 0; t < 90 && start < 0; t++)
                        {
                            e.Position[0] = hero; e.Health[0] = e.MaxHealth[0];
                            run.Step(InputFrame.Empty);
                            foreach (var ev in sim.Events)
                                if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketSeeds
                                    && ev.Amount == 0) start = sim.Tick;
                        }
                        if (start < 0) continue;
                        casts++;
                        CheckPlacement(sim, boss, hero, map, "семя " + seed + ", герой (" + gx + "; " + gy + ")");
                        for (int t = 0; t < 200 && Bushes(sim, boss).Count > 0; t++)
                        {
                            e.Position[0] = hero; e.Health[0] = e.MaxHealth[0];
                            run.Step(InputFrame.Empty);
                        }
                    }
            }
            Assert.That(casts, Is.GreaterThanOrEqualTo(30), "кастов на настоящей поляне");
        }

        [Test]
        public void Placement_NeverOnTheBossesLyingPollen()
        {
            var sim = Arena(9);
            Only(sim);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 60 / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 120), "рёв 66 — фаза 2");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Until(sim, roar.EndTick + 1);
            FixVec2 boss = sim.Entities.Position[Boss];
            int checkedBushes = 0;
            for (int round = 0; round < 6; round++)
            {
                double ang = round * 61 * System.Math.PI / 180;
                FixVec2 hero = boss + At(System.Math.Cos(ang) * 9, System.Math.Sin(ang) * 9);
                System.Action<Simulation> hold = s => { s.Entities.Position[0] = hero; s.Entities.Health[0] = s.Entities.MaxHealth[0]; };
                Only(sim, ThicketMasterAction.Pollen);
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Pollen, 0);
                Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketPollen, 300, hold), "пыльца, круг " + round);
                for (int t = 0; t < Simulation.ThicketPollenFallTicks + 2; t++) { hold(sim); sim.Step(InputFrame.Empty); }
                Only(sim, ThicketMasterAction.Seeds);
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, 0);
                Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketSeeds, 120, hold), "терновник при лежащей пыльце, круг " + round);
                foreach (var b in Bushes(sim))
                    for (int z = 0; z < Simulation.ThicketPollenZones; z++)
                        if (sim.TryGetThicketPollenZone(z, out var zone))
                        {
                            Assert.That(Metres(b.Center, zone.Center), Is.GreaterThanOrEqualTo((zone.Radius + Simulation.ThicketBushRadius).ToDouble() - 1e-3),
                                "куст не на облаке, круг " + round);
                            checkedBushes++;
                        }
                FlyOut(sim, hold);
                for (int t = 0; t < 200 && (Bushes(sim).Count > 0 || sim.TryGetThicketPollenZone(0, out _)
                    || sim.TryGetThicketPollenZone(1, out _) || sim.TryGetThicketPollenZone(2, out _)); t++) { hold(sim); sim.Step(InputFrame.Empty); }
            }
            Assert.That(checkedBushes, Is.GreaterThanOrEqualTo(18), "кусты рядом с лежащими облаками");
        }

        // ---------- выбор: фазы, перезарядки, полосы ----------

        [Test]
        public void PhaseOne_NextCastAfter195_PhasesTwoAndThree_ThreeBushes_Rarer360()
        {
            var sim = StartPhaseOne(7, out int first, out _);
            FlyOut(sim, null);
            int second = RunUntilStarted(sim, EnemyActionKind.ThicketSeeds, 400);
            Assert.AreEqual(first + Simulation.ThicketSeedCooldownPhase1Ticks, second, "фаза 1 — раз в 6,5 с, герой в средней полосе");
            FlyOut(sim, null);

            foreach (int percent in new[] { 60, 30 })
            {
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, int.MaxValue / 2);
                sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * percent / 100;
                Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 200), "рёв порога");
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
                Until(sim, roar.EndTick);
                for (int t = 0; t < 200 && Bushes(sim).Count > 0; t++) sim.Step(InputFrame.Empty);
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, 0);
                int start = RunUntilStarted(sim, EnemyActionKind.ThicketSeeds, 200);
                Assert.AreNotEqual(-1, start, "терновник в фазе " + (percent == 60 ? 2 : 3));
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var a));
                Assert.AreEqual(3, a.Tag, "фазы 2–3 — три куста");
                Assert.AreEqual(3, Bushes(sim).Count);
                int cooldown = percent > 50 ? Simulation.ThicketSeedCooldownTicks
                    : Simulation.ThicketSeedCooldownTicks * Simulation.ThicketEnragedCooldownPercent / 100;
                Assert.AreEqual(start + cooldown, sim.ThicketReadyTick(Boss, ThicketMasterAction.Seeds), "реже: перезарядка 12 с");
                var log = FlyOut(sim, null);
                Assert.AreEqual(12, CountOf(log, SimEventType.EnemyProjectileLaunched), "три куста — 12 шипов (без карты все линии целы)");
            }
        }

        [Test]
        public void NearBand_Rare_PawStaysTheBase()
        {
            var sim = Arena(3.3);
            Only(sim, ThicketMasterAction.Seeds);
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            sim.SetThicketRearStompReadyTick(Boss, int.MaxValue / 2);
            FixVec2 hero = sim.Entities.Position[0];
            int seeds = 0, paws = 0;
            for (int k = 0; k < IntroDone + 1500; k++)
            {
                if (sim.Tick >= IntroDone)
                {
                    sim.Entities.Position[0] = hero;
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                    Assert.AreEqual(ThicketBand.Near, sim.ThicketHeroBand(Boss), "тик " + sim.Tick);
                }
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.Amount == 0)
                    {
                        if (e.ActionVariant == (int)EnemyActionKind.ThicketSeeds) seeds++;
                        if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) paws++;
                    }
            }
            TestContext.WriteLine("near band: paws " + paws + ", bushes " + seeds);
            Assert.That(paws, Is.GreaterThan(10), "лапа — основа");
            Assert.That(seeds * 4, Is.LessThanOrEqualTo(paws), "терновник в ближней полосе — редко (вес 2 к лапе 10)");

            // Фазы 2–3: в ближней полосе терновника нет (кусты вытесняли нырок «под героя» фазы 3).
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 60 / 100;
            int late = 0;
            for (int k = 0; k < 1200; k++)
            {
                sim.Entities.Position[0] = hero;
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, 0);
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.Amount == 0 && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds
                        && sim.ThicketHullGap(Boss, hero) <= Simulation.ThicketNearGap) late++;
            }
            Assert.AreEqual(2, sim.ThicketMasterPhase(Boss));
            Assert.AreEqual(0, late, "фаза 2, ближняя полоса — терновника нет, даже когда он готов");
        }

        // ---------- бюджет и наслоение ----------

        [Test]
        public void MarkBudgetTwo_AndLayering_FromTheCastUntilTheLastThornStops_NoOtherCastMeanwhile()
        {
            var sim = StartPhaseOne(7, out int start, out _);
            Assert.AreEqual(2, sim.BigMarkLoad(out int lastStart), "каст — вес 2 (как прорастание и ливень)");
            Assert.AreEqual(start, lastStart);
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss));
            Assert.IsTrue(sim.ThicketHazardActive(Boss), "кусты — фоновая опасность");
            // Корни-плеть готовы, герой издали — но под кустами босс ничего, кроме лапы, не начинает.
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            FixVec2 hero = sim.Entities.Position[0];
            int sprout = -1, end = -1;
            while (sim.TryGetThicketSeedVolley(Boss, out _))
            {
                Assert.AreEqual(2, sim.BigMarkLoad(out lastStart), "тик " + sim.Tick);
                if (sim.Tick > start + 9) Assert.AreEqual(start + 9, lastStart, "начало — последнее прорастание");
                Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss));
                Assert.IsTrue(sim.ThicketHazardActive(Boss));
                sim.Entities.Position[0] = hero;
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                // Каст кончается в тик, когда встал последний шип, — в этот же тик босс уже свободен.
                bool alive = sim.TryGetThicketSeedVolley(Boss, out _);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.Amount == 0 && e.ActionVariant == (int)EnemyActionKind.ThicketSprout)
                    {
                        Assert.IsFalse(alive, "под кустами — без каста, тик " + tick);
                        sprout = tick;
                    }
                end = tick;
            }
            if (sprout < 0) sprout = RunUntilStarted(sim, EnemyActionKind.ThicketSprout, 90, s => s.Entities.Position[0] = hero);
            Assert.That(sprout, Is.InRange(end, end + 60), "кусты отпустили — корни-плеть сразу (после отдыха)");
            Assert.AreEqual(2, Bushes(sim).Count, "кусты ещё вянут, но выбор не держат");

            // Без готового каста: все шипы встали — меток и жетона нет.
            var plain = StartPhaseOne(7, out _, out _);
            while (plain.TryGetThicketSeedVolley(Boss, out _)) plain.Step(InputFrame.Empty);
            Assert.AreEqual(0, plain.BigMarkLoad(out _), "все шипы встали — меток нет");
            Assert.IsFalse(plain.ThicketMasterHoldsBigToken(Boss));
            Assert.IsFalse(plain.ThicketHazardActive(Boss), "увядающие кусты не держат");
        }

        // ---------- Часы, смерть ----------

        [Test]
        public void Hourglass_BushesWaitSixtyTicks_ThornsHangInTheAir_HitOnTheShiftedTick()
        {
            var sim = StartPhaseOne(7, out int start, out _);
            sim.SetArtifact(RunArtifact.Hourglass);
            var before = Bushes(sim);
            FixVec2 hero = sim.Entities.Position[0];
            Until(sim, start + 5);
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = hero });
            Assert.IsTrue(sim.TimeStopped);
            var after = Bushes(sim);
            const int shift = Simulation.ThicketHourglassShiftTicks;
            for (int k = 0; k < 2; k++)
            {
                Assert.AreEqual(before[k].LaunchTick + shift, after[k].LaunchTick, "куст " + k + ": выпуск ждёт");
                Assert.AreEqual(before[k].ContactTick + shift, after[k].ContactTick);
                Assert.AreEqual(before[k].WitherTick + shift, after[k].WitherTick);
                Assert.AreEqual(before[k].GoneTick + shift, after[k].GoneTick);
            }
            Assert.AreEqual(before[0].SproutTick, after[0].SproutTick, "уже проросший — тик прорастания прежний");
            Assert.AreEqual(before[1].SproutTick + shift, after[1].SproutTick, "куст 1 прорастёт позже");
            Assert.IsTrue(sim.TryGetThicketSeed(Boss, 0, out var planned));
            Assert.IsTrue(sim.TryGetTelegraph(sim.FindTelegraph(planned.TelegraphSerial), out var lane));
            Assert.AreEqual(before[0].LaunchTick + shift, lane.ImpactTick, "заполнение линии тянется");
            int health = sim.Entities.Health[0];
            var log = FlyOut(sim, s => s.Entities.Position[0] = hero, 300);
            foreach (var r in log)
            {
                if (r[0] == (int)SimEventType.EnemyProjectileLaunched)
                    Assert.That(r[1], Is.EqualTo(after[0].LaunchTick).Or.EqualTo(after[1].LaunchTick), "выпуск — в сдвинутый тик");
                if (r[0] == (int)SimEventType.EnemyActionImpact && r[3] == 1)
                    Assert.AreEqual(after[0].ContactTick, r[1], "попал на 60 тиков позже");
            }
            Assert.AreEqual(1, CountOf(log, SimEventType.EnemyActionImpact, 1));
            Assert.Less(sim.Entities.Health[0], health);

            // Часы в полёте: шип висит, линия полная и живёт на 60 дольше.
            var flying = StartPhaseOne(7, out start, out _);
            flying.SetArtifact(RunArtifact.Hourglass);
            var b0 = Bushes(flying)[0];
            FixVec2 safe = flying.Entities.Position[0] + At(0, 30);
            Until(flying, b0.LaunchTick + 3);
            Assert.IsTrue(flying.TryGetThicketSeed(Boss, 0, out var inAir));
            Assert.IsTrue(inAir.Released);
            Assert.IsTrue(flying.TryGetTelegraph(flying.FindTelegraph(inAir.TelegraphSerial), out var laneBefore));
            flying.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = safe });
            Assert.IsTrue(flying.TimeStopped);
            for (int k = 0; k < 30; k++) { flying.Entities.Position[0] = safe; flying.Step(InputFrame.Empty); }
            Assert.IsTrue(flying.TryGetThicketSeed(Boss, 0, out var frozen));
            Assert.AreEqual(inAir.Travelled, frozen.Travelled, "шип стоит в воздухе");
            Assert.IsTrue(flying.TryGetTelegraph(flying.FindTelegraph(inAir.TelegraphSerial), out var laneFrozen));
            Assert.AreEqual(TelegraphState.Active, laneFrozen.State);
            Assert.AreEqual(b0.LaunchTick, laneFrozen.ImpactTick, "прошедший удар не сдвигается — линия полная");
            Assert.AreEqual(laneBefore.EndTick + shift, laneFrozen.EndTick);
        }

        [Test]
        public void BossDies_WhileBushesGrow_NoThornsFly_BushesGone_InFlight_ThornsCancelled_LinesFade()
        {
            var sim = StartPhaseOne(7, out int start, out _);
            int health = sim.Entities.Health[0];
            Until(sim, start + 5);
            sim.Entities.Health[Boss] = 0;
            sim.Entities.Alive[Boss] = false;
            var log = FlyOut(sim, null, 80);
            Assert.AreEqual(0, CountOf(log, SimEventType.EnemyProjectileLaunched), "снятые кусты не стреляют");
            Assert.AreEqual(0, Bushes(sim).Count, "кусты сняты сразу");
            Assert.IsFalse(sim.TryGetThicketSeedVolley(Boss, out _));
            Assert.AreEqual(health, sim.Entities.Health[0]);

            var flying = StartPhaseOne(7, out start, out _);
            var b0 = Bushes(flying)[0];
            var lanes = new List<int>();
            FixVec2 safe = flying.Entities.Position[0] + At(0, 30);
            Until(flying, start + 10);
            for (int i = 0; i < Simulation.ThicketSeedSlots; i++)
                if (flying.TryGetThicketSeed(Boss, i, out var s)) lanes.Add(s.TelegraphSerial);
            while (flying.Tick < b0.LaunchTick + 2) { flying.Entities.Position[0] = safe; flying.Step(InputFrame.Empty); }
            health = flying.Entities.Health[0];
            flying.Entities.Health[Boss] = 0;
            flying.Entities.Alive[Boss] = false;
            log = FlyOut(flying, s => s.Entities.Position[0] = safe, 60);
            Assert.AreEqual(4, CountOf(log, SimEventType.EnemyActionCancelled), "четыре летящих шипа куста 0 сняты");
            Assert.AreEqual(0, CountOf(log, SimEventType.EnemyProjectileLaunched), "куст 1 не выстрелил");
            Assert.AreEqual(0, CountOf(log, SimEventType.EnemyActionImpact));
            Assert.AreEqual(health, flying.Entities.Health[0], "босс мёртв — шипы не бьют");
            foreach (int serial in lanes)
                if (flying.TryGetTelegraph(flying.FindTelegraph(serial), out var lane))
                    Assert.AreEqual(TelegraphState.Cancelled, lane.State);
        }

        // ---------- детерминизм ----------

        [Test]
        public void Deterministic_TwoRuns_SameHashesEveryTick_WithAMovingHeroAndTheWholeBrain()
        {
            ulong[] first = null;
            int casts = 0;
            for (int run = 0; run < 2; run++)
            {
                var sim = new Simulation(77, 64);
                sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(7));
                sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
                sim.Entities.RefreshStats(0);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                var hashes = new ulong[1500];
                int seen = 0;
                for (int k = 0; k < hashes.Length; k++)
                {
                    // Герой ходит поперёк в 7–9 м от босса.
                    FixVec2 boss = sim.Entities.Position[Boss];
                    FixVec2 goal = boss + At(-8, (k / 40) % 2 == 0 ? 4 : -4);
                    var input = InputFrame.Empty;
                    input.Flags = (byte)InputFlags.MoveOrder;
                    input.Aim = goal;
                    sim.Step(input);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketSeeds && e.Amount == 0)
                            seen++;
                    hashes[k] = sim.StateHash();
                }
                if (run == 0) { first = hashes; casts = seen; continue; }
                Assert.AreEqual(casts, seen);
                for (int k = 0; k < hashes.Length; k++) Assert.AreEqual(first[k], hashes[k], "тик " + k);
            }
            Assert.That(casts, Is.GreaterThanOrEqualTo(2), "терновник в прогоне был");
        }
    }
}
