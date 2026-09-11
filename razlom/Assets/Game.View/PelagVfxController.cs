using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Presentation для пяти приёмов Pelag. Контроллер читает SimEvent и
    /// состояние View, но никогда не наносит урон и не перемещает сущность в Sim.
    /// Все временные объекты выдаются заранее прогретыми ViewPool.
    /// </summary>
    [RequireComponent(typeof(TickDriver), typeof(ArenaView))]
    [DefaultExecutionOrder(1010)]
    public sealed partial class PelagVfxController : MonoBehaviour
    {
        private const string LibraryPath = "VFX/Pelag/AbilityVfxLibrary";
        private const int MaxActive = 72;
        private const int MaxTargets = 128;
        private const float BrightVfxLifetime = 5f / 30f;
        private const float FlipbookLifetime = 16f / 30f;
        private static readonly float AttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        private const float WhirlwindContactTime = CharacterAnimatorView.WhirlwindContactTime;

        private enum Motion : byte { Static, Expand, Projectile, Dash, Chain, PullLine, Whirlwind, AnchorFlight, EnemyPull, HopChain, Roll, Cleave }

        private sealed class PoolRecord
        {
            public ViewPool Pool;
            public Quaternion AuthoredRotation;

        }


        private struct ActiveFx
        {
            public bool Active;
            public PelagVfxId Id;
            public GameObject Object;
            public PelagVfxElement Element;
            public float Age;
            public float Duration;
            public float StartScale;
            public float EndScale;
            public float ArcHeight;
            public Vector3 Start;
            public Vector3 End;

            public bool JustSpawned;
            public Motion Motion;
            public int FollowIndex;
        }

        private TickDriver _driver;
        private ArenaView _arena;
        private CombatJuiceView _juice;
        private PoolRecord[] _pools;
        private ActiveFx[] _active;
        private int _activeCursor;
        private readonly int[] _targets = new int[MaxTargets];
        private readonly float[] _targetDistanceSq = new float[MaxTargets];
        private int _targetCount;

        private PelagVfxShowcase _showcase;
        private float _showcaseTime;
        private int _showcaseStage;
        private PelagVfxShowcase _motionAbility;
        private float _motionTime;
        private float _motionStartedAt;
        private Vector3 _motionStart;
        private Vector3 _motionEnd;
        private Vector3 _chainRouteOrigin;
        private bool _captureMotion;
        private float _attackMotionTime = -1f;
        private Vector3 _attackMotionDirection;
        private bool _whirlwindContactPending;
        private int _cleaveVfxCast = -1;
        private bool _cleaveSlashPlayed;
        private bool _cleaveGroundPlayed;
        [SerializeField, Range(.25f, 2f)] private float _cleaveSlashScale = 1f;
        private float _whirlwindContactDelay;
        private Light _heroLight;
        private float _combatLightPulse;
        private Transform _footstepBody;
        private PelagFootPlantView _footstepView;
        private CampGroundStudy _footstepCampGround;
        private LayoutView _footstepLayout;
        private static readonly int HeroLightPositionId =
            Shader.PropertyToID("_RazlomHeroLightPosition");
        private static readonly int HeroLightColorId =
            Shader.PropertyToID("_RazlomHeroLightColor");

        public bool PoolsReady { get; private set; }
        public int VisibleFlyingAnchors
        {
            get
            {
                int count = 0;
                if (_active != null)
                    foreach (var fx in _active)
                        if (fx.Active && fx.Motion == Motion.AnchorFlight && fx.Object.activeInHierarchy) count++;
                return count;
            }
        }
        public bool ShowcaseRunning => _showcase != PelagVfxShowcase.None;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            _juice = GetComponent<CombatJuiceView>();
            _active = new ActiveFx[MaxActive];
            BuildPools();
            BuildHeroLight();
            HeroTrail();
            _footstepCampGround = FindAnyObjectByType<CampGroundStudy>(FindObjectsInactive.Include);
        }

        private void LateUpdate()
        {
            if (_driver == null || _arena == null) return;
            ConsumeSimEvents();
            UpdateShowcase();
            UpdateAutoattackPresentation();
            UpdateWhirlwindContact();
            UpdateCleaveSlash();

            UpdateAbilityMotion();
            UpdateFootstepDust();
            UpdateActive(Time.deltaTime);
            UpdateCyclonePresentation();
            UpdateCombatLighting(Time.unscaledDeltaTime);
        }

        private void UpdateFootstepDust()
        {
            if (!PoolsReady || Time.deltaTime <= 0f
                || !_arena.TryGetEntityView(Simulation.PlayerId, out Transform body)) return;
            if (_footstepBody != body)
            {
                _footstepBody = body;
                _footstepView = body.GetComponentInChildren<PelagFootPlantView>();
            }
            if (_footstepView == null) return;
            for (int side = 0; side < 2; side++)
            {
                if (!_footstepView.TryGetStepContact(side == 0, out Vector3 position)) continue;
                if (_footstepLayout == null) _footstepLayout = GetComponent<LayoutView>();
                bool camp = _driver.Session != null && _driver.Session.Mode == GameMode.Camp;
                bool dusty = camp
                    ? !_driver.Session.OnProvingGround && _footstepCampGround != null && _footstepCampGround.IsDustyPath(position)
                    : _footstepLayout != null && _footstepLayout.IsDustyPath(position);
                if (CaptureRig.HasEnemyOverride)
                    Debug.Log($"[footstep-surface] side={side} time={Time.time:F3} dust={dusty} position={position}");
                if (!dusty || !TryAcquire(PelagVfxId.FootstepDust, out GameObject go, out PelagVfxElement element)) continue;
                int index = ReserveActive();
                element.Begin(position, Quaternion.identity);
                if (CaptureRig.HasEnemyOverride)
                    Debug.Log($"[footstep-vfx] side={side} time={Time.time:F3} position={position}");
                _active[index] = new ActiveFx
                {
                    Active = true, Id = PelagVfxId.FootstepDust, Object = go, Element = element,
                    Duration = Mathf.Max(0.05f, element.DefaultLifetime),
                    Start = position, End = position, Motion = Motion.Static, FollowIndex = -1
                };
            }
        }

        private void BuildPools()
        {
            AbilityVfxLibrary library = Resources.Load<AbilityVfxLibrary>(LibraryPath);
            if (library == null || library.Entries == null)
            {
                Debug.LogError($"[Pelag VFX] Library не найдена: Resources/{LibraryPath}");
                return;
            }

            _pools = new PoolRecord[(int)PelagVfxId.Count];
            Transform root = new GameObject("Пул: Pelag VFX").transform;
            root.SetParent(transform, false);

            for (int i = 0; i < library.Entries.Length; i++)
            {
                AbilityVfxLibrary.Entry entry = library.Entries[i];
                if (entry.Prefab == null) continue;
                int id = (int)entry.Id;
                GameObject prefab = entry.Prefab;
                Transform parent = new GameObject(entry.Id.ToString()).transform;
                parent.SetParent(root, false);
                _pools[id] = new PoolRecord
                {
                    AuthoredRotation = prefab.transform.localRotation,
                    Pool = new ViewPool(parent, () => Instantiate(prefab), Mathf.Max(1, entry.Prewarm))
                };
                // Блики перекрываются уже в первом броске; ViewPool сам создаёт только один экземпляр.
                if(entry.Id == PelagVfxId.AnchorLeapFlight || entry.Id == PelagVfxId.AnchorLeapLanding
                    || entry.Id == PelagVfxId.FootstepDust || entry.Id == PelagVfxId.WhirlwindHit
                    || entry.Id == PelagVfxId.ChainStepDash || entry.Id == PelagVfxId.ChainStepHit
                    || entry.Id == PelagVfxId.ChainStepFinish
                    || entry.Id == PelagVfxId.CleaveHit || entry.Id == PelagVfxId.CleaveSlash
                    || entry.Id == PelagVfxId.CleaveGround)
                    _pools[id].Pool.PrewarmStep(Mathf.Max(3,entry.Prewarm));
            }

            PoolsReady = true;
        }

        private void BuildHeroLight()
        {
            GameObject go = new GameObject("Свет: Пелаг / боевой импульс");
            go.transform.SetParent(transform, false);
            _heroLight = go.AddComponent<Light>();
            _heroLight.type = LightType.Point;
            _heroLight.color = new Color(1f, 0.24f, 0.055f);
            _heroLight.range = 4.2f;
            _heroLight.intensity = 0.38f;
            _heroLight.shadows = LightShadows.None;
            _heroLight.renderMode = LightRenderMode.ForcePixel;
        }

        private void PulseCombatLight(float strength)
        {
            _combatLightPulse = Mathf.Max(_combatLightPulse, Mathf.Clamp01(strength));
        }

        private void UpdateCombatLighting(float dt)
        {
            if (_driver == null || _driver.Sim == null)
            {
                _combatLightPulse = 0f;
                Shader.SetGlobalColor(HeroLightColorId, Color.black);
                if (_heroLight != null) _heroLight.intensity = 0f;
                return;
            }

            _combatLightPulse = Mathf.MoveTowards(_combatLightPulse, 0f, dt * 2.25f);
            float peak = _combatLightPulse * _combatLightPulse;
            Vector3 position = PlayerPosition() + Vector3.up * 0.82f;
            float radius = Mathf.Lerp(4.2f, 6.1f, peak);
            float shaderIntensity = Mathf.Lerp(0.15f, 2.85f, peak);
            Color shaderColor = new Color(1.00f, 0.23f, 0.035f, 1f) * shaderIntensity;

            Shader.SetGlobalVector(HeroLightPositionId,
                new Vector4(position.x, position.y, position.z, radius));
            Shader.SetGlobalColor(HeroLightColorId, shaderColor);

            if (_heroLight == null) return;
            _heroLight.transform.position = position;
            _heroLight.range = radius;
            _heroLight.intensity = Mathf.Lerp(0.38f, 4.4f, peak);
            if (!_heroLight.enabled) _heroLight.enabled = true;
        }

        private void OnEnable()
        {
            if (_heroLight != null) _heroLight.enabled = true;
        }

        private void OnDisable()
        {
            StopCleaveSlash();
            StopLeapMotionVfx();
            ReleaseCyclone();
            if (_heroLight != null) _heroLight.enabled = false;
            _combatLightPulse = 0f;
            Shader.SetGlobalVector(HeroLightPositionId, new Vector4(0f, -100f, 0f, 1f));
            Shader.SetGlobalColor(HeroLightColorId, Color.black);
        }

        private void ConsumeSimEvents()
        {
            if (!PoolsReady || !_driver.enabled || _showcase != PelagVfxShowcase.None) return;

            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type == SimEventType.Death && e.Target == Simulation.PlayerId)
                {
                    StopCleaveSlash();
                    CancelActiveAnchorMotionForReplacement();
                    _whirlwindContactPending = false;
                    for (int effect = 0; effect < _active.Length; effect++) Release(effect);
                    continue;
                }
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.AbilityCast) StopCleaveSlash();

                if (e.Type == SimEventType.Evaded)
                {
                    PlayEvade();
                }
                else if (e.Type == SimEventType.Attack)
                {
                    CancelActiveAnchorMotionForReplacement();
                    _whirlwindContactPending = false;
                    // Animator уже запускает ArenaView. Здесь начинается только
                    // additive-выпад корпуса, поэтому A/B не дёргается дважды.
                    BeginGameplayAttackMotion(e.Target);
                }
                else if (e.Type == SimEventType.ChainStepHop)
                {
                    if (e.Amount > 0)
                    {
                        _squallHop = e.ActionVariant;
                        _squallFinalHop = e.Amount == 1;
                        BeginChainHop(new Vector3(e.Position.X.ToFloat(), 0f, e.Position.Y.ToFloat()),
                            EntityPosition(e.Target, PlayerPosition()));
                    }
                }
                else if (e.Type == SimEventType.AbilityCast)
                {
                    // ДО 1 СЕНТЯБРЯ ЗДЕСЬ ЖИЛ ТОЛЬКО ВИХРЬ.
                    //
                    // Presentation трёх остальных способностей была написана
                    // целиком — prefab'ы, цепь, полёт якоря, — но вызывалась
                    // только из showcase-режима. В бою они кастовались молча
                    // и невидимо: логика двигала тела, а на экране не было
                    // ни якоря, ни цепи.
                    PlayGameplayAbility(e.Amount);
                }
                else if (e.Type == SimEventType.Damage
                         && e.DamageOrigin == DamageOrigin.BasicAttack)
                {
                    PlayBasicAttackImpact(e.Target, e.Position, e.Flag);
                    PulseCombatLight(0.62f);
                }
                else if (e.Type == SimEventType.Damage
                          && e.DamageOrigin == DamageOrigin.Ability)
                {
                    // A very long render frame may contain the contact Damage
                    // before the presentation timer gets its next Update. The
                    // authoritative event wins and releases the slash now.
                    // ActionVariant is the ability slot. Only the Whirlwind
                    // contact may consume this pending ring; a later anchor
                    // hit must never flush a stale ring from another cast.
                    if (_whirlwindContactPending && IsWhirlwindSlot(e.ActionVariant))
                        PlayWhirlwindContact();
                    AbilityBuild ability = (uint)e.ActionVariant < Simulation.AbilitySlots
                        ? _driver.Sim.GetAbility(e.ActionVariant) : null;
                    if (ability != null && ability.DefinitionId == AbilityDefinition.ChainCycloneId)
                        PlaySweepTargetPull(e.Target);
                    if (ability != null && ability.DefinitionId == AbilityDefinition.WhirlwindId)
                        PlayWhirlwindImpact(e.Target, e.Position);
                    if (ability != null && ability.DefinitionId == AbilityDefinition.CleaveId && e.DamageKind == DamageType.Physical)
                        PlayCleaveImpact(e.Target, e.Position);
                    if (ability != null && ability.DefinitionId == AbilityDefinition.ChainStepId)
                    {
                        PlaySquallImpact(e.Target, e.Position, _squallFinalHop);
                    }
                    if (ability == null || ability.DefinitionId != AbilityDefinition.CleaveId)
                        PulseCombatLight(IsWhirlwindSlot(e.ActionVariant) ? 0.30f : 0.46f);
                }
            }
        }

        private float _lastEvadeAt = -100f;

        private void PlayEvade()
        {
            if (Time.time - _lastEvadeAt < .2f) return;
            var sim = _driver.Sim;
            if (sim == null || !sim.Entities.Alive[Simulation.PlayerId]) return;
            _lastEvadeAt = Time.time;
            Camera camera = Camera.main;
            Quaternion rotation = camera != null ? camera.transform.rotation : Quaternion.identity;
            // Короткий воздушный росчерк не запускает hit-reaction, движение или новую атаку.
            Spawn(PelagVfxId.Evade, PlayerPosition() + Vector3.up * .9f,
                rotation * Quaternion.Euler(0f, 0f, -25f), .2f, .65f, .65f, Motion.Static);
        }

        [SerializeField, Range(.2f, 5f)] private float _cleaveImpactScale = 3f;

        private void StopCleaveSlash()
        {
            _cleaveVfxCast = -1;
            _cleaveSlashPlayed = false;
            _cleaveGroundPlayed = false;
            if (_active == null) return;
            for (int i = 0; i < _active.Length; i++)
                if (_active[i].Active && _active[i].Id == PelagVfxId.CleaveSlash) Release(i);
        }

        private void UpdateCleaveSlash()
        {
            if (_cleaveVfxCast < 0) return;
            Simulation sim = _driver.Sim;
            if (sim == null || !sim.CleaveActive || sim.CleaveStartTick != _cleaveVfxCast)
            { StopCleaveSlash(); return; }
            float tick = sim.Tick - 1 + _driver.Alpha;
            if (!_cleaveGroundPlayed && tick >= sim.CleaveContactTick)
            {
                _cleaveGroundPlayed = true;
                FixVec2 facingAtContact = sim.Entities.Facing[Simulation.PlayerId];
                Vector3 forward = new Vector3(facingAtContact.X.ToFloat(), 0f, facingAtContact.Y.ToFloat());
                Vector3 ground = PlayerPosition() + forward * 1.4f + Vector3.up * .025f;
                Spawn(PelagVfxId.CleaveGround, ground, Quaternion.identity, 1.2f, .65f, .65f, Motion.Static);
                if (CaptureRig.HasEnemyOverride) Debug.Log($"[cleave-ground-contact] tick={tick:F2} position={ground}");
            }
            if (_cleaveSlashPlayed || tick < sim.CleaveSwingStartTick) return;
            _cleaveSlashPlayed = true;
            if (!TryAcquire(PelagVfxId.CleaveSlash, out GameObject go, out PelagVfxElement element)) return;
            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Vector3 direction = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat()).normalized;
            Vector3 at = PlayerPosition() + Vector3.up * 1.5f;
            // Полукруг пака лежит в XY: нормаль вдоль правой стороны героя
            // помещает рассечение в вертикальную плоскость реального взмаха.
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, direction), Vector3.up)
                * Quaternion.Euler(0f, 0f, -35f);
            element.Begin(at, rotation);
            go.transform.localScale = new Vector3(2.05f, 2.05f, 1.15f) * _cleaveSlashScale;
            int index = ReserveActive();
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.CleaveSlash, Object = go, Element = element,
                Duration = .25f, Start = at, End = direction, JustSpawned = true,
                Motion = Motion.Cleave, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride) Debug.Log($"[cleave-heavy-slash] tick={tick:F2} position={at} scale={go.transform.localScale}");
        }

        private void PlayCleaveImpact(int targetEntity, FixVec2 fallback)
        {
            if (!TryAcquire(PelagVfxId.CleaveHit, out GameObject go, out PelagVfxElement element)) return;
            Vector3 position = EntityPosition(targetEntity, fallback) + Vector3.up * .9f;
            if (_arena.TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip))
            {
                Vector3 blade = bladeTip.position - bladeRoot.position;
                float t = blade.sqrMagnitude > .0001f
                    ? Mathf.Clamp01(Vector3.Dot(position - bladeRoot.position, blade) / blade.sqrMagnitude) : 0f;
                position = bladeRoot.position + blade * t;
            }
            // The pack's view-aligned mesh must sit in front of the target surface.
            Camera camera = Camera.main;
            if (camera != null) position += (camera.transform.position - position).normalized * .35f;
            int index = ReserveActive();
            element.Begin(position, _pools[(int)PelagVfxId.CleaveHit].AuthoredRotation);
            // Контакт одного тяжёлого удара должен перекрывать корпус цели, а не теряться у ног.
            go.transform.localScale *= _cleaveImpactScale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.CleaveHit, Object = go, Element = element,
                Duration = .32f, Start = position, End = position,
                Motion = Motion.Static, FollowIndex = -1
            };
        }

        private void PlayWhirlwindImpact(int targetEntity, FixVec2 fallback)
        {
            if (!TryAcquire(PelagVfxId.WhirlwindHit, out GameObject go, out PelagVfxElement element)) return;
            Vector3 position = EntityPosition(targetEntity, fallback) + Vector3.up * 0.85f;
            Camera camera = Camera.main;
            if (camera != null) position += (camera.transform.position - position).normalized * 0.45f;
            int index = ReserveActive();
            element.Begin(position, Quaternion.identity);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindHit, Object = go, Element = element,
                Duration = Mathf.Max(0.05f, element.DefaultLifetime),
                Start = position, End = position, Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-hit] target={targetEntity} scale={go.transform.localScale.x} lifetime={element.DefaultLifetime}");
        }

        private void PlaySquallImpact(int targetEntity, FixVec2 fallback, bool finisher)
        {
            PelagVfxId id = finisher ? PelagVfxId.ChainStepFinish : PelagVfxId.ChainStepHit;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return;
            Vector3 position = EntityPosition(targetEntity, fallback) + Vector3.up * .85f;
            Camera camera = Camera.main;
            if (camera != null) position += (camera.transform.position - position).normalized * .45f;
            // Плоскость меша уже ориентирует ParticleSystemRenderer. Здесь чередуем замахи A/B.
            float roll = (_squallHop % 2 == 0 ? 25f : -25f) + UnityEngine.Random.Range(-15f, 15f);
            element.Begin(position, Quaternion.Euler(0f, 0f, roll));
            int index = ReserveActive();
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = position, End = position,
                Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-impact] hop={_squallHop} id={id} target={targetEntity} scale={go.transform.localScale} lifetime={element.DefaultLifetime}");
        }

        private void PlayBasicAttackImpact(int targetEntity, FixVec2 fallback, bool critical)
        {
            PelagVfxId id = critical ? PelagVfxId.AutoAttackCriticalImpact : PelagVfxId.AutoAttackImpact;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element))
            {
                // Пока отдельный крит не назначен, сохраняем обычное подтверждение попадания.
                if (!critical) return;
                id = PelagVfxId.AutoAttackImpact;
                if (!TryAcquire(id, out go, out element)) return;
            }
            Vector3 position = EntityPosition(targetEntity, fallback) + Vector3.up * 0.85f;
            Camera camera = Camera.main;
            // Выносим вспышку перед поверхностью тела, чтобы она не скрывалась внутри модели.
            if (camera != null) position += (camera.transform.position - position).normalized * 0.45f;
            int index = ReserveActive();
            // Begin восстанавливает авторский масштаб; общий Spawn перезаписывает его.
            element.Begin(position, Quaternion.identity);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = Mathf.Max(0.05f, element.DefaultLifetime),
                Start = position, End = position, Motion = Motion.Static, FollowIndex = -1
            };
        }

        private void BeginGameplayAttackMotion(int targetEntity)
        {
            Vector3 player = PlayerPosition();

            // У пустого взмаха цели нет (targetEntity < 0), и запасной вариант
            // «метр по мировому Z» увёл бы выпад корпуса куда попало. Тело
            // должно подаваться туда, куда смотрит герой, — он уже развёрнут
            // на курсор симуляцией.
            Vector3 fallback = player + PlayerFacing();
            Vector3 target = EntityPosition(targetEntity, fallback);
            _attackMotionDirection = FlatDirection(player, target);
            _attackMotionTime = 0f;
        }

        /// <summary>Плоское направление взгляда героя из симуляции.</summary>
        private Vector3 PlayerFacing()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return Vector3.forward;
            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            var flat = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat());
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        }

        /// <summary>
        /// Стоит ли в этом слоте Вихрь.
        ///
        /// Спрашивается у симуляции, а не сравнивается с числом. Раньше здесь
        /// стояло `e.Amount == 0`, и это молча сломалось в тот день, когда кит
        /// Пелага занял все четыре слота и Вихрь переехал из нулевого в третий.
        /// Слот — это позиция на панели, а не имя способности.
        /// </summary>
        private bool IsWhirlwindSlot(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || slot < 0 || slot >= Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == AbilityDefinition.WhirlwindId;
        }

        private void ScheduleGameplayWhirlwind()
        {
            CancelActiveAnchorMotionForReplacement();
            _attackMotionTime = -1f;
            _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.zero);
            _whirlwindContactPending = true;
            _whirlwindContactDelay = WhirlwindContactTime;
            // Anticipation is visible, but the HDR peak belongs to contact.
            PulseCombatLight(0.06f);
        }

        public void BeginShowcase(PelagVfxShowcase showcase)
        {
            if (!PoolsReady || showcase == PelagVfxShowcase.None) return;
            StopShowcase();
            _showcase = showcase;
            _showcaseTime = 0f;
            _showcaseStage = 0;

            if (showcase != PelagVfxShowcase.Rotation)
                PlayShowcaseAbility(showcase);
        }

        public void StopShowcase()
        {
            ReleaseCyclone();
            // Showcase can be interrupted while the anchor is still traveling
            // (including when a new showcase replaces the current one). The
            // equipment state is presentation-only and must never survive that
            // interruption into the next pooled/reset view.
            _arena?.EndPlayerAnchorUse();
            _arena?.ClearPresentationOffsets();
            _showcase = PelagVfxShowcase.None;
            _motionAbility = PelagVfxShowcase.None;
            _captureMotion = false;
            _attackMotionTime = -1f;
            _whirlwindContactPending = false;
        }

        private void UpdateShowcase()
        {
            if (_showcase == PelagVfxShowcase.None) return;
            _showcaseTime += Time.deltaTime;

            if (_showcase == PelagVfxShowcase.Rotation)
            {
                if (_showcaseStage == 0 && _showcaseTime >= 0.25f) StartRotationStage(PelagVfxShowcase.AnchorSweep);
                else if (_showcaseStage == 1 && _showcaseTime >= 2.45f) StartRotationStage(PelagVfxShowcase.Whirlwind);
                else if (_showcaseStage == 2 && _showcaseTime >= 4.35f) StartRotationStage(PelagVfxShowcase.AnchorLeap);
                else if (_showcaseStage == 3 && _showcaseTime >= 6.55f) StartRotationStage(PelagVfxShowcase.ChainStep);
                else if (_showcaseStage == 4 && _showcaseTime >= 8.75f) StartRotationStage(PelagVfxShowcase.Autoattack);
                else if (_showcaseStage == 5 && _showcaseTime >= 10.2f) StopShowcase();
            }
            else if (_showcase == PelagVfxShowcase.Autoattack)
            {
                // Полная удерживаемая серия для QA: одиночный красивый кадр
                // не показывает, рвутся ли переходы A -> B -> finisher.
                if (_showcaseStage == 0 && _showcaseTime >= 0.80f)
                {
                    _showcaseStage = 1;
                    BeginAutoattack(FirstTargetPosition());
                }
                else if (_showcaseStage == 1 && _showcaseTime >= 1.60f)
                {
                    _showcaseStage = 2;
                    BeginAutoattack(FirstTargetPosition());
                }
                else if (_showcaseTime >= 2.48f)
                {
                    StopShowcase();
                }
            }
            else if (_showcaseTime >= ShowcaseDuration(_showcase))
            {
                StopShowcase();
            }
        }

        private void StartRotationStage(PelagVfxShowcase ability)
        {
            _showcaseStage++;
            _arena.ClearPresentationOffsets();
            PlayShowcaseAbility(ability);
        }

        private void PlayShowcaseAbility(PelagVfxShowcase ability)
        {
            switch (ability)
            {
                case PelagVfxShowcase.Autoattack: BeginAutoattack(FirstTargetPosition()); break;
                case PelagVfxShowcase.Whirlwind: PlayWhirlwind(true); break;
                case PelagVfxShowcase.AnchorLeap: PlayAnchorLeap(true); break;
                case PelagVfxShowcase.AnchorSweep: PlayAnchorSweep(true); break;
                case PelagVfxShowcase.ChainStep: PlayChainStep(true); break;
            }
        }

        private static float ShowcaseDuration(PelagVfxShowcase showcase)
        {
            switch (showcase)
            {
                case PelagVfxShowcase.Autoattack: return 2.48f;
                case PelagVfxShowcase.Whirlwind: return 1.85f;
                case PelagVfxShowcase.AnchorLeap: return 2.15f;
                case PelagVfxShowcase.AnchorSweep: return 2.15f;
                case PelagVfxShowcase.ChainStep: return 2.20f;
                default: return 1f;
            }
        }

        private void BeginAutoattack(Vector3 target)
        {
            CancelActiveAnchorMotionForReplacement();
            Vector3 player = PlayerPosition();
            Vector3 direction = FlatDirection(player, target);

            _arena.PlayPlayerAttackPresentation();
            _juice?.PlayBasicAttackTrail();
            _attackMotionTime = 0f;
            _attackMotionDirection = direction;

            // Этот путь существует только для отдельной VFX-витрины. В обычном
            // бою старт атаки не создаёт ни пыли, ни заранее нарисованной дуги.
        }

        private void UpdateAutoattackPresentation()
        {
            if (_attackMotionTime < 0f) return;
            _attackMotionTime += Time.deltaTime;

            // Presentation-lunge: только намёк на перенос веса. Симуляционную
            // позицию и дальность атаки он не меняет.
            //
            // БОЛЬШЕ ЭТИХ ЧИСЕЛ БРАТЬ НЕЛЬЗЯ — ПОЕДУТ НОГИ. Клипы удара
            // проиграны покадрово на риге, стопы переведены в игровые метры:
            //
            //   Saber A, окно выпада (кадры 2–17): правая стопа проходит
            //   6 см — она стоит. Прежний выпад тащил тело на +25.5 см,
            //   то есть опорная нога проезжала по полу четверть метра за
            //   0.3 с. Это и было «скольжение ног во время атаки».
            //
            //   Saber B, то же окно: обе стопы проходят по ~0.66 м — в клипе
            //   уже есть авторский подшаг, и выпад просто добавлялся сверху.
            //
            // Потолок — собственный ход опорной стопы, 6 см. Ниже 4 см перенос
            // веса перестаёт читаться, выше 8 см начинает ехать A.
            const float PullBack = -0.02f;
            const float LungeForward = 0.06f;

            float distance;
            if (_attackMotionTime < 0.10f)
                distance = Mathf.Lerp(0f, PullBack, Smooth(_attackMotionTime / 0.10f));
            else if (_attackMotionTime < AttackContactTime)
                distance = Mathf.Lerp(PullBack, LungeForward,
                    Smooth((_attackMotionTime - 0.10f) / Mathf.Max(0.01f, AttackContactTime - 0.10f)));
            else
                distance = Mathf.Lerp(LungeForward, 0f,
                    Smooth((_attackMotionTime - AttackContactTime) / 0.28f));
            _arena.SetPresentationOffset(Simulation.PlayerId,
                _attackMotionDirection * distance);

            if (_attackMotionTime >= 0.68f)
            {
                _attackMotionTime = -1f;
                _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.zero);
            }
        }

        private void PlayWhirlwind(bool showcase)
        {
            CancelActiveAnchorMotionForReplacement();
            _attackMotionTime = -1f;
            _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.zero);
            _arena.PlayPlayerAbilityPresentation(0);
            _juice?.PlayWhirlwindTrail();
            _whirlwindContactPending = true;
            _whirlwindContactDelay = WhirlwindContactTime;

            // ПОДГОТОВКА ЧЕРЕЗ КОНТРАСТ, А НЕ ЧЕРЕЗ ЯРКОСТЬ.
            //
            // Пик читается как пик только на фоне тихого замаха. Раньше замах
            // светил 0.28 при контакте 1.0 — разница меньше четырёх крат, и удар
            // выходил ровным. Теперь замах почти не светит, и вспышка контакта
            // бьёт на порядок.
            //
            // Именно занизить, а не погасить: PulseCombatLight берёт максимум и
            // клампит в 0..1, поэтому отрицательное значение было бы молчаливым
            // ничем, а не провалом света.
            PulseCombatLight(0.08f);
        }

        private void UpdateWhirlwindContact()
        {
            if (!_whirlwindContactPending) return;
            _whirlwindContactDelay -= Time.deltaTime;
            if (_whirlwindContactDelay > 0f) return;
            PlayWhirlwindContact();
        }

        private void PlayWhirlwindContact()
        {
            if (!_whirlwindContactPending) return;
            _whirlwindContactPending = false;

            // The outer crescent expands from Pelag, at the blade's height.
            // Its particles fade themselves before the pooled object is released.
            Vector3 center = PlayerPosition() + Vector3.up * 0.90f;
            float yaw = 0f;
            if (_arena.TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip))
            {
                Vector3 blade = bladeTip.position - bladeRoot.position;
                yaw = Mathf.Atan2(blade.x, blade.z) * Mathf.Rad2Deg;
            }
            if (TryAcquire(PelagVfxId.WhirlwindRing, out GameObject go, out PelagVfxElement element))
            {
                bool authored = element.AuthoredRadius > 0f;
                float radius = 2.3f;
                if (_driver.Sim != null)
                    for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                    {
                        AbilityBuild ability = _driver.Sim.GetAbility(slot);
                        if (ability != null && ability.DefinitionId == AbilityDefinition.WhirlwindId)
                        { radius = ability.Get(AbilityStatType.Radius).ToFloat(); break; }
                    }
                float scale = authored ? radius / element.AuthoredRadius : 0.9f;
                int brush = ReserveActive();
                element.Begin(center, Quaternion.Euler(authored ? 90f : 0f, yaw, 0f));
                go.transform.localScale = Vector3.one * scale;
                _active[brush] = new ActiveFx
                {
                    Active = true, Id = PelagVfxId.WhirlwindRing, Object = go, Element = element,
                    Duration = authored ? element.DefaultLifetime : 0.36f,
                    StartScale = scale, EndScale = authored ? scale : 1.1f,
                    Start = center, End = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward,
                    Motion = Motion.Whirlwind, FollowIndex = -1
                };
                if (CaptureRig.HasEnemyOverride)
                    Debug.Log($"[whirlwind-authored] authored={authored} radius={radius} scale={scale}");
            }
            PulseCombatLight(0.55f);
        }
        /// <summary>
        /// Показ способности по НАСТОЯЩЕМУ касту, а не по витрине.
        ///
        /// Отличие от showcase одно, но существенное: цель берётся из
        /// симуляции, а не выдумывается на три с половиной метра вперёд.
        /// Якорь обязан прилететь туда, куда действительно уехало тело, —
        /// иначе цепь показывает одно, а игрок оказывается в другом.
        /// </summary>
        private void PlayGameplayAbility(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || slot < 0 || slot >= Simulation.AbilitySlots) return;

            AbilityBuild build = sim.GetAbility(slot);
            if (build == null) return;

            int id = build.DefinitionId;
            if (id == AbilityDefinition.CleaveId)
            {
                CancelActiveAnchorMotionForReplacement();
                _whirlwindContactPending = false;
                _cleaveVfxCast = sim.CleaveStartTick;
                _cleaveSlashPlayed = false;
                _cleaveGroundPlayed = false;
                return;
            }
            if (id == AbilityDefinition.DashId)
            {
                CancelActiveAnchorMotionForReplacement();
                Vector3 from = PlayerPosition() + Vector3.up * .35f;
                Vector3 to = ForcedTargetWorld(sim) + Vector3.up * .35f;
                float duration = build.Get(AbilityStatType.DurationTicks).ToInt() / (float)Simulation.TicksPerSecond;
                int roll = SpawnMoving(PelagVfxId.RollDash, from, to, duration, 0f, Motion.Roll);
                Camera camera = Camera.main;
                if (roll >= 0 && camera != null)
                {
                    Vector3 direction = camera.WorldToScreenPoint(to) - camera.WorldToScreenPoint(from);
                    _active[roll].Object.transform.rotation = camera.transform.rotation
                        * Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    _active[roll].Object.transform.localScale *= .7f;
                }
                return;
            }
            if (id == AbilityDefinition.WhirlwindId)
            {
                ScheduleGameplayWhirlwind();
            }
            else if (id == AbilityDefinition.AnchorLeapId)
            {
                FixVec2 aimed = sim.LeapAim;
                Vector3 point = new Vector3(aimed.X.ToFloat(), 0f, aimed.Y.ToFloat());
                Vector3 origin = PlayerPosition();
                PlayAnchorLeapTo(origin + Vector3.ClampMagnitude(point - origin, AnchorKit.LeapRange.ToFloat()), false, slot);
            }
            else if (id == AbilityDefinition.ChainCycloneId)
            {
                CancelActiveAnchorMotionForReplacement();
            }
            else if (id == AbilityDefinition.ChainStepId)
            {
                PlayChainStep(false, slot);
            }
        }

        /// <summary>Куда симуляция реально тащит игрока, в мировых координатах.</summary>
        private Vector3 ForcedTargetWorld(Simulation sim)
        {
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            if (e.ForcedTicksLeft[player] <= 0) return PlayerPosition();

            FixVec2 t = e.ForcedTarget[player];
            return new Vector3(t.X.ToFloat(), 0f, t.Y.ToFloat());
        }

        private void PlayAnchorLeap(bool showcase)
        {
            // Capture должен проверять запрошенную дальность, а не всегда 3.8 м.
            Vector3 direction = CameraPlaneDirection(new Vector3(1f, 0f, .25f));
            float range = 3.8f;
            if (CaptureRig.IsVfxShowcase)
            {
                direction = Quaternion.Euler(0,CaptureRig.CastYaw,0) * direction;
                range = CaptureRig.CastDistance;
            }
            PlayAnchorLeapTo(PlayerPosition() + direction * range,showcase,1);
        }

        private void PlayAnchorLeapTo(Vector3 target, bool showcase, int slot)
        {
            Vector3 player = PlayerPosition();
            StartMotion(PelagVfxShowcase.AnchorLeap, player, target, showcase);
            if (showcase) _arena.PlayPlayerAbilityPresentation(slot, AbilityDefinition.AnchorLeapId);
            LaunchAnchor(PelagVfxId.AnchorLeapThrow, PelagVfxId.AnchorLeapChain,
                target + Vector3.up * 0.20f, PelagAbilityTiming.LeapRecovery);
        }

        private void PlayAnchorSweep(bool showcase, int slot = 2)
        {
            // Циклон демонстрируется реальным удержанием в TickDriver.
        }

        private void PlaySweepTargetPull(int entity)
        {
            Vector3 enemy = EntityPosition(entity, PlayerPosition());
            Spawn(PelagVfxId.CyclonePullImpact, enemy + Vector3.up * 0.7f, Quaternion.identity,
                0.25f, 0.4f, 0.65f, Motion.Expand);
            Spawn(PelagVfxId.DustSmall, enemy + Vector3.up * 0.05f, Quaternion.identity,
                0.35f, 0.6f, 0.9f, Motion.Expand);
        }

        private void PlayChainStep(bool showcase, int slot = 3)
        {
            _squallHop = 0;
            _squallFinalHop = false;
            Vector3 player = PlayerPosition();
            Vector3 target = showcase ? FirstTargetPosition() : ForcedTargetWorld(_driver.Sim);
            StartMotion(PelagVfxShowcase.ChainStep, player, target, showcase);
            _chainRouteOrigin = ChainHandPosition();
            if (!showcase) return;
            if (showcase) _arena.PlayPlayerAbilityPresentation(slot, AbilityDefinition.ChainStepId);
            BeginChainHop(player, target);
        }

        private void BeginChainHop(Vector3 from, Vector3 to)
        {
            for (int i = 0; i < _active.Length; i++)
                if (_active[i].Id == PelagVfxId.AnchorLeapThrow) Release(i);
            _arena.SetPlayerAbilityFacing(to - from);
            _motionStart = from;
            _motionEnd = to;
            _motionTime = 0f;
            _motionStartedAt = Time.time;
            _motionAbility = PelagVfxShowcase.ChainStep;
            _juice?.PlayChainSlashTrail();
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-hop] index={_squallHop} final={_squallFinalHop} from={from} to={to}");
            int dash = SpawnMoving(PelagVfxId.ChainStepDash, from + Vector3.up * 0.75f,
                to + Vector3.up * 0.75f, PelagAbilityTiming.ChainHop, 0f, Motion.Dash);
            Camera camera = Camera.main;
            if (dash >= 0 && camera != null)
            {
                Vector3 direction = camera.WorldToScreenPoint(to) - camera.WorldToScreenPoint(from);
                float roll = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                _active[dash].Object.transform.rotation = camera.transform.rotation * Quaternion.Euler(0, 0, roll);
            }
        }
        private int _squallHop;
        private bool _squallFinalHop;

        private Vector3 ChainHandPosition()
        {
            Vector3 hand = _arena.PlayerChainHandPosition;
            return hand != Vector3.zero ? hand : PlayerPosition() + Vector3.up;
        }

        private void LaunchAnchor(PelagVfxId head, PelagVfxId chain, Vector3 target, float duration)
        {
            int projectile = SpawnMoving(head, _arena.PlayerAnchorHeadPosition, target, duration, 0.50f, Motion.AnchorFlight);
            SpawnFollowingChain(chain, projectile, duration);
        }
        private void StartMotion(PelagVfxShowcase ability, Vector3 start, Vector3 end, bool showcase)
        {
            // A new cast may arrive before the previous anchor motion's timer
            // reaches FinishMotion. Release the old grip before replacing the
            // motion so one equipment state cannot leak across casts.
            CancelActiveAnchorMotionForReplacement();
            _motionAbility = ability;
            _motionTime = 0f;
            _motionStartedAt = Time.time;
            _motionStart = start;
            _motionEnd = end;
            _captureMotion = showcase;
            _leapWakePuffs = 0;
            _leapFlightStrokes = 0;
            _leapReleased = false;
            _leapLanded = false;
            _arena.SetPlayerAbilityFacing(end - start);
            if (ability != PelagVfxShowcase.ChainStep) _arena.BeginPlayerAnchorUse(ability == PelagVfxShowcase.AnchorLeap);
            FillTargets();
        }

        private void CancelActiveAnchorMotionForReplacement()
        {
            if (!IsAnchorMotion(_motionAbility) && _motionAbility != PelagVfxShowcase.ChainStep) return;
            StopLeapMotionVfx();
            for (int i = 0; i < _active.Length; i++)
            {
                PelagVfxId id = _active[i].Id;
                if (id == PelagVfxId.AnchorLeapThrow || id == PelagVfxId.CycloneHook
                    || id == PelagVfxId.AnchorLeapChain || id == PelagVfxId.CycloneChain
                    || id == PelagVfxId.CycloneWake || id == PelagVfxId.ChainStepDash)
                    Release(i);
            }
            if (IsAnchorMotion(_motionAbility)) _arena?.EndPlayerAnchorUse();
            _arena?.ClearPresentationOffsets();
            _motionAbility = PelagVfxShowcase.None;
            _motionTime = 0f;
            _captureMotion = false;
        }

        private void UpdateAbilityMotion()
        {
            if (_motionAbility == PelagVfxShowcase.None) return;
            // Оружие и оборудование используют часы одного кадра, включая кадр каста.
            _motionTime = Time.time - _motionStartedAt;

            switch (_motionAbility)
            {
                case PelagVfxShowcase.AnchorLeap: UpdateAnchorLeap(); break;
                case PelagVfxShowcase.AnchorSweep: break;
                case PelagVfxShowcase.ChainStep: UpdateChainStep(); break;
            }
        }

        /// <summary>Сколько клубов уже сброшено за текущий полёт.</summary>
        private int _leapWakePuffs;

        /// <summary>Сколько бликов цепи уже показано за текущую тягу.</summary>
        private int _leapFlightStrokes;
        private bool _leapReleased, _leapLanded;

        /// <summary>Три пакета полос воздуха за один рывок.</summary>
        private const int LeapFlightStrokeCount = 3;

        private TrailRenderer _heroTrail;
        private TrailRenderer[] _leapSideTrails;
        private PelagLeapPressureWave[] _leapBowWaves;

        /// <summary>Лента за героем на время тяги, светящимся проходом.</summary>
        private TrailRenderer HeroTrail()
        {
            if (_heroTrail != null) return _heroTrail;
            var go = new GameObject("Pelag Leap Trail");
            go.transform.SetParent(transform, false);
            _heroTrail = go.AddComponent<TrailRenderer>();
            _heroTrail.sharedMaterial =
                Resources.Load<Material>("VFX/Pelag/Materials/M_LeapStroke");
            _heroTrail.time = 0.24f;
            _heroTrail.widthCurve = new AnimationCurve(new Keyframe(0,0f), new Keyframe(.20f,1f), new Keyframe(1,0f));
            _heroTrail.widthMultiplier = 1.15f;
            
            _heroTrail.startColor = Color.white;
            _heroTrail.endColor = new Color(1f,1f,1f,0f);
            _heroTrail.textureMode = LineTextureMode.Stretch;
            _heroTrail.alignment = LineAlignment.View;
            _heroTrail.minVertexDistance = 0.015f;
            _heroTrail.numCapVertices = 2;
            _heroTrail.numCornerVertices = 4;
            _heroTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _heroTrail.receiveShadows = false;
            _heroTrail.emitting = false;
            _leapSideTrails = new TrailRenderer[2];
            for(int i=0;i<2;i++)
            {
                var side=new GameObject("Leap side stroke "+i); side.transform.SetParent(transform,false);
                var line=side.AddComponent<TrailRenderer>();
                line.sharedMaterial=_heroTrail.sharedMaterial;
                line.time=.18f+i*.035f; line.startWidth=.24f; line.endWidth=0;
                line.startColor=new Color(1,1,1,.9f); line.endColor=new Color(1,1,1,0);
                line.minVertexDistance=.015f; line.numCornerVertices=6; line.textureMode=LineTextureMode.Stretch;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows=false; line.emitting=false;
                _leapSideTrails[i]=line;
            }
            _leapBowWaves=new PelagLeapPressureWave[2];
            var pressureMaterial=Resources.Load<Material>("VFX/Pelag/Materials/M_LeapPressure");
            for(int i=0;i<2;i++)
            {
                var bow=new GameObject("Leap pressure shell "+i); bow.transform.SetParent(transform,false);
                var wave=bow.AddComponent<PelagLeapPressureWave>();
                wave.Initialize(pressureMaterial);_leapBowWaves[i]=wave;
            }
            return _heroTrail;
        }

        private void UpdateAnchorLeap()
        {
            float phase = Mathf.Clamp01((_motionTime - PelagAbilityTiming.LeapWindup) / PelagAbilityTiming.LeapTravel);
            float distance = Vector3.Distance(_motionStart, _motionEnd);
            float height = Mathf.Sin(phase * Mathf.PI) * .45f * Mathf.Clamp01(distance / 7f);
            if (!_captureMotion)
                _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.up * height);
            else
                _arena.SetPresentationOffset(Simulation.PlayerId,
                    (_motionEnd - _motionStart) * Smooth(phase) + Vector3.up * height);

            // Широкий росчерк и две разнесённые кромки показывают скорость всего корпуса.
            TrailRenderer trail = HeroTrail();
            bool pulling = _motionTime >= PelagAbilityTiming.LeapWindup
                           && _motionTime < PelagAbilityTiming.LeapArrival;
            trail.transform.position = PlayerPosition() + Vector3.up * .65f;
            if (pulling && !trail.emitting) trail.Clear();
            trail.emitting = pulling;
            Vector3 leapDirection=FlatDirection(_motionStart,_motionEnd);
            Vector3 sideDirection=Vector3.Cross(Vector3.up,leapDirection);
            for(int i=0;i<_leapSideTrails.Length;i++)
            {
                var side=_leapSideTrails[i];
                side.transform.position=PlayerPosition()+Vector3.up*(i==0?.3f:1.05f)
                    +sideDirection*(i==0?-.42f:.42f)-leapDirection*.12f;
                if(pulling&&!side.emitting) side.Clear();
                side.emitting=pulling;
            }

            for(int layer=0;layer<_leapBowWaves.Length;layer++)
                _leapBowWaves[layer].Present(PlayerPosition()+Vector3.up*.95f+leapDirection*.8f,
                    leapDirection,_motionTime-PelagAbilityTiming.LeapWindup,layer,pulling);

            if (_motionTime >= PelagAbilityTiming.LeapRelease && !_leapReleased)
            {
                Spawn(PelagVfxId.TargetFlash, _arena.PlayerAnchorHeadPosition,
                    Quaternion.LookRotation(FlatDirection(_motionStart, _motionEnd), Vector3.up),
                    .09f, .16f, .23f, Motion.Expand);
                _leapReleased = true;
            }

            if (_motionTime >= PelagAbilityTiming.LeapWindup && _leapWakePuffs == 0)
            {
                // Втыкание металла и отрыв ног — отдельные короткие импульсы.
                Spawn(PelagVfxId.AnchorLeapLand, _motionEnd + Vector3.up * .07f,
                    Quaternion.LookRotation(FlatDirection(_motionEnd, _motionStart), Vector3.up),
                    .62f, 1f, 1f, Motion.Expand);
                Spawn(PelagVfxId.DustSmall, _motionStart + Vector3.up * .06f,
                    Quaternion.identity, .24f, .55f, .75f, Motion.Expand);
                PulseCombatLight(.55f);
                _juice?.PunchCamera(.24f, .06f);
                _leapWakePuffs = 1;
            }
            // Пакеты острых следов остаются позади героя на каждой трети рывка.
            if (pulling && _leapFlightStrokes < LeapFlightStrokeCount)
            {
                float flown = (_motionTime - PelagAbilityTiming.LeapWindup)
                              / PelagAbilityTiming.LeapTravel;
                if (flown >= (_leapFlightStrokes + 1f) / (LeapFlightStrokeCount + 1f))
                {
                    Vector3 hero = PlayerPosition() + Vector3.up * .78f;
                    Spawn(PelagVfxId.AnchorLeapFlight, hero - leapDirection * .25f,
                        Quaternion.LookRotation(FlatDirection(_motionStart, _motionEnd), Vector3.up),
                        .30f, 1f, 1f, Motion.Static);
                    _leapFlightStrokes++;
                }
            }

            if (_motionTime >= PelagAbilityTiming.LeapArrival && !_leapLanded)
            {
                // Пыль расходится от ног; повторный взрыв скрывал момент посадки.
                Spawn(PelagVfxId.AnchorLeapLanding, PlayerPosition() + Vector3.up * .06f,
                    Quaternion.LookRotation(FlatDirection(_motionStart, _motionEnd)),
                    .72f, 1f, 1f, Motion.Expand);
                _juice?.PunchCamera(.32f, .06f);
                _leapLanded = true;
            }
            if (_motionTime >= PelagAbilityTiming.LeapRecovery) FinishMotion();
        }
        private void UpdateChainStep()
        {
            if (_captureMotion)
                _arena.SetPresentationOffset(Simulation.PlayerId,
                    Vector3.Lerp(_motionStart, _motionEnd, Smooth(_motionTime / PelagAbilityTiming.ChainHop))
                    - BasePlayerPosition());
            if (_motionTime >= PelagAbilityTiming.ChainHop + 0.08f) FinishMotion();
        }
        private void StopLeapMotionVfx()
        {
            // Прерывание не доходит до конца анимации: дуги нужно погасить сразу.
            if(_heroTrail!=null) { _heroTrail.emitting=false;_heroTrail.Clear(); }
            if(_leapSideTrails!=null)
                foreach(var trail in _leapSideTrails) { trail.emitting=false;trail.Clear(); }
            if(_leapBowWaves!=null)
                foreach(var bow in _leapBowWaves) bow.Hide();
        }

        private void FinishMotion()
        {
            _leapWakePuffs = 0;
            _leapFlightStrokes = 0;
            StopLeapMotionVfx();

            PelagVfxShowcase finishedMotion = _motionAbility;
            if (IsAnchorMotion(finishedMotion))
                _arena?.EndPlayerAnchorUse();
            _arena?.ClearPresentationOffsets();
            _motionAbility = PelagVfxShowcase.None;
            _captureMotion = false;
        }

        private static bool IsAnchorMotion(PelagVfxShowcase ability)
        {
            return ability == PelagVfxShowcase.AnchorLeap
                   || ability == PelagVfxShowcase.AnchorSweep
;
        }

        private int Spawn(PelagVfxId id, Vector3 position, Quaternion rotation, float duration,
            float startScale, float endScale, Motion motion)
        {
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            element.Begin(position, rotation);
            go.transform.localScale = Vector3.one * startScale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration, StartScale = startScale, EndScale = endScale,
                Start = position, End = position, Motion = motion, FollowIndex = -1
            };
            return index;
        }

        private int SpawnMoving(PelagVfxId id, Vector3 start, Vector3 end, float duration,
            float arcHeight, Motion motion)
        {
            int index = Spawn(id, start, Quaternion.LookRotation(FlatDirection(start, end), Vector3.up),
                duration, 1f, 1f, motion);
            if (index < 0) return -1;
            _active[index].Start = start;
            _active[index].End = end;
            _active[index].ArcHeight = arcHeight;
            return index;
        }

        private int SpawnLine(PelagVfxId id, Vector3 start, Vector3 end, float duration)
        {
            int index = Spawn(id, Vector3.zero, Quaternion.identity, duration, 1f, 1f, Motion.PullLine);
            if (index < 0) return -1;
            _active[index].Start = start;
            _active[index].End = end;
            _active[index].Element.SetLine(start, Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.18f, end);
            return index;
        }

        private void SpawnFollowingChain(PelagVfxId id, int projectileIndex, float duration)
        {
            int index = Spawn(id, Vector3.zero, Quaternion.identity, duration, 1f, 1f, Motion.Chain);
            if (index < 0) return;
            _active[index].FollowIndex = projectileIndex;
            _active[index].End = projectileIndex >= 0 ? _active[projectileIndex].End : PlayerPosition();
        }

        private bool TryAcquire(PelagVfxId id, out GameObject go, out PelagVfxElement element)
        {
            go = null;
            element = null;
            int index = (int)id;
            if (_pools == null || (uint)index >= (uint)_pools.Length || _pools[index] == null) return false;
            go = _pools[index].Pool.Acquire();
            element = go.GetComponent<PelagVfxElement>();
            if (element != null) return true;
            _pools[index].Pool.Release(go);
            go = null;
            return false;
        }

        private int ReserveActive()
        {
            for (int offset = 0; offset < _active.Length; offset++)
            {
                int index = (_activeCursor + offset) % _active.Length;
                if (_active[index].Active) continue;
                _activeCursor = (index + 1) % _active.Length;
                return index;
            }

            int fallback = _activeCursor++ % _active.Length;
            Release(fallback);
            return fallback;
        }

        private void UpdateActive(float dt)
        {
            if (_active == null) return;
            for (int i = 0; i < _active.Length; i++)
            {
                ref ActiveFx fx = ref _active[i];
                if (!fx.Active) continue;
                if (fx.Object == null || fx.Element == null)
                {
                    fx = default;
                    continue;
                }
                if (fx.JustSpawned) fx.JustSpawned = false;
                else fx.Age += dt;
                if (CaptureRig.HasEnemyOverride && fx.Id == PelagVfxId.FootstepDust
                    && fx.Age >= 0.1f && fx.Age - dt < 0.1f)
                    Debug.Log($"[footstep-particles] count={fx.Object.GetComponent<ParticleSystem>().particleCount}");
                float t = Mathf.Clamp01(fx.Age / Mathf.Max(0.01f, fx.Duration));

                switch (fx.Motion)
                {
                    case Motion.Cleave:
                        // Центр остаётся у героя; меняется только угол рассечения.
                        // Эффект не летит к цели и не растягивается за её движением.
                        float sweep = Smooth(Mathf.Clamp01(fx.Age / .16f));
                        fx.Object.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, fx.End), Vector3.up)
                            * Quaternion.Euler(0f, 0f, Mathf.Lerp(-35f, 40f, sweep));
                        break;
                    case Motion.AnchorFlight:
                        fx.Age = _motionTime;
                        float returnAt = fx.Duration - .06f;
                        // Якорь улетает в тот же момент, в который рука его
                        // отпускает в клипе. Раньше здесь стоял AnchorDraw —
                        // момент ДОСТАВАНИЯ якоря, и снаряд уходил за четверть
                        // секунды до броска: замах ещё шёл, а якорь уже почти
                        // долетел.
                        bool released = fx.Age >= PelagAbilityTiming.LeapRelease;
                        fx.Object.SetActive(released && fx.Age < returnAt);
                        if (!released) { fx.Start = _arena.PlayerAnchorHeadPosition; break; }
                        float outward = PelagAbilityTiming.LeapWindup;
                        // Возврат цепи не рисует второй бросок в обратную сторону.
                        fx.Element?.SetTrailEmission(fx.Age < outward);
                        float retract = PelagAbilityTiming.LeapArrival;
                        float flight = Smooth((fx.Age - PelagAbilityTiming.LeapRelease)
                            / (outward - PelagAbilityTiming.LeapRelease));
                        Vector3 anchorPosition = Vector3.Lerp(fx.Start, fx.End, flight)
                            + Vector3.up * (Mathf.Sin(flight * Mathf.PI) * fx.ArcHeight);
                        anchorPosition = Vector3.Lerp(anchorPosition, _arena.PlayerAnchorHeadPosition,
                            Smooth((fx.Age - retract) / (returnAt - retract)));
                        // Корень летит прямо, кренится только модель якоря.
                        // След висит на корне: пока крен был на нём, след
                        // наматывался на вращение и рисовал спираль.
                        fx.Object.transform.rotation =
                            Quaternion.LookRotation(FlatDirection(fx.Start, fx.End), Vector3.up);
                        Transform spinner = fx.Element != null ? fx.Element.Spinner : null;
                        if (spinner != null)
                            spinner.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-45f, 20f, flight));
                        fx.Object.transform.position = anchorPosition;
                        break;
                    case Motion.EnemyPull:
                        Vector3 enemy = EntityPosition(fx.FollowIndex, fx.End) + Vector3.up * 0.65f;
                        Vector3 hand = ChainHandPosition();
                        float pullPhase = Smooth((fx.Age - PelagAbilityTiming.SweepWindup) / 0.16f);
                        Vector3 sideways = Vector3.Cross(Vector3.up, FlatDirection(hand, enemy));
                        float reach = Vector3.Distance(hand, enemy);
                        Vector3 belly = (hand + enemy) * 0.5f
                            + sideways * (Mathf.Min(2.4f, reach * 0.65f) * (1f - pullPhase))
                            + Vector3.up * Mathf.Lerp(0.85f, -0.20f, pullPhase);
                        float extend = Smooth(fx.Age / PelagAbilityTiming.SweepWindup);
                        float withdraw = Smooth((fx.Age - PelagAbilityTiming.SweepWindup - PelagAbilityTiming.SweepTravel) / 0.08f);
                        fx.Element.SetLineProgress(hand, belly, enemy, extend * (1f - withdraw));
                        break;
                    case Motion.HopChain:
                        Vector3 hopHand = ChainHandPosition();
                        // Hold the route between departure and destination through
                        // the slash, then wind it back into the left hand.
                        Vector3 tail = Vector3.Lerp(fx.Start, hopHand, Smooth((fx.Age - PelagAbilityTiming.ChainHop) / 0.12f));
                        Vector3 hopBend = (tail + fx.End) * 0.5f
                            + Vector3.Cross(Vector3.up, FlatDirection(tail, fx.End)) * (0.65f * (1f - Smooth(t)))
                            + Vector3.up * 0.32f;
                        Vector3 chainEnd = Vector3.Lerp(fx.End, hopHand,
                            Smooth((fx.Age - PelagAbilityTiming.ChainHop - 0.05f) / 0.07f));
                        fx.Element.SetLineProgress(tail, hopBend, chainEnd, Smooth(fx.Age / 0.035f));
                        break;
                    case Motion.Whirlwind:
                        fx.Object.transform.position = PlayerPosition() + Vector3.up * 0.90f;
                        fx.Object.transform.localScale = Vector3.one * Mathf.Lerp(fx.StartScale, fx.EndScale, Smooth(t));
                        if (_arena.TryGetPlayerBlade(out Transform bladeBase, out Transform bladeEnd))
                        {
                            Vector3 direction = Vector3.ProjectOnPlane(bladeEnd.position - bladeBase.position, Vector3.up).normalized;
                            if (direction.sqrMagnitude > .01f)
                            {
                                float turn = Vector3.SignedAngle(fx.End, direction, Vector3.up);
                                fx.Object.transform.Rotate(Vector3.up, turn, Space.World);
                                fx.End = direction;
                            }
                        }
                        fx.Element.AnimateBrush(fx.Age);
                        fx.Element.SetOpacity(1f - Smooth((t - 0.32f) / 0.68f));
                        break;
                    case Motion.Expand:
                        float pulse = Mathf.Sin(t * Mathf.PI) * 0.08f;
                        fx.Object.transform.localScale = Vector3.one *
                            (Mathf.Lerp(fx.StartScale, fx.EndScale, Smooth(t)) + pulse);
                        if (fx.Id == PelagVfxId.ChainStepHit)
                        {
                            fx.Element.AnimateBrush(fx.Age * 1.5f);
                            fx.Element.SetOpacity(1f - Smooth((t - .25f) / .75f));
                        }
                        break;
                    case Motion.Projectile:
                        Vector3 projectile = Vector3.Lerp(fx.Start, fx.End, Smooth(t));
                        projectile.y += Mathf.Sin(t * Mathf.PI) * fx.ArcHeight;
                        fx.Object.transform.position = projectile;
                        fx.Object.transform.Rotate(Vector3.right, 900f * dt, Space.Self);
                        break;
                    case Motion.Dash:
                        fx.Object.transform.position = PlayerPosition() + Vector3.up * .75f;
                        break;
                    case Motion.Roll:
                        var rollSim = _driver != null ? _driver.Sim : null;
                        if (rollSim == null || rollSim.Entities.ForcedKind[Simulation.PlayerId] != (byte)ForcedMotionKind.Roll)
                        { Release(i); continue; }
                        fx.Object.transform.position = PlayerPosition() + Vector3.up * .35f;
                        break;
                    case Motion.Chain:
                        fx.Age = _motionTime;
                        if (fx.Age < PelagAbilityTiming.AnchorDraw)
                        { fx.Element.SetLineProgress(ChainHandPosition(), ChainHandPosition(), ChainHandPosition(), 0f); break; }
                        Vector3 from = ChainHandPosition();
                        Vector3 to = fx.End;
                        if ((uint)fx.FollowIndex < (uint)_active.Length && _active[fx.FollowIndex].Active)
                            to = _active[fx.FollowIndex].Object.transform.position;
                        // Throw phase extends link by link, then holds under
                        // tension before a short retract. AnchorLeap is
                        // snappy; AnchorSweep stays taut longer while the
                        // pull resolves across the group.
                        float progress = 1f;
                        Vector3 tip = Vector3.Lerp(from, to, progress);
                        float slack = 1f - Smooth(fx.Age / 0.24f);
                        Vector3 bend = Vector3.Lerp(from, tip, 0.5f)
                            + Vector3.Cross(Vector3.up, FlatDirection(from, to)) * slack * 1.2f
                            + Vector3.up * Mathf.Lerp(-0.20f, 0.6f, slack);
                        fx.Element.SetLineProgress(from, bend, to, progress);
                        break;
                }

                if (fx.Age >= fx.Duration) Release(i);
            }
        }

        private void Release(int index)
        {
            if (_active == null || (uint)index >= (uint)_active.Length) return;
            ref ActiveFx fx = ref _active[index];
            if (!fx.Active) return;
            if (fx.Element != null) fx.Element.End();
            int poolIndex = (int)fx.Id;
            if (fx.Object != null && _pools != null
                                  && (uint)poolIndex < (uint)_pools.Length
                                  && _pools[poolIndex] != null)
                _pools[poolIndex].Pool.Release(fx.Object);
            else if (fx.Object != null)
                fx.Object.SetActive(false);
            fx = default;
        }

        private void FillTargets()
        {
            _targetCount = 0;
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            EntityStore entities = sim.Entities;
            Vector3 player = BasePlayerPosition();
            for (int i = 0; i < entities.Count; i++)
            {
                if (i == Simulation.PlayerId || !entities.Alive[i]) continue;
                if (entities.Side[i] == entities.Side[Simulation.PlayerId]) continue;
                Vector3 position = _driver.GetRenderPosition(i);
                float distanceSq = (position - player).sqrMagnitude;

                int insert;
                if (_targetCount < _targets.Length)
                {
                    insert = _targetCount++;
                }
                else
                {
                    if (distanceSq >= _targetDistanceSq[_targetCount - 1]) continue;
                    insert = _targetCount - 1;
                }

                while (insert > 0 && distanceSq < _targetDistanceSq[insert - 1])
                {
                    _targets[insert] = _targets[insert - 1];
                    _targetDistanceSq[insert] = _targetDistanceSq[insert - 1];
                    insert--;
                }
                _targets[insert] = i;
                _targetDistanceSq[insert] = distanceSq;
            }
        }

        private Vector3 FirstTargetPosition()
        {
            FillTargets();
            if (_targetCount > 0) return EntityPosition(_targets[0], PlayerPosition() + Vector3.forward * 1.4f);
            return PlayerPosition() + CameraPlaneDirection(Vector3.forward) * 1.4f;
        }

        private Vector3 AverageTargetPosition(Vector3 fallback)
        {
            FillTargets();
            if (_targetCount == 0) return fallback;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _targetCount; i++) sum += EntityPosition(_targets[i], fallback);
            return sum / _targetCount;
        }

        private Vector3 PlayerPosition()
        {
            return _arena.TryGetEntityView(Simulation.PlayerId, out Transform view)
                ? view.position : BasePlayerPosition();
        }

        private Vector3 BasePlayerPosition()
        {
            return _driver.Sim != null ? _driver.GetRenderPosition(Simulation.PlayerId) : Vector3.zero;
        }

        private Vector3 EntityPosition(int entityId, FixVec2 fallback)
        {
            return EntityPosition(entityId, new Vector3(fallback.X.ToFloat(), 0f, fallback.Y.ToFloat()));
        }

        private Vector3 EntityPosition(int entityId, Vector3 fallback)
        {
            return _arena.TryGetEntityView(entityId, out Transform view) ? view.position : fallback;
        }

        private Vector3 BaseEntityPosition(int entityId, Vector3 fallback)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || (uint)entityId >= (uint)sim.Entities.Count) return fallback;
            return _driver.GetRenderPosition(entityId);
        }

        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        private static Vector3 CameraPlaneDirection(Vector3 fallback)
        {
            Camera camera = Camera.main;
            if (camera == null) return fallback.normalized;
            Vector3 right = camera.transform.right;
            right.y = 0f;
            return right.sqrMagnitude > 0.0001f ? right.normalized : fallback.normalized;
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
