using System;
using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Game.EditorTools
{
    /// <summary>
    /// Фиксирует текущие простые препятствия на их неизменных авторских корнях.
    /// После этого замена дочерней модели не меняет проходы. Позиции и меши не трогает.
    /// Сравнение источников NavMesh до/после обязательно; несовпадение откатывает установку.
    /// </summary>
    public static class CampNavigationAuthoring
    {
        [MenuItem("Разлом/Лагерь/Зафиксировать препятствия ходьбы")]
        public static void InstallFromMenu() => Debug.Log(InstallLoadedScene());

        /// <summary>Работает только с открытой сценой. Не открывает и не сохраняет другие сцены.</summary>
        public static string InstallLoadedScene()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return "[camp-navigation-authoring] Установку выполнять в остановленном редакторе.";
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return "[camp-navigation-authoring] Нет открытой сцены.";
            GameObject camp = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var world in root.GetComponentsInChildren<SceneWorldView>(true))
                    if (world.CampRoot != null && world.CampRoot.scene == scene) { camp = world.CampRoot; break; }
                if (camp != null) break;
            }
            if (camp == null) return "[camp-navigation-authoring] В открытой сцене не найден SceneWorldView.CampRoot.";
            float ground = 0;
            foreach (var node in camp.GetComponentsInChildren<Transform>(true))
                if (node.name == "Anchor - Player") { ground = node.position.y; break; }
            CampRiverPassage passage = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                passage = root.GetComponentInChildren<CampRiverPassage>(true);
                if (passage != null) break;
            }
            Transform bridge = passage != null ? passage.Bridge : null;
            var before = CampNavigationGeometry.Collect(camp.transform, bridge, ground);
            if (before.Count == 0) return "[camp-navigation-authoring] Препятствий нет; сцена не изменена.";
            var expected = new Dictionary<Transform, NavMeshBuildSource>();
            foreach (var footprint in before)
            {
                if (footprint.Root == camp.transform || !footprint.Root.IsChildOf(camp.transform))
                    return "[camp-navigation-authoring] Неоднозначный корень препятствия; сцена не изменена: " + footprint.Root.name;
                expected.Add(footprint.Root, CampNavigationGeometry.Source(footprint, ground));
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Постоянные основания препятствий лагеря");
            int installed = 0, frozen = 0;
            try
            {
                foreach (var footprint in before)
                {
                    var proxy = footprint.Root.GetComponent<CampNavigationObstacle>();
                    if (proxy == null) { proxy = Undo.AddComponent<CampNavigationObstacle>(footprint.Root.gameObject); installed++; }
                    else Undo.RecordObject(proxy, "Зафиксировать основание препятствия");
                    if (proxy.FitVisualFootprint) frozen++;
                    proxy.Role = footprint.Role;
                    proxy.FitVisualFootprint = false;
                    proxy.Center = footprint.Role == CampObstacleRole.Trunk ? footprint.LocalCenter : footprint.LocalBounds.center;
                    proxy.Size = footprint.LocalBounds.size;
                    proxy.Radius = footprint.Radius;
                    proxy.FootprintScale = footprint.Scale;
                    EditorUtility.SetDirty(proxy);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
                }
                var after = CampNavigationGeometry.Collect(camp.transform, bridge, ground);
                if (after.Count != before.Count)
                    throw new InvalidOperationException("Изменилось число оснований: " + before.Count + " → " + after.Count);
                foreach (var footprint in after)
                {
                    if (!expected.TryGetValue(footprint.Root, out var source)
                        || !SameSource(source, CampNavigationGeometry.Source(footprint, ground)))
                        throw new InvalidOperationException("Изменился источник NavMesh: " + footprint.Root.name);
                }
                Undo.CollapseUndoOperations(group);
                EditorSceneManager.MarkSceneDirty(scene);
                return $"[camp-navigation-authoring] Оснований {before.Count}, новых компонентов {installed}, авторазмеров зафиксировано {frozen}. Источники NavMesh совпадают; сцена не сохранена.";
            }
            catch (Exception error)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogException(error);
                return "[camp-navigation-authoring] Установка полностью отменена: " + error.Message;
            }
        }

        static bool SameSource(NavMeshBuildSource before, NavMeshBuildSource after)
        {
            if (before.shape != after.shape || before.area != after.area || (before.size - after.size).sqrMagnitude > .0000000001f)
                return false;
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (Mathf.Abs(before.transform[row, column] - after.transform[row, column]) > .00001f) return false;
            return true;
        }
    }
}
