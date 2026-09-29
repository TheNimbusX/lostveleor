using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// БЫСТРЫЕ ДЕЛЕНИЕ И КОРЕНЬ Fix64 (поток D плана «Мобы леса v2»: починка
    /// просадки на 48 мобах). Деление и корень ускорены новым алгоритмом, но
    /// обязаны отвечать бит в бит как прежние побитовые: от них зависит каждый
    /// шаг боя и каждый хэш. Эталон — копия прежних алгоритмов ниже. Проверка —
    /// все порядки величин обоих знаков, целые делители, края (0, ±1, 2^k±1,
    /// MinValue, MaxValue), точные квадраты и их соседи. Упал — быстрый путь
    /// разошёлся с прежним ответом: чинить быстрый путь, а не эталон.
    /// </summary>
    public sealed class Fix64FastMathTests
    {
        private const int FractionalBits = 32;

        [Test]
        public void Division_MatchesBitwiseReference_AllMagnitudesAndSigns()
        {
            var rng = new XorShift(0x9E3779B97F4A7C15UL);
            int checkedPairs = 0;
            long[] special =
            {
                0, 1, -1, 2, -2, 3, 1L << 31, (1L << 31) - 1, 1L << 32, -(1L << 32), (1L << 32) + 1, (1L << 32) - 1, 1L << 33,
                1L << 47, 1L << 48, 1L << 61, 1L << 62, long.MaxValue, long.MinValue, long.MinValue + 1, long.MaxValue - 1,
                3L << 31, 100L << 32, (1L << 32) / 3, 715827883L,
            };
            foreach (long x in special)
                foreach (long y in special)
                {
                    if (y == 0) continue;
                    AssertDivision(x, y);
                    checkedPairs++;
                }
            for (int bitsX = 0; bitsX <= 63; bitsX++)
                for (int bitsY = 1; bitsY <= 63; bitsY++)
                    for (int k = 0; k < 24; k++)
                    {
                        long x = rng.Raw(bitsX), y = rng.Raw(bitsY);
                        if ((k & 1) == 1) x = -x;
                        if ((k & 2) == 2) y = -y;
                        if ((k & 12) == 4) y = (y >> FractionalBits) << FractionalBits; // целые делители
                        if ((k & 12) == 8) x = (x >> FractionalBits) << FractionalBits; // целые делимые
                        if (y == 0) y = 1;
                        AssertDivision(x, y);
                        checkedPairs++;
                    }
            // Типичное для боя: координаты и длины до сотни метров.
            for (int k = 0; k < 20000; k++)
            {
                long x = (long)(rng.Next() % (200UL << FractionalBits)) - (100L << FractionalBits);
                long y = (long)(rng.Next() % (100UL << FractionalBits)) + 1;
                AssertDivision(x, (k & 1) == 0 ? y : -y);
                checkedPairs++;
            }
            Assert.That(checkedPairs, Is.GreaterThan(100000));
        }

        [Test]
        public void Sqrt_MatchesBitwiseReference_AllMagnitudesAndSquares()
        {
            var rng = new XorShift(0xD1B54A32D192ED03UL);
            for (int bits = 0; bits <= 63; bits++)
                for (int k = 0; k < 800; k++)
                {
                    long x = k < 8 ? (bits == 0 ? k : (1L << (bits - 1)) + (k - 4)) : rng.Raw(bits);
                    if (x < 0) continue;
                    AssertSqrt(x);
                }
            AssertSqrt(long.MaxValue);
            AssertSqrt(long.MaxValue - 1);
            // Точные квадраты (и в сыром виде, и сдвинутые на дробь) и их соседи:
            // здесь оценка дробной части ошибается чаще всего.
            for (long s = 0; s < 70000; s++)
            {
                long square = s * s * 31;
                AssertSqrt(square); AssertSqrt(square + 1); if (square > 0) AssertSqrt(square - 1);
                long shifted = (s * s) << 4;
                AssertSqrt(shifted); AssertSqrt(shifted + 1); if (shifted > 0) AssertSqrt(shifted - 1);
            }
            for (long v = 1; v < 2000; v++)
            {
                long whole = (v * v) << FractionalBits;
                AssertSqrt(whole); AssertSqrt(whole - 1); AssertSqrt(whole + 1);
            }
        }

        private static void AssertDivision(long x, long y)
        {
            long expected = ReferenceDivide(x, y);
            long actual = (Fix64.FromRaw(x) / Fix64.FromRaw(y)).Raw;
            if (actual != expected)
                Assert.Fail("деление " + x + " / " + y + ": быстрое " + actual + ", прежнее " + expected);
        }

        private static void AssertSqrt(long x)
        {
            long expected = ReferenceSqrt(x);
            long actual = Fix64.Sqrt(Fix64.FromRaw(x)).Raw;
            if (actual != expected)
                Assert.Fail("корень " + x + ": быстрый " + actual + ", прежний " + expected);
        }

        // ---------- эталон: прежние алгоритмы Fix64 без изменений ----------

        private static long ReferenceDivide(long xl, long yl)
        {
            ulong remainder = (ulong)(xl >= 0 ? xl : -xl);
            ulong divider = (ulong)(yl >= 0 ? yl : -yl);
            ulong quotient = 0UL;
            int bitPos = FractionalBits + 1;
            while ((divider & 0xF) == 0 && bitPos >= 4) { divider >>= 4; bitPos -= 4; }
            while (remainder != 0 && bitPos >= 0)
            {
                int shift = ReferenceLeadingZeroes(remainder);
                if (shift > bitPos) shift = bitPos;
                remainder <<= shift;
                bitPos -= shift;
                ulong div = remainder / divider;
                remainder = remainder % divider;
                quotient += div << bitPos;
                if (bitPos > 0 && (div & ~(0xFFFFFFFFFFFFFFFFUL >> bitPos)) != 0)
                    return ((xl ^ yl) & long.MinValue) == 0 ? long.MaxValue : long.MinValue;
                remainder <<= 1;
                --bitPos;
            }
            ++quotient;
            long result = (long)(quotient >> 1);
            if (((xl ^ yl) & long.MinValue) != 0) result = -result;
            return result;
        }

        private static int ReferenceLeadingZeroes(ulong x)
        {
            int result = 0;
            while ((x & 0xF000000000000000UL) == 0) { result += 4; x <<= 4; }
            while ((x & 0x8000000000000000UL) == 0) { result += 1; x <<= 1; }
            return result;
        }

        private static long ReferenceSqrt(long xl)
        {
            ulong num = (ulong)xl;
            ulong result = 0UL;
            ulong bit = 1UL << 62;
            while (bit > num) bit >>= 2;
            for (int i = 0; i < 2; ++i)
            {
                while (bit != 0)
                {
                    if (num >= result + bit) { num -= result + bit; result = (result >> 1) + bit; }
                    else { result = result >> 1; }
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
            return (long)result;
        }

        /// <summary>Свой генератор: один и тот же ряд в .NET и в Unity.</summary>
        private sealed class XorShift
        {
            private ulong _state;
            public XorShift(ulong seed) { _state = seed; }

            public ulong Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 7;
                _state ^= _state << 17;
                return _state;
            }

            /// <summary>Неотрицательное число ровно из bits бит (старший бит взведён).</summary>
            public long Raw(int bits)
            {
                if (bits == 0) return 0;
                ulong v = Next();
                if (bits < 64) v &= (1UL << bits) - 1;
                v |= 1UL << (bits - 1);
                return (long)(v & 0x7FFFFFFFFFFFFFFFUL);
            }
        }
    }
}
