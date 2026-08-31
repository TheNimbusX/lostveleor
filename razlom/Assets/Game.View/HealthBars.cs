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
    /// Рисуется квадами из пула, всегда лицом к камере. Ни одного файла.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(950)]
    public sealed class HealthBars : MonoBehaviour
    {
        [Header("Вид")]
        public float Width = 1.05f;
        public float Height = 0.11f;

        [Tooltip("На сколько метров полоска висит над центром тела.")]
        public float Height3D = 2.15f;

        public Color BackColor = new Color(0.05f, 0.05f, 0.07f, 0.80f);
        public Color FillColor = new Color(0.86f, 0.24f, 0.22f, 0.95f);

        [Tooltip("Цель, которую бьют прямо сейчас, отмечается ярче.")]
        public Color FocusColor = new Color(1.00f, 0.62f, 0.18f, 0.98f);

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
                if (age > ShowFor) continue;

                int max = entities.MaxHealth[i];
                if (max <= 0) continue;

                float fill = Mathf.Clamp01(entities.Health[i] / (float)max);

                // Полная полоска не показывается: если по врагу попали, но он
                // ещё цел, полоска всё равно нужна — она и говорит, что цел.
                float alpha = age > ShowFor - FadeFor
                    ? Mathf.InverseLerp(ShowFor, ShowFor - FadeFor, age)
                    : 1f;

                Bar bar = _bars[used++];
                bar.Root.gameObject.SetActive(true);

                Vector3 at = _driver.GetRenderPosition(i);
                bar.Root.position = new Vector3(at.x, at.y + Height3D, at.z);
                if (_camera != null) bar.Root.rotation = _camera.rotation;

                Color back = BackColor;
                back.a *= alpha;
                bar.BackRenderer.color = back;

                Color front = i == _focus ? FocusColor : FillColor;
                front.a *= alpha;
                bar.FillRenderer.color = front;

                // Заливка растёт от левого края: якорь спрайта стоит слева,
                // поэтому достаточно масштаба по X.
                bar.Fill.localScale = new Vector3(Width * fill, Height, 1f);
                bar.Fill.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);
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

        private Bar MakeBar(Transform root, int index)
        {
            var rootGo = new GameObject("Полоска " + index);
            rootGo.transform.SetParent(root, false);
            rootGo.SetActive(false);

            var backGo = new GameObject("Фон");
            backGo.transform.SetParent(rootGo.transform, false);
            SpriteRenderer back = backGo.AddComponent<SpriteRenderer>();
            back.sprite = _quad;
            back.sortingOrder = 4000;
            backGo.transform.localScale = new Vector3(Width, Height, 1f);
            backGo.transform.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);

            var fillGo = new GameObject("Заливка");
            fillGo.transform.SetParent(rootGo.transform, false);
            SpriteRenderer fill = fillGo.AddComponent<SpriteRenderer>();
            fill.sprite = _quad;
            fill.sortingOrder = 4001;

            return new Bar
            {
                Root = rootGo.transform,
                Fill = fillGo.transform,
                BackRenderer = back,
                FillRenderer = fill,
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
