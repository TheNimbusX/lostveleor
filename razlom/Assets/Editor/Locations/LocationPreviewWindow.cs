using System;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.LocationEditor
{
    public sealed class LocationPreviewWindow : EditorWindow
    {
        [SerializeField] private LocationTheme _profile;
        [SerializeField] private string _seed = "42";
        [SerializeField] private int _level = 1;
        [SerializeField] private bool _floor = true, _connectors = true, _spawns = true, _labels = true;
        [SerializeField] private int _display;
        [SerializeField] private Vector2 _orbit = new Vector2(0, 65);
        private LocationPreview _preview;
        private Vector2 _scroll;
        private Vector3 _focus;
        private float _size = 60;
        private bool _pending, _stale = true, _frameAfterBuild;
        private string _error;
        private int _selectedModule = -1;

        [MenuItem("Разлом/Локации/Мастерская локации %&l", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<LocationPreviewWindow>("Локация");
            window.minSize = new Vector2(820, 540);
            if (window._profile == null) window._profile = MeadowLocationAssets.EnsureCreated();
            window._frameAfterBuild = true;
            window._pending = true;
            window.Show();
        }

        public static void OpenProfile(LocationTheme profile)
        {
            var window = GetWindow<LocationPreviewWindow>("Локация");
            window.minSize = new Vector2(820, 540);
            window._profile = profile;
            window._frameAfterBuild = window._pending = true;
            window.Show();
        }

        private void OnEnable()
        {
            _preview = new LocationPreview();
            EditorApplication.update += UpdatePreview;
            EditorApplication.projectChanged += MarkStale;
            Undo.undoRedoPerformed += MarkStale;
            if (_profile != null) { _pending = true; _frameAfterBuild = true; }
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.projectChanged -= MarkStale;
            Undo.undoRedoPerformed -= MarkStale;
            _preview?.Dispose();
            _preview = null;
        }

        private void MarkStale() { _stale = true; Repaint(); }

        private void UpdatePreview()
        {
            if (!_pending || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            _pending = false;
            try
            {
                if (!ulong.TryParse(_seed, out ulong seed))
                    throw new ArgumentException("Сид — целое число от 0 до 18446744073709551615.");
                _preview.Generate(_profile, seed, _level);
                _error = null;
                _stale = false;
                _selectedModule = -1;
                if (_frameAfterBuild) FrameMap();
                _frameAfterBuild = false;
            }
            catch (Exception e)
            {
                _preview.Clear();
                _error = e.Message;
                _stale = true;
            }
            Repaint();
        }

        private void FrameMap()
        {
            if (_preview.Map == null) return;
            _focus = _preview.Bounds.center;
            _size = Mathf.Max(12, _preview.Bounds.extents.magnitude * 1.15f);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("ЛУГОВАЯ ОКРАИНА", EditorStyles.boldLabel, GUILayout.Width(180));
                _display = GUILayout.Toolbar(_display, new[] { "Окружение", "Схема" }, EditorStyles.toolbarButton, GUILayout.Width(200));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Вписать", EditorStyles.toolbarButton, GUILayout.Width(70))) FrameMap();
                if (GUILayout.Button("Сверху", EditorStyles.toolbarButton, GUILayout.Width(70))) _orbit = new Vector2(0, 90);
                if (GUILayout.Button("Объём", EditorStyles.toolbarButton, GUILayout.Width(70))) _orbit = new Vector2(-25, 60);
            }
            Rect panel = new Rect(0, 24, 310, position.height - 24);
            GUILayout.BeginArea(panel, EditorStyles.helpBox);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettings();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            Rect viewport = new Rect(314, 24, Mathf.Max(1, position.width - 314), Mathf.Max(1, position.height - 48));
            EditorGUI.DrawRect(viewport, new Color(0.12f, 0.15f, 0.14f));
            if (_preview?.Map != null)
            {
                HandleCamera(viewport);
                if (Event.current.type == EventType.Repaint)
                {
                    var texture = _preview.Render(viewport, _focus, _orbit, _size, _display == 0);
                    if (texture != null) GUI.DrawTexture(viewport, texture, ScaleMode.StretchToFill, false);
                }
                DrawOverlay(viewport);
            }
            else GUI.Label(new Rect(viewport.x + 20, viewport.center.y - 20, viewport.width - 40, 50),
                "Выберите профиль и нажмите «Пересобрать».", EditorStyles.centeredGreyMiniLabel);
            GUI.Label(new Rect(320, position.height - 22, position.width - 330, 20),
                "ЛКМ — модуль · ПКМ — вращение · СКМ — сдвиг · колесо — масштаб", EditorStyles.miniLabel);
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Профиль локации", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _profile = (LocationTheme)EditorGUILayout.ObjectField(_profile, typeof(LocationTheme), false);
            int levels = _profile != null && _profile.Gameplay != null ? _profile.Gameplay.Levels?.Length ?? 1 : 1;
            _level = EditorGUILayout.IntSlider("Уровень разлома", _level, 1, Mathf.Max(1, levels));
            _seed = EditorGUILayout.TextField("Сид забега", _seed);
            if (EditorGUI.EndChangeCheck()) _stale = true;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Пересобрать", GUILayout.Height(28))) _pending = true;
                if (GUILayout.Button("Следующий сид", GUILayout.Height(28)))
                {
                    if (ulong.TryParse(_seed, out ulong seed)) _seed = unchecked(seed + 1).ToString();
                    else _seed = "1";
                    _pending = _frameAfterBuild = true;
                }
            }
            if (_stale && _preview.Map != null)
                EditorGUILayout.HelpBox("Настройки могли измениться. Нажмите «Пересобрать».", MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);

            using (new EditorGUI.DisabledScope(_profile == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Оформление")) Selection.activeObject = _profile;
                    if (GUILayout.Button("Генерация / уровни")) Selection.activeObject = _profile.Gameplay;
                }
            }
            if (Application.isPlaying && GUILayout.Button("Взять сид текущего забега"))
            {
                var driver = FindAnyObjectByType<TickDriver>();
                if (driver?.Run != null)
                {
                    _seed = driver.Session.LastRunSeed.ToString();
                    _level = Mathf.Min(driver.Run.Depth, Mathf.Max(1, levels));
                    _pending = _frameAfterBuild = true;
                }
            }
            EditorGUILayout.HelpBox("Это сид отдельного забега. Поле Run Seed у Bootstrap задаёт сид всей сессии.", MessageType.None);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Наложения", EditorStyles.boldLabel);
            _floor = EditorGUILayout.ToggleLeft("Проходимая область", _floor);
            _connectors = EditorGUILayout.ToggleLeft("Стыковки и реальные проходы", _connectors);
            _spawns = EditorGUILayout.ToggleLeft("Игрок и враги при входе", _spawns);
            _labels = EditorGUILayout.ToggleLeft("Номера модулей и выходы", _labels);
            EditorGUILayout.HelpBox("Зелёный — вход; охра — выход; голубой — проход; оранжевый — свободный коннектор; красный — враги.", MessageType.None);
            if (_preview.Map != null) DrawStatistics();
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("Свет в окне — нейтральный для проверки материалов. Финальное освещение настраивается в сцене. Фон, тропы и декор сами по себе не добавляют проходимость.", MessageType.None);
            if (GUILayout.Button("Убрать предпросмотр")) _preview.Clear();
        }

        private void DrawStatistics()
        {
            var map = _preview.Map;
            EditorGUILayout.LabelField("Собранная карта", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Модулей: {map.PlacedCount} · выходов: {map.ExitCount}");
            EditorGUILayout.LabelField($"Врагов: {_preview.Sim.Entities.Count - 1} · декора: {_preview.View.DecorCount}");
            EditorGUILayout.LabelField($"Объектов в пулах: {_preview.View.PooledCount}");
            EditorGUILayout.SelectableLabel($"Layout seed: {_preview.Seeds.Layout}\nSpawn seed: {_preview.Seeds.Spawns}\nMap hash: {map.Hash():X16}", EditorStyles.miniLabel, GUILayout.Height(48));
            if (map.PlacedCount < _preview.Settings.TargetModules)
                EditorGUILayout.HelpBox("Генератор исчерпал подходящие стыковки раньше заданного числа модулей. Проверьте формы и коннекторы.", MessageType.Warning);
            if (map.ExitCount < _preview.Settings.ExitCount)
                EditorGUILayout.HelpBox("Подходящих дальних тупиков меньше, чем запрошенных выходов.", MessageType.Warning);
            if (_selectedModule < 0 || _selectedModule >= map.PlacedCount) return;
            var p = map.GetPlaced(_selectedModule);
            var module = map.Modules.Get(p.ModuleIndex);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Модуль #{_selectedModule}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Клетки: {p.Width} × {p.Height}; поворот: {p.Quarters * 90}°");
            EditorGUILayout.LabelField($"Начало: ({p.OriginX}, {p.OriginY}); глубина: {map.DepthOf(_selectedModule)}");
            if (_profile?.Gameplay?.Modules == null) return;
            foreach (var asset in _profile.Gameplay.Modules)
                if (asset != null && StableId.Of(asset.StableKey) == module.Id)
                {
                    if (GUILayout.Button("Править " + asset.name)) Selection.activeObject = asset;
                    break;
                }
        }

        private Vector2 Project(Vector3 point, Rect area)
        {
            var p = _preview.Camera.WorldToViewportPoint(point);
            return new Vector2(p.x * area.width, (1 - p.y) * area.height);
        }

        private void DrawOverlay(Rect area)
        {
            GUI.BeginGroup(area);
            Handles.BeginGUI();
            var map = _preview.Map;
            float cell = LayoutMap.CellSize.ToFloat();
            Color old = Handles.color;
            for (int i = 0; i < map.PlacedCount; i++)
            {
                var p = map.GetPlaced(i);
                Color color = i == 0 ? new Color(0.3f, 1f, 0.4f) : map.IsExit(i)
                    ? new Color(1f, 0.7f, 0.2f) : new Color(0.45f, 0.8f, 1f);
                var corners = new Vector3[] {
                    Project(new Vector3(p.OriginX * cell, 0, p.OriginY * cell), area),
                    Project(new Vector3((p.OriginX + p.Width) * cell, 0, p.OriginY * cell), area),
                    Project(new Vector3((p.OriginX + p.Width) * cell, 0, (p.OriginY + p.Height) * cell), area),
                    Project(new Vector3(p.OriginX * cell, 0, (p.OriginY + p.Height) * cell), area) };
                if (_floor || _display == 1)
                    Handles.DrawSolidRectangleWithOutline(corners, new Color(color.r, color.g, color.b, _display == 1 ? 0.18f : 0.06f), color);
                Vector2 center = Project(ToWorld(map.CenterOf(i)), area);
                if (_labels)
                    GUI.Label(new Rect(center.x - 28, center.y - 12, 100, 20),
                        i == 0 ? "ВХОД" : map.IsExit(i) ? $"#{i} ВЫХОД" : $"#{i}", EditorStyles.whiteMiniLabel);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    Vector2.Distance(Event.current.mousePosition, center) < 25)
                { _selectedModule = i; Event.current.Use(); Repaint(); }
                if (!_connectors) continue;
                var definition = map.Modules.Get(p.ModuleIndex);
                for (int c = 0; c < definition.ConnectorCount; c++)
                {
                    var connector = definition.RotatedConnector(c, p.Quarters);
                    Directions.Step(connector.Facing, out int dx, out int dy);
                    var start = new Vector3((p.OriginX + connector.X + 0.5f) * cell, 0, (p.OriginY + connector.Y + 0.5f) * cell);
                    var beyond = start + new Vector3(dx, 0, dy) * cell;
                    bool joined = map.ContainsWorld(new FixVec2(Fix64.FromDouble(beyond.x), Fix64.FromDouble(beyond.z)));
                    Handles.color = joined ? Color.cyan : new Color(1, 0.55f, 0.15f);
                    Handles.DrawAAPolyLine(3, Project(start, area), Project(start + new Vector3(dx, 0, dy) * cell * 0.6f, area));
                }
            }
            if (_connectors) DrawActualPassages(area, cell);
            if (_spawns)
            {
                var entities = _preview.Sim.Entities;
                for (int i = 0; i < entities.Count; i++)
                {
                    Vector2 point = Project(ToWorld(entities.Position[i]), area);
                    Color color = i == Simulation.PlayerId ? Color.green : new Color(1, 0.3f, 0.25f);
                    EditorGUI.DrawRect(new Rect(point.x - 2.5f, point.y - 2.5f, 5, 5), color);
                }
            }
            Handles.color = old;
            Handles.EndGUI();
            GUI.EndGroup();
        }

        private void DrawActualPassages(Rect area, float cell)
        {
            // Entire shared rectangle edges are walkable in the current simulation.
            // Drawing only the authored connector would falsely imply a one-cell doorway.
            var map = _preview.Map;
            Handles.color = Color.cyan;
            for (int a = 0; a < map.PlacedCount; a++)
                for (int b = a + 1; b < map.PlacedCount; b++)
                {
                    var p = map.GetPlaced(a); var q = map.GetPlaced(b);
                    int min = Math.Max(p.OriginY, q.OriginY), max = Math.Min(p.OriginY + p.Height, q.OriginY + q.Height);
                    if (min < max && (p.OriginX + p.Width == q.OriginX || q.OriginX + q.Width == p.OriginX))
                    {
                        float x = Math.Max(p.OriginX, q.OriginX) * cell;
                        Handles.DrawAAPolyLine(4, Project(new Vector3(x, 0, min * cell), area), Project(new Vector3(x, 0, max * cell), area));
                    }
                    min = Math.Max(p.OriginX, q.OriginX); max = Math.Min(p.OriginX + p.Width, q.OriginX + q.Width);
                    if (min < max && (p.OriginY + p.Height == q.OriginY || q.OriginY + q.Height == p.OriginY))
                    {
                        float y = Math.Max(p.OriginY, q.OriginY) * cell;
                        Handles.DrawAAPolyLine(4, Project(new Vector3(min * cell, 0, y), area), Project(new Vector3(max * cell, 0, y), area));
                    }
                }
        }

        private static Vector3 ToWorld(FixVec2 p) => new Vector3(p.X.ToFloat(), 0, p.Y.ToFloat());

        private void HandleCamera(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type == EventType.ScrollWheel)
            { _size = Mathf.Clamp(_size * Mathf.Exp(e.delta.y * 0.07f), 3, 600); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDrag && e.button == 1)
            { _orbit.x += e.delta.x * 0.5f; _orbit.y = Mathf.Clamp(_orbit.y + e.delta.y * 0.5f, 15, 90); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                Vector3 right = Quaternion.Euler(0, _orbit.x, 0) * Vector3.right;
                Vector3 forward = Quaternion.Euler(0, _orbit.x, 0) * Vector3.forward;
                _focus += (-right * e.delta.x + forward * e.delta.y) * (_size * 2 / area.height);
                e.Use(); Repaint();
            }
        }
    }
}
