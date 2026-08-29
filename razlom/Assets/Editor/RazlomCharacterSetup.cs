using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Полная настройка персонажа одним действием.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ КНОПКА. Настройки импорта применяет AssetPostprocessor,
/// а он срабатывает ТОЛЬКО в момент импорта. Ассет, лежавший в проекте до
/// появления постпроцессора, так и остаётся с прежними настройками: модель
/// приходит Generic-ригом и с материалом под встроенный шейдер — то есть
/// белая и без анимаций, сколько ни перезапускай игру.
///
/// Поэтому здесь принудительный переимпорт, а следом сборка материала
/// и контроллера. Порядок важен: и материал, и контроллер ссылаются на то,
/// что появляется только после переимпорта.
/// </summary>
public static class RazlomCharacterSetup
{
    private const string ModelPath = "Assets/Resources/Characters/Pelag_Rodin01/Pelag_Rodin01.fbx";
    private const string ClipsFolder = "Assets/Art/Characters/Pelag_Rodin01/Animations";
    private const string ControllerPath =
        "Assets/Resources/Characters/Pelag_Rodin01/Pelag_Rodin01.controller";
    private const string MaterialPath =
        "Assets/Resources/Characters/Pelag_Rodin01/Pelag_Rodin01.mat";

    /// <summary>
    /// Текстура ОТДЕЛЬНЫМ файлом, а не вшитая в FBX.
    ///
    /// Вшитую Unity кладёт подчинённым ассетом модели, и она пересоздаётся
    /// при каждом переимпорте: материал теряет ссылку, персонаж становится
    /// белым, и выглядит это как «цвет опять слетел». Обычный PNG в проекте
    /// не пересоздаётся никогда.
    /// </summary>
    private const string TexturePath =
        "Assets/Art/Characters/Pelag_Rodin01/Pelag_Rodin01_BaseColor.png";

    [MenuItem("Разлом/Настроить пелага — полный проход", priority = 0)]
    public static void SetupPelag()
    {
        Debug.Log("[Разлом] Настройка пелага: начали.");

        if (!File.Exists(ModelPath))
        {
            Debug.LogError("[Разлом] Модель не найдена: " + ModelPath);
            return;
        }

        // 1. Модель. ForceUpdate обязателен: без него Unity считает ассет
        // неизменившимся и пропускает импорт вместе с нашими настройками.
        Debug.Log("[Разлом] Переимпорт модели...");
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

        // 2. Анимации.
        if (AssetDatabase.IsValidFolder(ClipsFolder))
        {
            string[] clips = Directory.GetFiles(ClipsFolder, "*.fbx", SearchOption.TopDirectoryOnly);
            Debug.Log("[Разлом] Переимпорт анимаций: " + clips.Length + " шт. Файлы тяжёлые, это долго.");
            foreach (string clip in clips)
                AssetDatabase.ImportAsset(clip.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        }
        else
        {
            Debug.LogWarning("[Разлом] Папки с анимациями нет: " + ClipsFolder);
        }

        AssetDatabase.Refresh();

        // 3. Материал отдельным ассетом.
        BuildMaterial();

        // 4. Контроллер поверх уже импортированных клипов.
        RazlomAnimatorBuilder.Build();

        Report();
    }

    /// <summary>
    /// Собирает материал персонажа ОТДЕЛЬНЫМ ассетом.
    ///
    /// Материал внутри FBX править бесполезно: он пересоздаётся при каждом
    /// переимпорте, и правка шейдера живёт до первого же обновления модели.
    /// Свой .mat переживает всё и лежит в Resources рядом с моделью, откуда
    /// его и берёт отрисовка.
    ///
    /// Текстура берётся из самой модели: она вшита в FBX и лежит там
    /// подчинённым ассетом.
    /// </summary>
    private static void BuildMaterial()
    {
        // Сначала отдельный файл — он надёжнее. Вшитая в модель текстура
        // остаётся запасным путём на случай, если PNG забыли положить.
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture != null)
        {
            Debug.Log("[Разлом] Текстура взята из файла: " + TexturePath);
        }
        else
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                var t = o as Texture2D;
                if (t != null && !t.name.StartsWith("__"))
                {
                    texture = t;
                    Debug.Log("[Разлом] Текстура взята из модели: " + t.name);
                    break;
                }
            }
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[Разлом] Не найден шейдер URP/Lit. Проект точно на URP?");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        if (texture != null)
        {
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
        }
        else
        {
            Debug.LogError("[Разлом] Текстуры нет ни отдельным файлом, ни в модели — " +
                           "персонаж останется белым. Ждали: " + TexturePath);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

        // Мультяшной фигуре блик по всей поверхности мешает: он забивает
        // силуэт, а силуэт здесь главный канал распознавания.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log("[Разлом] Материал собран: " + MaterialPath);
    }

    /// <summary>
    /// Всё ли на месте. Проверяется НЕ ТОЛЬКО контроллер: модель могли
    /// заменить новым файлом, и тогда материал ссылается на текстуру,
    /// которой больше нет — персонаж снова белый, хотя контроллер цел.
    /// </summary>
    private static bool IsReady()
    {
        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) == null) return false;

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null || mat.mainTexture == null) return false;

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null || importer.animationType != ModelImporterAnimationType.Human) return false;

        return true;
    }

    /// <summary>Что в итоге получилось — чтобы не гадать, сработало или нет.</summary>
    private static void Report()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        string rig = importer != null ? importer.animationType.ToString() : "импортер не найден";

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        string matInfo = mat == null
            ? "НЕ СОБРАН"
            : mat.shader.name + (mat.mainTexture != null ? " + текстура" : " БЕЗ текстуры");

        string lines =
            "[Разлом] ИТОГ настройки пелага:" +
            "\n  риг: " + rig + "  (нужен Human)" +
            "\n  контроллер: " + (controller != null ? "собран" : "НЕ СОБРАН") +
            "\n  материал: " + matInfo +
            "\n  дальше: выйди из Play и запусти заново.";

        Debug.Log(lines);
    }

    private const string TriedKey = "Razlom.PelagSetupTried";

    /// <summary>
    /// Настраивает персонажа САМ, если он ещё не настроен.
    ///
    /// Полагаться на то, что человек не забудет нажать пункт меню, нельзя:
    /// забыть — это норма, а результат забывания выглядит как «ничего
    /// не изменилось», и на поиск причины уходит вечер.
    ///
    /// Запуск один раз за сессию редактора и только при отсутствии
    /// контроллера: полный проход тяжёлый, и гонять его на каждой
    /// перекомпиляции значит превратить работу в ожидание. Флаг живёт
    /// в SessionState, поэтому перезапуск редактора даёт новую попытку.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(ModelPath)) return;
            if (IsReady()) return;

            if (SessionState.GetBool(TriedKey, false))
            {
                Debug.LogWarning("[Разлом] Пелаг всё ещё без контроллера. " +
                                 "Меню «Разлом → Настроить пелага — полный проход».");
                return;
            }
            SessionState.SetBool(TriedKey, true);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Разлом] Пелаг не настроен, но идёт Play. " +
                                 "Выйди из Play — настройка запустится сама.");
                return;
            }

            Debug.Log("[Разлом] Пелаг не настроен — запускаю полный проход сам.");
            SetupPelag();
        };
    }
}
