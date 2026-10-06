using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Сохранение собранного — только своего (память razlom-animator-builder-saveassets, 02.10):
/// общий AssetDatabase.SaveAssets() в конце Build() записывал версии из памяти редактора
/// поверх дисковых правок соседней сессии (материалы босса откатились к HEAD). Редактор
/// общий, поэтому сохраняем ровно то, что пишет этот сборщик: контроллер, обе маски и
/// производные клипы в Assets/Resources/Characters/Pelag_v5 (.anim — RazlomPelagAuthoredClips,
/// TurnClips, SaberClips, StanceClips, WhirlwindClip, BlazeClip и опоры рывка/Шквала/Абордажа).
/// Исходные FBX в Mixamo/ — импорт, а не сохранение; их не трогаем.
/// </summary>
public static partial class RazlomPelagV5AnimatorBuilder
{
    private const string OutputFolder = "Assets/Resources/Characters/Pelag_v5";

    private static void SaveBuiltAssets(AnimatorController controller)
    {
        int saved = 0;
        foreach (string guid in AssetDatabase.FindAssets("", new[] { OutputFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(Folder + "/")) continue;
            if (!(path.EndsWith(".anim") || path.EndsWith(".mask") || path.EndsWith(".controller"))) continue;
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null || !EditorUtility.IsDirty(asset)) continue;
            AssetDatabase.SaveAssetIfDirty(asset);
            saved++;
        }
        if (controller != null && EditorUtility.IsDirty(controller))
        {
            AssetDatabase.SaveAssetIfDirty(controller);
            saved++;
        }
        Debug.Log("[Разлом] Pelag v5 controller: сохранено своих ассетов " + saved + " (без общего SaveAssets).");
    }
}
