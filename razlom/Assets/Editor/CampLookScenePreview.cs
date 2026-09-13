using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Временные значения живут только внутри рендера: Save, Play и сборка получают авторский базовый свет.
[InitializeOnLoad]
public static class CampLookScenePreview
{
    static CampLookController _look, _rendering;

    static CampLookScenePreview()
    {
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
        AssemblyReloadEvents.beforeAssemblyReload += Restore;
        EditorSceneManager.sceneSaving += (scene, path) => Restore();
        EditorApplication.playModeStateChanged += state => Restore();
    }

    static void Begin(ScriptableRenderContext context, Camera camera)
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)) return;
        if (_look == null) _look = Object.FindAnyObjectByType<CampLookController>();
        if (_look == null || !_look.isActiveAndEnabled) return;
        Restore();
        _rendering = _look;
        _rendering.BeginEditorPreview();
    }

    static void End(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Game) Restore();
    }

    static void Restore()
    {
        if (_rendering != null) _rendering.EndEditorPreview();
        _rendering = null;
    }
}

[CustomEditor(typeof(CampLookController))]
public sealed class CampLookControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Style меняет свет и обработку в Scene и Play. Для Scene нужны Lighting и Post Processing в панели Effects.", MessageType.Info);
        if (DrawDefaultInspector()) SceneView.RepaintAll();
    }
}
