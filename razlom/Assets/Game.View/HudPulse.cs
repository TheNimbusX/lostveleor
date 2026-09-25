using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Медленная пульсация «внимание»: красная дымка у героя при низком здоровье, мягкий свет
    /// на банке Живицы, когда её пора пить, сияние камня с восемью усилениями. Пока
    /// <see cref="Active"/> выключен, свет плавно гаснет и не рисуется.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudPulse : MonoBehaviour
    {
        public Graphic Target;
        public bool Active;
        [Range(0f, 1f)] public float Min = .15f;
        [Range(0f, 1f)] public float Max = .5f;
        [Tooltip("Секунд на один вдох")] public float Period = 1.2f;
        [Tooltip("Секунд на включение и угасание")] public float Fade = .3f;
        [Tooltip("Насколько свет подрастает на вдохе")] public float Grow = .06f;

        float _weight;

        void LateUpdate()
        {
            if (Target == null) return;
            _weight = Mathf.MoveTowards(_weight, Active ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.02f, Fade));
            Target.enabled = _weight > 0f;
            if (_weight <= 0f) return;
            float breath = .5f + .5f * Mathf.Sin(UiMotion.Now * Mathf.PI * 2f / Mathf.Max(.1f, Period));
            HudFx.SetAlpha(Target, _weight * Mathf.Lerp(Min, Max, breath));
            Target.rectTransform.localScale = Vector3.one * (1f + Grow * breath);
        }
    }
}
