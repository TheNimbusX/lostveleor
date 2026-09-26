using System;

namespace Game.Sim
{
    /// <summary>Действие Шипомёта. Значение идёт в хеш — новые только в конец.</summary>
    public enum ThornAction : byte { None, Line, Burst }

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
        /// линии. До него включительно линия держит крупный жетон; стойка
        /// (заморозка) идёт от него до EndTick.
        /// </summary>
        public int ImpactTick;

        /// <summary>Когда линия и всплеск снова готовы. Переживают действие.</summary>
        public int NextLineTick, NextBurstTick;

        /// <summary>Откуда и куда — фиксируются в тик начала.</summary>
        public FixVec2 Origin, Direction;

        /// <summary>
        /// Метки действия: номер первой и сколько открыто (у всплеска одна).
        /// Номера подряд (открыты одним вызовом); не открытых сегментов нет
        /// вовсе — ни рисунка, ни удара.
        /// </summary>
        public int FirstTelegraphSerial, Segments;

        /// <summary>Сколько сегментов уже сработало.</summary>
        public int Erupted;

        /// <summary>Уже попал: за одно действие — одно попадание.</summary>
        public bool HitResolved;
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
    /// замах 21, стойка 15, перезарядка 90. Без жетона.
    ///
    /// Оглушение, волок и смерть снимают действие: ещё не сработавшие метки
    /// гаснут, сработавшие доживают вспышку.
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

        public static readonly Fix64 ThornSegmentLength = Fix64.Ratio(7, 4);
        public static readonly Fix64 ThornSegmentWidth = Fix64.Ratio(7, 5);
        public static readonly Fix64 ThornLineStartOffset = Fix64.One;
        public static readonly Fix64 ThornLineMinDistance = Fix64.FromInt(3);
        public static readonly Fix64 ThornLineMaxDistance = Fix64.Ratio(17, 2);
        public static readonly Fix64 ThornBurstTriggerDistance = Fix64.Ratio(13, 5);
        public static readonly Fix64 ThornBurstRadius = Fix64.Ratio(12, 5);

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

        private readonly ThorncasterState[] _thorncasters;
        private int _thorncasterSerial;

        public bool TryGetThorncasterAction(int id, out ThorncasterState state)
        {
            state = (uint)id < (uint)_thorncasters.Length ? _thorncasters[id] : default;
            return state.Serial != 0;
        }

        /// <summary>Урон шипа — урон листа: глубина и «Сложно» приходят в него сами.</summary>
        public int ThornSpikeDamageOf(int id) => Entities.Damage[id];

        /// <summary>Всплеск — шип × 22/30, доля из таблицы видов.</summary>
        public int ThornBurstDamageOf(int id)
            => EnemyArchetypes.Share(Entities.Damage[id], EnemyArchetypes.ThorncasterBurstDamage,
                EnemyArchetypes.ThorncasterSpikeDamage);

        /// <summary>
        /// Сегмент index линии из origin вдоль direction — одна фигура и для
        /// метки, и для удара. Сегменты стыкуются: конец одного — начало следующего.
        /// </summary>
        public static EnemyTelegraph ThornLineSegment(FixVec2 origin, FixVec2 direction, int index)
            => EnemyTelegraph.Lane(origin + direction * (ThornLineStartOffset + ThornSegmentLength * index),
                direction, ThornSegmentLength, ThornSegmentWidth);

        /// <summary>Тик контакта index действия: шипы линии — 27 + 6k от начала, всплеск — 21.</summary>
        public static int ThornContactTick(in ThorncasterState state, int index)
            => state.Action == ThornAction.Burst
                ? state.StartTick + ThornBurstWindupTicks
                : state.StartTick + ThornLineWindupTicks + ThornLineSpikeStepTicks * index;

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
            // Обе атаки готовы сразу. Бесплатного выстрела из-под земли нет и
            // так: волна держит вставшего из земли через NextAttackTick, а у
            // линии 27 тиков видимого замаха до первого шипа.
            _thorncasters[id] = new ThorncasterState { NextLineTick = Tick, NextBurstTick = Tick };
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
            Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return true; }

            var facing = Entities.Facing[id]; var step = Entities.MoveStep[id];
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(Entities.Velocity[id], facing));
            // Подходит, пока дальше 6 м; начав идти, доходит до 5 м. Ближе
            // 5 м стоит и доворачивается: назад не шагает никогда.
            var distanceSq = toPlayer.LengthSq;
            bool advance = distanceSq > ThorncasterHoldMax * ThorncasterHoldMax
                || (speed.Raw > 0 && distanceSq > ThorncasterHoldMin * ThorncasterHoldMin);
            Fix64 wanted = Fix64.Zero;
            if (advance)
            {
                var share = (FixVec2.Dot(facing, toPlayer.Normalized()) - ThorncasterWalkAlignFrom)
                    / (Fix64.One - ThorncasterWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон — как у Вендиго: скаляром вдоль взгляда, без бокового остатка.
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            var from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, facing * speed);
            Entities.Velocity[id] = Entities.Position[id] - from;
            return true;
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
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                    || ForcedMotion.IsActive(Entities, id))
                {
                    CancelThorncaster(id);
                    continue;
                }
                var a = _thorncasters[id];
                if (a.Serial != 0 && Tick >= a.EndTick)
                {
                    // Действие кончилось; перезарядки остаются.
                    a = new ThorncasterState { NextLineTick = a.NextLineTick, NextBurstTick = a.NextBurstTick };
                    _thorncasters[id] = a;
                }
                if (a.Serial == 0 && !TryStartThorncaster(id)) continue;
                ResolveThornContacts(id);
            }
        }

        /// <summary>
        /// Начинает всплеск или линию, если пора. Всплеск — первым: прижатый
        /// вплотную отвечает кругом, линии там всё равно нет (она с 3 м).
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
            // Линия — крупная атака: без жетона идёт или стоит. И только лицом к
            // герою с точностью до одного шага разворота — линия идёт по взгляду.
            if (distance >= ThornLineMinDistance && distance <= ThornLineMaxDistance && Tick >= a.NextLineTick
                && FixVec2.Dot(Entities.Facing[id], direction) >= EnemyTurnStepCos && BigAttackTokenFree(id))
                return StartThornLine(id, direction);
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
            var previous = _thorncasters[id];
            int last = Tick + ThornLineWindupTicks + ThornLineSpikeStepTicks * (opened - 1);
            _thorncasters[id] = new ThorncasterState
            {
                Serial = ++_thorncasterSerial, Action = ThornAction.Line, StartTick = Tick,
                ImpactTick = last, EndTick = last + ThornLineRecoveryTicks,
                NextLineTick = Tick + ThornLineCooldownTicks, NextBurstTick = previous.NextBurstTick,
                Origin = origin, Direction = direction, FirstTelegraphSerial = first, Segments = opened,
            };
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
            var previous = _thorncasters[id];
            _thorncasters[id] = new ThorncasterState
            {
                Serial = ++_thorncasterSerial, Action = ThornAction.Burst, StartTick = Tick,
                ImpactTick = impact, EndTick = impact + ThornBurstRecoveryTicks,
                NextLineTick = previous.NextLineTick, NextBurstTick = Tick + ThornBurstCooldownTicks,
                Origin = origin, Direction = direction, FirstTelegraphSerial = _telegraphs[slot].Serial, Segments = 1,
            };
            Entities.Facing[id] = direction; Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThornBurst, origin));
            return true;
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
                    a.Action == ThornAction.Burst ? EnemyActionKind.ThornBurst : EnemyActionKind.ThornLine,
                    center, k, hit));
                if (!hit) continue;
                ApplyAbilityDamage(id, PlayerId,
                    a.Action == ThornAction.Burst ? ThornBurstDamageOf(id) : ThornSpikeDamageOf(id),
                    -1, DamageType.Physical);
                if (!Entities.Alive[id]) { CancelThorncaster(id); return; }
                if (!Entities.Alive[PlayerId]) return;
            }
        }

        /// <summary>Снимает действие и его ещё не сработавшие метки. Смерть метки снимает и сама (Kill).</summary>
        private void CancelThorncaster(int id)
        {
            var a = _thorncasters[id];
            if (a.Serial == 0) return;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                a.Action == ThornAction.Burst ? EnemyActionKind.ThornBurst : EnemyActionKind.ThornLine,
                Entities.Position[id]));
            // Перезарядки переживают снятое действие.
            _thorncasters[id] = new ThorncasterState { NextLineTick = a.NextLineTick, NextBurstTick = a.NextBurstTick };
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
                Hashing.Mix(ref hash, a.Origin.X.Raw); Hashing.Mix(ref hash, a.Origin.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.FirstTelegraphSerial); Hashing.Mix(ref hash, a.Segments);
                Hashing.Mix(ref hash, a.Erupted); Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
            }
        }
    }
}
