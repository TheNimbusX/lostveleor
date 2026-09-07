using System;
using Game.Data;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.LocationEditor
{
    [CustomEditor(typeof(LocationProfileAsset))]
    public sealed class LocationProfileInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Изменение набора модулей, stableKey, размеров, коннекторов и весов меняет карты на прежних сидах. Параметры уровней влияют на генерацию и спавн. Сохраните профиль вместе с реплеями.", MessageType.Warning);
            DrawDefaultInspector();
            try { ((LocationProfileAsset)target).ToDefinition(); }
            catch (ArgumentException e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
        }
    }

    [CustomEditor(typeof(LocationTheme))]
    public sealed class LocationThemeInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Этот профиль используется в игре и в мастерской. Изменение оформления не меняет карту, спавн или боевые броски. После изменения нажмите «Пересобрать» в мастерской.", MessageType.Info);
            DrawDefaultInspector();
            var theme = (LocationTheme)target;
            try { theme.Style?.Validate(); }
            catch (ArgumentException e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
            if (theme.Gameplay == null) EditorGUILayout.HelpBox("Назначьте игровой профиль.", MessageType.Error);
            if (GUILayout.Button("Открыть мастерскую с этим профилем")) LocationPreviewWindow.OpenProfile(theme);
        }
    }

    [CustomPropertyDrawer(typeof(LevelSettingsAsset))]
    public sealed class LevelSettingsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => (EditorGUIUtility.singleLineHeight + 3) * (property.isExpanded ? 8 : 1);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            int start = property.propertyPath.LastIndexOf('[') + 1;
            int end = property.propertyPath.LastIndexOf(']');
            if (start > 0 && end > start && int.TryParse(property.propertyPath.Substring(start, end - start), out int index))
                label = new GUIContent("Уровень " + (index + 1));
            EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                string[] fields = { "Rooms", "Exits", "Loops", "RewardBranches", "MinEnemies", "MaxEnemies", "EnemyHealth" };
                string[] labels = { "Цель: модулей", "Выходов (до)", "Петель (до)", "Веток наград (до)", "Врагов: минимум", "Врагов: максимум", "Здоровье врага" };
                for (int i = 0; i < fields.Length; i++)
                {
                    position.y += EditorGUIUtility.singleLineHeight + 3;
                    EditorGUI.PropertyField(position, property.FindPropertyRelative(fields[i]), new GUIContent(labels[i]));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }

    [CustomEditor(typeof(ModuleAsset))]
    public sealed class ModuleAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var asset = (ModuleAsset)target;
            try { asset.ToDefinition(); }
            catch (ArgumentException e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
            EditorGUILayout.HelpBox("X — вправо, Y — вверх. Коннектор стоит в крайней клетке и смотрит наружу. Одна клетка = 2 м. Сейчас игровая форма модуля — прямоугольник.", MessageType.None);
            Rect rect = GUILayoutUtility.GetRect(180, 210, GUILayout.ExpandWidth(true));
            int width = Mathf.Clamp(asset.Width, 1, 32), height = Mathf.Clamp(asset.Height, 1, 32);
            float cell = Mathf.Min((rect.width - 30) / width, (rect.height - 30) / height);
            var origin = new Vector2(rect.center.x - width * cell * 0.5f, rect.center.y + height * cell * 0.5f);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    EditorGUI.DrawRect(new Rect(origin.x + x * cell + 1, origin.y - (y + 1) * cell + 1, cell - 2, cell - 2), new Color(0.26f, 0.4f, 0.32f));
            if (asset.Connectors == null) return;
            Color before = Handles.color;
            Handles.color = Color.cyan;
            for (int i = 0; i < asset.Connectors.Length; i++)
            {
                var c = asset.Connectors[i];
                var center = new Vector2(origin.x + (c.Cell.x + 0.5f) * cell, origin.y - (c.Cell.y + 0.5f) * cell);
                Directions.Step(c.Facing, out int dx, out int dy);
                Handles.DrawAAPolyLine(3, center, center + new Vector2(dx, -dy) * cell * 0.8f);
                GUI.Label(new Rect(center.x, center.y, 28, 20), i.ToString(), EditorStyles.whiteMiniLabel);
            }
            Handles.color = before;
        }
    }
}
