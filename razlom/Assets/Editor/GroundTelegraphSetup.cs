using UnityEditor;
using UnityEngine;

/// <summary>
/// Материал общих меток — сектора, круга и кольца (GroundTelegraphView).
///
/// Лежит в Resources не ради удобства: шейдер, на который не ссылается ни
/// один материал сборки, в плеер не попадает, и Shader.Find там вернёт null —
/// метки молча пропали бы из съёмки capture.ps1. Полоса уже есть рядом:
/// EnemyLane.mat. Создаётся один раз, если его нет; руками не правится.
/// </summary>
public static class GroundTelegraphSetup
{
    private const string Folder = "Assets/Resources/VFX/Telegraphs";
    private const string SectorPath = Folder + "/EnemySector.mat";
    private const string SectorShader = "Razlom/Ground Telegraph Sector";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Ensure;

    [MenuItem("Разлом/Телеграфы/Материал общих меток")]
    public static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(SectorPath) != null) return;
        var shader = Shader.Find(SectorShader);
        // Шейдер ещё не импортирован — повторится на следующей перезагрузке домена.
        if (shader == null) { Debug.LogWarning($"[Разлом] Нет шейдера {SectorShader}: материал меток не создан."); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/VFX")) AssetDatabase.CreateFolder("Assets/Resources", "VFX");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/VFX", "Telegraphs");
        AssetDatabase.CreateAsset(new Material(shader) { name = "EnemySector" }, SectorPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Разлом] Материал общих меток создан: {SectorPath}");
    }
}
