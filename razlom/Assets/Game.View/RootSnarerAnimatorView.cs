using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// КОРНЕХВАТ — ТЕЛО ПО ФАЗАМ SIM (пакет animation_r01 от 26.09, 30 кадров/с).
    ///
    /// Клипы стоят на месте (корень не едет), состояния контроллера ведутся
    /// своими параметрами фазы — как у Вендиго: кадр считает этот вид по часам
    /// Sim (тик − 1 + Alpha), поэтому пауза, хит-стоп и съёмка держат позу сами.
    ///
    /// • Удар (Slam, 72 кадра): кадры 0–15 — за 15 тиков позы, и кадр 15 (плиты
    ///   в земле) совпадает с тиком, в который Sim открывает круг. 15–60 —
    ///   плиты держатся в земле (корни выходят на тике 36, наказание до 72),
    ///   60–72 — выдёргивает плиты за последние 12 тиков стойки. Клип кончается
    ///   на кадре 0 Idle — выход из удара без рывка.
    /// • Ход (Walk, 16 кадров = 1,28 м при 2,4 м/с): фаза — пройденный путь,
    ///   поэтому ноги не скользят при любой скорости. Разворот на месте — та же
    ///   фаза от поворота корпуса: Idle под крутящимся корнем читался прокруткой.
    /// • Попадание (Hit, 12 кадров) — только вне удара: удар корнями не рвётся.
    /// • Смерть (Death, 45 кадров): на 25-м — брюхом в землю, с 32-го лежит.
    ///
    /// Материал тела — URP Lit (цвет, нормали, ORM), без тун-шейдера: у него нет
    /// _DeathFade, и растворения при смерти нет. Чтобы тело не пропадало разом в
    /// конце смерти, оно за последние доли секунды уходит в землю
    /// (<see cref="DeathSinkMetres"/>). Если на теле тун-материал с растворением —
    /// не уходит.
    ///
    /// Привязка — <see cref="Bind"/> из ArenaView, как у Вендиго. Без неё вид
    /// привязывается сам: находит свою сущность через ArenaView.TryGetEntityView
    /// и ставит на арену RootSnarerCombatView.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class RootSnarerAnimatorView : MonoBehaviour
    {
        // Кадры клипов пакета (export.json, animation_r01).
        public const int IdleFrames = 60, WalkFrames = 16, SlamFrames = 72, HitFrames = 12, DeathFrames = 45;
        public const int SlamContactFrame = 15, SlamRootsFrame = 36, SlamReleaseFrame = 60;
        public const float ClipFramesPerSecond = 30f;

        /// <summary>Путь за цикл Walk: 2,4 м/с × 16 кадров / 30 кадров/с.</summary>
        public const float WalkMetresPerCycle = 1.28f;

        /// <summary>Ход быстрее этого, м/с, — Walk; медленнее — Idle.</summary>
        private const float WalkThreshold = .06f;

        /// <summary>Разворот на месте: быстрее 30°/с без хода — ноги переступают, цикл Walk на 120°.</summary>
        private const float TurnRateThreshold = 30f, TurnShuffleDegreesPerCycle = 120f, TurnHoldSeconds = .2f;

        /// <summary>Выдёргивание плит — последние 12 тиков стойки (кадры 60–72).</summary>
        private const int ReleaseTicks = SlamFrames - SlamReleaseFrame;

        /// <summary>Тело уходит в землю под конец смерти, м (URP Lit без растворения).</summary>
        public const float DeathSinkMetres = .6f;

        /// <summary>Не раньше, чем тело легло (кадр 32 клипа Death).</summary>
        private const float DeathStillSeconds = 32f / ClipFramesPerSecond;

        private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle"),
            WalkState = Animator.StringToHash("Base Layer.Walk"), SlamState = Animator.StringToHash("Base Layer.Slam"),
            HitState = Animator.StringToHash("Base Layer.Hit"), DeathState = Animator.StringToHash("Base Layer.Death"),
            MendState = Animator.StringToHash("Base Layer.Mend");
        private static readonly int IdlePhase = Animator.StringToHash("IdlePhase"),
            WalkPhase = Animator.StringToHash("WalkPhase"), SlamPhase = Animator.StringToHash("SlamPhase"),
            HitPhase = Animator.StringToHash("HitPhase"), DeathPhase = Animator.StringToHash("DeathPhase"),
            MendPhase = Animator.StringToHash("MendPhase");

        /// <summary>
        /// «Волна из корней» (клип Mend, 50 кадров): плиты в земле с 8-го кадра,
        /// выброс волны на 30-м, выдёргивание 38–50. Действие Sim длится ровно 50
        /// тиков (30 сбора и 20 стойки), поэтому кадр = тик от начала.
        /// </summary>
        private const float MendFrames = 50f;
        private static readonly int DeathFadeId = Shader.PropertyToID("_DeathFade");

        private Animator _animator;
        private Transform _body;
        private Vector3 _bodyPosition;
        private TickDriver _driver;
        private ArenaView _arena;
        private int _entity = -1, _state, _health, _generation = -1, _slamSerial;
        private bool _dead, _burrowed, _hasDissolve, _slamDone, _combatInstalled;
        private float _deathClock, _idleClock, _walkPhase, _hitClock = 1f, _turnUntil, _sink;
        private Vector3 _lastFacing;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            // Погружение смерти двигает тело под корнем: корень ведёт ArenaView.
            _body = _animator != null && _animator.transform != transform ? _animator.transform : null;
            if (_body != null) _bodyPosition = _body.localPosition;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(DeathFadeId)) { _hasDissolve = true; break; }
        }

        /// <summary>Привязка к сущности. ArenaView зовёт при выдаче тела из пула и при уходе в землю.</summary>
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver; _entity = entity; _generation = driver != null ? driver.Generation : -1;
            _dead = false; _state = IdleState; _slamSerial = 0; _slamDone = false;
            _deathClock = _idleClock = _walkPhase = 0f; _hitClock = 1f; _turnUntil = -1f;
            _lastFacing = Vector3.zero;
            var sim = driver != null ? driver.Sim : null;
            bool known = sim != null && (uint)entity < (uint)sim.Entities.Count;
            _health = known ? sim.Entities.Health[entity] : 0;
            // Повторная привязка неживого — это уход в землю (ArenaView по Burrowed):
            // тело стоит в покое, смерть не играется.
            _burrowed = known && !sim.Entities.Alive[entity];
            SetSink(0f);
            if (_animator == null) return;
            _animator.Rebind(); _animator.SetFloat(IdlePhase, 0f); _animator.Play(IdleState, 0, 0f); _animator.Update(0f);
        }

        /// <summary>Смерть: клип Death с начала. ArenaView зовёт по событию Death; вид и сам видит «не жив».</summary>
        public void PlayDeath()
        {
            if (_dead || _burrowed) return;
            _dead = true; _deathClock = 0f;
        }

        private void Update()
        {
            if (_animator == null || !EnsureBound()) return;
            var sim = _driver.Sim;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            _animator.speed = 1f;
            float dt = Time.deltaTime;

            // Уход в землю по концу выживания — не смерть: тело стоит, ArenaView опускает его.
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.Burrowed && events[i].Target == _entity && !_dead)
                { _burrowed = true; Sample(IdleState, IdlePhase, 0f, .12f); }
            if (_burrowed) return;

            if (!sim.Entities.Alive[_entity]) PlayDeath();
            if (_dead)
            {
                _deathClock += dt;
                Sample(DeathState, DeathPhase, Mathf.Clamp01(_deathClock * ClipFramesPerSecond / DeathFrames), .08f);
                UpdateDeathSink(sim);
                return;
            }

            // Поворот считается каждый кадр: иначе первый кадр после удара увидел бы весь угол разом.
            Vector3 facing = _driver.GetRenderFacing(_entity);
            float yaw = _lastFacing.sqrMagnitude > .5f && facing.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastFacing, facing, Vector3.up) : 0f;
            if (facing.sqrMagnitude > .5f) _lastFacing = facing;

            bool acting = sim.TryGetRootSnarerAction(_entity, out var a);
            int health = sim.Entities.Health[_entity];
            if (health < _health && !acting) _hitClock = 0f;
            _health = health; _hitClock += dt;

            if (acting)
            {
                if (a.Serial != _slamSerial) { _slamSerial = a.Serial; _slamDone = false; }
                float time = Mathf.Max(0f, sim.Tick - 1 + _driver.Alpha - a.StartTick);
                if (a.Action == RootSnarerAction.Mend)
                {
                    float length = Mathf.Max(1f, a.EndTick - a.StartTick);
                    Sample(MendState, MendPhase, Mathf.Clamp01(time / length), .06f);
                }
                else Sample(SlamState, SlamPhase, SlamFrame(a, time) / SlamFrames, .06f);
                _turnUntil = -1f;
                return;
            }
            // Удар кончился стойкой — клип уже на кадре Idle, смесь короткая. Снят оглушением
            // или волоком посреди — поза далеко от Idle, смесь длиннее.
            float leave = .1f;
            if (_state == SlamState || _state == MendState)
            {
                leave = _slamDone ? .1f : .22f;
                _slamSerial = 0;
            }

            if (_hitClock * ClipFramesPerSecond < HitFrames)
            {
                Sample(HitState, HitPhase, Mathf.Clamp01(_hitClock * ClipFramesPerSecond / HitFrames), Mathf.Max(.05f, (_state == SlamState || _state == MendState) ? leave : 0f));
                return;
            }

            float speed = sim.Entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            _idleClock += dt;
            if (speed > WalkThreshold)
            {
                _walkPhase += speed * dt / WalkMetresPerCycle;
                _turnUntil = -1f;
                Sample(WalkState, WalkPhase, Mathf.Repeat(_walkPhase, 1f), (_state == SlamState || _state == MendState) ? leave : .12f);
                return;
            }
            // Разворот на месте: переступание фазой Walk от поворота корпуса.
            bool rotating = dt > 1e-5f && Mathf.Abs(yaw) / dt > TurnRateThreshold;
            if (rotating) _turnUntil = Time.time + TurnHoldSeconds;
            if (Time.time < _turnUntil)
            {
                _walkPhase += Mathf.Abs(yaw) / TurnShuffleDegreesPerCycle;
                Sample(WalkState, WalkPhase, Mathf.Repeat(_walkPhase, 1f), (_state == SlamState || _state == MendState) ? leave : .12f);
                return;
            }
            Sample(IdleState, IdlePhase, Mathf.Repeat(_idleClock * ClipFramesPerSecond / IdleFrames, 1f), (_state == SlamState || _state == MendState) ? leave : .13f);
        }

        /// <summary>
        /// Кадр клипа Slam на time тиков от начала позы. Тики берутся из действия
        /// Sim, а не из констант: поменяют длину позы или стойки — контакт клипа
        /// (кадр 15) всё равно встанет на тик круга, выдёргивание — на конец стойки.
        /// </summary>
        public static float SlamFrame(in RootSnarerState a, float time)
        {
            float slam = Mathf.Max(1f, a.SlamTick - a.StartTick);
            float end = Mathf.Max(slam + 1f, a.EndTick - a.StartTick);
            float release = Mathf.Clamp(end - ReleaseTicks, slam, end - 1f);
            if (time < slam) return Mathf.Lerp(0f, SlamContactFrame, time / slam);
            if (time < release) return Mathf.Lerp(SlamContactFrame, SlamReleaseFrame, (time - slam) / Mathf.Max(1f, release - slam));
            return Mathf.Lerp(SlamReleaseFrame, SlamFrames, Mathf.Clamp01((time - release) / Mathf.Max(1f, end - release)));
        }

        private void Sample(int state, int parameter, float phase, float blend)
        {
            _animator.SetFloat(parameter, phase);
            // Плиты уже выдернуты (последние кадры клипа близки к Idle) — выход короткой смесью.
            if (state == SlamState && phase >= (SlamFrames - 6f) / SlamFrames) _slamDone = true;
            if (state == MendState && phase >= (MendFrames - 4f) / MendFrames) _slamDone = true;
            if (_state == state) return;
            _state = state;
            if (blend <= 0f) _animator.Play(state, 0, 0f);
            else _animator.CrossFadeInFixedTime(state, blend, 0, 0f);
        }

        /// <summary>
        /// Без растворения тело уходит в землю между «лёг» и концом показа смерти
        /// (EnemyPresentationProfile: после него ArenaView возвращает тело в пул).
        /// </summary>
        private void UpdateDeathSink(Simulation sim)
        {
            if (_hasDissolve || _body == null) return;
            // Такт убийства (поток I, 29.09): тело уходит через ~0,44 с, а клип
            // ложится к 1,07 с — оседание от «лёг» сжималось в 50 мс перед исчезновением.
            // Теперь оно идёт вместе с распадом: от залпа до ухода тела.
            float total = EnemyPresentationProfile.Death(EnemyKind.ForestRootSnarer).TotalSeconds;
            var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestRootSnarer, false, false);
            float to = Mathf.Max(.1f, Mathf.Min(total, beat.BodyGoneAt) - .03f);
            float from = Mathf.Min(beat.BurstAt, to - .05f);
            float k = Mathf.Clamp01((_deathClock - from) / Mathf.Max(.05f, to - from));
            SetSink(k * k * DeathSinkMetres);
        }

        private void SetSink(float metres)
        {
            if (_body == null || Mathf.Approximately(metres, _sink)) return;
            _sink = metres;
            _body.localPosition = _bodyPosition + Vector3.down * metres;
        }

        /// <summary>
        /// Привязка есть и жива: та же симуляция и тело ArenaView этой сущности —
        /// всё ещё мы. Иначе ищем свою сущность сами (ArenaView без вызова Bind).
        /// </summary>
        private bool EnsureBound()
        {
            if (_arena == null) _arena = GetComponentInParent<ArenaView>();
            if (_driver == null && _arena != null) _driver = _arena.GetComponent<TickDriver>();
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return false;
            if (_arena != null && !_combatInstalled) { RootSnarerCombatView.EnsureOn(_arena.gameObject); _combatInstalled = true; }
            bool valid = (uint)_entity < (uint)sim.Entities.Count && _generation == _driver.Generation;
            if (valid && (_arena == null || (_arena.TryGetEntityView(_entity, out var mine) && mine == transform))) return true;
            if (_arena == null) return false;
            for (int id = 1; id < sim.Entities.Count; id++)
                if (_arena.TryGetEntityView(id, out var view) && view == transform)
                {
                    Bind(_driver, id);
                    return true;
                }
            return false;
        }
    }
}
