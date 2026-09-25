using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Библиотека элементов пака «Ночная акварель»: каждый элемент — отдельный префаб
    /// в Assets/UI/Kit/Watercolor/Prefabs. Окна собираются из экземпляров этих
    /// префабов, поэтому правка префаба руками (толщина, отступ, свечение) расходится
    /// по всем окнам сразу. Цвета и шрифты — через тему (<see cref="UiTheme"/>).
    ///
    /// Каждый элемент — слои поверх друг друга: тень, заливка, зерно, свет по
    /// кромке, серебряная рамка; у редкого — свечение, подсветка изнутри и камень.
    /// Все слои названы по-русски понятными именами, чтобы их было легко найти в иерархии.
    ///
    /// Префабы создаются, только если их нет: ручные правки не затираются.
    /// Пересобрать один элемент — удалить его префаб и запустить
    /// «Разлом/UI/Пак «Ночная акварель» — собрать недостающие префабы».
    /// </summary>
    public static partial class UiKitBuilder
    {
        public const string PrefabFolder = UiKitImport.KitRoot + "/Watercolor/Prefabs";

        static UiTheme Theme
        {
            get
            {
                var theme = UiThemeBuilder.Ensure(false);
                return theme != null ? theme : UiTheme.Current;
            }
        }

        [MenuItem("Разлом/UI/Пак «Ночная акварель» — собрать недостающие префабы")]
        public static void EnsurePrefabs()
        {
            Directory.CreateDirectory(PrefabFolder);
            Save("Panel", () => Panel(null, "Panel"));
            Save("ButtonPrimary", () => Button(null, "ButtonPrimary", "Основная", ButtonKind.Primary));
            Save("ButtonSecondary", () => Button(null, "ButtonSecondary", "Вторичная", ButtonKind.Secondary));
            Save("Tab", () => Tab(null, "Tab", "Вкладка", false));
            Save("ListItem", () => ListItem(null, "ListItem", "Пункт", false));
            Save("Cell", () => Cell(null, "Cell", null, WcRarity.Tier.Common));
            Save("CellEmpty", () => CellEmpty(null, "CellEmpty"));
            Save("Card", () => Card(null, "Card", "Название", "Описание в две строки.", "Урон", "+15", null, WcRarity.Tier.Common));
            Save("Bar", () => Bar(null, "Bar", Role.Health, .7f));
            Save("BarLarge", () => Bar(null, "BarLarge", Role.Health, .7f, 360f, 20f));
            Save("Pill", () => Pill(null, "Pill", "120 / 144"));
            Save("Divider", () => Divider(null, "Divider"));
            Save("DividerPlain", () => Divider(null, "DividerPlain", 400f, false));
            Save("SectionHeader", () => SectionHeader(null, "SectionHeader", "Раздел"));
            Save("CloseButton", () => CloseButton(null, "CloseButton"));
            EnsureControlPrefabs();
            EnsureHudPrefabs();
            AssetDatabase.SaveAssets();
        }

        static void Save(string name, System.Func<RectTransform> build)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            RectTransform root = build();
            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject, path); }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        public static GameObject Prefab(string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            if (!File.Exists(path)) EnsurePrefabs();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Экземпляр префаба элемента (связь с префабом сохраняется).</summary>
        public static RectTransform Place(string prefab, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(prefab), parent);
            go.name = name;
            return (RectTransform)go.transform;
        }

        // ================================================================ основа

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rect = (RectTransform)go.transform;
            if (parent != null) rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Позиция от левого верхнего угла родителя, в единицах Canvas (1920×1080).</summary>
        public static RectTransform TopLeft(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float pad = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(pad, pad);
            rect.offsetMax = new Vector2(-pad, -pad);
            return rect;
        }

        public static RectTransform At(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static ThemeColor Tint(Graphic graphic, Role role, float alpha = 1f)
        {
            var tint = graphic.GetComponent<ThemeColor>();
            if (tint == null) tint = graphic.gameObject.AddComponent<ThemeColor>();
            tint.Role = role;
            tint.Alpha = alpha;
            tint.Apply();
            return tint;
        }

        /// <summary>Слой на весь элемент; expand — насколько слой шире элемента (свечение, тень).</summary>
        public static Image Layer(RectTransform parent, string name, Sprite sprite, Role role, float alpha = 1f, float expand = 0f, bool tiled = false)
        {
            RectTransform rect = Stretch(Node(name, parent), -expand);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = tiled ? Image.Type.Tiled : sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            Tint(image, role, alpha);
            return image;
        }

        /// <summary>Отдельная деталь фиксированного размера (ромб, камень, значок).</summary>
        /// <summary>
        /// Мягкая круглая тень — под круги, медальоны, портрет. Тень пака (Glow, GlowSmall) —
        /// 9-slice прямоугольник: под кругом или шестиугольником она видна «прозрачной подложкой»
        /// с краями (владелец, 25 сентября).
        /// </summary>
        public static Sprite RoundShadow => KitSprite("wc_shadow_round");

        /// <summary>Мягкое круглое свечение (без жёстких краёв) — для круглых деталей.</summary>
        public static Sprite RoundGlow => KitSprite("wc_fx_glow");

        public static Sprite KitSprite(string name)
        {
            string path = UiKitImport.KitRoot + "/Watercolor/" + name + ".png";
            UiKitImport.Ensure(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static Image Mark(RectTransform parent, string name, Sprite sprite, Role role, float alpha, Vector2 anchor, Vector2 position, float size)
        {
            RectTransform rect = At(Node(name, parent), anchor, position, new Vector2(size, size));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Tint(image, role, alpha);
            return image;
        }

        public static TMP_Text Label(RectTransform parent, string name, string text, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float spacing = 0f, float alpha = 1f)
        {
            RectTransform rect = Stretch(Node(name, parent));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.characterSpacing = spacing;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role, alpha);
            return label;
        }

        static RectTransform Root(Transform parent, string name, float w, float h)
        {
            RectTransform root = Node(name, parent);
            root.sizeDelta = new Vector2(w, h);
            return root;
        }

        // ================================================================ элементы

        /// <summary>Окно: тень, заливка, зерно, свет по кромке, серебряная рамка; дети — в «Содержимое».</summary>
        public static RectTransform Panel(Transform parent, string name, float w = 520f, float h = 340f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            Image shadow = Layer(root, "Тень", t.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(root, "Заливка", t.Fill, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSprite, Role.Veil, .55f);
            Layer(root, "Подсветка края", t.InnerGlow, Role.Text, .035f);
            Layer(root, "Свет по кромке", t.HighlightSprite, Role.Highlight);
            Layer(root, "Рамка", t.Frame, Role.PanelLine);
            Stretch(Node("Содержимое", root));
            return root;
        }

        public enum ButtonKind { Primary, Secondary }

        /// <summary>Кнопка-капсула: основная оранжевая с ободком и свечением, вторичная — серебряный контур.</summary>
        public static RectTransform Button(Transform parent, string name, string text, ButtonKind kind, float w = 280f, float h = 56f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            bool primary = kind == ButtonKind.Primary;
            if (primary) Layer(root, "Свечение", t.ButtonGlow, Role.Accent, .12f, 24f);
            Image fill = Layer(root, "Заливка", t.ButtonFill, primary ? Role.Accent : Role.Panel, primary ? 1f : .75f);
            fill.raycastTarget = true;
            Image rim = Layer(root, "Ободок", t.ButtonFrame, primary ? Role.TextOnAccent : Role.PanelLine, primary ? .45f : 1f);
            TMP_Text label = Label(root, "Надпись", text, FontRole.Heading, 26f, primary ? Role.TextOnAccent : Role.Text, TextAlignmentOptions.Center, 2f);

            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;
            var states = root.gameObject.AddComponent<ThemeStates>();
            states.Label = label.GetComponent<ThemeColor>();
            if (primary)
            {
                states.Target = fill.GetComponent<ThemeColor>();
            }
            else
            {
                // У вторичной на наведение загорается ободок, заливка остаётся тёмной.
                states.Target = rim.GetComponent<ThemeColor>();
                states.Normal = Role.PanelLine;
                states.Hover = Role.Accent;
                states.Pressed = Role.AccentPressed;
                states.Disabled = Role.Disabled;
            }
            states.Apply();
            return root;
        }

        /// <summary>Вкладка: подпись и оранжевое подчёркивание у выбранной.</summary>
        public static RectTransform Tab(Transform parent, string name, string text, bool selected, float w = 150f, float h = 48f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            Label(root, "Надпись", text, FontRole.Body, 20f, selected ? Role.Accent : Role.Text, TextAlignmentOptions.Center, 0f, selected ? 1f : .85f)
                .textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform line = Node("Подчёркивание", root);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(.5f, 0f);
            line.offsetMin = new Vector2(10f, 2f);
            line.offsetMax = new Vector2(-10f, 5f);
            var image = line.gameObject.AddComponent<Image>();
            image.sprite = t.BarFill;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            Tint(image, Role.Accent);
            line.gameObject.SetActive(selected);
            return root;
        }

        /// <summary>Строка списка: гранёный ромб-маркер и подпись; у выбранной — светлая подложка и оранжевый ромб.</summary>
        public static RectTransform ListItem(Transform parent, string name, string text, bool selected, float w = 420f, float h = 52f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            Image bg = Layer(root, "Подложка", t.FillSmall, Role.Text, .045f);
            Image frame = Layer(root, "Рамка", t.FrameSmall, Role.PanelLine, .35f);
            bg.gameObject.SetActive(selected);
            frame.gameObject.SetActive(selected);
            Mark(root, "Маркер", t.DiamondMedium, selected ? Role.Accent : Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(34f, 0f), 18f);
            TMP_Text label = Label(root, "Надпись", text, FontRole.Body, 21f, Role.Text);
            label.rectTransform.offsetMin = new Vector2(62f, 0f);
            return root;
        }

        /// <summary>
        /// Ячейка предмета (вариант А владельца 23 сентября): фон цвета редкости, одна рамка
        /// того же цвета, камень сверху у редкой и выше. Наведение и выбор меняют ту же рамку
        /// (WcSlotState), вторых контуров поверх нет.
        /// </summary>
        public static RectTransform Cell(Transform parent, string name, Texture icon, WcRarity.Tier tier, float size = 138f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Image shadow = Layer(root, "Тень", t.GlowSmall, Role.Veil, .7f, 14f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(root, "Заливка", t.FillSmall, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSmall, Role.Veil, .5f);
            Image fill = SlotFill(root);
            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight, .7f);
            RectTransform art = Stretch(Node("Предмет", root), 14f);
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
            raw.enabled = icon != null;
            Image frame = Layer(root, "Рамка", t.FrameSmall, Role.Common);
            Image gem = Mark(root, "Камень", t.Gem, Role.Rare, 1f, new Vector2(.5f, 1f), Vector2.zero, 22f);

            WcSlotState state = SlotState(root, frame, fill);
            state.Rarity = (int)tier;
            var rarity = root.gameObject.AddComponent<WcRarity>();
            rarity.Tinted = new[] { gem.GetComponent<ThemeColor>() };
            rarity.RareOnly = new[] { gem.gameObject };
            rarity.Set(tier);
            return root;
        }

        public static Sprite WcSprite(string name)
        {
            string path = UiKitImport.KitRoot + "/Watercolor/" + name + ".png";
            UiKitImport.Ensure(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Цветной фон редкости (вариант А владельца 23 сентября); цвет ставит WcSlotState.</summary>
        public static Image SlotFill(RectTransform root)
        {
            Image fill = Layer(root, "Фон редкости", WcSprite("wc_rarity_fill_s"), Role.Common);
            Object.DestroyImmediate(fill.GetComponent<ThemeColor>());
            return fill;
        }

        /// <summary>
        /// Одна рамка ячейки: редкость, наведение и выбор меняют её саму, без вторых контуров
        /// поверх (владелец 23 сентября: «появляется поверх ещё одна рамка»).
        /// </summary>
        public static WcSlotState SlotState(RectTransform root, Image frame, Image fill)
        {
            UiTheme t = Theme;
            ThemeColor tint = frame.GetComponent<ThemeColor>();
            if (tint != null) Object.DestroyImmediate(tint);
            var state = root.gameObject.AddComponent<WcSlotState>();
            state.Frame = frame;
            state.Fill = fill;
            // 24 сентября: рисованный фон редкости и светящаяся рамка по концепту А; пустая — тонкая тихая.
            state.FrameSprite = WcSprite("wc_slot_glow");
            state.SelectedSprite = WcSprite("wc_slot_glow_bold");
            state.QuietSprite = t.FrameSmall;
            state.FillSprites = new[] { WcSprite("wc_rarity_bg_common"), WcSprite("wc_rarity_bg_rare"), WcSprite("wc_rarity_bg_epic"), WcSprite("wc_rarity_bg_unique") };
            frame.type = Image.Type.Sliced;
            state.Apply();
            return state;
        }

        /// <summary>Пустая ячейка: тонкая рамка, пунктир внутри и ромб по центру.</summary>
        public static RectTransform CellEmpty(Transform parent, string name, float size = 138f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Layer(root, "Заливка", t.FillSmall, Role.Panel, .7f);
            Layer(root, "Рамка", t.FrameSmall, Role.PanelLine, .7f);
            Image dashed = Layer(root, "Пунктир", t.FrameDashed, Role.PanelLine, .45f);
            Stretch(dashed.rectTransform, 18f);
            dashed.preserveAspect = false;
            Mark(root, "Ромб", t.DiamondLarge, Role.TextMuted, .7f, new Vector2(.5f, .5f), Vector2.zero, 26f);
            return root;
        }

        /// <summary>
        /// Карточка улучшения (выбор 1 из 3, навыки): вся рамка в цвете редкости, камень на
        /// верхней кромке с двумя малыми ромбами, круглый значок, название, плашка редкости,
        /// описание, строка значения.
        /// </summary>
        public static RectTransform Card(Transform parent, string name, string title, string description, string valueLabel, string value,
            Texture icon, WcRarity.Tier tier, float w = 715f, float h = 195f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            Image glow = Layer(root, "Свечение", t.Glow, Role.Rare, .8f, 24f);
            Image shadow = Layer(root, "Тень", t.Glow, Role.Veil, .8f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(root, "Заливка", t.Fill, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSprite, Role.Veil, .55f);
            Layer(root, "Свет по кромке", t.HighlightSprite, Role.Highlight);
            Image inner = Layer(root, "Подсветка изнутри", t.InnerGlow, Role.Rare, .14f);
            Image frame = Layer(root, "Рамка", t.Frame, Role.Common);
            Image frameRare = Layer(root, "Рамка редкой", t.FrameBold, Role.Rare);

            Image gem = Mark(root, "Камень", t.Gem, Role.Common, 1f, new Vector2(.5f, 1f), Vector2.zero, 24f);
            Image wingL = Mark(root, "Ромб слева", t.DiamondSmall, Role.Common, 1f, new Vector2(.5f, 1f), new Vector2(-24f, 0f), 9f);
            Image wingR = Mark(root, "Ромб справа", t.DiamondSmall, Role.Common, 1f, new Vector2(.5f, 1f), new Vector2(24f, 0f), 9f);

            // Круглый значок: диск, картинка в круглой маске, кольцо в цвете редкости.
            RectTransform disk = At(Node("Значок", root), new Vector2(0f, .5f), new Vector2(100f, 0f), new Vector2(140f, 140f));
            Image back = Layer(disk, "Диск", t.CircleFill, Role.Veil, .9f);
            back.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            RectTransform art = Stretch(Node("Картинка", back.rectTransform), 4f);
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
            raw.enabled = icon != null;
            Image ring = Layer(disk, "Кольцо", t.CircleFrame, Role.Common);

            RectTransform text = Node("Текст", root);
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(196f, 16f);
            text.offsetMax = new Vector2(-24f, -18f);
            TMP_Text titleLabel = Label(text, "Название", title, FontRole.Heading, 30f, Role.Text, TextAlignmentOptions.TopLeft, 1.5f);
            titleLabel.textWrappingMode = TextWrappingModes.NoWrap;

            // Вертикальный ритм: название 0–36, ярлык 42–72, описание 80–122, значение снизу.
            RectTransform pill = TopLeft(Node("Плашка", text), 0f, 42f, 130f, 30f);
            Image pillFill = Layer(pill, "Заливка", t.TagFill, Role.Common, .04f);
            Image pillFrame = Layer(pill, "Ободок", t.TagFrame, Role.Common);
            TMP_Text pillLabel = Label(pill, "Надпись", tier == WcRarity.Tier.Rare ? "Редкое" : "Обычное", FontRole.Body, 17f, Role.Common, TextAlignmentOptions.Center);

            TMP_Text desc = Label(text, "Описание", description, FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            desc.rectTransform.offsetMax = new Vector2(0f, -80f);
            desc.lineSpacing = -6f;

            RectTransform valueRow = Node("Значение", text);
            valueRow.anchorMin = new Vector2(0f, 0f);
            valueRow.anchorMax = new Vector2(1f, 0f);
            valueRow.pivot = new Vector2(0f, 0f);
            valueRow.offsetMin = new Vector2(0f, 0f);
            valueRow.offsetMax = new Vector2(0f, 28f);
            TMP_Text valueName = Label(valueRow, "Подпись", valueLabel, FontRole.Body, 20f, Role.TextMuted);
            TMP_Text valueNumber = Label(valueRow, "Число", value, FontRole.Body, 22f, Role.Accent);
            valueNumber.rectTransform.offsetMin = new Vector2(72f, 0f);

            var rarity = root.gameObject.AddComponent<WcRarity>();
            rarity.Tinted = new[]
            {
                frame.GetComponent<ThemeColor>(), frameRare.GetComponent<ThemeColor>(), gem.GetComponent<ThemeColor>(),
                wingL.GetComponent<ThemeColor>(), wingR.GetComponent<ThemeColor>(), ring.GetComponent<ThemeColor>(),
                pillFill.GetComponent<ThemeColor>(), pillFrame.GetComponent<ThemeColor>(), pillLabel.GetComponent<ThemeColor>(),
            };
            rarity.TextTinted = new[] { titleLabel.GetComponent<ThemeColor>() };
            rarity.CommonOnly = new[] { frame.gameObject };
            rarity.RareOnly = new[] { glow.gameObject, inner.gameObject, frameRare.gameObject };
            rarity.Set(tier);
            return root;
        }

        /// <summary>
        /// Полоса: тёмная дорожка, заполнение капсулой, серебряный контур.
        /// Крупная (выше 15 единиц: здоровье героя, босс) — как на листе HUD: заполнение
        /// с зазором внутри контура и светлым кончиком.
        /// </summary>
        public static RectTransform Bar(Transform parent, string name, Role fillRole, float value, float w = 360f, float h = 14f)
        {
            UiTheme t = Theme;
            bool large = h > 15f;
            Sprite fillSprite = large ? t.BarFillLarge : t.BarFill;
            RectTransform root = Root(parent, name, w, h);
            Layer(root, "Дорожка", fillSprite, Role.Track);
            RectTransform inner = large ? Stretch(Node("Внутри", root), 3.5f) : root;
            // След недавнего урона: светлая часть между новым и прежним значением.
            RectTransform trail = Node("След", inner);
            var trailImage = trail.gameObject.AddComponent<Image>();
            trailImage.sprite = fillSprite;
            trailImage.type = Image.Type.Sliced;
            trailImage.raycastTarget = false;
            Tint(trailImage, Role.Text, .85f);
            trail.gameObject.SetActive(false);
            RectTransform fill = Node("Заполнение", inner);
            fill.pivot = new Vector2(0f, .5f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = fillSprite;
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            Tint(fillImage, fillRole);
            Layer(fill, "Свет", t.HighlightSmall, Role.Text, .35f);
            if (large)
            {
                Image tip = Mark(fill, "Кончик", t.Blob, Role.Text, .5f, new Vector2(1f, .5f), new Vector2(-h * .8f, 0f), h);
                tip.preserveAspect = false;
                tip.rectTransform.sizeDelta = new Vector2(h * 1.8f, h * 1.1f);
            }
            Layer(root, "Контур", large ? t.BarFrameLarge : t.BarFrame, Role.PanelLine, .55f);
            var bar = root.gameObject.AddComponent<WcBar>();
            bar.Fill = fill;
            bar.Trail = trail;
            bar.Set(value);
            return root;
        }

        /// <summary>Плашка-капсула со значением («120 / 144», «×3»).</summary>
        public static RectTransform Pill(Transform parent, string name, string text, float w = 120f, float h = 40f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, h);
            Layer(root, "Заливка", t.PillFill, Role.Panel, .8f);
            Layer(root, "Ободок", t.PillFrame, Role.PanelLine, .8f);
            Label(root, "Надпись", text, FontRole.Body, 20f, Role.Text, TextAlignmentOptions.Center);
            return root;
        }

        /// <summary>Разделитель: серебряная линия, гранёные ромбы на концах и в центре.</summary>
        public static RectTransform Divider(Transform parent, string name, float w = 400f, bool center = true)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 16f);
            // У эталона линия прерывается перед центральным ромбом: две половины с зазором.
            float gap = center ? 16f : 0f;
            for (int side = 0; side < 2; side++)
            {
                RectTransform line = Node(side == 0 ? "Линия слева" : "Линия справа", root);
                line.anchorMin = new Vector2(side == 0 ? 0f : .5f, .5f);
                line.anchorMax = new Vector2(side == 0 ? .5f : 1f, .5f);
                line.offsetMin = new Vector2(side == 0 ? 5f : gap, -.75f);
                line.offsetMax = new Vector2(side == 0 ? -gap : -5f, .75f);
                var image = line.gameObject.AddComponent<Image>();
                image.sprite = t.Pixel;
                image.raycastTarget = false;
                Tint(image, Role.PanelLine, .8f);
            }
            Mark(root, "Ромб слева", t.DiamondSmall, Role.PanelLine, 1f, new Vector2(0f, .5f), Vector2.zero, 9f);
            Mark(root, "Ромб справа", t.DiamondSmall, Role.PanelLine, 1f, new Vector2(1f, .5f), Vector2.zero, 9f);
            if (center) Mark(root, "Ромб в центре", t.DiamondMedium, Role.PanelLine, 1f, new Vector2(.5f, .5f), Vector2.zero, 14f);
            return root;
        }

        /// <summary>Заголовок раздела: ромб, подпись капителью с разрядкой, линия до конца, ромб.</summary>
        public static RectTransform SectionHeader(Transform parent, string name, string text, float w = 500f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 30f);
            // Раскладка строкой: линия сама начинается после подписи и тянется до конца,
            // поэтому смена текста в инспекторе ничего не ломает.
            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.spacing = 16f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            Image Diamond(string part)
            {
                RectTransform r = Node(part, root);
                var image = r.gameObject.AddComponent<Image>();
                image.sprite = t.DiamondSmall;
                image.preserveAspect = true;
                image.raycastTarget = false;
                Tint(image, Role.PanelLine);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 10f;
                le.preferredHeight = 10f;
                return image;
            }

            Diamond("Ромб");
            RectTransform labelRect = Node("Надпись", root);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text.ToUpperInvariant();
            label.fontSize = 24f;
            label.characterSpacing = 10f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            labelRect.gameObject.AddComponent<ThemeFont>().Role = FontRole.Heading;
            labelRect.GetComponent<ThemeFont>().Apply();
            Tint(label, Role.Text, .9f);

            RectTransform lineBox = Node("Линия", root);
            var flex = lineBox.gameObject.AddComponent<LayoutElement>();
            flex.flexibleWidth = 1f;
            flex.preferredHeight = 30f;
            RectTransform line = Node("Черта", lineBox);
            line.anchorMin = new Vector2(0f, .5f);
            line.anchorMax = new Vector2(1f, .5f);
            line.offsetMin = new Vector2(0f, -.75f);
            line.offsetMax = new Vector2(0f, .75f);
            var image = line.gameObject.AddComponent<Image>();
            image.sprite = t.Pixel;
            image.raycastTarget = false;
            Tint(image, Role.PanelLine, .6f);
            Diamond("Ромб в конце");
            return root;
        }

        /// <summary>Кнопка «закрыть»: ромб-рамка и крест.</summary>
        public static RectTransform CloseButton(Transform parent, string name, float size = 44f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Image hit = Layer(root, "Заливка", t.DiamondFill, Role.Panel, .6f);
            hit.raycastTarget = true;
            Image frame = Layer(root, "Рамка", t.DiamondFrameSmall, Role.PanelLine);
            Mark(root, "Крест", t.Cross, Role.Text, .9f, new Vector2(.5f, .5f), Vector2.zero, 20f);
            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            var states = root.gameObject.AddComponent<ThemeStates>();
            states.Target = frame.GetComponent<ThemeColor>();
            states.Normal = Role.PanelLine;
            states.Hover = Role.Accent;
            states.Pressed = Role.AccentPressed;
            states.Disabled = Role.Disabled;
            states.Apply();
            return root;
        }
    }
}
