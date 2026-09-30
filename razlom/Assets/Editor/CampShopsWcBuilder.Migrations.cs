using System.Collections.Generic;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции окон лагерных NPC CampShopsWc поверх ручных правок владельца (по образцу
    /// CombatHudWcBuilder.Migrations). Префаб не пересобирается: каждая миграция берёт то, что лежит
    /// в префабе, и меняет только сказанное; узлы переставляются, а не создаются заново — ссылки
    /// вида и GUID остаются. Координаты — локальные, в единицах раскладки 1920×1080. Номер —
    /// CampShopView.LayoutVersion. Свежая сборка (Build(true)) — раскладка 23 сентября и те же миграции.
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем.
    /// </summary>
    [InitializeOnLoad]
    public static partial class CampShopsWcBuilder
    {
        /// <summary>
        /// Версия раскладки CampShopsWc.
        /// v1 (29.09, ревью владельца, кадры «Кузнец А» и «Торговец А»): окна кузнеца, торговца и
        /// алхимика — в «Раскладке» 1920×1080, которая помещается в холст при масштабе интерфейса
        /// 80–120% и 16:10; кузнец и торговец — одна страница без вкладок: у кузнеца «Перековать» и
        /// «Разобрать» рядом (цена, выход, «Предмет будет уничтожен»), у торговца товары и сумка рядом,
        /// под ними вещь со сравнением и одна кнопка; внизу — клавиши Enter, Del и Esc.
        /// v2 (30.09, единый набор — лист 5): кейкапы (E над NPC, Enter, Del, Esc) — тёмный скруглённый квадрат вместо
        /// круга в дыме. Только форма клавиш: узлы, раскладка и ссылки окон не меняются.
        /// v3 (30.09, владелец: шрифт кнопок и вкладок «как в главном меню»): подписи вкладок («Зелья», «Рецепты» и
        /// спрятанные вкладки кузнеца и торговца) — Philosopher, как у кнопок. Только шрифт: размер и раскладка те же.
        /// </summary>
        public const int LayoutVersion = 3;

        const string FrameName = "Раскладка";
        const string SmokeFrameName = "Раскладка дыма";
        const string SceneName = "Сцена за портретом";
        static readonly string[] Backdrop = { "Вуаль", "Глубина", "Виньетка" };
        static readonly string[] ContentSmoke = { "Тень под содержимым", "Дым под содержимым" };

        // Торговец на одной странице: товары слева, надетое и сумка справа, полоса выбранной вещи снизу.
        const float GoodsX = Left, GoodsW = 592f, TileW = 290f, TileH = 80f, TileStepY = 88f, GoodsY = 216f;
        const float BagX = 1290f, BagW = 590f, BagCell = 52f, BagStepX = 72f, BagStepY = 70f, BagY = 326f;
        const int GoodsCount = 12;
        // Кузнец: две колонки кнопок под карточкой вещи.
        const float ColumnW = 270f, ColumnB = DetailX + 290f;

        static bool _waiting;

        static CampShopsWcBuilder()
        {
            // Съёмочная сборка (batchmode) зовёт миграцию сама — через Build(false).
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static void MigrateWhenIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Wait();
                return;
            }
            // Идёт импорт или компиляция — следующий такт (после компиляции домен перезагрузится сам).
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += MigrateWhenIdle;
                return;
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PrefabPath)
            {
                if (!NeedsMigration()) return;
                Debug.Log("[ui-kit] Окна лагеря открыты в Prefab Mode — доработка до v" + LayoutVersion + " после их закрытия.");
                Wait();
                return;
            }
            EnsureMigrated();
        }

        /// <summary>Повторить после выхода из Play или закрытия префаба — один раз, без двойных подписок.</summary>
        static void Wait()
        {
            if (_waiting) return;
            _waiting = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
            PrefabStage.prefabStageClosing += OnStageClosing;
        }

        static void StopWaiting()
        {
            _waiting = false;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            PrefabStage.prefabStageClosing -= OnStageClosing;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            StopWaiting();
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static void OnStageClosing(PrefabStage stage)
        {
            if (stage.assetPath != PrefabPath) return;
            StopWaiting();
            // Сцена префаба ещё закрывается — файл трогаем на следующем такте.
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static bool NeedsMigration()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var view = prefab != null ? prefab.GetComponent<CampShopView>() : null;
            return view != null && view.LayoutVersion < LayoutVersion;
        }

        /// <summary>
        /// Доводит готовый префаб до <see cref="LayoutVersion"/>; нет префаба или он уже новый — ничего.
        /// true — префаб в последней версии.
        /// </summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponent<CampShopView>();
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponent<CampShopView>();
                int from = view.LayoutVersion;
                Migrate(contents, view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Окна лагеря доработаны поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(GameObject root, CampShopView view)
        {
            if (view.LayoutVersion < 1) MigrateTo1(view);
            if (view.LayoutVersion < 2) Debug.Log("[ui-kit] Окна лагеря: кейкапов листа 5 — " + UiInkKit.RestyleKeycaps(view.transform) + ".");
            if (view.LayoutVersion < 3) MigrateTo3(view);
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// v3 — подписи вкладок → Philosopher (кнопки окон уже на нём: UiInkKit.Button). Вкладки берутся по ссылкам вида
        /// (CampShopScreen.Tabs, CampAlchemyScreen.Tabs), подпись — по самой кнопке (UiInkKit.HeadingLabels), так что
        /// переименованные руками узлы не мешают.
        /// </summary>
        static void MigrateTo3(CampShopView view)
        {
            var tabs = new List<Selectable>();
            foreach (Button[] group in new[] { view.Smith?.Tabs, view.Trader?.Tabs, view.Alchemist?.Tabs })
                if (group != null)
                    foreach (Button tab in group)
                        if (tab != null) tabs.Add(tab);
            Debug.Log("[ui-kit] Окна лагеря: подписей вкладок на Philosopher — " + UiInkKit.HeadingLabels(tabs) + " из " + tabs.Count + ".");
        }

        /// <summary>v1 — ревью владельца 29.09: раскладка в холст, кузнец и торговец на одной странице.</summary>
        static void MigrateTo1(CampShopView view)
        {
            foreach (CanvasGroup group in new[] { view.Smith?.Group, view.Trader?.Group, view.Alchemist?.Group })
                if (group != null) FitToCanvas((RectTransform)group.transform);
            if (view.Smith?.Group != null) SmithOnePage(view.Smith);
            else Debug.LogWarning("[ui-kit] В окнах лагеря нет кузнеца (CampShopView.Smith): одна страница не собрана");
            if (view.Trader?.Group != null) TraderOnePage(view.Trader);
            else Debug.LogWarning("[ui-kit] В окнах лагеря нет торговца (CampShopView.Trader): одна страница не собрана");
        }

        // ---------------------------------------------------------------- раскладка в холст

        /// <summary>
        /// Всё окно, кроме вуалей на весь экран, уходит в «Раскладку» 1920×1080 с опорой на низ по
        /// середине (UiFitFrame): при 80% она меньше экрана, при 120% и 16:10 ужимается целиком. Дым под
        /// содержимым — в свою «Раскладку дыма» (он был под сценой за портретом — порядок тот же). Сцена
        /// за портретом остаётся у левого края экрана и тянется на всю высоту. На 1920×1080 при 100%
        /// ничего не сдвигается: раскладка совпадает с экраном.
        /// </summary>
        static RectTransform FitToCanvas(RectTransform screen)
        {
            if (screen.Find(FrameName) is RectTransform done) return done;
            var children = new List<Transform>();
            foreach (Transform child in screen) children.Add(child);
            RectTransform smoke = FitFrame(screen, SmokeFrameName);
            RectTransform frame = FitFrame(screen, FrameName);
            Transform scene = null;
            foreach (Transform child in children)
            {
                if (System.Array.IndexOf(Backdrop, child.name) >= 0) continue;
                if (child.name == SceneName) { scene = child; continue; }
                child.SetParent(System.Array.IndexOf(ContentSmoke, child.name) >= 0 ? smoke : frame, false);
            }
            if (scene != null)
            {
                smoke.SetSiblingIndex(scene.GetSiblingIndex());
                StretchScene((RectTransform)scene);
            }
            frame.SetAsLastSibling();
            return frame;
        }

        static RectTransform FitFrame(RectTransform screen, string name)
        {
            RectTransform frame = Node(name, screen);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, 0f);
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(1920f, 1080f);
            frame.gameObject.AddComponent<UiFitFrame>();
            return frame;
        }

        /// <summary>Сцена за портретом — у левого края, на всю высоту экрана; ширина — по пропорции рисунка.</summary>
        static void StretchScene(RectTransform scene)
        {
            Vector2 size = scene.rect.size.x > 1f && scene.rect.size.y > 1f ? scene.rect.size : scene.sizeDelta;
            float aspect = size.y > 1f ? size.x / size.y : 720f / 1080f;
            scene.anchorMin = new Vector2(0f, 0f);
            scene.anchorMax = new Vector2(0f, 1f);
            scene.pivot = new Vector2(0f, .5f);
            scene.anchoredPosition = Vector2.zero;
            scene.sizeDelta = new Vector2(aspect * 1080f, 0f);
            var fitter = scene.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = scene.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = aspect;
        }

        static RectTransform FrameOf(CampShopScreen s)
        {
            var screen = (RectTransform)s.Group.transform;
            return screen.Find(FrameName) as RectTransform ?? screen;
        }

        // ---------------------------------------------------------------- общие детали

        /// <summary>Рамка надписи, сделанной Text(): узел-коробка над «Надписью»; иначе — сама надпись.</summary>
        static RectTransform BoxOf(TMP_Text label)
        {
            if (label == null) return null;
            var box = label.transform.parent as RectTransform;
            return box == null || box.GetComponent<UiFitFrame>() != null || box.GetComponent<CanvasGroup>() != null ? label.rectTransform : box;
        }

        static void Place(TMP_Text label, float x, float y, float w, float h)
        {
            RectTransform box = BoxOf(label);
            if (box != null) TopLeft(box, x, y, w, h);
        }

        static void Place(Component part, float x, float y, float w, float h)
        {
            if (part != null) TopLeft((RectTransform)part.transform, x, y, w, h);
        }

        static void Fit(TMP_Text label, float max, float min)
        {
            if (label == null) return;
            label.enableAutoSizing = true;
            label.fontSizeMax = max;
            label.fontSizeMin = min;
            label.fontSize = max;
        }

        static void HideTabs(CampShopScreen s)
        {
            if (s.Tabs == null) return;
            foreach (Button tab in s.Tabs)
                if (tab != null) tab.gameObject.SetActive(false);
        }

        /// <summary>Что делает NPC — под заголовком, на месте вкладок (кадр «Кузнец А»).</summary>
        static void Subtitle(RectTransform frame, CampShopScreen s, string text)
        {
            if (s.Subtitle != null) return;
            s.Subtitle = Text(frame, "Подзаголовок", text, Left, 126f, Right - Left, 32f, FontRole.Body, 21f, Role.TextMuted, TextAlignmentOptions.Center);
            s.Subtitle.characterSpacing = 1.5f;
        }

        /// <summary>
        /// Клавиши внизу справа: Esc — у правого края (подпись «Закрыть» или «Отменить»), левее —
        /// Enter (основная кнопка) и Del (разбор у кузнеца). Пары прячет и подписывает окно.
        /// </summary>
        static void KeyRows(RectTransform frame, CampShopScreen s, string main, string second)
        {
            if (frame.Find("Клавиши") is RectTransform close)
            {
                TopLeft(close, Right - 200f, 1000f, 200f, 36f);
                if (s.CloseKeyLabel == null && close.Find("Подпись") is Transform caption) s.CloseKeyLabel = caption.GetComponentInChildren<TMP_Text>(true);
            }
            if (frame.Find("Клавиши действий") != null) return;
            RectTransform row = TopLeft(Node("Клавиши действий", frame), Right - 200f - 800f, 1000f, 790f, 36f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 28f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            s.MainKey = KeyPair(row, "Enter", main, out s.MainKeyLabel);
            if (second != null) s.SecondKey = KeyPair(row, "Del", second, out s.SecondKeyLabel);
        }

        /// <summary>Пара «клавиша + подпись» — общая подсказка листа 5 «[Enter] Перековать» (UiInkKit.KeyHint).</summary>
        static GameObject KeyPair(RectTransform row, string key, string text, out TMP_Text label)
            => UiInkKit.KeyHint(row, key, text, out label, 32f, 18f).gameObject;

        /// <summary>Отметка «сколько за неё дадут» под ячейкой сумки торговца; ячейка несёт её с собой.</summary>
        static TMP_Text CellPrice(CampShopCell cell)
        {
            var rect = (RectTransform)cell.transform;
            if (rect.Find("Цена") is Transform old) return old.GetComponentInChildren<TMP_Text>(true);
            RectTransform box = Node("Цена", rect);
            box.anchorMin = box.anchorMax = new Vector2(.5f, 0f);
            box.pivot = new Vector2(.5f, 1f);
            box.anchoredPosition = new Vector2(0f, -1f);
            box.sizeDelta = new Vector2(BagStepX, 16f);
            TMP_Text label = UiInkKit.Label(box, "Надпись", "", FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.Coins, TextAlignmentOptions.Center, 0f, 1f, .15f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>
        /// Товар прилавка: вещь в ячейке, название и цена; нажимается весь. У выбранного — полоса дыма
        /// и нить света (как у строки списка).
        /// </summary>
        static CampShopGood GoodTile(RectTransform parent, int index, float x, float y)
        {
            var good = new CampShopGood();
            RectTransform tile = TopLeft(Node("Товар " + (index + 1), parent), x, y, TileW, TileH);
            RectTransform chosen = Stretch(Node("Выбрано", tile));
            UiInkKit.SmokeLayer(chosen, "Подложка", "smoke_plate", .9f, 14f, 6f);
            UiInkKit.LightAt(chosen, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(TileW + 30f, 26f), .6f);
            chosen.gameObject.SetActive(false);
            good.Chosen = chosen.gameObject;
            good.Button = Clickable(tile);
            tile.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.02f;
            const float cell = 72f;
            good.Cell = Cell(tile, "Вещь", 4f, (TileH - cell) * .5f, cell, false);
            good.Name = Text(tile, "Название", "", 90f, 2f, TileW - 94f, 46f, FontRole.Heading, 19f, Role.Text, TextAlignmentOptions.BottomLeft);
            Fit(good.Name, 19f, 14f);
            RectTransform price = TopLeft(Node("Цена", tile), 90f, 50f, TileW - 94f, 28f);
            good.PriceGroup = price.gameObject;
            Pic(price, "Значок золота", "gold", 0f, 2f, 24f, 24f);
            good.Price = Text(price, "Число", "0", 30f, 0f, 120f, 28f, FontRole.Heading, 21f, Role.Coins);
            good.Price.textWrappingMode = TextWrappingModes.NoWrap;
            return good;
        }

        // ---------------------------------------------------------------- кузнец

        /// <summary>
        /// Кузнец одной страницей (кадр «Кузнец А»): слева надетое и сумка — как были; справа вещь,
        /// «Перековки 1 / 3» с тремя отметками, «Что перековать» со строками свойств и две колонки:
        /// «Перековать» (цена, уровень после, причина) и «Разобрать» (выход, «Предмет будет уничтожен»,
        /// запрет или подтверждение).
        /// </summary>
        static void SmithOnePage(CampShopScreen s)
        {
            RectTransform frame = FrameOf(s);
            HideTabs(s);
            Subtitle(frame, s, CampServiceText.Get("smith.subtitle"));
            if (s.GridCaption != null) s.GridCaption.text = CampServiceText.Get("smith.bag.caption");

            Place(s.Item, DetailX, 196f, 132f, 132f);
            Place(s.ItemName, DetailX + 152f, 204f, DetailW - 152f, 50f);
            Place(s.ItemMeta, DetailX + 152f, 258f, DetailW - 152f, 60f);
            if (frame.Find("Линия под вещью") is RectTransform under) TopLeft(under, DetailX, 336f, DetailW, 16f);

            // Перековки: подпись слева, «1 / 3» и три круглые отметки справа.
            if (s.ReforgeCount == null)
            {
                Text(frame, "Перековки", CampServiceText.Get("smith.attempts"), DetailX, 352f, 260f, 36f, FontRole.Body, 20f, Role.TextMuted);
                s.ReforgeCount = Text(frame, "Счёт перековок", "0 / 3", DetailX + DetailW - 200f, 352f, 108f, 36f, FontRole.Heading, 22f, Role.Text,
                    TextAlignmentOptions.MidlineRight);
                RectTransform pips = TopLeft(Node("Отметки перековок", frame), DetailX + DetailW - 82f, 352f, 82f, 36f);
                s.ReforgePips = new Image[CampShopDeals.ReforgeLimit];
                for (int i = 0; i < s.ReforgePips.Length; i++)
                {
                    Image pip = Mark(pips, "Отметка " + (i + 1), T.CircleFill, Role.TextMuted, .35f, new Vector2(0f, .5f), new Vector2(14f + i * 26f, 0f), 14f);
                    pip.material = UiInkKit.Plain;
                    UiInkKit.Inked(pip, delay: .1f + i * .04f);
                    s.ReforgePips[i] = pip;
                }
            }
            if (s.AffixCaption == null) s.AffixCaption = SectionHeader(frame, "Подпись свойств", CampServiceText.Get("smith.affixes"), DetailX, 402f, DetailW);
            for (int i = 0; i < s.Rows.Length; i++)
            {
                if (s.Rows[i] != null) TopLeft((RectTransform)s.Rows[i].transform, DetailX, 446f + i * 48f, DetailW, 44f);
                if (i < s.RowLabels.Length && s.RowLabels[i] != null) s.RowLabels[i].fontSize = 19f;
            }
            if (frame.Find("Линия над ценой") is RectTransform over) TopLeft(over, DetailX, 742f, DetailW, 16f);

            // Колонка «Перековать»: кнопка, цена, уровень вещи после, причина.
            Place(s.Action, DetailX + 12f, 764f, ColumnW - 24f, 58f);
            Fit(s.ActionLabel, 22f, 15f);
            if (s.Price != null)
            {
                var price = (RectTransform)s.Price.transform;
                TopLeft(price, DetailX, 834f, ColumnW, 40f);
                if (price.Find("Подпись") is Transform caption) caption.gameObject.SetActive(false);
                if (price.Find("Значок золота") is RectTransform goldIcon) TopLeft(goldIcon, 46f, 4f, 32f, 32f);
                Place(s.PriceGold, 82f, 0f, 64f, 40f);
                if (s.PriceGold != null) s.PriceGold.fontSize = 24f;
                if (s.PriceShardsGroup != null)
                {
                    var shards = (RectTransform)s.PriceShardsGroup.transform;
                    TopLeft(shards, 150f, 0f, 120f, 40f);
                    if (shards.Find("Значок осколков") is RectTransform shardIcon) TopLeft(shardIcon, 0f, 4f, 32f, 32f);
                }
                Place(s.PriceShards, 36f, 0f, 76f, 40f);
                if (s.PriceShards != null) s.PriceShards.fontSize = 24f;
            }
            Place(s.Preview, DetailX, 878f, ColumnW, 28f);
            if (s.Preview != null) { s.Preview.fontSize = 17f; s.Preview.alignment = TextAlignmentOptions.Center; Tint(s.Preview, Role.TextMuted); }
            Place(s.Note, DetailX, 906f, ColumnW, 62f);
            if (s.Note != null) { Fit(s.Note, 16f, 12f); s.Note.alignment = TextAlignmentOptions.Top; }

            // Колонка «Разобрать»: вторая кнопка (поле Extra у кузнеца было пустым), выход и предупреждение.
            if (s.Extra == null)
            {
                s.Extra = InkButton(frame, false, "Разобрать", CampServiceText.Get("smith.dismantle.action"), ColumnB + 12f, 764f, ColumnW - 24f, 58f, 22f, out s.ExtraLabel);
                ButtonArt(s.Extra, "dismantle", 50f);
                Fit(s.ExtraLabel, 22f, 15f);
            }
            if (s.Yield == null)
            {
                RectTransform yield = TopLeft(Node("Выход разбора", frame), ColumnB, 834f, ColumnW, 40f);
                Pic(yield, "Значок осколков", "shards", 40f, 4f, 32f, 32f);
                s.YieldShards = Text(yield, "Число", "+0", 78f, 0f, ColumnW - 80f, 40f, FontRole.Heading, 22f, Role.Text);
                s.YieldShards.textWrappingMode = TextWrappingModes.NoWrap;
                s.Yield = yield.gameObject;
            }
            if (s.ExtraNote == null)
            {
                s.ExtraNote = Text(frame, "Под разбором", CampServiceText.Get("smith.destroy.warning"), ColumnB, 878f, ColumnW, 90f, FontRole.Body, 16f, Role.TextMuted,
                    TextAlignmentOptions.Top);
                Fit(s.ExtraNote, 16f, 12f);
            }
            KeyRows(frame, s, CampServiceText.Get("smith.reforge.action"), CampServiceText.Get("smith.dismantle.action"));
        }

        // ---------------------------------------------------------------- торговец

        /// <summary>
        /// Торговец одной страницей (кадр «Торговец А»): слева «Товары» — плитки с вещью, названием и
        /// ценой; справа «Надето» и «Сумка» (48 ячеек с ценой продажи под каждой); ниже полоса выбранной
        /// вещи: вещь, свойства, сравнение с надетым и одна кнопка с причиной под ней; внизу слева —
        /// «Обновить товары» и шансы редкого.
        /// </summary>
        static void TraderOnePage(CampShopScreen s)
        {
            RectTransform frame = FrameOf(s);
            HideTabs(s);
            Subtitle(frame, s, CampServiceText.Get("trader.subtitle"));

            if (s.Goods == null || s.Goods.Length == 0)
            {
                s.GoodsCaption = SectionHeader(frame, "Подпись товаров", CampServiceText.Get("trader.stock.caption"), GoodsX, 172f, GoodsW);
                RectTransform goods = Stretch(Node("Товары", frame));
                s.Goods = new CampShopGood[GoodsCount];
                for (int i = 0; i < GoodsCount; i++)
                    s.Goods[i] = GoodTile(goods, i, GoodsX + i % 2 * (TileW + 12f), GoodsY + i / 2 * TileStepY);
            }

            // Надето и сумка — справа; ячейки те же, только меньше и ближе.
            Place(BoxOf(s.WornCaption), BagX, 172f, BagW, 30f);
            for (int i = 0; i < s.Worn.Length; i++)
                if (s.Worn[i] != null) TopLeft((RectTransform)s.Worn[i].transform, BagX + i * 64f, 214f, 56f, 56f);
            Place(BoxOf(s.GridCaption), BagX, 282f, BagW, 30f);
            if (s.GridCaption != null) s.GridCaption.text = CampServiceText.Get("trader.bag.caption");
            var prices = new TMP_Text[s.Cells.Length];
            for (int i = 0; i < s.Cells.Length; i++)
            {
                if (s.Cells[i] == null) continue;
                TopLeft((RectTransform)s.Cells[i].transform, BagX + i % 8 * BagStepX, BagY + i / 8 * BagStepY, BagCell, BagCell);
                prices[i] = CellPrice(s.Cells[i]);
            }
            s.CellPrices = prices;

            // Низ слева: обновление товаров и шансы редкого.
            Place(s.Extra, GoodsX + 16f, 950f, 316f, 50f);
            Fit(s.ExtraLabel, 19f, 14f);
            Place(s.Info, GoodsX + 350f, 950f, 420f, 50f);
            if (s.Info != null) { s.Info.fontSize = 15f; s.Info.alignment = TextAlignmentOptions.MidlineLeft; }

            // Полоса выбранной вещи.
            if (frame.Find("Линия под вещью") is RectTransform under) TopLeft(under, GoodsX, 748f, Right - Left, 16f);
            Place(s.Item, GoodsX + 12f, 774f, 116f, 116f);
            Place(s.ItemName, GoodsX + 146f, 766f, 440f, 44f);
            Fit(s.ItemName, 30f, 20f);
            Place(s.ItemMeta, GoodsX + 146f, 810f, 440f, 28f);
            if (s.ItemMeta != null) { Fit(s.ItemMeta, 16f, 12f); s.ItemMeta.textWrappingMode = TextWrappingModes.NoWrap; }
            Place(s.Detail, GoodsX + 146f, 840f, 440f, 100f);
            Fit(s.Detail, 17f, 12f);
            if (s.Compare == null)
            {
                s.Compare = Text(frame, "Сравнение", "", 1262f, 768f, 340f, 172f, FontRole.Body, 17f, Role.Text, TextAlignmentOptions.TopLeft);
                Fit(s.Compare, 17f, 12f);
                s.Compare.lineSpacing = 2f;
            }
            if (frame.Find("Линия над ценой") is RectTransform over) over.gameObject.SetActive(false);
            if (BoxOf(s.Preview) is RectTransform preview) preview.gameObject.SetActive(false);
            if (s.Price != null) s.Price.SetActive(false);
            Place(s.Action, 1624f, 790f, 246f, 60f);
            Fit(s.ActionLabel, 22f, 14f);
            Place(s.Note, 1612f, 860f, 268f, 84f);
            if (s.Note != null) { Fit(s.Note, 16f, 12f); s.Note.alignment = TextAlignmentOptions.Top; }
            KeyRows(frame, s, CampServiceText.Get("trader.buy"), null);
        }
    }
}
