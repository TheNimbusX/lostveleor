using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Бросок якоря · ФОРМЫ НА СТЕКЕ БАЗЫ (08.10; целевые кадры ART/characters/pelag/anchor-throw-2026-10-07/chatgpt-results:
/// Невод form-net-v1, Веер form-fan-ghost-v1, Гарпун form-harpoon-v2). Форма = база «холодное железо» в своей краске (тот же
/// сборщик BuildIronPaint из .ThrowIron с палитрой PelagThrowFormLook) + свои элементы:
///  • VFX_Pelag_ThrowForm{Chain,Hit,Catch,Stun}{Net,Fan,Harpoon} — перекраски струны, знака, ловли и оглушения базы
///    (материалы M_ThrowIron_*{Net,Fan,Harpoon}); у Веера в струне ещё «Links» — звенья призрачной цепи (шейдер
///    «Razlom/Anchor Throw Form Chain», только звенья), у Гарпуна в оглушении — пунктир орбиты;
///  • VFX_Pelag_ThrowFormNet — сеть из цепей Невода (полосы-цепи со звеньями и светом шва, узлы-блёстки) + искры узлов;
///  • VFX_Pelag_ThrowFormGhost — призрачный якорь Веера: меш якоря Пелага на призрачном шейдере Крушения (копии его
///    материалов, БЕЗ чёрного обвода) + осколки распада.
/// Борозда, комья, камни и пыль — общие с базой (земля одна). Первая попытка форм 07.10 (свои ленты, знаки, звёзды, борозды)
/// заменена — её ассеты удаляются здесь же (только свои). Шейдеры и материалы Крушения не трогаются. Сохраняются только свои
/// ассеты и библиотека (SaveAssetIfDirty); общего SaveAssets нет.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int ThrowFormVersion = 10;
    private static readonly string ThrowFormRevision = "PelagThrowFormV" + ThrowFormVersion;
    private const string ThrowChainShaderName = "Razlom/Anchor Throw Form Chain";
    private const string ThrowGlintShaderName = "Razlom/Anchor Throw Form Glint";
    private const string ThrowGlintTexture = CfxrGraphics + "cfxr magic star.png";
    public const string ThrowNetName = "VFX_Pelag_ThrowFormNet", ThrowGhostName = "VFX_Pelag_ThrowFormGhost";
    private static readonly PelagForm[] ThrowForms = { PelagForm.AnchorThrowNet, PelagForm.AnchorThrowFan, PelagForm.AnchorThrowHarpoon };
    private static readonly string[] ThrowFormTags = { "Net", "Fan", "Harpoon" };

    /// <summary>Ассеты первой попытки форм (07.10), которые заменила краска базы: удаляются (свои, не в git).</summary>
    private static readonly string[] ThrowFormOrphans =
    {
        "Prefabs/VFX_Pelag_ThrowFormChain.prefab", "Prefabs/VFX_Pelag_ThrowFormStun.prefab", "Prefabs/VFX_Pelag_ThrowFormFurrow.prefab",
        "Materials/M_ThrowForm_ChainNet.mat", "Materials/M_ThrowForm_ChainFan.mat", "Materials/M_ThrowForm_ChainHarpoon.mat",
        "Materials/M_ThrowForm_BurstNet.mat", "Materials/M_ThrowForm_BurstFan.mat", "Materials/M_ThrowForm_BurstHarpoon.mat",
        "Materials/M_ThrowForm_SparkNet.mat", "Materials/M_ThrowForm_SparkFan.mat", "Materials/M_ThrowForm_SparkHarpoon.mat",
        "Materials/M_ThrowForm_StunStar.mat", "Materials/M_ThrowForm_Furrow.mat"
    };

    private static string FormChainName(int i) => "VFX_Pelag_ThrowFormChain" + ThrowFormTags[i];

    [InitializeOnLoadMethod]
    private static void ScheduleThrowForm()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallThrowForm(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: формы на базе (Невод, Веер, Гарпун) — подключить")]
    public static void InstallThrowForm() => InstallThrowForm(false);

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: формы на базе (Невод, Веер, Гарпун) — пересобрать")]
    public static void RebuildThrowForm() => InstallThrowForm(true);

    public static void InstallThrowForm(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string shader in new[] { ThrowStreakShaderName, ThrowIronFurrowShaderName, BitShaderName, ThrowChainShaderName,
                     ThrowGlintShaderName, GhostShaderName, GhostGlowShaderName })
            if (Shader.Find(shader) == null)
            {
                Debug.LogWarning("[throw-form] Нет шейдера «" + shader + "» — формы Броска не собраны.");
                return;
            }
        foreach (string path in new[] { ThrowGlintTexture, ThrowIronStarTexture, FacetTexture, SparkTexture, BurstTexture, DebrisTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[throw-form] Нет текстуры " + path + " — формы Броска не собраны.");
                return;
            }
        if (GhostSourceMesh() == null)
        {
            Debug.LogWarning("[throw-form] Нет меша якоря " + GhostModel + " — формы Броска не собраны.");
            return;
        }
        // База собирается своим сетапом (.ThrowIron): её общие борозда и комья нужны формам — без них ждём следующего раза.
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_ThrowIron_Furrow")) == null
            || AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_ThrowIron_Clod")) == null
            || AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Chip")) == null
            || AssetDatabase.LoadAssetAtPath<Material>(WreckRockMaterial) == null || IronRockMeshes().Length == 0)
        {
            Debug.LogWarning("[throw-form] База Броска (M_ThrowIron_Furrow/Clod) или камни Крушения не собраны — формы позже.");
            return;
        }
        if (force || !ThrowFormUpToDate()) BuildThrowForm();
        bool ok = true;
        for (int i = 0; i < 3; i++)
        {
            IronPaint p = FormPaint(i, null);
            ok &= PelagWhirlwindVfxSetup.Bind(library, p.ChainId, PrefabPath(p.ChainName), i == 1 ? 3 : 2);
            ok &= PelagWhirlwindVfxSetup.Bind(library, p.HitId, PrefabPath(p.HitName), 6);
            ok &= PelagWhirlwindVfxSetup.Bind(library, p.CatchId, PrefabPath(p.CatchName), 2);
            ok &= PelagWhirlwindVfxSetup.Bind(library, p.StunId, PrefabPath(p.StunName), i == 2 ? 2 : 3);
        }
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowFormNet, PrefabPath(ThrowNetName), 1);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowFormGhost, PrefabPath(ThrowGhostName), 2);
        if (!ok) Debug.LogWarning("[throw-form] Префабы форм Броска не нашлись — записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
        foreach (string orphan in ThrowFormOrphans)
            if (AssetDatabase.LoadMainAssetAtPath(Root + "/" + orphan) != null) AssetDatabase.DeleteAsset(Root + "/" + orphan);
    }

    private static bool ThrowFormUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(FormChainName(0)));
        if (importer == null || importer.userData != ThrowFormRevision) return false;
        for (int i = 0; i < 3; i++)
        {
            IronPaint p = FormPaint(i, null);
            foreach (string name in new[] { p.ChainName, p.HitName, p.CatchName, p.StunName })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        }
        foreach (string name in new[] { ThrowNetName, ThrowGhostName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static Color Gain(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
    private static Color FromRgb(PelagThrowFormLook.Rgb c) => new Color(c.R, c.G, c.B, 1f);

    /// <summary>Краска формы <paramref name="i"/> (0 Невод, 1 Веер, 2 Гарпун) из палитры PelagThrowFormLook — тот же расклад, что у базы.</summary>
    private static IronPaint FormPaint(int i, Material links)
    {
        PelagThrowFormLook.Palette p = PelagThrowFormLook.For(ThrowForms[i]);
        PelagThrowFormLook.Tint tint = PelagThrowFormLook.TintFor(ThrowForms[i]);
        string tag = ThrowFormTags[i];
        return new IronPaint
        {
            Tag = tag, ChainName = FormChainName(i), HitName = "VFX_Pelag_ThrowFormHit" + tag,
            CatchName = "VFX_Pelag_ThrowFormCatch" + tag, StunName = "VFX_Pelag_ThrowFormStun" + tag,
            ChainId = i == 0 ? PelagVfxId.AnchorThrowFormChainNet : i == 1 ? PelagVfxId.AnchorThrowFormChainFan : PelagVfxId.AnchorThrowFormChainHarpoon,
            HitId = i == 0 ? PelagVfxId.AnchorThrowFormHitNet : i == 1 ? PelagVfxId.AnchorThrowFormHitFan : PelagVfxId.AnchorThrowFormHitHarpoon,
            CatchId = i == 0 ? PelagVfxId.AnchorThrowFormCatchNet : i == 1 ? PelagVfxId.AnchorThrowFormCatchFan : PelagVfxId.AnchorThrowFormCatchHarpoon,
            StunId = i == 0 ? PelagVfxId.AnchorThrowFormStunNet : i == 1 ? PelagVfxId.AnchorThrowFormStunFan : PelagVfxId.AnchorThrowFormStunHarpoon,
            // Сердцевина — почти белая с оттенком краски и запасом яркости (как HDR-сердцевина базы).
            Edge = Hex(p.Edge), Mid = Hex(p.Mid), Light = Hex(p.Light), Hot = Gain(Color.Lerp(Hex(p.Hot), Hex(p.Light), .15f), 1.25f),
            HotShade = Gain(Hex(p.Light), 1.08f), HaloDeep = Hex(p.HaloDeep), HaloLight = Hex(p.HaloLight),
            BurstCore = Gain(Hex(p.Hot), 1.3f), ChipEdge = Gain(Hex(p.Edge), .42f), BitShade = Gain(Hex(p.Light), .92f),
            BitCore = Gain(Hex(p.Hot), 1.2f),
            Star = FromRgb(tint.Flare), Echo = FromRgb(tint.Echo), StunStar = FromRgb(tint.Stun),
            Links = i == 1 ? links : null, StunTrail = i == 2, CoreFrom = 1.3f
        };
    }

    private static void BuildThrowForm()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);
        Shader bit = Shader.Find(BitShaderName);
        Material furrow = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_ThrowIron_Furrow"));
        Material clod = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_ThrowIron_Clod"));
        Material links = ThrowLinksMaterial(facets);
        Material netSpark = null;
        for (int i = 0; i < 3; i++)
        {
            ThrowIronMaterials t = BuildIronPaint(FormPaint(i, links), facets, bit, furrow, clod);
            if (i == 0) netSpark = t.Spark;
        }
        var m = new ThrowFormMaterials
        {
            Strand = ThrowNetStrandMaterial(facets), Knot = ThrowNetKnotMaterial(), NetSpark = netSpark,
            Chip = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Chip")), GhostMesh = ThrowGhostMesh()
        };
        ThrowGhostMaterials(ref m, facets, bit);
        SaveThrowNetPrefab(m);
        SaveThrowGhostPrefab(m);
        var importer = AssetImporter.GetAtPath(PrefabPath(FormChainName(0)));
        if (importer != null && importer.userData != ThrowFormRevision)
        {
            importer.userData = ThrowFormRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[throw-form] Формы Броска на стеке базы собраны (" + ThrowFormRevision + ").");
    }

    private struct ThrowFormMaterials
    {
        public Material Strand, Knot, NetSpark, Chip, GhostBody, GhostIn, GhostOut, GhostShard, GhostSpark;
        public Mesh GhostMesh;
    }
}
