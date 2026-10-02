namespace Game.Sim
{
    /// <summary>
    /// Шквал · ПЕННЫЙ СЛЕД (форма, владелец 02.10): каждый прыжок оставляет на
    /// земле полосу пены от старта до посадки. Полоса живёт FoamTrailLifeTicks;
    /// враг на ней раз в FoamTrailPulseTicks получает урон (доля удара Шквала,
    /// тик урона, не удар) и замедлен, пока стоит на ней и ещё
    /// FoamTrailSlowLingerTicks после. Полосы бьют как одна площадь: враг на
    /// двух сразу получает урон раз за импульс. Числа — ЗАГЛУШКИ.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int FoamTrailCapacity = 12;
        public const int FoamTrailLifeTicks = 3 * TicksPerSecond;
        public const int FoamTrailPulseTicks = TicksPerSecond / 2;
        public const int FoamTrailDamagePercent = 15;
        public const int FoamTrailSlowPercent = 30;
        public const int FoamTrailSlowLingerTicks = TicksPerSecond / 2;

        /// <summary>Полоса шириной в пол-роста героя.</summary>
        public static readonly Fix64 FoamTrailHalfWidth = Fix64.Ratio(45, 100);

        private const int FoamSlowModifierId = 0x53514654;   // "SQFT"

        private readonly FixVec2[] _foamTrailFrom = new FixVec2[FoamTrailCapacity];
        private readonly FixVec2[] _foamTrailDir = new FixVec2[FoamTrailCapacity];
        private readonly Fix64[] _foamTrailLength = new Fix64[FoamTrailCapacity];
        private readonly int[] _foamTrailUntil = new int[FoamTrailCapacity];
        private readonly int[] _foamTrailDamage = new int[FoamTrailCapacity];
        private int _foamTrailCursor, _foamTrailNextPulse, _foamTrailSlot = -1;
        private bool _foamTrailUsed;

        /// <summary>До какого тика враг замедлен пеной; 0 — не замедлен (модификатора нет).</summary>
        private int[] _foamSlowUntil;
        private int _foamSlowedCount;

        /// <summary>Полоса slot виду: откуда, куда и до какого тика живёт. False — полосы нет.</summary>
        public bool TryGetSquallFoamStrip(int slot, out FixVec2 from, out FixVec2 to, out int untilTick)
        {
            bool live = (uint)slot < FoamTrailCapacity && _foamTrailUntil[slot] > Tick;
            from = live ? _foamTrailFrom[slot] : FixVec2.Zero;
            to = live ? _foamTrailFrom[slot] + _foamTrailDir[slot] * _foamTrailLength[slot] : FixVec2.Zero;
            untilTick = live ? _foamTrailUntil[slot] : 0;
            return live;
        }

        /// <summary>Враг замедлен пеной (виду — пена у щиколоток).</summary>
        public bool SquallFoamSlowed(int id) => _foamSlowUntil != null && (uint)id < (uint)_foamSlowUntil.Length && _foamSlowUntil[id] > Tick;

        /// <summary>При сборке способностей (EnsureTalentBuffers), а не в бою.</summary>
        private void EnsureSquallFoamBuffers()
        {
            if (_foamSlowUntil == null) _foamSlowUntil = new int[Entities.Capacity];
        }

        private bool AnyFoamStripLive()
        {
            for (int s = 0; s < FoamTrailCapacity; s++) if (_foamTrailUntil[s] > Tick) return true;
            return false;
        }

        /// <summary>Прыжок приземлился: полоса from → to.</summary>
        private void LaySquallFoamStrip(AbilityBuild build, FixVec2 from, FixVec2 to)
        {
            if (_foamSlowUntil == null) return;
            if (!AnyFoamStripLive()) _foamTrailNextPulse = Tick + FoamTrailPulseTicks;
            int s = _foamTrailCursor;
            _foamTrailCursor = (s + 1) % FoamTrailCapacity;
            FixVec2 delta = to - from;
            Fix64 length = delta.Length;
            _foamTrailFrom[s] = from;
            _foamTrailDir[s] = length.Raw != 0 ? delta / length : Entities.Facing[PlayerId];
            _foamTrailLength[s] = length;
            _foamTrailUntil[s] = Tick + FoamTrailLifeTicks;
            _foamTrailDamage[s] = build.Get(AbilityStatType.Damage).ToInt() * FoamTrailDamagePercent / 100;
            _foamTrailSlot = _squall.Slot;
            _foamTrailUsed = true;
            _events.Add(new SimEvent(SimEventType.SquallFoamStrip, PlayerId, -1, s, false, from,
                DamageType.Physical, DamageOrigin.Ability, FoamTrailLifeTicks));
        }

        /// <summary>Каждый тик (из ContinueChainStep): замедление стоящих на пене, импульс урона, снятие замедления.</summary>
        private void UpdateSquallFoamTrail()
        {
            if (!_foamTrailUsed || _foamSlowUntil == null) return;
            bool live = AnyFoamStripLive();
            if (!live && _foamSlowedCount == 0) return;
            bool pulse = live && Tick >= _foamTrailNextPulse;
            if (pulse) _foamTrailNextPulse = Tick + FoamTrailPulseTicks;

            for (int i = 1; i < Entities.Count && i < _foamSlowUntil.Length; i++)
            {
                int damage = 0;
                if (live && Entities.Alive[i] && Entities.Side[i] != Entities.Side[PlayerId] && !ThicketShielded(i))
                    for (int s = 0; s < FoamTrailCapacity; s++)
                    {
                        if (_foamTrailUntil[s] <= Tick) continue;
                        if (!InsideLane(i, _foamTrailFrom[s], _foamTrailDir[s], Fix64.Zero, _foamTrailLength[s], FoamTrailHalfWidth)) continue;
                        if (_foamTrailDamage[s] > damage) damage = _foamTrailDamage[s];
                        if (damage == 0) damage = -1;   // полоса без урона всё равно замедляет
                    }

                if (damage != 0)
                {
                    SlowByFoam(i);
                    if (pulse && damage > 0) ApplyAbilityDamage(PlayerId, i, damage, _foamTrailSlot, DamageType.Physical, overTime: true);
                }
                else if (_foamSlowUntil[i] != 0 && (_foamSlowUntil[i] <= Tick || !Entities.Alive[i])) UnslowByFoam(i);
            }
        }

        /// <summary>Замедление пеной; Хозяина Чащи пена не держит. Лист статов пересчитается в начале следующего тика.</summary>
        private void SlowByFoam(int id)
        {
            if (Entities.Kind[id] == EnemyKind.ForestThicketMaster) return;
            if (_foamSlowUntil[id] == 0)
            {
                Entities.Stats[id].Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(-FoamTrailSlowPercent, 100),
                    ModifierSource.Buff, FoamSlowModifierId));
                _foamSlowedCount++;
            }
            _foamSlowUntil[id] = Tick + FoamTrailSlowLingerTicks;
        }

        private void UnslowByFoam(int id)
        {
            Entities.Stats[id].RemoveSource(ModifierSource.Buff, FoamSlowModifierId);
            _foamSlowUntil[id] = 0;
            _foamSlowedCount--;
        }

        private void ResetSquallFoamTrail()
        {
            if (_foamSlowUntil != null && _foamSlowedCount > 0)
                for (int i = 1; i < Entities.Count && i < _foamSlowUntil.Length; i++)
                    if (_foamSlowUntil[i] != 0) UnslowByFoam(i);
            if (_foamSlowUntil != null) System.Array.Clear(_foamSlowUntil, 0, _foamSlowUntil.Length);
            _foamSlowedCount = 0;
            System.Array.Clear(_foamTrailUntil, 0, FoamTrailCapacity);
            _foamTrailCursor = _foamTrailNextPulse = 0;
            _foamTrailSlot = -1;
            _foamTrailUsed = false;
        }

        /// <summary>Только после первой полосы расстановки: без формы хеш прежний.</summary>
        private void HashSquallFoamTrail(ref ulong hash)
        {
            if (!_foamTrailUsed) return;
            Hashing.Mix(ref hash, 0x53514654);   // "SQFT"
            Hashing.Mix(ref hash, _foamTrailCursor);
            Hashing.Mix(ref hash, _foamTrailNextPulse);
            Hashing.Mix(ref hash, _foamTrailSlot);
            Hashing.Mix(ref hash, _foamSlowedCount);
            for (int s = 0; s < FoamTrailCapacity; s++)
            {
                Hashing.Mix(ref hash, _foamTrailUntil[s]);
                if (_foamTrailUntil[s] <= Tick) continue;
                Hashing.Mix(ref hash, _foamTrailFrom[s].X); Hashing.Mix(ref hash, _foamTrailFrom[s].Y);
                Hashing.Mix(ref hash, _foamTrailDir[s].X); Hashing.Mix(ref hash, _foamTrailDir[s].Y);
                Hashing.Mix(ref hash, _foamTrailLength[s]);
                Hashing.Mix(ref hash, _foamTrailDamage[s]);
            }
            for (int i = 0; i < Entities.Count && i < _foamSlowUntil.Length; i++) Hashing.Mix(ref hash, _foamSlowUntil[i]);
        }
    }
}
