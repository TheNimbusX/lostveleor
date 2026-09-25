using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Значок действующего эффекта зелья над героем (Живица, Порыв). Владелец, 24 сентября:
    /// «значки эффектов не особо понятны, что дают, без наведения» — поэтому рядом с кругом
    /// всегда написано, что эффект делает, и сколько секунд осталось; кольцо вокруг значка
    /// убывает вместе со временем. Последняя секунда мигает. Время берётся из симуляции.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudBuffChip : MonoBehaviour
    {
        public CanvasGroup Group;
        [Tooltip("Кольцо-таймер: Image.Type.Filled, радиальное")] public Image Ring;
        [Tooltip("Что даёт эффект: «−25% получаемого урона»")] public TMP_Text Effect;
        [Tooltip("Название и секунды: «Живица · 4 с»")] public TMP_Text Title;
        [Tooltip("Необязательно: вспышка света за кругом, когда эффект только что выпит (аддитивная)")] public Image Glow;
        [Tooltip("Необязательно: круг со значком — вздрагивает, когда эффект только что выпит")] public RectTransform Circle;
        public string Name = "Эффект";
        public string EffectText = "";
        public float Fade = .2f;

        float _alpha;
        int _seconds = -1, _ticks;
        bool _on;

        void Awake()
        {
            if (Effect != null) Effect.text = EffectText;
        }

        /// <summary>ticksLeft ≤ 0 — эффекта нет: значок угасает и сам выключается, место в ряду освобождается.</summary>
        public void Set(int ticksLeft, int fullTicks, int ticksPerSecond)
        {
            // Только что выпит или выпит заново (таймер вырос) — круг вздрагивает, за ним вспышка.
            bool fresh = ticksLeft > 0 && (!_on || ticksLeft > _ticks);
            _ticks = ticksLeft;
            _on = ticksLeft > 0;
            if (!_on) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (fresh)
            {
                HudFx.Punch(Circle != null ? Circle : transform, 1.35f, .4f);
                HudFx.Flash(Glow, .9f, .6f);
            }
            if (Ring != null) Ring.fillAmount = Mathf.Clamp01(ticksLeft / (float)Mathf.Max(1, fullTicks));
            int seconds = Mathf.CeilToInt(ticksLeft / (float)Mathf.Max(1, ticksPerSecond));
            if (seconds != _seconds && Title != null)
            {
                _seconds = seconds;
                Title.text = Name + " · " + seconds + " с";
            }
            if (Effect != null && Effect.text != EffectText) Effect.text = EffectText;
        }

        void LateUpdate()
        {
            _alpha = Mathf.MoveTowards(_alpha, _on ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.02f, Fade));
            float blink = _on && _seconds <= 1 ? .65f + .35f * Mathf.Cos(Time.unscaledTime * 12f) : 1f;
            if (Group != null) Group.alpha = _alpha * blink;
            if (!_on && _alpha <= 0f) gameObject.SetActive(false);
        }
    }
}
