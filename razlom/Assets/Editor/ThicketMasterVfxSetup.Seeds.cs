using System.Collections.Generic;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// «ТЕРНОВНИК» ХОЗЯИНА ЧАЩИ — СБОРКА ЭФФЕКТОВ (V17, контракт boss-tempo-contract.md § 17.2, владелец 08.10; заменил «Веер
/// шипов-семян» V16). Вид — ThicketMasterCombatView.Seeds*.cs, время и раскладка — ThicketMasterSeedRules.
///
/// Куст — из ассетов арены и Шипомёта: листва — падуб поляны (меш CreatingBush_Breeze0 — копия ThicketBushLeaves, материал —
/// копия CreatingBush_Surface на «Game/Camp Breeze Lit»: та же фактура и ветер, что у кустов арены), четыре больших шипа по
/// линиям креста и колючие стебли — шипы Шипомёта (ThornSpire, ThornA–C, побеги ThornShootA–B) на коре стручка
/// (M_Thicket_SeedWood, ThornWood: мшистая кора, красно-оранжевые кончики). Шип в полёте — тот же стручок, что летал у
/// веера (своя сетка ThicketSeedPod: бугристое ядро с рёбрами шелухи и 14 шипов, кончики тлеют янтарём M_Thicket_SeedTip),
/// меньше. Лента следа — копия ленты когтей Вендиго в листовом золоте. След, земля под кустом, листья, угольки — системы
/// без эмиссии: частицы ставит вид. Всплески — плиты дёрна, комья, зерно, трава и пыль земли босса (V14), паки CFXR
/// (щепки, листья, облака, искры), шипы Шипомёта осколками.
/// </summary>
public static partial class ThicketMasterVfxSetup
{
    private const string ThornWoodTexture = "Assets/Resources/VFX/Thorncaster/Textures/ThornWood.png";
    private const string ThornGeometry = "Assets/Resources/VFX/Thorncaster/Geometry/";
    private const string WendigoClawRibbon = "Assets/Resources/VFX/Wendigo/Materials/M_Wendigo_ClawRibbon.mat";
    private const string ArenaBushMesh = "Assets/Resources/Environment/Meadow/CreatingBush_Breeze0.asset";
    private const string ArenaBushMaterial = "Assets/Resources/Environment/Meadow/CreatingBush_Surface.mat";

    // ------------------------------------------------------------- materials, mesh

    private static void SeedKit(Kit kit)
    {
        var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(ThornWoodTexture);
        if (wood == null)
        {
            Note(ThornWoodTexture + " (кора стручка; взята кора корней " + RootBarkTexture + ")");
            wood = AssetDatabase.LoadAssetAtPath<Texture2D>(RootBarkTexture);
        }
        kit.SeedWood = SeedWoodMaterial("M_Thicket_SeedWood", wood, false);
        kit.SeedTip = SeedWoodMaterial("M_Thicket_SeedTip", wood, true);
        kit.SeedRibbon = PackCopy("M_Thicket_SeedRibbon", WendigoClawRibbon);
        if (kit.SeedRibbon != null)
        {
            // Листовое золото с тёмной корой по краю: лента видна на тёмной поляне, но не светится пятном.
            kit.SeedRibbon.SetColor("_Core", new Color(.86f, .72f, .30f));
            kit.SeedRibbon.SetColor("_Mid", new Color(.62f, .50f, .18f));
            kit.SeedRibbon.SetColor("_Edge", new Color(.40f, .36f, .14f));
            kit.SeedRibbon.SetColor("_Rim", new Color(.22f, .19f, .08f));
            kit.SeedRibbon.SetFloat("_Timed", 0f);
            kit.SeedRibbon.SetFloat("_Glow", .06f);
            if (kit.SeedRibbon.HasProperty("_TaperSkew")) kit.SeedRibbon.SetFloat("_TaperSkew", 1.9f);
            if (kit.SeedRibbon.HasProperty("_TaperPower")) kit.SeedRibbon.SetFloat("_TaperPower", .7f);
            EditorUtility.SetDirty(kit.SeedRibbon);
        }
        kit.SeedPod = SeedPodMesh("ThicketSeedPod");
        var shards = new List<Mesh>();
        foreach (string name in new[] { "ThornA", "ThornB", "ThornC" })
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ThornGeometry + name + ".asset");
            if (mesh != null) shards.Add(mesh);
        }
        if (shards.Count == 0) Note(ThornGeometry + "ThornA–C (осколки шипов; взяты шипы прорастания)");
        kit.SeedShards = shards.Count > 0 ? shards.ToArray() : kit.Spikes;
        BushKit(kit);
    }

    /// <summary>
    /// Куст терновника: листва — копии меша и материала падуба поляны (арена их пересобирает — копия держит куст), шипы линий —
    /// ThornSpire Шипомёта, стебли — ThornA–C и побеги ThornShootA–B (нет сборки Шипомёта — шипы прорастания).
    /// </summary>
    private static void BushKit(Kit kit)
    {
        kit.BushLeaves = PackCopy("M_Thicket_BushLeaves", ArenaBushMaterial);
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(ArenaBushMesh);
        if (source == null) Note(ArenaBushMesh + " (листва куста; взят шар ягоды)");
        else
        {
            string path = GeometryFolder + "/ThicketBushLeaves.asset";
            var copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (copy == null) { copy = Object.Instantiate(source); AssetDatabase.CreateAsset(copy, path); }
            else EditorUtility.CopySerialized(source, copy);
            copy.name = "ThicketBushLeaves";
            EditorUtility.SetDirty(copy);
            kit.BushLeafMesh = copy;
        }
        if (kit.BushLeafMesh == null) kit.BushLeafMesh = kit.BerryMesh;
        if (kit.BushLeaves == null) kit.BushLeaves = kit.SeedWood;
        kit.BushSpire = AssetDatabase.LoadAssetAtPath<Mesh>(ThornGeometry + "ThornSpire.asset");
        if (kit.BushSpire == null)
        {
            Note(ThornGeometry + "ThornSpire (шипы линий куста; взят шип прорастания)");
            kit.BushSpire = kit.Spikes != null && kit.Spikes.Length > 0 ? kit.Spikes[0] : null;
        }
        var canes = new List<Mesh>();
        foreach (string name in new[] { "ThornA", "ThornB", "ThornC", "ThornShootA", "ThornShootB" })
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ThornGeometry + name + ".asset");
            if (mesh != null) canes.Add(mesh);
        }
        if (canes.Count == 0) Note(ThornGeometry + "ThornA–C, ThornShootA–B (стебли куста; взяты шипы прорастания)");
        kit.BushCanes = canes.Count > 0 ? canes.ToArray() : kit.Spikes;
    }

    /// <summary>
    /// Кора стручка — как M_Thorn_Wood Шипомёта (URP Lit, свет и тень арены, гладкость из альфы: кора матовая, кончик
    /// с блеском); glow — кончики шипов тлеют янтарём ThicketMasterCombatView.SeedTipGlow по той же фактуре (вид в блике
    /// перед выпуском разгоняет свечение блоком свойств). Цвет белый — без подъёма яркости.
    /// </summary>
    private static Material SeedWoodMaterial(string name, Texture2D texture, bool glow)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Lit"));
        if (texture != null) material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .9f);
        if (material.HasProperty("_SmoothnessTextureChannel")) material.SetFloat("_SmoothnessTextureChannel", 1f);
        material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        if (glow)
        {
            if (texture != null) material.SetTexture("_EmissionMap", texture);
            var tip = ThicketMasterCombatView.SeedTipGlow;
            material.SetColor("_EmissionColor", new Color(tip.r, tip.g, tip.b, 1f));
            material.EnableKeyword("_EMISSION");
            // RealtimeEmissive держит _EMISSION при проверке материала URP (урок V8 — ключ срезался).
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Стручок (ось +Z — вдоль, размер ~1): бугристое ядро-эллипсоид 1 × 1 × 1,2 с шестью рёбрами шелухи (кора ThornWood
    /// v 0,08–0,5: мох сзади, кора спереди) и 14 шипов по сфере Фибоначчи (длина 0,32–0,46, у основания 0,07–0,1,
    /// чуть загнуты): стебель — подсетка 0 (v 0,5–0,8), кончик — подсетка 1 (v 0,8–1, красный, тлеет). Нормали гладкие,
    /// обход — как у шипов Шипомёта (наружу). Белый Color32 в вершинах (на случай меш-частицы).
    /// </summary>
    private static Mesh SeedPodMesh(string name)
    {
        const int salt = 707, thorns = 14, sides = 6;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var body = new List<int>();
        var tips = new List<int>();
        var points = new List<Vector3>();
        var sphere = new List<int>();
        UnitSphere(2, points, sphere);
        foreach (var p in points) vertices.Add(SeedCore(p, salt));
        foreach (var p in points) uv.Add(new Vector2(.5f + .35f * p.x + .2f * p.y, Mathf.Lerp(.08f, .5f, .5f + .5f * p.z)));
        body.AddRange(sphere);

        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int k = 0; k < thorns; k++)
        {
            float y = 1f - 2f * (k + .5f) / thorns, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = k * golden + Hash01(k, salt) * .4f;
            var direction = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r).normalized;
            Vector3 root = SeedCore(direction, salt) * .92f;
            Vector3 normal = root.normalized;
            Vector3 reference = Mathf.Abs(normal.z) < .9f ? Vector3.forward : Vector3.right;
            Vector3 bend = Vector3.Cross(normal, reference).normalized;
            float length = Mathf.Lerp(.32f, .46f, Hash01(k, salt + 1)), radius = Mathf.Lerp(.07f, .1f, Hash01(k, salt + 2));
            float curve = Mathf.Lerp(-.08f, .08f, Hash01(k, salt + 3));
            // Кольца: основание, середина, начало кончика; вершина — точка.
            float[] at = { 0f, .5f, .78f };
            float[] width = { radius, radius * .55f, radius * .28f };
            float[] v = { .5f, .66f, .8f };
            var rings = new int[3];
            Vector3 Spine(float t) => root + normal * (length * t) + bend * (curve * t * t);
            for (int ring = 0; ring < 3; ring++)
            {
                rings[ring] = vertices.Count;
                AddRing(vertices, uv, Spine(at[ring]), (Spine(Mathf.Min(1f, at[ring] + .02f)) - Spine(at[ring])).normalized,
                    width[ring], sides, v[ring], Hash01(k, salt + 4) * 6.28f);
            }
            // Начало кончика — свои вершины (подсетка 1 со своей фактурой края).
            int tipRing = vertices.Count;
            AddRing(vertices, uv, Spine(at[2]), (Spine(.8f) - Spine(at[2])).normalized, width[2], sides, .8f, Hash01(k, salt + 4) * 6.28f);
            int apex = vertices.Count;
            vertices.Add(Spine(1f));
            uv.Add(new Vector2(.5f, 1f));
            Strip(body, rings[0], rings[1], sides);
            Strip(body, rings[1], rings[2], sides);
            for (int s = 0; s < sides; s++) tips.AddRange(new[] { tipRing + s, apex, tipRing + s + 1 });
        }

        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        var colors = new List<Color32>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
        mesh.SetColors(colors);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(body, 0);
        mesh.SetTriangles(tips, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Точка ядра стручка по направлению p единичной сферы: эллипсоид 0,5 × 0,5 × 0,6, бугры и рёбра шелухи.</summary>
    private static Vector3 SeedCore(Vector3 p, int salt)
    {
        float bumps = 1f + .1f * (SphereNoise(p, 2.2f, salt) * 2f - 1f);
        float ribs = 1f + .05f * Mathf.Cos(6f * Mathf.Atan2(p.y, p.x)) * (1f - Mathf.Abs(p.z));
        var v = p * (.5f * bumps * ribs);
        v.z *= 1.2f;
        return v;
    }

    /// <summary>Кольцо sides + 1 вершин (шов развёртки) вокруг оси axis в center: рамка как у шипов Шипомёта.</summary>
    private static void AddRing(List<Vector3> vertices, List<Vector2> uv, Vector3 center, Vector3 axis, float radius, int sides, float v, float phase)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) < .95f ? Vector3.forward : Vector3.right;
        Vector3 side = Vector3.Cross(axis, reference).normalized, other = Vector3.Cross(side, axis);
        for (int s = 0; s <= sides; s++)
        {
            float a = s / (float)sides * Mathf.PI * 2f + phase;
            vertices.Add(center + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * radius);
            uv.Add(new Vector2(s / (float)sides, v));
        }
    }

    /// <summary>Полоса между кольцами low и high (по sides + 1 вершин): обход (v, up, v + 1) — нормали наружу.</summary>
    private static void Strip(List<int> triangles, int low, int high, int sides)
    {
        for (int s = 0; s < sides; s++)
            triangles.AddRange(new[] { low + s, high + s, low + s + 1, low + s + 1, high + s, high + s + 1 });
    }
}
