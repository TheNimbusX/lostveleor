using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Большой баннер «Новый уровень» сверху по центру — вариант А концепта 4-buffs-level-sheet
    /// (владелец, 24 сентября: «баннер уровня большой лучше»; потом: «скудно, нет эффекта вау»).
    /// Рисованная плашка с обожжённой кромкой, слева крупная золотая цифра, справа прибавки.
    ///
    /// Последовательность, секунды от начала:
    /// 0 — вспышка света, плашка влетает крупнее и оседает с перелётом, веером вылетают искры;
    /// 0,2 — цифра щёлкает со старого уровня на новый с толчком, за ней медленно вращаются лучи;
    /// 0,3 — проблеск пробегает по плашке; 0,38 — прибавки выезжают по одной;
    /// потом баннер держится и уходит вверх с угасанием. Ничего не блокирует.
    ///
    /// Кадр на любой момент считается из времени (<see cref="Apply"/>), поэтому редактор снимает
    /// раскадровку без игры (<see cref="Preview"/>). Время неигровое.
    ///
    /// Та же плашка несёт моменты забега (аудит UI, этап 2): «Разлом 3 из 10» на входе в арену,
    /// «Разлом зачищен» по последнему врагу (<see cref="ShowMoment"/>). Новый показ во время
    /// текущего ждёт в очереди: уровень на последнем ударе не перебивает «Разлом зачищен».
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudLevelBanner : MonoBehaviour
    {
        public CanvasGroup Group;
        [Tooltip("Плашка с цифрой и прибавками — влетает с перелётом")] public RectTransform Plate;
        public TMP_Text Number;
        [Tooltip("Надпись над строками: «Новый уровень», «Разлом из 10»")] public TMP_Text Caption;
        [Tooltip("Строки прибавок: здоровье, урон, лавидий — выезжают по одной")] public TMP_Text[] GainLines = new TMP_Text[0];
        [Tooltip("Вспышка света за плашкой (аддитивная)")] public Image Flash;
        [Tooltip("Лучи за цифрой (аддитивные), медленно вращаются")] public Image Rays;
        public HudSparkBurst Sparks;
        public HudGlint Glint;
        [Tooltip("Секунд держится после появления")] public float HoldTime = 2.8f;
        [Tooltip("Секунд на уход вверх")] public float OutTime = .6f;
        [Tooltip("Насколько крупнее плашка влетает")] public float PopScale = 1.3f;

        const float In = .4f;
        // Свои часы показа: шаг за кадр не больше MaxStep. Кадр сборки арены длится секунды,
        // и по настоящим часам плашка «Разлом 1 из 10» успевала отыграть целиком до первого кадра.
        const float MaxStep = .1f;
        float _t = 100f, _lastNow;
        Vector2 _rest;
        Vector2[] _gainRest;
        bool _restKnown;

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

        float Total => In + _current.Hold + OutTime;

        /// <summary>Держать меньше, если следующий показ ждёт: очередь не должна тянуться секундами.</summary>
        float HoldNow => _queue.Count > 0 ? Mathf.Min(_current.Hold, QueuedHold) : _current.Hold;

        /// <summary>Секунд с начала текущего показа (для съёмки и проверки).</summary>
        public float ShownFor => _t;

        /// <summary>Плашка на экране и ещё не ушла.</summary>
        public bool Busy => gameObject.activeSelf && _t <= Total;

        public void Show(int level) => Enqueue(Level(level));

        /// <summary>
        /// Момент забега на той же плашке: число щёлкает с <paramref name="before"/> на
        /// <paramref name="after"/>, над строками — <paramref name="caption"/>, строк до трёх.
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
            if (!Busy) { Begin(showing); return; }
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
        }

        void Begin(Showing showing)
        {
            SetTexts(showing);
            _t = 0f;
            _lastNow = UiMotion.Now;
            gameObject.SetActive(true);
            Apply(0f);
        }

        void SetTexts(Showing showing)
        {
            _current = showing;
            if (Caption != null) Caption.text = showing.Caption;
            for (int i = 0; i < GainLines.Length; i++)
                if (GainLines[i] != null) GainLines[i].text = showing.Lines != null && i < showing.Lines.Length ? showing.Lines[i] : string.Empty;
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            _t += Mathf.Clamp(now - _lastNow, 0f, MaxStep);
            _lastNow = now;
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
            Apply(t);
        }

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        /// <summary>С перелётом: к 1 через 1,1 и обратно.</summary>
        static float Back(float k)
        {
            const float s = 1.9f;
            k -= 1f;
            return k * k * ((s + 1f) * k + s) + 1f;
        }

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
            float alpha = Smooth(0f, .12f, t) * (1f - outK);
            if (Group != null) Group.alpha = alpha;
            rect.anchoredPosition = _rest + new Vector2(0f, 28f * outK);

            if (Plate != null)
            {
                float k = Mathf.Clamp01(t / In);
                Plate.localScale = Vector3.one * Mathf.LerpUnclamped(PopScale, 1f, Back(k));
            }
            if (Flash != null)
            {
                // Короткая вспышка в первые доли секунды, потом слабое тёплое сияние за плашкой:
                // сцену вокруг не заливает.
                float burst = t < .06f ? t / .06f * .7f : Mathf.Lerp(.7f, .14f, Smooth(.06f, .45f, t));
                HudFx.SetAlpha(Flash, burst * (1f - outK));
                Flash.rectTransform.localScale = Vector3.one * (1f + .25f * Smooth(0f, .5f, t));
            }
            if (Rays != null)
            {
                HudFx.SetAlpha(Rays, .75f * Smooth(.05f, .35f, t) * (1f - outK));
                Rays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 14f);
            }
            if (Number != null)
            {
                bool clicked = t >= .2f;
                string text = (clicked ? _current.After : _current.Before) ?? string.Empty;
                if (Number.text != text) Number.text = text;
                float punch = clicked ? Mathf.Lerp(1.7f, 1f, UiMotion.EaseOut.Evaluate(Mathf.Clamp01((t - .2f) / .28f))) : .92f;
                Number.rectTransform.localScale = Vector3.one * punch;
            }
            for (int i = 0; i < GainLines.Length; i++)
            {
                TMP_Text line = GainLines[i];
                if (line == null) continue;
                float k = Mathf.Clamp01((t - (.38f + i * .12f)) / .24f);
                line.alpha = k;
                line.rectTransform.anchoredPosition = _gainRest[i] + new Vector2(26f * (1f - UiMotion.EaseOut.Evaluate(k)), 0f);
            }
            if (Sparks != null) Sparks.Apply(t - .06f);
            if (Glint != null) Glint.Apply((t - .3f) / .7f);
        }
    }
}
