using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CampGroundSetup
{
    const string Folder = "Assets/Resources/Environment/Camp/ground/";
    public static void ApplyAndBuild()
    {
        Apply();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    [MenuItem("Разлом/Лагерь/Применить материалы земли")]
    public static void Apply()
    {
        var shader = Shader.Find("Game/Camp Ground");
        if (shader == null) throw new InvalidOperationException("Camp Ground shader is missing.");
        var grass = LoadTexture(File.Exists(Folder+"CampMeadow_v2.png") ? "CampMeadow_v2.png" : "CampMeadow_v1.png");
        var dirt = LoadTexture("CampEarth_v1.png");
        LoadTexture("CampTurf_v3.png");
        LoadTexture("CampTrailStones_v2.png");
        string backup = Path.GetFullPath("../artifacts/camp-ground-original-materials");
        Directory.CreateDirectory(backup);
        // Меняются только три материала лагеря, сцена и коллизии не сохраняются.
        foreach (string name in new[] { "M_Ground_Base_New", "M_Ground_Dirt", "M_Ground_Dirt_2" })
        {
            string path = Folder + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) throw new InvalidOperationException(path);
            string original = Path.Combine(backup, name + ".mat");
            if (!File.Exists(original)) File.Copy(path, original);
            bool isPath = name != "M_Ground_Base_New";
            Undo.RecordObject(mat, "Camp ground materials");
            mat.shader = shader;
            mat.shaderKeywords = Array.Empty<string>();
            mat.SetTexture("_GrassTex", grass); mat.SetTexture("_DirtTex", dirt);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_TileMeters", 8f);
            mat.SetFloat("_IsPath", isPath ? 1 : 0);
            mat.SetFloat("_SrcBlend", (float)(isPath ? BlendMode.SrcAlpha : BlendMode.One));
            mat.SetFloat("_DstBlend", (float)(isPath ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            mat.SetFloat("_ZWrite", isPath ? 0 : 1);
            mat.SetOverrideTag("RenderType", isPath ? "Transparent" : "Opaque");
            mat.SetShaderPassEnabled("DepthOnly", !isPath);
            mat.renderQueue = isPath ? 2989 : 2000;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }
        File.WriteAllText(Path.GetFullPath("../artifacts/camp-ground-applied.txt"), DateTime.Now.ToString("O"));
        SceneView.RepaintAll();
        Debug.Log("[camp-ground] Three ground materials updated; scene and colliders unchanged.");
    }

    static Texture2D LoadTexture(string name)
    {
        string path = Folder + name;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true; importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4; importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
