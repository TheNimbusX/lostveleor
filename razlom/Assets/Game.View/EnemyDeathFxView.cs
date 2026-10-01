using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// РАСПАД МОБА В МОМЕНТ УБИЙСТВА (план «Мобы леса v2», поток I; кадры
    /// владельца death/01–04, G7 «все да»).
    ///
    /// По событию Death из Sim тело рассыпается в свой материал:
    ///   кора и щепки — Хранитель, Камнекопыт;
    ///   труха, волокна, сухие листья — Корнеполз, Корнехват;
    ///   споры, капли сока, лепестки — Плюй-плод;
    ///   сколы панциря — Расщепень и детёныш (поверх их раскола);
    ///   шипы — Шипомёт;
    ///   кость рогов и мох — Вендиго.
    /// Такт убийства (EnemyKillBeat) общий с телом (ArenaView.BeginKillBeat —
    /// вспышка 2–3 кадра, стоп-кадр, распад кусками) и огоньками
    /// (EssenceMotesView). Здесь — залп обломков, лёгкая тряска камеры у
    /// тяжёлых (крупный вид, элита, последний в волне) и запуск огоньков.
    /// Тело, которое не трескается, а падает (Корнехват, URP Lit; ревью 01.10),
    /// получает второй, низкий залп в миг касания земли (EnemyKillBeat.LandsAt).
    ///
    /// Всё — от СОБЫТИЯ Death с тиком события; возраст залпа считается от тика
    /// Sim с долей кадра: пауза держит кадр, съёмка повторяется. Пулы — до боя,
    /// в драке ни одного Instantiate; кончился пул — берётся самый старый залп.
    /// Префабы собирает Editor/EnemyDeathVfxSetup (Resources/VFX/Death).
    ///
    /// Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1004)]
    public sealed class EnemyDeathFxView : MonoBehaviour
    {
        public const string Folder = "VFX/Death/";

        /// <summary>Префабы залпа; индекс — <see cref="DeathMaterial"/>.</summary>
        public static readonly string[] BurstPrefabs =
        {
            Folder + "VFX_Death_Bark", Folder + "VFX_Death_Rot", Folder + "VFX_Death_Spore",
            Folder + "VFX_Death_Shell", Folder + "VFX_Death_Thorn", Folder + "VFX_Death_Bone"
        };

        // Одновременных залпов: Вихрь кладёт пятерых корнеползов разом, вендиго — один.
        private static readonly int[] PoolSizes = { 6, 10, 6, 6, 3, 2 };

        /// <summary>Сколько живёт залп: обломки лежат и тают к этому возрасту (см. EnemyDeathVfxSetup).</summary>
        private static readonly float[] BurstLife = { 2.4f, 2.7f, 2.8f, 2.0f, 2.4f, 3.4f };

        private const float SimulateStep = 1f / 30f;

        /// <summary>Залп касания земли: на сколько впереди корня ложится грудь (при масштабе 1), м, и во сколько он меньше главного.</summary>
        private const float LandingReach = .5f, LandingScale = .7f;

        public static EnemyDeathFxView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<EnemyDeathFxView>();
            return view != null ? view : host.AddComponent<EnemyDeathFxView>();
        }

        /// <summary>
        /// Такт убийства сущности в кадре её события Death. Последний в волне —
        /// на поле не осталось живых врагов и не ждёт раскол Расщепеня.
        /// Одинаков для тела, залпа, огоньков и звука: все читают одно
        /// состояние Sim после шагов кадра.
        /// </summary>
        public static EnemyKillBeat BeatFor(Simulation sim, int entity)
        {
            if (sim == null || (uint)entity >= (uint)sim.Entities.Count)
                return EnemyPresentationProfile.Kill(EnemyKind.None, false, false);
            return EnemyPresentationProfile.Kill(sim.Entities.Kind[entity], sim.IsElite(entity), FieldCleared(sim, entity));
        }

        /// <summary>
        /// Убийство зачистило поле. Не CountAliveEnemies() == 0: следующая волна
        /// встаёт в тот же тик, в который лёг последний (UpdateEncounterWaves в
        /// конце тика), и к кадру смерти она уже жива. Встающие из земли не
        /// считаются — они ещё не в бою; ждущий раскол Расщепеня — считается.
        /// </summary>
        public static bool FieldCleared(Simulation sim, int killed)
        {
            if (sim == null || sim.HasPendingSplits) return false;
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (i == killed || !entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (!sim.IsEmerging(i)) return false;
            }
            return true;
        }

        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public float Tick = -1000f, Life, Simulated = -1f;
            public bool Armed;
        }

        private sealed class Pool
        {
            public Burst[] Items = new Burst[0];
            public int Cursor;
        }

        private struct PendingShake
        {
            public float Tick, Trauma, Zoom;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private EssenceMotesView _motes;
        private CombatCameraJuice _cameraJuice;
        private Camera _camera;
        private Simulation _shown;
        private Pool[] _pools;
        private readonly List<PendingShake> _shakes = new List<PendingShake>(8);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            _motes = EssenceMotesView.EnsureOn(gameObject);
            _pools = new Pool[BurstPrefabs.Length];
            bool missing = false;
            for (int m = 0; m < BurstPrefabs.Length; m++)
            {
                _pools[m] = MakePool(BurstPrefabs[m], "Смерть: " + (DeathMaterial)m, PoolSizes[m], BurstLife[m]);
                missing |= _pools[m].Items.Length == 0;
            }
            if (missing)
                Debug.LogWarning("[death-fx] Нет префабов распада в Resources/VFX/Death — собери: Разлом/Смерть мобов/VFX распада.", this);
        }

        /// <summary>Есть ли залп материала вида: без него CombatJuiceView рисует старую искру смерти.</summary>
        public bool HandlesKind(EnemyKind kind)
        {
            int m = (int)EnemyPresentationProfile.Kill(kind, false, false).Material;
            return _pools != null && m < _pools.Length && _pools[m].Items.Length > 0;
        }

        // ------------------------------------------------------------- frame

        private void LateUpdate()
        {
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null)
            {
                if (_shown != null) ResetAll();
                _shown = null;
                return;
            }
            if (_shown != sim)
            {
                ResetAll();
                _shown = sim;
            }
            float tick = sim.Tick - 1 + _driver.Alpha;

            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                // Событие тика T приходит с SimulationTick = T + 1.
                if (e.Type == SimEventType.Death) OnDeath(sim, e, contexts[i].SimulationTick - 1);
            }

            for (int i = _shakes.Count - 1; i >= 0; i--)
            {
                if (tick < _shakes[i].Tick) continue;
                Shake(_shakes[i].Trauma, _shakes[i].Zoom);
                _shakes.RemoveAt(i);
            }
            foreach (var pool in _pools)
                foreach (var burst in pool.Items) Advance(burst, tick);
        }

        private void OnDeath(Simulation sim, SimEvent e, int tick)
        {
            int id = e.Target;
            if ((uint)id >= (uint)sim.Entities.Count || id == Simulation.PlayerId) return;
            if (sim.Entities.Side[id] != Faction.Orvill) return;

            EnemyKillBeat beat = BeatFor(sim, id);
            const float perSecond = Simulation.TicksPerSecond;
            if (beat.ShakeTrauma > 0f)
                _shakes.Add(new PendingShake { Tick = tick + beat.BurstAt * perSecond, Trauma = beat.ShakeTrauma, Zoom = beat.ShakeZoom });
            if (CaptureRig.NoVfx) return;

            Vector3 ground = Ground(e.Position);
            // +Z залпа — прочь от убийцы: обломки летят по удару. Убийцы нет — от героя.
            int killer = (uint)e.Source < (uint)sim.Entities.Count ? e.Source : Simulation.PlayerId;
            FixVec2 from = sim.Entities.Position[killer];
            var away = new Vector3(e.Position.X.ToFloat() - from.X.ToFloat(), 0f, e.Position.Y.ToFloat() - from.Y.ToFloat());
            Quaternion yaw = away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away, Vector3.up) : Quaternion.identity;
            int seed = id * 7919 + tick * 104729;

            Take(_pools[(int)beat.Material], tick + beat.BurstAt * perSecond, ground, yaw, beat.Scale, seed);
            // Падающее тело (Корнехват, ревью 01.10): на касании земли брюхом — второй, низкий
            // залп того же материала у груди, впереди корня по взгляду, на котором он падает.
            if (beat.LandsAt > beat.BurstAt)
            {
                FixVec2 f = sim.Entities.Facing[id];
                var facing = new Vector3(f.X.ToFloat(), 0f, f.Y.ToFloat());
                if (facing.sqrMagnitude < 1e-6f) facing = yaw * Vector3.forward;
                Vector3 chest = Ground(e.Position) + facing.normalized * (LandingReach * beat.Scale);
                chest.y = _layout != null ? _layout.WeaponGroundHeight(chest.x, chest.z) : ground.y;
                // +Z залпа — по ходу падения: труха брызжет из-под груди вперёд.
                Take(_pools[(int)beat.Material], tick + beat.LandsAt * perSecond, chest,
                    Quaternion.LookRotation(facing.normalized, Vector3.up), beat.Scale * LandingScale, seed + 7);
            }
            if (_motes != null && beat.MoteCount > 0)
                _motes.Launch(tick + beat.MotesAt * perSecond, ground + Vector3.up * beat.CentreHeight, beat.MoteCount, seed);
        }

        private void Shake(float trauma, float zoom)
        {
            if (_cameraJuice == null || _camera != Camera.main)
            {
                _camera = Camera.main;
                _cameraJuice = _camera != null ? _camera.GetComponent<CombatCameraJuice>() : null;
            }
            _cameraJuice?.AddImpulse(trauma, zoom);
        }

        private void ResetAll()
        {
            _shakes.Clear();
            if (_pools == null) return;
            foreach (var pool in _pools)
                foreach (var burst in pool.Items) Retire(burst);
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

        /// <summary>
        /// Следующий залп пула ставится на землю и ждёт своего тика (после
        /// вспышки и стоп-кадра). Зерно систем — от убийства: соседние смерти
        /// не повторяют друг друга, перемотка той же даёт тот же кадр.
        /// </summary>
        private static void Take(Pool pool, float burstTick, Vector3 position, Quaternion rotation, float scale, int seed)
        {
            if (pool.Items.Length == 0) return;
            var b = pool.Items[pool.Cursor++ % pool.Items.Length];
            b.Root.SetActive(false);
            b.Tick = burstTick;
            b.Simulated = -1f;
            b.Armed = true;
            b.Root.transform.SetPositionAndRotation(position, rotation);
            b.Root.transform.localScale = Vector3.one * scale;
            for (int k = 0; k < b.Particles.Length; k++)
            {
                var ps = b.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = b.Seeds[k] + (uint)seed * 7919u;
            }
        }

        private static void Retire(Burst b)
        {
            if (b == null) return;
            b.Armed = false;
            b.Tick = -1000f;
            b.Simulated = -1f;
            if (b.Root != null && b.Root.activeSelf) b.Root.SetActive(false);
        }

        /// <summary>
        /// Возраст залпа — от тика Sim с долей кадра. До своего тика залп спрятан
        /// (стоп-кадр ещё идёт); вперёд системы догоняются приращениями, назад —
        /// перезапуском; на паузе возраст и частицы стоят.
        /// </summary>
        private static void Advance(Burst b, float tick)
        {
            if (b == null || !b.Armed) return;
            float age = (tick - b.Tick) / Simulation.TicksPerSecond;
            if (age < 0f)
            {
                if (b.Root.activeSelf) b.Root.SetActive(false);
                b.Simulated = -1f;
                return;
            }
            if (age > b.Life) { Retire(b); return; }
            if (!b.Root.activeSelf) b.Root.SetActive(true);
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

        private Vector3 Ground(FixVec2 at)
        {
            var p = new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat());
            p.y = _layout != null ? _layout.WeaponGroundHeight(p.x, p.z) : 0f;
            return p;
        }

        private void OnDestroy()
        {
            if (_pools == null) return;
            foreach (var pool in _pools)
                foreach (var b in pool.Items)
                    if (b != null && b.Root != null) Destroy(b.Root);
        }
    }
}
