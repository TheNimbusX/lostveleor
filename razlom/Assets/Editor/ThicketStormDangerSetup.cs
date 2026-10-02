using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Материал поля опасности бури цветения Хозяина Чащи (ThicketStormDangerView, шейдер
/// Razlom/Thicket Storm Danger). Лежит в Resources не ради удобства: шейдер, на который не
/// ссылается ни один материал сборки, в плеер не попадает, и Shader.Find там вернёт null — поле
/// молча пропало бы из съёмки capture.ps1 (как у общих меток, GroundTelegraphSetup). Создаётся
/// один раз, если его нет; руками не правится: всё, что меняется, вид ставит в материал сам
/// каждый кадр. Шейдер включает GroundTelegraphStyle.hlsl — смена стиля меток переимпортирует и
/// его (меню ниже — «Пересобрать»).
/// </summary>
public static class ThicketStormDangerSetup
{
    private const string Folder = "Assets/Resources/VFX/ThicketMaster";
    private const string MaterialPath = Folder + "/StormDanger.mat";
    private const string ShaderPath = "Assets/Shaders/ThicketStormDanger.shader";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Ensure;

    [MenuItem("Разлом/Босс/Хозяин Чащи/Материал поля бури")]
    public static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
        var shader = Shader.Find(ThicketStormDangerView.ShaderName);
        // Шейдер ещё не импортирован — повторится на следующей перезагрузке домена.
        if (shader == null)
        {
            Debug.LogWarning($"[thicketmaster-vfx] Нет шейдера {ThicketStormDangerView.ShaderName}: материал {MaterialPath} не создан.");
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/VFX")) AssetDatabase.CreateFolder("Assets/Resources", "VFX");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/VFX", "ThicketMaster");
        AssetDatabase.CreateAsset(new Material(shader) { name = "StormDanger" }, MaterialPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[thicketmaster-vfx] Материал поля опасности бури создан: {MaterialPath}");
    }

    /// <summary>Переимпорт шейдера поля (после правки GroundTelegraphStyle.hlsl) и проверка материала.</summary>
    [MenuItem("Разлом/Босс/Хозяин Чащи/Пересобрать поле бури")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceUpdate);
        Ensure();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var shader = Shader.Find(ThicketStormDangerView.ShaderName);
        if (material == null || shader == null || material.shader == shader) return;
        material.shader = shader;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
    }
}
