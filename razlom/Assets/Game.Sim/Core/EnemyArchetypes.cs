using System;

namespace Game.Sim
{
    /// <summary>
    /// Строка таблицы видов: сколько моб живёт, сколько отнимает за попадание,
    /// во что обходится бюджету угроз и сколько места занимает телом.
    ///
    /// ЧИСЛА ПЕРВОЙ АРЕНЫ, ДО РОСТА ГЛУБИНЫ. Глубину, «Сложно» и подстройку
    /// пачки накладывает расстановка — см. EnemyArchetypes.DepthHealthPercent.
    /// Бой таблицу не читает: Configure* пишет её в лист статов, а удар, как и
    /// раньше, берёт числа из EntityStore. Поэтому предмет или усиление врага
    /// меняют удар тем же путём, что и у героя.
    /// </summary>
    public readonly struct EnemyArchetype
    {
        public readonly EnemyKind Kind;

        /// <summary>Здоровье на первой арене при проценте уровня 100.</summary>
        public readonly int BaseHealth;

        /// <summary>
        /// Урон ОДНОГО попадания: удар Хранителя, укус Корнеполза, один плод
        /// (в залпе их пять), таран Камнекопыта без отброса, коготь Вендиго,
        /// один шип Шипомёта, удар корнями Корнехвата, удар Расщепеня.
        /// </summary>
        public readonly int BaseDamage;

        /// <summary>Цена в бюджете угроз волны. Корнеполз — единица отсчёта.</summary>
        public readonly int Threat;

        /// <summary>Радиус тела. Им же расстановка ищет место под моба.</summary>
        public readonly Fix64 BodyRadius;

        /// <summary>
        /// Окна основной атаки в тиках: замах до контакта, стойка после него и
        /// полный цикл от замаха до следующего замаха. СПРАВОЧНЫЕ: у Хранителя,
        /// Корнеполза, Камнекопыта, Вендиго и новых мобов леса таблица
        /// повторяет их константы в Simulation, а не наоборот. Плюй-плод — единственный, чья настройка
        /// по умолчанию (ForestBudSettings) сама читает таблицу.
        /// </summary>
        public readonly int WindupTicks, RecoveryTicks, CycleTicks;

        public EnemyArchetype(EnemyKind kind, int baseHealth, int baseDamage, int threat, Fix64 bodyRadius,
            int windupTicks, int recoveryTicks, int cycleTicks)
        {
            if (baseHealth < 1 || baseDamage < 0 || threat < 1 || bodyRadius <= Fix64.Zero
                || bodyRadius > EntityStore.MaxBodyRadius || windupTicks < 1 || recoveryTicks < 0
                || cycleTicks < windupTicks + recoveryTicks)
                throw new ArgumentException("Invalid enemy archetype: " + kind);
            Kind = kind; BaseHealth = baseHealth; BaseDamage = baseDamage; Threat = threat;
            BodyRadius = bodyRadius; WindupTicks = windupTicks; RecoveryTicks = recoveryTicks; CycleTicks = cycleTicks;
        }
    }

    /// <summary>
    /// Баланс видов v1 (стадия 2 плана «Мобы леса»).
    ///
    /// Цель — герой 5-го уровня лагеря: 270 здоровья, 54 урона за удар.
    /// Обычный удар моба — 4–8% его здоровья, крупная фигура — 10–16%. Урон
    /// взят примерно на 20% ниже черновика проектировщика, потому что здоровье
    /// героя теперь переносится между аренами (решение владельца от 26.09).
    /// </summary>
    public static class EnemyArchetypes
    {
        // ---- рост с глубиной ----
        //
        // Здоровье ×(1 + 0,07·(арена−1)), урон ×(1 + 0,08·(арена−1)). Здоровье
        // растёт через EnemyHealth уровня (это процент, а не очки), урон —
        // через DamagePerLevelPercent профиля встреч. Функции ниже — эталон,
        // по которому собраны ассеты и прототипный забег.
        public const int HealthPercentPerArena = 7;
        public const int DamagePercentPerArena = 8;

        /// <summary>Маршрут «Сложно»: здоровье и урон врагов арены ещё ×1,25.</summary>
        public const int HardRoutePercent = 125;

        // ---- Вендиго ----
        //
        // Коготь — BaseDamage вида. Прыжок и вой считаются от когтя той же
        // долей, что в таблице: глубина, «Сложно» и ярость растят все три
        // атаки разом, и литерала урона в коде Вендиго больше нет.
        //
        // 32/42/38 → 26/34/30 (стенд баланса, проход 2, 26.09). Вендиго стоит
        // на А4–А8, и рост урона глубины (+8% за арену) поднимал коготь на А6–А8
        // до 17–19% героя 5-го уровня, прыжок — до 24%: крупная фигура дока —
        // 10–16%. Теперь коготь на А6–А8 — 13–15%, вой — 16–17%.
        public const int WendigoClawDamage = 26;
        public const int WendigoLeapDamage = 34;

        /// <summary>
        /// «Вой чащи»: кольцо 2–5,5 м вокруг Вендиго, замах 30 тиков. Крупная
        /// фигура — 11% здоровья эталонного героя, между когтем и прыжком:
        /// от воя уходят за секунду и в любую из двух сторон. Сверху урона —
        /// замедление на секунду (Simulation.WendigoHowlSlowPercent).
        /// </summary>
        public const int WendigoHowlDamage = 30;

        // ---- временный босс ----
        //
        // Хранитель с большим здоровьем до настоящего «Хозяина Чащи». Здоровье
        // растёт с глубиной, как у всех. Удар — Хранителя ×1,25, как было у
        // прежнего босса; ярость на половине здоровья добавляет ещё ×1,3.
        // Подмога на 66% и 33% — ForestEncounterTemplates.BossAdds.
        //
        // 8000 → 8500 (стенд баланса, 26.09). На старом стенде (реакция бота
        // 8 тиков) герой 5-го уровня клал босса за 122 с при цели владельца
        // 150–195, и просили 10000. С реакцией живого игрока (12 тиков) бот
        // тратит на уходы из сектора две трети цикла, и 10000 шли 220–230 с
        // даже у героя 10-го уровня; 8500 — 170 с у 5-го, середина окна.
        //
        // 8500 → 6800 (стенд, проход 2): с подмогой на 66% и 33% (две волны
        // по хранителю и 2–3 роя) босс 5-го уровня шёл 212 с при окне 150–195;
        // 7000 — 180–190 с, 6500 — 155–165 с, 6800 — середина окна.
        public const int InterimBossHealth = 6800;
        public const int InterimBossDamagePercent = 125;

        // ---- Плюй-плод ----
        //
        // Константами, а не только строкой таблицы: ими же заданы значения по
        // умолчанию ForestBudSettings, а параметр по умолчанию обязан быть
        // константой времени компиляции.
        public const int ForestBudHealth = 300;
        public const int ForestBudDamage = 11;
        public const int ForestBudWindupTicks = 24;
        public const int ForestBudRecoveryTicks = 18;
        public const int ForestBudCycleTicks = 135;

        // ---- новые мобы леса (план от 26.09) ----
        //
        // Шипомёт — элита: шип линии — BaseDamage вида, всплеск против
        // объятий — та же доля от шипа, что в таблице (Share), как прыжок
        // Вендиго от когтя. Корнехват бьёт 20 и замедляет. Расщепень —
        // 420/12; его детёныш — доля РОДИТЕЛЯ (120/420 здоровья, 4/12 урона):
        // глубина, «Сложно» и проценты пачки родителя переходят к детям.
        public const int ThorncasterSpikeDamage = 30;
        public const int ThorncasterBurstDamage = 22;
        public const int RootSnarerDamage = 20;
        public const int SplitterHealth = 420, SplitterDamage = 12;
        public const int SplitlingHealth = 120, SplitlingDamage = 4;

        // ---- тела ----
        //
        // Хранитель 0.85: 1.7 м между центрами при силуэте 2.35 м — см.
        // Simulation.ConfigureEnemy. Вендиго — самое толстое тело в игре, по
        // нему поднят EntityStore.MaxBodyRadius.
        public static readonly Fix64 GuardianBodyRadius = Fix64.Ratio(85, 100);
        public static readonly Fix64 RootSwarmBodyRadius = Fix64.Ratio(45, 100);
        public static readonly Fix64 ForestBudBodyRadius = Fix64.Ratio(65, 100);
        public static readonly Fix64 StonehoofBodyRadius = Fix64.Ratio(7, 10);
        public static readonly Fix64 WendigoBodyRadius = Fix64.Ratio(95, 100);
        public static readonly Fix64 ThorncasterBodyRadius = Fix64.Ratio(80, 100);
        public static readonly Fix64 RootSnarerBodyRadius = Fix64.Ratio(75, 100);
        public static readonly Fix64 SplitterBodyRadius = Fix64.Ratio(70, 100);
        public static readonly Fix64 SplitlingBodyRadius = Fix64.Ratio(42, 100);

        // Порядок строк — порядок EnemyKind, начиная с Хранителя. Значения
        // EnemyKind сериализованы и новые идут только в конец, поэтому и
        // таблица растёт только в конец.
        private static readonly EnemyArchetype[] Table =
        {
            //                    вид                        HP    урон  угроза  тело
            // Хранитель 450 → 550 (стенд баланса, 26.09): время арен А3–А8 поднято
            // его здоровьем, а не числом Корнеползов — от сектора бот уходит в
            // 97% замахов, а укус без метки не обходится, и лишний рой убивал бы
            // забег с переносом здоровья, а не растягивал бой.
            new EnemyArchetype(EnemyKind.ForestGuardian,    550,  14, 2, GuardianBodyRadius,
                Simulation.EnemyAttackWindupTicks, Simulation.GuardianSwingRecoveryTicks,
                Simulation.GuardianSwingCycleTicks),
            // Укус 5 → 4 (стенд баланса, 26.09): вместе с циклом 30 и жетоном
            // укуса урон роя по герою упал примерно вдвое; он всё ещё главный
            // (около 70% полученного), но забег с переносом здоровья его держит.
            // 4 → 3 (проход 2): E01 стал семью волнами роя, и укус 4 снимал на
            // А1 до 40% здоровья героя 5-го уровня, а новичку — до 90%. Жетон
            // укуса 2 вместо 3 пробовали: урон роя −5%, не стоит того.
            new EnemyArchetype(EnemyKind.ForestRootSwarm,   130,   3, 1, RootSwarmBodyRadius,
                Simulation.RootSwarmAttackWindupTicks, Simulation.RootSwarmRecoveryTicks,
                Simulation.RootSwarmAttackCooldownTicks),
            new EnemyArchetype(EnemyKind.ForestBud, ForestBudHealth, ForestBudDamage, 2, ForestBudBodyRadius,
                ForestBudWindupTicks, ForestBudRecoveryTicks, ForestBudCycleTicks),
            new EnemyArchetype(EnemyKind.ForestWendigo,    2000, WendigoClawDamage, 6, WendigoBodyRadius,
                Simulation.WendigoClawWindupTicks, Simulation.WendigoClawRecoveryTicks,
                Simulation.WendigoClawCycleTicks),
            // Цикл Камнекопыта — без самого разбега: его длина зависит от арены.
            new EnemyArchetype(EnemyKind.ForestStonehoof,   650,  24, 3, StonehoofBodyRadius,
                Simulation.StonehoofWindupTicks, Simulation.StonehoofBrakeTicks,
                Simulation.StonehoofWindupTicks + Simulation.StonehoofBrakeTicks + Simulation.StonehoofRestTicks),
            // Шипомёт: замах — до первого шипа, стойка — от него до конца
            // заморозки после последнего, цикл — перезарядка линии.
            new EnemyArchetype(EnemyKind.ForestThorncaster, 1700, ThorncasterSpikeDamage, 6, ThorncasterBodyRadius,
                Simulation.ThornLineWindupTicks,
                Simulation.ThornLineLastImpactTicks - Simulation.ThornLineWindupTicks + Simulation.ThornLineRecoveryTicks,
                Simulation.ThornLineCooldownTicks),
            // Корнехват: замах — поза и круг до удара корнями.
            new EnemyArchetype(EnemyKind.ForestRootSnarer,   520, RootSnarerDamage, 3, RootSnarerBodyRadius,
                Simulation.RootSnarerSlamTicks + Simulation.RootSnarerImpactDelayTicks,
                Simulation.RootSnarerRecoveryTicks, Simulation.RootSnarerCooldownTicks),
            // Угроза Расщепеня 4 — вместе с детьми: бюджет волны платит за всё, что встанет.
            new EnemyArchetype(EnemyKind.ForestSplitter, SplitterHealth, SplitterDamage, 4, SplitterBodyRadius,
                Simulation.SplitterSwingWindupTicks, Simulation.SplitterSwingRecoveryTicks,
                Simulation.SplitterSwingCycleTicks),
            // Детёныш: числа строки — для родителя с табличными 420/12; настоящие
            // считает распад от живого родителя (Share).
            new EnemyArchetype(EnemyKind.ForestSplitling, SplitlingHealth, SplitlingDamage, 1, SplitlingBodyRadius,
                Simulation.SplitlingBiteWindupTicks, Simulation.SplitlingBiteRecoveryTicks,
                Simulation.SplitlingBiteCycleTicks),
        };

        public static int Count => Table.Length;
        public static EnemyArchetype At(int index) => Table[index];

        public static bool IsDefined(EnemyKind kind)
            => kind >= EnemyKind.ForestGuardian && (int)kind <= Table.Length;

        /// <summary>
        /// Ставит ли вид расстановка: пачки, волны, шаблоны. Детёныш Расщепеня
        /// появляется только из распада родителя (будущий босс — тоже не сюда).
        /// </summary>
        public static bool IsPlaceable(EnemyKind kind)
            => IsDefined(kind) && kind != EnemyKind.ForestSplitling;

        /// <summary>
        /// Сколько мест в пуле сущностей занимает одна поставленная особь:
        /// Расщепень — сам и его дети (Simulation.SplitChildren), прочие — одно.
        /// Им считается ёмкость пачек и волн: дети встают в пул после смерти
        /// родителя, а его место не освобождается никогда.
        /// </summary>
        public static int BodiesPerSpawn(EnemyKind kind)
            => kind == EnemyKind.ForestSplitter ? 1 + Simulation.SplitChildren : 1;

        /// <summary>
        /// Строка вида. Моб без вида — только в тестах и стендах — живёт по
        /// правилам Хранителя, поэтому и строку получает его.
        /// </summary>
        public static EnemyArchetype Get(EnemyKind kind)
        {
            if (kind == EnemyKind.None) kind = EnemyKind.ForestGuardian;
            if (!IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            return Table[(int)kind - 1];
        }

        /// <summary>Процент здоровья врагов на арене: 100, 107, 114…</summary>
        public static int DepthHealthPercent(int arena)
            => 100 + HealthPercentPerArena * (arena > 1 ? arena - 1 : 0);

        /// <summary>Процент урона врагов на арене: 100, 108, 116…</summary>
        public static int DepthDamagePercent(int arena)
            => 100 + DamagePercentPerArena * (arena > 1 ? arena - 1 : 0);

        /// <summary>
        /// Здоровье после двух процентов — уровня и подстройки пачки — с
        /// округлением до ближайшего и не меньше единицы. Считается в long:
        /// 8000 × 1000% × 1000% в int не помещается.
        /// </summary>
        public static int ScaleHealth(int baseHealth, int levelPercent, int packPercent = 100)
        {
            long scaled = ((long)baseHealth * levelPercent * packPercent + 5000) / 10000;
            return scaled < 1 ? 1 : scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        /// <summary>
        /// Урон другой атаки Вендиго при текущем когте: та же доля, что в
        /// таблице, с округлением до ближайшего. Нулевой коготь (стенд) — ноль.
        /// </summary>
        public static int WendigoShare(int clawDamage, int authoredDamage)
            => clawDamage <= 0 ? 0
                : (int)(((long)clawDamage * authoredDamage + WendigoClawDamage / 2) / WendigoClawDamage);

        /// <summary>
        /// То же правило для любой пары чисел таблицы: value относится к
        /// authoredBase, как ответ — к authored, с округлением до ближайшего.
        /// Всплеск Шипомёта от шипа (22/30), детёныш от Расщепеня (120/420,
        /// 4/12). Ноль и меньше — ноль.
        /// </summary>
        public static int Share(int value, int authored, int authoredBase)
            => value <= 0 || authoredBase <= 0 ? 0
                : (int)(((long)value * authored + authoredBase / 2) / authoredBase);
    }

    public sealed partial class Simulation
    {
        /// <summary>
        /// Здоровье вида на первой арене. Плюй-плод берёт своё из настройки
        /// этой симуляции: тесты подменяют её целиком, и расстановка обязана
        /// видеть подменённое число, а не табличное.
        /// </summary>
        public int ArchetypeHealth(EnemyKind kind)
            => kind == EnemyKind.ForestBud ? ForestBudConfig.Health : EnemyArchetypes.Get(kind).BaseHealth;

        /// <summary>Радиус тела вида — тот же, что поставит ему Configure*.</summary>
        public Fix64 ArchetypeBodyRadius(EnemyKind kind)
            => kind == EnemyKind.ForestBud ? ForestBudConfig.BodyRadius : EnemyArchetypes.Get(kind).BodyRadius;
    }
}
