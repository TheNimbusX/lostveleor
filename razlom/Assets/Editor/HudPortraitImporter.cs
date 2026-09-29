using UnityEditor;

// HUD читает исходники один раз для общей маски скругления арта и рамки.
// Сжатие размывало способности на небольших экранных плитках.
//
// MIP-УРОВНИ ВКЛЮЧЕНЫ (15 сентября). Canvas-HUD рисует эти 1254-пиксельные
// картинки на плитках ~70 px напрямую видеокартой; без mip-уровней уменьшение
// в 17 раз давало рваные края и мерцание — владелец: «резко и нечетко».
// Фильтр Kaiser и небольшой отрицательный bias держат мелкие плитки чёткими;
// CPU-чтение IMGUI (isReadable) берёт нулевой уровень и не меняется.
//
// ПОРТРЕТ — ИНАЧЕ (29 сентября, владелец: кайма, рваный край, шов). У портрета Пелага
// (PelagPortrait*) Kaiser и отрицательный bias давали звон по краю волос и мерцание
// при дыхании портрета: mip-уровни простым усреднением, bias 0, трилинейная выборка —
// между уровнями без скачка. Маска портрета (PelagPortraitMask, рисует
// tools/ui-kit/make-hud-portrait.py) — одна альфа, линейная, процессору не нужна.
public sealed class HudPortraitImporter : AssetPostprocessor
{
    const string MaskName = "PelagPortraitMask";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/HUD/") &&
            !assetPath.StartsWith("Assets/Resources/UI/Abilities/")) return;
        Apply((TextureImporter)assetImporter, assetPath);
    }

    static bool IsPortrait(string path) =>
        path.StartsWith("Assets/Resources/UI/HUD/") && System.IO.Path.GetFileNameWithoutExtension(path).StartsWith("PelagPortrait");

    static bool IsMask(string path) => IsPortrait(path) && System.IO.Path.GetFileNameWithoutExtension(path) == MaskName;

    static void Apply(TextureImporter importer, string path)
    {
        bool portrait = IsPortrait(path), mask = IsMask(path);
        importer.textureType = mask ? TextureImporterType.SingleChannel : TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.isReadable = !mask;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = portrait ? TextureImporterMipFilter.BoxFilter : TextureImporterMipFilter.KaiserFilter;
        importer.mipMapBias = portrait ? 0f : -.3f;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.filterMode = portrait ? UnityEngine.FilterMode.Trilinear : UnityEngine.FilterMode.Bilinear;
        importer.alphaIsTransparency = !mask;
        if (!mask) return;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (settings.singleChannelComponent == TextureImporterSingleChannelComponent.Alpha) return;
        settings.singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;
        importer.SetTextureSettings(settings);
    }

    /// <summary>
    /// Портрет и маска импортированы так, как задаёт этот импортёр; иначе настройки ставятся и
    /// картинка переимпортируется. Смена кода импортёра сама старые картинки не переимпортирует —
    /// это зовёт материал портрета боевого HUD при сборке и миграции (CombatHudWcBuilder.PortraitMaterial).
    /// </summary>
    public static void EnsurePortraitImport(string path)
    {
        if (!IsPortrait(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        bool mask = IsMask(path);
        bool right = importer.mipmapFilter == TextureImporterMipFilter.BoxFilter && importer.mipMapBias == 0f
                     && importer.filterMode == UnityEngine.FilterMode.Trilinear
                     && (!mask || importer.textureType == TextureImporterType.SingleChannel && !importer.sRGBTexture);
        if (right) return;
        Apply(importer, path);
        importer.SaveAndReimport();
    }
}
