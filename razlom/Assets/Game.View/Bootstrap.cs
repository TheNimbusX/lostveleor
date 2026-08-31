using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Собирает игровую сцену кодом. Сцена намеренно НЕ хранится в .unity файле:
    /// пустая сцена плюс один объект с этим компонентом — и Play работает.
    /// Настройки сцены тогда лежат в системе контроля версий как текст, а не как
    /// бинарник, который нельзя ни прочитать, ни слить.
    ///
    /// Использование: создать пустую сцену, добавить пустой объект, повесить
    /// Bootstrap, нажать Play. Больше ничего настраивать не нужно.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("Забег")]
        [Tooltip("0 — сгенерировать сид из текущего времени.")]
        public ulong RunSeed = 0;
        public int EnemyCount = 40;
        public bool LogStateHash = false;

        [Header("Камера")]
        [Tooltip("Классическая изометрия ARPG: наклон 30°, поворот 45°.")]
        public Vector3 CameraAngles = new Vector3(38f, 45f, 0f);
        public float CameraSize = 4.8f;
        [Tooltip("Отступ камеры назад по своему forward. На ортографии не влияет на масштаб, только на отсечение.")]
        public float CameraDistance = 60f;

        // Фон — далёкая дымка яркого дня, а не пустота. Прежний почти чёрный
        // (0.09,0.10,0.12) задавал тон всему кадру: любой предмет на его фоне
        // читался как тёмное фэнтези, сколько бы цвета ни было в текстурах.
        public Color Background = new Color(0.42f, 0.68f, 0.78f);

        [Header("Звук")]
        // Выключен по решению владельца 30.08.2026: нынешние синтезированные
        // звуки мешают оценивать подачу удара, а не помогают. Компонент не
        // удалён — вся логика событий в нём рабочая и понадобится, когда
        // приедут настоящие сэмплы. Это переключатель, а не удаление.
        [Tooltip("Боевой звук. Сейчас выключен: временные звуки мешают.")]
        public bool CombatSound = false;


        public TickDriver Driver { get; private set; }

        private void Awake()
        {
            // Корень сцены жёстко в начале координат: симуляция считает позиции
            // в мировых, и сдвинутый в редакторе объект развёл бы пол, камеру
            // и тела по разным системам отсчёта.
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // Пол больше не рисуется плоскостью: его кладёт LayoutView
            // по собранной карте Разлома, комната за комнатой.
            Camera cam = BuildCamera();
            BuildLight();

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

            // Съёмочная рига может задать сид и число врагов из командной
            // строки. Сцена при этом не трогается: сохранённый .unity — это
            // состояние проекта, а не параметр отдельного запуска.
            Driver.RunSeed = CaptureRig.HasSeedOverride ? CaptureRig.SeedOverride : RunSeed;
            Driver.EnemyCount = CaptureRig.HasEnemyOverride ? CaptureRig.EnemyOverride : EnemyCount;
            Driver.LogStateHash = LogStateHash;

            sim.AddComponent<ArenaView>();
            if (!CaptureRig.IsVfxShowcase)
            {
                sim.AddComponent<DamageNumbers>();
                sim.AddComponent<CombatJuiceView>();
            }
            sim.AddComponent<PelagVfxController>();
            if (CombatSound) sim.AddComponent<CombatAudio>();
            sim.AddComponent<LayoutView>();
            if (!CaptureRig.IsVfxShowcase)
            {
                sim.AddComponent<CombatIndicators>();
                sim.AddComponent<HealthBars>();
                sim.AddComponent<PlayerHud>();
                sim.AddComponent<RunHud>();
                sim.AddComponent<CampHud>();
            }

            sim.SetActive(true);

            // Камера цепляется после SetActive: до него TickDriver.Awake
            // ещё не отработал, и симуляции, за которой следить, не существует.
            CameraFollow follow = gameObject.AddComponent<CameraFollow>();
            follow.Driver = Driver;
            follow.Target = cam.transform;
        }

        private Camera BuildCamera()
        {
            GameObject go = new GameObject("Камера");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(CameraAngles);
            go.tag = "MainCamera"; // DamageNumbers разворачивает цифры по Camera.main

            Camera cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;

            // Камера отодвигается назад по своему forward. При ортографии это
            // не меняет масштаб, но убирает арену из зоны отсечения; дальняя
            // плоскость поэтому считается от того же отступа.
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = CameraDistance * 3f;
            go.transform.position = -go.transform.forward * CameraDistance;

            go.AddComponent<AudioListener>();
            go.AddComponent<CombatCameraJuice>();
            return cam;
        }

        private void BuildLight()
        {
            // Ambient оставляет цвет в тени, но больше не заливает всю модель
            // одинаковым бежевым. Форму задаёт заметный боковой тёплый ключ.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.42f;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.49f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.31f, 0.33f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.16f, 0.16f);

            GameObject go = new GameObject("Свет: ключевой");
            go.transform.SetParent(transform, false);
            // Азимут намеренно далеко от 45° камеры: тени уходят поперёк пола,
            // поэтому освещение видно в изометрии, а не прячется за моделями.
            go.transform.rotation = Quaternion.Euler(46f, -118f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.84f, 0.66f);
            light.intensity = 2.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            light.shadowBias = 0.045f;
            light.shadowNormalBias = 0.20f;
            RenderSettings.sun = light;

            GameObject rimObject = new GameObject("Свет: контровой");
            rimObject.transform.SetParent(transform, false);
            rimObject.transform.rotation = Quaternion.Euler(42f, 62f, 0f);
            // Дополнительные Directional нужны только как лёгкая поддержка для
            // URP/Lit-окружения. Персонажи формуются главным светом.
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.56f, 0.72f, 1.00f);
            rim.intensity = 0.26f;
            rim.shadows = LightShadows.None;

            GameObject fillObject = new GameObject("Свет: заполняющий");
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.rotation = Quaternion.Euler(58f, -18f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.92f, 0.86f, 0.78f);
            fill.intensity = 0.14f;
            fill.shadows = LightShadows.None;
        }
    }
}
