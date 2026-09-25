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
    /// размера, разделитель, два зелья размером со способность. Готовность — камень на
    /// верхней кромке плитки (HudReadyGem) вместо свечения. Карта справа сверху.
    /// Единицы Canvas 1920×1080.
    ///
    /// Смысл — в CombatHudView, вид — в префабе. Сборщик создаёт префаб только
    /// если его нет; пересборка из меню спрашивает, потому что стирает ручные правки.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CombatHudWc.prefab";

        // Полоса внизу, единицы Canvas 1920×1080 от низа экрана и от его центра.
        const float Bottom = 14f;        // низ полосы
        const float StripHeight = 112f;
        const float StripLeft = -524f, StripRight = 524f;
        const float Slot = 76f, SlotGap = 14f;
        const float RowBottom = 36f;     // низ плиток: под ними клавиши на кромке
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
            // Над плиткой камень и ряд насечек — подсказка встаёт выше, чтобы их не закрывать.
            view.TooltipGap = 54f;

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
            shade.sizeDelta = new Vector2(0f, 230f);
            var shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.sprite = T.VeilLinear;
            shadeImage.raycastTarget = false;
            // Вуаль темы с 25 сентября .78 (было .62): сила подобрана, чтобы низ игры не потемнел.
            Tint(shadeImage, Role.Veil, .48f);
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

        /// <summary>Ромбик усилений над плиткой, единицы Canvas.</summary>
        const float GemSize = 52f;

        static Sprite Kit(string name)
        {
            string path = UiKitImport.KitRoot + "/Watercolor/" + name + ".png";
            UiKitImport.Ensure(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- полоса
        static void BuildStrip(RectTransform root, CombatHudView view)
        {
            RectTransform strip = Box(Node("Полоса", root), BottomCenter, Vector2.zero, new Vector2(StripLeft, Bottom),
                new Vector2(StripRight - StripLeft, StripHeight));
            view.Strip = strip;
            Image shadow = Layer(strip, "Тень", T.Glow, Role.Veil, .85f, 22f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -5f);
            Layer(strip, "Заливка", T.Fill, Role.Panel, .94f);
            Layer(strip, "Тень снизу", T.ShadeSprite, Role.Veil, .45f);
            Layer(strip, "Свет по кромке", T.HighlightSprite, Role.Highlight);
            Layer(strip, "Рамка", T.Frame, Role.PanelLine, .8f);
            // Ромбы по торцам полосы, как в концепте.
            foreach (float side in new[] { 0f, 1f })
            {
                RectTransform tip = Box(Node(side < .5f ? "Ромб слева" : "Ромб справа", strip), new Vector2(side, .5f), new Vector2(.5f, .5f),
                    Vector2.zero, new Vector2(16f, 16f));
                Layer(tip, "Заливка", T.DiamondFill, Role.Panel);
                Layer(tip, "Оправа", T.DiamondFrameSmall, Role.PanelLine, .9f);
            }
            // Разделитель между кувырком и зельями: тонкая черта с ромбом.
            RectTransform divider = Box(Node("Разделитель", strip), Vector2.zero, new Vector2(.5f, .5f),
                new Vector2(DividerX - StripLeft, StripHeight * .5f), new Vector2(2f, StripHeight - 34f));
            var line = divider.gameObject.AddComponent<Image>();
            line.sprite = T.Pixel;
            line.raycastTarget = false;
            Tint(line, Role.PanelLine, .3f);
            Mark(divider, "Ромб", T.DiamondSmall, Role.PanelLine, .7f, new Vector2(.5f, .5f), Vector2.zero, 12f);
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

            // Портрет пака (кольцо, вырез, ромб уровня), заходит за левый край полосы.
            RectTransform portrait = Place("Portrait", hero, "Портрет");
            Box(portrait, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(200f, 200f));
            portrait.localScale = Vector3.one * (Portrait / 200f);
            view.Portrait = portrait.Find("Диск/Портрет").GetComponent<RawImage>();
            view.Level = portrait.Find("Уровень/Число").GetComponent<TMP_Text>();
            view.Level.text = "1";
            Transform badge = portrait.Find("Уровень");
            badge.localScale = Vector3.one * 1.3f;
            // Новый уровень — ромб на портрете вспыхивает светом.
            Image flare = Mark((RectTransform)badge, "Вспышка уровня", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 150f);
            Additive(flare, new Color(1f, .78f, .4f, 0f));
            flare.enabled = false;
            view.LevelFlare = flare;

            // Медальон артефакта забега на нижней левой кромке портрета, чуть за кольцом (справа внизу —
            // ромб уровня; в концепте 2-artifact-sheet стороны зеркальны). Виден, только пока артефакт есть.
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
            // Клавиша слева от медальона: снизу он стоит у края экрана.
            RectTransform cap = Keycap(medal, "Клавиша", "F", 20f);
            cap.anchorMin = cap.anchorMax = new Vector2(0f, .5f);
            cap.pivot = new Vector2(1f, .5f);
            cap.anchoredPosition = new Vector2(-2f, -6f);
            view.ArtifactKey = cap.Find("Буква").GetComponent<TMP_Text>();
            view.ArtifactSlot = medal.gameObject;
            view.ArtifactHit = medal;
            medal.gameObject.SetActive(false);

            float x = HeroX - left;
            RectTransform name = Box(Node("Имя", hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x, Bottom + 92f - bottom), new Vector2(BarWidth, 28f));
            view.HeroName = Label(name, "Надпись", "Пелаг", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.MidlineLeft, 1f);
            Shadowed(view.HeroName);

            view.HealthFill = Vital(hero, "Здоровье", Role.Health, x, Bottom + 66f - bottom, 20f, 14f, out view.HealthText, out GameObject healthRow);
            view.HealthBar = (RectTransform)healthRow.transform;
            // Удар по герою — полоса вспыхивает светом (HudFx.Flash из CombatHudView).
            Image hit = Layer(view.HealthBar, "Вспышка", T.BarFill, Role.Text, 0f, 3f);
            Additive(hit, new Color(1f, .55f, .45f, 0f));
            hit.type = Image.Type.Sliced;
            hit.enabled = false;
            view.HealthFlash = hit;
            view.LavidiumFill = Vital(hero, "Лавидий", Role.Lavidium, x, Bottom + 42f - bottom, 16f, 12f, out view.LavidiumText, out GameObject row);
            view.LavidiumRow = row;
            // Полный лавидий — редкий проблеск по полосе.
            HudGlint lavidiumGlint = Glint(Stretch(Node("Проблеск", (RectTransform)row.transform), 2f), 30f, 60f, .45f);
            lavidiumGlint.Every = 5f;
            view.LavidiumGlint = lavidiumGlint;

            // Опыт: тонкая полоса под лавидием.
            RectTransform xp = Box(Node("Опыт", hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x + 6f, Bottom + 22f - bottom), new Vector2(BarWidth - 12f, 6f));
            view.ExperienceHit = xp;
            Layer(xp, "Дорожка", T.BarFill, Role.Track);
            RectTransform fill = Node("Заполнение", xp);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.35f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = T.BarFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            Tint(fillImage, Role.Experience, .85f);
            view.ExperienceFill = fill;
            Layer(xp, "Рамка", T.BarFrame, Role.PanelLine, .5f);
            // Опыт прибавился — проблеск по полосе.
            view.ExperienceGlint = Glint(Stretch(Node("Проблеск", xp)), 30f, 50f, .6f);
            // Числа опыта — внутри полосы (владелец 24 сентября): под мышью она подрастает до 16 ед.
            view.ExperienceText = Label(xp, "Числа", "", FontRole.Body, 12f, Role.Text, TextAlignmentOptions.Center);
            view.ExperienceText.textWrappingMode = TextWrappingModes.NoWrap;
            view.ExperienceText.fontStyle = FontStyles.Bold;
            Shadowed(view.ExperienceText);
            view.ExperienceHoverHeight = 16f;
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
        /// след потери, тонкая серебряная рамка и ромбы на торцах; числа внутри, только под мышью.
        /// Долю ставит CombatHudView через HudBarAnim (anchorMax.x заливки).
        /// </summary>
        static RectTransform Vital(RectTransform hero, string name, Role role, float x, float centerY, float height, float font,
            out TMP_Text value, out GameObject row)
        {
            RectTransform bar = Box(Node(name, hero), Vector2.zero, new Vector2(0f, .5f), new Vector2(x, centerY), new Vector2(BarWidth, height));
            row = bar.gameObject;
            Image shadow = Layer(bar, "Тень", T.GlowSmall, Role.Veil, .6f, 6f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            Layer(bar, "Дорожка", T.BarFill, Role.Track);
            RectTransform inner = Stretch(Node("Внутри", bar), 2f);
            RectTransform trail = Node("След", inner);
            trail.anchorMin = Vector2.zero;
            trail.anchorMax = new Vector2(.9f, 1f);
            trail.offsetMin = trail.offsetMax = Vector2.zero;
            var trailImage = trail.gameObject.AddComponent<Image>();
            trailImage.sprite = T.BarFill;
            trailImage.type = Image.Type.Sliced;
            trailImage.raycastTarget = false;
            Tint(trailImage, Role.Text, .4f);
            trail.gameObject.SetActive(false);
            RectTransform fill = Node("Заполнение", inner);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.8f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = T.BarFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            Tint(fillImage, role);
            Layer(bar, "Рамка", T.BarFrame, Role.PanelLine, .7f);
            foreach (float side in new[] { 0f, 1f })
            {
                RectTransform tip = Box(Node(side < .5f ? "Ромб слева" : "Ромб справа", bar), new Vector2(side, .5f), new Vector2(.5f, .5f),
                    Vector2.zero, new Vector2(height + 4f, height + 4f));
                Layer(tip, "Заливка", T.DiamondFill, Role.Panel);
                Layer(tip, "Оправа", T.DiamondFrameSmall, Role.PanelLine, .9f);
                if (side > .5f) Mark(tip, "Камень", T.DiamondSmall, role, 1f, new Vector2(.5f, .5f), Vector2.zero, height * .45f);
            }
            var anim = bar.gameObject.AddComponent<HudBarAnim>();
            anim.Fill = fill;
            anim.Trail = trail;
            value = Label(bar, "Числа", "", FontRole.Body, font, Role.Text, TextAlignmentOptions.Center);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.fontStyle = FontStyles.Bold;
            Shadowed(value);
            return fill;
        }
    }
}
