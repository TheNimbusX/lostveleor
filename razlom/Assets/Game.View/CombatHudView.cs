using Game.Sim;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Боевой HUD на Canvas: префаб Resources/UI/Prefabs/CombatHud.
    ///
    /// ВИД ПРАВИТСЯ В ПРЕФАБЕ, СМЫСЛ — ЗДЕСЬ. Скрипт только кладёт числа,
    /// иконки и включает части; позиции, размеры, цвета и шрифты он не
    /// трогает, поэтому ручная правка префаба в Unity не перетирается кодом.
    /// Исключения два: подсказка встаёт над плиткой под мышью, а заливки
    /// полос двигают anchorMax.x — у 9-slice спрайта режим Filled рвёт углы.
    ///
    /// Кликов Canvas не ловит (Raycast выключен, EventSystem не нужен): мышь
    /// разбирает TickDriver через <see cref="HitTest"/>, как и раньше.
    /// </summary>
    public sealed class CombatHudView : MonoBehaviour
    {
        [Header("Герой")]
        public RectTransform HeroPanel;
        public RawImage Portrait;
        public TMP_Text Level;
        public TMP_Text HeroName;
        public RectTransform HealthFill;
        public TMP_Text HealthText;
        public GameObject LavidiumRow;
        public RectTransform LavidiumFill;
        public TMP_Text LavidiumText;

        [Header("Способности")]
        public RectTransform AbilityPanel;
        public HudSlotWidget[] Slots = new HudSlotWidget[4];
        public RectTransform DashPanel;
        public HudSlotWidget Dash;
        public RectTransform ExperienceHit;
        public RectTransform ExperienceFill;
        public TMP_Text ExperienceText;

        [Header("Зелья")]
        public RectTransform PotionPanel;

        [Header("Подсказка")]
        public RectTransform Tooltip;
        public RawImage TooltipIcon;
        public TMP_Text TooltipTitle;
        public TMP_Text TooltipKey;
        public TMP_Text TooltipBody;
        public HudTooltipMetric[] TooltipMetrics = new HudTooltipMetric[6];
        public TMP_Text TooltipStatus;
        [Tooltip("Хвостик под карточкой; по X встаёт напротив плитки")]
        public RectTransform TooltipTail;
        [Tooltip("Столбцов в сетке параметров; черта-разделитель стоит только между столбцами")]
        public int TooltipMetricColumns = 3;
        [Tooltip("Отступ подсказки над панелью способностей, в единицах Canvas")]
        public float TooltipGap = 14f;

        [Header("Отказ при нажатии")]
        public RectTransform Feedback;
        public TMP_Text FeedbackText;

        [Header("Миникарта")]
        public RectTransform MinimapFrame;
        [Tooltip("Прямоугольник, внутри которого рисуется сама карта")]
        public RectTransform MinimapArea;
        [Tooltip("Картинка карты; её обрезает маска той же формы, что и рамка")]
        public RawImage MinimapImage;
        [Tooltip("Приближение содержимого карты: больше — крупнее")]
        public float MinimapZoom = 2.6f;
        [Tooltip("Размер меток и значков на карте относительно HUD")]
        public float MinimapMarkerScale = 1.1f;
        [Tooltip("Подложка меток мест на карте")]
        public Texture2D MinimapMarkerRing;
        [Tooltip("Стрелка героя на карте")]
        public Texture2D MinimapPlayerArrow;
        public RectTransform MinimapCaptionPanel;
        public TMP_Text MinimapCaption;

        [Header("Иконки параметров: сердце, лавидий, время, урон, дальность, радиус, длительность")]
        public Sprite[] StatIcons = new Sprite[7];

        [Header("Цвета состояний")]
        public Color HealthNormal = new Color32(0xE0, 0x6A, 0x6E, 0xFF);
        public Color HealthLow = new Color32(0xF0, 0x7A, 0x3C, 0xFF);
        public Color KeyNormal = new Color32(0xC8, 0x3C, 0x55, 0xFF);
        public Color Denied = new Color32(0xF0, 0x7A, 0x3C, 0xFF);
        public Color ArtDimmed = new Color(.62f, .66f, .74f, 1f);

        /// <summary>
        /// Версия раскладки сборщика, из которой сделан префаб. Сборщик
        /// пересобирает префаб, только если его версия новее — поднимается
        /// лишь с согласия владельца, потому что стирает ручные правки.
        /// </summary>
        [HideInInspector] public int LayoutVersion;

        const int DashSlot = Simulation.AbilitySlots - 1;
        readonly float[] _pressUntil = new float[Simulation.AbilitySlots];
        readonly int[] _iconIds = new int[Simulation.AbilitySlots];
        readonly PlayerHud.TooltipValue[] _values = new PlayerHud.TooltipValue[8];
        Canvas _canvas;
        Image _healthImage;
        int _health = -1, _maxHealth = -1, _lavidium = -1, _maxLavidium = -1, _level = -1, _xp = -1, _xpMax = -1;
        bool _xpHovered;
        int _feedbackSlot = -1;
        float _feedbackUntil;
        HudAbilityBlock _feedbackBlock;
        string _feedbackMessage;
        int _tooltipShown = -2, _tooltipDefinition = -1;
        string _tooltipStatusShown;

        /// <summary>Плитка под мышью; -1 — ни одной. PlayerHud рисует по ней радиус.</summary>
        public int HoverSlot { get; private set; } = -1;
        internal HudAbilityBlock LastFeedbackBlock => _feedbackBlock;

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            // «Масштаб интерфейса» из настроек паузы; эталон CanvasScaler берётся из префаба.
            if (GetComponent<UnityEngine.UI.CanvasScaler>() != null && GetComponent<UiScaleFollower>() == null)
                gameObject.AddComponent<UiScaleFollower>();
            if (HealthFill != null) _healthImage = HealthFill.GetComponent<Image>();
            for (int i = 0; i < _iconIds.Length; i++) _iconIds[i] = int.MinValue;
            if (Tooltip != null) Tooltip.gameObject.SetActive(false);
            if (Feedback != null) Feedback.gameObject.SetActive(false);
        }

        float Scale => _canvas != null ? _canvas.scaleFactor : 1f;

        /// <summary>Мышь в координатах экрана, начало снизу слева.</summary>
#if ENABLE_INPUT_SYSTEM
        static Vector2 Pointer => Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1f, -1f);
#else
        static Vector2 Pointer => Input.mousePosition;
#endif

        internal void Refresh(Simulation sim, Camp camp, TickDriver driver)
        {
            Vector2 pointer = Pointer;
            RefreshHero(sim, camp);
            RefreshExperience(camp, pointer);
            HoverSlot = -1;
            for (int slot = 0; slot < DashSlot && slot < Slots.Length; slot++)
                RefreshSlot(sim, slot, Slots[slot], pointer);
            bool hasDash = sim.GetAbility(DashSlot) != null;
            if (DashPanel != null) DashPanel.gameObject.SetActive(hasDash);
            if (hasDash) RefreshSlot(sim, DashSlot, Dash, pointer);
            RefreshTooltip(sim, driver);
            RefreshFeedback();
        }

        void RefreshHero(Simulation sim, Camp camp)
        {
            if (Portrait != null && Portrait.texture == null)
                Portrait.texture = Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout");
            int level = camp != null ? camp.Level : 1;
            if (level != _level && Level != null)
            {
                // Первый показ — не повышение: _level стартует нулём, и звук сыграл бы при входе в игру.
                if (_level > 0 && level > _level) GameSound.Play("level_up", .9f);
                _level = level;
                Level.text = level.ToString();
            }

            int health = Mathf.Max(0, sim.Entities.Health[Simulation.PlayerId]);
            int max = Mathf.Max(1, sim.Entities.MaxHealth[Simulation.PlayerId]);
            if (health != _health || max != _maxHealth)
            {
                _health = health; _maxHealth = max;
                float ratio = Mathf.Clamp01(health / (float)max);
                SetFill(HealthFill, ratio);
                if (_healthImage != null) _healthImage.color = ratio <= .25f ? HealthLow : HealthNormal;
                if (HealthText != null) HealthText.text = health + " / " + max;
            }

            // Сердцебиение на низком здоровье: петля включается и гаснет каждый кадр,
            // поэтому долю считаем здесь, а не внутри проверки «значение изменилось».
            float healthRatio = Mathf.Clamp01(health / (float)max);
            GameSound.Loop(healthRatio > 0f && healthRatio <= .25f ? "low_health_loop" : null, .55f);

            int maxResource = sim.Entities.MaxLavidium[Simulation.PlayerId];
            if (LavidiumRow != null) LavidiumRow.SetActive(maxResource > 0);
            if (maxResource <= 0) return;
            int resource = Mathf.FloorToInt(sim.Entities.Lavidium[Simulation.PlayerId].ToFloat());
            if (resource == _lavidium && maxResource == _maxLavidium) return;
            _lavidium = resource; _maxLavidium = maxResource;
            SetFill(LavidiumFill, resource / (float)maxResource);
            if (LavidiumText != null) LavidiumText.text = resource + " / " + maxResource;
        }

        void RefreshExperience(Camp camp, Vector2 pointer)
        {
            int xp = camp != null ? camp.Experience : 0;
            int next = camp != null ? Mathf.Max(1, camp.ExperienceToNextLevel) : 1;
            bool hovered = HudReviewCapture.Enabled ? HudReviewCapture.HoverXp : Contains(ExperienceHit, pointer);
            if (xp == _xp && next == _xpMax && hovered == _xpHovered) return;
            _xp = xp; _xpMax = next; _xpHovered = hovered;
            SetFill(ExperienceFill, Mathf.Clamp01(xp / (float)next));
            // Подпись «XP» стоит в префабе отдельной плашкой; числа — только под мышью.
            if (ExperienceText != null) ExperienceText.text = hovered && camp != null ? xp + " / " + next : string.Empty;
        }

        void RefreshSlot(Simulation sim, int slot, HudSlotWidget widget, Vector2 pointer)
        {
            if (widget == null) return;
            AbilityBuild build = sim.GetAbility(slot);
            bool empty = build == null;
            if (widget.Art != null) widget.Art.enabled = !empty;
            if (widget.Key != null) widget.Key.transform.parent.gameObject.SetActive(!empty);
            if (empty)
            {
                SetActive(widget.Cooldown, false); SetActive(widget.CooldownText, false);
                SetActive(widget.Highlight, false);
                if (widget.ResourceBadge != null) widget.ResourceBadge.SetActive(false);
                _iconIds[slot] = int.MinValue;
                return;
            }

            if (_iconIds[slot] != build.DefinitionId && widget.Art != null)
            {
                _iconIds[slot] = build.DefinitionId;
                string file = slot == DashSlot ? null : PlayerHud.IconFile(build.DefinitionId);
                Texture2D icon = file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file)
                    : Resources.Load<Texture2D>("UI/HUD/DashSilhouette");
                widget.Art.texture = icon;
                widget.Art.uvRect = slot == DashSlot ? new Rect(0f, 0f, 1f, 1f) : new Rect(.12f, .12f, .76f, .76f);
            }
            if (widget.Key != null)
            {
                string key = PlayerHud.SlotKey(slot);
                if (widget.Key.text != key) widget.Key.text = key;
            }

            HudAbilityAvailability state = PlayerHud.Availability(sim, slot, build);
            bool hovered = HudReviewCapture.Enabled ? HudReviewCapture.HoverSlot == slot
                : CaptureRig.HudTooltipSlot >= 0 ? CaptureRig.HudTooltipSlot == slot
                : !CaptureRig.ForestBudShowcase && Contains(widget.Hit, pointer);
            if (hovered) HoverSlot = slot;

            if (widget.Art != null) widget.Art.color = state.Ready ? Color.white : ArtDimmed;
            bool cooling = state.Block == HudAbilityBlock.Cooldown;
            SetActive(widget.Cooldown, cooling);
            SetActive(widget.CooldownText, cooling);
            if (cooling)
            {
                widget.Cooldown.fillAmount = Mathf.Clamp01(state.RemainingTicks / (float)Mathf.Max(1, sim.AbilityCooldownTicks(build)));
                widget.CooldownText.text = (state.RemainingTicks / (float)Simulation.TicksPerSecond).ToString("0.0");
            }
            bool lacking = state.Block == HudAbilityBlock.Resource;
            if (widget.ResourceBadge != null) widget.ResourceBadge.SetActive(lacking);
            if (lacking && widget.ResourceText != null) widget.ResourceText.text = "−" + state.MissingResource;

            bool denied = _feedbackSlot == slot && _feedbackBlock != HudAbilityBlock.None && Time.unscaledTime < _feedbackUntil;
            SetActive(widget.Highlight, hovered || denied);
            if (widget.Highlight != null)
                widget.Highlight.color = denied ? Color.Lerp(Color.white, Denied, Mathf.Clamp01((_feedbackUntil - Time.unscaledTime) * 2f)) : Color.white;

            float press = Mathf.Clamp01((_pressUntil[slot] - Time.unscaledTime) / .16f);
            if (widget.Body != null) widget.Body.localScale = Vector3.one * (1f - press * .05f);
        }

        void RefreshTooltip(Simulation sim, TickDriver driver)
        {
            if (Tooltip == null) return;
            int slot = HoverSlot;
            AbilityBuild build = slot >= 0 ? sim.GetAbility(slot) : null;
            if (build == null)
            {
                if (_tooltipShown != -1) { Tooltip.gameObject.SetActive(false); _tooltipShown = -1; }
                return;
            }

            HudAbilityAvailability state = PlayerHud.Availability(sim, slot, build);
            string status = driver.AbilityTargetAimSlot == slot ? "Выбери цель · ПКМ — отмена"
                : !state.Ready ? PlayerHud.AvailabilityText(state) : string.Empty;
            bool rebuilt = slot != _tooltipShown || build.DefinitionId != _tooltipDefinition;
            if (rebuilt)
            {
                _tooltipShown = slot; _tooltipDefinition = build.DefinitionId;
                Tooltip.gameObject.SetActive(true);
                if (TooltipIcon != null)
                {
                    string file = PlayerHud.IconFile(build.DefinitionId);
                    TooltipIcon.texture = file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file) : null;
                    TooltipIcon.enabled = TooltipIcon.texture != null;
                    TooltipIcon.uvRect = new Rect(.12f, .12f, .76f, .76f);
                }
                if (TooltipTitle != null) TooltipTitle.text = PlayerHud.AbilityName(build.DefinitionId);
                if (TooltipKey != null) TooltipKey.text = PlayerHud.SlotKey(slot);
                if (TooltipBody != null) TooltipBody.text = PlayerHud.AbilityDescription(build.DefinitionId);
                int count = PlayerHud.CollectTooltipValues(build, _values, sim);
                for (int i = 0; i < TooltipMetrics.Length; i++)
                {
                    HudTooltipMetric metric = TooltipMetrics[i];
                    if (metric == null) continue;
                    bool shown = i < count;
                    metric.gameObject.SetActive(shown);
                    if (!shown) continue;
                    PlayerHud.TooltipValue value = _values[i];
                    int icon = value.Caption == "Длительность" ? 6 : value.Icon;
                    if (metric.Icon != null && (uint)icon < (uint)StatIcons.Length) metric.Icon.sprite = StatIcons[icon];
                    if (metric.Value != null) metric.Value.text = value.Value;
                    if (metric.Separator != null) metric.Separator.SetActive(i % Mathf.Max(1, TooltipMetricColumns) != 0);
                }
                _tooltipStatusShown = null;
            }
            if (TooltipStatus != null && status != _tooltipStatusShown)
            {
                _tooltipStatusShown = status;
                TooltipStatus.text = status;
                TooltipStatus.gameObject.SetActive(status.Length > 0);
                rebuilt = true;
            }
            if (rebuilt) LayoutRebuilder.ForceRebuildLayoutImmediate(Tooltip);
            PlaceTooltip(slot);
        }

        void PlaceTooltip(int slot)
        {
            HudSlotWidget widget = slot == DashSlot ? Dash : Slots[slot];
            if (widget == null || widget.Hit == null) return;
            RectTransform above = slot == DashSlot && DashPanel != null ? DashPanel : AbilityPanel;
            Vector3 center = Center(widget.Hit);
            float top = above != null ? Corners(above)[1].y : Corners(widget.Hit)[1].y;
            float scale = Scale;
            float half = Tooltip.rect.width * .5f * scale;
            float x = Mathf.Clamp(center.x, half + 16f * scale, Screen.width - half - 16f * scale);
            Tooltip.pivot = new Vector2(.5f, 0f);
            Tooltip.position = new Vector3(x, top + TooltipGap * scale, 0f);
            // Карточку прижимает к краю экрана, а хвостик всё равно смотрит на плитку.
            if (TooltipTail != null)
            {
                float limit = half - 24f * scale;
                TooltipTail.position = new Vector3(Mathf.Clamp(center.x, x - limit, x + limit), TooltipTail.position.y, 0f);
            }
        }

        void RefreshFeedback()
        {
            if (Feedback == null) return;
            bool show = HoverSlot < 0 && Time.unscaledTime < _feedbackUntil && !string.IsNullOrEmpty(_feedbackMessage);
            if (Feedback.gameObject.activeSelf != show) Feedback.gameObject.SetActive(show);
            if (show && FeedbackText != null && FeedbackText.text != _feedbackMessage) FeedbackText.text = _feedbackMessage;
        }

        internal void NotifyAbilityPress(int slot, HudAbilityAvailability state)
        {
            if ((uint)slot >= _pressUntil.Length) return;
            _pressUntil[slot] = Time.unscaledTime + .16f;
            _feedbackSlot = slot; _feedbackBlock = state.Block; _feedbackUntil = Time.unscaledTime + 1.25f;
            _feedbackMessage = PlayerHud.AvailabilityText(state);
        }

        /// <summary>Точка экрана (снизу слева) над частью HUD; <paramref name="slot"/> — плитка способности.</summary>
        internal bool HitTest(Vector2 screen, Simulation sim, out int slot)
        {
            slot = -1;
            for (int i = 0; i < Slots.Length && i < DashSlot; i++)
                if (Slots[i] != null && Contains(Slots[i].Hit, screen))
                {
                    // Пустая рамка ловит мышь, но слота не даёт: нажимать нечего.
                    if (sim.GetAbility(i) != null) slot = i;
                    return true;
                }
            if (Dash != null && DashPanel != null && DashPanel.gameObject.activeInHierarchy && Contains(DashPanel, screen))
            { slot = DashSlot; return true; }
            return Contains(HeroPanel, screen) || Contains(AbilityPanel, screen) || Contains(PotionPanel, screen)
                || Contains(MinimapFrame, screen) || Contains(MinimapCaptionPanel, screen);
        }

        /// <summary>Центр плитки в координатах экрана снизу слева — для съёмочного сценария.</summary>
        internal Vector2 SlotCenter(int slot)
        {
            HudSlotWidget widget = slot == DashSlot ? Dash : (uint)slot < (uint)Slots.Length ? Slots[slot] : null;
            return widget != null && widget.Hit != null ? (Vector2)Center(widget.Hit) : Vector2.zero;
        }

        /// <summary>Прямоугольник карты в пикселях экрана с началом сверху слева, как у IMGUI.</summary>
        internal Rect MinimapScreenRect
        {
            get
            {
                if (MinimapArea == null) return Rect.zero;
                Vector3[] c = Corners(MinimapArea);
                return new Rect(c[0].x, Screen.height - c[2].y, c[2].x - c[0].x, c[2].y - c[0].y);
            }
        }

        /// <summary>Низ миникарты вместе с подписью, в пикселях от верха экрана.</summary>
        internal float MinimapBottom
        {
            get
            {
                RectTransform lowest = MinimapCaptionPanel != null ? MinimapCaptionPanel : MinimapFrame;
                return lowest != null ? Screen.height - Corners(lowest)[0].y : 0f;
            }
        }

        internal float CanvasScale => Scale;

        internal void SetMinimapTexture(Texture texture)
        {
            if (MinimapImage == null) return;
            if (MinimapImage.texture != texture) MinimapImage.texture = texture;
            // RawImage без текстуры рисуется белым прямоугольником.
            if (MinimapImage.enabled != (texture != null)) MinimapImage.enabled = texture != null;
        }

        internal void SetMinimapCaption(string caption)
        {
            if (MinimapCaption != null && caption != null && MinimapCaption.text != caption)
                MinimapCaption.text = caption;
        }

        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            Vector2 max = fill.anchorMax;
            if (Mathf.Approximately(max.x, ratio)) return;
            fill.anchorMax = new Vector2(ratio, max.y);
            // Пустая полоса не показывает скруглённые края заливки.
            if (fill.gameObject.activeSelf != ratio > .001f) fill.gameObject.SetActive(ratio > .001f);
        }

        static void SetActive(Component part, bool active)
        {
            if (part != null && part.gameObject.activeSelf != active) part.gameObject.SetActive(active);
        }

        static readonly Vector3[] Buffer = new Vector3[4];

        static Vector3[] Corners(RectTransform rect)
        {
            rect.GetWorldCorners(Buffer);
            return Buffer;
        }

        static Vector3 Center(RectTransform rect)
        {
            Vector3[] c = Corners(rect);
            return (c[0] + c[2]) * .5f;
        }

        static bool Contains(RectTransform rect, Vector2 screen)
            => rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);
    }
}
