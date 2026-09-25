using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Короткие движения боевого HUD поверх <see cref="UiMotion"/> (владелец, 24 сентября:
    /// «нет живости в HUD… свечения, пульсации, но не переборно»). Только по событиям:
    /// толчок, вспышка, волна, дрожь — и всё снова в покое. Постоянные петли живут в
    /// <see cref="HudPulse"/> и только для «внимание».
    /// </summary>
    public static class HudFx
    {
        const int ChannelPunch = 21, ChannelFlash = 22, ChannelShake = 23, ChannelBurst = 24;
        static readonly AnimationCurve Linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        static readonly Dictionary<RectTransform, Vector2> Rest = new Dictionary<RectTransform, Vector2>();

        /// <summary>Толчок масштаба: с <paramref name="from"/> обратно к 1.</summary>
        public static void Punch(Transform target, float from, float duration)
        {
            if (target == null) return;
            UiMotion.Play(target, ChannelPunch, duration, k => target.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, k));
        }

        /// <summary>Вспышка: прозрачность сразу <paramref name="peak"/> и гаснет к нулю.</summary>
        public static void Flash(Graphic graphic, float peak, float duration)
        {
            if (graphic == null) return;
            graphic.enabled = true;
            UiMotion.Play(graphic, ChannelFlash, duration, k => SetAlpha(graphic, peak * (1f - k)), Linear,
                () => { if (graphic != null) graphic.enabled = false; });
        }

        /// <summary>Волна: вспышка, которая растёт от <paramref name="fromScale"/> до <paramref name="toScale"/>.</summary>
        public static void Burst(Graphic graphic, float peak, float fromScale, float toScale, float duration)
        {
            if (graphic == null) return;
            graphic.enabled = true;
            Transform t = graphic.transform;
            UiMotion.Play(graphic, ChannelBurst, duration, k =>
            {
                t.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, toScale, k);
                SetAlpha(graphic, peak * (1f - k) * (1f - k));
            }, UiMotion.EaseOut, () => { if (graphic != null) graphic.enabled = false; });
        }

        /// <summary>Короткая дрожь по X: полоса здоровья от удара. Место покоя запоминается один раз.</summary>
        public static void Shake(RectTransform rect, float amplitude, float duration)
        {
            if (rect == null) return;
            if (!Rest.TryGetValue(rect, out Vector2 rest)) Rest[rect] = rest = rect.anchoredPosition;
            UiMotion.Play(rect, ChannelShake, duration, k =>
                rect.anchoredPosition = rest + new Vector2(amplitude * (1f - k) * Mathf.Sin(k * Mathf.PI * 7f), 0f), Linear,
                () => { if (rect != null) rect.anchoredPosition = rest; });
        }

        public static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
