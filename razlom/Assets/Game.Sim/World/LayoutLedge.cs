namespace Game.Sim
{
    /// <summary>
    /// Уступ между сегментами арены (владелец, 2 октября): поперёк прохода лежит линия обрыва, за ней
    /// следующий сегмент ниже на Drop метров. В проходе — склон, по нему ходят в обе стороны
    /// (владелец: «сделай возможность подняться обратно»); по бокам обрыв закрывают скалы.
    ///
    /// Симуляция плоская и уступа не замечает: высота — только у представления (LayoutView).
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

        public void MixHash(ref ulong hash)
        {
            Hashing.Mix(ref hash, Point.X); Hashing.Mix(ref hash, Point.Y);
            Hashing.Mix(ref hash, Down.X); Hashing.Mix(ref hash, Down.Y);
            Hashing.Mix(ref hash, HalfWidth); Hashing.Mix(ref hash, Drop);
        }
    }
}
