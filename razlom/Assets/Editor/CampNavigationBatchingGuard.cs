using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// Не даёт статическому батчингу склеить меши, из которых лагерь строит навигацию.
    ///
    /// ЗАЧЕМ. Навигация лагеря собирается на старте из MeshFilter.sharedMesh
    /// (<see cref="CampPlayerView.UsedByNavigation"/>). В собранной игре Unity
    /// заранее склеивает статичные меши в «Combined Mesh (root: scene)», а такой
    /// меш читать нельзя: NavMeshBuilder его пропускает, и сквозь эти объекты
    /// герой ходит. В редакторе батчинга нет, поэтому расхождение видно только
    /// в плеере — по красной консоли «does not allow read access».
    ///
    /// Сохранённая сцена не меняется: флаг снимается только у копии, которая
    /// уходит в сборку. Картинка тоже: меняется число draw call, а не вид.
    /// </summary>
    public sealed class CampNavigationBatchingGuard : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Вход в Play mode тоже зовёт обработку сцены, но батчинга там нет.
            if (report == null || !BuildPipeline.isBuildingPlayer) return;

            SceneWorldView world = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                world = root.GetComponentInChildren<SceneWorldView>(true);
                if (world != null) break;
            }
            if (world == null || world.CampRoot == null) return;

            int unbatched = 0;
            var unreadable = new List<string>();
            foreach (MeshFilter mesh in world.CampRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (!CampPlayerView.UsedByNavigation(mesh)) continue;

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(mesh.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(mesh.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                    unbatched++;
                }

                // Снятый батчинг бесполезен, если исходный меш сам нечитаем.
                if (!mesh.sharedMesh.isReadable) unreadable.Add(mesh.sharedMesh.name);
            }

            if (unbatched > 0)
                Debug.Log($"[camp-build] Статический батчинг снят у {unbatched} объектов навигации лагеря.");
            if (unreadable.Count > 0)
            {
                unreadable.Sort();
                Debug.LogWarning($"[camp-build] {unreadable.Count} мешей навигации нечитаемы — в сборке их не будет. "
                    + "Меню «Разлом → Лагерь → Разрешить чтение мешей навигации». Список: "
                    + string.Join(", ", unreadable));
            }
        }
    }
}
