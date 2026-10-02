using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ТЕЛО ПО ФАЗАМ SIM (контракт клипов artifacts/tools/wf/boss-clip-spec.md, 02.10).
    ///
    /// Каждое состояние контроллера — Motion Time от параметра «&lt;Клип&gt;Phase»
    /// (ThicketMasterBuilder), как у Расщепеня и Корнехвата. Какой клип и какой кадр —
    /// решает чистое правило <see cref="ThicketMasterClipRules"/> по полям действия Sim:
    /// контакт клипа ложится ровно на тик удара (EnemyActionImpact) при любых замахах
    /// и стойках фаз. Часы — тики Sim с долей кадра (Tick − 1 + Alpha): пауза, хит-стоп
    /// и съёмка держат позу сами; под Песочными Часами (босс стоит) аниматор стоит.
    ///
    /// • Сон — Sleep (цикл) до пробуждения; Wake → Roar — вступление.
    /// • Ход — Walk фазой по пройденному пути: скорость Sim / шаг клипа (_walkStride,
    ///   сборщик пишет замер из отчёта клипов; до отчёта — ThicketMasterClipRules.DefaultWalkStride).
    /// • Разворот на месте (4,5°/тик Sim) — TurnL/TurnR, доля клипа = поворот / 90°;
    ///   без клипов — переступание фазой Walk.
    /// • Нырок: DiveIn за уход, потом тело спрятано (<see cref="IsBurrowed"/>), бугор
    ///   рисует вид боя по <see cref="MoundPosition"/>; выход — Emerge с кадра 0 на тике удара.
    /// • Смерть — Death по профилю EnemyPresentationProfile (касание боком на FallSeconds),
    ///   последний кадр держится; под конец показа лежащее тело уходит в землю (URP Lit
    ///   без растворения шейдером — как у Корнехвата). Убит под землёй — тело поднимается
    ///   из глубины, уже заваливаясь, а не встаёт над бугром целиком.
    /// • Песочные Часы: пока босс стоит, аниматор стоит; прошедший удар в стойке догоняет
    ///   <see cref="ThicketHourglassTrack"/> — после Часов кадр не прыгает.
    ///
    /// Корень принадлежит ArenaView (место, поворот по взгляду Sim, масштаб 1); вид
    /// двигает только узел модели под ним (погружение смерти) и рендеры (нырок).
    /// Нет клипа в контроллере — состояние подменяется Idle, исключений нет.
    /// Персонажа не высветляет: ни эмиссии, ни подъёма цвета.
    /// </summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class ThicketMasterAnimatorView : MonoBehaviour
    {
        /// <summary>Метров на цикл Walk в масштабе 1. Пишет ThicketMasterBuilder из отчёта клипов.</summary>
        [SerializeField] private float _walkStride = ThicketMasterClipRules.DefaultWalkStride;

        /// <summary>Поперечник контактной тени, м: тело 4,2 × 5,7 м, тень ArenaView — 0,88 м на всех.</summary>
        [SerializeField] private float _contactShadowMetres = 3.4f;

        /// <summary>Лежащее тело уходит в землю под конец смерти, м (лёжа на боку босс ниже 2 м, крона выше).</summary>
        public const float DeathSinkMetres = 2.4f;

        /// <summary>
        /// Убит под землёй (нырок, бугор бьётся): тело не выскакивает над бугром целиком, а
        /// поднимается из глубины роста за первые BurrowDeathRiseSeconds смерти, уже заваливаясь.
        /// </summary>
        private const float BurrowDeathDepth = 3.6f, BurrowDeathRiseSeconds = .6f;

        private const float WalkThreshold = .06f;
        private const float TurnRateThreshold = 30f, TurnHoldSeconds = .2f, TurnBlendSeconds = .18f;
        private const float TurnShuffleDegreesPerCycle = 160f;
        private const float LocomotionBlend = .2f, ActionBlend = .1f, PawChainBlend = .06f, DeathBlend = .12f;

        /// <summary>Имя тени из ArenaView.CreateContactShadow (там оно закрытое).</summary>
        private const string ContactShadowName = "Contact Shadow";
        private const int None = int.MinValue;

        private static readonly int[] StateHashes = new int[ThicketMasterClipRules.All.Length];
        private static readonly int[] PhaseHashes = new int[ThicketMasterClipRules.All.Length];

        private Animator _animator;
        private Transform _body;
        private Vector3 _bodyPosition;
        private TickDriver _driver;
        private Simulation _sim;
        private ArenaView _arena;
        private int _entity = -1, _generation = -1, _deathTick = None, _actionSerial, _actionStage;
        private int _state = -1;
        private readonly bool[] _has = new bool[ThicketMasterClipRules.All.Length];
        private float _walkPhase, _turnTravel, _turnSign, _turnUntil = -1f, _sink;
        private Vector3 _lastBody;
        private bool _isHidden, _shadowScaled, _deathSeen, _deathFromBurrow;
        private Renderer[] _hidden;
        private ThicketClipPose _pose;
        private float _tick;
        private ThicketHourglassTrack _hourglass;

        /// <summary>Сущность, к которой привязано тело; −1 — не привязано.</summary>
        public int Entity => _entity;

        /// <summary>Клип и кадр этого кадра — для съёмки и вида боя.</summary>
        public ThicketClipPose Pose => _pose;

        /// <summary>Тело под землёй (между концом DiveIn и выходом): видно только бугор.</summary>
        public bool IsBurrowed => _isHidden && _deathTick == None;

        /// <summary>Где бугор: тело Sim едет под землёй к герою. Вне нырка — место тела.</summary>
        public Vector3 MoundPosition => _driver != null && _entity >= 0 ? _driver.GetRenderPosition(_entity) : transform.position;

        /// <summary>Узел модели под корнем.</summary>
        public Transform Body => _body != null ? _body : transform;

        /// <summary>Кости для вида боя (знаки, VFX позже): найдены один раз на тело пула; null — нет кости.</summary>
        public Transform Head { get; private set; }
        public Transform CrownLeft { get; private set; }
        public Transform CrownRight { get; private set; }
        public Transform Bush { get; private set; }
        public Transform PawToeLeft { get; private set; }
        public Transform PawToeRight { get; private set; }
        public Transform Chest { get; private set; }

        /// <summary>Шаг клипа Walk, м на цикл (масштаб 1). Сборщик ставит его через SerializedObject.</summary>
        public float WalkStride => _walkStride;

        private void Awake()
        {
            EnsureHashes();
            _animator = GetComponentInChildren<Animator>();
            _body = _animator != null && _animator.transform != transform ? _animator.transform : null;
            if (_body != null) _bodyPosition = _body.localPosition;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "head": Head = t; break;
                    case "crown_L": CrownLeft = t; break;
                    case "crown_R": CrownRight = t; break;
                    case "bush": Bush = t; break;
                    case "leg_front_L_toe": PawToeLeft = t; break;
                    case "leg_front_R_toe": PawToeRight = t; break;
                    case "chest": Chest = t; break;
                }
            }
        }

        private static void EnsureHashes()
        {
            if (StateHashes[0] != 0) return;
            for (int i = 0; i < ThicketMasterClipRules.All.Length; i++)
            {
                ThicketClip clip = ThicketMasterClipRules.All[i];
                StateHashes[(int)clip] = Animator.StringToHash("Base Layer." + ThicketMasterClipRules.Name(clip));
                PhaseHashes[(int)clip] = Animator.StringToHash(ThicketMasterClipRules.PhaseParameter(clip));
            }
        }

        /// <summary>Привязка тела из пула к сущности: ArenaView зовёт при выдаче, без кадра в чужой позе.</summary>
        public void Bind(TickDriver driver, int entity)
        {
            _driver = driver;
            _sim = driver != null ? driver.Sim : null;
            _arena = driver != null ? driver.GetComponent<ArenaView>() : null;
            _generation = driver != null ? driver.Generation : -1;
            _entity = entity;
            _deathTick = None;
            _deathSeen = _deathFromBurrow = false;
            _hourglass = default;
            _actionSerial = _actionStage = 0;
            _walkPhase = _turnTravel = _turnSign = 0f;
            _turnUntil = -1f;
            _lastBody = Vector3.zero;
            ScaleContactShadow();
            SetHidden(false);
            SetSink(0f);
            _state = -1;
            if (_animator == null || _sim == null || (uint)entity >= (uint)_sim.Entities.Count) return;
            _animator.Rebind();
            for (int i = 0; i < _has.Length; i++) _has[i] = _animator.HasState(0, StateHashes[i]);
            if (_animator.runtimeAnimatorController == null || !_has[(int)ThicketClip.Idle])
                Debug.LogWarning("[thicketmaster] У тела нет контроллера с состоянием Idle — собери «Разлом → Босс → Хозяин Чащи → Собрать представление».");
            // Первый кадр — сразу в позе Sim (сон, действие), а не в Idle пула.
            Evaluate(0f, force: true);
            _animator.Update(0f);
        }

        /// <summary>Привязать вид тела, если он на нём есть (одна строка в ArenaView.BindNewEntities).</summary>
        public static void BindOn(GameObject body, TickDriver driver, int entity)
        {
            if (body != null && body.TryGetComponent(out ThicketMasterAnimatorView view)) view.Bind(driver, entity);
        }

        /// <summary>Смерть на тике события Death (ArenaView, вид боя). Без вызова вид сам видит «не жив».</summary>
        public void PlayDeath(int deathTick)
        {
            if (_deathTick != None) return;
            _deathTick = deathTick;
        }

        public void PlayDeath()
        {
            if (_sim != null) PlayDeath(_sim.Tick - 1);
        }

        /// <summary>Смерть тела, если на нём этот вид (одна строка в ArenaView, событие Death).</summary>
        public static void PlayDeathOn(Transform body, int deathTick)
        {
            if (body != null && body.TryGetComponent(out ThicketMasterAnimatorView view)) view.PlayDeath(deathTick);
        }

        private void Update()
        {
            if (_animator == null || !EnsureBound()) return;
            if (_driver.GameplayPaused) { _animator.speed = 0f; return; }
            Evaluate(Time.deltaTime, force: false);
        }

        private void Evaluate(float dt, bool force)
        {
            var entities = _sim.Entities;
            float tick = _sim.Tick - 1 + _driver.Alpha;
            _tick = tick;

            if (_deathTick == None && !entities.Alive[_entity]) _deathTick = _sim.Tick - 1;
            if (_deathTick != None)
            {
                // Смерть под землёй: тело было спрятано — оно поднимается из глубины, а не появляется над бугром.
                if (!_deathSeen) { _deathSeen = true; _deathFromBurrow = _isHidden; }
                _animator.speed = 1f;
                float seconds = Mathf.Max(0f, tick - _deathTick) / Simulation.TicksPerSecond;
                var profile = EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster);
                var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
                float frame = ThicketMasterClipRules.DeathFrame(seconds, beat.HitStopSeconds, profile.FallSeconds, profile.RestSeconds);
                Sample(new ThicketClipPose(ThicketClip.Death, frame), _deathFromBurrow ? 0f : DeathBlend);
                UpdateDeathSink(seconds, profile, beat);
                SetHidden(false);
                return;
            }

            // Песочные Часы: Sim держит босса и сдвигает все его сроки — поза стоит.
            bool frozen = _sim.ThicketMasterFrozenTicksLeft(_entity) > 0;
            if (frozen && !force) { _animator.speed = 0f; return; }
            _animator.speed = 1f;

            Vector3 body = _arena != null ? _arena.BodyFacing(_entity) : _driver.GetRenderFacing(_entity);
            float yaw = body.sqrMagnitude > .5f && _lastBody.sqrMagnitude > .5f
                ? Vector3.SignedAngle(_lastBody, body, Vector3.up) : 0f;
            if (body.sqrMagnitude > .5f) _lastBody = body;

            if (_sim.TryGetThicketMasterAction(_entity, out ThicketMasterState a))
            {
                ForgetTurn();
                // Часы в стойке: прошедший удар Sim не сдвигает — трекер догоняет его, кадр не прыгает.
                ThicketClipPose pose = ThicketMasterClipRules.Action(_hourglass.Apply(a), tick);
                // Вторая лапа двойной — сразу следом: короткая смесь из контакта первой.
                float blend = a.Action == ThicketMasterAction.Paw && a.Serial == _actionSerial && a.Stage != _actionStage
                    ? PawChainBlend : ActionBlend;
                // Выход из-под земли — кадр 0 сразу, без смеси со спрятанной позой нырка.
                if (pose.Clip == ThicketClip.Emerge && _isHidden) blend = 0f;
                _actionSerial = a.Serial; _actionStage = a.Stage;
                SetHidden(pose.Burrowed);
                Sample(pose, pose.Burrowed ? 0f : blend);
                return;
            }
            SetHidden(false);

            if (!_sim.ThicketMasterAwake(_entity))
            {
                ForgetTurn();
                Sample(new ThicketClipPose(ThicketClip.Sleep,
                    ThicketMasterClipRules.LoopPhase(tick, ThicketMasterClipRules.SleepFrames) * ThicketMasterClipRules.SleepFrames),
                    force ? 0f : LocomotionBlend);
                return;
            }

            float speed = entities.Velocity[_entity].Length.ToFloat() * Simulation.TicksPerSecond;
            if (speed > WalkThreshold)
            {
                ForgetTurn();
                _walkPhase += ThicketMasterClipRules.WalkCycles(speed * dt, _walkStride, transform.lossyScale.x);
                Sample(ThicketClipPose.WithPhase(ThicketClip.Walk, Mathf.Repeat(_walkPhase, 1f)), LocomotionBlend);
                return;
            }
            if (UpdateTurn(yaw, dt)) return;
            Sample(new ThicketClipPose(ThicketClip.Idle,
                ThicketMasterClipRules.LoopPhase(tick, ThicketMasterClipRules.IdleFrames) * ThicketMasterClipRules.IdleFrames),
                force ? 0f : LocomotionBlend);
        }

        /// <summary>Корпус крутится на месте (Sim — 4,5°/тик): разворот клипом по углу. False — не крутится.</summary>
        private bool UpdateTurn(float yaw, float dt)
        {
            float now = Time.time;
            bool rotating = dt > 1e-5f && Mathf.Abs(yaw) / dt > TurnRateThreshold;
            if (rotating)
            {
                float sign = Mathf.Sign(yaw);
                if (sign != _turnSign) { _turnSign = sign; _turnTravel = 0f; }
                _turnTravel += Mathf.Abs(yaw);
                _turnUntil = now + TurnHoldSeconds;
            }
            if (_turnSign == 0f || now > _turnUntil) { ForgetTurn(); return false; }
            ThicketClip clip = _turnSign < 0f ? ThicketClip.TurnL : ThicketClip.TurnR;
            if (_has[(int)clip])
            {
                float phase = ThicketMasterClipRules.TurnPhase(_turnTravel);
                Sample(new ThicketClipPose(clip, phase * ThicketMasterClipRules.TurnFrames), TurnBlendSeconds);
            }
            else
            {
                // Заплатка до клипов разворота: лапы переступают фазой Walk в темпе поворота.
                _walkPhase += Mathf.Abs(yaw) / TurnShuffleDegreesPerCycle;
                Sample(ThicketClipPose.WithPhase(ThicketClip.Walk, Mathf.Repeat(_walkPhase, 1f)), LocomotionBlend);
            }
            return true;
        }

        private void ForgetTurn() { _turnSign = 0f; _turnTravel = 0f; _turnUntil = -1f; }

        private void Sample(in ThicketClipPose pose, float blend)
        {
            _pose = pose;
            int clip = (int)pose.Clip;
            // Клипа нет в контроллере (пакет неполный) — покой той же долей, без исключений.
            if (!_has[clip]) clip = (int)ThicketClip.Idle;
            if (!_has[clip]) return;
            _animator.SetFloat(PhaseHashes[clip], clip == (int)pose.Clip ? pose.Phase
                : ThicketMasterClipRules.LoopPhase(_tick, ThicketMasterClipRules.IdleFrames));
            if (_state == clip) return;
            bool turn = IsTurn(clip) || IsTurn(_state);
            _state = clip;
            if (turn) blend = Mathf.Max(blend, TurnBlendSeconds);
            if (blend <= 0f) _animator.Play(StateHashes[clip], 0, 0f);
            else _animator.CrossFadeInFixedTime(StateHashes[clip], blend, 0, 0f);
        }

        /// <summary>
        /// Тень ArenaView (CreateContactShadow) — 0,88 м на всех при масштабе корня 1, а тело
        /// 4,2 × 5,7 м. Тень вешается на тело пула после его Awake, поэтому — при первой привязке.
        /// </summary>
        private void ScaleContactShadow()
        {
            if (_shadowScaled) return;
            foreach (Transform t in transform)
                if (t.name == ContactShadowName)
                {
                    t.localScale = Vector3.one * _contactShadowMetres;
                    _shadowScaled = true;
                }
        }

        private static bool IsTurn(int clip) => clip == (int)ThicketClip.TurnL || clip == (int)ThicketClip.TurnR;

        /// <summary>
        /// Без растворения шейдером (URP Lit) лежащее тело уходит в землю под конец
        /// показа смерти (такт убийства; потом ArenaView возвращает тело в пул).
        /// </summary>
        private void UpdateDeathSink(float seconds, EnemyDeathPresentation profile, EnemyKillBeat beat)
        {
            float still = Mathf.Max(beat.HitStopSeconds + .05f, profile.FallSeconds) + profile.RestSeconds;
            float to = Mathf.Max(.1f, beat.BodyGoneAt - .03f);
            float from = Mathf.Clamp(to - 1.2f, Mathf.Min(still, to - .1f), to - .1f);
            float k = Mathf.Clamp01((seconds - from) / Mathf.Max(.05f, to - from));
            float sink = k * k * DeathSinkMetres;
            if (_deathFromBurrow)
            {
                float rise = 1f - Mathf.Clamp01(seconds / BurrowDeathRiseSeconds);
                sink = Mathf.Max(sink, rise * rise * BurrowDeathDepth);
            }
            SetSink(sink);
        }

        private void SetSink(float metres)
        {
            if (_body == null || Mathf.Approximately(metres, _sink)) return;
            _sink = metres;
            _body.localPosition = _bodyPosition + Vector3.down * metres;
        }

        /// <summary>
        /// Прячет все рендеры тела, включая контактную тень и контур ArenaView, — под
        /// землёй тела нет. Выключенные запоминаются и возвращаются при показе и в пул.
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

        /// <summary>
        /// Привязка жива: та же симуляция и тело ArenaView этой сущности — всё ещё мы.
        /// Иначе ищем свою сущность сами (стенд без вызова Bind).
        /// </summary>
        private bool EnsureBound()
        {
            if (_arena == null) _arena = GetComponentInParent<ArenaView>();
            if (_driver == null && _arena != null) _driver = _arena.GetComponent<TickDriver>();
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return false;
            bool valid = _sim == sim && (uint)_entity < (uint)sim.Entities.Count && _generation == _driver.Generation;
            if (valid && (_arena == null || (_arena.TryGetEntityView(_entity, out Transform mine) && mine == transform))) return true;
            if (_arena == null) return false;
            for (int id = 1; id < sim.Entities.Count; id++)
                if (sim.Entities.Kind[id] == EnemyKind.ForestThicketMaster
                    && _arena.TryGetEntityView(id, out Transform view) && view == transform)
                {
                    Bind(_driver, id);
                    return true;
                }
            return false;
        }

        private void OnDisable()
        {
            // Возврат в пул: следующий владелец не должен получить тело спрятанным или в земле.
            SetHidden(false);
            SetSink(0f);
            if (_animator != null) _animator.speed = 1f;
            if (gameObject.activeInHierarchy) return;
            _deathTick = None;
            _deathSeen = _deathFromBurrow = false;
            _entity = -1;
            _sim = null;
        }
    }
}
