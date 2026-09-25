using UnityEditor;
using UnityEngine;

/// <summary>
/// Импорт арта главного меню.
///
/// Настройки те же, что у инвентаря, и по той же причине: это экранная
/// графика, а не текстура на модели. Мипмапы ей не нужны и только мылят
/// картинку, сжатие даёт полосы на градиенте неба, а подложка 1672 px должна
/// дойти до экрана в исходном размере.
/// </summary>
public sealed class MainMenuArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/MainMenu/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        // Панорама меню пака (3840x2160) — единственный полноэкранный арт: на 1440p и 4K
        // ужатая до 2048 она заметно мылится.
        importer.maxTextureSize = assetPath.EndsWith("/menu_panorama.png") ? 4096 : 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Маски анимации — это веса, а не цвет. В sRGB гамма исказила бы их:
        // половина силы колыхания превратилась бы примерно в пятую часть.
        bool mask = assetPath.EndsWith("_Mask.png");
        importer.sRGBTexture = !mask;
        importer.alphaIsTransparency = !mask;
    }
}
