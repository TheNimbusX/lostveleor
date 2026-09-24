using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Тумблер: две нарисованные картинки «вкл» и «выкл» плавно сменяют друг
    /// друга, при нажатии тумблер чуть утапливается. Вид — в префабе.
    /// </summary>
    public sealed class UiToggle : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Graphic On;
        public Graphic Off;
        [Tooltip("Необязательно: ручка, которая ездит между KnobOff и KnobOn по X")] public RectTransform Knob;
        public float KnobOff = -16f;
        public float KnobOn = 16f;
        [Tooltip("Секунд на смену положения")]
        public float Duration = 0.16f;
        public float PressScale = 0.95f;
        public bool Interactable = true;

        /// <summary>Игрок нажал: новое желаемое значение.</summary>
        public event Action<bool> Changed;

        bool _value;
        bool _shown;

        public bool Value => _value;

        void OnDisable() => _shown = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable || eventData.button != PointerEventData.InputButton.Left) return;
            UiSound.Play(UiSoundEvent.Toggle);
            Changed?.Invoke(!_value);
        }

        public void OnPointerDown(PointerEventData _) { if (Interactable) UiMotion.ScaleTo(transform, PressScale, 0.08f); }
        public void OnPointerUp(PointerEventData _) => UiMotion.ScaleTo(transform, 1f, 0.12f);

        public void SetValue(bool value)
        {
            if (_shown && value == _value) return;
            float duration = _shown ? Duration : 0f;
            _value = value;
            _shown = true;
            if (On != null) { On.gameObject.SetActive(true); On.CrossFadeAlpha(value ? 1f : 0f, duration, true); }
            if (Off != null) { Off.gameObject.SetActive(true); Off.CrossFadeAlpha(value ? 0f : 1f, duration, true); }
            if (Knob != null)
            {
                var target = new Vector2(value ? KnobOn : KnobOff, Knob.anchoredPosition.y);
                if (duration > 0f) UiMotion.MoveTo(Knob, target, duration); else Knob.anchoredPosition = target;
            }
        }
    }
}
