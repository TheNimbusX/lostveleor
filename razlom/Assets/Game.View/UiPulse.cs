using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Мягкое «дыхание» прозрачности — свечение выбранного пункта меню.
    /// Идёт на реальном времени: в паузе Time.timeScale = 0.
    /// </summary>
    public sealed class UiPulse : MonoBehaviour
    {
        public Graphic Graphic;
        [Range(0f, 1f)] public float Min = 0.45f;
        [Range(0f, 1f)] public float Max = 1f;
        [Tooltip("Секунд на полный вдох-выдох")] public float Period = 1.6f;
        [Tooltip("Проявление при включении, секунд")] public float FadeIn = 0.18f;

        float _enabledAt;

        void OnEnable() => _enabledAt = UiMotion.Now;

        void Update()
        {
            if (Graphic == null) return;
            float wave = 0.5f + 0.5f * Mathf.Sin((UiMotion.Now - _enabledAt) * Mathf.PI * 2f / Mathf.Max(0.1f, Period) - Mathf.PI * 0.5f);
            float appear = FadeIn > 0f ? Mathf.Clamp01((UiMotion.Now - _enabledAt) / FadeIn) : 1f;
            Graphic.canvasRenderer.SetAlpha(Mathf.Lerp(Min, Max, wave) * appear);
        }
    }
}
