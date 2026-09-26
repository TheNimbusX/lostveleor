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
    /// Боевой HUD на паке «Ночная акварель» (23 сентября 2026):
    /// Resources/UI/Prefabs/CombatHudWc.prefab. PlayerHud берёт его первым,
    /// прежний CombatHud остаётся запасным.
    ///
    /// РАСКЛАДКА — ВАРИАНТ B ВЛАДЕЛЬЦА (23 СЕНТЯБРЯ), ВИД — ПАК. Одна тёмная полоса внизу
    /// по центру: портрет, заходящий за её левый край, имя и полосы здоровья, лавидия и
    /// опыта (числа — внутри полос и только под мышью), четыре способности, кувырок того же
    /// размера, разделитель, два зелья размером со способность. Готовность — огненное кольцо
    /// плитки и огонёк усилений на её верхней кромке (HudReadyGem). Карта справа сверху.
    /// Единицы Canvas 1920×1080.
    ///
    /// 26 сентября (владелец): один язык фигур — контейнеры круглые (портрет, уровень, плитки,
    /// клавиши, медальоны), ромб остался только мелким светом в украшениях; огня меньше; HUD выше
    /// на 10 единиц (треугольник уровня обрезался низом экрана); голова Пелага выходит за круг.
    ///
    /// Смысл — в CombatHudView, вид — в префабе. Сборщик создаёт префаб только
    /// если его нет; пересборка из меню спрашивает, потому что стирает ручные правки.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CombatHudWc.prefab";

        // Полоса внизу, единицы Canvas 1920×1080 от низа экрана и от его центра.
        // 26 сентября — всё на 10 выше (было 14 и 36): уровень и медальон артефакта обрезались низом экрана.
        const float Bottom = 24f;        // низ полосы
        const float StripHeight = 112f;
        const float StripLeft = -524f, StripRight = 524f;
        const float Slot = 76f, SlotGap = 14f;
        const float RowBottom = 46f;     // низ плиток: под ними клавиши на кромке
        const float RowCenter = RowBottom + Slot * .5f;
        const float RowWidth = Slot * 4f + SlotGap * 3f;
        const float Portrait = 132f;
        const float BarWidth = 236f;
        const float HeroX = StripLeft + 116f;             // начало полос героя
        const float AbilitiesX = StripLeft + 382f;
        const float DashX = AbilitiesX + RowWidth + 18f;
        const float DividerX = DashX + Slot + 26f;
        const float PotionsX = DividerX + 26f;
        const float PotionPitch = Slot + 14f;

        static readonly Vector2 BottomCenter = new Vector2(.5f, 0f);

        [MenuItem("Разлом/UI/Собрать боевой HUD «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Боевой HUD", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        /// <summary>Собирает префаб, если его нет (или всегда при <paramref name="force"/>).</summary>
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

        static UiTheme T => UiTheme.Current;

        static RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static GameObject Layout()
        {
            var root = new GameObject("CombatHudWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            var view = root.AddComponent<CombatHudView>();
            view.IconCrop = .03f;
            view.HealthNormal = T.Health;
            view.HealthLow = new Color32(0xFF, 0x6A, 0x4A, 0xFF);
            view.Denied = T.Accent;
            view.ArtDimmed = new Color(.5f, .5f, .54f, 1f);
            // Над плиткой огонёк и дуга точек усилений (под мышью видны вместе с подсказкой) — подсказка
            // встаёт выше, чтобы плотный дым её подложки (на 34 ниже края) не закрывал точки.
            view.TooltipGap = 58f;

            var rect = (RectTransform)root.transform;
            // Переход между аренами: новая арена проявляется из темноты, а не вспыхивает склейкой.
            // Ниже всего HUD — затемняется мир, полосы и карта видны.
            var fade = Stretch(Node("Затемнение перехода", rect)).gameObject.AddComponent<Image>();
            fade.sprite = T.Pixel;
            fade.raycastTarget = false;
            fade.color = new Color(.027f, .039f, .063f, 0f);
            fade.enabled = false;
            view.ArenaFade = fade;
            // Мягкое затемнение по низу экрана: полоса и клавиши не сливаются со светлой землёй.
            RectTransform shade = Node("Затемнение низа", rect);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(1f, 0f);
            shade.pivot = new Vector2(.5f, 0f);
            shade.anchoredPosition = Vector2.zero;
            shade.sizeDelta = new Vector2(0f, 240f);
            var shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.sprite = T.VeilLinear;
            shadeImage.raycastTarget = false;
            // Вуаль темы с 25 сентября .78 (было .62). С «Дымом и светом» — слабее: подложку даёт
            // дым, а сплошная темнота низа съедала его форму.
            Tint(shadeImage, Role.Veil, .26f);
            BuildStrip(rect, view);
            BuildHero(rect, view);
            BuildAbilities(rect, view);
            BuildPotions(rect, view);
            BuildTooltip(rect, view);
            BuildFeedback(rect, view);
            BuildLevelBanner(rect, view);
            BuildBuffs(rect, view);
            BuildToasts(rect, view);
            BuildAnnounce(rect, view);
            view.OrderIcons = new[] { Kit("wc_buff_resin").texture, Kit("wc_buff_surge").texture };
            view.GoldIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/RunIcons/gold.png");
            BuildMinimap(rect, view);
            view.StatIcons = new[]
            {
                Kit("wc_stat_heart"), Kit("wc_stat_lavidium"), Kit("wc_stat_cooldown"), Kit("wc_stat_damage"),
                Kit("wc_stat_range"), Kit("wc_stat_radius"), Kit("wc_stat_duration"),
            };
            // Подсказки — последними, поверх всего HUD: значки зелий, всплывашки, объявление и карта собраны
            // позже и рисовались поверх них (26 сентября «−25% получаемого урона» лежало на подсказке
            // способности). CombatHudView ещё раз поднимает подсказку наверх при каждом появлении.
            view.PotionTooltip.SetAsLastSibling();
            view.Tooltip.SetAsLastSibling();
            // Появление HUD (владелец 25 сентября: «всё появляется анимированно»): дым растекается
            // слева направо — герой, способности, зелья, последней карта; за дымом буквы и свет.
            // Всплывашки, подсказки и баннеры ведут свои группы.
            UiInkGroup appear = UiInkKit.Group(rect, UiInkGroup.Sweep.LeftToRight, .6f, .5f);
            appear.StartDelay = .1f;
            // Только при первой загрузке (владелец 25 сентября): выход из паузы, палатки, лавок
            // возвращает HUD сразу, без повторного проявления.
            appear.FirstTimeOnly = true;
            return root;
        }

        /// <summary>
        /// Стиль шрифта с мягкой тёмной тенью для надписей прямо над игрой (без
        /// подложки): на светлой земле леса белый текст без тени не читался.
        /// Лежит рядом со шрифтом, правится в Unity: Assets/UI/Fonts/… Shadow.mat.
        /// </summary>
        internal static Material ShadowMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            string path = "Assets/UI/Fonts/" + font.name + " Shadow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(font.material) { name = font.name + " Shadow" };
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 1f));
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", -.6f);
            mat.SetFloat("_UnderlayDilate", .75f);
            mat.SetFloat("_UnderlaySoftness", .55f);
            // Без пересчёта коэффициентов TMP тень остаётся нулевой ширины.
            ShaderUtilities.GetShaderPropertyIDs();
            ShaderUtilities.UpdateShaderRatios(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void Shadowed(TMP_Text label)
        {
            Material mat = ShadowMaterial(label.font);
            if (mat != null) label.fontSharedMaterial = mat;
        }

        /// <summary>Огонёк усилений над плиткой (круг без оправы), единицы Canvas.</summary>
        const float OrbSize = 12f;

        static Sprite Kit(string name)
        {
            string path = UiKitImport.KitRoot + "/Watercolor/" + name + ".png";
            UiKitImport.Ensure(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- полоса
        /// <summary>
        /// Материал «Дым и свет» (владелец 25 сентября, концепт 3-hud-C-smoke): плашки нет — под
        /// героем, способностями и зельями клубится чернильный дым, края тают в мир; по верху
        /// бежит нить огненного света, из дыма редко всплывают угли. Прямоугольник полосы
        /// остаётся: по нему CombatHudView понимает, что мышь над HUD.
        /// 26 сентября (владелец: «огня слишком много и везде поровну»): нить и угли тише —
        /// главным огнём остаётся кольцо готовой способности.
        /// </summary>
        static void BuildStrip(RectTransform root, CombatHudView view)
        {
            float width = StripRight - StripLeft;
            RectTransform strip = Box(Node("Полоса", root), BottomCenter, Vector2.zero, new Vector2(StripLeft, Bottom),
                new Vector2(width, StripHeight));
            view.Strip = strip;
            UiInkKit.SmokeAt(strip, "Дым под героем", "smoke_band_1", new Vector2(0f, .5f), new Vector2(150f, 8f), new Vector2(640f, 230f),
                origin: new Vector2(.2f, .5f));
            UiInkKit.SmokeAt(strip, "Дым под способностями", "smoke_band_2", new Vector2(.5f, .5f), new Vector2(70f, 4f), new Vector2(760f, 230f));
            UiInkKit.SmokeAt(strip, "Дым под зельями", "smoke_plate", new Vector2(1f, .5f), new Vector2(-96f, 4f), new Vector2(420f, 170f), .96f);
            UiInkKit.LightAt(strip, "Нить света", "light_thread", new Vector2(.5f, 1f), new Vector2(60f, 2f), new Vector2(980f, 44f), .3f,
                origin: new Vector2(0f, .5f), delay: .3f);
            // Между кувырком и зельями — огненный ромб вместо серебряной черты.
            UiInkKit.LightAt(strip, "Разделитель", "light_gem", Vector2.zero, new Vector2(DividerX - StripLeft, RowCenter - Bottom), new Vector2(22f, 24f), .85f,
                delay: .35f);
            UiEmbers embers = UiInkKit.Embers(strip, "Угли", new Vector2(.5f, 1f), new Vector2(40f, 30f), new Vector2(width, 110f), 1f);
            embers.Size = new Vector2(5f, 10f);
        }

        // ---------------------------------------------------------------- герой
        static void BuildHero(RectTransform root, CombatHudView view)
        {
            float left = StripLeft - 44f, bottom = Bottom - 6f;
            RectTransform hero = Box(Node("Герой", root), BottomCenter, Vector2.zero, new Vector2(left, bottom),
                new Vector2(AbilitiesX - 16f - left, StripHeight + 26f));
            view.HeroPanel = hero;
            // Под мышью над героем — числа внутри полос здоровья, лавидия и опыта.
            view.VitalsHit = hero;

            // Красная дымка за портретом и полосами: пульсирует, пока здоровья ≤ 25% (HudPulse).
            Image danger = Mark(hero, "Опасность", Kit("wc_fx_glow"), Role.Text, 0f, Vector2.zero, new Vector2(Portrait * .5f + 30f, Portrait * .5f + 6f), 330f);
            Additive(danger, new Color(1f, .22f, .14f, 0f));
            danger.rectTransform.sizeDelta = new Vector2(430f, 250f);
            danger.enabled = false;
            var dangerPulse = danger.gameObject.AddComponent<HudPulse>();
            dangerPulse.Target = danger;
            dangerPulse.Min = .12f;
            dangerPulse.Max = .42f;
            dangerPulse.Period = 1.05f;
            view.DangerPulse = dangerPulse;

            // Портрет заходит за левый край полосы; кружок уровня — справа снизу на его кромке.
            BuildPortrait(hero, view);

            // Медальон артефакта забега на нижней левой кромке портрета, чуть за кольцом (справа внизу —
            // кружок уровня; в концепте 2-artifact-sheet стороны зеркальны). Виден, только пока артефакт есть.
            // Активные: буква клавиши под медальоном, вуаль перезарядки по кругу, свет, пока действует.
            RectTransform medal = Box(Node("Артефакт", hero), Vector2.zero, new Vector2(.5f, .5f), new Vector2(12f, 18f), new Vector2(54f, 54f));
            Image active = Mark(medal, "Сияние", RoundGlow, Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 118f);
            Additive(active, new Color(1f, .7f, .38f, 0f));
            active.enabled = false;
            var activePulse = active.gameObject.AddComponent<HudPulse>();
            activePulse.Target = active;
            activePulse.Min = .3f;
            activePulse.Max = .7f;
            activePulse.Period = .9f;
            view.ArtifactActive = activePulse;
            Image medalShadow = Layer(medal, "Тень", RoundShadow, Role.Veil, .85f, 8f);
            medalShadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(medal, "Подложка", T.CircleFill, Role.Panel, 1f);
            view.ArtifactIcon = Stretch(Node("Картинка", medal), 4f).gameObject.AddComponent<RawImage>();
            view.ArtifactIcon.raycastTarget = false;
            Image cooldown = Layer(medal, "Перезарядка", T.CircleFill, Role.Panel, .8f, -3f);
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)Image.Origin360.Top;
            cooldown.fillClockwise = false;
            cooldown.enabled = false;
            view.ArtifactCooldown = cooldown;
            RectTransform medalGlint = Stretch(Node("Проблеск", medal), 3f);
            var medalMask = medalGlint.gameObject.AddComponent<Image>();
            medalMask.sprite = T.CircleFill;
            medalMask.raycastTarget = false;
            medalGlint.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            HudGlint glint = Glint(medalGlint, 26f, 90f, .5f);
            glint.Every = 6f;
            glint.Repeat = true;
            Layer(medal, "Кольцо", T.CircleFrame, Role.Unique, .95f);
            Image ready = Mark(medal, "Вспышка", Kit("wc_fx_aura"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 104f);
            Additive(ready, new Color(1f, .84f, .55f, 0f));
            ready.enabled = false;
            view.ArtifactFlash = ready;
            // Клавиша слева от медальона: снизу он стоит у края экрана. Кружок «Дыма и света», не серебро пака.
            RectTransform cap = UiInkKit.Keycap(medal, "Клавиша", "F", 20f);
            cap.anchorMin = cap.anchorMax = new Vector2(0f, .5f);
            cap.pivot = new Vector2(1f, .5f);
            cap.anchoredPosition = new Vector2(-2f, -6f);
            view.ArtifactKey = cap.Find("Буква").GetComponent<TMP_Text>();
            view.ArtifactSlot = medal.gameObject;
            view.ArtifactHit = medal;
            medal.gameObject.SetActive(false);

            float x = HeroX - left;
            RectTransform name = Box(Node("Имя", hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x, Bottom + 92f - bottom), new Vector2(BarWidth, 28f));
            view.HeroName = UiInkKit.Label(name, "Надпись", "Пелаг", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.MidlineLeft, 1f);
            Shadowed(view.HeroName);

            view.HealthFill = Vital(hero, "Здоровье", Role.Health, x, Bottom + 66f - bottom, 20f, 14f, out view.HealthText, out GameObject healthRow);
            view.HealthBar = (RectTransform)healthRow.transform;
            // Удар по герою — полоса вспыхивает светом (HudFx.Flash из CombatHudView).
            Image hit = Layer(view.HealthBar, "Вспышка", UiInkKit.Sprite("brush_stroke_2"), Role.Text, 0f, 4f);
            Additive(hit, new Color(1f, .55f, .45f, 0f));
            hit.type = Image.Type.Simple;
            hit.enabled = false;
            view.HealthFlash = hit;
            view.LavidiumFill = Vital(hero, "Лавидий", Role.Lavidium, x, Bottom + 42f - bottom, 16f, 12f, out view.LavidiumText, out GameObject row);
            view.LavidiumRow = row;
            // Полный лавидий — редкий проблеск по полосе.
            HudGlint lavidiumGlint = Glint(Stretch(Node("Проблеск", (RectTransform)row.transform), 2f), 30f, 60f, .45f);
            lavidiumGlint.Every = 5f;
            view.LavidiumGlint = lavidiumGlint;

            // Опыт: тонкий мазок под лавидием; заливка — маска, мазок внутри во всю длину.
            RectTransform xp = Box(Node("Опыт", hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x + 6f, Bottom + 22f - bottom), new Vector2(BarWidth - 12f, 6f));
            view.ExperienceHit = xp;
            Stretch(UiInkKit.StrokeLayer(xp, "Дорожка", "brush_stroke_1", Role.Smoke, 1f).rectTransform, -3f);
            RectTransform fill = Node("Заполнение", xp);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.35f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<RectMask2D>();
            StrokeFill(fill, BarWidth - 12f, Role.Experience, .9f, 0f);
            view.ExperienceFill = fill;
            // Опыт прибавился — проблеск по полосе.
            view.ExperienceGlint = Glint(Stretch(Node("Проблеск", xp)), 30f, 50f, .6f);
            // Числа опыта — внутри полосы (владелец 24 сентября): под мышью она подрастает до 16 ед.
            view.ExperienceText = Label(xp, "Числа", "", FontRole.Body, 12f, Role.Text, TextAlignmentOptions.Center);
            view.ExperienceText.textWrappingMode = TextWrappingModes.NoWrap;
            view.ExperienceText.fontStyle = FontStyles.Bold;
            Shadowed(view.ExperienceText);
            view.ExperienceHoverHeight = 16f;
        }

        const string PortraitCutoutPath = "Assets/Resources/UI/HUD/PelagPortraitPaintedCutout.png";
        /// <summary>Вырез портрета крупнее круга и приподнят: голова пересекает верхнюю кромку.</summary>
        const float PortraitArtScale = 1.18f, PortraitArtLift = 16f;
        /// <summary>Насколько выше круга ещё видна голова (маска верха).</summary>
        const float PortraitHeadroom = 40f;
        const float LevelBadgeSize = 38f;

        /// <summary>
        /// Портрет, выходящий за круг (владелец 26 сентября: «чтобы выходил немного за рамки обложки»).
        /// Строится на месте, без префаба пака. Тёмный диск — маска: в его нижней половине вырез Пелага,
        /// плечи обрезаны кругом. Выше середины — тот же вырез тем же прямоугольником под RectMask2D до
        /// 40 единиц над кругом, без круглой маски: голова и волосы переходят кромку без шва. Красный
        /// ореол старой картинки был нарисован в её фоне — вместо него мягкий тёплый свет за головой.
        /// Уровень — кружок на кромке справа снизу (ромб ушёл: контейнеры только круглые).
        /// </summary>
        static void BuildPortrait(RectTransform hero, CombatHudView view)
        {
            RectTransform portrait = Box(Node("Портрет", hero), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Portrait, Portrait));
            UiInkKit.SmokeLayer(portrait, "Дым", "smoke_blot_1", 1f, 46f, 46f);
            var cutout = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitCutoutPath);
            if (cutout == null) Debug.LogWarning("Нет выреза портрета " + PortraitCutoutPath + ": HUD возьмёт его из Resources при запуске");

            Image disk = Layer(portrait, "Диск", T.CircleFill, Role.SmokeDeep, 1f);
            disk.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            UiInkKit.LightAt(disk.rectTransform, "Свет за головой", "light_glow", Vector2.zero, new Vector2(Portrait * .6f, Portrait * .62f),
                new Vector2(Portrait * 1.1f, Portrait * 1.1f), .28f, delay: .25f);
            float size = Portrait * PortraitArtScale;
            var centre = new Vector2(Portrait * .5f, Portrait * .5f + PortraitArtLift);
            // Низ — только нижняя половина круга: верх рисует одна копия, полупрозрачные края волос
            // не ложатся дважды и не темнеют выше середины.
            RectTransform lower = Node("Низ", disk.rectTransform);
            lower.anchorMin = Vector2.zero;
            lower.anchorMax = new Vector2(1f, .5f);
            lower.offsetMin = lower.offsetMax = Vector2.zero;
            lower.gameObject.AddComponent<RectMask2D>();
            view.Portrait = PortraitArt(lower, cutout, centre, size);

            // Верх: от середины круга вверх, по бокам с запасом — волосы шире круга тоже видны.
            RectTransform top = Node("Над кругом", portrait);
            top.anchorMin = new Vector2(0f, .5f);
            top.anchorMax = Vector2.one;
            top.pivot = new Vector2(.5f, .5f);
            top.offsetMin = new Vector2(-PortraitHeadroom, 0f);
            top.offsetMax = new Vector2(PortraitHeadroom, PortraitHeadroom);
            top.gameObject.AddComponent<RectMask2D>();
            // Тот же прямоугольник в координатах маски верха: её начало — (−40, середина круга).
            view.PortraitOuter = PortraitArt(top, cutout, centre - new Vector2(-PortraitHeadroom, Portrait * .5f), size);

            // Уровень: клуб дыма, тёмный диск, тонкое кремовое кольцо и число антиквой.
            RectTransform badge = Box(Node("Уровень", portrait), Vector2.zero, new Vector2(.5f, .5f), new Vector2(Portrait - 18f, 20f),
                new Vector2(LevelBadgeSize, LevelBadgeSize));
            UiInkKit.SmokeLayer(badge, "Дым", "smoke_blot_2", 1f, 9f, 9f, deep: true);
            Layer(badge, "Диск", T.CircleFill, Role.SmokeDeep, .92f);
            Image ring = Layer(badge, "Кольцо", T.CircleFrameBold, Role.Text, 1f);
            Object.DestroyImmediate(ring.GetComponent<ThemeColor>());
            ring.color = new Color(1f, .9f, .74f, .8f);
            view.Level = UiInkKit.Label(badge, "Число", "1", FontRole.Heading, 20f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .2f);
            view.Level.textWrappingMode = TextWrappingModes.NoWrap;
            view.Level.enableAutoSizing = true;
            view.Level.fontSizeMin = 12f;
            view.Level.fontSizeMax = 20f;
            Shadowed(view.Level);
            // Новый уровень — кружок вспыхивает светом.
            Image flare = Mark(badge, "Вспышка уровня", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 110f);
            Additive(flare, new Color(1f, .78f, .4f, 0f));
            flare.enabled = false;
            view.LevelFlare = flare;
        }

        /// <summary>Вырез портрета размером <paramref name="size"/> с центром <paramref name="centre"/> от угла родителя.</summary>
        static RawImage PortraitArt(RectTransform parent, Texture2D art, Vector2 centre, float size)
        {
            RectTransform rect = Box(Node("Портрет", parent), Vector2.zero, new Vector2(.5f, .5f), centre, new Vector2(size, size));
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = art;
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>
        /// Проблеск по элементу: <paramref name="box"/> получает RectMask2D (если на нём нет своей
        /// маски), внутри — косая полоса света; бег полосы ведёт HudGlint.
        /// </summary>
        static HudGlint Glint(RectTransform box, float width, float height, float peak)
        {
            if (box.GetComponent<Mask>() == null) box.gameObject.AddComponent<RectMask2D>();
            RectTransform stripe = Box(Node("Полоса", box), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(width, height));
            stripe.localRotation = Quaternion.Euler(0f, 0f, -24f);
            var image = stripe.gameObject.AddComponent<Image>();
            image.sprite = Kit("wc_fx_glow");
            Additive(image, new Color(1f, .93f, .8f, 0f));
            image.enabled = false;
            var glint = box.gameObject.AddComponent<HudGlint>();
            glint.Stripe = image;
            glint.Peak = peak;
            return glint;
        }

        /// <summary>
        /// Полоса ресурса (вариант B): тёмная дорожка-капсула, заливка цветом ресурса, светлый
        /// след потери; числа внутри, только под мышью.
        /// Долю ставит CombatHudView через HudBarAnim (anchorMax.x заливки).
        /// </summary>
        static RectTransform Vital(RectTransform hero, string name, Role role, float x, float centerY, float height, float font,
            out TMP_Text value, out GameObject row)
        {
            // «Дым и свет»: полоса — мазок кистью. Дорожка — тёмный мазок чуть шире, заливка —
            // мазок цвета ресурса во всю длину под маской (доля — ширина маски, мазок не сжимается),
            // поверх — тот же мазок светом. След потери — светлый мазок.
            RectTransform bar = Box(Node(name, hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x, centerY), new Vector2(BarWidth, height));
            row = bar.gameObject;
            Stretch(UiInkKit.StrokeLayer(bar, "Дорожка", "brush_stroke_1", Role.Smoke, 1f).rectTransform, -4f);
            RectTransform inner = Stretch(Node("Внутри", bar), 2f);
            RectTransform trail = Node("След", inner);
            trail.anchorMin = Vector2.zero;
            trail.anchorMax = new Vector2(.9f, 1f);
            trail.offsetMin = trail.offsetMax = Vector2.zero;
            UiInkKit.StrokeLayer(trail, "Мазок", "brush_stroke_2", Role.Text, .45f);
            trail.gameObject.SetActive(false);
            RectTransform fill = Node("Заполнение", inner);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.8f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<RectMask2D>();
            StrokeFill(fill, BarWidth - 4f, role, 1f, .35f);
            var anim = bar.gameObject.AddComponent<HudBarAnim>();
            anim.Fill = fill;
            anim.Trail = trail;
            value = Label(bar, "Числа", "", FontRole.Body, font, Role.Text, TextAlignmentOptions.Center);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.fontStyle = FontStyles.Bold;
            Shadowed(value);
            return fill;
        }

        /// <summary>
        /// Заливка-мазок во всю длину полосы внутри маски <paramref name="mask"/>: при изменении доли
        /// меняется ширина маски, а мазок остаётся целым. Первым идёт цветной мазок — его цвет
        /// меняет CombatHudView (низкое здоровье), за ним тот же мазок светом.
        /// </summary>
        static void StrokeFill(RectTransform mask, float length, Role role, float alpha, float glow)
        {
            RectTransform stroke = Node("Мазок", mask);
            stroke.anchorMin = Vector2.zero;
            stroke.anchorMax = new Vector2(0f, 1f);
            stroke.pivot = new Vector2(0f, .5f);
            stroke.offsetMin = new Vector2(0f, -2f);
            stroke.offsetMax = new Vector2(length, 2f);
            UiInkKit.StrokeLayer(stroke, "Краска", "brush_stroke_2", role, alpha);
            if (glow <= 0f) return;
            Color light = T.Get(role);
            light.a = glow;
            UiInkKit.LightLayer(stroke, "Свет", "brush_stroke_2", 1f, 3f, new Vector2(0f, .5f), .1f).color = light;
        }
    }
}
