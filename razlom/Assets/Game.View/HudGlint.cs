using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Проблеск: светлая косая полоса пробегает по элементу (полоса опыта, лавидий, медальон,
    /// баннер уровня). Объект — прямоугольник с RectMask2D, полоса внутри него. По вызову
    /// <see cref="Play"/> или сам раз в <see cref="Every"/> секунд, пока включён <see cref="Repeat"/>.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudGlint : MonoBehaviour
    {
        public Graphic Stripe;
        public float Duration = .6f;
        [Range(0f, 1f)] public float Peak = .55f;
        [Tooltip("Повтор, секунды, пока Repeat включён; 0 — только по Play")] public float Every;
        [Tooltip("Повторять сам (лавидий полон, медальон артефакта)")] public bool Repeat;
        [Tooltip("Кадр ставит хозяин (баннер уровня) через Apply — свои часы не идут")] public bool Driven;

        float _start = -100f, _next;

        void OnEnable() => _next = UiMotion.Now + Random.Range(.5f, 1f) * Mathf.Max(1f, Every);

        public void Play() => _start = UiMotion.Now;

        void LateUpdate()
        {
            if (Driven) return;
            float now = UiMotion.Now;
            if (Repeat && Every > 0f && now >= _next)
            {
                _start = now;
                _next = now + Every;
            }
            Apply((now - _start) / Mathf.Max(.05f, Duration));
        }

        /// <summary>Положение полосы на доле пробега 0…1; вне отрезка полосы не видно.</summary>
        public void Apply(float k)
        {
            if (Stripe == null) return;
            bool on = k > 0f && k < 1f;
            Stripe.enabled = on;
            if (!on) return;
            var box = (RectTransform)transform;
            RectTransform stripe = Stripe.rectTransform;
            float travel = box.rect.width * .5f + stripe.rect.width;
            float eased = 1f - (1f - k) * (1f - k);
            stripe.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, eased), 0f);
            HudFx.SetAlpha(Stripe, Peak * Mathf.Sin(k * Mathf.PI));
        }
    }
}
