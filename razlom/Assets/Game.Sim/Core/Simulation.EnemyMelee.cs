using System;

namespace Game.Sim
{
    /// <summary>
    /// Профиль ближнего замаха вида: окна, фигура удара, правило старта и
    /// дистанция, на которой моб перестаёт подходить. Один на вид — см.
    /// Simulation.MeleeProfileOf. Числа Хранителя и Корнеполза — ровно их
    /// прежние константы; Расщепень и его детёныш держат свои в
    /// Simulation.Splitter.
    /// </summary>
    public readonly struct EnemyMeleeProfile
    {
        public readonly int WindupTicks, RecoveryTicks;

        /// <summary>Фигура удара: сектор радиуса Radius с половиной раствора ArcCos.</summary>
        public readonly Fix64 Radius, ArcCos;

        /// <summary>Замах начинается, когда герой ближе StartRange и в секторе StartCos от взгляда.</summary>
        public readonly Fix64 StartRange, StartCos;

        /// <summary>На этой дистанции до героя моб перестаёт идти (MoveEnemies).</summary>
        public readonly Fix64 ChaseRange;

        public EnemyMeleeProfile(int windupTicks, int recoveryTicks, Fix64 radius, Fix64 arcCos,
            Fix64 startRange, Fix64 startCos, Fix64 chaseRange)
        {
            WindupTicks = windupTicks; RecoveryTicks = recoveryTicks; Radius = radius; ArcCos = arcCos;
            StartRange = startRange; StartCos = startCos; ChaseRange = chaseRange;
        }
    }

    /// <summary>
    /// Замах ближнего моба: Лесного хранителя, Корнеполза, Расщепеня и его
    /// детёныша.
    ///
    /// Всё, что виду нужно для фазы анимации, лежит здесь, а не в паре
    /// PendingAttackTarget/AttackImpactTick: та пара знает только «когда
    /// контакт», но не «куда» и не «до какого тика моб стоит».
    /// </summary>
    public readonly struct EnemySwingState
    {
        public readonly int Serial, StartTick, ImpactTick, RecoverUntil, Target;

        /// <summary>Слот метки в общем списке или -1: у Корнеполза метки нет.</summary>
        public readonly int Telegraph, TelegraphSerial;

        /// <summary>Откуда и куда бьёт. Зафиксированы в тик начала замаха.</summary>
        public readonly FixVec2 Origin, Direction;
        public readonly bool HitResolved;

        public EnemySwingState(int serial, int startTick, int impactTick, int recoverUntil, int target,
            FixVec2 origin, FixVec2 direction, int telegraph, int telegraphSerial, bool hitResolved = false)
        {
            Serial = serial; StartTick = startTick; ImpactTick = impactTick; RecoverUntil = recoverUntil;
            Target = target; Origin = origin; Direction = direction; Telegraph = telegraph;
            TelegraphSerial = telegraphSerial; HitResolved = hitResolved;
        }

        internal EnemySwingState Resolve() => new EnemySwingState(Serial, StartTick, ImpactTick, RecoverUntil,
            Target, Origin, Direction, Telegraph, TelegraphSerial, true);
    }

    public sealed partial class Simulation
    {
        // ---- Лесной хранитель ----
        //
        // Урон 11–25 по правилу окна из бестиария: 18–21 тик замаха и фигура
        // на земле. Замах 21 тик — EnemyAttackWindupTicks; направление
        // фиксируется в первый тик, и моб не доворачивается и не идёт до
        // конца восстановления. 15 тиков стоит после удара — окно, в которое
        // его наказывают; ещё 12 свободен. Итого цикл 48.
        public const int GuardianSwingRecoveryTicks = 15;
        public const int GuardianSwingCycleTicks = 48;
        //
        // Радиус 2,4 → 2,2 (стенд баланса, 26.09): попадание считается до края
        // тела героя, и сектор 2,4 м доставал на 2,85 м, когда герой бьёт с
        // 1,875 м, — из-под удара приходилось выходить на целый метр.
        public static readonly Fix64 GuardianSwingRadius = Fix64.Ratio(11, 5);
        public static readonly Fix64 GuardianSwingArcCos = Fix64.Ratio(1, 2);

        // Замах начинается, только когда корпус уже смотрит на героя: ±37°.
        // Прежние ±120° давали замах боком, и сектор на земле указывал бы мимо.
        private static readonly Fix64 GuardianSwingCommitCos = Fix64.Ratio(4, 5);

        // ---- Корнеполз ----
        //
        // До 10 урона — только поза, без метки: шесть-десять укусов разом
        // засорили бы землю так, что метку Хранителя стало бы не прочесть.
        // В момент контакта тело само бросается вперёд на полметра.
        public const int RootSwarmRecoveryTicks = 8;
        public const int RootSwarmLungeTicks = 4;
        public static readonly Fix64 RootSwarmLungeDistance = Fix64.Ratio(1, 2);
        public static readonly Fix64 RootSwarmBiteArcCos = Fix64.Ratio(70711, 100000);

        /// <summary>
        /// Сколько тиков моб не начинает нового замаха после оглушения,
        /// волока или сбитого замаха. Считается от последнего тика помехи:
        /// очнулся — ещё 0,4 с на то, чтобы по нему ударить.
        /// </summary>
        public const int EnemySwingInterruptPauseTicks = 12;

        // ---- жетоны атак ----
        //
        // Док: «разносить крупные телеграфы по времени». Жетон — право на
        // атаку прямо сейчас; кто не получил, кружит или держит дистанцию.
        // Раздаются по возрастанию индекса: младший, решивший первым, первым
        // и получает, и порядок одинаков на всех машинах.

        /// <summary>
        /// Ближних замахов (Хранитель, Расщепень) одновременно. Укусы Корнеполза
        /// и детёныша Расщепеня не считаются — у них свой жетон.
        /// </summary>
        public const int MeleeAttackTokenLimit = 2;

        /// <summary>
        /// Укусов Корнеполза (и детёныша Расщепеня) в замахе одновременно —
        /// свой жетон, отдельный от ближнего. Метки у укуса нет, и восемь поз разом со всех сторон не
        /// прочесть и не обойти: стенд баланса насчитал рою 30–70% всего урона
        /// по герою. Кто не получил жетон, стоит на дистанции укуса и ждёт.
        /// </summary>
        public const int SwarmBiteTokenLimit = 3;

        private int _bigAttackTokenLimit = 1;

        /// <summary>
        /// Крупных атак одновременно: таран, прыжок Вендиго (потом вой), залп
        /// Плюй-плода. Задаёт забег по номеру арены — см. BigAttackTokensForArena;
        /// сама расстановка его не сбрасывает. Меньше единицы не бывает.
        /// </summary>
        public int BigAttackTokenLimit
        {
            get => _bigAttackTokenLimit;
            set => _bigAttackTokenLimit = value < 1 ? 1 : value;
        }

        /// <summary>Одна крупная атака на аренах 1–4, две с пятой.</summary>
        public static int BigAttackTokensForArena(int arena) => arena >= 5 ? 2 : 1;

        private readonly EnemySwingState[] _enemySwings;
        private int _enemySwingSerial;

        public bool TryGetEnemySwing(int entity, out EnemySwingState swing)
        {
            swing = (uint)entity < (uint)_enemySwings.Length ? _enemySwings[entity] : default;
            return swing.Serial != 0;
        }

        /// <summary>Фигура удара Хранителя. Одна на метку и на проверку попадания.</summary>
        public static EnemyTelegraph GuardianSector(FixVec2 origin, FixVec2 direction)
            => EnemyTelegraph.Sector(origin, direction, GuardianSwingRadius, GuardianSwingArcCos);

        // ---- кто бьёт общим замахом ----
        //
        // Раньше замахивался любой вид, кроме трёх перечисленных, и новый вид
        // молча становился Хранителем: чужой сектор, чужой жетон, чужой ход.
        // Теперь список явный. Вид не отсюда ходит и бьёт сам (Simulation.*),
        // а если своего хода у него нет — стоит и доворачивается (MoveEnemies).

        /// <summary>
        /// Бьёт ли вид общим ближним замахом. Моб без вида — стенды и тесты —
        /// живёт по правилам Хранителя.
        /// </summary>
        internal static bool UsesEnemySwing(EnemyKind kind)
            => kind == EnemyKind.None || kind == EnemyKind.ForestGuardian || kind == EnemyKind.ForestRootSwarm
                || kind == EnemyKind.ForestSplitter || kind == EnemyKind.ForestSplitling;

        /// <summary>
        /// Кусает, как Корнеполз: жетон укуса вместо ближнего, метки на земле
        /// нет, в миг контакта тело бросается вперёд, подход — рывком вплотную.
        /// </summary>
        internal static bool IsSwarmLike(EnemyKind kind)
            => kind == EnemyKind.ForestRootSwarm || kind == EnemyKind.ForestSplitling;

        /// <summary>
        /// Профиль замаха вида. Собирается при каждом вызове, а не хранится
        /// в статическом поле: числа Расщепеня лежат в другом файле класса, а
        /// порядок инициализации статических полей между файлами partial-класса
        /// не определён — кэш мог бы прочесть нули. Вид без общего замаха
        /// получает профиль Хранителя: он нужен только WindupTicksFor и
        /// AttackRangeFor, которые спрашивают о любом мобе.
        /// </summary>
        internal static EnemyMeleeProfile MeleeProfileOf(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestRootSwarm:
                    return new EnemyMeleeProfile(RootSwarmAttackWindupTicks, RootSwarmRecoveryTicks,
                        RootSwarmAttackRange, RootSwarmBiteArcCos, RootSwarmAttackRange, EnemyAttackArcCos,
                        RootSwarmAttackRange);
                case EnemyKind.ForestSplitter:
                    return new EnemyMeleeProfile(SplitterSwingWindupTicks, SplitterSwingRecoveryTicks,
                        SplitterSwingRadius, SplitterSwingArcCos, SplitterSwingRadius, SplitterSwingCommitCos,
                        SplitterChaseRange);
                case EnemyKind.ForestSplitling:
                    return new EnemyMeleeProfile(SplitlingBiteWindupTicks, SplitlingBiteRecoveryTicks,
                        SplitlingBiteRange, RootSwarmBiteArcCos, SplitlingBiteRange, EnemyAttackArcCos,
                        SplitlingBiteRange);
                default:
                    return new EnemyMeleeProfile(EnemyAttackWindupTicks, GuardianSwingRecoveryTicks,
                        GuardianSwingRadius, GuardianSwingArcCos, GuardianSwingRadius, GuardianSwingCommitCos,
                        AttackRange);
            }
        }

        /// <summary>Фигура удара моба id из точки origin. Одна на метку и на проверку попадания.</summary>
        public EnemyTelegraph EnemySwingShape(int id, FixVec2 origin, FixVec2 direction)
        {
            var profile = MeleeProfileOf(Entities.Kind[id]);
            return EnemyTelegraph.Sector(origin, direction, profile.Radius, profile.ArcCos);
        }

        private Fix64 EnemySwingStartRange(int id) => MeleeProfileOf(Entities.Kind[id]).StartRange;

        private Fix64 EnemySwingStartCos(int id) => MeleeProfileOf(Entities.Kind[id]).StartCos;

        /// <summary>Замах или восстановление: тело стоит и смотрит туда, куда бьёт.</summary>
        private bool EnemySwingHoldsBody(int id)
            => _enemySwings[id].Serial != 0 && Tick < _enemySwings[id].RecoverUntil;

        /// <summary>
        /// Стадия ближнего боя одного моба. Зовётся из ResolveAttacks по
        /// возрастанию индекса.
        ///
        /// ПОМЕХА ПРОВЕРЯЕТСЯ ДО КОНТАКТА. Раньше оглушённый моб просто
        /// пропускал тики, а его замах оставался висеть: в тик конца оглушения
        /// удар прилетал сразу, без всякого замаха на экране.
        /// </summary>
        private void UpdateEnemySwing(int id)
        {
            EnemyKind kind = Entities.Kind[id];
            if (!UsesEnemySwing(kind)) return;
            if (!Entities.Alive[id]) { CancelEnemySwing(id); return; }

            var swing = _enemySwings[id];
            // Свой бросок — выпад укуса, выброс детёныша — замаху не помеха.
            bool forced = ForcedMotion.IsInterrupting(Entities, id);
            // Способности сбивают замах и по-старому, стирая PendingAttackTarget
            // (Якорный удар, Крушение, оглушение талантом). Это тоже помеха, а
            // не забытый удар: замах без цели не имеет права долететь.
            bool knockedOff = swing.Serial != 0 && !swing.HitResolved && Entities.PendingAttackTarget[id] < 0;
            if (Statuses.IsStunned(id, Tick) || forced || knockedOff)
            {
                InterruptEnemySwing(id);
                return;
            }

            if (swing.Serial != 0)
            {
                if (!swing.HitResolved)
                {
                    if (Tick < swing.ImpactTick) return;
                    LandEnemySwing(id);
                    // Отражённый урон мог убить самого моба — тогда замах уже снят.
                    swing = _enemySwings[id];
                }
                if (swing.Serial == 0 || Tick < swing.RecoverUntil) return;
                _enemySwings[id] = default;
            }

            if (Entities.NextAttackTick[id] == int.MaxValue || Tick < Entities.NextAttackTick[id]) return;
            int target = FindNearestEnemy(id);
            if (target < 0) return;
            if (IsSwarmLike(kind)
                    ? CountSwarmBiteTokens(id) >= SwarmBiteTokenLimit
                    : CountMeleeAttackTokens(id) >= MeleeAttackTokenLimit) return;
            StartEnemySwing(id, target);
        }

        private void StartEnemySwing(int id, int target)
        {
            bool swarm = IsSwarmLike(Entities.Kind[id]);
            var profile = MeleeProfileOf(Entities.Kind[id]);
            FixVec2 origin = Entities.Position[id];
            // Бьёт туда, где герой стоял в начале замаха. Корпус встаёт в это
            // направление сразу: доворот до ±37° — цена того, что сектор на
            // земле смотрит на героя, а не мимо него.
            FixVec2 direction = (Entities.Position[target] - origin).Normalized();
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[id];
            int windup = WindupTicksFor(id);
            int recovery = profile.RecoveryTicks;
            int impact = Tick + windup;

            int slot = -1, telegraphSerial = 0;
            if (!swarm)
            {
                slot = OpenTelegraph(id, EnemyTelegraph.Sector(origin, direction, profile.Radius, profile.ArcCos),
                    impact, impact + TelegraphLingerTicks, TelegraphFlags.SharedView);
                if (slot >= 0) telegraphSerial = _telegraphs[slot].Serial;
            }

            _enemySwings[id] = new EnemySwingState(++_enemySwingSerial, Tick, impact, impact + recovery,
                target, origin, direction, slot, telegraphSerial);
            Entities.Facing[id] = direction;
            Entities.Velocity[id] = FixVec2.Zero;
            // Старая пара полей живёт дальше: по ней вид узнаёт, что над героем
            // занесён удар, а чужие способности — что замах есть и его можно сбить.
            Entities.PendingAttackTarget[id] = target;
            Entities.AttackImpactTick[id] = impact;
            Entities.PendingAttackVariant[id] = 0;
            // Скорость атаки со стата может только растянуть цикл: короче
            // замаха с восстановлением он не станет, иначе моб бил бы из позы.
            Entities.NextAttackTick[id] = Tick + Math.Max(Entities.AttackCooldown[id], windup + recovery);
            _events.Add(SimEvent.Attack(id, target, origin));
        }

        /// <summary>
        /// Контакт. Ровно один на замах: попал герой в фигуру — удар, нет —
        /// промах, второго шанса у этого замаха нет.
        /// </summary>
        private void LandEnemySwing(int id)
        {
            var swing = _enemySwings[id];
            bool swarm = IsSwarmLike(Entities.Kind[id]);
            Entities.PendingAttackTarget[id] = -1;
            Entities.AttackImpactTick[id] = 0;
            Entities.PendingAttackVariant[id] = 0;

            // У Хранителя проверяется ИМЕННО нарисованная метка. Заготовка
            // нужна только на случай переполненного пула, и она та же самая.
            // Укус меткой не рисуется и бьёт от того места, где тело сейчас.
            EnemyTelegraph shape;
            if (swarm) shape = EnemySwingShape(id, Entities.Position[id], swing.Direction);
            else if (!TryGetTelegraph(swing.Telegraph, out shape) || shape.Serial != swing.TelegraphSerial)
                shape = EnemySwingShape(id, swing.Origin, swing.Direction);
            if (!swarm) ResolveTelegraph(swing.Telegraph, swing.TelegraphSerial);

            // Состояние пишется ДО урона: отражение может убить моба внутри
            // ApplyAttack, и запись после него воскресила бы снятый замах.
            _enemySwings[id] = swing.Resolve();
            int target = swing.Target;
            if (swarm) BeginRootSwarmLunge(id, swing.Direction, target);

            if ((uint)target < (uint)Entities.Count && Entities.Alive[target]
                && Entities.Side[target] != Entities.Side[id]
                && TelegraphContains(in shape, Entities.Position[target], Entities.BodyRadius[target]))
                ApplyAttack(id, target, 0, Fix64.One);
        }

        /// <summary>
        /// Выпад Корнеполза. Упирается в стену и в тело героя: проскочить
        /// сквозь того, кого кусаешь, значит оказаться у него за спиной.
        /// </summary>
        private void BeginRootSwarmLunge(int id, FixVec2 direction, int target)
        {
            Fix64 distance = RootSwarmLungeDistance;
            if ((uint)target < (uint)Entities.Count && Entities.Alive[target])
            {
                FixVec2 relative = Entities.Position[target] - Entities.Position[id];
                Fix64 along = FixVec2.Dot(relative, direction);
                Fix64 lateral = Fix64.Abs(direction.X * relative.Y - direction.Y * relative.X);
                Fix64 contact = Entities.BodyRadius[id] + Entities.BodyRadius[target];
                if (along.Raw > 0 && lateral < contact)
                {
                    Fix64 touch = along - Fix64.Sqrt(contact * contact - lateral * lateral);
                    if (touch < distance) distance = touch;
                }
            }
            if (distance.Raw <= 0) return;
            ForcedMotion.Begin(Entities, id, Entities.Position[id] + direction * distance,
                RootSwarmLungeTicks, ForcedMotionKind.EnemyLunge);
        }

        /// <summary>Снимает замах и его метку. Без паузы: так снимает смерть.</summary>
        private void CancelEnemySwing(int id)
        {
            var swing = _enemySwings[id];
            if (swing.Serial == 0) return;
            _enemySwings[id] = default;
            if (!swing.HitResolved)
            {
                Entities.PendingAttackTarget[id] = -1;
                Entities.AttackImpactTick[id] = 0;
                Entities.PendingAttackVariant[id] = 0;
            }
            CancelTelegraphsOf(id);
        }

        /// <summary>
        /// Оглушение, волок, сбитый замах. Пауза продлевается каждый тик,
        /// пока помеха длится, поэтому отсчитывается от её конца.
        /// </summary>
        private void InterruptEnemySwing(int id)
        {
            CancelEnemySwing(id);
            int pause = Tick + EnemySwingInterruptPauseTicks;
            if (Entities.NextAttackTick[id] < pause) Entities.NextAttackTick[id] = pause;
        }

        /// <summary>
        /// Сколько ближних замахов (Хранитель, Расщепень) сейчас в замахе,
        /// кроме except. Замах есть только у видов общего замаха.
        /// </summary>
        private int CountMeleeAttackTokens(int except)
        {
            int held = 0;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (id == except || !Entities.Alive[id] || IsSwarmLike(Entities.Kind[id])) continue;
                var swing = _enemySwings[id];
                if (swing.Serial != 0 && !swing.HitResolved) held++;
            }
            return held;
        }

        /// <summary>
        /// Сколько укусов (Корнеполз, детёныш Расщепеня) сейчас в замахе,
        /// кроме except. Раздача — тем же порядком, что и ближний жетон:
        /// UpdateEnemySwing идёт по возрастанию индекса, и в один тик первым
        /// жетон берёт младший.
        /// </summary>
        private int CountSwarmBiteTokens(int except)
        {
            int held = 0;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (id == except || !Entities.Alive[id] || !IsSwarmLike(Entities.Kind[id])) continue;
                var swing = _enemySwings[id];
                if (swing.Serial != 0 && !swing.HitResolved) held++;
            }
            return held;
        }

        /// <summary>
        /// Свободен ли крупный жетон для self. Держит его тот, чья крупная
        /// атака ещё грозит: залп — пока идёт, прыжок — до приземления, вой —
        /// до удара кольца, таран — до остановки, линия шипов — до последнего
        /// шипа, удар корнями — до контакта. Кто и когда держит у новых мобов,
        /// решает их файл (*HoldsBigToken).
        /// </summary>
        private bool BigAttackTokenFree(int self)
        {
            int held = 0;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (id == self || !Entities.Alive[id]) continue;
                switch (Entities.Kind[id])
                {
                    case EnemyKind.ForestBud:
                        if (_forestBudAttacks[id].Serial != 0) held++;
                        break;
                    case EnemyKind.ForestWendigo:
                    {
                        // Прыжок держит жетон до приземления, вой — до удара кольца.
                        if (WendigoHoldsBigToken(id)) held++;
                        break;
                    }
                    case EnemyKind.ForestStonehoof:
                    {
                        var charge = _stonehoofActions[id];
                        if (charge.Serial != 0 && Tick <= charge.StopTick) held++;
                        break;
                    }
                    case EnemyKind.ForestThorncaster:
                        if (ThorncasterHoldsBigToken(id)) held++;
                        break;
                    case EnemyKind.ForestRootSnarer:
                        if (RootSnarerHoldsBigToken(id)) held++;
                        break;
                }
            }
            return held < _bigAttackTokenLimit;
        }

        /// <summary>
        /// Хранитель без жетона не встаёт в очередь, а кружит на дистанции
        /// удара — та же дуга, что при обходе соседа. Сторона — по чётности
        /// индекса: половина толпы идёт по кругу в одну сторону, половина в
        /// другую, и никакой случайности.
        /// </summary>
        private FixVec2 TokenWaitCircle(int id, FixVec2 toPlayer, Fix64 speed)
        {
            Fix64 distance = toPlayer.Length;
            if (distance.Raw == 0) return FixVec2.Zero;
            FixVec2 inward = toPlayer / distance;
            var side = new FixVec2(-inward.Y, inward.X);
            if ((id & 1) != 0) side = -side;
            // Дальше дистанции удара — подтягивается, ближе — отступает.
            Fix64 radial = Fix64.Clamp((distance - AttackRange) / (ApproachBrakeRange - AttackRange),
                -Fix64.One, Fix64.One);
            FixVec2 arc = side * ArcSideShare + inward * (ArcForwardShare * radial);
            return arc.Normalized() * (speed * CircleAroundScale);
        }

        private void ResetEnemySwings()
        {
            Array.Clear(_enemySwings, 0, _enemySwings.Length);
            _enemySwingSerial = 0;
        }

        private void HashEnemySwings(ref ulong hash)
        {
            Hashing.Mix(ref hash, _bigAttackTokenLimit);
            if (_enemySwingSerial == 0) return;
            Hashing.Mix(ref hash, 0x5357494E); Hashing.Mix(ref hash, _enemySwingSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                var s = _enemySwings[id];
                if (s.Serial == 0) continue;
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, s.Serial);
                Hashing.Mix(ref hash, s.StartTick); Hashing.Mix(ref hash, s.ImpactTick);
                Hashing.Mix(ref hash, s.RecoverUntil); Hashing.Mix(ref hash, s.Target);
                Hashing.Mix(ref hash, s.Telegraph); Hashing.Mix(ref hash, s.TelegraphSerial);
                Hashing.Mix(ref hash, s.Origin.X); Hashing.Mix(ref hash, s.Origin.Y);
                Hashing.Mix(ref hash, s.Direction.X); Hashing.Mix(ref hash, s.Direction.Y);
                Hashing.Mix(ref hash, s.HitResolved ? 1 : 0);
            }
        }
    }
}
