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

        /// <summary>
        /// Арена временного босса: Хранитель с EnemyArchetypes.InterimBossHealth,
        /// выросшим с глубиной, как у всех (healthPercent — EnemyHealth уровня).
        /// Удар — Хранителя ×1,25 и рост урона профиля; процентов пачки у босса
        /// нет. Ярость на половине здоровья включает RiftRun.
        ///
        /// Подмога (стадия 6 плана): на 66% и 33% здоровья босса, по разу, из
        /// земли встают 2–3 роя и хранитель (ForestEncounterTemplates.BossAdds)
        /// — см. Simulation.EncounterWaves. Их здоровье и урон — глубина уровня;
        /// hardPercent — маршрут «Сложно» (125) для босса и подмоги разом.
        /// </summary>
        public EncounterPlan SetupBossArena(LayoutMap map, ulong spawnSeed, int healthPercent, EncounterSettings settings,
            int hardPercent = 100, int arena = ForestEncounterTemplates.ArenaCount + 1)
        {
            SetupRift(map, spawnSeed, 0, 0, 1);
            int module = map.GetPlaced(map.GetExit(0)).Parent;
            var center = BossFloorPoint(map, map.CenterOf(module), EnemyArchetypes.GuardianBodyRadius);
            // Пачка выхода выбирается по-прежнему: её ключ остаётся в плане
            // встреч и в хеше, хотя состав босс больше не читает.
            var rng = new Pcg32(spawnSeed, 0x424F5353UL);
            var pack = settings.Pick(EncounterRole.ExitGuard, ref rng);
            int boss = Entities.Spawn(center,
                EnemyArchetypes.ScaleHealth(EnemyArchetypes.InterimBossHealth, healthPercent, hardPercent), Faction.Orvill);
            ConfigureEnemy(boss, EnemyKind.ForestGuardian);
            Entities.XpReward[boss] = Progression.BossKillXp;
            Entities.Stats[boss].SetBase(StatType.Damage, Entities.Damage[boss]
                * Fix64.Ratio(EnemyArchetypes.InterimBossDamagePercent * settings.DamagePercent, 10000)
                * Fix64.Ratio(hardPercent, 100));
            Entities.RefreshStats(boss);
            _events.Add(SimEvent.Spawn(boss, center));
            var elite = new bool[Entities.Capacity]; elite[boss] = true;
            _eliteMask = elite;
            var sites = new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.ExitGuard,
                module, -1, pack.Id, center, boss, 1) };
            Grid.Rebuild(Entities);
            var plan = new EncounterPlan(sites, elite, Fix64.FromInt(9), 0) { BossId = boss };
            if (map.Routes != null)
            {
                PrepareBossAdds(map, spawnSeed, arena, healthPercent, settings.DamagePercent, hardPercent, boss);
                _encounterPlan = plan;
            }
            return plan;
        }

        /// <summary>
        /// Где встаёт босс: центр комнаты перед выходом, если там пол, иначе
        /// ближайшая к нему клетка маршрута, до которой можно дойти от входа
        /// и где тело босса стоит на полу. Центр угловой комнаты поляны бывает
        /// за её контуром: сессия с сидом 10 ставила босса в (−30, 35), вне
        /// пола, и до него было не дойти. При равном расстоянии — младшая клетка.
        /// </summary>
        private static FixVec2 BossFloorPoint(LayoutMap map, FixVec2 center, Fix64 radius)
        {
            if (map.IsWalkable(center, radius) || map.Routes == null) return map.ClampToWalkable(center, radius);
            int best = -1;
            Fix64 nearest = Fix64.MaxValue;
            for (int i = 0; i < map.Routes.CellCount; i++)
            {
                FixVec2 cell = map.Routes.GetCell(i).Center;
                if (map.Routes.DistanceFromEntry(i) < 0 || !map.IsWalkable(cell, radius)) continue;
                Fix64 distance = FixVec2.DistanceSq(cell, center);
                if (distance >= nearest) continue;
                best = i; nearest = distance;
            }
            return best >= 0 ? map.Routes.GetCell(best).Center : map.ClampToWalkable(center, radius);
        }

        /// <summary>
        /// Встречи вдоль маршрута. Здоровье каждого моба — строка его вида в
        /// EnemyArchetypes × healthPercent уровня (EnemyHealth: 100 на первой
        /// арене, +7 за каждую следующую) × HealthPercent группы. Урон — строка
        /// вида × DamagePercent профиля (рост с глубиной) × DamagePercent
        /// группы. Проценты группы — подстройка около 100; элиты стоят на 100.
        /// </summary>
        public EncounterPlan SetupEncounters(LayoutMap map, ulong spawnSeed, int healthPercent, EncounterSettings settings, int entryClearance = 9)
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
            SetupRift(map, spawnSeed, 0, 0, 1);
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
                bool hasRanged = false;
                for (int g = 0; g < counts.Length; g++)
                    if (counts[g] > 0 && pack.GetGroup(g).Kind == EnemyKind.ForestBud) hasRanged = true;
                int first = Entities.Count;
                // Reserve cramped rooms for their elite first; other groups retain authored order.
                for (int pass = 0; pass < 2; pass++)
                    for (int g = 0; g < counts.Length; g++)
                    {
                        var group = pack.GetGroup(g);
                        if (group.Elite != (pass == 0)) continue;
                        for (int n = 0; n < counts[g]; n++)
                        {
                            // Место ищется под то тело, которое поставит Configure*:
                            // Камнекопыту и Вендиго чужие 0.85 малы или велики.
                            var radius = ArchetypeBodyRadius(group.Kind);
                            if (hasRanged)
                            {
                                // Стрелки занимают дальнюю сторону комнаты относительно входа пачки.
                                int parent = map.GetPlaced(site.Module).Parent;
                                var entry = parent >= 0 ? map.CenterOf(parent) : map.EntryPoint;
                                var away = (site.Center - entry).Normalized();
                                cells.Sort((a, b) => {
                                    int order = FixVec2.Dot(a - entry, away).Raw.CompareTo(FixVec2.Dot(b - entry, away).Raw);
                                    if (order == 0) order = a.X.Raw.CompareTo(b.X.Raw);
                                    order = order != 0 ? order : a.Y.Raw.CompareTo(b.Y.Raw);
                                    return group.Kind == EnemyKind.ForestBud ? order : -order;
                                });
                            }
                            if (!TakeEncounterPoint(map, site.Module, cells, radius, ref rng, out var spot)) { omitted++; continue; }
                            int health = EnemyArchetypes.ScaleHealth(ArchetypeHealth(group.Kind),
                                healthPercent, group.HealthPercent);
                            int id = Entities.Spawn(spot, health, Faction.Orvill);
                            ConfigureEnemy(id, group.Kind);
                            var sheet = Entities.Stats[id];
                            var damage = Entities.Damage[id] * Fix64.Ratio(group.DamagePercent * settings.DamagePercent, 10000);
                            sheet.SetBase(StatType.Damage, damage);
                            Entities.RefreshStats(id);
                            elite[id] = group.Elite;
                            if (group.Elite) Entities.XpReward[id] = Progression.EliteKillXp;
                            _events.Add(SimEvent.Spawn(id, spot));
                        }
                    }
                placed.Add(new EncounterPlacement(site.Role, site.Module, site.Branch, pack.Id, site.Center,
                    first, Entities.Count - first));
            }
            Grid.Rebuild(Entities);
            _eliteMask = elite;
            return new EncounterPlan(placed, elite, settings.FormationRadius, omitted);
        }

        /// <summary>
        /// Стенд одного вида для тестов и съёмки: герой и count врагов kind в
        /// шеренге поперёк линии «герой — враг» на distance от героя (0 — 6 м).
        /// Здоровье и урон — строка вида, выросшая до арены arena, с «Сложно»
        /// hardPercent — тем же путём, что у волн встречи (SpawnScaledEnemy).
        /// Враги сразу заметили героя и смотрят на него; перезарядки — какие
        /// ставит Configure* вида. Шипомёт и Вендиго помечены элитой.
        ///
        /// Без карты — пустое поле без стен; с картой — площадка стенда
        /// Плюй-плода (FindForestBudTestStage), а на тестовой поляне из одного
        /// входа без маршрута — её центр.
        /// </summary>
        public EncounterPlan SetupKindTestArena(EnemyKind kind, int count = 1, LayoutMap map = null, ulong seed = 0,
            int arena = 1, int hardPercent = 100, Fix64 distance = default)
        {
            if (!EnemyArchetypes.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (count < 1 || count >= Entities.Capacity) throw new ArgumentOutOfRangeException(nameof(count));
            if (hardPercent < 1) throw new ArgumentOutOfRangeException(nameof(hardPercent));
            if (distance.Raw <= 0) distance = Fix64.FromInt(6);
            if (map == null) SetupTestArena(0); else SetupRift(map, seed, 0, 0, 1);
            _campWalkMap = null; _events.Clear();
            FixVec2 hero, enemy; int module;
            if (map != null && map.PlacedCount == 1 && map.Routes == null)
            {
                // Тестовая поляна из одного входа (камни на линии): герой в её
                // центре, враг по +X — как на пустом поле, только со стенами.
                hero = map.CenterOf(0); enemy = hero + new FixVec2(Fix64.FromInt(6), Fix64.Zero); module = 0;
            }
            else FindForestBudTestStage(map, out hero, out enemy, out module);
            var direction = (enemy - hero).Normalized();
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 radius = ArchetypeBodyRadius(kind);
            enemy = hero + direction * distance;
            if (map != null) enemy = map.ClampToWalkable(enemy, radius);
            Entities.Position[PlayerId] = hero; Entities.Facing[PlayerId] = direction;

            var side = new FixVec2(-direction.Y, direction.X);
            Fix64 spacing = radius * 2 + Fix64.Ratio(1, 2);
            bool eliteKind = kind == EnemyKind.ForestWendigo || kind == EnemyKind.ForestThorncaster;
            var elite = new bool[Entities.Capacity];
            int healthPercent = EnemyArchetypes.DepthHealthPercent(arena);
            int damagePercent = EnemyArchetypes.DepthDamagePercent(arena);
            for (int n = 0; n < count; n++)
            {
                // Шеренга от середины: 0, +1, −1, +2, −2…
                int rank = (n + 1) / 2;
                if ((n & 1) == 0) rank = -rank;
                var point = enemy + side * (spacing * rank);
                if (map != null) point = map.ClampToWalkable(point, radius);
                int id = SpawnScaledEnemy(point, kind, healthPercent, damagePercent, hardPercent);
                var look = (hero - point).Normalized();
                if (look.LengthSq.Raw != 0) Entities.Facing[id] = look;
                Entities.Aggro[id] = true;
                if (eliteKind) { elite[id] = true; Entities.XpReward[id] = Progression.EliteKillXp; }
                _events.Add(SimEvent.Spawn(id, point));
            }
            _eliteMask = elite;
            Grid.Rebuild(Entities);
            return new EncounterPlan(new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.MainPath,
                module, -1, StableId.Of("encounter.forest-kind.test"), enemy, 1, count) }, elite, Fix64.FromInt(5), 0);
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
