using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.View
{
    /// <summary>Передаёт наведение мыши владельцу окна: строки статов, зелья, ячейки атласа.</summary>
    public sealed class CampHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> Hover;
        public void OnPointerEnter(PointerEventData e) => Hover?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Hover?.Invoke(false);
        void OnDisable() => Hover?.Invoke(false);
    }
}
