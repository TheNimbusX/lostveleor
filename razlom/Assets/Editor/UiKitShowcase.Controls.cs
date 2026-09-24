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
    /// Вторая страница витрины — элементы управления по листу
    /// ART/UI/concepts-2026-09-22/final-2/kit-sheet-3-controls.png.
    /// Префаб: Assets/UI/Kit/Watercolor/ShowcaseControls.prefab.
    /// </summary>
    public static partial class UiKitShowcase
    {
        public const string ControlsPrefabPath = UiKitImport.KitRoot + "/Watercolor/ShowcaseControls.prefab";

        [MenuItem("Разлом/UI/Пак «Ночная акварель» — пересобрать витрину управления")]
        public static void RebuildControlsFromMenu() => RebuildControls();

        public static string RebuildControls()
        {
            EnsurePrefabs();
            RectTransform root = BuildControls();
            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject, ControlsPrefabPath); }
            finally { Object.DestroyImmediate(root.gameObject); }
            AssetDatabase.SaveAssets();
            return ControlsPrefabPath;
        }

        static RectTransform Box(RectTransform root, string name, float x, float y, float w, float h)
        {
            RectTransform box = TopLeft(Place("Panel", root, name), x, y, w, h);
            return (RectTransform)box.Find("Содержимое");
        }

        static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font = FontRole.Body,
            float size = 21f, Role role = Role.Text, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            return Label(box, "Надпись", text, font, size, role, align);
        }

        static RectTransform BuildControls()
        {
            UiTheme t = UiThemeBuilder.Ensure(false) ?? UiTheme.Current;
            RectTransform root = Page("Витрина управления", t);

            // ---------------- ряд 1
            TopLeft(SectionHeader(root, "Заголовок слайдеров", "Слайдеры", 515f), 46f, 50f, 515f, 30f);
            TopLeft(SectionHeader(root, "Заголовок переключателей", "Переключатели", 475f), 600f, 50f, 475f, 30f);
            TopLeft(SectionHeader(root, "Заголовок списка", "Выпадающий список", 460f), 1118f, 50f, 460f, 30f);
            TopLeft(SectionHeader(root, "Заголовок прокрутки", "Прокрутка", 250f), 1625f, 50f, 250f, 30f);

            RectTransform sliders = Box(root, "Слайдеры", 46f, 104f, 515f, 165f);
            Mark(sliders, "Значок", CombatHudBuilder.Icon("menu_sound"), Role.Text, .9f, new Vector2(0f, 1f), new Vector2(48f, -52f), 30f);
            Text(sliders, "Громкость", "Громкость", 80f, 36f, 300f, 32f);
            TopLeft(Place("Slider", sliders, "Ползунок"), 36f, 92f, 330f, 28f);
            RectTransform pct = TopLeft(Place("Pill", sliders, "Значение"), 390f, 86f, 94f, 40f);
            pct.Find("Надпись").GetComponent<TMP_Text>().text = "70%";

            RectTransform toggles = Box(root, "Переключатели", 600f, 104f, 475f, 230f);
            RectTransform sw1 = TopLeft(Place("Switch", toggles, "Тумблер вкл"), 34f, 34f, 60f, 30f);
            Text(toggles, "Подпись 1", "Включено", 108f, 32f, 130f, 34f);
            RectTransform sw2 = TopLeft(Place("Switch", toggles, "Тумблер выкл"), 256f, 34f, 60f, 30f);
            sw2.GetComponent<Toggle>().isOn = false; sw2.GetComponent<WcToggleVisual>().Apply();
            Text(toggles, "Подпись 2", "Выключено", 330f, 32f, 140f, 34f, FontRole.Body, 21f, Role.TextMuted);
            TopLeft(Place("Checkbox", toggles, "Флажок вкл"), 49f, 104f, 30f, 30f);
            Text(toggles, "Подпись 3", "Выбрать", 108f, 102f, 130f, 34f);
            RectTransform cb2 = TopLeft(Place("Checkbox", toggles, "Флажок выкл"), 271f, 104f, 30f, 30f);
            cb2.GetComponent<Toggle>().isOn = false; cb2.GetComponent<WcToggleVisual>().Apply();
            Text(toggles, "Подпись 4", "Не выбирать", 330f, 102f, 140f, 34f, FontRole.Body, 21f, Role.TextMuted);
            TopLeft(Place("Radio", toggles, "Радио вкл"), 49f, 170f, 30f, 30f);
            Text(toggles, "Подпись 5", "Опция 1", 108f, 168f, 130f, 34f);
            RectTransform r2 = TopLeft(Place("Radio", toggles, "Радио выкл"), 271f, 170f, 30f, 30f);
            r2.GetComponent<Toggle>().isOn = false; r2.GetComponent<WcToggleVisual>().Apply();
            Text(toggles, "Подпись 6", "Опция 2", 330f, 168f, 140f, 34f, FontRole.Body, 21f, Role.TextMuted);

            RectTransform dropdown = Box(root, "Список", 1118f, 104f, 460f, 300f);
            TopLeft(Place("DropdownField", dropdown, "Поле"), 20f, 18f, 420f, 66f);
            RectTransform open = TopLeft(Place("Panel", dropdown, "Раскрытый список"), 20f, 92f, 420f, 190f);
            string[] res = { "1920 × 1080", "1600 × 900", "1280 × 720", "1024 × 768" };
            for (int i = 0; i < res.Length; i++)
            {
                RectTransform row = TopLeft(Place("ListItem", (RectTransform)open.Find("Содержимое"), "Строка " + res[i]), 6f, 6f + i * 45f, 408f, 44f);
                row.Find("Надпись").GetComponent<TMP_Text>().text = res[i];
                if (i == 0)
                {
                    row.Find("Подложка").gameObject.SetActive(true);
                    row.Find("Маркер").GetComponent<ThemeColor>().SetRole(Role.Accent);
                }
            }

            RectTransform scroll = Box(root, "Прокрутка", 1625f, 104f, 250f, 300f);
            At(Place("Scrollbar", scroll, "Полоса прокрутки"), new Vector2(.5f, .5f), Vector2.zero, new Vector2(24f, 250f));

            // ---------------- ряд 2
            TopLeft(SectionHeader(root, "Заголовок диалога", "Диалог", 555f), 46f, 395f, 555f, 30f);
            TopLeft(SectionHeader(root, "Заголовок уведомлений", "Уведомления", 575f), 640f, 440f, 575f, 30f);
            TopLeft(SectionHeader(root, "Заголовок подсказок", "Подсказки управления", 620f), 1255f, 440f, 620f, 30f);

            TopLeft(Place("Dialog", root, "Диалог"), 46f, 450f, 555f, 230f);
            RectTransform toast = TopLeft(Place("Toast", root, "Уведомление"), 640f, 500f, 540f, 120f);
            var toastIcon = toast.Find("Содержимое/Предмет/Предмет").GetComponent<RawImage>();
            toastIcon.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/potion_lavidium_large.png");
            toastIcon.enabled = true;
            toast.Find("Содержимое/Предмет").GetComponent<WcRarity>().Set(WcRarity.Tier.Rare);

            RectTransform keys = Box(root, "Подсказки", 1255f, 500f, 620f, 180f);
            string[] kk = { "E", "Esc", "Tab", "ЛКМ" };
            string[] kc = { "Действие", "Меню", "Инвентарь", "Левый клик" };
            for (int i = 0; i < 4; i++)
            {
                RectTransform hint = TopLeft(Place("KeyHint", keys, "Клавиша " + kk[i]), 30f + i * 145f, 34f, 130f, 110f);
                hint.Find("Клавиша/Буква").GetComponent<TMP_Text>().text = kk[i];
                hint.Find("Подпись/Надпись").GetComponent<TMP_Text>().text = kc[i];
                var capRect = (RectTransform)hint.Find("Клавиша");
                capRect.sizeDelta = new Vector2(Mathf.Max(56f, 24f + kk[i].Length * 17f), 56f);
                if (kk[i] == "ЛКМ")
                {
                    // Мышь вместо букв, как на эталоне.
                    capRect.sizeDelta = new Vector2(56f, 56f);
                    capRect.Find("Буква").gameObject.SetActive(false);
                    Mark(capRect, "Мышь", t.Mouse, Role.Text, .95f, new Vector2(.5f, .5f), Vector2.zero, 30f);
                }
            }

            // ---------------- ряд 3
            TopLeft(SectionHeader(root, "Заголовок валюты", "Валюта", 555f), 46f, 725f, 555f, 30f);
            TopLeft(SectionHeader(root, "Заголовок бейджей", "Бейджи", 660f), 640f, 725f, 660f, 30f);
            TopLeft(SectionHeader(root, "Заголовок загрузки", "Загрузка", 520f), 1355f, 725f, 520f, 30f);

            RectTransform money = Box(root, "Валюта", 46f, 780f, 555f, 170f);
            TopLeft(Place("Currency", money, "Монеты"), 40f, 50f, 230f, 70f);
            RectTransform lav = TopLeft(Place("Currency", money, "Лавидий"), 310f, 50f, 230f, 70f);
            lav.Find("Значок").GetComponent<Image>().sprite = CombatHudBuilder.Icon("lavidium");
            lav.Find("Значок").GetComponent<ThemeColor>().SetRole(Role.Lavidium);
            lav.Find("Подпись").GetComponent<TMP_Text>().text = "Лавидий";
            lav.Find("Число").GetComponent<TMP_Text>().text = "36";
            lav.Find("Число").GetComponent<ThemeColor>().SetRole(Role.Lavidium);
            RectTransform sep = TopLeft(Node("Разделитель", money), 286f, 40f, 1.5f, 90f);
            Tint(sep.gameObject.AddComponent<Image>(), Role.PanelLine, .4f).GetComponent<Image>().sprite = t.Pixel;

            RectTransform badges = Box(root, "Бейджи", 640f, 780f, 660f, 205f);
            TopLeft(Place("LevelBadge", badges, "Уровень"), 40f, 26f, 90f, 90f);
            Text(badges, "Подпись уровня", "Уровень", 20f, 140f, 130f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center);
            RectTransform stack = TopLeft(Place("Cell", badges, "Стопка"), 190f, 26f, 100f, 100f);
            var stackIcon = stack.Find("Предмет").GetComponent<RawImage>();
            stackIcon.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/potion_health_small.png");
            stackIcon.enabled = true;
            TMP_Text count = Label(TopLeft(Node("Количество", stack), 50f, 66f, 44f, 30f), "Надпись", "×3", FontRole.Body, 20f, Role.Text, TextAlignmentOptions.BottomRight);
            count.fontStyle = FontStyles.Bold;
            Text(badges, "Подпись стопки", "Стопка", 170f, 140f, 140f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center);
            // «ур. 4» на эталоне — крупная плашка антиквой, «Новый» — малая с бирюзовым ромбом в серебре.
            RectTransform level = TopLeft(Place("Tag", badges, "Уровень предмета"), 344f, 38f, 102f, 64f);
            TMP_Text levelLabel = level.Find("Надпись").GetComponent<TMP_Text>();
            levelLabel.text = "ур. 4";
            levelLabel.fontSize = 28f;
            var levelFont = levelLabel.GetComponent<ThemeFont>();
            levelFont.Role = FontRole.Heading;
            levelFont.Apply();
            Text(badges, "Подпись ур", "Уровень предмета", 305f, 140f, 180f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center).textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform fresh = TopLeft(Place("Tag", badges, "Новый"), 506f, 52f, 112f, 38f);
            fresh.Find("Ромб").gameObject.SetActive(true);
            TMP_Text freshLabel = fresh.Find("Надпись").GetComponent<TMP_Text>();
            freshLabel.text = "Новый";
            freshLabel.rectTransform.offsetMin = new Vector2(18f, 0f);
            Text(badges, "Подпись новый", "Метка «новый»", 480f, 140f, 164f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center);

            RectTransform loading = Box(root, "Загрузка", 1355f, 780f, 520f, 170f);
            TopLeft(Place("LoadingBar", loading, "Полоса"), 36f, 78f, 320f, 14f);
            Text(loading, "Подпись", "Загрузка…", 376f, 66f, 130f, 36f, FontRole.Body, 20f, Role.TextMuted);
            return root;
        }
    }
}
