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
    /// Главное меню М1 «Трещина» (выбор владельца 25 сентября, концепт
    /// ART/UI/concepts-2026-09-25-material/7-menu-min-crack.png) на фоне «Дым»: 26 сентября светлый
    /// рассвет с большой трещиной поверх читался хуже, владелец выбрал минималистичный фон
    /// (ART/UI/concepts-2026-09-26-pass/menu-bg/mock-6-smoke.png). Фон — menu_smoke: сланцево-бирюзовый
    /// чернильный дым на тёмной сине-серой земле, справа (~0,68–0,71 ширины) нарисован тонкий рваный
    /// шов огня разлома с углями; левая половина — спокойный тёмный дым, на нём без плашек стоят лого
    /// и пункты меню. Отдельной трещины, дымки и тени слева, светлого тумана и виньетки больше нет:
    /// трещина нарисована в самом фоне (две двоились бы), а светлые слои размывали тёмный дым. Под
    /// столбиком меню — едва заметная глубокая тень, чтобы кремовый текст не терялся на светлых клубах.
    ///
    /// Нарисованный шов живёт: над ним лежит его же свет, вынутый из фона (menu_smoke_glow — ядро с
    /// ближним заревом, menu_smoke_halo — дальнее зарево; tools/ui-kit/make-menu-seam.py), на аддитивном
    /// свете «Дыма и света». Слои — дети картины фона на весь её прямоугольник, поэтому совпадают со
    /// швом на любых пропорциях экрана. Свет медленно мерцает, зарево дышит, шов изредка тихо
    /// трескается искрами, вдоль него всплывают редкие угли (<see cref="Seam"/>), вся картина едва
    /// заметно дышит масштабом. При открытии свет шва разгорается сверху вниз, лого прожигается,
    /// пункты проявляются по очереди; под мышью пункт теплеет, шов мягко вспыхивает (MainMenuRift,
    /// MainMenuInkItem).
    ///
    /// Лого — logo_stone_hd (чистая альфа, мипмапы Кайзера) и его огонь logo_stone_glow поверх: мерцает,
    /// вдоль трещины через «REMAINS» всплывают угли (tools/ui-kit/make-menu-logo.py).
    /// Подтверждение новой игры — окно «Дыма и света» (UiInkKit.Plate). Логика — MainMenuView.
    /// </summary>
    public static class MainMenuWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/MainMenuWc.prefab";
        const string Art = "Assets/Resources/UI/MainMenu/";
        /// <summary>
        /// Фон меню. Запасные — светлый рассвет над лесом "menu_dawn.png" и сумерки "menu_twilight.png":
        /// шва в них нет, и без их menu_*_glow.png слоёв света и углей просто не будет (BuildRift).
        /// </summary>
        const string Background = "menu_smoke.png";
        static UiTheme T => UiTheme.Current;

        [MenuItem("Разлом/UI/Собрать главное меню «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Главное меню", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

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

        static Texture Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Art + file);

        /// <summary>
        /// Картина фона и слои её света — в своих пропорциях. Импорт до MainMenuArtImport с
        /// npotScale = None подогнал бы их к степени двойки (4096×2292 → 4096×2048): фон, который
        /// подгоняется под экран по пропорциям текстуры, растянулся бы вширь. Такую — переимпортировать
        /// с нынешними настройками (раз: дальше пропорции совпадают).
        /// </summary>
        static Texture PlateTex(string file)
        {
            string path = Art + file;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) return texture;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width <= 0 || height <= 0) return texture;
            float source = width / (float)height, imported = texture.width / (float)texture.height;
            if (Mathf.Abs(imported / source - 1f) < .005f) return texture;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Layout()
        {
            var root = new GameObject("MainMenuWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над игрой и HUD, под настройками паузы (300): «Настройки» открываются поверх меню.
            canvas.sortingOrder = 200;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<MainMenuPanel>();
            panel.Group = root.AddComponent<CanvasGroup>();
            var rect = (RectTransform)root.transform;

            // Фон на весь экран, без полей на любых пропорциях: «Фон» подгоняется под экран (с запасом,
            // лишнее уходит за край поровну), «Картина» в нём — сам рисунок со светом шва и углями. Всё,
            // что лежит на рисунке, — её дети на её прямоугольнике: та же подгонка, что у рисунка, и на
            // 16:10 или 21:9 свет стоит точно на нарисованном шве. Картина же дышит масштабом.
            RectTransform back = Stretch(Node("Фон", rect));
            Texture backTexture = PlateTex(Background);
            var fitter = back.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = backTexture != null ? backTexture.width / (float)backTexture.height : 16f / 9f;
            RectTransform plate = Stretch(Node("Картина", back));
            var backImage = plate.gameObject.AddComponent<RawImage>();
            backImage.texture = backTexture;
            // Фон ловит мышь: клики не проходят в игру под меню.
            backImage.raycastTarget = true;

            MainMenuRift rift = BuildRift(back, plate, fitter.aspectRatio);

            // Под столбиком меню — едва заметная глубокая тень: слева дым спокойный и тёмный, но у
            // «Выхода» проходят светлые клубы. От левого верхнего угла, как само меню.
            UiInkKit.SmokeAt(rect, "Тень под меню", "soft_blot", new Vector2(0f, 1f), new Vector2(390f, -640f), new Vector2(900f, 560f), .2f,
                origin: new Vector2(0f, .5f), deep: true);

            // Лого и меню проявляются вслед за светом шва, сверху вниз.
            RectTransform front = Stretch(Node("Лого и меню", rect));
            UiInkGroup frontAppear = UiInkKit.Group(front, UiInkGroup.Sweep.TopToBottom, .7f, .55f);
            frontAppear.StartDelay = .65f;

            BuildLogo(front, rift);

            RectTransform menu = TopLeft(Node("Меню", front), 170f, 470f, MenuWidth, 330f);
            RectTransform continueButton = MenuItem(menu, "Продолжить", 0f, 96f, true, rift, out TMP_Text continueLabel, out RectTransform marker);
            panel.Continue = continueButton.GetComponent<Button>();
            panel.ContinueLabel = continueLabel;
            panel.ContinueBullet = marker;
            RectTransform line = Node("Сохранение", continueLabel.transform.parent);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0f, 0f);
            line.offsetMin = new Vector2(TextInset, 14f);
            line.offsetMax = new Vector2(-24f, 40f);
            panel.ContinueLine = UiInkKit.Label(line, "Надпись", "", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, 0f, 1f, .2f);
            panel.SetContinueLine("Пелаг · уровень 5");

            panel.NewGame = MenuItem(menu, "Новая игра", 110f, 60f, false, rift, out _, out _).GetComponent<Button>();
            panel.Settings = MenuItem(menu, "Настройки", 176f, 60f, false, rift, out _, out _).GetComponent<Button>();
            panel.Exit = MenuItem(menu, "Выход", 242f, 60f, false, rift, out _, out _).GetComponent<Button>();

            BuildConfirm(rect, panel);
            return root;
        }

        const float MenuWidth = 480f;
        const float TextInset = 46f;
        const float LogoWidth = 820f;
        const float LogoHeight = LogoWidth * 636f / 2048f;
        /// <summary>Поле огня вокруг лого, доля его ширины с каждой стороны (GLOW_PAD в make-menu-logo.py).</summary>
        const float LogoGlowPad = 48f / 1024f;

        /// <summary>
        /// Трещина через «REMAINS»: ломаная в долях лого (x слева, y сверху), печатает
        /// tools/ui-kit/make-menu-logo.py. По ней стоят угли — по одному излучателю на отрезок.
        /// </summary>
        static readonly Vector2[] LogoCrack =
        {
            new Vector2(0f, .556f), new Vector2(.4f, .605f), new Vector2(.8f, .749f), new Vector2(1f, .928f),
        };

        /// <summary>
        /// Шов разлома на фоне menu_smoke: ломаная в долях картины (x слева, y сверху), печатает
        /// tools/ui-kit/make-menu-seam.py. По ней стоят угли — по одному излучателю на отрезок.
        /// </summary>
        static readonly Vector2[] Seam =
        {
            new Vector2(.679f, 0f), new Vector2(.685f, .111f), new Vector2(.688f, .222f), new Vector2(.68f, .333f),
            new Vector2(.71f, .444f), new Vector2(.693f, .556f), new Vector2(.714f, .667f), new Vector2(.69f, .778f),
            new Vector2(.701f, .889f), new Vector2(.697f, 1f),
        };

        /// <summary>Искр в секунду на весь шов в покое — редко: угли, а не искропад.</summary>
        const float SeamSparkRate = 2.4f;
        /// <summary>Поле по обе стороны шва, где рождаются угли, единицы Canvas.</summary>
        const float SeamSpread = 22f;

        /// <summary>
        /// Свой материал меню — копия материала UiInkKit с поправками. Числа берутся из исходного
        /// материала при каждой сборке (как у материалов UiInkKit); лежит рядом с ними.
        /// </summary>
        static Material MenuMaterial(string name, Material source, System.Action<Material> setup)
        {
            if (source == null) return null;
            string path = "Assets/UI/Shaders/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(source) { name = name };
            else mat.CopyPropertiesFromMaterial(source);
            setup(mat);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Картинка без течения и без светлых прожилок дыма: лого. Копия UiInkKit.Plain с
        /// _WispAmount = 0 — у Plain светлые завихрения дыма бродили по камню лого голубыми пятнами и
        /// поднимали его тёмный контур до серого.
        /// </summary>
        static Material MenuPlain => MenuMaterial("UiInkMenuPlain", UiInkKit.Plain, m => m.SetFloat("_WispAmount", 0f));

        /// <summary>
        /// Свет шва: копия UiInkKit.Light, но картинка лежит точно на нарисованном шве. Течение почти
        /// нулевое — у Light выборка гуляет на ±0,3 % картинки, на весь экран это ±6 px, и нити света
        /// двоились бы с нарисованными; здесь ±1 px — лёгкое марево. Плотность дышит пятнами (шов тлеет
        /// неравномерно), пульс тихий, ток бежит вдоль шва (по y), а не поперёк.
        /// </summary>
        static Material MenuSeamLight => MenuMaterial("UiInkMenuSeam", UiInkKit.Light, m =>
        {
            m.SetFloat("_FlowAmount", .0012f);
            m.SetFloat("_FlowSpeed", .03f);
            m.SetFloat("_Breath", .3f);
            m.SetFloat("_Pulse", .06f);
            m.SetFloat("_PulseSpeed", 1.1f);
            m.SetFloat("_Shimmer", .45f);
            m.SetVector("_ShimmerAxis", new Vector4(0f, 1f, 0f, 0f));
        });

        /// <summary>
        /// Разлом на картине фона: над нарисованным швом — его же свет (ядро с ближним заревом и дальнее
        /// зарево, свет «Дыма и света» — прибавляется к фону), вдоль шва — редкие угли. Слои — дети
        /// картины на весь её прямоугольник: совпадают со швом на любых пропорциях и дышат масштабом
        /// вместе с фоном. При открытии свет шва разгорается от его верха вниз (UiInkGroup, без огненной
        /// кромки — свет лежит на всю картину, кромка обвела бы кольцом весь экран). Жизнь —
        /// MainMenuRift. У запасных фонов шва нет: без их menu_*_glow.png свет и угли не строятся.
        /// </summary>
        static MainMenuRift BuildRift(RectTransform back, RectTransform plate, float aspect)
        {
            var rift = back.gameObject.AddComponent<MainMenuRift>();
            rift.Plate = plate;
            // Фон дышит от шва: он стоит на месте, дым вокруг едва заметно расходится и сходится.
            plate.pivot = new Vector2(Seam[4].x, .5f);
            string name = Path.GetFileNameWithoutExtension(Background);
            Texture core = PlateTex(name + "_glow.png");
            if (core == null) return rift;
            Texture far = PlateTex(name + "_halo.png");

            RectTransform light = Stretch(Node("Свет шва", plate));
            UiInkGroup open = UiInkKit.Group(light, UiInkGroup.Sweep.TopToBottom, 1.6f, 0f);
            open.StartDelay = .15f;
            open.Burn = 0f;
            var top = new Vector2(Seam[0].x, 1f);
            Material material = MenuSeamLight;
            if (far != null) rift.Glow = SeamLayer(light, "Дальнее зарево", far, material, rift.GlowRest, top, 0f);
            rift.Crack = SeamLayer(light, "Шов", core, material, rift.CrackRest, top, .15f);

            // Угли: излучатель на каждый отрезок ломаной, прямоугольник отрезка в долях картины и поле
            // по обе стороны шва. Искры рождаются по всей его площади, всплывают и гаснут; частота —
            // доля общей по длине отрезка.
            RectTransform sparks = Stretch(Node("Угли шва", plate));
            var lengths = new float[Seam.Length - 1];
            float total = 0f;
            for (int i = 0; i < lengths.Length; i++)
            {
                Vector2 d = Seam[i + 1] - Seam[i];
                lengths[i] = new Vector2(d.x * aspect, d.y).magnitude;
                total += lengths[i];
            }
            var embers = new UiEmbers[lengths.Length];
            for (int i = 0; i < embers.Length; i++)
            {
                Vector2 a = Seam[i], b = Seam[i + 1];
                UiEmbers ember = UiInkKit.Embers(sparks, "Угли " + (i + 1), Vector2.zero, Vector2.zero, Vector2.zero,
                    SeamSparkRate * lengths[i] / Mathf.Max(total, .0001f));
                RectTransform box = ember.rectTransform;
                // y ломаной — сверху, у якорей — снизу.
                box.anchorMin = new Vector2(Mathf.Min(a.x, b.x), 1f - Mathf.Max(a.y, b.y));
                box.anchorMax = new Vector2(Mathf.Max(a.x, b.x), 1f - Mathf.Min(a.y, b.y));
                box.offsetMin = new Vector2(-SeamSpread, 0f);
                box.offsetMax = new Vector2(SeamSpread, 0f);
                ember.SpawnBand = 1f;
                ember.Life = new Vector2(2f, 4.5f);
                ember.Size = new Vector2(4f, 10f);
                ember.Speed = new Vector2(16f, 46f);
                ember.Sway = 14f;
                ember.Max = 12;
                ember.color = new Color(1f, .84f, .66f, .85f);
                embers[i] = ember;
            }
            rift.Embers = embers;
            return rift;
        }

        /// <summary>Слой света шва на всю картину: RGB на чёрном, сила — альфа цвета (её двигает MainMenuRift).</summary>
        static RawImage SeamLayer(RectTransform parent, string name, Texture texture, Material material, float strength, Vector2 origin, float delay)
        {
            RectTransform rect = Stretch(Node(name, parent));
            var layer = rect.gameObject.AddComponent<RawImage>();
            layer.texture = texture;
            layer.material = material;
            layer.color = new Color(1f, 1f, 1f, strength);
            layer.raycastTarget = false;
            UiInkKit.Inked(layer, origin, delay);
            return layer;
        }

        /// <summary>
        /// Лого «расписной камень» и его огонь: слой света поверх лого (мерцает, по трещине бежит ток —
        /// свет UiInkKit.Light), угли вдоль трещины. Живость огня — MainMenuRift.
        /// </summary>
        static void BuildLogo(RectTransform front, MainMenuRift rift)
        {
            RectTransform logo = TopLeft(Node("Лого", front), 100f, 150f, LogoWidth, LogoHeight);
            var logoImage = logo.gameObject.AddComponent<RawImage>();
            Texture logoTexture = Tex("logo_stone_hd.png");
            if (logoTexture == null)
            {
                Debug.LogWarning("Нет " + Art + "logo_stone_hd.png (tools/ui-kit/make-menu-logo.py) — лого из старой текстуры");
                logoTexture = Tex("logo_painted_stone.png");
            }
            logoImage.texture = logoTexture;
            logoImage.raycastTarget = false;
            logoImage.material = MenuPlain;
            UiInkKit.Inked(logoImage, new Vector2(0f, .5f));

            Texture fireTexture = Tex("logo_stone_glow.png");
            if (fireTexture == null) return;
            RectTransform fireBox = Stretch(Node("Огонь", logo), -LogoGlowPad * LogoWidth);
            var fire = fireBox.gameObject.AddComponent<RawImage>();
            fire.texture = fireTexture;
            fire.material = UiInkKit.Light;
            fire.color = new Color(1f, 1f, 1f, rift.LogoRest);
            fire.raycastTarget = false;
            // Загорается после камня: лого прожглось — трещина в нём вспыхивает.
            UiInkKit.Inked(fire, new Vector2(0f, .5f), .35f);
            rift.LogoGlow = fire;

            // Угли: излучатель на каждый отрезок трещины, повёрнут вдоль него, рождает искры у самой
            // трещины (нижняя полоса в 6 единиц) — они всплывают над камнем и гаснут.
            var embers = new UiEmbers[LogoCrack.Length - 1];
            for (int i = 0; i < embers.Length; i++)
            {
                Vector2 a = new Vector2(LogoCrack[i].x * LogoWidth, -LogoCrack[i].y * LogoHeight);
                Vector2 b = new Vector2(LogoCrack[i + 1].x * LogoWidth, -LogoCrack[i + 1].y * LogoHeight);
                float length = Vector2.Distance(a, b);
                const float height = 80f;
                UiEmbers sparks = UiInkKit.Embers(logo, "Угли " + (i + 1), new Vector2(0f, 1f), Vector2.zero, new Vector2(length, height),
                    2.2f * length / LogoWidth);
                RectTransform sparksRect = sparks.rectTransform;
                sparksRect.pivot = new Vector2(.5f, 0f);
                sparksRect.anchoredPosition = (a + b) * .5f;
                sparksRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                sparks.SpawnBand = 6f / height;
                sparks.Life = new Vector2(1f, 2.2f);
                sparks.Size = new Vector2(3f, 7f);
                sparks.Speed = new Vector2(10f, 30f);
                sparks.Sway = 7f;
                sparks.Max = 16;
                sparks.color = new Color(1f, .86f, .7f, .9f);
                embers[i] = sparks;
            }
            rift.LogoEmbers = embers;
        }

        /// <summary>
        /// Пункт меню М1: текст без плашки, огненный ромб перед ним, прозрачная область под мышь.
        /// featured — «Продолжить»: крупнее, тёплый и с ромбом и без мыши.
        /// </summary>
        static RectTransform MenuItem(RectTransform menu, string text, float y, float h, bool featured, MainMenuRift rift,
            out TMP_Text label, out RectTransform marker)
        {
            RectTransform button = TopLeft(Node(text, menu), 0f, y, MenuWidth, h);
            var hit = button.gameObject.AddComponent<Image>();
            hit.sprite = T.Pixel;
            hit.color = new Color(1f, 1f, 1f, 0f);
            var clickable = button.gameObject.AddComponent<Button>();
            clickable.transition = Selectable.Transition.None;
            clickable.targetGraphic = hit;

            RectTransform body = Stretch(Node("Тело", button));
            label = UiInkKit.Label(body, "Надпись", text, FontRole.Heading, featured ? 34f : 28f, Role.Text, TextAlignmentOptions.MidlineLeft, 1.5f);
            Object.DestroyImmediate(label.GetComponent<ThemeColor>());
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.offsetMin = new Vector2(TextInset, 0f);

            marker = At(Node("Ромб", body), new Vector2(0f, .5f), new Vector2(18f, 0f), new Vector2(22f, 24f));
            var markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            markerGroup.blocksRaycasts = false;
            UiInkKit.LightLayer(marker, "Свет", "light_gem", 1f, 4f, delay: .25f);

            var item = button.gameObject.AddComponent<MainMenuInkItem>();
            item.Label = label;
            item.Body = body;
            item.Marker = markerGroup;
            item.Rift = rift;
            item.Featured = featured;
            var hover = button.gameObject.AddComponent<UiHoverMotion>();
            hover.HoverScale = 1f;
            hover.Body = body;
            return button;
        }

        /// <summary>
        /// «Начать новую игру?» — стирание лагеря только после явного согласия. Окно «Дыма и света»:
        /// клуб глубокого дыма с нитью света (UiInkKit.Plate), заголовок, разделитель с огоньком,
        /// кнопки-мазки. Проявляется от центра быстро и без огня по краю (как окна паузы).
        /// </summary>
        static void BuildConfirm(RectTransform root, MainMenuPanel panel)
        {
            var center = new Vector2(.5f, .5f);
            RectTransform shade = Stretch(Node("Подтверждение", root));
            panel.Confirm = shade.gameObject.AddComponent<CanvasGroup>();
            Image veil = Layer(shade, "Вуаль", T.Pixel, Role.Veil, .9f);
            veil.raycastTarget = true;

            RectTransform card = Box(Node("Карточка", shade), center, center, Vector2.zero, new Vector2(660f, 300f));
            UiInkGroup appear = UiInkKit.Group(card, UiInkGroup.Sweep.FromCenter, .4f, .14f);
            appear.Burn = 0f;
            UiInkKit.Plate(card);

            RectTransform title = Box(Node("Заголовок", card), new Vector2(.5f, 1f), center, new Vector2(0f, -52f), new Vector2(600f, 50f));
            TMP_Text titleLabel = UiInkKit.Label(title, "Надпись", "Начать новую игру?", FontRole.Heading, 32f, Role.Text, TextAlignmentOptions.Center, 1f, 1f, .05f);
            titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform divider = UiInkKit.Divider(card, "Разделитель", 380f);
            Box(divider, new Vector2(.5f, 1f), center, new Vector2(0f, -92f), new Vector2(380f, 16f));
            RectTransform text = Box(Node("Пояснение", card), new Vector2(.5f, 1f), center, new Vector2(0f, -146f), new Vector2(560f, 76f));
            TMP_Text body = UiInkKit.Label(text, "Надпись", "Лагерь начнётся заново: уровень Пелага, вещи, деньги и заказы жителей будут стёрты.",
                FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .15f);
            body.textWrappingMode = TextWrappingModes.Normal;

            var buttonSize = new Vector2(220f, 52f);
            RectTransform yes = UiInkKit.Button(card, "Начать заново", "Начать заново", true, buttonSize, 22f);
            Box(yes, new Vector2(.5f, 0f), center, new Vector2(-142f, 46f), buttonSize);
            panel.ConfirmYes = yes.GetComponent<Button>();
            RectTransform no = UiInkKit.Button(card, "Отмена", "Отмена", false, buttonSize, 22f);
            Box(no, new Vector2(.5f, 0f), center, new Vector2(142f, 46f), buttonSize);
            panel.ConfirmNo = no.GetComponent<Button>();
            shade.gameObject.SetActive(false);
        }

        static RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Кадр меню без запуска игры; confirm — показать подтверждение новой игры.</summary>
        public static string Capture(string outPath, bool confirm = false)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var panel = inst.GetComponent<MainMenuPanel>();
                panel.SetContinueLine("Пелаг · уровень 5");
                if (confirm)
                {
                    panel.Confirm.gameObject.SetActive(true);
                    panel.Confirm.alpha = 1f;
                }
                foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                    if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
            });
        }
    }
}
