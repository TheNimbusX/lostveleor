using UnityEditor;
using UnityEngine;

public sealed class InventoryArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/Inventory/")) return;
        var importer=(TextureImporter)assetImporter;
        importer.textureType=TextureImporterType.Default;
        importer.mipmapEnabled=false;
        importer.wrapMode=TextureWrapMode.Clamp;
        importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency=true;
    }
}
