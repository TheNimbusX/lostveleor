using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.View
{
    /// <summary>
    /// Строка окна настроек: какая это опция (<see cref="Setting"/>) — по ней панель справа показывает
    /// заголовок, описание и стандартное значение (SettingsCatalog). Наведение запоминает строку,
    /// выбранную отмечает огонёк (<see cref="Selected"/>, его включает PauseMenu).
    ///
    /// Щелчок по любому месту строки с тумблером переключает тумблер: круг в 48 единиц — маленькая
    /// цель, вся строка — большая. Свой файл обязателен: компонент стоит в префабе PauseMenuWc.
    /// </summary>
    public sealed class UiSettingRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public SettingId Setting;
        [Tooltip("Необязательно: тумблер строки — щелчок по строке переключает его")] public UiToggle Toggle;
        [Tooltip("Отметка выбранной строки (огонёк слева); включает PauseMenu")] public GameObject Selected;

        static UiSettingRow _hovered;

        /// <summary>Строка под курсором; null — курсор не над строкой настроек.</summary>
        public static UiSettingRow Hovered => _hovered != null && _hovered.isActiveAndEnabled ? _hovered : null;

        /// <summary>Щелчок по строке (не по её элементу со своим откликом).</summary>
        public static event Action<UiSettingRow> Clicked;

        public void OnPointerEnter(PointerEventData _) => _hovered = this;

        public void OnPointerExit(PointerEventData _)
        {
            if (_hovered == this) _hovered = null;
        }

        void OnDisable()
        {
            if (_hovered == this) _hovered = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke(this);
            if (Toggle != null && Toggle.Interactable && Toggle.isActiveAndEnabled) Toggle.OnPointerClick(eventData);
        }

        public void SetSelected(bool on)
        {
            if (Selected != null && Selected.activeSelf != on) Selected.SetActive(on);
        }
    }
}
