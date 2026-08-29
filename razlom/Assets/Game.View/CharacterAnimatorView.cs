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
        private static readonly int Relaxed = Animator.StringToHash("Relaxed");
        private static readonly int Stunned = Animator.StringToHash("Stunned");

        private Animator _animator;
        private SpriteCharacterVisual _spriteVisual;
        private Faction _faction;
        private int _attackVariant;

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
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        public void ResetForSpawn()
        {
            IsDead = false;
            _attackVariant = 0;
            if (_spriteVisual != null)
            {
                _spriteVisual.ResetForSpawn();
                return;
            }
            if (_animator == null) return;
            _animator.Rebind();
            _animator.Update(0f);
            if (_faction == Faction.Wole) _animator.SetBool(Relaxed, false);
            _animator.SetBool(Stunned, false);
            _animator.SetFloat(MoveSpeed, 0f);
        }

        public void SetMoving(bool moving)
        {
            if (_spriteVisual != null)
            {
                _spriteVisual.SetMoving(moving);
                return;
            }
            if (_animator == null || IsDead) return;
            _animator.SetFloat(MoveSpeed, moving ? 1f : 0f, 0.08f, Time.deltaTime);
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
                _animator.SetTrigger((_attackVariant++ & 1) == 0 ? "AttackA" : "AttackB");
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
            if (_faction == Faction.Wole)
                _animator.SetTrigger(slot == 1 ? "Hook" : "HeavyAttack");
        }

        public void PlayHit(int variant)
        {
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayHit(variant);
                return;
            }
            if (_animator == null || IsDead) return;
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
            _animator.SetFloat(MoveSpeed, 0f);
            _animator.SetBool(Stunned, false);
            _animator.SetTrigger("Death");
        }

        public void FaceCamera(Vector3 facing)
        {
            _spriteVisual?.FaceCamera(facing);
        }
    }
}
