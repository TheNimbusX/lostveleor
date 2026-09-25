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
    public static partial class CombatHudWcBuilder
    {
        // ---------------------------------------------------------------- зелья
        static void BuildPotions(RectTransform root, CombatHudView view)
        {
            // Вариант B: два зелья за разделителем, размером со способность (владелец: «мелковаты»).
            // Бутылку по выбранному размеру ставит CombatHudView (Resources/UI/Items/potion_*).
            RectTransform panel = Box(Node("Зелья", root), BottomCenter, Vector2.zero, new Vector2(PotionsX, RowBottom),
                new Vector2(PotionPitch + Slot, Slot));
            view.PotionPanel = panel;
            string[] art = { "potion_health_small", "potion_lavidium_small" };
            for (int i = 0; i < 2; i++)
            {
                // Имена частей — те, что ищет CombatHudView.Potions: «Art», «Count», «Key».
                RectTransform tileRect = Box(Node(i == 0 ? "Health Potion" : "Lavidium Potion", panel), Vector2.zero, Vector2.zero,
                    new Vector2(i * PotionPitch, 0f), new Vector2(Slot, Slot));
                Image shadow = Layer(tileRect, "Тень", T.GlowSmall, Role.Veil, .8f, 20f);
                shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
                Layer(tileRect, "Заливка", T.FillSmall, Role.Panel);
                Layer(tileRect, "Тень снизу", T.ShadeSmall, Role.Veil, .5f);
                Image halo = Mark(tileRect, "Свет", T.Blob, i == 0 ? Role.Health : Role.Lavidium, .18f, new Vector2(.5f, .5f), Vector2.zero, Slot);
                halo.preserveAspect = false;
                var raw = Stretch(Node("Art", tileRect), 6f).gameObject.AddComponent<RawImage>();
                raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + art[i] + ".png");
                raw.raycastTarget = false;
                Layer(tileRect, "Свет по кромке", T.HighlightSmall, Role.Highlight, .8f);
                Layer(tileRect, "Рамка", T.FrameSmall, Role.PanelLine, .9f);
                if (i == 0)
                {
                    // Здоровья мало, а Живица есть — банка мягко дышит светом (HudPulse, CombatHudView).
                    Image call = Mark(tileRect, "Пора пить", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, Slot * 1.7f);
                    Additive(call, new Color(1f, .42f, .3f, 0f));
                    call.enabled = false;
                    var pulse = call.gameObject.AddComponent<HudPulse>();
                    pulse.Target = call;
                    pulse.Min = .18f;
                    pulse.Max = .55f;
                    pulse.Period = 1.3f;
                    view.HealthPotionPulse = pulse;
                }
                RectTransform count = Box(Node("Count", tileRect), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-5f, 12f), new Vector2(40f, 24f));
                TMP_Text countLabel = LabelOn(count, "0", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.BottomRight);
                countLabel.fontStyle = FontStyles.Bold;
                Shadowed(countLabel);
                // Клавиша — плашкой на нижней кромке, как у способностей.
                RectTransform cap = Keycap(tileRect, "Клавиша", "5", 26f);
                cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
                cap.pivot = new Vector2(.5f, .5f);
                cap.anchoredPosition = new Vector2(0f, -4f);
            }

            // Подсказка зелья: малая карточка пака, высота по тексту.
            RectTransform tip = Box(Node("Подсказка зелья", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 200f), new Vector2(320f, 90f));
            Card(tip, 8f);
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(18, 18, 12, 12);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Текст", tip);
            view.PotionTooltipText = LabelOn(text, "Зелье здоровья · 10%\nЛКМ — выпить\nПКМ — сменить размер", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
            view.PotionTooltip = tip;
            tip.gameObject.SetActive(false);
        }

        /// <summary>
        /// Надпись прямо на узле. Label пака кладёт текст дочерним объектом, а здесь
        /// текст нужен на самом узле: CombatHudView ищет «Count» и «Key» с TMP_Text,
        /// а раскладка подсказки берёт высоту строки у самого текста.
        /// </summary>
        static TMP_Text LabelOn(RectTransform rect, string text, FontRole font, float size, Role role, TextAlignmentOptions align)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            return label;
        }

        /// <summary>Фон карточки пака слоями, которые не участвуют в раскладке.</summary>
        static void Card(RectTransform rect, float radius)
        {
            bool small = radius < 12f;
            Image shadow = Layer(rect, "Тень", small ? T.GlowSmall : T.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(rect, "Заливка", small ? T.FillSmall : T.Fill, Role.Panel, .96f);
            Layer(rect, "Тень снизу", small ? T.ShadeSmall : T.ShadeSprite, Role.Veil, .5f);
            Layer(rect, "Свет по кромке", small ? T.HighlightSmall : T.HighlightSprite, Role.Highlight);
            Layer(rect, "Рамка", small ? T.FrameSmall : T.Frame, Role.PanelLine, .9f);
            foreach (Transform child in rect)
                child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        // ---------------------------------------------------------------- подсказка способности
        static void BuildTooltip(RectTransform root, CombatHudView view)
        {
            RectTransform tip = Box(Node("Подсказка", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 180f), new Vector2(420f, 200f));
            Card(tip, 14f);
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20, 20, 16, 18);
            column.spacing = 10f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Tooltip = tip;

            // Шапка: иконка в ячейке, название антиквой, клавиша.
            RectTransform header = Node("Шапка", tip);
            var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Size(header, -1f, 56f);
            RectTransform cell = Node("Иконка", header);
            Size(cell, 56f, 56f);
            Layer(cell, "Заливка", T.FillSmall, Role.Panel);
            RectTransform mask = Stretch(Node("Маска", cell), 1.5f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = T.FillSmall;
            maskImage.type = Image.Type.Sliced;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            view.TooltipIcon = Stretch(Node("Картинка", mask)).gameObject.AddComponent<RawImage>();
            view.TooltipIcon.raycastTarget = false;
            Layer(cell, "Рамка", T.FrameSmall, Role.PanelLine, .9f);

            RectTransform title = Node("Название", header);
            Size(title, -1f, 40f).flexibleWidth = 1f;
            view.TooltipTitle = LabelOn(title, "Способность", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.MidlineLeft);
            view.TooltipTitle.characterSpacing = 1f;
            view.TooltipTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.TooltipTitle.enableAutoSizing = true;
            view.TooltipTitle.fontSizeMin = 16f;
            view.TooltipTitle.fontSizeMax = 26f;
            RectTransform keyBox = Node("Клавиша", header);
            Size(keyBox, 34f, 34f);
            RectTransform cap = Keycap(keyBox, "Плашка", "Q", 34f);
            Stretch(cap);
            view.TooltipKey = cap.Find("Буква").GetComponent<TMP_Text>();

            // Усиления (концепт 1-gem-ingame): ряд из 8 ромбиков, первые N залиты, и «Усилений: N из 8».
            RectTransform upgrades = Node("Усиления", tip);
            var upgradeRow = upgrades.gameObject.AddComponent<HorizontalLayoutGroup>();
            upgradeRow.spacing = 5f;
            upgradeRow.childAlignment = TextAnchor.MiddleLeft;
            upgradeRow.childControlWidth = upgradeRow.childControlHeight = true;
            upgradeRow.childForceExpandWidth = upgradeRow.childForceExpandHeight = false;
            Size(upgrades, -1f, 22f);
            view.TooltipUpgradePips = new Image[Game.Sim.RunLoadout.MaxUpgrades];
            view.UpgradePipFilled = T.DiamondFill;
            view.UpgradePipEmpty = T.DiamondFrameSmall;
            for (int i = 0; i < view.TooltipUpgradePips.Length; i++)
            {
                RectTransform pipBox = Node("Ромбик " + (i + 1), upgrades);
                Size(pipBox, 14f, 14f);
                var pip = pipBox.gameObject.AddComponent<Image>();
                pip.sprite = T.DiamondFrameSmall;
                pip.preserveAspect = true;
                pip.raycastTarget = false;
                view.TooltipUpgradePips[i] = pip;
            }
            RectTransform upgradeText = Node("Счёт", upgrades);
            Size(upgradeText, -1f, 22f).flexibleWidth = 1f;
            view.TooltipUpgradeText = LabelOn(upgradeText, "Усилений: 0 из 8", FontRole.Body, 15f, Role.TextMuted, TextAlignmentOptions.MidlineRight);
            view.TooltipUpgradeRow = upgrades.gameObject;

            RectTransform divider = Place("DividerPlain", tip, "Разделитель");
            Size(divider, -1f, 16f);

            RectTransform body = Node("Описание", tip);
            view.TooltipBody = LabelOn(body, "Описание способности.", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.TopLeft);
            view.TooltipBody.lineSpacing = 2f;

            // Параметры: сетка в три столбца, черта только между столбцами.
            RectTransform metrics = Node("Параметры", tip);
            var grid = metrics.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(126f, 32f);
            grid.spacing = new Vector2(0f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            view.TooltipMetricColumns = 3;
            view.TooltipMetrics = new HudTooltipMetric[6];
            for (int i = 0; i < 6; i++)
            {
                RectTransform item = Node("Параметр " + (i + 1), metrics);
                var metric = item.gameObject.AddComponent<HudTooltipMetric>();
                RectTransform sep = Box(Node("Черта", item), new Vector2(0f, .5f), new Vector2(0f, .5f), Vector2.zero, new Vector2(1.5f, 22f));
                var sepImage = sep.gameObject.AddComponent<Image>();
                sepImage.sprite = T.Pixel;
                Tint(sepImage, Role.PanelLine, .3f);
                metric.Separator = sep.gameObject;
                metric.Icon = Mark(item, "Значок", T.Pixel, Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(24f, 0f), 22f);
                RectTransform value = Box(Node("Значение", item), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(42f, 0f), new Vector2(80f, 30f));
                metric.Value = Label(value, "Надпись", "0", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineLeft);
                metric.Value.textWrappingMode = TextWrappingModes.NoWrap;
                metric.Value.enableAutoSizing = true;
                metric.Value.fontSizeMin = 12f;
                metric.Value.fontSizeMax = 18f;
                view.TooltipMetrics[i] = metric;
            }

            // Состояние («Перезарядка 2,1 с»): CombatHudView включает и прячет сам текст.
            RectTransform status = Node("Состояние", tip);
            Size(status, -1f, 24f);
            view.TooltipStatus = LabelOn(status, "", FontRole.Body, 16f, Role.Bad, TextAlignmentOptions.MidlineLeft);
            status.gameObject.SetActive(false);

            // Хвостик — гранёный ромб на нижней кромке, напротив плитки.
            RectTransform tail = Box(Node("Хвостик", tip), new Vector2(.5f, 0f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(18f, 18f));
            tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Layer(tail, "Заливка", T.DiamondFill, Role.Panel);
            Layer(tail, "Оправа", T.DiamondFrameSmall, Role.PanelLine);
            view.TooltipTail = tail;
            tip.gameObject.SetActive(false);
        }

        static LayoutElement Size(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = element.minWidth = width;
            if (height >= 0f) element.preferredHeight = element.minHeight = height;
            return element;
        }

        // ---------------------------------------------------------------- отказ при нажатии
        // ---------------------------------------------------------------- новый уровень
        const float BannerWidth = 780f, BannerHeight = 193f, BannerNumberX = -236f;

        /// <summary>
        /// Большой баннер «Новый уровень» сверху по центру — вариант А листа 4-buffs-level-sheet
        /// (владелец, 24 сентября: «скудно, нет эффекта вау»). Рисованная плашка с обожжённой
        /// кромкой (wc_level_banner, из генерации по концепту), слева крупная золотая цифра с
        /// лучами, справа прибавки; вспышка, искры и проблеск — свет (Razlom/UI Additive).
        /// Движение — в HudLevelBanner.
        /// </summary>
        static void BuildLevelBanner(RectTransform root, CombatHudView view)
        {
            RectTransform banner = Box(Node("Новый уровень", root), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -150f - BannerHeight * .5f),
                new Vector2(BannerWidth, BannerHeight));
            var bannerView = banner.gameObject.AddComponent<HudLevelBanner>();
            bannerView.Group = banner.gameObject.AddComponent<CanvasGroup>();
            bannerView.Group.blocksRaycasts = false;
            bannerView.Group.interactable = false;

            Image flash = Layer(banner, "Вспышка", Kit("wc_fx_glow"), Role.Text, 0f, 150f);
            Additive(flash, new Color(1f, .62f, .3f, 0f));
            bannerView.Flash = flash;
            // Тень — размытый силуэт самой плашки: прямоугольная тень пака выдавала «подложку» у острых концов.
            Image shadow = Layer(banner, "Тень", Kit("wc_level_banner_shadow"), Role.Veil, .8f, BannerWidth * 48f / 1192f);
            shadow.type = Image.Type.Simple;
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -8f);

            RectTransform plate = Stretch(Node("Плашка", banner));
            bannerView.Plate = plate;
            Image art = Layer(plate, "Рисунок", Kit("wc_level_banner"), Role.Text, 1f);
            Object.DestroyImmediate(art.GetComponent<ThemeColor>());
            art.color = Color.white;
            art.type = Image.Type.Simple;
            // Лучи — поверх рисунка, под цифрой; маска по форме плашки не даёт им вылезти за кромку.
            RectTransform raysBox = PlateMask(plate, "Лучи");
            Image rays = Mark(raysBox, "Свет", Kit("wc_fx_shine"), Role.Text, 0f, new Vector2(.5f, .5f), new Vector2(BannerNumberX, 0f), 360f);
            Additive(rays, new Color(1f, .74f, .38f, 0f));
            bannerView.Rays = rays;

            // Проблеск по плашке: косая полоса света под маской той же формы.
            RectTransform glintBox = PlateMask(plate, "Проблеск");
            RectTransform stripe = Box(Node("Полоса", glintBox), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(42f, 320f));
            stripe.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var stripeImage = stripe.gameObject.AddComponent<Image>();
            stripeImage.sprite = Kit("wc_fx_glow");
            Additive(stripeImage, new Color(1f, .82f, .55f, 0f));
            stripeImage.enabled = false;
            var glint = glintBox.gameObject.AddComponent<HudGlint>();
            glint.Stripe = stripeImage;
            glint.Peak = .5f;
            glint.Driven = true;
            bannerView.Glint = glint;

            // Цифра: золото сверху вниз в оранжевое, тёмная обводка и тёплое свечение подложкой.
            RectTransform number = Box(Node("Уровень", plate), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(BannerNumberX, 2f), new Vector2(210f, 170f));
            TMP_Text digits = LabelOn(number, "4", FontRole.Heading, 128f, Role.Text, TextAlignmentOptions.Center);
            Object.DestroyImmediate(digits.GetComponent<ThemeColor>());
            digits.color = Color.white;
            digits.enableVertexGradient = true;
            digits.colorGradient = new VertexGradient(new Color(1f, .96f, .74f), new Color(1f, .96f, .74f), new Color(1f, .62f, .2f), new Color(1f, .62f, .2f));
            digits.textWrappingMode = TextWrappingModes.NoWrap;
            Material gold = LevelNumberMaterial(digits.font);
            if (gold != null) digits.fontSharedMaterial = gold;
            bannerView.Number = digits;

            // Черта между цифрой и прибавками — с ромбом, как на плашке.
            RectTransform divider = Box(Node("Черта", plate), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(BannerNumberX + 112f, 0f), new Vector2(2f, 104f));
            var line = divider.gameObject.AddComponent<Image>();
            line.sprite = T.Pixel;
            line.raycastTarget = false;
            Tint(line, Role.PanelLine, .45f);
            Mark(divider, "Ромб", T.DiamondSmall, Role.PanelLine, .9f, new Vector2(.5f, .5f), Vector2.zero, 11f);

            float textX = BannerNumberX + 136f;
            RectTransform caption = Box(Node("Надпись", plate), new Vector2(.5f, .5f), new Vector2(0f, .5f), new Vector2(textX, 52f), new Vector2(380f, 30f));
            TMP_Text captionText = LabelOn(caption, "НОВЫЙ УРОВЕНЬ", FontRole.Heading, 21f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            captionText.characterSpacing = 6f;
            captionText.textWrappingMode = TextWrappingModes.NoWrap;
            bannerView.Caption = captionText;
            bannerView.GainLines = new TMP_Text[3];
            string[] gains = { "+30 здоровья", "+5 урона", "+10 лавидия" };
            for (int i = 0; i < gains.Length; i++)
            {
                RectTransform gainBox = Box(Node("Прибавка " + (i + 1), plate), new Vector2(.5f, .5f), new Vector2(0f, .5f), new Vector2(textX, 16f - i * 30f), new Vector2(380f, 30f));
                TMP_Text gain = LabelOn(gainBox, gains[i], FontRole.Body, 22f, Role.Text, TextAlignmentOptions.MidlineLeft);
                gain.textWrappingMode = TextWrappingModes.NoWrap;
                gain.fontStyle = FontStyles.Bold;
                bannerView.GainLines[i] = gain;
            }

            // Искры веером из-за цифры, поверх плашки.
            RectTransform sparks = Box(Node("Искры", banner), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(BannerNumberX, 0f), new Vector2(10f, 10f));
            var burst = sparks.gameObject.AddComponent<HudSparkBurst>();
            burst.Sparks = new Image[20];
            for (int i = 0; i < burst.Sparks.Length; i++)
            {
                RectTransform one = Box(Node("Искра " + (i + 1), sparks), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(24f, 24f));
                var image = one.gameObject.AddComponent<Image>();
                image.sprite = T.Spark;
                Additive(image, i % 3 == 0 ? new Color(1f, .95f, .8f, 0f) : new Color(1f, .72f, .36f, 0f));
                image.enabled = false;
                burst.Sparks[i] = image;
            }
            burst.Driven = true;
            burst.Distance = 240f;
            burst.Size = 38f;
            burst.Stretch = 1.9f;
            bannerView.Sparks = burst;

            view.LevelBanner = bannerView;
            banner.gameObject.SetActive(false);
        }

        /// <summary>Маска по форме рисунка плашки: всё внутри видно только на самой плашке.</summary>
        static RectTransform PlateMask(RectTransform plate, string name)
        {
            RectTransform box = Stretch(Node(name, plate));
            var shape = box.gameObject.AddComponent<Image>();
            shape.sprite = Kit("wc_level_banner");
            shape.raycastTarget = false;
            box.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            return box;
        }

        /// <summary>
        /// Материал цифры уровня: тёмно-коричневая обводка и тёплое свечение подложкой (шрифт на
        /// мобильном SDF-шейдере — своего свечения у него нет). Лежит рядом со шрифтом.
        /// </summary>
        static Material LevelNumberMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            string path = "Assets/UI/Fonts/" + font.name + " Level.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(font.material) { name = font.name + " Level" };
            mat.SetFloat("_OutlineWidth", .12f);
            mat.SetColor("_OutlineColor", new Color(.28f, .12f, .03f, 1f));
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(1f, .45f, .1f, .75f));
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", 0f);
            mat.SetFloat("_UnderlayDilate", 1f);
            mat.SetFloat("_UnderlaySoftness", 1f);
            ShaderUtilities.GetShaderPropertyIDs();
            ShaderUtilities.UpdateShaderRatios(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---------------------------------------------------------------- эффекты зелий
        /// <summary>
        /// Ряд значков действующих эффектов зелий над панелью героя (концепт 4-buffs-level-ingame).
        /// У каждого — круг с кольцом-таймером и подпись, что эффект даёт: без наведения понятно.
        /// Значки — временные, из концепта; финальные бутылки-иконки готовит владелец.
        /// </summary>
        static void BuildBuffs(RectTransform root, CombatHudView view)
        {
            RectTransform row = Box(Node("Эффекты зелий", root), BottomCenter, new Vector2(0f, 0f), new Vector2(HeroX - 12f, Bottom + StripHeight + 44f), new Vector2(500f, 48f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            view.ResinChip = BuffChip(row, "Живица", "wc_buff_resin", "Живица", "−25% получаемого урона");
            view.SurgeChip = BuffChip(row, "Порыв", "wc_buff_surge", "Порыв", "+20% к бегу и приёмам");
        }

        static HudBuffChip BuffChip(RectTransform row, string name, string icon, string title, string effect)
        {
            RectTransform chip = Node(name, row);
            chip.sizeDelta = new Vector2(244f, 48f);
            var buff = chip.gameObject.AddComponent<HudBuffChip>();
            buff.Group = chip.gameObject.AddComponent<CanvasGroup>();
            buff.Group.blocksRaycasts = false;
            buff.Group.interactable = false;
            buff.Name = title;
            buff.EffectText = effect;

            // Подложка под текстом — светлая земля не съедает подпись.
            RectTransform plate = Box(Node("Подложка", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(222f, 40f));
            Layer(plate, "Заливка", T.PillFill, Role.Panel, .82f);

            RectTransform circle = Box(Node("Круг", chip), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(24f, 0f), new Vector2(46f, 46f));
            Layer(circle, "Тень", T.CircleFill, Role.Panel, 1f);
            Image art = Mark(circle, "Значок", Kit(icon), Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, 38f);
            Object.DestroyImmediate(art.GetComponent<ThemeColor>());
            art.color = Color.white;
            Layer(circle, "Ободок", T.CircleFrame, Role.PanelLine, .5f);
            Image pop = Mark(circle, "Вспышка", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 110f);
            Additive(pop, new Color(1f, .7f, .35f, 0f));
            pop.enabled = false;
            buff.Glow = pop;
            buff.Circle = circle;
            Image ring = Layer(circle, "Кольцо", T.CircleFrame, Role.Accent, 1f);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = false;
            ring.fillAmount = .7f;
            buff.Ring = ring;

            RectTransform effectBox = Box(Node("Эффект", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(54f, 8f), new Vector2(186f, 22f));
            buff.Effect = LabelOn(effectBox, effect, FontRole.Body, 15f, Role.Text, TextAlignmentOptions.MidlineLeft);
            buff.Effect.fontStyle = FontStyles.Bold;
            buff.Effect.textWrappingMode = TextWrappingModes.NoWrap;
            buff.Effect.enableAutoSizing = true;
            buff.Effect.fontSizeMin = 13f;
            buff.Effect.fontSizeMax = 15f;
            RectTransform titleBox = Box(Node("Название", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(54f, -10f), new Vector2(186f, 18f));
            buff.Title = LabelOn(titleBox, title + " · 4 с", FontRole.Body, 13f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            chip.gameObject.SetActive(false);
            return buff;
        }

        static void BuildFeedback(RectTransform root, CombatHudView view)
        {
            RectTransform pill = Box(Node("Отказ", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, Bottom + StripHeight + 22f), new Vector2(240f, 40f));
            Image shadow = Layer(pill, "Тень", T.ButtonGlow, Role.Veil, .7f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(pill, "Заливка", T.PillFill, Role.Panel, .95f);
            Layer(pill, "Ободок", T.PillFrame, Role.Accent, .9f);
            foreach (Transform child in pill) child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(26, 26, 6, 6);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Сообщение", pill);
            view.FeedbackText = LabelOn(text, "Перезарядка", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            view.FeedbackText.textWrappingMode = TextWrappingModes.NoWrap;
            view.Feedback = pill;
            pill.gameObject.SetActive(false);
        }
    }
}
