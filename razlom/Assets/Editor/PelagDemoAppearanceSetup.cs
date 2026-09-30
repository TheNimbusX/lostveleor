using System;
using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagDemoAppearanceSetup
{
    private const string Folder = "Assets/Resources/Weapons/Pelag/AnchorDemo";
    private const string HeadPath = Folder + "/Pelag_AnchorHead_Tripo.fbx";
    private const string ProfilePath = "Assets/Resources/" + PelagAppearanceProfile.ResourcePath + ".asset";

    public static void BuildCapture()
    {
        Build();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    [MenuItem("Разлом/Пелаг/Внешность/Собрать цвета и якорь")]
    public static void Build()
    {
        var importer = AssetImporter.GetAtPath(HeadPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Импортируйте Tripo FBX якоря: " + HeadPath);
        importer.globalScale = 1f;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.SaveAndReimport();
        var textureImporter = AssetImporter.GetAtPath(Folder + "/Anchor_BaseColor.png") as TextureImporter;
        textureImporter.textureType = TextureImporterType.Default;
        textureImporter.sRGBTexture = true;
        textureImporter.mipmapEnabled = true;
        textureImporter.maxTextureSize = 1024;
        textureImporter.filterMode = FilterMode.Trilinear;
        textureImporter.wrapMode = TextureWrapMode.Clamp;
        textureImporter.textureCompression = TextureImporterCompression.Compressed;
        textureImporter.SaveAndReimport();
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Anchor_NormalGL.png");
        normalImporter.textureType = TextureImporterType.NormalMap;
        normalImporter.maxTextureSize = 1024;
        normalImporter.mipmapEnabled = true;
        normalImporter.SaveAndReimport();

        var body = MaterialAt("Assets/Resources/Characters/Pelag_v6/Pelag_ColorPolish.mat",
            Resources.Load<Texture2D>("Characters/Pelag_v6/Pelag_v6_BaseColor"), true);
        var headMaterial = MaterialAt(Folder + "/Anchor_PaintedIron.mat",
            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Anchor_BaseColor.png"), false);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(HeadPath);
        var profile = AssetDatabase.LoadAssetAtPath<PelagAppearanceProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<PelagAppearanceProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.BodyMaterial = body;
        profile.AnchorHeadPrefab = source;
        profile.AnchorHeadMaterial = headMaterial;
        profile.AnchorShape = BuildShape(source);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[Pelag appearance] Tripo anchor and editable palette ready: " + ProfilePath);
    }

    private static Material MaterialAt(string path, Texture texture, bool body)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material; // Preserve the owner's material edits on rebuild.
        material = new Material(Shader.Find(body ? "Razlom/Texture Toon" : "Universal Render Pipeline/Lit"));
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        if (body)
        {
            material.SetColor("_ShadowColor", new Color(.72f, .75f, .84f));
            material.SetColor("_MidColor", new Color(.94f, .90f, .91f));
            material.SetFloat("_WhiteClothLift", .50f);
            material.SetFloat("_LightFeather", .11f);
            material.SetFloat("_ArtSaturation", 1.16f);
            material.SetFloat("_ArtContrast", 1.06f);
            material.SetTexture("_SkinMask", Resources.Load<Texture2D>("Characters/Pelag_v6/Pelag_SkinMask"));
            material.SetFloat("_SkinToneStrength", 1f);
            material.SetFloat("_SkinSaturation", .82f);
            material.SetVector("_SkinToneScale", new Vector4(.98f, 1.14f, 1.55f, .04f));
            material.SetFloat("_OutlineWidth", 0f);
        }
        else
        {
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture>(Folder + "/Anchor_NormalGL.png"));
            material.SetFloat("_BumpScale", .7f);
            material.SetFloat("_Metallic", .25f);
            material.SetFloat("_Smoothness", .4f);
            material.EnableKeyword("_NORMALMAP");
        }
        material.enableInstancing = true;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static AnchorHeadShape BuildShape(GameObject source)
    {
        const string path = Folder + "/AnchorHeadShape.asset";
        var shape = AssetDatabase.LoadAssetAtPath<AnchorHeadShape>(path);
        if (shape == null)
        {
            shape = ScriptableObject.CreateInstance<AnchorHeadShape>();
            AssetDatabase.CreateAsset(shape, path);
        }
        var vertices = new List<Vector3>();
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            foreach (var vertex in filter.sharedMesh.vertices)
                vertices.Add(source.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
        var points = new List<Vector3>();
        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
        {
            if (x == 0 && y == 0 && z == 0) continue;
            var direction = new Vector3(x, y, z);
            float farthest = float.NegativeInfinity;
            Vector3 point = Vector3.zero;
            foreach (var vertex in vertices)
            {
                float distance = Vector3.Dot(vertex, direction);
                if (distance > farthest) { farthest = distance; point = vertex; }
            }
            if (!points.Contains(point)) points.Add(point);
        }
        shape.Points = points.ToArray();
        EditorUtility.SetDirty(shape);
        return shape;
    }

    [MenuItem("Разлом/Пелаг/Внешность/Настроить посадку и цвет")]
    public static void SelectProfile() => Selection.activeObject =
        AssetDatabase.LoadAssetAtPath<PelagAppearanceProfile>(ProfilePath);
}
