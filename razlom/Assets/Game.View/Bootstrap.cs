using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        // За границей комнаты видна светлая холодная дымка. Она поддерживает
        // яркую сказочную палитру, но остаётся холоднее песочной арены.
        public Color Background = new Color(0.46f, 0.70f, 0.80f);

        [Header("Звук")]
        // Синтетический прототип полностью заменён короткими CC0 one-shot.
        // Их слои привязаны только к подтверждённым событиям симуляции.
        [Tooltip("Слоёный боевой звук из импортированных CC0-сэмплов.")]
        public bool CombatSound = true;


        public TickDriver Driver { get; private set; }
        private VolumeProfile _runtimeLookProfile;

        private void Awake()
        {
            // В сцене может остаться сериализованный цвет от прежнего look-pass.
            // Кодовая сцена владеет палитрой, чтобы Editor и player build не расходились.
            Background = new Color(0.46f, 0.70f, 0.80f, 1f);

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
            BuildPostProcessing(cam);

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
            cam.allowHDR = true;

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

        private void BuildPostProcessing(Camera cam)
        {
            UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.stopNaN = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            // Профиль создаётся вместе с кодовой сценой. Он не зависит от того,
            // сохранился ли Volume в конкретном .unity, и одинаков в Editor и build.
            _runtimeLookProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeLookProfile.name = "Razlom Runtime Combat Look";

            Bloom bloom = _runtimeLookProfile.Add<Bloom>(true);
            // Bloom остаётся за боевыми HDR-пиками, а не заливает ореолом
            // светлый пол. В толпе пик удара поэтому читается отдельно.
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.36f);
            bloom.scatter.Override(0.54f);
            bloom.highQualityFiltering.Override(true);

            Tonemapping tonemapping = _runtimeLookProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            ColorAdjustments color = _runtimeLookProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.30f);
            color.contrast.Override(8f);
            color.saturation.Override(16f);
            color.colorFilter.Override(new Color(1.00f, 0.99f, 0.96f, 1f));

            Vignette vignette = _runtimeLookProfile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.12f, 0.25f, 0.32f, 1f));
            vignette.intensity.Override(0.075f);
            vignette.smoothness.Override(0.58f);

            GameObject volumeObject = new GameObject("Свет: глобальный цвет и bloom");
            volumeObject.transform.SetParent(transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = _runtimeLookProfile;
        }

        private void BuildLight()
        {
            // Светлая трёхцветная среда сохраняет раскраску даже на теневой
            // стороне. Форму по-прежнему задаёт боковой тёплый ключ, а не плоская эмиссия.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.74f;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.79f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.55f, 0.59f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.25f);
            RenderSettings.reflectionIntensity = 0.82f;

            GameObject go = new GameObject("Свет: ключевой");
            go.transform.SetParent(transform, false);
            // Азимут намеренно далеко от 45° камеры: тени уходят поперёк пола,
            // поэтому освещение видно в изометрии, а не прячется за моделями.
            go.transform.rotation = Quaternion.Euler(46f, -118f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.92f, 0.78f);
            light.intensity = 1.72f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.58f;
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
            rim.color = new Color(0.52f, 0.78f, 1.00f);
            rim.intensity = 0.42f;
            rim.shadows = LightShadows.None;

            GameObject fillObject = new GameObject("Свет: заполняющий");
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.rotation = Quaternion.Euler(58f, -18f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(1.00f, 0.82f, 0.62f);
            fill.intensity = 0.24f;
            fill.shadows = LightShadows.None;
        }

        private void OnDestroy()
        {
            if (_runtimeLookProfile != null) Destroy(_runtimeLookProfile);
        }
    }
}
