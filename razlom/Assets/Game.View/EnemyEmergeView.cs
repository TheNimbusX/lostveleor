using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Выход врага из-под земли и уход обратно (стадия 6 «Мобы леса»). Поздние волны
    /// встречи встают из земли (SimEvent Spawn с Flag, Amount — тики бездействия),
    /// по концу выживания оставшиеся уходят в землю (SimEventType.Burrowed). Тело
    /// поднимает и опускает ArenaView на глубину <see cref="SinkDepth"/>; здесь —
    /// выброс земли на месте тела.
    ///
    /// ВЫБРОС СОБРАН ИЗ ПАКОВ, как прочие эффекты врагов: пятно разрытой земли
    /// (кратер Hovl на размытом облаке CFXR), комья — debris CFXR unlit на
    /// кувыркающихся в мире четырёхугольниках, каменная крошка камнекопыта и пыль
    /// CFXR, лежащая на земле. Экранных билбордов нет: слой либо лежит на земле,
    /// либо летит сеткой в мире. Материалы — готовые копии паков в Resources
    /// (набор Вендиго и камнекопыта); недостающий слой просто не строится.
    ///
    /// ЧАСЫ — ЧАСЫ СИМУЛЯЦИИ, как у StonehoofCombatView: пауза и хит-стоп держат и
    /// землю. Частицы с заданным зерном пересчитываются от начала выброса каждый
    /// кадр, поэтому пересчёт не мигает. Всё создаётся в Awake — в бою только пул.
    /// </summary>
    [DefaultExecutionOrder(650)]
    public sealed class EnemyEmergeView : MonoBehaviour
    {
        [Header("Глубина тела под землёй в начале выхода, м")]
        [Tooltip("Чуть больше роста: макушка не торчит из земли в первый кадр")]
        public float RootSwarmDepth = 1.2f;
        public float GuardianDepth = 2.5f;
        public float BudDepth = 1.8f;
        public float StonehoofDepth = 2.1f;
        public float WendigoDepth = 3.3f;
        // Новые мобы леса: по росту заглушек (ForestMobPlaceholderView); придут
        // модели — поправить под их рост. Детёныш из земли не встаёт, он из распада.
        public float ThorncasterDepth = 2.7f;
        public float RootSnarerDepth = 2.4f;
        public float SplitterDepth = 2f;
        public float SplitlingDepth = 1.2f;

        [Header("Выброс земли")]
        [Tooltip("Сколько выбросов готово заранее: волна до дюжины тел или уход в землю всех разом")]
        [Min(4)] public int PoolSize = 24;
        [Tooltip("Размер выброса на метр радиуса тела")]
        public float ScalePerRadius = 1.6f;
        [Tooltip("Сколько живёт выброс, с (пятно земли тает последним)")]
        public float BurstSeconds = 2.4f;

        // Тона земли — те же, что у набора Вендиго: игра яркая, земля тёплая, без чёрного.
        private static readonly Color SoilLight = new Color(.56f, .41f, .26f);
        private static readonly Color SoilDark = new Color(.30f, .20f, .12f);
        private static readonly Color DustLight = new Color(.84f, .72f, .55f, .34f);
        private static readonly Color DustDark = new Color(.68f, .56f, .41f, .34f);

        private const string WendigoMaterials = "VFX/Wendigo/Materials/";
        private const string StonehoofFolder = "Characters/Forest_Stonehoof/";

        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public float Tick = -1000f;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shown;
        private Burst[] _bursts;
        private int _cursor;
        private Mesh _quad, _cube;

        /// <summary>На сколько метров тело этого вида уходит под землю.</summary>
        public float SinkDepth(EnemyKind kind) => kind switch
        {
            EnemyKind.ForestRootSwarm => RootSwarmDepth,
            EnemyKind.ForestBud => BudDepth,
            EnemyKind.ForestStonehoof => StonehoofDepth,
            EnemyKind.ForestWendigo => WendigoDepth,
            EnemyKind.ForestThorncaster => ThorncasterDepth,
            EnemyKind.ForestRootSnarer => RootSnarerDepth,
            EnemyKind.ForestSplitter => SplitterDepth,
            EnemyKind.ForestSplitling => SplitlingDepth,
            _ => GuardianDepth,
        };

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            _quad = BuildQuad();
            // Встроенный куб — меш крошки, как у копыт камнекопыта; сам объект не нужен.
            var cubeProbe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeProbe.SetActive(false);
            _cube = cubeProbe.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cubeProbe);

            Material splat = Load(WendigoMaterials + "M_Wendigo_SoilSplat");
            Material clod = Load(WendigoMaterials + "M_Wendigo_Clod");
            Material dust = Load(WendigoMaterials + "M_Wendigo_Dust") ?? Load(StonehoofFolder + "Stonehoof_Dust");
            Material stone = Load(StonehoofFolder + "Stonehoof_Stone");

            _bursts = new Burst[Mathf.Max(4, PoolSize)];
            for (int i = 0; i < _bursts.Length; i++)
            {
                var root = new GameObject("Выход из земли " + i);
                root.transform.SetParent(transform, false);
                var systems = new System.Collections.Generic.List<ParticleSystem>(4);
                uint seed = 7919u * (uint)(i + 1);
                if (splat != null) systems.Add(Soil(root, splat, seed));
                if (clod != null) systems.Add(Clods(root, clod, seed + 1));
                if (stone != null) systems.Add(Chips(root, stone, seed + 2));
                if (dust != null) systems.Add(Dust(root, dust, seed + 3));
                root.SetActive(false);
                _bursts[i] = new Burst { Root = root, Particles = systems.ToArray() };
            }
        }

        private static Material Load(string path) => Resources.Load<Material>(path);

        private void LateUpdate()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || _bursts == null) return;
            if (_shown != sim) { _shown = sim; HideAll(); }
            float now = sim.Tick - 1 + _driver.Alpha;

            var events = _driver.FrameEvents;
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                bool emerge = e.Type == SimEventType.Spawn && e.Flag;
                if (!emerge && e.Type != SimEventType.Burrowed) continue;
                if ((uint)e.Target >= (uint)sim.Entities.Count) continue;
                // Событие тика T приходит с Tick = T + 1; часы отрисовки считают его тиком T.
                float at = i < contexts.Count ? contexts[i].SimulationTick - 1 : now;
                float scale = Mathf.Clamp(sim.Entities.BodyRadius[e.Target].ToFloat() * ScalePerRadius, .7f, 2.4f);
                Emit(new Vector3(e.Position.X.ToFloat(), 0f, e.Position.Y.ToFloat()), at, scale);
            }
            Advance(now);
        }

        private void Emit(Vector3 point, float tick, float scale)
        {
            Burst burst = _bursts[_cursor++ % _bursts.Length];
            point.y = (_layout != null ? _layout.WeaponGroundHeight(point.x, point.z) : 0f) + .025f;
            burst.Tick = tick;
            burst.Root.transform.SetPositionAndRotation(point, Quaternion.identity);
            burst.Root.transform.localScale = Vector3.one * scale;
            burst.Root.SetActive(true);
        }

        private void Advance(float tick)
        {
            foreach (Burst burst in _bursts)
            {
                if (!burst.Root.activeSelf) continue;
                float age = Mathf.Max(0f, tick - burst.Tick) / Simulation.TicksPerSecond;
                if (age > BurstSeconds) { burst.Root.SetActive(false); continue; }
                foreach (ParticleSystem system in burst.Particles)
                {
                    system.Simulate(age, false, true, false);
                    system.Pause(false);
                }
            }
        }

        private void HideAll()
        {
            foreach (Burst burst in _bursts) { burst.Tick = -1000f; burst.Root.SetActive(false); }
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
        }

        // ---------------------------------------------------------------- слои выброса

        /// <summary>Пятно разрытой земли: лежит на грунте, раскрывается за доли секунды и тает последним.</summary>
        private static ParticleSystem Soil(GameObject root, Material material, uint seed)
        {
            ParticleSystem system = Particles(root, "Пятно земли", 2, seed);
            var main = system.main;
            main.startLifetime = 2.3f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.7f, 2.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(.46f, .34f, .22f, .85f);
            var shape = system.shape; shape.enabled = false;
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .55f),
                new Keyframe(.06f, 1.04f), new Keyframe(.12f, 1f), new Keyframe(1f, 1f)));
            var color = system.colorOverLifetime; color.enabled = true;
            color.color = Fade(0f, .7f);
            SetupRenderer(system, material, ParticleSystemRenderMode.HorizontalBillboard, null).sortingFudge = 4f;
            return system;
        }

        /// <summary>Комья: кадры debris CFXR на четырёхугольниках, кувыркаются в мире и падают.</summary>
        private ParticleSystem Clods(GameObject root, Material material, uint seed)
        {
            ParticleSystem system = Particles(root, "Комья", 24, seed);
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 4.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.10f, .22f);
            main.gravityModifier = 1.6f;
            Tumble(main);
            main.startColor = new ParticleSystem.MinMaxGradient(SoilLight, SoilDark);
            Cone(system, 32f, .25f);
            // Второй толчок — когда тело проходит поверхность: земля выходит двумя вздохами.
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12), new ParticleSystem.Burst(.2f, 6) });
            Spin(system, 8f);
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.8f, 1f), new Keyframe(1f, 0f)));
            Sheet(system, 3, 8.99f);
            SetupRenderer(system, material, ParticleSystemRenderMode.Mesh, _quad);
            return system;
        }

        /// <summary>Каменная крошка — та же, что из-под копыт камнекопыта (освещённый материал).</summary>
        private ParticleSystem Chips(GameObject root, Material material, uint seed)
        {
            ParticleSystem system = Particles(root, "Крошка", 12, seed);
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .075f);
            main.gravityModifier = 1f;
            Tumble(main);
            Cone(system, 50f, .2f);
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            Spin(system, 5f);
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            SetupRenderer(system, material, ParticleSystemRenderMode.Mesh, _cube);
            return system;
        }

        /// <summary>Пыль: размытые облака CFXR лежат на земле и расходятся кольцом от тела.</summary>
        private static ParticleSystem Dust(GameObject root, Material material, uint seed)
        {
            ParticleSystem system = Particles(root, "Пыль", 12, seed);
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.9f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(.45f, .95f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(DustLight, DustDark);
            var shape = system.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .35f;
            // Круг формы лежит в её XY — поворот кладёт его на землю, разлёт — по радиусу.
            shape.rotation = new Vector3(90f, 0f, 0f);
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 7), new ParticleSystem.Burst(.25f, 4) });
            var drag = system.limitVelocityOverLifetime; drag.enabled = true;
            drag.limit = new ParticleSystem.MinMaxCurve(.25f);
            drag.dampen = .22f;
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .45f), new Keyframe(.25f, .9f), new Keyframe(1f, 1.45f)));
            var color = system.colorOverLifetime; color.enabled = true;
            color.color = Fade(.08f, .45f);
            Sheet(system, 2, 3.99f);
            SetupRenderer(system, material, ParticleSystemRenderMode.HorizontalBillboard, null).sortingFudge = 1f;
            return system;
        }

        // ---------------------------------------------------------------- общее

        /// <summary>Система без самозапуска, с постоянным зерном: пересчёт от нуля каждый кадр даёт ту же картину.</summary>
        private static ParticleSystem Particles(GameObject root, string name, int max, uint seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = seed;
            var main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            // Размер выброса — масштаб корня: форма, размер и разлёт растут вместе с телом.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            return system;
        }

        private static void Tumble(ParticleSystem.MainModule main)
        {
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        private static void Spin(ParticleSystem system, float speed)
        {
            var spin = system.rotationOverLifetime; spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-speed, speed);
            spin.y = new ParticleSystem.MinMaxCurve(-speed * .6f, speed * .6f);
            spin.z = new ParticleSystem.MinMaxCurve(-speed, speed);
        }

        /// <summary>Конус вверх из-под тела.</summary>
        private static void Cone(ParticleSystem system, float angle, float radius)
        {
            var shape = system.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>Атлас n×n: кадр наугад на частицу, без анимации.</summary>
        private static void Sheet(ParticleSystem system, int tiles, float lastFrame)
        {
            var sheet = system.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = tiles;
            sheet.numTilesY = tiles;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
            sheet.cycleCount = 1;
        }

        private static ParticleSystemRenderer SetupRenderer(ParticleSystem system, Material material,
            ParticleSystemRenderMode mode, Mesh mesh)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            if (mesh != null) renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>Прозрачность за жизнь: проявление до inEnd (0 — сразу), таяние с outFrom.</summary>
        private static Gradient Fade(float inEnd, float outFrom)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                inEnd > 0f
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd), new GradientAlphaKey(1f, outFrom), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, outFrom), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>Двусторонний (шейдер CFXR без отсечения) квадрат 1×1 в плоскости XY с полной UV.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "Выход из земли: комок" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(-.5f, .5f, 0f), new Vector3(.5f, .5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
