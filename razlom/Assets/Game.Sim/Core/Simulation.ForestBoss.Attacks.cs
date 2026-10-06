using System;

namespace Game.Sim
{
    /// <summary>
    /// Фоновая опасность Хозяина Чащи (наслоение, темп 02.10): прорастание или
    /// ливень после короткого жеста идут сами, пока босс решает дальше.
    /// Изменяемая структура: каждое поле — в хеш (ThicketHashExtra). Круги —
    /// в местах ThicketShape* (TryGetThicketShape): прорастание 0–5, ливень
    /// 4 × залп + круг.
    /// </summary>
    public struct ThicketHazardState
    {
        /// <summary>Номер опасности; 0 — её нет.</summary>
        public int Serial;

        /// <summary>Sprout или Rain.</summary>
        public ThicketMasterAction Action;

        /// <summary>Тик каста (жест и первая метка).</summary>
        public int StartTick;

        /// <summary>Сколько меток (кругов, залпов) уже встало и сколько всего.</summary>
        public int Stage, Stages;

        /// <summary>Когда встанет следующая метка (пока Stage &lt; Stages).</summary>
        public int NextStageTick;

        /// <summary>Ближайший ещё не случившийся удар и последний удар.</summary>
        public int ImpactTick, LastImpactTick;

        /// <summary>Где стоял герой при последней метке.</summary>
        public FixVec2 Target;
    }

    /// <summary>
    /// ХОЗЯИН ЧАЩИ, этап 2 «Атаки» (спецификация 01.10): нырок в корни,
    /// прорастание, облака пыльцы (Simulation.ForestBoss.Pollen.cs), ягодный
    /// ливень. Ядро (Simulation.ForestBoss.cs / .Brain.cs) зовёт частичные
    /// методы этого файла; своё состояние — ленивые массивы ниже, свой хеш.
    ///
    /// Фазы: нырок — 1–3, прорастание и пыльца — 2–3, ливень — 3.
    ///
    /// Ревью 02.10, ночь («лапа — основа, темп, дальники»): нырок — только
    /// сближение по герою 2 с в дальней полосе (ThicketDiveFarTicks), бугор 18
    /// тиков вместо 30; касты фаз 2–3 — с лапой в жребии или по дальнему и
    /// кайтящему герою на любом расстоянии; ливень фазы 3 — по сроку, раньше бури.
    /// Ревью 03.10 («босс за игру ни разу не залез под землю»): ещё и нырок «под
    /// героя» по сроку (ThicketDiveEvery*, DiveNextTick) — круг там, где герой
    /// стоял, когда бугор тронулся (ThicketDiveUnderHeroBit). Владелец 08.10
    /// («перемещение под землёй оч быстрое»): ход бугра — по пути, 7 м/с
    /// (ThicketDiveTravelPlan: 18–60 тиков, ThicketDiveTravelOf).
    ///
    /// НЫРОК. Замах — уход в землю (ThicketDiveBurrowTicks), потом бугор
    /// ThicketDiveTravelOf тиков (путь на 7 м/с, 18–60) едет к герою своим ходом босса (без оглядки на
    /// поворот, с поводком и стенами; шаг — остаток пути на оставшиеся тики,
    /// не больше ThicketMoundMaxStep), в тик фиксации круг r4,0 встаёт под
    /// героем (не дальше поводка + круга от точки появления — Target), через
    /// ThicketDiveLockTicks (24) босс вылезает (контакт) в точке выхода (Origin):
    /// в поводке, тело целиком помещается, бугор доезжает по прямой, всегда
    /// внутри круга (ThicketDiveLock). Потом стоит ThicketDiveStandTicks (36);
    /// в фазах 2–3 сразу связка — серия лапы или топот (Simulation.ForestBoss.Storm.cs).
    /// От начала ухода до выхода — неуязвим и не цель (ThicketShielded); под
    /// землёй тело ThicketMoundRadius и не расталкивается.
    /// Stage: 0 — уходит, 1 — бугор едет, 2 — круг лежит, 3 — вылез.
    /// EnemyActionStarted ThicketDive: Amount 0 — уход, 1 — бугор, 2 — круг;
    /// EnemyActionImpact — выход (Amount 3, Position — где вылез, Flag — задел).
    ///
    /// НАСЛОЕНИЕ (темп 02.10). Прорастание, пыльца и ливень — короткий жест
    /// босса (ThicketCastGestureTicks), дальше опасность идёт сама
    /// (ThicketHazardState, AdvanceThicketHazard), а босс решает дальше; пока
    /// она идёт (ThicketHazardActive) — только серии лапы.
    ///
    /// ПРОРАСТАНИЕ. 6 кругов r1,5 там, где герой стоит, новый раз в 9 тиков
    /// (первый — в тик каста), удар каждого через 30 после его метки, одно
    /// попадание на круг, без корней. Вес в бюджете — 2 до последнего удара.
    /// EnemyActionStarted ThicketSprout: Amount 0 — жест и круг 0, k — круг k
    /// (Position — его центр). EnemyActionImpact: Amount — номер круга.
    ///
    /// ЛИВЕНЬ. 5 залпов по 4 круга r1,4 вокруг героя по шаблону со щелями
    /// не меньше 2 м (ThicketRainTemplate), залпы через 12 тиков (первый — в
    /// тик каста), удар через 30 после метки залпа, за залп — не больше одного
    /// попадания. Вес — 2. EnemyActionStarted ThicketRain: Amount 0 — жест и
    /// залп 0, k — залп k (Position — герой, центр шаблона). Impact: Amount —
    /// номер залпа.
    ///
    /// Если пул меток полон, удар всё равно решается по сохранённой фигуре —
    /// как у лапы и топота ядра.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- нырок в корни ----

        /// <summary>
        /// Уход 12, бугор — по пути (ниже), круг под героем 24 до выхода. Под землёй — неуязвим
        /// (ThicketShielded) 12 + ход + 24 тика. Ход бугра (владелец 08.10: «само перемещение под
        /// землёй оч быстрое… холмик просто скользит по полу»): путь от тела до героя (в поводке) на
        /// ThicketMoundSpeed (7 м/с), не короче ThicketDiveTravelTicks (18 — прежний ход: короткий
        /// нырок «под героя» до 4,2 м не меняется) и не длиннее ThicketDiveTravelMaxTicks (60, 2 с:
        /// дальше 14 м бугор быстрее, до 10 м/с на 20 м). Было (02.10, ночь — 08.10): всегда 18
        /// тиков, бугор до 16,5 м/с (ThicketMoundMaxStep 0,55). Ход этого нырка — в Tag
        /// (ThicketDiveTravelOf), его начало — ThicketDiveBurrowEndTick.
        /// </summary>
        public const int ThicketDiveBurrowTicks = 12, ThicketDiveTravelTicks = 18, ThicketDiveTravelMaxTicks = 60;

        /// <summary>Ход бугра, м за тик: 7/30 (7 м/с) — по нему считается время хода нырка (ThicketDiveTravelPlan).</summary>
        public static readonly Fix64 ThicketMoundSpeed = Fix64.Ratio(7, 30);

        /// <summary>Ход нырка лежит в Tag со сдвигом 8 (биты 8–15; бит 0 — «под героя»).</summary>
        private const int ThicketDiveTravelShift = 8;
        /// <summary>Стойка после выхода 36 (владелец 02.10: «дать чуть больше окно для атаки», было 24).</summary>
        public const int ThicketDiveLockTicks = 24, ThicketDiveStandTicks = 36;

        /// <summary>Круг выхода 4,0 м (3,5 × 1,15).</summary>
        public static readonly Fix64 ThicketDiveRadius = Fix64.FromInt(4);

        /// <summary>
        /// Нырок-сближение (ревью 02.10, ночь): герой ThicketDiveFarTicks (60, 2 с) тиков подряд
        /// в дальней полосе (ThicketHeroBand == Far, FarTicks). Ближе — нырок «под героя» по своему
        /// сроку (ThicketDiveEvery*, 03.10).
        /// </summary>
        public const int ThicketDiveFarTicks = 60;

        /// <summary>
        /// Перезарядка нырка-СБЛИЖЕНИЯ от начала любого нырка: фаза 1 — 540 (18 с), фазы 2–3 — 450
        /// (15 с; баланс 02.10, ночь — было 360 / 300: дальник был под землёй у босса 10–12% боя);
        /// ×1,25 при подмоге, ×0,85 с половины здоровья, Часы сдвигают (ThicketReady[Dive]).
        /// </summary>
        public const int ThicketDiveCooldownPhase1Ticks = 540, ThicketDiveCooldownTicks = 450;

        /// <summary>
        /// Нырок «под героя» (03.10, владелец: «босс за игру ни разу не залез под землю. надо
        /// участить»): по герою рядом (центром в круге топота, 5,2 м) не раньше 720 (фаза 1, 24 с) /
        /// 180 (фаза 2, 6 с) / 180 (фаза 3; в фазе 3 всегда ярость ×0,85 — 153, 5,1 с, как и в
        /// фазе 2 ниже половины здоровья) тиков от начала ЛЮБОГО нырка (ThicketMasterMemory.DiveNextTick):
        /// сближение и «под героя» сбрасывают оба срока. ×1,25 при подмоге, ×0,85 с половины здоровья, Часы сдвигают.
        /// Срок — нижняя граница: нырок встаёт, только когда босс свободен, герой рядом и лапа готова, —
        /// в фазах 2–3 он на деле приходит раз в ~12–15 с.
        /// Фаза 1 — правилом, в первый свободный тик; фазы 2–3 — в жребии вместо серии лапы
        /// (ThicketDiveWeight 80 к лапе 10 и кастам 6 / 4): свободных выборов там мало (ливень, буря,
        /// связки), и правило забирало их все — касты фазы 3 пропадали. Перебаланс 03.10: фаза 1 —
        /// 450 → 720 (24 с: в фазе 1 первая атака-сближение и ещё один «под героя» примерно в каждом
        /// втором бою — нырок 13% атак фазы 1, а не 19%), фазы 2–3 — 330 → 180 и вес 40 → 80 (нырки
        /// переехали в фазу 2). Проверка находок 03.10: сближение первой атакой больше не отодвигает
        /// первый «под героя» на эти 24 с (он через ThicketDiveFirstTicks от сближения, StartThicketDive) —
        /// «под героя» в фазе 1 почти в каждом бою (F8 41% → 100%, лесной забег 18% → 99%), нырок
        /// 19% атак фазы 1; у сильного ближника ≈ 4,7 нырка за бой в F8 (фаза 1 — 2,0 с первой атакой,
        /// фаза 2 — 1,9, фаза 3 — 0,9) и 4,2 в лесном забеге, под землёй ≈ 10–11% боя. Было (баланс 02.10,
        /// ночь): только по дальнему герою — ближник за бой видел один нырок, первую атаку после вступления.
        /// </summary>
        public const int ThicketDiveEveryPhase1Ticks = 720, ThicketDiveEveryPhase2Ticks = 180, ThicketDiveEveryPhase3Ticks = 180;

        /// <summary>
        /// Первый нырок «под героя» — не раньше чем через 300 тиков (10 с) после конца рёва
        /// вступления (DiveNextTick). Нырок-сближение первой атакой (герой с кромки поляны, тик
        /// конца рёва) ставит тот же срок — 300 от себя, а не ThicketDiveEveryOf (проверка находок
        /// 03.10: иначе первый «под героя» ждал 24 с и в лесном забеге в фазу 1 почти не попадал).
        /// </summary>
        public const int ThicketDiveFirstTicks = 300;

        /// <summary>
        /// Бит Tag нырка: «под героя» (03.10) — нырок не по правилу сближения (герой не в дальней
        /// полосе и не 2 с в ней в тик начала). Круг ложится не туда,
        /// где герой в тик фиксации, а туда, где он стоял, когда бугор тронулся (Stage 1, через 12
        /// тиков после ухода, — Target): кто пошёл прочь от уходящего в землю босса, выходит из
        /// круга ногами (ход бугра + 24 ≥ 42 тика до выхода); кто стоял — рывок. Сближение (дальний герой) — по-прежнему
        /// под героем в тик фиксации.
        /// </summary>
        public const int ThicketDiveUnderHeroBit = 1;

        /// <summary>Тело под землёй — не перегораживает проход, но бьётся.</summary>
        public static readonly Fix64 ThicketMoundRadius = Fix64.Ratio(1, 2);

        /// <summary>
        /// Бугор — не быстрее 0,4 м за тик (12 м/с; 08.10 — было 0,55, 16,5 м/с): потолок, когда
        /// герой убегает от бугра и тот догоняет к фиксации круга; стоящего героя бугор везёт
        /// ровно путь / ход ≤ 7 м/с (до 10 м/с на самом длинном пути).
        /// </summary>
        public static readonly Fix64 ThicketMoundMaxStep = Fix64.Ratio(2, 5);

        // ---- прорастание ----

        public const int ThicketSproutCircles = 6, ThicketSproutEveryTicks = 9, ThicketSproutImpactTicks = 30;
        public const int ThicketSproutCooldownTicks = 450;
        public static readonly Fix64 ThicketSproutRadius = Fix64.Ratio(3, 2);

        /// <summary>
        /// «Корни-плеть» фазы 1 (баланс 02.10, ночь — ответ дальникам; ЖДЁТ ПОДТВЕРЖДЕНИЯ
        /// владельца): то же прорастание, но 3 круга вместо 6 и только по дальнему или кайтящему
        /// герою (лапа не достаёт ThicketKiteTicks подряд), когда лапы в жребии нет. Лапа такого
        /// героя не достаёт, так что перезарядка — всегда «издали», ThicketSproutFarCooldownTicks
        /// (240, 8 с; отдельные 360 не наступали никогда и убраны). В фазах 2–3 — полное
        /// (6 кругов, 450 под лапой).
        /// </summary>
        public const int ThicketSproutPhase1Circles = 3;

        /// <summary>
        /// Каст издали (баланс 02.10, ночь: дальник получал атаку раз в 4–5 с, ближник — раз в 2,4):
        /// если в тик каста лапа героя не достаёт, перезарядка короче — прорастание 240 (было 450;
        /// «Корни-плеть» фазы 1 — всегда 240), пыльца 180 (было 270). Под лапой — прежние.
        /// </summary>
        public const int ThicketSproutFarCooldownTicks = 240, ThicketPollenFarCooldownTicks = 180;

        // ---- упреждение кастов (баланс 02.10, ночь: «дальник уходит из всего ногами») ----

        /// <summary>
        /// Круги каста ложатся не под героя, а туда, где он будет, если не свернёт: герой +
        /// его ход за тик × столько тиков (прорастание и ливень — 20 из 30 до удара: бегущий
        /// прямо попадает краем, свернувший или вставший — нет; пыльца — 12 из 24 до падения),
        /// не дальше ThicketLeadMax (3 м) и на полу (ClampToWalkable). Стоящему — под ноги, как было.
        /// </summary>
        public const int ThicketSproutLeadTicks = 20, ThicketRainLeadTicks = 20, ThicketPollenLeadTicks = 12;
        public static readonly Fix64 ThicketLeadMax = Fix64.FromInt(3);

        /// <summary>Центр круга каста на полу: проба малым радиусом (круги мимо пола никого не бьют и сбивают с толку).</summary>
        private static readonly Fix64 ThicketFloorProbe = Fix64.Ratio(1, 5);

        // ---- ягодный ливень ----

        public const int ThicketRainVolleys = 5, ThicketRainCircles = 4, ThicketRainEveryTicks = 12, ThicketRainImpactTicks = 30;

        /// <summary>
        /// Ливень фазы 3 — по сроку, а не жребием (ревью 02.10, ночь: «ливня почти нет», был
        /// вес 6 в жребии — 0,6 за бой): готов — начинается раньше бури, топотов, нырка и
        /// выбора (буря ждёт конца его кругов), перезарядка 420 (в фазе 3 всегда ×0,85 —
        /// 357 тиков, 11,9 с; было 450). Первый — сразу после рёва 33, до бури. Ждёт только
        /// фоновой опасности, бюджета и такта; под ним — серии лапы.
        /// </summary>
        public const int ThicketRainCooldownTicks = 420;
        public static readonly Fix64 ThicketRainRadius = Fix64.Ratio(7, 5);

        // ---- наслоение ----

        /// <summary>Жест каста (прорастание, пыльца, ливень): босс стоит столько, дальше опасность идёт сама.</summary>
        public const int ThicketCastGestureTicks = 18;

        /// <summary>Удар серии лапы — не ближе этого к удару своей фоновой опасности (не сливаются в один).</summary>
        public const int ThicketOwnContactSpacingTicks = 3;

        /// <summary>Щель между кругами одного залпа — не меньше 2 м от края до края.</summary>
        public static readonly Fix64 ThicketRainGap = Fix64.FromInt(2);

        /// <summary>Шаблон залпа: один круг на герое, три — в 5–5,6 м от него через 120° ± 15°.</summary>
        public static readonly Fix64 ThicketRainRingMin = Fix64.FromInt(5);
        public static readonly Fix64 ThicketRainRingJitter = Fix64.Ratio(3, 5);
        private static readonly Fix64 ThicketRainAngleJitter = Fix64.Pi / 12;

        // ---- общее ----

        /// <summary>Вес каста прорастания и ливня в бюджете крупных меток.</summary>
        public const int ThicketCastMarkWeight = 2;

        /// <summary>
        /// Веса взвешенного выбора рядом с лапой (у неё 10). Касты фаз 2–3 (ревью 02.10, ночь,
        /// «дальники»): с лапой в жребии, когда лапа готова; без неё — только по дальнему
        /// герою или по кайтящему (лапа не достаёт ThicketKiteTicks подряд), на любом
        /// расстоянии (круги ложатся под героя); иначе босс идёт к герою под лапу. Ливень —
        /// по сроку (ThicketRainCooldownTicks), не жребием. Нырок «под героя» фаз 2–3 (03.10) — 80
        /// (перебаланс 03.10, было 40), когда срок пришёл и лапа готова: почти всегда он, но касты
        /// фазы не пропадают (правилом нырок забирал все редкие свободные выборы фазы 3 — касты 5% и
        /// 4% атак → 1% и 1%).
        /// </summary>
        public const int ThicketSproutWeight = 6, ThicketPollenWeight = 4, ThicketDiveWeight = 80;

        /// <summary>Мест под круги на одного босса: 6 прорастания, 5 × 4 ливня или 2 × 3 бури.</summary>
        public const int ThicketShapeSlots = 20;

        private const byte ThicketShapeEmpty = 0, ThicketShapePending = 1, ThicketShapeDone = 2;

        private int[] _thicketShapeSerial, _thicketShapeImpact;
        private byte[] _thicketShapeState;
        private FixVec2[] _thicketShapeCenter;
        private FixVec2[] _thicketRainScratch;

        private ThicketHazardState[] _thicketHazards;
        private int _thicketHazardSerial;
        private ThicketHazardState[] ThicketHazards => _thicketHazards ??= new ThicketHazardState[Entities.Capacity];

        private int[] ThicketShapeSerial => _thicketShapeSerial ??= new int[Entities.Capacity * ThicketShapeSlots];
        private int[] ThicketShapeImpact => _thicketShapeImpact ??= new int[Entities.Capacity * ThicketShapeSlots];
        private byte[] ThicketShapeState => _thicketShapeState ??= new byte[Entities.Capacity * ThicketShapeSlots];
        private FixVec2[] ThicketShapeCenter => _thicketShapeCenter ??= new FixVec2[Entities.Capacity * ThicketShapeSlots];

        // ---- чтение для вида и тестов ----

        /// <summary>Босс под землёй: бугор едет или круг нырка лежит.</summary>
        public bool ThicketUnderground(int id)
        {
            if (_thicketMasters == null || (uint)id >= (uint)_thicketMasters.Length) return false;
            var a = _thicketMasters[id];
            return a.Serial != 0 && a.Action == ThicketMasterAction.Dive && !a.HitResolved && a.Stage >= 1;
        }

        /// <summary>
        /// Круг каста index (прорастание 0–5, ливень 4 × залп + круг): центр,
        /// тик удара, сработал ли. false — круга нет.
        /// </summary>
        /// <summary>Фоновая опасность босса (прорастание или ливень после жеста); false — её нет.</summary>
        public bool TryGetThicketHazard(int id, out ThicketHazardState hazard)
        {
            hazard = _thicketHazards != null && (uint)id < (uint)_thicketHazards.Length ? _thicketHazards[id] : default;
            return hazard.Serial != 0;
        }

        /// <summary>
        /// Идёт ли фоновая опасность босса: круги прорастания или ливня ещё
        /// встают или не ударили, или лежит (падает) его пыльца. Пока идёт —
        /// босс начинает только серии лапы.
        /// </summary>
        public bool ThicketHazardActive(int id)
            => (_thicketHazards != null && (uint)id < (uint)_thicketHazards.Length && _thicketHazards[id].Serial != 0)
                || ThicketPollenOf(id, fallingOnly: false) || ThicketSeedsHoldToken(id);

        /// <summary>
        /// Держит ли опасность выбор босса (только серии лапы): круги прорастания или ливня,
        /// падающая пыльца — всегда; лежащая пыльца (до 4,8 с) — только пока лапа достаёт героя
        /// (баланс 02.10, ночь: дальника босс после пыльцы 5 с только догонял — темп по дальнику
        /// был 4–5 с между атаками; издали под лежащей пыльцой можно прорастание и нырок).
        /// </summary>
        private bool ThicketHazardBlocks(int id)
            => (_thicketHazards != null && (uint)id < (uint)_thicketHazards.Length && _thicketHazards[id].Serial != 0)
                || ThicketSeedsHoldToken(id)
                || ThicketPollenOf(id, fallingOnly: true)
                || (ThicketPollenOf(id, fallingOnly: false) && ThicketPawInReach(id));

        public bool TryGetThicketShape(int id, int index, out FixVec2 center, out int impactTick, out bool resolved)
        {
            center = default; impactTick = 0; resolved = false;
            if (_thicketShapeState == null || (uint)id >= (uint)Entities.Capacity || (uint)index >= ThicketShapeSlots) return false;
            int k = id * ThicketShapeSlots + index;
            if (_thicketShapeState[k] == ThicketShapeEmpty) return false;
            center = _thicketShapeCenter[k]; impactTick = _thicketShapeImpact[k];
            resolved = _thicketShapeState[k] == ThicketShapeDone;
            return true;
        }

        public int ThicketDiveDamageOf(int id) => ThicketShareOf(id, ThicketDiveDamageA9);
        public int ThicketSproutDamageOf(int id) => ThicketShareOf(id, ThicketSproutDamageA9);
        public int ThicketRainDamageOf(int id) => ThicketShareOf(id, ThicketRainDamageA9);

        public static EnemyTelegraph ThicketDiveCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketDiveRadius);
        public static EnemyTelegraph ThicketSproutCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketSproutRadius);
        public static EnemyTelegraph ThicketRainCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketRainRadius);

        /// <summary>
        /// Шаблон залпа ливня вокруг hero (centers — 4 места): один круг на
        /// герое, три — на 5–5,6 м через 120° с разбросом ±15°, поворот всего
        /// шаблона случайный. Край к краю — не меньше ThicketRainGap: от
        /// центрального 5 − 2,8 = 2,2 м, между внешними (угол ≥ 90°) ≥ 4,2 м.
        /// </summary>
        public static void ThicketRainTemplate(ref Pcg32 rng, FixVec2 hero, FixVec2[] centers)
        {
            centers[0] = hero;
            Fix64 turn = rng.NextFix() * Fix64.TwoPi;
            for (int k = 1; k < ThicketRainCircles; k++)
            {
                Fix64 angle = turn + Fix64.TwoPi * Fix64.Ratio(k - 1, 3)
                    + (rng.NextFix() * 2 - Fix64.One) * ThicketRainAngleJitter;
                Fix64 ring = ThicketRainRingMin + rng.NextFix() * ThicketRainRingJitter;
                centers[k] = hero + FixVec2.FromAngle(angle) * ring;
            }
        }

        private int ThicketReadyAt(int id, ThicketMasterAction action) => ThicketReady[id * ThicketActionSlots + (int)action];

        // ---- выбор ----

        /// <summary>
        /// После топотов — нырок. Сближение (ревью 02.10, ночь): герой ThicketDiveFarTicks подряд в
        /// дальней полосе (от 6,5 м за кромкой корпуса, или за поводком дальше лапы — пешком босс
        /// его не достанет), своя перезарядка (ThicketReady[Dive], 18 / 15 с); круг — под ним в тик
        /// фиксации, за поводком — на краю досягаемого (ThicketDiveLock). «Под героя» (03.10,
        /// владелец: «босс за игру ни разу не залез под землю»): герой рядом (в круге топота), срок
        /// пришёл (DiveNextTick: 10 с после вступления, дальше 24 / 6 / 6 с от начала любого
        /// нырка) и нырок встанет сейчас (бюджет крупных меток с подмогой, такт) — иначе обычный
        /// выбор, босс не стоит в ожидании. Здесь — фаза 1; в фазах 2–3 «под героя» идёт в жребий
        /// (ThicketAddCandidates). Серию лапы, топот, каст и связку не прерывает: выбор — только у
        /// свободного босса, связки и ливень с бурей — раньше (ThicketChooseForced), фоновая
        /// опасность держит только лапу (ThicketHazardBlocks).
        /// </summary>
        partial void ThicketChooseRule(int id, ref ThicketMasterAction choice)
        {
            ref var m = ref ThicketMemory[id];
            if ((Tick >= ThicketReadyAt(id, ThicketMasterAction.Dive) && m.FarTicks >= ThicketDiveFarTicks)
                || (m.Phase < 2 && ThicketDiveUnderHeroDue(id)))
                choice = ThicketMasterAction.Dive;
        }

        /// <summary>
        /// Нырок «под героя» пора и встанет сейчас: срок, герой рядом — центром в круге топота
        /// (HugTicks &gt; 0, 5,2 м: ближник; дальник, которого сближение вынесло к боссу, к сроку уже
        /// отошёл), бюджет и такт.
        /// </summary>
        private bool ThicketDiveUnderHeroDue(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (m.DiveNextTick == 0 || Tick < m.DiveNextTick || m.HugTicks == 0) return false;
            return BigMarkAllowed(id, 1, Tick + ThicketDiveBurrowTicks + ThicketDiveTravelPlan(id) + ThicketDiveLockTicks);
        }

        /// <summary>
        /// Ход бугра нырка, начатого сейчас: путь от тела до героя (герой за поводком — до края
        /// поводка, куда бугор доедет) на ThicketMoundSpeed, с округлением вверх, в
        /// [ThicketDiveTravelTicks; ThicketDiveTravelMaxTicks]. Считается в тик начала (выход и
        /// метка бронируются сразу); дальше герой может уйти — бугор догоняет (ThicketMoundMaxStep).
        /// </summary>
        private int ThicketDiveTravelPlan(int id)
        {
            FixVec2 goal = ThicketWithin(ThicketMemory[id].Home, Entities.Position[PlayerId], ThicketLeash);
            Fix64 path = FixVec2.Distance(Entities.Position[id], goal);
            int ticks = (path / ThicketMoundSpeed).ToInt();
            if (ThicketMoundSpeed * ticks < path) ticks++;
            return Math.Max(ThicketDiveTravelTicks, Math.Min(ThicketDiveTravelMaxTicks, ticks));
        }

        /// <summary>Ход бугра нырка a, тиков (Tag, биты 8–15; 0 — прежние ThicketDiveTravelTicks).</summary>
        public static int ThicketDiveTravelOf(in ThicketMasterState a)
        {
            int travel = (a.Tag >> ThicketDiveTravelShift) & 0xFF;
            return travel > 0 ? travel : ThicketDiveTravelTicks;
        }

        /// <summary>
        /// Тик, когда нырок a уходит под землю и бугор трогается (Stage 1, Started(Dive, 1)):
        /// ImpactTick − ThicketDiveLockTicks − ход. От ImpactTick — его сдвигают Часы.
        /// </summary>
        public static int ThicketDiveBurrowEndTick(in ThicketMasterState a)
            => a.ImpactTick - ThicketDiveLockTicks - ThicketDiveTravelOf(a);

        /// <summary>Срок нырка «под героя» по фазе (до множителей): 720 / 180 / 180.</summary>
        private int ThicketDiveEveryOf(int id)
        {
            int phase = ThicketMemory[id].Phase;
            return phase >= 3 ? ThicketDiveEveryPhase3Ticks : phase == 2 ? ThicketDiveEveryPhase2Ticks : ThicketDiveEveryPhase1Ticks;
        }

        /// <summary>
        /// Ливень фазы 3 по сроку: готов и встанет сейчас (бюджет, такт) — не держит ни лапу,
        /// ни бурю: не встаёт — обычный выбор.
        /// </summary>
        private bool ThicketRainDue(int id)
            => ThicketMemory[id].Phase >= 3 && Tick >= ThicketReadyAt(id, ThicketMasterAction.Rain) && ThicketRainFits(id);

        /// <summary>
        /// Касты фаз 2–3 (перезарядка готова): в жребии с лапой, когда она готова; без неё —
        /// только по дальнему или кайтящему герою (ответ дальникам: круги ложатся под героя
        /// на любом расстоянии). Иначе — ничего: босс идёт к герою под лапу. Нырок «под героя»
        /// фаз 2–3 (03.10) — тоже в жребии с лапой, вес ThicketDiveWeight.
        /// </summary>
        partial void ThicketAddCandidates(int id)
        {
            ref var m = ref ThicketMemory[id];
            bool withPaw = _thicketCandidateCount > 0;
            bool kiting = m.OutOfReachTicks >= ThicketKiteTicks || ThicketHeroBand(id) == ThicketBand.Far;
            if (m.Phase < 2)
            {
                // Фаза 1 (баланс 02.10, ночь; ждёт подтверждения владельца): дальнику — короткие
                // «Корни-плеть» (3 круга), а не одни нырки.
                if (!withPaw && kiting && Tick >= ThicketReadyAt(id, ThicketMasterAction.Sprout))
                    AddThicketCandidate(ThicketMasterAction.Sprout, ThicketSproutWeight);
                // «Терновник» (владелец 08.10; до него — «Веер шипов-семян», 07.10) — главный ответ дальнику
                // фазы 1: средняя и дальняя полосы — 8, ближняя — 2 (Simulation.ForestBoss.Seeds).
                AddThicketCandidate(ThicketMasterAction.Seeds, ThicketBushWeightNow(id));
                return;
            }
            if (withPaw && ThicketDiveUnderHeroDue(id)) AddThicketCandidate(ThicketMasterAction.Dive, ThicketDiveWeight);
            // Терновник в фазах 2–3 — реже (перезарядка 12 с, вес 5; ближняя полоса — нет).
            AddThicketCandidate(ThicketMasterAction.Seeds, ThicketBushWeightNow(id));
            if (!withPaw && !kiting) return;
            if (Tick >= ThicketReadyAt(id, ThicketMasterAction.Sprout))
                AddThicketCandidate(ThicketMasterAction.Sprout, ThicketSproutWeight);
            if (Tick >= ThicketReadyAt(id, ThicketMasterAction.Pollen) && ThicketLivePollenZones() == 0)
                AddThicketCandidate(ThicketMasterAction.Pollen, ThicketPollenWeight);
        }

        partial void ThicketStartExtra(int id, ThicketMasterAction action, ref bool started)
        {
            switch (action)
            {
                case ThicketMasterAction.Dive: started = StartThicketDive(id); return;
                case ThicketMasterAction.Sprout: started = StartThicketSprout(id); return;
                case ThicketMasterAction.Pollen: started = StartThicketPollen(id); return;
                case ThicketMasterAction.Rain: started = StartThicketRain(id); return;
                case ThicketMasterAction.Storm: started = StartThicketStorm(id); return;
                case ThicketMasterAction.Seeds: started = StartThicketSeeds(id); return;
            }
        }

        partial void ThicketAdvanceExtra(int id)
        {
            switch (ThicketMasters[id].Action)
            {
                case ThicketMasterAction.Dive: AdvanceThicketDive(id); return;
                case ThicketMasterAction.Sprout:
                case ThicketMasterAction.Rain:
                case ThicketMasterAction.Pollen:
                case ThicketMasterAction.Seeds:
                    // Жест каста: опасность (круги, облака, кусты терновника) уже идёт сама, босс стоит до EndTick.
                    if (Tick >= ThicketMasters[id].EndTick) FinishThicketAction(id, ThicketRestTicks(id));
                    return;
                case ThicketMasterAction.Storm: AdvanceThicketStorm(id); return;
            }
        }

        /// <summary>Снятое действие: тело из-под земли — в свой размер, круги бури — прочь. Фоновую опасность снимает CancelThicketHazard.</summary>
        partial void ThicketCancelExtra(int id)
        {
            var a = ThicketMasters[id];
            if (a.Action == ThicketMasterAction.Dive)
            {
                // Снят под землёй (смерть героя): тело — в своё место, не в дерево.
                Fix64 body = EnemyArchetypes.ThicketMasterBodyRadius;
                if (Entities.Alive[id] && _layout != null && !_layout.IsWalkable(Entities.Position[id], body))
                    Entities.Position[id] = ThicketDiveSurface(id, Entities.Position[id]);
                Entities.BodyRadius[id] = body;
            }
            if (a.Action == ThicketMasterAction.Storm) ClearThicketShapes(id);
            if (a.Action == ThicketMasterAction.Pollen) DropFallingPollen(id);
        }

        /// <summary>
        /// Снимает фоновую опасность (смерть босса или героя): несработавшие
        /// метки гаснут (TelegraphCancelled), круги — прочь, падающая пыльца не ложится.
        /// </summary>
        private void CancelThicketHazard(int id)
        {
            if (_thicketHazards == null || (uint)id >= (uint)_thicketHazards.Length || _thicketHazards[id].Serial == 0) return;
            if (_thicketShapeState != null)
            {
                int from = id * ThicketShapeSlots;
                for (int k = from; k < from + ThicketShapeSlots; k++)
                    if (_thicketShapeState[k] == ThicketShapePending) CancelTelegraphSerial(_thicketShapeSerial[k]);
            }
            ClearThicketShapes(id);
            _thicketHazards[id] = default;
        }

        private void ClearThicketShapes(int id)
        {
            if (_thicketShapeState == null) return;
            int from = id * ThicketShapeSlots;
            Array.Clear(_thicketShapeState, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeSerial, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeImpact, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeCenter, from, ThicketShapeSlots);
        }

        // ---------- нырок в корни ----------

        /// <summary>
        /// Нырок: крупная метка весом 1 бронируется с начала ухода — круг ляжет через 12 + ход
        /// (ThicketDiveTravelPlan, 18–60) тиков, выход — ещё через 24. Оба срока — от этого начала: сближение (18 / 15 с) и «под героя»
        /// (24 / 6 / 6 с; после сближения первой атакой вступления — 10 с); счёт дальней полосы — заново. Герой не в дальней полосе — нырок «под
        /// героя» (ThicketDiveUnderHeroBit в Tag): круг — где он стоял, когда бугор тронулся.
        /// </summary>
        private bool StartThicketDive(int id)
        {
            // Ход бугра — по пути (08.10): дальний нырок дольше под землёй, короткий — прежние 18.
            int travel = ThicketDiveTravelPlan(id);
            int impact = Tick + ThicketDiveBurrowTicks + travel + ThicketDiveLockTicks;
            if (!BigMarkAllowed(id, 1, impact)) return false;
            ref var m = ref ThicketMemory[id];
            bool under = m.FarTicks < ThicketDiveFarTicks && ThicketHeroBand(id) != ThicketBand.Far;
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Dive, impact, impact, impact + ThicketDiveStandTicks, 1,
                Entities.Facing[id], Entities.Position[PlayerId]);
            a.Tag = travel << ThicketDiveTravelShift;
            if (under) a.Tag |= ThicketDiveUnderHeroBit;
            SetThicketCooldown(id, ThicketMasterAction.Dive,
                m.Phase >= 2 ? ThicketDiveCooldownTicks : ThicketDiveCooldownPhase1Ticks);
            // Сближение первой атакой в тик конца рёва вступления (герой с кромки поляны — всегда)
            // не отодвигает первый нырок «под героя»: он через ThicketDiveFirstTicks от этого нырка,
            // а не через 24 с (проверка находок 03.10: в лесном забеге фаза 1 ≈ 24 с, и «под героя»
            // в ней был лишь в 18% боёв; сам этот нырок прячется в возврате камеры кат-сцены).
            m.DiveNextTick = Tick + (ThicketIntroOpenerDue(id) ? ThicketDiveFirstTicks : ThicketScaled(id, ThicketDiveEveryOf(id)));
            m.FarTicks = 0;
            return true;
        }

        /// <summary>Нырок «под героя» (03.10): круг — где герой стоял, когда бугор тронулся (Target со Stage 1).</summary>
        public static bool ThicketDiveUnderHero(in ThicketMasterState a)
            => a.Action == ThicketMasterAction.Dive && (a.Tag & ThicketDiveUnderHeroBit) != 0;

        /// <summary>Тик фиксации круга. Считается от ImpactTick — его сдвигают Часы, StartTick — нет.</summary>
        private static int ThicketDiveLockTick(in ThicketMasterState a) => a.ImpactTick - ThicketDiveLockTicks;

        /// <summary>
        /// Ход под землёй — свой, вместо ядра. Уход: стоит. Бугор: к герою (нырок «под
        /// героя» — к месту, где герой стоял, когда бугор тронулся, Target), шаг — остаток пути
        /// на оставшиеся тики хода (ThicketDiveTravelOf; в тик фиксации — под ним), не больше
        /// ThicketMoundMaxStep: стоящего героя бугор везёт ровно, ≤ 7 м/с. Круг лежит: к точке
        /// выхода (Origin). Поводок и стены — как у обычного шага; круп не выталкивает.
        /// </summary>
        partial void ThicketMoveExtra(int id, ref bool handled)
        {
            var a = ThicketMasters[id];
            if (a.Action != ThicketMasterAction.Dive || a.HitResolved) return;
            handled = true;
            if (a.Stage == 0) return;
            FixVec2 from = Entities.Position[id];
            FixVec2 goal = a.Stage != 1 ? a.Origin : ThicketDiveUnderHero(a) ? a.Target : Entities.Position[PlayerId];
            // Бугор едет к герою не дальше поводка (08.10): шаг — остаток досягаемого пути, без рывка за край.
            if (a.Stage == 1) goal = ThicketWithin(ThicketMemory[id].Home, goal, ThicketLeash);
            FixVec2 toGoal = goal - from;
            if (toGoal.LengthSq.Raw == 0) return;
            Fix64 distance = toGoal.Length;
            FixVec2 direction = toGoal / distance;
            Fix64 step = ThicketMoundMaxStep;
            if (a.Stage == 1)
            {
                int remaining = Math.Max(1, ThicketDiveLockTick(a) - Tick + 1);
                step = Fix64.Min(step, distance / remaining);
            }
            FixVec2 delta = direction * Fix64.Min(step, distance);
            Entities.Facing[id] = direction;
            FixVec2 next = from + delta;
            ref var m = ref ThicketMemory[id];
            Fix64 leashSq = ThicketLeash * ThicketLeash;
            if (FixVec2.DistanceSq(next, m.Home) > leashSq && FixVec2.DistanceSq(next, m.Home) > FixVec2.DistanceSq(from, m.Home))
                return;
            if (delta.LengthSq.Raw == 0) return;
            Entities.Position[id] = EnemyStep(id, from, delta);
            Entities.Velocity[id] = Entities.Position[id] - from;
        }

        private void AdvanceThicketDive(int id)
        {
            ref var a = ref ThicketMasters[id];
            int lockTick = ThicketDiveLockTick(a);
            if (a.Stage == 0 && Tick >= ThicketDiveBurrowEndTick(a))
            {
                // Ушёл: дальше едет бугор, тело — маленькое. «Под героя» — бугор едет туда, где
                // герой сейчас, и круг ляжет там же (Target): кто уже идёт прочь, уходит ногами.
                a.Stage = 1;
                a.StageStartTick = Tick;
                if (ThicketDiveUnderHero(a)) a.Target = Entities.Position[PlayerId];
                Entities.BodyRadius[id] = ThicketMoundRadius;
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketDive, Entities.Position[id], 1));
            }
            else if (a.Stage == 1 && Tick >= lockTick)
            {
                // Круг фиксируется под героем (или на краю досягаемого); бугор
                // доезжает к точке выхода — она всегда внутри круга.
                a.Stage = 2;
                a.StageStartTick = Tick;
                ThicketDiveLock(id, ref a);
                OpenThicketMark(id, ref a, ThicketDiveCircle(a.Target), TelegraphFlags.SharedView);
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketDive, a.Target, 2));
            }
            if (a.Stage == 2 && !a.HitResolved && Tick >= a.ImpactTick)
            {
                ResolveThicketDive(id);
                return;
            }
            // Стоит 36 после выхода; связка фаз 2–3 (лапа или топот) — в ThicketFinishedExtra.
            if (a.Stage == 3 && Tick >= a.EndTick) FinishThicketAction(id, ThicketRestTicks(id));
        }

        /// <summary>Выход: тело — в свой размер, удар по кругу (подброс без контроля — только вид).</summary>
        private void ResolveThicketDive(int id)
        {
            ref var a = ref ThicketMasters[id];
            // Состояние — до урона: отражение может убить босса внутри ApplyAbilityDamage.
            a.HitResolved = true;
            a.Stage = 3;
            a.StageStartTick = Tick;
            a.Direction = Entities.Facing[id];
            // Вылезает в точке выхода (Origin): тело там помещается, она в круге.
            Entities.Position[id] = a.Origin;
            Entities.BodyRadius[id] = EnemyArchetypes.ThicketMasterBodyRadius;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool hit = live && Entities.Alive[PlayerId]
                && TelegraphContains(ThicketDiveCircle(a.Target), Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketDive, Entities.Position[id], 3, hit));
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketDiveDamageOf(id), -1, DamageType.Physical);
            ReleaseThicketHeldDamage(id);
        }

        /// <summary>
        /// Песочные Часы кончились, пока босс был в нырке: урон, накопленный под
        /// ними до ухода, не пропадает (ReleaseHeldDamage его оставил), а
        /// приходит в тик выхода. Часы ещё идут — ждёт их обычного конца.
        /// </summary>
        private void ReleaseThicketHeldDamage(int id)
        {
            if (_heldDamage == null || TimeStopped || !Entities.Alive[id]) return;
            int held = _heldDamage[id];
            if (held <= 0) return;
            _heldDamage[id] = 0;
            Entities.Health[id] -= held;
            _events.Add(SimEvent.Damage(PlayerId, id, held, true, Entities.Position[id], DamageType.Physical, DamageOrigin.Ability, -1));
            if (Entities.Health[id] <= 0) Kill(id, PlayerId, -1);
        }

        /// <summary>
        /// Фиксация круга нырка. Круг (Target) — под героем (нырок «под героя» — где
        /// он стоял, когда бугор тронулся, 03.10), но не дальше
        /// поводка + круга от точки появления: герой за поводком тоже под
        /// угрозой. Точка выхода (Origin) — ближайшая к кругу в поводке, где
        /// тело босса целиком помещается и куда бугор доедет по прямой
        /// (ThicketDiveSurface). Круг при нужде подвигается к ней — босс всегда
        /// вылезает в своём круге, удар и выход совпадают.
        /// </summary>
        private void ThicketDiveLock(int id, ref ThicketMasterState a)
        {
            FixVec2 home = ThicketMemory[id].Home;
            FixVec2 aim = ThicketDiveUnderHero(a) ? a.Target : Entities.Position[PlayerId];
            FixVec2 circle = ThicketWithin(home, aim, ThicketLeash + ThicketDiveRadius);
            FixVec2 surface = ThicketDiveSurface(id, ThicketWithin(home, circle, ThicketLeash - ThicketLeashSlack));
            FixVec2 off = circle - surface;
            if (off.LengthSq > ThicketDiveRadius * ThicketDiveRadius)
                circle = surface + off.Normalized() * (ThicketDiveRadius - ThicketLeashSlack);
            a.Target = circle;
            a.Origin = surface;
        }

        /// <summary>Шаг поиска точки выхода и запас на округление у края поводка.</summary>
        private static readonly Fix64 ThicketSurfaceProbe = Fix64.Ratio(1, 2);
        private static readonly Fix64 ThicketLeashSlack = Fix64.Ratio(1, 100);

        /// <summary>point, подтянутая к home не дальше radius.</summary>
        private static FixVec2 ThicketWithin(FixVec2 home, FixVec2 point, Fix64 radius)
        {
            FixVec2 off = point - home;
            return off.LengthSq > radius * radius ? home + off.Normalized() * radius : point;
        }

        /// <summary>
        /// Где вылезет босс: ближайшая к want точка в поводке, где его тело
        /// помещается и куда бугор (ThicketMoundRadius) доедет от себя по прямой —
        /// кольца через 0,5 м до радиуса круга. Не нашлось (стена между бугром и
        /// героем) — то же вокруг последней точки прямой бугор → want, куда он
        /// доезжает; совсем тупик — ближайшее место тела на карте.
        /// </summary>
        private FixVec2 ThicketDiveSurface(int id, FixVec2 want)
        {
            if (_layout == null) return want;
            FixVec2 mound = Entities.Position[id];
            if (ThicketSurfaceAround(id, mound, want, out FixVec2 found)) return found;
            FixVec2 reach = mound;
            FixVec2 toWant = want - mound;
            if (toWant.LengthSq.Raw > 0)
            {
                Fix64 length = toWant.Length;
                FixVec2 direction = toWant / length;
                int steps = (length / ThicketSurfaceProbe).ToInt();
                for (int k = 1; k <= steps; k++)
                {
                    FixVec2 p = mound + direction * (ThicketSurfaceProbe * k);
                    if (!_layout.CanTravel(mound, p, ThicketMoundRadius)) break;
                    reach = p;
                }
            }
            if (ThicketSurfaceAround(id, mound, reach, out found)) return found;
            return _layout.ClampToWalkable(mound, EnemyArchetypes.ThicketMasterBodyRadius);
        }

        private bool ThicketSurfaceAround(int id, FixVec2 mound, FixVec2 center, out FixVec2 found)
        {
            FixVec2 home = ThicketMemory[id].Home;
            Fix64 leashSq = ThicketLeash * ThicketLeash;
            Fix64 body = EnemyArchetypes.ThicketMasterBodyRadius;
            int rings = (ThicketDiveRadius / ThicketSurfaceProbe).ToInt();
            for (int ring = 0; ring <= rings; ring++)
            {
                int directions = ring == 0 ? 1 : 12;
                for (int k = 0; k < directions; k++)
                {
                    FixVec2 c = center + FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 12)) * (ThicketSurfaceProbe * ring);
                    if (FixVec2.DistanceSq(c, home) > leashSq || !_layout.IsWalkable(c, body)
                        || !_layout.CanTravel(mound, c, ThicketMoundRadius)) continue;
                    found = c;
                    return true;
                }
            }
            found = center;
            return false;
        }

        // ---------- прорастание и ливень: жест и фоновая опасность ----------

        /// <summary>
        /// Прорастание: жест ThicketCastGestureTicks, первый круг — сразу под
        /// героем, остальные 5 — раз в 9 тиков уже без босса (AdvanceThicketHazard).
        /// </summary>
        private bool StartThicketSprout(int id)
        {
            // Фаза 1 — «Корни-плеть»: 3 круга (баланс 02.10, ночь; ждёт подтверждения владельца) — только
            // по герою, которого лапа не достаёт, так что перезарядка — «издали» (240).
            bool short1 = ThicketMemory[id].Phase < 2;
            int circles = short1 ? ThicketSproutPhase1Circles : ThicketSproutCircles;
            int first = Tick + ThicketSproutImpactTicks;
            int last = first + ThicketSproutEveryTicks * (circles - 1);
            if (!BigMarkAllowed(id, ThicketCastMarkWeight, first) || !HeroContactAllowed(id, first, last)) return false;
            BeginThicketCast(id, ThicketMasterAction.Sprout);
            BeginThicketHazard(id, ThicketMasterAction.Sprout, circles, last);
            SetThicketCooldown(id, ThicketMasterAction.Sprout,
                ThicketPawInReach(id) ? ThicketSproutCooldownTicks : ThicketSproutFarCooldownTicks);
            return true;
        }

        /// <summary>Встанет ли ливень в этот тик: бюджет крупных меток и такт всех пяти залпов.</summary>
        private bool ThicketRainFits(int id)
        {
            int first = Tick + ThicketRainImpactTicks;
            int last = first + ThicketRainEveryTicks * (ThicketRainVolleys - 1);
            return BigMarkAllowed(id, ThicketCastMarkWeight, first) && HeroContactAllowed(id, first, last);
        }

        /// <summary>Ливень: жест, первый залп — сразу, остальные 4 — раз в 12 тиков уже без босса.</summary>
        private bool StartThicketRain(int id)
        {
            int first = Tick + ThicketRainImpactTicks;
            int last = first + ThicketRainEveryTicks * (ThicketRainVolleys - 1);
            if (!ThicketRainFits(id)) return false;
            BeginThicketCast(id, ThicketMasterAction.Rain);
            BeginThicketHazard(id, ThicketMasterAction.Rain, ThicketRainVolleys, last);
            SetThicketCooldown(id, ThicketMasterAction.Rain, ThicketRainCooldownTicks);
            return true;
        }

        /// <summary>
        /// Жест каста: действие без контакта (HitResolved сразу), StageStartTick —
        /// начало, ImpactTick = LastImpactTick = EndTick — конец жеста. Событие
        /// Started (Amount 0) — общее с первой меткой опасности.
        /// </summary>
        private ref ThicketMasterState BeginThicketCast(int id, ThicketMasterAction action)
        {
            int end = Tick + ThicketCastGestureTicks;
            ref var a = ref BeginThicketAction(id, action, end, end, end, 1, Entities.Facing[id], Entities.Position[PlayerId]);
            a.HitResolved = true;
            return ref a;
        }

        /// <summary>Заводит фоновую опасность и ставит её первую метку в этот же тик.</summary>
        private void BeginThicketHazard(int id, ThicketMasterAction action, int stages, int last)
        {
            ClearThicketShapes(id);
            ref var h = ref ThicketHazards[id];
            h = new ThicketHazardState
            {
                Serial = ++_thicketHazardSerial, Action = action, StartTick = Tick, Stages = stages,
                NextStageTick = Tick, LastImpactTick = last,
            };
            PlaceThicketHazardStage(id, ref h);
        }

        /// <summary>
        /// Следующая метка опасности: круг прорастания под героем или залп ливня
        /// вокруг него. Метка 0 идёт под событием жеста; с 1-й — своё Started
        /// (Amount — номер, Position — центр круга или герой).
        /// </summary>
        private void PlaceThicketHazardStage(int id, ref ThicketHazardState h)
        {
            bool rain = h.Action == ThicketMasterAction.Rain;
            // Упреждение (баланс 02.10, ночь): туда, где герой будет, если не свернёт; на полу.
            FixVec2 hero = ThicketLeadPoint(rain ? ThicketRainLeadTicks : ThicketSproutLeadTicks);
            int impact = Tick + (rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks);
            int stage = h.Stage;
            if (rain)
            {
                _thicketRainScratch ??= new FixVec2[ThicketRainCircles];
                ThicketRainTemplate(ref ThicketMemory[id].Rng, hero, _thicketRainScratch);
                // Внешние круги — на пол (у стены 13–39% ложились мимо): поворот вокруг центра шаблона.
                for (int k = 1; k < ThicketRainCircles; k++)
                    _thicketRainScratch[k] = ThicketOntoFloor(hero, _thicketRainScratch[k]);
                for (int k = 0; k < ThicketRainCircles; k++)
                    OpenThicketShape(id, stage * ThicketRainCircles + k, ThicketRainCircle(_thicketRainScratch[k]), impact);
            }
            else OpenThicketShape(id, stage, ThicketSproutCircle(hero), impact);
            h.Stage = stage + 1;
            h.NextStageTick = Tick + (rain ? ThicketRainEveryTicks : ThicketSproutEveryTicks);
            h.Target = hero;
            if (h.ImpactTick < Tick || impact < h.ImpactTick) h.ImpactTick = impact;
            if (stage > 0)
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    rain ? EnemyActionKind.ThicketRain : EnemyActionKind.ThicketSprout, hero, stage));
        }

        /// <summary>
        /// Точка упреждения каста: герой + его ход за тик × leadTicks, не дальше ThicketLeadMax,
        /// на полу (тело героя там помещается; иначе ближайшее такое место). Стоящему — он сам.
        /// </summary>
        private FixVec2 ThicketLeadPoint(int leadTicks)
        {
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 lead = (Entities.Velocity[PlayerId] * Fix64.FromInt(leadTicks)).ClampLength(ThicketLeadMax);
            if (lead.LengthSq.Raw == 0) return hero;
            FixVec2 point = hero + lead;
            Fix64 body = Entities.BodyRadius[PlayerId];
            if (_layout == null || _layout.IsWalkable(point, body)) return point;
            return _layout.ClampToWalkable(point, body);
        }

        /// <summary>
        /// Круг каста point вокруг center — на пол: на месте, если центр на полу; иначе тот же
        /// радиус, поворот на ±20°, ±40°, ±60°; иначе ближайшее место на полу. Поток не тратит.
        /// </summary>
        private FixVec2 ThicketOntoFloor(FixVec2 center, FixVec2 point)
        {
            if (_layout == null || _layout.IsWalkable(point, ThicketFloorProbe)) return point;
            FixVec2 off = point - center;
            if (off.LengthSq.Raw != 0)
            {
                Fix64 angle = off.Angle, radius = off.Length;
                for (int k = 1; k <= 3; k++)
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        FixVec2 q = center + FixVec2.FromAngle(angle + ThicketFloorTurn * (k * sign)) * radius;
                        if (_layout.IsWalkable(q, ThicketFloorProbe)) return q;
                    }
            }
            return _layout.ClampToWalkable(point, ThicketFloorProbe);
        }

        /// <summary>Шаг поворота круга каста на пол — 20°.</summary>
        private static readonly Fix64 ThicketFloorTurn = Fix64.Pi / 9;

        private int OpenThicketShape(int id, int index, in EnemyTelegraph shape, int impact)
        {
            int k = id * ThicketShapeSlots + index;
            int slot = OpenTelegraph(id, shape, impact, impact + TelegraphLingerTicks, TelegraphFlags.SharedView);
            int serial = TryGetTelegraph(slot, out var t) ? t.Serial : 0;
            ThicketShapeSerial[k] = serial;
            ThicketShapeImpact[k] = impact;
            ThicketShapeCenter[k] = shape.Origin;
            ThicketShapeState[k] = ThicketShapePending;
            return serial;
        }

        /// <summary>
        /// Тик фоновой опасности (до действия босса): встаёт следующая метка,
        /// срабатывают созревшие — каждый круг прорастания бьёт сам, залп ливня —
        /// не больше одного раза на все 4 круга. Всё отбито — опасность снята
        /// (круги остаются «сработавшими» для вида до следующего каста или бури).
        /// </summary>
        private void AdvanceThicketHazard(int id)
        {
            if (_thicketHazards == null) return;
            ref var h = ref _thicketHazards[id];
            if (h.Serial == 0) return;
            if (h.Stage < h.Stages && Tick >= h.NextStageTick) PlaceThicketHazardStage(id, ref h);
            ResolveThicketShapesDue(id, h.Action == ThicketMasterAction.Rain);
            if (h.Serial == 0 || !Entities.Alive[id]) return;
            int next = NextThicketHazardImpact(id, h);
            if (next == int.MaxValue) h = default;
            else h.ImpactTick = next;
        }

        /// <summary>Ближайший ещё не случившийся удар опасности (встал или встанет); MaxValue — всё отбито.</summary>
        private int NextThicketHazardImpact(int id, in ThicketHazardState h)
        {
            int best = int.MaxValue;
            int from = id * ThicketShapeSlots;
            for (int k = 0; k < ThicketShapeSlots; k++)
                if (ThicketShapeState[from + k] == ThicketShapePending && ThicketShapeImpact[from + k] < best)
                    best = ThicketShapeImpact[from + k];
            if (best == int.MaxValue && h.Stage < h.Stages)
                best = h.NextStageTick + (h.Action == ThicketMasterAction.Rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks);
            return best;
        }

        private void ResolveThicketShapesDue(int id, bool rain)
        {
            int from = id * ThicketShapeSlots;
            int groups = rain ? ThicketRainVolleys : ThicketSproutCircles;
            int size = rain ? ThicketRainCircles : 1;
            for (int g = 0; g < groups; g++)
            {
                int first = from + g * size;
                if (ThicketShapeState[first] != ThicketShapePending || ThicketShapeImpact[first] > Tick) continue;
                // Состояние всех кругов группы — до урона.
                bool hit = false;
                FixVec2 at = ThicketShapeCenter[first];
                FixVec2 hero = Entities.Position[PlayerId];
                Fix64 body = Entities.BodyRadius[PlayerId];
                for (int k = first; k < first + size; k++)
                {
                    ThicketShapeState[k] = ThicketShapeDone;
                    bool live = ResolveTelegraphSerial(ThicketShapeSerial[k]) || ThicketShapeSerial[k] == 0;
                    var circle = rain ? ThicketRainCircle(ThicketShapeCenter[k]) : ThicketSproutCircle(ThicketShapeCenter[k]);
                    if (!hit && live && Entities.Alive[PlayerId] && TelegraphContains(in circle, hero, body))
                    { hit = true; at = ThicketShapeCenter[k]; }
                }
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                    rain ? EnemyActionKind.ThicketRain : EnemyActionKind.ThicketSprout, at, g, hit));
                if (!hit) continue;
                ApplyAbilityDamage(id, PlayerId, rain ? ThicketRainDamageOf(id) : ThicketSproutDamageOf(id), -1, DamageType.Physical);
                // Отражение могло убить босса: дальше круги снимет его смерть.
                if (!Entities.Alive[id]) return;
            }
        }

        /// <summary>Удары опасности, которые ещё впереди (встали или встанут), — для такта и для серии лапы.</summary>
        private int ThicketHazardImpacts(int id, int[] into)
        {
            if (_thicketHazards == null || _thicketHazards[id].Serial == 0) return 0;
            var h = _thicketHazards[id];
            bool rain = h.Action == ThicketMasterAction.Rain;
            int size = rain ? ThicketRainCircles : 1;
            int from = id * ThicketShapeSlots, count = 0;
            for (int k = 0; k < h.Stage && count < into.Length; k++)
            {
                int slot = from + k * size;
                if (ThicketShapeState[slot] == ThicketShapePending && ThicketShapeImpact[slot] >= Tick)
                    into[count++] = ThicketShapeImpact[slot];
            }
            int every = rain ? ThicketRainEveryTicks : ThicketSproutEveryTicks;
            int delay = rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks;
            for (int k = h.Stage; k < h.Stages && count < into.Length; k++)
                into[count++] = h.NextStageTick + every * (k - h.Stage) + delay;
            return count;
        }

        private int[] _thicketImpactScratch;

        /// <summary>
        /// Ляжет ли удар серии лапы (strikes ударов от first через gap1, gap2) ближе
        /// ThicketOwnContactSpacingTicks к удару своей опасности.
        /// </summary>
        private bool ThicketHazardClash(int id, int first, int strikes, int gap1, int gap2)
        {
            _thicketImpactScratch ??= new int[ThicketShapeSlots + ThicketBushSlots];
            int n = ThicketHazardImpacts(id, _thicketImpactScratch);
            // И контакты шипов терновника (08.10): удар лапы не сливается с ними в одну вспышку.
            n = ThicketBushImpacts(id, _thicketImpactScratch, n);
            for (int s = 0; s < strikes; s++)
            {
                int strike = ThicketPawStrikeTick(first, s, gap1, gap2);
                for (int k = 0; k < n; k++)
                    if (Math.Abs(strike - _thicketImpactScratch[k]) < ThicketOwnContactSpacingTicks) return true;
            }
            return false;
        }

        /// <summary>Контакты опасности в такт ударов (вставшие и будущие).</summary>
        private void AddThicketHazardContacts(int id)
        {
            _thicketImpactScratch ??= new int[ThicketShapeSlots + ThicketBushSlots];
            int n = ThicketHazardImpacts(id, _thicketImpactScratch);
            for (int k = 0; k < n; k++) AddHeroContact(id, _thicketImpactScratch[k], _thicketImpactScratch[k]);
        }

        /// <summary>Держит ли опасность крупный жетон: круги ещё ударят или пыльца ещё падает.</summary>
        private bool ThicketHazardHoldsToken(int id)
        {
            if (_thicketHazards != null && _thicketHazards[id].Serial != 0 && Tick <= _thicketHazards[id].LastImpactTick) return true;
            return ThicketPollenOf(id, fallingOnly: true);
        }

        /// <summary>
        /// Вес опасности в бюджете: прорастание и ливень — 2 до последнего удара
        /// (начало — тик каста), падающая пыльца — 1 до падения.
        /// </summary>
        private int ThicketHazardMarkWeight(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            int weight = 0;
            if (_thicketHazards != null && _thicketHazards[id].Serial != 0)
            {
                var h = _thicketHazards[id];
                weight += ThicketCastMarkWeight;
                start = h.StartTick;
                impact = h.ImpactTick;
            }
            bool falling = false;
            if (_thicketPollen != null)
                for (int k = 0; k < _thicketPollen.Length; k++)
                {
                    var z = _thicketPollen[k];
                    if (z.Serial == 0 || z.Source != id || z.LandTick <= Tick) continue;
                    falling = true;
                    if (z.StartTick > start) start = z.StartTick;
                    if (impact == int.MinValue || z.LandTick < impact) impact = z.LandTick;
                }
            return falling ? weight + 1 : weight;
        }

        // ---------- бюджет, такт, Часы, сброс, хеш ----------

        /// <summary>
        /// Вес действия в бюджете крупных меток: нырок — 1 с начала ухода до
        /// выхода (круг ляжет через 12 + ход тиков, место бронируется сразу), буря — весь
        /// бюджет (не меньше 4) до второй волны (Simulation.ForestBoss.Storm.cs).
        /// Жест каста — 0: его метки считает фоновая опасность (ThicketHazardMarkWeight).
        /// </summary>
        partial void ThicketMarkWeightExtra(int id, ref int weight, ref int start, ref int impact)
        {
            var a = _thicketMasters[id];
            if (a.HitResolved) return;
            switch (a.Action)
            {
                case ThicketMasterAction.Dive:
                    weight = 1; start = a.StartTick; impact = a.ImpactTick;
                    return;
                case ThicketMasterAction.Storm:
                    weight = ThicketStormWeight; start = a.StartTick; impact = a.ImpactTick;
                    return;
            }
        }

        /// <summary>Контакты действия в такт ударов: выход из нырка, волны бури. Удары опасности — AddThicketHazardContacts.</summary>
        partial void ThicketContactsExtra(int id)
        {
            var a = _thicketMasters[id];
            switch (a.Action)
            {
                case ThicketMasterAction.Dive:
                    if (!a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                    return;
                case ThicketMasterAction.Storm:
                    AddThicketStormContacts(id, a);
                    return;
            }
        }

        /// <summary>Часы: ещё не сработавшие круги, фоновая опасность, пыльца и ждущая связка этого босса ждут вместе с ним.</summary>
        partial void ThicketHourglassExtra(int id, int ticks)
        {
            if (_thicketShapeState != null)
            {
                int from = id * ThicketShapeSlots;
                for (int k = from; k < from + ThicketShapeSlots; k++)
                    if (_thicketShapeState[k] == ThicketShapePending && _thicketShapeImpact[k] >= Tick) _thicketShapeImpact[k] += ticks;
            }
            if (_thicketHazards != null && _thicketHazards[id].Serial != 0)
            {
                ref var h = ref _thicketHazards[id];
                if (h.Stage < h.Stages && h.NextStageTick >= Tick) h.NextStageTick += ticks;
                if (h.ImpactTick >= Tick) h.ImpactTick += ticks;
                if (h.LastImpactTick >= Tick) h.LastImpactTick += ticks;
            }
            ref var m = ref ThicketMemory[id];
            if (m.ChainNext != ThicketMasterAction.None) m.ChainStep += ticks;
            DelayThicketPollen(id, ticks);
            ShiftThicketSeeds(id, ticks);
        }

        partial void ThicketResetExtra()
        {
            if (_thicketShapeState != null)
            {
                Array.Clear(_thicketShapeState, 0, _thicketShapeState.Length);
                Array.Clear(_thicketShapeSerial, 0, _thicketShapeSerial.Length);
                Array.Clear(_thicketShapeImpact, 0, _thicketShapeImpact.Length);
                Array.Clear(_thicketShapeCenter, 0, _thicketShapeCenter.Length);
            }
            if (_thicketHazards != null) Array.Clear(_thicketHazards, 0, _thicketHazards.Length);
            _thicketHazardSerial = 0;
            ResetThicketPollen();
            ResetThicketSeeds();
        }

        partial void ThicketHashExtra(ref ulong hash)
        {
            if (_thicketShapeState != null)
            {
                Hashing.Mix(ref hash, 0x54485348); // "THSH"
                for (int id = 1; id < Entities.Count; id++)
                {
                    if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                    int from = id * ThicketShapeSlots;
                    for (int k = from; k < from + ThicketShapeSlots; k++)
                    {
                        Hashing.Mix(ref hash, (int)_thicketShapeState[k]);
                        if (_thicketShapeState[k] == ThicketShapeEmpty) continue;
                        Hashing.Mix(ref hash, _thicketShapeSerial[k]); Hashing.Mix(ref hash, _thicketShapeImpact[k]);
                        Hashing.Mix(ref hash, _thicketShapeCenter[k].X); Hashing.Mix(ref hash, _thicketShapeCenter[k].Y);
                    }
                }
            }
            if (_thicketHazards != null)
            {
                Hashing.Mix(ref hash, 0x5448485A); // "THHZ"
                Hashing.Mix(ref hash, _thicketHazardSerial);
                for (int id = 1; id < Entities.Count; id++)
                {
                    if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                    var h = _thicketHazards[id];
                    Hashing.Mix(ref hash, h.Serial);
                    if (h.Serial == 0) continue;
                    Hashing.Mix(ref hash, (int)h.Action); Hashing.Mix(ref hash, h.StartTick);
                    Hashing.Mix(ref hash, h.Stage); Hashing.Mix(ref hash, h.Stages); Hashing.Mix(ref hash, h.NextStageTick);
                    Hashing.Mix(ref hash, h.ImpactTick); Hashing.Mix(ref hash, h.LastImpactTick);
                    Hashing.Mix(ref hash, h.Target.X); Hashing.Mix(ref hash, h.Target.Y);
                }
            }
            HashThicketPollen(ref hash);
            HashThicketSeeds(ref hash);
        }
    }
}
