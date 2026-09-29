using System;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Прогрев шейдеров и состояний GPU (PSO) до боя. Сборка стоит на DX12: первый показ нового
    /// материала компилирует состояние прямо в кадре — так первый выход мобов стоил 333 мс (LOG, 3 сентября),
    /// а эффекты способностей и поздние волны давали рывки уже после рассеивания завесы.
    ///
    /// Набор состояний записан в самой игре (<see cref="GraphicsStateCollection"/>, съёмочный плеер с
    /// -capture-pso-trace &lt;файл&gt;: арены, мобы, способности, завеса) и лежит в
    /// Resources/Shaders/RazlomWarmup.graphicsstate. При загрузке, пока открыто главное меню, он греется
    /// в фоне на рабочих потоках: главный поток не ждёт, к первому входу в разлом всё готово.
    /// Нет файла или он записан под другой API — ничего не делается, игра как раньше.
    ///
    /// Запись 29.09 (DX12): маршрут арена → награда → завеса → арена 2 (-capture-ui-route -capture-smoke)
    /// и стенды мобов леса (-Encounter forest-wendigo, -stonehoof, -bud, -thorncaster, -snarer, -splitter,
    /// -guardian, root-swarm), 115 вариантов и 349 состояний. Новые мобы и эффекты — дописать тем же флагом
    /// в тот же файл (запись дополняет набор) и положить файл сюда заново. Лагерь не записан: съёмки лагеря нет.
    /// В редакторе не греется: там шейдеры компилируются асинхронно, а набор записан под плеер.
    /// </summary>
    public sealed class ShaderWarmup : MonoBehaviour
    {
        const string CollectionPath = "Shaders/RazlomWarmup";
        const string TraceFlag = "-capture-pso-trace";

        static ShaderWarmup _instance;
        GraphicsStateCollection _collection, _trace, _previous;
        JobHandle _handle;
        bool _warming;
        float _startedAt;
        string _tracePath;
        float _nextSave;

        /// <summary>Прогрев закончен (или его нет вовсе).</summary>
        public static bool Done => _instance == null || !_instance._warming;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isEditor || _instance != null) return;
            string[] args = Environment.GetCommandLineArgs();
            var host = new GameObject("Shader warmup") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<ShaderWarmup>();
            int trace = Array.IndexOf(args, TraceFlag);
            if (Array.IndexOf(args, "-razlom-capture") >= 0 && trace >= 0 && trace + 1 < args.Length)
                _instance.StartTrace(args[trace + 1]);
            else _instance.StartWarmup(Array.IndexOf(args, "-razlom-capture") >= 0);
        }

        void StartWarmup(bool capture)
        {
            _collection = Resources.Load<GraphicsStateCollection>(CollectionPath);
            if (_collection == null) { if (capture) Debug.Log("[pso] набора состояний нет — прогрева нет"); return; }
            if (_collection.graphicsDeviceType != SystemInfo.graphicsDeviceType)
            {
                // Набор записан под другой API (DX12 против Vulkan/DX11): состояния не подойдут, не тратим время.
                if (capture) Debug.Log($"[pso] набор записан под {_collection.graphicsDeviceType}, а игра на {SystemInfo.graphicsDeviceType} — прогрева нет");
                return;
            }
            // В съёмке пропущенные состояния тоже записываются: по ним видно, что набор неполный.
            _handle = _collection.WarmUp(default, capture);
            JobHandle.ScheduleBatchedJobs();
            _warming = true;
            _startedAt = Time.realtimeSinceStartup;
            if (capture)
                Debug.Log($"[pso] прогрев: вариантов {_collection.variantCount}, состояний {_collection.totalGraphicsStateCount}, " +
                          $"{_collection.graphicsDeviceType}/{_collection.qualityLevelName}");
        }

        /// <summary>
        /// Запись набора: всё, что игра нарисует до выхода, дописывается к уже записанному в том же файле
        /// (несколько съёмочных сценариев — один набор).
        /// </summary>
        void StartTrace(string path)
        {
            _tracePath = path;
            if (File.Exists(path))
            {
                _previous = new GraphicsStateCollection();
                if (!_previous.LoadFromFile(path)) _previous = null;
            }
            _trace = new GraphicsStateCollection();
            bool started = _trace.BeginTrace();
            _nextSave = Time.realtimeSinceStartup + 20f;
            Debug.Log($"[pso] запись набора в {path}: {(started ? "идёт" : "не началась")}, было вариантов {(_previous != null ? _previous.variantCount : 0)}");
            Application.quitting += SaveTrace;
        }

        void Update()
        {
            if (_warming && _handle.IsCompleted)
            {
                _handle.Complete();
                _warming = false;
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-razlom-capture") >= 0)
                    Debug.Log($"[pso] прогрев готов за {Time.realtimeSinceStartup - _startedAt:0.00} с: {_collection.completedWarmupCount} состояний");
            }
            // Плеер съёмки может быть закрыт по сроку, не дойдя до выхода: набор сохраняется и по ходу.
            if (_trace != null && Time.realtimeSinceStartup >= _nextSave)
            {
                _nextSave = Time.realtimeSinceStartup + 20f;
                Save(false);
            }
        }

        void SaveTrace() => Save(true);

        void Save(bool final)
        {
            if (_trace == null) return;
            // Запись закрывается, копится в общем наборе и (если игра идёт дальше) начинается заново.
            _trace.EndTrace();
            // Первая запись в новый файл: заголовок набора (API, платформа, качество) — из самой записи.
            // Пустой new GraphicsStateCollection() помечен Null-устройством, и такой набор не греется.
            if (_previous == null) _previous = _trace;
            else _previous.Append(_trace);
            bool saved = _previous.SaveToFile(_tracePath);
            Debug.Log($"[pso] набор {(final ? "итог" : "по ходу")}: вариантов {_previous.variantCount}, состояний {_previous.totalGraphicsStateCount}, сохранён {saved}");
            if (final) { _trace = null; return; }
            _trace = new GraphicsStateCollection();
            _trace.BeginTrace();
        }

        void OnApplicationQuit()
        {
            if (_collection == null || Array.IndexOf(Environment.GetCommandLineArgs(), "-razlom-capture") < 0) return;
            var missed = _collection.cacheMissCollection;
            if (missed != null)
                Debug.Log($"[pso] не прогреты: вариантов {missed.variantCount}, состояний {missed.totalGraphicsStateCount}");
        }
    }
}
