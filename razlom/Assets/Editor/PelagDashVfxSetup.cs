using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Рывок Пелага — «Пенный след» (выбор владельца 02.10: кадр А
/// ART/characters/pelag/dash-2026-10-02/1-A-foam-wake.png и корона брызг у
/// передней ноги из А5, 8-A5-wake-arrival-splash.png). Семья «морской пены»
/// серии сабли: плоская бирюзовая вода, белые гребни по краям, тонкий тёмный
/// обвод, вырезанные кремовые капли.
///
/// Собрано на ассетах пака Cartoon FX Remaster, как серия сабли: шум
/// «cfxr sword trail noise bubbles» (комья гребней), «cfxr perlin mid»
/// (струи вдоль рывка и распад на капли), клякса «cfxr blood splash
/// dissolve» и капля «cfxr water drop blur anim» — через шейдер кляксы
/// серии сабли (Razlom/Sabre Foam Blob, не меняется) и её материал капель
/// M_Sabre_Drop (только чтение) для заноса и капель у старта.
/// Наши материалы: след на шейдере Razlom/Dash Foam Wake, клочья пены
/// M_Dash_Foam (клякса с порогом выше — без лучей-звёздочек, проба V5) и
/// белые вырезанные капли короны M_Dash_Drop (капли сабли с голубой тенью
/// на малом размере читались бледно-голубыми, проверка 02.10).
///
/// Два префаба:
///  • след — полоса на земле (меш пишет PelagDashWake каждый кадр по
///    настоящему пути героя), пенный занос у задней ноги и капли у старта
///    (частицы выбрасывает контроллер по пройденному пути);
///  • корона у передней ноги в конце рывка (А5): белые языки пены веером
///    вверх, вырезанные белые капли, комья пены кольцом, пятно воды под
///    ногой; около трети роста героя, один выброс.
/// Всё рождается от событий Sim DashStarted/DashEnded
/// (Game.View/PelagVfxController.Dash.cs).
///
/// МИГРАЦИЯ ПО ВЕРСИИ. На загрузке редактора сборка идёт, только если нет
/// своих ассетов или поднята <see cref="Version"/> (она пишется в userData
/// .meta префаба следа). Сохраняются только свои ассеты
/// (SaveAssetIfDirty) — никакого общего SaveAssets: он сбрасывал на диск
/// чужие грязные материалы и префабы (M_FlamePro, M_Arcadia*, CampForge…).
/// Любая правка материалов или префабов ниже — поднять Version.
/// </summary>
public static class PelagDashVfxSetup
{
    /// <summary>Версия сборки ассетов рывка. Поднимать при любой правке сборки ниже.</summary>
    private const int Version = 9;
    private static readonly string Revision = "PelagDashFoamV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string PerlinTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string SplatTexture = CfxrGraphics + "cfxr blood splash dissolve.png";
    private const string DropTexture = CfxrGraphics + "cfxr water drop blur anim.png";
    private const string SabreDropMaterial = MaterialFolder + "/M_Sabre_Drop.mat";
    private const string WakeShaderName = "Razlom/Dash Foam Wake";
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";
    private const string WakeName = "VFX_Pelag_Dash_Wake";
    private const string SplashName = "VFX_Pelag_Dash_Splash";
    private const string WakeMaterialName = "M_Dash_Wake";
    private const string FoamMaterialName = "M_Dash_Foam";
    private const string CrownDropMaterialName = "M_Dash_Drop";

    private static string WakePrefabPath => PrefabFolder + "/" + WakeName + ".prefab";
    private static string SplashPrefabPath => PrefabFolder + "/" + SplashName + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // Палитра серии сабли (PelagSabreComboVfxSetup, вариант Б).
    private static readonly Color Deep = new Color(.03f, .24f, .32f);
    private static readonly Color Water = new Color(.06f, .60f, .68f);
    private static readonly Color Shallow = new Color(.42f, .90f, .92f);
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .86f, .91f);
    private static readonly Color Outline = new Color(.02f, .08f, .11f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color FoamWhite = new Color(.96f, 1.08f, 1.10f, 1f);
    private static readonly Color DropAqua = new Color(.50f, .92f, .95f, 1f);
    /// <summary>Тень у края белой пены и капель короны: светлая, чтобы мелочь не уходила в голубое.</summary>
    private static readonly Color WhiteShade = new Color(.74f, .90f, .95f, 1f);

    /// <summary>Жизнь короны брызг, с.</summary>
    private const float SplashLife = .50f;

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Рывок/Подключить «Пенный след»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Рывок/Пересобрать «Пенный след»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WakeShaderName) == null || Shader.Find(BlobShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial) == null)
        {
            Debug.LogWarning("[dash-setup] Нет шейдеров пены, текстур CFXR или капель серии сабли — «Пенный след» не собран.");
            return;
        }
        if (force || !UpToDate()) Build();
        // Библиотеку пишем, только если наши записи в ней действительно поменялись.
        bool bound = Bound(library, PelagVfxId.DashWake, WakePrefabPath) && Bound(library, PelagVfxId.DashSplash, SplashPrefabPath);
        if (bound) return;
        bool wake = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.DashWake, WakePrefabPath, 2);
        bool splash = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.DashSplash, SplashPrefabPath, 2);
        if (wake && splash) AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(WakePrefabPath);
        if (importer == null || importer.userData != Revision) return false;
        return AssetDatabase.LoadAssetAtPath<GameObject>(WakePrefabPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(SplashPrefabPath) != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(WakeMaterialName)) != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(FoamMaterialName)) != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(CrownDropMaterialName)) != null;
    }

    private static bool Bound(AbilityVfxLibrary library, PelagVfxId id, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == id);
        return prefab != null && index >= 0 && library.Entries[index].Prefab == prefab && library.Entries[index].Prewarm == 2;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        Material wake = WakeMaterial();
        Material foam = FoamMaterial();
        Material crownDrop = CrownDropMaterial();
        // Капли заноса и у старта — тот же материал, что у серии сабли (только чтение).
        Material drop = AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial);

        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам.
        foreach (Material material in new[] { wake, foam, crownDrop }) AssetDatabase.SaveAssetIfDirty(material);
        SaveWakePrefab(wake, foam, drop);
        SaveSplashPrefab(foam, crownDrop);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем.
        foreach (Material material in new[] { wake, foam, crownDrop })
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(WakePrefabPath);
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[dash-setup] «Пенный след» рывка собран: след, занос, капли, корона пены, ревизия " + Revision + ".");
    }

    // -------------------------------------------------------------- materials

    /// <summary>Материал с нуля при сохранении ассета и GUID (как у серии сабли).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static Material WakeMaterial()
    {
        Material material = Fresh(WakeMaterialName, Shader.Find(WakeShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        // Крупный шум (пятна воды, рваный хвост) и ячейки комьев пены ~15 см.
        material.SetVector("_CoarseScale", new Vector4(.42f, .65f, 0f, 0f));
        material.SetFloat("_ClumpScale", .85f);
        material.SetVector("_Bands", new Vector4(.15f, .75f, 0f, 0f));
        // Гребни: валик пены 7 см у хвоста … 16 см у ног, комья выпирают на 9 см.
        material.SetVector("_Crest", new Vector4(.07f, .16f, .09f, .03f));
        material.SetFloat("_HeadFoam", .6f);
        material.SetFloat("_CrestLength", .9f);
        // Струи вдоль рывка (кадр А): ~4 светлых и ~3 тёмных, длинными отрезками.
        material.SetVector("_Lines", new Vector4(4.5f, 1.6f, .40f, .85f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.6f, .32f, .45f));
        material.SetFloat("_TailRag", .32f);
        material.SetFloat("_HeadRag", .30f);
        material.SetFloat("_OutlinePx", 2.0f);
        material.SetFloat("_Glow", .25f);
        // Распад фронтом от старта к ногам: участок белеет пеной и трескается
        // к возрасту 0,30 ± 0,03 с (хвост старше на 0,11 с — фронт идёт от
        // старта к ногам ~0,3 с), капли ~9 см живут ещё до 0,07 с; у ног
        // последние капли уходят к ~0,40 с после конца рывка.
        material.SetVector("_Break", new Vector4(.30f, .06f, .11f, .04f));
        material.SetFloat("_BreakScale", 2.2f);
        material.SetFloat("_DropLife", .07f);
        material.SetFloat("_EdgeEarly", .03f);
        material.SetFloat("_BreakRimPx", 1.5f);
        material.SetFloat("_FadeFrom", PelagDashWake.LifeAfterEnd - .05f);
        material.SetFloat("_FadeTo", PelagDashWake.LifeAfterEnd - .01f);
        material.SetFloat("_FlowSpeed", .8f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Клочья пены: клякса пака («cfxr blood splash dissolve») на шейдере
    /// кляксы серии сабли, порог с рождения выше — лучи кляксы срезаны,
    /// остаётся комок пены, а не звёздочка. Тень светлая — пена белая.
    /// </summary>
    private static Material FoamMaterial()
    {
        Material material = Fresh(FoamMaterialName, Shader.Find(BlobShaderName));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture));
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", WhiteShade);
        material.SetFloat("_CutFrom", .46f);
        material.SetFloat("_CutTo", .95f);
        material.SetFloat("_CutPower", 1.3f);
        material.SetFloat("_OutlineWidth", .07f);
        material.SetFloat("_ShadeWidth", .12f);
        material.SetFloat("_CameraPush", .3f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Белые вырезанные капли и языки короны (А5): капля пака на шейдере
    /// кляксы, тень светлая и узкая, обвод толще — на игровой камере капля
    /// 6–10 px и иначе уходит в голубую дымку.
    /// </summary>
    private static Material CrownDropMaterial()
    {
        Material material = Fresh(CrownDropMaterialName, Shader.Find(BlobShaderName));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture));
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", WhiteShade);
        material.SetFloat("_CutFrom", .24f);
        material.SetFloat("_CutTo", .82f);
        material.SetFloat("_CutPower", 2.2f);
        material.SetFloat("_OutlineWidth", .11f);
        material.SetFloat("_ShadeWidth", .10f);
        // Почти без сдвига к камере: низ зубцов короны должен уходить под землю.
        material.SetFloat("_CameraPush", .10f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// След: корень — в точке старта на земле, местная +Z — направление
    /// рывка. Меш полосы пишет контроллер; частицы — в мировых координатах,
    /// без своих вспышек: их выбрасывает контроллер по пройденному пути.
    /// </summary>
    private static void SaveWakePrefab(Material wake, Material foam, Material drop)
    {
        var root = new GameObject(WakeName);
        try
        {
            AddElement(root, PelagVfxId.DashWake, PelagDashWake.MaxLife);
            var strip = new GameObject("Wake");
            strip.transform.SetParent(root.transform, false);
            strip.AddComponent<MeshFilter>();
            var renderer = strip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = wake;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // Капли у старта: лежат на земле, тают порогом — круглые кремовые
            // кляксы (мягкий эллипс пака на квадрате).
            ParticleSystem tail = Emitted(root, "TailDrops", 8, .40f);
            var tailMain = tail.main;
            tailMain.startColor = DropWhite;
            DropSheet(tail);
            Renderer(tail, drop, ParticleSystemRenderMode.Billboard);

            // Занос у задней ноги: клочья пены скользят по земле.
            ParticleSystem skidFoam = Emitted(root, "SkidFoam", 48, .44f);
            var foamMain = skidFoam.main;
            foamMain.gravityModifier = .5f;
            foamMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            foamMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, new Color(.86f, 1.04f, 1.06f, 1f));
            var foamSize = skidFoam.sizeOverLifetime; foamSize.enabled = true;
            foamSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .7f), new Keyframe(.2f, 1f), new Keyframe(1f, .9f)));
            var foamDrag = skidFoam.limitVelocityOverLifetime; foamDrag.enabled = true;
            foamDrag.limit = new ParticleSystem.MinMaxCurve(10f);
            foamDrag.drag = new ParticleSystem.MinMaxCurve(4f);
            foamDrag.multiplyDragByParticleSize = false;
            foamDrag.multiplyDragByParticleVelocity = false;
            Renderer(skidFoam, foam, ParticleSystemRenderMode.Billboard);

            // ...и низкие брызги: вытянутые по скорости капли, падают до колена.
            ParticleSystem skidDrops = Emitted(root, "SkidDrops", 48, .34f);
            var dropsMain = skidDrops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(skidDrops);
            Renderer(skidDrops, drop, ParticleSystemRenderMode.Stretch);
            var dropsRenderer = skidDrops.GetComponent<ParticleSystemRenderer>();
            dropsRenderer.lengthScale = 1.5f;
            dropsRenderer.velocityScale = .025f;

            Save(root, WakeName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Корона пены у передней ноги (А5): корень на земле у ноги, местная +Z
    /// — направление рывка. Белые языки пены веером вверх (корона), белые
    /// вырезанные капли выше и шире, комья пены кольцом по земле, пятно воды
    /// под ногой. Высота — около трети роста героя, один выброс; языки
    /// быстро встают и рвутся порогом на капли, а не висят над телом.
    /// </summary>
    private static void SaveSplashPrefab(Material foam, Material crownDrop)
    {
        var root = new GameObject(SplashName);
        try
        {
            AddElement(root, PelagVfxId.DashSplash, SplashLife);

            // Пятно воды под ногой: клякса пака плашмя, бирюзовая с обводом.
            ParticleSystem puddle = Burst(root, "Puddle", 1, .34f, .34f, 0f, 0f, .80f, .80f);
            var puddleMain = puddle.main;
            puddleMain.startColor = DropAqua;
            puddleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var puddleShape = puddle.shape; puddleShape.enabled = false;
            var puddleSize = puddle.sizeOverLifetime; puddleSize.enabled = true;
            puddleSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.18f, 1.02f), new Keyframe(1f, 1.08f)));
            Renderer(puddle, foam, ParticleSystemRenderMode.HorizontalBillboard);
            puddle.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            // Комья пены кольцом по земле вокруг ноги — белое основание короны.
            ParticleSystem bits = Burst(root, "Foam", 8, .30f, .42f, .3f, .9f, .20f, .30f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .4f;
            bitsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            var bitsShape = bits.shape;
            bitsShape.enabled = true;
            bitsShape.shapeType = ParticleSystemShapeType.Circle;
            bitsShape.radius = .18f;
            bitsShape.radiusThickness = 0f;
            bitsShape.rotation = new Vector3(90f, 0f, 0f);
            var bitsSize = bits.sizeOverLifetime; bitsSize.enabled = true;
            bitsSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .7f), new Keyframe(.2f, 1f), new Keyframe(1f, .9f)));
            Renderer(bits, foam, ParticleSystemRenderMode.Billboard);
            bits.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;

            // Языки короны: тонкие белые зубцы кольцом от ноги вверх и веером
            // наружу (А5). Растут из земли: длинная полоса вдоль скорости с
            // центром у самой земли — нижняя половина уходит под землю по
            // глубине, видна корона от ноги до ~0,6 м (треть роста героя).
            // Почти не летят (сильное торможение, без тяжести — зубец не
            // переворачивается вниз), сохнут и рвутся порогом за ~0,25 с.
            ParticleSystem crown = Burst(root, "Crown", 14, .24f, .32f, 2.6f, 3.6f, .08f, .10f);
            var crownMain = crown.main;
            crownMain.gravityModifier = 0f;
            crownMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(crown, 45f, .20f, -84f);
            Drag(crown, 10f);
            var crownSize = crown.sizeOverLifetime; crownSize.enabled = true;
            crownSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .6f), new Keyframe(.15f, 1f), new Keyframe(1f, .55f)));
            DropSheet(crown);
            Renderer(crown, crownDrop, ParticleSystemRenderMode.Stretch);
            var crownRenderer = crown.GetComponent<ParticleSystemRenderer>();
            crownRenderer.lengthScale = 8f;
            crownRenderer.velocityScale = 0f;
            crownRenderer.sortingFudge = -2f;

            // Вырезанные капли: круглые белые с обводом, выше и шире языков.
            ParticleSystem drops = Burst(root, "Drops", 16, .30f, .40f, 4.5f, 6.5f, .06f, .09f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 2.0f;
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(drops, 55f, .14f, -78f);
            Drag(drops, 2f);
            DropSheet(drops);
            Renderer(drops, crownDrop, ParticleSystemRenderMode.Billboard);
            drops.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;

            Save(root, SplashName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = 1f;
    }

    private static void Save(GameObject root, string name)
        => PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");

    // ----------------------------------------------------------------- layers

    /// <summary>
    /// Слой, который выбрасывает контроллер (EmitParams): мировые координаты,
    /// без своих вспышек; играет весь след, чтобы выброс шёл в живую систему.
    /// </summary>
    private static ParticleSystem Emitted(GameObject root, string name, int max, float lifeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, max, lifeMax * .7f, lifeMax, 0f, 0f, .1f, .1f);
        var main = particles.main;
        main.duration = PelagDashWake.MaxLife;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = max;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 0f;
        var shape = particles.shape; shape.enabled = false;
        return particles;
    }

    /// <summary>Разовый слой короны: вспышка частиц в момент выдачи из пула.</summary>
    private static ParticleSystem Burst(GameObject root, string name, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.duration = SplashLife;
        return particles;
    }

    /// <summary>Конус вокруг вертикали, наклонённый вперёд по рывку: <paramref name="pitch"/> −90 — строго вверх.</summary>
    private static void Cone(ParticleSystem particles, float angle, float radius, float pitch)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Линейное торможение: брызги встают рывком и зависают, а не улетают выше колена.</summary>
    private static void Drag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(20f);
        limit.drag = new ParticleSystem.MinMaxCurve(drag);
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    /// <summary>Лист капель пака 1×3: мягкий эллипс — у жёсткого порогу не из чего сделать обвод.</summary>
    private static void DropSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
    }

    private static void Renderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
    }
}
