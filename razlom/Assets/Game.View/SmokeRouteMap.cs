using System;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using L = Game.View.SmokeRouteMapLayout;

namespace Game.View
{
    /// <summary>
    /// «Карта тушью» на закрытой дымной завесе (выбор владельца 30.09 из концептов перехода: a5 — кисть
    /// дорисовывает дорогу от «Арены 2», впереди развилка пустых кругов; a6 — узел «Арена 3» загорается,
    /// «Впереди — …»). Владелец: «надо красиво анимировать», «примерно так, только доработать для нас с
    /// максимальным удобством».
    ///
    /// Пока завеса закрывает мир и арена собирается по кадрам, на дыму проявляется путь забега:
    ///  1. пройденная дорога — от лагеря, заполненные круги тушью с мазками между ними — проявляется
    ///     лесенкой слева направо, сверху — «Путь по Разлому»;
    ///  2. впереди бледная развилка пустых кругов (три пути выбора; при входе в забег — один);
    ///  3. кисть рисует мазок к выбранному узлу: мокрая голова, подтёк вокруг свежей краски, капля у кисти;
    ///  4. узел загорается спокойным тёплым светом, невыбранные пути гаснут, дальше бледно уходит дорога;
    ///  5. подпись: «Арена N» и одно главное «впереди» (Хранитель, элита, опасная арена, новый враг…);
    ///  6. на рассеивании завесы карта растворяется в дыму.
    /// Вход из лагеря через арку и «Повторить» — тот же показ, путь начинается узлом лагеря к «Арене 1».
    ///
    /// Раскладка, кривая мазка, расписание и тексты — в <see cref="SmokeRouteMapLayout"/> (тесты вне Unity).
    /// Время — своё (UiMotion.Now, шаг не больше 0,1 с): кадры сборки арены под завесой длятся 30–60 мс, карта
    /// не прыгает. Выбранный путь карта узнаёт сама из забега (TickDriver): сим переходит на новую арену через
    /// тик-другой после закрытия завесы, до этого кисть ждёт. Выделений памяти по кадрам нет: части —
    /// готовые из префаба (SmokeTransitionBuilder, миграции v1–v2), строки ставятся раз на переход.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SmokeRouteMap : MonoBehaviour
    {
        /// <summary>Какой путь рисовать.</summary>
        public enum Route : byte
        {
            None = 0,
            /// <summary>Из лагеря через арку: лагерь → «Арена 1».</summary>
            FromCamp = 1,
            /// <summary>«Повторить» на итогах: новый забег, тот же показ.</summary>
            FromRepeat = 2,
            /// <summary>Выбор следующей арены: пройденная дорога, развилка, мазок к выбранной.</summary>
            Advance = 3,
        }

        /// <summary>Пройденный узел: круг тушью, знак и подпись под ним.</summary>
        [Serializable]
        public struct Stop
        {
            public RectTransform Root;
            public Graphic Disc;
            public RawImage Sign;
            public TMP_Text Label;
        }

        [Tooltip("Всё содержимое карты: середина — середина экрана, по нему идёт медленный дрейф")]
        public RectTransform Content;
        [Tooltip("Прозрачность карты целиком: растворение на рассеивании завесы")]
        public CanvasGroup Group;

        [Header("Заголовок")]
        public TMP_Text Title;
        public Graphic TitleThread;

        [Header("Дороги")]
        [Tooltip("Мазки пройденной дороги, по одному на узел (в узел — из предыдущего)")]
        public SmokeRouteStroke[] PastRoads = new SmokeRouteStroke[0];
        [Tooltip("Бледные пути развилки к пустым кругам")]
        public SmokeRouteStroke[] ForkRoads = new SmokeRouteStroke[0];
        [Tooltip("Мазок, который кисть рисует к выбранной арене")]
        public SmokeRouteStroke Road;
        [Tooltip("Подтёк: широкий бледный след мокрой краски под свежим мазком")]
        public SmokeRouteStroke Halo;
        [Tooltip("Дорога дальше загоревшегося узла, бледная — путь не кончается")]
        public SmokeRouteStroke Onward;
        [Tooltip("Капля мокрой краски у кисти")]
        public Graphic Bleed;

        [Header("Узлы")]
        public Stop[] Stops = new Stop[0];
        [Tooltip("Пустые бледные круги развилки")]
        public Graphic[] ForkRings = new Graphic[0];

        [Header("Загоревшийся узел")]
        public RectTransform Lit;
        public Graphic LitShade, LitGlow, LitDisc, LitRing;
        public RawImage LitSign;
        public UiEmbers LitSparks;

        [Header("Подпись")]
        public RectTransform Caption;
        public Graphic CaptionShade;
        public TMP_Text CaptionTitle, CaptionAhead;
        [Tooltip("Подпись ниже середины загоревшегося узла, единиц")] public float CaptionDrop = L.CaptionDrop;

        [Header("Знаки")]
        [Tooltip("По SmokeRouteSign: клинки, лагерь, заново, улучшение (сундук), магазин (мешок), опасная, Хранитель")]
        public Texture[] Signs = new Texture[7];

        [Header("Движение")]
        [Tooltip("Медленный дрейф карты влево за показ, единиц: карта живая, а не картинка")] public float Drift = 40f;
        [Tooltip("Кисть идёт с запаздыванием подтёка, единиц длины")] public float HaloLag = 26f;
        [Tooltip("Дольше не ждём выбранный путь, с: дальше завеса раскрывается без мазка")] public float WaitMax = 2.5f;
        [Tooltip("Доля рассеивания завесы, за которую карта растворяется")] [Range(.1f, 1f)] public float OpenFade = .3f;
        [Tooltip("Искр от загоревшегося узла: спокойно, не салют")] public int SparkBurst = 4;

        Route _route;
        bool _active, _ready, _gaveUp, _burst, _remembered;
        // Новая арена уже пришла, подпись и мазки ставятся кадром позже (см. Collect).
        bool _arrived;
        float _clock, _paint, _last = -1f, _open, _waited;
        int _from, _next, _count, _chosen, _first, _stopCount, _pastCount, _totalLevels, _oldNumber;
        float _halfWidth = 960f;
        SmokeRouteSign _start = SmokeRouteSign.Camp;
        TickDriver _driver;
        bool _lookedForDriver;
        RiftRun _run, _oldRun;
        readonly ArenaRouteOffer[] _offers = new ArenaRouteOffer[3];
        Vector2 _litAt, _fromAt;

        // Кэш проявлений: GetComponent раз на жизнь префаба.
        UiInkReveal[] _stopDisc = new UiInkReveal[0], _stopSign = new UiInkReveal[0], _ringInk = new UiInkReveal[0];
        UiInkText[] _stopLabel = new UiInkText[0];
        int[] _stopDepth = new int[0];
        UiInkReveal _thread, _bleed, _litShade, _litGlow, _litDisc, _litRing, _litSign, _captionShade;
        UiInkText _title, _captionTitle, _captionAhead;

        // Какие пути игрок выбирал в этом забеге — знаки пройденных узлов. Номер забега, а не сам забег:
        // ссылка держала бы в памяти старую симуляцию.
        static readonly SmokeRouteSign[] History = new SmokeRouteSign[64];
        static int _historyRun = -1;
        static SmokeRouteSign _historyStart = SmokeRouteSign.Camp;

        /// <summary>Карта показывается (с начала показа до конца рассеивания).</summary>
        public bool Active => _active;

        /// <summary>
        /// Карту дочитали — завесу можно раскрывать. Без карты, после отказа (забега нет, путь не пришёл)
        /// и на рассеивании — сразу.
        /// </summary>
        public bool Done => !_active || _gaveUp || _open > 0f || (_ready && _paint >= L.ReadUntil);

        void Awake()
        {
            Remember();
            Hide();
        }

        void Remember()
        {
            if (_remembered) return;
            _remembered = true;
            _stopDisc = new UiInkReveal[Stops.Length];
            _stopSign = new UiInkReveal[Stops.Length];
            _stopLabel = new UiInkText[Stops.Length];
            _stopDepth = new int[Stops.Length];
            for (int i = 0; i < Stops.Length; i++)
            {
                _stopDisc[i] = Ink(Stops[i].Disc);
                _stopSign[i] = Ink(Stops[i].Sign);
                _stopLabel[i] = Letters(Stops[i].Label);
            }
            _ringInk = new UiInkReveal[ForkRings.Length];
            for (int i = 0; i < ForkRings.Length; i++) _ringInk[i] = Ink(ForkRings[i]);
            _thread = Ink(TitleThread);
            _bleed = Ink(Bleed);
            _litShade = Ink(LitShade);
            _litGlow = Ink(LitGlow);
            _litDisc = Ink(LitDisc);
            _litRing = Ink(LitRing);
            _litSign = Ink(LitSign);
            _captionShade = Ink(CaptionShade);
            _title = Letters(Title);
            _captionTitle = Letters(CaptionTitle);
            _captionAhead = Letters(CaptionAhead);
        }

        static UiInkReveal Ink(Component part) => part != null ? part.GetComponent<UiInkReveal>() : null;
        static UiInkText Letters(Component part) => part != null ? part.GetComponent<UiInkText>() : null;

        // ---------------------------------------------------------------- показ

        /// <summary>
        /// Начать показ (завеса почти закрыла экран). Для <see cref="Route.Advance"/> забег должен стоять на
        /// выборе пути: снимается пройденная глубина и три предложения; выбранное карта узнает, когда сим
        /// перейдёт на новую арену. Нет данных — карты нет, завеса ведёт себя как раньше.
        /// </summary>
        public void Begin(Route route)
        {
            Remember();
            Hide();
            if (route == Route.None) return;
            if (!_lookedForDriver || _driver == null)
            {
                // Один поиск на показ, не на кадр.
                _lookedForDriver = true;
                _driver = FindAnyObjectByType<TickDriver>();
            }
            RiftRun run = _driver != null ? _driver.Run : null;
            _route = route;
            _chosen = -1;
            if (route == Route.Advance)
            {
                if (run == null || run.Phase != RunPhase.ChoosingRoute) return;
                _run = run;
                _from = run.Depth;
                _next = _from + 1;
                _count = _offers.Length;
                for (int i = 0; i < _offers.Length; i++) _offers[i] = run.GetRoute(i);
                _start = SameRun(run) ? _historyStart : SmokeRouteSign.Camp;
                _totalLevels = run.TotalLevels;
            }
            else
            {
                _run = null;
                _oldRun = run;
                _oldNumber = _driver != null && _driver.Session != null ? _driver.Session.RunNumber : -1;
                _from = 0;
                _next = 1;
                _count = 1;
                _start = route == Route.FromRepeat ? SmokeRouteSign.Repeat : SmokeRouteSign.Camp;
                _totalLevels = 0;
            }
            _active = true;
            if (Group != null) Group.alpha = 1f;
            Place();
            Pose();
        }

        /// <summary>Рассеивание завесы, 0..1: карта растворяется в дыму за первую <see cref="OpenFade"/>.</summary>
        public void SetOpen(float k)
        {
            if (!_active) return;
            _open = Mathf.Clamp01(k);
            if (_open >= OpenFade) { Hide(); return; }
            Pose();
        }

        /// <summary>Всё скрыто: мазков нет, узлы и подписи не видны, карта ничего не ждёт.</summary>
        public void Hide()
        {
            Remember();
            _active = _ready = _gaveUp = _burst = _arrived = false;
            _route = Route.None;
            _clock = _paint = _open = _waited = 0f;
            _last = -1f;
            _run = _oldRun = null;
            if (Group != null) Group.alpha = 0f;
            foreach (SmokeRouteStroke road in PastRoads) Clear(road);
            foreach (SmokeRouteStroke road in ForkRoads) Clear(road);
            Clear(Road);
            Clear(Halo);
            Clear(Onward);
            for (int i = 0; i < Stops.Length; i++)
            {
                Reveal(_stopDisc[i], 1f);
                Reveal(_stopSign[i], 1f);
                Letters(_stopLabel[i], 1f);
                Show(Stops[i].Root, false);
            }
            for (int i = 0; i < _ringInk.Length; i++) Reveal(_ringInk[i], 1f);
            Reveal(_thread, 1f);
            Reveal(_bleed, 1f);
            Reveal(_litShade, 1f);
            Reveal(_litGlow, 1f);
            Reveal(_litDisc, 1f);
            Reveal(_litRing, 1f);
            Reveal(_litSign, 1f);
            Reveal(_captionShade, 1f);
            Letters(_title, 1f);
            Letters(_captionTitle, 1f);
            Letters(_captionAhead, 1f);
            if (LitSparks != null) LitSparks.Rate = 0f;
        }

        void LateUpdate()
        {
            if (!_active) return;
            using (FrameCost.Measure("Карта тушью"))
            {
                float now = UiMotion.Now;
                // Шаг не больше 0,1 с: кадр смены арены длится десятки миллисекунд, карта не прыгает.
                float dt = _last < 0f ? 0f : Mathf.Clamp(now - _last, 0f, .1f);
                _last = now;
                if (!_ready && !_gaveUp) Collect();
                _clock += dt;
                if (_ready && _clock >= L.PaintAt) _paint += dt;
                else if (!_ready && _clock >= L.PaintAt)
                {
                    _waited += dt;
                    if (_waited > WaitMax) _gaveUp = true;
                }
                Pose();
            }
        }

        // ---------------------------------------------------------------- данные забега

        /// <summary>
        /// Пришла ли новая арена. Кадр прихода — самый тяжёлый кадр перехода (смена симуляции, привязка тел,
        /// начало сборки; по съёмке 30.09 — 53–77 мс): подпись и мазки (≈2 мс) карта ставит кадром позже,
        /// уже под бюджетом сборки, а не поверх него.
        /// </summary>
        void Collect()
        {
            RiftRun run = _driver != null ? _driver.Run : null;
            if (_route == Route.Advance)
            {
                // Забег сменился или кончился (смерть на выходе, «В лагерь») — мазку некуда идти.
                if (run == null || run != _run) { _gaveUp = true; return; }
                if (run.Depth != _next) return;
                if (!_arrived) { _arrived = true; return; }
                _chosen = Match(run.CurrentRoute);
                Ready(run);
                return;
            }
            GameSession session = _driver != null ? _driver.Session : null;
            // Новый забег — новый номер (GameSession.BeginRift); сам объект забега сравнивается на всякий случай.
            if (run == null || session == null || session.Mode != GameMode.Rift) return;
            if (run == _oldRun && session.RunNumber == _oldNumber) return;
            if (!_arrived) { _arrived = true; return; }
            _run = run;
            _chosen = 0;
            Ready(run);
        }

        int Match(in ArenaRouteOffer route)
        {
            for (int i = 0; i < _offers.Length; i++)
            {
                ArenaRouteOffer o = _offers[i];
                if (o.Reward == route.Reward && o.Hard == route.Hard && o.Size == route.Size && o.BonusGold == route.BonusGold) return i;
            }
            // Не нашли (предложения поменялись) — середина развилки.
            return _count > 1 ? 1 : 0;
        }

        void Ready(RiftRun run)
        {
            _ready = true;
            bool boss = run.LevelSettings.Boss;
            ArenaRouteOffer route = run.CurrentRoute;
            bool arenaFlow = run.ArenaFlow;
            var ahead = new SmokeRouteAhead
            {
                Known = true,
                Boss = boss,
                BossName = EnemyTexts.BossName(EnemyKind.ForestGuardian),
                Hard = arenaFlow && route.Hard,
                BonusGold = route.BonusGold,
                Shop = arenaFlow && route.Reward == ArenaReward.Shop,
            };
            ArenaEncounterTemplate template = run.CurrentEncounter;
            if (template != null)
            {
                EnemyKind elite = EliteOf(template);
                ahead.Elite = template.Type == ArenaEncounterType.Elite || elite != EnemyKind.None;
                ahead.EliteName = elite != EnemyKind.None ? EnemyTexts.Name(elite) : null;
                ahead.Lesson = template.Lesson != EnemyKind.None ? EnemyTexts.Name(template.Lesson) : null;
                ahead.Ambush = template.Type == ArenaEncounterType.Ambush;
                ahead.Survival = template.Type == ArenaEncounterType.Survival;
            }
            // Награду выбирают только на развилке: первая арена забега — просто клинки (или Хранитель).
            SmokeRouteSign sign = _route == Route.Advance ? L.Sign(boss, ahead.Hard, ahead.Shop)
                : boss ? SmokeRouteSign.Boss : SmokeRouteSign.Arena;
            if (CaptionTitle != null) CaptionTitle.text = L.Title(run.Depth);
            if (CaptionAhead != null) CaptionAhead.text = L.Ahead(ahead);
            if (LitSign != null)
            {
                LitSign.texture = SignTexture(sign);
                LitSign.enabled = LitSign.texture != null;
            }
            _totalLevels = run.TotalLevels;
            Remember(run, sign);

            // Кисть идёт от текущего узла к выбранному пути развилки.
            L.Branch(_next, _chosen, _count, out float x, out float y);
            _litAt = new Vector2(x, y);
            if (Lit != null) Lit.anchoredPosition = _litAt;
            if (Caption != null) Caption.anchoredPosition = new Vector2(x, y - CaptionDrop);
            SetPath(Road, _fromAt, _litAt);
            SetPath(Halo, _fromAt, _litAt);
            if (Road != null) { Road.Head = 0f; Road.Wet = 1f; Road.Glow = 0f; }
            if (Halo != null) Halo.Head = 0f;
            // Дальше узла — бледная дорога, если путь не последний.
            if (Onward != null)
            {
                if (_totalLevels > 0 && run.Depth >= _totalLevels) Clear(Onward);
                else
                {
                    SetPath(Onward, _litAt, new Vector2(x + L.Spacing * 1.85f, L.WaveY(_next + 1) - (y - L.WaveY(_next)) * .35f));
                    Onward.Head = 1f;
                    Onward.Hidden = 1f;
                }
            }
        }

        /// <summary>Вид элиты встречи: группа с пометкой элиты в любой волне; None — элиты нет.</summary>
        static EnemyKind EliteOf(ArenaEncounterTemplate template)
        {
            for (int w = 0; w < template.WaveCount; w++)
            {
                EncounterWave wave = template.GetWave(w);
                for (int g = 0; g < wave.GroupCount; g++)
                {
                    WaveGroup group = wave.GetGroup(g);
                    if (group.Elite && group.Max > 0) return group.Kind;
                }
            }
            return EnemyKind.None;
        }

        bool SameRun(RiftRun run)
        {
            GameSession session = _driver != null ? _driver.Session : null;
            return session != null && session.Run == run && session.RunNumber == _historyRun;
        }

        /// <summary>Запомнить знак новой арены (и начало пути при входе) — для пройденных узлов следующих карт.</summary>
        void Remember(RiftRun run, SmokeRouteSign sign)
        {
            GameSession session = _driver != null ? _driver.Session : null;
            int number = session != null ? session.RunNumber : -1;
            if (number != _historyRun)
            {
                _historyRun = number;
                for (int i = 0; i < History.Length; i++) History[i] = SmokeRouteSign.Arena;
                _historyStart = _route == Route.Advance ? SmokeRouteSign.Camp : _start;
            }
            if (run.Depth >= 0 && run.Depth < History.Length) History[run.Depth] = sign;
        }

        Texture SignTexture(SmokeRouteSign sign)
        {
            int i = (int)sign;
            Texture texture = Signs != null && i < Signs.Length ? Signs[i] : null;
            if (texture == null && Signs != null && Signs.Length > 0) texture = Signs[0];
            return texture;
        }

        // ---------------------------------------------------------------- раскладка

        void Place()
        {
            if (Content != null && Content.rect.width > 1f) _halfWidth = Content.rect.width * .5f;
            _first = L.FirstShown(_from, _next, _halfWidth);
            _stopCount = Mathf.Min(_from - _first + 1, Stops.Length);
            _first = _from - _stopCount + 1;
            _fromAt = new Vector2(L.X(_from, _next), L.WaveY(_from));
            bool sameRun = _route == Route.Advance && SameRun(_run);
            for (int i = 0; i < Stops.Length; i++)
            {
                bool shown = i < _stopCount;
                Show(Stops[i].Root, shown);
                if (!shown) continue;
                int depth = _first + i;
                _stopDepth[i] = depth;
                float x = L.X(depth, _next), y = L.WaveY(depth);
                if (Stops[i].Root != null) Stops[i].Root.anchoredPosition = new Vector2(x, y);
                if (Stops[i].Label != null) Stops[i].Label.text = L.StopLabel(depth, _start);
                SmokeRouteSign sign = depth == 0 ? _start : sameRun && depth < History.Length ? History[depth] : SmokeRouteSign.Arena;
                if (Stops[i].Sign != null)
                {
                    Stops[i].Sign.texture = SignTexture(sign);
                    Stops[i].Sign.enabled = Stops[i].Sign.texture != null;
                }
                float edge = L.EdgeAlpha(x, _halfWidth);
                Alpha(Stops[i].Disc, edge);
                Alpha(Stops[i].Sign, edge);
                Alpha(Stops[i].Label, edge);
            }
            // Мазки в каждый видимый узел из предыдущего (кроме лагеря): первый может начинаться за краем.
            _pastCount = 0;
            for (int i = 0; i < PastRoads.Length; i++)
            {
                SmokeRouteStroke road = PastRoads[i];
                int depth = _first + i;
                if (road == null) continue;
                if (i >= _stopCount || depth < 1) { Clear(road); continue; }
                SetPath(road, new Vector2(L.X(depth - 1, _next), L.WaveY(depth - 1)), new Vector2(L.X(depth, _next), L.WaveY(depth)));
                road.Head = 1f;
                road.Hidden = 1f;
                _pastCount = i + 1;
            }
            // Развилка: бледные пути к пустым кругам; при входе — одна дорога к «Арене 1».
            for (int i = 0; i < ForkRings.Length; i++)
            {
                bool shown = i < _count;
                Show(ForkRings[i], shown);
                SmokeRouteStroke road = i < ForkRoads.Length ? ForkRoads[i] : null;
                if (!shown) { Clear(road); continue; }
                L.Branch(_next, i, _count, out float x, out float y);
                ForkRings[i].rectTransform.anchoredPosition = new Vector2(x, y);
                if (road != null)
                {
                    // До края пустого круга: внутри круга дороги нет, как на концепте.
                    SetPath(road, _fromAt, new Vector2(x - L.RingEdge, y));
                    road.Head = 1f;
                    road.Hidden = 1f;
                }
            }
        }

        // ---------------------------------------------------------------- поза по времени

        void Pose()
        {
            float t = _clock, p = _paint;
            // Растворение на рассеивании: шумом шейдера и прозрачностью карты — сразу с началом, пока основа
            // завесы ещё закрывает мир (карта поверх открывшейся арены читалась мусором, первый кадр 30.09).
            float gone = _open > 0f ? L.Smooth(_open / Mathf.Max(OpenFade, .01f)) : 0f;
            if (Group != null) Group.alpha = 1f - gone;

            // Заголовок — у верхнего края, а верхняя полоса дыма встаёт последней: буквы идут, когда край уже закрыт.
            float title = L.Phase(t, .18f, .42f);
            Letters(_title, Max(1f - title, gone));
            Reveal(_thread, Max(1f - L.Phase(t, .26f, .4f), gone));

            for (int i = 0; i < _stopCount; i++)
            {
                float at = i * L.InkStagger;
                Reveal(_stopDisc[i], Max(1f - L.Phase(t, at, L.InkIn), gone));
                Reveal(_stopSign[i], Max(1f - L.Phase(t, at + .08f, L.InkIn), gone));
                Letters(_stopLabel[i], Max(1f - L.Phase(t, at + .1f, L.InkIn), gone));
            }
            for (int i = 0; i < _pastCount; i++)
            {
                SmokeRouteStroke road = PastRoads[i];
                if (road != null) road.Hidden = Max(1f - L.Phase(t, (i - .6f) * L.InkStagger, L.InkIn + .08f), gone);
            }

            bool painting = _ready && _chosen >= 0;
            float light = painting ? L.Phase(p, L.Arrival - L.LightLead, L.LightTime) : 0f;
            float fork = L.Phase(t, L.ForkAt, L.ForkTime);
            for (int i = 0; i < _count && i < ForkRings.Length; i++)
            {
                // Выбранный пустой круг уступает загоревшемуся узлу, остальные пути тихо гаснут.
                float rest = i == _chosen ? 1f - light : Mathf.Lerp(1f, L.ForkRest, light);
                Reveal(_ringInk[i], Max(1f - fork, gone));
                Alpha(ForkRings[i], rest);
                SmokeRouteStroke road = i < ForkRoads.Length ? ForkRoads[i] : null;
                if (road == null) continue;
                road.Hidden = Max(1f - L.Phase(t, L.ForkAt - .04f, L.ForkTime + .08f), gone);
                Alpha(road, i == _chosen ? 1f : rest);
            }

            // Кисть.
            float brush = painting ? L.Brush(p / L.PaintTime) : 0f;
            float dry = painting ? L.Phase(p, L.Arrival, .55f) : 0f;
            if (Road != null)
            {
                Road.Head = brush;
                Road.Wet = 1f - dry;
                Road.Glow = light;
                Road.Hidden = gone;
            }
            if (Halo != null)
            {
                float lag = Halo.Length > 1f ? HaloLag / Halo.Length : 0f;
                Halo.Head = painting ? Mathf.Clamp01(brush - lag * (1f - dry)) : 0f;
                Halo.Hidden = gone;
                // Подтёк свежей краски впитывается: к концу почти не виден.
                Alpha(Halo, 1f - .7f * dry);
            }
            if (Bleed != null)
            {
                bool wet = painting && brush > 0f;
                if (wet && Road != null) Bleed.rectTransform.anchoredPosition = Road.HeadPoint;
                // Капля растекается и впитывается, когда кисть пришла.
                float drop = wet ? (1f - dry) : 0f;
                Reveal(_bleed, wet ? gone : 1f);
                Alpha(Bleed, drop * L.Phase(p, 0f, .08f));
                float swell = 1f + .35f * dry;
                Bleed.rectTransform.localScale = new Vector3(swell, swell, 1f);
            }

            // Узел загорается: тёмный круг тушью изнутри, спокойное кольцо света, знак, мягкое свечение.
            if (Lit != null)
            {
                float pop = .9f + .1f * L.Smooth(light);
                Lit.localScale = new Vector3(pop, pop, 1f);
            }
            Reveal(_litShade, Max(1f - light, gone));
            Reveal(_litDisc, Max(1f - light, gone));
            Reveal(_litRing, Max(1f - (painting ? L.Phase(p, L.Arrival, .42f) : 0f), gone));
            Reveal(_litSign, Max(1f - (painting ? L.Phase(p, L.Arrival + .06f, .3f) : 0f), gone));
            // Свечение дышит: чуть ярче в миг прихода кисти и оседает (без вспышки).
            float settle = painting ? L.Phase(p, L.Arrival + .1f, .5f) : 0f;
            Reveal(_litGlow, Max(1f - light, gone));
            Alpha(LitGlow, .78f + .22f * light * (1f - settle));
            if (!_burst && light > .35f)
            {
                _burst = true;
                if (LitSparks != null && SparkBurst > 0) LitSparks.Burst(SparkBurst);
            }
            if (Onward != null && painting) Onward.Hidden = Max(1f - L.Phase(p, L.Arrival + .08f, .45f), gone);

            // Подпись.
            Reveal(_captionShade, Max(1f - (painting ? L.Phase(p, L.CaptionAt - .08f, .3f) : 0f), gone));
            Letters(_captionTitle, Max(1f - (painting ? L.Phase(p, L.CaptionAt, L.CaptionTime) : 0f), gone));
            Letters(_captionAhead, Max(1f - (painting ? L.Phase(p, L.SubAt, L.CaptionTime) : 0f), gone));

            if (Content != null)
            {
                float drift = L.Clamp01(t / (L.DoneAt + .5f));
                Content.anchoredPosition = new Vector2(Drift * (.5f - drift), 0f);
            }
        }

        // ---------------------------------------------------------------- кадр для владельца

        /// <summary>
        /// Поза без забега — кадр сборщика (SmokeTransitionBuilder.Capture) и проверка вида: пройдено до
        /// <paramref name="from"/>, развилка из <paramref name="count"/>, выбран <paramref name="chosen"/>,
        /// <paramref name="time"/> — секунды показа.
        /// </summary>
        public void Preview(Route route, int from, int count, int chosen, SmokeRouteSign sign, string ahead, float time)
        {
            Remember();
            Hide();
            _route = route;
            _active = true;
            _from = route == Route.Advance ? Mathf.Max(0, from) : 0;
            _next = _from + 1;
            _count = route == Route.Advance ? Mathf.Clamp(count, 1, ForkRings.Length) : 1;
            _chosen = Mathf.Clamp(chosen, 0, _count - 1);
            _start = route == Route.FromRepeat ? SmokeRouteSign.Repeat : SmokeRouteSign.Camp;
            if (Group != null) Group.alpha = 1f;
            Place();
            _ready = true;
            L.Branch(_next, _chosen, _count, out float x, out float y);
            _litAt = new Vector2(x, y);
            if (Lit != null) Lit.anchoredPosition = _litAt;
            if (Caption != null) Caption.anchoredPosition = new Vector2(x, y - CaptionDrop);
            if (CaptionTitle != null) CaptionTitle.text = L.Title(_next);
            if (CaptionAhead != null) CaptionAhead.text = ahead ?? string.Empty;
            if (LitSign != null) { LitSign.texture = SignTexture(sign); LitSign.enabled = LitSign.texture != null; }
            SetPath(Road, _fromAt, _litAt);
            SetPath(Halo, _fromAt, _litAt);
            SetPath(Onward, _litAt, new Vector2(x + L.Spacing * 1.85f, L.WaveY(_next + 1) - (y - L.WaveY(_next)) * .35f));
            if (Onward != null) Onward.Head = 1f;
            _clock = Mathf.Max(0f, time);
            _paint = Mathf.Max(0f, time - L.PaintAt);
            _burst = true;
            Pose();
        }

        // ---------------------------------------------------------------- мелочи

        static float Max(float a, float b) => a > b ? a : b;

        static void Reveal(UiInkReveal part, float hidden)
        {
            if (part != null) part.Hidden = hidden;
        }

        static void Letters(UiInkText part, float hidden)
        {
            if (part != null) part.Hidden = hidden;
        }

        static void Alpha(Component part, float alpha)
        {
            if (part == null) return;
            var graphic = part as Graphic;
            if (graphic == null) graphic = part.GetComponent<Graphic>();
            if (graphic == null) return;
            alpha = Mathf.Clamp01(alpha);
            CanvasRenderer renderer = graphic.canvasRenderer;
            if (Mathf.Abs(renderer.GetAlpha() - alpha) > .002f) renderer.SetAlpha(alpha);
        }

        static void Show(Component part, bool shown)
        {
            if (part != null && part.gameObject.activeSelf != shown) part.gameObject.SetActive(shown);
        }

        static void Clear(SmokeRouteStroke road)
        {
            if (road == null) return;
            road.ClearPath();
            road.Hidden = 1f;
        }

        static void SetPath(SmokeRouteStroke road, Vector2 from, Vector2 to)
        {
            if (road != null) road.SetPath(from, to);
        }
    }
}
