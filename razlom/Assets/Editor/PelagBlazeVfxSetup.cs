using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Огонь «Ладно смазал»: поджиг, пламя на клинке и огненное попадание.
///
/// Пламя клинка использует авторский Blade Fire из Arcadia; разовые вспышки
/// и попадания сохраняют Cartoon FX Remaster, общий с остальными умениями.
/// </summary>
public static class PelagBlazeVfxSetup
{
    private const string Pack = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
    private const string IgniteSource = Pack + "Fire/CFXR3 Hit Fire B (Air).prefab";
    private const string BladeSource = Pack + "Fire/CFXR Fire.prefab";
    private const string HitSource = Pack + "Sword Trails/Fire/CFXR4 Sword Hit FIRE (Cross).prefab";
    private const string Folder = "Assets/Resources/VFX/Pelag/Prefabs/";
    private const string Library = "Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить огонь «Ладно смазал»")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        InstallBottle();
        InstallFire();
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(Library);
        if (library == null) return;

        // Пламя на клинке живёт всё усиление, поэтому оно зациклено; вспышка и
        // попадание — разовые.
        Register(library, PelagVfxId.BlazeIgnite, IgniteSource, "VFX_Pelag_Blaze_Ignite",
            lifetime: .35f, scale: .55f, loop: false, prewarm: 2);
        Register(library, PelagVfxId.BlazeBlade, BladeSource, "VFX_Pelag_Blaze_Blade",
            lifetime: 3f, scale: .22f, loop: true, prewarm: 1);
        Register(library, PelagVfxId.BlazeHit, HitSource, "VFX_Pelag_Blaze_Hit",
            lifetime: .4f, scale: .5f, loop: false, prewarm: 4);

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static void InstallFire()
    {
        const string folder = "Assets/Resources/VFX/Pelag/BlazeFire/";
        var shader = Shader.Find("Razlom/Blade Fire");
        if (shader == null) return;
        foreach (string texture in new[] { "Pelag_Arcadia_BladeFire_Atlas", "Pelag_Arcadia_Smoke", "Pelag_Arcadia_Soft" })
        {
            var importer = AssetImporter.GetAtPath(folder + texture + ".png") as TextureImporter;
            if (importer == null) continue;
            if (importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }
        FireMaterial("M_ArcadiaBladeFire", "Pelag_Arcadia_BladeFire_Atlas", 1, 2.1f, .8f);
        FireMaterial("M_ArcadiaFlameParticle", "Pelag_Arcadia_BladeFire_Atlas", 2, 1.1f, 0);
        FireMaterial("M_ArcadiaEmber", "Pelag_Arcadia_Soft", 0, 2, 0);
        void FireMaterial(string name, string texture, float atlas, float energy, float halo)
        {
            string path = folder + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + texture + ".png"));
            mat.SetFloat("_Atlas", atlas); mat.SetFloat("_Energy", energy); mat.SetFloat("_Halo", halo);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }
    }

    private static void InstallBottle()
    {
        const string folder="Assets/Resources/Weapons/Pelag/BlazeBottle/";
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"BlazeBottle.fbx");
        if(model==null)return;
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"BlazeBottle_BaseColor.png");
        var importer=AssetImporter.GetAtPath(folder+"BlazeBottle_BaseColor.png") as TextureImporter;
        if(importer!=null && importer.maxTextureSize!=1024){importer.maxTextureSize=1024;importer.SaveAndReimport();}
        var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"M_BlazeBottle.mat");
        if(mat==null){mat=new Material(Shader.Find("Razlom/Texture Toon"));AssetDatabase.CreateAsset(mat,folder+"M_BlazeBottle.mat");}
        mat.SetTexture("_BaseMap",texture);EditorUtility.SetDirty(mat);
        var liquid=AssetDatabase.LoadAssetAtPath<Material>(folder+"M_BlazeLiquid.mat");
        if(liquid==null){liquid=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(liquid,folder+"M_BlazeLiquid.mat");}
        liquid.SetColor("_BaseColor",new Color(2.4f,1.1f,.04f,1));EditorUtility.SetDirty(liquid);
        if(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"Pelag_BlazeBottle.prefab")!=null)return;
        var root=new GameObject("Pelag Blaze Bottle");
        try
        {
            var body=Object.Instantiate(model,root.transform,false);
            foreach(var r in body.GetComponentsInChildren<Renderer>())r.sharedMaterial=mat;
            var mouth=new GameObject("Mouth").transform;mouth.SetParent(root.transform,false);mouth.localPosition=new Vector3(0,.0423f,0);
            var hinge=new GameObject("Stopper hinge").transform;hinge.SetParent(root.transform,false);hinge.localPosition=mouth.localPosition;
            foreach(var t in body.GetComponentsInChildren<Transform>())
                if(t.name=="BlazeBottle_Stopper")t.SetParent(hinge,true);
            PrefabUtility.SaveAsPrefabAsset(root,folder+"Pelag_BlazeBottle.prefab");
        }
        finally{Object.DestroyImmediate(root);}
    }

    private static void Register(AbilityVfxLibrary library, PelagVfxId id, string source,
        string name, float lifetime, float scale, bool loop, int prewarm)
    {
        string destination = Folder + name + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
        if (prefab == null)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null) return;
            var root = PrefabUtility.LoadPrefabContents(source);
            try
            {
                root.name = name;
                var element = root.GetComponent<PelagVfxElement>();
                if (element == null) element = root.AddComponent<PelagVfxElement>();
                element.Id = id;
                element.DefaultLifetime = lifetime;
                root.transform.localScale = Vector3.one * scale;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = loop;
                    // Разовые эффекты обязаны уложиться в свой срок: пул
                    // забирает объект обратно по нему, а не по затуханию частиц.
                    if (!loop)
                    {
                        main.duration = lifetime;
                        main.startLifetime = Mathf.Min(main.startLifetime.constant, lifetime);
                    }
                    var emission = ps.emission;
                    emission.enabled = true;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, destination);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == id);
        if (index >= 0 && library.Entries[index].Prefab == prefab) return;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = id, Prefab = prefab, Prewarm = prewarm };
    }
}
