using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagEvadeVfxSetup
{
    private const string Source = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/VFX_Pelag_Squall_Dash.prefab";
    private const string Destination = "Assets/Resources/VFX/Pelag/Prefabs/VFX_Pelag_Evade.prefab";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить успешное уклонение")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        if (library == null) return;
        PelagSquallVfxSetup.Install();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Destination);
        if (prefab == null)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Source) == null) return;
            var root = PrefabUtility.LoadPrefabContents(Source);
            try
            {
                root.name = "VFX_Pelag_Evade";
                var element = root.GetComponent<PelagVfxElement>();
                if (element == null) element = root.AddComponent<PelagVfxElement>();
                element.Id = PelagVfxId.Evade;
                element.DefaultLifetime = .2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.duration = .2f;
                    main.startLifetime = .2f;
                    main.startColor = new Color(.8f, .95f, 1f, .5f);
                    main.loop = false;
                    if (ps.gameObject != root)
                    {
                        Vector3 position = ps.transform.localPosition;
                        position.y *= 2f;
                        ps.transform.localPosition = position;
                    }
                    var fade = ps.colorOverLifetime;
                    fade.enabled = true;
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(0f, 1f) });
                    fade.color = gradient;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, Destination);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == PelagVfxId.Evade);
        if (index >= 0 && library.Entries[index].Prefab == prefab) return;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = PelagVfxId.Evade, Prefab = prefab, Prewarm = 2 };
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }
}
