using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class CombatHudWcBuilder
    {
        static void BuildAbilities(RectTransform root, CombatHudView view)
        {
            RectTransform panel = Box(Node("Способности", root), BottomCenter, Vector2.zero, new Vector2(AbilitiesX, RowBottom),
                new Vector2(RowWidth, Slot));
            view.AbilityPanel = panel;
            string[] keys = { "Q", "W", "E", "R" };
            view.Slots = new HudSlotWidget[4];
            for (int i = 0; i < 4; i++)
            {
                RectTransform slot = Box(Node("Плитка " + keys[i], panel), Vector2.zero, Vector2.zero,
                    new Vector2(i * (Slot + SlotGap), 0f), new Vector2(Slot, Slot));
                view.Slots[i] = SlotWidget(slot, keys[i], 26f);
            }

            // Кувырок — плитка того же размера справа от ряда (вариант B); клавиша — широкая плашка.
            RectTransform dash = Box(Node("Кувырок", root), BottomCenter, Vector2.zero, new Vector2(DashX, RowBottom), new Vector2(Slot, Slot));
            view.DashPanel = dash;
            RectTransform dashSlot = Stretch(Node("Плитка кувырка", dash));
            view.Dash = SlotWidget(dashSlot, "SPACE", 22f, 66f);
            view.Dash.Art.texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_Dash.png");
        }

        /// <summary>Свечение и цветная рамка одного состояния плитки; выключено, пока его не включит CombatHudView.</summary>
        static GameObject StateGlow(RectTransform body, string name, Role role, float glow, float frame)
        {
            RectTransform group = Stretch(Node(name, body));
            Image halo = Layer(group, "Свечение", T.GlowSmall, role, glow, 22f);
            Layer(group, "Рамка", T.FrameBoldSmall, role, frame);
            group.gameObject.SetActive(false);
            return group.gameObject;
        }

        /// <summary>
        /// Плитка способности на паке: заливка ячейки, иконка под маской формы,
        /// при перезарядке — тёмная вуаль и кольцо делений, светлая рамка при
        /// наведении, камень и плашка нехватки лавидия, клавиша на нижней кромке.
        /// </summary>
        static HudSlotWidget SlotWidget(RectTransform slot, string key, float keySize, float keyWidth = 0f)
        {
            var widget = slot.gameObject.AddComponent<HudSlotWidget>();
            widget.Hit = slot;
            RectTransform body = Stretch(Node("Тело", slot));
            widget.Body = body;

            Image shadow = Layer(body, "Тень", T.GlowSmall, Role.Veil, .8f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(body, "Заливка", T.FillSmall, Role.Panel);
            RectTransform mask = Stretch(Node("Маска", body), 1.5f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = T.FillSmall;
            maskImage.type = Image.Type.Sliced;
            maskImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = Stretch(Node("Иконка", mask)).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            widget.Art = art;

            // Перезарядка: вуаль и кольцо — один узел, CombatHudView включает его целиком.
            Image veil = Layer(mask, "Перезарядка", T.Pixel, Role.Panel, .78f);
            veil.type = Image.Type.Filled;
            veil.fillMethod = Image.FillMethod.Radial360;
            veil.fillOrigin = (int)Image.Origin360.Top;
            veil.fillClockwise = false;
            widget.Cooldown = veil;
            RectTransform ringBox = Stretch(Node("Кольцо", veil.rectTransform), Slot * .12f);
            Layer(ringBox, "Деления", T.RingTicks, Role.Text, .16f);
            Image ring = Layer(ringBox, "Осталось", T.RingTicks, Role.Text, .9f);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            widget.CooldownRing = ring;
            veil.gameObject.SetActive(false);

            Layer(body, "Свет по кромке", T.HighlightSmall, Role.Highlight, .8f);
            widget.Frame = Layer(body, "Рамка", T.FrameSmall, Role.PanelLine, .9f);
            // Наведение и отказ: цвет ставит CombatHudView (белый или вспышка отказа), поэтому без темы.
            RectTransform highlight = Stretch(Node("Наведение", body), -1f);
            var hi = highlight.gameObject.AddComponent<Image>();
            hi.sprite = T.FrameBoldSmall;
            hi.type = Image.Type.Sliced;
            hi.raycastTarget = false;
            widget.Highlight = hi;
            highlight.gameObject.SetActive(false);

            // Нажатие — короткая оранжевая вспышка. «Готово» — не свечение (владелец: «слишком
            // явное»), а камень на верхней кромке: горит и изредка мерцает искрой (HudReadyGem).
            widget.PressGlow = StateGlow(body, "Нажата", Role.Accent, .6f, 1f);
            RectTransform gemBox = Box(Node("Готовность", body), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, 1f), new Vector2(20f, 20f));
            var ready = gemBox.gameObject.AddComponent<HudReadyGem>();
            ready.Gem = Mark(gemBox, "Камень", T.Gem, Role.Rare, 1f, new Vector2(.5f, .5f), Vector2.zero, 20f);
            Object.DestroyImmediate(ready.Gem.GetComponent<ThemeColor>());
            ready.Spark = Mark(gemBox, "Искра", Kit("wc_stat_crit_chance"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 34f);
            Object.DestroyImmediate(ready.Spark.GetComponent<ThemeColor>());
            ready.Spark.color = new Color(1f, 1f, 1f, 0f);
            ready.Frame = widget.Frame;
            ready.ReadyColour = T.Rare;
            widget.ReadyGem = ready;

            widget.CooldownText = Label(body, "Секунды", "0.0", FontRole.Body, Slot * .27f, Role.Text, TextAlignmentOptions.Center);
            widget.CooldownText.fontStyle = FontStyles.Bold;
            widget.CooldownText.gameObject.SetActive(false);

            // Нехватка лавидия: камень на углу и плашка с недостачей.
            RectTransform lacking = Stretch(Node("Нет лавидия", body));
            Mark(lacking, "Камень", T.Gem, Role.Lavidium, 1f, new Vector2(1f, 1f), new Vector2(-2f, -2f), 20f);
            RectTransform pill = Box(Node("Плашка", lacking), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 16f), new Vector2(46f, 20f));
            Layer(pill, "Заливка", T.TagFill, Role.Panel, .95f);
            Layer(pill, "Ободок", T.TagFrame, Role.Lavidium, .8f);
            widget.ResourceText = Label(pill, "Надпись", "−0", FontRole.Body, 14f, Role.Lavidium, TextAlignmentOptions.Center);
            widget.ResourceText.fontStyle = FontStyles.Bold;
            widget.ResourceBadge = lacking.gameObject;
            lacking.gameObject.SetActive(false);

            // Клавиша на нижней кромке. CombatHudView прячет её родителя у пустой плитки.
            RectTransform cap = Keycap(body, "Клавиша", key, keySize);
            cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
            cap.pivot = new Vector2(.5f, .5f);
            cap.anchoredPosition = new Vector2(0f, -4f);
            if (keyWidth > 0f) cap.sizeDelta = new Vector2(keyWidth, keySize);
            widget.Key = cap.Find("Буква").GetComponent<TMP_Text>();
            widget.Key.fontSize = keySize * .58f;
            widget.Key.enableAutoSizing = true;
            widget.Key.fontSizeMin = 9f;
            widget.Key.fontSizeMax = keySize * .58f;
            widget.Key.textWrappingMode = TextWrappingModes.NoWrap;
            return widget;
        }
    }
}
