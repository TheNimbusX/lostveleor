using System;

namespace Game.Sim
{
    /// <summary>
    /// Детерминированное число с фиксированной точкой Q31.32 на long.
    /// Вся арифметика целочисленная, поэтому результат побитово одинаков
    /// на любой машине, при любом компиляторе и любых настройках оптимизации.
    ///
    /// ПРАВИЛО: внутри Game.Sim никогда не использовать float и double.
    /// FromDouble и ToDouble существуют только для редактора, тестов и отрисовки.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionalBits = 32;
        private const long OneRaw = 1L << FractionalBits;

        public readonly long Raw;

        private Fix64(long raw) { Raw = raw; }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);
        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        /// <summary>Дробь a/b. Считается на целых, поэтому безопасна для симуляции.</summary>
        public static Fix64 Ratio(int numerator, int denominator)
            => new Fix64(((long)numerator << FractionalBits) / denominator);

        /// <summary>ТОЛЬКО для редактора, тестов и импорта данных. В симуляции не вызывать.</summary>
        public static Fix64 FromDouble(double value) => new Fix64((long)(value * OneRaw));

        /// <summary>ТОЛЬКО для отрисовки и отладки.</summary>
        public double ToDouble() => (double)Raw / OneRaw;
        public float ToFloat() => (float)((double)Raw / OneRaw);
        public int ToInt() => (int)(Raw >> FractionalBits);

        public static readonly Fix64 Zero     = new Fix64(0);
        public static readonly Fix64 One      = new Fix64(OneRaw);
        public static readonly Fix64 Half     = new Fix64(OneRaw >> 1);
        public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
        public static readonly Fix64 MinValue = new Fix64(long.MinValue);

        public static readonly Fix64 Pi      = new Fix64(13493037705L);
        public static readonly Fix64 TwoPi   = new Fix64(26986075410L);
        public static readonly Fix64 PiOver2 = new Fix64(6746518852L);

        // ---------- сложение и вычитание ----------

        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.Raw + b.Raw);
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.Raw - b.Raw);
        public static Fix64 operator -(Fix64 a) => new Fix64(-a.Raw);

        // ---------- умножение ----------
        // Раскладываем оба операнда на старшие и младшие 32 бита и собираем
        // 128-битное произведение вручную. Переполнение оборачивается предсказуемо.

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            long xl = a.Raw, yl = b.Raw;

            ulong xlo = (ulong)xl & 0xFFFFFFFFUL;
            long  xhi = xl >> FractionalBits;
            ulong ylo = (ulong)yl & 0xFFFFFFFFUL;
            long  yhi = yl >> FractionalBits;

            ulong lolo = xlo * ylo;
            long  lohi = (long)xlo * yhi;
            long  hilo = xhi * (long)ylo;
            long  hihi = xhi * yhi;

            long loResult = (long)(lolo >> FractionalBits);
            long hiResult = hihi << FractionalBits;

            return new Fix64(loResult + lohi + hilo + hiResult);
        }

        public static Fix64 operator *(Fix64 a, int b) => new Fix64(a.Raw * b);
        public static Fix64 operator *(int a, Fix64 b) => new Fix64(b.Raw * a);

        // ---------- деление ----------
        // Побитовое восстанавливающее деление: без 128-битных типов и без плавающей точки.

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            long xl = a.Raw, yl = b.Raw;
            if (yl == 0) throw new DivideByZeroException("Fix64: деление на ноль");

            ulong remainder = (ulong)(xl >= 0 ? xl : -xl);
            ulong divider   = (ulong)(yl >= 0 ? yl : -yl);
            ulong quotient  = 0UL;
            int bitPos = FractionalBits + 1;

            while ((divider & 0xF) == 0 && bitPos >= 4) { divider >>= 4; bitPos -= 4; }

            while (remainder != 0 && bitPos >= 0)
            {
                int shift = CountLeadingZeroes(remainder);
                if (shift > bitPos) shift = bitPos;
                remainder <<= shift;
                bitPos -= shift;

                ulong div = remainder / divider;
                remainder = remainder % divider;
                quotient += div << bitPos;

                if (bitPos > 0 && (div & ~(0xFFFFFFFFFFFFFFFFUL >> bitPos)) != 0)
                    return ((xl ^ yl) & long.MinValue) == 0 ? MaxValue : MinValue;

                remainder <<= 1;
                --bitPos;
            }

            ++quotient;
            long result = (long)(quotient >> 1);
            if (((xl ^ yl) & long.MinValue) != 0) result = -result;
            return new Fix64(result);
        }

        public static Fix64 operator /(Fix64 a, int b) => new Fix64(a.Raw / b);

        private static int CountLeadingZeroes(ulong x)
        {
            int result = 0;
            while ((x & 0xF000000000000000UL) == 0) { result += 4; x <<= 4; }
            while ((x & 0x8000000000000000UL) == 0) { result += 1; x <<= 1; }
            return result;
        }

        // ---------- вспомогательное ----------

        public static Fix64 Abs(Fix64 v) => v.Raw < 0 ? new Fix64(-v.Raw) : v;
        public static int Sign(Fix64 v) => v.Raw < 0 ? -1 : (v.Raw > 0 ? 1 : 0);
        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;
        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;

        public static Fix64 Clamp(Fix64 v, Fix64 lo, Fix64 hi)
            => v.Raw < lo.Raw ? lo : (v.Raw > hi.Raw ? hi : v);

        /// <summary>Квадратный корень. Побитовый алгоритм, полностью целочисленный.</summary>
        public static Fix64 Sqrt(Fix64 x)
        {
            long xl = x.Raw;
            if (xl < 0) throw new ArgumentOutOfRangeException(nameof(x), "Fix64.Sqrt из отрицательного");

            ulong num = (ulong)xl;
            ulong result = 0UL;
            ulong bit = 1UL << 62;

            while (bit > num) bit >>= 2;

            for (int i = 0; i < 2; ++i)
            {
                while (bit != 0)
                {
                    if (num >= result + bit) { num -= result + bit; result = (result >> 1) + bit; }
                    else                     { result = result >> 1; }
                    bit >>= 2;
                }

                if (i == 0)
                {
                    if (num > (1UL << FractionalBits) - 1)
                    {
                        num -= result;
                        num = (num << FractionalBits) - 0x80000000UL;
                        result = (result << FractionalBits) + 0x80000000UL;
                    }
                    else
                    {
                        num <<= FractionalBits;
                        result <<= FractionalBits;
                    }
                    bit = 1UL << (FractionalBits - 2);
                }
            }
            return new Fix64((long)result);
        }

        // Тригонометрия полиномами Тейлора, только на целых операциях,
        // поэтому детерминирована по построению. Точность около 3e-6.
        //
        // ВАЖНО: результаты приближённые. Никогда не сравнивать их на точное равенство.
        // Синус и косинус считаются РАЗДЕЛЬНО, а не друг через друга: иначе ошибка
        // полинома на пике переносится в ноль второй функции, и cos(0) перестаёт
        // быть единицей — а на это опирается любая работа с направлениями.

        // sin(x) = x*(1 - t*(1/6 - t*(1/120 - t*(1/5040 - t/362880)))), t = x²
        private static readonly Fix64 S1 = new Fix64(715827883L); // 1/6
        private static readonly Fix64 S2 = new Fix64(35791394L);  // 1/120
        private static readonly Fix64 S3 = new Fix64(852135L);    // 1/5040
        private static readonly Fix64 S4 = new Fix64(11836L);     // 1/362880

        // cos(x) = 1 - t*(1/2 - t*(1/24 - t*(1/720 - t/40320))), t = x²
        private static readonly Fix64 K1 = new Fix64(2147483648L); // 1/2
        private static readonly Fix64 K2 = new Fix64(178956971L);  // 1/24
        private static readonly Fix64 K3 = new Fix64(5965232L);    // 1/720
        private static readonly Fix64 K4 = new Fix64(106522L);     // 1/40320

        /// <summary>Приводит угол в [-Pi/2, Pi/2]. Возвращает true, если знак результата нужно инвертировать.</summary>
        private static long ReduceToQuarter(long raw, out bool flipSign)
        {
            raw %= TwoPi.Raw;
            if (raw > Pi.Raw) raw -= TwoPi.Raw;
            else if (raw < -Pi.Raw) raw += TwoPi.Raw;

            flipSign = false;
            if (raw > PiOver2.Raw) { raw = Pi.Raw - raw; flipSign = true; }
            else if (raw < -PiOver2.Raw) { raw = -Pi.Raw - raw; flipSign = true; }
            return raw;
        }

        public static Fix64 Sin(Fix64 x)
        {
            // Для синуса отражение вокруг Pi/2 знак НЕ меняет.
            long raw = ReduceToQuarter(x.Raw, out _);

            Fix64 a = new Fix64(raw);
            Fix64 t = a * a;
            Fix64 inner = S3 - t * S4;
            inner = S2 - t * inner;
            inner = S1 - t * inner;
            return a * (One - t * inner);
        }

        public static Fix64 Cos(Fix64 x)
        {
            // Для косинуса отражение вокруг Pi/2 знак меняет.
            long raw = ReduceToQuarter(x.Raw, out bool flip);

            Fix64 a = new Fix64(raw);
            Fix64 t = a * a;
            Fix64 inner = K3 - t * K4;
            inner = K2 - t * inner;
            inner = K1 - t * inner;
            Fix64 result = One - t * inner;
            return flip ? -result : result;
        }

        /// <summary>Угол вектора. Точность около 0.005 рад — достаточно для поворота персонажа.</summary>
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            if (x.Raw == 0 && y.Raw == 0) return Zero;

            Fix64 k = new Fix64(1202590843L); // 0.28
            Fix64 angle;

            if (Abs(x).Raw >= Abs(y).Raw)
            {
                Fix64 z = y / x;
                angle = z / (One + k * z * z);
                if (x.Raw < 0) angle = y.Raw < 0 ? angle - Pi : angle + Pi;
            }
            else
            {
                Fix64 z = x / y;
                angle = PiOver2 - z / (One + k * z * z);
                if (y.Raw < 0) angle -= Pi;
            }
            return angle;
        }

        // ---------- сравнения ----------

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
        public static bool operator < (Fix64 a, Fix64 b) => a.Raw <  b.Raw;
        public static bool operator > (Fix64 a, Fix64 b) => a.Raw >  b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix64 f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToDouble().ToString("0.#####");
    }
}
