using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Итоги забега со статистикой (выбор владельца 30.09, кадр 4-summary-stats — «ок»): время, арены,
    /// глубина, уровни, урон нанесён и получен, лучший удар, криты, зелья; «Убито» — круги видов врагов с
    /// «×N»; полосы «Потеряно» и «Остаётся». Здесь чистая логика без Unity (проверяется в
    /// Combat.Presentation.Tests): порядок строк, подписи, формат чисел, счёт по очереди, порядок убитых и
    /// строки полос. Числа — из RunSummary и его RunStats (GameSession.LastRun.Stats, только чтение).
    /// Вид — RunHud.Summary, узлы — миграция v3 RunHudWcBuilder.
    /// </summary>
    public static class RunHudSummary
    {
        /// <summary>Строка статистики. Порядок перечисления — порядок на экране: левый столбец, потом правый.</summary>
        public enum Stat : byte { Time, Arenas, Depth, Levels, DamageDealt, DamageTaken, BestHit, Crits, Potions }

        /// <summary>Строки по порядку чтения; первые <see cref="LeftColumn"/> — в левом столбце.</summary>
        public static readonly Stat[] Rows =
        {
            Stat.Time, Stat.Arenas, Stat.Depth, Stat.Levels, Stat.DamageDealt,
            Stat.DamageTaken, Stat.BestHit, Stat.Crits, Stat.Potions,
        };

        public const int LeftColumn = 5;

        /// <summary>Кругов «Убито»: больше видов — самые частые.</summary>
        public const int KillSlots = 6;

        public static string Caption(Stat stat)
        {
            switch (stat)
            {
                case Stat.Time: return "Время";
                case Stat.Arenas: return "Арен зачищено";
                case Stat.Depth: return "Глубина";
                case Stat.Levels: return "Уровней получено";
                case Stat.DamageDealt: return "Урон нанесён";
                case Stat.DamageTaken: return "Урон получен";
                case Stat.BestHit: return "Лучший удар";
                case Stat.Crits: return "Критов";
                default: return "Зелий выпито";
            }
        }

        /// <summary>Белый знак забега строки (Assets/UI/RunIcons): часов в наборе нет — время — круговая стрелка.</summary>
        public static string Icon(Stat stat)
        {
            switch (stat)
            {
                case Stat.Time: return "repeat";
                case Stat.Arenas: return "rift";
                case Stat.Depth: return "depth";
                case Stat.Levels: return "ability";
                case Stat.DamageDealt: return "encounter";
                case Stat.DamageTaken: return "health";
                case Stat.BestHit: return "victory";
                case Stat.Crits: return "lavidium";
                default: return "alchemist";
            }
        }

        /// <summary>Число строки из итогов забега.</summary>
        public static long Value(Stat stat, in RunSummary summary)
        {
            RunStats stats = summary.Stats;
            switch (stat)
            {
                case Stat.Time: return stats.TotalSeconds;
                case Stat.Arenas: return summary.RiftsCleared;
                case Stat.Depth: return summary.Depth;
                case Stat.Levels: return stats.LevelsGained;
                case Stat.DamageDealt: return stats.DamageDealt;
                case Stat.DamageTaken: return stats.DamageTaken;
                case Stat.BestHit: return stats.BestHit;
                case Stat.Crits: return stats.Crits;
                default: return stats.PotionsUsed;
            }
        }

        /// <summary>Как число строки выглядит: «12:47», «+3», «18 420».</summary>
        public static string Format(Stat stat, long value)
        {
            if (stat == Stat.Time) return Clock(value);
            if (stat == Stat.Levels) return value > 0 ? "+" + value : "0";
            return Number(value);
        }

        /// <summary>Время забега: «4:07», «12:47», больше часа — «1:02:05».</summary>
        public static string Clock(long seconds)
        {
            if (seconds < 0) seconds = 0;
            long hours = seconds / 3600, minutes = seconds / 60 % 60, rest = seconds % 60;
            return hours > 0
                ? hours + ":" + minutes.ToString("00") + ":" + rest.ToString("00")
                : minutes + ":" + rest.ToString("00");
        }

        /// <summary>Неразрывный пробел между разрядами: «18 420» не рвётся на строки.</summary>
        public const char GroupSeparator = ' ';

        /// <summary>Число с разрядами через неразрывный пробел: «18 420», «2 960», «612».</summary>
        public static string Number(long value)
        {
            bool negative = value < 0;
            ulong rest = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
            string digits = rest.ToString();
            int groups = (digits.Length - 1) / 3;
            if (groups == 0) return negative ? "−" + digits : digits;
            var chars = new char[digits.Length + groups];
            int write = chars.Length - 1;
            for (int read = digits.Length - 1, n = 0; read >= 0; read--, n++)
            {
                if (n > 0 && n % 3 == 0) chars[write--] = GroupSeparator;
                chars[write--] = digits[read];
            }
            return (negative ? "−" : "") + new string(chars);
        }

        // ---- счёт по очереди ----

        /// <summary>Счёт начинается, когда цифры проявились из дыма; строки — по очереди, каждая за свои полсекунды.</summary>
        public const float CountDelay = .9f, CountDuration = .55f, CountStagger = .11f;

        /// <summary>Доля счёта строки номер <paramref name="order"/> через <paramref name="clock"/> с после показа (0…1, мягкий конец).</summary>
        public static float CountK(float clock, int order)
        {
            float k = (clock - CountDelay - order * CountStagger) / CountDuration;
            if (k <= 0f) return 0f;
            if (k >= 1f) return 1f;
            return 1f - (1f - k) * (1f - k) * (1f - k);
        }

        /// <summary>Показанное число при доле счёта <paramref name="k"/>: к цели снизу, последнее — ровно цель.</summary>
        public static long Counted(long target, float k)
        {
            if (k >= 1f) return target;
            if (k <= 0f) return 0;
            return (long)Math.Round(target * (double)k);
        }

        /// <summary>Все строки досчитаны: <paramref name="count"/> строк после <paramref name="clock"/> с.</summary>
        public static bool CountDone(float clock, int count) => count <= 0 || CountK(clock, count - 1) >= 1f;

        // ---- убитые ----

        /// <summary>
        /// Виды убитых для кругов «Убито»: чаще убитые раньше, при равенстве — кто убит первым (как забег шёл).
        /// Возвращает сколько видов записано в <paramref name="into"/> (не больше его длины).
        /// </summary>
        public static int KillOrder(RunStats stats, EnemyKind[] into)
        {
            if (stats == null || into == null) return 0;
            int count = 0;
            for (int i = 0; i < stats.KilledKindCount; i++)
            {
                EnemyKind kind = stats.KilledKind(i);
                int kills = stats.KillsOf(kind);
                if (kills <= 0) continue;
                // Вставка по убыванию; равные остаются в порядке первого убийства.
                int at = count;
                while (at > 0 && stats.KillsOf(into[at - 1]) < kills) at--;
                if (at >= into.Length) continue;
                int last = Math.Min(count, into.Length - 1);
                for (int j = last; j > at; j--) into[j] = into[j - 1];
                into[at] = kind;
                if (count < into.Length) count++;
            }
            return count;
        }

        /// <summary>«×38».</summary>
        public static string KillCount(int kills) => "×" + kills;

        /// <summary>
        /// Рост тела вида в метрах — по нему кадрируется портрет для «Убито» (RunHud.Portraits): камера
        /// отходит так, чтобы тело заняло кадр. Незнакомый вид — как человек.
        /// </summary>
        public static float PortraitHeight(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestGuardian: return 3f;
                case EnemyKind.ForestRootSwarm: return .9f;
                case EnemyKind.ForestBud: return 1.3f;
                case EnemyKind.ForestWendigo: return 2.6f;
                case EnemyKind.ForestStonehoof: return 2f;
                case EnemyKind.ForestThorncaster: return 2.1f;
                case EnemyKind.ForestRootSnarer: return 2f;
                case EnemyKind.ForestSplitter: return 1.8f;
                case EnemyKind.ForestSplitling: return 1f;
                default: return 1.8f;
            }
        }

        // ---- «Потеряно» и «Остаётся» ----

        /// <summary>Форма слова по числу: 0 — «1 вещь», 1 — «2 вещи», 2 — «5 вещей».</summary>
        public static int Plural(long n)
        {
            n = Math.Abs(n);
            long tens = n % 100, ones = n % 10;
            if (tens >= 11 && tens <= 14) return 2;
            if (ones == 1) return 0;
            return ones >= 2 && ones <= 4 ? 1 : 2;
        }

        static string Word(long n, string one, string few, string many)
        {
            int form = Plural(n);
            return form == 0 ? one : form == 1 ? few : many;
        }

        /// <summary>
        /// Строки полосы «Потеряно»: что осталось в Разломе со смертью и что не влезло в сумку. Пусто — одна
        /// строка «Ничего» (вид красит её приглушённо). Возвращает число строк.
        /// </summary>
        public static int LostLines(in RunSummary summary, bool developer, string[] into)
        {
            int n = 0;
            if (into == null || into.Length == 0) return 0;
            if (developer)
            {
                into[n++] = "Тестовый забег — добыча не переносится";
                return n;
            }
            if (summary.ItemsLeftBehind > 0 && n < into.Length)
                into[n++] = summary.ItemsLeftBehind + " " + Word(summary.ItemsLeftBehind, "вещь", "вещи", "вещей") + " в Разломе";
            if (summary.GoldLeftBehind > 0 && n < into.Length)
                into[n++] = "−" + Number(summary.GoldLeftBehind) + " золота";
            if (summary.ItemsLost > 0 && n < into.Length)
                into[n++] = "Не влезло в сумку: " + summary.ItemsLost;
            if (n == 0) into[n++] = "Ничего";
            return n;
        }

        /// <summary>
        /// Строки полосы «Остаётся»: уровни и опыт (уходят в лагерь сразу, смерть их не трогает), вещи в сумке
        /// и золото. Возвращает число строк.
        /// </summary>
        public static int KeptLines(in RunSummary summary, bool developer, string[] into)
        {
            int n = 0;
            if (into == null || into.Length == 0) return 0;
            if (developer)
            {
                into[n++] = "В лагерь ничего не уйдёт";
                return n;
            }
            RunStats stats = summary.Stats;
            if (stats.LevelsGained > 0)
                into[n++] = "+" + stats.LevelsGained + " " + Word(stats.LevelsGained, "уровень", "уровня", "уровней") + " · опыт сохранён";
            else if (stats.ExperienceGained > 0)
                into[n++] = "+" + Number(stats.ExperienceGained) + " опыта · сохранён";
            else into[n++] = "Опыт сохранён";
            if (summary.ItemsKept > 0 && n < into.Length)
                into[n++] = summary.ItemsKept + " " + Word(summary.ItemsKept, "вещь", "вещи", "вещей") + " в сумке лагеря";
            if (summary.GoldKept > 0 && n < into.Length)
                into[n++] = "+" + Number(summary.GoldKept) + " золота";
            return n;
        }

        /// <summary>
        /// Подпись стоп-кадра: «Последний удар · Лесной вендиго · 64» (гибель), «Победный удар · Хранитель лугов ·
        /// 612» (победа), «Уход из Разлома · арена 3» (ушёл с добычей). Имя врага и число — снаружи; нет имени —
        /// без него.
        /// </summary>
        public static string FreezeCaption(RunOutcome outcome, string foe, int blow, int depth)
        {
            if (outcome == RunOutcome.Left) return "Уход из Разлома · арена " + Math.Max(1, depth);
            string head = outcome == RunOutcome.Died ? "Последний удар" : "Победный удар";
            if (!string.IsNullOrEmpty(foe)) head += " · " + foe;
            if (blow > 0) head += " · " + Number(blow);
            return head;
        }
    }
}
