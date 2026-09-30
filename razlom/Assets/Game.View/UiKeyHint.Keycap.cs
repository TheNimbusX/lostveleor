using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Один кейкап на всю игру (лист 5): тёмный скруглённый квадрат, кремовая буква Nunito, тонкая кромка. Собирает
    /// его UiInkKit.Keycap (дети «Дым», «Подложка», «Кольцо» — кромка, «Буква»); здесь — подгонка ширины под
    /// подпись, когда вид меняет клавишу (игрок переназначил, геймпад): одна буква — квадрат, длинная подпись
    /// («Esc», «Space», «ЛКМ») — шире, скругление то же. Раньше то же правило жило четырьмя копиями с разными полями
    /// (CombatHudView, CampServicesView, RunHudView, UiKeyBindingRow).
    /// </summary>
    public static partial class UiKeyHint
    {
        /// <summary>
        /// Ширина кейкапа по подписи <paramref name="letter"/>. Меняется узел «Клавиша» (сама буква в нём или в его
        /// «Плашке») либо узел с кромкой «Кольцо»; у запасных префабов другие имена — их не трогаем.
        /// Высота — из LayoutElement, иначе из прямоугольника узла.
        /// </summary>
        public static void FitKeycap(TMP_Text letter)
        {
            if (letter == null || !(letter.transform.parent is RectTransform cap)) return;
            RectTransform box = cap.name == "Клавиша" ? cap
                : cap.parent is RectTransform outer && outer.name == "Клавиша" ? outer
                : cap.Find("Кольцо") != null ? cap : null;
            if (box == null) return;
            var layout = box.GetComponent<LayoutElement>();
            float height = layout != null && layout.preferredHeight > 0f ? layout.preferredHeight
                : box.rect.height > 0f ? box.rect.height : box.sizeDelta.y;
            FitKeycap(box, letter, height);
        }

        /// <summary>
        /// Ширина узла <paramref name="box"/> (кейкап или его ячейка раскладки) по подписи: LayoutElement, если
        /// есть, иначе sizeDelta. <paramref name="height"/> — высота клавиши.
        /// </summary>
        public static void FitKeycap(RectTransform box, TMP_Text letter, float height, float maxRatio = UiTheme.KeycapMaxWidthRatio)
        {
            if (box == null || letter == null || height <= 0f) return;
            string text = letter.text ?? string.Empty;
            float textWidth = text.Length > 1 ? letter.GetPreferredValues(text).x - letter.margin.x - letter.margin.z : 0f;
            float width = UiTheme.KeycapWidth(Mathf.Max(0f, textWidth), height, text.Length, maxRatio);
            var layout = box.GetComponent<LayoutElement>();
            if (layout != null)
            {
                if (!Mathf.Approximately(layout.preferredWidth, width)) layout.preferredWidth = layout.minWidth = width;
            }
            else if (!Mathf.Approximately(box.sizeDelta.x, width)) box.sizeDelta = new Vector2(width, box.sizeDelta.y);
        }

        /// <summary>Новая подпись кейкапа и ширина под неё; та же подпись — ничего.</summary>
        public static void SetKeycap(TMP_Text letter, string key)
        {
            if (letter == null || letter.text == key) return;
            letter.text = key;
            FitKeycap(letter);
        }
    }
}
