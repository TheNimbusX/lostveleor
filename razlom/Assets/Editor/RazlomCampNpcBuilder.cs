using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Собирает аниматор мирного NPC лагеря и вешает его на модель в сцене.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНО ОТ МОБОВ. У моба граф из семи состояний с триггерами на
    /// удары и смерть; у торговца одно состояние, которое крутится вечно.
    /// Гонять NPC через боевой сборщик значило бы заводить ему параметры
    /// «получил в морду» и «умер».
    ///
    /// РАСКЛАДКА ФАЙЛОВ — та же конвенция Unity и Mixamo, что у мобов:
    ///
    ///     Resources/Characters/NPC/&lt;имя&gt;/
    ///         &lt;имя&gt;.fbx              тело
    ///         &lt;имя&gt;@Talking.fbx      что он делает целыми днями
    ///
    /// Берётся клип с ролью Idle, а если такого нет — первый по алфавиту: у
    /// мирного жителя один цикл, и выбирать не из чего.
    ///
    /// ЗАЦИКЛИВАНИЕМ ЭТОТ ИНСТРУМЕНТ НЕ РАСПОРЯЖАЕТСЯ — им владеет разбор
    /// импорта. Здесь оно только проверяется.
    /// </summary>
    public static class RazlomCampNpcBuilder
    {
        private const string NpcFolder = "Assets/Resources/Characters/NPC";
        private const string StateName = "Life";

        private static readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        private static void Log(string line) => _log.AppendLine(line);

        [MenuItem("Разлом/Лагерь/Собрать NPC лагеря")]
        public static void Build()
        {
            _log.Clear();

            if (!AssetDatabase.IsValidFolder(NpcFolder))
            {
                EditorUtility.DisplayDialog("Разлом", $"Папки {NpcFolder} нет.", "Понятно");
                return;
            }

            var built = new List<string>();
            var assigned = new List<string>();
            var skipped = new List<string>();

            foreach (string folder in AssetDatabase.GetSubFolders(NpcFolder))
            {
                string name = Path.GetFileName(folder);
                Log($"NPC «{name}» ({folder}):");

                AnimatorController controller = BuildOne(folder, name, skipped);
                if (controller == null) { Log("  контроллер не собран"); continue; }

                built.Add(name);
                assigned.AddRange(AssignInScene(folder, name, controller));
            }

            AssetDatabase.SaveAssets();
            DumpScene();
            string report = WriteReport();
            Report(built, assigned, skipped, report);
        }

        /// <summary>Кладёт отчёт рядом с остальными артефактами сборки.</summary>
        private static string WriteReport()
        {
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            string directory = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "camp-npc-report.txt");
            File.WriteAllText(path, _log.ToString());
            Debug.Log($"[Разлом] Отчёт по NPC: {path}");
            return path;
        }

        private static AnimatorController BuildOne(
            string folder, string name, List<string> skipped)
        {
            string bodyPath = $"{folder}/{name}.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath) == null)
            {
                skipped.Add($"{name}: нет тела {name}.fbx");
                return null;
            }

            AnimationClip clip = ResolveClip(folder, name, skipped);
            if (clip == null) return null;

            string output = $"{folder}/{name}_Life.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(output) != null)
                AssetDatabase.DeleteAsset(output);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(output);
            AnimatorState state = controller.layers[0].stateMachine.AddState(StateName);
            state.motion = clip;
            // Единственное состояние обязано быть стартовым: иначе аниматор
            // стоит в пустом Entry и модель замирает в позе привязки.
            controller.layers[0].stateMachine.defaultState = state;
            return controller;
        }

        private static AnimationClip ResolveClip(
            string folder, string name, List<string> skipped)
        {
            string[] takes = AssetDatabase.FindAssets("t:Model", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith(name + "@", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path)
                .ToArray();

            if (takes.Length == 0)
            {
                skipped.Add($"{name}: нет ни одного клипа {name}@…fbx");
                return null;
            }

            string chosen = takes.FirstOrDefault(path =>
                path.IndexOf("@Idle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? takes[0];

            // В выгрузке Mixamo два такта: служебный длиной в один кадр и
            // настоящий. Берём длинный — короткий даёт застывшую позу.
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(chosen)
                .OfType<AnimationClip>()
                .Where(candidate => !candidate.name.StartsWith("__preview__"))
                .OrderByDescending(candidate => candidate.length)
                .FirstOrDefault();

            if (clip == null)
            {
                skipped.Add($"{name}: в {Path.GetFileName(chosen)} нет клипа");
                return null;
            }

            // ЗАЦИКЛИВАНИЕ ЗДЕСЬ НЕ ПРАВИТСЯ, А ТОЛЬКО ПРОВЕРЯЕТСЯ.
            //
            // Им распоряжается разбор импорта (RazlomCharacterImport), и он
            // переписывает clipAnimations при КАЖДОМ импорте. Правка отсюда
            // честно применялась и тут же откатывалась следующим реимпортом —
            // ровно это и держало торговца неподвижным. Правило должно жить в
            // одном месте, и это место — импортёр.
            if (!clip.isLooping)
                skipped.Add($"{name}: клип не зациклен — переимпортируйте модель (Reimport)");

            return clip;
        }

        /// <summary>
        /// Вешает контроллер на модель, стоящую в открытой сцене.
        ///
        /// Экземпляры ищутся по ИСТОЧНИКУ префаба, а не по имени объекта: имя
        /// в сцене автор меняет как хочет, ссылка на модель остаётся.
        ///
        /// Аниматора может не быть вовсе: если модель перетащили в сцену до
        /// того, как ей выставили Humanoid, экземпляр остаётся без него.
        /// Занятый контроллер не трогаем — если его поставили руками, значит
        /// так и задумано.
        /// </summary>
        private static List<string> AssignInScene(
            string folder, string name, AnimatorController controller)
        {
            var touched = new List<string>();
            string bodyPath = $"{folder}/{name}.fbx";

            foreach (GameObject root in SceneRootsOf(bodyPath, name))
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = Undo.AddComponent<Animator>(root);
                    Log($"  {root.name}: не было Animator — добавлен");
                }

                if (animator.runtimeAnimatorController != null)
                {
                    Log($"  {root.name}: контроллер уже стоит "
                        + $"({animator.runtimeAnimatorController.name}) — не трогаю");
                    continue;
                }

                Undo.RecordObject(animator, "Разлом: контроллер NPC");
                animator.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(animator);
                touched.Add(root.name);
                Log($"  {root.name}: контроллер назначен");
            }

            if (touched.Count > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            return touched;
        }

        /// <summary>
        /// Экземпляры этой модели в открытой сцене.
        ///
        /// ТРИ ПРИЗНАКА, А НЕ ОДИН. Опознание только по источнику префаба
        /// оказалось слишком узким: модель могли распаковать, вложить в
        /// контейнер или собрать из кусков — и тогда экземпляр перестаёт быть
        /// корнем префаба, хотя визуально это тот же торговец. Аватар и имя
        /// ловят эти случаи, а лишнего не заденут: аватар принадлежит именно
        /// этой модели, а имя проверяется на совпадение с папкой NPC.
        /// </summary>
        private static IEnumerable<GameObject> SceneRootsOf(string modelPath, string name)
        {
            var seen = new HashSet<GameObject>();
            Object body = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<Avatar>().FirstOrDefault();

            foreach (Transform transform in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject candidate = transform.gameObject;
                if (seen.Contains(candidate)) continue;

                bool match = false;

                if (PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
                {
                    Object source =
                        PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate);
                    if (source != null && AssetDatabase.GetAssetPath(source) == modelPath)
                        match = true;
                }

                if (!match && avatar != null)
                {
                    var animator = candidate.GetComponent<Animator>();
                    if (animator != null && animator.avatar == avatar) match = true;
                }

                if (!match && candidate.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)
                    && candidate.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                    match = true;

                if (!match) continue;

                seen.Add(candidate);
                yield return candidate;
            }

            if (seen.Count == 0)
                Log($"  в сцене не найдено ни одного экземпляра {name} "
                    + $"(тело загружено: {body != null}, аватар: {avatar != null})");
        }

        /// <summary>
        /// Полный список того, что вообще есть в сцене.
        ///
        /// Пишется всегда: «не назначилось» без списка кандидатов не лечится,
        /// а заставлять человека переписывать консоль руками — плохая замена
        /// файлу, который можно прочитать целиком.
        /// </summary>
        private static void DumpScene()
        {
            Log("");
            Log("Аниматоры в открытой сцене:");
            Animator[] animators = Object.FindObjectsByType<Animator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (animators.Length == 0) Log("  (ни одного)");

            foreach (Animator animator in animators)
            {
                Object source =
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(animator.gameObject);
                Log($"  «{animator.gameObject.name}»"
                    + $" источник={(source != null ? AssetDatabase.GetAssetPath(source) : "—")}"
                    + $" контроллер={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "нет")}"
                    + $" аватар={(animator.avatar != null ? animator.avatar.name : "нет")}");
            }

            Log("");
            Log("Корни префабов, чей источник лежит в NPC:");
            bool any = false;
            foreach (Transform transform in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject candidate = transform.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate)) continue;

                Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate);
                if (source == null) continue;

                string path = AssetDatabase.GetAssetPath(source);
                if (path.IndexOf("/NPC/", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                any = true;
                Log($"  «{candidate.name}» ← {path}"
                    + $" Animator={(candidate.GetComponentInChildren<Animator>(true) != null)}");
            }
            if (!any) Log("  (ни одного)");
        }

        private static void Report(
            List<string> built, List<string> assigned, List<string> skipped, string reportPath)
        {
            foreach (string problem in skipped) Debug.LogWarning($"[Разлом] NPC {problem}");

            string summary = built.Count == 0
                ? "Не собрано ни одного NPC."
                : $"Собраны контроллеры: {string.Join(", ", built)}.";

            summary += assigned.Count > 0
                ? $"\nПовешены на объекты сцены: {string.Join(", ", assigned)}."
                  + "\n\nНЕ ЗАБУДЬТЕ СОХРАНИТЬ СЦЕНУ (Ctrl+S): без этого правка "
                  + "живёт только до закрытия редактора и в сборку не попадёт."
                : "\n\nВ сцене никому не назначено — модели нет в открытой сцене "
                  + "либо контроллер у неё уже стоял. Что именно найдено, написано "
                  + "в консоли.";

            if (skipped.Count > 0) summary += $"\n\nПропущено: {skipped.Count}. Подробности в консоли.";
            summary += $"\n\nОтчёт: {reportPath}";

            EditorUtility.DisplayDialog("Разлом", summary, "Хорошо");
        }
    }
}
