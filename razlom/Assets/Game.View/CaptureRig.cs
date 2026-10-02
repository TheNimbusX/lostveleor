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
        private const string ReelsFlag = "-capture-reels";
        private const string ReelsZoomFlag = "-capture-reels-zoom";
        /// <summary>Во сколько раз ролик ближе обычного плана: доля обычного ортографического размера.</summary>
        private const float DefaultReelsZoom = .8f;
        /// <summary>Сглаживание камеры ролика (у игры 8): центр схватки скачет на смертях, а не только на шагах героя.</summary>
        private const float ReelsSmoothing = 5f;
        /// <summary>Враги дальше этого радиуса от героя в центр схватки не входят, метры.</summary>
        private const float ReelsFightRadius = 8f;
        private const float ReelsHeroWeight = 2f;
        /// <summary>Насколько центр кадра может уйти от героя: доля половины кадра по каждой оси.</summary>
        private const float ReelsHeroRoom = .45f;

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
        public static int HudTooltipSlot { get; private set; } = -1;

        /// <summary>
        /// -capture-hud-tooltip-detail: подсказка способности снимается как с зажатым Alt (взятые
        /// усиления, «было → стало»). Съёмке без усилений показывать нечего, поэтому флаг включает
        /// первые три усиления у четырёх способностей съёмочного набора через меню разработчика.
        /// </summary>
        public static bool HudTooltipDetail { get; private set; }

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
        /// <summary>
        /// Съёмка удержания Вихря: талант «Удержание» включается как
        /// отладочный, а первый слот держится HoldTicks тиков после каждого
        /// нажатия. Без этого флага удержание в съёмке не показать —
        /// у capture-нажатий нет удержания клавиши.
        /// </summary>
        public static bool WhirlwindHold { get; private set; }
        /// <summary>
        /// -capture-skill-form whirlwind:storm|maelstrom|waves (вид форм Вихря 02.10): стенд
        /// -capture-whirlwind с формой в слоте Вихря. TickDriver ставит её набору забега через
        /// RunLoadout.DebugSetForm — как F8 «Вихрь: форма». Нажатий два (FormCastTicks), у Бури
        /// клавиша зажата все StormHoldTicks от нажатия. Без ключа — None, стенд прежний.
        /// </summary>
        public static PelagForm SkillForm { get; private set; }
        /// <summary>
        /// -capture-whirlwind-ring &lt;м&gt;: мишени стенда Вихря на этом расстоянии от героя
        /// (стенд ставит их на 1,7 м — тяге Водоворота там тянуть нечего). Ставятся в начале
        /// и перед каждым нажатием, здоровье доливается каждый тик. 0 — стенд прежний.
        /// </summary>
        public static float WhirlwindRing { get; private set; }
        /// <summary>
        /// -capture-whirlwind-shift &lt;dx&gt;,&lt;dz&gt; (вместе с -capture-whirlwind-ring): весь стенд сдвигается
        /// на открытую землю. Центр первой комнаты — колодец-препятствие: тело героя в нём, тягу
        /// Водоворота мишени упираются в его края (замер 02.10: 3,40 → 3,40 м).
        /// </summary>
        public static Vector2 WhirlwindShift { get; private set; }
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
        public static bool NoVfx { get; private set; }
        public static int TempoPreset { get; private set; } = -1;
        public static string SlamCase { get; private set; } = "";
        public static int CastYaw { get; private set; }
        public static float CastDistance { get; private set; } = 3f;
        public static int HoldTicks { get; private set; } = 60;
        private static bool _showHud;
        /// <summary>
        /// Кадры видео — с экрана (то, что видит владелец), а не отдельным
        /// Camera.Render в RenderTexture (ключ -capture-screen). Заведён при
        /// разборе невидимых слоёв Рассекающего (LOG 24–25.09); оба пути дали
        /// одно и то же, ключ оставлен для сравнения экрана с RT.
        /// </summary>
        private static bool _screenVideo;
        public static bool PerformanceCapture { get; private set; }
        public static bool TurnDuringSkill { get; private set; }
        public static bool ActiveEnemies { get; private set; }
        private static string _combatEncounter;
        public static string ForestBudCase { get; private set; }
        public static bool ForestBudShowcase => _combatEncounter == "forest-bud";
        public static bool WendigoShowcase => _combatEncounter == "forest-wendigo";
        /// <summary>-capture-encounter forest-guardian: несколько Хранителей на ровной арене стенда.</summary>
        public static bool GuardianShowcase => _combatEncounter == "forest-guardian";
        /// <summary>-capture-encounter forest-stonehoof: один Камнекопыт, как в стенде StonehoofTestWindow.</summary>
        public static bool StonehoofShowcase => _combatEncounter == "forest-stonehoof";
        /// <summary>
        /// -capture-encounter forest-thorncaster | forest-snarer | forest-splitter: новый моб леса
        /// (1–3, -capture-enemies) на стенде вида. Пока модели не утверждены, тела — серые
        /// заглушки с подписью (ForestMobPlaceholderView). None — стенд не новый моб.
        /// </summary>
        public static EnemyKind ForestMobShowcase => _combatEncounter == "forest-thorncaster" ? EnemyKind.ForestThorncaster
            : _combatEncounter == "forest-snarer" ? EnemyKind.ForestRootSnarer
            : _combatEncounter == "forest-splitter" ? EnemyKind.ForestSplitter : EnemyKind.None;
        /// <summary>
        /// -capture-enemy-case: что делает герой против врага (dodge, tank, stun, turn, death).
        /// Ввод ведёт TickDriver.EnemyReviewCase; журнал плеера получает строки [enemy-qa].
        /// </summary>
        public static string EnemyCase { get; private set; }
        private static bool EnemyQa => GuardianShowcase || StonehoofShowcase || ForestMobShowcase != EnemyKind.None
            || !string.IsNullOrEmpty(EnemyCase);
        public static bool SweepAimCapture { get; private set; }
        public static bool DeathDuringSkill { get; private set; }

        public static CombatFeelCaptureTier CombatFeelTier { get; private set; }
        public static bool IsCombatFeelShowcase => CombatFeelTier != CombatFeelCaptureTier.None;
        public static bool GcWarmupActive { get; private set; }

        /// <summary>Съёмка просит TickDriver жаловаться на скачки тел.</summary>
        public static bool WatchTeleports { get; private set; }

        /// <summary>
        /// -capture-audio-log: CombatAudio пишет в журнал плеера каждый сыгранный боевой звук
        /// с причиной (строки [audio-log]: событие Sim, кто, на ком, тик; ожидание моба;
        /// отложенный звук; фон), несыгранный за нехваткой голоса (drop), сбросы на смене
        /// режима, симуляции и арены, заглушённый топот роя, выключенное сердцебиение и
        /// прочие источники звука сцены — GameSound, лагерь, музыка (source-start/stop).
        /// Проверка «звука, которого тут быть не должно» (владелец, 29.09) без редактора.
        /// </summary>
        public static bool AudioLog { get; private set; }

        /// <summary>
        /// -capture-reels: вертикальный ролик для соцсетей (владелец, 29.09). Кадр 1080×1920
        /// (альбомные -capture-width/-capture-height переворачиваются), окно плеера того же размера,
        /// камера ближе в ReelsZoom раз и ведёт центр схватки, а не одного героя (ReelsFightFocus
        /// в CameraFollow). HUD скрыт, пока нет -capture-hud. Рецепты -capture-encounter,
        /// -capture-enemy-case, -capture-hit-tier работают как в обычной съёмке.
        /// </summary>
        public static bool Reels { get; private set; }

        /// <summary>
        /// -capture-reels-zoom: доля обычного ортографического размера (или -capture-camera-size),
        /// по умолчанию 0,8. Меньше — ближе; 1 — прежний масштаб, только кадр вертикальный.
        /// </summary>
        public static float ReelsZoom { get; private set; } = DefaultReelsZoom;

        /// <summary>
        /// -capture-no-bars: кадр для рекламы без полосок здоровья врагов, полосы элиты с цифрами
        /// и таблички с именем элиты (29.09: цифры стенда «5000 / 5000», «1 / 2000» в ролике
        /// выдают тестовую арену). Цифры урона остаются — они продают удар. Вне -razlom-capture
        /// флаг не читается и всегда false.
        /// </summary>
        public static bool NoBars { get; private set; }
        private static float _reelsSize;
        private static float _reelsAspect = 9f / 16f;

        /// <summary>Служебный запуск UI-QA: открыть системное меню после кадра.</summary>
        public static bool PauseMenuCaptureRequested { get; private set; }
        public static string PauseMenuCapturePage { get; private set; }

        public static bool IsVfxShowcase => VfxShowcase != PelagVfxShowcase.None || WhirlwindShowcase;

        private static readonly int[] WhirlwindCastTicks = { 18, 54, 90 };
        /// <summary>Съёмка формы: два нажатия через 4 с — Буря (3 с) и откат Вихря (72 тика) успевают.</summary>
        private static readonly int[] FormCastTicks = { 18, 18 + 4 * Simulation.TicksPerSecond };
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
            int[] schedule = SkillForm != PelagForm.None ? FormCastTicks : WhirlwindCastTicks;
            if (_nextWhirlwindCast >= schedule.Length) return false;
            if (simTick - _whirlwindStartedTick < schedule[_nextWhirlwindCast]) return false;
            _nextWhirlwindCast++;
            return true;
        }

        /// <summary>Номер следующего нажатия стенда и тиков до него; false — отсчёт не начат или нажатий больше нет.</summary>
        public static bool TryNextWhirlwindCast(int simTick, out int index, out int ticksLeft)
        {
            int[] schedule = SkillForm != PelagForm.None ? FormCastTicks : WhirlwindCastTicks;
            index = _nextWhirlwindCast;
            ticksLeft = 0;
            if (_whirlwindStartedTick < 0 || index >= schedule.Length) return false;
            ticksLeft = _whirlwindStartedTick + schedule[index] - simTick;
            return true;
        }

        private string _outputDirectory;
        private float[] _marks;
        private CampInputCapture _campInputCapture;
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
            // Съёмка идёт без главного меню, а меню — единственное, что запускает время. В 30.09 в TimeManager.asset
            // случайно записался m_TimeScale: 0, и вся съёмка стояла на первом тике (01.10). Страховка: время идёт всегда.
            Time.timeScale = 1f;
            // -capture-style comic|off: комикс-рисовка на этот запуск, без записи в настройки. Без ключа —
            // выключена, даже если её включили в F8: сравнения «было → стало» не зависят от чужого выбора.
            ComicStyle.OverrideForSession(ComicStyle.ParseCaptureValue(ReadValue(args, ComicStyle.CaptureFlag)) ?? false);
            // -capture-mood off|auto|day|mist|dusk|boss|<ось 0…2>: свет арены по глубине так же — на запуск, без ключа выключен.
            ArenaMood.ApplyCaptureValue(ReadValue(args, ArenaMood.CaptureFlag));
            if (int.TryParse(ReadValue(args, "-capture-hud-tooltip"), out int tooltipSlot)
                && tooltipSlot >= 0 && tooltipSlot < Simulation.AbilitySlots)
                HudTooltipSlot = tooltipSlot;
            HudTooltipDetail = Array.IndexOf(args, "-capture-hud-tooltip-detail") >= 0;
            if (HudTooltipDetail)
                // Съёмочный набор — пул 0–3 в слотах 1–4 (TickDriver): Вихрь, Рассекающий, Пламя, Шквал.
                for (int line = 0; line < 4; line++)
                    for (int index = 0; index < 3; index++)
                        DeveloperTalents.Set((SabreTalentLine)line, index, true);

            string output = ReadValue(args, OutputFlag) ?? "capture";
            float[] marks = ParseMarks(ReadValue(args, TimesFlag));

            WhirlwindShowcase = Array.IndexOf(args, WhirlwindFlag) >= 0;
            WhirlwindHold = Array.IndexOf(args, "-capture-whirlwind-hold") >= 0;
            SkillForm = ParseSkillForm(ReadValue(args, "-capture-skill-form"));
            WhirlwindRing = Mathf.Max(0f, ReadFloat(args, "-capture-whirlwind-ring", 0f));
            WhirlwindShift = ParseShift(ReadValue(args, "-capture-whirlwind-shift"));
            PoseShowcase = ReadValue(args, "-capture-pose");
            RunShowcase = Array.IndexOf(args, RunFlag) >= 0;
            WatchTeleports = Array.IndexOf(args, WatchTeleportsFlag) >= 0;
            AudioLog = Array.IndexOf(args, "-capture-audio-log") >= 0;
            Reels = Array.IndexOf(args, ReelsFlag) >= 0;
            ReelsZoom = Mathf.Clamp(ReadFloat(args, ReelsZoomFlag, DefaultReelsZoom), .3f, 2f);
            _reelsSize = 0f;
            NoBars = Array.IndexOf(args, "-capture-no-bars") >= 0;
            EquipmentShowcase = Array.IndexOf(args, EquipmentFlag) >= 0;
            LocomotionShowcase = Array.IndexOf(args, LocomotionFlag) >= 0;
            MovingCombatShowcase = Array.IndexOf(args, MovingCombatFlag) >= 0;
            MovingCombatDelay = Mathf.Clamp(ReadInt(args, "-capture-moving-combat-delay", 2), 1, 24);
            VfxShowcase = ParseShowcase(ReadValue(args, SkillFlag));
            LiveSkill = Array.IndexOf(args, "-capture-live-skill") >= 0;
            NoVfx = Array.IndexOf(args, "-capture-no-vfx") >= 0;
            TempoPreset = ReadInt(args, "-capture-tempo", -1);
            SlamCase = ReadValue(args, "-capture-slam-case") ?? "";
            PerformanceCapture = ReadFloat(args, PerfFlag, 0f) > 0f;
            CastYaw = ReadInt(args, "-capture-cast-yaw", 0);
            CastDistance = Mathf.Clamp(ReadFloat(args, "-capture-cast-distance", 3f), .5f, 7f);
            HoldTicks = Mathf.Clamp(ReadInt(args, "-capture-hold-ticks", 60), 1, 60);
            _showHud = Array.IndexOf(args, "-capture-hud") >= 0;
            _screenVideo = Array.IndexOf(args, "-capture-screen") >= 0;
            TurnDuringSkill = Array.IndexOf(args, "-capture-turn-during-skill") >= 0;
            ActiveEnemies = Array.IndexOf(args, "-capture-active-enemies") >= 0;
            _combatEncounter = ReadValue(args, "-capture-encounter");
            ForestBudCase = ReadValue(args, "-capture-forest-bud-case");
            EnemyCase = ReadValue(args, "-capture-enemy-case");
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
            GcWarmupActive = IsCombatFeelShowcase || WhirlwindShowcase || TempoPreset >= 0;
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
            // Ролик вертикальный: альбомный размер (в том числе 1920×1080 по умолчанию у
            // capture.ps1 при -ExtraArgs '-capture-reels') переворачивается в 1080×1920.
            if (Reels && rig._captureWidth > rig._captureHeight)
                (rig._captureWidth, rig._captureHeight) = (rig._captureHeight, rig._captureWidth);
            _reelsAspect = rig._captureWidth / (float)rig._captureHeight;
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
            if (ForestBudShowcase) gameObject.AddComponent<ForestBudCaptureProbe>();
            if (VfxShowcase == PelagVfxShowcase.Blaze)
                gameObject.AddComponent<PelagBlazeCapture>().Initialize(_outputDirectory);
            if (IsCombatFeelShowcase || WhirlwindShowcase || VfxShowcase != PelagVfxShowcase.None || TempoPreset >= 0)
                gameObject.AddComponent<PelagAttackCapture>().Initialize(_outputDirectory);
            if (RunShowcase || LocomotionShowcase || WhirlwindShowcase || MovingCombatShowcase || VfxShowcase != PelagVfxShowcase.None || TempoPreset >= 0)
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
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-input") >= 0)
                {
                    _campInputCapture = gameObject.AddComponent<CampInputCapture>();
                    _campInputCapture.Initialize(_outputDirectory);
                }
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-polish") >= 0)
                    gameObject.AddComponent<CampPolishCapture>().Initialize(_outputDirectory);
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-look") >= 0)
                    gameObject.AddComponent<CampLookCapture>().Initialize(_outputDirectory);
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-sound") >= 0)
                    gameObject.AddComponent<CampSoundCapture>().Initialize(_outputDirectory);
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-review") >= 0)
                    gameObject.AddComponent<CampReviewCapture>().Initialize(_outputDirectory);
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-integration") >= 0)
                    gameObject.AddComponent<CampIntegrationCapture>().Initialize(_outputDirectory);
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-magic") >= 0)
                    gameObject.AddComponent<CampMagicCapture>();
                else if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-blocked") >= 0)
                    gameObject.AddComponent<CampBlockedReport>().Initialize(_outputDirectory);
                else gameObject.AddComponent<CampWalkCapture>();
            }
            else while (!CombatViewReady()) yield return null;
            // Съёмка уступа арены (-capture-at-ledge [номер]): герой у края обрыва, сверху, лицом вниз.
            int ledgeFlag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-at-ledge");
            if (ledgeFlag >= 0)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                var map = driver.Run?.Map;
                var args = System.Environment.GetCommandLineArgs();
                int index = ledgeFlag + 1 < args.Length && int.TryParse(args[ledgeFlag + 1], out int n) ? n : 0;
                if (map != null && index < map.LedgeCount)
                {
                    var ledge = map.GetLedge(index);
                    driver.Sim.Entities.Position[Simulation.PlayerId] = ledge.Point - ledge.Down * Fix64.FromInt(3);
                    driver.Sim.Entities.Facing[Simulation.PlayerId] = ledge.Down;
                    driver.Sim.StopPlayerMovement();
                    Debug.Log($"[capture-ledge] уступ {index}: {ledge.Point}");
                }
                else Debug.Log("[capture-ledge] уступа нет");
                yield return null;
            }
            if (ActiveEnemies && IsCombatFeelShowcase)
            {
                TickDriver driver = FindAnyObjectByType<TickDriver>();
                driver.Sim.SetupCombatFeelShowcase(driver.Run.Map, EnemyOverride, CombatFeelTier, true, IsPerfRun);
                Debug.Log("[capture-combat] Живых врагов: " + (driver.Sim.Entities.Count - 1));
                yield return null;
            }
            if (!string.IsNullOrEmpty(_combatEncounter) && (IsCombatFeelShowcase || ForestBudShowcase))
            {
                CombatCaptureEncounter.Configure(FindAnyObjectByType<TickDriver>(), _combatEncounter,
                    EnemyOverride, CombatFeelTier, ActiveEnemies);
                yield return null;
            }
            if (ForestBudShowcase)
            {
                // Отдельный тестовый забег даёт свежее поколение привязок и не меняет сохранение.
                var driver = FindAnyObjectByType<TickDriver>();
                driver.StartForestBudTest(driver.GetComponent<LayoutView>().Profile, SeedOverride,
                    Mathf.Clamp(EnemyOverride, 1, 8));
                yield return null;
            }
            if (WendigoShowcase)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                driver.StartWendigoTest(driver.GetComponent<LayoutView>().Profile, SeedOverride, EnemyOverride > 1);
                driver.Session.SetDeveloperInvulnerable(true);
                // -ExtraArgs '-capture-wendigo-howl': вой через секунду, прыжок заперт. Иначе первый
                // вой — только после прыжка и 9 с перезарядки, и на коротких записях его нет.
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-wendigo-howl") >= 0)
                    for (int id = 1; id < driver.Sim.Entities.Count; id++)
                        driver.Sim.SetWendigoCooldowns(id, driver.Sim.Tick + 100000, driver.Sim.Tick + 30);
                yield return null;
            }
            if (GuardianShowcase || StonehoofShowcase || ForestMobShowcase != EnemyKind.None)
            {
                // Стенд Камнекопыта: свежее поколение, открытая поляна, прогресс не сохраняется.
                // Хранители и новые мобы встают вместо быка в том же кадре — виды привязываются уже к ним.
                var driver = FindAnyObjectByType<TickDriver>();
                driver.StartStonehoofTest(driver.GetComponent<LayoutView>().Profile, SeedOverride, 1, StonehoofShowcase);
                // -ExtraArgs '-capture-pack': до шести Хранителей — места вокруг героя и кружение стаи (ИИ v2).
                bool pack = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-pack") >= 0;
                if (GuardianShowcase) CombatCaptureEncounter.SetupGuardians(driver, Mathf.Clamp(EnemyOverride, 1, pack ? 6 : 2));
                else if (ForestMobShowcase != EnemyKind.None)
                    CombatCaptureEncounter.SetupForestMob(driver, ForestMobShowcase, Mathf.Clamp(EnemyOverride, 1, 3));
                yield return null;
            }
            if (EnemyQa)
            {
                // Без сценария герой просто стоит (как tank): запись не должна кончиться его смертью.
                CombatCaptureEncounter.PrepareEnemyCase(FindAnyObjectByType<TickDriver>(), EnemyCase ?? "tank");
                yield return null;
            }
            if (TempoPreset >= 0)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                driver.StartTempoTest(new[] { 0, 1, 8, 9 }, TempoPreset);
                yield return null;
            }
            ConfigureCaptureView();
            // Явный флаг снимает настоящий экран перехода, не затрагивая обычный забег.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-arena-route") >= 0)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                var run = driver.Run;
                for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                driver.Session.Step(InputFrame.Empty);
                run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
                driver.Session.Step(InputFrame.Empty);
                driver.Session.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                if (run.Phase == RunPhase.ReplacingAbility)
                    driver.Session.Step(new InputFrame { Command = (byte)RunCommand.SalvageAbility });
                Debug.Log("[arena-qa] Portal phase=" + run.Phase + ", routes=" + run.GetRoute(0).Size
                    + "/" + run.GetRoute(1).Size + "/" + run.GetRoute(2).Size);
                yield return null;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-no-vfx") >= 0)
            {
                // Контроллер также ведёт физическую голову Абордажа: скрываем только эффекты внутри него.
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

            if (!ForestBudShowcase && (IsCombatFeelShowcase || WhirlwindShowcase || TempoPreset >= 0))
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

            if (_showHud || _screenVideo || Reels)
            {
                // Настройки игрока могут переопределить параметры запуска окна;
                // для IMGUI и съёмки с экрана нужен framebuffer именно запрошенного размера.
                Screen.SetResolution(_captureWidth, _captureHeight, FullScreenMode.Windowed);
                yield return null;
            }
            if (Reels) yield return FitReelsWindow();
            if (_showHud)
            {
                // Нативная консоль перекрывает HUD на снимке; сами ошибки
                // остаются в player.log и проверяются отдельно от вида интерфейса.
                Debug.ClearDeveloperConsole();
                Debug.developerConsoleVisible = false;
                Debug.Log("[capture-hud] Developer console overlay hidden for UI review; diagnostics remain in player.log.");
            }

            TickDriver qaDriver = EnemyQa ? FindAnyObjectByType<TickDriver>() : null;
            while ((_recordVideo
                        ? _timelineFrame / (float)_videoFps
                        : (animationClock ? Time.time : Time.unscaledTime - ComicStyleCapture.FrozenSeconds) - combatStartedAt) < finish
                   || mark < _marks.Length || (_campInputCapture != null && !_campInputCapture.Finished))
            {
                // Тестовая арена пересоздаёт привязку камеры после прогрева.
                // Обзорный ракурс удерживается и после этого перехода.
                if (LiveSkill || ReadFloat(Environment.GetCommandLineArgs(), "-capture-camera-pitch", -1f) >= 0)
                    ConfigureCaptureView();
                yield return new WaitForEndOfFrame();
                if (qaDriver != null) CombatCaptureEncounter.LogEnemyEvents(qaDriver);

                float now = _recordVideo
                    ? _timelineFrame++ / (float)_videoFps
                    // Заморозка ради вариантов комикс-рисовки в часы -Times не идёт.
                    : (animationClock ? Time.time : Time.unscaledTime - ComicStyleCapture.FrozenSeconds) - combatStartedAt;
                bool videoFrame = _recordVideo && now >= _videoStart && now < _videoEnd;
                _audioCapture?.Frame(videoFrame);
                if (videoFrame) CaptureVideoFrame();

                // Пока снимаются варианты рисовки прошлого кадра (мир заморожен), следующий кадр ждёт.
                while (mark < _marks.Length && now >= _marks[mark] && !ComicStyleCapture.Busy)
                {
                    CaptureStill(mark, _marks[mark]);
                    mark++;
                }
            }

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-arena-route") >= 0)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                driver.QueueRunCommand(RunCommand.ChooseRoute3);
                for (int frame = 0; frame < 120 && driver.Run.Depth == 1; frame++) yield return null;
                if (driver.Run.Depth != 2 || !driver.Run.CurrentRoute.Hard || driver.Run.Phase != RunPhase.Clearing)
                    throw new InvalidOperationException("Arena route UI command did not enter the selected arena.");
                Debug.Log("[arena-qa] Queued route accepted: depth=2, hard=true, phase=Clearing");
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
            // В изолированной записи портал не должен закрывать постановку тела.
            if (LiveSkill)
                foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name == "Портал" || t.name == "Схрон" || t.name == "Вход в луга" || t.name == "Проход дальше") t.gameObject.SetActive(false);
            float reviewPitch = ReadFloat(Environment.GetCommandLineArgs(), "-capture-camera-pitch", -1f);
            if (reviewPitch >= 0 && Camera.main != null)
            {
                var driver = FindAnyObjectByType<TickDriver>();
                var follow = FindAnyObjectByType<CameraFollow>();
                if (follow != null) follow.enabled = false;
                var juice = Camera.main.GetComponent<CombatCameraJuice>();
                if (juice != null) juice.enabled = false;
                var simPosition = driver.Sim.Entities.Position[ForestBudShowcase && driver.Sim.Entities.Count > 1 ? 1 : Simulation.PlayerId];
                Vector3 pivot = new Vector3(simPosition.X.ToFloat(), .9f, simPosition.Y.ToFloat());
                if (!ForestBudShowcase) pivot += Quaternion.Euler(0, CastYaw, 0) * Vector3.right * 1.8f;
                Quaternion angle = Quaternion.Euler(reviewPitch,
                    ReadFloat(Environment.GetCommandLineArgs(), "-capture-camera-yaw", 0f), 0);
                Camera.main.transform.SetPositionAndRotation(pivot - angle * Vector3.forward * 12f, angle);
                _cameraYaw = 0;
            }
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
            // В ролике -capture-camera-size — только основа масштаба, её умножает ReelsZoom ниже.
            if (_cameraSize > 0f && !Reels && Camera.main != null && Camera.main.orthographic)
            {
                Camera.main.orthographicSize = _cameraSize;
                // CombatCameraJuice restores its cached base size every
                // LateUpdate; update that cache too or the close-up lasts only
                // until the first captured frame.
                CombatCameraJuice juice = Camera.main.GetComponent<CombatCameraJuice>();
                if (juice != null) juice.SetBaseOrthographicSize(_cameraSize);
            }
            if (Reels && _reelsSize <= 0f && Camera.main != null && Camera.main.orthographic)
            {
                // Один раз: размер уходит и в CameraFollow.CombatSize, поэтому повторная
                // привязка камеры на тестовой арене берёт его же. Каждый кадр base size
                // не трогаем — SetBaseOrthographicSize запоминает поворот, а посреди тряски
                // это вшило бы крен в покой камеры.
                CameraFollow follow = FindAnyObjectByType<CameraFollow>();
                float normal = _cameraSize > 0f ? _cameraSize
                    : follow != null ? follow.CombatSize : Camera.main.orthographicSize;
                _reelsSize = normal * ReelsZoom;
                if (follow != null)
                {
                    follow.CombatSize = _reelsSize;
                    follow.Smoothing = ReelsSmoothing;
                }
                Camera.main.orthographicSize = _reelsSize;
                CombatCameraJuice juice = Camera.main.GetComponent<CombatCameraJuice>();
                if (juice != null) juice.SetBaseOrthographicSize(_reelsSize);
            }

            if ((IsCombatFeelShowcase || Reels) && !IsPerfRun && !_showHud)
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

        private static Vector2 ParseShift(string raw)
        {
            string[] parts = (raw ?? "").Split(',');
            if (parts.Length != 2) return Vector2.zero;
            bool x = float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx);
            bool z = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz);
            return x && z ? new Vector2(dx, dz) : Vector2.zero;
        }

        private static PelagForm ParseSkillForm(string raw)
        {
            switch ((raw ?? "").Trim().ToLowerInvariant())
            {
                case "whirlwind:storm": return PelagForm.WhirlwindStorm;
                case "whirlwind:maelstrom": return PelagForm.WhirlwindMaelstrom;
                case "whirlwind:waves": return PelagForm.WhirlwindFoamWaves;
                default: return PelagForm.None;
            }
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
                case "blaze": return PelagVfxShowcase.Blaze;
                case "dash": return PelagVfxShowcase.Dash;
                case "wreck": return PelagVfxShowcase.Wreck;
                case "fire-flask": return PelagVfxShowcase.FireFlask;
                case "skewer": return PelagVfxShowcase.Skewer;
                case "backblast": return PelagVfxShowcase.Backblast;
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
            // -capture-comic-variants: тот же миг ещё в нескольких вариантах рисовки (подбор ручек).
            ComicStyleCapture.AfterStill(path);
        }

        private void CaptureVideoFrame()
        {
            string path = Path.Combine(_outputDirectory, "video_frames",
                string.Format(CultureInfo.InvariantCulture, "frame_{0:0000}.jpg", _videoFrame++));
            // Контрольные снимки идут прежним путём (Camera.Render): по ним видно, расходится ли он с экраном.
            Texture2D frame = _screenVideo ? FitReelsShot(ScreenCapture.CaptureScreenshotAsTexture()) : CaptureFrame();
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
            // Layout может пересоздать пул после настройки камеры. Убираем
            // перекрывающий героя портал непосредственно перед контрольным кадром.
            if (LiveSkill && VfxShowcase == PelagVfxShowcase.AnchorSweep)
                foreach (var item in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                    if (item.name == "Вход в луга" || item.name == "Проход дальше")
                        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                            renderer.forceRenderingOff = true;
            // Camera.Render не содержит IMGUI. Для QA системного меню нужен
            // именно итоговый framebuffer после OnGUI, иначе лог подтвердит
            // открытие экрана, а снимок покажет только арену под ним.
            PauseMenu pauseMenu = FindAnyObjectByType<PauseMenu>();
            if (_showHud || (pauseMenu != null && pauseMenu.IsOpen))
                return FitReelsShot(ScreenCapture.CaptureScreenshotAsTexture());

            Camera camera = Camera.main;
            if (camera == null) return ScreenCapture.CaptureScreenshotAsTexture();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(
                _captureWidth, _captureHeight, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                // Ролик: пропорции кадра, а не окна — окно выше монитора Windows может урезать.
                if (Reels) camera.aspect = _reelsAspect;
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
                if (Reels) camera.ResetAspect();
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>
        /// Центр схватки, за которым камера ролика едет вместо героя (CameraFollow под
        /// -capture-reels): герой с весом 2 и живые враги ближе 8 м с весом, спадающим к краю
        /// круга, — в узкий кадр телефона попадает сама драка, а не пустая земля за спиной.
        /// Уход центра от героя ограничен средней частью кадра: ради дальнего моба герой
        /// к краю не уезжает.
        /// </summary>
        public static Vector3 ReelsFightFocus(TickDriver driver, Vector3 hero, Camera camera)
        {
            Simulation sim = driver.Sim;
            if (sim == null || camera == null) return hero;
            EntityStore entities = sim.Entities;
            Vector3 sum = hero * ReelsHeroWeight;
            float weight = ReelsHeroWeight;
            for (int id = 0; id < entities.Count; id++)
            {
                if (id == Simulation.PlayerId || !entities.Alive[id] || entities.Side[id] == Faction.Wole) continue;
                Vector3 enemy = driver.GetRenderPosition(id);
                float distance = new Vector2(enemy.x - hero.x, enemy.z - hero.z).magnitude;
                if (distance >= ReelsFightRadius) continue;
                float w = 1f - distance / ReelsFightRadius;
                sum += enemy * w;
                weight += w;
            }
            Vector3 offset = sum / weight - hero;
            // Оси кадра на земле: поперёк — right камеры, вглубь — её forward без наклона;
            // шаг вглубь ложится на экран с множителем sin наклона (≈0,74 при 48°).
            Transform view = camera.transform;
            Vector3 across = view.right; across.y = 0f; across.Normalize();
            Vector3 depth = view.forward; depth.y = 0f; depth.Normalize();
            float tilt = Mathf.Max(.3f, Vector3.Dot(depth, view.up));
            float halfHeight = camera.orthographic ? camera.orthographicSize : 6f;
            float roomAcross = ReelsHeroRoom * halfHeight * _reelsAspect;
            float roomDepth = ReelsHeroRoom * halfHeight / tilt;
            return hero + across * Mathf.Clamp(Vector3.Dot(offset, across), -roomAcross, roomAcross)
                        + depth * Mathf.Clamp(Vector3.Dot(offset, depth), -roomDepth, roomDepth);
        }

        /// <summary>
        /// Окно ролика. Кадр без HUD камера рендерит прямо в текстуру 1080×1920, и ему размер
        /// окна не важен. С -capture-hud / -capture-screen кадр берётся с экрана, а окно выше
        /// монитора Windows урезает: тогда окно — наибольшее 9:16, что влезает, а снимок
        /// растягивается до размера ролика (FitReelsShot).
        /// </summary>
        private IEnumerator FitReelsWindow()
        {
            yield return null;
            bool fromScreen = _showHud || _screenVideo;
            if (fromScreen && (Screen.width != _captureWidth || Screen.height != _captureHeight))
            {
                int height = Mathf.Min(_captureHeight, Display.main.systemHeight - 96) & ~1;
                int width = Mathf.RoundToInt(height * _reelsAspect) & ~1;
                Debug.LogWarning($"[capture-reels] Окно {Screen.width}x{Screen.height} вместо {_captureWidth}x{_captureHeight}: "
                                 + $"снимаем с экрана в окне {width}x{height} и растягиваем до кадра.");
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                yield return null;
                yield return null;
            }
            Debug.Log($"[capture-reels] окно {Screen.width}x{Screen.height}, кадр {_captureWidth}x{_captureHeight}, "
                      + $"с экрана={fromScreen}, zoom={ReelsZoom.ToString("0.00", CultureInfo.InvariantCulture)}, "
                      + $"ортографический размер={_reelsSize.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        /// <summary>Снимок экрана ролика — в размер кадра, если окно пришлось уменьшить (FitReelsWindow).</summary>
        private Texture2D FitReelsShot(Texture2D shot)
        {
            if (!Reels || (shot.width == _captureWidth && shot.height == _captureHeight)) return shot;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture scaled = RenderTexture.GetTemporary(_captureWidth, _captureHeight, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(shot, scaled);
                RenderTexture.active = scaled;
                var frame = new Texture2D(_captureWidth, _captureHeight, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0f, 0f, _captureWidth, _captureHeight), 0, 0, false);
                frame.Apply(false, false);
                return frame;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(scaled);
                Destroy(shot);
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
