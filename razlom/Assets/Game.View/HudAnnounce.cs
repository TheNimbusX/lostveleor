using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Узкий баннер сверху по центру — концепт 2Б (владелец, 25 сентября): скошенная плашка пака,
    /// «РАЗЛОМ ЗАЧИЩЕН» антиквой с ромбами по бокам и строка под ней. Сползает сверху, держится и гаснет; новый
    /// показ перебивает старый. Часы свои, шаг не больше 0,1 с за кадр.
    /// </summary>
    public sealed class HudAnnounce : MonoBehaviour
    {
        public CanvasGroup Group;
        public TMP_Text Title;
        public TMP_Text Line;
        [Tooltip("Ромбы по бокам надписи: встают вплотную к её ширине")] public RectTransform LeftMark, RightMark;
        [Tooltip("Зазор между надписью и ромбом")] public float MarkGap = 16f;
        public float InTime = .3f, HoldTime = 2.4f, OutTime = .5f;
        [Tooltip("Откуда сползает, по Y")] public float DropFrom = 26f;

        Vector2 _rest;
        bool _restKnown;
        float _t = 100f, _lastNow;

        float Total => InTime + HoldTime + OutTime;

        public void Show(string title, string line)
        {
            if (!_restKnown) { _rest = ((RectTransform)transform).anchoredPosition; _restKnown = true; }
            if (Title != null)
            {
                Title.text = title;
                float half = Title.GetPreferredValues(title).x * .5f + MarkGap;
                if (LeftMark != null) LeftMark.anchoredPosition = new Vector2(-half, LeftMark.anchoredPosition.y);
                if (RightMark != null) RightMark.anchoredPosition = new Vector2(half, RightMark.anchoredPosition.y);
            }
            if (Line != null) { Line.text = line ?? string.Empty; Line.gameObject.SetActive(!string.IsNullOrEmpty(line)); }
            _t = 0f;
            _lastNow = UiMotion.Now;
            gameObject.SetActive(true);
            Apply(0f);
        }

        /// <summary>Кадр редактора: баннер в покое.</summary>
        public void Preview(string title, string line)
        {
            Show(title, line);
            Apply(InTime + .5f);
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            _t += Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            if (_t > Total) { gameObject.SetActive(false); return; }
            Apply(_t);
        }

        void Apply(float t)
        {
            float enter = UiMotion.EaseOut.Evaluate(Mathf.Clamp01(t / InTime));
            float leave = Mathf.Clamp01((t - InTime - HoldTime) / OutTime);
            if (Group != null) Group.alpha = enter * (1f - leave);
            ((RectTransform)transform).anchoredPosition = _rest + new Vector2(0f, DropFrom * (1f - enter) + 10f * leave);
        }
    }
}
