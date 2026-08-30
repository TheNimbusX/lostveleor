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
            // Заполняющий свет ЯРКИЙ. В мультяшной картинке неосвещённая
            // сторона обязана оставаться цветной: там, где заполнение почти
            // чёрное, тун-шейдер уводит всю теневую половину фигуры в грязь,
            // и раскраска персонажа перестаёт работать.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.70f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.30f, 0.24f);

            GameObject go = new GameObject("Свет: ключевой");
            go.transform.SetParent(transform, false);
            // Наклон 38°, а не 50°: при крутом свете вертикальные поверхности —
            // то есть почти весь персонаж — получают низкий ndl и проваливаются
            // в теневую полосу. Пологий свет скользит по фигуре и лепит объём.
            go.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.95f, 0.86f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            GameObject rimObject = new GameObject("Свет: контровой");
            rimObject.transform.SetParent(transform, false);
            rimObject.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            // Контровой приглушён и уведён из синевы. На 0.42 он работал как
            // ночной фильтр: холодная плёнка ложилась на всё подряд и красила
            // сцену в синий — ровно то, чего в этой игре быть не должно.
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.60f, 0.80f, 0.92f);
            rim.intensity = 0.18f;
            rim.shadows = LightShadows.None;
        }
    }
}
