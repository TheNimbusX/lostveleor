using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Детали материала «Дым и свет» (выбор владельца 25 сентября 2026, концепт
    /// `ART/UI/concepts-2026-09-25-material/3-hud-C-smoke.png`): чернильный дым вместо плашек,
    /// мазки кистью вместо полос, огненные кольца и нити света вместо серебряных рамок.
    ///
    /// Спрайты — Assets/UI/Kit/Smoke (режет tools/ui-kit/cut-smoke-kit.py из листов
    /// ART/UI/smoke-kit-2026-09-25). Дым и мазки — белые маски, цвет даёт тема; свет — оранжевый
    /// на чёрном, прибавляется к миру. Шейдер — Razlom/UI Ink, материалы лежат рядом с ним.
    ///
    /// Всё, что строится здесь, получает <see cref="UiInkReveal"/>: появлением ведёт
    /// <see cref="UiInkGroup"/> на ближайшем родителе («всё появляется анимированно»).
    /// </summary>
    public static class UiInkKit
    {
        public const string Folder = UiKitImport.KitRoot + "/Smoke";
        /// <summary>Спрайт мягкой формы карты (маска материала <see cref="Map"/>).</summary>
        public const string MapShape = "map_shape";
        const string ShaderFolder = "Assets/UI/Shaders";
        const string NoisePath = ShaderFolder + "/ink_noise.png";

        public static Sprite Sprite(string name)
        {
            string path = Folder + "/" + name + ".png";
            UiKitImport.Ensure(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogError("Нет спрайта «Дыма и света»: " + path);
            return sprite;
        }

        // ---------------------------------------------------------------- материалы

        /// <summary>Дым: течёт заметно, плотность дышит.</summary>
        public static Material Smoke => Material("UiInkSmoke", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", .018f);
            m.SetFloat("_FlowSpeed", .035f);
            m.SetFloat("_Breath", .35f);
        });

        /// <summary>
        /// Глубокий дым — для окон поверх затемнённого мира (итоги, пауза): почти чёрный, светлые края
        /// едва заметны. Обычный сланцевый дым на тёмной вуали читался серым облаком светлее фона.
        /// </summary>
        public static Material SmokeDeep => Material("UiInkSmokeDeep", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", .018f);
            m.SetFloat("_FlowSpeed", .03f);
            // Дыхание слабее обычного: под текстом дым не должен «дырявиться».
            m.SetFloat("_Breath", .12f);
            m.SetColor("_Wisp", new Color(.15f, .18f, .23f, 1f));
            m.SetFloat("_WispAmount", .35f);
        });

        /// <summary>Мазок кистью (полосы): почти не течёт — полоса не должна «плыть».</summary>
        public static Material Stroke => Material("UiInkStroke", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", .004f);
            m.SetFloat("_FlowSpeed", .02f);
            m.SetFloat("_Breath", .08f);
        });

        /// <summary>Свет: прибавляется к миру, дышит пульсом, по нитям бежит ток.</summary>
        public static Material Light => Material("UiInkLight", m =>
        {
            m.SetFloat("_Light", 1f);
            m.SetFloat("_FlowAmount", .006f);
            m.SetFloat("_FlowSpeed", .05f);
            m.SetFloat("_Breath", .25f);
            m.SetFloat("_Pulse", .14f);
            m.SetFloat("_PulseSpeed", 2.1f);
            m.SetFloat("_Shimmer", .8f);
        });

        /// <summary>Трещина меню: свет дрожит сильнее, ток бежит вдоль неё сверху вниз.</summary>
        public static Material Crack => Material("UiInkCrack", m =>
        {
            m.SetFloat("_Light", 1f);
            m.SetFloat("_FlowAmount", .004f);
            m.SetFloat("_FlowSpeed", .06f);
            m.SetFloat("_Breath", .3f);
            m.SetFloat("_Pulse", .1f);
            m.SetFloat("_PulseSpeed", 1.6f);
            m.SetFloat("_Shimmer", 1.1f);
            m.SetVector("_ShimmerAxis", new Vector4(0f, 1f, 0f, 0f));
            m.SetFloat("_EdgeWidth", .1f);
            m.SetFloat("_EdgeGlow", 2.4f);
        });

        /// <summary>Картинка без течения (лого): только проявление с тлеющей кромкой.</summary>
        public static Material Plain => Material("UiInkPlain", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", 0f);
            m.SetFloat("_Breath", 0f);
            m.SetFloat("_EdgeWidth", .05f);
            m.SetFloat("_EdgeGlow", 2f);
        });

        /// <summary>
        /// Рисунок вещи в ячейке: как <see cref="Plain"/>, но без светлых завихрений дыма — у Plain они
        /// бродят по картинке голубоватой дымкой, на цветном рисунке вещи это заметно. Только
        /// проявление с тлеющей кромкой.
        /// </summary>
        public static Material Art => Material("UiInkArt", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", 0f);
            m.SetFloat("_Breath", 0f);
            m.SetFloat("_WispAmount", 0f);
            m.SetFloat("_EdgeWidth", .05f);
            m.SetFloat("_EdgeGlow", 2f);
        });

        /// <summary>
        /// Карта: рисунок карты, края тают в дым по мягкой форме клуба.
        ///
        /// Форма — <see cref="MapShape"/> (512², белая маска, повтор Clamp), рисует её
        /// tools/ui-kit/cut-smoke-kit.py (save_map_shape). Владелец 26 сентября: карта — округлый
        /// клуб, а не квадрат с рваными краями; форма перерисовывается скриптом (суперэллипс
        /// ≈2,4, перо шире), код здесь не меняется — материал берёт текстуру спрайта при каждой
        /// сборке. Маска накладывается по uv2 элемента (UiInkReveal), так что у RawImage карты
        /// должен быть UiInkReveal (<see cref="Inked"/>).
        /// </summary>
        public static Material Map => Material("UiInkMap", m =>
        {
            m.SetFloat("_Light", 0f);
            m.SetFloat("_FlowAmount", 0f);
            m.SetFloat("_Breath", 0f);
            // Своя форма карты (tools/ui-kit, map_shape): мягкий округлый клуб, края тают.
            m.SetTexture("_ShapeTex", Sprite(MapShape).texture);
            m.SetFloat("_UseShape", 1f);
            m.EnableKeyword("INK_SHAPE");
        });

        /// <summary>
        /// Материал по имени; числа ставятся из кода при каждой сборке — ручная правка материала
        /// живёт до следующей пересборки UI (подбирать в инспекторе, переносить сюда).
        /// </summary>
        static Material Material(string name, System.Action<Material> setup)
        {
            string path = ShaderFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created)
            {
                var shader = Shader.Find("Razlom/UI Ink");
                if (shader == null) { Debug.LogError("Нет шейдера Razlom/UI Ink"); return null; }
                mat = new Material(shader) { name = name };
            }
            mat.SetTexture("_NoiseTex", Noise());
            mat.SetColor("_EdgeColor", new Color(1f, .46f, .16f, 1f));
            mat.SetFloat("_EdgeWidth", .06f);
            mat.SetFloat("_EdgeGlow", 1.6f);
            mat.SetColor("_Wisp", new Color(.2f, .25f, .31f, 1f));
            mat.SetFloat("_WispAmount", 1f);
            setup(mat);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Шум шейдера: повтор по краям, линейный цвет (это данные, не картинка).</summary>
        static Texture2D Noise()
        {
            var importer = AssetImporter.GetAtPath(NoisePath) as TextureImporter;
            if (importer != null && (importer.wrapMode != TextureWrapMode.Repeat || importer.sRGBTexture || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        }

        // ---------------------------------------------------------------- элементы

        static int _seed;

        /// <summary>Данные «Дыма и света» готовой картинке (карта, туман): без них шейдер не знает, где край.</summary>
        public static UiInkReveal Inked(Graphic graphic, Vector2? origin = null, float delay = 0f) =>
            Reveal(graphic, origin ?? new Vector2(.5f, .5f), delay);

        static UiInkReveal Reveal(Graphic graphic, Vector2 origin, float delay)
        {
            var reveal = graphic.gameObject.AddComponent<UiInkReveal>();
            // Зерно — по счётчику, а не случайно: пересборка префаба не должна менять вид.
            _seed = (_seed + 7) % 97;
            reveal.Seed = _seed * .137f;
            reveal.Origin = origin;
            reveal.Delay = delay;
            return reveal;
        }

        static Image Piece(RectTransform parent, string name, Sprite sprite, Material material, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = UiKitBuilder.At(UiKitBuilder.Node(name, parent), anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.material = material;
            return image;
        }

        /// <summary>Клуб дыма (подложка под текст и значки); цвет — роль темы (по умолчанию Smoke).</summary>
        public static Image SmokeAt(RectTransform parent, string name, string sprite, Vector2 anchor, Vector2 position, Vector2 size,
            float alpha = 1f, Role role = Role.Smoke, Vector2? origin = null, float delay = 0f, bool deep = false)
        {
            Image image = Piece(parent, name, Sprite(sprite), deep ? SmokeDeep : Smoke, anchor, position, size);
            UiKitBuilder.Tint(image, deep && role == Role.Smoke ? Role.SmokeDeep : role, alpha);
            Reveal(image, origin ?? new Vector2(.5f, .5f), delay);
            return image;
        }

        /// <summary>Дым на весь элемент, шире его на <paramref name="expand"/>.</summary>
        public static Image SmokeLayer(RectTransform parent, string name, string sprite, float alpha = 1f, float expandX = 0f, float expandY = 0f,
            Role role = Role.Smoke, Vector2? origin = null, float delay = 0f, bool deep = false)
        {
            RectTransform rect = UiKitBuilder.Node(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-expandX, -expandY);
            rect.offsetMax = new Vector2(expandX, expandY);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite(sprite);
            image.raycastTarget = false;
            image.material = deep ? SmokeDeep : Smoke;
            UiKitBuilder.Tint(image, deep && role == Role.Smoke ? Role.SmokeDeep : role, alpha);
            Reveal(image, origin ?? new Vector2(.5f, .5f), delay);
            return image;
        }

        /// <summary>Мазок кистью: дорожка полосы или заливка; цвет — роль темы.</summary>
        public static Image StrokeLayer(RectTransform parent, string name, string sprite, Role role, float alpha = 1f, float delay = 0f)
        {
            Image image = UiKitBuilder.Layer(parent, name, Sprite(sprite), role, alpha);
            image.type = Image.Type.Simple;
            image.material = Stroke;
            Reveal(image, new Vector2(0f, .5f), delay);
            return image;
        }

        /// <summary>Свет (кольцо, нить, ромб): прибавляется к миру, цвет спрайта родной.</summary>
        public static Image LightAt(RectTransform parent, string name, string sprite, Vector2 anchor, Vector2 position, Vector2 size,
            float strength = 1f, Vector2? origin = null, float delay = .18f)
        {
            Image image = Piece(parent, name, Sprite(sprite), Light, anchor, position, size);
            image.color = new Color(1f, 1f, 1f, strength);
            Reveal(image, origin ?? new Vector2(.5f, .5f), delay);
            return image;
        }

        /// <summary>Свет на весь элемент, шире его на <paramref name="expand"/>.</summary>
        public static Image LightLayer(RectTransform parent, string name, string sprite, float strength = 1f, float expand = 0f,
            Vector2? origin = null, float delay = .18f)
        {
            RectTransform rect = UiKitBuilder.Stretch(UiKitBuilder.Node(name, parent), -expand);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite(sprite);
            image.raycastTarget = false;
            image.material = Light;
            image.color = new Color(1f, 1f, 1f, strength);
            Reveal(image, origin ?? new Vector2(.5f, .5f), delay);
            return image;
        }

        /// <summary>Надпись, которая проявляется по буквам вслед за дымом.</summary>
        public static TMP_Text Label(RectTransform parent, string name, string text, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float spacing = 0f, float alpha = 1f, float delay = .12f)
        {
            TMP_Text label = UiKitBuilder.Label(parent, name, text, font, size, role, align, spacing, alpha);
            Revealed(label, delay);
            return label;
        }

        /// <summary>Готовой надписи — проявление по буквам.</summary>
        public static UiInkText Revealed(TMP_Text label, float delay = .12f)
        {
            var reveal = label.gameObject.GetComponent<UiInkText>();
            if (reveal == null) reveal = label.gameObject.AddComponent<UiInkText>();
            reveal.Delay = delay;
            return reveal;
        }

        /// <summary>Группа появления на узле: включение узла само запускает показ.</summary>
        public static UiInkGroup Group(RectTransform node, UiInkGroup.Sweep sweep = UiInkGroup.Sweep.LeftToRight, float duration = .55f, float stagger = .22f)
        {
            var group = node.GetComponent<UiInkGroup>();
            if (group == null) group = node.gameObject.AddComponent<UiInkGroup>();
            group.Direction = sweep;
            group.Duration = duration;
            group.Stagger = stagger;
            return group;
        }

        /// <summary>
        /// Кнопка «Дыма и света»: основная — оранжевый мазок кистью со светом поверх, вторичная —
        /// тёмный мазок с огненной нитью по низу. Надпись проявляется по буквам. Состояния — ThemeStates:
        /// у основной меняется краска мазка, у вторичной — цвет надписи. Мышь ловит прозрачный прямоугольник.
        /// </summary>
        public static RectTransform Button(RectTransform parent, string name, string text, bool primary, Vector2 size, float fontSize = 26f)
        {
            UiTheme t = UiTheme.Current;
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = size;
            var hit = root.gameObject.AddComponent<Image>();
            hit.sprite = t.Pixel;
            hit.color = new Color(1f, 1f, 1f, 0f);

            Image stroke = StrokeLayer(root, "Мазок", primary ? "brush_stroke_2" : "brush_stroke_1", primary ? Role.Accent : Role.Smoke, 1f);
            stroke.rectTransform.offsetMin = new Vector2(-26f, -12f);
            stroke.rectTransform.offsetMax = new Vector2(26f, 12f);
            if (primary)
            {
                Color glow = t.Accent;
                glow.a = .4f;
                LightLayer(root, "Свет", "brush_stroke_2", 1f, 14f, new Vector2(0f, .5f), .15f).color = glow;
            }
            else LightAt(root, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(size.x + 70f, 30f), .6f, delay: .2f);

            TMP_Text label = Label(root, "Надпись", text, FontRole.Heading, fontSize, primary ? Role.TextOnAccent : Role.Text, TextAlignmentOptions.Center, 1.5f, 1f, .15f);
            label.textWrappingMode = TextWrappingModes.NoWrap;

            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            var states = root.gameObject.AddComponent<ThemeStates>();
            if (primary)
            {
                states.Target = stroke.GetComponent<ThemeColor>();
                states.Label = label.GetComponent<ThemeColor>();
            }
            else
            {
                states.Target = label.GetComponent<ThemeColor>();
                states.Normal = Role.Text;
            }
            states.Hover = Role.AccentHover;
            states.Pressed = Role.AccentPressed;
            states.Disabled = Role.Disabled;
            states.Apply();
            root.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            return root;
        }

        // ---------------------------------------------------------------- детали окон
        // Окна лагеря и паузы переходят с серебряного пака на «Дым и свет» (владелец 26 сентября:
        // «перевести вообще всё на новую версию»). Детали строятся на месте, без префабов пака:
        // правка кода пака не обновляет уже сохранённые префабы. Имена частей — те, что ищут виды.
        // Всё проявляет UiInkGroup окна; частым всплывашкам группа ставит Burn = 0.

        /// <summary>
        /// Подложка окна или всплывашки с раскладкой: плотный клуб дыма шире рамки (края тают в мир),
        /// мягкое тёмное пятно без краёв под текстом и огненная нить по низу. Слои встают первыми
        /// детьми (под содержимым) и не участвуют в раскладке. Та же подложка, что у подсказок боевого
        /// HUD (CombatHudWcBuilder.Card).
        /// Мышь подложка не ловит: окну, которое не должно пропускать клик в мир, нужен <see cref="HitArea"/>.
        /// </summary>
        /// <param name="small">Малая всплывашка (подсказка зелья, подпись над NPC): дым ближе к краю.</param>
        /// <param name="deep">Глубокий, почти чёрный дым (подсказки, окна поверх затемнённого мира);
        /// false — сланцевый дым, как у полос HUD прямо над миром.</param>
        /// <returns>Нить по низу: у широкого окна её можно растянуть или убрать.</returns>
        public static Image Plate(RectTransform rect, bool small = false, bool deep = true)
        {
            // Под текстом — мягкое тёмное пятно без краёв (дым сам по себе рваный, трава просвечивала).
            // Плотная середина шире самого окна: у клуба дыма плотное только ядро.
            Image shade = SmokeLayer(rect, "Тень под текстом", "soft_blot", .92f, small ? 50f : 70f, small ? 30f : 44f, deep: deep);
            Image smoke = SmokeLayer(rect, "Дым", small ? "smoke_plate" : "smoke_blot_2", 1f, small ? 90f : 120f, small ? 50f : 80f, deep: deep);
            Image dense = SmokeLayer(rect, "Дым плотнее", "smoke_plate", 1f, small ? 60f : 90f, small ? 26f : 34f, deep: deep);
            // Нить не уже подсказки HUD и не короче трёх пятых окна: под широким окном короткая нить терялась.
            float width = Mathf.Max(small ? 300f : 420f, rect.rect.width * .6f);
            Image thread = LightAt(rect, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -4f), new Vector2(width, 34f), .45f,
                origin: new Vector2(.5f, .5f), delay: .2f);
            Image[] layers = { shade, smoke, dense, thread };
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i].transform.SetSiblingIndex(i);
                NoLayout(layers[i]);
            }
            return thread;
        }

        /// <summary>
        /// Клавиша «Дыма и света»: буква в клубе дыма с тонким кольцом. Одна буква — круг
        /// <paramref name="size"/>, длинная подпись (Space, ЛКМ, Shift) — капсула по ширине текста.
        /// Кольцо — капсула 9-slice с радиусом в полвысоты: круг, который вид растянет под длинную
        /// подпись после смены клавиши, становится капсулой, а не овалом. Ребёнок «Буква» — тот, что
        /// ищут виды: одна строка, размер сам уменьшается, если подпись не влезает.
        /// </summary>
        public static RectTransform Keycap(RectTransform parent, string name, string key, float size = 34f)
        {
            UiTheme t = UiTheme.Current;
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(size, size);
            SmokeLayer(root, "Дым", "smoke_blot_2", 1f, size * .35f, size * .3f);
            Image ring = UiKitBuilder.Layer(root, "Кольцо", t.PillFrame, Role.PanelLine, .4f);
            Capsule(ring, size);
            ring.material = Plain;
            Reveal(ring, new Vector2(.5f, .5f), .1f);

            TMP_Text label = Label(root, "Буква", key, FontRole.Body, size * .56f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .2f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // Ширина текста — до полей и автоподбора: так мерится сама подпись.
            float text = string.IsNullOrEmpty(key) ? 0f : label.GetPreferredValues(key).x;
            if (key != null && key.Length > 1) root.sizeDelta = new Vector2(Mathf.Max(size, text + size * .7f), size);
            // Поля — чтобы буквы не наезжали на кольцо и круглые концы капсулы.
            float pad = size * .12f;
            label.margin = new Vector4(pad, 0f, pad, 0f);
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(8f, size * .26f);
            label.fontSizeMax = size * .56f;
            return root;
        }

        /// <summary>
        /// Разделитель «Дыма и света»: нить света, у <paramref name="gem"/> — с огоньком-ромбом в центре
        /// (ромб остаётся только таким мелким светом, не рамкой). Узел высотой 16 — для раскладки;
        /// свет выше узла и тянется по его ширине, так что раскладка может менять ширину.
        /// </summary>
        public static RectTransform Divider(RectTransform parent, string name, float width = 400f, bool gem = true, float strength = .6f)
        {
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(width, 16f);
            LightStrip(root, "Нить", gem ? "light_thread_gem" : "light_thread", .5f, 0f, gem ? 44f : 34f, 0f, strength, .18f);
            return root;
        }

        /// <summary>
        /// Прозрачный ловец мыши на весь элемент. Картинки «Дыма и света» мышь не ловят, и без ловца
        /// клик по окну проваливается в мир (ходьба по лагерю). Ставится на сам узел, если на нём нет
        /// графики, иначе — первым ребёнком «Ловец» вне раскладки. Годится и в targetGraphic кнопки.
        /// </summary>
        public static Image HitArea(RectTransform rect)
        {
            Image hit;
            if (rect.GetComponent<Graphic>() == null) hit = rect.gameObject.AddComponent<Image>();
            else
            {
                RectTransform catcher = UiKitBuilder.Stretch(UiKitBuilder.Node("Ловец", rect));
                catcher.SetAsFirstSibling();
                hit = catcher.gameObject.AddComponent<Image>();
                NoLayout(hit);
            }
            hit.sprite = UiTheme.Current.Pixel;
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            return hit;
        }

        /// <summary>
        /// Кнопка «закрыть» «Дыма и света»: клуб дыма кругом, тонкое кольцо и крест из двух коротких
        /// прямых отрезков нити света. Наведение: кольцо разгорается акцентом (ThemeStates), за крестом —
        /// мягкое свечение (UiHoverMotion). Мышь ловит прозрачный прямоугольник на всём узле.
        /// </summary>
        public static RectTransform CloseButton(RectTransform parent, string name, float size = 44f)
        {
            UiTheme t = UiTheme.Current;
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(size, size);
            Image hit = HitArea(root);
            SmokeLayer(root, "Дым", "smoke_blot_2", 1f, size * .3f, size * .3f);
            // Свечение наведения: UiHoverMotion держит его прозрачным, пока мышь не над кнопкой.
            Image glow = LightAt(root, "Свечение", "light_glow", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * 1.4f, size * 1.4f), .45f, delay: .2f);
            Image ring = UiKitBuilder.Layer(root, "Кольцо", t.CircleFrame, Role.PanelLine, .5f);
            ring.material = Plain;
            Reveal(ring, new Vector2(.5f, .5f), .1f);
            // Середина нити почти прямая; края нити волнистые и в искрах — на кресте они читались каракулями.
            Texture thread = Sprite("light_thread").texture;
            for (int i = 0; i < 2; i++)
            {
                RectTransform arm = UiKitBuilder.At(UiKitBuilder.Node(i == 0 ? "Крест 1" : "Крест 2", root), new Vector2(.5f, .5f), Vector2.zero,
                    new Vector2(size * .56f, size * .24f));
                arm.localEulerAngles = new Vector3(0f, 0f, i == 0 ? 45f : -45f);
                var line = arm.gameObject.AddComponent<RawImage>();
                line.texture = thread;
                line.uvRect = new Rect(.36f, .3f, .28f, .32f);
                line.material = Light;
                line.color = new Color(1f, 1f, 1f, .95f);
                line.raycastTarget = false;
                Reveal(line, new Vector2(.5f, .5f), .18f);
            }

            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            var states = root.gameObject.AddComponent<ThemeStates>();
            states.Target = ring.GetComponent<ThemeColor>();
            states.Normal = Role.PanelLine;
            states.Hover = Role.Accent;
            states.Pressed = Role.AccentPressed;
            states.Disabled = Role.Disabled;
            states.Apply();
            var motion = root.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = glow;
            motion.HoverScale = 1.08f;
            return root;
        }

        /// <summary>
        /// Вкладка «Дыма и света»: подпись и нить света под выбранной. Части — те, что переключает
        /// CampShopView.SetTab: «Надпись» (ThemeColor: акцент у выбранной, текст у остальных) и
        /// «Подчёркивание» (включено только у выбранной). Кнопку и ловца мыши добавляет окно
        /// (как с вкладкой пака) — например <see cref="HitArea"/> и Button.
        /// </summary>
        public static RectTransform Tab(RectTransform parent, string name, string text, bool selected, float w = 150f, float h = 48f, float fontSize = 20f)
        {
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(w, h);
            TMP_Text label = Label(root, "Надпись", text, FontRole.Body, fontSize, selected ? Role.Accent : Role.Text, TextAlignmentOptions.Center,
                0f, selected ? 1f : .85f, .1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Image line = LightStrip(root, "Подчёркивание", "light_thread", 0f, 2f, 26f, 4f, .85f, .15f);
            line.gameObject.SetActive(selected);
            return root;
        }

        /// <summary>
        /// Строка списка «Дыма и света»: огонёк-ромб маркером и подпись; у выбранной — полоса дыма
        /// под строкой и нить света по низу. Части — те, что переключает CampShopView.SetRow:
        /// «Подложка» и «Рамка» (включены у выбранной), «Маркер» (ThemeColor: акцент у выбранной,
        /// приглушённый у остальных), «Надпись» (отступ слева 62). Кнопку и ловца мыши добавляет окно.
        /// </summary>
        public static RectTransform ListRow(RectTransform parent, string name, string text, bool selected, float w = 420f, float h = 52f)
        {
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(w, h);
            Image backing = SmokeLayer(root, "Подложка", "smoke_plate", .9f, 16f, 6f);
            Image frame = LightStrip(root, "Рамка", "light_thread", 0f, 0f, 26f, 8f, .5f, .15f);
            backing.gameObject.SetActive(selected);
            frame.gameObject.SetActive(selected);
            Image marker = LightAt(root, "Маркер", "light_gem", new Vector2(0f, .5f), new Vector2(34f, 0f), new Vector2(16f, 18f), 1f, delay: .15f);
            UiKitBuilder.Tint(marker, selected ? Role.Accent : Role.TextMuted);
            TMP_Text label = Label(root, "Надпись", text, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.MidlineLeft, 0f, 1f, .12f);
            label.rectTransform.offsetMin = new Vector2(62f, 0f);
            return root;
        }

        // ---------------------------------------------------------------- ячейки
        // Ячейки сумки, атласа и лавок, слоты зелий и надетого. Вид ведёт WcSlotState (у ячейки
        // вещи ещё WcRarity), как у ячейки пака, но без рисованных спрайтов: тонкая линия цвета
        // редкости, мягкий свет редкости за вещью, свет выбора и наведения. Имена «Предмет» и
        // «Рамка» — те же, что у ячейки пака: их ищут сборщики окон.
        // Всё, что выходит за ячейку, лежит под соседями (сетка — братья по порядку), поэтому дым
        // ячейки почти не шире её самой: иначе сосед справа закрывал бы край рамки слева.

        /// <summary>
        /// Ячейка вещи «Дыма и света» (сумка, атлас, лавки): мягкая дымная подложка, вещь и тонкая
        /// скруглённая рамка (концепт v3-tent: сетка скруглённых квадратов). Дети по порядку:
        /// «Ловец» — прозрачный ловец мыши (наведение WcSlotState; годится в targetGraphic кнопки,
        /// второй ловец не нужен); «Тень» и «Дым» — подложка; «Свет» — выбор и наведение
        /// (WcSlotState.Glow); «Фон редкости» — мягкий свет цвета редкости за вещью (WcSlotState.Fill);
        /// «Предмет» — RawImage вещи, выключен, пока окно не даст картинку (свою Image окно ставит
        /// на номер «Рамки» среди детей, как с ячейкой пака); «Рамка» — WcSlotState.Frame, у выбранной
        /// толще. На корне WcSlotState (пусто) и WcRarity: вещь ставится через них, как у ячейки пака.
        /// </summary>
        /// <param name="small">Малая ячейка (надетое у лавок, вещь в подсказке): дым мягкий, без рваных
        /// клубов, угол рамки круглее.</param>
        public static RectTransform Cell(RectTransform parent, string name, float size, bool small = false)
        {
            UiTheme t = UiTheme.Current;
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(size, size);
            CatchMouse(root);
            // Тёмная лунка чуть шире ячейки (сетке хватает промежутка) и дым внутри неё.
            float shade = size * (small ? .04f : .06f);
            SmokeLayer(root, "Тень", "soft_blot", .8f, shade, shade, deep: true);
            if (small) SmokeLayer(root, "Дым", "soft_blot", .9f);
            else SmokeLayer(root, "Дым", "smoke_plate", 1f, size * .06f, size * .12f);

            Image glow = SlotLight(root, "Свет", 0f, .2f);
            Image fill = SlotLight(root, "Фон редкости", -size * .06f, .12f);
            ItemArt(root, size * (small ? .12f : .14f));
            Image frame = SlotFrame(root, t.FrameSmall);
            Rounded(frame, size * (small ? .18f : .16f));

            InkSlotState(root, frame, fill, glow, t.FrameSmall, t.FrameBoldSmall, WcSlotState.Empty);
            root.gameObject.AddComponent<WcRarity>();
            return root;
        }

        /// <summary>
        /// Круглый слот «Дыма и света» — зелье, надетая вещь: клуб дыма (как у плиток способностей
        /// боевого HUD), тёмный диск, тонкое кольцо без огня (владелец 26 сентября: у зелий огненного
        /// кольца нет). Дети по порядку: «Ловец», «Дым», «Диск», «Сияние» (только с <paramref name="inner"/>:
        /// слабый свет цвета зелья внутри), «Свет» (выбор и наведение), «Фон редкости», «Предмет»
        /// (RawImage, выключен, пока нет картинки), «Рамка» (кольцо, у выбранной толще). На корне
        /// WcSlotState без редкости (Plain: тихое серебряное кольцо); надетой вещи окно ставит
        /// редкость через Set, как ячейке.
        /// </summary>
        /// <param name="inner">Цвет слабого сияния внутри (Health, Lavidium у зелий); null — без него.</param>
        public static RectTransform SlotOrb(RectTransform parent, string name, float size, Role? inner = null, float innerAlpha = .2f)
        {
            UiTheme t = UiTheme.Current;
            RectTransform root = UiKitBuilder.Node(name, parent);
            root.sizeDelta = new Vector2(size, size);
            CatchMouse(root);
            SmokeLayer(root, "Дым", "smoke_ring", 1f, size * .2f, size * .2f);
            Image disc = UiKitBuilder.Layer(root, "Диск", t.CircleFill, Role.Panel, .9f, -2f);
            disc.material = Plain;
            Reveal(disc, new Vector2(.5f, .5f), .05f);
            if (inner.HasValue)
            {
                Image glowInside = LightLayer(root, "Сияние", "soft_blot", 1f, -size * .1f, delay: .2f);
                UiKitBuilder.Tint(glowInside, inner.Value, innerAlpha);
            }

            Image glow = SlotLight(root, "Свет", size * .12f, .2f);
            Image fill = SlotLight(root, "Фон редкости", -size * .12f, .12f);
            ItemArt(root, size * .18f);
            Image frame = SlotFrame(root, t.CircleFrame);

            WcSlotState state = InkSlotState(root, frame, fill, glow, t.CircleFrame, t.CircleFrameBold, WcSlotState.Plain);
            state.PlainAlpha = .45f;
            state.Apply();
            return root;
        }

        /// <summary>Прозрачный ловец мыши первым ребёнком «Ловец», вне раскладки.</summary>
        static Image CatchMouse(RectTransform root)
        {
            Image hit = HitArea(UiKitBuilder.Stretch(UiKitBuilder.Node("Ловец", root)));
            NoLayout(hit);
            return hit;
        }

        /// <summary>
        /// Мягкий свет ячейки: белое пятно в материале света, цвет и силу ставит WcSlotState
        /// (пока нечему гореть — выключен). <paramref name="expand"/> меньше нуля — отступ внутрь.
        /// </summary>
        static Image SlotLight(RectTransform root, string name, float expand, float delay)
        {
            Image light = LightLayer(root, name, "soft_blot", 0f, expand, delay: delay);
            light.enabled = false;
            return light;
        }

        /// <summary>Рисунок вещи «Предмет»: без течения и дымки, выключен до первой картинки.</summary>
        static RawImage ItemArt(RectTransform root, float inset)
        {
            RectTransform rect = UiKitBuilder.Stretch(UiKitBuilder.Node("Предмет", root), inset);
            var art = rect.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.material = Art;
            art.enabled = false;
            Reveal(art, new Vector2(.5f, .5f), .12f);
            return art;
        }

        /// <summary>Рамка ячейки «Рамка»: тонкая линия без темы — цвет ставит WcSlotState.</summary>
        static Image SlotFrame(RectTransform root, Sprite sprite)
        {
            RectTransform rect = UiKitBuilder.Stretch(UiKitBuilder.Node("Рамка", root));
            var frame = rect.gameObject.AddComponent<Image>();
            frame.sprite = sprite;
            frame.type = Image.Type.Simple;
            frame.raycastTarget = false;
            frame.material = Plain;
            Reveal(frame, new Vector2(.5f, .5f), .1f);
            return frame;
        }

        /// <summary>
        /// Состояние ячейки «Дыма и света»: спрайт меняется только на толстую линию у выбранной
        /// (тихой рамки нет — рамка не выходит за ячейку), фон редкости — свет, а не краска.
        /// </summary>
        static WcSlotState InkSlotState(RectTransform root, Image frame, Image fill, Image glow, Sprite line, Sprite bold, int rarity)
        {
            var state = root.gameObject.AddComponent<WcSlotState>();
            state.Frame = frame;
            state.Fill = fill;
            state.Glow = glow;
            state.FrameSprite = line;
            state.SelectedSprite = bold;
            state.QuietSprite = null;
            state.GlowBleed = 0f;
            state.FillSprites = new Sprite[0];
            // Свет за вещью: у обычной едва заметен, у редких — ясным пятном цвета редкости.
            state.FillAlpha = new[] { .1f, .3f, .36f, .4f };
            state.HoverFill = .1f;
            state.Rarity = rarity;
            state.Apply();
            return state;
        }

        /// <summary>
        /// 9-slice рамка со скруглением по размеру ячейки: угловой кусок — <paramref name="corner"/>
        /// единиц холста (бордюр спрайта пересчитывается, как у <see cref="Capsule"/>). Толстая линия
        /// выбора с тем же бордюром ложится в те же углы.
        /// </summary>
        static void Rounded(Image image, float corner)
        {
            Sprite sprite = image.sprite;
            if (sprite == null || sprite.border == Vector4.zero) return;
            image.type = Image.Type.Sliced;
            float border = Mathf.Max(sprite.border.x, sprite.border.w) * 100f / sprite.pixelsPerUnit;
            image.pixelsPerUnitMultiplier = border / Mathf.Max(corner, 1f);
        }

        /// <summary>
        /// Полоса света по ширине узла: якорь по высоте <paramref name="anchorY"/> (0 — низ, .5 — середина),
        /// сдвиг <paramref name="y"/>, высота <paramref name="height"/>, отступ от краёв <paramref name="inset"/>.
        /// </summary>
        static Image LightStrip(RectTransform parent, string name, string sprite, float anchorY, float y, float height, float inset, float strength, float delay)
        {
            RectTransform rect = UiKitBuilder.Node(name, parent);
            rect.anchorMin = new Vector2(0f, anchorY);
            rect.anchorMax = new Vector2(1f, anchorY);
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(inset, y - height * .5f);
            rect.offsetMax = new Vector2(-inset, y + height * .5f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite(sprite);
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.material = Light;
            image.color = new Color(1f, 1f, 1f, strength);
            Reveal(image, new Vector2(.5f, .5f), delay);
            return image;
        }

        /// <summary>
        /// Капсула 9-slice с радиусом в полвысоты: квадратный узел — круг, шире — капсула с круглыми
        /// концами. Бордюр спрайта пересчитывается в единицы холста (опорные 100 пикселей на единицу).
        /// </summary>
        static void Capsule(Image image, float height)
        {
            Sprite sprite = image.sprite;
            if (sprite == null || sprite.border == Vector4.zero) return;
            image.type = Image.Type.Sliced;
            float border = Mathf.Max(sprite.border.y, sprite.border.w) * 100f / sprite.pixelsPerUnit;
            image.pixelsPerUnitMultiplier = border / Mathf.Max(height * .5f, 1f);
        }

        /// <summary>Слой вне раскладки: подложки и ловцы не должны сдвигать содержимое окна.</summary>
        static void NoLayout(Component part)
        {
            var element = part.GetComponent<LayoutElement>();
            if (element == null) element = part.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        /// <summary>Угли: редкие искры всплывают из нижней части прямоугольника.</summary>
        public static UiEmbers Embers(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float rate = 3f)
        {
            RectTransform rect = UiKitBuilder.At(UiKitBuilder.Node(name, parent), anchor, position, size);
            var embers = rect.gameObject.AddComponent<UiEmbers>();
            embers.Sprite = Sprite("ember_1");
            embers.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/Shaders/UiAdditive.mat");
            embers.raycastTarget = false;
            embers.Rate = rate;
            embers.color = new Color(1f, .9f, .8f, .9f);
            return embers;
        }
    }
}
