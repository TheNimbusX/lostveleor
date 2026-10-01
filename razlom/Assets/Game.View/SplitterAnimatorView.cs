using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Тело Расщепеня и его детёныша (то же тело в масштабе 0,6, ArenaView.SplitlingScale).
    /// Клипы ForestSplitter.fbx ведутся фазой, как у вендиго: каждое состояние
    /// контроллера — Motion Time от параметра «&lt;Роль&gt;Phase» (SplitterBuilder).
    ///
    /// Укус — фаза общего ближнего замаха Sim (TryGetEnemySwing): кадры 0–18
    /// идут за замах, 18–30 — за стойку. У Расщепеня это 18 + 12 тиков, у
    /// детёныша 12 + 8; контакт клипа (кадр 18, доля 0,6) у обоих ложится ровно
    /// на ImpactTick. Ходьба — по скорости Sim: клип снят под 3,1 м/с при шаге
    /// 1,0333 м на цикл, у детёныша шаг в 0,6 раза короче — его 4,2 м/с
    /// сами дают ногам нужный темп.
    ///
    /// СМЕРТЬ — НЕ ПАДЕНИЕ, А РАСКОЛ. Панцирь разделён на настоящие створки, клип Death —
    /// только короткая трещина (12 кадров). Вид играет её за CrackTicks тиков
    /// от тика смерти и на последнем кадре прячет тело: дальше распад продают
    /// обломки и пыль (SplitterCombatView). Детёныш создаётся симуляцией на тике раскола
    /// и сразу играет Pop — прыжок из пыли.
    ///
    /// ПЕРЕКАТ КЛУБКОМ (27.09). Сжатие RollCurl идёт за 30 тиков от начала до
    /// пуска (кадр 12 — фиксация полосы, 24–30 — дрожь). Катится — RollLoop
    /// петлёй, а всё тело вращается вокруг поперечной оси через точку пакета
    /// (spin_pivot, радиус 0,627 м) по пройденному пути: клубок катится, а не
    /// скользит. На остановке доворачивается до ближайшего «стоя» за 4 тика и
    /// играет RollUncurl (окно наказания, 30 тиков) или RollDizzy (о стену, 45).
    ///
    /// Все часы — тики Sim с долей кадра: пауза держит позу, съёмка повторяется.
    /// </summary>
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class SplitterAnimatorView : MonoBehaviour
    {
        /// <summary>За сколько тиков играется трещина Death (кадры 0–12) до раскола.</summary>
        public const int CrackTicks = Simulation.SplitterDeathReleaseTicks;

        /// <summary>Клип Pop: 10 кадров по тику, отрыв на 2-м, касание на 8-м.</summary>
        public const int PopTicks = 10, PopTakeoffTicks = 2, PopLandTicks = 8;

        private const float BiteFrames = 30f, BiteContactFrame = 18f;

        /// <summary>Метров на цикл Walk в масштабе 1 (export.json: stride_m_per_cycle).</summary>
        private const float WalkStride = 1.0333f;

        private const float IdleSeconds = 2f, HitSeconds = .4f, MoveThreshold = .06f;
        private const int None = int.MinValue;

        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle"),
            Walk = Animator.StringToHash("Base Layer.Walk"), Bite = Animator.StringToHash("Base Layer.Bite"),
            Hit = Animator.StringToHash("Base Layer.Hit"), Death = Animator.StringToHash("Base Layer.Death"),
            Pop = Animator.StringToHash("Base Layer.Pop"),
            RollCurl = Animator.StringToHash("Base Layer.RollCurl"), RollLoop = Animator.StringToHash("Base Layer.RollLoop"),
            RollUncurl = Animator.StringToHash("Base Layer.RollUncurl"), RollDizzy = Animator.StringToHash("Base Layer.RollDizzy");

        private static readonly int IdlePhase = Animator.StringToHash("IdlePhase"),
            WalkPhase = Animator.StringToHash("WalkPhase"), BitePhase = Animator.StringToHash("BitePhase"),
            HitPhase = Animator.StringToHash("HitPhase"), DeathPhase = Animator.StringToHash("DeathPhase"),
            PopPhase = Animator.StringToHash("PopPhase"),
            RollCurlPhase = Animator.StringToHash("RollCurlPhase"), RollLoopPhase = Animator.StringToHash("RollLoopPhase"),
            RollUncurlPhase = Animator.StringToHash("RollUncurlPhase"), RollDizzyPhase = Animator.StringToHash("RollDizzyPhase");

        // Перекат: точка вращения клубка в осях тела (export.json: spin_pivot_unity_local),
        // радиус качения, подъём клубка в качении, петля RollLoop (12 кадров = 12 тиков),
        // доворот до «стоя» на остановке.
        private static readonly Vector3 SpinPivot = new Vector3(0f, .546f, -.028f);
        private const float SpinRadius = .627f, SpinLift = .081f, RollLoopTicks = 12f, SettleTicks = 4f;

        private Animator _animator;
        private TickDriver _driver;
        private Simulation _sim;
        private int _entity = -1, _state, _health;
        private int _deathTick = None, _popTick = None;
        private float _idleClock, _walkPhase, _hitClock = 1f;
        private Renderer[] _hidden;
        private bool _isHidden;
        private bool _spinning;
        private Quaternion _bodyRotation = Quaternion.identity;
        private Vector3 _bodyPosition;
        private int _spinSerial;
        private float _spinAtStop;

        // Разворот на месте (ревью 01.10): стоит, а корпус крутится — к герою или обратно к
        // взгляду Sim после хода «телом по ходу» (EnemyBodyFacingRules). Ноги переступают
        // фазой Walk: полный цикл шага на 180° поворота.
        private const float TurnDegreesPerCycle = 180f;
        private ArenaView _arena;
        private EnemyTurnSteps _turnSteps;
        private Vector3 _lastBody;
        private float _frameYaw;

        /// <summary>Сущность, к которой привязано тело; −1 — не привязано.</summary>
        public int Entity => _entity;

        /// <summary>Узел модели под корнем: по нему ставятся половины коры в позе последнего кадра трещины.</summary>
        public Transform Body => _animator != null ? _animator.transform : transform;

        /// <summary>Тик раскола: трещина кончилась, тело спрятано. None, пока смерти нет.</summary>
        public int BreakTick => _deathTick == None ? None : _deathTick + CrackTicks;

        /// <summary>Тик, с которого детёныш виден и прыгает (раскол родителя). None — не из распада.</summary>
        public int PopStartTick => _popTick;

        public bool IsBoundTo(Simulation sim, int entity) => _driver != null && _sim == sim && _entity == entity;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _bodyRotation = _animator.transform.localRotation;
                _bodyPosition = _animator.transform.localPosition;
            }
        }

        /// <summary>
        /// Привязка тела из пула к сущности. Зовут ArenaView (BindNewEntities, уход в
        /// землю) и SplitterCombatView — тот сам привязывает тело, если ArenaView этого
        /// не сделал. Сбрасывает смерть, прыжок и спрятанное тело прежнего владельца.
        /// </summary>
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver;
            _sim = driver != null ? driver.Sim : null;
            _entity = entity;
            _deathTick = _popTick = None;
            _idleClock = _walkPhase = 0f;
            _hitClock = 1f;
            _arena = driver != null ? driver.GetComponent<ArenaView>() : null;
            _turnSteps.Reset(); _lastBody = Vector3.zero; _frameYaw = 0f;
            SetHidden(false);
            StopSpin();
            if (_sim == null || (uint)entity >= (uint)_sim.Entities.Count || _animator == null) return;
            _health = _sim.Entities.Health[entity];
            _animator.Rebind();
            _animator.SetFloat(IdlePhase, 0f);
            _animator.Play(Idle, 0, 0f);
            _state = Idle;
            _animator.Update(0f);

            // Детёныш, привязанный без события распада (вид боя не подключён): тик
            // распада восстанавливается по остатку выброса — тот идёт с тика рождения.
            var entities = _sim.Entities;
            if (_sim.SplitParentOf(entity) >= 0 && entities.ForcedKind[entity] == (byte)ForcedMotionKind.SplitPop
                && entities.ForcedTicksLeft[entity] > 0)
                BeginPop(_sim.Tick - 1 - (Simulation.SplitPopTicks - entities.ForcedTicksLeft[entity]));
        }

        /// <summary>Смерть без точного тика (совместимость с ArenaView): трещина с текущего тика.</summary>
        public void PlayDeath()
        {
            if (_deathTick != None || _sim == null) return;
            PlayDeath(_sim.Tick - 1);
        }

        /// <summary>
        /// Смерть на тике события Death. Дети существуют только после выпуска из родителя.
        /// </summary>
        public void PlayDeath(int deathTick)
        {
            _deathTick = deathTick;
        }

        /// <summary>Детёныш создан на тике splitTick: Pop начинается сразу, без повторной задержки.</summary>
        public void BeginPop(int splitTick)
        {
            _popTick = splitTick;
        }

        /// <summary>
        /// Применить состояние в этом же кадре. SplitterCombatView зовёт после Bind,
        /// PlayDeath и BeginPop из LateUpdate — аниматор этого кадра уже отработал.
        /// </summary>
        public void Refresh()
        {
            if (!Valid()) return;
            Evaluate(0f);
            _animator.Update(0f);
        }

        private bool Valid()
            => _driver != null && _sim != null && _driver.Sim == _sim && _animator != null
               && (uint)_entity < (uint)_sim.Entities.Count;

        private void Update()
        {
            if (!Valid()) return;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            _animator.speed = 1f;
            // Поворот показанного тела за кадр (ArenaView.BodyFacing — прошлый LateUpdate).
            Vector3 body = _arena != null ? _arena.BodyFacing(_entity) : Vector3.zero;
            _frameYaw = body.sqrMagnitude > .5f && _lastBody.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastBody, body, Vector3.up) : 0f;
            if (body.sqrMagnitude > .5f) _lastBody = body;
            Evaluate(Time.deltaTime);
            _frameYaw = 0f;
        }

        private void Evaluate(float dt)
        {
            var entities = _sim.Entities;
            float tick = _sim.Tick - 1 + _driver.Alpha;

            if (_deathTick != None)
            {
                float age = tick - _deathTick;
                Sample(Death, DeathPhase, Mathf.Clamp01(age / CrackTicks), .04f);
                // До своего прыжка убитый детёныш ещё спрятан; после раскола спрятан любой.
                SetHidden(age < 0f || age >= CrackTicks);
                return;
            }

            if (_popTick != None)
            {
                float age = tick - _popTick;
                if (age < 0f)
                {
                    // До раскола родителя детёныш сидит внутри него — его не видно.
                    SetHidden(true);
                    Sample(Pop, PopPhase, 0f, 0f);
                    _health = entities.Health[_entity];
                    return;
                }
                SetHidden(false);
                if (age < PopTicks)
                {
                    Sample(Pop, PopPhase, age / PopTicks, 0f);
                    _health = entities.Health[_entity];
                    return;
                }
                _popTick = None;
            }

            // Уход в землю — не смерть: тело уходит вниз стоя (ArenaView), здесь покой.
            if (!entities.Alive[_entity])
            {
                _idleClock += dt;
                Sample(Idle, IdlePhase, Mathf.Repeat(_idleClock / IdleSeconds, 1f), .15f);
                return;
            }

            if (_sim.TryGetSplitterRoll(_entity, out var roll))
            {
                EvaluateRoll(in roll, tick);
                _health = entities.Health[_entity];
                _hitClock = 1f;
                return;
            }
            StopSpin();

            if (_sim.TryGetEnemySwing(_entity, out var swing) && tick < swing.RecoverUntil)
            {
                float frame = tick < swing.ImpactTick
                    ? Mathf.Lerp(0f, BiteContactFrame, (tick - swing.StartTick) / Mathf.Max(1, swing.ImpactTick - swing.StartTick))
                    : Mathf.Lerp(BiteContactFrame, BiteFrames, (tick - swing.ImpactTick) / Mathf.Max(1, swing.RecoverUntil - swing.ImpactTick));
                Sample(Bite, BitePhase, frame / BiteFrames, .06f);
                _health = entities.Health[_entity];
                _hitClock = 1f;
                return;
            }

            if (entities.Health[_entity] < _health) _hitClock = 0f;
            _health = entities.Health[_entity];
            _hitClock += dt;
            if (_hitClock < HitSeconds)
            {
                Sample(Hit, HitPhase, _hitClock / HitSeconds, .04f);
                return;
            }

            float speed = entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            _idleClock += dt;
            if (speed > MoveThreshold)
            {
                // Детёныш — то же тело в 0,6: шаг короче, и его 4,2 м/с дают ногам
                // 2,26× темп клипа — ступни не скользят (export.json, child.playback_speed).
                float scale = Mathf.Max(.1f, transform.lossyScale.x);
                _walkPhase += speed * dt / (WalkStride * scale);
                _turnSteps.Reset();
                Sample(Walk, WalkPhase, Mathf.Repeat(_walkPhase, 1f), .12f);
            }
            else if (_turnSteps.Step(_frameYaw, dt, TurnDegreesPerCycle, ref _walkPhase))
                Sample(Walk, WalkPhase, Mathf.Repeat(_walkPhase, 1f), .12f);
            else Sample(Idle, IdlePhase, Mathf.Repeat(_idleClock / IdleSeconds, 1f), .15f);
        }

        /// <summary>
        /// Перекат: сжатие до пуска, клубок с вращением по пройденному пути,
        /// раскрытие или оглушение после остановки. Вращение — от места корня
        /// на экране (ArenaView его уже сгладил), поэтому клубок катится ровно
        /// с той скоростью, с какой едет.
        /// </summary>
        private void EvaluateRoll(in SplitterRollState roll, float tick)
        {
            switch (roll.Phase)
            {
                case SplitterRollPhase.Curl:
                case SplitterRollPhase.Locked:
                    StopSpin();
                    Sample(RollCurl, RollCurlPhase,
                        Mathf.Clamp01((tick - roll.StartTick) / Mathf.Max(1, roll.LaunchTick - roll.StartTick)), .06f);
                    return;
                case SplitterRollPhase.Rolling:
                {
                    Sample(RollLoop, RollLoopPhase, Mathf.Repeat((tick - roll.LaunchTick) / RollLoopTicks, 1f), 0f);
                    var origin = new Vector3(roll.Origin.X.ToFloat(), 0f, roll.Origin.Y.ToFloat());
                    var direction = new Vector3(roll.Direction.X.ToFloat(), 0f, roll.Direction.Y.ToFloat());
                    var here = transform.position; here.y = 0f;
                    float travelled = Mathf.Max(0f, Vector3.Dot(here - origin, direction));
                    float angle = travelled / SpinRadius * Mathf.Rad2Deg;
                    _spinSerial = roll.Serial;
                    _spinAtStop = angle;
                    ApplySpin(angle, 1f);
                    return;
                }
                default:
                {
                    bool dizzy = roll.Phase == SplitterRollPhase.Dizzy;
                    float age = tick - roll.StopTick;
                    // Доворот до ближайшего «стоя» за 4 тика с замедлением, пока
                    // первые кадры раскрытия ещё держат позу клубка.
                    if (_spinSerial == roll.Serial && _spinning)
                    {
                        float upright = Mathf.Ceil(_spinAtStop / 360f) * 360f;
                        float t = Mathf.Clamp01(age / SettleTicks);
                        float eased = 1f - (1f - t) * (1f - t);
                        if (t >= 1f) StopSpin();
                        else ApplySpin(Mathf.Lerp(_spinAtStop, upright, eased), 1f - eased);
                    }
                    if (dizzy) Sample(RollDizzy, RollDizzyPhase, Mathf.Clamp01(age / Simulation.SplitterRollDizzyTicks), 0f);
                    else Sample(RollUncurl, RollUncurlPhase, Mathf.Clamp01(age / Simulation.SplitterRollUncurlTicks), 0f);
                    return;
                }
            }
        }

        /// <summary>Поворот тела на angle градусов вокруг поперечной оси через точку клубка; lift — доля подъёма.</summary>
        private void ApplySpin(float angle, float lift)
        {
            if (_animator == null) return;
            var body = _animator.transform;
            var spin = Quaternion.AngleAxis(angle, Vector3.right);
            // Точка вращения — в осях корня (тело повёрнуто к +Z сборщиком): p' = P + R(p - P).
            body.localPosition = SpinPivot + spin * (_bodyPosition - SpinPivot) + Vector3.up * (SpinLift * lift);
            body.localRotation = spin * _bodyRotation;
            _spinning = true;
        }

        private void StopSpin()
        {
            if (!_spinning || _animator == null) return;
            _animator.transform.localRotation = _bodyRotation;
            _animator.transform.localPosition = _bodyPosition;
            _spinning = false;
        }

        private void Sample(int state, int parameter, float phase, float blend)
        {
            _animator.SetFloat(parameter, phase);
            if (_state == state) return;
            _state = state;
            if (blend <= 0f) _animator.Play(state, 0, phase);
            else _animator.CrossFadeInFixedTime(state, blend, 0, 0f);
        }

        /// <summary>
        /// Прячет все рендеры тела, включая контактную тень ArenaView. Выключенные
        /// запоминаются и возвращаются при показе, привязке и возврате тела в пул.
        /// </summary>
        private void SetHidden(bool hidden)
        {
            if (hidden == _isHidden) return;
            _isHidden = hidden;
            if (hidden)
            {
                var renderers = GetComponentsInChildren<Renderer>(false);
                int count = 0;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i].enabled) renderers[count++] = renderers[i];
                System.Array.Resize(ref renderers, count);
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
                _hidden = renderers;
                return;
            }
            if (_hidden == null) return;
            for (int i = 0; i < _hidden.Length; i++)
                if (_hidden[i] != null) _hidden[i].enabled = true;
            _hidden = null;
        }

        private void OnDisable()
        {
            // Возврат в пул: следующий владелец тела не должен получить его спрятанным или повёрнутым.
            SetHidden(false);
            StopSpin();
            // Выключен только сам вид (ArenaView на уходе в землю) — привязка остаётся.
            if (gameObject.activeInHierarchy) return;
            _deathTick = _popTick = None;
            _entity = -1;
            _sim = null;
        }
    }
}
