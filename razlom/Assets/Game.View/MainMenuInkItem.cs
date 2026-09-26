using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.View
{
    /// <summary>
    /// Пункт главного меню М1: текст без плашки (Hollow Knight). Под мышью надпись теплеет и
    /// отъезжает вправо, перед ней загорается огненный ромб, трещина вспыхивает. Главный пункт
    /// («Продолжить») держит ромб и тёплый цвет и в покое. Звук и лёгкое увеличение — UiHoverMotion.
    /// </summary>
    public sealed class MainMenuInkItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public TMP_Text Label;
        [Tooltip("Что отъезжает вправо под мышью: надпись с ромбом и строкой (не сама надпись — её поля двигает MainMenuPanel)")]
        public RectTransform Body;
        [Tooltip("Огненный ромб перед надписью (свет)")] public CanvasGroup Marker;
        public MainMenuRift Rift;
        [Tooltip("Главный пункт: ромб и тёплый цвет и без мыши")] public bool Featured;
        [Tooltip("На сколько отъезжает надпись под мышью, единицы Canvas")] public float Shift = 12f;
        public float Duration = .18f;
        public Color Rest = new Color(.9f, .92f, .95f, 1f);
        public Color Warm = new Color(1f, .72f, .5f, 1f);

        Vector2 _bodyRest;
        bool _known, _hover;
        float _k, _lastNow = -1f;

        void OnEnable()
        {
            if (Body != null && !_known) { _bodyRest = Body.anchoredPosition; _known = true; }
            _lastNow = -1f;
            Apply(true);
        }

        public void OnPointerEnter(PointerEventData e) => SetHover(true);
        public void OnPointerExit(PointerEventData e) => SetHover(false);
        public void OnSelect(BaseEventData e) => SetHover(true);
        public void OnDeselect(BaseEventData e) => SetHover(false);

        void SetHover(bool hover)
        {
            if (hover == _hover) return;
            _hover = hover;
            if (hover && Rift != null) Rift.Flare(Featured ? 1f : .7f);
        }

        void Update() => Apply(false);

        void Apply(bool instant)
        {
            float now = UiMotion.Now;
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            float target = _hover ? 1f : 0f;
            _k = instant ? target : Mathf.MoveTowards(_k, target, dt / Mathf.Max(.02f, Duration));
            float k = _k * _k * (3f - 2f * _k);
            float warm = Featured ? Mathf.Lerp(.75f, 1f, k) : k;
            if (Label != null) Label.color = Color.Lerp(Rest, Warm, warm);
            if (Body != null && _known) Body.anchoredPosition = _bodyRest + new Vector2(Shift * k, 0f);
            if (Marker != null) Marker.alpha = Featured ? Mathf.Lerp(.8f, 1f, k) : k;
        }
    }
}
