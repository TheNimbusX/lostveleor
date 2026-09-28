using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Разнос пачек волн (владелец, 26.09: «по пачкам их чуть правильнее надо
    /// рассредоточить»). Центры групп одной волны не слипаются, крупные члены
    /// группы не встают плечом к плечу, стрелки стоят дальше ближнего боя,
    /// волна встаёт из земли и бьёт впервые вразнобой — и всё детерминированно.
    ///
    /// Выборка — все шаблоны леса на меньшей и большей своей арене (и на
    /// малой поляне, если шаблон её допускает), по 30 сидов; арена гибнет
    /// каждый тик, и выходят все волны. Снимок волны — сразу после тика, в
    /// котором она встала. Центры и сущности групп — черновик последней
    /// расстановки (Simulation.LastWaveGroup).
    /// </summary>
    public class EncounterSpreadTests
    {
        private const int Seeds = 30;

        private sealed class GroupShot
        {
            public FixVec2 Anchor;
            public int First, End;
        }

        private sealed class WaveShot
        {
            public string Key;
            public int Arena, Wave, Tick, First, Count, Size;
            public ulong Seed;
            public bool Emerging;
            public FixVec2 Hero;
            public readonly List<GroupShot> Groups = new List<GroupShot>();
            public EnemyKind[] Kinds;
            public FixVec2[] Positions;
            public int[] EmergeLeft, NextAttack, EventDormant;
        }

        private static List<WaveShot> _shots;
        private static int _omitted;

        private static List<WaveShot> Shots => _shots ??= Collect();

        private static Simulation Arena(LocationDefinition location, ArenaEncounterTemplate template, int arena,
            ulong seed, int size, out LayoutMap map, out EncounterPlan plan)
        {
            map = ArenaEncounterTests.ArenaMap(location, arena, seed, size);
            var sim = new Simulation(seed, 512);
            plan = location.GetLevel(arena).Spawn(sim, map, seed ^ 0x5151UL, template, arena);
            return sim;
        }

        private static void KillAll(Simulation sim)
        {
            for (int i = 1; i < sim.Entities.Count; i++)
                if (sim.Entities.Side[i] != Faction.Wole) sim.Entities.Alive[i] = false;
        }

        private static List<WaveShot> Collect()
        {
            var shots = new List<WaveShot>();
            var location = ArenaEncounterTests.ForestLocation();
            foreach (var t in ForestEncounterTemplates.All)
            {
                int[] arenas = t.MinArena == t.MaxArena ? new[] { t.MinArena } : new[] { t.MinArena, t.MaxArena };
                // Шаблоны, допустимые на малой арене, — и на ней: там теснее всего.
                int[] sizes = t.MinArenaSize < 3 ? new[] { t.MinArenaSize, 3 } : new[] { t.MinArenaSize };
                foreach (int size in sizes)
                    foreach (int arena in arenas)
                        for (ulong seed = 1; seed <= Seeds; seed++)
                        {
                            var sim = Arena(location, t, arena, seed, size, out var map, out var plan);
                            sim.PlayerInvulnerable = true;
                            shots.Add(Shot(sim, plan, t, arena, seed, size, map.EntryPoint));
                            for (int tick = 0; tick < 4000 && sim.EncounterWavesPending; tick++)
                            {
                                int before = sim.EncounterWavesSpawned;
                                KillAll(sim);
                                sim.Step(InputFrame.Empty);
                                if (sim.EncounterWavesSpawned > before)
                                    shots.Add(Shot(sim, plan, t, arena, seed, size, sim.Entities.Position[0]));
                            }
                            Assert.That(sim.EncounterWavesPending, Is.False, t.Key + " seed " + seed);
                            _omitted += plan.OmittedEnemies;
                        }
            }
            return shots;
        }

        private static WaveShot Shot(Simulation sim, EncounterPlan plan, ArenaEncounterTemplate t, int arena,
            ulong seed, int size, FixVec2 hero)
        {
            int wave = sim.EncounterWavesSpawned - 1;
            var placement = plan.Get(plan.Count - 1);
            var shot = new WaveShot
            {
                Key = t.Key, Arena = arena, Seed = seed, Size = size, Wave = wave, Tick = sim.Tick,
                Emerging = wave > 0, Hero = hero, First = placement.FirstEntity, Count = placement.EnemyCount,
            };
            for (int g = 0; g < sim.LastWaveGroupCount; g++)
            {
                var anchor = sim.LastWaveGroup(g, out int first, out int end);
                shot.Groups.Add(new GroupShot { Anchor = anchor, First = first, End = end });
            }
            shot.Kinds = new EnemyKind[shot.Count];
            shot.Positions = new FixVec2[shot.Count];
            shot.EmergeLeft = new int[shot.Count];
            shot.NextAttack = new int[shot.Count];
            shot.EventDormant = new int[shot.Count];
            for (int n = 0; n < shot.Count; n++)
            {
                int id = shot.First + n;
                shot.Kinds[n] = sim.Entities.Kind[id];
                shot.Positions[n] = sim.Entities.Position[id];
                shot.EmergeLeft[n] = sim.EmergeTicksLeft(id);
                shot.NextAttack[n] = sim.Entities.NextAttackTick[id];
                shot.EventDormant[n] = -1;
            }
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Spawn && e.Target >= shot.First && e.Target < shot.First + shot.Count)
                    shot.EventDormant[e.Target - shot.First] = e.Flag ? e.Amount : 0;
            return shot;
        }

        private static string Where(WaveShot s) => s.Key + " A" + s.Arena + " seed " + s.Seed + " wave " + s.Wave;

        private static float Meters(Fix64 value) => value.Raw / (float)Fix64.One.Raw;

        // ---------- места ----------

        [Test]
        public void GroupAnchorsOfOneWave_StandAtLeastFiveMetresApart()
        {
            int pairs = 0, apart = 0, centerPairs = 0, centerApart = 0, smallPairs = 0, smallApart = 0;
            Fix64 spreadSq = Simulation.WaveAnchorSpread * Simulation.WaveAnchorSpread;
            foreach (var s in Shots)
            {
                Assert.That(s.Groups.Count, Is.GreaterThan(0), Where(s));
                int members = 0;
                for (int a = 0; a < s.Groups.Count; a++)
                {
                    var ga = s.Groups[a];
                    members += ga.End - ga.First;
                    Assert.That(ga.First, Is.GreaterThanOrEqualTo(s.First), Where(s));
                    Assert.That(ga.End, Is.LessThanOrEqualTo(s.First + s.Count), Where(s));
                    if (ga.End <= ga.First) continue;
                    for (int b = 0; b < a; b++)
                    {
                        var gb = s.Groups[b];
                        if (gb.End <= gb.First) continue;
                        pairs++;
                        bool ok = FixVec2.DistanceSq(ga.Anchor, gb.Anchor) >= spreadSq;
                        if (ok) apart++;
                        if (s.Size < 3) { smallPairs++; if (ok) smallApart++; }
                    }
                }
                Assert.That(members, Is.EqualTo(s.Count), "группы покрывают волну: " + Where(s));
            }
            // Две группы «в центре» одной волны: вторая встаёт флангом, не в тот же ком.
            foreach (var t in ForestEncounterTemplates.All)
                for (int w = 0; w < t.WaveCount; w++)
                {
                    int centers = 0;
                    for (int g = 0; g < t.GetWave(w).GroupCount; g++)
                        if (t.GetWave(w).GetGroup(g).Placement == WavePlacement.Center) centers++;
                    if (centers < 2) continue;
                    foreach (var s in Shots)
                    {
                        if (s.Key != t.Key || s.Wave != w || s.Groups.Count < 2) continue;
                        centerPairs++;
                        if (FixVec2.DistanceSq(s.Groups[0].Anchor, s.Groups[1].Anchor) >= spreadSq) centerApart++;
                    }
                }
            TestContext.WriteLine("центры групп: " + apart + " из " + pairs + " пар не ближе 5 м (на малой арене "
                + smallApart + " из " + smallPairs + "); две «центральные» — " + centerApart + " из " + centerPairs
                + "; не поместилось врагов: " + _omitted + " на " + Shots.Count + " волн");
            Assert.That(pairs, Is.GreaterThan(500), "выборка мала");
            Assert.That(apart * 100, Is.GreaterThanOrEqualTo(pairs * 90), "центры групп волны слиплись");
            Assert.That(centerPairs, Is.GreaterThan(30));
            Assert.That(centerApart * 100, Is.GreaterThanOrEqualTo(centerPairs * 90), "два «центра» в одном коме");
        }

        [Test]
        public void MembersOfABigKindGroup_KeepTheirSpacing_TheSwarmStillHuddles()
        {
            int pairs = 0, spaced = 0;
            var byKind = new Dictionary<EnemyKind, int[]>();
            foreach (var s in Shots)
                foreach (var g in s.Groups)
                {
                    if (g.End - g.First < 2) continue;
                    var kind = s.Kinds[g.First - s.First];
                    Fix64 spacing = Simulation.WaveMemberSpacing(kind);
                    for (int i = g.First; i < g.End; i++)
                        Assert.That(s.Kinds[i - s.First], Is.EqualTo(kind), "группа — один вид: " + Where(s));
                    if (spacing.Raw == 0) continue;
                    if (!byKind.TryGetValue(kind, out var tally)) byKind[kind] = tally = new int[2];
                    for (int i = g.First; i < g.End; i++)
                        for (int j = g.First; j < i; j++)
                        {
                            pairs++;
                            tally[0]++;
                            if (FixVec2.DistanceSq(s.Positions[i - s.First], s.Positions[j - s.First]) >= spacing * spacing)
                            {
                                spaced++;
                                tally[1]++;
                            }
                        }
                }
            foreach (var pair in byKind)
                TestContext.WriteLine(pair.Key + ": " + pair.Value[1] + " из " + pair.Value[0] + " пар держат разнос "
                    + Meters(Simulation.WaveMemberSpacing(pair.Key)) + " м");
            Assert.That(pairs, Is.GreaterThan(200), "выборка мала");
            Assert.That(spaced * 100, Is.GreaterThanOrEqualTo(pairs * 90), "члены группы встали плечом к плечу");
            Assert.That(Simulation.WaveMemberSpacing(EnemyKind.ForestRootSwarm).Raw, Is.Zero, "рой стоит кучей, как стоял");
            Assert.That(Simulation.WaveMemberSpacing(EnemyKind.ForestSplitling).Raw, Is.Zero);
        }

        [Test]
        public void RangedKinds_StandFartherFromTheHero_ThanTheMeleeOfTheirWave()
        {
            // Отдельно стартовые (от входа) и вставшие из земли (от героя в бою):
            // у них разная желаемая дальность, и среднее не смешивается.
            for (int pass = 0; pass < 2; pass++)
            {
                bool emerging = pass == 1;
                double ranged = 0, melee = 0;
                int waves = 0, fartherWaves = 0;
                foreach (var s in Shots)
                {
                    if (s.Emerging != emerging) continue;
                    double r = 0, m = 0;
                    int rc = 0, mc = 0;
                    for (int n = 0; n < s.Count; n++)
                    {
                        double d = Meters(FixVec2.Distance(s.Positions[n], s.Hero));
                        if (Simulation.WaveKeepsBack(s.Kinds[n])) { r += d; rc++; }
                        else { m += d; mc++; }
                    }
                    if (rc == 0 || mc == 0) continue;
                    waves++;
                    ranged += r / rc;
                    melee += m / mc;
                    if (r / rc > m / mc) fartherWaves++;
                }
                string what = emerging ? "из земли" : "стартовые";
                TestContext.WriteLine(what + ": стрелки в среднем в " + (ranged / waves).ToString("0.00")
                    + " м от героя, ближний бой — в " + (melee / waves).ToString("0.00") + " м; дальше в "
                    + fartherWaves + " волнах из " + waves);
                Assert.That(waves, Is.GreaterThan(100), what + ": выборка мала");
                Assert.That(ranged / waves, Is.GreaterThan(melee / waves), what + ": стрелки стоят не дальше ближнего боя");
            }
        }

        // ---------- выход и первые удары ----------

        [Test]
        public void EmergingWave_RisesOneByOne_AndTheEventCarriesEachMembersDormancy()
        {
            int checkedWaves = 0;
            foreach (var s in Shots)
            {
                if (!s.Emerging) continue;
                int low = int.MaxValue, high = int.MinValue;
                for (int n = 0; n < s.Count; n++)
                {
                    int left = s.EmergeLeft[n];
                    // k-й член волны: EmergeTicks + min(4k, 12).
                    Assert.That(left, Is.EqualTo(Simulation.EmergeTicks
                        + System.Math.Min(n * Simulation.EmergeStaggerTicks, Simulation.EmergeStaggerMaxTicks)), Where(s) + " #" + n);
                    Assert.That(s.EventDormant[n], Is.EqualTo(left), "событие выхода несёт своё бездействие: " + Where(s));
                    Assert.That(s.NextAttack[n] - s.Tick, Is.GreaterThanOrEqualTo(left), "не бьёт из-под земли: " + Where(s));
                    low = System.Math.Min(low, left);
                    high = System.Math.Max(high, left);
                }
                if (s.Count < 3) continue;
                Assert.That(high - low, Is.GreaterThanOrEqualTo(8), "волна встала разом: " + Where(s));
                checkedWaves++;
            }
            Assert.That(checkedWaves, Is.GreaterThan(300));
        }

        [Test]
        public void FirstAttacksOfAWave_AreStaggered_StartWaveToo()
        {
            int checkedWaves = 0, startWaves = 0;
            foreach (var s in Shots)
            {
                if (s.Count < 3) continue;
                int low = int.MaxValue, high = int.MinValue;
                for (int n = 0; n < s.Count; n++)
                {
                    int delay = n % Simulation.FirstAttackStaggerGroups * Simulation.FirstAttackStaggerTicks;
                    // Снимок — после тика выхода: Tick уже на единицу дальше тика появления.
                    int from = s.Emerging ? s.Tick + s.EmergeLeft[n] : s.Tick;
                    Assert.That(s.NextAttack[n], Is.GreaterThanOrEqualTo(from + delay), Where(s) + " #" + n);
                    low = System.Math.Min(low, s.NextAttack[n]);
                    high = System.Math.Max(high, s.NextAttack[n]);
                }
                Assert.That(high - low, Is.GreaterThanOrEqualTo(20), "первые удары волны залпом: " + Where(s));
                checkedWaves++;
                if (!s.Emerging) startWaves++;
            }
            Assert.That(checkedWaves, Is.GreaterThan(300));
            Assert.That(startWaves, Is.GreaterThan(100));
        }

        // ---------- детерминизм ----------

        [Test]
        public void SameSeedTwice_SpawnsEveryWaveInTheSamePlaces()
        {
            var location = ArenaEncounterTests.ForestLocation();
            string[] keys = { "forest.E04", "forest.E05", "forest.E09", "forest.E10" };
            foreach (var key in keys)
            {
                var t = ForestEncounterTemplates.Find(key);
                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    var a = Arena(location, t, t.MaxArena, seed, t.MinArenaSize < 3 ? 3 : t.MinArenaSize, out _, out _);
                    var b = Arena(location, t, t.MaxArena, seed, t.MinArenaSize < 3 ? 3 : t.MinArenaSize, out _, out _);
                    a.PlayerInvulnerable = b.PlayerInvulnerable = true;
                    for (int tick = 0; tick < 4000 && a.EncounterWavesPending; tick++)
                    {
                        Assert.That(b.Entities.Count, Is.EqualTo(a.Entities.Count), key + " seed " + seed);
                        for (int i = 0; i < a.Entities.Count; i++)
                        {
                            Assert.That(b.Entities.Position[i], Is.EqualTo(a.Entities.Position[i]), key + " seed " + seed + " #" + i);
                            Assert.That(b.Entities.NextAttackTick[i], Is.EqualTo(a.Entities.NextAttackTick[i]));
                            Assert.That(b.EmergeTicksLeft(i), Is.EqualTo(a.EmergeTicksLeft(i)));
                        }
                        Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()), key + " seed " + seed + " tick " + tick);
                        KillAll(a); KillAll(b);
                        a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                    }
                    Assert.That(a.EncounterWavesPending, Is.False);
                }
            }
        }
    }
}
