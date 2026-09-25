using UnityEditor;
using UnityEngine;

namespace GameCursorImport
{
    // Курсоры не сжимаются и не получают mipmaps: при 48 px любой блок
    // компрессии превращает острые засечки в грязные точки.
    public sealed class GameCursorImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/UI/Cursors/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Cursor;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 64;
        }
    }
}
