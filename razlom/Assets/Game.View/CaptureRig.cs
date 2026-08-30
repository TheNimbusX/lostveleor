using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

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
    /// Симуляции рига не касается вообще. Она читает время и пишет PNG.
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
        private const string VideoFlag = "-capture-video";
        private const string VideoStartFlag = "-capture-video-start";
        private const string VideoDurationFlag = "-capture-video-duration";
        private const string VideoFpsFlag = "-capture-video-fps";
        private const string CameraSizeFlag = "-capture-camera-size";
        private const string SkillFlag = "-capture-skill";

        /// <summary>
        /// Переопределения для <see cref="Bootstrap"/>. Считываются ДО загрузки
        /// сцены, поэтому к моменту Awake бутстрапа они уже проставлены.
        ///
        /// Срез требует 5–8 врагов, а сцена собрана под сорок. Править сцену
        /// ради снимка нельзя: сохранённый .unity — это состояние проекта,
        /// а не параметр запуска.
        /// </summary>
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

        public static bool RunShowcase { get; private set; }

        public static PelagVfxShowcase VfxShowcase { get; private set; }

        public static bool IsVfxShowcase => VfxShowcase != PelagVfxShowcase.None || WhirlwindShowcase;

        private static readonly int[] WhirlwindCastTicks = { 18, 54, 90 };
        private static int _nextWhirlwindCast;

        /// <summary>Три capture-нажатия первого слота, привязанные к sim tick.</summary>
        public static bool ShouldCastWhirlwind(int simTick)
        {
            if (!WhirlwindShowcase || simTick < 0) return false;
            if (_nextWhirlwindCast >= WhirlwindCastTicks.Length) return false;
            if (simTick < WhirlwindCastTicks[_nextWhirlwindCast]) return false;
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
        private int _timelineFrame;
        private float _cameraSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, EnableFlag) < 0) return;

            string output = ReadValue(args, OutputFlag) ?? "capture";
            float[] marks = ParseMarks(ReadValue(args, TimesFlag));

            WhirlwindShowcase = Array.IndexOf(args, WhirlwindFlag) >= 0;
            RunShowcase = Array.IndexOf(args, RunFlag) >= 0;
            VfxShowcase = ParseShowcase(ReadValue(args, SkillFlag));
            _nextWhirlwindCast = 0;

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

            AutoEnterRift = true;

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
        }

        private void Start()
        {
            Directory.CreateDirectory(_outputDirectory);
            if (_recordVideo)
            {
                Directory.CreateDirectory(Path.Combine(_outputDirectory, "video_frames"));
                Time.captureFramerate = _videoFps;
            }
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // Splash и первая загрузка FBX занимают разное время на разных
            // машинах. Отсчёт начинается только когда Rift и реальная сабля
            // уже привязаны — иначе расписание снимает заставку вместо боя.
            while (!CombatViewReady()) yield return null;
            ConfigureCaptureView();
            PelagVfxController vfx = FindFirstObjectByType<PelagVfxController>();
            if (vfx != null && VfxShowcase != PelagVfxShowcase.None)
            {
                // Дать толпе подойти в читаемую дистанцию; отсчёт видео ещё
                // не начался, поэтому в ролик ожидание не попадает.
                yield return new WaitForSecondsRealtime(1.25f);
                vfx.BeginShowcase(VfxShowcase);
            }
            float combatStartedAt = Time.unscaledTime;

            int mark = 0;
            float lastMark = _marks.Length > 0 ? _marks[_marks.Length - 1] : 0f;
            float finish = Mathf.Max(lastMark, _recordVideo ? _videoEnd : 0f);

            while ((_recordVideo
                        ? _timelineFrame / (float)_videoFps
                        : Time.unscaledTime - combatStartedAt) < finish
                   || mark < _marks.Length)
            {
                yield return new WaitForEndOfFrame();

                float now = _recordVideo
                    ? _timelineFrame++ / (float)_videoFps
                    : Time.unscaledTime - combatStartedAt;
                if (_recordVideo && now >= _videoStart && now < _videoEnd)
                    CaptureVideoFrame();

                while (mark < _marks.Length && now >= _marks[mark])
                {
                    CaptureStill(mark, _marks[mark]);
                    mark++;
                }
            }

            if (_recordVideo) Time.captureFramerate = 0;
            Application.Quit();
        }

        private void ConfigureCaptureView()
        {
            if (_cameraSize > 0f && Camera.main != null && Camera.main.orthographic)
            {
                Camera.main.orthographicSize = _cameraSize;
                // CombatCameraJuice restores its cached base size every
                // LateUpdate; update that cache too or the close-up lasts only
                // until the first captured frame.
                CombatCameraJuice juice = Camera.main.GetComponent<CombatCameraJuice>();
                if (juice != null) juice.SetBaseOrthographicSize(_cameraSize);
            }

            ArenaView arena = FindFirstObjectByType<ArenaView>();
            if (arena == null || !arena.TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip))
                return;

            Renderer renderer = bladeRoot.GetComponentInParent<Renderer>();
            string rendererState = renderer == null
                ? "renderer=none"
                : $"renderer={renderer.name}, enabled={renderer.enabled}, active={renderer.gameObject.activeInHierarchy}, " +
                  $"boundsCenter={renderer.bounds.center}, boundsSize={renderer.bounds.size}, " +
                  $"shader={(renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "none")}";
            Debug.Log($"[capture-grip] root={bladeRoot.position}, tip={bladeTip.position}, " +
                      $"length={Vector3.Distance(bladeRoot.position, bladeTip.position):0.000}, {rendererState}");
        }

        private static bool CombatViewReady()
        {
            ArenaView arena = FindFirstObjectByType<ArenaView>();
            PelagVfxController vfx = FindFirstObjectByType<PelagVfxController>();
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
                case "anchor-sweep": return PelagVfxShowcase.AnchorSweep;
                case "chain-step": return PelagVfxShowcase.ChainStep;
                case "rotation": return PelagVfxShowcase.Rotation;
                default:
                    Debug.LogWarning("[capture] Неизвестный VFX showcase: " + raw);
                    return PelagVfxShowcase.None;
            }
        }

        private void CaptureStill(int index, float mark)
        {
            string path = Path.Combine(_outputDirectory,
                string.Format(CultureInfo.InvariantCulture, "shot_{0:00}_t{1:0.00}s.png", index, mark));
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
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
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(path, frame.EncodeToJPG(92));
            }
            finally
            {
                Destroy(frame);
            }
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
