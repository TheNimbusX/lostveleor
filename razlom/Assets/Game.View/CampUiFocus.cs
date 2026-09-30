using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    // При переключении с мыши на геймпад окно получает первый доступный фокус.
    // У уже выбранной кнопки фокус сохраняется, чтобы не сбивать навигацию по окну.
    internal static class CampUiFocus
    {
        internal static void Ensure(CanvasGroup group, Selectable fallback)
        {
            if (!TickDriver.GamepadLastUsed || group == null || !group.interactable) return;
            var system = EventSystem.current;
            if (system == null) return;
            var selected = system.currentSelectedGameObject;
            var control = selected != null ? selected.GetComponent<Selectable>() : null;
            if (control != null && control.IsActive() && control.IsInteractable()
                && selected.transform.IsChildOf(group.transform)) return;
            if (fallback == null || !fallback.IsActive() || !fallback.IsInteractable()
                || !fallback.transform.IsChildOf(group.transform))
            {
                fallback = null;
                foreach (var candidate in group.GetComponentsInChildren<Selectable>())
                    if (candidate.IsActive() && candidate.IsInteractable()) { fallback = candidate; break; }
            }
            if (fallback != null) fallback.Select();
        }
    }
}
