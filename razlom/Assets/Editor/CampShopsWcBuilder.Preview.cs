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
                CampShopView.SetTab(s.Tabs[0], true);
                CampShopView.SetTab(s.Tabs[1], false);
                s.Tabs[0].GetComponentInChildren<TMPro.TMP_Text>().text = smith ? "Перековка" : "Товары";
                s.Tabs[1].GetComponentInChildren<TMPro.TMP_Text>().text = smith ? "Разбор" : "Продать из сумки";
                s.GridCaption.text = smith ? "Предметы в сумке" : "Товары";
                int count = smith ? bag.Length : 8;
                for (int i = 0; i < s.Cells.Length; i++)
                {
                    bool has = i < count;
                    s.Cells[i].gameObject.SetActive(smith || i < 8);
                    s.Cells[i].Show(has ? Item(bag[(i + (smith ? 0 : 3)) % bag.Length]) : null, has ? (4 + i % 5).ToString() : "", has ? rare[(i + (smith ? 0 : 3)) % bag.Length] : 0, !smith && i == 2);
                }
                // Ряд «Надето»: у кузнеца выбрано надетое оружие (перековка без палатки).
                string[] worn = { "officer_sabre", "boarding_vest", "lavidium_ring", null };
                int[] wornRarity = { 2, 1, 1, 0 };
                for (int i = 0; i < s.Worn.Length; i++)
                    s.Worn[i].Show(worn[i] != null ? Item(worn[i]) : null, worn[i] != null ? "9" : "", wornRarity[i], smith && i == 0);
                if (!smith && s.Cells[4].State != null) s.Cells[4].State.SetHover(1f);
                s.Info.text = smith ? "Надетое можно перековать прямо здесь.\nРазбирать — только из сумки." : "Шанс редкого товара при обновлении: 10%\nПосле босса — бесплатное обновление, шанс 25%.";
                if (smith)
                {
                    s.Item.Show(Item("officer_sabre"), "9", 2, false);
                    s.ItemName.text = "Офицерская сабля";
                    s.ItemMeta.text = "Уровень 9  ·  Перековки 1/3  ·  <color=#3BF0F5>надето</color>";
                    string[] rows = { "Урон  <color=#C9D2E0>14</color>", "Шанс крита  <color=#C9D2E0>8%</color>", "Скорость атаки  <color=#C9D2E0>6%</color>" };
                    for (int i = 0; i < s.Rows.Length; i++)
                    {
                        s.Rows[i].gameObject.SetActive(i < rows.Length);
                        if (i < rows.Length) s.RowLabels[i].text = rows[i];
                        CampShopView.SetRow(s.Rows[i], i == 0);
                    }
                    s.Preview.text = "Урон  14  →  <color=#FA883C>16…20</color>";
                    s.PriceGold.text = "60";
                    s.PriceShards.text = "6";
                    s.PriceShardsGroup.SetActive(true);
                    s.Note.text = "Уровень +2";
                    s.ActionLabel.text = "Перековка";
                }
                else
                {
                    s.Item.Show(Item("copper_ring"), "5", 0, false);
                    s.ItemName.text = "Медное кольцо";
                    s.ItemMeta.text = "Обычный  ·  Уровень 5  ·  Перековки 0/3";
                    s.Detail.text = "Здоровье  <color=#F4F7FB>+12</color>\nСопротивление огню  <color=#F4F7FB>8%</color>\n\n<size=90%><color=#F4F7FB>По сравнению с надетым:</color></size>\n<color=#8CE07A>Здоровье +12</color>\n<color=#FF7A66>Шанс крита −2%</color>";
                    s.Preview.text = "";
                    s.PriceGold.text = "38";
                    s.PriceShardsGroup.SetActive(false);
                    s.Note.text = "";
                    s.ActionLabel.text = "Купить";
                    s.ExtraLabel.text = "Обновить товары  ·  25";
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
                string[] names = { "Малое зелье здоровья", "Большое зелье здоровья", "Малое зелье лавидия", "Большое зелье лавидия", "Живица", "Лавидиевый порыв" };
                string[] effects = { "Восстановит 10% здоровья", "Восстановит 30% здоровья", "Восстановит 10% лавидия", "Восстановит 30% лавидия",
                    "20% здоровья и −25% входящего урона на 6 с", "20% лавидия и +20% скорости на 6 с" };
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
                string[] names = { "Живица", "Лавидиевый порыв" };
                string[] effects = { "20% здоровья и −25% входящего урона на 6 с", "20% лавидия и +20% скорости на 6 с" };
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
