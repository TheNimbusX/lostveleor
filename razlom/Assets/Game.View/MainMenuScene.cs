using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Живая сцена главного меню: слои-спрайты и своя ортографическая камера.
    ///
    /// НЕ IMGUI. Первая версия рисовала слои через GUI.DrawTexture, а IMGUI
    /// округляет позиции до целого пикселя — ради чёткости текста и рамок.
    /// Медленный параллакс сдвигается на доли пикселя за кадр, и округление
    /// превращало его в рывки по пикселю: «дёрганый, прерывистый». Спрайты
    /// двигаются дробно, и GPU сглаживает движение честно.
    ///
    /// Один пиксель холста — одна единица мира (PPU = 1), поэтому все сдвиги
    /// считаются в тех же пикселях холста 1672×941, на котором нарисованы
    /// слои и размечен HUD-пак.
    /// </summary>
    internal sealed class MainMenuScene
    {
        public const float CanvasWidth = 1672f;
        public const float CanvasHeight = 941f;

        // Сдвиги — в пикселях холста для БЛИЖНЕГО слоя; дальние умножают их
        // на свою глубину.
        //
        // Слои крупнее холста на 5%: запас 2,5% на сторону — 42 px по ширине,
        // а ближний слой уходит максимум на ~34 с учётом дыхания. Проверено
        // офлайн-рендером в крайних положениях мыши: край не открывается.
        private const float Overscan = 1.05f;
        private const float PointerShiftX = 22f;
        private const float PointerShiftY = 12f;
        private const float IdleDrift = 4f;
        private const float BreathZoom = 0.010f;
        private const float CloudDrift = 14f;
        private const float CloudPeriod = 60f;
        private const float LogoBob = 3f;
        private const float LogoBobPeriod = 6f;
        private const float VersionFadeSeconds = 1.5f;
        private const int LeafCount = 7;

        /// <summary>
        /// Место логотипа в долях холста. Логотип пришёл слоем, растянутым на
        /// весь кадр; место и масштаб сняты с утверждённой 6.png.
        /// </summary>
        private static readonly Rect LogoRect = new Rect(0.28409f, 0.04919f, 0.47249f, 0.44839f);

        /// <summary>
        /// Сцена стоит далеко под миром. Игровая камера туда не смотрит, а
        /// камере меню не нужен отдельный слой рендера в настройках проекта.
        /// </summary>
        private static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);

        /// <summary>Выше игровой камеры: меню рисуется последним и кроет кадр.</summary>
        private const float CameraDepth = 100f;

        // Цвета неба со слоя L0_Sky: верх кадра, середина и полоса у горизонта.
        private static readonly Color SkyTop = new Color32(80, 168, 252, 255);
        private static readonly Color SkyMiddle = new Color32(151, 196, 246, 255);
        private static readonly Color SkyHorizon = new Color32(165, 205, 249, 255);

        private sealed class LayerView
        {
            public Transform Root;
            public SpriteRenderer Light;
            public SpriteRenderer Dark;
            public float Depth;
            public bool Clouds;
        }

        private struct Leaf
        {
            public int Sprite;
            /// <summary>Позиция в пикселях холста, в системе переднего плана.</summary>
            public Vector2 Position;
            public Vector2 Velocity;
            public float Angle, Spin, Phase, Age, Life;
        }

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Sprite> _sprites = new List<Sprite>();
        // Фиксированный сид: листопад одинаков от запуска к запуску.
        private readonly System.Random _rng = new System.Random(1672);
        private readonly Leaf[] _leaves = new Leaf[LeafCount];

        private GameObject _root;
        private Camera _camera;
        private LayerView[] _layers;
        private Transform _logo;
        private Sprite[] _leafSprites;
        private SpriteRenderer[] _leafRenderers;
        private Texture2D _backdrop;
        private float _versionBlend;
        private float _versionTarget;
        private int _order;

        private Shader _animatedShader;
        private readonly List<Material> _materials = new List<Material>();
        private static readonly int MenuTimeId = Shader.PropertyToID("_RazlomMenuTime");
        private static readonly int MaskId = Shader.PropertyToID("_MaskTex");
        private static readonly int SwayAmpId = Shader.PropertyToID("_SwayAmp");
        private static readonly int SwaySpeedId = Shader.PropertyToID("_SwaySpeed");
        private static readonly int SwayFreqId = Shader.PropertyToID("_SwayFreq");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
        private static readonly int FlowPeriodId = Shader.PropertyToID("_FlowPeriod");
        private static readonly int FlowGlintId = Shader.PropertyToID("_FlowGlint");

        /// <summary>
        /// Погасить сцену немедленно. Уничтожение объектов в Unity отложено до
        /// конца кадра, а камера меню не должна рисовать поверх игры ни одного
        /// лишнего кадра после PLAY — даже если дальше что-то упадёт.
        /// </summary>
        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>Тёмная версия замка включена или включается.</summary>
        public bool Dark => _versionTarget > 0.5f;

        /// <summary>
        /// Переход между версиями — наплыв за полторы секунды: замки разной
        /// формы, и мгновенная подмена читалась бы как ошибка загрузки.
        /// </summary>
        public void SetDark(bool dark) => _versionTarget = dark ? 1f : 0f;

        /// <summary>Имя корня сцены: по нему находится то, что утекло из прошлых сессий.</summary>
        public const string RootName = "Главное меню · сцена";

        /// <summary>
        /// Удалить корни сцены меню, оставшиеся от прошлых сессий.
        ///
        /// Первая версия ставила корню HideFlags.DontSave, а такой объект в
        /// редакторе ПЕРЕЖИВАЕТ выход из Play. После перекомпиляции во время
        /// игры ссылка на сцену терялась, и её камера с голубым фоном и
        /// глубиной 100 рисовала поверх игры во всех следующих запусках —
        /// «голубой экран после PLAY», которого не было в собранном плеере.
        /// </summary>
        public static void DestroyLeftovers()
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != RootName || go.transform.parent != null) continue;
                Debug.Log("[Разлом] Главное меню: удалена сцена, оставшаяся от прошлой сессии.");
                Object.Destroy(go);
            }
        }

        /// <summary>
        /// Собрать сцену. Null, если не хватает обязательного слоя: тогда меню
        /// показывает плоскую картинку, а не пустой экран.
        /// </summary>
        public static MainMenuScene TryCreate()
        {
            DestroyLeftovers();
            var scene = new MainMenuScene();
            if (scene.Build()) return scene;
            scene.Dispose();
            return null;
        }

        private bool Build()
        {
            Texture2D sky = Load("L0_Sky"), mountains = Load("L1_Mountains"),
                castle = Load("L2_Castle"), valley = Load("L3_Valley"),
                ruins = Load("L4_Ruins"), front = Load("L5_Foreground"), logo = Load("Logo");
            if (sky == null || mountains == null || castle == null || valley == null
                || ruins == null || front == null || logo == null)
                return false;

            // Необязательное: тёмные версии замка и долины и герой. Версии
            // переключаются вместе — F2 в редакторе меняет весь пейзаж сразу.
            Texture2D castleDark = Load("L2_Castle_Dark", optional: true);
            Texture2D valleyDark = Load("L3_Valley_Dark", optional: true);
            Texture2D hero = Load("L6_Hero", optional: true);

            // Обычный объект сцены, без HideFlags.DontSave: такой Unity сама
            // уничтожает при выходе из Play, и утечь ему некуда.
            _root = new GameObject(RootName);
            _root.transform.position = Origin;
            BuildCamera();

            _backdrop = BuildBackdrop();
            SpriteRenderer backdrop = AddSprite("Небо · подложка", _backdrop, _root.transform);
            // Подложка 1×64 растянута с запасом: камера видит максимум весь холст.
            backdrop.transform.localScale = new Vector3(CanvasWidth * 1.3f, CanvasHeight * 1.3f / _backdrop.height, 1f);

            _layers = new[]
            {
                AddLayer("Небо", sky, null, 0.05f, clouds: true),
                AddLayer("Горы", mountains, null, 0.15f),
                AddLayer("Замок", castle, castleDark, 0.30f),
                AddLayer("Долина", valley, valleyDark, 0.45f),
                AddLayer("Руины", ruins, null, 0.70f),
                AddLayer("Передний план", front, null, 1.00f),
            };

            // Герой стоит на камне переднего плана: та же глубина, поверх него.
            Transform frontRoot = _layers[_layers.Length - 1].Root;
            SpriteRenderer heroRenderer = hero != null ? AddSprite("Пелаг", hero, frontRoot) : null;

            BuildLeaves(frontRoot);

            // Логотип — часть интерфейса, а не сцены: за курсором не уходит.
            SpriteRenderer logoRenderer = AddSprite("Логотип", logo, _root.transform);
            _logo = logoRenderer.transform;
            _logo.localScale = new Vector3(
                CanvasWidth * LogoRect.width / logo.width,
                CanvasHeight * LogoRect.height / logo.height, 1f);

            // Живые детали. Амплитуды — в пикселях своей текстуры: логотип
            // нарисован вдвое крупнее, чем показан, поэтому его листьям нужно
            // чуть больше, чтобы сдвиг вообще был заметен на экране.
            _animatedShader = Shader.Find("Razlom/MenuSprite");
            AnimateWater(_layers[2].Light, "L2_Castle");
            AnimateWater(_layers[2].Dark, "L2_Castle_Dark");
            AnimateWater(_layers[3].Light, "L3_Valley");
            AnimateWater(_layers[3].Dark, "L3_Valley_Dark");
            AnimateSway(heroRenderer, "L6_Hero", amplitude: 6f, speed: 2.4f, frequency: 30f);
            AnimateSway(logoRenderer, "Logo", amplitude: 5f, speed: 1.6f, frequency: 22f);
            return true;
        }

        /// <summary>
        /// Колыхание по маске: волосы и хвосты ленты Пелага, листья логотипа.
        /// Узел ленты и кольцо логотипа стоят — маска гаснет к креплению.
        /// </summary>
        private void AnimateSway(SpriteRenderer renderer, string layer, float amplitude, float speed, float frequency)
        {
            Material material = Animated(renderer, layer);
            if (material == null) return;
            material.SetFloat(SwayAmpId, amplitude);
            material.SetFloat(SwaySpeedId, speed);
            material.SetFloat(SwayFreqId, frequency);
        }

        /// <summary>Течение воды по маске: струи водопадов у замка и в долине.</summary>
        private void AnimateWater(SpriteRenderer renderer, string layer)
        {
            Material material = Animated(renderer, layer);
            if (material == null) return;
            material.SetFloat(FlowSpeedId, 0.8f);
            material.SetFloat(FlowPeriodId, 14f);
            material.SetFloat(FlowGlintId, 0.3f);
        }

        /// <summary>
        /// Свой материал слою, у которого есть маска. Нет маски или шейдера —
        /// слой остаётся обычным спрайтом: живость — украшение, а не условие
        /// того, что меню вообще покажется.
        /// </summary>
        private Material Animated(SpriteRenderer renderer, string layer)
        {
            if (renderer == null || _animatedShader == null) return null;
            Texture2D mask = Load(layer + "_Mask", optional: true);
            if (mask == null) return null;
            var material = new Material(_animatedShader);
            material.SetTexture(MaskId, mask);
            renderer.sharedMaterial = material;
            _materials.Add(material);
            return material;
        }

        private void BuildCamera()
        {
            var cameraObject = new GameObject("Камера меню");
            cameraObject.transform.SetParent(_root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = SkyHorizon;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            _camera.depth = CameraDepth;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
        }

        private LayerView AddLayer(string name, Texture2D texture, Texture2D dark, float depth, bool clouds = false)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_root.transform, false);
            var layer = new LayerView
            {
                Root = root,
                Light = AddSprite(name, texture, root),
                Depth = depth,
                Clouds = clouds,
            };
            if (dark != null)
            {
                layer.Dark = AddSprite(name + " · тёмная версия", dark, root);
                layer.Dark.color = new Color(1f, 1f, 1f, 0f);
            }
            return layer;
        }

        private void BuildLeaves(Transform parent)
        {
            var sprites = new List<Sprite>();
            for (int i = 0; i < 5; i++)
            {
                Texture2D leaf = Load("Leaf_" + i, optional: true);
                if (leaf != null) sprites.Add(MakeSprite(leaf));
            }
            _leafSprites = sprites.ToArray();
            if (_leafSprites.Length == 0) return;

            _leafRenderers = new SpriteRenderer[LeafCount];
            for (int i = 0; i < LeafCount; i++)
            {
                var go = new GameObject("Лист " + i);
                go.transform.SetParent(parent, false);
                _leafRenderers[i] = go.AddComponent<SpriteRenderer>();
                _leafRenderers[i].sortingOrder = _order++;
                SpawnLeaf(ref _leaves[i], anywhere: true);
                _leafRenderers[i].sprite = _leafSprites[_leaves[i].Sprite];
            }
        }

        // ---- Кадр ----

        /// <summary>
        /// Шаг сцены. <paramref name="pointer"/> — уже сглаженный курсор в
        /// долях экрана от −1 до 1, ось Y вниз, как на экране.
        /// </summary>
        public void Tick(Vector2 pointer, float dt, float t)
        {
            if (_root == null) return;
            FitCamera();

            // Шейдер живых деталей идёт по этому времени, а не по _Time:
            // встроенное стоит, пока timeScale на экране меню равен нулю.
            Shader.SetGlobalFloat(MenuTimeId, t);

            _versionBlend = Mathf.MoveTowards(_versionBlend, _versionTarget, dt / VersionFadeSeconds);
            float breath = 0.5f + 0.5f * Mathf.Sin(t * 0.21f);

            foreach (LayerView layer in _layers)
            {
                float d = layer.Depth;
                float zoom = Overscan * (1f + BreathZoom * d * breath);
                float clouds = layer.Clouds ? Mathf.Sin(t * Mathf.PI * 2f / CloudPeriod) * CloudDrift : 0f;
                float dx = (-pointer.x * PointerShiftX + Mathf.Sin(t * 0.13f + d * 2.1f) * IdleDrift) * d + clouds;
                float dy = (-pointer.y * PointerShiftY + Mathf.Cos(t * 0.11f + d * 3.3f) * IdleDrift) * d;
                // Экранная ось Y вниз, мировая — вверх.
                layer.Root.localPosition = new Vector3(dx, -dy, 0f);
                layer.Root.localScale = new Vector3(zoom, zoom, 1f);

                if (layer.Dark != null)
                {
                    layer.Light.color = new Color(1f, 1f, 1f, 1f - _versionBlend);
                    layer.Dark.color = new Color(1f, 1f, 1f, _versionBlend);
                }
            }

            float bob = Mathf.Sin(t * Mathf.PI * 2f / LogoBobPeriod) * LogoBob;
            _logo.localPosition = new Vector3(
                (LogoRect.center.x - 0.5f) * CanvasWidth,
                -(LogoRect.center.y - 0.5f) * CanvasHeight + bob, 0f);

            TickLeaves(dt, t);
        }

        /// <summary>
        /// Холст кроет экран целиком, лишнее уходит за края — ровно так же
        /// кнопки размечены в MainMenuView, поэтому они совпадают с артом.
        /// </summary>
        private void FitCamera()
        {
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            _camera.orthographicSize = aspect >= CanvasWidth / CanvasHeight
                ? CanvasWidth / aspect * 0.5f
                : CanvasHeight * 0.5f;
        }

        // ---- Листопад ----

        /// <summary>
        /// Лист с кроны. Пять спрайтов вырезаны из самого переднего плана —
        /// это те листья, что художник нарисовал летящими; их неподвижные
        /// копии с переднего плана стёрты, иначе висели бы рядом с падающими.
        /// </summary>
        private void SpawnLeaf(ref Leaf leaf, bool anywhere)
        {
            leaf.Sprite = _rng.Next(_leafSprites.Length);
            leaf.Position = new Vector2(Range(60f, 720f), Range(-20f, 120f));
            leaf.Velocity = new Vector2(Range(10f, 26f), Range(20f, 34f));
            leaf.Angle = Range(0f, 360f);
            leaf.Spin = Range(-50f, 50f);
            leaf.Phase = Range(0f, Mathf.PI * 2f);
            leaf.Life = Range(14f, 22f);
            leaf.Age = 0f;

            // На старте листья уже в полёте, а не вылетают все разом из кроны.
            if (anywhere)
            {
                float head = Range(0f, leaf.Life * 0.8f);
                leaf.Age = head;
                leaf.Position += leaf.Velocity * head;
            }
        }

        private void TickLeaves(float dt, float t)
        {
            if (_leafRenderers == null) return;
            for (int i = 0; i < _leaves.Length; i++)
            {
                ref Leaf leaf = ref _leaves[i];
                leaf.Age += dt;
                leaf.Position += leaf.Velocity * dt;
                leaf.Angle += leaf.Spin * dt;
                SpriteRenderer renderer = _leafRenderers[i];
                if (leaf.Age >= leaf.Life || leaf.Position.y > 720f || leaf.Position.x > 1500f)
                {
                    SpawnLeaf(ref leaf, anywhere: false);
                    renderer.sprite = _leafSprites[leaf.Sprite];
                }

                float alpha = Mathf.Clamp01(leaf.Age)
                              * Mathf.Clamp01((leaf.Life - leaf.Age) / 1.5f)
                              * Mathf.Clamp01(1f - (leaf.Position.y - 620f) / 100f);

                // Покачивание в сторону и переворот через ребро — лист в
                // воздухе крутится, а не скользит плоской картинкой.
                float x = leaf.Position.x + Mathf.Sin(t * 1.3f + leaf.Phase) * 12f;
                float flip = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * 2f + leaf.Phase));
                Transform tr = renderer.transform;
                tr.localPosition = new Vector3(x - CanvasWidth * 0.5f, -(leaf.Position.y - CanvasHeight * 0.5f), 0f);
                tr.localRotation = Quaternion.Euler(0f, 0f, -leaf.Angle);
                tr.localScale = new Vector3(flip, 1f, 1f);
                renderer.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        // ---- Ресурсы ----

        private SpriteRenderer AddSprite(string name, Texture2D texture, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = MakeSprite(texture);
            renderer.sortingOrder = _order++;
            return renderer;
        }

        /// <summary>
        /// FullRect, а не Tight: плотный контур для кадра 1672×941 строился бы
        /// сотни миллисекунд на слой прямо при старте игры.
        /// </summary>
        private Sprite MakeSprite(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            _sprites.Add(sprite);
            return sprite;
        }

        private Texture2D Load(string name, bool optional = false)
        {
            Texture2D texture = Resources.Load<Texture2D>("UI/MainMenu/Layers/" + name);
            if (texture != null) _textures.Add(texture);
            else if (!optional) Debug.LogWarning($"[Разлом] Главное меню: нет слоя {name}.");
            return texture;
        }

        /// <summary>
        /// Вертикальный градиент неба. Закрывает места, где небо уходит в
        /// прозрачность, — проём арки левых руин и полосу у правого края.
        /// </summary>
        private static Texture2D BuildBackdrop()
        {
            const int height = 64;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < height; y++)
            {
                // Строка 0 текстуры — низ; t = 0 — верх кадра.
                float t = 1f - y / (float)(height - 1);
                Color color = t < 0.27f
                    ? Color.Lerp(SkyTop, SkyMiddle, t / 0.27f)
                    : Color.Lerp(SkyMiddle, SkyHorizon, Mathf.Clamp01((t - 0.27f) / 0.23f));
                texture.SetPixel(0, y, color);
            }
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Меню показывается один раз за запуск, а слои — десятки мегабайт
        /// несжатых текстур. Держать их весь забег незачем.
        /// </summary>
        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            foreach (Sprite sprite in _sprites) if (sprite != null) Object.Destroy(sprite);
            _sprites.Clear();
            foreach (Material material in _materials) if (material != null) Object.Destroy(material);
            _materials.Clear();
            foreach (Texture2D texture in _textures) if (texture != null) Resources.UnloadAsset(texture);
            _textures.Clear();
            if (_backdrop != null) Object.Destroy(_backdrop);
            _backdrop = null;
        }
    }
}
