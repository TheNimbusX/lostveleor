using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(300)]
    public sealed class ForestWendigoAnimatorView : MonoBehaviour
    {
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle"), Walk = Animator.StringToHash("Base Layer.Walk"),
            Claw = Animator.StringToHash("Base Layer.Claw"), Leap = Animator.StringToHash("Base Layer.Leap"),
            Hit = Animator.StringToHash("Base Layer.Hit"), Death = Animator.StringToHash("Base Layer.Death");
        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _state, _health;
        private bool _dead;
        private float _deathClock, _idleClock, _walkPhase, _hitClock;
        private void Awake() => _animator = GetComponentInChildren<Animator>();
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity; _state = Idle; _dead = false;
            _deathClock = _idleClock = _walkPhase = 0; _hitClock = 1f; _health = driver.Sim.Entities.Health[entity];
            _animator.Rebind(); _animator.SetFloat("IdlePhase", 0); _animator.Play(Idle, 0, 0); _animator.Update(0);
        }
        private void Update()
        {
            if (_driver?.Sim == null || _entity >= _driver.Sim.Entities.Count) return;
            if (_driver.GameplayPaused) { _animator.speed = 0; return; }
            _animator.speed = 1; var sim = _driver.Sim; float dt = Time.deltaTime;
            if (!sim.Entities.Alive[_entity]) PlayDeath();
            if (_dead)
            {
                _deathClock += dt;
                // Убираем вступительное ожидание референса, само падение сохраняет исходный темп.
                Sample(Death, Mathf.Lerp(18f/96f, 1, Mathf.Clamp01(_deathClock/(78f/24f))), .07f); return;
            }
            if (sim.Entities.Health[_entity] < _health && !sim.TryGetWendigoAction(_entity, out _)) _hitClock = 0;
            _health = sim.Entities.Health[_entity]; _hitClock += dt;
            if (sim.TryGetWendigoAction(_entity, out var a))
            {
                float time = Mathf.Max(0, sim.Tick - 1 + _driver.Alpha - a.StartTick);
                float frame;
                if (a.Kind == WendigoAction.Claw)
                    frame = time < 18 ? Mathf.Lerp(19, 52, time/18) : Mathf.Lerp(52, 84, Mathf.Clamp01((time-18)/12));
                else if (time < 24) frame = Mathf.Lerp(9,45,time/24);
                else if (time < 33) frame = Mathf.Lerp(45,60,(time-24)/9);
                else frame = Mathf.Lerp(60,96,Mathf.Clamp01((time-33)/18));
                Sample(a.Kind == WendigoAction.Claw ? Claw : Leap, frame/96, .045f);
            }
            else if (_hitClock < .5f) Sample(Hit, _hitClock/.5f, .04f);
            else
            {
                float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
                // Walk r04 is authored for 2.4 m per complete left/right cycle.
                _idleClock += dt; _walkPhase += speed*dt/2.4f;
                Sample(speed > .06f ? Walk : Idle, speed > .06f ? Mathf.Repeat(_walkPhase,1) : Mathf.Repeat(_idleClock/2.5f,1), .13f);
            }
        }
        private void Sample(int state, float phase, float blend)
        {
            string parameter = state == Idle ? "IdlePhase" : state == Walk ? "WalkPhase" : state == Claw ? "ClawPhase"
                : state == Leap ? "LeapPhase" : state == Hit ? "HitPhase" : "DeathPhase";
            _animator.SetFloat(parameter, phase);
            if (_state == state) return;
            _state = state; _animator.CrossFadeInFixedTime(state, blend, 0, 0);
        }
        public void PlayDeath()
        {
            if (_dead) return; _dead = true; _deathClock = 0;
        }
    }
}
