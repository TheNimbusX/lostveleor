using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Разрешает чтение мешей, из которых строится навигация лагеря.
    ///
    /// ЗАЧЕМ. NavMesh в лагере собирается на старте из коллайдеров, а коллайдеры
    /// вешаются на меши сцены. Меш без галки Read/Write отдаёт свои вершины
    /// только в редакторе: в собранной игре данные выгружаются из памяти сразу
    /// после загрузки на видеокарту. Отсюда «does not allow read access. This
    /// will work in playmode in the editor but not in player» — и навигация в
    /// плеере получалась ДРУГОЙ, чем та, что видно при запуске из редактора.
    /// Хуже всего, что расхождение молчаливое: игра запускается, герой ходит,
    /// просто сквозь часть деревьев.
    ///
    /// ПОЧЕМУ НЕ ВСЕМ МОДЕЛЯМ ПОДРЯД. Read/Write держит вторую копию меша в
    /// оперативной памяти. Платить за это имеет смысл только там, где вершины
    /// действительно читаются, поэтому список берётся не из папки, а из живой
    /// сцены — тем же отбором, которым пользуется сама навигация
    /// (<see cref="CampPlayerView.UsedByNavigation"/>).
    ///
    /// Инструмент идемпотентен: повторный запуск ничего не меняет и так и
    /// говорит.
    /// </summary>
    public static class RazlomCampMeshReadable
    {
        [MenuItem("Разлом/Лагерь/Разрешить чтение мешей навигации")]
        public static void MakeReadable()
        {
            if (!TryCollect(out List<Mesh> meshes, out string problem))
            {
                EditorUtility.DisplayDialog("Разлом", problem, "Понятно");
                return;
            }

            var changed = new List<string>();
            var alreadyReadable = new List<string>();
            var notModels = new List<string>();

            foreach (Mesh mesh in meshes)
            {
                if (mesh.isReadable) { alreadyReadable.Add(mesh.name); continue; }

                string path = AssetDatabase.GetAssetPath(mesh);
                // Меш, собранный кодом или лежащий прямо в сцене, импортёра не
                // имеет — включить ему чтение нечем.
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { notModels.Add($"{mesh.name} ({path})"); continue; }

                importer.isReadable = true;
                importer.SaveAndReimport();
                changed.Add(path);
            }

            Report(changed, alreadyReadable, notModels);
        }

        /// <summary>
        /// Собирает меши навигации из открытой сцены.
        ///
        /// Сцена обязана быть открыта: отбор опирается на живую иерархию —
        /// на то, есть ли уже коллайдер и какой уровень LODGroup рисуется. Из
        /// файла сцены этого не видно.
        /// </summary>
        private static bool TryCollect(out List<Mesh> meshes, out string problem)
        {
            meshes = null;

            SceneWorldView world = Object.FindAnyObjectByType<SceneWorldView>();
            if (world == null || world.CampRoot == null)
            {
                problem = "В открытой сцене нет SceneWorldView с лагерем. "
                          + "Откройте сцену лагеря и повторите.";
                return false;
            }

            meshes = world.CampRoot.transform
                .GetComponentsInChildren<MeshFilter>()
                .Where(CampPlayerView.UsedByNavigation)
                .Select(filter => filter.sharedMesh)
                .Distinct()
                .ToList();

            if (meshes.Count == 0)
            {
                problem = "Навигация лагеря не берёт ни одного меша — проверять нечего.";
                return false;
            }

            problem = null;
            return true;
        }

        private static void Report(
            List<string> changed, List<string> alreadyReadable, List<string> notModels)
        {
            if (changed.Count > 0)
            {
                changed.Sort();
                Debug.Log($"[Разлом] Чтение включено у {changed.Count} моделей:\n"
                          + string.Join("\n", changed));
            }

            if (notModels.Count > 0)
            {
                // Не ошибка инструмента, а честная дыра: эти меши так и останутся
                // нечитаемыми, и навигация в сборке будет без них.
                notModels.Sort();
                Debug.LogWarning(
                    $"[Разлом] {notModels.Count} мешей не из моделей — чтение им включить нечем, "
                    + $"в сборке навигация их не увидит:\n{string.Join("\n", notModels)}");
            }

            string summary = changed.Count > 0
                ? $"Чтение включено у {changed.Count} моделей.\n"
                  + $"Уже было включено: {alreadyReadable.Count}."
                : $"Менять нечего: у всех {alreadyReadable.Count} мешей навигации "
                  + "чтение уже включено.";

            if (notModels.Count > 0)
                summary += $"\n\nОсталось нечитаемых мешей вне моделей: {notModels.Count}. "
                           + "Подробности в консоли.";

            EditorUtility.DisplayDialog("Разлом", summary, "Хорошо");
        }
    }
}
