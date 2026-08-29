using System;

namespace Game.Sim
{
    /// <summary>
    /// PCG32 (PCG-XSH-RR, 64-битное состояние, 32-битный выход).
    /// Маленький, быстрый, статистически хороший и полностью детерминированный.
    ///
    /// Структура изменяемая: держать её по ссылке (поле класса) или передавать по ref.
    /// </summary>
    public struct Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        public Pcg32(ulong seed, ulong sequence)
        {
            _state = 0UL;
            _inc = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>Состояние для хеширования и сохранения. Только чтение.</summary>
        public ulong State => _state;
        public ulong Increment => _inc;

        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = oldState * Multiplier + _inc;
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>
        /// Целое в [0, bound). Отбраковка смещённого диапазона делается детерминированно,
        /// поэтому последовательность одинакова везде.
        /// </summary>
        public uint NextBounded(uint bound)
        {
            if (bound == 0) return 0;
            uint threshold = (uint)((0x100000000UL - bound) % bound);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }

        /// <summary>Целое в [min, maxExclusive).</summary>
        public int NextInt(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)NextBounded((uint)(maxExclusive - min));
        }

        /// <summary>Дробное в [0, 1). Ровно 32 бита точности — весь дробный разряд Fix64.</summary>
        public Fix64 NextFix() => Fix64.FromRaw(NextUInt());

        /// <summary>Дробное в [min, max).</summary>
        public Fix64 NextFix(Fix64 min, Fix64 max) => min + (max - min) * NextFix();

        /// <summary>Испытание с вероятностью p, где p в [0, 1].</summary>
        public bool Chance(Fix64 p) => NextFix() < p;

        /// <summary>Прокрутить генератор вперёд без использования результата.</summary>
        public void Skip(int count)
        {
            for (int i = 0; i < count; i++) NextUInt();
        }
    }
}
