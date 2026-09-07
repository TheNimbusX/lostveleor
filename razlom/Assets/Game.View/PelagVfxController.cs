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

        private enum Motion : byte { Static, Expand, Projectile, Dash, Chain, PullLine, Whirlwind, AnchorFlight, EnemyPull, HopChain }

        private sealed class PoolRecord
        {
            public ViewPool Pool;
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
        private float _whirlwindContactDelay;
        private Light _heroLight;
        private float _combatLightPulse;
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
        }

        private void LateUpdate()
        {
            if (_driver == null || _arena == null) return;
            ConsumeSimEvents();
            UpdateShowcase();
            UpdateAutoattackPresentation();
            UpdateWhirlwindContact();
            UpdateAbilityMotion();
            UpdateActive(Time.deltaTime);
            UpdateCyclonePresentation();
            UpdateCombatLighting(Time.unscaledDeltaTime);
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
                    Pool = new ViewPool(parent, () => Instantiate(prefab), Mathf.Max(1, entry.Prewarm))
                };
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
                    CancelActiveAnchorMotionForReplacement();
                    _whirlwindContactPending = false;
                    for (int effect = 0; effect < _active.Length; effect++) Release(effect);
                    continue;
                }
                if (e.Source != Simulation.PlayerId) continue;

                if (e.Type == SimEventType.Attack)
                {
                    CancelActiveAnchorMotionForReplacement();
                    _whirlwindContactPending = false;
                    // Animator уже запускает ArenaView. Здесь начинается только
                    // additive-выпад корпуса, поэтому A/B не дёргается дважды.
                    BeginGameplayAttackMotion(e.Target);
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
                    // The old authored prefab contained a stale blue sword and
                    // rendered it through the enemy's torso. Contact geometry
                    // now belongs to CombatJuice; this layer only adds light.
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
                    {
                        Vector3 victim = EntityPosition(e.Target, PlayerPosition());
                        Vector3 impact = victim + FlatDirection(victim, PlayerPosition()) * 0.28f + Vector3.up * 0.8f;
                        Spawn(PelagVfxId.WhirlwindHit, impact, Quaternion.identity,
                            0.36f, 0.8f, 1.05f, Motion.Expand);
                    }
                    if (ability != null && ability.DefinitionId == AbilityDefinition.ChainStepId)
                    {
                        _squallContacts++;
                        bool finisher = _squallContacts == AnchorKit.ChainMaxHops;
                        Spawn(PelagVfxId.ChainStepHit, EntityPosition(e.Target, PlayerPosition()) + Vector3.up * 0.75f,
                            Quaternion.LookRotation(_motionEnd - _motionStart + Vector3.up * 0.001f)
                                * Quaternion.Euler(0, 0, finisher ? -35f : 25f),
                            finisher ? .3f : .22f, finisher ? 1.05f : .75f, finisher ? 1.55f : 1.1f, Motion.Expand);
                        var context = i < _driver.FrameEventContexts.Count ? _driver.FrameEventContexts[i] : default;
                        if (context.SourceForcedTicksLeft > 0
                            && _driver.Sim.Entities.ForcedTicksLeft[Simulation.PlayerId] > 0)
                        {
                            _chainRouteOrigin = EntityPosition(e.Target, PlayerPosition()) + Vector3.up * 0.9f;
                            BeginChainHop(PlayerPosition(), ForcedTargetWorld(_driver.Sim));
                        }
                    }
                    PulseCombatLight(IsWhirlwindSlot(e.ActionVariant) ? 0.30f : 0.46f);
                }
            }
        }

        private void BeginGameplayAttackMotion(int targetEntity)
        {
            Vector3 player = PlayerPosition();
            Vector3 target = EntityPosition(targetEntity, player + Vector3.forward);
            _attackMotionDirection = FlatDirection(player, target);
            _attackMotionTime = 0f;
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
            int brush = Spawn(PelagVfxId.WhirlwindRing, center, Quaternion.Euler(0f, yaw, 0f),
                0.36f, 0.90f, 1.10f, Motion.Whirlwind);
            if (brush >= 0) _active[brush].End = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Spawn(PelagVfxId.DustHeavy, PlayerPosition() + Vector3.up * 0.04f,
                Quaternion.identity, 0.36f, 0.8f, 1.35f, Motion.Expand);
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
            // Витрина целится сама: настоящей цели в этом режиме нет.
            PlayAnchorLeapTo(
                PlayerPosition() + CameraPlaneDirection(new Vector3(1f, 0f, 0.25f)) * 3.8f,
                showcase, 1);
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
            Spawn(PelagVfxId.WhirlwindHit, enemy + Vector3.up * 0.7f, Quaternion.identity,
                0.25f, 0.4f, 0.65f, Motion.Expand);
            Spawn(PelagVfxId.DustSmall, enemy + Vector3.up * 0.05f, Quaternion.identity,
                0.35f, 0.6f, 0.9f, Motion.Expand);
        }

        private void PlayChainStep(bool showcase, int slot = 3)
        {
            _squallContacts = 0;
            Vector3 player = PlayerPosition();
            Vector3 target = showcase ? FirstTargetPosition() : ForcedTargetWorld(_driver.Sim);
            StartMotion(PelagVfxShowcase.ChainStep, player, target, showcase);
            _chainRouteOrigin = ChainHandPosition();
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
            Spawn(PelagVfxId.ChainStepHit, from + Vector3.up * 0.85f,
                Quaternion.LookRotation(FlatDirection(from, to)), 0.18f, 0.45f, 0.85f, Motion.Expand);
            SpawnMoving(PelagVfxId.ChainStepDash, from + Vector3.up * 0.75f,
                to + Vector3.up * 0.75f, PelagAbilityTiming.ChainHop, 0f, Motion.Dash);
        }
        private int _squallContacts;

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
            _arena.SetPlayerAbilityFacing(end - start);
            if (ability != PelagVfxShowcase.ChainStep) _arena.BeginPlayerAnchorUse(ability == PelagVfxShowcase.AnchorLeap);
            FillTargets();
        }

        private void CancelActiveAnchorMotionForReplacement()
        {
            if (!IsAnchorMotion(_motionAbility)) return;
            for (int i = 0; i < _active.Length; i++)
            {
                PelagVfxId id = _active[i].Id;
                if (id == PelagVfxId.AnchorLeapThrow || id == PelagVfxId.CycloneHook
                    || id == PelagVfxId.AnchorLeapChain || id == PelagVfxId.CycloneChain
                    || id == PelagVfxId.CycloneWake || id == PelagVfxId.ChainStepDash)
                    Release(i);
            }
            _arena?.EndPlayerAnchorUse();
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

        private void UpdateAnchorLeap()
        {
            float travel = PelagAbilityTiming.LeapArrival;
            float phase = Mathf.Clamp01((_motionTime - PelagAbilityTiming.LeapWindup) / PelagAbilityTiming.LeapTravel);
            float height = Mathf.Sin(phase * Mathf.PI) * 1.5f * Mathf.Clamp01(Vector3.Distance(_motionStart, _motionEnd) / 7f);
            if (!_captureMotion) _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.up * height);
            if (_captureMotion)
                _arena.SetPresentationOffset(Simulation.PlayerId,
                    (_motionEnd - _motionStart) * Smooth(phase) + Vector3.up * height);
            if (_motionTime >= PelagAbilityTiming.LeapWindup && _motionTime - Time.deltaTime < PelagAbilityTiming.LeapWindup)
            {
                Spawn(PelagVfxId.AnchorLeapLand, _motionEnd + Vector3.up * 0.06f,
                    Quaternion.LookRotation(FlatDirection(_motionEnd, _motionStart), Vector3.up),
                    0.42f, 0.52f, 0.72f, Motion.Expand);
                PulseCombatLight(0.9f);
            }
            if (_motionTime >= travel && _motionTime - Time.deltaTime < travel)
                Spawn(PelagVfxId.AnchorLeapLand, PlayerPosition() + Vector3.up * 0.04f,
                    Quaternion.LookRotation(FlatDirection(_motionStart, _motionEnd)),
                    0.48f, 0.8f, 1.25f, Motion.Expand);
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
        private void FinishMotion()
        {
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
                fx.Age += dt;
                float t = Mathf.Clamp01(fx.Age / Mathf.Max(0.01f, fx.Duration));

                switch (fx.Motion)
                {
                    case Motion.AnchorFlight:
                        fx.Age = _motionTime;
                        float returnAt = fx.Duration - .06f;
                        bool released = fx.Age >= PelagAbilityTiming.AnchorDraw;
                        fx.Object.SetActive(released && fx.Age < returnAt);
                        if (!released) { fx.Start = _arena.PlayerAnchorHeadPosition; break; }
                        float outward = PelagAbilityTiming.LeapWindup;
                        float retract = PelagAbilityTiming.LeapArrival;
                        float flight = Smooth((fx.Age - PelagAbilityTiming.AnchorDraw)
                            / (outward - PelagAbilityTiming.AnchorDraw));
                        Vector3 anchorPosition = Vector3.Lerp(fx.Start, fx.End, flight)
                            + Vector3.up * (Mathf.Sin(flight * Mathf.PI) * fx.ArcHeight);
                        anchorPosition = Vector3.Lerp(anchorPosition, _arena.PlayerAnchorHeadPosition,
                            Smooth((fx.Age - retract) / (returnAt - retract)));
                        fx.Object.transform.rotation = Quaternion.LookRotation(FlatDirection(fx.Start, fx.End), Vector3.up)
                            * Quaternion.Euler(0f, 0f, Mathf.Lerp(-45f, 20f, flight));
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
                        fx.Object.transform.position = Vector3.Lerp(fx.Start, fx.End, Smooth(t));
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
