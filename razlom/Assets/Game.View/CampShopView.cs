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
        [Tooltip("Кузница Эни вкладками внутри окна кузнеца (temper-a.png, миграция v4); Root пуст — префаб до v4, старая страница")]
        public CampTemperScreen Temper;

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
        [Tooltip("Попытки закалки: «1 / 3» и отметки (кузнец); имя поля — от прежней перековки, его держит префаб")] public TMP_Text ReforgeCount;
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

    /// <summary>
    /// Кузница Эни (temper-a.png, 06.10): вкладки «Закалить · Переплавить · Добавить свойство · Сердце · Разобрать» внутри
    /// окна кузнеца, наковальня с дугой «Риск трещины», карточки свойств, «Ударить [E]» и «Взять [Esc]», строка цены.
    /// Собирает CampShopsWcBuilder.Temper (миграция v4), содержимое ставит CampServicesView.Temper. Unity сериализует класс
    /// всегда (пустым у префаба до v4) — признак собранной кузницы: <see cref="Root"/> не пуст.
    /// </summary>
    [Serializable]
    public sealed class CampTemperScreen
    {
        [Tooltip("Узел страницы кузницы в «Раскладке» окна кузнеца")] public GameObject Root;
        [Tooltip("Вкладки по EniTab: Закалить, Переплавить, Добавить свойство, Сердце, Разобрать")] public Button[] Tabs = new Button[0];
        [Tooltip("Замок и «Эни · ранг N» под закрытой вкладкой")] public GameObject[] TabLocks = new GameObject[0];
        public TMP_Text[] TabLockLabels = new TMP_Text[0];

        [Header("Наковальня")]
        public TMP_Text RiskCaption, RiskValue;
        [Tooltip("Дорожка дуги: верхняя половина круга")] public Image RiskTrack;
        [Tooltip("Огонь дуги: Filled Radial360 от левого края, 100% риска — половина круга")] public Image RiskFill;
        [Tooltip("Рисунок наковальни (Assets/UI/CampShops/anvil.png); нет — вещь в медальоне")] public RawImage Anvil;
        [Tooltip("Вещь на наковальне (когда наковальня есть)")] public Image AnvilItem;
        [Tooltip("Медальон вещи (когда наковальни нет)")] public CampTemperMedal ItemMedal;
        [Tooltip("Вещь внутри медальона")] public Image ItemMedalArt;
        [Tooltip("Искры над наковальней")] public GameObject Embers;
        public TMP_Text ItemName, ItemMeta;
        [Tooltip("Отметки попыток: трещина, потрачена, свободна")] public CampTemperMedal[] Pips = new CampTemperMedal[0];
        public TMP_Text AttemptsLabel;

        [Header("Свойства и варианты")]
        public TMP_Text PropsCaption;
        [Tooltip("«Вместо: Урон +12», «Оплачено · выбор обязателен»")] public TMP_Text Replaced;
        public CampTemperCard[] Cards = new CampTemperCard[0];
        [Tooltip("Сердца в вещи: «Сердце Чащи · Корни»")] public TMP_Text HeartsLine;
        [Tooltip("Знаки статов по StatType (белые маски wc_stat_*)")] public Texture[] StatIcons = new Texture[0];
        [Tooltip("Знаки граней сердца по порядку граней босса; окончательные — Resources/UI/HeartFacets")] public Texture[] FacetIcons = new Texture[0];

        [Header("Кнопки и цена")]
        public Button Primary;
        public TMP_Text PrimaryLabel, PrimaryKey;
        public Button Secondary;
        public TMP_Text SecondaryLabel, SecondaryKey;
        [Tooltip("Строка цены: подпись «Цена:» / «Выход:» и валюты")] public GameObject PriceRow;
        public TMP_Text PriceCaption;
        [Tooltip("Числа цены: золото, осколки, сталь, сердце; узел валюты — родитель числа")] public TMP_Text[] PriceValues = new TMP_Text[0];
        [Tooltip("Значки цены по тем же валютам; окончательные стали и сердца — Resources/UI/CampCurrency")] public RawImage[] PriceIcons = new RawImage[0];
        [Tooltip("Предупреждение под ценой: риск, нехватка, причина")] public TMP_Text Warning;

        [Header("Кошелёк")]
        [Tooltip("Золото, осколки, сталь, сердца в лагере")] public TMP_Text[] WalletValues = new TMP_Text[0];
        public RawImage[] WalletIcons = new RawImage[0];
        [Tooltip("Прокрутка сумки (48 ячеек в 5 колонок)")] public ScrollRect BagScroll;
    }

    /// <summary>Карточка свойства, варианта или грани: медальон со знаком, имя, значение «12 → 15», пометка справа.</summary>
    [Serializable]
    public sealed class CampTemperCard
    {
        public Button Button;
        [Tooltip("Полоса дыма и нить выбранной карточки")] public GameObject Chosen;
        public CampTemperMedal Icon;
        public TMP_Text Name, Value, Note;
    }

    /// <summary>Медальон «Дыма и света» (CampInkParts.IconMedallion): рисунок, кольцо покоя, огонь «в силе», выбор, замок.</summary>
    [Serializable]
    public sealed class CampTemperMedal
    {
        public GameObject Root;
        public Button Button;
        public RawImage Art;
        public Image Ring;
        [Tooltip("Огненное кольцо light_ring: выбрано / потрачено")] public GameObject Fire;
        [Tooltip("Толстое кольцо акцента")] public GameObject Picked;
        public GameObject Locked;
        public TMP_Text LockLabel;
    }
}
