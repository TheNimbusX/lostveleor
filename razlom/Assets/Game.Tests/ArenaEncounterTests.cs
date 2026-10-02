using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Шаблоны встреч, план забега и волны (стадия 6 плана «Мобы леса»).
    ///
    /// План леса — 8 арен и босс — бросается на 10 000 сидов: уровни арен по
    /// лестнице владельца (29.09) и в её долях, одна элита на А5–А7 всегда,
    /// вторая после неё на А7–А8 в 30% забегов, вендиго и Шипомёт никогда не
    /// в одном забеге, засада и выживание не чаще раза, повторов нет, первая
    /// встреча вида — урок, и каждый вид встречается не реже чем в 45%
    /// забегов. Волны — детерминизм, выход из-под земли, выживание по
    /// таймеру, подмога босса. Состав пула по уровням — ForestEncounterTests.
    ///
    /// Корнехват, Расщепень и Шипомёт в игре с 27.09: их шаблоны — в All.
    /// Staged (виды без арта) сейчас пуст, но таблица проверяет и его.
    /// </summary>
    public class ArenaEncounterTests
    {
        private static readonly bool[] Forest = { false, false, false, false, false, false, false, false, true };

        /// <summary>Шаблоны с разными ключами: игры (All) и видов без арта (Staged).</summary>
        internal static List<ArenaEncounterTemplate> GameAndStaged()
        {
            var result = new List<ArenaEncounterTemplate>(ForestEncounterTemplates.All);
            result.AddRange(ForestEncounterTemplates.Staged);
            return result;
        }

        /// <summary>Каждый объект шаблона по разу: All и Staged.</summary>
        internal static List<ArenaEncounterTemplate> EveryTemplate() => GameAndStaged();

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
            // All — в порядке ключей: порядок входит в бросок плана.
            var all = ForestEncounterTemplates.All;
            for (int i = 1; i < all.Length; i++)
                Assert.That(string.CompareOrdinal(all[i - 1].Key, all[i].Key), Is.LessThan(0), all[i].Key);
            foreach (var t in all) Assert.That(ForestEncounterTemplates.Find(t.Key), Is.SameAs(t), t.Key);
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
            // У каждого вида, которого ставит расстановка, есть урок по All +
            // Staged (все — в All), с лестницы 29.09 — по одному на уровень, где
            // вид может встретиться впервые (ForestEncounterTests); детёныша
            // Расщепеня не ставит ни один шаблон — он только из распада.
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                if (!EnemyArchetypes.IsPlaceable(kind))
                {
                    foreach (var t in EveryTemplate()) Assert.That(t.Uses(kind), Is.False, t.Key + ": " + kind);
                    Assert.That(lessons.ContainsKey(kind), Is.False, kind + ": урок");
                    continue;
                }
                Assert.That(lessons.TryGetValue(kind, out int count) ? count : 0, Is.GreaterThanOrEqualTo(1), kind + ": урок");
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
                if (adds.GetGroup(g).Kind == EnemyKind.ForestRootSwarm) { addSwarm += adds.GetGroup(g).Max; Assert.That(adds.GetGroup(g).Min, Is.EqualTo(4)); }
            }
            // Подмога босса — хранитель и 4–5 роя (подгонка 29.09, было 2–3).
            Assert.That(addGuardians, Is.EqualTo(1));
            Assert.That(addSwarm, Is.EqualTo(5));
            // Выше двух Хранителей волну не собрать вовсе.
            Assert.Throws<System.ArgumentException>(() => new EncounterWave(WaveTrigger.Start,
                new WaveGroup(EnemyKind.ForestGuardian, 2, 2, WavePlacement.Front),
                new WaveGroup(EnemyKind.ForestGuardian, 0, 1, WavePlacement.Flank)));
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

            // Пул ровно под шаблон: все три Расщепня урока убиты честно, и каждый
            // распался — детям хватило мест. На единицу меньше — шаблон не встаёт.
            var location = ForestLocation();
            var lesson = ForestEncounterTemplates.E07;
            int arena = lesson.MinArena;
            var tight = new Simulation(3, lesson.MaxEnemies);
            Assert.Throws<System.ArgumentException>(() => location.GetLevel(arena).Spawn(tight,
                ArenaMap(location, arena, 3), 3, lesson, arena));
            for (ulong seed = 1; seed <= 2; seed++)
            {
                var sim = new Simulation(seed, lesson.MaxEnemies + 1);
                location.GetLevel(arena).Spawn(sim, ArenaMap(location, arena, seed), seed ^ 0x5151UL, lesson, arena);
                sim.PlayerInvulnerable = true;
                int killed = 0, children = 0, splits = 0;
                bool sawPendingChildren = false;
                for (int tick = 0; tick < 4000 && (sim.EncounterWavesPending || sim.HasPendingSplits || sim.CountAliveEnemies() > 0); tick++)
                {
                    if (tick % 10 == 0)
                        for (int i = 1; i < sim.Entities.Count; i++)
                        {
                            if (!sim.Entities.Alive[i] || sim.Entities.Side[i] == Faction.Wole) continue;
                            if (sim.Entities.Kind[i] == EnemyKind.ForestSplitter) killed++;
                            sim.ApplyAbilityDamage(Simulation.PlayerId, i, 1000000, -1, DamageType.Physical);
                        }
                    sim.Step(InputFrame.Empty);
                    sawPendingChildren |= sim.HasPendingSplits;
                    int reservedChildren = sim.PendingSplitCount * Simulation.SplitChildren;
                    for (int i = 1; i < sim.Entities.Count; i++)
                        if (sim.Entities.Alive[i] && sim.Entities.Kind[i] == EnemyKind.ForestSplitter)
                            reservedChildren += Simulation.SplitChildren;
                    Assert.That(sim.Entities.Count + reservedChildren, Is.LessThanOrEqualTo(sim.Entities.Capacity),
                        "места живых родителей и ожидающих детёнышей, seed " + seed + ", tick " + tick);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.SplitterSplit) { splits++; children += e.Amount; }
                }
                Assert.That(sim.EncounterWavesPending, Is.False, "seed " + seed);
                Assert.That(sawPendingChildren, Is.True, "тест прошёл задержку появления, seed " + seed);
                Assert.That(sim.HasPendingSplits, Is.False, "все ожидающие детёныши появились, seed " + seed);
                Assert.That(sim.CountAliveEnemies(), Is.Zero, "seed " + seed);
                Assert.That(killed, Is.EqualTo(3), "три Расщепня урока, seed " + seed);
                Assert.That(splits, Is.EqualTo(killed), "seed " + seed);
                Assert.That(children, Is.EqualTo(killed * Simulation.SplitChildren), "seed " + seed);
                Assert.That(sim.Entities.Count, Is.LessThanOrEqualTo(sim.Entities.Capacity));
            }
        }

        // ---------- план ----------

        /// <summary>
        /// Хватает ли забегов, где вид вообще встречается: ниже этой доли вид
        /// почти выпал из леса (E08 с А4–А6 давал вендиго 22% против 78% у
        /// Шипомёта). Все виды в один забег не входят: средних арен три-четыре,
        /// и в них же уроки, засада и выживание; вендиго и Шипомёт делят забеги
        /// пополам. До лестницы (29.09) порог был 35%, камнекопыт и Корнехват
        /// стояли на 49–50%; с тяжёлыми уроками все виды — не реже 45%.
        /// </summary>
        private const int MinKindSharePercent = 45;

        /// <summary>Допуск долей лестницы и слотов на 10 000 сидов, процентные пункты (≈6σ).</summary>
        private const double LadderTolerance = 3.0;

        /// <summary>
        /// Лестница владельца на 10 000 сидов (29.09): уровень каждой обычной и
        /// особой арены — из допустимых лестницей, и доли уровней — её веса;
        /// первая элита — на А5/А6/А7 по весам 30/30/40, вторая — после неё на
        /// А7–А8 в 30% забегов; первая встреча каждого вида — урок; повторов
        /// нет; засада и выживание — не больше чем по одной, обычных — 5–6;
        /// вендиго и Шипомёт порознь; правила не снимаются ни разу; каждый вид —
        /// не реже MinKindSharePercent забегов, каждый шаблон — не реже 1%.
        /// </summary>
        [Test]
        public void Plan_OverTenThousandSeeds_FollowsTheLadder_LessonsFirst_AndMeetsEveryKind()
        {
            const int runs = 10000;
            int secondElite = 0, secondRolled = 0, ambushes = 0, survivals = 0;
            var tiers = new int[9, 3];
            var ordinaryArenas = new int[9];
            var firstEliteAt = new int[9];
            var used = new Dictionary<string, int>();
            var where = new Dictionary<string, int>();
            var runsWith = new Dictionary<EnemyKind, int>();
            var failures = new List<string>();
            void Check(bool ok, string what)
            {
                if (!ok && failures.Count < 20) failures.Add(what);
            }
            for (ulong seed = 1; seed <= runs; seed++)
            {
                var plan = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.All);
                string at0 = "seed " + seed + ": ";
                Check(plan.LevelCount == 9 && plan.IsBoss(9) && plan.TemplateFor(9) == null, at0 + "босс не девятый");
                Check(plan.Relaxed == 0, at0 + "план снял правила (" + plan.Relaxed + ")");
                var seen = new HashSet<string>();
                var known = new HashSet<EnemyKind>();
                var met = new HashSet<EnemyKind>();
                int elites = 0, firstElite = 0, ambush = 0, survival = 0, normal = 0;
                for (int arena = 1; arena <= 8; arena++)
                {
                    var t = plan.TemplateFor(arena);
                    string at = at0 + "А" + arena + " ";
                    if (t == null) { Check(false, at + "без шаблона"); continue; }
                    at += t.Key + ": ";
                    Check(System.Array.IndexOf(ForestEncounterTemplates.All, t) >= 0, at + "не из пула");
                    Check(t.AllowsArena(arena), at + "вне своих арен");
                    Check(seen.Add(t.Key), at + "повтор");
                    used[t.Key] = used.TryGetValue(t.Key, out int n) ? n + 1 : 1;
                    string place = "A" + arena + " " + t.Key;
                    where[place] = where.TryGetValue(place, out int m) ? m + 1 : 1;
                    // Урок раньше сочетаний: каждый вид шаблона уже был, кроме его
                    // урока, — первая встреча вида всегда урок.
                    for (int k = 0; k < EnemyArchetypes.Count; k++)
                    {
                        var kind = EnemyArchetypes.At(k).Kind;
                        if (!t.Uses(kind)) continue;
                        met.Add(kind);
                        if (kind != t.Lesson) Check(known.Contains(kind), at + kind + " раньше урока");
                    }
                    if (t.Lesson != EnemyKind.None) known.Add(t.Lesson);
                    if (t.Type == ArenaEncounterType.Elite)
                    {
                        // Первая — на А5–А7, вторая — после неё, на А7–А8.
                        if (++elites == 1)
                        {
                            firstElite = arena;
                            firstEliteAt[arena]++;
                            Check(arena >= ArenaRunPlan.FirstEliteMinArena && arena <= ArenaRunPlan.FirstEliteMaxArena,
                                at + "первая элита вне окна");
                        }
                        else
                            Check(arena >= ArenaRunPlan.SecondEliteMinArena && arena <= ArenaRunPlan.SecondEliteMaxArena,
                                at + "вторая элита вне окна");
                        continue;
                    }
                    // Уровень обычной и особой арены — из тех, что допускает лестница.
                    Check(ArenaRunPlan.TierWeight(arena, t.Tier) > 0, at + t.Tier + " не по лестнице");
                    tiers[arena, (int)t.Tier]++;
                    ordinaryArenas[arena]++;
                    if (t.Type == ArenaEncounterType.Ambush) ambush++;
                    if (t.Type == ArenaEncounterType.Survival) survival++;
                    if (t.Type == ArenaEncounterType.Normal) normal++;
                }
                Check(firstElite != 0, at0 + "нет элиты");
                Check(elites == (plan.SecondEliteRolled ? 2 : 1), at0 + "элит " + elites);
                Check(!(met.Contains(EnemyKind.ForestWendigo) && met.Contains(EnemyKind.ForestThorncaster)),
                    at0 + "вендиго и Шипомёт в одном забеге");
                Check(ambush <= 1 && survival <= 1, at0 + "засад " + ambush + ", выживаний " + survival);
                Check(normal >= ArenaRunPlan.MinNormal && normal <= ArenaRunPlan.MaxNormal, at0 + "обычных " + normal);
                foreach (var kind in met) runsWith[kind] = runsWith.TryGetValue(kind, out int r) ? r + 1 : 1;
                if (elites == 2) secondElite++;
                if (plan.SecondEliteRolled) secondRolled++;
                ambushes += ambush; survivals += survival;

                if (seed % 10 == 0)
                {
                    var again = ArenaRunPlan.Roll(seed, Forest, ForestEncounterTemplates.All);
                    ulong a = 1, b = 1;
                    plan.HashInto(ref a); again.HashInto(ref b);
                    Check(a == b, at0 + "план — не функция сида");
                }
            }
            Assert.That(failures, Is.Empty);

            TestContext.WriteLine("second elite " + secondElite + "/" + runs + ", ambush " + ambushes + ", survival " + survivals);
            for (int arena = 1; arena <= 8; arena++)
            {
                string line = "A" + arena + ": elite first " + firstEliteAt[arena];
                for (int tier = 0; tier < 3; tier++)
                {
                    int weight = ArenaRunPlan.TierWeight(arena, (EncounterTier)tier);
                    double share = ordinaryArenas[arena] > 0 ? 100.0 * tiers[arena, tier] / ordinaryArenas[arena] : 0;
                    line += ", " + (EncounterTier)tier + " " + tiers[arena, tier] + " (" + share.ToString("0.0") + "% / " + weight + "%)";
                    // Доли уровней арены — веса лестницы.
                    Assert.That(share, Is.EqualTo((double)weight).Within(LadderTolerance), "А" + arena + " " + (EncounterTier)tier);
                }
                TestContext.WriteLine(line);
            }
            // Арена первой элиты — по весам 30/30/40.
            for (int arena = ArenaRunPlan.FirstEliteMinArena; arena <= ArenaRunPlan.FirstEliteMaxArena; arena++)
                Assert.That(100.0 * firstEliteAt[arena] / runs,
                    Is.EqualTo((double)ArenaRunPlan.FirstEliteWeight(arena)).Within(LadderTolerance), "первая элита на А" + arena);
            var kinds = new List<string>();
            for (int k = 0; k < EnemyArchetypes.Count; k++)
            {
                var kind = EnemyArchetypes.At(k).Kind;
                int count = runsWith.TryGetValue(kind, out int r) ? r : 0;
                TestContext.WriteLine("kind " + kind + ": " + (100.0 * count / runs).ToString("0.0") + "% of runs");
                if (!EnemyArchetypes.IsPlaceable(kind)) { Assert.That(count, Is.Zero, kind + " в шаблоне"); continue; }
                if (count * 100 < MinKindSharePercent * runs) kinds.Add(kind + " " + (100.0 * count / runs).ToString("0.0") + "%");
            }
            var places = new List<string>(where.Keys);
            places.Sort(System.StringComparer.Ordinal);
            foreach (var place in places) TestContext.WriteLine(place + ": " + where[place]);
            Assert.That(kinds, Is.Empty, "виды реже " + MinKindSharePercent + "% забегов");
            Assert.That(secondElite, Is.InRange(runs * 30 / 100 - runs / 40, runs * 30 / 100 + runs / 40));
            Assert.That(secondElite, Is.EqualTo(secondRolled), "выпавшая вторая элита всегда помещается");
            // Все шаблоны реально встают в планы — и не реже раза на сотню забегов.
            foreach (var t in ForestEncounterTemplates.All)
            {
                int count = used.TryGetValue(t.Key, out int u) ? u : 0;
                TestContext.WriteLine(t.Key + " " + t.Tier + ": " + (100.0 * count / runs).ToString("0.0") + "% of runs");
                Assert.That(count * 100, Is.GreaterThanOrEqualTo(runs), t.Key + " почти не встаёт в планы");
            }
        }

        [Test]
        public void Plan_ShortLocationsStillGetValidTemplates()
        {
            for (int arenas = 1; arenas <= 3; arenas++)
                for (ulong seed = 1; seed <= 50; seed++)
                {
                    var boss = new bool[arenas + 1];
                    boss[arenas] = true;
                    var plan = ArenaRunPlan.Roll(seed, boss, ForestEncounterTemplates.All);
                    Assert.That(plan.Relaxed, Is.Zero, "короткий лес тоже встаёт по лестнице, seed " + seed);
                    for (int arena = 1; arena <= arenas; arena++)
                    {
                        Assert.That(plan.TemplateFor(arena).AllowsArena(arena), Is.True);
                        Assert.That(plan.TemplateFor(arena).Type, Is.EqualTo(ArenaEncounterType.Normal));
                        Assert.That(ArenaRunPlan.TierWeight(arena, plan.TemplateFor(arena).Tier), Is.GreaterThan(0));
                    }
                    // А1 — лёгкий урок роя: E01 или E16.
                    Assert.That(plan.TemplateFor(1).Lesson, Is.EqualTo(EnemyKind.ForestRootSwarm));
                    Assert.That(plan.TemplateFor(1).Tier, Is.EqualTo(EncounterTier.Easy));
                    // Бесконечная локация: дальше конца плана — допустимый шаблон восьмой арены.
                    var extra = plan.TemplateFor(12);
                    Assert.That(extra.AllowsArena(8), Is.True);
                    Assert.That(System.Array.IndexOf(ForestEncounterTemplates.All, extra), Is.GreaterThanOrEqualTo(0));
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
            // А1 — лёгкий урок роя (E01 или E16), тот, что выпал плану сида.
            Assert.That(a.CurrentEncounter, Is.SameAs(ArenaRunPlan.Roll(77, location).TemplateFor(1)));
            Assert.That(a.CurrentEncounter.Lesson, Is.EqualTo(EnemyKind.ForestRootSwarm));
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
                    for (ulong seed = 1; seed <= 2; seed++)
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
                for (ulong seed = 1; seed <= 3; seed++)
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
            for (ulong seed = 1; seed <= 3; seed++)
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
                // Члены волны встают по одному: k-й — через EmergeTicks + min(4k, 12).
                int Dormant(int k) => Simulation.EmergeTicks
                    + System.Math.Min(k * Simulation.EmergeStaggerTicks, Simulation.EmergeStaggerMaxTicks);
                int emerged = 0;
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Spawn)
                    {
                        Assert.That(e.Flag, Is.True, "поздняя волна встаёт из земли");
                        Assert.That(e.Amount, Is.EqualTo(Dormant(e.Target - before)));
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
                    Assert.That(sim.EmergeTicksLeft(i), Is.EqualTo(Dormant(i - before)));
                }
                int longest = Dormant(sim.Entities.Count - 1 - before);
                for (int tick = 0; tick < longest; tick++)
                {
                    for (int i = before; i < sim.Entities.Count; i++)
                    {
                        bool dormant = tick < Dormant(i - before);
                        Assert.That(sim.IsEmerging(i), Is.EqualTo(dormant), "tick " + tick);
                        if (!dormant) continue;
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
            Assert.That(checkedWaves, Is.EqualTo(3));
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
                for (int arena = 4; arena <= 6; arena++)
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
            for (ulong seed = 1; seed <= 4; seed++)
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
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var map = ArenaMap(location, 9, seed);
                var sim = new Simulation(seed, 512) { ThicketMasterBossEnabled = false }; // временный босс-Хранитель: проверяется запасной путь
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
            // 4–5 роя с подгонки 29.09 (было 2–3): ForestEncounterTemplates.BossAdds.
            Assert.That(swarm, Is.InRange(4, 5), "seed " + seed);
            Assert.That(guardians, Is.EqualTo(1), "seed " + seed);
        }

        [Test]
        public void BossAdds_BothThresholdsInOneHit_ComeOneAfterAnother()
        {
            var location = ForestLocation();
            var map = ArenaMap(location, 9, 4);
            var sim = new Simulation(4, 512) { ThicketMasterBossEnabled = false }; // временный босс-Хранитель: проверяется запасной путь
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
