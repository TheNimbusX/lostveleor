#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class DeveloperMenu : MonoBehaviour
    {
        [Tooltip("Дополнительные локации вне Resources/Locations.")]
        public LocationTheme[] AdditionalLocations = Array.Empty<LocationTheme>();
        private TickDriver _driver;
        private LocationTheme[] _locations;
        private int _selected, _level = 1, _request;
        private int _heroLevel = 1;
        private int _loadoutSlot, _loadoutStep;
        private string _seed = "42", _error;
        private bool _open;
        private Vector2 _scroll;
        private GUIStyle _wrapped;
        public static bool CapturesEscape { get; private set; }
        private static int _closedFrame = -1;
        public static bool BlocksPause => CapturesEscape || _closedFrame == Time.frameCount;

        private void Awake() => _driver = GetComponent<TickDriver>();

        private void LoadLocations()
        {
            var list = new List<LocationTheme>();
            var current = GetComponent<LayoutView>().Profile;
            if (current != null && current.Gameplay != null) list.Add(current);
            foreach (var theme in Resources.LoadAll<LocationTheme>("Locations"))
                if (theme != null && theme.Gameplay != null && !list.Contains(theme)) list.Add(theme);
            foreach (var theme in AdditionalLocations)
                if (theme != null && theme.Gameplay != null && !list.Contains(theme)) list.Add(theme);
            _locations = list.ToArray();
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _locations.Length - 1));
        }

        private void Update()
        {
            if (_driver.Session == null) return;
#if ENABLE_INPUT_SYSTEM
            bool toggle = Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            bool toggle = Input.GetKeyDown(KeyCode.F8);
            bool escape = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (_open && (toggle || escape)) Close();
            else if (toggle && !_driver.GameplayPaused && CampPlayerView.Instance?.InventoryOpen != true)
            {
                LoadLocations(); _open = true; CapturesEscape = true;
                _driver.SetGameplayPaused(true);
            }
            if (_request == 0) return;
            int request = _request; _request = 0;
            try
            {
                if (request == 4) _driver.Session.SetDeveloperInvulnerable(!_driver.Session.DeveloperInvulnerable);
                else if (request == 5 || request == 7)
                {
                    if (request == 5) _driver.Session.Camp.DeveloperGrantLevel();
                    else _driver.Session.Camp.DeveloperSetLevel(_heroLevel);
                    // Статы уровня обязаны вырасти в текущем бою, а не со следующей расстановки.
                    _driver.Session.SyncPlayerLevel();
                }
                else if (request == 6 || request == 8)
                {
                    if (request == 6) DeveloperTalents.Clear();
                    _driver.RefreshAbilityBuild();
                }
                else if (request >= 9 && request <= 11)
                {
                    var session = _driver.Session;
                    var loadout = session.ActiveLoadout;
                    if (request == 9) CycleSlot(loadout, _loadoutSlot, _loadoutStep);
                    else if (request == 10)
                    {
                        for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++) loadout.Put(slot, Game.Sim.RunLoadout.EmptySlot);
                        for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++) loadout.Put(slot, slot);
                    }
                    else loadout.ResetToStarter();
                    // Набор забега — его добыча; правка из меню делает забег тестовым.
                    if (session.Mode == Game.Sim.GameMode.Rift) session.MarkDeveloperRun();
                    _driver.RefreshAbilityBuild();
                }
                else if (request == 3) _driver.ReturnToCampFromMenu();
                else
                {
                    if (!ulong.TryParse(_seed, out ulong seed)) throw new ArgumentException("Сид должен быть целым неотрицательным числом.");
                    var theme = _locations[_selected];
                    int level = _level;
                    if (request == 2)
                    {
                        level = 0;
                        for (int i = 0; i < theme.Gameplay.Levels.Length; i++)
                            if (theme.Gameplay.Levels[i].Boss) { level = i + 1; break; }
                        if (level == 0) throw new ArgumentException("В этой локации пока нет уровня с боссом.");
                    }
                    _driver.StartDeveloperRift(theme, level, request == 2, seed);
                    _level = level;
                }
                _error = null;
            }
            catch (Exception e) { _error = e.Message; }
        }

        private void Close()
        {
            if (!_open) return;
            _open = false; CapturesEscape = false; _closedFrame = Time.frameCount;
            _request = 0;
            _driver.SetGameplayPaused(false);
        }

        private void OnDisable() => Close();

        private void OnGUI()
        {
            if (!_open)
            {
                // Подсказка не рисуется поверх главного меню: там нет забега,
                // к которому относилось бы меню разработчика.
                if (MainMenuView.IsOpen) return;
                GUI.Label(new Rect(18, Screen.height - 32, 330, 24),
                    _driver.Session?.DeveloperInvulnerable == true ? "БЕССМЕРТИЕ · F8 — разработчик"
                    : _driver.Session?.IsDeveloperRun == true ? "ТЕСТОВЫЙ ЗАБЕГ · F8 — разработчик" : "F8 — меню разработчика");
                return;
            }
            float width = Mathf.Min(640, Screen.width - 24), height = Mathf.Min(760, Screen.height - 24);
            var panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14, panel.y + 14, panel.width - 28, panel.height - 28));
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_wrapped == null) _wrapped = new GUIStyle(GameTypography.Label) { wordWrap = true };
            GUILayout.Label("МЕНЮ РАЗРАБОТЧИКА · ПАУЗА");
            GUILayout.Label("Переход создаёт свежую тестовую карту. Текущий бой заменяется.", _wrapped);
            GUILayout.Label("Снаряжение сохраняется; наград за пропуск нет, добыча теста не переносится в сумку.", _wrapped);
            GUILayout.Space(12);
            if (_locations.Length > 0)
            {
                GUILayout.Label("Локация:");
                for (int i = 0; i < _locations.Length; i++)
                    if (GUILayout.Toggle(_selected == i, _locations[i].Gameplay.DisplayName, "Button") && _selected != i)
                    { _selected = i; _level = 1; _error = null; }
                int count = _locations[_selected].Gameplay.Levels.Length;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("−", GUILayout.Width(50))) _level--;
                _level = Mathf.Clamp(_level, 1, Mathf.Max(1, count));
                GUILayout.Label("Уровень " + _level + " / " + count);
                if (GUILayout.Button("+", GUILayout.Width(50))) _level = Mathf.Min(count, _level + 1);
                GUILayout.EndHorizontal();
                GUILayout.Label("Сид тестового забега:");
                _seed = GUILayout.TextField(_seed, 20);
                if (GUILayout.Button("Перейти на выбранный уровень", GUILayout.Height(36))) _request = 1;
                if (GUILayout.Button("К боссу · свежий бой", GUILayout.Height(42))) _request = 2;
            }
            else GUILayout.Label("Профили локаций не найдены.");
            bool canToggle = _driver.Session.Mode == Game.Sim.GameMode.Rift;
            GUI.enabled = canToggle;
            bool immortal = _driver.Session.DeveloperInvulnerable;
            if (GUILayout.Toggle(immortal, "Бессмертие персонажа", "Button", GUILayout.Height(36)) != immortal) _request = 4;
            GUI.enabled = true;
            if (!canToggle) GUILayout.Label("Для бессмертия сначала войди в любой разлом.", _wrapped);
            else GUILayout.Label("Бессмертие помечает текущий забег тестовым: добыча не переносится в сумку.", _wrapped);
            if (GUILayout.Button("Вернуться в лагерь", GUILayout.Height(32))) _request = 3;
            GUILayout.Space(8);
            var camp = _driver.Session.Camp;
            GUILayout.Label("Прокачка: уровень " + camp.Level + " · опыт " + camp.Experience + " / " + camp.ExperienceToNextLevel, _wrapped);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−", GUILayout.Width(50))) _heroLevel = Mathf.Max(1, _heroLevel - 1);
            GUILayout.Label("Уровень героя: " + _heroLevel);
            if (GUILayout.Button("+", GUILayout.Width(50))) _heroLevel++;
            if (GUILayout.Button("+10", GUILayout.Width(60))) _heroLevel += 10;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Поставить уровень " + _heroLevel, GUILayout.Height(32))) _request = 7;
            if (GUILayout.Button("+1 уровень", GUILayout.Height(32))) _request = 5;
            GUILayout.Space(8);
            DrawLoadout();
            GUILayout.Space(8);
            DrawTalentToggles();
            if (_driver.Session?.IsDeveloperRun == true)
                GUILayout.Label("Загружен тест: уровень " + _driver.Run.Depth + ". Закрой меню, чтобы начать бой.");
            if (!string.IsNullOrEmpty(_error)) GUILayout.Label("Ошибка: " + _error, _wrapped);
            GUILayout.Space(12);
            if (GUILayout.Button("Продолжить · F8 / Esc", GUILayout.Height(36))) Close();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Способности в четырёх слотах. В лагере правится лагерный набор — его
        /// же берут тестовые забеги из этого меню; в Разломе — набор забега.
        /// </summary>
        private void DrawLoadout()
        {
            var session = _driver.Session;
            var loadout = session.ActiveLoadout;
            GUILayout.Label(session.Mode == Game.Sim.GameMode.Rift
                ? "Способности забега · правка делает забег тестовым"
                : "Способности в лагере · тестовые забеги отсюда берут этот набор", _wrapped);
            for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++)
            {
                int pool = loadout.PoolIndexAt(slot);
                var definition = Game.Sim.PelagKit.PoolDefinition(pool);
                int rank = loadout.TalentRank(pool);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", GUILayout.Width(44))) { _loadoutSlot = slot; _loadoutStep = -1; _request = 9; }
                GUILayout.Label((slot + 1) + ": " + (definition == null ? "пусто" : PlayerHud.AbilityName(definition.Id))
                    + (rank > 0 ? " · талантов " + rank : ""));
                if (GUILayout.Button("▶", GUILayout.Width(44))) { _loadoutSlot = slot; _loadoutStep = 1; _request = 9; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Четыре сабельных", GUILayout.Height(30))) _request = 10;
            if (GUILayout.Button("Только Вихрь", GUILayout.Height(30))) _request = 11;
            GUILayout.EndHorizontal();
        }

        /// <summary>Следующая по кругу способность пула, которой нет в других слотах; «пусто» входит в круг.</summary>
        private static void CycleSlot(Game.Sim.RunLoadout loadout, int slot, int step)
        {
            int size = Game.Sim.PelagKit.PoolSize + 1;
            int current = loadout.PoolIndexAt(slot);
            for (int i = 1; i < size; i++)
            {
                int next = ((current + 1 + step * i) % size + size) % size - 1;
                if (next == Game.Sim.RunLoadout.EmptySlot || !loadout.Owns(next))
                {
                    loadout.Put(slot, next);
                    return;
                }
            }
        }

        private GUIStyle _talentButton;

        /// <summary>
        /// Двадцать сабельных талантов переключателями. Порядка и очков нет:
        /// это отладка визуала, а не прокачка — в игре таланты берутся в забеге.
        /// </summary>
        private void DrawTalentToggles()
        {
            if (_talentButton == null) _talentButton = new GUIStyle(GUI.skin.button) { wordWrap = true };
            GUILayout.Label("Таланты · отладка визуала · включено " + DeveloperTalents.Count, _wrapped);
            GUILayout.Label("Включаются поштучно, без порядка. Действуют в лагере, на полигоне и в забеге. Не сохраняются.", _wrapped);
            for (int line = 0; line < Game.Sim.SabreTalents.LineCount; line++)
            {
                var lineId = (Game.Sim.SabreTalentLine)line;
                GUILayout.Label(SabreTalentTexts.LineName(lineId));
                GUILayout.BeginHorizontal();
                for (int index = 0; index < Game.Sim.SabreTalents.TalentsPerLine; index++)
                {
                    bool on = DeveloperTalents.Has(lineId, index);
                    if (GUILayout.Toggle(on, SabreTalentTexts.Name(lineId, index), _talentButton, GUILayout.Height(48)) != on)
                    {
                        DeveloperTalents.Set(lineId, index, !on);
                        _request = 8;
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Снять все таланты", GUILayout.Height(32))) _request = 6;
        }
    }
}
#endif
