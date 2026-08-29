using UnityEngine;
using Game.Sim;

namespace Game.Data
{
    /// <summary>
    /// Аффикс в том виде, в каком его правит человек в редакторе.
    ///
    /// ЭТО НЕ ТО, ЧТО ВИДИТ СИМУЛЯЦИЯ. ScriptableObject — это UnityEngine.Object,
    /// в Game.Sim он попасть не может физически: у той сборки стоит
    /// noEngineReferences. При загрузке ассет один раз конвертируется в плоскую
    /// структуру AffixDefinition, и дальше симуляция работает только с ней.
    ///
    /// Числа здесь float, и это нормально: конвертация в Fix64 происходит
    /// ровно один раз, на границе, и она детерминирована. Внутрь симуляции
    /// ни один float не проходит.
    /// </summary>
    [CreateAssetMenu(fileName = "affix_", menuName = "Разлом/Аффикс")]
    public sealed class AffixAsset : ScriptableObject
    {
        [Header("Опознание")]
        [Tooltip("Стабильная строка, например affix.fire_damage_t3. НИКОГДА не менять у выпущенного аффикса: " +
                 "от неё считается id, который лежит в сейвах игроков.")]
        public string StableKey = "affix.";

        [Tooltip("Группа взаимоисключения, например affix_group.fire_damage. " +
                 "Два аффикса одной группы на предмет не попадут.")]
        public string GroupKey = "affix_group.";

        [Header("Что делает")]
        public StatType Stat = StatType.Damage;
        public ModifierOp Op = ModifierOp.Flat;

        [Tooltip("Для Increased и More это ДОЛЯ: 0.2 значит +20%.")]
        public float MinValue;
        public float MaxValue;

        [Header("Где встречается")]
        [Tooltip("Минимальный уровень предмета. Так тиры разводятся по глубине забега.")]
        public short MinItemLevel;

        [Tooltip("Вес во взвешенном выборе. Ноль — не выпадает никогда.")]
        public int Weight = 100;

        public bool OnWeapons = true;
        public bool OnArmor;
        public bool OnJewellery;

        /// <summary>Конвертация в плоскую структуру для симуляции. Вызывается один раз при загрузке.</summary>
        public AffixDefinition ToDefinition()
        {
            byte mask = 0;
            if (OnWeapons) mask |= 1 << (int)ItemCategory.Weapon;
            if (OnArmor) mask |= 1 << (int)ItemCategory.Armor;
            if (OnJewellery) mask |= 1 << (int)ItemCategory.Jewellery;

            return new AffixDefinition(
                StableId.Of(StableKey),
                StableId.Of(GroupKey),
                Stat,
                Op,
                Fix64.FromDouble(MinValue),
                Fix64.FromDouble(MaxValue),
                MinItemLevel,
                Weight,
                mask);
        }

        private void OnValidate()
        {
            // Перевёрнутый диапазон даёт NextFix с отрицательной шириной и,
            // как следствие, значение вне задуманного. Ловим у автора данных,
            // а не в отчёте об ошибке через полгода.
            if (MaxValue < MinValue) MaxValue = MinValue;
            if (Weight < 0) Weight = 0;
        }
    }
}
