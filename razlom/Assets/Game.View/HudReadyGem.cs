using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Огонёк усилений и огненное кольцо способности в боевом HUD. Показывает две вещи сразу.
    ///
    /// Усиления (владелец, 24 сентября: ступени камня «нечитаемы» — выбран знак + 8 насечек;
    /// потом: ряд «показывать только при наведении»). Над плиткой дуга из 8 точек: сколько горит —
    /// столько усилений. Дуга видна только под мышью, в покое — один огонёк.
    /// 26 сентября (владелец: «треугольники и круги — выбрать фигуру») гранёный камень стал круглым
    /// огоньком: цвет ступенями — 0 тусклый, 1–2 сталь, 3–5 серебро, 6–7 золото, 8 кристалл с мягким
    /// дыханием. Старый префаб с рисованными камнями (<see cref="Grades"/>) работает как раньше.
    /// Взятое усиление — новая точка вспыхивает, огонёк вздрагивает.
    ///
    /// Огненное кольцо (владелец 26 сентября: «огня слишком много и везде поровну»): в перезарядке
    /// тонкий тлеющий уголь (<see cref="RestAlpha"/>), у готовой разгорается (<see cref="ReadyAlpha"/>),
    /// в момент «перезарядка → готово» и на нажатии коротко вспыхивает в полную силу. Там же по
    /// плитке проходит одна волна света. Часы неигровые, шаг не больше 0,1 с за кадр.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudReadyGem : MonoBehaviour
    {
        [Tooltip("Огонёк (или рисованный камень старого префаба); у кувырка пусто")] public Image Gem;
        [Tooltip("Рисунки камня по числу усилений: 0…8 (старый префаб). Пусто — огонёк красится цветом ступени")]
        public Sprite[] Grades = new Sprite[0];
        [Tooltip("Насечки усилений слева направо; горят первые N")] public Image[] Notches = new Image[0];
        [Tooltip("Ряд насечек целиком: виден только при наведении на плитку")] public CanvasGroup NotchGroup;
        [Tooltip("Секунд на появление и угасание ряда")] public float NotchFade = .12f;
        [Tooltip("Цвет горящей насечки и огонька по материалу: сталь, серебро, золото, кристалл")]
        public Color[] TierColours =
        {
            new Color(.78f, .8f, .86f, 1f), new Color(.93f, .96f, 1f, 1f), new Color(1f, .8f, .38f, 1f), new Color(1f, .96f, .82f, 1f),
        };
        [Tooltip("Негорящая насечка")] public Color NotchOff = new Color(.08f, .09f, .12f, .9f);
        [Tooltip("Огонёк без усилений")] public Color OrbNone = new Color(.36f, .38f, .44f, .75f);
        [Tooltip("Необязательно: сияние огонька (аддитивное); дышит на восьми усилениях и вспыхивает на новом")]
        public Image Halo;
        [Tooltip("Необязательно: волна света по плитке в момент готовности (аддитивная)")] public Image Burst;
        [Tooltip("Необязательно: огненное кольцо плитки")] public Image Frame;
        [Tooltip("Цвет кольца во вспышке (готовность, нажатие)")] public Color ReadyColour = new Color(1f, .9f, .72f, 1f);
        [Tooltip("Камень старого префаба на перезарядке")] public Color IdleColour = new Color(.62f, .62f, .66f, .55f);
        [Tooltip("Кольцо в перезарядке и без лавидия: тонкий уголь")] [Range(0f, 1f)] public float RestAlpha = .18f;
        [Tooltip("Кольцо у готовой способности")] [Range(0f, 1f)] public float ReadyAlpha = .42f;
        [Tooltip("Секунд на переход кольца между покоем и готовностью")] public float RingFade = .25f;
        [Tooltip("Секунд на угасание вспышки готовности")] public float FlashTime = .5f;

        bool _known, _ready, _hover;
        int _count = -1;
        float _flashAt = -100f, _haloFlashUntil, _press, _ringAlpha = -1f, _lastNow = -1f;
        Color _frameColour = Color.white;

        /// <summary>Огонёк вместо рисованного камня: цвет ступени, а не спрайт.</summary>
        bool Orb => Grades == null || Grades.Length == 0;

        void Awake()
        {
            if (Frame != null) _frameColour = Frame.color;
            if (Burst != null) Burst.enabled = false;
        }

        public static int TierOf(int count) => count >= 8 ? 3 : count >= 6 ? 2 : count >= 3 ? 1 : 0;

        /// <summary>Цвет огонька и горящих насечек при <paramref name="count"/> усилениях.</summary>
        Color TierColour(int count)
        {
            if (count <= 0) return OrbNone;
            return TierColours.Length > 0 ? TierColours[Mathf.Min(TierOf(count), TierColours.Length - 1)] : Color.white;
        }

        /// <summary>Сколько усилений у способности (0…8): насечки и цвет огонька.</summary>
        public void SetUpgrades(int count)
        {
            count = Mathf.Clamp(count, 0, Mathf.Max(Notches.Length, Grades.Length - 1));
            if (count == _count) return;
            bool grew = _count >= 0 && count > _count;
            int from = Mathf.Max(0, _count);
            _count = count;
            if (!Orb && Gem != null && count < Grades.Length && Grades[count] != null) Gem.sprite = Grades[count];
            Color lit = TierColour(Mathf.Max(1, count));
            for (int i = 0; i < Notches.Length; i++)
                if (Notches[i] != null) Notches[i].color = i < count ? lit : NotchOff;
            // Сияние огонька — цвета его ступени; прозрачность ведёт LateUpdate и вспышки.
            if (Orb && Halo != null)
            {
                Color glow = TierColour(count);
                glow.a = Halo.color.a;
                Halo.color = glow;
            }
            if (!grew) return;
            for (int i = from; i < count && i < Notches.Length; i++)
                if (Notches[i] != null) HudFx.Punch(Notches[i].transform, 2.1f, .45f);
            if (Gem != null) HudFx.Punch(Gem.transform, Orb ? 1.7f : 1.35f, .4f);
            if (Halo != null)
            {
                _haloFlashUntil = Time.unscaledTime + .7f;
                HudFx.Flash(Halo, .9f, .7f);
            }
        }

        /// <summary>Мышь над плиткой — показать ряд насечек.</summary>
        public void SetHover(bool hover) => _hover = hover;

        /// <summary>Нажатие: 1 — кольцо вспыхнуло, дальше гаснет к нулю (ведёт CombatHudView).</summary>
        public void SetPress(float amount) => _press = Mathf.Clamp01(amount);

        public void SetReady(bool ready)
        {
            if (_known && ready && !_ready)
            {
                _flashAt = UiMotion.Now;
                if (Burst != null) HudFx.Burst(Burst, .85f, .8f, 1.35f, .5f);
            }
            _known = true;
            _ready = ready;
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float step = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            if (Gem != null)
            {
                // Огонёк горит цветом ступени и в откате притухает. Рисованный камень старого префаба
                // несёт свой материал — готовый не красится, только тускнеет в откате.
                Color c = Orb ? OrbColour(Mathf.Max(0, _count), _ready) : _ready ? Color.white : IdleColour;
                if (Gem.color != c) Gem.color = c;
            }
            // Сияние: огонёк с усилениями светится тихо, кристалл (8) дышит; вспышку нового усиления ведёт HudFx.
            bool flashing = Time.unscaledTime < _haloFlashUntil;
            if (Halo != null && !flashing)
            {
                bool lit = _count >= 8 || Orb && _count > 0;
                if (Halo.enabled != lit) Halo.enabled = lit;
                if (lit)
                {
                    float breath = .5f + .5f * Mathf.Sin(now * Mathf.PI * 2f / 2.6f);
                    float alpha = _count >= 8 ? (_ready ? .32f : .14f) + .18f * breath : _ready ? .22f : .08f;
                    HudFx.SetAlpha(Halo, alpha);
                }
            }
            if (Frame != null)
            {
                float target = _ready ? ReadyAlpha : RestAlpha;
                _ringAlpha = _ringAlpha < 0f ? target : Mathf.MoveTowards(_ringAlpha, target, step / Mathf.Max(.02f, RingFade));
                float flash = 1f - Mathf.Clamp01((now - _flashAt) / Mathf.Max(.05f, FlashTime));
                Color ring = RingColour(_ringAlpha, Mathf.Max(flash, _press));
                // Цвет трогается только при изменении: иначе кольцо пересобиралось бы каждый кадр.
                if (Frame.color != ring) Frame.color = ring;
            }
            if (NotchGroup != null)
                NotchGroup.alpha = Mathf.MoveTowards(NotchGroup.alpha, _hover ? 1f : 0f, step / Mathf.Max(.02f, NotchFade));
        }

        /// <summary>Огонёк: цвет ступени, в откате темнее и прозрачнее.</summary>
        Color OrbColour(int count, bool ready)
        {
            Color c = TierColour(count);
            return ready ? c : new Color(c.r * .7f, c.g * .7f, c.b * .7f, c.a * .6f);
        }

        /// <summary>Цвет кольца: покой с прозрачностью <paramref name="alpha"/>, вспышка <paramref name="lit"/> — к ReadyColour.</summary>
        Color RingColour(float alpha, float lit)
        {
            Color rest = _frameColour;
            rest.a = alpha;
            return Color.Lerp(rest, ReadyColour, Mathf.Clamp01(lit));
        }

        /// <summary>Кадр редактора: число усилений, готовность, наведение и вспышка кольца без анимаций.</summary>
        public void Preview(int count, bool ready, bool hover = false, float flash = 0f)
        {
            _hover = hover;
            if (NotchGroup != null) NotchGroup.alpha = hover ? 1f : 0f;
            if (Frame != null && _ringAlpha < 0f) _frameColour = Frame.color;
            _count = -1;
            SetUpgrades(count);
            _ready = ready;
            _ringAlpha = ready ? ReadyAlpha : RestAlpha;
            if (Frame != null) Frame.color = RingColour(_ringAlpha, flash);
            if (Gem != null) Gem.color = Orb ? OrbColour(count, ready) : ready ? Color.white : IdleColour;
            if (Halo != null)
            {
                Halo.enabled = count >= 8 || Orb && count > 0;
                HudFx.SetAlpha(Halo, count >= 8 ? (ready ? .42f : .2f) : ready ? .22f : .08f);
            }
        }
    }
}
