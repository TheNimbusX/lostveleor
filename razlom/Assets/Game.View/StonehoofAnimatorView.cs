using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(310)]
    public sealed class StonehoofAnimatorView : MonoBehaviour
    {
        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _health;
        private string _state;
        private bool _dead;
        private float _deathClock, _idleClock, _walkPhase, _hitClock;
        public Transform[] Hooves { get; private set; }
        public Transform Head { get; private set; }
        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>(); Hooves = new Transform[4];
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "leg_front_left_bot2") Hooves[0] = t;
                if (t.name == "leg_front_right_bot2") Hooves[1] = t;
                if (t.name == "leg_hind_left_bot2") Hooves[2] = t;
                if (t.name == "leg_hind_right_bot2") Hooves[3] = t;
                if (t.name == "head0") Head = t;
            }
        }
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity; _dead = false; _state = "Idle";
            _deathClock = _idleClock = _walkPhase = 0; _hitClock = 1;
            _health = driver.Sim.Entities.Health[entity];
            _animator.Rebind(); _animator.SetFloat("IdlePhase", 0);
            _animator.Play("Base Layer.Idle", 0, 0); _animator.Update(0);
        }
        private void Update()
        {
            if (_driver?.Sim == null || _entity >= _driver.Sim.Entities.Count) return;
            if (_driver.GameplayPaused) { _animator.speed = 0; return; }
            _animator.speed = 1;
            var sim = _driver.Sim; float dt = Time.deltaTime;
            if (!sim.Entities.Alive[_entity]) PlayDeath();
            if (_dead) { _deathClock += dt; Sample("Death", Mathf.Clamp01(_deathClock / 2), .09f); return; }
            bool acting = sim.TryGetStonehoofAction(_entity, out var a);
            if (_health > sim.Entities.Health[_entity] && !acting) _hitClock = 0;
            _health = sim.Entities.Health[_entity]; _hitClock += dt;
            float tick = sim.Tick - 1 + _driver.Alpha;
            if (acting)
            {
                if (tick < a.LaunchTick) Sample("Windup", Mathf.Clamp01((tick - a.StartTick) / 30), .06f);
                else if (tick >= a.StopTick && a.StopReason == StonehoofStop.Obstacle)
                    Sample("WallImpact", Mathf.Clamp01((tick - a.StopTick) / 36), .025f);
                else if (a.StopReason == StonehoofStop.Obstacle && tick >= a.StopTick - 4)
                    Sample("WallBrace", Mathf.Clamp01((tick - a.StopTick + 4) / 4), .04f);
                else if (a.StopReason == StonehoofStop.ArenaEdge && tick >= a.BrakeTick)
                    Sample("Brake", Mathf.Clamp01((tick - a.BrakeTick) / 18), .055f);
                else if (tick < a.LaunchTick + 6) Sample("Launch", Mathf.Clamp01((tick - a.LaunchTick) / 6), 0);
                else Sample("ChargeLoop", Mathf.Repeat((tick - a.LaunchTick - 6) / 12, 1), .025f);
                return;
            }
            if (_hitClock < .4f) { Sample("Hit", _hitClock / .4f, .05f); return; }
            float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            _idleClock += dt; _walkPhase += speed * dt / 1.0f;
            Sample(speed > .04f ? "Walk" : "Idle", speed > .04f ? Mathf.Repeat(_walkPhase, 1) : Mathf.Repeat(_idleClock / 3, 1), .12f);
        }
        private void Sample(string state, float phase, float blend)
        {
            _animator.SetFloat(state + "Phase", phase);
            if (_state == state) return;
            _state = state;
            if (blend <= 0) _animator.Play("Base Layer." + state, 0, phase);
            else _animator.CrossFadeInFixedTime("Base Layer." + state, blend, 0, 0);
        }
        public void PlayDeath() { if (_dead) return; _dead = true; _deathClock = 0; }
    }
}
