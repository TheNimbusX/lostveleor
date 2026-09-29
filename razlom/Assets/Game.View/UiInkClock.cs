using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Часы шейдера «Razlom/UI Ink» (_UiTime): реальное время интерфейса (<see cref="UiMotion.Now"/>).
    /// Встроенное _Time стоит в паузе (timeScale = 0), а дым в меню паузы должен течь.
    /// Один скрытый объект на всю игру, создаётся при первой группе «Дыма и света».
    ///
    /// Шаг часов не больше 0,1 с за кадр — как у углей и клубов завесы: долгий кадр (сборка арены,
    /// загрузка) иначе останавливал течение дыма, а на следующем кадре оно перепрыгивало вперёд
    /// (поток T1, 29.09: «дым не замирает и не прыгает»). Шейдер берёт из часов только течение и
    /// пульс, начала появлений идут своими часами, поэтому отставание от UiMotion.Now ничего не ломает.
    /// </summary>
    public sealed class UiInkClock : MonoBehaviour
    {
        static readonly int UiTime = Shader.PropertyToID("_UiTime");
        static UiInkClock _clock;

        const float MaxStep = .1f;
        float _time, _last = -1f;

        public static void Ensure()
        {
            if (_clock != null || !Application.isPlaying) return;
            var host = new GameObject("UI Ink Clock") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            _clock = host.AddComponent<UiInkClock>();
        }

        void Update()
        {
            float now = UiMotion.Now;
            if (_last < 0f) _time = now;
            else _time += Mathf.Clamp(now - _last, 0f, MaxStep);
            _last = now;
            Shader.SetGlobalFloat(UiTime, _time);
        }
    }
}
