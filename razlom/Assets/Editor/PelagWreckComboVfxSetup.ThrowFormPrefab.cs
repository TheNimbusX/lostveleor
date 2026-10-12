using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Свои элементы форм Броска якоря (см. PelagWreckComboVfxSetup.ThrowForm): звенья призрачной цепи Веера, сеть из цепей
/// Невода, призрачный якорь Веера. Меши сети и звеньев строит вид каждый кадр (объекты стоят в нуле мира, вершины — в мире);
/// частицы без залпа вид выпускает сам (Emit).
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    /// <summary>Очередь звеньев: после струны базы (Transparent+38) — звенья поверх света.</summary>
    private const int ThrowLinksQueue = 3039;

    /// <summary>
    /// Звенья призрачной цепи (кадр form-fan-ghost-v1: боковые цепи — светлое лавандовое стекло в фиолетовом свете): шейдер
    /// «Form Chain» только звеньями (_GlowAlpha 0) — свет вокруг даёт струна базы в краске Веера.
    /// </summary>
    private static Material ThrowLinksMaterial(Texture2D facets)
    {
        Material m = Fresh("M_ThrowForm_GhostChain", Shader.Find(ThrowChainShaderName));
        m.SetTexture("_FacetTex", facets);
        m.SetFloat("_GlowAlpha", 0f);
        m.SetFloat("_Links", 1f);
        m.SetFloat("_LinkPitch", PelagThrowFormLook.LinkPitch);
        m.SetFloat("_LinkHalf", PelagThrowFormLook.LinkHalf);
        m.SetFloat("_LinkThick", .02f);
        m.SetColor("_LinkBody", Lin(0xC9BEFF, 1f));
        m.SetColor("_LinkRim", new Color(1.05f, 1.02f, 1.5f, 1f));
        m.SetColor("_LinkShadow", Lin(0x3A1E9A, 1f));
        m.SetFloat("_LinkGlass", .85f);
        m.SetFloat("_CameraPush", .05f);
        m.renderQueue = ThrowLinksQueue;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    /// <summary>Цепи сети Невода: тёмное железо со светлой гранью, шов светится морской зеленью, звенья крупнее звеньев рига.</summary>
    private static Material ThrowNetStrandMaterial(Texture2D facets)
    {
        PelagThrowFormLook.Palette net = PelagThrowFormLook.Net;
        Material m = Fresh("M_ThrowForm_NetStrand", Shader.Find(ThrowChainShaderName));
        m.SetTexture("_FacetTex", facets);
        m.SetColor("_Deep", Lin(net.Edge, 1f));
        m.SetColor("_Mid", Lin(net.Mid, 1.6f));
        m.SetColor("_Light", Lin(net.Light, 1f));
        m.SetColor("_Hot", Lin(net.Hot, 1.05f));
        m.SetFloat("_Links", 1f);
        // V9 (игра V8 1:1: проволока .04 м почти вся — «кромка», звенья читались мятным стеклом): звено — тёмная сталь с
        // холодным бликом, свет морской зелени — вокруг (кадр: тёмные цепи в бирюзовом свечении).
        m.SetColor("_LinkBody", new Color(.06f, .07f, .08f, 1f));
        m.SetColor("_LinkRim", new Color(.24f, .34f, .34f, 1f));
        m.SetColor("_LinkShadow", Lin(0x05261E, 1f));
        m.SetFloat("_LinkGlass", 1f);
        // V5 (проба p1: полосы-ленты .34 м на ячейке .6 м сливались в мятное пятно): звено — тёмное железо почти во всю ленту,
        // свет морской зелени — узкий шов и ореол вокруг (кадр: тёмные цепи, светится шов, между цепями видна земля).
        // V7 (игра, съёмка formsv2 cap1: звено .13 × .05 с проволокой .02 м — 1 px при ~50 px/м, цепи читались мятной лентой):
        // звено крупнее (.20 × .065, проволока .04 — ~2 px), полоса света уже (под звеньями, видна сквозь кольца), свет — ореолом
        // по краям ленты (кадр: тёмные звенья железа в свечении морской зелени).
        // V10 (игра V9 1:1: лента .28 м и звено .12 — цепи сети толще кадровых): звено и лента тоньше.
        m.SetFloat("_LinkPitch", .18f);
        m.SetFloat("_LinkHalf", .052f);
        m.SetFloat("_LinkThick", .036f);
        m.SetFloat("_BodyW", .20f);
        m.SetFloat("_BodyAlpha", .70f);
        m.SetFloat("_CoreW", .07f);
        m.SetFloat("_CoreW0", .04f);
        // V8 (игра V7 1:1: ореол .8 × 1.8 заливал ленту мятным — цепи «из мятного стекла»): ореол слабее, земля видна,
        // свет сети — в узлах (кадр).
        m.SetFloat("_HaloAlpha", .55f);
        m.SetFloat("_HaloGain", 1.6f);
        m.SetFloat("_Sparkle", .35f);
        m.SetFloat("_FlowSpeed", 1.2f);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    /// <summary>Узлы сети — блёстки-звёзды морской зелени с белым ядром.</summary>
    private static Material ThrowNetKnotMaterial()
    {
        Material m = Fresh("M_ThrowForm_NetKnot", Shader.Find(ThrowGlintShaderName));
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ThrowGlintTexture));
        // V10: ореол узла — светлая бирюза краски (V9 — зелёные точки; на кадре узлы бело-бирюзовые), ядро белое.
        m.SetColor("_Mid", Lin(PelagThrowFormLook.Net.Light, .8f));
        m.SetColor("_Hot", new Color(.92f, 1.12f, 1.08f, 1f));
        // V9: узлы — главный свет сети (кадр): ярче.
        m.SetFloat("_Gain", 1.8f);
        m.SetFloat("_FlareGain", 1.6f);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    /// <summary>Призрак Веера: копии материалов Призрачного якоря Крушения (тот же шейдер), ореол фиолетовее, без чернил.</summary>
    private static void ThrowGhostMaterials(ref ThrowFormMaterials t, Texture2D facets, Shader bit)
    {
        Material body = Fresh("M_ThrowForm_GhostBody", Shader.Find(GhostShaderName));
        body.SetTexture("_FacetTex", facets);
        // V8 (игра V7: призрак тусклый лавандовый, терялся у фиолетовых лучей): светлее и белее, кромка шире (кадр — почти
        // белое светящееся стекло с белым краем).
        body.SetColor("_Inner", Lin(0xAEA4FF, 1f));
        body.SetColor("_Core", Lin(0xE6E1FF, 1f));
        body.SetColor("_Band", Lin(0x6A4BFF, 1f));
        body.SetVector("_FacetTones", new Vector4(.86f, 1.42f, .64f, .28f));
        body.SetColor("_Rim", new Color(.80f, .76f, 1.25f, 1f));
        body.SetColor("_Hot", new Color(1.1f, 1.08f, 1.65f, 1f));
        body.SetFloat("_RimFrom", .30f);
        body.SetFloat("_HotFrom", .48f);
        body.SetFloat("_InnerAlpha", .93f);
        body.SetFloat("_FacetLines", .85f);
        body.SetFloat("_BandFreq", 2.2f);
        // Голова броска ~в 3 раза меньше падающего якоря Крушения: ореол уже (в пикселях), иначе силуэт тонет в «пятне».
        Material glowIn = GhostGlow("M_ThrowForm_GhostGlowIn", facets, new Color(.62f, .46f, 1.6f, 1f), .86f, 5.5f, 100f, 3037);
        Material glowOut = GhostGlow("M_ThrowForm_GhostGlowOut", facets, new Color(.24f, .12f, .85f, 1f), .50f, 10f, 50f, 3038);
        Material shard = GhostBit(bit, "M_ThrowForm_GhostShard", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        shard.SetFloat("_ShapeAlpha", 1f);
        shard.SetFloat("_BevelMul", 1.25f);
        shard.SetColor("_BevelAdd", new Color(.42f, .40f, .62f, 1f));
        Material spark = GhostBit(bit, "M_ThrowForm_GhostSpark", SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .34f);
        foreach (var m in new[] { body, glowIn, glowOut, shard, spark }) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
        t.GhostBody = body; t.GhostIn = glowIn; t.GhostOut = glowOut; t.GhostShard = shard; t.GhostSpark = spark;
    }

    private static Renderer ThrowMeshChild(GameObject parent, string name, Mesh mesh, Material material, int order)
    {
        var host = new GameObject(name);
        host.transform.SetParent(parent.transform, false);
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = order;
        return renderer;
    }

    private static void SaveThrowNetPrefab(ThrowFormMaterials t)
    {
        var root = new GameObject(ThrowNetName);
        try
        {
            AddElement(root, PelagVfxId.AnchorThrowFormNet, 6f);
            ThrowMeshChild(root, "Strands", null, t.Strand, 0);
            ThrowMeshChild(root, "Knots", null, t.Knot, 1);
            // Искры узлов на стяжке: краска Невода (цвет — EmitParams вида).
            ParticleSystem sparks = Sparks(root, "Sparks", t.NetSpark, 260, .14f, .30f, 1.5f, 5f, .03f, .055f, .5f, 0f);
            Lane(sparks);
            // Обрывки звеньев на стяжке: железо сколами, падают.
            ParticleSystem links = Chips(root, "Links", t.Chip, 60, .35f, .55f, 1.5f, 3f, .05f, .09f, 2.4f, 1.2f,
                Linear(PelagWreckComboLook.MarkChipDark), Linear(PelagWreckComboLook.MarkChipLight));
            Lane(links);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(ThrowNetName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Призрак: «Model» (вид ставит позу настоящей головы, повёрнутую на угол полосы) → Body, GlowIn, GlowOut.</summary>
    private static void SaveThrowGhostPrefab(ThrowFormMaterials t)
    {
        var root = new GameObject(ThrowGhostName);
        try
        {
            AddElement(root, PelagVfxId.AnchorThrowFormGhost, 6f);
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            ThrowMeshChild(model, "Body", t.GhostMesh, t.GhostBody, 0);
            ThrowMeshChild(model, "GlowIn", t.GhostMesh, t.GhostIn, 1);
            ThrowMeshChild(model, "GlowOut", t.GhostMesh, t.GhostOut, 2);
            Lane(Chips(root, "Shards", t.GhostShard, 60, .35f, .60f, 0f, 0f, .08f, .16f, -.25f, 0f, GhostShardDark, GhostShardLight));
            ParticleSystem motes = Sparks(root, "Motes", t.GhostSpark, 120, .22f, .40f, 0f, 0f, .03f, .05f, -.2f, 0f);
            Lane(motes);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(ThrowGhostName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Якорь Пелага (меш Tripo головы рига) копией в свой меш БЕЗ цепи: низ в 0, высота 1, по центру XZ; TEXCOORD1 —
    /// сглаженные нормали (ореол «… Glow» без щелей). Вид переводит позу настоящей головы в эти оси (низ, высота — из bounds).
    /// </summary>
    private static Mesh ThrowGhostMesh()
    {
        Mesh source = GhostSourceMesh();
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/ThrowFormGhostAnchor.asset", "ThrowFormGhostAnchor");
        Vector3[] sv = source.vertices;
        Vector3[] sn = source.normals;
        Bounds b = source.bounds;
        float k = 1f / Mathf.Max(.01f, b.size.y);
        var shift = new Vector3(b.center.x, b.min.y, b.center.z);
        var vertices = new Vector3[sv.Length];
        var normals = new Vector3[sv.Length];
        var sums = new Dictionary<Vector3Int, Vector3>();
        var keys = new Vector3Int[sv.Length];
        for (int i = 0; i < sv.Length; i++)
        {
            vertices[i] = (sv[i] - shift) * k;
            normals[i] = sn != null && sn.Length == sv.Length ? sn[i] : Vector3.up;
            Vector3 q = vertices[i] * 2000f;
            keys[i] = new Vector3Int(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), Mathf.RoundToInt(q.z));
            sums.TryGetValue(keys[i], out Vector3 sum);
            sums[keys[i]] = sum + normals[i];
        }
        var smooth = new List<Vector3>(sv.Length);
        var colors = new Color32[sv.Length];
        for (int i = 0; i < sv.Length; i++)
        {
            smooth.Add(sums[keys[i]].normalized);
            colors[i] = new Color32(255, 255, 255, 255);
        }
        mesh.indexFormat = sv.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.SetUVs(1, smooth);
        mesh.colors32 = colors;
        mesh.triangles = source.triangles;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }
}
