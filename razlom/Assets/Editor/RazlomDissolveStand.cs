using System.Reflection;
using UnityEditor;
using UnityEngine;
using Game.View;

/// <summary>
/// СТЕНД РАСТВОРЕНИЯ — ручки эффекта смерти и живое превью прямо на теле моба.
///
/// ЗАЧЕМ ОТДЕЛЬНОЕ ОКНО, А НЕ ПРОСТО ИНСПЕКТОР МАТЕРИАЛА.
///
/// Ручки растворения живут на МАТЕРИАЛЕ, и их видно в его инспекторе. Но
/// увидеть там можно только шарик предпросмотра, а эффект настраивается под
/// конкретное тело: у Лесного стража шерсть, рога и юбка — разной плотности
/// куски, и один и тот же шум читается на них по-разному. Крутить вслепую и
/// проверять запуском игры — это цикл в минуту на одно движение слайдера.
///
/// Стенд ставит настоящее тело в открытую сцену, в позе последнего кадра
/// смерти (именно поверх неё эффект и проигрывается в бою), и даёт скрести
/// растворение слайдером. Что покручено — то и в игре: ручки пишутся в тот же
/// .mat, который грузит ArenaView.
///
/// ОДНО ИСКЛЮЧЕНИЕ, И О НЁМ ЧЕСТНО СКАЗАНО В ОКНЕ. Само значение _DeathFade
/// в бою задаёт ArenaView через MaterialPropertyBlock — покадрово, от 0 до 1.
/// Ползунок на материале игра проигнорирует. Поэтому здесь он и стоит
/// отдельно, подписанный как «превью»: это не настройка, а протяжка времени.
/// </summary>
public sealed class RazlomDissolveStand : EditorWindow
{
    private const string MaterialPath =
        "Assets/Resources/Characters/Forest_Guardian/Forest_Guardian_Material.mat";
    private const string ModelPath =
        "Assets/Resources/Characters/Forest_Guardian/Forest_Guardian.fbx";
    private const string DeathClipPath =
        "Assets/Resources/Characters/Forest_Guardian/Forest_Guardian@Mutant Dying.fbx";

    private const string StandName = "СТЕНД РАСТВОРЕНИЯ (не сохраняется)";

    private Material _material;
    private MaterialEditor _materialEditor;
    private GameObject _stand;
    private MaterialPropertyBlock _block;

    private float _fade;
    private bool _playing;
    private double _playStartedAt;
    private Vector2 _scroll;

    [MenuItem("Разлом/Стенд растворения", priority = 20)]
    private static void Open()
    {
        GetWindow<RazlomDissolveStand>("Растворение").minSize = new Vector2(360f, 420f);
    }

    private void OnEnable()
    {
        _material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        _block = new MaterialPropertyBlock();
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        RemoveStand();
        if (_materialEditor != null) DestroyImmediate(_materialEditor);
    }

    /// <summary>
    /// Длительность осыпания берётся ИЗ ИГРЫ, а не переписывается сюда
    /// числом. Константа приватная, поэтому читается рефлексией: копия
    /// разъехалась бы с боем при первой же правке, и стенд начал бы врать
    /// именно в том, ради чего он существует.
    /// </summary>
    private static float GameFadeDuration()
    {
        FieldInfo field = typeof(ArenaView).GetField(
            "OrvillDeathFadeDuration",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null && field.IsLiteral)
            return (float)field.GetRawConstantValue();
        return 0.5f;
    }

    private void Tick()
    {
        if (!_playing) return;

        float duration = Mathf.Max(0.01f, GameFadeDuration());
        _fade = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - _playStartedAt) / duration);
        if (_fade >= 1f) _playing = false;

        ApplyFade();
        Repaint();
    }

    private void OnGUI()
    {
        if (_material == null)
        {
            EditorGUILayout.HelpBox(
                "Материал не найден: " + MaterialPath,
                MessageType.Error);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawStandSection();
        EditorGUILayout.Space(8f);
        DrawScrubSection();
        EditorGUILayout.Space(8f);
        DrawKnobsSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawStandSection()
    {
        EditorGUILayout.LabelField("Тело", EditorStyles.boldLabel);

        if (_stand == null)
        {
            EditorGUILayout.HelpBox(
                "Поставь стенд — в открытую сцену ляжет Лесной страж в позе " +
                "последнего кадра смерти. Объект помечен «не сохранять»: в " +
                "сцену он не попадёт и уберётся сам, когда закроешь окно.",
                MessageType.Info);
            if (GUILayout.Button("Поставить стенд", GUILayout.Height(28f)))
                BuildStand();
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Навести камеру")) FrameStand();
            if (GUILayout.Button("Убрать стенд")) RemoveStand();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawScrubSection()
    {
        EditorGUILayout.LabelField("Протяжка (превью)", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _fade = EditorGUILayout.Slider("Растворение", _fade, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            _playing = false;
            ApplyFade();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button($"Проиграть ({GameFadeDuration():0.00} с — как в бою)"))
        {
            if (_stand == null) BuildStand();
            _playing = true;
            _playStartedAt = EditorApplication.timeSinceStartup;
        }
        if (GUILayout.Button("Сброс", GUILayout.Width(70f)))
        {
            _playing = false;
            _fade = 0f;
            ApplyFade();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Это не настройка, а перемотка времени. В бою значение ведёт " +
            "ArenaView покадрово, ползунок на материале игра не читает.",
            MessageType.None);
    }

    private void DrawKnobsSection()
    {
        EditorGUILayout.LabelField("Ручки эффекта (пишутся в материал)", EditorStyles.boldLabel);

        if (_materialEditor == null || _materialEditor.target != _material)
        {
            if (_materialEditor != null) DestroyImmediate(_materialEditor);
            _materialEditor = (MaterialEditor)Editor.CreateEditor(_material);
        }

        // Рисуются только ручки растворения, а не весь материал: остальные
        // свойства к эффекту отношения не имеют и мешали бы искать нужное.
        // Список берётся из ШЕЙДЕРА, а не переписан сюда руками — появится
        // новая ручка, окно покажет её само.
        MaterialProperty[] properties =
            MaterialEditor.GetMaterialProperties(new Object[] { _material });

        bool drewAny = false;
        foreach (MaterialProperty property in properties)
        {
            if (!property.name.StartsWith("_Dissolve")) continue;
            _materialEditor.ShaderProperty(property, property.displayName);
            drewAny = true;
        }

        if (!drewAny)
        {
            EditorGUILayout.HelpBox(
                "У материала нет свойств растворения. Проверь, что на нём " +
                "стоит шейдер Razlom/Texture Toon.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Сохранить в проект"))
        {
            EditorUtility.SetDirty(_material);
            AssetDatabase.SaveAssets();
        }
        if (GUILayout.Button("Показать материал", GUILayout.Width(140f)))
        {
            Selection.activeObject = _material;
            EditorGUIUtility.PingObject(_material);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void BuildStand()
    {
        RemoveStand();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null)
        {
            Debug.LogError($"[Разлом] Модель не найдена: {ModelPath}");
            return;
        }

        _stand = Instantiate(prefab);
        _stand.name = StandName;

        // Масштаб берётся из игры по той же причине, что и длительность:
        // на теле другого размера тот же шум читается другой крупностью.
        ArenaView arena = FindFirstObjectByType<ArenaView>();
        float scale = arena != null ? arena.OrvillScale : 2.4f;
        _stand.transform.localScale = Vector3.one * scale;

        SceneView view = SceneView.lastActiveSceneView;
        _stand.transform.position = view != null ? view.pivot : Vector3.zero;

        // Флаг ставится на всю ветку: он не наследуется, и без обхода
        // дети попали бы в сохранённую сцену без корня.
        foreach (Transform node in _stand.GetComponentsInChildren<Transform>(true))
            node.gameObject.hideFlags = HideFlags.DontSave;

        foreach (Renderer renderer in _stand.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) slots[i] = _material;
            renderer.sharedMaterials = slots;
        }

        PoseAsDead();
        ApplyFade();
        FrameStand();
    }

    /// <summary>
    /// Ставит тело в последний кадр смерти. Эффект в бою проигрывается
    /// именно поверх этой позы — на T-позе шум ложится не так, и настройка
    /// вышла бы верной для кадра, которого в игре не бывает.
    /// </summary>
    private void PoseAsDead()
    {
        AnimationClip clip = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(DeathClipPath))
        {
            if (asset is AnimationClip candidate && !candidate.name.StartsWith("__preview__"))
            {
                clip = candidate;
                break;
            }
        }

        if (clip == null) return;

        // Аниматор на время выключается: работающий контроллер перебивает
        // ручную выборку своим состоянием по умолчанию.
        Animator animator = _stand.GetComponent<Animator>();
        bool wasEnabled = animator != null && animator.enabled;
        if (animator != null) animator.enabled = false;

        clip.SampleAnimation(_stand, clip.length);

        if (animator != null) animator.enabled = wasEnabled;
    }

    private void ApplyFade()
    {
        if (_stand == null) return;

        // Значение кладётся ЧЕРЕЗ БЛОК, а не в материал: ровно тем же
        // каналом, которым его ведёт ArenaView в бою. Пиши стенд прямо в
        // .mat — он бы менял вид всех мобов разом и оставлял их полураст-
        // воренными после закрытия окна.
        foreach (Renderer renderer in _stand.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(_block);
            _block.SetFloat("_DeathFade", _fade);
            renderer.SetPropertyBlock(_block);
        }

        SceneView.RepaintAll();
    }

    private void FrameStand()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || _stand == null) return;

        Renderer renderer = _stand.GetComponentInChildren<Renderer>();
        if (renderer != null) view.Frame(renderer.bounds, false);
    }

    private void RemoveStand()
    {
        if (_stand == null) return;
        DestroyImmediate(_stand);
        _stand = null;
    }
}
