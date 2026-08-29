using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Конвейер способности на примере «Печати пламени».
    ///
    /// Здесь выясняется реальная цена дерева — ДО того, как её умножат
    /// на двадцать способностей. Проверяется, что каждый из трёх типов узлов
    /// включается и выключается, и что порядок применения узлов задан их Id,
    /// а не порядком взятия игроком.
    /// </summary>
    public class AbilityTests
    {
        private const int Slot = 0;
        private const ulong Seed = 0xF1A3EUL;

        private static InputFrame Cast(Fix64 x, Fix64 y)
            => new InputFrame
            {
                Aim = new FixVec2(x, y),
                AbilityMask = 1 << Slot,
                Flags = 0
            };

        private static InputFrame Idle => InputFrame.Empty;

        /// <summary>Арена без врагов, куда цели ставятся руками: бой не должен мешать проверке.</summary>
        private static Simulation Arena(params AbilityNode[] nodes)
        {
            var sim = new Simulation(Seed);
            sim.SetupTestArena(0);
            sim.SetAbility(Slot, AbilityDefinition.FlameSeal(), nodes, nodes.Length);
            return sim;
        }

        private static int PlaceEnemy(Simulation sim, int x, int y, int health = 1000)
            => sim.Entities.Spawn(new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y)), health, Faction.Orvill);

        private static void Run(Simulation sim, in InputFrame frame, int ticks)
        {
            for (int t = 0; t < ticks; t++) sim.Step(in frame);
        }

        /// <summary>
        /// Сжимает вспышку до одного метра.
        ///
        /// Нужен там, где проверяется перекидывание горения: радиус вспышки
        /// и радиус перекидывания в балансе равны трём метрам, поэтому любой
        /// сосед, до которого горение могло бы перекинуться, уже задет самой
        /// вспышкой. Без этого узла тест на «Перекидывается» проверял бы
        /// попадание по площади и был бы зелёным всегда.
        ///
        /// Flat, а не More: 3 + (−2) даёт ровно единицу, без округления.
        /// </summary>
        private static AbilityNode TightRadius()
            => AbilityNode.StatMod("node.test.tight_radius",
                AbilityStatType.Radius, ModifierOp.Flat, Fix64.FromInt(-2));

        /// <summary>
        /// Убирает игрока из боя, оставляя всё остальное как есть.
        ///
        /// Нужен там, где проверяется урон от горения: иначе враг доходит
        /// до игрока, они начинают бить друг друга, и к проверяемому числу
        /// примешивается автоатака.
        /// </summary>
        private static void RemovePlayerFromFight(Simulation sim)
            => sim.Entities.Alive[Simulation.PlayerId] = false;

        /// <summary>
        /// Кастует знак и тут же убирает игрока, чтобы враги замерли.
        ///
        /// Враги идут к игроку, а знак летит полтора десятка тиков — за это
        /// время цель успевает уйти из сжатой вспышки, и проверка ломается
        /// по причине, к проверяемому отношения не имеющей.
        /// </summary>
        private static void CastAndFreeze(Simulation sim, int x, int y)
        {
            sim.Step(Cast(Fix64.FromInt(x), Fix64.FromInt(y)));
            RemovePlayerFromFight(sim);
        }

        // ---- конвейер целиком ----

        [Test]
        public void Cast_ThrowsSeal_WhichDetonatesAndBurns()
        {
            var sim = Arena();
            int enemy = PlaceEnemy(sim, 6, 0);
            int healthBefore = sim.Entities.Health[enemy];

            sim.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            Assert.That(sim.Projectiles.HighWater, Is.GreaterThan(0), "снаряд не породился");

            // Скорость 12 м/с при 30 Гц — шесть метров примерно за 15 тиков.
            Run(sim, Idle, 20);

            Assert.That(sim.Entities.Health[enemy], Is.LessThan(healthBefore), "вспышка не нанесла урон");
            Assert.That(sim.Statuses.IsBurning(enemy), Is.True, "цель не подожглась");
        }

        [Test]
        public void Burn_DealsDamageOverTime_ThenStops()
        {
            var sim = Arena();
            int enemy = PlaceEnemy(sim, 6, 0);

            sim.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            Run(sim, Idle, 20);

            // Дальше проверяется только горение, поэтому автоатаку убираем.
            RemovePlayerFromFight(sim);

            int afterHit = sim.Entities.Health[enemy];
            Run(sim, Idle, 10);
            int burning = sim.Entities.Health[enemy];
            Assert.That(burning, Is.LessThan(afterHit), "горение не тикает");

            // BurnTicks = 60: за сотню тиков обязано догореть и сняться.
            Run(sim, Idle, 100);
            Assert.That(sim.Statuses.IsBurning(enemy), Is.False, "горение не кончается");

            int burnedOut = sim.Entities.Health[enemy];
            Run(sim, Idle, 30);
            Assert.That(sim.Entities.Health[enemy], Is.EqualTo(burnedOut), "горение тикает после окончания");
        }

        [Test]
        public void Cooldown_BlocksSecondCast_AndReleasesLater()
        {
            var sim = Arena();
            PlaceEnemy(sim, 6, 0);

            InputFrame cast = Cast(Fix64.FromInt(6), Fix64.Zero);

            sim.Step(in cast);
            int afterFirst = CountLiveProjectiles(sim);
            Assert.That(afterFirst, Is.EqualTo(1));

            // Кулдаун 90 тиков: подряд второй знак уйти не должен.
            sim.Step(in cast);
            Assert.That(CountLiveProjectiles(sim), Is.EqualTo(1), "кулдаун не сработал");

            Run(sim, Idle, 120);
            sim.Step(in cast);
            Assert.That(CountLiveProjectiles(sim), Is.GreaterThan(0), "кулдаун не отпустил");
        }

        private static int CountLiveProjectiles(Simulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.Projectiles.HighWater; i++)
                if (sim.Projectiles.Alive[i]) n++;
            return n;
        }

        // ---- узел StatMod: «Жарче» ----

        [Test]
        public void NodeHotter_RaisesDamage_ByExactlyTwentyPercent()
        {
            var plain = Arena();
            var hotter = Arena(AbilityDefinition.NodeHotter());

            Fix64 baseDamage = plain.GetAbility(Slot).Get(AbilityStatType.Damage);
            Fix64 raised = hotter.GetAbility(Slot).Get(AbilityStatType.Damage);

            // 60 * 1.2 = 72, и это точное число: 0.2 в Fix64 округляется вниз,
            // поэтому сверяем с формулой, а не с целым.
            Fix64 expected = StatMath.Combine(baseDamage, Fix64.Zero, Fix64.Ratio(20, 100), Fix64.One);
            Assert.That(raised.Raw, Is.EqualTo(expected.Raw));
            Assert.That(raised.Raw, Is.GreaterThan(baseDamage.Raw));
        }

        [Test]
        public void NodeHotter_ActuallyHitsHarder()
        {
            int plainHealth = HealthAfterFlash();
            int hotterHealth = HealthAfterFlash(AbilityDefinition.NodeHotter());

            Assert.That(hotterHealth, Is.LessThan(plainHealth), "узел не доехал до урона");
        }

        /// <summary>Здоровье цели сразу после вспышки, до заметного горения.</summary>
        private static int HealthAfterFlash(params AbilityNode[] nodes)
        {
            var sim = Arena(nodes);
            int enemy = PlaceEnemy(sim, 6, 0);

            sim.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            Run(sim, Idle, 16);

            return sim.Entities.Health[enemy];
        }

        // ---- узел Flag: «Раскол» ----

        [Test]
        public void NodeSplit_TurnsOneSealIntoThree()
        {
            var plain = Arena();
            var split = Arena(AbilityDefinition.NodeSplit());

            plain.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            split.Step(Cast(Fix64.FromInt(6), Fix64.Zero));

            Assert.That(CountLiveProjectiles(plain), Is.EqualTo(1));
            Assert.That(CountLiveProjectiles(split), Is.EqualTo(3));
        }

        [Test]
        public void NodeSplit_EachShardHitsForLess()
        {
            var plain = Arena();
            var split = Arena(AbilityDefinition.NodeSplit());

            plain.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            split.Step(Cast(Fix64.FromInt(6), Fix64.Zero));

            Fix64 whole = FirstProjectileDamage(plain);
            Fix64 shard = FirstProjectileDamage(split);

            // −45%: осколок обязан бить заметно слабее целого знака.
            Assert.That(shard.Raw, Is.LessThan(whole.Raw));
            Assert.That(shard.ToDouble(), Is.EqualTo(whole.ToDouble() * 0.55).Within(0.01));
        }

        private static Fix64 FirstProjectileDamage(Simulation sim)
        {
            for (int i = 0; i < sim.Projectiles.HighWater; i++)
                if (sim.Projectiles.Alive[i]) return sim.Projectiles.Damage[i];

            Assert.Fail("живых снарядов нет");
            return Fix64.Zero;
        }

        [Test]
        public void NodeSplit_TurnsOffCleanly()
        {
            // Узел сняли — поведение обязано вернуться к исходному, а не
            // остаться включённым от прошлой пересборки.
            var sim = Arena(AbilityDefinition.NodeSplit());
            Assert.That(sim.GetAbility(Slot).Has(AbilityFlag.Split), Is.True);

            sim.SetAbility(Slot, AbilityDefinition.FlameSeal(), new AbilityNode[0], 0);
            Assert.That(sim.GetAbility(Slot).Has(AbilityFlag.Split), Is.False);

            sim.Step(Cast(Fix64.FromInt(6), Fix64.Zero));
            Assert.That(CountLiveProjectiles(sim), Is.EqualTo(1));
        }

        // ---- узел EffectInsert: «Перекидывается» ----

        [Test]
        public void NodeSpreads_IgnitesTheNearestOnDeath()
        {
            // Вспышка сжата до метра: сосед в двух метрах ею НЕ задет,
            // значит загореться он может только от перекидывания.
            var sim = Arena(AbilityDefinition.NodeSpreads(), TightRadius());

            int victim = PlaceEnemy(sim, 6, 0, health: 1);
            int neighbour = PlaceEnemy(sim, 8, 0, health: 1000);

            CastAndFreeze(sim, 6, 0);
            Run(sim, Idle, 20);

            Assert.That(sim.Entities.Alive[victim], Is.False, "цель не умерла");

            // Сосед обязан быть НЕ задет вспышкой, иначе тест проверял бы урон
            // по площади, а не перекидывание. Вспышка бьёт на 60, горение — на два
            // за тик, так что порог их надёжно разделяет: потерять больше
            // полусотни за двадцать тиков можно только попав под сам знак.
            Assert.That(sim.Entities.Health[neighbour], Is.GreaterThan(1000 - 50),
                "сосед всё-таки попал под вспышку — тест ничего не доказывает");

            Assert.That(sim.Statuses.IsBurning(neighbour), Is.True, "горение не перекинулось");
        }

        [Test]
        public void WithoutNodeSpreads_NothingSpreads()
        {
            // Та же расстановка без узла — и сосед обязан остаться холодным.
            var sim = Arena(TightRadius());

            int victim = PlaceEnemy(sim, 6, 0, health: 1);
            int neighbour = PlaceEnemy(sim, 8, 0, health: 1000);

            CastAndFreeze(sim, 6, 0);
            Run(sim, Idle, 20);

            Assert.That(sim.Entities.Alive[victim], Is.False);
            Assert.That(sim.Statuses.IsBurning(neighbour), Is.False, "эффект сработал без узла");
        }

        [Test]
        public void NodeSpreads_DoesNotJumpToAlreadyBurning()
        {
            // Перекидывание на уже горящего — это потерянный эффект.
            // Тест держит правило, потому что глазами оно не читается.
            var sim = Arena(AbilityDefinition.NodeSpreads(), TightRadius());

            int victim = PlaceEnemy(sim, 6, 0, health: 1);
            int alsoHit = PlaceEnemy(sim, 6, 0, health: 1000);   // под знаком, горит от вспышки
            int untouched = PlaceEnemy(sim, 8, 0, health: 1000); // вне вспышки, но в радиусе перекидывания

            CastAndFreeze(sim, 6, 0);
            Run(sim, Idle, 20);

            Assert.That(sim.Entities.Alive[victim], Is.False);
            Assert.That(sim.Statuses.IsBurning(alsoHit), Is.True, "задетый вспышкой не горит");
            Assert.That(sim.Statuses.IsBurning(untouched), Is.True, "горение не ушло дальше горящего");
        }

        // ---- порядок применения узлов ----

        [Test]
        public void NodeOrder_DoesNotDependOnPickOrder()
        {
            // Те же три узла в шести перестановках. Итог обязан совпасть
            // побитово: умножение в Fix64 округляет и порядок замечает,
            // а два одинаковых билда обязаны считать одинаково.
            AbilityNode[] source =
            {
                AbilityDefinition.NodeHotter(),
                AbilityDefinition.NodeSplit(),
                AbilityDefinition.NodeSpreads(),
            };

            int[][] orders =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };

            ulong expected = 0;
            for (int o = 0; o < orders.Length; o++)
            {
                var nodes = new AbilityNode[3];
                for (int i = 0; i < 3; i++) nodes[i] = source[orders[o][i]];

                var build = new AbilityBuild();
                build.Rebuild(AbilityDefinition.FlameSeal(), nodes, nodes.Length);

                ulong hash = Hashing.Offset;
                build.HashInto(ref hash);

                if (o == 0) expected = hash;
                Assert.That(hash, Is.EqualTo(expected), $"перестановка {o} дала другой билд");
            }
        }

        [Test]
        public void StackedStatMods_FollowTheThreeLayerModel()
        {
            // Два «Жарче» подряд — это Increased, они СУММИРУЮТСЯ и множатся
            // один раз, а не перемножаются. Модель та же, что у статов
            // персонажа, и расходиться они не должны.
            var nodes = new[]
            {
                AbilityNode.StatMod("node.test.a", AbilityStatType.Damage,
                    ModifierOp.Increased, Fix64.Ratio(50, 100)),
                AbilityNode.StatMod("node.test.b", AbilityStatType.Damage,
                    ModifierOp.Increased, Fix64.Ratio(50, 100)),
            };

            var build = new AbilityBuild();
            build.Rebuild(AbilityDefinition.FlameSeal(), nodes, nodes.Length);

            // 60 * (1 + 0.5 + 0.5) = 120, а не 60 * 1.5 * 1.5 = 135.
            Assert.That(build.Get(AbilityStatType.Damage), Is.EqualTo(Fix64.FromInt(120)));
        }

        [Test]
        public void CooldownStat_NeverDropsBelowOneTick()
        {
            // Кулдаун в ноль тиков означал бы каст каждый тик.
            var nodes = new[]
            {
                AbilityNode.StatMod("node.test.cdr", AbilityStatType.CooldownTicks,
                    ModifierOp.More, -Fix64.One),
            };

            var build = new AbilityBuild();
            build.Rebuild(AbilityDefinition.FlameSeal(), nodes, nodes.Length);

            Assert.That(build.CooldownTicks, Is.EqualTo(1));
        }

        // ---- детерминизм ----

        [Test]
        public void AbilitiesKeepDeterminism()
        {
            AbilityNode[] nodes =
            {
                AbilityDefinition.NodeHotter(),
                AbilityDefinition.NodeSplit(),
                AbilityDefinition.NodeSpreads(),
            };

            ulong first = RunWithCasts(nodes);
            ulong second = RunWithCasts(nodes);

            Assert.That(second, Is.EqualTo(first));
        }

        private static ulong RunWithCasts(AbilityNode[] nodes)
        {
            var sim = new Simulation(0xABCDEFUL);
            sim.SetupTestArena(30);

            // Копия массива: Rebuild сортирует его на месте, и переиспользовать
            // тот же массив между прогонами значило бы сравнивать разное.
            var copy = (AbilityNode[])nodes.Clone();
            sim.SetAbility(Slot, AbilityDefinition.FlameSeal(), copy, copy.Length);

            var rng = new Pcg32(31337UL, 5UL);
            for (int t = 0; t < 400; t++)
            {
                var frame = new InputFrame
                {
                    Aim = new FixVec2(rng.NextFix(Fix64.FromInt(-20), Fix64.FromInt(20)),
                                      rng.NextFix(Fix64.FromInt(-20), Fix64.FromInt(20))),
                    AbilityMask = (byte)(rng.NextInt(0, 10) == 0 ? 1 : 0),
                    Flags = (byte)InputFlags.MoveOrder
                };
                sim.Step(in frame);
            }

            return sim.StateHash();
        }
    }
}
