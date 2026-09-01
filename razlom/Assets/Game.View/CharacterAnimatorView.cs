using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Presentation-мост к Animator. Он только показывает уже принятое Sim решение
    /// и никогда не вызывает damage, hit detection или движение сущности.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimatorView : MonoBehaviour
    {
        private static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int TurnDirection = Animator.StringToHash("TurnDirection");
        private static readonly int Relaxed = Animator.StringToHash("Relaxed");
        private static readonly int Stunned = Animator.StringToHash("Stunned");
        private static readonly int LocomotionPlaybackSpeed = Animator.StringToHash("LocomotionPlaybackSpeed");
        private static readonly int AttackPlaybackSpeed = Animator.StringToHash("AttackPlaybackSpeed");
        private static readonly int UpperBodyEmptyState = Animator.StringToHash("UpperBody_Empty");
        private static readonly int LowerBodyEmptyState = Animator.StringToHash("LowerBody_Empty");
        private static readonly int UpperBodyAttackAState =
            Animator.StringToHash("UpperBody Combat.Saber_A_v5");
        private static readonly int UpperBodyAttackBState =
            Animator.StringToHash("UpperBody Combat.Saber_B_v5");
        private static readonly int LowerBodyAttackAState =
            Animator.StringToHash("LowerBody Combat.Lower_Saber_A_v5");
        private static readonly int LowerBodyAttackBState =
            Animator.StringToHash("LowerBody Combat.Lower_Saber_B_v5");
        private static readonly int OrvillSwordAttack = Animator.StringToHash("SwordAttack");
        private static readonly int OrvillShieldBash = Animator.StringToHash("ShieldBash");
        private static readonly int OrvillHighBlock = Animator.StringToHash("HighBlock");
        private static readonly int OrvillGuardBreak = Animator.StringToHash("GuardBreak");
        private static readonly int OrvillHitLeft = Animator.StringToHash("HitLeft");
        private static readonly int OrvillHitRight = Animator.StringToHash("HitRight");
        private static readonly int OrvillKnockback = Animator.StringToHash("Knockback");
        private static readonly int OrvillDeath = Animator.StringToHash("Death");
        private static readonly int OrvillLocomotionState =
            Animator.StringToHash("Base Layer.Locomotion");
        private static readonly int OrvillDeathState = Animator.StringToHash("Base Layer.DeathBack");
        private const string UpperBodyLayerName = "UpperBody Combat";
        private const string LowerBodyLayerName = "LowerBody Combat";

        // Simulation owns the contact tick. Derive presentation timings from
        // that value so an animation cannot silently drift from Damage.
        //
        // ЗАМАХ РАЗНЫЙ У ГЕРОЯ И У ВРАГА, поэтому и контакт разный. Общая
        // константа здесь означала бы, что анимация врага бьёт на 0.2 с, а урон
        // приходит на 0.4 — то есть клинок проходит сквозь цель за две десятых
        // до того, как что-то произойдёт. Ровно та рассинхронизация, ради
        // устранения которой замах и разносили с контактом.
        private static readonly float PlayerAttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        private static readonly float EnemyAttackContactTime =
            Simulation.EnemyAttackWindupTicks / (float)Simulation.TicksPerSecond;
        private const float AttackAnticipationEnd = 0.12f;
        private const float AttackContactEndOffset = 0.10f;
        private const float AttackRecoveryEndOffset = 0.32f;
        private const float AttackContactHoldDuration = 0.055f;
        private const float AttackAnticipationSpeed = 0.76f;
        private const float AttackAccelerationSpeed = 1.82f;
        private const float AttackContactSpeed = 0.76f;
        private const float AttackRecoverySpeed = 1.24f;
        // Contact is authored at different normalized phases because the two
        // combo clips have very different source lengths (49 and 97 frames).
        // These values match the deterministic 12/30 s contact after the
        // per-phase playback warp above.
        private const float AttackAContactNormalized = 0.37f;
        private const float AttackBContactNormalized = 0.225f;
        // The authored spin reaches its readable recovery pose at normalized
        // 0.76. The controller then blends out for 0.18 s; keep the lower-body
        // presentation alive through that blend so it cannot drop to idle a
        // second time underneath the upper-body recovery.
        private const float WhirlwindPresentationDuration = 0.94f;

        // Imported Orvill clips have authored contact poses on frames 17 and
        // 20. Per-clip playback keeps either pose on the deterministic 12/30 s
        // Sim contact without moving damage authority into presentation.
        private const float OrvillSwordAuthoredContactTime = 17f / 30f;
        private const float OrvillSwordAuthoredDuration = 33f / 30f;
        private const float OrvillShieldAuthoredContactTime = 20f / 30f;
        private const float OrvillShieldAuthoredDuration = 42f / 30f;
        private const float OrvillHitPresentationDuration = 0.57f;
        // GuardWalk covers about 0.387 m over a 0.20 s planted-foot phase:
        // 1.93 m/s at 1x versus Orvill's deterministic 3.5 m/s full speed.
        private const float OrvillLocomotionBasePlaybackSpeed = 1.81f;
        private const float OrvillLocomotionMinPlaybackSpeed = 0.42f;
        // Клипы Орвилла подгоняются под ЕГО контакт, а не под геройский.
        private static readonly float OrvillSwordPlaybackSpeed =
            OrvillSwordAuthoredContactTime / EnemyAttackContactTime;
        private static readonly float OrvillSwordPresentationDuration =
            OrvillSwordAuthoredDuration / OrvillSwordPlaybackSpeed;
        private static readonly float OrvillShieldPlaybackSpeed =
            OrvillShieldAuthoredContactTime / EnemyAttackContactTime;
        private static readonly float OrvillShieldPresentationDuration =
            OrvillShieldAuthoredDuration / OrvillShieldPlaybackSpeed;

        private Animator _animator;
        private SpriteCharacterVisual _spriteVisual;
        private Faction _faction;

        /// <summary>Контакт той стороны, которую показывает этот вид.</summary>
        private float AttackContactTime => _faction == Faction.Wole
            ? PlayerAttackContactTime
            : EnemyAttackContactTime;
        private int _attackVariant;
        private int _orvillAttackCount;
        private float _actionProtectedUntil;
        private float _lastHitAt = -10f;
        private float _attackWarpStartedAt;
        private bool _attackWarpActive;
        private float _attackContactHoldUntil;
        private bool _attackPresentationActive;
        private float _attackPresentationUntil;
        private bool _abilityPresentationActive;
        private float _abilityPresentationUntil;
        private int _upperBodyLayer = -1;
        private int _lowerBodyLayer = -1;
        private bool _locomotionMoving;
        private float _orvillLocomotionPlaybackSpeed = 1f;
        private float _orvillHitPresentationUntil;

        public float DeathDuration => _faction == Faction.Wole ? 1.55f : 1.65f;
        public bool IsDead { get; private set; }
        public bool UsesSprites => _spriteVisual != null;

        public void Configure(Faction faction)
        {
            _faction = faction;
            _spriteVisual = GetComponentInChildren<SpriteCharacterVisual>(true);
            _animator = GetComponent<Animator>();
            if (_spriteVisual != null) return;
            if (_animator == null)
                Debug.LogError($"[Разлом] У prefab {name} отсутствует Animator.", this);
            else
            {
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _upperBodyLayer = _animator.GetLayerIndex(UpperBodyLayerName);
                _lowerBodyLayer = _animator.GetLayerIndex(LowerBodyLayerName);
            }
        }

        public void ResetForSpawn()
        {
            PoolTag poolTag = GetComponent<PoolTag>();
            ResetForSpawn(poolTag != null ? poolTag.Slot : 0);
        }

        public void ResetForSpawn(int presentationId)
        {
            IsDead = false;
            _attackVariant = 0;
            _orvillAttackCount = 0;
            _actionProtectedUntil = 0f;
            _lastHitAt = -10f;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _attackContactHoldUntil = 0f;
            _locomotionMoving = false;
            _orvillLocomotionPlaybackSpeed = 1f;
            _orvillHitPresentationUntil = 0f;
            StopAttackWarp();
            if (_spriteVisual != null)
            {
                _spriteVisual.ResetForSpawn();
                return;
            }
            if (_animator == null) return;
            _animator.Rebind();
            _animator.Update(0f);
            _animator.speed = 1f;
            if (_faction == Faction.Wole) _animator.SetBool(Relaxed, false);
            _animator.SetBool(Stunned, false);
            _animator.SetFloat(MoveSpeed, 0f);
            if (_faction == Faction.Wole)
            {
                _animator.SetFloat(TurnDirection, 0f);
                _animator.SetFloat(LocomotionPlaybackSpeed, 1f);
                _animator.SetFloat(AttackPlaybackSpeed, 1f);
                if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            }
            else if (_animator.HasState(0, OrvillLocomotionState))
            {
                _animator.Play(
                    OrvillLocomotionState, 0,
                    DeterministicLocomotionPhase(presentationId));
                _animator.Update(0f);
            }
        }

        private void Update()
        {
            if (_abilityPresentationActive && Time.time >= _abilityPresentationUntil)
                _abilityPresentationActive = false;
            bool attackPresentationEnded = false;
            if (_attackPresentationActive && Time.time >= _attackPresentationUntil)
            {
                _attackPresentationActive = false;
                attackPresentationEnded = true;
            }

            bool orvillHitPresentationEnded = false;
            if (_orvillHitPresentationUntil > 0f && Time.time >= _orvillHitPresentationUntil)
            {
                _orvillHitPresentationUntil = 0f;
                orvillHitPresentationEnded = true;
            }

            if (_animator == null) return;
            if (_faction == Faction.Orvill
                && (attackPresentationEnded || orvillHitPresentationEnded))
                RestoreOrvillLocomotionPlayback();
            if (_faction == Faction.Wole && _lowerBodyLayer >= 0)
            {
                // Warp ends at the timed contact recovery, before the authored
                // follow-through ends. Using it as the leg-layer lifetime made
                // the end of combo B teleport to idle in roughly 55 ms.
                bool actionActive = _attackPresentationActive || _abilityPresentationActive;
                float target = actionActive && !_locomotionMoving ? 1f : 0f;
                float weight = Mathf.MoveTowards(
                    _animator.GetLayerWeight(_lowerBodyLayer), target,
                    Time.deltaTime / 0.16f);
                _animator.SetLayerWeight(_lowerBodyLayer, weight);
            }

            if (_attackContactHoldUntil > 0f)
            {
                if (Time.unscaledTime < _attackContactHoldUntil)
                {
                    _animator.SetFloat(AttackPlaybackSpeed, 0.035f);
                    return;
                }

                _attackContactHoldUntil = 0f;
            }

            if (!_attackWarpActive) return;

            // Scaled time lets contact hit-stop pause this visual curve too.
            float elapsed = Time.time - _attackWarpStartedAt;
            if (elapsed < AttackAnticipationEnd)
                _animator.SetFloat(AttackPlaybackSpeed, AttackAnticipationSpeed);
            else if (elapsed < AttackContactTime)
                _animator.SetFloat(AttackPlaybackSpeed, AttackAccelerationSpeed);
            else if (elapsed < AttackContactTime + AttackContactEndOffset)
                _animator.SetFloat(AttackPlaybackSpeed, AttackContactSpeed);
            else if (elapsed < AttackContactTime + AttackRecoveryEndOffset)
                _animator.SetFloat(AttackPlaybackSpeed, AttackRecoverySpeed);
            else
                StopAttackWarp();
        }

        public void SetLocomotion(bool moving, float turnDirection, float normalizedSpeed)
        {
            if (_spriteVisual != null)
            {
                _spriteVisual.SetMoving(moving);
                return;
            }
            if (_animator == null || IsDead) return;
            _locomotionMoving = moving;
            // Начало шага слегка сглажено, остановка — нет. Симуляция уже
            // тормозит тело сама; второй damp после нулевой скорости и создавал
            // видимое «ноги ещё бегут, хотя персонаж уже приехал».
            if (moving)
                _animator.SetFloat(MoveSpeed, 1f, 0.04f, Time.deltaTime);
            else
                _animator.SetFloat(MoveSpeed, 0f);
            // TurnDirection is a Pelag-only presentation layer. The Orvill
            // controller intentionally has no matching parameter.
            if (_faction == Faction.Wole)
            {
                // The base run state follows the real deterministic body speed.
                // A small floor avoids a frozen first pose during acceleration;
                // stopping still snaps MoveSpeed to zero above.
                float playback = moving ? Mathf.Clamp(normalizedSpeed, 0.42f, 1f) : 1f;
                _animator.SetFloat(LocomotionPlaybackSpeed, playback, 0.045f, Time.deltaTime);
                _animator.SetFloat(TurnDirection,
                    moving ? 0f : Mathf.Clamp(turnDirection, -1f, 1f));
            }
            else
            {
                // Orvill's controller has no playback-speed parameter. Limit
                // Animator.speed to this faction so Pelag's layered warps are
                // untouched, and follow the actual deterministic velocity.
                _orvillLocomotionPlaybackSpeed = moving
                    ? OrvillLocomotionBasePlaybackSpeed
                      * Mathf.Clamp(normalizedSpeed, OrvillLocomotionMinPlaybackSpeed, 1f)
                    : 1f;
                RestoreOrvillLocomotionPlayback();
            }
        }

        public void PlayAttack(int authoritativeVariant = -1)
        {
            int variant = authoritativeVariant >= 0 ? authoritativeVariant : _attackVariant;
            _attackVariant = variant + 1;
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayAttack(variant);
                return;
            }
            if (_animator == null || IsDead) return;
            if (_faction == Faction.Wole)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                // A newly committed basic attack is allowed to replace the
                // Whirlwind recovery immediately. Leaving the ability triggers
                // queued made AnyState wait behind the old exit transition,
                // while Sim had already started the next windup.
                _animator.ResetTrigger("Hook");
                _animator.ResetTrigger("HeavyAttack");
                _animator.ResetTrigger("LowerHeavyAttack");
                // В исходной delivery два настоящих удара; frames 99–147 —
                // recovery второго, а не третий finisher. Проигрываем честную
                // связку A/B и оставляем recovery внутри B.
                bool secondStrike = (variant & 1) != 0;
                string trigger = secondStrike ? "AttackB" : "AttackA";
                string lowerTrigger = trigger == "AttackA" ? "LowerAttackA" : "LowerAttackB";
                _animator.ResetTrigger("AttackA");
                _animator.ResetTrigger("AttackB");
                _animator.ResetTrigger("LowerAttackA");
                _animator.ResetTrigger("LowerAttackB");
                // Attack is already committed by Simulation. Enter the authored
                // states directly instead of asking AnyState through a one-shot
                // trigger: a trigger raised while the layer was blending out of
                // Whirlwind/previous combo could be consumed without entering
                // the requested state. Damage would then still resolve twelve
                // ticks later with no visible swing.
                //
                // Keep trigger fallback for an older/misbuilt controller, but
                // the production controller uses deterministic direct entry.
                int upperState = secondStrike ? UpperBodyAttackBState : UpperBodyAttackAState;
                int lowerState = secondStrike ? LowerBodyAttackBState : LowerBodyAttackAState;
                bool upperEntered = EnterCommittedAttackState(_upperBodyLayer, upperState);
                bool lowerEntered = EnterCommittedAttackState(_lowerBodyLayer, lowerState);
                if (!upperEntered) _animator.SetTrigger(trigger);
                if (!lowerEntered) _animator.SetTrigger(lowerTrigger);
                StartAttackWarp();
                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + (secondStrike ? 0.98f : 0.78f);
                // Слабые входящие попадания всё ещё получают recoil/flash в
                // ArenaView, но не имеют права ломать читаемую фазу клинка.
                _actionProtectedUntil = Time.time + 0.76f;
            }
            else
            {
                bool shieldBash = _orvillAttackCount++ % 3 == 2;
                float playbackSpeed = shieldBash
                    ? OrvillShieldPlaybackSpeed
                    : OrvillSwordPlaybackSpeed;
                float presentationDuration = shieldBash
                    ? OrvillShieldPresentationDuration
                    : OrvillSwordPresentationDuration;

                // Attack has priority over a presentation-only hit reaction:
                // Sim has already committed this action and will resolve its
                // Damage at AttackContactTime even if the view is struck.
                ResetOrvillActionTriggers();
                _orvillHitPresentationUntil = 0f;
                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + presentationDuration;
                _actionProtectedUntil = Time.time + AttackContactTime;
                _animator.speed = playbackSpeed;
                _animator.SetTrigger(shieldBash ? OrvillShieldBash : OrvillSwordAttack);
            }
        }

        public void PlayAbility(int slot)
        {
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayAbility();
                return;
            }
            if (_animator == null || IsDead) return;
            StopAttackWarp();
            if (_faction == Faction.Wole)
            {
                _attackPresentationActive = false;
                _attackPresentationUntil = 0f;
                CancelUpperBodyAttack(0.04f);
                _animator.ResetTrigger("Hook");
                _animator.ResetTrigger("HeavyAttack");
                _animator.ResetTrigger("LowerHeavyAttack");
                _animator.SetTrigger(slot == 1 ? "Hook" : "HeavyAttack");
                if (slot != 1) _animator.SetTrigger("LowerHeavyAttack");
                _abilityPresentationActive = true;
                // Gameplay may resolve before the authored follow-through.
                // Presentation stays protected through the controller's soft
                // return to Empty; a new committed action can still replace it.
                float duration = slot == 1 ? 0.72f : WhirlwindPresentationDuration;
                _abilityPresentationUntil = Time.time + duration;
                _actionProtectedUntil = _abilityPresentationUntil;
            }
        }

        /// <summary>
        /// Тело потащило цепью. Играет knockback-клип, который в контроллере
        /// уже есть.
        ///
        /// Без него волочимый враг едет по земле в позе покоя: собственная
        /// скорость у него обнулена намеренно (иначе играл бы бег), и Idle
        /// оказывается единственным, что остаётся. Скользящая стойка читается
        /// как баг физики, а не как «его тащат».
        ///
        /// Игрока это не касается: его собственный рывок — не потеря контроля,
        /// и подменять ему анимацию на knockback значило бы сообщать обратное.
        /// </summary>
        public void PlayDragged()
        {
            if (_spriteVisual != null || _animator == null || IsDead) return;
            if (_faction == Faction.Wole) return;

            CancelUpperBodyAttack(0.03f);
            _animator.SetTrigger(OrvillKnockback);
        }

        public void PlayHit(int variant)
        {
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayHit(variant);
                return;
            }
            if (_animator == null || IsDead) return;

            // Pelag must never lose locomotion or a committed action to a
            // presentation-only hit. ArenaView already supplies additive root
            // recoil, scale punch and flash, while CombatJuice supplies hit-stop.
            // Until HitFront has its own additive layer, playing the full-body
            // state here would visibly interrupt run/turn/attack/ability.
            if (_faction == Faction.Wole) return;

            // В толпе полный hit-клип на каждый Damage перебивал собственные
            // атаки по нескольку раз в секунду. Для частых лёгких попаданий
            // достаточно уже существующих flash/recoil/hit-stop слоёв.
            if (Time.time < _actionProtectedUntil || Time.time - _lastHitAt < 0.34f) return;
            _lastHitAt = Time.time;
            CancelUpperBodyAttack(0.03f);
            // After contact, Hit may interrupt an Orvill attack recovery. Before
            // it, the guard above leaves only ArenaView recoil/flash visible.
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            ResetOrvillActionTriggers();
            _orvillHitPresentationUntil = Time.time + OrvillHitPresentationDuration;
            _animator.speed = 1f;
            _animator.SetTrigger((variant & 1) == 0 ? OrvillHitLeft : OrvillHitRight);
        }

        /// <summary>
        /// Подтверждённый контакт базовой атаки. Damage остаётся единственной
        /// точкой истины, а presentation на короткий момент фиксирует authored
        /// contact pose, чтобы клинок, вспышка и реакция цели читались одним
        /// событием даже при плавающей частоте кадров.
        /// </summary>
        public void PlayAttackContact(int variant)
        {
            if (_animator == null || IsDead || _faction != Faction.Wole) return;

            bool secondStrike = (variant & 1) != 0;
            int upperState = secondStrike ? UpperBodyAttackBState : UpperBodyAttackAState;
            int lowerState = secondStrike ? LowerBodyAttackBState : LowerBodyAttackAState;
            float contactPhase = secondStrike
                ? AttackBContactNormalized
                : AttackAContactNormalized;

            // Damage is authoritative. If a trigger/transition, a long frame,
            // or an ability recovery ever swallowed the earlier Attack event,
            // never allow health to change on an idle Pelag. Restore the exact
            // A/B contact pose and continue from its recovery. This is a safety
            // net, not a second attack: it has no gameplay authority.
            bool visibleAttack = IsStateVisible(_upperBodyLayer, upperState);
            if (!_attackPresentationActive || !_attackWarpActive || !visibleAttack)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _animator.ResetTrigger("HeavyAttack");
                _animator.ResetTrigger("LowerHeavyAttack");
                _animator.ResetTrigger("Hook");
                ForceAttackContactState(_upperBodyLayer, upperState, contactPhase);
                if (!_locomotionMoving)
                {
                    ForceAttackContactState(_lowerBodyLayer, lowerState, contactPhase);
                    if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 1f);
                }

                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + (secondStrike ? 0.58f : 0.42f);
                _actionProtectedUntil = Mathf.Max(_actionProtectedUntil, Time.time + 0.20f);
                _attackWarpStartedAt = Time.time - AttackContactTime;
                _attackWarpActive = true;
            }

            float until = Time.unscaledTime + AttackContactHoldDuration;
            if (until > _attackContactHoldUntil) _attackContactHoldUntil = until;
            _animator.SetFloat(AttackPlaybackSpeed, 0.035f);
        }

        public void PlayDeath()
        {
            if (IsDead) return;
            IsDead = true;
            if (_spriteVisual != null)
            {
                _spriteVisual.PlayDeath();
                return;
            }
            if (_animator == null) return;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _attackContactHoldUntil = 0f;
            _actionProtectedUntil = 0f;
            _orvillHitPresentationUntil = 0f;
            _animator.SetFloat(MoveSpeed, 0f);
            _animator.SetBool(Stunned, false);
            if (_faction == Faction.Orvill)
            {
                // Damage and Death are emitted in the same Sim tick. Clear the
                // earlier Hit plus any committed attack before bypassing
                // AnyState ordering and entering the terminal death state.
                ResetOrvillActionTriggers();
                _animator.speed = 1f;
                if (_animator.HasState(0, OrvillDeathState))
                    _animator.CrossFadeInFixedTime(OrvillDeathState, 0.025f, 0, 0f);
                else
                    _animator.SetTrigger(OrvillDeath);
                return;
            }

            CancelUpperBodyAttack(0.02f);
            _animator.SetFloat(TurnDirection, 0f);
            _animator.SetTrigger("Death");
        }

        public void FaceCamera(Vector3 facing)
        {
            _spriteVisual?.FaceCamera(facing);
        }

        // Авторские клипы содержат presentation-маркеры. Они намеренно ничего
        // не решают: Damage и тайминг контакта принадлежат Game.Sim. Наличие
        // приёмников только не даёт Unity засорять Console на каждом клипе.
        public void AttackContactCue() { }
        public void ShieldImpactCue() { }
        public void GuardBreakCue() { }
        public void DeathImpactCue() { }
        public void HookReleaseCue() { }
        public void HookRecoverCue() { }
        public void HeavyImpactCue() { }

        private void StartAttackWarp()
        {
            // A contact freeze belongs to exactly one committed attack. A new
            // Attack can be delivered in the same render frame as the previous
            // Damage; carrying the old freeze into it made the next wind-up
            // look absent even though its state had entered correctly.
            _attackContactHoldUntil = 0f;
            _attackWarpStartedAt = Time.time;
            _attackWarpActive = true;
            _animator.SetFloat(AttackPlaybackSpeed, AttackAnticipationSpeed);
        }

        private void StopAttackWarp()
        {
            _attackWarpActive = false;
            _attackContactHoldUntil = 0f;
            if (_animator != null && _faction == Faction.Wole)
                _animator.SetFloat(AttackPlaybackSpeed, 1f);
        }

        private bool EnterCommittedAttackState(int layer, int stateHash)
        {
            if (_animator == null || layer < 0 || !_animator.HasState(layer, stateHash))
                return false;

            // A short fixed blend preserves continuity without leaving action
            // ownership to an interruptible transition graph.
            _animator.CrossFadeInFixedTime(stateHash, 0.045f, layer, 0f);
            return true;
        }

        private void ForceAttackContactState(int layer, int stateHash, float normalizedTime)
        {
            if (_animator == null || layer < 0 || !_animator.HasState(layer, stateHash)) return;
            _animator.CrossFade(stateHash, 0.012f, layer, normalizedTime);
        }

        private bool IsStateVisible(int layer, int stateHash)
        {
            if (_animator == null || layer < 0) return false;
            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(layer);
            if (current.fullPathHash == stateHash) return true;
            if (!_animator.IsInTransition(layer)) return false;
            return _animator.GetNextAnimatorStateInfo(layer).fullPathHash == stateHash;
        }

        private void CancelUpperBodyAttack(float blend)
        {
            if (_animator == null) return;
            StopAttackWarp();
            if (_faction != Faction.Wole) return;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _animator.ResetTrigger("AttackA");
            _animator.ResetTrigger("AttackB");
            _animator.ResetTrigger("LowerAttackA");
            _animator.ResetTrigger("LowerAttackB");
            if (_upperBodyLayer >= 0
                && _animator.HasState(_upperBodyLayer, UpperBodyEmptyState))
                _animator.CrossFade(UpperBodyEmptyState, blend, _upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0)
            {
                _animator.SetLayerWeight(_lowerBodyLayer, 0f);
                if (_animator.HasState(_lowerBodyLayer, LowerBodyEmptyState))
                    _animator.CrossFade(LowerBodyEmptyState, blend, _lowerBodyLayer, 0f);
            }
        }

        private void RestoreOrvillLocomotionPlayback()
        {
            if (_animator == null || _faction != Faction.Orvill || IsDead) return;
            if (_attackPresentationActive || Time.time < _orvillHitPresentationUntil) return;
            _animator.speed = _orvillLocomotionPlaybackSpeed;
        }

        private static float DeterministicLocomotionPhase(int presentationId)
        {
            unchecked
            {
                uint hash = (uint)presentationId + 0x9E3779B9u;
                hash = (hash ^ (hash >> 16)) * 0x7FEB352Du;
                hash = (hash ^ (hash >> 15)) * 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777216f;
            }
        }

        private void ResetOrvillActionTriggers()
        {
            if (_animator == null) return;
            _animator.ResetTrigger(OrvillSwordAttack);
            _animator.ResetTrigger(OrvillShieldBash);
            _animator.ResetTrigger(OrvillHighBlock);
            _animator.ResetTrigger(OrvillGuardBreak);
            _animator.ResetTrigger(OrvillHitLeft);
            _animator.ResetTrigger(OrvillHitRight);
            _animator.ResetTrigger(OrvillKnockback);
            _animator.ResetTrigger(OrvillDeath);
        }

        private void OnDisable()
        {
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _locomotionMoving = false;
            _orvillLocomotionPlaybackSpeed = 1f;
            _orvillHitPresentationUntil = 0f;
            if (_animator != null && _lowerBodyLayer >= 0)
                _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_animator != null && _faction == Faction.Orvill)
                _animator.speed = 1f;
            StopAttackWarp();
        }
    }
}
