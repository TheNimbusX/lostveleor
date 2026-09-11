using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagRollVfxSetup
{
    private const string Source = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/VFX_Pelag_Squall_Dash.prefab";
    private const string Destination = "Assets/Resources/VFX/Pelag/Prefabs/VFX_Pelag_Roll.prefab";
    private const string RevisionKey = "Pelag.RollVfx.v2.SpacingOpacity";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить кувырок")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        if (library == null) return;
        PelagSquallVfxSetup.Install();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Destination);
        if (prefab == null || !SessionState.GetBool(RevisionKey, false))
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Source) == null) return;
            SessionState.SetBool(RevisionKey, true);
            var root = PrefabUtility.LoadPrefabContents(Source);
            try
            {
                root.name = "VFX_Pelag_Roll";
                var element = root.GetComponent<PelagVfxElement>();
                if (element == null) element = root.AddComponent<PelagVfxElement>();
                element.Id = PelagVfxId.RollDash;
                element.DefaultLifetime = 10f / 30f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    // След живёт столько же, сколько перекат, сохраняя рисунок Шквала.
                    var main = ps.main;
                    main.duration = 10f / 30f;
                    main.startLifetime = 10f / 30f;
                    main.loop = false;
                    main.startColor = Fade(main.startColor, .65f);
                    if (ps.gameObject != root)
                    {
                        Vector3 position = ps.transform.localPosition;
                        position.y *= 1.8f;
                        ps.transform.localPosition = position;
                    }
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, Destination);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == PelagVfxId.RollDash);
        if (index >= 0 && library.Entries[index].Prefab == prefab) return;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = PelagVfxId.RollDash, Prefab = prefab, Prewarm = 2 };
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static ParticleSystem.MinMaxGradient Fade(ParticleSystem.MinMaxGradient value, float opacity)
    {
        Color min = value.colorMin, max = value.colorMax;
        min.a *= opacity; max.a *= opacity;
        value.colorMin = min; value.colorMax = max;
        if (value.gradientMin != null) value.gradientMin = FadeGradient(value.gradientMin, opacity);
        if (value.gradientMax != null) value.gradientMax = FadeGradient(value.gradientMax, opacity);
        return value;
    }

    private static Gradient FadeGradient(Gradient source, float opacity)
    {
        var alpha = source.alphaKeys;
        for (int i = 0; i < alpha.Length; i++) alpha[i].alpha *= opacity;
        var result = new Gradient { mode = source.mode };
        result.SetKeys(source.colorKeys, alpha);
        return result;
    }
}
