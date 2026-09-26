using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(310)]
    public sealed class StonehoofAnimatorView : MonoBehaviour
    {
        // РАЗВОРОТ НА МЕСТЕ. Симуляция крутит корпус по 6° за тик (180°/с),
        // и Idle под вращающимся корнем читался как прокрутка модели вокруг
        // оси. Поворот ловится по отрисованному направлению: быстрее 30°/с и
        // без хода — это разворот. Фаза клипа TurnLeft/TurnRight — накопленный
        // поворот / 90°, как UpdateTurn у Пелага. Если клипов в модели нет (сборщик
        // их пропускает), ноги переступают медленной фазой Walk от того же поворота.
        //
        // ВХОД И ВЫХОД ПЛАВНЫЕ. Клипы разворота начинаются с присевшей стойки,
        // голова в ней до 0,19 м от Idle, и короткая смесь щёлкала позой.
        // Поэтому: смесь в разворот и из него не короче TurnBlendSeconds;
        // в разворот — только после TurnEnterDegrees поворота в одну сторону
        // (поправка курса в один тик его не включает); после остановки
        // корпуса поза держится TurnHoldSeconds; пока идёт смесь с участием
        // разворота, новая смена разворота не начинается — прерванная смесь
        // Unity щёлкает так же, как короткая.
        private const float TurnRateThreshold = 30f, TurnHoldSeconds = .25f, TurnClipDegrees = 90f;
        private const float TurnBlendSeconds = .18f, TurnEnterDegrees = 8f;
        private const float TurnShuffleDegreesPerCycle = 120f;
        private static readonly int TurnLeftState = Animator.StringToHash("Base Layer.TurnLeft"),
            TurnRightState = Animator.StringToHash("Base Layer.TurnRight");
        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _health;
        private string _state;
        private bool _dead, _hasTurnClips;
        private float _deathClock, _idleClock, _walkPhase, _hitClock;
        // _turnSign: 0 — разворота нет, −1 — влево, +1 — вправо. _pendingYaw —
        // поворот со знаком, ещё не включивший разворот. _turnSettled — время
        // конца последней смеси с участием разворота.
        private float _turnTravel, _turnSign, _turnUntil, _pendingYaw, _pendingUntil, _turnSettled;
        private Vector3 _lastFacing;
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
            _turnTravel = _turnSign = _pendingYaw = 0; _turnUntil = _pendingUntil = _turnSettled = -1;
            _lastFacing = Vector3.zero;
            _health = driver.Sim.Entities.Health[entity];
            _animator.Rebind(); _animator.SetFloat("IdlePhase", 0);
            _hasTurnClips = _animator.HasState(0, TurnLeftState) && _animator.HasState(0, TurnRightState);
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
            // Поворот считается каждый кадр, и в атаке тоже: иначе первый кадр
            // после неё увидел бы весь накопленный за таран угол разом.
            Vector3 facing = _driver.GetRenderFacing(_entity);
            float yaw = _lastFacing.sqrMagnitude > .5f && facing.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastFacing, facing, Vector3.up) : 0f;
            if (facing.sqrMagnitude > .5f) _lastFacing = facing;
            bool acting = sim.TryGetStonehoofAction(_entity, out var a);
            if (_health > sim.Entities.Health[_entity] && !acting) _hitClock = 0;
            _health = sim.Entities.Health[_entity]; _hitClock += dt;
            float tick = sim.Tick - 1 + _driver.Alpha;
            if (acting)
            {
                ForgetTurn();
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
            if (_hitClock < .4f) { ForgetTurn(); Sample("Hit", _hitClock / .4f, .05f); return; }
            float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            _idleClock += dt; _walkPhase += speed * dt / 1.0f;
            if (speed > .04f) ForgetTurn();
            else if (UpdateTurn(yaw, dt)) return;
            Sample(speed > .04f ? "Walk" : "Idle", speed > .04f ? Mathf.Repeat(_walkPhase, 1) : Mathf.Repeat(_idleClock / 3, 1), .12f);
        }

        /// <summary>Корпус крутится на месте — играет разворот. False — разворота нет.</summary>
        private bool UpdateTurn(float yaw, float dt)
        {
            float now = Time.time;
            bool rotating = dt > .00001f && Mathf.Abs(yaw) / dt > TurnRateThreshold;
            bool settled = now >= _turnSettled;
            if (_turnSign != 0f && yaw * _turnSign > 0f)
            {
                // Корпус крутится в ту же сторону: фаза идёт за поворотом. 30 Гц
                // поворота интерполированы, но кадр без сдвига всё равно бывает;
                // удержание не даёт позе мигать Turn/Idle/Turn.
                _turnTravel += Mathf.Abs(yaw); _pendingYaw = 0f;
                if (rotating) _turnUntil = now + TurnHoldSeconds;
            }
            else if (rotating)
            {
                // Новый разворот или смена стороны — только после TurnEnterDegrees
                // в одну сторону и после конца прошлой смеси.
                if (_pendingYaw * yaw < 0f || now > _pendingUntil) _pendingYaw = 0f;
                _pendingYaw += yaw; _pendingUntil = now + TurnHoldSeconds;
                if (settled && Mathf.Abs(_pendingYaw) >= TurnEnterDegrees)
                {
                    _turnSign = Mathf.Sign(_pendingYaw); _turnTravel = Mathf.Abs(_pendingYaw);
                    _turnUntil = now + TurnHoldSeconds; _pendingYaw = 0f;
                }
            }
            if (_turnSign != 0f && now > _turnUntil && settled) _turnSign = 0f;
            if (_turnSign == 0f) return false;
            if (_hasTurnClips)
            {
                float phase = Mathf.Repeat(_turnTravel, TurnClipDegrees) / TurnClipDegrees;
                if (phase < .0001f && _turnTravel > 1f) phase = 1f;
                Sample(_turnSign < 0f ? "TurnLeft" : "TurnRight", phase, TurnBlendSeconds);
            }
            else
            {
                // Заплатка до клипов: переступание ногами Walk в темпе поворота.
                _walkPhase += Mathf.Abs(yaw) / TurnShuffleDegreesPerCycle;
                Sample("Walk", Mathf.Repeat(_walkPhase, 1), .12f);
            }
            return true;
        }

        /// <summary>Атака, попадание или ход перебили разворот: копить поворот заново.</summary>
        private void ForgetTurn() { _turnSign = 0f; _pendingYaw = 0f; }

        private static bool IsTurn(string state) => state == "TurnLeft" || state == "TurnRight";

        private void Sample(string state, float phase, float blend)
        {
            _animator.SetFloat(state + "Phase", phase);
            if (_state == state) return;
            if (IsTurn(state) || IsTurn(_state))
            {
                // Присевшая стойка разворота далеко от остальных поз — смесь длинная.
                blend = Mathf.Max(blend, TurnBlendSeconds);
                _turnSettled = Time.time + blend;
                if (!IsTurn(state)) _turnSign = 0f;
            }
            _state = state;
            if (blend <= 0) _animator.Play("Base Layer." + state, 0, phase);
            else _animator.CrossFadeInFixedTime("Base Layer." + state, blend, 0, 0);
        }
        public void PlayDeath() { if (_dead) return; _dead = true; _deathClock = 0; }
    }
}
