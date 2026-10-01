using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// МЯГКИЙ ЗЕЛЁНЫЙ ОТКЛИК НА ТЕЛЕ ВЫЛЕЧЕННОГО (ревью 01.10, Корнехват: «когда он хилит
    /// союзников, нет эффекта»). Полоска и «+N» — HealthBars и DamageNumbers; здесь — само тело.
    ///
    /// Не высветление: материалы тела не трогаются (кромка и подъём яркости героя отвергнуты
    /// владельцем 24.09). Поверх тела — частицы из материалов «Волны из корней» Корнехвата
    /// (unlit CFXR, проверены в URP на его волне лечения):
    ///  • ореол — мягкое зелёное свечение ЗА телом: билборд отодвинут от камеры за спину,
    ///    поэтому тело его закрывает, а по силуэту видна светлая зелёная кайма;
    ///  • кольца — золотисто-зелёное кольцо от ног поднимается выше макушки, за ним второе тоньше;
    ///  • искры — горсть звёздочек всплывает вокруг тела.
    /// Длится ~1 с, размер — по телу этого кадра (рост и ширина рендереров), от корнеполза до
    /// Вендиго одинаково читается.
    ///
    /// Всё — от события Heal с тиком события; возраст — от тика Sim с долей кадра: пауза держит
    /// кадр, перемотка повторяет его. Системы частиц собираются кодом до боя из
    /// Resources/VFX/RootSnarer/Materials — префаба нет, пересборки в редакторе не нужно. Нет
    /// материалов — отклика на теле нет (предупреждение в лог), полоска и цифра остаются.
    ///
    /// Ставится на объект арены из HealthBars (EnsureOn).
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(660)]
    public sealed class EnemyHealPulseView : MonoBehaviour
    {
        private const string MaterialFolder = "VFX/RootSnarer/Materials/";
        private const string GlowMaterial = MaterialFolder + "M_RootSnarer_MendGlow";
        private const string RingMaterial = MaterialFolder + "M_RootSnarer_MendRing";
        private const string SparkMaterial = MaterialFolder + "M_RootSnarer_MendSpark";

        /// <summary>Сколько тел светится разом: волна лечит пачку, обычно 2–5 союзников.</summary>
        private const int PoolSize = 8;

        /// <summary>Сколько живёт отклик, с.</summary>
        public const float LifeSeconds = 1.15f;

        /// <summary>Кольцо поднимается от ног выше макушки за столько, с.</summary>
        private const float RingRiseSeconds = .6f;

        private const float SimulateStep = 1f / 30f;

        // Палитра «Волны из корней» (RootSnarerVfxSetup: MendGold, MendGreen).
        private static readonly Color MendGold = new Color(.93f, .82f, .40f), MendGreen = new Color(.56f, .78f, .30f);

        // Горизонтальный билборд CFXR рисуется на ≈0,72 размера частицы (память: ловушки частиц) —
        // кольцо по ширине тела ставится с запасом √2.
        private const float BillboardFill = 1.41f;

        private sealed class Pulse
        {
            public GameObject Root;
            public Transform Glow;
            public ParticleSystem GlowSystem, Ring, RingInner, Sparks;
            public ParticleSystem[] Systems;
            public uint[] Seeds;
            public float[] Done;
            public int Entity = -1, Tick = -1000;
            public float Height = 2f, Radius = .6f;
        }

        public static EnemyHealPulseView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<EnemyHealPulseView>();
            return view != null ? view : host.AddComponent<EnemyHealPulseView>();
        }

        private TickDriver _driver;
        private ArenaView _arena;
        private LayoutView _layout;
        private Camera _camera;
        private Simulation _shown;
        private int _generation = -1;
        private Pulse[] _pool = new Pulse[0];
        private int _cursor;
        private readonly List<Renderer> _renderers = new List<Renderer>(16);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            _layout = GetComponent<LayoutView>();
            var glow = Resources.Load<Material>(GlowMaterial);
            var ring = Resources.Load<Material>(RingMaterial);
            var spark = Resources.Load<Material>(SparkMaterial);
            if (glow == null || ring == null || spark == null)
            {
                Debug.LogWarning("[Разлом] Отклик лечения на теле: нет материалов «" + MaterialFolder + "» — собери «Разлом/Корнехват/VFX: пересобрать». Полоска и «+N» работают.", this);
                enabled = false;
                return;
            }
            var root = new GameObject("Пул: лечение на теле").transform;
            root.SetParent(transform, false);
            _pool = new Pulse[PoolSize];
            for (int i = 0; i < PoolSize; i++) _pool[i] = Build(root, glow, ring, spark);
        }

        // ------------------------------------------------------------- frame

        private void LateUpdate()
        {
            var sim = _driver != null ? _driver.Sim : null;
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation)
            {
                RetireAll();
                _shown = sim;
                _generation = _driver.Generation;
            }
            if (sim == null) return;
            if (_camera == null) _camera = Camera.main;

            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.Type != SimEventType.Heal || e.Amount <= 0) continue;
                // Событие тика T приходит с SimulationTick = T + 1.
                Begin(sim, e.Target, contexts[i].SimulationTick - 1);
            }

            float tick = sim.Tick - 1 + _driver.Alpha;
            for (int i = 0; i < _pool.Length; i++) Advance(sim, _pool[i], tick);
        }

        private void Begin(Simulation sim, int entity, int tick)
        {
            if (CaptureRig.NoVfx || _pool.Length == 0) return;
            if ((uint)entity >= (uint)sim.Entities.Count || entity == Simulation.PlayerId || !sim.Entities.Alive[entity]) return;
            // Тот же союзник ещё светится (лечение подряд) — отклик начинается заново, второго не надо.
            Pulse p = null;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i].Entity == entity && _pool[i].Root.activeSelf) { p = _pool[i]; break; }
            if (p == null) p = _pool[_cursor++ % _pool.Length];

            Vector3 ground = Ground(entity);
            Measure(entity, ground, out p.Height, out p.Radius);
            p.Entity = entity;
            p.Tick = tick;
            Shape(p);
            for (int k = 0; k < p.Systems.Length; k++)
            {
                var ps = p.Systems[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = p.Seeds[k] + (uint)(entity * 7919 + tick * 104729);
                p.Done[k] = -1f;
            }
            Place(p, ground);
            p.Root.SetActive(true);
        }

        /// <summary>Возраст — от тика Sim: вперёд системы догоняются шагами по 1/30 с, назад — перезапуском.</summary>
        private void Advance(Simulation sim, Pulse p, float tick)
        {
            if (!p.Root.activeSelf) return;
            float age = (tick - p.Tick) / Simulation.TicksPerSecond;
            bool alive = (uint)p.Entity < (uint)sim.Entities.Count && sim.Entities.Alive[p.Entity];
            if (age > LifeSeconds || !alive) { Retire(p); return; }
            Place(p, Ground(p.Entity));
            age = Mathf.Max(0f, age);
            for (int k = 0; k < p.Systems.Length; k++)
            {
                var ps = p.Systems[k];
                if (p.Done[k] >= 0f && Mathf.Abs(age - p.Done[k]) < 1e-5f) continue;
                bool restart = p.Done[k] < 0f || age < p.Done[k];
                float done = restart ? 0f : p.Done[k];
                bool first = restart;
                do
                {
                    float step = Mathf.Min(SimulateStep, age - done);
                    ps.Simulate(Mathf.Max(0f, step), false, first, false);
                    first = false;
                    done += step;
                } while (done < age - 1e-5f);
                ps.Pause(false);
                p.Done[k] = age;
            }
        }

        /// <summary>
        /// Корень — у ног союзника; ореол — на середине роста, за телом по взгляду камеры (только
        /// по горизонтали: вниз по взгляду он ушёл бы под землю).
        /// </summary>
        private void Place(Pulse p, Vector3 ground)
        {
            p.Root.transform.position = ground;
            Vector3 back = _camera != null ? _camera.transform.forward : Vector3.forward;
            back.y = 0f;
            back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.forward;
            p.Glow.position = ground + Vector3.up * (p.Height * .5f) + back * (p.Radius * .9f);
        }

        private void RetireAll()
        {
            for (int i = 0; i < _pool.Length; i++) Retire(_pool[i]);
        }

        private static void Retire(Pulse p)
        {
            if (p == null) return;
            p.Entity = -1;
            p.Tick = -1000;
            if (p.Root != null && p.Root.activeSelf) p.Root.SetActive(false);
        }

        // ----------------------------------------------------------- measure

        /// <summary>
        /// Рост и полуширина тела этого кадра по рендерерам (сетки и кожа, без тени, подписей,
        /// частиц). Тела нет — средний моб: 2 м и 0,6 м.
        /// </summary>
        private void Measure(int entity, Vector3 ground, out float height, out float radius)
        {
            height = 2f;
            radius = .6f;
            if (_arena == null || !_arena.TryGetEntityView(entity, out Transform view) || view == null) return;
            view.GetComponentsInChildren(false, _renderers);
            bool any = false;
            Bounds all = default;
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if (!r.enabled || r.gameObject.name == "Contact Shadow" || ForestMobPlaceholderView.IsLabel(r)) continue;
                if (r is MeshRenderer && r.GetComponent<TMPro.TMP_Text>() != null) continue;
                if (!any) { all = r.bounds; any = true; }
                else all.Encapsulate(r.bounds);
            }
            _renderers.Clear();
            if (!any) return;
            height = Mathf.Clamp(all.max.y - ground.y, .6f, 4.5f);
            radius = Mathf.Clamp(Mathf.Max(all.extents.x, all.extents.z) * .75f, .3f, 1.4f);
        }

        private Vector3 Ground(int entity)
        {
            Vector3 p;
            if (_arena != null && _arena.TryGetEntityView(entity, out Transform view) && view != null) p = view.position;
            else p = _driver.GetRenderPosition(entity);
            p.y = _layout != null ? _layout.WeaponGroundHeight(p.x, p.z) : 0f;
            return p;
        }

        // ------------------------------------------------------------- build

        /// <summary>Размеры по телу: ореол — по росту, кольца — по ширине, подъём колец — до макушки.</summary>
        private static void Shape(Pulse p)
        {
            float h = p.Height, r = p.Radius;
            var glow = p.GlowSystem.main;
            glow.startSize = h * 1.1f;

            float ring = 2f * r * 1.25f * BillboardFill;
            var main = p.Ring.main; main.startSize = ring;
            var inner = p.RingInner.main; inner.startSize = ring * .8f;
            float rise = h * 1.1f / RingRiseSeconds;
            var up = p.Ring.velocityOverLifetime; up.y = new ParticleSystem.MinMaxCurve(rise);
            var upInner = p.RingInner.velocityOverLifetime; upInner.y = new ParticleSystem.MinMaxCurve(rise * .9f);

            var shape = p.Sparks.shape; shape.radius = r * .85f;
            var sparks = p.Sparks.main;
            float k = Mathf.Clamp(h / 2f, .6f, 1.6f);
            sparks.startSize = new ParticleSystem.MinMaxCurve(.07f * k, .13f * k);
            sparks.startSpeed = new ParticleSystem.MinMaxCurve(.5f * k, 1.1f * k);
        }

        private Pulse Build(Transform parent, Material glow, Material ring, Material spark)
        {
            var root = new GameObject("Лечение: отклик на теле");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            var p = new Pulse { Root = root };

            // Ореол за телом: одна мягкая частица, вспухает и тает.
            p.GlowSystem = NewSystem(root, "Ореол", glow, 1, .8f, .8f, ParticleSystemRenderMode.Billboard);
            p.Glow = p.GlowSystem.transform;
            var gm = p.GlowSystem.main;
            gm.startColor = new Color(MendGreen.r, MendGreen.g, MendGreen.b, .45f);
            var gs = p.GlowSystem.sizeOverLifetime; gs.enabled = true;
            gs.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .18f, 1.05f, 1f, .95f));
            var gc = p.GlowSystem.colorOverLifetime; gc.enabled = true;
            gc.color = Fade(.12f, .45f);

            // Кольца: горизонтальные, от ног вверх выше макушки, светлеют к середине пути.
            p.Ring = NewSystem(root, "Кольцо", ring, 1, RingRiseSeconds, RingRiseSeconds, ParticleSystemRenderMode.HorizontalBillboard);
            p.Ring.transform.localPosition = Vector3.up * .05f;
            Rise(p.Ring, new Color((MendGold.r + MendGreen.r) * .5f, (MendGold.g + MendGreen.g) * .5f, (MendGold.b + MendGreen.b) * .5f, .85f), 0f);
            p.RingInner = NewSystem(root, "Кольцо 2", ring, 1, RingRiseSeconds, RingRiseSeconds, ParticleSystemRenderMode.HorizontalBillboard);
            p.RingInner.transform.localPosition = Vector3.up * .05f;
            Rise(p.RingInner, new Color(MendGreen.r, MendGreen.g, MendGreen.b, .6f), .14f);

            // Искры: звёздочки с кромки круга у ног всплывают вдоль тела и гаснут (конус +Z — вверх).
            p.Sparks = NewSystem(root, "Искры", spark, 10, .7f, 1.05f, ParticleSystemRenderMode.Billboard);
            p.Sparks.transform.localPosition = Vector3.up * .15f;
            p.Sparks.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            var sm = p.Sparks.main;
            sm.gravityModifier = -.08f;
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(MendGold.r, MendGold.g, MendGold.b, .9f),
                new Color(MendGreen.r, MendGreen.g, MendGreen.b, .8f));
            var emission = p.Sparks.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6), new ParticleSystem.Burst(.14f, 4) });
            var shape = p.Sparks.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = .5f;
            shape.radiusThickness = .3f;
            var drag = p.Sparks.limitVelocityOverLifetime; drag.enabled = true;
            drag.limit = new ParticleSystem.MinMaxCurve(.9f);
            drag.dampen = .12f;
            var ss = p.Sparks.sizeOverLifetime; ss.enabled = true;
            ss.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .4f, .2f, 1f, 1f, .2f));
            var sc = p.Sparks.colorOverLifetime; sc.enabled = true;
            sc.color = Fade(.08f, .5f);

            p.Systems = new[] { p.GlowSystem, p.Ring, p.RingInner, p.Sparks };
            p.Seeds = new uint[p.Systems.Length];
            p.Done = new float[p.Systems.Length];
            for (int k = 0; k < p.Systems.Length; k++)
            {
                p.Systems[k].randomSeed = (uint)(1009 * (k + 1));
                p.Seeds[k] = p.Systems[k].randomSeed;
                p.Done[k] = -1f;
            }
            return p;
        }

        /// <summary>Кольцо, которое поднимается вдоль тела: скорость вверх ставит Shape по росту.</summary>
        private static void Rise(ParticleSystem ps, Color color, float delay)
        {
            var main = ps.main;
            main.startColor = color;
            main.startRotation = 0f;
            main.startDelay = delay;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .85f, .3f, 1f, 1f, .8f));
            var fade = ps.colorOverLifetime; fade.enabled = true;
            fade.color = Fade(.1f, .55f);
        }

        /// <summary>Система частиц под корнем отклика: одна вспышка, своё пространство, без теней и света.</summary>
        private static ParticleSystem NewSystem(GameObject root, string name, Material material, int count,
            float lifeMin, float lifeMax, ParticleSystemRenderMode mode)
        {
            var host = new GameObject(name);
            host.transform.SetParent(root.transform, false);
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(.2f, lifeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(1, count);
            ps.useAutoRandomSeed = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape; shape.enabled = false;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingFudge = mode == ParticleSystemRenderMode.HorizontalBillboard ? 3f : 0f;
            return ps;
        }

        /// <summary>Прозрачность: из нуля за fadeIn, держится, гаснет с fadeOut до конца жизни.</summary>
        private static ParticleSystem.MinMaxGradient Fade(float fadeIn, float fadeOut)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static AnimationCurve Curve(params float[] keys)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i + 1 < keys.Length; i += 2) curve.AddKey(keys[i], keys[i + 1]);
            return curve;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && _pool[i].Root != null) Destroy(_pool[i].Root);
        }
    }
}
