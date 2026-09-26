using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Реплика NPC в окне лавки (префаб CampShopsWc): облачко дыма над головой портрета,
    /// с хвостиком к нему и именем на нити света. Текст ставит CampServicesView (поле
    /// Message окна); пустой текст прячет облачко. Новая реплика: облачко мягко
    /// выпрыгивает, буквы печатаются одна за другой. Время неигровое.
    /// </summary>
    public sealed class CampSpeechBubble : MonoBehaviour
    {
        public CanvasGroup Group;
        public TMP_Text Speaker;
        public TMP_Text Line;
        [Tooltip("Что выпрыгивает (само облачко)")] public RectTransform Body;
        [Tooltip("Секунд на проявление новой реплики")] public float FadeIn = .18f;
        [Tooltip("Букв в секунду")] public float LettersPerSecond = 55f;
        [Tooltip("Насколько облачко меньше в начале выпрыгивания")] public float PopFrom = .9f;
        [Tooltip("Проявление дыма облачка («Дым и свет»): заново, когда реплика появляется после пустоты. Пусто — нет")]
        public UiInkGroup Ink;

        string _shown = "";
        float _since;

        void OnEnable()
        {
            _shown = "";
            Apply(true);
        }

        void LateUpdate() => Apply(false);

        void Apply(bool instant)
        {
            if (Line == null || Group == null) return;
            string text = Line.text ?? "";
            if (text != _shown)
            {
                // Облачко возникает из пустоты — дым растекается заново (с окном его проявляет включение
                // группы). Смена одной реплики на другую — только печать по буквам.
                if (!instant && _shown.Length == 0 && text.Length > 0 && Ink != null && Ink.isActiveAndEnabled) Ink.Show();
                _shown = text;
                _since = 0f;
            }
            _since += Time.unscaledDeltaTime;
            bool empty = text.Length == 0;
            float appear = empty ? 0f : instant ? 1f : Mathf.Clamp01(_since / Mathf.Max(.01f, FadeIn));
            Group.alpha = appear;
            if (Body != null)
            {
                // Выпрыгивание с лёгким перелётом: .9 → 1.03 → 1.
                float t = Mathf.Clamp01(_since / .28f);
                float s = empty || instant ? 1f : Mathf.LerpUnclamped(PopFrom, 1f, 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f));
                Body.localScale = Vector3.one * s;
            }
            int total = Line.textInfo != null ? Line.textInfo.characterCount : int.MaxValue;
            Line.maxVisibleCharacters = instant || empty ? 99999 : Mathf.Min(99999, Mathf.FloorToInt(_since * LettersPerSecond));
            if (total > 0 && Line.maxVisibleCharacters >= total) Line.maxVisibleCharacters = 99999;
        }
    }
}
