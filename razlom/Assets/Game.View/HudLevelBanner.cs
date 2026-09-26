using System.Collections.Generic;
using System.Text;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Баннер «Новый уровень» сверху по центру в материале «Дым и свет» (владелец 26 сентября: плашка
    /// из обгоревшей бумаги «не сочетается с интерфейсом и громоздкая»). Узкая полоса глубокого дыма,
    /// слева круглый медальон с числом (тёмный диск, тонкое кремовое кольцо, мягкий тёплый свет за
    /// ним; огонь — едва заметной искрой: яркое красное кольцо «огня меньше» не прошло), справа
    /// подпись и одна строка прибавок через « · ». Круг — та же фигура, что портрет, способности и
    /// значок уровня.
    ///
    /// Последовательность, секунды от начала:
    /// 0 — дым растекается от середины (своя UiInkGroup), плашка оседает из чуть большей;
    /// до <see cref="ClickAt"/> — проявляются старое число и подпись; <see cref="ClickAt"/> — число
    /// щёлкает на новое с лёгким толчком, свет за медальоном вздыхает, из медальона всплывают угли;
    /// следом выезжает строка прибавок. Потом баннер держится и уходит вверх с угасанием.
    /// Ничего не блокирует.
    ///
    /// Кадр на любой момент считается из времени (<see cref="Apply"/>), поэтому редактор снимает
    /// раскадровку без игры (<see cref="Preview"/>). Время неигровое.
    ///
    /// Та же плашка несёт вход на арену (аудит UI, этап 2): «3 | РАЗЛОМ ИЗ 10 | 4 встречи · 2 тайника»
    /// (<see cref="ShowMoment"/>). «Разлом зачищен» ушёл в узкое объявление (HudAnnounce). Новый показ
    /// во время текущего ждёт в очереди и проигрывает дым заново.
    ///
    /// Пока открыт экран выбора забега (награда, арена, замена — <see cref="RunHud.ModalOpen"/>) или
    /// мир закрывает дымная завеса перехода, плашки нет: новый показ ждёт в очереди, а показ на экране
    /// быстро гаснет на месте и, если его не успели прочитать, проигрывается заново после выбора
    /// (26 сентября «2 НОВЫЙ УРОВЕНЬ» проступал за заголовком «Выбери награду»).
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudLevelBanner : MonoBehaviour
    {
        public CanvasGroup Group;
        [Tooltip("Плашка с медальоном и строками — оседает из чуть большей")] public RectTransform Plate;
        public TMP_Text Number;
        [Tooltip("Надпись над строкой: «Новый уровень», «Разлом из 10»")] public TMP_Text Caption;
        [Tooltip("Строки прибавок. Строк меньше, чем прибавок, — последняя собирает остальные через « · » " +
                 "(«Дым и свет»: одна строка)")]
        public TMP_Text[] GainLines = new TMP_Text[0];
        [Tooltip("Мягкий тёплый свет за медальоном (материал света): вздыхает, когда щёлкает число")] public Image Flash;
        [Tooltip("Кольцо огня медальона (материал света): едва заметное, медленно вращается, чуть теплеет на щелчке")] public Image Rays;
        [Tooltip("Необязательно: веер искр старой плашки")] public HudSparkBurst Sparks;
        [Tooltip("Необязательно: проблеск по старой плашке")] public HudGlint Glint;
        [Tooltip("Угли из медальона: вспышка искр на щелчке числа")] public UiEmbers Embers;
        [Tooltip("Сколько углей разом")] public int EmberBurst = 10;
        [Tooltip("Секунд держится после появления")] public float HoldTime = 2.4f;
        [Tooltip("Секунд на уход вверх")] public float OutTime = .5f;
        [Tooltip("Насколько крупнее плашка появляется (спокойно: чуть-чуть)")] public float PopScale = 1.06f;
        [Tooltip("Когда число щёлкает со старого на новое, с от начала: дым и цифра уже проявились")] public float ClickAt = .6f;
        [Tooltip("Толчок числа на щелчке")] public float NumberPunch = 1.18f;
        [Tooltip("Свет за медальоном: пик на щелчке и покой")] public float FlashPeak = .4f, FlashRest = .12f;
        [Tooltip("Сила кольца огня в покое (владелец 26 сентября: огня меньше; .3 читалось ярким красным кольцом)")]
        public float RaysAlpha = .1f;
        [Tooltip("Вращение кольца огня, градусов в секунду")] public float RaysSpin = 6f;
        [Tooltip("Ширина по тексту: баннер обнимает подпись и строку (узлы текста — от левого края)")] public bool FitWidth;
        [Tooltip("Ширина баннера: от–до, единицы Canvas")] public Vector2 WidthRange = new Vector2(400f, 680f);
        [Tooltip("Поле справа от самой длинной строки")] public float RightPad = 48f;
        [Tooltip("Секунд на угасание, когда поверх показа открылся экран выбора забега")] public float YieldTime = .2f;
        [Tooltip("Показ, прерванный экраном выбора раньше этого (с от начала), проигрывается заново после выбора; " +
                 "позже — уже прочитан и просто кончается")]
        public float SeenAfter = 2f;

        const float In = .4f;
        // Свои часы показа: шаг за кадр не больше MaxStep. Кадр сборки арены длится секунды,
        // и по настоящим часам плашка «Разлом 1 из 10» успевала отыграть целиком до первого кадра.
        const float MaxStep = .1f;
        // Разделитель прибавок в одной строке: точка приглушена, числа остаются золотыми.
        const string Separator = " <alpha=#8C>·<alpha=#FF> ";
        float _t = 100f, _lastNow;
        Vector2 _rest;
        Vector2[] _gainRest;
        bool _restKnown, _embersDue;
        // Ожидание: объект включён (LateUpdate разбирает очередь), но плашки нет — открыт выбор забега.
        bool _waiting;
        // Угасание под экраном выбора: доля 0…1, меньше нуля — не гаснет; с какой прозрачности и повторять ли.
        float _yield = -1f, _yieldFrom;
        bool _replay;

        struct Showing
        {
            public string Before, After, Caption;
            public string[] Lines;
            public float Hold;
            public bool Level;
        }

        [Tooltip("Сколько держится показ, пока следующий ждёт в очереди")] public float QueuedHold = 1.1f;

        Showing _current;
        readonly Queue<Showing> _queue = new Queue<Showing>();
        static readonly StringBuilder Joined = new StringBuilder();

        float Total => In + _current.Hold + OutTime;

        /// <summary>Держать меньше, если следующий показ ждёт: очередь не должна тянуться секундами.</summary>
        float HoldNow => _queue.Count > 0 ? Mathf.Min(_current.Hold, QueuedHold) : _current.Hold;

        /// <summary>Секунд с начала текущего показа (для съёмки и проверки).</summary>
        public float ShownFor => _t;

        /// <summary>Плашка на экране и ещё не ушла.</summary>
        public bool Busy => gameObject.activeSelf && !_waiting && _t <= Total;

        /// <summary>
        /// Плашке сейчас нельзя на экран: открыт выбор забега (награда, арена, замена) или дымная завеса
        /// перехода закрывает мир — после выбора арены глубина сменяется ещё под завесой.
        /// </summary>
        static bool Held => RunHud.ModalOpen || CampTransition.Covering;

        public void Show(int level) => Enqueue(Level(level));

        /// <summary>
        /// Момент забега на той же плашке: число щёлкает с <paramref name="before"/> на
        /// <paramref name="after"/>, над строкой — <paramref name="caption"/>; строки
        /// <paramref name="lines"/> встают в одну через « · » (пустые пропускаются).
        /// </summary>
        public void ShowMoment(string before, string after, string caption, string[] lines, float hold)
            => Enqueue(new Showing { Before = before, After = after, Caption = caption, Lines = lines, Hold = hold });

        /// <summary>Кадр редактора: баннер на момент <paramref name="t"/> секунд после появления.</summary>
        public void Preview(int level, float t)
        {
            SetTexts(Level(level));
            gameObject.SetActive(true);
            Apply(t);
        }

        Showing Level(int level) => new Showing
        {
            Before = Mathf.Max(1, level - 1).ToString(),
            After = level.ToString(),
            Caption = "НОВЫЙ УРОВЕНЬ",
            Lines = new[]
            {
                "<color=#FFD27A>+" + Progression.HealthPerLevel + "</color> здоровья",
                "<color=#FFD27A>+" + Progression.DamagePerLevel + "</color> урона",
                "<color=#FFD27A>+" + Progression.LavidiumPerLevel + "</color> лавидия",
            },
            Hold = HoldTime,
            Level = true,
        };

        void Enqueue(Showing showing)
        {
            // Очередь не пуста — показ после тех, кто ждёт, даже если плашка сейчас свободна.
            if (!Busy && !Held && _queue.Count == 0) { Begin(showing); return; }
            // Несколько уровней подряд (пачка убийств) — один показ «со старого на последний».
            if (showing.Level && _queue.Count > 0)
            {
                Showing[] pending = _queue.ToArray();
                if (pending[pending.Length - 1].Level)
                {
                    showing.Before = pending[pending.Length - 1].Before;
                    pending[pending.Length - 1] = showing;
                    _queue.Clear();
                    foreach (Showing one in pending) _queue.Enqueue(one);
                    return;
                }
            }
            _queue.Enqueue(showing);
            // Плашка выключена, а показ ждёт закрытия выбора: включается пустой, очередь разберёт LateUpdate.
            if (!gameObject.activeSelf) Wait();
        }

        /// <summary>Пустое ожидание: объект включён, чтобы шли свои часы, но плашки не видно.</summary>
        void Wait()
        {
            _waiting = true;
            _yield = -1f;
            _lastNow = UiMotion.Now;
            gameObject.SetActive(true);
            if (Group != null) Group.alpha = 0f;
        }

        /// <summary>
        /// Прерванный показ — первым в очередь. Уровень, который уже ждёт первым, забирает его «старое»
        /// число: один показ «со старого на последний», как у пачки уровней подряд.
        /// </summary>
        void Replay(Showing showing)
        {
            Showing[] pending = _queue.ToArray();
            _queue.Clear();
            if (showing.Level && pending.Length > 0 && pending[0].Level) pending[0].Before = showing.Before;
            else _queue.Enqueue(showing);
            foreach (Showing one in pending) _queue.Enqueue(one);
        }

        void Begin(Showing showing)
        {
            SetTexts(showing);
            _t = 0f;
            _lastNow = UiMotion.Now;
            _embersDue = true;
            _yield = -1f;
            _waiting = false;
            bool wasShown = gameObject.activeSelf;
            gameObject.SetActive(true);
            // Показ из очереди (и после ожидания выбора) идёт на включённом объекте — OnEnable не
            // сработает, дым и буквы проявляются заново вручную (выключенный объект проявит сам OnEnable группы).
            var ink = GetComponent<UiInkGroup>();
            if (wasShown && ink != null) ink.Show();
            Apply(0f);
        }

        void SetTexts(Showing showing)
        {
            _current = showing;
            if (Caption != null) Caption.text = showing.Caption;
            int count = GainLines.Length;
            for (int i = 0; i < count; i++)
                if (GainLines[i] != null) GainLines[i].text = i < count - 1 ? LineAt(showing.Lines, i) : JoinFrom(showing.Lines, i);
            Fit();
        }

        static string LineAt(string[] lines, int i) => lines != null && i < lines.Length && lines[i] != null ? lines[i] : string.Empty;

        /// <summary>Строки с <paramref name="from"/> и дальше — в одну через разделитель, пустые пропускаются.</summary>
        static string JoinFrom(string[] lines, int from)
        {
            if (lines == null || from >= lines.Length) return string.Empty;
            Joined.Clear();
            for (int i = from; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                if (Joined.Length > 0) Joined.Append(Separator);
                Joined.Append(lines[i]);
            }
            return Joined.ToString();
        }

        /// <summary>
        /// Ширина по тексту: от левого края до конца самой длинной строки плюс поле. Узлы текста и
        /// медальон стоят от левого края, дым и нить растянуты — середина баннера остаётся на месте.
        /// </summary>
        void Fit()
        {
            if (!FitWidth || Caption == null) return;
            float text = Caption.GetPreferredValues(Caption.text).x;
            foreach (TMP_Text line in GainLines)
                if (line != null && !string.IsNullOrEmpty(line.text)) text = Mathf.Max(text, line.GetPreferredValues(line.text).x);
            var rect = (RectTransform)transform;
            float width = Mathf.Clamp(Caption.rectTransform.anchoredPosition.x + text + RightPad, WidthRange.x, WidthRange.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float step = Mathf.Clamp(now - _lastNow, 0f, MaxStep);
            _lastNow = now;
            if (_waiting)
            {
                // Ждать нечего — выключиться; выбор закрылся и завеса разошлась — первый из очереди.
                if (_queue.Count == 0) { _waiting = false; gameObject.SetActive(false); }
                else if (!Held) Begin(_queue.Dequeue());
                return;
            }
            if (_yield >= 0f || Held) { Yield(step); return; }
            _t += step;
            float t = _t;
            // Ждёт следующий — текущий уходит раньше: время сдвигается к началу ухода.
            float hold = HoldNow;
            if (hold < _current.Hold && t > In + hold && t < In + _current.Hold) _t = t = In + _current.Hold;
            if (t > Total)
            {
                if (_queue.Count > 0) { Begin(_queue.Dequeue()); return; }
                gameObject.SetActive(false);
                return;
            }
            // Угли — на щелчке, а не в Begin: только что включённые угли до своего первого кадра
            // искр не принимают (их запас растёт в Update).
            if (_embersDue && t >= ClickAt)
            {
                _embersDue = false;
                if (Embers != null) Embers.Burst(EmberBurst);
            }
            Apply(t);
        }

        /// <summary>
        /// Поверх показа открылся экран выбора (или накатила завеса): плашка гаснет на месте за
        /// <see cref="YieldTime"/>, без ухода вверх, — под заголовком выбора ничего не должно шевелиться.
        /// Недочитанный показ (меньше <see cref="SeenAfter"/> с) встаёт в очередь первым и проиграется
        /// целиком, когда выбор закроется; дочитанный просто кончается.
        /// </summary>
        void Yield(float step)
        {
            if (_yield < 0f)
            {
                _yield = 0f;
                _yieldFrom = Group != null ? Group.alpha : 0f;
                _replay = _t < SeenAfter && _t <= Total;
            }
            _yield += step / Mathf.Max(.01f, YieldTime);
            if (Group != null) Group.alpha = _yieldFrom * (1f - Mathf.SmoothStep(0f, 1f, _yield));
            if (_yield < 1f) return;
            if (_replay) Replay(_current);
            Wait();
        }

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        public void Apply(float t)
        {
            var rect = (RectTransform)transform;
            if (!_restKnown)
            {
                _rest = rect.anchoredPosition;
                _gainRest = new Vector2[GainLines.Length];
                for (int i = 0; i < GainLines.Length; i++)
                    if (GainLines[i] != null) _gainRest[i] = GainLines[i].rectTransform.anchoredPosition;
                _restKnown = true;
            }
            float outK = Smooth(In + _current.Hold, Total, t);
            float alpha = Smooth(0f, .1f, t) * (1f - outK);
            if (Group != null) Group.alpha = alpha;
            rect.anchoredPosition = _rest + new Vector2(0f, 16f * outK);

            if (Plate != null)
            {
                // Спокойно: оседает из чуть большей без перелёта — дым и так в движении.
                float k = UiMotion.EaseOut.Evaluate(Mathf.Clamp01(t / In));
                Plate.localScale = Vector3.one * Mathf.LerpUnclamped(PopScale, 1f, k);
            }
            // Вздох на щелчке числа: быстро вверх, плавно вниз.
            float since = t - ClickAt;
            float pulse = since < 0f ? 0f : since < .08f ? since / .08f : 1f - Smooth(.08f, .6f, since);
            if (Flash != null)
            {
                float glow = FlashRest * Smooth(0f, .4f, t) + (FlashPeak - FlashRest) * pulse;
                HudFx.SetAlpha(Flash, glow * (1f - outK));
                Flash.rectTransform.localScale = Vector3.one * (1f + .18f * Smooth(0f, .5f, since));
            }
            if (Rays != null)
            {
                HudFx.SetAlpha(Rays, RaysAlpha * (Smooth(.05f, .35f, t) + 1.2f * pulse) * (1f - outK));
                Rays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * RaysSpin);
            }
            if (Number != null)
            {
                bool clicked = since >= 0f;
                string text = (clicked ? _current.After : _current.Before) ?? string.Empty;
                if (Number.text != text) Number.text = text;
                float punch = clicked ? Mathf.Lerp(NumberPunch, 1f, UiMotion.EaseOut.Evaluate(Mathf.Clamp01(since / .3f))) : 1f;
                Number.rectTransform.localScale = Vector3.one * punch;
            }
            // Прибавки — следствие щелчка: выезжают сразу за ним.
            for (int i = 0; i < GainLines.Length; i++)
            {
                TMP_Text line = GainLines[i];
                if (line == null) continue;
                float k = Mathf.Clamp01((since - (.08f + i * .12f)) / .3f);
                line.alpha = k;
                line.rectTransform.anchoredPosition = _gainRest[i] + new Vector2(16f * (1f - UiMotion.EaseOut.Evaluate(k)), 0f);
            }
            if (Sparks != null) Sparks.Apply(since);
            if (Glint != null) Glint.Apply((since - .1f) / .7f);
        }
    }
}
