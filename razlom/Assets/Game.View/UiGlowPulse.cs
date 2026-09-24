using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Живое свечение «готово» у плиток боевого HUD: мягкое дыхание яркости и размера
    /// ореола, у соседних плиток вразнобой; при появлении (способность перезарядилась) —
    /// короткая вспышка. Время неигровое: пауза и замедление на свечение не влияют.
    /// Все числа правятся в префабе CombatHudWc на объекте «Готово».
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiGlowPulse : MonoBehaviour
    {
        public CanvasGroup Group;
        [Tooltip("Ореол, который слегка дышит размером")] public RectTransform Glow;
        [Range(0f, 1f)] public float Min = .6f;
        [Range(0f, 1f)] public float Max = 1f;
        [Tooltip("Секунд на вдох-выдох")] public float Period = 2.4f;
        [Tooltip("Насколько ореол дышит размером")] public float GlowScale = .06f;
        [Tooltip("Длина вспышки при появлении, секунды")] public float PopTime = .4f;
        [Tooltip("Насколько ореол раздувается во вспышке")] public float PopScale = .22f;

        float _born, _phase;

        void OnEnable()
        {
            _born = Time.unscaledTime;
            // Сдвиг по положению на экране: соседние плитки дышат не в такт.
            _phase = Mathf.Repeat(transform.position.x * .0023f, 1f);
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            float t = Time.unscaledTime;
            float breath = .5f + .5f * Mathf.Sin((t / Mathf.Max(.1f, Period) + _phase) * Mathf.PI * 2f);
            float pop = 1f - Mathf.Clamp01((t - _born) / Mathf.Max(.01f, PopTime));
            pop *= pop;
            if (Group != null) Group.alpha = Mathf.Min(1f, Mathf.Lerp(Min, Max, breath) + pop);
            if (Glow != null) Glow.localScale = Vector3.one * (1f + GlowScale * breath + PopScale * pop);
        }
    }
}
