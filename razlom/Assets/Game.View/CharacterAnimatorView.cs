using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Presentation-мост к Animator. Он только показывает уже принятое Sim решение
    /// и никогда не вызывает damage, hit detection или движение сущности.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimatorView : MonoBehaviour
    {
        private static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveY = Animator.StringToHash("MoveY");
        private static readonly int TurnDirection = Animator.StringToHash("TurnDirection");
        private static readonly int Relaxed = Animator.StringToHash("Relaxed");
        private static readonly int Stunned = Animator.StringToHash("Stunned");
        private static readonly int LocomotionPlaybackSpeed = Animator.StringToHash("LocomotionPlaybackSpeed");
        private static readonly int AttackPlaybackSpeed = Animator.StringToHash("AttackPlaybackSpeed");
        private static readonly int UpperBodyEmptyState = Animator.StringToHash("UpperBody_Empty");
        private static readonly int LowerBodyEmptyState = Animator.StringToHash("LowerBody_Empty");
        private static readonly int UpperBodyAttackAState =
            Animator.StringToHash("UpperBody Combat.Saber_A_v5");
        private static readonly int UpperBodyAttackBState =
            Animator.StringToHash("UpperBody Combat.Saber_B_v5");
        private static readonly int LowerBodyAttackAState =
            Animator.StringToHash("LowerBody Combat.Lower_Saber_A_v5");
        private static readonly int LowerBodyAttackBState =
            Animator.StringToHash("LowerBody Combat.Lower_Saber_B_v5");
        private static readonly int AnchorLeapTrigger = Animator.StringToHash("AnchorLeap");
        private static readonly int AnchorSweepTrigger = Animator.StringToHash("AnchorSweep");
        private static readonly int ChainStepTrigger = Animator.StringToHash("ChainStep");
        private static readonly int AnchorLeapState = Animator.StringToHash("Base Layer.AnchorLeap_v5");
        private static readonly int AnchorSweepState = Animator.StringToHash("Base Layer.AnchorSweep_v5");
        private static readonly int ChainStepState = Animator.StringToHash("Base Layer.ChainStep_v5");
        private static readonly int PelagDeathState = Animator.StringToHash("Base Layer.Death_v5");
        private static readonly int OrvillSwordAttack = Animator.StringToHash("SwordAttack");
        private static readonly int OrvillShieldBash = Animator.StringToHash("ShieldBash");
        private static readonly int OrvillHighBlock = Animator.StringToHash("HighBlock");
        private static readonly int OrvillGuardBreak = Animator.StringToHash("GuardBreak");
        private static readonly int OrvillHitLeft = Animator.StringToHash("HitLeft");
        private static readonly int OrvillHitRight = Animator.StringToHash("HitRight");
        private static readonly int OrvillKnockback = Animator.StringToHash("Knockback");
        private static readonly int OrvillDeath = Animator.StringToHash("Death");
        private static readonly int OrvillLocomotionState =
            Animator.StringToHash("Base Layer.Locomotion");
        private static readonly int OrvillDeathState = Animator.StringToHash("Base Layer.DeathBack");
        private const string UpperBodyLayerName = "UpperBody Combat";
        private const string LowerBodyLayerName = "LowerBody Combat";

        // Simulation owns the contact tick. Derive presentation timings from
        // that value so an animation cannot silently drift from Damage.
        //
        // ЗАМАХ РАЗНЫЙ У ГЕРОЯ И У ВРАГА, поэтому и контакт разный. Общая
        // константа здесь означала бы, что анимация врага бьёт на 0.2 с, а урон
        // приходит на 0.4 — то есть клинок проходит сквозь цель за две десятых
        // до того, как что-то произойдёт. Ровно та рассинхронизация, ради
        // устранения которой замах и разносили с контактом.
        private static readonly float PlayerAttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        private static readonly float EnemyAttackContactTime =
            Simulation.EnemyAttackWindupTicks / (float)Simulation.TicksPerSecond;
        // Сколько гаснет верхний слой после конца показа удара. Короче 0.1 с
        // читается обрывом, длиннее 0.2 — персонаж «доносит» саблю в бег.
        // A slightly longer release lets the shoulders and saber settle back
        // into locomotion instead of snapping to the idle pose on the first
        // frame after contact. The authored attack remains fully readable;
        // only the masked layer's return is softened.
        private const float UpperBodyReleaseSeconds = 0.18f;
        public const float BasicAttackClipDuration = 0.64f;
        private const float BasicAttackPresentationDuration =
            Simulation.PlayerBaseAttackCycleTicks / (float)Simulation.TicksPerSecond + 0.035f;
        private static readonly float AttackAContactNormalized = PlayerAttackContactTime / BasicAttackClipDuration;
        private static readonly float AttackBContactNormalized = AttackAContactNormalized;
        public const float WhirlwindClipDuration = 0.8f;
        public const float WhirlwindContactTime = Simulation.WhirlwindContactDelayTicks / (float)Simulation.TicksPerSecond;
        public const float WhirlwindTrailStart = 0.12f;
        public const float WhirlwindTrailEnd = 0.49f;
        private const float WhirlwindRecoveryStart = 0.60f;
        private const float ChainStepPresentationDuration = 8f / 30f;
        private const float AnchorLeapPlaybackSpeed = 1f;
        private const float AnchorSweepPlaybackSpeed = 1f;
        private const float ChainStepPlaybackSpeed = 1f;

        // Imported Orvill clips have authored contact poses on frames 17 and
        // 20. Per-clip playback keeps either pose on the deterministic 12/30 s
        // Sim contact without moving damage authority into presentation.
        private const float OrvillSwordAuthoredContactTime = 17f / 30f;
        private const float OrvillSwordAuthoredDuration = 33f / 30f;
        private const float OrvillShieldAuthoredContactTime = 20f / 30f;
        private const float OrvillShieldAuthoredDuration = 42f / 30f;
        private const float OrvillHitPresentationDuration = 0.57f;

        // GuardWalk covers about 0.387 m over a 0.20 s planted-foot phase:
        // 1.93 m/s at 1x versus Orvill's deterministic 3.5 m/s full speed.
        /// <summary>
        /// Скорость проигрывания бега моба. ВЫЧИСЛЕНА, А НЕ ПОДОБРАНА.
        ///
        /// Стояло 1.81 — число от старого Орвилла, и на Лесном страже оно
        /// давало ровно тот эффект, который владелец назвал телепортом: ноги
        /// шли вдвое быстрее тела, и корпус визуально утаскивало назад. Тела
        /// на карте при этом не прыгали — прыгала картинка.
        ///
        /// Замер клипа `Forest_Guardian@Run` в Blender:
        ///   цикл 26 кадров при 30 fps = 0.867 с
        ///   таз проезжает за цикл 1.04 единицы исходника
        ///   в игре это ×2.4 (OrvillScale) = 2.49 м
        ///   значит клип «едет» 2.49 / 0.867 = 2.87 м/с
        /// Моб движется 3.5 м/с (EnemyBaseMoveSpeed), отсюда 3.5 / 2.87 = 1.22.
        ///
        /// ЗАВИСИТ ОТ МАСШТАБА. Меняешь OrvillScale или клип бега — пересчитай
        /// по этим же четырём строкам, а не крути на глаз.
        ///
        /// СКОРОСТЬ ТЕЛА БОЛЬШЕ НЕ ПЕРЕПИСЫВАЕТСЯ СЮДА ЧИСЛОМ. Здесь стояло
        /// готовое 1.22, посчитанное под 3.5 м/с, и в тот день, когда владелец
        /// попросил замедлить мобов, ноги остались бы крутиться под старую
        /// скорость — то есть вернулось бы скольжение стопы, ради устранения
        /// которого число и считали. Делитель — замер клипа, он от скорости
        /// тела не зависит; делимое берётся у симуляции.
        private const float OrvillRunClipGroundSpeed = 2.87f;
        private static readonly float OrvillLocomotionBasePlaybackSpeed =
            Simulation.EnemyBaseMoveSpeed.ToFloat() / OrvillRunClipGroundSpeed;
        private const float OrvillLocomotionMinPlaybackSpeed = 0.42f;

        /// <summary>
        /// Жёсткость сглаживания темпа ног. Восемь — примерно восьмая доля
        /// секунды на догон: рывок скорости не проходит в ноги, а настоящее
        /// ускорение с места читается без задержки.
        /// </summary>
        private const float OrvillLocomotionSmoothing = 8f;

        /// <summary>
        /// Сколько ходьбы подмешивается мобу, пока он разворачивается на месте.
        /// Треть: ноги переступают, но тело никуда не едет.
        /// </summary>
        private const float TurnShuffleMoveSpeed = 0.33f;
        // Клипы Орвилла подгоняются под ЕГО контакт, а не под геройский.
        private static readonly float OrvillSwordPlaybackSpeed =
            OrvillSwordAuthoredContactTime / EnemyAttackContactTime;
        private static readonly float OrvillSwordPresentationDuration =
            OrvillSwordAuthoredDuration / OrvillSwordPlaybackSpeed;
        private static readonly float OrvillShieldPlaybackSpeed =
            OrvillShieldAuthoredContactTime / EnemyAttackContactTime;
        private static readonly float OrvillShieldPresentationDuration =
            OrvillShieldAuthoredDuration / OrvillShieldPlaybackSpeed;

        private Animator _animator;

        /// <summary>Только для диагностики скачков — см. ArenaView.TraceMob.</summary>
        public Animator Animator => _animator;
        private SpriteCharacterVisual _spriteVisual;
        private Faction _faction;
        private EnemyKind _enemyKind;
        private bool IsRootSwarm => _enemyKind == EnemyKind.ForestRootSwarm;

        public void SetEnemyKind(EnemyKind kind) => _enemyKind = kind;

        /// <summary>Контакт той стороны, которую показывает этот вид.</summary>
        private float AttackContactTime => _faction == Faction.Wole
            ? PlayerAttackContactTime
            : IsRootSwarm ? Simulation.RootSwarmAttackWindupTicks / (float)Simulation.TicksPerSecond
                : EnemyAttackContactTime;
        private int _attackVariant;
        private int _orvillAttackCount;
        private float _actionProtectedUntil;
        private float _lastHitAt = -10f;
        private float _attackWarpStartedAt;
        private bool _attackWarpActive;
        private bool _attackPresentationActive;
        private float _attackPresentationUntil;
        private bool _abilityPresentationActive;
        private bool _sweepLocomotion;
        private bool _leapLocomotion;

        /// <summary>
        /// Сохраняем контакт и первые 0.10 с амортизации. Если маршрут уже
        /// движет героя, дальше посадка перетекает в первый шаг: ожидание
        /// последнего хвоста клипа держало бегущее тело в позе приземления.
        /// </summary>
        private const float LeapRunBlend = 0.18f;
        private const float LeapRunHandoff = PelagAbilityTiming.LeapRecovery - PelagAbilityTiming.LeapArrival - 0.10f;
        private float _abilityPresentationUntil;
        private bool _abilityUsesLowerBodyLayer;
        private int _abilityDefinitionId;
        private bool _cycloneReleasing;
        private int _upperBodyLayer = -1;
        private int _saberStanceLayer = -1;
        private int _saberFootworkLayer = -1;
        private PelagEquipmentView _stanceEquipment;
        private int _lowerBodyLayer = -1;
        private bool _combatReady;
        private bool _locomotionMoving;
        private float _orvillLocomotionPlaybackSpeed = 1f;
        private float _orvillHitPresentationUntil;
        [SerializeField, Range(.0f, .08f)] private float _cleaveHitStop = .045f;
        private float _cleaveHitTick = -1f;

        private bool _cleaveContactConfirmed;

        private EnemyContactPose _contactPose;
        public float DeathDuration => _faction == Faction.Wole ? 1.55f : EnemyPresentationProfile.Death(_enemyKind).TotalSeconds;
        public void PlayContactPose(Vector3 direction, float strength, bool heavy = false)
        { if (!IsDead) _contactPose?.Hit(direction, strength, _enemyKind, heavy); }

        public bool CleaveActive => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.CleaveId;

        public void ConfirmCleaveContact()
        {
            if (IsDead || _animator == null || !_abilityPresentationActive
                || _abilityDefinitionId != AbilityDefinition.CleaveId || _cleaveContactConfirmed) return;
            _cleaveContactConfirmed = true;
            // Задерживается только поза рубящего героя; симуляция и управление продолжаются.

            _cleaveHitTick = _cycloneDriver.Sim.Tick - 1;

            _actionProtectedUntil = Mathf.Max(_actionProtectedUntil, _abilityPresentationUntil);
            // Only the Cleave time parameter is held; other animator speed modifiers are untouched.
        }
        public bool IsDead { get; private set; }
        public bool UsesSprites => _spriteVisual != null;
        public bool CombatReady => _combatReady;
        public bool BasicAttackActive => _faction == Faction.Wole && _attackPresentationActive && !IsDead;
        public bool WhirlwindActive => _faction == Faction.Wole && _abilityPresentationActive
            && _abilityDefinitionId == AbilityDefinition.WhirlwindId && !IsDead;
        private bool CyclonePresentationActive => _abilityPresentationActive
            && _abilityDefinitionId == AbilityDefinition.ChainCycloneId && !IsDead;
        private bool MaskedAbilityActive => WhirlwindActive || CyclonePresentationActive;
        private float MaskedAbilityWeight => WhirlwindActive ? WhirlwindWeight
            : !_cycloneReleasing ? 1f : Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((_abilityPresentationUntil - Time.time) / 0.16f));
        public bool AnchorAbilityActive => _faction == Faction.Wole && _abilityPresentationActive && !_abilityUsesLowerBodyLayer && !IsDead;
        public bool LeapFacingRecovery => _leapLocomotion && !IsDead
            && Time.time < _abilityPresentationUntil + 0.12f;
        public float AnchorFacingWeight => (_sweepLocomotion || _leapLocomotion)
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_abilityPresentationUntil - Time.time)
                / (_leapLocomotion ? LeapRunHandoff : .20f))) : 1f;
        public float WhirlwindElapsed => WhirlwindActive ? WhirlwindClipDuration - (_abilityPresentationUntil - Time.time) : 0f;
        private float WhirlwindWeight => 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(WhirlwindRecoveryStart, WhirlwindClipDuration, WhirlwindElapsed));
        public bool LocomotionMoving => _locomotionMoving;
        public bool RollActive => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.DashId && !IsDead;
        public bool HasCommittedAction => IsDead || _attackPresentationActive || _abilityPresentationActive;
        public float TurnAngularSpeed { get; private set; }
        private float _turnTravel;
        private float _lastTurnAt = -10f;
        private float _turnSign;
        private static readonly int TurnPhase = Animator.StringToHash("TurnPhase");
        private static readonly int LeftTurnState = Animator.StringToHash("Base Layer.TurnLeft_v5");
        private static readonly int RightTurnState = Animator.StringToHash("Base Layer.TurnRight_v5");

        /// <summary>
        /// Sets Pelag's presentation-only combat intent. The simulation owns
        /// gameplay state; this flag only selects the relaxed/combat Animator
        /// idle and is intentionally ignored for other factions.
        /// </summary>
        public void SetCombatReady(bool combatReady)
        {
            if (_faction != Faction.Wole) return;
            _combatReady = combatReady;
            if (_animator != null) _animator.SetBool(Relaxed, !combatReady);
        }

        public void Configure(Faction faction)
        {
            _faction = faction;
            _spriteVisual = GetComponentInChildren<SpriteCharacterVisual>(true);
            _animator = GetComponent<Animator>();
            if (_spriteVisual != null) return;
            if (_animator == null)
                Debug.LogError($"[Разлом] У prefab {name} отсутствует Animator.", this);
            else
            {
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (faction == Faction.Orvill)
                {
                    _contactPose = GetComponent<EnemyContactPose>() ?? gameObject.AddComponent<EnemyContactPose>();
                    _contactPose.Initialize();
                }
                _upperBodyLayer = _animator.GetLayerIndex(UpperBodyLayerName);
                _saberStanceLayer = _animator.GetLayerIndex("Saber Stance");
                _saberFootworkLayer = _animator.GetLayerIndex("Saber Footwork");
                _stanceEquipment = GetComponent<PelagEquipmentView>();
                _lowerBodyLayer = _animator.GetLayerIndex(LowerBodyLayerName);
            }
        }

        public void ResetForSpawn()
        {
            PoolTag poolTag = GetComponent<PoolTag>();
            ResetForSpawn(poolTag != null ? poolTag.Slot : 0);
        }

        public void ResetForSpawn(int presentationId)
        {
            IsDead = false;
            _contactPose?.Clear();
            _attackVariant = 0;
            _orvillAttackCount = 0;
            _actionProtectedUntil = 0f;
            _lastHitAt = -10f;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _abilityUsesLowerBodyLayer = false;
            _cycloneWasActive = false;
            _cycloneReleasing = false;
            _abilityDefinitionId = 0;
            _cleaveContactConfirmed = false;
            _chainPresentationVariant = 0;
            _cyclonePhase = -1;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _combatReady = false;
            _locomotionMoving = false;
            _turnTravel = 0f;
            _lastTurnAt = -10f;
            _turnSign = 0f;
            TurnAngularSpeed = 0f;
            _orvillLocomotionPlaybackSpeed = 1f;
            _orvillHitPresentationUntil = 0f;
            StopAttackWarp();
            if (_spriteVisual != null)
            {
                _spriteVisual.ResetForSpawn();
                if (_faction == Faction.Wole) SetCombatReady(false);
                return;
            }
            if (_animator == null) return;

            _animator.Rebind();
            _animator.Update(0f);
            _animator.speed = 1f;
            if (_faction == Faction.Wole) SetCombatReady(false);
            _animator.SetBool(Stunned, false);
            _animator.SetFloat(MoveSpeed, 0f);
            if (_faction == Faction.Wole)
            {
                _animator.SetFloat(TurnDirection, 0f);
                _animator.SetFloat(MoveX, 0f);
                _animator.SetFloat(MoveY, 1f);
                _animator.SetFloat(LocomotionPlaybackSpeed, 1f);
                _animator.SetFloat(AttackPlaybackSpeed, 1f);
                if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
                // Свежезаспавненный герой не бьёт, значит верхний слой обязан
                // молчать: иначе первый же удар оставит его залипшим.
                if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
            }
            else if (_animator.HasState(0, OrvillLocomotionState))
            {
                _animator.Play(
                    OrvillLocomotionState, 0,
                    DeterministicLocomotionPhase(presentationId));
                _animator.Update(0f);
            }
        }

        private TickDriver _cycloneDriver;
        private bool _cycloneWasActive;
        private int _cyclonePhase = -1;
        private int _cycloneAnimationTick;
        private float _cycloneAnimationTravel, _cycloneAnimationStep;

        public bool PlayEquipmentGesture(float phase, bool drawing)
        {
            if (_animator == null || _upperBodyLayer < 0 || IsDead || HasCommittedAction) return false;
            int state = Animator.StringToHash(drawing ? "UpperBody Combat.SaberDraw" : "UpperBody Combat.SaberStow");
            if (!_animator.HasState(_upperBodyLayer, state)) return false;
            // Вход и выход жеста смешиваются с текущей позой корпуса, иначе
            // первый ключ мгновенно переносит кисть с правого бока на левый.
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / .20f))
                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - phase) / .20f));
            _animator.SetLayerWeight(_upperBodyLayer, weight);
            _animator.Play(state, _upperBodyLayer, phase);
            UpdateStanceFootwork(phase);
            _animator.Update(0f);
            return true;
        }

        private void UpdateStanceFootwork(float phase)
        {
            if (_saberFootworkLayer < 0) return;
            bool stepping = !IsDead && !HasCommittedAction && !_locomotionMoving
                && phase > .55f && phase < 1f;
            _animator.SetLayerWeight(_saberFootworkLayer, stepping ? 1f : 0f);
            if (stepping)
            {
                // The final planted pose is also the idle's first frame.
                // Start its breathing cycle only after both steps land.
                _animator.Play("Saber Stance.SaberIdle", _saberStanceLayer, 0f);
                _animator.Play("Saber Footwork.StanceSteps", _saberFootworkLayer, (phase - .55f) / .45f);
            }
        }

        private void UpdateCycloneAnimation()
        {
            if (_faction != Faction.Wole || _animator == null || IsDead) return;
            if (_cycloneDriver == null) _cycloneDriver = FindAnyObjectByType<TickDriver>();
            Simulation sim = _cycloneDriver != null ? _cycloneDriver.Sim : null;
            bool active = sim != null && sim.CycloneActive;
            if (active)
            {
                _abilityDefinitionId = AbilityDefinition.ChainCycloneId;
                _cycloneReleasing = false;
                _attackPresentationActive = false;
                _abilityUsesLowerBodyLayer = true;
                _abilityPresentationActive = true;
                _abilityPresentationUntil = Time.time + 0.3f;
                _actionProtectedUntil = _abilityPresentationUntil;
                _sweepLocomotion = _leapLocomotion = false;
                if (_cyclonePhase < 0)
                {
                    _cycloneAnimationTick = sim.Tick;
                    _cycloneAnimationTravel = sim.CycloneTravel.ToFloat();
                    _cycloneAnimationStep = 0f;
                }
                else if (_cycloneAnimationTick != sim.Tick)
                {
                    float travel = sim.CycloneTravel.ToFloat();
                    _cycloneAnimationStep = (travel - _cycloneAnimationTravel)
                        / Mathf.Max(1, sim.Tick - _cycloneAnimationTick);
                    _cycloneAnimationTick = sim.Tick;
                    _cycloneAnimationTravel = travel;
                }
                int phase = sim.CycloneElapsedTicks < 6 ? 0 : 1;
                if (phase != _cyclonePhase)
                {
                    if (_cyclonePhase < 0)
                    {
                        CancelUpperBodyAttack(0.04f);
                        _animator.CrossFadeInFixedTime(_locomotionMoving ? "Run_v5" : "CombatIdle_v5", 0.08f, 0);
                    }
                    PlayCycloneLayers(phase == 0 ? "CycloneStart" : "CycloneLoop", 0f, true);
                    _cyclonePhase = phase;
                }
                if (phase == 1)
                {
                    // Поза и якорь используют одну интерполяцию между тиками, без ступенек на 30 Гц.
                    float travel = _cycloneAnimationTravel - _cycloneAnimationStep * (1f - _cycloneDriver.Alpha);
                    PlayCycloneLayers("CycloneLoop", Mathf.Repeat(travel / (2f * Mathf.PI), 1f), false);
                }
            }
            else if (_cycloneWasActive)
            {
                // Новый подтверждённый каст уже выбрал своё состояние и не ждёт возврата цепи.
                bool replaced = false;
                if (sim != null)
                    foreach (var ev in _cycloneDriver.FrameEvents)
                        if (ev.Type == SimEventType.AbilityCast && ev.Source == Simulation.PlayerId) replaced = true;
                if (!replaced)
                {
                    _cycloneReleasing = true;
                    _abilityPresentationUntil = Time.time + 0.30f;
                    PlayCycloneLayers("CycloneEnd", 0f, true);
                }
                _cyclonePhase = -1;
            }
            _cycloneWasActive = active;
        }

        private void PlayCycloneLayers(string state, float phase, bool blend)
        {
            for (int i = 0; i < 2; i++)
            {
                int layer = i == 0 ? _upperBodyLayer : _lowerBodyLayer;
                if (layer < 0) continue;
                if (blend) _animator.CrossFadeInFixedTime(state, state == "CycloneEnd" ? .10f : .06f, layer, phase);
                else _animator.Play(state, layer, phase);
            }
        }

        private void UpdateCleaveAnimation()
        {
            if (!_abilityPresentationActive || _abilityDefinitionId != AbilityDefinition.CleaveId) return;
            var sim = _cycloneDriver != null ? _cycloneDriver.Sim : null;
            if (sim == null || !sim.CleaveActive || IsDead)
            {
                _cleaveHitTick = -1f;
                _abilityPresentationActive = false;
                _actionProtectedUntil = 0f;
                ReleaseUpperBodyToLocomotion(.12f);
                return;
            }
            float tick = sim.Tick - 1 + _cycloneDriver.Alpha;
            if (_cleaveContactConfirmed && _cleaveHitTick >= 0f)
            {
                float holdTicks = _cleaveHitStop * Simulation.TicksPerSecond;
                if (tick < _cleaveHitTick + holdTicks) tick = _cleaveHitTick;
                else tick = Mathf.Lerp(_cleaveHitTick, sim.CleaveEndTick,
                    Mathf.InverseLerp(_cleaveHitTick + holdTicks, sim.CleaveEndTick, tick));
            }
            float clipTime = tick <= sim.CleaveContactTick
                ? .4f * Mathf.InverseLerp(sim.CleaveStartTick, sim.CleaveContactTick, tick)
                : Mathf.Lerp(.4f, .9f, Mathf.InverseLerp(sim.CleaveContactTick, sim.CleaveEndTick, tick));
            _animator.SetFloat("CleavePhase", clipTime / .9f);
        }

        private void Update()
        {
            UpdateCleaveAnimation();
            UpdateCycloneAnimation();
            if (RollActive && _cycloneDriver != null && _cycloneDriver.Sim != null
                && _cycloneDriver.Sim.Entities.ForcedKind[Simulation.PlayerId] != (byte)ForcedMotionKind.Roll)
            {
                _abilityPresentationActive = false;
                _actionProtectedUntil = 0f;
                _animator.CrossFadeInFixedTime(_locomotionMoving ? "Run_v5" : "CombatIdle_v5", .08f, 0);
            }
            if (_abilityPresentationActive && _abilityDefinitionId != AbilityDefinition.CleaveId && Time.time >= _abilityPresentationUntil)
            {
                if (_abilityUsesLowerBodyLayer) ReleaseUpperBodyToLocomotion(0.12f);
                _abilityPresentationActive = false;
            }
            bool attackPresentationEnded = false;
            if (_attackPresentationActive && Time.time >= _attackPresentationUntil)
            {
                _attackPresentationActive = false;
                attackPresentationEnded = true;
            }

            bool orvillHitPresentationEnded = false;
            if (_orvillHitPresentationUntil > 0f && Time.time >= _orvillHitPresentationUntil)
            {
                _orvillHitPresentationUntil = 0f;
                orvillHitPresentationEnded = true;
            }

            if (_animator == null) return;

            if (IsDead && _faction == Faction.Wole)
            {
                AnimatorStateInfo death = _animator.GetCurrentAnimatorStateInfo(0);
                if (death.fullPathHash == PelagDeathState && death.normalizedTime >= 1f)
                    _animator.Play(PelagDeathState, 0, 1f);
            }

            if (IsDead && _faction == Faction.Orvill
                && _animator.HasState(0, OrvillDeathState))
            {
                AnimatorStateInfo death = _animator.GetCurrentAnimatorStateInfo(0);
                // OrvillDeathState contains the full layer path ("Base Layer.DeathBack").
                // Comparing it with shortNameHash made this terminal-pose guard
                // permanently false even while DeathBack was playing.
                if (death.fullPathHash == OrvillDeathState
                    && death.normalizedTime >= EnemyPresentationProfile.Death(_enemyKind).RestNormalized)
                    _animator.Play(OrvillDeathState, 0,
                        EnemyPresentationProfile.Death(_enemyKind).RestNormalized);
            }
            if (_faction == Faction.Orvill
                && (attackPresentationEnded || orvillHitPresentationEnded))
                RestoreOrvillLocomotionPlayback();
            // ВЕРХНИЙ СЛОЙ ОТПУСКАЕТСЯ ВЕСОМ. ПУСТОГО СОСТОЯНИЯ НЕДОСТАТОЧНО.
            //
            // Замерено на живом контроллере (правая кисть в системе модели):
            //   чистый idle .................. (0.179, 0.467, 0.036)
            //   во время удара ............... (-0.210, 0.580, 0.140)
            //   пустое состояние, вес 1 ...... (-0.208, 0.579, 0.139)  ← поза удара
            //   пустое состояние, вес 0 ...... (0.177, 0.458, 0.040)   ← idle
            //
            // То есть с выключенным Write Defaults пустое состояние НЕ отдаёт
            // кости базовому слою: их просто никто не пишет, и они навсегда
            // застывают в последней позе удара. Именно это выглядело как
            // «убили моба, и Пелаг замер с вытянутой саблей».
            //
            // Раньше это скрывал включённый Write Defaults: он писал позу
            // покоя рига — тоже неправильно, но хотя бы не залипало.
            //
            // Вход за 80 мс сохраняет исходную стойку, выход весом за
            // UpperBodyReleaseSeconds возвращает управление базовому слою.
            if (_saberStanceLayer >= 0)
            {
                if (_stanceEquipment == null) _stanceEquipment = GetComponent<PelagEquipmentView>();
                float phase = _stanceEquipment != null ? _stanceEquipment.DrawPhase : 0f;
                // Полная стойка включает таз и ноги. При старте/остановке
                // движения плавно передаём их локомоции; удар имеет приоритет.
                float stanceWeight = IsDead || HasCommittedAction || _locomotionMoving ? 0f
                    : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((phase - .55f) / .45f));
                if (!IsDead && !HasCommittedAction)
                    stanceWeight = Mathf.MoveTowards(_animator.GetLayerWeight(_saberStanceLayer),
                        stanceWeight, Time.deltaTime / .15f);
                _animator.SetLayerWeight(_saberStanceLayer, stanceWeight);
                UpdateStanceFootwork(phase);
            }
            if (_faction == Faction.Wole && _upperBodyLayer >= 0)
            {
                bool upperActive = _attackPresentationActive
                                   || (_abilityPresentationActive && _abilityUsesLowerBodyLayer);
                float current = _animator.GetLayerWeight(_upperBodyLayer);
                float weight = MaskedAbilityActive
                    ? Mathf.MoveTowards(current, MaskedAbilityWeight, Time.deltaTime / 0.08f)
                    : upperActive
                    ? Mathf.MoveTowards(current, 1f, Time.deltaTime / 0.08f)
                    : Mathf.MoveTowards(current, 0f, Time.deltaTime / UpperBodyReleaseSeconds);
                _animator.SetLayerWeight(_upperBodyLayer, weight);
            }
            if (_faction == Faction.Wole && attackPresentationEnded
                && !_abilityPresentationActive && !IsDead)
                ReleaseUpperBodyToLocomotion(0.12f);
            if (_faction == Faction.Wole && _lowerBodyLayer >= 0)
            {
                // Warp ends at the timed contact recovery, before the authored
                // follow-through ends. Using it as the leg-layer lifetime made
                // the end of combo B teleport to idle in roughly 55 ms.
                bool actionActive = _attackPresentationActive
                                    || (_abilityPresentationActive && _abilityUsesLowerBodyLayer);
                // Вихрь сохраняет полный разворот; Циклон при движении оставляет ногам бег.
                float target = CyclonePresentationActive && _locomotionMoving ? 0f
                    : MaskedAbilityActive ? MaskedAbilityWeight
                    : actionActive && !_locomotionMoving ? 1f : 0f;
                float weight = Mathf.MoveTowards(
                    _animator.GetLayerWeight(_lowerBodyLayer), target,
                    Time.deltaTime / (MaskedAbilityActive ? 0.08f : 0.16f));
                _animator.SetLayerWeight(_lowerBodyLayer, weight);
            }

            if (!_attackWarpActive) return;
            // The derived clips already contain anticipation/acceleration and
            // recovery. Global hit-stop slows Sim and Animator together; a
            // second per-Animator freeze would drift the next combo stroke.
            _animator.SetFloat(AttackPlaybackSpeed, 1f);
            if (Time.time - _attackWarpStartedAt >= BasicAttackPresentationDuration) StopAttackWarp();
        }

        public void SetLocomotion(bool moving, float turnDirection, float normalizedSpeed,
            float localMoveX, float localMoveY, float worldSpeed, float turnDelta)
        {
            if (_spriteVisual != null)
            {
                _spriteVisual.SetMoving(moving);
                return;
            }
            if (_animator == null || IsDead) return;
            if (_abilityPresentationActive && _leapLocomotion && moving
                && _abilityPresentationUntil - Time.time <= LeapRunHandoff && _faction == Faction.Wole)
            {
                int run = Animator.StringToHash("Base Layer.Run_v5");
                var current = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                    : _animator.GetCurrentAnimatorStateInfo(0);
                if (current.fullPathHash != run) _animator.CrossFadeInFixedTime(run, LeapRunBlend, 0);
            }
            if (_abilityPresentationActive && _sweepLocomotion && _faction == Faction.Wole)
            {
                int desired = moving ? Animator.StringToHash("Base Layer.Run_v5") : AnchorSweepState;
                var current = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                    : _animator.GetCurrentAnimatorStateInfo(0);
                if (current.fullPathHash != desired)
                    _animator.CrossFadeInFixedTime(desired, 0.12f, 0,
                        moving ? 0f : Mathf.Max(0f, PelagAbilityTiming.SweepRecovery - (_abilityPresentationUntil - Time.time)));
            }
            _locomotionMoving = moving;
            // Начало шага слегка сглажено, остановка — нет. Симуляция уже
            // тормозит тело сама; второй damp после нулевой скорости и создавал
            // видимое «ноги ещё бегут, хотя персонаж уже приехал».
            if (moving)
                _animator.SetFloat(MoveSpeed, 1f, 0.04f, Time.deltaTime);
            else if (_faction == Faction.Orvill && Mathf.Abs(turnDirection) > 0.01f)
                // РАЗВОРОТ НА МЕСТЕ БЕЗ КЛИПА ПОВОРОТА. У моба нет TurnLeft и
                // TurnRight, поэтому корень доворачивался, а тело оставалось в
                // стойке — на экране это чистое вращение 3D-модели вокруг оси.
                //
                // Пока клипов нет, подмешиваем немного ходьбы: ноги
                // переступают, и разворот перестаёт читаться как прокрутка.
                // Это заплатка, а не решение: настоящий ответ — Turn_90 в паке,
                // и он уже описан в справочнике анимаций.
                _animator.SetFloat(MoveSpeed, TurnShuffleMoveSpeed, 0.06f, Time.deltaTime);
            else
                _animator.SetFloat(MoveSpeed, 0f);
            // TurnDirection is a Pelag-only presentation layer. The Orvill
            // controller intentionally has no matching parameter.
            if (_faction == Faction.Wole)
            {
                // Measured on the imported runtime rig at ArenaView's 1.82 scale:
                // planted toes travel backwards at 4.43 m/s at clip speed 1.
                // Use metres/second, including speed modifiers and collision
                // clipping. A minimum cadence or a second damping filter makes
                // the feet outrun the body during acceleration/braking.
                float clipGroundSpeed = 4.43f * transform.lossyScale.y / 1.82f;
                float playback = worldSpeed / Mathf.Max(0.01f, clipGroundSpeed);
                _animator.SetFloat(LocomotionPlaybackSpeed, playback);
                Vector2 localDirection = new Vector2(localMoveX, localMoveY);
                if (!moving || localDirection.sqrMagnitude < 0.0001f)
                    localDirection = Vector2.up;
                else
                    localDirection.Normalize();
                _animator.SetFloat(MoveX, localDirection.x, 0.045f, Time.deltaTime);
                _animator.SetFloat(MoveY, localDirection.y, 0.045f, Time.deltaTime);
                UpdateTurn(moving, turnDelta);
            }
            else
            {
                // Orvill's controller has no playback-speed parameter. Limit
                // Animator.speed to this faction so Pelag's layered warps are
                // untouched, and follow the actual deterministic velocity.
                // СГЛАЖИВАЕМ, А НЕ БЕРЁМ МГНОВЕННО. Скорость тела в толпе
                // дёргается каждый тик — расталкивание, обход занятого места,
                // торможение у цели, — и без сглаживания это уходило прямо в
                // темп ног. Трасса показала пульсацию 0.67 ↔ 1.22 по нескольку
                // раз в секунду: ноги то частят, то залипают.
                float target = moving
                    // Опорная стопа Run проходит 0.092 м за 3 кадра при
                    // масштабе 1.5868: естественная скорость клипа 1.46 м/с.
                    ? (IsRootSwarm ? Simulation.RootSwarmMoveSpeed.ToFloat() / 1.46f
                        : OrvillLocomotionBasePlaybackSpeed)
                      * Mathf.Clamp(normalizedSpeed, OrvillLocomotionMinPlaybackSpeed,
                          IsRootSwarm ? Simulation.RootSwarmRushSpeed.ToFloat()
                              / Simulation.RootSwarmMoveSpeed.ToFloat() : 1f)
                    : 1f;
                _orvillLocomotionPlaybackSpeed = Mathf.Lerp(
                    _orvillLocomotionPlaybackSpeed, target,
                    1f - Mathf.Exp(-OrvillLocomotionSmoothing * Time.deltaTime));
                RestoreOrvillLocomotionPlayback();
            }
        }

        private void UpdateTurn(bool moving, float delta)
        {
            float angularSpeed = Time.deltaTime > 0.00001f ? delta / Time.deltaTime : 0f;
            TurnAngularSpeed = Mathf.Lerp(TurnAngularSpeed, angularSpeed,
                1f - Mathf.Exp(-14f * Time.deltaTime));
            if (moving || HasCommittedAction)
            {
                _turnSign = 0f;
                _lastTurnAt = -10f;
                _animator.SetFloat(TurnDirection, 0f);
                return;
            }
            if (Mathf.Abs(delta) > 0.05f)
            {
                float sign = Mathf.Sign(delta);
                bool start = sign != _turnSign || Time.time - _lastTurnAt > 0.12f;
                if (start) _turnTravel = 0f;
                _turnTravel += Mathf.Abs(delta);
                _turnSign = sign;
                _lastTurnAt = Time.time;
                float phase = Mathf.Repeat(_turnTravel, 90f) / 90f;
                if (phase < 0.0001f && _turnTravel > 1f) phase = 1f;
                _animator.SetFloat(TurnPhase, phase);
                _animator.SetFloat(TurnDirection, sign);
                if (start)
                    _animator.CrossFadeInFixedTime(sign < 0f ? LeftTurnState : RightTurnState, 0.035f, 0);
                // ArenaView supplies the already rendered yaw in LateUpdate.
                // Evaluate only this non-combat turn at zero delta so the feet
                // do not lag one render frame behind that yaw.
                _animator.Update(0f);
            }
            else if (Time.time - _lastTurnAt > 0.08f)
            {
                _animator.SetFloat(TurnDirection, 0f);
                _turnSign = 0f;
            }
        }

        public void PlayAttack(int authoritativeVariant = -1, float elapsed = 0f)
        {
            int variant = authoritativeVariant >= 0 ? authoritativeVariant : _attackVariant;
            _attackVariant = variant + 1;
            if (_faction == Faction.Wole && !IsDead) SetCombatReady(true);
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayAttack(variant);
                return;
            }
            if (_animator == null || IsDead) return;
            if (_faction == Faction.Wole)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _abilityUsesLowerBodyLayer = false;
                // A newly committed basic attack is allowed to replace the
                // Whirlwind recovery immediately. Leaving the ability triggers
                // queued made AnyState wait behind the old exit transition,
                // while Sim had already started the next windup.
                ResetAbilityTriggers();
                // В исходной delivery два настоящих удара; frames 99–147 —
                // recovery второго, а не третий finisher. Проигрываем честную
                // связку A/B и оставляем recovery внутри B.
                bool secondStrike = (variant & 1) != 0;
                string trigger = secondStrike ? "AttackB" : "AttackA";
                string lowerTrigger = trigger == "AttackA" ? "LowerAttackA" : "LowerAttackB";
                _animator.ResetTrigger("AttackA");
                _animator.ResetTrigger("AttackB");
                _animator.ResetTrigger("LowerAttackA");
                _animator.ResetTrigger("LowerAttackB");
                // Attack is already committed by Simulation. Enter the authored
                // states directly instead of asking AnyState through a one-shot
                // trigger: a trigger raised while the layer was blending out of
                // Whirlwind/previous combo could be consumed without entering
                // the requested state. Damage would then still resolve twelve
                // ticks later with no visible swing.
                //
                // Keep trigger fallback for an older/misbuilt controller, but
                // the production controller uses deterministic direct entry.
                int upperState = secondStrike ? UpperBodyAttackBState : UpperBodyAttackAState;
                int lowerState = secondStrike ? LowerBodyAttackBState : LowerBodyAttackAState;
                bool upperEntered = EnterCommittedAttackState(_upperBodyLayer, upperState, elapsed);
                bool lowerEntered = EnterCommittedAttackState(_lowerBodyLayer, lowerState, elapsed);
                // Первый удар входит весом за 80 мс; в связке вес уже равен
                // единице. Пустой слой не должен одним кадром подменять стойку.
                if (!upperEntered) _animator.SetTrigger(trigger);
                if (!lowerEntered) _animator.SetTrigger(lowerTrigger);
                StartAttackWarp();
                _attackWarpStartedAt -= elapsed;
                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + BasicAttackPresentationDuration - elapsed;
                // Слабые входящие попадания всё ещё получают recoil/flash в
                // ArenaView, но не имеют права ломать читаемую фазу клинка.
                _actionProtectedUntil = Time.time + BasicAttackClipDuration - elapsed;
            }
            else
            {
                bool shieldBash = IsRootSwarm ? _orvillAttackCount++ % 2 == 1
                    : _orvillAttackCount++ % 3 == 2;
                float playbackSpeed = IsRootSwarm ? 1f : shieldBash
                    ? OrvillShieldPlaybackSpeed
                    : OrvillSwordPlaybackSpeed;
                float presentationDuration = IsRootSwarm ? 16f / 30f : shieldBash
                    ? OrvillShieldPresentationDuration
                    : OrvillSwordPresentationDuration;

                // Attack has priority over a presentation-only hit reaction:
                // Sim has already committed this action and will resolve its
                // Damage at AttackContactTime even if the view is struck.
                ResetOrvillActionTriggers();
                _orvillHitPresentationUntil = 0f;
                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + presentationDuration;
                _actionProtectedUntil = Time.time + AttackContactTime;
                _animator.speed = playbackSpeed;
                _animator.SetTrigger(shieldBash ? OrvillShieldBash : OrvillSwordAttack);
            }
        }

        /// <summary>
        /// Совместимый вход для старой витрины, где Pelag abilities были
        /// жёстко разложены по слотам 0..3. Игровой путь обязан использовать
        /// <see cref="PlayAbilityDefinition"/>: слот — это позиция кнопки, а
        /// не идентификатор самой способности.
        /// </summary>
        public void PlayAbility(int slot)
        {
            int definitionId;
            switch (slot)
            {
                case 0: definitionId = AbilityDefinition.WhirlwindId; break;
                case 1: definitionId = AbilityDefinition.AnchorLeapId; break;
                case 2: definitionId = AbilityDefinition.ChainCycloneId; break;
                case 3: definitionId = AbilityDefinition.ChainStepId; break;
                default: return;
            }

            PlayAbilityDefinition(definitionId);
        }

        /// <summary>
        /// Показывает Pelag-приём по стабильному DefinitionId.
        ///
        /// DefinitionId приходит из того же AbilityBuild, который породил
        /// SimEvent. Это не даёт перестановке кнопок случайно включить другой
        /// клип и не превращает неизвестную способность в Whirlwind.
        /// </summary>
        public void PlayAbilityDefinition(int definitionId)
        {
            if (definitionId == AbilityDefinition.CleaveId)
            {
                if (IsDead || _animator == null) return;
                GetComponent<PelagFootPlantView>()?.BeginCleave();
                StopAttackWarp();
                CancelUpperBodyAttack(.02f);
                ResetAbilityTriggers();
                _cyclonePhase = -1;
                _cycloneReleasing = false;
                _sweepLocomotion = _leapLocomotion = false;
                _attackPresentationActive = false;
                _abilityDefinitionId = definitionId;
                _abilityPresentationActive = true;
                _abilityUsesLowerBodyLayer = true;
                if (_cycloneDriver == null) _cycloneDriver = FindAnyObjectByType<TickDriver>();
                var sim = _cycloneDriver != null ? _cycloneDriver.Sim : null;
                float windup = .4f;
                if (sim != null)
                    for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                    {
                        var build = sim.GetAbility(slot);
                        if (build != null && build.DefinitionId == definitionId)
                            windup = Mathf.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt()) / (float)Simulation.TicksPerSecond;
                    }
                _abilityPresentationUntil = Time.time + .9f * windup / .4f;
                _actionProtectedUntil = _abilityPresentationUntil;
                SetCombatReady(true);
                _cleaveContactConfirmed = false;
                _cleaveHitTick = -1f;

                _animator.SetFloat("CleavePhase", 0f);
                _animator.CrossFadeInFixedTime(_locomotionMoving ? "Run_v5" : "CombatIdle_v5", .04f, 0);
                if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
                if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);
                if (_upperBodyLayer >= 0) _animator.CrossFadeInFixedTime("Cleave", .035f, _upperBodyLayer);
                if (_lowerBodyLayer >= 0) _animator.CrossFadeInFixedTime("Cleave", .035f, _lowerBodyLayer);
                return;
            }
            if (definitionId == AbilityDefinition.DashId)
            {
                if (IsDead || _animator == null) return;
                StopAttackWarp();
                CancelUpperBodyAttack(.02f);
                ResetAbilityTriggers();
                _cyclonePhase = -1;
                _cycloneReleasing = false;
                _sweepLocomotion = _leapLocomotion = false;
                _attackPresentationActive = false;
                _abilityDefinitionId = definitionId;
                _abilityPresentationActive = true;
                _abilityUsesLowerBodyLayer = false;
                if (_cycloneDriver == null) _cycloneDriver = FindAnyObjectByType<TickDriver>();
                var sim = _cycloneDriver != null ? _cycloneDriver.Sim : null;
                float duration = sim != null ? Mathf.Max(2, sim.Entities.ForcedTicksLeft[Simulation.PlayerId])
                    / (float)Simulation.TicksPerSecond : 10f / Simulation.TicksPerSecond;
                _abilityPresentationUntil = Time.time + duration;
                _actionProtectedUntil = _abilityPresentationUntil;
                // Кувырок пишет всё тело: оставшийся слой удара иначе удерживает руки и ноги.
                if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
                if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
                if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
                if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);
                SetAbilityPlaybackSpeed(.6f / duration);
                EnterCommittedAbilityState(Animator.StringToHash("Base Layer.Roll_v5"), .035f);
                return;
            }
            if (definitionId == AbilityDefinition.AnchorSlamId)
            {
                if (IsDead || _animator == null) return;
                // До нового авторского клипа убираем прежний мах и вращение из окна удара.
                StopAttackWarp();
                CancelUpperBodyAttack(.04f);
                ResetAbilityTriggers();
                _attackPresentationActive = false;
                _abilityDefinitionId = definitionId;
                _abilityPresentationActive = true;
                _abilityUsesLowerBodyLayer = false;
                _abilityPresentationUntil = Time.time + .9f;
                _actionProtectedUntil = _abilityPresentationUntil;
                _animator.CrossFadeInFixedTime("CombatIdle_v5", .06f, 0);
                return;
            }
            bool whirlwind = definitionId == AbilityDefinition.WhirlwindId;
            bool anchorLeap = definitionId == AbilityDefinition.AnchorLeapId;
            bool anchorSweep = definitionId == AbilityDefinition.ChainCycloneId;
            bool chainStep = definitionId == AbilityDefinition.ChainStepId;
            if (!whirlwind && !anchorLeap && !anchorSweep && !chainStep) return;
            _abilityDefinitionId = definitionId;
            _cycloneReleasing = false;
            if (chainStep) { _chainPresentationVariant = 0; _chainFinishing = false; }
            if (_faction == Faction.Wole && !IsDead && (whirlwind || chainStep)) SetCombatReady(true);

            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayAbility();
                return;
            }
            if (_animator == null || IsDead) return;
            if (anchorSweep)
            {
                _cyclonePhase = -1;
                UpdateCycloneAnimation();
                return;
            }
            StopAttackWarp();
            if (_faction == Faction.Wole)
            {
                _attackPresentationActive = false;
                _attackPresentationUntil = 0f;
                if (!whirlwind) CancelUpperBodyAttack(0.04f);
                _animator.ResetTrigger("AttackA");
                _animator.ResetTrigger("AttackB");
                _animator.ResetTrigger("LowerAttackA");
                _animator.ResetTrigger("LowerAttackB");
                ResetAbilityTriggers();

                int stateHash;
                int triggerHash;
                float duration;
                float abilitySpeed = 1f;
                if (anchorLeap)
                {
                    stateHash = AnchorLeapState;
                    triggerHash = AnchorLeapTrigger;
                    duration = PelagAbilityTiming.LeapRecovery;
                    _abilityUsesLowerBodyLayer = false;
                    abilitySpeed = AnchorLeapPlaybackSpeed;
                }
                else if (anchorSweep)
                {
                    stateHash = _locomotionMoving ? Animator.StringToHash("Base Layer.Run_v5") : AnchorSweepState;
                    triggerHash = AnchorSweepTrigger;
                    duration = PelagAbilityTiming.SweepRecovery;
                    _abilityUsesLowerBodyLayer = false;
                    abilitySpeed = AnchorSweepPlaybackSpeed;
                }
                else if (chainStep)
                {
                    _chainPresentationVariant = 0;
                    stateHash = ChainStepState;
                    triggerHash = ChainStepTrigger;
                    duration = ChainStepPresentationDuration;
                    _abilityUsesLowerBodyLayer = false;
                    abilitySpeed = ChainStepPlaybackSpeed;
                }
                else
                {
                    // Whirlwind is the layered lower-body path.
                    stateHash = 0;
                    triggerHash = 0;
                    duration = WhirlwindClipDuration;
                    _abilityUsesLowerBodyLayer = true;
                    EnterWhirlwindLayer(_upperBodyLayer, "UpperBody Combat.Whirlwind_v5", "HeavyAttack");
                    EnterWhirlwindLayer(_lowerBodyLayer, "LowerBody Combat.Lower_Whirlwind_v5", "LowerHeavyAttack");
                }

                _sweepLocomotion = anchorSweep;
                _leapLocomotion = anchorLeap;
                SetAbilityPlaybackSpeed(abilitySpeed);
                if (stateHash != 0 && !EnterCommittedAbilityState(stateHash, chainStep ? 0.025f : 0.06f))
                    _animator.SetTrigger(triggerHash);
                _abilityPresentationActive = true;
                // Gameplay may resolve before the authored follow-through.
                // Presentation stays protected through the controller's soft
                // return to Empty; a new committed action can still replace it.
                _abilityPresentationUntil = Time.time + duration;
                _actionProtectedUntil = _abilityPresentationUntil;
            }
        }

        private void EnterWhirlwindLayer(int layer, string state, string fallback)
        {
            int hash = Animator.StringToHash(state);
            if (layer >= 0 && _animator.HasState(layer, hash))
                _animator.CrossFadeInFixedTime(hash, 0.08f, layer, Mathf.Min(Time.deltaTime, 1f / 30f));
            else _animator.SetTrigger(fallback);
        }

        private int _chainPresentationVariant;
        private bool _chainFinishing;

        public void PlayChainStepHop(int index, int remaining)
        {
            if (IsDead || _faction != Faction.Wole || _animator == null) return;
            SetCombatReady(true);
            _abilityDefinitionId = AbilityDefinition.ChainStepId;
            _sweepLocomotion = _leapLocomotion = false;
            _chainPresentationVariant = index & 1;
            _chainFinishing = remaining == 1;
            string state = _chainFinishing ? "ChainStep_Finish_v5"
                : index == 0 ? "ChainStep_v5"
                : _chainPresentationVariant == 0 ? "ChainStep_A_v5" : "ChainStep_B_v5";
            EnterChainState(state, _chainFinishing ? 14f / 30f : ChainStepPresentationDuration);
        }

        public void FinishChainStep()
        {
            if (IsDead || _animator == null || _abilityDefinitionId != AbilityDefinition.ChainStepId
                || !_abilityPresentationActive || _chainFinishing) return;
            // Если цели закончились раньше, доигрываем выход текущего удара.
            // Количество переходов и причина отсутствия урона здесь не важны.
            EnterChainState(_chainPresentationVariant == 0
                ? "ChainStep_RecoverA_v5" : "ChainStep_RecoverB_v5", 8f / 30f);
        }

        private void EnterChainState(string state, float duration)
        {
            CancelUpperBodyAttack(0.015f);
            ResetAbilityTriggers();
            SetAbilityPlaybackSpeed(ChainStepPlaybackSpeed);
            EnterCommittedAbilityState(Animator.StringToHash("Base Layer." + state), 0.02f);
            _abilityUsesLowerBodyLayer = false;
            _abilityPresentationActive = true;
            _abilityPresentationUntil = Time.time + duration;
            _actionProtectedUntil = _abilityPresentationUntil;
        }

        /// <summary>
        /// Тело потащило цепью. Играет knockback-клип, который в контроллере
        /// уже есть.
        ///
        /// Без него волочимый враг едет по земле в позе покоя: собственная
        /// скорость у него обнулена намеренно (иначе играл бы бег), и Idle
        /// оказывается единственным, что остаётся. Скользящая стойка читается
        /// как баг физики, а не как «его тащат».
        ///
        /// Игрока это не касается: его собственный рывок — не потеря контроля,
        /// и подменять ему анимацию на knockback значило бы сообщать обратное.
        /// </summary>
        public void PlayDragged()
        {
            if (_spriteVisual != null || _animator == null || IsDead) return;
            if (_faction == Faction.Wole) return;
            if (IsRootSwarm) return;

            CancelUpperBodyAttack(0.03f);
            _animator.SetTrigger(OrvillKnockback);
        }

        public void PlayStun()
        {
            if (IsDead || _faction == Faction.Wole) return;
            // Оглушение в Sim отменило контакт: старый телеграф больше не должен доигрывать.
            _actionProtectedUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _lastHitAt = -1f;
            if (_animator != null && IsRootSwarm && _animator.HasState(0, OrvillLocomotionState))
                _animator.CrossFadeInFixedTime(OrvillLocomotionState, .05f, 0);
            else PlayHit(0);
        }

        public void PlayHit(int variant)
        {
            // У роя нет Hit-клипа. Попадание уже показывает ArenaView;
            // пустой триггер не должен сбивать темп бега и текущий мах.
            if (IsRootSwarm) return;
            if (_spriteVisual != null)
            {
                if (!IsDead) _spriteVisual.PlayHit(variant);
                return;
            }
            if (_animator == null || IsDead) return;

            // Pelag must never lose locomotion or a committed action to a
            // presentation-only hit. ArenaView already supplies additive root
            // recoil, scale punch and flash, while CombatJuice supplies hit-stop.
            // Until HitFront has its own additive layer, playing the full-body
            // state here would visibly interrupt run/turn/attack/ability.
            if (_faction == Faction.Wole) return;

            // Замах продолжится в Sim: полный Hit не должен скрыть телеграф.
            // Аддитивный наклон корпуса и вспышка уже подтвердили попадание.
            if (Time.time < _actionProtectedUntil) return;
            if (Time.time - _lastHitAt < 0.18f) return;
            _lastHitAt = Time.time;
            CancelUpperBodyAttack(0.03f);
            // After contact, Hit may interrupt an Orvill attack recovery. Before
            // it, the guard above leaves only ArenaView recoil/flash visible.
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            ResetOrvillActionTriggers();
            _orvillHitPresentationUntil = Time.time + OrvillHitPresentationDuration;
            _animator.speed = 1f;
            _animator.SetTrigger((variant & 1) == 0 ? OrvillHitLeft : OrvillHitRight);
        }

        /// <summary>
        /// Подтверждённый контакт базовой атаки. Damage остаётся единственной
        /// точкой истины, а presentation на короткий момент фиксирует authored
        /// contact pose, чтобы клинок, вспышка и реакция цели читались одним
        /// событием даже при плавающей частоте кадров.
        /// </summary>
        public void PlayAttackContact(int variant)
        {
            if (IsDead || _faction != Faction.Wole) return;
            SetCombatReady(true);
            if (_animator == null) return;

            bool secondStrike = (variant & 1) != 0;
            int upperState = secondStrike ? UpperBodyAttackBState : UpperBodyAttackAState;
            int lowerState = secondStrike ? LowerBodyAttackBState : LowerBodyAttackAState;
            float contactPhase = secondStrike
                ? AttackBContactNormalized
                : AttackAContactNormalized;

            // Damage is authoritative. If a trigger/transition, a long frame,
            // or an ability recovery ever swallowed the earlier Attack event,
            // never allow health to change on an idle Pelag. Restore the exact
            // A/B contact pose and continue from its recovery. This is a safety
            // net, not a second attack: it has no gameplay authority.
            bool visibleAttack = IsStateVisible(_upperBodyLayer, upperState);
            if (!_attackPresentationActive || !_attackWarpActive || !visibleAttack)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _abilityUsesLowerBodyLayer = false;
                ResetAbilityTriggers();
                ForceAttackContactState(_upperBodyLayer, upperState, contactPhase);
                if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 1f);
                if (!_locomotionMoving)
                {
                    ForceAttackContactState(_lowerBodyLayer, lowerState, contactPhase);
                    if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 1f);
                }

                _attackPresentationActive = true;
                _attackPresentationUntil = Time.time + BasicAttackPresentationDuration - PlayerAttackContactTime;
                _actionProtectedUntil = Mathf.Max(_actionProtectedUntil, Time.time + 0.20f);
                _attackWarpStartedAt = Time.time - AttackContactTime;
                _attackWarpActive = true;
            }

            // Обычный контакт не перематывает уже играющий удар. Повторный
            // Play на кадре Damage давал скачок назад/вперёд посреди маха.
            // Восстановление выше остаётся только для потерянного Attack.

        }

        public void PlayDeath()
        {
            if (IsDead) return;
            IsDead = true;
            _contactPose?.Clear();
            if (_spriteVisual != null)
            {
                _spriteVisual.PlayDeath();
                return;
            }
            if (_animator == null) return;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _actionProtectedUntil = 0f;
            _orvillHitPresentationUntil = 0f;
            _animator.SetFloat(MoveSpeed, 0f);
            _animator.SetBool(Stunned, false);
            if (_faction == Faction.Orvill)
            {
                // Damage and Death are emitted in the same Sim tick. Clear the
                // earlier Hit plus any committed attack before bypassing
                // AnyState ordering and entering the terminal death state.
                ResetOrvillActionTriggers();
                _animator.speed = 1f;
                if (_animator.HasState(0, OrvillDeathState))
                {
                    var death = EnemyPresentationProfile.Death(_enemyKind);
                    // fixedTimeOffset измеряется в СЕКУНДАХ клипа, не в нормали.
                    _animator.CrossFadeInFixedTime(OrvillDeathState,
                        death.BlendSeconds, 0, death.StartNormalized * death.ClipSeconds);
                }
                else
                    _animator.SetTrigger(OrvillDeath);
                return;
            }

            CancelUpperBodyAttack(0.02f);
            ResetAbilityTriggers();
            _animator.ResetTrigger("Death");
            _animator.speed = 1f;
            _animator.SetFloat(TurnDirection, 0f);
            if (_animator.HasState(0, PelagDeathState))
                _animator.CrossFadeInFixedTime(PelagDeathState, 0.08f, 0, 0f);
            else _animator.SetTrigger("Death");
        }

        public void FaceCamera(Vector3 facing)
        {
            _spriteVisual?.FaceCamera(facing);
        }

        // Авторские клипы содержат presentation-маркеры. Они намеренно ничего
        // не решают: Damage и тайминг контакта принадлежат Game.Sim. Наличие
        // приёмников только не даёт Unity засорять Console на каждом клипе.
        public void AttackContactCue() { }
        public void ShieldImpactCue() { }
        public void GuardBreakCue() { }
        public void DeathImpactCue() { }
        public void HookReleaseCue() { }
        public void HookRecoverCue() { }
        public void HeavyImpactCue() { }

        private void StartAttackWarp()
        {
            // Track this stroke for contact recovery and interruption guards.
            _attackWarpStartedAt = Time.time;
            _attackWarpActive = true;
            _animator.SetFloat(AttackPlaybackSpeed, 1f);
        }

        private void StopAttackWarp()
        {
            _attackWarpActive = false;
            if (_animator != null && _faction == Faction.Wole)
                _animator.SetFloat(AttackPlaybackSpeed, 1f);
        }

        private bool EnterCommittedAttackState(int layer, int stateHash, float elapsed = 0f)
        {
            if (_animator == null || layer < 0 || !_animator.HasState(layer, stateHash))
                return false;

            // A short fixed blend preserves continuity without leaving action
            // ownership to an interruptible transition graph.
            // Attack приходит после оценки Animator в LateUpdate. Компенсируем
            // этот кадр при входе, чтобы позже не перематывать клип на Damage.
            _animator.CrossFadeInFixedTime(stateHash, 0.10f, layer, elapsed + Mathf.Min(Time.deltaTime, 1f / 30f));
            return true;
        }

        private bool EnterCommittedAbilityState(int stateHash, float blend)
        {
            if (_animator == null || !_animator.HasState(0, stateHash))
                return false;
            _animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, blend), 0, Mathf.Min(Time.deltaTime, 1f / 30f));
            return true;
        }

        private void ResetAbilityTriggers()
        {
            if (_animator == null) return;
            _animator.ResetTrigger("Hook");
            _animator.ResetTrigger("HeavyAttack");
            _animator.ResetTrigger("LowerHeavyAttack");
            _animator.ResetTrigger(AnchorLeapTrigger);
            _animator.ResetTrigger(AnchorSweepTrigger);
            _animator.ResetTrigger(ChainStepTrigger);
        }

        private void SetAbilityPlaybackSpeed(float value)
        {
            // Старые controller assets могут пережить исходный builder. Не
            // спамим Console и не ломаем cast: при отсутствии параметра клип
            // проигрывается с собственной скоростью состояния.
            if (_animator == null || !HasAnimatorParameter("AbilityPlaybackSpeed")) return;
            _animator.SetFloat("AbilityPlaybackSpeed", value);
        }

        private bool HasAnimatorParameter(string parameterName)
        {
            AnimatorControllerParameter[] parameters = _animator != null ? _animator.parameters : null;
            if (parameters == null) return false;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == parameterName) return true;
            return false;
        }

        private void ForceAttackContactState(int layer, int stateHash, float normalizedTime)
        {
            if (_animator == null || layer < 0 || !_animator.HasState(layer, stateHash)) return;
            _animator.CrossFade(stateHash, 0.012f, layer, normalizedTime);
        }

        private bool IsStateVisible(int layer, int stateHash)
        {
            if (_animator == null || layer < 0) return false;
            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(layer);
            if (current.fullPathHash == stateHash) return true;
            if (!_animator.IsInTransition(layer)) return false;
            return _animator.GetNextAnimatorStateInfo(layer).fullPathHash == stateHash;
        }

        /// <summary>
        /// Мягко возвращает верх тела базовому слою: только кроссфейд в пустое
        /// состояние, без сброса веса ног. Этим отличается от
        /// <see cref="CancelUpperBodyAttack"/>, который обрывает и ноги — там
        /// это нужно, потому что удар перебивают способностью или смертью.
        /// </summary>
        private void ReleaseUpperBodyToLocomotion(float blend)
        {
            if (_animator == null || _upperBodyLayer < 0) return;
            if (!_animator.HasState(_upperBodyLayer, UpperBodyEmptyState)) return;
            _animator.CrossFade(UpperBodyEmptyState, blend, _upperBodyLayer, 0f);
        }

        private void CancelUpperBodyAttack(float blend)
        {
            if (_animator == null) return;
            _cleaveHitTick = -1f;
            _cleaveContactConfirmed = false;
            StopAttackWarp();
            if (_faction != Faction.Wole) return;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _animator.ResetTrigger("AttackA");
            _animator.ResetTrigger("AttackB");
            _animator.ResetTrigger("LowerAttackA");
            _animator.ResetTrigger("LowerAttackB");
            if (_upperBodyLayer >= 0
                && _animator.HasState(_upperBodyLayer, UpperBodyEmptyState))
                _animator.CrossFade(UpperBodyEmptyState, blend, _upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0)
            {
                _animator.SetLayerWeight(_lowerBodyLayer, 0f);
                if (_animator.HasState(_lowerBodyLayer, LowerBodyEmptyState))
                    _animator.CrossFade(LowerBodyEmptyState, blend, _lowerBodyLayer, 0f);
            }
        }

        private void RestoreOrvillLocomotionPlayback()
        {
            if (_animator == null || _faction != Faction.Orvill || IsDead) return;
            if (_attackPresentationActive || Time.time < _orvillHitPresentationUntil) return;
            _animator.speed = _orvillLocomotionPlaybackSpeed;
        }

        private static float DeterministicLocomotionPhase(int presentationId)
        {
            unchecked
            {
                uint hash = (uint)presentationId + 0x9E3779B9u;
                hash = (hash ^ (hash >> 16)) * 0x7FEB352Du;
                hash = (hash ^ (hash >> 15)) * 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777216f;
            }
        }

        private void ResetOrvillActionTriggers()
        {
            if (_animator == null) return;
            _animator.ResetTrigger(OrvillSwordAttack);
            _animator.ResetTrigger(OrvillShieldBash);
            _animator.ResetTrigger(OrvillHighBlock);
            _animator.ResetTrigger(OrvillGuardBreak);
            _animator.ResetTrigger(OrvillHitLeft);
            _animator.ResetTrigger(OrvillHitRight);
            _animator.ResetTrigger(OrvillKnockback);
            _animator.ResetTrigger(OrvillDeath);
        }

        private void OnDisable()
        {
            _cleaveHitTick = -1f;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            _combatReady = false;
            _locomotionMoving = false;
            _orvillLocomotionPlaybackSpeed = 1f;
            _orvillHitPresentationUntil = 0f;
            if (_animator != null && _lowerBodyLayer >= 0)
                _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_animator != null && _upperBodyLayer >= 0)
                _animator.SetLayerWeight(_upperBodyLayer, 0f);
            if (_animator != null && _faction == Faction.Orvill)
                _animator.speed = 1f;
            if (_animator != null && _faction == Faction.Wole)
                _animator.SetBool(Relaxed, true);
            StopAttackWarp();
        }
    }
}
