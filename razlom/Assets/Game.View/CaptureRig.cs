using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Unity.Profiling;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Снимает кадры из собранного плеера по расписанию и выходит.
    ///
    /// Зачем это нужно: визуальная задача не считается закрытой, пока нет
    /// картинки. Ручной запуск редактора картинку даёт, но её нельзя ни
    /// повторить тем же сидом, ни сравнить с предыдущей — а сравнение «до и
    /// после» и есть единственный честный способ обсуждать внешний вид.
    ///
    /// Рига НЕТ в сцене. Она ставит себя сама и только когда в командной
    /// строке есть -razlom-capture: обычный запуск игры ничего об этом коде
    /// не знает и не платит за него ни кадром.
    ///
    /// Обычный capture симуляцию не меняет. Только изолированный animation/VFX
    /// QA после готовности кадра ставит TickDriver на паузу, чтобы пустая волна
    /// не сменила арену посреди проверяемого клипа.
    /// </summary>
    public sealed class CaptureRig : MonoBehaviour
    {
        private const string EnableFlag = "-razlom-capture";
        private const string OutputFlag = "-capture-out";
        private const string TimesFlag = "-capture-times";
        private const string EnemiesFlag = "-capture-enemies";
        private const string SeedFlag = "-capture-seed";
        private const string WhirlwindFlag = "-capture-whirlwind";
        private const string RunFlag = "-capture-run";
        private const string EquipmentFlag = "-capture-equipment";
        private const string LocomotionFlag = "-capture-locomotion";
        private const string MovingCombatFlag = "-capture-moving-combat";
        private const string VideoFlag = "-capture-video";
        private const string VideoStartFlag = "-capture-video-start";
        private const string VideoDurationFlag = "-capture-video-duration";
        private const string VideoFpsFlag = "-capture-video-fps";
        private const string CameraSizeFlag = "-capture-camera-size";
        private const string SkillFlag = "-capture-skill";
        private const string HitTierFlag = "-capture-hit-tier";
        private const string PauseMenuFlag = "-capture-pause-menu";
        private const string CaptureWidthFlag = "-capture-width";
        private const string CaptureHeightFlag = "-capture-height";
        private const string PerfFlag = "-capture-perf";
        private const string PerfWarmupFlag = "-capture-perf-warmup";
        private const string PerfNoHudFlag = "-capture-perf-nohud";
        private const string QualityFlag = "-capture-quality";
        private const string FrameCapFlag = "-capture-frame-cap";
        private const string WatchTeleportsFlag = "-capture-watch-teleports";

        /// <summary>
        /// Переопределения для <see cref="Bootstrap"/>. Считываются ДО загрузки
        /// сцены, поэтому к моменту Awake бутстрапа они уже проставлены.
        ///
        /// Срез требует 5–8 врагов, а сцена собрана под сорок. Править сцену
        /// ради снимка нельзя: сохранённый .unity — это состояние проекта,
        /// а не параметр запуска.
        /// </summary>
        /// <summary>
        /// Запуск идёт под съёмку: процесс получил -razlom-capture.
        ///
        /// Один признак вместо перебора десятка showcase-флагов. Нужен тем, кто
        /// обязан вести себя иначе в съёмке целиком, а не в конкретном её
        /// режиме: главное меню, например, ждёт нажатия PLAY, а съёмка не
        /// нажимает кнопок и молча записала бы заставку вместо игры.
        /// </summary>
        public static bool Installed { get; private set; }

        /// <summary>
        /// Съёмка главного меню: под -capture-main-menu меню не выключается, а
        /// само нажимает PLAY через три секунды и пишет в лог состояние камер.
        /// Нужна, чтобы увидеть переход меню → игра в настоящем плеере.
        /// </summary>
        public static bool MainMenuCapture { get; private set; }

        public static bool HasEnemyOverride { get; private set; }

        public static int EnemyOverride { get; private set; }

        public static bool HasSeedOverride { get; private set; }

        public static ulong SeedOverride { get; private set; }

        /// <summary>
        /// Съёмка просит «войти в Разлом» — ровно тем же путём, что и клавиша E
        /// у игрока. Симуляция при этом не трогается: команда проходит обычный
        /// защёлкивающий ввод и обычный тик.
        ///
        /// Признак держится постоянно, а не один кадр. Раскладка сама решает,
        /// что «войти» имеет смысл только в лагере, поэтому в бою и на экране
        /// награды постоянный запрос ничего не делает — а забег после возврата
        /// в лагерь начинается снова, что для съёмки как раз и нужно.
        /// </summary>
        public static bool AutoEnterRift { get; private set; }

        public static bool WhirlwindShowcase { get; private set; }
        public static string PoseShowcase { get; private set; }

        public static bool RunShowcase { get; private set; }

        public static bool EquipmentShowcase { get; private set; }
        public static float EquipmentStartedAt { get; private set; } = float.PositiveInfinity;
        public static bool EquipmentReady
        {
            get
            {
                float t = Time.time - EquipmentStartedAt;
                return (t >= 0.2f && t < 2.2f) || (t >= 4.2f && t < 4.55f)
                    || (t >= 4.75f && t < 7f) || (t >= 8.8f && t < 10.8f);
            }
        }

        public static bool LocomotionShowcase { get; private set; }

        public static bool MovingCombatShowcase { get; private set; }
        public static int MovingCombatDelay { get; private set; } = 2;

        public static PelagVfxShowcase VfxShowcase { get; private set; }
        public static bool LiveSkill { get; private set; }
        public static int CastYaw { get; private set; }
        public static float CastDistance { get; private set; } = 3f;
        public static int HoldTicks { get; private set; } = 60;
        private static bool _showHud;
        public static bool PerformanceCapture { get; private set; }
        public static bool TurnDuringSkill { get; private set; }
        public static bool ActiveEnemies { get; private set; }
        private static string _combatEncounter;
        public static bool SweepAimCapture { get; private set; }
        public static bool DeathDuringSkill { get; private set; }

        public static CombatFeelCaptureTier CombatFeelTier { get; private set; }
        public static bool IsCombatFeelShowcase => CombatFeelTier != CombatFeelCaptureTier.None;
        public static bool GcWarmupActive { get; private set; }

        /// <summary>Съёмка просит TickDriver жаловаться на скачки тел.</summary>
        public static bool WatchTeleports { get; private set; }

        /// <summary>Служебный запуск UI-QA: открыть системное меню после кадра.</summary>
        public static bool PauseMenuCaptureRequested { get; private set; }
        public static string PauseMenuCapturePage { get; private set; }

        public static bool IsVfxShowcase => VfxShowcase != PelagVfxShowcase.None || WhirlwindShowcase;

        private static readonly int[] WhirlwindCastTicks = { 18, 54, 90 };
        private static int _nextWhirlwindCast;
        private static int _whirlwindStartedTick = -1;

        /// <summary>Три capture-нажатия первого слота, привязанные к sim tick.</summary>
        public static bool ShouldCastWhirlwind(int simTick)
        {
            // Флаг Whirlwind также даёт locomotion-capture стабильную боевую
            // расстановку. Во время RunShowcase способность не жмём: иначе
            // она обрывает тот самый gait-cycle, который мы проверяем.
            if (!WhirlwindShowcase || RunShowcase || LocomotionShowcase || simTick < 0) return false;
            if (GcWarmupActive) { _whirlwindStartedTick = -1; return false; }
            if (_whirlwindStartedTick < 0) _whirlwindStartedTick = simTick;
            if (_nextWhirlwindCast >= WhirlwindCastTicks.Length) return false;
            if (simTick - _whirlwindStartedTick < WhirlwindCastTicks[_nextWhirlwindCast]) return false;
            _nextWhirlwindCast++;
            return true;
        }

        private string _outputDirectory;
        private float[] _marks;
        private bool _recordVideo;
        private float _videoStart;
        private float _videoEnd;
        private int _videoFps;
        private int _videoFrame;
        private CombatAudioCapture _audioCapture;
        private int _timelineFrame;
        private float _cameraSize;
        private float _cameraYaw;
        private int _captureWidth;
        private int _captureHeight;
        private ProfilerRecorder _gcRecorder;
        private long _gcTotal;
        private long _gcMax;
        private int _gcSamples;
        private float _perfSeconds;
        private int _perfWarmupFrames;
        private bool _perfNoHud;
        private bool _hasFrameCapOverride;
        private bool IsPerfRun => _perfSeconds > 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, EnableFlag) < 0) return;
            Installed = true;

            string output = ReadValue(args, OutputFlag) ?? "capture";
            float[] marks = ParseMarks(ReadValue(args, TimesFlag));

            WhirlwindShowcase = Array.IndexOf(args, WhirlwindFlag) >= 0;
            PoseShowcase = ReadValue(args, "-capture-pose");
            RunShowcase = Array.IndexOf(args, RunFlag) >= 0;
            WatchTeleports = Array.IndexOf(args, WatchTeleportsFlag) >= 0;
            EquipmentShowcase = Array.IndexOf(args, EquipmentFlag) >= 0;
            LocomotionShowcase = Array.IndexOf(args, LocomotionFlag) >= 0;
            MovingCombatShowcase = Array.IndexOf(args, MovingCombatFlag) >= 0;
            MovingCombatDelay = Mathf.Clamp(ReadInt(args, "-capture-moving-combat-delay", 2), 1, 24);
            VfxShowcase = ParseShowcase(ReadValue(args, SkillFlag));
            LiveSkill = Array.IndexOf(args, "-capture-live-skill") >= 0;
            PerformanceCapture = ReadFloat(args, PerfFlag, 0f) > 0f;
            CastYaw = ReadInt(args, "-capture-cast-yaw", 0);
            CastDistance = Mathf.Clamp(ReadFloat(args, "-capture-cast-distance", 3f), .5f, 7f);
            HoldTicks = Mathf.Clamp(ReadInt(args, "-capture-hold-ticks", 60), 1, 60);
            _showHud = Array.IndexOf(args, "-capture-hud") >= 0;
            TurnDuringSkill = Array.IndexOf(args, "-capture-turn-during-skill") >= 0;
            ActiveEnemies = Array.IndexOf(args, "-capture-active-enemies") >= 0;
            _combatEncounter = ReadValue(args, "-capture-encounter");
            SweepAimCapture = Array.IndexOf(args, "-capture-sweep-aim") >= 0;
            DeathDuringSkill = Array.IndexOf(args, "-capture-death-during-skill") >= 0;
            CombatFeelTier = ParseHitTier(ReadValue(args, HitTierFlag));
            PauseMenuCaptureRequested = Array.IndexOf(args, PauseMenuFlag) >= 0;
            MainMenuCapture = Array.IndexOf(args, "-capture-main-menu") >= 0;
            PauseMenuCapturePage = ReadValue(args, PauseMenuFlag);
            if ((MovingCombatShowcase || LiveSkill) && CombatFeelTier == CombatFeelCaptureTier.None)
                CombatFeelTier = CombatFeelCaptureTier.Normal;
            // Input polling starts before the capture coroutine reaches its
            // explicit warmup. Gate combat immediately, otherwise an attack
            // can begin during scene startup and contaminate frame zero.
            GcWarmupActive = IsCombatFeelShowcase || WhirlwindShowcase;
            _nextWhirlwindCast = 0;
            _whirlwindStartedTick = -1;

            bool recordVideo = Array.IndexOf(args, VideoFlag) >= 0;
            float videoStart = ReadFloat(args, VideoStartFlag, 0.4f);
            float videoDuration = ReadFloat(args, VideoDurationFlag, 3.9f);
            int videoFps = Mathf.Clamp(ReadInt(args, VideoFpsFlag, 60), 1, 120);

            string enemies = ReadValue(args, EnemiesFlag);
            if (int.TryParse(enemies, NumberStyles.Integer, CultureInfo.InvariantCulture, out int enemyCount))
            {
                HasEnemyOverride = true;
                EnemyOverride = enemyCount;
            }

            string seed = ReadValue(args, SeedFlag);
            if (ulong.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seedValue))
            {
                HasSeedOverride = true;
                SeedOverride = seedValue;
            }

            AutoEnterRift = Array.IndexOf(args, "-capture-camp") < 0;

            // Объект переживает загрузку сцены: расписание отсчитывается от
            // старта процесса, а не от того, какая сцена сейчас открыта.
            GameObject host = new GameObject("Razlom Capture Rig");
            DontDestroyOnLoad(host);

            CaptureRig rig = host.AddComponent<CaptureRig>();
            rig._outputDirectory = output;
            rig._marks = marks;
            rig._recordVideo = recordVideo;
            rig._videoStart = videoStart;
            rig._videoEnd = videoStart + videoDuration;
            rig._videoFps = videoFps;
            rig._cameraSize = ReadFloat(args, CameraSizeFlag, 0f);
            rig._cameraYaw = ReadFloat(args, "-capture-camera-yaw", 0f);
            rig._captureWidth = Mathf.Max(1, ReadInt(args, CaptureWidthFlag, 1920));
            rig._captureHeight = Mathf.Max(1, ReadInt(args, CaptureHeightFlag, 1080));
            rig._perfSeconds = Mathf.Max(0f, ReadFloat(args, PerfFlag, 0f));
            rig._perfWarmupFrames = Mathf.Max(0, ReadInt(args, PerfWarmupFlag, 240));
            rig._perfNoHud = Array.IndexOf(args, PerfNoHudFlag) >= 0;

            // Пресет качества задаётся ДО загрузки сцены и через тот же путь,
            // что и выбор игрока: замер должен мерить настройку из меню, а не
            // отдельную ветку кода, живущую только в съёмке.
            string quality = ReadValue(args, QualityFlag);
            if (!string.IsNullOrEmpty(quality))
            {
                if (string.Equals(quality, "low", StringComparison.OrdinalIgnoreCase))
                    GameUserSettings.OverrideQuality(GameUserSettings.QualityLevel.Low);
                else if (string.Equals(quality, "medium", StringComparison.OrdinalIgnoreCase))
                    GameUserSettings.OverrideQuality(GameUserSettings.QualityLevel.Medium);
                else if (string.Equals(quality, "high", StringComparison.OrdinalIgnoreCase))
                    GameUserSettings.OverrideQuality(GameUserSettings.QualityLevel.High);
            }

            // Замер С потолком: проверяется не «движок принял число», а держит
            // ли игра этот потолок ровно. Без флага замер снимает потолок сам.
            rig._hasFrameCapOverride = Array.IndexOf(args, FrameCapFlag) >= 0;
            if (rig._hasFrameCapOverride)
                GameUserSettings.OverrideFrameCap(ReadInt(args, FrameCapFlag, 60));
        }

        private void Start()
        {
            Directory.CreateDirectory(_outputDirectory);
            if (IsCombatFeelShowcase || WhirlwindShowcase || VfxShowcase != PelagVfxShowcase.None)
                gameObject.AddComponent<PelagAttackCapture>().Initialize(_outputDirectory);
            if (RunShowcase || LocomotionShowcase || WhirlwindShowcase || MovingCombatShowcase || VfxShowcase != PelagVfxShowcase.None)
                gameObject.AddComponent<PelagLocomotionCapture>().Initialize(_outputDirectory);
            if (_recordVideo)
            {
                Directory.CreateDirectory(Path.Combine(_outputDirectory, "video_frames"));
                Time.captureFramerate = _videoFps;
            }
            else if (WhirlwindShowcase || MovingCombatShowcase || VfxShowcase != PelagVfxShowcase.None || !string.IsNullOrEmpty(PoseShowcase))
                Time.captureFramerate = 60;
            // Финальная проверка идёт с настоящим dt; фиксированный шаг оставлен для разбора поз.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-real-time") >= 0)
                Time.captureFramerate = 0;
            if (IsCombatFeelShowcase)
                _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory,
                    "GC Allocated In Frame", 1);
            StartCoroutine(Run());
        }

        private void OnDestroy() { _audioCapture?.Dispose(); _audioCapture = null; }

        private IEnumerator Run()
        {
            if (!IsPerfRun && IsCombatFeelShowcase)
                gameObject.AddComponent<CombatPresentationCapture>().Initialize(_outputDirectory);
            // Splash и первая загрузка FBX занимают разное время на разных
            // машинах. Отсчёт начинается только когда Rift и реальная сабля
            // уже привязаны — иначе расписание снимает заставку вместо боя.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp") >= 0)
            {
                while (CampPlayerView.Instance == null || CampPlayerView.Instance.Body == null) yield return null;
                gameObject.AddComponent<CampWalkCapture>();
            }
            else while (!CombatViewReady()) yield return null;
            if (ActiveEnemies && IsCombatFeelShowcase)
            {
                TickDriver driver = FindAnyObjectByType<TickDriver>();
                driver.Sim.SetupCombatFeelShowcase(driver.Run.Map, EnemyOverride, CombatFeelTier, true, IsPerfRun);
                Debug.Log("[capture-combat] Живых врагов: " + (driver.Sim.Entities.Count - 1));
                yield return null;
            }
            if (!string.IsNullOrEmpty(_combatEncounter) && IsCombatFeelShowcase)
            {
                CombatCaptureEncounter.Configure(FindAnyObjectByType<TickDriver>(), _combatEncounter,
                    EnemyOverride, CombatFeelTier, ActiveEnemies);
                yield return null;
            }
            ConfigureCaptureView();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-no-vfx") >= 0)
            {
                var effects = FindAnyObjectByType<PelagVfxController>();
                if (effects != null) effects.enabled = false;
                var juice = FindAnyObjectByType<CombatJuiceView>();
                if (juice != null) juice.enabled = false;
            }

            if (EquipmentShowcase && !RunShowcase && !MovingCombatShowcase)
            {
                TickDriver driver = FindAnyObjectByType<TickDriver>();
                if (driver != null) driver.enabled = false;
            }

            if (!string.IsNullOrEmpty(PoseShowcase))
            {
                TickDriver driver = FindAnyObjectByType<TickDriver>();
                if (driver != null) driver.enabled = false;
                ArenaView arena = FindAnyObjectByType<ArenaView>();
                arena.SetPlayerCombatReady(PoseShowcase != "idle");
                yield return null;
                if (PoseShowcase == "death" && arena.TryGetEntityView(Simulation.PlayerId, out Transform body))
                    body.GetComponent<CharacterAnimatorView>().PlayDeath();
            }

            if (IsPerfRun)
            {
                yield return MeasurePerformance();
                Application.Quit();
                yield break;
            }

            PelagVfxController vfx = FindAnyObjectByType<PelagVfxController>();
            if (vfx != null && VfxShowcase != PelagVfxShowcase.None && !LiveSkill)
            {
                // Дать толпе подойти в читаемую дистанцию; отсчёт видео ещё
                // не начался, поэтому в ролик ожидание не попадает. Нулевой
                // enemy override — специальный чистый animation QA: ждать в
                // нём нельзя, иначе пустой Разлом успевает перелистнуть глубину
                // и к первому кадру снова окружает Пелага новой толпой.
                if (!HasEnemyOverride || EnemyOverride > 0)
                    yield return new WaitForSecondsRealtime(1.25f);
                else
                {
                    // Изолированный animation/VFX QA должен оставаться в той
                    // же глубине Разлома. Без этого пустая волна мгновенно
                    // завершается и следующий кадр снова заполняет арену.
                    // Останавливаем только игровой тик уже собранного capture-
                    // player; Animator и presentation продолжают жить в Update.
                    TickDriver tickDriver = FindAnyObjectByType<TickDriver>();
                    if (tickDriver != null) tickDriver.enabled = false;
                    yield return null;
                }
                vfx.BeginShowcase(VfxShowcase);
            }

            if (IsCombatFeelShowcase || WhirlwindShowcase)
            {
                GcWarmupActive = true;
                for (int i = 0; i < 120; i++)
                {
                    yield return new WaitForEndOfFrame();
                    SampleGc();
                }
                GcWarmupActive = false;
                Debug.Log($"[capture-gc] samples={_gcSamples}, total={_gcTotal}, max={_gcMax}, " +
                          $"average={(_gcSamples > 0 ? _gcTotal / _gcSamples : 0)} bytes/frame");
            }
            if (EquipmentShowcase)
            {
                EquipmentStartedAt = Time.time;
                gameObject.AddComponent<PelagEquipmentCapture>().Initialize(_outputDirectory);
            }
            // Fixed-step animation captures use the animation clock. PNG IO
            // must not advance the screenshot schedule past the next pose.
            bool animationClock = !_recordVideo && Time.captureFramerate > 0;
            float combatStartedAt = animationClock ? Time.time : Time.unscaledTime;

            if (_recordVideo && Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-silent-video") < 0)
                _audioCapture = new CombatAudioCapture(_outputDirectory);
            int mark = 0;
            float lastMark = _marks.Length > 0 ? _marks[_marks.Length - 1] : 0f;
            float finish = Mathf.Max(lastMark, _recordVideo ? _videoEnd : 0f);

            while ((_recordVideo
                        ? _timelineFrame / (float)_videoFps
                        : (animationClock ? Time.time : Time.unscaledTime) - combatStartedAt) < finish
                   || mark < _marks.Length)
            {
                yield return new WaitForEndOfFrame();

                float now = _recordVideo
                    ? _timelineFrame++ / (float)_videoFps
                    : (animationClock ? Time.time : Time.unscaledTime) - combatStartedAt;
                bool videoFrame = _recordVideo && now >= _videoStart && now < _videoEnd;
                _audioCapture?.Frame(videoFrame);
                if (videoFrame) CaptureVideoFrame();

                while (mark < _marks.Length && now >= _marks[mark])
                {
                    CaptureStill(mark, _marks[mark]);
                    mark++;
                }
            }

            _audioCapture?.Dispose();
            _audioCapture = null;
            if (_recordVideo) Time.captureFramerate = 0;
            if (_gcRecorder.Valid) _gcRecorder.Dispose();
            Application.Quit();
        }

        /// <summary>
        /// Замер кадра на фиксированном сиде и фиксированном числе врагов.
        ///
        /// Существует по той же причине, что и съёмка кадров: «стало быстрее» —
        /// это утверждение, которое либо подтверждается двумя одинаковыми
        /// прогонами, либо не значит ничего. Правило проекта прямое: не
        /// оптимизируй без замера.
        ///
        /// Меряется ВРЕМЯ КАДРА, а не FPS: среднее FPS прячет ровно то, что
        /// портит ощущение — редкие длинные кадры. Отсюда перцентили и максимум.
        /// </summary>
        private IEnumerator MeasurePerformance()
        {
            // Замер идёт без потолка: любой cap измерял бы сам себя. Но если
            // потолок задан явно, мы как раз его и проверяем — тогда не трогаем.
            Time.captureFramerate = 0;
            if (!_hasFrameCapOverride)
            {
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
            }

            // Второй прогон с выключенным HUD нужен, чтобы РАЗДЕЛИТЬ мусор
            // кадра: IMGUI выделяет память на каждый Label, и без этой пары
            // цифр нельзя сказать, чей это мусор — боя или интерфейса.
            if (_perfNoHud)
            {
                PlayerHud playerHud = FindAnyObjectByType<PlayerHud>();
                RunHud runHud = FindAnyObjectByType<RunHud>();
                CampHud campHud = FindAnyObjectByType<CampHud>();
                if (playerHud != null) playerHud.enabled = false;
                if (runHud != null) runHud.enabled = false;
                if (campHud != null) campHud.enabled = false;
            }

            // Не `using`: C# запрещает yield return внутри try/finally, а
            // `using` разворачивается именно в него. Освобождаем руками ниже.
            ProfilerRecorder gc = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            // Имена счётчиков отрисовки в URP разошлись между версиями Unity:
            // «Batches Count» в этой сборке не существует и молча отдаёт ноль.
            // Берём первое имя, которое реально нашлось.
            ProfilerRecorder drawCalls = StartFirstValid(ProfilerCategory.Render,
                "Draw Calls Count", "Total Draw Calls Count");
            ProfilerRecorder setPass = StartFirstValid(ProfilerCategory.Render,
                "SetPass Calls Count");
            ProfilerRecorder batches = StartFirstValid(ProfilerCategory.Render,
                "Total Batches Count", "Batches Count", "Dynamic Batched Draw Calls Count");
            ProfilerRecorder triangles = StartFirstValid(ProfilerCategory.Render,
                "Triangles Count");

            // Прогрев: первые кадры платят за компиляцию шейдеров, загрузку
            // атласов и первый рост пулов. Считать их — значит мерить загрузку.
            var endOfFrame = new WaitForEndOfFrame();
            var frames = new List<float>(65536);
            for (int i = 0; i < _perfWarmupFrames; i++) yield return endOfFrame;
            GcWarmupActive = false;

            long gcTotal = 0;
            long gcMax = 0;
            long drawSum = 0, setPassSum = 0, batchSum = 0, triangleSum = 0;
            int samples = 0;
            // Врагов считаем КАЖДЫЙ кадр, а не один раз в конце: за окно замера
            // толпа успевает поредеть, и число «на выходе» соврало бы про то,
            // при какой нагрузке получены миллисекунды.
            TickDriver driver = FindAnyObjectByType<TickDriver>();
            int enemyMin = int.MaxValue, enemyMax = 0;
            long enemySum = 0;
            float started = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - started < _perfSeconds)
            {
                yield return endOfFrame;

                Simulation live = driver != null ? driver.Sim : null;
                int alive = live != null ? live.CountAliveEnemies() : 0;
                if (alive < enemyMin) enemyMin = alive;
                if (alive > enemyMax) enemyMax = alive;
                enemySum += alive;

                frames.Add(Time.unscaledDeltaTime * 1000f);
                if (gc.Valid)
                {
                    long value = gc.LastValue;
                    gcTotal += value;
                    if (value > gcMax) gcMax = value;
                }
                if (drawCalls.Valid) drawSum += drawCalls.LastValue;
                if (setPass.Valid) setPassSum += setPass.LastValue;
                if (batches.Valid) batchSum += batches.LastValue;
                if (triangles.Valid) triangleSum += triangles.LastValue;
                samples++;
            }

            long gcAverage = samples > 0 ? gcTotal / samples : 0;
            long drawAverage = samples > 0 ? drawSum / samples : 0;
            long setPassAverage = samples > 0 ? setPassSum / samples : 0;
            long batchAverage = samples > 0 ? batchSum / samples : 0;
            long triangleAverage = samples > 0 ? triangleSum / samples : 0;
            if (gc.Valid) gc.Dispose();
            if (drawCalls.Valid) drawCalls.Dispose();
            if (setPass.Valid) setPass.Dispose();
            if (batches.Valid) batches.Dispose();
            if (triangles.Valid) triangles.Dispose();

            if (samples == 0)
            {
                Debug.Log("[perf] {\"error\":\"no samples\"}");
                yield break;
            }

            frames.Sort();
            float average = 0f;
            for (int i = 0; i < frames.Count; i++) average += frames[i];
            average /= frames.Count;

            UnityEngine.Rendering.RenderPipelineAsset pipeline =
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;

            // Одна строка JSON: её разбирает capture.ps1, и её же можно
            // сравнить глазами между двумя прогонами без всякого инструмента.
            Debug.Log("[perf] {"
                      + $"\"frames\":{samples},"
                      + $"\"enemies_avg\":{enemySum / samples},"
                      + $"\"enemies_min\":{(enemyMin == int.MaxValue ? 0 : enemyMin)},"
                      + $"\"enemies_max\":{enemyMax},"
                      + $"\"screen\":\"{Screen.width}x{Screen.height}\","
                      + $"\"quality\":\"{GameUserSettings.Quality}\","
                      + $"\"frame_cap\":{GameUserSettings.FrameCap},"
                      + $"\"pipeline\":\"{(pipeline != null ? pipeline.name : "builtin")}\","
                      + $"\"ms_avg\":{average.ToString("0.000", CultureInfo.InvariantCulture)},"
                      + $"\"ms_p50\":{Percentile(frames, 0.50f).ToString("0.000", CultureInfo.InvariantCulture)},"
                      + $"\"ms_p95\":{Percentile(frames, 0.95f).ToString("0.000", CultureInfo.InvariantCulture)},"
                      + $"\"ms_p99\":{Percentile(frames, 0.99f).ToString("0.000", CultureInfo.InvariantCulture)},"
                      + $"\"ms_max\":{frames[frames.Count - 1].ToString("0.000", CultureInfo.InvariantCulture)},"
                      + $"\"fps_avg\":{(1000f / average).ToString("0.0", CultureInfo.InvariantCulture)},"
                      + $"\"fps_p95\":{(1000f / Percentile(frames, 0.95f)).ToString("0.0", CultureInfo.InvariantCulture)},"
                      + $"\"gc_bytes_avg\":{gcAverage},"
                      + $"\"gc_bytes_max\":{gcMax},"
                      + $"\"draw_calls\":{drawAverage},"
                      + $"\"setpass\":{setPassAverage},"
                      + $"\"batches\":{batchAverage},"
                      + $"\"triangles\":{triangleAverage}"
                      + "}");
        }

        /// <summary>Первый счётчик из списка, который в этой сборке существует.</summary>
        private static ProfilerRecorder StartFirstValid(ProfilerCategory category,
            params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, names[i], 1);
                if (recorder.Valid) return recorder;
                recorder.Dispose();
            }
            return default;
        }

        /// <summary>Перцентиль по уже отсортированному списку.</summary>
        private static float Percentile(List<float> sorted, float fraction)
        {
            if (sorted.Count == 0) return 0f;
            int index = Mathf.Clamp(
                Mathf.RoundToInt(fraction * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[index];
        }

        private void ConfigureCaptureView()
        {
            if (Mathf.Abs(_cameraYaw) > 0.01f && Camera.main != null)
            {
                var orbitArena = FindAnyObjectByType<ArenaView>();
                if (orbitArena != null && orbitArena.TryGetEntityView(Simulation.PlayerId, out Transform body))
                {
                    var follow = FindAnyObjectByType<CameraFollow>();
                    if (follow != null) follow.enabled = false;
                    var cameraJuice = Camera.main.GetComponent<CombatCameraJuice>();
                    if (cameraJuice != null) cameraJuice.enabled = false;
                    Vector3 pivot = body.position + Vector3.up * 0.8f;
                    var driver = FindAnyObjectByType<TickDriver>();
                    if (driver != null && driver.Sim != null)
                    {
                        // Тело уже создано, но его первый LateUpdate ещё мог
                        // не перенести prefab из нуля к позиции симуляции.
                        var position = driver.Sim.Entities.Position[Simulation.PlayerId];
                        pivot = new Vector3(position.X.ToFloat(), 0.8f, position.Y.ToFloat());
                    }
                    Quaternion orbit = Quaternion.AngleAxis(_cameraYaw, Vector3.up);
                    Camera.main.transform.position = pivot + orbit * (Camera.main.transform.position - pivot);
                    // После отключения follow его прежняя точка взгляда может
                    // отставать от героя. Орбита всегда смотрит в новый центр.
                    Camera.main.transform.LookAt(pivot, Vector3.up);
                }
            }
            if (_cameraSize > 0f && Camera.main != null && Camera.main.orthographic)
            {
                Camera.main.orthographicSize = _cameraSize;
                // CombatCameraJuice restores its cached base size every
                // LateUpdate; update that cache too or the close-up lasts only
                // until the first captured frame.
                CombatCameraJuice juice = Camera.main.GetComponent<CombatCameraJuice>();
                if (juice != null) juice.SetBaseOrthographicSize(_cameraSize);
            }

            if (IsCombatFeelShowcase && !IsPerfRun && !_showHud)
            {
                PlayerHud playerHud = FindAnyObjectByType<PlayerHud>();
                RunHud runHud = FindAnyObjectByType<RunHud>();
                CampHud campHud = FindAnyObjectByType<CampHud>();
                if (playerHud != null) playerHud.enabled = false;
                if (runHud != null) runHud.enabled = false;
                if (campHud != null) campHud.enabled = false;
            }

            ArenaView arena = FindAnyObjectByType<ArenaView>();
            if (arena == null || !arena.TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip))
                return;

            Renderer renderer = bladeRoot.GetComponentInParent<Renderer>();
            Transform saber = bladeRoot;
            while (saber != null && saber.name != "Pelag_FantasySaber_Equipped")
                saber = saber.parent;
            Vector3 cuttingEdge = saber != null
                ? saber.TransformDirection(Vector3.right).normalized
                : Vector3.zero;
            Transform socket = saber != null ? saber.parent : null;
            string socketBasis = socket == null
                ? "socket=none"
                : $"socketRight={socket.right}, socketUp={socket.up}, socketForward={socket.forward}";
            string rendererState = renderer == null
                ? "renderer=none"
                : $"renderer={renderer.name}, enabled={renderer.enabled}, active={renderer.gameObject.activeInHierarchy}, " +
                  $"boundsCenter={renderer.bounds.center}, boundsSize={renderer.bounds.size}, " +
                  $"shader={(renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "none")}";
            Debug.Log($"[capture-grip] root={bladeRoot.position}, tip={bladeTip.position}, " +
                      $"length={Vector3.Distance(bladeRoot.position, bladeTip.position):0.000}, " +
                      $"edge={cuttingEdge}, edgeDown={Vector3.Dot(cuttingEdge, Vector3.down):0.000}, " +
                      $"{socketBasis}, {rendererState}");
        }

        private static bool CombatViewReady()
        {
            ArenaView arena = FindAnyObjectByType<ArenaView>();
            PelagVfxController vfx = FindAnyObjectByType<PelagVfxController>();
            return arena != null && arena.TryGetPlayerBlade(out _, out _)
                   && vfx != null && vfx.PoolsReady;
        }

        private static PelagVfxShowcase ParseShowcase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return PelagVfxShowcase.None;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "autoattack": return PelagVfxShowcase.Autoattack;
                case "whirlwind": return PelagVfxShowcase.Whirlwind;
                case "anchor-leap": return PelagVfxShowcase.AnchorLeap;
                case "chain-cyclone":
                case "anchor-slam":
                case "anchor-sweep": return PelagVfxShowcase.AnchorSweep;
                case "squall":
                case "chain-step": return PelagVfxShowcase.ChainStep;
                case "cleave": return PelagVfxShowcase.Cleave;
                case "rotation": return PelagVfxShowcase.Rotation;
                default:
                    Debug.LogWarning("[capture] Неизвестный VFX showcase: " + raw);
                    return PelagVfxShowcase.None;
            }
        }

        private static CombatFeelCaptureTier ParseHitTier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CombatFeelCaptureTier.None;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "normal": return CombatFeelCaptureTier.Normal;
                case "crit":
                case "critical": return CombatFeelCaptureTier.Critical;
                case "kill": return CombatFeelCaptureTier.Kill;
                default:
                    Debug.LogWarning("[capture] Unknown combat-feel tier: " + raw);
                    return CombatFeelCaptureTier.None;
            }
        }

        private static bool _linesDumped;

        /// <summary>
        /// Разовый список включённых линий и следов в сцене.
        ///
        /// На всех съёмках в углу кадра висит тонкая линия, которой владелец не
        /// видит в редакторе. Искать её перебором кода бесполезно: быстрее
        /// спросить сцену, кто вообще сейчас рисует линию и откуда докуда.
        /// </summary>
        private static void DumpSceneLines()
        {
            if (_linesDumped) return;
            _linesDumped = true;
            foreach (var line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
            {
                if (!line.enabled || line.positionCount == 0) continue;
                Debug.Log($"[capture-lines] LineRenderer «{line.name}» путь=«{FullPath(line.transform)}» " +
                          $"точек={line.positionCount} мир={line.useWorldSpace} " +
                          $"от={line.GetPosition(0)} до={line.GetPosition(line.positionCount - 1)}");
            }
            foreach (var trail in FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None))
            {
                if (!trail.enabled) continue;
                Debug.Log($"[capture-lines] TrailRenderer «{trail.name}» путь=«{FullPath(trail.transform)}» " +
                          $"emitting={trail.emitting} позиция={trail.transform.position}");
            }
        }

        private static string FullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private void CaptureStill(int index, float mark)
        {
            DumpSceneLines();
            string path = Path.Combine(_outputDirectory,
                string.Format(CultureInfo.InvariantCulture, "shot_{0:00}_t{1:0.00}s.png", index, mark));
            Texture2D frame = CaptureFrame();
            try
            {
                File.WriteAllBytes(path, frame.EncodeToPNG());
                Debug.Log($"[capture] {path}  {frame.width}x{frame.height}");
            }
            finally
            {
                Destroy(frame);
            }
        }

        private void CaptureVideoFrame()
        {
            string path = Path.Combine(_outputDirectory, "video_frames",
                string.Format(CultureInfo.InvariantCulture, "frame_{0:0000}.jpg", _videoFrame++));
            Texture2D frame = CaptureFrame();
            try
            {
                File.WriteAllBytes(path, frame.EncodeToJPG(92));
            }
            finally
            {
                Destroy(frame);
            }
        }

        private Texture2D CaptureFrame()
        {
            // Camera.Render не содержит IMGUI. Для QA системного меню нужен
            // именно итоговый framebuffer после OnGUI, иначе лог подтвердит
            // открытие экрана, а снимок покажет только арену под ним.
            PauseMenu pauseMenu = FindAnyObjectByType<PauseMenu>();
            if (_showHud || (pauseMenu != null && pauseMenu.IsOpen))
                return ScreenCapture.CaptureScreenshotAsTexture();

            Camera camera = Camera.main;
            if (camera == null) return ScreenCapture.CaptureScreenshotAsTexture();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(
                _captureWidth, _captureHeight, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var frame = new Texture2D(_captureWidth, _captureHeight,
                    TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0f, 0f, _captureWidth, _captureHeight), 0, 0, false);
                frame.Apply(false, false);
                return frame;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void SampleGc()
        {
            if (!_gcRecorder.Valid || _gcRecorder.Count == 0) return;
            long bytes = _gcRecorder.LastValue;
            _gcTotal += bytes;
            if (bytes > _gcMax) _gcMax = bytes;
            _gcSamples++;
        }

        /// <summary>
        /// Значение параметра вида «-флаг значение». Отсутствие флага и флаг
        /// без значения — это одно и то же: нечего читать.
        /// </summary>
        private static string ReadValue(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            if (index < 0 || index + 1 >= args.Length) return null;
            return args[index + 1];
        }

        private static float ReadFloat(string[] args, string flag, float fallback)
        {
            string raw = ReadValue(args, flag);
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;
        }

        private static int ReadInt(string[] args, string flag, int fallback)
        {
            string raw = ReadValue(args, flag);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : fallback;
        }

        /// <summary>
        /// «2,6,10» → секунды от старта процесса. Пустой список означал бы
        /// мгновенный выход без единого снимка, поэтому есть запасное
        /// расписание: пара кадров после того, как бой успел начаться.
        /// </summary>
        private static float[] ParseMarks(string raw)
        {
            var marks = new List<float>();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (string part in raw.Split(','))
                {
                    if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    {
                        marks.Add(value);
                    }
                }
            }

            if (marks.Count == 0)
            {
                marks.Add(3f);
                marks.Add(8f);
            }

            marks.Sort();
            return marks.ToArray();
        }
    }
}
