using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Кадр нового HUD без запуска игры: префаб в сцене предпросмотра, поверх
    /// кадра игры без интерфейса (ART/no-ui.png), с примером данных. В префаб
    /// ничего не пишется — всё меняется только на экземпляре для снимка.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        const string Backdrop = "../ART/no-ui.png";

        public static string Capture(string outPath)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, Preview);
        }

        /// <summary>
        /// Раскадровка баннера нового уровня: кадр на каждый момент из <paramref name="times"/>
        /// (секунды от появления) — файлы level-XXX.png в <paramref name="outDir"/>. Остальной HUD
        /// как в обычном кадре.
        /// </summary>
        public static string CaptureLevelSequence(string outDir, float[] times)
        {
            Build(false);
            System.IO.Directory.CreateDirectory(outDir);
            string last = null;
            foreach (float t in times)
            {
                float at = t;
                last = UiKitShowcase.Capture(System.IO.Path.Combine(outDir, "level-" + Mathf.RoundToInt(at * 1000f).ToString("0000") + ".png"),
                    PrefabPath, 1920, 1080, inst =>
                    {
                        Preview(inst);
                        inst.GetComponent<CombatHudView>().LevelBanner.Preview(4, at);
                    });
            }
            return last;
        }

        static Texture2D Ability(string key) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_" + key + ".png");

        static void Preview(GameObject inst)
        {
            var view = inst.GetComponent<CombatHudView>();
            var root = (RectTransform)inst.transform;

            // Подложка: кадр игры без интерфейса, без панели инструментов редактора слева сверху.
            if (System.IO.File.Exists(Backdrop))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(System.IO.File.ReadAllBytes(Backdrop));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.uvRect = new Rect(.035f, 0f, .965f, .955f);
                back.transform.SetSiblingIndex(0);
            }

            view.Level.text = "3";
            SetFill(view.HealthFill, .78f); view.HealthText.text = "78 / 100";
            SetFill(view.LavidiumFill, .45f); view.LavidiumText.text = "90 / 200";
            SetFill(view.ExperienceFill, .35f);

            string[] icons = { "Whirlwind", "Cleave", "FireFlask", "Skewer" };
            for (int i = 0; i < 4; i++)
            {
                HudSlotWidget slot = view.Slots[i];
                slot.Art.texture = Ability(icons[i]);
                slot.Art.uvRect = new Rect(view.IconCrop, view.IconCrop, 1f - view.IconCrop * 2f, 1f - view.IconCrop * 2f);
            }
            // Перезарядка на третьей, нехватка лавидия на четвёртой, наведение на второй.
            view.Slots[2].Cooldown.gameObject.SetActive(true);
            view.Slots[2].CooldownRing.fillAmount = .62f;
            view.Slots[2].CooldownText.gameObject.SetActive(true);
            view.Slots[2].CooldownText.text = "4.8";
            view.Slots[3].Art.color = view.ArtDimmed;
            view.Slots[3].ResourceBadge.SetActive(true);
            view.Slots[3].ResourceText.text = "−15";
            view.Slots[1].Highlight.gameObject.SetActive(true);
            view.Slots[1].Highlight.color = Color.white;
            view.Dash.Key.text = "SPACE";
            view.Dash.Art.texture = Ability("Dash");
            view.Dash.Art.uvRect = view.Slots[0].Art.uvRect;
            // Камень: 2 / 4 / 7 / 8 — сталь, серебро, золото, кристалл; у перезарядки и нехватки
            // лавидия тусклый. Ряд насечек — только у плитки под мышью (вторая, с подсказкой).
            int[] upgrades = { 2, 4, 7, 8 };
            for (int slot = 0; slot < view.Slots.Length && slot < upgrades.Length; slot++)
                view.Slots[slot].ReadyGem.Preview(upgrades[slot], slot < 2, slot == 1);
            view.Dash.ReadyGem.Preview(0, true);
            // Эффекты зелий над героем: Живица на 4 с, Порыв на 2 с.
            foreach (var (chip, fill, title) in new[] { (view.ResinChip, .66f, "Живица · 4 с"), (view.SurgeChip, .33f, "Порыв · 2 с") })
            {
                chip.gameObject.SetActive(true);
                chip.Group.alpha = 1f;
                chip.Ring.fillAmount = fill;
                chip.Title.text = title;
            }
            // Артефакт забега: медальон у портрета.
            view.ArtifactSlot.SetActive(true);
            view.ArtifactIcon.texture = RunArtifactTexts.Icon(Game.Sim.RunArtifact.SunSeal);
            // Новый уровень: баннер в покое, через секунду после появления.
            view.LevelBanner.Preview(4, 1.2f);
            // Концепт 2Б: всплывашки над портретом и объявление сверху.
            view.Toasts.Preview(
                (AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/leather_jacket.png"), Role.Rare, "Кожаная куртка", "Редкая · ур. 4", Role.Rare),
                (view.GoldIcon, Role.Coins, "+45 золота", null, Role.TextMuted),
                (view.OrderIcons[0], Role.Epic, "Заказ Лео выполнен", "Живица", Role.TextMuted));
            view.Announce.Preview("РАЗЛОМ ЗАЧИЩЕН", "Путь к выходу открыт");
            // Под мышью над героем числа видны внутри полос.
            view.ExperienceText.text = "140 / 300";
            var xpBar = (RectTransform)view.ExperienceFill.parent;
            xpBar.sizeDelta = new Vector2(xpBar.sizeDelta.x, view.ExperienceHoverHeight);

            foreach (string potion in new[] { "Health Potion", "Lavidium Potion" })
            {
                Transform tile = view.PotionPanel.Find(potion);
                tile.Find("Count").GetComponent<TMP_Text>().text = potion.StartsWith("Health") ? "2" : "1";
                tile.Find("Клавиша/Буква").GetComponent<TMP_Text>().text = potion.StartsWith("Health") ? "5" : "6";
            }

            // Подсказка над второй плиткой — как её ставит CombatHudView.
            view.Tooltip.gameObject.SetActive(true);
            view.TooltipIcon.texture = Ability("Cleave");
            view.TooltipTitle.text = "Рассекающий удар";
            view.TooltipKey.text = "W";
            view.TooltipBody.text = "Сильный удар саблей сверху перед собой. Для применения не требуется выбранная цель.";
            for (int i = 0; i < view.TooltipUpgradePips.Length; i++)
            {
                view.TooltipUpgradePips[i].sprite = i < 4 ? view.UpgradePipFilled : view.UpgradePipEmpty;
                view.TooltipUpgradePips[i].color = i < 4 ? Color.white : new Color(1f, 1f, 1f, .45f);
            }
            view.TooltipUpgradeText.text = "Усилений: 4 из 8";
            string[] values = { "34", "6 с", "2,5 м" };
            int[] stat = { 3, 2, 4 };
            for (int i = 0; i < view.TooltipMetrics.Length; i++)
            {
                HudTooltipMetric metric = view.TooltipMetrics[i];
                bool shown = i < values.Length;
                metric.gameObject.SetActive(shown);
                if (!shown) continue;
                metric.Icon.sprite = view.StatIcons[stat[i]];
                metric.Value.text = values[i];
                metric.Separator.SetActive(i % view.TooltipMetricColumns != 0);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.Tooltip);
            var slotRect = (RectTransform)view.Slots[1].transform;
            float slotCenter = AbilitiesX + slotRect.anchoredPosition.x + Slot * .5f;
            view.Tooltip.anchoredPosition = new Vector2(slotCenter, RowBottom + Slot + view.TooltipGap);
            view.TooltipTail.anchoredPosition = Vector2.zero;

            // Карта: образец местности и настоящие метки холста — выход, награда за краем, алхимик
            // под мышью с подписью, враги и герой.
            view.MinimapImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(UiKitImport.KitRoot + "/Watercolor/wc_map_sample.png");
            view.MinimapImage.enabled = true;
            view.MinimapCaption.text = "Разлом · 2";
            view.MinimapMarks.Preview(
                new[] { new Vector3(.78f, .2f, 4f), new Vector3(.95f, .6f, 5f), new Vector3(.28f, .78f, HudMinimapMarks.AlchemistMark) },
                new[] { new Vector2(.32f, .38f), new Vector2(.6f, .3f), new Vector2(.66f, .62f), new Vector2(.36f, .7f) },
                new Vector2(.5f, .5f), 24f, 2);
        }

        static void SetFill(RectTransform fill, float ratio) => fill.anchorMax = new Vector2(ratio, fill.anchorMax.y);
    }
}
