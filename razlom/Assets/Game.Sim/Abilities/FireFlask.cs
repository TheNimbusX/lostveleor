namespace Game.Sim
{
    /// <summary>
    /// ВЗРЫВНАЯ СМЕСЬ. Бутылка разбивается о землю и оставляет горящую лужу.
    ///
    /// ЛУЖА — ЭТО И ЕСТЬ СПОСОБНОСТЬ, а не украшение взрыва. Она единственная
    /// в наборе Пелага удерживает урон НА МЕСТЕ, а не на теле: якорный герой
    /// долго стоит в замахе, и площадка, которая продолжает бить, пока он
    /// заносит якорь, — это ровно та валюта, которой ему не хватает.
    ///
    /// Луж может гореть несколько. Стоящий в двух получает урон от обеих:
    /// диздок оставляет их взаимодействие открытым, и складывать урон —
    /// единственный вариант, который ничего не отнимает у игрока, пока
    /// правило не написано. Запрет, поставленный наугад, потом придётся
    /// снимать, а разрешение — нет.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Сколько луж горит одновременно. Четырёх хватает: перезарядка
        /// способности длиннее, чем живёт лужа, и упереться в предел можно
        /// только зарядами из талантов, которых ещё нет.
        /// </summary>
        private const int MaxFirePools = 4;

        private readonly FixVec2[] _poolAt = new FixVec2[MaxFirePools];
        private readonly int[] _poolUntilTick = new int[MaxFirePools];
        private readonly int[] _poolNextTick = new int[MaxFirePools];
        private readonly int[] _poolSlot = new int[MaxFirePools];
        private readonly Fix64[] _poolRadius = new Fix64[MaxFirePools];
        private readonly int[] _poolDamage = new int[MaxFirePools];
        private readonly int[] _poolPeriod = new int[MaxFirePools];

        private int _flaskSlot = -1;
        private int _flaskLandTick = -1;
        private FixVec2 _flaskTarget;

        /// <summary>Летит ли бутылка прямо сейчас. Показу — рисовать её дугу.</summary>
        public bool FlaskInFlight => _flaskSlot >= 0;
        public FixVec2 FlaskTarget => _flaskTarget;

        /// <summary>Горит ли лужа в этой ячейке. Нужно показу и тестам.</summary>
        public bool FirePoolActive(int index)
            => (uint)index < MaxFirePools && Tick < _poolUntilTick[index];

        public FixVec2 FirePoolAt(int index) => _poolAt[index];
        public Fix64 FirePoolRadius(int index) => _poolRadius[index];

        private void StopFlask()
        {
            _flaskSlot = _flaskLandTick = -1;
            _flaskTarget = FixVec2.Zero;
        }

        private void ClearFirePools()
        {
            for (int i = 0; i < MaxFirePools; i++)
            {
                _poolUntilTick[i] = 0;
                _poolNextTick[i] = 0;
                _poolSlot[i] = -1;
                _poolDamage[i] = 0;
                _poolPeriod[i] = 0;
                _poolRadius[i] = Fix64.Zero;
                _poolAt[i] = FixVec2.Zero;
            }
        }

        /// <summary>
        /// Бросок. Точка за пределом дальности не отменяет бросок, а укорачивает
        /// его — игрок целится примерно, и отказ вместо броска читается как
        /// пропавшее нажатие.
        ///
        /// Время полёта считается от расстояния, а не берётся постоянным: иначе
        /// бутылка, брошенная под ноги, летела бы столько же, сколько брошенная
        /// на всю дальность.
        /// </summary>
        private void BeginFlask(int slot, FixVec2 aim)
        {
            AbilityBuild build = _abilityBuilds[slot];
            FixVec2 from = Entities.Position[PlayerId];

            FixVec2 delta = aim - from;
            Fix64 distance = delta.Length;
            Fix64 range = build.Get(AbilityStatType.Radius);

            if (distance.Raw == 0)
            {
                FixVec2 facing = Entities.Facing[PlayerId];
                if (facing.LengthSq.Raw == 0) facing = new FixVec2(Fix64.One, Fix64.Zero);
                delta = facing;
                distance = Fix64.One;
            }

            FixVec2 direction = delta / distance;
            Fix64 reach = distance > range ? range : distance;

            Entities.Facing[PlayerId] = direction;
            _flaskTarget = from + direction * reach;
            _flaskSlot = slot;

            Fix64 speed = build.Get(AbilityStatType.ProjectileSpeed);
            int flight = speed.Raw > 0 ? (reach / speed).ToInt() : 1;
            _flaskLandTick = Tick + (flight < 1 ? 1 : flight);
        }

        private void UpdateFlask()
        {
            UpdateFirePools();

            if (_flaskSlot < 0) return;

            AbilityBuild build = _abilityBuilds[_flaskSlot];
            if (build == null || build.DefinitionId != AbilityDefinition.FireFlaskId)
            { StopFlask(); return; }

            if (Tick < _flaskLandTick) return;

            int slot = _flaskSlot;
            Fix64 radius = build.Get(AbilityStatType.Width) / Fix64.FromInt(2);
            int blast = build.Get(AbilityStatType.Damage).ToInt();

            _events.Add(new SimEvent(SimEventType.FlaskBurst, PlayerId, -1, slot,
                false, _flaskTarget, DamageType.Fire));

            // Взрыв бьёт сразу, лужа — потом. Разделено намеренно: без удара в
            // момент попадания способность не даёт никакой обратной связи в тот
            // единственный кадр, когда игрок на неё смотрит.
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;

                Fix64 gap = (Entities.Position[i] - _flaskTarget).LengthSq;
                Fix64 limit = radius + Entities.BodyRadius[i];
                if (gap > limit * limit) continue;

                ApplyAbilityDamage(PlayerId, i, blast, slot, DamageType.Fire);
            }

            LightFirePool(slot, _flaskTarget, radius, build);
            StopFlask();
        }

        private void LightFirePool(int slot, FixVec2 at, Fix64 radius, AbilityBuild build)
        {
            int duration = build.Get(AbilityStatType.DurationTicks).ToInt();
            int period = build.Get(AbilityStatType.BurnTicks).ToInt();
            if (period < 1) period = 1;

            // Свободная ячейка, а если все заняты — самая старая. Отказать в
            // поджиге хуже, чем погасить лужу, которой и так осталось меньше
            // всех: игрок нажал кнопку и обязан увидеть результат.
            int index = 0;
            int oldest = int.MaxValue;
            for (int i = 0; i < MaxFirePools; i++)
            {
                if (Tick >= _poolUntilTick[i]) { index = i; break; }
                if (_poolUntilTick[i] >= oldest) continue;
                oldest = _poolUntilTick[i];
                index = i;
            }

            _poolAt[index] = at;
            _poolRadius[index] = radius;
            _poolUntilTick[index] = Tick + duration;
            _poolNextTick[index] = Tick + period;
            _poolPeriod[index] = period;
            _poolSlot[index] = slot;
            _poolDamage[index] = build.Get(AbilityStatType.BurnDamagePercent).ToInt();
        }

        private void UpdateFirePools()
        {
            for (int i = 0; i < MaxFirePools; i++)
            {
                if (Tick >= _poolUntilTick[i]) continue;
                if (Tick < _poolNextTick[i]) continue;

                _poolNextTick[i] = Tick + _poolPeriod[i];

                for (int e = 1; e < Entities.Count; e++)
                {
                    if (!Entities.Alive[e] || Entities.Side[e] == Entities.Side[PlayerId]) continue;

                    Fix64 gap = (Entities.Position[e] - _poolAt[i]).LengthSq;
                    Fix64 limit = _poolRadius[i] + Entities.BodyRadius[e];
                    if (gap > limit * limit) continue;

                    // Урон по времени, а не удар: тик лужи не имеет права
                    // звучать и трясти экран как попадание.
                    ApplyAbilityDamage(PlayerId, e, _poolDamage[i], _poolSlot[i],
                        DamageType.Fire, overTime: true);
                }
            }
        }

        private void HashFlask(ref ulong hash)
        {
            Hashing.Mix(ref hash, _flaskSlot);
            Hashing.Mix(ref hash, _flaskLandTick);
            Hashing.Mix(ref hash, _flaskTarget.X);
            Hashing.Mix(ref hash, _flaskTarget.Y);

            for (int i = 0; i < MaxFirePools; i++)
            {
                Hashing.Mix(ref hash, _poolUntilTick[i]);
                Hashing.Mix(ref hash, _poolNextTick[i]);
                Hashing.Mix(ref hash, _poolDamage[i]);
                Hashing.Mix(ref hash, _poolAt[i].X);
                Hashing.Mix(ref hash, _poolAt[i].Y);
            }
        }
    }
}
