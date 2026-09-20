using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PelagOrdnanceVfxSetup
{
    private const string Folder = "Assets/Resources/VFX/Pelag/Ordnance";
    private const string Version = "Pelag Ordnance painted v10";
    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;
    public static void ValidateProductionAssets()
    {
        foreach (string name in new[] { "BottleExplosion", "BurningOil" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");
            if (prefab == null || prefab.GetComponentInChildren<ParticleSystem>(true) == null)
                throw new System.InvalidOperationException("Missing painted Pelag effect: " + name);
        }
    }
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/VFX/Pelag", "Ordnance");
        string texturePath = Folder + "/Pelag_FireBurst_Atlas.png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) return;
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        if (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency)
        {
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp; importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048; importer.SaveAndReimport();
        }
        Build("BottleExplosion", texture, false);
        string oilPath = Folder + "/Pelag_OilFlame_Atlas.png";
        var oilTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(oilPath);
        if (oilTexture == null) throw new System.InvalidOperationException("Missing painted oil flame atlas");
        var oilImporter = (TextureImporter)AssetImporter.GetAtPath(oilPath);
        if (oilImporter.mipmapEnabled || oilImporter.textureCompression != TextureImporterCompression.Uncompressed || !oilImporter.alphaIsTransparency)
        {
            oilImporter.mipmapEnabled = false; oilImporter.alphaIsTransparency = true;
            oilImporter.textureCompression = TextureImporterCompression.Uncompressed;
            oilImporter.wrapMode = TextureWrapMode.Clamp; oilImporter.npotScale = TextureImporterNPOTScale.None;
            oilImporter.maxTextureSize = 2048; oilImporter.SaveAndReimport();
        }
        Build("BurningOil", oilTexture, true);
    }
    private static void Build(string name, Texture2D texture, bool loop)
    {
        string path = Folder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null && AssetImporter.GetAtPath(path).userData == Version) return;
        string materialPath = Folder + "/" + name + "Fire.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Razlom/Ordnance Fire"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_MainTex", texture); material.SetFloat("_Energy", loop ? 1.15f : 1.45f);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var root = new GameObject(name);
        try
        {
            var emitter = new GameObject("Painted flame"); emitter.transform.SetParent(root.transform,false);
            float size = loop ? 1.45f : 2.2f;
            var ps = emitter.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = loop; main.duration = loop ? 1 : .48f; main.playOnAwake = false;
            main.startLifetime = loop ? new ParticleSystem.MinMaxCurve(.86f,1.16f) : new ParticleSystem.MinMaxCurve(.48f); main.startSpeed = 0;
            main.startSize = size; main.startColor = Color.white;
            main.startSize3D = true; main.startSizeX = size; main.startSizeY = size*(loop ? 1.3f : .66f); main.startSizeZ = size;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None; main.maxParticles = loop ? 4 : 1;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = loop ? 2.2f : 0;
            if (!loop) emission.SetBursts(new[] {new ParticleSystem.Burst(0,1)});
            var animation = ps.textureSheetAnimation; animation.enabled = true;
            animation.numTilesX = animation.numTilesY = 4; animation.cycleCount = 1;
            animation.frameOverTime = new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,.999f));
            if (loop)
            {
                var color = ps.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(.88f,.2f),new GradientAlphaKey(.75f,.6f),new GradientAlphaKey(0,1)});
                color.color = gradient;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.pivot = new Vector3(0,loop ? .43f : .37f,0);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = 5731;
            PrefabUtility.SaveAsPrefabAsset(root,path);
            var importer = AssetImporter.GetAtPath(path); importer.userData = Version; importer.SaveAndReimport();
        }
        finally { Object.DestroyImmediate(root); }
    }
}
