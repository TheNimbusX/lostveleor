namespace Game.Sim
{
    public enum SimEventType : byte
    {
        None = 0,
        Damage = 1,
        Death = 2,
        Heal = 3,
        AbilityCast = 4,
        Spawn = 5,
        Attack = 6,

        /// <summary>
        /// Урон по времени: горение и всё, что будет после него.
        ///
        /// Отдельный тип, а не флаг у Damage, по одной практической причине.
        /// Горение тикает ТРИДЦАТЬ РАЗ В СЕКУНДУ на каждой горящей цели, и если
        /// представление считает это попаданиями, оно ставит hit-stop каждый
        /// кадр — игра встаёт в слоу-мо, пока хоть что-то горит. Тик урона
        /// и удар — разные события, и путать их нельзя.
        /// </summary>
        DamageOverTime = 7,

        /// <summary>Начало перехода Шквала; Amount — оставшиеся переходы,
        /// ActionVariant — индекс перехода. Amount=0 завершает цепочку.</summary>
        ChainStepHop = 8,
        AnchorSlamImpact = 9,
        Stun = 10,

        /// <summary>Удар Крушения. Amount — номер этапа 0..2, Flag — завершающий.</summary>
        WreckStage = 11,

        /// <summary>Сабля вспыхнула. Amount — на сколько тиков.</summary>
        BlazeBegin = 12,

        /// <summary>Удар прошёл мимо: сработало уклонение.</summary>
        Evaded = 13,

        /// <summary>Бутылка разбилась. Amount — слот, Position — где.</summary>
        FlaskBurst = 14,

        /// <summary>Кувырок под огнём оставил кусок следа. Amount — сколько тиков горит, Position — где.</summary>
        BlazeTrail = 15,

        /// <summary>Amount — номер залпа; часы анимации читаются из ForestBudAttackState.</summary>
        ForestBudVolleyStarted = 16,
        /// <summary>Amount — слот плода, ActionVariant — номер 0..4, Position — зафиксированная цель.</summary>
        ForestFruitLaunched = 17,
        ForestFruitImpact = 18,
        /// <summary>Невыпущенные плоды отменены; уже летящие остаются в своём пуле.</summary>
        ForestBudVolleyCancelled = 19,
        BackblastBurst = 20,
        /// <summary>Продолжение серии без повторной оплаты. Amount — слот, ActionVariant — этап.</summary>
        ActionStageStarted = 21,
        /// <summary>Включён артефакт забега (или сработал Обет Хранителя); ActionVariant — номер артефакта.</summary>
        ArtifactUsed = 22,
        WendigoStarted = 23,
        WendigoImpact = 24,
        WendigoCancelled = 25,
        StonehoofStarted = 26,
        StonehoofStopped = 27,
        StonehoofCancelled = 28,

        /// <summary>
        /// На земле появилась метка удара. Source — владелец, Amount — слот
        /// в общем списке меток, ActionVariant — её серийный номер, Flag —
        /// рисует ли её общий вид (TelegraphFlags.SharedView), Position — начало фигуры.
        /// </summary>
        TelegraphOpened = 29,

        /// <summary>Метка снята до удара: оглушение, смерть, волок. Поля как у TelegraphOpened.</summary>
        TelegraphCancelled = 30,

        /// <summary>
        /// Враг ушёл в землю, не умерев: таймер выживания кончился. Target —
        /// кто, Position — где. Ни опыта, ни добычи; Alive уже false.
        /// </summary>
        Burrowed = 31,

        /// <summary>
        /// Вышла волна встречи арены. Amount — номер волны с нуля,
        /// ActionVariant — сколько волн у встречи, Flag — выживание,
        /// Position — где встала первая группа. Волна подмоги босса —
        /// Amount = −1, ActionVariant — какая по счёту (1 — на 66%, 2 — на 33%).
        /// </summary>
        EncounterWave = 32,

        /// <summary>
        /// Моб начал действие новых мобов леса (линия шипов, всплеск, удар
        /// корнями). Source — моб, Target — герой, Amount — номер шага
        /// действия (0), Position — откуда действует моб, ActionVariant —
        /// EnemyActionKind. Им же кормится слот звука EnemyWarning.
        /// </summary>
        EnemyActionStarted = 33,

        /// <summary>
        /// Контакт действия: один шип линии, всплеск, удар корнями, шип
        /// выстрела. Amount — номер контакта в действии (шип 0..3), Flag —
        /// задел ли героя, Position — центр сработавшей фигуры, а у выстрела —
        /// точка, где шип остановился (в герое или в конце пути).
        /// ActionVariant — EnemyActionKind.
        /// </summary>
        EnemyActionImpact = 34,

        /// <summary>Действие снято до конца: оглушение, смерть, волок. Поля как у EnemyActionStarted.</summary>
        EnemyActionCancelled = 35,

        /// <summary>
        /// Расщепень распался. Source — родитель, Target — первый детёныш
        /// (остальные идут подряд за ним), Amount — сколько детёнышей,
        /// Position — где умер родитель.
        /// </summary>
        SplitterSplit = 36,

        /// <summary>
        /// Снаряд моба вылетел: шип выстрела Шипомёта в тик выпуска. Source —
        /// стрелок, Target — герой, Amount — номер выстрела (ThornShotState.Serial),
        /// Position — начало пути, ActionVariant — EnemyActionKind. Вид ставит
        /// по нему шип, след и звук броска; где шип дальше — читает из Sim
        /// (Simulation.TryGetThornShot), где встал — из EnemyActionImpact.
        /// </summary>
        EnemyProjectileLaunched = 37,

        /// <summary>
        /// Кислая лужа гнилого плода Плюй-плода легла. Source — стрелок (может
        /// быть уже мёртв), Amount — слот лужи (Simulation.TryGetForestPuddle),
        /// ActionVariant — номер лужи, Position — центр.
        /// </summary>
        PuddleOpened = 38,

        /// <summary>
        /// Лужа ушла с земли. Поля как у PuddleOpened; Flag — вытеснена новой
        /// раньше срока (вид гасит её быстрее).
        /// </summary>
        PuddleClosed = 39,

        /// <summary>
        /// На героя наложен контроль. Source — кто наложил (моб, -1 если нет),
        /// Target — герой, Amount — сколько тиков длится, Flag — корни
        /// (true: не ходит и не кувыркается, но бьёт и кастует) или оглушение
        /// (false), Position — где стоит герой. Контроль, отбитый иммунитетом,
        /// события не даёт. Фабрика — SimEvent.HeroControl.
        /// </summary>
        HeroControl = 40,

        /// <summary>
        /// Контакт удара серии сабли (Simulation.SabreCombo) — и при промахе.
        /// Source — герой, Amount — сколько целей в секторе, Flag — добивающий,
        /// ActionVariant — место в серии 0..2, Position — где стоит герой.
        /// Направление и сроки удара — Simulation.SabreSwing. Урон по каждой
        /// цели — отдельные Damage с DamageOrigin.BasicAttack сразу за ним.
        /// </summary>
        SabreContact = 41,

        /// <summary>
        /// Рывок героя начат (Simulation.Dash), в тик нажатия. Source — герой,
        /// Amount — длительность рывка в тиках, ActionVariant — номер рывка
        /// (PelagDashState.Serial), Position — откуда. Куда (полная дальность,
        /// до стены), направление и окно неуязвимости — Simulation.PelagDash.
        /// Идёт рядом с обычным AbilityCast того же тика.
        /// </summary>
        DashStarted = 42,

        /// <summary>
        /// Рывок героя кончился — тело встало: доехало, упёрлось в стену или
        /// рывок снят другим уходом. Source — герой, Amount — пройденный путь в
        /// сантиметрах (длина следа), Flag — встал раньше полной дальности
        /// (стена или снятие), ActionVariant — номер рывка, Position — где встал.
        /// Неуязвимость у стены не кончается: она держится до
        /// PelagDashState.InvulnerableUntilTick.
        /// </summary>
        DashEnded = 43,

        /// <summary>
        /// Вихрь · Буря (форма, Simulation.WhirlwindForms): удержание началось — в
        /// тик контакта, если клавишу ещё держат. Source — герой, Amount — сколько
        /// тиков до предела удержания (3 с от нажатия), Position — где герой.
        /// Пока крутится — Simulation.WhirlwindStorming (и прежний WhirlwindChanneling).
        /// </summary>
        WhirlwindStormStarted = 44,

        /// <summary>
        /// Буря: оборот удержания ударил (каждые Simulation.StormPulseTicks).
        /// Source — герой, Amount — сколько врагов в круге, ActionVariant — сколько
        /// тиков осталось до предела, Position — где герой (центр круга). Урон по
        /// каждой цели — Damage с DamageOrigin.Ability сразу за ним.
        /// </summary>
        WhirlwindStormPulse = 45,

        /// <summary>
        /// Буря кончилась. Source — герой, Amount — сколько тиков не дотянула до
        /// предела (0 — докрутила), Flag — кончилась Концентрация, ActionVariant —
        /// WhirlwindStormEnd (отпустил, Концентрация, предел, прервали), Position — где герой.
        /// </summary>
        WhirlwindStormEnded = 46,

        /// <summary>
        /// Вихрь · Водоворот: в тик каста враги в Simulation.MaelstromRadius
        /// потянуты к герою. Source — герой, Amount — сколько тел тянут (тяжёлых,
        /// элит и босса не тянет — им только оглушение), ActionVariant — за сколько
        /// тиков (тяга кончается к контакту), Position — центр. В контакт — удар
        /// Вихря и оглушение: события Damage и Stun.
        /// </summary>
        WhirlwindMaelstromPull = 47,

        /// <summary>
        /// Вихрь · Пенные волны: кольцо пошло от центра. Source — герой, Amount —
        /// номер кольца (0, 1), ActionVariant — за сколько тиков дойдёт до внешнего
        /// радиуса, Position — центр (где был герой в контакт). Радиусы —
        /// Simulation.FoamRingInnerRadius и FoamRingOuterRadius(кольцо), живое
        /// кольцо — Simulation.TryGetFoamRing.
        /// </summary>
        WhirlwindFoamRing = 48,

        /// <summary>
        /// Пенные волны: кольцо задело врага (каждое кольцо — раз на врага). Source —
        /// герой, Target — враг, Amount — номер кольца, Position — где враг (брызги).
        /// Урон — Damage следом; лёгких кольцо отталкивает (ForcedMotionKind.Shoved).
        /// </summary>
        WhirlwindFoamRingHit = 49,

        /// <summary>
        /// Шквал (Simulation.Squall): старт прыжка к цели — после замаха и после
        /// каждой опоры. Source — герой, Target — цель, Amount — тиков полёта до
        /// удара (2–6, по длине), Flag — удар обратный (false — прямой, строго
        /// чередуются), ActionVariant — номер прыжка с нуля, Position — точка
        /// посадки. Откуда — где герой сейчас (Simulation.Squall.From); тик удара —
        /// Simulation.Squall.ArriveTick. Рядом идёт прежний ChainStepHop.
        /// </summary>
        SquallJump = 50,

        /// <summary>
        /// Шквал: прибытие и удар, в тик прибытия. Source — герой, Target — цель,
        /// Amount — сколько прыжков осталось после него (0 — последний), Flag —
        /// удар дошёл (цель жива и рядом; Damage и Death идут сразу за событием),
        /// ActionVariant — номер прыжка, Position — где герой. Следующая цель
        /// (к ней поворот в опоре) — Simulation.Squall.NextTarget.
        /// </summary>
        SquallStrike = 51,

        /// <summary>
        /// Шквал · Охота: удар убил — лишний прыжок. Source — герой, Target — убитый,
        /// Amount — сколько лишних прыжков дано за каст, ActionVariant — номер
        /// прыжка, Position — где лежит убитый (всплеск и знак лишнего прыжка).
        /// </summary>
        SquallHuntKill = 52,

        /// <summary>
        /// Шквал: прыжок назад к точке каста (талант «Возврат» или форма
        /// Неуловимый). Source — герой, Amount — тиков полёта, ActionVariant —
        /// точек дуги (0 — прямо, 2 — Simulation.Squall.Via0, Via1), Position — куда.
        /// </summary>
        SquallReturn = 53,

        /// <summary>
        /// Шквал кончился. Source — герой, Amount — SquallEnd (доигран, сорван
        /// ходьбой, снят, без цели), ActionVariant — номер каста
        /// (Simulation.Squall.Serial), Position — где герой.
        /// </summary>
        SquallEnded = 54,

        /// <summary>
        /// Шквал · Пенный след: прыжок оставил полосу пены. Source — герой,
        /// Amount — слот полосы (Simulation.TryGetSquallFoamStrip), ActionVariant —
        /// сколько тиков живёт, Position — начало полосы. Урон пены — DamageOverTime.
        /// </summary>
        SquallFoamStrip = 55,
    }

    /// <summary>
    /// Какое действие моба описывает событие EnemyAction*. Значение идёт в
    /// ActionVariant и в хеш — новые только в конец. Будущий босс берёт свои
    /// значения отсюда же.
    /// </summary>
    public enum EnemyActionKind : byte
    {
        None = 0,

        /// <summary>Шипомёт: линия шипов вдоль взгляда.</summary>
        ThornLine = 1,

        /// <summary>Шипомёт: всплеск вокруг себя, когда героя прижало вплотную.</summary>
        ThornBurst = 2,

        /// <summary>Корнехват: удар корнями по месту героя.</summary>
        SnarerSlam = 3,

        /// <summary>
        /// Резерв под замах Расщепеня. Сейчас замах идёт общим ближним
        /// замахом (SimEvent.Attack, Simulation.EnemyMelee) и этим значением
        /// не пользуется.
        /// </summary>
        SplitterSwing = 4,

        /// <summary>
        /// Шипомёт: выстрел шипом (обычная дальняя атака) без метки на земле.
        /// Started — начало замаха, EnemyProjectileLaunched — выпуск на 21-м
        /// тике, Impact — шип остановился: попал (Flag) или долетел до конца
        /// пути; Position — где он встал.
        /// </summary>
        ThornShot = 5,

        /// <summary>
        /// Расщепень: перекат клубком. Started — сжатие (0), Impact со stage 0 —
        /// пуск (тик 30, Position — начало полосы), Impact со stage 1 — стоп
        /// (Flag — задел героя, Position — где встал), Cancelled — снят.
        /// </summary>
        SplitterRoll = 6,

        /// <summary>
        /// Корнехват: «Волна из корней», лечение союзников. Started — лапы в
        /// землю, Impact — волна (Flag — кого-то вылечила, Amount — скольких),
        /// Cancelled — сбит. Само лечение — события Heal по каждому союзнику.
        /// </summary>
        SnarerMend = 7,

        /// <summary>
        /// Плюй-плод: гнилой плод. ForestFruitLaunched с ActionVariant = номер
        /// плода несёт гнилость в ForestFruitState.Rotten; это значение — для
        /// звука и описаний.
        /// </summary>
        BudRotFruit = 8,

        /// <summary>
        /// Камнекопыт: взмах клыками вплотную (герой уже рядом, сам кабан не
        /// подходит), отброс на 1 м. Started — замах со знаком на теле,
        /// Impact — контакт (Flag — задел героя), Cancelled — снят.
        /// </summary>
        StonehoofTusk = 9,

        /// <summary>
        /// Вендиго: размашистый удар на 360° с отбросом, когда герой долго
        /// крутится сбоку или сзади. Started — замах, Impact — круг
        /// (Flag — задел героя), Cancelled — снят.
        /// </summary>
        WendigoSweep = 10,

        /// <summary>
        /// Хозяин Чащи: лапа, сектор 120° на 3,6 м. Started — замах (знак на
        /// теле; Amount — номер лапы 0/1 в двойной фазы 3), Impact — контакт
        /// (Flag — задел героя), Cancelled — снята.
        /// </summary>
        ThicketPaw = 11,

        /// <summary>Хозяин Чащи: дыбом и топот, круг 4,5 м вокруг себя с отбросом.</summary>
        ThicketStomp = 12,

        /// <summary>Хозяин Чащи: рёв (вступление и пороги 66/50/33), кольцо 2–5,5 м, отброс без урона.</summary>
        ThicketRoar = 13,

        /// <summary>Хозяин Чащи: пробуждение — вырывает лапы из земли (только Started); за ним рёв.</summary>
        ThicketWake = 14,

        /// <summary>Хозяин Чащи, этап 2: нырок в корни.</summary>
        ThicketDive = 15,

        /// <summary>Хозяин Чащи, этап 2: прорастание.</summary>
        ThicketSprout = 16,

        /// <summary>Хозяин Чащи, этап 2: облака пыльцы.</summary>
        ThicketPollen = 17,

        /// <summary>Хозяин Чащи, этап 3: ягодный ливень.</summary>
        ThicketRain = 18,

        /// <summary>Хозяин Чащи, этап 3: буря цветения.</summary>
        ThicketStorm = 19,

        /// <summary>
        /// Хозяин Чащи: вступление-кат-сцена — герой ступил на пол поляны босса
        /// (или ранил спящего). Только EnemyActionStarted, Amount 0, Position —
        /// босс, в тик начала окна. Сроки (пробуждение, конец рёва и первая
        /// атака) — Simulation.TryGetThicketIntro; пока окно идёт — герой не
        /// слушается и неуязвим (Simulation.ThicketIntroHoldsHero).
        /// </summary>
        ThicketIntro = 20,
    }

    /// <summary>
    /// Причина прямого урона для presentation-слоя. Физический тип урона не
    /// отвечает на вопрос, чем рисовать контакт: сабля и Вихрь оба physical,
    /// но один требует точечного hit, второй — единого кругового акцента.
    /// </summary>
    public enum DamageOrigin : byte
    {
        BasicAttack = 0,
        Ability = 1,
        DamageOverTime = 2,
    }

    /// <summary>
    /// Единица связи «симуляция → представление». Симуляция описывает ЧТО произошло,
    /// представление решает, как это показать.
    ///
    /// Структура, а не класс: за забег их будут миллионы, аллокации недопустимы.
    /// Событие не влияет на симуляцию и не читается ею обратно.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly int Source;     // индекс сущности-источника, -1 если нет
        public readonly int Target;     // индекс сущности-цели, -1 если нет
        public readonly int Amount;     // урон, лечение, номер способности
        public readonly bool Flag;      // крит, добивание — по смыслу типа
        public readonly FixVec2 Position;

        /// <summary>
        /// Чем ударили. Осмысленно только у Damage.
        ///
        /// Нужен не отрисовке, а Полигону: «разбивка урона по источникам» —
        /// это его смысл, а без типа в событии разбить урон не по чему.
        /// </summary>
        public readonly DamageType DamageKind;
        public readonly DamageOrigin DamageOrigin;
        public readonly int ActionVariant;
        /// <summary>Снимок начала новой обычной серии; Serial=0 у прежних событий.</summary>
        public readonly PelagBasicAttackState BasicAttackState;

        public SimEvent(SimEventType type, int source, int target, int amount, bool flag,
            FixVec2 position, DamageType damageKind = DamageType.Physical,
            DamageOrigin damageOrigin = DamageOrigin.BasicAttack, int actionVariant = 0,
            PelagBasicAttackState basicAttackState = default)
        {
            Type = type;
            Source = source;
            Target = target;
            Amount = amount;
            Flag = flag;
            Position = position;
            DamageKind = damageKind;
            DamageOrigin = damageOrigin;
            ActionVariant = actionVariant;
            BasicAttackState = basicAttackState;
        }

        public static SimEvent Damage(int source, int target, int amount, bool crit, FixVec2 at,
            DamageType kind, DamageOrigin origin = DamageOrigin.BasicAttack, int actionVariant = 0,
            PelagBasicAttackState basicAttackState = default)
            => new SimEvent(SimEventType.Damage, source, target, amount, crit, at, kind,
                origin, actionVariant, basicAttackState);

        /// <summary>Тик урона по времени. Не удар: ни стопа, ни тряски, ни звука попадания.</summary>
        public static SimEvent DamageOverTime(int source, int target, int amount, FixVec2 at,
            DamageType kind)
            => new SimEvent(SimEventType.DamageOverTime, source, target, amount, false, at, kind,
                DamageOrigin.DamageOverTime);

        public static SimEvent Death(int source, int target, FixVec2 at)
            => new SimEvent(SimEventType.Death, source, target, 0, false, at);

        public static SimEvent Cast(int source, int abilityIndex, FixVec2 at)
            => new SimEvent(SimEventType.AbilityCast, source, -1, abilityIndex, false, at);

        public static SimEvent ChainHop(int source, int target, int remaining, int index, FixVec2 at)
            => new SimEvent(SimEventType.ChainStepHop, source, target, remaining, index == 0, at,
                DamageType.Physical, DamageOrigin.Ability, index);

        public static SimEvent Spawn(int target, FixVec2 at)
            => new SimEvent(SimEventType.Spawn, -1, target, 0, false, at);

        /// <summary>
        /// Появление из-под земли: тот же Spawn, но Flag = true, Amount — сколько
        /// тиков моб бездействует (Simulation.EmergeTicks). Вид играет выход из корней.
        /// </summary>
        public static SimEvent Emerge(int target, FixVec2 at, int dormantTicks)
            => new SimEvent(SimEventType.Spawn, -1, target, dormantTicks, true, at);

        public static SimEvent Burrow(int target, FixVec2 at)
            => new SimEvent(SimEventType.Burrowed, -1, target, 0, false, at);

        public static SimEvent Attack(int source, int target, FixVec2 at, int variant = 0,
            PelagBasicAttackState basicAttackState = default)
            => new SimEvent(SimEventType.Attack, source, target, variant, false, at,
                DamageType.Physical, DamageOrigin.BasicAttack, variant, basicAttackState);

        /// <summary>
        /// Событие действия моба: type — EnemyActionStarted, EnemyActionImpact
        /// или EnemyActionCancelled. stage — номер контакта в действии, hit —
        /// задел ли контакт героя (только у EnemyActionImpact). Для контакта
        /// ThornShot stage содержит уникальный Serial снаряда, а не номер стадии.
        /// </summary>
        public static SimEvent EnemyAction(SimEventType type, int source, int target, EnemyActionKind kind,
            FixVec2 at, int stage = 0, bool hit = false)
            => new SimEvent(type, source, target, stage, hit, at,
                DamageType.Physical, DamageOrigin.BasicAttack, (int)kind);

        /// <summary>Снаряд моба вылетел: serial — номер выстрела, at — начало пути.</summary>
        public static SimEvent EnemyProjectile(int source, int target, int serial, EnemyActionKind kind, FixVec2 at)
            => new SimEvent(SimEventType.EnemyProjectileLaunched, source, target, serial, false, at,
                DamageType.Physical, DamageOrigin.BasicAttack, (int)kind);

        /// <summary>Лечение моба мобом: source лечит target на amount.</summary>
        public static SimEvent Heal(int source, int target, int amount, FixVec2 at)
            => new SimEvent(SimEventType.Heal, source, target, amount, false, at);

        /// <summary>Распад Расщепеня: count детёнышей подряд, начиная с firstChild.</summary>
        public static SimEvent Split(int parent, int firstChild, int count, FixVec2 at)
            => new SimEvent(SimEventType.SplitterSplit, parent, firstChild, count, false, at);

        /// <summary>
        /// Контроль героя наложен: source — кто (или -1), hero — на кого, ticks —
        /// сколько тиков, rooted — корни (true) или оглушение (false), at — где
        /// стоит герой. Вид рисует корни у ног или кружок оглушения.
        /// </summary>
        public static SimEvent HeroControl(int source, int hero, int ticks, bool rooted, FixVec2 at)
            => new SimEvent(SimEventType.HeroControl, source, hero, ticks, rooted, at);
    }
}
