using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagCleaveVfxSetup
{
    private const string Folder = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/";

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Pelag VFX/Подключить Рассекающий удар")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        if (library == null || library.Entries == null) return;
        PrepareSlashVolume();
        PrepareGroundImpact();
        // Готовое рассечение из пака включается на взмахе, отдельно от вспышки контакта.
        Bind(library, PelagVfxId.CleaveSlash, "VFX_Pelag_Cleave_Slash");
        Bind(library, PelagVfxId.CleaveHit, "VFX_Pelag_Cleave_Impact");
        Bind(library, PelagVfxId.CleaveGround, "VFX_Pelag_Cleave_Ground");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static void PrepareSlashVolume()
    {
        string path = Folder + "VFX_Pelag_Cleave_Slash.prefab";
        var importer = AssetImporter.GetAtPath(path);
        const string revision = "PelagCleaveVolumeV3";
        if (importer == null || importer.userData.Contains(revision)) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startLifetime = .25f;
                main.startSpeed = 0f;
                bool edge = particles.name.StartsWith("Wave Depth");
                main.startColor = edge ? new Color(1.35f, .12f, .025f, .8f) : new Color(1.3f, 1.25f, 1.2f, 1f);
                NeutralLifetimeColor(particles);
                if (!particles.name.StartsWith("Wave Depth")) continue;
                bool left = particles.name.EndsWith("Left");
                // Те же меши пака образуют объём, видимый с бокового ракурса.
                particles.transform.localRotation = Quaternion.Euler(0f, left ? -42f : 42f, 0f);
                Color color = main.startColor.color;
                color.a = .70f;
                main.startColor = color;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // Однократная миграция: дальнейшие ручные правки prefab сохраняются.
        importer.userData = (importer.userData + " " + revision).Trim();
        importer.SaveAndReimport();
    }

    private static void PrepareGroundImpact()
    {
        string path = Folder + "VFX_Pelag_Cleave_Ground.prefab";
        var importer = AssetImporter.GetAtPath(path);
        const string revision = "PelagGroundAuthoredV3";
        if (importer != null && importer.userData.Contains(revision)) return;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Ground AOE explosion.prefab");
        if (source == null) return;
        var root = Object.Instantiate(source);
        root.name = "VFX_Pelag_Cleave_Ground";
        try
        {
            // Сохраняем авторские меши, кратер, движение камней и кривые из одного prefab.
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(root);
            RazlomPelagVfxAssetBuilder.ReplaceImportedParticleMaterials(root);
            root.transform.localScale = Vector3.one * .65f;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpeed = 1.6f;
                if (particles.name == "Flash")
                {
                    var emission = particles.emission; emission.enabled = false;
                    continue;
                }
                main.startColor = particles.name == "Stones" ? new Color(.24f, .19f, .17f, 1f)
                    : particles.name == "Crater" ? new Color(.32f, .27f, .24f, 1f)
                    : particles.name == "Smoke" ? new Color(.48f, .43f, .39f, .7f)
                    : new Color(1.1f, .30f, .16f, .85f);
                NeutralLifetimeColor(particles);
                if (particles.name == "Smoke")
                {
                    main.startSizeMultiplier *= .45f;
                    main.startColor = new Color(.45f, .40f, .36f, .38f);
                    var emission = particles.emission;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4) });
                }
                if (particles.name == "Stones") main.startSizeMultiplier *= 1.5f;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    // Локальная копия убирает HDR-засветку, сохраняя текстуры пака.
                    string materialPath = Folder + "M_CleaveGround_" + particles.name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(renderer.sharedMaterial);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    else EditorUtility.CopySerialized(renderer.sharedMaterial, material);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                    if (material.HasProperty("_Emission")) material.SetFloat("_Emission", 1f);
                    EditorUtility.SetDirty(material);
                    renderer.sharedMaterial = material;
                }
            }
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.CleaveGround;
            element.DefaultLifetime = 1.2f;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
        importer = AssetImporter.GetAtPath(path);
        importer.userData = revision;
        importer.SaveAndReimport();
    }

    private static void NeutralLifetimeColor(ParticleSystem particles)
    {
        var overLife = particles.colorOverLifetime;
        if (!overLife.enabled) return;
        var gradient = overLife.color.gradient;
        if (gradient == null) return;
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, gradient.alphaKeys);
        overLife.color = gradient;
    }

    private static void Bind(AbilityVfxLibrary library, PelagVfxId id, string name)
    {
        // Размер, цвет, поворот и кривые остаются в авторском prefab.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab");
        if (prefab == null) return;
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == id);
        if (index >= 0 && library.Entries[index].Prefab == prefab) return;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = id, Prefab = prefab, Prewarm = 3 };
        EditorUtility.SetDirty(library);
    }
}
