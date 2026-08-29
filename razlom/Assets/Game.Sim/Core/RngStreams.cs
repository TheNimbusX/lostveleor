namespace Game.Sim
{
    /// <summary>
    /// Независимые потоки случайности, по одному на предметную область.
    /// Все выведены из одного мастер-сида забега.
    ///
    /// ЗАЧЕМ ЭТО ТАК. Один общий генератор — дорогая ошибка. Стоит добавить
    /// в патче ещё один бросок на крит, и вся последовательность сдвинется:
    /// у тех же сидов начнёт падать другой лут, старые реплеи сломаются,
    /// баг-репорты перестанут воспроизводиться.
    ///
    /// С раздельными потоками добавление нового броска в боёвку не трогает
    /// генерацию карты и лута.
    ///
    /// НОВЫЙ ПОТОК ДОБАВЛЯТЬ ТОЛЬКО В КОНЕЦ и никогда не менять существующие
    /// номера последовательностей — иначе сломаются все сохранённые сиды.
    /// </summary>
    public sealed class RngStreams
    {
        public Pcg32 Layout;   // 1 — планировка карты и Разломов
        public Pcg32 Spawns;   // 2 — что и где заспавнилось
        public Pcg32 Combat;   // 3 — криты, уклонения, разброс урона
        public Pcg32 Loot;     // 4 — что выпало
        public Pcg32 Affix;    // 5 — роллы аффиксов на предмете
        public Pcg32 Ai;       // 6 — решения ИИ

        public readonly ulong MasterSeed;

        public RngStreams(ulong masterSeed)
        {
            MasterSeed = masterSeed;
            Layout = new Pcg32(masterSeed, 1);
            Spawns = new Pcg32(masterSeed, 2);
            Combat = new Pcg32(masterSeed, 3);
            Loot   = new Pcg32(masterSeed, 4);
            Affix  = new Pcg32(masterSeed, 5);
            Ai     = new Pcg32(masterSeed, 6);
        }

        /// <summary>Состояния всех потоков — часть хеша состояния симуляции.</summary>
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Layout.State);
            Hashing.Mix(ref hash, Spawns.State);
            Hashing.Mix(ref hash, Combat.State);
            Hashing.Mix(ref hash, Loot.State);
            Hashing.Mix(ref hash, Affix.State);
            Hashing.Mix(ref hash, Ai.State);
        }
    }

    /// <summary>FNV-1a по 64-битным словам. Используется только для проверки детерминизма.</summary>
    public static class Hashing
    {
        public const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static void Mix(ref ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= Prime;
            }
        }

        public static void Mix(ref ulong hash, long value) => Mix(ref hash, (ulong)value);
        public static void Mix(ref ulong hash, int value) => Mix(ref hash, (ulong)(uint)value);
        public static void Mix(ref ulong hash, Fix64 value) => Mix(ref hash, (ulong)value.Raw);
    }
}
