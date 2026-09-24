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
    /// Третья страница витрины — боевой HUD по листу
    /// ART/UI/concepts-2026-09-22/final-2/kit-sheet-4-hud.png.
    /// Префаб: Assets/UI/Kit/Watercolor/ShowcaseHud.prefab.
    /// </summary>
    public static partial class UiKitShowcase
    {
        public const string HudPrefabPath = UiKitImport.KitRoot + "/Watercolor/ShowcaseHud.prefab";

        [MenuItem("Разлом/UI/Пак «Ночная акварель» — пересобрать витрину HUD")]
        public static void RebuildHudFromMenu() => RebuildHud();

        public static string RebuildHud()
        {
            EnsurePrefabs();
            RectTransform root = BuildHud();
            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject, HudPrefabPath); }
            finally { Object.DestroyImmediate(root.gameObject); }
            AssetDatabase.SaveAssets();
            return HudPrefabPath;
        }

        static Texture AbilityIcon(string key) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_" + key + ".png");

        static void CaptionUnder(RectTransform root, string text, float cx, float y, float w = 260f)
        {
            RectTransform box = TopLeft(Node("Подпись " + text, root), cx - w / 2f, y, w, 60f);
            TMP_Text label = Label(box, "Надпись", text, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Top);
            label.lineSpacing = -10f;
        }

        static void VitalRow(RectTransform parent, string icon, Role role, float value, float trail, string numbers, float y)
        {
            Mark(parent, "Значок " + numbers, CombatHudBuilder.Icon(icon), role, 1f, new Vector2(0f, 1f), new Vector2(38f, -y - 10f), 32f);
            RectTransform bar = TopLeft(Place("BarLarge", parent, "Полоса " + numbers), 68f, y, 200f, 20f);
            bar.Find("Внутри/Заполнение").GetComponent<ThemeColor>().SetRole(role);
            var wb = bar.GetComponent<WcBar>();
            wb.TrailValue = trail;
            wb.Set(value);
            Text(parent, "Число " + numbers, numbers, 282f, y - 7f, 90f, 34f, FontRole.Body, 21f, Role.Text);
        }

        static RectTransform BuildHud()
        {
            UiTheme t = UiThemeBuilder.Ensure(false) ?? UiTheme.Current;
            RectTransform root = Page("Витрина HUD", t);

            // ---------------- ряд 1
            TopLeft(SectionHeader(root, "Заголовок портрета", "Портрет", 270f), 46f, 50f, 270f, 30f);
            TopLeft(SectionHeader(root, "Заголовок параметров", "Параметры", 380f), 345f, 50f, 380f, 30f);
            TopLeft(SectionHeader(root, "Заголовок умений", "Ячейка умения", 560f), 765f, 50f, 560f, 30f);
            TopLeft(SectionHeader(root, "Заголовок зелья", "Зелье", 190f), 1360f, 50f, 190f, 30f);
            TopLeft(SectionHeader(root, "Заголовок баффов", "Баффы", 300f), 1580f, 50f, 300f, 30f);

            TopLeft(Place("Portrait", root, "Портрет"), 72f, 104f, 200f, 200f);
            CaptionUnder(root, "Портрет героя с уровнем", 172f, 330f);

            RectTransform vitals = Box(root, "Параметры", 345f, 110f, 380f, 220f);
            // Низкое здоровье: зарево на всю нижнюю часть окна, сильнее за сердцем.
            RectTransform lowGlow = TopLeft(Node("Мало здоровья", vitals), 3f, 134f, 374f, 83f);
            Layer(lowGlow, "Зарево", t.Haze, Role.Health, .26f);
            VitalRow(vitals, "heart", Role.Health, .80f, .92f, "92 / 100", 34f);
            VitalRow(vitals, "lavidium", Role.Lavidium, .40f, 0f, "40 / 100", 82f);
            TopLeft(Place("DividerPlain", vitals, "Разделитель"), 20f, 126f, 340f, 16f);
            VitalRow(vitals, "heart", Role.Health, .28f, 0f, "28 / 100", 166f);
            CaptionUnder(root, "Полосы здоровья и лавидия,\nнизкое здоровье", 535f, 340f, 380f);

            RectTransform slots = Box(root, "Умения", 765f, 110f, 560f, 262f);
            string[] icons = { "Cleave", "Whirlwind", "Skewer", "Blaze" };
            string[] names = { "Готово", "Нажата", "Перезарядка", "Нет лавидия" };
            var states = new[] { WcAbilitySlot.State.Ready, WcAbilitySlot.State.Pressed, WcAbilitySlot.State.Cooldown, WcAbilitySlot.State.NoLavidium };
            for (int i = 0; i < 4; i++)
            {
                RectTransform slot = TopLeft(Place("AbilitySlot", slots, "Умение " + names[i]), 24f + i * 134f, 24f, 104f, 104f);
                var slotView = slot.GetComponent<WcAbilitySlot>();
                slotView.Icon.texture = AbilityIcon(icons[i]);
                slotView.Set(states[i], .62f, "4,8");
                Text(slots, "Подпись " + names[i], names[i], 4f + i * 134f, 202f, 144f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center)
                    .textWrappingMode = TextWrappingModes.NoWrap;
            }

            RectTransform potion = Box(root, "Зелье", 1360f, 110f, 190f, 262f);
            RectTransform ps = TopLeft(Place("PotionSlot", potion, "Зелье"), 43f, 24f, 104f, 104f);
            var bottle = ps.Find("Бутылка").GetComponent<RawImage>();
            bottle.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/potion_health_small.png");
            bottle.enabled = true;
            ps.Find("Количество/Надпись").GetComponent<TMP_Text>().text = "×2";
            Text(potion, "Подпись", "Зелье (количество)", 0f, 202f, 190f, 30f, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center)
                .textWrappingMode = TextWrappingModes.NoWrap;

            string[] buffIcons = { "stat_attack_speed", "stat_damage", "stat_armor" };
            Role[] buffRoles = { Role.Rare, Role.Health, Role.Rare };
            float[] buffLeft = { .7f, .45f, .85f };
            string[] buffTime = { "8с", "12с", "5с" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform buff = TopLeft(Place("Buff", root, "Бафф " + i), 1586f + i * 106f, 114f, 88f, 88f);
                buff.Find("Значок").GetComponent<Image>().sprite = CombatHudBuilder.Icon(buffIcons[i]);
                buff.Find("Значок").GetComponent<ThemeColor>().SetRole(buffRoles[i]);
                buff.Find("Свечение").GetComponent<ThemeColor>().SetRole(buffRoles[i], .38f);
                foreach (string ring in new[] { "Время", "Свечение кольца" })
                {
                    var image = buff.Find(ring).GetComponent<Image>();
                    image.fillAmount = buffLeft[i];
                    image.GetComponent<ThemeColor>().SetRole(buffRoles[i], ring == "Время" ? 1f : .35f);
                }
                buff.Find("Секунды/Надпись").GetComponent<TMP_Text>().text = buffTime[i];
            }
            CaptionUnder(root, "Эффекты с таймером", 1734f, 262f);

            // ---------------- ряд 2
            TopLeft(SectionHeader(root, "Заголовок босса", "Полоса босса", 640f), 46f, 440f, 640f, 30f);
            TopLeft(SectionHeader(root, "Заголовок врага", "Полоса врага", 390f), 760f, 440f, 390f, 30f);
            TopLeft(SectionHeader(root, "Заголовок урона", "Урон", 300f), 1195f, 440f, 300f, 30f);
            TopLeft(SectionHeader(root, "Заголовок миникарты", "Миникарта", 330f), 1545f, 440f, 330f, 30f);

            TopLeft(Place("BossBar", root, "Босс"), 56f, 494f, 620f, 104f);
            CaptionUnder(root, "Имя и здоровье босса", 366f, 614f);

            TopLeft(Place("EnemyBar", root, "Враг"), 855f, 520f, 200f, 12f);
            CaptionUnder(root, "Обычный", 955f, 542f, 200f);
            TopLeft(Place("EnemyBarElite", root, "Элита"), 855f, 604f, 200f, 12f);
            CaptionUnder(root, "Элита", 955f, 626f, 200f);

            TopLeft(Place("DamageNumber", root, "Урон"), 1188f, 520f, 150f, 80f);
            TopLeft(Place("DamageNumberCrit", root, "Крит"), 1330f, 508f, 190f, 96f);
            CaptionUnder(root, "Обычный и критический урон", 1345f, 620f, 300f);

            RectTransform map = TopLeft(Place("Minimap", root, "Миникарта"), 1580f, 494f, 260f, 260f);
            CaptionUnder(root, "Игрок, враги и выход", 1710f, 774f);
            var area = (RectTransform)map.Find("Карта");
            foreach (var p in new[] { new Vector2(.3f, .64f), new Vector2(.6f, .72f), new Vector2(.64f, .36f), new Vector2(.74f, .46f), new Vector2(.34f, .32f), new Vector2(.52f, .16f) })
                MinimapEnemy(area, p);

            // ---------------- ряд 3
            TopLeft(SectionHeader(root, "Заголовок подсказки", "Подсказка взаимодействия", 640f), 46f, 790f, 640f, 30f);
            TopLeft(Place("InteractPrompt", root, "Подсказка"), 140f, 856f, 280f, 66f);
            CaptionUnder(root, "Всплывает над объектом", 280f, 944f);

            // Все способности Пелага в размере боевой панели: как читаются мелко.
            TopLeft(SectionHeader(root, "Заголовок способностей", "Способности", 800f), 740f, 850f, 800f, 30f);
            string[] all = { "Whirlwind", "Cleave", "Blaze", "Squall", "AnchorSweep", "Wreck", "AnchorLeap", "FireFlask", "Skewer", "Backblast", "Dash" };
            for (int i = 0; i < all.Length; i++)
            {
                RectTransform slot = TopLeft(Place("AbilitySlot", root, "Способность " + all[i]), 752f + i * 72f, 906f, 62f, 62f);
                var slotView = slot.GetComponent<WcAbilitySlot>();
                slotView.Icon.texture = AbilityIcon(all[i]);
                slotView.Set(WcAbilitySlot.State.Ready);
                slot.Find("Клавиша").gameObject.SetActive(false);
            }
            CaptionUnder(root, "Размер боевой панели, 62 единицы", 1140f, 984f, 400f);
            return root;
        }
    }
}
