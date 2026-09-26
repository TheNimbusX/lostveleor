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
        private static readonly int Hit = Animator.StringToHash("Base Layer.Hit");
        private static readonly int Death = Animator.StringToHash("Base Layer.Death");
        private static readonly int AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int WalkPhase = Animator.StringToHash("WalkPhase");
        private static readonly int HitPhase = Animator.StringToHash("HitPhase");
        // Клип Hit — 11 кадров при 30 fps, первый и последний ровно в позе покоя.
        private const float HitSeconds = 10f / 30f;
        // Удар раньше этого не перезапускает реакцию: серия частых попаданий
        // иначе держала бы бутон в первых кадрах клипа, и он бы замер.
        private const float HitRetrigger = .16f;
        // Поверх залпа и шага реакция идёт слоем «Hit Additive» и слабее: фаза
        // атаки, сокеты плодов и поступь лап не прерываются.
        private const float HitOverAttackWeight = .55f, HitOverWalkWeight = .8f;
        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _state, _hitLayer = -1;
        private bool _dead, _hasHitState, _hitAdditive;
        private float _clock, _reloadAt, _walkPhase, _hitClock, _hitWeight;
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
            _hitClock = HitSeconds; _hitAdditive = false;
            _animator.Rebind(); _animator.speed = 1f;
            // Старый контроллер без Hit не ломает моба: реакции просто нет.
            _hasHitState = _animator.HasState(0, Hit);
            _hitLayer = _animator.GetLayerIndex("Hit Additive");
            if (_hitLayer >= 0) _animator.SetLayerWeight(_hitLayer, 0f);
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
            _hitClock += Time.deltaTime;
            if (_dead) { _animator.speed = 1f; return; }
            if (_driver.Sim == null || (uint)_entity >= _driver.Sim.Entities.Count) return;
            var sim = _driver.Sim;
            if (!sim.Entities.Alive[_entity]) { PlayDeath(); return; }
            bool attacking = sim.TryGetForestBudAttack(_entity, out var attack);
            float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            if (_hitClock >= HitRetrigger && TookHitThisFrame())
            {
                _hitClock = 0f;
                // Режим выбирается в момент попадания и держится до конца клипа:
                // стоящий бутон играет Hit целиком, идущий или стреляющий — слоем.
                _hitAdditive = attacking || speed > .025f || !_hasHitState;
                _hitWeight = attacking ? HitOverAttackWeight : HitOverWalkWeight;
            }
            bool hitActive = _hitClock < HitSeconds;
            _animator.SetFloat(HitPhase, Mathf.Clamp01(_hitClock / HitSeconds));
            if (_hitLayer >= 0) _animator.SetLayerWeight(_hitLayer, hitActive && _hitAdditive ? _hitWeight : 0f);
            if (attacking)
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
                if (hitActive && !_hitAdditive) Enter(Hit, .04f);
                else
                {
                    var velocity = sim.Entities.Velocity[_entity];
                    var facing = sim.Entities.Facing[_entity];
                    bool backward = (velocity.X * facing.X + velocity.Y * facing.Y).Raw < 0;
                    // При отходе назад не переворачиваем весь Animator: сохраняем фазу опоры лап.
                    _walkPhase += (backward ? -1f : 1f) * speed / .90f * Time.deltaTime / (32f / 30f);
                    _animator.SetFloat(WalkPhase, _walkPhase - Mathf.Floor(_walkPhase));
                    Enter(speed > .025f ? Walk : Idle, .14f);
                }
                _animator.speed = 1f;
                if (_clock >= _reloadAt) SetLoadedFruits(0);
            }
        }

        /// <summary>
        /// Попадание по этому бутону на тиках текущего кадра. Урон по времени
        /// идёт другим типом события и реакцию не будит — иначе горящий бутон
        /// вздрагивал бы без конца.
        /// </summary>
        private bool TookHitThisFrame()
        {
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.Damage && events[i].Target == _entity && events[i].Amount > 0) return true;
            return false;
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
            _dead = true; _animator.speed = 1f; _hitClock = HitSeconds;
            if (_hitLayer >= 0) _animator.SetLayerWeight(_hitLayer, 0f);
            Enter(Death, .09f);
        }
    }
}
