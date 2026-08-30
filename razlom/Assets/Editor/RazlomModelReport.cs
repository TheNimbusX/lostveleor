using System.Text;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Отчёт по импортированной модели: рост, части, материалы, риг.
    ///
    /// Нужен потому, что «персонаж маленький» и «у персонажа лишняя плоскость» —
    /// это симптомы, а чинить надо причину. Гадать по скриншоту, какой из
    /// тридцати кусков меша даёт красный прямоугольник, дороже, чем один раз
    /// прочитать иерархию.
    /// </summary>
    public static class RazlomModelReport
    {
        private const string TargetFlag = "-razlom-report-model";

        private const string CharactersFolder = "Assets/Resources/Characters";

        [MenuItem("Разлом/Отчёт по модели пелага")]
        public static void ReportPelag()
        {
            Report("Assets/Resources/Characters/Pelag_v4/Pelag_v4.fbx");
        }

        /// <summary>
        /// Принудительный переимпорт всех моделей персонажей.
        ///
        /// Настройки и починку материалов делает AssetPostprocessor, а он
        /// срабатывает ТОЛЬКО в момент импорта. Ассет, уже лежащий в проекте,
        /// правку постпроцессора не заметит — и это выглядит как «код изменил,
        /// а ничего не поменялось».
        /// </summary>
        [MenuItem("Разлом/Переимпортировать модели персонажей")]
        public static void ReimportCharacters()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Разлом] Переимпортировано моделей: {guids.Length}.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Report()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int index = System.Array.IndexOf(args, TargetFlag);
            string path = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : "Assets/Resources/Characters/Pelag_v4/Pelag_v4.fbx";

            Report(path);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Печатает фактические свойства импортированного клипа для batch-проверки.</summary>
        public static void ReportWhirlwindClip()
        {
            const string path = "Assets/Resources/Characters/Pelag_v4/Animations/Pelag_Whirlwind.fbx";
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__"))
                .ToArray();
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Debug.Log($"[whirlwind-import] clips={clips.Length}, rig={importer?.animationType}, " +
                      $"avatarSetup={importer?.avatarSetup}, sourceAvatar={importer?.sourceAvatar}");
            foreach (AnimationClip clip in clips)
                Debug.Log($"[whirlwind-import] name={clip.name}, length={clip.length:0.000}, " +
                          $"fps={clip.frameRate:0.0}, human={clip.isHumanMotion}, legacy={clip.legacy}, " +
                          $"loop={clip.isLooping}, empty={clip.empty}");
            if (Application.isBatchMode) EditorApplication.Exit(clips.Length == 1 ? 0 : 2);
        }

        public static void ReportTripoRunClip()
        {
            const string path = "Assets/Resources/Characters/Pelag_v5/Animations/Pelag_Run_Tripo.fbx";
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__"))
                .ToArray();
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            ModelImporterClipAnimation[] defaults = importer?.defaultClipAnimations;
            ModelImporterClipAnimation[] configured = importer?.clipAnimations;
            Debug.Log($"[tripo-run] defaults={defaults?.Length ?? 0}, configured={configured?.Length ?? 0}, " +
                      $"rig={importer?.animationType}, avatarSetup={importer?.avatarSetup}, " +
                      $"sourceAvatar={importer?.sourceAvatar}");
            if (defaults != null)
                foreach (ModelImporterClipAnimation take in defaults)
                    Debug.Log($"[tripo-run] default name={take.name}, take={take.takeName}, " +
                              $"frames={take.firstFrame:0.##}-{take.lastFrame:0.##}");
            foreach (AnimationClip clip in clips)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string clipGuid, out long localId);
                EditorCurveBinding[] curves = AnimationUtility.GetCurveBindings(clip);
                Debug.Log($"[tripo-run] clip={clip.name}, length={clip.length:0.000}, " +
                          $"fps={clip.frameRate:0.0}, human={clip.isHumanMotion}, empty={clip.empty}, " +
                          $"curves={curves.Length}, guid={clipGuid}, localId={localId}");
                foreach (EditorCurveBinding curve in curves.Take(12))
                    Debug.Log($"[tripo-run] curve path={curve.path}, property={curve.propertyName}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(clips.Length == 1 ? 0 : 2);
        }

        /// <summary>
        /// Humanoid по умолчанию выносит поворот Hips в root motion. Для Вихря
        /// этот поворот — сама поза: bake rotation возвращает полный оборот в
        /// скелет, при этом Transform сущности остаётся под контролем Game.Sim.
        /// </summary>
        public static void ConfigureWhirlwindClip()
        {
            const string path = "Assets/Resources/Characters/Pelag_v4/Animations/Pelag_Whirlwind.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[whirlwind-import] importer не найден: {path}");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length != 1)
            {
                Debug.LogError($"[whirlwind-import] ожидался один take, найдено {clips?.Length ?? 0}");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            clips[0].name = "Pelag_Whirlwind";
            clips[0].loopTime = false;
            clips[0].lockRootRotation = true;
            clips[0].keepOriginalOrientation = true;
            clips[0].lockRootPositionXZ = true;
            clips[0].keepOriginalPositionXZ = true;
            clips[0].lockRootHeightY = true;
            clips[0].keepOriginalPositionY = true;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            Debug.Log("[whirlwind-import] rotation baked into pose; root translation locked.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Report(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogError($"[отчёт] Модель не загрузилась: {assetPath}");
                return;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            var text = new StringBuilder();
            text.AppendLine($"[отчёт] {assetPath}");
            text.AppendLine($"  риг: {(importer != null ? importer.animationType.ToString() : "?")}");

            var animator = prefab.GetComponent<Animator>();
            text.AppendLine($"  аватар: {(animator != null && animator.avatar != null ? animator.avatar.name : "нет")}" +
                            $", гуманоид: {(animator != null && animator.avatar != null && animator.avatar.isHuman)}" +
                            $", валиден: {(animator != null && animator.avatar != null && animator.avatar.isValid)}");

            // Габариты считаются по мешам, а не по Renderer.bounds: у префаба
            // на диске рендереры не «прогреты» сценой и дают нулевые границы.
            Bounds total = default;
            bool first = true;
            int triangles = 0;

            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                triangles += mesh.triangles.Length / 3;

                Bounds bounds = mesh.bounds;
                if (first) { total = bounds; first = false; }
                else total.Encapsulate(bounds);

                text.AppendLine($"    часть «{renderer.name}» " +
                                $"тр {mesh.triangles.Length / 3}, " +
                                $"габарит {bounds.size}, " +
                                $"{Describe(renderer.sharedMaterials)}");
            }

            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                triangles += mesh.triangles.Length / 3;

                Bounds bounds = mesh.bounds;
                if (first) { total = bounds; first = false; }
                else total.Encapsulate(bounds);

                text.AppendLine($"    ЖЁСТКАЯ часть «{renderer.name}» " +
                                $"тр {mesh.triangles.Length / 3}, выс {bounds.size.y:0.000}");
            }

            text.AppendLine($"  ИТОГО треугольников: {triangles}");
            text.AppendLine($"  ИТОГО габарит: {total.size}, центр {total.center}");
            text.AppendLine($"  множитель до роста 1.78 м: " +
                            $"{(total.size.y > 0.0001f ? 1.78f / total.size.y : 0f):0.000}");

            Debug.Log(text.ToString());
        }

        /// <summary>
        /// Материал без текстуры выглядит как заливка цветом — именно так
        /// в кадре появляются «лишние цветные прямоугольники». Поэтому в отчёт
        /// идёт и шейдер, и то, лежит ли в базовом слоте картинка.
        /// </summary>
        private static string Describe(Material[] materials)
        {
            var text = new StringBuilder();
            foreach (Material material in materials)
            {
                if (material == null) { text.Append("[материала нет] "); continue; }

                Texture map = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                text.Append($"[{material.name} / {material.shader.name} / " +
                            $"текстура {(map != null ? map.name : "НЕТ")} / " +
                            $"цвет {(material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").ToString() : "?")}] ");
            }

            return text.ToString();
        }
    }
}
