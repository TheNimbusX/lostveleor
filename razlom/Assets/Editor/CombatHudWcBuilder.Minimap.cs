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
        /// <summary>Сторона карты: 29.09 владелец — +10 % (было 230), только карта, остальной HUD как был.</summary>
        const float MapSize = 253f, MapMargin = 24f;
        const string MapVeilName = "Затемнение края";
        const string MapVeilMaterialPath = "Assets/UI/Shaders/UiInkMapVeil.mat";

        /// <summary>
        /// Карта справа сверху в материале «Дым и свет» (владелец 25 сентября): рамки нет — карта
        /// нарисована чернилами, края тают в клуб дыма (мягкая форма шейдера); огненную дугу владелец
        /// убрал (25 сентября). Под картой подпись места на дыму с огненными ромбами. Картинку карты
        /// и раскладку меток даёт HudMinimap, показывает их HudMinimapMarks в MinimapArea.
        ///
        /// 26 сентября: клуб округлый (map_shape — суперэллипс 2,4 с широким пером, метки у края
        /// HudMinimap прижимает по той же форме), кромка проходов Разлома — мягкая кремовая линия
        /// кистью по полю расстояний (шейдер Resources/UI/HUD/MinimapInk), а не серебряная лесенка.
        /// Картинка карты — RenderTexture с мипами под ширину карты на экране; префаб её не хранит,
        /// в кадре редактора (Preview) по-прежнему статичный образец wc_map_sample.
        ///
        /// 29 сентября (владелец): карта на 10 % крупнее (230 → 253), подпись под ней сдвинута;
        /// затемнение края — по округлой форме карты, без квадратных углов (<see cref="ShapedVeil"/>).
        /// </summary>
        static void BuildMinimap(RectTransform root, CombatHudView view)
        {
            var topRight = new Vector2(1f, 1f);
            RectTransform frame = Box(Node("Миникарта", root), topRight, topRight, new Vector2(-MapMargin, -MapMargin), new Vector2(MapSize, MapSize));
            view.MinimapFrame = frame;
            UiInkKit.SmokeLayer(frame, "Дым", "smoke_blot_2", 1f, 70f, 70f);
            RectTransform area = Stretch(Node("Карта", frame), 1.5f);
            area.gameObject.AddComponent<RectMask2D>();
            view.MinimapArea = area;
            var image = Stretch(Node("Картинка", area)).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
            image.material = UiInkKit.Map;
            UiInkKit.Inked(image);
            view.MinimapImage = image;
            // Туман Разлома: маска LayoutView белая, цвет даёт этот слой — чернила заливки пака.
            var fog = Stretch(Node("Туман", area)).gameObject.AddComponent<RawImage>();
            fog.raycastTarget = false;
            fog.enabled = false;
            fog.material = UiInkKit.Map;
            UiInkKit.Inked(fog);
            Tint(fog, Role.Smoke, 1f);
            ShapedVeil(Layer(area, MapVeilName, T.VeilRadial, Role.Veil, .32f));

            RectTransform plate = Box(Node("Подпись карты", root), topRight, new Vector2(.5f, 1f),
                new Vector2(-MapMargin - MapSize * .5f, -MapMargin - MapSize - 12f), new Vector2(190f, 34f));
            view.MinimapCaptionPanel = plate;
            UiInkKit.SmokeLayer(plate, "Дым", "smoke_band_2", 1f, 50f, 24f);
            UiInkKit.LightAt(plate, "Ромб слева", "light_gem", new Vector2(0f, .5f), new Vector2(16f, 0f), new Vector2(14f, 16f), .9f, delay: .3f);
            UiInkKit.LightAt(plate, "Ромб справа", "light_gem", new Vector2(1f, .5f), new Vector2(-16f, 0f), new Vector2(14f, 16f), .9f, delay: .3f);
            view.MinimapCaption = UiInkKit.Label(plate, "Надпись", "Лагерь", FontRole.Heading, 18f, Role.Text, TextAlignmentOptions.Center, 3f);
            view.MinimapCaption.fontStyle = FontStyles.UpperCase;
            view.MinimapCaption.margin = new Vector4(28f, 0f, 28f, 0f);
            view.MinimapCaption.enableAutoSizing = true;
            view.MinimapCaption.fontSizeMin = 12f;
            view.MinimapCaption.fontSizeMax = 18f;

            BuildMinimapMarks(root, area, fog, view);
        }

        /// <summary>
        /// Затемнение края карты по её округлой форме (владелец 29.09: «квадратная тень»). Радиальная
        /// вуаль пака непрозрачна в углах квадрата, а сама карта тает по мягкой форме map_shape —
        /// тёмные углы торчали за клубом квадратом. Теперь вуаль режет та же форма (материал как
        /// UiInkMap, но без светлой дымки по краям — вуаль должна темнить, а не светлеть): остаётся
        /// мягкое затемнение внутри клуба у его края. Форма ложится по uv2 элемента (UiInkReveal).
        /// </summary>
        static void ShapedVeil(Image veil)
        {
            if (veil == null) return;
            veil.material = InkMaterial(UiInkKit.Map, MapVeilMaterialPath, "UiInkMapVeil", m => m.SetFloat("_WispAmount", 0f));
            veil.raycastTarget = false;
            if (veil.GetComponent<UiInkReveal>() == null) UiInkKit.Inked(veil);
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
            UiInkKit.SmokeLayer(hint, "Дым", "smoke_band_1", 1f, 40f, 20f, origin: new Vector2(1f, .5f));
            TMP_Text text = UiInkKit.Label(hint, "Надпись", "Лео · 23 м", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .05f);
            UiInkKit.Group(hint, UiInkGroup.Sweep.FromCenter, .25f, .05f).Burn = 0f;
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
