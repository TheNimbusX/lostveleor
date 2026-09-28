using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// РАСКОЛ РАСЩЕПЕНЯ со стороны картинки (целевой кадр владельца 1-burst-leap,
    /// 26.09). В пакете r03 панцирь — две отдельные части со скином;
    /// клип Death раскрывает их перед переходом в летящие обломки:
    ///
    ///   тик T (Death) — тело играет трещину (SplitterAnimatorView);
    ///   T + CrackTicks — тело прячется, на его месте тёплая пыль, щепки коры,
    ///     листья и шляпки грибов (VFX_Splitter_Burst), а две половины коры,
    ///     вырезанные из той же модели в позе последнего кадра трещины,
    ///     разлетаются дугами, кувыркаются, ложатся (пыль касания) и тают;
    ///   на T + CrackTicks приходит SplitterSplit: детёныши только теперь
    ///     создаются в Sim и сразу играют Pop. Выброс идёт метр за 8 тиков;
    ///     второй задержки появления в представлении нет.
    ///
    /// Смерть детёныша — тот же раскол в его масштабе (0,6), без новых детей:
    /// своего растворения у освещённого материала нет.
    ///
    /// Всё — от СОБЫТИЙ Sim (Death, SplitterSplit) с тиком события; возраст
    /// эффектов и обломков считается от тика Sim с долей кадра — пауза держит
    /// кадр, съёмка повторяется. Префабы собирает SplitterVfxSetup.
    ///
    /// ПЕРЕКАТ (27.09, кадры 2-roll-windup и 1-roll): с фиксации полосы вдоль
    /// неё от клубка проступает борозда — продавленная тёмная земля с
    /// трещинками и комьями (VFX_Splitter_GrooveSegment), вся за 10 тиков; это
    /// и есть метка переката — общая заливка его полосу не рисует. Катится —
    /// каждые 2 тика из-под клубка летит дёрн (VFX_Splitter_RollTurf).
    /// Остановился — пыль касания; о стену — удар со щепками (VFX_Splitter_RollWall).
    /// Снят до пуска — борозда гаснет сразу.
    ///
    /// Ставится на объект ArenaView: SplitterCombatView.Install(arena). Тела
    /// Расщепеня вид привязывает сам, если ArenaView этого не сделал.
    /// </summary>
    [DefaultExecutionOrder(645)]
    public sealed class SplitterCombatView : MonoBehaviour
    {
        public const string BurstPrefab = "VFX/Splitter/VFX_Splitter_Burst";
        public const string ShellLandPrefab = "VFX/Splitter/VFX_Splitter_ShellLand";
        public const string ShellLeftPrefab = "VFX/Splitter/Splitter_ShellL";
        public const string ShellRightPrefab = "VFX/Splitter/Splitter_ShellR";
        public const string GroovePrefab = "VFX/Splitter/VFX_Splitter_GrooveSegment";
        public const string TurfPrefab = "VFX/Splitter/VFX_Splitter_RollTurf";
        public const string RollWallPrefab = "VFX/Splitter/VFX_Splitter_RollWall";

        /// <summary>Шаг кусков борозды вдоль полосы, метры, и за сколько тиков она проступает целиком.</summary>
        private const float GrooveStep = .45f, GroovePressTicks = 10f;

        /// <summary>Дёрн из-под клубка — раз в столько тиков качения.</summary>
        private const int TurfEveryTicks = 2;

        /// <summary>Через сколько тиков после смерти тело раскалывается (трещина Death).</summary>
        public const int BreakDelayTicks = SplitterAnimatorView.CrackTicks;

        /// <summary>То же в секундах — для звука раскола (CombatAudio).</summary>
        public const float BreakDelaySeconds = BreakDelayTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Номинальное касание земли половиной коры после раскола, секунды (разброс ±10%).</summary>
        public const float ShellLandSeconds = .47f;

        /// <summary>Сколько живут половины коры от раскола: лежат и тают к этому возрасту.</summary>
        public const float ShellLifeSeconds = 1.2f;

        private const float ShellFadeFrom = .82f, Gravity = 16f, SimulateStep = 1f / 30f;

        public static SplitterCombatView Install(ArenaView arena)
        {
            if (arena == null) return null;
            var view = arena.GetComponent<SplitterCombatView>();
            return view != null ? view : arena.gameObject.AddComponent<SplitterCombatView>();
        }

        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public float Tick = -1000f, Life, Simulated = -1f;
            /// <summary>Номер переката, которому принадлежит кусок борозды; 0 — ничей.</summary>
            public int Owner;
        }

        /// <summary>Перекат, за которым вид ставит борозду и дёрн.</summary>
        private sealed class RollTrack
        {
            public int Entity, Serial, NextGroove, NextTurfTick;
            public bool Seen;
        }

        private sealed class Pool
        {
            public Burst[] Items = new Burst[0];
            public int Cursor;
        }

        private sealed class Shell
        {
            public GameObject Root;
            public Vector3[] Corners;
            public bool Active, Landed;
            public int Tick;
            public float Scale, LandAge, RestY;
            public Vector3 Start, Velocity, Slide, RollAxis, PitchAxis;
            public Quaternion Rotation;
            public float Roll, Pitch, Yaw;
        }

        private struct PendingBreak
        {
            public int Entity, Tick;
            public float Scale;
            public FixVec2 At;
        }

        private struct Leap
        {
            public int Child, Parent, StartTick;
            public FixVec2 At;
        }

        private TickDriver _driver;
        private ArenaView _arena;
        private LayoutView _layout;
        private Simulation _shown;
        private Pool _bursts, _lands, _grooves, _turfs, _walls;
        private readonly List<RollTrack> _rolls = new List<RollTrack>(8);
        private Shell[] _left, _right;
        private int _shellCursor;
        private readonly List<PendingBreak> _breaks = new List<PendingBreak>(8);
        private readonly List<Leap> _leaps = new List<Leap>(8);
        private readonly Dictionary<Transform, SplitterAnimatorView> _bodies = new Dictionary<Transform, SplitterAnimatorView>();

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            _layout = GetComponent<LayoutView>();
            // Пулы — сразу: в бою ни одного Instantiate. Шесть расколов разом —
            // пачка Расщепеней и их детей под одним Вихрем.
            _bursts = MakePool(BurstPrefab, "Расщепень: раскол", 6, 2.2f);
            _lands = MakePool(ShellLandPrefab, "Расщепень: касание коры", 12, 1.1f);
            _left = MakeShells(ShellLeftPrefab, "Расщепень: левая кора", 6);
            _right = MakeShells(ShellRightPrefab, "Расщепень: правая кора", 6);
            // Перекат: две полосы разом по 12 кусков, дёрн — до 16 разом, стены — редкость.
            _grooves = MakePool(GroovePrefab, "Расщепень: борозда", 28, 2.4f);
            _turfs = MakePool(TurfPrefab, "Расщепень: дёрн", 18, 1.2f);
            _walls = MakePool(RollWallPrefab, "Расщепень: удар о стену", 3, 1.6f);
            if (_bursts.Items.Length == 0 || _left.Length == 0)
                Debug.LogWarning("[splitter] Нет префабов раскола в Resources/VFX/Splitter — собери: Разлом/Расщепень/VFX распада.", this);
        }

        // ------------------------------------------------------------- frame

        private void LateUpdate()
        {
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;
            if (_shown != sim) ResetFor(sim);
            float tick = sim.Tick - 1 + _driver.Alpha;

            BindBodies(sim);
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                // Событие тика T приходит с SimulationTick = T + 1.
                int at = contexts[i].SimulationTick - 1;
                if (e.Type == SimEventType.SplitterSplit) OnSplit(sim, e, at);
                else if (e.Type == SimEventType.Death && IsSplitterKind(sim, e.Target)) OnDeath(sim, e, at);
                else if (e.ActionVariant == (int)EnemyActionKind.SplitterRoll) OnRollEvent(sim, e, at);
            }
            UpdateRolls(sim, tick);

            for (int i = _breaks.Count - 1; i >= 0; i--)
            {
                if (tick < _breaks[i].Tick) continue;
                Break(sim, _breaks[i]);
                _breaks.RemoveAt(i);
            }
            for (int i = _leaps.Count - 1; i >= 0; i--)
                if (!AdvanceLeap(sim, _leaps[i], tick)) _leaps.RemoveAt(i);

            foreach (var b in _bursts.Items) Advance(b, tick);
            foreach (var b in _lands.Items) Advance(b, tick);
            foreach (var b in _grooves.Items) Advance(b, tick);
            foreach (var b in _turfs.Items) Advance(b, tick);
            foreach (var b in _walls.Items) Advance(b, tick);
            foreach (var s in _left) AdvanceShell(s, tick);
            foreach (var s in _right) AdvanceShell(s, tick);
        }

        private void ResetFor(Simulation sim)
        {
            _shown = sim;
            _breaks.Clear();
            _leaps.Clear();
            _bodies.Clear();
            _rolls.Clear();
            foreach (var b in _bursts.Items) Retire(b);
            foreach (var b in _lands.Items) Retire(b);
            foreach (var b in _grooves.Items) Retire(b);
            foreach (var b in _turfs.Items) Retire(b);
            foreach (var b in _walls.Items) Retire(b);
            foreach (var s in _left) RetireShell(s);
            foreach (var s in _right) RetireShell(s);
        }

        private static bool IsSplitterKind(Simulation sim, int id)
            => (uint)id < (uint)sim.Entities.Count
               && (sim.Entities.Kind[id] == EnemyKind.ForestSplitter || sim.Entities.Kind[id] == EnemyKind.ForestSplitling);

        /// <summary>
        /// Тело из пула ArenaView — к своей сущности. ArenaView привязывает тела
        /// вендиго и камнекопыта сам; тело Расщепеня, если он этого не сделал,
        /// привязывается здесь — в том же кадре, до отрисовки.
        /// </summary>
        private void BindBodies(Simulation sim)
        {
            if (_arena == null) return;
            var entities = sim.Entities;
            for (int id = 1; id < entities.Count; id++)
            {
                if (entities.Kind[id] != EnemyKind.ForestSplitter && entities.Kind[id] != EnemyKind.ForestSplitling) continue;
                var body = BodyOf(id);
                if (body == null || body.IsBoundTo(sim, id)) continue;
                body.Bind(_driver, id);
            }
        }

        private SplitterAnimatorView BodyOf(int id)
        {
            if (_arena == null || !_arena.TryGetEntityView(id, out Transform view) || view == null) return null;
            if (!_bodies.TryGetValue(view, out var body))
            {
                body = view.GetComponent<SplitterAnimatorView>();
                _bodies[view] = body;
            }
            return body;
        }

        // ------------------------------------------------------------ events

        private void OnSplit(Simulation sim, SimEvent e, int tick)
        {
            for (int n = 0; n < e.Amount; n++)
            {
                int child = e.Target + n;
                if ((uint)child >= (uint)sim.Entities.Count) break;
                var body = BodyOf(child);
                if (body != null)
                {
                    if (!body.IsBoundTo(sim, child)) body.Bind(_driver, child);
                    body.BeginPop(tick);
                    body.Refresh();
                }
                _leaps.Add(new Leap { Child = child, Parent = e.Source, StartTick = tick, At = e.Position });
            }
        }

        private void OnDeath(Simulation sim, SimEvent e, int tick)
        {
            int id = e.Target;
            var body = BodyOf(id);
            int breakTick = tick + BreakDelayTicks;
            if (body != null)
            {
                if (!body.IsBoundTo(sim, id)) body.Bind(_driver, id);
                body.PlayDeath(tick);
                body.Refresh();
                breakTick = body.BreakTick;
            }
            float scale = sim.Entities.Kind[id] == EnemyKind.ForestSplitling ? .6f : 1f;
            _breaks.Add(new PendingBreak { Entity = id, Tick = breakTick, Scale = scale, At = e.Position });
        }

        // -------------------------------------------------------------- roll

        /// <summary>
        /// Стоп переката (Impact stage 1) — пыль касания или удар о стену; снят
        /// (Cancelled) — борозда гаснет сразу. Пуск (stage 0) продаёт сам клубок.
        /// </summary>
        private void OnRollEvent(Simulation sim, SimEvent e, int tick)
        {
            if (e.Type == SimEventType.EnemyActionCancelled)
            {
                for (int i = 0; i < _rolls.Count; i++)
                    if (_rolls[i].Entity == e.Source)
                        foreach (var b in _grooves.Items) if (b.Owner == _rolls[i].Serial) Retire(b);
                return;
            }
            if (e.Type != SimEventType.EnemyActionImpact || e.Amount != 1) return;
            bool wall = sim.TryGetSplitterRoll(e.Source, out var roll) && roll.WallStop;
            var at = Ground(e.Position);
            var look = wall ? Quaternion.LookRotation(new Vector3(roll.Direction.X.ToFloat(), 0f, roll.Direction.Y.ToFloat()))
                : Quaternion.identity;
            Take(wall ? _walls : _lands, tick, at, look, e.Source * 31 + tick);
        }

        /// <summary>
        /// Борозда и дёрн — по состоянию переката Sim на тик: куски борозды
        /// проступают от клубка по полосе за GroovePressTicks после фиксации,
        /// дёрн — каждые TurfEveryTicks тиков качения там, где клубок был в тот тик.
        /// </summary>
        private void UpdateRolls(Simulation sim, float tick)
        {
            for (int i = 0; i < _rolls.Count; i++) _rolls[i].Seen = false;
            var entities = sim.Entities;
            for (int id = 1; id < entities.Count; id++)
            {
                if (entities.Kind[id] != EnemyKind.ForestSplitter || !sim.TryGetSplitterRoll(id, out var roll)) continue;
                var track = Track(id, roll.Serial);
                track.Seen = true;
                if (roll.Phase == SplitterRollPhase.Curl || roll.Length.Raw <= 0) continue;
                float length = roll.Length.ToFloat();
                var origin = new Vector3(roll.Origin.X.ToFloat(), 0f, roll.Origin.Y.ToFloat());
                var direction = new Vector3(roll.Direction.X.ToFloat(), 0f, roll.Direction.Y.ToFloat());
                var along = Quaternion.LookRotation(direction);

                int pieces = Mathf.Max(1, Mathf.CeilToInt(length / GrooveStep));
                while (track.NextGroove < pieces)
                {
                    float appear = roll.LockTick + track.NextGroove * GroovePressTicks / pieces;
                    if (tick < appear) break;
                    float d = Mathf.Min(length, GrooveStep * (track.NextGroove + .5f));
                    var at = origin + direction * d; at.y = GroundHeight(at);
                    var b = Take(_grooves, appear, at, along, roll.Serial * 17 + track.NextGroove);
                    if (b != null) b.Owner = roll.Serial;
                    track.NextGroove++;
                }

                if (track.NextTurfTick == 0) track.NextTurfTick = roll.LaunchTick;
                int last = roll.Phase == SplitterRollPhase.Rolling ? Mathf.FloorToInt(tick) : roll.StopTick;
                while (track.NextTurfTick <= last && track.NextTurfTick >= roll.LaunchTick)
                {
                    float d = Mathf.Min(length, Simulation.SplitterRollSpeed.ToFloat() * (track.NextTurfTick - roll.LaunchTick + 1));
                    var at = origin + direction * d; at.y = GroundHeight(at);
                    Take(_turfs, track.NextTurfTick, at, along, roll.Serial * 13 + track.NextTurfTick);
                    track.NextTurfTick += TurfEveryTicks;
                }
            }
            for (int i = _rolls.Count - 1; i >= 0; i--) if (!_rolls[i].Seen) _rolls.RemoveAt(i);
        }

        private RollTrack Track(int entity, int serial)
        {
            for (int i = 0; i < _rolls.Count; i++)
                if (_rolls[i].Entity == entity && _rolls[i].Serial == serial) return _rolls[i];
            var track = new RollTrack { Entity = entity, Serial = serial };
            _rolls.Add(track);
            return track;
        }

        // ------------------------------------------------------------- break

        /// <summary>
        /// Раскол: пыль и щепки у ног, две половины коры из позы последнего кадра
        /// трещины. Поза — с узла модели; нет тела — с точки смерти и взгляда Sim.
        /// </summary>
        private void Break(Simulation sim, PendingBreak b)
        {
            Vector3 position;
            Quaternion rotation;
            float scale = b.Scale;
            var body = BodyOf(b.Entity);
            if (body != null && body.IsBoundTo(sim, b.Entity))
            {
                Transform node = body.Body;
                position = node.position;
                rotation = node.rotation;
                scale = node.lossyScale.x;
            }
            else
            {
                position = Ground(b.At);
                FixVec2 facing = sim.Entities.Facing[b.Entity];
                var forward = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat());
                rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            }

            int seed = b.Entity * 7919 + b.Tick * 104729;
            Vector3 flat = rotation * Vector3.forward;
            flat.y = 0f;
            Quaternion yaw = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat, Vector3.up) : Quaternion.identity;
            Vector3 ground = new Vector3(position.x, GroundHeight(position), position.z);
            var burst = Take(_bursts, b.Tick, ground, yaw, seed);
            if (burst != null) burst.Root.transform.localScale = Vector3.one * scale;

            if (_left.Length == 0 || _right.Length == 0) return;
            int slot = _shellCursor++ % _left.Length;
            Launch(_left[slot], b.Tick, position, rotation, scale, -1f, seed, ground.y);
            Launch(_right[slot], b.Tick, position, rotation, scale, 1f, seed + 31, ground.y);
        }

        /// <summary>
        /// Полёт половины коры: наружу вбок и чуть назад от взгляда, вверх; кувырок —
        /// внутрь вокруг оси взгляда (ложится срезом вниз, корой вверх), наклон и поворот. Поза на касании
        /// известна заранее (доли вращения — от доли полёта), поэтому высота
        /// лежащей половины считается по углам её рамки до вылета.
        /// </summary>
        private void Launch(Shell s, int tick, Vector3 bodyPosition, Quaternion bodyRotation, float scale,
            float side, int seed, float groundY)
        {
            if (s.Root == null) return;
            Transform pivot = s.Root.transform;
            Transform mesh = pivot.childCount > 0 ? pivot.GetChild(0) : null;
            Vector3 pivotLocal = mesh != null ? -mesh.localPosition : Vector3.zero;

            s.Active = true;
            s.Landed = false;
            s.Tick = tick;
            s.Scale = scale;
            s.Rotation = bodyRotation;
            s.Start = bodyPosition + bodyRotation * (pivotLocal * scale);
            Vector3 outward = bodyRotation * (side > 0f ? Vector3.right : Vector3.left);
            Vector3 back = bodyRotation * Vector3.back;
            outward.y = 0f; back.y = 0f;
            outward.Normalize(); back.Normalize();
            float lift = .6f + .4f * scale;
            s.Velocity = outward * (Rand(seed, 1, .95f, 1.25f) * scale) + back * (Rand(seed, 2, .75f, 1.05f) * scale)
                         + Vector3.up * (Rand(seed, 3, 2.8f, 3.2f) * lift);
            // Положительный угол вокруг взгляда ведёт верх к −X, а +X — вверх. Половина
            // летит наружу, а кувыркается внутрь и ложится на плоский срез: наружу
            // смотрит кора со спиралью, как на целевом кадре 1-burst-leap. Кувырок
            // наружу (прежний знак) клал её срезом вверх — в кадре тёмный веер среза.
            // Левая половина (side −1) — с минусом, правая — с плюсом.
            s.RollAxis = bodyRotation * Vector3.forward;
            s.PitchAxis = bodyRotation * Vector3.right;
            s.Roll = side * Rand(seed, 4, 75f, 100f);
            s.Pitch = Rand(seed, 5, -25f, 25f);
            s.Yaw = Rand(seed, 6, 15f, 40f) * (Rand(seed, 7, 0f, 1f) < .5f ? -1f : 1f);

            // Высота опоры на касании: нижний угол рамки в конечной позе, чуть в землю.
            Quaternion final = Spin(s, 1f);
            float low = 0f;
            for (int i = 0; i < s.Corners.Length; i++) low = Mathf.Min(low, (final * (s.Corners[i] * scale)).y);
            s.RestY = groundY - low * .88f;
            float vy = s.Velocity.y, drop = Mathf.Max(0f, s.Start.y - s.RestY);
            s.LandAge = Mathf.Max(.2f, (vy + Mathf.Sqrt(vy * vy + 2f * Gravity * drop)) / Gravity);
            s.Slide = new Vector3(s.Velocity.x, 0f, s.Velocity.z) * .18f;

            pivot.SetPositionAndRotation(s.Start, bodyRotation);
            pivot.localScale = Vector3.one * scale;
            s.Root.SetActive(true);
        }

        private static Quaternion Spin(Shell s, float u)
            => Quaternion.AngleAxis(s.Yaw * u, Vector3.up) * Quaternion.AngleAxis(s.Pitch * u, s.PitchAxis)
               * Quaternion.AngleAxis(s.Roll * u, s.RollAxis) * s.Rotation;

        private void AdvanceShell(Shell s, float tick)
        {
            if (!s.Active) return;
            float age = (tick - s.Tick) / Simulation.TicksPerSecond;
            if (age >= ShellLifeSeconds) { RetireShell(s); return; }
            age = Mathf.Max(0f, age);
            float u = Mathf.Clamp01(age / s.LandAge);
            Vector3 position;
            if (age < s.LandAge)
                position = s.Start + s.Velocity * age + Vector3.down * (.5f * Gravity * age * age);
            else
            {
                float after = age - s.LandAge;
                Vector3 landed = s.Start + s.Velocity * s.LandAge;
                position = new Vector3(landed.x, s.RestY, landed.z) + s.Slide * (1f - Mathf.Exp(-7f * after));
                if (!s.Landed)
                {
                    // Пыль касания — с тика касания, не с кадра, в который его заметили.
                    s.Landed = true;
                    var ground = new Vector3(position.x, s.RestY, position.z);
                    ground.y = GroundHeight(ground);
                    var puff = Take(_lands, s.Tick + s.LandAge * Simulation.TicksPerSecond, ground, Quaternion.identity,
                        s.Tick * 2 + (s.Roll > 0f ? 1 : 0));
                    if (puff != null) puff.Root.transform.localScale = Vector3.one * s.Scale;
                }
            }
            float scale = s.Scale;
            if (age > ShellFadeFrom)
            {
                // Тает на месте: сжимается к своему центру и чуть оседает.
                float k = Mathf.Clamp01((age - ShellFadeFrom) / (ShellLifeSeconds - ShellFadeFrom));
                scale *= 1f - k * k;
                position.y -= .06f * k * s.Scale;
            }
            var pivot = s.Root.transform;
            pivot.SetPositionAndRotation(position, Spin(s, u));
            pivot.localScale = Vector3.one * Mathf.Max(.001f, scale);
        }

        private static void RetireShell(Shell s)
        {
            if (s == null || s.Root == null) return;
            s.Active = false;
            if (s.Root.activeSelf) s.Root.SetActive(false);
        }

        // -------------------------------------------------------------- leap

        /// <summary>
        /// Прыжок детёныша: до раскола вид прячет тело, с раскола тело вылетает из
        /// центра родителя (кадры отрыва 2–8 клипа Pop) и к касанию стоит там, где
        /// его держит Sim. Позиция тела — поверх ArenaView этого же кадра (порядок
        /// 645 после его LateUpdate), в Sim ничего не пишется. false — прыжок кончился.
        /// </summary>
        private bool AdvanceLeap(Simulation sim, Leap leap, float tick)
        {
            var entities = sim.Entities;
            if ((uint)leap.Child >= (uint)entities.Count || !entities.Alive[leap.Child]) return false;
            var body = BodyOf(leap.Child);
            int start = body != null && body.PopStartTick != int.MinValue ? body.PopStartTick : leap.StartTick;
            if (tick < start) return true;
            if (tick >= start + SplitterAnimatorView.PopLandTicks) return false;
            if (_arena == null || !_arena.TryGetEntityView(leap.Child, out Transform view) || view == null) return false;

            Vector3 target = view.position;
            Vector3 from = _arena.TryGetEntityView(leap.Parent, out Transform parent) && parent != null
                ? parent.position : Ground(leap.At);
            Vector3 side = target - from;
            side.y = 0f;
            float scale = view.lossyScale.x;
            if (side.sqrMagnitude > 1e-6f) from += side.normalized * (.15f * scale);
            from.y = target.y;
            float k = Mathf.Clamp01((tick - start - SplitterAnimatorView.PopTakeoffTicks)
                                    / (SplitterAnimatorView.PopLandTicks - SplitterAnimatorView.PopTakeoffTicks));
            k = k * k * (3f - 2f * k);
            view.position = Vector3.Lerp(from, target, k);
            return true;
        }

        // ----------------------------------------------------------- effects

        private Pool MakePool(string path, string name, int count, float life)
        {
            var pool = new Pool();
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return pool;
            pool.Items = new Burst[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = name;
                var burst = new Burst { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true), Life = life };
                burst.Seeds = new uint[burst.Particles.Length];
                for (int k = 0; k < burst.Particles.Length; k++)
                {
                    var ps = burst.Particles[k];
                    // Прогрев: один короткий прогон заводит буферы частиц до боя.
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Seeds[k] = ps.randomSeed;
                }
                go.SetActive(false);
                pool.Items[i] = burst;
            }
            return pool;
        }

        private Shell[] MakeShells(string path, string name, int count)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return new Shell[0];
            var shells = new Shell[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = name;
                go.SetActive(false);
                shells[i] = new Shell { Root = go, Corners = Corners(go) };
            }
            return shells;
        }

        /// <summary>Углы рамки половины в осях её корня (центр полёта), масштаб 1.</summary>
        private static Vector3[] Corners(GameObject root)
        {
            var corners = new List<Vector3>(8);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    corners.Add(m.MultiplyPoint3x4(c));
                }
            }
            if (corners.Count == 0) corners.Add(Vector3.down * .3f);
            return corners.ToArray();
        }

        /// <summary>
        /// Следующий экземпляр пула на земле. Зерно систем — от номера раскола:
        /// расколы не повторяют друг друга, а перемотка того же даёт тот же кадр.
        /// </summary>
        private static Burst Take(Pool pool, float tick, Vector3 position, Quaternion rotation, int seed)
        {
            if (pool.Items.Length == 0) return null;
            var b = pool.Items[pool.Cursor++ % pool.Items.Length];
            b.Tick = tick;
            b.Simulated = -1f;
            b.Owner = 0;
            b.Root.transform.SetPositionAndRotation(position, rotation);
            b.Root.transform.localScale = Vector3.one;
            b.Root.SetActive(true);
            for (int k = 0; k < b.Particles.Length; k++)
            {
                var ps = b.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = b.Seeds[k] + (uint)seed * 7919u;
            }
            return b;
        }

        private static void Retire(Burst b)
        {
            if (b == null) return;
            b.Tick = -1000f;
            b.Simulated = -1f;
            if (b.Root.activeSelf) b.Root.SetActive(false);
        }

        /// <summary>
        /// Возраст эффекта — от тика Sim с долей кадра. Вперёд системы догоняются
        /// приращениями, назад — перезапуском; на паузе возраст и частицы стоят.
        /// </summary>
        private static void Advance(Burst b, float tick)
        {
            if (b == null || !b.Root.activeSelf) return;
            float age = (tick - b.Tick) / Simulation.TicksPerSecond;
            if (age > b.Life) { Retire(b); return; }
            age = Mathf.Max(0f, age);
            if (b.Simulated >= 0f && Mathf.Abs(age - b.Simulated) < 1e-5f) return;
            bool restart = b.Simulated < 0f || age < b.Simulated;
            float from = restart ? 0f : b.Simulated;
            foreach (var ps in b.Particles)
            {
                float done = from;
                bool first = restart;
                do
                {
                    float step = Mathf.Min(SimulateStep, age - done);
                    ps.Simulate(step, false, first, false);
                    first = false;
                    done += step;
                } while (done < age - 1e-5f);
                ps.Pause(false);
            }
            b.Simulated = age;
        }

        private float GroundHeight(Vector3 at) => _layout != null ? _layout.WeaponGroundHeight(at.x, at.z) : 0f;

        private Vector3 Ground(FixVec2 at)
        {
            var p = new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat());
            p.y = GroundHeight(p);
            return p;
        }

        /// <summary>Устойчивое число в [min, max) от зерна и соли: без UnityEngine.Random.</summary>
        private static float Rand(int seed, int salt, float min, float max)
        {
            uint h = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return min + (max - min) * ((h & 0xFFFFFF) / 16777216f);
        }

        private void OnDestroy()
        {
            if (_bursts != null) foreach (var b in _bursts.Items) if (b != null && b.Root != null) Destroy(b.Root);
            if (_lands != null) foreach (var b in _lands.Items) if (b != null && b.Root != null) Destroy(b.Root);
            foreach (var pool in new[] { _grooves, _turfs, _walls })
                if (pool != null) foreach (var b in pool.Items) if (b != null && b.Root != null) Destroy(b.Root);
            if (_left != null) foreach (var s in _left) if (s.Root != null) Destroy(s.Root);
            if (_right != null) foreach (var s in _right) if (s.Root != null) Destroy(s.Root);
        }
    }
}
