using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Окна лагерных NPC на паке «Ночная акварель» (23 сентября 2026):
    /// Resources/UI/Prefabs/CampShopsWc.prefab — кузнец, торговец, алхимик и подпись над NPC.
    /// Раскладка по концепту final-P4/4-smith.png: рисованный портрет NPC слева поверх
    /// лагеря, справа заголовок с линией, вкладки, сетка вещей и карточка выбранной вещи.
    /// Портреты и значки — ART/UI/camp-shops-2026-09-23 → Assets/UI/CampShops.
    /// Префаб создаётся, только если его нет: ручные правки не затираются.
    /// </summary>
    public static partial class CampShopsWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampShopsWc.prefab";
        const string ArtFolder = "Assets/UI/CampShops/";
        const string RunIconFolder = "Assets/UI/RunIcons/";

        static UiTheme T => UiTheme.Current;

        // Колонки содержимого справа от портрета, в единицах холста 1920×1080 от левого верхнего угла.
        const float Left = 660f, Right = 1880f, Mid = (Left + Right) * .5f;
        const float GridX = 660f, GridY = 380f, CellSize = 66f, CellStep = 74f;
        const float WornY = 244f, WornSize = 82f, WornStep = 96f;
        const float DetailX = 1300f, DetailW = 560f;

        [MenuItem("Разлом/UI/Собрать окна лагеря «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Окна лагеря", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        public static string Build(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return PrefabPath;
            UiThemeBuilder.Ensure(false);
            EnsurePrefabs();
            GameObject root = Layout();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return PrefabPath;
        }

        static Texture Art(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + name + ".png")
                                           ?? AssetDatabase.LoadAssetAtPath<Texture2D>(RunIconFolder + name + ".png");

        static RawImage Pic(RectTransform parent, string name, string art, float x, float y, float w, float h)
        {
            RectTransform rect = TopLeft(Node(name, parent), x, y, w, h);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = Art(art);
            raw.raycastTarget = false;
            return raw;
        }

        static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            return Label(box, "Надпись", text, font, size, role, align);
        }

        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>Кнопка по всему элементу: невидимый ловец мыши и (по желанию) оранжевая рамка наведения.</summary>
        static Button Clickable(RectTransform rect, Sprite hover, float grow)
        {
            Image catcher = Layer(rect, "Ловец", T.Pixel, Role.Panel, 0f);
            catcher.raycastTarget = true;
            catcher.transform.SetAsFirstSibling();
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = catcher;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            if (hover != null)
            {
                var motion = rect.gameObject.AddComponent<UiHoverMotion>();
                motion.Highlight = Layer(rect, "Наведение", hover, Role.Accent, 1f);
                motion.HoverScale = grow;
            }
            return button;
        }

        static Button KitButton(RectTransform parent, string prefab, string name, string text, float x, float y, float w, float h, float font, out TMP_Text label)
        {
            RectTransform rect = Place(prefab, parent, name);
            TopLeft(rect, x, y, w, h);
            label = rect.Find("Надпись").GetComponent<TMP_Text>();
            label.text = text;
            label.fontSize = font;
            label.characterSpacing = 1f;
            rect.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            var button = rect.GetComponent<Button>();
            NoNavigation(button);
            return button;
        }

        static void ButtonArt(Button button, string art, float size)
        {
            // Медальон на левом краю кнопки: тёмный круг с серебряным ободком, значок крупно поверх.
            // На оранжевой заливке сама картинка не читалась (владелец, 23 сентября).
            var rect = (RectTransform)button.transform;
            float disc = size + 12f;
            RectTransform medal = At(Node("Значок", rect), new Vector2(0f, .5f), new Vector2(disc * .32f, 0f), new Vector2(disc, disc));
            Image glow = Layer(medal, "Тень", RoundShadow, Role.Veil, .8f, 10f);
            glow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(medal, "Круг", T.CircleFill, Role.Panel, 1f);
            Layer(medal, "Ободок", T.CircleFrame, Role.PanelLine, .9f);
            RectTransform pic = Stretch(Node("Картинка", medal), -4f);
            var raw = pic.gameObject.AddComponent<RawImage>();
            raw.texture = Art(art);
            raw.raycastTarget = false;
            var label = rect.Find("Надпись").GetComponent<TMP_Text>();
            label.rectTransform.offsetMin = new Vector2(disc * .82f + 6f, label.rectTransform.offsetMin.y);
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampShopsWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 105;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            var view = root.AddComponent<CampShopView>();
            var rect = (RectTransform)root.transform;

            BuildHint(rect, view);
            RectTransform windows = Stretch(Node("Окна", rect));
            view.Smith = BuildShop(windows, "Кузнец", "portrait-smith", true);
            view.Trader = BuildShop(windows, "Торговец", "portrait-trader", false);
            view.Alchemist = BuildAlchemist(windows);
            return root;
        }

        // ---------------------------------------------------------------- подпись над NPC
        static void BuildHint(RectTransform root, CampShopView view)
        {
            RectTransform hint = Node("Подпись над NPC", root);
            hint.anchorMin = hint.anchorMax = new Vector2(.5f, .5f);
            hint.pivot = new Vector2(.5f, 0f);
            hint.sizeDelta = new Vector2(240f, 108f);
            view.Hint = hint;

            // Плашка с именем и тем, кто это: читается на траве, камне и у огня.
            RectTransform plate = TopLeft(Node("Имя", hint), 20f, 0f, 200f, 58f);
            Image shadow = Layer(plate, "Тень", T.GlowSmall, Role.Veil, .8f, 18f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(plate, "Заливка", T.FillSmall, Role.Panel, .92f);
            Layer(plate, "Свет по кромке", T.HighlightSmall, Role.Highlight);
            Layer(plate, "Рамка", T.FrameSmall, Role.PanelLine, .8f);
            Mark(plate, "Камень", T.Gem, Role.Accent, 1f, new Vector2(.5f, 1f), Vector2.zero, 14f);
            view.HintTitle = Text(plate, "Имя", "Эни", 0f, 5f, 200f, 30f, FontRole.Heading, 24f, Role.Text, TextAlignmentOptions.Center);
            view.HintTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.HintTitle.characterSpacing = 2f;
            view.HintRole = Text(plate, "Кто это", "Кузнец", 0f, 34f, 200f, 18f, FontRole.Body, 14f, Role.Accent, TextAlignmentOptions.Center);

            // Действие — готовая плашка пака: клавиша, ромб, подпись.
            RectTransform prompt = Place("InteractPrompt", hint, "Действие");
            prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 1f);
            prompt.pivot = new Vector2(.5f, 1f);
            prompt.anchoredPosition = new Vector2(0f, -64f);
            prompt.localScale = Vector3.one * .62f;
            view.HintKeyCap = (RectTransform)prompt.Find("Клавиша");
            view.HintKey = prompt.Find("Клавиша/Буква").GetComponent<TMP_Text>();
            view.HintNote = prompt.Find("Действие").GetComponent<TMP_Text>();
            view.HintNote.text = "Поговорить";
        }

        /// <summary>Материал шрифта с тёмным контуром и тенью (подпись над NPC поверх мира). Создаётся один раз ассетом.</summary>
        static Material Outlined(TMP_FontAsset font, string name)
        {
            string path = ArtFolder + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null || font == null) return existing;
            var m = new Material(font.material) { name = name };
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.03f, .04f, .07f, 1f));
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, .22f);
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .75f));
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, .4f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .6f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.5f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------------------------------------------------------------- общий каркас окна
        static RectTransform Screen(RectTransform windows, string name, string portrait, out CanvasGroup group, out RawImage picture)
        {
            RectTransform screen = Stretch(Node(name, windows));
            group = screen.gameObject.AddComponent<CanvasGroup>();
            // Лагерь остаётся виден: вуаль тише, чем на экранах забега, и темнее справа, где текст.
            Image veil = Layer(screen, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            Layer(screen, "Глубина", T.Pixel, Role.Panel, .32f);
            Layer(screen, "Виньетка", T.VeilRadial, Role.Veil, 1f);
            // Мягкое тёмное пятно под содержимым: текст читается и на светлой земле, жёсткого края нет.
            Image under = Layer(screen, "Тень под содержимым", T.Blob, Role.Veil, 1f);
            under.preserveAspect = false;
            TopLeft(under.rectTransform, Left - 260f, -120f, Right - Left + 520f, 1320f);
            // Подложка-сцена за NPC (кузня, прилавок, котёл): края растворены в самой картинке.
            RectTransform scene = TopLeft(Node("Сцена за портретом", screen), 0f, 0f, 720f, 1080f);
            var sceneArt = scene.gameObject.AddComponent<RawImage>();
            sceneArt.texture = Art("scene-" + portrait.Substring("portrait-".Length));
            sceneArt.raycastTarget = false;
            sceneArt.enabled = sceneArt.texture != null;
            RectTransform face = Node("Портрет", screen);
            face.anchorMin = face.anchorMax = new Vector2(0f, 0f);
            face.pivot = new Vector2(.5f, 0f);
            face.anchoredPosition = new Vector2(340f, 0f);
            picture = face.gameObject.AddComponent<RawImage>();
            picture.texture = Art(portrait);
            picture.raycastTarget = false;
            float height = 900f;
            face.sizeDelta = picture.texture != null ? new Vector2(height * picture.texture.width / picture.texture.height, height) : new Vector2(675f, height);
            return screen;
        }

        /// <summary>Закрыть окно: крестик пака в правом верхнем углу, одинаковый у всех NPC.</summary>
        static Button Closer(RectTransform screen)
        {
            RectTransform close = Place("CloseButton", screen, "Закрыть");
            TopLeft(close, 1830f, 38f, 52f, 52f);
            close.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.08f;
            var button = close.GetComponent<Button>();
            NoNavigation(button);
            return button;
        }

        /// <summary>Подсказка внизу окна: плашка Esc и «Закрыть». Кнопки геймпада не показываем, пока он не выбран.</summary>
        static void CloseHint(RectTransform screen)
        {
            RectTransform row = TopLeft(Node("Клавиши", screen), Mid - 100f, 1004f, 200f, 36f);
            RectTransform cap = Keycap(row, "Клавиша", "Esc", 36f);
            TopLeft(cap, 40f, 0f, 50f, 36f);
            cap.Find("Буква").GetComponent<TMP_Text>().fontSize = 16f;
            Text(row, "Подпись", CampServiceText.Get("close.action"), 100f, 0f, 100f, 36f, FontRole.Body, 18f, Role.TextMuted);
        }

        /// <summary>Облачко реплики под портретом: имя NPC и текст; пустой текст прячет облачко.</summary>
        static TMP_Text Speech(RectTransform screen, string speaker)
        {
            // Облачко над головой портрета, хвостик вниз к нему, имя на ленточке. Выпрыгивание
            // и печать по буквам — CampSpeechBubble.
            RectTransform box = TopLeft(Node("Реплика", screen), 70f, 52f, 470f, 96f);
            var bubble = box.gameObject.AddComponent<CampSpeechBubble>();
            bubble.Group = box.gameObject.AddComponent<CanvasGroup>();
            bubble.Group.blocksRaycasts = false;
            RectTransform body = Stretch(Node("Облачко", box));
            body.pivot = new Vector2(.45f, 0f);
            body.anchoredPosition = Vector2.zero;
            bubble.Body = body;
            Image shadow = Layer(body, "Тень", T.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            // Хвостик — ромб под облачком: верхняя половина уходит под заливку, видна острая нижняя.
            RectTransform tail = At(Node("Хвостик", body), new Vector2(.45f, 0f), new Vector2(0f, 0f), new Vector2(38f, 38f));
            Layer(tail, "Заливка", T.DiamondFill, Role.Panel, 1f);
            Layer(tail, "Оправа", T.DiamondFrameSmall, Role.PanelLine, .9f);
            Layer(body, "Заливка", T.Fill, Role.Panel, .97f);
            Layer(body, "Тень снизу", T.ShadeSprite, Role.Veil, .45f);
            Layer(body, "Свет по кромке", T.HighlightSprite, Role.Highlight);
            Layer(body, "Рамка", T.Frame, Role.PanelLine, .9f);

            // Имя — оранжевая ленточка на верхней кромке.
            RectTransform ribbon = TopLeft(Node("Имя", body), 24f, -16f, 150f, 32f);
            Image ribbonGlow = Layer(ribbon, "Свечение", T.ButtonGlow, Role.Accent, .25f, 12f);
            ribbonGlow.raycastTarget = false;
            Layer(ribbon, "Заливка", T.TagFill, Role.Accent, 1f);
            Layer(ribbon, "Ободок", T.TagFrame, Role.TextOnAccent, .45f);
            bubble.Speaker = Label(ribbon, "Надпись", speaker, FontRole.Heading, 20f, Role.TextOnAccent, TextAlignmentOptions.Center, 1f);
            bubble.Speaker.textWrappingMode = TextWrappingModes.NoWrap;
            bubble.Line = Text(body, "Текст", "", 26f, 22f, 418f, 64f, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.MidlineLeft);
            bubble.Line.enableAutoSizing = true;
            bubble.Line.fontSizeMin = 15f;
            bubble.Line.fontSizeMax = 21f;
            return bubble.Line;
        }

        static TMP_Text ScreenTitle(RectTransform screen, string text)
        {
            TMP_Text title = Text(screen, "Заголовок", text, Left, 34f, Right - Left, 76f, FontRole.Heading, 58f, Role.Text, TextAlignmentOptions.Center);
            title.characterSpacing = 3f;
            TopLeft(Place("Divider", screen, "Линия"), Mid - 300f, 112f, 600f, 16f);
            return title;
        }

        static TMP_Text Wallet(RectTransform screen, string name, string art, float x, Role color, out GameObject group)
        {
            RectTransform box = TopLeft(Node(name, screen), x, 40f, 130f, 50f);
            group = box.gameObject;
            Pic(box, "Значок", art, 0f, 3f, 44f, 44f);
            TMP_Text value = Text(box, "Число", "0", 50f, 0f, 80f, 50f, FontRole.Heading, 30f, color);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            return value;
        }

        static Button TabButton(RectTransform screen, string name, float x)
        {
            RectTransform tab = Place("Tab", screen, name);
            TopLeft(tab, x, 138f, 240f, 52f);
            tab.Find("Надпись").GetComponent<TMP_Text>().fontSize = 24f;
            return Clickable(tab, null, 1f);
        }

        /// <summary>Ячейка вещи: элемент Cell пака, картинка вещи спрайтом, уровень в углу, рамка выбора.</summary>
        static CampShopCell Cell(RectTransform parent, string name, float x, float y, float size, bool clickable)
        {
            RectTransform rect = Place("Cell", parent, name);
            TopLeft(rect, x, y, size, size);
            rect.Find("Предмет").gameObject.SetActive(false);
            var cell = rect.gameObject.AddComponent<CampShopCell>();
            RectTransform art = Stretch(Node("Иконка", rect), size * .1f);
            art.SetSiblingIndex(rect.Find("Рамка").GetSiblingIndex());
            cell.Icon = art.gameObject.AddComponent<Image>();
            cell.Icon.preserveAspect = true;
            cell.Icon.raycastTarget = false;
            cell.Icon.enabled = false;
            cell.Rarity = rect.GetComponent<WcRarity>();
            // Выбор и наведение меняют единственную рамку ячейки (WcSlotState), без второго контура.
            cell.State = rect.GetComponent<WcSlotState>();
            Image empty = Mark(rect, "Пусто", T.DiamondMedium, Role.TextMuted, .3f, new Vector2(.5f, .5f), Vector2.zero, size * .2f);
            cell.Empty = empty.gameObject;
            RectTransform level = Node("Уровень", rect);
            level.anchorMin = level.anchorMax = new Vector2(1f, 0f);
            level.pivot = new Vector2(1f, 0f);
            level.anchoredPosition = new Vector2(-4f, 1f);
            level.sizeDelta = new Vector2(size * .6f, size * .3f);
            cell.Level = Label(level, "Надпись", "", FontRole.Body, Mathf.Max(13f, size * .2f), Role.Text, TextAlignmentOptions.BottomRight);
            if (clickable)
            {
                cell.Button = Clickable(rect, null, 1f);
                var motion = rect.gameObject.AddComponent<UiHoverMotion>();
                motion.HoverScale = 1.06f;
            }
            return cell;
        }

        // ---------------------------------------------------------------- кузнец и торговец
        static CampShopScreen BuildShop(RectTransform windows, string name, string portrait, bool smith)
        {
            var s = new CampShopScreen();
            RectTransform screen = Screen(windows, name, portrait, out s.Group, out s.Portrait);
            s.Title = ScreenTitle(screen, name);
            s.Shards = Wallet(screen, "Осколки", "shards", 1530f, Role.Text, out s.ShardsGroup);
            s.Gold = Wallet(screen, "Золото", "gold", 1670f, Role.Coins, out _);
            s.Back = Closer(screen);
            s.Tabs = new[] { TabButton(screen, "Вкладка 1", Mid - 250f), TabButton(screen, "Вкладка 2", Mid + 10f) };

            // Левая колонка: сверху надетое (владелец 23 сентября: «видно надетые вещи»;
            // кузнец их перековывает, торговец показывает для сравнения), ниже сетка 8 × 6.
            RectTransform wornCaption = Place("SectionHeader", screen, "Подпись надетого");
            TopLeft(wornCaption, GridX, 204f, 8 * CellStep - 8f, 30f);
            s.WornCaption = wornCaption.Find("Надпись").GetComponent<TMP_Text>();
            s.WornCaption.text = "Надето";
            s.WornCaption.fontSize = 19f;
            s.WornCaption.characterSpacing = 4f;
            s.Worn = new CampShopCell[4];
            string[] wornNames = { "Оружие", "Броня", "Кольцо", "Талисман" };
            for (int i = 0; i < 4; i++)
                s.Worn[i] = Cell(screen, "Надето: " + wornNames[i], GridX + i * WornStep, WornY, WornSize, true);

            RectTransform caption = Place("SectionHeader", screen, "Подпись сетки");
            TopLeft(caption, GridX, GridY - 44f, 8 * CellStep - 8f, 30f);
            s.GridCaption = caption.Find("Надпись").GetComponent<TMP_Text>();
            s.GridCaption.fontSize = 19f;
            s.GridCaption.characterSpacing = 4f;
            RectTransform grid = Node("Сетка", screen);
            Stretch(grid);
            s.Cells = new CampShopCell[48];
            for (int i = 0; i < 48; i++)
                s.Cells[i] = Cell(grid, "Ячейка " + (i + 1), GridX + i % 8 * CellStep, GridY + i / 8 * CellStep, CellSize, true);
            s.Info = Text(screen, "Пояснение", "", GridX, GridY + 6 * CellStep + 4f, 8 * CellStep - 8f, 50f, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            if (!smith)
            {
                s.Extra = KitButton(screen, "ButtonSecondary", "Обновить товары", "Обновить товары", GridX, GridY + 6 * CellStep + 58f, 8 * CellStep - 8f, 54f, 21f, out s.ExtraLabel);
                ButtonArt(s.Extra, "refresh", 46f);
            }

            // Правая колонка: выбранная вещь.
            s.Item = Cell(screen, "Выбранная вещь", DetailX, 206f, 132f, false);
            s.ItemName = Text(screen, "Название", "Выбери предмет", DetailX + 152f, 214f, DetailW - 152f, 50f, FontRole.Heading, 34f, Role.Text, TextAlignmentOptions.BottomLeft);
            s.ItemName.enableAutoSizing = true;
            s.ItemName.fontSizeMin = 22f;
            s.ItemName.fontSizeMax = 34f;
            s.ItemMeta = Text(screen, "Уровень и перековки", "", DetailX + 152f, 270f, DetailW - 152f, 56f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            TopLeft(Place("DividerPlain", screen, "Линия под вещью"), DetailX, 356f, DetailW, 16f);

            if (smith)
            {
                s.Rows = new Button[6];
                s.RowLabels = new TMP_Text[6];
                for (int i = 0; i < 6; i++)
                {
                    RectTransform row = Place("ListItem", screen, "Свойство " + (i + 1));
                    TopLeft(row, DetailX, 380f + i * 50f, DetailW, 46f);
                    s.RowLabels[i] = row.Find("Надпись").GetComponent<TMP_Text>();
                    s.RowLabels[i].fontSize = 20f;
                    s.Rows[i] = Clickable(row, null, 1f);
                }
            }
            else
            {
                s.Detail = Text(screen, "Свойства", "", DetailX + 8f, 384f, DetailW - 8f, 306f, FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.TopLeft);
                s.Detail.lineSpacing = 4f;
                s.Detail.enableAutoSizing = true;
                s.Detail.fontSizeMin = 14f;
                s.Detail.fontSizeMax = 19f;
            }
            TopLeft(Place("DividerPlain", screen, "Линия над ценой"), DetailX, 694f, DetailW, 16f);
            s.Preview = Text(screen, "Результат", "", DetailX, 716f, DetailW, 36f, FontRole.Body, 21f, Role.Text);

            RectTransform price = TopLeft(Node("Стоимость", screen), DetailX, 756f, DetailW, 46f);
            s.Price = price.gameObject;
            Text(price, "Подпись", CampServiceText.Get(smith ? "smith.cost" : "trader.price"), 0f, 0f, 150f, 46f, FontRole.Body, 19f, Role.TextMuted);
            Pic(price, "Значок золота", "gold", 150f, 2f, 42f, 42f);
            s.PriceGold = Text(price, "Золото", "0", 198f, 0f, 90f, 46f, FontRole.Heading, 28f, Role.Coins);
            RectTransform shards = TopLeft(Node("Осколки", price), 300f, 0f, 150f, 46f);
            s.PriceShardsGroup = shards.gameObject;
            Pic(shards, "Значок осколков", "shards", 0f, 2f, 42f, 42f);
            s.PriceShards = Text(shards, "Число", "0", 48f, 0f, 90f, 46f, FontRole.Heading, 28f, Role.Text);
            s.Note = Text(screen, "Примечание", "", DetailX, 804f, DetailW, 56f, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);

            s.Action = KitButton(screen, "ButtonPrimary", "Действие", smith ? "Перековать" : "Купить", DetailX + 40f, 870f, DetailW - 40f, 62f, 26f, out s.ActionLabel);
            ButtonArt(s.Action, smith ? "reforge" : "sell", 58f);

            s.Message = Speech(screen, name);
            s.Speaker = s.Message.transform.parent.parent.Find("Имя/Надпись").GetComponent<TMP_Text>();
            CloseHint(screen);
            return s;
        }

        // ---------------------------------------------------------------- алхимик
        static CampAlchemyScreen BuildAlchemist(RectTransform windows)
        {
            var s = new CampAlchemyScreen();
            RectTransform screen = Screen(windows, "Алхимик", "portrait-alchemist", out s.Group, out s.Portrait);
            s.Title = ScreenTitle(screen, "Алхимик");
            s.Gold = Wallet(screen, "Золото", "gold", 1670f, Role.Coins, out _);
            s.Back = Closer(screen);
            s.Tabs = new[] { TabButton(screen, "Вкладка 1", Mid - 250f), TabButton(screen, "Вкладка 2", Mid + 10f) };
            s.Tabs[0].GetComponentInChildren<TMP_Text>().text = "Зелья";
            s.Tabs[1].GetComponentInChildren<TMP_Text>().text = "Рецепты";

            // Вкладка «Зелья»: лавка, 2 × 3 карточки.
            RectTransform potions = Stretch(Node("Зелья", screen));
            s.PotionsPage = potions.gameObject;
            s.Potions = new CampPotionCard[6];
            const float w = 590f, h = 226f, gap = 20f;
            for (int i = 0; i < 6; i++)
            {
                // Строка — размер и вид: здоровье слева, лавидий справа; снизу зелья по рецептам.
                int col = i < 4 ? i / 2 : i - 4, row = i < 4 ? i % 2 : 2;
                s.Potions[i] = PotionCard(potions, i, Left + 10f + col * (w + gap), 206f + row * (h + 16f), w, h);
            }

            // Вкладка «Рецепты»: новые зелья за заказы Лео или ресурсы. Сейчас их два.
            RectTransform recipes = Stretch(Node("Рецепты", screen));
            s.RecipesPage = recipes.gameObject;
            s.Recipes = new CampPotionCard[2];
            for (int i = 0; i < 2; i++)
                s.Recipes[i] = RecipeCard(recipes, i, Left + 10f, 216f + i * 262f, Right - Left - 20f, 236f);
            recipes.gameObject.SetActive(false);

            // Отказ — системной строкой под карточками, не репликой (AGENTS/CAMP-NPC-DIALOGUE.md).
            s.Status = Text(screen, "Состояние", "", Left + 10f, 928f, Right - Left - 20f, 34f, FontRole.Body, 18f, Role.Bad, TextAlignmentOptions.Center);
            s.Message = Speech(screen, "Алхимик");
            s.Speaker = s.Message.transform.parent.parent.Find("Имя/Надпись").GetComponent<TMP_Text>();
            CloseHint(screen);
            return s;
        }

        /// <summary>Карточка рецепта: бутылка, что даёт зелье, условие и ход заказа, взять/сдать и обмен.</summary>
        static CampPotionCard RecipeCard(RectTransform page, int index, float x, float y, float w, float h)
        {
            RectTransform card = TopLeft(Node("Рецепт " + (index + 1), page), x, y, w, h);
            Image shadow = Layer(card, "Тень", T.Glow, Role.Veil, .8f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(card, "Заливка", T.Fill, Role.Panel);
            Layer(card, "Тень снизу", T.ShadeSprite, Role.Veil, .5f);
            Layer(card, "Свет по кромке", T.HighlightSprite, Role.Highlight);
            Layer(card, "Рамка", T.Frame, Role.PanelLine, .9f);
            var recipe = card.gameObject.AddComponent<CampPotionCard>();
            recipe.Chosen = Layer(card, "Открыт", T.FrameBold, Role.Rare, .9f).gameObject;
            recipe.Chosen.SetActive(false);

            bool health = index == 0;
            Image halo = Mark(card, "Свет за бутылкой", T.Blob, health ? Role.Health : Role.Lavidium, .25f, new Vector2(0f, .5f), new Vector2(120f, 0f), 220f);
            halo.preserveAspect = false;
            RectTransform bottle = At(Node("Бутылка", card), new Vector2(0f, .5f), new Vector2(120f, 0f), new Vector2(176f, 176f));
            recipe.Art = bottle.gameObject.AddComponent<RawImage>();
            recipe.Art.texture = Resources.Load<Texture2D>("UI/Items/potion_" + (health ? "health" : "lavidium") + "_large");
            recipe.Art.raycastTarget = false;

            const float tx = 250f;
            recipe.Name = Text(card, "Название", "Рецепт", tx, 22f, 560f, 40f, FontRole.Heading, 32f, Role.Text);
            recipe.Name.textWrappingMode = TextWrappingModes.NoWrap;
            recipe.Effect = Text(card, "Действие", "", tx, 66f, 560f, 30f, FontRole.Body, 18f, Role.TextMuted);
            TopLeft(Place("DividerPlain", card, "Линия"), tx, 104f, w - tx - 30f, 16f);
            recipe.LockedLabel = Text(card, "Условие", "", tx, 128f, 520f, 84f, FontRole.Body, 19f, Role.Text, TextAlignmentOptions.TopLeft);
            recipe.Stock = Text(card, "Состояние", "", w - 330f, 26f, 300f, 34f, FontRole.Heading, 22f, Role.Accent, TextAlignmentOptions.MidlineRight);
            recipe.OrderMain = KitButton(card, "ButtonPrimary", "Заказ", "Взять заказ", w - 540f, 150f, 250f, 58f, 21f, out recipe.OrderMainLabel);
            recipe.OrderAlt = KitButton(card, "ButtonSecondary", "Обмен", "Отдать 12 осколков", w - 280f, 150f, 250f, 58f, 18f, out recipe.OrderAltLabel);
            return recipe;
        }

        static CampPotionCard PotionCard(RectTransform screen, int index, float x, float y, float w, float h)
        {
            RectTransform card = TopLeft(Node("Зелье " + (index + 1), screen), x, y, w, h);
            Image shadow = Layer(card, "Тень", T.Glow, Role.Veil, .8f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(card, "Заливка", T.Fill, Role.Panel);
            Layer(card, "Тень снизу", T.ShadeSprite, Role.Veil, .5f);
            Layer(card, "Свет по кромке", T.HighlightSprite, Role.Highlight);
            Layer(card, "Рамка", T.Frame, Role.PanelLine, .9f);
            var potion = card.gameObject.AddComponent<CampPotionCard>();
            potion.Chosen = Layer(card, "Выбрано", T.FrameBold, Role.Accent, 1f).gameObject;

            bool health = index == 0 || index == 1 || index == 4;
            bool large = index == 1 || index == 3;
            Image halo = Mark(card, "Свет за бутылкой", T.Blob, health ? Role.Health : Role.Lavidium, .22f, new Vector2(0f, .5f), new Vector2(96f, 6f), 170f);
            halo.preserveAspect = false;
            float art = large || index >= 4 ? 150f : 118f;
            RectTransform bottle = At(Node("Бутылка", card), new Vector2(0f, .5f), new Vector2(96f, 6f), new Vector2(art, art));
            potion.Art = bottle.gameObject.AddComponent<RawImage>();
            // Бутылки первого набора (Resources/UI/Items); у зелий заказов пока большие бутылки.
            potion.Art.texture = Resources.Load<Texture2D>("UI/Items/potion_" + (health ? "health" : "lavidium") + (large || index >= 4 ? "_large" : "_small"));
            potion.Art.raycastTarget = false;

            const float tx = 188f;
            potion.Name = Text(card, "Название", "Зелье", tx, 20f, w - tx - 20f, 38f, FontRole.Heading, 27f, Role.Text);
            potion.Name.textWrappingMode = TextWrappingModes.NoWrap;
            potion.Effect = Text(card, "Действие", "", tx, 60f, w - tx - 20f, 48f, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            potion.Stock = Text(card, "Запас", "", tx, 110f, w - tx - 20f, 28f, FontRole.Body, 18f, Role.TextMuted);
            potion.Buy = KitButton(card, "ButtonPrimary", "Купить", "Купить", tx, 150f, 190f, 52f, 20f, out potion.BuyLabel);
            ButtonArt(potion.Buy, "gold", 34f);
            potion.Select = KitButton(card, "ButtonSecondary", "Выбрать", "В быстрый слот", tx + 200f, 150f, w - tx - 220f, 52f, 18f, out potion.SelectLabel);

            RectTransform locked = Stretch(Node("Закрыто", card));
            potion.Locked = locked.gameObject;
            Layer(locked, "Вуаль", T.Fill, Role.Veil, .55f);
            // Закрытое зелье отсылает к вкладке «Рецепты», где живёт заказ Лео.
            potion.LockedLabel = Text(locked, "Рецепт", "Нужен рецепт · вкладка «Рецепты»", tx, 150f, w - tx - 20f, 52f, FontRole.Body, 18f, Role.Accent);
            locked.gameObject.SetActive(false);
            return potion;
        }
    }
}
