namespace Game.Sim
{
    public sealed partial class Simulation
    {
        private int _slamSlot = -1;
        private int _slamImpactTick = -1;
        private int _slamEndTick = -1;
        private FixVec2 _slamOrigin, _slamDirection;

        public bool AnchorSlamActive => _slamSlot >= 0 && Entities.Alive[PlayerId] && Tick < _slamEndTick;
        public FixVec2 AnchorSlamOrigin => _slamOrigin;
        public FixVec2 AnchorSlamDirection => _slamDirection;
        public int AnchorSlamImpactTick => _slamImpactTick;
        public int AnchorSlamEndTick => _slamEndTick;

        private void StopAnchorSlam()
        {
            _slamSlot = _slamImpactTick = _slamEndTick = -1;
            _slamOrigin = _slamDirection = FixVec2.Zero;
        }

        private void BeginAnchorSlam(int slot, FixVec2 aim)
        {
            var build = _abilityBuilds[slot];
            _slamSlot = slot;
            _slamOrigin = Entities.Position[PlayerId];
            FixVec2 direction = aim - _slamOrigin;
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            _slamDirection = direction.Normalized();
            _slamImpactTick = Tick + AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt());
            _slamEndTick = System.Math.Max(_slamImpactTick + 1, Tick + AbilityExecutionTicks(build.Get(AbilityStatType.DurationTicks).ToInt()));
        }

        private void UpdateAnchorSlam()
        {
            if (_slamSlot < 0) return;
            var build = _abilityBuilds[_slamSlot];
            if (!AnchorSlamActive || build == null || build.DefinitionId != AbilityDefinition.AnchorSlamId)
            { StopAnchorSlam(); return; }
            if (_slamImpactTick < 0 || Tick < _slamImpactTick) return;
            _slamImpactTick = -1;
            Fix64 length = build.Get(AbilityStatType.Radius);
            Fix64 halfWidth = build.Get(AbilityStatType.Width) / Fix64.FromInt(2);
            int stunTicks = build.Get(AbilityStatType.StunTicks).ToInt();
            // Контакт существует и при промахе: будущий VFX не зависит от наличия жертвы.
            _events.Add(new SimEvent(SimEventType.AnchorSlamImpact, PlayerId, -1, _slamSlot,
                false, _slamOrigin + _slamDirection * length));
            // Талант «Три направления» добавляет две полосы под ±30°. Цель, задетая
            // несколькими полосами, получает удар один раз.
            int lanes = build.Has(AbilityFlag.AnchorSlamThreeWays) ? 3 : 1;
            bool stunnedBonus = build.Has(AbilityFlag.AnchorSlamStunnedBonus);
            int damage = build.Get(AbilityStatType.Damage).ToInt();
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                bool inside = false;
                for (int lane = 0; lane < lanes && !inside; lane++)
                    inside = InsideSlamLane(i, SlamLaneDirection(lane), length, halfWidth);
                if (!inside) continue;
                // «Добить оглушённого» смотрит на стан ДО удара: собственный стан
                // этого же удара бонуса не даёт.
                int hit = stunnedBonus && Statuses.IsStunned(i, Tick) ? damage * 150 / 100 : damage;
                ApplyAbilityDamage(PlayerId, i, hit, _slamSlot, DamageType.Physical);
                if (!Entities.Alive[i] || stunTicks <= 0) continue;
                Statuses.ApplyStun(i, Tick + stunTicks);
                Entities.Velocity[i] = FixVec2.Zero;
                Entities.PendingAttackTarget[i] = -1;
                Entities.AttackImpactTick[i] = 0;
                Entities.PendingAttackVariant[i] = 0;
                _events.Add(new SimEvent(SimEventType.Stun, PlayerId, i, stunTicks, false, Entities.Position[i]));
            }
        }

        private static readonly Fix64 SlamSideCos = Fix64.Ratio(8660, 10000);   // cos 30°
        private static readonly Fix64 SlamSideSin = Fix64.Ratio(1, 2);          // sin 30°

        /// <summary>Полоса удара: 0 — прямо, 1 и 2 — ±30° от направления удара.</summary>
        private FixVec2 SlamLaneDirection(int lane)
        {
            FixVec2 v = _slamDirection;
            if (lane == 0) return v;
            Fix64 s = lane == 1 ? SlamSideSin : Fix64.Zero - SlamSideSin;
            return new FixVec2(v.X * SlamSideCos - v.Y * s, v.X * s + v.Y * SlamSideCos);
        }

        /// <summary>Пересечение тела с прямоугольником сохраняет одинаковую ширину по всей длине.</summary>
        private bool InsideSlamLane(int entity, FixVec2 direction, Fix64 length, Fix64 halfWidth)
        {
            FixVec2 delta = Entities.Position[entity] - _slamOrigin;
            Fix64 along = FixVec2.Dot(delta, direction);
            Fix64 across = Fix64.Abs(delta.X * direction.Y - delta.Y * direction.X);
            Fix64 dx = along - Fix64.Clamp(along, Fix64.Zero, length);
            Fix64 dy = across > halfWidth ? across - halfWidth : Fix64.Zero;
            return dx * dx + dy * dy <= Entities.BodyRadius[entity] * Entities.BodyRadius[entity];
        }

        private void HashAnchorSlam(ref ulong hash)
        {
            Hashing.Mix(ref hash, _slamSlot);
            Hashing.Mix(ref hash, _slamImpactTick);
            Hashing.Mix(ref hash, _slamEndTick);
            Hashing.Mix(ref hash, _slamOrigin.X);
            Hashing.Mix(ref hash, _slamOrigin.Y);
            Hashing.Mix(ref hash, _slamDirection.X);
            Hashing.Mix(ref hash, _slamDirection.Y);
        }
    }
}
