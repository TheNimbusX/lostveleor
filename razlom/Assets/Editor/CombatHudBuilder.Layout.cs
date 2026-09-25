using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Раскладка боевого HUD один в один по ART/UI/concepts-2026-09-15/v3-hud.png.
    /// Размеры сняты с концепта и пересчитаны в единицы Canvas 1920×1080
    /// (1 пиксель концепта 2688 px = 0.714 единицы). Дальше их правят в префабе.
    /// </summary>
    public static partial class CombatHudBuilder
    {
        static readonly Color Cream = Hex(0xF3E8D9);
        static readonly Color NameInk = Hex(0xCFDDEB);
        static readonly Color NavyInk = Hex(0x1C3A5E);
        static readonly Color TitleInk = Hex(0x52708F);
        static readonly Color QuietInk = Hex(0x8098AE);
        static readonly Color PaperLine = Hex(0xD8CDBF);
        static readonly Color Coral = Hex(0xCB5158);
        static readonly Color Cyan = Hex(0x97EBFD);
        static readonly Color Divider = Hex(0x9DB6CB);
        static readonly Color Health = Hex(0xE0676C);
        static readonly Color Lavidium = Hex(0xEFA36A);
        static readonly Color Experience = Hex(0xA9D8F2);
        static readonly Color XpCap = Hex(0xDCEAF5);
        static readonly Color CooldownShade = new Color(.03f, .12f, .24f, .72f);

        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 BottomCenter = new Vector2(.5f, 0f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 TopCenter = new Vector2(.5f, 1f);
        static readonly Vector2 LeftMiddle = new Vector2(0f, .5f);
        static readonly Vector2 Middle = new Vector2(.5f, .5f);

        /// <summary>Спрайты рамок нарисованы с полями 10 px (5 единиц) под свечение.</summary>
        const float FrameBleed = 5f;

        static GameObject BuildLayout(Kit kit)
        {
            var root = new GameObject("CombatHud", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;
            var view = root.AddComponent<CombatHudView>();
            // Чистая сборка = раскладка v2 плюс те же миграции, что дорабатывают ручной префаб.
            view.LayoutVersion = 2;

            BuildHero(root.transform, view, kit);
            BuildAbilities(root.transform, view, kit);
            BuildPotions(root.transform, view, kit);
            BuildTooltip(root.transform, view, kit);
            BuildFeedback(root.transform, view, kit);
            BuildMinimap(root.transform, view, kit);
            view.StatIcons = new[]
            {
                Icon("heart"), Icon("lavidium"), Icon("stat_cooldown"), Icon("stat_damage"),
                Icon("stat_range"), Icon("stat_radius"), Icon("stat_duration"),
            };
            Migrate(root, view);
            return root;
        }

        /// <summary>Доводит префаб до <see cref="LayoutVersion"/>, не трогая остальное, что правил владелец.</summary>
        static void Migrate(GameObject root, CombatHudView view)
        {
            if (view.LayoutVersion < 3) MigrateTo3(root, view);
            if (view.LayoutVersion < 4) MigrateTo4(root, view);
            if (view.LayoutVersion < 5) MigrateTo5(root, view);
            if (view.LayoutVersion < 6) SwapFonts(root);
            if (view.LayoutVersion < 7) ApplyBottomGrid(view);
            if (view.LayoutVersion < 8) PlaceLevelBadge(view);
            if (view.LayoutVersion < 9)
            {
                ApplyBottomGrid(view);
                PlaceLevelBadge(view);
                ScaleMinimap(view, 1.1f, true);
                RefreshHudTextures();
            }
            if (view.LayoutVersion < 10) TidyTooltip(view);
            if (view.LayoutVersion < 11) BiggerMapDetails(view);
            if (view.LayoutVersion < 12) FitTooltipTitle(view);
            view.LayoutVersion = LayoutVersion;
            UnityEditor.EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// v3 по просьбе владельца: весь HUD на 65% (CanvasScaler), имя на
        /// плашке под портретом, карта под маской той же фаски, что рамка, —
        /// без толстого синего поля между рамкой и картой.
        /// </summary>
        static void MigrateTo3(GameObject root, CombatHudView view)
        {
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = new Vector2(2954f, 1662f);

            RectTransform hero = view.HeroPanel;
            var tile = hero != null ? hero.Find("Portrait Tile") as RectTransform : null;
            var plate = view.HeroName != null ? view.HeroName.rectTransform.parent as RectTransform : null;
            if (tile != null && plate != null && plate != hero && plate.IsChildOf(hero))
            {
                // Полосы жизни и лавидия остаются там, где их поставил владелец.
                var keep = new Transform[plate.childCount];
                for (int i = 0; i < keep.Length; i++) keep[i] = plate.GetChild(i);
                // Только локальные координаты: у Canvas в сцене префаба нулевой масштаб,
                // и перенос «с мировой позицией» давал полосам scale 0.
                foreach (Transform child in keep)
                    if (child != view.HeroName.transform)
                    {
                        Vector3 local = plate.localPosition + Vector3.Scale(child.localPosition, plate.localScale);
                        child.SetParent(hero, false);
                        child.localPosition = local;
                    }
                plate.SetParent(hero, false);
                plate.name = "Name Plate";
                plate.anchorMin = plate.anchorMax = Vector2.zero;
                plate.pivot = TopCenter;
                plate.sizeDelta = new Vector2(tile.rect.width, 44f);
                plate.anchoredPosition = new Vector2(tile.anchoredPosition.x + tile.rect.width * (.5f - tile.pivot.x),
                    tile.anchoredPosition.y - tile.rect.height * tile.pivot.y - 6f);
                RectTransform name = view.HeroName.rectTransform;
                name.anchorMin = Vector2.zero; name.anchorMax = Vector2.one; name.pivot = Middle;
                name.offsetMin = name.offsetMax = Vector2.zero;
                view.HeroName.alignment = TextAlignmentOptions.Center;
                view.HeroName.textWrappingMode = TextWrappingModes.NoWrap;
            }

            RectTransform frame = view.MinimapFrame, area = view.MinimapArea;
            if (frame != null && area != null && view.MinimapImage == null)
            {
                var plateImage = frame.GetComponent<Image>();
                if (plateImage != null) Object.DestroyImmediate(plateImage);
                area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
                area.offsetMin = new Vector2(-FrameBleed, -FrameBleed);
                area.offsetMax = new Vector2(FrameBleed, FrameBleed);
                if (area.GetComponent<Image>() == null) Img(area, Chrome("hud_mask"), Color.white);
                if (area.GetComponent<Mask>() == null) area.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var map = Stretch("Map", area, 0f).gameObject.AddComponent<RawImage>();
                map.raycastTarget = false;
                map.enabled = false;
                view.MinimapImage = map;
                Img(Stretch("Frame", frame, -FrameBleed), Chrome("hud_frame"), Color.white);
            }
        }

        /// <summary>
        /// v4 по просьбе владельца. Каждый шаг берёт текущие значения префаба,
        /// а не числа сборщика: ручные правки остаются, меняется только сказанное.
        /// </summary>
        static void MigrateTo4(GameObject root, CombatHudView view)
        {
            ScaleMinimap(view, 1.1f, false);

            // Герой вплотную слева от способностей — по видимому содержимому, не по пустым рамкам.
            PlaceHeroBesideSkills(root.transform, view);

            // Подложка кувырка — та же, что у плиток зелий.
            if (view.DashPanel != null && view.PotionPanel != null)
            {
                Image potion = null;
                foreach (Transform tile in view.PotionPanel)
                {
                    var image = tile.GetComponent<Image>();
                    if (image != null && image.enabled && tile.gameObject.activeSelf) { potion = image; break; }
                }
                var dash = view.DashPanel.GetComponent<Image>();
                if (dash == null) dash = view.DashPanel.gameObject.AddComponent<Image>();
                if (potion != null)
                {
                    dash.sprite = potion.sprite;
                    dash.type = potion.type;
                    dash.color = potion.color;
                    dash.pixelsPerUnitMultiplier = potion.pixelsPerUnitMultiplier;
                }
                dash.raycastTarget = false;
                dash.enabled = true;
            }

            // Числа опыта: белые с синим контуром — читаются и на светлой заливке, и на тёмной дорожке.
            if (view.ExperienceText != null && view.ExperienceText.font != null)
            {
                view.ExperienceText.color = Color.white;
                view.ExperienceText.fontSharedMaterial = OutlineMaterial(view.ExperienceText.font);
            }

        }

        /// <summary>
        /// Рамка карты крупнее в <paramref name="factor"/> раз, при <paramref name="caption"/> —
        /// и лента подписи с текстом и ромбами. Подпись держит прежний отступ
        /// от низа рамки и сдвиг от её центра.
        /// </summary>
        static void ScaleMinimap(CombatHudView view, float factor, bool caption)
        {
            RectTransform frame = view.MinimapFrame, label = view.MinimapCaptionPanel;
            if (frame == null) return;
            Vector2 was = frame.sizeDelta, size = was * factor;
            float bottomWas = frame.anchoredPosition.y - was.y * frame.pivot.y;
            float centerWas = frame.anchoredPosition.x + was.x * (.5f - frame.pivot.x);
            frame.sizeDelta = size;
            if (label == null || label.anchorMin != frame.anchorMin) return;
            float gap = bottomWas - label.anchoredPosition.y, shift = label.anchoredPosition.x - centerWas;
            if (caption)
            {
                label.sizeDelta *= factor;
                foreach (Transform child in label)
                    if (child is RectTransform part) { part.sizeDelta *= factor; part.anchoredPosition *= factor; }
                if (view.MinimapCaption != null) view.MinimapCaption.fontSize *= factor;
            }
            label.anchoredPosition = new Vector2(frame.anchoredPosition.x + size.x * (.5f - frame.pivot.x) + shift,
                frame.anchoredPosition.y - size.y * frame.pivot.y - gap);
        }

        /// <summary>
        /// v9: mip-уровни у всех картинок HUD. Импортёры уже ставят их сами, но
        /// старые файлы нужно переимпортировать явно — и записать в .meta, иначе
        /// копия проекта для съёмки держит прежний импорт.
        /// </summary>
        static void RefreshHudTextures()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { UiKitImport.KitRoot }))
                UiKitImport.Ensure(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI/HUD", "Assets/Resources/UI/Abilities" }))
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)) as UnityEditor.TextureImporter;
                if (importer == null || importer.mipmapEnabled) continue;
                importer.mipmapEnabled = true;
                importer.mipmapFilter = UnityEditor.TextureImporterMipFilter.KaiserFilter;
                importer.mipMapBias = -.3f;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// v10 по замечанию владельца: текст подсказки местами не читался,
        /// отступы между параметрами гуляли, рисованные значки на 30 px
        /// превращались в пятна. Тёмный текст покрупнее; параметры — сетка из
        /// трёх столбцов с постоянными ячейками (значок слева, число сразу за
        /// ним, черта только между столбцами); значки — плоские stat_glyph_*.
        /// </summary>
        static void TidyTooltip(CombatHudView view)
        {
            if (view.TooltipTitle != null) { view.TooltipTitle.color = NavyInk; view.TooltipTitle.fontSize = 26f; view.TooltipTitle.characterSpacing = 16f; }
            if (view.TooltipKey != null) { view.TooltipKey.color = Hex(0x5C7590); view.TooltipKey.fontSize = 18f; }
            if (view.TooltipBody != null) { view.TooltipBody.color = Hex(0x22384F); view.TooltipBody.fontSize = 22f; view.TooltipBody.lineSpacing = 4f; }
            if (view.TooltipStatus != null) view.TooltipStatus.color = Hex(0xB8434B);

            var sample = view.TooltipMetrics != null && view.TooltipMetrics.Length > 0 && view.TooltipMetrics[0] != null ? view.TooltipMetrics[0] : null;
            if (sample != null && sample.transform.parent is RectTransform metrics)
            {
                const int columns = 3;
                const float cellWidth = 132f, cellHeight = 40f;
                var row = metrics.GetComponent<HorizontalLayoutGroup>();
                if (row != null) Object.DestroyImmediate(row);
                var grid = metrics.GetComponent<GridLayoutGroup>();
                if (grid == null) grid = metrics.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(cellWidth, cellHeight);
                grid.spacing = new Vector2(0f, 6f);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                grid.childAlignment = TextAnchor.UpperLeft;
                view.TooltipMetricColumns = columns;
                var element = metrics.GetComponent<LayoutElement>();
                if (element != null) { element.minHeight = element.preferredHeight = -1f; }

                foreach (HudTooltipMetric metric in view.TooltipMetrics)
                {
                    if (metric == null) continue;
                    var cellLayout = metric.GetComponent<LayoutElement>();
                    if (cellLayout != null) Object.DestroyImmediate(cellLayout);
                    if (metric.Separator != null)
                        Place((RectTransform)metric.Separator.transform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(2f, 26f));
                    if (metric.Icon != null)
                    {
                        Place(metric.Icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(14f, 0f), new Vector2(30f, 30f));
                        metric.Icon.preserveAspect = true;
                    }
                    if (metric.Value != null)
                    {
                        Place(metric.Value.rectTransform, LeftMiddle, LeftMiddle, new Vector2(52f, 0f), new Vector2(cellWidth - 58f, 34f));
                        metric.Value.alignment = TextAlignmentOptions.MidlineLeft;
                        metric.Value.color = NavyInk;
                        metric.Value.enableAutoSizing = true;
                        metric.Value.fontSizeMin = 14f;
                        metric.Value.fontSizeMax = 22f;
                        if (view.TooltipTitle != null && view.TooltipTitle.font != null) metric.Value.font = view.TooltipTitle.font;
                    }
                }
            }

            Sprite[] glyphs =
            {
                Chrome("stat_glyph_heart"), Chrome("stat_glyph_lavidium"), Chrome("stat_glyph_cooldown"), Chrome("stat_glyph_damage"),
                Chrome("stat_glyph_range"), Chrome("stat_glyph_radius"), Chrome("stat_glyph_duration"),
            };
            if (System.Array.TrueForAll(glyphs, glyph => glyph != null)) view.StatIcons = glyphs;
        }

        /// <summary>
        /// v11 по просьбе владельца: метки, стрелка и подсказки карты +10%
        /// (MinimapMarkerScale — тот же масштаб рисует и их, и всплывашку),
        /// лента подписи +10%. Заодно лента шире текста: на кадре v10 ромбы
        /// закрывали первую и последнюю буквы «РАЗЛОМ · 1». Описание подсказки
        /// способности — SemiBold: тонкий Regular на креме читался серым.
        /// </summary>
        static void BiggerMapDetails(CombatHudView view)
        {
            RectTransform ribbon = view.MinimapCaptionPanel;
            TMP_Text caption = view.MinimapCaption;
            if (ribbon != null)
            {
                ribbon.sizeDelta *= 1.1f;
                foreach (Transform child in ribbon)
                    if (child is RectTransform part) { part.sizeDelta *= 1.1f; part.anchoredPosition *= 1.1f; }
            }
            if (caption != null)
            {
                caption.fontSize *= 1.1f;
                caption.characterSpacing = 12f;
                float side = 0f;
                if (ribbon != null)
                    foreach (Transform child in ribbon)
                        if (child != caption.transform && child is RectTransform diamond)
                            side = Mathf.Max(side, Mathf.Abs(diamond.anchoredPosition.x) + diamond.sizeDelta.x * .5f + 8f);
                caption.margin = new Vector4(side, 0f, side, 0f);
                caption.enableAutoSizing = true;
                caption.fontSizeMax = caption.fontSize;
                caption.fontSizeMin = 12f;
                if (ribbon != null)
                {
                    // Самая длинная подпись — разлом с двузначной глубиной.
                    float text = caption.GetPreferredValues("Разлом · 10").x;
                    ribbon.sizeDelta = new Vector2(Mathf.Max(ribbon.sizeDelta.x, text + side * 2f), ribbon.sizeDelta.y);
                }
            }

            if (view.TooltipBody != null)
            {
                TMP_FontAsset semibold = EnsureFont("SemiBold");
                if (semibold != null) { view.TooltipBody.font = semibold; view.TooltipBody.fontSharedMaterial = semibold.material; }
            }
        }

        /// <summary>
        /// v12: «ЛАДНО СМАЗАЛ» не влезал в заголовок подсказки — широкий Tektur
        /// с разрядкой рядом с иконкой и подписью клавиши обрезался многоточием.
        /// Заголовок сжимается под ширину (26 → 16), разрядка меньше, подпись
        /// клавиши уже.
        /// </summary>
        static void FitTooltipTitle(CombatHudView view)
        {
            TMP_Text title = view.TooltipTitle;
            if (title != null)
            {
                title.fontSizeMax = Mathf.Max(title.fontSize, 16f);
                title.fontSizeMin = 16f;
                title.enableAutoSizing = true;
                title.characterSpacing = Mathf.Min(title.characterSpacing, 10f);
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Overflow;
            }
            if (view.TooltipKey != null && view.TooltipKey.GetComponent<LayoutElement>() is LayoutElement key)
                key.minWidth = key.preferredWidth = 36f;
        }

        /// <summary>Левый и правый край видимых частей <paramref name="target"/> в координатах <paramref name="space"/>.</summary>
        static bool VisualSpan(Transform space, RectTransform target, out float minX, out float maxX)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.enabled) continue;
                if (graphic is Image image && image.sprite == null && graphic.GetComponent<Mask>() == null && image.color.a <= 0f) continue;
                if (graphic is TMP_Text label && string.IsNullOrEmpty(label.text)) continue;
                Rect rect = graphic.rectTransform.rect;
                foreach (float edge in new[] { rect.xMin, rect.xMax })
                {
                    float x = ToAncestor(space, graphic.rectTransform, new Vector2(edge, rect.center.y)).x;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                }
            }
            return minX <= maxX;
        }

        /// <summary>
        /// Точка из локальных координат <paramref name="from"/> в координаты предка —
        /// по localPosition и localScale, без мировых матриц: у корневого Canvas
        /// в сцене префаба масштаб нулевой, и мировая математика врёт.
        /// </summary>
        static Vector2 ToAncestor(Transform space, Transform from, Vector2 point)
        {
            for (Transform t = from; t != null && t != space; t = t.parent)
                point = Vector2.Scale(point, t.localScale) + (Vector2)t.localPosition;
            return point;
        }

        static void PlaceHeroBesideSkills(Transform root, CombatHudView view)
        {
            RectTransform hero = view.HeroPanel, skills = view.AbilityPanel;
            if (hero == null || skills == null) return;
            hero.anchorMin = hero.anchorMax = new Vector2(skills.anchorMin.x, hero.anchorMin.y);
            if (VisualSpan(root, hero, out _, out float heroRight) && VisualSpan(root, skills, out float skillsLeft, out _))
                hero.anchoredPosition += new Vector2(skillsLeft - 18f - heroRight, 0f);
        }

        /// <summary>
        /// v5 — починка v3/v4. v3 переносила полосы жизни и лавидия с мировой
        /// позицией при нулевом масштабе Canvas: полосы сохранились со scale 0.
        /// Их место восстанавливается по сохранённым значениям владельца: плашка,
        /// из которой их вынули, стояла на 5 правее портрета и на 20 ниже верха
        /// героя. Герой заново встаёт к способностям, метки карты подключаются.
        /// </summary>
        static void MigrateTo5(GameObject root, CombatHudView view)
        {
            RectTransform hero = view.HeroPanel;
            var tile = hero != null ? hero.Find("Portrait Tile") as RectTransform : null;
            foreach (RectTransform fill in new[] { view.HealthFill, view.LavidiumFill })
            {
                var row = fill != null && fill.parent != null && fill.parent.parent != null
                    ? fill.parent.parent.parent as RectTransform : null;
                if (row == null || row.parent != hero || row.localScale != Vector3.zero) continue;
                row.localScale = Vector3.one;
                float tileRight = tile != null ? tile.anchoredPosition.x + tile.rect.width * (1f - tile.pivot.x) : 196f;
                row.anchoredPosition = new Vector2(tileRight + 5f + row.anchoredPosition.x, row.anchoredPosition.y - 20f);
            }
            PlaceHeroBesideSkills(root.transform, view);
        }

        // ---- v7: сетка нижней группы (единицы Canvas при эталонной высоте 1662) ----
        const float GridBottom = 28f;   // отступ от низа экрана
        const float GridHeight = 176f;  // общая высота всех нижних блоков
        const float TopBand = 128f;     // портрет, плитки с клавишами, кувырок; полосы по центру полосы
        const float BandCenter = 20f;   // центр нижней полосы от низа блока: имя, опыт, SPACE
        const float GroupGap = 24f;     // между блоками
        const float SlotSize = 112f, SlotGap = 15f;
        const float HeroWidth = 432f, DashTile = 100f, PotionSize = 84f;

        /// <summary>
        /// v7 по замечанию владельца «плавает, высота не соблюдена»: одна нижняя
        /// линия и одна высота у всех блоков, внутри — две общие полосы. Верхняя
        /// (128): портрет, плитки с выступом клавиш, плитка кувырка, полосы жизни
        /// и лавидия по её центру. Нижняя: плашка имени, опыт и SPACE на одной
        /// центральной линии. Зелья — две плитки на всю высоту. Группа по центру.
        /// Спрайты, цвета и тексты не меняются — только места и размеры.
        /// </summary>
        static void ApplyBottomGrid(CombatHudView view)
        {
            float abilitiesWidth = SlotSize * 4f + SlotGap * 3f;
            // По центру экрана — ряд способностей (v9: владелец видел группу «не по центру»,
            // когда центр считался по всей ширине с тяжёлым блоком героя слева).
            float x = -abilitiesWidth * .5f - GroupGap - HeroWidth;

            RectTransform hero = view.HeroPanel;
            if (hero != null)
            {
                Place(hero, BottomCenter, BottomLeft, new Vector2(x, GridBottom), new Vector2(HeroWidth, GridHeight));
                if (hero.Find("Portrait Tile") is RectTransform tile)
                    Place(tile, BottomLeft, BottomLeft, new Vector2(0f, GridHeight - TopBand), new Vector2(TopBand, TopBand));
                if (view.HeroName != null && view.HeroName.rectTransform.parent is RectTransform plate && plate != hero)
                {
                    Place(plate, BottomLeft, BottomLeft, new Vector2(0f, BandCenter - 20f), new Vector2(TopBand, 40f));
                    view.HeroName.fontSize = 22f;
                    view.HeroName.characterSpacing = 18f;
                }
                float barsCenter = GridHeight - TopBand * .5f;
                PlaceVital(view.HealthFill, new Vector2(TopBand + 16f, barsCenter + 27f));
                PlaceVital(view.LavidiumFill, new Vector2(TopBand + 16f, barsCenter - 27f));
            }
            x += HeroWidth + GroupGap;

            RectTransform skills = view.AbilityPanel;
            if (skills != null)
            {
                Place(skills, BottomCenter, BottomLeft, new Vector2(x, GridBottom), new Vector2(abilitiesWidth, GridHeight));
                for (int i = 0; i < view.Slots.Length; i++)
                    if (view.Slots[i] != null)
                        Place((RectTransform)view.Slots[i].transform, TopLeft, TopLeft,
                            new Vector2(i * (SlotSize + SlotGap), 0f), new Vector2(SlotSize, SlotSize));
                if (view.ExperienceHit != null)
                    Place(view.ExperienceHit, BottomLeft, BottomLeft, new Vector2(0f, BandCenter - 14f), new Vector2(abilitiesWidth, 28f));
                if (view.Feedback != null)
                    view.Feedback.anchoredPosition = new Vector2(x + abilitiesWidth * .5f, GridBottom + GridHeight + 18f);
            }
            x += abilitiesWidth + GroupGap;

            RectTransform dash = view.DashPanel;
            if (dash != null)
            {
                // v9: плитка кувырка почти как зелье и по центру ряда способностей;
                // SPACE сидит на её нижней кромке, как клавиши на плитках.
                float rowCenter = GridHeight - SlotSize * .5f;
                Place(dash, BottomCenter, BottomLeft, new Vector2(x, GridBottom + rowCenter - DashTile * .5f), new Vector2(DashTile, DashTile));
                if (view.Dash != null)
                {
                    const float inset = 12f;
                    Place((RectTransform)view.Dash.transform, TopLeft, TopLeft, new Vector2(inset, -inset),
                        new Vector2(DashTile - inset * 2f, DashTile - inset * 2f));
                    if (view.Dash.Key != null && view.Dash.Key.transform.parent is RectTransform tab)
                    {
                        tab.sizeDelta = new Vector2(80f, 26f);
                        tab.anchoredPosition = new Vector2(0f, -inset);
                    }
                }
            }
            x += DashTile + GroupGap;

            RectTransform potions = view.PotionPanel;
            if (potions != null)
            {
                Place(potions, BottomCenter, BottomLeft, new Vector2(x, GridBottom), new Vector2(PotionSize, GridHeight));
                float step = GridHeight - PotionSize;
                int index = 0;
                foreach (Transform child in potions)
                {
                    if (!(child is RectTransform tile) || index > 1) continue;
                    Place(tile, TopLeft, TopLeft, new Vector2(0f, -index * step), new Vector2(PotionSize, PotionSize));
                    if (tile.Find("Art") is RectTransform art) Place(art, Middle, Middle, Vector2.zero, new Vector2(56f, 56f));
                    index++;
                }
            }
        }

        /// <summary>
        /// v8: значок уровня после сетки v7 наезжал на плашку имени (стоял с
        /// выносом −23 вниз). Теперь он в нижнем левом углу портрета, выносится
        /// только влево и не опускается ниже рамки.
        /// </summary>
        static void PlaceLevelBadge(CombatHudView view)
        {
            if (view.Level == null || !(view.Level.rectTransform.parent is RectTransform badge)) return;
            Place(badge, BottomLeft, BottomLeft, new Vector2(-10f, 6f), new Vector2(44f, 44f));
            view.Level.fontSize = 22f;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.localScale = Vector3.one;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// <summary>Строка «иконка + полоса»: одинаковая ширина у жизни и лавидия.</summary>
        static void PlaceVital(RectTransform fill, Vector2 leftCenter)
        {
            var row = fill != null && fill.parent != null && fill.parent.parent != null ? fill.parent.parent.parent as RectTransform : null;
            if (row == null) return;
            Place(row, BottomLeft, LeftMiddle, leftCenter, new Vector2(HeroWidth - TopBand - 16f, 44f));
            if (row.Find("Icon") is RectTransform icon) Place(icon, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(36f, 36f));
            if (fill.parent.parent is RectTransform track)
            {
                Place(track, LeftMiddle, LeftMiddle, new Vector2(46f, 0f), new Vector2(HeroWidth - TopBand - 16f - 46f, 40f));
                if (track.Find("Value") is RectTransform value && value.GetComponent<TMP_Text>() is TMP_Text label) label.fontSize = 24f;
            }
        }

        /// <summary>
        /// v6: каждая надпись получает то же начертание гарнитуры проекта
        /// (Regular/SemiBold/Bold берутся из имени прежнего SDF-шрифта). Пресет
        /// с контуром переносится на такой же пресет нового шрифта.
        /// </summary>
        static void SwapFonts(GameObject root)
        {
            string family = GameTypography.Family + "-";
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.font == null || label.font.name.StartsWith(family)) continue;
                string name = label.font.name;
                int dash = name.IndexOf('-'), space = name.IndexOf(" SDF", System.StringComparison.Ordinal);
                string weight = dash >= 0 && space > dash ? name.Substring(dash + 1, space - dash - 1) : "Regular";
                TMP_FontAsset font = EnsureFont(weight) ?? EnsureFont("Regular");
                if (font == null) continue;
                bool outlined = label.fontSharedMaterial != null && label.fontSharedMaterial.name.EndsWith(" Outline");
                label.font = font;
                label.fontSharedMaterial = outlined ? OutlineMaterial(font) : font.material;
                UnityEditor.EditorUtility.SetDirty(label);
            }
        }

        /// <summary>Пресет материала шрифта с контуром; лежит рядом со шрифтом и правится в Unity.</summary>
        static Material OutlineMaterial(TMP_FontAsset font)
        {
            string path = FontFolder + "/" + font.name + " Outline.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(font.material) { name = font.name + " Outline" };
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, .3f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.04f, .16f, .32f, 1f));
            UnityEditor.AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---- герой: плашка с именем и полосами, поверх неё рамка портрета ----
        static void BuildHero(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform hero = Node("Hero", parent, BottomLeft, BottomLeft, BottomLeft, new Vector2(42f, 64f), new Vector2(552f, 187f));
            view.HeroPanel = hero;

            RectTransform info = Node("Info Plate", hero, BottomLeft, BottomLeft, BottomLeft, new Vector2(185f, 0f), new Vector2(367f, 167f));
            Img(info, Chrome("hud_panel"), Color.white);
            view.HeroName = Text(Node("Name", info, TopLeft, TopLeft, TopLeft, new Vector2(37f, -12f), new Vector2(300f, 40f)),
                "Пелаг", kit.SemiBold, 30f, NameInk, TextAlignmentOptions.MidlineLeft, 30f);
            view.HeroName.fontStyle = FontStyles.UpperCase;
            view.HealthFill = VitalRow(info, "Health", -80f, Icon("heart"), Health, kit, out view.HealthText);
            RectTransform lavidium = VitalRow(info, "Lavidium", -130f, Icon("lavidium"), Lavidium, kit, out view.LavidiumText);
            view.LavidiumFill = lavidium;
            view.LavidiumRow = lavidium.parent.parent.parent.gameObject;

            RectTransform tile = Node("Portrait Tile", hero, BottomLeft, BottomLeft, BottomLeft, Vector2.zero, new Vector2(196f, 187f));
            Img(tile, Chrome("hud_panel"), Color.white);
            // Портрет не под маской: волосы выходят за верх рамки, как в концепте.
            RectTransform portrait = Stretch("Portrait", tile, 0f);
            portrait.offsetMin = new Vector2(6f, 6f);
            portrait.offsetMax = new Vector2(-6f, 26f);
            var art = portrait.gameObject.AddComponent<RawImage>();
            art.uvRect = new Rect(.145f, .20f, .71f, .80f);
            art.raycastTarget = false;
            art.texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/HUD/PelagPortraitCutout.png");
            view.Portrait = art;

            RectTransform badge = Node("Level Badge", tile, BottomLeft, BottomLeft, BottomLeft, Vector2.zero, new Vector2(57f, 57f));
            Img(badge, Chrome("hud_panel"), Color.white);
            view.Level = Text(Stretch("Level", badge, 0f), "1", kit.SemiBold, 26f, Color.white, TextAlignmentOptions.Center);
        }

        /// <summary>Иконка и полоса со значением. Возвращает заливку; ширину задаёт anchorMax.x.</summary>
        static RectTransform VitalRow(RectTransform panel, string name, float centerY, Sprite icon, Color color, Kit kit, out TMP_Text value)
        {
            RectTransform row = Node(name, panel, TopLeft, TopLeft, LeftMiddle, new Vector2(30f, centerY), new Vector2(310f, 40f));
            Img(Node("Icon", row, LeftMiddle, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(36f, 36f)), icon, Color.white).preserveAspect = true;
            RectTransform track = Node("Track", row, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(51f, 0f), new Vector2(251f, 37f));
            Img(track, Chrome("bar_track"), Color.white);
            RectTransform area = Stretch("Fill Area", track, 4f);
            RectTransform fill = Stretch("Fill", area, 0f);
            Img(fill, Chrome("bar_fill_white"), color);
            fill.anchorMax = new Vector2(.84f, 1f);
            value = Text(Stretch("Value", track, 0f), "0 / 0", kit.SemiBold, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            value.margin = new Vector4(18f, 0f, 8f, 0f);
            return fill;
        }

        // ---- способности, опыт, кувырок ----
        static void BuildAbilities(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform panel = Node("Abilities", parent, BottomCenter, BottomCenter, BottomLeft, new Vector2(-315f, 64f), new Vector2(562f, 202f));
            Img(panel, Chrome("hud_panel"), Color.white);
            view.AbilityPanel = panel;
            string[] keys = { "Q", "W", "E", "R" };
            view.Slots = new HudSlotWidget[4];
            for (int i = 0; i < 4; i++)
            {
                RectTransform slot = Node("Slot " + keys[i], panel, TopLeft, TopLeft, TopLeft, new Vector2(37f + i * 127f, -16f), new Vector2(112f, 112f));
                view.Slots[i] = SlotWidget(slot, kit, keys[i], true);
            }

            RectTransform xp = Node("Experience", panel, BottomLeft, BottomLeft, BottomLeft, new Vector2(31f, 14f), new Vector2(505f, 28f));
            Img(xp, Chrome("bar_track"), Color.white);
            view.ExperienceHit = xp;
            RectTransform cap = Node("XP Cap", xp, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(3f, 0f), new Vector2(54f, 22f));
            Img(cap, Chrome("bar_fill_white"), XpCap);
            Text(Stretch("Label", cap, 0f), "XP", kit.Bold, 17f, NavyInk, TextAlignmentOptions.Center);
            RectTransform area = Stretch("Fill Area", xp, 4f);
            area.offsetMin = new Vector2(62f, 4f);
            RectTransform fill = Stretch("Fill", area, 0f);
            Img(fill, Chrome("bar_fill_white"), Experience);
            fill.anchorMax = new Vector2(.5f, 1f);
            view.ExperienceFill = fill;
            view.ExperienceText = Text(Stretch("Value", area, 0f), "", kit.SemiBold, 17f, NavyInk, TextAlignmentOptions.Center);

            RectTransform dash = Node("Dash", parent, BottomCenter, BottomCenter, BottomLeft, new Vector2(251f, 91f), new Vector2(132f, 153f));
            Img(dash, Chrome("hud_panel"), Color.white);
            view.DashPanel = dash;
            RectTransform dashSlot = Node("Slot SPACE", dash, TopLeft, TopLeft, TopLeft, new Vector2(16f, -12f), new Vector2(100f, 96f));
            view.Dash = SlotWidget(dashSlot, kit, "SPACE", false);
        }

        /// <summary>
        /// Плитка способности: арт под маской с тенью перезарядки, светлая
        /// рамка, голубая рамка при наведении, цифры, плашка лавидия, клавиша.
        /// </summary>
        static HudSlotWidget SlotWidget(RectTransform slot, Kit kit, string key, bool framed)
        {
            var widget = slot.gameObject.AddComponent<HudSlotWidget>();
            widget.Hit = slot;
            RectTransform body = Stretch("Body", slot, 0f);
            widget.Body = body;

            RectTransform mask = Stretch("Mask", body, framed ? -FrameBleed : 0f);
            Img(mask, Chrome("hud_mask"), Color.white);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            RectTransform artRect = Stretch("Art", mask, framed ? FrameBleed : 0f);
            var art = artRect.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            widget.Art = art;
            Image cooldown = Img(Stretch("Cooldown", mask, 0f), Chrome("hud_mask"), CooldownShade, false);
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Vertical;
            cooldown.fillOrigin = (int)Image.OriginVertical.Top;
            widget.Cooldown = cooldown;

            if (framed) widget.Frame = Img(Stretch("Frame", body, -FrameBleed), Chrome("hud_frame"), Color.white);
            RectTransform highlight = Stretch("Highlight", body, framed ? -FrameBleed : 0f);
            widget.Highlight = Img(highlight, Chrome("hud_frame_active"), Color.white);
            highlight.gameObject.SetActive(false);

            widget.CooldownText = Text(Stretch("Cooldown Value", body, 0f), "0.0", kit.Bold, 34f, Cream, TextAlignmentOptions.Center);
            widget.CooldownText.gameObject.SetActive(false);

            RectTransform badge = Node("Lavidium Short", body, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 22f), new Vector2(88f, 28f));
            Img(badge, Chrome("pill_navy"), Color.white);
            Img(Node("Icon", badge, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(14f, 0f), new Vector2(18f, 18f)), Icon("lavidium"), Color.white).preserveAspect = true;
            widget.ResourceText = Text(Node("Value", badge, BottomLeft, TopRight, Middle, new Vector2(10f, 0f), new Vector2(-32f, 0f)),
                "−0", kit.SemiBold, 16f, Lavidium, TextAlignmentOptions.Center);
            widget.ResourceBadge = badge.gameObject;
            badge.gameObject.SetActive(false);

            RectTransform tab = framed
                ? Node("Key Tab", body, BottomCenter, BottomCenter, Middle, Vector2.zero, new Vector2(47f, 32f))
                : Node("Key Tab", body, BottomCenter, BottomCenter, Middle, new Vector2(0f, -20f), new Vector2(93f, 29f));
            Img(tab, framed ? Chrome("keycap") : Chrome("pill_navy"), Color.white);
            widget.Key = Text(Stretch("Key", tab, 0f), key, kit.Bold, framed ? 21f : 17f, Color.white, TextAlignmentOptions.Center, framed ? 0f : 4f);
            return widget;
        }

        static void BuildPotions(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform panel = Node("Potions", parent, BottomCenter, BottomCenter, BottomLeft, new Vector2(401f, 77f), new Vector2(110f, 184f));
            view.PotionPanel = panel;
            string[] art = { "UI/HUD/PotionHealth", "UI/HUD/PotionLavidium" };
            for (int i = 0; i < 2; i++)
            {
                RectTransform tile = Node(i == 0 ? "Health Potion" : "Lavidium Potion", panel, TopLeft, TopLeft, TopLeft,
                    new Vector2(0f, -i * 94f), new Vector2(110f, 90f));
                Img(tile, Chrome("hud_panel"), Color.white);
                RectTransform image = Node("Art", tile, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(12f, 2f), new Vector2(60f, 60f));
                var raw = image.gameObject.AddComponent<RawImage>();
                raw.texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + art[i] + ".png");
                raw.raycastTarget = false;
                // Запас расходников появится вместе с их моделью; фиктивных чисел нет.
                Text(Node("Count", tile, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 8f), new Vector2(36f, 32f)),
                    "", kit.SemiBold, 24f, Cream, TextAlignmentOptions.BottomRight);
            }
        }

        // ---- подсказка: кремовая карточка с хвостиком к плитке ----
        static void BuildTooltip(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform tip = Node("Tooltip", parent, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 300f), new Vector2(444f, 180f));
            Img(tip, Chrome("card_cream"), Color.white);
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(24, 24, 16, 14);
            column.spacing = 8f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Tooltip = tip;
            view.TooltipGap = 28f;

            RectTransform header = Node("Header", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 64f));
            var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Layout(header, -1f, 64f);

            RectTransform ring = Node("Icon Ring", header, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(64f, 64f));
            Img(ring, Chrome("icon_ring"), Color.white).preserveAspect = true;
            Layout(ring, 64f, 64f);
            RectTransform ringMask = Stretch("Mask", ring, 7f);
            Img(ringMask, Chrome("icon_ring"), Color.white);
            ringMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            view.TooltipIcon = Stretch("Icon", ringMask, 0f).gameObject.AddComponent<RawImage>();
            view.TooltipIcon.raycastTarget = false;

            view.TooltipTitle = Text(Node("Title", header, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(250f, 40f)),
                "Способность", kit.SemiBold, 27f, TitleInk, TextAlignmentOptions.MidlineLeft, 25f);
            view.TooltipTitle.fontStyle = FontStyles.UpperCase;
            view.TooltipTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.TooltipTitle.overflowMode = TextOverflowModes.Ellipsis;
            Layout(view.TooltipTitle.rectTransform, -1f, 40f).flexibleWidth = 1f;
            view.TooltipKey = Text(Node("Key", header, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(60f, 40f)),
                "Q", kit.SemiBold, 18f, QuietInk, TextAlignmentOptions.MidlineRight);
            Layout(view.TooltipKey.rectTransform, 60f, 40f);

            RectTransform divider = Node("Divider", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 12f));
            Img(divider, Chrome("divider"), Divider);
            Layout(divider, -1f, 12f);

            view.TooltipBody = Text(Node("Description", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 30f)),
                "Описание", kit.Regular, 21f, NavyInk, TextAlignmentOptions.TopLeft);

            RectTransform line = Node("Line", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 2f));
            Img(line, null, PaperLine);
            Layout(line, -1f, 2f);

            RectTransform metrics = Node("Metrics", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 42f));
            var metricsRow = metrics.gameObject.AddComponent<HorizontalLayoutGroup>();
            metricsRow.childControlWidth = metricsRow.childControlHeight = true;
            metricsRow.childForceExpandWidth = metricsRow.childForceExpandHeight = true;
            Layout(metrics, -1f, 42f);
            view.TooltipMetrics = new HudTooltipMetric[6];
            for (int i = 0; i < view.TooltipMetrics.Length; i++)
            {
                RectTransform item = Node("Metric " + (i + 1), metrics, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(132f, 42f));
                Layout(item, -1f, 42f).flexibleWidth = 1f;
                var metric = item.gameObject.AddComponent<HudTooltipMetric>();
                RectTransform separator = Node("Separator", item, LeftMiddle, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(2f, 30f));
                Img(separator, null, PaperLine);
                metric.Separator = separator.gameObject;
                metric.Icon = Img(Node("Icon", item, Middle, Middle, Middle, new Vector2(-24f, 0f), new Vector2(30f, 30f)), Icon("stat_damage"), Color.white);
                metric.Icon.preserveAspect = true;
                metric.Value = Text(Node("Value", item, Middle, Middle, LeftMiddle, new Vector2(-4f, 0f), new Vector2(70f, 34f)),
                    "0", kit.Regular, 23f, NavyInk, TextAlignmentOptions.MidlineLeft);
                metric.Value.textWrappingMode = TextWrappingModes.NoWrap;
                view.TooltipMetrics[i] = metric;
            }

            view.TooltipStatus = Text(Node("Status", tip, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(396f, 26f)),
                "", kit.SemiBold, 18f, Coral, TextAlignmentOptions.MidlineLeft);
            view.TooltipStatus.gameObject.SetActive(false);

            RectTransform tail = Node("Tail", tip, BottomCenter, BottomCenter, TopCenter, new Vector2(0f, 4f), new Vector2(40f, 25f));
            Img(tail, Chrome("tooltip_tail"), Color.white);
            tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            view.TooltipTail = tail;
            tip.gameObject.SetActive(false);
        }

        static void BuildFeedback(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform pill = Node("Feedback", parent, BottomCenter, BottomCenter, BottomCenter, new Vector2(-34f, 290f), new Vector2(320f, 46f));
            Img(pill, Chrome("pill_navy"), Color.white);
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(32, 32, 8, 8);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Feedback = pill;
            view.FeedbackText = Text(Node("Message", pill, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(256f, 30f)),
                "Перезарядка", kit.SemiBold, 20f, Cream, TextAlignmentOptions.Center);
            view.FeedbackText.textWrappingMode = TextWrappingModes.NoWrap;
            pill.gameObject.SetActive(false);
        }

        // ---- миникарта: рамка и лента с подписью; саму карту рисует IMGUI ----
        static void BuildMinimap(Transform parent, CombatHudView view, Kit kit)
        {
            RectTransform frame = Node("Minimap", parent, TopRight, TopRight, TopRight, new Vector2(-31f, -26f), new Vector2(243f, 239f));
            Img(frame, Chrome("hud_panel"), Color.white);
            view.MinimapFrame = frame;
            view.MinimapArea = Stretch("Map Area", frame, 16f);

            RectTransform caption = Node("Minimap Caption", parent, TopRight, TopRight, TopCenter, new Vector2(-152.5f, -272f), new Vector2(211f, 43f));
            Img(caption, Chrome("pill_navy"), Color.white);
            view.MinimapCaptionPanel = caption;
            for (int side = 0; side < 2; side++)
                Img(Node(side == 0 ? "Diamond L" : "Diamond R", caption, new Vector2(side, .5f), new Vector2(side, .5f), Middle,
                    new Vector2(side == 0 ? 30f : -30f, 0f), new Vector2(12f, 12f)), Chrome("diamond"), Cyan);
            view.MinimapCaption = Text(Stretch("Caption", caption, 0f), "Лагерь", kit.SemiBold, 20f, Cream, TextAlignmentOptions.Center, 25f);
            view.MinimapCaption.fontStyle = FontStyles.UpperCase;
        }

        internal static RectTransform Node(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        internal static RectTransform Stretch(string name, Transform parent, float inset)
        {
            RectTransform rect = Node(name, parent, Vector2.zero, Vector2.one, Middle, Vector2.zero, Vector2.zero);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        internal static Image Img(RectTransform rect, Sprite sprite, Color color, bool slice = true)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.type = slice && sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            return image;
        }

        internal static TextMeshProUGUI Text(RectTransform rect, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment, float spacing = 0f)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.characterSpacing = spacing;
            label.raycastTarget = false;
            return label;
        }

        internal static LayoutElement Layout(RectTransform rect, float width, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) { element.minWidth = width; element.preferredWidth = width; }
            if (height >= 0f) { element.minHeight = height; element.preferredHeight = height; }
            return element;
        }

        internal static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
