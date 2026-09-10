using System;
using Game.View;
using UnityEditor;
using UnityEngine;

public static class CommonFootstepVfxSetup
{
    private const string PrefabPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/VFX_Common_FootstepDust.prefab";
    private const string LibraryPath = "Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset";
    private const string MaterialPath = "Assets/Resources/VFX/Pelag/Materials/M_CommonFootstepDust.mat";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить пыль шагов")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (library == null || prefab == null) return;
        bool materialMissing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null;
        if (!materialMissing && library.Entries != null)
            foreach (var entry in library.Entries)
                if (entry.Id == PelagVfxId.FootstepDust && entry.Prefab != null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var element = root.GetComponent<PelagVfxElement>();
            if (element == null) element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.FootstepDust;
            element.DefaultLifetime = 0.55f;
            if (materialMissing)
            {
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                var source = renderer.sharedMaterial;
                var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                material.name = "M_CommonFootstepDust";
                material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", 0f);
                material.SetFloat("_SoftParticlesEnabled", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
                AssetDatabase.CreateAsset(material, MaterialPath);
                renderer.sharedMaterial = material;
                var main = root.GetComponent<ParticleSystem>().main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 0.45f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                main.startColor = new Color(0.85f, 0.75f, 0.57f, 0.75f);
                var emission = root.GetComponent<ParticleSystem>().emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        int index = library.Entries == null ? -1 : Array.FindIndex(library.Entries, e => e.Id == PelagVfxId.FootstepDust);
        if (index < 0)
        {
            index = library.Entries?.Length ?? 0;
            Array.Resize(ref library.Entries, index + 1);
        }
        library.Entries[index] = new AbilityVfxLibrary.Entry
        {
            Id = PelagVfxId.FootstepDust, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), Prewarm = 4
        };
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
        Debug.Log("[Pelag VFX] Authored footstep dust connected.");
    }
}
