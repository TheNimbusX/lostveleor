using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Серия сабли Пелага — «морская пена» (вариант Б, выбор владельца 01.10,
/// ART/characters/pelag/basic-attack-2026-10-01/1-combo-target-frames.jpg):
/// бирюзовая вода с белой пеной по внешнему краю, тонкий тёмный обвод,
/// капли и клочья пены веером от волны и на телах целей.
///
/// Всё собирается из ассетов пака Cartoon FX Remaster, как Вихрь и
/// Рассекающий: форма волны — полумесяц «sword_trail 180 thick», пересчитанный
/// под сектор удара (Simulation.SabreReach / SabreArcCos); пузырьки пены —
/// «cfxr sword trail noise bubbles»; клочья пены — клякса «cfxr blood splash
/// dissolve»; капли — «cfxr water drop blur anim». Свои у нас только два
/// шейдера (Razlom/Sabre Foam Wave и Blob: полосы воды, порог с обводом,
/// раскадровка по возрасту частицы) и тайминг.
///
/// Четыре префаба:
///  • волна лёгкого удара (1 и 2) — горизонтальный полумесяц на высоте
///    клинка вокруг героя, внешний край на досягаемости сектора; удар 2 —
///    зеркало удара 1 (контроллер переворачивает корень);
///  • стоячая волна добивающего — билборд к камере, как серп Рассекающего,
///    выпуклостью к цели, хвост наверху, голова у земли;
///  • пена добивающего по земле — клочья и брызги по краю сектора, без
///    своего полумесяца: тот вставал на земле второй дугой рядом со
///    стоячей волной, и пена «двоилась» (02.10);
///  • всплеск на теле цели — клякса пены, капли и клочья по ходу клинка;
///    у добивающего контроллер увеличивает корень.
/// Каждый слой — система частиц, рождается от событий Sim (SabreContact,
/// Damage), в кадре ничего не пишется.
/// </summary>
public static class PelagSabreComboVfxSetup
{
    private const string Revision = "PelagSabreFoamV4";
    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string SplatTexture = CfxrGraphics + "cfxr blood splash dissolve.png";
    private const string DropTexture = CfxrGraphics + "cfxr water drop blur anim.png";
    private const string WaveShaderName = "Razlom/Sabre Foam Wave";
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";
    private const string WaveName = "VFX_Pelag_Sabre_Wave";
    private const string CrashName = "VFX_Pelag_Sabre_Crash";
    private const string WashName = "VFX_Pelag_Sabre_Wash";
    private const string SplashName = "VFX_Pelag_Sabre_Splash";

    // Палитра варианта Б: тёмная глубина у внутреннего края, бирюза,
    // светлая вода, белая пена (ядро чуть за порогом блума, холодное), тень
    // пузырьков, тонкий тёмный обвод.
    private static readonly Color Deep = new Color(.03f, .24f, .32f);
    private static readonly Color Water = new Color(.06f, .60f, .68f);
    private static readonly Color Shallow = new Color(.42f, .90f, .92f);
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .86f, .91f);
    private static readonly Color Outline = new Color(.02f, .08f, .11f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color DropAqua = new Color(.50f, .92f, .95f, 1f);

    /// <summary>
    /// Угловой размах волны лёгкого удара, град. Сектор урона — 110°
    /// (SabreArcCos); острые концы полумесяца тонкие, видимое «тело» волны
    /// ложится на сектор, кончики чуть выходят за него.
    /// </summary>
    internal const float WaveSpanDegrees = 140f;
    private const float CrashSpanDegrees = 170f;

    // Жизнь слоёв, с: лёгкий удар уходит до следующего (удары через .27 с),
    // добивающий держится дольше.
    private const float WaveLife = .32f;
    private const float CrashLife = .46f;

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Pelag VFX/Серия сабли: подключить «морскую пену»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Серия сабли: пересобрать «морскую пену»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaveShaderName) == null || Shader.Find(BlobShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null)
        {
            Debug.LogWarning("[sabre-setup] Нет шейдеров пены или текстур CFXR, «морская пена» не собрана.");
            return;
        }
        if (force || !Built()) Build();
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SabreWave, PrefabFolder + "/" + WaveName + ".prefab", 3);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SabreCrash, PrefabFolder + "/" + CrashName + ".prefab", 2);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SabreWash, PrefabFolder + "/" + WashName + ".prefab", 2);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SabreSplash, PrefabFolder + "/" + SplashName + ".prefab", 8);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + WaveName + ".prefab");
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in new[] { CrashName, WashName, SplashName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + name + ".prefab") == null) return false;
        return true;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        Shader waveShader = Shader.Find(WaveShaderName);
        Shader blobShader = Shader.Find(BlobShaderName);
        Mesh thick = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh && mesh.name == "cfxr mesh sword_trail 180 thick") thick = mesh;
        if (thick == null) { Debug.LogWarning("[sabre-setup] Нет меша пака «sword_trail 180 thick»."); return; }
        var bubbles = AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture);
        var splat = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture);
        var drop = AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture);

        // Полумесяц пака: полоса r .20–1.00 с острыми концами. Сжатая
        // поперёк до трети радиуса и утолщённая к голове — гребень волны
        // нарастает за клинком (целевой кадр), хвост тонкий.
        Mesh wave = WaveMesh("SabreWave", thick, WaveSpanDegrees, .48f);
        Mesh crash = WaveMesh("SabreWaveHeavy", thick, CrashSpanDegrees, .50f);

        Material waveMain = WaveMaterial(waveShader, "M_Sabre_Wave", bubbles, WaveLife, .12f, .055f, .10f, .28f, .22f, .32f, 7f);
        Material waveEcho = WaveMaterial(waveShader, "M_Sabre_WaveEcho", bubbles, WaveLife, .05f, .06f, .05f, .20f, .14f, .26f, 7f);
        waveEcho.SetFloat("_EdgeRag", .24f);
        waveEcho.SetVector("_Bands", new Vector4(.22f, .50f, .58f, .34f));
        Material crashMain = WaveMaterial(waveShader, "M_Sabre_Crash", bubbles, CrashLife, .10f, .07f, .14f, .36f, .28f, .42f, 5f);
        Material crashEcho = WaveMaterial(waveShader, "M_Sabre_CrashEcho", bubbles, CrashLife, .05f, .08f, .10f, .28f, .22f, .36f, 5f);
        crashEcho.SetFloat("_EdgeRag", .24f);
        crashEcho.SetVector("_Bands", new Vector4(.22f, .50f, .58f, .34f));
        // Сдвиг к камере, м: волна лёгкого удара идёт на высоте груди сквозь
        // тела целей — без сдвига они прятали её почти целиком (съёмка V2).
        // Стоячая волна уже подтянута к камере расстановкой.
        waveMain.SetFloat("_CameraPush", 1.0f);
        waveEcho.SetFloat("_CameraPush", .9f);
        crashMain.SetFloat("_CameraPush", .5f);
        crashEcho.SetFloat("_CameraPush", .4f);
        foreach (var material in new[] { waveMain, waveEcho, crashMain, crashEcho }) EditorUtility.SetDirty(material);

        Material foam = BlobMaterial(blobShader, "M_Sabre_Foam", splat, .30f, .95f, 1.3f, .06f, .15f);
        Material dropMaterial = BlobMaterial(blobShader, "M_Sabre_Drop", drop, .28f, .80f, 2.0f, .09f, .18f);
        foam.SetFloat("_CameraPush", .6f);
        dropMaterial.SetFloat("_CameraPush", .6f);

        SaveWavePrefab(wave, waveMain, waveEcho, foam, dropMaterial);
        SaveCrashPrefab(crash, crashMain, crashEcho, foam, dropMaterial);
        SaveWashPrefab(foam, dropMaterial);
        SaveSplashPrefab(foam, dropMaterial);

        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + WaveName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[sabre-setup] «Морская пена» собрана из CFXR: волна, стоячая волна, накат, всплеск, ревизия " + Revision + ".");
    }

    // ----------------------------------------------------------------- meshes

    /// <summary>
    /// Полумесяц из полосы пака под сектор удара. Исходник — ровная полоса
    /// r .20–1.00 на 180° в XY (три ряда поперёк, форму серпа паку давала
    /// маска плёнки), выпуклость к −X, u=0 у угла −90°, u=1 у +90°, v=1 на
    /// внешней окружности r=1. Углы сжимаются от 180° к
    /// <paramref name="spanDegrees"/>; толщина (1 − r) — до
    /// <paramref name="compress"/> в самом широком месте и сходит на нет к
    /// обоим концам профилем sin(π·u^1.6)^.6 — острые концы, гребень ближе к
    /// голове (u≈.65), как у волны на целевом кадре. Внешний край остаётся
    /// окружностью. UV и треугольники пака не трогаются.
    /// </summary>
    private static Mesh WaveMesh(string name, Mesh source, float spanDegrees, float compress)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        float k = spanDegrees / 180f;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];
            float r = new Vector2(p.x, p.y).magnitude;
            float theta = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            if (theta < 0f) theta += 360f;
            float phi = (theta - 180f) * k;
            float u = Mathf.Clamp01(uv[i].x);
            float profile = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 1.6f))), .6f);
            float radius = 1f - (1f - r) / .8f * compress * profile;
            float angle = (180f + phi) * Mathf.Deg2Rad;
            vertices[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, source.triangles);
        return mesh;
    }

    // -------------------------------------------------------------- materials

    /// <summary>Материал с нуля при сохранении ассета и GUID (см. PelagCleaveVfxSetup.Fresh).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static Material WaveMaterial(Shader shader, string name, Texture2D bubbles, float life,
        float headFrom, float headSeconds, float erodeFrom, float erodeTo, float fadeFrom, float fadeTo, float aspect)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_FoamTex", bubbles);
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetVector("_FoamScale", new Vector4(5f, 1.4f, 2f, .7f));
        // Пена — внешние ~45% ширины, граница рваная: на кадре V2 белого было мало.
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
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material BlobMaterial(Shader shader, string name, Texture2D mask,
        float cutFrom, float cutTo, float cutPower, float outline, float shade)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_MainTex", mask);
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", new Color(.55f, .84f, .90f, 1f));
        material.SetFloat("_CutFrom", cutFrom);
        material.SetFloat("_CutTo", cutTo);
        material.SetFloat("_CutPower", cutPower);
        material.SetFloat("_OutlineWidth", outline);
        material.SetFloat("_ShadeWidth", shade);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Волна лёгкого удара. Корень — в герое на высоте клинка, масштаб —
    /// внешний радиус в метрах; местная −X — направление удара, +Y — сторона,
    /// куда уходит клинок (голова волны), +Z — нормаль плоскости. Удар 2 —
    /// тот же префаб, корень повёрнут на 180° вокруг оси удара.
    /// </summary>
    private static void SaveWavePrefab(Mesh mesh, Material main, Material echo, Material foam, Material drop)
    {
        var root = new GameObject(WaveName);
        try
        {
            AddElement(root, PelagVfxId.SabreWave, .60f);
            // Основная волна: хлопок масштаба и доворот по ходу клинка — волна
            // проходит сквозь сектор, а не висит.
            ParticleSystem wave = MeshLayer(root, "Wave", mesh, main, WaveLife, 0f, 1f, 1f);
            Pop(wave, .86f, 1.03f, .08f, WaveLife);
            Spin(wave, -10f, .24f, WaveLife, 0f);
            // Эхо: чуть шире и ниже, отстаёт и рассыпается первым — второй
            // гребень, глубина воды.
            ParticleSystem echoLayer = MeshLayer(root, "Echo", mesh, echo, WaveLife, .03f, .55f, 1.05f);
            echoLayer.transform.localPosition = new Vector3(0f, 0f, -.05f);
            Pop(echoLayer, .90f, 1.02f, .08f, WaveLife);
            Spin(echoLayer, -8f, .24f, WaveLife, 7f);
            // Брызги и клочья пены с внешнего края веером наружу.
            ParticleSystem spray = Drops(root, "Spray", drop, 10, .26f, .42f, 2.6f, 5.0f, .07f, .12f, 1.6f, 1.8f);
            ArcShape(spray, 1f, 180f - WaveSpanDegrees * .5f, WaveSpanDegrees);
            ParticleSystem bits = FoamBits(root, "Foam", foam, 6, .26f, .40f, .5f, 1.3f, .22f, .40f, .2f);
            ArcShape(bits, .96f, 180f - WaveSpanDegrees * .5f, WaveSpanDegrees);
            Save(root, WaveName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Стоячая волна добивающего: корень — билборд к камере, как серп
    /// Рассекающего (контроллер ставит поворот: местная +X — проекция
    /// направления удара на экран). Полумесяц повёрнут на 180°: выпуклость к
    /// цели, хвост наверху, голова у земли — рубящий сверху; доворот против
    /// часовой уводит хвост назад — диагональ.
    /// </summary>
    private static void SaveCrashPrefab(Mesh mesh, Material main, Material echo, Material foam, Material drop)
    {
        var root = new GameObject(CrashName);
        try
        {
            AddElement(root, PelagVfxId.SabreCrash, .85f);
            ParticleSystem wave = MeshLayer(root, "Wave", mesh, main, CrashLife, 0f, 1f, 1f);
            wave.transform.localRotation = Quaternion.Euler(0f, 0f, 180f + 18f);
            Pop(wave, .78f, 1.05f, .10f, CrashLife);
            Spin(wave, -8f, .30f, CrashLife, 0f);
            ParticleSystem echoLayer = MeshLayer(root, "Echo", mesh, echo, CrashLife, .035f, .60f, 1.07f);
            echoLayer.transform.localPosition = new Vector3(0f, 0f, .05f);
            echoLayer.transform.localRotation = Quaternion.Euler(0f, 0f, 180f + 28f);
            Pop(echoLayer, .84f, 1.03f, .10f, CrashLife);
            Spin(echoLayer, -6f, .30f, CrashLife, 0f);
            // Большой веер брызг и клочьев вокруг выпуклости.
            // Капель меньше и живут короче: на съёмке V3 белая «крупа» держалась полсекунды по всему кадру.
            ParticleSystem spray = Drops(root, "Spray", drop, 22, .32f, .55f, 3.5f, 7.0f, .08f, .16f, 1.9f, 1.2f);
            ArcShape(spray, .95f, -100f, 200f);
            ParticleSystem bits = FoamBits(root, "Foam", foam, 10, .30f, .46f, .8f, 2.0f, .30f, .60f, .3f);
            ArcShape(bits, .90f, -100f, 200f);
            Save(root, CrashName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Пена добивающего по земле: оси как у волны лёгкого удара, корень у
    /// ног героя, масштаб — досягаемость; клочья и брызги ложатся по краю
    /// сектора. Своего полумесяца-наката нет (02.10): лёжа на земле, он
    /// расходился со стоячей волной на экране, и при ударе от камеры (вверх
    /// экрана) было видно две параллельные дуги — волна одна.
    /// </summary>
    private static void SaveWashPrefab(Material foam, Material drop)
    {
        var root = new GameObject(WashName);
        try
        {
            AddElement(root, PelagVfxId.SabreWash, .60f);
            ParticleSystem bits = FoamBits(root, "Foam", foam, 8, .30f, .45f, .5f, 1.2f, .30f, .50f, 0f);
            ArcShape(bits, .92f, 180f - WaveSpanDegrees * .5f, WaveSpanDegrees);
            ParticleSystem spray = Drops(root, "Spray", drop, 10, .26f, .42f, 1.6f, 3.4f, .06f, .11f, 1.6f, 2.4f);
            ArcShape(spray, .90f, 180f - WaveSpanDegrees * .5f, WaveSpanDegrees);
            Save(root, WashName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Всплеск на теле цели: местная +Z — ход клинка в точке цели (конус
    /// брызг), корень у груди цели ближе к камере. Масштаб корня меняет всё
    /// разом (размеры, скорости): добивающий и крит крупнее.
    /// </summary>
    private static void SaveSplashPrefab(Material foam, Material drop)
    {
        var root = new GameObject(SplashName);
        try
        {
            AddElement(root, PelagVfxId.SabreSplash, .55f);
            // Клякса пены во весь корпус: два кадра раскрывается, потом
            // втягивает лучи.
            ParticleSystem splat = PelagWhirlwindVfxSetup.NewParticles(root, "Splat", 1, .24f, .24f, 0f, 0f, 1.0f, 1.0f);
            var splatMain = splat.main;
            splatMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            splatMain.startColor = DropWhite;
            splatMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var splatShape = splat.shape; splatShape.enabled = false;
            var splatSize = splat.sizeOverLifetime; splatSize.enabled = true;
            splatSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.16f, 1.06f), new Keyframe(.35f, 1f), new Keyframe(1f, .96f)));
            BlobRenderer(splat, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem drops = Drops(root, "Drops", drop, 12, .28f, .46f, 3f, 6f, .07f, .13f, 1.5f, 0f);
            var dropsMain = drops.main;
            dropsMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var dropsShape = drops.shape;
            dropsShape.enabled = true;
            dropsShape.shapeType = ParticleSystemShapeType.Cone;
            dropsShape.angle = 30f;
            dropsShape.radius = .05f;

            ParticleSystem bits = FoamBits(root, "Foam", foam, 5, .24f, .36f, 1.2f, 2.6f, .14f, .26f, .5f);
            var bitsMain = bits.main;
            bitsMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var bitsShape = bits.shape;
            bitsShape.enabled = true;
            bitsShape.shapeType = ParticleSystemShapeType.Cone;
            bitsShape.angle = 55f;
            bitsShape.radius = .08f;
            Save(root, SplashName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        // Маркер авторского масштаба: контроллер ставит масштаб корня сам.
        element.AuthoredRadius = 1f;
    }

    private static void Save(GameObject root, string name)
        => PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");

    // ----------------------------------------------------------------- layers

    /// <summary>
    /// Слой-волна: одна мешевая частица, ориентация по трансформу системы,
    /// масштаб по иерархии; возраст уходит в шейдер потоком AgePercent.
    /// </summary>
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
    /// Доворот слоя вокруг нормали: начальный угол <paramref name="startDegrees"/>,
    /// затем ещё <paramref name="degrees"/> за <paramref name="seconds"/> с ease-out.
    /// Все оси — в одном режиме кривых (иначе Unity отказывает).
    /// </summary>
    private static void Spin(ParticleSystem layer, float degrees, float seconds, float life, float startDegrees)
    {
        var main = layer.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(startDegrees * Mathf.Deg2Rad);
        float end = Mathf.Clamp01(seconds / life);
        // Средняя скорость ease-out-кривой (пик → треть → ноль) — около 0,45 пика.
        float peak = degrees * Mathf.Deg2Rad / (seconds * .45f);
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, peak), new Keyframe(end * .5f, peak * .33f), new Keyframe(end, 0f), new Keyframe(1f, 0f)));
    }

    /// <summary>
    /// Капли: вытянутые по скорости билборды, мягкий эллипс пака режется
    /// порогом с обводом; белые и бирюзовые вперемешку, падают.
    /// </summary>
    private static ParticleSystem Drops(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
        float gravity, float upward)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = gravity;
        // Размер и скорость капель — в метрах; масштаб корня растягивает только форму эмиттера.
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
        // Лист пака 1×3: полоса, мягкий эллипс, жёсткий эллипс. Берётся
        // мягкий — у жёсткого порогу не из чего сделать обвод.
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
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
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite, new Color(.86f, 1.04f, 1.06f, 1f));
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .7f), new Keyframe(.2f, 1f), new Keyframe(1f, .9f)));
        BlobRenderer(particles, material, ParticleSystemRenderMode.Billboard);
        return particles;
    }

    private static void BlobRenderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
    }

    /// <summary>Эмиттер по дуге окружности в плоскости XY корня: от угла <paramref name="fromDegrees"/> на <paramref name="arcDegrees"/>, наружу.</summary>
    private static void ArcShape(ParticleSystem particles, float radius, float fromDegrees, float arcDegrees)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0f;
        shape.arc = arcDegrees;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
        shape.rotation = new Vector3(0f, 0f, fromDegrees);
    }

    private static System.Collections.Generic.List<ParticleSystemVertexStream> Streams()
        => new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        };
}
