using System.IO;
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
    /// «Карта тушью» на закрытой завесе (<see cref="SmokeRouteMap"/>; выбор владельца 30.09, концепты
    /// ART/UI/concepts-2026-09-30-hud-final/a5-inkmap-draw.png и a6-inkmap-node.png): сверху «Путь по
    /// Разлому» с нитью света, слева пройденная дорога — кремовые круги тушью со знаками и мазки между ними,
    /// справа бледная развилка пустых кругов, кисть рисует мазок к выбранной арене, узел загорается
    /// спокойным тёплым кольцом, под ним «Арена N» и одно «впереди».
    ///
    /// Всё — детали «Дыма и света»: мазки набора (brush_stroke_*), круги тушью (ink_disc / ink_ring —
    /// tools/ui-kit/make-inkmap-kit.py из того же мазка), знаки забега (Assets/UI/RunIcons), свет кольца.
    /// Кромки не горят (Burn 0, фронт широкий): огонь — только у загоревшегося узла, и тот спокойный.
    /// Ромбов нет: нить заголовка без камня. Места узлов ставит SmokeRouteMap по SmokeRouteMapLayout,
    /// здесь только части; всё скрыто, пока карта не показана.
    /// </summary>
    public static partial class SmokeTransitionBuilder
    {
        /// <summary>Узел карты в префабе завесы.</summary>
        public const string MapName = "Карта тушью";

        /// <summary>Кремовая краска дороги и пройденных кругов (как бумага концептов, не белая).</summary>
        static readonly Color Paint = new Color(.925f, .894f, .83f, .94f);
        /// <summary>Бледная краска развилки: холодная, полупрозрачная — путь ещё не выбран.</summary>
        static readonly Color Faint = new Color(.72f, .76f, .82f, .34f);
        /// <summary>Тушь знака на кремовом круге.</summary>
        static readonly Color InkDark = new Color(.035f, .05f, .07f, .92f);

        const int PastSlots = SmokeRouteMapLayout.MaxPast;
        const int ForkSlots = 3;

        /// <summary>Знаки узлов по <see cref="SmokeRouteSign"/>: клинки, лагерь, заново, сундук, мешок, клинки, разлом.</summary>
        static readonly string[] SignFiles = { "encounter", "camp", "repeat", "cache", "items", "encounter", "rift" };

        /// <summary>Собрать «Карту тушью» под <paramref name="root"/> (последним ребёнком) и отдать её завесе.</summary>
        static RectTransform BuildMap(RectTransform root, SmokeTransition veil)
        {
            RectTransform node = Stretch(Node(MapName, root));
            var group = node.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var map = node.gameObject.AddComponent<SmokeRouteMap>();
            map.Group = group;

            // Заголовок — выше развилки: верхний круг пути стоит не выше ≈ +282, нить — на +382.
            map.Title = Text(node, "Заголовок", "Путь по Разлому", FontRole.Heading, 40f, .92f, new Vector2(0f, SmokeRouteMapLayout.TitleY),
                new Vector2(900f, 58f));
            map.Title.characterSpacing = 2f;
            Image thread = UiInkKit.LightAt(node, "Нить заголовка", "light_thread", Center, new Vector2(0f, SmokeRouteMapLayout.ThreadY),
                new Vector2(640f, 90f), .38f, delay: 0f);
            Calm(thread);
            map.TitleThread = thread;

            RectTransform content = Stretch(Node("Путь", node));
            map.Content = content;

            // Дороги: снизу подтёк свежей краски, пройденные мазки, бледная развилка, дорога дальше, свежий мазок.
            map.Halo = Road(content, "Подтёк", "brush_stroke_2", new Color(Paint.r, Paint.g, Paint.b, .13f), 74f, 3.1f);
            map.Halo.Wobble = 7f;
            map.PastRoads = new SmokeRouteStroke[PastSlots];
            for (int i = 0; i < PastSlots; i++)
                map.PastRoads[i] = Road(content, "Дорога " + (i + 1), i % 2 == 0 ? "brush_stroke_1" : "brush_stroke_2", Paint, 34f, .7f + i * 1.37f);
            map.ForkRoads = new SmokeRouteStroke[ForkSlots];
            for (int i = 0; i < ForkSlots; i++)
            {
                SmokeRouteStroke fork = Road(content, "Путь развилки " + (i + 1), "brush_stroke_2", Faint, 20f, 5.3f + i * 2.1f);
                fork.Wobble = 3f;
                map.ForkRoads[i] = fork;
            }
            map.Onward = Road(content, "Дорога дальше", "brush_stroke_1", new Color(Faint.r, Faint.g, Faint.b, .26f), 24f, 9.4f);
            map.Onward.Span = new Vector2(.3f, .97f);
            map.Road = Road(content, "Мазок", "brush_stroke_1", new Color(Paint.r, Paint.g, Paint.b, .96f), 38f, 2.2f);
            map.Road.Segments = 48;

            Image bleed = Disc(content, "Капля", "soft_blot", UiInkKit.Stroke, new Vector2(66f, 66f), new Color(Paint.r, Paint.g, Paint.b, .26f));
            map.Bleed = bleed;

            // Пройденные узлы: кремовый круг тушью, знак тушью, подпись под кругом.
            Texture[] signs = Signs();
            map.Stops = new SmokeRouteMap.Stop[PastSlots];
            for (int i = 0; i < PastSlots; i++)
            {
                RectTransform stop = At(Node("Узел " + (i + 1), content), Center, Vector2.zero, new Vector2(108f, 108f));
                Image disc = Disc(stop, "Диск", "ink_disc", UiInkKit.Stroke, new Vector2(108f, 108f), Paint);
                RawImage sign = Sign(stop, "Знак", signs[0], 58f, InkDark);
                TMP_Text label = Text(stop, "Подпись", "Арена " + (i + 1), FontRole.Body, 22f, .78f, new Vector2(0f, -86f), new Vector2(260f, 32f));
                map.Stops[i] = new SmokeRouteMap.Stop { Root = stop, Disc = disc, Sign = sign, Label = label };
            }

            // Развилка: пустые бледные круги.
            map.ForkRings = new Graphic[ForkSlots];
            for (int i = 0; i < ForkSlots; i++)
                map.ForkRings[i] = Disc(content, "Круг развилки " + (i + 1), "ink_ring", UiInkKit.Stroke,
                    Vector2.one * (SmokeRouteMapLayout.RingHalf * 2f), Faint);

            // Загоревшийся узел: тень дыма, мягкое тёплое свечение, тёмный круг тушью, спокойное кольцо света,
            // кремовый знак и пара искр в миг прихода кисти.
            RectTransform lit = At(Node("Узел выбора", content), Center, Vector2.zero, new Vector2(130f, 130f));
            map.Lit = lit;
            Image shade = UiInkKit.SmokeAt(lit, "Тень", "soft_blot", Center, Vector2.zero, new Vector2(250f, 250f), .6f, Role.SmokeDeep, deep: true);
            map.LitShade = Calm(shade);
            map.LitGlow = Calm(UiInkKit.LightAt(lit, "Свечение", "light_glow", Center, Vector2.zero, new Vector2(330f, 330f), .42f, delay: 0f));
            Image dark = Disc(lit, "Тёмный диск", "ink_disc", UiInkKit.Stroke, new Vector2(128f, 128f), Color.white);
            Tint(dark, Role.SmokeDeep, .96f);
            map.LitDisc = dark;
            map.LitRing = Calm(UiInkKit.LightAt(lit, "Кольцо света", "light_ring", Center, Vector2.zero, new Vector2(206f, 195f), .8f, delay: 0f));
            map.LitSign = Sign(lit, "Знак", signs[(int)SmokeRouteSign.Upgrade], 66f, Paint);
            UiEmbers sparks = UiInkKit.Embers(lit, "Искры", Center, new Vector2(0f, 30f), new Vector2(220f, 260f), 0f);
            sparks.Max = 8;
            sparks.Life = new Vector2(.9f, 1.8f);
            sparks.Size = new Vector2(3f, 6f);
            sparks.Speed = new Vector2(26f, 60f);
            sparks.SpawnBand = .35f;
            sparks.Sway = 10f;
            map.LitSparks = sparks;

            // Подпись: «Арена N» и одно «впереди» на тени дыма.
            RectTransform caption = At(Node("Подпись арены", content), Center, Vector2.zero, new Vector2(640f, 110f));
            map.Caption = caption;
            map.CaptionShade = Calm(UiInkKit.SmokeLayer(caption, "Тень подписи", "soft_blot", .62f, 90f, 40f, Role.SmokeDeep, deep: true));
            map.CaptionTitle = Text(caption, "Название", "Арена 2", FontRole.Heading, 50f, 1f, new Vector2(0f, 22f), new Vector2(640f, 62f));
            map.CaptionAhead = Text(caption, "Впереди", "Награда — способность или талант", FontRole.Body, 25f, .82f, new Vector2(0f, -26f),
                new Vector2(900f, 34f));

            map.Signs = signs;
            TuneMap(map);
            veil.Map = map;
            return node;
        }

        /// <summary>
        /// Числа карты после первого кадра в игре (30.09, миграция v2): нить заголовка и свет загоревшегося
        /// узла тише — огонь спокойный, не красное пятно; у мазков берётся шире полоса кисти — видна рваная
        /// кромка, а не гладкая трубка; подтёк бледнее; карта уходит за первые 30 % рассеивания — не висит
        /// поверх открывшейся арены. Абсолютные значения: повторный вызов ничего не меняет.
        /// </summary>
        static void TuneMap(SmokeRouteMap map)
        {
            SetAlpha(map.TitleThread, .24f);
            SetAlpha(map.LitGlow, .3f);
            if (map.LitGlow != null) map.LitGlow.rectTransform.sizeDelta = new Vector2(290f, 290f);
            SetAlpha(map.LitRing, .62f);
            SetAlpha(map.Halo, .1f);
            foreach (SmokeRouteStroke road in map.PastRoads) Brushy(road, 36f);
            foreach (SmokeRouteStroke road in map.ForkRoads) Brushy(road, 21f);
            Brushy(map.Onward, 25f);
            Brushy(map.Road, 40f);
            Brushy(map.Halo, 76f);
            map.OpenFade = .3f;
            EditorUtility.SetDirty(map);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
            EditorUtility.SetDirty(graphic);
        }

        /// <summary>Шире полоса кисти поперёк ленты — рваная кромка мазка; толщина — под неё.</summary>
        static void Brushy(SmokeRouteStroke road, float width)
        {
            if (road == null) return;
            road.Band = new Vector2(.19f, .81f);
            road.Width = width;
            EditorUtility.SetDirty(road);
        }

        static Texture[] Signs()
        {
            var signs = new Texture[SignFiles.Length];
            for (int i = 0; i < SignFiles.Length; i++)
            {
                string path = "Assets/UI/RunIcons/" + SignFiles[i] + ".png";
                signs[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (signs[i] == null) Debug.LogWarning("[ui-kit] Нет знака карты тушью: " + path);
            }
            return signs;
        }

        /// <summary>Мазок дороги на весь «Путь»: середина ленты — середина экрана, как у раскладки карты.</summary>
        static SmokeRouteStroke Road(RectTransform parent, string name, string sprite, Color color, float width, float seed)
        {
            RectTransform rect = Stretch(Node(name, parent));
            var road = rect.gameObject.AddComponent<SmokeRouteStroke>();
            road.Brush = UiInkKit.Sprite(sprite);
            road.material = UiInkKit.Stroke;
            road.color = color;
            road.raycastTarget = false;
            road.Width = width;
            // Полоса краски мазков набора — средние ≈ 0,27…0,73 высоты спрайта.
            road.Band = new Vector2(.24f, .76f);
            road.Seed = seed;
            road.EdgeScale = 2.5f;
            road.Hidden = 1f;
            return road;
        }

        /// <summary>Круг (или капля) тушью: спрайт набора, материал «Дыма и света», проявление без огня.</summary>
        static Image Disc(RectTransform parent, string name, string sprite, Material material, Vector2 size, Color color)
        {
            RectTransform rect = At(Node(name, parent), Center, Vector2.zero, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiInkKit.Sprite(sprite);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.material = material;
            image.color = color;
            image.raycastTarget = false;
            UiInkKit.Inked(image);
            return Calm(image);
        }

        /// <summary>Знак забега (белая маска 256 px), цвет — тушь или краска.</summary>
        static RawImage Sign(RectTransform parent, string name, Texture texture, float size, Color color)
        {
            RectTransform rect = At(Node(name, parent), Center, Vector2.zero, new Vector2(size, size));
            var art = rect.gameObject.AddComponent<RawImage>();
            art.texture = texture;
            art.material = UiInkKit.Art;
            art.color = color;
            art.raycastTarget = false;
            UiInkKit.Inked(art);
            return Calm(art);
        }

        /// <summary>Надпись по середине в одну строку, проявляется по буквам.</summary>
        static TMP_Text Text(RectTransform parent, string name, string text, FontRole font, float size, float alpha, Vector2 position, Vector2 box)
        {
            TMP_Text label = UiInkKit.Label(parent, name, text, font, size, Role.Text, TextAlignmentOptions.Center, alpha: alpha, delay: 0f);
            At(label.rectTransform, Center, position, box);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.GetComponent<UiInkText>().Hidden = 1f;
            return label;
        }

        /// <summary>Проявление без тлеющей кромки, с широким мягким фронтом: карта — тушь на дыму, не огонь.</summary>
        static T Calm<T>(T graphic) where T : Graphic
        {
            var ink = graphic.GetComponent<UiInkReveal>();
            if (ink != null)
            {
                ink.Burn = 0f;
                ink.EdgeScale = 2.5f;
                ink.Hidden = 1f;
            }
            return graphic;
        }

        // ---------------------------------------------------------------- кадр для владельца

        /// <summary>
        /// Кадр карты на закрытой завесе без запуска игры: пройдено до <paramref name="from"/> (0 — вход из
        /// лагеря), развилка из <paramref name="count"/>, выбран <paramref name="chosen"/>, <paramref name="time"/> —
        /// секунды показа (≈ .45 — кисть на полпути, ≈ 1,2 — узел горит, подпись прочитана).
        /// </summary>
        public static string CaptureMap(string outPath, float time, int from = 2, int count = 3, int chosen = 0,
            SmokeRouteSign sign = SmokeRouteSign.Upgrade, string ahead = "Впереди — новый враг: Шипомёт")
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var veil = inst.GetComponent<SmokeTransition>();
                veil.BeginCover();
                veil.SetCover(1f);
                if (veil.Map == null) return;
                SmokeRouteMap.Route route = from > 0 ? SmokeRouteMap.Route.Advance : SmokeRouteMap.Route.FromCamp;
                veil.Map.Preview(route, from, count, chosen, sign, ahead, time);
            });
        }

        [MenuItem("Разлом/UI/Снимок карты тушью")]
        static void CaptureMapFromMenu()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "inkmap-preview"));
            Directory.CreateDirectory(folder);
            foreach (float time in new[] { .1f, .45f, .75f, 1.4f })
                Debug.Log("[ui-kit] Карта тушью: " + CaptureMap(Path.Combine(folder, "inkmap-" + time.ToString("0.00") + ".png"), time));
        }
    }
}
