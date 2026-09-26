using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Живой разлом главного меню на фоне «Дым» (владелец, 26 сентября: светлый рассвет с отдельной
    /// трещиной поверх читался хуже — выбран минималистичный фон menu_smoke, шов огня нарисован в нём
    /// самом). Отдельной трещины больше нет: над нарисованным швом лежит его же свет, вынутый из фона
    /// (tools/ui-kit/make-menu-seam.py), — ядро с ближним заревом и дальнее зарево, свет «Дыма и
    /// света». Здесь они живут: ядро медленно и неровно мерцает, зарево дышит, изредка шов тихо
    /// «трескается» — вспыхивает и роняет пару искр из одного места; вдоль шва всплывают редкие угли.
    /// Наведение на пункт меню — мягкая вспышка шва и горсть искр по всей его длине. Весь фон с
    /// разломом медленно дышит масштабом (дальний план за неподвижными лого и меню).
    ///
    /// Огонь лого (владелец, 26 сентября: «анимировать огоньки — искры в нём»): слой огня трещины
    /// через «REMAINS» мерцает, изредка трескается — вспыхивает и роняет пару искр, — а вдоль
    /// трещины всплывают угли. Наведение на пункт меню отзывается и в лого, слабее.
    /// Угли и шва, и лого рождаются, только когда их свет уже проявился: до этого искры висели бы
    /// в пустоте.
    /// Всё спокойно, без стробоскопа: волны медленные и несоразмерные, вспышки гаснут долго.
    /// Часы — время интерфейса (UiMotion.Now), шаг не больше 0,1 с: меню стоит на паузе (timeScale = 0).
    /// </summary>
    public sealed class MainMenuRift : MonoBehaviour
    {
        [Tooltip("Свет шва: ядро и ближнее зарево (свет поверх фона)")] public Graphic Crack;
        [Tooltip("Дальнее зарево шва (свет поверх фона)")] public Graphic Glow;
        [Tooltip("Угли вдоль шва: по излучателю на отрезок")] public UiEmbers[] Embers;
        [Tooltip("Сила света шва в покое")] [Range(0f, 1f)] public float CrackRest = .15f;
        [Tooltip("Размах мерцания шва, доля силы покоя")] [Range(0f, 1f)] public float CrackFlicker = .3f;
        [Tooltip("Сила дальнего зарева в покое")] [Range(0f, 1f)] public float GlowRest = .12f;
        [Tooltip("Размах дыхания зарева")] [Range(0f, 1f)] public float Breath = .35f;
        [Tooltip("Секунд на вдох-выдох зарева")] public float Period = 5.5f;
        [Tooltip("Сколько вспышка (наведение) прибавляет свету шва")] [Range(0f, 1f)] public float FlareGain = .22f;
        [Tooltip("Секунд, за которые гаснет вспышка")] public float FlareFade = 1.1f;
        [Tooltip("Искр на вспышку (по всей длине шва)")] public int FlareSparks = 6;
        [Tooltip("Сколько «треск» шва прибавляет его свету")] [Range(0f, 1f)] public float SeamCrackle = .2f;
        [Tooltip("Секунд между «тресками» шва (от–до)")] public Vector2 SeamCrackleEvery = new Vector2(4.5f, 9f);
        [Tooltip("Искр на «треск» шва (от–до): из одного места")] public Vector2Int SeamCrackleSparks = new Vector2Int(2, 4);

        [Header("Фон")]
        [Tooltip("Картина фона со светом и углями: медленно дышит масштабом от шва")] public RectTransform Plate;
        [Tooltip("Размах дыхания фона, доля размера (только вверх от 1 — края экрана не открываются)")]
        [Range(0f, .05f)] public float PlateBreath = .012f;
        [Tooltip("Секунд на вдох-выдох фона")] public float PlatePeriod = 28f;

        [Header("Огонь лого")]
        [Tooltip("Слой огня трещины лого (свет поверх лого)")] public Graphic LogoGlow;
        [Tooltip("Угли вдоль трещины лого")] public UiEmbers[] LogoEmbers;
        [Tooltip("Сила огня лого в покое")] [Range(0f, 1f)] public float LogoRest = .7f;
        [Tooltip("Размах мерцания огня лого")] [Range(0f, 1f)] public float LogoFlicker = .22f;
        [Tooltip("Доля вспышки меню, что доходит до лого")] [Range(0f, 1f)] public float LogoFlare = .35f;
        [Tooltip("Секунд между «тресками» лого (от–до)")] public Vector2 CrackleEvery = new Vector2(2.5f, 5.5f);
        [Tooltip("Искр на «треск» (от–до)")] public Vector2Int CrackleSparks = new Vector2Int(2, 5);

        float _flare, _lastNow = -1f, _plateStart = -1f;
        Crackling _seam, _logo;
        float[] _seamRates, _logoRates;
        UiInkReveal _crackReveal, _logoReveal;

        /// <summary>«Треск»: вспышка через случайные промежутки, гаснет сама.</summary>
        struct Crackling
        {
            public float Level, Next;

            /// <summary>Шаг часов; true — пора трескаться (первый раз — не сразу после открытия).</summary>
            public bool Step(float now, float dt, Vector2 every, float fade)
            {
                Level = Mathf.MoveTowards(Level, 0f, dt / Mathf.Max(.05f, fade));
                if (Next <= 0f) Next = now + Random.Range(every.x, every.y);
                if (now < Next) return false;
                Next = now + Random.Range(every.x, every.y);
                return true;
            }
        }

        /// <summary>Вспышка: наведение на пункт меню.</summary>
        public void Flare(float strength = 1f)
        {
            _flare = Mathf.Max(_flare, strength);
            if (Shown(_crackReveal) < .5f) return;
            // Искры — по всей длине шва, по одной из случайных отрезков.
            int count = Mathf.RoundToInt(FlareSparks * strength);
            for (int i = 0; i < count; i++) Pick(Embers)?.Burst(1);
        }

        void Awake()
        {
            // Родная частота углей: дальше она множится на то, насколько проявлен их свет.
            _seamRates = Rates(Embers);
            _logoRates = Rates(LogoEmbers);
            if (Crack != null) _crackReveal = Crack.GetComponent<UiInkReveal>();
            if (LogoGlow != null) _logoReveal = LogoGlow.GetComponent<UiInkReveal>();
        }

        void OnDisable()
        {
            if (Plate != null) Plate.localScale = Vector3.one;
            _plateStart = -1f;
            _lastNow = -1f;
            // Меню открыли снова — первый «треск» опять не сразу.
            _seam = default;
            _logo = default;
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            _flare = Mathf.MoveTowards(_flare, 0f, dt / Mathf.Max(.05f, FlareFade));
            float flare = _flare * _flare * (3f - 2f * _flare);
            UpdatePlate(now);
            UpdateSeam(now, dt, flare);
            UpdateLogo(now, dt, flare);
        }

        /// <summary>Фон дышит масштабом от 1 вверх: с открытия меню, плавно с места.</summary>
        void UpdatePlate(float now)
        {
            if (Plate == null) return;
            if (_plateStart < 0f) _plateStart = now;
            float k = .5f - .5f * Mathf.Cos((now - _plateStart) * Mathf.PI * 2f / Mathf.Max(1f, PlatePeriod));
            float scale = 1f + PlateBreath * k;
            Plate.localScale = new Vector3(scale, scale, 1f);
        }

        void UpdateSeam(float now, float dt, float flare)
        {
            float shown = Shown(_crackReveal);
            if (_seam.Step(now, dt, SeamCrackleEvery, 1.4f) && shown > .9f)
            {
                _seam.Level = 1f;
                Pick(Embers)?.Burst(Random.Range(SeamCrackleSparks.x, SeamCrackleSparks.y + 1));
            }
            float crackle = _seam.Level * _seam.Level;

            // Мерцание — две медленные несоразмерные волны: шов тлеет неровно, без заметного такта.
            float flicker = .6f * Mathf.Sin(now * .9f) + .4f * Mathf.Sin(now * 2.3f + 1.3f);
            if (Crack != null)
                SetAlpha(Crack, Mathf.Clamp01(CrackRest * (1f + CrackFlicker * flicker) + SeamCrackle * crackle + FlareGain * flare));
            float breath = .5f + .5f * Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(.1f, Period));
            if (Glow != null)
                SetAlpha(Glow, Mathf.Clamp01(GlowRest * (1f - Breath + Breath * breath) + .4f * SeamCrackle * crackle + .45f * FlareGain * flare));

            SetRates(Embers, _seamRates, shown);
        }

        void UpdateLogo(float now, float dt, float flare)
        {
            if (LogoGlow == null) return;
            float shown = Shown(_logoReveal);
            // «Треск»: огонь вспыхивает и роняет пару искр. Первый — не сразу после проявления.
            if (_logo.Step(now, dt, CrackleEvery, .6f) && shown > .9f)
            {
                _logo.Level = 1f;
                Pick(LogoEmbers)?.Burst(Random.Range(CrackleSparks.x, CrackleSparks.y + 1));
            }

            // Мерцание — две несоразмерные волны: огонь дышит неровно, без заметного такта.
            float flicker = .6f * Mathf.Sin(now * 2.3f) + .4f * Mathf.Sin(now * 5.9f + 1.3f);
            float crackle = _logo.Level * _logo.Level;
            SetAlpha(LogoGlow, Mathf.Clamp01(LogoRest * (1f + LogoFlicker * flicker) + .35f * crackle + LogoFlare * flare));
            SetRates(LogoEmbers, _logoRates, shown);
        }

        static float[] Rates(UiEmbers[] embers)
        {
            if (embers == null) return null;
            var rates = new float[embers.Length];
            for (int i = 0; i < embers.Length; i++) rates[i] = embers[i] != null ? embers[i].Rate : 0f;
            return rates;
        }

        static void SetRates(UiEmbers[] embers, float[] rates, float shown)
        {
            if (embers == null || rates == null) return;
            for (int i = 0; i < embers.Length && i < rates.Length; i++)
                if (embers[i] != null) embers[i].Rate = rates[i] * shown;
        }

        static UiEmbers Pick(UiEmbers[] embers) =>
            embers != null && embers.Length > 0 ? embers[Random.Range(0, embers.Length)] : null;

        /// <summary>Насколько проявлен свет (1 — целиком); без данных проявления — виден сразу.</summary>
        static float Shown(UiInkReveal reveal) => reveal != null ? 1f - reveal.Hidden : 1f;

        static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            if (Mathf.Approximately(c.a, alpha)) return;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
