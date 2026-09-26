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
    /// Экраны забега: Resources/UI/Prefabs/RunHudWc.prefab — выбор награды (три карточки стопкой;
    /// тот же экран — выбор следующей арены), «Заменить артефакт?», замена способности, панель
    /// состояния забега, полоса босса и итоги. RunHud берёт префаб, если он есть; без него — прежний
    /// IMGUI. Префаб создаётся, только если его нет.
    ///
    /// С 26 сентября всё — в материале «Дым и свет» (владелец: «перевести вообще всё на новую
    /// версию»): карточки — клубы глубокого дыма без серебряных рамок, редкость — цветом названия,
    /// строки вида и тонким кольцом света вокруг круглого значка; наведение — тёплый свет.
    /// Кнопки — мазки UiInkKit, полоса босса — мазок кистью, как полосы боевого HUD.
    ///
    /// Первый кадр в игре (26 сентября): огонь на частом экране — лишний. Нити под карточками и
    /// свет наведения — тёплые кремовые, тусклые (красно-оранжевое пятно под текстом карточки
    /// ушло), сияние редкости вдвое слабее. Всё, что внизу экрана, закрывал боевой HUD (нижние
    /// ~150 единиц): подсказки клавиш — мелко под пояснением, «Отказаться» — справа от карточек.
    /// </summary>
    public static partial class RunHudWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/RunHudWc.prefab";

        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);

        [MenuItem("Разлом/UI/Собрать экраны забега «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Экраны забега", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static RectTransform Box(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = position;
            r.sizeDelta = size;
            return r;
        }

        static GameObject Layout()
        {
            var root = new GameObject("RunHudWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            var view = root.AddComponent<RunHudView>();
            var rect = (RectTransform)root.transform;

            BuildStatus(rect, view);
            BuildSurvival(rect, view);
            BuildBoss(rect, view);
            BuildChoice(rect, view);
            BuildArtifactReplace(rect, view);
            BuildReplace(rect, view);
            BuildSummary(rect, view);
            return root;
        }

        /// <summary>Экран поверх игры: вуаль и виньетка, свой CanvasGroup (проявляет RunHudView).</summary>
        static RectTransform Screen(RectTransform root, string name, out CanvasGroup group)
        {
            RectTransform screen = Stretch(Node(name, root));
            group = screen.gameObject.AddComponent<CanvasGroup>();
            Image veil = Layer(screen, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            Layer(screen, "Глубина", T.Pixel, Role.Panel, .35f);
            Layer(screen, "Виньетка", T.VeilRadial, Role.Veil, 1f);
            return screen;
        }

        static TMP_Text Title(RectTransform screen, string text, float y, float size)
        {
            RectTransform box = Box(Node("Заголовок", screen), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -y), new Vector2(1400f, 80f));
            TMP_Text label = UiInkKit.Label(box, "Надпись", text, FontRole.Heading, size, Role.Text, TextAlignmentOptions.Center, 4f, 1f, .05f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>Строка по центру экрана; проявляется по буквам (задержку можно переставить через UiInkKit.Revealed).</summary>
        static TMP_Text Line(RectTransform screen, string name, string text, float y, float size, Role role, bool fromBottom = false)
        {
            Vector2 anchor = fromBottom ? new Vector2(.5f, 0f) : new Vector2(.5f, 1f);
            RectTransform box = Box(Node(name, screen), anchor, new Vector2(.5f, .5f), new Vector2(0f, fromBottom ? y : -y), new Vector2(1200f, 34f));
            return UiInkKit.Label(box, "Надпись", text, FontRole.Body, size, role, TextAlignmentOptions.Center);
        }

        /// <summary>
        /// Экран выбора поверх мира в «Дыме и свете»: мир притемнён, но виден (как у итогов), а
        /// карточки — клубы глубокого, почти чёрного дыма; под плотной вуалью они терялись бы.
        /// Появление — сверху вниз, огонь по кромке слабый и тлеет (экран бывает после каждой
        /// зачистки: владелец 26 сентября назвал быструю красную кромку «странноватой», а после
        /// первого кадра в игре — «успокоить огонь»: кромка едва тлеет).
        /// </summary>
        static RectTransform InkScreen(RectTransform root, string name, out CanvasGroup group)
        {
            RectTransform screen = Screen(root, name, out group);
            screen.Find("Вуаль").GetComponent<ThemeColor>().SetRole(Role.Veil, .7f);
            screen.Find("Глубина").gameObject.SetActive(false);
            UiInkGroup appear = UiInkKit.Group(screen, UiInkGroup.Sweep.TopToBottom, .45f, .5f);
            appear.InkDuration = .75f;
            appear.Curve = UiInkGroup.Easing.Smooth;
            appear.EdgeScale = 1.6f;
            appear.Burn = .15f;
            return screen;
        }

        /// <summary>Подсказка клавиш — мелко и приглушённо сразу под пояснением: внизу экрана её закрывал боевой HUD.</summary>
        static TMP_Text Hint(RectTransform screen, string text, float y)
        {
            RectTransform box = Box(Node("Подсказка", screen), new Vector2(.5f, 1f), Center, new Vector2(0f, -y), new Vector2(1200f, 26f));
            TMP_Text label = UiInkKit.Label(box, "Надпись", text, FontRole.Body, 16f, Role.TextMuted, TextAlignmentOptions.Center, .5f, .8f, .2f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>
        /// Тёплый кремовый свет — как кромка следа досягаемости на земле. Спрайты света набора
        /// (light_thread, light_glow) нарисованы огнём, краска поверх их только умножает, поэтому
        /// кремовый свет — белое пятно soft_blot в материале света.
        /// </summary>
        static readonly Color WarmLight = new Color(1f, .87f, .72f, 1f);

        static Color Warm(float alpha) => new Color(WarmLight.r, WarmLight.g, WarmLight.b, alpha);

        /// <summary>Кремовая нить по низу элемента: сплющенное пятно — тонкая линия с тающими концами.</summary>
        static Image CreamThread(RectTransform parent, string name, float width, float height, float alpha, float delay = .1f)
        {
            Image thread = UiInkKit.LightAt(parent, name, "soft_blot", new Vector2(.5f, 0f), new Vector2(0f, -4f), new Vector2(width, height), 1f, delay: delay);
            thread.color = Warm(alpha);
            return thread;
        }

        /// <summary>
        /// Огненную нить подложки (UiInkKit.Plate) — в тусклую кремовую: под каждой карточкой
        /// частого экрана горела красно-оранжевая линия.
        /// </summary>
        static void CalmThread(Image thread, float width, float alpha)
        {
            thread.sprite = UiInkKit.Sprite("soft_blot");
            thread.color = Warm(alpha);
            thread.rectTransform.sizeDelta = new Vector2(width, 5f);
        }

        /// <summary>Знак забега, который проявляется вместе с экраном (материал без течения).</summary>
        static RawImage InkPic(RectTransform parent, string name, string icon, Vector2 anchor, Vector2 position, float size, float delay = 0f)
        {
            RawImage pic = Pic(parent, name, icon, anchor, position, size);
            pic.material = UiInkKit.Plain;
            UiInkKit.Inked(pic, delay: delay);
            return pic;
        }

        const string IconFolder = "Assets/UI/RunIcons/";

        static Texture Icon(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + name + ".png");

        /// <summary>
        /// Знак забега (Assets/UI/RunIcons, 256 px): центр в точке якоря. С 26 сентября знаки — белые
        /// силуэты в духе wc_stat_* (владелец: рисованные значки не сочетались с проектом), краску
        /// даёт тема — кремовый текст; кому нужен другой цвет (исход итогов), перекрашивает роль.
        /// </summary>
        static RawImage Pic(RectTransform parent, string name, string icon, Vector2 anchor, Vector2 position, float size)
        {
            RectTransform rect = At(Node(name, parent), anchor, position, new Vector2(size, size));
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = Icon(icon);
            raw.raycastTarget = false;
            Tint(raw, Role.Text);
            return raw;
        }

        /// <summary>
        /// Значок слева на кнопке; надпись сдвигается вправо. <paramref name="inked"/> — кнопка «Дыма и
        /// света»: значок проявляется вместе с надписью, а не выскакивает раньше мазка под ним.
        /// </summary>
        static RawImage ButtonIcon(RectTransform button, string icon, float size, bool inked = false, Role role = Role.Text)
        {
            RawImage pic = Pic(button, "Значок", icon, new Vector2(0f, .5f), new Vector2(26f + size * .5f, 0f), size);
            if (role != Role.Text) Tint(pic, role);
            if (inked)
            {
                pic.material = UiInkKit.Plain;
                UiInkKit.Inked(pic, delay: .15f);
            }
            var label = button.GetComponentInChildren<TMP_Text>();
            label.rectTransform.offsetMin = new Vector2(size + 22f, label.rectTransform.offsetMin.y);
            return pic;
        }

        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>
        /// Кнопка на весь элемент «Дыма и света»: мышь ловит прозрачный прямоугольник (картинки дыма
        /// мышь не ловят), наведение — тёплый свет за элементом и яркая нить по низу, а не рамка.
        /// Свет лежит группой «Наведение» сразу за подложкой, под значком и текстом.
        /// Свет — небольшое кремовое пятно за медальоном и названием (<paramref name="glowAnchor"/>,
        /// <paramref name="glowAt"/>, <paramref name="glowSize"/>), не огненное облако во всю карточку:
        /// light_glow на всю карточку лёг под текст красно-оранжевым пятном (кадр в игре 26 сентября).
        /// Нить — та же кремовая, что лежит под элементом, только ярче, с мягким отсветом.
        /// </summary>
        static Button InkClickable(RectTransform rect, float grow, float threadWidth, Vector2 glowAnchor, Vector2 glowAt, Vector2 glowSize)
        {
            Image hit = UiInkKit.HitArea(rect);
            RectTransform hover = Stretch(Node("Наведение", rect));
            var group = hover.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            UiInkKit.LightAt(hover, "Тёплый свет", "soft_blot", glowAnchor, glowAt, glowSize, 1f, delay: .1f).color = Warm(.1f);
            CreamThread(hover, "Отсвет нити", threadWidth, 22f, .1f);
            CreamThread(hover, "Нить", threadWidth, 5f, .55f);

            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            var motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.HighlightGroup = group;
            motion.HoverScale = grow;
            motion.PulseMin = .7f;
            return button;
        }

        /// <summary>
        /// Круглый медальон «Дыма и света» (круг — единственная форма-оправа, владелец 26 сентября):
        /// клуб дыма, тёмный диск, картинка в круглой маске и тонкое кольцо света цвета
        /// <paramref name="ring"/>. Огненного кольца нет — только тонкая линия, свет прибавляется к миру.
        /// </summary>
        static RawImage Medallion(RectTransform parent, string name, Vector2 anchor, Vector2 position, float size, Role ring, float ringAlpha,
            out RectTransform disc, out Image ringImage)
        {
            disc = At(Node(name, parent), anchor, position, new Vector2(size, size));
            UiInkKit.SmokeLayer(disc, "Дым", "smoke_ring", 1f, size * .2f, size * .2f, deep: true);
            Image back = Layer(disc, "Диск", T.CircleFill, Role.SmokeDeep, 1f);
            back.type = Image.Type.Simple;
            back.material = UiInkKit.Plain;
            UiInkKit.Inked(back, delay: .05f);
            // Маска — отдельная невидимая картинка: диск под ней проявляется чернилами, маска — нет.
            RectTransform maskRect = Stretch(Node("Маска", disc), size * .04f);
            var mask = maskRect.gameObject.AddComponent<Image>();
            mask.sprite = T.CircleFill;
            mask.raycastTarget = false;
            maskRect.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = Stretch(Node("Картинка", maskRect)).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.material = UiInkKit.Art;
            UiInkKit.Inked(art, delay: .12f);
            ringImage = Layer(disc, "Кольцо", T.CircleFrame, ring, ringAlpha);
            ringImage.type = Image.Type.Simple;
            ringImage.material = UiInkKit.Light;
            UiInkKit.Inked(ringImage, delay: .2f);
            return art;
        }

        /// <summary>Узел во всю ширину родителя: сверху <paramref name="top"/>, высотой <paramref name="height"/>.</summary>
        static RectTransform TopRow(RectTransform parent, string name, float top, float height)
        {
            RectTransform row = Node(name, parent);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.offsetMin = new Vector2(0f, -top - height);
            row.offsetMax = new Vector2(0f, -top);
            return row;
        }

        /// <summary>Клавиша в углу элемента (правый верхний угол): круг «Дыма и света», длинная подпись — капсула.</summary>
        static TMP_Text CornerKey(RectTransform parent, string key, float size, Vector2 offset)
        {
            RectTransform cap = UiInkKit.Keycap(parent, "Клавиша", key, size);
            cap.anchorMin = cap.anchorMax = new Vector2(1f, 1f);
            cap.pivot = new Vector2(1f, 1f);
            cap.anchoredPosition = offset;
            return cap.Find("Буква").GetComponent<TMP_Text>();
        }

        // ---------------------------------------------------------------- выбор награды
        static void BuildChoice(RectTransform root, RunHudView view)
        {
            RectTransform screen = InkScreen(root, "Выбор награды", out view.Choice);
            InkPic(screen, "Значок", "rift", new Vector2(.5f, 1f), new Vector2(0f, -66f), 92f);
            view.ChoiceTitle = Title(screen, "Выбери награду", 150f, 58f);
            // Огненная нить под заголовком — тусклее: экран частый (владелец: «успокоить огонь»).
            Box(UiInkKit.Divider(screen, "Линия", 860f, true, .35f), new Vector2(.5f, 1f), Center, new Vector2(0f, -212f), new Vector2(860f, 16f));
            view.ChoiceSubtitle = Line(screen, "Пояснение", "Арена зачищена", 242f, 19f, Role.TextMuted);
            // Клавиши — сразу под пояснением: внизу строка ложилась на полосу способностей HUD.
            view.ChoiceHint = Hint(screen, "1  2  3 — выбрать    ·    L — уйти с добычей", 268f);
            view.KindIcons = new[] { Icon("ability"), Icon("talent"), Icon("items"), Icon("rift") };
            // Родник (лечение сразу): белый знак здоровья — краску здоровья даёт RunHud.
            view.SpringIcon = Icon("health") as Texture2D;
            // Выбор следующей арены — тот же экран (RunHud.FillRoute): улучшение, магазин, опасная арена.
            view.RouteIcons = new[] { Icon("talent"), Icon("gold"), Icon("encounter") };

            view.Offers = new RunOfferCard[3];
            for (int i = 0; i < 3; i++)
                view.Offers[i] = OfferCard(screen, i);

            // Награда босса: «Отказаться» — оставить артефакт как есть. На обычных наградах скрыта.
            // Справа от последней карточки, а не под стопкой: низ экрана занят боевым HUD.
            view.Skip = InkButton(screen, "Отказаться", false, new Vector2(.5f, 1f),
                new Vector2(665f, -(CardTop + 2f * CardPitch + CardHeight * .5f)), new Vector2(240f, 52f));
            view.Skip.gameObject.SetActive(false);
        }

        // Стопка карточек чуть плотнее прежней (200 через 220 от 286): последняя кончается на 892,
        // выше боевого HUD (нижние ~150 единиц экрана), с запасом на её нить и дым.
        const float CardTop = 294f, CardPitch = 204f, CardHeight = 190f;

        /// <summary>Кнопка «Дыма и света» (мазок UiInkKit) на месте: основная — оранжевый мазок, вторичная — тёмный с нитью.</summary>
        static UnityEngine.UI.Button InkButton(RectTransform parent, string text, bool primary, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = UiInkKit.Button(parent, text, text, primary, size);
            Box(rect, anchor, Center, position, size);
            var button = rect.GetComponent<UnityEngine.UI.Button>();
            NoNavigation(button);
            return button;
        }

        // ---------------------------------------------------------------- «Заменить артефакт?»
        /// <summary>
        /// Слот артефакта один (владелец, 24 сентября): при занятом слоте выбор нового сначала
        /// спрашивает, менять ли. Старый → новый, «Заменить» и «Оставить» (концепт 2-artifact-sheet).
        /// </summary>
        static void BuildArtifactReplace(RectTransform root, RunHudView view)
        {
            RectTransform shade = Stretch(Node("Заменить артефакт", root));
            view.ArtifactReplace = shade.gameObject.AddComponent<CanvasGroup>();
            Image veil = Layer(shade, "Вуаль", T.Pixel, Role.Veil, .85f);
            veil.raycastTarget = true;

            // Окно — клуб глубокого дыма без рамки; лишний мягкий слой под ним, чтобы карточки награды
            // под окном не просвечивали. Вопрос частый — огня по кромке нет (Burn 0), встаёт от центра.
            RectTransform card = Box(Node("Карточка", shade), Center, Center, Vector2.zero, new Vector2(620f, 360f));
            UiInkKit.Plate(card).color = new Color(1f, 1f, 1f, .35f);
            UiInkKit.SmokeLayer(card, "Подложка", "soft_blot", 1f, 130f, 110f, deep: true).transform.SetAsFirstSibling();
            UiInkGroup appear = UiInkKit.Group(card, UiInkGroup.Sweep.FromCenter, .35f, .12f);
            appear.Burn = 0f;
            RectTransform titleBox = Box(Node("Заголовок", card), new Vector2(.5f, 1f), Center, new Vector2(0f, -46f), new Vector2(560f, 50f));
            UiInkKit.Label(titleBox, "Надпись", "Заменить артефакт?", FontRole.Heading, 32f, Role.Text, TextAlignmentOptions.Center, 1f, 1f, .05f);

            view.ArtifactOld = Medallion(card, "Прежний", new Vector2(.5f, 1f), new Vector2(-120f, -150f), 112f, Role.Unique, .5f, out _, out _);
            view.ArtifactNew = Medallion(card, "Новый", new Vector2(.5f, 1f), new Vector2(120f, -150f), 112f, Role.Unique, .95f, out _, out _);
            RectTransform arrowBox = Box(Node("Стрелка", card), new Vector2(.5f, 1f), Center, new Vector2(0f, -150f), new Vector2(80f, 60f));
            UiInkKit.Label(arrowBox, "Надпись", "→", FontRole.Heading, 44f, Role.Accent, TextAlignmentOptions.Center);

            RectTransform lineBox = Box(Node("Пояснение", card), new Vector2(.5f, 1f), Center, new Vector2(0f, -236f), new Vector2(540f, 44f));
            view.ArtifactReplaceText = UiInkKit.Label(lineBox, "Надпись", "«Прежний» уйдёт, на его место встанет «Новый».", FontRole.Body, 17f, Role.TextMuted,
                TextAlignmentOptions.Center, 0f, 1f, .15f);
            view.ArtifactReplaceText.textWrappingMode = TextWrappingModes.Normal;

            view.ArtifactConfirm = InkButton(card, "Заменить", true, new Vector2(.5f, 0f), new Vector2(-132f, 46f), new Vector2(240f, 54f));
            view.ArtifactKeep = InkButton(card, "Оставить", false, new Vector2(.5f, 0f), new Vector2(132f, 46f), new Vector2(240f, 54f));
            shade.gameObject.SetActive(false);
        }

        static RunOfferCard OfferCard(RectTransform screen, int index)
        {
            // Карточка «Дыма и света»: клуб глубокого дыма без рамки и камня (подложка как у подсказок
            // HUD). Редкость — цветом названия, строкой вида с её значком и тонким кольцом света вокруг
            // круглой картинки; у редкой и выше за картинкой мягкое сияние её цвета.
            RectTransform card = Box(Node("Карточка " + (index + 1), screen), new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -CardTop - index * CardPitch), new Vector2(980f, CardHeight));
            // Нить под карточкой — тусклая кремовая; ярче она только у карточки под мышью (InkClickable).
            CalmThread(UiInkKit.Plate(card), 600f, .18f);
            var offer = card.gameObject.AddComponent<RunOfferCard>();
            // Свет наведения — за медальоном и началом названия.
            offer.Button = InkClickable(card, 1.015f, 720f, new Vector2(0f, .5f), new Vector2(170f, 12f), new Vector2(440f, 230f));

            RectTransform iconBox = At(Node("Значок", card), new Vector2(0f, .5f), new Vector2(100f, 0f), new Vector2(140f, 140f));
            // Сияние редкости — вдвое слабее и теснее к кругу: голубой ореол был тяжёлым.
            Image glow = UiInkKit.LightLayer(iconBox, "Сияние", "soft_blot", 1f, 18f, delay: .2f);
            Tint(glow, Role.Rare, .11f);
            offer.Icon = Medallion(iconBox, "Диск", Center, Vector2.zero, 140f, Role.Common, .9f, out _, out Image ring);

            RectTransform text = Node("Текст", card);
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(196f, 14f);
            text.offsetMax = new Vector2(-72f, -16f);
            // Ритм как у карточки пака: название 0–38, вид 44–72, описание 80–, значение снизу.
            offer.Title = UiInkKit.Label(TopRow(text, "Название", 0f, 38f), "Надпись", "Название", FontRole.Heading, 30f, Role.Text,
                TextAlignmentOptions.TopLeft, 1.5f, 1f, .05f);
            offer.Title.textWrappingMode = TextWrappingModes.NoWrap;
            offer.Title.enableAutoSizing = true;
            offer.Title.fontSizeMin = 20f;
            offer.Title.fontSizeMax = 30f;

            // Вид награды: значок (свиток, кристалл, сумка — ставит RunHud) и подпись цвета редкости.
            RectTransform kind = TopRow(text, "Вид", 44f, 28f);
            offer.KindIcon = InkPic(kind, "Значок", "ability", new Vector2(0f, .5f), new Vector2(13f, 0f), 26f, .1f);
            offer.Kind = UiInkKit.Label(kind, "Надпись", "Способность", FontRole.Body, 17f, Role.Common, TextAlignmentOptions.MidlineLeft, 1f, 1f, .1f);
            offer.Kind.textWrappingMode = TextWrappingModes.NoWrap;
            offer.Kind.rectTransform.offsetMin = new Vector2(34f, 0f);

            RectTransform body = Node("Описание", text);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(0f, 30f);
            body.offsetMax = new Vector2(0f, -80f);
            offer.Description = UiInkKit.Label(body, "Надпись", "Описание в две строки.", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            offer.Description.textWrappingMode = TextWrappingModes.Normal;
            offer.Description.lineSpacing = -6f;
            UiInkKit.Revealed(offer.Description, .15f).Softness = .6f;

            RectTransform valueRow = Node("Значение", text);
            valueRow.anchorMin = Vector2.zero;
            valueRow.anchorMax = new Vector2(1f, 0f);
            valueRow.pivot = Vector2.zero;
            valueRow.offsetMin = Vector2.zero;
            valueRow.offsetMax = new Vector2(0f, 28f);
            offer.ValueLabel = UiInkKit.Label(valueRow, "Подпись", "Урон", FontRole.Body, 20f, Role.TextMuted, delay: .18f);
            offer.ValueLabel.textWrappingMode = TextWrappingModes.NoWrap;
            // Число правее длинной подписи («Лавидий»).
            offer.Value = UiInkKit.Label(valueRow, "Число", "+15", FontRole.Body, 22f, Role.Accent, delay: .2f);
            offer.Value.textWrappingMode = TextWrappingModes.NoWrap;
            offer.Value.rectTransform.offsetMin = new Vector2(150f, 0f);

            offer.Key = CornerKey(card, (index + 1).ToString(), 38f, new Vector2(-24f, -22f));

            var rarity = card.gameObject.AddComponent<WcRarity>();
            rarity.Tinted = new[]
            {
                ring.GetComponent<ThemeColor>(), glow.GetComponent<ThemeColor>(),
                offer.Kind.GetComponent<ThemeColor>(), offer.KindIcon.GetComponent<ThemeColor>(),
            };
            rarity.TextTinted = new[] { offer.Title.GetComponent<ThemeColor>() };
            rarity.RareOnly = new[] { glow.gameObject };
            offer.Rarity = rarity;
            rarity.Set(WcRarity.Tier.Common);
            return offer;
        }

        // ---------------------------------------------------------------- замена способности
        static void BuildReplace(RectTransform root, RunHudView view)
        {
            RectTransform screen = InkScreen(root, "Замена способности", out view.Replace);
            InkPic(screen, "Значок", "ability", new Vector2(.5f, 1f), new Vector2(0f, -128f), 100f);
            view.ReplaceTitle = Title(screen, "Новая способность", 222f, 52f);
            Box(UiInkKit.Divider(screen, "Линия", 860f, true, .35f), new Vector2(.5f, 1f), Center, new Vector2(0f, -284f), new Vector2(860f, 16f));
            view.ReplaceSubtitle = Line(screen, "Пояснение", "Панель полна.", 320f, 19f, Role.TextMuted);
            // Клавиши — под пояснением, как на выборе награды: внизу их закрывал боевой HUD.
            view.ReplaceHint = Hint(screen, "1  2  3  4 — заменить слот    ·    L — уйти с добычей", 348f);

            view.Slots = new RunSlotTile[4];
            for (int i = 0; i < 4; i++) view.Slots[i] = SlotTile(screen, i);

            view.Salvage = InkButton(screen, "Разобрать", true, new Vector2(.5f, 1f), new Vector2(0f, -722f), new Vector2(460f, 58f));
            view.SalvageLabel = view.Salvage.GetComponentInChildren<TMP_Text>();
            view.SalvageLabel.text = "Разобрать на 0 золота";
            ButtonIcon((RectTransform)view.Salvage.transform, "salvage", 44f, true, Role.TextOnAccent);
        }

        /// <summary>
        /// Плитка слота: малый клуб дыма, круглый медальон способности (как плитки HUD), имя и что
        /// пропадёт; клавиша — кругом в углу. Наведение — тёплый свет, как у карточек награды.
        /// </summary>
        static RunSlotTile SlotTile(RectTransform screen, int index)
        {
            const float w = 230f, h = 250f, gap = 24f;
            RectTransform tile = Node("Слот " + (index + 1), screen);
            Box(tile, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2((index - 1.5f) * (w + gap), -382f), new Vector2(w, h));
            // Малая подложка: соседние клубы сливаются в одну полосу дыма; нить — только под своей
            // плиткой, тусклая кремовая, как под карточками награды.
            CalmThread(UiInkKit.Plate(tile, small: true), w - 20f, .18f);
            var slot = tile.gameObject.AddComponent<RunSlotTile>();
            // Свет наведения — за медальоном способности.
            slot.Button = InkClickable(tile, 1.03f, w, new Vector2(.5f, 1f), new Vector2(0f, -84f), new Vector2(210f, 210f));

            slot.Icon = Medallion(tile, "Ячейка", new Vector2(.5f, 1f), new Vector2(0f, -84f), 112f, Role.PanelLine, .45f, out _, out _);
            slot.Icon.enabled = false;
            RectTransform name = Box(Node("Название", tile), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -156f), new Vector2(w - 24f, 34f));
            slot.Name = UiInkKit.Label(name, "Надпись", "Способность", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .1f);
            slot.Name.enableAutoSizing = true;
            slot.Name.fontSizeMin = 14f;
            slot.Name.fontSizeMax = 22f;
            slot.Name.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform note = Box(Node("Таланты", tile), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -194f), new Vector2(w - 24f, 28f));
            // Цвет строки ставит RunHud: усиления пропадут — красным, иначе приглушённым.
            slot.Note = UiInkKit.Label(note, "Надпись", "Усилений нет", FontRole.Body, 16f, Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .15f);

            slot.Key = CornerKey(tile, (index + 1).ToString(), 34f, new Vector2(-14f, -14f));
            return slot;
        }

        // ---------------------------------------------------------------- состояние и босс
        static void BuildStatus(RectTransform root, RunHudView view)
        {
            // «Дым и свет» (владелец 25 сентября): карточки нет — клуб дыма из угла экрана, тающий
            // вправо и вниз, под ним огненная нить. Смена заголовка проявляет панель заново (RunHudView).
            RectTransform panel = Box(Node("Состояние забега", root), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(370f, 110f));
            UiInkKit.SmokeLayer(panel, "Дым", "smoke_band_1", 1f, 120f, 50f, origin: new Vector2(0f, 1f));
            UiInkKit.LightAt(panel, "Нить", "light_thread", new Vector2(0f, 0f), new Vector2(170f, -2f), new Vector2(340f, 34f), .45f,
                origin: new Vector2(0f, .5f), delay: .3f);
            UiInkGroup appear = UiInkKit.Group(panel, UiInkGroup.Sweep.TopToBottom, .5f, .2f);
            // Как у HUD: анимация — при первом показе и при смене заголовка, не при каждом выходе из меню.
            appear.FirstTimeOnly = true;
            view.Status = panel;
            Pic(panel, "Значок разлома", "rift", new Vector2(0f, 1f), new Vector2(38f, -30f), 50f);
            Pic(panel, "Значок встреч", "encounter", new Vector2(0f, 1f), new Vector2(86f, -62f), 30f);
            Pic(panel, "Значок тайников", "cache", new Vector2(0f, 1f), new Vector2(86f, -88f), 30f);
            RectTransform title = Box(Node("Заголовок", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(72f, -12f), new Vector2(290f, 34f));
            view.StatusTitle = UiInkKit.Label(title, "Надпись", "Арена 1", FontRole.Heading, 26f, Role.Text);
            RectTransform line = Box(Node("Строка", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -50f), new Vector2(256f, 24f));
            view.StatusLine = UiInkKit.Label(line, "Надпись", "Встречи 0 / 3 · целей 12", FontRole.Body, 16f, Role.Text, delay: .2f);
            RectTransform extra = Box(Node("Ещё строка", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -76f), new Vector2(256f, 24f));
            view.StatusExtra = UiInkKit.Label(extra, "Надпись", "Тайники 0 / 2 · золото 0", FontRole.Body, 16f, Role.TextMuted, delay: .28f);
            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Таймер выживания (стадия 6 «Мобы леса»): свой клуб дыма под панелью состояния, в её
        /// манере — значок, антиква, огненная нить. Не сверху по центру: там полоса босса и узкое
        /// объявление боевого HUD («Новая волна»). Проявляется при каждом показе — выживание
        /// бывает раз за арену. Последние десять секунд RunHud красит акцентом.
        /// </summary>
        static void BuildSurvival(RectTransform root, RunHudView view)
        {
            RectTransform panel = Box(Node("Выживание", root), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -144f), new Vector2(300f, 62f));
            UiInkKit.SmokeLayer(panel, "Дым", "smoke_band_1", 1f, 110f, 40f, origin: new Vector2(0f, 1f));
            UiInkKit.LightAt(panel, "Нить", "light_thread", new Vector2(0f, 0f), new Vector2(140f, 0f), new Vector2(280f, 30f), .45f,
                origin: new Vector2(0f, .5f), delay: .3f);
            UiInkKit.Group(panel, UiInkGroup.Sweep.TopToBottom, .5f, .2f);
            view.Survival = panel;
            Pic(panel, "Значок", "encounter", new Vector2(0f, 1f), new Vector2(38f, -30f), 40f);
            RectTransform time = Box(Node("Время", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(72f, -12f), new Vector2(220f, 38f));
            view.SurvivalLabel = UiInkKit.Label(time, "Надпись", "Выстоять 0:42", FontRole.Heading, 28f, Role.Text);
            view.SurvivalLabel.textWrappingMode = TextWrappingModes.NoWrap;
            panel.gameObject.SetActive(false);
        }

        static void BuildBoss(RectTransform root, RunHudView view)
        {
            // «Дым и свет»: имя антиквой на клубе дыма, под ним полоса — мазок кистью, как здоровье
            // героя в боевом HUD (тёмная дорожка, заливка во всю длину под маской, свет поверх), и
            // тусклая нить. Появление — от центра, огонь по кромке мягкий: выход босса — большой момент.
            RectTransform boss = Box(Node("Босс", root), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -22f), new Vector2(680f, 104f));
            view.Boss = boss;
            UiInkKit.SmokeLayer(boss, "Дым", "smoke_band_1", 1f, 90f, 26f);
            UiInkKit.SmokeAt(boss, "Дым под именем", "soft_blot", new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(520f, 70f), .8f, deep: true);
            UiInkGroup appear = UiInkKit.Group(boss, UiInkGroup.Sweep.FromCenter, .5f, .3f);
            appear.InkDuration = .7f;
            appear.Burn = .4f;
            appear.EdgeScale = 1.4f;

            RectTransform nameBox = Box(Node("Имя", boss), new Vector2(.5f, 1f), Center, new Vector2(0f, -30f), new Vector2(640f, 50f));
            view.BossName = UiInkKit.Label(nameBox, "Надпись", "Лесной страж", FontRole.Heading, 38f, Role.Text, TextAlignmentOptions.Center, 2f, 1f, .1f);
            view.BossName.textWrappingMode = TextWrappingModes.NoWrap;
            view.BossName.enableAutoSizing = true;
            view.BossName.fontSizeMin = 24f;
            view.BossName.fontSizeMax = 38f;

            const float barWidth = 600f;
            RectTransform bar = Box(Node("Здоровье", boss), new Vector2(.5f, 1f), Center, new Vector2(0f, -78f), new Vector2(barWidth, 20f));
            Stretch(UiInkKit.StrokeLayer(bar, "Дорожка", "brush_stroke_1", Role.SmokeDeep, 1f).rectTransform, -5f);
            RectTransform inner = Stretch(Node("Внутри", bar), 2f);
            // След недавнего урона: светлый мазок от доли до прежней доли (растягивает WcBar).
            RectTransform trail = Node("След", inner);
            trail.anchorMin = new Vector2(.64f, 0f);
            trail.anchorMax = new Vector2(.8f, 1f);
            trail.offsetMin = trail.offsetMax = Vector2.zero;
            UiInkKit.StrokeLayer(trail, "Мазок", "brush_stroke_2", Role.Text, .45f);
            trail.gameObject.SetActive(false);
            // Заливка — маска, мазок внутри во всю длину: доля меняет ширину маски, мазок не сжимается.
            // Первым в заливке идёт цветной мазок — его цвет RunHud.BossBar меняет в ярости.
            RectTransform fill = Node("Заполнение", inner);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.64f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<RectMask2D>();
            RectTransform stroke = Node("Мазок", fill);
            stroke.anchorMin = Vector2.zero;
            stroke.anchorMax = new Vector2(0f, 1f);
            stroke.pivot = new Vector2(0f, .5f);
            stroke.offsetMin = new Vector2(0f, -2f);
            stroke.offsetMax = new Vector2(barWidth - 4f, 2f);
            UiInkKit.StrokeLayer(stroke, "Краска", "brush_stroke_2", Role.Health, 1f);
            Color light = T.Get(Role.Health);
            light.a = .3f;
            UiInkKit.LightLayer(stroke, "Свет", "brush_stroke_2", 1f, 3f, new Vector2(0f, .5f), .1f).color = light;
            UiInkKit.LightAt(boss, "Нить", "light_thread", new Vector2(.5f, 1f), new Vector2(0f, -94f), new Vector2(560f, 30f), .3f, delay: .25f);

            var health = bar.gameObject.AddComponent<WcBar>();
            health.Fill = fill;
            health.Trail = trail;
            health.Set(.64f);
            view.BossBar = health;
            boss.gameObject.SetActive(false);
        }
    }
}
