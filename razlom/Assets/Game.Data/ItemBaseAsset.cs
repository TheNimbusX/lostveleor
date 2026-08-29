using UnityEngine;
using Game.Sim;

namespace Game.Data
{
    /// <summary>
    /// База предмета в редакторе: «ржавый меч», «кожаная куртка».
    /// Как и AffixAsset, в симуляцию не попадает — конвертируется на границе.
    /// </summary>
    [CreateAssetMenu(fileName = "base_", menuName = "Разлом/База предмета")]
    public sealed class ItemBaseAsset : ScriptableObject
    {
        [Header("Опознание")]
        [Tooltip("Стабильная строка, например base.rusty_sword. НИКОГДА не менять у выпущенной базы.")]
        public string StableKey = "base.";

        public ItemCategory Category = ItemCategory.Weapon;

        [Header("Собственный модификатор базы")]
        [Tooltip("Не роллится: одинаков на всех экземплярах этой базы.")]
        public bool HasImplicit;
        public StatType ImplicitStat = StatType.Damage;
        public ModifierOp ImplicitOp = ModifierOp.Flat;
        public float ImplicitValue;

        public ItemBaseDefinition ToDefinition()
        {
            int id = StableId.Of(StableKey);

            return HasImplicit
                ? new ItemBaseDefinition(id, Category, ImplicitStat, ImplicitOp,
                    Fix64.FromDouble(ImplicitValue))
                : new ItemBaseDefinition(id, Category);
        }
    }
}
