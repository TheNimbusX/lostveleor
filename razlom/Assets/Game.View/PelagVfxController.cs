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
    public sealed class PelagVfxController : MonoBehaviour
    {
        private const string LibraryPath = "VFX/Pelag/AbilityVfxLibrary";
        private const int MaxActive = 72;
        private const int MaxTargets = 8;
        private const float BrightVfxLifetime = 5f / 30f;
        private const float FlipbookLifetime = 16f / 30f;
        private static readonly float AttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        private const float WhirlwindContactTime = 10f / Simulation.TicksPerSecond;

        private enum Motion : byte { Static, Expand, Projectile, Dash, Chain, PullLine }

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
        private Vector3 _motionStart;
        private Vector3 _motionEnd;
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
                if (e.Source != Simulation.PlayerId) continue;

                if (e.Type == SimEventType.Attack)
                {
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
                    PulseCombatLight(0.92f);
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
            _attackMotionTime = -1f;
            _arena.SetPresentationOffset(Simulation.PlayerId, Vector3.zero);
            _whirlwindContactPending = true;
            _whirlwindContactDelay = WhirlwindContactTime;
            // Anticipation is visible, but the HDR peak belongs to contact.
            PulseCombatLight(0.28f);
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

            // Небольшая presentation-lunge связывает опорную ногу, клинок и
            // цель. Симуляционную позицию и дальность атаки он не меняет.
            float distance;
            if (_attackMotionTime < 0.10f)
                distance = Mathf.Lerp(0f, -0.035f, Smooth(_attackMotionTime / 0.10f));
            else if (_attackMotionTime < AttackContactTime)
                distance = Mathf.Lerp(-0.035f, 0.22f,
                    Smooth((_attackMotionTime - 0.10f) / Mathf.Max(0.01f, AttackContactTime - 0.10f)));
            else
                distance = Mathf.Lerp(0.22f, 0f,
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

            Vector3 player = PlayerPosition();
            Vector3 slashCenter = player + Vector3.up * 0.92f;
            if (_arena.TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip))
            {
                Vector3 bladeCenter = Vector3.Lerp(bladeRoot.position, bladeTip.position, 0.62f);
                slashCenter = Vector3.Lerp(slashCenter, bladeCenter, 0.72f);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                // Keep the crescent readable as a weapon stroke instead of a
                // decal around Pelag's feet. A slight view-space offset also
                // prevents the transparent particles from vanishing in armour.
                slashCenter -= camera.transform.forward * 0.20f;
            }

            // ОДИН СЛЕШ, А НЕ ТРИ ЭФФЕКТА ПОВЕРХ ДРУГ ДРУГА.
            //
            // Раньше в один и тот же момент выходили след клинка, кольцо-слеш и
            // тяжёлая пыль под ногами. Три полупрозрачных элемента в одной точке
            // не складываются в удар: они гасят друг друга и вместе читаются как
            // подсветка зоны, а не как взмах. Остаётся один — кольцо-слеш; он
            // единственный имеет форму оружия, а не форму круга под ногами.
            //
            // ДЛИТЕЛЬНОСТЬ ИЗМЕРЕНА ПО HADES 2, А НЕ ПОДОБРАНА.
            //
            // Покадровый разбор их записи: большая вспышка способности целиком
            // исчезает за десять кадров при 60 fps — 167 мс. Их эффекты КОРОЧЕ
            // наших, а не длиннее, и в этом половина ощущения удара: вспышка
            // должна кончиться раньше, чем игрок успеет её рассмотреть, иначе
            // она читается не как удар, а как подсветка зоны.
            //
            // Пять кадров симуляции — жёсткая верхняя граница яркого слоя.
            Spawn(PelagVfxId.WhirlwindRing, slashCenter, Quaternion.identity,
                BrightVfxLifetime, 0.20f, 1.32f, Motion.Expand);
            PulseCombatLight(1f);
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
                PlayAnchorLeapTo(ForcedTargetWorld(sim), false, slot);
            }
            else if (id == AbilityDefinition.AnchorSweepId)
            {
                PlayAnchorSweep(false, slot);
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
            _arena.PlayPlayerAbilityPresentation(slot, AbilityDefinition.AnchorLeapId);

            int projectile = SpawnMoving(PelagVfxId.AnchorLeapThrow, player + Vector3.up * 1.15f,
                target + Vector3.up * 0.18f, 0.48f, 0.65f, Motion.Projectile);
            SpawnFollowingChain(PelagVfxId.AnchorLeapChain, projectile, 0.86f);
        }

        private void PlayAnchorSweep(bool showcase, int slot = 2)
        {
            Vector3 player = PlayerPosition();
            Vector3 group = AverageTargetPosition(player + CameraPlaneDirection(Vector3.forward) * 3f);
            Vector3 target = group + FlatDirection(player, group) * 1.25f;
            StartMotion(PelagVfxShowcase.AnchorSweep, player, target, showcase);
            _arena.PlayPlayerAbilityPresentation(slot, AbilityDefinition.AnchorSweepId);

            int projectile = SpawnMoving(PelagVfxId.AnchorSweepThrow, player + Vector3.up * 1.20f,
                target + Vector3.up * 0.16f, 0.52f, 0.85f, Motion.Projectile);
            SpawnFollowingChain(PelagVfxId.AnchorSweepPull, projectile, 1.12f);
        }

        private void PlayChainStep(bool showcase, int slot = 3)
        {
            Vector3 player = PlayerPosition();
            Vector3 target = FirstTargetPosition();
            StartMotion(PelagVfxShowcase.ChainStep, player, target, showcase);
            _arena.PlayPlayerAbilityPresentation(slot, AbilityDefinition.ChainStepId);
        }

        private void StartMotion(PelagVfxShowcase ability, Vector3 start, Vector3 end, bool showcase)
        {
            _motionAbility = ability;
            _motionTime = 0f;
            _motionStart = start;
            _motionEnd = end;
            _captureMotion = showcase;
            FillTargets();
        }

        private void UpdateAbilityMotion()
        {
            if (_motionAbility == PelagVfxShowcase.None) return;
            _motionTime += Time.deltaTime;

            switch (_motionAbility)
            {
                case PelagVfxShowcase.AnchorLeap: UpdateAnchorLeap(); break;
                case PelagVfxShowcase.AnchorSweep: UpdateAnchorSweep(); break;
                case PelagVfxShowcase.ChainStep: UpdateChainStep(); break;
            }
        }

        private void UpdateAnchorLeap()
        {
            if (_motionTime >= 0.46f && _motionTime - Time.deltaTime < 0.46f)
            {
                Spawn(PelagVfxId.AnchorLeapLand, _motionEnd + Vector3.up * 0.04f,
                    Quaternion.identity, FlipbookLifetime, 0.45f, 1.35f, Motion.Expand);
            }

            if (_captureMotion)
            {
                Vector3 full = _motionEnd - _motionStart;
                float amount;
                if (_motionTime < 0.42f) amount = 0f; // якорь сначала реально улетает
                else if (_motionTime < 0.68f) amount = Smooth((_motionTime - 0.42f) / 0.26f);
                else if (_motionTime < 1.28f) amount = 1f;
                else amount = 1f - Smooth((_motionTime - 1.28f) / 0.36f);
                _arena.SetPresentationOffset(Simulation.PlayerId, full * Mathf.Clamp01(amount));
            }

            if (_motionTime >= 1.66f) FinishMotion();
        }

        private void UpdateAnchorSweep()
        {
            if (_motionTime >= 0.52f && _motionTime - Time.deltaTime < 0.52f)
            {
                Vector3 player = BasePlayerPosition();
                Spawn(PelagVfxId.AnchorLeapLand, _motionEnd + Vector3.up * 0.04f,
                    Quaternion.identity, FlipbookLifetime, 0.40f, 1.20f, Motion.Expand);
                for (int i = 0; i < _targetCount; i++)
                {
                    Vector3 enemy = BaseEntityPosition(_targets[i], player);
                    SpawnLine(PelagVfxId.AnchorSweepEnemyPull, enemy + Vector3.up * 0.45f,
                        Vector3.Lerp(enemy, player, 0.62f) + Vector3.up * 0.35f, 0.42f);
                    Spawn(PelagVfxId.DustSmall, enemy + Vector3.up * 0.03f, Quaternion.identity,
                        0.34f, 0.58f, 1.0f, Motion.Expand);
                }
            }

            if (_captureMotion && _motionTime >= 0.52f)
            {
                Vector3 player = BasePlayerPosition();
                float pull = _motionTime < 0.88f
                    ? Smooth((_motionTime - 0.52f) / 0.36f)
                    : 1f - Smooth((_motionTime - 1.25f) / 0.38f);
                pull = Mathf.Clamp01(pull);
                for (int i = 0; i < _targetCount; i++)
                {
                    Vector3 enemy = BaseEntityPosition(_targets[i], player);
                    float resistance = (i % 4) == 3 ? 0.18f : 0.62f;
                    _arena.SetPresentationOffset(_targets[i], (player - enemy) * resistance * pull);
                }
            }

            if (_motionTime >= 1.68f) FinishMotion();
        }

        private void UpdateChainStep()
        {
            if (_targetCount == 0)
            {
                FinishMotion();
                return;
            }

            const float first = 0.18f;
            const float hop = 0.34f;
            int hopIndex = Mathf.FloorToInt((_motionTime - first) / hop);
            if (_motionTime >= first && hopIndex >= 0 && hopIndex < Mathf.Min(4, _targetCount))
            {
                float local = (_motionTime - first - hopIndex * hop) / hop;
                Vector3 from = hopIndex == 0 ? _motionStart : BaseEntityPosition(_targets[hopIndex - 1], _motionStart);
                Vector3 to = BaseEntityPosition(_targets[hopIndex], _motionStart);

                if (local < Time.deltaTime / hop)
                {
                    SpawnLine(PelagVfxId.AnchorLeapChain, from + Vector3.up * 1.1f,
                        to + Vector3.up * 0.8f, 0.24f);
                    SpawnMoving(PelagVfxId.ChainStepDash, from + Vector3.up * 0.8f,
                        to + Vector3.up * 0.8f, hop * 0.82f, 0f, Motion.Dash);
                }

                if (_captureMotion)
                    _arena.SetPresentationOffset(Simulation.PlayerId,
                        Vector3.Lerp(from, to, Smooth(local)) - BasePlayerPosition());

                if (local >= 0.72f && local - Time.deltaTime / hop < 0.72f)
                    Spawn(PelagVfxId.ChainStepHit, to + Vector3.up * 0.9f,
                        Quaternion.identity, 0.25f, 0.65f, 1.05f, Motion.Expand);
            }

            float end = first + hop * Mathf.Min(4, _targetCount);
            if (_motionTime >= end)
            {
                if (_captureMotion)
                {
                    float recover = Smooth((_motionTime - end) / 0.38f);
                    Vector3 last = BaseEntityPosition(_targets[Mathf.Min(3, _targetCount - 1)], _motionStart);
                    _arena.SetPresentationOffset(Simulation.PlayerId,
                        Vector3.Lerp(last - BasePlayerPosition(), Vector3.zero, recover));
                }
                if (_motionTime >= end + 0.42f) FinishMotion();
            }
        }

        private void FinishMotion()
        {
            _arena.ClearPresentationOffsets();
            _motionAbility = PelagVfxShowcase.None;
            _captureMotion = false;
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
                    case Motion.Expand:
                        float pulse = Mathf.Sin(t * Mathf.PI) * 0.08f;
                        fx.Object.transform.localScale = Vector3.one *
                            (Mathf.Lerp(fx.StartScale, fx.EndScale, Smooth(t)) + pulse);
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
                        Vector3 from = PlayerPosition() + Vector3.up * 1.05f;
                        Vector3 to = fx.End;
                        if ((uint)fx.FollowIndex < (uint)_active.Length && _active[fx.FollowIndex].Active)
                            to = _active[fx.FollowIndex].Object.transform.position;
                        Vector3 bend = Vector3.Lerp(from, to, 0.5f) + Vector3.down * (0.22f * (1f - t));
                        fx.Element.SetLine(from, bend, to);
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
