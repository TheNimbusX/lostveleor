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
        const float MapSize = 230f, MapMargin = 24f;

        /// <summary>
        /// Карта справа сверху: квадратная серебряная рамка с полыми ромбами по углам
        /// (лист HUD), под ней плашка с названием места. Картинку карты и раскладку меток
        /// даёт HudMinimap, показывает их HudMinimapMarks в прямоугольнике MinimapArea.
        /// </summary>
        static void BuildMinimap(RectTransform root, CombatHudView view)
        {
            var topRight = new Vector2(1f, 1f);
            RectTransform frame = Box(Node("Миникарта", root), topRight, topRight, new Vector2(-MapMargin, -MapMargin), new Vector2(MapSize, MapSize));
            view.MinimapFrame = frame;
            Image shadow = Layer(frame, "Тень", T.Glow, Role.Veil, .6f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(frame, "Заливка", T.Pixel, Role.Panel, .95f);
            RectTransform area = Stretch(Node("Карта", frame), 1.5f);
            area.gameObject.AddComponent<RectMask2D>();
            view.MinimapArea = area;
            var image = Stretch(Node("Картинка", area)).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
            view.MinimapImage = image;
            // Туман Разлома: маска LayoutView белая, цвет даёт этот слой — чернила заливки пака.
            var fog = Stretch(Node("Туман", area)).gameObject.AddComponent<RawImage>();
            fog.raycastTarget = false;
            fog.enabled = false;
            Tint(fog, Role.Panel, 1f);
            Layer(area, "Затемнение края", T.VeilRadial, Role.Veil, .32f);

            void Edge(string name, Vector2 min, Vector2 max, Vector2 size)
            {
                RectTransform edge = Node(name, frame);
                edge.anchorMin = min;
                edge.anchorMax = max;
                edge.anchoredPosition = Vector2.zero;
                edge.sizeDelta = size;
                var img = edge.gameObject.AddComponent<Image>();
                img.sprite = T.Pixel;
                img.raycastTarget = false;
                Tint(img, Role.PanelLine, .85f);
            }
            Edge("Кромка сверху", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1.5f));
            Edge("Кромка снизу", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 1.5f));
            Edge("Кромка слева", Vector2.zero, new Vector2(0f, 1f), new Vector2(1.5f, 0f));
            Edge("Кромка справа", new Vector2(1f, 0f), Vector2.one, new Vector2(1.5f, 0f));
            foreach (var corner in new[] { Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 1f), Vector2.one })
            {
                // Два слоя заливки: у одной заливки темы прозрачность 0,92, и сквозь ромб проступал угол карты.
                RectTransform gem = At(Node("Ромб угла", frame), corner, Vector2.zero, new Vector2(18f, 18f));
                Layer(gem, "Подложка", T.DiamondFill, Role.Veil);
                Layer(gem, "Заливка", T.DiamondFill, Role.Panel);
                Layer(gem, "Оправа", T.DiamondFrameSmall, Role.PanelLine);
            }

            RectTransform plate = Box(Node("Подпись карты", root), topRight, new Vector2(.5f, 1f),
                new Vector2(-MapMargin - MapSize * .5f, -MapMargin - MapSize - 12f), new Vector2(190f, 34f));
            view.MinimapCaptionPanel = plate;
            Image plateShadow = Layer(plate, "Тень", T.GlowSmall, Role.Veil, .7f, 24f);
            plateShadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(plate, "Заливка", T.FillSmall, Role.Panel, .95f);
            Layer(plate, "Свет по кромке", T.HighlightSmall, Role.Highlight);
            Layer(plate, "Рамка", T.FrameSmall, Role.PanelLine, .85f);
            Mark(plate, "Ромб слева", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(0f, .5f), new Vector2(16f, 0f), 9f);
            Mark(plate, "Ромб справа", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(1f, .5f), new Vector2(-16f, 0f), 9f);
            view.MinimapCaption = Label(plate, "Надпись", "Лагерь", FontRole.Heading, 18f, Role.Text, TextAlignmentOptions.Center, 3f);
            view.MinimapCaption.fontStyle = FontStyles.UpperCase;
            view.MinimapCaption.margin = new Vector4(28f, 0f, 28f, 0f);
            view.MinimapCaption.enableAutoSizing = true;
            view.MinimapCaption.fontSizeMin = 12f;
            view.MinimapCaption.fontSizeMax = 18f;

            BuildMinimapMarks(root, area, fog, view);
        }

        /// <summary>
        /// Метки карты на холсте (аудит UI, 25 сентября; раньше — IMGUI старым шрифтом поверх окон).
        /// Враги — точки листа HUD, места — знак в тёмном круге с оправой, герой — стрелка пака.
        /// Подпись при наведении живёт вне маски карты, иначе у края её обрезало бы.
        /// </summary>
        static void BuildMinimapMarks(RectTransform root, RectTransform area, RawImage fog, CombatHudView view)
        {
            var topLeft = new Vector2(0f, 1f);
            var centre = new Vector2(.5f, .5f);
            RectTransform layer = Stretch(Node("Метки", area));
            var marks = layer.gameObject.AddComponent<HudMinimapMarks>();
            view.MinimapMarks = marks;

            RectTransform enemies = Stretch(Node("Враги", layer));
            RectTransform dot = Box(Node("Враг", enemies), topLeft, centre, Vector2.zero, new Vector2(12f, 12f));
            Layer(dot, "Обводка", T.CircleFill, Role.Veil, .9f);
            Stretch(Layer(dot, "Точка", T.CircleFill, Role.Health).rectTransform, 2f);
            dot.gameObject.SetActive(false);

            RectTransform places = Stretch(Node("Места", layer));
            RectTransform place = Box(Node("Место", places), topLeft, centre, Vector2.zero, new Vector2(24f, 24f));
            Image placeShadow = Layer(place, "Тень", RoundShadow, Role.Veil, .75f, 6f);
            placeShadow.rectTransform.anchoredPosition = new Vector2(0f, -1.5f);
            Layer(place, "Круг", T.CircleFill, Role.Panel);
            Layer(place, "Оправа", T.CircleFrame, Role.PanelLine, .9f);
            var symbol = Stretch(Node("Знак", place)).gameObject.AddComponent<RawImage>();
            symbol.raycastTarget = false;
            place.gameObject.SetActive(false);

            RectTransform hero = Box(Node("Герой", layer), topLeft, centre, Vector2.zero, new Vector2(18f, 18f));
            Layer(hero, "Тень", RoundShadow, Role.Veil, .6f, 5f);
            Layer(hero, "Стрелка", T.Arrow, Role.Text).preserveAspect = true;
            hero.gameObject.SetActive(false);

            RectTransform hint = Box(Node("Подпись метки", root), Vector2.one, new Vector2(1f, .5f), Vector2.zero, new Vector2(120f, 32f));
            Image hintShadow = Layer(hint, "Тень", T.GlowSmall, Role.Veil, .7f, 14f);
            hintShadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(hint, "Заливка", T.FillSmall, Role.Panel, .97f);
            Layer(hint, "Свет по кромке", T.HighlightSmall, Role.Highlight);
            Layer(hint, "Рамка", T.FrameSmall, Role.PanelLine, .8f);
            TMP_Text text = Label(hint, "Надпись", "Лео · 23 м", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            hint.gameObject.SetActive(false);

            marks.PlaceLayer = places;
            marks.EnemyLayer = enemies;
            marks.PlaceTemplate = place;
            marks.EnemyTemplate = dot;
            marks.Player = hero;
            marks.Fog = fog;
            marks.Hint = hint;
            marks.HintText = text;
            // Тот же знак, что у подсказки жителя с заказом (CampGuideWc).
            marks.AlchemistSymbol = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/potion_health_small.png");
        }
    }
}
