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
    /// Части медальона «Дыма и света» (<see cref="CampInkParts.IconMedallion"/>): ссылки, которые сборщик окна отдаёт своему виду.
    /// Состояния (ui-common, «тонкая тлеющая кромка»): покой — <see cref="Ring"/>; наведение — UiHoverMotion сам; выбрано или
    /// в силе — <see cref="Fire"/>; закрыто — <see cref="Locked"/>.
    /// </summary>
    internal sealed class InkMedal
    {
        /// <summary>Узел медальона: размер — диаметр, опора — центр; его растит наведение.</summary>
        public RectTransform Root;
        /// <summary>Тёмный диск под рисунком.</summary>
        public Image Disc;
        /// <summary>
        /// Рисунок под круглой маской: расписная картинка во весь круг или белая маска знака (краска темы). Без картинки
        /// выключен (пустая RawImage рисует белый квадрат) — вид включает его вместе с texture.
        /// </summary>
        public RawImage Art;
        /// <summary>Тонкое кремовое кольцо покоя (CircleFrame, материал Plain, ~0,45).</summary>
        public Image Ring;
        /// <summary>Огненное кольцо light_ring «выбрано / в силе» (как у готовой способности HUD); узел выключен. Тление — альфа цвета.</summary>
        public Image Fire;
        /// <summary>Толстое кольцо акцента «выбрано рукой» — когда окну нужны два состояния сразу (в силе и выбрано); узел выключен.</summary>
        public Image Picked;
        /// <summary>Закрытый медальон: тень на рисунке, замок, подпись; null у медальона без замка; узел выключен.</summary>
        public GameObject Locked;
        /// <summary>Подпись под замком («Ур. 6»); null без замка.</summary>
        public TMP_Text LockLabel;
        /// <summary>Кнопка на весь медальон; null у некликабельного.</summary>
        public Button Button;
        /// <summary>Подпись под медальоном (цена печати у <see cref="CampInkParts.SealSet"/>); иначе null.</summary>
        public TMP_Text Note;
    }

    /// <summary>
    /// Общие детали «Дыма и света» для новых окон лагеря (06.10: закалка Эни, «Клятвы» и «Атлас» палатки, стол «Перед походом»).
    /// Это КОПИИ приватных помощников принятых сборщиков (CampShopsWcBuilder, CampTentWcBuilder, RunHudWcBuilder,
    /// CombatHudWcBuilder.Abilities, CampPolishUiBuilder.ExpandAlchemy): оригиналы не тронуты, чтобы принятые окна и HUD не
    /// менялись от правок здесь. Кегли — только шкала темы (UiTheme.Size: 56/32/24/18/14), шрифты — через ThemeFont, тексты
    /// окон — CampWindowText. Координаты — единицы холста 1920×1080 от левого верхнего угла родителя (как TopLeft); у
    /// медальонов — их центр. Звать с именем класса (CampInkParts.SectionHeader): у partial-сборщиков есть одноимённые помощники.
    /// Клоны шаблонов в рантайме — затем UiInkGroup.Collect() окна (ловушка 8 ui-common), иначе клоны не проявятся.
    /// </summary>
    internal static class CampInkParts
    {
        static UiTheme T => UiTheme.Current;

        /// <summary>Кегль ступени шкалы темы.</summary>
        internal static float Size(TextStep step) => T.Size(step);

        // ---------------------------------------------------------------- основа

        /// <summary>Узел по центру (<paramref name="cx"/>, <paramref name="cy"/>) от левого верхнего угла родителя.</summary>
        internal static RectTransform CenterAt(RectTransform rect, float cx, float cy, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(cx, -cy);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        /// <summary>Без навигации клавишами и геймпадом: геймпад в окнах лагеря владелец остановил.</summary>
        internal static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>Кнопка на весь узел: прозрачный ловец мыши (дым мышь не ловит — клик ушёл бы в ходьбу по лагерю), без перехода цвета.</summary>
        internal static Button Clickable(RectTransform rect)
        {
            Image hit = UiInkKit.HitArea(rect);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            return button;
        }

        /// <summary>Надпись в своей рамке (копия Text окон жителей), кегль — ступень шкалы; проявляется по буквам.</summary>
        internal static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font, TextStep step, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            return UiInkKit.Label(box, "Надпись", text, font, T.Size(step), role, align);
        }

        /// <summary>Надпись прямо на узле (копия TextOn): ширину берёт раскладка строки — для рядов с HorizontalLayoutGroup.</summary>
        internal static TMP_Text InlineText(RectTransform parent, string name, string text, FontRole font, TextStep step, Role role)
        {
            RectTransform rect = Node(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = T.Size(step);
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

        /// <summary>Заголовок окна (Display 56) по центру <paramref name="cx"/> и нить с огоньком под ним — как ScreenTitle окон жителей.</summary>
        internal static TMP_Text Title(RectTransform parent, string text, float cx, float y, float w, float lineWidth)
        {
            TMP_Text title = Text(parent, "Заголовок", text, cx - w * .5f, y, w, 76f, FontRole.Heading, TextStep.Display, Role.Text, TextAlignmentOptions.Center);
            title.characterSpacing = 3f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            TopLeft(UiInkKit.Divider(parent, "Линия", lineWidth, true, .7f), cx - lineWidth * .5f, y + 78f, lineWidth, 16f);
            return title;
        }

        // ---------------------------------------------------------------- картинки

        /// <summary>Где искать временные знаки: сцены жителей, белые маски забега, значки набора, акварельные статы.</summary>
        static readonly string[] IconFolders =
        {
            "Assets/UI/CampShops/", "Assets/UI/RunIcons/", UiKitImport.KitRoot + "/Icons/", UiKitImport.KitRoot + "/Watercolor/",
        };

        /// <summary>Временный знак по имени файла без расширения (death, wc_stat_heart, lock, shards); нет — null.</summary>
        internal static Texture2D KitTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (string folder in IconFolders)
            {
                string path = folder + name + ".png";
                // Значки набора импортируются своими настройками (мип-уровни, спрайт): без них края рвутся.
                if (folder.StartsWith(UiKitImport.KitRoot)) UiKitImport.Ensure(path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) return texture;
            }
            return null;
        }

        /// <summary>
        /// Окончательная картинка из Resources (UI/OathIcons/heavy_hand и т. п., без «.png»), а пока её нет — временный знак
        /// <paramref name="placeholder"/>. Так кадр сборки показывает финальный арт, как только его положат; вид в рантайме
        /// всё равно грузит Resources первым.
        /// </summary>
        internal static Texture2D ArtOr(string resourcesPath, string placeholder)
        {
            var final = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + resourcesPath + ".png");
            return final != null ? final : KitTexture(placeholder);
        }

        // ---------------------------------------------------------------- медальоны

        /// <summary>
        /// Круглый медальон «Дыма и света» (копия RunHudWcBuilder.Medallion; круг — единственная оправа, владелец 26.09): клуб
        /// дыма, тёмный диск, картинка в круглой маске и тонкое кольцо света цвета <paramref name="ring"/>; огня нет.
        /// </summary>
        internal static RawImage Medallion(RectTransform parent, string name, Vector2 anchor, Vector2 position, float size, Role ring, float ringAlpha,
            out RectTransform disc, out Image ringImage)
        {
            disc = At(Node(name, parent), anchor, position, new Vector2(size, size));
            UiInkKit.SmokeLayer(disc, "Дым", "smoke_ring", 1f, size * .2f, size * .2f, deep: true);
            Image back = Layer(disc, "Диск", T.CircleFill, Role.SmokeDeep, 1f);
            back.type = Image.Type.Simple;
            back.material = UiInkKit.Plain;
            UiInkKit.Inked(back, delay: .05f);
            // Маска — отдельная невидимая картинка: диск под ней проявляется чернилами, маска — нет (ловушка 9 ui-common).
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

        /// <summary>
        /// Медальон с состояниями (эталон — плитка способности боевого HUD): тёмное кольцо покоя, рисунок или белая маска знака,
        /// тонкая тлеющая кромка light_ring при наведении (UiHoverMotion, рост 1,05), огненное кольцо «выбрано / в силе»,
        /// толстое кольцо акцента «выбрано рукой», замок с подписью. Центр — (<paramref name="cx"/>, <paramref name="cy"/>).
        /// </summary>
        /// <param name="whiteMask">Белая маска знака (RunIcons, wc_stat_*): с полями, краска темы; false — расписной рисунок во весь круг.</param>
        /// <param name="clickable">Кнопка и наведение; false — только картинка (карточка справа, большая печать).</param>
        /// <param name="lockable">Собрать узел «Закрыто» (замок Kit/Icons/lock, тень на рисунке, подпись «Ур. N»).</param>
        internal static InkMedal IconMedallion(RectTransform parent, string name, float cx, float cy, float size, Texture art, bool whiteMask,
            bool clickable = true, bool lockable = false)
        {
            var medal = new InkMedal();
            medal.Art = Medallion(parent, name, new Vector2(0f, 1f), new Vector2(cx, -cy), size, Role.Text, .45f, out medal.Root, out medal.Ring);
            medal.Disc = medal.Root.Find("Диск").GetComponent<Image>();
            // Покой — тихое кремовое кольцо без света (таблица состояний ui-common: CircleFrame, Plain, ~0,45): свет оставлен огню.
            medal.Ring.material = UiInkKit.Plain;
            medal.Art.texture = art;
            medal.Art.enabled = art != null;
            if (whiteMask)
            {
                // Белый знак — с полями, как у значка цели жителя (CampGuideWcBuilder: ~0,22 диаметра), и кремовой краской темы.
                Stretch(medal.Art.rectTransform, size * .18f);
                Tint(medal.Art, Role.Text);
            }

            // Наведение — тонкая тлеющая кромка: light_ring тише и уже огня, в группе с нулевой прозрачностью (её ведёт
            // UiHoverMotion) — так кромки нет ни в покое, ни в кадре сборки без Play.
            RectTransform hover = Stretch(Node("Наведение", medal.Root));
            var hoverGroup = hover.gameObject.AddComponent<CanvasGroup>();
            hoverGroup.alpha = 0f;
            hoverGroup.blocksRaycasts = false;
            hoverGroup.interactable = false;
            UiInkKit.LightLayer(hover, "Кромка", "light_ring", .4f, size * .1f, delay: .2f);

            // «Выбрано / в силе» — огненное кольцо на полную, шире круга как у готовой способности HUD (Slot * .16).
            medal.Fire = UiInkKit.LightLayer(medal.Root, "Огонь", "light_ring", 1f, size * .16f, delay: .25f);
            medal.Fire.gameObject.SetActive(false);

            medal.Picked = Layer(medal.Root, "Выбор", T.CircleFrameBold, Role.Accent, .9f, size * .03f);
            medal.Picked.type = Image.Type.Simple;
            medal.Picked.material = UiInkKit.Plain;
            UiInkKit.Inked(medal.Picked, delay: .1f);
            medal.Picked.gameObject.SetActive(false);

            if (lockable) Lock(medal, size);

            if (clickable)
            {
                medal.Button = Clickable(medal.Root);
                var motion = medal.Root.gameObject.AddComponent<UiHoverMotion>();
                motion.HighlightGroup = hoverGroup;
                motion.HoverScale = 1.05f;
                motion.PulseMin = .6f;
            }
            else Object.DestroyImmediate(hover.gameObject);
            return medal;
        }

        /// <summary>Закрытый медальон: тусклая тень на рисунке, белый замок и подпись (Caption 14) под ним; узел выключен.</summary>
        static void Lock(InkMedal medal, float size)
        {
            RectTransform locked = Stretch(Node("Закрыто", medal.Root));
            Image shade = Layer(locked, "Тень", T.CircleFill, Role.SmokeDeep, .72f, -size * .04f);
            shade.type = Image.Type.Simple;
            shade.material = UiInkKit.Plain;
            UiInkKit.Inked(shade, delay: .08f);
            Image icon = Mark(locked, "Замок", CombatHudBuilder.Icon("lock"), Role.TextMuted, .9f, new Vector2(.5f, .5f), new Vector2(0f, size * .1f), size * .3f);
            icon.material = UiInkKit.Plain;
            UiInkKit.Inked(icon, delay: .12f);
            float caption = T.Size(TextStep.Caption);
            RectTransform box = At(Node("Подпись", locked), new Vector2(.5f, .5f), new Vector2(0f, -size * .2f), new Vector2(size * .9f, caption + 6f));
            medal.LockLabel = UiInkKit.Label(box, "Надпись", "", FontRole.Body, caption, Role.TextMuted, TextAlignmentOptions.Center, delay: .15f);
            medal.LockLabel.textWrappingMode = TextWrappingModes.NoWrap;
            medal.Locked = locked.gameObject;
            locked.gameObject.SetActive(false);
        }

        /// <summary>
        /// Набор печатей группы (клятвы острова): ряды медальонов по <paramref name="perRow"/>, каждый ряд по центру
        /// <paramref name="cx"/>, первый ряд — на высоте <paramref name="cy"/>; под каждой печатью цена (значок и число, Caption 14).
        /// Печати кликабельны, без замка; узел набора <paramref name="name"/> на весь родитель — окно прячет группу целиком.
        /// </summary>
        internal static InkMedal[] SealSet(RectTransform parent, string name, float cx, float cy, int[] perRow, float size, float stepX, float stepY,
            Texture[] arts, bool whiteMask, Texture priceIcon)
        {
            RectTransform set = Stretch(Node(name, parent));
            int count = 0;
            foreach (int n in perRow) count += n;
            var seals = new InkMedal[count];
            int index = 0;
            for (int row = 0; row < perRow.Length; row++)
            {
                float left = cx - (perRow[row] - 1) * stepX * .5f;
                for (int col = 0; col < perRow[row]; col++, index++)
                {
                    Texture art = arts != null && index < arts.Length ? arts[index] : null;
                    seals[index] = IconMedallion(set, "Печать " + (index + 1), left + col * stepX, cy + row * stepY, size, art, whiteMask);
                    seals[index].Note = Note(seals[index].Root, size, priceIcon);
                }
            }
            return seals;
        }

        /// <summary>Строка под медальоном: значок (если есть) и число, по центру; растёт вместе с медальоном при наведении.</summary>
        static TMP_Text Note(RectTransform medal, float size, Texture icon)
        {
            float caption = T.Size(TextStep.Caption);
            RectTransform note = Node("Цена", medal);
            note.anchorMin = note.anchorMax = new Vector2(.5f, 0f);
            note.pivot = new Vector2(.5f, 1f);
            note.anchoredPosition = new Vector2(0f, -size * .08f);
            note.sizeDelta = new Vector2(size + 40f, caption + 8f);
            var row = note.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 4f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            if (icon != null) LayoutIcon(note, "Значок", icon, caption + 4f, true);
            return InlineText(note, "Число", "0", FontRole.Body, TextStep.Caption, Role.TextMuted);
        }

        /// <summary>Значок валюты в ряду раскладки: RawImage без дымки, фиксированный квадрат; белая маска — краской темы.</summary>
        static RawImage LayoutIcon(RectTransform parent, string name, Texture texture, float size, bool tint)
        {
            RectTransform rect = Node(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = size;
            element.preferredHeight = element.minHeight = size;
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.enabled = texture != null;
            raw.raycastTarget = false;
            if (tint) Tint(raw, Role.Text);
            raw.material = UiInkKit.Art;
            UiInkKit.Inked(raw, delay: .12f);
            return raw;
        }

        // ---------------------------------------------------------------- заголовки и вкладки

        /// <summary>
        /// Заголовок раздела (копия Header палатки на шкале темы): огонёк-ромб, подпись Philosopher (прописными при
        /// <paramref name="caps"/>) и нить света от подписи до края; со значком <paramref name="icon"/> — знак вместо ромба,
        /// ромб — в начале нити (концепт стола: «Умение ◆——»). Нить начинается за шириной подписи на момент сборки: у
        /// подписи, которую окно меняет («Сумка · N / 48»), передать самый длинный вид.
        /// </summary>
        internal static TMP_Text SectionHeader(RectTransform parent, string name, string text, float x, float y, float w, TextStep step = TextStep.Caption,
            bool caps = true, Texture icon = null, float strength = .35f)
        {
            float font = T.Size(step);
            float h = Mathf.Round(font * 1.6f);
            RectTransform header = TopLeft(Node(name, parent), x, y, w, h);
            float left;
            if (icon == null)
            {
                UiInkKit.LightAt(header, "Ромб", "light_gem", new Vector2(0f, .5f), new Vector2(8f, 0f), new Vector2(12f, 13f), .9f, delay: .15f);
                left = 22f;
            }
            else
            {
                float size = Mathf.Round(font * 1.3f);
                RectTransform mark = At(Node("Значок", header), new Vector2(0f, .5f), new Vector2(size * .5f, 0f), new Vector2(size, size));
                var raw = mark.gameObject.AddComponent<RawImage>();
                raw.texture = icon;
                raw.raycastTarget = false;
                Tint(raw, Role.Text);
                raw.material = UiInkKit.Art;
                UiInkKit.Inked(raw, delay: .1f);
                left = size + 12f;
            }
            string caption = caps ? text.ToUpperInvariant() : text;
            TMP_Text label = UiInkKit.Label(header, "Надпись", caption, FontRole.Heading, font, Role.Text, spacing: caps ? 3f : .5f, delay: .08f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.offsetMin = new Vector2(left, 0f);
            float start = left + label.GetPreferredValues(caption).x + 14f;
            if (icon != null)
            {
                UiInkKit.LightAt(header, "Ромб", "light_gem", new Vector2(0f, .5f), new Vector2(start + 4f, 0f), new Vector2(11f, 12f), .85f, delay: .15f);
                start += 14f;
            }
            RectTransform line = UiInkKit.Divider(header, "Линия", 100f, false, strength);
            line.anchorMin = new Vector2(0f, .5f);
            line.anchorMax = new Vector2(1f, .5f);
            line.pivot = new Vector2(0f, .5f);
            line.offsetMin = new Vector2(start, -8f);
            line.offsetMax = new Vector2(-4f, 8f);
            return label;
        }

        /// <summary>
        /// Строка вкладок концептов (палатка «СУМКА ◇ КЛЯТВЫ ◇ АТЛАС», кузня «Закалить ◇ Переплавить …»): подписи Philosopher,
        /// ширина — по тексту плюс <paramref name="pad"/> с боков, между вкладками огоньки light_gem. Выбранная — акцентная
        /// подпись и огненное подчёркивание с ромбом (UiInkKit.Tab: «Надпись», «Подчёркивание» — их переключает
        /// CampShopView.SetTab); наведение — тихая нить (UiHoverMotion, звук вкладки играет само окно). Первая выбрана.
        /// </summary>
        /// <param name="lineTo">Не 0 — нить от последней вкладки до этой x (единицы родителя) с огоньком на конце (палатка — до пепла).</param>
        /// <param name="edgeGems">Огоньки и перед первой, и после последней вкладки («◇ СУМКА ◇ … ◇»).</param>
        internal static Button[] TabStrip(RectTransform parent, string name, string[] labels, float x, float y, float h, TextStep step, bool caps,
            float pad = 18f, float lineTo = 0f, bool edgeGems = false)
        {
            const float gap = 34f;
            RectTransform strip = TopLeft(Node(name, parent), x, y, 100f, h);
            var tabs = new Button[labels.Length];
            float cursor = 0f;
            if (edgeGems) cursor = StripGem(strip, "Огонёк перед вкладками", cursor, gap);
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) cursor = StripGem(strip, "Огонёк " + i, cursor, gap);
                string text = caps ? labels[i].ToUpperInvariant() : labels[i];
                RectTransform tab = UiInkKit.Tab(strip, "Вкладка " + (i + 1), text, false, 100f, h, T.Size(step));
                TMP_Text label = tab.Find("Надпись").GetComponent<TMP_Text>();
                label.characterSpacing = caps ? 3f : .5f;
                float w = Mathf.Ceil(label.GetPreferredValues(text).x) + pad * 2f;
                TopLeft(tab, cursor, 0f, w, h);
                cursor += w;
                // Огненное подчёркивание выбранной — с ромбом посередине, как на концептах.
                UiInkKit.LightAt((RectTransform)tab.Find("Подчёркивание"), "Ромб", "light_gem", new Vector2(.5f, .5f), Vector2.zero,
                    new Vector2(12f, 13f), 1f, delay: .2f);

                RectTransform hover = Stretch(Node("Наведение", tab));
                var group = hover.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
                UiInkKit.LightAt(hover, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, 2f), new Vector2(w - 8f, 26f), .3f, delay: .15f);

                tabs[i] = Clickable(tab);
                var motion = tab.gameObject.AddComponent<UiHoverMotion>();
                motion.HighlightGroup = group;
                motion.HoverScale = 1.02f;
                motion.PulseMin = .6f;
                motion.SilentClick = true;
                CampShopView.SetTab(tabs[i], i == 0);
            }
            if (edgeGems) cursor = StripGem(strip, "Огонёк после вкладок", cursor, gap);
            float tail = lineTo - x - cursor;
            if (lineTo > 0f && tail > 40f)
            {
                TopLeft(UiInkKit.Divider(strip, "Нить", tail, false, .45f), cursor, h * .5f - 8f, tail - 10f, 16f);
                UiInkKit.LightAt(strip, "Огонёк конца", "light_gem", new Vector2(0f, .5f), new Vector2(cursor + tail - 6f, 0f), new Vector2(11f, 12f), .7f, delay: .2f);
                cursor += tail;
            }
            strip.sizeDelta = new Vector2(cursor, h);
            return tabs;
        }

        /// <summary>Огонёк-разделитель строки вкладок в середине промежутка <paramref name="gap"/>; отдаёт новый курсор.</summary>
        static float StripGem(RectTransform strip, string name, float cursor, float gap)
        {
            UiInkKit.LightAt(strip, name, "light_gem", new Vector2(0f, .5f), new Vector2(cursor + gap * .5f, 0f), new Vector2(11f, 12f), .7f, delay: .15f);
            return cursor + gap;
        }

        // ---------------------------------------------------------------- кошелёк

        /// <summary>
        /// Ряд валют (кошелёк сверху справа, строка цены «золото · осколки · сталь · сердце»): значок и число Philosopher на
        /// каждую валюту, выравнивание ряда <paramref name="align"/> в рамке шириной <paramref name="w"/>. Узел валюты —
        /// values[i].transform.parent: спрятанный выпадает из ряда, остальные съезжаются.
        /// </summary>
        /// <param name="colors">Краска числа по валютам (Coins у золота); короче — Text.</param>
        /// <param name="tintIcons">Белые маски — кремовой краской темы; false — расписные значки как есть.</param>
        internal static RectTransform WalletRow(RectTransform parent, string name, float x, float y, float w, Texture[] icons, Role[] colors,
            TextAnchor align, out TMP_Text[] values, TextStep step = TextStep.Title, bool tintIcons = true)
        {
            float font = T.Size(step);
            float h = Mathf.Round(font * 1.6f);
            RectTransform row = TopLeft(Node(name, parent), x, y, w, h);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = align;
            layout.spacing = T.SpaceL;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            values = new TMP_Text[icons.Length];
            for (int i = 0; i < icons.Length; i++)
            {
                RectTransform entry = Node("Валюта " + (i + 1), row);
                var line = entry.gameObject.AddComponent<HorizontalLayoutGroup>();
                line.childAlignment = TextAnchor.MiddleLeft;
                line.spacing = T.SpaceS;
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = line.childForceExpandHeight = false;
                LayoutIcon(entry, "Значок", icons[i], Mathf.Round(font * 1.3f), tintIcons);
                values[i] = InlineText(entry, "Число", "0", FontRole.Heading, step, colors != null && i < colors.Length ? colors[i] : Role.Text);
            }
            return row;
        }

        // ---------------------------------------------------------------- кнопки и клавиши

        /// <summary>
        /// Основная кнопка концептов «Ударить [E]», «Кляться [E]», «Отправиться [E]»: оранжевый мазок (UiInkKit.Button) и
        /// кейкап СПРАВА внутри мазка (UiInkKit.ButtonKey ставит его слева). Букву клавиши вид меняет по GameKeyBindings
        /// (<paramref name="keyLabel"/>, затем UiKeyHint.FitKeycap).
        /// </summary>
        internal static Button PrimaryWithKey(RectTransform parent, string name, string text, string key, float x, float y, float w, float h,
            out TMP_Text label, out TMP_Text keyLabel) => KeyedButton(parent, true, name, text, key, x, y, w, h, out label, out keyLabel);

        /// <summary>Вторичная кнопка «Взять [Esc]», «Остаться [Esc]»: тёмный дымный мазок, нить по низу приглушена, кейкап справа.</summary>
        internal static Button SecondaryWithKey(RectTransform parent, string name, string text, string key, float x, float y, float w, float h,
            out TMP_Text label, out TMP_Text keyLabel) => KeyedButton(parent, false, name, text, key, x, y, w, h, out label, out keyLabel);

        static Button KeyedButton(RectTransform parent, bool primary, string name, string text, string key, float x, float y, float w, float h,
            out TMP_Text label, out TMP_Text keyLabel)
        {
            RectTransform rect = UiInkKit.Button(parent, name, text, primary, new Vector2(w, h), T.Size(TextStep.Heading));
            TopLeft(rect, x, y, w, h);
            label = rect.Find("Надпись").GetComponent<TMP_Text>();
            label.characterSpacing = 1f;
            // Вторичная тише основной: огненная нить по низу — едва тлеет (на концептах тёмный мазок без огня).
            if (!primary && rect.Find("Нить") is Transform thread)
            {
                var line = thread.GetComponent<Image>();
                if (line != null) line.color = new Color(1f, 1f, 1f, .35f);
            }
            // Кейкап листа 5 у правого края мазка; его имя «Клавиша» — как у UiInkKit.ButtonKey (её ищут виды и миграции).
            float keySize = Mathf.Min(T.KeycapSize, Mathf.Round(h * .6f));
            RectTransform cap = UiInkKit.Keycap(rect, "Клавиша", key, keySize);
            cap.anchorMin = cap.anchorMax = new Vector2(1f, .5f);
            cap.pivot = new Vector2(1f, .5f);
            cap.anchoredPosition = new Vector2(-18f, 0f);
            keyLabel = cap.Find("Буква").GetComponent<TMP_Text>();
            // Подпись — по центру места левее клавиши; длинная ужимается до ступени Body, не ниже шкалы.
            label.margin = new Vector4(16f, 0f, cap.sizeDelta.x + 30f, 0f);
            label.enableAutoSizing = true;
            label.fontSizeMax = T.Size(TextStep.Heading);
            label.fontSizeMin = T.Size(TextStep.Body);
            var button = rect.GetComponent<Button>();
            NoNavigation(button);
            return button;
        }

        /// <summary>
        /// «[Esc] Закрыть» внизу справа (лист 5, как «Клавиши» окон жителей): ряд «Клавиши» с раскладкой к правому краю
        /// <paramref name="right"/>; подпись — <paramref name="label"/> (окно меняет её на «Отменить»). Другие пары
        /// (UiInkKit.KeyHint) окно кладёт в тот же ряд и ставит первыми — они встанут левее Esc.
        /// </summary>
        internal static RectTransform EscHint(RectTransform parent, out TMP_Text label, float right = 1880f, float y = 1000f)
        {
            RectTransform row = TopLeft(Node("Клавиши", parent), right - 600f, y, 600f, 36f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 28f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            UiInkKit.KeyHint(row, "Esc", CampWindowText.Get("close.action", "Закрыть"), out label, T.KeycapSmall + 2f, T.Size(TextStep.Body));
            return row;
        }

        // ---------------------------------------------------------------- прокрутка

        /// <summary>
        /// Окно прокрутки сетки (по образцу CampPolishUiBuilder.ExpandAlchemy): RectMask2D + ScrollRect, только по вертикали,
        /// без полосы; прозрачный ловец на окне — колесо работает и над промежутками. Отдаёт «Содержимое»: при
        /// <paramref name="columns"/> &gt; 0 — GridLayoutGroup (UpperCenter, поля <paramref name="pad"/>, чтобы дым и рост
        /// ячейки при наведении не резались краем) и высота по содержимому; при 0 — без раскладки, высоту и места ставит окно.
        /// </summary>
        internal static RectTransform ScrollGrid(RectTransform parent, string name, float x, float y, float w, float h, Vector2 cell, Vector2 spacing,
            int columns, out ScrollRect scroll, int pad = 8)
        {
            RectTransform viewport = TopLeft(Node(name, parent), x, y, w, h);
            UiInkKit.HitArea(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Node("Содержимое", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, h);
            if (columns > 0)
            {
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = cell;
                grid.spacing = spacing;
                grid.padding = new RectOffset(pad, pad, pad, pad);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                grid.childAlignment = TextAnchor.UpperCenter;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            return content;
        }

        // ---------------------------------------------------------------- сохранение

        /// <summary>Материалы «Дыма и света» в Assets/UI/Shaders: их числа UiInkKit ставит при каждой сборке и помечает грязными.</summary>
        static readonly string[] InkMaterials =
        {
            "UiInkSmoke", "UiInkSmokeDeep", "UiInkStroke", "UiInkLight", "UiInkCrack", "UiInkPlain", "UiInkArt", "UiInkMap",
        };

        /// <summary>
        /// Точечное сохранение вместо общего AssetDatabase.SaveAssets после сборки или миграции окна «Дыма и света». Префаб уже
        /// записал SaveAsPrefabAsset, тема сохраняет себя сама (UiThemeBuilder.Ensure), импорт значков — SaveAndReimport,
        /// новые материалы — CreateAsset; на диск дописываются только материалы UiInkKit, если сборка их пометила. Чужие
        /// грязные ассеты остаются их владельцам: общий SaveAssets 02.10 сбросил на диск и откатил 3 материала босса.
        /// </summary>
        internal static void SaveInkMaterials()
        {
            foreach (string name in InkMaterials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/Shaders/" + name + ".mat");
                if (material != null) AssetDatabase.SaveAssetIfDirty(material);
            }
        }
    }
}
