using UnityEditor;
using UnityEngine;

public sealed class CharacterSpriteImporter : AssetPostprocessor
{
    private const string SpriteFolder = "Assets/Resources/CharacterSprites/";
    private const float PixelsPerUnit = 420f;
    private static readonly Vector2 GroundedPivot = new Vector2(0.5f, 136f / 1536f);

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(SpriteFolder, System.StringComparison.Ordinal)) return;
        Configure((TextureImporter)assetImporter);
    }

    [InitializeOnLoadMethod]
    private static void EnsureExistingSpritesAreImported()
    {
        EditorApplication.delayCall += () =>
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteFolder.TrimEnd('/') });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                if (importer.textureType == TextureImporterType.Sprite
                    && Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)
                    && importer.mipmapEnabled
                    && importer.textureCompression == TextureImporterCompression.Uncompressed)
                    continue;

                Configure(importer);
                importer.SaveAndReimport();
            }
        };
    }

    private static void Configure(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = GroundedPivot;
        importer.SetTextureSettings(settings);
        importer.alphaIsTransparency = true;
        // Персонажи часто занимают всего несколько десятков пикселей экрана.
        // Полноразмерная текстура + mipmaps сохраняют линии комикса без ряби.
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
    }
}
