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

        [Tooltip("Версия раскладки префаба: миграции CampShopsWcBuilder доводят ручные правки до неё")]
        [HideInInspector] public int LayoutVersion;

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

    /// <summary>
    /// Окно кузнеца или торговца: портрет, кошелёк, сетка вещей, карточка выбранной вещи.
    /// С 29.09 (кадры «Кузнец А», «Торговец А») — одна страница без вкладок: у кузнеца две кнопки
    /// рядом, у торговца товары и сумка рядом и одна кнопка по выбранной вещи.
    /// </summary>
    [Serializable]
    public sealed class CampShopScreen
    {
        public CanvasGroup Group;
        public RawImage Portrait;
        public TMP_Text Title;
        [Tooltip("Что делает NPC — под заголовком (вместо вкладок)")] public TMP_Text Subtitle;
        public TMP_Text Gold, Shards;
        public GameObject ShardsGroup;
        [Tooltip("Старое: вкладки до одной страницы (29.09); миграция их прячет")] public Button[] Tabs = new Button[2];
        public TMP_Text GridCaption;
        [Tooltip("Сумка: 48 ячеек")] public CampShopCell[] Cells = new CampShopCell[0];
        [Tooltip("Цена продажи под ячейкой сумки (торговец)")] public TMP_Text[] CellPrices = new TMP_Text[0];
        [Tooltip("Надетые вещи: оружие, броня, кольцо, талисман")] public CampShopCell[] Worn = new CampShopCell[0];
        public TMP_Text WornCaption;
        [Tooltip("Товары прилавка рядом с сумкой (торговец)")] public CampShopGood[] Goods = new CampShopGood[0];
        public TMP_Text GoodsCaption;
        public TMP_Text Info;
        [Tooltip("Вторая кнопка: «Обновить товары» у торговца, «Разобрать» у кузнеца")] public Button Extra;
        public TMP_Text ExtraLabel;
        [Tooltip("Что даст разбор: осколки (кузнец)")] public GameObject Yield;
        public TMP_Text YieldShards;
        [Tooltip("Под второй кнопкой: «Предмет будет уничтожен», запрет или подтверждение")] public TMP_Text ExtraNote;

        [Header("Выбранная вещь")]
        public CampShopCell Item;
        public TMP_Text ItemName, ItemMeta;
        [Tooltip("Перековки: «1 / 3» и три отметки (кузнец)")] public TMP_Text ReforgeCount;
        public Image[] ReforgePips = new Image[0];
        [Tooltip("Заголовок «Что перековать»")] public TMP_Text AffixCaption;
        [Tooltip("Строки аффиксов (кузнец)")] public Button[] Rows = new Button[0];
        public TMP_Text[] RowLabels = new TMP_Text[0];
        [Tooltip("Свойства (торговец)")] public TMP_Text Detail;
        [Tooltip("Сравнение с надетым (торговец)")] public TMP_Text Compare;
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

        [Header("Клавиши внизу")]
        [Tooltip("Enter — основная кнопка")] public GameObject MainKey;
        public TMP_Text MainKeyLabel;
        [Tooltip("Del — вторая кнопка (разбор)")] public GameObject SecondKey;
        public TMP_Text SecondKeyLabel;
        [Tooltip("Подпись у Esc: «Закрыть» или «Отменить», пока ждёт подтверждение")] public TMP_Text CloseKeyLabel;
    }

    /// <summary>Товар на прилавке торговца: вещь, название и цена; нажимается весь.</summary>
    [Serializable]
    public sealed class CampShopGood
    {
        public Button Button;
        public CampShopCell Cell;
        public TMP_Text Name, Price;
        [Tooltip("Значок и число цены: у проданного прячутся")] public GameObject PriceGroup;
        [Tooltip("Подложка выбранного товара")] public GameObject Chosen;
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
