using System.Text;

namespace Game.View
{
    /// <summary>
    /// ОДИН ФОРМАТ ПОДСКАЗКИ КЛАВИШИ — «[Esc] Закрыть» (лист 5 единого набора, выбор владельца 30.09). Раньше у окон
    /// было шесть записей: «Esc — продолжить игру», «Esc / B — закрыть», «ПКМ · Подойти», «I / Esc — закрыть»,
    /// «1  2  3 — выбрать    ·    L — уйти с добычей», «[Alt] подробнее». Теперь везде: клавиша в скобках (там, где
    /// можно — кейкапом UiInkKit.Keycap), за ней глагол с большой буквы; несколько клавиш одного действия — каждая в
    /// своих скобках («[ПКМ] [Esc] Отмена»), несколько действий — через <see cref="Separator"/>.
    ///
    /// Здесь только строки и числа, без Unity (проверяется в tools/Combat.Presentation.Tests); подгонка ширины
    /// кейкапа по подписи — UiKeyHint.Keycap.cs.
    /// </summary>
    public static partial class UiKeyHint
    {
        /// <summary>Между действиями в одной строке: «[ЛКМ] Применить   ·   [ПКМ] [Esc] Отмена».</summary>
        public const string Separator = "   ·   ";

        // Готовые подсказки — константы: собираются при компиляции, без строки на каждый показ.
        public const string EscClose = "[Esc] Закрыть";
        public const string EscCancel = "[Esc] Отмена";
        public const string EscBack = "[Esc] Назад";

        /// <summary>Прицел способности мышью: подсказка у курсора (RunWorldView, запасной PelagTargetAimView).</summary>
        public const string AimMouse = "[ЛКМ] Применить" + Separator + "[ПКМ] [Esc] Отмена";
        /// <summary>Прицел способности геймпадом.</summary>
        public const string AimPad = "[Правый стик] Прицел" + Separator + "[RT] [A] Применить" + Separator + "[B] Отмена";
        /// <summary>Строка состояния подсказки способности, пока идёт прицел (боевой HUD и его запасной IMGUI).</summary>
        public const string AimStatus = "Выбери цель" + Separator + "[ПКМ] Отмена";

        /// <summary>Клавиша в скобках: «[Esc]». Пустая — пустая строка.</summary>
        public static string Key(string key) => string.IsNullOrEmpty(key) ? string.Empty : "[" + key.Trim() + "]";

        /// <summary>Глагол подсказки с большой буквы: «закрыть» → «Закрыть» (остальные буквы не трогаются).</summary>
        public static string Verb(string verb)
        {
            if (string.IsNullOrEmpty(verb)) return string.Empty;
            verb = verb.Trim();
            if (verb.Length == 0 || char.IsUpper(verb[0])) return verb;
            return char.ToUpperInvariant(verb[0]) + verb.Substring(1);
        }

        /// <summary>
        /// «[Esc] Закрыть», «[1] [2] [3] Выбрать», «[ПКМ] [Esc] Отмена». Пустые клавиши пропускаются; без клавиш —
        /// один глагол, без глагола — одни клавиши.
        /// </summary>
        public static string Hint(string verb, string key1, string key2 = null, string key3 = null, string key4 = null)
        {
            var text = new StringBuilder(32);
            AppendKey(text, key1);
            AppendKey(text, key2);
            AppendKey(text, key3);
            AppendKey(text, key4);
            string action = Verb(verb);
            if (action.Length > 0)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(action);
            }
            return text.ToString();
        }

        /// <summary>Два действия одной строкой через <see cref="Separator"/>; пустое не даёт лишнего разделителя.</summary>
        public static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second ?? string.Empty;
            if (string.IsNullOrEmpty(second)) return first;
            return first + Separator + second;
        }

        /// <summary>Три действия одной строкой.</summary>
        public static string Join(string first, string second, string third) => Join(Join(first, second), third);

        static void AppendKey(StringBuilder text, string key)
        {
            if (string.IsNullOrEmpty(key) || key.Trim().Length == 0) return;
            if (text.Length > 0) text.Append(' ');
            text.Append('[').Append(key.Trim()).Append(']');
        }
    }
}
