using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шрифт текста по роли из <see cref="UiTheme"/>: заголовок (Philosopher) или
    /// текст (Nunito). Замена шрифта в теме меняет его во всех окнах сразу.
    /// Размер задаётся самим TMP_Text — его удобнее править на месте.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
    public sealed class ThemeFont : MonoBehaviour
    {
        public UiTheme.FontRole Role = UiTheme.FontRole.Body;

        void OnEnable()
        {
            UiTheme.Changed += Apply;
            Apply();
        }

        void OnDisable() => UiTheme.Changed -= Apply;

        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null) return;
            TMP_FontAsset font = UiTheme.Current.Get(Role);
            var text = GetComponent<TMP_Text>();
            if (font != null && text != null && text.font != font) text.font = font;
        }
    }
}
