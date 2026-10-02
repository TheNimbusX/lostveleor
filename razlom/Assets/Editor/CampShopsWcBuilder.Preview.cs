using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>Кадры окон лагеря без запуска игры, поверх кадра игры (ART/no-ui.png), с примером данных.</summary>
    public static partial class CampShopsWcBuilder
    {
        public enum Shot { Smith, Trader, Alchemist, Hint, Recipes }

        public static string Capture(string outPath, Shot shot)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst => Preview(inst, shot));
        }

        static Sprite Item(string key)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + key + ".png");
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
        }

        static void Preview(GameObject inst, Shot shot)
        {
            var view = inst.GetComponent<CampShopView>();
            var root = (RectTransform)inst.transform;
            if (System.IO.File.Exists("../ART/no-ui.png"))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(System.IO.File.ReadAllBytes("../ART/no-ui.png"));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.uvRect = new Rect(.035f, 0f, .965f, .955f);
                back.transform.SetSiblingIndex(0);
            }
            view.Hint.gameObject.SetActive(shot == Shot.Hint);
            if (shot == Shot.Hint)
            {
                // Над прилавком на кадре игры: имя, кто это, клавиша плашкой.
                view.Hint.anchoredPosition = new Vector2(80f, 120f);
                view.HintTitle.text = "Вен";
                view.HintRole.text = "Торговец";
                view.HintKey.text = "E";
                view.HintNote.text = "Поговорить";
            }
            view.Smith.Group.gameObject.SetActive(shot == Shot.Smith);
            view.Trader.Group.gameObject.SetActive(shot == Shot.Trader);
            view.Alchemist.Group.gameObject.SetActive(shot == Shot.Alchemist || shot == Shot.Recipes);

            string[] bag = { "rusty_sword", "leather_jacket", "copper_ring", "fang_cord", "duelist_sabre", "scout_jacket", "sea_knot", "smith_ring",
                "quilted_jacket", "woodland_talisman", "boarding_cutlass" };
            int[] rare = { 0, 0, 0, 2, 1, 1, 0, 3, 0, 0, 1 };
            if (shot != Shot.Alchemist && shot != Shot.Recipes)
            {
                bool smith = shot == Shot.Smith;
                CampShopScreen s = smith ? view.Smith : view.Trader;
                s.Gold.text = "450";
                s.Shards.text = "14";
                s.ShardsGroup.SetActive(smith);
                // С 29.09 одна страница: вкладки спрятаны миграцией v1.
                foreach (var tab in s.Tabs) if (tab != null) tab.gameObject.SetActive(false);
                if (s.Subtitle != null) s.Subtitle.text = smith ? "Перековка и разбор" : "Покупка и продажа";
                s.GridCaption.text = "Сумка  ·  " + bag.Length + " / 48";
                int chosenBag = smith ? 3 : 2;
                for (int i = 0; i < s.Cells.Length; i++)
                {
                    bool has = i < bag.Length;
                    s.Cells[i].gameObject.SetActive(true);
                    s.Cells[i].Show(has ? Item(bag[i]) : null, has ? (4 + i % 5).ToString() : "", has ? rare[i] : 0, i == chosenBag);
                    if (i < s.CellPrices.Length && s.CellPrices[i] != null) s.CellPrices[i].text = has ? (i == 5 ? "беречь" : "+" + (7 + i % 5 + rare[i] * 6)) : "";
                }
                string[] worn = { "officer_sabre", "boarding_vest", "lavidium_ring", null };
                int[] wornRarity = { 2, 1, 1, 0 };
                for (int i = 0; i < s.Worn.Length; i++)
                    s.Worn[i].Show(worn[i] != null ? Item(worn[i]) : null, worn[i] != null ? "9" : "", wornRarity[i], false);
                s.Info.text = smith ? "Надетое можно перековать прямо здесь.\nРазбирать — только из сумки." : "Обновление — шанс редкого 10%\nПосле босса — бесплатно, шанс 25%";
                if (smith)
                {
                    // Выбрана вещь из сумки: обе кнопки рядом — перековка по строке «Урон», разбор с выходом.
                    s.Item.Show(Item("fang_cord"), "7", 2, false);
                    s.ItemName.text = "Клык на шнурке";
                    s.ItemMeta.text = "<color=#A765FF>Эпический</color>  ·  Уровень 7";
                    if (s.ReforgeCount != null) s.ReforgeCount.text = "1 / 3";
                    for (int i = 0; i < s.ReforgePips.Length; i++)
                        if (s.ReforgePips[i] != null && s.ReforgePips[i].GetComponent<ThemeColor>() is ThemeColor pip)
                            pip.SetRole(i < 1 ? UiTheme.Role.Accent : UiTheme.Role.TextMuted, i < 1 ? 1f : .35f);
                    string[] rows = { "Урон  <color=#F4F7FB>+3</color>  →  <color=#FD7442>+4…+6</color>", "Шанс крита  <color=#F4F7FB>8%</color>",
                        "Скорость атаки  <color=#F4F7FB>6%</color>  <color=#93A2BC>· предел</color>" };
                    for (int i = 0; i < s.Rows.Length; i++)
                    {
                        s.Rows[i].gameObject.SetActive(i < rows.Length);
                        if (i < rows.Length) s.RowLabels[i].text = rows[i];
                        CampShopView.SetRow(s.Rows[i], i == 0);
                    }
                    s.PriceGold.text = "60";
                    s.PriceShards.text = "6";
                    s.PriceShardsGroup.SetActive(true);
                    s.Preview.text = "Уровень вещи 7 → 10";
                    s.Note.text = "";
                    s.ActionLabel.text = "Перековать";
                    if (s.ExtraLabel != null) s.ExtraLabel.text = "Разобрать";
                    if (s.YieldShards != null) s.YieldShards.text = "+9 осколков";
                    if (s.ExtraNote != null) s.ExtraNote.text = "Предмет будет уничтожен";
                    if (s.MainKeyLabel != null) s.MainKeyLabel.text = "Перековать";
                    if (s.SecondKeyLabel != null) s.SecondKeyLabel.text = "Разобрать";
                }
                else
                {
                    // Товары рядом с сумкой; выбрана вещь из сумки — сравнение с надетым и «Продать · +N».
                    string[] goods = { "rusty_sword", "boarding_cutlass", "quilted_jacket", "leather_jacket", "copper_ring", "smith_ring", "woodland_talisman", "fang_cord" };
                    string[] names = { "Ржавая сабля", "Абордажный тесак", "Стёганая куртка", "Кожаная куртка", "Медное кольцо", "Кольцо кузнеца", "Лесной талисман", "Клык на шнурке" };
                    int[] prices = { 21, 27, 21, 48, 21, 21, 21, 48 };
                    for (int i = 0; i < s.Goods.Length; i++)
                    {
                        var good = s.Goods[i];
                        if (good == null || good.Button == null) continue;
                        good.Button.gameObject.SetActive(i < goods.Length);
                        if (i >= goods.Length) continue;
                        bool sold = i == 6;
                        good.Cell.Show(sold ? null : Item(goods[i]), sold ? "" : "3", i == 3 || i == 7 ? 1 : 0, false);
                        good.Name.text = sold ? "Продано" : names[i];
                        good.Price.text = prices[i].ToString();
                        if (good.PriceGroup != null) good.PriceGroup.SetActive(!sold);
                        if (good.Chosen != null) good.Chosen.SetActive(false);
                    }
                    s.Item.Show(Item("copper_ring"), "5", 0, false);
                    s.ItemName.text = "Медное кольцо";
                    s.ItemMeta.text = "<color=#A6B3C8>Обычный</color>  ·  Уровень 5  ·  Перековки 0/3";
                    s.Detail.text = "Здоровье  <color=#F4F7FB>+12</color>";
                    if (s.Compare != null)
                        // Кольцо — металл мира, стат — ресурс способностей «концентрация» (владелец 01.10).
                        s.Compare.text = "<color=#93A2BC>Вместо «Кольцо с лавидием»:</color>\nЗдоровье   120 → 132  <color=#8FE3A8>+12</color>\nЗапас концентрации   115 → 100  <color=#FF6A5A>−15</color>";
                    s.Preview.text = "";
                    s.Price.SetActive(false);
                    s.Note.text = "Станет 457 золота";
                    s.ActionLabel.text = "Продать · +7";
                    s.ExtraLabel.text = "Обновить товары · 50";
                    if (s.MainKeyLabel != null) s.MainKeyLabel.text = "Продать";
                }
                s.Title.text = smith ? "Эни" : "Вен";
                s.Speaker.text = s.Title.text;
                s.Message.text = smith ? "Ну, что тебе сковать?" : "Ну, чего тебе?";
            }
            else
            {
                var s = view.Alchemist;
                s.Gold.text = "450";
                s.Title.text = "Лео";
                s.Speaker.text = "Лео";
                s.Message.text = "Ты ради меня даже реку перешел?";
                string[] names = { "Малое зелье здоровья", "Большое зелье здоровья", "Малое зелье концентрации", "Большое зелье концентрации", "Живица", "Порыв" };
                string[] effects = { "Восстановит 10% здоровья", "Восстановит 30% здоровья", "Восстановит 10% концентрации", "Восстановит 30% концентрации",
                    "20% здоровья и −25% входящего урона на 6 с", "20% концентрации и +20% скорости на 6 с" };
                int[] prices = { 15, 40, 15, 40, 70, 70 };
                for (int i = 0; i < 6; i++)
                {
                    var c = s.Potions[i];
                    c.Name.text = names[i];
                    c.Effect.text = effects[i];
                    c.Stock.text = "В запасе:  <color=#F4F7FB>" + (i < 4 ? 3 - i % 3 : 0) + "</color>";
                    c.BuyLabel.text = "Купить  ·  " + prices[i];
                    bool selected = i == 0 || i == 3;
                    c.SelectLabel.text = selected ? "В быстром слоте" : "В быстрый слот";
                    c.Select.interactable = !selected;
                    c.Chosen.SetActive(selected);
                    bool locked = i == 5;
                    c.Locked.SetActive(locked);
                    c.Buy.gameObject.SetActive(!locked);
                    c.Select.gameObject.SetActive(!locked);
                    c.Stock.gameObject.SetActive(!locked);
                    if (locked) c.LockedLabel.text = "Нужен рецепт · вкладка «Рецепты»";
                }
            }

            if (shot == Shot.Recipes)
            {
                var s = view.Alchemist;
                s.PotionsPage.SetActive(false);
                s.RecipesPage.SetActive(true);
                CampShopView.SetTab(s.Tabs[0], false);
                CampShopView.SetTab(s.Tabs[1], true);
                s.Message.text = "Договорились. Возвращайся, когда справишься.";
                string[] names = { "Живица", "Порыв" };
                string[] effects = { "20% здоровья и −25% входящего урона на 6 с", "20% концентрации и +20% скорости на 6 с" };
                for (int i = 0; i < 2; i++)
                {
                    var r = s.Recipes[i];
                    r.Name.text = names[i];
                    r.Effect.text = effects[i];
                    bool done = i == 0;
                    r.Chosen.SetActive(done);
                    r.Stock.text = done ? "Рецепт открыт" : "В работе";
                    r.LockedLabel.text = done ? "Рецепт у Лео — зелье теперь продаётся во вкладке «Зелья»."
                        : "В работе: Пройди уровень Разлома без зелий или отдай 12 осколков";
                    r.OrderMain.gameObject.SetActive(false);
                    r.OrderAlt.gameObject.SetActive(!done);
                    r.OrderAltLabel.text = "Отдать 12 осколков";
                }
            }
            else if (shot == Shot.Alchemist)
            {
                view.Alchemist.PotionsPage.SetActive(true);
                view.Alchemist.RecipesPage.SetActive(false);
                CampShopView.SetTab(view.Alchemist.Tabs[0], true);
                CampShopView.SetTab(view.Alchemist.Tabs[1], false);
            }

            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
        }
    }
}
