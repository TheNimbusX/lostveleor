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
    /// Рисуется из пула, всегда лицом к камере. Вид — пак «Ночная акварель»
    /// (23 сентября 2026, лист HUD): капсула-дорожка, заливка капсулой, серебряный
    /// контур; у элиты — светлый ромб в красной оправе на конце заливки. Спрайты
    /// берутся из UiTheme (Resources), поэтому работают и в сборке.
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
        [Tooltip("Ромб элиты, метры")]
        public float EliteGem = 0.2f;

        [Tooltip("На сколько метров полоска висит над центром тела.")]
        public float Height3D = 2.15f;
        public float RootSwarmHeight3D = 1.25f;

        public Color BackColor = new Color32(0x1B, 0x20, 0x29, 0xE0);
        public Color FillColor = new Color32(0xE0, 0x46, 0x34, 0xF2);
        [Tooltip("Заливка элиты; саму элиту выделяет ромб на конце.")]
        public Color EliteColor = new Color32(0xF3, 0x4F, 0x37, 0xFF);
        public Color FrameColor = new Color32(0xD8, 0xE1, 0xEE, 0x90);

        [Tooltip("Цель, которую бьют прямо сейчас, отмечается ярче.")]
        public Color FocusColor = new Color32(0xFF, 0x6A, 0x4A, 0xFF);

        [Header("Время")]
        [Tooltip("Сколько секунд полоска висит после последнего попадания.")]
        public float ShowFor = 2.4f;

        [Tooltip("За сколько секунд до конца полоска начинает гаснуть.")]
        public float FadeFor = 0.5f;

        [Tooltip("Потолок одновременно видимых полосок.")]
        public int MaxBars = 24;

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
                bar.Root.localScale = elite ? new Vector3(1.4f, 1.2f, 1f) : Vector3.one;

                Vector3 at = _driver.GetRenderPosition(i);
                float height = entities.Kind[i] == EnemyKind.ForestBud ? 1.6f : entities.Kind[i] == EnemyKind.ForestRootSwarm
                    ? RootSwarmHeight3D : Height3D;
                bar.Root.position = new Vector3(at.x, at.y + height, at.z);
                if (dummy != null) bar.Root.position = dummy.BarPosition;
                if (_camera != null) bar.Root.rotation = _camera.rotation;

                bar.BackRenderer.color = Faded(BackColor, alpha);
                bar.FrameRenderer.color = Faded(FrameColor, alpha);
                bar.FillRenderer.color = Faded(elite ? EliteColor : i == _focus ? FocusColor : FillColor, alpha);

                // Заливка — капсула от левого края дорожки. Короче своей высоты
                // капсула сминается, поэтому ширина не меньше высоты.
                float inner = Width - Inset * 2f, h = Height - Inset * 2f;
                float w = Mathf.Max(h, inner * fill);
                bar.Fill.gameObject.SetActive(fill > .001f);
                bar.FillRenderer.size = new Vector2(w, h);
                bar.Fill.localPosition = new Vector3(-inner * .5f + w * .5f, 0f, -.001f);

                bar.Gem.gameObject.SetActive(elite && fill > .001f);
                if (elite)
                {
                    bar.Gem.localPosition = new Vector3(-inner * .5f + w, 0f, -.003f);
                    bar.GemFill.color = Faded(Color.white, alpha);
                    bar.GemRim.color = Faded(EliteColor, alpha);
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
            UiTheme theme = UiTheme.Current;

            SpriteRenderer back = Part(rootGo.transform, "Дорожка", theme.BarFill, 4000, true);
            if (back.drawMode == SpriteDrawMode.Sliced) back.size = new Vector2(Width, Height);
            else back.transform.localScale = new Vector3(Width, Height, 1f);

            SpriteRenderer fill = Part(rootGo.transform, "Заливка", theme.BarFill, 4001, true);
            SpriteRenderer frame = Part(rootGo.transform, "Контур", theme.BarFrame, 4002, true);
            if (frame.drawMode == SpriteDrawMode.Sliced) frame.size = new Vector2(Width, Height);
            else frame.gameObject.SetActive(false);

            // Ромб элиты: светлая заливка в оправе цвета здоровья, едет за концом заливки.
            var gem = new GameObject("Элита").transform;
            gem.SetParent(rootGo.transform, false);
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
                Root = rootGo.transform,
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
