using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Карточка награды и пути на экране выбора (префаб RunHudWc, «Дым и свет»). Только ссылки
    /// на части карточки: что писать, решает <see cref="RunHud"/>, как это выглядит — сборщик
    /// RunHudWcBuilder. Редкость (<see cref="Rarity"/>) красит название, строку вида и тонкое
    /// кольцо света вокруг картинки. Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class RunOfferCard : MonoBehaviour
    {
        public Button Button;
        public RawImage Icon;
        public TMP_Text Title;
        [Tooltip("Строка вида под названием: способность, усиление, редкость вещи — цвета редкости")] public TMP_Text Kind;
        [Tooltip("Белый знак вида награды в строке вида (красит редкость)")] public RawImage KindIcon;
        public TMP_Text Description;
        public TMP_Text ValueLabel;
        public TMP_Text Value;
        [Tooltip("Клавиша выбора в углу карточки (ставить через RunHudView.SetKey: длинная подпись — капсула)")] public TMP_Text Key;
        public WcRarity Rarity;

        /// <summary>Белый знак в круге меньше цветной картинки награды: вокруг поле, как у значков HUD.</summary>
        public const float GlyphScale = .62f;

        /// <summary>
        /// Большая картинка карточки. Рисунок награды (способность, вещь, артефакт) — свой цвет на весь
        /// круг; белый знак (путь арены) — краска текста темы и поле вокруг. Карточки общие для наград
        /// и выбора арены, поэтому ставится и то и другое.
        /// </summary>
        public void SetIcon(Texture icon, bool glyph)
        {
            if (Icon == null) return;
            Icon.texture = icon;
            Icon.enabled = icon != null;
            Icon.color = glyph ? UiTheme.Current.Get(UiTheme.Role.Text) : Color.white;
            Icon.rectTransform.localScale = Vector3.one * (glyph ? GlyphScale : 1f);
        }
    }
}
