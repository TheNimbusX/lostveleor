using System.Globalization;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Сравнение «было → станет» на карточке награды (выбор владельца 30.09, кадр 3a): «Здоровье 120 → 150 ▲»,
    /// «Перезарядка 8 с → 6 с ▼». Здесь чистая логика без Unity (проверяется в Combat.Presentation.Tests):
    /// какие характеристики меняются и как собрать строку; имена и значения характеристик героя пишет вид
    /// (StatText), характеристик способностей — здесь же.
    ///
    /// Вещь сравнивается с надетой тем же способом, что в палатке (CampInventoryView.CompareStats): копия
    /// настоящего листа героя, из неё снята вещь того же слота и надета новая. Копия — свой лист-черновик,
    /// заведённый один раз: сравнение считается при показе экрана, а не каждый кадр. Палатка может перейти на
    /// этот же помощник — порядок и правило «только изменившееся» совпадают.
    /// </summary>
    public static class RunHudCompare
    {
        /// <summary>Характеристика героя: было и станет.</summary>
        public struct StatRow
        {
            public StatType Stat;
            public Fix64 Before, After;

            /// <summary>Куда идёт число: 1 — растёт, −1 — падает. У характеристик героя больше — лучше.</summary>
            public int Direction => After > Before ? 1 : After < Before ? -1 : 0;
        }

        /// <summary>Характеристика способности: было и станет с усилением.</summary>
        public struct AbilityRow
        {
            public AbilityStatType Stat;
            public Fix64 Before, After;
            public int Direction => After > Before ? 1 : After < Before ? -1 : 0;
            /// <summary>Лучше ли станет: у перезарядки, цены и замаха лучше — меньше.</summary>
            public int Better => LowerIsBetter(Stat) ? -Direction : Direction;
        }

        /// <summary>Порядок строк — тот же, что в палатке.</summary>
        public static readonly StatType[] Order =
        {
            StatType.Damage, StatType.AttackSpeed, StatType.CritChance, StatType.Armor, StatType.MaxHealth, StatType.MoveSpeed,
            StatType.FireResist, StatType.CritMultiplier, StatType.MaxLavidium, StatType.LavidiumRegen, StatType.AbilitySpeed,
            StatType.CooldownRecovery,
        };

        /// <summary>
        /// Вещь против надетой: строки изменившихся характеристик в порядке <see cref="Order"/>, не больше
        /// into.Length. <paramref name="slot"/> — слот вещи (кто сейчас надет, спрашивать у Equipment).
        /// −1 — вещи нет в каталоге (сравнивать не с чем, слот неизвестен); 0 — ничего не меняется.
        /// </summary>
        public static int ItemRows(StatSheet current, in ItemInstance item, ItemDatabase items, GeneratedItem roll,
            StatSheet scratch, StatRow[] into, out EquipSlot slot)
        {
            slot = default;
            if (current == null || items == null || roll == null || scratch == null || into == null || item.IsEmpty) return -1;
            if (!ItemGenerator.Generate(in item, items, roll)) return -1;
            int index = items.IndexOfBase(item.BaseId);
            if (index < 0) return -1;
            slot = Equipment.SlotOf(items.GetBase(index).Category);
            CopyInto(current, scratch);
            scratch.RemoveSource(ModifierSource.Equipment, (int)slot);
            roll.ApplyTo(scratch, (int)slot);
            return Diff(current, scratch, into);
        }

        /// <summary>Прибавка характеристики до конца забега: одна строка, если число меняется.</summary>
        public static bool StatBoostRow(StatSheet current, StatType stat, ModifierOp op, Fix64 value, StatSheet scratch, out StatRow row)
        {
            row = default;
            if (current == null || scratch == null) return false;
            CopyInto(current, scratch);
            scratch.Add(new StatModifier(stat, op, value, ModifierSource.TreeNode, int.MaxValue));
            row = new StatRow { Stat = stat, Before = current.Get(stat), After = scratch.Get(stat) };
            return row.Before != row.After;
        }

        static void CopyInto(StatSheet from, StatSheet to)
        {
            to.ClearModifiers();
            for (int s = 0; s < (int)StatType.Count; s++) to.SetBase((StatType)s, from.GetBase((StatType)s));
            for (int m = 0; m < from.ModifierCount; m++)
            {
                StatModifier mod = from.GetModifier(m);
                to.Add(in mod);
            }
        }

        static int Diff(StatSheet before, StatSheet after, StatRow[] into)
        {
            int count = 0;
            for (int i = 0; i < Order.Length && count < into.Length; i++)
            {
                StatType stat = Order[i];
                Fix64 was = before.Get(stat), will = after.Get(stat);
                if (was == will) continue;
                into[count++] = new StatRow { Stat = stat, Before = was, After = will };
            }
            return count;
        }

        /// <summary>
        /// Усиление способности: что оно меняет числом. Механические усиления (флаги) числа не меняют —
        /// false, на карточке остаётся описание. Черновики сборки и буфер узлов — снаружи, заведённые раз.
        /// </summary>
        public static bool TalentRow(RunLoadout loadout, int poolIndex, int talentIndex, AbilityBuild before, AbilityBuild after,
            AbilityNode[] buffer, out AbilityRow row)
        {
            row = default;
            if (loadout == null || before == null || after == null || buffer == null) return false;
            AbilityDefinition definition = PelagKit.PoolDefinition(poolIndex);
            if (definition == null || !SabreTalents.TryLineOf(poolIndex, out SabreTalentLine line)) return false;
            int count = 0;
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++)
                if (i != talentIndex && loadout.HasTalent(poolIndex, i)) count = SabreTalents.AppendNode(line, i, buffer, count);
            int with = SabreTalents.AppendNode(line, talentIndex, buffer, count);
            if (with == count) return false;
            AbilityNode offered = buffer[count];
            if (offered.Kind != NodeKind.StatMod) return false;
            // Rebuild сортирует узлы у себя в начале массива: первая сборка не трогает предложенный узел.
            before.Rebuild(definition, buffer, count);
            Fix64 was = before.Get(offered.Stat);
            after.Rebuild(definition, buffer, with);
            row = new AbilityRow { Stat = offered.Stat, Before = was, After = after.Get(offered.Stat) };
            return row.Before != row.After;
        }

        /// <summary>У перезарядки, цены, замаха и окна «меньше» — это лучше.</summary>
        public static bool LowerIsBetter(AbilityStatType stat)
            => stat == AbilityStatType.CooldownTicks || stat == AbilityStatType.LavidiumCost || stat == AbilityStatType.WindupTicks;

        /// <summary>Имя характеристики способности на карточке; у дальних способностей радиус — дальность.</summary>
        public static string AbilityStatName(SabreTalentLine line, AbilityStatType stat)
        {
            switch (stat)
            {
                case AbilityStatType.Radius:
                    return line == SabreTalentLine.Boarding ? "Длина цепи"
                        : line == SabreTalentLine.Flask ? "Дальность броска"
                        : line == SabreTalentLine.Cleave || line == SabreTalentLine.AnchorSlam ? "Дальность" : "Радиус";
                case AbilityStatType.Width: return line == SabreTalentLine.Flask ? "Лужа" : "Ширина";
                case AbilityStatType.CooldownTicks: return "Перезарядка";
                case AbilityStatType.DurationTicks: return line == SabreTalentLine.Flask ? "Лужа горит" : "Горит";
                // Цена — ресурс способностей, игроку «Концентрация» (владелец 01.10); стат в коде прежний.
                case AbilityStatType.LavidiumCost: return "Концентрация";
                case AbilityStatType.WindupTicks: return "Замах";
                case AbilityStatType.StunTicks: return "Оглушение";
                case AbilityStatType.ComboWindowTicks: return "Окно серии";
                case AbilityStatType.KnockbackDistance: return "Отброс";
                case AbilityStatType.Damage: return "Урон";
                default: return "Сила";
            }
        }

        /// <summary>Значение характеристики способности: «3,5 м», «1,5 с», «28».</summary>
        public static string AbilityStatValue(AbilityStatType stat, Fix64 value)
        {
            switch (stat)
            {
                case AbilityStatType.Radius:
                case AbilityStatType.Width:
                case AbilityStatType.MinimumRadius:
                case AbilityStatType.WeaponRadius:
                case AbilityStatType.KnockbackDistance:
                    return Number(value.ToFloat()) + " м";
                case AbilityStatType.CooldownTicks:
                case AbilityStatType.DurationTicks:
                case AbilityStatType.StunTicks:
                case AbilityStatType.WindupTicks:
                case AbilityStatType.ComboWindowTicks:
                case AbilityStatType.BurnTicks:
                    return Number(value.ToFloat() / Simulation.TicksPerSecond) + " с";
                default:
                    return Number(value.ToFloat());
            }
        }

        /// <summary>Число по-русски: до десятых, запятая, без лишнего нуля — «3,5», «8», «0,8».</summary>
        public static string Number(float value)
        {
            float rounded = (float)System.Math.Round(value * 10f) / 10f;
            return rounded.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        /// <summary>
        /// Строка сравнения: «Здоровье  120 → 150 ▲». Стрелка — куда идёт число, цвет нового значения —
        /// лучше (<paramref name="goodHex"/>) или хуже (<paramref name="badHex"/>) станет; без перемены — без цвета.
        /// </summary>
        public static string Line(string name, string before, string after, int direction, int better, string goodHex, string badHex)
        {
            string arrow = direction > 0 ? " ▲" : direction < 0 ? " ▼" : "";
            string head = name + "  " + before + " → ";
            if (better == 0) return head + after + arrow;
            return head + "<color=" + (better > 0 ? goodHex : badHex) + ">" + after + arrow + "</color>";
        }

        /// <summary>Строка «Надето: …» под сравнением вещи; пустой слот — «Слот пуст».</summary>
        public static string Worn(string wornName) => string.IsNullOrEmpty(wornName) ? "Слот пуст — снимать нечего" : "Надето: " + wornName;
    }
}
