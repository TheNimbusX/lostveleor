using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ТЕЛО ПО ФАЗАМ SIM (контракт клипов artifacts/tools/wf/boss-clip-spec.md, 02.10).
    ///
    /// Каждое состояние контроллера — Motion Time от параметра «&lt;Клип&gt;Phase»
    /// (ThicketMasterBuilder), как у Расщепеня и Корнехвата. Какой клип и какой кадр —
    /// решает чистое правило <see cref="ThicketMasterClipRules"/> по полям действия Sim:
    /// контакт клипа ложится ровно на тик удара (EnemyActionImpact) при любых замахах
    /// и стойках фаз. Часы — тики Sim с долей кадра (Tick − 1 + Alpha): пауза, хит-стоп
    /// и съёмка держат позу сами; под Песочными Часами (босс стоит) аниматор стоит.
    ///
    /// • Сон — Sleep (цикл) до пробуждения; Wake → Roar — вступление.
    /// • Ход — Walk фазой по пройденному пути показанного тела / шаг клипа (_walkStride, сборщик пишет замер шага
    ///   стоящих лап клипа; до замера — ThicketMasterClipRules.DefaultWalkStride). Владелец 08.10 «ноги немного
    ///   проскальзывают»: прямо лапы и так стояли (шаг = клип, 2,148 м/цикл), скользили на ходу с поворотом — Sim
    ///   крутит тело вокруг центра, передние лапы в 2,5 м впереди. Теперь (ThicketWalkRules, LateUpdate) шаг каждой
    ///   лапы идёт туда, куда под ней едет земля, фаза — по самой «быстрой» лапе, стоящая лапа держит точку касания
    ///   двухкостной ИК поверх клипа; F8 «Визуал · Хозяин Чащи» — A/B (<see cref="FootLock"/>).
    /// • Разворот (Sim — 2,5°/тик, ревью 02.10 вечер: «прокручивается на месте») — правило
    ///   ThicketMasterClipRules.Locomotion: стоит или ползёт медленнее 0,45 м/с, а корпус крутится, —
    ///   шаги TurnL/TurnR, доля клипа = поворот / 90° (при 2,5°/тик клип идёт ×0,83); идёт и
    ///   доворачивает — Walk, но лапы переступают не реже поворота (WalkCyclesTurning). Поворот
    ///   обрывает хвост прошлого клипа смесью: тело не вращается на стоящих лапах. Без клипов
    ///   разворота — переступание фазой Walk.
    /// • Серия лапы крутит корпус быстрее хода (3,5°/тик, первый удар до ~60°, контракт § 11), а клип удара стоит на
    ///   месте — поверх него добавочные слои шагов «Paw Turn Legs R/L» (ThicketMasterBuilder): задние лапы
    ///   и опорная передняя переступают TurnL/TurnR по накопленному повороту (<see cref="ThicketPawTurnLegs"/>,
    ///   проверка находок 03.10). Контроллер без слоёв (собран раньше) — как было. Те же шаги — в замахе веера
    ///   шипов-семян (клип ливня, корпус доворачивает к середине веера 3,5°/тик; ThicketMasterClipRules.TurnStepsUnder).
    /// • Нырок: DiveIn за уход, потом тело спрятано (<see cref="IsBurrowed"/>), бугор
    ///   рисует вид боя по <see cref="MoundPosition"/>; выход — Emerge с кадра 0 на тике удара.
    /// • Смерть — Death по профилю EnemyPresentationProfile (касание боком на FallSeconds),
    ///   последний кадр держится; после касания лежащее тело уходит под холм смерти (URP Lit
    ///   без растворения шейдером — как у Корнехвата). Убит под землёй — тело поднимается
    ///   из глубины, уже заваливаясь, а не встаёт над бугром целиком.
    /// • Песочные Часы: пока босс стоит, аниматор стоит; прошедший удар в стойке догоняет
    ///   <see cref="ThicketHourglassTrack"/> — после Часов кадр не прыгает.
    /// • Темп 02.10: серия лапы П/Л/П — П→Л без смеси (PawL кадр 0 = PawR кадр 25), Л→П —
    ///   смесь 4 тика; клип, что идёт сам за собой (серия фазы 3 кончается и начинается правой,
    ///   топот за топотом), переходит во вторую копию состояния («PawRAlt», свой параметр
    ///   Motion Time) со смесью, а не прыгает на кадр 0. Хвост: Sim сняла действие, а клип
    ///   ещё доигрывает (отход лапы, подъём после кольца топота, оседание пыльцы, толчки
    ///   ливня) — та же функция правила по запомненному действию до TailEndTick, пока босс
    ///   стоит; пошёл — ход, начал новое — смесь в него. Смеси — в тиках Sim (правило).
    /// • Рост 4,14 м (×1,15, решение 02.10): узел тела масштабирует сборщик; тень, погружение
    ///   смерти и глубина бугра — под этот рост.
    ///
    /// Корень принадлежит ArenaView (место, поворот по взгляду Sim, масштаб 1); вид
    /// двигает только узел модели под ним (погружение смерти) и рендеры (нырок).
    /// Нет клипа в контроллере — состояние подменяется Idle, исключений нет.
    /// Персонажа не высветляет: ни эмиссии, ни подъёма цвета.
    /// </summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class ThicketMasterAnimatorView : MonoBehaviour
    {
        /// <summary>Метров на цикл Walk в масштабе 1. Пишет ThicketMasterBuilder из отчёта клипов.</summary>
        [SerializeField] private float _walkStride = ThicketMasterClipRules.DefaultWalkStride;

        /// <summary>Поперечник контактной тени по умолчанию, м: тело 4,8 × 6,6 м (рост 4,14), тень ArenaView — 0,88 м на всех.</summary>
        public const float DefaultContactShadowMetres = 3.9f;

        /// <summary>Поперечник контактной тени, м. Сборщик пишет <see cref="DefaultContactShadowMetres"/>.</summary>
        [SerializeField] private float _contactShadowMetres = DefaultContactShadowMetres;

        /// <summary>
        /// Лежащее тело уходит под холм смерти, м (лёжа на боку босс ниже 2,3 м, крона выше; было 2,75 —
        /// с запасом на крону вне холма).
        /// </summary>
        public const float DeathSinkMetres = 3.3f;

        /// <summary>
        /// Убит под землёй (нырок): тело не выскакивает над бугром целиком, а поднимается из
        /// глубины роста (4,14 м) за первые BurrowDeathRiseSeconds смерти, уже заваливаясь.
        /// </summary>
        private const float BurrowDeathDepth = 4.14f, BurrowDeathRiseSeconds = .6f;

        /// <summary>Сколько держать разворот после последнего поворота: кадры без сдвига (30 Гц Sim) не мигают Idle.</summary>
        private const float TurnHoldSeconds = .2f;
        private const float TurnShuffleDegreesPerCycle = 160f;
        private const float TicksToSeconds = 1f / Simulation.TicksPerSecond;
        private const float LocomotionBlend = ThicketMasterClipRules.LocomotionBlendTicks * TicksToSeconds;
        private const float TurnBlendSeconds = ThicketMasterClipRules.TurnBlendTicks * TicksToSeconds;
        private const float DeathBlend = ThicketMasterClipRules.DeathBlendTicks * TicksToSeconds;

        /// <summary>Имя тени из ArenaView.CreateContactShadow (там оно закрытое).</summary>
        private const string ContactShadowName = "Contact Shadow";
        private const int None = int.MinValue;

        /// <summary>Состояния контроллера: клип × 2 копии (вторая — только у ThicketMasterClipRules.HasAlternate).</summary>
        private const int Copies = 2;
        private static readonly int[] StateHashes = new int[ThicketMasterClipRules.All.Length * Copies];
        private static readonly int[] PhaseHashes = new int[ThicketMasterClipRules.All.Length * Copies];

        /// <summary>Слои шагов под ударом лапы: [сторона × 2 + клип] — сторона 0 — бьёт правая, клип 0 — TurnL, 1 — TurnR.</summary>
        private static readonly int[] LegsStateHashes = new int[4];
        private static readonly int[] LegsPhaseHashes = new int[4];

        private Animator _animator;
        private Transform _body;
        /// <summary>Контактная тень ArenaView (дочка корня, в землю с телом не уходит); null — ещё не найдена.</summary>
        private Transform _contactShadow;
        /// <summary>Доля поперечника тени, что сейчас стоит (1 — вся; смерть сжимает её под холм).</summary>
        private float _shadowLeft = 1f;
        private Vector3 _bodyPosition;
        private TickDriver _driver;
        private Simulation _sim;
        private ArenaView _arena;
        private int _entity = -1, _generation = -1, _deathTick = None, _shownSerial;

        /// <summary>Текущее состояние контроллера: клип × 2 + копия; −1 — ещё не выбрано.</summary>
        private int _state = -1;
        private readonly bool[] _has = new bool[ThicketMasterClipRules.All.Length * Copies];
        private readonly int[] _copy = new int[ThicketMasterClipRules.All.Length];
        private float _walkPhase, _turnTravel, _turnSign, _turnUntil = -1f, _sink;
        private Vector3 _lastBody;
        private bool _isHidden, _shadowScaled, _deathSeen, _deathFromBurrow;
        /// <summary>В прошлом кадре тело играло разворот: гистерезис «почти на месте» (ClipRules.Locomotion).</summary>
        private bool _turnShown;
        /// <summary>Списки SetHidden переиспользуются: нырок раз в 5–10 с не мусорит в бою.</summary>
        private readonly System.Collections.Generic.List<Renderer> _hidden = new System.Collections.Generic.List<Renderer>(32),
            _scan = new System.Collections.Generic.List<Renderer>(32);
        private ThicketClipPose _pose;
        private float _tick;
        private ThicketHourglassTrack _hourglass;

        /// <summary>Последнее показанное действие (с поправкой Часов) — по нему играет хвост.</summary>
        private ThicketMasterState _last;
        private bool _tail;
        private int _tailFreeze;

        /// <summary>Шаги разворота под ударом лапы: слои контроллера (−1 — их нет), правило, показанный клип слоя (−1 — пусто).</summary>
        private readonly int[] _legsLayer = { -1, -1 };
        private ThicketPawTurnLegs _legs;
        private int _legsState = -1;
        private float _legsShown;

        /// <summary>Показан веер семян (действие или его хвост): клип ливня с шагами разворота (ThicketMasterClipRules.TurnStepsUnder).</summary>
        private bool _seeds;

        /// <summary>
        /// F8 «Визуал · Хозяин Чащи»: лапы держат землю на ходу (шаг под поворот, замок стоп, ИК — ThicketWalkRules).
        /// Выключено — ход как до 08.10: Walk по пути с полом поворота, без ИК. До выхода из игры.
        /// </summary>
        public static bool FootLock = true;

        /// <summary>Лапы на ходу (ThicketWalkRules): фаза Walk кадра, замки стоп, цели ИК.</summary>
        private readonly ThicketWalkFeet _feet = new ThicketWalkFeet();

        /// <summary>Кости ног по лапам ThicketWalkRules: плечо/бедро, локоть/колено, конец (запястье/скакательный сустав), касание.</summary>
        private readonly Transform[] _legUpper = new Transform[ThicketWalkRules.PawCount], _legLower = new Transform[ThicketWalkRules.PawCount],
            _legEnd = new Transform[ThicketWalkRules.PawCount], _legContact = new Transform[ThicketWalkRules.PawCount];

        /// <summary>Локальные повороты, что ИК оставила костям ноги в прошлом кадре: аниматор их не переписал — второй раз не ставить.</summary>
        private readonly Quaternion[] _legSet = new Quaternion[ThicketWalkRules.PawCount * 3];
        private bool _legsFound, _legsSet;

        /// <summary>Фазу Walk ведёт шаг под поворот (а не заплатка разворота); лапы держат землю; корень и тело Sim уже видены.</summary>
        private bool _feetDriven, _feetActive, _rootSeen, _renderSeen;
        private Vector3 _lastRender, _lastRootPosition, _lastRootForward;

        /// <summary>Сущность, к которой привязано тело; −1 — не привязано.</summary>
        public int Entity => _entity;

        /// <summary>Клип и кадр этого кадра — для съёмки и вида боя.</summary>
        public ThicketClipPose Pose => _pose;

        /// <summary>Тело под землёй (между концом DiveIn и выходом): видно только бугор.</summary>
        public bool IsBurrowed => _isHidden && _deathTick == None;

        /// <summary>Где бугор: тело Sim едет под землёй к герою. Вне нырка — место тела.</summary>
        public Vector3 MoundPosition => _driver != null && _entity >= 0 ? _driver.GetRenderPosition(_entity) : transform.position;

        /// <summary>Узел модели под корнем.</summary>
        public Transform Body => _body != null ? _body : transform;

        /// <summary>Кости для вида боя (знаки, VFX позже): найдены один раз на тело пула; null — нет кости.</summary>
        public Transform Head { get; private set; }
        public Transform CrownLeft { get; private set; }
        public Transform CrownRight { get; private set; }
        public Transform Bush { get; private set; }
        public Transform PawToeLeft { get; private set; }
        public Transform PawToeRight { get; private set; }
        public Transform Chest { get; private set; }

        /// <summary>Шаг клипа Walk, м на цикл (масштаб 1). Сборщик ставит его через SerializedObject.</summary>
        public float WalkStride => _walkStride;

        private void Awake()
        {
            EnsureHashes();
            _animator = GetComponentInChildren<Animator>();
            _body = _animator != null && _animator.transform != transform ? _animator.transform : null;
            if (_body != null) _bodyPosition = _body.localPosition;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "head": Head = t; break;
                    case "crown_L": CrownLeft = t; break;
                    case "crown_R": CrownRight = t; break;
                    case "bush": Bush = t; break;
                    case "leg_front_L_toe": PawToeLeft = t; break;
                    case "leg_front_R_toe": PawToeRight = t; break;
                    case "chest": Chest = t; break;
                }
            }
            FindLegs();
        }

        /// <summary>Кости ног для ИК на ходу: передние — плечо, локоть, запястье, костяшка; задние — бедро, колено, скакательный сустав.</summary>
        private void FindLegs()
        {
            var bones = GetComponentsInChildren<Transform>(true);
            _legsFound = true;
            for (int paw = 0; paw < ThicketWalkRules.PawCount; paw++)
            {
                bool front = ThicketWalkRules.IsFront(paw);
                string side = paw == ThicketWalkRules.FrontLeft || paw == ThicketWalkRules.HindLeft ? "L" : "R";
                string prefix = (front ? "leg_front_" : "leg_hind_") + side + "_";
                _legUpper[paw] = FindBone(bones, prefix + "upper");
                _legLower[paw] = FindBone(bones, prefix + "lower");
                _legEnd[paw] = FindBone(bones, prefix + (front ? "paw" : "foot"));
                _legContact[paw] = front ? FindBone(bones, prefix + "toe") : _legEnd[paw];
                _legsFound &= _legUpper[paw] != null && _legLower[paw] != null && _legEnd[paw] != null && _legContact[paw] != null;
            }
            if (!_legsFound && _animator != null)
                Debug.LogWarning("[thicketmaster] Нет костей ног leg_front_*/leg_hind_* — лапы на ходу без замка (ИК выключена).");
        }

        private static Transform FindBone(Transform[] bones, string bone)
        {
            foreach (var t in bones)
                if (t.name == bone) return t;
            return null;
        }

        private static void EnsureHashes()
        {
            if (StateHashes[0] != 0) return;
            for (int i = 0; i < ThicketMasterClipRules.All.Length; i++)
            {
                ThicketClip clip = ThicketMasterClipRules.All[i];
                StateHashes[Slot((int)clip, 0)] = Animator.StringToHash("Base Layer." + ThicketMasterClipRules.Name(clip));
                PhaseHashes[Slot((int)clip, 0)] = Animator.StringToHash(ThicketMasterClipRules.PhaseParameter(clip));
                if (!ThicketMasterClipRules.HasAlternate(clip)) continue;
                StateHashes[Slot((int)clip, 1)] = Animator.StringToHash("Base Layer." + ThicketMasterClipRules.AlternateName(clip));
                PhaseHashes[Slot((int)clip, 1)] = Animator.StringToHash(ThicketMasterClipRules.AlternatePhaseParameter(clip));
            }
            for (int side = 0; side < 2; side++)
                for (int turn = 0; turn < 2; turn++)
                {
                    ThicketClip clip = turn == 0 ? ThicketClip.TurnL : ThicketClip.TurnR;
                    LegsStateHashes[side * 2 + turn] = Animator.StringToHash(
                        ThicketMasterClipRules.PawTurnLayer(side == 0) + "." + ThicketMasterClipRules.Name(clip));
                    LegsPhaseHashes[side * 2 + turn] = Animator.StringToHash(ThicketMasterClipRules.PawTurnParameter(side == 0, clip));
                }
        }

        private static int Slot(int clip, int copy) => clip * Copies + copy;

        /// <summary>Привязка тела из пула к сущности: ArenaView зовёт при выдаче, без кадра в чужой позе.</summary>
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver;
            _sim = driver != null ? driver.Sim : null;
            _arena = driver != null ? driver.GetComponent<ArenaView>() : null;
            _generation = driver != null ? driver.Generation : -1;
            _entity = entity;
            _deathTick = None;
            _deathSeen = _deathFromBurrow = false;
            _hourglass = default;
            _shownSerial = 0;
            _last = default;
            _tail = false;
            _tailFreeze = 0;
            System.Array.Clear(_copy, 0, _copy.Length);
            _walkPhase = _turnTravel = _turnSign = 0f;
            _turnUntil = -1f;
            _turnShown = false;
            _lastBody = Vector3.zero;
            _legs = default;
            _legsState = -1;
            _legsShown = 0f;
            _seeds = false;
            ForgetFeet();
            _renderSeen = false;
            ScaleContactShadow();
            SetShadowLeft(1f);
            SetHidden(false);
            SetSink(0f);
            _state = -1;
            if (_animator == null || _sim == null || (uint)entity >= (uint)_sim.Entities.Count) return;
            _animator.Rebind();
            for (int i = 0; i < _has.Length; i++) _has[i] = StateHashes[i] != 0 && _animator.HasState(0, StateHashes[i]);
            if (_animator.runtimeAnimatorController == null || !_has[Slot((int)ThicketClip.Idle, 0)])
                Debug.LogWarning("[thicketmaster] У тела нет контроллера с состоянием Idle — собери «Разлом → Босс → Хозяин Чащи → Собрать представление».");
            // Слои шагов под ударом лапы — оба, с обоими клипами разворота; иначе (контроллер старше 03.10) без них.
            for (int side = 0; side < 2; side++)
            {
                int layer = _animator.GetLayerIndex(ThicketMasterClipRules.PawTurnLayer(side == 0));
                _legsLayer[side] = layer > 0 && _animator.HasState(layer, LegsStateHashes[side * 2])
                    && _animator.HasState(layer, LegsStateHashes[side * 2 + 1]) ? layer : -1;
            }
            if (_legsLayer[0] < 0 || _legsLayer[1] < 0) _legsLayer[0] = _legsLayer[1] = -1;
            // Первый кадр — сразу в позе Sim (сон, действие), а не в Idle пула.
            Evaluate(0f, force: true);
            _animator.Update(0f);
        }

        /// <summary>Привязать вид тела, если он на нём есть (одна строка в ArenaView.BindNewEntities).</summary>
        public static void BindOn(GameObject body, TickDriver driver, int entity)
        {
            if (body != null && body.TryGetComponent(out ThicketMasterAnimatorView view)) view.Bind(driver, entity);
        }

        /// <summary>Смерть на тике события Death (ArenaView, вид боя). Без вызова вид сам видит «не жив».</summary>
        public void PlayDeath(int deathTick)
        {
            if (_deathTick != None) return;
            _deathTick = deathTick;
        }

        public void PlayDeath()
        {
            if (_sim != null) PlayDeath(_sim.Tick - 1);
        }

        /// <summary>Смерть тела, если на нём этот вид (одна строка в ArenaView, событие Death).</summary>
        public static void PlayDeathOn(Transform body, int deathTick)
        {
            if (body != null && body.TryGetComponent(out ThicketMasterAnimatorView view)) view.PlayDeath(deathTick);
        }

        private void Update()
        {
            if (_animator == null || !EnsureBound()) return;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            Evaluate(Time.deltaTime, force: false);
        }

        private void Evaluate(float dt, bool force)
        {
            var entities = _sim.Entities;
            float tick = _sim.Tick - 1 + _driver.Alpha;
            _tick = tick;

            if (_deathTick == None && !entities.Alive[_entity]) _deathTick = _sim.Tick - 1;
            if (_deathTick != None)
            {
                // Смерть под землёй: тело было спрятано — оно поднимается из глубины, а не появляется над бугром.
                if (!_deathSeen) { _deathSeen = true; _deathFromBurrow = _isHidden; }
                _animator.speed = 1f;
                float seconds = Mathf.Max(0f, tick - _deathTick) / Simulation.TicksPerSecond;
                var profile = EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster);
                var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
                float frame = ThicketMasterClipRules.DeathFrame(seconds, beat.HitStopSeconds, profile.FallSeconds, profile.RestSeconds);
                Sample(new ThicketClipPose(ThicketClip.Death, frame), _deathFromBurrow ? 0f : DeathBlend);
                UpdateDeathSink(seconds, profile, beat);
                SetHidden(false);
                UpdatePawTurnLegs(0f, dt);
                return;
            }

            // Песочные Часы: Sim держит босса и сдвигает все его сроки — поза стоит. Хвост
            // (действия в Sim уже нет) Sim не сдвигает — его сдвигает вид, раз на каждые Часы.
            bool frozen = _sim.ThicketMasterFrozenTicksLeft(_entity) > 0;
            if (frozen && !force)
            {
                _animator.speed = 0f;
                if (_tail && _sim.TryGetThicketMasterMemory(_entity, out ThicketMasterMemory memory)
                    && memory.FrozenUntil != _tailFreeze && !_sim.TryGetThicketMasterAction(_entity, out _))
                {
                    _tailFreeze = memory.FrozenUntil;
                    _last = ThicketMasterClipRules.Shift(_last, Simulation.ThicketHourglassShiftTicks);
                }
                return;
            }
            _animator.speed = 1f;

            Vector3 body = _arena != null ? _arena.BodyFacing(_entity) : _driver.GetRenderFacing(_entity);
            float yaw = body.sqrMagnitude > .5f && _lastBody.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastBody, body, Vector3.up) : 0f;
            if (body.sqrMagnitude > .5f) _lastBody = body;
            // Путь показанного тела за кадр (оси тела, масштаб корня): по нему идёт фаза Walk, а не по скорости Sim × dt.
            TrackRender(body);

            EvaluateAlive(tick, yaw, dt, force);
            // Серия лапы крутит корпус — шаги разворота поверх клипа удара (по позе, выбранной этим кадром).
            UpdatePawTurnLegs(yaw, dt);
        }

        /// <summary>Живое тело вне Часов: действие, сон, ход, разворот, хвост клипа или покой.</summary>
        private void EvaluateAlive(float tick, float yaw, float dt, bool force)
        {
            _seeds = false;
            if (_sim.TryGetThicketMasterAction(_entity, out ThicketMasterState a))
            {
                ForgetTurn();
                _seeds = a.Action == ThicketMasterAction.Seeds;
                // Часы в стойке: прошедший удар Sim не сдвигает — трекер догоняет его, кадр не прыгает.
                ThicketMasterState shown = _hourglass.Apply(a);
                ThicketClipPose pose = ThicketMasterClipRules.Action(shown, tick);
                // Новое действие — клип заново (тот же клип — во вторую копию); шаг того же — по правилу смеси:
                // П→Л без смеси, Л→П — 4 тика; серия из подъёма топота (связка «топот → лапа») — 7.
                bool same = a.Serial == _shownSerial;
                float blend = ThicketMasterClipRules.ActionBlend(_pose.Clip, pose.Clip, same) * TicksToSeconds;
                // Выход из-под земли — кадр 0 сразу, без смеси со спрятанной позой нырка.
                if (pose.Clip == ThicketClip.Emerge && _isHidden) blend = 0f;
                _shownSerial = a.Serial;
                _last = shown;
                _tail = true;
                SetHidden(pose.Burrowed);
                Sample(pose, pose.Burrowed || force ? 0f : blend, restart: !same);
                return;
            }
            SetHidden(false);

            if (!_sim.ThicketMasterAwake(_entity))
            {
                ForgetTurn();
                _tail = false;
                Sample(new ThicketClipPose(ThicketClip.Sleep,
                    ThicketMasterClipRules.LoopPhase(tick, ThicketMasterClipRules.SleepFrames) * ThicketMasterClipRules.SleepFrames),
                    force ? 0f : LocomotionBlend);
                return;
            }

            float speed = _sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            bool turning = TrackTurn(yaw, dt);
            ThicketMotion motion = ThicketMasterClipRules.Locomotion(speed, turning, _turnShown);
            // Хвост клипа (отход лапы, подъём после топота) переживает мелкий доворот к герою — корпус
            // доходит до взгляда за 2–3 тика; разворот обрывает его, когда поворот уже настоящий.
            bool tailing = _tail && tick >= _last.EndTick - 1 && tick < ThicketMasterClipRules.TailEndTick(_last);
            if (motion == ThicketMotion.Turn && tailing && !ThicketMasterClipRules.TurnBreaksTail(_turnTravel))
                motion = ThicketMotion.Idle;
            _turnShown = motion == ThicketMotion.Turn;
            if (motion == ThicketMotion.Walk)
            {
                // Пошёл — хвост прошлого клипа обрывается смесью в ход. Доворачивает на ходу (Sim пускает
                // шаг долей с 53° от героя) — лапы переступают не реже поворота, а не стоят под крутящимся телом.
                _tail = false;
                _turnTravel = 0f;
                if (FootLock && _legsFound)
                {
                    // Лапы на ходу (владелец 08.10, «ноги проскальзывают»): фаза — по ходу земли под самой «быстрой» лапой
                    // (прямо — ровно путь показанного тела / шаг, на повороте лапы переступают чаще), стопы под поворот и
                    // замок стоящих — в LateUpdate (ThicketWalkRules).
                    if (!_feetDriven) { _feet.Reset(); _feetDriven = true; }
                    _walkPhase += _feet.Advance(dt, _renderForward, _renderSide, yaw, _walkStride, BodyScale);
                }
                else
                    _walkPhase += ThicketMasterClipRules.WalkCyclesTurning(speed * dt, yaw, _walkStride, transform.lossyScale.x);
                Sample(ThicketClipPose.WithPhase(ThicketClip.Walk, Mathf.Repeat(_walkPhase, 1f)), LocomotionBlend);
                return;
            }
            if (motion == ThicketMotion.Turn)
            {
                // Крутится на месте или почти на месте (герой кружит) — шаги разворота по углу; хвост
                // прошлого клипа (отход лапы, подъём после топота) поворот обрывает смесью.
                _tail = false;
                SampleTurn(yaw);
                return;
            }
            // Хвост: действие снято, клип доигрывает (отход лапы, подъём после топота, оседание каста,
            // толчки ливня), пока босс стоит. Тот же номер действия — та же копия состояния.
            // Снятое раньше конца (смерть героя) хвоста не играет: замах не доигрывается.
            if (tailing)
            {
                _seeds = _last.Action == ThicketMasterAction.Seeds;
                Sample(ThicketMasterClipRules.Action(_last, tick), LocomotionBlend);
                return;
            }
            _tail = false;
            Sample(new ThicketClipPose(ThicketClip.Idle,
                ThicketMasterClipRules.LoopPhase(tick, ThicketMasterClipRules.IdleFrames) * ThicketMasterClipRules.IdleFrames),
                force ? 0f : LocomotionBlend);
        }

        /// <summary>
        /// Корпус крутится (показанное тело, Sim — 2,5°/тик = 75°/с): копит угол поворота в одну сторону,
        /// смена стороны — счёт заново; разворот держится TurnHoldSeconds после последнего поворота.
        /// False — не крутится (счёт сброшен).
        /// </summary>
        private bool TrackTurn(float yaw, float dt)
        {
            float now = Time.time;
            if (dt > 1e-5f && Mathf.Abs(yaw) / dt > ThicketMasterClipRules.TurnRateThreshold)
            {
                float sign = Mathf.Sign(yaw);
                if (sign != _turnSign) { _turnSign = sign; _turnTravel = 0f; }
                _turnTravel += Mathf.Abs(yaw);
                _turnUntil = now + TurnHoldSeconds;
            }
            if (_turnSign == 0f || now > _turnUntil) { ForgetTurn(); return false; }
            return true;
        }

        /// <summary>Разворот клипом по накопленному углу: ~90° на клип, лапы переступают дважды за клип.</summary>
        private void SampleTurn(float yaw)
        {
            ThicketClip clip = _turnSign < 0f ? ThicketClip.TurnL : ThicketClip.TurnR;
            if (_has[Slot((int)clip, 0)])
            {
                float phase = ThicketMasterClipRules.TurnPhase(_turnTravel);
                Sample(new ThicketClipPose(clip, phase * ThicketMasterClipRules.TurnFrames), TurnBlendSeconds);
                return;
            }
            // Заплатка без клипов разворота: лапы переступают фазой Walk в темпе поворота (замка стоп у неё нет).
            ForgetFeet();
            _walkPhase += Mathf.Abs(yaw) / TurnShuffleDegreesPerCycle;
            Sample(ThicketClipPose.WithPhase(ThicketClip.Walk, Mathf.Repeat(_walkPhase, 1f)), LocomotionBlend);
        }

        private void ForgetTurn() { _turnSign = 0f; _turnTravel = 0f; _turnUntil = -1f; _turnShown = false; }

        // ------------------------------------------------------------ лапы на ходу (08.10)

        /// <summary>Тело переставили, а не провели (пул, стенд): дальше этого за кадр — не шаг.</summary>
        private const float MaxRenderStepMetres = 1.5f;

        private float _renderForward, _renderSide;

        /// <summary>Масштаб тела под корнем (сборщик: 4,14 м / рост модели) — стоп-центры ThicketWalkRules даны в масштабе модели 1.</summary>
        private float BodyScale => _body != null ? Mathf.Abs(_body.localScale.x) : ThicketWalkRules.MeasuredBodyScale;

        /// <summary>Путь показанного тела за кадр (TickDriver, как его поставит ArenaView) в осях тела и единицах корня.</summary>
        private void TrackRender(Vector3 body)
        {
            Vector3 render = _driver.GetRenderPosition(_entity);
            Vector3 delta = _renderSeen ? render - _lastRender : Vector3.zero;
            _lastRender = render;
            _renderSeen = true;
            delta.y = 0f;
            if (delta.sqrMagnitude > MaxRenderStepMetres * MaxRenderStepMetres) delta = Vector3.zero;
            float unit = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            Vector3 forward = body.sqrMagnitude > .5f ? new Vector3(body.x, 0f, body.z).normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            _renderForward = Vector3.Dot(delta, forward) / unit;
            _renderSide = Vector3.Dot(delta, right) / unit;
        }

        /// <summary>Ход кончился (вес Walk погас, тело из пула, заплатка разворота): замков и поправок нет.</summary>
        private void ForgetFeet()
        {
            _feet.Reset();
            _feetDriven = _feetActive = _legsSet = _rootSeen = false;
        }

        /// <summary>
        /// Лапы держат землю на ходу (ThicketWalkRules, владелец 08.10): после аниматора (и после ArenaView — корень уже
        /// стоит на месте кадра) каждая лапа идёт шагом под поворот, стоящая держит точку касания и разворот стопы, пока
        /// корпус идёт и крутится, — двухкостной ИК поверх клипа. Вес — вес состояния Walk в аниматоре: смесь в ход и из
        /// хода поправку проявляет и гасит. Пауза, Часы, смерть, нырок — ничего не трогает.
        /// </summary>
        private void LateUpdate()
        {
            if (_animator == null || !_legsFound || _sim == null || _driver == null) return;
            // Пауза и Часы: фаза и корень стоят — те же цели ставятся заново на ту же позу (или позу не переписали).
            float weight = FootLock && _feetDriven && _deathTick == None && !_isHidden ? WalkWeight() : 0f;
            Vector3 position = transform.position, forward = transform.forward;
            forward.y = 0f;
            if (weight <= 1e-3f || forward.sqrMagnitude < 1e-6f)
            {
                // Ход погас — следующий начнётся с чистого листа.
                if (_feetActive) ForgetFeet();
                return;
            }
            forward.Normalize();
            float unit = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            if (_rootSeen)
            {
                // Корень сдвинулся и повернулся с прошлого кадра — стоящие лапы остаются на земле (в его прежних осях).
                Vector3 moved = position - _lastRootPosition;
                moved.y = 0f;
                if (moved.sqrMagnitude > MaxRenderStepMetres * MaxRenderStepMetres) _feet.Reset();
                else
                {
                    Vector3 right = Vector3.Cross(Vector3.up, _lastRootForward);
                    _feet.Follow(Vector3.Dot(moved, _lastRootForward) / unit, Vector3.Dot(moved, right) / unit,
                        Vector3.SignedAngle(_lastRootForward, forward, Vector3.up));
                }
            }
            _lastRootPosition = position;
            _lastRootForward = forward;
            _rootSeen = true;
            _feetActive = true;
            // Аниматор позу не переписал (кости те же, что оставила ИК) — цели прошлого кадра уже стоят.
            if (_legsSet && LegsUnchanged()) return;
            float phase = Mathf.Repeat(_walkPhase, 1f);
            for (int paw = 0; paw < ThicketWalkRules.PawCount; paw++) PlaceLeg(paw, phase, weight);
            _legsSet = true;
        }

        /// <summary>Вес состояния Walk базового слоя этого кадра: 1 — играет, смесь — доля перехода.</summary>
        private float WalkWeight()
        {
            int walk = StateHashes[Slot((int)ThicketClip.Walk, 0)];
            float weight = _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == walk ? 1f : 0f;
            if (_animator.IsInTransition(0))
            {
                float t = Mathf.Clamp01(_animator.GetAnimatorTransitionInfo(0).normalizedTime);
                weight = weight * (1f - t) + (_animator.GetNextAnimatorStateInfo(0).fullPathHash == walk ? t : 0f);
            }
            return weight;
        }

        private bool LegsUnchanged()
        {
            for (int paw = 0; paw < ThicketWalkRules.PawCount; paw++)
                if (!Same(_legUpper[paw].localRotation, _legSet[paw * 3]) || !Same(_legLower[paw].localRotation, _legSet[paw * 3 + 1])
                    || !Same(_legEnd[paw].localRotation, _legSet[paw * 3 + 2]))
                    return false;
            return true;
        }

        private static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        /// <summary>
        /// Лапа paw: точка касания клипа (костяшка передней, скакательный сустав задней) → цель ThicketWalkFeet в осях
        /// корня, поправка — по весу Walk; конец ноги сдвигается на столько же (и поворачивается вокруг касания на разворот
        /// стопы), колено и плечо — ИК, стопа сохраняет поворот клипа (плюс разворот на земле).
        /// </summary>
        private void PlaceLeg(int paw, float phase, float weight)
        {
            Transform upper = _legUpper[paw], lower = _legLower[paw], end = _legEnd[paw];
            Vector3 contact = _legContact[paw].position;
            Vector3 local = transform.InverseTransformPoint(contact);
            _feet.Place(paw, phase, local.x, local.z, out float tx, out float tz, out float yaw);
            Vector3 goal = transform.TransformPoint(new Vector3(Mathf.LerpUnclamped(local.x, tx, weight), local.y,
                Mathf.LerpUnclamped(local.z, tz, weight)));
            Quaternion turn = Quaternion.AngleAxis(yaw * weight, Vector3.up);
            Vector3 tip = end.position;
            Vector3 target = goal + turn * (tip - contact);
            Quaternion endRotation = turn * end.rotation;
            if ((target - tip).sqrMagnitude > 1e-8f
                && ThicketLegIk.Solve(N(upper.position), N(lower.position), N(tip), N(target), out var bend, out var aim))
            {
                lower.rotation = Q(bend) * lower.rotation;
                upper.rotation = Q(aim) * upper.rotation;
            }
            end.rotation = endRotation;
            _legSet[paw * 3] = upper.localRotation;
            _legSet[paw * 3 + 1] = lower.localRotation;
            _legSet[paw * 3 + 2] = end.localRotation;
        }

        private static System.Numerics.Vector3 N(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        private static Quaternion Q(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        /// <summary>
        /// Шаги разворота под ударом лапы (проверка находок 03.10): Sim крутит корпус в серии 3,5°/тик, клип удара
        /// стоит на месте — добавочные слои «Paw Turn Legs R/L» (задние лапы и опорная передняя) играют TurnL/TurnR
        /// долей по накопленному повороту, вес — по правилу <see cref="ThicketPawTurnLegs"/>; вне клипа удара гаснут.
        /// Слоёв нет (контроллер собран до 03.10) — правило идёт вхолостую.
        /// </summary>
        private void UpdatePawTurnLegs(float yaw, float dt)
        {
            // Замах веера семян (клип ливня) Sim тоже доворачивает корпус 3,5°/тик — те же шаги (ClipRules.TurnStepsUnder).
            bool paw = ThicketMasterClipRules.TurnStepsUnder(_pose.Clip, _seeds && _deathTick == None, out bool rightStrikes);
            _legs.Step(paw ? yaw : 0f, dt, paw, rightStrikes);
            if (_legsLayer[0] < 0) return;
            int turn = _legs.Sign < 0f ? 0 : 1;
            bool switched = _legs.Sign != 0f && turn != _legsState;
            for (int side = 0; side < 2; side++)
            {
                int layer = _legsLayer[side];
                _animator.SetLayerWeight(layer, side == 0 ? _legs.WeightRight : _legs.WeightLeft);
                if (_legs.Sign == 0f) continue;
                int slot = side * 2 + turn;
                _animator.SetFloat(LegsPhaseHashes[slot], _legs.Phase);
                if (!switched) continue;
                // Слой гас — клип сразу; смена стороны поворота посреди шагов — смесью, как у разворота на месте.
                if (_legsShown <= 0f || _legsState < 0) _animator.Play(LegsStateHashes[slot], layer, 0f);
                else _animator.CrossFadeInFixedTime(LegsStateHashes[slot], TurnBlendSeconds, layer, 0f);
            }
            if (switched) _legsState = turn;
            _legsShown = _legs.Weight;
        }

        /// <summary>
        /// Поза в контроллер: доля Motion Time в параметр состояния, смена состояния — смесью blend
        /// (с). restart — новое действие: тот же клип уходит во вторую копию состояния со смесью
        /// (у одного состояния Motion Time одна доля — смешать отход с кадром 0 нельзя).
        /// </summary>
        private void Sample(in ThicketClipPose pose, float blend, bool restart = false)
        {
            _pose = pose;
            int clip = (int)pose.Clip;
            // Клипа нет в контроллере (пакет неполный) — покой той же долей, без исключений.
            if (!_has[Slot(clip, 0)]) clip = (int)ThicketClip.Idle;
            if (!_has[Slot(clip, 0)]) return;
            float phase = clip == (int)pose.Clip ? pose.Phase
                : ThicketMasterClipRules.LoopPhase(_tick, ThicketMasterClipRules.IdleFrames);
            int current = _state >= 0 ? _state / Copies : -1;
            int slot;
            if (current == clip)
            {
                slot = _state;
                if (restart && _has[Slot(clip, 1)])
                {
                    slot = _state ^ 1;
                    _copy[clip] = slot & 1;
                }
            }
            else
            {
                // Входим в клип заново: другая копия, чем в прошлый раз, — прошлая могла ещё таять в смеси.
                if (_has[Slot(clip, 1)]) _copy[clip] ^= 1;
                else _copy[clip] = 0;
                slot = Slot(clip, _copy[clip]);
            }
            _animator.SetFloat(PhaseHashes[slot], phase);
            if (slot == _state) return;
            bool turn = IsTurn(clip) || IsTurn(current);
            _state = slot;
            if (turn) blend = Mathf.Max(blend, TurnBlendSeconds);
            if (blend <= 0f) _animator.Play(StateHashes[slot], 0, 0f);
            else _animator.CrossFadeInFixedTime(StateHashes[slot], blend, 0, 0f);
        }

        /// <summary>
        /// Тень ArenaView (CreateContactShadow) — 0,88 м на всех при масштабе корня 1, а тело
        /// 4,8 × 6,6 м (рост 4,14). Тень вешается на тело пула после его Awake, поэтому — при первой привязке.
        /// </summary>
        private void ScaleContactShadow()
        {
            if (_shadowScaled) return;
            foreach (Transform t in transform)
                if (t.name == ContactShadowName)
                {
                    t.localScale = Vector3.one * _contactShadowMetres;
                    _contactShadow = t;
                    _shadowLeft = 1f;
                    _shadowScaled = true;
                }
        }

        /// <summary>
        /// Поперечник тени — доля left от полного (_contactShadowMetres). Смерть сжимает её вместе с уходом
        /// тела под холм: тень стоит у корня, а холм — у середины лежащего тела (до 1,8 м вбок), иначе край
        /// тени тёмным пятном торчал бы из-под холма до ухода тела в пул (5,5 с). Не ноль — вырожденная матрица.
        /// </summary>
        private void SetShadowLeft(float left)
        {
            left = Mathf.Clamp01(left);
            if (_contactShadow == null || Mathf.Approximately(left, _shadowLeft)) return;
            _shadowLeft = left;
            _contactShadow.localScale = Vector3.one * (_contactShadowMetres * Mathf.Max(.002f, left));
        }

        private static bool IsTurn(int clip) => clip == (int)ThicketClip.TurnL || clip == (int)ThicketClip.TurnR;

        /// <summary>
        /// Без растворения шейдером (URP Lit) лежащее тело уходит в землю под конец
        /// показа смерти (такт убийства; потом ArenaView возвращает тело в пул).
        /// </summary>
        private void UpdateDeathSink(float seconds, EnemyDeathPresentation profile, EnemyKillBeat beat)
        {
            // «Цветущий холм» (владелец 02.10, вечер: «смерть надо доработать»): тело уходит под встающий
            // холм сразу после касания боком (ThicketMasterDeathRules.BodySink, land + 0,3 … + 1,8 с) и к
            // уходу из пула (BodyGoneAt, 5,5 с) давно под землёй — не пропадает кадром. Не позже BodyGoneAt.
            float land = beat.LandsAt > 0f ? beat.LandsAt : beat.HitStopSeconds + profile.FallSeconds;
            float k = ThicketMasterDeathRules.BodySink(seconds, land);
            float gone = Mathf.Max(.1f, beat.BodyGoneAt - .03f);
            if (seconds >= gone) k = 1f;
            float sink = k * DeathSinkMetres;
            // Тень корня уходит вместе с телом: под холмом её не видно, а сбоку не торчит.
            SetShadowLeft(ThicketMasterDeathRules.ShadowLeft(k));
            if (_deathFromBurrow)
            {
                float rise = 1f - Mathf.Clamp01(seconds / BurrowDeathRiseSeconds);
                sink = Mathf.Max(sink, rise * rise * BurrowDeathDepth);
            }
            SetSink(sink);
        }

        private void SetSink(float metres)
        {
            if (_body == null || Mathf.Approximately(metres, _sink)) return;
            _sink = metres;
            _body.localPosition = _bodyPosition + Vector3.down * metres;
        }

        /// <summary>
        /// Прячет все рендеры тела, включая контактную тень и контур ArenaView, — под
        /// землёй тела нет. Выключенные запоминаются и возвращаются при показе и в пул.
        /// </summary>
        private void SetHidden(bool hidden)
        {
            if (hidden == _isHidden) return;
            _isHidden = hidden;
            if (hidden)
            {
                // Набор рендеров читается заново (накладки фаз, тень и контур ArenaView ставятся
                // после Awake), но в свои списки — без выделений в бою (ревью 02.10).
                GetComponentsInChildren(false, _scan);
                _hidden.Clear();
                for (int i = 0; i < _scan.Count; i++)
                    if (_scan[i].enabled)
                    {
                        _scan[i].enabled = false;
                        _hidden.Add(_scan[i]);
                    }
                _scan.Clear();
                return;
            }
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].enabled = true;
            _hidden.Clear();
        }

        /// <summary>
        /// Привязка жива: та же симуляция и тело ArenaView этой сущности — всё ещё мы.
        /// Иначе ищем свою сущность сами (стенд без вызова Bind).
        /// </summary>
        private bool EnsureBound()
        {
            if (_arena == null) _arena = GetComponentInParent<ArenaView>();
            if (_driver == null && _arena != null) _driver = _arena.GetComponent<TickDriver>();
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return false;
            bool valid = _sim == sim && (uint)_entity < (uint)sim.Entities.Count && _generation == _driver.Generation;
            if (valid && (_arena == null || (_arena.TryGetEntityView(_entity, out Transform mine) && mine == transform))) return true;
            if (_arena == null) return false;
            for (int id = 1; id < sim.Entities.Count; id++)
                if (sim.Entities.Kind[id] == EnemyKind.ForestThicketMaster
                    && _arena.TryGetEntityView(id, out Transform view) && view == transform)
                {
                    Bind(_driver, id);
                    return true;
                }
            return false;
        }

        private void OnDisable()
        {
            // Возврат в пул: следующий владелец не должен получить тело спрятанным или в земле.
            SetHidden(false);
            SetSink(0f);
            SetShadowLeft(1f);
            if (_animator != null) _animator.speed = 1f;
            ForgetFeet();
            _renderSeen = false;
            if (gameObject.activeInHierarchy) return;
            _tail = false;
            _shownSerial = 0;
            _deathTick = None;
            _deathSeen = _deathFromBurrow = false;
            _entity = -1;
            _sim = null;
        }
    }
}
