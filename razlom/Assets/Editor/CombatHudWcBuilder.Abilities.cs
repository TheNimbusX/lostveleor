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

            // Кувырок — плитка того же размера справа от ряда (вариант B); клавиша — капсула по ширине подписи.
            RectTransform dash = Box(Node("Кувырок", root), BottomCenter, Vector2.zero, new Vector2(DashX, RowBottom), new Vector2(Slot, Slot));
            view.DashPanel = dash;
            RectTransform dashSlot = Stretch(Node("Плитка кувырка", dash));
            view.Dash = SlotWidget(dashSlot, "SPACE", 22f, upgrades: false);
            view.Dash.Art.texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_Dash.png");
        }

        /// <summary>Свечение и цветное кольцо одного состояния плитки; выключено, пока его не включит CombatHudView.</summary>
        static GameObject StateGlow(RectTransform body, string name, Role role, float glow, float frame)
        {
            RectTransform group = Stretch(Node(name, body));
            Image halo = Layer(group, "Свечение", RoundGlow, role, glow, 22f);
            Layer(group, "Рамка", T.CircleFrameBold, role, frame);
            group.gameObject.SetActive(false);
            return group.gameObject;
        }

        /// <summary>Огонёк над верхней кромкой плитки: центр на столько выше её края.</summary>
        const float OrbLift = 1f;

        /// <summary>
        /// Плитка способности в материале «Дым и свет» (владелец 25 сентября): круглая иконка в
        /// чернильной кляксе, вокруг — огненное кольцо; при перезарядке — тёмная вуаль и кольцо
        /// делений, светлое кольцо при наведении, огонёк усилений с дугой точек, плашка нехватки
        /// лавидия, клавиша на нижней кромке.
        /// 26 сентября (владелец: «огня слишком много», «треугольники и круги — выбрать фигуру»):
        /// кольцо тоньше и в покое едва тлеет, разгорается у готовой и вспыхивает на готовности и
        /// нажатии (HudReadyGem); гранёный камень стал круглым огоньком, насечки — точками.
        /// </summary>
        static HudSlotWidget SlotWidget(RectTransform slot, string key, float keySize, bool upgrades = true)
        {
            var widget = slot.gameObject.AddComponent<HudSlotWidget>();
            widget.Hit = slot;
            RectTransform body = Stretch(Node("Тело", slot));
            widget.Body = body;

            UiInkKit.SmokeLayer(body, "Клякса", "smoke_ring", 1f, Slot * .2f, Slot * .2f);
            Layer(body, "Заливка", T.CircleFill, Role.Panel, .9f, -2f);
            RectTransform mask = Stretch(Node("Маска", body), 3f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = T.CircleFill;
            maskImage.type = Image.Type.Simple;
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

            // Огненное кольцо вместо серебряной рамки: HudReadyGem держит его яркость — тлеет в покое,
            // горит у готовой, вспыхивает на готовности и нажатии. Толщина нарисована в спрайте и
            // растёт с размером, поэтому кольцо чуть уже прежнего (было Slot * .2).
            var ready = Stretch(Node("Готовность", body)).gameObject.AddComponent<HudReadyGem>();
            widget.Frame = UiInkKit.LightLayer(body, "Огненное кольцо", "light_ring", ready.RestAlpha, Slot * .16f, delay: .3f);
            // Наведение и отказ: цвет ставит CombatHudView (белый или вспышка отказа), поэтому без темы.
            RectTransform highlight = Stretch(Node("Наведение", body), -1f);
            var hi = highlight.gameObject.AddComponent<Image>();
            hi.sprite = T.CircleFrameBold;
            hi.type = Image.Type.Simple;
            hi.raycastTarget = false;
            widget.Highlight = hi;
            highlight.gameObject.SetActive(false);

            // Нажатие — короткая оранжевая вспышка. «Готово» — не постоянное свечение (владелец:
            // «слишком явное»): в момент готовности по плитке проходит одна волна света.
            widget.PressGlow = StateGlow(body, "Нажата", Role.Accent, .45f, .8f);
            Image wave = Layer(body, "Волна готовности", Kit("wc_fx_aura"), Role.Text, 0f, Slot * .3f);
            Additive(wave, new Color(1f, .84f, .55f, 0f));
            wave.enabled = false;
            ready.Grades = new Sprite[0];
            ready.Burst = wave;
            ready.Frame = widget.Frame;
            ready.ReadyColour = new Color(1f, .9f, .72f, 1f);
            // Огонёк и дуга точек — только у способностей с усилениями: у кувырка их не бывает.
            if (upgrades) UpgradeOrb(body, ready);
            widget.ReadyGem = ready;

            widget.CooldownText = Label(body, "Секунды", "0.0", FontRole.Body, Slot * .27f, Role.Text, TextAlignmentOptions.Center);
            widget.CooldownText.fontStyle = FontStyles.Bold;
            widget.CooldownText.gameObject.SetActive(false);

            // Нехватка лавидия: недостача капсулой на нижней кромке — клуб дыма и тонкое кольцо цвета лавидия.
            RectTransform lacking = Stretch(Node("Нет лавидия", body));
            RectTransform pill = UiInkKit.Keycap(lacking, "Плашка", "−00", 20f);
            pill.anchorMin = pill.anchorMax = new Vector2(.5f, 0f);
            pill.pivot = new Vector2(.5f, 0f);
            pill.anchoredPosition = new Vector2(0f, 16f);
            pill.Find("Кольцо").GetComponent<ThemeColor>().SetRole(Role.Lavidium, .75f);
            widget.ResourceText = pill.Find("Буква").GetComponent<TMP_Text>();
            widget.ResourceText.GetComponent<ThemeColor>().SetRole(Role.Lavidium);
            widget.ResourceText.text = "−0";
            widget.ResourceText.fontStyle = FontStyles.Bold;
            widget.ResourceBadge = lacking.gameObject;
            lacking.gameObject.SetActive(false);

            // Клавиша на нижней кромке: круг у буквы, капсула у длинной подписи (SPACE). CombatHudView
            // прячет её у пустой плитки и подгоняет ширину после смены клавиши.
            RectTransform cap = UiInkKit.Keycap(body, "Клавиша", key, keySize);
            cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
            cap.pivot = new Vector2(.5f, .5f);
            cap.anchoredPosition = new Vector2(0f, -4f);
            widget.Key = cap.Find("Буква").GetComponent<TMP_Text>();
            return widget;
        }

        /// <summary>
        /// Огонёк усилений на верхней кромке плитки: круг цвета ступени (тусклый, сталь, серебро,
        /// золото, кристалл — красит HudReadyGem) в тёмной оправе, за ним мягкое сияние того же цвета.
        /// Над ним по кругу плитки — дуга из 8 точек (видна только под мышью).
        /// </summary>
        static void UpgradeOrb(RectTransform body, HudReadyGem ready)
        {
            RectTransform box = Box(Node("Огонёк", body), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, OrbLift),
                new Vector2(OrbSize + 6f, OrbSize + 6f));
            Image halo = Mark(box, "Сияние", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, OrbSize * 3.6f);
            Additive(halo, new Color(1f, .88f, .62f, 0f));
            halo.enabled = false;
            ready.Halo = halo;
            // Тёмная оправа: над светлой землёй огонёк не теряется.
            Mark(box, "Оправа", T.CircleFill, Role.SmokeDeep, .9f, new Vector2(.5f, .5f), Vector2.zero, OrbSize + 6f);
            ready.Gem = Mark(box, "Огонёк", T.CircleFill, Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, OrbSize);
            Object.DestroyImmediate(ready.Gem.GetComponent<ThemeColor>());
            ready.Gem.color = ready.OrbNone;
            ready.NotchOff = new Color(.3f, .32f, .38f, .95f);
            ready.Notches = DotArc(box, out ready.NotchGroup);
        }

        const int NotchCount = Game.Sim.RunLoadout.MaxUpgrades;
        /// <summary>Точка усиления, радиус дуги от центра плитки и шаг между точками в градусах.</summary>
        const float DotSize = 5f, DotRadius = 56f, DotStep = 9f;

        /// <summary>
        /// Дуга из 8 точек над огоньком по кругу плитки: сколько горит — столько усилений. У каждой
        /// тёмная оправа (читается над светлой землёй) и заливка; цвет заливки ставит HudReadyGem.
        /// Видна только при наведении на плитку (владелец, 24 сентября) — в покое прозрачна.
        /// </summary>
        static Image[] DotArc(RectTransform orb, out CanvasGroup group)
        {
            RectTransform arc = Box(Node("Точки усилений", orb), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(DotRadius * 1.2f, 40f));
            group = arc.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            // Центр плитки — ниже огонька на полплитки и подъём огонька.
            float centreY = -(Slot * .5f + OrbLift);
            var fills = new Image[NotchCount];
            for (int i = 0; i < NotchCount; i++)
            {
                float angle = (90f + (NotchCount - 1) * .5f * DotStep - i * DotStep) * Mathf.Deg2Rad;
                var at = new Vector2(Mathf.Cos(angle) * DotRadius, centreY + Mathf.Sin(angle) * DotRadius);
                RectTransform dot = Box(Node("Точка " + (i + 1), arc), new Vector2(.5f, .5f), new Vector2(.5f, .5f), at,
                    new Vector2(DotSize + 3f, DotSize + 3f));
                Layer(dot, "Оправа", T.CircleFill, Role.SmokeDeep, .85f);
                Image fill = Layer(dot, "Заливка", T.CircleFill, Role.Text, 1f, -1.5f);
                Object.DestroyImmediate(fill.GetComponent<ThemeColor>());
                fill.color = new Color(.3f, .32f, .38f, .95f);
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
