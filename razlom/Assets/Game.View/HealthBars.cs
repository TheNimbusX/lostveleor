using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Полоски здоровья над врагами.
    ///
    /// ПОЯВЛЯЮТСЯ ПО УДАРУ И ГАСНУТ САМИ. Постоянные полоски над сорока телами
    /// превратили бы кадр в диаграмму и залезли бы в середину экрана, которая
    /// в этой игре свободна всегда. Полоска нужна ровно тогда, когда игрок
    /// начал кого-то бить и хочет понять, добьёт он его или нет.
    ///
    /// Рисуется из пула, всегда лицом к камере. Вид — материал «Дым и свет», как
    /// полоса героя в боевом HUD (владелец 26 сентября: «перевести вообще всё»):
    /// тёмная дымная дорожка, заливка — мазок кистью цвета здоровья, обрезанный по
    /// доле (мазок не сжимается), без серебряного контура; у элиты на конце заливки —
    /// светящийся огонёк-круг вместо ромба. Шейдер «Дыма и света» в мире не работает,
    /// поэтому дым и мазки заранее вырезаны из пака: Resources/UI/HUD/EnemyBar*.png
    /// (tools/ui-kit/make-enemy-bars.py). Нет их — прежний вид пака «Ночная акварель»
    /// из UiTheme (Resources), поэтому работает и в сборке.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(950)]
    public sealed class HealthBars : MonoBehaviour
    {
        [Header("Вид")]
        public float Width = 1.05f;
        public float Height = 0.13f;
        [Tooltip("Зазор заливки внутри дорожки, метры")]
        public float Inset = 0.022f;
        [Tooltip("Огонёк элиты, метры")]
        public float EliteGem = 0.2f;

        [Tooltip("На сколько метров полоска висит над центром тела.")]
        public float Height3D = 2.15f;
        public float RootSwarmHeight3D = 1.25f;

        [Tooltip("Дорожка: чернильный дым, как у полос HUD (роль Smoke).")]
        public Color BackColor = new Color32(0x12, 0x19, 0x23, 0xEB);
        public Color FillColor = new Color32(0xE0, 0x46, 0x34, 0xF2);
        [Tooltip("Заливка элиты; саму элиту выделяет огонёк на конце.")]
        public Color EliteColor = new Color32(0xF3, 0x4F, 0x37, 0xFF);
        [Tooltip("Серебряный контур прежнего вида (только без спрайтов «Дыма и света»).")]
        public Color FrameColor = new Color32(0xD8, 0xE1, 0xEE, 0x90);

        [Tooltip("Цель, которую бьют прямо сейчас, отмечается ярче.")]
        public Color FocusColor = new Color32(0xFF, 0x6A, 0x4A, 0xFF);

        [Tooltip("Огонёк элиты: тёплый свет (как вспышка готовности способности в HUD).")]
        public Color OrbColor = new Color(1f, .9f, .72f, 1f);
        [Tooltip("Сияние вокруг огонька; альфа — сила, свет дышит вокруг неё.")]
        public Color OrbGlowColor = new Color(1f, .5f, .2f, .7f);

        [Header("Время")]
        [Tooltip("Сколько секунд полоска висит после последнего попадания.")]
        public float ShowFor = 2.4f;

        [Tooltip("За сколько секунд до конца полоска начинает гаснуть.")]
        public float FadeFor = 0.5f;

        [Tooltip("Потолок одновременно видимых полосок.")]
        public int MaxBars = 24;

        // Спрайты «Дыма и света» и их раскладка (tools/ui-kit/make-enemy-bars.py, числа — оттуда):
        // плотная часть дорожки — середина 448×64 холста 512×128, у заливки плотная часть — 40 из
        // 64 по высоте (холст ложится на полную высоту полоски), огонёк — круг 44 из 64.
        private const string TrackPath = "UI/HUD/EnemyBarTrack";
        private const string FillPath = "UI/HUD/EnemyBarFill";
        private const string OrbPath = "UI/HUD/EnemyBarOrb";
        private const string GlowPath = "UI/HUD/EnemyBarGlow";
        private const float TrackSpanX = 512f / 448f, TrackSpanY = 128f / 64f;
        private const float OrbDisc = .6f, OrbSpan = OrbDisc * 64f / 44f, GlowSpan = 2f;
        // Ступеней обрезки мазка: доля здоровья выбирает готовый спрайт, в кадре ничего не создаётся.
        private const int FillSteps = 128;

        private static readonly Vector3 EliteScale = new Vector3(1.4f, 1.2f, 1f);

        private TickDriver _driver;
        private Transform _camera;

        private struct Bar
        {
            public Transform Root;
            public Transform Fill;
            public SpriteRenderer BackRenderer;
            public SpriteRenderer FillRenderer;
            public SpriteRenderer FrameRenderer;
            public Transform Gem;
            public SpriteRenderer GemFill;
            public SpriteRenderer GemRim;
        }

        private Bar[] _bars;
        private Sprite _quad;

        // «Дым и свет»: дорожка, ступени заливки (i — доля (i + 1) / FillSteps), огонёк и сияние.
        private bool _ink;
        private Sprite _track, _orb, _glow;
        private Sprite[] _fillSteps;

        // Когда по кому в последний раз попали. Индекс — сущность.
        private float[] _hitAt;
        private int _focus = -1;
        private bool _ready;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void Build()
        {
            _ready = true;
            _camera = Camera.main != null ? Camera.main.transform : null;
            _quad = MakeQuadSprite();
            _ink = LoadInk();

            Transform root = new GameObject("Пул: полоски здоровья").transform;
            root.SetParent(transform, false);

            _hitAt = new float[TickDriver.MaxSimCapacity];
            for (int i = 0; i < _hitAt.Length; i++) _hitAt[i] = -999f;

            _bars = new Bar[Mathf.Max(1, MaxBars)];
            for (int i = 0; i < _bars.Length; i++) _bars[i] = MakeBar(root, i);
        }

        private void LateUpdate()
        {
            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                if (_ready) HideFrom(0);
                return;
            }

            if (!_ready) Build();
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;

            TrackHits();
            Draw(sim);
        }

        /// <summary>
        /// Отмечает, кого задели. Урон по времени тоже считается: горящий враг
        /// должен показывать, сколько ему осталось, — иначе непонятно, ждать
        /// его смерти или бить дальше.
        /// </summary>
        private void TrackHits()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];

                bool isDamage = e.Type == SimEventType.Damage
                                || e.Type == SimEventType.DamageOverTime;
                if (!isDamage) continue;
                if (e.Target == Simulation.PlayerId) continue;
                if ((uint)e.Target >= (uint)_hitAt.Length) continue;

                _hitAt[e.Target] = Time.unscaledTime;

                // Цель прямого удара игрока — та, за которой он следит.
                if (e.Type == SimEventType.Damage && e.Source == Simulation.PlayerId)
                    _focus = e.Target;
            }
        }

        private void Draw(Simulation sim)
        {
            EntityStore entities = sim.Entities;
            float now = Time.unscaledTime;
            int used = 0;
            float inner = Width - Inset * 2f;
            // Сияние огонька дышит, как свет «Дыма и света» в HUD (пульс шейдера ≈ 0,14).
            float breath = .86f + .14f * Mathf.Sin(now * 2.1f);

            for (int i = 0; i < entities.Count && used < _bars.Length; i++)
            {
                if (i == Simulation.PlayerId) continue;
                if (!entities.Alive[i]) continue;
                if ((uint)i >= (uint)_hitAt.Length) continue;

                float age = now - _hitAt[i];
                var dummy = CampTrainingView.Find(i);
                bool nearbyDummy = dummy != null && CampPlayerView.Instance != null
                    && CampTrainingView.IsNear(dummy, CampPlayerView.Instance.Position);
                bool elite = _driver.Run?.Encounters?.IsElite(i) == true;
                bool nearbyElite = elite && FixVec2.DistanceSq(entities.Position[i], entities.Position[Simulation.PlayerId]) < Fix64.FromInt(256);
                if (age > ShowFor && !nearbyElite && !nearbyDummy) continue;

                int max = entities.MaxHealth[i];
                if (max <= 0) continue;

                float fill = Mathf.Clamp01(entities.Health[i] / (float)max);

                // Полная полоска не показывается: если по врагу попали, но он
                // ещё цел, полоска всё равно нужна — она и говорит, что цел.
                float alpha = !nearbyElite && !nearbyDummy && age > ShowFor - FadeFor
                    ? Mathf.InverseLerp(ShowFor, ShowFor - FadeFor, age)
                    : 1f;

                Bar bar = _bars[used++];
                bar.Root.gameObject.SetActive(true);
                bar.Root.localScale = elite ? EliteScale : Vector3.one;

                Vector3 at = _driver.GetRenderPosition(i);
                float height = entities.Kind[i] == EnemyKind.ForestBud ? 1.6f : entities.Kind[i] == EnemyKind.ForestRootSwarm
                    ? RootSwarmHeight3D : Height3D;
                bar.Root.position = new Vector3(at.x, at.y + height, at.z);
                if (dummy != null) bar.Root.position = dummy.BarPosition;
                if (_camera != null) bar.Root.rotation = _camera.rotation;

                bar.BackRenderer.color = Faded(BackColor, alpha);
                if (bar.FrameRenderer != null) bar.FrameRenderer.color = Faded(FrameColor, alpha);
                bar.FillRenderer.color = Faded(elite ? EliteColor : i == _focus ? FocusColor : FillColor, alpha);
                bar.Fill.gameObject.SetActive(fill > .001f);

                // Где кончается заливка: там едет огонёк элиты.
                float end;
                if (_ink)
                {
                    // Мазок во всю длину, обрезанный справа по доле: готовая ступень, не сжатие.
                    int step = Mathf.Clamp(Mathf.CeilToInt(fill * FillSteps), 1, FillSteps);
                    Sprite sprite = _fillSteps[step - 1];
                    if (bar.FillRenderer.sprite != sprite) bar.FillRenderer.sprite = sprite;
                    end = -inner * .5f + inner * step / FillSteps;
                }
                else
                {
                    // Заливка — капсула от левого края дорожки. Короче своей высоты
                    // капсула сминается, поэтому ширина не меньше высоты.
                    float h = Height - Inset * 2f;
                    float w = Mathf.Max(h, inner * fill);
                    bar.FillRenderer.size = new Vector2(w, h);
                    bar.Fill.localPosition = new Vector3(-inner * .5f + w * .5f, 0f, -.001f);
                    end = -inner * .5f + w;
                }

                bar.Gem.gameObject.SetActive(elite && fill > .001f);
                if (elite)
                {
                    bar.Gem.localPosition = new Vector3(end, 0f, -.003f);
                    if (_ink)
                    {
                        bar.GemFill.color = Faded(OrbColor, alpha);
                        bar.GemRim.color = Faded(OrbGlowColor, alpha * breath);
                    }
                    else
                    {
                        bar.GemFill.color = Faded(Color.white, alpha);
                        bar.GemRim.color = Faded(EliteColor, alpha);
                    }
                }
            }

            HideFrom(used);
        }

        private void HideFrom(int from)
        {
            if (_bars == null) return;
            for (int i = from; i < _bars.Length; i++)
            {
                Transform root = _bars[i].Root;
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            }
        }

        private static Color Faded(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private SpriteRenderer Part(Transform parent, string name, Sprite sprite, int order, bool sliced)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : _quad;
            renderer.sortingOrder = order;
            if (sliced && sprite != null) renderer.drawMode = SpriteDrawMode.Sliced;
            return renderer;
        }

        private Bar MakeBar(Transform root, int index)
        {
            var rootGo = new GameObject("Полоска " + index);
            rootGo.transform.SetParent(root, false);
            rootGo.SetActive(false);
            return _ink ? InkBar(rootGo.transform) : KitBar(rootGo.transform);
        }

        /// <summary>
        /// Мазки «Дыма и света» из Resources. Импорт текстур любой (спрайт или обычная текстура):
        /// спрайты собираются здесь, один раз. Нет хоть одной — false, рисуется прежний вид.
        /// </summary>
        private bool LoadInk()
        {
            var track = Resources.Load<Texture2D>(TrackPath);
            var fill = Resources.Load<Texture2D>(FillPath);
            var orb = Resources.Load<Texture2D>(OrbPath);
            var glow = Resources.Load<Texture2D>(GlowPath);
            if (track == null || fill == null || orb == null || glow == null) return false;

            _track = Whole(track);
            _orb = Whole(orb);
            _glow = Whole(glow);
            // Ступени заливки — один и тот же мазок, обрезанный справа; якорь — левый край.
            _fillSteps = new Sprite[FillSteps];
            for (int i = 0; i < FillSteps; i++)
            {
                float width = Mathf.Max(1f, Mathf.Round(fill.width * (i + 1) / (float)FillSteps));
                _fillSteps[i] = Sprite.Create(fill, new Rect(0f, 0f, width, fill.height), new Vector2(0f, .5f), 100f, 0, SpriteMeshType.FullRect);
                _fillSteps[i].name = "Мазок " + (i + 1);
            }
            return true;
        }

        private static Sprite Whole(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }

        /// <summary>Масштаб, при котором весь спрайт ложится в <paramref name="width"/>×<paramref name="height"/> метров.</summary>
        private static void Fit(SpriteRenderer renderer, float width, float height)
        {
            Vector2 size = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / Mathf.Max(.001f, size.x), height / Mathf.Max(.001f, size.y), 1f);
        }

        /// <summary>
        /// Полоска «Дыма и света»: плотная часть дымной дорожки — ровно Width×Height (ореол
        /// выходит за неё), мазок заливки — во всю внутреннюю длину и полную высоту (его плотная
        /// часть — как прежняя капсула), огонёк элиты — сияние и ядро.
        /// </summary>
        private Bar InkBar(Transform root)
        {
            float inner = Width - Inset * 2f;
            SpriteRenderer back = Part(root, "Дорожка", _track, 4000, false);
            Fit(back, Width * TrackSpanX, Height * TrackSpanY);

            // Масштаб — по полному мазку: короткая ступень той же высоты и плотности, просто обрезана.
            SpriteRenderer fill = Part(root, "Заливка", _fillSteps[FillSteps - 1], 4001, false);
            Fit(fill, inner, Height);
            fill.transform.localPosition = new Vector3(-inner * .5f, 0f, -.001f);

            var gem = new GameObject("Элита").transform;
            gem.SetParent(root, false);
            // Корень элиты растянут неровно (1,4 × 1,2): огонёк сжимается обратно в круг.
            gem.localScale = new Vector3(EliteScale.y / EliteScale.x, 1f, 1f);
            SpriteRenderer glow = Part(gem, "Сияние", _glow, 4003, false);
            Fit(glow, EliteGem * GlowSpan, EliteGem * GlowSpan);
            SpriteRenderer orb = Part(gem, "Огонёк", _orb, 4004, false);
            Fit(orb, EliteGem * OrbSpan, EliteGem * OrbSpan);
            gem.gameObject.SetActive(false);

            return new Bar
            {
                Root = root,
                Fill = fill.transform,
                BackRenderer = back,
                FillRenderer = fill,
                Gem = gem,
                GemFill = orb,
                GemRim = glow,
            };
        }

        /// <summary>
        /// Прежний вид пака «Ночная акварель» (спрайтов «Дыма и света» нет): капсула-дорожка,
        /// заливка капсулой, серебряный контур; у элиты — светлый ромб в оправе цвета здоровья.
        /// </summary>
        private Bar KitBar(Transform root)
        {
            UiTheme theme = UiTheme.Current;

            SpriteRenderer back = Part(root, "Дорожка", theme.BarFill, 4000, true);
            if (back.drawMode == SpriteDrawMode.Sliced) back.size = new Vector2(Width, Height);
            else back.transform.localScale = new Vector3(Width, Height, 1f);

            SpriteRenderer fill = Part(root, "Заливка", theme.BarFill, 4001, true);
            SpriteRenderer frame = Part(root, "Контур", theme.BarFrame, 4002, true);
            if (frame.drawMode == SpriteDrawMode.Sliced) frame.size = new Vector2(Width, Height);
            else frame.gameObject.SetActive(false);

            // Ромб элиты: светлая заливка в оправе цвета здоровья, едет за концом заливки.
            var gem = new GameObject("Элита").transform;
            gem.SetParent(root, false);
            SpriteRenderer gemFill = Part(gem, "Заливка", theme.DiamondFill, 4003, false);
            SpriteRenderer gemRim = Part(gem, "Оправа", theme.DiamondFrameSmall, 4004, false);
            foreach (SpriteRenderer part in new[] { gemFill, gemRim })
            {
                Vector2 size = part.sprite.bounds.size;
                part.transform.localScale = new Vector3(EliteGem / Mathf.Max(.001f, size.x), EliteGem / Mathf.Max(.001f, size.y), 1f);
            }
            gemFill.transform.localScale *= .82f;
            gem.gameObject.SetActive(false);

            return new Bar
            {
                Root = root,
                Fill = fill.transform,
                BackRenderer = back,
                FillRenderer = fill,
                FrameRenderer = frame,
                Gem = gem,
                GemFill = gemFill,
                GemRim = gemRim,
            };
        }

        /// <summary>Белый квадрат с якорем на ЛЕВОМ крае: заливка должна расти вправо.</summary>
        private static Sprite MakeQuadSprite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4);
        }
    }
}
