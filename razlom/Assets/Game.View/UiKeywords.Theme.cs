using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Цвета ключевых слов из темы UI: роль слова (<see cref="UiKeywords.Tone"/>) → роль UiTheme.
    /// Отдельным файлом, потому что UiKeywords.cs собирается и в тестах без Unity, а тема — ScriptableObject.
    /// Правка цвета в теме подхватывается при следующей разметке; строки, размеченные раньше, держат старый цвет.
    /// </summary>
    public static partial class UiKeywords
    {
        /// <summary>Роль темы для слова. Цвета предварительные — вид подсказок ещё не выбран.</summary>
        public static UiTheme.Role ThemeRole(Tone tone)
        {
            switch (tone)
            {
                case Tone.Control: return UiTheme.Role.Rare;
                case Tone.Defense: return UiTheme.Role.Good;
                case Tone.Fire: return UiTheme.Role.Unique;
                case Tone.Resource: return UiTheme.Role.Lavidium;
                case Tone.Offense: return UiTheme.Role.Epic;
                default: return UiTheme.Role.Bad;
            }
        }

        /// <summary>«#RRGGBB» роли из текущей темы; без темы — запасная палитра.</summary>
        public static string ThemeHex(Tone tone)
        {
            UiTheme theme = UiTheme.Current;
            return theme == null ? DefaultHex(tone) : "#" + ColorUtility.ToHtmlStringRGB(theme.Get(ThemeRole(tone)));
        }

        static readonly Func<Tone, string> ThemeColors = ThemeHex;

        /// <summary><see cref="Markup"/> с цветами темы — то, что кладётся в TMP_Text.text.</summary>
        public static string Themed(string text, ICollection<Id> found = null, bool autoTag = true)
            => Markup(text, found, ThemeColors, autoTag);

        /// <summary>Определение слова для вложенной подсказки, с цветами темы.</summary>
        public static string ThemedDefinition(Id id, ICollection<Id> found = null) => DefinitionMarkup(id, found, ThemeColors);
    }
}
