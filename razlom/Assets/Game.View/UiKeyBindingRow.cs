using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Строка назначения клавиши: название действия и клавиша-плашка. Пока
    /// игра ждёт новую клавишу, плашка «дышит» свечением. Строки создаются
    /// из шаблона в префабе — вид правится на шаблоне.
    /// </summary>
    public sealed class UiKeyBindingRow : MonoBehaviour
    {
        public TMP_Text Action;
        public Button Key;
        public TMP_Text KeyLabel;
        [Tooltip("Свечение плашки в ожидании клавиши (с UiPulse)")]
        public GameObject Waiting;
        public Color KeyDefault = Color.white;
        [Tooltip("Цвет подписи, если клавиша назначена игроком, а не раскладкой")]
        public Color KeyCustom = new Color32(0xFF, 0xB4, 0xA8, 0xFF);

        [Tooltip("Подсветка строки: вспыхивает, когда эта строка отдала клавишу при обмене")]
        public Graphic FlashGraphic;
        public float FlashDuration = 0.6f;

        [HideInInspector] public GameAction Binding;

        public void Flash()
        {
            if (FlashGraphic == null) return;
            FlashGraphic.gameObject.SetActive(true);
            UiMotion.Play(FlashGraphic, 30, FlashDuration, t =>
                FlashGraphic.canvasRenderer.SetAlpha(t < 0.25f ? t / 0.25f : 1f - (t - 0.25f) / 0.75f), AnimationCurve.Linear(0f, 0f, 1f, 1f));
        }

        /// <summary>Нажата плашка клавиши.</summary>
        public event Action<UiKeyBindingRow> Clicked;

        void Awake()
        {
            if (Key != null) Key.onClick.AddListener(() => Clicked?.Invoke(this));
            if (Waiting != null) Waiting.SetActive(false);
        }

        public void Show(string action, string key, bool waiting, bool custom)
        {
            if (Action != null && Action.text != action) Action.text = action;
            string label = waiting ? "…" : key;
            if (KeyLabel != null)
            {
                if (KeyLabel.text != label) KeyLabel.text = label;
                KeyLabel.color = custom && !waiting ? KeyCustom : KeyDefault;
            }
            if (Waiting != null && Waiting.activeSelf != waiting)
            {
                Waiting.SetActive(waiting);
                if (waiting && Key != null) UiMotion.ScaleTo(Key.transform, 1.06f, 0.12f);
                else if (Key != null) UiMotion.ScaleTo(Key.transform, 1f, 0.12f);
            }
        }
    }
}
