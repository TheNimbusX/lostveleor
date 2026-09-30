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
    /// ВИД — КАДР 1a ДОСКИ ПОЛИРОВКИ (выбор владельца 30.09, ART/UI/concepts-2026-09-30-hud-polish:
    /// «42 · 37 · крит 126 · слитое 211 · по герою −38»):
    /// <list type="bullet">
    /// <item>обычная — светло-кремовая, Nunito Bold (шрифт текста UI, UiTheme.Body), без минуса, тёмная
    /// обводка и мягкая тень;</item>
    /// <item>крит — крупнее, акцентный оранжевый #FF8A4C с тёплым свечением и искрой, появляется коротким
    /// «хлопком» (перелёт масштаба) и всегда своей цифрой — в сумму не растворяется;</item>
    /// <item>быстрые попадания по одной цели сливаются в одну растущую цифру: каждое новое толкает её,
    /// она крупнеет и теплеет к акценту (DamageNumberRules);</item>
    /// <item>урон по герою — красный «−38», крупнее своего: это потеря;</item>
    /// <item>цифры одной цели встают короткой лесенкой, а не друг на друга.</item>
    /// </list>
    /// Настройка «Цифры урона» (GameUserSettings.ShowDamageNumbers) выключает всё, кроме отклика
    /// уклонения. Текст пишется через SetText с числом — без строк в куче на каждое попадание.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class DamageNumbers : MonoBehaviour
    {
        [Header("Пул")]
        public int PoolSize = 64;

        [Header("Вид")]
        [Tooltip("Обычная цифра: светло-кремовая, как в кадре 1a")]
        public Color NormalColor = new Color32(0xF6, 0xEE, 0xDF, 0xFF);
        [Tooltip("Крит: единственный акцент интерфейса #FF8A4C; к низу градиент густеет (CritBottom)")]
        public Color CritColor = new Color32(0xFF, 0x8A, 0x4C, 0xFF);
        [Tooltip("Множитель цвета крита у низа цифры")]
        public Color CritBottom = new Color(1f, .8f, .72f, 1f);
        [Tooltip("Размер шрифта на единицу прежнего characterSize: 0,036 × 112 ≈ 4 (em ≈ 0,4 м)")]
        public float FontScale = 112f;
        [Tooltip("Искра у крита, метры")]
        public float SparkSize = 0.34f;
        public Color PlayerHitColor = new Color(1.00f, 0.28f, 0.30f);
        public Color FireColor = new Color(1.00f, 0.47f, 0.16f);
        public Color EvadeColor = new Color(.72f, .94f, 1f);

        [Tooltip("Тик горения. Приглушённый намеренно: их тридцать в секунду.")]
        public Color BurnColor = new Color(0.95f, 0.55f, 0.25f, 0.75f);

        public float NormalSize = 0.036f;
        [Tooltip("Крит: в полтора раза крупнее обычной (126 против 42 в кадре 1a)")]
        public float CritSize = 0.054f;

        [Tooltip("Урон по герою рисуется крупнее своего: это потеря, и она важнее.")]
        public float PlayerHitSize = 0.048f;

        [Tooltip("Тик урона по времени — самый мелкий: он фон, а не событие.")]
        public float BurnSize = 0.029f;

        [Header("Агрегация")]
        [Tooltip("Сколько цифр разрешено видеть одновременно. Удар по площади " +
                 "не должен превращать экран в таблицу.")]
        public int MaxVisible = 18;

        [Tooltip("Если с прошлого попадания по цели прошло меньше, новое вливается в её цифру, " +
                 "а не порождает вторую. Крит всегда встаёт отдельно.")]
        public float MergeWindow = 0.3f;
        [Tooltip("На сколько слитая цифра крупнеет с каждым попаданием (до ×1,6)")]
        public float MergeGrow = .14f;
        [Tooltip("Толчок слитой цифры от нового попадания: +доля размера, гаснет за 0,14 с")]
        public float MergeBump = .22f;

        [Header("Полёт")]
        public float Lifetime = 0.62f;
        [Tooltip("Последние секунды жизни цифра гаснет")]
        public float FadeTime = 0.26f;
        [Tooltip("Появление: секунд на рост из нуля с перелётом")]
        public float AppearTime = 0.11f;
        [Tooltip("Перелёт появления крита: 3,2 — короткий «хлопок» (пик ≈ ×1,28), у обычной 1,7 (≈ ×1,1)")]
        public float CritPop = 3.2f;
        public float RiseSpeed = 1.05f;
        public float SpawnHeight = 1.48f;
        [Tooltip("Разброс по горизонтали, чтобы цифры по одной цели не слипались.")]
        public float Jitter = 0.30f;
        [Tooltip("Ступень лесенки цифр одной цели, метры вдоль «вверх» экрана")]
        public float StackStep = 0.36f;

        private const float BumpTime = .14f;

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

            /// <summary>Сколько попаданий слито в цифру: от этого она крупнеет и теплеет.</summary>
            public int Hits;

            /// <summary>Секунд с последнего попадания: окно слияния считается от него.</summary>
            public float SinceHit;

            /// <summary>Возраст толчка от последнего слитого попадания.</summary>
            public float BumpAge;

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
        // Крит в эту привязку не встаёт: он своя цифра, а серия идёт мимо него.
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

            // Цифры — Nunito, шрифт текста UI (кадр 1a, правило «Philosopher — заголовки, Nunito — текст»).
            // Жирное начертание берётся шрифтом из таблицы весов, а не стилем Bold: так наш материал с
            // обводкой доезжает до цифр. Нет Nunito в теме — прежний шрифт цифр.
            TMP_FontAsset body = UiTheme.Current.Body;
            TMP_FontAsset bold = body != null ? BoldOf(body) : null;
            TMP_FontAsset font = bold != null ? bold : body != null ? body : UiTheme.Current.Numbers;
            if (font == null)
            {
                Debug.LogError("[Разлом] DamageNumbers: в теме UI нет шрифта цифр, цифры отключены.");
                enabled = false;
                return;
            }
            _syntheticBold = bold == null;
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
                // «Цифры урона: Выкл» в настройках: урон без цифр, отклик уклонения выше остаётся.
                if (!GameUserSettings.ShowDamageNumbers) continue;

                Spawn(e);
            }
        }

        private void Spawn(in SimEvent e)
        {
            bool crit = e.Flag;
            bool playerHit = e.Target == Simulation.PlayerId;
            bool overTime = e.Type == SimEventType.DamageOverTime;
            bool evaded = e.Type == SimEventType.Evaded;
            bool tracked = (uint)e.Target < (uint)_slotOfTarget.Length;

            // Свежая цифра по той же цели — доливаем в неё и толкаем её.
            // Новую не заводим. Крит и уклонение всегда встают своей цифрой.
            int existing = tracked ? _slotOfTarget[e.Target] : -1;
            // Горение сливается втрое дольше обычного: тридцать тиков в секунду
            // иначе дадут тридцать цифр в секунду на одной цели.
            float window = overTime ? MergeWindow * 3f : MergeWindow;

            if (!evaded
                && existing >= 0
                && _slots[existing].Remaining > 0f
                && _targetOfSlot[existing] == e.Target
                && !_slots[existing].Evaded
                && DamageNumberRules.Merges(_slots[existing].SinceHit, window, crit))
            {
                Merge(existing, in e, playerHit);
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
            _targetOfSlot[slot] = -1;

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

            // Лесенка: цифра по цели, у которой уже висят другие (крит посреди серии,
            // серия после окна), встаёт ступенью выше и чуть в сторону.
            if (_camera != null && e.Target >= 0)
            {
                DamageNumberRules.Stack(LiveCountFor(e.Target), StackStep, out float sx, out float sy);
                at += _camera.up * sy + _camera.right * sx;
            }

            ref Slot s = ref _slots[slot];
            s.Transform.gameObject.SetActive(true);
            s.Transform.position = at;
            if (_camera != null) s.Transform.rotation = _camera.rotation;

            s.Value = e.Amount;
            s.Hits = 1;
            s.Crit = crit;
            s.PlayerHit = playerHit;
            s.OverTime = overTime;
            s.Evaded = evaded;
            s.BaseColor = evaded ? EvadeColor : ColorFor(in e, crit, playerHit, overTime);
            WriteValue(ref s);

            s.Remaining = Lifetime;
            s.Age = 0f;
            s.SinceHit = 0f;
            s.BumpAge = BumpTime;
            s.Velocity = Vector3.up * RiseSpeed;
            s.Angle = JitterAngle(e.Target, _driver.Sim.Tick);
            s.Transform.localScale = Vector3.zero;

            // Крит и уклонение в серию не встают: следующие удары льются мимо них.
            if (tracked && !crit && !evaded) _slotOfTarget[e.Target] = slot;
            _targetOfSlot[slot] = e.Target;
            _visible++;
        }

        /// <summary>Сколько живых цифр уже висит над этой целью (для лесенки). Пул маленький — проход дешёвый.</summary>
        private int LiveCountFor(int target)
        {
            int count = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_targetOfSlot[i] == target && _slots[i].Remaining > 0f) count++;
            return count;
        }

        /// <summary>
        /// Доливает попадание в уже висящую цифру: сумма растёт, цифра крупнеет и
        /// толкается, жизнь продлевается. Появление не повторяется — цифра должна
        /// дёрнуться, а не начать жизнь заново.
        /// </summary>
        private void Merge(int slot, in SimEvent e, bool playerHit)
        {
            ref Slot s = ref _slots[slot];

            s.Value += e.Amount;
            s.Hits++;

            // Долив ударом снимает пометку «это горение»: серия, в которой
            // был настоящий удар, обязана выглядеть как удар.
            if (e.Type != SimEventType.DamageOverTime) s.OverTime = false;
            if (!s.Crit) s.BaseColor = ColorFor(in e, false, playerHit, s.OverTime);

            WriteValue(ref s);

            s.Remaining = Lifetime;
            s.SinceHit = 0f;
            s.BumpAge = 0f;
            // Небольшой подскок, а не новый взлёт: длинная серия не должна улетать вверх.
            s.Velocity = Vector3.up * (RiseSpeed * .45f);
        }

        private void WriteValue(ref Slot s)
        {
            // Кадр 1a: у урона по врагам минуса нет, у урона по герою — «−38» (настоящий минус
            // U+2212 есть в Nunito). SetText с числом не создаёт строку на каждое попадание.
            if (s.Evaded) s.Text.SetText("УКЛОНЕНИЕ");
            else if (s.PlayerHit) s.Text.SetText("−{0}", s.Value);
            else s.Text.SetText("{0}", s.Value);

            s.Size = s.Evaded ? NormalSize * .8f : s.PlayerHit ? PlayerHitSize
                : s.Crit ? CritSize
                : s.OverTime ? BurnSize
                : NormalSize;
            s.Text.fontSize = s.Size * FontScale;
            bool warm = s.Crit && !s.PlayerHit && !s.Evaded;
            s.Text.fontSharedMaterial = warm ? _critMaterial : _normalMaterial;
            s.Text.enableVertexGradient = warm;
            if (warm) s.Text.colorGradient = new VertexGradient(Color.white, Color.white, CritBottom, CritBottom);
            s.Text.color = DisplayColor(in s);

            // Искра крита — у правого верхнего угла числа.
            bool spark = s.Crit && !s.PlayerHit && !s.Evaded && s.Spark != null;
            if (s.Spark != null)
            {
                s.Spark.gameObject.SetActive(spark);
                if (spark)
                {
                    float k = s.Size / CritSize;
                    Vector2 size = s.Text.GetPreferredValues();
                    s.Spark.transform.localPosition = new Vector3(size.x * .5f + .02f, size.y * .32f, -.01f);
                    s.Spark.transform.localScale = Vector3.one * (SparkSize / Mathf.Max(.001f, s.Spark.sprite.bounds.size.x) * k);
                    s.Spark.color = s.BaseColor;
                }
            }
        }

        /// <summary>
        /// Цвет цифры с учётом слияния: обычная серия теплеет к акценту по мере роста
        /// (слитое «211» в кадре 1a тёплое). Урон по герою, крит, горение, уклонение — свои цвета.
        /// </summary>
        private Color DisplayColor(in Slot s)
        {
            if (s.PlayerHit || s.Crit || s.Evaded || s.OverTime) return s.BaseColor;
            float warmth = DamageNumberRules.MergedWarmth(s.Hits);
            return warmth > 0f ? Color.Lerp(s.BaseColor, CritColor, warmth) : s.BaseColor;
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
                s.SinceHit += dt;
                s.BumpAge += dt;
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

                // Появление с перелётом (у крита — «хлопок»), рост от слияния, толчок от
                // нового попадания и лёгкое сжатие к концу жизни.
                float pop = DamageNumberRules.Pop(s.Age, AppearTime, s.Crit && !s.PlayerHit ? CritPop : 1.70158f);
                float grow = DamageNumberRules.MergedScale(s.Hits, MergeGrow);
                float bump = 1f + DamageNumberRules.Bump(s.BumpAge, BumpTime, MergeBump);
                float settle = Mathf.Lerp(.86f, 1f, s.Remaining / Mathf.Max(.01f, Lifetime));
                s.Transform.localScale = Vector3.one * (pop * grow * bump * settle);
                float spin = 1f - Mathf.Clamp01(s.Age / Mathf.Max(.01f, Lifetime));
                if (_camera != null)
                    s.Transform.rotation = _camera.rotation * Quaternion.Euler(0f, 0f, s.Angle * spin);

                Color c = DisplayColor(in s);
                c.a *= DamageNumberRules.Fade(s.Remaining, FadeTime);
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
        private bool _syntheticBold;

        /// <summary>Жирный (вес 700) из таблицы весов шрифта; null — его нет.</summary>
        private static TMP_FontAsset BoldOf(TMP_FontAsset font)
        {
            TMP_FontWeightPair[] weights = font.fontWeightTable;
            const int bold = 7;
            if (weights == null || weights.Length <= bold) return null;
            TMP_FontAsset typeface = weights[bold].regularTypeface;
            return typeface != null && typeface != font ? typeface : null;
        }

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
            text.fontStyle = _syntheticBold ? FontStyles.Bold : FontStyles.Normal;
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
