using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Вихрь в «морской пене» (решение владельца 02.10: «вихрь переводим в пену»,
/// без целевого кадра Higgsfield). Та же семья, что серия сабли и рывок:
/// плоская бирюзовая вода, белые комья пены по гребню, тонкий тёмный обвод,
/// вырезанные капли; без свечения и дыма.
///
/// Раскладка и время — как у принятого серпа A (PelagWhirlwindVfxSetup):
/// три кольца на разной высоте с тем же вращением и задержками, расходящееся
/// водяное кольцо на земле, брызги и комья пены вместо щепок и ромбов, всплеск
/// пены на телах целей вместо креста CFXR. Внешний край главного кольца — радиус урона (корень
/// масштабирует контроллер: AuthoredRadius = 1).
///
/// Ассеты пака CFXR: меш «sword_trail 360 thick» (пересчитан в кольцо с
/// гребнем у головы), пузырьковый шум «cfxr sword trail noise bubbles»,
/// клякса «cfxr blood splash dissolve», капля «cfxr water drop blur anim».
/// Шейдеры серии сабли (Razlom/Sabre Foam Wave и Blob) не меняются — свои
/// только материалы M_Whirlwind_Foam* и меши Geometry/WhirlwindFoam*.
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или
/// поднята <see cref="Version"/> (пишется в userData .meta префаба кольца).
/// Сохраняются только свои ассеты (SaveAssetIfDirty), общего SaveAssets нет.
/// Любая правка сборки ниже — поднять Version.
/// </summary>
public static partial class PelagWhirlwindFoamVfxSetup
{
    /// <summary>Версия сборки пенного Вихря. Поднимать при любой правке сборки ниже.</summary>
    private const int Version = 6;
    private static readonly string Revision = "PelagWhirlwindFoamV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string SplatTexture = CfxrGraphics + "cfxr blood splash dissolve.png";
    private const string DropTexture = CfxrGraphics + "cfxr water drop blur anim.png";
    private const string WaveShaderName = "Razlom/Sabre Foam Wave";
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";

    public const string SweepName = "VFX_Pelag_Whirlwind_Sweep_Foam";
    public const string SplashName = "VFX_Pelag_Whirlwind_FoamSplash";
    public static string SweepPath => PrefabFolder + "/" + SweepName + ".prefab";
    public static string SplashPath => PrefabFolder + "/" + SplashName + ".prefab";

    private static readonly string[] MaterialNames =
    {
        "M_Whirlwind_FoamWave", "M_Whirlwind_FoamEcho", "M_Whirlwind_FoamWisp", "M_Whirlwind_FoamGroundRing",
        "M_Whirlwind_FoamDrop", "M_Whirlwind_FoamBit", "M_Whirlwind_FoamSplat"
    };

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
    private static readonly Color AquaShade = new Color(.55f, .84f, .90f, 1f);
    private static readonly Color WhiteShade = new Color(.74f, .90f, .95f, 1f);

    // Жизнь колец, с — как у дуг серпа A (Main .38, Echo .36, Wisp .30).
    private const float MainLife = .38f, EchoLife = .36f, WispLife = .30f, RingLife = .30f;

    [MenuItem("Разлом/Pelag VFX/Вихрь: пересобрать «морскую пену»")]
    public static void RebuildMenu()
    {
        Ensure(true);
        PelagWhirlwindVfxSetup.Install();
    }

    /// <summary>Свои ассеты на месте и собраны этой версией; иначе собрать. false — собрать нечем.</summary>
    public static bool Ensure(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        if (Shader.Find(WaveShaderName) == null || Shader.Find(BlobShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture) == null)
        {
            Debug.LogWarning("[whirlwind-foam] Нет шейдеров пены или текстур CFXR — «морская пена» Вихря не собрана.");
            return false;
        }
        if (!force && UpToDate()) return true;
        return Build();
    }

    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(SweepPath);
        if (importer == null || importer.userData != Revision) return false;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SplashPath) == null) return false;
        foreach (string name in MaterialNames)
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name)) == null) return false;
        return true;
    }

    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    private static bool Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        Mesh thick = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh && mesh.name == "cfxr mesh sword_trail 360 thick") thick = mesh;
        if (thick == null) { Debug.LogWarning("[whirlwind-foam] Нет меша пака «sword_trail 360 thick»."); return false; }
        Shader waveShader = Shader.Find(WaveShaderName);
        Shader blobShader = Shader.Find(BlobShaderName);
        var bubbles = AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture);
        var splat = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture);
        var drop = AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture);

        // Кольца из полосы пака: главное почти замкнуто (330°), эхо 300°, завиток 170°.
        Mesh main = BandMesh("WhirlwindFoamMain", thick, 330f, .36f);
        Mesh echo = BandMesh("WhirlwindFoamEcho", thick, 300f, .22f);
        Mesh wisp = BandMesh("WhirlwindFoamWisp", thick, 170f, .40f);

        // Пена — внешняя половина ширины с рваной границей, у головы гребень гуще.
        // Распад и угасание — к концу жизни, как у дуг серпа A (на 0,25 с кольцо ещё видно).
        Material mainMat = WaveMaterial(waveShader, "M_Whirlwind_FoamWave", bubbles, MainLife,
            .15f, .06f, .18f, .38f, .30f, MainLife, 16f, new Vector4(13f, 1.0f, 5f, .5f));
        mainMat.SetVector("_Bands", new Vector4(.14f, .34f, .50f, .40f));
        mainMat.SetFloat("_EdgeRag", .22f);
        mainMat.SetFloat("_HeadFoam", .45f);
        // Распад идёт от хвоста к голове, а не дырами по всему кольцу: на длинном
        // кольце дыры с тёмным обводом сливались в кружево.
        mainMat.SetFloat("_ErodeAlong", .9f);
        mainMat.SetFloat("_CameraPush", .6f);
        Material echoMat = WaveMaterial(waveShader, "M_Whirlwind_FoamEcho", bubbles, EchoLife,
            .05f, .06f, .14f, .34f, .24f, EchoLife, 24f, new Vector4(12f, .8f, 5f, .4f));
        echoMat.SetFloat("_EdgeRag", .24f);
        echoMat.SetVector("_Bands", new Vector4(.22f, .50f, .58f, .34f));
        echoMat.SetFloat("_ErodeAlong", .9f);
        echoMat.SetFloat("_CameraPush", .5f);
        Material wispMat = WaveMaterial(waveShader, "M_Whirlwind_FoamWisp", bubbles, WispLife,
            .10f, .05f, .12f, .28f, .20f, WispLife, 7.5f, new Vector4(6f, 1.2f, 3f, .6f));
        wispMat.SetFloat("_HeadFoam", .35f);
        wispMat.SetFloat("_CameraPush", .6f);

        // Капли — как у серии сабли; клочья — порог выше (без лучей-звёздочек, как у рывка);
        // клякса на теле — как всплеск сабли.
        // Кольцо на земле — как тонкое кольцо серпа A с чёрным обводом, только водой:
        // замкнутая лента (шум с целым числом плиток, без головы, гребня у головы и
        // штрихов — они не периодичны по u), пена по внешнему краю. Распад — фронтом
        // по u (кольцо «расходится» от одной точки), а не дырами по всей ленте:
        // россыпь тёмных колец на земле читалась грязью у рывка.
        Material ringMat = WaveMaterial(waveShader, "M_Whirlwind_FoamGroundRing", bubbles, RingLife,
            1.02f, .01f, .11f, .28f, .24f, RingLife, 24f, new Vector4(24f, 1.2f, 8f, .5f));
        ringMat.SetFloat("_HeadFoam", 0f);
        ringMat.SetFloat("_Streaks", 0f);
        ringMat.SetFloat("_ErodeAlong", .9f);
        ringMat.SetFloat("_EdgeRag", .22f);
        ringMat.SetFloat("_Glow", .15f);
        ringMat.SetVector("_Bands", new Vector4(.10f, .32f, .50f, .30f));
        ringMat.SetFloat("_CameraPush", 0f);

        Material dropMat = BlobMaterial(blobShader, "M_Whirlwind_FoamDrop", drop, .28f, .80f, 2.0f, .09f, .18f, AquaShade, .6f);
        // Комья пены и лужицы — мягкий эллипс капли пака (кадр листа 1×3), а не клякса:
        // у кляксы со звёздными лучами при высоком пороге оставались только кончики лучей —
        // россыпь точек вместо комка (проба 02.10).
        Material bitMat = BlobMaterial(blobShader, "M_Whirlwind_FoamBit", drop, .30f, .85f, 1.6f, .09f, .12f, WhiteShade, .5f);
        Material splatMat = BlobMaterial(blobShader, "M_Whirlwind_FoamSplat", splat, .26f, .95f, 1.3f, .06f, .12f, WhiteShade, .6f);
        // Тоньше главного кольца: на земле оно вторым планом, как тонкое кольцо серпа A.
        Mesh ring = GroundRingMesh("WhirlwindFoamGroundRing", .18f);

        var materials = new[] { mainMat, echoMat, wispMat, ringMat, dropMat, bitMat, splatMat };
        foreach (Material material in materials) { EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); }
        foreach (Mesh mesh in new[] { main, echo, wisp, ring }) AssetDatabase.SaveAssetIfDirty(mesh);

        SaveSweepPrefab(main, echo, wisp, ring, mainMat, echoMat, wispMat, ringMat, dropMat, bitMat);
        SaveSplashPrefab(splatMat, dropMat, bitMat);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем.
        foreach (Material material in materials)
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(SweepPath);
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[whirlwind-foam] Вихрь «морская пена» собран: три кольца, кольцо на земле, брызги, всплеск на целях, ревизия " + Revision + ".");
        return true;
    }

    // ----------------------------------------------------------------- meshes

    /// <summary>
    /// Кольцо из полосы пака «360 thick» (r .20–1.00, u по часовой от +Y, v=1
    /// снаружи). Угол по u пересчитан: голова (u=1) на местной +Y — там клинок
    /// в миг контакта, хвост уходит против часовой на <paramref name="spanDegrees"/>,
    /// то есть туда, где клинок уже прошёл (сабля идёт по часовой сверху).
    /// Внешний край — окружность r=1 (радиус урона); толщина до
    /// <paramref name="compress"/> радиуса растёт от острого хвоста к голове
    /// профилем sin(0,6·π·u)^0,7 — у головы тупой гребень волны. UV и
    /// треугольники пака не трогаются.
    /// </summary>
    private static Mesh BandMesh(string name, Mesh source, float spanDegrees, float compress)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];
            float r = new Vector2(p.x, p.y).magnitude;
            float u = Mathf.Clamp01(uv[i].x);
            // Хвост не сходит в нитку: тонкое остриё с обводом с двух сторон читалось чёрной чертой.
            float profile = Mathf.Lerp(.30f, 1f, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * .6f * u)), .7f));
            float radius = 1f - (1f - r) / .8f * compress * profile;
            float angle = (90f + spanDegrees * (1f - u)) * Mathf.Deg2Rad;
            vertices[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, source.triangles);
        return mesh;
    }

    /// <summary>Замкнутое кольцо радиуса 1 в XY шириной <paramref name="width"/>; u по кругу, v=1 снаружи.</summary>
    private static Mesh GroundRingMesh(string name, float width)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 128;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[2 * i] = direction * (1f - width);
            vertices[2 * i + 1] = direction;
            uv[2 * i] = new Vector2(t, 0f);
            uv[2 * i + 1] = new Vector2(t, 1f);
            if (i == segments) continue;
            int v = i * 2, at = i * 6;
            triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
            triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
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

    private static Material WaveMaterial(Shader shader, string name, Texture2D bubbles, float life,
        float headFrom, float headSeconds, float erodeFrom, float erodeTo, float fadeFrom, float fadeTo,
        float aspect, Vector4 foamScale)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_FoamTex", bubbles);
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetVector("_FoamScale", foamScale);
        material.SetVector("_Bands", new Vector4(.14f, .36f, .56f, .34f));
        material.SetFloat("_EdgeRag", .18f);
        material.SetFloat("_HeadFoam", .22f);
        material.SetFloat("_OutlinePx", 2.2f);
        material.SetFloat("_Aspect", aspect);
        material.SetFloat("_ErodeGain", 1.6f);
        material.SetFloat("_Streaks", .6f);
        material.SetFloat("_Glow", .25f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_HeadFrom", headFrom);
        material.SetFloat("_HeadSeconds", headSeconds);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeTo);
        material.SetFloat("_ErodeAlong", .7f);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeTo);
        material.SetFloat("_FlowSpeed", .9f);
        return material;
    }

    private static Material BlobMaterial(Shader shader, string name, Texture2D mask,
        float cutFrom, float cutTo, float cutPower, float outline, float shadeWidth, Color shade, float push)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_MainTex", mask);
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", shade);
        material.SetFloat("_CutFrom", cutFrom);
        material.SetFloat("_CutTo", cutTo);
        material.SetFloat("_CutPower", cutPower);
        material.SetFloat("_OutlineWidth", outline);
        material.SetFloat("_ShadeWidth", shadeWidth);
        material.SetFloat("_CameraPush", push);
        return material;
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Кольцо Вихря. Корень — как у серпа A: контроллер ставит его в героя на
    /// высоте клинка, поворот X90 + рыскание по клинку (местная +Y — клинок,
    /// +Z — вниз), масштаб — радиус урона в метрах. Кольцо на земле ведёт
    /// PelagWhirlwindSweepView (опускает к земле и раздвигает), остальное —
    /// частицы со своим временем.
    /// </summary>
    private static void SaveSweepPrefab(Mesh main, Mesh echo, Mesh wisp, Mesh ring,
        Material mainMat, Material echoMat, Material wispMat, Material ringMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(SweepName);
        try
        {
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindRing;
            element.DefaultLifetime = .62f;
            element.AuthoredRadius = 1f;
            var view = root.AddComponent<PelagWhirlwindSweepView>();

            // Три кольца серпа A: высота, задержка, стартовый угол и вращение те же.
            ParticleSystem mainLayer = MeshLayer(root, "Main", main, mainMat, MainLife, 0f, 1f, 1f);
            Pop(mainLayer, .86f, 1.03f, .08f, MainLife);
            Spin(mainLayer, -14f, 0f);
            ParticleSystem echoLayer = MeshLayer(root, "Echo", echo, echoMat, EchoLife, .04f, .75f, .96f);
            echoLayer.transform.localPosition = new Vector3(0f, 0f, .16f);
            Pop(echoLayer, .88f, 1.02f, .08f, EchoLife);
            Spin(echoLayer, -11f, 130f);
            ParticleSystem wispLayer = MeshLayer(root, "Wisp", wisp, wispMat, WispLife, .08f, .70f, .55f);
            wispLayer.transform.localPosition = new Vector3(0f, 0f, -.15f);
            Pop(wispLayer, .86f, 1.03f, .08f, WispLife);
            Spin(wispLayer, -12f, 250f);

            // Кольцо на земле: PelagWhirlwindSweepView опускает его на RingDrop и раздвигает
            // .38 → 1.12 радиуса за .22 с — те же числа, что у кольца серпа A.
            ParticleSystem ground = MeshLayer(root, "Ring", ring, ringMat, RingLife, 0f, 1f, 1f);
            view.Ring = ground.GetComponent<ParticleSystemRenderer>();
            view.RingPropertyBlock = false;

            // Брызги с внешнего края веером наружу и клочья пены по кольцу — вместо щепок и ромбов.
            ParticleSystem spray = Drops(root, "Spray", dropMat, 18, .26f, .44f, 2.6f, 5.0f, .07f, .12f, 1.6f, 1.8f);
            var sprayEmission = spray.emission;
            sprayEmission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)7), new ParticleSystem.Burst(.05f, (short)6), new ParticleSystem.Burst(.10f, (short)5)
            });
            CircleShape(spray, 1f);
            ParticleSystem bits = FoamBits(root, "Foam", bitMat, 12, .28f, .44f, .6f, 1.4f, .24f, .40f, .25f);
            var bitsEmission = bits.emission;
            bitsEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)7), new ParticleSystem.Burst(.06f, (short)5) });
            CircleShape(bits, .95f);

            PrefabUtility.SaveAsPrefabAsset(root, SweepPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Всплеск пены на теле цели: местная +Z — ход клинка в точке цели
    /// (контроллер поворачивает корень по касательной), корень у груди ближе
    /// к камере. Масштаб корня — авторский (Begin возвращает его), блик на
    /// острие берёт тот же префаб ×0,42.
    /// </summary>
    private static void SaveSplashPrefab(Material splatMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(SplashName);
        try
        {
            root.transform.localScale = Vector3.one * 1.3f;
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindHit;
            element.DefaultLifetime = .42f;

            ParticleSystem splat = PelagWhirlwindVfxSetup.NewParticles(root, "Splat", 1, .24f, .24f, 0f, 0f, 1.0f, 1.0f);
            var splatMain = splat.main;
            splatMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            splatMain.startColor = DropWhite;
            splatMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var splatShape = splat.shape; splatShape.enabled = false;
            var splatSize = splat.sizeOverLifetime; splatSize.enabled = true;
            splatSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.16f, 1.06f), new Keyframe(.35f, 1f), new Keyframe(1f, .96f)));
            BlobRenderer(splat, splatMat, ParticleSystemRenderMode.Billboard);

            ParticleSystem drops = Drops(root, "Drops", dropMat, 12, .28f, .42f, 3f, 6f, .07f, .13f, 1.5f, 0f);
            var dropsMain = drops.main;
            dropsMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var dropsShape = drops.shape;
            dropsShape.enabled = true;
            dropsShape.shapeType = ParticleSystemShapeType.Cone;
            dropsShape.angle = 30f;
            dropsShape.radius = .05f;

            ParticleSystem bits = FoamBits(root, "Foam", bitMat, 5, .24f, .36f, 1.2f, 2.6f, .14f, .26f, .5f);
            var bitsMain = bits.main;
            bitsMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var bitsShape = bits.shape;
            bitsShape.enabled = true;
            bitsShape.shapeType = ParticleSystemShapeType.Cone;
            bitsShape.angle = 55f;
            bitsShape.radius = .08f;

            PrefabUtility.SaveAsPrefabAsset(root, SplashPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ----------------------------------------------------------------- layers

    /// <summary>Слой-кольцо: одна мешевая частица, ориентация по трансформу, возраст — в шейдер потоком AgePercent.</summary>
    private static ParticleSystem MeshLayer(GameObject root, string name, Mesh mesh, Material material,
        float life, float delay, float opacity, float size)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = life + delay + .05f;
        main.startDelay = delay;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = size;
        main.startSizeY = size;
        main.startSizeZ = 1f;
        main.startColor = new Color(1f, 1f, 1f, opacity);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 1;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.SetActiveVertexStreams(Streams());
        return particles;
    }

    /// <summary>Хлопок масштаба: недолёт → перелёт → размер за <paramref name="seconds"/>.</summary>
    private static void Pop(ParticleSystem layer, float from, float peak, float seconds, float life)
    {
        float end = Mathf.Clamp01(seconds / life);
        var size = layer.sizeOverLifetime; size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, from), new Keyframe(end * .5f, peak), new Keyframe(end, 1f), new Keyframe(1f, 1f)));
    }

    /// <summary>
    /// Вращение вокруг нормали кольца — кривая дуг серпа A: <paramref name="radPerSecond"/>
    /// на выходе, треть к 40% жизни, ноль в конце. Все оси — в одном режиме кривых.
    /// </summary>
    private static void Spin(ParticleSystem layer, float radPerSecond, float startDegrees)
    {
        var main = layer.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(startDegrees * Mathf.Deg2Rad);
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, radPerSecond), new Keyframe(.4f, radPerSecond * .3f), new Keyframe(1f, 0f)));
    }

    /// <summary>Капли: вытянутые по скорости билборды, мягкий эллипс пака режется порогом с обводом.</summary>
    private static ParticleSystem Drops(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
        float gravity, float upward)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
        if (upward > 0f)
        {
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(upward);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
        }
        SoftEllipse(particles);
        BlobRenderer(particles, material, ParticleSystemRenderMode.Stretch);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 1.5f;
        renderer.velocityScale = .025f;
        return particles;
    }

    /// <summary>Клочья пены: клякса пака, случайный поворот, тает порогом.</summary>
    private static ParticleSystem FoamBits(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .7f), new Keyframe(.2f, 1f), new Keyframe(1f, .9f)));
        SoftEllipse(particles);
        BlobRenderer(particles, material, ParticleSystemRenderMode.Billboard);
        return particles;
    }

    /// <summary>Лист капли пака 1×3: мягкий эллипс (у жёсткого порогу не из чего сделать обвод).</summary>
    private static void SoftEllipse(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
    }

    private static void BlobRenderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
    }

    /// <summary>Эмиттер по окружности в плоскости XY корня (горизонталь после X90), наружу.</summary>
    private static void CircleShape(ParticleSystem particles, float radius)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0f;
        shape.arc = 360f;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
        shape.randomDirectionAmount = .12f;
    }

    private static System.Collections.Generic.List<ParticleSystemVertexStream> Streams()
        => new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        };
}
