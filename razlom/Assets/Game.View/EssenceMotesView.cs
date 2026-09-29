using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ОГОНЬКИ СУЩНОСТИ ОТ УБИТОГО К ГЕРОЮ (план «Мобы леса v2», поток I; кадры
    /// владельца death/01–04: золотые точки с тонким хвостом дугой летят в героя).
    ///
    /// EnemyDeathFxView по событию Death запускает огоньки из облака обломков
    /// (EnemyKillBeat.MotesAt, число — по виду). Каждый огонёк сначала выносит
    /// наружу и вверх, потом по дуге Безье он догоняет героя, ускоряясь к концу,
    /// и гаснет вспышкой поглощения у груди. Полёт 0,6–0,9 с
    /// (EnemyPresentationProfile.MoteFlight*). Конец дуги — текущее положение
    /// героя: дуга перестраивается каждый кадр, огонёк догоняет бегущего.
    ///
    /// Пул — до 64 огоньков (MotePoolSize) и одна система частиц на все: вид
    /// ставит частицы сам (SetParticles) — голова, хвост из точек по той же
    /// дуге чуть раньше по времени и вспышка у героя. Лишние на массовом
    /// убийстве не рождаются, а не выталкивают летящие. Возраст — от тика Sim:
    /// пауза держит огоньки, съёмка повторяется. Чистое представление: в Sim
    /// ничего не пишет, награды и опыт огоньки не несут.
    ///
    /// Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1005)]
    public sealed class EssenceMotesView : MonoBehaviour
    {
        public const string PrefabPath = "VFX/Death/VFX_Death_Motes";

        // Хвост в кадрах 01–04 — тонкая сплошная нить за золотой точкой, а не
        // пунктир: десять точек через 0,012 с при скорости огонька 3–6 м/с
        // ложатся через 4–7 см — меньше самой точки, на камере боя это линия.
        // Съёмка 29.09: шесть точек через 0,018 с при голове 0,15–0,21 м читались
        // пунктиром из крупных пятен.
        private const int TailDots = 10;
        /// <summary>Шаг хвоста по времени полёта, с: точки хвоста — где голова была раньше.</summary>
        private const float TailStep = .012f;
        /// <summary>Вспышка поглощения у героя.</summary>
        private const float PulseSeconds = .16f;
        /// <summary>Размер вспышки поглощения у героя: от и до, м. Меньше торса — героя не высветляет.</summary>
        private const float PulseSizeFrom = .22f, PulseSizeTo = .5f;
        /// <summary>Ореол вокруг головы огонька, в размерах головы.</summary>
        private const float HaloScale = 1.9f;
        /// <summary>Размер головы огонька, м. Кадры 01–04: яркая точка, а не шар.</summary>
        private const float HeadSizeMin = .09f, HeadSizeMax = .12f;
        /// <summary>Куда летят: грудь героя над землёй, м.</summary>
        private const float HeroChest = 1.05f;
        /// <summary>Частиц на огонёк: ореол, голова, хвост. Потолок системы в префабе — это × MotePoolSize.</summary>
        public const int ParticlesPerMote = TailDots + 2;

        // Материал огоньков — HDR ×3 с bloom: светлые жёлтые тона уходят в белёсо-салатовое
        // пятно на траве (съёмка 29.09). Тона глубже в оранжевое — после тонмаппинга золото.
        private static readonly Color32 HeadColor = new Color32(255, 186, 84, 255);
        private static readonly Color32 HaloColor = new Color32(255, 128, 40, 60);
        private static readonly Color32 TailColor = new Color32(255, 146, 48, 255);
        private static readonly Color32 PulseColor = new Color32(255, 186, 96, 90);

        public static EssenceMotesView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<EssenceMotesView>();
            return view != null ? view : host.AddComponent<EssenceMotesView>();
        }

        private struct Mote
        {
            public bool Active;
            public float LaunchTick, Flight, Size;
            public Vector3 Start, Out;
        }

        private readonly Mote[] _motes = new Mote[EnemyPresentationProfile.MotePoolSize];
        private ParticleSystem.Particle[] _buffer;
        private ParticleSystem _system;
        private TickDriver _driver;
        private Simulation _shown;
        private Vector3 _lastHero;
        private int _shownCount;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _buffer = new ParticleSystem.Particle[_motes.Length * ParticlesPerMote];
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[motes] Нет " + PrefabPath + " — собери: Разлом/Смерть мобов/VFX распада.", this);
                return;
            }
            var go = Instantiate(prefab, transform);
            go.name = "Огоньки сущности";
            _system = go.GetComponentInChildren<ParticleSystem>(true);
            if (_system != null)
            {
                // Своих частиц система не рождает: только то, что ставит вид.
                var emission = _system.emission; emission.enabled = false;
                // Префаб старой ревизии мог быть собран под короткий хвост.
                var main = _system.main;
                if (main.maxParticles < _buffer.Length) main.maxParticles = _buffer.Length;
                _system.Play(true);
            }
        }

        /// <summary>
        /// Запуск count огоньков из origin; первый — на тике launchTick (часы
        /// отрисовки: тик Sim − 1 + Alpha), остальные — вразброс до
        /// MoteLaunchSpreadSeconds. Возвращает, сколько встало в пул.
        /// </summary>
        public int Launch(float launchTick, Vector3 origin, int count, int seed)
        {
            if (_system == null) return 0;
            int launched = 0;
            for (int n = 0; n < count; n++)
            {
                int slot = FreeSlot();
                if (slot < 0) break;
                float delay = Rand(seed, n * 7 + 1, 0f, EnemyPresentationProfile.MoteLaunchSpreadSeconds);
                float flight = Rand(seed, n * 7 + 2, EnemyPresentationProfile.MoteFlightMinSeconds,
                    EnemyPresentationProfile.MoteFlightMaxSeconds);
                float angle = Rand(seed, n * 7 + 3, 0f, Mathf.PI * 2f);
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Rand(seed, n * 7 + 4, .45f, .95f)
                              + Vector3.up * Rand(seed, n * 7 + 5, .35f, .85f);
                var jitter = new Vector3(Rand(seed, n * 7 + 6, -.15f, .15f), Rand(seed, n * 7 + 7, -.12f, .12f),
                    Rand(seed, n * 7 + 8, -.15f, .15f));
                _motes[slot] = new Mote
                {
                    Active = true,
                    LaunchTick = launchTick + delay * Simulation.TicksPerSecond,
                    Flight = flight,
                    Size = Rand(seed, n * 7 + 9, HeadSizeMin, HeadSizeMax),
                    Start = origin + jitter,
                    Out = outward,
                };
                launched++;
            }
            return launched;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < _motes.Length; i++)
                if (!_motes[i].Active) return i;
            return -1;
        }

        private void LateUpdate()
        {
            if (_system == null) return;
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null || sim != _shown)
            {
                for (int i = 0; i < _motes.Length; i++) _motes[i].Active = false;
                _shown = sim;
                if (_shownCount > 0) { _system.SetParticles(_buffer, 0); _shownCount = 0; }
                if (sim == null) return;
            }

            float tick = sim.Tick - 1 + _driver.Alpha;
            if ((uint)Simulation.PlayerId < (uint)sim.Entities.Count)
                _lastHero = _driver.GetRenderPosition(Simulation.PlayerId) + Vector3.up * HeroChest;
            Vector3 target = _lastHero;

            int count = 0;
            for (int i = 0; i < _motes.Length; i++)
            {
                ref Mote m = ref _motes[i];
                if (!m.Active) continue;
                float age = (tick - m.LaunchTick) / Simulation.TicksPerSecond;
                if (age < 0f) continue;
                float t = age / m.Flight;
                if (t >= 1f)
                {
                    float pulse = (age - m.Flight) / PulseSeconds;
                    if (pulse >= 1f) { m.Active = false; continue; }
                    Color32 c = PulseColor;
                    c.a = (byte)(PulseColor.a * (1f - pulse));
                    Put(ref count, target, Mathf.Lerp(PulseSizeFrom, PulseSizeTo, pulse), c);
                    continue;
                }

                // Рождение: огонёк вспыхивает за первые 0,08 с, а не возникает целым.
                float birth = Mathf.Clamp01(age / .08f);
                Vector3 head = PathPoint(in m, t, target);
                Put(ref count, head, m.Size * HaloScale * birth, HaloColor);
                Put(ref count, head, m.Size * birth, HeadColor);
                for (int k = 1; k <= TailDots; k++)
                {
                    float tk = t - k * TailStep / m.Flight;
                    if (tk <= 0f) break;
                    Color32 c = TailColor;
                    c.a = (byte)(255f * (1f - k / (TailDots + 1f)) * .8f);
                    // Нить сужается к концу: от 0,7 головы до 0,25.
                    float thin = Mathf.Lerp(.7f, .25f, (k - 1f) / (TailDots - 1f));
                    Put(ref count, PathPoint(in m, tk, target), m.Size * thin * birth, c);
                }
            }

            if (!_system.isPlaying) _system.Play(true);
            _system.SetParticles(_buffer, count);
            _shownCount = count;
        }

        /// <summary>
        /// Дуга Безье: из облака обломков наружу-вверх (Out), потом к герою.
        /// Параметр ускоряется к концу — огонёк «втягивается» в героя.
        /// </summary>
        private static Vector3 PathPoint(in Mote m, float t, Vector3 target)
        {
            float u = .35f * t + .65f * t * t;
            Vector3 p0 = m.Start;
            Vector3 p1 = m.Start + m.Out * 1.1f;
            Vector3 p2 = target + (p1 - target) * .3f + Vector3.up * .45f;
            float v = 1f - u;
            return v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * target;
        }

        private void Put(ref int count, Vector3 position, float size, Color32 color)
        {
            if (count >= _buffer.Length || size <= 0f) return;
            ref ParticleSystem.Particle p = ref _buffer[count++];
            p.position = position;
            p.velocity = Vector3.zero;
            p.startSize = size;
            p.startColor = color;
            p.rotation = 0f;
            p.startLifetime = 1f;
            p.remainingLifetime = 1f;
        }

        /// <summary>Устойчивое число в [min, max) от зерна и соли: без UnityEngine.Random.</summary>
        private static float Rand(int seed, int salt, float min, float max)
        {
            uint h = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return min + (max - min) * ((h & 0xFFFFFF) / 16777216f);
        }
    }
}
