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
    private string NormalPath => assetPath.Replace('\\', '/');

    private bool IsCharacter =>
        NormalPath.Contains(CharactersFolder) || NormalPath.Contains(ArtCharactersFolder);

    /// <summary>Эти папки содержат клипы, а не игровые меши персонажей.</summary>
    private bool IsAnimationOnly =>
        NormalPath.Contains("/Animations/") || NormalPath.Contains("/Mixamo/");

    private bool IsWhirlwind => NormalPath.EndsWith("/Pelag_v4/Animations/Pelag_Whirlwind.fbx");

    private bool IsPelagMixamo => NormalPath.Contains("/Pelag_v5/Mixamo/");

    private bool IsPelagMixamoRuntime =>
        NormalPath.Contains("/Runtime/") && NormalPath.EndsWith("MixamoRig.fbx");

    /// <summary>
    /// Клип моба: «Resources/Characters/&lt;Моб&gt;/&lt;Моб&gt;@&lt;Роль&gt;.fbx».
    /// Конвенция самой Unity «модель@клип» и родной формат выгрузки Mixamo —
    /// по ней же раскладывает клипы по ролям RazlomMobAnimatorBuilder.
    /// </summary>
    private bool IsMobClip =>
        NormalPath.Contains(CharactersFolder)
        && System.IO.Path.GetFileNameWithoutExtension(NormalPath).Contains('@');

    public override uint GetVersion() => 13;

    private void OnPreprocessAnimation()
    {
        if (IsPelagMixamo)
        {
            ConfigurePelagMixamoClips((ModelImporter)assetImporter);
            return;
        }

        if (IsMobClip)
        {
            ConfigureMobClip((ModelImporter)assetImporter);
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

    /// <summary>
    /// Клипы мобов. Всё, кроме цикла бега, оставляет проезд корня в позе;
    /// БЕГ — НЕТ, и вот почему.
    ///
    /// ЭТО ПОЧИНКА «МОБЫ ХОДЯТ РЫВКАМИ И ТЕЛЕПОРТИРУЮТСЯ». В .meta бега
    /// Лесного стража стояла галка Bake Into Pose на Root Transform Position
    /// (XZ). Она означает ровно одно: проезд остаётся ВНУТРИ позы, то есть
    /// тело едет само, поверх шага от симуляции. Замер клипа в Blender:
    ///
    ///     Forest_Guardian@Run — 27 кадров цикла, таз проезжает 1.037
    ///     единицы исходника по прямой (путь 1.039 — это не раскачка);
    ///     ×2.4 (ArenaView.OrvillScale) = 2.49 м за цикл.
    ///
    /// Цикл идёт 0.867 с при скорости 1.22 (CharacterAnimatorView) — 0.71 с.
    /// Значит картинка ехала вдвое быстрее сущности три четверти секунды, а
    /// на стыке цикла прыгала на 2.49 м назад одним кадром. Контактная тень
    /// висит на корне и никуда не уезжала: на видео владельца видно, как тело
    /// отрывается от собственной тени и возвращается к ней.
    ///
    /// applyRootMotion в ArenaView выключён навсегда — там же и записано, что
    /// «клип, двигающий персонажа сам, увёл бы картинку от симуляции». Снятый
    /// с позы проезд просто выбрасывается, и тело остаётся ровно там, куда его
    /// поставил тик.
    ///
    /// СМЕРТЬ СНИМАЕТСЯ ПО ТОЙ ЖЕ ПРИЧИНЕ, но эффект там мелкий. Замер:
    /// Forest_Guardian@Mutant Dying везёт таз на 0.461 единицы (1.1 м), и труп
    /// съезжает с собственной тени. Показ смерти длится 0.46 с при скорости
    /// 0.67 — это около восьмой части клипа, — да ещё поверх идёт парабола
    /// выброса из ArenaView, так что глазами разница почти не видна. Снято
    /// всё равно: тело не должно ездить само, а выброс уже написан кодом, и
    /// авторский проезд просто добавлялся к нему вторым слагаемым.
    ///
    /// ОСТАЛЬНЫМ КЛИПАМ ПРОЕЗД НУЖЕН. Замер тех же файлов: у стойки, реакций
    /// и обоих ударов чистый проезд НУЛЕВОЙ, но путь таза у удара — 1.116
    /// единицы. Это выпад вперёд и возврат, то есть вес удара. Снимешь его —
    /// и моб будет бить, не сходя с места. Правило поэтому не «снимать всегда»,
    /// а «снимать там, где клип везёт тело ТУДА, КУДА ЕГО НЕ ЗВАЛ ТИК».
    ///
    /// Y и поворот запекаются везде: вертикальная раскачка — часть походки,
    /// а разворот тела решает Facing из симуляции.
    /// </summary>
    private void ConfigureMobClip(ModelImporter importer)
    {
        // Пересобираем из defaults, а не правим .meta: подменённый FBX с тем
        // же GUID сохраняет старую запись клипа, и диапазон кадров начинает
        // указывать в никуда. На этом уже обожглись с Pelag_Run_Tripo.
        ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0) return;

        string file = System.IO.Path.GetFileNameWithoutExtension(NormalPath);
        ModelImporterClipAnimation clip = defaults[0];

        // Имя клипа — суффикс после «@». Роли раскладываются по имени ФАЙЛА,
        // так что имя внутри файла ни на что не влияет, кроме читаемости
        // окна Animator; «mixamo.com» там не говорит ничего.
        clip.name = file.Substring(file.IndexOf('@') + 1);

        // В исходнике Корнеполза три маха. Оставляем первый: кисть проходит
        // перед телом на кадре 17, то есть через 9 кадров после начала 8.
        // Второй файл зеркальный; это та же Царапина с другой руки.
        if (file == "Forest_RootSwarm@AttackA" || file == "Forest_RootSwarm@AttackB")
        {
            clip.firstFrame = 8f;
            clip.lastFrame = 24f;
        }

        bool loop = RazlomMobAnimatorBuilder.IsLoopingClipFile(file);
        clip.loopTime = loop;

        clip.lockRootPositionXZ = !RazlomMobAnimatorBuilder.IsRootTravelClipFile(file);
        clip.keepOriginalPositionXZ = true;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;

        importer.clipAnimations = new[] { clip };
    }

    private void ConfigurePelagMixamoClips(ModelImporter importer)
    {
        ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0) return;

        ModelImporterClipAnimation source = defaults[0];
        string file = System.IO.Path.GetFileNameWithoutExtension(NormalPath);

        if (file.StartsWith("Pelag_AN_"))
        {
            importer.clipAnimations = new[] { Clip(source, file, source.firstFrame, source.lastFrame, file.EndsWith("Loop")) };
            return;
        }
        if (file == "Pelag_MX_SaberCombo")
        {
            // Два удара и recovery остаются одним тейком, но delivery теперь
            // пересобран в 30 fps. Граница 25 принадлежит обеим половинам:
            // поза стыка совпадает, а B сохраняет весь мягкий recovery до 74.
            //
            // ГРАНИЦУ 25 НЕ ДВИГАТЬ. 2 сентября её пробовали сдвинуть на 31,
            // чтобы выбросить кадры 25–30: там сабля делает полный оборот
            // вокруг персонажа за пять кадров, остриё идёт до 1.19 м за кадр
            // (36 м/с), и при 30 fps это ближе к телепорту, чем к росчерку.
            // Стало хуже: связка A→B держится именно на том, что последний
            // кадр A и первый кадр B — один и тот же кадр исходника. Сдвиг
            // разорвал стык, и вместо быстрого росчерка получился обрыв позы,
            // который видно куда сильнее. Прокрут — это плата за непрерывность,
            // и убирать его можно только перерисовкой клипов, а не нарезкой.
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

                // ТЕЛО ПРИВОДИТСЯ К ЕДИНИЦАМ КЛИПОВ, А НЕ К МЕТРАМ.
                //
                // Clips bind bone positions, not just rotations, so the body has
                // to share the skeleton scale the clips were authored on. That
                // scale is v5's: a bind pose 0.978 units tall, which WoleScale
                // 1.82 turns into the 1.78 m the artist specified.
                //
                // v6 comes out of Blender normalised to metres - 1.8 units - so
                // it needs 0.978 / 1.8. Left alone it would not merely look
                // wrong, it would pull the skeleton apart under animation.
                //
                // Меняешь тело — прогони «Разлом → Проверить Pelag v6 runtime»
                // и подгони этот множитель так, чтобы meshHeight стал 0.978.
                importer.globalScale = 0.5433f;
            }
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
            LockRoot(importer);
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
    /// Прибивает корень клипа на месте.
    ///
    /// Mixamo выгружает анимации со снятой галкой «In Place», и клип тащит
    /// персонажа за собой. В нашей игре положение тела решает тик симуляции:
    /// клип, двигающий персонажа сам, уводит картинку от симуляции — моб
    /// убегает по анимации и возвращается рывком, когда цикл замыкается.
    ///
    /// `ArenaView` на всякий случай ещё и гасит `applyRootMotion`, но это
    /// защита рантайма. Правильное место — импорт: тогда клип честно лежит
    /// на месте, кто бы его ни проигрывал, включая объект, брошенный в сцену
    /// руками для проверки.
    ///
    /// Те же шесть флагов стоят у клипов Пелага — см. `Clip()` выше.
    /// </summary>
    private static void LockRoot(ModelImporter importer)
    {
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].lockRootRotation = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].lockRootHeightY = true;
            clips[i].keepOriginalPositionY = true;
        }

        importer.clipAnimations = clips;
    }

    /// <summary>
    /// Применяет то же самое к уже импортированным клипам.
    ///
    /// Нужно отдельной командой, потому что постпроцессор специально не трогает
    /// настроенный ассет — иначе он затирал бы ручные правки в инспекторе. Для
    /// клипов, приехавших до этой правки, обойти охрану можно только так.
    /// </summary>
    [MenuItem("Разлом/Клипы мобов — прибить корень на месте")]
    public static void LockRootOnExistingClips()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model",
            new[] { "Assets/Resources/Characters" });
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Клипы Пелага уже прибиты своим рецептом, второй раз не трогаем.
            if (path.Contains("/Pelag_v5/")) continue;
            if (!System.IO.Path.GetFileNameWithoutExtension(path).Contains('@')) continue;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            LockRoot(importer);
            importer.SaveAndReimport();
            fixedCount++;
            Debug.Log($"[Разлом] Корень прибит: {System.IO.Path.GetFileName(path)}");
        }

        Debug.Log($"[Разлом] Клипов приведено к in-place: {fixedCount}");
    }

    /// <summary>
    /// Кладёт в тангенсы нормаль, усреднённую ПО ПОЛОЖЕНИЮ вершины.
    ///
    /// Обводка рисуется вывернутой оболочкой: меш растягивается вдоль нормали
    /// и рисуется задними гранями. На стыках, где нормали разъехались — жёсткие
    /// рёбра, швы развёртки, кромки листвы, — оболочка расходится, и красное
    /// лезет ВНУТРЬ силуэта. Именно это владелец и увидел на Лесном страже:
    /// «много лишних мелких моментов выделяет, а не только силуэт».
    ///
    /// Чинится не сглаживанием нормалей модели — это поменяло бы саму заливку, —
    /// а второй нормалью, сшитой по положению. Тангенсы для этого свободны:
    /// `RazlomTextureToon` не читает ни их, ни карту нормалей, проверено поиском.
    /// Обводочный проход берёт направление отсюда, освещение — из обычной
    /// нормали, и заливка не меняется ни на пиксель.
    /// </summary>
    private void OnPostprocessMesh(Mesh mesh)
    {
        if (!IsCharacter || IsAnimationOnly) return;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (vertices.Length == 0 || normals.Length != vertices.Length) return;

        var sums = new System.Collections.Generic.Dictionary<Vector3, Vector3>(vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 key = Quantize(vertices[i]);
            sums.TryGetValue(key, out Vector3 sum);
            sums[key] = sum + normals[i];
        }

        var tangents = new Vector4[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 smooth = sums[Quantize(vertices[i])];
            // Вырожденный случай — противоположные нормали в одной точке гасят
            // друг друга. Тогда честнее оставить исходную, чем нулевой вектор.
            smooth = smooth.sqrMagnitude > 1e-8f ? smooth.normalized : normals[i];
            tangents[i] = new Vector4(smooth.x, smooth.y, smooth.z, 1f);
        }

        mesh.tangents = tangents;
    }

    /// <summary>
    /// Округление координаты до сотых долей миллиметра: вершины шва совпадают
    /// по положению не побитово, а с точностью экспорта.
    /// </summary>
    private static Vector3 Quantize(Vector3 value) => new Vector3(
        Mathf.Round(value.x * 10000f), Mathf.Round(value.y * 10000f),
        Mathf.Round(value.z * 10000f));

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
        if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0f);
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
