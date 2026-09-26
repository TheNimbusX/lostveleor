using System;

namespace Game.Sim
{
    /// <summary>
    /// Состояние Расщепеня и его детёныша на сущность. Изменяемая структура:
    /// поля добавляет этот файл, и каждое новое обязано попасть в HashSplitters.
    /// </summary>
    public struct SplitterState
    {
        /// <summary>У детёныша — индекс родителя; 0 — не из распада (герой родителем не бывает).</summary>
        public int Parent;
    }

    /// <summary>
    /// РАСЩЕПЕНЬ И ЕГО ДЕТЁНЫШ (план новых мобов от 26.09).
    ///
    /// Бьют общим ближним замахом (Simulation.EnemyMelee), числа замаха —
    /// здесь: Расщепень — сектор 1,8 м / 90°, замах 18, стойка 12, ближний
    /// жетон, как Хранитель; детёныш — укус, как Корнеполз, с жетоном укуса.
    /// Ходят общим ходом ближника (MoveSplitter отдаёт ход MoveEnemies).
    ///
    /// Распад: Kill ставит умершего Расщепеня в очередь (QueueSplit) — только
    /// настоящую смерть: уход в землю по концу выживания и Alive = false в
    /// тестах идут мимо Kill, а детёныш сам не делится. ResolvePendingSplits
    /// зовётся в Step после TickIgnite и до UpdateEncounterWaves: дети встают
    /// в тот же тик, и счёт живых никогда не видит ложного нуля.
    ///
    /// Дети — два детёныша по бокам от взгляда родителя. Здоровье и урон — доля
    /// родителя (120/420 и 4/12), поэтому глубина, «Сложно» и подстройка пачки
    /// доезжают до детей сами. Опыт 5, элитой не бывают. Их выбрасывает на метр
    /// за 8 тиков (ForcedMotionKind.SplitPop — не помеха укусу), и первые 15
    /// тиков они не кусают: иначе смерть Расщепеня вплотную к герою била бы
    /// двумя укусами без всякой позы.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- замах Расщепеня (профиль — MeleeProfileOf) ----
        public const int SplitterSwingWindupTicks = 18, SplitterSwingRecoveryTicks = 12;
        public const int SplitterSwingCycleTicks = 42;
        public static readonly Fix64 SplitterSwingRadius = Fix64.Ratio(9, 5);
        public static readonly Fix64 SplitterSwingArcCos = Fix64.Ratio(70711, 100000);   // ±45°
        public static readonly Fix64 SplitterSwingCommitCos = Fix64.Ratio(4, 5);         // старт, как у Хранителя
        public static readonly Fix64 SplitterChaseRange = Fix64.Ratio(8, 5);             // подходит на 1,6 м
        public static readonly Fix64 SplitterMoveSpeed = Fix64.Ratio(31, 10);

        // ---- распад ----
        /// <summary>Сколько детёнышей даёт Расщепень. Им же EnemyArchetypes.BodiesPerSpawn считает места.</summary>
        public const int SplitChildren = 2;

        /// <summary>За сколько тиков детёныша выбрасывает из тела родителя.</summary>
        public const int SplitPopTicks = 8;

        /// <summary>На сколько метров выбрасывает — от точки появления, вбок от взгляда родителя.</summary>
        public static readonly Fix64 SplitPopDistance = Fix64.One;

        /// <summary>Где встаёт детёныш: вбок от центра родителя, метры.</summary>
        public static readonly Fix64 SplitSideOffset = Fix64.Ratio(1, 10);

        // ---- детёныш ----
        public const int SplitlingBiteWindupTicks = 12, SplitlingBiteRecoveryTicks = 8;
        public const int SplitlingBiteCycleTicks = 30;
        /// <summary>Сколько тиков после появления детёныш не кусает.</summary>
        public const int SplitlingSpawnGuardTicks = 15;
        public const int SplitlingKillXp = 5;
        public static readonly Fix64 SplitlingBiteRange = Fix64.Ratio(7, 5);
        public static readonly Fix64 SplitlingMoveSpeed = Fix64.Ratio(21, 5);
        public static readonly Fix64 SplitlingRushSpeed = Fix64.FromInt(5);

        private readonly SplitterState[] _splitters;

        // Очередь распада этого тика: индексы умерших Расщепеней по порядку смерти.
        // Размер — ёмкость пула: индекс ставится не больше раза, так что места
        // хватает всегда, и в бою очередь не растёт.
        private readonly int[] _pendingSplits;
        private int _pendingSplitCount;

        /// <summary>Родитель детёныша или -1: не детёныш или поставлен не распадом.</summary>
        public int SplitParentOf(int id)
            => (uint)id < (uint)Entities.Count && _splitters[id].Parent > 0 ? _splitters[id].Parent : -1;

        private void ResetSplitters()
        {
            Array.Clear(_splitters, 0, _splitters.Length);
            Array.Clear(_pendingSplits, 0, _pendingSplits.Length);
            _pendingSplitCount = 0;
        }

        private void ConfigureSplitter(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestSplitter);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.One;
            var s = Entities.Stats[id];
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.Ratio(TicksPerSecond, SplitterSwingCycleTicks));
            s.SetBase(StatType.MoveSpeed, SplitterMoveSpeed);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            _splitters[id] = default;
        }

        /// <summary>
        /// Детёныш по строке таблицы. Распад поверх этого ставит здоровье и
        /// урон от родителя, родителя, выброс и паузу перед первым укусом.
        /// </summary>
        private void ConfigureSplitling(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestSplitling);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.FromInt(2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.Ratio(TicksPerSecond, SplitlingBiteCycleTicks));
            s.SetBase(StatType.MoveSpeed, SplitlingMoveSpeed);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = SplitlingKillXp;
            _splitters[id] = default;
        }

        /// <summary>
        /// Ход Расщепеня и детёныша. true — ход сделан целиком; false — общий
        /// ход ближника в MoveEnemies (подход, жетоны, кружение, рывок детёныша
        /// вплотную). Своего хода у обоих нет: они ближники, и чужой ход тут
        /// был бы только лишней копией общего.
        /// </summary>
        private bool MoveSplitter(int id, FixVec2 toPlayer) => false;

        /// <summary>
        /// Зовётся в Step после UpdateRootSnarers. Своих действий у Расщепеня
        /// нет: замах — общий (EnemyMelee), распад — ResolvePendingSplits в
        /// конце тика, когда все смерти тика уже случились.
        /// </summary>
        private void UpdateSplitters() { }

        /// <summary>
        /// Ставит умершего Расщепеня в очередь распада. Зовёт Kill — и только
        /// для вида ForestSplitter. Повтор того же индекса за тик не ставится.
        /// </summary>
        private void QueueSplit(int id)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestSplitter) return;
            for (int k = 0; k < _pendingSplitCount; k++) if (_pendingSplits[k] == id) return;
            if (_pendingSplitCount < _pendingSplits.Length) _pendingSplits[_pendingSplitCount++] = id;
        }

        /// <summary>
        /// Распад всех, кто умер в этом тике, в порядке смерти. Зовётся в Step
        /// после TickIgnite и до UpdateEncounterWaves; Kill между шагами (тесты)
        /// ждёт распада до конца следующего шага.
        ///
        /// Не внутри Kill: тот зовётся посреди обходов сущностей, и новые
        /// индексы в середине обхода получили бы ход, удар или урон в тик
        /// рождения. Здесь все обходы тика уже прошли, и сетка пересобирается
        /// один раз на всех детей.
        /// </summary>
        private void ResolvePendingSplits()
        {
            if (_pendingSplitCount == 0) return;
            for (int k = 0; k < _pendingSplitCount; k++)
            {
                // EntityStore.Spawn ёмкость не проверяет. Встречи считают места
                // на детей заранее (EnemyArchetypes.BodiesPerSpawn), так что
                // полный пул — только стенд; там детей просто нет.
                if (Entities.Count + SplitChildren > Entities.Capacity) continue;
                SplitParent(_pendingSplits[k]);
            }
            Array.Clear(_pendingSplits, 0, _pendingSplitCount);
            _pendingSplitCount = 0;
            Grid.Rebuild(Entities);
        }

        /// <summary>
        /// Дети одного родителя: встают подряд по индексу, первый — влево от
        /// взгляда родителя, второй — вправо, в SplitSideOffset от его центра.
        /// Числа — доля родителя: мёртвый держит свои MaxHealth и Damage, а в
        /// них уже вошли глубина арены, «Сложно» и подстройка пачки.
        /// </summary>
        private void SplitParent(int parent)
        {
            FixVec2 at = Entities.Position[parent];
            FixVec2 facing = Entities.Facing[parent];
            FixVec2 side = new FixVec2(-facing.Y, facing.X).Normalized();
            if (side.LengthSq.Raw == 0) side = new FixVec2(Fix64.Zero, Fix64.One);

            int health = Math.Max(1, EnemyArchetypes.Share(Entities.MaxHealth[parent],
                EnemyArchetypes.SplitlingHealth, EnemyArchetypes.SplitterHealth));
            Fix64 damage = Fix64.FromInt(EnemyArchetypes.Share(Entities.Damage[parent],
                EnemyArchetypes.SplitlingDamage, EnemyArchetypes.SplitterDamage));
            Fix64 radius = EnemyArchetypes.SplitlingBodyRadius;
            FixVec2 hero = Entities.Position[PlayerId];

            int first = Entities.Count;
            _events.Add(SimEvent.Split(parent, first, SplitChildren, at));
            for (int n = 0; n < SplitChildren; n++)
            {
                FixVec2 away = (n & 1) == 0 ? side : -side;
                FixVec2 spot = at + away * SplitSideOffset;
                // Родитель стоял на проходимом, и тело детёныша в 0,1 м от его
                // центра в нём помещается. Если родителя всё же вытолкнули в
                // стену — встаём ровно в его центр, а не в стену.
                if (_layout != null && !_layout.IsWalkable(spot, radius)) spot = at;

                int child = Entities.Spawn(spot, health, Faction.Orvill);
                ConfigureEnemy(child, EnemyKind.ForestSplitling);
                Entities.Stats[child].SetBase(StatType.Damage, damage);
                Entities.RefreshStats(child);
                Entities.Health[child] = Entities.MaxHealth[child];
                _splitters[child].Parent = parent;
                if (_eliteMask != null && child < _eliteMask.Length) _eliteMask[child] = false;

                // Герой уже в бою с родителем — дети не ждут обнаружения.
                Entities.Aggro[child] = true;
                FixVec2 look = (hero - spot).Normalized();
                Entities.Facing[child] = look.LengthSq.Raw != 0 ? look : facing;

                // Тик рождения уже идёт, замах проверяется со следующего: 15
                // тиков без укуса — это тики Tick + 1 … Tick + 15.
                Entities.NextAttackTick[child] = Tick + 1 + SplitlingSpawnGuardTicks;

                // Выброс прямой и упирается в стену (ResolveForcedMotion): тело
                // не скользит вдоль ствола и не пролетает сквозь него.
                ForcedMotion.Begin(Entities, child, spot + away * SplitPopDistance,
                    SplitPopTicks, ForcedMotionKind.SplitPop);
                _events.Add(SimEvent.Spawn(child, spot));
            }
        }

        private void HashSplitters(ref ulong hash)
        {
            bool present = _pendingSplitCount != 0;
            for (int id = 1; id < Entities.Count && !present; id++)
                present = Entities.Kind[id] == EnemyKind.ForestSplitter || Entities.Kind[id] == EnemyKind.ForestSplitling;
            if (!present) return;
            Hashing.Mix(ref hash, 0x53504C54); Hashing.Mix(ref hash, _pendingSplitCount);
            for (int k = 0; k < _pendingSplitCount; k++) Hashing.Mix(ref hash, _pendingSplits[k]);
            for (int id = 1; id < Entities.Count; id++)
            {
                var kind = Entities.Kind[id];
                if (kind != EnemyKind.ForestSplitter && kind != EnemyKind.ForestSplitling) continue;
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, _splitters[id].Parent);
            }
        }
    }
}
