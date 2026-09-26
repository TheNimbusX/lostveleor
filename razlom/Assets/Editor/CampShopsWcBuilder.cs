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
    /// Окна лагерных NPC (раскладка 23 сентября 2026, тогда на паке «Ночная акварель»):
    /// Resources/UI/Prefabs/CampShopsWc.prefab — кузнец, торговец, алхимик и подпись над NPC.
    /// Раскладка по концепту final-P4/4-smith.png: рисованный портрет NPC слева поверх
    /// лагеря, справа заголовок с линией, вкладки, сетка вещей и карточка выбранной вещи.
    /// Портреты и значки — ART/UI/camp-shops-2026-09-23 → Assets/UI/CampShops.
    /// Префаб создаётся, только если его нет: ручные правки не затираются.
    ///
    /// Материал — «Дым и свет», как у боевого HUD (владелец 26 сентября: «перевести вообще всё
    /// на новую версию»). Раскладка прежняя, сменился материал: вместо серебряных плашек пака —
    /// глубокий дым под содержимым, нити света вместо линий, ячейки и кнопки UiInkKit. Префабы
    /// пака (Place) больше не используются. Окно проявляет UiInkGroup экрана — коротко и почти
    /// без огня; облачко реплики и подпись над NPC — свои группы без огня (частые всплывашки).
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

        /// <summary>
        /// Значок (золото, осколки): с 26 сентября знаки — белые силуэты, краску даёт тема (кремовый
        /// текст). Материал рисунка вещи — без дымки, проявляется вместе с окном.
        /// </summary>
        static RawImage Pic(RectTransform parent, string name, string art, float x, float y, float w, float h, Role role = Role.Text)
        {
            RectTransform rect = TopLeft(Node(name, parent), x, y, w, h);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = Art(art);
            raw.raycastTarget = false;
            Tint(raw, role);
            raw.material = UiInkKit.Art;
            UiInkKit.Inked(raw, delay: .12f);
            return raw;
        }

        /// <summary>Надпись в своей рамке; <paramref name="ink"/> — проявление по буквам (у реплики свой набор букв).</summary>
        static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool ink = true)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            return ink ? UiInkKit.Label(box, "Надпись", text, font, size, role, align) : Label(box, "Надпись", text, font, size, role, align);
        }

        /// <summary>Надпись прямо на узле: её ширину берёт раскладка строки (подпись над NPC).</summary>
        static TMP_Text TextOn(RectTransform rect, string text, FontRole font, float size, Role role)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            UiInkKit.Revealed(label, .12f);
            return label;
        }

        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>
        /// Кнопка по всему элементу «Дыма и света» (вкладка, строка): прозрачный ловец мыши на самом узле
        /// (UiInkKit.HitArea) — картинки дыма мышь не ловят.
        /// </summary>
        static Button Clickable(RectTransform rect)
        {
            Image hit = UiInkKit.HitArea(rect);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            return button;
        }

        /// <summary>
        /// Кнопка «Дыма и света»: основная — оранжевый мазок, вторичная — тёмный мазок с нитью.
        /// Наведение (UiHoverMotion) и мышь (прозрачный ловец) ставит UiInkKit.Button.
        /// </summary>
        static Button InkButton(RectTransform parent, bool primary, string name, string text, float x, float y, float w, float h, float font, out TMP_Text label)
        {
            RectTransform rect = UiInkKit.Button(parent, name, text, primary, new Vector2(w, h), font);
            TopLeft(rect, x, y, w, h);
            label = rect.Find("Надпись").GetComponent<TMP_Text>();
            label.characterSpacing = 1f;
            var button = rect.GetComponent<Button>();
            NoNavigation(button);
            return button;
        }

        static void ButtonArt(Button button, string art, float size)
        {
            // Медальон на левом краю кнопки: тёмный круг в клубе дыма, тонкое кольцо, значок поверх.
            // На оранжевом мазке сам знак без круга не читался (владелец, 23 сентября).
            var rect = (RectTransform)button.transform;
            float disc = size + 12f;
            RectTransform medal = At(Node("Значок", rect), new Vector2(0f, .5f), new Vector2(disc * .32f, 0f), new Vector2(disc, disc));
            UiInkKit.SmokeLayer(medal, "Дым", "soft_blot", .9f, disc * .14f, disc * .14f, deep: true);
            Image circle = Layer(medal, "Круг", T.CircleFill, Role.SmokeDeep, .92f);
            circle.material = UiInkKit.Plain;
            UiInkKit.Inked(circle, delay: .08f);
            Image ring = Layer(medal, "Ободок", T.CircleFrame, Role.PanelLine, .5f);
            ring.material = UiInkKit.Plain;
            UiInkKit.Inked(ring, delay: .12f);
            RectTransform pic = Stretch(Node("Картинка", medal), disc * .18f);
            var raw = pic.gameObject.AddComponent<RawImage>();
            raw.texture = Art(art);
            raw.raycastTarget = false;
            Tint(raw, Role.Text);
            raw.material = UiInkKit.Art;
            UiInkKit.Inked(raw, delay: .15f);
            var label = rect.Find("Надпись").GetComponent<TMP_Text>();
            label.rectTransform.offsetMin = new Vector2(disc * .82f + 6f, label.rectTransform.offsetMin.y);
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampShopsWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 105;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
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

            // Всплывает при каждом наведении на NPC: коротко и без огня (владелец отверг красную вспышку).
            UiInkKit.Group(hint, UiInkGroup.Sweep.FromCenter, .3f, .08f).Burn = 0f;

            // Имя и кто это — на мягкой дымной капсуле, под именем нить света: читается на траве,
            // камне и у огня.
            RectTransform plate = TopLeft(Node("Имя", hint), 20f, 0f, 200f, 58f);
            UiInkKit.SmokeLayer(plate, "Тень", "soft_blot", .9f, 34f, 16f, deep: true);
            UiInkKit.SmokeLayer(plate, "Дым", "smoke_plate", 1f, 56f, 22f, deep: true);
            UiInkKit.LightAt(plate, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, 2f), new Vector2(210f, 28f), .5f);
            view.HintTitle = Text(plate, "Имя", "Эни", 0f, 5f, 200f, 30f, FontRole.Heading, 24f, Role.Text, TextAlignmentOptions.Center);
            view.HintTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.HintTitle.characterSpacing = 2f;
            view.HintRole = Text(plate, "Кто это", "Кузнец", 0f, 34f, 200f, 18f, FontRole.Body, 14f, Role.Accent, TextAlignmentOptions.Center);

            // Действие — малая дымная капсула: клавиша «Дыма и света» и подпись. Ширина — по содержимому
            // (клавиша E — круг, ПКМ — капсула; её ширину подгоняет CampServicesView).
            RectTransform prompt = Node("Действие", hint);
            prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 1f);
            prompt.pivot = new Vector2(.5f, 1f);
            prompt.anchoredPosition = new Vector2(0f, -66f);
            prompt.sizeDelta = new Vector2(170f, 38f);
            foreach (Image smoke in new[]
                     {
                         UiInkKit.SmokeLayer(prompt, "Тень", "soft_blot", .85f, 16f, 8f, deep: true),
                         UiInkKit.SmokeLayer(prompt, "Дым", "smoke_plate", 1f, 30f, 12f, deep: true),
                     })
                smoke.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = prompt.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(10, 14, 4, 4);
            row.spacing = 9f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            prompt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            const float key = 30f;
            view.HintKeyCap = UiInkKit.Keycap(prompt, "Клавиша", "E", key);
            var capSize = view.HintKeyCap.gameObject.AddComponent<LayoutElement>();
            capSize.preferredWidth = capSize.minWidth = view.HintKeyCap.sizeDelta.x;
            capSize.preferredHeight = capSize.minHeight = key;
            view.HintKey = view.HintKeyCap.Find("Буква").GetComponent<TMP_Text>();
            view.HintNote = TextOn(Node("Подпись", prompt), "Поговорить", FontRole.Body, 17f, Role.Text);
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
            // Вуаль ловит мышь: клик по окну не проваливается в ходьбу по лагерю.
            Image veil = Layer(screen, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            Layer(screen, "Глубина", T.Pixel, Role.Panel, .32f);
            Layer(screen, "Виньетка", T.VeilRadial, Role.Veil, 1f);
            // Под содержимым — глубокий дым без краёв (как колонна паузы и итогов): текст читается и на
            // светлой земле. Мягкое пятно на всю правую часть и рваный клуб поверх, края тают в лагерь.
            UiInkKit.SmokeAt(screen, "Тень под содержимым", "soft_blot", new Vector2(0f, 1f), new Vector2(Mid, -540f),
                new Vector2(Right - Left + 520f, 1320f), .7f, origin: new Vector2(.8f, .5f), deep: true);
            UiInkKit.SmokeAt(screen, "Дым под содержимым", "smoke_blot_1", new Vector2(0f, 1f), new Vector2(Mid, -520f),
                new Vector2(Right - Left + 360f, 1180f), .8f, origin: new Vector2(.5f, .85f), delay: -.1f, deep: true);
            // Подложка-сцена за NPC (кузня, прилавок, котёл): края растворены в самой картинке.
            RectTransform scene = TopLeft(Node("Сцена за портретом", screen), 0f, 0f, 720f, 1080f);
            var sceneArt = scene.gameObject.AddComponent<RawImage>();
            sceneArt.texture = Art("scene-" + portrait.Substring("portrait-".Length));
            sceneArt.raycastTarget = false;
            sceneArt.enabled = sceneArt.texture != null;
            sceneArt.material = UiInkKit.Art;
            UiInkKit.Inked(sceneArt, new Vector2(0f, .5f));
            RectTransform face = Node("Портрет", screen);
            face.anchorMin = face.anchorMax = new Vector2(0f, 0f);
            face.pivot = new Vector2(.5f, 0f);
            face.anchoredPosition = new Vector2(340f, 0f);
            picture = face.gameObject.AddComponent<RawImage>();
            picture.texture = Art(portrait);
            picture.raycastTarget = false;
            // Портрет проявляется снизу вверх, как дым; рисунок — без дымки материала.
            picture.material = UiInkKit.Art;
            UiInkKit.Inked(picture, new Vector2(.5f, 0f), .05f);
            float height = 900f;
            face.sizeDelta = picture.texture != null ? new Vector2(height * picture.texture.width / picture.texture.height, height) : new Vector2(675f, height);
            // Окно открывают часто: короткое мягкое проявление, огонь по кромке едва тлеет.
            UiInkGroup appear = UiInkKit.Group(screen, UiInkGroup.Sweep.LeftToRight, .36f, .2f);
            appear.Burn = .2f;
            appear.HideDuration = .16f;
            return screen;
        }

        /// <summary>Закрыть окно: круглый крестик «Дыма и света» в правом верхнем углу, одинаковый у всех NPC.</summary>
        static Button Closer(RectTransform screen)
        {
            RectTransform close = UiInkKit.CloseButton(screen, "Закрыть", 52f);
            TopLeft(close, 1830f, 38f, 52f, 52f);
            var button = close.GetComponent<Button>();
            NoNavigation(button);
            return button;
        }

        /// <summary>Подсказка внизу окна: клавиша Esc и «Закрыть». Кнопки геймпада не показываем, пока он не выбран.</summary>
        static void CloseHint(RectTransform screen)
        {
            RectTransform row = TopLeft(Node("Клавиши", screen), Mid - 100f, 1004f, 200f, 36f);
            // Клавиша сама берёт ширину по подписи (Esc — капсула); правый край — там же, где был.
            RectTransform cap = UiInkKit.Keycap(row, "Клавиша", "Esc", 32f);
            TopLeft(cap, 90f - cap.sizeDelta.x, 2f, cap.sizeDelta.x, 32f);
            Text(row, "Подпись", CampServiceText.Get("close.action"), 100f, 0f, 100f, 36f, FontRole.Body, 18f, Role.TextMuted);
        }

        /// <summary>Облачко реплики под портретом: имя NPC и текст; пустой текст прячет облачко.</summary>
        static TMP_Text Speech(RectTransform screen, string speaker)
        {
            // Облачко над головой портрета: клуб глубокого дыма, мягкий хвостик из двух дымных капель
            // вниз к голове, имя антиквой на нити света. Выпрыгивание и печать по буквам — CampSpeechBubble;
            // проявление дыма — своя группа без огня (облачко появляется с каждой новой репликой).
            RectTransform box = TopLeft(Node("Реплика", screen), 70f, 52f, 470f, 96f);
            var bubble = box.gameObject.AddComponent<CampSpeechBubble>();
            bubble.Group = box.gameObject.AddComponent<CanvasGroup>();
            bubble.Group.blocksRaycasts = false;
            RectTransform body = Stretch(Node("Облачко", box));
            body.pivot = new Vector2(.45f, 0f);
            body.anchoredPosition = Vector2.zero;
            bubble.Body = body;
            // Хвостик — две мягкие капли дыма под облачком, к голове портрета (ромба больше нет).
            RectTransform tail = At(Node("Хвостик", body), new Vector2(.45f, 0f), new Vector2(0f, 0f), new Vector2(60f, 60f));
            UiInkKit.SmokeAt(tail, "Капля", "soft_blot", new Vector2(.5f, .5f), new Vector2(8f, -14f), new Vector2(46f, 40f), .95f,
                origin: new Vector2(.5f, 1f), deep: true);
            UiInkKit.SmokeAt(tail, "Капля меньше", "soft_blot", new Vector2(.5f, .5f), new Vector2(22f, -44f), new Vector2(22f, 20f), .9f,
                origin: new Vector2(.5f, 1f), delay: .06f, deep: true);
            // Подложка всплывашки (как у подсказок HUD); нить по низу не нужна — нить под именем.
            Object.DestroyImmediate(UiInkKit.Plate(body, small: true).gameObject);

            // Имя — антиквой на верхней кромке, под ним нить света.
            RectTransform ribbon = TopLeft(Node("Имя", body), 24f, -16f, 170f, 32f);
            UiInkKit.LightAt(ribbon, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -1f), new Vector2(230f, 28f), .75f,
                origin: new Vector2(0f, .5f), delay: .1f);
            bubble.Speaker = UiInkKit.Label(ribbon, "Надпись", speaker, FontRole.Heading, 21f, Role.Text, TextAlignmentOptions.Center, 1.5f, 1f, .08f);
            bubble.Speaker.textWrappingMode = TextWrappingModes.NoWrap;
            // Текст реплики печатается по буквам сам (CampSpeechBubble) — без второго проявления.
            bubble.Line = Text(body, "Текст", "", 26f, 22f, 418f, 64f, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.MidlineLeft, false);
            UiInkGroup ink = UiInkKit.Group(box, UiInkGroup.Sweep.FromCenter, .32f, .1f);
            ink.Burn = 0f;
            bubble.Ink = ink;
            bubble.Line.enableAutoSizing = true;
            bubble.Line.fontSizeMin = 15f;
            bubble.Line.fontSizeMax = 21f;
            return bubble.Line;
        }

        static TMP_Text ScreenTitle(RectTransform screen, string text)
        {
            TMP_Text title = Text(screen, "Заголовок", text, Left, 34f, Right - Left, 76f, FontRole.Heading, 58f, Role.Text, TextAlignmentOptions.Center);
            title.characterSpacing = 3f;
            TopLeft(UiInkKit.Divider(screen, "Линия", 600f, true, .7f), Mid - 300f, 112f, 600f, 16f);
            return title;
        }

        /// <summary>
        /// Заголовок раздела «Дыма и света» (надетое, сетка): подпись с разрядкой и нить света под ней
        /// на всю ширину колонки — вместо ромбов и серебряной черты пака.
        /// </summary>
        static TMP_Text SectionHeader(RectTransform screen, string name, string text, float x, float y, float w)
        {
            RectTransform header = TopLeft(Node(name, screen), x, y, w, 30f);
            TMP_Text label = UiInkKit.Label(header, "Надпись", text, FontRole.Heading, 19f, Role.Text, TextAlignmentOptions.MidlineLeft, 4f, .9f, .1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            UiInkKit.LightAt(header, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -3f), new Vector2(w + 40f, 24f), .4f,
                origin: new Vector2(0f, .5f));
            return label;
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

        /// <summary>Вкладка «Дыма и света» (UiInkKit.Tab): подпись и нить света под выбранной; мышь ловит сам узел.</summary>
        static Button TabButton(RectTransform screen, string name, float x)
        {
            RectTransform tab = UiInkKit.Tab(screen, name, "Вкладка", false, 240f, 52f, 24f);
            TopLeft(tab, x, 138f, 240f, 52f);
            return Clickable(tab);
        }

        /// <summary>
        /// Ячейка вещи «Дыма и света» (UiInkKit.Cell): дымная лунка, тонкая скруглённая рамка цвета
        /// редкости, мягкий свет редкости за вещью; картинка вещи спрайтом, уровень в углу.
        /// </summary>
        /// <param name="small">Малая ячейка (ряд «Надето»): дым мягче, угол рамки круглее.</param>
        static CampShopCell Cell(RectTransform parent, string name, float x, float y, float size, bool clickable, bool small = false)
        {
            RectTransform rect = UiInkKit.Cell(parent, name, size, small);
            TopLeft(rect, x, y, size, size);
            // Своя картинка вещи — спрайтом (CampShopCell.Icon); «Предмет» ячейки не нужен.
            rect.Find("Предмет").gameObject.SetActive(false);
            var cell = rect.gameObject.AddComponent<CampShopCell>();
            RectTransform art = Stretch(Node("Иконка", rect), size * .1f);
            art.SetSiblingIndex(rect.Find("Рамка").GetSiblingIndex());
            cell.Icon = art.gameObject.AddComponent<Image>();
            cell.Icon.preserveAspect = true;
            cell.Icon.raycastTarget = false;
            cell.Icon.enabled = false;
            cell.Icon.material = UiInkKit.Art;
            UiInkKit.Inked(cell.Icon, delay: .12f);
            cell.Rarity = rect.GetComponent<WcRarity>();
            // Выбор и наведение меняют единственную рамку ячейки и её свет (WcSlotState), без второго контура.
            cell.State = rect.GetComponent<WcSlotState>();
            // Пустая ячейка — едва заметная точка (круг, а не ромб: ромб остался только мелким светом).
            Image empty = Mark(rect, "Пусто", T.CircleFill, Role.TextMuted, .22f, new Vector2(.5f, .5f), Vector2.zero, size * .1f);
            empty.material = UiInkKit.Plain;
            UiInkKit.Inked(empty, delay: .1f);
            cell.Empty = empty.gameObject;
            RectTransform level = Node("Уровень", rect);
            level.anchorMin = level.anchorMax = new Vector2(1f, 0f);
            level.pivot = new Vector2(1f, 0f);
            level.anchoredPosition = new Vector2(-4f, 1f);
            level.sizeDelta = new Vector2(size * .6f, size * .3f);
            cell.Level = UiInkKit.Label(level, "Надпись", "", FontRole.Body, Mathf.Max(13f, size * .2f), Role.Text, TextAlignmentOptions.BottomRight, delay: .15f);
            if (clickable)
            {
                // Мышь ловит «Ловец» самой ячейки: второй ловец не нужен.
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = rect.Find("Ловец").GetComponent<Image>();
                button.transition = Selectable.Transition.None;
                NoNavigation(button);
                cell.Button = button;
                var motion = rect.gameObject.AddComponent<UiHoverMotion>();
                motion.HoverScale = 1.06f;
            }
            // Выбранная вещь справа не нажимается: свет наведения на ней обманывал бы.
            else rect.Find("Ловец").GetComponent<Image>().raycastTarget = false;
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
            s.WornCaption = SectionHeader(screen, "Подпись надетого", "Надето", GridX, 204f, 8 * CellStep - 8f);
            s.Worn = new CampShopCell[4];
            string[] wornNames = { "Оружие", "Броня", "Кольцо", "Талисман" };
            for (int i = 0; i < 4; i++)
                s.Worn[i] = Cell(screen, "Надето: " + wornNames[i], GridX + i * WornStep, WornY, WornSize, true, true);

            s.GridCaption = SectionHeader(screen, "Подпись сетки", "Предметы", GridX, GridY - 44f, 8 * CellStep - 8f);
            RectTransform grid = Node("Сетка", screen);
            Stretch(grid);
            s.Cells = new CampShopCell[48];
            for (int i = 0; i < 48; i++)
                s.Cells[i] = Cell(grid, "Ячейка " + (i + 1), GridX + i % 8 * CellStep, GridY + i / 8 * CellStep, CellSize, true);
            s.Info = Text(screen, "Пояснение", "", GridX, GridY + 6 * CellStep + 4f, 8 * CellStep - 8f, 50f, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            if (!smith)
            {
                s.Extra = InkButton(screen, false, "Обновить товары", "Обновить товары", GridX, GridY + 6 * CellStep + 58f, 8 * CellStep - 8f, 54f, 21f, out s.ExtraLabel);
                ButtonArt(s.Extra, "refresh", 46f);
            }

            // Правая колонка: выбранная вещь.
            s.Item = Cell(screen, "Выбранная вещь", DetailX, 206f, 132f, false);
            s.ItemName = Text(screen, "Название", "Выбери предмет", DetailX + 152f, 214f, DetailW - 152f, 50f, FontRole.Heading, 34f, Role.Text, TextAlignmentOptions.BottomLeft);
            s.ItemName.enableAutoSizing = true;
            s.ItemName.fontSizeMin = 22f;
            s.ItemName.fontSizeMax = 34f;
            s.ItemMeta = Text(screen, "Уровень и перековки", "", DetailX + 152f, 270f, DetailW - 152f, 56f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            TopLeft(UiInkKit.Divider(screen, "Линия под вещью", DetailW, false, .5f), DetailX, 356f, DetailW, 16f);

            if (smith)
            {
                s.Rows = new Button[6];
                s.RowLabels = new TMP_Text[6];
                for (int i = 0; i < 6; i++)
                {
                    RectTransform row = UiInkKit.ListRow(screen, "Свойство " + (i + 1), "Свойство", false, DetailW, 46f);
                    TopLeft(row, DetailX, 380f + i * 50f, DetailW, 46f);
                    s.RowLabels[i] = row.Find("Надпись").GetComponent<TMP_Text>();
                    s.RowLabels[i].fontSize = 20f;
                    s.Rows[i] = Clickable(row);
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
            TopLeft(UiInkKit.Divider(screen, "Линия над ценой", DetailW, false, .5f), DetailX, 694f, DetailW, 16f);
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

            s.Action = InkButton(screen, true, "Действие", smith ? "Перековать" : "Купить", DetailX + 40f, 870f, DetailW - 40f, 62f, 26f, out s.ActionLabel);
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

        /// <summary>
        /// Подложка карточки зелья или рецепта «Дыма и света»: мягкая дымная плита без рамки, чуть шире
        /// карточки (между карточками 16–20 единиц — соседние клубы не сливаются в одно облако), и
        /// тихая нить света по низу.
        /// </summary>
        static void CardPlate(RectTransform card, float w)
        {
            UiInkKit.SmokeLayer(card, "Тень", "soft_blot", .9f, 24f, 18f, deep: true);
            UiInkKit.SmokeLayer(card, "Дым", "smoke_plate", 1f, 46f, 28f, origin: new Vector2(0f, .5f), deep: true);
            UiInkKit.LightAt(card, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(w * .7f, 28f), .3f);
        }

        /// <summary>
        /// Отметка выбранного (зелье в быстром слоте) или открытого (рецепт) — тихая: слабый ореол цвета
        /// отметки только за бутылкой (чуть шире её собственного сияния) и нить по низу ярче обычной.
        /// Свет на всю карточку заливал текст цветом (26 сентября: рецепт «Живица» весь бирюзовый,
        /// выбранные зелья — красные) — его больше нет.
        /// </summary>
        /// <param name="bottle">Центр бутылки от левого края карточки по середине высоты.</param>
        /// <param name="size">Размер рисунка бутылки: ореол выглядывает из-за неё тонким кольцом.</param>
        static GameObject CardChosen(RectTransform card, string name, Role role, float w, Vector2 bottle, float size)
        {
            RectTransform chosen = Stretch(Node(name, card));
            Image glow = UiInkKit.LightAt(chosen, "Свет за бутылкой", "soft_blot", new Vector2(0f, .5f), bottle,
                new Vector2(size * 1.35f, size * 1.35f), 1f, delay: .2f);
            Tint(glow, role, .12f);
            // Нить своего тёплого цвета, без краски: оранжевый спрайт под бирюзой редкого выходил болотным.
            // Свет складывается с обычной нитью карточки (.3): вместе ≈ .7 и шире — заметно, но не пламя.
            UiInkKit.LightAt(chosen, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(w * .85f, 30f), .4f);
            return chosen.gameObject;
        }

        /// <summary>
        /// Слабое сияние цвета зелья за бутылкой (огненного кольца у зелий нет, владелец 26 сентября).
        /// Размер — чуть больше бутылки, свет тихий: пятно в полкарточки читалось красной кляксой.
        /// </summary>
        static void BottleGlow(RectTransform card, bool health, Vector2 at, float size, float alpha)
        {
            Image halo = UiInkKit.LightAt(card, "Свет за бутылкой", "soft_blot", new Vector2(0f, .5f), at, new Vector2(size, size), 1f, delay: .15f);
            Tint(halo, health ? Role.Health : Role.Lavidium, alpha);
        }

        /// <summary>Бутылка (Resources/UI/Items): рисунок вещи без дымки материала, проявляется с карточкой.</summary>
        static RawImage Bottle(RectTransform rect, string key)
        {
            var art = rect.gameObject.AddComponent<RawImage>();
            art.texture = Resources.Load<Texture2D>("UI/Items/" + key);
            art.raycastTarget = false;
            art.material = UiInkKit.Art;
            UiInkKit.Inked(art, delay: .1f);
            return art;
        }

        /// <summary>Карточка рецепта: бутылка, что даёт зелье, условие и ход заказа, взять/сдать и обмен.</summary>
        static CampPotionCard RecipeCard(RectTransform page, int index, float x, float y, float w, float h)
        {
            RectTransform card = TopLeft(Node("Рецепт " + (index + 1), page), x, y, w, h);
            CardPlate(card, w);
            var recipe = card.gameObject.AddComponent<CampPotionCard>();
            Vector2 at = new Vector2(120f, 0f);
            const float art = 176f;
            recipe.Chosen = CardChosen(card, "Открыт", Role.Rare, w, at, art);
            recipe.Chosen.SetActive(false);

            bool health = index == 0;
            BottleGlow(card, health, at, art * 1.08f, .12f);
            RectTransform bottle = At(Node("Бутылка", card), new Vector2(0f, .5f), at, new Vector2(art, art));
            recipe.Art = Bottle(bottle, "potion_" + (health ? "health" : "lavidium") + "_large");

            const float tx = 250f;
            recipe.Name = Text(card, "Название", "Рецепт", tx, 22f, 560f, 40f, FontRole.Heading, 32f, Role.Text);
            recipe.Name.textWrappingMode = TextWrappingModes.NoWrap;
            recipe.Effect = Text(card, "Действие", "", tx, 66f, 560f, 30f, FontRole.Body, 18f, Role.TextMuted);
            TopLeft(UiInkKit.Divider(card, "Линия", w - tx - 30f, false, .45f), tx, 104f, w - tx - 30f, 16f);
            recipe.LockedLabel = Text(card, "Условие", "", tx, 128f, 520f, 84f, FontRole.Body, 19f, Role.Text, TextAlignmentOptions.TopLeft);
            recipe.Stock = Text(card, "Состояние", "", w - 330f, 26f, 300f, 34f, FontRole.Heading, 22f, Role.Accent, TextAlignmentOptions.MidlineRight);
            recipe.OrderMain = InkButton(card, true, "Заказ", "Взять заказ", w - 540f, 150f, 250f, 58f, 21f, out recipe.OrderMainLabel);
            recipe.OrderAlt = InkButton(card, false, "Обмен", "Отдать 12 осколков", w - 280f, 150f, 250f, 58f, 18f, out recipe.OrderAltLabel);
            return recipe;
        }

        static CampPotionCard PotionCard(RectTransform screen, int index, float x, float y, float w, float h)
        {
            RectTransform card = TopLeft(Node("Зелье " + (index + 1), screen), x, y, w, h);
            CardPlate(card, w);
            var potion = card.gameObject.AddComponent<CampPotionCard>();

            bool health = index == 0 || index == 1 || index == 4;
            bool large = index == 1 || index == 3;
            float art = large || index >= 4 ? 150f : 118f;
            Vector2 at = new Vector2(96f, 6f);
            potion.Chosen = CardChosen(card, "Выбрано", Role.Accent, w, at, art);
            BottleGlow(card, health, at, art * 1.08f, .11f);
            RectTransform bottle = At(Node("Бутылка", card), new Vector2(0f, .5f), at, new Vector2(art, art));
            // Бутылки первого набора (Resources/UI/Items); у зелий заказов пока большие бутылки.
            potion.Art = Bottle(bottle, "potion_" + (health ? "health" : "lavidium") + (large || index >= 4 ? "_large" : "_small"));

            const float tx = 188f;
            potion.Name = Text(card, "Название", "Зелье", tx, 20f, w - tx - 20f, 38f, FontRole.Heading, 27f, Role.Text);
            potion.Name.textWrappingMode = TextWrappingModes.NoWrap;
            potion.Effect = Text(card, "Действие", "", tx, 60f, w - tx - 20f, 48f, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            potion.Stock = Text(card, "Запас", "", tx, 110f, w - tx - 20f, 28f, FontRole.Body, 18f, Role.TextMuted);
            potion.Buy = InkButton(card, true, "Купить", "Купить", tx, 150f, 190f, 52f, 20f, out potion.BuyLabel);
            ButtonArt(potion.Buy, "gold", 34f);
            potion.Select = InkButton(card, false, "Выбрать", "В быстрый слот", tx + 200f, 150f, w - tx - 220f, 52f, 18f, out potion.SelectLabel);

            RectTransform locked = Stretch(Node("Закрыто", card));
            potion.Locked = locked.gameObject;
            // Закрытое — под тёмной дымкой (мягкое пятно без краёв, как тень под текстом).
            UiInkKit.SmokeLayer(locked, "Вуаль", "soft_blot", .7f, 16f, 10f, deep: true);
            // Закрытое зелье отсылает к вкладке «Рецепты», где живёт заказ Лео.
            potion.LockedLabel = Text(locked, "Рецепт", "Нужен рецепт · вкладка «Рецепты»", tx, 150f, w - tx - 20f, 52f, FontRole.Body, 18f, Role.Accent);
            locked.gameObject.SetActive(false);
            return potion;
        }
    }
}
