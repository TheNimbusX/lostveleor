using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одна метка мира в материале «Дым и свет»: клуб дыма, значок, надпись; у элиты — светящийся
    /// огонёк-круг (вместо ромба) и необязательная полоска здоровья. Значок — белый знак забега
    /// (кремовый от темы) или цветной рисунок способности у добычи: рисунок — в круглой маске
    /// (владелец 26 сентября: вместилища — круги). Размер плашки подстраивается под текст
    /// (ContentSizeFitter в префабе). Свой файл обязателен: компонент стоит в префабе RunWorldWc.
    /// </summary>
    public sealed class RunWorldMarker : MonoBehaviour
    {
        [Tooltip("Белый знак забега (выход, тайник, вход, предмет)")] public RawImage Icon;
        [Tooltip("Цветной рисунок способности у добычи — в круглой маске; пусто — рисунок идёт в Icon")] public RawImage Art;
        public TMP_Text Text;
        [Tooltip("Полоска здоровья элиты: заливка, Image.Type.Filled")] public Image HealthFill;
        public GameObject HealthRow;
        [Tooltip("Огонёк элиты слева от имени")] public GameObject EliteMark;

        string _text;

        /// <param name="art">Значок — цветной рисунок способности (круг), а не знак.</param>
        public void Show(Texture icon, string text, float health, bool elite, bool art = false)
        {
            RawImage target = art && Art != null ? Art : Icon;
            RawImage other = target == Icon ? Art : Icon;
            if (target != null)
            {
                if (target.texture != icon) target.texture = icon;
                bool shown = icon != null && !elite;
                if (target.enabled != shown) target.enabled = shown;
            }
            if (other != null && other.enabled) other.enabled = false;
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
