using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.View
{
    /// <summary>
    /// Замер подлага переходов (владелец 29.09: «плавный переход, сейчас подлаг»). Тяжёлые шаги смены
    /// арены (сборка пола и декора, прогрев пулов, смена симуляции) обёрнуты в <see cref="Measure"/>:
    /// это метки профилировщика (видны в Profiler редактора и dev-сборки) и, только в съёмочном
    /// плеере с -capture-frame-log, счёт миллисекунд по кадру для журнала <see cref="FrameLog"/>.
    /// Вне съёмки метка стоит одну пару Begin/End — счёта нет.
    /// </summary>
    public static class FrameCost
    {
        /// <summary>Журнал долгих кадров включён: -razlom-capture и -capture-frame-log.</summary>
        public static bool Logging { get; internal set; }

        static readonly Dictionary<string, ProfilerMarker> Markers = new Dictionary<string, ProfilerMarker>();
        // Время меток за текущий кадр, мс (вложенные считаются и сами, и в родителе).
        internal static readonly Dictionary<string, double> Frame = new Dictionary<string, double>();
        internal static readonly List<string> Order = new List<string>();
        internal static readonly List<string> Notes = new List<string>();
        static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public readonly struct Scope : IDisposable
        {
            readonly ProfilerMarker _marker;
            readonly string _label;
            readonly long _start;

            internal Scope(ProfilerMarker marker, string label)
            {
                _marker = marker;
                _label = label;
                _start = Logging ? Stopwatch.GetTimestamp() : 0;
                marker.Begin();
            }

            public void Dispose()
            {
                _marker.End();
                if (_label != null && Logging) Add(_label, (Stopwatch.GetTimestamp() - _start) * TicksToMs);
            }
        }

        /// <summary>Метка шага: using (FrameCost.Measure("LayoutView.Сборка")) { … }. Только главный поток.</summary>
        public static Scope Measure(string label)
        {
            if (!Markers.TryGetValue(label, out var marker))
            {
                marker = new ProfilerMarker(ProfilerCategory.Scripts, label);
                Markers.Add(label, marker);
            }
            return new Scope(marker, label);
        }

        /// <summary>
        /// Время работы рабочего потока (сборка масок арены) — строкой в журнал, только в съёмке с журналом
        /// кадров. Можно звать с любого потока: Debug.Log потокобезопасен. start — Stopwatch.GetTimestamp().
        /// </summary>
        public static void Worker(string label, long start)
        {
            if (!Logging) return;
            Debug.Log($"[frame-log] поток: {label} {(Stopwatch.GetTimestamp() - start) * TicksToMs:0.0} мс");
        }

        /// <summary>Пометка кадра без времени (снимок экрана съёмки, начало перехода и т.п.).</summary>
        public static void Note(string note)
        {
            if (Logging && !Notes.Contains(note)) Notes.Add(note);
        }

        static void Add(string label, double ms)
        {
            if (Frame.TryGetValue(label, out double sum)) Frame[label] = sum + ms;
            else { Frame.Add(label, ms); Order.Add(label); }
        }

        internal static void ResetFrame()
        {
            Frame.Clear();
            Order.Clear();
            Notes.Clear();
        }
    }

    /// <summary>
    /// Журнал долгих кадров съёмочного плеера (-capture-frame-log): каждый кадр дольше 33 мс — строкой
    /// «[frame-log]» в player.log с разбивкой по фазам (Update с корутинами, LateUpdate, отрисовка и
    /// ожидание) и по меткам <see cref="FrameCost"/>; на каждый переход — итог: длительность, самый
    /// длинный кадр, кадры дольше 50 и 33 мс, пока завеса на экране. Только dev-плеер съёмки; в игре
    /// и в редакторе компонента нет.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class FrameLog : MonoBehaviour
    {
        const double LongFrame = 33.0, VeilLimit = 50.0;
        static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        long _frameStart, _lateStart, _endOfFrame;
        int _gcBefore;
        string _stage = "—";
        bool _transition;
        long _transitionStart;
        int _transitionFrames, _over50, _over33, _over50Clean, _over33Clean;
        double _longest, _longestClean;
        string _longestLabel = "";
        readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        readonly List<string> _recorderNames = new List<string>();
        readonly StringBuilder _line = new StringBuilder(512);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-razlom-capture") < 0 || Array.IndexOf(args, "-capture-frame-log") < 0) return;
            FrameCost.Logging = true;
            var host = new GameObject("Frame log") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            host.AddComponent<FrameLog>();
        }

        void Start()
        {
            // Встроенные метки движка, по которым видно компиляцию шейдеров, загрузку и сборку мусора.
            // Список имён печатается один раз: по нему выбираются метки для следующих замеров.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var names = new StringBuilder();
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                string name = description.Name;
                if (!Interesting(name)) continue;
                names.Append(name).Append("; ");
                if (_recorders.Count >= 48) continue;
                var recorder = ProfilerRecorder.StartNew(description.Category, name, 1, ProfilerRecorderOptions.Default);
                if (!recorder.Valid) continue;
                _recorders.Add(recorder);
                _recorderNames.Add(name);
            }
            Debug.Log("[frame-log] метки движка: " + names);
            _frameStart = Stopwatch.GetTimestamp();
            _gcBefore = GC.CollectionCount(0);
            StartCoroutine(EndOfFrame());
        }

        static bool Interesting(string name)
            => name.IndexOf("GPUProgram", StringComparison.Ordinal) >= 0
               || name.IndexOf("PipelineState", StringComparison.Ordinal) >= 0
               || name.IndexOf("Shader.", StringComparison.Ordinal) == 0
               || name.IndexOf("WarmUp", StringComparison.OrdinalIgnoreCase) >= 0
               || name == "Instantiate" || name == "GC.Collect" || name == "GC.Alloc"
               || name == "Loading.ReadObject" || name == "Gfx.WaitForPresentOnGfxThread"
               || name == "Gfx.WaitForRenderThread" || name == "Texture2D.Apply"
               || name == "Mesh.RecalculateNormals"
               // Фазы кадра движка: куда уходит время вне меток FrameCost (чужие виды, отрисовка, аниматоры).
               || name == "Update.ScriptRunBehaviourUpdate" || name == "PreLateUpdate.ScriptRunBehaviourLateUpdate"
               || name == "PreLateUpdate.DirectorUpdateAnimationBegin" || name == "PreLateUpdate.DirectorUpdateAnimationEnd"
               || name == "PostLateUpdate.FinishFrameRendering" || name == "PostLateUpdate.PlayerUpdateCanvases"
               || name == "Canvas.SendWillRenderCanvases" || name == "Canvas.BuildBatch" || name == "Camera.Render"
               || name == "Update.ScriptRunDelayedDynamicFrameRate" || name == "PostLateUpdate.UpdateAllRenderers"
               || name == "Physics.Simulate" || name == "Physics.SyncColliderTransform"
               || name == "RenderPipelineManager.DoRenderLoop_Internal()" || name == "Inl_UniversalRenderPipeline.RenderSingleCameraInternal";

        void Update()
        {
            long now = Stopwatch.GetTimestamp();
            double frame = (now - _frameStart) * TicksToMs;
            if (Time.frameCount > 2) Report(frame, now);
            FrameCost.ResetFrame();
            _frameStart = now;
            _lateStart = _endOfFrame = 0;
            _gcBefore = GC.CollectionCount(0);
            // Стадия перехода — на начало кадра: смена и сборка идут в этом кадре под ней.
            _stage = CampTransition.Stage;
        }

        void LateUpdate() => _lateStart = Stopwatch.GetTimestamp();

        IEnumerator EndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                _endOfFrame = Stopwatch.GetTimestamp();
            }
        }

        void Report(double frame, long now)
        {
            bool running = _stage != "—";
            bool shot = FrameCost.Notes.Contains("снимок");
            if (running)
            {
                if (!_transition)
                {
                    _transition = true;
                    _transitionStart = _frameStart;
                    _transitionFrames = _over50 = _over33 = _over50Clean = _over33Clean = 0;
                    _longest = _longestClean = 0;
                    _longestLabel = "";
                }
                _transitionFrames++;
                if (frame > VeilLimit) { _over50++; if (!shot) _over50Clean++; }
                if (frame > LongFrame) { _over33++; if (!shot) _over33Clean++; }
                if (frame > _longest) { _longest = frame; _longestLabel = TopLabel(); }
                if (!shot && frame > _longestClean) _longestClean = frame;
            }
            else if (_transition)
            {
                _transition = false;
                double length = (_frameStart - _transitionStart) * TicksToMs / 1000.0;
                Debug.Log($"[frame-log] переход: {length:0.00} с, кадров {_transitionFrames}, самый длинный {_longest:0.0} мс ({_longestLabel}), "
                          + $">50 мс: {_over50}, >33 мс: {_over33}; без кадров со снимком: самый длинный {_longestClean:0.0} мс, "
                          + $">50 мс: {_over50Clean}, >33 мс: {_over33Clean}");
            }
            if (frame <= LongFrame) return;

            _line.Clear();
            _line.Append("[frame-log] #").Append(Time.frameCount - 1).Append(' ')
                 .Append(frame.ToString("0.0")).Append(" мс, завеса ").Append(_stage);
            if (_lateStart > 0)
            {
                double update = (_lateStart - _frameStart) * TicksToMs;
                double late = ((_endOfFrame > _lateStart ? _endOfFrame : now) - _lateStart) * TicksToMs;
                double rest = _endOfFrame > 0 ? (now - _endOfFrame) * TicksToMs : 0;
                _line.Append(" | Update ").Append(update.ToString("0.0"))
                     .Append(", Late+кадр ").Append(late.ToString("0.0"))
                     .Append(", после кадра ").Append(rest.ToString("0.0"));
            }
            int gc = GC.CollectionCount(0) - _gcBefore;
            if (gc > 0) _line.Append(", GC ").Append(gc);
            foreach (string note in FrameCost.Notes) _line.Append(", ").Append(note);
            if (FrameCost.Order.Count > 0)
            {
                _line.Append(" |");
                foreach (string label in FrameCost.Order)
                {
                    double ms = FrameCost.Frame[label];
                    if (ms < .5) continue;
                    _line.Append(' ').Append(label).Append(' ').Append(ms.ToString("0.0")).Append(';');
                }
            }
            bool engine = false;
            for (int i = 0; i < _recorders.Count; i++)
            {
                long value = _recorders[i].LastValue;
                if (value <= 0) continue;
                double ms = _recorders[i].UnitType == ProfilerMarkerDataUnit.TimeNanoseconds ? value / 1e6 : value;
                if (_recorders[i].UnitType == ProfilerMarkerDataUnit.TimeNanoseconds && ms < .5) continue;
                if (!engine) { _line.Append(" | движок:"); engine = true; }
                _line.Append(' ').Append(_recorderNames[i]).Append(' ').Append(ms.ToString("0.0")).Append(';');
            }
            Debug.Log(_line.ToString());
        }

        static string TopLabel()
        {
            string top = "";
            double best = 0;
            foreach (string label in FrameCost.Order)
                if (FrameCost.Frame[label] > best && label.IndexOf('/') < 0) { best = FrameCost.Frame[label]; top = label; }
            return top.Length > 0 ? top + " " + best.ToString("0") + " мс" : "без меток";
        }

        void OnDestroy()
        {
            foreach (var recorder in _recorders) recorder.Dispose();
            _recorders.Clear();
        }
    }
}
