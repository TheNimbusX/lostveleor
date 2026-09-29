using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class ForestEncounterTests
    {
        private static RiftRun NewRun(ulong seed)
        {
            var run = new RiftRun(new Simulation(seed, 128), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            return run;
        }

        private static void AssertPack(RiftRun run)
        {
            EntityStore entities = run.Sim.Entities;
            int guardians = 0, swarm = 0;
            // Прототипный забег растит здоровье тем же процентом глубины, что и
            // авторские уровни: 100 на первой арене, 107 на второй.
            int percent = EnemyArchetypes.DepthHealthPercent(run.Depth);
            Assert.That(run.LevelSettings.EnemyHealth, Is.EqualTo(percent));
            for (int i = 1; i < entities.Count; i++)
            {
                Assert.That(entities.Alive[i], Is.True);
                Assert.That(run.Map.IsWalkable(entities.Position[i], entities.BodyRadius[i]), Is.True);
                if (entities.Kind[i] == EnemyKind.ForestGuardian)
                {
                    guardians++;
                    // 500/17 — подгонка «Мобов леса v2» (29.09), было 550/14.
                    Assert.That(entities.Health[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(500, percent)));
                    Assert.That(entities.Damage[i], Is.EqualTo(17));
                }
                if (entities.Kind[i] != EnemyKind.ForestRootSwarm) continue;
                swarm++;
                Assert.That(entities.Health[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(130, percent)));
                // Укус 3, цикл 30 тиков (стенд баланса, 26.09, проход 2).
                Assert.That(entities.Damage[i], Is.EqualTo(3));
                Assert.That(entities.AttackCooldown[i], Is.EqualTo(30));
                Assert.That(entities.BodyRadius[i], Is.EqualTo(Fix64.Ratio(45, 100)));
                for (int j = 4; j < i; j++)
                {
                    Fix64 spacing = Fix64.Sqrt((entities.Position[i] - entities.Position[j]).LengthSq);
                    Assert.That(spacing.ToFloat(), Is.InRange(0.9f, 2.5f),
                        "рой должен появляться одной пачкой без пересечений тел");
                }
            }
            Assert.That(guardians, Is.EqualTo(3));
            Assert.That(swarm, Is.EqualTo(6));
            Assert.That(entities.Count, Is.EqualTo(10));
        }

        [Test]
        public void EveryRunAndNextRift_HasThreeGuardiansAndSixRootSwarm()
        {
            for (ulong seed = 1; seed <= 32; seed++)
            {
                RiftRun run = NewRun(seed);
                AssertPack(run);
                for (int i = 1; i < run.Sim.Entities.Count; i++)
                    run.Sim.Entities.Alive[i] = false;
                run.Step(InputFrame.Empty);
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                run.Step(InputFrame.Empty);
                run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                AssertPack(run);
                run.StartRun();
                AssertPack(run);
            }
        }

        private static Simulation IsolatedSwarm()
        {
            Simulation sim = NewRun(123UL).Sim;
            for (int i = 1; i < sim.Entities.Count; i++) sim.Entities.Alive[i] = i == 4;
            sim.Entities.Facing[4] = new FixVec2(-Fix64.One, Fix64.Zero);
            return sim;
        }

        [Test]
        public void ReusedEntitySlot_DropsPreviousEnemyKind()
        {
            var entities = new EntityStore(2);
            int first = entities.Spawn(FixVec2.Zero, 30, Faction.Orvill);
            entities.Kind[first] = EnemyKind.ForestRootSwarm;
            entities.Clear();
            int player = entities.Spawn(FixVec2.Zero, 100, Faction.Wole);
            Assert.That(entities.Kind[player], Is.EqualTo(EnemyKind.None));
        }

        // ---------- пул пачек по уровням (лестница владельца, 29.09) ----------
        //
        // «~4 лёгких, ~10 средних, ~5 тяжёлых + элитные отдельно»; уровень
        // арены бросает ArenaRunPlan, здесь — что пул под лестницу собран:
        // окна шаблонов лежат на своём уровне, у каждого вида есть урок того
        // уровня, где он может встретиться впервые, урок показывает вид одного,
        // и тяжёлой арене всегда есть что поставить. План на 10 000 сидов —
        // ArenaEncounterTests.

        private static readonly EncounterTier[] Tiers = { EncounterTier.Easy, EncounterTier.Medium, EncounterTier.Hard };

        [Test]
        public void Pool_HasAboutFourEasyTenMediumFiveHard_PlusElites()
        {
            int[] expected = { 4, 10, 5 };
            var count = new int[3];
            int elites = 0;
            foreach (var t in ForestEncounterTemplates.All)
            {
                if (t.Type == ArenaEncounterType.Elite)
                {
                    elites++;
                    Assert.That(t.Tier, Is.EqualTo(EncounterTier.Hard), t.Key + ": элита вне лестницы, её уровень — тяжёлый");
                    continue;
                }
                count[(int)t.Tier]++;
            }
            for (int tier = 0; tier < 3; tier++)
            {
                TestContext.WriteLine(Tiers[tier] + ": " + count[tier] + " (цель ~" + expected[tier] + ")");
                Assert.That(count[tier], Is.InRange(expected[tier] - 1, expected[tier] + 1), Tiers[tier].ToString());
            }
            TestContext.WriteLine("Elite: " + elites);
            Assert.That(elites, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void EveryWindow_LiesOnItsTierOfTheLadder_ElitesInTheirSlots()
        {
            foreach (var t in ForestEncounterTemplates.All)
            {
                if (t.Type == ArenaEncounterType.Elite)
                {
                    Assert.That(t.MinArena, Is.GreaterThanOrEqualTo(ArenaRunPlan.FirstEliteMinArena), t.Key);
                    Assert.That(t.MaxArena, Is.LessThanOrEqualTo(ArenaRunPlan.SecondEliteMaxArena), t.Key);
                    // Урок элиты — всегда первая элита забега.
                    if (t.Lesson != EnemyKind.None)
                        Assert.That(t.MinArena <= ArenaRunPlan.FirstEliteMinArena && t.MaxArena >= ArenaRunPlan.FirstEliteMaxArena,
                            Is.True, t.Key + ": урок элиты закрывает всё окно первой");
                    continue;
                }
                for (int arena = t.MinArena; arena <= t.MaxArena; arena++)
                    Assert.That(ArenaRunPlan.TierWeight(arena, t.Tier), Is.GreaterThan(0),
                        t.Key + " (" + t.Tier + ") на А" + arena + " — не по лестнице");
            }
            // Каждому уровню каждой арены лестницы есть обычная пачка.
            for (int arena = 1; arena <= ForestEncounterTemplates.ArenaCount; arena++)
                foreach (var tier in Tiers)
                {
                    if (ArenaRunPlan.TierWeight(arena, tier) == 0) continue;
                    bool any = false;
                    foreach (var t in ForestEncounterTemplates.All)
                        any |= t.Type == ArenaEncounterType.Normal && t.Tier == tier && t.AllowsArena(arena);
                    Assert.That(any, Is.True, "А" + arena + " " + tier + ": нет обычной пачки");
                }
            // Лестница: А1 лёгкая, А2 60/40, А3–А5 средние, А6 50/50, А7–А8 тяжёлые.
            int[,] ladder = { { 100, 0, 0 }, { 60, 40, 0 }, { 0, 100, 0 }, { 0, 100, 0 }, { 0, 100, 0 }, { 0, 50, 50 }, { 0, 0, 100 }, { 0, 0, 100 } };
            for (int arena = 1; arena <= 8; arena++)
                for (int tier = 0; tier < 3; tier++)
                    Assert.That(ArenaRunPlan.TierWeight(arena, Tiers[tier]), Is.EqualTo(ladder[arena - 1, tier]), "А" + arena);
            Assert.That(ArenaRunPlan.TierWeight(12, EncounterTier.Hard), Is.EqualTo(100), "дальше восьмой — как восьмая");
        }

        [Test]
        public void EveryKind_HasALesson_NoLaterThanTheTierThatUsesIt()
        {
            // Вид, которого ставит пачка уровня T, учит урок уровня не выше T:
            // иначе забег, не встретивший вид раньше, не встретит его и там.
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                if (!EnemyArchetypes.IsPlaceable(kind)) continue;
                int earliestUse = int.MaxValue, earliestLesson = int.MaxValue;
                foreach (var t in ForestEncounterTemplates.All)
                {
                    if (!t.Uses(kind)) continue;
                    // Элиты — вне лестницы: их виды учатся элитным уроком в окне первой элиты.
                    int tier = t.Type == ArenaEncounterType.Elite ? (int)EncounterTier.Hard : (int)t.Tier;
                    earliestUse = System.Math.Min(earliestUse, tier);
                    if (t.Lesson == kind) earliestLesson = System.Math.Min(earliestLesson, tier);
                }
                Assert.That(earliestLesson, Is.LessThanOrEqualTo(earliestUse), kind + ": урок позже первой пачки");
                // Поздние виды Корнехват и Расщепень учатся и тяжёлым уроком — лестница
                // ставит их средний урок не в каждый забег.
                foreach (var tier in Tiers)
                {
                    bool uses = false, taught = false;
                    foreach (var t in ForestEncounterTemplates.All)
                    {
                        if (t.Type == ArenaEncounterType.Elite || t.Tier != tier || !t.Uses(kind)) continue;
                        uses = true;
                        taught |= t.Lesson == kind;
                    }
                    if (uses && (kind == EnemyKind.ForestRootSnarer || kind == EnemyKind.ForestSplitter
                        || kind == EnemyKind.ForestStonehoof))
                        Assert.That(taught, Is.True, kind + ": на уровне " + tier + " есть пачки, но нет урока");
                }
            }
        }

        [Test]
        public void LessonPacks_ShowTheNewKindAloneFirst()
        {
            // «Новый вид — сначала один»: в первой волне, где вид урока есть,
            // он один (рой — горстью, он учится сам по себе на А1).
            foreach (var t in ForestEncounterTemplates.All)
            {
                if (t.Lesson == EnemyKind.None) continue;
                Assert.That(t.GetWave(0), Is.Not.Null);
                int first = -1;
                for (int w = 0; w < t.WaveCount && first < 0; w++)
                    for (int g = 0; g < t.GetWave(w).GroupCount; g++)
                        if (t.GetWave(w).GetGroup(g).Kind == t.Lesson && t.GetWave(w).GetGroup(g).Max > 0) first = w;
                Assert.That(first, Is.EqualTo(0), t.Key + ": вид урока выходит с первой волной");
                if (t.Lesson == EnemyKind.ForestRootSwarm)
                {
                    // Урок роя — только рой.
                    for (int k = 0; k < EnemyArchetypes.Count; k++)
                        if (EnemyArchetypes.At(k).Kind != EnemyKind.ForestRootSwarm)
                            Assert.That(t.Uses(EnemyArchetypes.At(k).Kind), Is.False, t.Key);
                    continue;
                }
                int alone = 0;
                for (int g = 0; g < t.GetWave(0).GroupCount; g++)
                    if (t.GetWave(0).GetGroup(g).Kind == t.Lesson) alone += t.GetWave(0).GetGroup(g).Max;
                Assert.That(alone, Is.EqualTo(1), t.Key + ": вид урока в первой волне не один");
            }
        }

        [Test]
        public void HardArenas_AlwaysHaveThreePacks_ForARunThatKnowsOnlyTheBasics()
        {
            // Забег, где до А6 учили только рой, хранителя и плод: на тяжёлых
            // аренах (до трёх подряд — А6, А7, А8) ему всё равно есть что
            // поставить — тяжёлые уроки. Иначе плану пришлось бы снимать правила.
            var basics = new[] { EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestBud };
            for (int arena = 6; arena <= 8; arena++)
            {
                int open = 0;
                foreach (var t in ForestEncounterTemplates.All)
                {
                    if (t.Type != ArenaEncounterType.Normal || t.Tier != EncounterTier.Hard || !t.AllowsArena(arena)) continue;
                    bool fits = true;
                    for (int k = 0; k < EnemyArchetypes.Count; k++)
                    {
                        var kind = EnemyArchetypes.At(k).Kind;
                        if (t.Uses(kind) && kind != t.Lesson && System.Array.IndexOf(basics, kind) < 0) fits = false;
                    }
                    if (fits) open++;
                }
                Assert.That(open, Is.GreaterThanOrEqualTo(3), "А" + arena);
            }
        }
    }
}
