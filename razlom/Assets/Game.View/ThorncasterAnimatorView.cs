using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ПОЗА ШИПОМЁТА ИЗ ФАЗ SIM (как ForestWendigoAnimatorView). Клипы собирает
    /// ThorncasterBuilder: у каждого состояния время — параметр «&lt;Роль&gt;Phase»,
    /// и кадр клипа здесь считается из тиков действия (Simulation.Thorncaster),
    /// а не из часов Unity. Часы — часы Sim (тик − 1 + Alpha): пауза и хит-стоп
    /// держат позу сами.
    ///
    /// • Линия (LineCast, 75 кадров): замах 0–24 за 27 тиков — на кадре 24 шипы
    ///   рук входят в землю ровно в тик первого шипа линии; 24–60 (руки в
    ///   земле) — до последнего открытого шипа (ImpactTick); 60–75 — руки
    ///   выходят из земли за стойку до EndTick.
    /// • Всплеск (Burst, 36): 0–21 за замах 21 тик, 21–36 за стойку 15.
    /// • Выстрел (Shot, 33): 0–21 за замах 21 — на кадре 21 шип срывается с
    ///   кончика правой руки (сокет Muzzle_RightSpike), 21–33 за стойку 12.
    /// • Ходьба: клип снят на 2,2 м/с, цикл 1,76 м — фаза идёт от скорости Sim.
    ///   Разворот на месте переступает той же фазой.
    /// • Попадание вне действия — Hit (0,4 с). Смерть — Death по профилю вида
    ///   (EnemyPresentationProfile): тело ложится к концу падения и лежит до
    ///   растворения.
    ///
    /// Привязка: ArenaView зовёт <see cref="ThorncasterViewInstaller.Attach"/>
    /// при выдаче тела из пула. Если не позвал, вид сам находит свою сущность
    /// через ArenaView.TryGetEntityView — тело не стоит столбом.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class ThorncasterAnimatorView : MonoBehaviour
    {
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle"), Walk = Animator.StringToHash("Base Layer.Walk"),
            LineCast = Animator.StringToHash("Base Layer.LineCast"), Burst = Animator.StringToHash("Base Layer.Burst"),
            Shot = Animator.StringToHash("Base Layer.Shot"), Hit = Animator.StringToHash("Base Layer.Hit"),
            Death = Animator.StringToHash("Base Layer.Death");

        // Последние кадры дублей (export.json) и опорные кадры контактов.
        private const float IdleFrames = 60f, LineFrames = 75f, BurstFrames = 36f, ShotFrames = 33f, DeathFrames = 48f;
        private const float LineContactFrame = 24f, LineHoldEndFrame = 60f, BurstReleaseFrame = 21f, ShotReleaseFrame = 21f;
        private const float DeathGroundFrame = 39f;

        /// <summary>Руки держатся в земле хотя бы столько тиков: у линии из одного шипа кадры 24–60 не проскакивают разом.</summary>
        private const float MinLineHoldTicks = 6f;

        /// <summary>Клип ходьбы: 1,76 м за цикл (24 кадра на 2,2 м/с).</summary>
        private const float WalkCycleMetres = 1.76f, WalkSpeedThreshold = .06f;

        /// <summary>Разворот на месте: быстрее 30°/с — переступает фазой Walk, цикл на 150°.</summary>
        private const float TurnRateThreshold = 30f, TurnDegreesPerCycle = 150f, TurnHoldSeconds = .2f;

        private const float HitSeconds = .4f;

        public const string MuzzleName = "Muzzle_RightSpike";

        /// <summary>Выпуск шипа по замеру export.json, если сокета нет: в осях корня тела, взгляд +Z.</summary>
        public static readonly Vector3 MuzzleFallback = new Vector3(.12f, 1.9f, 1.42f);

        private Animator _animator;
        private TickDriver _driver;
        private ArenaView _arena;
        private Simulation _sim;
        private int _generation = -1, _entity = -1, _health, _state;
        private bool _dead, _burrowed;
        private float _deathClock, _idleClock, _walkPhase, _hitClock, _turnUntil;
        private Vector3 _lastFacing;

        /// <summary>Кончик правой руки-шипа, откуда срывается шип выстрела. Может быть null.</summary>
        public Transform Muzzle { get; private set; }

        /// <summary>Сущность Sim, к которой привязано тело; −1 — не привязано.</summary>
        public int Entity => _entity;

        /// <summary>Точка выпуска шипа в мире: сокет, а без него — замер export.json.</summary>
        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : transform.TransformPoint(MuzzleFallback);

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == MuzzleName) { Muzzle = t; break; }
        }

        /// <summary>Привязка к сущности. Зовёт ThorncasterViewInstaller.Attach (ArenaView) или сам вид.</summary>
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity;
            _arena = driver != null ? driver.GetComponent<ArenaView>() : null;
            _sim = driver != null ? driver.Sim : null;
            _generation = driver != null ? driver.Generation : -1;
            _dead = _burrowed = false;
            _deathClock = _idleClock = _walkPhase = 0f; _hitClock = 1f; _turnUntil = -1f;
            _lastFacing = Vector3.zero;
            _health = _sim != null && (uint)entity < (uint)_sim.Entities.Count ? _sim.Entities.Health[entity] : 0;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.speed = 1f;
                _animator.Rebind(); _animator.SetFloat("IdlePhase", 0f);
                _animator.Play(Idle, 0, 0f); _animator.Update(0f);
            }
            _state = Idle;
            // Эффекты линии и всплеска живут на арене; заводятся с первым телом, если ArenaView не завёл раньше.
            if (driver != null) ThorncasterViewInstaller.Prepare(driver.gameObject);
        }

        private void Update()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            if (!EnsureBound()) return;
            var sim = _sim;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            _animator.speed = 1f;
            float dt = Time.deltaTime;
            float tick = sim.Tick - 1 + _driver.Alpha;
            TrackBurrow();

            if (!sim.Entities.Alive[_entity] && !_burrowed) PlayDeath();
            if (_dead)
            {
                _deathClock += dt;
                SampleDeath();
                return;
            }
            if (_burrowed) { Sample(Idle, Mathf.Repeat(_idleClock / (IdleFrames / 30f), 1f), .15f); return; }

            // Поворот считается каждый кадр: иначе первый кадр после действия увидел бы весь угол разом.
            Vector3 facing = _driver.GetRenderFacing(_entity);
            float yaw = _lastFacing.sqrMagnitude > .5f && facing.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastFacing, facing, Vector3.up) : 0f;
            if (facing.sqrMagnitude > .5f) _lastFacing = facing;

            bool acting = sim.TryGetThorncasterAction(_entity, out var a);
            // Удар посреди действия замах не сбивает: там вспышка и отдача ArenaView.
            if (sim.Entities.Health[_entity] < _health && !acting) _hitClock = 0f;
            _health = sim.Entities.Health[_entity];
            _hitClock += dt;

            if (acting && a.Action != ThornAction.None)
            {
                _turnUntil = -1f;
                float time = Mathf.Max(0f, tick - a.StartTick);
                switch (a.Action)
                {
                    case ThornAction.Line: Sample(LineCast, LineFrame(a, time) / LineFrames, .06f); return;
                    case ThornAction.Burst: Sample(Burst, TwoPhase(a, time, BurstReleaseFrame, BurstFrames) / BurstFrames, .05f); return;
                    case ThornAction.Shot: Sample(Shot, TwoPhase(a, time, ShotReleaseFrame, ShotFrames) / ShotFrames, .06f); return;
                }
            }
            if (_hitClock < HitSeconds) { _turnUntil = -1f; Sample(Hit, _hitClock / HitSeconds, .04f); return; }

            float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            _idleClock += dt;
            if (speed > WalkSpeedThreshold)
            {
                _turnUntil = -1f;
                _walkPhase += speed * dt / WalkCycleMetres;
                Sample(Walk, Mathf.Repeat(_walkPhase, 1f), .13f);
                return;
            }
            // Разворот на месте: ноги переступают фазой Walk в темпе поворота, а не едут под Idle.
            if (dt > 1e-5f && Mathf.Abs(yaw) / dt > TurnRateThreshold) _turnUntil = Time.time + TurnHoldSeconds;
            if (Time.time < _turnUntil)
            {
                _walkPhase += Mathf.Abs(yaw) / TurnDegreesPerCycle;
                Sample(Walk, Mathf.Repeat(_walkPhase, 1f), .15f);
                return;
            }
            Sample(Idle, Mathf.Repeat(_idleClock / (IdleFrames / 30f), 1f), .15f);
        }

        /// <summary>
        /// Кадр линии: 0–24 за замах до первого шипа (27 тиков), 24–60 до
        /// последнего открытого шипа, 60–75 за стойку до конца действия.
        /// </summary>
        private static float LineFrame(in ThorncasterState a, float time)
        {
            float contact = Simulation.ThornLineWindupTicks;
            if (time < contact) return Mathf.Lerp(0f, LineContactFrame, time / contact);
            float end = Mathf.Max(contact + 1f, a.EndTick - a.StartTick);
            float hold = Mathf.Min(Mathf.Max(a.ImpactTick - a.StartTick, contact + MinLineHoldTicks), end - 1f);
            if (time < hold) return Mathf.Lerp(LineContactFrame, LineHoldEndFrame, (time - contact) / Mathf.Max(1f, hold - contact));
            return Mathf.Lerp(LineHoldEndFrame, LineFrames, Mathf.Clamp01((time - hold) / Mathf.Max(1f, end - hold)));
        }

        /// <summary>Замах 0–release за тики до контакта, остаток клипа — за стойку.</summary>
        private static float TwoPhase(in ThorncasterState a, float time, float release, float last)
        {
            float windup = Mathf.Max(1f, a.ImpactTick - a.StartTick), recovery = Mathf.Max(1f, a.EndTick - a.ImpactTick);
            if (time < windup) return Mathf.Lerp(0f, release, time / windup);
            return Mathf.Lerp(release, last, Mathf.Clamp01((time - windup) / recovery));
        }

        /// <summary>
        /// Смерть по профилю вида: кадры 0–39 (до касания земли) за время падения,
        /// 39–48 за стойку до растворения. Профиль без своего вида (Хранитель)
        /// падает быстрее клипа — тело всё равно ложится до растворения.
        /// </summary>
        private void SampleDeath()
        {
            var profile = EnemyPresentationProfile.Death(EnemyKind.ForestThorncaster);
            float fall = Mathf.Max(.05f, profile.FallSeconds), rest = Mathf.Max(.05f, profile.RestSeconds);
            float frame = _deathClock < fall
                ? Mathf.Lerp(0f, DeathGroundFrame, _deathClock / fall)
                : Mathf.Lerp(DeathGroundFrame, DeathFrames, Mathf.Clamp01((_deathClock - fall) / rest));
            Sample(Death, frame / DeathFrames, .07f);
        }

        /// <summary>Конец выживания: сущность ушла в землю, а не умерла — смерти не играть.</summary>
        private void TrackBurrow()
        {
            if (_burrowed) return;
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.Burrowed && events[i].Target == _entity) { _burrowed = true; return; }
        }

        private void Sample(int state, float phase, float blend)
        {
            _animator.SetFloat(Parameter(state), phase);
            if (_state == state) return;
            _state = state;
            _animator.CrossFadeInFixedTime(state, blend, 0, 0f);
        }

        private static string Parameter(int state)
            => state == Idle ? "IdlePhase" : state == Walk ? "WalkPhase" : state == LineCast ? "LineCastPhase"
                : state == Burst ? "BurstPhase" : state == Shot ? "ShotPhase" : state == Hit ? "HitPhase" : "DeathPhase";

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true; _deathClock = 0f;
        }

        /// <summary>
        /// Привязка жива: та же симуляция и поколение, и ArenaView держит это тело
        /// за этой сущностью. Иначе — поиск своей сущности (тело из пула ушло к
        /// другой или ArenaView не позвал Attach).
        /// </summary>
        private bool EnsureBound()
        {
            if (_driver == null)
            {
                _driver = FindAnyObjectByType<TickDriver>();
                if (_driver == null) return false;
            }
            var sim = _driver.Sim;
            if (sim == null) return false;
            if (_arena == null) _arena = _driver.GetComponent<ArenaView>();
            bool same = ReferenceEquals(sim, _sim) && _generation == _driver.Generation && (uint)_entity < (uint)sim.Entities.Count;
            if (same && (_arena == null || (_arena.TryGetEntityView(_entity, out var view) && view == transform))) return true;
            if (_arena == null) return false;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (sim.Entities.Kind[id] != EnemyKind.ForestThorncaster) continue;
                if (_arena.TryGetEntityView(id, out var candidate) && candidate == transform)
                {
                    Bind(_driver, id);
                    return true;
                }
            }
            return false;
        }
    }
}
