using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Плавная полоса ресурса в боевом HUD (здоровье, лавидий). Заливка догоняет новое
    /// значение: потеря — быстро, прибавка — мягко. При потере светлый «след» держит
    /// прежнее значение и через паузу стекает к новому. Долю задаёт CombatHudView через
    /// <see cref="Target"/>; время неигровое, числа правятся в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudBarAnim : MonoBehaviour
    {
        public RectTransform Fill;
        [Tooltip("Светлый след недавней потери (необязательно)")] public RectTransform Trail;
        [Tooltip("Скорость потери, долей полосы в секунду")] public float DropSpeed = 3f;
        [Tooltip("Скорость прибавки, долей полосы в секунду")] public float RiseSpeed = .9f;
        [Tooltip("Пауза перед тем, как след начнёт стекать, секунды")] public float TrailDelay = .45f;
        [Tooltip("Скорость стекания следа, долей полосы в секунду")] public float TrailSpeed = .7f;

        [System.NonSerialized] public float Target = -1f;
        float _shown = -1f, _trail = -1f, _hold;

        void LateUpdate()
        {
            if (Fill == null || Target < 0f) return;
            float dt = Time.unscaledDeltaTime;
            if (_shown < 0f) _shown = _trail = Target;
            // Скачок больше полосы за кадр (новый забег, смена героя) — без анимации.
            if (Mathf.Abs(Target - _shown) > .999f) _shown = _trail = Target;

            if (Target < _shown)
            {
                if (_trail < _shown) _trail = _shown;
                _hold = TrailDelay;
                _shown = Mathf.MoveTowards(_shown, Target, DropSpeed * dt);
            }
            else _shown = Mathf.MoveTowards(_shown, Target, RiseSpeed * dt);

            if (_hold > 0f) _hold -= dt;
            else _trail = Mathf.MoveTowards(_trail, _shown, TrailSpeed * dt);
            if (_trail < _shown) _trail = _shown;

            Vector2 max = Fill.anchorMax;
            Fill.anchorMax = new Vector2(_shown, max.y);
            bool visible = _shown > .001f;
            if (Fill.gameObject.activeSelf != visible) Fill.gameObject.SetActive(visible);
            if (Trail != null)
            {
                bool trail = _trail - _shown > .002f;
                if (Trail.gameObject.activeSelf != trail) Trail.gameObject.SetActive(trail);
                if (trail)
                {
                    Trail.anchorMin = new Vector2(Mathf.Max(0f, _shown - .02f), 0f);
                    Trail.anchorMax = new Vector2(_trail, 1f);
                    Trail.offsetMin = Trail.offsetMax = Vector2.zero;
                }
            }
        }
    }
}
