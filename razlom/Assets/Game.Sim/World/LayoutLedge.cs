namespace Game.Sim
{
    /// <summary>
    /// Уступ между сегментами арены (владелец, 2 октября): поперёк прохода лежит линия обрыва, за ней
    /// следующий сегмент ниже на Drop метров. Сверху её переходят (спрыгивают), снизу — нет: путь,
    /// пересекающий линию против Down, симуляция не пускает. Это одна и та же проверка для героя,
    /// врагов, расталкивания и лучей стрелков (LayoutMap.CanTravel).
    ///
    /// Сама симуляция плоская: высота — только у представления (LayoutView), уступ для неё — стенка
    /// с одной стороны.
    /// </summary>
    public readonly struct LayoutLedge
    {
        /// <summary>Середина линии обрыва.</summary>
        public readonly FixVec2 Point;
        /// <summary>Единичное направление вниз — к следующему сегменту.</summary>
        public readonly FixVec2 Down;
        /// <summary>Полуширина обрыва вдоль линии, метры: шире прохода, чтобы край не обойти.</summary>
        public readonly Fix64 HalfWidth;
        /// <summary>Высота обрыва, метры. Только для представления.</summary>
        public readonly Fix64 Drop;

        public LayoutLedge(FixVec2 point, FixVec2 down, Fix64 halfWidth, Fix64 drop)
        { Point = point; Down = down; HalfWidth = halfWidth; Drop = drop; }

        /// <summary>Знаковое расстояние до линии: меньше нуля — сверху, больше — снизу.</summary>
        public Fix64 Side(FixVec2 point) => FixVec2.Dot(point - Point, Down);

        /// <summary>Расстояние вдоль линии от её середины.</summary>
        public Fix64 Along(FixVec2 point) => FixVec2.Dot(point - Point, new FixVec2(-Down.Y, Down.X));

        /// <summary>
        /// Путь from → to лезет на уступ снизу: начинается не выше линии (снизу или на ней), кончается
        /// сверху, и пересекает линию в её ширине.
        /// </summary>
        public bool BlocksClimb(FixVec2 from, FixVec2 to)
        {
            Fix64 a = Side(from), b = Side(to);
            if (a.Raw < 0 || b.Raw >= 0) return false;
            // Точка пересечения линии: from + (to − from)·a/(a − b).
            FixVec2 cross = from + (to - from) * (a / (a - b));
            return Fix64.Abs(Along(cross)) <= HalfWidth;
        }

        public void MixHash(ref ulong hash)
        {
            Hashing.Mix(ref hash, Point.X); Hashing.Mix(ref hash, Point.Y);
            Hashing.Mix(ref hash, Down.X); Hashing.Mix(ref hash, Down.Y);
            Hashing.Mix(ref hash, HalfWidth); Hashing.Mix(ref hash, Drop);
        }
    }
}
