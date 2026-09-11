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
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
    }
}
