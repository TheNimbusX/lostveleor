using UnityEditor;

/// <summary>Deterministic import settings for Pelag's production saber.</summary>
public sealed class RazlomWeaponImport : AssetPostprocessor
{
    private string NormalPath => assetPath.Replace('\\', '/');
    private bool IsSaber => NormalPath.Contains("/Resources/Weapons/Pelag/FantasySaber/");

    public override uint GetVersion() => 2;

    private void OnPreprocessModel()
    {
        if (!IsSaber) return;
        var importer = (ModelImporter)assetImporter;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.globalScale = 1f;
        // Geometry is authored and numerically validated in metres. Blender's
        // FBX writer still labels the file in centimetres; applying that file
        // metadata makes Unity shrink the 1.326 m saber to 1.326 cm.
        importer.useFileScale = false;
        importer.isReadable = false;
    }

    private void OnPreprocessTexture()
    {
        if (!IsSaber) return;
        var importer = (TextureImporter)assetImporter;
        if (NormalPath.EndsWith("_Normal.jpg"))
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else if (NormalPath.EndsWith("_Metallic.jpg") || NormalPath.EndsWith("_Roughness.jpg"))
        {
            importer.sRGBTexture = false;
        }
    }
}
