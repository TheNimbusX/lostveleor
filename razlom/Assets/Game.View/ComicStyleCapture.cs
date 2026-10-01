using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Sim;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Подбор комикс-рисовки в изолированной съёмке (только под -razlom-capture, обычная игра этот код
    /// не видит). Три ключа, все — на один запуск, без записи в ассет и настройки:
    /// <list type="bullet">
    /// <item>-capture-comic-set "Имя=значение;Имя=значение" — ручки ComicStyleSettings на весь запуск
    /// (float, bool, цвет #RRGGBB). Пересборка плеера не нужна.</item>
    /// <item>-capture-comic-variants "&lt;файл&gt;" — после каждого кадра -Times снимает тот же миг ещё в
    /// нескольких вариантах: строка файла «имя: off», «имя: on» или «имя: Имя=значение; …» (поверх
    /// настроек запуска). Без HUD — тот же кадр камерой в текстуру; с -capture-hud — мир замирает
    /// (timeScale 0) и каждый вариант снимается с экрана вместе с HUD. Файлы: shot_…__имя.png.</item>
    /// <item>-capture-comic-gpu — время GPU каждого прохода рисовки (Recorder на семплерах Render Graph,
    /// только Development-плеер) и кадра целиком (FrameTimingManager): строки [comic-gpu] в журнале.</item>
    /// <item>-capture-comic-low-hp &lt;с&gt; — через столько секунд после входа в Разлом здоровье героя
    /// падает до 20 %: проверить, что полоски и цифры поверх рисовки остаются чёткими.</item>
    /// </list>
    /// </summary>
    internal sealed class ComicStyleCapture : MonoBehaviour
    {
        const string CaptureFlag = "-razlom-capture";
        const string SetFlag = "-capture-comic-set";
        const string VariantsFlag = "-capture-comic-variants";
        const string GpuFlag = "-capture-comic-gpu";
        const string LowHpFlag = "-capture-comic-low-hp";

        // Имена проходов = имена семплеров Render Graph (ComicStyleFeature.AddDraw).
        static readonly string[] PassNames =
        {
            "Comic normals", "Comic key grid", "Comic key", "Comic paint", "Comic paint (copy)", "Comic paint (half)", "Comic paint mix",
            "Comic ink and tone", "Comic tilt downsample", "Comic tilt blur X", "Comic tilt blur Y", "Comic tilt composite",
            // Проходы URP — цена света арены по глубине: cookie и фонари (непрозрачные), дымка, лучи и
            // огоньки (прозрачные), тени солнца.
            "DrawOpaqueObjects", "DrawTransparentObjects", "Draw Main Light Shadowmap",
        };

        sealed class Variant
        {
            public string Name;
            public bool Enabled;
            public string Overrides;
        }

        sealed class PassProbe
        {
            public string Name;
            public Recorder Recorder;
            public readonly List<float> GpuMs = new List<float>();
            public double CpuMs;
        }

        static ComicStyleCapture _instance;

        readonly List<Variant> _variants = new List<Variant>();
        readonly List<PassProbe> _probes = new List<PassProbe>();
        readonly List<float> _gpuFrameMs = new List<float>();
        readonly List<float> _cpuFrameMs = new List<float>();
        readonly FrameTiming[] _timings = new FrameTiming[1];
        bool _gpu, _screen, _busy;
        float _lowHpAfter = -1f, _riftAt = -1f;
        int _gpuFrames;
        TickDriver _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, CaptureFlag) < 0) return;
            string set = ReadValue(args, SetFlag);
            string variants = ReadValue(args, VariantsFlag);
            bool gpu = Array.IndexOf(args, GpuFlag) >= 0;
            float lowHp = float.TryParse(ReadValue(args, LowHpFlag), NumberStyles.Float, CultureInfo.InvariantCulture, out float at) ? at : -1f;
            if (set == null && variants == null && !gpu && lowHp < 0f) return;

            if (!string.IsNullOrEmpty(set))
                foreach (ComicStyleFeature feature in Features())
                    Debug.Log("[comic-capture] ручки на запуск: " + Apply(feature.Settings, set));

            var go = new GameObject("Comic style capture");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ComicStyleCapture>();
            _instance._gpu = gpu;
            _instance._lowHpAfter = lowHp;
            _instance._screen = Array.IndexOf(args, "-capture-hud") >= 0;
            if (!string.IsNullOrEmpty(variants)) _instance.LoadVariants(variants);
            if (gpu) _instance.StartProbes();
            Debug.Log($"[comic-capture] рисовка={(ComicStyle.Enabled ? "комикс" : "обычная")}, вариантов={_instance._variants.Count}, " +
                      $"с экрана={_instance._screen}, gpu={gpu}, низкое здоровье={lowHp}");
        }

        /// <summary>Снимаются варианты прошлого кадра: CaptureRig откладывает следующий кадр -Times.</summary>
        public static bool Busy => _instance != null && _instance._busy;

        /// <summary>
        /// Сколько реальных секунд мир простоял замороженным ради вариантов. CaptureRig вычитает их из
        /// часов по unscaledTime: иначе следующие кадры -Times срабатывали бы сразу после заморозки.
        /// </summary>
        public static float FrozenSeconds { get; private set; }

        /// <summary>Зовёт CaptureRig сразу после записи кадра -Times.</summary>
        public static void AfterStill(string path)
        {
            if (_instance == null || _instance._variants.Count == 0) return;
            if (_instance._busy)
            {
                Debug.LogWarning("[comic-capture] кадр " + Path.GetFileName(path) + " пришёл, пока снимались варианты прошлого — пропущен.");
                return;
            }
            if (_instance._screen) _instance.StartCoroutine(_instance.ScreenVariants(path));
            else _instance.CameraVariants(path);
        }

        // ---------------------------------------------------------------------------------------
        // Ручки
        // ---------------------------------------------------------------------------------------

        static List<ComicStyleFeature> Features()
        {
            var found = new List<ComicStyleFeature>();
            var assets = new List<RenderPipelineAsset> { GraphicsSettings.currentRenderPipeline, GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.count; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (RenderPipelineAsset asset in assets)
            {
                if (!(asset is UniversalRenderPipelineAsset urp)) continue;
                ScriptableRendererData[] list = urp.rendererDataList.ToArray();
                foreach (ScriptableRendererData data in list)
                {
                    if (data == null) continue;
                    foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                        if (feature is ComicStyleFeature comic && !found.Contains(comic)) found.Add(comic);
                }
            }
            return found;
        }

        /// <summary>«Имя=значение;…» в поля настроек. Возвращает применённое — для журнала.</summary>
        static string Apply(ComicStyleSettings settings, string overrides)
        {
            var applied = new StringBuilder();
            foreach (string part in overrides.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string name = part.Substring(0, eq).Trim(), raw = part.Substring(eq + 1).Trim();
                FieldInfo field = typeof(ComicStyleSettings).GetField(name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null)
                {
                    Debug.LogWarning("[comic-capture] нет ручки " + name);
                    continue;
                }
                object value = null;
                if (field.FieldType == typeof(float)
                    && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) value = f;
                else if (field.FieldType == typeof(bool))
                    value = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
                else if (field.FieldType == typeof(Color) && ColorUtility.TryParseHtmlString(raw.StartsWith("#") ? raw : "#" + raw, out Color c))
                    value = c;
                if (value == null)
                {
                    Debug.LogWarning($"[comic-capture] не разобрать {name}={raw}");
                    continue;
                }
                field.SetValue(settings, value);
                applied.Append(name).Append('=').Append(raw).Append(' ');
            }
            settings.Sanitize();
            return applied.ToString().Trim();
        }

        void LoadVariants(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[comic-capture] нет файла вариантов " + path);
                return;
            }
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#")) continue;
                int colon = text.IndexOf(':');
                if (colon <= 0) continue;
                string name = text.Substring(0, colon).Trim();
                string body = text.Substring(colon + 1).Trim();
                bool off = body.Equals("off", StringComparison.OrdinalIgnoreCase);
                _variants.Add(new Variant
                {
                    Name = name,
                    Enabled = !off,
                    Overrides = off || body.Equals("on", StringComparison.OrdinalIgnoreCase) ? "" : body,
                });
            }
        }

        // ---------------------------------------------------------------------------------------
        // Варианты одного мига
        // ---------------------------------------------------------------------------------------

        /// <summary>Без HUD: тот же кадр камерой в текстуру, варианты подряд в одном кадре игры.</summary>
        void CameraVariants(string path)
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            bool baseEnabled = ComicStyle.Enabled;
            var snapshots = Snapshot();
            try
            {
                foreach (Variant variant in _variants)
                {
                    Prepare(variant, snapshots);
                    Texture2D frame = RenderCamera(camera);
                    Save(frame, path, variant.Name);
                }
            }
            finally
            {
                Restore(snapshots, baseEnabled);
            }
        }

        /// <summary>С HUD: мир замирает, каждый вариант рисуется следующим кадром и снимается с экрана.</summary>
        IEnumerator ScreenVariants(string path)
        {
            _busy = true;
            float frozenFrom = Time.unscaledTime;
            float timeScale = Time.timeScale;
            bool baseEnabled = ComicStyle.Enabled;
            var snapshots = Snapshot();
            try
            {
                foreach (Variant variant in _variants)
                {
                    Prepare(variant, snapshots);
                    Time.timeScale = 0f;
                    yield return null;
                    Time.timeScale = 0f;
                    yield return new WaitForEndOfFrame();
                    Save(ScreenCapture.CaptureScreenshotAsTexture(), path, variant.Name);
                }
            }
            finally
            {
                Restore(snapshots, baseEnabled);
                Time.timeScale = timeScale > 0f ? timeScale : 1f;
                FrozenSeconds += Time.unscaledTime - frozenFrom;
                _busy = false;
            }
        }

        static List<(ComicStyleFeature feature, string json)> Snapshot()
        {
            var list = new List<(ComicStyleFeature, string)>();
            foreach (ComicStyleFeature feature in Features()) list.Add((feature, JsonUtility.ToJson(feature.Settings)));
            return list;
        }

        static void Prepare(Variant variant, List<(ComicStyleFeature feature, string json)> snapshots)
        {
            foreach (var (feature, json) in snapshots)
            {
                JsonUtility.FromJsonOverwrite(json, feature.Settings);
                if (!string.IsNullOrEmpty(variant.Overrides)) Apply(feature.Settings, variant.Overrides);
            }
            SetEnabledQuietly(variant.Enabled);
        }

        static void Restore(List<(ComicStyleFeature feature, string json)> snapshots, bool enabled)
        {
            foreach (var (feature, json) in snapshots) JsonUtility.FromJsonOverwrite(json, feature.Settings);
            SetEnabledQuietly(enabled);
        }

        static void SetEnabledQuietly(bool enabled)
        {
            if (ComicStyle.Enabled != enabled) ComicStyle.OverrideForSession(enabled);
        }

        static Texture2D RenderCamera(Camera camera)
        {
            int width = Screen.width, height = Screen.height;
            string[] args = Environment.GetCommandLineArgs();
            if (int.TryParse(ReadValue(args, "-capture-width"), out int w) && w > 0) width = w;
            if (int.TryParse(ReadValue(args, "-capture-height"), out int h) && h > 0) height = h;
            RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var frame = new Texture2D(width, height, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
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

        static void Save(Texture2D frame, string stillPath, string variant)
        {
            string path = Path.Combine(Path.GetDirectoryName(stillPath) ?? "",
                Path.GetFileNameWithoutExtension(stillPath) + "__" + variant + ".png");
            try
            {
                File.WriteAllBytes(path, frame.EncodeToPNG());
                Debug.Log($"[comic-capture] {path}  {frame.width}x{frame.height}");
            }
            finally
            {
                Destroy(frame);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Низкое здоровье
        // ---------------------------------------------------------------------------------------

        void Update()
        {
            if (_lowHpAfter >= 0f) TickLowHealth();
            if (_gpu) SampleGpu();
        }

        void TickLowHealth()
        {
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            GameSession session = _driver != null ? _driver.Session : null;
            RiftRun run = _driver != null ? _driver.Run : null;
            if (session == null || session.Mode != GameMode.Rift || run == null) return;
            if (_riftAt < 0f) _riftAt = Time.unscaledTime;
            if (Time.unscaledTime - _riftAt < _lowHpAfter) return;
            EntityStore entities = run.Sim.Entities;
            int id = Simulation.PlayerId;
            entities.Health[id] = Mathf.Max(1, entities.MaxHealth[id] / 5);
            Debug.Log($"[comic-capture] здоровье героя {entities.Health[id]}/{entities.MaxHealth[id]}");
            _lowHpAfter = -1f;
        }

        // ---------------------------------------------------------------------------------------
        // Цена проходов
        // ---------------------------------------------------------------------------------------

        void StartProbes()
        {
            foreach (string name in PassNames)
            {
                Sampler sampler = Sampler.Get(name);
                var probe = new PassProbe { Name = name };
                if (sampler != null && sampler.isValid)
                {
                    probe.Recorder = sampler.GetRecorder();
                    probe.Recorder.enabled = true;
                }
                _probes.Add(probe);
            }
        }

        void SampleGpu()
        {
            // Семплеры Render Graph заводятся при первой записи прохода: пересматриваем недостающие.
            if (Time.frameCount % 60 == 0)
                foreach (PassProbe probe in _probes)
                    if (probe.Recorder == null || !probe.Recorder.isValid)
                    {
                        Sampler sampler = Sampler.Get(probe.Name);
                        if (sampler != null && sampler.isValid)
                        {
                            probe.Recorder = sampler.GetRecorder();
                            probe.Recorder.enabled = true;
                        }
                    }

            _gpuFrames++;
            // Первые 300 кадров — компиляция шейдеров и прогрев пулов, их не считаем.
            if (_gpuFrames < 300) return;
            foreach (PassProbe probe in _probes)
            {
                if (probe.Recorder == null || !probe.Recorder.isValid) continue;
                long gpu = probe.Recorder.gpuElapsedNanoseconds;
                if (gpu > 0 || probe.Recorder.sampleBlockCount > 0) probe.GpuMs.Add(gpu / 1e6f);
                probe.CpuMs += probe.Recorder.elapsedNanoseconds / 1e6;
            }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                if (_timings[0].gpuFrameTime > 0) _gpuFrameMs.Add((float)_timings[0].gpuFrameTime);
                if (_timings[0].cpuFrameTime > 0) _cpuFrameMs.Add((float)_timings[0].cpuFrameTime);
            }
        }

        void OnApplicationQuit()
        {
            if (!_gpu) return;
            var line = new StringBuilder("[comic-gpu] {");
            line.Append($"\"style\":\"{(ComicStyle.Enabled ? "comic" : "off")}\",");
            line.Append($"\"screen\":\"{Screen.width}x{Screen.height}\",");
            line.Append($"\"frames\":{Mathf.Max(0, _gpuFrames - 300)},");
            line.Append($"\"gpu_frame_avg\":{F(Average(_gpuFrameMs))},\"gpu_frame_p95\":{F(Percentile(_gpuFrameMs, .95f))},");
            line.Append($"\"cpu_frame_avg\":{F(Average(_cpuFrameMs))},\"cpu_frame_p95\":{F(Percentile(_cpuFrameMs, .95f))},");
            line.Append("\"passes\":{");
            bool first = true;
            foreach (PassProbe probe in _probes)
            {
                if (probe.GpuMs.Count == 0) continue;
                if (!first) line.Append(',');
                first = false;
                line.Append($"\"{probe.Name}\":{{\"gpu_avg\":{F(Average(probe.GpuMs))},\"gpu_p95\":{F(Percentile(probe.GpuMs, .95f))},\"n\":{probe.GpuMs.Count}}}");
            }
            line.Append("}}");
            Debug.Log(line.ToString());
        }

        static float Average(List<float> values)
        {
            if (values.Count == 0) return 0f;
            double sum = 0;
            foreach (float v in values) sum += v;
            return (float)(sum / values.Count);
        }

        static float Percentile(List<float> values, float fraction)
        {
            if (values.Count == 0) return 0f;
            var sorted = new List<float>(values);
            sorted.Sort();
            return sorted[Mathf.Clamp(Mathf.RoundToInt(fraction * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

        static string ReadValue(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index < 0 || index + 1 >= args.Length ? null : args[index + 1];
        }
    }
}
