using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Шаблоны встреч, план забега и волны (стадия 6 плана «Мобы леса»).
    ///
    /// План леса — 8 арен и босс — бросается на тысяче сидов: одна элита на
    /// А4–А6 всегда, вторая на А7–А8 в 25–35% забегов, засада и выживание не
    /// чаще раза, повторов нет, уроки раньше сочетаний. Волны — детерминизм,
    /// выход из-под земли, выживание по таймеру, подмога босса.
    ///
    /// Шаблоны новых видов (ForestEncounterTemplates.Staged) в игре ещё нет,
    /// но таблица, волны и план с ними (staged) проверяются здесь же: по All +
    /// Staged у каждого вида один урок, элита — вендиго или Шипомёт, и никогда
    /// оба в одном забеге.
    /// </summary>
    public class ArenaEncounterTests
    {
        private static readonly bool[] Forest = { false, false, false, false, false, false, false, false, true };

        /// <summary>Шаблоны с разными ключами: игры (All) и новых видов (Staged).</summary>
        internal static List<ArenaEncounterTemplate> GameAndStaged()
        {
            var result = new List<ArenaEncounterTemplate>(ForestEncounterTemplates.All);
            result.AddRange(ForestEncounterTemplates.Staged);
            return result;
        }

        /// <summary>
        /// Каждый объект шаблона по разу: All, Staged и копии пула Release с
        /// другими аренами (E04, E05, E08) — их бюджет и волны проверяются на
        /// их собственных аренах.
        /// </summary>
        internal static List<ArenaEncounterTemplate> EveryTemplate()
        {
            var result = GameAndStaged();
            foreach (var t in ForestEncounterTemplates.Release)
                if (!result.Contains(t)) result.Add(t);
            return result;
        }

        /// <summary>Лесная локация без Unity: 8 арен и босс, как MeadowGameplay, профиль — простой.</summary>
        internal static LocationDefinition ForestLocation(int arenas = 8)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var levels = new RiftLevelSettings[arenas + 1];
            for (int i = 0; i < levels.Length; i++)
            {
                int arena = i + 1;
                bool boss = i == arenas;
                var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                    new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                    new[] { new EncounterPack(4, 100, new[] { guardian }) },
                    1, 0, EnemyArchetypes.DepthDamagePercent(arena), Fix64.FromInt(5));
                levels[i] = new RiftLevelSettings(boss ? 20 : 11 + i, 1, 1, 2, 1, 3, EnemyArchetypes.DepthHealthPercent(arena),
                    settings, boss, playerHealth: 150, entryClearance: 14, solidEnvironment: true, naturalGlade: true)
                    .WithArenaSize(boss ? 4 : 3);
            }
            return new LocationDefinition(StableId.Of("location.test-forest"), PrototypeContent.Modules(), levels,
                64, completeAtEnd: true);
        }

        internal static LayoutMap ArenaMap(LocationDefinition location, int level, ulong seed, int size = 3)
        {
            var settings = location.GetLevel(level);
            if (!settings.Boss) settings = settings.WithArenaSize(size);
            var map = new LayoutMap(location.Modules, location.MaxModules);
            settings.Generate(new LayoutGenerator(), location.Modules, map, seed);
            return map;
        }

        // ---------- таблица ----------

        [Test]
        public void Templates_KeepGuardianLimitBudgetsAndLessons()
        {
            var keys = new HashSet<string>();
            var lessons = new Dictionary<EnemyKind, int>();
            foreach (var t in GameAndStaged())
            {
                Assert.That(keys.Add(t.Key), Is.True, "повтор ключа " + t.Key);
                Assert.That(t.Key.StartsWith("forest.E"), Is.True);
                if (t.Lesson != EnemyKind.None)
                    lessons[t.Lesson] = lessons.TryGetValue(t.Lesson, out int n) ? n + 1 : 1;
            }
            foreach (var t in EveryTemplate())
            {
                Assert.That(t.GetWave(0).Trigger.Kind, Is.EqualTo(WaveTriggerKind.Start), t.Key);
                // Вендиго и Шипомёт не встречаются в одном забеге — и в одном шаблоне тем более.
                Assert.That(ArenaRunPlan.Rivals(t, t), Is.False, t.Key + ": вендиго и Шипомёт вместе");
                int elites = 0;
                for (int w = 0; w < t.WaveCount; w++)
                {
                    var wave = t.GetWave(w);
                    if (w > 0) Assert.That(wave.Trigger.Kind, Is.Not.EqualTo(WaveTriggerKind.Start), t.Key);
                    int guardians = 0;
                    for (int g = 0; g < wave.GroupCount; g++)
                    {
                        var group = wave.GetGroup(g);
                        if (group.Kind == EnemyKind.ForestGuardian) guardians += group.Max;
                        if (group.Elite)
                        {
                            elites++;
                            Assert.That(group.Kind == EnemyKind.ForestWendigo || group.Kind == EnemyKind.ForestThorncaster,
                                Is.True, t.Key + ": элита — вендиго или Шипомёт, а не " + group.Kind);
                            Assert.That(w, Is.Zero, t.Key + ": элита стоит с начала");
                        }
                        else
                            Assert.That(group.Kind == EnemyKind.ForestWendigo || group.Kind == EnemyKind.ForestThorncaster,
                                Is.False, t.Key + ": " + group.Kind + " без флага элиты");
                    }
                    // Правило владельца: не больше двух Хранителей в одной пачке — волне.
                    Assert.That(guardians, Is.LessThanOrEqualTo(2), t.Key + " wave " + w);
                    for (int arena = t.MinArena; arena <= t.MaxArena; arena++)
                    {
                        ForestEncounterTemplates.WaveThreatRange(t, w, arena, out int min, out int max);
                        t.WaveBudget(arena, out int low, out int high);
                        Assert.That(max, Is.LessThanOrEqualTo(high), t.Key + " wave " + w + " arena " + arena);
                        // Обычная встреча держит бюджет дока целиком; прочие — только потолок.
                        if (t.Type == ArenaEncounterType.Normal)
                            Assert.That(min, Is.GreaterThanOrEqualTo(low), t.Key + " wave " + w + " arena " + arena);
                    }
                }
                Assert.That(elites, Is.EqualTo(t.Type == ArenaEncounterType.Elite ? 1 : 0), t.Key);
                Assert.That(t.SurvivalTicks > 0, Is.EqualTo(t.Type == ArenaEncounterType.Survival), t.Key);
                if (t.Type == ArenaEncounterType.Survival) Assert.That(t.SurvivalTicks, Is.EqualTo(1800));
                // С А2 обычная арена — две волны и больше.
                if (t.Type == ArenaEncounterType.Normal && t.MinArena >= 2)
                    Assert.That(t.WaveCount, Is.GreaterThanOrEqualTo(2), t.Key);
            }
            // У каждого вида, которого ставит расстановка, ровно один урок по All +
            // Staged; детёныша Расщепеня не ставит ни один шаблон — он только из распада.
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                if (!EnemyArchetypes.IsPlaceable(kind))
                {
                    foreach (var t in EveryTemplate()) Assert.That(t.Uses(kind), Is.False, t.Key + ": " + kind);
                    Assert.That(lessons.ContainsKey(kind), Is.False, kind + ": урок");
                    continue;
                }
                Assert.That(lessons.TryGetValue(kind, out int count) ? count : 0, Is.EqualTo(1), kind + ": урок");
            }
            // Пул игры полон сам по себе: каждый вид из All учит урок из All.
            foreach (var t in ForestEncounterTemplates.All)
                for (int k = 0; k < EnemyArchetypes.Count; k++)
                {
                    var kind = EnemyArchetypes.At(k).Kind;
                    if (!t.Uses(kind)) continue;
                    bool taught = false;
                    foreach (var other in ForestEncounterTemplates.All) taught |= other.Lesson == kind;
                    Assert.That(taught, Is.True, t.Key + ": урок " + kind + " не в игре");
                }
            var adds = ForestEncounterTemplates.BossAdds;
            int addGuardians = 0, addSwarm = 0;
            for (int g = 0; g < adds.GroupCount; g++)
            {
                if (adds.GetGroup(g).Kind == EnemyKind.ForestGuardian) addGuardians += adds.GetGroup(g).Max;
                if (adds.GetGroup(g).Kind == EnemyKind.ForestRootSwarm) { addSwarm += adds.GetGroup(g).Max; Assert.That(adds.GetGroup(g).Min, Is.EqualTo(2)); }
            }
            Assert.That(addGuardians, Is.EqualTo(1));
            Assert.That(addSwarm, Is.EqualTo(3));
            // Выше двух Хранителей волну не собрать вовсе.
            Assert.Throws<System.ArgumentException>(() => new EncounterWave(WaveTrigger.Start,
                new WaveGroup(EnemyKind.ForestGuardian, 2, 2, WavePlacement.Front),
                new WaveGroup(EnemyKind.ForestGuardian, 0, 1, WavePlacement.Flank)));
        }

        [Test]
        public void Staged_StaysOutOfTheGame_AndReleaseCopiesDifferOnlyInArenas()
        {
            var game = new HashSet<string>();
            foreach (var t in ForestEncounterTemplates.All) game.Add(t.Key);
            foreach (var t in ForestEncounterTemplates.Staged)
            {
                Assert.That(game.Contains(t.Key), Is.False, t.Key + " уже в игре");
                Assert.That(ForestEncounterTemplates.Find(t.Key), Is.SameAs(t));
            }
            // Release — те же ключи, что All + Staged, по разу и в порядке ключей.
            var byKey = new Dictionary<string, ArenaEncounterTemplate>();
            foreach (var t in GameAndStaged()) byKey[t.Key] = t;
            var release = ForestEncounterTemplates.Release;
            Assert.That(release.Length, Is.EqualTo(byKey.Count));
            for (int i = 0; i < release.Length; i++)
            {
                var copy = release[i];
                Assert.That(byKey.TryGetValue(copy.Key, out var source), Is.True, copy.Key);
                if (i > 0) Assert.That(string.CompareOrdinal(release[i - 1].Key, copy.Key), Is.LessThan(0), copy.Key);
                if (ReferenceEquals(copy, source)) continue;
                // Копия отличается только аренами: ключ, урок, вес, бюджет и сами волны — те же.
                Assert.That(copy.Id, Is.EqualTo(source.Id), copy.Key);
                Assert.That(copy.Type, Is.EqualTo(source.Type), copy.Key);
                Assert.That(copy.Lesson, Is.EqualTo(source.Lesson), copy.Key);
                Assert.That(copy.Weight, Is.EqualTo(source.Weight), copy.Key);
                Assert.That(copy.MinArenaSize, Is.EqualTo(source.MinArenaSize), copy.Key);
                Assert.That(copy.BudgetPercent, Is.EqualTo(source.BudgetPercent), copy.Key);
                Assert.That(copy.SurvivalTicks, Is.EqualTo(source.SurvivalTicks), copy.Key);
                Assert.That(copy.WaveCount, Is.EqualTo(source.WaveCount), copy.Key);
                for (int w = 0; w < copy.WaveCount; w++) Assert.That(copy.GetWave(w), Is.SameAs(source.GetWave(w)), copy.Key);
            }
            Assert.That(ForestEncounterTemplates.Pool(false), Is.SameAs(ForestEncounterTemplates.All));
            Assert.That(ForestEncounterTemplates.Pool(true), Is.SameAs(ForestEncounterTemplates.Release));

            // Игра — как до новых видов: временные диапазоны на месте.
            AssertArenas(ForestEncounterTemplates.All, "forest.E03", 3, 4);
            AssertArenas(ForestEncounterTemplates.All, "forest.E04", 3, 3);
            AssertArenas(ForestEncounterTemplates.All, "forest.E05", 5, 7);
            AssertArenas(ForestEncounterTemplates.All, "forest.E08", 4, 6);
            // С новыми видами — диапазоны дока; E03 — запасной А3–А4 (иначе
            // камнекопыт не встаёт ни в один план), E08 — в окне первой элиты А5–А7.
            AssertArenas(release, "forest.E03", 3, 4);
            AssertArenas(release, "forest.E04", 2, 3);
            AssertArenas(release, "forest.E05", 5, 8);
            AssertArenas(release, "forest.E08", 5, 7);
            AssertArenas(release, "forest.E08T", 5, 7);
        }

        private static void AssertArenas(ArenaEncounterTemplate[] pool, string key, int min, int max)
        {
            ArenaEncounterTemplate found = null;
            foreach (var t in pool) if (t.Key == key) found = t;
            Assert.That(found, Is.Not.Null, key);
            Assert.That(found.MinArena, Is.EqualTo(min), key);
            Assert.That(found.MaxArena, Is.EqualTo(max), key);
        }

        [Test]
        public void Capacity_CountsTheSplitterChildren()
        {
            Assert.That(EnemyArchetypes.BodiesPerSpawn(EnemyKind.ForestSplitter), Is.EqualTo(1 + Simulation.SplitChildren));
            foreach (var t in EveryTemplate())
            {
                int bodies = 0, splitters = 0;
                for (int w = 0; w < t.WaveCount; w++)
                {
                    var wave = t.GetWave(w);
                    int places = 0;
                    for (int g = 0; g < wave.GroupCount; g++)
                    {
                        var group = wave.GetGroup(g);
                        places += group.Max * EnemyArchetypes.BodiesPerSpawn(group.Kind);
                        if (group.Kind == EnemyKind.ForestSplitter) splitters += group.Max;
                    }
                    Assert.That(wave.MaxEnemies, Is.EqualTo(places), t.Key + " wave " + w);
                    bodies += places;
                }
                Assert.That(t.MaxEnemies, Is.EqualTo(bodies), t.Key);
                Assert.That(splitters > 0, Is.EqualTo(t.Uses(EnemyKind.ForestSplitter)), t.Key);
            }

            // Пул ровно под шаблон: оба Расщепня урока убиты честно, и каждый
            // распался — детям хватило мест. На единицу меньше — шаблон не встаёт.
            var location = ForestLocation();
            var lesson = ForestEncounterTemplates.E07;
            int arena = lesson.MinArena;
            var tight = new Simulation(3, lesson.MaxEnemies);
            Assert.Throws<System.ArgumentException>(() => location.GetLevel(arena).Spawn(tight,
                ArenaMap(location, arena, 3), 3, lesson, arena));
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var sim = new Simulation(seed, lesson.MaxEnemies + 1);
                location.GetLevel(arena).Spawn(sim, ArenaMap(location, arena, seed), seed ^ 0x5151UL, lesson, arena);
                sim.PlayerInvulnerable = true;
                int killed = 0, children = 0, splits = 0;
                for (int tick = 0; tick < 4000 && (sim.EncounterWavesPending || sim.CountAliveEnemies() > 0); tick++)
                {
                    if (tick % 10 == 0)
                        for (int i = 1; i < sim.Entities.Count; i++)
                        {
                            if (!sim.Entities.Alive[i] || sim.Entities.Side[i] == Faction.Wole) continue;
                            if (sim.Entities.Kind[i] == EnemyKind.ForestSplitter) killed++;
                            sim.ApplyAbilityDamage(Simulation.PlayerId, i, 1000000, -1, DamageType.Physical);
                        }
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.SplitterSplit) { splits++; children += e.Amount; }
                }
                Assert.That(sim.EncounterWavesPending, Is.False, "seed " + seed);
                Assert.That(sim.CountAliveEnemies(), Is.Zero, "seed " + seed);
                Assert.That(killed, Is.EqualTo(2), "два Расщепня урока, seed " + seed);
                Assert.That(splits, Is.EqualTo(killed), "seed " + seed);
                Assert.That(children, Is.EqualTo(killed * Simulation.SplitChildren), "seed " + seed);
                Assert.That(sim.Entities.Count, Is.LessThanOrEqualTo(sim.Entities.Capacity));
            }
        }

        // ---------- план ----------

        [Test]
        public void Plan_OverThousandSeeds_FollowsTheForestRules()
        {
            int secondElite = 0, secondRolled = 0, ambushes = 0, survivals = 0;
            var used = new Dictionary<string, int>();
            var where = new Dictionary<string, int>();
            for (ulong seed = 1; seed <= 1000; seed++)
            {
                var plan = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.All);
                Assert.That(plan.LevelCount, Is.EqualTo(9));
                Assert.That(plan.IsBoss(9), Is.True);
                Assert.That(plan.TemplateFor(9), Is.Null, "у босса нет шаблона");
                var seen = new HashSet<string>();
                var known = new HashSet<EnemyKind>();
                int firstElites = 0, lateElites = 0, ambush = 0, survival = 0, normal = 0;
                for (int arena = 1; arena <= 8; arena++)
                {
                    var t = plan.TemplateFor(arena);
                    Assert.That(t, Is.Not.Null, "seed " + seed);
                    Assert.That(t.AllowsArena(arena), Is.True, t.Key + " на А" + arena + ", seed " + seed);
                    Assert.That(seen.Add(t.Key), Is.True, "повтор " + t.Key + ", seed " + seed);
                    used[t.Key] = used.TryGetValue(t.Key, out int n) ? n + 1 : 1;
                    string at = "A" + arena + " " + t.Key;
                    where[at] = where.TryGetValue(at, out int m) ? m + 1 : 1;
                    // Урок раньше сочетаний: каждый вид шаблона уже был, кроме его урока.
                    for (int k = 0; k < EnemyArchetypes.Count; k++)
                    {
                        var kind = EnemyArchetypes.At(k).Kind;
                        if (t.Uses(kind) && kind != t.Lesson)
                            Assert.That(known.Contains(kind), Is.True, kind + " раньше урока в " + t.Key + ", seed " + seed);
                    }
                    if (t.Lesson != EnemyKind.None) known.Add(t.Lesson);
                    if (t.Type == ArenaEncounterType.Elite)
                    {
                        if (arena >= 4 && arena <= 6) firstElites++;
                        else if (arena >= 7) lateElites++;
                        else Assert.Fail("элита на А" + arena + ", seed " + seed);
                    }
                    if (t.Type == ArenaEncounterType.Ambush) ambush++;
                    if (t.Type == ArenaEncounterType.Survival) survival++;
                    if (t.Type == ArenaEncounterType.Normal) normal++;
                }
                Assert.That(firstElites, Is.EqualTo(1), "seed " + seed);
                Assert.That(lateElites, Is.EqualTo(plan.SecondEliteRolled ? 1 : 0), "seed " + seed);
                Assert.That(ambush, Is.LessThanOrEqualTo(1));
                Assert.That(survival, Is.LessThanOrEqualTo(1));
                Assert.That(normal, Is.InRange(5, 6), "seed " + seed);
                secondElite += lateElites;
                if (plan.SecondEliteRolled) secondRolled++;
                ambushes += ambush; survivals += survival;

                var again = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.All);
                ulong a = 1, b = 1;
                plan.HashInto(ref a); again.HashInto(ref b);
                Assert.That(a, Is.EqualTo(b), "план — функция сида");
            }
            TestContext.WriteLine("second elite " + secondElite + "/1000, ambush " + ambushes + ", survival " + survivals);
            var places = new List<string>(where.Keys);
            places.Sort(System.StringComparer.Ordinal);
            foreach (var place in places) TestContext.WriteLine(place + ": " + where[place]);
            Assert.That(secondElite, Is.InRange(250, 350));
            Assert.That(secondElite, Is.EqualTo(secondRolled), "выпавшая вторая элита всегда помещается");
            // Все шаблоны реально встают в планы.
            foreach (var t in ForestEncounterTemplates.All)
                Assert.That(used.ContainsKey(t.Key), Is.True, t.Key + " не встал ни в один план");
        }

        /// <summary>
        /// Хватает ли забегов, где вид вообще встречается: ниже этой доли вид
        /// почти выпал из леса (E08 с А4–А6 давал вендиго 22% против 78% у
        /// Шипомёта). Уроки нового вида — одна арена (E06 — А5, E07 — А6), а
        /// обычных встреч не больше шести, и все виды в один забег не входят.
        /// </summary>
        private const int MinKindSharePercent = 35;

        [Test]
        public void StagedPlan_OverThousandSeeds_FollowsTheReleaseRules_AndMeetsEveryKind()
        {
            int secondElite = 0, secondRolled = 0, ambushes = 0, survivals = 0;
            var used = new Dictionary<string, int>();
            var where = new Dictionary<string, int>();
            var runsWith = new Dictionary<EnemyKind, int>();
            for (ulong seed = 1; seed <= 1000; seed++)
            {
                var plan = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.Release, staged: true);
                Assert.That(plan.Staged, Is.True);
                Assert.That(plan.LevelCount, Is.EqualTo(9));
                Assert.That(plan.TemplateFor(9), Is.Null, "у босса нет шаблона");
                var seen = new HashSet<string>();
                var known = new HashSet<EnemyKind>();
                var met = new HashSet<EnemyKind>();
                int elites = 0, firstElite = 0, ambush = 0, survival = 0, normal = 0;
                for (int arena = 1; arena <= 8; arena++)
                {
                    var t = plan.TemplateFor(arena);
                    Assert.That(t, Is.Not.Null, "seed " + seed);
                    Assert.That(System.Array.IndexOf(ForestEncounterTemplates.Release, t), Is.GreaterThanOrEqualTo(0), t.Key);
                    Assert.That(t.AllowsArena(arena), Is.True, t.Key + " на А" + arena + ", seed " + seed);
                    Assert.That(seen.Add(t.Key), Is.True, "повтор " + t.Key + ", seed " + seed);
                    used[t.Key] = used.TryGetValue(t.Key, out int n) ? n + 1 : 1;
                    string at = "A" + arena + " " + t.Key;
                    where[at] = where.TryGetValue(at, out int m) ? m + 1 : 1;
                    for (int k = 0; k < EnemyArchetypes.Count; k++)
                    {
                        var kind = EnemyArchetypes.At(k).Kind;
                        if (!t.Uses(kind)) continue;
                        met.Add(kind);
                        if (kind != t.Lesson)
                            Assert.That(known.Contains(kind), Is.True, kind + " раньше урока в " + t.Key + ", seed " + seed);
                    }
                    if (t.Lesson != EnemyKind.None) known.Add(t.Lesson);
                    if (t.Type == ArenaEncounterType.Elite)
                    {
                        // Первая — на А5–А7, вторая — после неё, на А7–А8.
                        if (++elites == 1)
                        {
                            firstElite = arena;
                            Assert.That(arena, Is.InRange(ArenaRunPlan.StagedFirstEliteMinArena,
                                ArenaRunPlan.StagedFirstEliteMaxArena), "первая элита на А" + arena + ", seed " + seed);
                        }
                        else
                            Assert.That(arena, Is.InRange(ArenaRunPlan.SecondEliteMinArena,
                                ArenaRunPlan.SecondEliteMaxArena), "вторая элита на А" + arena + ", seed " + seed);
                    }
                    if (t.Type == ArenaEncounterType.Ambush) ambush++;
                    if (t.Type == ArenaEncounterType.Survival) survival++;
                    if (t.Type == ArenaEncounterType.Normal) normal++;
                }
                Assert.That(firstElite, Is.Not.Zero, "seed " + seed);
                Assert.That(elites, Is.EqualTo(plan.SecondEliteRolled ? 2 : 1), "seed " + seed);
                Assert.That(met.Contains(EnemyKind.ForestWendigo) && met.Contains(EnemyKind.ForestThorncaster), Is.False,
                    "вендиго и Шипомёт в одном забеге, seed " + seed);
                Assert.That(ambush, Is.LessThanOrEqualTo(1));
                Assert.That(survival, Is.LessThanOrEqualTo(1));
                Assert.That(normal, Is.InRange(5, 6), "seed " + seed);
                foreach (var kind in met) runsWith[kind] = runsWith.TryGetValue(kind, out int r) ? r + 1 : 1;
                if (elites == 2) secondElite++;
                if (plan.SecondEliteRolled) secondRolled++;
                ambushes += ambush; survivals += survival;

                var again = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.Release, staged: true);
                ulong a = 1, b = 1, game = 1;
                plan.HashInto(ref a); again.HashInto(ref b);
                Assert.That(a, Is.EqualTo(b), "план — функция сида");
                ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.All).HashInto(ref game);
                Assert.That(game, Is.Not.EqualTo(a), "план с новыми видами — другой план");
            }
            TestContext.WriteLine("staged: second elite " + secondElite + "/1000, ambush " + ambushes + ", survival " + survivals);
            var kinds = new List<string>();
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                int runs = runsWith.TryGetValue(kind, out int r) ? r : 0;
                TestContext.WriteLine("kind " + kind + ": " + runs / 10.0 + "% of runs");
                if (!EnemyArchetypes.IsPlaceable(kind)) { Assert.That(runs, Is.Zero, kind + " в шаблоне"); continue; }
                if (runs * 100 < MinKindSharePercent * 1000) kinds.Add(kind + " " + runs / 10.0 + "%");
            }
            var places = new List<string>(where.Keys);
            places.Sort(System.StringComparer.Ordinal);
            foreach (var place in places) TestContext.WriteLine(place + ": " + where[place]);
            Assert.That(kinds, Is.Empty, "виды реже " + MinKindSharePercent + "% забегов");
            Assert.That(secondElite, Is.InRange(250, 350));
            Assert.That(secondElite, Is.EqualTo(secondRolled), "выпавшая вторая элита всегда помещается");
            foreach (var t in ForestEncounterTemplates.Release)
                Assert.That(used.ContainsKey(t.Key), Is.True, t.Key + " не встал ни в один план");
        }

        [Test]
        public void Plan_ShortLocationsStillGetValidTemplates()
        {
            foreach (bool staged in new[] { false, true })
                for (int arenas = 1; arenas <= 3; arenas++)
                    for (ulong seed = 1; seed <= 50; seed++)
                    {
                        var boss = new bool[arenas + 1];
                        boss[arenas] = true;
                        var plan = ArenaRunPlan.Roll(seed, boss, ForestEncounterTemplates.Pool(staged), staged);
                        for (int arena = 1; arena <= arenas; arena++)
                        {
                            Assert.That(plan.TemplateFor(arena).AllowsArena(arena), Is.True);
                            Assert.That(plan.TemplateFor(arena).Type, Is.Not.EqualTo(ArenaEncounterType.Elite));
                        }
                        Assert.That(plan.TemplateFor(1), Is.SameAs(ForestEncounterTemplates.E01));
                        // Бесконечная локация: дальше конца плана — допустимый шаблон восьмой арены.
                        var extra = plan.TemplateFor(12);
                        Assert.That(extra.AllowsArena(8), Is.True);
                        Assert.That(System.Array.IndexOf(ForestEncounterTemplates.Pool(staged), extra), Is.GreaterThanOrEqualTo(0));
                        Assert.That(plan.TemplateFor(12), Is.SameAs(extra));
                    }
        }

        [Test]
        public void Run_RollsThePlanOnce_FromTheMasterSeed_AndHashesIt()
        {
            var location = ForestLocation();
            var a = new RiftRun(new Simulation(77, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            var b = new RiftRun(new Simulation(77, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            a.StartRun(); b.StartRun();
            Assert.That(a.TemplateFlow, Is.True);
            Assert.That(a.Plan, Is.Not.Null);
            Assert.That(a.CurrentEncounter, Is.SameAs(ForestEncounterTemplates.E01));
            Assert.That(a.Sim.ActiveEncounter, Is.SameAs(a.CurrentEncounter));
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            ulong expected = 1, actual = 1;
            ArenaRunPlan.Roll(77, location).HashInto(ref expected);
            a.Plan.HashInto(ref actual);
            Assert.That(actual, Is.EqualTo(expected));
            // Сиды уровней не сдвинулись: план бросается своим потоком.
            var seeds = RiftLevelSeeds.ForLevel(77, 1);
            Assert.That(a.LayoutSeed, Is.EqualTo(seeds.Layout));
            Assert.That(a.SpawnSeed, Is.EqualTo(seeds.Spawns));
        }

        // ---------- волны ----------

        private static Simulation Arena(LocationDefinition location, ArenaEncounterTemplate template, int arena,
            ulong seed, out LayoutMap map, int hard = 100)
        {
            map = ArenaMap(location, arena, seed, template.MinArenaSize < 3 ? 3 : template.MinArenaSize);
            var sim = new Simulation(seed, 512);
            location.GetLevel(arena).Spawn(sim, map, seed ^ 0x5151UL, template, arena, hard);
            return sim;
        }

        private static void KillAll(Simulation sim)
        {
            for (int i = 1; i < sim.Entities.Count; i++)
                if (sim.Entities.Side[i] != Faction.Wole) sim.Entities.Alive[i] = false;
        }

        [Test]
        public void EveryTemplate_SpawnsWithinBudget_AtMostTwoGuardiansPerWave_AndDeterministically()
        {
            var location = ForestLocation();
            foreach (var t in EveryTemplate())
                for (int arena = t.MinArena; arena <= t.MaxArena; arena++)
                    for (ulong seed = 1; seed <= 4; seed++)
                    {
                        var a = Arena(location, t, arena, seed, out var map);
                        var b = Arena(location, t, arena, seed, out _);
                        a.PlayerInvulnerable = b.PlayerInvulnerable = true;
                        Assert.That(a.ActiveEncounter, Is.SameAs(t));
                        Assert.That(a.EncounterWavesSpawned, Is.EqualTo(1));
                        int omitted = 0;
                        for (int tick = 0; tick < 4000 && (a.EncounterWavesPending || a.CountAliveEnemies() > 0); tick++)
                        {
                            // Каждые полсекунды вся арена гибнет: волны выходят по порогам и срокам.
                            if (tick % 15 == 0) { KillAll(a); KillAll(b); }
                            a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), t.Key + " seed " + seed + " tick " + tick);
                        }
                        Assert.That(a.EncounterWavesPending, Is.False, t.Key + " не выпустил все волны");
                        Assert.That(a.EncounterWavesSpawned, Is.EqualTo(t.WaveCount));
                        var plan = PlanOf(a);
                        omitted += plan.OmittedEnemies;
                        Assert.That(omitted, Is.Zero, t.Key + " arena " + arena + " seed " + seed);
                        for (int w = 0; w < plan.Count; w++)
                        {
                            var placement = plan.Get(w);
                            int guardians = 0, threat = 0;
                            for (int i = placement.FirstEntity; i < placement.FirstEntity + placement.EnemyCount; i++)
                            {
                                var kind = a.Entities.Kind[i];
                                if (kind == EnemyKind.ForestGuardian) guardians++;
                                threat += EnemyArchetypes.Get(kind).Threat;
                                // Здоровье и урон — строка вида × глубина арены.
                                Assert.That(a.Entities.MaxHealth[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(
                                    a.ArchetypeHealth(kind), EnemyArchetypes.DepthHealthPercent(arena))), kind.ToString());
                                Assert.That(a.Entities.BodyRadius[i], Is.EqualTo(a.ArchetypeBodyRadius(kind)));
                                Assert.That(plan.IsElite(i), Is.EqualTo(kind == EnemyKind.ForestWendigo
                                    || kind == EnemyKind.ForestThorncaster), kind.ToString());
                            }
                            Assert.That(guardians, Is.LessThanOrEqualTo(2), t.Key + " wave " + w);
                            t.WaveBudget(arena, out int low, out int high);
                            Assert.That(threat, Is.LessThanOrEqualTo(high), t.Key + " wave " + w + " arena " + arena);
                            if (t.Type == ArenaEncounterType.Normal)
                                Assert.That(threat, Is.GreaterThanOrEqualTo(low), t.Key + " wave " + w + " arena " + arena);
                        }
                    }
        }

        private static EncounterPlan PlanOf(Simulation sim)
        {
            var field = typeof(Simulation).GetField("_encounterPlan",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (EncounterPlan)field.GetValue(sim);
        }

        [Test]
        public void StartWave_StandsAwayFromTheEntry_OnTheArenaFloor()
        {
            var location = ForestLocation();
            foreach (var t in EveryTemplate())
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var sim = Arena(location, t, t.MinArena, seed, out var map);
                    Assert.That(sim.CountAliveEnemies(), Is.GreaterThan(0));
                    for (int i = 1; i < sim.Entities.Count; i++)
                    {
                        Assert.That(map.IsWalkable(sim.Entities.Position[i], sim.Entities.BodyRadius[i]), Is.True, t.Key);
                        Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint) >= Fix64.FromInt(196), Is.True, t.Key);
                        Assert.That(sim.IsEmerging(i), Is.False, "стартовая волна стоит с начала");
                        for (int j = 1; j < i; j++)
                        {
                            var spacing = sim.Entities.BodyRadius[i] + sim.Entities.BodyRadius[j];
                            Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], sim.Entities.Position[j]) >= spacing * spacing, Is.True);
                        }
                    }
                }
        }

        [Test]
        public void LaterWave_EmergesAwayFromTheHero_AndStaysDormantFor24Ticks()
        {
            var location = ForestLocation();
            int checkedWaves = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var sim = Arena(location, ForestEncounterTemplates.E02, 2, seed, out var map);
                sim.PlayerInvulnerable = true;
                // Герой посреди арены, первая волна мертва.
                var hero = map.GetGlade(0).Center;
                hero = map.ClampToWalkable(hero, sim.Entities.BodyRadius[0]);
                sim.Entities.Position[0] = hero;
                sim.Grid.Rebuild(sim.Entities);
                int before = sim.Entities.Count;
                KillAll(sim);
                sim.Step(InputFrame.Empty);
                Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(2), "seed " + seed);
                Assert.That(sim.Entities.Count, Is.GreaterThan(before));
                int emerged = 0;
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Spawn)
                    {
                        Assert.That(e.Flag, Is.True, "поздняя волна встаёт из земли");
                        Assert.That(e.Amount, Is.EqualTo(Simulation.EmergeTicks));
                        emerged++;
                    }
                Assert.That(emerged, Is.EqualTo(sim.Entities.Count - before));
                bool waveEvent = false;
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EncounterWave) { waveEvent = true; Assert.That(e.Amount, Is.EqualTo(1)); Assert.That(e.ActionVariant, Is.EqualTo(ForestEncounterTemplates.E02.WaveCount)); }
                Assert.That(waveEvent, Is.True);
                var start = new FixVec2[sim.Entities.Count];
                for (int i = before; i < sim.Entities.Count; i++)
                {
                    start[i] = sim.Entities.Position[i];
                    Assert.That(FixVec2.DistanceSq(start[i], hero) >= Fix64.FromInt(36), Is.True, "ближе 6 м к герою");
                    Assert.That(map.IsWalkable(start[i], sim.Entities.BodyRadius[i]), Is.True);
                    Assert.That(sim.Entities.Aggro[i], Is.True, "вставший сразу идёт на героя");
                    Assert.That(sim.EmergeTicksLeft(i), Is.EqualTo(Simulation.EmergeTicks));
                }
                for (int tick = 0; tick < Simulation.EmergeTicks; tick++)
                {
                    for (int i = before; i < sim.Entities.Count; i++)
                    {
                        Assert.That(sim.IsEmerging(i), Is.True, "tick " + tick);
                        Assert.That(sim.Entities.Position[i], Is.EqualTo(start[i]), "встающий не ходит, tick " + tick);
                        Assert.That(sim.Entities.PendingAttackTarget[i], Is.EqualTo(-1), "и не бьёт");
                    }
                    sim.Step(InputFrame.Empty);
                }
                bool moved = false;
                for (int i = before; i < sim.Entities.Count; i++) Assert.That(sim.IsEmerging(i), Is.False);
                for (int tick = 0; tick < 30; tick++) sim.Step(InputFrame.Empty);
                for (int i = before; i < sim.Entities.Count; i++)
                    moved |= FixVec2.DistanceSq(sim.Entities.Position[i], start[i]) > Fix64.Ratio(1, 4);
                Assert.That(moved, Is.True, "после выхода волна идёт на героя");
                checkedWaves++;
            }
            Assert.That(checkedWaves, Is.EqualTo(10));
        }

        [Test]
        public void Survival_RunsOneMinute_TimedWaves_ThenTheRestBurrow()
        {
            var location = ForestLocation();
            var sim = Arena(location, ForestEncounterTemplates.E12, 5, 3, out _);
            sim.PlayerInvulnerable = true;
            Assert.That(sim.SurvivalTicksLeft, Is.EqualTo(1800));
            for (int i = 1; i < sim.Entities.Count; i++) Assert.That(sim.Entities.Aggro[i], Is.True, "выживание идёт на героя сразу");
            // Волны по таймеру, пока на арене есть живые: каждые 11 с.
            int spawnedAt330 = -1;
            for (int tick = 1; tick <= 1800; tick++)
            {
                // Живые держатся: герой не бьёт, но и сам не умирает.
                sim.Step(InputFrame.Empty);
                if (spawnedAt330 < 0 && sim.EncounterWavesSpawned == 2) spawnedAt330 = tick;
                if (tick < 1800)
                {
                    Assert.That(sim.SurvivalTicksLeft, Is.EqualTo(1800 - tick));
                    Assert.That(sim.CountAliveEnemies(), Is.GreaterThan(0));
                }
            }
            Assert.That(spawnedAt330, Is.EqualTo(ForestEncounterTemplates.SurvivalWaveTicks));
            Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(5), "все пять волн вышли за минуту");
            Assert.That(sim.SurvivalTicksLeft, Is.Zero);
            Assert.That(sim.CountAliveEnemies(), Is.Zero, "по таймеру оставшиеся ушли в землю");
            Assert.That(sim.EncounterWavesPending, Is.False);
            int burrowed = 0;
            foreach (var e in sim.Events) if (e.Type == SimEventType.Burrowed) burrowed++;
            Assert.That(burrowed, Is.GreaterThan(0));
            Assert.That(sim.Entities.Alive[0], Is.True);
        }

        [Test]
        public void Survival_ClearedEarly_EndsWithTheLastWave_AndStopsTheTimer()
        {
            var location = ForestLocation();
            var sim = Arena(location, ForestEncounterTemplates.E12, 6, 11, out _);
            sim.PlayerInvulnerable = true;
            int ticks = 0;
            // Пустая арена волну не ждёт: пять волн подряд, каждая — через подъём из земли.
            for (; ticks < 1800 && (sim.EncounterWavesPending || sim.CountAliveEnemies() > 0); ticks++)
            {
                KillAll(sim);
                sim.Step(InputFrame.Empty);
            }
            Assert.That(sim.EncounterWavesSpawned, Is.EqualTo(ForestEncounterTemplates.E12.WaveCount));
            Assert.That(ticks, Is.LessThan(5 * Simulation.EmergeTicks + 10), "волны выживания не ждут своего таймера на пустой арене");
            sim.Step(InputFrame.Empty);
            Assert.That(sim.SurvivalTicksLeft, Is.Zero, "таймер над пустой ареной не тикает");
            Assert.That(sim.EncounterWavesPending, Is.False);
        }

        [Test]
        public void Survival_InTheRun_CountsAsClearedWhenTheTimerEnds_WithoutXp()
        {
            var location = ForestLocation();
            RiftRun run = null;
            for (ulong seed = 1; seed <= 400 && run == null; seed++)
            {
                var plan = ArenaRunPlan.Roll(seed, location);
                for (int arena = 5; arena <= 7; arena++)
                    if (plan.TemplateFor(arena).Type == ArenaEncounterType.Survival)
                    {
                        run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                            PrototypeContent.ItemBaseIds(), location: location);
                        run.StartTestAtLevel(arena, false);
                        break;
                    }
            }
            Assert.That(run, Is.Not.Null);
            Assert.That(run.CurrentEncounter.Type, Is.EqualTo(ArenaEncounterType.Survival));
            run.Sim.PlayerInvulnerable = true;
            int xp = run.Sim.PendingXp;
            for (int tick = 0; tick < 1799; tick++)
            {
                run.Step(InputFrame.Empty);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "tick " + tick);
            }
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit), "выживание кончилось — арена засчитана");
            Assert.That(run.Sim.PendingXp, Is.EqualTo(xp), "ушедшие в землю опыта не дают");
        }

        [Test]
        public void Run_ClearsOnlyWhenEveryWaveIsOutAndDead()
        {
            var location = ForestLocation();
            var run = new RiftRun(new Simulation(5, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartTestAtLevel(2, false);
            var template = run.CurrentEncounter;
            Assert.That(template.WaveCount, Is.GreaterThanOrEqualTo(2));
            // Первая волна мертва — но вторая ещё не вышла: арена не зачищена.
            for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            Assert.That(run.CountRequiredEnemies(), Is.GreaterThan(0), "вторая волна встала в тот же тик");
            for (int guard = 0; guard < 400 && run.Phase == RunPhase.Clearing; guard++)
            {
                for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                run.Step(InputFrame.Empty);
            }
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            Assert.That(run.Sim.EncounterWavesSpawned, Is.EqualTo(template.WaveCount));
            Assert.That(run.Encounters.Count, Is.EqualTo(template.WaveCount), "по размещению плана на волну");
        }

        [Test]
        public void HardRoute_AppliesToLaterWavesToo()
        {
            var location = ForestLocation();
            var sim = Arena(location, ForestEncounterTemplates.E02, 2, 9, out _, hard: EnemyArchetypes.HardRoutePercent);
            sim.PlayerInvulnerable = true;
            int before = sim.Entities.Count;
            KillAll(sim);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Count, Is.GreaterThan(before));
            for (int i = 1; i < sim.Entities.Count; i++)
            {
                var kind = sim.Entities.Kind[i];
                Assert.That(sim.Entities.MaxHealth[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(sim.ArchetypeHealth(kind),
                    EnemyArchetypes.DepthHealthPercent(2), EnemyArchetypes.HardRoutePercent)), kind + " #" + i);
                int damage = CombatStats.RoundToInt(Fix64.FromInt(EnemyArchetypes.Get(kind).BaseDamage)
                    * Fix64.Ratio(EnemyArchetypes.DepthDamagePercent(2) * EnemyArchetypes.HardRoutePercent, 10000));
                Assert.That(sim.Entities.Damage[i], Is.EqualTo(damage), kind + " #" + i);
            }
        }

        [Test]
        public void RouteOffers_NeverOfferAnArenaTooSmallForTheNextTemplate()
        {
            var location = ForestLocation();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                    PrototypeContent.ItemBaseIds(), location: location);
                run.StartRun();
                for (int depth = 1; depth <= 4; depth++)
                {
                    for (int guard = 0; guard < 4000 && run.Phase == RunPhase.Clearing; guard++)
                    {
                        for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                        run.Step(InputFrame.Empty);
                    }
                    run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
                    run.Step(InputFrame.Empty);
                    Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
                    run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                    if (run.Phase == RunPhase.ReplacingAbility) run.Step(new InputFrame { Command = (byte)RunCommand.SalvageAbility });
                    Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingRoute));
                    var next = run.Plan.TemplateFor(depth + 1);
                    for (int r = 0; r < 3; r++)
                        Assert.That(run.GetRoute(r).Size, Is.GreaterThanOrEqualTo(next.MinArenaSize), next.Key);
                    run.Step(new InputFrame { Command = (byte)RunCommand.ChooseRoute1 });
                    Assert.That(run.CurrentEncounter, Is.SameAs(next));
                }
            }
        }

        // ---------- босс ----------

        [Test]
        public void BossAdds_EmergeAt66And33Percent_OnceEach()
        {
            var location = ForestLocation();
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var map = ArenaMap(location, 9, seed);
                var sim = new Simulation(seed, 512);
                var plan = location.GetLevel(9).Spawn(sim, map, seed, null, 9);
                sim.PlayerInvulnerable = true;
                int boss = plan.BossId;
                Assert.That(sim.Entities.Count, Is.EqualTo(2));
                int max = sim.Entities.MaxHealth[boss];

                sim.Entities.Health[boss] = max * 70 / 100;
                sim.Step(InputFrame.Empty);
                Assert.That(sim.BossAddWavesSpawned, Is.Zero);
                Assert.That(sim.Entities.Count, Is.EqualTo(2));

                sim.Entities.Health[boss] = max * 66 / 100;
                sim.Step(InputFrame.Empty);
                Assert.That(sim.BossAddWavesSpawned, Is.EqualTo(1), "seed " + seed);
                CheckAdds(sim, 2, sim.Entities.Count, seed);
                int afterFirst = sim.Entities.Count;
                for (int tick = 0; tick < 60; tick++) sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Count, Is.EqualTo(afterFirst), "на 66% — один раз");

                sim.Entities.Health[boss] = max * 20 / 100;
                sim.Step(InputFrame.Empty);
                Assert.That(sim.BossAddWavesSpawned, Is.EqualTo(2));
                CheckAdds(sim, afterFirst, sim.Entities.Count, seed);
                int afterSecond = sim.Entities.Count;
                sim.Entities.Health[boss] = 1;
                for (int tick = 0; tick < 60; tick++) sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Count, Is.EqualTo(afterSecond), "третьей волны нет");
                Assert.That(sim.EncounterWavesPending, Is.False, "подмога босса не держит зачистку");
            }
        }

        private static void CheckAdds(Simulation sim, int from, int to, ulong seed)
        {
            int swarm = 0, guardians = 0;
            for (int i = from; i < to; i++)
            {
                if (sim.Entities.Kind[i] == EnemyKind.ForestRootSwarm) swarm++;
                if (sim.Entities.Kind[i] == EnemyKind.ForestGuardian) guardians++;
                Assert.That(sim.IsEmerging(i), Is.True);
                Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], sim.Entities.Position[0]) >= Fix64.FromInt(36), Is.True);
                Assert.That(sim.Entities.MaxHealth[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(
                    sim.ArchetypeHealth(sim.Entities.Kind[i]), EnemyArchetypes.DepthHealthPercent(9))));
            }
            Assert.That(swarm, Is.InRange(2, 3), "seed " + seed);
            Assert.That(guardians, Is.EqualTo(1), "seed " + seed);
        }

        [Test]
        public void BossAdds_BothThresholdsInOneHit_ComeOneAfterAnother()
        {
            var location = ForestLocation();
            var map = ArenaMap(location, 9, 4);
            var sim = new Simulation(4, 512);
            var plan = location.GetLevel(9).Spawn(sim, map, 4, null, 9);
            sim.PlayerInvulnerable = true;
            sim.Entities.Health[plan.BossId] = sim.Entities.MaxHealth[plan.BossId] / 10;
            sim.Step(InputFrame.Empty);
            Assert.That(sim.BossAddWavesSpawned, Is.EqualTo(1));
            for (int tick = 0; tick < Simulation.EmergeTicks && sim.BossAddWavesSpawned == 1; tick++) sim.Step(InputFrame.Empty);
            Assert.That(sim.BossAddWavesSpawned, Is.EqualTo(2));
        }
    }
}
