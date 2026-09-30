using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Единый набор в боевом HUD (лист 5 «один язык», выбор владельца 30.09: «5 ок» — эталон для единообразия):
    /// * один кейкап — тёмный скруглённый квадрат, кремовая буква, тонкая кромка (UiInkKit.Keycap / KitKeycap) вместо
    ///   круга в дыме: клавиши плиток способностей, зелий, артефакта, шапки подсказки, «[Alt] Подробнее»;
    /// * подсказка клавиши — один формат «[Alt] Подробнее» (глагол с большой буквы, ступень Caption);
    /// * ключевые слова в подсказке способности и артефакта подсвечены, рядом — вложенная подсказка слова, та же, что
    ///   на экране награды (UiKeywordTip).
    /// Плашка недостачи лавидия на плитке («Нет лавидия») — не клавиша: остаётся капсулой.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        const float KeywordTipWidth = 320f;

        /// <summary>
        /// Вложенная подсказка слова — ребёнок подсказки способности вне её раскладки: прячется и ездит вместе с ней,
        /// сторону (справа, у края экрана — слева) выбирает CombatHudView.PlaceKeywordTip.
        /// </summary>
        static RectTransform BuildKeywordTip(RectTransform tooltip, CombatHudView view)
        {
            RectTransform keyword = UiInkKit.KeywordTip(tooltip, "Ключевое слово", KeywordTipWidth, out view.KeywordTipTitle, out view.KeywordTipBody);
            keyword.anchorMin = keyword.anchorMax = new Vector2(1f, 1f);
            keyword.pivot = new Vector2(0f, 1f);
            keyword.anchoredPosition = new Vector2(view.KeywordTipGap, 0f);
            var element = keyword.GetComponent<LayoutElement>();
            if (element == null) element = keyword.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            view.KeywordTip = keyword;
            return keyword;
        }

        /// <summary>v3 — единый набор: кейкапы листа 5, «[Alt] Подробнее», вложенная подсказка ключевого слова.</summary>
        static void MigrateTo3(GameObject root, CombatHudView view)
        {
            int caps = UiInkKit.RestyleKeycaps(root.transform, "Нет лавидия");
            Debug.Log("[ui-kit] Боевой HUD: кейкапов листа 5 — " + caps + ".");

            if (view.TooltipDetailHint != null)
                foreach (TMP_Text label in view.TooltipDetailHint.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.name == "Буква") continue;
                    label.text = UiKeyHint.Verb(label.text);
                    label.fontSize = T.Size(UiTheme.TextStep.Caption);
                    // Ширина подписи в строке — по новому тексту и размеру.
                    var element = label.GetComponent<LayoutElement>();
                    if (element != null && element.preferredWidth > 0f) element.preferredWidth = element.minWidth = label.GetPreferredValues(label.text).x + 2f;
                }
            else Debug.LogWarning("[ui-kit] В подсказке способности нет «Подсказка Alt» — подпись не приведена к «[Alt] Подробнее»");

            if (view.KeywordTip == null)
            {
                if (view.Tooltip != null) BuildKeywordTip(view.Tooltip, view);
                else Debug.LogWarning("[ui-kit] В боевом HUD нет подсказки способности — вложенная подсказка слова не собрана");
            }
        }
    }
}
