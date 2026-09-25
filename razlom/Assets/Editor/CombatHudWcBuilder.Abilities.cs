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
                view.Slots[i] = SlotWidget(slot, keys[i], 26f, upgrades: true);
            }

            // Кувырок — плитка того же размера справа от ряда (вариант B); клавиша — широкая плашка.
            RectTransform dash = Box(Node("Кувырок", root), BottomCenter, Vector2.zero, new Vector2(DashX, RowBottom), new Vector2(Slot, Slot));
            view.DashPanel = dash;
            RectTransform dashSlot = Stretch(Node("Плитка кувырка", dash));
            view.Dash = SlotWidget(dashSlot, "SPACE", 22f, 66f, upgrades: false);
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

        /// <summary>Рисунок камня по числу усилений: ступенями — пустой, сталь, серебро, золото, кристалл.</summary>
        static Sprite GemFor(int upgrades) => Kit("wc_upgrade_gem_" + (upgrades >= 8 ? 8 : upgrades >= 6 ? 7 : upgrades >= 3 ? 4 : upgrades >= 1 ? 2 : 0));

        /// <summary>
        /// Плитка способности на паке: заливка ячейки, иконка под маской формы,
        /// при перезарядке — тёмная вуаль и кольцо делений, светлая рамка при
        /// наведении, камень с насечками усилений, плашка нехватки лавидия, клавиша на нижней кромке.
        /// </summary>
        static HudSlotWidget SlotWidget(RectTransform slot, string key, float keySize, float keyWidth = 0f, bool upgrades = true)
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

            // Нажатие — короткая оранжевая вспышка. «Готово» — не постоянное свечение (владелец:
            // «слишком явное»): камень горит в полную силу, а в момент готовности по плитке проходит
            // одна волна света. Старая мерцающая искра спорила с камнем — убрана (24 сентября).
            widget.PressGlow = StateGlow(body, "Нажата", Role.Accent, .6f, 1f);
            Image wave = Layer(body, "Волна готовности", Kit("wc_fx_aura"), Role.Text, 0f, Slot * .3f);
            Additive(wave, new Color(1f, .84f, .55f, 0f));
            wave.enabled = false;

            // Камень + 8 насечек (владелец, 24 сентября: ступени камня «нечитаемы»). Насечки дают
            // точный счёт, камень — материал ступенями: сталь, серебро, золото, кристалл.
            RectTransform gemBox = Box(Node("Готовность", body), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, 9f), new Vector2(GemSize, GemSize));
            var ready = gemBox.gameObject.AddComponent<HudReadyGem>();
            ready.Grades = new Sprite[Game.Sim.RunLoadout.MaxUpgrades + 1];
            for (int grade = 0; grade < ready.Grades.Length; grade++) ready.Grades[grade] = GemFor(grade);
            Image halo = Mark(gemBox, "Сияние", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, GemSize * 1.9f);
            Additive(halo, new Color(1f, .88f, .62f, 0f));
            halo.enabled = false;
            ready.Halo = halo;
            ready.Gem = Mark(gemBox, "Камень", ready.Grades[0] != null ? ready.Grades[0] : T.Gem, Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, GemSize);
            Object.DestroyImmediate(ready.Gem.GetComponent<ThemeColor>());
            if (upgrades) ready.Notches = NotchRow(gemBox, out ready.NotchGroup);
            ready.Burst = wave;
            ready.Frame = widget.Frame;
            ready.ReadyColour = new Color(1f, .9f, .72f, 1f);
            widget.ReadyGem = ready;

            widget.CooldownText = Label(body, "Секунды", "0.0", FontRole.Body, Slot * .27f, Role.Text, TextAlignmentOptions.Center);
            widget.CooldownText.fontStyle = FontStyles.Bold;
            widget.CooldownText.gameObject.SetActive(false);

            // Нехватка лавидия: плашка с недостачей на нижней кромке (камень на углу убран — спорил с камнем усилений).
            RectTransform lacking = Stretch(Node("Нет лавидия", body));
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

        const int NotchCount = Game.Sim.RunLoadout.MaxUpgrades;
        const float NotchSize = 9f, NotchStep = 10f;

        /// <summary>
        /// Ряд из 8 насечек над камнем на тёмной плашке: над светлой землёй тоже читается.
        /// У каждой — серебряная оправа и заливка; цвет заливки ставит HudReadyGem. Ряд виден
        /// только при наведении на плитку (владелец, 24 сентября) — в покое прозрачен.
        /// </summary>
        static Image[] NotchRow(RectTransform gemBox, out CanvasGroup group)
        {
            float width = NotchStep * (NotchCount - 1) + NotchSize;
            RectTransform row = Box(Node("Насечки", gemBox), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, GemSize * .5f + 4f),
                new Vector2(width + 10f, NotchSize + 6f));
            group = row.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            Layer(row, "Подложка", T.TagFill, Role.Panel, .88f);
            var fills = new Image[NotchCount];
            for (int i = 0; i < NotchCount; i++)
            {
                var at = new Vector2(-width * .5f + NotchSize * .5f + i * NotchStep, 0f);
                RectTransform notch = Box(Node("Насечка " + (i + 1), row), new Vector2(.5f, .5f), new Vector2(.5f, .5f), at, new Vector2(NotchSize, NotchSize));
                Image fill = Layer(notch, "Заливка", T.DiamondFill, Role.Text, 1f);
                Object.DestroyImmediate(fill.GetComponent<ThemeColor>());
                fill.color = new Color(.08f, .09f, .12f, .9f);
                Layer(notch, "Оправа", T.DiamondFrameSmall, Role.PanelLine, .75f);
                fills[i] = fill;
            }
            return fills;
        }

        const string AdditivePath = "Assets/UI/Shaders/UiAdditive.mat";

        /// <summary>Материал света для UI (Razlom/UI Additive): цвет прибавляется, как у света.</summary>
        static Material AdditiveMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AdditivePath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Razlom/UI Additive")) { name = "UiAdditive" };
            AssetDatabase.CreateAsset(mat, AdditivePath);
            return mat;
        }

        /// <summary>Слой — свет: аддитивный материал и свой цвет вместо цвета темы.</summary>
        static void Additive(Graphic graphic, Color colour)
        {
            graphic.material = AdditiveMaterial();
            var tint = graphic.GetComponent<ThemeColor>();
            if (tint != null) Object.DestroyImmediate(tint);
            graphic.color = colour;
            graphic.raycastTarget = false;
        }
    }
}
