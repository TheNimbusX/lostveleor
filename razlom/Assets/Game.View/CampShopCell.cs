using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>Ячейка вещи в окнах кузнеца и торговца (префаб CampShopsWc). Только ссылки и вид.</summary>
    public sealed class CampShopCell : MonoBehaviour
    {
        public Button Button;
        public Image Icon;
        public TMP_Text Level;
        public WcRarity Rarity;
        [Tooltip("Одна рамка и цветной фон редкости: выбор и наведение меняют её же")] public WcSlotState State;
        [Tooltip("Старое: отдельная рамка выбора (у префабов до 23 сентября)")] public GameObject Selected;
        [Tooltip("Пустая ячейка: пунктир и ромб")] public GameObject Empty;

        public void Show(Sprite icon, string level, bool rare, bool selected) => Show(icon, level, rare ? 1 : 0, selected);

        /// <summary>Недоступная ячейка (надетое на вкладке «Разбор»): вещь тусклая.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (Icon != null) Icon.color = dimmed ? new Color(.55f, .6f, .68f, .45f) : Color.white;
        }

        /// <summary>rarity — как ItemRarity: 0 обычная … 3 уникальная.</summary>
        public void Show(Sprite icon, string level, int rarity, bool selected)
        {
            bool has = icon != null;
            Icon.sprite = icon;
            Icon.enabled = has;
            if (Empty != null) Empty.SetActive(!has);
            if (Level != null) Level.text = level;
            if (Rarity != null) Rarity.Set(has ? WcRarity.FromItem(rarity) : WcRarity.Tier.Common);
            if (State != null) State.Set(has ? rarity : WcSlotState.Empty, selected);
            if (Selected != null) Selected.SetActive(selected);
        }
    }
}
