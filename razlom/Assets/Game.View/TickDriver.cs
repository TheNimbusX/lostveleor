using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

// ENABLE_INPUT_SYSTEM и ENABLE_LEGACY_INPUT_MANAGER определяет сам Unity
// по настройке Player → Active Input Handling. При значении Both определены оба,
// и тогда берётся новая система как более новая из двух.
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Снимок presentation-relevant состояния источника в момент, когда
    /// симуляция породила событие.  FrameEvents переживает несколько тиков
    /// одного render-кадра, поэтому читать это состояние позднее напрямую из
    /// Simulation нельзя: там уже будет состояние последнего тика.
    /// </summary>
    public readonly struct FrameEventContext
    {
        public readonly SimEvent Event;
        public readonly int SimulationTick;
        public readonly int SourceForcedTicksLeft;
        public readonly byte SourceForcedKind;

        public FrameEventContext(SimEvent @event, int simulationTick,
            int sourceForcedTicksLeft, byte sourceForcedKind)
        {
            Event = @event;
            SimulationTick = simulationTick;
            SourceForcedTicksLeft = sourceForcedTicksLeft;
            SourceForcedKind = sourceForcedKind;
        }
    }

    /// <summary>
    /// Мост между Unity и симуляцией. Единственное место, где встречаются
    /// Time.deltaTime и Fix64.
    ///
    /// ПРАВИЛО БЕЗ ИСКЛЮЧЕНИЙ: представление никогда не пишет в симуляцию напрямую.
    /// Ввод собирается на кадре, квантуется в Fix64, буферизуется и применяется
    /// на границе тика. Анимация не решает, когда наносится урон — урон наносится
    /// на тике, анимация лишь показывает это.
    /// </summary>
    public sealed partial class TickDriver : MonoBehaviour
    {
        [Header("Сессия")]
        [Tooltip("0 — сгенерировать сид из текущего времени при старте.")]
        public ulong RunSeed = 0;
        public Game.Data.LocationProfileAsset Location;

        [Tooltip("Не используется в режиме забега — Разлом расставляет врагов по комнатам сам.")]
        public int EnemyCount = 40;

        /// <summary>
        /// Игра целиком: лагерь, портал, забег, экран итогов. Забег живёт
        /// внутри неё и пересоздаётся на каждый вход в Разлом.
        /// </summary>
        public GameSession Session { get; private set; }

        public RiftRun Run => Session != null ? Session.Run : null;

        /// <summary>Зажата ли кнопка удара прямо сейчас. Для отрисовки сектора атаки.</summary>
        public bool AttackHeld { get; private set; }

        /// <summary>
        /// Одноразовый приказ по земле именно на фронте нажатия ПКМ.
        /// Presentation использует его для короткого Dota-пинга; удержание
        /// кнопки продолжает обновлять путь, но не перезапускает маркер 60 раз/с.
        /// </summary>
        public bool MoveOrderPressedThisFrame { get; private set; }

        /// <summary>
        /// ПКМ прямо сейчас удерживается над землёй, а не над врагом.
        /// Это presentation-only состояние для редкого повтора метки приказа;
        /// симуляция по-прежнему получает ввод только через InputFrame.
        /// </summary>
        public bool MoveOrderHeld { get; private set; }

        /// <summary>
        /// Точка под курсором в мире, уже квантованная. Для подсветки того,
        /// на кого игрок навёл мышь.
        /// </summary>
        public FixVec2 CursorWorld => _pending.Aim;

        /// <summary>
        /// Сущность под курсором, или -1.
        ///
        /// Считается здесь, а не в отрисовке: этот же индекс уезжает в поток
        /// ввода как назначенная цель, и он обязан быть тем самым, что игрок
        /// видел под курсором в момент нажатия.
        /// </summary>
        public int HoveredEntity { get; private set; }

        /// <summary>
        /// Номер активной симуляции. Растёт при каждой её смене — вход в Разлом,
        /// вход на Полигон, возврат в лагерь. Отрисовка сравнивает его со своим
        /// и пересобирает привязки: у новой симуляции индексы сущностей
        /// начинаются заново, и старые привязки указывали бы в никуда.
        /// </summary>
        public int Generation => Session != null ? Session.Generation : 0;

        [Header("Дерево «Печати пламени» — способность 1")]
        [Tooltip("StatMod: +20% урона огнём.")]
        public bool NodeHotter = false;

        [Tooltip("Flag: знак делится на три снаряда, урон каждого −45%.")]
        public bool NodeSplit = false;

        [Tooltip("EffectInsert: горящий враг при смерти поджигает ближайшего.")]
        public bool NodeSpreads = false;

        [Header("Отладка")]
        public bool LogStateHash = false;
        public int MaxTicksPerFrame = 5;

        /// <summary>
        /// Что сейчас рисовать. В лагере вне Полигона — null: рисовать там
        /// пока нечего, и это честнее, чем держать пустую симуляцию ради
        /// того, чтобы поле не было пустым.
        /// </summary>
        public Simulation Sim => Session != null ? Session.ActiveSim : null;

        /// <summary>Доля тика, прошедшая с последнего шага. Для интерполяции отрисовки.</summary>
        public float Alpha { get; private set; }

        /// <summary>
        /// Системная пауза принадлежит оболочке игры, а не детерминированной
        /// симуляции. Пока она активна, ввод не собирается и тики не копятся.
        /// </summary>
        public bool GameplayPaused { get; private set; }

        public bool CanReturnToCamp
            => Session != null && (Session.Mode != GameMode.Camp || Session.OnProvingGround);

        /// <summary>
        /// Ставит команду забега в тот же буфер, что и клавиатурный ввод.
        /// HUD вызывает этот метод только после клика по карточке награды;
        /// команда будет применена на ближайшей границе тика.
        /// </summary>
        public void QueueRunCommand(RunCommand command)
        {
            if (Session == null || Session.Mode != GameMode.Rift || Run == null) return;

            bool validChoice = command >= RunCommand.ChooseReward1
                               && command <= RunCommand.ChooseReward3;
            if (command != RunCommand.Leave && (!validChoice || Run.Phase != RunPhase.ChoosingReward))
                return;

            _commandLatch = (byte)command;
        }

        /// <summary>
        /// События всех тиков, случившихся на этом кадре, в порядке возникновения.
        ///
        /// Sim.Events живёт ровно один тик — Step очищает список в начале шага,
        /// а на просевшем кадре шагов бывает несколько. Читать Sim.Events напрямую
        /// значит терять всё, кроме последнего тика. Потребители (цифры урона,
        /// VFX, звук) читают это в LateUpdate, когда кадр уже отшагал.
        /// </summary>
        public IReadOnlyList<SimEvent> FrameEvents => _frameEvents;

        /// <summary>
        /// Те же события с коротким снимком состояния источника на их
        /// авторитетном тике. Индексы совпадают с <see cref="FrameEvents"/>.
        /// </summary>
        public IReadOnlyList<FrameEventContext> FrameEventContexts => _frameEventContexts;

        private readonly List<SimEvent> _frameEvents = new List<SimEvent>(256);
        private readonly List<FrameEventContext> _frameEventContexts =
            new List<FrameEventContext>(256);

        /// <summary>
        /// Под какую самую большую симуляцию рассчитан буфер интерполяции.
        /// Совпадает с ёмкостью по умолчанию у Simulation — Полигон меньше,
        /// и меньшая просто не займёт весь буфер.
        /// </summary>
        public const int MaxSimCapacity = 512;

        private const float TickLength = 1f / Simulation.TicksPerSecond;
        private float _accumulator;

        [Tooltip("Стенд для съёмки способностей: ровно три неподвижных врага " +
                 "вокруг игрока, без урона и без атак. Обычной игре не нужен.")]
        public bool ShowcaseStandInEditor;

        [Tooltip("Пишет в консоль каждый скачок тела больше трёх обычных шагов " +
                 "за тик. Нужен, когда «оно телепортируется», а глазом причину " +
                 "не поймать.")]
        public bool WatchTeleports;

        private InputFrame _pending = InputFrame.Empty;
        private byte _abilityLatch;
        private InputFrame _abilityPressFrame;
        private bool _abilityPressLatched;
        private int _targetAimSlot = -1;
        public int AbilityTargetAimSlot => _targetAimSlot;
        public bool AimingAbilityTarget => _targetAimSlot >= 0 && !GameplayPaused && Sim != null
            && Sim.Entities.Alive[Simulation.PlayerId];
        private byte _commandLatch;
        // Читается каждый кадр, поэтому живёт полем, а не литералом массива в
        // CaptureInput: шестьдесят выбросов в секунду на ровном месте.
        private readonly bool[] _slotPressed = new bool[Simulation.AbilitySlots];
        // Render frames can be shorter than the 30 Hz simulation tick. Keep the
        // exact RMB edge (intent, aim and target) until one Step consumes it;
        // otherwise a quick click can show presentation feedback yet never
        // reach deterministic gameplay.
        private bool _pointerPressLatched;
        private InputFrame _pointerPressFrame = InputFrame.Empty;

        /// <summary>Поколение, под которое уже настроено представление.</summary>
        private int _shownGeneration = -1;
        private int _movingCombatStartedTick = -1;

        // Камера кэшируется: Camera.main ищет объект по тегу, и делать это
        // каждый кадр незачем.
        private Camera _camera;
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        // Буфер узлов выделен один раз: пересборка билда не должна мусорить.
        private readonly AbilityNode[] _nodeBuffer = new AbilityNode[3];
        private bool _appliedHotter, _appliedSplit, _appliedSpreads;

        // Позиции и направления на предыдущем тике — нужны, чтобы
        // интерполировать отрисовку. Поворот идёт из того же тика, что и
        // позиция, и обязан интерполироваться вместе с ней.
        private FixVec2[] _prevPositions = new FixVec2[0];
        private FixVec2[] _prevFacings = new FixVec2[0];

        private void Awake()
        {
            gameObject.AddComponent<PelagTargetAimView>();
            gameObject.AddComponent<CampPlayerView>();
            ulong seed = RunSeed != 0 ? RunSeed : (ulong)System.DateTime.UtcNow.Ticks;
            RunSeed = seed;

            // Игра начинается в ЛАГЕРЕ, а не в Разломе. Забег теперь то, во что
            // входят, а не то, что запускается вместо главного меню.
            var location = Location != null ? Location.ToDefinition() : null;
            Session = CampSaveStore.Load(seed, location);
            gameObject.AddComponent<CampSaveStore>();
            // ВИТРИНА БОЛЬШЕ НЕ ВКЛЮЧАЕТСЯ САМА В PLAY MODE.
            //
            // Здесь стояло `|| Application.isEditor`. Это была подпорка под
            // съёмку combat slice: Play Mode ставил тот же стенд 1+3, который
            // снимала камера. Подпорка пережила задачу и стала ложью про игру —
            // при каждом Play вокруг игрока вставали РОВНО ТРИ врага с нулевым
            // уроном, нулевой скоростью и `NextAttackTick = int.MaxValue`, то
            // есть манекены, которые физически не могут ни подойти, ни ударить.
            // Владелец так и описал: «три челика стоят как манекены и не бьют».
            //
            // Теперь Play Mode играет НАСТОЯЩИЙ забег. Стенд остался доступен
            // галкой в инспекторе — он нужен для съёмки способностей.
            Session.WhirlwindShowcase = CaptureRig.WhirlwindShowcase || ShowcaseStandInEditor;
            WatchTeleports |= CaptureRig.WatchTeleports;
            Session.CombatFeelShowcase = CaptureRig.CombatFeelTier;
            Session.CombatFeelEnemyCount = CaptureRig.HasEnemyOverride
                ? CaptureRig.EnemyOverride
                : 1;

            // Буфер выделяется с запасом под самую большую из симуляций сессии.
            // Иначе сбой вылезал бы кадром позже и в другом месте — в интерполяции
            // отрисовки, к настоящей причине отношения не имеющей.
            _prevPositions = new FixVec2[MaxSimCapacity];
            _prevFacings = new FixVec2[MaxSimCapacity];

            Debug.Log($"[Разлом] Лагерь. Сид сессии {seed}. E — войти в Разлом, T — Полигон.");
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RestoreDeveloperThemeIfNeeded();
#endif
            // Если Awake не доработал, компонент выключается, а не сыплет
            // одинаковой ошибкой каждый кадр. Тысяча одинаковых строк в консоли
            // прячет первую — ту единственную, в которой написана причина.
            if (Session == null)
            {
                Debug.LogError("[Разлом] TickDriver: сессия не создана, Awake оборвался. " +
                               "Причина — в ПЕРВОЙ ошибке консоли, выше этой строки. Компонент выключен.");
                enabled = false;
                return;
            }

            if (GameplayPaused || CampPlayerView.Instance?.InventoryOpen == true)
            {
                _frameEvents.Clear();
                _frameEventContexts.Clear();
                Alpha = 0f;
                return;
            }

            // Симуляция могла смениться между кадрами — например, игрока
            // отправили в Разлом кнопкой в интерфейсе лагеря.
            SyncGeneration();

            // Камера создаётся Bootstrap-ом в том же Awake, поэтому берём её
            // при первом обращении, а не в Awake: порядок вызовов не гарантирован.
            if (_camera == null) _camera = Camera.main;

            // Узлы можно щёлкать прямо во время игры: пересборка билда стоит
            // копейки и случается только когда чекбокс реально поменялся.
            if (NodeHotter != _appliedHotter || NodeSplit != _appliedSplit || NodeSpreads != _appliedSpreads)
                ApplyAbilityBuild();

            CaptureInput();

            _frameEvents.Clear();
            _frameEventContexts.Clear();
            _accumulator += Time.deltaTime;

            int steps = 0;
            while (_accumulator >= TickLength && steps < MaxTicksPerFrame)
            {
                if (Sim != null) SavePreviousPositions();

                InputFrame frame = ConsumeInput();
                CampPlayerView.Instance?.PrepareInput(ref frame);

                // Шагает СЕССИЯ, а не забег и тем более не симуляция: что
                // именно шагает, решает режим. В лагере не идёт даже время боя.
                int tickBefore = Sim != null ? Sim.Tick : 0;
                int depthBefore = Run != null ? Run.Depth : 0;

                Session.Step(in frame);

                // У короткого Шквала смерть должна попасть внутрь серии;
                // 24 тика оставляем длинным якорным способностям.
                int captureDeathDelay = CaptureRig.VfxShowcase == PelagVfxShowcase.ChainStep ? 8 : 24;
                if (CaptureRig.DeathDuringSkill && CaptureRig.LiveSkill && Sim != null
                    && _liveSkillCastTick >= 0 && Sim.Tick - _liveSkillCastTick == captureDeathDelay
                    && Sim.Entities.Alive[Simulation.PlayerId])
                {
                    Session.SetDeveloperInvulnerable(false);
                    Sim.ApplyAbilityDamage(1, Simulation.PlayerId, 100000, 0, DamageType.Physical);
                    Debug.Log($"[capture-skill-interrupt] tick={Sim.Tick} alive={Sim.Entities.Alive[Simulation.PlayerId]}");
                }

                if (_shownGeneration != Session.Generation) SyncGeneration();
                else if (Sim != null && Sim.Tick != tickBefore)
                {
                    PlayEvents(Sim.Events);
                    if (WatchTeleports) ReportTeleports(tickBefore);
                }

                // Новый Разлом — интерполировать не от чего: старые позиции
                // относятся к другой локации, и кадр показал бы, как все
                // размазываются через полкарты.
                if (Sim != null && Run != null && Run.Depth != depthBefore) SavePreviousPositions();

                _accumulator -= TickLength;
                steps++;
            }

            // Если кадр просел настолько, что накопилось больше MaxTicksPerFrame шагов,
            // излишек отбрасывается: лучше замедлить время, чем словить спираль смерти.
            //
            // Но отбрасывается ТОЛЬКО лишние целые тики, а не дробный остаток.
            // Раньше здесь стоял ноль, и после каждой просадки Alpha падала в
            // ноль вместе с ним: тело, уже доехавшее до середины между тиками,
            // отскакивало на начало. Замер поймал это на кадре в 333 мс —
            // тридцать тел прыгнули разом. Просадка и так стоит рывка, добавлять
            // к ней второй незачем.
            if (steps >= MaxTicksPerFrame)
                _accumulator = Mathf.Repeat(_accumulator, TickLength);

            // Смена симуляции посреди кадра обнуляет накопитель, а цикл после
            // этого успевает вычесть из него длину тика. Отрицательный
            // накопитель дал бы отрицательную Alpha на следующем кадре.
            if (_accumulator < 0f) _accumulator = 0f;

            Alpha = Mathf.Clamp01(_accumulator / TickLength);
        }

        public void SetGameplayPaused(bool paused)
        {
            if (GameplayPaused == paused) return;
            GameplayPaused = paused;
            ClearCapturedInput();
        }

        public void ClearCapturedInput()
        {
            _targetAimSlot = -1;

            // Ни приказ, нажатый перед открытием меню, ни клавиша из самого
            // меню не должны сработать после закрытия паузы.
            _pending = InputFrame.Empty;
            _pointerPressFrame = InputFrame.Empty;
            _pointerPressLatched = false;
            _abilityLatch = 0;
            _abilityPressLatched = false;
            _commandLatch = 0;
            _accumulator = 0f;
            _frameEvents.Clear();
            _frameEventContexts.Clear();
            AttackHeld = false;
            MoveOrderPressedThisFrame = false;
            MoveOrderHeld = false;
            HoveredEntity = -1;
            Alpha = 0f;
        }

        /// <summary>Системное действие pause-меню, выполняемое вне боевого тика.</summary>
        public void ReturnToCampFromMenu()
        {
            if (Session == null) return;
            Session.ReturnToCamp();
        }

        /// <summary>
        /// Ввод собирается на частоте кадра и КВАНТУЕТСЯ В Fix64 ЗДЕСЬ.
        /// Ни одно float-значение не должно пересечь границу симуляции.
        ///
        /// Обе ветки читают ОДНО И ТО ЖЕ: точку под курсором, правую кнопку
        /// мыши и четыре клавиши того ряда, который выбран в настройках. Какая
        /// система ввода включена в настройках проекта — вопрос сборки, и на
        /// поведение симуляции влиять не должен.
        /// </summary>
        private void CaptureInput()
        {
            MoveOrderPressedThisFrame = false;

            // На экране награды тот же ряд означает ВЫБОР, а не способность.
            // Одни и те же клавиши: у игрока не должно быть двух рядов кнопок,
            // а бой на этом экране всё равно стоит.
            bool choosing = Session.Mode == GameMode.Rift
                            && Run != null && Run.Phase == RunPhase.ChoosingReward;
            bool letters = GameUserSettings.AbilityRowUsesLetters;
            _pending.AbilityHoldMask = 0;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (letters)
                {
                    _slotPressed[0] = kb.qKey.wasPressedThisFrame;
                    if (kb.qKey.isPressed) _pending.AbilityHoldMask |= 1;
                    _slotPressed[1] = kb.wKey.wasPressedThisFrame;
                    if (kb.wKey.isPressed) _pending.AbilityHoldMask |= 2;
                    _slotPressed[2] = kb.eKey.wasPressedThisFrame;
                    if (kb.eKey.isPressed) _pending.AbilityHoldMask |= 4;
                    _slotPressed[3] = kb.rKey.wasPressedThisFrame;
                    if (kb.rKey.isPressed) _pending.AbilityHoldMask |= 8;
                }
                else
                {
                    _slotPressed[0] = kb.digit1Key.wasPressedThisFrame;
                    if (kb.digit1Key.isPressed) _pending.AbilityHoldMask |= 1;
                    _slotPressed[1] = kb.digit2Key.wasPressedThisFrame;
                    if (kb.digit2Key.isPressed) _pending.AbilityHoldMask |= 2;
                    _slotPressed[2] = kb.digit3Key.wasPressedThisFrame;
                    if (kb.digit3Key.isPressed) _pending.AbilityHoldMask |= 4;
                    _slotPressed[3] = kb.digit4Key.wasPressedThisFrame;
                    if (kb.digit4Key.isPressed) _pending.AbilityHoldMask |= 8;
                }

                // ПЯТЫЙ СЛОТ — общий кувырок, и клавиша у него ОДНА В ОБОИХ
                // РЯДАХ. Ряд способностей — это настройка вкуса, 1234 против
                // QWER; кувырок в него не входит вовсе, он живёт под большим
                // пальцем, как деш в любом экшене. Поэтому Space стоит после
                // развилки, а не дважды внутри неё.
                _slotPressed[4] = kb.spaceKey.wasPressedThisFrame;
                if (kb.spaceKey.isPressed) _pending.AbilityHoldMask |= 16;

                LatchSlots(_slotPressed, choosing);

                LatchKeys(
                    leave: kb.lKey.wasPressedThisFrame,
                    enter: kb.eKey.wasPressedThisFrame || CaptureRig.AutoEnterRift,
                    repeat: kb.rKey.wasPressedThisFrame,
                    back: kb.cKey.wasPressedThisFrame,
                    ground: kb.tKey.wasPressedThisFrame,
                    salvage: kb.vKey.wasPressedThisFrame);
            }

            Mouse mouse = Mouse.current;
            CaptureAim(
                mouse != null ? mouse.position.ReadValue() : Vector2.zero,
                moveHeld: mouse != null && mouse.rightButton.isPressed,
                movePressed: mouse != null && mouse.rightButton.wasPressedThisFrame,
                attackHeld: mouse != null && mouse.leftButton.isPressed);
#else
            if (letters)
            {
                _slotPressed[0] = Input.GetKeyDown(KeyCode.Q);
                if (Input.GetKey(KeyCode.Q)) _pending.AbilityHoldMask |= 1;
                _slotPressed[1] = Input.GetKeyDown(KeyCode.W);
                if (Input.GetKey(KeyCode.W)) _pending.AbilityHoldMask |= 2;
                _slotPressed[2] = Input.GetKeyDown(KeyCode.E);
                if (Input.GetKey(KeyCode.E)) _pending.AbilityHoldMask |= 4;
                _slotPressed[3] = Input.GetKeyDown(KeyCode.R);
                if (Input.GetKey(KeyCode.R)) _pending.AbilityHoldMask |= 8;
            }
            else
            {
                _slotPressed[0] = Input.GetKeyDown(KeyCode.Alpha1);
                if (Input.GetKey(KeyCode.Alpha1)) _pending.AbilityHoldMask |= 1;
                _slotPressed[1] = Input.GetKeyDown(KeyCode.Alpha2);
                if (Input.GetKey(KeyCode.Alpha2)) _pending.AbilityHoldMask |= 2;
                _slotPressed[2] = Input.GetKeyDown(KeyCode.Alpha3);
                if (Input.GetKey(KeyCode.Alpha3)) _pending.AbilityHoldMask |= 4;
                _slotPressed[3] = Input.GetKeyDown(KeyCode.Alpha4);
                if (Input.GetKey(KeyCode.Alpha4)) _pending.AbilityHoldMask |= 8;
            }

            // Кувырок вне ряда: одна клавиша при обоих раскладах. См.
            // комментарий в ветке ENABLE_INPUT_SYSTEM.
            _slotPressed[4] = Input.GetKeyDown(KeyCode.Space);
            if (Input.GetKey(KeyCode.Space)) _pending.AbilityHoldMask |= 16;

            LatchSlots(_slotPressed, choosing);

            LatchKeys(
                leave: Input.GetKeyDown(KeyCode.L),
                // На время combat-slice Play Mode сразу открывает тот же Rift
                // 1+3, который проходит capture; старый Полигон здесь больше
                // не должен маскироваться под проверку Вихря.
                enter: Input.GetKeyDown(KeyCode.E) || CaptureRig.AutoEnterRift,
                repeat: Input.GetKeyDown(KeyCode.R),
                back: Input.GetKeyDown(KeyCode.C),
                ground: Input.GetKeyDown(KeyCode.T),
                salvage: Input.GetKeyDown(KeyCode.V));

            CaptureAim(Input.mousePosition,
                moveHeld: Input.GetMouseButton(1),
                movePressed: Input.GetMouseButtonDown(1),
                attackHeld: Input.GetMouseButton(0));
#endif

            if (CaptureRig.RunShowcase && Sim != null)
            {
                // Deterministic locomotion QA: long straight segments followed
                // by hard 90-degree turns expose bad retargeting, foot sliding
                // and facing pops much better than a hand-driven capture.
                int phase = (Sim.Tick / 45) & 3;
                switch (phase)
                {
                    case 0: _pending.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero); break;
                    case 1: _pending.Aim = new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5)); break;
                    case 2: _pending.Aim = new FixVec2(Fix64.FromInt(-5), Fix64.FromInt(5)); break;
                    default: _pending.Aim = new FixVec2(Fix64.FromInt(-5), Fix64.FromInt(-5)); break;
                }
                _pending.Flags = (byte)InputFlags.MoveOrder;
                AttackHeld = false;
            }

            if (CaptureRig.LocomotionShowcase && Sim != null)
            {
                // Изолированный stop/turn QA: сначала короткий пробег, затем
                // один-единственный близкий клик назад. После него кнопка уже
                // отпущена — так проверяется, что приказ разворота живёт в Sim,
                // а не поддерживается повторяющимся capture-вводом.
                int tick = Sim.Tick;
                FixVec2 player = Sim.Entities.Position[Simulation.PlayerId];
                if (tick < 24)
                {
                    _pending.Aim = new FixVec2(Fix64.FromInt(4), Fix64.Zero);
                    _pending.Flags = (byte)InputFlags.MoveOrder;
                }
                else if (tick == 46)
                {
                    _pending.Aim = player + new FixVec2(Fix64.Ratio(-3, 10), Fix64.Zero);
                    _pending.Flags = (byte)InputFlags.MoveOrder;
                }
                else if (tick == 80 || tick == 110 || tick == 140 || tick == 170)
                {
                    // Alternating short clicks: left/right 90 and 180 degrees,
                    // all inside the turn-in-place radius, through real orders.
                    FixVec2 direction = tick == 80 ? new FixVec2(Fix64.Zero, Fix64.One)
                        : tick == 110 ? new FixVec2(Fix64.One, Fix64.Zero)
                        : tick == 140 ? new FixVec2(-Fix64.One, Fix64.Zero)
                        : new FixVec2(Fix64.One, Fix64.Zero);
                    _pending.Aim = player + direction * Fix64.Ratio(3, 10);
                    _pending.Flags = (byte)InputFlags.MoveOrder;
                }
                else if (tick >= 200 && tick < 225)
                {
                    _pending.Aim = player + new FixVec2(Fix64.Zero, Fix64.FromInt(3));
                    _pending.Flags = (byte)InputFlags.MoveOrder;
                }
                else
                {
                    _pending.Flags = 0;
                }
                AttackHeld = false;
            }

            if (CaptureRig.IsCombatFeelShowcase && !CaptureRig.MovingCombatShowcase && !CaptureRig.LiveSkill && Sim != null)
            {
                bool attack = !CaptureRig.GcWarmupActive;
                _pending.Flags = attack ? (byte)InputFlags.Attack : (byte)0;
                _pending.AttackTarget = -1;
                AttackHeld = attack;
            }

            if (CaptureRig.MovingCombatShowcase && Sim != null)
            {
                _pending.Flags = 0;
                _pending.AttackTarget = -1;
                AttackHeld = false;

                if (CaptureRig.GcWarmupActive)
                {
                    _movingCombatStartedTick = -1;
                }
                else
                {
                    if (_movingCombatStartedTick < 0)
                        _movingCombatStartedTick = Sim.Tick;
                    int elapsed = Sim.Tick - _movingCombatStartedTick;
                    FixVec2 player = Sim.Entities.Position[Simulation.PlayerId];

                    // Attack starts against the authored stand directly in
                    // front of Pelag. A configurable delay also tests releasing
                    // planted feet after contact. Then a ground order takes
                    // ownership of locomotion without erasing that committed
                    // swing, making the 50%-speed hit-in-motion unambiguous.
                    if (elapsed == 0 && Sim.Entities.Count > 1)
                    {
                        _pending.Aim = Sim.Entities.Position[1];
                        _pending.Flags = (byte)InputFlags.Attack;
                        _pending.AttackTarget = 1;
                        AttackHeld = true;
                    }
                    else if (elapsed == CaptureRig.MovingCombatDelay)
                    {
                        _pending.Aim = player + new FixVec2(Fix64.Zero, Fix64.FromInt(1));
                        _pending.Flags = (byte)InputFlags.MoveOrder;
                        MoveOrderPressedThisFrame = true;
                    }
                    // Once the first strike has visibly recovered, move again
                    // and cast the sole production ability in the same tick.
                    // It must keep control, cancel an unseen pending basic, and
                    // use the same half-speed action contract.
                    else if (elapsed == 35)
                    {
                        _pending.Aim = player + new FixVec2(Fix64.Zero, Fix64.FromInt(-2));
                        _pending.Flags = (byte)InputFlags.MoveOrder;
                        MoveOrderPressedThisFrame = true;
                        _abilityLatch |= 1;
                    }
                    else if (elapsed >= 60 && Sim.Entities.Count > 1)
                    {
                        // Exercise Whirlwind recovery directly into a real
                        // target order and the next authoritative basic stroke.
                        _pending.Aim = Sim.Entities.Position[1];
                        _pending.Flags = (byte)InputFlags.Attack;
                        _pending.AttackTarget = 1;
                        AttackHeld = true;
                    }
                }
            }

            if (!choosing && CaptureRig.ShouldCastWhirlwind(Sim != null ? Sim.Tick : -1))
                _abilityLatch |= 1;
            if (CaptureRig.SweepAimCapture && Sim != null) UpdateSweepAimCapture();
            else if (CaptureRig.LiveSkill && Sim != null)
            {
                _pending.Flags = 0;
                _pending.AttackTarget = -1;
                AttackHeld = false;
                if (CaptureRig.GcWarmupActive) { _liveSkillStartedTick = -1; _liveSkillCastStage = 0; }
                else
                {
                    if (_liveSkillStartedTick < 0) _liveSkillStartedTick = Sim.Tick;
                    int elapsed = Sim.Tick - _liveSkillStartedTick;
                    if (CaptureRig.VfxShowcase == PelagVfxShowcase.Cleave && Sim.Entities.Count > 1)
                    {
                        Vector3 direction = Quaternion.Euler(0f, CaptureRig.CastYaw, 0f) * Vector3.right;
                        var facing = new FixVec2(Fix64.FromDouble(direction.x), Fix64.FromDouble(direction.z));
                        if (elapsed < 18)
                        {
                            Sim.Entities.Facing[Simulation.PlayerId] = facing;
                            Sim.Entities.Position[1] = Sim.Entities.Position[Simulation.PlayerId]
                                + facing * Fix64.FromDouble(CaptureRig.CastDistance);
                        }
                        if (elapsed == 26 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-cleave-miss") >= 0)
                            Sim.Entities.Position[1] = Sim.Entities.Position[Simulation.PlayerId] + facing * Fix64.FromInt(8);
                    }
                    bool sequence = CaptureRig.VfxShowcase == PelagVfxShowcase.Rotation;
                    if (CaptureRig.PerformanceCapture && !sequence && elapsed >= 180)
                    {
                        _liveSkillStartedTick = Sim.Tick;
                        elapsed = 0;
                        _liveSkillCastStage = 0;
                    }
                    if (CaptureRig.RunShowcase && elapsed < (sequence ? 282 : 90))
                    {
                        _pending.Flags = (byte)InputFlags.MoveOrder;
                        _pending.Aim = Sim.Entities.Position[Simulation.PlayerId]
                            + (CaptureRig.TurnDuringSkill && elapsed % 48 >= 26
                                ? new FixVec2(Fix64.FromInt(-2), Fix64.FromInt(3))
                                : new FixVec2(Fix64.FromInt(3), Fix64.One));
                    }
                    int cycloneAt = sequence ? 93 : 18;
                    _pending.AbilityHoldMask = (byte)(elapsed >= cycloneAt && elapsed < cycloneAt + CaptureRig.HoldTicks ? 4 : 0);
                    int castAt = sequence ? 18 + _liveSkillCastStage * 75 : 18;
                    if (_liveSkillCastStage < (sequence ? 4 : 1) && elapsed >= castAt)
                    {
                        int definition = sequence
                            ? (_liveSkillCastStage == 0 ? AbilityDefinition.AnchorLeapId
                                : _liveSkillCastStage == 1 ? AbilityDefinition.AnchorSlamId
                                : _liveSkillCastStage == 2 ? AbilityDefinition.WhirlwindId : AbilityDefinition.ChainStepId)
                            : CaptureRig.VfxShowcase == PelagVfxShowcase.AnchorLeap ? AbilityDefinition.AnchorLeapId
                            : CaptureRig.VfxShowcase == PelagVfxShowcase.AnchorSweep ? AbilityDefinition.AnchorSlamId
                            : CaptureRig.VfxShowcase == PelagVfxShowcase.ChainStep ? AbilityDefinition.ChainStepId
                            : CaptureRig.VfxShowcase == PelagVfxShowcase.Cleave ? AbilityDefinition.CleaveId
                            : AbilityDefinition.WhirlwindId;
                        for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                            if (Sim.GetAbility(slot)?.DefinitionId == definition) _abilityLatch |= (byte)(1 << slot);
                        _pending.AbilityTarget = Sim.Entities.Count > 1 ? 1 : -1;
                        _liveSkillCastStage++;
                        _liveSkillCastTick = Sim.Tick;
                        Debug.Log($"[capture-live-cast] ability={definition} tick={Sim.Tick} stage={_liveSkillCastStage}");
                        // Прицел каста не заменяет ранее отданный приказ движения.
                        _pending.Flags = 0;
                        Vector3 cast = Quaternion.Euler(0f, CaptureRig.CastYaw, 0f) * Vector3.right * CaptureRig.CastDistance;
                        _pending.Aim = Sim.Entities.Position[Simulation.PlayerId]
                            + new FixVec2(Fix64.FromDouble(cast.x), Fix64.FromDouble(cast.z));
                    }
                    if (elapsed >= (sequence ? 282 : 90) && Sim.Entities.Count > 1)
                    {
                        _pending.Aim = Sim.Entities.Position[1];
                        _pending.Flags = (byte)InputFlags.Attack;
                        _pending.AttackTarget = 1;
                        AttackHeld = true;
                    }
                }
            }
            // При 60+ FPS между нажатием и тиком Sim есть новые кадры ввода.
            // Сохраняем прицел вместе с кнопкой, иначе движение заменяет цель броска.
            if (_abilityLatch != 0 && !_abilityPressLatched)
            {
                _abilityPressFrame = _pending;
                _abilityPressLatched = true;
            }
        }
        private void ResolveTargetAim(bool confirm, bool cancel)
        {
            if (_targetAimSlot < 0) return;
            if (cancel) { _targetAimSlot = -1; return; }
            if (!confirm) return;

            // ДВА РОДА ПРИЦЕЛИВАНИЯ.
            //
            // Шаг по цепи выбирает ВРАГА: без цели прыгать не к кому, поэтому
            // подтверждение требует наведения на живого противника.
            //
            // Бросок якоря выбирает ТОЧКУ. Это перемещение, и притягиваться к
            // пустому месту — законный и основной сценарий: уйти из окружения,
            // перескочить пропасть, занять позицию. Требовать здесь врага
            // значило бы запретить способности её главное применение.
            if (GroundTargetedSlot(_targetAimSlot))
            {
                // Точка уже лежит в _pending.Aim — это позиция курсора на полу.
                // Дальность обрезает сама симуляция по AnchorKit.LeapRange.
                _abilityLatch |= (byte)(1 << _targetAimSlot);
                _targetAimSlot = -1;
                return;
            }

            if (Sim.ValidAbilityTarget(HoveredEntity, Sim.GetAbility(_targetAimSlot)))
            {
                _pending.AbilityTarget = HoveredEntity;
                _abilityLatch |= (byte)(1 << _targetAimSlot);
                _targetAimSlot = -1;
            }
        }

        /// <summary>Слот целится в точку на полу, а не во врага.</summary>
        public bool GroundTargetedSlot(int slot) =>
            Sim?.GetAbility(slot)?.DefinitionId == AbilityDefinition.AnchorLeapId;

        /// <summary>Слот вообще требует выбора цели перед применением.</summary>
        private bool TargetedSlot(int slot)
        {
            int? id = Sim?.GetAbility(slot)?.DefinitionId;
            return id == AbilityDefinition.ChainStepId || id == AbilityDefinition.AnchorLeapId;
        }

        private void UpdateSweepAimCapture()
        {
            _pending.Flags = 0; _pending.AttackTarget = -1; AttackHeld = false;
            if (CaptureRig.GcWarmupActive) { _liveSkillStartedTick = -1; _aimCaptureLastTick = -1; return; }
            if (_liveSkillStartedTick < 0) _liveSkillStartedTick = Sim.Tick;
            int elapsed = Sim.Tick - _liveSkillStartedTick;
            _pending.Aim = Sim.Entities.Position[Simulation.PlayerId] + new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            if (_aimCaptureLastTick == elapsed) return;
            _aimCaptureLastTick = elapsed;
            if (elapsed == 6 || elapsed == 40)
            {
                System.Array.Clear(_slotPressed, 0, _slotPressed.Length);
                _slotPressed[2] = true;
                LatchSlots(_slotPressed, false);
                _slotPressed[2] = false;
            }
            if (elapsed == 35) ResolveTargetAim(false, true);
            if (elapsed == 65) ResolveTargetAim(true, false);
        }

        private int _liveSkillStartedTick = -1;
        private int _liveSkillCastTick = -1;
        private int _liveSkillCastStage;
        private int _aimCaptureLastTick = -1;

        /// <summary>
        /// Одна и та же клавиша означает разное в разных режимах — ровно как
        /// и сам байт команды. Поэтому раскладка решается ЗДЕСЬ, в одном месте,
        /// а не дважды в двух одинаковых ветках ввода.
        /// </summary>
        private void LatchKeys(bool leave, bool enter, bool repeat, bool back,
            bool ground, bool salvage)
        {
            switch (Session.Mode)
            {
                case GameMode.Rift:
                    if (leave) _commandLatch = (byte)RunCommand.Leave;
                    break;

                case GameMode.Camp:
                    if (CampPlayerView.Instance != null && CampPlayerView.Instance.InventoryOpen) break;
                    // На Полигоне E — это способность из ряда QWER, а не выход
                    // в Разлом: Полигон и существует ради проверки способностей,
                    // и молча съедать треть ряда он не должен. Сойти с него
                    // по-прежнему T, и после этого E работает как раньше.
                    //
                    // На цифровом ряду пересечения нет вообще, и уступка тоже
                    // не нужна: там E остаётся входом в Разлом даже с Полигона.
                    if (enter && (CaptureRig.AutoEnterRift || CampPlayerView.Instance == null) && !(Session.OnProvingGround
                                   && GameUserSettings.AbilityRowUsesLetters))
                        _commandLatch = (byte)CampCommand.EnterRift;
                    if (ground) _commandLatch = (byte)CampCommand.ToggleProvingGround;
                    if (salvage) _commandLatch = (byte)CampCommand.SalvageJunk;
                    break;

                case GameMode.Summary:
                    if (repeat) _commandLatch = (byte)CampCommand.RepeatRift;
                    if (back) _commandLatch = (byte)CampCommand.ReturnToCamp;
                    break;
            }
        }

        /// <summary>
        /// Нажатия копятся между тиками, чтобы короткий тап не потерялся.
        /// Куда именно они копятся — в способности или в выбор награды —
        /// решает фаза забега.
        ///
        /// Ряд QWER делит две клавиши с командами режимов: E — вход в Разлом,
        /// R — повтор забега. Способность латчится только там, где ей есть куда
        /// прийти: в Разломе и на Полигоне. В пустом лагере и на экране итогов
        /// шагать нечему, и клавиша целиком принадлежит команде режима.
        /// </summary>
        private void LatchSlots(bool[] pressed, bool choosing)
        {
            bool abilitiesLive = Session.Mode != GameMode.Summary && Sim != null
                && CampPlayerView.Instance?.InventoryOpen != true;

            for (int i = 0; i < pressed.Length; i++)
            {
                if (!pressed[i]) continue;

                if (choosing)
                {
                    if (i < RiftRun.RewardChoices)
                        _commandLatch = (byte)((int)RunCommand.ChooseReward1 + i);
                }
                else if (abilitiesLive)
                {
                    if (TargetedSlot(i))
                    {
                        // Повторное нажатие той же клавиши снимает прицел —
                        // это и есть отмена без обращения к другой кнопке.
                        if (Sim.Tick >= Sim.AbilityReadyTick(i)) _targetAimSlot = _targetAimSlot == i ? -1 : i;
                    }
                    else
                    {
                        _targetAimSlot = -1;
                        _abilityLatch |= (byte)(1 << i);
                    }
                }
            }
        }

        /// <summary>
        /// Курсор экрана → точка на полу → Fix64.
        ///
        /// Луч, плоскость и Vector3 — это Unity, и именно поэтому вся эта
        /// арифметика живёт ЗДЕСЬ, а не в симуляции. Через границу проходит
        /// уже квантованная точка.
        ///
        /// ДВЕ КНОПКИ — ДВА НАМЕРЕНИЯ, И НИ ОДНО НЕ ЗАВИСИТ ОТ ТОГО, ЧТО ПОД
        /// КУРСОРОМ. ПКМ (<paramref name="moveHeld"/>) ведёт, ЛКМ
        /// (<paramref name="attackHeld"/>) бьёт. Раньше кнопка была одна, и
        /// намерение приходилось угадывать по силуэту под указателем: клик
        /// рядом с толпой означал атаку, чуть в сторону — движение, и игрок
        /// платил за промах мышью сменой действия.
        /// </summary>
        internal void CaptureAim(Vector2 screenPosition, bool moveHeld, bool movePressed,
            bool attackHeld)
        {
            byte flags = 0;
            if (_camera == null) _camera = Camera.main;
            // Курсор на собственном силуэте гасит ПРИКАЗ ИДТИ, но не удар.
            //
            // Правило появилось, чтобы ПКМ по своей же модели не отправляла
            // героя шагать за собственную спину. Пока кнопка была одна, гасить
            // приходилось весь кадр. Теперь это стоило бы атаки: в ближнем бою
            // курсор оказывается на герое постоянно, и кнопка молча пропускала
            // бы взмахи ровно там, где по ней и колотят.
            //
            // Направление удара при этом не берётся из-под курсора: оно уже
            // есть в Aim прошлого кадра, а симуляция доворачивает корпус сама.
            if (_targetAimSlot < 0 && PointerOverPlayer(screenPosition))
            {
                _pointerPressLatched = false;
                MoveOrderHeld = MoveOrderPressedThisFrame = false;
                HoveredEntity = -1;
                AttackHeld = attackHeld;
                _pending.Flags = attackHeld ? (byte)InputFlags.Attack : (byte)0;
                _pending.AttackTarget = -1;
                return;
            }

            if (_camera != null)
            {
                Ray ray = _camera.ScreenPointToRay(screenPosition);

                // Пол — плоскость y = 0. Симуляция плоская, высоты в ней нет.
                if (GroundPlane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    _pending.Aim = new FixVec2(QuantizePosition(hit.x), QuantizePosition(hit.z));
                }
            }

            // Наведение считается ПОСЛЕ обновления Aim. Раньше здесь читалась
            // точка прошлого кадра, поэтому быстрый клик рядом с силуэтом мог
            // назначить не того врага или превратиться в приказ по земле.
            HoveredEntity = FindUnderCursor(screenPosition);
            if (_targetAimSlot >= 0)
            {
                // Прицел держится, пока слот вообще требует выбора цели.
                // Проверка на конкретный Шаг по цепи отменяла прицел Броска
                // якоря в том же кадре, в котором он включался, и вторая
                // способность переставала срабатывать вовсе.
                bool valid = Sim != null && Sim.Entities.Alive[Simulation.PlayerId]
                    && TargetedSlot(_targetAimSlot);
#if ENABLE_INPUT_SYSTEM
                bool confirm = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
                bool confirm = Input.GetMouseButtonDown(0);
                bool cancel = Input.GetKeyDown(KeyCode.Escape);
#endif
                ResolveTargetAim(confirm, !valid || cancel || movePressed);
                _pointerPressLatched = false;
                _pending.Flags = 0;
                _pending.AttackTarget = -1;
                AttackHeld = MoveOrderHeld = MoveOrderPressedThisFrame = false;
                return;
            }
            HoveredEntity = FindUnderCursor(screenPosition);
            AttackHeld = attackHeld;
            MoveOrderHeld = moveHeld;
            MoveOrderPressedThisFrame = movePressed;

            // Флаги собираются независимо и могут стоять оба сразу: отходить,
            // продолжая махать, — обычное поведение, а не исключение.
            //
            // ЦЕЛЬ ПОД КУРСОРОМ — УТОЧНЕНИЕ, А НЕ УСЛОВИЕ АТАКИ. Курсор на
            // силуэте означает «бей вот этого»; курсор по пустому месту
            // означает «бей того, кто передо мной», и цель выбирает симуляция
            // тем же поиском ближайшего в лобовом секторе. Пустота больше не
            // отменяет удар — раньше отменяла, потому что кнопка была общая и
            // иначе любой промах мышью превращался бы в бег в толпу.
            if (moveHeld) flags |= (byte)InputFlags.MoveOrder;
            if (attackHeld) flags |= (byte)InputFlags.Attack;

            _pending.Flags = flags;
            _pending.AttackTarget = attackHeld && HoveredEntity >= 0 ? HoveredEntity : -1;

            if (movePressed)
            {
                // Защёлка держит ИМЕННО ЭТОТ кадр целиком, включая зажатую
                // атаку: ConsumeInput подменяет флаги целиком, и собранный
                // только из приказа идти кадр стирал бы удар за тот же тик.
                _pointerPressFrame = InputFrame.Empty;
                _pointerPressFrame.Aim = _pending.Aim;
                _pointerPressFrame.Flags = flags;
                _pointerPressFrame.AttackTarget = _pending.AttackTarget;
                _pointerPressLatched = true;
            }
        }

        /// <summary>
        /// Ставит законченный combat-slice «Вихря» в первый слот.
        /// Дерево «Печати пламени» временно не участвует: сейчас проверяется
        /// качество одного приёма, а не ширина набора способностей.
        /// </summary>
        /// <summary>
        /// Перечитывает набор способностей из лагеря.
        ///
        /// Нужен смене ветки: сам по себе набор применяется при смене
        /// симуляции, то есть на входе в забег, и без этого вызова игрок
        /// увидел бы новые кнопки только со следующего Разлома — а решение
        /// принято уже сейчас.
        /// </summary>
        public void RefreshAbilityBuild() => ApplyAbilityBuild();

        private void ApplyAbilityBuild()
        {
            Simulation sim = Sim;
            if (sim == null) return;

            int count = 0;
            if (NodeHotter) _nodeBuffer[count++] = AbilityDefinition.NodeHotter();
            if (NodeSplit) _nodeBuffer[count++] = AbilityDefinition.NodeSplit();
            if (NodeSpreads) _nodeBuffer[count++] = AbilityDefinition.NodeSpreads();

            // Порядок в буфере значения не имеет: AbilityBuild сортирует узлы
            // по возрастанию Id сам, иначе порядок галочек влиял бы на урон.
            //
            // ЧТО ЛЕЖИТ В СЛОТАХ, РЕШАЕТ ВЕТКА, А НЕ ЭТОТ ФАЙЛ. Раньше здесь
            // стоял жёсткий список, и он был смесью обеих веток сразу: Вихрь
            // саблей рядом с Ударом якорем. Набор — это правило игры, и живёт
            // оно в симуляции (PelagKit), иначе съёмка, тесты и живой запуск
            // разошлись бы в том, чем игрок вообще бьёт.
            //
            // Ветку выбирают в лагере; вне лагеря берётся сабельная как
            // стартовая — ею игрок знакомится с боем в начале Акта 1.
            CombatBranch branch = Session?.Camp != null ? Session.Camp.Branch : CombatBranch.Sabre;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                sim.SetAbility(slot, PelagKit.Definition(branch, slot), _nodeBuffer, 0);

            _appliedHotter = NodeHotter;
            _appliedSplit = NodeSplit;
            _appliedSpreads = NodeSpreads;
        }

        /// <summary>
        /// Кто под курсором. Радиус берётся из симуляции — тот же, по которому
        /// тела расталкиваются и рисуется кольцо. Наведение, столкновение
        /// и картинка обязаны считаться от одного числа.
        /// </summary>
        internal bool PointerOverPlayer(Vector2 screenPosition)
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null) return false;
            Transform body = null;
            if (Sim == null)
                body = CampPlayerView.Instance?.Body;
            else
            {
                var arena = FindAnyObjectByType<ArenaView>();
                if (arena != null) arena.TryGetEntityView(Simulation.PlayerId, out body);
            }
            if (body == null || !body.gameObject.activeInHierarchy) return false;
            // Персонаж перекрывает землю и интерактивные объекты за собой.
            // Используем объём тела, а не плоскость пола позади его головы.
            Ray ray = camera.ScreenPointToRay(screenPosition);
            var renderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            return renderer != null && renderer.enabled && renderer.bounds.IntersectRay(ray);
        }

        private int FindUnderCursor(Vector2 screenPosition)
        {
            Simulation sim = Sim;
            if (sim == null || _camera == null) return -1;

            EntityStore entities = sim.Entities;

            int best = -1;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                if (i == Simulation.PlayerId) continue;
                if (entities.Side[i] == entities.Side[Simulation.PlayerId]) continue;

                // Наведение идёт по экранному объёму всей фигуры, а не по
                // кругу на полу. Луч через торс изометрической модели попадает
                // на землю позади неё — именно поэтому прежняя проверка
                // пропускала голову, плечи и щит.
                FixVec2 p = entities.Position[i];
                Vector3 origin = new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());
                Vector3 feet = _camera.WorldToScreenPoint(origin + Vector3.up * 0.08f);
                Vector3 head = _camera.WorldToScreenPoint(origin + Vector3.up * 2.35f);
                Vector3 chest = _camera.WorldToScreenPoint(origin + Vector3.up * 1.15f);
                if (feet.z <= 0f || head.z <= 0f) continue;

                // Orvill со щитом шире физического body-radius. Ширина в
                // пикселях выводится из камеры, поэтому одинаково работает
                // при любом разрешении и orthographic size.
                Vector3 side = _camera.WorldToScreenPoint(
                    origin + Vector3.up * 1.15f + _camera.transform.right * 0.86f);
                float radiusPixels = Mathf.Max(18f,
                    Vector2.Distance(new Vector2(chest.x, chest.y), new Vector2(side.x, side.y)) + 5f);
                float distSq = DistanceSqToSegment(screenPosition,
                    new Vector2(feet.x, feet.y), new Vector2(head.x, head.y));
                float radiusSq = radiusPixels * radiusPixels;
                if (distSq > radiusSq) continue;

                float score = distSq / radiusSq + feet.z * 0.000001f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static float DistanceSqToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 0.0001f) return (point - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return (point - (a + ab * t)).sqrMagnitude;
        }

        private InputFrame ConsumeInput()
        {
            InputFrame frame = _pending;
            if (_pointerPressLatched)
            {
                frame.Aim = _pointerPressFrame.Aim;
                frame.Flags = _pointerPressFrame.Flags;
                frame.AttackTarget = _pointerPressFrame.AttackTarget;
                _pointerPressLatched = false;
            }

            if (_abilityPressLatched && _abilityLatch != 0)
            {
                frame.Aim = _abilityPressFrame.Aim;
                frame.Flags = _abilityPressFrame.Flags;
                frame.AttackTarget = _abilityPressFrame.AttackTarget;
                frame.AbilityTarget = _abilityPressFrame.AbilityTarget;
            }
            _abilityPressLatched = false;
            frame.AbilityMask = _abilityLatch;
            frame.Command = _commandLatch;
            _abilityLatch = 0;
            _commandLatch = 0;
            _pending.AbilityTarget = -1;
            return frame;
        }

        /// <summary>
        /// Подхватывает смену активной симуляции: вход в Разлом, выход на
        /// Полигон, возврат в лагерь.
        ///
        /// Способности ставятся заново каждый раз: они живут в симуляции,
        /// а симуляция у нового забега своя. Настоящее место способностей —
        /// персонаж в лагере, но дерева ещё нет, а список чекбоксов есть.
        /// </summary>
        private void SyncGeneration()
        {
            if (_shownGeneration == Session.Generation) return;
            _shownGeneration = Session.Generation;
            _pointerPressLatched = false;
            _pointerPressFrame = InputFrame.Empty;

            Simulation sim = Sim;
            if (sim == null) return;

            if (_prevPositions.Length < sim.Entities.Capacity)
                _prevPositions = new FixVec2[sim.Entities.Capacity];

            ApplyAbilityBuild();

            // События расстановки надо проиграть до первого шага:
            // Step очищает список событий в начале тика.
            PlayEvents(sim.Events);
            SavePreviousPositions();

            _accumulator = 0f;

            if (Session.Mode == GameMode.Rift)
                Debug.Log($"[Разлом] Забег {Session.RunNumber}, сид {Session.LastRunSeed}, " +
                          $"комнат {Run.Map.PlacedCount}");
        }

        private void OnDisable()
        {
            _pointerPressLatched = false;
            _pointerPressFrame = InputFrame.Empty;
        }

        /// <summary>
        /// КВАНТОВАНИЕ МИРОВОЙ КООРДИНАТЫ — та самая граница симуляции.
        /// Шаг 1/1024, границы — размер арены. Ни одно float-значение
        /// не пересекает эту черту неокруглённым.
        /// </summary>
        private static Fix64 QuantizePosition(float value)
        {
            const int Steps = 1024;
            const float Limit = 64f; // половина стороны арены, см. SpatialHash
            int q = Mathf.RoundToInt(Mathf.Clamp(value, -Limit, Limit) * Steps);
            return Fix64.Ratio(q, Steps);
        }

        /// <summary>
        /// Ловит скачки тела за один тик.
        ///
        /// «Оно телепортируется» — это наблюдение, а не диагноз: прыгать может
        /// и симуляция, и интерполяция отрисовки. Здесь сравнивается ровно то,
        /// что решил тик, с тем, что было тиком раньше. Если сторож молчит, а
        /// глаз прыжок видит — виновата отрисовка, и искать надо в ArenaView.
        ///
        /// Порог — три обычных шага. Меньше даёт ложные срабатывания на
        /// расталкивании, больше пропускает настоящий скачок.
        /// </summary>
        private void ReportTeleports(int tickBefore)
        {
            Simulation sim = Sim;
            if (sim == null) return;

            EntityStore entities = sim.Entities;
            for (int i = 0; i < entities.Count && i < _prevPositions.Length; i++)
            {
                if (!entities.Alive[i]) continue;

                FixVec2 delta = entities.Position[i] - _prevPositions[i];
                float moved = Mathf.Sqrt(
                    delta.X.ToFloat() * delta.X.ToFloat()
                    + delta.Y.ToFloat() * delta.Y.ToFloat());
                float step = entities.MoveStep[i].ToFloat();
                float limit = Mathf.Max(step * 3f, 0.35f);
                if (moved <= limit) continue;

                Debug.LogWarning($"[Разлом][скачок] тик {tickBefore}→{sim.Tick}, "
                                 + $"сущность {i} ({entities.Side[i]}): {moved:0.00} м "
                                 + $"при шаге {step:0.000} м, "
                                 + $"тяга={entities.ForcedTicksLeft[i]} "
                                 + $"вид={entities.ForcedKind[i]}");
            }
        }

        private void SavePreviousPositions()
        {
            Simulation sim = Sim;
            if (sim == null) return;

            for (int i = 0; i < sim.Entities.Count; i++)
            {
                _prevPositions[i] = sim.Entities.Position[i];
                _prevFacings[i] = sim.Entities.Facing[i];
            }
        }

        /// <summary>Позиция для отрисовки: между прошлым и текущим тиком.</summary>
        /// <summary>
        /// Скорость тела ИЗ СИМУЛЯЦИИ, метров в секунду.
        ///
        /// Нужна камере для компенсации отставания. Считать её разностью
        /// экранных позиций за кадр нельзя: в редакторе длительность кадра
        /// скачет, оценка шумит, и этот шум уходит прямо в положение камеры —
        /// герой начинает мелко дрожать в кадре, а мир вокруг него «плыть».
        ///
        /// Здесь значение точное и от частоты кадров не зависит вовсе.
        /// </summary>
        public Vector3 GetSimVelocity(int entityId)
        {
            if (Sim == null || (uint)entityId >= (uint)Sim.Entities.Count) return Vector3.zero;
            FixVec2 velocity = Sim.Entities.Velocity[entityId];
            // Скорость в симуляции задана за тик, а камере нужна за секунду.
            return new Vector3(velocity.X.ToFloat(), 0f, velocity.Y.ToFloat())
                   * Simulation.TicksPerSecond;
        }

        public Vector3 GetRenderPosition(int entityId)
        {
            FixVec2 prev = _prevPositions[entityId];
            FixVec2 curr = Sim.Entities.Position[entityId];
            float x = Mathf.Lerp(prev.X.ToFloat(), curr.X.ToFloat(), Alpha);
            float z = Mathf.Lerp(prev.Y.ToFloat(), curr.Y.ToFloat(), Alpha);
            float height = CampPlayerView.Instance?.Active == true ? CampPlayerView.Instance.GroundHeight : 0f;
            return new Vector3(x, height, z);
        }

        /// <summary>
        /// Направление для отрисовки: между прошлым и текущим тиком, ровно как
        /// позиция.
        ///
        /// Источник у поворота тот же самый — тик на 30 Гц, — и без этой
        /// интерполяции тело ехало плавно, а разворачивалось ступенями по
        /// двадцать градусов. При 120 кадрах это четыре одинаковых кадра и
        /// прыжок на пятом, и заметно это именно на том персонаже, на которого
        /// игрок смотрит.
        ///
        /// Это НЕ сглаживание и НЕ задержка: возвращается точка между двумя
        /// авторитетными значениями, как и у позиции. Симуляция по-прежнему
        /// решает бой по мгновенному `Entities.Facing`.
        /// </summary>
        public Vector3 GetRenderFacing(int entityId)
        {
            FixVec2 curr = Sim.Entities.Facing[entityId];
            Vector3 current = new Vector3(curr.X.ToFloat(), 0f, curr.Y.ToFloat());
            if (current.sqrMagnitude < 0.000001f) return Vector3.zero;
            current.Normalize();

            if (Sim.Entities.ForcedKind[entityId] == (byte)ForcedMotionKind.Roll) return current;

            FixVec2 prev = _prevFacings[entityId];
            Vector3 previous = new Vector3(prev.X.ToFloat(), 0f, prev.Y.ToFloat());
            if (previous.sqrMagnitude < 0.000001f) return current;

            return Vector3.Slerp(previous.normalized, current, Alpha).normalized;
        }

        private void PlayEvents(IReadOnlyList<SimEvent> events)
        {
            Simulation sim = Sim;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];

                // Копия в буфер кадра: список симуляции переживёт только этот тик.
                _frameEvents.Add(e);

                int sourceForcedTicksLeft = 0;
                byte sourceForcedKind = (byte)ForcedMotionKind.None;
                if (e.Source >= 0 && e.Source < sim.Entities.Count)
                {
                    sourceForcedTicksLeft = sim.Entities.ForcedTicksLeft[e.Source];
                    sourceForcedKind = sim.Entities.ForcedKind[e.Source];
                }
                _frameEventContexts.Add(new FrameEventContext(e, sim.Tick,
                    sourceForcedTicksLeft, sourceForcedKind));

                switch (e.Type)
                {
                    case SimEventType.Attack:
                        // ArenaView запускает presentation-анимацию. Урон по-прежнему
                        // полностью рассчитывается детерминированной симуляцией.
                        break;
                    case SimEventType.Damage:
                        // Цифру рисует DamageNumbers по FrameEvents.
                        // TODO: вспышка попадания, звук.
                        break;
                    case SimEventType.Death:
                        // Труп прячет ArenaView по Alive.
                        // TODO: анимация смерти, дроп лута.
                        break;
                    case SimEventType.Spawn:
                        // Объект из пула привязывает ArenaView по индексу сущности.
                        break;
                }
            }

            if (LogStateHash && sim != null && sim.Tick % Simulation.TicksPerSecond == 0)
                Debug.Log($"[Разлом] тик {sim.Tick} хеш {sim.StateHash():X16}");
        }
    }
}
