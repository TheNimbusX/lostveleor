using UnityEditor;

// Runtime-построение навигации читает вершины авторских моделей лагеря.
public sealed class CampModelImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (assetPath.StartsWith("Assets/Resources/Environment/Camp/"))
            ((ModelImporter)assetImporter).isReadable = true;
    }
    [InitializeOnLoadMethod]
    static void Schedule() { EditorApplication.delayCall += EnsureReadable; }
    static void EnsureReadable()
    { Prepare(); }
    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[]{"Assets/Resources/Environment/Camp"}))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
            if (importer == null || importer.isReadable) continue;
            importer.isReadable = true; importer.SaveAndReimport();
        }
    }
}
