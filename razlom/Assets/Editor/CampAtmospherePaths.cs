using System;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CampAtmospherePaths
{
    [MenuItem("Разлом/Лагерь/Смягчить подход к мосту")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Выйдите из Play.");
        var passage = Object.FindAnyObjectByType<CampRiverPassage>();
        var study = CampPathPainter.FindStudy();
        if (passage == null || study == null) throw new InvalidOperationException("Откройте сцену лагеря.");
        var surface = CampPathPainter.SurfaceRenderer(study);
        var data = CampPathPainter.EnsureData(surface.sharedMaterial.GetTexture("_SurfaceMap") as Texture2D);
        Vector4 bounds = surface.sharedMaterial.GetVector("_SurfaceBounds");
        Bounds bridge = passage.BridgeBounds;
        float x = bridge.center.x, z = bridge.max.z;
        // Камни уже подходят к реке. Два мягких мазка связывают их с первой доской,
        // не превращая весь берег в ровную полосу земли.
        CampPathPainter.PaintSegment(data, bounds, new Vector3(x + .15f, 0, z + 2.25f),
            new Vector3(x + .03f, 0, z + .65f), 1.95f, .68f, false);
        CampPathPainter.PaintSegment(data, bounds, new Vector3(x + .03f, 0, z + .8f),
            new Vector3(x, 0, z - .05f), 1.55f, .72f, false);
        CampPathPainter.Apply(data);
        CampPathPainter.Save(data);
        Debug.Log($"[camp-atmosphere] Подход к мосту: x={x:F2}, z={z:F2}");
    }

    public static void BuildCapture()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/artifacts/"))
            throw new InvalidOperationException("Пакетная правка возможна только в теневом проекте.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Apply();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }
}
