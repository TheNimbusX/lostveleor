using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// КОНТРОЛЬ НА ГЕРОЕ: КОРНИ И ОГЛУШЕНИЕ (план «Мобы леса v2», поток C).
    ///
    /// Оба — по событию SimEventType.HeroControl (Amount — сколько тиков, Flag —
    /// корни или оглушение). Событие есть, только если контроль лёг на самом
    /// деле: отбитый иммунитетом не рисуется, хотя удар и урон были.
    /// • Корни (Корнехват, 30 тиков; кадр 07-snarer-roots-emerging-a-burst):
    ///   толстые корни выходят из земли и обвивают ноги до середины бедра, у стоп —
    ///   короткие корни веером в грунт, треснувшая тёмная земля, комья и пыль на
    ///   старте. Держатся на герое (отброс его двигает) и уходят в землю за
    ///   RootsSinkLeadTicks до конца: к последнему тику корней ноги свободны.
    /// • Оглушение (разбег Камнекопыта, 30 тиков; кадр 02-stonehoof-stun-daze):
    ///   над головой тёплое золотое кольцо, по нему кружат звёздочки-щепки
    ///   (кремовые с тёмной кромкой) и осенние листья; на старте — щепки и
    ///   листья брызгами от головы. Кольцо вскакивает с перелётом и сжимается
    ///   к концу оглушения.
    ///
    /// Возраст — от тика Sim с долей кадра (тик − 1 + Alpha), как у видов мобов:
    /// частицы догоняются Simulate, корни ставятся по возрасту, кружение — угол
    /// от возраста; пауза держит кадр, перемотка даёт тот же кадр. Срок — из
    /// события; раньше срока (сброс стенда, смерть героя) эффект гасит состояние
    /// Sim: HeroRooted / HeroStunned уже false.
    ///
    /// Героя не высветлять (решение владельца 24.09): здесь ни вспышки, ни
    /// подсветки тела — только корни у ног и кольцо над головой.
    ///
    /// Префабы собирает RootSnarerVfxSetup (кора и материалы общие с корнями
    /// Корнехвата). Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class HeroControlView : MonoBehaviour
    {
        public const string PrefabFolder = "VFX/HeroControl/Prefabs/";
        public const string RootsName = "VFX_HeroControl_Roots";
        public const string DazeName = "VFX_HeroControl_Daze";

        /// <summary>Ребёнок оглушения, который вскакивает и сжимается (кольцо и кружащие).</summary>
        public const string HaloName = "Halo";

        /// <summary>Ребёнок Halo, который вид крутит вокруг +Y: звёздочки и листья на кольце.</summary>
        public const string OrbitName = "Orbit";

        public const float RootsRiseSeconds = .16f, RootsSinkSeconds = .14f;

        /// <summary>Корни уходят в землю за столько тиков до конца: к концу контроля ноги свободны.</summary>
        public const int RootsSinkLeadTicks = 4;

        public const float DazePopSeconds = .12f, DazeFadeSeconds = .14f;
        public const float DazeOrbitDegreesPerSecond = 300f;

        /// <summary>Кольцо над макушкой: столько над костью головы; без кости — над землёй.</summary>
        public const float DazeHeadLift = .3f, DazeFallbackHeight = 2.05f;

        /// <summary>Хвост после конца контроля: пыль и комья старта успевают лечь и растаять.</summary>
        private const float RootsTail = .7f, DazeTail = .5f;

        private const string HeadBone = "mixamorig:Head";

        private sealed class Control
        {
            public RootSnarerCombatView.Fx Fx;
            public Transform Halo, Orbit;
            public bool Rooted;
            /// <summary>Последний тик Sim под контролем (тик события + Amount).</summary>
            public int End;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;
        private Control[] _roots = new Control[0], _dazes = new Control[0];
        private int _rootsCursor, _dazeCursor;
        private Transform _heroView, _head;

        public static HeroControlView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<HeroControlView>();
            return view != null ? view : host.AddComponent<HeroControlView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
            // Пулы — сразу, в бою ни одного Instantiate. Контроль на герое один за раз
            // (иммунитет 45 тиков), второе место — на смену, пока первое уходит в землю.
            _roots = MakePool(RootsName, "Контроль героя: корни", 2, false);
            _dazes = MakePool(DazeName, "Контроль героя: оглушение", 2, true);
        }

        private Control[] MakePool(string prefabName, string title, int count, bool daze)
        {
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[Разлом] Контроль героя: нет префаба «{PrefabFolder}{prefabName}» — собери «Разлом/Корнехват/VFX: пересобрать».");
                return new Control[0];
            }
            var pool = new Control[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform); go.name = title;
                var c = new Control { Fx = RootSnarerCombatView.Prepare(go), Rooted = !daze };
                if (daze)
                {
                    c.Halo = go.transform.Find(HaloName);
                    c.Orbit = c.Halo != null ? c.Halo.Find(OrbitName) : null;
                }
                go.SetActive(false);
                pool[i] = c;
            }
            return pool;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: тики начинаются заново.
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                RetireAll(); _shown = sim; _generation = _driver.Generation; _depth = depth;
            }
            if (sim == null) return;
            int now = sim.Tick - 1;
            ConsumeEvents();
            CutShort(sim, now);
            float tick = now + _driver.Alpha;
            foreach (var c in _roots) AdvanceRoots(c, tick);
            foreach (var c in _dazes) AdvanceDaze(c, tick);
        }

        private void ConsumeEvents()
        {
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                int at = contexts[i].SimulationTick - 1;
                switch (e.Type)
                {
                    case SimEventType.HeroControl:
                        if (e.Target == Simulation.PlayerId && e.Amount > 0) Begin(e.Flag, at, e.Amount);
                        break;
                    case SimEventType.Death:
                        if (e.Target == Simulation.PlayerId) { EndAll(_roots, at); EndAll(_dazes, at); }
                        break;
                }
            }
        }

        /// <summary>
        /// Контроль снят раньше срока (сброс стенда, смерть без события в этом
        /// кадре): Sim уже не держит героя — эффект уходит с этого тика.
        /// </summary>
        private void CutShort(Simulation sim, int now)
        {
            if (!sim.HeroRooted) EndAll(_roots, now);
            if (!sim.HeroStunned) EndAll(_dazes, now);
        }

        // ------------------------------------------------------------ control

        private void Begin(bool rooted, int at, int ticks)
        {
            var pool = rooted ? _roots : _dazes;
            if (pool.Length == 0) return;
            // Новый контроль того же вида сменяет прежний (иммунитет этого не даёт,
            // но стенд может повесить его руками).
            EndAll(pool, at);
            ref int cursor = ref rooted ? ref _rootsCursor : ref _dazeCursor;
            var c = pool[cursor++ % pool.Length];
            c.End = at + ticks;
            float seconds = ticks / (float)Simulation.TicksPerSecond;
            var place = rooted ? HeroGround() : DazePoint();
            RootSnarerCombatView.Restart(c.Fx, at, place, Quaternion.identity, at, seconds + (rooted ? RootsTail : DazeTail));
            if (rooted)
            {
                c.Fx.RiseSeconds = RootsRiseSeconds; c.Fx.SinkSeconds = RootsSinkSeconds;
                c.Fx.SinkAge = SinkAgeFor(c);
            }
            else if (c.Halo != null) c.Halo.localScale = Vector3.one * .001f;
        }

        /// <summary>Корни уходят за RootsSinkLeadTicks до конца, но не раньше, чем выйдут целиком.</summary>
        private static float SinkAgeFor(Control c)
            => Mathf.Max(RootsRiseSeconds, (c.End - RootsSinkLeadTicks - c.Fx.Tick) / (float)Simulation.TicksPerSecond);

        /// <summary>Контроль кончился на тике now: корни уходят сразу, кольцо сжимается.</summary>
        private static void EndAll(Control[] pool, int now)
        {
            foreach (var c in pool)
            {
                var fx = c.Fx;
                if (!fx.Root.activeSelf || now >= c.End || now < fx.Tick) continue;
                c.End = now;
                float seconds = (now - fx.Tick) / (float)Simulation.TicksPerSecond;
                if (c.Rooted)
                {
                    fx.SinkAge = Mathf.Min(fx.SinkAge, Mathf.Max(0f, seconds - RootsSinkSeconds * .5f));
                    fx.Life = Mathf.Min(fx.Life, seconds + RootsSinkSeconds + RootsTail);
                }
                else fx.Life = Mathf.Min(fx.Life, seconds + DazeTail);
            }
        }

        private void AdvanceRoots(Control c, float tick)
        {
            var fx = c.Fx;
            if (!fx.Root.activeSelf) return;
            float age = (tick - fx.Tick) / Simulation.TicksPerSecond;
            // Моложе старта — перемотка до события: эффекта ещё нет.
            if (age > fx.Life || age < -1e-3f) { RootSnarerCombatView.Retire(fx); return; }
            age = Mathf.Max(0f, age);
            fx.Root.transform.position = HeroGround();
            RootSnarerCombatView.AnimateGrows(fx, age);
            RootSnarerCombatView.StepParticles(fx, age);
        }

        private void AdvanceDaze(Control c, float tick)
        {
            var fx = c.Fx;
            if (!fx.Root.activeSelf) return;
            float age = (tick - fx.Tick) / Simulation.TicksPerSecond;
            if (age > fx.Life || age < -1e-3f) { RootSnarerCombatView.Retire(fx); return; }
            age = Mathf.Max(0f, age);
            fx.Root.transform.position = DazePoint();
            if (c.Halo != null)
            {
                // Вскакивает с перелётом от трети размера, к концу сжимается в точку.
                float scale = Mathf.Lerp(.35f, 1f, RootSnarerCombatView.Rise(age / DazePopSeconds));
                float end = (c.End - fx.Tick) / (float)Simulation.TicksPerSecond;
                float fade = Mathf.Clamp01((age - (end - DazeFadeSeconds)) / DazeFadeSeconds);
                scale *= 1f - fade * fade * (3f - 2f * fade);
                bool show = scale > .01f;
                if (c.Halo.gameObject.activeSelf != show) c.Halo.gameObject.SetActive(show);
                c.Halo.localScale = Vector3.one * Mathf.Max(.001f, scale);
            }
            if (c.Orbit != null) c.Orbit.localRotation = Quaternion.Euler(0f, age * DazeOrbitDegreesPerSecond, 0f);
            RootSnarerCombatView.StepParticles(fx, age);
        }

        private void RetireAll()
        {
            foreach (var c in _roots) RootSnarerCombatView.Retire(c.Fx);
            foreach (var c in _dazes) RootSnarerCombatView.Retire(c.Fx);
        }

        // -------------------------------------------------------------- place

        private bool TryHeroView(out Transform view)
        {
            if (_arena != null && _arena.TryGetEntityView(Simulation.PlayerId, out view) && view != null)
            {
                // Тело героя сменилось (пул, новый Разлом) — кость головы ищется заново, один раз.
                if (view != _heroView) { _heroView = view; _head = FindHead(view); }
                return true;
            }
            view = null;
            return false;
        }

        private static Transform FindHead(Transform root)
        {
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == HeadBone) return bone;
            return null;
        }

        private Vector3 HeroRoot()
        {
            if (TryHeroView(out var view)) return view.position;
            var sim = _driver.Sim;
            return sim != null && sim.Entities.Count > Simulation.PlayerId ? _driver.GetRenderPosition(Simulation.PlayerId) : Vector3.zero;
        }

        private Vector3 HeroGround()
        {
            var p = HeroRoot();
            return new Vector3(p.x, GroundY(p.x, p.z), p.z);
        }

        /// <summary>
        /// Центр кольца: над костью головы (идёт за покачиванием тела), по
        /// горизонтали — между макушкой и центром героя, чтобы наклон головы не
        /// уводил кольцо с тела. Без кости — на постоянной высоте над землёй.
        /// </summary>
        private Vector3 DazePoint()
        {
            var p = HeroRoot();
            float ground = GroundY(p.x, p.z);
            if (_head == null) return new Vector3(p.x, ground + DazeFallbackHeight, p.z);
            var h = _head.position;
            return new Vector3(Mathf.Lerp(p.x, h.x, .6f), Mathf.Max(ground + 1.5f, h.y + DazeHeadLift), Mathf.Lerp(p.z, h.z, .6f));
        }

        private float GroundY(float x, float z) => _layout != null ? _layout.WeaponGroundHeight(x, z) : 0f;

        private void OnDisable() => RetireAll();

        private void OnDestroy()
        {
            foreach (var c in _roots) if (c.Fx.Root != null) Destroy(c.Fx.Root);
            foreach (var c in _dazes) if (c.Fx.Root != null) Destroy(c.Fx.Root);
        }
    }
}
