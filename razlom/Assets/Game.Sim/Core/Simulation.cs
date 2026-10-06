using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>Capture-only intensity tier for the deterministic combat-feel stand.</summary>
    public enum CombatFeelCaptureTier : byte
    {
        None = 0,
        Normal = 1,
        Critical = 2,
        Kill = 3,
    }

    /// <summary>
    /// Ядро симуляции. Ничего не знает про Unity: сборка Game.Sim собрана
    /// с noEngineReferences, поэтому обращение к Time, Random или transform
    /// не скомпилируется. Детерминизм здесь — свойство сборки, а не дисциплины.
    ///
    /// Один вызов Tick — ровно один шаг фиксированной длительности.
    /// Представление читает Events и Entities и интерполирует между тиками.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- параметры тика ----
        // Длина тика нигде в логике не используется: все скорости и задержки
        // заданы В ТИКАХ. Поэтому переход на 60 Гц — правка одной константы.
        public const int TicksPerSecond = 30;

        // Геройский контакт и темп ускорены вместе с клипами A/B.
        // Вражеский телеграф остаётся отдельным окном на реакцию.
        public const int AttackWindupTicks = 9;
        public const int PlayerBaseAttackCycleTicks = 20;

        /// <summary>
        /// От начала замаха до контакта у Лесного хранителя: 28 тиков ≈ 0,93 с.
        /// Число живёт в GuardianSwingWindupTicks (Simulation.EnemyMelee).
        /// </summary>
        // Не трогать заодно с геройской цифрой. Это единственное окно, в
        // которое игрок видит занесённый удар и может уйти; укоротишь — и
        // враги начнут бить без предупреждения, а бой станет нечестным
        // ровно в том смысле, который игрок чувствует, но не формулирует.
        //
        // Было 12, и моб при этом доворачивался и шёл: уйти можно было только
        // угадав. Теперь направление фиксируется, на земле заполняется сектор,
        // а удар бьёт ровно по нарисованному — см. Simulation.EnemyMelee.
        public const int EnemyAttackWindupTicks = GuardianSwingWindupTicks;

        // Здоровье и урон Корнеполза — в EnemyArchetypes, как у всех видов.
        // 9 тиков читались только как дёрганье. 12 — ещё поза без метки на
        // земле, но уже поза, которую успеваешь увидеть.
        //
        // Цикл 24 → 30 (стенд баланса, 26.09): укус без метки не уворачивается,
        // и рой делал 30–70% всего урона по герою. Реже кусает — и вместе с
        // жетоном укуса (SwarmBiteTokenLimit) толпа перестаёт жевать героя разом.
        public const int RootSwarmAttackWindupTicks = 12;
        public const int RootSwarmAttackCooldownTicks = 30;
        public static readonly Fix64 RootSwarmMoveSpeed = Fix64.Ratio(34, 10);
        public static readonly Fix64 RootSwarmRushSpeed = Fix64.FromInt(5);
        private static readonly Fix64 RootSwarmAttackRange = Fix64.Ratio(14, 10);

        // У моба окно и дистанция подхода — из профиля его замаха
        // (Simulation.EnemyMelee): Хранитель 28 тиков и 2 м, Корнеполз 12 и 1,4.
        private int WindupTicksFor(int entityId)
            => entityId == PlayerId ? PlayerAttackWindupTicks
                : MeleeProfileOf(Entities.Kind[entityId]).WindupTicks;

        private Fix64 AttackRangeFor(int entityId)
            => entityId == PlayerId ? PlayerAttackRange
                : MeleeProfileOf(Entities.Kind[entityId]).ChaseRange;

        /// <summary>
        /// Во время активного действия герой сохраняет управление, но идёт
        /// вдвое медленнее. 24 тика = 0.8 с — длина presentation-фазы
        /// основных способностей Pelag, а не их кулдаун.
        /// </summary>
        public const int AbilityMovePenaltyTicks = 24;

        // ---- баланс прототипа (потом уедет в таблицы) ----

        // Мёртвая зона приказа на движение — примерно радиус тела персонажа.
        // Клик внутри неё разворачивает, но не сдвигает: так в жанре сделан
        // разворот на месте, и отдельной кнопки для него не нужно.
        //
        // Заодно это и порог прибытия: «дойти до точки» значит встать в неё
        // телом, а не совместить с ней математический центр. Радиус мал
        // настолько, что недоход глазом не читается — метр читался бы.
        public static readonly Fix64 TurnInPlaceRadius = Fix64.Ratio(1, 2);
        private static readonly Fix64 TurnInPlaceRadiusSq = TurnInPlaceRadius * TurnInPlaceRadius;

        // Скорость разворота ЗАДАНА В ТИКАХ, как и всё остальное: полный оборот
        // за 0.6 секунды, то есть 20° за тик на 30 Гц. Старый полный оборот за
        // секунду ощущался как задержка между приказом и ответом персонажа.
        // Синус и косинус шага считаются
        // один раз при загрузке класса: в самом тике тригонометрии нет.
        private static readonly Fix64 PlayerTurnStep = Fix64.TwoPi / 9;
        private static readonly Fix64 EnemyTurnStep = Fix64.TwoPi / TicksPerSecond;
        private static readonly Fix64 PlayerTurnStepCos = Fix64.Cos(PlayerTurnStep);
        private static readonly Fix64 PlayerTurnStepSin = Fix64.Sin(PlayerTurnStep);
        private static readonly Fix64 EnemyTurnStepCos = Fix64.Cos(EnemyTurnStep);
        private static readonly Fix64 EnemyTurnStepSin = Fix64.Sin(EnemyTurnStep);
        private static readonly Fix64 AttackRange  = Fix64.FromInt(2);
        private static readonly Fix64 AttackRangeSq = AttackRange * AttackRange;

        /// <summary>
        /// Дальность автоатаки ГЕРОЯ. Шире, чем у мобов, по решению владельца
        /// от 11 сентября: сабля должна доставать чуть раньше, чем игрок
        /// упирается в тело.
        ///
        /// ОТДЕЛЬНАЯ КОНСТАНТА, А НЕ ПОДНЯТЫЙ AttackRange. Одно число обслуживало
        /// и героя, и мобов, поэтому «шире игроку» молча удлиняло бы и удар
        /// врага. У врага дальность — часть телеграфа, на который игрок
        /// реагирует, и трогать её без отдельного решения нельзя.
        /// </summary>
        private static readonly Fix64 PlayerAttackRange = Fix64.Ratio(5, 2);

        // Прежний поиск цели героя: сектор 120°. С 01.10 герой бьёт сектором
        // серии (Simulation.SabreCombo), а это число живёт только в общих
        // ветках поиска ближайшего, которые теперь спрашивают о мобах.
        private static readonly Fix64 AttackArcCos = Fix64.Ratio(1, 2);

        // Цель можно выбрать в широком секторе 120°, но сам взмах начинается
        // только когда корпус уже почти смотрит на неё. 0.8 = примерно ±37°.
        // Раньше широкий сектор одновременно был и порогом старта: клип мог
        // начаться боком, а затем Damage проверял уже другое направление.
        private static readonly Fix64 AttackCommitCos = Fix64.Ratio(4, 5);

        // У Корнеполза старт мягче, чем у игрока: после расталкивания он
        // часто стоит боком, а в начале замаха всё равно разворачивается к
        // цели целиком. ±120° не дают ему зависнуть, но и не превращают
        // укус в круг вокруг тела. Хранитель стартует строже — см.
        // GuardianSwingCommitCos: его сектор нарисован на земле.
        private static readonly Fix64 EnemyAttackArcCos = Fix64.Ratio(-1, 2);

        // Прежние подход к цели по клику, тяжёлый второй удар и автопереход
        // к соседу сняты 01.10 вместе со старой автоатакой: серия бьёт
        // сектором туда, куда показывает игрок, и сама к цели не ходит.
        private static readonly Fix64 AbilityMoveScale = Fix64.Ratio(3, 4);
        private const int WhirlwindKillCooldownRefundTicks = 7;

        // ---- ИИ врага: обнаружение ----
        //
        // Раньше враг бежал к игроку с первого тика существования, по прямой,
        // независимо от расстояния — весь Разлом срывался с места одним кадром,
        // едва игрок появлялся на карте. Теперь погоня начинается только после
        // обнаружения: игрок должен подойти на EnemyDetectRange, а сама реакция
        // ещё и не мгновенна — см. UpdateAggro.
        private static readonly Fix64 EnemyDetectRange = Fix64.FromInt(7);
        private static readonly Fix64 EnemyDetectRangeSq = EnemyDetectRange * EnemyDetectRange;

        // Случайная пауза между входом в радиус обнаружения и стартом погони.
        // Не ноль: враги в одной комнате, замеченные игроком одновременно, не
        // должны срываться с места одним и тем же тиком — это читается строем
        // роботов, а не толпой существ. 5–15 тиков — это 0.17–0.5 секунды на 30 Гц.
        private const int EnemyNoticeMinTicks = 5;
        private const int EnemyNoticeMaxTicks = 15;

        // ---- базы статов прототипа (потом уедут в таблицы народа и класса) ----
        //
        // Числа те же, что были боевыми константами до подключения статов.
        // Поменялась не величина, а место: бой больше не читает ни одного
        // боевого числа отсюда — он читает лист статов сущности, и поэтому
        // надетый предмет меняет удар так же, как узел дерева или пассивка.
        private const int PlayerBaseHealth = 1000;

        // Здоровье мишеней тестовой арены и стенда темпа — не баланс вида.
        // Настоящее здоровье видов — в EnemyArchetypes, его ставит расстановка.
        private const int EnemyBaseHealth  = 100;

        // Щит и широкий силуэт требуют больше воздуха, чем прежняя техническая
        // капсула. Названо константой: расстановка в SetupRift отступает от
        // стен на этот же радиус, а не только ConfigureEnemy.
        private static readonly Fix64 EnemyBodyRadius = Fix64.Ratio(62, 100);

        // Отступ от стен модуля сверх тела врага при случайной расстановке.
        // Без него силуэт мог бы встать вплотную к границе и наполовину
        // уйти за неё зрительно, даже пройдя ClampToWalkable.
        private static readonly Fix64 EnemySpawnWallMargin = Fix64.One;

        private static readonly Fix64 PlayerBaseDamage = Fix64.FromInt(34);

        // Скорость атаки — В АТАКАХ В СЕКУНДУ: только в этих единицах «+20%»
        // на предмете значит то, что игрок прочитает. В тики её переводит
        // CombatStats.AttackCooldownTicks, и делает это в единственном месте.
        // У героя это удары серии сабли: три в секунду (Simulation.SabreCombo,
        // 01.10). У Хранителя цикл 65 тиков: замах, окно для наказания и
        // свободная пауза (см. Simulation.EnemyMelee).
        private static readonly Fix64 PlayerBaseAttackSpeed = Fix64.FromInt(SabreHitsPerSecond);
        private static readonly Fix64 EnemyBaseAttackSpeed  = Fix64.Ratio(TicksPerSecond, GuardianSwingCycleTicks);

        // Скорость движения — в метрах в секунду; шаг за тик считает CombatStats.
        // 6 м/с было быстрее естественной подачи текущего authored-run и
        // неизбежно тащило опорную стопу по полу. 4.5 м/с оставляет игрока
        // быстрее толпы, но совпадает с читаемым длинным беговым шагом.
        public static readonly Fix64 PlayerBaseMoveSpeed = Fix64.Ratio(9, 2);

        /// <summary>
        /// Скорость хода врага. Было 3.5, стало 3.1 — на 11.4% медленнее по
        /// просьбе владельца: толпа шла слишком бойко и налетала на игрока
        /// раньше, чем её успевали прочитать.
        ///
        /// ПУБЛИЧНАЯ НЕ СЛУЧАЙНО. Темп ног в CharacterAnimatorView считается из
        /// этого числа: клип бега «едет» свои метры в секунду, и во сколько раз
        /// его крутить, зависит от того, с какой скоростью едет тело. Пока
        /// число было приватным, оно жило в представлении копией — и копия
        /// разъехалась с оригиналом ровно тогда, когда оригинал поменяли.
        /// </summary>
        public static readonly Fix64 EnemyBaseMoveSpeed = Fix64.Ratio(31, 10);

        /// <summary>
        /// Ход Лесного хранителя (и моба без вида — он живёт по правилам
        /// Хранителя; временный босс — тоже Хранитель): 3,1 → 2,8 м/с, на 10%
        /// медленнее общей скорости врага (ревью владельца 01.10: «скорость
        /// передвижения немного замедлить»). Остальные виды ходят своими
        /// числами и не задеты.
        ///
        /// ПУБЛИЧНАЯ ПО ТОЙ ЖЕ ПРИЧИНЕ, что и EnemyBaseMoveSpeed: темп ног
        /// Хранителя в CharacterAnimatorView делится из этого числа.
        /// </summary>
        public static readonly Fix64 GuardianMoveSpeed = Fix64.Ratio(28, 10);

        /// <summary>
        /// Насколько далеко тело может быть отодвинуто чужими телами за один тик.
        /// Примерно четверть шага: расталкивание должно быть заметно медленнее
        /// собственного хода, иначе толпа возит игрока по арене.
        /// </summary>
        public void SetupRift(LayoutMap map, ulong spawnSeed, int enemiesPerRoom, int enemyHealth)
        {
            SetupRift(map, spawnSeed, enemiesPerRoom, enemiesPerRoom, enemyHealth);
        }

        /// <summary>
        /// За сколько тиков тело набирает полную скорость. Торможение хранится
        /// отдельно: четыре тика дают точке мягкий, но не скользкий подъезд.
        ///
        /// ЗАЧЕМ. Мгновенный разгон читается не как быстрота, а как отсутствие
        /// тела: фишка, переставленная по доске. Задержка в одну шестую секунды
        /// на клик уже чувствовалась как вязкость. 100 мс сохраняют массу, но
        /// возвращают непосредственный ответ на приказ.
        /// </summary>
        private const int AccelerationTicks = 3;

        /// <summary>
        /// Подъезд к точке. Желаемая скорость у цели ограничивается так, чтобы
        /// встать без проскока: иначе персонаж пролетал бы точку приказа и
        /// возвращался, а это читается как непослушание.
        /// </summary>
        private const int BrakeTicks = 4;

        private static readonly Fix64 MaxSeparationStep = Fix64.Ratio(5, 100);

        /// <summary>
        /// Доля скорости на обход занятого места. Две трети: обходящий заметно
        /// медленнее набегающего, иначе кольцо крутится каруселью.
        /// </summary>
        private static readonly Fix64 CircleAroundScale = Fix64.Ratio(2, 3);

        /// <summary>
        /// Где начинается подъезд к дистанции удара. Полтора радиуса удара:
        /// полоса торможения шириной в полрадиуса — это заметно больше
        /// MaxSeparationStep, значит выпихнутое расталкиванием тело всегда
        /// оказывается внутри полосы, а не за ней.
        /// </summary>
        private static readonly Fix64 ApproachBrakeRange = AttackRange * Fix64.Ratio(3, 2);
        private static readonly Fix64 ApproachBrakeRangeSq =
            ApproachBrakeRange * ApproachBrakeRange;

        /// <summary>Доли бокового и лобового хода при обходе занятого места.</summary>
        private static readonly Fix64 ArcSideShare = Fix64.Ratio(8, 10);
        private static readonly Fix64 ArcForwardShare = Fix64.Ratio(55, 100);

        /// <summary>
        /// Вес игрока при расталкивании. Он тяжелее толпы вчетверо с лишним:
        /// сорок тел не должны сдвигать того, кто ими управляет.
        /// </summary>
        private static readonly Fix64 PlayerPushWeight = Fix64.Ratio(22, 100);

        private static readonly Fix64 BaseCritChance     = Fix64.Ratio(15, 100);
        private static readonly Fix64 BaseCritMultiplier = Fix64.FromInt(2);

        /// <summary>
        /// Дальность и раствор автоатаки — ТОЛЬКО ДЛЯ ОТРИСОВКИ опознавателей.
        /// Бой читает поля напрямую; эти свойства существуют затем, чтобы
        /// нарисованный на полу сектор не разъехался с настоящим.
        /// </summary>
        public static Fix64 AutoAttackRange => SabreReach;
        public static Fix64 AutoAttackArcCos => SabreArcCos;

        public const int PlayerId = 0;

        /// <summary>Сколько способностей на панели. Ровно четыре, см. архитектуру.</summary>
        /// <summary>
        /// Сколько активных кнопок способностей у героя.
        ///
        /// ПЯТЬ, А НЕ ЧЕТЫРЕ, из-за якорной ветки: у неё четыре основные
        /// способности плюс отдельный базовый рывок, который не открывается
        /// талантом и не заменяет ни одну из четырёх. У сабельной ветки пятый
        /// слот пуст — её мобильность встроена в Выпад, и выдавать ей рывок
        /// «за компанию» значило бы стереть разницу между ветками.
        ///
        /// Пустой слот — это null в _abilityBuilds, а не способность-пустышка:
        /// пустышка попала бы в интерфейс, в звук и в кулдауны.
        /// </summary>
        public const int AbilitySlots = 5;

        public readonly EntityStore Entities;
        public readonly RngStreams Rng;
        public readonly SpatialHash Grid;
        public readonly StatusStore Statuses;

        /// <summary>
        /// Билды способностей игрока по слотам. Пересобираются при смене узлов
        /// дерева, а не каждый каст.
        /// </summary>
        private readonly AbilityBuild[] _abilityBuilds = new AbilityBuild[AbilitySlots];
        private readonly int[] _abilityReadyTick = new int[AbilitySlots];

        // Presentation starts at Cast, gameplay contact stays deterministic.
        public const int WhirlwindContactDelayTicks = 10;
        private int _whirlwindImpactTick = -1;
        private int _whirlwindImpactSlot = -1;

        // Абордаж (прежний бросок якоря в точку с замахом 15 и тягой 17) — с 02.10
        // Simulation.Abordage: цель — враг под курсором, как у Шквала.
        private int _abilityMovePenaltyUntilTick;

        // Состояние «Шага по цепи» между прыжками. Живёт в симуляции, а не в
        // способности: способность кончилась в момент каста, а цепочка идёт
        // ещё двадцать тиков. Входит в хеш — иначе реплей разъедется.
        private int _chainHopsLeft;
        private int _chainTarget = -1;
        public int ChainTargetId => _chainHopsLeft > 0 ? _chainTarget : -1;
        private int _chainSlot = -1;
        // +1 под талант «Пять прыжков», и лишние прыжки Охоты (Simulation.Squall).
        private readonly int[] _chainVisited = new int[AnchorKit.ChainMaxHops + 1 + HuntBonusHopsMax];
        private int _chainVisitedCount;

        /// <summary>
        /// Общий буфер радиусных запросов. Выделен один раз: за забег таких
        /// запросов десятки тысяч, и ни один не должен стоить аллокации.
        /// </summary>
        public readonly int[] HitScratch;

        /// <summary>
        /// Буферы расталкивания. Свои, а не общий HitScratch: расталкивание
        /// идёт до боя, и делить с ним буфер значит однажды получить
        /// расхождение, которое ищут неделю.
        /// </summary>
        private readonly int[] _separationScratch;

        // Свой буфер, а не общий с расталкиванием: обе выборки живут в одном
        // тике, и делить один массив между ними значит однажды поймать баг,
        // который воспроизводится раз в сто забегов.
        private readonly int[] _crowdScratch;
        private readonly FixVec2[] _separationPush;

        /// <summary>
        /// Кто в этом тике держит своё место сам и не расталкивается (Вендиго
        /// в прыжке, кабан в действии, Расщепень в перекате). Считается раз на
        /// тик в начале SeparateBodies — не состояние, в хеш не идёт.
        /// </summary>
        private readonly bool[] _separationExempt;

        private readonly List<SimEvent> _events = new List<SimEvent>(256);
        private LayoutMap _layout;
        private bool _navigationWaypoint;

        /// <summary>
        /// Точку надо ПРОЙТИ НАСКВОЗЬ, а не встать в ней.
        ///
        /// Третий режим появился не от любви к режимам, а потому что двух не
        /// хватало ни одному промежуточному углу маршрута. Обычный приказ имеет
        /// мёртвую зону в полметра: цель ближе неё считается достигнутой, и на
        /// углу тело просто вставало в тридцати сантиметрах от него. Режим
        /// прибытия мёртвой зоны не имеет, но режет скорость до четверти
        /// остатка пути — тело подползало к каждому повороту.
        ///
        /// Транзит — это ни то, ни другое: полная скорость и никакого порога
        /// прибытия. Тормозить перед углом незачем, за ним дорога продолжается.
        /// </summary>
        private bool _navigationTransit;
        private CampWalkMap _campWalkMap;

        public void SetupCamp(FixVec2 spawn, CampWalkMap map)
        {
            SetupTestArena(0);
            _campWalkMap = map;
            Entities.Position[PlayerId] = spawn;
        }

        public void StopPlayerMovement()
        {
            _navigationWaypoint = false;
            _navigationTransit = false;
            ClearMoveOrder();
            Entities.Velocity[PlayerId] = FixVec2.Zero;
        }

        public int SpawnCampDummy(CampDummyDefinition definition)
        {
            int id = Entities.Spawn(definition.Position, definition.Health, Faction.Orvill);
            ConfigureDummy(id, definition.Armor, definition.FireResist);
            return id;
        }

        public void ResetCampActivity()
            => SetupCamp(Entities.Position[PlayerId], _campWalkMap);

        /// <summary>
        /// Только для теста эквивалентности: заставляет поиск целей идти наивным
        /// перебором вместо сетки. В игре всегда false — существует ради того,
        /// чтобы можно было доказать, что оптимизация не поменяла поведение.
        /// </summary>
        public bool DebugUseNaiveTargeting = false;

        // ---- приказ на движение ----
        //
        // ПРИКАЗ ЖИВЁТ В СИМУЛЯЦИИ, А НЕ В ПРЕДСТАВЛЕНИИ. Это управление жанра:
        // щёлкнул один раз — персонаж идёт в точку и доходит, даже если кнопку
        // отпустили. Держи представление этот приказ у себя, реплей перестал бы
        // воспроизводить ходьбу: в потоке ввода лежало бы одно нажатие, а шло
        // бы оно сто тиков.
        //
        // Отсюда же и разделение полей ввода: Aim — это КУРСОР, им целятся
        // способности; точка ходьбы запоминается здесь в момент приказа.
        private FixVec2 _moveOrder;
        private bool _hasMoveOrder;
        // Явная точка на земле во время committed-удара задаёт направление
        // ног и корпуса. Автоподход к цели, напротив, всегда смотрит на цель.
        private bool _explicitMoveOrder;

        /// <summary>Куда идёт игрок. Для отрисовки метки приказа.</summary>
        public bool TryGetMoveOrder(out FixVec2 target)
        {
            target = _moveOrder;
            return _hasMoveOrder;
        }

        /// <summary>
        /// Кого игрок бьёт по приказу. С 01.10 всегда −1: приказа «бей вот
        /// этого, подойдя» больше нет — серия сабли бьёт сектором туда, куда
        /// показывает игрок (Simulation.SabreCombo). Свойство оставлено
        /// подсветке целей, которая спрашивает о нём каждый кадр.
        /// </summary>
        public int AttackTarget => -1;

        public int Tick { get; private set; }
        public IReadOnlyList<SimEvent> Events => _events;

        public Simulation(ulong runSeed, int capacity = 512, ForestBudSettings forestBud = null)
        {
            Rng = new RngStreams(runSeed);
            Entities = new EntityStore(capacity);
            ForestBudConfig = forestBud ?? ForestBudSettings.Default;
            _forestBudAttacks = new ForestBudAttackState[capacity];
            _wendigoActions = new WendigoActionState[capacity];
            _stonehoofActions = new StonehoofActionState[capacity];
            _stonehoofArena = new int[capacity];
            _wendigoNextLeap = new int[capacity];
            // Новые мобы леса: состояние на сущность и очередь распада Расщепеня.
            _thorncasters = new ThorncasterState[capacity];
            _thornShots = new ThornShotState[capacity];
            _rootSnarers = new RootSnarerState[capacity];
            _splitters = new SplitterState[capacity];
            _pendingSplits = new PendingSplitterSplit[capacity];
            _forestFruits = new ForestFruitState[capacity * ForestFruitSlotsPerEnemy];
            _telegraphs = new EnemyTelegraph[capacity * TelegraphSlotsPerEntity];
            _enemySwings = new EnemySwingState[capacity];
            _cleavePreviousPositions = new FixVec2[capacity];
            _mobilityHits = new bool[capacity];
            _sabreTargets = new int[capacity];

            // Ячейка равна дальности удара МОБА: обычный запрос задевает 3×3
            // ячейки. Запросы игрока шире (PlayerAttackRange = 2.5), и это
            // корректно: охват ячеек считается от фактического радиуса, поэтому
            // такой запрос просто обходит 5×5. Цена — обход, а не правильность.
            // Сетка покрывает 128×128 метров — с запасом на комнату Разлома.
            Grid = new SpatialHash(
                origin: new FixVec2(Fix64.FromInt(-64), Fix64.FromInt(-64)),
                cellSize: AttackRange,
                cellsX: 64, cellsY: 64,
                capacity: capacity);

            Statuses = new StatusStore(capacity);
            HitScratch = new int[capacity];
            _separationScratch = new int[capacity];
            _crowdScratch = new int[capacity];
            _separationPush = new FixVec2[capacity];
            _separationExempt = new bool[capacity];
            // ИИ мобов v2: путь, застревание, места вокруг героя; лужи гнилых плодов.
            AllocateEnemyBrain(capacity);
            AllocateForestPuddles(capacity);

            Tick = 0;
        }

        /// <summary>
        /// Ставит способность в слот с набором взятых узлов.
        ///
        /// Узлы применяются по возрастанию их Id, а не в порядке взятия —
        /// сортирует их сам AbilityBuild.Rebuild.
        /// </summary>
        public void SetAbility(int slot, AbilityDefinition definition, AbilityNode[] nodes, int nodeCount)
        {
            // Буферы талантов на каждую сущность — при сборке, а не в бою.
            EnsureTalentBuffers();
            // Пустой слот действительно пуст. Ветка, у которой способностей
            // меньше, чем кнопок, — это норма, а не ошибка вызывающего.
            if (definition == null)
            {
                _abilityBuilds[slot] = null;
                _abilityReadyTick[slot] = 0;
                return;
            }

            AbilityBuild build = _abilityBuilds[slot];
            if (build == null)
            {
                build = new AbilityBuild();
                _abilityBuilds[slot] = build;
            }

            build.Rebuild(definition, nodes, nodeCount);
        }

        public AbilityBuild GetAbility(int slot) => _abilityBuilds[slot];

        /// <summary>
        /// Тик, когда слот способности снова готов. Только для интерфейса:
        /// игрок обязан видеть кулдаун, а бой читает это поле сам.
        /// </summary>
        public int AbilityReadyTick(int slot) => _abilityReadyTick[slot];

        /// <summary>
        /// Радиусный запрос в общий буфер. Возвращает количество найденных;
        /// читать из HitScratch. Отдельный метод, чтобы буфер был один на всех.
        /// </summary>
        public int QueryRadiusIntoScratch(FixVec2 center, Fix64 radius, int exclude)
            => AddThicketHullsToQuery(Grid.QueryRadius(Entities, center, radius, exclude, HitScratch), center, radius, exclude);

        /// <summary>
        /// Начальная расстановка. Использует поток Spawns, поэтому одинакова
        /// для одного сида и не зависит от боевых бросков.
        /// </summary>
        public void SetupTestArena(int enemyCount)
        {
            _campWalkMap = null;
            _layout = null;
            ClearMoveOrder();
            Entities.Clear();
            ResetForestBud();
            ResetForestMobs();
            ResetEncounterWaves();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            ConfigurePlayer(Entities.Spawn(FixVec2.Zero, PlayerBaseHealth, Faction.Wole));

            for (int i = 0; i < enemyCount; i++)
            {
                Fix64 x = Rng.Spawns.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30));
                Fix64 y = Rng.Spawns.NextFix(Fix64.FromInt(-30), Fix64.FromInt(30));
                int id = Entities.Spawn(new FixVec2(x, y), EnemyBaseHealth, Faction.Orvill);
                ConfigureEnemy(id);
                _events.Add(SimEvent.Spawn(id, Entities.Position[id]));
            }
        }

        /// <summary>
        /// Расстановка по собранной карте Разлома.
        ///
        /// Свой локальный Pcg32 от переданного сида, как у предметов и карты:
        /// расстановку можно повторить, зная только сид, не таща с собой
        /// состояние забега.
        ///
        /// Игрок встаёт в модуль-вход, враги — во все остальные. Число врагов
        /// в каждой комнате — свой бросок в диапазоне [min, max]: комнаты
        /// одной карты не должны быть заселены поровну.
        /// </summary>
        /// <summary>
        /// Расстановка обычного Разлома.
        ///
        /// <paramref name="enemyBudget"/> — потолок на ВЕСЬ забег, а не на
        /// комнату; ноль означает «потолка нет». Врагов он не размазывает
        /// тоньше, а просто обрывает расстановку: первые комнаты набиваются
        /// как обычно, дальние остаются пустыми. Для теста это и нужно — все
        /// живые тела идут к игроку с первой же комнаты, потому что своего
        /// радиуса агро у врага нет.
        /// </summary>
        public void SetupRift(LayoutMap map, ulong spawnSeed, int minEnemiesPerRoom, int maxEnemiesPerRoom,
            int enemyHealth, int enemyBudget = 0)
        {
            _layout = map;
            ClearMoveOrder();
            Entities.Clear();
            ResetForestBud();
            ResetForestMobs();
            ResetEncounterWaves();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            var rng = new Pcg32(spawnSeed, 0x517CC1B727220A95UL);

            // Вход — всегда нулевое размещение: генератор ставит его первым,
            // и ручная расстановка обязана следовать тому же правилу.
            FixVec2 start = map.PlacedCount > 0 ? map.EntryPoint : FixVec2.Zero;
            ConfigurePlayer(Entities.Spawn(start, PlayerBaseHealth, Faction.Wole));
            if (map.Routes != null) Entities.Facing[PlayerId] = map.Routes.EntryFacing;

            int spawned = 0;
            for (int placement = 1; placement < map.PlacedCount; placement++)
            {
                int enemyCount = rng.NextInt(minEnemiesPerRoom, maxEnemiesPerRoom + 1);

                for (int e = 0; e < enemyCount; e++)
                {
                    if (enemyBudget > 0 && spawned >= enemyBudget) return;
                    var spawnRadius = map.ObstacleCount > 0 ? Fix64.Ratio(85, 100) : EnemyBodyRadius;
                    FixVec2 spot = RandomSpotInModule(map, placement, spawnRadius, ref rng);
                    if (map.Routes != null && !map.Routes.TrySafeSpawn(placement, spot, out spot)) continue;
                    if (!map.IsWalkable(spot, Fix64.Ratio(85, 100))) continue;

                    int id = Entities.Spawn(spot, enemyHealth, Faction.Orvill);
                    ConfigureEnemy(id);
                    _events.Add(SimEvent.Spawn(id, Entities.Position[id]));
                    spawned++;
                }
            }
        }

        /// <summary>
        /// Случайная точка внутри модуля, отступив от его стен на радиус тела
        /// плюс запас. Слишком узкий модуль (отступ съедает всю площадь)
        /// откатывается на центр — так спавн не ломается на тесных комнатах.
        ///
        /// ClampToWalkable — подстраховка, а не основной механизм: угол
        /// модуля мог оказаться вне проходимой зоны, если сосед пристыкован
        /// не во всю грань, и точку нужно вернуть на пол гарантированно.
        /// </summary>
        private static FixVec2 RandomSpotInModule(LayoutMap map, int placement, Fix64 radius, ref Pcg32 rng)
        {
            PlacedModule module = map.GetPlaced(placement);
            Fix64 margin = radius + EnemySpawnWallMargin;

            Fix64 minX = LayoutMap.CellSize * module.OriginX + margin;
            Fix64 maxX = LayoutMap.CellSize * (module.OriginX + module.Width) - margin;
            Fix64 minY = LayoutMap.CellSize * module.OriginY + margin;
            Fix64 maxY = LayoutMap.CellSize * (module.OriginY + module.Height) - margin;

            FixVec2 point = minX > maxX || minY > maxY
                ? map.CenterOf(placement)
                : new FixVec2(rng.NextFix(minX, maxX), rng.NextFix(minY, maxY));

            return map.ClampToWalkable(point, radius);
        }

        /// <summary>
        /// Текущая тестовая пачка Разлома: три Хранителя и шесть Корнеползов.
        ///
        /// Здоровье — в очках, а не процент уровня: съёмка ставит своё
        /// (CombatCaptureEncounter — 1000). Корнеполз без явного числа берёт
        /// табличное; прототипный забег передаёт оба уже умноженными на глубину.
        /// </summary>
        public void SetupForestEncounter(LayoutMap map, ulong spawnSeed, int guardianHealth, int swarmHealth = 0)
        {
            if (swarmHealth <= 0) swarmHealth = ArchetypeHealth(EnemyKind.ForestRootSwarm);
            SetupRift(map, spawnSeed, 3, 3, guardianHealth, enemyBudget: 3);

            // Весь рой приходит из одной комнаты плотной волной. Шаг сетки
            // больше диаметра тела, чтобы первый тик не разбрасывал пачку.
            int placement = map.PlacedCount > 2 ? 2 : map.PlacedCount - 1;
            FixVec2 center = placement >= 0 ? map.CenterOf(placement) : FixVec2.Zero;
            for (int i = 0; i < 6; i++)
            {
                FixVec2 offset = new FixVec2(Fix64.Ratio((i % 3 - 1) * 11, 10),
                    Fix64.Ratio((i / 3 * 2 - 1) * 11, 20));
                FixVec2 spot = map.ClampToWalkable(center + offset, EnemyBodyRadius);
                if (map.Routes != null && !map.Routes.TrySafeSpawn(placement, spot, out spot)) continue;
                    if (!map.IsWalkable(spot, Fix64.Ratio(85, 100))) continue;
                int id = Entities.Spawn(spot, swarmHealth, Faction.Orvill);
                ConfigureEnemy(id, EnemyKind.ForestRootSwarm);
                Entities.Facing[id] = (Entities.Position[PlayerId] - Entities.Position[id]).Normalized();
                _events.Add(SimEvent.Spawn(id, Entities.Position[id]));
            }
            for (int i = 1; i < Entities.Count; i++)
                Entities.Aggro[i] = map.Routes == null;
            Grid.Rebuild(Entities);
        }

        /// <summary>Стенд Вихря: Pelag и три неподвижные мишени.</summary>
        public void SetupWhirlwindShowcase(LayoutMap map, int enemyHealth = 360)
        {
            _layout = map;
            ClearMoveOrder();
            Entities.Clear();
            ResetForestBud();
            ResetForestMobs();
            ResetEncounterWaves();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            FixVec2 center = map.PlacedCount > 0 ? map.CenterOf(0) : FixVec2.Zero;
            ConfigurePlayer(Entities.Spawn(center, PlayerBaseHealth, Faction.Wole));

            Fix64 radius = Fix64.Ratio(17, 10);
            Fix64 halfRadius = radius / Fix64.FromInt(2);
            Fix64 triangleHeight = Fix64.Ratio(147, 100);
            FixVec2[] offsets =
            {
                new FixVec2(radius, Fix64.Zero),
                new FixVec2(-halfRadius, triangleHeight),
                new FixVec2(-halfRadius, -triangleHeight),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                int id = Entities.Spawn(center + offsets[i], enemyHealth, Faction.Orvill);
                ConfigureEnemy(id);

                // В capture враги демонстрируют hit/death reaction, а не
                // устраивают случайную драку поверх оцениваемого приёма.
                Entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
                Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
                Entities.RefreshStats(id);
                Entities.NextAttackTick[id] = int.MaxValue;
                Entities.Facing[id] = (-offsets[i]).Normalized();
                _events.Add(SimEvent.Spawn(id, Entities.Position[id]));
            }

            Grid.Rebuild(Entities);
        }

        /// <summary>
        /// Deterministic combat-feel stand. Damage still comes exclusively from
        /// <see cref="ApplyAttack"/>; this method only authors capture fixtures.
        /// </summary>
        public void SetupCombatFeelShowcase(LayoutMap map, int enemyCount,
            CombatFeelCaptureTier tier, bool activeEnemies = false, bool endurance = false)
        {
            _layout = map;
            ClearMoveOrder();
            Entities.Clear();
            ResetForestBud();
            ResetForestMobs();
            ResetEncounterWaves();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            FixVec2 center = map.PlacedCount > 0 ? map.CenterOf(0) : FixVec2.Zero;
            ConfigurePlayer(Entities.Spawn(center, PlayerBaseHealth, Faction.Wole));

            StatSheet player = Entities.Stats[PlayerId];
            player.SetBase(StatType.CritChance,
                tier == CombatFeelCaptureTier.Critical ? Fix64.One : Fix64.Zero);
            Entities.RefreshStats(PlayerId);
            Entities.Health[PlayerId] = Entities.MaxHealth[PlayerId];
            if (endurance) Entities.Health[PlayerId] = Entities.MaxHealth[PlayerId] = 1000000;
            Entities.Facing[PlayerId] = new FixVec2(Fix64.One, Fix64.Zero);

            int limit = activeEnemies ? Entities.Capacity - 1 : 5;
            int count = enemyCount < 1 ? 1 : enemyCount > limit ? limit : enemyCount;
            FixVec2[] offsets =
            {
                new FixVec2(Fix64.Ratio(31, 20), Fix64.Zero),
                new FixVec2(Fix64.Ratio(12, 5), Fix64.Ratio(13, 10)),
                new FixVec2(Fix64.Ratio(12, 5), Fix64.Ratio(-13, 10)),
                new FixVec2(Fix64.Ratio(1, 2), Fix64.Ratio(12, 5)),
                new FixVec2(Fix64.Ratio(1, 2), Fix64.Ratio(-12, 5)),
            };

            for (int i = 0; i < count; i++)
            {
                // «Убийство» съёмки — с первого удара: у серии сабли он лёгкий (5/6 силы).
                int firstHit = _pelagBasicComboEnabled ? Entities.Damage[PlayerId]
                    : CombatStats.RoundToInt(Fix64.FromInt(Entities.Damage[PlayerId]) * SabreLightScale);
                int health = tier == CombatFeelCaptureTier.Kill && i == 0
                    ? firstHit - 1
                    : 1000;
                FixVec2 offset = offsets[i % offsets.Length];
                if (activeEnemies)
                {
                    // QA сохраняет фиксированную арифметику и поддерживает полную толпу.
                    int ring = i / 10;
                    Fix64 angle = Fix64.TwoPi * Fix64.Ratio(i % 10, 10) + Fix64.Ratio(ring * 18, 100);
                    Fix64 radius = Fix64.Ratio(26, 10) + Fix64.Ratio(ring * 85, 100);
                    offset = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * radius;
                }
                if (endurance) health = 1000000;
                int id = Entities.Spawn(center + offset, health, Faction.Orvill);
                ConfigureEnemy(id);
                if (!activeEnemies)
                {
                    Entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
                    Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
                }
                Entities.RefreshStats(id);
                Entities.Health[id] = health;
                Entities.MaxHealth[id] = health;
                Entities.NextAttackTick[id] = activeEnemies ? Tick + 90 : int.MaxValue;
                Entities.Facing[id] = (-offset).Normalized();
                _events.Add(SimEvent.Spawn(id, Entities.Position[id]));
            }

            Grid.Rebuild(Entities);
        }

        /// <summary>
        /// Базовые статы игрока. Здоровье не трогает — его задал Spawn, и оно
        /// единственное, что приходит снаружи: тир Разлома масштабирует врагов,
        /// а не игрока.
        ///
        /// Заглушка баланса: настоящие базы приедут из таблиц народа и класса.
        /// </summary>
        private void ConfigurePlayer(int id)
        {
            Entities.PushWeight[id] = PlayerPushWeight;

            StatSheet sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, PlayerBaseDamage);
            sheet.SetBase(StatType.AttackSpeed, PlayerBaseAttackSpeed);
            sheet.SetBase(StatType.MoveSpeed, PlayerBaseMoveSpeed);
            sheet.SetBase(StatType.CritChance, BaseCritChance);
            sheet.SetBase(StatType.CritMultiplier, BaseCritMultiplier);
            sheet.SetBase(StatType.MaxLavidium, PlayerBaseLavidium);
            sheet.SetBase(StatType.LavidiumRegen, PlayerBaseLavidiumRegen);
            // Spawn стёр модификаторы — база героя (бывший 5-й уровень) вешается заново.
            ApplyHeroBaselineModifiers(sheet);

            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
            Entities.Lavidium[id] = Fix64.FromInt(Entities.MaxLavidium[id]);
        }

        /// <summary>Пул лавидия героя. Решение владельца от 15 сентября: 200 вместо 100.</summary>
        private static readonly Fix64 PlayerBaseLavidium = Fix64.FromInt(200);

        /// <summary>Восстановление лавидия героя, ед/с. Решение владельца от 13 сентября.</summary>
        private static readonly Fix64 PlayerBaseLavidiumRegen = Fix64.FromInt(3);

        /// <summary>
        /// Базовые статы рядового врага. Здоровье приходит из Spawn: его
        /// считает расстановка — таблица видов, глубина и подстройка пачки.
        /// Урон, тело и окна — строка EnemyArchetypes этого вида.
        ///
        /// Криты у врагов выключены (шанс 0): «повезло мобу» в игре без
        /// щита читается как нечестный удар. Сам бросок в ApplyAttack
        /// остаётся — поток Combat не сдвигается от того, кто бьёт.
        /// </summary>
        private void ConfigureEnemy(int id, EnemyKind kind = EnemyKind.ForestGuardian)
        {
            Entities.Kind[id] = kind;
            if (kind == EnemyKind.ForestBud) { ConfigureForestBud(id); return; }
            if (kind == EnemyKind.ForestWendigo) { ConfigureWendigo(id); return; }
            if (kind == EnemyKind.ForestStonehoof) { ConfigureStonehoof(id); return; }
            if (kind == EnemyKind.ForestThorncaster) { ConfigureThorncaster(id); return; }
            if (kind == EnemyKind.ForestRootSnarer) { ConfigureRootSnarer(id); return; }
            if (kind == EnemyKind.ForestSplitter) { ConfigureSplitter(id); return; }
            if (kind == EnemyKind.ForestSplitling) { ConfigureSplitling(id); return; }
            if (kind == EnemyKind.ForestThicketMaster) { ConfigureThicketMaster(id); return; }
            bool swarm = kind == EnemyKind.ForestRootSwarm;
            EnemyArchetype archetype = EnemyArchetypes.Get(kind);
            // Щит и широкий силуэт требуют больше воздуха, чем прежняя
            // техническая капсула. Радиус не даёт строю схлопываться в одну
            // нечитаемую стопку вокруг игрока.
            // РАДИУС ТЕЛА ПОДНЯТ ПОД РАЗМЕР МОДЕЛИ. Стояло 0.62 при старом
            // мобе ростом метр. Лесной страж — 2.35 м, и на прежнем радиусе
            // толпа слипалась в кучу: тела расходились на 1.24 м при ширине
            // силуэта под два метра, и игрока под ними просто не было видно.
            //
            // 0.85 даёт 1.7 м между центрами. Больше брать нельзя без проверки
            // коридоров: этот же радиус проходит через LayoutMap.IsWalkable, и
            // слишком толстое тело перестанет пролезать в связки комнат.
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = swarm ? Fix64.FromInt(2) : Fix64.One;

            StatSheet sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            sheet.SetBase(StatType.AttackSpeed, swarm
                ? Fix64.Ratio(TicksPerSecond, RootSwarmAttackCooldownTicks) : EnemyBaseAttackSpeed);
            // Сюда доходят только Корнеполз, Хранитель и моб без вида (прочие
            // виды настроены выше своими Configure*): не рой — значит Хранитель.
            sheet.SetBase(StatType.MoveSpeed, swarm ? RootSwarmMoveSpeed : GuardianMoveSpeed);
            sheet.SetBase(StatType.CritChance, Fix64.Zero);
            sheet.SetBase(StatType.CritMultiplier, Fix64.One);

            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
        }

        /// <summary>
        /// Один враг вида kind в точке — с обычной настройкой вида и событием
        /// появления. Для стендов и тестов: забег расставляет пачки сам,
        /// через SetupEncounters.
        /// </summary>
        internal int SpawnEnemy(FixVec2 position, int health, EnemyKind kind)
        {
            int id = Entities.Spawn(position, health, Faction.Orvill);
            ConfigureEnemy(id, kind);
            _events.Add(SimEvent.Spawn(id, position));
            Grid.Rebuild(Entities);
            return id;
        }

        /// <summary>
        /// Расстановка Полигона: игрок и два манекена.
        ///
        /// Мишень стоит в полутора метрах прямо перед игроком — дальность
        /// автоатаки два метра, а взгляд по умолчанию направлен по оси X.
        /// Второй манекен стоит за спиной и тоже не атакует.
        /// </summary>
        public void SetupProvingGround(int dummyHealth, Fix64 dummyArmor, Fix64 dummyFireResist)
        {
            ClearMoveOrder();
            Entities.Clear();
            ResetForestBud();
            ResetForestMobs();
            ResetEncounterWaves();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            ConfigurePlayer(Entities.Spawn(FixVec2.Zero, PlayerBaseHealth, Faction.Wole));

            int dummy = Entities.Spawn(new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero),
                dummyHealth, Faction.Orvill);
            ConfigureDummy(dummy, dummyArmor, dummyFireResist);
            _events.Add(SimEvent.Spawn(dummy, Entities.Position[dummy]));

            int sparring = Entities.Spawn(new FixVec2(-Fix64.Ratio(3, 2), Fix64.Zero),
                System.Math.Max(10000, dummyHealth), Faction.Orvill);
            ConfigureDummy(sparring, Fix64.Zero, Fix64.Zero);
            _events.Add(SimEvent.Spawn(sparring, Entities.Position[sparring]));
        }

        /// <summary>
        /// Манекен-мишень: не ходит, не бьёт, но имеет настраиваемые защиты.
        ///
        /// Нулевая скорость движения удерживает мишень на месте. Запрет атаки
        /// задаётся отдельно: нулевая скорость атаки сама по себе допускает
        /// первый удар до начала долгой перезарядки.
        /// </summary>
        private void ConfigureDummy(int id, Fix64 armor, Fix64 fireResist)
        {
            // Бесконечно восстанавливаемая мишень измеряет билд, а не выдаёт опыт лагеря.
            Entities.XpReward[id] = 0;
            // Ноль веса: мишень не должна отъезжать от ударов. Полигон меряет
            // урон, а не то, как далеко игрок укатил манекен.
            Entities.PushWeight[id] = Fix64.Zero;

            StatSheet sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, Fix64.Zero);
            sheet.SetBase(StatType.AttackSpeed, Fix64.Zero);
            sheet.SetBase(StatType.MoveSpeed, Fix64.Zero);
            sheet.SetBase(StatType.Armor, armor);
            sheet.SetBase(StatType.FireResist, fireResist);

            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
            Entities.NextAttackTick[id] = int.MaxValue;
        }

        /// <summary>
        /// Поднимает манекен обратно. ТОЛЬКО ДЛЯ ПОЛИГОНА: в бою мёртвые
        /// остаются мёртвыми, а мишень обязана пережить любой билд — иначе
        /// замер обрывался бы ровно на сильном, то есть на том, ради которого
        /// Полигон и нужен.
        /// </summary>
        public void ReviveDummy(int id)
        {
            Entities.Alive[id] = true;
            Entities.Health[id] = Entities.MaxHealth[id];
            Statuses.ClearBurn(id);
            Statuses.StunUntilTick[id] = 0;
        }

        /// <summary>
        /// Приводит производные боевые числа игрока в соответствие с листом
        /// после того, как снаряжение или награды поменяли снаружи.
        ///
        /// heal нужен на входе в забег и в лагерь: туда игрок входит с полным
        /// здоровьем, и прибавка к максимуму от надетой вещи иначе осталась бы
        /// пустой строчкой в описании. Между аренами забега здоровье НЕ
        /// восполняется (владелец, 26.09): RiftRun переносит недостачу сам.
        /// </summary>
        public void RefreshPlayerStats(bool heal)
        {
            Entities.RefreshStats(PlayerId);
            if (!heal) return;
            Entities.Health[PlayerId] = Entities.MaxHealth[PlayerId];
            // В Разлом входят и с полным лавидием: пустой пул на старте забега
            // наказывал бы за касты, сделанные ещё в лагере.
            Entities.Lavidium[PlayerId] = Fix64.FromInt(Entities.MaxLavidium[PlayerId]);
        }

        /// <summary>
        /// Сколько здоровья герою не хватает до максимума.
        ///
        /// Между аренами переезжает ИМЕННО НЕДОСТАЧА, а не само здоровье:
        /// прибавка к максимуму от награды доходит и до текущего, а полученный
        /// урон остаётся полученным. Лечат зелья и награды — не дверь; уровень
        /// с 29 сентября статов не даёт и не лечит.
        /// </summary>
        public int PlayerMissingHealth
            => Entities.Count > PlayerId && Entities.Health[PlayerId] < Entities.MaxHealth[PlayerId]
                ? Entities.MaxHealth[PlayerId] - Entities.Health[PlayerId] : 0;

        /// <summary>
        /// Возвращает перенесённую недостачу после расстановки новой арены.
        /// Не убивает: вход в арену с нулём здоровья читался бы как смерть
        /// без удара, поэтому минимум — единица.
        /// </summary>
        public void ApplyPlayerMissingHealth(int missing)
        {
            if (missing <= 0 || Entities.Count <= PlayerId) return;
            int health = Entities.MaxHealth[PlayerId] - missing;
            Entities.Health[PlayerId] = health < 1 ? 1 : health;
        }

        /// <summary>
        /// Разводит пересекающиеся тела.
        ///
        /// ЗАЧЕМ. Враги идут в одну точку — к игроку — и без расталкивания
        /// сливаются в неё буквально: сорок тел в одном пикселе. Толпа
        /// перестаёт читаться, а вместе с ней перестаёт работать всё, что
        /// на толпу рассчитано, от урона по площади до силуэтов.
        ///
        /// Смещения КОПЯТСЯ В БУФЕР и применяются одним проходом. Применяй их
        /// сразу — и результат начал бы зависеть от того, в каком порядке сетка
        /// вернула соседей; так он зависит только от состава пар.
        /// </summary>
        private void SeparateBodies()
        {
            int count = Entities.Count;
            // Исключения пар — чистые проверки состояния, которое расталкивание
            // не трогает: раз на тело за тик, а не дважды на каждую пару
            // соседей (поток D, 29.09: на 48 мобах это была заметная доля шага).
            for (int i = 0; i < count; i++)
            {
                _separationPush[i] = FixVec2.Zero;
                _separationExempt[i] = Entities.Alive[i]
                    && (IsWendigoAirborne(i) || StonehoofOwnsPosition(i) || SplitterOwnsPosition(i)
                        || ThicketShielded(i) || AbordageLifted(i));
            }
            // Тяга Абордажа, как рывок, тел не держит (Simulation.Abordage).
            bool heroPhased = VoidPhased || DashInvulnerable || _mobilitySlot >= 0
                && _abilityBuilds[_mobilitySlot].DefinitionId == AbilityDefinition.SkewerId
                || _abordage.Phase == AbordagePhase.Pull;
            // Герой в фазе не расталкивается ни с кем — как тело-исключение.
            // Рывок (Simulation.Dash) проходит сквозь тела всё своё окно.
            if (heroPhased && PlayerId < count) _separationExempt[PlayerId] = true;

            // Пара тел касается только ближе суммы радиусов, а она не больше
            // двух MaxBodyRadius — меньше стороны ячейки. Значит, касаются
            // только тела из одной или соседних ячеек, и пары обходятся по
            // сетке: ячейка с собой и с четырьмя соседями «вперёд», каждая
            // пара — один раз. Раньше каждое тело спрашивало QueryRadius, и
            // каждая пара мерилась дважды (поток D, 29.09). Смещения копятся
            // сложением, поэтому порядок пар ответа не меняет — меняется
            // только скорость; прежний обход остаётся на случай мелкой сетки.
            if (EntityStore.MaxBodyRadius * 2 + SeparationCellSlack <= Grid.CellSize)
                SeparatePairsByCells();
            else
                SeparatePairsByQuery();

            for (int i = 0; i < count; i++)
            {
                if (!Entities.Alive[i]) continue;
                if (_separationPush[i].LengthSq.Raw == 0) continue;

                // Потолок смещения за тик. Без него две глубоко вложенные
                // сущности выстреливают друг из друга рывком, и это читается
                // как телепорт, а не как расталкивание.
                FixVec2 push = _separationPush[i].ClampLength(MaxSeparationStep);
                Entities.Position[i] = MoveInsideLayout(i, Entities.Position[i], push);
            }
        }

        /// <summary>Запас на округление положения ячейки: пара ближе 1,9 м — в одной или соседних ячейках по 2 м.</summary>
        private static readonly Fix64 SeparationCellSlack = Fix64.Ratio(1, 100);

        /// <summary>Пары касающихся тел — обходом ячеек сетки (сетка только что пересобрана).</summary>
        private void SeparatePairsByCells()
        {
            int[] starts = Grid.CellStarts, counts = Grid.CellCounts, entries = Grid.Entries;
            int cellsX = Grid.CellsX, cellsY = Grid.CellsY;
            for (int cell = Grid.UsedLow; cell <= Grid.UsedHigh; cell++)
            {
                int n = counts[cell];
                if (n == 0) continue;
                int first = starts[cell], end = first + n;
                int cx = cell % cellsX, cy = cell / cellsX;
                bool right = cx + 1 < cellsX, up = cy + 1 < cellsY, left = cx > 0;
                for (int a = first; a < end; a++)
                {
                    int p = entries[a];
                    if (_separationExempt[p]) continue;
                    for (int b = a + 1; b < end; b++) SeparatePair(p, entries[b]);
                    if (right) SeparateWithCell(p, cell + 1, starts, counts, entries);
                    if (!up) continue;
                    if (left) SeparateWithCell(p, cell + cellsX - 1, starts, counts, entries);
                    SeparateWithCell(p, cell + cellsX, starts, counts, entries);
                    if (right) SeparateWithCell(p, cell + cellsX + 1, starts, counts, entries);
                }
            }
        }

        private void SeparateWithCell(int p, int cell, int[] starts, int[] counts, int[] entries)
        {
            int n = counts[cell];
            if (n == 0) return;
            int first = starts[cell], end = first + n;
            for (int b = first; b < end; b++) SeparatePair(p, entries[b]);
        }

        /// <summary>Прежний обход: каждое тело спрашивает соседей у сетки. Для сетки мельче двух тел.</summary>
        private void SeparatePairsByQuery()
        {
            int count = Entities.Count;
            for (int i = 0; i < count; i++)
            {
                if (!Entities.Alive[i] || _separationExempt[i]) continue;
                Fix64 reach = Entities.BodyRadius[i] + EntityStore.MaxBodyRadius;
                int found = Grid.QueryRadius(Entities, Entities.Position[i], reach, i,
                    _separationScratch);
                for (int k = 0; k < found; k++)
                {
                    // Каждая пара обрабатывается ровно один раз, младшим индексом.
                    int j = _separationScratch[k];
                    if (j > i) SeparatePair(i, j);
                }
            }
        }

        /// <summary>
        /// Одна пара тел p, q (в любом порядке): роли — по возрастанию индекса,
        /// как всегда. Смещения копятся в буфер _separationPush.
        /// </summary>
        private void SeparatePair(int p, int q)
        {
            int i = p < q ? p : q, j = p < q ? q : p;
            if (!Entities.Alive[i] || !Entities.Alive[j] || _separationExempt[i] || _separationExempt[j]) return;

            Fix64 wanted = Entities.BodyRadius[i] + Entities.BodyRadius[j];
            FixVec2 delta = Entities.Position[j] - Entities.Position[i];
            // Дальше суммы радиусов по одной оси — дальше и по прямой: квадрат
            // не нужен (dx² ≥ wanted², ответ тот же).
            if (Fix64.Abs(delta.X) >= wanted || Fix64.Abs(delta.Y) >= wanted) return;
            Fix64 distSq = delta.LengthSq;
            if (distSq >= wanted * wanted) return;

            FixVec2 direction;
            Fix64 overlap;

            if (distSq.Raw <= 0)
            {
                // Тела ровно друг в друге. Направление берётся из индексов,
                // а не из случайности: расхождение обязано быть одинаковым
                // на всех машинах, а нормировать нулевой вектор нельзя.
                direction = ((i + j) & 1) == 0
                    ? new FixVec2(Fix64.One, Fix64.Zero)
                    : new FixVec2(Fix64.Zero, Fix64.One);
                overlap = wanted;
            }
            else
            {
                Fix64 distance = Fix64.Sqrt(distSq);
                direction = delta / distance;
                overlap = wanted - distance;
            }

            // Доли смещения нормируются по весам: неподвижное тело
            // не двигается вовсе, а его половину забирает второе.
            Fix64 total = Entities.PushWeight[i] + Entities.PushWeight[j];
            if (total.Raw <= 0) return;

            PushShares(Entities.PushWeight[i], Entities.PushWeight[j], total, out Fix64 shareI, out Fix64 shareJ);

            _separationPush[i] -= direction * (overlap * shareI);
            _separationPush[j] += direction * (overlap * shareJ);
        }

        // Доли расталкивания по паре весов. Деление Fix64 дорогое, а разных
        // пар весов в толпе единицы (рой с роем, рой с хранителем…): восемь
        // последних пар помнятся. Чистая функция весов — не состояние, в хеш
        // не идёт и ответа не меняет (поток D, 29.09).
        private const int PushShareMemo = 8;
        private readonly long[] _pushShareKeyI = new long[PushShareMemo], _pushShareKeyJ = new long[PushShareMemo];
        private readonly Fix64[] _pushShareI = new Fix64[PushShareMemo], _pushShareJ = new Fix64[PushShareMemo];
        private int _pushShareCount, _pushShareNext;

        private void PushShares(Fix64 weightI, Fix64 weightJ, Fix64 total, out Fix64 shareI, out Fix64 shareJ)
        {
            for (int k = 0; k < _pushShareCount; k++)
                if (_pushShareKeyI[k] == weightI.Raw && _pushShareKeyJ[k] == weightJ.Raw)
                { shareI = _pushShareI[k]; shareJ = _pushShareJ[k]; return; }
            shareI = weightI / total;
            shareJ = weightJ / total;
            int slot;
            if (_pushShareCount < PushShareMemo) slot = _pushShareCount++;
            else { slot = _pushShareNext; _pushShareNext = (_pushShareNext + 1) % PushShareMemo; }
            _pushShareKeyI[slot] = weightI.Raw; _pushShareKeyJ[slot] = weightJ.Raw;
            _pushShareI[slot] = shareI; _pushShareJ[slot] = shareJ;
        }

        /// <summary>
        /// Сброс новых мобов леса и общего замедления героя при любой
        /// расстановке — рядом с ResetForestBud, который сбрасывает Вендиго,
        /// Камнекопыта, метки и замахи. Зовётся после Entities.Clear.
        /// </summary>
        private void ResetForestMobs()
        {
            ResetThorncasters();
            ResetRootSnarers();
            ResetSplitters();
            ResetThicketMasters();
            ResetHeroSlow();
            ResetEnemyBrain();
            ResetForestPuddles();
        }

        private void ResetAbilityState(bool preserveBasicSerial = false)
        {
            ResetPotionEffects();
            ResetPreparedGiftTiming();
            ResetUpgrades();
            ResetWhirlwindForms();
            EndArtifactEffects();
            ResetTempo();
            ResetPelagBasicCombo(preserveBasicSerial);
            ResetSabre(preserveBasicSerial);
            CancelBlazeGesture();
            _blazeUntilTick = 0;
            _blazeSlot = -1;
            StopAnchorSlam();
            _whirlwindImpactTick = _whirlwindImpactSlot = -1;
            ResetAbordage();
            ResetAnchorThrow();
            ResetWreck();
            ResetSquall();
            _abilityMovePenaltyUntilTick = 0;
        }

        private void ClearMoveOrder()
        {
            StopCleave();
            _navigationWaypoint = false;
            _navigationTransit = false;
            _hasMoveOrder = false;
            _moveOrder = FixVec2.Zero;
            _explicitMoveOrder = false;
            _abilityMovePenaltyUntilTick = 0;
        }

        // Разбор приказа «бей вот этого» (ReadOrders) снят 01.10 вместе со
        // старой автоатакой: ЛКМ задаёт только направление удара серии, а
        // InputFrame.AttackTarget симуляция больше не читает.

        /// <summary>Сколько врагов ещё живо. Условие зачистки Разлома.</summary>
        public int CountAliveEnemies()
        {
            int alive = 0;
            for (int i = 0; i < Entities.Count; i++)
                if (Entities.Alive[i] && Entities.Side[i] != Faction.Wole) alive++;
            return alive;
        }

        /// <summary>
        /// Дошёл ли игрок до конца тропы. Ручные карты без маршрутов используют модуль-выход.
        ///
        /// Карта без выходов (ExitCount == 0) считается пройденной сразу —
        /// это старые карты и тесты, собранные до появления понятия «выход».
        /// </summary>
        public bool PlayerReachedExit(LayoutMap map)
        {
            if (map.ExitCount == 0) return true;
            if (!Entities.Alive[PlayerId]) return false;

            FixVec2 position = Entities.Position[PlayerId];
            for (int i = 0; i < map.ExitCount; i++)
                if (map.Routes != null
                    ? FixVec2.DistanceSq(position, map.ExitPoint(i)) <= LayoutRoutes.ExitRadius * LayoutRoutes.ExitRadius
                    : map.ContainsWorld(map.GetExit(i), position)) return true;
            return false;
        }

        /// <summary>Ровно один шаг симуляции.</summary>
        public void Step(in InputFrame rawInput)
        {
            _events.Clear();
            ApplyEnemySandboxCommands();
            ExpireTelegraphs();
            UpdatePotionEffects();

            // Замедление героя снимается до пересчёта листов: тик, в который
            // оно кончилось, герой уже бежит в полную силу.
            ExpireHeroSlow();

            // Пересчёт грязных листов статов — первой стадией и ровно один раз
            // за тик. У StatSheet пересчёт по грязному флагу, и точка, в которой
            // он случается, обязана быть одной и той же во всех прогонах: поймай
            // его случайным первым Get посреди боя — и результат начнёт зависеть
            // от того, кто первым до кого дотянулся.
            RefreshDirtyStats();

            // Восстановление лавидия — сразу после статов и до кастов: способность,
            // на которую ресурса хватило ровно к этому тику, обязана сработать
            // на нём, а не на следующем.
            RegenerateLavidium();
            UpdatePelagBasicContinuation();
            UpdateSabreChain();
            // Вступление Хозяина Чащи (кат-сцена): ввод героя не читается (Simulation.ForestBoss.Intro).
            InputFrame input = PrepareCombatInput(ThicketIntroHoldsHero ? InputFrame.Empty : rawInput);

            // Штраф движения начинается в кадр нажатия способности, хотя
            // gameplay-каст разрешается ниже по фиксированному порядку стадий.
            PrimeAbilityMovePenalty(in input);
            PrimePelagBasicAttack(in input);
            // Удар серии начинается до движения: замах режет шаг и держит
            // корпус уже в свой первый тик (Simulation.SabreCombo).
            PrimeSabreSwing(in input);

            // Принудительное перемещение решается ДО собственного движения:
            // тело, которое тащат, своим шагом не идёт, и порядок здесь — это
            // и есть правило приоритета, а не деталь реализации.
            CancelInvalidStonehooves();
            MarkThicketHullStart();
            ResolveForcedMotion();

            MovePlayer(input);
            MoveEnemies();

            // Сетка пересобирается ПОСЛЕ движения и ДО боя: иначе поиск целей
            // работал бы по позициям прошлого тика.
            Grid.Rebuild(Entities);

            // Расталкивание стоит МЕЖДУ движением и боем и требует своей
            // пересборки: оно двигает тела, и бой обязан видеть уже разведённые
            // позиции, а не те, что были до расталкивания.
            SeparateBodies();
            // Хозяин Чащи — твёрдое тело: корпус выдавливает вошедших (Simulation.ForestBoss.Hull).
            PushOutOfThicketHulls();
            Grid.Rebuild(Entities);

            // Порядок стадий боя зафиксирован. Любой другой был бы столь же
            // корректен, но менять его нельзя: он входит в поведение и хеш.
            BeginThicketHitBodies();
            ResolveArtifactUse(in input);
            ResolveAbilityCasts(in input);
            UpdateBlaze();
            ResolveWhirlwindImpact(in input);
            UpdateWhirlwindChannel(in input);
            UpdateFoamWaves();
            UpdateAnchorSlam();
            UpdateWreck(in input);
            UpdateCleave();
            UpdateFlask();
            UpdateBlazeTrail();
            UpdateUpgrades();
            UpdateArtifact();
            UpdateMobility();
            // Абордаж: якорь, зацеп, тяга, удар, фронт формы (Simulation.Abordage).
            UpdateAbordage();
            // Бросок якоря: полёт, натяг, тяга, ловля (Simulation.AnchorThrow).
            UpdateAnchorThrow();
            ContinueChainStep();
            ResolveAttacks(in input);
            EndThicketHitBodies();
            UpdateForestBud();
            UpdateForestPuddles();
            UpdateWendigo();
            UpdateStonehooves();
            UpdateThorncasters();
            UpdateRootSnarers();
            UpdateSplitters();
            UpdateThicketMasters();
            TickBurning();
            TickIgnite();

            // Распад Расщепеня — после всех смертей тика и до волн: дети
            // встают в тот же тик, что умер родитель, и счёт живых волн и
            // зачистки забега никогда не видит ложного нуля. Не внутри Kill:
            // тот зовётся посреди обходов сущностей и до пересборки сетки.
            ResolvePendingSplits();

            // Волны встречи — после всех обновлений врагов и горения: счёт
            // живых видит смерти этого тика, и волна выходит в тот же тик.
            UpdateEncounterWaves();
            // Клятвы и грани сердца (Simulation.Oaths): без них сразу выход.
            UpdateOaths();

            Tick++;
        }

        /// <summary>
        /// Обновляет производные числа у тех, чей лист испачкали снаружи:
        /// надели вещь, взяли узел, повесили баф.
        ///
        /// Обход по возрастанию индекса, как и везде. Проверка флага, а не
        /// пересчёт: чистый лист стоит одного сравнения.
        /// </summary>
        private void RefreshDirtyStats()
        {
            for (int i = 0; i < Entities.Count; i++)
                if (Entities.Stats[i].IsDirty) Entities.RefreshStats(i);
        }

        // ---------- способности ----------

        /// <summary>
        /// Стадия КАСТ для всех четырёх слотов. Слоты обходятся по возрастанию:
        /// при одновременном нажатии двух способностей порядок обязан быть
        /// определённым.
        /// </summary>
        private void ResolveAbilityCasts(in InputFrame input)
        {
            if (!Entities.Alive[PlayerId]) return;
            // Лик Пустоты: в фазе атаковать нельзя.
            if (VoidPhased) return;

            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                if (!input.Ability(slot)) continue;

                AbilityBuild build = _abilityBuilds[slot];
                if (build == null) continue;

                // ПРОДОЛЖЕНИЕ КОМБО ПРОВЕРЯЕТСЯ ДО КУЛДАУНА. Второй и третий
                // удары Крушения — то же самое нажатие той же кнопки, но это
                // не новый каст: кулдаун ещё идёт, и обычная проверка отменяла
                // бы серию на втором ударе всегда.
                if (build.DefinitionId == AbilityDefinition.WreckId
                    && _wreck.Slot == slot && WreckComboOpen)
                {
                    AdvanceWreck(input.Aim);
                    CaptureAbilityClock(slot);
                    _events.Add(new SimEvent(SimEventType.ActionStageStarted, PlayerId, -1, slot,
                        false, Entities.Position[PlayerId], actionVariant: _wreck.Stage));
                    continue;
                }

                if (Tick < _abilityReadyTick[slot]) continue;

                // Не хватает лавидия — каста нет вовсе: ни кулдауна, ни остановки
                // текущих действий, ни события. Нажатие без ресурса не должно
                // отменять Вихрь, который уже крутится.
                if (!CanAffordAbility(build)) continue;

                // Шквал и Абордаж — только по врагу под курсором: без него ни цены, ни кулдауна, ни срыва.
                if (NeedsEnemyTarget(build.DefinitionId) && !ValidAbilityTarget(input.AbilityTarget, build)) continue;
                CancelPlayerAction();

                if (build.DefinitionId == AbilityDefinition.WhirlwindId)
                {
                    _whirlwindImpactTick = Tick + AbilityExecutionTicks(WhirlwindContactDelayFor(slot));
                    _whirlwindImpactSlot = slot;
                    WhirlwindUpgradesAtCast(slot);
                    WhirlwindFormAtCast(slot);
                }
                else if (build.DefinitionId == AbilityDefinition.AnchorLeapId)
                {
                    BeginAbordage(slot, input.AbilityTarget);
                }
                else if (build.DefinitionId == AbilityDefinition.AnchorThrowId)
                {
                    BeginAnchorThrow(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.AnchorSlamId)
                {
                    BeginAnchorSlam(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.ChainStepId)
                {
                    BeginChainStep(slot, input.AbilityTarget);
                }
                else if (build.DefinitionId == AbilityDefinition.WreckId)
                {
                    BeginWreck(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.CleaveId)
                {
                    BeginCleave(slot);
                }
                else if (build.DefinitionId == AbilityDefinition.BlazeId)
                {
                    CastBlaze(slot);
                }
                else if (build.DefinitionId == AbilityDefinition.FireFlaskId)
                {
                    BeginFlask(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.SkewerId || build.DefinitionId == AbilityDefinition.BackblastId)
                {
                    BeginMobility(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.DashId)
                {
                    CastDash(slot, input.Aim);
                }

                _abilityReadyTick[slot] = Tick + AbilityCooldownTicks(build);
                AnchorTalentAfterCast(slot, build);
                FlaskTwoCharges(slot, build);
                SpendLavidium(build);
                CaptureAbilityClock(slot);
                _events.Add(SimEvent.Cast(PlayerId, slot, Entities.Position[PlayerId]));
                if (build.DefinitionId == AbilityDefinition.ChainStepId && _chainHopsLeft > 0)
                    EmitChainHop();
            }
        }

        private void PrimeAbilityMovePenalty(in InputFrame input)
        {
            if (!Entities.Alive[PlayerId]) return;

            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                if (!input.Ability(slot)) continue;
                if (_abilityBuilds[slot] == null) continue;
                if (_abilityBuilds[slot].DefinitionId == AbilityDefinition.BlazeId) continue;
                if (NeedsEnemyTarget(_abilityBuilds[slot].DefinitionId) && !ValidAbilityTarget(input.AbilityTarget, _abilityBuilds[slot])) continue;
                if (Tick < _abilityReadyTick[slot]) continue;
                if (!CanAffordAbility(_abilityBuilds[slot])) continue;

                int until = Tick + AbilityMovePenaltyTicks;
                if (until > _abilityMovePenaltyUntilTick)
                    _abilityMovePenaltyUntilTick = until;

                // Способность имеет приоритет над незавершённой автоатакой.
                // View в тот же кадр убирает upper-body swing; скрытого урона
                // от уже не показываемого клинка оставаться не должно.
                Entities.PendingAttackTarget[PlayerId] = -1;
                Entities.AttackImpactTick[PlayerId] = 0;
                Entities.PendingAttackVariant[PlayerId] = 0;
                return;
            }
        }

        /// <summary>
        /// Единственный момент нанесения урона «Вихрем». View получает обычные
        /// Damage/Death events и уже от них показывает весь impact.
        /// </summary>
        private void ResolveWhirlwindImpact(in InputFrame input)
        {
            if (_whirlwindImpactTick < 0 || Tick < _whirlwindImpactTick) return;

            int slot = _whirlwindImpactSlot;
            _whirlwindImpactTick = -1;
            _whirlwindImpactSlot = -1;
            StopSquall();

            if (!Entities.Alive[PlayerId]) return;
            AbilityBuild build = slot >= 0 && slot < AbilitySlots ? _abilityBuilds[slot] : null;
            if (build == null || build.DefinitionId != AbilityDefinition.WhirlwindId) return;

            // Урон, «Толпа разгоняет» и «Возврат лавидия» — в одном обороте;
            // удержание начинается, только если кнопку ещё держат к контакту.
            WhirlwindPulse(slot, firstContact: true);
            StartWhirlwindWave(slot);
            WhirlwindFormAtContact(slot);
            BeginWhirlwindChannel(slot, in input);
        }


        /// <summary>
        /// Горение. Тикает ПОСЛЕ автоатак, чтобы урон за тик считался один раз
        /// и в одном месте.
        /// </summary>
        private void TickBurning()
        {
            for (int i = 0; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i]) continue;
                if (Statuses.BurnTicksLeft[i] <= 0) continue;

                Statuses.BurnTicksLeft[i]--;

                int damage = Statuses.BurnDamage[i].ToInt();
                if (damage > 0)
                    ApplyAbilityDamage(Statuses.BurnSource[i], i, damage, Statuses.BurnSlot[i],
                        DamageType.Fire, overTime: true);

                if (Statuses.BurnTicksLeft[i] <= 0 && Entities.Alive[i]) Statuses.ClearBurn(i);
            }
        }

        /// <summary>
        /// Урон от способности: без крита и без разброса — их считает
        /// сама способность, если ей положено.
        ///
        /// Тип урона приходит от вызывающего: только он знает, чем бьёт,
        /// а от типа зависит, броня гасит удар или сопротивление.
        /// </summary>
        public void ApplyAbilityDamage(int source, int target, int amount, int slot, DamageType type)
            => ApplyAbilityDamage(source, target, amount, slot, type, overTime: false);

        /// <summary>
        /// То же, но с указанием, удар это или тик урона по времени.
        ///
        /// Различие нужно ТОЛЬКО представлению и стоит ровно одного типа
        /// события: тик горения не имеет права звучать и трясти экран как удар,
        /// потому что их тридцать в секунду на каждой горящей цели.
        /// </summary>
        public void ApplyAbilityDamage(int source, int target, int amount, int slot, DamageType type,
            bool overTime)
        {
            if (!Entities.Alive[target] || amount <= 0) return;
            if (target == PlayerId && PlayerImmune) return;
            // Хозяин Чащи в нырке (от ухода до выхода) и во вступлении неуязвим (Simulation.ForestBoss*).
            if (ThicketShielded(target) || ThicketIntroShields(target)) return;
            if (BlazeEvades(target, overTime)) return;

            // «Горючее»: враг в луже Взрывной смеси получает от Пелага +20%.
            if (source == PlayerId && InFuelledPool(target)) amount = amount * 120 / 100;
            // Метка Абордажа (заготовка таланта, Simulation.Abordage.Strike): ×1,30 от Пелага.
            amount = AbordageMarkAmplify(source, target, amount);
            if (source == PlayerId && !overTime) amount = ApplySunder(target, amount);
            amount = ArtifactOutgoing(source, target, amount, ability: !overTime);
            int power = amount;
            amount = CombatStats.Mitigate(amount, type,
                Entities.Armor[target], Entities.FireResist[target]);
            amount = ApplyResinReduction(target, amount);
            amount = ApplyUpgradeReduction(source, target, amount);
            // «Стойкость» (клятвы, Simulation.Oaths): урон по герою от элит и боссов. Без клятв ×1.
            if (target == PlayerId) amount = OathScaled(amount, OathIncomingDamageScale(source));
            amount = MirrorIncoming(source, target, amount);
            if (HoldDamage(source, target, amount)) return;

            Entities.Health[target] -= amount;
            CrimsonTookDamage(target, amount);
            _events.Add(overTime
                ? SimEvent.DamageOverTime(source, target, amount, Entities.Position[target], type)
                : SimEvent.Damage(source, target, amount, false, Entities.Position[target], type,
                    DamageOrigin.Ability, slot));

            if (Entities.Health[target] > 0)
            {
                // Огненная добавка «Ладно смазал» к способностям приходит только
                // с финальным талантом ветки. Без него урон способности остаётся
                // своим числом — решение владельца от 12 сентября.
                if (!overTime && source == PlayerId) ApplyBlazeAbilityBonus(source, target, power, slot);
                return;
            }
            // Обет Хранителя первым, «Последний вдох» (клятвы) — только если Обет не спас.
            if (VowSaves(target) || OathSaves(target)) return;
            Kill(target, source, slot);
        }

        /// <summary>
        /// Смерть и стадия ПРИ УБИЙСТВЕ.
        ///
        /// Эффекты стадии выполняются в том порядке, в каком их вставили узлы,
        /// а тот задан возрастанием Id узла — см. AbilityBuild.Rebuild.
        /// Статус снимается ПОСЛЕ эффектов: «Перекидывается» читает горение
        /// убитого, и снять его раньше значило бы сломать узел.
        /// </summary>
        private void Kill(int target, int killer, int slot, bool basicAttackKill = false)
        {
            Entities.Health[target] = 0;
            Entities.Alive[target] = false;
            if (target == PlayerId) ResetAbilityState(preserveBasicSerial: true);
            _events.Add(SimEvent.Death(killer, target, Entities.Position[target]));
            // Метка мёртвого гаснет в тик смерти, а не тиком позже, когда до
            // него дойдёт очередь: иначе над трупом кадр висел бы живой сектор.
            CancelEnemySwing(target);
            CancelTelegraphsOf(target);
            // Только настоящая смерть Расщепеня: уход в землю по концу
            // выживания и Alive = false в тестах идут мимо Kill, а детёныш
            // сам не делится. Дети встанут в ResolvePendingSplits этого тика.
            if (Entities.Kind[target] == EnemyKind.ForestSplitter) QueueSplit(target);
            GrantKillXp(target, killer);
            TalentOnKill(target, killer, slot);
            UpgradeOnKill(target, killer);

            if (basicAttackKill && killer == PlayerId)
            {
                // Сабельные убийства возвращают Вихрь небольшими порциями:
                // быстрый клир пачки ускоряет следующий burst, но сам Вихрь
                // не может зациклить собственный кулдаун.
                for (int ability = 0; ability < AbilitySlots; ability++)
                {
                    AbilityBuild candidate = _abilityBuilds[ability];
                    if (candidate == null || candidate.DefinitionId != AbilityDefinition.WhirlwindId)
                        continue;
                    int reduced = _abilityReadyTick[ability] - WhirlwindKillCooldownRefundTicks;
                    _abilityReadyTick[ability] = reduced < Tick ? Tick : reduced;
                }
            }

            Statuses.ClearBurn(target);
            Statuses.StunUntilTick[target] = 0;
        }

        /// <summary>
        /// Управление персонажем. Единственный ввод — приказ идти в точку,
        /// правая кнопка мыши. Направления с клавиатуры нет: игрок указывает
        /// КУДА, а как туда идти — забота симуляции.
        ///
        /// Разворот на месте отдельной кнопки не имеет и не должен иметь.
        /// Приказ в точку рядом с собой не двигает персонажа, а только
        /// доворачивает его — так это работает в жанре, и отдельной механики
        /// тут нет, есть мёртвая зона радиусом TurnInPlaceRadius.
        /// </summary>
        private void MovePlayer(in InputFrame input)
        {
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                ClearMoveOrder();
                return;
            }

            if (AnchorSlamActive && _slamImpactTick >= Tick)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                Entities.Facing[PlayerId] = PlayerFacingStep(Entities.Facing[PlayerId], _slamDirection);
                return;
            }

            // С талантом «На ходу» Рассекающий удар героя не останавливает.
            if (CleaveActive && !CleaveMovable && Tick <= _cleaveImpactTick)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Рывок сильнее приказа: пока якорь тащит игрока, своим шагом он
            // не идёт. Приказ при этом НЕ отменяется — долетев, персонаж
            // продолжит туда, куда его послали до рывка.
            if (ForcedMotion.IsActive(Entities, PlayerId))
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Шквал держит героя и в опоре между прыжками, и в начале выхода; со
            // второго тика выхода шаг идёт и срывает выход (Simulation.Squall).
            if (SquallHoldsHero)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Абордаж держит героя так же: замах, полёт якоря, натяг, удар и
            // начало выхода; со второго тика выхода шаг срывает выход (Simulation.Abordage).
            if (AbordageHoldsHero)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Бросок якоря держит героя от каста до ловли, удержания и начала выхода;
            // со второго тика выхода шаг срывает выход (Simulation.AnchorThrow).
            if (AnchorThrowHoldsHero)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Крушение держит героя: замахи, 2 тика проводки, заряд, удержание и начало
            // выхода; в окне между нажатиями и в хвосте выхода он ходит (Simulation.Wreck).
            if (WreckHoldsHero)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            // Новый приказ перебивает старый. При удержании кнопки он приходит
            // каждый тик и точка едет за курсором — это то же самое поведение,
            // что и раньше, просто теперь оно частный случай.
            if (input.Has(InputFlags.MoveOrder))
            {
                _moveOrder = _layout != null
                    ? _layout.ClampToWalkable(input.Aim, Entities.BodyRadius[PlayerId])
                    : input.Aim;
                _hasMoveOrder = true;
                _explicitMoveOrder = true;
                _navigationWaypoint = input.Has(InputFlags.NavigationWaypoint);
                _navigationTransit = input.Has(InputFlags.NavigationTransit);
            }

            // Выпад добивающего серии сабли: тело ведёт выпад, своим шагом
            // герой не идёт. Приказ идти этого тика уже запомнен выше и
            // поведёт героя после контакта. В корнях выпада нет — тогда сюда
            // не попадаем, и шаг ниже нулевой (Simulation.HeroSlow).
            if (SabreLungeNow)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                Entities.Facing[PlayerId] = _sabre.Direction;
                StepSabreLunge();
                return;
            }

            FixVec2 pos = Entities.Position[PlayerId];
            FixVec2 step = FixVec2.Zero;
            FixVec2 desiredFacing = FixVec2.Zero;
            bool finishingTurnInPlace = false;
            bool attacking = input.Has(InputFlags.Attack);

            // Шаг за тик приходит из листа статов: скорость передвижения —
            // такой же стат, как урон, и предмет вправе её менять. Активное
            // действие меняет только текущий cap, но не сам стат. Замах удара
            // серии — 75%, после контакта снова полная (DESIGN, 30.09).
            Fix64 fullSpeed = Entities.MoveStep[PlayerId];
            Fix64 speed = SabreWindup || PelagBasicWindup
                ? fullSpeed * Fix64.Ratio(3, 4)
                : Tick < _abilityMovePenaltyUntilTick
                    ? fullSpeed * AbilityMoveScale
                    : fullSpeed;
            // Буря (форма Вихря): пока держат — доля шага из стата сборки (Simulation.WhirlwindForms).
            if (WhirlwindStorming) speed = Fix64.Min(speed, fullSpeed * StormMoveScale);

            if (_hasMoveOrder)
            {
                FixVec2 toTarget = _moveOrder - pos;
                Fix64 distSq = toTarget.LengthSq;

                // Смотрим на указанную точку; зажатая атака и идущий удар
                // перехватывают корпус ниже, после всех веток движения.
                desiredFacing = toTarget;
                // Мёртвая зона разворота на месте — свойство КЛИКА МЫШЬЮ, а не
                // движения вообще. Путевая точка приходит не от курсора, и
                // порога у неё нет: угол маршрута может лежать в десяти
                // сантиметрах, и его всё равно надо пройти.
                bool navigating = _navigationWaypoint || _navigationTransit;
                // ПОРОГ ПРИБЫТИЯ — 10 СМ, А НЕ САНТИМЕТР.
                //
                // С сантиметром подъезд не заканчивался вовремя: шаг равен
                // четверти остатка пути, то есть с каждым тиком остаток лишь
                // умножается на 3/4 и до сантиметра ползёт десяток тиков. Всё
                // это время тело числится идущим, и ходьба в конце вырождалась
                // в еле заметное подползание вместо остановки.
                Fix64 arrivalSq = _campWalkMap!=null && _navigationTransit ? Fix64.Zero : navigating ? Fix64.Ratio(1,100) : TurnInPlaceRadiusSq;
                if (distSq > arrivalSq)
                {
                    Fix64 distance = Fix64.Sqrt(distSq);

                    step = _navigationTransit
                        // Транзитный угол проходится насквозь на полной
                        // скорости: за ним дорога продолжается, и тормозить
                        // перед ним не перед чем.
                        ? toTarget / distance * (_campWalkMap!=null?Fix64.Min(speed,distance):speed)
                        : _navigationWaypoint
                            // У ЦЕЛИ скорость ограничивается остатком пути: так
                            // тело подъезжает и встаёт, а не пролетает точку по
                            // инерции. Ограничение ровно остатком, а не его
                            // четвертью: четверть не доводит до цели никогда,
                            // а мягкость даёт разгон в Approach ниже.
                            ? toTarget / distance * Fix64.Min(speed, distance)
                            : PlayerTravelStep(toTarget, speed);
                }
                else
                {
                    // Внутри мёртвой зоны идти уже не надо, но приказ живёт до
                    // завершения разворота. Раньше он снимался сразу, поэтому
                    // модель успевала провернуться ровно на один тик и замирала.
                    finishingTurnInPlace = true;
                }
            }

            if (input.Has(InputFlags.DirectMovement))
            {
                _hasMoveOrder = _explicitMoveOrder = false;
                step = input.MoveDirection.ClampLength(Fix64.One) * speed;
                desiredFacing = input.Aim - pos;
                finishingTurnInPlace = false;
            }

            // ЗАЖАТАЯ АТАКА САМА РАЗВОРАЧИВАЕТ ГЕРОЯ НА КУРСОР — и на бегу:
            // приказ движения не трогается, меняется только корпус, и удар
            // серии уходит туда, куда показывает игрок. Пока удар идёт, корпус
            // держит его направление (Simulation.SabreCombo).
            if (PelagBasicDirectionLocked)
                desiredFacing = _pelagBasicAttack.Direction;
            else if (SabreDirectionLocked)
                desiredFacing = _sabre.Direction;
            else if (attacking || input.Has(InputFlags.AttackPressed))
            {
                FixVec2 aim = input.Aim - pos;
                if (aim.LengthSq.Raw != 0) desiredFacing = aim;
            }

            // Желаемая скорость достигается не сразу: разгон и торможение
            // и есть тот вес, из-за отсутствия которого движение читалось
            // как перестановка фишки.
            FixVec2 velocity = (input.Has(InputFlags.DirectMovement)
                    ? ApproachDirect(Entities.Velocity[PlayerId], step, fullSpeed)
                    : Approach(Entities.Velocity[PlayerId], step, fullSpeed))
                .ClampLength(speed);

            // Маршрут рассчитан для отрезков: инерция на углах срезала путь в препятствие.
            if(_campWalkMap!=null && (_navigationTransit || _navigationWaypoint))velocity=step;
            Entities.Velocity[PlayerId] = velocity;
            FixVec2 moved = MoveInsideLayout(PlayerId, pos, velocity);
            Entities.Position[PlayerId] = moved;
            if (moved.Equals(pos) && velocity.LengthSq.Raw != 0)
                Entities.Velocity[PlayerId] = FixVec2.Zero;

            // Без приказа персонаж не крутится: взгляд — это состояние, а не
            // отражение положения курсора. Иначе он бы дёргался от каждого
            // движения мыши по столу.
            FixVec2 facingBefore = Entities.Facing[PlayerId];
            Entities.Facing[PlayerId] = PlayerFacingStep(facingBefore, desiredFacing);

            // На последнем тике TurnToward сам защёлкивается в target. Проверка
            // тем же порогом позволяет снять приказ после этого тика, не вводя
            // углы и float в детерминированную симуляцию.
            if (finishingTurnInPlace && desiredFacing.LengthSq.Raw == 0)
            {
                _hasMoveOrder = false;
                _explicitMoveOrder = false;
            }
            else if (finishingTurnInPlace && facingBefore.LengthSq.Raw != 0)
            {
                Fix64 remaining = FixVec2.Dot(
                    facingBefore.Normalized(), desiredFacing.Normalized());
                if (remaining >= PlayerTurnStepCos)
                {
                    _hasMoveOrder = false;
                    _explicitMoveOrder = false;
                }
            }
        }

        /// <summary>
        /// Тянет текущую скорость к желаемой не быстрее, чем позволяет разгон.
        ///
        /// Прирост считается от ПОЛНОЙ скорости тела, а не от текущей: иначе
        /// быстрый персонаж разгонялся бы столько же тиков, сколько медленный,
        /// и предмет на скорость передвижения менял бы заодно и отзывчивость.
        /// </summary>
        public static FixVec2 Approach(FixVec2 current, FixVec2 wanted, Fix64 fullSpeed)
        {
            Fix64 maxChange = fullSpeed / AccelerationTicks;

            // Нулевая скорость тела означает «не ходит вовсе» — манекен,
            // например. Тянуть там нечего.
            if (maxChange.Raw <= 0) return wanted;

            return current + (wanted - current).ClampLength(maxChange);
        }

        // WASD/стик реагируют сразу: первый тик даёт 75% скорости, второй —
        // полную; отпускание останавливает за один тик. Приказ мышью сохраняет
        // прежний плавный разгон и собственную навигацию.
        private static FixVec2 ApproachDirect(FixVec2 current, FixVec2 wanted, Fix64 fullSpeed)
        {
            Fix64 change = wanted.LengthSq == Fix64.Zero
                ? fullSpeed : fullSpeed * Fix64.Ratio(3, 4);
            return current + (wanted - current).ClampLength(change);
        }

        /// <summary>
        /// Разворот на ограниченную скорость: за тик не больше TurnStep.
        ///
        /// Поворот делается умножением на матрицу фиксированного шага, а не через
        /// Atan2 — угол как число вообще не появляется, поэтому и накопленной
        /// ошибки от перевода «вектор → угол → вектор» нет. Результат
        /// нормализуется каждый тик: без этого длина за сотни поворотов уползёт.
        /// </summary>
        public static FixVec2 PlayerTravelStep(FixVec2 toTarget, Fix64 speed)
        {
            if (toTarget.LengthSq <= TurnInPlaceRadiusSq) return FixVec2.Zero;
            Fix64 distance = Fix64.Sqrt(toTarget.LengthSq);
            Fix64 approach = distance / BrakeTicks;
            return toTarget / distance * (approach < speed ? approach : speed);
        }

        public static FixVec2 PlayerFacingStep(FixVec2 current, FixVec2 desired)
            => TurnToward(current, desired, PlayerTurnStepCos, PlayerTurnStepSin);

        private static FixVec2 TurnToward(FixVec2 current, FixVec2 desired,
            Fix64 stepCos, Fix64 stepSin)
        {
            if (desired.LengthSq.Raw == 0) return current;

            FixVec2 target = desired.Normalized();
            if (current.LengthSq.Raw == 0) return target;

            FixVec2 from = current.Normalized();

            // Осталось меньше шага — доворачиваем сразу, иначе будет дрожание
            // вокруг цели с амплитудой в один шаг.
            Fix64 dot = FixVec2.Dot(from, target);
            if (dot >= stepCos) return target;

            // Знак векторного произведения задаёт сторону поворота. При строго
            // противоположных векторах он равен нулю — тогда крутим влево,
            // и это решение одинаково на всех машинах, что и требуется.
            Fix64 cross = from.X * target.Y - from.Y * target.X;
            Fix64 sin = cross.Raw >= 0 ? stepSin : -stepSin;

            FixVec2 rotated = new FixVec2(
                from.X * stepCos - from.Y * sin,
                from.X * sin + from.Y * stepCos);

            return rotated.Normalized();
        }

        /// <summary>
        /// Стоит ли прямо по курсу к игроку союзник, который уже ближе.
        ///
        /// Считается по той же сетке, что и всё остальное, и в том же порядке —
        /// значит детерминировано. Сектор узкий: заслон засчитывается только
        /// когда сосед действительно на пути, а не сбоку, иначе враги вставали
        /// бы, едва оказавшись рядом друг с другом.
        /// </summary>
        private bool BlockedByCloserAlly(int self, FixVec2 playerPos, FixVec2 toPlayer,
            out FixVec2 blockerDirection)
        {
            blockerDirection = FixVec2.Zero;
            Fix64 myDistSq = toPlayer.LengthSq;
            Fix64 reach = Entities.BodyRadius[self] + EntityStore.MaxBodyRadius;
            int found = Grid.QueryRadius(Entities, Entities.Position[self], reach, self,
                _crowdScratch);

            for (int k = 0; k < found; k++)
            {
                int other = _crowdScratch[k];
                if (!Entities.Alive[other]) continue;
                if (other == PlayerId) continue;
                if (Entities.Side[other] != Entities.Side[self]) continue;

                // Ближе к игроку — значит он занял место, за которое мы боремся.
                if ((playerPos - Entities.Position[other]).LengthSq >= myDistSq) continue;

                FixVec2 toOther = Entities.Position[other] - Entities.Position[self];
                Fix64 touching = Entities.BodyRadius[self] + Entities.BodyRadius[other];
                if (toOther.LengthSq > touching * touching) continue;

                // Тот же сектор, что и у удара: сосед на пути, а не сбоку.
                if (!FixVec2.WithinArc(toPlayer, toOther, AttackArcCos)) continue;

                blockerDirection = toOther;
                return true;
            }

            return false;
        }

        private void MoveEnemies()
        {
            FixVec2 playerPos = Entities.Position[PlayerId];
            bool playerAlive = Entities.Alive[PlayerId];

            // Жетоны считаются один раз на всё движение: за время хода ни один
            // замах не начинается и не кончается — это делает ResolveAttacks.
            bool meleeTokensTaken = CountMeleeAttackTokens(-1) >= MeleeAttackTokenLimit;

            // Готовность к подходу меняется сразу, места каждый выбирает по
            // своему расписанию. Замахи и рывки не переориентируются.
            if (playerAlive) UpdateSurround();
            for (int i = 1; i < Entities.Count; i++)
                if (Entities.Alive[i] && Entities.Aggro[i]) ProbeStuck(i);

            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i]) continue;
                // Хозяин Чащи ходит сам и до проверки оглушения: его не держит ни оглушение, ни волок.
                if (Entities.Kind[i] == EnemyKind.ForestThicketMaster) { MoveThicketMaster(i); continue; }

                if (Statuses.IsStunned(i, Tick))
                { Entities.Velocity[i] = FixVec2.Zero; continue; }

                // Встающий из земли стоит: он ещё корни (Simulation.EncounterWaves).
                if (IsEmerging(i))
                { Entities.Velocity[i] = FixVec2.Zero; continue; }

                // Волочимый враг не идёт своим ходом. Иначе он приезжал бы
                // вдвое быстрее задуманного — тяга плюс собственный шаг к
                // игроку складывались бы, и Подсечка била бы сильнее, чем
                // написано на листе.
                if (ForcedMotion.IsActive(Entities, i)) continue;

                if (!playerAlive) { Entities.Velocity[i] = FixVec2.Zero; continue; }

                FixVec2 toPlayer = playerPos - Entities.Position[i];
                EnemyKind kind = Entities.Kind[i];

                if (kind == EnemyKind.ForestWendigo)
                { MoveWendigo(i, toPlayer); continue; }
                if (kind == EnemyKind.ForestStonehoof)
                { MoveStonehoof(i, toPlayer); continue; }

                // Новые мобы леса ходят в своих файлах. true — ход сделан
                // целиком (разворот, агро, шаг); false — общий ход ниже:
                // Расщепень и детёныш идут как ближники, прочие стоят.
                if (kind == EnemyKind.ForestThorncaster)
                { if (MoveThorncaster(i, toPlayer)) continue; }
                else if (kind == EnemyKind.ForestRootSnarer)
                { if (MoveRootSnarer(i, toPlayer)) continue; }
                else if (kind == EnemyKind.ForestSplitter || kind == EnemyKind.ForestSplitling)
                { if (MoveSplitter(i, toPlayer)) continue; }

                // Замах и восстановление держат тело: ни шага, ни доворота.
                // Направление зафиксировано в начале замаха, и именно по нему
                // нарисован сектор; доворот вслед за героем сделал бы метку ложью.
                if (EnemySwingHoldsBody(i))
                {
                    Entities.Velocity[i] = FixVec2.Zero;
                    Entities.Facing[i] = _enemySwings[i].Direction;
                    continue;
                }

                if (!UpdateAggro(i, toPlayer))
                {
                    // Ещё не заметил — стоит на месте и следит взглядом, а не
                    // бежит вслепую через весь Разлом с той секунды, как игрок вошёл.
                    Entities.Facing[i] = TurnToward(Entities.Facing[i], toPlayer,
                        EnemyTurnStepCos, EnemyTurnStepSin);
                    Entities.Velocity[i] = FixVec2.Zero;
                    continue;
                }

                if (kind == EnemyKind.ForestBud)
                {
                    Entities.Facing[i] = TurnToward(Entities.Facing[i], toPlayer,
                        EnemyTurnStepCos, EnemyTurnStepSin);
                    MoveForestBud(i, toPlayer);
                    continue;
                }

                // Ход ближника — только тем, кто бьёт общим замахом. Вид со
                // своими атаками, не сделавший хода сам, стоит и следит за
                // героем: чужой подход к герою сделал бы из него Хранителя.
                if (!UsesEnemySwing(kind))
                {
                    Entities.Facing[i] = TurnToward(Entities.Facing[i], toPlayer,
                        EnemyTurnStepCos, EnemyTurnStepSin);
                    Entities.Velocity[i] = FixVec2.Zero;
                    continue;
                }

                Fix64 speed = Entities.MoveStep[i];
                bool swarm = IsSwarmLike(kind);
                Fix64 attackRange = AttackRangeFor(i);
                if (swarm && toPlayer.LengthSq <= Fix64.FromInt(4))
                    speed = kind == EnemyKind.ForestSplitling
                        ? speed * SplitlingRushSpeed / SplitlingMoveSpeed
                        : speed * RootSwarmRushSpeed / RootSwarmMoveSpeed;

                // Подошёл на дистанцию удара — гасим ход, но не мгновенно:
                // враг, встающий как вкопанный, выдаёт отсутствие тела ровно
                // так же, как и игрок.
                //
                // ВТОРОЕ УСЛОВИЕ ОСТАНОВКИ: впереди стоит свой.
                //
                // Без него каждый враг идёт в ЦЕНТР игрока и тормозит только в
                // двух метрах от него. В круг такого радиуса помещается семь
                // тел, а идут туда все тридцать: расталкивание выпихивает
                // лишних наружу, они разворачиваются и идут снова. На экране
                // это читается как «прошли пару шагов — телепорт назад», и
                // именно это владелец и снял на видео.
                //
                // Правило простое и детерминированное: если прямо по курсу
                // вплотную стоит союзник, который УЖЕ ближе к игроку, — встаём
                // за ним. Толпа сама собирается в кольцо и перестаёт бурлить.
                // Место вокруг героя: к нему — по пути в обход, у кольца — по дуге.
                FixVec2 wanted = SurroundWanted(i, toPlayer, speed, attackRange, out FixVec2 look);

                // Стоит в чужой метке (круг, кольцо, полоса) или в кислой луже —
                // выходит бегом вбок от неё: умный моб не ждёт удара союзника.
                if (InAllyDanger(i, Entities.Position[i], out FixVec2 escape))
                {
                    wanted = escape * Entities.MoveStep[i];
                    look = escape;
                }

                // Взгляд — по ходу при заметном боковом шаге (клипы боком не
                // ходят), иначе на героя: добежав, моб доворачивается к цели.
                Entities.Facing[i] = TurnToward(Entities.Facing[i], look,
                    EnemyTurnStepCos, EnemyTurnStepSin);

                Entities.Velocity[i] = Approach(Entities.Velocity[i], wanted, speed);
                FixVec2 from = Entities.Position[i];
                FixVec2 moved = EnemyStep(i, from, Entities.Velocity[i]);
                Entities.Position[i] = moved;
                if (moved.Equals(from) && Entities.Velocity[i].LengthSq.Raw != 0)
                    Entities.Velocity[i] = FixVec2.Zero;
            }
        }

        /// <summary>
        /// Решает, гонится ли враг за игроком уже сейчас. Агро одноразовое и
        /// необратимое: заметив, враг не «забывает» игрока, даже если тот
        /// выйдет за радиус обнаружения — так же ведёт себя большинство ARPG,
        /// и это проще объяснить игроку, чем скрытый таймер забывания.
        ///
        /// Пока не агрится — таймер обнаружения живёт только внутри радиуса:
        /// вышел, не успев среагировать, — обнаружение сбрасывается, а не
        /// тикает в фоне, иначе погоня стартовала бы необъяснимо поздно.
        /// </summary>
        private bool UpdateAggro(int i, FixVec2 toPlayer)
        {
            if (Entities.Aggro[i]) return true;

            // Позвала пачка: сосед заметил героя — этот идёт следом, даже не видя его сам.
            // Или он из последних на арене — идёт сам (LastStandAlert).
            if (PackAlerted(i) || LastStandAlert())
            {
                Entities.Aggro[i] = true;
                AlertPack(i);
                return true;
            }

            if (toPlayer.LengthSq > EnemyDetectRangeSq)
            {
                Entities.NoticeTick[i] = -1;
                return false;
            }

            if (Entities.NoticeTick[i] < 0)
                Entities.NoticeTick[i] = Tick + Rng.Ai.NextInt(EnemyNoticeMinTicks, EnemyNoticeMaxTicks + 1);

            if (Tick < Entities.NoticeTick[i]) return false;

            Entities.Aggro[i] = true;
            AlertPack(i);
            return true;
        }

        /// <summary>
        /// Двигает всех, кого тащат: якорь, подсечка, шаг по цепи.
        ///
        /// Скорость не хранится — она пересчитывается каждый тик из остатка
        /// пути и остатка тиков. Так тело приезжает ровно за назначенное
        /// число тиков даже если по дороге его прижало к стене: следующий тик
        /// просто получит больший шаг. Хранимая скорость в такой ситуации
        /// давала бы недолёт, и способность с гарантированной дальностью
        /// иногда не доносила бы до цели.
        /// </summary>
        private void ResolveForcedMotion()
        {
            for (int i = 0; i < Entities.Count; i++)
            {
                int left = Entities.ForcedTicksLeft[i];
                if (left <= 0) continue;

                if (!Entities.Alive[i])
                {
                    ForcedMotion.Clear(Entities, i);
                    continue;
                }
                // Хозяина Чащи чужая воля не двигает (Simulation.ForestBoss).
                if (Entities.Kind[i] == EnemyKind.ForestThicketMaster) { ForcedMotion.Clear(Entities, i); continue; }

                FixVec2 from = Entities.Position[i];
                FixVec2 delta = Entities.ForcedTarget[i] - from;

                // Делим остаток пути на остаток тиков. Целочисленное деление
                // Fix64 округляет вниз, поэтому на последнем тике шаг берётся
                // целиком — иначе тело вечно не доезжало бы последние миллиметры.
                // Водоворот ведёт своих с разгоном, последний тик тоже (Simulation.WhirlwindForms.MaelstromPullStep).
                // Бросок якоря — так же, своими массивами (Simulation.AnchorThrow.Pull, AnchorThrowPullStep).
                FixVec2 step = MaelstromPullStep(i, left, delta, out FixVec2 eased) ? eased
                    : AnchorThrowPullStep(i, left, delta, out FixVec2 reeled) ? reeled
                    : left <= 1 ? delta : delta / Fix64.FromInt(left);

                // Большой шаг рывка не должен перескочить узкую стену между концами.
                int substeps = System.Math.Max(1, (step.Length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
                FixVec2 piece = step / Fix64.FromInt(substeps);
                // Рывок героя (Roll) — тоже прямо: стены его останавливают
                // (владелец 02.10), а не ведут вдоль себя.
                bool dash = i == PlayerId && Entities.ForcedKind[i] == (byte)ForcedMotionKind.Roll;
                bool straight = dash
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.Skewer
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.Backblast
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.EnemyLunge
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.Knockback
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.SplitPop
                    || Entities.ForcedKind[i] == (byte)ForcedMotionKind.Shoved;
                bool blocked = false;
                for (int s = 0; s < substeps; s++)
                {
                    // Выпад и отскок заканчиваются у стены: скольжение меняло бы полосу удара.
                    if (straight && (_layout != null || _campWalkMap != null)
                        && !CanTravel(from, from + piece, Entities.BodyRadius[i]))
                    { Entities.ForcedTarget[i] = from; blocked = true; break; }
                    from = straight ? from + piece : MoveInsideLayout(i, from, piece);
                }
                Entities.Position[i] = from;
                // Рывок под огнём с талантом «Огненный след» оставляет след по пути.
                if (i == PlayerId) DropBlazeTrail(i);

                // Скорость обнуляется намеренно: тело едет не своим ходом, и
                // представление обязано видеть это как перемещение чужой волей,
                // а не как бег. Иначе у волочимого врага играла бы анимация бега.
                Entities.Velocity[i] = FixVec2.Zero;

                Entities.ForcedTicksLeft[i] = left - 1;
                // Рывок кончается, как только тело встало: доехало или упёрлось
                // в стену. Неуязвимость при этом держится своим окном (Simulation.Dash).
                if (dash && (blocked || Entities.ForcedTicksLeft[i] <= 0))
                {
                    StopDash(cutShort: blocked);
                    ForcedMotion.Clear(Entities, i);
                    continue;
                }
                if (Entities.ForcedTicksLeft[i] <= 0) ForcedMotion.Clear(Entities, i);
            }
        }

        private FixVec2 MoveInsideLayout(int entity, FixVec2 from, FixVec2 delta)
        {
            if ((_layout == null && _campWalkMap == null) || delta.LengthSq.Raw == 0) return from + delta;
            if (_campWalkMap != null) return _campWalkMap.Slide(from, delta);

            Fix64 radius = Entities.BodyRadius[entity];
            FixVec2 full = from + delta;
            if (CanTravel(from, full, radius)) return full;

            // Скользим вдоль стены вместо полной остановки на диагональном
            // вводе. Сначала пробуется большая компонента, чтобы направление
            // игрока сохранялось максимально близко к приказу.
            bool xFirst = Fix64.Abs(delta.X) >= Fix64.Abs(delta.Y);
            FixVec2 first = xFirst
                ? from + new FixVec2(delta.X, Fix64.Zero)
                : from + new FixVec2(Fix64.Zero, delta.Y);
            if (CanTravel(from, first, radius)) return first;

            FixVec2 second = xFirst
                ? from + new FixVec2(Fix64.Zero, delta.Y)
                : from + new FixVec2(delta.X, Fix64.Zero);
            return CanTravel(from, second, radius) ? second : from;
        }

        private bool CanTravel(FixVec2 from, FixVec2 to, Fix64 radius)
            => _campWalkMap != null ? _campWalkMap.CanTravel(from, to) : _layout.CanTravel(from, to, radius);

        /// <summary>
        /// Автоатака. Каждая живая сущность ищет ближайшую цель чужой стороны
        /// в радиусе удара. Порядок обхода строго по индексу — от него зависит,
        /// кто ударит первым при равных условиях, и он обязан быть стабильным.
        /// </summary>
        private void ResolveAttacks(in InputFrame input)
        {
            for (int order = 0; order < Entities.Count; order++)
            {
                // Первый остаётся герой. Начало очереди мобов смещается по
                // времени Sim: низкий id не забирает свободный жетон вечно.
                int i = order == 0 ? PlayerId : 1 + (order - 1 + Tick / SurroundAssignTicks) % (Entities.Count - 1);
                if (i != PlayerId) { UpdateEnemySwing(i); continue; }
                // Герой бьёт серией сабли (Simulation.SabreCombo): старт удара
                // — в PrimeSabreSwing до движения, здесь только контакт.
                if (_pelagBasicComboEnabled) ResolvePelagBasicContact();
                else ResolveSabreContact();
            }
        }

        private int FindNearestEnemy(int from)
        {
            // У ИГРОКА ВЫБОР ИДЁТ В ШИРОКОМ СЕКТОРЕ 120°, А НЕ В УЗКОМ ОКНЕ
            // СТАРТА ВЗМАХА. Здесь стоял AttackCommitCos — порог, при котором
            // корпус уже почти смотрит на цель. Как условие ВЫБОРА он означал
            // «бей только то, на что и так смотришь»: на бегу мимо толпы враг
            // проскакивал через это окно за пару тиков, и зажатая кнопка почти
            // не срабатывала. Доворот у героя 20° за тик при замахе в 9 тиков,
            // то есть цель на 60° он успевает добрать с запасом — и уже к
            // контакту CanLandAttack видит её во фронтальном секторе.
            //
            // У МОБА ЭТО УСЛОВИЕ СТАРТА ЗАМАХА. Хранитель ищет героя в 2,2 м и
            // только перед собой (±37°), Корнеполз — в своих 1,4 м и ±120°.
            // Дальность и сектор берутся из одних функций и сеткой, и прямым
            // перебором: иначе тест эквивалентности сравнивал бы разные правила.
            Fix64 arc = from == PlayerId ? AttackArcCos : EnemySwingStartCos(from);
            return DebugUseNaiveTargeting
                ? NaiveFindNearestEnemy(from, arc)
                : Grid.FindNearestEnemy(Entities, from, TargetSearchRange(from), arc);
        }

        private Fix64 TargetSearchRange(int from)
            => from == PlayerId ? PlayerAttackRange : EnemySwingStartRange(from);

        /// <summary>
        /// Эталонная реализация: прямой перебор всех сущностей.
        /// Используется только тестом эквивалентности — доказывает, что сетка
        /// даёт ровно тот же результат, включая разрыв ничьих по индексу.
        /// </summary>
        private int NaiveFindNearestEnemy(int from, Fix64 arcCos)
        {
            int best = -1;
            Fix64 bestDistSq = Fix64.MaxValue;
            FixVec2 origin = Entities.Position[from];
            FixVec2 facing = Entities.Facing[from];
            Faction mySide = Entities.Side[from];
            Fix64 range = TargetSearchRange(from);

            for (int i = 0; i < Entities.Count; i++)
            {
                if (i == from || !Entities.Alive[i]) continue;
                if (Entities.Side[i] == mySide) continue;

                FixVec2 toTarget = Entities.Position[i] - origin;
                Fix64 distSq = toTarget.LengthSq;
                if (distSq > range * range) continue;
                if (!FixVec2.WithinArc(facing, toTarget, arcCos)) continue;
                if (distSq < bestDistSq || (distSq == bestDistSq && i < best))
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Автоатака. Все числа приходят из плоских массивов EntityStore, а те —
        /// из листа статов: другого источника боевых чисел в симуляции нет.
        /// </summary>
        private int ApplyAttack(int source, int target, int variant, Fix64 damageScale)
        {
            // Бросок на крит делается ВСЕГДА, даже при нулевом шансе: иначе
            // расход боевого потока случайности зависел бы от снаряжения,
            // и один и тот же сид перестал бы давать один и тот же забег.
            bool crit = Rng.Combat.Chance(Entities.CritChance[source]);
            // Keep the normal critical roll even when developer immunity absorbs the hit.
            if (target == PlayerId && PlayerImmune) return 0;
            // Хозяин Чащи в нырке и во вступлении неуязвим; бросок крита выше уже сделан — поток не сдвигается.
            if (ThicketShielded(target) || ThicketIntroShields(target)) return 0;
            if (BlazeEvades(target, overTime: false)) return 0;
            // «Верный удар»: бросок уже сделан (поток не сдвигается), усиление подменяет результат.
            crit = SureCrit(source, crit);

            int damage = CombatStats.RoundToInt(
                Fix64.FromInt(Entities.Damage[source]) * damageScale);
            if (crit)
                damage = CombatStats.RoundToInt(Fix64.FromInt(damage) * Entities.CritMultiplier[source]);
            if (source == PlayerId && InFuelledPool(target)) damage = damage * 120 / 100;
            damage = AbordageMarkAmplify(source, target, damage);
            if (source == PlayerId) damage = ApplySunder(target, damage);
            damage = ArtifactOutgoing(source, target, damage, ability: false);
            damage = PreparedGiftAttackDamage(source, damage);
            // «Тяжёлая рука» (клятвы): все удары ЛКМ героя идут сюда, способности — нет. Без клятв ×1.
            if (source == PlayerId) damage = OathScaled(damage, OathSabreDamageScale);

            // Броня гасит удар ПОСЛЕ крита: крит увеличивает сам удар, а кривая
            // брони зависит от его размера — значит и считать её надо от того,
            // что реально прилетело.
            int power = damage;
            damage = CombatStats.MitigateByArmor(damage, Entities.Armor[target]);
            damage = ApplyResinReduction(target, damage);
            damage = ApplyUpgradeReduction(source, target, damage);
            if (target == PlayerId) damage = OathScaled(damage, OathIncomingDamageScale(source));
            damage = MirrorIncoming(source, target, damage);
            if (HoldDamage(source, target, damage)) return 0;

            Entities.Health[target] -= damage;
            CrimsonTookDamage(target, damage);
            _events.Add(SimEvent.Damage(source, target, damage, crit, Entities.Position[target],
                DamageType.Physical, DamageOrigin.BasicAttack, variant,
                source == PlayerId && _pelagBasicComboEnabled ? _pelagBasicAttack : default));

            // Горящая сабля добавляет огонь ТОЛЬКО к обычным атакам — решение
            // владельца от 12 сентября. Доля берётся от силы удара до брони:
            // огонь едет на взмахе, а гасит его сопротивление огню.
            if (Entities.Health[target] > 0) ApplyBlazeBonus(source, target, power);
            if (Entities.Health[target] > 0) ApplyBlazeIgnite(source, target, power);

            // Смерть от автоатаки идёт тем же путём, что и от способности:
            // стадия ПриУбийстве обязана срабатывать независимо от того, чем
            // добили. «Перекидывается» иначе не сработал бы на добитом мечом.
            if (Entities.Health[target] <= 0 && !VowSaves(target) && !OathSaves(target))
                Kill(target, source, BurnSlotOf(target), basicAttackKill: true);
            return damage;
        }

        /// <summary>
        /// Множитель клятвы к целому урону. Ровно ×1 — число не трогается и в Fix64 не ходит:
        /// без клятв бой бит в бит прежний, прибитые хеши стоят.
        /// </summary>
        private static int OathScaled(int amount, Fix64 scale)
            => scale == Fix64.One || amount <= 0 ? amount
                : System.Math.Max(1, CombatStats.RoundToInt(Fix64.FromInt(amount) * scale));

        /// <summary>
        /// Слот способности, которой цель была подожжена, или -1.
        /// Нужен, чтобы добитый обычной атакой горящий враг всё равно попал
        /// в стадию ПриУбийстве той способности, которая его подожгла.
        /// </summary>
        private int BurnSlotOf(int target)
            => Statuses.IsBurning(target) ? Statuses.BurnSlot[target] : -1;

        /// <summary>
        /// Хеш полного состояния. Используется только тестом на детерминизм
        /// и валидацией реплеев — в игровой логике не участвует.
        /// </summary>
        internal bool PlayerInvulnerable { get; set; }

        public ulong StateHash()
        {
            ulong hash = Hashing.Offset;
            if (PlayerInvulnerable) Hashing.Mix(ref hash, 0x474F44);
            Hashing.Mix(ref hash, Tick);
            HashTempo(ref hash);
            HashPelagBasicCombo(ref hash);
            HashSabreCombo(ref hash);
            HashPotionEffects(ref hash);
            HashPreparedGift(ref hash);
            HashArtifact(ref hash);
            HashAnchorSlam(ref hash);
            HashWreck(ref hash);
            HashCleave(ref hash);
            HashBlaze(ref hash);
            HashFlask(ref hash);
            HashProgression(ref hash);
            HashTalents(ref hash);
            HashUpgrades(ref hash);
            HashWhirlwindForms(ref hash);
            HashSquall(ref hash);
            HashAbordage(ref hash);
            HashAnchorThrow(ref hash);
            HashForestBud(ref hash);
            HashWendigo(ref hash);
            HashStonehooves(ref hash);
            HashThorncasters(ref hash);
            HashRootSnarers(ref hash);
            HashSplitters(ref hash);
            HashThicketMasters(ref hash);
            HashHeroSlow(ref hash);
            HashTelegraphs(ref hash);
            HashEnemySwings(ref hash);
            HashCleaveFan(ref hash);
            HashEncounterWaves(ref hash);
            HashEnemyBrain(ref hash);
            HashForestPuddles(ref hash);
            // Клятвы: без них пусто — хеш прежний.
            HashOaths(ref hash);

            // Приказ — часть состояния персонажа, а не ввода: он переживает
            // отпущенную кнопку, значит обязан быть в хеше.
            Hashing.Mix(ref hash, _hasMoveOrder ? 1 : 0);
            Hashing.Mix(ref hash, _moveOrder.X);
            Hashing.Mix(ref hash, _moveOrder.Y);
            Hashing.Mix(ref hash, _explicitMoveOrder ? 1 : 0);
            Hashing.Mix(ref hash, _navigationWaypoint ? 1 : 0);
            Hashing.Mix(ref hash, _navigationTransit ? 1 : 0);
            Hashing.Mix(ref hash, _abilityMovePenaltyUntilTick);

            Entities.HashInto(ref hash);

            // Статусы и кулдауны — такая же часть состояния, как позиции.
            // Не попади они в хеш, тест детерминизма перестал бы их проверять,
            // и расхождение в способностях жило бы незамеченным.
            Statuses.HashInto(ref hash, Entities.Count);

            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                Hashing.Mix(ref hash, _abilityReadyTick[slot]);
                _abilityBuilds[slot]?.HashInto(ref hash);
            }
            Hashing.Mix(ref hash, _whirlwindImpactTick);
            Hashing.Mix(ref hash, _whirlwindImpactSlot);
            // Прежние тик запуска тяги и точка броска Абордажа (до 02.10): постоянные
            // −1 и 0 на прежнем месте — закреплённые хеши сцен без Абордажа те же
            // бит в бит. Абордаж хеширует себя сам (HashAbordage).
            Hashing.Mix(ref hash, -1);
            Hashing.Mix(ref hash, 0L);
            Hashing.Mix(ref hash, 0L);

            // Цепочка прыжков переживает несколько тиков и решает, кого бить
            // следующим. Не попади она в хеш — реплей, начатый посреди цепочки,
            // сошёлся бы по позициям и разошёлся по целям.
            Hashing.Mix(ref hash, _chainHopsLeft);
            Hashing.Mix(ref hash, _chainTarget);
            Hashing.Mix(ref hash, _chainSlot);
            // Completed chains cannot affect another cast; only active visits are state.
            int visitedCount = _chainHopsLeft > 0 ? _chainVisitedCount : 0;
            Hashing.Mix(ref hash, visitedCount);
            for (int v = 0; v < visitedCount; v++) Hashing.Mix(ref hash, _chainVisited[v]);

            Rng.HashInto(ref hash);
            return hash;
        }
    }
}
