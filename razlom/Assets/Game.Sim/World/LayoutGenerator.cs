namespace Game.Sim
{
    /// <summary>
    /// Сборка комнаты Разлома из модулей по сиду.
    ///
    /// ЧИСТАЯ ФУНКЦИЯ ОТ (сид + набор модулей) — ровно как у предметов.
    /// Генератор берёт свой локальный Pcg32, засеянный сидом карты, и потоков
    /// забега не касается. Поэтому карту можно пересобрать когда угодно,
    /// зная только сид: при загрузке сейва, на сервере, в отчёте об ошибке.
    ///
    /// Сам сид берётся из потока Rng.Layout при входе в Разлом — см. RollSeed.
    ///
    /// Экземпляр держит буферы и переиспользуется: сборка не должна мусорить.
    /// </summary>
    public sealed class LayoutGenerator
    {
        /// <summary>
        /// Номер последовательности локального генератора. Меняя её,
        /// вы меняете КАЖДУЮ карту у КАЖДОГО игрока.
        /// </summary>
        private const ulong LayoutSequence = 0x5DEECE66DUL;

        /// <summary>Сколько вариантов размещения рассматривается за один шаг.</summary>
        private const int MaxCandidates = 256;

        private readonly int[] _candidateModule = new int[MaxCandidates];
        private readonly int[] _candidateQuarters = new int[MaxCandidates];
        private readonly int[] _candidateOriginX = new int[MaxCandidates];
        private readonly int[] _candidateOriginY = new int[MaxCandidates];
        private readonly int[] _candidateWeight = new int[MaxCandidates];

        /// <summary>Достаёт сид карты из потока Layout. Единственное место расхода потока.</summary>
        public static ulong RollSeed(ref Pcg32 layoutStream)
        {
            ulong high = layoutStream.NextUInt();
            ulong low = layoutStream.NextUInt();
            return (high << 32) | low;
        }

        /// <summary>
        /// Собирает карту в переданную LayoutMap.
        /// Возвращает количество поставленных модулей.
        ///
        /// Алгоритм: от входа наращиваем дерево. На каждом шаге берём одну
        /// открытую точку стыковки и пробуем пристыковать к ней модуль.
        /// Дерево связно по построению — до любой комнаты есть путь от входа,
        /// и это свойство алгоритма, а не удача сида.
        /// </summary>
        public int Generate(ModuleSet modules, ulong seed, LayoutMap into, int targetModules)
        {
            into.Clear();

            int entrance = modules.FindEntrance();
            if (entrance < 0) entrance = 0;
            if (modules.Count == 0) return 0;

            var rng = new Pcg32(seed, LayoutSequence);

            if (into.TryPlace(entrance, 0, 0, 0) < 0) return 0;

            while (into.PlacedCount < targetModules && into.OpenCount > 0)
            {
                int openIndex = rng.NextInt(0, into.OpenCount);
                OpenConnector open = into.GetOpen(openIndex);

                int candidates = CollectCandidates(modules, into, open);
                if (candidates == 0)
                {
                    // К этой точке ничего не приставить: она становится стеной.
                    into.CloseOpen(openIndex);
                    continue;
                }

                int chosen = PickWeighted(candidates, ref rng);

                // Точка закрывается ДО постановки: иначе новый модуль добавит
                // свои точки, индексы поедут, и закрывать пришлось бы уже не ту.
                into.CloseOpen(openIndex);

                int placement = into.TryPlace(_candidateModule[chosen], _candidateQuarters[chosen],
                    _candidateOriginX[chosen], _candidateOriginY[chosen], open.Placement);

                // Точка стыковки нового модуля, которой он прирос, тоже занята.
                if (placement >= 0) CloseFacing(into, open);
            }

            return into.PlacedCount;
        }

        /// <summary>
        /// Все способы пристыковать какой-нибудь модуль к данной точке.
        ///
        /// Перебор строго по возрастанию: модуль, поворот, номер точки.
        /// От этого порядка зависит, какой вариант достанется какому броску,
        /// и он обязан быть одним и тем же всегда.
        /// </summary>
        private int CollectCandidates(ModuleSet modules, LayoutMap map, in OpenConnector open)
        {
            Directions.Step(open.Facing, out int dx, out int dy);
            int targetX = open.WorldX + dx;
            int targetY = open.WorldY + dy;
            Direction needed = Directions.Opposite(open.Facing);

            int count = 0;

            for (int m = 0; m < modules.Count; m++)
            {
                ModuleDefinition module = modules.Get(m);
                if (module.Weight <= 0) continue;
                if (module.IsEntrance) continue; // вход в локации один

                for (int q = 0; q < 4; q++)
                {
                    for (int c = 0; c < module.ConnectorCount; c++)
                    {
                        ModuleConnector rotated = module.RotatedConnector(c, q);
                        if (rotated.Facing != needed) continue;

                        int originX = targetX - rotated.X;
                        int originY = targetY - rotated.Y;
                        if (!map.Fits(m, q, originX, originY)) continue;

                        if (count >= MaxCandidates) return count;

                        _candidateModule[count] = m;
                        _candidateQuarters[count] = q;
                        _candidateOriginX[count] = originX;
                        _candidateOriginY[count] = originY;
                        _candidateWeight[count] = module.Weight;
                        count++;
                    }
                }
            }

            return count;
        }

        private int PickWeighted(int count, ref Pcg32 rng)
        {
            int total = 0;
            for (int i = 0; i < count; i++) total += _candidateWeight[i];
            if (total <= 0) return 0;

            int roll = rng.NextInt(0, total);
            for (int i = 0; i < count; i++)
            {
                roll -= _candidateWeight[i];
                if (roll < 0) return i;
            }

            return count - 1;
        }

        /// <summary>
        /// Закрывает встречную точку стыковки нового модуля — ту, которой он
        /// прирос к соседу. Без этого генератор попробовал бы пристыковать
        /// к ней ещё один модуль и наложил бы его на родителя.
        /// </summary>
        private static void CloseFacing(LayoutMap map, in OpenConnector open)
        {
            Directions.Step(open.Facing, out int dx, out int dy);
            int targetX = open.WorldX + dx;
            int targetY = open.WorldY + dy;
            Direction needed = Directions.Opposite(open.Facing);

            for (int i = 0; i < map.OpenCount; i++)
            {
                OpenConnector candidate = map.GetOpen(i);
                if (candidate.WorldX != targetX || candidate.WorldY != targetY) continue;
                if (candidate.Facing != needed) continue;

                map.CloseOpen(i);
                return;
            }
        }
    }
}
