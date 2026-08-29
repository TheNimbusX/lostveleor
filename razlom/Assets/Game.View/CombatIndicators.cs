using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Опознаватели на полу: кто свой, кто чужой, куда я иду и кого сейчас бью.
    ///
    /// ЗАЧЕМ. В кадре до сорока тел, и разобрать в этой каше собственного
    /// персонажа, зону своего удара и текущую цель по одним только силуэтам
    /// нельзя — особенно пока арт прототипный. Кольцо под ногами читается
    /// мгновенно и не занимает центр экрана, а значит не спорит с правилом
    /// «середина экрана свободна всегда».
    ///
    /// Всё рисуется ПОД телами и плоско по земле. Ни одного файла: кольцо
    /// и сектор генерируются кодом, как и звук.
    ///
    /// Представление только читает симуляцию и ничего в ней не трогает.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(900)]
    public sealed class CombatIndicators : MonoBehaviour
    {
        [Header("Что показывать")]
        public bool ShowFootRings = true;
        public bool ShowAttackArc = true;
        public bool ShowMoveOrder = true;

        [Tooltip("Менять курсор на боевой, когда он над врагом.")]
        public bool ShowAttackCursor = true;

        [Header("Цвета")]
        [Tooltip("Свой. Бирюза народов Вола — тот же язык, что и в палитрах.")]
        public Color AllyColor = new Color(0.15f, 0.90f, 0.95f, 1.00f);

        public Color EnemyColor = new Color(0.95f, 0.25f, 0.22f, 0.85f);

        [Tooltip("Цель, которую персонаж бьёт прямо сейчас.")]
        public Color TargetColor = new Color(1.00f, 0.78f, 0.22f, 0.95f);

        [Tooltip("Тело под курсором. Наведение обязано быть видно ДО удара.")]
        public Color HoverColor = new Color(1.00f, 0.97f, 0.90f, 0.95f);

        [Tooltip("Сектор автоатаки. Виден, пока зажата кнопка удара.")]
        public Color ArcColor = new Color(1.00f, 0.72f, 0.30f, 0.16f);

        public Color OrderColor = new Color(0.55f, 0.90f, 1.00f, 0.70f);

        [Header("Размеры")]
        [Tooltip("Кольцо рисуется по НАСТОЯЩЕМУ радиусу тела из симуляции. " +
                 "Множитель только добавляет каёмку, чтобы обод не резался телом.")]
        public float RingScale = 2.15f;

        [Tooltip("Сколько секунд цель считается подсвеченной после удара по ней.")]
        public float TargetHighlight = 0.35f;

        [Tooltip("Потолок колец. Больше в кадре и не нужно: дальние всё равно не читаются.")]
        public int MaxRings = 96;

        private TickDriver _driver;

        private Transform _ringRoot;
        private SpriteRenderer[] _rings;
        private SpriteRenderer _arc;
        private SpriteRenderer _order;

        private Sprite _ringSprite;
        private Sprite _arcSprite;

        // Кого игрок ударил последним и когда. Подсветка живёт доли секунды:
        // постоянная метка цели превратилась бы в прицел, а прицела в этой
        // игре нет — цель выбирает симуляция.
        private int _lastTarget = -1;
        private float _lastTargetAt = -99f;

        private Texture2D _attackCursor;
        private bool _cursorIsAttack;

        private bool _ready;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void Build()
        {
            _ready = true;

            _ringSprite = MakeRingSprite(128, 0.66f);
            _arcSprite = MakeArcSprite(160, Simulation.AutoAttackArcCos.ToFloat());

            _ringRoot = new GameObject("Пул: опознаватели").transform;
            _ringRoot.SetParent(transform, false);

            _rings = new SpriteRenderer[Mathf.Max(1, MaxRings)];
            for (int i = 0; i < _rings.Length; i++)
                _rings[i] = MakeFlatSprite(_ringRoot, _ringSprite, "Кольцо " + i, -120);

            _arc = MakeFlatSprite(_ringRoot, _arcSprite, "Сектор удара", -140);
            _order = MakeFlatSprite(_ringRoot, _ringSprite, "Метка приказа", -130);

            _attackCursor = MakeAttackCursor(32);

            Debug.Log($"[Разлом] Опознаватели собраны: колец {_rings.Length}, " +
                      $"сектор {(_arcSprite != null ? "есть" : "НЕТ")}, курсор готов.");
        }

        private void LateUpdate()
        {
            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                if (_ready) HideAll();
                return;
            }

            if (!_ready) Build();

            TrackAttacks();
            DrawFootRings(sim);
            DrawAttackArc(sim);
            DrawMoveOrder(sim);
            UpdateCursor(sim);
        }

        /// <summary>Запоминает, кого игрок ударил в этом кадре.</summary>
        private void TrackAttacks()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Attack) continue;
                if (e.Source != Simulation.PlayerId) continue;

                _lastTarget = e.Target;
                _lastTargetAt = Time.unscaledTime;
            }
        }

        private void DrawFootRings(Simulation sim)
        {
            int used = 0;
            if (ShowFootRings)
            {
                bool highlightAlive = Time.unscaledTime - _lastTargetAt < TargetHighlight;
                EntityStore entities = sim.Entities;
                int hovered = _driver.HoveredEntity;

                // Назначенная цель подсвечена ПОСТОЯННО, пока приказ жив:
                // игрок должен видеть, кого персонаж добивает, а не гадать
                // по вспышкам последнего удара.
                int ordered = sim.AttackTarget;

                for (int i = 0; i < entities.Count && used < _rings.Length; i++)
                {
                    if (!entities.Alive[i]) continue;

                    bool isPlayer = i == Simulation.PlayerId;
                    bool isTarget = i == ordered || (highlightAlive && i == _lastTarget);

                    bool isHovered = i == hovered;

                    SpriteRenderer ring = _rings[used++];
                    ring.enabled = true;

                    // Порядок важностей: кого бью > на кого навёл > кто это вообще.
                    ring.color = isTarget ? TargetColor
                        : isHovered ? HoverColor
                        : isPlayer ? AllyColor
                        : EnemyColor;

                    // Диаметр — из симуляции. Кольцо и есть тот самый «хитбокс»:
                    // тела расталкиваются ровно по этому радиусу, и нарисованное
                    // не может разъехаться с настоящим.
                    float diameter = entities.BodyRadius[i].ToFloat() * RingScale;

                    // Цель и наведённый крупнее: цвет читается хуже размера,
                    // когда на экране двадцать красных колец.
                    if (isTarget || isHovered) diameter *= 1.22f;

                    Place(ring.transform, _driver.GetRenderPosition(i), diameter);
                }
            }

            for (int i = used; i < _rings.Length; i++)
                if (_rings[i].enabled) _rings[i].enabled = false;
        }

        /// <summary>
        /// Сектор автоатаки под игроком. Именно СЕКТОР, а не круг: бить можно
        /// только вперёд, и круг обманывал бы — игрок решил бы, что достаёт
        /// и за спину.
        /// </summary>
        private void DrawAttackArc(Simulation sim)
        {
            bool show = ShowAttackArc && sim.Entities.Alive[Simulation.PlayerId] && _driver.AttackHeld;
            _arc.enabled = show;
            if (!show) return;

            _arc.color = ArcColor;

            Vector3 at = _driver.GetRenderPosition(Simulation.PlayerId);
            float diameter = Simulation.AutoAttackRange.ToFloat() * 2f;

            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Vector3 forward = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat());

            Transform t = _arc.transform;
            t.position = new Vector3(at.x, 0.015f, at.z);
            t.localScale = Vector3.one * diameter;

            // Спрайт нарисован сектором вокруг своего +Y. LookRotation ставит
            // +Z по направлению взгляда, поворот на 90° вокруг X кладёт спрайт
            // на землю и переводит его +Y в это направление.
            t.rotation = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.Euler(90f, 0f, 0f);
        }

        private void DrawMoveOrder(Simulation sim)
        {
            // Запрос делается ДО проверки настройки: при коротком замыкании
            // компилятор справедливо считает target неприсвоенным.
            FixVec2 target;
            bool hasOrder = sim.TryGetMoveOrder(out target);
            bool show = ShowMoveOrder && hasOrder;
            _order.enabled = show;
            if (!show) return;

            _order.color = OrderColor;

            // Метка пульсирует: неподвижная точка на полу теряется среди тел.
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 9f);
            Place(_order.transform, new Vector3(target.X.ToFloat(), 0f, target.Y.ToFloat()),
                0.55f * pulse);
        }

        private void HideAll()
        {
            for (int i = 0; i < _rings.Length; i++) _rings[i].enabled = false;
            _arc.enabled = false;
            _order.enabled = false;
            SetAttackCursor(false);
        }

        /// <summary>
        /// Курсор над врагом становится боевым.
        ///
        /// Это второй канал того же сообщения, что и подсветка кольцом, и он
        /// нужен именно как второй: кольцо под ногами живёт на полу и теряется
        /// в толпе, а курсор всегда там, куда игрок и смотрит.
        /// </summary>
        private void UpdateCursor(Simulation sim)
        {
            if (!ShowAttackCursor)
            {
                SetAttackCursor(false);
                return;
            }
            SetAttackCursor(_driver.HoveredEntity >= 0);
        }

        private void SetAttackCursor(bool attack)
        {
            if (attack == _cursorIsAttack) return;
            _cursorIsAttack = attack;

            // Смена курсора стоит дорого на некоторых платформах, поэтому она
            // и делается только на переходе, а не каждый кадр.
            Cursor.SetCursor(attack ? _attackCursor : null,
                attack ? new Vector2(16f, 16f) : Vector2.zero, CursorMode.Auto);
        }

        private void OnDisable()
        {
            SetAttackCursor(false);
        }

        /// <summary>
        /// Боевой курсор: кольцо с четырьмя засечками. Рисуется кодом, как
        /// и всё остальное здесь — файла с курсором в проекте нет.
        /// </summary>
        private static Texture2D MakeAttackCursor(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / half;

                    // Кольцо.
                    float ring = (r > 0.52f && r < 0.78f) ? 1f : 0f;

                    // Четыре засечки по осям — они и делают курсор «боевым»,
                    // а не просто кружком.
                    bool spikeX = Mathf.Abs(dy) < 1.6f && r > 0.78f && r < 1.0f;
                    bool spikeY = Mathf.Abs(dx) < 1.6f && r > 0.78f && r < 1.0f;
                    float spike = (spikeX || spikeY) ? 1f : 0f;

                    float a = Mathf.Clamp01(ring + spike);
                    pixels[y * size + x] = a > 0.5f
                        ? new Color32(255, 70, 60, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static void Place(Transform t, Vector3 at, float diameter)
        {
            // Чуть выше нуля: ровно на полу спрайт дерётся с плитой за глубину
            // и мерцает.
            t.position = new Vector3(at.x, 0.02f, at.z);
            t.rotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = Vector3.one * diameter;
        }

        private static SpriteRenderer MakeFlatSprite(Transform root, Sprite sprite, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>Кольцо. thickness — доля радиуса, занятая ободком.</summary>
        private static Sprite MakeRingSprite(int size, float innerRatio)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Мягкие края с обеих сторон ободка: жёсткий край на полу
                    // выглядит как ступенька, особенно на наклонной камере.
                    float outer = 1f - Mathf.SmoothStep(0.92f, 1.0f, r);
                    float inner = Mathf.SmoothStep(innerRatio - 0.08f, innerRatio, r);
                    byte a = (byte)(Mathf.Clamp01(outer * inner) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>
        /// Сектор вокруг оси +Y текстуры. arcCos — косинус ПОЛОВИНЫ угла,
        /// ровно та же величина, по которой симуляция выбирает цель.
        /// </summary>
        private static Sprite MakeArcSprite(int size, float arcCos)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    byte a = 0;
                    if (r <= 1f && r > 0.0001f)
                    {
                        float cos = dy / r;                       // ось сектора — это +Y
                        if (cos >= arcCos)
                        {
                            // Гасим к внешнему краю и у боковых границ: заливка
                            // с резкой каймой читается как объект, а не как зона.
                            float edge = 1f - Mathf.SmoothStep(0.80f, 1.0f, r);
                            float side = Mathf.SmoothStep(arcCos, Mathf.Min(1f, arcCos + 0.18f), cos);
                            a = (byte)(Mathf.Clamp01(edge * side) * 255f);
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
