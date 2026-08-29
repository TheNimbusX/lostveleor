using System;

namespace Game.Sim
{
    /// <summary>
    /// Двумерный вектор на Fix64. Изометрия плоская, третья координата симуляции не нужна:
    /// высота — забота отрисовки.
    /// </summary>
    public readonly struct FixVec2 : IEquatable<FixVec2>
    {
        public readonly Fix64 X;
        public readonly Fix64 Y;

        public FixVec2(Fix64 x, Fix64 y) { X = x; Y = y; }

        public static readonly FixVec2 Zero = new FixVec2(Fix64.Zero, Fix64.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator -(FixVec2 a) => new FixVec2(-a.X, -a.Y);
        public static FixVec2 operator *(FixVec2 a, Fix64 s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator *(Fix64 s, FixVec2 a) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix64 s) => new FixVec2(a.X / s, a.Y / s);

        public static Fix64 Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Квадрат длины. В сравнениях расстояний использовать только его — корень не нужен и дорог.</summary>
        public Fix64 LengthSq => X * X + Y * Y;

        public Fix64 Length => Fix64.Sqrt(LengthSq);

        public static Fix64 DistanceSq(FixVec2 a, FixVec2 b)
        {
            Fix64 dx = a.X - b.X;
            Fix64 dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static Fix64 Distance(FixVec2 a, FixVec2 b) => Fix64.Sqrt(DistanceSq(a, b));

        /// <summary>Единичный вектор. Нулевой вектор возвращается как есть.</summary>
        public FixVec2 Normalized()
        {
            Fix64 lenSq = LengthSq;
            if (lenSq.Raw == 0) return Zero;
            Fix64 len = Fix64.Sqrt(lenSq);
            return new FixVec2(X / len, Y / len);
        }

        /// <summary>Ограничить длину вектора. Полезно для скорости движения.</summary>
        public FixVec2 ClampLength(Fix64 maxLen)
        {
            Fix64 lenSq = LengthSq;
            if (lenSq <= maxLen * maxLen) return this;
            return Normalized() * maxLen;
        }

        /// <summary>
        /// Лежит ли направление toTarget в секторе вокруг facing с половинным
        /// углом, косинус которого равен minCos. Facing считается единичным.
        ///
        /// БЕЗ КОРНЯ. Условие cos(угла) ≥ minCos равносильно
        /// dot ≥ minCos * |toTarget|, а сравнение возводится в квадрат —
        /// извлекать корень на каждого кандидата в поиске цели было бы
        /// расточительно, и это ровно тот случай, когда квадраты уместны.
        ///
        /// Одна реализация на всех: и прямой перебор, и сетка обязаны отвечать
        /// побитово одинаково, иначе тест эквивалентности перестанет что-то значить.
        /// </summary>
        public static bool WithinArc(FixVec2 facing, FixVec2 toTarget, Fix64 minCos)
        {
            // Проверяем dot ≥ minCos * |toTarget|. Возводить в квадрат можно
            // только когда обе стороны одного знака, иначе знак потеряется —
            // поэтому случаи разобраны явно, а не одной формулой.
            Fix64 dot = Dot(facing, toTarget);
            Fix64 lhsSq = dot * dot;
            Fix64 rhsSq = minCos * minCos * toTarget.LengthSq;

            if (minCos.Raw >= 0)
            {
                // Сектор не шире полусферы: цель позади — сразу мимо.
                return dot.Raw >= 0 && lhsSq >= rhsSq;
            }

            // Сектор шире полусферы: всё, что впереди, подходит заведомо,
            // а позади — только пока не вышло за половину угла.
            return dot.Raw >= 0 || lhsSq <= rhsSq;
        }

        public static FixVec2 FromAngle(Fix64 radians)
            => new FixVec2(Fix64.Cos(radians), Fix64.Sin(radians));

        public Fix64 Angle => Fix64.Atan2(Y, X);

        public bool Equals(FixVec2 other) => X.Raw == other.X.Raw && Y.Raw == other.Y.Raw;
        public override bool Equals(object obj) => obj is FixVec2 v && Equals(v);
        public override int GetHashCode() => (X.Raw * 397).GetHashCode() ^ Y.Raw.GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }
}
