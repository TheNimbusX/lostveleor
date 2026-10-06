using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Материалы и меш железного осколка Крушения «холодное железо» (см. PelagWreckIronVfxSetup.cs).
/// Чужие материалы и текстуры — только чтение; копии CFXR — свои файлы (PelagWhirlwindVfxSetup.CfxrMaterial).
/// Любая правка — поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    private static Material OwnMaterial(string name, string shaderName)
    {
        string path = MaterialPath(name);
        Shader shader = Shader.Find(shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Solid(Material m, Texture texture, Color tint, Color shade, float glowHeight, float core, float outline, float hull)
    {
        m.SetTexture("_BaseMap", texture != null ? texture : PelagWhirlwindVfxSetup.WhiteTexture());
        m.SetColor("_BaseColor", tint);
        m.SetColor("_Tint", Color.white);
        m.SetColor("_ShadeColor", shade);
        m.SetFloat("_LightThreshold", .5f);
        m.SetFloat("_LightFeather", .07f);
        m.SetFloat("_AmbientWeight", .55f);
        m.SetColor("_EarthColor", EarthClod);
        m.SetFloat("_Earth", 0f);
        m.SetColor("_GlowColor", Sky);
        m.SetColor("_HotColor", SkyPale);
        m.SetColor("_GlintColor", Color.white * 1.25f);
        m.SetFloat("_Glow", 0f);
        m.SetFloat("_GroundY", -10000f);
        m.SetFloat("_GlowHeight", glowHeight);
        m.SetFloat("_Core", core);
        m.SetFloat("_CoreGain", 2f);
        m.SetFloat("_VertexColor", 0f);
        m.SetColor("_OutlineColor", Outline);
        m.SetFloat("_OutlineWidth", outline);
        m.SetFloat("_HullWidth", hull);
        m.SetTextureScale("_BaseMap", Vector2.one);
        m.SetFloat("_Saturation", 1f);
        m.SetColor("_HighlightColor", Color.black);
        m.SetFloat("_InnerGlow", 0f);
        m.SetFloat("_VeinGlow", 0f);
    }

    private static Texture ArenaRockTexture()
    {
        var arena = AssetDatabase.LoadAssetAtPath<Material>(RockBMaterial);
        return arena != null && arena.HasProperty("_BaseMap") ? arena.GetTexture("_BaseMap") : null;
    }

    /// <summary>
    /// Камень: фактура камня арены B, приглушённая к серо-бурому (V5 — бежевая «картошка»), оттенок
    /// куска — _Tint/_Earth от вида; подошва светится только в миг выхода. V7 («форма слишком чёткая»):
    /// контур тоньше — оболочка 1,2 см вместо 3 см и контур силуэта 1,5 px, камни не вырезаны из картона.
    /// </summary>
    private static Material StoneMaterial()
    {
        Material m = OwnMaterial(StoneMaterialName, SolidShaderName);
        Solid(m, ArenaRockTexture(), StoneTint, StoneShade, .1f, 0f, 1.5f, .012f);
        m.SetFloat("_Saturation", StoneSaturation);
        return m;
    }

    /// <summary>Железный осколок: тёмная сталь, подошва светится из трещины в миг выхода, контур.</summary>
    private static Material IronMaterial()
    {
        Material m = OwnMaterial(IronMaterialName, SolidShaderName);
        Solid(m, null, Steel, SteelShade, .12f, 0f, 1.5f, .01f);
        return m;
    }

    /// <summary>
    /// Звено (V6): тёмное железо #2A2F36 со стальными бликами и толстым тёмным контуром (оболочка 4,5 см +
    /// контур силуэта); голубой — только изнутри: внутренняя сторона прута, щель у земли (_GlowHeight над
    /// _GroundY) и жилы-трещины Hovl Crack4 (_BaseMap — маска, не цвет).
    /// </summary>
    private static Material LinkMaterial()
    {
        Material m = OwnMaterial(LinkMaterialName, SolidShaderName);
        Solid(m, AssetDatabase.LoadAssetAtPath<Texture2D>(LaneCrackTexture), LinkIron, SteelShade, .04f, 1f, 3f, .06f);
        m.SetTextureScale("_BaseMap", new Vector2(3f, .5f));
        m.SetColor("_HighlightColor", SteelHighlight);
        m.SetFloat("_InnerGlow", 1f);
        m.SetFloat("_VeinGlow", .5f);
        return m;
    }

    /// <summary>Летящий кусок (меш-частица): фактура камня, цвет — от частицы (камень, ком земли, железо).</summary>
    private static Material ChunkMaterial()
    {
        Material m = OwnMaterial(ChunkMaterialName, SolidShaderName);
        Solid(m, ArenaRockTexture(), Color.white, StoneShade, .01f, 0f, 1.4f, 0f);
        m.SetFloat("_VertexColor", 1f);
        m.SetFloat("_Saturation", StoneSaturation);
        return m;
    }

    /// <summary>
    /// Разбитая земля: трещины Hovl, тёмная воронка, шум CFXR; свет из трещин — мягкий холодный голубой.
    /// V7: рваное пятно Crater2 и звезда трещин Crack (Hovl), трещины Crack4 за край полосы — без ровных краёв.
    /// </summary>
    private static Material GroundMaterial()
    {
        Material m = OwnMaterial(GroundMaterialName, GroundShaderName);
        m.SetTexture("_CrackTex", AssetDatabase.LoadAssetAtPath<Texture2D>(LaneCrackTexture));
        m.SetTexture("_DirtTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CraterDirtTexture));
        m.SetTexture("_CraterCrackTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CraterCrackTexture));
        m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture));
        m.SetTexture("_SplatTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture));
        m.SetTexture("_StarTex", AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexture));
        m.SetFloat("_CrackReach", .7f);
        m.SetFloat("_SplatScale", .85f);
        m.SetFloat("_StarScale", .8f);
        m.SetColor("_DarkColor", DarkEarth);
        m.SetFloat("_DarkAlpha", .78f);
        m.SetFloat("_CrackDark", .9f);
        m.SetColor("_GlowColor", Sky);
        m.SetColor("_HotColor", SkyPale);
        m.SetFloat("_GlowBase", .8f);
        m.SetFloat("_GlowHot", .4f);
        m.SetFloat("_GlowBlur", 2.6f);
        m.SetFloat("_SpotGlow", 1.5f);
        m.SetFloat("_CraterGlow", .3f);
        m.SetFloat("_CraterCrackGlow", 1f);
        m.SetFloat("_LaneEdgeGlow", .25f);
        m.SetFloat("_LinkA", Game.View.PelagWreckIronRules.LinkAxisRadius);
        m.SetFloat("_LinkC", Game.View.PelagWreckIronRules.LinkAxisHalfStraight);
        m.SetFloat("_LinkTube", Game.View.PelagWreckIronRules.LinkTube);
        m.SetFloat("_Cool", Game.View.PelagWreckIronRules.CoolSeconds);
        m.SetFloat("_Hold", Game.View.PelagWreckIronRules.HoldSeconds);
        m.SetFloat("_FadeTime", Game.View.PelagWreckIronRules.FadeSeconds);
        m.SetFloat("_LaneTile", 1.35f);
        m.SetFloat("_Age", 0f);
        m.renderQueue = 2992;
        return m;
    }
    /// <summary>Обломки пака (unlit 3×3 с обводом): камень и железо — цветом частицы.</summary>
    private static Material DebrisMaterial()
        => PelagWhirlwindVfxSetup.CfxrMaterial(DebrisMaterialName, AssetDatabase.LoadAssetAtPath<Material>(CfxrDebris), false);

    /// <summary>Брызги земли (V7): плоские обломки пака без обводки (flat unlit 3×3), цвет земли — от частицы.</summary>
    private static Material DirtMaterial()
        => PelagWhirlwindVfxSetup.CfxrMaterial(DirtMaterialName, AssetDatabase.LoadAssetAtPath<Material>(CfxrDirt), false);

    /// <summary>Пыль земли: мягкие облака пака, бурый цвет частицы.</summary>
    private static Material DustMaterial()
        => PelagWhirlwindVfxSetup.CfxrMaterial(DustMaterialName, AssetDatabase.LoadAssetAtPath<Material>(CfxrDust), false);

    /// <summary>Искры удара: заострённый росчерк пака, HDR — белый жар и холодный голубой.</summary>
    private static Material SparkMaterial() => Hdr(PelagWhirlwindVfxSetup.CfxrMaterial(SparkMaterialName,
        AssetDatabase.LoadAssetAtPath<Material>(CfxrStreak), false), 2.6f);

    /// <summary>Росчерк маха: тот же росчерк пака, мягче по яркости (сталь, не вспышка).</summary>
    private static Material StreakMaterial() => Hdr(PelagWhirlwindVfxSetup.CfxrMaterial(StreakMaterialName,
        AssetDatabase.LoadAssetAtPath<Material>(CfxrStreak), false), 1.5f);

    /// <summary>Короткая маленькая вспышка контакта (не «блин»): мягкое свечение пака, HDR.</summary>
    private static Material FlashMaterial() => Hdr(PelagWhirlwindVfxSetup.CfxrMaterial(FlashMaterialName,
        AssetDatabase.LoadAssetAtPath<Material>(CfxrGlow), false), 2.2f);

    private static Material Hdr(Material material, float multiply)
    {
        if (material == null) return null;
        material.EnableKeyword("_CFXR_HDR_BOOST");
        if (material.HasProperty("_HdrMultiply")) material.SetFloat("_HdrMultiply", multiply);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Железный осколок: неровная плита-клин 1 м по длине (−Z острый конец), гранёная (свои нормали
    /// граней), цвета вершин 8-битные (razlom-unity-particle-gotchas). Свой ассет, пересобирается.
    /// </summary>
    private static Mesh IronShardMesh()
    {
        string path = MeshPath(IronMeshName);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = IronMeshName };
            AssetDatabase.CreateAsset(mesh, path);
        }
        // Контур плиты в плоскости XZ (ширина по X, длина по Z), толщина по Y.
        Vector2[] outline =
        {
            new Vector2(0f, -.5f), new Vector2(.22f, -.18f), new Vector2(.28f, .2f), new Vector2(.1f, .5f),
            new Vector2(-.16f, .42f), new Vector2(-.3f, .06f), new Vector2(-.14f, -.24f)
        };
        const float top = .07f, bottom = -.07f;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            int at = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
        }
        var centreTop = new Vector3(.02f, top + .03f, .04f);
        var centreBottom = new Vector3(-.02f, bottom - .02f, -.02f);
        int n = outline.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = outline[i], q = outline[(i + 1) % n];
            var pt = new Vector3(p.x, top, p.y); var qt = new Vector3(q.x, top, q.y);
            var pb = new Vector3(p.x * .92f, bottom, p.y * .92f); var qb = new Vector3(q.x * .92f, bottom, q.y * .92f);
            Face(centreTop, qt, pt);
            Face(centreBottom, pb, qb);
            Face(pt, qt, qb);
            Face(pt, qb, pb);
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        var colors = new Color32[vertices.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        mesh.SetColors(colors);
        var uv = new Vector2[vertices.Count];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x + .5f, vertices[i].z + .5f);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
