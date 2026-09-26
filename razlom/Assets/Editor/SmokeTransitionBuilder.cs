using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Дымная завеса перехода лагерь ↔ разлом и между аренами (<see cref="SmokeTransition"/>):
    /// Resources/UI/Prefabs/SmokeTransition.prefab. Владелец 26 сентября: переход «в стиле такой дымки,
    /// как комбат-худ, — красиво анимировано» вместо белой вспышки.
    ///
    /// Слои снизу вверх:
    ///  - «Основа» — сплошной глубокий дым (UiInkPlain, SmokeDeep, непрозрачный) на весь холст с запасом:
    ///    у клубов своё дыхание и просветы, а на кадре сборки арены сквозь завесу не должно быть видно ничего;
    ///  - «Клубы» — крупный дым: сердце в центре, кольцо вокруг, края и полосы сверху и снизу. Якоря в долях
    ///    холста, так что на широком экране (21:9, 32:9) клубы расходятся вместе с краями, а основа тянется;
    ///  - «Отсвет» — едва заметная холодная кремовая дымка в глубине, формой клуба, а не круглое пятно;
    ///  - «Угли» — редкие мелкие искры у нижнего края.
    /// Огня в завесе нет (владелец 26 сентября, «успокоить огонь»): кромки не тлеют, фронт широкий и мягкий —
    /// на первом кадре тлеющая кромка основы шла по всему экрану ржавой сыпью, а тёплый отсвет стоял
    /// красным «яйцом» посреди экрана.
    /// Холст 900 — поверх всего, включая паузу. Масштаб по высоте 1080, UiScaleFollower не нужен:
    /// завеса на весь экран при любом масштабе интерфейса.
    ///
    /// Время ведёт CampTransition; префаб только хранит детали и очередь клубов.
    /// </summary>
    public static class SmokeTransitionBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/SmokeTransition.prefab";
        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);
        /// <summary>Запас основы и полос за краем холста, единиц: неровный фронт не должен оставить щель у края.</summary>
        const float Overscan = 120f;

        [MenuItem("Разлом/UI/Собрать дымную завесу перехода")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Дымная завеса", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        /// <summary>Собрать префаб; без <paramref name="force"/> — только если его ещё нет.</summary>
        public static string Build(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return PrefabPath;
            UiThemeBuilder.Ensure(false);
            GameObject root = Layout();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return PrefabPath;
        }

        static GameObject Layout()
        {
            var root = new GameObject("SmokeTransition", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Поверх всего: HUD 10, итоги 50, окна лагеря 100–150, меню 200, пауза 300/320.
            canvas.sortingOrder = 900;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            var raycaster = root.AddComponent<GraphicRaycaster>();
            var veil = root.AddComponent<SmokeTransition>();
            veil.Canvas = canvas;
            veil.Raycaster = raycaster;
            var rect = (RectTransform)root.transform;

            // Основа: сплошная, растекается из центра последней и закрывает всё.
            RectTransform baseRect = Stretch(Node("Основа", rect), -Overscan);
            var fill = baseRect.gameObject.AddComponent<Image>();
            fill.sprite = T.Pixel;
            fill.type = Image.Type.Simple;
            fill.material = UiInkKit.Plain;
            // Ловит мышь, пока завеса на экране (GraphicRaycaster включает SmokeTransition).
            fill.raycastTarget = true;
            Tint(fill, Role.SmokeDeep, 1f);
            UiInkReveal baseInk = UiInkKit.Inked(fill);
            // Самый широкий фронт: основа наплывает мягкой тенью, а не мелкой рваной сыпью по шуму.
            baseInk.EdgeScale = 3f;
            baseInk.Burn = 0f;
            veil.Base = baseInk;

            RectTransform puffs = Stretch(Node("Клубы", rect));
            veil.Puffs = new[]
            {
                // Сердце: встаёт первым из центра и первым уходит.
                Puff(puffs, "Сердце", "smoke_blot_1", Center, new Vector2(1350f, 1300f), 0f, Role.SmokeDeep, 1f, 0f),
                // Кольцо вокруг: светлый сланцевый дым даёт фактуру поверх сплошной основы.
                Puff(puffs, "Клуб над центром", "smoke_blot_2", new Vector2(.3f, .66f), new Vector2(1150f, 1100f), 15f, Role.Smoke, .85f, .3f),
                Puff(puffs, "Клуб под центром", "smoke_blot_1", new Vector2(.7f, .34f), new Vector2(1150f, 1100f), 200f, Role.Smoke, .85f, .3f),
                Puff(puffs, "Клуб справа вверху", "smoke_blot_2", new Vector2(.74f, .74f), new Vector2(1050f, 1000f), -30f, Role.SmokeDeep, 1f, .5f),
                Puff(puffs, "Клуб слева внизу", "smoke_blot_1", new Vector2(.26f, .26f), new Vector2(1050f, 1000f), 110f, Role.SmokeDeep, 1f, .5f),
                // Края: на широком экране уходят вместе с краями холста.
                Puff(puffs, "Левый край", "smoke_blot_2", new Vector2(.03f, .5f), new Vector2(1250f, 1250f), 70f, Role.Smoke, .7f, .85f),
                Puff(puffs, "Правый край", "smoke_blot_1", new Vector2(.97f, .5f), new Vector2(1250f, 1250f), -80f, Role.Smoke, .7f, .85f),
                Band(puffs, "Низ", "smoke_band_1", false, 560f, 1f),
                Band(puffs, "Верх", "smoke_band_2", true, 520f, 1f),
            };

            // Дымка в глубине: свет формой клуба (белая маска дыма), холодный кремовый, вытянут вширь —
            // не круглый огненный light_glow (у того рыжий цвет в самом спрайте, оттенком его не остудить).
            // Силу ставит SmokeTransition.GlowStrength (не больше .08).
            Image glow = UiInkKit.LightAt(rect, "Отсвет", "smoke_blot_2", Center, new Vector2(0f, 30f), new Vector2(1600f, 950f), 1f, delay: 0f);
            glow.color = new Color(.88f, .91f, .95f, 1f);
            glow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 8f);
            veil.Glow = glow.GetComponent<UiInkReveal>();
            veil.Glow.Burn = 0f;
            veil.Glow.EdgeScale = 3f;

            // Угли у нижнего края: редкие и мелкие, пока завеса закрыта, пара — в миг полного закрытия.
            UiEmbers embers = UiInkKit.Embers(rect, "Угли", new Vector2(.5f, 0f), Vector2.zero, new Vector2(100f, 420f), 0f);
            RectTransform emberRect = embers.rectTransform;
            emberRect.anchorMin = new Vector2(0f, 0f);
            emberRect.anchorMax = new Vector2(1f, 0f);
            emberRect.pivot = new Vector2(.5f, 0f);
            emberRect.sizeDelta = new Vector2(-160f, 420f);
            emberRect.anchoredPosition = Vector2.zero;
            embers.SpawnBand = .35f;
            embers.Max = 12;
            embers.Life = new Vector2(1.2f, 2.4f);
            embers.Size = new Vector2(3f, 7f);
            embers.Speed = new Vector2(30f, 80f);
            embers.Sway = 14f;
            veil.Embers = embers;

            return root;
        }

        /// <summary>
        /// Клуб в долях холста. Начало на накате — сторона клуба к центру экрана, на рассеивании — к краю;
        /// обе пересчитаны в собственные доли повёрнутого клуба (так их читает UiInkReveal).
        /// </summary>
        static SmokeTransition.Puff Puff(RectTransform parent, string name, string sprite, Vector2 anchor, Vector2 size, float angle,
            Role role, float alpha, float order)
        {
            bool deep = role == Role.SmokeDeep;
            Image image = UiInkKit.SmokeAt(parent, name, sprite, anchor, Vector2.zero, size, alpha, role, deep: deep);
            image.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
            Vector2 away = new Vector2((anchor.x - .5f) * 1920f, (anchor.y - .5f) * 1080f);
            away = away.sqrMagnitude > 1f ? away.normalized : Vector2.up;
            return Entry(image, angle, away, order, order <= 0f);
        }

        /// <summary>Полоса дыма во всю ширину холста (с запасом) у верхнего или нижнего края.</summary>
        static SmokeTransition.Puff Band(RectTransform parent, string name, string sprite, bool top, float height, float order)
        {
            Image image = UiInkKit.SmokeLayer(parent, name, sprite, 1f, role: Role.SmokeDeep, deep: true);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = Center;
            rect.sizeDelta = new Vector2(Overscan * 4f, height);
            rect.anchoredPosition = new Vector2(0f, top ? -height * .22f : height * .22f);
            float angle = top ? 180f : 0f;
            rect.localEulerAngles = new Vector3(0f, 0f, angle);
            return Entry(image, angle, top ? Vector2.up : Vector2.down, order, false);
        }

        static SmokeTransition.Puff Entry(Image image, float angle, Vector2 away, float order, bool heart)
        {
            var ink = image.GetComponent<UiInkReveal>();
            // Широкий мягкий фронт и у клубов: дым наплывает, край не горит (силу кромки ставит
            // SmokeTransition.PuffBurn, по умолчанию 0).
            ink.EdgeScale = 2.8f;
            ink.Burn = 0f;
            Vector2 local = Quaternion.Euler(0f, 0f, -angle) * (Vector3)away;
            return new SmokeTransition.Puff
            {
                Ink = ink,
                Order = order,
                // Сердце растекается из своей середины; остальные — со стороны центра экрана.
                Inner = heart ? Center : Center - local * .35f,
                // Не угол клуба: от угла фронт при «скрыто 0» не дотягивается до дальнего угла.
                Outer = Center + local * .42f,
                Away = away,
            };
        }

        // ---------------------------------------------------------------- кадр для владельца
        /// <summary>
        /// Кадр завесы поверх кадра разлома, без запуска игры: <paramref name="cover"/> — накат (0..1),
        /// <paramref name="open"/> больше нуля — рассеивание после полного закрытия.
        /// </summary>
        public static string Capture(string outPath, float cover = .6f, float open = 0f)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var root = (RectTransform)inst.transform;
                const string backdrop = "../artifacts/editor-whirl/c1/f020.png";
                if (File.Exists(backdrop))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(backdrop));
                    var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                    back.texture = tex;
                    back.raycastTarget = false;
                    back.transform.SetSiblingIndex(0);
                }
                var veil = inst.GetComponent<SmokeTransition>();
                veil.BeginCover();
                if (open > 0f)
                {
                    veil.SetCover(1f);
                    veil.BeginOpen();
                    veil.SetOpen(open);
                }
                else veil.SetCover(cover);
            });
        }
    }
}
