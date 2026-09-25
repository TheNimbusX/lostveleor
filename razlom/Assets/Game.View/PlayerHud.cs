using System.Collections;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Боевой HUD: состояние героя, способности и миникарта Разлома.</summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class PlayerHud : MonoBehaviour
    {
        [Header("Полоса здоровья")]
        public float BarWidth = 168f;
        public float BarHeight = 24f;
        public float Margin = 18f;

        [Header("Портрет, лавидий и опыт")]
        public float PortraitSize = 96f;
        public float PortraitGap = 12f;
        public float LavidiumBarHeight = 22f;
        public float ExperienceBarHeight = 18f;

        [Header("Слоты способностей")]
        public float SlotSize = 72f;
        public float SlotGap = 8f;

        [Header("Миникарта")]
        public float MinimapWidth = 190f;
        public float MinimapHeight = 190f;

        private TickDriver _driver;
        private GUIStyle _label;
        private GUIStyle _smallLabel;
        private GUIStyle _levelLabel;
        private GUIStyle _slotLabel;
        private GUIStyle _slotKey;
        private GUIStyle _slotName;
        private GUIStyle _cooldownLabel;
        private GUIStyle _mapLabel;
        private GUIStyle _tooltipTitle;
        private GUIStyle _tooltipBody;
        private GUIStyle _tooltipStats;
        private int _tooltipSlot = -1;
        private Rect _tooltipAnchor;
        private readonly HudChrome _chrome = new HudChrome();
        private Texture2D _portraitArt, _portraitCutout, _dashArt, _healthPotion, _lavidiumPotion;
        private readonly HudMinimap _minimap = new HudMinimap();
        private readonly float[] _pressUntil = new float[Simulation.AbilitySlots];
        private int _feedbackSlot = -1;
        private float _feedbackUntil;
        private string _feedbackText;
        private HudAbilityBlock _feedbackBlock;
        private GUIStyle _feedbackLabel;
        internal struct TooltipValue { public int Icon; public string Caption, Value; }
        private readonly TooltipValue[] _tooltipValues = new TooltipValue[8];
        private int _tooltipValueCount;
        private Camera _rangeCamera;
        private HudRangePreview _rangePreview;
        private float _screenHudScale = 1f;
        private GUIStyle _xpLabel, _tooltipKey, _tooltipMetric, _tooltipCaption;
        private Vector2 _pointer;
        private bool _xpHovered;
        private Rect _xpAnchor;
        private GUIStyle _heroName, _healthValue, _resourceValue, _resourceName;
        private Texture2D _white;
        // Иконка на каждую способность, а не одна на Вихрь. Нарезаны с
        // утверждённого листа `ART/.../PELAG/abilitys.png`; там же лежит
        // разбор того, что каждая делает, — если понадобится перерезать,
        // источник один и он в репозитории.
        private Texture2D[] _abilityIcons;
        private int[] _abilityIconIds;
        private bool _abilityIconsLoaded;
        private float _canvasWidth;
        private float _canvasHeight;
        private float _safeLeft;
        private float _safeRight;
        private float _safeBottom;

        private bool _portraitBaking;
        private float _nextPortraitTry;
        private int _shownLevel;
        private float _levelFlashUntil;

        private static readonly Color HealthBack = new Color(0.16f, 0.09f, 0.07f, 0.90f);
        private static readonly Color HealthFill = new Color(.91f, .33f, .29f, 1f);
        private static readonly Color HealthLow = new Color(1.00f, 0.47f, 0.16f, 0.98f);
        // Лавидий янтарный, а не синий: рядом золото HUD, а синий с золотом по
        // правилу палитры в одном дизайне не сочетаются.
        private static readonly Color LavidiumBack = new Color(0.20f, 0.11f, 0.05f, 1f);
        private static readonly Color LavidiumFill = new Color(.94f, .62f, .23f, 1f);
        private static readonly Color ExperienceBack = new Color(0.12f, 0.08f, 0.06f, 1f);
        private static readonly Color ExperienceFill = new Color(.83f, .82f, .77f, 1f);
        private static readonly Color SlotBack = new Color(0.13f, 0.075f, 0.06f, 0.92f);
        private static readonly Color SlotReady = new Color(0.97f, 0.58f, 0.28f, 0.58f);
        private static readonly Color SlotCooling = new Color(0.10f, 0.065f, 0.07f, 0.68f);
        private static readonly Color Ink = new Color(0.16f, 0.07f, 0.045f, 0.98f);
        private static readonly Color Coral = new Color(0.91f, 0.27f, 0.20f, 0.98f);
        private static readonly Color Gold = new Color(1.00f, 0.78f, 0.43f, 0.98f);
        private static readonly Color MapBack = new Color(0.16f, 0.09f, 0.07f, 0.84f);
        private static readonly Color MapRoom = new Color(0.21f, 0.60f, 0.58f, 0.86f);
        private static readonly Color MapEntrance = new Color(0.95f, 0.38f, 0.22f, 0.96f);

        private static readonly Color Plate = new Color(.20f, .19f, .14f, .74f);
        private static readonly Color Contour = new Color(.94f, .89f, .73f, .95f);
        private static readonly Color Paper = new Color(1f, .95f, .81f, 1f);
        private static readonly Color Quiet = new Color(.64f, .62f, .57f, 1f);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _rangePreview = new HudRangePreview(transform);
            _portraitArt = Resources.Load<Texture2D>("UI/HUD/PelagPortrait");
            _portraitCutout = Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout");
            _dashArt = Resources.Load<Texture2D>("UI/HUD/DashSilhouette");
            _healthPotion = Resources.Load<Texture2D>("UI/HUD/PotionHealth");
            _lavidiumPotion = Resources.Load<Texture2D>("UI/HUD/PotionLavidium");
            // Префаб собирает Разлом/UI/Собрать боевой HUD и дальше правится
            // руками. Нет префаба — остаётся прежний IMGUI, игра не ломается.
            // С 23 сентября первым берётся HUD на паке «Ночная акварель»
            // (CombatHudWc); прежний CombatHud остаётся запасным — удалить новый
            // префаб, и игра вернётся к нему.
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CombatHudWc") ?? Resources.Load<GameObject>("UI/Prefabs/CombatHud");
            if (prefab != null)
            {
                _view = Instantiate(prefab, transform).GetComponentInChildren<CombatHudView>(true);
                if (_view != null) _view.gameObject.SetActive(false);
            }
        }

        private CombatHudView _view;

        /// <summary>Открыта палатка или окно NPC: боевой HUD прячется целиком, чтобы не мешать окну.</summary>
        static bool CampWindowOpen => CampPlayerView.Instance?.InventoryOpen == true || CampServicesView.Instance?.IsOpen == true;

        private void RefreshView()
        {
            if (_view == null) return;
            GameSession session = _driver.Session;
            Simulation sim = _driver.Sim;
            bool visible = !_driver.GameplayPaused && session != null && session.Mode != GameMode.Summary
                && !CampWindowOpen && sim != null && sim.Entities.Count > 0;
            SetHudShown(visible);
            if (!visible) return;
            _view.Refresh(sim, session.Camp, _driver);
            RefreshMinimap(sim, session, _driver.Run);
        }

        private bool _hudShown;
        private CanvasGroup _hudGroup;

        /// <summary>
        /// HUD уходит и возвращается затуханием (аудит UI, этап 2): под окном палатки, паузой и итогами
        /// он раньше пропадал рывком. Время реальное — пауза его не держит.
        /// </summary>
        private void SetHudShown(bool shown)
        {
            if (shown == _hudShown) return;
            _hudShown = shown;
            GameObject hud = _view.gameObject;
            if (_hudGroup == null) _hudGroup = hud.GetComponent<CanvasGroup>();
            if (_hudGroup == null) _hudGroup = hud.AddComponent<CanvasGroup>();
            CanvasGroup group = _hudGroup;
            UiMotion.Stop(group);
            if (shown)
            {
                if (!hud.activeSelf) { group.alpha = 0f; hud.SetActive(true); }
                UiMotion.FadeTo(group, 1f, .2f);
            }
            else UiMotion.FadeTo(group, 0f, .16f, () => { if (hud != null && !_hudShown) hud.SetActive(false); });
        }

        /// <summary>
        /// Карта целиком на холсте: HudMinimap раскладывает метки в единицах карты префаба,
        /// HudMinimapMarks их показывает. До кадра холста, а не из OnGUI: иначе метки
        /// отставали на кадр и рисовались поверх любых окон.
        /// </summary>
        private void RefreshMinimap(Simulation sim, GameSession session, RiftRun run)
        {
            Vector2 size = _view.MinimapSize;
            if (size.x <= 0f || size.y <= 0f) return;
            Rect panel = new Rect(0f, 0f, size.x, size.y);
            _minimap.Bare = true;
            _minimap.Zoom = Mathf.Max(1f, _view.MinimapZoom);
            _minimap.Inset = 13f;
            if (session.Mode == GameMode.Rift && run != null) _minimap.DrawRift(panel, run, sim, _chrome, _mapLabel);
            else if (CampPlayerView.Instance != null && CampPlayerView.Instance.Active)
                _minimap.DrawCamp(panel, CampPlayerView.Instance, _chrome, _mapLabel);
            else return;
            _view.SetMinimapTexture(_minimap.MapTexture);
            _view.SetMinimapCaption(_minimap.Caption);
            _view.SetMinimapMarks(_minimap);
            MinimapBottom = _view.MinimapBottom;
        }

        /// <summary>
        /// При HUD на Canvas IMGUI остаётся только у радиуса способности в мире.
        /// </summary>
        private void DrawCanvasCompanions(Simulation sim)
        {
            _tooltipSlot = _view.HoverSlot;
            int reachSlot = _tooltipSlot >= 0 ? _tooltipSlot : _driver.AimingAbilityTarget ? _driver.AbilityTargetAimSlot : -1;
            AbilityBuild reach = reachSlot >= 0 ? sim.GetAbility(reachSlot) : null;
            if (reach != null) DrawAbilityReach(sim, reach);
        }

        /// <summary>
        /// Снимок портрета — вне OnGUI: студии нужны два обычных кадра, а
        /// сцена может ещё не иметь ArenaView в первые кадры запуска.
        /// </summary>
        private void LateUpdate()
        {
            RefreshView();
            if (_portraitArt != null || HeroPortrait.Texture != null || _portraitBaking || Time.unscaledTime < _nextPortraitTry) return;
            _nextPortraitTry = Time.unscaledTime + 2f;
            ArenaView arena = FindAnyObjectByType<ArenaView>();
            if (arena == null) return;
            _portraitBaking = true;
            StartCoroutine(BakePortrait(arena));
        }

        private IEnumerator BakePortrait(ArenaView arena)
        {
            yield return HeroPortrait.Bake(arena);
            _portraitBaking = false;
        }

        private void OnGUI()
        {
            // Мышь снимает TickDriver перед шагом Sim; здесь только рисунок.
            // Не дублируем нажатия на Layout/Repaint и не теряем тап между тиками.
            if (Event.current.type != EventType.Repaint) return;
            _rangePreview?.Hide();

            if (_driver.GameplayPaused) return;
            GameSession session = _driver.Session;
            if (session == null || session.Mode == GameMode.Summary || CampWindowOpen) return;

            Simulation sim = _driver.Sim;
            RiftRun run = _driver.Run;
            if (sim == null || sim.Entities.Count == 0) return;

            EnsureStyles();
            _tooltipSlot = -1;
            _xpHovered = false;
            Vector2 screenPointer = Event.current.mousePosition;
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Min(Mathf.Clamp(Screen.height / 1080f, 1f, 2f), Screen.safeArea.width / 940f);
            Rect safe = Screen.safeArea;
            _canvasWidth = Screen.width / scale;
            _canvasHeight = Screen.height / scale;
            _safeLeft = safe.xMin / scale;
            _safeRight = _canvasWidth - safe.xMax / scale;
            _safeBottom = safe.yMin / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                if (_view != null) { DrawCanvasCompanions(sim); return; }
                float mapScale = scale * .85f;
                GUI.matrix = Matrix4x4.Scale(new Vector3(mapScale, mapScale, 1f));
                _minimap.Pointer = screenPointer / mapScale;
                Rect mapPanel = new Rect(safe.xMax / mapScale - Margin - MinimapWidth,
                    (Screen.height - safe.yMax) / mapScale + Margin, MinimapWidth, MinimapHeight);
                // Подпись карты стоит на 4 px ниже и высотой 23 (HudMinimap.DrawBase).
                MinimapBottom = (mapPanel.yMax + 27f) * mapScale;
                if (session.Mode == GameMode.Rift && run != null) _minimap.DrawRift(mapPanel, run, sim, _chrome, _mapLabel);
                else if (CampPlayerView.Instance != null && CampPlayerView.Instance.Active)
                    _minimap.DrawCamp(mapPanel, CampPlayerView.Instance, _chrome, _mapLabel);
                // Независимые масштабы: миникарта 85%, нижняя группа с подсказками 90%.
                float hudScale = scale * .9f;
                _screenHudScale = hudScale;
                _canvasWidth = Screen.width / hudScale;
                _canvasHeight = Screen.height / hudScale;
                _safeLeft = safe.xMin / hudScale;
                _safeRight = _canvasWidth - safe.xMax / hudScale;
                _safeBottom = safe.yMin / hudScale;
                GUI.matrix = Matrix4x4.Scale(new Vector3(hudScale, hudScale, 1f));
                _pointer = screenPointer / hudScale;
                DrawHero(sim, session.Camp);
                DrawAbilities(sim);
                DrawExperience(sim, session.Camp);
                DrawPotions(sim);
                DrawAbilityTooltip(sim);
                DrawAbilityFeedback(sim);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        // Захват мыши выполняет TickDriver до шага Sim; OnGUI только рисует.
        internal bool HitTest(Vector2 screenPosition, out int slot)
        {
            slot = -1;
            if (_driver == null || _driver.GameplayPaused || _driver.Session == null ||
                _driver.Session.Mode == GameMode.Summary || CampWindowOpen) return false;
            Simulation sim = _driver.Sim;
            if (sim == null || sim.Entities.Count == 0) return false;
            if (_view != null) return _view.HitTest(screenPosition, sim, out slot);
            float baseScale = Mathf.Min(Mathf.Clamp(Screen.height / 1080f, 1f, 2f), Screen.safeArea.width / 940f);
            float scale = baseScale * .9f;
            Rect safe = Screen.safeArea;
            _canvasWidth = Screen.width / scale; _canvasHeight = Screen.height / scale;
            _safeLeft = safe.xMin / scale; _safeRight = (Screen.width - safe.xMax) / scale; _safeBottom = safe.yMin / scale;
            Vector2 point = new Vector2(screenPosition.x / scale, (Screen.height - screenPosition.y) / scale);
            BottomBar bar = MeasureBottomBar(sim);
            for (int i = 0; i < DashSlot; i++)
            {
                Rect tile = new Rect(bar.RowX + i * (SlotSize + SlotGap), bar.Y, SlotSize, SlotSize + 14f);
                // Пустая рамка ловит мышь, но слота не даёт: нажимать нечего.
                if (tile.Contains(point)) { if (sim.GetAbility(i) != null) slot = i; return true; }
            }
            if (bar.HasDash && new Rect(bar.DashX, bar.Bottom - bar.DashSize, bar.DashSize, bar.DashSize + 14f).Contains(point))
            { slot = DashSlot; return true; }
            float right = bar.HasDash ? bar.DashX + bar.DashSize + 46f : bar.RowX + bar.RowWidth + 52f;
            if (new Rect(bar.GroupX, bar.Bottom - PortraitSize, right - bar.GroupX, PortraitSize + 36f).Contains(point)) return true;
            float mapScale = baseScale * .85f;
            Vector2 mapPoint = new Vector2(screenPosition.x / mapScale, (Screen.height - screenPosition.y) / mapScale);
            return new Rect((safe.xMax / mapScale) - Margin - MinimapWidth,
                (Screen.height - safe.yMax) / mapScale + Margin, MinimapWidth, MinimapHeight + 36f).Contains(mapPoint);
        }

        /// <summary>Слот общего кувырка — он же последний. Живёт вне ряда.</summary>
        private const int DashSlot = Simulation.AbilitySlots - 1;

        /// <summary>Плитка кувырка меньше боевых: это не пятая способность.</summary>
        private const float DashSlotScale = 0.7f;

        /// <summary>Отрыв плитки кувырка от ряда — заметно больше обычного зазора.</summary>
        private const float DashGap = 16f;

        /// <summary>Расстояние от полосы здоровья до первой плитки.</summary>
        private const float BarToRowGap = 28f;

        /// <summary>
        /// Геометрия нижней панели.
        ///
        /// Ряд боевых способностей задаёт центр; герой стоит слева, кувырок
        /// справа. Общий расчёт держит полосу опыта точно под боевым рядом.
        /// </summary>
        private struct BottomBar
        {
            public float GroupX, ColumnX, RowX, RowWidth, DashX, DashSize, Y, Bottom;
            public int RowCount;
            public bool HasDash;
        }

        private float HeroWidth => PortraitSize + PortraitGap + BarWidth;

        private BottomBar MeasureBottomBar(Simulation sim)
        {
            BottomBar bar = default;
            // Ряд всегда из четырёх рамок: пустой слот забега виден как место под находку.
            bar.RowCount = DashSlot;
            bar.HasDash = sim.GetAbility(DashSlot) != null;
            bar.DashSize = Mathf.Round(SlotSize * DashSlotScale);

            float row = bar.RowCount > 0
                ? bar.RowCount * SlotSize + (bar.RowCount - 1) * SlotGap
                : 0f;
            float dash = bar.HasDash ? DashGap + bar.DashSize : 0f;
            float abilities = row + dash;
            float groupWidth = HeroWidth + (abilities > 0f ? BarToRowGap + abilities : 0f) + 46f;

            float groupMaxX = Mathf.Max(_safeLeft + Margin,
                _canvasWidth - _safeRight - Margin - groupWidth);
            bar.GroupX = Mathf.Clamp(_canvasWidth * 0.5f - row * 0.5f - HeroWidth - BarToRowGap,
                _safeLeft + Margin, groupMaxX);
            bar.ColumnX = bar.GroupX + PortraitSize + PortraitGap;
            bar.RowX = bar.GroupX + HeroWidth + BarToRowGap;
            bar.RowWidth = row;
            bar.DashX = bar.RowX + row + DashGap;
            // Опыт отделён от иконок небольшим зазором и не касается края экрана.
            bar.Bottom = _canvasHeight - _safeBottom - Mathf.Max(42f, Margin + 18f + ExperienceBarHeight);
            bar.Y = bar.Bottom - SlotSize;
            return bar;
        }

        /// <summary>
        /// Блок героя: портрет с уровнем и колонка полос — жизнь и лавидий.
        /// Низ колонки стоит на той же линии, что низ ряда способностей.
        /// </summary>
        private void DrawHero(Simulation sim, Camp camp)
        {
            if(_portraitCutout==null && Time.unscaledTime >= _nextPortraitTry)
            {
                _portraitCutout=Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout");
                _nextPortraitTry=Time.unscaledTime+1f;
            }
            BottomBar bar = MeasureBottomBar(sim);
            float x = bar.ColumnX;
            Rect portrait = new Rect(bar.GroupX, bar.Bottom - PortraitSize, PortraitSize, PortraitSize);
            int level = camp != null ? camp.Level : 1;
            if (_shownLevel == 0) _shownLevel = level;
            if (level > _shownLevel) { _shownLevel = level; _levelFlashUntil = Time.unscaledTime + 1.2f; }
            _chrome.Shape(portrait, new Color(.89f,.84f,.70f,.95f), 13f);
            if (_portraitCutout == null)
                _chrome.Portrait(portrait, _portraitArt != null ? _portraitArt : HeroPortrait.Texture);
            _chrome.Shape(portrait, Paper, 13f, Time.unscaledTime < _levelFlashUntil ? 2.5f : 1.5f);
            if (_portraitCutout != null)
            {
                // Волосы и плечи перекрывают верхнюю рамку; низ бюста остаётся на общей линии.
                Rect bust = new Rect(portrait.x - 4f, portrait.y - 16f, portrait.width + 8f, portrait.height + 16f);
                GUI.DrawTextureWithTexCoords(bust, _portraitCutout, new Rect(.08f,.20f,.84f,.80f));
            }
            Rect badge = new Rect(portrait.x, portrait.yMax - 28f, 28f, 28f);
            _chrome.Shape(badge, Plate, 6f);
            _chrome.Shape(badge, Contour, 6f, 1f);
            GUI.Label(badge, level.ToString(), _levelLabel);
            Rect name = new Rect(x, portrait.y + 5f, BarWidth, 27f);
            var shadow = new Rect(name.x + 1f, name.y + 1f, name.width, name.height);
            Color previous = GUI.color;
            GUI.color = new Color(.12f, .12f, .08f, .75f);
            GUI.Label(shadow, "Пелаг", _heroName);
            GUI.color = previous;
            GUI.Label(name, "Пелаг", _heroName);

            int health = Mathf.Max(0, sim.Entities.Health[Simulation.PlayerId]);
            int max = Mathf.Max(1, sim.Entities.MaxHealth[Simulation.PlayerId]);
            float ratio = Mathf.Clamp01(health / (float)max);
            Rect hp = new Rect(x + 25f, bar.Bottom - 60f, BarWidth - 25f, BarHeight);
            DrawVital(hp, ratio, ratio <= .25f ? HealthLow : HealthFill, health + " / " + max);
            HudSymbols.Stat(new Rect(x, hp.center.y - 12f, 22f, 24f), 0);
            int maxResource = sim.Entities.MaxLavidium[Simulation.PlayerId];
            if (maxResource <= 0) return;
            int resource = Mathf.FloorToInt(sim.Entities.Lavidium[Simulation.PlayerId].ToFloat());
            Rect lavidium = new Rect(x + 25f, bar.Bottom - 28f, BarWidth - 25f, LavidiumBarHeight);
            DrawVital(lavidium, resource / (float)maxResource, LavidiumFill, resource + " / " + maxResource);
            HudSymbols.Stat(new Rect(x, lavidium.center.y - 12f, 22f, 24f), 1);
        }

        private void DrawVital(Rect rect, float ratio, Color color, string value)
        {
            _chrome.Meter(rect, ratio, color, rect.height * .5f);
            _healthValue.normal.textColor = new Color(.19f, .12f, .08f);
            GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), value, _healthValue);
            GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), value, _healthValue);
            GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), value, _healthValue);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), value, _healthValue);
            _healthValue.normal.textColor = new Color(1f, .98f, .91f);
            GUI.Label(rect, value, _healthValue);
        }

        private void DrawExperience(Simulation sim, Camp camp)
        {
            BottomBar bar = MeasureBottomBar(sim);
            if (bar.RowWidth <= 0f) return;
            float value = camp != null ? Mathf.Clamp01(camp.Experience / (float)Mathf.Max(1, camp.ExperienceToNextLevel)) : 0f;
            Rect track = new Rect(bar.RowX, bar.Bottom + 18f, bar.RowWidth, ExperienceBarHeight);
            _chrome.Meter(track, value, Paper, 7f);
            _xpHovered = HudReviewCapture.Enabled ? HudReviewCapture.HoverXp : track.Contains(_pointer);
            _xpAnchor = track;
            string text = _xpHovered && camp != null ? camp.Experience + "\\" + camp.ExperienceToNextLevel : "XP";
            float textWidth = Mathf.Min(track.width - 6f, 94f);
            Rect label = new Rect(track.center.x - textWidth * .5f,
                track.y + 2f, textWidth, track.height - 4f);
            // Непрозрачная вставка внутри полосы не зависит от процента светлой заливки.
            _chrome.Shape(label, new Color(.98f, .95f, .85f), 4f);
            _xpLabel.normal.textColor = new Color(.20f, .17f, .11f);
            GUI.Label(label, text, _xpLabel);
        }

        private void DrawPotions(Simulation sim)
        {
            BottomBar bar = MeasureBottomBar(sim);
            float size = 36f;
            float x = bar.HasDash ? bar.DashX + bar.DashSize + 10f : bar.RowX + bar.RowWidth + 16f;
            for (int i = 0; i < 2; i++)
            {
                Rect tile = new Rect(x, bar.Y + i * (size + 8f), size, size);
                _chrome.Shape(tile, Plate, size * .125f);
                Texture2D art = i == 0 ? _healthPotion : _lavidiumPotion;
                int stock=_driver.Session.Camp.PotionCount(_driver.Session.Camp.SelectedPotion(i));
                if (art != null){if(stock==0)_chrome.EmptyIcon(Inset(tile,2f),art);else GUI.DrawTexture(Inset(tile,2f),art);}
                _chrome.Shape(tile, new Color(.55f, .54f, .48f, .55f), size * .125f, 1f);
                GUI.Label(tile,stock.ToString());
            }
        }

        private void DrawAbilities(Simulation sim)
        {
            BottomBar bar = MeasureBottomBar(sim);
            if (bar.RowCount == 0 && !bar.HasDash) return;

            for (int slot = 0; slot < DashSlot; slot++)
            {
                Rect box = new Rect(bar.RowX + slot * (SlotSize + SlotGap), bar.Y, SlotSize, SlotSize);
                AbilityBuild build = sim.GetAbility(slot);
                if (build == null) DrawEmptySlot(box);
                else DrawSlot(sim, slot, build, box);
            }

            if (bar.HasDash)
            {
                // Плитка кувырка ниже ростом и стоит на общей нижней линии:
                // ряд способностей остаётся ровным, а меньший размер сам
                // говорит, что это не пятая способность, а отдельная кнопка.
                Rect box = new Rect(bar.DashX, bar.Y + (SlotSize - bar.DashSize),
                    bar.DashSize, bar.DashSize);
                DrawSlot(sim, DashSlot, sim.GetAbility(DashSlot), box);
            }
        }

        /// <summary>Пустой слот забега: тёмная рамка без иконки и клавиши.</summary>
        private void DrawEmptySlot(Rect box)
        {
            _chrome.Shape(box, new Color(.10f, .09f, .07f, .55f), box.width * .125f);
            _chrome.Shape(box, new Color(Contour.r, Contour.g, Contour.b, .45f), box.width * .125f, 1f);
        }

        /// <summary>
        /// Одна плитка: рамка, заливка готовности, иконка, кулдаун, клавиша и
        /// подсказка. Размер берётся из <paramref name="box"/> — кувырок рисуется
        /// тем же кодом, только меньшей плиткой, и любая правка вида
        /// применяется к обоим сразу.
        /// </summary>
        private void DrawSlot(Simulation sim, int slot, AbilityBuild build, Rect box)
        {
            if (build == null) return;
            int left = sim.AbilityReadyTick(slot) - sim.Tick;
            var availability = Availability(sim, slot, build);
            bool affordable = availability.Block != HudAbilityBlock.Resource;
            bool ready = availability.Ready;
            bool hovered = HudReviewCapture.Enabled ? HudReviewCapture.HoverSlot == slot : CaptureRig.HudTooltipSlot >= 0
                ? CaptureRig.HudTooltipSlot == slot
                : box.Contains(_pointer);
            Rect hitBox = box;
            float press = Mathf.Clamp01((_pressUntil[slot] - Time.unscaledTime) / .16f);
            if (press > 0f) box = Inset(box, press * 1.5f);
            _chrome.Shape(new Rect(box.x, box.y + 3f, box.width, box.height), new Color(0f, 0f, 0f, .4f), 7f);
            _chrome.Shape(box, Plate, box.width * .125f);
            Rect inner = Inset(box, 1.5f);
            Texture2D icon = slot == DashSlot ? _dashArt : AbilityIcon(slot, build.DefinitionId);
            if (icon != null)
            {
                Color previous = GUI.color;
                GUI.color = ready ? Color.white : new Color(.82f, .80f, .75f, 1f);
                // В исходных карточках уже нарисована массивная рама. В HUD
                // используем внутреннюю иллюстрацию и собственный общий контур.
                if (slot == DashSlot) HudSymbols.Dash(Inset(inner, 2f));
                else _chrome.Art(inner, icon, new Rect(.12f, .12f, .76f, .76f));
                GUI.color = previous;
            }
            if (availability.Block == HudAbilityBlock.Cooldown)
            {
                float remaining = Mathf.Clamp01(left / (float)Mathf.Max(1, sim.AbilityCooldownTicks(build)));
                GUI.BeginGroup(new Rect(inner.x, inner.y, inner.width, inner.height * remaining));
                _chrome.Shape(new Rect(0f, 0f, inner.width, inner.height), new Color(.14f, .13f, .10f, .66f), inner.width * .125f);
                GUI.EndGroup();
            }
            Color rim = hovered ? Paper : Contour;
            if (availability.Block == HudAbilityBlock.Resource) rim = new Color(.97f,.65f,.27f);
            bool denied = _feedbackSlot == slot && _feedbackBlock != HudAbilityBlock.None && Time.unscaledTime < _feedbackUntil;
            if (denied) rim = Color.Lerp(rim,new Color(1f,.43f,.27f),Mathf.Clamp01((_feedbackUntil-Time.unscaledTime)*2f));
            if (hovered || press > 0f) _chrome.Shape(Inset(box,-2f),new Color(rim.r,rim.g,rim.b,.22f),box.width*.125f+2f,2f);
            _chrome.Shape(box, rim, box.width * .125f, hovered || denied ? 2f : 1.25f);
            if (hovered)
            {
                _tooltipSlot = slot;
                _tooltipAnchor = hitBox;
            }

            if (availability.Block == HudAbilityBlock.Cooldown)
            {
                string seconds = (left / (float)Simulation.TicksPerSecond).ToString("0.0");
                GUI.Label(new Rect(box.x + 1f, box.y + 1f, box.width, box.height), seconds, _slotLabel);
                GUI.Label(box, seconds, _cooldownLabel);
            }
            else if (!affordable)
            {
                Rect resource = new Rect(box.x + 5f, box.yMax - 24f, box.width - 10f, 17f);
                _chrome.Shape(resource,new Color(.22f,.17f,.10f,.92f),4f);
                HudSymbols.Stat(new Rect(resource.x+3f,resource.y+1f,14f,15f),1);
                GUI.Label(new Rect(resource.x+18f,resource.y,resource.width-20f,resource.height),"−"+availability.MissingResource,_slotKey);
            }
            string key = SlotKey(slot);
            float keyWidth = slot == DashSlot ? 46f : 24f;
            Rect keyTab = new Rect(box.center.x - keyWidth * .5f, box.yMax - 4f, keyWidth, 18f);
            _chrome.Shape(keyTab, slot == DashSlot ? Plate : new Color(.47f, .21f, .15f, .96f), 5f);
            _chrome.Shape(keyTab, hovered ? Paper : Contour, 4f, 1f);
            GUI.Label(keyTab, key, _slotKey);
        }

        internal static HudAbilityAvailability Availability(Simulation sim, int slot, AbilityBuild build)
            => HudAbilityAvailability.Evaluate(sim.Entities.Alive[Simulation.PlayerId],
                build.DefinitionId == AbilityDefinition.WreckId && sim.WreckComboOpen,
                sim.AbilityReadyTick(slot)-sim.Tick,sim.Entities.Lavidium[Simulation.PlayerId].ToInt(),Simulation.LavidiumCostOf(build));

        internal void NotifyAbilityPress(int slot, HudAbilityAvailability state)
        {
            if ((uint)slot >= _pressUntil.Length) return;
            _pressUntil[slot] = Time.unscaledTime + .16f;
            _feedbackSlot = slot; _feedbackBlock = state.Block; _feedbackUntil = Time.unscaledTime + 1.25f;
            _feedbackText = AvailabilityText(state);
            // Одна воронка на оба HUD: отказ слышен и со старым IMGUI, и с новым на Canvas.
            if (state.Block == HudAbilityBlock.Resource) GameSound.Play("mana_empty", .8f);
            else if (state.Block != HudAbilityBlock.None) GameSound.Play("ability_denied", .7f);
            _view?.NotifyAbilityPress(slot, state);
        }
        internal HudAbilityBlock LastFeedbackBlock => _view != null ? _view.LastFeedbackBlock : _feedbackBlock;
        internal string ReviewAssets => "portrait="+(_portraitCutout!=null?_portraitCutout.name+" "+_portraitCutout.width+"x"+_portraitCutout.height:"NULL")
            +"; name-font="+(_heroName!=null?_heroName.font.name:"pending");
        internal Vector2 ReviewAbilityPoint(int slot)
        {
            if (_view != null) return _view.SlotCenter(slot);
            HitTest(Vector2.zero,out _);
            BottomBar bar=MeasureBottomBar(_driver.Sim);
            float scale=Mathf.Min(Mathf.Clamp(Screen.height/1080f,1f,2f),Screen.safeArea.width/940f)*.9f;
            int visual=slot;
            Vector2 point=slot==DashSlot?new Vector2(bar.DashX+bar.DashSize*.5f,bar.Bottom-bar.DashSize*.5f)
                :new Vector2(bar.RowX+visual*(SlotSize+SlotGap)+SlotSize*.5f,bar.Y+SlotSize*.5f);
            return new Vector2(point.x*scale,Screen.height-point.y*scale);
        }

        internal static string AvailabilityText(HudAbilityAvailability state)
        {
            if (state.Block == HudAbilityBlock.Cooldown) return "Перезарядка · " + (state.RemainingTicks/(float)Simulation.TicksPerSecond).ToString("0.0") + " с";
            if (state.Block == HudAbilityBlock.Resource) return "Не хватает лавидия: " + state.MissingResource;
            if (state.Block == HudAbilityBlock.Dead) return "Герой без сознания";
            return string.Empty;
        }

        private void DrawAbilityFeedback(Simulation sim)
        {
            if (_tooltipSlot >= 0 || Time.unscaledTime >= _feedbackUntil || string.IsNullOrEmpty(_feedbackText)) return;
            BottomBar bar = MeasureBottomBar(sim);
            float width = _feedbackLabel.CalcSize(new GUIContent(_feedbackText)).x+26f;
            Rect rect = new Rect(bar.RowX+(bar.RowWidth-width)*.5f,bar.Y-40f,width,29f);
            TooltipPanel(rect);
            GUI.Label(rect,_feedbackText,_feedbackLabel);
        }

        private void TooltipPanel(Rect panel)
        {
            _chrome.Shape(new Rect(panel.x, panel.y + 2f, panel.width, panel.height), new Color(.12f, .10f, .07f, .16f), 7f);
            _chrome.Shape(panel, new Color(.96f, .93f, .84f, .98f), 7f);
            _chrome.Shape(panel, new Color(.69f, .62f, .47f, .65f), 7f, .7f);
        }

        private void BuildTooltipValues(AbilityBuild build)
            => _tooltipValueCount = CollectTooltipValues(build, _tooltipValues, _driver.Sim);

        /// <summary>
        /// Параметры подсказки по сборке способности — общие для IMGUI и Canvas.
        /// Icon: 0 здоровье, 1 лавидий, 2 время, 3 урон, 4 дальность, 5 радиус.
        /// </summary>
        internal static int CollectTooltipValues(AbilityBuild build, TooltipValue[] into, Simulation sim = null)
        {
            int count = 0;
            void AddTooltipValue(int icon, string caption, string value)
            {
                if (count < into.Length) into[count++] = new TooltipValue { Icon = icon, Caption = caption, Value = value };
            }
            AddTooltipValue(1, "Лавидий", Simulation.LavidiumCostOf(build).ToString());
            AddTooltipValue(2, "Перезарядка", ((sim != null ? sim.AbilityCooldownTicks(build) : build.CooldownTicks) / (float)Simulation.TicksPerSecond).ToString("0.#") + " с");
            int id = build.DefinitionId;
            int damage = build.Get(AbilityStatType.Damage).ToInt();
            if (damage > 0)
                AddTooltipValue(3, id == AbilityDefinition.WreckId ? "Урон комбо" : "Базовый урон",
                    id == AbilityDefinition.WreckId ? damage + " / " + damage + " / " + damage * 2 : damage.ToString());
            float bonus = build.Get(AbilityStatType.BonusDamagePercent).ToFloat();
            if (bonus > 0f) AddTooltipValue(3, "Усиление атак", "+" + (bonus * 100f).ToString("0.#") + "%");
            float radius = build.Get(AbilityStatType.Radius).ToFloat();
            bool area = id == AbilityDefinition.WhirlwindId;
            if (radius > 0f)
                AddTooltipValue(area ? 5 : 4, area ? "Радиус" : id == AbilityDefinition.DashId ? "Перемещение" : "Дальность",
                    radius.ToString("0.#") + " м");
            else AddTooltipValue(4, "Применение", "На себя");
            if (id == AbilityDefinition.FireFlaskId)
                AddTooltipValue(5, "Радиус взрыва", (build.Get(AbilityStatType.Width).ToFloat() * .5f).ToString("0.#") + " м");
            else if (id == AbilityDefinition.BackblastId)
                AddTooltipValue(5, "Радиус взрыва", build.Get(AbilityStatType.Width).ToFloat().ToString("0.#") + " м");
            else if (id == AbilityDefinition.AnchorSlamId || id == AbilityDefinition.SkewerId)
                AddTooltipValue(5, "Ширина удара", build.Get(AbilityStatType.Width).ToFloat().ToString("0.#") + " м");
            if (id == AbilityDefinition.BlazeId || id == AbilityDefinition.FireFlaskId)
                AddTooltipValue(2, "Длительность", (build.Get(AbilityStatType.DurationTicks).ToFloat() / Simulation.TicksPerSecond).ToString("0.#") + " с");
            return count;
        }

        private void DrawAbilityTooltip(Simulation sim)
        {
            if (_tooltipSlot < 0)
            {
                if (_driver.AimingAbilityTarget)
                {
                    var aimingBuild = sim.GetAbility(_driver.AbilityTargetAimSlot);
                    if (aimingBuild != null) DrawAbilityReach(sim, aimingBuild);
                }
                return;
            }
            AbilityBuild build = sim.GetAbility(_tooltipSlot);
            if (build == null) return;
            DrawAbilityReach(sim, build);
            BuildTooltipValues(build);
            const float padding = 13f;
            float width = Mathf.Min(300f, _canvasWidth - _safeLeft - _safeRight - Margin * 2f);
            float inner = width - padding * 2f;
            string name = AbilityName(build.DefinitionId);
            name = name.Substring(0, 1) + name.Substring(1).ToLowerInvariant();
            var title = new GUIContent(name);
            var description = new GUIContent(AbilityDescription(build.DefinitionId));
            float heading = Mathf.Max(22f, _tooltipTitle.CalcHeight(title, inner - 48f));
            float body = _tooltipBody.CalcHeight(description, inner);
            bool aiming = _driver.AbilityTargetAimSlot == _tooltipSlot;
            var availability = Availability(sim,_tooltipSlot,build);
            float metrics = LayoutTooltipMetrics(new Vector2(0f, 0f), inner, false);
            float height = padding * 2f + heading + 5f + body + 12f + metrics + (aiming || !availability.Ready ? 24f : 0f);
            float x = Mathf.Clamp(_tooltipAnchor.center.x - width * .5f, _safeLeft + Margin,
                _canvasWidth - _safeRight - Margin - width);
            float y = Mathf.Max(8f, MeasureBottomBar(sim).Y - height - 14f);
            Rect panel = new Rect(x, y, width, height);
            TooltipPanel(panel);
            GUI.Label(new Rect(x + padding, y + padding, inner - 48f, heading), title, _tooltipTitle);
            Rect key = new Rect(panel.xMax - padding - 40f, y + padding, 40f, 22f);
            GUI.Label(key, SlotKey(_tooltipSlot), _tooltipKey);
            float bodyY = y + padding + heading + 5f;
            GUI.Label(new Rect(x + padding, bodyY, inner, body), description, _tooltipBody);
            float metricsY = bodyY + body + 12f;
            Fill(new Rect(x + padding, metricsY - 5f, inner, .7f), new Color(.43f, .37f, .25f, .18f));
            LayoutTooltipMetrics(new Vector2(x + padding, metricsY), inner, true);
            if (aiming)
                GUI.Label(new Rect(x + padding, panel.yMax - padding - 17f, inner, 17f), "Выбери цель · ПКМ — отмена", _tooltipCaption);
            else if (!availability.Ready)
                GUI.Label(new Rect(x+padding,panel.yMax-padding-19f,inner,20f),AvailabilityText(availability),_feedbackLabel);
        }

        // Параметры текут строками; короткие навыки не получают пустых ячеек.
        private float LayoutTooltipMetrics(Vector2 origin, float width, bool draw)
        {
            float x = 0f, y = 0f;
            for (int i = 0; i < _tooltipValueCount; i++)
            {
                TooltipValue metric = _tooltipValues[i];
                // Цена, время восстановления и урон узнаются по символу.
                // У расстояний и длительности оставляем подпись: единицы совпадают.
                string prefix = metric.Icon == 4 || metric.Icon == 5 || metric.Caption == "Длительность"
                    || metric.Caption == "Усиление атак" ? metric.Caption.ToLowerInvariant() + " " : "";
                var content = new GUIContent(prefix + metric.Value);
                float itemWidth = Mathf.Min(width, 21f + _tooltipBody.CalcSize(content).x);
                if (x > 0f && x + itemWidth > width) { x = 0f; y += 24f; }
                if (draw)
                {
                    HudSymbols.Stat(new Rect(origin.x + x, origin.y + y + 2f, 17f, 18f), metric.Icon);
                    GUI.Label(new Rect(origin.x + x + 21f, origin.y + y, itemWidth - 21f, 23f), content, _tooltipBody);
                }
                x += itemWidth + 16f;
            }
            return y + 23f;
        }

        private void DrawAbilityReach(Simulation sim, AbilityBuild build)
        {
            float radius = build.Get(AbilityStatType.Radius).ToFloat();
            if (radius <= 0f) return;
            if (_rangeCamera == null) _rangeCamera = Camera.main;
            if (_rangeCamera == null) return;
            _rangePreview.Begin(_rangeCamera,Availability(sim,_tooltipSlot >= 0 ? _tooltipSlot : _driver.AbilityTargetAimSlot,build).Ready);
            Vector3 center = _driver.GetRenderPosition(Simulation.PlayerId) + Vector3.up * .06f;
            var position = sim.Entities.Position[Simulation.PlayerId];
            var aim = _driver.CursorWorld - position;
            var facing = sim.Entities.Facing[Simulation.PlayerId];
            if (aim.LengthSq.Raw == 0) aim = facing;
            if (aim.LengthSq.Raw == 0) aim = new FixVec2(Fix64.One, Fix64.Zero);
            Vector3 forward = new Vector3(aim.X.ToFloat(), 0f, aim.Y.ToFloat()).normalized;
            int id = build.DefinitionId;
            if (id == AbilityDefinition.CleaveId)
            {
                // Width здесь радиус клинка, а не полная ширина полосы.
                var direction = sim.CleaveActive ? sim.CleaveDirection : facing;
                forward = new Vector3(direction.X.ToFloat(), 0f, direction.Y.ToFloat()).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.right;
                ReachCapsule(center, forward, radius, build.Get(AbilityStatType.Width).ToFloat());
                if (build.Has(AbilityFlag.CleaveFan))
                {
                    ReachCapsule(center, Quaternion.Euler(0f, 35f, 0f) * forward, radius, build.Get(AbilityStatType.Width).ToFloat());
                    ReachCapsule(center, Quaternion.Euler(0f, -35f, 0f) * forward, radius, build.Get(AbilityStatType.Width).ToFloat());
                }
            }
            else if (id == AbilityDefinition.AnchorSlamId)
            {
                if (sim.AnchorSlamActive)
                {
                    center.x = sim.AnchorSlamOrigin.X.ToFloat(); center.z = sim.AnchorSlamOrigin.Y.ToFloat();
                    forward = new Vector3(sim.AnchorSlamDirection.X.ToFloat(), 0f, sim.AnchorSlamDirection.Y.ToFloat());
                }
                Vector3 side = new Vector3(forward.z, 0f, -forward.x) * build.Get(AbilityStatType.Width).ToFloat() * .5f;
                ReachLine(center - side, center + forward * radius - side);
                ReachLine(center + side, center + forward * radius + side);
                ReachLine(center - side, center + side);
                ReachLine(center + forward * radius - side, center + forward * radius + side);
                ReachArrow(center, center + forward * radius);
            }
            else if (id == AbilityDefinition.WreckId)
            {
                if (sim.WreckDirection.LengthSq.Raw != 0)
                    forward = new Vector3(sim.WreckDirection.X.ToFloat(), 0f, sim.WreckDirection.Y.ToFloat()).normalized;
                float angle = Mathf.Atan2(forward.z, forward.x);
                float half = Mathf.Acos(Mathf.Clamp(build.Get(AbilityStatType.ArcCosine).ToFloat(), -1f, 1f));
                ReachArc(center, radius, angle - half, angle + half);
                ReachLine(center, center + RingOffset(angle - half) * radius);
                ReachLine(center, center + RingOffset(angle + half) * radius);
            }
            else if (id == AbilityDefinition.SkewerId)
                ReachCapsule(center, forward, radius, build.Get(AbilityStatType.Width).ToFloat() * .5f);
            else if (id == AbilityDefinition.BackblastId)
            {
                ReachArrow(center, center - forward * radius);
                ReachArc(center, build.Get(AbilityStatType.Width).ToFloat(), 0f, Mathf.PI * 2f);
            }
            else if (id == AbilityDefinition.DashId)
                ReachArrow(center, center + forward * radius);
            else if (id == AbilityDefinition.AnchorLeapId)
            {
                // Дальность из сборки: талант «Длинная цепь» её удлиняет.
                float range = build.Get(AbilityStatType.Radius).ToFloat();
                float reach = Mathf.Min((_driver.CursorWorld - position).Length.ToFloat(), range);
                ReachArc(center, range, 0f, Mathf.PI * 2f);
                ReachArrow(center, center + forward * reach);
            }
            else if (id == AbilityDefinition.FireFlaskId)
            {
                float distance = (_driver.CursorWorld - position).Length.ToFloat();
                if (distance <= 0f) distance = 1f;
                Vector3 landing = center + forward * Mathf.Min(distance, radius);
                if (sim.FlaskInFlight) { landing.x = sim.FlaskTarget.X.ToFloat(); landing.z = sim.FlaskTarget.Y.ToFloat(); }
                ReachArrow(center, landing);
                ReachArc(landing, build.Get(AbilityStatType.Width).ToFloat() * .5f, 0f, Mathf.PI * 2f);
            }
            else if (id == AbilityDefinition.WhirlwindId || id == AbilityDefinition.ChainStepId)
                ReachArc(center, radius, 0f, Mathf.PI * 2f);
            else
            {
                Vector3 target = new Vector3(_driver.CursorWorld.X.ToFloat(), center.y, _driver.CursorWorld.Y.ToFloat());
                ReachArc(target, radius, 0f, Mathf.PI * 2f);
            }
            _rangePreview.End();
        }

        private static Vector3 RingOffset(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        private void ReachArc(Vector3 center, float radius, float start, float end)
        {
            int segments = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(end - start) * 12f));
            for (int i = 0; i < segments; i++)
                ReachLine(center + RingOffset(Mathf.Lerp(start, end, i / (float)segments)) * radius,
                    center + RingOffset(Mathf.Lerp(start, end, (i + 1f) / segments)) * radius);
        }

        private void ReachCapsule(Vector3 center, Vector3 forward, float length, float halfWidth)
        {
            Vector3 side = new Vector3(forward.z, 0f, -forward.x) * halfWidth;
            Vector3 end = center + forward * length;
            float angle = Mathf.Atan2(forward.z, forward.x);
            ReachLine(center - side, end - side);
            ReachLine(center + side, end + side);
            ReachArc(center, halfWidth, angle + Mathf.PI * .5f, angle + Mathf.PI * 1.5f);
            ReachArc(end, halfWidth, angle - Mathf.PI * .5f, angle + Mathf.PI * .5f);
        }

        private void ReachArrow(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .001f) return;
            Vector3 direction = delta.normalized;
            Vector3 side = new Vector3(direction.z, 0f, -direction.x);
            float size = Mathf.Min(.28f, delta.magnitude * .2f);
            ReachLine(from, to);
            ReachLine(to, to - direction * size + side * size * .55f);
            ReachLine(to, to - direction * size - side * size * .55f);
        }

        private void ReachLine(Vector3 from, Vector3 to)
        {
            _rangePreview.Line(from,to);
        }
        /// <summary>Низ миникарты вместе с подписью, в пикселях экрана. Панели справа сверху встают ниже.</summary>
        internal static float MinimapBottom { get; private set; }

        internal static string AbilityDescription(int id)
        {
            if (id == AbilityDefinition.SkewerId) return "Выпад к курсору сквозь врагов. Каждый получает урон один раз. Прерывает текущую атаку.";
            if (id == AbilityDefinition.BackblastId) return "Бутылка взрывается под ногами, Пелаг отскакивает от курсора. Прерывает текущую атаку.";
            if (id == AbilityDefinition.WhirlwindId) return "Круговой удар саблей поражает врагов вокруг Пелага.";
            if (id == AbilityDefinition.CleaveId) return "Мощный удар саблей перед собой.";
            if (id == AbilityDefinition.BlazeId) return "Поджигает саблю и повышает уклонение. Можно применять на бегу.";
            if (id == AbilityDefinition.ChainStepId) return "Удары с переходами между врагами. Выбери первую цель.";
            if (id == AbilityDefinition.DashId) return "Кувырок в выбранном направлении.";
            if (id == AbilityDefinition.AnchorLeapId) return "Рывок к врагу на цепи с ударом кулака.";
            if (id == AbilityDefinition.AnchorSlamId) return "Удар якорем перед собой с коротким оглушением.";
            if (id == AbilityDefinition.WreckId) return "Три удара якорем: нажимай повторно. Последний оглушает.";
            if (id == AbilityDefinition.FireFlaskId) return "Взрыв в выбранной точке оставляет горящую область.";
            return "Описание этой способности пока недоступно.";
        }

        /// <summary>
        /// Клавиша слота в углу иконки. Какой ряд — решает игрок в настройках,
        /// и панель обязана показывать именно его: подпись, врущая про кнопку,
        /// хуже отсутствующей.
        ///
        /// Разбор ЗДЕСЬ идёт по номеру слота, а не по видимой позиции: пустые
        /// слоты панель пропускает, а клавиша у слота своя.
        /// </summary>
        internal static string SlotKey(int slot)
        {
            // Подпись из назначений игрока: своя клавиша видна сразу, без перезапуска.
            if (slot < 0 || slot > (int)GameAction.Dash) return string.Empty;
            string label = TickDriver.GamepadLastUsed
                ? (slot == 0 ? "LB" : slot == 1 ? "RB" : slot == 2 ? "X" : slot == 3 ? "Y" : "B")
                : GameKeyBindings.Label((GameAction)slot);
            return slot == DashSlot ? label.ToUpperInvariant() : label;
        }

        /// <summary>
        /// Иконка слота. Кэшируется по слоту, ищется по способности.
        ///
        /// Загрузка ленивая и одноразовая на слот: `Resources.Load` в OnGUI
        /// звался бы шестьдесят раз в секунду на каждый слот.
        /// </summary>
        private Texture2D AbilityIcon(int slot, int definitionId)
        {
            if (_abilityIcons == null || (uint)slot >= (uint)_abilityIcons.Length) return null;

            // КЛЮЧ — СЛОТ ПЛЮС СПОСОБНОСТЬ, А НЕ ОДИН СЛОТ. Кэш по номеру
            // слота живёт всю сессию, а содержимое слота меняется: смена ветки
            // в лагере кладёт в первую кнопку Удар якорем, и плитка продолжала
            // бы показывать Вихрь. Лишней загрузки нет — идентификатор совпадает
            // на всех кадрах, пока набор не сменили.
            if (_abilityIcons[slot] != null && _abilityIconIds[slot] == definitionId)
                return _abilityIcons[slot];

            string file = IconFile(definitionId);
            if (file == null) return null;

            _abilityIcons[slot] = Resources.Load<Texture2D>("UI/Abilities/" + file);
            _abilityIconIds[slot] = definitionId;
            return _abilityIcons[slot];
        }

        internal static string IconFile(int definitionId)
        {
            if (definitionId == AbilityDefinition.SkewerId) return "Icon_Skewer";
            if (definitionId == AbilityDefinition.BackblastId) return "Icon_Backblast";
            if (definitionId == AbilityDefinition.AnchorSlamId) return "Icon_AnchorSweep";
            if (definitionId == AbilityDefinition.WreckId) return "Icon_Wreck";
            if (definitionId == AbilityDefinition.FireFlaskId) return "Icon_FireFlask";
            if (definitionId == AbilityDefinition.CleaveId) return "Icon_Cleave";
            if (definitionId == AbilityDefinition.DashId) return "Icon_Dash";
            if (definitionId == AbilityDefinition.WhirlwindId) return "Icon_Whirlwind";
            if (definitionId == AbilityDefinition.AnchorLeapId) return "Icon_AnchorLeap";
            if (definitionId == AbilityDefinition.ChainStepId) return "Icon_Squall";
            if (definitionId == AbilityDefinition.BlazeId) return "Icon_Blaze";
            return null;
        }

        /// <summary>
        /// Имя способности во всплывающей подсказке.
        ///
        /// Разбор по DefinitionId, а не по номеру слота: слот — это позиция на
        /// панели, и она уже один раз переехала.
        /// </summary>
        internal static string AbilityName(int definitionId)
        {
            if (definitionId == AbilityDefinition.SkewerId) return "НА ВЫЛЕТ";
            if (definitionId == AbilityDefinition.BackblastId) return "ОТБОЙ";
            if (definitionId == AbilityDefinition.CleaveId) return "РАССЕКАЮЩИЙ УДАР";
            if (definitionId == AbilityDefinition.DashId) return "КУВЫРОК";
            if (definitionId == AbilityDefinition.WhirlwindId) return "ВИХРЬ";
            if (definitionId == AbilityDefinition.AnchorLeapId) return "АБОРДАЖ";
            if (definitionId == AbilityDefinition.AnchorSlamId) return "УДАР ЯКОРЕМ";
            if (definitionId == AbilityDefinition.ChainStepId) return "ШКВАЛ";
            if (definitionId == AbilityDefinition.BlazeId) return "ЛАДНО СМАЗАЛ";
            if (definitionId == AbilityDefinition.WreckId) return "КРУШЕНИЕ";
            if (definitionId == AbilityDefinition.FireFlaskId) return "ВЗРЫВНАЯ СМЕСЬ";
            return "СПОСОБНОСТЬ";
        }

        private static Rect Inset(Rect r, float by)
            => new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        private void Fill(Rect rect, Color color)
        {
            Color was = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = was;
        }

        private void Frame(Rect rect, Color color, float width)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private void OnDestroy()
        {
            _rangePreview?.Dispose();
            _chrome.Dispose();
            _minimap.Dispose();
            if (_white != null) Destroy(_white);
        }
        private void OnDisable()
        {
            _rangePreview?.Hide();
            if (_view != null) _view.gameObject.SetActive(false);
        }

        private void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (!_abilityIconsLoaded)
            {
                _abilityIconsLoaded = true;
                _abilityIcons = new Texture2D[Simulation.AbilitySlots];
                _abilityIconIds = new int[Simulation.AbilitySlots];
            }
            if (_label != null) return;

            _label = new GUIStyle(GameTypography.Label)
            {
                fontSize = 13, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft,
            };
            _label.font = GameTypography.Regular;
            _label.normal.textColor = new Color(1f, 0.96f, 0.92f);
            _smallLabel = new GUIStyle(_label) { fontSize = 10 };
            _smallLabel.normal.textColor = new Color(0.16f, 0.07f, 0.03f);
            _levelLabel = new GUIStyle(_label) { font = GameTypography.Semibold, fontSize = 16, fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(), margin = new RectOffset(), contentOffset = Vector2.zero };
            _levelLabel.normal.textColor = Paper;
            _slotLabel = new GUIStyle(_label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _slotLabel.normal.textColor = new Color(0.06f, 0.08f, 0.10f);
            _cooldownLabel = new GUIStyle(_slotLabel);
            _cooldownLabel.normal.textColor = new Color(1f, 0.97f, 0.90f);
            _slotKey = new GUIStyle(_slotLabel) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            _slotKey.normal.textColor = Color.white;
            _slotName = new GUIStyle(_slotLabel) { fontSize = 11 };
            _slotName.normal.textColor = new Color(1f, 0.92f, 0.80f);
            _mapLabel = new GUIStyle(_slotName) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _mapLabel.normal.textColor = new Color(0.96f, 0.86f, 0.66f);
            _tooltipTitle = new GUIStyle(_label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _tooltipBody = new GUIStyle(_label) { fontSize = 13, fontStyle = FontStyle.Normal, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _tooltipStats = new GUIStyle(_tooltipBody) { fontSize = 12 };
            _tooltipStats.normal.textColor = new Color(.78f, .76f, .71f);
            Font heading = GameTypography.Display;
            _xpLabel = new GUIStyle(_label) { font = GameTypography.Semibold, fontSize = 12, fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(), contentOffset = Vector2.zero };
            _heroName = new GUIStyle(_label) { font = heading, fontSize = 24, fontStyle = FontStyle.Normal, padding = new RectOffset() };
            _heroName.normal.textColor = Paper;
            _healthValue = new GUIStyle(_label) { font = GameTypography.Semibold, fontSize = 14, fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(), contentOffset = Vector2.zero };
            _healthValue.normal.textColor = Paper;
            _resourceName = new GUIStyle(_label) { fontSize = 9, fontStyle = FontStyle.Normal };
            _resourceName.normal.textColor = Paper;
            _resourceValue = new GUIStyle(_label) { fontSize = 10, alignment = TextAnchor.MiddleRight };
            _resourceValue.normal.textColor = Paper;
            _tooltipTitle.font = heading;
            _tooltipTitle.fontSize = 17;
            _tooltipTitle.fontStyle = FontStyle.Normal;
            _tooltipTitle.normal.textColor = new Color(.25f, .22f, .16f);
            _tooltipBody.normal.textColor = new Color(.35f, .31f, .23f);
            _tooltipBody.font = GameTypography.Regular;
            _tooltipBody.fontSize = 14;
            _tooltipKey = new GUIStyle(_slotKey) { font = GameTypography.Semibold, fontSize = 12, fontStyle = FontStyle.Normal };
            _tooltipKey.normal.textColor = new Color(.35f, .29f, .20f);
            _tooltipMetric = new GUIStyle(_tooltipTitle) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _tooltipCaption = new GUIStyle(_tooltipBody) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _tooltipCaption.normal.textColor = new Color(.48f, .41f, .30f);
            _feedbackLabel = new GUIStyle(_tooltipBody) { font=GameTypography.Semibold,fontSize=12,alignment=TextAnchor.MiddleCenter,wordWrap=false,padding=new RectOffset() };
            _feedbackLabel.normal.textColor = new Color(.64f,.29f,.15f);
            _mapLabel.font = heading;
            _mapLabel.fontSize = 14;
            _mapLabel.fontStyle = FontStyle.Normal;
            _mapLabel.normal.textColor = Paper;
        }
    }
}
