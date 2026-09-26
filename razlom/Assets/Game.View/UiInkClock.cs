using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Часы шейдера «Razlom/UI Ink» (_UiTime): реальное время интерфейса (<see cref="UiMotion.Now"/>).
    /// Встроенное _Time стоит в паузе (timeScale = 0), а дым в меню паузы должен течь.
    /// Один скрытый объект на всю игру, создаётся при первой группе «Дыма и света».
    /// </summary>
    public sealed class UiInkClock : MonoBehaviour
    {
        static readonly int UiTime = Shader.PropertyToID("_UiTime");
        static UiInkClock _clock;

        public static void Ensure()
        {
            if (_clock != null || !Application.isPlaying) return;
            var host = new GameObject("UI Ink Clock") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            _clock = host.AddComponent<UiInkClock>();
        }

        void Update() => Shader.SetGlobalFloat(UiTime, UiMotion.Now);
    }
}
