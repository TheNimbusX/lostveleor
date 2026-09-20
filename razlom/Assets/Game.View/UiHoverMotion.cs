using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Отклик кнопки, вкладки или строки на мышь.
    /// Наведение: подсветка-рамка плавно проявляется, элемент чуть растёт, и
    /// пока курсор на месте, рамка медленно «дышит». Нажатие: вспышка рамки и
    /// утапливание. Звуки — через <see cref="UiSound"/>. Числа правятся в инспекторе.
    /// Свой файл обязателен: компонент стоит в префабах (см. HudTooltipMetric).
    /// </summary>
    public sealed class UiHoverMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("Что увеличивать; пусто — сам объект")] public Transform Body;
        [Tooltip("Слой рамки-подсветки при наведении (необязательно)")] public Graphic Highlight;
        public float HoverScale = 1.03f;
        public float PressScale = 0.97f;
        public float Duration = 0.15f;

        [Header("Дыхание рамки при наведении")]
        public bool PulseWhileHovered = true;
        [Range(0f, 1f)] public float PulseMin = 0.55f;
        [Tooltip("Секунд на вдох-выдох")] public float PulsePeriod = 1.4f;

        [Header("Блик (необязательно)")]
        [Tooltip("Полоса блика под маской кнопки; пробегает при наведении")] public RectTransform Shine;
        public float ShineDuration = 0.45f;

        [Header("Звук")]
        public bool HoverSound = true;
        public UiSoundEvent ClickSound = UiSoundEvent.Click;
        [Tooltip("Без звука клика: его играет сам экран (вкладки, назад)")] public bool SilentClick;

        bool _inside;
        bool _pressed;
        float _hoverStarted;
        float _highlightAlpha;

        Transform Target => Body != null ? Body : transform;

        void OnEnable()
        {
            _inside = _pressed = false;
            Target.localScale = Vector3.one;
            SetHighlight(0f);
            if (Shine != null) Shine.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!_inside || _pressed || !PulseWhileHovered || Highlight == null) return;
            float since = UiMotion.Now - _hoverStarted;
            if (since < Duration) return;
            float wave = 0.5f + 0.5f * Mathf.Cos((since - Duration) * Mathf.PI * 2f / Mathf.Max(0.1f, PulsePeriod));
            SetHighlight(Mathf.Lerp(PulseMin, 1f, wave));
        }

        public void OnPointerEnter(PointerEventData _)
        {
            _inside = true;
            _hoverStarted = UiMotion.Now;
            Animate(HoverScale, 1f, Duration);
            RunShine();
            if (HoverSound && Interactable) UiSound.Play(UiSoundEvent.Hover);
        }

        public void OnPointerExit(PointerEventData _)
        {
            _inside = false;
            Animate(1f, 0f, Duration);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Interactable) return;
            _pressed = true;
            UiMotion.ScaleTo(Target, PressScale, 0.08f);
            SetHighlight(1f);
            if (!SilentClick) UiSound.Play(ClickSound);
        }

        public void OnPointerUp(PointerEventData _)
        {
            _pressed = false;
            _hoverStarted = UiMotion.Now - Duration;
            Animate(_inside ? HoverScale : 1f, _inside ? 1f : 0f, 0.12f);
        }

        bool Interactable
        {
            get
            {
                var selectable = GetComponent<Selectable>();
                return selectable == null || selectable.IsInteractable();
            }
        }

        void Animate(float scale, float highlight, float duration)
        {
            UiMotion.ScaleTo(Target, scale, duration);
            if (Highlight == null) return;
            float from = _highlightAlpha;
            UiMotion.Play(Highlight, 20, duration, t => SetHighlight(Mathf.LerpUnclamped(from, highlight, t)));
        }

        void SetHighlight(float alpha)
        {
            _highlightAlpha = alpha;
            if (Highlight != null) Highlight.canvasRenderer.SetAlpha(alpha);
        }

        void RunShine()
        {
            if (Shine == null || !(Shine.parent is RectTransform lane)) return;
            Shine.gameObject.SetActive(true);
            float width = lane.rect.width + Shine.rect.width;
            float start = -width * 0.5f, end = width * 0.5f;
            UiMotion.Play(Shine, 21, ShineDuration, t => Shine.anchoredPosition = new Vector2(Mathf.LerpUnclamped(start, end, t), 0f),
                AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), () => { if (Shine != null) Shine.gameObject.SetActive(false); });
        }
    }
}
