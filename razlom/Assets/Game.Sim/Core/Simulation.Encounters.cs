using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public sealed partial class Simulation
    {
        private Fix64 _encounterEntryRadius;
        private readonly struct EncounterSite
        {
            public readonly int Module, Branch, Distance;
            public readonly EncounterRole Role;
            public readonly FixVec2 Center;
            public EncounterSite(int module, int branch, EncounterRole role, FixVec2 center, int distance)
            { Module = module; Branch = branch; Role = role; Center = center; Distance = distance; }
        }

        public EncounterPlan SetupBossArena(LayoutMap map, ulong spawnSeed, int health, EncounterSettings settings)
        {
            SetupRift(map, spawnSeed, 0, 0, health);
            int module = map.GetPlaced(map.GetExit(0)).Parent;
            var center = map.CenterOf(module);
            var rng = new Pcg32(spawnSeed, 0x424F5353UL);
            var pack = settings.Pick(EncounterRole.ExitGuard, ref rng);
            EncounterGroup guardian = default;
            for (int g = 0; g < pack.GroupCount; g++)
                if (pack.GetGroup(g).Elite && pack.GetGroup(g).Kind == EnemyKind.ForestGuardian)
                { guardian = pack.GetGroup(g); break; }
            int boss = Entities.Spawn(center, health * 6, Faction.Orvill);
            ConfigureEnemy(boss, EnemyKind.ForestGuardian);
            Entities.Stats[boss].SetBase(StatType.Damage,
                Entities.Damage[boss] * Fix64.Ratio(guardian.DamagePercent * settings.DamagePercent * 5, 40000));
            Entities.RefreshStats(boss);
            _events.Add(SimEvent.Spawn(boss, center));
            var elite = new bool[Entities.Capacity]; elite[boss] = true;
            var sites = new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.ExitGuard,
                module, -1, pack.Id, center, boss, 1) };
            Grid.Rebuild(Entities);
            return new EncounterPlan(sites, elite, Fix64.FromInt(9), 0) { BossId = boss };
        }

        public EncounterPlan SetupEncounters(LayoutMap map, ulong spawnSeed, int guardianHealth, EncounterSettings settings, int entryClearance = 9)
        {
            _encounterEntryRadius = Fix64.FromInt(entryClearance);
            if (map.Routes == null || map.ExitCount == 0) throw new ArgumentException("Encounters require generated walking routes and an exit.");
            if (settings.CapacityNeeded(map.ExitCount, map.RewardBranchCount) > Entities.Capacity)
                throw new ArgumentException("Encounter population exceeds entity capacity.");
            var sites = new List<EncounterSite>();
            var candidates = new List<EncounterSite>();
            for (int m = 1; m < map.PlacedCount; m++)
            {
                if (!map.Routes.IsMainModule(m) || map.IsExit(m) || map.IsRewardBranch(m)) continue;
                if (TryEncounterSite(map, m, -1, EncounterRole.MainPath, out var site)) candidates.Add(site);
            }
            candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Module.CompareTo(b.Module));
            if (candidates.Count > 0)
            {
                var intro = candidates[0];
                sites.Add(new EncounterSite(intro.Module, -1, EncounterRole.Introduction, intro.Center, intro.Distance));
                candidates.RemoveAt(0);
                int take = Math.Min(settings.MainCount, candidates.Count);
                for (int i = 0; i < take; i++)
                    sites.Add(candidates[(i + 1) * candidates.Count / (take + 1)]);
            }
            for (int b = 0; b < map.RewardBranchCount; b++)
            {
                if (!TryEncounterSite(map, map.GetRewardBranch(b), b, EncounterRole.RewardBranch, out var site))
                    continue; // A cache near the entrance remains unguarded instead of filling the safe area.
                sites.Add(site);
            }
            for (int e = 0; e < map.ExitCount; e++)
            {
                if (!TryEncounterSite(map, map.GetExit(e), -1, EncounterRole.ExitGuard, out var site))
                    continue;
                sites.Add(site);
            }

            // Reuse the existing reset/player setup. Its local zero-count rolls never touch live streams.
            SetupRift(map, spawnSeed, 0, 0, guardianHealth);
            var placed = new List<EncounterPlacement>();
            var elite = new bool[Entities.Capacity];
            int omitted = 0;
            foreach (var site in sites)
            {
                // Each site has its own stream. Adding a side encounter does not reroll main-path packs.
                var rng = new Pcg32(spawnSeed ^ unchecked((ulong)(site.Module + 1) * 0x9E3779B97F4A7C15UL),
                    0x454E434F554E5400UL + (ulong)site.Role);
                var pack = settings.Pick(site.Role, ref rng);
                var cells = EncounterCells(map, site, settings.FormationRadius);
                for (int i = cells.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    var swap = cells[i]; cells[i] = cells[j]; cells[j] = swap;
                }
                int[] counts = new int[pack.GroupCount];
                for (int g = 0; g < counts.Length; g++)
                {
                    var group = pack.GetGroup(g);
                    counts[g] = rng.NextInt(group.Min, group.Max + 1) + (group.GrowWithDepth ? settings.CountBonus : 0);
                }
                int first = Entities.Count;
                // Reserve cramped rooms for their elite first; other groups retain authored order.
                for (int pass = 0; pass < 2; pass++)
                    for (int g = 0; g < counts.Length; g++)
                    {
                        var group = pack.GetGroup(g);
                        if (group.Elite != (pass == 0)) continue;
                        for (int n = 0; n < counts[g]; n++)
                        {
                            var radius = group.Kind == EnemyKind.ForestRootSwarm ? Fix64.Ratio(45, 100) : Fix64.Ratio(85, 100);
                            if (!TakeEncounterPoint(map, site.Module, cells, radius, ref rng, out var spot)) { omitted++; continue; }
                            int health = Math.Max(1, guardianHealth * group.HealthPercent / 100);
                            int id = Entities.Spawn(spot, health, Faction.Orvill);
                            ConfigureEnemy(id, group.Kind);
                            var sheet = Entities.Stats[id];
                            var damage = Entities.Damage[id] * Fix64.Ratio(group.DamagePercent * settings.DamagePercent, 10000);
                            sheet.SetBase(StatType.Damage, damage);
                            Entities.RefreshStats(id);
                            elite[id] = group.Elite;
                            _events.Add(SimEvent.Spawn(id, spot));
                        }
                    }
                placed.Add(new EncounterPlacement(site.Role, site.Module, site.Branch, pack.Id, site.Center,
                    first, Entities.Count - first));
            }
            Grid.Rebuild(Entities);
            return new EncounterPlan(placed, elite, settings.FormationRadius, omitted);
        }

        private bool TryEncounterSite(LayoutMap map, int module, int branch, EncounterRole role, out EncounterSite site)
        {
            var center = map.CenterOf(module);
            int best = -1;
            Fix64 nearest = Fix64.MaxValue;
            // Prefer the route itself, so mandatory guards are encountered while following it.
            for (int i = 0; i < map.Routes.CellCount; i++)
            {
                var cell = map.Routes.GetCell(i);
                if (cell.Module != module || !map.Routes.IsRoadCell(i) || !SafeEncounterPoint(map, cell.Center)) continue;
                var distance = FixVec2.DistanceSq(cell.Center, center);
                if (distance >= nearest) continue;
                best = i; nearest = distance;
            }
            // A cache trail can end inside the safe zone even though the rest of its room is usable.
            if (best < 0 && role == EncounterRole.RewardBranch)
                for (int i = 0; i < map.Routes.CellCount; i++)
                {
                    var cell = map.Routes.GetCell(i);
                    if (cell.Module != module || !SafeEncounterPoint(map, cell.Center)) continue;
                    var distance = FixVec2.DistanceSq(cell.Center, center);
                    if (distance >= nearest) continue;
                    best = i; nearest = distance;
                }
            if (best < 0) { site = default; return false; }
            site = new EncounterSite(module, branch, role, map.Routes.GetCell(best).Center, map.Routes.DistanceFromEntry(best));
            return true;
        }

        private bool SafeEncounterPoint(LayoutMap map, FixVec2 point)
            => FixVec2.DistanceSq(point, map.EntryPoint) >= _encounterEntryRadius * _encounterEntryRadius;

        private List<FixVec2> EncounterCells(LayoutMap map, EncounterSite site, Fix64 radius)
        {
            var cells = new List<FixVec2>();
            for (int i = 0; i < map.Routes.CellCount; i++)
            {
                var cell = map.Routes.GetCell(i);
                if (cell.Module == site.Module && SafeEncounterPoint(map, cell.Center) &&
                    FixVec2.DistanceSq(cell.Center, site.Center) <= radius * radius) cells.Add(cell.Center);
            }
            return cells;
        }

        private bool TakeEncounterPoint(LayoutMap map, int module, List<FixVec2> cells, Fix64 radius,
            ref Pcg32 rng, out FixVec2 point)
        {
            while (cells.Count > 0)
            {
                point = cells[cells.Count - 1]; cells.RemoveAt(cells.Count - 1);
                var jitter = Fix64.Ratio(1, 10);
                var candidate = map.ClampToWalkable(point + new FixVec2(rng.NextFix(-jitter, jitter), rng.NextFix(-jitter, jitter)), radius);
                if (map.ContainsWorld(module, candidate) && SafeEncounterPoint(map, candidate)) point = candidate;
                if (!map.IsWalkable(point, radius)) continue;
                bool free = true;
                for (int i = 0; i < Entities.Count; i++)
                {
                    var spacing = Entities.BodyRadius[i] + radius + Fix64.Ratio(1, 10);
                    if (FixVec2.DistanceSq(point, Entities.Position[i]) < spacing * spacing) { free = false; break; }
                }
                if (free) return true;
            }
            point = FixVec2.Zero;
            return false;
        }
    }
}
