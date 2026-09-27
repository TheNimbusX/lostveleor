using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// ШИП ВЫСТРЕЛА ШИПОМЁТА (требование владельца от 26.09: «для шипов
    /// выстрелов не делай на земле видимую траекторию, а просто пусть будет
    /// проджектайл, от которого можно увернуться»). Метки на земле у выстрела
    /// нет ни в Sim, ни здесь: игрок читает замах (клип бросает шип на 21-м
    /// кадре) и сам шип — поэтому шип обязан читаться в движении.
    ///
    /// Шип — шип Шипомёта из его же VFX (Resources/VFX/Thorncaster/Geometry/
    /// ThornDart — главный шип без раструба, с закрытым основанием; нет его —
    /// ThornSpire; кора M_Thorn_Wood: мшистое дерево, красно-оранжевый
    /// кончик с отростками — как шипы линии и на теле), остриём по полёту, на
    /// уровне груди, ≈0,2 м в радиусе (толщина попадания Simulation.ThornShotRadius —
    /// 0,25), с вращением вокруг оси и лёгким покачиванием. Нет ThornSpire —
    /// корневой шип Вендиго (WendigoRootThornC, M_Wendigo_RootWood). За ним короткий след из паков: пыль (размытое облако CFXR,
    /// M_Wendigo_Dust) и щепки коры (CFXR debris wood unlit 3×3, M_Wendigo_Bark);
    /// на остановке — горсть щепок и пыли: попал — шип ломается о героя, не
    /// попал — клюёт вниз и уходит в землю.
    ///
    /// ИЗ СОБЫТИЙ И ИЗ Sim. Шип заводится по EnemyProjectileLaunched, встаёт по
    /// EnemyActionImpact(ThornShot); где он между ними — только из Sim
    /// (Simulation.TryGetThornShot): начало пути, направление, пройденное; между
    /// тиками — по часам Sim (тик − 1 + Alpha). Частицы следа и остановки своей
    /// жизнью не живут: каждая — функция номера выстрела, пройденного пути и
    /// тика Sim (SetParticles), так что пауза, хит-стоп и съёмка держат кадр, а
    /// повтор даёт тот же рисунок. Шип мёртвого Шипомёта летит так же: он
    /// снаряд, и Sim держит его без стрелка.
    ///
    /// СРЫВАЕТСЯ С РУКИ. Путь Sim начинается в 0,8 м перед Шипомётом на высоте
    /// полёта, а кончик руки-шипа в кадре выпуска — выше (≈1,9 м) и чуть в
    /// стороне. Шип стартует из кончика руки (ThorncasterAnimatorView.MuzzlePosition)
    /// и за первые MuzzleEaseMetres пути плавно сходит на линию полёта — до
    /// ближайшей дистанции выстрела (3,5 м), так что попадание и промах видны
    /// там же, где их считает Sim.
    /// </summary>
    [DefaultExecutionOrder(645)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ForestThornShotView : MonoBehaviour
    {
        private sealed class Thorn
        {
            public Transform Root, Wobble, Body;
            public ParticleSystem Dust, Bark;
            public ParticleSystem.Particle[] DustBuffer, BarkBuffer;
            public int Caster = -1, Serial;
            public bool Shown, Flying, Hit;
            public ThornShotState Shot;
            /// <summary>Сдвиг старта к кончику руки поперёк пути (вверх и вбок); сходит на нет к MuzzleEaseMetres.</summary>
            public Vector3 MuzzleOffset;
            /// <summary>Остриё: где встало (м пути), когда встало (тик Sim), сколько уже показано.</summary>
            public float StopDistance, StopTick, ShownTip;
        }

        /// <summary>
        /// Шип выстрела — ThornDart (без раструба, основание закрыто): в полёте основание
        /// смотрит назад и вверх, в камеру, и открытая труба ThornSpire читалась воронкой.
        /// Нет ThornDart (старая сборка VFX) — летит ThornSpire.
        /// </summary>
        private const string DartResource = "VFX/Thorncaster/Geometry/ThornDart";
        private const string SpireResource = "VFX/Thorncaster/Geometry/ThornSpire";
        private const string SpireMaterialResource = "VFX/Thorncaster/Materials/M_Thorn_Wood";
        private const string MeshResource = "VFX/Wendigo/Geometry/WendigoRootThornC";
        private const string MaterialResource = "VFX/Wendigo/Materials/M_Wendigo_RootWood";
        private const string DustResource = "VFX/Wendigo/Materials/M_Wendigo_Dust";
        private const string BarkResource = "VFX/Wendigo/Materials/M_Wendigo_Bark";

        /// <summary>Шипомёт — элита, их в бою один-два, шип у каждого один: шесть мест с запасом.</summary>
        private const int PoolSize = 6;

        /// <summary>
        /// Длина шипа в мире, м, и толщина меша: ThornSpire — 0,15 м в радиусе при
        /// единичном масштабе (раструб у основания шире), корень Вендиго — ≈0,13.
        /// </summary>
        private const float ThornLength = 1.3f, SpireThickness = 1.3f, ThornThickness = 1.7f;

        /// <summary>Высота полёта над землёй — на уровне груди героя.</summary>
        private const float FlightHeight = 1.05f;

        /// <summary>
        /// За столько метров пути шип сходит с кончика руки на линию полёта. Меньше
        /// ближайшей дистанции выстрела за вычетом начала пути и тела героя (≈2,3 м).
        /// </summary>
        private const float MuzzleEaseMetres = 2.2f;

        /// <summary>Вращение вокруг оси полёта, градусов за тик Sim (450°/с).</summary>
        private const float SpinDegreesPerTick = 15f;

        /// <summary>Покачивание: амплитуда, градусов, и период, тиков Sim.</summary>
        private const float WobbleDegrees = 4f, WobblePeriodTicks = 7f;

        /// <summary>Сломанный о героя шип исчезает за столько тиков, упавший — клюёт и уходит в землю.</summary>
        private const float ShatterTicks = 3f, DropTicks = 9f;

        // След: клуб пыли каждые 0,3 м пути, щепка — через клуб. Короткий: пыль
        // живёт 0,3 с, щепка — 0,45 с, за шипом тянется метра три-четыре.
        private const float PuffSpacing = .3f;
        private const float DustLifeTicks = 9f, BarkLifeTicks = 13.5f;
        private const int BurstBark = 10, BurstDust = 4, DropBark = 5, DropDust = 3;
        private const float BurstLifeTicks = 15f;
        private const float Gravity = 12f;
        private const int Capacity = 48;

        private static readonly Color DustLight = new Color(.76f, .58f, .39f), DustDark = new Color(.58f, .43f, .28f);
        private static readonly Color BarkLight = new Color(.66f, .52f, .36f), BarkDark = new Color(.45f, .33f, .22f);

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;
        private Thorn[] _thorns;
        private Material _fallbackMaterial, _spireMaterial;
        private bool _overflowReported;

        /// <summary>
        /// Сколько остриё пролетело от начала пути к моменту tick на часах Sim
        /// (тик − 1 + Alpha): в тик выпуска — от 0 до 0,6 м, дальше по 0,6 м за
        /// тик, но не дальше, чем Sim уже насчитала. Снизу — не дальше одного
        /// тика позади насчитанного: в Песочных Часах Sim сдвигает выпуск вперёд
        /// (DelayThornShots), и без этого шип отскочил бы назад.
        /// </summary>
        public static float TipDistance(in ThornShotState shot, float tick)
        {
            float speed = Simulation.ThornShotSpeed.ToFloat(), travelled = shot.Travelled.ToFloat();
            return Mathf.Clamp(speed * (tick - shot.ReleaseTick), Mathf.Max(0f, travelled - speed), travelled);
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
            var spire = Resources.Load<Mesh>(DartResource);
            if (spire == null) spire = Resources.Load<Mesh>(SpireResource);
            var spireWood = Resources.Load<Material>(SpireMaterialResource);
            if (spire != null && spireWood != null)
            {
                // У ThornSpire труба открыта у основания, а оно в полёте смотрит в камеру: своя
                // копия коры рисует обе стороны, чтобы при старой сборке на его месте не зияла дыра.
                _spireMaterial = new Material(spireWood) { name = spireWood.name + " (выстрел)" };
                if (_spireMaterial.HasProperty("_Cull")) _spireMaterial.SetFloat("_Cull", (float)CullMode.Off);
            }
            var mesh = _spireMaterial != null ? null : Resources.Load<Mesh>(MeshResource);
            var material = _spireMaterial != null ? null : Resources.Load<Material>(MaterialResource);
            var dust = Resources.Load<Material>(DustResource);
            var bark = Resources.Load<Material>(BarkResource);
            if (_spireMaterial == null)
                Debug.LogWarning($"[Разлом] Шип Шипомёта: нет «{SpireResource}» — летит корнем Вендиго (собери «Разлом/Шипомёт/VFX: пересобрать»).");
            if (_spireMaterial == null && (mesh == null || material == null))
                Debug.LogWarning($"[Разлом] Шип Шипомёта: нет «{MeshResource}» или «{MaterialResource}» — летит капсулой.");
            if (dust == null || bark == null)
                Debug.LogWarning($"[Разлом] Шип Шипомёта: нет «{DustResource}» или «{BarkResource}» — без следа и щепок.");
            _thorns = new Thorn[PoolSize];
            for (int i = 0; i < PoolSize; i++) _thorns[i] = Create(i, spire, mesh, material, dust, bark);
        }

        /// <summary>
        /// Корень шипа стоит в острие и смотрит по полёту (+Z); тело тянется назад.
        /// Покачивание — вокруг середины тела, чтобы остриё не сходило с пути.
        /// </summary>
        private Thorn Create(int index, Mesh spire, Mesh mesh, Material material, Material dust, Material bark)
        {
            var thorn = new Thorn { Root = new GameObject("Шип Шипомёта " + index).transform };
            thorn.Root.SetParent(transform, false);
            thorn.Wobble = new GameObject("Покачивание").transform;
            thorn.Wobble.SetParent(thorn.Root, false);
            thorn.Wobble.localPosition = new Vector3(0f, 0f, -ThornLength * .5f);
            GameObject body;
            if (_spireMaterial != null)
            {
                body = new GameObject("Шип");
                body.AddComponent<MeshFilter>().sharedMesh = spire;
                body.AddComponent<MeshRenderer>().sharedMaterial = _spireMaterial;
                body.transform.SetParent(thorn.Wobble, false);
                // ThornSpire: основание в нуле, ось +Y, длина 1. Поворот на 90° вокруг X
                // кладёт ось по полёту (+Z): основание позади, остриё в корне шипа.
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                body.transform.localScale = new Vector3(SpireThickness, ThornLength, SpireThickness);
                body.transform.localPosition = new Vector3(0f, 0f, -ThornLength * .5f);
            }
            else if (mesh != null && material != null)
            {
                body = new GameObject("Шип");
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = material;
                body.transform.SetParent(thorn.Wobble, false);
                // Меш корня Вендиго: основание в нуле, остриё в −Z на 1 м. Разворот
                // на 180° и сдвиг ставят остриё в корень, основание — позади него.
                body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                body.transform.localScale = new Vector3(ThornThickness, ThornThickness, ThornLength);
                body.transform.localPosition = new Vector3(0f, 0f, -ThornLength * .5f);
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Шип (капсула)";
                // Столкновения считает Sim: коллайдер примитива тут лишний.
                var collider = body.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                if (_fallbackMaterial == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader != null)
                    {
                        _fallbackMaterial = new Material(shader) { name = "Runtime_ThornShotFallback" };
                        _fallbackMaterial.SetColor("_BaseColor", new Color(.36f, .25f, .16f, 1f));
                    }
                }
                if (_fallbackMaterial != null) body.GetComponent<MeshRenderer>().sharedMaterial = _fallbackMaterial;
                body.transform.SetParent(thorn.Wobble, false);
                // Капсула лежит вдоль Y: поворот на 90° вокруг X кладёт её по полёту.
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                body.transform.localScale = new Vector3(.3f, ThornLength * .5f, .3f);
                body.transform.localPosition = Vector3.zero;
            }
            thorn.Body = body.transform;
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = false;
            thorn.Root.gameObject.SetActive(false);
            // Частицы — в мире и вне корня шипа: след и щепки остаются, когда шип ушёл.
            if (dust != null && bark != null)
            {
                thorn.Dust = Particles("След шипа: пыль " + index, dust, 2, 3.99f);
                thorn.Bark = Particles("След шипа: щепки " + index, bark, 3, 8.99f);
                thorn.DustBuffer = new ParticleSystem.Particle[Capacity];
                thorn.BarkBuffer = new ParticleSystem.Particle[Capacity];
            }
            return thorn;
        }

        /// <summary>
        /// Система без своей эмиссии и своего времени: частицы ставит
        /// SetParticles каждый кадр, скорость у них нулевая, срок — с запасом.
        /// Кадр атласа пака n×n — случайный по зерну частицы, без анимации.
        /// </summary>
        private ParticleSystem Particles(string name, Material material, int tiles, float lastFrame)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Capacity;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var sheet = particles.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = tiles; sheet.numTilesY = tiles;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
            sheet.cycleCount = 1;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            particles.Play();
            return particles;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: номера выстрелов начинаются заново.
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                Clear(); _shown = sim; _generation = _driver.Generation; _depth = depth;
            }
            if (sim == null) return;
            float tick = sim.Tick - 1 + _driver.Alpha;

            // События кадра: выпуск заводит шип, остановка ставит его туда, где его остановила Sim.
            // SimulationTick — тик после шага; сам шаг — на единицу раньше.
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.ActionVariant != (int)EnemyActionKind.ThornShot) continue;
                int at = contexts[i].SimulationTick - 1;
                if (e.Type == SimEventType.EnemyProjectileLaunched) Launch(sim, e.Source, e.Amount, e.Position, at, fromHand: true);
                else if (e.Type == SimEventType.EnemyActionImpact) Stop(sim, e.Source, e.Position, e.Flag, at);
            }

            // Летящие: путь и пройденное — из Sim. Шип, чей выпуск вид пропустил
            // (включён посреди полёта), заводится здесь же из состояния.
            var entities = sim.Entities;
            for (int id = 1; id < entities.Count; id++)
            {
                if (entities.Kind[id] != EnemyKind.ForestThorncaster || !sim.TryGetThornShot(id, out var shot)) continue;
                var thorn = Find(id, shot.Serial) ?? Launch(sim, id, shot.Serial, shot.Origin, shot.ReleaseTick);
                if (thorn != null && thorn.Flying) thorn.Shot = shot;
            }

            foreach (var thorn in _thorns)
            {
                if (!thorn.Shown) continue;
                // Пропал из Sim без события (сброс) — встаёт там, где его видели, без щепок.
                if (thorn.Flying && !(sim.TryGetThornShot(thorn.Caster, out var still) && still.Serial == thorn.Serial))
                {
                    thorn.Flying = false; thorn.Hit = false;
                    thorn.StopDistance = thorn.ShownTip; thorn.StopTick = tick;
                }
                Draw(thorn, tick);
            }
        }

        /// <summary>Летящий шип Шипомёта caster с номером выстрела serial или null.</summary>
        private Thorn Find(int caster, int serial)
        {
            foreach (var thorn in _thorns)
                if (thorn.Shown && thorn.Caster == caster && thorn.Serial == serial) return thorn;
            return null;
        }

        /// <summary>
        /// Шип вылетел в тик at из точки start. Направление и путь — из Sim, пока
        /// шип в воздухе; если он уже встал в этом же кадре, их даст остановка.
        /// </summary>
        private Thorn Launch(Simulation sim, int caster, int serial, FixVec2 start, int at, bool fromHand = false)
        {
            var known = Find(caster, serial);
            if (known != null) return known;
            Thorn free = null;
            foreach (var thorn in _thorns) if (!thorn.Shown) { free = thorn; break; }
            if (free == null)
            {
                // Самый старый остановленный уступает место: летящий шип важнее догорающих щепок.
                foreach (var thorn in _thorns)
                    if (!thorn.Flying && (free == null || thorn.StopTick < free.StopTick)) free = thorn;
                if (free == null)
                {
                    if (!_overflowReported) { Debug.LogError("[Разлом] Шип Шипомёта: исчерпан пул шипов — шип летит невидимым."); _overflowReported = true; }
                    return null;
                }
                Hide(free);
            }
            if (!sim.TryGetThornShot(caster, out var shot) || shot.Serial != serial)
            {
                var facing = (uint)caster < (uint)sim.Entities.Count ? sim.Entities.Facing[caster] : new FixVec2(Fix64.One, Fix64.Zero);
                shot = new ThornShotState { Serial = serial, ReleaseTick = at, Origin = start, Direction = facing,
                    Length = Simulation.ThornShotMaxLength };
            }
            free.Caster = caster; free.Serial = serial; free.Shown = true; free.Flying = true; free.Hit = false;
            free.Shot = shot; free.ShownTip = 0f; free.StopDistance = 0f; free.StopTick = float.MaxValue;
            // С руки — только в кадр выпуска: шип, заведённый посреди полёта, уже на линии.
            free.MuzzleOffset = fromHand ? MuzzleOffset(caster, in shot) : Vector3.zero;
            free.Root.localScale = Vector3.one;
            free.Root.gameObject.SetActive(true);
            return free;
        }

        /// <summary>
        /// Шип Шипомёта caster встал в точке point (попал — hit). Остриё
        /// догоняет точку по своему расписанию и дальше не идёт; возраст щепок
        /// и падения — от тика, когда оно туда пришло.
        /// </summary>
        private void Stop(Simulation sim, int caster, FixVec2 point, bool hit, int at)
        {
            Thorn stopped = null;
            foreach (var thorn in _thorns)
                if (thorn.Shown && thorn.Flying && thorn.Caster == caster) { stopped = thorn; break; }
            if (stopped == null)
            {
                // Выпуска вид не видел: шип встаёт сразу, направление — от стрелка к точке.
                var from = (uint)caster < (uint)sim.Entities.Count ? sim.Entities.Position[caster] : point;
                stopped = Launch(sim, caster, -1 - at, point, at);
                if (stopped == null) return;
                var away = point - from;
                if (away.LengthSq.Raw > 0) stopped.Shot.Direction = away.Normalized();
                stopped.Shot.Origin = point;
            }
            var s = stopped.Shot;
            float speed = Simulation.ThornShotSpeed.ToFloat();
            float distance = Mathf.Max(0f, FixVec2.Dot(point - s.Origin, s.Direction).ToFloat());
            stopped.Flying = false; stopped.Hit = hit;
            stopped.StopDistance = distance;
            stopped.StopTick = Mathf.Max(at, s.ReleaseTick + distance / speed);
        }

        /// <summary>
        /// Кончик руки Шипомёта caster против начала пути на высоте полёта — только
        /// поперёк пути: вдоль него шип идёт по расписанию Sim. Нет тела — ноль.
        /// </summary>
        private Vector3 MuzzleOffset(int caster, in ThornShotState shot)
        {
            if (_arena == null || !_arena.TryGetEntityView(caster, out Transform view)) return Vector3.zero;
            var body = ThorncasterViewInstaller.ViewOf(view);
            if (body == null || body.Entity != caster) return Vector3.zero;
            float x = shot.Origin.X.ToFloat(), z = shot.Origin.Y.ToFloat();
            var direction = new Vector3(shot.Direction.X.ToFloat(), 0f, shot.Direction.Y.ToFloat());
            if (direction.sqrMagnitude < 1e-6f) return Vector3.zero;
            direction.Normalize();
            Vector3 offset = body.MuzzlePosition - new Vector3(x, Ground(x, z) + FlightHeight, z);
            offset -= direction * Vector3.Dot(offset, direction);
            // Сокет, уехавший с тела (кривой импорт), не должен уводить шип: не дальше 1,5 м.
            return offset.sqrMagnitude > 2.25f ? Vector3.zero : offset;
        }

        /// <summary>Доля сдвига к руке на distance метров пути: 1 на выпуске, 0 с MuzzleEaseMetres.</summary>
        private static float MuzzleShare(float distance)
        {
            float u = Mathf.Clamp01(distance / MuzzleEaseMetres);
            return 1f - u * u * (3f - 2f * u);
        }

        /// <summary>Производная доли по пути, 1/м: наклон шипа, пока он сходит с руки.</summary>
        private static float MuzzleShareSlope(float distance)
        {
            float u = Mathf.Clamp01(distance / MuzzleEaseMetres);
            return -6f * u * (1f - u) / MuzzleEaseMetres;
        }

        private void Draw(Thorn thorn, float tick)
        {
            var s = thorn.Shot;
            float speed = Simulation.ThornShotSpeed.ToFloat();
            float tip = thorn.Flying ? TipDistance(in s, tick)
                : Mathf.Min(thorn.StopDistance, speed * (tick - s.ReleaseTick));
            tip = Mathf.Max(tip, thorn.Flying ? 0f : Mathf.Min(thorn.ShownTip, thorn.StopDistance));
            thorn.ShownTip = tip;
            var origin = new Vector2(s.Origin.X.ToFloat(), s.Origin.Y.ToFloat());
            var direction = new Vector2(s.Direction.X.ToFloat(), s.Direction.Y.ToFloat());
            if (direction.sqrMagnitude < 1e-6f) direction = Vector2.up;
            direction.Normalize();

            float after = thorn.Flying ? -1f : tick - thorn.StopTick;
            float thornEnd = thorn.Hit ? ShatterTicks : DropTicks;
            if (!thorn.Flying && after > thornEnd + Mathf.Max(BurstLifeTicks, BarkLifeTicks))
            {
                Hide(thorn);
                return;
            }
            PlaceThorn(thorn, origin + direction * tip, direction, tip, tick, after, thornEnd);
            DrawParticles(thorn, origin, direction, tip, tick, after);
        }

        /// <summary>
        /// Остриё в точке tip на уровне груди, по полёту, с вращением и
        /// покачиванием. Встал: сломанный о героя шип сжимается за три тика,
        /// упавший клюёт вниз, опускается к земле и уходит в неё.
        /// </summary>
        private void PlaceThorn(Thorn thorn, Vector2 tip, Vector2 direction, float distance, float tick, float after, float thornEnd)
        {
            if (after >= thornEnd) { if (thorn.Root.gameObject.activeSelf) thorn.Root.gameObject.SetActive(false); return; }
            float ground = Ground(tip.x, tip.y);
            // Сходит с руки: остриё смещено к её кончику и смотрит вдоль изгиба пути.
            var forward = new Vector3(direction.x, 0f, direction.y) + thorn.MuzzleOffset * MuzzleShareSlope(distance);
            Vector3 lift = thorn.MuzzleOffset * MuzzleShare(distance);
            float height = FlightHeight, pitch = 0f, scale = 1f;
            if (after >= 0f)
            {
                float k = Mathf.Clamp01(after / thornEnd);
                if (thorn.Hit) scale = 1f - k * k;
                else
                {
                    // Клюёт остриём в землю и уходит в неё к концу падения.
                    pitch = 38f * Mathf.Sqrt(k);
                    height = Mathf.Lerp(FlightHeight, .05f, k * k);
                    scale = k < .7f ? 1f : 1f - (k - .7f) / .3f;
                }
            }
            float spin = tick * SpinDegreesPerTick;
            float phase = thorn.Serial * 1.7f;
            float wobble = after >= 0f ? 0f : WobbleDegrees;
            thorn.Root.SetPositionAndRotation(new Vector3(tip.x, ground + height, tip.y) + lift,
                Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f));
            thorn.Root.localScale = Vector3.one * Mathf.Max(0f, scale);
            thorn.Wobble.localRotation = Quaternion.Euler(
                wobble * Mathf.Sin(tick * 2f * Mathf.PI / WobblePeriodTicks + phase),
                wobble * .7f * Mathf.Sin(tick * 2f * Mathf.PI / (WobblePeriodTicks * 1.3f) + phase * 2f), spin);
            if (!thorn.Root.gameObject.activeSelf) thorn.Root.gameObject.SetActive(true);
        }

        /// <summary>
        /// След и остановка. Клуб пыли встаёт каждые PuffSpacing метров пути в
        /// тот тик, когда там прошло остриё; через клуб — щепка коры, которая
        /// отлетает назад-вверх и падает. На остановке — горсть щепок и пыли:
        /// попал — у тела героя, на уровне груди; не попал — под остриём, когда
        /// шип уходит в землю. Всё — от номера выстрела и тика Sim.
        /// </summary>
        private void DrawParticles(Thorn thorn, Vector2 origin, Vector2 direction, float tip, float tick, float after)
        {
            if (thorn.Dust == null) return;
            var s = thorn.Shot;
            float speed = Simulation.ThornShotSpeed.ToFloat();
            var forward = new Vector3(direction.x, 0f, direction.y);
            var side = new Vector3(-direction.y, 0f, direction.x);
            int dust = 0, bark = 0;
            int puffs = Mathf.FloorToInt(tip / PuffSpacing);
            for (int j = 0; j < puffs; j++)
            {
                float d = (j + .5f) * PuffSpacing;
                float born = s.ReleaseTick + d / speed;
                float age = tick - born;
                if (age < 0f || age >= BarkLifeTicks * 1.2f) continue;
                var at2 = origin + direction * d;
                var at = new Vector3(at2.x, Ground(at2.x, at2.y) + FlightHeight, at2.y) + thorn.MuzzleOffset * MuzzleShare(d);
                float life = DustLifeTicks * (.8f + .4f * Rand(thorn.Serial, j, 1));
                if (age < life && dust < Capacity)
                {
                    float u = age / life;
                    var p = at + side * ((Rand(thorn.Serial, j, 2) - .5f) * .2f)
                        + Vector3.up * ((Rand(thorn.Serial, j, 3) - .5f) * .16f)
                        - forward * (.25f * u) + Vector3.up * (.3f * u);
                    float size = Mathf.Lerp(.25f, .55f, Mathf.Sqrt(u)) * (.8f + .4f * Rand(thorn.Serial, j, 4));
                    float alpha = .32f * (1f - u) * (1f - u) * Mathf.Clamp01(age);
                    Set(ref thorn.DustBuffer[dust++], p, size, Color.Lerp(DustLight, DustDark, Rand(thorn.Serial, j, 5)), alpha,
                        Rand(thorn.Serial, j, 6) * 360f, Seed(thorn.Serial, j, 7));
                }
                if (Rand(thorn.Serial, j, 8) > .55f || bark >= Capacity) continue;
                float barkLife = BarkLifeTicks * (.8f + .4f * Rand(thorn.Serial, j, 9));
                if (age >= barkLife) continue;
                var velocity = -forward * (.6f + 1.2f * Rand(thorn.Serial, j, 10))
                    + side * ((Rand(thorn.Serial, j, 11) - .5f) * 2.4f)
                    + Vector3.up * (.8f + 1.4f * Rand(thorn.Serial, j, 12));
                Set(ref thorn.BarkBuffer[bark++], Fly(at, velocity, age), .08f + .06f * Rand(thorn.Serial, j, 13),
                    Color.Lerp(BarkLight, BarkDark, Rand(thorn.Serial, j, 14)), Fade(age / barkLife),
                    Rand(thorn.Serial, j, 15) * 360f + (Rand(thorn.Serial, j, 16) - .5f) * 24f * age, Seed(thorn.Serial, j, 17));
            }

            if (after >= 0f)
            {
                // Попал — горсть у тела героя; не попал — под остриём, когда шип дошёл до земли.
                var stop2 = origin + direction * thorn.StopDistance;
                float ground = Ground(stop2.x, stop2.y);
                float start = thorn.Hit ? 0f : DropTicks * .6f;
                float age = after - start;
                var at = new Vector3(stop2.x, ground + (thorn.Hit ? FlightHeight : .08f), stop2.y);
                int barkCount = thorn.Hit ? BurstBark : DropBark, dustCount = thorn.Hit ? BurstDust : DropDust;
                for (int i = 0; age >= 0f && i < barkCount && bark < Capacity; i++)
                {
                    int k = 1000 + i;
                    float life = BurstLifeTicks * (.7f + .3f * Rand(thorn.Serial, k, 1));
                    if (age >= life) continue;
                    // Щепки летят назад, к стрелку, и в стороны — не сквозь героя.
                    var velocity = -forward * (thorn.Hit ? 1f + 2f * Rand(thorn.Serial, k, 2) : .3f * Rand(thorn.Serial, k, 2))
                        + side * ((Rand(thorn.Serial, k, 3) - .5f) * (thorn.Hit ? 3.5f : 2.2f))
                        + Vector3.up * ((thorn.Hit ? 1f : 1.4f) + 2f * Rand(thorn.Serial, k, 4));
                    Set(ref thorn.BarkBuffer[bark++], Fly(at, velocity, age), .09f + .08f * Rand(thorn.Serial, k, 5),
                        Color.Lerp(BarkLight, BarkDark, Rand(thorn.Serial, k, 6)), Fade(age / life),
                        Rand(thorn.Serial, k, 7) * 360f + (Rand(thorn.Serial, k, 8) - .5f) * 30f * age, Seed(thorn.Serial, k, 9));
                }
                for (int i = 0; age >= 0f && i < dustCount && dust < Capacity; i++)
                {
                    int k = 2000 + i;
                    float life = 12f * (.8f + .4f * Rand(thorn.Serial, k, 1));
                    if (age >= life) continue;
                    float u = age / life;
                    var drift = side * ((Rand(thorn.Serial, k, 2) - .5f) * .8f) - forward * (.3f * Rand(thorn.Serial, k, 3))
                        + Vector3.up * (.35f + .25f * Rand(thorn.Serial, k, 4));
                    var p = at + drift * u + Vector3.up * (thorn.Hit ? 0f : .15f);
                    float size = Mathf.Lerp(.3f, .8f, Mathf.Sqrt(u)) * (.8f + .4f * Rand(thorn.Serial, k, 5));
                    Set(ref thorn.DustBuffer[dust++], p, size, Color.Lerp(DustLight, DustDark, Rand(thorn.Serial, k, 6)),
                        .38f * (1f - u) * (1f - u), Rand(thorn.Serial, k, 7) * 360f, Seed(thorn.Serial, k, 8));
                }
            }
            thorn.Dust.SetParticles(thorn.DustBuffer, dust);
            thorn.Bark.SetParticles(thorn.BarkBuffer, bark);
        }

        /// <summary>Баллистика щепки от at со скоростью velocity за age тиков Sim; на земле лежит.</summary>
        private Vector3 Fly(Vector3 at, Vector3 velocity, float age)
        {
            float t = age / Simulation.TicksPerSecond;
            var p = at + velocity * t + Vector3.down * (.5f * Gravity * t * t);
            float floor = Ground(p.x, p.z) + .03f;
            if (p.y < floor) p.y = floor;
            return p;
        }

        /// <summary>Непрозрачна, последние 30% жизни тает.</summary>
        private static float Fade(float u) => u < .7f ? 1f : Mathf.Clamp01(1f - (u - .7f) / .3f);

        private static void Set(ref ParticleSystem.Particle particle, Vector3 position, float size, Color color, float alpha,
            float rotation, uint seed)
        {
            color.a = Mathf.Clamp01(alpha);
            particle.position = position;
            particle.velocity = Vector3.zero;
            particle.startSize = size;
            particle.startColor = (Color32)color;
            particle.rotation = rotation;
            particle.angularVelocity = 0f;
            particle.startLifetime = 10f;
            particle.remainingLifetime = 10f;
            particle.randomSeed = seed;
        }

        /// <summary>Случайное в [0, 1) — функция выстрела, номера частицы и соли; одинаково на любом кадре.</summary>
        private static float Rand(int serial, int index, int salt) => (Seed(serial, index, salt) & 0xFFFFFF) / 16777216f;

        private static uint Seed(int serial, int index, int salt)
        {
            uint h = unchecked((uint)serial * 0x9E3779B1u ^ (uint)index * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du);
            h ^= h >> 15; h = unchecked(h * 0x2C1B3C6Du);
            h ^= h >> 12; h = unchecked(h * 0x297A2D39u);
            h ^= h >> 15;
            return h;
        }

        private float Ground(float x, float z) => _layout != null ? _layout.WeaponGroundHeight(x, z) : 0f;

        private static void Hide(Thorn thorn)
        {
            thorn.Shown = false; thorn.Flying = false; thorn.Hit = false; thorn.Caster = -1; thorn.Serial = 0;
            thorn.ShownTip = thorn.StopDistance = 0f; thorn.StopTick = float.MaxValue;
            thorn.MuzzleOffset = Vector3.zero;
            thorn.Root.localScale = Vector3.one;
            thorn.Root.gameObject.SetActive(false);
            if (thorn.Dust != null) { thorn.Dust.SetParticles(thorn.DustBuffer, 0); thorn.Bark.SetParticles(thorn.BarkBuffer, 0); }
        }

        private void Clear()
        {
            if (_thorns == null) return;
            foreach (var thorn in _thorns) Hide(thorn);
        }

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            if (_fallbackMaterial != null) Destroy(_fallbackMaterial);
            if (_spireMaterial != null) Destroy(_spireMaterial);
        }
    }
}
