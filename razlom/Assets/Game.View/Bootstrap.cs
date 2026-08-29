using UnityEngine;

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
        public Vector3 CameraAngles = new Vector3(30f, 45f, 0f);
        public float CameraSize = 10.5f;
        [Tooltip("Отступ камеры назад по своему forward. На ортографии не влияет на масштаб, только на отсечение.")]
        public float CameraDistance = 60f;
        public Color Background = new Color(0.09f, 0.10f, 0.12f);


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
            Driver.RunSeed = RunSeed;
            Driver.EnemyCount = EnemyCount;
            Driver.LogStateHash = LogStateHash;

            sim.AddComponent<ArenaView>();
            sim.AddComponent<DamageNumbers>();
            sim.AddComponent<CombatJuiceView>();
            sim.AddComponent<CombatAudio>();
            sim.AddComponent<CombatIndicators>();
            sim.AddComponent<HealthBars>();
            sim.AddComponent<PlayerHud>();
            sim.AddComponent<LayoutView>();
            sim.AddComponent<RunHud>();
            sim.AddComponent<CampHud>();

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
            GameObject go = new GameObject("Свет");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }
    }
}
