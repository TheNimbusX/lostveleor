using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Одна таблица характеристик для палатки, сравнения вещей, кузнеца и торговца.
    /// Раньше их было три, и они расходились: «Скорость» и «Скор. навыков» в палатке,
    /// «Исполнение» в сравнении, «Скорость способностей» у кузнеца; сила крита то «150%»,
    /// то «1.5». Тексты — ключи CampServiceText: stat.X (полное имя), stat.X.short
    /// (строка листа героя), stat.X.hint (что делает).
    /// </summary>
    public static class StatText
    {
        public static string Name(StatType stat) => CampServiceText.Get("stat." + stat);
        public static string Short(StatType stat) => CampServiceText.Get("stat." + stat + ".short");
        public static string Hint(StatType stat) => CampServiceText.Get("stat." + stat + ".hint");

        /// <summary>Доля, показанная процентом: шанс и сила крита, сопротивление, ускорения.</summary>
        public static bool Percent(StatType stat) =>
            stat == StatType.CritChance || stat == StatType.CritMultiplier || stat == StatType.FireResist
            || stat == StatType.AbilitySpeed || stat == StatType.CooldownRecovery;

        /// <summary>Число для показа: проценты умножены на 100.</summary>
        public static float Shown(StatType stat, Fix64 value) => Percent(stat) ? value.ToFloat() * 100f : value.ToFloat();

        /// <summary>Итоговое значение стата: «14», «8%», «4,5 м/с», «3/с».</summary>
        public static string Value(StatType stat, float shown)
        {
            if (Percent(stat)) return Mathf.RoundToInt(shown) + "%";
            switch (stat)
            {
                case StatType.MoveSpeed: return shown.ToString("0.#") + " м/с";
                case StatType.LavidiumRegen: return shown.ToString("0.#") + "/с";
                case StatType.AttackSpeed: return shown.ToString("0.##") + "/с";
                default: return Mathf.RoundToInt(shown).ToString();
            }
        }

        public static string Value(StatType stat, Fix64 value) => Value(stat, Shown(stat, value));

        /// <summary>Разница со знаком: «+3», «−2%».</summary>
        public static string Delta(StatType stat, Fix64 delta)
        {
            float shown = Shown(stat, delta);
            string sign = shown > 0f ? "+" : shown < 0f ? "−" : "";
            string number = Percent(stat) ? Mathf.RoundToInt(Mathf.Abs(shown)) + "%"
                : Mathf.Abs(shown) >= 10f || Mathf.Approximately(Mathf.Round(shown), shown) ? Mathf.RoundToInt(Mathf.Abs(shown)).ToString()
                : Mathf.Abs(shown).ToString("0.##");
            return sign + number;
        }

        /// <summary>Свойство вещи: прибавка числом или процентом («+12», «+6%»).</summary>
        public static string Modifier(StatType stat, Fix64 value, ModifierOp op)
        {
            float v = value.ToFloat();
            string sign = v < 0f ? "−" : "+";
            if (op != ModifierOp.Flat || Percent(stat)) return sign + (Mathf.Abs(v) * 100f).ToString("0.#") + "%";
            return sign + Mathf.Abs(v).ToString("0.##");
        }
    }
}
