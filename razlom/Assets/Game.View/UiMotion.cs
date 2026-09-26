using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Анимации интерфейса на реальном времени: в паузе Time.timeScale = 0,
    /// а меню обязано двигаться. Без сторонних библиотек — в проекте их нет.
    ///
    /// Один раннер на сцену; анимации — короткие «от текущего к цели» по
    /// кривой, с необязательной задержкой (появление лесенкой). Новая анимация
    /// того же свойства того же объекта заменяет старую, поэтому быстрые
    /// повторные клики не дёргают элемент.
    /// </summary>
    public sealed class UiMotion : MonoBehaviour
    {
        struct Track
        {
            public UnityEngine.Object Target;
            public int Channel;
            public float Start, Duration;
            public AnimationCurve Curve;
            public Action Begin;
            public Action<float> Apply;
            public Action Done;
            public bool Started;
        }

        static UiMotion _runner;
        readonly List<Track> _tracks = new List<Track>();

        static float _clock, _lastReal;
        static int _clockFrame = -1;

        /// <summary>
        /// Часы интерфейса. В игре — реальное время. При записи видео
        /// (Time.captureDeltaTime &gt; 0) — шаг записи на кадр: кадры пишутся медленнее
        /// реального времени, и по настоящим часам анимации на видео были бы ускорены.
        /// Идут без скачков при включении и выключении записи.
        /// </summary>
        public static float Now
        {
            get
            {
                if (_clockFrame == Time.frameCount) return _clock;
                if (_clockFrame < 0) _clock = Time.unscaledTime;
                else _clock += Time.captureDeltaTime > 0f
                    ? Time.captureDeltaTime * (Time.frameCount - _clockFrame)
                    : Time.unscaledTime - _lastReal;
                _lastReal = Time.unscaledTime;
                _clockFrame = Time.frameCount;
                return _clock;
            }
        }

        /// <summary>Мягкий выход: быстрое начало, плавная остановка.</summary>
        public static readonly AnimationCurve EaseOut = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

        static UiMotion Runner
        {
            get
            {
                if (_runner != null) return _runner;
                var host = new GameObject("UI Motion") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(host);
                _runner = host.AddComponent<UiMotion>();
                return _runner;
            }
        }

        /// <summary>
        /// Анимирует значение 0→1; channel отличает свойства одного объекта.
        /// При <paramref name="delay"/> &gt; 0 значение 0 применяется сразу, а движение
        /// начинается позже; <paramref name="begin"/> вызывается в момент старта.
        /// </summary>
        public static void Play(UnityEngine.Object target, int channel, float duration, Action<float> apply,
            AnimationCurve curve = null, Action done = null, float delay = 0f, Action begin = null)
        {
            if (target == null) return;
            // Превью сборщиков в редакторе: сразу конечное состояние, без скрытого объекта в сцене.
            if (!Application.isPlaying) { begin?.Invoke(); apply(1f); done?.Invoke(); return; }
            UiMotion runner = Runner;
            runner._tracks.RemoveAll(t => t.Target == target && t.Channel == channel);
            if (duration <= 0f && delay <= 0f) { begin?.Invoke(); apply(1f); done?.Invoke(); return; }
            var track = new Track
            {
                Target = target, Channel = channel, Start = Now + Mathf.Max(0f, delay),
                Duration = Mathf.Max(0.0001f, duration), Curve = curve ?? EaseOut, Begin = begin, Apply = apply, Done = done,
            };
            apply(0f);
            if (delay <= 0f) { track.Started = true; begin?.Invoke(); }
            runner._tracks.Add(track);
        }

        public static void Stop(UnityEngine.Object target)
        {
            if (_runner != null) _runner._tracks.RemoveAll(t => t.Target == target);
        }

        void Update()
        {
            float now = Now;
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                Track track = _tracks[i];
                if (track.Target == null) { _tracks.RemoveAt(i); continue; }
                if (now < track.Start) continue;
                if (!track.Started)
                {
                    track.Started = true;
                    _tracks[i] = track;
                    track.Begin?.Invoke();
                }
                float t = Mathf.Clamp01((now - track.Start) / track.Duration);
                track.Apply(track.Curve.Evaluate(t));
                if (t < 1f) continue;
                _tracks.RemoveAt(i);
                track.Done?.Invoke();
            }
        }

        // ---- готовые движения ----
        const int ChannelPosition = 1, ChannelAlpha = 2, ChannelScale = 3, ChannelColor = 4;

        public static void MoveTo(RectTransform rect, Vector2 position, float duration, float delay = 0f)
        {
            if (rect == null) return;
            Vector2 from = rect.anchoredPosition;
            Play(rect, ChannelPosition, duration, t => rect.anchoredPosition = Vector2.LerpUnclamped(from, position, t), null, null, delay);
        }

        public static void FadeTo(CanvasGroup group, float alpha, float duration, Action done = null, float delay = 0f)
        {
            if (group == null) return;
            float from = group.alpha;
            Play(group, ChannelAlpha, duration, t => group.alpha = Mathf.LerpUnclamped(from, alpha, t), null, done, delay);
        }

        public static void ScaleTo(Transform transform, float scale, float duration)
        {
            if (transform == null) return;
            Vector3 from = transform.localScale;
            Play(transform, ChannelScale, duration, t => transform.localScale = Vector3.LerpUnclamped(from, Vector3.one * scale, t));
        }

        public static void ColorTo(Graphic graphic, Color color, float duration)
        {
            if (graphic == null) return;
            Color from = graphic.color;
            Play(graphic, ChannelColor, duration, t => graphic.color = Color.LerpUnclamped(from, color, t));
        }

        /// <summary>
        /// Появление из прозрачности со сдвигом: элемент ставится на
        /// <paramref name="offset"/> и через <paramref name="delay"/> приезжает на своё место.
        /// </summary>
        public static void Arrive(RectTransform rect, CanvasGroup group, Vector2 offset, float duration, float delay)
        {
            if (rect == null || group == null) return;
            Vector2 rest = rect.anchoredPosition;
            group.alpha = 0f;
            rect.anchoredPosition = rest + offset;
            Play(rect, ChannelPosition, duration, t => rect.anchoredPosition = Vector2.LerpUnclamped(rest + offset, rest, t), null, null, delay);
            Play(group, ChannelAlpha, duration, t => group.alpha = t, null, null, delay);
        }
    }
}
