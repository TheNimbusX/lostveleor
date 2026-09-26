using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(300)]
    public sealed class ForestWendigoAnimatorView : MonoBehaviour
    {
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle"), Walk = Animator.StringToHash("Base Layer.Walk"),
            Claw = Animator.StringToHash("Base Layer.Claw"), Leap = Animator.StringToHash("Base Layer.Leap"),
            Hit = Animator.StringToHash("Base Layer.Hit"), Death = Animator.StringToHash("Base Layer.Death"),
            Howl = Animator.StringToHash("Base Layer.Howl");

        // «ВОЙ ЧАЩИ» ДО СВОЕГО КЛИПА. Пока Howl не нарисован (сборщик пропускает
        // состояние), вой читается временной позой из кадров когтя: за замах
        // зверь медленно заводит лапы до верхней точки и поднимается, раздуваясь;
        // на контакте держит верх с дрожью — это и есть рёв, — потом опадает.
        // Кадры — из клипа Claw (96 кадров): 19 — стойка, 44 — верх замаха до броска.
        private const float HowlPoseFrom = 19f, HowlPoseTop = 44f, HowlHoldShare = .4f;
        private const float HowlRiseMetres = .12f, HowlSwell = .04f;

        /// <summary>Доля клипа Howl на контакте. Замерить в Blender, когда клип появится.</summary>
        private const float HowlContactPhase = .5f;

        private Animator _animator;
        private TickDriver _driver;
        private int _entity, _state, _health;
        private bool _dead, _hasHowl;
        private float _deathClock, _idleClock, _walkPhase, _hitClock, _rise;
        private Transform _body;
        private Vector3 _bodyPosition, _bodyScale;
        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            // Подъём временной позы двигает тело под корнем: корень ведёт ArenaView.
            _body = _animator.transform != transform ? _animator.transform : null;
            if (_body != null) { _bodyPosition = _body.localPosition; _bodyScale = _body.localScale; }
        }
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity; _state = Idle; _dead = false;
            _deathClock = _idleClock = _walkPhase = 0; _hitClock = 1f; _health = driver.Sim.Entities.Health[entity];
            _rise = 0; ApplyRise();
            _animator.Rebind(); _animator.SetFloat("IdlePhase", 0); _animator.Play(Idle, 0, 0); _animator.Update(0);
            _hasHowl = _animator.HasState(0, Howl);
        }
        private void Update()
        {
            if (_driver?.Sim == null || _entity >= _driver.Sim.Entities.Count) return;
            if (_driver.GameplayPaused) { _animator.speed = 0; return; }
            _animator.speed = 1; var sim = _driver.Sim; float dt = Time.deltaTime;
            float rise = 0f;
            if (!sim.Entities.Alive[_entity]) PlayDeath();
            if (_dead)
            {
                _deathClock += dt;
                // Убираем вступительное ожидание референса, само падение сохраняет исходный темп.
                Sample(Death, Mathf.Lerp(18f/96f, 1, Mathf.Clamp01(_deathClock/(78f/24f))), .07f);
            }
            else
            {
                if (sim.Entities.Health[_entity] < _health && !sim.TryGetWendigoAction(_entity, out _)) _hitClock = 0;
                _health = sim.Entities.Health[_entity]; _hitClock += dt;
                if (sim.TryGetWendigoAction(_entity, out var a))
                {
                    float time = Mathf.Max(0, sim.Tick - 1 + _driver.Alpha - a.StartTick);
                    if (a.Kind == WendigoAction.Howl) rise = SampleHowl(a, time);
                    else
                    {
                        float frame;
                        if (a.Kind == WendigoAction.Claw)
                            frame = time < 18 ? Mathf.Lerp(19, 52, time/18) : Mathf.Lerp(52, 84, Mathf.Clamp01((time-18)/12));
                        else if (time < 24) frame = Mathf.Lerp(9,45,time/24);
                        else if (time < 33) frame = Mathf.Lerp(45,60,(time-24)/9);
                        else frame = Mathf.Lerp(60,96,Mathf.Clamp01((time-33)/18));
                        Sample(a.Kind == WendigoAction.Claw ? Claw : Leap, frame/96, .045f);
                    }
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
            // Сглаживание: отменённый оглушением вой опадает, а не выпрыгивает из позы.
            float settled = Mathf.Lerp(_rise, rise, 1f - Mathf.Exp(-12f * dt));
            if (rise == 0f && settled < .001f) settled = 0f;
            if (settled != _rise) { _rise = settled; ApplyRise(); }
        }

        /// <summary>Вой: свой клип, если собран, иначе временная поза. Возвращает подъём 0..1.</summary>
        private float SampleHowl(WendigoActionState a, float time)
        {
            float windup = a.ImpactTick - a.StartTick, recovery = Mathf.Max(1, a.EndTick - a.ImpactTick);
            float after = Mathf.Clamp01((time - windup) / recovery);
            if (_hasHowl)
            {
                Sample(Howl, time < windup ? Mathf.Lerp(0, HowlContactPhase, time / windup)
                    : Mathf.Lerp(HowlContactPhase, 1, after), .06f);
                return 0f;
            }
            float frame, rise;
            if (time < windup)
            {
                float k = Mathf.SmoothStep(0, 1, time / windup);
                frame = Mathf.Lerp(HowlPoseFrom, HowlPoseTop, k); rise = k;
            }
            else if (after < HowlHoldShare)
            {
                // Рёв: верх замаха с мелкой дрожью, около 11 Гц от тиков Sim.
                frame = HowlPoseTop + Mathf.Sin(time * 2.4f) * 1.2f; rise = 1;
            }
            else
            {
                float back = Mathf.SmoothStep(0, 1, (after - HowlHoldShare) / (1 - HowlHoldShare));
                frame = Mathf.Lerp(HowlPoseTop, HowlPoseFrom, back); rise = 1 - back;
            }
            Sample(Claw, frame/96, .06f);
            return rise;
        }

        private void ApplyRise()
        {
            if (_body == null) return;
            _body.localPosition = _bodyPosition + Vector3.up * (HowlRiseMetres * _rise);
            _body.localScale = _bodyScale * (1f + HowlSwell * _rise);
        }

        private void Sample(int state, float phase, float blend)
        {
            string parameter = state == Idle ? "IdlePhase" : state == Walk ? "WalkPhase" : state == Claw ? "ClawPhase"
                : state == Leap ? "LeapPhase" : state == Hit ? "HitPhase" : state == Howl ? "HowlPhase" : "DeathPhase";
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
