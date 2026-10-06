using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;
using TextStep = Game.View.UiTheme.TextStep;

namespace Game.EditorTools
{
    /// <summary>
    /// Кузница Эни вкладками внутри окна кузнеца (06.10, раскладка temper-a.png, наш стиль — ingame-style/temper.png): DESIGN
    /// «все окна жителя — одно полноэкранное окно с портретом и облачком». Миграция v4 поверх ручных правок: узлы одной
    /// страницы кузнеца (29.09) прячутся, а не удаляются (ссылки и правки владельца живы); надетое и сумка переставляются
    /// вправо; заголовок — к середине; новая страница «Кузница» собирается целиком один раз. Портрет, сцена, облачко,
    /// «Развитие лагеря», крестик и «[Esc] Закрыть» — те же. Торговца и алхимика миграция не трогает.
    /// Без панелей: всё на дыму окна, медальоны со знаками в тёмных кольцах, наведение — тонкая тлеющая кромка.
    /// </summary>
    public static partial class CampShopsWcBuilder
    {
        const string TemperPageName = "Кузница";
        /// <summary>Наковальня в нашем стиле — вырез из выбранного концепта, его согласует владелец. Нет файла — вещь в медальоне.</summary>
        const string AnvilArtPath = "Assets/UI/CampShops/anvil.png";

        // Колонки кузницы в «Раскладке» 1920×1080 (концепт 1672 px × 1,148): наковальня, свойства, надетое и сумка.
        const float TitleX = 1010f, AnvilX = 800f, PropsX = 1060f, PropsW = 400f, SideX = 1516f, SideW = 364f;
        const float CardsY = 260f, CardH = 70f, CardStep = 78f, PipY = 742f, PipSize = 44f, PipStep = 56f;
        const int TemperCards = 6;

        static readonly string[] TemperTabLabels = { "Закалить", "Переплавить", "Добавить свойство", "Сердце", "Разобрать" };

        /// <summary>Знаки свойств по StatType (порядок перечисления), те же акварельные белые маски, что у листа героя палатки.</summary>
        static readonly string[] TemperStatIcons =
        {
            "wc_stat_heart", "wc_stat_damage", "wc_stat_attack_speed", "wc_stat_move_speed", "wc_stat_crit_chance", "wc_stat_crit_power",
            "wc_stat_armor", "wc_stat_fire_resist", "wc_stat_lavidium", "wc_stat_lavidium_regen", "wc_stat_ability_speed", "wc_stat_cooldown",
        };

        /// <summary>Грани сердца Чащи (Корни, Пыльца, Цветение): своих знаков нет — эффекты HUD того же смысла до финального арта.</summary>
        static readonly string[] FacetPlaceholders = { "wc_effect_root", "wc_effect_slow", "wc_effect_resin" };
        static readonly string[] FacetArt = { "UI/HeartFacets/roots", "UI/HeartFacets/pollen", "UI/HeartFacets/bloom" };

        /// <summary>
        /// Валюты цены и кошелька: золото, осколки, сталь, сердце. У стали и сердца своих знаков нет — временные белые маски
        /// (молот, сердце забега); окончательные вид грузит из Resources/UI/CampCurrency первым, сборка — тоже, если уже лежат.
        /// </summary>
        static Texture[] TemperCurrencyIcons() => new Texture[]
        {
            Art("gold"), Art("shards"), CampInkParts.ArtOr("UI/CampCurrency/steel", "salvage"), CampInkParts.ArtOr("UI/CampCurrency/heart", "health"),
        };

        /// <summary>
        /// v4 — кузница Эни вкладками (temper-a.png). Идемпотентна: собранная страница (CampShopView.Temper.Root) не
        /// пересобирается. Трогает только окно кузнеца.
        /// </summary>
        static void MigrateTo4(CampShopView view)
        {
            CampShopScreen s = view.Smith;
            if (s?.Group == null)
            {
                Debug.LogWarning("[ui-kit] В окнах лагеря нет кузнеца (CampShopView.Smith): кузница Эни не собрана");
                return;
            }
            if (view.Temper != null && view.Temper.Root != null) return;
            RectTransform frame = FrameOf(s);
            HideSmithOnePage(frame, s);
            PlaceSmithSide(frame, s, out ScrollRect bag);
            view.Temper = BuildTemper(frame, s);
            view.Temper.BagScroll = bag;
            Debug.Log("[ui-kit] Окна лагеря: кузница Эни вкладками собрана (Закалить · Переплавить · Добавить свойство · Сердце · Разобрать).");
        }

        // ---------------------------------------------------------------- одна страница 29.09 → спрятать

        /// <summary>
        /// Узлы одной страницы кузнеца (вещь справа, строки свойств, «Перековать» и «Разобрать», цена, клавиши Enter/Del,
        /// старый кошелёк) прячутся: вид их больше не включает, ссылки префаба остаются для отката.
        /// </summary>
        static void HideSmithOnePage(RectTransform frame, CampShopScreen s)
        {
            foreach (Component part in new Component[] { s.Item, s.Action, s.Extra })
                if (part != null) part.gameObject.SetActive(false);
            foreach (TMP_Text label in new[] { s.ItemName, s.ItemMeta, s.ReforgeCount, s.Preview, s.Note, s.ExtraNote, s.Subtitle, s.Info })
                if (BoxOf(label) is RectTransform box) box.gameObject.SetActive(false);
            if (s.AffixCaption != null && s.AffixCaption.transform.parent != frame && s.AffixCaption.transform.parent != null)
                s.AffixCaption.transform.parent.gameObject.SetActive(false);
            foreach (Button row in s.Rows)
                if (row != null) row.gameObject.SetActive(false);
            foreach (GameObject part in new[] { s.Price, s.Yield, s.MainKey, s.SecondKey })
                if (part != null) part.SetActive(false);
            foreach (string name in new[] { "Линия под вещью", "Линия над ценой", "Перековки", "Отметки перековок", "Клавиши действий", "Подзаголовок" })
                if (frame.Find(name) is Transform node) node.gameObject.SetActive(false);
            // Старый кошелёк (осколки и золото): у кузницы свой ряд со сталью и сердцами.
            foreach (TMP_Text value in new[] { s.Gold, s.Shards })
            {
                Transform wallet = value != null && value.transform.parent != null ? value.transform.parent.parent : null;
                if (wallet != null && wallet.parent == frame) wallet.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Заголовок «Кузница» — над серединой содержимого, нить с камнем под ним; справа «Надето» (4 ячейки) и «Сумка · N / 48»:
        /// 48 существующих ячеек переезжают в прокрутку 5 колонок (в 5 колонок без прокрутки 48 вещей не влезают).
        /// </summary>
        static void PlaceSmithSide(RectTransform frame, CampShopScreen s, out ScrollRect bag)
        {
            Place(s.Title, TitleX - 450f, 24f, 900f, 76f);
            if (s.Title != null)
            {
                s.Title.enableAutoSizing = false;
                s.Title.fontSize = T.Size(TextStep.Display);
            }
            if (frame.Find("Линия") is RectTransform line) TopLeft(line, TitleX - 360f, 100f, 720f, 16f);

            Place(s.WornCaption, SideX, 194f, SideW, 30f);
            if (s.WornCaption != null) { s.WornCaption.fontSize = T.Size(TextStep.Heading); s.WornCaption.characterSpacing = 1f; }
            for (int i = 0; i < s.Worn.Length; i++)
                if (s.Worn[i] != null) TopLeft((RectTransform)s.Worn[i].transform, SideX + i * 72f, 236f, 60f, 60f);

            Place(s.GridCaption, SideX, 316f, SideW, 30f);
            if (s.GridCaption != null) { s.GridCaption.fontSize = T.Size(TextStep.Heading); s.GridCaption.characterSpacing = 1f; }
            RectTransform content = CampInkParts.ScrollGrid(frame, "Сумка кузницы", SideX - 8f, 354f, SideW + 16f, 566f, new Vector2(60f, 60f),
                new Vector2(10f, 10f), 5, out bag, 6);
            foreach (CampShopCell cell in s.Cells)
                if (cell != null) cell.transform.SetParent(content, false);
            if (frame.Find("Сетка") is Transform grid) grid.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- страница кузницы

        static CampTemperScreen BuildTemper(RectTransform frame, CampShopScreen s)
        {
            var t = new CampTemperScreen();
            RectTransform page = Stretch(Node(TemperPageName, frame));
            t.Root = page.gameObject;
            Texture[] currency = TemperCurrencyIcons();

            // Кошелёк: золото, осколки, сталь, сердца — справа сверху, левее крестика.
            CampInkParts.WalletRow(page, "Кошелёк кузницы", 1290f, 34f, 510f, currency, new[] { Role.Coins, Role.Text, Role.Text, Role.Text },
                TextAnchor.MiddleRight, out t.WalletValues);
            t.WalletIcons = IconsOf(t.WalletValues);

            BuildTemperTabs(page, t);
            BuildAnvil(page, t);
            BuildProperties(page, t);

            // Кнопки под нитью: «Ударить [E]» — оранжевый мазок, «Взять [Esc]» — тёмный; клавиша справа внутри мазка.
            TopLeft(UiInkKit.Divider(page, "Линия над кнопками", 680f, false, .5f), 690f, 808f, 680f, 16f);
            t.Primary = CampInkParts.PrimaryWithKey(page, "Ударить", "Ударить", "E", 690f, 830f, 320f, 64f, out t.PrimaryLabel, out t.PrimaryKey);
            t.Secondary = CampInkParts.SecondaryWithKey(page, "Взять", "Взять", "Esc", 1050f, 830f, 320f, 64f, out t.SecondaryLabel, out t.SecondaryKey);

            // Цена одной строкой «Цена: [золото] 80 [осколки] 5 [сталь] 1 [сердце] 1»; у разбора — «Выход: +9».
            RectTransform price = CampInkParts.WalletRow(page, "Цена", 600f, 904f, 860f, currency, new[] { Role.Coins, Role.Text, Role.Text, Role.Text },
                TextAnchor.MiddleCenter, out t.PriceValues, TextStep.Heading);
            t.PriceRow = price.gameObject;
            t.PriceIcons = IconsOf(t.PriceValues);
            t.PriceCaption = CampInkParts.InlineText(price, "Подпись", "Цена:", FontRole.Body, TextStep.Body, Role.TextMuted);
            t.PriceCaption.transform.SetAsFirstSibling();
            t.Warning = CampInkParts.Text(page, "Предупреждение", "", 600f, 950f, 860f, 30f, FontRole.Body, TextStep.Body, Role.Bad, TextAlignmentOptions.Center);
            t.Warning.textWrappingMode = TextWrappingModes.NoWrap;

            t.StatIcons = new Texture[TemperStatIcons.Length];
            for (int i = 0; i < TemperStatIcons.Length; i++) t.StatIcons[i] = CampInkParts.KitTexture(TemperStatIcons[i]);
            t.FacetIcons = new Texture[FacetArt.Length];
            for (int i = 0; i < FacetArt.Length; i++) t.FacetIcons[i] = CampInkParts.ArtOr(FacetArt[i], FacetPlaceholders[i]);
            return t;
        }

        /// <summary>Значки ряда валют (WalletRow): узел валюты — родитель числа, значок — его «Значок».</summary>
        static RawImage[] IconsOf(TMP_Text[] values)
        {
            var icons = new RawImage[values.Length];
            for (int i = 0; i < values.Length; i++)
                icons[i] = values[i] != null && values[i].transform.parent.Find("Значок") is Transform icon ? icon.GetComponent<RawImage>() : null;
            return icons;
        }

        /// <summary>
        /// Пять вкладок одной строкой под заголовком, между ними огоньки; ряд по центру заголовка, но не левее облачка Эни.
        /// Под закрытой — замок и «Эни · ранг N» (узел выключен, включает вид по рангу лагеря).
        /// </summary>
        static void BuildTemperTabs(RectTransform page, CampTemperScreen t)
        {
            t.Tabs = CampInkParts.TabStrip(page, "Вкладки кузницы", TemperTabLabels, 600f, 128f, 48f, TextStep.Heading, false, 16f);
            var strip = (RectTransform)t.Tabs[0].transform.parent;
            float width = strip.sizeDelta.x;
            TopLeft(strip, Mathf.Max(560f, TitleX - width * .5f), 128f, width, 48f);
            t.TabLocks = new GameObject[t.Tabs.Length];
            t.TabLockLabels = new TMP_Text[t.Tabs.Length];
            Texture lockArt = CampInkParts.KitTexture("lock");
            float caption = T.Size(TextStep.Caption);
            for (int i = 0; i < t.Tabs.Length; i++)
            {
                var tab = (RectTransform)t.Tabs[i].transform;
                RectTransform locked = Node("Замок", tab);
                locked.anchorMin = locked.anchorMax = new Vector2(.5f, 0f);
                locked.pivot = new Vector2(.5f, 1f);
                locked.anchoredPosition = new Vector2(0f, -2f);
                locked.sizeDelta = new Vector2(tab.sizeDelta.x + 60f, caption + 6f);
                var row = locked.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.childAlignment = TextAnchor.MiddleCenter;
                row.spacing = 4f;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = false;
                RowIcon(locked, "Значок", lockArt, caption, Role.TextMuted);
                t.TabLockLabels[i] = CampInkParts.InlineText(locked, "Ранг", "Эни · ранг " + Game.View.CampTemperRules.TabRank((EniTab)i), FontRole.Body,
                    TextStep.Caption, Role.TextMuted);
                t.TabLocks[i] = locked.gameObject;
                locked.gameObject.SetActive(false);
            }
        }

        /// <summary>Знак в ряду раскладки: квадрат <paramref name="size"/>, белая маска краской темы, без дымки.</summary>
        static RawImage RowIcon(RectTransform parent, string name, Texture texture, float size, Role role)
        {
            RectTransform rect = Node(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = size;
            element.preferredHeight = element.minHeight = size;
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.enabled = texture != null;
            raw.raycastTarget = false;
            Tint(raw, role);
            raw.material = UiInkKit.Art;
            UiInkKit.Inked(raw, delay: .12f);
            return raw;
        }

        /// <summary>
        /// Колонка наковальни: «Риск трещины» и число, дуга (верхняя половина круга: тихая дорожка и огонь light_ring, Filled от
        /// левого края), наковальня с вещью и искрами (без рисунка наковальни — вещь в большом медальоне), имя вещи, отметки попыток.
        /// </summary>
        static void BuildAnvil(RectTransform page, CampTemperScreen t)
        {
            t.RiskCaption = CampInkParts.Text(page, "Риск трещины", "Риск трещины", AnvilX - 170f, 188f, 340f, 34f, FontRole.Heading, TextStep.Heading,
                Role.Text, TextAlignmentOptions.Center);
            t.RiskValue = CampInkParts.Text(page, "Риск", "0%", AnvilX - 100f, 220f, 200f, 48f, FontRole.Heading, TextStep.Title, Role.Text,
                TextAlignmentOptions.Center);

            RectTransform arc = CampInkParts.CenterAt(Node("Дуга риска", page), AnvilX, 434f, 330f, 330f);
            t.RiskTrack = Layer(arc, "Дорожка", T.CircleFrame, Role.TextMuted, .4f);
            Half(t.RiskTrack, .5f);
            t.RiskTrack.material = UiInkKit.Plain;
            UiInkKit.Inked(t.RiskTrack, delay: .1f);
            t.RiskFill = UiInkKit.LightLayer(arc, "Огонь", "light_ring", 1f, 10f, new Vector2(0f, .5f), .2f);
            Half(t.RiskFill, 0f);

            var anvilArt = AssetDatabase.LoadAssetAtPath<Texture2D>(AnvilArtPath);
            RectTransform anvil = TopLeft(Node("Наковальня", page), AnvilX - 220f, 380f, 440f, 280f);
            t.Anvil = anvil.gameObject.AddComponent<RawImage>();
            t.Anvil.raycastTarget = false;
            t.Anvil.material = UiInkKit.Art;
            UiInkKit.Inked(t.Anvil, new Vector2(.5f, 1f), .08f);
            SetAnvil(t, anvilArt);

            Image glow = UiInkKit.LightAt(page, "Свет вещи", "light_glow", new Vector2(0f, 1f), new Vector2(AnvilX, -470f), new Vector2(380f, 240f), .45f);
            glow.raycastTarget = false;
            RectTransform item = CampInkParts.CenterAt(Node("Вещь на наковальне", page), AnvilX, 452f, 200f, 200f);
            t.AnvilItem = item.gameObject.AddComponent<Image>();
            t.AnvilItem.preserveAspect = true;
            t.AnvilItem.raycastTarget = false;
            t.AnvilItem.material = UiInkKit.Art;
            t.AnvilItem.enabled = false;
            UiInkKit.Inked(t.AnvilItem, delay: .15f);

            InkMedal medal = CampInkParts.IconMedallion(page, "Медальон вещи", AnvilX, 520f, 196f, null, false, clickable: false);
            t.ItemMedal = Medal(medal);
            RectTransform inside = Stretch(Node("Вещь", medal.Root.Find("Маска")), 196f * .12f);
            t.ItemMedalArt = inside.gameObject.AddComponent<Image>();
            t.ItemMedalArt.preserveAspect = true;
            t.ItemMedalArt.raycastTarget = false;
            t.ItemMedalArt.material = UiInkKit.Art;
            t.ItemMedalArt.enabled = false;
            UiInkKit.Inked(t.ItemMedalArt, delay: .15f);
            t.ItemMedal.Root.SetActive(anvilArt == null);

            t.Embers = UiInkKit.Embers(page, "Искры", new Vector2(0f, 1f), new Vector2(AnvilX, -500f), new Vector2(340f, 260f), 2.5f).gameObject;

            t.ItemName = CampInkParts.Text(page, "Название вещи", "Выбери вещь", AnvilX - 220f, 662f, 440f, 36f, FontRole.Heading, TextStep.Heading,
                Role.Text, TextAlignmentOptions.Center);
            Fit(t.ItemName, T.Size(TextStep.Heading), T.Size(TextStep.Body));
            t.ItemName.textWrappingMode = TextWrappingModes.NoWrap;
            t.ItemMeta = CampInkParts.Text(page, "Редкость вещи", "", AnvilX - 220f, 698f, 440f, 22f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Center);

            // Отметки попыток: до 4 (у эпической); трещина — знак трещины, потраченная — огненное кольцо, свободная — тихое.
            t.Pips = new CampTemperMedal[CampTemperRules.MaxPips];
            Texture crack = CampInkParts.KitTexture("rift");
            for (int i = 0; i < t.Pips.Length; i++)
            {
                float x = CampTemperRules.PipX(i, t.Pips.Length, AnvilX, PipStep);
                InkMedal pip = CampInkParts.IconMedallion(page, "Попытка " + (i + 1), x, PipY, PipSize, crack, true, clickable: false);
                Tint(pip.Art, Role.Bad);
                t.Pips[i] = Medal(pip);
            }
            t.AttemptsLabel = CampInkParts.Text(page, "Попытки", "", AnvilX - 220f, 770f, 440f, 24f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Center);
        }

        /// <summary>Дуга: Filled Radial360 от левого края по часовой — через верх; 0,5 — вся верхняя половина круга.</summary>
        static void Half(Image image, float fill)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Left;
            image.fillClockwise = true;
            image.fillAmount = fill;
        }

        /// <summary>Рисунок наковальни по пропорции файла (не выше 280); нет файла — рисунок выключен, вещь в медальоне.</summary>
        static void SetAnvil(CampTemperScreen t, Texture2D art)
        {
            t.Anvil.texture = art;
            t.Anvil.enabled = art != null;
            if (art == null) return;
            // Вписать в 440×280 с сохранением пропорций, низом к той же линии. Раньше узкая высокая картинка (рендер наковальни
            // 06.10 — 612×713) упиралась в 280 по высоте, а ширина оставалась 440 — наковальню сплющивало вдвое.
            float aspect = art.width / Mathf.Max(1f, art.height);
            float w = 440f, h = w / aspect;
            if (h > 280f) { h = 280f; w = h * aspect; }
            TopLeft(t.Anvil.rectTransform, AnvilX - w * .5f, 380f + (280f - h), w, h);
        }

        /// <summary>
        /// Колонка свойств: заголовок, строка «Вместо: …», шесть карточек (неявное обычной и аффиксы, три варианта или три
        /// грани), строка сердец в вещи.
        /// </summary>
        static void BuildProperties(RectTransform page, CampTemperScreen t)
        {
            // Нить заголовка начинается за самой длинной подписью, которую ставит вид.
            t.PropsCaption = CampInkParts.SectionHeader(page, "Подпись свойств", "Выбери одно из трёх", PropsX, 194f, PropsW, TextStep.Heading, false);
            t.PropsCaption.text = "Свойства предмета";
            t.Replaced = CampInkParts.Text(page, "Вместо", "", PropsX, 232f, PropsW, 24f, FontRole.Body, TextStep.Caption, Role.TextMuted);
            t.Cards = new CampTemperCard[TemperCards];
            for (int i = 0; i < TemperCards; i++) t.Cards[i] = TemperCard(page, i, PropsX, CardsY + i * CardStep, PropsW, CardH);
            t.HeartsLine = CampInkParts.Text(page, "Сердца в вещи", "", PropsX, CardsY + TemperCards * CardStep - 2f, PropsW, 28f, FontRole.Body, TextStep.Body,
                Role.TextMuted);
        }

        /// <summary>
        /// Карточка свойства: медальон со знаком стата в тёмном кольце, имя (Body), значение «12 → 15» (Heading), пометка справа
        /// («предел»). Выбранная — полоса дыма, нить и огненное кольцо знака; наведение — тонкая тлеющая кромка вокруг знака и
        /// нить по низу (UiHoverMotion, без подъёма яркости). Между карточками — тихая нить.
        /// </summary>
        static CampTemperCard TemperCard(RectTransform page, int index, float x, float y, float w, float h)
        {
            var card = new CampTemperCard();
            RectTransform root = TopLeft(Node("Свойство " + (index + 1), page), x, y, w, h);
            RectTransform chosen = Stretch(Node("Выбрано", root));
            UiInkKit.SmokeLayer(chosen, "Подложка", "smoke_plate", .85f, 14f, 6f, deep: true);
            UiInkKit.LightAt(chosen, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(w + 30f, 26f), .75f);
            chosen.gameObject.SetActive(false);
            card.Chosen = chosen.gameObject;
            TopLeft(UiInkKit.Divider(root, "Черта", w - 40f, false, .18f), 40f, h + 1f, w - 40f, 16f);

            float medal = h - 12f, cx = medal * .5f + 6f;
            RectTransform hover = Stretch(Node("Наведение", root));
            var group = hover.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            UiInkKit.LightAt(hover, "Кромка", "light_ring", new Vector2(0f, .5f), new Vector2(cx, 0f), new Vector2(medal * 1.22f, medal * 1.22f), .4f, delay: .2f);
            UiInkKit.LightAt(hover, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(w, 24f), .3f, delay: .15f);

            card.Icon = Medal(CampInkParts.IconMedallion(root, "Знак", cx, h * .5f, medal, null, true, clickable: false));
            float tx = medal + 22f;
            card.Name = CampInkParts.Text(root, "Имя", "Свойство", tx, 4f, w - tx - 8f, 26f, FontRole.Body, TextStep.Body, Role.TextMuted);
            Fit(card.Name, T.Size(TextStep.Body), T.Size(TextStep.Caption));
            card.Name.textWrappingMode = TextWrappingModes.NoWrap;
            card.Value = CampInkParts.Text(root, "Значение", "", tx, 30f, w - tx - 8f, 36f, FontRole.Heading, TextStep.Heading, Role.Text);
            card.Value.textWrappingMode = TextWrappingModes.NoWrap;
            card.Note = CampInkParts.Text(root, "Пометка", "", w - 150f, 34f, 142f, 28f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.MidlineRight);

            card.Button = CampInkParts.Clickable(root);
            var motion = root.gameObject.AddComponent<UiHoverMotion>();
            motion.HighlightGroup = group;
            motion.HoverScale = 1.02f;
            motion.PulseMin = .6f;
            return card;
        }

        static CampTemperMedal Medal(InkMedal medal) => new CampTemperMedal
        {
            Root = medal.Root.gameObject, Button = medal.Button, Art = medal.Art, Ring = medal.Ring,
            Fire = medal.Fire != null ? medal.Fire.gameObject : null, Picked = medal.Picked != null ? medal.Picked.gameObject : null,
            Locked = medal.Locked, LockLabel = medal.LockLabel,
        };

        // ---------------------------------------------------------------- арт без пересборки

        /// <summary>
        /// Положили наковальню (Assets/UI/CampShops/anvil.png) или знаки граней и валют — подставить их в готовый префаб без
        /// миграции и без потери ручных правок. Вид в игре и так грузит Resources/UI/CampCurrency и UI/HeartFacets первым.
        /// </summary>
        [MenuItem("Разлом/UI/Кузница Эни: подставить арт наковальни и знаков")]
        static void RefreshTemperArt()
        {
            if (!EnsureMigrated()) { Debug.LogWarning("[ui-kit] Нет префаба окон лагеря " + PrefabPath); return; }
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponent<CampShopView>();
                CampTemperScreen t = view != null ? view.Temper : null;
                if (t == null || t.Root == null) { Debug.LogWarning("[ui-kit] В префабе нет кузницы Эни (v4)"); return; }
                var anvil = AssetDatabase.LoadAssetAtPath<Texture2D>(AnvilArtPath);
                if (t.Anvil != null) SetAnvil(t, anvil);
                if (t.ItemMedal?.Root != null) t.ItemMedal.Root.SetActive(anvil == null);
                Texture[] currency = TemperCurrencyIcons();
                for (int i = 0; i < currency.Length; i++)
                {
                    if (i < t.PriceIcons.Length && t.PriceIcons[i] != null) { t.PriceIcons[i].texture = currency[i]; t.PriceIcons[i].enabled = currency[i] != null; }
                    if (i < t.WalletIcons.Length && t.WalletIcons[i] != null) { t.WalletIcons[i].texture = currency[i]; t.WalletIcons[i].enabled = currency[i] != null; }
                }
                for (int i = 0; i < FacetArt.Length && i < t.FacetIcons.Length; i++) t.FacetIcons[i] = CampInkParts.ArtOr(FacetArt[i], FacetPlaceholders[i]);
                EditorUtility.SetDirty(view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Кузница Эни: арт подставлен (наковальня " + (anvil != null ? "есть" : "нет — вещь в медальоне") + ").");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
