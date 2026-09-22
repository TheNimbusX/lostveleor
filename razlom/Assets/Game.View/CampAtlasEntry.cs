using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Ячейка атласа находок: основа предмета. Открытая — в цвете и с названием,
    /// закрытая — тёмный силуэт той же иконки. Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class CampAtlasEntry : MonoBehaviour
    {
        public Image Frame;
        public Image Icon;
        public TMP_Text Name;
        [Tooltip("Цвет силуэта неоткрытой вещи")] public Color Silhouette = new Color(.03f, .07f, .14f, .85f);

        public void Show(Sprite frame, Sprite icon, string name, bool open)
        {
            if (Frame != null && frame != null) Frame.sprite = frame;
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
