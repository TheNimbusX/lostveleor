using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Урон по герою (этап 4, п. 4; выбор владельца 30.09 — кадр 1a доски
    /// ART/UI/concepts-2026-09-30-hud-polish):
    /// <list type="bullet">
    /// <item>мягкая красная КРОМКА экрана со стороны удара — направление от героя к бьющему
    /// (Damage.Source), спроецированное на экран; вспыхивает и гаснет за ~0,4 с, сила — по доле здоровья,
    /// которую снял удар;</item>
    /// <item>ВИНЬЕТКА при низком здоровье (меньше ~30 %): тёмно-красный мягкий свет по краям, медленно
    /// дышит и уходит после лечения. Не мазок туши — мазок из 1b владелец отверг («похож на кровь»);</item>
    /// <item>МИКРОСТОП на сильном ударе (≥ 15 % здоровья) и оглушении: тот же приём, что у стоп-кадра
    /// тяжёлого убийства (ArenaView.HoldEntityPose — тело бьющего держит позу удара, Sim идёт своим
    /// чередом), плюс камера на те же 60 мс держит свой толчок (CombatCameraJuice.Hold). Глобальное
    /// время не трогается — правило CombatJuiceView.</item>
    /// </list>
    /// Холст — поверх мира, но ПОД боевым HUD (порядок 6: HUD 10, подписи мира RunWorld 8): плашки HUD
    /// рисуются поверх кромки и не тонут в красном. Внизу, где портрет с его красной дымкой
    /// (HudPortraitMotion / HudPulse), виньетка вдвое тише, а её вдох совпадает с каждым вторым вдохом
    /// дымки — портрету второй дымки не достаётся. Всё гасится настройкой «Вспышки и мерцание»
    /// (GameUserSettings.FlashScale). Время — часы интерфейса (UiMotion.Now); кадр без выделений памяти:
    /// текстуры собираются один раз, в кадре только числа. Кривые — HeroHitFeedbackCurves (тесты вне Unity).
    /// Звуков нет: общий звуковой проход позже.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1007)]
    public sealed class HeroHitFeedback : MonoBehaviour
    {
        [Header("Кромка удара")]
        [Tooltip("Цвет кромки: мягкий красный (прозрачность — в Edge Alpha)")]
        public Color EdgeColor = new Color(.80f, .10f, .09f, 1f);
        [Tooltip("Прозрачность кромки на полной силе")] [Range(0f, 1f)] public float EdgeAlpha = .55f;
        [Tooltip("Глубина кромки от края к середине, доли высоты экрана")] public float EdgeDepth = .24f;
        [Tooltip("Длина пятна вдоль края, доли высоты экрана")] public float EdgeLength = 1.15f;
        [Tooltip("Секунд на подъём")] public float EdgeRise = .05f;
        [Tooltip("Секунд на полной силе")] public float EdgeHold = .05f;
        [Tooltip("Секунд на угасание")] public float EdgeFade = .4f;
        [Tooltip("Нижний край тише: там боевой HUD и портрет")] [Range(0f, 1f)] public float BottomEdgeScale = .55f;
        [Tooltip("Удар без понятного направления (бьющего нет или он в герое): все края на эту долю силы")]
        [Range(0f, 1f)] public float AmbientEdgeScale = .45f;

        [Header("Виньетка низкого здоровья")]
        [Tooltip("Тёмно-красный, не кровь: прозрачность — в Vignette Alpha")]
        public Color VignetteColor = new Color(.34f, .025f, .035f, 1f);
        [Tooltip("Прозрачность виньетки на полной силе")] [Range(0f, 1f)] public float VignetteAlpha = .6f;
        [Tooltip("Доля здоровья, ниже которой виньетка начинает проявляться")] [Range(0f, 1f)] public float LowHealthStart = .3f;
        [Tooltip("Доля здоровья, при которой виньетка полная")] [Range(0f, 1f)] public float LowHealthFull = .1f;
        [Tooltip("Секунд на проявление")] public float VignetteRise = .6f;
        [Tooltip("Секунд на уход после лечения")] public float VignetteFall = .9f;
        [Tooltip("Период красной дымки портрета (HudPulse «Danger» в CombatHudWc): виньетка дышит вдвое медленнее, в такт")]
        public float PortraitPulsePeriod = 1.05f;
        [Tooltip("Глубина дыхания виньетки: на выдохе — (1 − глубина) силы")] [Range(0f, 1f)] public float BreathDepth = .3f;
        [Tooltip("Нижний край виньетки тише: там портрет со своей дымкой")] [Range(0f, 1f)] public float VignetteBottom = .5f;

        [Header("Микростоп")]
        [Tooltip("Удар от этой доли здоровья и выше даёт микростоп (плюс любое оглушение)")] [Range(0f, 1f)] public float MicroStopShare = .15f;
        [Tooltip("Длина микростопа, с")] [Range(0f, .1f)] public float MicroStopSeconds = .06f;
        [Tooltip("Не чаще, с: толпа не держит кадр застывшим")] public float MicroStopCooldown = .6f;
        [Tooltip("Позу держит бьющий не дальше этого, м (стрелку издалека стоп не нужен — хватает камеры)")]
        public float MicroStopReach = 4.5f;
        [Tooltip("Толчок камеры на сильном ударе (сила тряски — из настроек)")] [Range(0f, 1f)] public float MicroStopTrauma = .45f;
        [Range(0f, 1f)] public float MicroStopZoom = .25f;

        /// <summary>Порядок холста: поверх мира, но под подписями мира (RunWorldWc, 8) и боевым HUD (CombatHudWc, 10).</summary>
        public const int CanvasOrder = 6;

        private TickDriver _driver;
        private ArenaView _arena;
        private Camera _camera;
        private CombatCameraJuice _cameraJuice;

        private Canvas _canvas;
        private readonly RawImage[] _edges = new RawImage[HeroHitFeedbackCurves.EdgeCount];
        private RawImage _vignette;
        private Texture2D _edgeAcrossX, _edgeAcrossY, _vignetteTexture;

        // Кромка каждого края: пик, когда вспыхнула (часы интерфейса), где вдоль края.
        private readonly float[] _edgePeak = new float[HeroHitFeedbackCurves.EdgeCount];
        private readonly float[] _edgeAt = new float[HeroHitFeedbackCurves.EdgeCount];
        private readonly float[] _edgeAlong = new float[HeroHitFeedbackCurves.EdgeCount];

        private float _vignetteWeight;
        private float _lastNow = -1f;
        private float _lastStopAt = -100f;
        private int _generation = -1;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            for (int i = 0; i < _edgeAt.Length; i++) _edgeAt[i] = -100f;
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_edgeAcrossX != null) Destroy(_edgeAcrossX);
            if (_edgeAcrossY != null) Destroy(_edgeAcrossY);
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        private void LateUpdate()
        {
            float now = UiMotion.Now;
            // Шаг по часам интерфейса, не больше 0,1 с: на склейке арены кадр длится секунды.
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;

            Simulation sim = _driver.Sim;
            if (_generation != _driver.Generation)
            {
                // Новый забег или арена: прошлые кромки и виньетка сюда не переходят.
                _generation = _driver.Generation;
                for (int i = 0; i < _edgePeak.Length; i++) _edgePeak[i] = 0f;
                _vignetteWeight = 0f;
            }

            float lowHealth = 0f;
            bool fight = sim != null && _driver.Session != null && _driver.Session.Mode != GameMode.Summary;
            if (fight)
            {
                if (_canvas == null) Build();
                ConsumeEvents(sim, now);
                EntityStore entities = sim.Entities;
                int max = entities.MaxHealth[Simulation.PlayerId];
                if (entities.Alive[Simulation.PlayerId] && max > 0)
                    lowHealth = HeroHitFeedbackCurves.LowHealth(entities.Health[Simulation.PlayerId] / (float)max, LowHealthStart, LowHealthFull);
            }
            else
            {
                // Лагерь, итоги: кромки гаснут сразу, виньетка уходит своим ходом.
                for (int i = 0; i < _edgePeak.Length; i++) _edgePeak[i] = 0f;
            }

            _vignetteWeight = HeroHitFeedbackCurves.Approach(_vignetteWeight, lowHealth, dt, VignetteRise, VignetteFall);
            if (_canvas != null) Render(now);
        }

        private void ConsumeEvents(Simulation sim, float now)
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            EntityStore entities = sim.Entities;
            int max = entities.MaxHealth[Simulation.PlayerId];
            int biggest = 0, stopSource = -1;
            bool stun = false;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Target != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.Damage && e.Amount > 0)
                {
                    Flare(sim, e.Source, HeroHitFeedbackCurves.HitStrength(e.Amount, max), now);
                    if (e.Amount > biggest) { biggest = e.Amount; stopSource = e.Source; }
                }
                else if (e.Type == SimEventType.HeroControl && !e.Flag)
                {
                    // Оглушение (Flag = false; корни — true): разбег Камнекопыта и ему подобные.
                    stun = true;
                    if (stopSource < 0) stopSource = e.Source;
                }
            }

            if (HeroHitFeedbackCurves.MicroStop(biggest, max, stun, now - _lastStopAt, MicroStopShare, MicroStopCooldown))
                MicroStop(sim, stopSource, now);
        }

        /// <summary>Кромка от удара силой <paramref name="strength"/>: края со стороны бьющего по косинусу.</summary>
        private void Flare(Simulation sim, int source, float strength, float now)
        {
            if (strength <= 0f) return;
            if (_camera == null) _camera = Camera.main;
            float dx = 0f, dy = 0f, hx = .5f, hy = .5f, aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            bool directed = false;
            if (_camera != null && source >= 0 && source != Simulation.PlayerId && source < sim.Entities.Count)
            {
                // Центр тела, а не точка на земле: у высоких бьющих направление на экране честнее.
                Vector3 hero = _camera.WorldToScreenPoint(_driver.GetRenderPosition(Simulation.PlayerId) + Vector3.up);
                Vector3 from = _camera.WorldToScreenPoint(_driver.GetRenderPosition(source) + Vector3.up);
                float height = Mathf.Max(1f, Screen.height);
                dx = (from.x - hero.x) / height;
                dy = (from.y - hero.y) / height;
                hx = hero.x / Mathf.Max(1f, Screen.width);
                hy = hero.y / height;
                // Меньше ~полпроцента экрана — бьющий «в герое»: стороны не понять.
                directed = dx * dx + dy * dy > .005f * .005f;
            }

            for (int edge = 0; edge < HeroHitFeedbackCurves.EdgeCount; edge++)
            {
                float weight = directed ? HeroHitFeedbackCurves.EdgeWeight(dx, dy, edge) : AmbientEdgeScale;
                if (weight < .05f) continue;
                float along = directed ? HeroHitFeedbackCurves.EdgeAlong(hx, hy, dx, dy, aspect, edge) : .5f;
                float before = _edgePeak[edge] * HeroHitFeedbackCurves.Flash(now - _edgeAt[edge], EdgeRise, EdgeHold, EdgeFade);
                float hit = strength * weight;
                HeroHitFeedbackCurves.Retrigger(_edgePeak[edge], now - _edgeAt[edge], hit, EdgeRise, EdgeHold, EdgeFade,
                    out float peak, out float age);
                // Место пятна — к более сильному удару; два удара с одной стороны не прыгают.
                _edgeAlong[edge] = before <= 0f ? along : Mathf.Lerp(_edgeAlong[edge], along, hit / (hit + before));
                _edgePeak[edge] = peak;
                _edgeAt[edge] = now - age;
            }
        }

        /// <summary>
        /// Микростоп: бьющий рядом держит позу удара (стоп-кадр тела, как у тяжёлого убийства), камера
        /// на то же время держит толчок. Sim, ввод и тики не трогаются.
        /// </summary>
        private void MicroStop(Simulation sim, int source, float now)
        {
            _lastStopAt = now;
            EntityStore entities = sim.Entities;
            if (_arena != null && source >= 0 && source != Simulation.PlayerId && source < entities.Count && entities.Alive[source]
                && (_driver.GetRenderPosition(source) - _driver.GetRenderPosition(Simulation.PlayerId)).sqrMagnitude
                   <= MicroStopReach * MicroStopReach)
                _arena.HoldEntityPose(source, MicroStopSeconds);

            if (_cameraJuice == null)
            {
                if (_camera == null) _camera = Camera.main;
                _cameraJuice = _camera != null ? _camera.GetComponent<CombatCameraJuice>() : null;
            }
            if (_cameraJuice != null)
            {
                _cameraJuice.AddImpulse(MicroStopTrauma, MicroStopZoom);
                _cameraJuice.Hold(MicroStopSeconds);
            }
        }

        private void Render(float now)
        {
            float flash = GameUserSettings.FlashScale;
            float width = Mathf.Max(1f, Screen.width), height = Mathf.Max(1f, Screen.height);
            float across = EdgeDepth * height / width;   // глубина левого и правого краёв, доли ширины
            float alongX = EdgeLength * height / width;  // длина пятна нижнего и верхнего, доли ширины

            for (int edge = 0; edge < _edges.Length; edge++)
            {
                RawImage image = _edges[edge];
                float value = _edgePeak[edge] * HeroHitFeedbackCurves.Flash(now - _edgeAt[edge], EdgeRise, EdgeHold, EdgeFade)
                              * EdgeAlpha * flash * (edge == HeroHitFeedbackCurves.EdgeBottom ? BottomEdgeScale : 1f);
                bool on = value > .003f;
                if (image.enabled != on) image.enabled = on;
                if (!on) { if (_edgePeak[edge] > 0f && now - _edgeAt[edge] > EdgeRise + EdgeHold + EdgeFade) _edgePeak[edge] = 0f; continue; }

                Color color = EdgeColor;
                color.a = value;
                image.color = color;
                float a = _edgeAlong[edge];
                RectTransform rect = image.rectTransform;
                switch (edge)
                {
                    case HeroHitFeedbackCurves.EdgeLeft:
                        Place(rect, 0f, a - EdgeLength * .5f, across, a + EdgeLength * .5f);
                        break;
                    case HeroHitFeedbackCurves.EdgeRight:
                        Place(rect, 1f - across, a - EdgeLength * .5f, 1f, a + EdgeLength * .5f);
                        break;
                    case HeroHitFeedbackCurves.EdgeBottom:
                        Place(rect, a - alongX * .5f, 0f, a + alongX * .5f, EdgeDepth);
                        break;
                    default:
                        Place(rect, a - alongX * .5f, 1f - EdgeDepth, a + alongX * .5f, 1f);
                        break;
                }
            }

            float breath = HeroHitFeedbackCurves.VignetteBreath(now, PortraitPulsePeriod);
            float vignette = _vignetteWeight * VignetteAlpha * flash * Mathf.Lerp(1f - BreathDepth, 1f, breath);
            bool shown = vignette > .003f;
            if (_vignette.enabled != shown) _vignette.enabled = shown;
            if (shown)
            {
                Color color = VignetteColor;
                color.a = vignette;
                _vignette.color = color;
            }
        }

        private static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            var min = new Vector2(xMin, yMin);
            var max = new Vector2(xMax, yMax);
            if (rect.anchorMin != min) rect.anchorMin = min;
            if (rect.anchorMax != max) rect.anchorMax = max;
        }

        /// <summary>
        /// Холст поверх мира и под HUD, без приёма кликов (ни одного луча, ни одного блокирования).
        /// Четыре кромки и виньетка — RawImage с текстурами, собранными один раз.
        /// </summary>
        private void Build()
        {
            var root = new GameObject("Отклик удара по герою", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasOrder;

            _vignetteTexture = VignetteTexture(VignetteBottom);
            _edgeAcrossX = EdgeTexture(true);
            _edgeAcrossY = EdgeTexture(false);

            _vignette = Image(root.transform, "Виньетка низкого здоровья", _vignetteTexture);
            RectTransform full = _vignette.rectTransform;
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;

            _edges[HeroHitFeedbackCurves.EdgeLeft] = Image(root.transform, "Кромка слева", _edgeAcrossX);
            _edges[HeroHitFeedbackCurves.EdgeRight] = Image(root.transform, "Кромка справа", _edgeAcrossX);
            _edges[HeroHitFeedbackCurves.EdgeRight].uvRect = new Rect(1f, 0f, -1f, 1f);
            _edges[HeroHitFeedbackCurves.EdgeBottom] = Image(root.transform, "Кромка снизу", _edgeAcrossY);
            _edges[HeroHitFeedbackCurves.EdgeTop] = Image(root.transform, "Кромка сверху", _edgeAcrossY);
            _edges[HeroHitFeedbackCurves.EdgeTop].uvRect = new Rect(0f, 1f, 1f, -1f);
        }

        private static RawImage Image(Transform parent, string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.enabled = false;
            RectTransform rect = image.rectTransform;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }

        /// <summary>
        /// Кромка: белая, прозрачность — от края внутрь (квадратично, мягко) и колоколом вдоль края
        /// (к концам пятна — ноль). <paramref name="acrossX"/> — поперёк по x (левый край; правый —
        /// отражение uvRect), иначе поперёк по y (нижний; верхний — отражение).
        /// </summary>
        private static Texture2D EdgeTexture(bool acrossX)
        {
            const int Across = 64, Along = 128;
            int w = acrossX ? Across : Along, h = acrossX ? Along : Across;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = acrossX ? "Кромка удара · поперёк x" : "Кромка удара · поперёк y",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = ((acrossX ? x : y) + .5f) / Across;   // 0 — у края экрана, 1 — внутри
                float v = ((acrossX ? y : x) + .5f) / Along;    // вдоль края
                float inward = 1f - u;
                float fall = inward * inward * (3f - 2f * inward) * inward;
                float bell = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.6f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(fall * bell)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Виньетка: эллипс по форме экрана (текстура растянута на весь холст — 16:9 и 16:10 одинаково),
        /// середина чистая, к углам мягко темнеет; нижняя полоса тише в <paramref name="bottom"/> раз —
        /// там HUD и портрет со своей красной дымкой.
        /// </summary>
        private static Texture2D VignetteTexture(float bottom)
        {
            const int Size = 128;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Виньетка низкого здоровья",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + .5f) / Size * 2f - 1f;
                float v = (y + .5f) / Size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float k = Mathf.Clamp01((r - .62f) / (1.38f - .62f));
                float a = k * k * (3f - 2f * k);
                float low = Mathf.SmoothStep(0f, 1f, (y + .5f) / (Size * .3f));
                a *= Mathf.Lerp(Mathf.Clamp01(bottom), 1f, low);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
