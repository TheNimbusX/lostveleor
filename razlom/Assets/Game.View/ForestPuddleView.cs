using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// КИСЛЫЕ ЛУЖИ ПЛЮЙ-ПЛОДА со стороны картинки (кадр 2-acid-puddle-flat от 27.09).
    ///
    /// Лужа — по состоянию Sim (Simulation.TryGetForestPuddle): пока слот занят,
    /// на месте падения гнилого плода лежит VFX_Puddle_Acid — брызги и ошмётки,
    /// растекание за 9 тиков созревания, пузыри и дымка, кромка ровно по радиусу
    /// урона. Каждый тик кислоты (раз в 15 после созревания) по луже пробегает
    /// волна VFX_Puddle_Pulse — ритм урона виден. Вытесненная новой лужа
    /// (PuddleClosed с Flag) сохнет за 0,2 с, а не обрывается.
    ///
    /// Возраст — от тика Sim с долей кадра, системы догоняются Simulate: пауза
    /// держит кадр, перемотка даёт тот же. Префабы собирает ForestPuddleVfxSetup.
    /// Ставится на объект арены вместе с видами Плюй-плода (ArenaView.PrepareForestBud).
    /// </summary>
    [DefaultExecutionOrder(655)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ForestPuddleView : MonoBehaviour
    {
        public const string AcidPrefab = "VFX/ForestPuddle/VFX_Puddle_Acid";
        public const string PulsePrefab = "VFX/ForestPuddle/VFX_Puddle_Pulse";

        /// <summary>За сколько секунд сохнет лужа, вытесненная новой.</summary>
        private const float EvictSeconds = .2f;
        private const float SimulateStep = 1f / 30f, PulseLife = .45f;

        private sealed class Fx
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public ForestPuddleSurface Surface;
            public uint[] Seeds;
            public float Tick = -1000f, Simulated = -1f, Life;
            public int Serial;
            public float ShrinkFrom = float.MaxValue;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shown;
        private int _generation = -1;
        private Fx[] _puddles, _pulses;
        private int _pulseCursor;
        private readonly int[] _lastPulse = new int[Simulation.ForestPuddleCapacity];

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
            // Одна лужа на слот Sim и шесть волн на смену — в бою ни одного Instantiate.
            _puddles = Make(AcidPrefab, "Плюй-плод: кислая лужа", Simulation.ForestPuddleCapacity,
                (Simulation.PuddleArmTicks + Simulation.PuddleLifeTicks + Simulation.PuddleFadeTicks) / (float)Simulation.TicksPerSecond);
            _pulses = Make(PulsePrefab, "Плюй-плод: тик кислоты", 6, PulseLife);
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
                fx.Seeds = new uint[fx.Particles.Length];
                for (int k = 0; k < fx.Particles.Length; k++)
                {
                    var ps = fx.Particles[k];
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    fx.Seeds[k] = ps.randomSeed;
                }
                go.SetActive(false);
                items[i] = fx;
            }
            return items;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation)
            {
                foreach (var fx in _puddles) Retire(fx);
                foreach (var fx in _pulses) Retire(fx);
                System.Array.Clear(_lastPulse, 0, _lastPulse.Length);
                _shown = sim; _generation = _driver.Generation;
            }
            if (sim == null || _puddles.Length == 0) return;
            float tick = sim.Tick - 1 + _driver.Alpha;

            // Вытеснение: лужа сохнет быстро, а не обрывается.
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.Type != SimEventType.PuddleClosed || !e.Flag || (uint)e.Amount >= (uint)_puddles.Length) continue;
                var fx = _puddles[e.Amount];
                if (fx.Root.activeSelf && fx.Serial == e.ActionVariant) fx.ShrinkFrom = contexts[i].SimulationTick - 1;
            }

            for (int slot = 0; slot < _puddles.Length; slot++)
            {
                var fx = _puddles[slot];
                bool live = sim.TryGetForestPuddle(slot, out var puddle);
                if (live && fx.Serial != puddle.Serial)
                {
                    // Слот занят новой лужей (старую вытеснили) — новый экземпляр с её тика.
                    Place(fx, puddle.StartTick, Ground(puddle.Center), puddle.Serial, puddle.Radius.ToFloat());
                    _lastPulse[slot] = puddle.ArmTick;
                }
                if (fx.Root.activeSelf && fx.ShrinkFrom == float.MaxValue && !live) fx.ShrinkFrom = tick;
                Advance(fx, tick);
                if (!live || _pulses.Length == 0) continue;
                // Тики кислоты — по расписанию Sim: ArmTick + 15k.
                int due = puddle.ArmTick + Simulation.PuddlePulseTicks;
                while (_lastPulse[slot] + Simulation.PuddlePulseTicks <= tick && _lastPulse[slot] + Simulation.PuddlePulseTicks < puddle.EndTick)
                {
                    _lastPulse[slot] += Simulation.PuddlePulseTicks;
                    if (_lastPulse[slot] < due) continue;
                    var pulse = _pulses[_pulseCursor++ % _pulses.Length];
                    Place(pulse, _lastPulse[slot], Ground(puddle.Center), puddle.Serial * 31 + _lastPulse[slot]);
                }
            }
            foreach (var fx in _pulses) Advance(fx, tick);
        }

        private void Place(Fx fx, float tick, Vector3 at, int seed, float radius = 0f)
        {
            fx.Tick = tick; fx.Simulated = -1f; fx.Serial = seed; fx.ShrinkFrom = float.MaxValue;
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
            fx.Tick = -1000f; fx.Simulated = -1f; fx.Serial = 0; fx.ShrinkFrom = float.MaxValue;
            if (fx.Root.activeSelf) fx.Root.SetActive(false);
        }

        /// <summary>Возраст — от тика Sim; вперёд догоняется приращениями, назад — перезапуском.</summary>
        private static void Advance(Fx fx, float tick)
        {
            if (fx == null || !fx.Root.activeSelf) return;
            float age = (tick - fx.Tick) / Simulation.TicksPerSecond;
            if (age > fx.Life) { Retire(fx); return; }
            float visible = 1f;
            if (fx.ShrinkFrom != float.MaxValue)
            {
                float dry = (tick - fx.ShrinkFrom) / Simulation.TicksPerSecond / EvictSeconds;
                if (dry >= 1f) { Retire(fx); return; }
                visible = Mathf.Clamp01(1f - dry * dry);
                // Moving a conformed mesh after sampling would bury it on slopes.
                if (fx.Surface == null) fx.Root.transform.localScale = Vector3.one * Mathf.Max(.05f, visible);
            }
            age = Mathf.Max(0f, age);
            if (fx.Surface != null) fx.Surface.Sample(age, visible);
            if (fx.Simulated >= 0f && Mathf.Abs(age - fx.Simulated) < 1e-5f) return;
            bool restart = fx.Simulated < 0f || age < fx.Simulated;
            float from = restart ? 0f : fx.Simulated;
            foreach (var ps in fx.Particles)
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
