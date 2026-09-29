using System.Collections.Generic;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// МОМЕНТ УБИЙСТВА — залпы распада по материалу вида и огоньки сущности
/// (поток I, «Мобы леса v2», выбор владельца 29.09, G7 «все да»):
/// ART/characters/act-1-enemies/review/mobs-v2-concepts-2026-09-29/death/
///   01-guardian-bark-burst — кора трескается светом и разлетается щепками,
///     сучьями и листьями, древесная пыль, 6–8 огоньков дугой к герою;
///   02-swarm-rot-crumble — низкий веер трухи, волокон, сухих листьев и земли,
///     оседает кучкой;
///   03-bud-spore-sap-burst — золотая пыльца, капли янтарно-зелёного сока с
///     брызгами на земле, рваные лепестки;
///   04-wendigo-antler-shatter — костяные осколки рогов и клочья мха, пыль,
///     поток 10–12 огоньков.
///
/// Всё из паков CFXR (unlit: в URP освещённые материалы CFXR бледнеют;
/// мягкие частицы сняты — без depth-текстуры они гасят облако у земли; дым —
/// «smoke cloud x4 ab blurred»): щепки «debris wood unlit», комья и сколы
/// «debris unlit 3×3», лист «leave a» с мешем листа, лепестки «petal x4»,
/// брызги «blood splash» одним каналом, свечение «proc glow soft hdr», звезда
/// удара «spikes impact hdr». Наш слой — сгенерированные меши осколков кости,
/// шипов и кусков панциря (цвета вершин Color32, «Align to direction» ведёт
/// локальную −Z), раскладка, цвета и тайминг.
///
/// Собирает в Assets/Resources/VFX/Death:
///   VFX_Death_Bark/Rot/Spore/Shell/Thorn/Bone — залп распада; корень на
///     земле у ног, +Y вверх, +Z — прочь от убийцы, центр тела на 0,8 при
///     масштабе 1 (Хранитель; вид масштабирует корень по EnemyKillBeat.Scale);
///   VFX_Death_Motes — одна система частиц огоньков без эмиссии: частицы
///     ставит EssenceMotesView (SetParticles) по возрасту от тика Sim.
/// Системы залпов — один залп, местные оси, постоянное зерно: EnemyDeathFxView
/// ведёт их Simulate по возрасту от тика события Death.
/// </summary>
public static class EnemyDeathVfxSetup
{
    // V2 (интеграция N, 29.09): вспышка меньше и золотистее — в пробе под
    // камерой боя она тонмапилась в плоский кремовый диск 1,3 м поверх тела.
    private const string Revision = "EnemyDeathVfxV2";
    private const string Root = "Assets/Resources/VFX/Death";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrPetalMaterial = CfxrGraphics + "cfxr petal pink x4 ab lit normal.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrGlowHdr = CfxrGraphics + "cfxr proc glow soft hdr ab.mat";
    private const string CfxrSpikesHdr = CfxrGraphics + "cfxr spikes impact hdr ab.mat";
    private const string CfxrSplashMaterial = CfxrGraphics + "cfxr blood splash dissolve ab.mat";
    private const string CfxrSplashTexture = CfxrGraphics + "cfxr blood splash.png";

    /// <summary>Центр тела при масштабе 1 — тот же, что EnemyPresentationProfile.BurstCentreUnit.</summary>
    private static readonly Vector3 Centre = Vector3.up * EnemyPresentationProfile.BurstCentreUnit;

    // Тона целевых кадров. Кора: светлый излом и тёмная кора, свежая зелень с головы.
    private static readonly Color BarkLight = new Color(.66f, .50f, .34f), BarkDark = new Color(.38f, .27f, .18f);
    private static readonly Color WoodInner = new Color(.84f, .70f, .48f), WoodInnerDark = new Color(.66f, .52f, .34f);
    private static readonly Color LeafLight = new Color(.56f, .74f, .28f), LeafDark = new Color(.36f, .54f, .18f);
    private static readonly Color DustLight = new Color(.80f, .65f, .46f), DustDark = new Color(.62f, .48f, .33f);
    // Труха: тёмная гниль, волокна корней, сухие листья, земля.
    private static readonly Color RotLight = new Color(.42f, .31f, .20f), RotDark = new Color(.22f, .16f, .10f);
    private static readonly Color FiberLight = new Color(.52f, .40f, .26f), FiberDark = new Color(.34f, .25f, .16f);
    private static readonly Color DryLeafLight = new Color(.66f, .58f, .28f), DryLeafDark = new Color(.46f, .40f, .18f);
    private static readonly Color SoilLight = new Color(.50f, .39f, .27f), SoilDark = new Color(.34f, .26f, .18f);
    // Плюй-плод: пыльца, сок, лепестки шляпки.
    private static readonly Color Pollen = new Color(1f, .82f, .36f), PollenDeep = new Color(1f, .66f, .22f);
    private static readonly Color SapLight = new Color(.80f, .74f, .20f), SapDark = new Color(.52f, .62f, .12f);
    private static readonly Color SapSplat = new Color(.56f, .58f, .14f, .78f);
    private static readonly Color PetalLight = new Color(.86f, .18f, .20f), PetalDark = new Color(.58f, .08f, .12f);
    // Панцирь Расщепеня: кора снаружи, светлый скол внутри.
    private static readonly Color ShellLight = new Color(.64f, .50f, .35f), ShellDark = new Color(.40f, .29f, .19f);
    // Шипомёт: тёмные шипы, зеленоватая кора.
    private static readonly Color ThornLight = new Color(.46f, .34f, .22f), ThornDark = new Color(.24f, .17f, .11f);
    private static readonly Color MossBarkLight = new Color(.42f, .44f, .24f), MossBarkDark = new Color(.26f, .28f, .15f);
    // Вендиго: кость рогов, мох, бледная пыль.
    private static readonly Color BoneLight = new Color(.96f, .92f, .82f), BoneDark = new Color(.78f, .72f, .60f);
    private static readonly Color MossLight = new Color(.44f, .60f, .24f), MossDark = new Color(.26f, .40f, .14f);
    private static readonly Color PaleDustLight = new Color(.82f, .78f, .68f), PaleDustDark = new Color(.64f, .60f, .52f);
    // Вспышка удара и огоньки: тёплое золото, не белое.
    private static readonly Color FlashWarm = new Color(1f, .68f, .30f, .7f);
    private static readonly Color CrackWarm = new Color(1f, .60f, .26f, .9f);

    private sealed class Kit
    {
        public Material Dust, Wood, Chunk, Leaf, Petal, Glow, Spikes, Splat, Shard, Mote;
        public Mesh LeafMesh, BoneShard, ThornShard, ShellChunk;
    }

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += () => Install(false);

    [MenuItem("Разлом/Смерть мобов/VFX распада: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Смерть мобов/VFX распада: пересобрать")]
    public static void Build() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisWood) == null)
        {
            Debug.LogWarning("[death-vfx] Нет пака CFXR — залпы распада мобов не собраны.");
            return;
        }
        if (!force && Built()) return;
        Rebuild();
    }

    private static readonly string[] PrefabNames =
    {
        "VFX_Death_Bark", "VFX_Death_Rot", "VFX_Death_Spore", "VFX_Death_Shell",
        "VFX_Death_Thorn", "VFX_Death_Bone", "VFX_Death_Motes"
    };

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(Root + "/VFX_Death_Bark.prefab");
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in PrefabNames)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + name + ".prefab") == null) return false;
        return true;
    }

    private static void Rebuild()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "Death");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var kit = Materials();
        SaveBark(kit);
        SaveRot(kit);
        SaveSpore(kit);
        SaveShell(kit);
        SaveThorn(kit);
        SaveBone(kit);
        SaveMotes(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(Root + "/VFX_Death_Bark.prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[death-vfx] Залпы распада (кора, труха, споры, панцирь, шипы, кость) и огоньки собраны, ревизия " + Revision + ".");
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        return new Kit
        {
            Dust = Plain(PackCopy("M_Death_Dust", CfxrSmokeBlurred)),
            Wood = Plain(PackCopy("M_Death_Wood", CfxrDebrisWood)),
            Chunk = Plain(PackCopy("M_Death_Chunk", CfxrDebrisUnlit)),
            Leaf = Unlit(PackCopy("M_Death_Leaf", CfxrLeafMaterial)),
            Petal = Unlit(PackCopy("M_Death_Petal", CfxrPetalMaterial)),
            Glow = Hdr(Plain(PackCopy("M_Death_Glow", CfxrGlowHdr)), 2.6f),
            Spikes = Hdr(Plain(PackCopy("M_Death_Spikes", CfxrSpikesHdr)), 2.2f),
            Splat = SplatMaterial(),
            Shard = ShardMaterial(),
            Mote = Hdr(Plain(PackCopy("M_Death_Mote", CfxrGlowHdr)), 3f),
            LeafMesh = LoadMesh(CfxrLeafMesh),
            BoneShard = ShardMesh("DeathBoneShard", 6, .16f, new[] { -.5f, -.18f, .22f, .5f }, new[] { .3f, 1f, .85f, 0f }, .26f, 11),
            ThornShard = ShardMesh("DeathThornShard", 5, .10f, new[] { -.85f, .05f, .15f }, new[] { 0f, 1f, .7f }, .08f, 23),
            ShellChunk = ChunkMesh("DeathShellChunk", .38f, 37)
        };
    }

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (source == null) return material;
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят облако у земли.</summary>
    private static Material Plain(Material material)
    {
        if (material == null) return null;
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        material.DisableKeyword("_FADING_ON");
        foreach (string property in new[] { "_UseDissolve", "_UseDissolveOffsetUV", "_UseSP" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        if (material == null) return null;
        Plain(material);
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_BACK", "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        foreach (string property in new[] { "_UseLighting", "_UseNormalMap", "_UseBackLighting", "_CFXR_DITHERED_SHADOWS" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Сила HDR-усиления CFXR: свечение уходит за порог блума, но не выжигает кадр.</summary>
    private static Material Hdr(Material material, float multiply)
    {
        if (material == null) return null;
        if (material.HasProperty("_HdrMultiply")) material.SetFloat("_HdrMultiply", multiply);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Брызги сока: маска «blood splash» одним каналом, без растворения пака; цвет — из частиц.</summary>
    private static Material SplatMaterial()
    {
        var material = Plain(PackCopy("M_Death_Splat", CfxrSplashMaterial));
        if (material == null) return null;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrSplashTexture);
        if (texture != null) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Мешевые осколки (кость, шипы, панцирь) — непрозрачный unlit URP для
    /// частиц: полупрозрачный ubershader CFXR на объёмном меше рисует грани
    /// вразнобой. Цвет — частица × цвета вершин меша (тона граней).
    /// </summary>
    private static Material ShardMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Death_Shard.mat", shader);
        material.SetTexture("_BaseMap", PelagWhirlwindVfxSetup.WhiteTexture());
        material.SetColor("_BaseColor", Color.white);
        foreach (var pair in new[] { ("_Surface", 0f), ("_Cull", 2f), ("_ZWrite", 1f), ("_SrcBlend", 1f),
            ("_DstBlend", 0f), ("_ColorMode", 0f), ("_AlphaClip", 0f) })
            if (material.HasProperty(pair.Item1)) material.SetFloat(pair.Item1, pair.Item2);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    // ------------------------------------------------------------- particles

    private static AnimationCurve Curve(params float[] pairs)
    {
        var keys = new Keyframe[pairs.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    private static Gradient Alpha(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(.001f, fadeIn)),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    /// <summary>
    /// Один залп из точки at: конус вверх, наклонённый на tilt градусов к +Z
    /// (прочь от убийцы — вид ставит +Z залпа от героя к мобу).
    /// </summary>
    private static ParticleSystem Particles(GameObject root, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, Vector3 at, float cone, float radius, float tilt = 18f)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(root.name + "/" + name);
        particles.transform.localPosition = at;
        // Конус пака смотрит по +Z: ось поднимаем вверх и чуть от убийцы.
        Vector3 axis = Quaternion.Euler(tilt, 0f, 0f) * Vector3.up;
        particles.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        return particles;
    }

    private static void Collide(ParticleSystem particles, Transform ground, float bounce)
    {
        var collision = particles.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground);
        collision.bounce = bounce;
        collision.dampen = .65f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .45f;
        collision.minKillSpeed = 0f;
    }

    /// <summary>Атлас tilesX×tilesY: кадр наугад на частицу, без анимации.</summary>
    private static void Sheet(ParticleSystem particles, int tilesX, int tilesY)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = tilesX; sheet.numTilesY = tilesY;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tilesX * tilesY - .01f);
        sheet.cycleCount = 1;
    }

    /// <summary>
    /// Обломки-спрайты: баллистика, отскок от земли корня, лежат и тают в
    /// последней пятой части жизни (след «здесь рассыпался»). aspect < 1 —
    /// частица уже своей высоты: щепка из доски атласа 4×1.
    /// </summary>
    private static ParticleSystem Debris(GameObject root, string name, Material material, int count, Vector3 at, float cone,
        float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float life,
        Color light, Color dark, int tilesX, int tilesY, float aspect = 1f, float tilt = 18f)
    {
        if (material == null) return null;
        var particles = Particles(root, name, count, life * .8f, life, speedMin, speedMax, sizeMin, sizeMax, at, cone, radius, tilt);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        if (aspect < .999f)
        {
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(sizeMin * aspect, sizeMax * aspect);
            main.startSizeY = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
        }
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
        Collide(particles, root.transform, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, tilesX, tilesY);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Мешевые обломки: листья, лепестки, осколки кости и панциря — кувыркаются в 3D и ложатся на землю.</summary>
    private static ParticleSystem Tumblers(GameObject root, string name, Mesh mesh, Material material, int count, Vector3 at,
        float cone, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float drag, float life,
        Color light, Color dark, float tilt = 18f)
    {
        if (material == null) return null;
        var particles = Particles(root, name, count, life * .8f, life, speedMin, speedMax, sizeMin, sizeMax, at, cone, .2f, tilt);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        if (drag > 0f)
        {
            var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(drag);
            limit.dampen = .12f;
        }
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Collide(particles, root.transform, .1f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .82f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>
    /// Шипы: мешевые, летят остриём вперёд («Align to direction» ведёт
    /// локальную −Z меша по вылету), втыкаются и лежат.
    /// </summary>
    private static ParticleSystem Spikes(GameObject root, string name, Mesh mesh, Material material, int count, Vector3 at,
        float cone, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float life, Color light, Color dark)
    {
        if (material == null || mesh == null) return null;
        var particles = Particles(root, name, count, life * .85f, life, speedMin, speedMax, sizeMin, sizeMax, at, cone, .15f);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f);
        var shape = particles.shape;
        shape.alignToDirection = true;
        Collide(particles, root.transform, 0f);
        var collision = particles.collision; collision.dampen = 1f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Пыль: размытые облака CFXR (кадр 2×2 наугад), тормозит, растёт и тает; flat — стелется по земле.</summary>
    private static void Dust(GameObject root, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, bool flat,
        Color light, Color dark)
    {
        if (kit.Dust == null) return;
        // Стоячее облако поднято на половину размера: иначе земля срезала бы его прямой линией.
        var particles = Particles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax,
            flat ? at : at + Vector3.up * (.35f * sizeMax), cone, radius, flat ? 0f : 10f);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.03f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(light.r, light.g, light.b, alpha),
            new Color(dark.r, dark.g, dark.b, alpha));
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(.25f);
        limit.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .45f, .25f, .9f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.06f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        Sheet(particles, 2, 2);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Dust;
        renderer.sortingFudge = flat ? 1f : -1f;
    }

    /// <summary>
    /// Вспышка удара в центре тела: тёплое HDR-свечение и звезда трещин
    /// (spikes impact) на 0,12–0,14 с. Кадр 01: кора лопается светом. Тела не
    /// касается — это частица поверх, живёт три-четыре кадра.
    /// </summary>
    private static void Flash(GameObject root, Kit kit, float size, bool crack)
    {
        if (kit.Glow != null)
        {
            // Ядро вспышки — две трети размера залпа: ореол шире тела читался
            // наклейкой, звезда трещин ниже остаётся во весь размер.
            float core = size * .66f;
            var glow = Particles(root, "Flash", 1, .13f, .13f, 0f, 0f, core, core, Centre, 0f, .01f, 0f);
            var main = glow.main;
            main.startColor = FlashWarm;
            var grow = glow.sizeOverLifetime; grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .3f, 1f, 1f, 1.25f));
            var fade = glow.colorOverLifetime; fade.enabled = true;
            fade.color = Alpha(0f, .25f);
            var renderer = glow.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Glow;
            renderer.sortingFudge = -20f;
        }
        if (!crack || kit.Spikes == null) return;
        var star = Particles(root, "CrackStar", 1, .12f, .12f, 0f, 0f, size * 1.2f, size * 1.3f, Centre, 0f, .01f, 0f);
        var starMain = star.main;
        starMain.startColor = CrackWarm;
        var starGrow = star.sizeOverLifetime; starGrow.enabled = true;
        starGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .35f, 1f, 1f, 1.1f));
        var starFade = star.colorOverLifetime; starFade.enabled = true;
        starFade.color = Alpha(0f, .3f);
        var starRenderer = star.GetComponent<ParticleSystemRenderer>();
        starRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        starRenderer.sharedMaterial = kit.Spikes;
        starRenderer.sortingFudge = -19f;
    }

    /// <summary>Светящаяся пыльца: мелкие HDR-точки, медленно всплывают и гаснут.</summary>
    private static void Glints(GameObject root, Kit kit, string name, int count, Vector3 at, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float life, Color light, Color dark, float lift)
    {
        if (kit.Glow == null) return;
        var particles = Particles(root, name, count, life * .6f, life, speedMin, speedMax, sizeMin, sizeMax, at, 85f, .2f, 0f);
        var main = particles.main;
        main.gravityModifier = -lift;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(.35f);
        limit.dampen = .18f;
        var noise = particles.noise; noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(.35f);
        noise.frequency = .8f;
        noise.scrollSpeed = .4f;
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.05f, .55f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Glow;
        renderer.sortingFudge = -5f;
    }

    /// <summary>Капли сока: тянутся по скорости, падают, гаснут у земли.</summary>
    private static void Drops(GameObject root, Kit kit, string name, int count, Vector3 at, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float life, Color light, Color dark)
    {
        if (kit.Glow == null) return;
        var particles = Particles(root, name, count, life * .7f, life, speedMin, speedMax, sizeMin, sizeMax, at, 70f, .2f);
        var main = particles.main;
        main.gravityModifier = 2.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        Collide(particles, root.transform, 0f);
        var collision = particles.collision; collision.lifetimeLoss = 1f;
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(0f, .7f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = .06f;
        renderer.lengthScale = 1.4f;
        renderer.sharedMaterial = kit.Glow;
    }

    /// <summary>
    /// Пятно на земле: лежит плашмя, растёт за первые 12% жизни (растекается),
    /// держится и тает с fadeFrom. Для брызг сока и кучки трухи.
    /// </summary>
    private static void Decal(GameObject root, string name, Material material, int count, float sizeMin, float sizeMax,
        float life, float fadeFrom, Color light, Color dark, float spread, float fudge, float delay = 0f, int tiles = 1)
    {
        if (material == null) return;
        var particles = Particles(root, name, count, life, life, 0f, 0f, sizeMin, sizeMax, Vector3.up * .02f, 0f, spread, 0f);
        var main = particles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        main.startDelay = delay;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = Mathf.Max(.01f, spread);
        particles.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .3f, .12f, 1f, 1f, 1f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.05f, fadeFrom);
        if (tiles > 1) Sheet(particles, tiles, tiles);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = fudge;
    }

    // --------------------------------------------------------------- prefabs
    //
    // Числа — при масштабе 1 (Хранитель, рост около 1,7 м). Жизнь самой долгой
    // системы каждого залпа держит EnemyDeathFxView.BurstLife — меняешь здесь,
    // проверь там.

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    private static Vector3 At(float height) => Vector3.up * height;

    /// <summary>
    /// Кора (01-guardian-bark-burst; Хранитель, Камнекопыт ×1,15): кора
    /// лопается светом, щепки-доски и сучья веером, тёмные сколы коры, листья
    /// с головы планируют, древесная пыль клубом и по земле.
    /// </summary>
    private static void SaveBark(Kit kit)
    {
        var root = new GameObject("VFX_Death_Bark");
        Flash(root, kit, 1.3f, true);
        Dust(root, kit, "Puff", 6, Centre, 80f, .3f, .6f, 1.5f, .5f, .85f, .5f, .85f, .30f, false, DustLight, DustDark);
        Dust(root, kit, "Skirt", 5, At(.05f), 88f, .35f, 1.3f, 2.3f, .55f, .9f, .5f, .8f, .22f, true, DustLight, DustDark);
        Debris(root, "Splinters", kit.Wood, 18, Centre, 62f, .25f, 3.2f, 5.6f, .16f, .32f, 1.5f, 2.2f, WoodInner, BarkDark, 4, 1, .3f);
        Debris(root, "Twigs", kit.Wood, 6, Centre, 70f, .2f, 2.0f, 3.5f, .10f, .16f, 1.4f, 2.1f, BarkLight, BarkDark, 4, 1, .16f);
        Debris(root, "BarkChips", kit.Chunk, 10, Centre, 55f, .25f, 2.2f, 4.0f, .07f, .14f, 1.6f, 2.0f, BarkLight, BarkDark, 3, 3);
        Tumblers(root, "Leaves", kit.LeafMesh, kit.Leaf, 10, Centre + At(.45f), 75f, 1.6f, 3.2f, .14f, .24f, .45f, 1.4f, 2.2f, LeafLight, LeafDark);
        Save(root);
    }

    /// <summary>
    /// Труха (02-swarm-rot-crumble; Корнеполз ×0,6, Корнехват ×0,95): низкий
    /// веер трухи и волокон, сухие листья, земляная пыль стелется, на месте
    /// тела остаётся тёмная кучка. Вспышка мягкая, без звезды трещин.
    /// </summary>
    private static void SaveRot(Kit kit)
    {
        var root = new GameObject("VFX_Death_Rot");
        Flash(root, kit, 1.0f, false);
        Decal(root, "Heap", kit.Dust, 3, .6f, .85f, 2.6f, .75f, new Color(RotDark.r, RotDark.g, RotDark.b, .55f),
            new Color(SoilDark.r, SoilDark.g, SoilDark.b, .5f), .18f, 2f, 0f, 2);
        Dust(root, kit, "Skirt", 6, At(.05f), 88f, .35f, 1.0f, 2.0f, .5f, .8f, .5f, .8f, .30f, true, SoilLight, SoilDark);
        Dust(root, kit, "Puff", 4, Centre * .7f, 70f, .25f, .4f, 1.0f, .45f, .7f, .45f, .7f, .26f, false, SoilLight, SoilDark);
        Debris(root, "Crumbs", kit.Chunk, 18, Centre * .7f, 70f, .3f, 1.4f, 3.0f, .05f, .11f, 1.8f, 2.2f, RotLight, RotDark, 3, 3, 1f, 10f);
        Debris(root, "Fibers", kit.Wood, 10, Centre * .7f, 65f, .25f, 1.8f, 3.2f, .10f, .20f, 1.5f, 2.1f, FiberLight, FiberDark, 4, 1, .16f, 10f);
        Tumblers(root, "DryLeaves", kit.LeafMesh, kit.Leaf, 8, Centre, 70f, 1.2f, 2.4f, .12f, .20f, .4f, 1.2f, 2.2f, DryLeafLight, DryLeafDark, 10f);
        Save(root);
    }

    /// <summary>
    /// Споры и сок (03-bud-spore-sap-burst; Плюй-плод ×0,75): шляпка лопается
    /// золотой пыльцой, янтарно-зелёные капли падают и растекаются брызгами по
    /// земле, рваные красные лепестки и кусочки листьев кувыркаются.
    /// </summary>
    private static void SaveSpore(Kit kit)
    {
        var root = new GameObject("VFX_Death_Spore");
        Flash(root, kit, 1.3f, false);
        Glints(root, kit, "Pollen", 26, Centre, 1.2f, 3.0f, .05f, .10f, 1.4f, Pollen, PollenDeep, .05f);
        Dust(root, kit, "PollenHaze", 5, Centre, 80f, .25f, .5f, 1.2f, .45f, .75f, .5f, .8f, .28f, false, Pollen, PollenDeep);
        Drops(root, kit, "Sap", 16, Centre, 2.0f, 4.0f, .05f, .09f, .9f, SapLight, SapDark);
        Decal(root, "Splat", kit.Splat, 3, .45f, .8f, 2.6f, .7f, SapSplat, new Color(SapDark.r, SapDark.g, SapDark.b, .7f), .35f, 2f, .12f);
        Debris(root, "Petals", kit.Petal, 12, Centre + At(.15f), 72f, .25f, 2.0f, 3.8f, .14f, .24f, .5f, 2.2f, PetalLight, PetalDark, 2, 2);
        Tumblers(root, "LeafBits", kit.LeafMesh, kit.Leaf, 6, Centre, 70f, 1.4f, 2.8f, .10f, .16f, .45f, 1.4f, 2.0f, LeafLight, LeafDark);
        Save(root);
    }

    /// <summary>
    /// Сколы панциря (Расщепень, детёныш ×0,6): поверх его раскола
    /// (VFX_Splitter_Burst и две половины коры SplitterCombatView) — куски
    /// панциря кувыркаются и ложатся, мелкие сколы веером. Пыли нет: её даёт
    /// раскол.
    /// </summary>
    private static void SaveShell(Kit kit)
    {
        var root = new GameObject("VFX_Death_Shell");
        Flash(root, kit, 1.1f, true);
        Tumblers(root, "ShellShards", kit.ShellChunk, kit.Shard, 8, Centre, 60f, 2.6f, 4.4f, .14f, .26f, 1.5f, 0f, 1.9f, ShellLight, ShellDark);
        Debris(root, "ShellChips", kit.Chunk, 12, Centre, 60f, .25f, 2.4f, 4.6f, .06f, .12f, 1.6f, 1.8f, ShellLight, ShellDark, 3, 3);
        Save(root);
    }

    /// <summary>
    /// Шипы (Шипомёт): тёмные шипы летят остриём вперёд и втыкаются, сколы
    /// замшелой коры, пара щепок и листьев, пыль.
    /// </summary>
    private static void SaveThorn(Kit kit)
    {
        var root = new GameObject("VFX_Death_Thorn");
        Flash(root, kit, 1.2f, true);
        Dust(root, kit, "Puff", 5, Centre, 80f, .3f, .6f, 1.4f, .5f, .8f, .5f, .8f, .28f, false, DustLight, DustDark);
        Dust(root, kit, "Skirt", 4, At(.05f), 88f, .35f, 1.2f, 2.2f, .5f, .8f, .5f, .8f, .2f, true, DustLight, DustDark);
        Spikes(root, "Thorns", kit.ThornShard, kit.Shard, 14, Centre, 70f, 3.0f, 5.5f, .22f, .36f, 1.2f, 2.2f, ThornLight, ThornDark);
        Debris(root, "BarkChips", kit.Chunk, 10, Centre, 60f, .25f, 2.0f, 3.8f, .07f, .13f, 1.6f, 2.0f, MossBarkLight, MossBarkDark, 3, 3);
        Debris(root, "Splinters", kit.Wood, 8, Centre, 62f, .25f, 2.6f, 4.6f, .14f, .26f, 1.5f, 2.1f, ThornLight, ThornDark, 4, 1, .25f);
        Tumblers(root, "Leaves", kit.LeafMesh, kit.Leaf, 5, Centre + At(.3f), 75f, 1.4f, 2.8f, .12f, .2f, .45f, 1.4f, 2.0f, LeafLight, LeafDark);
        Save(root);
    }

    /// <summary>
    /// Кость и мох (04-wendigo-antler-shatter; Вендиго ×1,35): осколки рогов
    /// кувыркаются и ложатся белыми обломками, костяные щепки, клочья мха и
    /// листья, бледная пыль. Обломки лежат дольше всех — это элитная смерть.
    /// </summary>
    private static void SaveBone(Kit kit)
    {
        var root = new GameObject("VFX_Death_Bone");
        Flash(root, kit, 1.4f, true);
        Dust(root, kit, "Puff", 7, Centre, 80f, .35f, .6f, 1.5f, .6f, 1.0f, .6f, .95f, .34f, false, PaleDustLight, PaleDustDark);
        Dust(root, kit, "Skirt", 6, At(.05f), 88f, .4f, 1.3f, 2.4f, .6f, 1.0f, .55f, .9f, .26f, true, PaleDustLight, PaleDustDark);
        Tumblers(root, "BoneShards", kit.BoneShard, kit.Shard, 12, Centre, 65f, 2.4f, 4.6f, .28f, .46f, 1.4f, 0f, 3.2f, BoneLight, BoneDark);
        Debris(root, "BoneSplinters", kit.Wood, 14, Centre, 62f, .3f, 3.0f, 5.0f, .14f, .26f, 1.5f, 2.8f, BoneLight, BoneDark, 4, 1, .22f);
        Debris(root, "Moss", kit.Chunk, 12, Centre, 60f, .3f, 1.8f, 3.4f, .08f, .16f, 1.5f, 2.8f, MossLight, MossDark, 3, 3);
        Tumblers(root, "Leaves", kit.LeafMesh, kit.Leaf, 10, Centre + At(.3f), 75f, 1.6f, 3.2f, .14f, .24f, .45f, 1.4f, 2.6f, LeafLight, LeafDark);
        Save(root);
    }

    /// <summary>
    /// Огоньки сущности: одна система без эмиссии. EssenceMotesView ставит
    /// частицы сам (SetParticles) — голова, хвост из точек по той же дуге и
    /// вспышка поглощения у героя; зацикленная, чтобы не останавливалась.
    /// </summary>
    private static void SaveMotes(Kit kit)
    {
        var root = new GameObject("VFX_Death_Motes");
        var particles = root.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = .18f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = EnemyPresentationProfile.MotePoolSize * EssenceMotesView.ParticlesPerMote;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Mote;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingFudge = -30f;
        renderer.maxParticleSize = 1f;
        Save(root);
    }

    // ---------------------------------------------------------------- meshes

    /// <summary>Устойчивое число в [min, max) от соли: без UnityEngine.Random, пересборка даёт тот же меш.</summary>
    private static float Hash(int salt, int index, float min, float max)
    {
        uint h = (uint)salt * 2654435761u ^ (uint)index * 40503u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return min + (max - min) * ((h & 0xFFFFFF) / 16777216f);
    }

    /// <summary>
    /// Тон грани в вершинах (Color32 — float-цвета меша ломают цвет частиц):
    /// частица кувыркается, поэтому свет «запечён» по нормали грани к
    /// условному солнцу — грани читаются гранями при любом повороте.
    /// </summary>
    private static Color32 FaceTone(Vector3 normal)
    {
        float light = Vector3.Dot(normal.normalized, new Vector3(.35f, .8f, .48f).normalized);
        byte v = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(.62f, 1f, light * .5f + .5f));
        return new Color32(v, v, v, 255);
    }

    /// <summary>
    /// Осколок-веретено вдоль оси Z: кольца на zs с радиусами radius·radii
    /// (0 — острие), грани плоские, радиусы с неровностью jitter. Шип — острие
    /// на −Z: «Align to direction» ведёт локальную −Z по вылету, шип летит
    /// остриём вперёд. Длина по Z — единица, размер задаёт частица.
    /// </summary>
    private static Mesh ShardMesh(string name, int sides, float radius, float[] zs, float[] radii, float jitter, int salt)
    {
        var rings = new List<Vector3[]>();
        for (int r = 0; r < zs.Length; r++)
        {
            var ring = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float angle = (s + Hash(salt, r * 31 + s, -.18f, .18f)) / sides * Mathf.PI * 2f;
                float k = radii[r] * radius * (1f + Hash(salt, r * 57 + s + 7, -jitter, jitter));
                // Лёгкий изгиб осколка: середина чуть смещена вбок.
                float bend = Mathf.Sin((zs[r] + .5f) * Mathf.PI) * radius * .35f;
                ring[s] = new Vector3(Mathf.Cos(angle) * k + bend, Mathf.Sin(angle) * k, zs[r]);
            }
            rings.Add(ring);
        }
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        for (int r = 0; r + 1 < rings.Count; r++)
            for (int s = 0; s < sides; s++)
            {
                int n = (s + 1) % sides;
                Vector3 a = rings[r][s], b = rings[r][n], c = rings[r + 1][n], d = rings[r + 1][s];
                // Наружу — от оси веретена к грани.
                Vector3 centre = (a + b + c + d) * .25f;
                Vector3 outward = new Vector3(centre.x, centre.y, 0f);
                AddFace(vertices, colors, triangles, a, d, c, outward);
                AddFace(vertices, colors, triangles, a, c, b, outward);
            }
        // Торцы с ненулевым радиусом закрыты веером — у кости тупой скол.
        Vector3 first = Vector3.forward * zs[0], last = Vector3.forward * zs[zs.Length - 1];
        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides;
            if (radii[0] > 0f) AddFace(vertices, colors, triangles, first, rings[0][n], rings[0][s], Vector3.back);
            if (radii[radii.Length - 1] > 0f)
                AddFace(vertices, colors, triangles, last, rings[rings.Count - 1][s], rings[rings.Count - 1][n], Vector3.forward);
        }
        return SaveMesh(name, vertices, colors, triangles);
    }

    /// <summary>
    /// Плоская грань с тоном. Обход разворачивается так, чтобы лицевая сторона
    /// (у Unity — нормаль Cross(b − a, c − a)) смотрела по outward: материал
    /// осколков режет задние грани.
    /// </summary>
    private static void AddFace(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.sqrMagnitude < 1e-12f) return;
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        Color32 tone = FaceTone(normal);
        int at = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(tone); colors.Add(tone); colors.Add(tone);
        triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
    }

    /// <summary>
    /// Кусок панциря: сплюснутый неровный многогранник (октаэдр, разбитый
    /// пополам по граням, с разбросом вершин) — корка сверху, скол по бокам.
    /// Наибольший размер около единицы.
    /// </summary>
    private static Mesh ChunkMesh(string name, float flatten, int salt)
    {
        Vector3[] corners =
        {
            new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 c = corners[i] * .5f;
            c += new Vector3(Hash(salt, i * 3, -.14f, .14f), Hash(salt, i * 3 + 1, -.10f, .10f), Hash(salt, i * 3 + 2, -.14f, .14f));
            c.y *= flatten;
            corners[i] = c;
        }
        int[,] faces = { { 0, 4, 2 }, { 0, 2, 5 }, { 0, 5, 3 }, { 0, 3, 4 }, { 1, 2, 4 }, { 1, 5, 2 }, { 1, 3, 5 }, { 1, 4, 3 } };
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        for (int f = 0; f < faces.GetLength(0); f++)
        {
            Vector3 a = corners[faces[f, 0]], b = corners[faces[f, 1]], c = corners[faces[f, 2]];
            // Каждую грань делим пополам через середину ребра со сдвигом — скол неровный.
            Vector3 mid = (b + c) * .5f + (b + c - 2f * a).normalized * Hash(salt, 100 + f, -.04f, .06f);
            // Многогранник выпуклый вокруг нуля: наружу — к центру грани.
            AddFace(vertices, colors, triangles, a, b, mid, a + b + mid);
            AddFace(vertices, colors, triangles, a, mid, c, a + mid + c);
        }
        return SaveMesh(name, vertices, colors, triangles);
    }

    private static Mesh SaveMesh(string name, List<Vector3> vertices, List<Color32> colors, List<int> triangles)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        var uv = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) uv.Add(new Vector2(.5f, .5f));
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
