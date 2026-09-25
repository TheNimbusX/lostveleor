#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;
using Game.View;
using UnityEngine;

// Запись настоящего игрового боя в редакторе: камера, персонажи и телеграфы штатные.
[DefaultExecutionOrder(10000)]
public sealed class WendigoEditorRecorder : MonoBehaviour
{
    private TickDriver _driver;
    private Camera _camera;
    private RenderTexture _target;
    private Texture2D _readback;
    private string _folder, _mode;
    private float _start, _next;
    private int _frame, _startTick, _seenTick=-1;
    private int _oldFps, _oldVsync, _oldCaptureRate;
    private bool _finished;
    private Animator _actor;
    private readonly List<string> _events = new List<string>();
    public static string Begin(string mode="hit", int fps=60)
    {
        var driver=UnityEngine.Object.FindAnyObjectByType<TickDriver>();
        if(driver==null || driver.Sim==null)throw new InvalidOperationException("Сначала запустите тестовый бой вендиго.");
        if(UnityEngine.Object.FindAnyObjectByType<WendigoEditorRecorder>()!=null)throw new InvalidOperationException("Запись уже идёт.");
        var recorder=new GameObject("Wendigo review recorder").AddComponent<WendigoEditorRecorder>();
        recorder._driver=driver;recorder._camera=Camera.main;recorder._mode=mode;
        recorder._folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/wendigo-review/editor-"+mode+"-"+fps));
        Directory.CreateDirectory(recorder._folder);
        recorder._target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
        recorder._readback=new Texture2D(1280,720,TextureFormat.RGB24,false);
        recorder._start=Time.time;recorder._startTick=driver.Sim.Tick;
        recorder._oldFps=Application.targetFrameRate;recorder._oldVsync=QualitySettings.vSyncCount;
        recorder._oldCaptureRate=Time.captureFramerate;
        driver.Session.SetDeveloperInvulnerable(true);
        TickDriver.WendigoReviewCase=mode;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=fps;Time.captureFramerate=fps;
        return recorder._folder;
    }
    private void LateUpdate()
    {
        float elapsed=Time.time-_start;var sim=_driver.Sim;
        if(sim==null||sim.Entities.Count<2){Finish();return;}
        if(_seenTick!=sim.Tick)
        {
            _seenTick=sim.Tick;
            foreach(var context in _driver.FrameEventContexts)
            {
                var e=context.Event;
                if(e.Source==1||e.Target==1)
                    _events.Add(FormattableString.Invariant($"event,{Time.frameCount},{context.SimulationTick},{elapsed:F5},{e.Type},{e.Amount},{e.ActionVariant}"));
            }
        }
        if(_actor==null)
        {
            var view=UnityEngine.Object.FindAnyObjectByType<ForestWendigoAnimatorView>();
            if(view!=null)_actor=view.GetComponentInChildren<Animator>();
        }
        if(_actor!=null&&sim.TryGetWendigoAction(1,out var action))
        {
            float phase=_actor.GetFloat(action.Kind==WendigoAction.Leap?"LeapPhase":"ClawPhase");
            _events.Add(FormattableString.Invariant($"pose,{Time.frameCount},{sim.Tick-1+_driver.Alpha:F5},{action.Serial},{action.Kind},{phase*96:F5},{action.ImpactTick}"));
        }
        if(_actor!=null && _mode=="walk-r04")
        {
            var position=sim.Entities.Position[1];
            _events.Add(FormattableString.Invariant($"walk,{Time.frameCount},{elapsed:F5},{position.X.ToFloat():F5},{position.Y.ToFloat():F5},{_actor.GetFloat("WalkPhase"):F5},{sim.Entities.Velocity[1].Length.ToFloat()*30:F5}"));
        }
        if(_mode=="kill"&&elapsed>6&&sim.Entities.Alive[1])
        {
            sim.Entities.Health[1]=1;
            sim.Entities.Position[0]=sim.Entities.Position[1]+new FixVec2(Fix64.FromInt(2),Fix64.Zero);
        }
        if(elapsed>=_next)
        {
            _next+=1f/24;
            var previous=_camera.targetTexture;var active=RenderTexture.active;
            _camera.targetTexture=_target;_camera.Render();RenderTexture.active=_target;
            _readback.ReadPixels(new Rect(0,0,1280,720),0,0);_readback.Apply();
            File.WriteAllBytes(Path.Combine(_folder,$"frame_{_frame++:D5}.png"),_readback.EncodeToPNG());
            _camera.targetTexture=previous;RenderTexture.active=active;
            _events.Add(FormattableString.Invariant($"frame,{_frame-1},{sim.Tick},{elapsed:F5},{Time.unscaledDeltaTime:F5}"));
        }
        if(elapsed>=(_mode=="walk-r04"?16:12))Finish();
    }
    private void Finish()
    {
        if(_finished)return;_finished=true;
        File.WriteAllLines(Path.Combine(_folder,"timing.csv"),_events);
        Debug.Log("[wendigo-review] Съёмка готова: "+_folder);
        TickDriver.WendigoReviewCase=null;Application.targetFrameRate=_oldFps;QualitySettings.vSyncCount=_oldVsync;Time.captureFramerate=_oldCaptureRate;
        Destroy(_target);Destroy(_readback);Destroy(gameObject);
    }
    private void OnDestroy(){if(!_finished&&!string.IsNullOrEmpty(_folder))Finish();}
}
#endif
