using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Хозяин Чащи: текстуры пакета и материалы тела. Цвет — без подъёма яркости
/// (белый _BaseColor). Эмиссия — только руны и глаза по маске (T_ThicketMaster_Emission_F2/F3,
/// production/dressing/make_emission.py); в материале она чёрная, яркость по фазам
/// пишет ThicketMasterPhaseDressing блоком свойств. Тело не высветляется. Листва фаз
/// (T_ThicketMaster_Color_F2/F3, make_phase_colors.py) — только импорт: карту ставит вид.
/// </summary>
public static partial class ThicketMasterBuilder
{
    /// <summary>Пути текстур тела в Resources; null — такой карты в пакете нет.</summary>
    private sealed class TextureSet
    {
        public string Color, Normal, Orm;

        /// <summary>Маски эмиссии: F2 — руны и глаза (Ф2, ярость), F3 — плюс смоляные прожилки (Ф3).</summary>
        public string EmissionF2, EmissionF3;

        /// <summary>
        /// Листва фаз (make_phase_colors.py): F2 — осень, F3 — цветение. Материал их не держит —
        /// ставит ThicketMasterPhaseDressing блоком свойств; здесь только импорт как у цвета.
        /// </summary>
        public string ColorF2, ColorF3;
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
            // Маски эмиссии — раньше «color»: иначе ушли бы в rest и сломали «одна картинка — цвет».
            if (name.Contains("emission"))
            {
                if (name.EndsWith("f3", StringComparison.Ordinal)) { if (set.EmissionF3 == null) set.EmissionF3 = file; }
                else if (set.EmissionF2 == null) set.EmissionF2 = file;
            }
            else if (name.Contains("normal")) { if (set.Normal == null) set.Normal = file; }
            else if (orm.IsMatch(name)) { if (set.Orm == null) set.Orm = file; }
            // Листва фаз — раньше «color»: T_ThicketMaster_Color_F2 не должна стать цветом тела.
            else if (name.EndsWith("_f2", StringComparison.Ordinal)) { if (set.ColorF2 == null) set.ColorF2 = file; }
            else if (name.EndsWith("_f3", StringComparison.Ordinal)) { if (set.ColorF3 == null) set.ColorF3 = file; }
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
        // Маски эмиссии линейные: в файле — оттенок янтаря как есть (make_emission.py), яркость —
        // множитель _EmissionColor. Через sRGB янтарь ушёл бы в красный.
        if (set.EmissionF2 != null) ConfigureTexture(set.EmissionF2, TextureImporterType.Default, false);
        if (set.EmissionF3 != null) ConfigureTexture(set.EmissionF3, TextureImporterType.Default, false);
        ConfigurePhaseColors(set);
    }

    /// <summary>Листва фаз — как карта цвета тела (sRGB, 2048, сжатие HQ).</summary>
    private static void ConfigurePhaseColors(TextureSet set)
    {
        if (set.ColorF2 != null) ConfigureTexture(set.ColorF2, TextureImporterType.Default, true);
        if (set.ColorF3 != null) ConfigureTexture(set.ColorF3, TextureImporterType.Default, true);
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

    /// <summary>
    /// URP Lit (копия с прозрачностью перед героем — <see cref="SeeThroughShader"/>): цвет без
    /// подъёма яркости, нормали, затенение и металл/гладкость из ORM.
    /// </summary>
    private static Material LitMaterial(TextureSet set)
    {
        var shader = SeeThroughShader(UrpLit);
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
        ApplyEmission(material, set);
        EditorUtility.SetDirty(material);
        // Эмиссия терялась в памяти после сохранения (ловушка Вихря): сохранить и перечитать с диска.
        AssetDatabase.SaveAssetIfDirty(material);
        AssetDatabase.ImportAsset(MaterialPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) ?? material;
    }

    /// <summary>
    /// Светятся только руны и глаза по маске F2 (Ф3 — карту F3 ставит вид). Цвет в материале
    /// чёрный: Ф1, сон и заглушка без вида — руны тёмные; яркость по фазам пишет
    /// ThicketMasterPhaseDressing блоком свойств. _BaseColor белый — тело не высветляется.
    /// GI-флаги RealtimeEmissive: URP (LitShader.ValidateMaterial → BaseShaderGUI.SetMaterialKeywords)
    /// при каждой загрузке материала держит _EMISSION, только пока в флагах есть AnyEmissive;
    /// с None ключ снимался сразу после включения, и рантайм ничего не зажигал (ревью 02.10).
    /// FixupEmissiveFlag при чёрном цвете ставит EmissiveIsBlack только вместе с BakedEmissive,
    /// так что RealtimeEmissive переживает чёрный цвет. Нет маски — эмиссии нет, как раньше.
    /// </summary>
    private static void ApplyEmission(Material material, TextureSet set)
    {
        var map = Load(set.EmissionF2);
        material.SetColor("_EmissionColor", Color.black);
        material.SetTexture("_EmissionMap", map);
        if (map != null)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }

    /// <summary>
    /// Для ThicketMasterDressingSetup: материал тела, собранный до масок эмиссии, получает
    /// их без пересборки представления. True — материал в порядке или поправлен.
    /// </summary>
    internal static bool EnsurePhaseEmission()
    {
        // Прозрачность перед героем — тем же автоматическим проходом (загрузка редактора, выход из Play).
        ApplySeeThrough();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) return false;
        var set = Classify(TexturesIn(Root));
        ConfigurePhaseColors(set);
        if (set.EmissionF2 == null) return false;
        var map = Load(set.EmissionF2);
        if (map != null && material.IsKeywordEnabled("_EMISSION") && material.GetTexture("_EmissionMap") == map
            && material.globalIlluminationFlags == MaterialGlobalIlluminationFlags.RealtimeEmissive) return true;
        ConfigureTexture(set.EmissionF2, TextureImporterType.Default, false);
        if (set.EmissionF3 != null) ConfigureTexture(set.EmissionF3, TextureImporterType.Default, false);
        ApplyEmission(material, set);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        AssetDatabase.ImportAsset(MaterialPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[thicketmaster] Материал тела: включена эмиссия рун и глаз по маске " + set.EmissionF2 + ".");
        return true;
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

    // ---------------------------------------------------------- прозрачность перед героем

    private const string UrpLit = "Universal Render Pipeline/Lit", UrpSimpleLit = "Universal Render Pipeline/Simple Lit";

    /// <summary>Материалы накладок фаз (ThicketMasterDressingSetup): ягоды — URP Lit, цветы — URP Simple Lit.</summary>
    private const string PhaseMaterialFolder = "Assets/Resources/VFX/ThicketMaster/Phases/Materials";

    /// <summary>
    /// Выключатель прозрачности перед героем (владелец 02.10: «прозрачность должна быть, чтоб было
    /// видно»). false — тело и накладки фаз вернутся на шейдеры URP при следующей загрузке редактора
    /// или «Прозрачность: подключить»: запасной путь, если копия URP Lit не соберётся.
    /// </summary>
    internal static readonly bool SeeThroughEnabled = true;

    private static bool _warnedSeeThroughShader;

    /// <summary>
    /// Шейдер материала босса вместо шейдера URP: его копия с сетчатой прозрачностью перед героем
    /// (Resources/Shaders/RazlomBossSeeThroughLit/SimpleLit.shader, ThicketMasterSeeThroughRules).
    /// Копии нет, она с ошибкой или прозрачность выключена — сам шейдер URP.
    /// </summary>
    internal static Shader SeeThroughShader(string urpShaderName)
    {
        var urp = Shader.Find(urpShaderName);
        string name = urpShaderName == UrpLit ? ThicketMasterSeeThroughRules.LitShader
            : urpShaderName == UrpSimpleLit ? ThicketMasterSeeThroughRules.SimpleLitShader : null;
        if (!SeeThroughEnabled || name == null) return urp;
        var shader = Shader.Find(name);
        if (shader != null && !ShaderUtil.ShaderHasError(shader)) return shader;
        if (!_warnedSeeThroughShader)
        {
            _warnedSeeThroughShader = true;
            Debug.LogWarning("[thicketmaster] Шейдер «" + name + "» " + (shader == null ? "не найден" : "с ошибкой")
                             + " — босс остаётся на «" + urpShaderName + "», сквозь него героя не видно.");
        }
        return urp;
    }

    [MenuItem("Разлом/Босс/Хозяин Чащи/Прозрачность: подключить")]
    public static void ApplySeeThroughFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[thicketmaster] Прозрачность подключается только вне Play.");
            return;
        }
        int changed = ApplySeeThrough();
        Debug.Log("[thicketmaster] Прозрачность перед героем: " + (SeeThroughEnabled ? "включена" : "выключена")
                  + ", материалов переведено: " + changed + " (тело " + MaterialPath + ", накладки " + PhaseMaterialFolder + ").");
    }

    /// <summary>
    /// Тело (ThicketMaster.mat) и накладки фаз (ягоды, цветы) — на шейдер с прозрачностью или обратно
    /// на URP (по <see cref="SeeThroughEnabled"/>). Свойства, ключевые слова, очередь, тип отсечки и
    /// флаги GI сохраняются: шейдеры — копии URP с теми же свойствами. Возвращает число переведённых.
    /// </summary>
    internal static int ApplySeeThrough()
    {
        int changed = 0;
        if (SwitchSeeThrough(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath))) changed++;
        if (AssetDatabase.IsValidFolder(PhaseMaterialFolder))
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { PhaseMaterialFolder }))
                if (SwitchSeeThrough(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))) changed++;
        if (changed > 0)
            Debug.Log("[thicketmaster] Прозрачность перед героем: материалов на " + (SeeThroughEnabled ? "сетке" : "URP") + " — " + changed + ".");
        return changed;
    }

    private static bool SwitchSeeThrough(Material material)
    {
        if (material == null || material.shader == null) return false;
        string current = material.shader.name;
        string urp = current == UrpLit || current == ThicketMasterSeeThroughRules.LitShader ? UrpLit
            : current == UrpSimpleLit || current == ThicketMasterSeeThroughRules.SimpleLitShader ? UrpSimpleLit : null;
        if (urp == null) return false;
        var target = SeeThroughShader(urp);
        if (target == null || target == material.shader) return false;
        string[] keywords = material.shaderKeywords;
        int queue = material.renderQueue;
        string renderType = material.GetTag("RenderType", false, string.Empty);
        var gi = material.globalIlluminationFlags;
        material.shader = target;
        material.shaderKeywords = keywords;
        material.renderQueue = queue;
        if (renderType.Length > 0) material.SetOverrideTag("RenderType", renderType);
        material.globalIlluminationFlags = gi;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return true;
    }

    private static Texture2D Load(string path) => path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
}
