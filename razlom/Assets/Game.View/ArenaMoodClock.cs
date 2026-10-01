using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Ранний шаг света арены: LateUpdate раньше LayoutView. Новая арена (глубина или поколение сменились
    /// в тике или в корутине перехода — после Update) замечается здесь ДО начала её сборки: прежний свет
    /// откатывается, пока MeadowLighting ещё держит свет прежней арены, числа новой (<see cref="ArenaMood.Current"/>,
    /// <see cref="ArenaMood.EdgeShade"/>) готовы к расстановке, а пятно поляны считается на рабочем потоке
    /// всё время сборки под завесой. Сам свет ставит <see cref="ArenaMoodView"/> после сборки.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ArenaMoodClock : MonoBehaviour
    {
        internal ArenaMoodView View;

        private void LateUpdate()
        {
            if (View != null && View.isActiveAndEnabled) View.Prepare();
        }
    }
}
