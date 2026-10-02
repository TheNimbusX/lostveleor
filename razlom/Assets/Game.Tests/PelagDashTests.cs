using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// РЫВОК ВМЕСТО КУВЫРКА — решение владельца 02.10 (AGENTS/DESIGN.md,
    /// «Пелаг — новая структура набора», пункт «Дэш»): около 4 м за 0,17–0,2 с,
    /// неуязвимость на весь рывок («да, неуязвимость и 1.5 секунды ок»),
    /// перезарядка 1,5 с, один заряд, сквозь тела, стены останавливают. Корни
    /// рывок держат — это проверяет RootSnarerTests.
    /// </summary>
    public class PelagDashTests
    {
        private const ulong Seed = 0xDA54UL;
        private const int Dash = PelagKit.DashSlot;
        private const int Hero = Simulation.PlayerId;

        /// <summary>Пустое поле, набор забега (Вихрь и рывок), герой в начале координат.</summary>
        private static Simulation Field()
        {
            var sim = new Simulation(Seed, 32);
            sim.SetupTestArena(0);
            new RunLoadout().ApplyTo(sim);
            Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[Hero]);
            return sim;
        }

        /// <summary>Манекен: не ходит, не бьёт, здоровья много.</summary>
        private static int Dummy(Simulation sim, FixVec2 at)
        {
            int id = sim.Entities.Spawn(at, 5000, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));

        /// <summary>Точка с точностью до округления Fix64 на подшагах движения.</summary>
        private static void Near(FixVec2 expected, FixVec2 actual, string message)
        {
            Assert.That(actual.X.ToDouble(), Is.EqualTo(expected.X.ToDouble()).Within(1e-4), message);
            Assert.That(actual.Y.ToDouble(), Is.EqualTo(expected.Y.ToDouble()).Within(1e-4), message);
        }

        private static InputFrame Press(int slot, FixVec2 aim)
        {
            var press = InputFrame.Empty;
            press.AbilityMask = (byte)(1 << slot);
            press.Aim = aim;
            return press;
        }

        private static int Count(Simulation sim, SimEventType type, int target = int.MinValue)
        {
            int n = 0;
            foreach (var e in sim.Events)
                if (e.Type == type && (target == int.MinValue || e.Target == target)) n++;
            return n;
        }

        private static SimEvent Single(Simulation sim, SimEventType type)
        {
            Assert.AreEqual(1, Count(sim, type), type.ToString());
            foreach (var e in sim.Events) if (e.Type == type) return e;
            return default;
        }

        /// <summary>Комната 8×8 м из одного модуля: стены по x = 0 и x = 8.</summary>
        private static Simulation Room()
        {
            var room = new ModuleDefinition("module.test_room", 4, 4,
                new ModuleConnector[0], weight: 0, isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }), 2);
            Assert.That(map.TryPlace(0, 0, 0, 0), Is.EqualTo(0));
            var sim = new Simulation(Seed);
            sim.SetupRift(map, Seed, minEnemiesPerRoom: 0, maxEnemiesPerRoom: 0, enemyHealth: 100);
            new RunLoadout().ApplyTo(sim);
            return sim;
        }

        [Test]
        public void DashNumbers_FourMetresInSixTicks_OneChargeEveryOneAndAHalfSeconds()
        {
            var dash = AbilityDefinition.Dash();
            Assert.AreEqual(Fix64.FromInt(4), dash.GetBase(AbilityStatType.Radius));
            Assert.AreEqual(6, dash.GetBase(AbilityStatType.DurationTicks).ToInt(), "0,2 с");
            Assert.AreEqual(Simulation.TicksPerSecond * 3 / 2, dash.GetBase(AbilityStatType.CooldownTicks).ToInt(), "1,5 с");
            Assert.AreEqual(Fix64.Zero, dash.GetBase(AbilityStatType.Damage), "удара в базе нет");

            var sim = Field();
            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            Assert.AreEqual(1, Count(sim, SimEventType.AbilityCast));
            Assert.AreEqual(cast + 45, sim.AbilityReadyTick(Dash));

            // Один заряд: до конца перезарядки нажатие не проходит вовсе.
            while (sim.Tick < cast + 45)
            {
                sim.Step(Press(Dash, At(-10, 0)));
                Assert.AreEqual(0, Count(sim, SimEventType.DashStarted), "второго заряда нет, тик " + (sim.Tick - 1));
            }
            sim.Step(Press(Dash, At(-10, 0)));
            Assert.AreEqual(1, Count(sim, SimEventType.DashStarted), "через 1,5 с — снова");
            Assert.AreEqual(2, sim.PelagDash.Serial);
        }

        [Test]
        public void DashCoversFourMetresInSixEvenTicks_AndReportsStartAndEnd()
        {
            var sim = Field();
            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;

            SimEvent started = Single(sim, SimEventType.DashStarted);
            Assert.AreEqual(Hero, started.Source);
            Assert.AreEqual(6, started.Amount, "длительность в тиках");
            Assert.AreEqual(FixVec2.Zero, started.Position, "откуда");
            Assert.AreEqual(1, started.ActionVariant, "номер рывка");
            PelagDashState dash = sim.PelagDash;
            Assert.AreEqual(cast, dash.StartTick);
            Assert.AreEqual(cast + 6, dash.InvulnerableUntilTick);
            Assert.AreEqual(cast + 6, sim.DashInvulnerableUntilTick);
            Assert.AreEqual(At(4, 0), dash.To, "куда — на полную дальность, не до курсора");
            Assert.AreEqual(At(1, 0), dash.Direction);
            Assert.IsTrue(dash.Moving);
            Assert.AreEqual((byte)ForcedMotionKind.Roll, sim.Entities.ForcedKind[Hero]);

            double previous = 0;
            for (int k = 1; k <= 6; k++)
            {
                sim.Step(InputFrame.Empty);
                FixVec2 at = sim.Entities.Position[Hero];
                Assert.That(at.X.ToDouble() - previous, Is.EqualTo(4.0 / 6).Within(1e-3), "ровный шаг, тик " + k);
                Assert.AreEqual(Fix64.Zero, at.Y);
                previous = at.X.ToDouble();
                Assert.AreEqual(k == 6 ? 1 : 0, Count(sim, SimEventType.DashEnded), "конец рывка, тик " + k);
            }
            Near(At(4, 0), sim.Entities.Position[Hero], "доехал за 6 тиков");

            SimEvent ended = Single(sim, SimEventType.DashEnded);
            Assert.AreEqual(400, ended.Amount, "путь в сантиметрах — длина следа");
            Assert.IsFalse(ended.Flag, "доехал на полную дальность");
            Assert.AreEqual(sim.Entities.Position[Hero], ended.Position);
            Assert.AreEqual(1, ended.ActionVariant);
            Assert.AreEqual(cast + 6, sim.PelagDash.StopTick);
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[Hero]);
            Assert.IsFalse(sim.DashInvulnerable, "следующий тик после рывка — уже без неуязвимости");
        }

        /// <summary>
        /// Урон по герою — удар, способность моба и тик по времени (лужа,
        /// горение) — в рывке не проходит и события Damage не даёт; на
        /// следующем тике после рывка проходит как обычно.
        /// </summary>
        [Test]
        public void IncomingDamageIsIgnoredForTheWholeDash_AndLandsRightAfter()
        {
            var sim = Field();
            int enemy = Dummy(sim, At(0, 8));
            int health = sim.Entities.Health[Hero];
            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            Assert.IsTrue(sim.PelagDash.InvulnerableAt(cast), "с тика нажатия");

            for (int tick = cast + 1; tick <= cast + 6; tick++)
            {
                Assert.AreEqual(tick, sim.Tick);
                Assert.IsTrue(sim.DashInvulnerable, "тик " + tick);
                sim.ApplyAbilityDamage(enemy, Hero, 50, -1, DamageType.Physical);
                sim.ApplyAbilityDamage(enemy, Hero, 50, -1, DamageType.Fire, overTime: true);
                Assert.AreEqual(health, sim.Entities.Health[Hero], "урон в рывке, тик " + tick);
                Assert.AreEqual(0, Count(sim, SimEventType.Damage, Hero) + Count(sim, SimEventType.DamageOverTime, Hero));
                sim.Step(InputFrame.Empty);
            }

            Assert.IsFalse(sim.DashInvulnerable);
            sim.ApplyAbilityDamage(enemy, Hero, 50, -1, DamageType.Physical);
            Assert.Less(sim.Entities.Health[Hero], health, "сразу после рывка урон проходит");
            Assert.AreEqual(1, Count(sim, SimEventType.Damage, Hero));
        }

        /// <summary>Горение на герое в рывке не тикает, после рывка тикает снова.</summary>
        [Test]
        public void BurningDoesNotTickOnTheHeroDuringTheDash()
        {
            var sim = Field();
            int enemy = Dummy(sim, At(0, 8));
            sim.Statuses.ApplyBurn(Hero, Fix64.FromInt(10), 300, enemy, -1);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.DamageOverTime, Hero), "горение тикает");

            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            Assert.AreEqual(0, Count(sim, SimEventType.DamageOverTime, Hero), "тик нажатия");
            int health = sim.Entities.Health[Hero];
            while (sim.Tick <= cast + 6)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(0, Count(sim, SimEventType.DamageOverTime, Hero), "тик " + (sim.Tick - 1));
            }
            Assert.AreEqual(health, sim.Entities.Health[Hero]);

            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.DamageOverTime, Hero), "после рывка горение снова тикает");
        }

        /// <summary>
        /// Корни, оглушение и замедление в рывке отбиваются целиком: ни
        /// состояния, ни события HeroControl, ни иммунитета после. Рывок
        /// доезжает, а следующий контроль ложится как обычно.
        /// </summary>
        [Test]
        public void ControlBouncesOffTheDash_AndAppliesRightAfter()
        {
            var sim = Field();
            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            while (sim.Tick <= cast + 6)
            {
                Assert.IsFalse(sim.ApplyHeroRoot(30), "корни в рывке, тик " + sim.Tick);
                Assert.IsFalse(sim.ApplyHeroStun(30), "оглушение в рывке, тик " + sim.Tick);
                sim.ApplyHeroSlow(50, 30);
                Assert.IsFalse(sim.HeroRooted);
                Assert.IsFalse(sim.HeroStunned);
                Assert.AreEqual(0, sim.HeroSlowPercent, "замедление в рывке");
                Assert.AreEqual(0, sim.HeroControlImmuneTicksLeft, "отбитое рывком не даёт иммунитета");
                Assert.AreEqual(0, Count(sim, SimEventType.HeroControl));
                sim.Step(InputFrame.Empty);
            }
            Near(At(4, 0), sim.Entities.Position[Hero], "рывок доехал, как будто контроля не было");

            sim.ApplyHeroSlow(50, 30);
            Assert.AreEqual(50, sim.HeroSlowPercent, "после рывка замедление ложится");
            Assert.IsTrue(sim.ApplyHeroRoot(30), "после рывка корни ложатся");
            Assert.IsTrue(sim.AbilityHeldByRoots(Dash), "и держат следующий рывок");
        }

        /// <summary>
        /// Рывок проходит сквозь тела: они не держат и не толкают героя, и он
        /// их не расталкивает. После рывка тела снова расходятся как обычно.
        /// </summary>
        [Test]
        public void DashPassesThroughEnemyBodies_AndCollisionsComeBackAfter()
        {
            var sim = Field();
            var onPath = new[]
            {
                Dummy(sim, new FixVec2(Fix64.Ratio(13, 10), Fix64.Ratio(1, 10))),
                Dummy(sim, new FixVec2(Fix64.Ratio(26, 10), -Fix64.Ratio(1, 10))),
            };
            int landing = Dummy(sim, new FixVec2(Fix64.FromInt(4), Fix64.Ratio(1, 10)));
            var before = new FixVec2[sim.Entities.Count];
            for (int i = 0; i < before.Length; i++) before[i] = sim.Entities.Position[i];

            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            for (int k = 1; k <= 6; k++)
            {
                sim.Step(InputFrame.Empty);
                FixVec2 at = sim.Entities.Position[Hero];
                Assert.AreEqual(Fix64.Zero, at.Y, "тела не сдвинули героя вбок, тик " + k);
                Assert.That(at.X.ToDouble(), Is.EqualTo(k * 4.0 / 6).Within(1e-3), "тела не задержали героя, тик " + k);
                foreach (int id in onPath)
                    Assert.AreEqual(before[id], sim.Entities.Position[id], "герой не растолкал тело, тик " + k);
                Assert.AreEqual(before[landing], sim.Entities.Position[landing], "тело в точке прибытия, тик " + k);
            }
            Near(At(4, 0), sim.Entities.Position[Hero], "доехал на полные 4 м сквозь тела");
            Assert.IsFalse(Single(sim, SimEventType.DashEnded).Flag, "тела рывок не обрывают");
            Assert.AreEqual(cast + 7, sim.Tick);

            // Окно кончилось — тело в точке прибытия и герой расходятся.
            Fix64 contact = sim.Entities.BodyRadius[Hero] + sim.Entities.BodyRadius[landing];
            Assert.That(FixVec2.Distance(sim.Entities.Position[Hero], sim.Entities.Position[landing]), Is.LessThan(contact));
            for (int k = 0; k < 30; k++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(sim.Entities.Position[Hero], sim.Entities.Position[landing]).ToDouble(),
                Is.GreaterThanOrEqualTo(contact.ToDouble() - 0.01), "после рывка тела снова расталкиваются");
        }

        /// <summary>Стена останавливает рывок: тело встаёт у неё, рывок кончается, неуязвимость держится.</summary>
        [Test]
        public void WallStopsTheDash_ButNotItsInvulnerability()
        {
            var sim = Room();
            Fix64 radius = sim.Entities.BodyRadius[Hero];
            Fix64 maxX = LayoutMap.CellSize * Fix64.FromInt(4) - radius;
            FixVec2 start = new FixVec2(maxX - Fix64.Ratio(3, 2), Fix64.FromInt(4));
            sim.Entities.Position[Hero] = start;
            sim.Step(Press(Dash, start + At(10, 0)));
            int cast = sim.Tick - 1;
            Assert.AreEqual(start + At(4, 0), sim.PelagDash.To, "намерение — на полные 4 м");

            int stopped = -1;
            while (sim.Tick <= cast + 6 && stopped < 0)
            {
                sim.Step(InputFrame.Empty);
                if (Count(sim, SimEventType.DashEnded) > 0) stopped = sim.Tick - 1;
            }
            Assert.That(stopped, Is.InRange(cast + 1, cast + 5), "встал раньше полной длительности");
            SimEvent ended = Single(sim, SimEventType.DashEnded);
            Assert.IsTrue(ended.Flag, "упёрся в стену");
            FixVec2 at = sim.Entities.Position[Hero];
            Assert.AreEqual(at, ended.Position);
            Assert.That(at.X, Is.LessThanOrEqualTo(maxX), "стену не прошёл");
            Assert.Greater(at.X.ToDouble(), maxX.ToDouble() - 0.3, "встал у самой стены");
            Assert.AreEqual(start.Y, at.Y);
            Assert.That(ended.Amount, Is.InRange(120, 150), "путь до стены в сантиметрах");
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[Hero], "рывок кончился — герой снова свой");
            Assert.AreEqual(stopped, sim.PelagDash.StopTick);
            Assert.IsTrue(sim.PelagDash.CutShort);
            Assert.IsTrue(sim.DashInvulnerable, "неуязвимость у стены держится своим окном");
            Assert.AreEqual(cast + 6, sim.DashInvulnerableUntilTick);
        }

        /// <summary>По диагонали в стену рывок встаёт на своей линии, а не едет вдоль стены.</summary>
        [Test]
        public void DiagonalDashIntoAWallStopsOnItsLine_WithoutSliding()
        {
            var sim = Room();
            Fix64 radius = sim.Entities.BodyRadius[Hero];
            Fix64 maxX = LayoutMap.CellSize * Fix64.FromInt(4) - radius;
            FixVec2 start = new FixVec2(maxX - Fix64.One, Fix64.FromInt(2));
            sim.Entities.Position[Hero] = start;
            sim.Step(Press(Dash, start + At(5, 5)));
            for (int k = 0; k < 6; k++) sim.Step(InputFrame.Empty);

            FixVec2 moved = sim.Entities.Position[Hero] - start;
            Assert.That(sim.Entities.Position[Hero].X, Is.LessThanOrEqualTo(maxX));
            Assert.That(moved.Y.ToDouble(), Is.EqualTo(moved.X.ToDouble()).Within(1e-3), "остался на линии 45°");
            Assert.Less(moved.Y.ToDouble(), 1.2, "вдоль стены не уехал");
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[Hero]);
        }

        /// <summary>
        /// Настоящий удар моба приходит в окно рывка, а рывок упёрся в стену
        /// и героя из удара не вынес: урона, отброса и оглушения нет — это
        /// неуязвимость, а не уход. Без рывка тот же удар на том же тике лёг.
        /// </summary>
        [TestCase(EnemyKind.ForestGuardian)]
        [TestCase(EnemyKind.ForestThorncaster)]
        [TestCase(EnemyKind.ForestStonehoof)]
        public void RealHitInTheDashWindow_DoesNothing_EvenAgainstAWall(EnemyKind kind)
        {
            var control = WallFight(kind, out int enemy);
            int hit = -1;
            while (hit < 0 && control.Tick < 300)
            {
                int tick = control.Tick;
                control.Step(InputFrame.Empty);
                foreach (var e in control.Events)
                    if (e.Type == SimEventType.Damage && e.Source == enemy && e.Target == Hero) hit = tick;
            }
            Assert.Greater(hit, 3, "без рывка моб достаёт стоящего у стены героя");
            bool knocked = control.Entities.ForcedKind[Hero] == (byte)ForcedMotionKind.Knockback;
            TestContext.WriteLine(kind + ": удар на тике " + hit + ", отброс " + knocked);
            if (kind == EnemyKind.ForestStonehoof) Assert.IsTrue(knocked, "клыки без рывка отбрасывают");

            var sim = WallFight(kind, out enemy);
            while (sim.Tick < hit - 3) sim.Step(InputFrame.Empty);
            int health = sim.Entities.Health[Hero];
            FixVec2 stand = sim.Entities.Position[Hero];
            sim.Step(Press(Dash, stand + At(10, 0)));
            sim.Step(InputFrame.Empty);
            SimEvent ended = Single(sim, SimEventType.DashEnded);
            Assert.IsTrue(ended.Flag);
            Assert.AreEqual(0, ended.Amount, "стена не пустила ни на сантиметр");
            while (sim.Tick <= hit)
            {
                Assert.IsTrue(sim.DashInvulnerable, "тик " + sim.Tick);
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    Assert.IsFalse(e.Type == SimEventType.Damage && e.Target == Hero, "удар в рывке, тик " + (sim.Tick - 1));
            }
            Assert.AreEqual(health, sim.Entities.Health[Hero]);
            Assert.AreEqual(stand, sim.Entities.Position[Hero], "герой так и стоял в ударе");
            Assert.AreNotEqual((byte)ForcedMotionKind.Knockback, sim.Entities.ForcedKind[Hero], "отброса нет");
            Assert.IsFalse(sim.HeroStunned);
        }

        /// <summary>
        /// Комната, герой вплотную к правой стене; моб под ним, лицом к нему и
        /// на месте — отброс прочь от моба идёт вдоль стены, а рывок вправо
        /// упирается в неё сразу. Камнекопыт отдыхает (таран через 10000
        /// тиков) и бьёт клыками.
        /// </summary>
        private static Simulation WallFight(EnemyKind kind, out int enemy)
        {
            var sim = Room();
            Fix64 maxX = LayoutMap.CellSize * Fix64.FromInt(4) - sim.Entities.BodyRadius[Hero];
            sim.Entities.Position[Hero] = new FixVec2(maxX, Fix64.FromInt(4));
            sim.Entities.Facing[Hero] = new FixVec2(Fix64.Zero, -Fix64.One);
            sim.Entities.Stats[Hero].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.RefreshStats(Hero);
            sim.Entities.Health[Hero] = 10000;
            enemy = sim.SpawnEnemy(new FixVec2(maxX, Fix64.FromInt(2)), 1000, kind);
            Fix64 gap = sim.Entities.BodyRadius[Hero] + sim.Entities.BodyRadius[enemy] + Fix64.Ratio(1, 10);
            sim.Entities.Position[enemy] = new FixVec2(maxX, Fix64.FromInt(4) - gap);
            sim.Entities.Stats[enemy].SetBase(StatType.CritChance, Fix64.Zero);
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(enemy);
            sim.Entities.Facing[enemy] = new FixVec2(Fix64.Zero, Fix64.One);
            sim.Entities.Aggro[enemy] = true;
            if (kind == EnemyKind.ForestStonehoof) sim.Entities.NextAttackTick[enemy] = 10000;
            return sim;
        }

        /// <summary>
        /// Другой уход посреди рывка срывает его: тело встаёт, DashEnded с
        /// пометкой «раньше», неуязвимость кончается — рывка больше нет.
        /// </summary>
        [Test]
        public void AnotherEvadeCutsTheDashAndItsInvulnerability()
        {
            var sim = Field();
            sim.SetAbility(1, PelagKit.PoolDefinition(PelagKit.PoolIndexOf(AbilityDefinition.SkewerId)),
                new AbilityNode[0], 0);
            sim.Entities.Lavidium[Hero] = Fix64.FromInt(sim.Entities.MaxLavidium[Hero]);
            sim.Step(Press(Dash, At(10, 0)));
            int cast = sim.Tick - 1;
            sim.Step(InputFrame.Empty);
            sim.Step(Press(1, At(0, 10)));

            SimEvent ended = Single(sim, SimEventType.DashEnded);
            Assert.IsTrue(ended.Flag, "рывок сорван");
            Assert.That(ended.Amount, Is.EqualTo(67).Within(1), "один шаг рывка");
            Assert.AreEqual(cast + 1, sim.DashInvulnerableUntilTick, "неуязвимость кончилась с рывком");
            Assert.IsFalse(sim.PelagDash.InvulnerableAt(cast + 2));
            Assert.AreEqual((byte)ForcedMotionKind.Skewer, sim.Entities.ForcedKind[Hero]);
        }

        /// <summary>Два одинаковых прогона с рывками сквозь тела, уроном и стеной дают один хеш на каждом тике.</summary>
        [Test]
        public void DashIsDeterministic()
        {
            Simulation a = Scenario(), b = Scenario();
            for (int t = 0; t < 120; t++)
            {
                InputFrame input = t % 50 == 0 ? Press(Dash, At(t % 100 == 0 ? 10 : -10, 3)) : InputFrame.Empty;
                a.Step(input); b.Step(input);
                if (t % 3 == 0) { a.ApplyHeroStun(20); b.ApplyHeroStun(20); }
                Assert.AreEqual(a.StateHash(), b.StateHash(), "тик " + t);
            }
            Assert.GreaterOrEqual(a.PelagDash.Serial, 2, "рывки в прогоне были");
        }

        private static Simulation Scenario()
        {
            var sim = Field();
            Dummy(sim, At(2, 0));
            Dummy(sim, At(-2, 1));
            int burner = Dummy(sim, At(0, 9));
            sim.Statuses.ApplyBurn(Hero, Fix64.FromInt(1), 200, burner, -1);
            return sim;
        }
    }
}
