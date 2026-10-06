using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// ХОЗЯИН ЧАЩИ — ОДЕЖДА ФАЗ: накладки на кости и компонент на теле (план
/// artifacts/tools/wf/boss-vfx-plan.md §4.6–4.7; владелец 02.10: «босс по фазам не меняется вообще»).
///
/// Префабы — Resources/VFX/ThicketMaster/Phases/Prefabs (их грузит и вешает на кости
/// ThicketMasterPhaseDressing в Awake тела):
/// • VFX_ThicketPhase_Berries (bush) — 24 набухающие ягоды: сначала поверх нарисованных красных
///   (UV вершины красный в T_ThicketMaster_Color), добор — «самая дальняя точка» по кусту;
///   всплеск рёва 66 — ягоды-капли, листья, зелёная пыльца.
/// • VFX_ThicketPhase_BushBloom (bush) — 10 цветков Hovl Flower из куста (6 розовых, 4 белых;
///   красных нет с 02.10 вечер — Ф3 розово-белая); всплеск рёва 33 — лепестки и листья.
/// • VFX_ThicketPhase_CrownBloom_L/R (crown_L/R) — по 22 цветка на верхней стороне кроны
///   (60 % белые, 40 % розовые) и 6 плодов; петля падающих лепестков (Ф3); всплеск рёва 33.
///   Цветы (ревью 02.10 вечер: «цветы — тёмно-синие пятна»): меш — копия Flower.fbx с нормалями
///   к раскрытию чашки (у пака они смотрят вниз, цветок освещался снизу), материал светится мягко
///   своим цветом по текстуре лепестков — розовое и белое читаются и в синей тени арены.
/// • VFX_ThicketPhase_EyeGlow (head) — ореол на каждом глазу (UV-семена глаз из
///   production/dressing/work/eyes.json); всплеск рёва 50 — янтарные искры рун и листья.
/// • VFX_ThicketPhase_Embers (chest) — аура Ф2 (ревью 02.10 «фаза 2 не отличается»; вечер — «угли,
///   лавовый, а не лесной»): с кроны и спины падают осенние листья (петля «Leaf Fall», 4/с), углей —
///   редкие 5/с (было 14); всплеск рёва 66 — листопад осенних листьев и негустой сноп углей.
/// • VFX_ThicketPhase_BloomAura (chest) — аура Ф3: розовый свет плывёт над кроной; всплеск рёва 33 —
///   светящиеся лепестки и розовые искры.
/// Ягоды крупнее (с камеры игры ≈ 120 пикс. на метр при 1080p — ягода в 13 см была точкой) и тлеют красным (эмиссия ягоды,
/// не тела). Свечение углей, глаз и искр — копии мягкого свечения CFXR с _HdrMultiply.
/// Ягоды и цветы — MeshRenderer-дети «Bud|задержка мс|подпись»: вид растит их масштабом от 0.
/// Всё авторится в метрах модели в осях кости (точки — из вершин тела в позе привязки,
/// ручной скиннинг по bindposes), рост узла тела (TargetHeight сборщика) вид добавит сам.
///
/// Паки: CFXR (лепесток petal pink x4 одним каналом, лист leave a + cfxr mesh leave, мягкое
/// свечение proc glow soft ab, облако smoke cloud x4 blurred) — копии без освещения, без
/// dissolve и мягких частиц (URP); Hovl (Flower.fbx + Flower2.png на URP Simple Lit с отсечкой).
/// Материалы — свои копии в Phases/Materials (наборы ATTACKS и DRESSING независимы).
///
/// Компонент ThicketMasterPhaseDressing ставится на ThicketMaster_Runtime.prefab здесь же —
/// после каждой пересборки представления (постпроцессор префаба). Маски эмиссии
/// (T_ThicketMaster_Emission_F2/F3 — make_emission.py) и листва фаз (T_ThicketMaster_Color_F2/F3 —
/// make_phase_colors.py) копируются из пакета в Resources, и материал тела получает эмиссию без
/// пересборки представления.
/// В Play не собирается. Звука нет.
/// </summary>
public static class ThicketMasterDressingSetup
{
    // V3 (02.10 вечер, ревью владельца): цветы — меш с нормалями вверх и мягкое свечение, листопад Ф2,
    // углей меньше, без багрянца в листьях.
    private const string Revision = "ThicketPhaseV3";
    private const string Root = "Assets/Resources/VFX/ThicketMaster/Phases";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrPetal = CfxrGraphics + "cfxr petal pink x4 ab lit normal.mat";
    private const string CfxrLeaf = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrGlowSoft = CfxrGraphics + "cfxr proc glow soft ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrCloudBlur = CfxrGraphics + "cfxr cloud blur.png";
    private const string HovlFlower = "Assets/Hovl Studio/HSFiles/Models/Flower.fbx";
    private const string HovlFlowerTexture = "Assets/Hovl Studio/HSFiles/Textures/Flower2.png";

    /// <summary>Работа маски эмиссии (eyes.json) — рядом с пакетом, от корня репозитория.</summary>
    private const string DressingWork = "ART/characters/act-1-enemies/boss-forest-master/production/dressing/work/";
    private static readonly string[] EmissionMaps =
    {
        "T_ThicketMaster_Emission_F2.png", "T_ThicketMaster_Emission_F3.png",
        "T_ThicketMaster_Color_F2.jpg", "T_ThicketMaster_Color_F3.jpg",
    };

    // Палитра (план §1.2, как у Вендиго и Корнехвата; цвета — «на глаз», без подъёма яркости).
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f), LeafDark = new Color(.42f, .52f, .20f);
    private static readonly Color BerryRed = new Color(.86f, .12f, .10f), BerryDeep = new Color(.52f, .05f, .08f);
    private static readonly Color PetalWhite = new Color(1f, .96f, .92f), PetalPink = new Color(1f, .70f, .80f), PetalGold = new Color(1f, .86f, .45f);
    private static readonly Color RuneSpark = new Color(1f, .55f, .12f);
    private static readonly Color PollenGreen = new Color(.70f, .80f, .36f);
    // Аура Ф2: угли (как Ember атак) и осенние листья (как листва карты F2); Ф3 — розовый свет.
    private static readonly Color Ember = new Color(1f, .58f, .16f), EmberDeep = new Color(.95f, .36f, .08f);
    private static readonly Color AutumnRed = new Color(.82f, .20f, .08f), AutumnOrange = new Color(.93f, .46f, .10f), AutumnGold = new Color(.90f, .70f, .20f);
    private static readonly Color BloomLight = new Color(1f, .62f, .80f), BloomLightPale = new Color(1f, .84f, .90f);

    /// <summary>Сколько накладок и какого размера (метры модели в позе привязки, рост 3,57 м).</summary>
    private const int BerryCount = 24, BushFlowerCount = 10, CrownFlowerCount = 22, CrownFruitCount = 6, RuneSparkPoints = 24;
    private const float BerryMin = .14f, BerryMax = .22f, BushFlowerMin = .22f, BushFlowerMax = .34f;
    private const float CrownFlowerMin = .16f, CrownFlowerMax = .28f, FruitSize = .11f, EyeGlowSize = .30f;
    private const float BerryMerge = .06f, EyeLift = .08f;

    /// <summary>Аура — точки верха спины и кроны (нормаль вверх), с них поднимаются угли и свет.</summary>
    private static readonly string[] AuraBones = { "hips", "spine_01", "spine_02", "spine_03", "chest", "neck_01", "neck_02", "crown_L", "crown_R" };
    private const int AuraPoints = 48;

    /// <summary>Свечение (_HdrMultiply CFXR): угли, глаза и искры — за порог bloom 1,05; розовый свет мягче.</summary>
    private const float EmberHdr = 2.6f, BloomLightHdr = 1.5f, GlowPetalHdr = 1.6f;

    /// <summary>Ягоды тлеют красным: эмиссия ягоды = её цвет × это (тело не трогается).</summary>
    private const float BerryEmission = .75f;

    /// <summary>
    /// Цветы светятся мягко своим цветом по текстуре лепестков (гамма; белый ≈ .22 линейной яркости):
    /// в синей тени розовый и белый иначе синеют. На солнце лепесток с подсветкой чуть цепляет блум.
    /// </summary>
    private const float FlowerGlow = .5f;

    /// <summary>Нормали цветка наклоняются к оси чашки на столько (лепестки ловят свет сверху ровно).</summary>
    private const float FlowerNormalLift = .6f;

    /// <summary>Петли Ф2: листопад с кроны и редкие угли, в секунду (ярость ×1,4 — вид).</summary>
    private const float LeafFallRate = 4f, EmberRate = 5f;

    private sealed class Kit
    {
        public Material Berry, BerryDeep, BerryDrop, FlowerWhite, FlowerPink, FlowerRed, Petal, Leaf, Glow, Haze;
        /// <summary>Светящиеся копии (_HdrMultiply): угли/глаза/искры, розовый свет, лепестки всплеска Ф3.</summary>
        public Material Ember, BloomLight, GlowPetal;
        public Mesh Sphere, Flower, LeafMesh;
        public float FlowerSpan = 1f;
        public Quaternion FlowerBase = Quaternion.identity;
    }

    /// <summary>Тело в позе привязки: вершины в мире префаба, нормали, UV, доминирующая кость.</summary>
    private sealed class Body
    {
        public Vector3[] P, N;
        public Vector2[] Uv;
        public string[] Bone;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public float ModelScale = 1f;
        public Vector3 Center;
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Босс/Хозяин Чащи/Фазы: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Босс/Хозяин Чащи/Фазы: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(ThicketMasterBuilder.Prefab))
        {
            if (force) Debug.LogWarning("[thicket-phase] Нет " + ThicketMasterBuilder.Prefab + " — сначала «Разлом/Босс/Хозяин Чащи/Собрать представление».");
            return;
        }
        try
        {
            CopyEmissionMaps();
            ThicketMasterBuilder.EnsurePhaseEmission();
            if (force || !Built())
            {
                if (!PacksPresent()) Debug.LogWarning("[thicket-phase] Нет паков CFXR/Hovl — накладки фаз не собраны, светятся только руны.");
                else Build();
            }
            EnsureComponent();
        }
        catch (Exception e) { Debug.LogError("[thicket-phase] Одежда фаз не собрана: " + e); }
    }

    private static bool PacksPresent()
        => AssetDatabase.LoadAssetAtPath<Material>(CfxrPetal) != null && AssetDatabase.LoadAssetAtPath<Material>(CfxrLeaf) != null
           && AssetDatabase.LoadAssetAtPath<Material>(CfxrGlowSoft) != null && AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) != null
           && AssetDatabase.LoadAssetAtPath<GameObject>(HovlFlower) != null && AssetDatabase.LoadAssetAtPath<Texture2D>(HovlFlowerTexture) != null;

    /// <summary>Ревизия и модель: другая модель (FBX новее) — точки накладок пересчитываются.</summary>
    private static string Stamp() => Revision + "|" + (File.Exists(ThicketMasterBuilder.Model) ? File.GetLastWriteTimeUtc(ThicketMasterBuilder.Model).Ticks : 0L);

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterPhaseDressing.BerriesName));
        if (importer == null || importer.userData != Stamp()) return false;
        foreach (string name in new[] { ThicketMasterPhaseDressing.BushBloomName, ThicketMasterPhaseDressing.CrownBloomLeftName,
                     ThicketMasterPhaseDressing.CrownBloomRightName, ThicketMasterPhaseDressing.EyeGlowName,
                     ThicketMasterPhaseDressing.EmbersName, ThicketMasterPhaseDressing.BloomAuraName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    /// <summary>Маски эмиссии из пакета в Resources (как SyncPackage сборщика): новее — перезаписать.</summary>
    private static void CopyEmissionMaps()
    {
        string package = Path.Combine(RepositoryRoot(), ThicketMasterBuilder.Package, "textures");
        if (!Directory.Exists(package) || !Directory.Exists(ThicketMasterBuilder.Root)) return;
        foreach (string name in EmissionMaps)
        {
            string from = Path.Combine(package, name), to = ThicketMasterBuilder.Root + name;
            if (!File.Exists(from)) continue;
            if (File.Exists(to) && File.GetLastWriteTimeUtc(to) >= File.GetLastWriteTimeUtc(from)) continue;
            File.Copy(from, to, true);
            AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceSynchronousImport);
        }
    }

    /// <summary>Компонент на теле: префаб тела пересобирается сборщиком — ставим заново, если его нет.</summary>
    internal static void EnsureComponent()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThicketMasterBuilder.Prefab);
        if (prefab == null || prefab.GetComponent<ThicketMasterAnimatorView>() == null
            || prefab.GetComponent<ThicketMasterPhaseDressing>() != null) return;
        var contents = PrefabUtility.LoadPrefabContents(ThicketMasterBuilder.Prefab);
        try
        {
            contents.AddComponent<ThicketMasterPhaseDressing>();
            PrefabUtility.SaveAsPrefabAsset(contents, ThicketMasterBuilder.Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        Debug.Log("[thicket-phase] На ThicketMaster_Runtime поставлен ThicketMasterPhaseDressing (одежда фаз).");
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "ThicketMaster");
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX/ThicketMaster", "Phases");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var kit = Materials();
        var contents = PrefabUtility.LoadPrefabContents(ThicketMasterBuilder.Prefab);
        try
        {
            var body = SampleBody(contents);
            var berries = SaveBerries(kit, body);
            SaveBushBloom(kit, body, berries);
            SaveCrownBloom(kit, body, ThicketMasterPhaseDressing.BoneCrownLeft, ThicketMasterPhaseDressing.CrownBloomLeftName, 31);
            SaveCrownBloom(kit, body, ThicketMasterPhaseDressing.BoneCrownRight, ThicketMasterPhaseDressing.CrownBloomRightName, 47);
            SaveEyeGlow(kit, body);
            var aura = TopPoints(body, "ThicketPhaseAuraPoints");
            SaveEmbers(kit, aura);
            SaveBloomAura(kit, aura);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterPhaseDressing.BerriesName));
        if (importer != null && importer.userData != Stamp())
        {
            importer.userData = Stamp();
            importer.SaveAndReimport();
        }
        Debug.Log("[thicket-phase] Одежда фаз собрана: ягоды, цветы куста и кроны, лепестки, глаза, листопад и угли Ф2, розовый свет Ф3, всплески рёвов; ревизия " + Revision + ".");
    }

    // ------------------------------------------------------------ тело в позе привязки

    /// <summary>
    /// Вершины тела в мире префаба: ручной скиннинг (bones × bindposes) — поза привязки
    /// без аниматора. Доминирующая кость — с наибольшим весом.
    /// </summary>
    private static Body SampleBody(GameObject contents)
    {
        var smr = contents.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null) throw new InvalidOperationException("В ThicketMaster_Runtime нет SkinnedMeshRenderer.");
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        var bindposes = mesh.bindposes;
        var weights = mesh.boneWeights;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var body = new Body { Uv = mesh.uv };
        var matrices = new Matrix4x4[bones.Length];
        for (int b = 0; b < bones.Length; b++)
            matrices[b] = bones[b] != null && b < bindposes.Length ? bones[b].localToWorldMatrix * bindposes[b] : Matrix4x4.identity;
        body.P = new Vector3[vertices.Length];
        body.N = new Vector3[vertices.Length];
        body.Bone = new string[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights.Length == vertices.Length ? weights[i] : new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            Vector3 p = Vector3.zero, n = Vector3.zero;
            Accumulate(matrices, w.boneIndex0, w.weight0, vertices[i], normals.Length > i ? normals[i] : Vector3.up, ref p, ref n);
            Accumulate(matrices, w.boneIndex1, w.weight1, vertices[i], normals.Length > i ? normals[i] : Vector3.up, ref p, ref n);
            Accumulate(matrices, w.boneIndex2, w.weight2, vertices[i], normals.Length > i ? normals[i] : Vector3.up, ref p, ref n);
            Accumulate(matrices, w.boneIndex3, w.weight3, vertices[i], normals.Length > i ? normals[i] : Vector3.up, ref p, ref n);
            float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
            body.P[i] = sum > 1e-5f ? p / sum : smr.transform.TransformPoint(vertices[i]);
            body.N[i] = n.sqrMagnitude > 1e-10f ? n.normalized : Vector3.up;
            int top = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; top = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; top = w.boneIndex2; }
            if (w.weight3 > best) top = w.boneIndex3;
            body.Bone[i] = top >= 0 && top < bones.Length && bones[top] != null ? bones[top].name : "";
        }
        foreach (var t in contents.GetComponentsInChildren<Transform>(true))
            if (!body.Bones.ContainsKey(t.name)) body.Bones[t.name] = t;
        var animator = contents.GetComponentInChildren<Animator>(true);
        body.ModelScale = Mathf.Abs((animator != null ? animator.transform : smr.transform).lossyScale.x);
        if (body.ModelScale < 1e-6f) body.ModelScale = 1f;
        body.Center = body.Bones.TryGetValue("chest", out var chest) ? chest.position : smr.bounds.center;
        return body;
    }

    private static void Accumulate(Matrix4x4[] matrices, int bone, float weight, Vector3 v, Vector3 n, ref Vector3 p, ref Vector3 normal)
    {
        if (weight <= 0f || bone < 0 || bone >= matrices.Length) return;
        p += matrices[bone].MultiplyPoint3x4(v) * weight;
        normal += matrices[bone].MultiplyVector(n) * weight;
    }

    private static Transform BoneOf(Body body, string name)
    {
        if (!body.Bones.TryGetValue(name, out var bone)) throw new InvalidOperationException("В теле нет кости " + name + ".");
        return bone;
    }

    /// <summary>Мир → оси кости в метрах модели (накладка — ребёнок кости с масштабом тела/кости).</summary>
    private static Vector3 Local(Body body, Transform bone, Vector3 world)
        => Quaternion.Inverse(bone.rotation) * (world - bone.position) / body.ModelScale;

    private static Vector3 LocalDir(Transform bone, Vector3 world) => (Quaternion.Inverse(bone.rotation) * world).normalized;

    // ------------------------------------------------------------ выбор точек

    private static List<int> VerticesOf(Body body, string bone, Func<int, bool> keep)
    {
        var list = new List<int>();
        for (int i = 0; i < body.P.Length; i++)
            if (body.Bone[i] == bone && keep(i)) list.Add(i);
        return list;
    }

    /// <summary>
    /// «Самая дальняя точка»: count вершин из candidates, каждая — дальше всех от уже взятых
    /// (taken — точки, от которых тоже держаться). Расстояния — в метрах модели. Детерминированно.
    /// </summary>
    private static List<int> Farthest(Body body, List<int> candidates, int count, List<Vector3> taken, float minDistance)
    {
        var picked = new List<int>();
        if (candidates.Count == 0 || count <= 0) return picked;
        var distance = new float[candidates.Count];
        for (int c = 0; c < candidates.Count; c++)
        {
            float d = float.MaxValue;
            foreach (var t in taken) d = Mathf.Min(d, (body.P[candidates[c]] - t).magnitude / body.ModelScale);
            distance[c] = d;
        }
        // Без взятых точек начало — самая верхняя и передняя вершина: на виде сверху она точно на виду.
        int start = -1;
        if (taken.Count == 0)
        {
            start = 0;
            for (int c = 1; c < candidates.Count; c++)
                if (body.P[candidates[c]].y + body.P[candidates[c]].z > body.P[candidates[start]].y + body.P[candidates[start]].z) start = c;
        }
        while (picked.Count < count)
        {
            int best = start;
            start = -1;
            if (best < 0)
                for (int c = 0; c < candidates.Count; c++)
                    if (distance[c] >= 0f && (best < 0 || distance[c] > distance[best])) best = c;
            if (best < 0 || distance[best] < minDistance) break;
            picked.Add(candidates[best]);
            Vector3 at = body.P[candidates[best]];
            distance[best] = -1f;
            for (int c = 0; c < candidates.Count; c++)
                if (distance[c] >= 0f) distance[c] = Mathf.Min(distance[c], (body.P[candidates[c]] - at).magnitude / body.ModelScale);
        }
        return picked;
    }

    private static Vector3 Outward(Body body, int vertex) => (body.P[vertex] - body.Center).normalized;

    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Картинка пакета/Resources как пиксели (для проверки UV вершин); null — нет файла.</summary>
    private static Color32[] ReadPixels(string fileName, out int width, out int height)
    {
        width = height = 0;
        string path = ThicketMasterBuilder.Root + fileName;
        if (!File.Exists(path)) path = Path.Combine(RepositoryRoot(), ThicketMasterBuilder.Package, "textures", fileName);
        if (!File.Exists(path)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) return null;
            width = texture.width; height = texture.height;
            return texture.GetPixels32();
        }
        finally { Object.DestroyImmediate(texture); }
    }

    private static Color32 At(Color32[] pixels, int width, int height, Vector2 uv)
    {
        int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * width), 0, width - 1);
        int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * height), 0, height - 1); // строка 0 — низ, как v
        return pixels[y * width + x];
    }

    /// <summary>UV-семена глаз из eyes.json (uv_regions.py); нет файла — замер разведки 02.10.</summary>
    private static List<Vector2> EyeSeeds()
    {
        var seeds = new List<Vector2>();
        string path = Path.Combine(RepositoryRoot(), DressingWork, "eyes.json");
        if (File.Exists(path))
        {
            var number = "([-+0-9.eE]+)";
            foreach (Match match in Regex.Matches(File.ReadAllText(path), "\"uv\"\\s*:\\s*\\[\\s*" + number + "\\s*,\\s*" + number))
                if (float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float u)
                    && float.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    seeds.Add(new Vector2(u, v));
        }
        if (seeds.Count == 0) { seeds.Add(new Vector2(.9132f, .0048f)); seeds.Add(new Vector2(.854f, .0376f)); }
        return seeds;
    }

    // ------------------------------------------------------------ материалы и меши

    private static Kit Materials()
    {
        var kit = new Kit();
        // Ягоды и цветы — копии URP с прозрачностью перед героем, как тело (ThicketMasterBuilder.SeeThroughShader).
        var lit = ThicketMasterBuilder.SeeThroughShader("Universal Render Pipeline/Lit");
        kit.Berry = BerryMaterial("M_ThicketPhase_Berry", lit, BerryRed, BerryEmission);
        kit.BerryDeep = BerryMaterial("M_ThicketPhase_BerryDeep", lit, BerryDeep, BerryEmission);
        // Ягоды-капли всплеска: цвет из частиц, блеск без бледности lit-материалов CFXR.
        kit.BerryDrop = BerryMaterial("M_ThicketPhase_BerryDrop", Shader.Find("Universal Render Pipeline/Particles/Lit"), Color.white, 0f);
        var simpleLit = ThicketMasterBuilder.SeeThroughShader("Universal Render Pipeline/Simple Lit");
        kit.FlowerWhite = FlowerMaterial("M_ThicketPhase_FlowerWhite", simpleLit, PetalWhite);
        kit.FlowerPink = FlowerMaterial("M_ThicketPhase_FlowerPink", simpleLit, PetalPink);
        kit.FlowerRed = FlowerMaterial("M_ThicketPhase_FlowerRed", simpleLit, BerryRed);
        // Лепесток CFXR одним каналом: R-канал ≈ 1, цвет целиком из частиц (белые/розовые/золотые из одной текстуры).
        kit.Petal = Unlit(PackCopy("M_ThicketPhase_Petal", CfxrPetal));
        if (kit.Petal.HasProperty("_SingleChannel")) kit.Petal.SetFloat("_SingleChannel", 1f);
        kit.Leaf = Unlit(PackCopy("M_ThicketPhase_Leaf", CfxrLeaf));
        kit.Glow = Plain(PackCopy("M_ThicketPhase_Glow", CfxrGlowSoft));
        kit.Haze = Plain(PackCopy("M_ThicketPhase_Haze", CfxrSmokeBlurred));
        var blur = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrCloudBlur);
        if (blur != null) kit.Haze.SetTexture("_MainTex", blur);
        if (kit.Haze.HasProperty("_SingleChannel")) kit.Haze.SetFloat("_SingleChannel", 1f);
        // Свечение за порог bloom: цвет частицы 8-битный, яркость даёт _HdrMultiply копии (урок 02.10).
        kit.Ember = Hdr(Plain(PackCopy("M_ThicketPhase_Ember", CfxrGlowSoft)), EmberHdr);
        kit.BloomLight = Hdr(Plain(PackCopy("M_ThicketPhase_BloomLight", CfxrGlowSoft)), BloomLightHdr);
        kit.GlowPetal = Hdr(Unlit(PackCopy("M_ThicketPhase_GlowPetal", CfxrPetal)), GlowPetalHdr);
        if (kit.GlowPetal.HasProperty("_SingleChannel")) kit.GlowPetal.SetFloat("_SingleChannel", 1f);
        kit.Haze.renderQueue = 3000; kit.Petal.renderQueue = 3006; kit.Leaf.renderQueue = 3006; kit.Glow.renderQueue = 3008;
        kit.GlowPetal.renderQueue = 3006; kit.Ember.renderQueue = 3009; kit.BloomLight.renderQueue = 3009;
        foreach (var m in new[] { kit.Petal, kit.Leaf, kit.Glow, kit.Haze, kit.Ember, kit.BloomLight, kit.GlowPetal }) EditorUtility.SetDirty(m);
        kit.Sphere = Icosphere();
        var packFlower = LoadMesh(HovlFlower);
        kit.LeafMesh = LoadMesh(CfxrLeafMesh);
        if (packFlower != null)
        {
            // Ось чашки Flower.fbx — +Y (основание у нуля, разведка 02.10). Если импорт положил её
            // на ±Z (основание у нуля по Z) — разворот к +Y; ширина цветка — поперёк оси.
            var b = packFlower.bounds;
            bool zUp = Mathf.Abs(b.min.y) > .2f * b.size.y && Mathf.Abs(b.min.z) < .1f * b.size.z;
            bool zDown = Mathf.Abs(b.min.y) > .2f * b.size.y && Mathf.Abs(b.max.z) < .1f * b.size.z;
            kit.FlowerBase = zUp ? Quaternion.FromToRotation(Vector3.forward, Vector3.up)
                : zDown ? Quaternion.FromToRotation(Vector3.back, Vector3.up) : Quaternion.identity;
            kit.FlowerSpan = Mathf.Max(.01f, zUp || zDown ? Mathf.Max(b.size.x, b.size.y) : Mathf.Max(b.size.x, b.size.z));
            kit.Flower = UprightFlower(packFlower, zUp ? Vector3.forward : zDown ? Vector3.back : Vector3.up);
            Debug.Log($"[thicket-phase] Flower.fbx: рамка {b.size}, основание {b.min}, ось чашки {(zUp ? "+Z" : zDown ? "−Z" : "+Y")}.");
        }
        AssetDatabase.SaveAssets();
        return kit;
    }

    /// <summary>
    /// Ягода: URP Lit, гладкая (блик). emission &gt; 0 — тлеет своим цветом (в тёмной арене красная
    /// ягода иначе уходит в чёрное — ревью 02.10, ягод Ф2 с камеры не видно). GI-флаги RealtimeEmissive держат
    /// _EMISSION при валидации материала URP (ловушка тела, ThicketMasterBuilder.ApplyEmission).
    /// </summary>
    private static Material BerryMaterial(string name, Shader shader, Color color, float emission)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .65f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (emission > 0f && material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", new Color(color.r * emission, color.g * emission, color.b * emission, 1f));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Свечение CFXR: цвет частицы × _HdrMultiply (больше 0 — включено) уходит за порог bloom.</summary>
    private static Material Hdr(Material material, float hdr)
    {
        if (material.HasProperty("_HdrMultiply")) material.SetFloat("_HdrMultiply", hdr);
        else Debug.LogWarning("[thicket-phase] У " + material.name + " нет _HdrMultiply — слой не светится.");
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Цветок Hovl: Simple Lit, текстура лепестков Flower2 с отсечкой .35, двусторонний, бликов не видно.
    /// Блик — как в материалах, восстановленных вручную 02.10 (не откатывать): ключ _SPECULAR_COLOR,
    /// цвет блика чёрный, _SpecColor.a = гладкость .5. _SpecularHighlights = 1, а не 0: проверка
    /// материала URP (SimpleLitGUI) при выключенных бликах снимает _SPECULAR_COLOR, при включённых —
    /// держит его и пишет .a = _Smoothness; чёрный цвет блика = блика нет.
    /// Свечение (ревью 02.10 вечер): эмиссия = цвет × FlowerGlow по той же текстуре лепестков.
    /// </summary>
    private static Material FlowerMaterial(string name, Shader shader, Color color)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        var petals = AssetDatabase.LoadAssetAtPath<Texture2D>(HovlFlowerTexture);
        material.SetTexture("_BaseMap", petals);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", .35f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .5f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", new Color(0f, 0f, 0f, .5f));
        material.EnableKeyword("_SPECULAR_COLOR");
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetTexture("_EmissionMap", petals);
            material.SetColor("_EmissionColor", new Color(color.r * FlowerGlow, color.g * FlowerGlow, color.b * FlowerGlow, 1f));
            material.EnableKeyword("_EMISSION");
            // RealtimeEmissive держит _EMISSION при проверке материала URP (как у ягод).
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Цветок Hovl с нормалями к раскрытию чашки (ревью 02.10 вечер): у Flower.fbx нормали смотрят
    /// вниз, против оси чашки (средняя по углам −0,38 по оси; замер в Blender — 294 из 396 вниз), и
    /// цветок, поставленный чашкой наружу из кроны, освещался снизу — тёмно-синие пятна. Копия меша в
    /// Geometry: нормаль против оси разворачивается, все наклоняются к оси на FlowerNormalLift.
    /// Остальное (вершины, UV, треугольники) — как в паке; сам пак не трогается.
    /// </summary>
    private static Mesh UprightFlower(Mesh source, Vector3 axis)
    {
        var mesh = UprightFlowerMesh(source, axis, GeometryFolder + "/ThicketPhaseFlower.asset", "ThicketPhaseFlower", out int flipped);
        Debug.Log($"[thicket-phase] Цветок: нормалей развёрнуто к чашке {flipped} из {source.vertexCount}.");
        return mesh;
    }

    /// <summary>
    /// Копия цветка Hovl с нормалями к раскрытию чашки (см. <see cref="UprightFlower"/>) в ассет path. Общая с холмом
    /// смерти (ThicketMasterVfxSetup, V11: цветы холма со светом, как цветы куста Ф3); flipped — сколько нормалей
    /// развёрнуто к оси.
    /// </summary>
    internal static Mesh UprightFlowerMesh(Mesh source, Vector3 axis, string path, string name, out int flipped)
    {
        var mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(path, name);
        int count = source.vertexCount;
        mesh.indexFormat = source.indexFormat;
        mesh.SetVertices(source.vertices);
        var uv = new List<Vector4>();
        for (int channel = 0; channel < 8; channel++)
        {
            uv.Clear();
            source.GetUVs(channel, uv);
            if (uv.Count == count) mesh.SetUVs(channel, uv);
        }
        var colors = source.colors32;
        if (colors.Length == count) mesh.colors32 = colors;
        mesh.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(source.GetTriangles(s), s);
        var normals = source.normals;
        if (normals.Length != count) normals = new Vector3[count];
        flipped = 0;
        for (int i = 0; i < count; i++)
        {
            var n = normals[i].sqrMagnitude > 1e-8f ? normals[i].normalized : axis;
            if (Vector3.Dot(n, axis) < 0f) { n = -n; flipped++; }
            normals[i] = (n + axis * FlowerNormalLift).normalized;
        }
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят спрайт.</summary>
    private static Material Plain(Material material)
    {
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        material.DisableKeyword("_FADING_ON");
        foreach (var p in new[] { "_UseDissolve", "_UseDissolveOffsetUV", "_UseSP" })
            if (material.HasProperty(p)) material.SetFloat(p, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        foreach (var p in new[] { "_UseLighting", "_UseNormalMap", "_CFXR_DITHERED_SHADOWS" })
            if (material.HasProperty(p)) material.SetFloat(p, 0f);
        return Plain(material);
    }

    private static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    /// <summary>Ягода: икосфера (80 треугольников), диаметр 1, цвет вершин 8-битный (Float32 ломает цвет меш-частиц).</summary>
    private static Mesh Icosphere()
    {
        var mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/ThicketPhaseBerry.asset", "ThicketPhaseBerry");
        float t = (1f + Mathf.Sqrt(5f)) * .5f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var cache = new Dictionary<long, int>();
        var tris = new List<int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index)) return index;
            verts.Add((verts[a] + verts[b]) * .5f);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }
        for (int f = 0; f < faces.Length; f += 3)
        {
            int a = faces[f], b = faces[f + 1], c = faces[f + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        var normals = new List<Vector3>();
        var colors = new List<Color32>();
        var uv = new List<Vector2>();
        for (int i = 0; i < verts.Count; i++)
        {
            var n = verts[i].normalized;
            verts[i] = n * .5f;
            normals.Add(n);
            colors.Add(new Color32(255, 255, 255, 255));
            uv.Add(new Vector2(.5f + Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI), .5f + n.y * .5f));
        }
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Точки эмиссии: вершины — места, нормали — направления, треугольники вырожденные.</summary>
    private static Mesh Points(string name, List<Vector3> positions, List<Vector3> directions)
    {
        var mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        if (positions.Count == 0) { positions.Add(Vector3.zero); directions.Add(Vector3.up); }
        mesh.SetVertices(positions);
        mesh.SetNormals(directions);
        var triangles = new int[((positions.Count + 2) / 3) * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = Mathf.Min(i, positions.Count - 1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ------------------------------------------------------------ частицы

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

    /// <summary>Устойчивое зерно по имени (String.GetHashCode меняется от запуска к запуску).</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    /// <summary>Один залп, мир (падающее остаётся, где упало), масштаб иерархии (рост тела), постоянное зерно.</summary>
    private static ParticleSystem Particles(GameObject parent, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(parent, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(parent.transform.root.name + "/" + name);
        var main = particles.main;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return particles;
    }

    /// <summary>Эмиссия из точек меша (вершины — места, нормали — направления).</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, float randomDirection, bool loop)
    {
        particles.transform.localPosition = Vector3.zero;
        particles.transform.localRotation = Quaternion.identity;
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = points;
        shape.meshSpawnMode = loop ? ParticleSystemShapeMultiModeValue.Loop : ParticleSystemShapeMultiModeValue.Random;
        shape.useMeshColors = false;
        shape.alignToDirection = false;
        shape.randomDirectionAmount = randomDirection;
        shape.normalOffset = 0f;
    }

    private static void Drag(ParticleSystem particles, float limit, float dampen)
    {
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(limit);
        drag.dampen = dampen;
    }

    private static void Tumble(ParticleSystem particles, float spin)
    {
        var main = particles.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var turn = particles.rotationOverLifetime; turn.enabled = true;
        turn.separateAxes = true;
        turn.x = new ParticleSystem.MinMaxCurve(-spin, spin);
        turn.y = new ParticleSystem.MinMaxCurve(-spin * .6f, spin * .6f);
        turn.z = new ParticleSystem.MinMaxCurve(-spin, spin);
    }

    /// <summary>Лепестки бури (белые 50 %, розовые 35 %, золотые 15 %) — случайный цвет из градиента.</summary>
    private static ParticleSystem.MinMaxGradient PetalColors()
    {
        var gradient = new Gradient();
        gradient.mode = GradientMode.Fixed;
        gradient.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .50f), new GradientColorKey(PetalPink, .85f), new GradientColorKey(PetalGold, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>Лепестки: атлас CFXR 2×2 одним каналом, кружатся, планируют, тают.</summary>
    private static ParticleSystem Petals(GameObject host, Kit kit, string name, int count, Mesh points, float speedMin, float speedMax,
        float lifeMin, float lifeMax, float delay)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, .10f, .18f, delay);
        FromPoints(particles, points, .55f, false);
        var main = particles.main;
        main.gravityModifier = .12f;
        main.startColor = PetalColors();
        Tumble(particles, 4f);
        Drag(particles, 1.1f, .10f);
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .4f; noise.frequency = .6f; noise.scrollSpeed = .3f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.04f, .75f);
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 2; sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        sheet.cycleCount = 1;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Petal;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR без освещения, кувыркаются, планируют, тают.</summary>
    private static ParticleSystem Leaves(GameObject host, Kit kit, string name, int count, Mesh points, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, 1.6f, 2.3f, speedMin, speedMax, .14f, .24f, delay);
        FromPoints(particles, points, .5f, false);
        var main = particles.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        Tumble(particles, 5f);
        Drag(particles, 1.6f, .12f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>Ягоды-капли: меш-частицы икосферы (URP Particles/Lit), разлетаются из куста и падают.</summary>
    private static ParticleSystem Drops(GameObject host, Kit kit, string name, int count, Mesh points)
    {
        var particles = Particles(host, name, count, 1.4f, 2.0f, 2.2f, 4.2f, .05f, .08f, 0f);
        FromPoints(particles, points, .35f, false);
        var main = particles.main;
        main.gravityModifier = 1.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = kit.Sphere;
        renderer.sharedMaterial = kit.BerryDrop;
        return particles;
    }

    /// <summary>Мягкие клубы пыльцы: облако CFXR одним каналом, малая альфа, растут и тают.</summary>
    private static ParticleSystem Puffs(GameObject host, Kit kit, string name, int count, Mesh points, Color color)
    {
        var particles = Particles(host, name, count, 1.1f, 1.6f, .3f, .8f, .6f, 1.0f, 0f);
        FromPoints(particles, points, .6f, false);
        var main = particles.main;
        main.gravityModifier = -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, .35f), new Color(color.r * .85f, color.g * .9f, color.b * .8f, .28f));
        Drag(particles, .3f, .2f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .3f, .95f, 1f, 1.4f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.1f, .45f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Haze;
        renderer.sortingFudge = -1f;
        return particles;
    }

    /// <summary>Янтарные искры рун: мягкое свечение CFXR со свечением (альфа, без аддитива), всплывают и гаснут.</summary>
    private static ParticleSystem Sparks(GameObject host, Kit kit, string name, int count, Mesh points)
    {
        var particles = Particles(host, name, count, .8f, 1.3f, 1.0f, 2.5f, .06f, .12f, 0f);
        FromPoints(particles, points, .4f, false);
        var main = particles.main;
        main.gravityModifier = -.10f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(RuneSpark.r, RuneSpark.g, RuneSpark.b, .9f),
            new Color(1f, .70f, .25f, .75f));
        Drag(particles, .9f, .15f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .15f, 1f, 1f, .2f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.05f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Ember;
        renderer.sortingFudge = -2f;
        return particles;
    }

    /// <summary>Растущая часть: MeshRenderer «Bud|задержка мс|подпись», масштаб префаба = выросла.</summary>
    private static void Bud(GameObject root, string label, int delayMs, Mesh mesh, Material material, Vector3 position, Quaternion rotation, float scale)
    {
        var go = new GameObject(ThicketMasterPhaseDressing.BudPrefix + delayMs + ThicketMasterPhaseDressing.BudSeparator + label);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static GameObject Container(GameObject root, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        return go;
    }

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(root.name)); }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------ накладки

    /// <summary>
    /// Ягоды куста (Ф2): сначала поверх нарисованных красных ягод (кучки красных вершин
    /// ближе 6 см — одна ягода), добор до 24 — «самая дальняя точка» по наружной стороне куста.
    /// Возвращает места ягод в мире префаба (цветы куста держатся от них).
    /// </summary>
    private static List<Vector3> SaveBerries(Kit kit, Body body)
    {
        var bush = BoneOf(body, ThicketMasterPhaseDressing.BoneBush);
        var outward = VerticesOf(body, ThicketMasterPhaseDressing.BoneBush, i => Vector3.Dot(body.N[i], Outward(body, i)) > .2f);
        if (outward.Count == 0) outward = VerticesOf(body, ThicketMasterPhaseDressing.BoneBush, i => true);
        if (outward.Count == 0) throw new InvalidOperationException("У кости bush нет вершин — куст не найден.");
        var pixels = ReadPixels("T_ThicketMaster_Color.jpg", out int width, out int height);

        var seeds = new List<Vector3>();
        var sums = new List<Vector3>();
        var normals = new List<Vector3>();
        var counts = new List<int>();
        if (pixels != null)
            foreach (int i in outward)
            {
                var c = At(pixels, width, height, body.Uv[i]);
                if (!(c.r > 115 && c.r > 1.8f * c.g && c.r > 1.8f * c.b)) continue;
                int cluster = -1;
                for (int k = 0; k < seeds.Count && cluster < 0; k++)
                    if ((seeds[k] - body.P[i]).magnitude / body.ModelScale < BerryMerge) cluster = k;
                if (cluster < 0) { seeds.Add(body.P[i]); sums.Add(Vector3.zero); normals.Add(Vector3.zero); counts.Add(0); cluster = seeds.Count - 1; }
                sums[cluster] += body.P[i]; normals[cluster] += body.N[i]; counts[cluster]++;
            }
        var order = new List<int>();
        for (int k = 0; k < seeds.Count; k++) order.Add(k);
        order.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));

        var places = new List<Vector3>();
        var directions = new List<Vector3>();
        var painted = new List<bool>();
        foreach (int k in order)
        {
            if (places.Count >= BerryCount) break;
            places.Add(sums[k] / counts[k]);
            directions.Add(normals[k].sqrMagnitude > 1e-8f ? normals[k].normalized : Outward(body, 0));
            painted.Add(true);
        }
        foreach (int i in Farthest(body, outward, BerryCount - places.Count, places, .10f))
        {
            places.Add(body.P[i]);
            directions.Add(body.N[i]);
            painted.Add(false);
        }

        var root = new GameObject(ThicketMasterPhaseDressing.BerriesName);
        var localPoints = new List<Vector3>();
        var localDirections = new List<Vector3>();
        for (int k = 0; k < places.Count; k++)
        {
            // Нарисованная ягода получает свою набухшую поверх (крупнее), новые — мельче.
            float size = painted[k] ? Mathf.Lerp(.18f, BerryMax, Hash01(k, 11)) : Mathf.Lerp(BerryMin, .19f, Hash01(k, 12));
            Vector3 world = places[k] + directions[k] * (size * .35f * body.ModelScale);
            var local = Local(body, bush, world);
            var rotation = Quaternion.Euler(Hash01(k, 13) * 360f, Hash01(k, 14) * 360f, Hash01(k, 15) * 360f);
            Bud(root, "Berry " + k.ToString("00"), (k % 3) * 80, kit.Sphere, Hash01(k, 16) < .66f ? kit.Berry : kit.BerryDeep, local, rotation, size);
            localPoints.Add(local);
            localDirections.Add(LocalDir(bush, directions[k]));
        }
        var points = Points("ThicketPhaseBerryPoints", localPoints, localDirections);
        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        Drops(bursts, kit, "Burst Berries", 16, points);
        Leaves(bursts, kit, "Burst Leaves", 20, points, 1.8f, 3.6f, 0f);
        Puffs(bursts, kit, "Burst Pollen", 6, points, PollenGreen);
        Save(root);
        Debug.Log($"[thicket-phase] Ягод: {places.Count}, из них поверх нарисованных: {painted.FindAll(p => p).Count}.");
        return places;
    }

    /// <summary>
    /// Цветы из куста (Ф3): 10 мест «самой дальней точкой», в стороне от ягод; 6 розовых, 4 белых
    /// (материал красных цветов собирается, но не ставится: Ф3 — розово-белая).
    /// </summary>
    private static void SaveBushBloom(Kit kit, Body body, List<Vector3> berries)
    {
        var bush = BoneOf(body, ThicketMasterPhaseDressing.BoneBush);
        var outward = VerticesOf(body, ThicketMasterPhaseDressing.BoneBush, i => Vector3.Dot(body.N[i], Outward(body, i)) > .3f);
        var picked = Farthest(body, outward, BushFlowerCount, berries, .06f);
        var root = new GameObject(ThicketMasterPhaseDressing.BushBloomName);
        var localPoints = new List<Vector3>();
        var localDirections = new List<Vector3>();
        for (int k = 0; k < picked.Count; k++)
        {
            int i = picked[k];
            float size = Mathf.Lerp(BushFlowerMin, BushFlowerMax, Hash01(k, 21));
            Flower(root, kit, body, bush, i, "Flower " + k.ToString("00"), (int)(Hash01(k, 22) * 300f), k < 6 ? kit.FlowerPink : kit.FlowerWhite,
                size, Hash01(k, 23));
            localPoints.Add(Local(body, bush, body.P[i]));
            localDirections.Add(LocalDir(bush, body.N[i]));
        }
        var points = Points("ThicketPhaseBushBloomPoints", localPoints, localDirections);
        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        Petals(bursts, kit, "Burst Petals", 50, points, 1.5f, 3.5f, 2.2f, 3.2f, 0f);
        Leaves(bursts, kit, "Burst Leaves", 12, points, 1.5f, 3f, 0f);
        Save(root);
    }

    /// <summary>
    /// Цветущая крона (Ф3): 22 цветка на верхней стороне кроны (нормаль вверх), 6 плодов-ягод,
    /// петля падающих лепестков (8/с, ложатся на землю) и всплеск лепестков на рёве 33.
    /// </summary>
    private static void SaveCrownBloom(Kit kit, Body body, string boneName, string prefabName, int salt)
    {
        var bone = BoneOf(body, boneName);
        var top = VerticesOf(body, boneName, i => body.N[i].y > .35f);
        if (top.Count == 0) top = VerticesOf(body, boneName, i => true);
        var flowers = Farthest(body, top, CrownFlowerCount, new List<Vector3>(), .10f);
        var taken = new List<Vector3>();
        foreach (int i in flowers) taken.Add(body.P[i]);
        var fruits = Farthest(body, top, CrownFruitCount, taken, .08f);

        var root = new GameObject(prefabName);
        var localPoints = new List<Vector3>();
        var localDirections = new List<Vector3>();
        for (int k = 0; k < flowers.Count; k++)
        {
            int i = flowers[k];
            float size = Mathf.Lerp(CrownFlowerMin, CrownFlowerMax, Hash01(k, salt));
            Flower(root, kit, body, bone, i, "Flower " + k.ToString("00"), (int)(Hash01(k, salt + 1) * 400f),
                Hash01(k, salt + 2) < .6f ? kit.FlowerWhite : kit.FlowerPink, size, Hash01(k, salt + 3));
            localPoints.Add(Local(body, bone, body.P[i]));
            localDirections.Add(LocalDir(bone, body.N[i]));
        }
        for (int k = 0; k < fruits.Count; k++)
        {
            int i = fruits[k];
            var world = body.P[i] + body.N[i] * (FruitSize * .35f * body.ModelScale);
            Bud(root, "Fruit " + k.ToString("00"), 150 + (int)(Hash01(k, salt + 4) * 250f), kit.Sphere, kit.Berry,
                Local(body, bone, world), Quaternion.identity, FruitSize);
        }
        var points = Points("ThicketPhaseBloomPoints_" + boneName, localPoints, localDirections);

        // Петля: лепестки с кроны всё время Ф3 (вид включает эмиссию и шагает её по тикам Sim).
        // 8/с с каждой половины кроны (было 5): аура цветения должна читаться с камеры (ревью 02.10).
        var loop = Petals(root, kit, ThicketMasterPhaseDressing.PetalLoopName, 60, points, .05f, .25f, 2.6f, 3.2f, 0f);
        var main = loop.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 60;
        main.gravityModifier = .08f;
        var emission = loop.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 8f;
        var collision = loop.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, root.transform); // вид ставит плоскость на корень тела (земля)
        collision.bounce = 0f; collision.dampen = .95f; collision.lifetimeLoss = 0f; collision.radiusScale = .3f;

        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        Petals(bursts, kit, "Burst Petals", 60, points, 2f, 4.5f, 2.4f, 3.4f, 0f);
        Leaves(bursts, kit, "Burst Leaves", 10, points, 1.5f, 3f, 0f);
        Save(root);
    }

    /// <summary>Цветок Hovl: ось чашки (+Y меша) — по нормали места, разворот вокруг неё, основание чуть в куст.</summary>
    private static void Flower(GameObject root, Kit kit, Body body, Transform bone, int vertex, string label, int delayMs,
        Material material, float size, float twist)
    {
        var direction = LocalDir(bone, body.N[vertex]);
        var rotation = Quaternion.FromToRotation(Vector3.up, direction) * Quaternion.AngleAxis(twist * 360f, Vector3.up) * kit.FlowerBase;
        var local = Local(body, bone, body.P[vertex] - body.N[vertex] * (.015f * body.ModelScale));
        Bud(root, label, delayMs, kit.Flower, material, local, rotation, size / kit.FlowerSpan);
    }

    /// <summary>
    /// Глаза (Ф2+): ореол на каждом глазу — якоря «Eye …» по UV-семенам eyes.json (среднее вершин
    /// головы в круге 28 пикс. атласа 2048), чуть наружу по нормали, чтобы голова не срезала ореол.
    /// Всплеск рёва 50 — янтарные искры с рун гребня и листья.
    /// </summary>
    private static void SaveEyeGlow(Kit kit, Body body)
    {
        var head = BoneOf(body, ThicketMasterPhaseDressing.BoneHead);
        var headVertices = VerticesOf(body, ThicketMasterPhaseDressing.BoneHead, i => true);
        var root = new GameObject(ThicketMasterPhaseDressing.EyeGlowName);
        var seeds = EyeSeeds();
        const float radius = 28f / 2048f;
        for (int s = 0; s < seeds.Count; s++)
        {
            Vector3 sum = Vector3.zero, normal = Vector3.zero;
            int count = 0, nearest = -1;
            float best = float.MaxValue;
            foreach (int i in headVertices)
            {
                float d = (body.Uv[i] - seeds[s]).magnitude;
                if (d < best) { best = d; nearest = i; }
                if (d > radius) continue;
                sum += body.P[i]; normal += body.N[i]; count++;
            }
            if (count == 0 && nearest < 0) continue;
            var at = count > 0 ? sum / count : body.P[nearest];
            var n = count > 0 && normal.sqrMagnitude > 1e-8f ? normal.normalized : body.N[nearest];
            var eye = new GameObject(ThicketMasterPhaseDressing.EyePrefix + (char)('A' + s));
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = Local(body, head, at + n * (EyeLift * body.ModelScale));
        }

        var glow = PelagWhirlwindVfxSetup.NewParticles(root, ThicketMasterPhaseDressing.GlowName, 4, 1000f, 1000f, 0f, 0f, EyeGlowSize, EyeGlowSize);
        glow.useAutoRandomSeed = false;
        glow.randomSeed = Seed(ThicketMasterPhaseDressing.EyeGlowName + "/" + ThicketMasterPhaseDressing.GlowName);
        var main = glow.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = new Color(1f, .62f, .15f, 0f);
        var emission = glow.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.enabled = false;
        var shape = glow.shape; shape.enabled = false;
        var renderer = glow.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Ember; // ореол глаз за порогом bloom (ревью 02.10: глаз не видно)
        renderer.sortingFudge = -3f;

        // Искры — с рун гребня: вершины головы, чей UV светится в маске F2.
        var mask = ReadPixels("T_ThicketMaster_Emission_F2.png", out int width, out int height);
        var runes = mask == null ? new List<int>()
            : VerticesOf(body, ThicketMasterPhaseDressing.BoneHead, i => At(mask, width, height, body.Uv[i]).r > 40);
        if (runes.Count < 4) runes = VerticesOf(body, ThicketMasterPhaseDressing.BoneHead, i => body.N[i].z > .3f);
        var picked = Farthest(body, runes, RuneSparkPoints, new List<Vector3>(), .02f);
        var localPoints = new List<Vector3>();
        var localDirections = new List<Vector3>();
        foreach (int i in picked)
        {
            localPoints.Add(Local(body, head, body.P[i] + body.N[i] * (.03f * body.ModelScale)));
            localDirections.Add(LocalDir(head, (body.N[i] + Vector3.up * .6f).normalized));
        }
        var points = Points("ThicketPhaseRunePoints", localPoints, localDirections);
        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        Sparks(bursts, kit, "Burst Sparks", 24, points);
        Leaves(bursts, kit, "Burst Leaves", 14, points, 1.5f, 3f, .05f);
        Save(root);
    }

    // ------------------------------------------------------------ ауры фаз (ревью 02.10)

    /// <summary>
    /// Точки ауры в осях кости chest: вершины верха спины и кроны (нормаль вверх) «самой дальней
    /// точкой», чуть наружу по нормали; направление — между нормалью и вверх. Спина в анимации
    /// гнётся — точки отходят от коры на сантиметры, для углей и света это не видно.
    /// </summary>
    private static Mesh TopPoints(Body body, string name)
    {
        var chest = BoneOf(body, ThicketMasterPhaseDressing.BoneChest);
        var top = new List<int>();
        for (int i = 0; i < body.P.Length; i++)
            if (body.N[i].y > .45f && Array.IndexOf(AuraBones, body.Bone[i]) >= 0) top.Add(i);
        if (top.Count == 0)
            for (int i = 0; i < body.P.Length; i++)
                if (Array.IndexOf(AuraBones, body.Bone[i]) >= 0) top.Add(i);
        if (top.Count == 0) throw new InvalidOperationException("Нет вершин спины и кроны — аура фаз не собрана.");
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        foreach (int i in Farthest(body, top, AuraPoints, new List<Vector3>(), .08f))
        {
            positions.Add(Local(body, chest, body.P[i] + body.N[i] * (.05f * body.ModelScale)));
            directions.Add(LocalDir(chest, (body.N[i] + Vector3.up).normalized));
        }
        return Points(name, positions, directions);
    }

    /// <summary>
    /// Осенние листья (всплеск и листопад): рыжий, янтарь и золото, как листва карты F2 (ревью 02.10
    /// вечер — багрянца мало, 10 %: он читался обугленным). Случайный цвет.
    /// </summary>
    private static ParticleSystem.MinMaxGradient AutumnColors()
    {
        var gradient = new Gradient();
        gradient.mode = GradientMode.Fixed;
        gradient.SetKeys(
            new[] { new GradientColorKey(AutumnRed, .10f), new GradientColorKey(AutumnOrange, .60f), new GradientColorKey(AutumnGold, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>Движение углей: дрожь шума, тормоз, вспыхивают, остывают к красному и гаснут.</summary>
    private static void EmberMotion(ParticleSystem particles, float dragLimit)
    {
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .45f; noise.frequency = .9f; noise.scrollSpeed = .5f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        Drag(particles, dragLimit, .10f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .4f, .12f, 1f, .7f, .75f, 1f, 0f));
        var cool = new Gradient();
        cool.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, .45f), new GradientColorKey(new Color(1f, .62f, .50f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .08f), new GradientAlphaKey(1f, .55f), new GradientAlphaKey(0f, 1f) });
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = cool;
    }

    private static void Glowing(ParticleSystem particles, Material material)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -2f;
    }

    /// <summary>
    /// Аура Ф2 (ревью 02.10 «фаза 2 не отличается»: руны на груди камера видит мельком, а спину и
    /// крону — всегда; вечер — «угли, лавовый, а не лесной»): петля «Leaf Fall» — осенние листья
    /// падают с кроны и спины и ложатся на землю (LeafFallRate/с), петля «Aura» — редкие угли
    /// (EmberRate/с, было 14), оба ×1,4 в ярости (вид). Мир — тянутся следом за идущим боссом.
    /// Всплеск рёва 66 — листопад осенних листьев и негустой сноп углей (вместе с ягодами куста —
    /// смена фазы видна разом).
    /// </summary>
    private static void SaveEmbers(Kit kit, Mesh points)
    {
        var root = new GameObject(ThicketMasterPhaseDressing.EmbersName);
        var loop = Particles(root, ThicketMasterPhaseDressing.AuraName, 30, 1.2f, 2.0f, .35f, .90f, .09f, .16f, 0f);
        FromPoints(loop, points, .35f, false);
        var main = loop.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 30;
        main.gravityModifier = -.10f;
        main.startColor = new ParticleSystem.MinMaxGradient(Ember, EmberDeep);
        var emission = loop.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = EmberRate;
        EmberMotion(loop, 1.3f);
        Glowing(loop, kit.Ember);

        // Листопад: лист CFXR планирует с кроны 2,6–3,4 с и ложится на землю (плоскость — корень тела, вид).
        var fall = Leaves(root, kit, ThicketMasterPhaseDressing.LeafLoopName, 24, points, .05f, .30f, 0f);
        var fallMain = fall.main;
        fallMain.loop = true;
        fallMain.duration = 5f;
        fallMain.maxParticles = 24;
        fallMain.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 3.4f);
        fallMain.startSize = new ParticleSystem.MinMaxCurve(.16f, .26f);
        fallMain.gravityModifier = .16f;
        fallMain.startColor = AutumnColors();
        var fallEmission = fall.emission;
        fallEmission.SetBursts(new ParticleSystem.Burst[0]);
        fallEmission.rateOverTime = LeafFallRate;
        var fallNoise = fall.noise; fallNoise.enabled = true;
        fallNoise.strength = .35f; fallNoise.frequency = .5f; fallNoise.scrollSpeed = .25f;
        fallNoise.quality = ParticleSystemNoiseQuality.Medium;
        var landing = fall.collision; landing.enabled = true;
        landing.type = ParticleSystemCollisionType.Planes;
        landing.mode = ParticleSystemCollisionMode.Collision3D;
        landing.SetPlane(0, root.transform); // вид ставит плоскость на корень тела (земля)
        landing.bounce = 0f; landing.dampen = .95f; landing.lifetimeLoss = 0f; landing.radiusScale = .3f;

        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        var sparks = Particles(bursts, "Burst Embers", 24, .9f, 1.7f, 2.5f, 5.5f, .10f, .18f, 0f);
        FromPoints(sparks, points, .45f, false);
        var sparkMain = sparks.main;
        sparkMain.gravityModifier = -.18f;
        sparkMain.startColor = new ParticleSystem.MinMaxGradient(Ember, EmberDeep);
        EmberMotion(sparks, 2.2f);
        Glowing(sparks, kit.Ember);

        var leaves = Leaves(bursts, kit, "Burst Autumn Leaves", 70, points, 2.5f, 5f, 0f);
        var leafMain = leaves.main;
        leafMain.startColor = AutumnColors();
        leafMain.startSize = new ParticleSystem.MinMaxCurve(.18f, .30f);
        leafMain.startLifetime = new ParticleSystem.MinMaxCurve(1.9f, 2.8f);
        leafMain.gravityModifier = .35f;
        Save(root);
    }

    /// <summary>
    /// Аура Ф3 — розовый свет цветения: мягкие розовые огни плывут над кроной (петля «Aura» 9/с,
    /// вместе с лепестками кроны). Всплеск рёва 33 — светящиеся лепестки и сноп розового света.
    /// </summary>
    private static void SaveBloomAura(Kit kit, Mesh points)
    {
        var root = new GameObject(ThicketMasterPhaseDressing.BloomAuraName);
        var light = new Color(BloomLight.r, BloomLight.g, BloomLight.b, .85f);
        var pale = new Color(BloomLightPale.r, BloomLightPale.g, BloomLightPale.b, .75f);
        var loop = Particles(root, ThicketMasterPhaseDressing.AuraName, 40, 2.0f, 3.0f, .10f, .35f, .10f, .18f, 0f);
        FromPoints(loop, points, .6f, false);
        var main = loop.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 40;
        main.gravityModifier = -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(light, pale);
        var emission = loop.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 9f;
        BloomMotion(loop, .6f);
        Glowing(loop, kit.BloomLight);

        var bursts = Container(root, ThicketMasterPhaseDressing.BurstsName);
        var petals = Petals(bursts, kit, "Burst Glow Petals", 70, points, 2.5f, 5f, 2.2f, 3.2f, 0f);
        petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var motes = Particles(bursts, "Burst Bloom Light", 40, 1.4f, 2.4f, 1.2f, 3.0f, .12f, .22f, 0f);
        FromPoints(motes, points, .6f, false);
        var moteMain = motes.main;
        moteMain.gravityModifier = -.04f;
        moteMain.startColor = new ParticleSystem.MinMaxGradient(light, pale);
        BloomMotion(motes, 1.4f);
        Glowing(motes, kit.BloomLight);
        Save(root);
    }

    private static void BloomMotion(ParticleSystem particles, float dragLimit)
    {
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .5f; noise.frequency = .5f; noise.scrollSpeed = .25f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        Drag(particles, dragLimit, .12f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .3f, .2f, 1f, .8f, .9f, 1f, 0f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.15f, .6f);
    }
}

/// <summary>
/// Префаб тела пересобран (ThicketMasterBuilder.Build) — компонент одежды фаз ставится заново
/// и накладки пересобираются, если сменилась модель. Через delayCall: не внутри импорта.
/// </summary>
internal sealed class ThicketMasterDressingPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
        {
            if (path != ThicketMasterBuilder.Prefab) continue;
            EditorApplication.delayCall -= Reinstall;
            EditorApplication.delayCall += Reinstall;
            return;
        }
    }

    private static void Reinstall() => ThicketMasterDressingSetup.Install(false);
}
