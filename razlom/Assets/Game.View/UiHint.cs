using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.View
{
    /// <summary>
    /// Короткое описание элемента при наведении («Тени — дальность и чёткость
    /// теней»). Текст правится в инспекторе; показывает его экран — меню паузы
    /// пишет <see cref="Current"/> в строку статуса.
    /// </summary>
    public sealed class UiHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [TextArea(1, 3)] public string Text;

        static UiHint _shown;

        /// <summary>Описание под курсором; null — курсор не на элементе с описанием.</summary>
        public static string Current => _shown != null && _shown.isActiveAndEnabled ? _shown.Text : null;

        public void OnPointerEnter(PointerEventData _) => _shown = this;

        public void OnPointerExit(PointerEventData _)
        {
            if (_shown == this) _shown = null;
        }

        void OnDisable()
        {
            if (_shown == this) _shown = null;
        }
    }
}
