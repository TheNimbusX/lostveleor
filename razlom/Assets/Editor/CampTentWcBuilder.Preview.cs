using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>Кадр палатки без запуска игры, поверх кадра игры (ART/no-ui.png), с примером вещей.</summary>
    public static partial class CampTentWcBuilder
    {
        public static string Capture(string outPath)
        {
            Build(false);
            _statPreview = false;
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, Preview);
        }

        /// <summary>Тот же кадр, но с подсказкой стата «Скорость приёмов» (как в концепте).</summary>
        public static string CaptureStat(string outPath)
        {
            Build(false);
            _statPreview = true;
            try { return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, Preview); }
            finally { _statPreview = false; }
        }

        static bool _statPreview;

        static Sprite ItemSprite(string key)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + key + ".png");
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
        }

        static void Preview(GameObject inst)
        {
            var view = inst.GetComponent<CampTentView>();
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

            string[] bag = { "duelist_sabre", "leather_jacket", "copper_ring", "fang_cord", "rusty_sword", "scout_jacket", "sea_knot", "smith_ring",
                "quilted_jacket", "woodland_talisman", "boarding_cutlass", "marksman_ring", "courier_token", "boarding_vest" };
            int[] rarity = { 1, 0, 0, 2, 0, 1, 0, 2, 0, 0, 1, 0, 3, 3 };
            for (int i = 0; i < 48; i++)
            {
                var cell = Object.Instantiate(view.BagTemplate, view.BagGrid);
                cell.gameObject.SetActive(true);
                bool has = i < bag.Length;
                int r = has ? rarity[i] : -1;
                cell.Show(has ? view.FrameFor(r) : view.EmptyFrame, Color.white, has ? ItemSprite(bag[i]) : null, has ? (3 + i % 6).ToString() : "",
                    i == 0, false, i == 7, r, view.ColourFor(r));
                // Наведение на пятую вещь: та же рамка светлеет, второй рамки нет.
                if (i == 4 && cell.State != null) cell.State.SetHover(1f);
            }
            string[] worn = { "officer_sabre", "boarding_vest", "lavidium_ring", null };
            int[] wornRarity = { 2, 0, 1, -1 };
            for (int i = 0; i < 4; i++)
                view.Worn[i].Show(null, Color.white, worn[i] != null ? ItemSprite(worn[i]) : null, worn[i] != null ? "9" : "", false, false, false,
                    wornRarity[i], view.ColourFor(wornRarity[i]));
            string[] values = { "120", "14", "6", "5%", "8%", "150%", "200", "3/с", "4,5", "0%", "0%", "8%" };
            for (int i = 0; i < 12; i++) view.StatValues[i].text = values[i];
            view.Level.text = "3";
            view.XpText.text = "140 / 300";
            view.XpFill.anchorMax = new Vector2(.46f, 1f);
            string[] potions = { "potion_health_small", "potion_health_large", "potion_lavidium_small", "potion_lavidium_large" };
            for (int i = 0; i < 4; i++)
            {
                view.PotionIcons[i].sprite = ItemSprite(potions[i]);
                view.PotionCounts[i].text = (3 - i % 2).ToString();
                if (view.PotionStates[i] != null) view.PotionStates[i].Set(WcSlotState.Plain, i == 0 || i == 3);
            }
            view.BagCount.text = bag.Length + " / 48";
            view.ShowFilter(0);
            view.ShowPage(false);

            // Карточка у первой вещи сумки.
            view.ItemTitle.text = "Сабля дуэлянта";
            view.ItemRarity.text = "Редкая";
            view.ItemRarity.color = view.ColourFor(1);
            view.ItemKind.text = "Оружие · уровень 3";
            view.ItemArt.sprite = ItemSprite("duelist_sabre");
            view.ItemArt.enabled = true;
            view.SetItemFrame(1);
            view.ItemStats.text = "Урон  <b>+14</b>\nШанс крита  <b>+8%</b>\n\n<color=#93A2BC>Если надеть:</color>\nУрон   14 → 17  <color=#8FE3A8>+3</color>\nБроня   6 → 4  <color=#FF6A5A>−2</color>";
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.Tooltip);
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.BagGrid);
            if (_statPreview)
            {
                view.ItemTitle.text = "Скорость приёмов";
                view.ItemRarity.text = "Итого: 10%";
                view.ItemRarity.color = UiTheme.Current.Get(UiTheme.Role.Rare);
                view.ItemKind.gameObject.SetActive(false);
                view.ItemArt.sprite = view.StatIcons[9];
                view.SetItemFrame(-1);
                view.ItemStats.text = "Способности исполняются быстрее: короче замах и анимация приёма. Перезарядку не меняет.\n\n"
                    + "<color=#93A2BC>Из чего складывается:</color>\nОснова<pos=74%>0%\nЖетон гонца<pos=74%><color=#8FE3A8>+10%</color>";
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.Tooltip);
                view.PlaceTooltip(view.StatRows[9]);
                view.StatRows[9].GetComponent<UiHoverMotion>().Highlight.canvasRenderer.SetAlpha(1f);
            }
            else view.PlaceTooltip((RectTransform)view.BagGrid.GetChild(1));
            view.TooltipGroup.alpha = 1f;

            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                if (motion.Highlight != null && !(_statPreview && motion.transform == view.StatRows[9])) motion.Highlight.canvasRenderer.SetAlpha(0f);
        }
    }
}
