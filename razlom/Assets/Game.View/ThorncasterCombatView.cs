using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// БОЙ ШИПОМЁТА СО СТОРОНЫ КАРТИНКИ: эффекты по целевым кадрам 26.09
    /// (1-line-spikes, 3-burst), префабы собирает ThorncasterVfxSetup.
    ///
    /// Всё рождается из событий Sim (FrameEventContexts), а не из опроса тиков,
    /// и ведётся по тикам Sim (тик − 1 + Alpha): пауза и съёмка держат кадр,
    /// перемотка переигрывает тот же.
    ///
    /// • Шип линии — по EnemyActionImpact(ThornLine) каждого сегмента, ровно в
    ///   его тик (27/33/39/45 от начала): встаёт из земли с перелётом, стоит,
    ///   пока руки Шипомёта в земле, и через LineSinkDelayTicks после
    ///   последнего открытого шипа (ImpactTick) уходит вниз. Снятая линия
    ///   (оглушение, смерть — EnemyActionCancelled) опускает встающие шипы сразу.
    /// • Всплеск — по EnemyActionImpact(ThornBurst): звезда шипов вокруг тела,
    ///   держится стойку и уходит.
    /// • Выстрел: щепки у кончика руки в тик выпуска — EnemyProjectileLaunched
    ///   (снятый до выпуска выстрел его не шлёт). Полосы полёта нет; сам шип,
    ///   след и щепки там, где он встал, рисует ForestThornShotView.
    ///
    /// Заводится ThorncasterViewInstaller.Prepare на объекте арены. Пулы — сразу,
    /// в бою ни одного Instantiate.
    /// </summary>
    [DefaultExecutionOrder(640)]
    public sealed class ThorncasterCombatView : MonoBehaviour
    {
        public const string PrefabFolder = "VFX/Thorncaster/Prefabs/";
        public const string LineSpikePrefab = "VFX_Thorncaster_LineSpike", BurstPrefab = "VFX_Thorncaster_Burst",
            ShotReleasePrefab = "VFX_Thorncaster_ShotRelease";

        /// <summary>Узел префаба с шипами-MeshRenderer'ами (ось +Y, полный размер — масштаб в префабе).</summary>
        public const string ThornNodeName = "Thorns";

        /// <summary>Шипы линии стоят после последнего шипа столько тиков, пока руки выходят из земли, и опускаются.</summary>
        public const int LineSinkDelayTicks = 8;

        /// <summary>Звезда всплеска стоит столько тиков стойки (15) и уходит к её концу.</summary>
        public const int BurstHoldTicks = 11;

        /// <summary>Шаг догоняющей симуляции: столкновения комьев с землёй не проскакивают на рывке кадра.</summary>
        private const float SimulateStep = 1f / 30f;

        /// <summary>Как растут и уходят шипы вида эффекта. Время — в тиках Sim.</summary>
        private sealed class Motion
        {
            public float Rise, Overshoot, Settle, Sink, SinkDepth, DelaySpread;
        }

        private sealed class Thorn
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Scale;
            public float Delay;
        }

        private sealed class Effect
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public Transform ThornRoot;
            public Vector3 ThornRootPosition;
            public Thorn[] Thorns;
            public float Life;
            public Motion Motion;
            public int Tick = -1000;
            public float Simulated = -1f;
            // Чей эффект и докуда стоит: SinkTick — тик, с которого шипы уходят вниз.
            public int Entity = -1, ActionStart = int.MinValue, SinkTick = int.MaxValue;
            public float Stretch = 1f;
        }

        private sealed class Pool
        {
            public Effect[] Items = new Effect[0];
            public int Cursor;
        }

        /// <summary>Линия, начатая Шипомётом: тик начала и тик, с которого шипы уходят.</summary>
        private struct Line
        {
            public int StartTick, SinkTick;
            public Vector3 Direction;
        }

        private static readonly Motion LineMotion = new Motion
            { Rise = 3.5f, Overshoot = 1.12f, Settle = 3f, Sink = 10f, SinkDepth = .12f, DelaySpread = 2.2f };
        private static readonly Motion BurstMotion = new Motion
            { Rise = 2.5f, Overshoot = 1.08f, Settle = 2.5f, Sink = 8f, SinkDepth = .08f, DelaySpread = 1.5f };

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;
        private Pool _line, _burst, _release;
        private Pool[] _pools;
        private readonly Dictionary<int, Line> _lines = new Dictionary<int, Line>();
        private readonly Dictionary<Transform, ThorncasterAnimatorView> _bodies = new Dictionary<Transform, ThorncasterAnimatorView>();
        private bool _missingReported;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
            // Две линии по четыре шипа и запас на хвост уходящей; всплеск и выстрел — по Шипомёту в бою.
            _line = MakePool(LineSpikePrefab, "Шипомёт: шип линии", 10, 2.5f, LineMotion);
            _burst = MakePool(BurstPrefab, "Шипомёт: всплеск", 2, 2.7f, BurstMotion);
            _release = MakePool(ShotReleasePrefab, "Шипомёт: выпуск шипа", 3, .8f, null);
            _pools = new[] { _line, _burst, _release };
        }

        private Pool MakePool(string prefabName, string name, int count, float life, Motion motion)
        {
            var pool = new Pool();
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null)
            {
                if (!_missingReported)
                    Debug.LogWarning("[Разлом] Шипомёт: нет префабов «" + PrefabFolder + "…» — эффекты не рисуются. Собери: Разлом/Шипомёт/VFX: пересобрать.");
                _missingReported = true;
                return pool;
            }
            pool.Items = new Effect[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform); go.name = name;
                var effect = new Effect { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true), Life = life, Motion = motion };
                effect.Seeds = new uint[effect.Particles.Length];
                for (int k = 0; k < effect.Particles.Length; k++)
                {
                    var ps = effect.Particles[k];
                    // Прогрев: один короткий прогон заводит буферы частиц до боя.
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    effect.Seeds[k] = ps.randomSeed;
                }
                CollectThorns(effect);
                go.SetActive(false);
                pool.Items[i] = effect;
            }
            return pool;
        }

        /// <summary>Шипы — дети узла Thorns по порядку; первый (главный) встаёт без задержки.</summary>
        private static void CollectThorns(Effect effect)
        {
            effect.ThornRoot = effect.Root.transform.Find(ThornNodeName);
            if (effect.ThornRoot == null) { effect.Thorns = new Thorn[0]; return; }
            effect.ThornRootPosition = effect.ThornRoot.localPosition;
            var list = new List<Thorn>();
            for (int i = 0; i < effect.ThornRoot.childCount; i++)
            {
                var child = effect.ThornRoot.GetChild(i);
                var renderer = child.GetComponent<Renderer>();
                if (renderer == null) continue;
                float spread = effect.Motion != null ? effect.Motion.DelaySpread : 0f;
                list.Add(new Thorn
                {
                    Transform = child, Renderer = renderer, Scale = child.localScale,
                    Delay = list.Count == 0 ? 0f : spread * Hash01(list.Count, 17),
                });
            }
            effect.Thorns = list.ToArray();
        }

        private void LateUpdate()
        {
            var sim = _driver != null ? _driver.Sim : null;
            int depth = _driver != null && _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: номера и тики начинаются заново.
            if (!ReferenceEquals(sim, _shown) || (_driver != null && _generation != _driver.Generation) || depth != _depth)
            {
                foreach (var pool in _pools) foreach (var e in pool.Items) Retire(e);
                _lines.Clear(); _bodies.Clear();
                _shown = sim; _generation = _driver != null ? _driver.Generation : -1; _depth = depth;
            }
            if (sim == null) return;
            float tick = sim.Tick - 1 + _driver.Alpha;

            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                // Тик, в котором событие родилось, на часах отрисовки.
                int at = contexts[i].SimulationTick - 1;
                var kind = (EnemyActionKind)e.ActionVariant;
                switch (e.Type)
                {
                    case SimEventType.EnemyActionStarted:
                        if (kind == EnemyActionKind.ThornLine) LineStarted(sim, e.Source, at);
                        break;
                    case SimEventType.EnemyProjectileLaunched:
                        if (kind == EnemyActionKind.ThornShot) ShotReleased(sim, e.Source, e.Amount, at);
                        break;
                    case SimEventType.EnemyActionImpact:
                        if (kind == EnemyActionKind.ThornLine) LineSpike(sim, e.Source, e.Amount, e.Position, at);
                        else if (kind == EnemyActionKind.ThornBurst) BurstImpact(sim, e.Source, e.Position, at);
                        break;
                    case SimEventType.EnemyActionCancelled:
                        Cancelled(e.Source, kind, at);
                        break;
                }
            }

            foreach (var pool in _pools)
                foreach (var effect in pool.Items) Advance(effect, tick);
        }

        // ------------------------------------------------------------ events

        private void LineStarted(Simulation sim, int caster, int at)
        {
            var line = new Line
            {
                StartTick = at,
                SinkTick = at + Simulation.ThornLineLastImpactTicks + LineSinkDelayTicks,
                Direction = FacingOf(sim, caster),
            };
            // Последний открытый шип известен с начала: линия короче четырёх, если упёрлась в препятствие.
            if (sim.TryGetThorncasterAction(caster, out var a) && a.Action == ThornAction.Line && a.StartTick == at)
            {
                line.SinkTick = a.ImpactTick + LineSinkDelayTicks;
                line.Direction = Flat(a.Direction);
            }
            _lines[caster] = line;
        }

        private void LineSpike(Simulation sim, int caster, int index, FixVec2 center, int at)
        {
            int start = at - Simulation.ThornLineWindupTicks - Simulation.ThornLineSpikeStepTicks * index;
            if (!_lines.TryGetValue(caster, out var line) || line.StartTick != start)
            {
                // Начало линии прошло мимо вида (заведён посреди линии): берём, что знает Sim.
                line = new Line { StartTick = start, SinkTick = at + LineSinkDelayTicks, Direction = FacingOf(sim, caster) };
                if (sim.TryGetThorncasterAction(caster, out var a) && a.Action == ThornAction.Line && a.StartTick == start)
                {
                    line.SinkTick = a.ImpactTick + LineSinkDelayTicks;
                    line.Direction = Flat(a.Direction);
                }
                _lines[caster] = line;
            }
            int seed = Mix(caster, start, index);
            var effect = Take(_line, at, Ground(center, 0f), Facing(line.Direction), seed);
            if (effect == null) return;
            effect.Entity = caster; effect.ActionStart = start; effect.SinkTick = line.SinkTick;
            Vary(effect, seed, 50f, .9f, 1.1f);
        }

        private void BurstImpact(Simulation sim, int caster, FixVec2 center, int at)
        {
            int seed = Mix(caster, at, 7);
            var effect = Take(_burst, at, Ground(center, 0f), Facing(FacingOf(sim, caster)), seed);
            if (effect == null) return;
            effect.Entity = caster; effect.ActionStart = at - Simulation.ThornBurstWindupTicks; effect.SinkTick = at + BurstHoldTicks;
            Vary(effect, seed, 360f, .95f, 1.05f);
        }

        /// <summary>
        /// Шип сорвался с руки в тик at: щепки и дымок у кончика правой руки, по
        /// направлению полёта из Sim (у выстрела номер serial), без него — по взгляду.
        /// </summary>
        private void ShotReleased(Simulation sim, int caster, int serial, int at)
        {
            Vector3 direction = FacingOf(sim, caster);
            if (sim.TryGetThornShot(caster, out var shot) && shot.Serial == serial) direction = Flat(shot.Direction);
            var effect = Take(_release, at, MuzzleOf(sim, caster, direction), Facing(direction), Mix(caster, at, 3));
            if (effect != null) effect.Entity = caster;
        }

        /// <summary>
        /// Действие снято (оглушение, смерть, волок): вставшие шипы линии и
        /// всплеска уходят с этого тика. Невыпущенный шип выстрела и так не
        /// рисуется: щепки выпуска ждут EnemyProjectileLaunched.
        /// </summary>
        private void Cancelled(int caster, EnemyActionKind kind, int at)
        {
            if (kind == EnemyActionKind.ThornLine && _lines.TryGetValue(caster, out var line) && line.SinkTick > at)
            {
                line.SinkTick = at; _lines[caster] = line;
                foreach (var e in _line.Items)
                    if (e.Root.activeSelf && e.Entity == caster && e.ActionStart == line.StartTick && e.SinkTick > at) e.SinkTick = at;
            }
            else if (kind == EnemyActionKind.ThornBurst)
            {
                foreach (var e in _burst.Items)
                    if (e.Root.activeSelf && e.Entity == caster && e.Tick <= at && e.SinkTick > at) e.SinkTick = at;
            }
        }

        // ----------------------------------------------------------- effects

        /// <summary>
        /// Следующий экземпляр пула на земле. Зерно систем меняется с seed: шипы
        /// не повторяют друг друга, а перемотка того же удара даёт тот же кадр.
        /// </summary>
        private Effect Take(Pool pool, int tick, Vector3 position, Quaternion rotation, int seed)
        {
            if (pool.Items.Length == 0) return null;
            var e = pool.Items[pool.Cursor++ % pool.Items.Length];
            e.Tick = tick; e.Simulated = -1f; e.Entity = -1; e.ActionStart = int.MinValue; e.SinkTick = int.MaxValue;
            e.Stretch = 1f;
            e.Root.transform.SetPositionAndRotation(position, rotation);
            if (e.ThornRoot != null) { e.ThornRoot.localPosition = e.ThornRootPosition; e.ThornRoot.localRotation = Quaternion.identity; }
            foreach (var thorn in e.Thorns) thorn.Renderer.enabled = false;
            e.Root.SetActive(true);
            for (int k = 0; k < e.Particles.Length; k++)
            {
                var ps = e.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = e.Seeds[k] + (uint)seed * 7919u;
            }
            return e;
        }

        /// <summary>Разнобой куста: поворот вокруг вертикали до ±yaw/2 и длина шипов в [min, max].</summary>
        private static void Vary(Effect e, int seed, float yaw, float min, float max)
        {
            if (e.ThornRoot != null) e.ThornRoot.localRotation = Quaternion.Euler(0f, (Hash01(seed, 3) - .5f) * yaw, 0f);
            e.Stretch = Mathf.Lerp(min, max, Hash01(seed, 5));
        }

        private static void Retire(Effect e)
        {
            if (e == null) return;
            e.Tick = -1000; e.Simulated = -1f; e.Entity = -1;
            if (e.Root.activeSelf) e.Root.SetActive(false);
        }

        /// <summary>
        /// Возраст эффекта — от тика Sim с долей кадра. Шипы — функцией тика
        /// (рост, стойка, уход), частицы догоняются приращениями, назад
        /// (перемотка) — перезапуском; на паузе всё стоит.
        /// </summary>
        private void Advance(Effect e, float tick)
        {
            if (e == null || !e.Root.activeSelf) return;
            float ticks = tick - e.Tick;
            bool sunk = e.Thorns.Length == 0 || e.Motion == null || tick >= e.SinkTick + e.Motion.Sink;
            if ((ticks / Simulation.TicksPerSecond > e.Life && sunk) || ticks > Simulation.TicksPerSecond * 30f) { Retire(e); return; }
            if (ticks < 0f) return;
            if (e.Motion != null) PoseThorns(e, tick, ticks);
            float age = ticks / Simulation.TicksPerSecond;
            if (e.Simulated >= 0f && Mathf.Abs(age - e.Simulated) < 1e-5f) return;
            bool restart = e.Simulated < 0f || age < e.Simulated;
            float from = restart ? 0f : e.Simulated;
            foreach (var ps in e.Particles)
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
            e.Simulated = age;
        }

        /// <summary>
        /// Шип: рост за Rise тиков с перелётом Overshoot (ease-out), осадка к 1 за
        /// Settle; с SinkTick — уход за Sink тиков (ease-in) с опусканием куста в
        /// землю. Растёт только длина (ось +Y шипа): шип лезет из земли, а не раздувается.
        /// </summary>
        private static void PoseThorns(Effect e, float tick, float ticks)
        {
            var m = e.Motion;
            float sink = tick < e.SinkTick ? 1f : 1f - Square(Mathf.Clamp01((tick - e.SinkTick) / Mathf.Max(1f, m.Sink)));
            if (e.ThornRoot != null) e.ThornRoot.localPosition = e.ThornRootPosition + Vector3.down * (m.SinkDepth * (1f - sink));
            foreach (var thorn in e.Thorns)
            {
                float t = ticks - thorn.Delay;
                float grow;
                if (t <= 0f) grow = 0f;
                else if (t < m.Rise) { float k = 1f - t / m.Rise; grow = m.Overshoot * (1f - k * k * k); }
                else grow = Mathf.Lerp(m.Overshoot, 1f, Mathf.Clamp01((t - m.Rise) / Mathf.Max(.5f, m.Settle)));
                float length = grow * sink * e.Stretch;
                bool shown = length > .01f;
                if (thorn.Renderer.enabled != shown) thorn.Renderer.enabled = shown;
                if (shown) thorn.Transform.localScale = new Vector3(thorn.Scale.x, thorn.Scale.y * length, thorn.Scale.z);
            }
        }

        // ------------------------------------------------------------ places

        private Vector3 Ground(FixVec2 at, float lift)
        {
            float x = at.X.ToFloat(), z = at.Y.ToFloat();
            return new Vector3(x, (_layout != null ? _layout.WeaponGroundHeight(x, z) : 0f) + lift, z);
        }

        private static Quaternion Facing(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }

        private static Vector3 Flat(FixVec2 direction) => new Vector3(direction.X.ToFloat(), 0f, direction.Y.ToFloat());

        private static Vector3 FacingOf(Simulation sim, int entity)
            => (uint)entity < (uint)sim.Entities.Count ? Flat(sim.Entities.Facing[entity]) : Vector3.forward;

        /// <summary>
        /// Кончик правой руки-шипа Шипомёта: сокет тела, если тело есть и собрано
        /// ThorncasterBuilder; иначе — замер export.json от позиции Sim по направлению выстрела.
        /// </summary>
        private Vector3 MuzzleOf(Simulation sim, int entity, Vector3 direction)
        {
            if (_arena != null && _arena.TryGetEntityView(entity, out Transform view) && view != null)
            {
                if (!_bodies.TryGetValue(view, out var body))
                {
                    body = view.GetComponent<ThorncasterAnimatorView>();
                    _bodies[view] = body;
                }
                if (body != null && body.Entity == entity) return body.MuzzlePosition;
            }
            Vector3 origin = (uint)entity < (uint)sim.Entities.Count ? _driver.GetRenderPosition(entity) : Vector3.zero;
            if (_layout != null) origin.y = _layout.WeaponGroundHeight(origin.x, origin.z);
            return origin + Facing(direction) * ThorncasterAnimatorView.MuzzleFallback;
        }

        private static float Square(float x) => x * x;

        private static int Mix(int a, int b, int c) => unchecked(a * 73856093 ^ b * 19349663 ^ c * 83492791);

        /// <summary>Детерминированный разброс 0..1 (как у сборщика эффектов).</summary>
        private static float Hash01(int i, int salt)
        {
            uint h = (uint)(i * 747796405 + salt * 2891336453u);
            h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        private void OnDestroy()
        {
            if (_pools == null) return;
            foreach (var pool in _pools)
                foreach (var e in pool.Items) if (e != null && e.Root != null) Destroy(e.Root);
        }
    }
}
