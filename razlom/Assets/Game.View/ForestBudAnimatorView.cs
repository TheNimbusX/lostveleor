using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(300)]
    public sealed class ForestBudAnimatorView : MonoBehaviour
    {
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private static readonly int Attack = Animator.StringToHash("Base Layer.Ranged_Attack");
        private static readonly int Death = Animator.StringToHash("Base Layer.Death");
        private static readonly int AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int WalkPhase = Animator.StringToHash("WalkPhase");
        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _state;
        private bool _dead;
        private float _clock, _reloadAt, _walkPhase;
        private readonly Transform[] _sockets = new Transform[5];
        private readonly Renderer[] _loadedFruits = new Renderer[5];

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                for (int i = 0; i < _sockets.Length; i++)
                {
                    if (child.name == "Spawn_Fruit_" + (i + 1).ToString("00")) _sockets[i] = child;
                    if (child.name == "SM_LoadedFruit_" + (i + 1).ToString("00")) _loadedFruits[i] = child.GetComponent<Renderer>();
                }
        }

        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity; _dead = false; _state = Idle; _clock = _reloadAt = _walkPhase = 0f;
            _animator.Rebind(); _animator.speed = 1f;
            _animator.Play(Idle, 0, (entity * .173f) % 1f);
            _animator.Update(0f);
            SetLoadedFruits(0);
        }

        public Vector3 LaunchPosition(int shot)
            => _sockets[Mathf.Clamp(shot, 0, 4)] != null
                ? _sockets[Mathf.Clamp(shot, 0, 4)].position : transform.position + Vector3.up * 1.15f;

        private void Update()
        {
            if (_driver == null) return;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            _clock += Time.deltaTime;
            if (_dead) { _animator.speed = 1f; return; }
            if (_driver.Sim == null || (uint)_entity >= _driver.Sim.Entities.Count) return;
            var sim = _driver.Sim;
            if (!sim.Entities.Alive[_entity]) { PlayDeath(); return; }
            if (sim.TryGetForestBudAttack(_entity, out var attack))
            {
                float phase = Mathf.Clamp01((sim.Tick - 1 + _driver.Alpha - attack.StartTick)
                    / Mathf.Max(1, attack.EndTick - attack.StartTick));
                _animator.SetFloat(AttackPhase, phase);
                Enter(Attack, .10f);
                _animator.speed = 1f;
                SetLoadedFruits(attack.ShotsFired);
            }
            else
            {
                if (_state == Attack) _reloadAt = _clock + .2f;
                float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
                var velocity = sim.Entities.Velocity[_entity];
                var facing = sim.Entities.Facing[_entity];
                bool backward = (velocity.X * facing.X + velocity.Y * facing.Y).Raw < 0;
                // При отходе назад не переворачиваем весь Animator: сохраняем фазу опоры лап.
                _walkPhase += (backward ? -1f : 1f) * speed / .90f * Time.deltaTime / (32f / 30f);
                _animator.SetFloat(WalkPhase, _walkPhase - Mathf.Floor(_walkPhase));
                Enter(speed > .025f ? Walk : Idle, .14f);
                _animator.speed = 1f;
                if (_clock >= _reloadAt) SetLoadedFruits(0);
            }
        }

        private void Enter(int state, float blend)
        {
            if (_state == state) return;
            _state = state; _animator.CrossFadeInFixedTime(state, blend, 0, 0f);
        }

        private void SetLoadedFruits(int released)
        {
            for (int i = 0; i < _loadedFruits.Length; i++)
                if (_loadedFruits[i] != null) _loadedFruits[i].enabled = i >= released;
        }

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true; _animator.speed = 1f; Enter(Death, .09f);
        }
    }
}
