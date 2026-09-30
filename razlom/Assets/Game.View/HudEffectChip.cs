using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Круг эффекта героя в строке над портретом (выбор владельца 30.09 — кадр 1a, из 1b — секунды
    /// и стаки в углу значка). Тёмный диск, значок, тонкое кольцо-таймер, которое убывает; в правом
    /// нижнем углу — кружок с секундами, в правом верхнем — «×2», когда эффект наложен дважды.
    /// Кромки мазком туши нет (1b, владелец: «похожа на кровь»).
    ///
    /// Круг сам ничего не решает: когда появиться, обновиться и уйти, говорит
    /// <see cref="HudEffectRow"/>; здесь только вид и короткие движения на реальном времени
    /// (<see cref="UiMotion.Now"/>): появление — клуб дыма и «выпрыгивание», обновление — лёгкий
    /// толчок, уход — одна вспышка и угасание. Вспышки слабеют с «Вспышки и мерцание».
    /// Мышь круг не ловит (Raycast выключен): клик по нему уходит в игру.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudEffectChip : MonoBehaviour
    {
        [Tooltip("Всё, что масштабируется при появлении и уходе: диск, значок, кольцо, углы")]
        public RectTransform Body;
        public CanvasGroup Group;
        [Tooltip("Значок эффекта; у артефакта — картинка самого артефакта")] public RawImage Icon;
        [Tooltip("Кольцо-таймер: Image Filled, Radial 360, от верха")] public Image Ring;
        [Tooltip("Тусклая дорожка под кольцом")] public Image Track;
        [Tooltip("Свет появления, обновления и ухода (аддитивный)")] public Image Glow;
        [Tooltip("Клуб дыма при появлении")] public Image Puff;
        [Tooltip("Кружок секунд в правом нижнем углу (из кадра 1b)")] public GameObject SecondsBadge;
        public TMP_Text Seconds;
        [Tooltip("Стаки «×2» в правом верхнем углу (из кадра 1b); один стак — пусто")] public TMP_Text Stacks;
        [Tooltip("Только у круга «+N»: число вместо значка")] public TMP_Text Count;
        [Tooltip("Насколько тёмен клуб дыма появления")] [Range(0f, 1f)] public float PuffAlpha = .6f;
        [Tooltip("Сила света появления (до «Вспышки и мерцание»)")] [Range(0f, 1f)] public float GlowPeak = .55f;

        internal float Weight;
        internal bool Live, Leaving;
        float _appearAt = -10f, _leaveAt = -10f, _punchAt = -10f;
        bool _leaveFlash;
        int _seconds = -1, _stacks = -1, _count = -1;
        Color _glow = Color.white;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Эффект появился (или вернулся из «+N»): цвет по тону, клуб дыма, «выпрыгивание».</summary>
        internal void Enter(float now, Color ring, Color glow)
        {
            Live = true;
            Leaving = false;
            _appearAt = now;
            _leaveAt = -10f;
            _glow = glow;
            if (Ring != null) Ring.color = ring;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        /// <summary>Эффект выпит заново или продлён: круг коротко вздрагивает и вспыхивает слабее.</summary>
        internal void Punch(float now) => _punchAt = now;

        /// <summary>
        /// Уход: <paramref name="flash"/> — эффект кончился (вспышка один раз и угасание); без вспышки —
        /// эффект ещё идёт, но круг уступил место «+N».
        /// </summary>
        internal void Leave(float now, bool flash)
        {
            Live = false;
            Leaving = true;
            _leaveAt = now;
            _leaveFlash = flash;
        }

        /// <summary>Доля ухода 0 … 1; не уходит — 1.</summary>
        internal float LeaveProgress(float now, float leaveTime)
            => !Leaving ? 1f : leaveTime > 0f ? Mathf.Clamp01((now - _leaveAt) / leaveTime) : 1f;

        /// <summary>Спрятать сразу, без движения (новая симуляция, лагерь).</summary>
        internal void Hide()
        {
            Live = Leaving = false;
            Weight = 0f;
            _seconds = _stacks = _count = -1;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Кольцо, секунды и стаки. Надписи меняются только при смене числа — без выделения памяти.</summary>
        internal void SetState(float fill, int seconds, int stacks)
        {
            if (Ring != null) Ring.fillAmount = fill;
            if (seconds != _seconds)
            {
                _seconds = seconds;
                bool show = seconds > 0;
                if (SecondsBadge != null && SecondsBadge.activeSelf != show) SecondsBadge.SetActive(show);
                if (show && Seconds != null) Seconds.text = HudEffectList.SecondsLabel(seconds);
            }
            if (stacks != _stacks)
            {
                _stacks = stacks;
                if (Stacks != null)
                {
                    Stacks.text = HudEffectList.StacksLabel(stacks);
                    if (Stacks.gameObject.activeSelf != stacks > 1) Stacks.gameObject.SetActive(stacks > 1);
                }
            }
        }

        /// <summary>Число круга «+N».</summary>
        internal void SetCount(int count)
        {
            if (count == _count) return;
            _count = count;
            if (Count != null) Count.text = HudEffectList.OverflowLabel(count);
        }

        /// <summary>Кадр движения. true — круг ещё нужен (живой, уходит или держит место в строке).</summary>
        internal bool Animate(float now, float popTime, float leaveTime)
        {
            float flashScale = GameUserSettings.FlashScale;
            float scale = 1f, alpha = 1f, glow = 0f, puff = 0f, puffScale = 1f;

            float appear = popTime > 0f ? (now - _appearAt) / popTime : 1f;
            if (appear < 1f)
            {
                scale = HudEffectRowMath.PopScale(appear);
                float fade = 1f - appear;
                glow = GlowPeak * fade * fade;
                puff = PuffAlpha * fade;
                puffScale = .75f + .55f * appear;
                alpha = Mathf.Clamp01(appear * 4f);
            }

            float punch = (now - _punchAt) / .32f;
            if (punch >= 0f && punch < 1f)
            {
                float k = (1f - punch) * (1f - punch);
                scale *= 1f + .1f * k;
                glow = Mathf.Max(glow, GlowPeak * .55f * k);
            }

            if (Leaving)
            {
                float t = leaveTime > 0f ? (now - _leaveAt) / leaveTime : 1f;
                if (t >= 1f) Leaving = false;
                alpha = HudEffectRowMath.LeaveAlpha(t);
                scale = HudEffectRowMath.LeaveScale(t);
                glow = _leaveFlash ? GlowPeak * HudEffectRowMath.LeaveFlash(t) : 0f;
                puff = 0f;
            }
            else if (!Live)
            {
                // Угас, но ещё держит место, пока соседи съезжаются.
                alpha = 0f;
                glow = puff = 0f;
            }

            // В покое ничего не ставится заново: смена масштаба перестраивает холст.
            if (Body != null && !Mathf.Approximately(Body.localScale.x, scale)) Body.localScale = new Vector3(scale, scale, 1f);
            if (Group != null && !Mathf.Approximately(Group.alpha, alpha)) Group.alpha = alpha;
            SetLight(Glow, glow * flashScale, _glow);
            if (Puff != null)
            {
                bool on = puff > .004f;
                if (Puff.enabled != on) Puff.enabled = on;
                if (on)
                {
                    Color c = Puff.color;
                    c.a = puff;
                    Puff.color = c;
                    Puff.rectTransform.localScale = new Vector3(puffScale, puffScale, 1f);
                }
            }
            return Live || Leaving || Weight > 0f;
        }

        static void SetLight(Image image, float alpha, Color colour)
        {
            if (image == null) return;
            bool on = alpha > .004f;
            if (image.enabled != on) image.enabled = on;
            if (!on) return;
            colour.a = alpha;
            image.color = colour;
        }
    }
}
