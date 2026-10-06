using System;

namespace Game.Sim
{
    /// <summary>
    /// Шип терновника Хозяина Чащи (до 08.10 — семя веера). Изменяемая структура пула: каждое поле — в хеш
    /// (HashThicketSeeds). Пока куст растёт, шип запланирован (Released = false): его линия лежит на земле,
    /// полёта нет. С выпуска — снаряд, как шип Шипомёта (ThornShotState): летит по своей линии
    /// ThicketSeedSpeed за тик и встаёт на первом касании героя или в конце пути.
    /// </summary>
    public struct ThicketSeedState
    {
        /// <summary>Номер шипа — Amount событий EnemyProjectileLaunched / Impact / Cancelled; 0 — места нет.</summary>
        public int Serial;

        /// <summary>Номер каста — Serial действия босса (ThicketBushState.Cast).</summary>
        public int Volley;

        /// <summary>
        /// Место шипа в касте и в пуле: куст × ThicketBushLanes + линия. Index / 4 — ThicketBushState.Order
        /// (место куста, TryGetThicketBush), Index % 4 — линия куста (ThicketBushLaneDirection).
        /// </summary>
        public int Index;

        /// <summary>Выпущен и летит; false — куст ещё растёт, лежит только линия.</summary>
        public bool Released;

        /// <summary>Тик выпуска (до выпуска — запланированный, = LaunchTick куста): в него шип пролетает первые ThicketSeedSpeed пути. Часы сдвигают.</summary>
        public int ReleaseTick;

        /// <summary>Урон, снятый в тик выпуска.</summary>
        public int Damage;

        /// <summary>Начало пути (ThicketBushThornStart от центра куста) и направление линии.</summary>
        public FixVec2 Origin, Direction;

        /// <summary>Длина пути: ThicketBushLaneLength или до первого ствола, камня, края пола.</summary>
        public Fix64 Length;

        /// <summary>Сколько шип пролетел от начала пути к концу прошлого тика.</summary>
        public Fix64 Travelled;

        /// <summary>Номер линии шипа на земле (Lane, SharedView).</summary>
        public int TelegraphSerial;
    }

    /// <summary>Каст терновника целиком: от жеста до последнего шипа. Каждое поле — в хеш (HashThicketSeeds).</summary>
    public struct ThicketSeedVolley
    {
        /// <summary>Serial действия босса (ThicketMasterState.Serial); 0 — каста нет (все шипы встали).</summary>
        public int Serial;

        /// <summary>Начало каста (жест) и выпуск первого куста.</summary>
        public int StartTick, LaunchTick;

        /// <summary>
        /// ContactTick — самый ранний расчётный контакт (шипы дойдут до места героя в начале каста).
        /// LastTick — не позже этого тика встанет последний шип (выпуск последнего куста + полёт полной линии).
        /// </summary>
        public int ContactTick, LastTick;

        /// <summary>Шипов с открытой линией (растёт по мере прорастания кустов).</summary>
        public int Count;

        /// <summary>Каст уже попал: одно попадание на каст, остальные шипы летят мимо героя.</summary>
        public bool HitResolved;

        /// <summary>Тело босса и герой в начале каста.</summary>
        public FixVec2 Body, Aim;
    }

    /// <summary>
    /// Куст терновника (владелец 08.10). Изменяемая структура пула: каждое поле — в хеш (HashThicketSeeds).
    /// Место занято с начала каста; до SproutTick куста на земле ещё нет (Sprouted = false).
    /// </summary>
    public struct ThicketBushState
    {
        /// <summary>Номер куста (сквозной); 0 — места нет.</summary>
        public int Serial;

        /// <summary>Каст — Serial действия босса (= ThicketSeedState.Volley его шипов).</summary>
        public int Cast;

        /// <summary>Место куста в касте и в пуле (0 — ближний к герою); шипы куста — Index / 4 == Order.</summary>
        public int Order;

        /// <summary>Центр куста на полу.</summary>
        public FixVec2 Center;

        /// <summary>Линия 0; линия k — Axis, повёрнутая на k × 90° против часовой (ThicketBushLaneDirection).</summary>
        public FixVec2 Axis;

        /// <summary>Крест «×» (линии по диагоналям экрана боя); false — «+» (вдоль осей экрана).</summary>
        public bool Diagonal;

        /// <summary>
        /// SproutTick — куст пророс (Started(ThicketSeeds, Order + 1), линии открыты), растёт до LaunchTick —
        /// выпуск 4 шипов; WitherTick — начинает вянуть; GoneTick — его больше нет (место пусто). Часы сдвигают.
        /// </summary>
        public int SproutTick, LaunchTick, WitherTick, GoneTick;

        /// <summary>Расчётный контакт: шип дойдёт до места героя в начале каста (такт и бюджет меток).</summary>
        public int ContactTick;

        /// <summary>Пророс (линии и шипы заведены) и выпустил шипы.</summary>
        public bool Sprouted, Launched;
    }

    /// <summary>
    /// ХОЗЯИН ЧАЩИ — «ТЕРНОВНИК» (владелец 08.10: «шиповые семена надо сделать не от босса, а чтобы
    /// появлялось 2-3 куста на арене и от них исходило по 4 шипа этих в разные 4 стороны, а потом кусты
    /// исчезали. кусты в рандомных местах»). Заменил «Веер шипов-семян» (§ 16, 07.10): значения enum те же —
    /// действие ThicketMasterAction.Seeds, событие EnemyActionKind.ThicketSeeds.
    ///
    /// Жест каста (как у прорастания, ThicketCastGestureTicks 18, босс стоит): кусты прорастают в случайных
    /// местах пола (свой поток босса) — фаза 1 — 2, фазы 2–3 — 3, через ThicketBushEveryTicks (9) друг за
    /// другом. Куст 0 — в 3–5,5 м от героя на одной из своих линий (герою надо сойти с неё), остальные —
    /// в 3,5–8 м от героя; все — на полу с запасом, не ближе 2,5 м к герою и к кромке корпуса, не ближе 4 м
    /// друг к другу, не на лежащих метках босса (ThicketBushSpotFree). Крест каждого куста — «+» или «×»
    /// по осям экрана боя, по очереди (первый — броском). С прорастания у куста 4 линии (Lane, SharedView)
    /// ThicketBushLaneLength (9 м) или до ствола / камня / края пола; через ThicketBushWindupTicks (30) куст
    /// выпускает по шипу на каждую линию: 13 м/с, попадание — как у шипа Шипомёта (тело героя касается
    /// полосы, пройденной за тик). Одно попадание на каст. Через ThicketBushStandTicks (12) после выпуска
    /// куст вянет ThicketBushWitherTicks (24) и исчезает. Куст — не тело: не держит проход и не цель.
    ///
    /// Наслоение: пока шипы каста не встали (каст жив), это фоновая опасность — босс начинает только серии
    /// лапы, и их удары не ближе ThicketOwnContactSpacingTicks к контактам шипов. Бюджет крупных меток — вес
    /// ThicketBushMarkWeight (2) на весь каст, как у прорастания и ливня.
    ///
    /// Выбор (только у свободного босса): фаза 1 — вес 8 (средняя и дальняя полосы, рядом с «Корнями-плетью»),
    /// фазы 2–3 — вес 5; ближняя полоса — вес 2 только в фазе 1 (редко: там лапа, топот, нырок), в фазах 2–3 — нет. Перезарядки прежние: 195 /
    /// 360 от начала. Смерть босса или героя снимает всё (кусты — сразу, линии гаснут, летящие шипы —
    /// Cancelled); Часы держат кусты и шипы.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- терновник: кусты ----

        /// <summary>Кустов в касте: фаза 1 — 2, фазы 2–3 — 3 (мест на босса — ThicketBushSlots); меньше двух не встало — каста нет.</summary>
        public const int ThicketBushesPhase1 = 2, ThicketBushesPhase2 = 3, ThicketBushSlots = 3, ThicketBushesMin = 2;

        /// <summary>Линий (шипов) у куста — крест.</summary>
        public const int ThicketBushLanes = 4;

        /// <summary>Мест под шипы на босса: 3 куста × 4 линии. Место шипа = его Index.</summary>
        public const int ThicketSeedSlots = ThicketBushSlots * ThicketBushLanes;

        /// <summary>Куст k прорастает через k × 9 тиков после начала каста; растёт 30 тиков (линии заливаются) до выпуска.</summary>
        public const int ThicketBushEveryTicks = 9, ThicketBushWindupTicks = 30;

        /// <summary>После выпуска куст стоит 12 тиков, вянет 24 — и его нет.</summary>
        public const int ThicketBushStandTicks = 12, ThicketBushWitherTicks = 24;

        /// <summary>Не встал (место, бюджет, такт) — следующая попытка через столько тиков.</summary>
        public const int ThicketBushRetryTicks = 30;

        /// <summary>Радиус куста (вид, лежащие метки): 0,6 м.</summary>
        public static readonly Fix64 ThicketBushRadius = Fix64.Ratio(3, 5);

        /// <summary>
        /// Шип начинает путь в 0,3 м от центра куста: стоящий в самом кусте задет первым же тиком полёта
        /// (тело героя 0,45). Линия — ThicketBushLaneLength (9 м) от этой точки или до препятствия.
        /// </summary>
        public static readonly Fix64 ThicketBushThornStart = Fix64.Ratio(3, 10), ThicketBushLaneLength = Fix64.FromInt(9);

        /// <summary>Куст — не ближе 2,5 м к центру героя и к кромке корпуса босса (ThicketHullGap), кусты — не ближе 4 м друг к другу.</summary>
        public static readonly Fix64 ThicketBushHeroGap = Fix64.Ratio(5, 2), ThicketBushHullGap = Fix64.Ratio(5, 2),
            ThicketBushApart = Fix64.FromInt(4);

        /// <summary>Куст 0 — в 3–5,5 м от героя на своей линии; остальные — в 3,5–8 м от героя.</summary>
        public static readonly Fix64 ThicketBushNearMin = Fix64.FromInt(3), ThicketBushNearMax = Fix64.Ratio(11, 2),
            ThicketBushFarMin = Fix64.Ratio(7, 2), ThicketBushFarMax = Fix64.FromInt(8);

        /// <summary>Центр куста — на полу с запасом 1 м (круг 1 м целиком на полу).</summary>
        public static readonly Fix64 ThicketBushFloorMargin = Fix64.One;

        /// <summary>По возможности не ниже героя на экране боя больше 3 м (полоса HUD внизу), как круг 2 бури.</summary>
        public static readonly Fix64 ThicketBushScreenDownMax = Fix64.FromInt(3);

        // ---- шип ----

        /// <summary>Скорость шипа, м за тик: 13/30 (13 м/с).</summary>
        public static readonly Fix64 ThicketSeedSpeed = Fix64.Ratio(13, 30);

        /// <summary>Полуширина шипа и его линии на земле, м (линия 0,5 м; тело героя 0,45 — задевает до 0,7 м от оси).</summary>
        public static readonly Fix64 ThicketSeedRadius = Fix64.Ratio(1, 4);

        // ---- выбор, урон, бюджет ----

        /// <summary>Перезарядка от начала каста: фаза 1 — 195 (6,5 с), фазы 2–3 — 360 (12 с); ×1,25 при подмоге, ×0,85 с половины здоровья.</summary>
        public const int ThicketSeedCooldownPhase1Ticks = 195, ThicketSeedCooldownTicks = 360;

        /// <summary>
        /// Вес в жребии: фаза 1 — 8 (рядом с «Корнями-плетью» 6), фазы 2–3 — 5 (лапа 10, касты 6 / 4); ближняя
        /// полоса — 2 только в фазе 1 (разнообразие ближнику), в фазах 2–3 — нет (как веер): там кусты (фоновая
        /// опасность) вставали в очередь к ливню и буре и вытесняли нырок «под героя» фазы 3 (стенд 08.10:
        /// у сильного ближника 0,82 → 0,28 нырка в фазе 3 за бой).
        /// </summary>
        public const int ThicketSeedWeightPhase1 = 8, ThicketSeedWeight = 5, ThicketBushNearWeight = 2;

        /// <summary>Доля урона шипа в таблице долей (лапа — 41): на арене 9 при базе лапы 14 — 9. Одно попадание на каст.</summary>
        public const int ThicketSeedDamageA9 = 16;

        /// <summary>Вес всего каста в бюджете крупных меток (до последнего шипа) — как прорастание и ливень.</summary>
        public const int ThicketBushMarkWeight = 2;

        // ---- снято 08.10: «Веер шипов-семян» (§ 16) — имена держит вид веера; удалить вместе с ним ----

        /// <summary>Снято (веер): замах и стойка веера. Терновник — ThicketBushWindupTicks / ThicketCastGestureTicks.</summary>
        public const int ThicketSeedWindupTicks = 22, ThicketSeedRecoveryTicks = 12;

        /// <summary>Снято (веер): семян в веере 3 / 5. Терновник — ThicketBushesPhase1/2 × ThicketBushLanes.</summary>
        public const int ThicketSeedsPhase1 = 3, ThicketSeedsPhase2 = 5;

        /// <summary>Снято (веер): начало пути в 2 м от тела. Терновник — ThicketBushThornStart от куста.</summary>
        public static readonly Fix64 ThicketSeedStartOffset = Fix64.FromInt(2);

        /// <summary>Снято (веер): семян в веере по фазе. Терновник — ThicketBushCountOf.</summary>
        public int ThicketSeedCountOf(int id) => ThicketMasterPhase(id) >= 2 ? ThicketSeedsPhase2 : ThicketSeedsPhase1;

        /// <summary>Снято (веер): направление семени index веера из count вокруг center (шаг 20° / 15°).</summary>
        public static FixVec2 ThicketSeedDirection(FixVec2 center, int index, int count)
        {
            FixVec2 c = center.Normalized();
            int halfSteps = 2 * index - (count - 1);
            if (halfSteps == 0) return c;
            Fix64 angle = Fix64.Pi * ((count <= ThicketSeedsPhase1 ? 200 : 150) * halfSteps) / 3600;
            Fix64 cos = Fix64.Cos(angle), sin = Fix64.Sin(angle);
            return new FixVec2(c.X * cos - c.Y * sin, c.X * sin + c.Y * cos).Normalized();
        }

        // ---- состояние ----

        private ThicketSeedState[] _thicketSeeds;
        private ThicketSeedVolley[] _thicketSeedVolleys;
        private ThicketBushState[] _thicketBushes;
        private int _thicketSeedSerial, _thicketBushSerial;
        private FixVec2[] _thicketBushSpots;

        private ThicketSeedState[] ThicketSeeds => _thicketSeeds ??= new ThicketSeedState[Entities.Capacity * ThicketSeedSlots];
        private ThicketSeedVolley[] ThicketSeedVolleys => _thicketSeedVolleys ??= new ThicketSeedVolley[Entities.Capacity];
        private ThicketBushState[] ThicketBushes => _thicketBushes ??= new ThicketBushState[Entities.Capacity * ThicketBushSlots];

        /// <summary>
        /// Крест «+»: линия 0 — вправо по экрану боя (перпендикуляр ThicketCameraScreenDown: (−down.Y, down.X)
        /// = (0,9015; −0,4329)), 1 — вверх, 2 — влево, 3 — вниз. Числами, а не от ThicketCameraScreenDown:
        /// порядок статики между частями класса не задан. Поменяют рыскание камеры — поменять и здесь.
        /// </summary>
        public static readonly FixVec2 ThicketBushPlusAxis = new FixVec2(Fix64.Ratio(9015, 10000), Fix64.Ratio(-4329, 10000)).Normalized();

        /// <summary>Крест «×»: «+», повёрнутый на 45° против часовой ((x − y, x + y) / √2 = (0,9435; 0,3313)).</summary>
        public static readonly FixVec2 ThicketBushCrossAxis = new FixVec2(Fix64.Ratio(13344, 10000), Fix64.Ratio(4686, 10000)).Normalized();

        // ---- чтение для вида, тестов и стенда ----

        /// <summary>
        /// Шип в месте пула index (0 … ThicketSeedSlots − 1 = Index шипа: куст × 4 + линия) босса id:
        /// запланированный или летящий; false — места нет. Вставший шип (попал, долетел) освобождает своё
        /// место сразу — перебирать все места, не останавливаясь на пустом.
        /// </summary>
        public bool TryGetThicketSeed(int id, int index, out ThicketSeedState seed)
        {
            seed = _thicketSeeds != null && (uint)id < (uint)Entities.Capacity && (uint)index < ThicketSeedSlots
                ? _thicketSeeds[id * ThicketSeedSlots + index] : default;
            return seed.Serial != 0;
        }

        /// <summary>Каст терновника босса id (жест, кусты растут или шипы в полёте); false — каста нет.</summary>
        public bool TryGetThicketSeedVolley(int id, out ThicketSeedVolley volley)
        {
            volley = _thicketSeedVolleys != null && (uint)id < (uint)_thicketSeedVolleys.Length ? _thicketSeedVolleys[id] : default;
            return volley.Serial != 0;
        }

        /// <summary>
        /// Куст в месте index (0 … ThicketBushSlots − 1 = Order) босса id: с начала каста до GoneTick; до
        /// SproutTick (Sprouted = false) его на земле ещё нет. false — места нет.
        /// </summary>
        public bool TryGetThicketBush(int id, int index, out ThicketBushState bush)
        {
            bush = _thicketBushes != null && (uint)id < (uint)Entities.Capacity && (uint)index < ThicketBushSlots
                ? _thicketBushes[id * ThicketBushSlots + index] : default;
            return bush.Serial != 0;
        }

        /// <summary>Урон шипа: доля ThicketSeedDamageA9 / 41 удара лапы (9 на арене 9).</summary>
        public int ThicketSeedDamageOf(int id) => ThicketShareOf(id, ThicketSeedDamageA9);

        /// <summary>Кустов в касте по фазе: 2 / 3 / 3.</summary>
        public int ThicketBushCountOf(int id) => ThicketMasterPhase(id) >= 2 ? ThicketBushesPhase2 : ThicketBushesPhase1;

        /// <summary>Ось креста: «×» (диагонали экрана боя) или «+» (вдоль осей экрана).</summary>
        public static FixVec2 ThicketBushAxis(bool diagonal) => diagonal ? ThicketBushCrossAxis : ThicketBushPlusAxis;

        /// <summary>Линия lane (0–3) куста с осью axis: axis, повёрнутая на lane × 90° против часовой.</summary>
        public static FixVec2 ThicketBushLaneDirection(FixVec2 axis, int lane)
        {
            switch (lane & 3)
            {
                case 1: return new FixVec2(-axis.Y, axis.X);
                case 2: return new FixVec2(-axis.X, -axis.Y);
                case 3: return new FixVec2(axis.Y, -axis.X);
                default: return axis;
            }
        }

        /// <summary>Линия шипа на земле — та же фигура, что бьёт: полоса шириной 2 × ThicketSeedRadius.</summary>
        public static EnemyTelegraph ThicketSeedLane(FixVec2 origin, FixVec2 direction, Fix64 length)
            => EnemyTelegraph.Lane(origin, direction, length, ThicketSeedRadius * 2);

        /// <summary>
        /// Полоса, которую шип прошёл от from до to метров пути (как ThornShotSweep): фигура попадания одного
        /// тика полёта, внутри своей линии. Тело героя задето, если касается её краем.
        /// </summary>
        public static EnemyTelegraph ThicketSeedSweep(in ThicketSeedState seed, Fix64 from, Fix64 to)
            => EnemyTelegraph.Lane(seed.Origin + seed.Direction * from, seed.Direction,
                Fix64.Max(Fix64.Zero, to - from), ThicketSeedRadius * 2);

        /// <summary>За сколько тиков шип пролетает путь length (в тик выпуска — первые ThicketSeedSpeed).</summary>
        public static int ThicketSeedFlightTicks(Fix64 length)
        {
            int ticks = (length / ThicketSeedSpeed).ToInt();
            if (ThicketSeedSpeed * ticks < length) ticks++;
            return Math.Max(1, ticks);
        }

        /// <summary>Где остриё шипа к концу прошлого тика (для вида: дальше — ThicketSeedSpeed за тик от ReleaseTick).</summary>
        public static FixVec2 ThicketSeedTip(in ThicketSeedState seed) => seed.Origin + seed.Direction * seed.Travelled;

        /// <summary>Тик, в который шип с выпуском launch дойдёт до точки на расстоянии along от начала пути.</summary>
        private static int ThicketSeedArrival(int launch, Fix64 along)
        {
            if (along.Raw <= 0) return launch;
            return launch + ThicketSeedFlightTicks(along) - 1;
        }

        // ---- выбор и начало ----

        /// <summary>
        /// Терновник может встать сейчас (для жребия): перезарядка, прошлый каст кончился и его кусты ушли,
        /// выпуск первого куста — не раньше окна ответа (QuietUntil), бюджет крупных меток и такт пускают
        /// (по выпуску — контакты не раньше его; точные проверяет StartThicketSeeds).
        /// </summary>
        private bool ThicketSeedsReady(int id)
        {
            if (!Entities.Alive[PlayerId] || Tick < ThicketReadyAt(id, ThicketMasterAction.Seeds)) return false;
            if (ThicketBushesBusy(id)) return false;
            int launch = Tick + ThicketBushWindupTicks;
            if (launch < ThicketMemory[id].QuietUntil) return false;
            return BigMarkAllowed(id, ThicketBushMarkWeight, launch);
        }

        /// <summary>
        /// Вес терновника в жребии сейчас (0 — не готов): ближняя полоса — по самому зазору до кромки
        /// корпуса (ThicketHullGap ≤ ThicketNearGap; ThicketHeroBand у поводка зовёт и прижатого дальним) —
        /// ThicketBushNearWeight в фазе 1 (редко), в фазах 2–3 — 0; средняя и дальняя — фаза 1 — 8, фазы 2–3 — 5.
        /// </summary>
        private int ThicketBushWeightNow(int id)
        {
            bool late = ThicketMemory[id].Phase >= 2;
            bool near = ThicketHullGap(id, Entities.Position[PlayerId]) <= ThicketNearGap;
            if (near && late) return 0;
            if (!ThicketSeedsReady(id)) return 0;
            if (near) return ThicketBushNearWeight;
            return late ? ThicketSeedWeight : ThicketSeedWeightPhase1;
        }

        /// <summary>Каст ещё идёт (шипы не встали) или его кусты ещё не ушли.</summary>
        private bool ThicketBushesBusy(int id)
        {
            if (_thicketSeedVolleys != null && _thicketSeedVolleys[id].Serial != 0) return true;
            if (_thicketBushes == null) return false;
            for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots; k++)
                if (_thicketBushes[k].Serial != 0) return true;
            return false;
        }

        private int[] _thicketBushContacts;

        /// <summary>
        /// Начало каста: места кустов (свой поток босса, ThicketPlaceBushes), их расчётные контакты (шип до
        /// места героя), бюджет крупных меток (вес 2, по самому раннему) и такт всех контактов. Не встало —
        /// попытка через ThicketBushRetryTicks (поток уже потрачен). Встало — жест (действие Seeds: Stages 1,
        /// HitResolved, ImpactTick = LastImpactTick = EndTick = T + 18, Tag — кустов), куст 0 прорастает
        /// сразу, следующие — через ThicketBushEveryTicks (AdvanceThicketBushes).
        /// </summary>
        private bool StartThicketSeeds(int id)
        {
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 heroBody = Entities.BodyRadius[PlayerId];
            _thicketBushSpots ??= new FixVec2[ThicketBushSlots];
            _thicketBushContacts ??= new int[ThicketBushSlots];
            int count = ThicketBushCountOf(id);
            bool diagonal = (ThicketMemory[id].Rng.NextUInt() & 1) != 0;
            int placed = ThicketPlaceBushes(id, hero, count, ref diagonal, _thicketBushSpots);
            bool fits = placed >= ThicketBushesMin;
            int first = int.MaxValue;
            for (int k = 0; k < placed && fits; k++)
            {
                int launch = Tick + ThicketBushEveryTicks * k + ThicketBushWindupTicks;
                Fix64 along = FixVec2.Distance(_thicketBushSpots[k], hero) - ThicketBushThornStart - heroBody;
                int contact = Math.Min(ThicketSeedArrival(launch, along), launch + ThicketSeedFlightTicks(ThicketBushLaneLength) - 1);
                _thicketBushContacts[k] = contact;
                if (contact < first) first = contact;
                if (!HeroContactAllowed(id, contact, contact)) fits = false;
            }
            if (!fits || first < ThicketMemory[id].QuietUntil || !BigMarkAllowed(id, ThicketBushMarkWeight, first))
            {
                ThicketReady[id * ThicketActionSlots + (int)ThicketMasterAction.Seeds] = Tick + ThicketBushRetryTicks;
                return false;
            }
            ref var a = ref BeginThicketCast(id, ThicketMasterAction.Seeds);
            a.Tag = placed;
            var bushes = ThicketBushes;
            int from = id * ThicketBushSlots;
            Array.Clear(bushes, from, ThicketBushSlots);
            Array.Clear(ThicketSeeds, id * ThicketSeedSlots, ThicketSeedSlots);
            int lastLaunch = Tick;
            for (int k = 0; k < placed; k++)
            {
                int sprout = Tick + ThicketBushEveryTicks * k;
                int launch = sprout + ThicketBushWindupTicks, wither = launch + ThicketBushStandTicks;
                bool diag = diagonal ^ ((k & 1) != 0);
                bushes[from + k] = new ThicketBushState
                {
                    Serial = ++_thicketBushSerial, Cast = a.Serial, Order = k, Center = _thicketBushSpots[k],
                    Axis = ThicketBushAxis(diag), Diagonal = diag, SproutTick = sprout, LaunchTick = launch,
                    WitherTick = wither, GoneTick = wither + ThicketBushWitherTicks, ContactTick = _thicketBushContacts[k],
                };
                lastLaunch = launch;
            }
            ThicketSeedVolleys[id] = new ThicketSeedVolley
            {
                Serial = a.Serial, StartTick = Tick, LaunchTick = Tick + ThicketBushWindupTicks, ContactTick = first,
                LastTick = lastLaunch + ThicketSeedFlightTicks(ThicketBushLaneLength) - 1,
                Body = Entities.Position[id], Aim = hero,
            };
            SproutThicketBush(id, ref bushes[from]);
            int seedFrom = id * ThicketSeedSlots;
            for (int k = seedFrom; k < seedFrom + ThicketBushLanes && a.TelegraphSerial == 0; k++)
                a.TelegraphSerial = _thicketSeeds[k].TelegraphSerial;
            SetThicketCooldown(id, ThicketMasterAction.Seeds,
                ThicketMemory[id].Phase >= 2 ? ThicketSeedCooldownTicks : ThicketSeedCooldownPhase1Ticks);
            return true;
        }

        // ---- места кустов ----

        /// <summary>
        /// Места кустов вокруг героя hero (spots), свой поток босса — 4 броска на каст: сторона и
        /// расстояние куста 0, угол и расстояние остальных. Куст 0 (ThicketPlaceNearBush) — в 3–5,5 м от
        /// героя ровно на своей линии: герой стоит на ней и должен сойти; его крест — брошенный diagonal
        /// (или другой, если места нет — diagonal меняется). Остальные (ThicketPlaceFarBush) — в 3,5–8 м
        /// от героя, разнесены по кругу. Сколько встало (куст 0 не встал — 0).
        /// </summary>
        private int ThicketPlaceBushes(int id, FixVec2 hero, int count, ref bool diagonal, FixVec2[] spots)
        {
            ref var rng = ref ThicketMemory[id].Rng;
            int side = rng.NextInt(0, ThicketBushLanes);
            Fix64 near = rng.NextFix(ThicketBushNearMin, ThicketBushNearMax);
            Fix64 angle = rng.NextFix() * Fix64.TwoPi;
            Fix64 far = rng.NextFix(ThicketBushFarMin, ThicketBushFarMax);
            if (!ThicketPlaceNearBush(id, hero, side, near, ref diagonal, out spots[0])) return 0;
            int placed = 1;
            for (int k = 1; k < count && k < ThicketBushSlots; k++)
            {
                Fix64 turn = angle + Fix64.TwoPi * Fix64.Ratio(k - 1, Math.Max(1, count - 1));
                if (ThicketPlaceFarBush(id, hero, turn, far, spots, placed, out FixVec2 spot)) spots[placed++] = spot;
            }
            return placed;
        }

        /// <summary>
        /// Куст 0: в distance (или в зеркальном расстоянии 3–5,5 м) от героя по одной из линий своего креста,
        /// начиная со стороны side, — так, чтобы шип по линии к герою дотянулся до него (камень между —
        /// не годится). Сначала не под полосой HUD, потом где угодно; сначала брошенный крест, потом другой.
        /// Нигде — любое свободное место на distance вокруг (12 направлений). Совсем нет — false.
        /// </summary>
        private bool ThicketPlaceNearBush(int id, FixVec2 hero, int side, Fix64 distance, ref bool diagonal, out FixVec2 spot)
        {
            Fix64 body = Entities.BodyRadius[PlayerId];
            Fix64 mirror = ThicketBushNearMin + ThicketBushNearMax - distance;
            for (int pass = 0; pass < 2; pass++)
                for (int kind = 0; kind < 2; kind++)
                {
                    bool diag = diagonal ^ (kind == 1);
                    FixVec2 axis = ThicketBushAxis(diag);
                    for (int j = 0; j < ThicketBushLanes; j++)
                    {
                        FixVec2 toBush = ThicketBushLaneDirection(axis, side + j);
                        FixVec2 back = ThicketBushLaneDirection(axis, side + j + 2);
                        for (int t = 0; t < 2; t++)
                        {
                            Fix64 d = t == 0 ? distance : mirror;
                            FixVec2 c = hero + toBush * d;
                            if (!ThicketBushSpotFree(id, c, hero, null, 0, pass == 0)) continue;
                            Fix64 reach = GroundPathLength(c, back, ThicketBushThornStart, ThicketBushLaneLength);
                            if (reach.Raw <= 0 || ThicketBushThornStart + reach + body < d) continue;
                            diagonal = diag;
                            spot = c;
                            return true;
                        }
                    }
                }
            for (int pass = 0; pass < 2; pass++)
                for (int j = 0; j < 12; j++)
                {
                    FixVec2 c = hero + FixVec2.FromAngle(Fix64.Pi * side / 2 + ThicketStormProbeStep * j) * distance;
                    if (!ThicketBushSpotFree(id, c, hero, null, 0, pass == 0)) continue;
                    spot = c;
                    return true;
                }
            spot = hero;
            return false;
        }

        /// <summary>Куст 1–2: 12 направлений через 30° от angle на distance (потом на зеркальном 3,5–8 м); сначала не под HUD.</summary>
        private bool ThicketPlaceFarBush(int id, FixVec2 hero, Fix64 angle, Fix64 distance, FixVec2[] spots, int placed, out FixVec2 spot)
        {
            Fix64 mirror = ThicketBushFarMin + ThicketBushFarMax - distance;
            for (int pass = 0; pass < 2; pass++)
                for (int t = 0; t < 2; t++)
                {
                    Fix64 d = t == 0 ? distance : mirror;
                    for (int j = 0; j < 12; j++)
                    {
                        FixVec2 c = hero + FixVec2.FromAngle(angle + ThicketStormProbeStep * j) * d;
                        if (!ThicketBushSpotFree(id, c, hero, spots, placed, pass == 0)) continue;
                        spot = c;
                        return true;
                    }
                }
            spot = hero;
            return false;
        }

        /// <summary>
        /// Место куста c годится: не ближе ThicketBushHeroGap к герою и ThicketBushHullGap к кромке корпуса,
        /// на полу с запасом ThicketBushFloorMargin, не ближе ThicketBushApart к уже поставленным, не на лежащих
        /// метках босса (несработавшие метки и облака пыльцы — куст с радиусом ThicketBushRadius их не
        /// касается); screen — ещё и не ниже героя на экране больше ThicketBushScreenDownMax.
        /// </summary>
        private bool ThicketBushSpotFree(int id, FixVec2 c, FixVec2 hero, FixVec2[] spots, int placed, bool screen)
        {
            if (FixVec2.DistanceSq(c, hero) < ThicketBushHeroGap * ThicketBushHeroGap) return false;
            if (screen && ThicketScreenDown(hero, c) > ThicketBushScreenDownMax) return false;
            if (_layout != null && !_layout.IsWalkable(c, ThicketBushFloorMargin)) return false;
            if (ThicketHullGap(id, c) < ThicketBushHullGap) return false;
            for (int k = 0; k < placed; k++)
                if (FixVec2.DistanceSq(c, spots[k]) < ThicketBushApart * ThicketBushApart) return false;
            for (int slot = 0; slot < _telegraphHighWater; slot++)
            {
                var t = _telegraphs[slot];
                if (t.Serial == 0 || t.Source != id || !t.IsActive) continue;
                if (TelegraphContains(in t, c, ThicketBushRadius)) return false;
            }
            if (_thicketPollen != null)
                for (int k = 0; k < _thicketPollen.Length; k++)
                {
                    var z = _thicketPollen[k];
                    if (z.Serial == 0 || z.Source != id) continue;
                    Fix64 r = z.Radius + ThicketBushRadius;
                    if (FixVec2.DistanceSq(c, z.Center) < r * r) return false;
                }
            return true;
        }

        // ---- куст: прорастание, выпуск, увядание; полёт шипов ----

        /// <summary>
        /// Куст пророс: по линии (Lane, SharedView, удар метки = выпуск куста, живёт до конца пути шипа) и
        /// шипу на каждую из 4 сторон креста; линия, упёршаяся сразу (ствол, край пола), или полный пул меток —
        /// шипа нет. Событие Started(ThicketSeeds, Order + 1), Position — центр куста.
        /// </summary>
        private void SproutThicketBush(int id, ref ThicketBushState b)
        {
            b.Sprouted = true;
            var seeds = ThicketSeeds;
            int from = id * ThicketSeedSlots + b.Order * ThicketBushLanes;
            int opened = 0;
            for (int lane = 0; lane < ThicketBushLanes; lane++)
            {
                FixVec2 direction = ThicketBushLaneDirection(b.Axis, lane);
                Fix64 length = GroundPathLength(b.Center, direction, ThicketBushThornStart, ThicketBushLaneLength);
                if (length.Raw <= 0) continue;
                FixVec2 origin = b.Center + direction * ThicketBushThornStart;
                int flight = ThicketSeedFlightTicks(length);
                int slot = OpenTelegraph(id, ThicketSeedLane(origin, direction, length), b.LaunchTick,
                    b.LaunchTick + flight + TelegraphLingerTicks, TelegraphFlags.SharedView);
                if (!TryGetTelegraph(slot, out var mark)) continue;
                seeds[from + lane] = new ThicketSeedState
                {
                    Serial = ++_thicketSeedSerial, Volley = b.Cast, Index = b.Order * ThicketBushLanes + lane,
                    ReleaseTick = b.LaunchTick, Origin = origin, Direction = direction, Length = length,
                    TelegraphSerial = mark.Serial,
                };
                opened++;
            }
            if (_thicketSeedVolleys != null && _thicketSeedVolleys[id].Serial == b.Cast) _thicketSeedVolleys[id].Count += opened;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThicketSeeds, b.Center, b.Order + 1));
        }

        /// <summary>
        /// Выпуск куста: каждый его шип, чья линия ещё лежит, становится снарядом (EnemyProjectileLaunched,
        /// Amount — номер шипа, Position — начало пути) и в этот же тик пролетает первые ThicketSeedSpeed.
        /// </summary>
        private void LaunchThicketBush(int id, ref ThicketBushState b)
        {
            b.Launched = true;
            var seeds = ThicketSeeds;
            int damage = ThicketSeedDamageOf(id);
            int from = id * ThicketSeedSlots + b.Order * ThicketBushLanes;
            for (int k = from; k < from + ThicketBushLanes; k++)
            {
                ref var s = ref seeds[k];
                if (s.Serial == 0 || s.Released) continue;
                // Линия снята — шипа нет: не нарисовано — не летит.
                int slot = FindTelegraph(s.TelegraphSerial);
                if (slot < 0 || !_telegraphs[slot].IsActive) { s = default; continue; }
                s.Released = true;
                s.ReleaseTick = Tick;
                s.Damage = damage;
                _events.Add(SimEvent.EnemyProjectile(id, PlayerId, s.Serial, EnemyActionKind.ThicketSeeds, s.Origin));
            }
        }

        /// <summary>
        /// Тик терновника (до действия босса): кусты прорастают и выпускают шипы по своим тикам, шипы летят
        /// (выпущенные в этот тик — свои первые ThicketSeedSpeed), увядшие кусты уходят (в GoneTick места нет).
        /// </summary>
        private void AdvanceThicketBushes(int id)
        {
            if (_thicketBushes == null) return;
            int from = id * ThicketBushSlots;
            for (int k = from; k < from + ThicketBushSlots; k++)
            {
                ref var b = ref _thicketBushes[k];
                if (b.Serial == 0) continue;
                if (!b.Sprouted && Tick >= b.SproutTick) SproutThicketBush(id, ref b);
                if (b.Sprouted && !b.Launched && Tick >= b.LaunchTick) LaunchThicketBush(id, ref b);
            }
            FlyThicketSeeds(id);
            // Отражение могло убить босса: кусты и шипы снимет его смерть в следующем тике.
            if (!Entities.Alive[id]) return;
            for (int k = from; k < from + ThicketBushSlots; k++)
                if (_thicketBushes[k].Serial != 0 && Tick >= _thicketBushes[k].GoneTick) _thicketBushes[k] = default;
        }

        /// <summary>Все кусты каста выпустили шипы.</summary>
        private bool ThicketBushesLaunched(int id)
        {
            if (_thicketBushes == null) return true;
            for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots; k++)
                if (_thicketBushes[k].Serial != 0 && !_thicketBushes[k].Launched) return false;
            return true;
        }

        /// <summary>Полёт шипов босса за тик. Все кусты выпустили, и все шипы встали — каста больше нет.</summary>
        private void FlyThicketSeeds(int id)
        {
            if (_thicketSeedVolleys == null || _thicketSeedVolleys[id].Serial == 0) return;
            int from = id * ThicketSeedSlots;
            bool any = false;
            for (int k = from; k < from + ThicketSeedSlots; k++)
            {
                if (_thicketSeeds[k].Serial == 0) continue;
                if (_thicketSeeds[k].Released) FlyThicketSeed(id, k);
                // Отражение могло убить босса: шипы снимет его смерть в следующем тике.
                if (!Entities.Alive[id]) return;
                if (_thicketSeeds[k].Serial != 0) any = true;
            }
            if (!any && ThicketBushesLaunched(id)) _thicketSeedVolleys[id] = default;
        }

        /// <summary>
        /// Шип за тик — как шип Шипомёта (FlyThornShot): проходит от пройденного к новому, не дальше конца
        /// пути; герой задет, если его тело касается полосы этого тика (ThicketSeedSweep). Одно попадание на
        /// каст: каст уже попал — шип летит мимо героя. Встал (попал или конец пути) — линия вспыхивает
        /// (Resolved), EnemyActionImpact: Amount — номер шипа, Position — где встал, Flag — попал.
        /// Часы сдвинули выпуск — шип стоит, пока расписание не догонит пройденное.
        /// </summary>
        private void FlyThicketSeed(int id, int slot)
        {
            var s = _thicketSeeds[slot];
            int flown = Tick - s.ReleaseTick;
            Fix64 from = Fix64.Min(ThicketSeedSpeed * flown, s.Length);
            if (from < s.Travelled) return;
            Fix64 to = Fix64.Min(ThicketSeedSpeed * (flown + 1), s.Length);
            Fix64 stop = to;
            bool hit = false;
            if (Entities.Alive[PlayerId] && !_thicketSeedVolleys[id].HitResolved)
            {
                FixVec2 hero = Entities.Position[PlayerId];
                Fix64 body = Entities.BodyRadius[PlayerId];
                var sweep = ThicketSeedSweep(in s, from, to);
                if (TelegraphContains(in sweep, hero, body))
                {
                    hit = true;
                    // Шип встаёт у тела героя, а не в его центре.
                    stop = Fix64.Clamp(FixVec2.Dot(hero - s.Origin, s.Direction) - body, from, to);
                }
            }
            s.Travelled = stop;
            if (!hit && to < s.Length)
            {
                _thicketSeeds[slot] = s;
                return;
            }
            // Шип встал. Состояние — до урона: отражение может убить босса внутри ApplyAbilityDamage.
            _thicketSeeds[slot] = default;
            if (hit) _thicketSeedVolleys[id].HitResolved = true;
            ResolveTelegraphSerial(s.TelegraphSerial);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketSeeds, s.Origin + s.Direction * stop, s.Serial, hit));
            if (hit) ApplyAbilityDamage(id, PlayerId, s.Damage, -1, DamageType.Physical);
        }

        /// <summary>
        /// Смерть босса или героя: кусты снимаются сразу (места пусты — вид дорисовывает увядание сам),
        /// запланированные и летящие шипы — тоже: линии гаснут (TelegraphCancelled), у летящих —
        /// EnemyActionCancelled (Amount — номер шипа, Position — остриё). Жест каста Cancelled не шлёт
        /// (у жеста контакта нет, как у прорастания).
        /// </summary>
        private void CancelThicketSeeds(int id)
        {
            if (_thicketBushes != null && (uint)id < (uint)Entities.Capacity)
                Array.Clear(_thicketBushes, id * ThicketBushSlots, ThicketBushSlots);
            if (_thicketSeedVolleys == null || (uint)id >= (uint)_thicketSeedVolleys.Length
                || _thicketSeedVolleys[id].Serial == 0) return;
            int from = id * ThicketSeedSlots;
            for (int k = from; k < from + ThicketSeedSlots; k++)
            {
                var s = _thicketSeeds[k];
                if (s.Serial == 0) continue;
                _thicketSeeds[k] = default;
                CancelTelegraphSerial(s.TelegraphSerial);
                if (s.Released)
                    _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                        EnemyActionKind.ThicketSeeds, ThicketSeedTip(in s), s.Serial));
            }
            _thicketSeedVolleys[id] = default;
        }

        // ---- бюджет, такт, жетон, Часы, сброс, хеш ----

        /// <summary>
        /// Вес каста в бюджете крупных меток: ThicketBushMarkWeight (2), пока шипы не встали. Начало — самое
        /// позднее прорастание (метки встают с каждым кустом), удар — самый ранний ещё впереди контакт куста.
        /// </summary>
        private int ThicketSeedsMarkWeight(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            if (_thicketSeedVolleys == null || _thicketSeedVolleys[id].Serial == 0) return 0;
            var v = _thicketSeedVolleys[id];
            start = v.StartTick;
            if (_thicketBushes != null)
                for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots; k++)
                {
                    var b = _thicketBushes[k];
                    if (b.Serial == 0) continue;
                    if (b.Sprouted && b.SproutTick > start) start = b.SproutTick;
                    if (!v.HitResolved && b.ContactTick >= Tick && (impact == int.MinValue || b.ContactTick < impact))
                        impact = b.ContactTick;
                }
            return ThicketBushMarkWeight;
        }

        /// <summary>Контакты кустов в такте ударов по герою: расчётные тики, пока каст не попал.</summary>
        private void AddThicketSeedContacts(int id)
        {
            _thicketImpactScratch ??= new int[ThicketShapeSlots + ThicketBushSlots];
            int n = ThicketBushImpacts(id, _thicketImpactScratch, 0);
            for (int k = 0; k < n; k++) AddHeroContact(id, _thicketImpactScratch[k], _thicketImpactScratch[k]);
        }

        /// <summary>Дописывает в into (с места count) расчётные контакты кустов, что ещё впереди; новое число.</summary>
        private int ThicketBushImpacts(int id, int[] into, int count)
        {
            if (_thicketSeedVolleys == null || _thicketSeedVolleys[id].Serial == 0 || _thicketSeedVolleys[id].HitResolved
                || _thicketBushes == null) return count;
            for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots && count < into.Length; k++)
            {
                var b = _thicketBushes[k];
                if (b.Serial != 0 && b.ContactTick >= Tick) into[count++] = b.ContactTick;
            }
            return count;
        }

        /// <summary>Каст жив (кусты растут или шипы летят): держит крупный жетон и после жеста босса; это и фоновая опасность.</summary>
        private bool ThicketSeedsHoldToken(int id)
            => _thicketSeedVolleys != null && (uint)id < (uint)_thicketSeedVolleys.Length && _thicketSeedVolleys[id].Serial != 0;

        /// <summary>Часы: кусты ждут (прорастание, выпуск, увядание), шипы стоят в воздухе (выпуск сдвигается, как у шипа).</summary>
        private void ShiftThicketSeeds(int id, int ticks)
        {
            if (_thicketSeedVolleys != null && _thicketSeedVolleys[id].Serial != 0)
            {
                ref var v = ref _thicketSeedVolleys[id];
                if (v.LaunchTick >= Tick) v.LaunchTick += ticks;
                if (v.ContactTick >= Tick) v.ContactTick += ticks;
                if (v.LastTick >= Tick) v.LastTick += ticks;
                int from = id * ThicketSeedSlots;
                for (int k = from; k < from + ThicketSeedSlots; k++)
                    if (_thicketSeeds[k].Serial != 0) _thicketSeeds[k].ReleaseTick += ticks;
            }
            if (_thicketBushes == null) return;
            for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots; k++)
            {
                ref var b = ref _thicketBushes[k];
                if (b.Serial == 0) continue;
                if (b.SproutTick >= Tick) b.SproutTick += ticks;
                if (b.LaunchTick >= Tick) b.LaunchTick += ticks;
                if (b.ContactTick >= Tick) b.ContactTick += ticks;
                if (b.WitherTick >= Tick) b.WitherTick += ticks;
                if (b.GoneTick >= Tick) b.GoneTick += ticks;
            }
        }

        private void ResetThicketSeeds()
        {
            if (_thicketSeeds != null) Array.Clear(_thicketSeeds, 0, _thicketSeeds.Length);
            if (_thicketSeedVolleys != null) Array.Clear(_thicketSeedVolleys, 0, _thicketSeedVolleys.Length);
            if (_thicketBushes != null) Array.Clear(_thicketBushes, 0, _thicketBushes.Length);
            _thicketSeedSerial = 0;
            _thicketBushSerial = 0;
        }

        /// <summary>Хеш терновника — только если кусты хоть раз ставились (бои без него хеш не меняют).</summary>
        private void HashThicketSeeds(ref ulong hash)
        {
            if (_thicketBushSerial == 0 || _thicketBushes == null || _thicketSeedVolleys == null) return;
            Hashing.Mix(ref hash, 0x54484253); // "THBS"
            Hashing.Mix(ref hash, _thicketSeedSerial); Hashing.Mix(ref hash, _thicketBushSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                var v = _thicketSeedVolleys[id];
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, v.Serial);
                if (v.Serial != 0)
                {
                    Hashing.Mix(ref hash, v.StartTick); Hashing.Mix(ref hash, v.LaunchTick);
                    Hashing.Mix(ref hash, v.ContactTick); Hashing.Mix(ref hash, v.LastTick);
                    Hashing.Mix(ref hash, v.Count); Hashing.Mix(ref hash, v.HitResolved ? 1 : 0);
                    Hashing.Mix(ref hash, v.Body.X); Hashing.Mix(ref hash, v.Body.Y);
                    Hashing.Mix(ref hash, v.Aim.X); Hashing.Mix(ref hash, v.Aim.Y);
                }
                for (int k = id * ThicketBushSlots; k < (id + 1) * ThicketBushSlots; k++)
                {
                    var b = _thicketBushes[k];
                    Hashing.Mix(ref hash, b.Serial);
                    if (b.Serial == 0) continue;
                    Hashing.Mix(ref hash, b.Cast); Hashing.Mix(ref hash, b.Order);
                    Hashing.Mix(ref hash, b.Center.X); Hashing.Mix(ref hash, b.Center.Y);
                    Hashing.Mix(ref hash, b.Axis.X); Hashing.Mix(ref hash, b.Axis.Y);
                    Hashing.Mix(ref hash, b.Diagonal ? 1 : 0);
                    Hashing.Mix(ref hash, b.SproutTick); Hashing.Mix(ref hash, b.LaunchTick);
                    Hashing.Mix(ref hash, b.WitherTick); Hashing.Mix(ref hash, b.GoneTick); Hashing.Mix(ref hash, b.ContactTick);
                    Hashing.Mix(ref hash, b.Sprouted ? 1 : 0); Hashing.Mix(ref hash, b.Launched ? 1 : 0);
                }
                if (_thicketSeeds == null) continue;
                for (int k = id * ThicketSeedSlots; k < (id + 1) * ThicketSeedSlots; k++)
                {
                    var s = _thicketSeeds[k];
                    Hashing.Mix(ref hash, s.Serial);
                    if (s.Serial == 0) continue;
                    Hashing.Mix(ref hash, s.Volley); Hashing.Mix(ref hash, s.Index);
                    Hashing.Mix(ref hash, s.Released ? 1 : 0); Hashing.Mix(ref hash, s.ReleaseTick);
                    Hashing.Mix(ref hash, s.Damage);
                    Hashing.Mix(ref hash, s.Origin.X); Hashing.Mix(ref hash, s.Origin.Y);
                    Hashing.Mix(ref hash, s.Direction.X); Hashing.Mix(ref hash, s.Direction.Y);
                    Hashing.Mix(ref hash, s.Length); Hashing.Mix(ref hash, s.Travelled);
                    Hashing.Mix(ref hash, s.TelegraphSerial);
                }
            }
        }
    }
}
