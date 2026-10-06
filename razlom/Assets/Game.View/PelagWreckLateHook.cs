using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Поздний кадр Крушения: пенная дуга пишет путь головы якоря ПОСЛЕ того, как её поставили
    /// риг якоря (порядок 1021) или прежний PelagAnchorSlamView (1020). Контроллер эффектов
    /// идёт раньше (1010) и видел бы голову прошлого кадра — на 24 м/с это 0,4 м отставания
    /// («след за когтем, не после»). Вешает PelagVfxController на свой объект в Awake.
    /// </summary>
    [DefaultExecutionOrder(1030)]
    [DisallowMultipleComponent]
    public sealed class PelagWreckLateHook : MonoBehaviour
    {
        public System.Action Late;

        private void LateUpdate() => Late?.Invoke();
    }
}
