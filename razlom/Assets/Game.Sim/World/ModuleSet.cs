using System;

namespace Game.Sim
{
    /// <summary>
    /// Набор модулей, отсортированный по id.
    ///
    /// Сортировка — это и есть канонический порядок перебора при сборке.
    /// Порядок, в котором модули пришли из данных, зависит от имён файлов
    /// и от того, как их отдала Unity, и влиять на карту не должен.
    /// </summary>
    public sealed class ModuleSet
    {
        private readonly ModuleDefinition[] _modules;

        public int Count => _modules.Length;
        public ModuleDefinition Get(int index) => _modules[index];

        public ModuleSet(ModuleDefinition[] modules)
        {
            _modules = (ModuleDefinition[])modules.Clone();
            Array.Sort(_modules, (a, b) => a.Id.CompareTo(b.Id));

            for (int i = 1; i < _modules.Length; i++)
                if (_modules[i].Id == _modules[i - 1].Id)
                    throw new InvalidOperationException($"Два модуля с одним id: {_modules[i].Id}");
        }

        /// <summary>Первый модуль-вход или -1. Перебор по возрастанию id, поэтому выбор устойчив.</summary>
        public int FindEntrance()
        {
            for (int i = 0; i < _modules.Length; i++)
                if (_modules[i].IsEntrance) return i;
            return -1;
        }

        public int IndexOf(int id)
        {
            int lo = 0, hi = _modules.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int cmp = _modules[mid].Id.CompareTo(id);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        /// <summary>Хеш содержимого: позволяет убедиться, что карта собрана на тех же данных.</summary>
        public ulong ContentHash()
        {
            ulong hash = Hashing.Offset;
            Hashing.Mix(ref hash, _modules.Length);
            for (int i = 0; i < _modules.Length; i++) _modules[i].HashInto(ref hash);
            return hash;
        }
    }
}
