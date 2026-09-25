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
    public sealed partial class CombatHudView : MonoBehaviour
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
        [Tooltip("Необязательно: область героя; числа здоровья и лавидия видны внутри полос только под мышью (владелец, 23 сентября). Пусто — видны всегда")]
        public RectTransform VitalsHit;
        [Tooltip("Необязательно: общая полоса-подложка HUD (вариант B) — ловит мышь, чтобы клик по ней не уходил в мир")]
        public RectTransform Strip;

        [Header("Способности")]
        public RectTransform AbilityPanel;
        [Tooltip("Сколько срезать с каждого края иконки способности. Старые иконки несли свою рамку (0,12); новые — без рамки")]
        [Range(0f, .3f)] public float IconCrop = .12f;
        public HudSlotWidget[] Slots = new HudSlotWidget[4];
        public RectTransform DashPanel;
        public HudSlotWidget Dash;
        public RectTransform ExperienceHit;
        public RectTransform ExperienceFill;
        public TMP_Text ExperienceText;
        [Tooltip("Высота полосы опыта под мышью, чтобы числа встали внутрь неё. 0 — полоса не растёт (старый префаб)")]
        public float ExperienceHoverHeight;
        [Tooltip("Секунд на рост полосы опыта")] public float ExperienceGrowTime = .12f;

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
        [Tooltip("Необязательно: ряд ромбиков усилений в подсказке (8) — первые N залиты")]
        public Image[] TooltipUpgradePips = new Image[0];
        [Tooltip("Необязательно: «Усилений: N из 8»")] public TMP_Text TooltipUpgradeText;
        [Tooltip("Строка усилений целиком — прячется у кувырка и способностей без усилений")] public GameObject TooltipUpgradeRow;
        public Sprite UpgradePipFilled, UpgradePipEmpty;
        [Tooltip("Хвостик под карточкой; по X встаёт напротив плитки")]
        public RectTransform TooltipTail;
        [Tooltip("Столбцов в сетке параметров; черта-разделитель стоит только между столбцами")]
        public int TooltipMetricColumns = 3;
        [Tooltip("Отступ подсказки над панелью способностей, в единицах Canvas")]
        public float TooltipGap = 14f;

        [Header("Отказ при нажатии")]
        public RectTransform Feedback;
        public TMP_Text FeedbackText;
        [Tooltip("Необязательно: большой баннер «Новый уровень» сверху по центру")] public HudLevelBanner LevelBanner;
        [Tooltip("Затемнение мира на смене арены: новая арена проявляется из темноты")] public Image ArenaFade;
        [Tooltip("Необязательно: значки действующих эффектов зелий над героем — Живица и Порыв")]
        public HudBuffChip ResinChip, SurgeChip;
        [Tooltip("Необязательно: медальон артефакта забега на рамке портрета — виден, только пока артефакт есть")]
        public GameObject ArtifactSlot;
        public RawImage ArtifactIcon;
        [Tooltip("Область медальона для наведения — показывает подсказку артефакта")] public RectTransform ArtifactHit;
        [Tooltip("Необязательно: вуаль перезарядки на медальоне (Image Filled, радиальный)")] public Image ArtifactCooldown;
        [Tooltip("Необязательно: клавиша артефакта на медальоне — только у активных")] public TMP_Text ArtifactKey;
        [Tooltip("Необязательно: свет медальона, пока действует включённый артефакт")] public HudPulse ArtifactActive;
        [Tooltip("Необязательно: вспышка медальона в момент готовности и включения (аддитивная)")] public Graphic ArtifactFlash;

        [Header("Живость (24 сентября: «нет живости в HUD», только по событиям)")]
        [Tooltip("Красная дымка у героя: пульсирует, пока здоровья ≤ 25%")] public HudPulse DangerPulse;
        [Tooltip("Свет на банке здоровья: здоровья < 40%, а зелье есть")] public HudPulse HealthPotionPulse;
        [Tooltip("Полоса здоровья: вздрагивает от удара")] public RectTransform HealthBar;
        [Tooltip("Вспышка по полосе здоровья от удара (аддитивная)")] public Graphic HealthFlash;
        [Tooltip("Вспышка у ромба уровня на портрете (аддитивная)")] public Graphic LevelFlare;
        [Tooltip("Проблеск по полосе опыта, когда опыт прибавился")] public HudGlint ExperienceGlint;
        [Tooltip("Редкий проблеск по полному лавидию")] public HudGlint LavidiumGlint;

        [Header("Миникарта")]
        public RectTransform MinimapFrame;
        [Tooltip("Прямоугольник, внутри которого рисуется сама карта")]
        public RectTransform MinimapArea;
        [Tooltip("Картинка карты; её обрезает маска той же формы, что и рамка")]
        public RawImage MinimapImage;
        [Tooltip("Приближение содержимого карты: больше — крупнее")]
        public float MinimapZoom = 2.6f;
        [Tooltip("Метки, герой, туман и подпись при наведении — на холсте, не IMGUI")]
        public HudMinimapMarks MinimapMarks;
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
        bool _xpHovered, _vitalsHovered;
        float _xpRestHeight = -1f;
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

        Rect IconRect => new Rect(IconCrop, IconCrop, 1f - IconCrop * 2f, 1f - IconCrop * 2f);

        /// <summary>Мышь в координатах экрана, начало снизу слева.</summary>
#if ENABLE_INPUT_SYSTEM
        static Vector2 Pointer => Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1f, -1f);
#else
        static Vector2 Pointer => Input.mousePosition;
#endif

        readonly int[] _upgrades = new int[8];
        int _tooltipUpgrades = -1;
        RunArtifact _artifact = (RunArtifact)255;
        bool _artifactHovered;
        const int ArtifactTooltip = -2;

        /// <summary>
        /// Сколько усилений у способности в слоте: взятые в забеге (или в наборе лагеря) плюс
        /// включённые в меню разработчика. Порядок не важен — только число (ромбик и подсказка).
        /// </summary>
        static int UpgradesAt(TickDriver driver, int slot)
        {
            RunLoadout loadout = driver != null && driver.Session != null ? driver.Session.ActiveLoadout : null;
            if (loadout == null) return 0;
            int pool = loadout.PoolIndexAt(slot);
            if (!SabreTalents.TryLineOf(pool, out SabreTalentLine line)) return 0;
            int mask = loadout.TalentMask(pool);
            for (int index = 0; index < SabreTalents.TalentsPerLine; index++)
                if (DeveloperTalents.Has(line, index)) mask |= 1 << index;
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        internal void Refresh(Simulation sim, Camp camp, TickDriver driver)
        {
            Vector2 pointer = Pointer;
            // Момент арены — раньше уровня: «Разлом зачищен» причина, новый уровень — следствие.
            RefreshMoments(driver);
            for (int slot = 0; slot < _upgrades.Length; slot++)
                _upgrades[slot] = slot < DashSlot ? UpgradesAt(driver, slot) : 0;
            RefreshHero(sim, camp, pointer);
            RefreshExperience(camp, pointer);
            HoverSlot = -1;
            for (int slot = 0; slot < DashSlot && slot < Slots.Length; slot++)
                RefreshSlot(sim, slot, Slots[slot], pointer);
            bool hasDash = sim.GetAbility(DashSlot) != null;
            if (DashPanel != null) DashPanel.gameObject.SetActive(hasDash);
            if (hasDash) RefreshSlot(sim, DashSlot, Dash, pointer);
            RefreshTooltip(sim, driver);
            RefreshFeedback();
            RefreshPotions(camp,driver);
            RefreshArtifact(driver, Pointer);
            RefreshToasts(driver);
            if (ResinChip != null) ResinChip.Set(sim.ResinTicksLeft, Simulation.PotionEffectTicks, Simulation.TicksPerSecond);
            if (SurgeChip != null) SurgeChip.Set(sim.SurgeTicksLeft, Simulation.PotionEffectTicks, Simulation.TicksPerSecond);
        }

        int _momentDepth = -1;
        RunPhase _momentPhase = RunPhase.Idle;

        /// <summary>
        /// Моменты забега на плашке уровня (аудит UI, этап 2): вход на арену — «Разлом 3 из 10» и что
        /// в ней ждёт, последний враг — «Разлом зачищен, путь открыт». Раньше арена сменялась склейкой,
        /// а зачистку отмечала только строка в углу.
        /// </summary>
        void RefreshMoments(TickDriver driver)
        {
            RiftRun run = driver.Session != null && driver.Session.Mode == GameMode.Rift ? driver.Run : null;
            if (run == null || run.Map == null) { _momentDepth = -1; _momentPhase = RunPhase.Idle; return; }
            if (run.Depth != _momentDepth && run.Phase == RunPhase.Clearing)
            {
                bool next = _momentDepth > 0;
                _momentDepth = run.Depth;
                _momentPhase = run.Phase;
                ArenaIntro(run, next);
                return;
            }
            if (run.Phase == _momentPhase) return;
            if (_momentPhase == RunPhase.Clearing && run.Phase == RunPhase.SeekingExit) ArenaCleared(run);
            _momentPhase = run.Phase;
        }

        void ArenaIntro(RiftRun run, bool next)
        {
            // Первую арену открывает вспышка арки (CampTransition); следующие — выход из темноты.
            if (next && ArenaFade != null) HudFx.Flash(ArenaFade, .92f, .55f);
            GameSound.Play("rift_whoosh", .55f, .03f, .5f);
            if (LevelBanner == null) return;
            int fights = 0;
            if (run.Encounters != null)
                for (int e = 0; e < run.Encounters.Count; e++)
                    if (run.Encounters.Get(e).Role != EncounterRole.RewardBranch) fights++;
            int caches = run.Map.RewardBranchCount;
            var lines = new System.Collections.Generic.List<string>(3);
            if (fights > 0) lines.Add(Gold(fights) + " " + Plural(fights, "встреча", "встречи", "встреч"));
            if (caches > 0) lines.Add(Gold(caches) + " " + Plural(caches, "тайник", "тайника", "тайников"));
            if (run.IsFinalLevel) lines.Add("Последний разлом");
            else if (run.BossId >= 0) lines.Add("Здесь ждёт босс");
            string total = run.TotalLevels > 0 ? " ИЗ " + run.TotalLevels : string.Empty;
            LevelBanner.ShowMoment(Mathf.Max(1, run.Depth - 1).ToString(), run.Depth.ToString(), "РАЗЛОМ" + total, lines.ToArray(), 2.1f);
        }

        void ArenaCleared(RiftRun run)
        {
            GameSound.Play("arena_cleared", .8f, 0f, 1f);
            GameSound.Play("map_ping", .45f, .02f, .5f);
            // Концепт 2Б: зачистка — узкий баннер сверху, плашка уровня остаётся уровню и входу на арену.
            if (Announce != null) { Announce.Show("РАЗЛОМ ЗАЧИЩЕН", "Путь к выходу открыт"); return; }
            if (LevelBanner == null) return;
            string[] lines =
            {
                "Путь к выходу открыт",
                run.Gold > 0 ? Gold(run.Gold) + " золота в забеге" : string.Empty,
            };
            LevelBanner.ShowMoment(run.Depth.ToString(), run.Depth.ToString(), "РАЗЛОМ ЗАЧИЩЕН", lines, 1.8f);
        }

        static string Gold(int value) => "<color=#FFD27A>" + value + "</color>";

        static string Plural(int n, string one, string few, string many)
        {
            int tens = n % 100, units = n % 10;
            if (tens >= 11 && tens <= 14) return many;
            return units == 1 ? one : units >= 2 && units <= 4 ? few : many;
        }

        void RefreshHero(Simulation sim, Camp camp, Vector2 pointer)
        {
            if (Portrait != null && Portrait.texture == null)
                Portrait.texture = Resources.Load<Texture2D>("UI/HUD/PelagPortraitPainted") ?? Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout");
            int level = camp != null ? camp.Level : 1;
            if (level != _level && Level != null)
            {
                // Первый показ — не повышение: _level стартует нулём, и звук сыграл бы при входе в игру.
                if (_level > 0 && level > _level)
                {
                    GameSound.Play("level_up", .9f);
                    if (LevelBanner != null) LevelBanner.Show(level);
                    if (LevelFlare != null) HudFx.Burst(LevelFlare, 1f, .6f, 1.6f, .7f);
                    HudFx.Punch(Level.transform, 1.6f, .45f);
                }
                _level = level;
                Level.text = level.ToString();
            }

            int health = Mathf.Max(0, sim.Entities.Health[Simulation.PlayerId]);
            int max = Mathf.Max(1, sim.Entities.MaxHealth[Simulation.PlayerId]);
            bool vitals = VitalsHit == null || (HudReviewCapture.Enabled ? HudReviewCapture.HoverXp : Contains(VitalsHit, pointer));
            bool hoverChanged = vitals != _vitalsHovered;
            _vitalsHovered = vitals;
            if (health != _health || max != _maxHealth || hoverChanged)
            {
                // Удар по герою: полоса вздрагивает и вспыхивает. Первый показ — не удар.
                if (_health > 0 && health < _health)
                {
                    HudFx.Flash(HealthFlash, .8f, .35f);
                    HudFx.Shake(HealthBar, 3f, .28f);
                }
                _health = health; _maxHealth = max;
                float ratio = Mathf.Clamp01(health / (float)max);
                SetFill(HealthFill, ratio);
                if (_healthImage != null) _healthImage.color = ratio <= .25f ? HealthLow : HealthNormal;
                if (HealthText != null) HealthText.text = vitals ? health + " / " + max : string.Empty;
            }

            // Сердцебиение на низком здоровье: петля включается и гаснет каждый кадр,
            // поэтому долю считаем здесь, а не внутри проверки «значение изменилось».
            float healthRatio = Mathf.Clamp01(health / (float)max);
            GameSound.Loop(healthRatio > 0f && healthRatio <= .25f ? "low_health_loop" : null, .55f);
            if (DangerPulse != null) DangerPulse.Active = healthRatio > 0f && healthRatio <= .25f;
            if (HealthPotionPulse != null)
                HealthPotionPulse.Active = healthRatio > 0f && healthRatio < .4f && camp != null && camp.PotionCount(camp.SelectedPotion(0)) > 0;

            int maxResource = sim.Entities.MaxLavidium[Simulation.PlayerId];
            if (LavidiumRow != null) LavidiumRow.SetActive(maxResource > 0);
            if (maxResource <= 0) return;
            int resource = Mathf.FloorToInt(sim.Entities.Lavidium[Simulation.PlayerId].ToFloat());
            if (resource == _lavidium && maxResource == _maxLavidium && !hoverChanged) return;
            _lavidium = resource; _maxLavidium = maxResource;
            if (LavidiumGlint != null) LavidiumGlint.Repeat = resource >= maxResource;
            SetFill(LavidiumFill, resource / (float)maxResource);
            if (LavidiumText != null) LavidiumText.text = vitals ? resource + " / " + maxResource : string.Empty;
        }

        void RefreshExperience(Camp camp, Vector2 pointer)
        {
            int xp = camp != null ? camp.Experience : 0;
            int next = camp != null ? Mathf.Max(1, camp.ExperienceToNextLevel) : 1;
            bool hovered = HudReviewCapture.Enabled ? HudReviewCapture.HoverXp : Contains(ExperienceHit, pointer) || VitalsHit != null && Contains(VitalsHit, pointer);
            GrowExperience(hovered);
            if (xp == _xp && next == _xpMax && hovered == _xpHovered) return;
            if (_xp >= 0 && (xp > _xp || next != _xpMax) && ExperienceGlint != null) ExperienceGlint.Play();
            _xp = xp; _xpMax = next; _xpHovered = hovered;
            SetFill(ExperienceFill, Mathf.Clamp01(xp / (float)next));
            // Подпись «XP» стоит в префабе отдельной плашкой; числа — только под мышью.
            if (ExperienceText != null) ExperienceText.text = hovered && camp != null ? xp + " / " + next : string.Empty;
        }

        /// <summary>Полоса опыта под мышью плавно подрастает, чтобы числа поместились внутри.</summary>
        void GrowExperience(bool hovered)
        {
            if (ExperienceHoverHeight <= 0f || ExperienceHit == null) return;
            Vector2 size = ExperienceHit.sizeDelta;
            if (_xpRestHeight < 0f) _xpRestHeight = size.y;
            float target = hovered ? ExperienceHoverHeight : _xpRestHeight;
            if (Mathf.Approximately(size.y, target)) return;
            float speed = Mathf.Abs(ExperienceHoverHeight - _xpRestHeight) / Mathf.Max(.01f, ExperienceGrowTime);
            ExperienceHit.sizeDelta = new Vector2(size.x, Mathf.MoveTowards(size.y, target, speed * Time.unscaledDeltaTime));
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
                // У кувырка своя иконка (Icon_Dash, 23 сентября); силуэт — только запасной.
                string file = PlayerHud.IconFile(build.DefinitionId);
                Texture2D icon = file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file) : null;
                widget.Art.texture = icon != null ? icon : Resources.Load<Texture2D>("UI/HUD/DashSilhouette");
                widget.Art.uvRect = icon != null ? IconRect : new Rect(0f, 0f, 1f, 1f);
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
                float left = Mathf.Clamp01(state.RemainingTicks / (float)Mathf.Max(1, sim.AbilityCooldownTicks(build)));
                widget.Cooldown.fillAmount = left;
                if (widget.CooldownRing != null) widget.CooldownRing.fillAmount = left;
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
            // Состояния по листу HUD: готово — бирюзовое свечение, нажата — оранжевая вспышка.
            bool flash = Time.unscaledTime < _pressUntil[slot] + .2f;
            if (widget.ReadyGlow != null && widget.ReadyGlow.activeSelf != (state.Ready && !flash)) widget.ReadyGlow.SetActive(state.Ready && !flash);
            if (widget.ReadyGem != null)
            {
                widget.ReadyGem.SetReady(state.Ready);
                widget.ReadyGem.SetHover(hovered);
                widget.ReadyGem.SetUpgrades(slot < _upgrades.Length ? _upgrades[slot] : 0);
            }
            if (widget.PressGlow != null && widget.PressGlow.activeSelf != flash) widget.PressGlow.SetActive(flash);
        }

        /// <summary>Медальон артефакта у портрета: только в забеге и только пока артефакт есть.</summary>
        void RefreshArtifact(TickDriver driver, Vector2 pointer)
        {
            RunArtifact artifact = driver != null && driver.Session != null && driver.Session.Mode == GameMode.Rift && driver.Run != null
                ? driver.Run.Artifact : RunArtifact.None;
            if (artifact != _artifact)
            {
                _artifact = artifact;
                if (ArtifactSlot != null) ArtifactSlot.SetActive(artifact != RunArtifact.None);
                if (ArtifactIcon != null) ArtifactIcon.texture = RunArtifactTexts.Icon(artifact);
                if (_tooltipShown == ArtifactTooltip) _tooltipShown = -1;
                bool active = Simulation.IsActiveArtifact(artifact);
                if (ArtifactKey != null)
                {
                    ArtifactKey.transform.parent.gameObject.SetActive(active);
                    ArtifactKey.text = GameKeyBindings.Label(GameAction.UseArtifact);
                }
                _artifactWasReady = true;
            }
            _artifactHovered = artifact != RunArtifact.None && ArtifactHit != null && Contains(ArtifactHit, pointer);
            if (artifact == RunArtifact.None || driver == null) return;

            // Перезарядка — вуаль по кругу; готов — короткая вспышка; действует — медальон светится.
            Simulation sim = driver.Run != null ? driver.Run.Sim : null;
            if (sim == null) return;
            int total = Simulation.ArtifactCooldownTicks(artifact);
            int left = total > 0 ? Mathf.Max(0, sim.ArtifactReadyTick - sim.Tick) : 0;
            if (ArtifactCooldown != null)
            {
                ArtifactCooldown.enabled = left > 0;
                ArtifactCooldown.fillAmount = total > 0 ? left / (float)total : 0f;
            }
            bool ready = left <= 0;
            if (ready && !_artifactWasReady) HudFx.Burst(ArtifactFlash, 1f, .7f, 1.5f, .6f);
            _artifactWasReady = ready;
            if (ArtifactActive != null) ArtifactActive.Active = sim.ArtifactEffectActive;
        }

        bool _artifactWasReady = true;

        /// <summary>Подсказка артефакта — та же карточка, что у способностей, без клавиши и параметров.</summary>
        void ShowArtifactTooltip()
        {
            if (_tooltipShown != ArtifactTooltip)
            {
                _tooltipShown = ArtifactTooltip;
                _tooltipDefinition = int.MinValue;
                Tooltip.gameObject.SetActive(true);
                if (TooltipIcon != null)
                {
                    TooltipIcon.texture = RunArtifactTexts.Icon(_artifact);
                    TooltipIcon.enabled = TooltipIcon.texture != null;
                    TooltipIcon.uvRect = new Rect(0f, 0f, 1f, 1f);
                }
                if (TooltipTitle != null) TooltipTitle.text = RunArtifactTexts.Name(_artifact);
                if (TooltipKey != null) TooltipKey.transform.parent.gameObject.SetActive(false);
                if (TooltipBody != null) TooltipBody.text = RunArtifactTexts.Effect(_artifact);
                foreach (HudTooltipMetric metric in TooltipMetrics) if (metric != null) metric.gameObject.SetActive(false);
                if (TooltipUpgradeRow != null) TooltipUpgradeRow.SetActive(false);
                if (TooltipStatus != null)
                {
                    TooltipStatus.text = RunArtifactTexts.Use(_artifact) + "\nАртефакт забега · пропадёт с концом забега";
                    TooltipStatus.gameObject.SetActive(true);
                }
                _tooltipStatusShown = null;
                LayoutRebuilder.ForceRebuildLayoutImmediate(Tooltip);
            }
            Vector3 center = Center(ArtifactHit);
            float top = Corners(ArtifactHit)[1].y;
            float scale = Scale;
            float half = Tooltip.rect.width * .5f * scale;
            float x = Mathf.Clamp(center.x, half + 16f * scale, Screen.width - half - 16f * scale);
            Tooltip.pivot = new Vector2(.5f, 0f);
            Tooltip.position = new Vector3(x, top + TooltipGap * scale, 0f);
            if (TooltipTail != null)
            {
                float limit = half - 24f * scale;
                TooltipTail.position = new Vector3(Mathf.Clamp(center.x, x - limit, x + limit), TooltipTail.position.y, 0f);
            }
        }

        void RefreshTooltip(Simulation sim, TickDriver driver)
        {
            if (Tooltip == null) return;
            if (_artifactHovered) { ShowArtifactTooltip(); return; }
            if (_tooltipShown == ArtifactTooltip && TooltipKey != null) TooltipKey.transform.parent.gameObject.SetActive(true);
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
            int upgrades = slot < _upgrades.Length ? _upgrades[slot] : 0;
            bool rebuilt = slot != _tooltipShown || build.DefinitionId != _tooltipDefinition || upgrades != _tooltipUpgrades;
            if (rebuilt)
            {
                _tooltipShown = slot; _tooltipDefinition = build.DefinitionId; _tooltipUpgrades = upgrades;
                RefreshTooltipUpgrades(slot, driver, upgrades);
                Tooltip.gameObject.SetActive(true);
                if (TooltipIcon != null)
                {
                    string file = PlayerHud.IconFile(build.DefinitionId);
                    TooltipIcon.texture = file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file) : null;
                    TooltipIcon.enabled = TooltipIcon.texture != null;
                    TooltipIcon.uvRect = IconRect;
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

        /// <summary>Ряд из 8 ромбиков и «Усилений: N из 8» (подсказка из концепта 1-gem-ingame).</summary>
        void RefreshTooltipUpgrades(int slot, TickDriver driver, int upgrades)
        {
            RunLoadout loadout = driver != null && driver.Session != null ? driver.Session.ActiveLoadout : null;
            bool has = slot < DashSlot && loadout != null && SabreTalents.TryLineOf(loadout.PoolIndexAt(slot), out _);
            if (TooltipUpgradeRow != null) TooltipUpgradeRow.SetActive(has);
            if (!has) return;
            for (int i = 0; i < TooltipUpgradePips.Length; i++)
            {
                Image pip = TooltipUpgradePips[i];
                if (pip == null) continue;
                bool lit = i < upgrades;
                if (UpgradePipFilled != null && UpgradePipEmpty != null) pip.sprite = lit ? UpgradePipFilled : UpgradePipEmpty;
                pip.color = lit ? Color.white : new Color(1f, 1f, 1f, .45f);
            }
            if (TooltipUpgradeText != null) TooltipUpgradeText.text = "Усилений: " + upgrades + " из " + RunLoadout.MaxUpgrades;
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
            return Contains(Strip, screen) || Contains(HeroPanel, screen) || Contains(AbilityPanel, screen) || Contains(PotionPanel, screen)
                || Contains(MinimapFrame, screen) || Contains(MinimapCaptionPanel, screen);
        }

        /// <summary>Центр плитки в координатах экрана снизу слева — для съёмочного сценария.</summary>
        internal Vector2 SlotCenter(int slot)
        {
            HudSlotWidget widget = slot == DashSlot ? Dash : (uint)slot < (uint)Slots.Length ? Slots[slot] : null;
            return widget != null && widget.Hit != null ? (Vector2)Center(widget.Hit) : Vector2.zero;
        }

        /// <summary>Размер карты в единицах холста — в них HudMinimap раскладывает метки.</summary>
        internal Vector2 MinimapSize => MinimapArea != null ? MinimapArea.rect.size : Vector2.zero;

        internal void SetMinimapMarks(HudMinimap map)
        {
            if (MinimapMarks != null) MinimapMarks.Apply(map, Pointer);
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

        static readonly System.Collections.Generic.Dictionary<RectTransform, HudBarAnim> BarAnims =
            new System.Collections.Generic.Dictionary<RectTransform, HudBarAnim>();

        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            // Полосы здоровья и лавидия анимирует HudBarAnim: здесь только новая цель.
            if (!BarAnims.TryGetValue(fill, out HudBarAnim anim))
                BarAnims[fill] = anim = fill.GetComponentInParent<HudBarAnim>();
            if (anim != null) { anim.Target = ratio; return; }
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
