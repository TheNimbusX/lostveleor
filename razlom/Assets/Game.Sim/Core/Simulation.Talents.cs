namespace Game.Sim
{
    /// <summary>
    /// Механика талантов сабельной ветки, у которой нет своего места внутри
    /// одной способности: удержание Вихря, огненный след кувырка, поджиг,
    /// эффекты при убийстве и неуязвимость Шквала.
    ///
    /// Всё включается флагом способности, а флаг приходит узлом взятого
    /// таланта. Без таланта здесь не выполняется ничего, и бой идёт как раньше.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- общее ----

        /// <summary>
        /// Кто элита или босс. Знает только расстановка, поэтому маску кладёт
        /// она; на тестовой арене маски нет, и элит там нет.
        /// </summary>
        private bool[] _eliteMask;

        public bool IsElite(int entity)
            => _eliteMask != null && (uint)entity < (uint)_eliteMask.Length && _eliteMask[entity];

        /// <summary>
        /// Только для тестов и отладочных сцен: пометить сущность элитой без
        /// расстановки. Публичный, потому что в Unity тесты — отдельная сборка.
        /// </summary>
        public void MarkElite(int entity)
        {
            if (_eliteMask == null || _eliteMask.Length < Entities.Capacity) _eliteMask = new bool[Entities.Capacity];
            _eliteMask[entity] = true;
        }

        private bool BuildHas(int slot, AbilityFlag flag, int definitionId)
        {
            if (slot < 0 || slot >= AbilitySlots) return false;
            AbilityBuild build = _abilityBuilds[slot];
            return build != null && build.DefinitionId == definitionId && build.Has(flag);
        }

        /// <summary>Проверка цели до прерывания текущего действия и расхода кулдауна.</summary>
        public bool ValidAbilityTarget(int target, AbilityBuild build)
            => build != null && target > 0 && target < Entities.Count && Entities.Alive[target]
                && Entities.Side[target] != Entities.Side[PlayerId]
                && (Entities.Position[target] - Entities.Position[PlayerId]).LengthSq
                    <= build.Get(AbilityStatType.Radius) * build.Get(AbilityStatType.Radius);

        /// <summary>Возврат лавидия игроку, не выше потолка.</summary>
        private void RefundLavidium(int amount)
        {
            if (amount <= 0) return;
            Fix64 cap = Fix64.FromInt(Entities.MaxLavidium[PlayerId]);
            Fix64 next = Entities.Lavidium[PlayerId] + Fix64.FromInt(amount);
            Entities.Lavidium[PlayerId] = next > cap ? cap : next;
        }

        /// <summary>
        /// Буферы на каждую сущность выделяются при сборке способностей, а не в
        /// бою: аллокация посреди тика запрещена.
        /// </summary>
        private void EnsureTalentBuffers()
        {
            if (_igniteUntil != null) return;
            _igniteUntil = new int[Entities.Capacity];
            _ignitePulseDamage = new int[Entities.Capacity];
            _igniteNextPulse = new int[Entities.Capacity];
        }

        // ---- Вихрь ----

        /// <summary>Удержание длится до 2 с от нажатия.</summary>
        public const int WhirlwindChannelTicks = 2 * TicksPerSecond;

        /// <summary>Пока Вихрь держат, он бьёт каждые полсекунды.</summary>
        public const int WhirlwindPulseTicks = TicksPerSecond / 2;

        /// <summary>15 лавидия в секунду.</summary>
        private static readonly Fix64 WhirlwindChannelDrainPerTick = Fix64.Ratio(1, 2);

        private int _whirlChannelSlot = -1;
        private int _whirlChannelEndTick;
        private int _whirlChannelNextPulse;

        public bool WhirlwindChanneling => _whirlChannelSlot >= 0;

        private void StopWhirlwindChannel() => _whirlChannelSlot = -1;

        /// <summary>
        /// Один оборот Вихря. Первый контакт возвращает лавидий, повторные
        /// обороты удержания — нет: иначе удержание кормило бы само себя.
        /// </summary>
        private void WhirlwindPulse(int slot, bool firstContact)
        {
            AbilityBuild build = _abilityBuilds[slot];
            int found = QueryRadiusIntoScratch(
                Entities.Position[PlayerId], build.Get(AbilityStatType.Radius), PlayerId);

            int victims = 0;
            for (int i = 0; i < found; i++)
            {
                int target = HitScratch[i];
                if (Entities.Alive[target] && Entities.Side[target] != Entities.Side[PlayerId]) victims++;
            }

            int damage = build.Get(AbilityStatType.Damage).ToInt();
            if (build.Has(AbilityFlag.WhirlwindCrowd) && victims > 0)
                damage = damage * (100 + 10 * System.Math.Min(victims, 5)) / 100;

            for (int i = 0; i < found; i++)
            {
                int target = HitScratch[i];
                if (!Entities.Alive[target]) continue;
                if (Entities.Side[target] == Entities.Side[PlayerId]) continue;
                ApplyAbilityDamage(PlayerId, target, damage, slot, DamageType.Physical);
            }

            if (firstContact && build.Has(AbilityFlag.WhirlwindRefund))
                RefundLavidium(System.Math.Min(victims * 3, 15));
        }

        private void BeginWhirlwindChannel(int slot, in InputFrame input)
        {
            if (!BuildHas(slot, AbilityFlag.WhirlwindChannel, AbilityDefinition.WhirlwindId)) return;
            if ((input.AbilityHoldMask & (1 << slot)) == 0) return;
            _whirlChannelSlot = slot;
            _whirlChannelEndTick = Tick - WhirlwindContactDelayTicks + WhirlwindChannelTicks;
            _whirlChannelNextPulse = Tick + WhirlwindPulseTicks;
        }

        private void UpdateWhirlwindChannel(in InputFrame input)
        {
            if (_whirlChannelSlot < 0) return;
            int slot = _whirlChannelSlot;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick)
                || Tick >= _whirlChannelEndTick
                || (input.AbilityHoldMask & (1 << slot)) == 0
                || !BuildHas(slot, AbilityFlag.WhirlwindChannel, AbilityDefinition.WhirlwindId)
                || Entities.Lavidium[PlayerId] < WhirlwindChannelDrainPerTick)
            {
                StopWhirlwindChannel();
                return;
            }

            Entities.Lavidium[PlayerId] -= WhirlwindChannelDrainPerTick;
            if (Tick < _whirlChannelNextPulse) return;
            _whirlChannelNextPulse = Tick + WhirlwindPulseTicks;
            WhirlwindPulse(slot, firstContact: false);
        }

        // ---- «Ладно смазал»: огненный след кувырка ----

        public const int BlazeTrailTicks = 3 * TicksPerSecond;
        private const int BlazeTrailCapacity = 16;
        private const int BlazeTrailPulseTicks = TicksPerSecond / 2;

        /// <summary>10 за полсекунды — 20 урона в секунду.</summary>
        private const int BlazeTrailPulseDamage = 10;

        private static readonly Fix64 BlazeTrailRadius = Fix64.Ratio(8, 10);

        private readonly FixVec2[] _trailAt = new FixVec2[BlazeTrailCapacity];
        private readonly int[] _trailUntil = new int[BlazeTrailCapacity];
        private int _trailCursor;
        private int _trailNextPulse;
        private int _trailLastDropTick = -100;

        /// <summary>Сколько кусков следа ещё горит. Показу — рисовать огонь.</summary>
        public int BlazeTrailCount(FixVec2[] into)
        {
            int count = 0;
            for (int s = 0; s < BlazeTrailCapacity; s++)
                if (_trailUntil[s] > Tick)
                {
                    if (into != null && count < into.Length) into[count] = _trailAt[s];
                    count++;
                }
            return count;
        }

        /// <summary>Зовётся из принудительного перемещения после шага тела.</summary>
        private void DropBlazeTrail(int entity)
        {
            if (entity != PlayerId || (ForcedMotionKind)Entities.ForcedKind[entity] != ForcedMotionKind.Roll) return;
            if (!BlazeActive || !BuildHas(_blazeSlot, AbilityFlag.BlazeTrail, AbilityDefinition.BlazeId)) return;
            if (Tick - _trailLastDropTick < 2) return;

            _trailLastDropTick = Tick;
            _trailAt[_trailCursor] = Entities.Position[entity];
            _trailUntil[_trailCursor] = Tick + BlazeTrailTicks;
            _trailCursor = (_trailCursor + 1) % BlazeTrailCapacity;
            _events.Add(new SimEvent(SimEventType.BlazeTrail, PlayerId, -1, BlazeTrailTicks, false,
                Entities.Position[entity]));
        }

        /// <summary>
        /// След бьёт как одна площадь: враг в двух кусках сразу получает урон
        /// один раз, иначе шестнадцать кусков вдоль пути множили бы урон.
        /// </summary>
        private void UpdateBlazeTrail()
        {
            bool burning = false;
            for (int s = 0; s < BlazeTrailCapacity; s++)
                if (_trailUntil[s] > Tick) { burning = true; break; }
            if (!burning || Tick < _trailNextPulse) return;

            _trailNextPulse = Tick + BlazeTrailPulseTicks;
            for (int target = 0; target < Entities.Count; target++)
            {
                if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                Fix64 reach = BlazeTrailRadius + Entities.BodyRadius[target];
                for (int s = 0; s < BlazeTrailCapacity; s++)
                {
                    if (_trailUntil[s] <= Tick) continue;
                    if (FixVec2.DistanceSq(_trailAt[s], Entities.Position[target]) > reach * reach) continue;
                    ApplyAbilityDamage(PlayerId, target, BlazeTrailPulseDamage, _blazeSlot,
                        DamageType.Fire, overTime: true);
                    break;
                }
            }
        }

        // ---- «Ладно смазал»: поджиг, уклонение, огонь на способности ----

        public const int BlazeIgniteTicks = 3 * TicksPerSecond;

        /// <summary>Горение бьёт импульсами по трети секунды: 10% удара в секунду — это треть от трети.</summary>
        private const int IgnitePulseTicks = TicksPerSecond / 3;

        private int[] _igniteUntil;
        private int[] _ignitePulseDamage;
        private int[] _igniteNextPulse;

        /// <summary>
        /// Своё горение, а не общий статус: тот бьёт целым числом за тик, и
        /// десятая доля удара в секунду обрезалась бы до нуля.
        /// </summary>
        private void ApplyBlazeIgnite(int source, int target, int attackPower)
        {
            if (source != PlayerId || !BlazeActive || _igniteUntil == null) return;
            if (!BuildHas(_blazeSlot, AbilityFlag.BlazeIgnite, AbilityDefinition.BlazeId)) return;
            if (!Entities.Alive[target] || attackPower <= 0) return;

            int pulse = attackPower / 30;
            _ignitePulseDamage[target] = pulse < 1 ? 1 : pulse;
            _igniteUntil[target] = Tick + BlazeIgniteTicks;
            if (_igniteNextPulse[target] <= Tick) _igniteNextPulse[target] = Tick + IgnitePulseTicks;
        }

        private void TickIgnite()
        {
            if (_igniteUntil == null) return;
            for (int i = 0; i < Entities.Count; i++)
            {
                if (_igniteUntil[i] <= Tick) continue;
                if (!Entities.Alive[i]) { _igniteUntil[i] = 0; continue; }
                if (Tick < _igniteNextPulse[i]) continue;
                _igniteNextPulse[i] = Tick + IgnitePulseTicks;
                ApplyAbilityDamage(PlayerId, i, _ignitePulseDamage[i], _blazeSlot, DamageType.Fire, overTime: true);
            }
        }

        public bool IsIgnited(int entity) => _igniteUntil != null && _igniteUntil[entity] > Tick;

        private void BlazeEvadeRefund()
        {
            if (BuildHas(_blazeSlot, AbilityFlag.BlazeEvadeRefund, AbilityDefinition.BlazeId))
                RefundLavidium(5);
        }

        private void ApplyBlazeAbilityBonus(int source, int target, int power, int slot)
        {
            if (slot == _blazeSlot || !BlazeActive) return;
            if (!BuildHas(_blazeSlot, AbilityFlag.BlazeAbilities, AbilityDefinition.BlazeId)) return;
            ApplyBlazeBonus(source, target, power);
        }

        // ---- Рассекающий удар: на ходу и веер ----

        private int _cleaveFanMask;
        private readonly int[] _cleaveFanTargets = { -1, -1, -1 };
        private static readonly Fix64 FanCos = Fix64.Ratio(8192, 10000);   // cos 35°
        private static readonly Fix64 FanSin = Fix64.Ratio(5736, 10000);   // sin 35°

        /// <summary>Рассекающий удар с талантом «На ходу» не останавливает героя.</summary>
        public bool CleaveMovable
            => CleaveActive && BuildHas(_cleaveSlot, AbilityFlag.CleaveOnTheMove, AbilityDefinition.CleaveId);

        /// <summary>Направление веера: 0 — центр, 1 и 2 — ±35° от взгляда при касте.</summary>
        private FixVec2 CleaveFanDirection(int index)
        {
            FixVec2 v = _cleaveDirection;
            if (index == 0) return v;
            Fix64 s = index == 1 ? FanSin : Fix64.Zero - FanSin;
            return new FixVec2(v.X * FanCos - v.Y * s, v.X * s + v.Y * FanCos);
        }

        private void HashCleaveFan(ref ulong hash)
        {
            Hashing.Mix(ref hash, _cleaveFanMask);
            for (int i = 0; i < _cleaveFanTargets.Length; i++) Hashing.Mix(ref hash, _cleaveFanTargets[i]);
        }

        // ---- Шквал и эффекты при убийстве ----

        private const int SquallKillCooldownTicks = TicksPerSecond / 2;

        /// <summary>Во время прыжков Шквала с талантом урон по герою не проходит.</summary>
        public bool SquallShielded
            => _chainHopsLeft > 0 && BuildHas(_chainSlot, AbilityFlag.SquallInvulnerable, AbilityDefinition.ChainStepId);

        private bool PlayerImmune => PlayerInvulnerable || SquallShielded;

        private void TalentOnKill(int target, int killer, int slot)
        {
            if (killer != PlayerId || target == PlayerId || slot < 0 || slot >= AbilitySlots) return;
            AbilityBuild build = _abilityBuilds[slot];
            if (build == null) return;

            if (build.DefinitionId == AbilityDefinition.CleaveId && build.Has(AbilityFlag.CleaveKillRefund))
                RefundLavidium(LavidiumCostOf(build));

            if (build.DefinitionId == AbilityDefinition.ChainStepId && build.Has(AbilityFlag.SquallKillCooldown)
                && slot == _chainSlot && _chainHopsLeft > 0)
            {
                int reduced = _abilityReadyTick[slot] - SquallKillCooldownTicks;
                _abilityReadyTick[slot] = reduced < Tick ? Tick : reduced;
            }
        }

        // ---- якорные таланты ----

        /// <summary>«Тяжёлый кулак»: оглушение 0,5 с.</summary>
        private const int BoardingStunTicks = TicksPerSecond / 2;

        /// <summary>«Рука на эфес»: −1 с перезарядки остальных способностей.</summary>
        private const int BoardingHiltTicks = TicksPerSecond;

        /// <summary>«На абордаж!»: кулак достаёт врагов в 2 м от точки прибытия.</summary>
        private static readonly Fix64 BoardingSweepRadius = Fix64.FromInt(2);

        /// <summary>Когда снова накопится второй заряд Абордажа.</summary>
        private int _boardingSpareReadyTick;

        private void BoardingAfterHit(AbilityBuild build, int target)
        {
            if (build.Has(AbilityFlag.BoardingStun) && Entities.Alive[target])
                StunByTalent(target, BoardingStunTicks);
        }

        private void StunByTalent(int target, int ticks)
        {
            Statuses.ApplyStun(target, Tick + ticks);
            Entities.Velocity[target] = FixVec2.Zero;
            Entities.PendingAttackTarget[target] = -1;
            Entities.AttackImpactTick[target] = 0;
            Entities.PendingAttackVariant[target] = 0;
            _events.Add(new SimEvent(SimEventType.Stun, PlayerId, target, ticks, false, Entities.Position[target]));
        }

        private void ShortenOtherCooldowns(int exceptSlot, int ticks)
        {
            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                if (slot == exceptSlot || _abilityBuilds[slot] == null) continue;
                int reduced = _abilityReadyTick[slot] - ticks;
                _abilityReadyTick[slot] = reduced < Tick ? Tick : reduced;
            }
        }

        /// <summary>
        /// «Два заряда»: если запасной заряд накоплен, кнопка возвращается сразу
        /// после прыжка, а сам заряд копится полной перезарядкой.
        /// </summary>
        private void AnchorTalentAfterCast(int slot, AbilityBuild build)
        {
            if (build.DefinitionId != AbilityDefinition.AnchorLeapId || !build.Has(AbilityFlag.BoardingTwoCharges)) return;
            if (Tick < _boardingSpareReadyTick) return;
            _abilityReadyTick[slot] = Tick + AnchorKit.LeapWindupTicks + AnchorKit.LeapTicks;
            _boardingSpareReadyTick = Tick + build.CooldownTicks;
        }

        private void HashTalents(ref ulong hash)
        {
            Hashing.Mix(ref hash, _boardingSpareReadyTick);
            Hashing.Mix(ref hash, _whirlChannelSlot);
            Hashing.Mix(ref hash, _whirlChannelEndTick);
            Hashing.Mix(ref hash, _whirlChannelNextPulse);
            Hashing.Mix(ref hash, _trailCursor);
            Hashing.Mix(ref hash, _trailNextPulse);
            Hashing.Mix(ref hash, _trailLastDropTick);
            for (int s = 0; s < BlazeTrailCapacity; s++)
            {
                Hashing.Mix(ref hash, _trailUntil[s]);
                Hashing.Mix(ref hash, _trailAt[s].X);
                Hashing.Mix(ref hash, _trailAt[s].Y);
            }
            if (_igniteUntil != null)
                for (int i = 0; i < Entities.Count; i++)
                {
                    Hashing.Mix(ref hash, _igniteUntil[i]);
                    Hashing.Mix(ref hash, _ignitePulseDamage[i]);
                    Hashing.Mix(ref hash, _igniteNextPulse[i]);
                }
            if (_eliteMask != null)
                for (int i = 0; i < Entities.Count && i < _eliteMask.Length; i++)
                    Hashing.Mix(ref hash, _eliteMask[i] ? 1 : 0);
        }
    }
}
