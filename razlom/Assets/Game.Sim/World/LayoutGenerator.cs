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
        private readonly int[] _leafIndex = new int[64];
        private readonly int[] _leafDepth = new int[64];

        /// <summary>
        /// Какие размещения — мостики, закрывшие петлю (см. CloseLoops).
        /// У мостика оба коннектора уже заняты, но ни в чьём Parent он не
        /// значится ничьим родителем — формально он выглядит тупиком для
        /// HasChild, и ChooseExits/ChooseRewardBranches обязаны его исключать,
        /// иначе награда или выход достанутся проходной комнате посреди пути.
        /// </summary>
        private readonly bool[] _isBridge = new bool[64];

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
        ///
        /// После роста — два прохода ПОСТ-обработки, оба на планировку уже не
        /// влияют (могут только добавить мостик между уже стоящими стенами):
        /// CloseLoops стягивает случайные тупики в петли, а ChooseExits и
        /// ChooseRewardBranches размечают, что из оставшихся тупиков — выход,
        /// а что — необязательная награда за отклонение от маршрута.
        /// </summary>
        public int Generate(ModuleSet modules, ulong seed, LayoutMap into,
            int targetModules, int exitCount = 1, int maxLoops = 1, int rewardBranchCount = 2)
        {
            into.Clear();
            System.Array.Clear(_isBridge, 0, _isBridge.Length);

            int entrance = modules.FindEntrance();
            if (entrance < 0) entrance = 0;
            if (modules.Count == 0) return 0;

            var rng = new Pcg32(seed, LayoutSequence);

            int entranceQuarters = rng.NextInt(0, 4);
            if (into.TryPlace(entrance, entranceQuarters, 0, 0) < 0) return 0;

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

            CloseLoops(modules, into, ref rng, maxLoops);
            ChooseExits(into, ref rng, exitCount);
            ChooseRewardBranches(into, ref rng, rewardBranchCount);
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
        /// <summary>
        /// Стягивает случайные пары ещё не закрытых тупиков в петли: если
        /// какой-то модуль своими двумя коннекторами одновременно дотягивается
        /// и до одного тупика, и до другого, он встаёт между ними, и дерево
        /// перестаёт быть деревом в этом месте.
        ///
        /// Пары выбираются случайно и ограничены maxLoops, а не «сколько
        /// нашлось»: если стягивать КАЖДУЮ подходящую пару, локация превратится
        /// в сплошную сетку и потеряет ощущение блуждания по ветвям, ради
        /// которого дерево вообще растили.
        ///
        /// Мостик получает случайного из двух концов родителем (для DepthOf/
        /// IsConnected — им достаточно одного пути), а сам помечается в
        /// _isBridge, чтобы ChooseExits/ChooseRewardBranches не приняли его,
        /// формально бездетного, за настоящий тупик.
        /// </summary>
        private void CloseLoops(ModuleSet modules, LayoutMap into, ref Pcg32 rng, int maxLoops)
        {
            if (maxLoops <= 0) return;

            int made = 0;
            int guard = into.OpenCount * into.OpenCount + 8;

            while (made < maxLoops && into.OpenCount > 1 && guard-- > 0)
            {
                int a = rng.NextInt(0, into.OpenCount);
                int b = rng.NextInt(0, into.OpenCount);
                if (a == b) continue;

                OpenConnector openA = into.GetOpen(a);
                OpenConnector openB = into.GetOpen(b);

                if (!TryFindBridge(modules, into, openA, openB,
                        out int module, out int quarters, out int originX, out int originY))
                    continue;

                // Больший индекс закрывается первым: CloseOpen сдвигает хвост
                // списка влево, и закрытие меньшего индекса первым испортило
                // бы ещё не прочитанный больший.
                int first = a > b ? a : b;
                int second = a > b ? b : a;
                into.CloseOpen(first);
                into.CloseOpen(second);

                int placement = into.TryPlace(module, quarters, originX, originY, openA.Placement);
                if (placement < 0) continue; // Fits уже проверен в TryFindBridge — сюда не дойдём

                if (placement < _isBridge.Length) _isBridge[placement] = true;
                made++;
            }
        }

        /// <summary>
        /// Есть ли модуль, который одним поворотом одновременно достаёт
        /// коннектором до openA и ДРУГИМ своим коннектором — до openB.
        ///
        /// Перебор строго по возрастанию: модуль, поворот, первая точка,
        /// вторая точка — тот же порядок, что и у CollectCandidates, той же
        /// причины ради: одна и та же карта у сида должна собираться
        /// одинаково всегда.
        /// </summary>
        private static bool TryFindBridge(ModuleSet modules, LayoutMap into,
            in OpenConnector openA, in OpenConnector openB,
            out int moduleIndex, out int quarters, out int originX, out int originY)
        {
            Directions.Step(openA.Facing, out int dxA, out int dyA);
            int targetAX = openA.WorldX + dxA;
            int targetAY = openA.WorldY + dyA;
            Direction neededA = Directions.Opposite(openA.Facing);

            Directions.Step(openB.Facing, out int dxB, out int dyB);
            int targetBX = openB.WorldX + dxB;
            int targetBY = openB.WorldY + dyB;
            Direction neededB = Directions.Opposite(openB.Facing);

            for (int m = 0; m < modules.Count; m++)
            {
                ModuleDefinition module = modules.Get(m);
                if (module.Weight <= 0 || module.IsEntrance) continue;
                if (module.ConnectorCount < 2) continue; // мостику нужны оба конца разом

                for (int q = 0; q < 4; q++)
                {
                    for (int c1 = 0; c1 < module.ConnectorCount; c1++)
                    {
                        ModuleConnector r1 = module.RotatedConnector(c1, q);
                        if (r1.Facing != neededA) continue;

                        int oX = targetAX - r1.X;
                        int oY = targetAY - r1.Y;
                        if (!into.Fits(m, q, oX, oY)) continue;

                        for (int c2 = 0; c2 < module.ConnectorCount; c2++)
                        {
                            if (c2 == c1) continue;
                            ModuleConnector r2 = module.RotatedConnector(c2, q);
                            if (r2.Facing != neededB) continue;
                            if (oX + r2.X != targetBX || oY + r2.Y != targetBY) continue;

                            moduleIndex = m;
                            quarters = q;
                            originX = oX;
                            originY = oY;
                            return true;
                        }
                    }
                }
            }

            moduleIndex = quarters = originX = originY = 0;
            return false;
        }

        /// <summary>
        /// Выходы — тупики в дальней половине дерева. Рядом со входом выход
        /// обессмыслил бы локацию: игрок ушёл бы, не увидев её.
        ///
        /// Вызывается ПОСЛЕ сборки и ПОСЛЕ CloseLoops, поэтому на саму
        /// планировку не влияет, а мостики CloseLoops уже не спутает с
        /// тупиками — см. фильтр _isBridge ниже.
        /// </summary>
        private void ChooseExits(LayoutMap map, ref Pcg32 rng, int wanted)
        {
            if (wanted <= 0 || map.PlacedCount <= 1) return;

            int leaves = 0;
            int deepest = 0;

            for (int i = 1; i < map.PlacedCount && leaves < _leafIndex.Length; i++)
            {
                if (map.HasChild(i)) continue;
                if (i < _isBridge.Length && _isBridge[i]) continue;

                int depth = map.DepthOf(i);
                _leafIndex[leaves] = i;
                _leafDepth[leaves] = depth;
                if (depth > deepest) deepest = depth;
                leaves++;
            }

            if (leaves == 0) return;

            // Отбор дальних. Компактим на месте: eligible всегда не больше i,
            // поэтому запись не затирает то, что ещё не прочитано.
            int threshold = deepest / 2 + 1;
            int eligible = 0;
            for (int i = 0; i < leaves; i++)
                if (_leafDepth[i] >= threshold) _leafIndex[eligible++] = _leafIndex[i];

            // Дерево вышло плоским — берём любые тупики, лишь бы выход был.
            if (eligible == 0) eligible = leaves;

            int take = wanted < eligible ? wanted : eligible;
            for (int i = 0; i < take; i++)
            {
                int j = i + rng.NextInt(0, eligible - i);
                int swap = _leafIndex[i];
                _leafIndex[i] = _leafIndex[j];
                _leafIndex[j] = swap;

                map.AddExit(_leafIndex[i]);
            }
        }

        /// <summary>
        /// Необязательные ответвления с наградой: тупики, которые НЕ выход и
        /// не мостик. В отличие от выхода, порог глубины не нужен — сундук у
        /// самого входа такой же законный повод свернуть, как и дальний.
        ///
        /// Что именно кладётся в такую комнату (сундук, элитный моб) решает
        /// вызывающий: здесь только разметка планировки.
        /// </summary>
        private void ChooseRewardBranches(LayoutMap map, ref Pcg32 rng, int wanted)
        {
            if (wanted <= 0 || map.PlacedCount <= 1) return;

            int leaves = 0;
            for (int i = 1; i < map.PlacedCount && leaves < _leafIndex.Length; i++)
            {
                if (map.HasChild(i)) continue;
                if (i < _isBridge.Length && _isBridge[i]) continue;
                if (map.IsExit(i)) continue;

                _leafIndex[leaves++] = i;
            }

            if (leaves == 0) return;

            int take = wanted < leaves ? wanted : leaves;
            for (int i = 0; i < take; i++)
            {
                int j = i + rng.NextInt(0, leaves - i);
                int swap = _leafIndex[i];
                _leafIndex[i] = _leafIndex[j];
                _leafIndex[j] = swap;

                map.AddRewardBranch(_leafIndex[i]);
            }
        }
    }
}
    

