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
        //
        // Ревью владельца 01.10: «урон поднять, хп снизить; он должен нас
        // убивать размахом перед собой за 3-4 удара; аоешками меньше урона;
        // прыжком если попадает — 1/3 хп». Считано на его аренах (элита А5–А7,
        // вторая элита А7–А8; урон глубины 132/140/148/156%) и герое 270:
        //   коготь 26 → 60: 79 / 84 / 89 на А5 / А6 / А7 — 4 удара с полного
        //     здоровья (на А7 три удара — 267, хватает любой царапины), 94 на
        //     А8 — 3 удара. Это правило владельца, а не окно дока «10–16%»:
        //     коготь и линия Шипомёта — исключения (EnemyArchetypeTests).
        //   прыжок 34 → 64: 84 / 90 / 95 на А5 / А6 / А7 — треть героя (90 —
        //     ровно на средней арене окна), 100 на А8.
        //   вой 30 → 26 и круг когтей 26 (был урон когтя) → 22 — «аоешками
        //     меньше»: 34–39 и 29–33 на А5–А7, меньше половины когтя и меньше,
        //     чем били до ревью (39–44 и 34–38).
        // Все четыре — доли когтя (WendigoShare), поэтому глубина, «Сложно»
        // и подстройка пачки растят их разом, как и прежде.
        public const int WendigoClawDamage = 60;
        public const int WendigoLeapDamage = 64;

        /// <summary>
        /// «Вой чащи»: кольцо 2–5,5 м вокруг Вендиго, замах 30 тиков. Крупная
        /// фигура — 10% здоровья эталонного героя на первой арене, на аренах
        /// Вендиго (А5–А7) — 13–14%: меньше половины когтя (ревью 01.10).
        /// От воя уходят за секунду и в любую из двух сторон. Сверху урона —
        /// замедление на секунду (Simulation.WendigoHowlSlowPercent).
        /// </summary>
        public const int WendigoHowlDamage = 26;

        /// <summary>
        /// Круг когтей против кружения — своя доля когтя (до 01.10 бил уроном
        /// когтя целиком): 29 / 31 / 33 на А5 / А6 / А7. Его дело — отбросить
        /// кружащего и напомнить про спину, а убивает коготь спереди.
        /// </summary>
        public const int WendigoSweepDamage = 22;

        /// <summary>
        /// Здоровье Вендиго 2000 → 900 (ревью 01.10, «хп снизить»): на его
        /// аренах 1152 / 1215 / 1278 — 19 / 20 / 21–22 обычных удара героя
        /// (54 и тяжёлый 68 по очереди), на А8 1341 — 22.
        /// </summary>
        public const int WendigoHealth = 900;

        // ---- временный босс ----
        //
        // Хранитель с большим здоровьем до настоящего «Хозяина Чащи». Здоровье
        // растёт с глубиной, как у всех. Удар — Хранителя ×1,5 (до 29.09 —
        // ×1,25, как у прежнего босса; см. ниже); ярость на половине здоровья
        // добавляет ещё ×1,3.
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
        //
        // 6800 → 6000, удар ×1,25 → ×1,5 («Мобы леса v2», подгонка потока B,
        // 29.09; герой без роста статов — всегда 270/54). С героем, который
        // больше не дорастал до 8-го уровня к боссу, 6800 шли 196–200 с при
        // окне 150–195; 6000 — 178 с. Босс при ×1,25 снимал за бой 5% здоровья:
        // от сектора бот уходит почти всегда, и финал решала только подмога
        // (её число — ForestEncounterTemplates.BossAdds). ×1,5 от удара
        // Хранителя 17 — 15% здоровья героя за попадание, в ярости 20%.
        public const int InterimBossHealth = 6000;
        public const int InterimBossDamagePercent = 150;

        // ---- Хозяин Чащи (босс леса, план 01.10) ----
        //
        // Своё здоровье, не InterimBossHealth: 7300 → 11388 на арене 9. Лапа —
        // урон вида: 14 × 164% = 23 на арене 9; остальные атаки — её доли
        // (Simulation.ThicketShareOf). Тело 0,95 — предел MaxBodyRadius.
        //
        // Баланс 02.10 (стенд artifacts/tools/boss-tune, цели владельца: эксперт
        // ~9 из 10 на лесном забеге, бой 70–100 с). Лапа 25 → 10 — весь урон
        // босса ×0,4; здоровье 6000 → 6600 (+10%) — бой эксперта 77 → 85 с, в
        // середине окна. С корпусом и вступлением, но до решений (ход 2,6,
        // стойка после нырка 24, топот 90) при лапе 25 эксперт выигрывал 33%;
        // с ходом 2,0, стойкой 36 и топотом 150 — при лапе 17 — 62–66%, 13 — 85%,
        // 11 — 89%, 10 — 93% (здоровье 6000, бой 77 с); 10 и 6600 — 90% (1483
        // семени), бой 85,6 с. Перезарядка топота 120–300 меняла не больше шума.
        // Ревью 02.10 (вечер: арена +10%, угол, поворот 2,5°/тик, топот «за
        // спиной», лапа 17 + 10 от плеча, буря 90 + 75) и арены из сегментов
        // Кости: лапа 10 — эксперт 85,5% (1485 боёв), лапа 9 — 90,0%, бой с
        // доливом 83,3 с (стенд artifacts/tools/boss-review-bal).
        // Ревью 02.10, ночь («лапа — основа, темп, дальники», стенд
        // artifacts/tools/boss-sim/bench): под землёй 18% боя → 3%, бой эксперта
        // на F8 сжался 91 → 70 с. Здоровье 6600 → 7600 возвращает длину (F8: эксперт
        // 79 с, ArenaBot 99 с) и прежнюю трудность ArenaBot (забег 55% против 56%,
        // F8 80% против 77,5%); эксперт-уклонист — 100% при любых числах (лапы
        // уходит без рывка) — его трудность — за балансом.
        // Баланс 02.10, ночь («поймать баланс»: трудность — от давления босса и цены
        // ошибки, не от толстой шкуры; стенд artifacts/tools/boss-balance/bench, бот
        // «сильный игрок» — реакция 0,4 с, удары серии 0,27 с, четверть меток на 6 тиков
        // позже): лапа 9 → 15 (на арене 9 — 25, 9% здоровья героя), здоровье 7600 → 7500
        // (в пределах +15% от 6600 — длина боя, не трудность). F8: сильный 88%, дальник
        // 87%, ArenaBot 41%, с бурей 56%, бой сильного ~75 с; лесной забег — заметно
        // труднее (герой приходит с ~210/270).
        // Проверка находок 03.10 (стенд artifacts/tools/boss-gameplay-fix/bench): темп, сыгранный
        // владельцем с Костей, вернулся (отдых 30 / 20 / 12, окно 30 и замах 17 во всех фазах,
        // тычок не быстрее 10, замах топота 42 — ответ ногами), трудность — снова от частоты:
        // лапа 15 → 12 (на арене 9 — 20, 7% здоровья героя). F8: сильный 91 / 88,5% (семена
        // 401–600 / 601–800), ArenaBot 30–38,5%, со слоем бури 48–49%, эксперт 100%, бой сильного
        // ~80 с. Лесной забег (герой приходит с ~218/270): сильный 67%, ArenaBot 11% — по чему мерить,
        // решает владелец (лапа 10 — забег 85% / 30%, F8 98% / 59%).
        // Ревью владельца 03.10 (нырок «под героя», топот в жребии фазы 1; стенд
        // artifacts/tools/boss-balance2/bench): нырки и топоты заняли место части серий —
        // сильный выигрывал 92,6%, бой вырос до ~87 с (время под землёй). Лапа 12 → 14 (на
        // арене 9 — 23, 8,5% здоровья героя; доли бури и топота снижены — их урон прежний,
        // Simulation.ThicketStormDamageA9 / ThicketStompDamageA9), здоровье 7500 → 7300 (бой
        // ~85 с). F8: сильный 87%, ArenaBot 40%, эксперт 99%, дальник 100%; лесной забег —
        // сильный 63%, ArenaBot 21% (лапа 13 и здоровье 7750 — забег 70%, F8 90%).
        public const int ThicketMasterHealth = 7300, ThicketMasterPawDamage = 14;

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
        // 560/12; его детёныш — доля РОДИТЕЛЯ (160/560 здоровья, 4/12 урона):
        // глубина, «Сложно» и проценты пачки родителя переходят к детям.
        //
        // Здоровье — стенд баланса 27.09, выход в игру (герой 5-го уровня, 100
        // сидов): Шипомёт 1700 → 2000, как Вендиго той же угрозы (первая
        // элита на А5 шла 51 с при цели 50–70, на А7 — 44 с при 60–75);
        // Корнехват 520 → 650, как Камнекопыт той же угрозы; Расщепень
        // 420/120 → 560/160 — доля детёныша та же (2/7). До того на угрозу
        // выходило меньше здоровья, чем у старых видов, и их арены шли вдвое
        // быстрее целей. Урон не менялся.
        //
        // Выстрел шипом (требование владельца от 26.09) — обычная дальняя
        // атака Шипомёта: 14 на А1, та же доля от шипа (Share), что и всплеск,
        // поэтому растёт с глубиной и «Сложно» вместе с ним. 14 — удар 11–25
        // из правила меток: замах 21, но вместо фигуры на земле — сам летящий
        // шип (владелец, 26.09: «просто проджектайл, от которого можно
        // увернуться»); бьёт только там, где шип пролетел.
        //
        // Ревью владельца 01.10: «по балансу так же как вендиго — чтоб мы его
        // убивали быстрее, да и он нас тоже». Здоровье 2000 → 900, как у
        // Вендиго: на А5 / А6 / А7 — 1152 / 1215 / 1278, 19 / 20 / 21–22
        // обычных удара. Шип линии 30 → 60, как коготь: 79 / 84 / 89 на
        // А5–А7 — 4 попадания с полного здоровья, 94 на А8 — 3. Всплеск 22 и
        // выстрел 14 остались прежними числами (доли пересчитаны от нового
        // шипа): 29–33 и 18–21 на А5–А7 — заметно слабее линии.
        public const int ThorncasterHealth = 900, ThorncasterSpikeDamage = 60;
        public const int ThorncasterBurstDamage = 22;
        public const int ThorncasterShotDamage = 14;
        public const int RootSnarerHealth = 650, RootSnarerDamage = 20;
        public const int SplitterHealth = 560, SplitterDamage = 12;
        public const int SplitlingHealth = 160, SplitlingDamage = 4;

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

        /// <summary>
        /// 0,8 → 0,88 (ревью 01.10: «модельку увеличить на 10%»): тело
        /// попаданий растёт вместе с моделью. Начало пути выстрела (0,8 м от
        /// центра) и линии (1 м) не сдвигались — геометрия атак та же.
        /// </summary>
        public static readonly Fix64 ThorncasterBodyRadius = Fix64.Ratio(88, 100);
        public static readonly Fix64 RootSnarerBodyRadius = Fix64.Ratio(75, 100);
        public static readonly Fix64 SplitterBodyRadius = Fix64.Ratio(70, 100);
        public static readonly Fix64 SplitlingBodyRadius = Fix64.Ratio(42, 100);
        public static readonly Fix64 ThicketMasterBodyRadius = Fix64.Ratio(95, 100);

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
            //
            // 550/14 → 500/17 (подгонка потока B, 29.09). Замах Хранителя стал
            // на 10% медленнее (цикл 48 → 53), а герой больше не растёт статами:
            // Хранитель жил ~15 с, и А3–А4 шли 53 и 60 с при окнах 40–50 и 45–55.
            // Теперь он умирает быстрее, но бьёт сильнее: урон за жизнь тот же
            // (17/53 × 500 ≈ 14/48 × 550), а арены — в окнах.
            //
            // 500 → 270 (ревью владельца 01.10: «мы должны его убивать за 5–6
            // обычных ударов»). Обычная атака героя 270/54 чередует удар 54 и
            // тяжёлый 68 (×1,25, Simulation.HeavyPrimaryScale): с любого из двух
            // на первой арене без критов — 5 ударов (54+68+54+68+54 = 298,
            // 68+54+68+54+68 = 312), до шестой арены (365 HP) — 5–6, на А7–А8
            // (383 и 402) — 7. Урон 17 и угроза 2 не менялись; баланс арен —
            // общим проходом в конце переделки мобов и Пелага (владелец, 01.10).
            //
            // Серия сабли (Simulation.SabreCombo, 01.10 вечер) заменила 54/68:
            // удары 45 / 45 / 90, и на первой арене Хранитель умирает ровно
            // пятым ударом без критов (45+45+90+45+45 = 270). HP не менялось.
            new EnemyArchetype(EnemyKind.ForestGuardian,    270,  17, 2, GuardianBodyRadius,
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
            new EnemyArchetype(EnemyKind.ForestWendigo, WendigoHealth, WendigoClawDamage, 6, WendigoBodyRadius,
                Simulation.WendigoClawWindupTicks, Simulation.WendigoClawRecoveryTicks,
                Simulation.WendigoClawCycleTicks),
            // Цикл Камнекопыта — без самого разбега: его длина зависит от арены.
            // Таран 24 → 22 (подгонка потока B, 29.09): разбег теперь ещё и
            // оглушает на секунду, а вплотную кабан бьёт клыками (60% тарана).
            // С 24 он один снимал 22–30% здоровья за арену E05, и урок тарана
            // был самой смертной пачкой леса (27 забегов из 300). Ниже 22 нельзя:
            // таран — крупная фигура, от 8% здоровья эталонного героя.
            new EnemyArchetype(EnemyKind.ForestStonehoof,   650,  22, 3, StonehoofBodyRadius,
                Simulation.StonehoofWindupTicks, Simulation.StonehoofBrakeTicks,
                Simulation.StonehoofWindupTicks + Simulation.StonehoofBrakeTicks + Simulation.StonehoofRestTicks),
            // Шипомёт: замах — до первого шипа, стойка — от него до конца
            // заморозки после последнего, цикл — перезарядка линии.
            new EnemyArchetype(EnemyKind.ForestThorncaster, ThorncasterHealth, ThorncasterSpikeDamage, 6, ThorncasterBodyRadius,
                Simulation.ThornLineWindupTicks,
                Simulation.ThornLineLastImpactTicks - Simulation.ThornLineWindupTicks + Simulation.ThornLineRecoveryTicks,
                Simulation.ThornLineCooldownTicks),
            // Корнехват: замах — поза и круг до удара корнями.
            new EnemyArchetype(EnemyKind.ForestRootSnarer, RootSnarerHealth, RootSnarerDamage, 3, RootSnarerBodyRadius,
                Simulation.RootSnarerSlamTicks + Simulation.RootSnarerImpactDelayTicks,
                Simulation.RootSnarerRecoveryTicks, Simulation.RootSnarerCooldownTicks),
            // Угроза Расщепеня 4 — вместе с детьми: бюджет волны платит за всё, что встанет.
            new EnemyArchetype(EnemyKind.ForestSplitter, SplitterHealth, SplitterDamage, 4, SplitterBodyRadius,
                Simulation.SplitterSwingWindupTicks, Simulation.SplitterSwingRecoveryTicks,
                Simulation.SplitterSwingCycleTicks),
            // Детёныш: числа строки — для родителя с табличными 560/12; настоящие
            // считает распад от живого родителя (Share).
            new EnemyArchetype(EnemyKind.ForestSplitling, SplitlingHealth, SplitlingDamage, 1, SplitlingBodyRadius,
                Simulation.SplitlingBiteWindupTicks, Simulation.SplitlingBiteRecoveryTicks,
                Simulation.SplitlingBiteCycleTicks),
            // Хозяин Чащи: окна — лапы (замах 24, удар 1 + стойка 24), цикл —
            // средний темп фазы 1 (~2,6 с). Угроза 20 — бюджету волн не нужна:
            // босса расстановка не ставит (IsPlaceable).
            new EnemyArchetype(EnemyKind.ForestThicketMaster, ThicketMasterHealth, ThicketMasterPawDamage, 20,
                ThicketMasterBodyRadius, Simulation.ThicketPawWindupTicks,
                Simulation.ThicketPawStrikeTicks + Simulation.ThicketPawRecoveryTicks, 78),
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
            => IsDefined(kind) && kind != EnemyKind.ForestSplitling && kind != EnemyKind.ForestThicketMaster;

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
        /// Всплеск Шипомёта от шипа (22/60), детёныш от Расщепеня (160/560,
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
