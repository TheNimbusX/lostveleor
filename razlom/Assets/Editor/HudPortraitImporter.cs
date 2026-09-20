using UnityEditor;

// HUD читает исходники один раз для общей маски скругления арта и рамки.
// Сжатие размывало способности на небольших экранных плитках.
//
// MIP-УРОВНИ ВКЛЮЧЕНЫ (15 сентября). Canvas-HUD рисует эти 1254-пиксельные
// картинки на плитках ~70 px напрямую видеокартой; без mip-уровней уменьшение
// в 17 раз давало рваные края и мерцание — владелец: «резко и нечетко».
// Фильтр Kaiser и небольшой отрицательный bias держат мелкие плитки чёткими;
// CPU-чтение IMGUI (isReadable) берёт нулевой уровень и не меняется.
public sealed class HudPortraitImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/HUD/") &&
            !assetPath.StartsWith("Assets/Resources/UI/Abilities/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.isReadable = true;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        importer.mipMapBias = -.3f;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
    }
}
