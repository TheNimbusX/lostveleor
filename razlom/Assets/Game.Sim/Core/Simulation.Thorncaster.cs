using System;

namespace Game.Sim
{
    /// <summary>Действие Шипомёта. Значение идёт в хеш — новые только в конец.</summary>
    public enum ThornAction : byte { None, Line, Burst, Shot }

    /// <summary>
    /// Состояние Шипомёта. Изменяемая структура, как у Камнекопыта: поля
    /// добавляет этот файл, и каждое новое обязано попасть в HashThorncasters.
    /// </summary>
    public struct ThorncasterState
    {
        /// <summary>Номер действия; 0 — действия нет.</summary>
        public int Serial;
        public ThornAction Action;
        public int StartTick, EndTick;

        /// <summary>
        /// Последний контакт действия: всплеск или последний ОТКРЫТЫЙ шип
        /// линии, у выстрела — выпуск шипа. До него включительно линия держит
        /// крупный жетон; стойка (заморозка) идёт от него до EndTick.
        /// </summary>
        public int ImpactTick;

        /// <summary>Когда линия, всплеск и выстрел снова готовы. Переживают действие.</summary>
        public int NextLineTick, NextBurstTick, NextShotTick;

        /// <summary>Откуда и куда — фиксируются в тик начала.</summary>
        public FixVec2 Origin, Direction;

        /// <summary>
        /// Метки действия: номер первой и сколько открыто (у всплеска одна).
        /// Номера подряд (открыты одним вызовом); не открытых сегментов нет
        /// вовсе — ни рисунка, ни удара. У выстрела метки нет (номер 0,
        /// Segments = 1): его угроза — сам летящий шип.
        /// </summary>
        public int FirstTelegraphSerial, Segments;

        /// <summary>Сколько сегментов уже сработало; у выстрела 1 — шип выпущен.</summary>
        public int Erupted;

        /// <summary>Уже попал: за одно действие — одно попадание.</summary>
        public bool HitResolved;

        /// <summary>
        /// Обход без действия: +1 — влево от линии к герою, −1 — вправо,
        /// 0 — линия чиста и обход не нужен (см. ThornDetourHeading).
        /// </summary>
        public int DetourSide;
    }

    /// <summary>
    /// Шип выстрела в полёте. Живёт отдельно от действия: выпущенный шип
    /// летит, даже если Шипомёта оглушили, отбросили или убили, — это
    /// снаряд, а не замах. Один на Шипомёта: выстрел не чаще раза в 60
    /// тиков, а полёт — не дольше 17 (плюс остановка Песочных Часов, в
    /// которой шип стоит). Метки на земле у него нет (решение владельца от
    /// 26.09): угроза — сам шип, и бьёт он только там, где пролетел.
    /// Каждое поле обязано попасть в HashThorncasters.
    /// </summary>
    public struct ThornShotState
    {
        /// <summary>Номер выстрела — Serial действия, выпустившего шип; 0 — шипа в воздухе нет.</summary>
        public int Serial;

        /// <summary>
        /// Тик выпуска: в него шип пролетает первые 0,6 м пути. Песочные
        /// Часы сдвигают его вперёд на время остановки (DelayThornShots) —
        /// расписание полёта идёт от него.
        /// </summary>
        public int ReleaseTick;

        /// <summary>Урон, снятый в тик выпуска: смерть стрелка его не меняет.</summary>
        public int Damage;

        /// <summary>
        /// Начало пути (0,8 м перед телом стрелка) и направление полёта —
        /// зафиксированы в начале замаха.
        /// </summary>
        public FixVec2 Origin, Direction;

        /// <summary>Длина пути: 10 м или до первого препятствия; дальше шип не летит.</summary>
        public Fix64 Length;

        /// <summary>Сколько остриё пролетело от начала пути к концу последнего тика.</summary>
        public Fix64 Travelled;
    }

    /// <summary>
    /// ШИПОМЁТ — элита леса (план новых мобов от 26.09).
    ///
    /// Ходит, как Вендиго: сначала доворот, потом шаг только вдоль взгляда.
    /// Подходит на 5–6 м и дальше не идёт, но и не пятится — не кайтящий
    /// стрелок: прижатый вплотную, он отвечает всплеском, а не бегством.
    ///
    /// Линия: до четырёх полос-сегментов вдоль взгляда, первый — в метре
    /// перед телом, шипы встают по очереди (27/33/39/45 тиков от начала) —
    /// линия заполняется наружу. Сегмент, который упёрся бы в камень, дерево
    /// или край поляны, не открывается, и за ним линии нет: не нарисован —
    /// не бьёт. Одно попадание за линию. После последнего шипа стоит 30 тиков
    /// — окно наказания. Крупный жетон держит до последнего шипа.
    ///
    /// Всплеск против объятий: герой ближе 2,6 м — круг 2,4 м вокруг себя,
    /// замах 21, стойка 15, перезарядка 90. Без жетона. Линия — ровно оттуда,
    /// где кончается всплеск: полосы, где герой стоит рядом и его нечем
    /// достать, нет.
    ///
    /// Линия к герою упирается в ствол или камень и до героя не достаёт —
    /// обходит препятствие, сближаясь под углом (не пятится), и стреляет
    /// уже с чистой земли. Иначе герой за деревом бил бы его издали вечно.
    ///
    /// Выстрел шипом (требование владельца от 26.09) — обычная дальняя атака,
    /// без жетона: герой в 3,5–10 м, лицом к нему, перезарядка 60 от начала.
    /// Всплеск (прижали) и готовая линия с жетоном идут раньше. В тик начала
    /// направление фиксируется. МЕТКИ НА ЗЕМЛЕ НЕТ (владелец, 26.09: «просто
    /// проджектайл, от которого можно увернуться»): предупреждение — замах
    /// (клип бросает шип на 21-м кадре) и сам летящий шип. Путь — от 0,8 м
    /// перед телом, 10 м или до первого препятствия; путь до героя не
    /// достаёт (камень между ними) — выстрела нет. Замах 21 тик, затем шип
    /// летит 0,6 м за тик (18 м/с) и бьёт героя, только если тело коснулось
    /// полосы, которую остриё прошло за этот тик (толщина шипа 2 × 0,25 м), —
    /// одно попадание; долетел до конца пути — падает. Стойка 12 тиков после выпуска.
    ///
    /// Оглушение, волок и смерть снимают действие: ещё не сработавшие метки
    /// гаснут, сработавшие доживают вспышку; невыпущенный шип не вылетает.
    /// Выпущенный они не отзывают: он летит до попадания или до конца пути.
    /// Останавливают его только Песочные Часы — как и плоды бутона: он стоит
    /// в воздухе, пока стоит время.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- линия шипов ----
        public const int ThornLineWindupTicks = 27;       // первый шип
        public const int ThornLineSpikeStepTicks = 6;     // следующий — через столько
        public const int ThornLineSegments = 4;
        public const int ThornLineLastImpactTicks = ThornLineWindupTicks + ThornLineSpikeStepTicks * (ThornLineSegments - 1);
        public const int ThornLineRecoveryTicks = 30;     // стоит после последнего шипа
        public const int ThornLineCooldownTicks = 150;    // от начала линии

        // ---- всплеск ----
        public const int ThornBurstWindupTicks = 21, ThornBurstRecoveryTicks = 15, ThornBurstCooldownTicks = 90;

        // ---- выстрел шипом ----
        public const int ThornShotWindupTicks = 21;       // выпуск шипа (кадр 21 клипа ForestThorncaster_Shot)
        public const int ThornShotRecoveryTicks = 12;     // стоит после выпуска
        public const int ThornShotCooldownTicks = 60;     // от начала выстрела

        public static readonly Fix64 ThornShotMinDistance = Fix64.Ratio(7, 2);
        public static readonly Fix64 ThornShotMaxDistance = Fix64.FromInt(10);
        /// <summary>Путь шипа начинается в 0,8 м перед телом — там он вылетает из руки.</summary>
        public static readonly Fix64 ThornShotStartOffset = Fix64.Ratio(4, 5);
        public static readonly Fix64 ThornShotMaxLength = Fix64.FromInt(10);
        /// <summary>Скорость шипа, м за тик: 0,6 × 30 = 18 м/с.</summary>
        public static readonly Fix64 ThornShotSpeed = Fix64.Ratio(3, 5);

        /// <summary>
        /// Полутолщина шипа для попадания, м. Полосы на земле нет, поэтому бьёт
        /// только то, что видно: тело героя обязано коснуться полосы шириной
        /// 2 × 0,25 м, которую остриё прошло за тик (ThornShotSweep). Шире
        /// нарисованного шипа (≈0,2 м у основания) на пару сантиметров пыли
        /// следа, а не на полметра, как была полоса 0,7 м, — шаг вбок спасает.
        /// </summary>
        public static readonly Fix64 ThornShotRadius = Fix64.Ratio(1, 4);

        /// <summary>Шаг пробы пути выстрела; край уточняется делением пополам (ThornShotFlightLength).</summary>
        private static readonly Fix64 ThornShotProbeStep = Fix64.Ratio(1, 4);

        public static readonly Fix64 ThornSegmentLength = Fix64.Ratio(7, 4);
        public static readonly Fix64 ThornSegmentWidth = Fix64.Ratio(7, 5);
        public static readonly Fix64 ThornLineStartOffset = Fix64.One;
        public static readonly Fix64 ThornLineMaxDistance = Fix64.Ratio(17, 2);
        public static readonly Fix64 ThornBurstTriggerDistance = Fix64.Ratio(13, 5);
        public static readonly Fix64 ThornBurstRadius = Fix64.Ratio(12, 5);

        /// <summary>
        /// Линия — с того же 2,6 м, где кончается всплеск. С 3 м (как в плане)
        /// между ними была мёртвая полоса: герой в 2,6–3 м стоял у Шипомёта
        /// под носом, и тот не делал ничего. Первый сегмент (1–2,75 м) достаёт
        /// героя и здесь. Объявлена после ThornBurstTriggerDistance: статические
        /// поля заполняются по порядку.
        /// </summary>
        public static readonly Fix64 ThornLineMinDistance = ThornBurstTriggerDistance;

        /// <summary>Держит героя на 5–6 м и не отступает: не кайтящий стрелок.</summary>
        public static readonly Fix64 ThorncasterHoldMin = Fix64.FromInt(5), ThorncasterHoldMax = Fix64.FromInt(6);
        public static readonly Fix64 ThorncasterMoveSpeed = Fix64.Ratio(11, 5);

        /// <summary>
        /// Походка Вендиго: шаг только вдоль взгляда, ноль при cos = 0,6
        /// (≈53°), полный при точном совпадении. Своё число — настраивается здесь.
        /// </summary>
        public static readonly Fix64 ThorncasterWalkAlignFrom = Fix64.Ratio(6, 10);

        /// <summary>
        /// Толщина пробы земли под линией. Как у когтя Вендиго (IsWalkable с
        /// 0,1 м): шип встаёт из земли, и точка под ним обязана быть полом,
        /// а не стволом, камнем или пустотой за краем поляны.
        /// </summary>
        private static readonly Fix64 ThornGroundProbe = Fix64.Ratio(1, 10);

        /// <summary>
        /// Шаг обхода: 3/5 к герою и 4/5 вбок (≈53°). Вперёд — всегда, так что
        /// обход сближает и не пятится; вбок — больше, чтобы выйти из-за ствола.
        /// Числа точные: вектор единичный без корня.
        /// </summary>
        private static readonly Fix64 ThornDetourForward = Fix64.Ratio(3, 5), ThornDetourSideways = Fix64.Ratio(4, 5);

        /// <summary>На сколько вбок пробует линию, выбирая сторону обхода.</summary>
        private static readonly Fix64 ThornDetourProbe = Fix64.Ratio(3, 2);

        private readonly ThorncasterState[] _thorncasters;
        private readonly ThornShotState[] _thornShots;
        private int _thorncasterSerial;

        public bool TryGetThorncasterAction(int id, out ThorncasterState state)
        {
            state = (uint)id < (uint)_thorncasters.Length ? _thorncasters[id] : default;
            return state.Serial != 0;
        }

        /// <summary>Шип выстрела Шипомёта id в воздухе. Есть и у мёртвого стрелка: шип долетает сам.</summary>
        public bool TryGetThornShot(int id, out ThornShotState shot)
        {
            shot = (uint)id < (uint)_thornShots.Length ? _thornShots[id] : default;
            return shot.Serial != 0;
        }

        /// <summary>Урон шипа — урон листа: глубина и «Сложно» приходят в него сами.</summary>
        public int ThornSpikeDamageOf(int id) => Entities.Damage[id];

        /// <summary>Всплеск — шип × 22/30, доля из таблицы видов.</summary>
        public int ThornBurstDamageOf(int id)
            => EnemyArchetypes.Share(Entities.Damage[id], EnemyArchetypes.ThorncasterBurstDamage,
                EnemyArchetypes.ThorncasterSpikeDamage);

        /// <summary>Выстрел — шип × 14/30: глубина и «Сложно» растят его вместе с шипом.</summary>
        public int ThornShotDamageOf(int id)
            => EnemyArchetypes.Share(Entities.Damage[id], EnemyArchetypes.ThorncasterShotDamage,
                EnemyArchetypes.ThorncasterSpikeDamage);

        /// <summary>
        /// Полоса, которую остриё шипа прошло от from до to метров пути, шириной
        /// в толщину шипа (2 × ThornShotRadius). Не метка: в общий список не
        /// попадает и нигде не рисуется — это фигура попадания одного тика
        /// полёта (FlyThornShot). Тело героя задето, если касается её краем
        /// (TelegraphContains): сбоку — не дальше толщины шипа, впереди острия —
        /// не дальше самого тела.
        /// </summary>
        public static EnemyTelegraph ThornShotSweep(in ThornShotState shot, Fix64 from, Fix64 to)
            => EnemyTelegraph.Lane(shot.Origin + shot.Direction * from, shot.Direction,
                Fix64.Max(Fix64.Zero, to - from), ThornShotRadius * 2);

        /// <summary>
        /// За сколько тиков шип пролетает путь длиной length: в тик выпуска
        /// и в каждый следующий — по 0,6 м, последний тик — до конца пути.
        /// </summary>
        public static int ThornShotFlightTicks(Fix64 length)
        {
            int ticks = (length / ThornShotSpeed).ToInt();
            if (ThornShotSpeed * ticks < length) ticks++;
            return Math.Max(1, ticks);
        }

        /// <summary>
        /// Сегмент index линии из origin вдоль direction — одна фигура и для
        /// метки, и для удара. Сегменты стыкуются: конец одного — начало следующего.
        /// </summary>
        public static EnemyTelegraph ThornLineSegment(FixVec2 origin, FixVec2 direction, int index)
            => EnemyTelegraph.Lane(origin + direction * (ThornLineStartOffset + ThornSegmentLength * index),
                direction, ThornSegmentLength, ThornSegmentWidth);

        /// <summary>
        /// Тик контакта index действия: шипы линии — 27 + 6k от начала,
        /// всплеск — 21, выстрел — выпуск шипа на 21 (попадание — позже, в полёте).
        /// </summary>
        public static int ThornContactTick(in ThorncasterState state, int index)
            => state.Action == ThornAction.Burst ? state.StartTick + ThornBurstWindupTicks
                : state.Action == ThornAction.Shot ? state.StartTick + ThornShotWindupTicks
                : state.StartTick + ThornLineWindupTicks + ThornLineSpikeStepTicks * index;

        /// <summary>Вид действия для событий EnemyAction*.</summary>
        private static EnemyActionKind ThornActionKind(ThornAction action)
            => action == ThornAction.Burst ? EnemyActionKind.ThornBurst
                : action == ThornAction.Shot ? EnemyActionKind.ThornShot : EnemyActionKind.ThornLine;

        /// <summary>Перезарядки переживают действие: его конец, снятие, новое действие.</summary>
        private static ThorncasterState ThornCooldowns(in ThorncasterState a)
            => new ThorncasterState { NextLineTick = a.NextLineTick, NextBurstTick = a.NextBurstTick, NextShotTick = a.NextShotTick };

        /// <summary>
        /// Держит ли крупный жетон: линия — до последнего шипа включительно.
        /// Всплеск жетона не берёт. Для BigAttackTokenFree.
        /// </summary>
        internal bool ThorncasterHoldsBigToken(int id)
        {
            var a = _thorncasters[id];
            return a.Serial != 0 && a.Action == ThornAction.Line && Tick <= a.ImpactTick;
        }

        private void ResetThorncasters()
        {
            Array.Clear(_thorncasters, 0, _thorncasters.Length);
            // Шипы в воздухе гаснут вместе с расстановкой.
            Array.Clear(_thornShots, 0, _thornShots.Length);
            _thorncasterSerial = 0;
        }

        private void ConfigureThorncaster(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestThorncaster);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.Ratio(1, 2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, ThorncasterMoveSpeed);
            // Урон листа — шип; всплеск считается от него (ThornBurstDamageOf).
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.One);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = Progression.EliteKillXp;
            // Все три атаки готовы сразу. Бесплатного выстрела из-под земли нет и
            // так: волна держит вставшего из земли через NextAttackTick, а у
            // линии и выстрела — видимый замах до первого контакта.
            _thorncasters[id] = new ThorncasterState { NextLineTick = Tick, NextBurstTick = Tick, NextShotTick = Tick };
        }

        /// <summary>
        /// Ход Шипомёта. Всегда true: разворот, агро и шаг делает сам.
        /// В действии стоит и смотрит туда, куда бьёт: доворот вслед за
        /// героем сделал бы нарисованную линию ложью.
        /// </summary>
        private bool MoveThorncaster(int id, FixVec2 toPlayer)
        {
            var a = _thorncasters[id];
            if (a.Serial != 0)
            {
                Entities.Facing[id] = a.Direction;
                Entities.Velocity[id] = FixVec2.Zero;
                return true;
            }
            // Линия к герою упирается в препятствие — идёт в обход, а не к герою.
            // До агро не обходит: он ещё не охотится, только следит взглядом.
            var goal = toPlayer;
            bool detour = Entities.Aggro[id] && ThornDetourHeading(id, toPlayer, out goal);
            Entities.Facing[id] = TurnToward(Entities.Facing[id], goal, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return true; }

            var facing = Entities.Facing[id]; var step = Entities.MoveStep[id];
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(Entities.Velocity[id], facing));
            // Подходит, пока дальше 6 м; начав идти, доходит до 5 м. Ближе
            // 5 м стоит и доворачивается: назад не шагает никогда. Обход идёт
            // и ближе 5 м — стоять за стволом значило бы не стрелять вовсе.
            var distanceSq = toPlayer.LengthSq;
            bool advance = detour || distanceSq > ThorncasterHoldMax * ThorncasterHoldMax
                || (speed.Raw > 0 && distanceSq > ThorncasterHoldMin * ThorncasterHoldMin);
            Fix64 wanted = Fix64.Zero;
            if (advance)
            {
                var share = (FixVec2.Dot(facing, goal.Normalized()) - ThorncasterWalkAlignFrom)
                    / (Fix64.One - ThorncasterWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон — как у Вендиго: скаляром вдоль взгляда, без бокового остатка.
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            var from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, facing * speed);
            Entities.Velocity[id] = Entities.Position[id] - from;
            // Упёрся в обходе (стена, второй ствол) — пробует другую сторону.
            if (detour && speed.Raw > 0 && Entities.Position[id].Equals(from))
                _thorncasters[id].DetourSide = -_thorncasters[id].DetourSide;
            return true;
        }

        /// <summary>
        /// Куда идти, если линия к герою упёрлась бы в ствол, камень или край
        /// поляны и до героя не дотянулась бы: к герою под ≈53° вбок
        /// (ThornDetourForward/Sideways). Сторону выбирает один раз — ту, где
        /// линия со шага вбок длиннее (поровну — левую), — и держит её, пока
        /// обход нужен: иначе дёргался бы влево-вправо. false и heading = toPlayer —
        /// линия чиста или герой вне её дальности, обход не нужен.
        ///
        /// Стоя, с готовой линией и хоть одним чистым сегментом, сперва
        /// стреляет: линия, обрезанная камнем, — честное чтение укрытия (план).
        /// Обходит, когда из-за камня не встаёт ни сегмента или пока линия
        /// перезаряжается — за укрытием герой отсиживается один выстрел, не всю арену.
        /// </summary>
        private bool ThornDetourHeading(int id, FixVec2 toPlayer, out FixVec2 heading)
        {
            heading = toPlayer;
            var origin = Entities.Position[id];
            var a = _thorncasters[id];
            if (!ThornLineFallsShort(origin, toPlayer, out int clear)
                || (a.DetourSide == 0 && clear > 0 && Tick >= a.NextLineTick))
            { _thorncasters[id].DetourSide = 0; return false; }
            var direction = toPlayer.Normalized();
            var left = new FixVec2(-direction.Y, direction.X);
            if (_thorncasters[id].DetourSide == 0)
            {
                var hero = Entities.Position[PlayerId];
                var toLeft = origin + left * ThornDetourProbe;
                var toRight = origin - left * ThornDetourProbe;
                _thorncasters[id].DetourSide = ThornLineClearSegments(toRight, (hero - toRight).Normalized())
                    > ThornLineClearSegments(toLeft, (hero - toLeft).Normalized()) ? -1 : 1;
            }
            var sideways = _thorncasters[id].DetourSide > 0 ? ThornDetourSideways : -ThornDetourSideways;
            heading = direction * ThornDetourForward + left * sideways;
            return true;
        }

        /// <summary>
        /// Не достаёт ли линия отсюда до героя из-за препятствия. Только в
        /// дальности линии: дальше — просто подход, ближе — всплеск. Все четыре
        /// сегмента чисты — не препятствие, а дальность. Без карты — поле, чисто.
        /// </summary>
        private bool ThornLineFallsShort(FixVec2 origin, FixVec2 toPlayer, out int clear)
        {
            clear = ThornLineSegments;
            if (_layout == null) return false;
            var distanceSq = toPlayer.LengthSq;
            if (distanceSq < ThornLineMinDistance * ThornLineMinDistance
                || distanceSq > ThornLineMaxDistance * ThornLineMaxDistance) return false;
            clear = ThornLineClearSegments(origin, toPlayer.Normalized());
            if (clear >= ThornLineSegments) return false;
            var reach = ThornLineStartOffset + ThornSegmentLength * clear + Entities.BodyRadius[PlayerId];
            return distanceSq > reach * reach;
        }

        /// <summary>
        /// Действия Шипомёта. Зовётся в Step после UpdateStonehooves, по
        /// возрастанию индекса: крупный жетон в один тик первым берёт младший.
        /// </summary>
        private void UpdateThorncasters()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThorncaster) continue;
                UpdateThorncasterAction(id);
                // Шип в воздухе летит и без стрелка: он снаряд, а не замах. После
                // действия — чтобы выпущенный в этот тик шип пролетел свои 0,6 м сразу.
                FlyThornShot(id);
            }
        }

        private void UpdateThorncasterAction(int id)
        {
            if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                || ForcedMotion.IsActive(Entities, id))
            {
                CancelThorncaster(id);
                return;
            }
            var a = _thorncasters[id];
            if (a.Serial != 0 && Tick >= a.EndTick)
            {
                // Действие кончилось; перезарядки остаются.
                a = ThornCooldowns(a);
                _thorncasters[id] = a;
            }
            if (a.Serial == 0 && !TryStartThorncaster(id)) return;
            if (_thorncasters[id].Action == ThornAction.Shot) ReleaseThornShot(id);
            else ResolveThornContacts(id);
        }

        /// <summary>
        /// Начинает всплеск, линию или выстрел, если пора. Всплеск — первым:
        /// прижатый вплотную отвечает кругом, линия — только с 2,6 м, где
        /// всплеск кончается. Выстрел — обычная атака, последним: когда линия
        /// на перезарядке, вне дальности или без жетона.
        /// </summary>
        private bool TryStartThorncaster(int id)
        {
            var a = _thorncasters[id];
            // Вставший из земли ждёт своего NextAttackTick — его ставит волна.
            if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id] || IsEmerging(id)) return false;
            var delta = Entities.Position[PlayerId] - Entities.Position[id];
            if (delta.LengthSq < Fix64.Ratio(1, 10000)) return false;
            var distance = delta.Length;
            var direction = delta.Normalized();
            if (distance <= ThornBurstTriggerDistance && Tick >= a.NextBurstTick)
                return StartThornBurst(id, direction);
            // Линия и выстрел идут по взгляду: только лицом к герою с точностью
            // до одного шага разворота. В обходе не стреляет ни тем, ни другим:
            // взгляд, разворачиваясь на другую сторону, может мазнуть по герою,
            // а путь отсюда всё ещё упирается в препятствие.
            bool aimed = a.DetourSide == 0 && FixVec2.Dot(Entities.Facing[id], direction) >= EnemyTurnStepCos;
            // Линия — крупная атака: без жетона идёт или стоит.
            if (aimed && distance >= ThornLineMinDistance && distance <= ThornLineMaxDistance && Tick >= a.NextLineTick
                && BigAttackTokenFree(id) && StartThornLine(id, direction))
                return true;
            // Выстрел — обычная атака, жетона не берёт. Шип прошлого выстрела к
            // этому времени давно упал (полёт ≤ 17 тиков, перезарядка 60), но
            // второй поверх летящего не встаёт никогда: шип у Шипомёта один.
            if (aimed && distance >= ThornShotMinDistance && distance <= ThornShotMaxDistance && Tick >= a.NextShotTick
                && _thornShots[id].Serial == 0)
                return StartThornShot(id, direction, distance);
            return false;
        }

        private bool StartThornLine(int id, FixVec2 direction)
        {
            var origin = Entities.Position[id];
            int clear = ThornLineClearSegments(origin, direction);
            if (clear == 0) return false;
            // Все сегменты — одним обходом: номера меток идут подряд. Пул полон —
            // сегмента нет (ни рисунка, ни удара), и дальше него линии тоже нет.
            int first = 0, opened = 0;
            for (int k = 0; k < clear; k++)
            {
                int impact = Tick + ThornLineWindupTicks + ThornLineSpikeStepTicks * k;
                int slot = OpenTelegraph(id, ThornLineSegment(origin, direction, k), impact,
                    impact + TelegraphLingerTicks, TelegraphFlags.SharedView);
                if (slot < 0) break;
                if (opened == 0) first = _telegraphs[slot].Serial;
                opened++;
            }
            if (opened == 0) return false;
            var a = ThornCooldowns(_thorncasters[id]);
            int last = Tick + ThornLineWindupTicks + ThornLineSpikeStepTicks * (opened - 1);
            a.Serial = ++_thorncasterSerial; a.Action = ThornAction.Line; a.StartTick = Tick;
            a.ImpactTick = last; a.EndTick = last + ThornLineRecoveryTicks;
            a.NextLineTick = Tick + ThornLineCooldownTicks;
            a.Origin = origin; a.Direction = direction; a.FirstTelegraphSerial = first; a.Segments = opened;
            _thorncasters[id] = a;
            Entities.Facing[id] = direction; Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThornLine, origin));
            return true;
        }

        private bool StartThornBurst(int id, FixVec2 direction)
        {
            var origin = Entities.Position[id];
            int impact = Tick + ThornBurstWindupTicks;
            int slot = OpenTelegraph(id, EnemyTelegraph.Circle(origin, ThornBurstRadius), impact,
                impact + TelegraphLingerTicks, TelegraphFlags.SharedView);
            // Без нарисованного круга всплеска нет: удар без метки — нечестный удар.
            if (slot < 0) return false;
            var a = ThornCooldowns(_thorncasters[id]);
            a.Serial = ++_thorncasterSerial; a.Action = ThornAction.Burst; a.StartTick = Tick;
            a.ImpactTick = impact; a.EndTick = impact + ThornBurstRecoveryTicks;
            a.NextBurstTick = Tick + ThornBurstCooldownTicks;
            a.Origin = origin; a.Direction = direction; a.FirstTelegraphSerial = _telegraphs[slot].Serial; a.Segments = 1;
            _thorncasters[id] = a;
            Entities.Facing[id] = direction; Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThornBurst, origin));
            return true;
        }

        /// <summary>
        /// Выстрел шипом: направление на героя фиксируется в этот тик. Метки на
        /// земле нет — ни общей, ни своей: игрок читает замах и сам шип. Путь
        /// шипа (ThornShotFlightLength) — 10 м или до первого препятствия — обязан
        /// дотянуться до тела героя: шип в камень перед героем — не угроза, а
        /// пустой звук, и тогда выстрела нет (герой за укрытием — повод для
        /// обхода, не для стрельбы в ствол).
        /// </summary>
        private bool StartThornShot(int id, FixVec2 direction, Fix64 distance)
        {
            var origin = Entities.Position[id];
            var length = ThornShotFlightLength(origin, direction);
            if (length.Raw <= 0 || ThornShotStartOffset + length + Entities.BodyRadius[PlayerId] < distance) return false;
            int release = Tick + ThornShotWindupTicks;
            var a = ThornCooldowns(_thorncasters[id]);
            a.Serial = ++_thorncasterSerial; a.Action = ThornAction.Shot; a.StartTick = Tick;
            a.ImpactTick = release; a.EndTick = release + ThornShotRecoveryTicks;
            a.NextShotTick = Tick + ThornShotCooldownTicks;
            a.Origin = origin; a.Direction = direction; a.FirstTelegraphSerial = 0; a.Segments = 1;
            _thorncasters[id] = a;
            Entities.Facing[id] = direction; Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThornShot, origin));
            return true;
        }

        /// <summary>
        /// Длина пути шипа: от его начала (0,8 м перед телом) вдоль direction
        /// до первой точки, где пола нет, — та же проба, что у линии
        /// (CanTravel с ThornGroundProbe), но не длиннее 10 м. Шаг пробы 1/4 м,
        /// затем три деления пополам: шип встаёт у камня с точностью до 3 см.
        /// Путь от тела до начала тоже обязан быть чист — иначе ноль. Без карты
        /// (голый стенд) — поле, все 10 м. Карта арены неподвижна, поэтому
        /// выпуск считает ту же длину, что и начало замаха, из того же Origin.
        /// </summary>
        public Fix64 ThornShotFlightLength(FixVec2 origin, FixVec2 direction)
        {
            if (_layout == null) return ThornShotMaxLength;
            var start = origin + direction * ThornShotStartOffset;
            if (!ThornGroundClear(origin, start, 4)) return Fix64.Zero;
            var clear = Fix64.Zero;
            while (clear < ThornShotMaxLength)
            {
                var step = Fix64.Min(ThornShotProbeStep, ThornShotMaxLength - clear);
                var from = start + direction * clear;
                if (_layout.CanTravel(from, start + direction * (clear + step), ThornGroundProbe))
                {
                    clear += step;
                    continue;
                }
                // Препятствие внутри шага: край — делением пополам.
                for (int k = 0; k < 3; k++)
                {
                    step = step / 2;
                    if (!_layout.CanTravel(from, start + direction * (clear + step), ThornGroundProbe)) continue;
                    clear += step;
                    from = start + direction * clear;
                }
                return clear;
            }
            return ThornShotMaxLength;
        }

        /// <summary>
        /// Выпуск шипа в тик ImpactTick (кадр 21 клипа). С этого тика шип —
        /// снаряд: оглушение, отброс и смерть стрелка его не отзывают. Шип
        /// встаёт в начало пути и в этот же тик пролетает первые 0,6 м
        /// (FlyThornShot зовётся следом). Событие EnemyProjectileLaunched —
        /// виду: шип, след и звук броска ставятся по нему, а не опросом.
        /// </summary>
        private void ReleaseThornShot(int id)
        {
            var a = _thorncasters[id];
            if (a.Serial == 0 || a.Erupted > 0 || Tick < a.ImpactTick) return;
            a.Erupted = 1;
            _thorncasters[id] = a;
            var length = ThornShotFlightLength(a.Origin, a.Direction);
            if (length.Raw <= 0) return;
            var start = a.Origin + a.Direction * ThornShotStartOffset;
            _thornShots[id] = new ThornShotState
            {
                Serial = a.Serial, ReleaseTick = Tick, Damage = ThornShotDamageOf(id),
                Origin = start, Direction = a.Direction, Length = length,
            };
            _events.Add(SimEvent.EnemyProjectile(id, PlayerId, a.Serial, EnemyActionKind.ThornShot, start));
        }

        /// <summary>
        /// Полёт шипа за тик: остриё проходит от пройденного к новому — 0,6 м,
        /// но не дальше конца пути. Герой задет, если его тело касается полосы,
        /// которую остриё прошло за ЭТОТ тик (ThornShotSweep): сбоку — не дальше
        /// толщины шипа, впереди острия — не дальше самого тела. Метки нет, и
        /// бьёт только то, что видно: за концом пути и мимо шипа удара нет.
        /// Одно попадание: шип застревает в герое. Не задел и долетел до конца —
        /// падает там. В обоих случаях шипа больше нет, а EnemyActionImpact
        /// несёт точку, где он встал.
        /// </summary>
        private void FlyThornShot(int id)
        {
            var shot = _thornShots[id];
            if (shot.Serial == 0) return;
            int flown = Tick - shot.ReleaseTick;
            var from = Fix64.Min(ThornShotSpeed * flown, shot.Length);
            // Песочные Часы сдвинули выпуск вперёд (DelayThornShots): шип стоит
            // там, где его застала остановка, и никого не задевает, пока
            // расписание не догонит пройденное.
            if (from < shot.Travelled) return;
            var to = Fix64.Min(ThornShotSpeed * (flown + 1), shot.Length);
            var stop = to;
            bool hit = false;
            if (Entities.Alive[PlayerId])
            {
                var hero = Entities.Position[PlayerId];
                var body = Entities.BodyRadius[PlayerId];
                var sweep = ThornShotSweep(in shot, from, to);
                if (TelegraphContains(in sweep, hero, body))
                {
                    hit = true;
                    // Шип встаёт у тела героя, а не в его центре.
                    stop = Fix64.Clamp(FixVec2.Dot(hero - shot.Origin, shot.Direction) - body, from, to);
                }
            }
            shot.Travelled = stop;
            if (!hit && to < shot.Length)
            {
                _thornShots[id] = shot;
                return;
            }
            // Шип остановился. Состояние пишется ДО урона: отражение может
            // убить Шипомёта внутри ApplyAbilityDamage.
            _thornShots[id] = default;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThornShot, shot.Origin + shot.Direction * stop, 0, hit));
            if (!hit) return;
            ApplyAbilityDamage(id, PlayerId, shot.Damage, -1, DamageType.Physical);
            if (!Entities.Alive[id]) CancelThorncaster(id);
        }

        /// <summary>
        /// Песочные Часы: враги и их снаряды стоят (плоды — DelayForestFruit).
        /// Шип в воздухе замирает на ticks тиков: выпуск сдвигается вперёд, и
        /// FlyThornShot его не двигает, пока расписание не догонит пройденное.
        /// Замахи сюда не попадают: Часы оглушают стрелков, и невыпущенные шипы
        /// снимаются вместе с действием.
        /// </summary>
        private void DelayThornShots(int ticks)
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (_thornShots[id].Serial == 0) continue;
                _thornShots[id].ReleaseTick += ticks;
            }
        }

        /// <summary>
        /// Сколько сегментов линии встанет на чистой земле: от тела вдоль
        /// взгляда до первой точки, где пола нет. Сегмент, который упёрся бы в
        /// препятствие, не открывается целиком, и за ним линия кончается.
        /// Без карты (голый стенд) — поле, все четыре.
        /// </summary>
        private int ThornLineClearSegments(FixVec2 origin, FixVec2 direction)
        {
            if (_layout == null) return ThornLineSegments;
            var start = origin + direction * ThornLineStartOffset;
            if (!ThornGroundClear(origin, start, 4)) return 0;
            for (int k = 0; k < ThornLineSegments; k++)
            {
                var end = start + direction * ThornSegmentLength;
                if (!ThornGroundClear(start, end, 8)) return k;
                start = end;
            }
            return ThornLineSegments;
        }

        /// <summary>
        /// Чиста ли осевая линия от from до to. CanTravel протягивает пробу
        /// между соседними точками, так что и тонкий ствол между ними не проскочит.
        /// </summary>
        private bool ThornGroundClear(FixVec2 from, FixVec2 to, int samples)
        {
            var previous = from;
            for (int s = 1; s <= samples; s++)
            {
                var next = from + (to - from) * Fix64.Ratio(s, samples);
                if (!_layout.CanTravel(previous, next, ThornGroundProbe)) return false;
                previous = next;
            }
            return true;
        }

        /// <summary>
        /// Контакты действия, срок которых пришёл. Каждый — по своей метке:
        /// проверяется ИМЕННО нарисованная фигура, и только если она ещё
        /// действовала (снятая или пропавшая метка не бьёт). Попадание одно
        /// на действие: герой, стоящий на стыке двух сегментов, получает шип
        /// один раз.
        /// </summary>
        private void ResolveThornContacts(int id)
        {
            var a = _thorncasters[id];
            while (a.Serial != 0 && a.Erupted < a.Segments && Tick >= ThornContactTick(a, a.Erupted))
            {
                int k = a.Erupted++;
                int serial = a.FirstTelegraphSerial + k;
                bool shaped = TryGetTelegraph(FindTelegraph(serial), out var shape) && shape.Serial == serial;
                bool erupted = shaped && ResolveTelegraphSerial(serial);
                bool hit = erupted && !a.HitResolved
                    && TelegraphContains(in shape, Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
                if (hit) a.HitResolved = true;
                // Состояние пишется ДО урона: отражение может убить Шипомёта
                // внутри ApplyAbilityDamage, и запись после него воскресила бы
                // снятое действие.
                _thorncasters[id] = a;
                if (!erupted) continue;
                var center = a.Action == ThornAction.Burst ? shape.Origin
                    : shape.Origin + shape.Direction * (shape.Length / 2);
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                    ThornActionKind(a.Action), center, k, hit));
                if (!hit) continue;
                ApplyAbilityDamage(id, PlayerId,
                    a.Action == ThornAction.Burst ? ThornBurstDamageOf(id) : ThornSpikeDamageOf(id),
                    -1, DamageType.Physical);
                if (!Entities.Alive[id]) { CancelThorncaster(id); return; }
                if (!Entities.Alive[PlayerId]) return;
            }
        }

        /// <summary>
        /// Снимает действие и его ещё не сработавшие метки. Смерть метки снимает
        /// и сама (Kill). Выпущенный шип не трогает: он снаряд, меток у него
        /// нет, и летит он до попадания или до конца пути (FlyThornShot).
        /// </summary>
        private void CancelThorncaster(int id)
        {
            var a = _thorncasters[id];
            if (a.Serial == 0) return;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                ThornActionKind(a.Action), Entities.Position[id]));
            // Перезарядки переживают снятое действие.
            _thorncasters[id] = ThornCooldowns(a);
            Entities.Velocity[id] = FixVec2.Zero;
            CancelTelegraphsOf(id);
        }

        private void HashThorncasters(ref ulong hash)
        {
            bool present = _thorncasterSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++) present = Entities.Kind[id] == EnemyKind.ForestThorncaster;
            if (!present) return;
            Hashing.Mix(ref hash, 0x54484F52); Hashing.Mix(ref hash, _thorncasterSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThorncaster) continue;
                var a = _thorncasters[id]; Hashing.Mix(ref hash, id);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Action);
                Hashing.Mix(ref hash, a.StartTick); Hashing.Mix(ref hash, a.ImpactTick); Hashing.Mix(ref hash, a.EndTick);
                Hashing.Mix(ref hash, a.NextLineTick); Hashing.Mix(ref hash, a.NextBurstTick);
                Hashing.Mix(ref hash, a.NextShotTick);
                Hashing.Mix(ref hash, a.Origin.X.Raw); Hashing.Mix(ref hash, a.Origin.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.FirstTelegraphSerial); Hashing.Mix(ref hash, a.Segments);
                Hashing.Mix(ref hash, a.Erupted); Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
                Hashing.Mix(ref hash, a.DetourSide);
                // Шип в воздухе — и у мёртвого стрелка: мёртвый Шипомёт остаётся в пуле своего вида.
                var s = _thornShots[id];
                Hashing.Mix(ref hash, s.Serial);
                if (s.Serial == 0) continue;
                Hashing.Mix(ref hash, s.ReleaseTick); Hashing.Mix(ref hash, s.Damage);
                Hashing.Mix(ref hash, s.Origin.X.Raw); Hashing.Mix(ref hash, s.Origin.Y.Raw);
                Hashing.Mix(ref hash, s.Direction.X.Raw); Hashing.Mix(ref hash, s.Direction.Y.Raw);
                Hashing.Mix(ref hash, s.Length.Raw); Hashing.Mix(ref hash, s.Travelled.Raw);
            }
        }
    }
}
