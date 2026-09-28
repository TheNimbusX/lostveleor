using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Единственная точка запуска runtime-систем. Авторская часть мира — камера,
    /// свет, цвет, лагерь и Полигон — хранится в сцене и приходит сюда явными
    /// ссылками. Код создаёт только то, что действительно живёт один запуск.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("Контракт сцены")]
        [SerializeField] private Camera _gameplayCamera;
        [SerializeField] private SceneWorldView _sceneWorld;

        [Header("Забег")]
        [Tooltip("0 — сгенерировать сид из текущего времени.")]
        public ulong RunSeed = 0;
        [Tooltip("Профиль первой локации. Пусто — Resources/Locations/Meadow.")]
        public LocationTheme Location;
        public int EnemyCount = 40;
        public bool LogStateHash = false;
        [Tooltip("Только отдельная сцена стенда. Создаёт сессию в памяти без загрузки и записи прогресса.")]
        public EnemyTestArena EnemySandbox;

        [Header("Звук")]
        // Синтетический прототип полностью заменён короткими CC0 one-shot.
        // Их слои привязаны только к подтверждённым событиям симуляции.
        [Tooltip("Слоёный боевой звук из импортированных CC0-сэмплов.")]
        public bool CombatSound = true;


        public TickDriver Driver { get; private set; }

        private void Awake()
        {
            if (!ValidateSceneContract())
            {
                enabled = false;
                return;
            }

            // Runtime-корень остаётся в мировом нуле: симуляция считает позиции
            // в мировых, а авторские CampRoot и ProvingGroundRoot выровнены под
            // ту же систему координат в сцене.
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // Компоненты вешаются на ВЫКЛЮЧЕННЫЙ объект — это не стилистика.
            // AddComponent на включённом объекте вызывает Awake немедленно,
            // и TickDriver построил бы арену раньше, чем сюда доедут RunSeed
            // и EnemyCount: настройки в инспекторе молча ничего бы не значили.
            // У выключенного объекта Awake откладывается до SetActive(true),
            // и к этому моменту поля уже проставлены.
            GameObject sim = new GameObject("Симуляция");
            sim.transform.SetParent(transform, false);
            sim.SetActive(false);

            Driver = sim.AddComponent<TickDriver>();
            Driver.EnemySandbox = EnemySandbox;

            // Съёмочная рига может задать сид и число врагов из командной
            // строки. Сцена при этом не трогается: сохранённый .unity — это
            // состояние проекта, а не параметр отдельного запуска.
            Driver.RunSeed = CaptureRig.HasSeedOverride ? CaptureRig.SeedOverride : RunSeed;
            Driver.EnemyCount = CaptureRig.HasEnemyOverride ? CaptureRig.EnemyOverride : EnemyCount;
            Driver.LogStateHash = LogStateHash;
            var location = Location != null ? Location : Resources.Load<LocationTheme>("Locations/Meadow");
            Driver.Location = location != null ? location.Gameplay : null;

            var arena = sim.AddComponent<ArenaView>();
            if (EnemySandbox != null)
            {
                arena.PrewarmWole = 1;
                arena.PrewarmOrvill = arena.PrewarmRootSwarm = arena.PrewarmForestBud = 4;
            }
            // Лента сабли — часть самой атаки, поэтому она нужна и в обычном
            // бою, и в изолированной VFX-съёмке.
            sim.AddComponent<CombatJuiceView>();
            if (!CaptureRig.IsVfxShowcase)
            {
                sim.AddComponent<DamageNumbers>();
            }
            sim.AddComponent<PelagVfxController>();
            if (CombatSound) sim.AddComponent<CombatAudio>();
            sim.AddComponent<PauseMenu>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            sim.AddComponent<DeveloperMenu>();
#endif
            var layout = sim.AddComponent<LayoutView>();
            layout.Profile = location;
            if (EnemySandbox != null) layout.enabled = false;
            if (!CaptureRig.IsVfxShowcase)
            {
                sim.AddComponent<CombatIndicators>();
                sim.AddComponent<HealthBars>();
                sim.AddComponent<PlayerHud>();
                if (EnemySandbox == null)
                {
                    sim.AddComponent<RunHud>();
                    sim.AddComponent<RunEndBeat>();
                    sim.AddComponent<CampHud>();

                    // Главное меню держит обычную игру на паузе до PLAY.
                    sim.AddComponent<MainMenuView>();
                }
            }

            sim.SetActive(true);
            if (EnemySandbox != null) Driver.StartEnemySandbox();

            // Авторские представления получают зависимости только после
            // TickDriver.Awake: к этому моменту сессия уже существует.
            CameraFollow follow = _gameplayCamera.GetComponent<CameraFollow>();
            follow.Initialize(Driver, _gameplayCamera.transform);
            if (EnemySandbox == null) _sceneWorld.Initialize(Driver);
        }

        private bool ValidateSceneContract()
        {
            if (_gameplayCamera == null)
            {
                Debug.LogError("[Разлом] Bootstrap: в сцене не назначена Gameplay Camera.", this);
                return false;
            }

            if (_sceneWorld == null && EnemySandbox == null)
            {
                Debug.LogError("[Разлом] Bootstrap: в сцене не назначен SceneWorldView.", this);
                return false;
            }

            if (_gameplayCamera.GetComponent<CameraFollow>() == null)
            {
                Debug.LogError("[Разлом] Bootstrap: на Gameplay Camera нет CameraFollow.", _gameplayCamera);
                return false;
            }

            if (!_gameplayCamera.CompareTag("MainCamera"))
            {
                Debug.LogError("[Разлом] Bootstrap: Gameplay Camera обязана иметь тег MainCamera.", _gameplayCamera);
                return false;
            }

            return EnemySandbox != null || _sceneWorld.ValidateContract(true);
        }
    }
}
