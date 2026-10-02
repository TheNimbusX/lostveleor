#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Меню разработчика (F8) — оболочка. Открытие и закрытие, пауза и timeScale, Esc и BlocksPause, очередь действий,
    /// шапка с чипами состояния, быстрая строка, вкладки «Забег · Бой · Пелаг · Визуал», колонки, подвал, запоминание.
    ///
    /// Пункты сюда не пишутся: их регистрируют через <see cref="DevMenu"/> — встроенные в DeveloperMenu.Run/Combat/
    /// Pelag/Visual.cs, формы в DeveloperMenu.Forms.cs. Раскладка и правила — аудит F8 02.10, Docs/DeveloperMenu.md.
    ///
    /// Чего не менять: действия только из Update (OnGUI зовётся несколько раз за кадр); OnDisable → Close (иначе из
    /// Play выходят с timeScale 0); Esc, закрывший меню, не открывает паузу в том же кадре (_closedFrame); на съёмке
    /// forest-bud меню не открывается.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(TickDriver))]
    public sealed partial class DeveloperMenu : MonoBehaviour
    {
        [Tooltip("Дополнительные локации вне Resources/Locations.")]
        public LocationTheme[] AdditionalLocations = Array.Empty<LocationTheme>();

        private const string SeedControl = "razlom-dev-seed";

        private struct Pending
        {
            public string Id;
            public Action<DevContext> Action;
            public DevFlags Flags;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private LocationTheme[] _locations = Array.Empty<LocationTheme>();
        private int _selected, _arena = 1;
        private string _seed = "42";
        private DevTab _tab = DevTab.Run;
        private bool _open, _seedEnter;
        private float _previousTimeScale = 1f;
        private readonly Vector2[] _scroll = new Vector2[DevMenuRules.TabCount];
        private readonly List<Pending> _queue = new List<Pending>();
        // Смена своего выбора (вкладка, арена, линия талантов) — тоже в Update: посреди события IMGUI она поменяла
        // бы число элементов между Layout и вводом.
        private readonly List<Action> _local = new List<Action>();
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        private string _lastError, _loadedNote;
        private DeveloperMenuSkin _skin;
        private DevUi _ui;

        public static bool CapturesEscape { get; private set; }
        private static int _closedFrame = -1;
        public static bool BlocksPause => CapturesEscape || _closedFrame == Time.frameCount;

        /// <summary>Меню открыто.</summary>
        internal bool IsOpen => _open;

        /// <summary>Открытая вкладка.</summary>
        internal DevTab CurrentTab => _tab;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _skin = new DeveloperMenuSkin();
            _ui = new DevUi(_skin, Enqueue, id => id != null && _errors.TryGetValue(id, out string error) ? error : null);
            LoadPrefs();
        }

        private void OnEnable() => RegisterBuiltIns();

        private void OnDisable()
        {
            Close();
            UnregisterBuiltIns();
        }

        private void OnDestroy() => _skin?.Dispose();

        private void RegisterBuiltIns()
        {
            RegisterRunEntries();
            RegisterCombatEntries();
            RegisterPelagEntries();
            RegisterVisualEntries();
        }

        private static void UnregisterBuiltIns()
        {
            foreach (string id in BuiltInIds) DevMenu.Unregister(id);
        }

        /// <summary>Все встроенные пункты — для снятия в OnDisable и проверки переноса (Docs/DeveloperMenu.md).</summary>
        internal static readonly string[] BuiltInIds =
        {
            "run.location", "run.arena", "run.seed", "run.start-arena", "run.boss", "camp.hero-level", "run.to-camp",
            "combat.immortal", "combat.bud", "combat.tempo-preset", "combat.tempo", "combat.sandbox",
            "pelag.slots", "pelag.preset-capture", "pelag.preset-starter", "pelag.artifact", "pelag.talents", "pelag.talents-clear",
            "visual.comic", "visual.mood", "visual.mood-mode", "visual.mood-status",
        };

        private void LoadLocations()
        {
            var list = new List<LocationTheme>();
            var current = Layout != null ? Layout.Profile : null;
            if (current != null && current.Gameplay != null) list.Add(current);
            foreach (var theme in Resources.LoadAll<LocationTheme>("Locations"))
                if (theme != null && theme.Gameplay != null && !list.Contains(theme)) list.Add(theme);
            foreach (var theme in AdditionalLocations)
                if (theme != null && theme.Gameplay != null && !list.Contains(theme)) list.Add(theme);
            _locations = list.ToArray();
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _locations.Length - 1));
            _arena = DevMenuRules.ClampArena(_arena, ArenaCount);
        }

        // Bootstrap добавляет LayoutView после меню: ищется при первом обращении, не в Awake.
        private LayoutView Layout => _layout != null ? _layout : (_layout = GetComponent<LayoutView>());

        private LocationTheme SelectedLocation => _locations.Length > 0 ? _locations[Mathf.Clamp(_selected, 0, _locations.Length - 1)] : null;

        private int ArenaCount
        {
            get
            {
                var theme = SelectedLocation;
                return theme != null && theme.Gameplay != null ? theme.Gameplay.Levels.Length : 0;
            }
        }

        private DevContext Context()
            => new DevContext(_driver, SelectedLocation, DevMenuRules.ClampArena(_arena, ArenaCount), ArenaCount, _seed);

        // ---- Update: клавиши, открытие, очередь ---------------------------------------------------

        private void Update()
        {
            if (CaptureRig.ForestBudShowcase) return;
            if (_driver.Session == null) return;
            _ui.Now = Time.unscaledTime;
            ReadKeys(out bool toggle, out bool escape, out int tabStep, out int tabDigit);
            if (_open && escape)
            {
                // Esc сначала снимает взвод подтверждения или фокус поля сида, и только потом закрывает.
                if (_ui.Disarm()) { }
                else if (GUIUtility.keyboardControl != 0) GUIUtility.keyboardControl = 0;
                else Close();
            }
            else if (_open && toggle) Close();
            else if (toggle && !_driver.GameplayPaused && CampPlayerView.Instance?.InventoryOpen != true) Open();
            else if (_open && GUIUtility.keyboardControl == 0)
            {
                if (tabDigit >= 0) SelectTab((DevTab)tabDigit);
                else if (tabStep != 0) SelectTab((DevTab)DevMenuRules.NextTab((int)_tab, tabStep));
            }
            if (!_open) return;
            if (_seedEnter)
            {
                _seedEnter = false;
                PressFromKeyboard("run.start-arena");
            }
            RunQueue();
        }

        private void Open()
        {
            LoadLocations();
            _heroLevel = Mathf.Max(1, _driver.Session.Camp.Level);
            _loadedNote = null;
            _lastError = null;
            _errors.Clear();
            _open = true;
            CapturesEscape = true;
            _previousTimeScale = _driver.GetComponent<CombatJuiceView>()?.CancelHitStopForPause() ?? Time.timeScale;
            _driver.SetGameplayPaused(true);
            Time.timeScale = 0;
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            CapturesEscape = false;
            _closedFrame = Time.frameCount;
            _queue.Clear();
            _local.Clear();
            _seedEnter = false;
            _ui?.Disarm();
            _loadedNote = null;
            GUIUtility.keyboardControl = 0;
            SavePrefs();
            _driver.SetGameplayPaused(false);
            Time.timeScale = _previousTimeScale > 0 ? _previousTimeScale : 1;
        }

        private void SelectTab(DevTab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _ui.Disarm();
            SaveInt(DevMenuRules.TabKey, (int)tab);
        }

        private void Enqueue(string id, Action<DevContext> action, DevFlags flags)
        {
            if (!_open || action == null) return;
            _queue.Add(new Pending { Id = id, Action = action, Flags = flags });
        }

        private void Local(Action change)
        {
            if (_open && change != null) _local.Add(change);
        }

        private void RunQueue()
        {
            if (_local.Count > 0)
            {
                var changes = _local.ToArray();
                _local.Clear();
                foreach (var change in changes) change();
            }
            if (_queue.Count == 0) return;
            var pending = _queue.ToArray();
            _queue.Clear();
            foreach (var item in pending)
            {
                if (!_open) break;
                try
                {
                    item.Action(Context());
                    if (item.Id != null) _errors.Remove(item.Id);
                    _lastError = null;
                    if ((item.Flags & DevFlags.CloseMenu) != 0) Close();
                }
                catch (Exception e)
                {
                    if (item.Id != null) _errors[item.Id] = e.Message;
                    _lastError = e.Message;
                }
            }
        }

        /// <summary>Enter в поле сида — как нажатие кнопки пункта (с тем же подтверждением и той же блокировкой).</summary>
        private void PressFromKeyboard(string id)
        {
            var context = Context();
            foreach (var section in DevMenu.SectionsOf(_tab))
                foreach (var entry in section.Entries)
                {
                    if (entry.Id != id || entry.Kind != DevEntryKind.Button) continue;
                    if (!entry.IsVisible(context) || entry.BlockedReason(context) != null) return;
                    if (_ui.Press(entry.Id, entry.Flags, context.RealRun)) Enqueue(entry.Id, entry.Run, entry.Flags);
                    return;
                }
        }

        private static void ReadKeys(out bool toggle, out bool escape, out int tabStep, out int tabDigit)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            toggle = escape = false;
            tabStep = 0;
            tabDigit = -1;
            if (keyboard == null) return;
            // Ctrl+Alt+F8 — пункт редактора «Проверить все EditMode-тесты»; меню на сочетания с модификаторами не отвечает.
            bool modifiers = keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed || keyboard.shiftKey.isPressed;
            toggle = keyboard.f8Key.wasPressedThisFrame && !modifiers;
            escape = keyboard.escapeKey.wasPressedThisFrame;
            if (keyboard.tabKey.wasPressedThisFrame) tabStep = keyboard.shiftKey.isPressed ? -1 : 1;
            if (keyboard.digit1Key.wasPressedThisFrame) tabDigit = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) tabDigit = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) tabDigit = 2;
            else if (keyboard.digit4Key.wasPressedThisFrame) tabDigit = 3;
#else
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool modifiers = shift || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                             || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            toggle = Input.GetKeyDown(KeyCode.F8) && !modifiers;
            escape = Input.GetKeyDown(KeyCode.Escape);
            tabStep = Input.GetKeyDown(KeyCode.Tab) ? (shift ? -1 : 1) : 0;
            tabDigit = Input.GetKeyDown(KeyCode.Alpha1) ? 0 : Input.GetKeyDown(KeyCode.Alpha2) ? 1
                : Input.GetKeyDown(KeyCode.Alpha3) ? 2 : Input.GetKeyDown(KeyCode.Alpha4) ? 3 : -1;
#endif
        }

        // ---- Что запоминается -------------------------------------------------------------------

        private void LoadPrefs()
        {
            _tab = (DevTab)Mathf.Clamp(LoadInt(DevMenuRules.TabKey, (int)DevTab.Run), 0, DevMenuRules.TabCount - 1);
            _arena = Mathf.Max(1, LoadInt(DevMenuRules.ArenaKey, 1));
            _tempoPreset = Mathf.Clamp(LoadInt(DevMenuRules.TempoKey, 2), 0, TempoPresets.Length - 1);
            _talentLine = Mathf.Clamp(LoadInt(DevMenuRules.TalentLineKey, 0), 0, Game.Sim.SabreTalents.LineCount - 1);
            try
            {
                string seed = PlayerPrefs.GetString(DevMenuRules.SeedKey, _seed);
                if (!string.IsNullOrEmpty(seed)) _seed = seed;
            }
            catch (Exception e) { Debug.LogWarning("[dev-menu] сид не прочитан: " + e.Message); }
        }

        private void SavePrefs()
        {
            if (CaptureRig.Installed) return;
            try
            {
                PlayerPrefs.SetString(DevMenuRules.SeedKey, _seed ?? string.Empty);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[dev-menu] выбор не сохранён: " + e.Message); }
        }

        private static int LoadInt(string key, int fallback)
        {
            try { return PlayerPrefs.GetInt(key, fallback); }
            catch (Exception e)
            {
                Debug.LogWarning("[dev-menu] " + key + " не прочитан: " + e.Message);
                return fallback;
            }
        }

        // Съёмка меню не оставляет свой выбор в настройках плеера.
        private static void SaveInt(string key, int value)
        {
            if (CaptureRig.Installed) return;
            try { PlayerPrefs.SetInt(key, value); }
            catch (Exception e) { Debug.LogWarning("[dev-menu] " + key + " не сохранён: " + e.Message); }
        }

        // ---- OnGUI ------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (_driver == null || _skin == null) return;
            _skin.Ensure(DevMenuRules.Scale(Screen.height, GameUserSettings.UiScale));
            if (!_open)
            {
                DrawClosedHint();
                return;
            }
            // Выше паузы и панели Лео (−100).
            GUI.depth = -200;
            ConsumeKeys();
            var context = Context();
            DevMenuRules.PanelSize(Screen.width, Screen.height, _skin.Scale, out float width, out float height);
            var panel = new Rect(Mathf.Round((Screen.width - width) * .5f), Mathf.Round((Screen.height - height) * .5f), width, height);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _skin.DimTexture);
            GUI.DrawTexture(panel, _skin.PanelTexture);
            float header = _skin.Px(50), quick = _skin.Px(60), tabs = _skin.Px(48), footer = _skin.Px(58);
            DrawHeader(new Rect(panel.x, panel.y, panel.width, header), context);
            DrawQuickBar(new Rect(panel.x, panel.y + header, panel.width, quick), context);
            DrawTabs(new Rect(panel.x, panel.y + header + quick, panel.width, tabs));
            float bodyTop = panel.y + header + quick + tabs;
            DrawBody(new Rect(panel.x, bodyTop, panel.width, panel.yMax - footer - bodyTop), context);
            DrawFooter(new Rect(panel.x, panel.yMax - footer, panel.width, footer));
        }

        /// <summary>Enter в поле сида и Tab без фокуса забирает меню, а не IMGUI (иначе Tab ставил бы фокус в поле).</summary>
        private void ConsumeKeys()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown) return;
            if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == SeedControl)
            {
                _seedEnter = true;
                current.Use();
            }
            else if ((current.keyCode == KeyCode.Tab || current.character == '\t') && GUIUtility.keyboardControl == 0)
                current.Use();
        }

        /// <summary>Подсказка при закрытом меню: чипы внизу слева. Не над главным меню и не в кадрах съёмки.</summary>
        private void DrawClosedHint()
        {
            if (MainMenuView.IsOpen || CaptureRig.Installed) return;
            var session = _driver.Session;
            GUILayout.BeginArea(new Rect(_skin.Px(18), Screen.height - _skin.Px(36), _skin.Px(640), _skin.Px(28)));
            GUILayout.BeginHorizontal();
            if (session != null && session.DeveloperInvulnerable) _ui.Chip("БЕССМЕРТИЕ", DevChip.On);
            if (session != null && session.IsDeveloperRun) _ui.Chip("ТЕСТОВЫЙ ЗАБЕГ", DevChip.Warn);
            _ui.Chip("F8 — меню разработчика", DevChip.Neutral);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawHeader(Rect rect, DevContext context)
        {
            GUI.Box(rect, GUIContent.none, _skin.Header);
            float pad = _skin.Px(18);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y, rect.width - 2 * pad, rect.height));
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label("МЕНЮ РАЗРАБОТЧИКА", _skin.Title, GUILayout.ExpandWidth(false));
            GUILayout.Space(_skin.Px(14));
            GUILayout.BeginVertical();
            GUILayout.Space(_skin.Px(3));
            GUILayout.BeginHorizontal();
            _ui.Chip(ModeTitle(context), DevChip.Neutral);
            if (context.TestRun) _ui.Chip("ТЕСТОВЫЙ", DevChip.Warn);
            if (context.Session.DeveloperInvulnerable) _ui.Chip("БЕССМЕРТИЕ", DevChip.On);
            if (!string.IsNullOrEmpty(_loadedNote)) _ui.Chip(_loadedNote, DevChip.Accent);
            GUILayout.FlexibleSpace();
            _ui.Chip("ПАУЗА", DevChip.Off);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private string ModeTitle(DevContext context)
        {
            var run = context.Run;
            int depth = run != null ? run.Depth : 0;
            var theme = Layout != null ? Layout.Profile : null;
            var levels = theme != null && theme.Gameplay != null ? theme.Gameplay.Levels : null;
            bool boss = levels != null && depth >= 1 && depth <= levels.Length && levels[depth - 1].Boss;
            return DevMenuRules.ModeTitle(context.Sandbox, context.InRift, context.Session.OnProvingGround, depth,
                levels != null ? levels.Length : 0, boss);
        }

        private void DrawQuickBar(Rect rect, DevContext context)
        {
            float pad = _skin.Px(18);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + _skin.Px(10), rect.width - 2 * pad + _skin.Px(6), rect.height - _skin.Px(10)));
            GUILayout.BeginHorizontal();
            foreach (var entry in DevMenu.QuickEntries())
            {
                if (!entry.InQuickBar(context)) continue;
                bool enabled = entry.BlockedReason(context) == null;
                if (entry.Kind == DevEntryKind.Toggle)
                {
                    bool on = entry.IsOn(context);
                    if (_ui.QuickToggle(entry.QuickLabelFor(context), on, enabled))
                        Enqueue(entry.Id, c => entry.Set(c, !on), entry.Flags);
                }
                else if (entry.Kind == DevEntryKind.Button
                         && _ui.QuickButton(entry.Id, entry.QuickLabelFor(context), entry.Flags, context.RealRun, enabled))
                    Enqueue(entry.Id, entry.Run, entry.Flags);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), _skin.LineTexture);
        }

        private void DrawTabs(Rect rect)
        {
            float pad = _skin.Px(18);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + _skin.Px(8), rect.width - 2 * pad, rect.height - _skin.Px(8)));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < DevMenuRules.TabCount; i++)
            {
                var tab = (DevTab)i;
                string title = (i + 1) + "  " + DevMenuRules.TabTitle(tab, DeveloperTalents.Count);
                if (GUILayout.Button(title, _tab == tab ? _skin.TabOn : _skin.Tab, GUILayout.MinWidth(_skin.Px(130)), GUILayout.ExpandWidth(false)) && _tab != tab)
                    Local(() => SelectTab(tab));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawBody(Rect rect, DevContext context)
        {
            float pad = _skin.Px(18);
            var inner = new Rect(rect.x + pad, rect.y + _skin.Px(12), rect.width - pad - _skin.Px(8), rect.height - _skin.Px(16));
            GUILayout.BeginArea(inner);
            int tab = (int)_tab;
            _scroll[tab] = GUILayout.BeginScrollView(_scroll[tab], false, false, GUI.skin.horizontalScrollbar, _skin.Scrollbar, GUIStyle.none);
            float content = inner.width - _skin.Px(18);
            DrawTab(_tab, context, content);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTab(DevTab tab, DevContext context, float content)
        {
            var sections = DevMenu.SectionsOf(tab);
            bool two = DevMenuRules.TwoColumns(content, _skin.Scale);
            float gap = _skin.Px(DevMenuRules.ColumnGap);
            bool any = false;
            int i = 0;
            while (i < sections.Count)
            {
                if (sections[i].Column == DevColumn.Full)
                {
                    if (AnyVisible(sections[i], context))
                    {
                        GUILayout.BeginVertical(GUILayout.Width(content));
                        DrawSection(sections[i], context);
                        GUILayout.EndVertical();
                        any = true;
                    }
                    i++;
                    continue;
                }
                // Подряд идущие колоночные секции — один блок: слева Left, справа Right.
                int end = i;
                bool left = false, right = false;
                for (; end < sections.Count && sections[end].Column != DevColumn.Full; end++)
                {
                    if (!AnyVisible(sections[end], context)) continue;
                    if (sections[end].Column == DevColumn.Right) right = true;
                    else left = true;
                }
                if (right && left && two)
                {
                    float column = DevMenuRules.ColumnWidth(content, _skin.Scale);
                    GUILayout.BeginHorizontal(GUILayout.Width(content));
                    GUILayout.BeginVertical(GUILayout.Width(column));
                    for (int k = i; k < end; k++)
                        if (sections[k].Column == DevColumn.Left && AnyVisible(sections[k], context)) DrawSection(sections[k], context);
                    GUILayout.EndVertical();
                    GUILayout.Space(gap);
                    GUILayout.BeginVertical(GUILayout.Width(column));
                    for (int k = i; k < end; k++)
                        if (sections[k].Column == DevColumn.Right && AnyVisible(sections[k], context)) DrawSection(sections[k], context);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    any = true;
                }
                else if (left || right)
                {
                    // Короткая вкладка — одна колонка 640 по левому краю; узкое окно — всё в одну колонку.
                    float width = right && left ? content : DevMenuRules.SingleWidth(content, _skin.Scale);
                    GUILayout.BeginVertical(GUILayout.Width(width));
                    for (int k = i; k < end; k++)
                        if (AnyVisible(sections[k], context)) DrawSection(sections[k], context);
                    GUILayout.EndVertical();
                    any = true;
                }
                i = end;
            }
            if (!any) _ui.Hint("Здесь пока пусто.");
        }

        private static bool AnyVisible(DevSection section, DevContext context)
        {
            foreach (var entry in section.Entries)
                if (entry.IsVisible(context)) return true;
            return false;
        }

        private void DrawSection(DevSection section, DevContext context)
        {
            GUILayout.BeginVertical(section.Danger ? _skin.DangerCard : _skin.Card);
            GUILayout.Label(section.Name.ToUpperInvariant(), _skin.SectionHeader);
            _ui.Hint(section.Hint);
            foreach (var entry in section.Entries)
                if (entry.IsVisible(context)) DrawEntry(entry, context);
            GUILayout.EndVertical();
            GUILayout.Space(_skin.Px(12));
        }

        private void DrawEntry(DevEntry entry, DevContext context)
        {
            string blocked;
            switch (entry.Kind)
            {
                case DevEntryKind.Button:
                    blocked = entry.BlockedReason(context);
                    if (_ui.Button(entry.Id, entry.LabelFor(context), entry.Flags, context.RealRun, blocked == null))
                        Enqueue(entry.Id, entry.Run, entry.Flags);
                    _ui.Consequences(entry.Flags, context);
                    _ui.Hint(blocked ?? entry.HintFor(context));
                    _ui.Error(entry.Id);
                    break;
                case DevEntryKind.Toggle:
                    blocked = entry.BlockedReason(context);
                    bool on = entry.IsOn(context);
                    if (_ui.ToggleRow(entry.LabelFor(context), on, blocked == null))
                        Enqueue(entry.Id, c => entry.Set(c, !on), entry.Flags);
                    _ui.Consequences(entry.Flags, context);
                    _ui.Hint(blocked ?? entry.HintFor(context));
                    _ui.Error(entry.Id);
                    break;
                case DevEntryKind.Choice:
                    GUILayout.Label(entry.LabelFor(context), _skin.Label);
                    int shown = entry.Get(context);
                    int chosen = _ui.Grid(shown, entry.Options);
                    if (chosen != shown) Enqueue(entry.Id, c => entry.Choose(c, chosen), entry.Flags);
                    _ui.Consequences(entry.Flags, context);
                    _ui.Hint(entry.HintFor(context));
                    _ui.Error(entry.Id);
                    break;
                default:
                    entry.Draw(context, _ui);
                    break;
            }
            GUILayout.Space(_skin.Px(6));
        }

        private void DrawFooter(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, _skin.Footer);
            float pad = _skin.Px(18);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y, rect.width - 2 * pad, rect.height));
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(_lastError)) GUILayout.Label("Ошибка: " + _lastError, _skin.ErrorText, GUILayout.ExpandWidth(true));
            else GUILayout.Label("1–4 или Tab — вкладки · Esc снимает подтверждение, потом закрывает", _skin.Hint, GUILayout.ExpandWidth(true));
            GUILayout.Space(_skin.Px(12));
            if (_ui.Plain("Продолжить · F8 / Esc", true, GUILayout.Width(_skin.Px(260)))) Local(Close);
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // ---- Съёмка меню (DeveloperMenuCapture, только -capture-dev-menu) -------------------------

        internal void CaptureOpen()
        {
            if (!_open) Open();
        }

        internal void CaptureSelectTab(DevTab tab) => SelectTab(tab);

        /// <summary>Взвести подтверждение пункта, как первым нажатием, — кадр «Ещё раз — …».</summary>
        internal void CaptureArm(string id)
        {
            var context = Context();
            foreach (var section in DevMenu.SectionsOf(_tab))
                foreach (var entry in section.Entries)
                    if (entry.Id == id && entry.Kind == DevEntryKind.Button) _ui.Press(entry.Id, entry.Flags | DevFlags.Confirm, context.RealRun);
        }

        internal void CaptureClose() => Close();
    }
}
#endif
