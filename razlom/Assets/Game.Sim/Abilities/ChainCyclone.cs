namespace Game.Sim
{
    public sealed partial class Simulation
    {
        private int _cycloneSlot = -1;
        private int _cycloneStart;
        private Fix64 _cycloneTravel;
        private Fix64 _cycloneStartAngle;
        private Fix64 _cycloneRadius;
        private int[] _cycloneHitTurn;
        private FixVec2[] _cycloneTickPositions;
        private readonly FixVec2[] _cycloneSampleEnds = new FixVec2[17];
        private readonly int[] _cycloneSampleTurns = new int[17];

        /// <summary>Проверка цели до прерывания текущего действия и расхода кулдауна.</summary>
        public bool ValidAbilityTarget(int target, AbilityBuild build)
            => build != null && target > 0 && target < Entities.Count && Entities.Alive[target]
                && Entities.Side[target] != Entities.Side[PlayerId]
                && (Entities.Position[target] - Entities.Position[PlayerId]).LengthSq
                    <= build.Get(AbilityStatType.Radius) * build.Get(AbilityStatType.Radius);

        /// <summary>Авторитетное состояние оружия для представления.</summary>
        public bool CycloneActive => _cycloneSlot >= 0;
        public Fix64 CycloneAngle => _cycloneStartAngle + _cycloneTravel;
        public Fix64 CycloneTravel => _cycloneTravel;
        public Fix64 CycloneRadius => _cycloneRadius;
        public int CycloneElapsedTicks => CycloneActive ? Tick - _cycloneStart : 0;

        private Fix64 CycloneProgress(AbilityBuild build)
            => Fix64.Clamp(Fix64.Ratio(Tick - _cycloneStart,
                System.Math.Max(1, build.Get(AbilityStatType.DurationTicks).ToInt() - 1)), Fix64.Zero, Fix64.One);

        private bool TryCycloneMoveScale(in InputFrame input, out Fix64 scale)
        {
            AbilityBuild build = CycloneActive && (input.AbilityHoldMask & (1 << _cycloneSlot)) != 0
                ? _abilityBuilds[_cycloneSlot] : null;
            bool fresh = false;
            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                var candidate = _abilityBuilds[slot];
                if (!input.Ability(slot) || candidate == null || Tick < _abilityReadyTick[slot]) continue;
                if (candidate.DefinitionId == AbilityDefinition.ChainStepId && !ValidAbilityTarget(input.AbilityTarget, candidate)) continue;
                build = candidate.DefinitionId == AbilityDefinition.ChainCycloneId
                    && (input.AbilityHoldMask & (1 << slot)) != 0 ? candidate : null;
                fresh = true;
            }
            scale = Fix64.One;
            if (build == null || (!fresh && Tick - _cycloneStart >= build.Get(AbilityStatType.DurationTicks).ToInt())) return false;
            Fix64 start = build.Get(AbilityStatType.StartMoveMultiplier);
            scale = fresh ? start : start + (build.Get(AbilityStatType.EndMoveMultiplier) - start) * CycloneProgress(build);
            return true;
        }

        private void BeginCyclone(int slot, FixVec2 aim)
        {
            _cycloneSlot = slot;
            _cycloneStart = Tick;
            _cycloneTravel = Fix64.Zero;
            FixVec2 direction = aim - Entities.Position[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            _cycloneStartAngle = Fix64.Atan2(direction.Y, direction.X);
            _cycloneRadius = _abilityBuilds[slot].Get(AbilityStatType.MinimumRadius);
            for (int i = 0; i < _cycloneHitTurn.Length; i++) _cycloneHitTurn[i] = -1;
        }

        private void StopCyclone()
        {
            _cycloneSlot = -1;
            _cycloneStart = 0;
            _cycloneTravel = _cycloneStartAngle = _cycloneRadius = Fix64.Zero;
            System.Array.Clear(_cycloneHitTurn, 0, _cycloneHitTurn.Length);
        }

        private void UpdateCyclone(in InputFrame input)
        {
            if (!CycloneActive) return;
            AbilityBuild build = _abilityBuilds[_cycloneSlot];
            if (!Entities.Alive[PlayerId] || build == null
                || (input.AbilityHoldMask & (1 << _cycloneSlot)) == 0
                || Tick - _cycloneStart >= build.Get(AbilityStatType.DurationTicks).ToInt())
            { StopCyclone(); return; }

            Fix64 progress = CycloneProgress(build);
            Fix64 radius = build.Get(AbilityStatType.MinimumRadius)
                + (build.Get(AbilityStatType.Radius) - build.Get(AbilityStatType.MinimumRadius)) * progress;
            Fix64 turns = build.Get(AbilityStatType.StartTurnsPerSecond)
                + (build.Get(AbilityStatType.EndTurnsPerSecond) - build.Get(AbilityStatType.StartTurnsPerSecond)) * progress;
            Fix64 travel = _cycloneTravel + turns * Fix64.TwoPi / Fix64.FromInt(TicksPerSecond);
            FixVec2 centre = Entities.Position[PlayerId];
            // Подшаги покрывают дугу между тиками; допуск включает максимальную
            // ошибку аппроксимации, чтобы тонкая цепь не проходила сквозь цель.
            const int samples = 16;
            Fix64 margin = radius * (travel - _cycloneTravel) / Fix64.FromInt(samples * 2);
            for (int s = 0; s <= samples; s++)
            {
                Fix64 t = Fix64.Ratio(s, samples);
                Fix64 angleTravel = _cycloneTravel + (travel - _cycloneTravel) * t;
                Fix64 angle = _cycloneStartAngle + angleTravel;
                _cycloneSampleTurns[s] = (angleTravel / Fix64.TwoPi).ToInt();
                _cycloneSampleEnds[s] = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle))
                    * (_cycloneRadius + (radius - _cycloneRadius) * t);
            }
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                FixVec2 delta = Entities.Position[i] - centre;
                FixVec2 previousDelta = Tick == _cycloneStart ? delta : _cycloneTickPositions[i] - _cycloneTickPositions[PlayerId];
                Fix64 width = Entities.BodyRadius[i] + build.Get(AbilityStatType.WeaponRadius) + margin
                    + (delta - previousDelta).Length / Fix64.FromInt(samples * 2);
                for (int s = 0; s <= samples; s++)
                {
                    Fix64 t = Fix64.Ratio(s, samples);
                    int turn = _cycloneSampleTurns[s];
                    if (_cycloneHitTurn[i] == turn) continue;
                    FixVec2 end = _cycloneSampleEnds[s];
                    FixVec2 sampleDelta = previousDelta + (delta - previousDelta) * t;
                    Fix64 projection = end.LengthSq.Raw == 0 ? Fix64.Zero
                        : Fix64.Clamp(FixVec2.Dot(sampleDelta, end) / end.LengthSq, Fix64.Zero, Fix64.One);
                    if ((sampleDelta - end * projection).LengthSq > width * width) continue;
                    _cycloneHitTurn[i] = turn;
                    ApplyAbilityDamage(PlayerId, i, build.Get(AbilityStatType.Damage).ToInt(), _cycloneSlot, DamageType.Physical);
                    if (!Entities.Alive[i]) break;
                }
            }
            _cycloneTravel = travel;
            _cycloneRadius = radius;
        }

        private void HashCyclone(ref ulong hash)
        {
            Hashing.Mix(ref hash, _cycloneSlot);
            Hashing.Mix(ref hash, _cycloneStart);
            Hashing.Mix(ref hash, _cycloneTravel);
            Hashing.Mix(ref hash, _cycloneStartAngle);
            Hashing.Mix(ref hash, _cycloneRadius);
            for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _cycloneHitTurn[i]);
        }
    }
}
