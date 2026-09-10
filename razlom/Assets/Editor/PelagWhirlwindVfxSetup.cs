using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagWhirlwindVfxSetup
{
    private const string PrefabPath = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/VFX_Pelag_Whirlwind_Heavy.prefab";
    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить авторский Вихрь")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (library == null || prefab == null || library.Entries == null) return;
        InstallHit(library);
        int index = System.Array.FindIndex(library.Entries, e => e.Id == PelagVfxId.WhirlwindRing);
        if (index < 0) return;
        var existing = prefab.GetComponent<PelagVfxElement>();
        if (library.Entries[index].Prefab == prefab && existing != null && existing.AuthoredRadius > 0f) return;
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var element = root.GetComponent<PelagVfxElement>();
            if (element == null) element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindRing;
            element.DefaultLifetime = 0.35f;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            Bounds bounds = renderer.mesh.bounds;
            element.AuthoredRadius = Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x),
                Mathf.Abs(bounds.min.y), Mathf.Abs(bounds.max.y));
            Debug.Log($"[whirlwind-setup] mesh bounds={bounds} radius={element.AuthoredRadius}");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        library.Entries[index] = new AbilityVfxLibrary.Entry
        { Id = PelagVfxId.WhirlwindRing, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), Prewarm = 3 };
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static void InstallHit(AbilityVfxLibrary library)
    {
        var hit = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/VFX_Pelag_Whirlwind_Hit.prefab");
        if (hit == null || hit.GetComponent<PelagVfxElement>() == null) return;
        int hitIndex = System.Array.FindIndex(library.Entries, e => e.Id == PelagVfxId.WhirlwindHit);
        if (hitIndex < 0) return;
        int pullIndex = System.Array.FindIndex(library.Entries, e => e.Id == PelagVfxId.CyclonePullImpact);
        if (pullIndex < 0)
        {
            pullIndex = library.Entries.Length;
            System.Array.Resize(ref library.Entries, pullIndex + 1);
        }
        if (library.Entries[pullIndex].Prefab == null)
        {
            // The former shared impact still belongs to the chain cyclone.
            var previous = library.Entries[hitIndex].Prefab;
            if (previous == hit) previous = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/VFX/Pelag/Prefabs/VFX_Whirlwind_Hit.prefab");
            library.Entries[pullIndex] = new AbilityVfxLibrary.Entry
            { Id = PelagVfxId.CyclonePullImpact, Prefab = previous, Prewarm = 10 };
        }
        library.Entries[hitIndex] = new AbilityVfxLibrary.Entry
        { Id = PelagVfxId.WhirlwindHit, Prefab = hit, Prewarm = 16 };
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }
}
