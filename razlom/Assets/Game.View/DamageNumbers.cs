using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Всплывающие цифры урона. Читает события SimEventType.Damage и ничего
    /// не решает сама: сколько урона и был ли крит — это уже посчитано на тике,
    /// цифра лишь показывает результат.
    ///
    /// Кольцевой пул: за забег цифр будут десятки тысяч, и ни одна не должна
    /// стоить аллокации. Когда все слоты заняты, переиспользуется самый старый —
    /// потерять цифру в мясорубке лучше, чем создать объект в бою.
    ///
    /// ВИД — ПАК «НОЧНАЯ АКВАРЕЛЬ» (лист HUD, 23 сентября 2026): TextMeshPro
    /// шрифтом UiTheme.Numbers (Nunito Bold), мягкая тёмная обводка; крит —
    /// градиент от светлого оранжевого к насыщенному, тёплое свечение и искра.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class DamageNumbers : MonoBehaviour
    {
        [Header("Пул")]
        public int PoolSize = 64;

        [Header("Вид")]
        // Цвета пака «Ночная акварель» (23 сентября 2026): светлая обычная,
        // оранжевый крит с искрой, как на листе HUD.
        public Color NormalColor = new Color32(0xF4, 0xF7, 0xFB, 0xFF);
        [Tooltip("Крит: верх цифры; к низу градиент густеет (CritBottom)")]
        public Color CritColor = new Color32(0xFF, 0xB0, 0x5C, 0xFF);
        [Tooltip("Множитель цвета крита у низа цифры")]
        public Color CritBottom = new Color(1f, .6f, .38f, 1f);
        [Tooltip("Размер шрифта на единицу прежнего characterSize: 0,036 × 96 ≈ 3,5")]
        public float FontScale = 112f;
        [Tooltip("Искра у крита, метры")]
        public float SparkSize = 0.34f;
        public Color PlayerHitColor = new Color(1.00f, 0.28f, 0.30f);
        public Color FireColor = new Color(1.00f, 0.47f, 0.16f);
        public Color EvadeColor = new Color(.72f, .94f, 1f);

        [Tooltip("Тик горения. Приглушённый намеренно: их тридцать в секунду.")]
        public Color BurnColor = new Color(0.95f, 0.55f, 0.25f, 0.75f);

        public float NormalSize = 0.036f;
        public float CritSize = 0.054f;

        [Tooltip("Урон по герою рисуется крупнее своего: это потеря, и она важнее.")]
        public float PlayerHitSize = 0.052f;

        [Tooltip("Тик урона по времени — самый мелкий: он фон, а не событие.")]
        public float BurnSize = 0.029f;

        [Header("Агрегация")]
        [Tooltip("Сколько цифр разрешено видеть одновременно. Удар по площади " +
                 "не должен превращать экран в таблицу.")]
        public int MaxVisible = 18;

        [Tooltip("Пока цифра моложе этого возраста, следующее попадание по той же " +
                 "цели вливается в неё, а не порождает вторую.")]
        public float MergeWindow = 0.28f;

        [Header("Полёт")]
        public float Lifetime = 0.62f;
        public float RiseSpeed = 1.05f;
        public float SpawnHeight = 1.48f;
        [Tooltip("Разброс по горизонтали, чтобы цифры по одной цели не слипались.")]
        public float Jitter = 0.30f;

        private TickDriver _driver;
        private Transform _camera;

        private struct Slot
        {
            public Transform Transform;
            public TextMeshPro Text;
            public SpriteRenderer Spark;
            public float Remaining;
            public float Age;
            public Color BaseColor;
            public Vector3 Velocity;
            public float Angle;
            public float Size;

            /// <summary>Накопленный урон. Слитые попадания складываются сюда.</summary>
            public int Value;

            public bool Crit;

            /// <summary>Урон ПО ГЕРОЮ. Рисуется с минусом и крупнее: это потеря.</summary>
            public bool PlayerHit;

            /// <summary>Тик урона по времени: самый мелкий и тихий.</summary>
            public bool OverTime;
            public bool Evaded;
        }

        private Slot[] _slots;
        private int _next; // следующий слот кольца

        // Кто в каком слоте. Нужно, чтобы серия быстрых попаданий по одной цели
        // читалась как ОДНА растущая цифра, а не как столбик из шести.
        //
        // Так это и работает в жанре: игрок читает не каждый удар, а сумму,
        // которую он снял с этой цели. Столбик мелких цифр не читается вообще.
        private int[] _slotOfTarget;
        private int[] _targetOfSlot;
        private int _visible;
        private float _lastEvadeShownAt = -100f;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void Start()
        {
            _camera = Camera.main != null ? Camera.main.transform : null;
            if (_camera == null)
                Debug.LogWarning("[Разлом] DamageNumbers: не найдена основная камера, цифры не будут развёрнуты к зрителю.");

            TMP_FontAsset font = UiTheme.Current.Numbers != null ? UiTheme.Current.Numbers : UiTheme.Current.Body;
            if (font == null)
            {
                Debug.LogError("[Разлом] DamageNumbers: в теме UI нет шрифта цифр, цифры отключены.");
                enabled = false;
                return;
            }
            // Обычная: тёмная обводка и мягкая тень снизу. Крит: тёплое свечение вокруг.
            _normalMaterial = Styled(font, new Color(.03f, .04f, .07f, 1f), .26f, new Color(0f, 0f, 0f, .7f), .35f, .6f, -.6f);
            _critMaterial = Styled(font, new Color(.28f, .08f, .02f, 1f), .2f, new Color(1f, .42f, .1f, .6f), .55f, 1f, 0f);

            Transform root = new GameObject("Пул: цифры урона").transform;
            root.SetParent(transform, false);

            _slots = new Slot[Mathf.Max(1, PoolSize)];
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = CreateSlot(root, font, i);

            _slotOfTarget = new int[TickDriver.MaxSimCapacity];
            for (int i = 0; i < _slotOfTarget.Length; i++) _slotOfTarget[i] = -1;

            _targetOfSlot = new int[_slots.Length];
            for (int i = 0; i < _targetOfSlot.Length; i++) _targetOfSlot[i] = -1;
        }

        private void LateUpdate()
        {
            // LateUpdate: все шаги кадра уже сделаны, FrameEvents собран целиком.
            if (_slots == null) return;

            ConsumeEvents();
            Animate();
        }

        private void ConsumeEvents()
        {
            // В лагере вне Полигона симуляции нет, а значит нет и событий:
            // но проверка стоит здесь, а не полагается на пустой список.
            if (_driver.Sim == null) return;

            IReadOnlyList<SimEvent> events = _driver.FrameEvents;

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type == SimEventType.Evaded && e.Source == Simulation.PlayerId)
                {
                    // Несколько промахов толпы в одном кадре дают один читаемый отклик.
                    if (Time.time - _lastEvadeShownAt < .2f) continue;
                    _lastEvadeShownAt = Time.time;
                    Spawn(e);
                    continue;
                }
                if (e.Type != SimEventType.Damage && e.Type != SimEventType.DamageOverTime) continue;

                Spawn(e);
            }
        }

        private void Spawn(in SimEvent e)
        {
            bool crit = e.Flag;
            bool playerHit = e.Target == Simulation.PlayerId;
            bool overTime = e.Type == SimEventType.DamageOverTime;

            // Свежая цифра по той же цели — доливаем в неё и бьём по ней
            // ещё раз масштабом. Новую не заводим.
            int existing = (uint)e.Target < (uint)_slotOfTarget.Length ? _slotOfTarget[e.Target] : -1;
            // Горение сливается вдвое дольше обычного: тридцать тиков в секунду
            // иначе дадут тридцать цифр в секунду на одной цели.
            float window = overTime ? MergeWindow * 3f : MergeWindow;

            if (existing >= 0
                && _slots[existing].Remaining > 0f
                && _slots[existing].Age < window
                && _targetOfSlot[existing] == e.Target)
            {
                Merge(existing, in e, crit, playerHit);
                return;
            }

            // Экран уже занят. Пропустить цифру честнее, чем добавить двадцатую:
            // двадцать цифр не читает никто, а важные тонут вместе с остальными.
            if (_visible >= MaxVisible) return;

            int slot = _next;
            _next = (_next + 1) % _slots.Length;

            // Слот мог принадлежать другой цели — снимаем старую привязку,
            // иначе та цель начнёт доливать в чужую цифру.
            int previousOwner = _targetOfSlot[slot];
            if (previousOwner >= 0
                && (uint)previousOwner < (uint)_slotOfTarget.Length
                && _slotOfTarget[previousOwner] == slot)
                _slotOfTarget[previousOwner] = -1;

            // Позиция берётся из события, а не из текущей позиции цели: цель могла
            // умереть на этом же тике, и её объект уже спрятан.
            Vector3 at = new Vector3(e.Position.X.ToFloat(), SpawnHeight, e.Position.Y.ToFloat());
            Simulation sim = _driver.Sim;
            if (e.Source == Simulation.PlayerId && sim != null
                && (uint)e.Source < (uint)sim.Entities.Count
                && (uint)e.Target < (uint)sim.Entities.Count)
            {
                FixVec2 source = sim.Entities.Position[e.Source];
                Vector3 away = new Vector3(at.x - source.X.ToFloat(), 0f, at.z - source.Y.ToFloat());
                if (away.sqrMagnitude > 0.0001f) at += away.normalized * 0.24f;
            }
            at += HorizontalJitter(e.Target, _driver.Sim.Tick);

            ref Slot s = ref _slots[slot];
            s.Transform.gameObject.SetActive(true);
            s.Transform.position = at;
            if (_camera != null) s.Transform.rotation = _camera.rotation;

            s.Value = e.Amount;
            s.Crit = crit;
            s.PlayerHit = playerHit;
            s.OverTime = overTime;
            s.Evaded = e.Type == SimEventType.Evaded;
            s.BaseColor = s.Evaded ? EvadeColor : ColorFor(in e, crit, playerHit, overTime);
            WriteValue(ref s);

            s.Remaining = Lifetime;
            s.Age = 0f;
            s.Velocity = Vector3.up * RiseSpeed;
            s.Angle = JitterAngle(e.Target, _driver.Sim.Tick);
            s.Transform.localScale = Vector3.one * 0.15f;

            if ((uint)e.Target < (uint)_slotOfTarget.Length) _slotOfTarget[e.Target] = slot;
            _targetOfSlot[slot] = e.Target;
            _visible++;
        }

        /// <summary>
        /// Доливает попадание в уже висящую цифру и повторяет удар масштабом.
        /// Возраст сбрасывается не в ноль, а в начало анимации появления —
        /// цифра должна дёрнуться, а не начать жизнь заново.
        /// </summary>
        private void Merge(int slot, in SimEvent e, bool crit, bool playerHit)
        {
            ref Slot s = ref _slots[slot];

            s.Value += e.Amount;
            if (crit) s.Crit = true;

            // Долив ударом снимает пометку «это горение»: серия, в которой
            // был настоящий удар, обязана выглядеть как удар.
            if (e.Type != SimEventType.DamageOverTime) s.OverTime = false;

            // Цвет крита забирает верх: серия, в которой был крит, обязана
            // выглядеть как крит.
            if (crit || !s.Crit) s.BaseColor = ColorFor(in e, s.Crit, playerHit, s.OverTime);

            WriteValue(ref s);

            s.Remaining = Lifetime;
            s.Age = 0f;
            s.Velocity = Vector3.up * RiseSpeed;
        }

        private void WriteValue(ref Slot s)
        {
            // ОПОЗНАВАТЕЛЬ. Свой урон — просто число, урон по герою — число
            // с минусом. Это первое, что читается, и читается оно даже боковым
            // зрением, когда на цвет смотреть некогда.
            string value = s.Evaded ? "УКЛОНЕНИЕ" : s.PlayerHit
                ? "−" + s.Value.ToString()
                : s.Crit ? s.Value.ToString() + "!" : s.Value.ToString();

            s.Text.text = value;

            s.Size = s.Evaded ? NormalSize * .8f : s.PlayerHit ? PlayerHitSize
                : s.Crit ? CritSize
                : s.OverTime ? BurnSize
                : NormalSize;
            s.Text.fontSize = s.Size * FontScale;
            bool warm = s.Crit && !s.PlayerHit && !s.Evaded;
            s.Text.fontSharedMaterial = warm ? _critMaterial : _normalMaterial;
            s.Text.enableVertexGradient = warm;
            if (warm) s.Text.colorGradient = new VertexGradient(Color.white, Color.white, CritBottom, CritBottom);
            s.Text.color = s.BaseColor;

            // Искра крита — у правого верхнего угла числа.
            bool spark = s.Crit && !s.PlayerHit && !s.Evaded && s.Spark != null;
            if (s.Spark != null)
            {
                s.Spark.gameObject.SetActive(spark);
                if (spark)
                {
                    float k = s.Size / CritSize;
                    Vector2 size = s.Text.GetPreferredValues(value);
                    s.Spark.transform.localPosition = new Vector3(size.x * .5f + .02f, size.y * .32f, -.01f);
                    s.Spark.transform.localScale = Vector3.one * (SparkSize / Mathf.Max(.001f, s.Spark.sprite.bounds.size.x) * k);
                    s.Spark.color = s.BaseColor;
                }
            }
        }

        /// <summary>
        /// Состояние цифры. Порядок проверок — это и есть иерархия важности:
        /// урон по герою важнее всего, крит важнее стихии, стихия важнее
        /// обычного удара.
        /// </summary>
        private Color ColorFor(in SimEvent e, bool crit, bool playerHit, bool overTime)
        {
            if (playerHit) return PlayerHitColor;
            if (crit) return CritColor;
            if (overTime) return BurnColor;
            if (e.DamageKind == DamageType.Fire) return FireColor;
            return NormalColor;
        }

        private void Animate()
        {
            float dt = Time.deltaTime;
            int visible = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (s.Remaining <= 0f) continue;

                s.Remaining -= dt;
                s.Age += dt;
                if (s.Remaining <= 0f)
                {
                    s.Transform.gameObject.SetActive(false);

                    int owner = _targetOfSlot[i];
                    if (owner >= 0
                        && (uint)owner < (uint)_slotOfTarget.Length
                        && _slotOfTarget[owner] == i)
                        _slotOfTarget[owner] = -1;
                    _targetOfSlot[i] = -1;
                    continue;
                }

                visible++;

                s.Transform.position += s.Velocity * dt;
                s.Velocity += Vector3.down * (1.4f * dt);

                float t = Mathf.Clamp01(s.Age / Lifetime);
                float punch = t < 0.18f
                    ? EaseOutBack(t / 0.18f)
                    : Mathf.Lerp(1f, 0.86f, (t - 0.18f) / 0.82f);
                s.Transform.localScale = Vector3.one * punch;
                if (_camera != null)
                    s.Transform.rotation = _camera.rotation * Quaternion.Euler(0f, 0f, s.Angle * (1f - t));

                Color c = s.BaseColor;
                c.a = 1f - Mathf.SmoothStep(0.58f, 1f, t);
                s.Text.color = c;
                if (s.Spark != null && s.Spark.gameObject.activeSelf)
                {
                    Color sc = s.BaseColor;
                    sc.a = c.a;
                    s.Spark.color = sc;
                }
            }

            _visible = visible;
        }

        /// <summary>
        /// Разброс считается от индекса цели и тика, а не случайно: две цифры,
        /// выпавшие на одном тике по одной цели, расходятся, и картинка при этом
        /// одинакова на повторе реплея.
        /// </summary>
        private Vector3 HorizontalJitter(int target, int tick)
        {
            unchecked
            {
                int h = (target * 73856093) ^ (tick * 19349663);
                float a = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
                float r = ((h >> 16) & 0xFF) / 255f * Jitter;
                return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
        }

        private Material _normalMaterial, _critMaterial;

        /// <summary>Стиль шрифта: обводка и подложка (тень или свечение). Создаётся один раз на запуск.</summary>
        private static Material Styled(TMP_FontAsset font, Color outline, float outlineWidth,
            Color underlay, float dilate, float softness, float offsetY)
        {
            var m = new Material(font.material) { name = font.name + " Damage" };
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, outline);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, underlay);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offsetY);
            ShaderUtilities.GetShaderPropertyIDs();
            ShaderUtilities.UpdateShaderRatios(m);
            return m;
        }

        private Slot CreateSlot(Transform root, TMP_FontAsset font, int index)
        {
            GameObject go = new GameObject($"Цифра {index}");
            go.transform.SetParent(root, false);

            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = _normalMaterial;
            text.fontSize = NormalSize * FontScale;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(4f, 1f);
            text.color = NormalColor;
            text.GetComponent<MeshRenderer>().sortingOrder = 6100;

            SpriteRenderer spark = null;
            Sprite sparkSprite = UiTheme.Current.Spark;
            if (sparkSprite != null)
            {
                var sparkGo = new GameObject("Искра");
                sparkGo.transform.SetParent(go.transform, false);
                spark = sparkGo.AddComponent<SpriteRenderer>();
                spark.sprite = sparkSprite;
                spark.sortingOrder = 6101;
                float native = Mathf.Max(.001f, sparkSprite.bounds.size.x);
                sparkGo.transform.localScale = Vector3.one * (SparkSize / native);
                sparkGo.SetActive(false);
            }

            go.SetActive(false);
            return new Slot
            {
                Transform = go.transform,
                Text = text,
                Spark = spark,
                Remaining = 0f,
                BaseColor = NormalColor
            };
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static float JitterAngle(int target, int tick)
        {
            unchecked
            {
                int h = (target * 83492791) ^ (tick * 297121507);
                return ((h & 1023) / 1023f * 2f - 1f) * 8f;
            }
        }
    }
}
