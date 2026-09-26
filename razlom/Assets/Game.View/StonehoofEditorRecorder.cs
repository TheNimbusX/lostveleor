#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;
using Game.View;
using UnityEngine;

// Diagnostic recording only. The ordinary test window always uses live owner input.
[DefaultExecutionOrder(10000)]
public sealed class StonehoofEditorRecorder : MonoBehaviour
{
    private TickDriver _driver;
    private Camera _camera;
    private RenderTexture _target;
    private Texture2D _readback;
    private string _folder, _mode;
    private float _start, _next;
    private int _frame, _seenTick = -1, _fps, _oldFps, _oldVsync, _oldCapture;
    private bool _finished;
    private StonehoofAnimatorView _view;
    private readonly List<string> _rows = new List<string>();
    public static string Begin(string mode = "wall", int fps = 60)
    {
        var driver = UnityEngine.Object.FindAnyObjectByType<TickDriver>();
        if (driver?.Sim == null) throw new InvalidOperationException("Сначала запустите бой Камнекопыта.");
        if (UnityEngine.Object.FindAnyObjectByType<StonehoofEditorRecorder>() != null) throw new InvalidOperationException("Запись уже идёт.");
        var recorder = new GameObject("Stonehoof review recorder").AddComponent<StonehoofEditorRecorder>();
        recorder._driver = driver; recorder._camera = Camera.main; recorder._mode = mode; recorder._fps = fps;
        recorder._folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/stonehoof-review/" + mode + "-" + fps));
        Directory.CreateDirectory(recorder._folder);
        recorder._target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        recorder._readback = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        recorder._start = Time.time;
        recorder._oldFps = Application.targetFrameRate; recorder._oldVsync = QualitySettings.vSyncCount; recorder._oldCapture = Time.captureFramerate;
        driver.Sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
        driver.Sim.Entities.RefreshStats(0); driver.Sim.Entities.Health[0] = 10000;
        driver.Session.SetDeveloperInvulnerable(false); TickDriver.StonehoofReviewCase = mode;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; Time.captureFramerate = fps;
        return recorder._folder;
    }
    private void LateUpdate()
    {
        float elapsed = Time.time - _start; var sim = _driver.Sim;
        if (sim == null || sim.Entities.Count < 2) { Finish(); return; }
        if (_seenTick != sim.Tick)
        {
            _seenTick = sim.Tick;
            foreach (var c in _driver.FrameEventContexts)
            {
                var e = c.Event;
                if (e.Source == 1 || e.Target == 1) _rows.Add(FormattableString.Invariant($"event,{Time.frameCount},{c.SimulationTick},{elapsed:F5},{e.Type},{e.Amount},{e.Position.X.ToFloat():F4},{e.Position.Y.ToFloat():F4}"));
            }
        }
        if (_view == null && _driver.GetComponent<ArenaView>().TryGetEntityView(1, out var body)) _view = body.GetComponent<StonehoofAnimatorView>();
        if (_view != null)
        {
            var actor = _view.GetComponentInChildren<Animator>(); var state = actor.GetCurrentAnimatorStateInfo(0);
            sim.TryGetStonehoofAction(1, out var a);
            _rows.Add(FormattableString.Invariant($"pose,{Time.frameCount},{sim.Tick - 1 + _driver.Alpha:F5},{a.Serial},{a.Phase},{actor.GetFloat("WindupPhase"):F5},{actor.GetFloat("LaunchPhase"):F5},{actor.GetFloat("ChargeLoopPhase"):F5},{actor.GetFloat("BrakePhase"):F5},{actor.GetFloat("WallImpactPhase"):F5},{actor.GetFloat("DeathPhase"):F5},{_view.transform.position.x:F4},{_view.transform.position.y:F4},{_view.transform.position.z:F4},{a.LaunchTick},{a.BrakeTick},{a.StopTick}"));
            if (_view.Hooves[0] != null) _rows.Add(FormattableString.Invariant($"hoof,{Time.frameCount},{elapsed:F5},{_view.Hooves[0].position.x:F5},{_view.Hooves[0].position.y:F5},{_view.Hooves[0].position.z:F5}"));
        }
        if (elapsed >= _next)
        {
            _next += 1f / 24;
            if (_fps == 60 || _frame % 12 == 0)
            {
                var previous = _camera.targetTexture; var active = RenderTexture.active;
                _camera.targetTexture = _target; _camera.Render(); RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); _readback.Apply();
                File.WriteAllBytes(Path.Combine(_folder, $"frame_{_frame:D5}.png"), _readback.EncodeToPNG());
                _camera.targetTexture = previous; RenderTexture.active = active;
            }
            _rows.Add(FormattableString.Invariant($"frame,{_frame++},{sim.Tick},{elapsed:F5},{Time.unscaledDeltaTime:F5}"));
        }
        if (elapsed >= (_mode == "death" ? 13 : 9)) Finish();
    }
    private void Finish()
    {
        if (_finished) return; _finished = true;
        File.WriteAllLines(Path.Combine(_folder, "timing.csv"), _rows);
        TickDriver.StonehoofReviewCase = null;
        Application.targetFrameRate = _oldFps; QualitySettings.vSyncCount = _oldVsync; Time.captureFramerate = _oldCapture;
        Debug.Log("[stonehoof-review] Съёмка готова: " + _folder);
        Destroy(_target); Destroy(_readback); Destroy(gameObject);
    }
    private void OnDestroy() { if (!_finished && !string.IsNullOrEmpty(_folder)) Finish(); }
}
#endif
