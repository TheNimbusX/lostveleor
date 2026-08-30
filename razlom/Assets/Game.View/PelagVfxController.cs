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

        public bool PoolsReady { get; private set; }
        public bool ShowcaseRunning => _showcase != PelagVfxShowcase.None;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            _active = new ActiveFx[MaxActive];
            BuildPools();
        }

        private void LateUpdate()
        {
            ConsumeSimEvents();
            UpdateShowcase();
            UpdateAbilityMotion();
            UpdateActive(Time.deltaTime);
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

        private void ConsumeSimEvents()
        {
            if (!PoolsReady || _showcase != PelagVfxShowcase.None) return;

            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Source != Simulation.PlayerId) continue;

                if (e.Type == SimEventType.Attack)
                {
                    Vector3 target = EntityPosition(e.Target, e.Position);
                    PlayAutoattack(target, false);
                }
                else if (e.Type == SimEventType.AbilityCast)
                {
                    switch (e.Amount)
                    {
                        case 0: PlayWhirlwind(false); break;
                        case 1: PlayAnchorLeap(false); break;
                        case 2: PlayAnchorSweep(false); break;
                        case 3: PlayChainStep(false); break;
                    }
                }
            }
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
                case PelagVfxShowcase.Autoattack: PlayAutoattack(FirstTargetPosition(), true); break;
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
                case PelagVfxShowcase.Autoattack: return 1.45f;
                case PelagVfxShowcase.Whirlwind: return 1.85f;
                case PelagVfxShowcase.AnchorLeap: return 2.15f;
                case PelagVfxShowcase.AnchorSweep: return 2.15f;
                case PelagVfxShowcase.ChainStep: return 2.20f;
                default: return 1f;
            }
        }

        private void PlayAutoattack(Vector3 target, bool showcase)
        {
            Vector3 player = PlayerPosition();
            Vector3 direction = FlatDirection(player, target);
            Vector3 slashAt = Vector3.Lerp(player, target, 0.58f) + Vector3.up * 1.05f;
            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);

            _arena.PlayPlayerAttackPresentation();
            Spawn(PelagVfxId.AutoAttackSlash, slashAt, facing, 0.22f, 0.75f, 1.08f, Motion.Expand);
            Spawn(PelagVfxId.AutoAttackImpact, target + Vector3.up * 0.95f, facing, 0.28f, 0.85f, 1.12f, Motion.Static);
            Spawn(PelagVfxId.TargetFlash, target + Vector3.up * 0.92f, facing, 0.12f, 0.62f, 0.85f, Motion.Expand);
            Spawn(PelagVfxId.DustSmall, player + direction * 0.22f + Vector3.up * 0.03f,
                Quaternion.identity, 0.35f, 0.75f, 1.20f, Motion.Expand);
        }

        private void PlayWhirlwind(bool showcase)
        {
            Vector3 player = PlayerPosition();
            _arena.PlayPlayerAbilityPresentation(0);
            Spawn(PelagVfxId.WhirlwindRing, player + Vector3.up * 0.08f, Quaternion.identity,
                0.56f, 0.35f, 1.55f, Motion.Expand);
            Spawn(PelagVfxId.DustHeavy, player + Vector3.up * 0.04f, Quaternion.identity,
                0.65f, 0.65f, 1.55f, Motion.Expand);

            FillTargets();
            for (int i = 0; i < _targetCount; i++)
            {
                Vector3 target = EntityPosition(_targets[i], player);
                Spawn(PelagVfxId.WhirlwindHit, target + Vector3.up * 0.9f, Quaternion.identity,
                    0.30f, 0.72f, 1.12f, Motion.Expand);
                if (i < 4)
                    Spawn(PelagVfxId.TargetFlash, target + Vector3.up * 0.9f, Quaternion.identity,
                        0.13f, 0.55f, 0.80f, Motion.Expand);
            }
        }

        private void PlayAnchorLeap(bool showcase)
        {
            Vector3 player = PlayerPosition();
            Vector3 target = player + CameraPlaneDirection(new Vector3(1f, 0f, 0.25f)) * 3.8f;
            StartMotion(PelagVfxShowcase.AnchorLeap, player, target, showcase);
            _arena.PlayPlayerAbilityPresentation(1);

            int projectile = SpawnMoving(PelagVfxId.AnchorLeapThrow, player + Vector3.up * 1.15f,
                target + Vector3.up * 0.18f, 0.48f, 0.65f, Motion.Projectile);
            SpawnFollowingChain(PelagVfxId.AnchorLeapChain, projectile, 0.86f);
        }

        private void PlayAnchorSweep(bool showcase)
        {
            Vector3 player = PlayerPosition();
            Vector3 group = AverageTargetPosition(player + CameraPlaneDirection(Vector3.forward) * 3f);
            Vector3 target = group + FlatDirection(player, group) * 1.25f;
            StartMotion(PelagVfxShowcase.AnchorSweep, player, target, showcase);
            _arena.PlayPlayerAbilityPresentation(1);

            int projectile = SpawnMoving(PelagVfxId.AnchorSweepThrow, player + Vector3.up * 1.20f,
                target + Vector3.up * 0.16f, 0.52f, 0.85f, Motion.Projectile);
            SpawnFollowingChain(PelagVfxId.AnchorSweepPull, projectile, 1.12f);
        }

        private void PlayChainStep(bool showcase)
        {
            Vector3 player = PlayerPosition();
            Vector3 target = FirstTargetPosition();
            StartMotion(PelagVfxShowcase.ChainStep, player, target, showcase);
            _arena.PlayPlayerAbilityPresentation(1);
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
                    Quaternion.identity, 0.50f, 0.45f, 1.35f, Motion.Expand);
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
            for (int i = 0; i < _active.Length; i++)
            {
                ref ActiveFx fx = ref _active[i];
                if (!fx.Active) continue;
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
            ref ActiveFx fx = ref _active[index];
            if (!fx.Active) return;
            fx.Element.End();
            _pools[(int)fx.Id].Pool.Release(fx.Object);
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
