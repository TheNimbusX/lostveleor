using UnityEditor;
using UnityEngine;

namespace Game.View.Editor
{
    /// <summary>Keep the short-lived ground marker crisp and genuinely transparent.</summary>
    public sealed class MoveOrderMarkerImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetPath.Replace('\\', '/') != "Assets/Resources/VFX/MoveOrderMarker.png") return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
        }
    }
}
