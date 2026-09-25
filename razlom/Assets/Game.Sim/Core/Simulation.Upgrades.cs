namespace Game.Sim
{
    /// <summary>
    /// Усиления 6–8 каждой линии — выбор владельца 24 сентября (DESIGN, «Выбор владельца,
    /// 24 сентября (ночь)»). Всё включается флагом способности, флаг приходит узлом взятого
    /// усиления; без усиления здесь не выполняется ничего, и бой идёт как раньше.
    ///
    /// Своё место внутри одной способности у большинства из них есть — там и стоит вызов,
    /// а состояние, которое живёт дольше самой способности (волна Вихря, трещина якоря,
    /// повтор Рассекающего, метка «Раскол брони»), собрано здесь.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- Вихрь: «Затягивает», «Режущая волна», «Стальной кокон» ----

        /// <summary>«Затягивает»: враги в 3,5 м подтягиваются к центру к моменту удара.</summary>
        private static readonly Fix64 WhirlwindPullRadius = Fix64.Ratio(35, 10);

        /// <summary>«Режущая волна»: кольцо расходится до 4 м от Пелага за 8 тиков, 40% урона.</summary>
        private static readonly Fix64 WhirlwindWaveReach = Fix64.FromInt(4);
        private const int WhirlwindWaveTicks = 8;

        /// <summary>«Стальной кокон»: сколько после контакта Вихря ещё держится защита, тиков.</summary>
        private const int WhirlwindCocoonTailTicks = 12;

        private int _waveSlot = -1, _waveStartTick, _waveDamage;
        private FixVec2 _waveCenter;
        private Fix64 _waveInner;
        private bool[] _waveHit;
        private int _cocoonUntilTick;

        private void WhirlwindUpgradesAtCast(int slot)
        {
            AbilityBuild build = _abilityBuilds[slot];
            int contact = AbilityExecutionTicks(WhirlwindContactDelayTicks);
            if (build.Has(AbilityFlag.WhirlwindCocoon)) _cocoonUntilTick = Tick + contact + WhirlwindCocoonTailTicks;
            if (!build.Has(AbilityFlag.WhirlwindPull)) return;

            FixVec2 center = Entities.Position[PlayerId];
            Fix64 near = Entities.BodyRadius[PlayerId] + Fix64.Ratio(1, 2);
            int ticks = contact > ForcedMotion.MinTicks ? contact - 1 : ForcedMotion.MinTicks;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                FixVec2 delta = Entities.Position[i] - center;
                Fix64 distance = delta.Length;
                Fix64 stop = near + Entities.BodyRadius[i];
                if (distance > WhirlwindPullRadius + Entities.BodyRadius[i] || distance <= stop || distance.Raw == 0) continue;
                ForcedMotion.Begin(Entities, i, center + delta / distance * stop, ticks, ForcedMotionKind.Dragged);
            }
        }

        private void StartWhirlwindWave(int slot)
        {
            AbilityBuild build = _abilityBuilds[slot];
            if (!build.Has(AbilityFlag.WhirlwindWave) || _waveHit == null) return;
            _waveSlot = slot;
            _waveStartTick = Tick;
            _waveCenter = Entities.Position[PlayerId];
            _waveInner = build.Get(AbilityStatType.Radius);
            _waveDamage = build.Get(AbilityStatType.Damage).ToInt() * 40 / 100;
            System.Array.Clear(_waveHit, 0, _waveHit.Length);
        }

        private void UpdateWhirlwindWave()
        {
            if (_waveSlot < 0) return;
            int elapsed = Tick - _waveStartTick + 1;
            Fix64 outer = WhirlwindWaveReach > _waveInner ? WhirlwindWaveReach : _waveInner + Fix64.One;
            Fix64 radius = _waveInner + (outer - _waveInner) * Fix64.Ratio(System.Math.Min(elapsed, WhirlwindWaveTicks), WhirlwindWaveTicks);
            for (int i = 1; i < Entities.Count; i++)
            {
                if (_waveHit[i] || !Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                Fix64 edge = (Entities.Position[i] - _waveCenter).Length - Entities.BodyRadius[i];
                // Внутри круга Вихря враг уже получил удар оборота — волна бьёт только дальше.
                if (edge <= _waveInner || edge > radius) continue;
                _waveHit[i] = true;
                ApplyAbilityDamage(PlayerId, i, _waveDamage, _waveSlot, DamageType.Physical);
            }
            if (elapsed >= WhirlwindWaveTicks) _waveSlot = -1;
        }

        private bool CocoonActive
            => Tick < _cocoonUntilTick
               || WhirlwindChanneling && BuildHas(_whirlChannelSlot, AbilityFlag.WhirlwindCocoon, AbilityDefinition.WhirlwindId);

        // ---- Рассекающий удар: «Раскол брони», «Двойной замах», «Волна клинка» ----

        /// <summary>«Раскол брони»: метка держится 4 с, следующий удар Пелага по цели +25%.</summary>
        private const int SunderTicks = 4 * TicksPerSecond;

        /// <summary>«Двойной замах»: второй удар через 0,3 с, половина урона.</summary>
        private const int CleaveEchoDelayTicks = 9;

        /// <summary>«Волна клинка»: 3 м дальше клинка, полоса шириной 1,2 м, половина урона.</summary>
        private static readonly Fix64 CleaveWaveLength = Fix64.FromInt(3);
        private static readonly Fix64 CleaveWaveHalfWidth = Fix64.Ratio(6, 10);

        private int[] _sunderUntil;
        private int _echoTick = -1, _echoSlot = -1, _echoDamage;
        private readonly int[] _echoTargets = { -1, -1, -1 };

        private void CleaveUpgradesOnHit(AbilityBuild build, int target, int direction, int damage)
        {
            if (build.Has(AbilityFlag.CleaveSunder) && _sunderUntil != null && Entities.Alive[target])
                _sunderUntil[target] = Tick + SunderTicks;
            if (!build.Has(AbilityFlag.CleaveDouble)) return;
            if (_echoTick != Tick + CleaveEchoDelayTicks)
            {
                for (int i = 0; i < _echoTargets.Length; i++) _echoTargets[i] = -1;
                _echoTick = Tick + CleaveEchoDelayTicks;
                _echoSlot = _cleaveSlot;
                _echoDamage = damage / 2;
            }
            if ((uint)direction < (uint)_echoTargets.Length) _echoTargets[direction] = target;
        }

        private void UpdateCleaveEcho()
        {
            if (_echoTick < 0 || Tick < _echoTick) return;
            int slot = _echoSlot;
            _echoTick = _echoSlot = -1;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick)) return;
            for (int i = 0; i < _echoTargets.Length; i++)
            {
                int target = _echoTargets[i];
                _echoTargets[i] = -1;
                if (target > 0 && Entities.Alive[target]) ApplyAbilityDamage(PlayerId, target, _echoDamage, slot, DamageType.Physical);
            }
        }

        /// <summary>«Волна клинка»: в момент контакта удар уходит волной дальше клинка.</summary>
        private void CleaveWave(AbilityBuild build)
        {
            if (!build.Has(AbilityFlag.CleaveWave)) return;
            FixVec2 from = Entities.Position[PlayerId];
            Fix64 start = build.Get(AbilityStatType.Radius);
            int damage = build.Get(AbilityStatType.Damage).ToInt() / 2;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                if (!InsideLane(i, from, _cleaveDirection, start, start + CleaveWaveLength, CleaveWaveHalfWidth)) continue;
                ApplyAbilityDamage(PlayerId, i, damage, _cleaveSlot, DamageType.Physical);
            }
        }

        /// <summary>«Раскол брони»: помеченная цель получает следующий удар Пелага на 25% сильнее, метка снимается.</summary>
        private int ApplySunder(int target, int amount)
        {
            if (_sunderUntil == null || _sunderUntil[target] <= Tick) return amount;
            _sunderUntil[target] = 0;
            return amount * 125 / 100;
        }

        // ---- «Ладно смазал»: «Вспышка», «Жар клинка», «Подбросить дров» ----

        private static readonly Fix64 BlazeFlareRadius = Fix64.FromInt(2);
        private const int BlazeFlareDamage = 60;
        private const int BlazeHasteModifierId = 0x4248;
        /// <summary>«Подбросить дров»: +0,5 с за убийство, не больше +3 с за один поджог.</summary>
        private const int BlazeStokeTicks = TicksPerSecond / 2;
        private const int BlazeStokeMaxTicks = 3 * TicksPerSecond;

        private bool _blazeHasteOn;
        private int _blazeStoked;

        /// <summary>В момент поджога сабли: «Вспышка» бьёт огнём вокруг, счётчик «Подбросить дров» с нуля.</summary>
        private void BlazeUpgradesAtIgnition(AbilityBuild build)
        {
            _blazeStoked = 0;
            if (!build.Has(AbilityFlag.BlazeFlare)) return;
            int found = QueryRadiusIntoScratch(Entities.Position[PlayerId], BlazeFlareRadius, PlayerId);
            for (int i = 0; i < found; i++)
            {
                int target = HitScratch[i];
                if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                ApplyAbilityDamage(PlayerId, target, BlazeFlareDamage, _blazeSlot, DamageType.Fire);
            }
        }

        /// <summary>«Жар клинка»: пока сабля горит, автоатаки на 20% быстрее.</summary>
        private void UpdateBlazeHaste()
        {
            bool want = BlazeActive && BuildHas(_blazeSlot, AbilityFlag.BlazeHaste, AbilityDefinition.BlazeId);
            if (want == _blazeHasteOn) return;
            _blazeHasteOn = want;
            var sheet = Entities.Stats[PlayerId];
            sheet.RemoveSource(ModifierSource.Buff, BlazeHasteModifierId);
            if (want) sheet.Add(StatModifier.Increased(StatType.AttackSpeed, Fix64.Ratio(20, 100), ModifierSource.Buff, BlazeHasteModifierId));
        }

        // ---- Шквал: «Возврат», «Двойной прыжок», «Первый удар» ----

        private FixVec2 _chainOrigin;
        private bool _chainRepeatHop;
        private const int SquallReturnTicks = 8;

        /// <summary>Урон прыжка с усилениями: первый по целой цели ×2, повтор в ту же цель +50%.</summary>
        private int SquallHopDamage(AbilityBuild build, int target, int damage)
        {
            if (_chainVisitedCount == 1 && build.Has(AbilityFlag.SquallOpener)
                && Entities.Health[target] >= Entities.MaxHealth[target]) damage *= 2;
            if (_chainRepeatHop && build.Has(AbilityFlag.SquallRepeat)) damage = damage * 150 / 100;
            return damage;
        }

        /// <summary>«Возврат»: серия кончилась — Пелаг возвращается туда, откуда начал.</summary>
        private void SquallReturn(AbilityBuild build)
        {
            if (build == null || !build.Has(AbilityFlag.SquallReturn) || !Entities.Alive[PlayerId]) return;
            ForcedMotion.Begin(Entities, PlayerId, _chainOrigin, SquallReturnTicks, ForcedMotionKind.Lunge);
        }

        // ---- Удар якорем: «Трещина», «Отдача» ----

        /// <summary>«Трещина»: полоса лежит 3 с, первый шаг врага на неё оглушает на 0,3 с.</summary>
        private const int CrackTicks = 3 * TicksPerSecond;
        private const int CrackStunTicks = 9;
        /// <summary>«Отдача»: каждый задетый враг −0,3 с перезарядки удара.</summary>
        private const int SlamRecoilTicks = 9;

        private int _crackUntilTick, _crackLanes;
        private FixVec2 _crackOrigin;
        private Fix64 _crackLength, _crackHalfWidth;
        private readonly FixVec2[] _crackDirections = new FixVec2[3];
        private bool[] _crackDone;

        private void StartCrack(AbilityBuild build, int lanes, Fix64 length, Fix64 halfWidth)
        {
            if (!build.Has(AbilityFlag.AnchorSlamCrack) || _crackDone == null) return;
            _crackUntilTick = Tick + CrackTicks;
            _crackLanes = lanes;
            _crackOrigin = _slamOrigin;
            _crackLength = length;
            _crackHalfWidth = halfWidth;
            for (int lane = 0; lane < lanes && lane < _crackDirections.Length; lane++) _crackDirections[lane] = SlamLaneDirection(lane);
            System.Array.Clear(_crackDone, 0, _crackDone.Length);
        }

        /// <summary>Задетые самим ударом уже оглушены им — трещина их не трогает.</summary>
        private void MarkCracked(int entity)
        {
            if (_crackDone != null && _crackUntilTick > Tick) _crackDone[entity] = true;
        }

        private void UpdateCrack()
        {
            if (_crackUntilTick <= Tick || _crackDone == null) return;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (_crackDone[i] || !Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                bool inside = false;
                for (int lane = 0; lane < _crackLanes && !inside; lane++)
                    inside = InsideLane(i, _crackOrigin, _crackDirections[lane], Fix64.Zero, _crackLength, _crackHalfWidth);
                if (!inside) continue;
                _crackDone[i] = true;
                StunByTalent(i, CrackStunTicks);
            }
        }

        private void SlamRecoil(AbilityBuild build, int slot, int hits)
        {
            if (hits <= 0 || !build.Has(AbilityFlag.AnchorSlamRecoil)) return;
            int reduced = _abilityReadyTick[slot] - SlamRecoilTicks * hits;
            _abilityReadyTick[slot] = reduced < Tick ? Tick : reduced;
        }

        // ---- Крушение: «Неудержимый», «Сотрясение», «Раскрутка» ----

        private const int WreckConcussTicks = 9;

        /// <summary>«Раскрутка»: каждый следующий удар серии +15%.</summary>
        private static int WreckMomentum(AbilityBuild build, int stage, int damage)
            => build.Has(AbilityFlag.WreckMomentum) ? damage * (100 + 15 * stage) / 100 : damage;

        // ---- Абордаж: «Разгон», «Сорвать атаку», «Верный удар» ----

        /// <summary>«Верный удар»: следующая автоатака в течение 2 с — крит.</summary>
        private const int SureCritTicks = 2 * TicksPerSecond;

        private FixVec2 _leapFrom;
        private int _sureCritUntilTick;

        private void BoardingUpgradesAtLaunch(AbilityBuild build)
        {
            _leapFrom = Entities.Position[PlayerId];
            if (build == null || !build.Has(AbilityFlag.BoardingInterrupt)) return;
            if ((uint)_leapTarget >= (uint)Entities.Count || !Entities.Alive[_leapTarget]) return;
            // Зацеп сбивает замах: начатая атака врага пропадает, как от оглушения.
            Entities.PendingAttackTarget[_leapTarget] = -1;
            Entities.AttackImpactTick[_leapTarget] = 0;
            Entities.PendingAttackVariant[_leapTarget] = 0;
        }

        /// <summary>«Разгон»: +10% урона кулака за каждый метр полёта.</summary>
        private int BoardingMomentum(AbilityBuild build, int damage)
        {
            if (!build.Has(AbilityFlag.BoardingMomentum)) return damage;
            int meters = (Entities.Position[PlayerId] - _leapFrom).Length.ToInt();
            return damage * (100 + 10 * meters) / 100;
        }

        private void BoardingSureCrit(AbilityBuild build)
        {
            if (build.Has(AbilityFlag.BoardingSureCrit)) _sureCritUntilTick = Tick + SureCritTicks;
        }

        /// <summary>Бросок на крит уже сделан; «Верный удар» его подменяет и гаснет.</summary>
        private bool SureCrit(int source, bool crit)
        {
            if (source != PlayerId || _sureCritUntilTick <= Tick) return crit;
            _sureCritUntilTick = 0;
            return true;
        }

        // ---- Взрывная смесь: «Два броска», «Шрапнель» ----

        private static readonly Fix64 ShrapnelRadius = Fix64.FromInt(4);
        private const int ShrapnelShards = 3, ShrapnelDamage = 30;
        private int _flaskSpareReadyTick;

        /// <summary>«Два броска»: запасной заряд готов — кнопка возвращается сразу после броска.</summary>
        private void FlaskTwoCharges(int slot, AbilityBuild build)
        {
            if (build.DefinitionId != AbilityDefinition.FireFlaskId || !build.Has(AbilityFlag.FlaskTwoCharges)) return;
            if (Tick < _flaskSpareReadyTick) return;
            _abilityReadyTick[slot] = Tick + AbilityExecutionTicks(6) + 1;
            _flaskSpareReadyTick = Tick + AbilityCooldownTicks(build);
        }

        /// <summary>«Шрапнель»: взрыв бросает 3 осколка в случайных врагов до 4 м, по 30.</summary>
        private void FlaskShrapnel(AbilityBuild build, int slot, FixVec2 at)
        {
            if (!build.Has(AbilityFlag.FlaskShrapnel)) return;
            int count = 0;
            for (int i = 1; i < Entities.Count && count < _arcScratch.Length; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                Fix64 reach = ShrapnelRadius + Entities.BodyRadius[i];
                if ((Entities.Position[i] - at).LengthSq > reach * reach) continue;
                _arcScratch[count++] = i;
            }
            for (int shard = 0; shard < ShrapnelShards && count > 0; shard++)
            {
                int pick = Rng.AbilityTargets.NextInt(0, count);
                int target = _arcScratch[pick];
                _arcScratch[pick] = _arcScratch[--count];
                ApplyAbilityDamage(PlayerId, target, ShrapnelDamage, slot, DamageType.Physical);
            }
        }

        // ---- общее ----

        /// <summary>Полоса от точки вдоль направления: тело задето, если касается прямоугольника [start…end] × 2·halfWidth.</summary>
        private bool InsideLane(int entity, FixVec2 origin, FixVec2 direction, Fix64 start, Fix64 end, Fix64 halfWidth)
        {
            FixVec2 delta = Entities.Position[entity] - origin;
            Fix64 along = FixVec2.Dot(delta, direction);
            Fix64 across = Fix64.Abs(delta.X * direction.Y - delta.Y * direction.X);
            Fix64 dx = along - Fix64.Clamp(along, start, end);
            Fix64 dy = across > halfWidth ? across - halfWidth : Fix64.Zero;
            return dx * dx + dy * dy <= Entities.BodyRadius[entity] * Entities.BodyRadius[entity];
        }

        /// <summary>Защита героя от усилений: «Стальной кокон» −30% во время Вихря, «Неудержимый» −25% во время Крушения.</summary>
        private int ApplyUpgradeReduction(int target, int damage)
        {
            if (target != PlayerId) return damage;
            if (CocoonActive) damage = damage * 70 / 100;
            if (_wreckSlot >= 0 && BuildHas(_wreckSlot, AbilityFlag.WreckUnstoppable, AbilityDefinition.WreckId)) damage = damage * 75 / 100;
            return damage;
        }

        /// <summary>Убийство Пелагом, чем бы ни добил: «Подбросить дров» продлевает огонь сабли.</summary>
        private void UpgradeOnKill(int target, int killer)
        {
            if (killer != PlayerId || target == PlayerId || !BlazeActive || _blazeStoked >= BlazeStokeMaxTicks) return;
            if (!BuildHas(_blazeSlot, AbilityFlag.BlazeStoke, AbilityDefinition.BlazeId)) return;
            int add = System.Math.Min(BlazeStokeTicks, BlazeStokeMaxTicks - _blazeStoked);
            _blazeStoked += add;
            _blazeUntilTick += add;
        }

        private void EnsureUpgradeBuffers()
        {
            if (_waveHit != null) return;
            _waveHit = new bool[Entities.Capacity];
            _sunderUntil = new int[Entities.Capacity];
            _crackDone = new bool[Entities.Capacity];
        }

        /// <summary>Каждый тик после способностей: волна, повтор удара, трещина, жар клинка.</summary>
        private void UpdateUpgrades()
        {
            UpdateWhirlwindWave();
            UpdateCleaveEcho();
            UpdateCrack();
            UpdateBlazeHaste();
        }

        private void ResetUpgrades()
        {
            _waveSlot = _echoTick = _echoSlot = -1;
            _cocoonUntilTick = _crackUntilTick = _crackLanes = _sureCritUntilTick = _blazeStoked = 0;
            _chainRepeatHop = false;
            for (int i = 0; i < _echoTargets.Length; i++) _echoTargets[i] = -1;
            if (_sunderUntil != null) System.Array.Clear(_sunderUntil, 0, _sunderUntil.Length);
            if (_blazeHasteOn && Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, BlazeHasteModifierId);
            _blazeHasteOn = false;
        }

        private void HashUpgrades(ref ulong hash)
        {
            Hashing.Mix(ref hash, _waveSlot);
            Hashing.Mix(ref hash, _waveStartTick);
            Hashing.Mix(ref hash, _waveDamage);
            Hashing.Mix(ref hash, _cocoonUntilTick);
            Hashing.Mix(ref hash, _echoTick);
            Hashing.Mix(ref hash, _echoDamage);
            Hashing.Mix(ref hash, _blazeStoked);
            Hashing.Mix(ref hash, _blazeHasteOn ? 1 : 0);
            Hashing.Mix(ref hash, _chainRepeatHop ? 1 : 0);
            Hashing.Mix(ref hash, _crackUntilTick);
            Hashing.Mix(ref hash, _sureCritUntilTick);
            Hashing.Mix(ref hash, _flaskSpareReadyTick);
            for (int i = 0; i < _echoTargets.Length; i++) Hashing.Mix(ref hash, _echoTargets[i]);
            if (_sunderUntil != null)
                for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _sunderUntil[i]);
        }
    }
}
