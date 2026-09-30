using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ВЛОЖЕННАЯ ПОДСКАЗКА КЛЮЧЕВОГО СЛОВА — одна на все описания (экран награды, подсказка способности и артефакта
    /// боевого HUD). Правило экрана награды (кадр 3a, поток R): в описании слова подсвечены (UiKeywords), рядом
    /// с подсказкой — вложенная с определением первого слова описания или того, что под мышью. Название — цветом
    /// слова, определение — со своими подсвеченными словами; без кромки и огня (подсказки без кромки).
    ///
    /// Узел подсказки собирает UiInkKit.KeywordTip (малая подложка «Дыма и света», «Название», «Определение»).
    /// Тексты пишутся только при смене слова — не каждый кадр.
    /// </summary>
    public static class UiKeywordTip
    {
        /// <summary>
        /// Показать определение слова <paramref name="id"/>. Слова нет — подсказка прячется, false.
        /// <paramref name="shown"/> — какое слово показано сейчас (хранит вид): то же слово — тексты не трогаются.
        /// </summary>
        public static bool Show(RectTransform tip, TMP_Text title, TMP_Text body, UiKeywords.Id id, ref UiKeywords.Id shown)
        {
            if (tip == null) return false;
            UiKeywords.Entry entry = UiKeywords.Get(id);
            if (entry == null)
            {
                Hide(tip, ref shown);
                return false;
            }
            bool changed = id != shown;
            shown = id;
            if (changed)
            {
                if (title != null && title.text != entry.Title) title.text = entry.Title;
                if (body != null) body.text = UiKeywords.ThemedDefinition(id);
            }
            bool reopen = !tip.gameObject.activeSelf;
            if (reopen) tip.gameObject.SetActive(true);
            // Название — цветом слова, после включения (краска темы на включении вернула бы обычный).
            if (title != null) title.color = UiTheme.Current.Get(UiKeywords.ThemeRole(entry.Tone));
            if (!reopen && changed)
            {
                var ink = tip.GetComponent<UiInkGroup>();
                if (ink != null) ink.Show();
            }
            return true;
        }

        /// <summary>Спрятать вложенную подсказку.</summary>
        public static void Hide(RectTransform tip, ref UiKeywords.Id shown)
        {
            shown = UiKeywords.Id.None;
            if (tip != null && tip.gameObject.activeSelf) tip.gameObject.SetActive(false);
        }

        /// <summary>
        /// Слово под точкой экрана <paramref name="pointer"/> в тексте <paramref name="text"/> (ссылка «kw:…»
        /// разметки UiKeywords); не на слове — <paramref name="fallback"/>. <paramref name="lastLink"/> — номер
        /// ссылки прошлого кадра: та же ссылка — ответ без разбора id.
        /// </summary>
        public static UiKeywords.Id Pointed(TMP_Text text, Vector2 pointer, UiKeywords.Id fallback, ref int lastLink, ref UiKeywords.Id lastPointed)
        {
            if (text == null) return fallback;
            int link = TMP_TextUtilities.FindIntersectingLink(text, pointer, null);
            if (link != lastLink)
            {
                lastLink = link;
                lastPointed = link >= 0 && link < text.textInfo.linkCount
                    && UiKeywords.TryParseLink(text.textInfo.linkInfo[link].GetLinkID(), out UiKeywords.Id id) ? id : UiKeywords.Id.None;
            }
            return lastPointed != UiKeywords.Id.None ? lastPointed : fallback;
        }

        /// <summary>
        /// Описание с подсвеченными словами для TMP и первое слово в нём (для вложенной подсказки по умолчанию).
        /// <paramref name="found"/> — черновик вида (очищается здесь).
        /// </summary>
        public static string Markup(string text, List<UiKeywords.Id> found, out UiKeywords.Id first)
        {
            first = UiKeywords.Id.None;
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            found?.Clear();
            string markup = UiKeywords.Themed(text, found);
            if (found != null && found.Count > 0) first = found[0];
            return markup;
        }
    }
}
