using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Хозяин Чащи: текстуры пакета и материалы тела. Цвет — без подъёма яркости
/// (белый _BaseColor), эмиссии нет: персонаж не высветляется.
/// </summary>
public static partial class ThicketMasterBuilder
{
    /// <summary>Пути текстур тела в Resources; null — такой карты в пакете нет.</summary>
    private sealed class TextureSet
    {
        public string Color, Normal, Orm;
    }

    /// <summary>
    /// Карты по именам файлов: «normal» — нормали, «orm» отдельным словом — ORM,
    /// «color/albedo/diffuse» — цвет; одна безымянная картинка — цвет. Картинок
    /// рядом нет — пробуем достать вшитые в FBX (папка Textures).
    /// </summary>
    /// <summary>
    /// Идёт выгрузка вшитых текстур: RazlomCharacterImport не гасит импорт материалов
    /// (его ветка новых мобов ставит None на каждом импорте, и выгружать было бы нечего).
    /// </summary>
    internal static bool ExtractingEmbeddedTextures { get; private set; }

    private static TextureSet FindTextures(ModelImporter importer)
    {
        var set = Classify(TexturesIn(Root));
        if (set.Color != null) return set;
        try
        {
            // Вшитые картинки Unity отдаёт только при импорте материалов: включаем на одну выгрузку.
            ExtractingEmbeddedTextures = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            if (!AssetDatabase.IsValidFolder(ExtractedTextures)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Textures");
            bool extracted = importer.ExtractTextures(ExtractedTextures);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!extracted) Debug.LogWarning("[thicketmaster] Во FBX нет вшитых текстур.");
        }
        catch (Exception e) { Debug.LogWarning("[thicketmaster] Текстуры из FBX не достались: " + e.Message); }
        finally
        {
            ExtractingEmbeddedTextures = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }
        set = Classify(TexturesIn(Root).Concat(TexturesIn(ExtractedTextures + "/")).ToArray());
        if (set.Color == null)
            Debug.LogWarning("[thicketmaster] Нет текстуры цвета ни в пакете, ни во FBX — тело будет белым URP Lit.");
        return set;
    }

    private static string[] TexturesIn(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.GetFiles(folder)
            .Where(f => TextureExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => folder + Path.GetFileName(f))
            .Where(f => f != OcclusionTexture && f != MetalSmoothTexture)
            .ToArray();
    }

    private static TextureSet Classify(string[] files)
    {
        var set = new TextureSet();
        var orm = new Regex("(^|[_\\-. ])orm([_\\-. ]|$)", RegexOptions.IgnoreCase);
        var rest = new System.Collections.Generic.List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (name.Contains("normal")) { if (set.Normal == null) set.Normal = file; }
            else if (orm.IsMatch(name)) { if (set.Orm == null) set.Orm = file; }
            else if (name.Contains("color") || name.Contains("albedo") || name.Contains("diffuse")) { if (set.Color == null) set.Color = file; }
            else rest.Add(file);
        }
        if (set.Color == null && rest.Count == 1) set.Color = rest[0];
        return set;
    }

    private static void ConfigureTextures(TextureSet set)
    {
        if (set.Color != null) ConfigureTexture(set.Color, TextureImporterType.Default, true);
        if (set.Normal != null)
        {
            ConfigureTexture(set.Normal, TextureImporterType.NormalMap, false);
            // Нормали DirectX (Y вниз) — Unity ждёт OpenGL: зелёный канал переворачивается.
            if (Path.GetFileNameWithoutExtension(set.Normal).ToLowerInvariant().Contains("dx")
                && AssetImporter.GetAtPath(set.Normal) is TextureImporter normal && !normal.flipGreenChannel)
            {
                normal.flipGreenChannel = true;
                normal.SaveAndReimport();
            }
        }
        if (set.Orm != null)
        {
            ConfigureTexture(set.Orm, TextureImporterType.Default, false);
            SplitOrm(set.Orm);
            ConfigureTexture(OcclusionTexture, TextureImporterType.Default, false);
            ConfigureTexture(MetalSmoothTexture, TextureImporterType.Default, false);
        }
    }

    private static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { Debug.LogWarning("[thicketmaster] Нет текстуры " + path); return; }
        bool changed = importer.textureType != type || importer.sRGBTexture != srgb || !importer.mipmapEnabled
                       || importer.maxTextureSize != 2048;
        if (!changed) return;
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.alphaIsTransparency = false;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// ORM (R — затенение, G — шероховатость, B — металл) в карты URP Lit: затенение
    /// и металл/гладкость (A = 1 − шероховатость). Пересобирается, если ORM новее.
    /// </summary>
    private static void SplitOrm(string ormPath)
    {
        DateTime orm = File.GetLastWriteTimeUtc(ormPath);
        if (File.Exists(OcclusionTexture) && File.Exists(MetalSmoothTexture)
            && File.GetLastWriteTimeUtc(OcclusionTexture) >= orm && File.GetLastWriteTimeUtc(MetalSmoothTexture) >= orm) return;
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(ormPath), false)) throw new InvalidOperationException("Не читается " + ormPath);
            Color32[] pixels = source.GetPixels32();
            var occlusion = new Color32[pixels.Length];
            var metalSmooth = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte ao = pixels[i].r, rough = pixels[i].g, metal = pixels[i].b;
                occlusion[i] = new Color32(ao, ao, ao, 255);
                metalSmooth[i] = new Color32(metal, metal, metal, (byte)(255 - rough));
            }
            WritePng(OcclusionTexture, occlusion, source.width, source.height);
            WritePng(MetalSmoothTexture, metalSmooth, source.width, source.height);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        AssetDatabase.ImportAsset(OcclusionTexture, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(MetalSmoothTexture, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void WritePng(string path, Color32[] pixels, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    // ---------------------------------------------------------- materials

    /// <summary>URP Lit: цвет без подъёма яркости, нормали, затенение и металл/гладкость из ORM.</summary>
    private static Material LitMaterial(TextureSet set)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Нет шейдера URP/Lit.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader) { name = "ThicketMaster" }; AssetDatabase.CreateAsset(material, MaterialPath); }
        else if (material.shader != shader) { material.shader = shader; material.shaderKeywords = Array.Empty<string>(); }
        material.SetTexture("_BaseMap", Load(set.Color));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_Cull", 2f);
        material.DisableKeyword("_SPECULAR_SETUP");

        var normal = Load(set.Normal);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");

        var occlusion = set.Orm != null ? Load(OcclusionTexture) : null;
        material.SetTexture("_OcclusionMap", occlusion);
        material.SetFloat("_OcclusionStrength", 1f);
        if (occlusion != null) material.EnableKeyword("_OCCLUSIONMAP"); else material.DisableKeyword("_OCCLUSIONMAP");

        var metalSmooth = set.Orm != null ? Load(MetalSmoothTexture) : null;
        material.SetTexture("_MetallicGlossMap", metalSmooth);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.SetFloat("_Metallic", 0f);
        // С картой гладкость = A карты × _Smoothness; без карты — матовая кора и листва.
        material.SetFloat("_Smoothness", metalSmooth != null ? 1f : .12f);
        if (metalSmooth != null) material.EnableKeyword("_METALLICSPECGLOSSMAP"); else material.DisableKeyword("_METALLICSPECGLOSSMAP");
        // Эмиссии нет: тело не светится и не высветляется.
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Второй слот под URP Lit: Texture Toon только ради прохода UnitOutlineMask —
    /// постоянной кромки врага (ArenaView). Тело он не рисует: проходы цвета,
    /// свечения, тени и глубины выключены.
    /// </summary>
    private static Material OutlineMask(string colorPath)
    {
        var toon = Shader.Find("Razlom/Texture Toon");
        if (toon == null) { Debug.LogWarning("[thicketmaster] Нет шейдера Razlom/Texture Toon — босс без контура врага."); return null; }
        var mask = AssetDatabase.LoadAssetAtPath<Material>(MaskPath);
        if (mask == null) { mask = new Material(toon) { name = "ThicketMaster_OutlineMask" }; AssetDatabase.CreateAsset(mask, MaskPath); }
        else if (mask.shader != toon) mask.shader = toon;
        mask.SetTexture("_BaseMap", Load(colorPath));
        mask.SetFloat("_OutlineWidth", 0f);
        foreach (string pass in new[] { "UniversalForward", "SRPDefaultUnlit", "InkOutline", "ShadowCaster", "DepthOnly" })
            mask.SetShaderPassEnabled(pass, false);
        mask.SetShaderPassEnabled("UnitOutlineMask", true);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static Texture2D Load(string path) => path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
}
