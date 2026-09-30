using Game.View;
using UnityEditor;
using UnityEngine;
using static Game.EditorTools.CampPolishUiBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static class CampTraderProgressionUiBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTraderProgression.prefab";

        [MenuItem("Разлом/Лагерь/Подготовить окно резерва и заказа Вена")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
            UiThemeBuilder.Ensure(false);
            var card = Canvas("CampTraderProgression", out var root, out var group, 1120, 740);
            var panel = root.AddComponent<CampTraderProgressionPanel>();
            panel.Group = group;
            panel.Title = Text(card, "Вен", "Прилавок Вена", 40, 28, 1040, 62, 40);
            Text(card, "Пояснение", "Резерв и следующий товар", 40, 104, 1040, 40, 24, Role.TextMuted);
            Text(card, "Резерв", "Сохранить товар", 40, 174, 490, 44, 28);
            panel.SelectedStock = Text(card, "Выбранный товар", "", 40, 234, 490, 104, 23);
            panel.ReservedStock = Text(card, "Сохранённый товар", "", 40, 356, 490, 100, 23, Role.Accent);
            panel.Reserve = Button(card, "Резерв товара", 40, 480, 490, 56, out panel.ReserveLabel, true, 22);
            panel.ReserveReason = Text(card, "Условие резерва", "", 40, 558, 490, 112, 21, Role.TextMuted);
            Text(card, "Категория", "Следующий товар", 580, 174, 500, 44, 28);
            Text(card, "Правило категории", "При каждом обновлении одна новая вещь будет выбранной категории. Остальные товары появятся как обычно.", 580, 234, 500, 96, 23);
            panel.Categories = new UnityEngine.UI.Button[4];
            panel.CategoryLabels = new TMPro.TMP_Text[4];
            for (int i = 0; i < 4; i++)
                panel.Categories[i] = Button(card, "Категория " + i, 580 + i % 2 * 258, 350 + i / 2 * 76, 242, 58, out panel.CategoryLabels[i], font: 22);
            panel.CategoryReason = Text(card, "Условие категории", "", 580, 520, 500, 110, 21, Role.TextMuted);
            panel.Status = Text(card, "Результат выбора", "", 40, 684, 744, 42, 18, Role.TextMuted);
            panel.Back = Button(card, "Вернуться к прилавку", 800, 660, 280, 56, out _, font: 20);
            root.SetActive(false);
            try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
