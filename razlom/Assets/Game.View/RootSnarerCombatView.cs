using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// БОЙ КОРНЕХВАТА СО СТОРОНЫ КАРТИНКИ (целевой кадр 1-roots-snare от 26.09).
    ///
    /// Круг на земле рисует общий GroundTelegraphView (метка SharedView), здесь —
    /// только VFX из паков (префабы собирает RootSnarerVfxSetup):
    /// • удар плитами — TelegraphOpened от Корнехвата (тик 15 позы: круг встал):
    ///   пыль, комья и тёмная земля у обеих плит, трещины бегут от плит к кругу;
    /// • корни — EnemyActionImpact(SnarerSlam), тик 36: толстые узловатые корни
    ///   рвутся из круга и загибаются внутрь, держатся, пока моб прижат, и уходят
    ///   в землю, когда он выдёргивает плиты (или раньше — оглушение, смерть);
    /// • путы на герое — тот же контакт с попаданием и Damage по герою в том же
    ///   тике (значит, замедление повешено): кольца корней вокруг ног, пока идёт
    ///   замедление, и уходят в землю к его концу;
    /// • «Волна из корней» (лечение, кадр 1-mend-ring от 27.09): лапы в землю
    ///   (EnemyActionStarted SnarerMend) — у плит свечение и пылинки света всю
    ///   секунду сбора; волна (EnemyActionImpact SnarerMend) — золотисто-зелёное
    ///   кольцо разбегается до 5 м, по земле — светлые корешки, листья и искры;
    ///   на каждом вылеченном (Heal) — листья раскрываются и всплывают искры.
    ///   Сбит — свечение гаснет сразу.
    ///
    /// Всё — от событий кадра (TickDriver.FrameEventContexts), не опросом. Возраст
    /// каждого эффекта — от тика Sim (тик − 1 + Alpha): частицы догоняются через
    /// Simulate, корни ставятся по возрасту, пауза и перемотка держат кадр.
    ///
    /// Ставится на объект арены (ArenaView + TickDriver) через <see cref="EnsureOn"/>.
    /// </summary>
    [DefaultExecutionOrder(650)]
    public sealed class RootSnarerCombatView : MonoBehaviour
    {
        public const string PrefabFolder = "VFX/RootSnarer/Prefabs/";
        public const string SlamCracksName = "VFX_RootSnarer_SlamCracks";
        public const string RootsEruptName = "VFX_RootSnarer_RootsErupt";
        public const string SnareName = "VFX_RootSnarer_SnareOnHero";
        public const string MendChannelName = "VFX_RootSnarer_MendChannel";
        public const string MendRingName = "VFX_RootSnarer_MendRing";
        public const string MendLeavesName = "VFX_RootSnarer_MendLeaves";

        /// <summary>Сегментов трещины на каждую плиту в префабе удара («Seg L 0» … «Seg R 7»).</summary>
        public const int RunSegmentsPerSide = 8;

        /// <summary>
        /// Растущая часть (корень, кольцо пут): прямой ребёнок корня префаба с именем
        /// «Grow|задержка, мс|скрутка, °|подпись». Вид выдвигает её из земли вдоль
        /// её оси +Y и уводит обратно.
        /// </summary>
        public const char GrowSeparator = '|';
        public const string GrowPrefix = "Grow|";

        /// <summary>Плиты в земле на контакте, оси корня моба (export.json, at_contact_unity_model_m).</summary>
        public static readonly Vector3 SlabLeft = new Vector3(-.8f, 0f, .6f), SlabRight = new Vector3(.8f, 0f, .64f);

        public const float SlamCracksLife = 2.5f, RootsEruptLife = 2.6f;
        public const float RootsRiseSeconds = .11f, RootsSinkSeconds = .28f;
        public const float SnareRiseSeconds = .16f, SnareSinkSeconds = .22f;

        /// <summary>Путы уходят за столько тиков до конца замедления: к его концу ног ничего не держит.</summary>
        public const int SnareRetractTicks = 7;

        /// <summary>Корни уходят через 2 тика после начала выдёргивания плит (кадр 60 клипа Slam).</summary>
        private const int RootsSinkAfterReleaseTicks = 2;
        private const int ReleaseTicks = RootSnarerAnimatorView.SlamFrames - RootSnarerAnimatorView.SlamReleaseFrame;

        /// <summary>Трещина: шаг сегментов, м; скорость бега к кругу, м/с; короче MinRun — не бежит.</summary>
        private const float SegmentStep = .75f, RunSpeed = 11f, MinRun = .3f;

        private const float SimulateStep = 1f / 30f;

        private sealed class Grow
        {
            public Transform T;
            public Renderer R;
            public Vector3 Pos, Axis;
            public Quaternion Rot;
            public float Depth, Delay, Twist;
        }

        private sealed class Fx
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public float[] Delays, Done;
            public int[] SegmentOf;
            public Grow[] Grows;
            public Transform[] Segments, Cracks, Slabs;
            public int Tick = -1000, Entity = -1, Serial;
            public float Life, SinkAge = float.MaxValue, RiseSeconds, SinkSeconds;
            public bool FollowHero;
            /// <summary>Держится за этим союзником (листья лечения); −1 — стоит на месте.</summary>
            public int FollowEntity = -1;
        }

        private sealed class Pool
        {
            public Fx[] Items = new Fx[0];
            public int Cursor;
            public float Life;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;
        private Pool _slam, _roots, _snare, _mendChannel, _mendRing, _mendLeaves;
        private Pool[] _pools;
        private readonly float[] _segmentDelay = new float[RunSegmentsPerSide * 2];

        /// <summary>Ставит вид на объект арены один раз. Зовут ArenaView (семья Корнехвата) и сам вид тела.</summary>
        public static RootSnarerCombatView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<RootSnarerCombatView>();
            return view != null ? view : host.AddComponent<RootSnarerCombatView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
            if (_driver == null)
            {
                Debug.LogWarning("[Разлом] Корнехват: RootSnarerCombatView без TickDriver на объекте — VFX не будет.");
                enabled = false; return;
            }
            // Пулы — сразу: в бою ни одного Instantiate. Удар и корни — крупная атака
            // по жетону, трёх мест хватает с запасом; путы на герое одни (вторые — на смену).
            _slam = MakePool(SlamCracksName, "Корнехват: удар плитами", 3, SlamCracksLife);
            _roots = MakePool(RootsEruptName, "Корнехват: корни", 3, RootsEruptLife);
            _snare = MakePool(SnareName, "Корнехват: путы", 2, 2f);
            // Лечит разом один Корнехват: сбор и волна — по два, листья — на всю пачку.
            _mendChannel = MakePool(MendChannelName, "Корнехват: сбор волны", 2, 1.4f);
            _mendRing = MakePool(MendRingName, "Корнехват: волна лечения", 2, 1.4f);
            _mendLeaves = MakePool(MendLeavesName, "Корнехват: лечение союзника", 8, 1.3f);
            _pools = new[] { _slam, _roots, _snare, _mendChannel, _mendRing, _mendLeaves };
        }

        private Pool MakePool(string prefabName, string title, int count, float life)
        {
            var pool = new Pool { Life = life };
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[Разлом] Корнехват: нет префаба «{PrefabFolder}{prefabName}» — собери «Разлом/Корнехват/VFX: пересобрать».");
                return pool;
            }
            pool.Items = new Fx[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform); go.name = title;
                pool.Items[i] = Prepare(go);
                go.SetActive(false);
            }
            return pool;
        }

        /// <summary>Разбирает экземпляр префаба: системы частиц, растущие корни, сегменты трещины.</summary>
        private static Fx Prepare(GameObject go)
        {
            var fx = new Fx { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true) };
            int count = fx.Particles.Length;
            fx.Seeds = new uint[count]; fx.Delays = new float[count]; fx.Done = new float[count]; fx.SegmentOf = new int[count];
            for (int k = 0; k < count; k++)
            {
                var ps = fx.Particles[k];
                // Прогрев: один короткий прогон заводит буферы частиц до боя.
                ps.Simulate(.05f, false, true, false);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                fx.Seeds[k] = ps.randomSeed;
                fx.Done[k] = -1f; fx.SegmentOf[k] = -1;
            }

            var root = go.transform;
            fx.Segments = new Transform[RunSegmentsPerSide * 2];
            fx.Cracks = new Transform[RunSegmentsPerSide * 2];
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < RunSegmentsPerSide; i++)
                {
                    var seg = root.Find(SegmentName(side, i));
                    if (seg == null) continue;
                    int index = side * RunSegmentsPerSide + i;
                    fx.Segments[index] = seg;
                    fx.Cracks[index] = seg.Find("Crack");
                    for (int k = 0; k < count; k++)
                        if (fx.Particles[k].transform.IsChildOf(seg)) fx.SegmentOf[k] = index;
                }
            fx.Slabs = new[] { root.Find("Slab L"), root.Find("Slab R") };

            var grows = new System.Collections.Generic.List<Grow>();
            for (int c = 0; c < root.childCount; c++)
            {
                var t = root.GetChild(c);
                if (!t.name.StartsWith(GrowPrefix, System.StringComparison.Ordinal)) continue;
                var filter = t.GetComponent<MeshFilter>();
                var renderer = t.GetComponent<Renderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null) continue;
                var g = new Grow { T = t, R = renderer, Pos = t.localPosition, Rot = t.localRotation };
                string[] parts = t.name.Split(GrowSeparator);
                if (parts.Length > 1 && int.TryParse(parts[1], out int delayMs)) g.Delay = delayMs / 1000f;
                if (parts.Length > 2 && int.TryParse(parts[2], out int twist)) g.Twist = twist;
                // Глубина, на которую часть уходит вдоль своей оси, чтобы целиком скрыться под землёй.
                g.Axis = t.localRotation * Vector3.up;
                var local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                var b = filter.sharedMesh.bounds;
                float top = float.MinValue;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x, (corner & 2) == 0 ? b.min.y : b.max.y,
                        (corner & 4) == 0 ? b.min.z : b.max.z);
                    top = Mathf.Max(top, local.MultiplyPoint3x4(p).y);
                }
                g.Depth = Mathf.Max(0f, top + .06f) / Mathf.Max(.35f, g.Axis.y);
                renderer.enabled = false;
                grows.Add(g);
            }
            fx.Grows = grows.ToArray();
            return fx;
        }

        public static string SegmentName(int side, int index) => "Seg " + (side == 0 ? "L" : "R") + " " + index;

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: сущности и тики начинаются заново.
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                RetireAll(); _shown = sim; _generation = _driver.Generation; _depth = depth;
            }
            if (sim == null || _pools == null) return;
            float tick = sim.Tick - 1 + _driver.Alpha;
            ConsumeEvents(sim);
            foreach (var pool in _pools)
                foreach (var fx in pool.Items) Advance(fx, tick);
        }

        private void ConsumeEvents(Simulation sim)
        {
            var contexts = _driver.FrameEventContexts;
            int caught = -1, caughtAt = int.MinValue;
            for (int i = 0; i < contexts.Count; i++)
            {
                var c = contexts[i];
                var e = c.Event;
                int at = c.SimulationTick - 1;
                switch (e.Type)
                {
                    case SimEventType.TelegraphOpened:
                        // Круг Корнехвата встаёт ровно в тик удара плитами.
                        if (IsSnarer(sim, e.Source)) Slam(sim, e, at);
                        break;
                    case SimEventType.EnemyActionStarted:
                        if (e.ActionVariant == (int)EnemyActionKind.SnarerMend) MendChannel(sim, e, at);
                        break;
                    case SimEventType.EnemyActionImpact:
                        if (e.ActionVariant == (int)EnemyActionKind.SnarerMend) { MendRing(e, at); break; }
                        if (e.ActionVariant != (int)EnemyActionKind.SnarerSlam) break;
                        Roots(sim, e, at);
                        if (e.Flag) { caught = e.Source; caughtAt = at; }
                        break;
                    case SimEventType.Heal:
                        if (IsSnarer(sim, e.Source)) MendLeaves(sim, e, at);
                        break;
                    case SimEventType.Damage:
                        // Урон по герою от того же удара в том же тике — замедление повешено
                        // (уклонение, неуязвимость и отложенный урон его не вешают — и Damage нет).
                        if (e.Target == Simulation.PlayerId && e.Source == caught && at == caughtAt && e.Amount > 0)
                        { Snare(sim, at); caught = -1; }
                        break;
                    case SimEventType.EnemyActionCancelled:
                        if (e.ActionVariant == (int)EnemyActionKind.SnarerSlam) SinkRootsOf(e.Source, at);
                        else if (e.ActionVariant == (int)EnemyActionKind.SnarerMend) StopMendOf(e.Source);
                        break;
                    case SimEventType.Death:
                        if (e.Target == Simulation.PlayerId) RetractSnare(at);
                        break;
                }
            }
        }

        private static bool IsSnarer(Simulation sim, int id)
            => (uint)id < (uint)sim.Entities.Count && sim.Entities.Kind[id] == EnemyKind.ForestRootSnarer;

        // ------------------------------------------------------------ effects

        /// <summary>Удар плитами: корень — в центре моба, +Z — на круг; трещины бегут до кромки круга.</summary>
        private void Slam(Simulation sim, in SimEvent e, int at)
        {
            int id = e.Source, tick = at, serial = e.ActionVariant;
            FixVec2 from = sim.Entities.Position[id], target = e.Position;
            if (sim.TryGetRootSnarerAction(id, out var a) && a.TelegraphSerial == e.ActionVariant)
            { target = a.Target; tick = a.SlamTick; serial = a.Serial; }
            var origin = new Vector2(from.X.ToFloat(), from.Y.ToFloat());
            var run = new Vector2(target.X.ToFloat(), target.Y.ToFloat()) - origin;
            float distance = run.magnitude;
            Vector3 forward = distance > 1e-3f ? new Vector3(run.x / distance, 0f, run.y / distance) : FacingOf(sim, id);
            var fx = Take(_slam, tick, Ground(origin.x, origin.y), Quaternion.LookRotation(forward, Vector3.up), serial);
            if (fx == null) return;
            fx.Entity = id; fx.Serial = serial;
            foreach (var slab in fx.Slabs) Snap(slab, .0f);
            LayoutRun(fx, distance, serial);
        }

        /// <summary>
        /// Сегменты трещины от каждой плиты к кромке круга: число — по длине, лёгкий
        /// разброс вбок и по углу (от номера удара), старт каждого — по бегу трещины.
        /// </summary>
        private void LayoutRun(Fx fx, float distance, int serial)
        {
            float radius = Simulation.RootSnarerCircleRadius.ToFloat();
            var center = new Vector3(0f, 0f, distance);
            for (int side = 0; side < 2; side++)
            {
                Vector3 from = side == 0 ? SlabLeft : SlabRight;
                Vector3 toCenter = center - from; toCenter.y = 0f;
                Vector3 to = toCenter.sqrMagnitude > 1e-6f ? center - toCenter.normalized * (radius * .8f) : from;
                Vector3 run = to - from; run.y = 0f;
                float length = run.magnitude;
                // Круг почти у плит (герой вплотную к 1,5 м) — бежать трещине некуда.
                if (Vector3.Dot(run, toCenter) <= 0f) length = 0f;
                int n = length < MinRun ? 0 : Mathf.Clamp(Mathf.CeilToInt(length / SegmentStep), 1, RunSegmentsPerSide);
                Vector3 direction = n > 0 ? run / length : Vector3.forward;
                Vector3 lateral = Vector3.Cross(Vector3.up, direction);
                float runSeconds = Mathf.Clamp(length / RunSpeed, .1f, .42f);
                for (int i = 0; i < RunSegmentsPerSide; i++)
                {
                    int index = side * RunSegmentsPerSide + i;
                    _segmentDelay[index] = 0f;
                    var seg = fx.Segments[index];
                    if (seg == null) continue;
                    bool on = i < n;
                    if (seg.gameObject.activeSelf != on) seg.gameObject.SetActive(on);
                    if (!on) continue;
                    float k = (i + .5f) / n;
                    float jitter = Hash01(serial, side * 32 + i) - .5f;
                    seg.localPosition = from + direction * (length * k) + lateral * (jitter * .22f);
                    seg.localRotation = Quaternion.LookRotation(Quaternion.AngleAxis(jitter * 24f, Vector3.up) * direction, Vector3.up);
                    Snap(seg, .0f);
                    var crack = fx.Cracks[index];
                    if (crack != null)
                        crack.localScale = new Vector3(Mathf.Lerp(.55f, .8f, Hash01(serial, side * 32 + i + 16)), 1f, length / n * 1.5f);
                    _segmentDelay[index] = k * runSeconds;
                }
            }
            for (int k = 0; k < fx.Particles.Length; k++)
                if (fx.SegmentOf[k] >= 0) fx.Delays[k] = _segmentDelay[fx.SegmentOf[k]];
        }

        /// <summary>
        /// Корни: корень префаба — в центре круга, +Z — от моба к кругу. Уходят в
        /// землю, когда моб начинает выдёргивать плиты (конец стойки − 12 тиков).
        /// </summary>
        private void Roots(Simulation sim, in SimEvent e, int at)
        {
            int id = e.Source, tick = at, sinkTick = at + Simulation.RootSnarerRecoveryTicks - ReleaseTicks + RootsSinkAfterReleaseTicks;
            int serial = at;
            if (sim.TryGetRootSnarerAction(id, out var a) && a.HitResolved)
            {
                tick = a.ImpactTick; serial = a.Serial;
                sinkTick = a.EndTick - ReleaseTicks + RootsSinkAfterReleaseTicks;
            }
            float x = e.Position.X.ToFloat(), z = e.Position.Y.ToFloat();
            Vector3 forward = Vector3.forward;
            if (IsSnarer(sim, id))
            {
                var from = sim.Entities.Position[id];
                var look = new Vector3(x - from.X.ToFloat(), 0f, z - from.Y.ToFloat());
                if (look.sqrMagnitude > 1e-6f) forward = look.normalized;
            }
            var fx = Take(_roots, tick, Ground(x, z), Quaternion.LookRotation(forward, Vector3.up), serial);
            if (fx == null) return;
            fx.Entity = id; fx.Serial = serial;
            fx.RiseSeconds = RootsRiseSeconds; fx.SinkSeconds = RootsSinkSeconds;
            fx.SinkAge = Mathf.Max(RootsRiseSeconds, (sinkTick - tick) / (float)Simulation.TicksPerSecond);
        }

        // ------------------------------------------------------------- mend

        /// <summary>Лапы в землю: свечение и пылинки у плит, корень — в центре моба, +Z — его взгляд.</summary>
        private void MendChannel(Simulation sim, in SimEvent e, int at)
        {
            int id = e.Source;
            if (!IsSnarer(sim, id)) return;
            var fx = Take(_mendChannel, at, Ground(e.Position.X.ToFloat(), e.Position.Y.ToFloat()),
                Quaternion.LookRotation(FacingOf(sim, id), Vector3.up), at * 3 + id);
            if (fx == null) return;
            fx.Entity = id;
            foreach (var slab in fx.Slabs) Snap(slab, 0f);
        }

        /// <summary>Волна: кольцо, корешки, листья и искры от центра волны.</summary>
        private void MendRing(in SimEvent e, int at)
        {
            float x = e.Position.X.ToFloat(), z = e.Position.Y.ToFloat();
            var fx = Take(_mendRing, at, Ground(x, z), Quaternion.identity, at * 5 + e.Source);
            if (fx != null) fx.Entity = e.Source;
        }

        /// <summary>Вылеченный союзник: листья и искры у его ног, держатся за ним.</summary>
        private void MendLeaves(Simulation sim, in SimEvent e, int at)
        {
            int ally = e.Target;
            if ((uint)ally >= (uint)sim.Entities.Count) return;
            var fx = Take(_mendLeaves, at, EntityGround(sim, ally), Quaternion.identity, at * 7 + ally);
            if (fx != null) fx.FollowEntity = ally;
        }

        /// <summary>Сбор сбит (оглушение, волок, урон): свечение гаснет сразу.</summary>
        private void StopMendOf(int entity)
        {
            foreach (var fx in _mendChannel.Items)
                if (fx != null && fx.Root.activeSelf && fx.Entity == entity) Retire(fx);
        }

        private Vector3 EntityGround(Simulation sim, int id)
        {
            Vector3 p;
            if (_arena != null && _arena.TryGetEntityView(id, out var view) && view != null) p = view.position;
            else p = _driver.GetRenderPosition(id);
            return Ground(p.x, p.z);
        }

        /// <summary>Оглушение или смерть моба после контакта: корни уходят сразу.</summary>
        private void SinkRootsOf(int entity, int at)
        {
            foreach (var fx in _roots.Items)
            {
                if (fx == null || !fx.Root.activeSelf || fx.Entity != entity || at < fx.Tick) continue;
                fx.SinkAge = Mathf.Min(fx.SinkAge, Mathf.Max(RootsRiseSeconds, (at - fx.Tick) / (float)Simulation.TicksPerSecond));
            }
        }

        /// <summary>
        /// Путы: кольца корней вокруг ног героя на всё замедление; держатся за героем.
        /// Срок — своё замедление удара (общий модификатор могут продлить другие мобы).
        /// </summary>
        private void Snare(Simulation sim, int at)
        {
            int slowTicks = Simulation.SnarerRoots ? Simulation.RootSnarerRootTicks : Simulation.RootSnarerSlowTicks;
            int end = at + 1 + slowTicks;
            RetractSnare(at);
            var fx = Take(_snare, at, HeroGround(sim), Quaternion.identity, at);
            if (fx == null) return;
            fx.FollowHero = true;
            fx.RiseSeconds = SnareRiseSeconds; fx.SinkSeconds = SnareSinkSeconds;
            fx.SinkAge = Mathf.Max(SnareRiseSeconds, (end - SnareRetractTicks - at) / (float)Simulation.TicksPerSecond);
            fx.Life = fx.SinkAge + SnareSinkSeconds + .8f;
        }

        private void RetractSnare(int at)
        {
            foreach (var fx in _snare.Items)
            {
                if (fx == null || !fx.Root.activeSelf || at < fx.Tick) continue;
                fx.SinkAge = Mathf.Min(fx.SinkAge, (at - fx.Tick) / (float)Simulation.TicksPerSecond);
            }
        }

        // ------------------------------------------------------------- pool

        /// <summary>
        /// Следующий экземпляр пула на земле. Зерно систем меняется с номером удара:
        /// удары не повторяют друг друга, а перемотка того же удара даёт тот же кадр.
        /// </summary>
        private static Fx Take(Pool pool, int tick, Vector3 position, Quaternion rotation, int serial)
        {
            if (pool.Items.Length == 0) return null;
            var fx = pool.Items[pool.Cursor++ % pool.Items.Length];
            fx.Tick = tick; fx.Entity = -1; fx.Serial = 0; fx.Life = pool.Life;
            fx.SinkAge = float.MaxValue; fx.RiseSeconds = RootsRiseSeconds; fx.SinkSeconds = RootsSinkSeconds;
            fx.FollowHero = false;
            fx.FollowEntity = -1;
            fx.Root.transform.SetPositionAndRotation(position, rotation);
            fx.Root.SetActive(true);
            foreach (var seg in fx.Segments) if (seg != null && !seg.gameObject.activeSelf) seg.gameObject.SetActive(true);
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                var ps = fx.Particles[k];
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = fx.Seeds[k] + (uint)serial * 7919u;
                fx.Delays[k] = 0f; fx.Done[k] = -1f;
            }
            foreach (var g in fx.Grows)
            {
                g.R.enabled = false;
                g.T.localPosition = g.Pos - g.Axis * g.Depth;
                g.T.localRotation = g.Rot;
            }
            return fx;
        }

        private static void Retire(Fx fx)
        {
            if (fx == null) return;
            fx.Tick = -1000; fx.Entity = -1; fx.FollowHero = false; fx.FollowEntity = -1;
            if (fx.Root.activeSelf) fx.Root.SetActive(false);
        }

        private void RetireAll()
        {
            if (_pools == null) return;
            foreach (var pool in _pools)
                foreach (var fx in pool.Items) Retire(fx);
        }

        /// <summary>
        /// Возраст — от тика Sim с долей кадра. Вперёд системы догоняются приращениями,
        /// назад (перемотка) — перезапуском; своя задержка у каждого сегмента трещины.
        /// </summary>
        private void Advance(Fx fx, float tick)
        {
            if (fx == null || !fx.Root.activeSelf) return;
            float age = (tick - fx.Tick) / Simulation.TicksPerSecond;
            if (age > fx.Life) { Retire(fx); return; }
            age = Mathf.Max(0f, age);
            if (fx.FollowHero) Follow(fx);
            else if (fx.FollowEntity >= 0 && _driver.Sim != null && (uint)fx.FollowEntity < (uint)_driver.Sim.Entities.Count)
                fx.Root.transform.position = EntityGround(_driver.Sim, fx.FollowEntity);
            AnimateGrows(fx, age);
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                var ps = fx.Particles[k];
                if (!ps.gameObject.activeInHierarchy) continue;
                float want = age - fx.Delays[k];
                if (want <= 0f)
                {
                    if (fx.Done[k] >= 0f) { ps.Clear(false); fx.Done[k] = -1f; }
                    continue;
                }
                if (fx.Done[k] >= 0f && Mathf.Abs(want - fx.Done[k]) < 1e-5f) continue;
                bool restart = fx.Done[k] < 0f || want < fx.Done[k];
                float done = restart ? 0f : fx.Done[k];
                bool first = restart;
                do
                {
                    float step = Mathf.Min(SimulateStep, want - done);
                    ps.Simulate(step, false, first, false);
                    first = false;
                    done += step;
                } while (done < want - 1e-5f);
                ps.Pause(false);
                fx.Done[k] = want;
            }
        }

        /// <summary>
        /// Растущие части: вылезают из земли вдоль своей оси с небольшим перелётом
        /// (у колец пут — ещё и довинчиваются), стоят и уходят обратно с SinkAge.
        /// </summary>
        private static void AnimateGrows(Fx fx, float age)
        {
            foreach (var g in fx.Grows)
            {
                float t = age - g.Delay;
                float up = t <= 0f ? 0f : Rise(t / Mathf.Max(.01f, fx.RiseSeconds));
                float sink = age - fx.SinkAge - g.Delay * .5f;
                if (sink > 0f)
                {
                    float s = Mathf.Clamp01(sink / Mathf.Max(.01f, fx.SinkSeconds));
                    up *= 1f - s * s;
                }
                bool show = up > .002f;
                if (g.R.enabled != show) g.R.enabled = show;
                if (!show) continue;
                g.T.localPosition = g.Pos - g.Axis * (g.Depth * (1f - up));
                float twist = g.Twist * (1f - Mathf.Min(1f, up));
                g.T.localRotation = twist != 0f ? Quaternion.AngleAxis(twist, Vector3.up) * g.Rot : g.Rot;
            }
        }

        /// <summary>Выход из земли: быстрый, с перелётом ~6% и возвратом (ease-out back).</summary>
        private static float Rise(float x)
        {
            if (x >= 1f) return 1f;
            const float c1 = 1.2f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        private void Follow(Fx fx)
        {
            var sim = _driver.Sim;
            if (sim == null) return;
            fx.Root.transform.position = HeroGround(sim);
        }

        private Vector3 HeroGround(Simulation sim)
        {
            Vector3 p;
            if (_arena != null && _arena.TryGetEntityView(Simulation.PlayerId, out var hero) && hero != null) p = hero.position;
            else if (sim.Entities.Count > Simulation.PlayerId) p = _driver.GetRenderPosition(Simulation.PlayerId);
            else p = Vector3.zero;
            return Ground(p.x, p.z);
        }

        private Vector3 FacingOf(Simulation sim, int id)
        {
            var f = sim.Entities.Facing[id];
            var forward = new Vector3(f.X.ToFloat(), 0f, f.Y.ToFloat());
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        private Vector3 Ground(float x, float z) => new Vector3(x, GroundY(x, z), z);

        private float GroundY(float x, float z) => _layout != null ? _layout.WeaponGroundHeight(x, z) : 0f;

        /// <summary>Часть префаба на неровной земле: мировая высота — земля под ней плюс lift.</summary>
        private void Snap(Transform t, float lift)
        {
            if (t == null) return;
            var w = t.position;
            t.position = new Vector3(w.x, GroundY(w.x, w.z) + lift, w.z);
        }

        /// <summary>Детерминированный разброс по номеру удара: перемотка даёт ту же трещину.</summary>
        private static float Hash01(int i, int salt)
        {
            uint h = (uint)(i * 747796405 + salt * 2891336453u);
            h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        private void OnDisable() => RetireAll();

        private void OnDestroy()
        {
            if (_pools == null) return;
            foreach (var pool in _pools)
                foreach (var fx in pool.Items) if (fx != null && fx.Root != null) Destroy(fx.Root);
        }
    }
}
