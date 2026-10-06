namespace Game.Sim
{
    public sealed partial class Simulation
    {
        public const int PelagBasicResumeTicks = 24;
        public const int PelagBasicPressBufferTicks = 6;
        public static readonly Fix64 PelagBasicAttackRange = Fix64.Ratio(22, 10);
        public static readonly Fix64 PelagBasicAttackArcCos = Fix64.Sqrt(Fix64.Half);

        bool _pelagBasicComboEnabled;
        PelagBasicAttackState _pelagBasicAttack;
        int _pelagBasicNextStage, _pelagBasicResumeUntil = -1;
        int _pelagBasicPressedUntil = -1;
        FixVec2 _pelagBasicPressedAim;

        /// <summary>Кандидат включается только в стенде до художественной приёмки.</summary>
        public bool PelagBasicComboEnabled => _pelagBasicComboEnabled;
        public PelagBasicAttackState PelagBasicAttack => _pelagBasicAttack;

        public void EnablePelagBasicCombo()
        {
            if (_pelagBasicComboEnabled) return;
            _pelagBasicComboEnabled = true;
            ResetPelagBasicCombo();
            if (Entities.Count <= PlayerId) return;
            if (!_explicitMoveOrder) _hasMoveOrder = false;
            Entities.PendingAttackTarget[PlayerId] = -1;
            Entities.AttackImpactTick[PlayerId] = 0;
            Entities.PendingAttackVariant[PlayerId] = 0;
            Entities.NextAttackTick[PlayerId] = Tick;
            Entities.Stats[PlayerId].SetBase(StatType.AttackSpeed, Fix64.FromInt(3));
            Entities.RefreshStats(PlayerId);
        }

        bool PelagBasicWindup => _pelagBasicComboEnabled && _pelagBasicAttack.ActiveAt(Tick)
            && !_pelagBasicAttack.ContactProcessed && Tick < _pelagBasicAttack.ContactTick;
        bool PelagBasicDirectionLocked => _pelagBasicComboEnabled && _pelagBasicAttack.ActiveAt(Tick)
            && !_pelagBasicAttack.ContactProcessed && Tick <= _pelagBasicAttack.ContactTick;

        // Удержание ЛКМ не считается намерением оборвать Вихрь или Крушение.
        bool PelagBasicAbilityBlocking => WhirlwindChanneling || _wreckSlot >= 0 || SquallHoldsHero || AbordageHoldsHero || AnchorThrowHoldsHero
            || _playerAction.ActiveAt(Tick) && _playerAction.DefinitionId == AbilityDefinition.WhirlwindId;

        void UpdatePelagBasicContinuation()
        {
            if (!_pelagBasicComboEnabled) return;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))
            { ResetPelagBasicCombo(preserveSerial: true); return; }
            if (_pelagBasicNextStage != 0 && PelagBasicAbilityBlocking)
                _pelagBasicResumeUntil = Tick + PelagBasicResumeTicks;
            if (_pelagBasicNextStage != 0 && Tick > _pelagBasicResumeUntil
                && !_pelagBasicAttack.ActiveAt(Tick)) _pelagBasicNextStage = 0;
            if (Tick > _pelagBasicPressedUntil) ClearPelagBasicPress();
        }

        /// <summary>Начало до движения: штраф и зафиксированный прицел действуют в первый тик.</summary>
        void PrimePelagBasicAttack(in InputFrame input)
        {
            if (!_pelagBasicComboEnabled || !Entities.Alive[PlayerId]
                || Statuses.IsStunned(PlayerId, Tick)) return;
            if (input.AbilityMask != 0) return;
            if (input.Has(InputFlags.UseArtifact) && Artifact == RunArtifact.VoidVisage
                && Tick >= _artifactReadyTick) return;
            if (input.Has(InputFlags.AttackPressed))
            {
                _pelagBasicPressedAim = input.Aim;
                _pelagBasicPressedUntil = Tick + PelagBasicPressBufferTicks;
            }
            bool pressed = _pelagBasicPressedUntil >= Tick;
            if (!pressed && !input.Has(InputFlags.Attack)) return;
            if (_pelagBasicAttack.ActiveAt(Tick) || Tick < Entities.NextAttackTick[PlayerId]
                || Entities.NextAttackTick[PlayerId] == int.MaxValue
                || !_playerAction.CanChainAt(Tick) || Entities.ForcedTicksLeft[PlayerId] > 0
                || PelagBasicAbilityBlocking || VoidPhased) return;

            FixVec2 aim = pressed ? _pelagBasicPressedAim : input.Aim;
            ClearPelagBasicPress();
            FixVec2 direction = aim - Entities.Position[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            direction = direction.Normalized();
            int stage = _pelagBasicNextStage;
            Fix64 speed = Entities.Stats[PlayerId].Get(StatType.AttackSpeed);
            int windup = CombatStats.PelagBasicPhaseTicks(stage == 2 ? 6 : 4, speed, 2);
            int cycle = System.Math.Max(windup + 2,
                CombatStats.PelagBasicPhaseTicks(stage == 2 ? 12 : 9, speed, 4));
            int serial = _pelagBasicAttack.Serial + 1;
            CancelPlayerAction();
            _pelagBasicAttack = new PelagBasicAttackState { Serial = serial, Stage = stage,
                StartTick = Tick, ContactTick = Tick + windup, EndTick = Tick + cycle,
                Direction = direction };
            _pelagBasicResumeUntil = _pelagBasicAttack.EndTick + PelagBasicResumeTicks;
            SetActionClock(-1, 0, _pelagBasicAttack.ContactTick, _pelagBasicAttack.EndTick);
            Entities.NextAttackTick[PlayerId] = _pelagBasicAttack.EndTick;
            Entities.PendingAttackTarget[PlayerId] = -1;
            Entities.PendingAttackVariant[PlayerId] = stage;
            Entities.AttackImpactTick[PlayerId] = _pelagBasicAttack.ContactTick;
            Entities.Facing[PlayerId] = direction;
            PreparedGiftOrdinaryAttackStarted();
            _events.Add(SimEvent.Attack(PlayerId, -1, Entities.Position[PlayerId], stage, _pelagBasicAttack));
        }

        void ResolvePelagBasicContact()
        {
            if (VoidPhased) { InterruptPelagBasicAttack(); return; }
            if (!_pelagBasicAttack.ActiveAt(Tick) || _pelagBasicAttack.ContactProcessed
                || Tick < _pelagBasicAttack.ContactTick || !Entities.Alive[PlayerId]
                || Statuses.IsStunned(PlayerId, Tick)) return;
            _pelagBasicAttack.ContactProcessed = true;
            _pelagBasicNextStage = (_pelagBasicAttack.Stage + 1) % 3;
            _pelagBasicResumeUntil = _pelagBasicAttack.EndTick + PelagBasicResumeTicks;
            Entities.AttackImpactTick[PlayerId] = 0;
            var sector = EnemyTelegraph.Sector(Entities.Position[PlayerId], _pelagBasicAttack.Direction,
                PelagBasicAttackRange, PelagBasicAttackArcCos);
            Fix64 scale = _pelagBasicAttack.Stage == 2 ? Fix64.Ratio(7, 10) : Fix64.Half;
            bool positiveHit = false;
            for (int target = 1; target < Entities.Count; target++)
            {
                if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                if (!ThicketHitTouches(in sector, target)) continue;
                if (ApplyAttack(PlayerId, target, _pelagBasicAttack.Stage, scale) > 0) positiveHit = true;
            }
            if (_pelagBasicAttack.Stage == 2 && positiveHit) RefundLavidium(10);
        }

        void InterruptPelagBasicAttack()
        {
            if (!_pelagBasicComboEnabled) return;
            ClearPelagBasicPress();
            if (!_pelagBasicAttack.ActiveAt(Tick)) return;
            _pelagBasicAttack.Interrupted = true;
            _pelagBasicResumeUntil = Tick + PelagBasicResumeTicks;
            Entities.AttackImpactTick[PlayerId] = 0;
            Entities.PendingAttackVariant[PlayerId] = 0;
            Entities.NextAttackTick[PlayerId] = Tick;
        }

        void ExtendPelagBasicContinuation(int actionEnd)
        {
            if (_pelagBasicComboEnabled && _pelagBasicNextStage != 0)
                _pelagBasicResumeUntil = System.Math.Max(_pelagBasicResumeUntil, actionEnd + PelagBasicResumeTicks);
        }

        void ClearPelagBasicPress()
        { _pelagBasicPressedUntil = -1; _pelagBasicPressedAim = FixVec2.Zero; }

        void ResetPelagBasicCombo(bool preserveSerial = false)
        {
            int serial = preserveSerial ? _pelagBasicAttack.Serial : 0;
            _pelagBasicAttack = new PelagBasicAttackState { Serial = serial, Interrupted = serial > 0 };
            _pelagBasicNextStage = 0; _pelagBasicResumeUntil = -1;
            ClearPelagBasicPress();
        }

        void HashPelagBasicCombo(ref ulong hash)
        {
            if (!_pelagBasicComboEnabled) return;
            Hashing.Mix(ref hash, 0x50434F4D);
            _pelagBasicAttack.HashInto(ref hash);
            Hashing.Mix(ref hash, _pelagBasicNextStage); Hashing.Mix(ref hash, _pelagBasicResumeUntil);
            Hashing.Mix(ref hash, _pelagBasicPressedUntil);
            Hashing.Mix(ref hash, _pelagBasicPressedAim.X); Hashing.Mix(ref hash, _pelagBasicPressedAim.Y);
        }
    }
}
