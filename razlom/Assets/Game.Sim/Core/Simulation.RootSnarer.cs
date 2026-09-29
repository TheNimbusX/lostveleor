using System;

namespace Game.Sim
{
    /// <summary>Действие Корнехвата. Значение идёт в хеш — новые только в конец.</summary>
    public enum RootSnarerAction : byte
    {
        None = 0,

        /// <summary>Удар корнями по месту героя.</summary>
        Slam = 1,

        /// <summary>«Волна из корней» — лечение союзников.</summary>
        Mend = 2,
    }

    /// <summary>
    /// Состояние Корнехвата. Изменяемая структура: поля добавляет этот файл,
    /// и каждое новое обязано попасть в HashRootSnarers.
    /// </summary>
    public struct RootSnarerState
    {
        /// <summary>Номер действия; 0 — действия нет.</summary>
        public int Serial;

        /// <summary>Какое действие идёт; None — действия нет.</summary>
        public RootSnarerAction Action;

        /// <summary>
        /// Удар: начало позы, удар корнями (круг на земле), контакт, конец стойки.
        /// Лечение: лапы в землю, волна, конец стойки; SlamTick = ImpactTick —
        /// тик волны, чтобы кадр контакта позы вставал на волну.
        /// </summary>
        public int StartTick, SlamTick, ImpactTick, EndTick;

        /// <summary>Когда удар снова готов. Переживает действие — и лечение тоже.</summary>
        public int NextActionTick;

        /// <summary>
        /// Удар: центр круга — где стоял герой в тик удара корнями.
        /// Лечение: центр волны — где моб встал.
        /// </summary>
        public FixVec2 Target;

        /// <summary>
        /// Куда смотрит моб. Удар: до удара корнями — взгляд в начале позы (дальше
        /// он доворачивается к герою), с удара и до конца стойки — на круг.
        /// Лечение: взгляд в тик «лапы в землю», без доворота до конца.
        /// </summary>
        public FixVec2 Direction;

        /// <summary>Номер метки круга; 0 — ещё не открыт (или пул был полон). У лечения метки нет.</summary>
        public int TelegraphSerial;

        /// <summary>
        /// Удар корнями случился. Круг при этом может и не встать — пул меток
        /// полон, — тогда TelegraphSerial остаётся нулём, и удара нет вовсе.
        /// У лечения всегда false.
        /// </summary>
        public bool Slammed;

        /// <summary>Контакт разрешён: попал или мимо, второго не будет. У лечения — волна прошла.</summary>
        public bool HitResolved;

        /// <summary>Лечение: здоровье моба в тик «лапы в землю» — от него меряется сбивающий урон.</summary>
        public int StartHealth;

        /// <summary>Лечение: скольких союзников вылечила волна.</summary>
        public int Healed;
    }

    /// <summary>
    /// КОРНЕХВАТ (план новых мобов от 26.09).
    ///
    /// Удар: герой в 1,5–7 м (между центрами), перезарядка готова и крупный
    /// жетон свободен — моб встаёт. 15 тиков только поза (событие
    /// EnemyActionStarted — им кормится звук EnemyWarning), метки на земле
    /// нет. На 15-м бьёт корнями: круг 1,5 м встаёт на месте героя в этот тик
    /// и больше не двигается. Через 21 тик контакт: герой в круге — урон и
    /// корни на 30 тиков (решение владельца 29.09, общий контроль героя
    /// ApplyHeroRoot: не ходит и не кувыркается, но бьёт и кастует; в
    /// иммунитете к контролю — только урон), мимо — ничего.
    /// Потом 36 тиков стоит открытым — окно наказания. Крупный жетон — от
    /// начала позы до контакта. Оглушение, волок и смерть снимают действие,
    /// и ещё не сработавший круг гаснет вместе с ним.
    ///
    /// «ВОЛНА ИЗ КОРНЕЙ» (решение владельца от 27.09: саппорт, лечение
    /// союзников). Главнее удара: готовы оба — лечит. Повод — раненый союзник:
    /// живой моб той же стороны, кроме самого Корнехвата и других Корнехватов,
    /// не дальше 5 м + радиус его тела (между центрами), один на ≤ 75%
    /// здоровья или двое на ≤ 90%. Союзник, которого любой Корнехват лечил
    /// последние 240 тиков, не повод и не лечится. Первое лечение — не раньше
    /// 90 тиков после того, как моб заметил героя, дальше раз в 300 тиков от
    /// начала. После конца любого действия 30 тиков тишины — и для лечения,
    /// и для удара. Лечит разом один Корнехват на арене. Крупного жетона и
    /// бюджета меток лечение не берёт: метки на земле у него нет, круг волны
    /// дружеский.
    ///
    /// Самый раненый дальше 4 м — сначала идёт к нему той же походкой до
    /// 2,5 м, не дольше 60 тиков, и лечит, где встал. Повод пропал — поход
    /// брошен, моб живёт как обычно: удар не ждёт лечения вечно.
    ///
    /// Ход: лапы в землю (EnemyActionStarted SnarerMend), 30 тиков стоит и
    /// собирает силу. Оглушение, волок, смерть или урон от 15% здоровья с
    /// начала сбивают (EnemyActionCancelled SnarerMend), следующее лечение —
    /// через 150 тиков. На 30-м волна: каждый союзник в круге, кроме уже
    /// леченных за 240 тиков, получает 10% своего здоровья (элита, Вендиго,
    /// Шипомёт и босс — 5%), не выше недостающего; полному — ничего. Событие
    /// Heal на каждого и затем EnemyActionImpact SnarerMend (Amount — скольких,
    /// Flag — кого-то вылечила, Position — центр волны). Потом 20 тиков стоит.
    ///
    /// Ходит как Вендиго — сначала разворот, потом шаг вдоль взгляда — до
    /// RootSnarerHoldDistance от героя. Не отступает: прижатый вплотную
    /// (ближе 1,5 м) бить не может, и это честный ответ игрока — дойти и
    /// срубить. Общим замахом Хранителя не бьёт никогда.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int RootSnarerSlamTicks = 15;          // поза до удара корнями
        public const int RootSnarerImpactDelayTicks = 21;   // от удара корнями до контакта
        public const int RootSnarerRecoveryTicks = 36;      // стоит после контакта
        public const int RootSnarerCooldownTicks = 150;     // от начала позы до следующей

        /// <summary>Замедление при попадании, если корни выключены: минус 40% на 45 шагов героя.</summary>
        public const int RootSnarerSlowPercent = 40, RootSnarerSlowTicks = 45;

        /// <summary>
        /// Переключатель владельца: true — вместо замедления корни на
        /// RootSnarerRootTicks (ApplyHeroRoot, с иммунитетом к контролю).
        /// Решение 29.09 — корни.
        /// </summary>
        public const bool SnarerRoots = true;

        /// <summary>Корни при попадании: 1 с — 30 шагов героя.</summary>
        public const int RootSnarerRootTicks = 30;

        public static readonly Fix64 RootSnarerCircleRadius = Fix64.Ratio(3, 2);
        public static readonly Fix64 RootSnarerMinDistance = Fix64.Ratio(3, 2);
        public static readonly Fix64 RootSnarerMaxDistance = Fix64.FromInt(7);
        public static readonly Fix64 RootSnarerMoveSpeed = Fix64.Ratio(12, 5);

        /// <summary>
        /// Подходит к герою до 5 м и там стоит: 2 м запаса до края дальности,
        /// чтобы шаг героя назад не выводил его из-под удара, и достаточно
        /// далеко, чтобы Хранители и рой успевали встать между ними.
        /// </summary>
        public static readonly Fix64 RootSnarerHoldDistance = Fix64.FromInt(5);

        /// <summary>
        /// Походка как у Вендиго: шаг только вдоль взгляда, скорость — доля
        /// совпадения взгляда с направлением на героя, ноль при cos 0,6 (≈53°).
        /// </summary>
        public static readonly Fix64 RootSnarerWalkAlignFrom = Fix64.Ratio(6, 10);

        // ---- «Волна из корней» ----

        /// <summary>Лапы в землю до волны: всё это время лечение можно сбить.</summary>
        public const int RootSnarerMendChannelTicks = 30;

        /// <summary>Стоит после волны.</summary>
        public const int RootSnarerMendRecoveryTicks = 20;

        /// <summary>От начала лечения до следующего.</summary>
        public const int RootSnarerMendCooldownTicks = 300;

        /// <summary>Сбитое лечение: следующее — через столько тиков от сбоя.</summary>
        public const int RootSnarerMendCancelCooldownTicks = 150;

        /// <summary>Первое лечение — не раньше, чем через столько тиков после того, как моб заметил героя.</summary>
        public const int RootSnarerMendFirstDelayTicks = 90;

        /// <summary>Тишина после конца любого действия (удара или лечения): ни того, ни другого.</summary>
        public const int RootSnarerMendGapTicks = 30;

        /// <summary>Одного союзника любой Корнехват лечит не чаще раза в столько тиков.</summary>
        public const int RootSnarerMendAllyCooldownTicks = 240;

        /// <summary>Волна даёт 10% здоровья союзника, элите и боссу — 5%; не выше недостающего.</summary>
        public const int RootSnarerMendPercent = 10, RootSnarerMendElitePercent = 5;

        /// <summary>Повод лечить: один союзник на ≤ 75% здоровья или двое на ≤ 90%.</summary>
        public const int RootSnarerMendBadlyHurtPercent = 75, RootSnarerMendHurtPercent = 90;

        /// <summary>Урон с начала лечения от 15% здоровья моба сбивает его.</summary>
        public const int RootSnarerMendBreakPercent = 15;

        /// <summary>К раненому союзнику идёт не дольше стольких тиков — потом лечит, где встал.</summary>
        public const int RootSnarerMendWalkTicks = 60;

        /// <summary>Круг волны: 5 м от центра моба плюс радиус тела союзника.</summary>
        public static readonly Fix64 RootSnarerMendRadius = Fix64.FromInt(5);

        /// <summary>Самый раненый дальше 4 м — сначала подходит к нему до 2,5 м (между центрами).</summary>
        public static readonly Fix64 RootSnarerMendWalkDistance = Fix64.FromInt(4);
        public static readonly Fix64 RootSnarerMendHoldDistance = Fix64.Ratio(5, 2);

        private readonly RootSnarerState[] _rootSnarers;
        private int _rootSnarerSerial;

        /// <summary>
        /// Память лечения по индексу сущности. У Корнехвата — готовность
        /// лечения, тишина после действия и поход к союзнику; у любого
        /// союзника — с какого тика его снова можно лечить.
        /// </summary>
        private struct RootSnarerMendMemory
        {
            /// <summary>Когда лечение готово; 0 — моб ещё не замечал героя, отсчёт первого не начат.</summary>
            public int NextMendTick;

            /// <summary>До этого тика ни удара, ни лечения: конец прошлого действия + RootSnarerMendGapTicks.</summary>
            public int QuietUntilTick;

            /// <summary>Идёт к ApproachAlly до этого тика; 0 — не идёт. Срок вышел — лечит, где стоит.</summary>
            public int ApproachUntilTick, ApproachAlly;

            /// <summary>Союзника снова можно лечить с этого тика; 0 — его ещё не лечили.</summary>
            public int HealFreeTick;
        }

        // Массив заводится при первой расстановке, а не в конструкторе:
        // конструктор живёт в Simulation.cs, а лечение — здесь. Расстановка —
        // не бой, аллокация там допустима (тот же приём, что у воя Вендиго).
        private RootSnarerMendMemory[] _rootSnarerMend;

        private RootSnarerMendMemory[] RootSnarerMend => _rootSnarerMend ??= new RootSnarerMendMemory[Entities.Capacity];

        public bool TryGetRootSnarerAction(int id, out RootSnarerState state)
        {
            state = (uint)id < (uint)_rootSnarers.Length ? _rootSnarers[id] : default;
            return state.Serial != 0;
        }

        /// <summary>
        /// Тик, в который волну любого Корнехвата получил союзник allyId; −1 —
        /// не получал. Для листьев на вылеченных; главный сигнал — события Heal.
        /// </summary>
        public int LastMendTick(int allyId)
        {
            if (_rootSnarerMend == null || (uint)allyId >= (uint)_rootSnarerMend.Length) return -1;
            int free = _rootSnarerMend[allyId].HealFreeTick;
            return free == 0 ? -1 : free - RootSnarerMendAllyCooldownTicks;
        }

        /// <summary>Когда у Корнехвата id будет готово лечение; 0 — он ещё не замечал героя.</summary>
        public int RootSnarerNextMendTick(int id)
            => _rootSnarerMend != null && (uint)id < (uint)_rootSnarerMend.Length ? _rootSnarerMend[id].NextMendTick : 0;

        /// <summary>Урон удара корнями — урон листа: глубина и «Сложно» приходят в него сами.</summary>
        public int RootSnarerDamageOf(int id) => Entities.Damage[id];

        /// <summary>Круг удара корнями с центром в center — одна фигура и для метки, и для удара.</summary>
        public static EnemyTelegraph RootSnarerCircle(FixVec2 center)
            => EnemyTelegraph.Circle(center, RootSnarerCircleRadius);

        /// <summary>
        /// Держит ли крупный жетон: только удар, от начала позы до контакта
        /// включительно. Лечение жетона не берёт. Для BigAttackTokenFree.
        /// </summary>
        internal bool RootSnarerHoldsBigToken(int id)
        {
            var a = _rootSnarers[id];
            return a.Serial != 0 && a.Action == RootSnarerAction.Slam && Tick <= a.ImpactTick;
        }

        private void ResetRootSnarers()
        {
            Array.Clear(_rootSnarers, 0, _rootSnarers.Length);
            Array.Clear(RootSnarerMend, 0, RootSnarerMend.Length);
            _rootSnarerSerial = 0;
        }

        /// <summary>
        /// Первый удар готов сразу: у Вендиго первый прыжок ждёт, потому что
        /// прыжок бьёт издалека без подхода, а Корнехват всё равно сначала
        /// идёт на дистанцию. Волна, встающая из земли, держит его своим
        /// NextAttackTick. Первое лечение отсчитывается позже — от агро
        /// (UpdateRootSnarers).
        /// </summary>
        private void ConfigureRootSnarer(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestRootSnarer);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.One;
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, RootSnarerMoveSpeed);
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.One);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            _rootSnarers[id] = default;
            RootSnarerMend[id] = default;
        }

        /// <summary>
        /// Ход Корнехвата — всегда целиком (true): разворот, агро, шаг.
        ///
        /// В действии стоит. Удар: до удара корнями доворачивается к герою —
        /// поза целится, — с удара смотрит на круг до конца стойки. Лечение:
        /// взгляд начала, без доворота. Без действия идёт к герою до
        /// RootSnarerHoldDistance, а в походе к раненому союзнику — к нему до
        /// RootSnarerMendHoldDistance.
        /// </summary>
        private bool MoveRootSnarer(int id, FixVec2 toPlayer)
        {
            var a = _rootSnarers[id];
            if (a.Serial != 0)
            {
                Entities.Velocity[id] = FixVec2.Zero;
                Entities.Facing[id] = a.Slammed || a.Action == RootSnarerAction.Mend ? a.Direction
                    : TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
                return true;
            }
            // Поход к раненому союзнику ведёт вместо героя; агро — всё равно по герою.
            FixVec2 goal = toPlayer; Fix64 hold = RootSnarerHoldDistance;
            var memory = RootSnarerMend[id];
            bool healingApproach = Tick < memory.ApproachUntilTick && Entities.Alive[memory.ApproachAlly];
            if (healingApproach)
            {
                goal = Entities.Position[memory.ApproachAlly] - Entities.Position[id];
                hold = RootSnarerMendHoldDistance;
            }
            // ИИ v2: куда смотреть и идти (heading) — отдельно от того, далеко ли
            // цель (goal): к герою — по полю пути, если упёрся; стоя на дистанции —
            // расходится по кругу с другим крупным стрелком (у героя остаётся
            // сторона без меток); из чужой метки или лужи — вон.
            FixVec2 heading = goal;
            bool shift = false;
            if (Entities.Aggro[id])
            {
                if (goal.LengthSq > hold * hold) heading = SteerToward(id, Entities.Position[id] + goal);
                else if (!healingApproach && BigAttackerSpread(id, out var side)) { heading = side; shift = true; }
            }
            if (Entities.Aggro[id] && InAllyDanger(id, Entities.Position[id], out var escape)) { heading = escape; shift = true; }
            Entities.Facing[id] = TurnToward(Entities.Facing[id], heading, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return true; }
            // Сначала разворот, потом шаг — и только вдоль взгляда (см. RootSnarerWalkAlignFrom).
            var facing = Entities.Facing[id]; var step = Entities.MoveStep[id];
            Fix64 wanted = Fix64.Zero;
            if (shift || goal.LengthSq > hold * hold)
            {
                var share = (FixVec2.Dot(facing, heading.Normalized()) - RootSnarerWalkAlignFrom)
                    / (Fix64.One - RootSnarerWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон тот же, что у всех (AccelerationTicks), но скаляром вдоль
            // взгляда: боковой остаток прошлой скорости не доживает ни тика.
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(Entities.Velocity[id], facing));
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            var from = Entities.Position[id];
            Entities.Position[id] = EnemyStep(id, from, facing * speed);
            Entities.Velocity[id] = Entities.Position[id] - from;
            return true;
        }

        /// <summary>
        /// Действия Корнехвата. Зовётся в Step после UpdateThorncasters —
        /// после движения героя: круг встаёт там, где герой стоит в этот тик.
        /// Обход по возрастанию индекса: крупный жетон и право лечить берёт
        /// младший.
        /// </summary>
        private void UpdateRootSnarers()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestRootSnarer) continue;
                // Отсчёт первого лечения — с тика, когда моб впервые заметил героя.
                ref var memory = ref RootSnarerMend[id];
                if (memory.NextMendTick == 0 && Entities.Aggro[id])
                    memory.NextMendTick = Tick + RootSnarerMendFirstDelayTicks;
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                    || ForcedMotion.IsActive(Entities, id))
                { CancelRootSnarer(id); continue; }
                var a = _rootSnarers[id];
                // Стойка кончилась — моб снова ходит; удар ждёт своей перезарядки,
                // и оба действия — тишины после этого.
                if (a.Serial != 0 && Tick >= a.EndTick)
                {
                    memory.QuietUntilTick = a.EndTick + RootSnarerMendGapTicks;
                    a = new RootSnarerState { NextActionTick = a.NextActionTick };
                    _rootSnarers[id] = a;
                }
                if (a.Serial == 0) { TryStartRootSnarer(id); continue; }
                if (a.Action == RootSnarerAction.Mend) { UpdateRootSnarerMend(id); continue; }
                if (!a.Slammed && Tick >= a.SlamTick) SlamRootSnarer(id);
                if (!_rootSnarers[id].HitResolved && Tick >= _rootSnarers[id].ImpactTick) ResolveRootSnarerImpact(id);
            }
        }

        private void TryStartRootSnarer(int id)
        {
            var a = _rootSnarers[id];
            ref var memory = ref RootSnarerMend[id];
            if (!Entities.Aggro[id] || IsEmerging(id) || Tick < Entities.NextAttackTick[id])
            { memory.ApproachUntilTick = 0; return; }
            if (Tick < memory.QuietUntilTick) return;
            // Лечение главнее удара: пока лечит или идёт к раненому — не бьёт.
            if (TryStartRootSnarerMend(id)) return;
            if (Tick < a.NextActionTick) return;
            var distanceSq = (Entities.Position[PlayerId] - Entities.Position[id]).LengthSq;
            if (distanceSq < RootSnarerMinDistance * RootSnarerMinDistance
                || distanceSq > RootSnarerMaxDistance * RootSnarerMaxDistance) return;
            // Удар корнями — крупная атака: без жетона моб стоит на дистанции и ждёт.
            if (!BigAttackTokenFree(id, 1, Tick + RootSnarerSlamTicks + RootSnarerImpactDelayTicks)) return;
            int impact = Tick + RootSnarerSlamTicks + RootSnarerImpactDelayTicks;
            _rootSnarers[id] = new RootSnarerState
            {
                Serial = ++_rootSnarerSerial, Action = RootSnarerAction.Slam,
                StartTick = Tick, SlamTick = Tick + RootSnarerSlamTicks,
                ImpactTick = impact, EndTick = impact + RootSnarerRecoveryTicks,
                // Перезарядка — от начала позы: снятый удар её не обнуляет.
                NextActionTick = Tick + RootSnarerCooldownTicks,
                Direction = Entities.Facing[id],
            };
            Entities.Velocity[id] = FixVec2.Zero;
            // Поза — только тело и звук: метки на земле ещё нет.
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.SnarerSlam, Entities.Position[id]));
        }

        /// <summary>
        /// Лечение или поход к раненому союзнику. true — моб занят лечением
        /// (начал или идёт), и удар в этот тик не начинается.
        /// </summary>
        private bool TryStartRootSnarerMend(int id)
        {
            ref var memory = ref RootSnarerMend[id];
            int ally = memory.NextMendTick == 0 || Tick < memory.NextMendTick || RootSnarerMendTaken(id)
                ? -1 : FindRootSnarerMendAlly(id);
            if (ally < 0) { memory.ApproachUntilTick = 0; return false; }
            // Не идёт — порог 4 м; уже идёт — до 2,5 м или до конца срока.
            bool walking = memory.ApproachUntilTick != 0;
            Fix64 near = walking ? RootSnarerMendHoldDistance : RootSnarerMendWalkDistance;
            if ((Entities.Position[ally] - Entities.Position[id]).LengthSq > near * near
                && (!walking || Tick < memory.ApproachUntilTick))
            {
                if (!walking) memory.ApproachUntilTick = Tick + RootSnarerMendWalkTicks;
                memory.ApproachAlly = ally;
                return true;
            }
            StartRootSnarerMend(id);
            return true;
        }

        /// <summary>Лечит ли сейчас другой Корнехват: разом лечит один на арене.</summary>
        private bool RootSnarerMendTaken(int self)
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (id == self || Entities.Kind[id] != EnemyKind.ForestRootSnarer || !Entities.Alive[id]) continue;
                var a = _rootSnarers[id];
                // Конец стойки у старшего индекса снимается в его ход — тик конца уже свободен.
                if (a.Serial != 0 && a.Action == RootSnarerAction.Mend && Tick < a.EndTick) return true;
            }
            return false;
        }

        /// <summary>
        /// Достаёт ли волна Корнехвата snarer до союзника ally: живой моб той
        /// же стороны, не Корнехват, не леченный за RootSnarerMendAllyCooldownTicks,
        /// центр не дальше RootSnarerMendRadius + радиус его тела.
        /// </summary>
        private bool RootSnarerMendReaches(int snarer, int ally)
        {
            if (ally == snarer || ally == PlayerId || !Entities.Alive[ally]
                || Entities.Side[ally] != Entities.Side[snarer]
                || Entities.Kind[ally] == EnemyKind.ForestRootSnarer
                || Tick < RootSnarerMend[ally].HealFreeTick) return false;
            Fix64 reach = RootSnarerMendRadius + Entities.BodyRadius[ally];
            return (Entities.Position[ally] - Entities.Position[snarer]).LengthSq <= reach * reach;
        }

        /// <summary>
        /// Самый раненый (меньшая доля здоровья, при равенстве — младший)
        /// союзник на ≤ 90%, если лечить есть повод: он сам на ≤ 75% или таких
        /// на ≤ 90% хотя бы двое. −1 — повода нет.
        /// </summary>
        private int FindRootSnarerMendAlly(int id)
        {
            int best = -1, hurt = 0;
            bool badly = false;
            for (int ally = 1; ally < Entities.Count; ally++)
            {
                if (!RootSnarerMendReaches(id, ally)) continue;
                long health = Entities.Health[ally], max = Entities.MaxHealth[ally];
                if (max <= 0 || health * 100 > max * RootSnarerMendHurtPercent) continue;
                hurt++;
                if (health * 100 <= max * RootSnarerMendBadlyHurtPercent) badly = true;
                if (best < 0 || health * Entities.MaxHealth[best] < Entities.Health[best] * max) best = ally;
            }
            return badly || hurt >= 2 ? best : -1;
        }

        /// <summary>Лапы в землю: моб встаёт, взгляд остаётся тем, что был.</summary>
        private void StartRootSnarerMend(int id)
        {
            ref var memory = ref RootSnarerMend[id];
            memory.ApproachUntilTick = 0;
            // Перезарядка — от начала: сбитое лечение ставит свою (CancelRootSnarer).
            memory.NextMendTick = Tick + RootSnarerMendCooldownTicks;
            int wave = Tick + RootSnarerMendChannelTicks;
            _rootSnarers[id] = new RootSnarerState
            {
                Serial = ++_rootSnarerSerial, Action = RootSnarerAction.Mend,
                StartTick = Tick, SlamTick = wave, ImpactTick = wave,
                EndTick = wave + RootSnarerMendRecoveryTicks,
                // Перезарядка удара переживает лечение.
                NextActionTick = _rootSnarers[id].NextActionTick,
                Target = Entities.Position[id], Direction = Entities.Facing[id],
                StartHealth = Entities.Health[id],
            };
            Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.SnarerMend, Entities.Position[id]));
        }

        /// <summary>
        /// Сбор силы и волна. Урон от RootSnarerMendBreakPercent здоровья с
        /// начала сбивает лечение до волны; после волны моб просто стоит.
        /// </summary>
        private void UpdateRootSnarerMend(int id)
        {
            var a = _rootSnarers[id];
            if (a.HitResolved) return;
            if ((long)(a.StartHealth - Entities.Health[id]) * 100
                >= (long)Entities.MaxHealth[id] * RootSnarerMendBreakPercent)
            { CancelRootSnarer(id); return; }
            if (Tick >= a.ImpactTick) ResolveRootSnarerMend(id);
        }

        /// <summary>
        /// Волна: каждый союзник в круге (RootSnarerMendReaches) с недостающим
        /// здоровьем получает долю своего здоровья, не выше недостающего.
        /// Событие Heal на каждого, затем одно EnemyActionImpact на волну.
        /// </summary>
        private void ResolveRootSnarerMend(int id)
        {
            int healed = 0;
            for (int ally = 1; ally < Entities.Count; ally++)
            {
                if (!RootSnarerMendReaches(id, ally)) continue;
                int missing = Entities.MaxHealth[ally] - Entities.Health[ally];
                if (missing <= 0) continue;
                int percent = RootSnarerMendHalved(ally) ? RootSnarerMendElitePercent : RootSnarerMendPercent;
                int amount = (int)Math.Min(missing, (long)Entities.MaxHealth[ally] * percent / 100);
                if (amount <= 0) continue;
                Entities.Health[ally] += amount;
                RootSnarerMend[ally].HealFreeTick = Tick + RootSnarerMendAllyCooldownTicks;
                healed++;
                _events.Add(SimEvent.Heal(id, ally, amount, Entities.Position[ally]));
            }
            var a = _rootSnarers[id];
            a.HitResolved = true; a.Healed = healed;
            _rootSnarers[id] = a;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.SnarerMend, Entities.Position[id], healed, healed > 0));
        }

        /// <summary>Лечится вполовину: элита расстановки, Вендиго, Шипомёт и босс встречи.</summary>
        private bool RootSnarerMendHalved(int ally)
            => IsElite(ally) || Entities.Kind[ally] == EnemyKind.ForestWendigo
                || Entities.Kind[ally] == EnemyKind.ForestThorncaster || ally == _encounterBoss;

        /// <summary>
        /// Удар корнями: круг встаёт на месте героя в этот тик и дальше не
        /// двигается — от него уходят, а не ждут, пока он догонит. Пул меток
        /// полон — круга нет вовсе: ни рисунка, ни удара.
        /// </summary>
        private void SlamRootSnarer(int id)
        {
            var a = _rootSnarers[id];
            a.Slammed = true;
            a.Target = Entities.Position[PlayerId];
            var look = a.Target - Entities.Position[id];
            if (look.LengthSq.Raw != 0) a.Direction = look.Normalized();
            Entities.Facing[id] = a.Direction;
            int slot = OpenTelegraph(id, RootSnarerCircle(a.Target), a.ImpactTick,
                a.ImpactTick + TelegraphLingerTicks, TelegraphFlags.SharedView);
            a.TelegraphSerial = TryGetTelegraph(slot, out var circle) ? circle.Serial : 0;
            _rootSnarers[id] = a;
        }

        /// <summary>
        /// Контакт. Попадание — ровно по нарисованному кругу: та же фигура из
        /// общего списка меток. Задел — урон и корни (или замедление, если
        /// переключатель выключен); мимо — ничего. Корни отбил иммунитет к
        /// контролю — остаётся только урон, события HeroControl нет.
        /// </summary>
        private void ResolveRootSnarerImpact(int id)
        {
            var a = _rootSnarers[id];
            // Состояние пишется ДО урона: отражённый урон (Зеркало Возмездия)
            // может убить самого моба внутри ApplyAbilityDamage.
            a.HitResolved = true;
            _rootSnarers[id] = a;
            EnemyTelegraph circle = default;
            int slot = FindTelegraph(a.TelegraphSerial);
            if (slot >= 0) TryGetTelegraph(slot, out circle);
            // Круга не было (пул полон) или он уже снят — контакта нет.
            if (!ResolveTelegraphSerial(a.TelegraphSerial)) return;
            bool hit = TelegraphContains(circle, Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.SnarerSlam, a.Target, 0, hit));
            if (!hit) return;
            int health = Entities.Health[PlayerId];
            ApplyAbilityDamage(id, PlayerId, RootSnarerDamageOf(id), -1, DamageType.Physical);
            // Корни — только если удар действительно достал: уклонение,
            // неуязвимость и отложенный урон их не вешают (правило воя Вендиго).
            // Переключатель — тернарником: if по константе дал бы CS0162.
            // 100% в ApplyHeroSlow — это корни (ApplyHeroRoot) от этого моба.
            if (Entities.Alive[PlayerId] && Entities.Health[PlayerId] < health)
                ApplyHeroSlow(SnarerRoots ? HeroRootPercent : RootSnarerSlowPercent,
                    SnarerRoots ? RootSnarerRootTicks : RootSnarerSlowTicks, id);
        }

        /// <summary>
        /// Снимает действие и круг, если он ещё не сработал, и бросает поход
        /// к союзнику. Перезарядка удара остаётся: оглушённый в позе не бьёт
        /// снова раньше срока. Лечение, сбитое до волны, готово снова через
        /// RootSnarerMendCancelCooldownTicks. Тишина — от тика снятия.
        /// </summary>
        private void CancelRootSnarer(int id)
        {
            ref var memory = ref RootSnarerMend[id];
            memory.ApproachUntilTick = 0;
            var a = _rootSnarers[id];
            if (a.Serial == 0) return;
            bool mend = a.Action == RootSnarerAction.Mend;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                mend ? EnemyActionKind.SnarerMend : EnemyActionKind.SnarerSlam, Entities.Position[id]));
            if (mend && !a.HitResolved) memory.NextMendTick = Tick + RootSnarerMendCancelCooldownTicks;
            memory.QuietUntilTick = Tick + RootSnarerMendGapTicks;
            _rootSnarers[id] = new RootSnarerState { NextActionTick = a.NextActionTick };
            // Смерть через Kill метку уже сняла — повторный вызов ничего не найдёт.
            CancelTelegraphsOf(id);
        }

        private void HashRootSnarers(ref ulong hash)
        {
            bool present = _rootSnarerSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++) present = Entities.Kind[id] == EnemyKind.ForestRootSnarer;
            if (!present) return;
            Hashing.Mix(ref hash, 0x534E4152); Hashing.Mix(ref hash, _rootSnarerSerial);
            var mend = RootSnarerMend;
            for (int id = 1; id < Entities.Count; id++)
            {
                var m = mend[id];
                // Отметка союзника о лечении — у любого вида.
                if (m.HealFreeTick != 0) { Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, m.HealFreeTick); }
                if (Entities.Kind[id] != EnemyKind.ForestRootSnarer) continue;
                var a = _rootSnarers[id]; Hashing.Mix(ref hash, id);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Action);
                Hashing.Mix(ref hash, a.StartTick);
                Hashing.Mix(ref hash, a.SlamTick); Hashing.Mix(ref hash, a.ImpactTick);
                Hashing.Mix(ref hash, a.EndTick); Hashing.Mix(ref hash, a.NextActionTick);
                Hashing.Mix(ref hash, a.Target.X.Raw); Hashing.Mix(ref hash, a.Target.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.TelegraphSerial); Hashing.Mix(ref hash, a.Slammed ? 1 : 0);
                Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
                Hashing.Mix(ref hash, a.StartHealth); Hashing.Mix(ref hash, a.Healed);
                Hashing.Mix(ref hash, m.NextMendTick); Hashing.Mix(ref hash, m.QuietUntilTick);
                Hashing.Mix(ref hash, m.ApproachUntilTick); Hashing.Mix(ref hash, m.ApproachAlly);
            }
        }
    }
}
