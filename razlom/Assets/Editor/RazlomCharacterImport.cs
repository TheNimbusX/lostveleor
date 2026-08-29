using UnityEditor;
using UnityEngine;

/// <summary>
/// Настройки импорта персонажей проекта.
///
/// Модели и их анимации обязаны приезжать в проект одинаково настроенными.
/// Делать это руками через инспектор нельзя: настройка живёт в .meta, .meta
/// легко потерять при переносе, и тогда персонаж молча приходит Generic-ригом —
/// а на Generic не работает ретаргет, то есть не работают вообще все анимации.
///
/// Humanoid выбран не по вкусу: скелеты у моделей разные — у одной свой,
/// у другой Mixamo, — а клипы записаны под разные скелеты. Ретаргет через
/// Humanoid единственное, что позволяет им работать вместе.
/// </summary>
public sealed class RazlomCharacterImport : AssetPostprocessor
{
    private const string CharactersFolder = "/Resources/Characters/";
    private const string ArtCharactersFolder = "/Art/Characters/";

    private string NormalPath => assetPath.Replace('\\', '/');

    private bool IsCharacter =>
        NormalPath.Contains(CharactersFolder) || NormalPath.Contains(ArtCharactersFolder);

    /// <summary>Файл лежит в подпапке Animations — значит это клип, а не персонаж.</summary>
    private bool IsAnimationOnly => NormalPath.Contains("/Animations/");

    private void OnPreprocessModel()
    {
        if (!IsCharacter) return;

        var importer = (ModelImporter)assetImporter;

        // Уже настроенный ассет второй раз не трогаем: иначе правки в инспекторе
        // сбрасывались бы при каждом переимпорте.
        if (!importer.importSettingsMissing && importer.animationType == ModelImporterAnimationType.Human)
            return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        // Метр в метре: симуляция считает в метрах, и любой другой масштаб
        // разъедется с радиусами тел и дальностью удара.
        importer.globalScale = 1f;
        importer.useFileScale = true;

        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = false;

        if (IsAnimationOnly)
        {
            // От файла с анимацией нужен только клип. Меш там тоже лежит —
            // Mixamo кладёт его в каждый экспорт, — но в игру он не пойдёт:
            // на него никто не ссылается.
            importer.importAnimation = true;
            importer.importNormals = ModelImporterNormals.None;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
        else
        {
            importer.importAnimation = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        Debug.Log($"[Разлом] Импорт настроен ({(IsAnimationOnly ? "анимация" : "персонаж")}): {assetPath}");
    }

    /// <summary>
    /// Проект на URP, а FBX приносит материал под встроенный шейдер. Без замены
    /// персонаж приезжает белым или розовым — именно это и происходит, когда
    /// «модель есть, а раскраски нет».
    ///
    /// Текстуру приходится переносить руками: у встроенного шейдера она зовётся
    /// _MainTex, у URP — _BaseMap, и при смене шейдера связь теряется.
    /// </summary>
    private void OnPostprocessMaterial(Material material)
    {
        if (!IsCharacter) return;

        Shader urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) return;

        Texture main = material.mainTexture;
        Color tint = material.HasProperty("_Color") ? material.color : Color.white;

        material.shader = urp;

        if (main != null)
        {
            material.SetTexture("_BaseMap", main);
            material.mainTexture = main;
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);

        // Персонажи мультяшные: блик по всей фигуре мешает читать силуэт,
        // а он здесь главный канал распознавания.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
    }
}
