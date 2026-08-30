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
    /// Мост между Unity и симуляцией. Единственное место, где встречаются
    /// Time.deltaTime и Fix64.
    ///
    /// ПРАВИЛО БЕЗ ИСКЛЮЧЕНИЙ: представление никогда не пишет в симуляцию напрямую.
    /// Ввод собирается на кадре, квантуется в Fix64, буферизуется и применяется
    /// на границе тика. Анимация не решает, когда наносится урон — урон наносится
    /// на тике, анимация лишь показывает это.
    /// </summary>
    public sealed class TickDriver : MonoBehaviour
    {
        [Header("Сессия")]
        [Tooltip("0 — сгенерировать сид из текущего времени при старте.")]
        public ulong RunSeed = 0;

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
        /// События всех тиков, случившихся на этом кадре, в порядке возникновения.
        ///
        /// Sim.Events живёт ровно один тик — Step очищает список в начале шага,
        /// а на просевшем кадре шагов бывает несколько. Читать Sim.Events напрямую
        /// значит терять всё, кроме последнего тика. Потребители (цифры урона,
        /// VFX, звук) читают это в LateUpdate, когда кадр уже отшагал.
        /// </summary>
        public IReadOnlyList<SimEvent> FrameEvents => _frameEvents;

        private readonly List<SimEvent> _frameEvents = new List<SimEvent>(256);

        /// <summary>
        /// Под какую самую большую симуляцию рассчитан буфер интерполяции.
        /// Совпадает с ёмкостью по умолчанию у Simulation — Полигон меньше,
        /// и меньшая просто не займёт весь буфер.
        /// </summary>
        public const int MaxSimCapacity = 512;

        private const float TickLength = 1f / Simulation.TicksPerSecond;
        private float _accumulator;

        private InputFrame _pending = InputFrame.Empty;
        private byte _abilityLatch;
        private byte _commandLatch;

        /// <summary>Поколение, под которое уже настроено представление.</summary>
        private int _shownGeneration = -1;

        // Камера кэшируется: Camera.main ищет объект по тегу, и делать это
        // каждый кадр незачем.
        private Camera _camera;
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        // Буфер узлов выделен один раз: пересборка билда не должна мусорить.
        private readonly AbilityNode[] _nodeBuffer = new AbilityNode[3];
        private bool _appliedHotter, _appliedSplit, _appliedSpreads;

        // Позиции на предыдущем тике — нужны, чтобы интерполировать отрисовку.
        private FixVec2[] _prevPositions = new FixVec2[0];

        private void Awake()
        {
            ulong seed = RunSeed != 0 ? RunSeed : (ulong)System.DateTime.UtcNow.Ticks;
            RunSeed = seed;

            // Игра начинается в ЛАГЕРЕ, а не в Разломе. Забег теперь то, во что
            // входят, а не то, что запускается вместо главного меню.
            Session = PrototypeContent.NewSession(seed);
            // В редакторе combat slice должен запускаться тем же 1+3 стендом,
            // который мы снимаем. Иначе владелец видит старый Полигон с двумя
            // болванками и закономерно не может проверить текущую работу.
            Session.WhirlwindShowcase = CaptureRig.WhirlwindShowcase || Application.isEditor;

            // Буфер выделяется с запасом под самую большую из симуляций сессии.
            // Иначе сбой вылезал бы кадром позже и в другом месте — в интерполяции
            // отрисовки, к настоящей причине отношения не имеющей.
            _prevPositions = new FixVec2[MaxSimCapacity];

            Debug.Log($"[Разлом] Лагерь. Сид сессии {seed}. E — войти в Разлом, T — Полигон.");
        }

        private void Update()
        {
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
            _accumulator += Time.deltaTime;

            int steps = 0;
            while (_accumulator >= TickLength && steps < MaxTicksPerFrame)
            {
                if (Sim != null) SavePreviousPositions();

                InputFrame frame = ConsumeInput();

                // Шагает СЕССИЯ, а не забег и тем более не симуляция: что
                // именно шагает, решает режим. В лагере не идёт даже время боя.
                int tickBefore = Sim != null ? Sim.Tick : 0;
                int depthBefore = Run != null ? Run.Depth : 0;

                Session.Step(in frame);

                if (_shownGeneration != Session.Generation) SyncGeneration();
                else if (Sim != null && Sim.Tick != tickBefore) PlayEvents(Sim.Events);

                // Новый Разлом — интерполировать не от чего: старые позиции
                // относятся к другой локации, и кадр показал бы, как все
                // размазываются через полкарты.
                if (Sim != null && Run != null && Run.Depth != depthBefore) SavePreviousPositions();

                _accumulator -= TickLength;
                steps++;
            }

            // Если кадр просел настолько, что накопилось больше MaxTicksPerFrame шагов,
            // излишек отбрасывается: лучше замедлить время, чем словить спираль смерти.
            if (steps >= MaxTicksPerFrame) _accumulator = 0f;

            // Смена симуляции посреди кадра обнуляет накопитель, а цикл после
            // этого успевает вычесть из него длину тика. Отрицательный
            // накопитель дал бы отрицательную Alpha на следующем кадре.
            if (_accumulator < 0f) _accumulator = 0f;

            Alpha = Mathf.Clamp01(_accumulator / TickLength);
        }

        /// <summary>
        /// Ввод собирается на частоте кадра и КВАНТУЕТСЯ В Fix64 ЗДЕСЬ.
        /// Ни одно float-значение не должно пересечь границу симуляции.
        ///
        /// Обе ветки читают ОДНО И ТО ЖЕ: точку под курсором, правую кнопку
        /// мыши и четыре цифры. Какая система ввода включена в настройках
        /// проекта — вопрос сборки, и на поведение симуляции влиять не должен.
        /// </summary>
        private void CaptureInput()
        {
            // На экране награды цифры означают ВЫБОР, а не способность.
            // Одни и те же клавиши: у игрока не должно быть двух рядов цифр,
            // а бой на этом экране всё равно стоит.
            bool choosing = Session.Mode == GameMode.Rift
                            && Run != null && Run.Phase == RunPhase.ChoosingReward;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                bool[] digits =
                {
                    kb.digit1Key.wasPressedThisFrame, kb.digit2Key.wasPressedThisFrame,
                    kb.digit3Key.wasPressedThisFrame, kb.digit4Key.wasPressedThisFrame,
                };

                LatchDigits(digits, choosing);

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
                mouse != null && mouse.rightButton.isPressed);
#else
            bool[] legacyDigits =
            {
                Input.GetKeyDown(KeyCode.Alpha1), Input.GetKeyDown(KeyCode.Alpha2),
                Input.GetKeyDown(KeyCode.Alpha3), Input.GetKeyDown(KeyCode.Alpha4),
            };

            LatchDigits(legacyDigits, choosing);

            LatchKeys(
                leave: Input.GetKeyDown(KeyCode.L),
                // На время combat-slice Play Mode сразу открывает тот же Rift
                // 1+3, который проходит capture; старый Полигон здесь больше
                // не должен маскироваться под проверку Вихря.
                enter: Input.GetKeyDown(KeyCode.E) || CaptureRig.AutoEnterRift || Application.isEditor,
                repeat: Input.GetKeyDown(KeyCode.R),
                back: Input.GetKeyDown(KeyCode.C),
                ground: Input.GetKeyDown(KeyCode.T),
                salvage: Input.GetKeyDown(KeyCode.V));

            CaptureAim(Input.mousePosition, Input.GetMouseButton(1));
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

            if (!choosing && CaptureRig.ShouldCastWhirlwind(Sim != null ? Sim.Tick : -1))
                _abilityLatch |= 1;
        }

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
                    if (enter) _commandLatch = (byte)CampCommand.EnterRift;
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
        /// </summary>
        private void LatchDigits(bool[] pressed, bool choosing)
        {
            for (int i = 0; i < pressed.Length; i++)
            {
                if (!pressed[i]) continue;

                if (choosing)
                {
                    if (i < RiftRun.RewardChoices)
                        _commandLatch = (byte)((int)RunCommand.ChooseReward1 + i);
                }
                else
                {
                    _abilityLatch |= (byte)(1 << i);
                }
            }
        }

        /// <summary>
        /// Курсор экрана → точка на полу → Fix64.
        ///
        /// Луч, плоскость и Vector3 — это Unity, и именно поэтому вся эта
        /// арифметика живёт ЗДЕСЬ, а не в симуляции. Через границу проходит
        /// уже квантованная точка.
        /// </summary>
        private void CaptureAim(Vector2 screenPosition, bool held)
        {
            AttackHeld = held;
            HoveredEntity = FindUnderCursor();
            byte flags = 0;

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

            // Одна кнопка на всё, как в жанре: пока ПКМ зажата, персонаж идёт
            // к курсору и бьёт то, что оказалось перед ним. Отпустил — бить
            // перестал, но ДОШЁЛ: приказ на движение живёт в симуляции и
            // переживает отпущенную кнопку.
            if (held) flags |= (byte)(InputFlags.MoveOrder | InputFlags.Attack);
            _pending.Flags = flags;
        }

        /// <summary>
        /// Ставит законченный combat-slice «Вихря» в первый слот.
        /// Дерево «Печати пламени» временно не участвует: сейчас проверяется
        /// качество одного приёма, а не ширина набора способностей.
        /// </summary>
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
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), _nodeBuffer, 0);

            _appliedHotter = NodeHotter;
            _appliedSplit = NodeSplit;
            _appliedSpreads = NodeSpreads;
        }

        /// <summary>
        /// Кто под курсором. Радиус берётся из симуляции — тот же, по которому
        /// тела расталкиваются и рисуется кольцо. Наведение, столкновение
        /// и картинка обязаны считаться от одного числа.
        /// </summary>
        private int FindUnderCursor()
        {
            Simulation sim = Sim;
            if (sim == null) return -1;

            EntityStore entities = sim.Entities;
            FixVec2 cursor = _pending.Aim;

            int best = -1;
            Fix64 bestDistSq = Fix64.MaxValue;

            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                if (i == Simulation.PlayerId) continue;
                if (entities.Side[i] == entities.Side[Simulation.PlayerId]) continue;

                Fix64 radius = entities.BodyRadius[i];
                Fix64 distSq = FixVec2.DistanceSq(cursor, entities.Position[i]);
                if (distSq > radius * radius) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }
            return best;
        }

        private InputFrame ConsumeInput()
        {
            _pending.AbilityMask = _abilityLatch;
            _pending.Command = _commandLatch;

            // Цель уезжает в симуляцию только вместе с приказом бить: водить
            // курсором по врагам, не нажимая, не должно ничего назначать.
            _pending.AttackTarget = AttackHeld ? HoveredEntity : -1;
            _abilityLatch = 0;
            _commandLatch = 0;
            return _pending;
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

        private void SavePreviousPositions()
        {
            Simulation sim = Sim;
            if (sim == null) return;

            for (int i = 0; i < sim.Entities.Count; i++)
                _prevPositions[i] = sim.Entities.Position[i];
        }

        /// <summary>Позиция для отрисовки: между прошлым и текущим тиком.</summary>
        public Vector3 GetRenderPosition(int entityId)
        {
            FixVec2 prev = _prevPositions[entityId];
            FixVec2 curr = Sim.Entities.Position[entityId];
            float x = Mathf.Lerp(prev.X.ToFloat(), curr.X.ToFloat(), Alpha);
            float z = Mathf.Lerp(prev.Y.ToFloat(), curr.Y.ToFloat(), Alpha);
            return new Vector3(x, 0f, z);
        }

        private void PlayEvents(IReadOnlyList<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];

                // Копия в буфер кадра: список симуляции переживёт только этот тик.
                _frameEvents.Add(e);

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

            Simulation sim = Sim;
            if (LogStateHash && sim != null && sim.Tick % Simulation.TicksPerSecond == 0)
                Debug.Log($"[Разлом] тик {sim.Tick} хеш {sim.StateHash():X16}");
        }
    }
}
