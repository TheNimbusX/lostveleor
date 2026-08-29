using System;

namespace Game.Sim
{
    /// <summary>
    /// Справочник баз и аффиксов в том виде, в каком его читает симуляция:
    /// плоские массивы, отсортированные по id, поиск двоичный.
    ///
    /// Заполняется ОДИН РАЗ при загрузке — из ScriptableObject-ов сборки
    /// Game.Data, из теста, из чего угодно. Сама симуляция про Unity не знает
    /// и знать не должна.
    ///
    /// Словаря здесь нет намеренно: порядок обхода Dictionary не гарантирован,
    /// а по аффиксам мы обходим ВЕСЬ массив при каждом ролле, и этот обход
    /// обязан быть в одном и том же порядке всегда.
    /// </summary>
    public sealed class ItemDatabase
    {
        private readonly ItemBaseDefinition[] _bases;
        private readonly AffixDefinition[] _affixes;

        public int BaseCount => _bases.Length;
        public int AffixCount => _affixes.Length;

        /// <summary>
        /// Массивы копируются и сортируются по id. Сортировка — это и есть
        /// канонический порядок: он не зависит ни от порядка файлов на диске,
        /// ни от того, в каком порядке Unity отдала ассеты.
        /// </summary>
        public ItemDatabase(ItemBaseDefinition[] bases, AffixDefinition[] affixes)
        {
            _bases = (ItemBaseDefinition[])bases.Clone();
            _affixes = (AffixDefinition[])affixes.Clone();

            Array.Sort(_bases, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(_affixes, (a, b) => a.Id.CompareTo(b.Id));

            RequireUniqueIds();
        }

        /// <summary>
        /// Одинаковые id — это либо опечатка в данных, либо коллизия хеша.
        /// И то и другое обязано валить загрузку с понятным текстом, а не
        /// всплывать через полгода как «предмет иногда не тот».
        /// </summary>
        private void RequireUniqueIds()
        {
            for (int i = 1; i < _bases.Length; i++)
                if (_bases[i].Id == _bases[i - 1].Id)
                    throw new InvalidOperationException($"Две базы с одним id: {_bases[i].Id}");

            for (int i = 1; i < _affixes.Length; i++)
                if (_affixes[i].Id == _affixes[i - 1].Id)
                    throw new InvalidOperationException($"Два аффикса с одним id: {_affixes[i].Id}");
        }

        public ItemBaseDefinition GetBase(int index) => _bases[index];
        public AffixDefinition GetAffix(int index) => _affixes[index];

        /// <summary>Индекс базы по id или -1. Двоичный поиск по отсортированному массиву.</summary>
        public int IndexOfBase(int id)
        {
            int lo = 0, hi = _bases.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int cmp = _bases[mid].Id.CompareTo(id);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        public int IndexOfAffix(int id)
        {
            int lo = 0, hi = _affixes.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int cmp = _affixes[mid].Id.CompareTo(id);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        /// <summary>Хеш содержимого. Позволяет проверить, что клиент и реплей собраны на одних данных.</summary>
        public ulong ContentHash()
        {
            ulong hash = Hashing.Offset;

            Hashing.Mix(ref hash, _bases.Length);
            for (int i = 0; i < _bases.Length; i++)
            {
                Hashing.Mix(ref hash, _bases[i].Id);
                Hashing.Mix(ref hash, (int)_bases[i].Category);
                Hashing.Mix(ref hash, _bases[i].ImplicitValue);
            }

            Hashing.Mix(ref hash, _affixes.Length);
            for (int i = 0; i < _affixes.Length; i++)
            {
                Hashing.Mix(ref hash, _affixes[i].Id);
                Hashing.Mix(ref hash, _affixes[i].Group);
                Hashing.Mix(ref hash, (int)_affixes[i].Stat);
                Hashing.Mix(ref hash, (int)_affixes[i].Op);
                Hashing.Mix(ref hash, _affixes[i].MinValue);
                Hashing.Mix(ref hash, _affixes[i].MaxValue);
                Hashing.Mix(ref hash, (int)_affixes[i].MinItemLevel);
                Hashing.Mix(ref hash, _affixes[i].Weight);
                Hashing.Mix(ref hash, (int)_affixes[i].AllowedCategories);
            }

            return hash;
        }
    }
}
