using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Окна лагерных NPC на паке «Ночная акварель» (префаб Resources/UI/Prefabs/CampShopsWc,
    /// собирает CampShopsWcBuilder): кузнец, торговец, алхимик и подпись над NPC.
    /// Только ссылки и общий вид вкладок/строк; содержимое ставит <see cref="CampServicesView"/>.
    /// </summary>
    public sealed class CampShopView : MonoBehaviour
    {
        [Header("Подпись над NPC")]
        public RectTransform Hint;
        public TMP_Text HintTitle, HintNote;
        [Tooltip("Кто это: кузнец, торговец, алхимик")] public TMP_Text HintRole;
        [Tooltip("Плашка клавиши (E, ПКМ); ширина подгоняется под подпись")] public RectTransform HintKeyCap;
        public TMP_Text HintKey;

        [Header("Окна")]
        public CampShopScreen Smith;
        public CampShopScreen Trader;
        public CampAlchemyScreen Alchemist;

        /// <summary>
        /// Вкладка пака или «Дыма и света» (UiInkKit.Tab): оранжевая подпись и подчёркивание
        /// (у пака — полоса, у «Дыма и света» — нить света) у выбранной.
        /// </summary>
        public static void SetTab(Button tab, bool selected)
        {
            if (tab == null) return;
            var label = Part(tab.transform, "Надпись")?.GetComponent<ThemeColor>();
            if (label != null) label.SetRole(selected ? UiTheme.Role.Accent : UiTheme.Role.Text, selected ? 1f : .85f);
            var line = Part(tab.transform, "Подчёркивание");
            if (line != null) line.gameObject.SetActive(selected);
        }

        /// <summary>
        /// Строка списка пака или «Дыма и света» (UiInkKit.ListRow): у выбранной — подложка
        /// (у «Дыма и света» — полоса дыма), рамка (нить света) и оранжевый ромб-огонёк.
        /// </summary>
        public static void SetRow(Button row, bool selected)
        {
            if (row == null) return;
            var bg = Part(row.transform, "Подложка");
            if (bg != null) bg.gameObject.SetActive(selected);
            var frame = Part(row.transform, "Рамка");
            if (frame != null) frame.gameObject.SetActive(selected);
            var marker = Part(row.transform, "Маркер")?.GetComponent<ThemeColor>();
            if (marker != null) marker.SetRole(selected ? UiTheme.Role.Accent : UiTheme.Role.TextMuted);
        }

        /// <summary>
        /// Часть вкладки или строки: прямой ребёнок (пак, UiInkKit.Tab и ListRow), иначе внук —
        /// окно может завернуть деталь «Дыма и света» в свою кнопку. Глубже не ищем: у вложенной
        /// ячейки тоже есть «Рамка».
        /// </summary>
        static Transform Part(Transform root, string name)
        {
            Transform part = root.Find(name);
            if (part != null) return part;
            foreach (Transform child in root)
            {
                part = child.Find(name);
                if (part != null) return part;
            }
            return null;
        }
    }

    /// <summary>Окно кузнеца или торговца: портрет, кошелёк, вкладки, сетка вещей, карточка выбранной вещи.</summary>
    [Serializable]
    public sealed class CampShopScreen
    {
        public CanvasGroup Group;
        public RawImage Portrait;
        public TMP_Text Title;
        public TMP_Text Gold, Shards;
        public GameObject ShardsGroup;
        public Button[] Tabs = new Button[2];
        public TMP_Text GridCaption;
        public CampShopCell[] Cells = new CampShopCell[0];
        [Tooltip("Надетые вещи: оружие, броня, кольцо, талисман")] public CampShopCell[] Worn = new CampShopCell[0];
        public TMP_Text WornCaption;
        public TMP_Text Info;
        [Tooltip("Кнопка под сеткой (обновить товар у торговца)")] public Button Extra;
        public TMP_Text ExtraLabel;

        [Header("Выбранная вещь")]
        public CampShopCell Item;
        public TMP_Text ItemName, ItemMeta;
        [Tooltip("Строки аффиксов (кузнец)")] public Button[] Rows = new Button[0];
        public TMP_Text[] RowLabels = new TMP_Text[0];
        [Tooltip("Свойства и сравнение (торговец)")] public TMP_Text Detail;
        public TMP_Text Preview;
        public GameObject Price;
        public TMP_Text PriceGold, PriceShards;
        public GameObject PriceShardsGroup;
        public TMP_Text Note;
        public Button Action;
        public TMP_Text ActionLabel;
        public Button Back;
        [Tooltip("Реплика NPC в облачке под портретом (пустая строка прячет облачко)")] public TMP_Text Message;
        [Tooltip("Имя в облачке реплики")] public TMP_Text Speaker;
    }

    /// <summary>Окно алхимика: портрет, кошелёк, шесть карточек зелий.</summary>
    [Serializable]
    public sealed class CampAlchemyScreen
    {
        public CanvasGroup Group;
        public RawImage Portrait;
        public TMP_Text Title;
        public TMP_Text Gold;
        [Tooltip("Вкладки «Зелья» и «Рецепты»")] public Button[] Tabs = new Button[2];
        public GameObject PotionsPage, RecipesPage;
        public CampPotionCard[] Potions = new CampPotionCard[0];
        [Tooltip("Рецепты новых зелий: условие, заказ, обмен (поля заказа у CampPotionCard)")] public CampPotionCard[] Recipes = new CampPotionCard[0];
        [Tooltip("Системная строка отказа («не хватает золота»): реплики алхимика про неудачу нет")] public TMP_Text Status;
        public Button Back;
        [Tooltip("Реплика NPC в облачке под портретом (пустая строка прячет облачко)")] public TMP_Text Message;
        [Tooltip("Имя в облачке реплики")] public TMP_Text Speaker;
    }
}
