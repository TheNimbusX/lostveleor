using Game.View;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(CampRiftEntrance))]
public sealed class CampRiftEntranceEditor : Editor
{
    readonly BoxBoundsHandle _bounds = new BoxBoundsHandle();
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Вход прикреплён к этой арке. Перемещай арку целиком; голубую зону можно менять ручками в Scene. Вход срабатывает при пересечении зоны ногами героя.", MessageType.Info);
        DrawDefaultInspector();
    }
    void OnSceneGUI()
    {
        var entrance = (CampRiftEntrance)target;
        using (new Handles.DrawingScope(Color.cyan, entrance.transform.localToWorldMatrix))
        {
            _bounds.center = entrance.TriggerCenter; _bounds.size = entrance.TriggerSize;
            EditorGUI.BeginChangeCheck(); _bounds.DrawHandle();
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(entrance,"Зона входа в забег");
            entrance.TriggerCenter = _bounds.center; entrance.TriggerSize = _bounds.size;
            PrefabUtility.RecordPrefabInstancePropertyModifications(entrance);
            EditorUtility.SetDirty(entrance);
        }
    }
}
