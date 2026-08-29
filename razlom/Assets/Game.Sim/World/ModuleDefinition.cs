namespace Game.Sim
{
    /// <summary>
    /// Стороны света на сетке локации. Ось X на восток, ось Y на север.
    /// Порядок значений задаёт поворот: +1 это поворот на 90° по часовой.
    /// </summary>
    public enum Direction : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public static class Directions
    {
        public static Direction Opposite(Direction d) => (Direction)(((int)d + 2) & 3);

        /// <summary>Поворот по часовой на quarters × 90°.</summary>
        public static Direction Rotate(Direction d, int quarters) => (Direction)(((int)d + quarters) & 3);

        public static void Step(Direction d, out int dx, out int dy)
        {
            switch (d)
            {
                case Direction.North: dx = 0; dy = 1; break;
                case Direction.East: dx = 1; dy = 0; break;
                case Direction.South: dx = 0; dy = -1; break;
                default: dx = -1; dy = 0; break;
            }
        }
    }

    /// <summary>
    /// Точка стыковки: клетка на краю модуля и направление НАРУЖУ.
    /// Два модуля стыкуются, когда их точки смотрят друг на друга
    /// и стоят в соседних клетках.
    /// </summary>
    public readonly struct ModuleConnector
    {
        public readonly int X;
        public readonly int Y;
        public readonly Direction Facing;

        public ModuleConnector(int x, int y, Direction facing)
        {
            X = x;
            Y = y;
            Facing = facing;
        }
    }

    /// <summary>
    /// Модуль — прямоугольный кусок локации с точками стыковки.
    ///
    /// НЕ ЗАВЯЗАН НА СЛУЧАЙНОСТЬ. Тем же типом данных собирается и комната
    /// Разлома по сиду, и ручная локация кампании: разница только в том, кто
    /// вызывает LayoutMap.TryPlace — генератор или человек в редакторе.
    ///
    /// Координаты целые. Дробных клеток не бывает, а целая арифметика
    /// детерминирована без оговорок про округление.
    /// </summary>
    public sealed class ModuleDefinition
    {
        public readonly int Id;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Вход в локацию. С такого модуля начинается сборка.</summary>
        public readonly bool IsEntrance;

        /// <summary>Вес во взвешенном выборе. Ноль — модуль не ставится генератором.</summary>
        public readonly int Weight;

        private readonly ModuleConnector[] _connectors;

        public int ConnectorCount => _connectors.Length;
        public ModuleConnector GetConnector(int index) => _connectors[index];

        public ModuleDefinition(string stableKey, int width, int height,
            ModuleConnector[] connectors, int weight = 100, bool isEntrance = false)
        {
            Id = StableId.Of(stableKey);
            Width = width;
            Height = height;
            _connectors = connectors;
            Weight = weight;
            IsEntrance = isEntrance;
        }

        /// <summary>Размер после поворота: на нечётных четвертях стороны меняются местами.</summary>
        public void RotatedSize(int quarters, out int width, out int height)
        {
            if ((quarters & 1) == 0) { width = Width; height = Height; }
            else { width = Height; height = Width; }
        }

        /// <summary>
        /// Клетка модуля после поворота на quarters × 90° по часовой.
        ///
        /// Один шаг по часовой при оси Y вверх: (x, y) → (y, W−1−x),
        /// размер (W, H) → (H, W). Применяется столько раз, сколько четвертей —
        /// цикл вместо четырёх формул, потому что ошибиться в одной из четырёх
        /// проще, чем в одной.
        /// </summary>
        public void RotateCell(int x, int y, int quarters, out int rx, out int ry)
        {
            int w = Width, h = Height;
            rx = x;
            ry = y;

            for (int q = 0; q < (quarters & 3); q++)
            {
                int nx = ry;
                int ny = w - 1 - rx;
                rx = nx;
                ry = ny;

                int nw = h;
                h = w;
                w = nw;
            }
        }

        /// <summary>Точка стыковки после поворота: и клетка, и направление.</summary>
        public ModuleConnector RotatedConnector(int index, int quarters)
        {
            ModuleConnector c = _connectors[index];
            RotateCell(c.X, c.Y, quarters, out int rx, out int ry);
            return new ModuleConnector(rx, ry, Directions.Rotate(c.Facing, quarters));
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Id);
            Hashing.Mix(ref hash, Width);
            Hashing.Mix(ref hash, Height);
            Hashing.Mix(ref hash, Weight);
            Hashing.Mix(ref hash, IsEntrance ? 1 : 0);
            Hashing.Mix(ref hash, _connectors.Length);

            for (int i = 0; i < _connectors.Length; i++)
            {
                Hashing.Mix(ref hash, _connectors[i].X);
                Hashing.Mix(ref hash, _connectors[i].Y);
                Hashing.Mix(ref hash, (int)_connectors[i].Facing);
            }
        }
    }
}
