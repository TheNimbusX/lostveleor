using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Полоски здоровья над врагами.
    ///
    /// ПОЯВЛЯЮТСЯ ПО УДАРУ И ГАСНУТ САМИ. Постоянные полоски над сорока телами
    /// превратили бы кадр в диаграмму и залезли бы в середину экрана, которая
    /// в этой игре свободна всегда. Полоска нужна ровно тогда, когда игрок
    /// начал кого-то бить и хочет понять, добьёт он его или нет.
    ///
    /// Рисуется из пула, всегда лицом к камере. Вид — материал «Дым и свет», как
    /// полоса героя в боевом HUD (владелец 26 сентября: «перевести вообще всё»):
    /// тёмная дымная дорожка, заливка — мазок кистью цвета здоровья, обрезанный по
    /// доле (мазок не сжимается), без серебряного контура; у элиты на конце заливки —
    /// светящийся огонёк-круг вместо ромба. Шейдер «Дыма и света» в мире не работает,
    /// поэтому дым и мазки заранее вырезаны из пака: Resources/UI/HUD/EnemyBar*.png
    /// (tools/ui-kit/make-enemy-bars.py). Нет их — прежний вид пака «Ночная акварель»
    /// из UiTheme (Resources), поэтому работает и в сборке.
    ///
    /// ПОЛОСА ЭЛИТЫ — «РОГА» (владелец 29.09, выбор «5 — Рога» из второго круга концептов,
    /// ART/characters/act-1-enemies/review/mobs-v2-round2-2026-09-29/elite-bar/05-*). Мазок и дымная
    /// дорожка прежние, только длиннее и выше обычной полосы; оба конца вырастают в костяные рога,
    /// загнутые вверх (Resources/UI/HUD/EliteBarAntler.png, левый; правый — он же, отражённый;
    /// tools/ui-kit/make-elite-bar-mark.py); заливка тёмно-алая; огонёк — ровно на конце заливки;
    /// внутри — цифры «1240 / 2000» (Nunito, EliteBarLayout.Numbers). Рогатый череп у левого края
    /// владелец отверг — его больше нет. Имя — табличкой над полосой, между рогами (RunWorldView берёт
    /// место из TryGetNameAnchor).
    ///
    /// ВСЕГДА НАД МОБОМ. Раньше полоска висела на постоянной высоте из таблицы вида и рисовалась с
    /// обычным тестом глубины — у высоких (Вендиго, Шипомёт) и в позах замаха тонула в модели.
    /// Теперь высота считается каждый кадр от макушки тела с зазором (MeasureTop, EliteBarLayout),
    /// а все части рисуются поверх мира (материал с ZTest Always).
    ///
    /// ПРЯМО НАД ГОЛОВОЙ, А НЕ В ВОЗДУХЕ (29.09, кадр wendigo-tank: полоса элиты в 85–90 пикселях
    /// над рогами). Макушку мерили по углам границ рендереров: камера смотрит сверху под 48°, и
    /// глубина коробки тела × tg 48° добавляла ей до метра. Теперь макушка — самая высокая на экране
    /// кость кожи плюс то, что торчит над ней у этого вида (BoneReach: рога над черепом, горб, панцирь),
    /// а прежняя коробка осталась только потолком. Зазор до низа полоски — ≈19 пикселей при 1080p.
    ///
    /// СЛЕД УРОНА (этап 4, п. 1; кадр 1a, 30.09): как у полосы героя (HudBarAnim) и босса (WcBar.Trail) —
    /// светлый отрезок держит здоровье до удара ~0,35 с и стекает к новому; лечение следа не даёт.
    /// Здоровье каждой живой сущности отслеживается и без полоски: первый же удар, который её показал,
    /// уже со следом. Логика шага — HealthBarTrail (тесты вне Unity).
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(950)]
    public sealed class HealthBars : MonoBehaviour
    {
        [Header("Вид")]
        public float Width = 1.05f;
        public float Height = 0.13f;
        [Tooltip("Зазор заливки внутри дорожки, метры")]
        public float Inset = 0.022f;

        [Header("Элита")]
        [Tooltip("Длина полосы элиты (плотная часть дорожки), метры: ≈190 пикселей при 1080p в бою, как в концепте «Рога»; с рогами ≈235")]
        public float EliteWidth = 2.2f;
        [Tooltip("Высота полосы элиты, метры (≈26 пикселей при 1080p в бою)")]
        public float EliteHeight = 0.3f;
        [Tooltip("Огонёк на конце заливки элиты, метры")]
        public float EliteGem = 0.34f;
        [Tooltip("Рог на конце полосы элиты: высота холста рога в высотах полосы (1,95 — ≈50 пикселей при 1080p, сам рог ≈45)")]
        public float EliteAntler = 1.95f;
        [Tooltip("Насколько основание рога заходит внутрь полосы от её конца, в высотах полосы: срез рога закрывает начало мазка")]
        public float AntlerTuck = .33f;
        [Tooltip("Высота основания рога над серединой полосы, в высотах полосы (минус — ниже): рог растёт из нижней половины конца, как в концепте")]
        public float AntlerLift = -.1f;
        [Tooltip("Кегль цифр элиты: высота em, метры (0,2 — цифры ≈12 пикселей при 1080p)")]
        public float EliteNumbersSize = 0.2f;
        [Tooltip("Цифры: тёплый светлый, как свет огонька")]
        public Color NumbersColor = new Color(1f, .95f, .86f, 1f);
        [Tooltip("Обводка цифр: чернила дорожки — читаются и на заливке, и на дыме")]
        public Color NumbersOutline = new Color32(0x0B, 0x10, 0x16, 0xF2);

        [Header("Над макушкой")]
        [Tooltip("Зазор между макушкой модели на экране и низом полоски (у элиты — низом полосы или рогов, что ниже), метры в плоскости полоски, как Height: 0,22 — ≈19 пикселей при 1080p в бою")]
        public float HeadGap = 0.22f;
        [Tooltip("Как быстро полоска поднимается к выросшей макушке, 1/с")]
        public float AnchorRise = 18f;
        [Tooltip("Как быстро опускается, 1/с: медленно, чтобы не прыгать за каждым взмахом")]
        public float AnchorFall = 3f;

        // Середина обычной полоски, когда замерить модель нельзя (нет рендереров, тело ещё в земле),
        // м над точкой сущности; вдвое выше — потолок замера (EliteBarLayout.Target). Замерено по FBX
        // 29.09: макушка модели на экране в покое (среднее по разворотам, в метрах по вертикали) +
        // зазор и полполоски — страж 2,56, корнеполз 1,25, бутон 1,49, вендиго 3,45 (рога),
        // камнекопыт 1,63, Шипомёт 2,82, Корнехват 1,66, Расщепень 1,57, детёныш 0,94.
        [Tooltip("Середина полоски над телом, когда замерить модель нельзя: страж и прочие.")]
        public float Height3D = 3f;
        public float RootSwarmHeight3D = 1.65f;
        public float BudHeight3D = 1.9f;
        public float WendigoHeight3D = 3.9f;
        public float StonehoofHeight3D = 2.05f;
        public float ThorncasterHeight3D = 3.25f;
        public float RootSnarerHeight3D = 2.1f;
        public float SplitterHeight3D = 2f;
        public float SplitlingHeight3D = 1.35f;

        [Tooltip("Дорожка: чернильный дым, как у полос HUD (роль Smoke).")]
        public Color BackColor = new Color32(0x12, 0x19, 0x23, 0xEB);
        public Color FillColor = new Color32(0xE0, 0x46, 0x34, 0xF2);
        [Tooltip("Заливка элиты: тёмно-алая, как в концепте «Рога» (в кадре после цветокоррекции ≈ #941C1E).")]
        public Color EliteColor = new Color32(0xA6, 0x1C, 0x2B, 0xFF);
        [Tooltip("Серебряный контур прежнего вида (только без спрайтов «Дыма и света»).")]
        public Color FrameColor = new Color32(0xD8, 0xE1, 0xEE, 0x90);

        [Tooltip("Цель, которую бьют прямо сейчас, отмечается ярче.")]
        public Color FocusColor = new Color32(0xFF, 0x6A, 0x4A, 0xFF);

        [Tooltip("Огонёк элиты: тёплый свет (как вспышка готовности способности в HUD).")]
        public Color OrbColor = new Color(1f, .9f, .72f, 1f);
        [Tooltip("Сияние вокруг огонька; альфа — сила, свет дышит вокруг неё.")]
        public Color OrbGlowColor = new Color(1f, .5f, .2f, .7f);

        [Header("След урона")]
        [Tooltip("Сколько секунд светлый след держит здоровье до удара, прежде чем стечь (как у полосы героя и босса)")]
        public float TrailDelay = .35f;
        [Tooltip("Скорость стекания следа, долей полоски в секунду")]
        public float TrailSpeed = 1.2f;
        [Tooltip("След: тёплый светлый, как след полосы героя; прозрачность — его сила")]
        public Color TrailColor = new Color(1f, .9f, .76f, .82f);

        [Header("Время")]
        [Tooltip("Сколько секунд полоска висит после последнего попадания.")]
        public float ShowFor = 2.4f;

        [Tooltip("За сколько секунд до конца полоска начинает гаснуть.")]
        public float FadeFor = 0.5f;

        [Tooltip("Потолок одновременно видимых полосок.")]
        public int MaxBars = 24;

        // Спрайты «Дыма и света» и их раскладка (tools/ui-kit/make-enemy-bars.py, числа — оттуда):
        // плотная часть дорожки — середина 448×64 холста 512×128, у заливки плотная часть — 40 из
        // 64 по высоте (холст ложится на полную высоту полоски), огонёк — круг 44 из 64.
        private const string TrackPath = "UI/HUD/EnemyBarTrack";
        private const string FillPath = "UI/HUD/EnemyBarFill";
        private const string OrbPath = "UI/HUD/EnemyBarOrb";
        private const string GlowPath = "UI/HUD/EnemyBarGlow";
        private const float TrackSpanX = 512f / 448f, TrackSpanY = 128f / 64f;
        private const float OrbDisc = .6f, OrbSpan = OrbDisc * 64f / 44f, GlowSpan = 2f;
        // Ступеней обрезки мазка: доля здоровья выбирает готовый спрайт, в кадре ничего не создаётся.
        private const int FillSteps = 128;
        // Рог элиты (make-elite-bar-mark.py печатает эти числа): точка крепления — центр среза основания,
        // в долях холста от левого нижнего угла; сколько рога ниже неё — в долях высоты холста.
        private const string AntlerPath = "UI/HUD/EliteBarAntler";
        private static readonly Vector2 AntlerPivot = new Vector2(.856f, .205f);
        private const float AntlerBelow = .153f;

        // Порядок частей внутри полоски. След урона — между дорожкой и мазком: начинается у конца
        // заливки. Рога — над мазком (их срез закрывает начало заливки), но под
        // огоньком: при почти пустой полосе огонёк у левого рога виден. Части элиты сдвинуты на
        // EliteOrder: её полоса ложится поверх обычных, если они пересеклись на экране. Цифры урона
        // (6100) — поверх всех полосок.
        private const int OrderTrack = 4000, OrderTrail = 4001, OrderFill = 4002, OrderFrame = 4003, OrderAntler = 4004;
        private const int OrderGlow = 4005, OrderOrb = 4006, OrderNumbers = 4007;
        // Отрезок следа — кусок середины мазка заливки (доли ширины холста), растянутый от конца
        // заливки до конца следа; чуть заходит под заливку, чтобы на срезе мазка не было щели.
        private const float TrailSliceFrom = .45f, TrailSliceWidth = .1f, TrailTuck = .012f;
        private const int EliteOrder = 20;
        private const int NeverFrame = -100;

        // Тест глубины UI/Default берётся из этого свойства; UGUI ставит его глобально, материал — перекрывает.
        private static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int TextureSampleAddId = Shader.PropertyToID("_TextureSampleAdd");

        private TickDriver _driver;
        private ArenaView _arena;
        private Transform _camera;

        private struct Bar
        {
            public Transform Root;
            public Transform Fill;
            public SpriteRenderer BackRenderer;
            public SpriteRenderer FillRenderer;
            // След урона: отрезок от конца заливки до конца следа.
            public SpriteRenderer TrailRenderer;
            public SpriteRenderer FrameRenderer;
            public Transform Gem;
            public SpriteRenderer GemFill;
            public SpriteRenderer GemRim;

            // Элита: рога на концах и цифры (цифры создаются при первой элите на этой полоске).
            public SpriteRenderer AntlerLeft;
            public SpriteRenderer AntlerRight;
            public TextMeshPro Numbers;
            public int ShownHealth, ShownMax;
            public float ShownAlpha;
            // Порядок частей сейчас сдвинут под элиту.
            public bool EliteOrdered;
        }

        private Bar[] _bars;
        private Sprite _quad;
        private Material _overlay, _numbersMaterial;

        // «Дым и свет»: дорожка, ступени заливки (i — доля (i + 1) / FillSteps), огонёк и сияние.
        private bool _ink;
        private Sprite _track, _orb, _glow, _antler, _trailSlice;
        private Sprite[] _fillSteps;

        // След урона по сущностям; сбрасывается, когда меняется симуляция (новая арена, забег).
        private BarTrailState[] _trail;
        private Simulation _trailSim;

        // Когда по кому в последний раз попали. Индекс — сущность.
        private float[] _hitAt;
        private int _focus = -1;
        private bool _ready;

        // Высота середины полоски над землёй (сглаженная) и кадр, когда её считали. Индекс — сущность.
        private float[] _anchor;
        private int[] _anchorFrame;
        // Тело, которое меряется: пул ArenaView переиспользует тела между сущностями.
        private Transform[] _bodyView;
        private BodyShape[] _bodyShape;
        // Разбор тела — один на объект тела: тело из пула приходит к новой сущности уже разобранным.
        private readonly Dictionary<Transform, BodyShape> _shapes = new Dictionary<Transform, BodyShape>();
        private const int MaxShapes = 256;
        // Низ таблички с именем элиты (RunWorldView) и кадр, когда его выставили.
        private Vector3[] _nameAnchor;
        private int[] _nameFrame;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnDestroy()
        {
            if (_overlay != null) Destroy(_overlay);
            if (_numbersMaterial != null) Destroy(_numbersMaterial);
        }

        /// <summary>Во сколько раз корень полоски элиты шире и выше обычной.</summary>
        private Vector3 EliteScale => new Vector3(EliteWidth / Mathf.Max(.001f, Width), EliteHeight / Mathf.Max(.001f, Height), 1f);

        /// <summary>
        /// Сколько полосы элиты ниже её середины, м: половина полосы или основание рога, что ниже
        /// (EliteBarLayout.EliteBelow). По этому низу полоса встаёт над макушкой.
        /// </summary>
        private float EliteBelowCenter => EliteBarLayout.EliteBelow(EliteHeight, EliteAntler * EliteHeight, AntlerLift * EliteHeight, AntlerBelow);

        /// <summary>
        /// Где низ таблички с именем элиты: верх её полосы, в мире — табличка встаёт между рогами.
        /// Полосы не видно (элита далеко и её не били) — там, где полоса была бы. Считается в
        /// LateUpdate полосок (порядок 950):
        /// RunWorldView (2050) читает в том же кадре, уже после камеры. false — элиту в этом кадре не
        /// считали (мертва, не элита, нет симуляции).
        /// </summary>
        public bool TryGetNameAnchor(int entity, out Vector3 world)
        {
            world = default;
            if (_nameFrame == null || (uint)entity >= (uint)_nameFrame.Length || _nameFrame[entity] != Time.frameCount) return false;
            world = _nameAnchor[entity];
            return true;
        }

        private void Build()
        {
            _ready = true;
            _camera = Camera.main != null ? Camera.main.transform : null;
            _arena = GetComponent<ArenaView>();
            _quad = MakeQuadSprite();
            _overlay = OverlayMaterial();
            _ink = LoadInk();
            LoadAntler();

            Transform root = new GameObject("Пул: полоски здоровья").transform;
            root.SetParent(transform, false);

            int capacity = TickDriver.MaxSimCapacity;
            _hitAt = new float[capacity];
            for (int i = 0; i < _hitAt.Length; i++) _hitAt[i] = -999f;
            _anchor = new float[capacity];
            _anchorFrame = new int[capacity];
            _bodyView = new Transform[capacity];
            _bodyShape = new BodyShape[capacity];
            _nameAnchor = new Vector3[capacity];
            _nameFrame = new int[capacity];
            _trail = new BarTrailState[capacity];
            // Кадров «никогда»: -1 совпал бы с «прошлым кадром» на кадре 0.
            for (int i = 0; i < capacity; i++) _anchorFrame[i] = _nameFrame[i] = NeverFrame;

            _bars = new Bar[Mathf.Max(1, MaxBars)];
            for (int i = 0; i < _bars.Length; i++) _bars[i] = MakeBar(root, i);
        }

        private void LateUpdate()
        {
            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                if (_ready) HideFrom(0);
                return;
            }

            if (!_ready) Build();
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            // Съёмка ролика без полосок (-capture-no-bars): пул спрятан, место таблички элиты
            // не считается — RunWorldView под тем же флагом табличку не ставит.
            // Настройка «Полоски здоровья врагов» режет врагов поштучно в Draw: при «Нет» остаются
            // только манекены лагеря (это тренировка, не враги), табличку элиты RunWorldView прячет сам.
            if (CaptureRig.NoBars)
            {
                HideFrom(0);
                return;
            }

            TrackHits();
            Draw(sim);
        }

        /// <summary>
        /// Отмечает, кого задели. Урон по времени тоже считается: горящий враг
        /// должен показывать, сколько ему осталось, — иначе непонятно, ждать
        /// его смерти или бить дальше.
        /// </summary>
        private void TrackHits()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];

                bool isDamage = e.Type == SimEventType.Damage
                                || e.Type == SimEventType.DamageOverTime;
                if (!isDamage) continue;
                if (e.Target == Simulation.PlayerId) continue;
                if ((uint)e.Target >= (uint)_hitAt.Length) continue;

                _hitAt[e.Target] = Time.unscaledTime;

                // Цель прямого удара игрока — та, за которой он следит.
                if (e.Type == SimEventType.Damage && e.Source == Simulation.PlayerId)
                    _focus = e.Target;
            }
        }

        private void Draw(Simulation sim)
        {
            EntityStore entities = sim.Entities;
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            int frame = Time.frameCount;
            int used = 0;
            float inner = Width - Inset * 2f;
            // Сияние огонька дышит, как свет «Дыма и света» в HUD (пульс шейдера ≈ 0,14).
            float breath = .86f + .14f * Mathf.Sin(now * 2.1f);
            // Верх экрана в мире: полоска повёрнута к камере, её «вверх» — вверх камеры.
            Vector3 up = _camera != null ? _camera.up : Vector3.up;
            Vector3 eliteScale = EliteScale;

            // Другая симуляция — индексы сущностей принадлежат другим телам: следы с нуля.
            if (_trailSim != sim)
            {
                _trailSim = sim;
                System.Array.Clear(_trail, 0, _trail.Length);
            }

            // Цикл идёт по всем: даже при полном пуле табличке элиты нужно место над её макушкой.
            for (int i = 0; i < entities.Count; i++)
            {
                if (i == Simulation.PlayerId) continue;
                if ((uint)i >= (uint)_hitAt.Length) continue;
                // Мёртвый — след забывается: место в пуле сущностей займёт новое тело.
                if (!entities.Alive[i]) { _trail[i].Ready = false; continue; }

                int max = entities.MaxHealth[i];
                if (max <= 0) continue;

                // След урона ведётся и без полоски: удар, который её покажет, уже со следом.
                HealthBarTrail.Step(ref _trail[i], entities.Health[i] / (float)max, dt, TrailDelay, TrailSpeed);

                float age = now - _hitAt[i];
                var dummy = CampTrainingView.Find(i);
                bool nearbyDummy = dummy != null && CampPlayerView.Instance != null
                    && CampTrainingView.IsNear(dummy, CampPlayerView.Instance.Position);
                bool elite = _driver.Run?.Encounters?.IsElite(i) == true;
                // Настройка: «Элита» — полоски только у элиты, «Нет» — ни у кого (и табличке
                // элиты место не считается). Манекен лагеря — тренировка, его полоска остаётся всегда.
                if (dummy == null && !GameUserSettings.ShowsEnemyBar(elite)) continue;
                bool nearbyElite = elite && FixVec2.DistanceSq(entities.Position[i], entities.Position[Simulation.PlayerId]) < Fix64.FromInt(256);
                bool shown = (age <= ShowFor || nearbyElite || nearbyDummy) && used < _bars.Length;
                if (!shown && !elite) continue;

                // Середина полоски: над макушкой модели этого кадра (манекен знает своё место сам).
                Vector3 center = dummy != null ? dummy.BarPosition : BarCenter(i, entities.Kind[i], elite, up, dt, frame);
                if (elite)
                {
                    // Низ таблички имени — сразу над полосой: рога стоят по краям, табличка — между ними.
                    float lift = EliteHeight * .5f + .03f;
                    _nameAnchor[i] = center + up * lift;
                    _nameFrame[i] = frame;
                }
                if (!shown) continue;

                float fill = Mathf.Clamp01(entities.Health[i] / (float)max);

                // Полная полоска не показывается: если по врагу попали, но он
                // ещё цел, полоска всё равно нужна — она и говорит, что цел.
                float alpha = !nearbyElite && !nearbyDummy && age > ShowFor - FadeFor
                    ? Mathf.InverseLerp(ShowFor, ShowFor - FadeFor, age)
                    : 1f;

                ref Bar bar = ref _bars[used++];
                bar.Root.gameObject.SetActive(true);
                bar.Root.localScale = elite ? eliteScale : Vector3.one;
                SetEliteOrder(ref bar, elite);

                bar.Root.position = center;
                if (_camera != null) bar.Root.rotation = _camera.rotation;

                bar.BackRenderer.color = Faded(BackColor, alpha);
                if (bar.FrameRenderer != null) bar.FrameRenderer.color = Faded(FrameColor, alpha);
                bar.FillRenderer.color = Faded(elite ? EliteColor : i == _focus ? FocusColor : FillColor, alpha);
                bar.Fill.gameObject.SetActive(fill > .001f);

                // Где кончается заливка: там едет огонёк элиты.
                float end;
                if (_ink)
                {
                    // Мазок во всю длину, обрезанный справа по доле: готовая ступень, не сжатие.
                    int step = Mathf.Clamp(Mathf.CeilToInt(fill * FillSteps), 1, FillSteps);
                    Sprite sprite = _fillSteps[step - 1];
                    if (bar.FillRenderer.sprite != sprite) bar.FillRenderer.sprite = sprite;
                    end = -inner * .5f + inner * step / FillSteps;
                }
                else
                {
                    // Заливка — капсула от левого края дорожки. Короче своей высоты
                    // капсула сминается, поэтому ширина не меньше высоты.
                    float h = Height - Inset * 2f;
                    float w = Mathf.Max(h, inner * fill);
                    bar.FillRenderer.size = new Vector2(w, h);
                    bar.Fill.localPosition = new Vector3(-inner * .5f + w * .5f, 0f, -.001f);
                    end = -inner * .5f + w;
                }
                DrawTrail(ref bar, in _trail[i], fill, end, inner, alpha);

                bar.Gem.gameObject.SetActive(elite && fill > .001f);
                if (elite)
                {
                    // Дети корня элиты живут в метрах: корень растянут неровно, они сжимаются обратно.
                    bar.Gem.localScale = new Vector3(EliteGem / eliteScale.x, EliteGem / eliteScale.y, 1f);
                    bar.Gem.localPosition = new Vector3(end, 0f, -.003f);
                    if (_ink)
                    {
                        bar.GemFill.color = Faded(OrbColor, alpha);
                        bar.GemRim.color = Faded(OrbGlowColor, alpha * breath);
                    }
                    else
                    {
                        bar.GemFill.color = Faded(Color.white, alpha);
                        bar.GemRim.color = Faded(EliteColor, alpha);
                    }
                }
                DrawAntlers(ref bar, elite, alpha, eliteScale);
                DrawNumbers(ref bar, elite, entities.Health[i], max, alpha, eliteScale);
            }

            HideFrom(used);
        }

        /// <summary>
        /// Рога на концах полосы элиты: основание — на AntlerTuck внутрь от конца полосы и на AntlerLift
        /// от её середины, рог уходит наружу и вверх. Правый — тот же спрайт, отражённый вокруг точки
        /// крепления (отрицательный масштаб; UI/Default рисует обе стороны). Размеры — в высотах полосы:
        /// рога растут и сжимаются вместе с ней. Корень растянут неровно — масштаб делится обратно.
        /// </summary>
        private void DrawAntlers(ref Bar bar, bool elite, float alpha, Vector3 eliteScale)
        {
            if (bar.AntlerLeft == null) return;
            if (bar.AntlerLeft.gameObject.activeSelf != elite)
            {
                bar.AntlerLeft.gameObject.SetActive(elite);
                bar.AntlerRight.gameObject.SetActive(elite);
            }
            if (!elite) return;
            // Холст рога ложится на EliteAntler высот полосы.
            float size = EliteAntler * EliteHeight / Mathf.Max(.001f, _antler.bounds.size.y);
            float x = (EliteWidth * .5f - AntlerTuck * EliteHeight) / eliteScale.x;
            float y = AntlerLift * EliteHeight / eliteScale.y;
            Vector3 scale = new Vector3(size / eliteScale.x, size / eliteScale.y, 1f);
            bar.AntlerLeft.transform.localScale = scale;
            bar.AntlerLeft.transform.localPosition = new Vector3(-x, y, -.002f);
            scale.x = -scale.x;
            bar.AntlerRight.transform.localScale = scale;
            bar.AntlerRight.transform.localPosition = new Vector3(x, y, -.002f);
            Color color = Faded(Color.white, alpha);
            bar.AntlerLeft.color = color;
            bar.AntlerRight.color = color;
        }

        /// <summary>
        /// След урона: светлый отрезок от конца заливки до доли следа (HealthBarTrail). «Дым и свет» — кусок
        /// середины мазка заливки, растянутый по длине (та же высота и плотность, что у мазка); прежний
        /// вид — капсула пака. Следа нет (не били, стёк, лечение) — отрезок спрятан.
        /// </summary>
        private void DrawTrail(ref Bar bar, in BarTrailState trail, float fill, float end, float inner, float alpha)
        {
            SpriteRenderer renderer = bar.TrailRenderer;
            if (renderer == null) return;
            float stop = _ink
                ? -inner * .5f + inner * Mathf.Clamp(Mathf.CeilToInt(trail.Trail * FillSteps), 1, FillSteps) / FillSteps
                : -inner * .5f + Mathf.Max(Height - Inset * 2f, inner * trail.Trail);
            bool on = HealthBarTrail.Visible(in trail) && trail.Trail > fill && stop - end > .004f;
            if (renderer.gameObject.activeSelf != on) renderer.gameObject.SetActive(on);
            if (!on) return;

            float start = end - TrailTuck;
            float length = stop - start;
            Transform t = renderer.transform;
            if (_ink)
            {
                Vector2 size = _trailSlice.bounds.size;
                t.localScale = new Vector3(length / Mathf.Max(.0001f, size.x), Height / Mathf.Max(.0001f, size.y), 1f);
                t.localPosition = new Vector3(start, 0f, -.0005f);
            }
            else
            {
                float h = Height - Inset * 2f;
                float w = Mathf.Max(h, length);
                renderer.size = new Vector2(w, h);
                t.localPosition = new Vector3(start + w * .5f, 0f, -.0005f);
            }
            renderer.color = Faded(TrailColor, alpha);
        }

        /// <summary>
        /// Цифры «1240 / 2000» внутри полосы элиты (владелец 29.09). Текст пересобирается только когда
        /// меняется здоровье; кегль ужимается сам, если число длиннее обычного.
        /// </summary>
        private void DrawNumbers(ref Bar bar, bool elite, int health, int max, float alpha, Vector3 eliteScale)
        {
            if (elite && bar.Numbers == null) bar.Numbers = MakeNumbers(bar.Root, OrderNumbers + (bar.EliteOrdered ? EliteOrder : 0));
            TextMeshPro text = bar.Numbers;
            if (text == null) return;
            if (text.gameObject.activeSelf != elite) text.gameObject.SetActive(elite);
            if (!elite) return;

            text.transform.localScale = new Vector3(1f / eliteScale.x, 1f / eliteScale.y, 1f);
            // По середине полосы, между основаниями рогов.
            text.transform.localPosition = new Vector3(0f, 0f, -.005f);
            float free = EliteWidth - 2f * (AntlerTuck + .5f) * EliteHeight;
            text.rectTransform.sizeDelta = new Vector2(Mathf.Max(.2f, free), EliteHeight * 1.6f);
            text.fontSizeMax = EliteNumbersSize * 10f;
            text.fontSizeMin = EliteNumbersSize * 6f;
            if (health != bar.ShownHealth || max != bar.ShownMax)
            {
                bar.ShownHealth = health;
                bar.ShownMax = max;
                text.text = EliteBarLayout.Numbers(health, max);
            }
            if (!Mathf.Approximately(alpha, bar.ShownAlpha))
            {
                bar.ShownAlpha = alpha;
                text.alpha = alpha;
            }
        }

        /// <summary>
        /// Середина полоски над макушкой тела в этом кадре. Высота сглажена по сущности: к выросшей
        /// макушке — быстро, вниз — медленно (EliteBarLayout.Follow). Сущность в прошлом кадре не
        /// считали (только появилась, полоска вернулась) — высота встаёт сразу, без подъезда.
        /// </summary>
        private Vector3 BarCenter(int entity, EnemyKind kind, bool elite, Vector3 up, float dt, int frame)
        {
            Vector3 at = _driver.GetRenderPosition(entity);
            // Полоска повёрнута к камере: её высота и зазор лежат вдоль «вверх» камеры, а середина
            // ставится по вертикали над сущностью. Метр вдоль «вверх» камеры — 1 / up.y метров по
            // вертикали (камера боя 48° — полтора).
            float rise = 1f / Mathf.Max(.05f, up.y);
            // Нижний край полосы элиты — низ полосы или основания рогов, что ниже: рога не ложатся на голову.
            float half = elite ? EliteBelowCenter : Height * .5f;
            // Высота из таблицы — середина обычной полоски; у элиты низ остаётся там же.
            float fallback = BarHeight(kind) + (half - Height * .5f) * rise;
            bool measured = MeasureTop(entity, kind, at, up, out float top);
            float target = EliteBarLayout.Target(top, measured, fallback, HeadGap * rise, half * rise);

            float height;
            if (_anchorFrame[entity] == frame) height = _anchor[entity];
            else if (_anchorFrame[entity] == frame - 1) height = EliteBarLayout.Follow(_anchor[entity], target, dt, AnchorRise, AnchorFall);
            else height = target;
            _anchor[entity] = height;
            _anchorFrame[entity] = frame;
            return new Vector3(at.x, at.y + height, at.z);
        }

        /// <summary>
        /// Макушка тела: самая высокая на экране точка тела, пересчитанная в метры по вертикали над
        /// точкой сущности. «На экране», а не в мире: камера смотрит сверху под углом, и рога,
        /// отклонённые назад, на экране выше, чем их мировая высота над центром.
        ///
        /// Два замера, берётся меньший. Кости кожи: самая высокая на экране кость плюс то, что торчит
        /// над ней у этого вида (BoneReach) — точки, а не коробки, поэтому глубина тела в них не
        /// попадает. Границы видимых рендереров: для AABB максимум dot(угол, up) = dot(центр, up) +
        /// dot(половины, |up|) — глубина коробки × tg 48° завышает его на 30–60 пикселей (у вендиго до
        /// 100), зато он честный сверху и работает у тел без кожи (заглушка, спрайт) — поэтому он потолок.
        /// Кости меряются, только пока видна хоть одна их кожа. Без выделений памяти в кадре.
        /// false — тела нет или все его части скрыты.
        /// </summary>
        private bool MeasureTop(int entity, EnemyKind kind, Vector3 ground, Vector3 up, out float top)
        {
            top = 0f;
            if (_arena == null || up.y < .05f || !_arena.TryGetEntityView(entity, out Transform view) || view == null) return false;
            BodyShape body = _bodyShape[entity];
            if (_bodyView[entity] != view || body == null)
            {
                body = ShapeOf(view);
                _bodyView[entity] = view;
                _bodyShape[entity] = body;
                // Новое тело — прежняя сглаженная высота ему не принадлежит.
                _anchorFrame[entity] = NeverFrame;
            }

            float groundUp = Vector3.Dot(ground, up);
            Renderer[] renderers = body.Renderers;
            Vector3 absUp = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            float box = float.NegativeInfinity;
            int skins = 0;
            for (int k = 0; k < renderers.Length; k++)
            {
                Renderer renderer = renderers[k];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds b = renderer.bounds;
                float reach = Vector3.Dot(b.center, up) + Vector3.Dot(b.extents, absUp);
                if (reach > box) box = reach;
                skins |= body.SkinBits[k];
            }
            if (float.IsNegativeInfinity(box)) return false;
            top = (box - groundUp) / up.y;

            if (skins == 0 || !BoneReach(kind, out float crown, out float limb, out string[] crownBones)) return true;
            if (body.CrownKind != (int)kind) MarkCrown(body, kind, crownBones);
            // Короны у тела не нашлось (кости переименовали при перевыгрузке) — всем костям запас короны:
            // полоска выше, но не в рогах; сверху её держит коробка.
            if (!body.HasCrown) limb = crown;
            // Запасы — в метрах модели; тело растянуто масштабом корня (страж 2,4, детёныш 0,6) и
            // сжатием удара. Вертикальный метр — up.y вдоль «вверх» камеры.
            float scale = Mathf.Abs(view.lossyScale.y) * up.y;
            float crownUp = crown * scale, limbUp = limb * scale;
            Transform[] bones = body.Bones;
            float best = float.NegativeInfinity;
            for (int j = 0; j < bones.Length; j++)
            {
                if ((body.BoneSkins[j] & skins) == 0) continue;
                Transform bone = bones[j];
                if (bone == null) continue;
                float reach = Vector3.Dot(bone.position, up) + (body.Crown[j] ? crownUp : limbUp);
                if (reach > best) best = reach;
            }
            if (!float.IsNegativeInfinity(best)) top = Mathf.Min(top, (best - groundUp) / up.y);
            return true;
        }

        /// <summary>
        /// Разбор тела для замера макушки: части для потолка и кости кожи. Тело пула приходит к новой
        /// сущности уже разобранным; уничтоженные тела (смена арены) выметаются, когда словарь разросся.
        /// </summary>
        private BodyShape ShapeOf(Transform view)
        {
            if (_shapes.TryGetValue(view, out BodyShape shape)) return shape;
            if (_shapes.Count >= MaxShapes) _shapes.Clear();
            shape = BuildShape(view);
            _shapes.Add(view, shape);
            return shape;
        }

        /// <summary>
        /// Тело, по которому меряется макушка.
        /// Части — сетки, кожа и спрайты тела. Без контактной тени, подписи заглушки, частиц, следов и
        /// линий — они либо на земле, либо улетают от тела. Кости — уникальные кости всех кож тела;
        /// маска у кости — какие кожи она двигает (бит кожи в SkinBits, первые 31 кожа).
        /// </summary>
        private sealed class BodyShape
        {
            public Renderer[] Renderers;
            public int[] SkinBits;
            public Transform[] Bones;
            public string[] BoneNames;
            public int[] BoneSkins;
            // Кости «короны» вида CrownKind (BoneReach): над ними торчит выше всего. Одно тело на два
            // вида только у Расщепеня и его детёныша, у них корона одна — разметка почти не меняется.
            public bool[] Crown;
            public int CrownKind = -1;
            public bool HasCrown;
        }

        private static BodyShape BuildShape(Transform view)
        {
            Renderer[] all = view.GetComponentsInChildren<Renderer>(true);
            var parts = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                bool part = renderer is SkinnedMeshRenderer || renderer is MeshRenderer || renderer is SpriteRenderer;
                if (!part) continue;
                if (renderer.gameObject.name == "Contact Shadow") continue;
                if (ForestMobPlaceholderView.IsLabel(renderer)) continue;
                if (renderer is MeshRenderer && renderer.GetComponent<TMP_Text>() != null) continue;
                parts.Add(renderer);
            }

            var shape = new BodyShape { Renderers = parts.ToArray() };
            shape.SkinBits = new int[shape.Renderers.Length];
            var bones = new List<Transform>();
            var masks = new List<int>();
            int skin = 0;
            for (int r = 0; r < shape.Renderers.Length && skin < 31; r++)
            {
                if (!(shape.Renderers[r] is SkinnedMeshRenderer skinned)) continue;
                int bit = 1 << skin++;
                shape.SkinBits[r] = bit;
                Transform[] own = skinned.bones;
                for (int b = 0; b < own.Length; b++)
                {
                    Transform bone = own[b];
                    if (bone == null) continue;
                    int at = bones.IndexOf(bone);
                    if (at < 0) { bones.Add(bone); masks.Add(bit); }
                    else masks[at] |= bit;
                }
            }
            shape.Bones = bones.ToArray();
            shape.BoneSkins = masks.ToArray();
            shape.BoneNames = new string[shape.Bones.Length];
            for (int b = 0; b < shape.Bones.Length; b++) shape.BoneNames[b] = shape.Bones[b].name;
            shape.Crown = new bool[shape.Bones.Length];
            return shape;
        }

        private static void MarkCrown(BodyShape shape, EnemyKind kind, string[] crownBones)
        {
            shape.CrownKind = (int)kind;
            shape.HasCrown = false;
            for (int b = 0; b < shape.Bones.Length; b++)
            {
                bool crown = System.Array.IndexOf(crownBones, shape.BoneNames[b]) >= 0;
                shape.Crown[b] = crown;
                shape.HasCrown |= crown;
            }
        }

        // Кости «короны» по видам — имена из FBX в Resources/Characters.
        private static readonly string[] MixamoCrown = { "mixamorig:Head" };
        private static readonly string[] WendigoCrown = { "head" };
        private static readonly string[] StonehoofCrown = { "neck0", "head0", "body_top0", "body_top1" };
        private static readonly string[] ThorncasterCrown = { "Head" };
        private static readonly string[] RootSnarerCrown = { "head", "neck", "spine_02", "L_clavicle", "R_clavicle" };
        private static readonly string[] SplitterCrown = { "body", "spine", "neck", "head", "shell_L", "shell_R" };
        private static readonly string[] NoCrown = { };

        /// <summary>
        /// Сколько тела торчит над костями кожи, в метрах модели (при масштабе корня 1; в кадре
        /// умножается на масштаб тела): над костями «короны» — рога с черепом, горб, панцирь, плечи;
        /// над остальными — кисти, когти, лепестки, ноги. false — вид не размечен, меряется коробкой.
        ///
        /// Замерено 29.09 по FBX из Resources/Characters в Blender: все клипы, кроме смерти, по 12
        /// разворотов моба под камерой боя (48°), макушка — по вершинам сетки. Запасы подобраны так,
        /// чтобы в покое и ходьбе ошибка в среднем была нулевой. Разброс от разворота моба и поз
        /// атаки (5–95 %, пиксели при 1080p): вендиго −14…+22 (рога широкие и откинуты назад),
        /// Шипомёт −13…+18, Расщепень −10…+17, Корнехват −11…+12, страж −7…+10, корнеполз −8…+7,
        /// камнекопыт −5…+4, бутон ±2 (в выстреле, пока лепестки переворачиваются, до −24).
        /// </summary>
        private static bool BoneReach(EnemyKind kind, out float crown, out float limb, out string[] crownBones)
        {
            switch (kind)
            {
                // Страж (и вид «None» на его теле): череп над костью головы; тело в игре ×2,4.
                case EnemyKind.None:
                case EnemyKind.ForestGuardian: crown = .24f; limb = .085f; crownBones = MixamoCrown; return true;
                case EnemyKind.ForestRootSwarm: crown = .215f; limb = .025f; crownBones = MixamoCrown; return true;
                // Бутон: над концами лепестков — только их кончики.
                case EnemyKind.ForestBud: crown = .08f; limb = .08f; crownBones = NoCrown; return true;
                // Вендиго: кость головы на 2,2 м, рога — до 3,1 м.
                case EnemyKind.ForestWendigo: crown = 1.24f; limb = .58f; crownBones = WendigoCrown; return true;
                case EnemyKind.ForestStonehoof: crown = .46f; limb = .16f; crownBones = StonehoofCrown; return true;
                case EnemyKind.ForestThorncaster: crown = .68f; limb = .44f; crownBones = ThorncasterCrown; return true;
                // Корнехват сгорблен: выше всего спина и плечи, голова ниже ключиц.
                case EnemyKind.ForestRootSnarer: crown = .5f; limb = .42f; crownBones = RootSnarerCrown; return true;
                // Расщепень: панцирь на 0,66 м выше хребта; детёныш — то же тело ×0,6.
                case EnemyKind.ForestSplitter:
                case EnemyKind.ForestSplitling: crown = .7f; limb = .05f; crownBones = SplitterCrown; return true;
                default: crown = 0f; limb = 0f; crownBones = NoCrown; return false;
            }
        }

        /// <summary>Сдвигает порядок всех частей полоски в блок элиты и обратно.</summary>
        private static void SetEliteOrder(ref Bar bar, bool elite)
        {
            if (bar.EliteOrdered == elite) return;
            bar.EliteOrdered = elite;
            int shift = elite ? EliteOrder : -EliteOrder;
            Shift(bar.BackRenderer, shift);
            Shift(bar.TrailRenderer, shift);
            Shift(bar.FillRenderer, shift);
            Shift(bar.FrameRenderer, shift);
            Shift(bar.GemFill, shift);
            Shift(bar.GemRim, shift);
            Shift(bar.AntlerLeft, shift);
            Shift(bar.AntlerRight, shift);
            if (bar.Numbers != null) bar.Numbers.GetComponent<MeshRenderer>().sortingOrder += shift;
        }

        private static void Shift(Renderer renderer, int shift)
        {
            if (renderer != null) renderer.sortingOrder += shift;
        }

        /// <summary>На какой высоте над телом висит полоска этого вида, когда замера нет, м.</summary>
        private float BarHeight(EnemyKind kind) => kind switch
        {
            EnemyKind.ForestBud => BudHeight3D,
            EnemyKind.ForestRootSwarm => RootSwarmHeight3D,
            EnemyKind.ForestWendigo => WendigoHeight3D,
            EnemyKind.ForestStonehoof => StonehoofHeight3D,
            EnemyKind.ForestThorncaster => ThorncasterHeight3D,
            EnemyKind.ForestRootSnarer => RootSnarerHeight3D,
            EnemyKind.ForestSplitter => SplitterHeight3D,
            EnemyKind.ForestSplitling => SplitlingHeight3D,
            _ => Height3D,
        };

        private void HideFrom(int from)
        {
            if (_bars == null) return;
            for (int i = from; i < _bars.Length; i++)
            {
                Transform root = _bars[i].Root;
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            }
        }

        private static Color Faded(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private SpriteRenderer Part(Transform parent, string name, Sprite sprite, int order, bool sliced)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : _quad;
            renderer.sortingOrder = order;
            if (_overlay != null) renderer.sharedMaterial = _overlay;
            if (sliced && sprite != null) renderer.drawMode = SpriteDrawMode.Sliced;
            return renderer;
        }

        private Bar MakeBar(Transform root, int index)
        {
            var rootGo = new GameObject("Полоска " + index);
            rootGo.transform.SetParent(root, false);
            rootGo.SetActive(false);
            Bar bar = _ink ? InkBar(rootGo.transform) : KitBar(rootGo.transform);
            AddAntlers(ref bar);
            return bar;
        }

        /// <summary>
        /// Материал «поверх всего» для частей полоски. Раньше они рисовались Sprites/Default с обычным
        /// тестом глубины и тонули в высоких моделях и позах замаха. UI/Default берёт тест глубины из
        /// unity_GUIZTestMode — на самом материале стоит Always. Шейдер всегда в сборке (Always
        /// Included Shaders), смешивание и цвет вершин — как у Sprites/Default, так что вид прежний.
        /// Нет шейдера — null: части остаются на материале по умолчанию.
        /// </summary>
        private static Material OverlayMaterial()
        {
            Shader shader = Shader.Find("UI/Default");
            if (shader == null) return null;
            var material = new Material(shader) { name = "Полоски здоровья · поверх мира" };
            material.SetFloat(ZTestId, (float)CompareFunction.Always);
            // UGUI ставит его глобально для текстур-масок шрифта; спрайтам полосы — ноль, иначе побелеют.
            material.SetVector(TextureSampleAddId, Vector4.zero);
            return material;
        }

        /// <summary>
        /// Рог элиты из Resources (импорт любой: спрайт или обычная текстура). Якорь спрайта — точка
        /// крепления рога (AntlerPivot): вокруг неё рог ставится к концу полосы и отражается. Нет
        /// спрайта — полоса элиты без рогов, но с цифрами.
        /// </summary>
        private void LoadAntler()
        {
            var texture = Resources.Load<Texture2D>(AntlerPath);
            if (texture == null) return;
            _antler = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), AntlerPivot, 100f, 0, SpriteMeshType.FullRect);
            _antler.name = texture.name;
        }

        /// <summary>
        /// Рога элиты на полоске: левый и правый, тем же материалом поверх мира, что и вся полоска.
        /// Место и размер выставляет DrawAntlers каждый кадр.
        /// </summary>
        private void AddAntlers(ref Bar bar)
        {
            if (_antler == null) return;
            bar.AntlerLeft = Part(bar.Root, "Рог слева", _antler, OrderAntler, false);
            bar.AntlerRight = Part(bar.Root, "Рог справа", _antler, OrderAntler, false);
            bar.AntlerLeft.gameObject.SetActive(false);
            bar.AntlerRight.gameObject.SetActive(false);
        }

        /// <summary>
        /// Цифры полосы элиты: Nunito из темы UI (роль Body), жирные, светлые, с чернильной обводкой и
        /// мягкой тенью. Рисуются поверх мира, как и сама полоса. Нет шрифта — null, полоса без цифр.
        /// </summary>
        private TextMeshPro MakeNumbers(Transform root, int order)
        {
            TMP_FontAsset body = UiTheme.Current.Body;
            if (body == null) return null;
            // Жирное начертание берётся сразу шрифтом (Nunito-Bold SDF из таблицы весов Body), а не
            // стилем Bold: со стилем TMP рисовал бы цифры запасным материалом того шрифта, и наш
            // материал (обводка, ZTest Always) до них мог не доехать. Нет жирного — синтетический Bold.
            TMP_FontAsset bold = BoldOf(body);
            TMP_FontAsset font = bold != null ? bold : body;
            if (_numbersMaterial == null) _numbersMaterial = NumbersMaterial(font);

            var go = new GameObject("Цифры");
            go.transform.SetParent(root, false);
            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = _numbersMaterial;
            text.fontStyle = bold != null ? FontStyles.Normal : FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.fontSizeMax = EliteNumbersSize * 10f;
            text.fontSizeMin = EliteNumbersSize * 6f;
            text.fontSize = EliteNumbersSize * 10f;
            text.color = NumbersColor;
            text.text = string.Empty;
            text.GetComponent<MeshRenderer>().sortingOrder = order;
            go.SetActive(false);
            return text;
        }

        private Material NumbersMaterial(TMP_FontAsset font)
        {
            var m = new Material(font.material) { name = font.name + " · полоса элиты" };
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, NumbersOutline);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, .24f);
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .55f));
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, .3f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .6f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.5f);
            // Шейдеры TMP тоже берут тест глубины из unity_GUIZTestMode: цифры не тонут вместе с полосой.
            m.SetFloat(ZTestId, (float)CompareFunction.Always);
            ShaderUtilities.GetShaderPropertyIDs();
            ShaderUtilities.UpdateShaderRatios(m);
            return m;
        }

        /// <summary>Жирный (вес 700) из таблицы весов шрифта; null — его нет.</summary>
        private static TMP_FontAsset BoldOf(TMP_FontAsset font)
        {
            TMP_FontWeightPair[] weights = font.fontWeightTable;
            const int bold = 7;
            if (weights == null || weights.Length <= bold) return null;
            TMP_FontAsset typeface = weights[bold].regularTypeface;
            return typeface != null && typeface != font ? typeface : null;
        }

        /// <summary>
        /// Мазки «Дыма и света» из Resources. Импорт текстур любой (спрайт или обычная текстура):
        /// спрайты собираются здесь, один раз. Нет хоть одной — false, рисуется прежний вид.
        /// </summary>
        private bool LoadInk()
        {
            var track = Resources.Load<Texture2D>(TrackPath);
            var fill = Resources.Load<Texture2D>(FillPath);
            var orb = Resources.Load<Texture2D>(OrbPath);
            var glow = Resources.Load<Texture2D>(GlowPath);
            if (track == null || fill == null || orb == null || glow == null) return false;

            _track = Whole(track);
            _orb = Whole(orb);
            _glow = Whole(glow);
            // Ступени заливки — один и тот же мазок, обрезанный справа; якорь — левый край.
            _fillSteps = new Sprite[FillSteps];
            for (int i = 0; i < FillSteps; i++)
            {
                float width = Mathf.Max(1f, Mathf.Round(fill.width * (i + 1) / (float)FillSteps));
                _fillSteps[i] = Sprite.Create(fill, new Rect(0f, 0f, width, fill.height), new Vector2(0f, .5f), 100f, 0, SpriteMeshType.FullRect);
                _fillSteps[i].name = "Мазок " + (i + 1);
            }
            // След урона — кусок середины того же мазка: растягивается по длине, якорь — левый край.
            float sliceFrom = Mathf.Round(fill.width * TrailSliceFrom);
            float sliceWidth = Mathf.Clamp(Mathf.Round(fill.width * TrailSliceWidth), 1f, fill.width - sliceFrom);
            _trailSlice = Sprite.Create(fill, new Rect(sliceFrom, 0f, sliceWidth, fill.height), new Vector2(0f, .5f), 100f, 0, SpriteMeshType.FullRect);
            _trailSlice.name = "След урона";
            return true;
        }

        private static Sprite Whole(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }

        /// <summary>Масштаб, при котором весь спрайт ложится в <paramref name="width"/>×<paramref name="height"/> метров.</summary>
        private static void Fit(SpriteRenderer renderer, float width, float height)
        {
            Vector2 size = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / Mathf.Max(.001f, size.x), height / Mathf.Max(.001f, size.y), 1f);
        }

        /// <summary>
        /// Полоска «Дыма и света»: плотная часть дымной дорожки — ровно Width×Height (ореол
        /// выходит за неё), мазок заливки — во всю внутреннюю длину и полную высоту (его плотная
        /// часть — как прежняя капсула), огонёк элиты — сияние и ядро.
        /// </summary>
        private Bar InkBar(Transform root)
        {
            float inner = Width - Inset * 2f;
            SpriteRenderer back = Part(root, "Дорожка", _track, OrderTrack, false);
            Fit(back, Width * TrackSpanX, Height * TrackSpanY);

            // След урона: место и длину ставит DrawTrail каждый кадр.
            SpriteRenderer trail = Part(root, "След урона", _trailSlice, OrderTrail, false);
            trail.gameObject.SetActive(false);

            // Масштаб — по полному мазку: короткая ступень той же высоты и плотности, просто обрезана.
            SpriteRenderer fill = Part(root, "Заливка", _fillSteps[FillSteps - 1], OrderFill, false);
            Fit(fill, inner, Height);
            fill.transform.localPosition = new Vector3(-inner * .5f, 0f, -.001f);

            // Огонёк вписан в 1 м: размер (EliteGem) и обратное сжатие неровного корня элиты
            // даёт масштаб узла, выставляемый каждый кадр.
            var gem = new GameObject("Элита").transform;
            gem.SetParent(root, false);
            SpriteRenderer glow = Part(gem, "Сияние", _glow, OrderGlow, false);
            Fit(glow, GlowSpan, GlowSpan);
            SpriteRenderer orb = Part(gem, "Огонёк", _orb, OrderOrb, false);
            Fit(orb, OrbSpan, OrbSpan);
            gem.gameObject.SetActive(false);

            return new Bar
            {
                Root = root,
                Fill = fill.transform,
                BackRenderer = back,
                FillRenderer = fill,
                TrailRenderer = trail,
                Gem = gem,
                GemFill = orb,
                GemRim = glow,
            };
        }

        /// <summary>
        /// Прежний вид пака «Ночная акварель» (спрайтов «Дыма и света» нет): капсула-дорожка,
        /// заливка капсулой, серебряный контур; у элиты — светлый ромб в оправе цвета здоровья.
        /// </summary>
        private Bar KitBar(Transform root)
        {
            UiTheme theme = UiTheme.Current;

            SpriteRenderer back = Part(root, "Дорожка", theme.BarFill, OrderTrack, true);
            if (back.drawMode == SpriteDrawMode.Sliced) back.size = new Vector2(Width, Height);
            else back.transform.localScale = new Vector3(Width, Height, 1f);

            SpriteRenderer trail = Part(root, "След урона", theme.BarFill, OrderTrail, true);
            trail.gameObject.SetActive(false);
            SpriteRenderer fill = Part(root, "Заливка", theme.BarFill, OrderFill, true);
            SpriteRenderer frame = Part(root, "Контур", theme.BarFrame, OrderFrame, true);
            if (frame.drawMode == SpriteDrawMode.Sliced) frame.size = new Vector2(Width, Height);
            else frame.gameObject.SetActive(false);

            // Ромб элиты: светлая заливка в оправе цвета здоровья, едет за концом заливки.
            // Вписан в 1 м, как огонёк «Дыма и света»: размер даёт масштаб узла.
            var gem = new GameObject("Элита").transform;
            gem.SetParent(root, false);
            SpriteRenderer gemFill = Part(gem, "Заливка", theme.DiamondFill, OrderGlow, false);
            SpriteRenderer gemRim = Part(gem, "Оправа", theme.DiamondFrameSmall, OrderOrb, false);
            foreach (SpriteRenderer part in new[] { gemFill, gemRim })
            {
                Vector2 size = part.sprite.bounds.size;
                part.transform.localScale = new Vector3(1f / Mathf.Max(.001f, size.x), 1f / Mathf.Max(.001f, size.y), 1f);
            }
            gemFill.transform.localScale *= .82f;
            gem.gameObject.SetActive(false);

            return new Bar
            {
                Root = root,
                Fill = fill.transform,
                BackRenderer = back,
                FillRenderer = fill,
                TrailRenderer = trail,
                FrameRenderer = frame,
                Gem = gem,
                GemFill = gemFill,
                GemRim = gemRim,
            };
        }

        /// <summary>Белый квадрат с якорем на ЛЕВОМ крае: заливка должна расти вправо.</summary>
        private static Sprite MakeQuadSprite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4);
        }
    }
}
