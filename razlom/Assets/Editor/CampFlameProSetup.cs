using UnityEditor;
using UnityEngine;

public static class CampFlameProSetup
{
    private const string Folder = "Assets/Resources/Environment/Camp/FlamePro/";
    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Лагерь/Подключить Flame Pro")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var shader = Shader.Find("Razlom/Camp Flame Pro");
        var importer = AssetImporter.GetAtPath(Folder + "FlamePro_Atlas.png") as TextureImporter;
        if (shader == null || importer == null) return;
        if (importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp
            || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency)
        {
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "M_FlamePro.mat");
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, Folder + "M_FlamePro.mat"); }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "FlamePro_Atlas.png"));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
    }
}
