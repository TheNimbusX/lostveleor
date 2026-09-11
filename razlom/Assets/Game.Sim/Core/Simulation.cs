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
        /// От начала замаха до контакта у ВРАГА: 12 тиков = 400 мс.
        /// </summary>
        // Не трогать заодно с геройской цифрой. Это единственное окно, в
        // которое игрок видит занесённый удар и может уйти; укоротишь — и
        // враги начнут бить без предупреждения, а бой станет нечестным
        // ровно в том смысле, который игрок чувствует, но не формулирует.
        public const int EnemyAttackWindupTicks = 12;

        /// <summary>Замах той сущности, которая бьёт.</summary>
        public const int RootSwarmHealth = 30;
        public const int RootSwarmAttackWindupTicks = 9;
        public const int RootSwarmAttackCooldownTicks = 24;
        public static readonly Fix64 RootSwarmMoveSpeed = Fix64.Ratio(34, 10);
        public static readonly Fix64 RootSwarmRushSpeed = Fix64.FromInt(5);
        private static readonly Fix64 RootSwarmAttackRange = Fix64.Ratio(14, 10);

        private int WindupTicksFor(int entityId)
            => entityId == PlayerId ? AttackWindupTicks
                : Entities.Kind[entityId] == EnemyKind.ForestRootSwarm
                    ? RootSwarmAttackWindupTicks : EnemyAttackWindupTicks;

        private Fix64 AttackRangeFor(int entityId)
            => entityId == PlayerId ? PlayerAttackRange
                : Entities.Kind[entityId] == EnemyKind.ForestRootSwarm
                    ? RootSwarmAttackRange : AttackRange;

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
        private static readonly Fix64 PlayerTurnStep = Fix64.TwoPi / 18;
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
        private static readonly Fix64 PlayerAttackRangeSq = PlayerAttackRange * PlayerAttackRange;

        // Бить можно только вперёд. Косинус половины сектора: 0.5 — это 60°
        // в каждую сторону, фронтальный сектор в 120°.
        //
        // Сектор участвует в ВЫБОРЕ цели, а не проверяется после него: иначе
        // персонаж выбирал бы ближайшего врага за спиной и не бил бы никого,
        // стоя лицом ко второму.
        private static readonly Fix64 AttackArcCos = Fix64.Ratio(1, 2);

        // Цель можно выбрать в широком секторе 120°, но сам взмах начинается
        // только когда корпус уже почти смотрит на неё. 0.8 = примерно ±37°.
        // Раньше широкий сектор одновременно был и порогом старта: клип мог
        // начаться боком, а затем Damage проверял уже другое направление.
        private static readonly Fix64 AttackCommitCos = Fix64.Ratio(4, 5);

        // У моба сектор мягче, чем у игрока: его корпус доворачивается с
        // ограниченной скоростью, а уже начатый замах не должен превращаться
        // в пустой жест из-за пары кадров расталкивания. ±120° достаточно,
        // чтобы надёжно принять контакт во время доворота, но всё ещё не
        // превращает удар в полноценный круг вокруг тела.
        private static readonly Fix64 EnemyAttackArcCos = Fix64.Ratio(-1, 2);

        /// <summary>
        /// Круг целиком: годится любое направление. Только для поворота.
        ///
        /// ЧУТЬ НИЖЕ МИНУС ЕДИНИЦЫ, И ЭТО НЕ ОПЕЧАТКА. Ровно на -1 проверка
        /// сектора сравнивает dot² с |вектор до цели|², и для цели строго за
        /// спиной эти числа обязаны совпасть. В фиксированной точке они и
        /// совпадают лишь примерно: нормализованный взгляд бывает чуть длиннее
        /// единицы, dot² выходит на пару младших разрядов больше, и сравнение
        /// переворачивается. Цель ровно за спиной переставала находиться —
        /// именно тот случай, ради которого круг здесь и нужен.
        /// </summary>
        private static readonly Fix64 FullCircleCos = Fix64.Ratio(-101, 100);

        /// <summary>
        /// На сколько подходить к цели по приказу атаки. Чуть ближе дальности
        /// удара: встать ровно на границе значит выпадать из неё от любого
        /// толчка и начинать шагать туда-обратно.
        /// </summary>
        private static readonly Fix64 AttackReach = PlayerAttackRange * Fix64.Ratio(75, 100);
        private static readonly Fix64 AttackReachSq = AttackReach * AttackReach;
        private static readonly Fix64 AttackChainRadius = Fix64.Ratio(11, 4);
        private static readonly Fix64 HeavyCleaveArcCos = Fix64.Zero;
        private static readonly Fix64 HeavyPrimaryScale = Fix64.Ratio(5, 4);
        private static readonly Fix64 HeavySecondaryScale = Fix64.Ratio(4, 5);
        private static readonly Fix64 AbilityMoveScale = Fix64.Ratio(3, 4);
        private const int HeavyCleaveTargets = 2;
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
        private static readonly Fix64 EnemyBaseDamage  = Fix64.FromInt(7);

        // Скорость атаки — В АТАКАХ В СЕКУНДУ: только в этих единицах «+20%»
        // на предмете значит то, что игрок прочитает. В тики её переводит
        // CombatStats.AttackCooldownTicks, и делает это в единственном месте.
        // Базовый цикл героя — 20 тиков, врага — 36.
        private static readonly Fix64 PlayerBaseAttackSpeed = Fix64.Ratio(TicksPerSecond, PlayerBaseAttackCycleTicks);
        private static readonly Fix64 EnemyBaseAttackSpeed  = Fix64.Ratio(TicksPerSecond, 36);

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
        public static Fix64 AutoAttackRange => PlayerAttackRange;
        public static Fix64 AutoAttackArcCos => AttackArcCos;

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
        public readonly ProjectileStore Projectiles;
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

        private int _leapLaunchTick = -1;
        private FixVec2 _leapAim;
        public FixVec2 LeapAim => _leapAim;

        /// <summary>
        /// Кого цепляет Абордаж. -1 — цели нет, и тогда это бросок в точку.
        ///
        /// Способность выбрана врагом, но остаётся рабочей и без него: кнопка,
        /// молча не срабатывающая, когда игрок промахнулся курсором мимо тела,
        /// читается как поломка ввода.
        /// </summary>
        private int _leapTarget = -1;
        private int _leapPunchTick = -1;
        private int _leapSlot = -1;
        private int _abilityMovePenaltyUntilTick;

        // Состояние «Шага по цепи» между прыжками. Живёт в симуляции, а не в
        // способности: способность кончилась в момент каста, а цепочка идёт
        // ещё двадцать тиков. Входит в хеш — иначе реплей разъедется.
        private int _chainHopsLeft;
        private int _chainTarget = -1;
        public int ChainTargetId => _chainHopsLeft > 0 ? _chainTarget : -1;
        private int _chainSlot = -1;
        private readonly int[] _chainVisited = new int[AnchorKit.ChainMaxHops];
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

        // ---- приказ атаковать ----
        //
        // ПРИКАЗ БИТЬ ЖИВЁТ, ПОКА ЦЕЛЬ ЖИВА. Это то же управление жанра, что
        // и приказ идти: щёлкнул по врагу — персонаж сам подходит и бьёт,
        // пока тот не умрёт. Требовать удержания кнопки значит превращать
        // сотню тысяч ударов за сессию в сотню тысяч нажатий.
        private int _attackTarget = -1;
        private int _nextPlayerAttackVariant;

        /// <summary>Куда идёт игрок. Для отрисовки метки приказа.</summary>
        public bool TryGetMoveOrder(out FixVec2 target)
        {
            target = _moveOrder;
            return _hasMoveOrder;
        }

        /// <summary>Кого игрок бьёт по приказу, или -1. Для подсветки цели.</summary>
        public int AttackTarget => _attackTarget;

        /// <summary>Приказ живёт, только пока цель жива и остаётся врагом.</summary>
        private bool AttackTargetValid
            => _attackTarget > 0
               && _attackTarget < Entities.Count
               && Entities.Alive[_attackTarget]
               && Entities.Side[_attackTarget] != Entities.Side[PlayerId];

        public int Tick { get; private set; }
        public IReadOnlyList<SimEvent> Events => _events;

        public Simulation(ulong runSeed, int capacity = 512)
        {
            Rng = new RngStreams(runSeed);
            Entities = new EntityStore(capacity);
            _cleavePreviousPositions = new FixVec2[capacity];
            _cycloneHitTurn = new int[capacity];
            _cycloneTickPositions = new FixVec2[capacity];

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

            Projectiles = new ProjectileStore(capacity);
            Statuses = new StatusStore(capacity);
            HitScratch = new int[capacity];
            _separationScratch = new int[capacity];
            _crowdScratch = new int[capacity];
            _separationPush = new FixVec2[capacity];

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
            => Grid.QueryRadius(Entities, center, radius, exclude, HitScratch);

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
            Projectiles.Clear();
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
            Projectiles.Clear();
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
                    FixVec2 spot = RandomSpotInModule(map, placement, EnemyBodyRadius, ref rng);
                    if (map.Routes != null && !map.Routes.TrySafeSpawn(placement, spot, out spot)) continue;

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
        /// </summary>
        public void SetupForestEncounter(LayoutMap map, ulong spawnSeed, int guardianHealth)
        {
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
                int id = Entities.Spawn(spot, RootSwarmHealth, Faction.Orvill);
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
            Projectiles.Clear();
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
            Projectiles.Clear();
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
                int health = tier == CombatFeelCaptureTier.Kill && i == 0
                    ? Entities.Damage[PlayerId] - 1
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

            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
        }

        /// <summary>
        /// Базовые статы рядового врага. Здоровье приходит из Spawn: им тир
        /// Разлома и масштабирует сложность.
        /// </summary>
        private void ConfigureEnemy(int id, EnemyKind kind = EnemyKind.ForestGuardian)
        {
            Entities.Kind[id] = kind;
            bool swarm = kind == EnemyKind.ForestRootSwarm;
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
            Entities.BodyRadius[id] = swarm ? Fix64.Ratio(45, 100) : Fix64.Ratio(85, 100);
            Entities.PushWeight[id] = swarm ? Fix64.FromInt(2) : Fix64.One;

            StatSheet sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, swarm ? Fix64.FromInt(4) : EnemyBaseDamage);
            sheet.SetBase(StatType.AttackSpeed, swarm
                ? Fix64.Ratio(TicksPerSecond, RootSwarmAttackCooldownTicks) : EnemyBaseAttackSpeed);
            sheet.SetBase(StatType.MoveSpeed, swarm ? RootSwarmMoveSpeed : EnemyBaseMoveSpeed);
            sheet.SetBase(StatType.CritChance, swarm ? Fix64.Zero : BaseCritChance);
            sheet.SetBase(StatType.CritMultiplier, BaseCritMultiplier);

            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
        }

        /// <summary>
        /// Расстановка Полигона: игрок и два манекена.
        ///
        /// Мишень стоит в полутора метрах прямо перед игроком — дальность
        /// автоатаки два метра, а взгляд по умолчанию направлен по оси X.
        /// Спарринг стоит ЗА СПИНОЙ: он обязан бить, но не обязан попадать
        /// под автоатаку и портить замер, а фронтальный сектор его не видит.
        /// </summary>
        public void SetupProvingGround(int dummyHealth, Fix64 dummyArmor, Fix64 dummyFireResist)
        {
            ClearMoveOrder();
            Entities.Clear();
            Projectiles.Clear();
            Statuses.Clear();
            for (int i = 0; i < AbilitySlots; i++) _abilityReadyTick[i] = 0;
            ResetAbilityState();

            ConfigurePlayer(Entities.Spawn(FixVec2.Zero, PlayerBaseHealth, Faction.Wole));

            int dummy = Entities.Spawn(new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero),
                dummyHealth, Faction.Orvill);
            ConfigureDummy(dummy, dummyArmor, dummyFireResist);
            _events.Add(SimEvent.Spawn(dummy, Entities.Position[dummy]));

            int sparring = Entities.Spawn(new FixVec2(-Fix64.Ratio(3, 2), Fix64.Zero),
                EnemyBaseHealth * 100, Faction.Orvill);
            ConfigureEnemy(sparring);

            // Спарринг тоже стоит на месте: Полигон меряет билд, а не догонялки.
            Entities.PushWeight[sparring] = Fix64.Zero;
            Entities.Stats[sparring].SetBase(StatType.MoveSpeed, Fix64.Zero);
            Entities.RefreshStats(sparring);
            _events.Add(SimEvent.Spawn(sparring, Entities.Position[sparring]));
        }

        /// <summary>
        /// Манекен-мишень: не ходит, не бьёт, но имеет настраиваемые защиты.
        ///
        /// И неподвижность, и молчание сделаны СТАТАМИ, а не флагом «манекен»:
        /// нулевая скорость движения — это нулевой шаг, нулевая скорость атаки —
        /// потолок кулдауна. Отдельного режима в бою заводить не пришлось,
        /// а значит, и ломаться в бою нечему.
        /// </summary>
        private void ConfigureDummy(int id, Fix64 armor, Fix64 fireResist)
        {
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
        /// heal нужен на входе в Разлом: игрок и так входит с полным здоровьем,
        /// и прибавка к максимуму от надетой вещи иначе осталась бы пустой
        /// строчкой в описании.
        /// </summary>
        public void RefreshPlayerStats(bool heal)
        {
            Entities.RefreshStats(PlayerId);
            if (heal) Entities.Health[PlayerId] = Entities.MaxHealth[PlayerId];
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
            for (int i = 0; i < count; i++) _separationPush[i] = FixVec2.Zero;

            for (int i = 0; i < count; i++)
            {
                if (!Entities.Alive[i]) continue;

                Fix64 reach = Entities.BodyRadius[i] + EntityStore.MaxBodyRadius;
                int found = Grid.QueryRadius(Entities, Entities.Position[i], reach, i,
                    _separationScratch);

                for (int k = 0; k < found; k++)
                {
                    int j = _separationScratch[k];

                    // Каждая пара обрабатывается ровно один раз, младшим индексом.
                    if (j <= i) continue;

                    Fix64 wanted = Entities.BodyRadius[i] + Entities.BodyRadius[j];
                    FixVec2 delta = Entities.Position[j] - Entities.Position[i];
                    Fix64 distSq = delta.LengthSq;
                    if (distSq >= wanted * wanted) continue;

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
                    if (total.Raw <= 0) continue;

                    Fix64 shareI = Entities.PushWeight[i] / total;
                    Fix64 shareJ = Entities.PushWeight[j] / total;

                    _separationPush[i] -= direction * (overlap * shareI);
                    _separationPush[j] += direction * (overlap * shareJ);
                }
            }

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

        private void ResetAbilityState()
        {
            StopCyclone();
            StopAnchorSlam();
            _whirlwindImpactTick = _whirlwindImpactSlot = -1;
            _leapLaunchTick = -1;
            _leapAim = FixVec2.Zero;
            _leapTarget = _leapPunchTick = _leapSlot = -1;
            _chainHopsLeft = _chainVisitedCount = 0;
            _chainTarget = _chainSlot = -1;
            System.Array.Clear(_chainVisited, 0, _chainVisited.Length);
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
            _attackTarget = -1;
            _abilityMovePenaltyUntilTick = 0;
            _nextPlayerAttackVariant = 0;
        }

        /// <summary>
        /// Разбирает приказы игрока на этот тик.
        ///
        /// Порядок важен: явный приказ по земле отменяет автоповтор, но не
        /// стирает уже начатый замах. Управление и committed-контакт могут
        /// сосуществовать, пока проверка попадания остаётся честной.
        ///
        /// ДВИЖЕНИЕ И АТАКА — РАЗНЫЕ КНОПКИ, ЗНАЧИТ РАЗНЫЕ ВЕТКИ. Пока обе
        /// команды приходили с одной ПКМ, они не могли встретиться в одном
        /// тике, и здесь стояла развилка if/else. Теперь ПКМ ведёт, а ЛКМ
        /// бьёт, и зажать их вместе — обычное дело: игрок отходит, продолжая
        /// махать. Развилка молча съедала бы приказ идти.
        /// </summary>
        private void ReadOrders(in InputFrame input)
        {
            // ПРИКАЗ ИДТИ ЧИТАЕТСЯ ПЕРВЫМ И СИЛЬНЕЕ АВТОЦЕЛИ. Защёлкнутая цель
            // ниже по коду подменяет точку движения собой (герой идёт к телу,
            // а не к курсору), поэтому оставить её живой значит проигнорировать
            // ПКМ. Игрок, который держит ЛКМ и уводит героя, получает ровно то,
            // что просил: шаг туда, куда указал, и удар по тому, кто оказался
            // перед носом.
            bool moveOrdered = input.Has(InputFlags.MoveOrder);
            if (moveOrdered) _attackTarget = -1;

            if (!moveOrdered && input.Has(InputFlags.Attack) && input.HasAttackTarget)
            {
                int target = input.AttackTarget;

                // Цель принимается, только если она вообще может быть целью.
                // Проверяет это симуляция, а не представление: представление
                // видит картинку прошлого кадра и может ошибиться.
                if (target < Entities.Count
                    && Entities.Alive[target]
                    && Entities.Side[target] != Entities.Side[PlayerId])
                {
                    _attackTarget = target;
                }
            }

            if (!AttackTargetValid) _attackTarget = -1;
        }

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
        public void Step(in InputFrame input)
        {
            _events.Clear();
            for (int i = 0; i < Entities.Count; i++) _cycloneTickPositions[i] = Entities.Position[i];

            // Пересчёт грязных листов статов — первой стадией и ровно один раз
            // за тик. У StatSheet пересчёт по грязному флагу, и точка, в которой
            // он случается, обязана быть одной и той же во всех прогонах: поймай
            // его случайным первым Get посреди боя — и результат начнёт зависеть
            // от того, кто первым до кого дотянулся.
            RefreshDirtyStats();

            // Приказы разбираются до движения: цель могла умереть на прошлом
            // тике, и идти к трупу персонаж не должен.
            ReadOrders(in input);

            // Штраф движения начинается в кадр нажатия способности, хотя
            // gameplay-каст разрешается ниже по фиксированному порядку стадий.
            PrimeAbilityMovePenalty(in input);

            // Принудительное перемещение решается ДО собственного движения:
            // тело, которое тащат, своим шагом не идёт, и порядок здесь — это
            // и есть правило приоритета, а не деталь реализации.
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
            Grid.Rebuild(Entities);

            // Порядок стадий боя зафиксирован. Любой другой был бы столь же
            // корректен, но менять его нельзя: он входит в поведение и хеш.
            ResolveAbilityCasts(in input);
            ResolveWhirlwindImpact();
            UpdateCyclone(in input);
            UpdateAnchorSlam();
            UpdateWreck();
            UpdateCleave();
            UpdateFlask();
            if (_leapLaunchTick >= 0 && Tick >= _leapLaunchTick)
            {
                _leapLaunchTick = -1;
                if (Entities.Alive[PlayerId])
                {
                    int ticks = AnchorKit.CastBoarding(this, _leapAim, _leapTarget);
                    _leapPunchTick = Tick + ticks;
                }
            }
            ResolveBoardingPunch();
            ContinueChainStep();
            UpdateProjectiles();
            ResolveAttacks(in input);
            TickBurning();

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
                    && _wreckSlot == slot && WreckComboOpen)
                {
                    AdvanceWreck(input.Aim);
                    continue;
                }

                if (Tick < _abilityReadyTick[slot]) continue;

                if (build.DefinitionId == AbilityDefinition.ChainStepId && !ValidAbilityTarget(input.AbilityTarget, build)) continue;
                StopCyclone();
                StopAnchorSlam();
                StopWreck();
                StopCleave();
                StopFlask();
                // A newly committed action replaces the old presentation and
                // its unlanded contacts. Do not launch an old anchor midway
                // through the next ability's animation.
                _leapLaunchTick = -1;
                _whirlwindImpactTick = _whirlwindImpactSlot = -1;
                _chainHopsLeft = 0;
                _chainVisitedCount = 0;
                ForcedMotion.Clear(Entities, PlayerId);

                if (build.DefinitionId == AbilityDefinition.WhirlwindId)
                {
                    _whirlwindImpactTick = Tick + WhirlwindContactDelayTicks;
                    _whirlwindImpactSlot = slot;
                }
                else if (build.DefinitionId == AbilityDefinition.AnchorLeapId)
                {
                    _leapAim = input.Aim;
                    _leapTarget = ValidAbilityTarget(input.AbilityTarget, build)
                        ? input.AbilityTarget
                        : -1;
                    _leapSlot = slot;
                    _leapPunchTick = -1;
                    _leapLaunchTick = Tick + AnchorKit.LeapWindupTicks;
                }
                else if (build.DefinitionId == AbilityDefinition.AnchorSlamId)
                {
                    BeginAnchorSlam(slot, input.Aim);
                }
                else if (build.DefinitionId == AbilityDefinition.ChainCycloneId)
                {
                    BeginCyclone(slot, input.Aim);
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
                else if (build.DefinitionId == AbilityDefinition.DashId)
                {
                    CastDash(slot, input.Aim);
                }
                else
                {
                    FlameSeal.Cast(this, PlayerId, slot, build, input.Aim);
                }

                _abilityReadyTick[slot] = Tick + build.CooldownTicks;
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
                if (_abilityBuilds[slot].DefinitionId == AbilityDefinition.ChainStepId && !ValidAbilityTarget(input.AbilityTarget, _abilityBuilds[slot])) continue;
                if (Tick < _abilityReadyTick[slot]) continue;

                int until = (_abilityBuilds[slot].DefinitionId == AbilityDefinition.ChainCycloneId)
                    ? Tick : Tick + AbilityMovePenaltyTicks;
                if (until > _abilityMovePenaltyUntilTick)
                    _abilityMovePenaltyUntilTick = until;

                // Способность имеет приоритет над незавершённой автоатакой.
                // View в тот же кадр убирает upper-body swing; скрытого урона
                // от уже не показываемого клинка оставаться не должно.
                Entities.PendingAttackTarget[PlayerId] = -1;
                Entities.AttackImpactTick[PlayerId] = 0;
                Entities.PendingAttackVariant[PlayerId] = 0;
                _nextPlayerAttackVariant = 0;
                return;
            }
        }

        /// <summary>
        /// Единственный момент нанесения урона «Вихрем». View получает обычные
        /// Damage/Death events и уже от них показывает весь impact.
        /// </summary>
        /// <summary>
        /// ПОДСЕЧКА. Волочит и бьёт тем же тиком.
        ///
        /// Урон сразу, а не по приезде: цель, которую тащат, уже поймана, и
        /// ждать конца волока значило бы, что убитый по дороге враг не
        /// получает урона от способности, которая его и убила.
        /// </summary>
        private void BeginChainStep(int slot, int target)
        {
            if (target < 0) return;

            _chainSlot = slot;
            _chainHopsLeft = AnchorKit.ChainMaxHops;
            _chainTarget = target;
            _chainVisitedCount = 1;
            _chainVisited[0] = target;
            ForcedMotion.Begin(Entities, PlayerId,
                AnchorKit.ChainLandingSpot(Entities, target),
                AnchorKit.ChainTicksPerHop, ForcedMotionKind.Lunge);
        }

        /// <summary>
        /// Долетел — ударил — выбрал следующего.
        ///
        /// Цепочка НЕ считается вперёд намеренно: к третьему прыжку заранее
        /// выбранные цели оказываются трупами, убитыми предыдущими прыжками
        /// той же способности. Каждый следующий выбирается из живых.
        /// </summary>
        private void ContinueChainStep()
        {
            if (_chainHopsLeft <= 0) return;
            if (ForcedMotion.IsActive(Entities, PlayerId)) return;

            if (!Entities.Alive[PlayerId])
            {
                _chainHopsLeft = 0;
                return;
            }

            AbilityBuild build = _chainSlot >= 0 && _chainSlot < AbilitySlots
                ? _abilityBuilds[_chainSlot]
                : null;
            if (build == null)
            {
                _chainHopsLeft = 0;
                return;
            }

            if (_chainTarget >= 0 && _chainTarget < Entities.Count
                && Entities.Alive[_chainTarget]
                && Entities.Side[_chainTarget] != Entities.Side[PlayerId]
                && ChainContactReachable(_chainTarget))
            {
                ApplyAbilityDamage(PlayerId, _chainTarget,
                    build.Get(AbilityStatType.Damage).ToInt(), _chainSlot,
                    DamageType.Physical);
            }

            _chainHopsLeft--;
            if (_chainHopsLeft <= 0)
            {
                EmitChainHop();
                _chainTarget = -1;
                _chainSlot = -1;
                return;
            }

            int next = AnchorKit.PickChainTarget(this, HitScratch, build.Get(AbilityStatType.Radius), _chainVisited, _chainVisitedCount);
            if (next < 0)
            {
                // Больше некого — цепочка кончается тихо. Оставшиеся прыжки
                // не переносятся: способность про перемещение между целями,
                // а не про число ударов.
                _chainHopsLeft = 0;
                EmitChainHop();
                _chainTarget = -1;
                _chainSlot = -1;
                return;
            }

            bool repeatTarget = next == _chainTarget;
            _chainTarget = next;
            _chainVisited[_chainVisitedCount++] = next;
            ForcedMotion.Begin(Entities, PlayerId,
                AnchorKit.ChainLandingSpot(Entities, next, repeatTarget),
                AnchorKit.ChainTicksPerHop, ForcedMotionKind.Lunge);
            EmitChainHop();
        }

        private void EmitChainHop()
        {
            // Старт движения существует даже при смерти цели до контакта.
            // Анимация не должна зависеть от наличия события урона.
            _events.Add(SimEvent.ChainHop(PlayerId, _chainTarget, _chainHopsLeft,
                _chainVisitedCount - 1, Entities.Position[PlayerId]));
        }

        private void ResolveWhirlwindImpact()
        {
            if (_whirlwindImpactTick < 0 || Tick < _whirlwindImpactTick) return;

            int slot = _whirlwindImpactSlot;
            _whirlwindImpactTick = -1;
            _whirlwindImpactSlot = -1;
            _chainHopsLeft = 0;
            _chainTarget = -1;
            _chainSlot = -1;

            if (!Entities.Alive[PlayerId]) return;
            AbilityBuild build = slot >= 0 && slot < AbilitySlots ? _abilityBuilds[slot] : null;
            if (build == null || build.DefinitionId != AbilityDefinition.WhirlwindId) return;

            int found = QueryRadiusIntoScratch(
                Entities.Position[PlayerId], build.Get(AbilityStatType.Radius), PlayerId);
            int damage = build.Get(AbilityStatType.Damage).ToInt();

            for (int i = 0; i < found; i++)
            {
                int target = HitScratch[i];
                if (!Entities.Alive[target]) continue;
                if (Entities.Side[target] == Entities.Side[PlayerId]) continue;
                ApplyAbilityDamage(PlayerId, target, damage, slot, DamageType.Physical);
            }
        }

        /// <summary>
        /// Полёт снарядов и стадия ПРИ ПОПАДАНИИ.
        ///
        /// Обход по возрастанию индекса, как и везде: от порядка попаданий
        /// зависит, кто умрёт первым при равном здоровье.
        /// </summary>
        private void UpdateProjectiles()
        {
            for (int i = 0; i < Projectiles.HighWater; i++)
            {
                if (!Projectiles.Alive[i]) continue;

                FixVec2 toTarget = Projectiles.Target[i] - Projectiles.Position[i];
                FixVec2 step = Projectiles.Velocity[i];

                bool arrived = toTarget.LengthSq <= step.LengthSq;
                Projectiles.Position[i] = arrived ? Projectiles.Target[i] : Projectiles.Position[i] + step;

                Projectiles.TicksLeft[i]--;

                // Снаряд, не долетевший за отведённое время, всё равно срабатывает:
                // тихо исчезнувший снаряд игрок читает как проглоченный ввод.
                if (!arrived && Projectiles.TicksLeft[i] > 0) continue;

                AbilityBuild build = _abilityBuilds[Projectiles.Slot[i]];
                if (build != null) FlameSeal.OnHit(this, i, build);

                Projectiles.Despawn(i);
            }
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
            if (target == PlayerId && PlayerInvulnerable) return;
            if (BlazeEvades(target, overTime)) return;

            amount = CombatStats.Mitigate(amount, type,
                Entities.Armor[target], Entities.FireResist[target]);

            Entities.Health[target] -= amount;
            _events.Add(overTime
                ? SimEvent.DamageOverTime(source, target, amount, Entities.Position[target], type)
                : SimEvent.Damage(source, target, amount, false, Entities.Position[target], type,
                    DamageOrigin.Ability, slot));

            if (Entities.Health[target] > 0)
            {
                // Добавка огнём идёт ПОСЛЕ основного урона и только по живому:
                // добивать уже мёртвого вторым ударом значило бы порождать
                // лишние события смерти.
                if (!overTime) ApplyBlazeBonus(source, target, slot);
                return;
            }
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
            bool killedOrderedTarget = killer == PlayerId && target == _attackTarget;
            Entities.Health[target] = 0;
            Entities.Alive[target] = false;
            if (target == PlayerId) ResetAbilityState();
            _events.Add(SimEvent.Death(target, Entities.Position[target]));

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

            if (killedOrderedTarget)
            {
                // Автопереход остаётся локальным внутри текущей пачки и только
                // во фронтальной полусфере: приказ не превращается в автопилот,
                // который сам пересекает комнату за игрока.
                _attackTarget = Grid.FindNearestEnemy(Entities, PlayerId,
                    AttackChainRadius, Fix64.Zero);
            }

            AbilityBuild build = slot >= 0 && slot < AbilitySlots ? _abilityBuilds[slot] : null;
            if (build != null)
            {
                int count = build.EffectCount(AbilityStage.OnKill);
                for (int e = 0; e < count; e++)
                {
                    switch (build.GetEffect(AbilityStage.OnKill, e))
                    {
                        case AbilityEffect.SpreadBurn:
                            FlameSeal.SpreadBurn(this, target, killer, slot);
                            break;
                    }
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
            if (!Entities.Alive[PlayerId])
            {
                ClearMoveOrder();
                return;
            }

            if (AnchorSlamActive)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                Entities.Facing[PlayerId] = PlayerFacingStep(Entities.Facing[PlayerId], _slamDirection);
                return;
            }

            if (CleaveActive)
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

            // Новый приказ перебивает старый. При удержании кнопки он приходит
            // каждый тик и точка едет за курсором — это то же самое поведение,
            // что и раньше, просто теперь оно частный случай.
            //
            // Проверки !AttackTargetValid здесь больше нет: ReadOrders снимает
            // автоцель в тот же тик, в котором пришёл явный приказ идти, и
            // условие стало не защитой, а вторым местом, где то же правило
            // записано другими словами.
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

            // Only the anchor leap needs a stationary launch. The mass hook
            // preserves locomotion while its upper body performs the throw.
            if (_leapLaunchTick >= 0)
            {
                Entities.Velocity[PlayerId] = FixVec2.Zero;
                return;
            }

            int committedTarget = Entities.PendingAttackTarget[PlayerId];
            bool committedTargetValid = committedTarget > 0
                                        && committedTarget < Entities.Count
                                        && Entities.Alive[committedTarget]
                                        && Entities.Side[committedTarget] != Entities.Side[PlayerId];

            // В committed windup и во время способности управление остаётся,
            // но максимальная скорость составляет 50%. Recovery/cooldown
            // штрафа не дают: после контакта герой снова ускоряется полностью.
            bool combatMovePenalty = committedTargetValid
                                     || Tick < _abilityMovePenaltyUntilTick;

            // Есть цель — идём к ней, а не к точке клика. Останавливаемся,
            // не доходя вплотную: подойти впритык значит упереться телом
            // и топтаться, пока расталкивание разводит тела.
            if (AttackTargetValid)
            {
                _moveOrder = Entities.Position[_attackTarget];
                _hasMoveOrder = FixVec2.DistanceSq(Entities.Position[PlayerId], _moveOrder)
                                > AttackReachSq;
                _explicitMoveOrder = false;
            }

            FixVec2 pos = Entities.Position[PlayerId];
            FixVec2 step = FixVec2.Zero;
            FixVec2 desiredFacing = FixVec2.Zero;
            bool finishingTurnInPlace = false;

            // Боевой доворот живёт независимо от locomotion-order. Раньше при
            // входе в AttackReach _hasMoveOrder становился false, desiredFacing
            // оставался нулём, и герой сохранял старое направление: отсюда
            // удары мимо цели и необъяснимое молчание прямо рядом с ней.
            bool attacking = input.Has(InputFlags.Attack);

            // К кому разворачиваться, пока кнопка зажата, а цель курсором не
            // назначена.
            //
            // КРУГ ЦЕЛИКОМ, А НЕ ЛОБОВОЙ СЕКТОР. Это ПОВОРОТ, а не удар, и
            // сектор ему только мешал: отошёл от врага, нажал бить — враг
            // остался за спиной, в сектор не попадал, корпус не разворачивался,
            // и кнопка молчала до тех пор, пока не встанешь к нему лицом
            // вручную. Стоя на месте всё работало, и разница выглядела
            // необъяснимой.
            //
            // Старое правило «за спину не бьём» при этом цело: удар по-прежнему
            // начинается только в лобовом секторе и проверяется на контакте.
            // Здесь герой лишь поворачивается к тому, кто рядом, — 20° за тик,
            // то есть полный разворот занимает девять тиков, ровно один замах.
            int swingTarget = attacking && !committedTargetValid && !AttackTargetValid
                ? FindTurnTarget()
                : -1;

            // ПОКА КНОПКА ЗАЖАТА, БОЕВОЙ ДОВОРОТ СИЛЬНЕЕ ПРИКАЗА ИДТИ.
            //
            // Приказ идти забирал направление корпуса себе целиком, и на бегу
            // с зажатой ЛКМ удар почти никогда не проходил: тело смотрит туда,
            // куда бежишь, окно старта взмаха узкое, и пробегающий мимо враг
            // успевал выйти из него за пару тиков. Игрок при этом видел, что
            // кнопка нажата, и не понимал, почему герой молчит.
            //
            // Бежать это не мешает: приказ движения не трогается, меняется
            // только то, куда развёрнут корпус. Отпустил кнопку — снова
            // смотришь, куда бежишь.
            int facingTarget = committedTargetValid
                ? committedTarget
                : AttackTargetValid
                    ? _attackTarget
                    : swingTarget;
            if (!attacking && _hasMoveOrder && _explicitMoveOrder) facingTarget = -1;

            if (facingTarget >= 0)
                desiredFacing = Entities.Position[facingTarget] - pos;

            // ЗАЖАТАЯ АТАКА САМА РАЗВОРАЧИВАЕТ ГЕРОЯ НА КУРСОР.
            //
            // Без этого удержание ЛКМ по пустому месту не делало НИЧЕГО:
            // цели нет, приказа идти нет, значит desiredFacing оставался нулём,
            // герой стоял как стоял, а выбор цели идёт в узком секторе ±37° от
            // корпуса. Враг в полушаге сбоку — и кнопка молчит, хотя игрок её
            // держит. Пока команда была одна, это не всплывало: ПКМ по земле
            // всегда задавала и направление тоже.
            //
            // Курсор здесь — именно направление удара, а не точка назначения:
            // шага он не вызывает, потому что _hasMoveOrder не трогается.
            else if (!_hasMoveOrder && attacking)
                desiredFacing = input.Aim - pos;

            // Шаг за тик приходит из листа статов: скорость передвижения —
            // такой же стат, как урон, и предмет вправе её менять. Активное
            // действие меняет только текущий cap, но не сам стат.
            Fix64 fullSpeed = Entities.MoveStep[PlayerId];
            Fix64 speed = committedTargetValid
                ? fullSpeed * Fix64.Half
                : Tick < _abilityMovePenaltyUntilTick
                    ? fullSpeed * AbilityMoveScale
                    : fullSpeed;
            if (TryCycloneMoveScale(in input, out Fix64 cycloneMoveScale)) speed = fullSpeed * cycloneMoveScale;

            if (_hasMoveOrder)
            {
                FixVec2 toTarget = _moveOrder - pos;
                Fix64 distSq = toTarget.LengthSq;

                // Смотрим на указанную точку, если корпус не занят боем.
                //
                // Здесь стояло `_explicitMoveOrder || facingTarget < 0`, и это
                // перетирало боевой доворот, выбранный выше: на бегу с зажатой
                // атакой герой снова разворачивался по направлению движения, и
                // цель уходила из окна старта взмаха. Приоритет боя над
                // направлением бега решается ОДИН РАЗ, в facingTarget.
                if (facingTarget < 0) desiredFacing = toTarget;

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
                Fix64 arrivalSq = navigating ? Fix64.Ratio(1,100) : TurnInPlaceRadiusSq;
                if (distSq > arrivalSq)
                {
                    Fix64 distance = Fix64.Sqrt(distSq);

                    step = _navigationTransit
                        // Транзитный угол проходится насквозь на полной
                        // скорости: за ним дорога продолжается, и тормозить
                        // перед ним не перед чем.
                        ? toTarget / distance * speed
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

            // Желаемая скорость достигается не сразу: разгон и торможение
            // и есть тот вес, из-за отсутствия которого движение читалось
            // как перестановка фишки.
            FixVec2 velocity = Approach(Entities.Velocity[PlayerId], step, fullSpeed)
                .ClampLength(speed);

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

            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i]) continue;

                if (Statuses.IsStunned(i, Tick))
                { Entities.Velocity[i] = FixVec2.Zero; continue; }

                // Волочимый враг не идёт своим ходом. Иначе он приезжал бы
                // вдвое быстрее задуманного — тяга плюс собственный шаг к
                // игроку складывались бы, и Подсечка била бы сильнее, чем
                // написано на листе.
                if (ForcedMotion.IsActive(Entities, i)) continue;

                if (!playerAlive) { Entities.Velocity[i] = FixVec2.Zero; continue; }

                FixVec2 toPlayer = playerPos - Entities.Position[i];

                // Разворот идёт ВСЕГДА, даже до того как враг решил погнаться:
                // тело следит взглядом за игроком, а погоня — отдельное,
                // не мгновенное решение (см. UpdateAggro). Разворот идёт и
                // когда враг уже подошёл вплотную и стоит: добежав, он должен
                // доворачиваться к цели, а не замирать боком.
                Entities.Facing[i] = TurnToward(Entities.Facing[i], toPlayer,
                    EnemyTurnStepCos, EnemyTurnStepSin);

                if (!UpdateAggro(i, toPlayer))
                {
                    // Ещё не заметил — стоит на месте, а не бежит вслепую
                    // через весь Разлом с той секунды, как игрок вошёл.
                    Entities.Velocity[i] = FixVec2.Zero;
                    continue;
                }

                Fix64 speed = Entities.MoveStep[i];
                bool swarm = Entities.Kind[i] == EnemyKind.ForestRootSwarm;
                Fix64 attackRange = AttackRangeFor(i);
                if (swarm && toPlayer.LengthSq <= Fix64.FromInt(4))
                    speed = speed * RootSwarmRushSpeed / RootSwarmMoveSpeed;

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
                FixVec2 wanted;
                if (toPlayer.LengthSq <= attackRange * attackRange)
                {
                    wanted = FixVec2.Zero;
                }
                else if (!swarm && toPlayer.LengthSq <= ApproachBrakeRangeSq)
                {
                    // ПОДЪЕЗД, А НЕ РЫВОК. Здесь стояла та же ветка, что и для
                    // дальнего хода: шаг на полной скорости.
                    //
                    // Отсюда и «дрыгаются». У стоящего вплотную моба скорость
                    // ноль, но расталкивание отодвигает его на пять сантиметров
                    // за тик — и он мгновенно оказывается за границей удара.
                    // Полный ход возвращал его внутрь за пару тиков, там он
                    // снова тормозил в ноль, сосед снова выпихивал. Кольцо у
                    // игрока кипело: разгон, торможение, толчок, разгон.
                    //
                    // Теперь у самой границы желаемая скорость падает до нуля
                    // линейно. Выпихнутый на сантиметр возвращается сантиметром,
                    // а не разбегом: колебание гаснет само, вместо того чтобы
                    // подпитывать себя.
                    Fix64 distance = Fix64.Sqrt(toPlayer.LengthSq);
                    Fix64 slack = ApproachBrakeRange - AttackRange;
                    Fix64 ramp = (distance - AttackRange) / slack;
                    // A solo enemy must cross the range boundary, not approach it asymptotically.
                    if (ramp < Fix64.Ratio(1, 10)) ramp = Fix64.Ratio(1, 10);
                    wanted = toPlayer.Normalized() * (speed * ramp);
                }
                else if (BlockedByCloserAlly(i, playerPos, toPlayer, out FixVec2 blocker))
                {
                    // ОБХОДИМ, А НЕ ВСТАЁМ В ОЧЕРЕДЬ. Останавливаться за спиной
                    // соседа — значит выстроить колонну к игроку, и владелец
                    // так и сказал: «стоят в очереди, чтобы меня ударить».
                    //
                    // Вместо этого шаг вбок вдоль кольца. Сторона выбирается по
                    // знаку векторного произведения — та, куда ближе обходить, —
                    // и это чистая арифметика, одинаковая на всех машинах.
                    FixVec2 side = new FixVec2(-toPlayer.Y, toPlayer.X).Normalized();
                    Fix64 cross = toPlayer.X * blocker.Y - toPlayer.Y * blocker.X;
                    if (cross.Raw > 0) side = new FixVec2(-side.X, -side.Y);

                    // ОБХОД ПО ДУГЕ, А НЕ СТРОГО ВБОК. Чистый боковой шаг — это
                    // поворот желаемой скорости ровно на 90°, а проверка «занято
                    // ли место» переключается туда-обратно от тика к тику, пока
                    // толпа шевелится. Каждое переключение разворачивало ход на
                    // прямой угол, и Approach отрабатывал его три тика — те же
                    // рывки, только по другой причине, чем у границы удара.
                    //
                    // Подмешанный ход к игроку уменьшает скачок с 90° примерно
                    // до 55°, и остаток съедает разгон. Кольцо по-прежнему
                    // собирается — вбок тянет сильнее, — но перестаёт трястись.
                    FixVec2 arc = side * ArcSideShare + toPlayer.Normalized() * ArcForwardShare;
                    wanted = arc.Normalized() * (speed * CircleAroundScale);
                }
                else
                {
                    wanted = toPlayer.Normalized() * speed;
                }

                Entities.Velocity[i] = Approach(Entities.Velocity[i], wanted, speed);
                FixVec2 from = Entities.Position[i];
                FixVec2 moved = MoveInsideLayout(i, from, Entities.Velocity[i]);
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

            if (toPlayer.LengthSq > EnemyDetectRangeSq)
            {
                Entities.NoticeTick[i] = -1;
                return false;
            }

            if (Entities.NoticeTick[i] < 0)
                Entities.NoticeTick[i] = Tick + Rng.Ai.NextInt(EnemyNoticeMinTicks, EnemyNoticeMaxTicks + 1);

            if (Tick < Entities.NoticeTick[i]) return false;

            Entities.Aggro[i] = true;
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

                FixVec2 from = Entities.Position[i];
                FixVec2 delta = Entities.ForcedTarget[i] - from;

                // Делим остаток пути на остаток тиков. Целочисленное деление
                // Fix64 округляет вниз, поэтому на последнем тике шаг берётся
                // целиком — иначе тело вечно не доезжало бы последние миллиметры.
                FixVec2 step = left <= 1 ? delta : delta / Fix64.FromInt(left);

                // Большой шаг рывка не должен перескочить узкую стену между концами.
                int substeps = System.Math.Max(1, (step.Length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
                FixVec2 piece = step / Fix64.FromInt(substeps);
                for (int s = 0; s < substeps; s++)
                    from = MoveInsideLayout(i, from, piece);
                Entities.Position[i] = from;

                // Скорость обнуляется намеренно: тело едет не своим ходом, и
                // представление обязано видеть это как перемещение чужой волей,
                // а не как бег. Иначе у волочимого врага играла бы анимация бега.
                Entities.Velocity[i] = FixVec2.Zero;

                Entities.ForcedTicksLeft[i] = left - 1;
                if (Entities.ForcedTicksLeft[i] <= 0) ForcedMotion.Clear(Entities, i);
            }
        }

        private FixVec2 MoveInsideLayout(int entity, FixVec2 from, FixVec2 delta)
        {
            if ((_layout == null && _campWalkMap == null) || delta.LengthSq.Raw == 0) return from + delta;

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
            => _campWalkMap != null ? _campWalkMap.CanTravel(from, to) : _layout.IsWalkable(to, radius);

        private bool ChainContactReachable(int target)
        {
            FixVec2 from = Entities.Position[PlayerId];
            FixVec2 delta = Entities.Position[target] - from;
            Fix64 reach = AnchorKit.ChainStandoff + Entities.BodyRadius[target] + Entities.BodyRadius[PlayerId];
            if (delta.LengthSq > reach * reach) return false;
            if (_campWalkMap != null) return _campWalkMap.CanTravel(from, Entities.Position[target]);
            if (_layout == null) return true;
            int steps = System.Math.Max(1, (delta.Length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
            for (int i = 1; i <= steps; i++)
                if (!_layout.IsWalkable(from + delta * Fix64.Ratio(i, steps), Fix64.Zero)) return false;
            return true;
        }

        /// <summary>
        /// Автоатака. Каждая живая сущность ищет ближайшую цель чужой стороны
        /// в радиусе удара. Порядок обхода строго по индексу — от него зависит,
        /// кто ударит первым при равных условиях, и он обязан быть стабильным.
        /// </summary>
        private void ResolveAttacks(in InputFrame input)
        {
            // Игрок бьёт либо пока держит кнопку, либо пока жив тот, кого он
            // назначил целью. Второе и есть автоатака: щёлкнул один раз —
            // персонаж бьёт, пока цель не кончится.
            bool playerAttacks = input.Has(InputFlags.Attack) || AttackTargetValid;

            for (int i = 0; i < Entities.Count; i++)
            {
                if (Statuses.IsStunned(i, Tick)) continue;
                int pendingTarget = Entities.PendingAttackTarget[i];
                if (pendingTarget >= 0)
                {
                    if (Tick >= Entities.AttackImpactTick[i])
                    {
                        int variant = Entities.PendingAttackVariant[i];
                        Entities.PendingAttackTarget[i] = -1;
                        Entities.AttackImpactTick[i] = 0;
                        Entities.PendingAttackVariant[i] = 0;
                        if (CanLandAttack(i, pendingTarget))
                        {
                            ApplyAttack(i, pendingTarget, variant,
                                i == PlayerId && variant == 1 ? HeavyPrimaryScale : Fix64.One);
                            if (i == PlayerId && variant == 1)
                                ApplyHeavyCleave(pendingTarget);
                        }
                    }
                    continue;
                }

                if (!Entities.Alive[i]) continue;
                if (Tick < Entities.NextAttackTick[i]) continue;

                // Одна активная способность — одно читаемое действие. Приказ
                // атаки живёт и возобновится после action-window, но второй
                // клип и второй контакт поверх способности не запускаются.
                if (i == PlayerId && (AnchorSlamActive || CycloneActive || CleaveActive || Tick < _abilityMovePenaltyUntilTick
                    || _leapLaunchTick >= 0 || Entities.ForcedTicksLeft[i] > 0)) continue;

                // Игрок бьёт только по приказу. Враги — сами: у них нет игрока,
                // который решал бы за них, и решать за них должен ИИ.
                if (i == PlayerId && !playerAttacks) continue;

                int target = i == PlayerId && AttackTargetValid
                    ? ChosenTarget()
                    : FindNearestEnemy(i);

                // ЗАЖАТАЯ КНОПКА ОБЯЗАНА ДАВАТЬ ВЗМАХ, ДАЖЕ ЕСЛИ БИТЬ НЕКОГО.
                //
                // Пустой взмах — это ответ на нажатие, а не удар: он тратит
                // такт атаки и играет клип, но никого не назначает целью и
                // потому никому не наносит урона. Молчащая кнопка читается как
                // залипание ввода, и владелец назвал это первым, что мешает.
                //
                // ВРАГА ЭТО НЕ КАСАЕТСЯ: у моба нет игрока, который держит
                // кнопку, и махать в пустоту ему незачем.
                bool emptySwing = target < 0;
                if (emptySwing && (i != PlayerId || !input.Has(InputFlags.Attack))) continue;

                int attackVariant = i == PlayerId ? _nextPlayerAttackVariant : 0;
                if (i == PlayerId) _nextPlayerAttackVariant ^= 1;
                _events.Add(SimEvent.Attack(i, target, Entities.Position[i], attackVariant));
                Entities.NextAttackTick[i] = Tick + Entities.AttackCooldown[i];
                if (emptySwing) continue;

                Entities.PendingAttackTarget[i] = target;
                Entities.AttackImpactTick[i] = Tick + WindupTicksFor(i);
                Entities.PendingAttackVariant[i] = attackVariant;
            }
        }

        private void ApplyHeavyCleave(int primaryTarget)
        {
            int found = Grid.QueryRadius(Entities, Entities.Position[PlayerId], PlayerAttackRange,
                PlayerId, HitScratch);
            int hit = 0;
            for (int i = 0; i < found && hit < HeavyCleaveTargets; i++)
            {
                int target = HitScratch[i];
                if (target == primaryTarget || !Entities.Alive[target]) continue;
                if (Entities.Side[target] == Entities.Side[PlayerId]) continue;
                FixVec2 toTarget = Entities.Position[target] - Entities.Position[PlayerId];
                if (!FixVec2.WithinArc(Entities.Facing[PlayerId], toTarget, HeavyCleaveArcCos))
                    continue;

                ApplyAttack(PlayerId, target, 1, HeavySecondaryScale);
                hit++;
            }
        }

        private bool CanLandAttack(int source, int target)
        {
            if ((uint)source >= (uint)Entities.Count || (uint)target >= (uint)Entities.Count)
                return false;
            if (!Entities.Alive[source] || !Entities.Alive[target]) return false;
            if (Entities.Side[source] == Entities.Side[target]) return false;

            FixVec2 toTarget = Entities.Position[target] - Entities.Position[source];
            Fix64 range = AttackRangeFor(source);
            if (toTarget.LengthSq > range * range) return false;
            // У врага доворот — телеграф, а не дополнительный случайный
            // бросок. Разрешаем мягкий сектор ±120°: уже показанный замах
            // переживает небольшую ошибку ориентации, но удар не становится
            // круговым. Игрок сохраняет строгий фронтальный commit.
            Fix64 arc = source == PlayerId ? AttackCommitCos : EnemyAttackArcCos;
            return FixVec2.WithinArc(Entities.Facing[source], toTarget, arc);
        }

        /// <summary>
        /// Назначенная цель, если до неё можно дотянуться прямо сейчас.
        ///
        /// Сектор проверяется и здесь: персонаж не бьёт за спину даже по
        /// приказу — сперва довернётся, а доворот идёт своей скоростью.
        /// </summary>
        private int ChosenTarget()
        {
            FixVec2 toTarget = Entities.Position[_attackTarget] - Entities.Position[PlayerId];
            if (toTarget.LengthSq > PlayerAttackRangeSq) return -1;
            if (!FixVec2.WithinArc(Entities.Facing[PlayerId], toTarget, AttackCommitCos)) return -1;
            return _attackTarget;
        }

        /// <summary>
        /// Ближайший враг в дальности удара, БЕЗ ограничения по сектору.
        ///
        /// Нужен только развороту корпуса под зажатой кнопкой атаки: искать
        /// цель для поворота в лобовом секторе — значит требовать, чтобы игрок
        /// сам довернулся раньше героя. Сам удар этой функцией не пользуется.
        /// </summary>
        private int FindTurnTarget()
            => DebugUseNaiveTargeting
                ? NaiveFindNearestEnemy(PlayerId, FullCircleCos)
                : Grid.FindNearestEnemy(Entities, PlayerId, PlayerAttackRange, FullCircleCos);

        private int FindNearestEnemy(int from)
        {
            // Враг выбирает цель в расширенном секторе ±120°. Полное снятие
            // ограничения делало бы удар через спину; узкий сектор, напротив,
            // возвращал исходный баг: после расталкивания моб стоял боком,
            // не создавал замах и выглядел зависшим. Расширенный сектор даёт
            // время на доворот, а CanLandAttack использует то же правило.
            //
            // У ИГРОКА ВЫБОР ИДЁТ В ШИРОКОМ СЕКТОРЕ 120°, А НЕ В УЗКОМ ОКНЕ
            // СТАРТА ВЗМАХА. Здесь стоял AttackCommitCos — порог, при котором
            // корпус уже почти смотрит на цель. Как условие ВЫБОРА он означал
            // «бей только то, на что и так смотришь»: на бегу мимо толпы враг
            // проскакивал через это окно за пару тиков, и зажатая кнопка почти
            // не срабатывала. Доворот у героя 20° за тик при замахе в 9 тиков,
            // то есть цель на 60° он успевает добрать с запасом — и уже к
            // контакту CanLandAttack видит её во фронтальном секторе.
            Fix64 arc = from == PlayerId ? AttackArcCos : EnemyAttackArcCos;
            return DebugUseNaiveTargeting
                ? NaiveFindNearestEnemy(from, arc)
                : Grid.FindNearestEnemy(Entities, from, AttackRangeFor(from), arc);
        }

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
            Fix64 range = AttackRangeFor(from);

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
        private void ApplyAttack(int source, int target, int variant, Fix64 damageScale)
        {
            // Бросок на крит делается ВСЕГДА, даже при нулевом шансе: иначе
            // расход боевого потока случайности зависел бы от снаряжения,
            // и один и тот же сид перестал бы давать один и тот же забег.
            bool crit = Rng.Combat.Chance(Entities.CritChance[source]);
            // Keep the normal critical roll even when developer immunity absorbs the hit.
            if (target == PlayerId && PlayerInvulnerable) return;
            if (BlazeEvades(target, overTime: false)) return;

            int damage = CombatStats.RoundToInt(
                Fix64.FromInt(Entities.Damage[source]) * damageScale);
            if (crit)
                damage = CombatStats.RoundToInt(Fix64.FromInt(damage) * Entities.CritMultiplier[source]);

            // Броня гасит удар ПОСЛЕ крита: крит увеличивает сам удар, а кривая
            // брони зависит от его размера — значит и считать её надо от того,
            // что реально прилетело.
            damage = CombatStats.MitigateByArmor(damage, Entities.Armor[target]);

            Entities.Health[target] -= damage;
            _events.Add(SimEvent.Damage(source, target, damage, crit, Entities.Position[target],
                DamageType.Physical, DamageOrigin.BasicAttack, variant));

            // Горящая сабля добавляет огонь и к автоатаке: диздок говорит
            // «усиление относится ко всем атакам, а не только к автоатаке».
            if (Entities.Health[target] > 0) ApplyBlazeBonus(source, target, -1);

            // Смерть от автоатаки идёт тем же путём, что и от способности:
            // стадия ПриУбийстве обязана срабатывать независимо от того, чем
            // добили. «Перекидывается» иначе не сработал бы на добитом мечом.
            if (Entities.Health[target] <= 0)
                Kill(target, source, BurnSlotOf(target), basicAttackKill: true);
        }

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
            HashCyclone(ref hash);
            HashAnchorSlam(ref hash);
            HashWreck(ref hash);
            HashCleave(ref hash);
            HashBlaze(ref hash);
            HashFlask(ref hash);

            // Приказ — часть состояния персонажа, а не ввода: он переживает
            // отпущенную кнопку, значит обязан быть в хеше.
            Hashing.Mix(ref hash, _hasMoveOrder ? 1 : 0);
            Hashing.Mix(ref hash, _moveOrder.X);
            Hashing.Mix(ref hash, _moveOrder.Y);
            Hashing.Mix(ref hash, _explicitMoveOrder ? 1 : 0);
            Hashing.Mix(ref hash, _navigationWaypoint ? 1 : 0);
            Hashing.Mix(ref hash, _navigationTransit ? 1 : 0);
            Hashing.Mix(ref hash, _attackTarget);
            Hashing.Mix(ref hash, _nextPlayerAttackVariant);
            Hashing.Mix(ref hash, _abilityMovePenaltyUntilTick);

            Entities.HashInto(ref hash);

            // Снаряды, статусы и кулдауны — такая же часть состояния, как позиции.
            // Не попади они в хеш, тест детерминизма перестал бы их проверять,
            // и расхождение в способностях жило бы незамеченным.
            Projectiles.HashInto(ref hash);
            Statuses.HashInto(ref hash, Entities.Count);

            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                Hashing.Mix(ref hash, _abilityReadyTick[slot]);
                _abilityBuilds[slot]?.HashInto(ref hash);
            }
            Hashing.Mix(ref hash, _whirlwindImpactTick);
            Hashing.Mix(ref hash, _whirlwindImpactSlot);
            Hashing.Mix(ref hash, _leapLaunchTick);
            Hashing.Mix(ref hash, _leapAim.X.Raw);
            Hashing.Mix(ref hash, _leapAim.Y.Raw);

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
