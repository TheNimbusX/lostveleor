using System;

namespace Game.Sim
{
    /// <summary>
    /// Состояние Корнехвата. Изменяемая структура: поля добавляет этот файл,
    /// и каждое новое обязано попасть в HashRootSnarers.
    /// </summary>
    public struct RootSnarerState
    {
        /// <summary>Номер действия; 0 — действия нет.</summary>
        public int Serial;

        /// <summary>Начало позы, удар корнями (круг на земле), контакт, конец стойки.</summary>
        public int StartTick, SlamTick, ImpactTick, EndTick;

        /// <summary>Когда удар снова готов. Переживает действие.</summary>
        public int NextActionTick;

        /// <summary>Центр круга — где стоял герой в тик удара корнями.</summary>
        public FixVec2 Target;

        /// <summary>
        /// Куда смотрит моб. До удара корнями — взгляд в начале позы (дальше
        /// он доворачивается к герою), с удара и до конца стойки — на круг.
        /// </summary>
        public FixVec2 Direction;

        /// <summary>Номер метки круга; 0 — ещё не открыт (или пул был полон).</summary>
        public int TelegraphSerial;

        /// <summary>
        /// Удар корнями случился. Круг при этом может и не встать — пул меток
        /// полон, — тогда TelegraphSerial остаётся нулём, и удара нет вовсе.
        /// </summary>
        public bool Slammed;

        /// <summary>Контакт разрешён: попал или мимо, второго не будет.</summary>
        public bool HitResolved;
    }

    /// <summary>
    /// КОРНЕХВАТ (план новых мобов от 26.09).
    ///
    /// Удар: герой в 1,5–7 м (между центрами), перезарядка готова и крупный
    /// жетон свободен — моб встаёт. 15 тиков только поза (событие
    /// EnemyActionStarted — им кормится звук EnemyWarning), метки на земле
    /// нет. На 15-м бьёт корнями: круг 1,5 м встаёт на месте героя в этот тик
    /// и больше не двигается. Через 21 тик контакт: герой в круге — урон и
    /// замедление 40% на 45 тиков (общее, ApplyHeroSlow), мимо — ничего.
    /// Потом 36 тиков стоит открытым — окно наказания. Крупный жетон — от
    /// начала позы до контакта. Оглушение, волок и смерть снимают действие,
    /// и ещё не сработавший круг гаснет вместе с ним.
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

        /// <summary>Замедление при попадании: минус 40% на 45 шагов героя.</summary>
        public const int RootSnarerSlowPercent = 40, RootSnarerSlowTicks = 45;

        /// <summary>
        /// Переключатель владельца: true — вместо замедления корни (100% на
        /// RootSnarerRootTicks). По умолчанию — замедление.
        /// </summary>
        public const bool SnarerRoots = false;
        public const int RootSnarerRootTicks = 12;

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

        private readonly RootSnarerState[] _rootSnarers;
        private int _rootSnarerSerial;

        public bool TryGetRootSnarerAction(int id, out RootSnarerState state)
        {
            state = (uint)id < (uint)_rootSnarers.Length ? _rootSnarers[id] : default;
            return state.Serial != 0;
        }

        /// <summary>Урон удара корнями — урон листа: глубина и «Сложно» приходят в него сами.</summary>
        public int RootSnarerDamageOf(int id) => Entities.Damage[id];

        /// <summary>Круг удара корнями с центром в center — одна фигура и для метки, и для удара.</summary>
        public static EnemyTelegraph RootSnarerCircle(FixVec2 center)
            => EnemyTelegraph.Circle(center, RootSnarerCircleRadius);

        /// <summary>Держит ли крупный жетон: от начала позы до контакта включительно. Для BigAttackTokenFree.</summary>
        internal bool RootSnarerHoldsBigToken(int id)
        {
            var a = _rootSnarers[id];
            return a.Serial != 0 && Tick <= a.ImpactTick;
        }

        private void ResetRootSnarers()
        {
            Array.Clear(_rootSnarers, 0, _rootSnarers.Length);
            _rootSnarerSerial = 0;
        }

        /// <summary>
        /// Первый удар готов сразу: у Вендиго первый прыжок ждёт, потому что
        /// прыжок бьёт издалека без подхода, а Корнехват всё равно сначала
        /// идёт на дистанцию. Волна, встающая из земли, держит его своим
        /// NextAttackTick.
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
        }

        /// <summary>
        /// Ход Корнехвата — всегда целиком (true): разворот, агро, шаг.
        ///
        /// В действии стоит. До удара корнями доворачивается к герою — поза
        /// целится, — с удара смотрит на круг до конца стойки. Без действия
        /// идёт к герою до RootSnarerHoldDistance.
        /// </summary>
        private bool MoveRootSnarer(int id, FixVec2 toPlayer)
        {
            var a = _rootSnarers[id];
            if (a.Serial != 0)
            {
                Entities.Velocity[id] = FixVec2.Zero;
                Entities.Facing[id] = a.Slammed ? a.Direction
                    : TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
                return true;
            }
            Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return true; }
            // Сначала разворот, потом шаг — и только вдоль взгляда (см. RootSnarerWalkAlignFrom).
            var facing = Entities.Facing[id]; var step = Entities.MoveStep[id];
            Fix64 wanted = Fix64.Zero;
            if (toPlayer.LengthSq > RootSnarerHoldDistance * RootSnarerHoldDistance)
            {
                var share = (FixVec2.Dot(facing, toPlayer.Normalized()) - RootSnarerWalkAlignFrom)
                    / (Fix64.One - RootSnarerWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон тот же, что у всех (AccelerationTicks), но скаляром вдоль
            // взгляда: боковой остаток прошлой скорости не доживает ни тика.
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(Entities.Velocity[id], facing));
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            var from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, facing * speed);
            Entities.Velocity[id] = Entities.Position[id] - from;
            return true;
        }

        /// <summary>
        /// Действия Корнехвата. Зовётся в Step после UpdateThorncasters —
        /// после движения героя: круг встаёт там, где герой стоит в этот тик.
        /// Обход по возрастанию индекса: крупный жетон берёт младший.
        /// </summary>
        private void UpdateRootSnarers()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestRootSnarer) continue;
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                    || ForcedMotion.IsActive(Entities, id))
                { CancelRootSnarer(id); continue; }
                var a = _rootSnarers[id];
                // Стойка кончилась — моб снова ходит; удар ждёт своей перезарядки.
                if (a.Serial != 0 && Tick >= a.EndTick)
                {
                    a = new RootSnarerState { NextActionTick = a.NextActionTick };
                    _rootSnarers[id] = a;
                }
                if (a.Serial == 0) { TryStartRootSnarer(id); continue; }
                if (!a.Slammed && Tick >= a.SlamTick) SlamRootSnarer(id);
                if (!_rootSnarers[id].HitResolved && Tick >= _rootSnarers[id].ImpactTick) ResolveRootSnarerImpact(id);
            }
        }

        private void TryStartRootSnarer(int id)
        {
            var a = _rootSnarers[id];
            if (!Entities.Aggro[id] || IsEmerging(id) || Tick < a.NextActionTick
                || Tick < Entities.NextAttackTick[id]) return;
            var distanceSq = (Entities.Position[PlayerId] - Entities.Position[id]).LengthSq;
            if (distanceSq < RootSnarerMinDistance * RootSnarerMinDistance
                || distanceSq > RootSnarerMaxDistance * RootSnarerMaxDistance) return;
            // Удар корнями — крупная атака: без жетона моб стоит на дистанции и ждёт.
            if (!BigAttackTokenFree(id)) return;
            int impact = Tick + RootSnarerSlamTicks + RootSnarerImpactDelayTicks;
            _rootSnarers[id] = new RootSnarerState
            {
                Serial = ++_rootSnarerSerial, StartTick = Tick, SlamTick = Tick + RootSnarerSlamTicks,
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
        /// общего списка меток. Задел — урон и замедление; мимо — ничего.
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
            // Замедление — только если удар действительно достал: уклонение,
            // неуязвимость и отложенный урон его не вешают (правило воя Вендиго).
            // Переключатель — тернарником: if по константе дал бы CS0162.
            if (Entities.Alive[PlayerId] && Entities.Health[PlayerId] < health)
                ApplyHeroSlow(SnarerRoots ? HeroRootPercent : RootSnarerSlowPercent,
                    SnarerRoots ? RootSnarerRootTicks : RootSnarerSlowTicks);
        }

        /// <summary>
        /// Снимает действие и круг, если он ещё не сработал. Перезарядка
        /// остаётся: оглушённый в позе не бьёт снова раньше срока.
        /// </summary>
        private void CancelRootSnarer(int id)
        {
            var a = _rootSnarers[id];
            if (a.Serial == 0) return;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                EnemyActionKind.SnarerSlam, Entities.Position[id]));
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
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestRootSnarer) continue;
                var a = _rootSnarers[id]; Hashing.Mix(ref hash, id);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, a.StartTick);
                Hashing.Mix(ref hash, a.SlamTick); Hashing.Mix(ref hash, a.ImpactTick);
                Hashing.Mix(ref hash, a.EndTick); Hashing.Mix(ref hash, a.NextActionTick);
                Hashing.Mix(ref hash, a.Target.X.Raw); Hashing.Mix(ref hash, a.Target.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.TelegraphSerial); Hashing.Mix(ref hash, a.Slammed ? 1 : 0);
                Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
            }
        }
    }
}
