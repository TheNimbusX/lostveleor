using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Живой портрет боевого HUD (владелец 29.09, вариант «рисованный + реакции»):
    /// <list type="bullet">
    /// <item>дыхание — рисунок в круге медленно подрастает на ~1 % от низа (окно выборки RawImage, круг и
    /// мягкая маска стоят на месте);</item>
    /// <item>удар — портрет целиком вздрагивает, рисунок на 2–3 кадра уходит в красный (умножением: не
    /// светлее, и после удара ничего не остаётся);</item>
    /// <item>лечение и новый уровень — тёплый свет вокруг портрета и за головой (свет, а не высветление
    /// самого Пелага);</item>
    /// <item>низкое здоровье — дыхание чаще и глубже в такт красной дымке <see cref="Danger"/>, лёгкий
    /// красный тон на вдохе.</item>
    /// </list>
    /// Время — часы интерфейса (<see cref="UiMotion.Now"/>, без масштаба времени). Без выделений памяти:
    /// ни замыканий, ни строк — только числа каждый кадр; вершины трогаются, только когда что-то сдвинулось.
    /// Кривые — <see cref="HudPortraitCurves"/> (проверяются тестами вне Unity). События даёт CombatHudView.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudPortraitMotion : MonoBehaviour
    {
        [Tooltip("Рисунок портрета: один RawImage с мягкой маской (UiInkPortrait)")] public RawImage Art;
        [Tooltip("Что вздрагивает от удара: узел портрета целиком (диск, свет, рисунок, уровень)")] public RectTransform Body;
        [Tooltip("Свет за головой: разгорается при лечении и новом уровне, в покое — как в префабе")] public Graphic BackLight;
        [Tooltip("Тёплый свет вокруг портрета (аддитивный): в покое не рисуется")] public Graphic Warm;
        [Tooltip("Красная дымка низкого здоровья: портрет дышит в её такт")] public HudPulse Danger;

        [Header("Дыхание")]
        [Tooltip("Секунд на вдох и выдох")] public float BreathPeriod = 3.8f;
        [Tooltip("Насколько рисунок подрастает на вдохе: 0,01 — 1 %")] [Range(0f, .04f)] public float BreathDepth = .01f;

        [Header("Удар")]
        [Tooltip("Насколько портрет отскакивает, единицы холста")] public float JoltDistance = 3.5f;
        [Tooltip("Направление отскока (вниз-влево — от удара)")] public Vector2 JoltDirection = new Vector2(-.6f, -.8f);
        [Tooltip("Секунд на отскок и возврат")] public float JoltTime = .24f;
        [Tooltip("Красный тон удара (умножается на рисунок)")] public Color HitColor = new Color(1f, .42f, .38f, 1f);
        [Tooltip("Сколько тон держится полностью, с (2–3 кадра)")] public float HitHold = .045f;
        [Tooltip("За сколько тон гаснет, с")] public float HitFade = .07f;

        [Header("Лечение и уровень")]
        [Tooltip("Сила тёплого света от лечения")] [Range(0f, 1f)] public float HealLight = .5f;
        [Tooltip("Сила тёплого света от нового уровня")] [Range(0f, 1f)] public float LevelLight = .9f;
        [Tooltip("Секунд на свет лечения")] public float HealTime = .75f;
        [Tooltip("Секунд на свет уровня")] public float LevelTime = 1.4f;
        [Tooltip("Насколько разгорается свет за головой на пике (к его силе в префабе)")] public float BackLightBoost = .45f;

        [Header("Тревога (здоровья ≤ 25%)")]
        [Tooltip("Глубина дыхания при тревоге")] [Range(0f, .04f)] public float AnxiousDepth = .018f;
        [Tooltip("Тон на вдохе при тревоге (умножается на рисунок)")] public Color AnxiousColor = new Color(1f, .8f, .76f, 1f);
        [Tooltip("Секунд на переход в тревогу и обратно")] public float AnxiousFade = .5f;

        /// <summary>Здоровья мало: ставит CombatHudView каждый кадр (там же включается красная дымка).</summary>
        public bool Anxious { get; set; }

        const float WarmRise = .12f;
        float _phase, _anxious, _lastNow = -1f;
        float _hitAt = float.NegativeInfinity, _warmAt = float.NegativeInfinity, _warmPeak, _warmTime = 1f;
        float _backRest = -1f;
        Vector2 _bodyRest;
        bool _hasRest;

        /// <summary>Удар по герою: отскок и красный тон на пару кадров.</summary>
        public void Hit() => _hitAt = UiMotion.Now;

        /// <summary>Лечение: тёплый свет; <paramref name="share"/> — вылеченная доля здоровья.</summary>
        public void Heal(float share) => Glow(HealLight * HudPortraitCurves.HealStrength(share), HealTime);

        /// <summary>Новый уровень: тёплый свет ярче и дольше.</summary>
        public void LevelUp() => Glow(LevelLight, LevelTime);

        void Glow(float peak, float duration)
        {
            float now = UiMotion.Now;
            // Свет уже ярче нового (уровень и лечение в одном кадре) — не сбивать его.
            if (peak <= CurrentWarmth(now)) return;
            _warmAt = now;
            _warmPeak = peak;
            _warmTime = Mathf.Max(.1f, duration);
        }

        float CurrentWarmth(float now) => _warmPeak * HudPortraitCurves.Warmth(now - _warmAt, WarmRise, _warmTime);

        void OnEnable()
        {
            if (Body != null && !_hasRest) { _bodyRest = Body.anchoredPosition; _hasRest = true; }
            if (BackLight != null && _backRest < 0f) _backRest = BackLight.color.a;
        }

        void OnDisable()
        {
            // HUD спрятали посреди удара или света — вернуть покой, иначе портрет так и останется сдвинутым.
            if (Body != null && _hasRest) Body.anchoredPosition = _bodyRest;
            if (Art != null) { Art.color = Color.white; Art.uvRect = new Rect(0f, 0f, 1f, 1f); }
            if (BackLight != null && _backRest >= 0f) HudFx.SetAlpha(BackLight, _backRest);
            if (Warm != null) Warm.enabled = false;
            _hitAt = _warmAt = float.NegativeInfinity;
            _lastNow = -1f;
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            // Шаг — по тем же часам интерфейса (при записи видео они идут шагом записи, а не реальным
            // временем); не больше 0,1 с — на склейке арены кадр длится секунды.
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            _anxious = Mathf.MoveTowards(_anxious, Anxious ? 1f : 0f, dt / Mathf.Max(.05f, AnxiousFade));

            // Дыхание: своя фаза (смена периода не рвёт движение); в тревоге — такт красной дымки.
            _phase = Mathf.Repeat(_phase + dt / Mathf.Max(.5f, BreathPeriod), 1f);
            float calm = HudPortraitCurves.Breath(_phase);
            float period = Danger != null ? Danger.Period : 1.05f;
            float pulse = HudPortraitCurves.PulseBreath(now, period);
            float breath = Mathf.Lerp(calm, pulse, _anxious);
            float scale = HudPortraitCurves.BreathScale(breath, Mathf.Lerp(BreathDepth, AnxiousDepth, _anxious));

            if (Art != null)
            {
                HudPortraitCurves.BreathWindow(scale, out float x, out float y, out float w, out float h);
                var window = new Rect(x, y, w, h);
                if (Art.uvRect != window) Art.uvRect = window;
                // «Вспышки и мерцание: Мягче» — красный тон удара слабее, толчок остаётся.
                float hit = HudPortraitCurves.HitTint(now - _hitAt, HitHold, HitFade) * GameUserSettings.FlashScale;
                Color tint = Color.Lerp(Color.white, AnxiousColor, _anxious * pulse);
                tint = Color.Lerp(tint, HitColor, hit);
                tint.a = 1f;
                if (Art.color != tint) Art.color = tint;
            }

            if (Body != null && _hasRest)
            {
                Vector2 offset = JoltDirection.normalized * (JoltDistance * HudPortraitCurves.Jolt(now - _hitAt, JoltTime));
                Vector2 place = _bodyRest + offset;
                if (Body.anchoredPosition != place) Body.anchoredPosition = place;
            }

            float warmth = CurrentWarmth(now);
            if (Warm != null)
            {
                bool on = warmth > .001f;
                if (Warm.enabled != on) Warm.enabled = on;
                if (on) HudFx.SetAlpha(Warm, warmth);
            }
            if (BackLight != null && _backRest >= 0f)
            {
                float alpha = Mathf.Clamp01(_backRest + BackLightBoost * warmth);
                if (!Mathf.Approximately(BackLight.color.a, alpha)) HudFx.SetAlpha(BackLight, alpha);
            }
        }
    }
}
