namespace Game.Sim
{
    /// <summary>Модуль, поставленный на карту.</summary>
    public readonly struct PlacedModule
    {
        public readonly int ModuleIndex;
        public readonly int Quarters;   // поворот, 0..3
        public readonly int OriginX;
        public readonly int OriginY;
        public readonly int Width;      // уже с учётом поворота
        public readonly int Height;

        /// <summary>К какому модулю пристыкован. −1 у входа.</summary>
        public readonly int Parent;

        public PlacedModule(int moduleIndex, int quarters, int originX, int originY,
            int width, int height, int parent)
        {
            ModuleIndex = moduleIndex;
            Quarters = quarters;
            OriginX = originX;
            OriginY = originY;
            Width = width;
            Height = height;
            Parent = parent;
        }

        public bool Overlaps(int x, int y, int w, int h)
            => OriginX < x + w && x < OriginX + Width &&
               OriginY < y + h && y < OriginY + Height;
    }

    /// <summary>Точка стыковки, ещё никем не занятая.</summary>
    public readonly struct OpenConnector
    {
        public readonly int Placement;      // индекс поставленного модуля
        public readonly int ConnectorIndex; // номер точки в определении модуля
        public readonly int WorldX;
        public readonly int WorldY;
        public readonly Direction Facing;

        public OpenConnector(int placement, int connectorIndex, int worldX, int worldY, Direction facing)
        {
            Placement = placement;
            ConnectorIndex = connectorIndex;
            WorldX = worldX;
            WorldY = worldY;
            Facing = facing;
        }
    }

    /// <summary>
    /// Собранная локация.
    ///
    /// ОДИН И ТОТ ЖЕ ТИП для комнаты Разлома и для ручной локации кампании.
    /// Генератор не имеет никаких привилегий: он вызывает ровно тот же
    /// TryPlace, что и человек, расставляющий модули руками. Именно поэтому
    /// карта не завязана на случайность — случайность снаружи, в том, кто
    /// решает, что и куда ставить.
    /// </summary>
    public sealed class LayoutMap
    {
        /// <summary>Сторона клетки в метрах. Отрисовка переводит клетки в мир по ней.</summary>
        public static readonly Fix64 CellSize = Fix64.FromInt(2);

        private readonly ModuleSet _modules;

        private readonly PlacedModule[] _placed;
        private int _placedCount;

        private readonly OpenConnector[] _open;
        private int _openCount;

        public int PlacedCount => _placedCount;
        public int OpenCount => _openCount;

        public PlacedModule GetPlaced(int index) => _placed[index];
        public OpenConnector GetOpen(int index) => _open[index];
        public ModuleSet Modules => _modules;

        public LayoutMap(ModuleSet modules, int maxModules = 64)
        {
            _modules = modules;
            _placed = new PlacedModule[maxModules];

            // Точек стыковки заведомо больше, чем модулей: с запасом.
            _open = new OpenConnector[maxModules * 8];
        }

        public void Clear()
        {
            _placedCount = 0;
            _openCount = 0;
        }

        /// <summary>Помещается ли модуль так, чтобы не задеть уже стоящие.</summary>
        public bool Fits(int moduleIndex, int quarters, int originX, int originY)
        {
            _modules.Get(moduleIndex).RotatedSize(quarters, out int w, out int h);

            // Перебор всех поставленных. Модулей десятки, и замер покажет,
            // когда это перестанет быть бесплатным; пока сетка тут была бы
            // сложностью без выигрыша.
            for (int i = 0; i < _placedCount; i++)
                if (_placed[i].Overlaps(originX, originY, w, h)) return false;

            return true;
        }

        /// <summary>
        /// Ставит модуль. Возвращает индекс размещения или −1, если не влез
        /// или кончилось место.
        ///
        /// Это ЕДИНСТВЕННЫЙ способ поставить модуль — и для генератора,
        /// и для ручной расстановки.
        /// </summary>
        public int TryPlace(int moduleIndex, int quarters, int originX, int originY, int parent = -1)
        {
            if (_placedCount >= _placed.Length) return -1;
            if (!Fits(moduleIndex, quarters, originX, originY)) return -1;

            ModuleDefinition module = _modules.Get(moduleIndex);
            module.RotatedSize(quarters, out int w, out int h);

            int placement = _placedCount++;
            _placed[placement] = new PlacedModule(moduleIndex, quarters, originX, originY, w, h, parent);

            // Точки стыковки нового модуля становятся открытыми — кроме тех,
            // что смотрят внутрь уже стоящего соседа: туда никто не влезет.
            for (int c = 0; c < module.ConnectorCount; c++)
            {
                ModuleConnector rotated = module.RotatedConnector(c, quarters);
                int wx = originX + rotated.X;
                int wy = originY + rotated.Y;

                if (_openCount >= _open.Length) break;
                _open[_openCount++] = new OpenConnector(placement, c, wx, wy, rotated.Facing);
            }

            return placement;
        }

        /// <summary>Убирает точку стыковки из открытых, сохраняя порядок остальных.</summary>
        public void CloseOpen(int index)
        {
            for (int i = index; i < _openCount - 1; i++) _open[i] = _open[i + 1];
            _openCount--;
        }

        /// <summary>
        /// Проверка связности: до всех ли модулей можно дойти от входа.
        ///
        /// Обход в ширину по дереву стыковок. Буфер очереди передаётся снаружи,
        /// чтобы проверка ничего не выделяла — её гоняют тысячами в тестах.
        /// </summary>
        public bool IsConnected(int[] queueScratch, bool[] visitedScratch)
        {
            if (_placedCount == 0) return true;

            for (int i = 0; i < _placedCount; i++) visitedScratch[i] = false;

            int head = 0, tail = 0;
            queueScratch[tail++] = 0;
            visitedScratch[0] = true;
            int seen = 1;

            while (head < tail)
            {
                int current = queueScratch[head++];

                // Обход по возрастанию индекса: детям текущего и его родителю.
                for (int i = 0; i < _placedCount; i++)
                {
                    if (visitedScratch[i]) continue;
                    if (_placed[i].Parent != current && _placed[current].Parent != i) continue;

                    visitedScratch[i] = true;
                    queueScratch[tail++] = i;
                    seen++;
                }
            }

            return seen == _placedCount;
        }

        /// <summary>Центр модуля в мировых координатах симуляции. Для расстановки врагов и отрисовки.</summary>
        public FixVec2 CenterOf(int placement)
        {
            PlacedModule p = _placed[placement];

            // Половина ширины в клетках — дробная величина, поэтому считаем
            // в удвоенных клетках и делим один раз: без промежуточного округления.
            Fix64 x = CellSize * Fix64.Ratio(2 * p.OriginX + p.Width, 2);
            Fix64 y = CellSize * Fix64.Ratio(2 * p.OriginY + p.Height, 2);
            return new FixVec2(x, y);
        }

        public ulong Hash()
        {
            ulong hash = Hashing.Offset;
            Hashing.Mix(ref hash, _placedCount);

            for (int i = 0; i < _placedCount; i++)
            {
                Hashing.Mix(ref hash, _modules.Get(_placed[i].ModuleIndex).Id);
                Hashing.Mix(ref hash, _placed[i].Quarters);
                Hashing.Mix(ref hash, _placed[i].OriginX);
                Hashing.Mix(ref hash, _placed[i].OriginY);
                Hashing.Mix(ref hash, _placed[i].Parent);
            }

            return hash;
        }
    }
}
