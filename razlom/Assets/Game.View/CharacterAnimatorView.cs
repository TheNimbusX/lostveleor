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
        private const string UpperBodyLayerName = "UpperBody Combat";
        private const string LowerBodyLayerName = "LowerBody Combat";

        private const float AttackAnticipationEnd = 0.28f;
        private const float AttackAccelerationEnd = 0.60f;
        private const float AttackContactEnd = 0.72f;
        private const float AttackRecoveryEnd = 0.84f;
        private const float AttackAnticipationSpeed = 0.62f;
        private const float AttackAccelerationSpeed = 1.33f;
        private const float AttackContactSpeed = 0.72f;
        private const float AttackRecoverySpeed = 1.18f;

        private Animator _animator;
        private SpriteCharacterVisual _spriteVisual;
        private Faction _faction;
        private int _attackVariant;
        private float _actionProtectedUntil;
        private float _lastHitAt = -10f;
        private float _attackWarpStartedAt;
        private bool _attackWarpActive;
        private bool _attackPresentationActive;
        private float _attackPresentationUntil;
        private bool _abilityPresentationActive;
        private float _abilityPresentationUntil;
        private int _upperBodyLayer = -1;
        private int _lowerBodyLayer = -1;
        private bool _locomotionMoving;

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
            IsDead = false;
            _attackVariant = 0;
            _actionProtectedUntil = 0f;
            _lastHitAt = -10f;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _locomotionMoving = false;
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
        }

        private void Update()
        {
            if (_abilityPresentationActive && Time.time >= _abilityPresentationUntil)
                _abilityPresentationActive = false;
            if (_attackPresentationActive && Time.time >= _attackPresentationUntil)
                _attackPresentationActive = false;

            if (_animator == null) return;
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

            if (!_attackWarpActive) return;

            // Scaled time lets contact hit-stop pause this visual curve too.
            float elapsed = Time.time - _attackWarpStartedAt;
            if (elapsed < AttackAnticipationEnd)
                _animator.SetFloat(AttackPlaybackSpeed, AttackAnticipationSpeed);
            else if (elapsed < AttackAccelerationEnd)
                _animator.SetFloat(AttackPlaybackSpeed, AttackAccelerationSpeed);
            else if (elapsed < AttackContactEnd)
                _animator.SetFloat(AttackPlaybackSpeed, AttackContactSpeed);
            else if (elapsed < AttackRecoveryEnd)
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
        }

        public void PlayAttack()
        {
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayAttack(_attackVariant++);
                return;
            }
            if (_animator == null || IsDead) return;
            if (_faction == Faction.Wole)
            {
                _abilityPresentationActive = false;
                // В исходной delivery два настоящих удара; frames 99–147 —
                // recovery второго, а не третий finisher. Проигрываем честную
                // связку A/B и оставляем recovery внутри B.
                bool secondStrike = (_attackVariant++ & 1) != 0;
                string trigger = secondStrike ? "AttackB" : "AttackA";
                string lowerTrigger = trigger == "AttackA" ? "LowerAttackA" : "LowerAttackB";
                _animator.ResetTrigger("AttackA");
                _animator.ResetTrigger("AttackB");
                _animator.ResetTrigger("LowerAttackA");
                _animator.ResetTrigger("LowerAttackB");
                _animator.SetTrigger(trigger);
                _animator.SetTrigger(lowerTrigger);
                StartAttackWarp();
                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + (secondStrike ? 1.22f : 0.96f);
                // Слабые входящие попадания всё ещё получают recoil/flash в
                // ArenaView, но не имеют права ломать читаемую фазу клинка.
                _actionProtectedUntil = Time.time + 0.76f;
            }
            else
                _animator.SetTrigger((_attackVariant++ % 3) == 2 ? "ShieldBash" : "SwordAttack");
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
                // Whirlwind's masked layer fully returns to Empty at the same
                // 0.80 s boundary used by deterministic Simulation.
                float duration = slot == 1 ? 0.72f : 0.80f;
                _abilityPresentationUntil = Time.time + duration;
                _actionProtectedUntil = _abilityPresentationUntil;
            }
        }

        public void PlayHit(int variant)
        {
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayHit(variant);
                return;
            }
            if (_animator == null || IsDead) return;
            // В толпе полный hit-клип на каждый Damage перебивал собственные
            // атаки по нескольку раз в секунду. Для частых лёгких попаданий
            // достаточно уже существующих flash/recoil/hit-stop слоёв.
            if (Time.time < _actionProtectedUntil || Time.time - _lastHitAt < 0.34f) return;
            _lastHitAt = Time.time;
            CancelUpperBodyAttack(0.03f);
            if (_faction == Faction.Wole)
                _animator.SetTrigger("HitFront");
            else
                _animator.SetTrigger((variant & 1) == 0 ? "HitLeft" : "HitRight");
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
            CancelUpperBodyAttack(0.02f);
            _animator.SetFloat(MoveSpeed, 0f);
            if (_faction == Faction.Wole) _animator.SetFloat(TurnDirection, 0f);
            _animator.SetBool(Stunned, false);
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
            _attackWarpStartedAt = Time.time;
            _attackWarpActive = true;
            _animator.SetFloat(AttackPlaybackSpeed, AttackAnticipationSpeed);
        }

        private void StopAttackWarp()
        {
            _attackWarpActive = false;
            if (_animator != null && _faction == Faction.Wole)
                _animator.SetFloat(AttackPlaybackSpeed, 1f);
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

        private void OnDisable()
        {
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _locomotionMoving = false;
            if (_animator != null && _lowerBodyLayer >= 0)
                _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            StopAttackWarp();
        }
    }
}
