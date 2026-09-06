using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ставит шейдер ветра на выделенную растительность.
///
/// ЗАЧЕМ ТУЛ, А НЕ РУЧНАЯ НАСТРОЙКА. Маска качания живёт в ОБЪЕКТНЫХ Y меша:
/// шейдеру надо знать, где у этой конкретной модели корни, а где крона. Числа
/// разные у каждой модели, на глаз не угадываются, а угаданные неверно дают
/// либо неподвижное дерево, либо ствол, ходящий вместе с листвой. Меряем по
/// мешу — ровно так же, как рост персонажей берётся из валидатора, а не из
/// головы.
/// </summary>
public static class RazlomFoliageSetup
{
    private const string ShaderName = "Razlom/Foliage Wind";
    private const string SetupMenu = "Разлом/Листва/Настроить ветер на выделенном";
    private const string ZoneMenu = "Разлом/Листва/Поставить зону ветра в сцену";

    /// <summary>
    /// Какая доля высоты меша снизу не качается вовсе.
    ///
    /// Чуть больше четверти. Меньше — и основание ствола начинает елозить по
    /// земле; заметно больше — и качание сжимается в верхушку, а дерево
    /// выглядит так, будто у него шевелится только шапка.
    /// </summary>
    private const float TrunkHold = 0.28f;

    /// <summary>
    /// Размер листовой шапки как доля от ширины кроны.
    ///
    /// Вершины внутри клетки этого размера трепещут в одной фазе, соседние
    /// клетки — в несвязанных. Мельче — фазы расходятся внутри одного листа и
    /// его рвёт; крупнее — крона снова ходит одной плитой. Двенадцать процентов
    /// ширины дают у обычного дерева примерно размер листовой шапки.
    /// </summary>
    private const float ClusterShare = 0.12f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int WindHeightRangeId = Shader.PropertyToID("_WindHeightRange");
    private static readonly int WindClusterSizeId = Shader.PropertyToID("_WindClusterSize");

    [MenuItem(SetupMenu, priority = 30)]
    private static void Setup()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[Разлом] Шейдер «{ShaderName}» не найден. " +
                "Проверь, что Resources/Shaders/RazlomFoliageWind.shader " +
                "импортирован и скомпилировался без ошибок.");
            return;
        }

        var renderers = new List<MeshRenderer>();
        foreach (GameObject selected in Selection.gameObjects)
            renderers.AddRange(selected.GetComponentsInChildren<MeshRenderer>(true));

        if (renderers.Count == 0)
        {
            Debug.LogWarning("[Разлом] В выделении нет ни одного MeshRenderer. " +
                "Выдели дерево в Hierarchy и повтори.");
            return;
        }

        var report = new StringBuilder();
        int changed = 0;

        foreach (MeshRenderer renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                report.AppendLine($"  {renderer.name}: пропущен, нет меша.");
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(assetPath))
            {
                report.AppendLine($"  {renderer.name}: пропущен, меш не лежит в проекте.");
                continue;
            }

            // Границы меша — в его собственном пространстве, ровно в тех же
            // координатах, что шейдер видит в positionOS. Масштаб трансформа на
            // них не влияет, и это правильно: маска обязана быть свойством
            // модели, а не того, как её растянули в сцене.
            Bounds bounds = mesh.bounds;
            float low = bounds.min.y + bounds.size.y * TrunkHold;
            float high = bounds.max.y;

            // zw — ось, вокруг которой дерево кланяется. Берём центр меша по
            // горизонтали, а не нули: у моделей, где начало координат сдвинуто
            // от ствола, поворот вокруг нуля унёс бы крону вбок дугой, и это
            // читалось бы как подпрыгивание, а не как наклон.
            var heightRange = new Vector4(low, high, bounds.center.x, bounds.center.z);

            float canopyWidth = Mathf.Max(bounds.size.x, bounds.size.z);
            float clusterSize = Mathf.Clamp(canopyWidth * ClusterShare, 0.05f, 3f);

            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string modelName = Path.GetFileNameWithoutExtension(assetPath);

            Material[] sources = renderer.sharedMaterials;
            var result = new Material[sources.Length];

            for (int slot = 0; slot < sources.Length; slot++)
            {
                Material source = sources[slot];
                string baseName = source != null ? source.name : $"{modelName}_{slot}";

                // Уже наш материал — не пересоздаём, только обновляем замер.
                // Всё остальное могло быть подкручено руками, и затирать это
                // повторным запуском тула нельзя.
                if (source != null && source.shader == shader)
                {
                    Undo.RecordObject(source, "Обновить замеры листвы");
                    source.SetVector(WindHeightRangeId, heightRange);
                    source.SetFloat(WindClusterSizeId, clusterSize);
                    EditorUtility.SetDirty(source);
                    result[slot] = source;
                    report.AppendLine($"  {renderer.name} [{slot}] {source.name}: " +
                        $"обновлён замер — качание {low:0.###} … {high:0.###}, " +
                        $"шапка {clusterSize:0.###}");
                    continue;
                }

                string materialPath = $"{folder}/{baseName}_Foliage.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                bool created = false;

                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                    created = true;
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                }

                Texture baseTexture = ReadBaseTexture(source);
                if (baseTexture != null) material.SetTexture(BaseMapId, baseTexture);
                material.SetVector(WindHeightRangeId, heightRange);
                material.SetFloat(WindClusterSizeId, clusterSize);
                EditorUtility.SetDirty(material);

                result[slot] = material;
                report.AppendLine($"  {renderer.name} [{slot}] → {baseName}_Foliage.mat " +
                    $"({(created ? "создан" : "переиспользован")}), " +
                    $"качание {low:0.###} … {high:0.###}, " +
                    $"шапка {clusterSize:0.###}, " +
                    $"текстура {(baseTexture != null ? baseTexture.name : "НЕ НАЙДЕНА")}");
            }

            Undo.RecordObject(renderer, "Материал листвы");
            renderer.sharedMaterials = result;
            EditorUtility.SetDirty(renderer);
            changed++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Разлом] Листва: обработано рендереров — {changed}.\n{report}" +
            "\nЕсли на краю экрана дерево начнёт пропадать целиком — это отсечение " +
            "по границам меша, которые про вершинный сдвиг не знают. Лечится " +
            "увеличением bounds на амплитуду качания.");
    }

    [MenuItem(SetupMenu, true)]
    private static bool CanSetup() => Selection.gameObjects.Length > 0;

    [MenuItem(ZoneMenu, priority = 31)]
    private static void CreateZone()
    {
        FoliageWindZone existing = Object.FindFirstObjectByType<FoliageWindZone>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[Разлом] Зона ветра в сцене уже есть — выделил её.");
            return;
        }

        var go = new GameObject("Foliage Wind");
        go.AddComponent<FoliageWindZone>();
        Undo.RegisterCreatedObjectUndo(go, "Зона ветра");
        Selection.activeGameObject = go;
        Debug.Log("[Разлом] Зона ветра создана. Направление и сила — в инспекторе.");
    }

    /// <summary>
    /// Достаёт базовую карту из исходного материала. Материал приезжает вшитым
    /// в FBX, и по какому имени там лежит текстура, зависит от того, каким
    /// шейдером его импортировало — поэтому три попытки, а не одна.
    /// </summary>
    private static Texture ReadBaseTexture(Material source)
    {
        if (source == null) return null;
        if (source.HasProperty(BaseMapId))
        {
            Texture texture = source.GetTexture(BaseMapId);
            if (texture != null) return texture;
        }
        if (source.HasProperty(MainTexId))
        {
            Texture texture = source.GetTexture(MainTexId);
            if (texture != null) return texture;
        }
        return source.mainTexture;
    }
}
