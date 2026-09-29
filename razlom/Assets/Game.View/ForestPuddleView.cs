using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// КИСЛЫЕ ЛУЖИ ПЛЮЙ-ПЛОДА со стороны картинки — «живая лужа» V6 по кадру
    /// владельца 10-bud-puddle-alive-b-vapour-crust (выбор G10, 29.09; V5
    /// «как наклейка» отклонена).
    ///
    /// Лужа рождается из события Sim PuddleOpened (после смены симуляции или
    /// включения вида — сверкой с TryGetForestPuddle): шлепок плода — всплеск
    /// пака CFXR, капли и осколки гнилой кожуры; кислота растекается за
    /// 9 тиков созревания; трава вокруг темнеет и вянет; пузыри, рябь и
    /// тяжёлый зелёный пар всю жизнь; у кромки нарастает сухая корка. С концом
    /// кислоты (EndTick) лужа за 9 тиков угасания уходит к центру и оставляет
    /// корку, которая бледнеет ещё 1,4 с — уже после ухода лужи из Sim.
    /// Вытесненная новой лужа (PuddleClosed с Flag) сохнет за 0,2 с и гаснет
    /// за 0,5 с. Каждый тик кислоты (раз в 15 после созревания) — выдох пара
    /// VFX_Puddle_Pulse и светлое кольцо по поверхности: ритм урона виден.
    ///
    /// Возраст — от тика Sim с долей кадра. Системы частиц догоняются
    /// Simulate (верхние — вместе с детьми-субэмиттерами пака), а постоянные
    /// эмиттеры замолкают ровно на тике высыхания: пауза держит кадр,
    /// перемотка даёт тот же. Префабы собирает ForestPuddleVfxSetup.
    /// Ставится на объект арены вместе с видами Плюй-плода (ArenaView.PrepareForestBud).
    /// </summary>
    [DefaultExecutionOrder(655)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ForestPuddleView : MonoBehaviour
    {
        public const string AcidPrefab = "VFX/ForestPuddle/VFX_Puddle_Acid";
        public const string PulsePrefab = "VFX/ForestPuddle/VFX_Puddle_Pulse";

        /// <summary>Экземпляров луж: четыре слота Sim и два высыхающих следа.</summary>
        private const int AcidPool = Simulation.ForestPuddleCapacity + 2, PulsePool = 6;
        /// <summary>Вытесненная лужа сохнет за 6 тиков, её корка гаснет ещё за 15.</summary>
        private const float EvictDryTicks = 6f, EvictCrustTicks = 15f;
        /// <summary>Сколько тиков корка лежит после ухода лужи из Sim.</summary>
        private const float CrustTicks = 42f;
        /// <summary>Предохранитель: дольше этого экземпляр лужи не живёт, что бы ни случилось.</summary>
        private const float MaxAcidSeconds = 30f;
        private const float SimulateStep = 1f / 30f, PulseLife = 1.3f;

        private sealed class Fx
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            /// <summary>Система без родителя-системы: её Simulate ведёт и детей.</summary>
            public bool[] Top;
            /// <summary>Постоянный эмиттер (пар, пена): замолкает на тике высыхания.</summary>
            public bool[] Looping;
            public uint[] Seeds;
            public ForestPuddleSurface Surface;
            public float Tick = -1000f, Simulated = -1f, Life;
            public int Serial, Slot = -1, LastPulse;
            public bool Evicted;
            public float DryTick = float.MaxValue, GoneTick = float.MaxValue, FadeEndTick = float.MaxValue;
            public float StopAge = float.MaxValue;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shown;
        private int _generation = -1;
        private bool _reconcile = true;
        private Fx[] _puddles, _pulses;
        private int _pulseCursor;

        public static ForestPuddleView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<ForestPuddleView>();
            return view != null ? view : host.AddComponent<ForestPuddleView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            // Лужи и выдохи прогреты заранее — в бою ни одного Instantiate.
            _puddles = Make(AcidPrefab, "Плюй-плод: кислая лужа", AcidPool, MaxAcidSeconds);
            _pulses = Make(PulsePrefab, "Плюй-плод: тик кислоты", PulsePool, PulseLife);
            if (_puddles.Length == 0)
                Debug.LogWarning("[Разлом] Плюй-плод: нет префаба кислой лужи — собери «Разлом/Плюй-плод/VFX луж: пересобрать».", this);
        }

        private Fx[] Make(string path, string title, int count, float life)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return new Fx[0];
            var items = new Fx[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = title;
                var fx = new Fx { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true),
                    Surface = go.GetComponent<ForestPuddleSurface>(), Life = life };
                int systems = fx.Particles.Length;
                fx.Seeds = new uint[systems];
                fx.Top = new bool[systems];
                fx.Looping = new bool[systems];
                for (int k = 0; k < systems; k++)
                {
                    var ps = fx.Particles[k];
                    fx.Top[k] = IsTop(ps.transform, go.transform);
                    var emission = ps.emission;
                    var rate = emission.rateOverTime;
                    fx.Looping[k] = emission.enabled && (rate.mode != ParticleSystemCurveMode.Constant || rate.constant > 0f);
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    fx.Seeds[k] = ps.randomSeed;
                }
                go.SetActive(false);
                items[i] = fx;
            }
            return items;
        }

        private static bool IsTop(Transform system, Transform root)
        {
            for (var parent = system.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<ParticleSystem>() != null) return false;
                if (parent == root) break;
            }
            return true;
        }

        private void OnEnable() => _reconcile = true;

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation)
            {
                foreach (var fx in _puddles) Retire(fx);
                foreach (var fx in _pulses) Retire(fx);
                _shown = sim; _generation = _driver.Generation; _reconcile = true;
            }
            if (sim == null || _puddles.Length == 0) return;
            float tick = sim.Tick - 1 + _driver.Alpha;

            // События кадра: легла лужа — экземпляр с её тика; ушла — слот свободен,
            // вытесненная (Flag) сохнет быстро.
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.Type == SimEventType.PuddleOpened) Spawn(sim, e.Amount, e.ActionVariant);
                else if (e.Type == SimEventType.PuddleClosed)
                {
                    var fx = Find(e.ActionVariant);
                    if (fx == null) continue;
                    fx.Slot = -1;
                    if (e.Flag) Evict(fx, contexts[i].SimulationTick - 1);
                }
            }
            if (_reconcile)
            {
                // Новая симуляция или включение вида: живые лужи — из состояния Sim.
                for (int slot = 0; slot < Simulation.ForestPuddleCapacity; slot++)
                    if (sim.TryGetForestPuddle(slot, out var puddle)) Spawn(sim, slot, puddle.Serial);
                _reconcile = false;
            }

            foreach (var fx in _puddles)
            {
                if (!fx.Root.activeSelf) continue;
                if (fx.Slot >= 0)
                {
                    if (sim.TryGetForestPuddle(fx.Slot, out var puddle) && puddle.Serial == fx.Serial)
                    {
                        // Песочные Часы сдвигают конец кислоты — сроки берутся из Sim.
                        if (!fx.Evicted) Schedule(fx, puddle.EndTick);
                        Pulse(fx, puddle, tick);
                    }
                    else
                    {
                        // Лужа пропала без события (сброс стенда) — сохнет как вытесненная.
                        fx.Slot = -1;
                        if (tick < fx.DryTick) Evict(fx, tick);
                    }
                }
                Advance(fx, tick);
            }
            foreach (var fx in _pulses) Advance(fx, tick);
        }

        private Fx Find(int serial)
        {
            if (serial == 0) return null;
            foreach (var fx in _puddles) if (fx.Root.activeSelf && fx.Serial == serial) return fx;
            return null;
        }

        private void Spawn(Simulation sim, int slot, int serial)
        {
            if (serial == 0 || Find(serial) != null) return;
            if (!sim.TryGetForestPuddle(slot, out var puddle) || puddle.Serial != serial) return;
            var fx = Take();
            Place(fx, puddle.StartTick, Ground(puddle.Center), serial, puddle.Radius.ToFloat());
            fx.Slot = slot;
            fx.LastPulse = puddle.ArmTick;
            Schedule(fx, puddle.EndTick);
        }

        /// <summary>Свободный экземпляр; иначе — высыхающий след, который погаснет раньше всех.</summary>
        private Fx Take()
        {
            Fx best = null;
            foreach (var fx in _puddles)
            {
                if (!fx.Root.activeSelf) return fx;
                if (fx.Slot >= 0) continue;
                if (best == null || fx.FadeEndTick < best.FadeEndTick) best = fx;
            }
            if (best != null) return best;
            best = _puddles[0];
            foreach (var fx in _puddles) if (fx.Tick < best.Tick) best = fx;
            return best;
        }

        /// <summary>Кислота кончается на endTick: дальше 9 тиков сохнет, потом корка гаснет.</summary>
        private static void Schedule(Fx fx, int endTick)
        {
            fx.DryTick = endTick;
            fx.GoneTick = endTick + Simulation.PuddleFadeTicks;
            fx.FadeEndTick = fx.GoneTick + CrustTicks;
            SetStop(fx, (endTick - fx.Tick) / Simulation.TicksPerSecond);
        }

        private static void Evict(Fx fx, float tick)
        {
            if (fx.Evicted) return;
            fx.Evicted = true;
            if (tick < fx.DryTick)
            {
                fx.DryTick = tick;
                SetStop(fx, (tick - fx.Tick) / Simulation.TicksPerSecond);
            }
            fx.GoneTick = Mathf.Min(fx.GoneTick, tick + EvictDryTicks);
            fx.FadeEndTick = Mathf.Min(fx.FadeEndTick, fx.GoneTick + EvictCrustTicks);
        }

        /// <summary>
        /// Возраст, с которого постоянные эмиттеры молчат. Сдвиг раньше уже
        /// прожитого — полный перепрогон с начала: выключение эмиссии ложится
        /// ровно на тот же шаг, что и при перемотке.
        /// </summary>
        private static void SetStop(Fx fx, float age)
        {
            if (Mathf.Abs(age - fx.StopAge) < 1e-5f) return;
            if (fx.Simulated >= 0f && Mathf.Min(age, fx.StopAge) < fx.Simulated) fx.Simulated = -1f;
            fx.StopAge = age;
        }

        /// <summary>Тики кислоты — по расписанию Sim: ArmTick + 15k, пока лужа жжёт.</summary>
        private void Pulse(Fx fx, ForestPuddleState puddle, float tick)
        {
            if (_pulses.Length == 0 || fx.Evicted) return;
            int next = fx.LastPulse + Simulation.PuddlePulseTicks;
            while (next <= tick && next < puddle.EndTick)
            {
                fx.LastPulse = next;
                // После сверки старые тики не догоняются пачкой — только свежий.
                if (tick - next < Simulation.PuddlePulseTicks)
                    Place(_pulses[_pulseCursor++ % _pulses.Length], next, fx.Root.transform.position, fx.Serial * 31 + next);
                next += Simulation.PuddlePulseTicks;
            }
        }

        private void Place(Fx fx, float tick, Vector3 at, int seed, float radius = 0f)
        {
            fx.Tick = tick; fx.Simulated = -1f; fx.Serial = seed; fx.Slot = -1; fx.Evicted = false;
            fx.DryTick = fx.GoneTick = fx.FadeEndTick = fx.StopAge = float.MaxValue;
            fx.Root.transform.SetPositionAndRotation(at, Quaternion.identity);
            fx.Root.transform.localScale = Vector3.one;
            fx.Root.SetActive(true);
            if (fx.Surface != null) fx.Surface.Place(_layout, seed, radius > 0f ? radius : Simulation.PuddleRadius.ToFloat());
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                var ps = fx.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = fx.Seeds[k] + (uint)seed * 7919u;
            }
        }

        private static void Retire(Fx fx)
        {
            if (fx == null) return;
            fx.Tick = -1000f; fx.Simulated = -1f; fx.Serial = 0; fx.Slot = -1; fx.Evicted = false;
            fx.DryTick = fx.GoneTick = fx.FadeEndTick = fx.StopAge = float.MaxValue;
            if (fx.Root.activeSelf) fx.Root.SetActive(false);
        }

        private static void SetEmission(ParticleSystem ps, bool enabled)
        {
            var emission = ps.emission;
            if (emission.enabled != enabled) emission.enabled = enabled;
        }

        /// <summary>Возраст — от тика Sim; вперёд догоняется шагами, назад — перепрогоном с начала.</summary>
        private static void Advance(Fx fx, float tick)
        {
            if (fx == null || !fx.Root.activeSelf) return;
            float age = (tick - fx.Tick) / Simulation.TicksPerSecond;
            if (age > fx.Life || tick >= fx.FadeEndTick) { Retire(fx); return; }
            age = Mathf.Max(0f, age);
            if (fx.Surface != null)
            {
                float dry = fx.DryTick == float.MaxValue ? 0f
                    : Mathf.Clamp01((tick - fx.DryTick) / Mathf.Max(1f, fx.GoneTick - fx.DryTick));
                float fade = fx.GoneTick == float.MaxValue ? 1f
                    : 1f - Mathf.Clamp01((tick - fx.GoneTick) / Mathf.Max(1f, fx.FadeEndTick - fx.GoneTick));
                fx.Surface.Sample(age, dry, fade);
            }
            if (fx.Simulated >= 0f && Mathf.Abs(age - fx.Simulated) < 1e-5f) return;
            bool restart = fx.Simulated < 0f || age < fx.Simulated;
            float from = restart ? 0f : fx.Simulated;
            for (int k = 0; k < fx.Particles.Length; k++)
            {
                if (!fx.Top[k]) continue;
                var ps = fx.Particles[k];
                bool looping = fx.Looping[k];
                if (looping) SetEmission(ps, restart || from < fx.StopAge - 1e-5f);
                float done = from;
                bool first = restart;
                while (true)
                {
                    float next = Mathf.Min(done + SimulateStep, age);
                    // Шаг обрывается ровно на тике высыхания — дальше эмиттер молчит.
                    if (looping && done < fx.StopAge && next > fx.StopAge) next = fx.StopAge;
                    ps.Simulate(next - done, true, first, false);
                    first = false;
                    done = next;
                    if (looping && done >= fx.StopAge - 1e-5f) SetEmission(ps, false);
                    if (done >= age - 1e-5f) break;
                }
                ps.Pause(true);
            }
            fx.Simulated = age;
        }

        private Vector3 Ground(FixVec2 at)
        {
            var p = new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat());
            p.y = _layout != null ? _layout.WeaponGroundHeight(p.x, p.z) : 0f;
            return p;
        }

        private void OnDisable()
        {
            if (_puddles != null) foreach (var fx in _puddles) Retire(fx);
            if (_pulses != null) foreach (var fx in _pulses) Retire(fx);
        }

        private void OnDestroy()
        {
            if (_puddles != null) foreach (var fx in _puddles) if (fx.Root != null) Destroy(fx.Root);
            if (_pulses != null) foreach (var fx in _pulses) if (fx.Root != null) Destroy(fx.Root);
        }
    }
}
