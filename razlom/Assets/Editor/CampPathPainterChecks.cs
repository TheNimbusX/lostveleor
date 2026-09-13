using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Проверка кисти на настоящих assets в изолированном capture-проекте.</summary>
public static class CampPathPainterChecks
{
    public static void BuildOriginalCapture()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/artifacts/"))
            throw new InvalidOperationException("Проверка запускается только в изолированном capture-проекте.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var data = AssetDatabase.LoadAssetAtPath<CampPathPaintData>(CampPathPainter.DataPath);
        if (data == null) throw new InvalidOperationException("Сначала запусти BuildCapture.");
        data.Path = (byte[])data.OriginalPath.Clone();
        EditorUtility.SetDirty(data);
        CampPathPainter.Apply(data); CampPathPainter.Save(data);
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    public static void BuildCapture()
    {
        Run();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/artifacts/"))
            throw new InvalidOperationException("Проверка запускается только в изолированном capture-проекте.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        CampGroundStudy study = CampPathPainter.FindStudy();
        Check(study != null, "camp study exists");
        MeshRenderer surface = CampPathPainter.SurfaceRenderer(study);
        Check(surface != null, "surface renderer exists");
        var map = surface.sharedMaterial.GetTexture("_SurfaceMap") as Texture2D;
        Vector4 bounds = surface.sharedMaterial.GetVector("_SurfaceBounds");
        var data = CampPathPainter.EnsureData(map);
        byte[] original = (byte[])data.Path.Clone();
        Color32[] originalPixels = map.GetPixels32();
        string hierarchy = Hierarchy(study);
        Vector3 center = study.transform.position + new Vector3(-3, 0, -1.5f);
        Vector3 from = center - Vector3.right * 1.5f, to = center + Vector3.right * 1.5f;

        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(data, "Path paint regression");
        CampPathPainter.PaintSegment(data, bounds, from, to, 1.5f, .25f, false);
        CampPathPainter.Apply(data);
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        byte[] painted = (byte[])data.Path.Clone();
        int changed = 0;
        for (int i = 0; i < painted.Length; i++) if (painted[i] != original[i]) changed++;
        Check(changed > 20, "stroke changes pixels");
        for (int step = 0; step <= 30; step++)
            Check(Read(data, bounds, Vector3.Lerp(from, to, step / 30f)) == 255, "fast stroke has no gaps");
        Color32[] after = map.GetPixels32();
        for (int i = 0; i < after.Length; i++)
            Check(after[i].g == originalPixels[i].g && after[i].b == originalPixels[i].b && after[i].a == originalPixels[i].a, "other surface channels preserved");
        Check(hierarchy == Hierarchy(study), "objects and transforms preserved");
        Undo.PerformUndo();
        Check(Same(original, data.Path), "Undo restores exact mask");
        Check(SamePixels(originalPixels, map.GetPixels32()), "Undo updates visible texture");
        Undo.PerformRedo();
        Check(Same(painted, data.Path), "Redo restores stroke");
        CampPathPainter.PaintSegment(data, bounds, from, to, 1.5f, .25f, true);
        CampPathPainter.Apply(data);
        Check(Read(data, bounds, center) == 0, "eraser clears path");
        int index = Pixel(data, bounds, center);
        Check(data.ClearedFoliage.GetPixels32()[index].r == 0, "eraser restores foliage visibility");

        data.Path = (byte[])original.Clone();
        EditorUtility.SetDirty(data);
        CampPathPainter.Apply(data); CampPathPainter.Save(data);
        AssetDatabase.ImportAsset(CampPathPainter.DataPath, ImportAssetOptions.ForceUpdate);
        data = AssetDatabase.LoadAssetAtPath<CampPathPaintData>(CampPathPainter.DataPath);
        Check(Same(original, data.Path), "saved mask survives import");
        CampGroundStudyBuilder.Install();
        Check(hierarchy == Hierarchy(study) && Same(original, data.Path), "old generator preserves painted ground");
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-path-paint-demo") >= 0)
        {
            CampPathPainter.PaintSegment(data, bounds, from, to, 1.5f, .25f, false);
            CampPathPainter.Apply(data); CampPathPainter.Save(data);
        }
        string report = $"PASS: paint, continuous stroke, erase, Undo, Redo, texture restoration, channel preservation, object preservation, save/import, old generator guard.\nchangedPixels={changed}\nfrom={from} to={to} bounds={bounds}\nhierarchyCount={study.GetComponentsInChildren<Transform>(true).Length}\n";
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../camp-path-painter-checks.txt"));
        File.WriteAllText(output, report);
        Debug.Log("[camp-path-painter-checks] " + report);
    }

    static int Pixel(CampPathPaintData data, Vector4 bounds, Vector3 p)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt((p.x - bounds.x) / bounds.z * (data.Width - 1)), 0, data.Width - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((p.z - bounds.y) / bounds.w * (data.Height - 1)), 0, data.Height - 1);
        return z * data.Width + x;
    }
    static byte Read(CampPathPaintData data, Vector4 bounds, Vector3 p) => data.Path[Pixel(data, bounds, p)];
    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    static bool SamePixels(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }
    static string Hierarchy(CampGroundStudy study)
    {
        var text = new System.Text.StringBuilder();
        foreach (var t in study.GetComponentsInChildren<Transform>(true))
            text.Append(t.GetEntityId().ToString()).Append(t.name).Append(t.localPosition).Append(t.localRotation).Append(t.localScale);
        return text.ToString();
    }
    static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("Camp path painter: " + message);
    }
}
