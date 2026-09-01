using UnityEditor;
using UnityEngine;

/// <summary>
/// Настройки импорта персонажей проекта.
///
/// Модели и их анимации обязаны приезжать в проект одинаково настроенными.
/// Делать это руками через инспектор нельзя: настройка живёт в .meta, .meta
/// легко потерять при переносе, и тогда модель и клипы приезжают с разными
/// правилами скелета.
///
/// Pelag v5 намеренно использует Generic: runtime-меш и все рабочие клипы
/// имеют один и тот же 65-костный Mixamo bind pose. Humanoid здесь не нужен
/// для ретаргета и повторно интерпретировал колени/голеностопы, из-за чего ноги
/// визуально выворачивались. Старые разнородные ассеты остаются Humanoid.
/// </summary>
public sealed class RazlomCharacterImport : AssetPostprocessor
{
    private const string CharactersFolder = "/Resources/Characters/";
    private const string ArtCharactersFolder = "/Art/Characters/";
    private const string PelagV4Model =
        "Assets/Resources/Characters/Pelag_v4/Pelag_v4.fbx";

    private string NormalPath => assetPath.Replace('\\', '/');

    private bool IsCharacter =>
        NormalPath.Contains(CharactersFolder) || NormalPath.Contains(ArtCharactersFolder);

    /// <summary>Эти папки содержат клипы, а не игровые меши персонажей.</summary>
    private bool IsAnimationOnly =>
        NormalPath.Contains("/Animations/") || NormalPath.Contains("/Mixamo/");

    private bool IsWhirlwind => NormalPath.EndsWith("/Pelag_v4/Animations/Pelag_Whirlwind.fbx");

    private bool IsTripoRun => NormalPath.EndsWith("/Pelag_v5/Animations/Pelag_Run_Tripo.fbx");

    private bool IsPelagMixamo => NormalPath.Contains("/Pelag_v5/Mixamo/");

    private bool IsPelagMixamoRuntime =>
        NormalPath.Contains("/Runtime/") && NormalPath.EndsWith("MixamoRig.fbx");

    public override uint GetVersion() => 8;

    private void OnPreprocessAnimation()
    {
        if (IsTripoRun)
        {
            var runImporter = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] runClips = runImporter.defaultClipAnimations;
            if (runClips != null && runClips.Length > 0)
            {
                ModelImporterClipAnimation run = runClips[0];
                for (int i = 0; i < runClips.Length; i++)
                {
                    string take = runClips[i].takeName ?? string.Empty;
                    if (take.Contains("preset:biped:run"))
                    {
                        run = runClips[i];
                        break;
                    }
                }

                // Always rebuild this entry from the FBX defaults. Replacing an
                // FBX while retaining its GUID also retains the old .meta clip;
                // ours still pointed at the obsolete Blender take "Scene" and
                // therefore sampled a frozen pose from the real Tripo file.
                run.name = "Pelag_Run_Tripo";
                run.loopTime = true;
                run.loopPose = true;
                run.lockRootRotation = true;
                run.keepOriginalOrientation = true;
                run.lockRootPositionXZ = true;
                run.keepOriginalPositionXZ = true;
                run.lockRootHeightY = true;
                run.keepOriginalPositionY = true;
                runImporter.clipAnimations = new[] { run };
            }
            return;
        }

        if (IsPelagMixamo)
        {
            ConfigurePelagMixamoClips((ModelImporter)assetImporter);
            return;
        }

        if (!IsWhirlwind) return;

        var importer = (ModelImporter)assetImporter;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length != 1) return;

        // Unity Humanoid обычно выносит поворот Hips в root motion. У Вихря
        // этот оборот — не перемещение сущности, а сама боевая поза. Запекаем
        // rotation в скелет; XZ/Y root translation по-прежнему блокируется.
        clips[0].name = "Pelag_Whirlwind";
        clips[0].loopTime = false;
        clips[0].lockRootRotation = true;
        clips[0].keepOriginalOrientation = true;
        clips[0].lockRootPositionXZ = true;
        clips[0].keepOriginalPositionXZ = true;
        clips[0].lockRootHeightY = true;
        clips[0].keepOriginalPositionY = true;
        importer.clipAnimations = clips;
    }

    private void ConfigurePelagMixamoClips(ModelImporter importer)
    {
        ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0) return;

        ModelImporterClipAnimation source = defaults[0];
        string file = System.IO.Path.GetFileNameWithoutExtension(NormalPath);

        if (file == "Pelag_MX_SaberCombo")
        {
            // Два удара и recovery остаются одним тейком, но delivery теперь
            // пересобран в 30 fps. Граница 25 принадлежит обеим половинам:
            // поза стыка совпадает, а B сохраняет весь мягкий recovery до 74.
            importer.clipAnimations = new[]
            {
                Clip(source, "Pelag_MX_SaberAttackA", 1f, 25f, false),
                Clip(source, "Pelag_MX_SaberAttackB", 25f, 74f, false)
            };
            return;
        }

        string name;
        float first = source.firstFrame;
        float last = source.lastFrame;
        bool loop = false;
        switch (file)
        {
            case "Pelag_MX_Idle": name = "Pelag_MX_Idle"; loop = true; break;
            case "Pelag_MX_Run": name = "Pelag_MX_Run"; loop = true; break;
            case "Pelag_MX_TurnLeft": name = "Pelag_MX_TurnLeft"; break;
            case "Pelag_MX_TurnRight": name = "Pelag_MX_TurnRight"; break;
            case "Pelag_MX_InjuredRun": name = "Pelag_MX_InjuredRun"; loop = true; break;
            case "Pelag_MX_Hit": name = "Pelag_MX_Hit"; last = Mathf.Min(last, 70f); break;
            case "Pelag_MX_Death": name = "Pelag_MX_Death"; last = Mathf.Min(last, 180f); break;
            case "Pelag_MX_Whirlwind": name = "Pelag_MX_Whirlwind"; last = Mathf.Min(last, 92f); break;
            case "Pelag_MX_DualCombo":
                // The Blender FBX round-trip reports this take as 2-77 even
                // though the authored delivery is 1-76. Pin the importer to
                // the authored range so the first contact remains frame 7
                // (six simulation ticks into the 75-tick clip).
                name = "Pelag_MX_DualCombo"; first = 1f; last = 76f; break;
            case "Pelag_MX_AnchorAttack": name = "Pelag_MX_AnchorAttack"; break;
            case "Pelag_MX_AnchorLeap":
                name = "Pelag_MX_AnchorLeap"; first = 1f; last = 16f; break;
            case "Pelag_MX_AnchorSweep":
                name = "Pelag_MX_AnchorSweep"; first = 1f; last = 16f; break;
            case "Pelag_MX_ChainStep":
                name = "Pelag_MX_ChainStep"; first = 1f; last = 6f; loop = true; break;
            case "Pelag_MX_RunStart":
                name = "Pelag_MX_RunStart"; first = 1f; last = 6f; break;
            case "Pelag_MX_RunStop":
                name = "Pelag_MX_RunStop"; first = 1f; last = 6f; break;
            case "Pelag_MX_StrafeLeft":
                name = "Pelag_MX_StrafeLeft"; first = 1f; last = 17f; loop = true; break;
            case "Pelag_MX_StrafeRight":
                name = "Pelag_MX_StrafeRight"; first = 1f; last = 17f; loop = true; break;
            case "Pelag_MX_StrafeBack":
                name = "Pelag_MX_StrafeBack"; first = 1f; last = 17f; loop = true; break;
            default: name = file; break;
        }

        importer.clipAnimations = new[] { Clip(source, name, first, last, loop) };
    }

    private static ModelImporterClipAnimation Clip(ModelImporterClipAnimation source,
        string name, float first, float last, bool loop)
    {
        return new ModelImporterClipAnimation
        {
            name = name,
            takeName = source.takeName,
            firstFrame = first,
            lastFrame = last,
            loopTime = loop,
            loopPose = loop,
            lockRootRotation = true,
            keepOriginalOrientation = true,
            lockRootPositionXZ = true,
            keepOriginalPositionXZ = true,
            lockRootHeightY = true,
            keepOriginalPositionY = true
        };
    }

    private void OnPreprocessModel()
    {
        if (!IsCharacter) return;

        var importer = (ModelImporter)assetImporter;

        // The runtime body and every v5 clip use the exact same 65-bone Mixamo
        // hierarchy and bind pose. This belongs in OnPreprocessModel: putting
        // it in OnPreprocessAnimation leaves the importer on Humanoid before
        // Unity creates the Avatar and bends the authored knees/ankles again.
        if (IsPelagMixamo || IsPelagMixamoRuntime)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;

            if (IsPelagMixamo)
            {
                importer.importAnimation = true;
                importer.importNormals = ModelImporterNormals.None;
                importer.importTangents = ModelImporterTangents.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            else
            {
                importer.importAnimation = false;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            return;
        }

        // Tripo export contains the correct Pelag hierarchy, but its bind pose is
        // taken from frame 1 of the run. Letting Unity build a fresh Humanoid
        // avatar from that pose produces a bad automatic map (RightHand was
        // mapped to R_ForearmTwist01) and the run arrives twisted. The playable
        // mesh is Pelag v4, so the animation must be retargeted through that
        // exact, already validated avatar.
        if (IsTripoRun)
        {
            Avatar pelagAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(PelagV4Model);
            if (pelagAvatar == null)
            {
                Debug.LogError($"[Разлом] Не найден Avatar Pelag v4 для Tripo-run: {PelagV4Model}");
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = pelagAvatar;
            importer.importAnimation = true;
            importer.importNormals = ModelImporterNormals.None;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            return;
        }

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

    private void OnPreprocessTexture()
    {
        if (!NormalPath.Contains("/Resources/Characters/Pelag_v4/") ||
            !NormalPath.Contains("BaseColor")) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
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

        Shader urp = Shader.Find("Razlom/Texture Toon")
                     ?? Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) return;

        Texture main = material.mainTexture;
        Color tint = material.HasProperty("_Color") ? material.color : Color.white;

        // Переименование FBX рвёт ссылку на встроенные текстуры: внутри файла
        // записан путь к прежней папке `<старое имя>.fbm`, и Unity его не
        // находит. Сами картинки при этом лежат рядом и импортированы как
        // обычные ассеты — их достаточно связать обратно.
        if (main == null) main = FindTextureBeside(material);

        material.shader = urp;

        if (main != null)
        {
            // Слот пишется по тому, что объявил ШЕЙДЕР. У «Razlom/Texture Toon»
            // и URP-шейдеров есть только _BaseMap, а material.mainTexture жёстко
            // адресует _MainTex: на таком материале присвоение не «ничего не
            // делает», а роняет ошибку в лог на каждый материал модели.
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", main);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", main);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);

        // Персонажи мультяшные: блик по всей фигуре мешает читать силуэт,
        // а он здесь главный канал распознавания.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_ShadowColor"))
            material.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);

        // Порог задаётся ЯВНО, а не наследуется из шейдера. Материал переживает
        // смену шейдера со своими прежними значениями, поэтому правка значения
        // по умолчанию в .shader не доходит до уже импортированных моделей —
        // и выглядит это как «поменял, ничего не изменилось».
        if (material.HasProperty("_MidColor"))
            material.SetColor("_MidColor", new Color(0.94f, 0.90f, 0.91f, 1f));
        if (material.HasProperty("_MidThreshold")) material.SetFloat("_MidThreshold", 0.24f);
        if (material.HasProperty("_LightThreshold")) material.SetFloat("_LightThreshold", 0.62f);
        if (material.HasProperty("_LightFeather")) material.SetFloat("_LightFeather", 0.045f);
        if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 1.10f);
    }

    /// <summary>
    /// Ищет текстуру этого материала среди картинок, лежащих рядом с моделью.
    ///
    /// Связь идёт по НОМЕРУ куска: материал «tripo_part_24_material» и картинка
    /// «..._tripo_part_24_basecolor» относятся к одной части тела. Номер
    /// сравнивается целиком, а не подстрокой: «part_8» иначе поймал бы
    /// «part_83», и лицо приехало бы с текстурой пряжки.
    /// </summary>
    private Texture FindTextureBeside(Material material)
    {
        string folder = System.IO.Path.GetDirectoryName(NormalPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return null;

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });

        // Сначала по номеру куска — это случай модели, нарезанной на части.
        int number = ExtractPartNumber(material.name);
        if (number >= 0)
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ExtractPartNumber(System.IO.Path.GetFileNameWithoutExtension(path)) != number) continue;

                var byNumber = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (byNumber == null) continue;

                Debug.Log($"[Разлом] Текстура по номеру части: {material.name} → {byNumber.name}");
                return byNumber;
            }
        }

        // Если рядом лежит РОВНО ОДНА картинка — это она и есть, как её ни зови.
        // После ретопологии модель приезжает одним мешем с одним атласом, и имя
        // атласа не связано с именем материала ничем: гадать по имени нечего,
        // а выбор однозначен. Двух и больше картинок это правило не касается —
        // там угадывание уже было бы враньём.
        if (guids.Length == 1)
        {
            var single = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (single != null)
            {
                Debug.Log($"[Разлом] Единственная текстура рядом с моделью: {material.name} → {single.name}");
                return single;
            }
        }

        Debug.LogWarning($"[Разлом] Для «{material.name}» текстуры рядом с моделью нет " +
                         $"(картинок рядом: {guids.Length}) — кусок останется залит цветом.");
        return null;
    }

    /// <summary>«..._part_24_...» → 24. Нет номера — минус один.</summary>
    private static int ExtractPartNumber(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(name, @"part_(\d+)");

        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : -1;
    }
}
