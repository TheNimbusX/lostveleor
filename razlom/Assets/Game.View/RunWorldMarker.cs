using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна метка мира на паке: плашка, значок, надпись; у элиты — полоска здоровья и оранжевый ромб.
    /// Размер плашки подстраивается под текст (ContentSizeFitter в префабе). Свой файл обязателен:
    /// компонент стоит в префабе RunWorldWc.
    /// </summary>
    public sealed class RunWorldMarker : MonoBehaviour
    {
        public RawImage Icon;
        public TMP_Text Text;
        [Tooltip("Полоска здоровья элиты: заливка, Image.Type.Filled")] public Image HealthFill;
        public GameObject HealthRow;
        [Tooltip("Оранжевый ромб элиты слева от имени")] public GameObject EliteMark;

        string _text;

        public void Show(Texture icon, string text, float health, bool elite)
        {
            if (Icon != null)
            {
                if (Icon.texture != icon) Icon.texture = icon;
                Icon.enabled = icon != null && !elite;
            }
            if (EliteMark != null && EliteMark.activeSelf != elite) EliteMark.SetActive(elite);
            if (text != _text && Text != null)
            {
                _text = text;
                Text.text = text;
            }
            bool bar = health >= 0f;
            if (HealthRow != null && HealthRow.activeSelf != bar) HealthRow.SetActive(bar);
            if (bar && HealthFill != null) HealthFill.fillAmount = Mathf.Clamp01(health);
        }
    }
}
