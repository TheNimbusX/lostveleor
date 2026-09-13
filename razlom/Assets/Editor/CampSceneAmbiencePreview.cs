using Game.View;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Предпросмотр принадлежит сцене и не зависит от выбранного объекта в Inspector.
[InitializeOnLoad]
public static class CampSceneAmbiencePreview
{
    static CampAmbience _active;
    static double _last;
    static CampSceneAmbiencePreview()
    {
        EditorApplication.update+=Update;
        AssemblyReloadEvents.beforeAssemblyReload+=Stop;
        EditorApplication.playModeStateChanged+=_=>Stop();
        EditorSceneManager.sceneSaving+=(scene,path)=>Stop();
        EditorSceneManager.sceneClosing+=(scene,removing)=>Stop();
        EditorApplication.quitting+=Stop;
    }
    public static void Stop(){if(_active!=null && !Application.isPlaying){_active.StopPreview();foreach(var smoke in _active.GetComponentsInChildren<CampChimneySmoke>())smoke.StopPreview();}_active=null;}
    static void Update()
    {
        if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        double now=EditorApplication.timeSinceStartup;if(now-_last<1.0/30)return;_last=now;
        if(_active==null)_active=Object.FindAnyObjectByType<CampAmbience>();
        if(_active==null)return;
        if(!_active.PreviewInScene || !_active.isActiveAndEnabled){Stop();return;}
        _active.PreviewAt((float)now);
        foreach(var biome in _active.GetComponentsInChildren<CampMicrobiome>())biome.Preview(1f/30);
        foreach(var smoke in _active.GetComponentsInChildren<CampChimneySmoke>())smoke.Preview(1f/30);
        SceneView.RepaintAll();
    }
}

[CustomEditor(typeof(CampAmbience))]
public sealed class CampAmbienceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Preview In Scene включает ветер и мерцание без Play. Перед сохранением и запуском игры исходная яркость автоматически восстанавливается.",MessageType.Info);
    }
}
