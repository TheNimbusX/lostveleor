using System;

namespace Game.Sim
{
    public enum EncounterRole : byte { Introduction, MainPath, RewardBranch, ExitGuard }

    /// <summary>
    /// Группа одного вида в пачке. Здоровье и урон берутся из строки вида в
    /// EnemyArchetypes; HealthPercent и DamagePercent здесь — лишь подстройка
    /// около 100 (элиты — ровно 100: элита сильна своим видом, а не надбавкой).
    /// </summary>
    public readonly struct EncounterGroup
    {
        public readonly EnemyKind Kind;
        public readonly int Min, Max, HealthPercent, DamagePercent;
        public readonly bool Elite, GrowWithDepth;
        public EncounterGroup(EnemyKind kind, int min, int max, int healthPercent = 100,
            int damagePercent = 100, bool elite = false, bool growWithDepth = false)
        {
            // Любой вид из таблицы — с Камнекопытом. В пачки Meadow он пока
            // не вписан: это решит владелец после стадии 3 плана. Кроме
            // детёныша Расщепеня: он встаёт только из распада родителя.
            if (!EnemyArchetypes.IsPlaceable(kind) ||
                min < 0 || max < min || max > 16 || healthPercent < 1 || healthPercent > 1000 ||
                damagePercent < 1 || damagePercent > 1000)
                throw new ArgumentException("Invalid encounter enemy group.");
            Kind = kind; Min = min; Max = max; HealthPercent = healthPercent;
            DamagePercent = damagePercent; Elite = elite; GrowWithDepth = growWithDepth;
        }
    }

    public sealed class EncounterPack
    {
        /// <summary>Не больше двух лесных хранителей в одной пачке (решение владельца 26.09).</summary>
        public const int MaxGuardiansPerPack = 2;

        public readonly int Id, Weight;
        private readonly EncounterGroup[] _groups;
        public int GroupCount => _groups.Length;
        public EncounterGroup GetGroup(int index) => _groups[index];
        public EncounterPack(int id, int weight, EncounterGroup[] groups)
        {
            if (weight < 1 || weight > 10000 || groups == null || groups.Length == 0 || groups.Length > 8)
                throw new ArgumentException("A pack needs a positive weight and 1–8 groups.");
            int guaranteed = 0, guardians = 0;
            bool guardiansGrow = false;
            foreach (var g in groups)
            {
                if (g.HealthPercent <= 0 || g.DamagePercent <= 0) throw new ArgumentException("Uninitialized group.");
                guaranteed += g.Min;
                if (g.Kind == EnemyKind.ForestGuardian || g.Kind == EnemyKind.None)
                {
                    guardians += g.Max;
                    guardiansGrow |= g.GrowWithDepth;
                }
            }
            if (guaranteed == 0) throw new ArgumentException("A pack must guarantee at least one enemy.");
            // Правило владельца (26.09): в одной пачке не больше двух лесных хранителей —
            // и без роста с глубиной, иначе бонус поднял бы их выше двух.
            if (guardians > MaxGuardiansPerPack || (guardians > 0 && guardiansGrow))
                throw new ArgumentException("A pack may hold at most " + MaxGuardiansPerPack + " forest guardians.");
            Id = id; Weight = weight; _groups = (EncounterGroup[])groups.Clone();
        }
        /// <summary>
        /// Сколько мест в пуле сущностей может занять пачка: особи × тела на
        /// особь (Расщепень — сам и дети, EnemyArchetypes.BodiesPerSpawn).
        /// </summary>
        public int MaxEnemies(int bonus)
        {
            int count = 0;
            foreach (var g in _groups)
                count += (g.Max + (g.GrowWithDepth ? bonus : 0)) * EnemyArchetypes.BodiesPerSpawn(g.Kind);
            return count;
        }
    }

    /// <summary>Compiled per-level encounter balance. Contains no authoring or Unity objects.</summary>
    public sealed class EncounterSettings
    {
        private readonly EncounterPack[][] _packs;
        public readonly int MainCount, CountBonus, DamagePercent;
        public readonly Fix64 FormationRadius;
        public EncounterSettings(EncounterPack[] introduction, EncounterPack[] main, EncounterPack[] reward,
            EncounterPack[] exit, int mainCount, int countBonus, int damagePercent, Fix64 formationRadius)
        {
            if (mainCount < 1 || mainCount > 12 || countBonus < 0 || countBonus > 8 ||
                damagePercent < 1 || damagePercent > 1000 || formationRadius < Fix64.FromInt(2) ||
                formationRadius > Fix64.FromInt(12)) throw new ArgumentException("Invalid encounter progression.");
            // Правила «пачка выхода обязана держать элитного Хранителя» больше
            // нет (стадия 6 плана): в потоке арен пачки не выбираются вовсе —
            // встречу ставит шаблон (ForestEncounterTemplates), и элита там
            // одна — Вендиго элитной встречи. Пачки остаются маршрутным
            // уровням, и страж выхода в них — решение ассета, а не закон.
            _packs = new[] { Copy(introduction), Copy(main), Copy(reward), Copy(exit) };
            MainCount = mainCount; CountBonus = countBonus; DamagePercent = damagePercent;
            FormationRadius = formationRadius;
        }
        private static EncounterPack[] Copy(EncounterPack[] source)
        {
            if (source == null || source.Length == 0 || source.Length > 32)
                throw new ArgumentException("Supply 1–32 packs for each encounter role.");
            var result = (EncounterPack[])source.Clone();
            foreach (var p in result) if (p == null) throw new ArgumentException("Missing encounter pack.");
            Array.Sort(result, (a, b) => a.Id.CompareTo(b.Id));
            for (int i = 1; i < result.Length; i++)
                if (result[i].Id == result[i - 1].Id) throw new ArgumentException("Duplicate encounter pack key.");
            return result;
        }
        public EncounterPack Pick(EncounterRole role, ref Pcg32 rng)
        {
            var packs = _packs[(int)role];
            int total = 0;
            foreach (var p in packs) total += p.Weight;
            int roll = rng.NextInt(0, total);
            foreach (var p in packs) { roll -= p.Weight; if (roll < 0) return p; }
            return packs[packs.Length - 1];
        }
        private int MaxFor(EncounterRole role)
        {
            int count = 0;
            foreach (var p in _packs[(int)role]) count = Math.Max(count, p.MaxEnemies(CountBonus));
            return count;
        }
        public int CapacityNeeded(int exits, int branches)
            => 1 + MaxFor(EncounterRole.Introduction) + MainCount * MaxFor(EncounterRole.MainPath)
                + exits * MaxFor(EncounterRole.ExitGuard) + branches * MaxFor(EncounterRole.RewardBranch);
    }
}
