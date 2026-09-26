using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Появление и уход группы элементов «Дыма и света» (владелец 25 сентября: «всё, что в таком
    /// виде появляется на экране, будет анимированно появляться»). Чернила дыма растекаются, по
    /// фронту тлеет кромка, за дымом лесенкой проявляется текст, последним загорается свет.
    ///
    /// Включение объекта само запускает показ (<see cref="PlayOnEnable"/>). Уход — <see cref="Hide"/>:
    /// то же в обратную сторону, быстрее, потом колбэк (обычно SetActive(false)).
    ///
    /// Ведёт только своих: вложенная группа (всплывашка внутри столбика) ведёт своих детей сама.
    /// Часы свои, шаг не больше 0,1 с: на склейке арены кадр длится секунды, и без этого
    /// появление проскакивало бы целиком за один кадр.
    ///
    /// Большие моменты (итоги забега) тлеют медленно: у дыма своя длительность
    /// (<see cref="InkDuration"/>), ровный ход (<see cref="Easing.Smooth"/>) и широкая тусклая
    /// кромка (<see cref="EdgeScale"/>); текст и свет идут в своём темпе (<see cref="Duration"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiInkGroup : MonoBehaviour
    {
        public enum Sweep { LeftToRight, BottomToTop, TopToBottom, FromCenter }

        /// <summary>Ход проявления дыма: быстрый старт с плавной остановкой или ровный (k²(3−2k)).</summary>
        public enum Easing { EaseOut, Smooth }

        [Tooltip("Показывать при включении объекта")] public bool PlayOnEnable = true;
        [Tooltip("Проявление одного элемента, с")] public float Duration = .55f;
        [Tooltip("Проявление дыма и мазков (не текста и не света), с; 0 и меньше — как Duration. " +
                 "Итоги забега: дым тлеет дольше, чем встаёт текст")]
        public float InkDuration;
        [Tooltip("Ход проявления дыма и мазков: EaseOut — быстрый старт (фронт огня пролетает за треть времени), " +
                 "Smooth — ровный, огонь тлеет всё проявление. Текст и свет всегда EaseOut")]
        public Easing Curve = Easing.EaseOut;
        [Tooltip("Разброс начала по группе, с: лесенка от первого элемента к последнему")] public float Stagger = .22f;
        [Tooltip("Уход одного элемента, с")] public float HideDuration = .22f;
        [Tooltip("Куда идёт лесенка")] public Sweep Direction = Sweep.LeftToRight;
        [Tooltip("Общая задержка перед показом, с")] public float StartDelay;
        [Tooltip("Сила тлеющей кромки у всех своих: 0 — без огня (подсказки: огонь на каждом наведении выглядел вспышкой)")]
        [Range(0f, 1f)] public float Burn = 1f;
        [Tooltip("Ширина тлеющей кромки у всех своих: больше 1 — шире, тусклее и гаснет дольше (итоги забега)")]
        [Range(.5f, 3f)] public float EdgeScale = 1f;
        [Tooltip("Проявляться анимацией только в первое включение за запуск игры (боевой HUD: владелец 25 сентября — " +
                 "появление при каждом выходе из меню лишнее); дальше включение показывает сразу")]
        public bool FirstTimeOnly;

        static readonly HashSet<string> Played = new HashSet<string>();
        static readonly List<UiInkReveal> InkBuffer = new List<UiInkReveal>();
        static readonly List<UiInkText> TextBuffer = new List<UiInkText>();

        struct Part
        {
            public UiInkReveal Ink;
            public UiInkText Text;
            public float Order;
            public float Extra;
            /// <summary>Дым, мазок, картинка — всё, что не свет: у них своя длительность и ход.</summary>
            public bool Smoke;
        }

        readonly List<Part> _parts = new List<Part>();
        bool _collected, _hiding, _running;
        float _t, _lastNow;
        // Сколько частей было в детях при сборе: другое число — добавили клонов, пора пересобрать.
        int _seen = -1;
        // Чем кончилось последнее движение: 0 — показано, 1 — скрыто, меньше нуля — ещё не было.
        float _rest = -1f;
        Action _done;

        public bool Hiding => _hiding;
        public bool Running => _running;

        void OnEnable()
        {
            UiInkClock.Ensure();
            EnableChannels();
            if (!PlayOnEnable || !Application.isPlaying) return;
            if (FirstTimeOnly && !Played.Add(name)) { ShowInstant(); return; }
            Show();
        }

        /// <summary>Каналы uv1/uv2 холста: без них шейдер не получает данных элемента.</summary>
        void EnableChannels()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        }

        /// <summary>
        /// Пересобрать список элементов (после того как в группу добавили детей). Звать можно
        /// когда угодно, и повторно: посреди движения новые части подхватит следующий кадр, а в
        /// покое они сразу встают в то же состояние, что и вся группа (клон, сделанный посреди
        /// ухода, иначе оставался невидимым). Show и Hide пересобирают сами, если число частей
        /// в детях изменилось.
        /// </summary>
        public void Collect()
        {
            _parts.Clear();
            var rect = (RectTransform)transform;
            Rect bounds = rect.rect;
            GetComponentsInChildren(true, InkBuffer);
            GetComponentsInChildren(true, TextBuffer);
            _seen = InkBuffer.Count + TextBuffer.Count;
            foreach (UiInkReveal ink in InkBuffer)
                if (Owns(ink))
                {
                    ink.Burn = Burn;
                    ink.EdgeScale = EdgeScale;
                    _parts.Add(new Part { Ink = ink, Order = OrderOf(ink.transform, bounds), Extra = ink.Delay, Smoke = !IsLight(ink) });
                }
            foreach (UiInkText text in TextBuffer)
                if (Owns(text)) _parts.Add(new Part { Text = text, Order = OrderOf(text.transform, bounds), Extra = text.Delay });
            InkBuffer.Clear();
            TextBuffer.Clear();
            _collected = true;
            if (!_running && _rest >= 0f) SetAll(_rest);
        }

        /// <summary>Собрать, если ещё не собрано или в детях стало другое число частей (клоны ячеек, строк).</summary>
        void EnsureCollected()
        {
            if (_collected)
            {
                GetComponentsInChildren(true, InkBuffer);
                GetComponentsInChildren(true, TextBuffer);
                int count = InkBuffer.Count + TextBuffer.Count;
                InkBuffer.Clear();
                TextBuffer.Clear();
                if (count == _seen) return;
            }
            Collect();
        }

        bool Owns(Component part) => part.GetComponentInParent<UiInkGroup>(true) == this;

        /// <summary>Свет (кольца, нити, трещина) прибавляется к миру и встаёт в темпе текста, а не дыма.</summary>
        static bool IsLight(UiInkReveal ink)
        {
            var graphic = ink.GetComponent<UnityEngine.UI.Graphic>();
            Material material = graphic != null ? graphic.material : null;
            return material != null && material.HasProperty("_Light") && material.GetFloat("_Light") > .5f;
        }

        float OrderOf(Transform part, Rect bounds)
        {
            Vector3 local = transform.InverseTransformPoint(((RectTransform)part).TransformPoint(((RectTransform)part).rect.center));
            float x = Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x);
            float y = Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y);
            switch (Direction)
            {
                case Sweep.BottomToTop: return y;
                case Sweep.TopToBottom: return 1f - y;
                case Sweep.FromCenter: return Mathf.Clamp01(new Vector2(x - .5f, y - .5f).magnitude * 1.6f);
                default: return x;
            }
        }

        public void Show()
        {
            EnsureCollected();
            _hiding = false;
            _done = null;
            Begin();
            Apply();
        }

        /// <summary>Уход в обратную сторону; <paramref name="done"/> — когда всё скрыто.</summary>
        public void Hide(Action done = null)
        {
            EnsureCollected();
            if (!isActiveAndEnabled) { _running = false; _rest = 1f; SetAll(1f); done?.Invoke(); return; }
            _hiding = true;
            _done = done;
            Begin();
        }

        /// <summary>Сразу показать всё, без анимации (кадры редактора).</summary>
        public void ShowInstant()
        {
            EnsureCollected();
            _running = false;
            _rest = 0f;
            SetAll(0f);
        }

        void Begin()
        {
            _t = 0f;
            _lastNow = UiMotion.Now;
            _running = true;
        }

        void SetAll(float hidden)
        {
            foreach (Part part in _parts)
            {
                if (part.Ink != null) part.Ink.Hidden = hidden;
                if (part.Text != null) part.Text.Hidden = hidden;
            }
        }

        void LateUpdate()
        {
            if (!_running) return;
            float now = UiMotion.Now;
            _t += Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            if (Apply()) return;
            _running = false;
            _rest = _hiding ? 1f : 0f;
            if (_hiding) { Action done = _done; _done = null; done?.Invoke(); }
        }

        /// <summary>Ставит всем «скрыто»; true — пока хоть кто-то в пути.</summary>
        bool Apply()
        {
            bool moving = false;
            float spread = _hiding ? Stagger * .5f : Stagger;
            float inkLength = InkDuration > 0f ? InkDuration : Duration;
            foreach (Part part in _parts)
            {
                // Уход — в обратном порядке: последний появившийся гаснет первым.
                float order = _hiding ? 1f - part.Order : part.Order;
                // Отрицательная задержка (дым ведёт раньше лесенки) не уводит начало в прошлое:
                // иначе дым вставал бы уже наполовину проявленным.
                float start = Mathf.Max(0f, (_hiding ? 0f : StartDelay + part.Extra) + order * spread);
                float length = _hiding ? HideDuration : part.Smoke ? inkLength : Duration;
                float k = Mathf.Clamp01((_t - start) / Mathf.Max(length, .0001f));
                if (k < 1f) moving = true;
                float hidden = _hiding ? k * k : 1f - Ease(k, part.Smoke);
                if (part.Ink != null) part.Ink.Hidden = hidden;
                if (part.Text != null) part.Text.Hidden = hidden;
            }
            return moving;
        }

        float Ease(float k, bool smoke) =>
            smoke && Curve == Easing.Smooth ? k * k * (3f - 2f * k) : UiMotion.EaseOut.Evaluate(k);
    }
}
