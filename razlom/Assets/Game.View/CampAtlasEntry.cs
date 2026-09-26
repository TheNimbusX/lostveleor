using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Ячейка атласа находок: основа предмета. Открытая — в цвете и с названием,
    /// закрытая — тёмный силуэт той же иконки. Свой файл обязателен: компонент стоит в префабе.
    /// «Дым и свет» (26 сентября): плитка — ячейка UiInkKit.Cell, редкость ставится её
    /// WcSlotState (<see cref="State"/>), спрайт рамки тогда не нужен.
    /// </summary>
    public sealed class CampAtlasEntry : MonoBehaviour
    {
        public Image Frame;
        public Image Icon;
        public TMP_Text Name;
        [Tooltip("Цвет силуэта неоткрытой вещи")] public Color Silhouette = new Color(.03f, .07f, .14f, .85f);
        [Tooltip("«Дым и свет»: состояние плитки (рамка и свет цвета редкости). Пусто — рамка меняет спрайт")] public WcSlotState State;

        /// <param name="rarity">Редкость основы для плитки «Дыма и света»: 0 — обычная, 1 — редкая; -1 — пусто.</param>
        public void Show(Sprite frame, Sprite icon, string name, bool open, int rarity = WcSlotState.Empty)
        {
            if (State != null) State.Set(rarity, false);
            else if (Frame != null && frame != null) Frame.sprite = frame;
            if (Icon != null)
            {
                Icon.sprite = icon;
                Icon.enabled = icon != null;
                Icon.color = open ? Color.white : Silhouette;
            }
            if (Name != null) Name.text = open ? name : "???";
        }
    }
}
