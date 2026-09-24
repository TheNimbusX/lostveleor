using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Состояния кнопки пака через роли темы: обычное, наведение, нажатие, недоступно.
    /// Вместо встроенного ColorTint у Button: тот умножает цвет и спорит с
    /// <see cref="ThemeColor"/>. Роли каждого состояния правятся в инспекторе.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ThemeStates : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public ThemeColor Target;
        public UiTheme.Role Normal = UiTheme.Role.Accent;
        public UiTheme.Role Hover = UiTheme.Role.AccentHover;
        public UiTheme.Role Pressed = UiTheme.Role.AccentPressed;
        public UiTheme.Role Disabled = UiTheme.Role.Disabled;
        [Tooltip("Надпись: гаснет у недоступной кнопки")] public ThemeColor Label;

        Selectable _selectable;
        bool _hover, _down, _lastInteractable = true;

        void OnEnable()
        {
            _selectable = GetComponent<Selectable>();
            Apply();
        }

        void Update()
        {
            bool interactable = _selectable == null || _selectable.IsInteractable();
            if (interactable != _lastInteractable) Apply();
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Apply(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Apply(); }

        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null || Target == null) return;
            if (_selectable == null) _selectable = GetComponent<Selectable>();
            _lastInteractable = _selectable == null || _selectable.IsInteractable();
            UiTheme.Role role = !_lastInteractable ? Disabled : _down ? Pressed : _hover ? Hover : Normal;
            Target.SetRole(role);
            if (Label != null) Label.Alpha = _lastInteractable ? 1f : .45f;
            if (Label != null) Label.Apply();
        }
    }
}
