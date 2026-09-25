using Game.Sim;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>Что за метка на карте: от этого её оправа и порядок.</summary>
    internal enum MapMarkKind { Place, Fire, Exit, Reward, Drop, Boss, Enemy }

    /// <summary>Метка в координатах карты: начало — левый верхний угол, ось Y вниз.</summary>
    internal struct MapMark
    {
        public Vector2 Point;
        public int Symbol;
        public bool Nearby;
        public MapMarkKind Kind;
        public string Name;
        public float Distance;
    }

    // Схема лагеря кэшируется по снимку навигации, Разлома — по LayoutMap.
    // Координаты меток и рисунок используют одну проекцию с севером (+Z) наверху.
    internal sealed class HudMinimap
    {
        Texture2D _terrain, _player;
        RenderTexture _detail;
        Material _mapInk;
        Texture _detailSource;
        Rect _detailUv;
        readonly HudMapBackdrop _backdrop = new HudMapBackdrop();
        HudChrome _chrome;
        CampWalkMap _campMap;
        LayoutMap _riftMap;
        int _riftDepth = -1;
        LayoutView _layoutView;
        Rect _world, _view;
        Transform _fire;
        // Карта Разлома — чернильная схема проходов (концепт P4), а не снимок сцены: снимок
        // делался раз при входе, под туманом, и открытые потом места на нём не появлялись.
        bool _ink;
        readonly List<(Transform anchor, string name, int symbol)> _landmarks = new List<(Transform, string, int)>();
        GUIStyle _hintLabel, _captionLabel;
        string _hoverName, _caption;
        float _hoverDistance;
        Vector2 _hero;
        public Vector2 Pointer;

        /// <summary>
        /// Только данные: картинку, метки, героя и туман показывает холст боевого HUD
        /// (<see cref="HudMinimapMarks"/>). IMGUI в этом режиме ничего не рисует.
        /// </summary>
        public bool Bare;
        public string Caption => _caption;
        /// <summary>Картинка карты для RawImage префаба в режиме <see cref="Bare"/>.</summary>
        public Texture MapTexture => _detail != null ? _detail : (Texture)_terrain;
        /// <summary>Приближение содержимого; в Canvas-HUD задаётся полем префаба.</summary>
        public float Zoom = 1.819f;
        /// <summary>Отступ от края карты, за которым метка места прижимается к краю.</summary>
        public float Inset = 15f;

        /// <summary>Метки последнего кадра (режим <see cref="Bare"/>).</summary>
        public readonly List<MapMark> Marks = new List<MapMark>();
        public bool HasPlayer;
        public Vector2 PlayerPoint;
        /// <summary>Поворот стрелки по часовой, в градусах; 0 — на север.</summary>
        public float PlayerAngle;
        /// <summary>Маска тумана Разлома (белая, прозрачность — туман) и её участок под картой.</summary>
        public Texture FogMask;
        public Rect FogUv;

        /// <summary>Номер знака алхимика: в атласе мест его нет, картинку даёт холст.</summary>
        public const int AlchemistSymbol = 8;

        static readonly Color Rim = new Color(1f, .95f, .81f, 1f);
        static readonly Color Ground = new Color(.36f, .37f, .20f, .82f);
        static readonly Color Path = new Color(.64f, .57f, .38f, .88f);
        // Чернила карты Разлома (лист HUD P4): глубокая тень, чуть светлее проход, серебро кромки.
        static readonly Color InkOutside = new Color(.047f, .063f, .090f, 1f);
        static readonly Color InkFloor = new Color(.118f, .149f, .192f, 1f);
        static readonly Color InkEdge = new Color(.73f, .77f, .83f, 1f);

        public void DrawCamp(Rect panel, CampPlayerView camp, HudChrome chrome, GUIStyle label)
        {
            if (camp.WalkMap == null) return;
            if (_campMap != camp.WalkMap || _riftMap != null)
            {
                ClearTerrain();
                _campMap = camp.WalkMap;
                _riftMap = null;
                _ink = false;
                Bounds bounds = camp.MapBounds;
                float size = Mathf.Max(bounds.size.x, bounds.size.z) * 1.15f;
                _world = new Rect(bounds.center.x - size * .5f, bounds.center.z - size * .5f, size, size);
                const int resolution = 192;
                var pixels = new Color[resolution * resolution];
                for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                    {
                        var point = new FixVec2(Fix64.FromDouble(_world.x + (x + .5f) / resolution * size),
                            Fix64.FromDouble(_world.y + (y + .5f) / resolution * size));
                        pixels[y * resolution + x] = _campMap.Contains(point) ? Path : Ground;
                    }
                SetTerrain(pixels, resolution);
                _backdrop.Request(_world, camp.GroundHeight);
                CacheLandmarks(camp);
                _fire = null;
                var sound = Object.FindAnyObjectByType<CampSoundscape>();
                if (sound != null && sound.Layers != null)
                    foreach (var layer in sound.Layers)
                        if (layer != null && layer.Place == CampSoundscape.Place.Fire) { _fire = layer.Anchor; break; }
            }
            _caption = "Лагерь";
            _hero = new Vector2(camp.Position.x, camp.Position.z);
            DrawBase(panel, chrome, label);
            // Жители, которых ещё нет в лагере (алхимик до знакомства), выключены — их не отмечаем.
            foreach (var landmark in _landmarks)
                if (landmark.anchor != null && landmark.anchor.gameObject.activeInHierarchy)
                    Landmark(panel, landmark.anchor.position.x, landmark.anchor.position.z,
                        landmark.name, landmark.symbol, Rim, MapMarkKind.Place, chrome);
            if (_fire != null)
                Landmark(panel, _fire.position.x, _fire.position.z, "Костёр", 1,
                    new Color(1f, .66f, .27f), MapMarkKind.Fire, chrome);
            Vector3 hero = camp.Position;
            DrawPlayer(Project(panel, hero.x, hero.z), camp.Body != null ? camp.Body.eulerAngles.y : 0f);
            DrawHover(panel, chrome);
        }

        void CacheLandmarks(CampPlayerView camp)
        {
            _landmarks.Clear();
            if (camp.Tent != null) _landmarks.Add((camp.Tent, "Палатка Пелага", 0));
            // Жители — по самим NPC, а не по старым якорям сцены: так на карте и алхимик,
            // и метка стоит там, где житель стоит на самом деле.
            foreach (var npc in Object.FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
            {
                if (npc.Kind == CampServiceKind.Smith) _landmarks.Add((npc.transform, npc.Title, 2));
                else if (npc.Kind == CampServiceKind.Trader) _landmarks.Add((npc.transform, npc.Title, 3));
                else if (npc.Kind == CampServiceKind.Alchemist) _landmarks.Add((npc.transform, npc.Title, AlchemistSymbol));
            }
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            if (world == null || world.CampRoot == null) return;
            foreach (var anchor in world.CampRoot.GetComponentsInChildren<Transform>(true))
                if (anchor.name == "Anchor - Rift Portal") _landmarks.Add((anchor, "Вход в Разлом", 4));
        }

        public void DrawRift(Rect panel, RiftRun run, Simulation sim, HudChrome chrome, GUIStyle label)
        {
            LayoutMap map = run.Map;
            if (map == null || map.PlacedCount == 0) return;
            if (_riftMap != map || _riftDepth != run.Depth)
            {
                ClearTerrain();
                _riftMap = map;
                _riftDepth = run.Depth;
                _campMap = null;
                _ink = true;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                for (int i = 0; i < map.PlacedCount; i++)
                {
                    PlacedModule room = map.GetPlaced(i);
                    minX = Mathf.Min(minX, room.OriginX); minY = Mathf.Min(minY, room.OriginY);
                    maxX = Mathf.Max(maxX, room.OriginX + room.Width); maxY = Mathf.Max(maxY, room.OriginY + room.Height);
                }
                float cell = LayoutMap.CellSize.ToFloat();
                float size = Mathf.Max(maxX - minX, maxY - minY) * cell * 1.15f;
                _world = new Rect((minX + maxX) * .5f * cell - size * .5f,
                    (minY + maxY) * .5f * cell - size * .5f, size, size);
                BuildInk(map, size);
                _layoutView = Object.FindAnyObjectByType<LayoutView>();
            }
            _caption = "Разлом · " + run.Depth;
            var heroWorld = sim.Entities.Position[Simulation.PlayerId];
            _hero = new Vector2(heroWorld.X.ToFloat(), heroWorld.Y.ToFloat());
            DrawBase(panel, chrome, label);
            DrawFog(panel);
            for (int exit = 0; exit < map.ExitCount; exit++)
            {
                var point = map.ExitPoint(exit);
                if (!Revealed(point.X.ToFloat(), point.Y.ToFloat())) continue;
                Landmark(panel, point.X.ToFloat(), point.Y.ToFloat(), "Выход", 4, Rim, MapMarkKind.Exit, chrome);
            }
            for (int branch = 0; branch < map.RewardBranchCount; branch++)
            {
                if (run.IsBranchClaimed(branch)) continue;
                var point = map.CenterOf(map.GetRewardBranch(branch));
                if (!Revealed(point.X.ToFloat(), point.Y.ToFloat())) continue;
                Landmark(panel, point.X.ToFloat(), point.Y.ToFloat(), "Награда", 5, new Color(1f, .75f, .36f), MapMarkKind.Reward, chrome);
            }
            for (int d = 0; d < run.DropCount; d++)
            {
                RunDrop drop = run.GetDrop(d);
                if (drop.Claimed || !Revealed(drop.Position.X.ToFloat(), drop.Position.Y.ToFloat())) continue;
                Landmark(panel, drop.Position.X.ToFloat(), drop.Position.Y.ToFloat(),
                    drop.Offer.Kind == RewardKind.Ability ? "Способность" : "Предмет", 5, new Color(1f, .58f, .30f), MapMarkKind.Drop, chrome);
            }
            EntityStore entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                var world = entities.Position[i];
                if (i == Simulation.PlayerId) continue;
                if (!Revealed(world.X.ToFloat(), world.Y.ToFloat())) continue;
                Vector2 point = Project(panel, world.X.ToFloat(), world.Y.ToFloat());
                if (i == run.BossId)
                    Landmark(panel, world.X.ToFloat(), world.Y.ToFloat(), EnemyTexts.BossName(entities.Kind[i]), 6,
                        new Color(1f, .35f, .27f), MapMarkKind.Boss, chrome);
                else if (InsideMap(panel, point))
                {
                    if (Bare) { Marks.Add(new MapMark { Point = point, Kind = MapMarkKind.Enemy, Nearby = true }); continue; }
                    chrome.Shape(new Rect(point.x - 2.5f, point.y - 2.5f, 5f, 5f), new Color(.29f, .16f, .10f, .85f), 2.5f);
                    chrome.Shape(new Rect(point.x - 1.5f, point.y - 1.5f, 3f, 3f), new Color(1f, .43f, .30f), 1.5f);
                }
            }
            var player = entities.Position[Simulation.PlayerId];
            var facing = entities.Facing[Simulation.PlayerId];
            DrawPlayer(Project(panel, player.X.ToFloat(), player.Y.ToFloat()),
                Mathf.Atan2(facing.X.ToFloat(), facing.Y.ToFloat()) * Mathf.Rad2Deg);
            DrawHover(panel, chrome);
        }

        void DrawBase(Rect panel, HudChrome chrome, GUIStyle label)
        {
            _chrome = chrome;
            _hoverName = null;
            Marks.Clear();
            HasPlayer = false;
            FogMask = null;
            // На холсте картинку и её обрезку под рамку показывает RawImage под маской префаба.
            // Стили IMGUI здесь не создаются: этот путь идёт из LateUpdate, вне OnGUI.
            if (Bare) { UpdateView(); return; }
            if (_hintLabel == null)
            {
                _hintLabel = new GUIStyle(GameTypography.Label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, padding = new RectOffset() };
                _hintLabel.normal.textColor = new Color(.25f, .23f, .17f);
                _captionLabel = new GUIStyle(_hintLabel) { font = GameTypography.Display, fontSize = 16 };
                _captionLabel.normal.textColor = Rim;
            }
            UpdateView();
            chrome.Shape(new Rect(panel.x, panel.y + 2f, panel.width, panel.height), new Color(.13f, .13f, .08f, .25f), 24f);
            if (_detail != null)
                GUI.DrawTexture(panel, _detail, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, panel.width * .125f);
            else if (_terrain != null) chrome.Art(panel, _terrain, new Rect(0, 0, 1, 1));
            chrome.Shape(new Rect(panel.x + 2f, panel.y + 2f, panel.width - 4f, panel.height - 4f), new Color(.26f, .29f, .17f, .3f), panel.width * .125f - 2f, 1f);
            chrome.Shape(panel, Rim, panel.width * .125f, 1.5f);
            Rect caption = new Rect(panel.x + 28f, panel.yMax + 4f, panel.width - 56f, 23f);
            _captionLabel.normal.textColor = new Color(.20f, .22f, .14f, .85f);
            GUI.Label(new Rect(caption.x + 1f, caption.y + 1f, caption.width, caption.height), _caption, _captionLabel);
            _captionLabel.normal.textColor = Rim;
            GUI.Label(caption, _caption, _captionLabel);
        }

        void UpdateView()
        {
            float size = _world.width / Zoom;
            Vector2 center = new Vector2(
                Mathf.Clamp(_hero.x, _world.xMin + size * .5f, _world.xMax - size * .5f),
                Mathf.Clamp(_hero.y, _world.yMin + size * .5f, _world.yMax - size * .5f));
            _view = new Rect(center.x - size * .5f, center.y - size * .5f, size, size);
            Texture source = !_ink && _backdrop.Texture != null ? _backdrop.Texture : _terrain;
            if (source == null) return;
            Rect uv = new Rect((_view.x - _world.x) / _world.width, (_view.y - _world.y) / _world.height,
                _view.width / _world.width, _view.height / _world.height);
            if (_detail == null)
            {
                _detail = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32)
                { name = "HUD nearby terrain", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _detail.Create();
                var shader = Resources.Load<Shader>("UI/HUD/MinimapTerrain");
                if (shader != null) _mapInk = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_detailSource == source && _detailUv == uv) return;
            _detailSource = source;
            _detailUv = uv;
            // Двигается выборка из готового фона, а не дополнительная камера сцены.
            RenderTexture previous = RenderTexture.active;
            // Чернильную схему Разлома не осветляем: шейдер подгоняет под HUD только снимок лагеря.
            if (_mapInk != null && !_ink)
            {
                _mapInk.SetVector("_View", new Vector4(uv.x, uv.y, uv.width, uv.height));
                Graphics.Blit(source, _detail, _mapInk);
            }
            else Graphics.Blit(source, _detail, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = previous;
        }

        bool Revealed(float x, float z) => _layoutView == null || _layoutView.IsRevealed(x, z);

        // Рисует ту же маску тумана, что и 3D-сцена: круг вокруг игрока растёт
        // плавно, без нарезки по комнатам, и совпадает с тем, что видно в игре.
        void DrawFog(Rect panel)
        {
            if (_layoutView == null) return;
            Texture2D mask = _layoutView.FogMask;
            if (mask == null) return;
            float size = _layoutView.FogWorldSize;
            if (size <= 0f) return;
            Vector2 origin = _layoutView.FogOrigin;
            Rect uv = new Rect((_view.x - origin.x) / size, (_view.y - origin.y) / size,
                _view.width / size, _view.height / size);
            // Маска белая: на холсте её красят в чернила. Как есть она делала карту белым квадратом.
            if (Bare) { FogMask = mask; FogUv = uv; return; }
            GUI.DrawTextureWithTexCoords(panel, mask, uv);
        }

        void Landmark(Rect panel, float x, float z, string name, int symbol, Color color, MapMarkKind kind, HudChrome chrome)
        {
            Vector2 point = Project(panel, x, z);
            bool nearby = InsideMap(panel, point);
            if (!nearby)
            {
                Vector2 direction = point - panel.center;
                float extent = panel.width * .5f - Inset - 2f;
                point = panel.center + direction * (extent / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y)));
                if (Bare)
                {
                    Marks.Add(new MapMark { Point = point, Symbol = symbol, Nearby = false, Kind = kind, Name = name,
                        Distance = Vector2.Distance(_hero, new Vector2(x, z)) });
                    return;
                }
                // У края остаются компактные ориентиры; врагов за краем не рисуем.
                chrome.Shape(new Rect(point.x - 3f, point.y - 3f, 6f, 6f), color, 3f);
            }
            else if (Bare)
            {
                Marks.Add(new MapMark { Point = point, Symbol = symbol, Nearby = true, Kind = kind, Name = name,
                    Distance = Vector2.Distance(_hero, new Vector2(x, z)) });
                return;
            }
            float side = nearby ? 19f : 12f;
            Rect marker = new Rect(point.x - side * .5f, point.y - side * .5f, side, side);
            if (nearby)
                chrome.Shape(new Rect(point.x - 8f, point.y - 4f, 16f, 12f), new Color(.12f, .13f, .08f, .25f), 6f);
            HudSymbols.Map(marker, symbol);
            Rect target = new Rect(point.x - 10f, point.y - 10f, 20f, 20f);
            if (target.Contains(Pointer))
            {
                _hoverName = name;
                _hoverDistance = Vector2.Distance(_hero, new Vector2(x, z));
                chrome.Shape(new Rect(marker.x - 2f, marker.y - 2f, marker.width + 4f, marker.height + 4f), Rim, 6f, 1f);
            }
        }

        void DrawHover(Rect panel, HudChrome chrome)
        {
            if (Bare || _hoverName == null) return;
            string text = _hoverName + " · " + Mathf.RoundToInt(_hoverDistance) + " м";
            float scale = Mathf.Max(.01f, GUI.matrix.m00);
            Rect safe = Screen.safeArea;
            float left = safe.xMin / scale + 6f, right = safe.xMax / scale - 6f;
            float top = (Screen.height - safe.yMax) / scale + 6f;
            float bottom = (Screen.height - safe.yMin) / scale - 6f;
            // Крупнее и в цветах пака «Ночная акварель»: тёмная плашка, серебряная кромка, светлый текст.
            const float height = 40f;
            float width = Mathf.Min(right - left, _hintLabel.CalcSize(new GUIContent(text)).x + 32f);
            float x = Pointer.x + 16f;
            if (x + width > right) x = Pointer.x - width - 12f;
            Rect hint = new Rect(Mathf.Clamp(x, left, right - width), Mathf.Clamp(Pointer.y + 18f, top, bottom - height), width, height);
            chrome.Shape(new Rect(hint.x, hint.y + 3f, hint.width, hint.height), new Color(.01f, .02f, .04f, .45f), 8f);
            chrome.Shape(hint, new Color(.067f, .086f, .125f, .96f), 8f);
            chrome.Shape(hint, new Color(.85f, .88f, .93f, .8f), 8f, 1f);
            Color ink = _hintLabel.normal.textColor;
            _hintLabel.normal.textColor = new Color(.96f, .97f, .98f);
            GUI.Label(hint, text, _hintLabel);
            _hintLabel.normal.textColor = ink;
        }

        bool InsideMap(Rect panel, Vector2 point) => point.x >= panel.x + Inset && point.x <= panel.xMax - Inset
            && point.y >= panel.y + Inset && point.y <= panel.yMax - Inset;

        Vector2 Project(Rect panel, float x, float z) => new Vector2(
            panel.x + (x - _view.x) / _view.width * panel.width,
            panel.yMax - (z - _view.y) / _view.height * panel.height);

        void DrawPlayer(Vector2 point, float angle)
        {
            if (Bare) { HasPlayer = true; PlayerPoint = point; PlayerAngle = angle; return; }
            _chrome.Shape(new Rect(point.x - 8f, point.y - 8f, 16f, 16f), new Color(.20f, .24f, .15f, .4f), 8f);
            if (_player == null)
            {
                const int size = 48;
                _player = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float half = (size - 5f - y) * .48f;
                    if (y < 7 || y > size - 5 || Mathf.Abs(x - size * .5f) > half) continue;
                    pixels[y * size + x] = y < 11 || Mathf.Abs(x - size * .5f) > half - 4 ? Rim : new Color(.95f, .34f, .27f);
                }
                _player.SetPixels(pixels); _player.Apply(false, true);
                _player.wrapMode = TextureWrapMode.Clamp;
            }
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, point);
            GUI.DrawTexture(new Rect(point.x - 7f, point.y - 7f, 14f, 14f), _player);
            GUI.matrix = matrix;
        }

        /// <summary>
        /// Чернильная схема Разлома: пол — чуть светлее тени, по кромке прохода серебряная линия
        /// в один texel. Пол берётся из той же природной формы, по которой ходит Sim (полуметровые
        /// клетки), а без неё — из прямоугольников модулей.
        /// </summary>
        void BuildInk(LayoutMap map, float size)
        {
            int resolution = Mathf.Clamp(Mathf.CeilToInt(size / .4f), 192, 512);
            float perMetre = resolution / size;
            var floor = new bool[resolution * resolution];
            void Fill(float x0, float z0, float x1, float z1)
            {
                int px0 = Mathf.Clamp(Mathf.FloorToInt((x0 - _world.x) * perMetre), 0, resolution);
                int pz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - _world.y) * perMetre), 0, resolution);
                int px1 = Mathf.Clamp(Mathf.CeilToInt((x1 - _world.x) * perMetre), 0, resolution);
                int pz1 = Mathf.Clamp(Mathf.CeilToInt((z1 - _world.y) * perMetre), 0, resolution);
                for (int z = pz0; z < pz1; z++) for (int x = px0; x < px1; x++) floor[z * resolution + x] = true;
            }
            float cell = LayoutMap.CellSize.ToFloat();
            float step = NaturalOutline.Step.ToFloat();
            // В одной клетке модуля — 4×4 клетки природной формы (NaturalOutline).
            int fine = Mathf.RoundToInt(cell / step);
            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule room = map.GetPlaced(i);
                if (map.Outline == null)
                {
                    Fill(room.OriginX * cell, room.OriginY * cell, (room.OriginX + room.Width) * cell, (room.OriginY + room.Height) * cell);
                    continue;
                }
                for (int y = room.OriginY * fine; y < (room.OriginY + room.Height) * fine; y++)
                    for (int x = room.OriginX * fine; x < (room.OriginX + room.Width) * fine; x++)
                        if (map.Outline.ContainsCell(x, y)) Fill(x * step, y * step, (x + 1) * step, (y + 1) * step);
            }
            var pixels = new Color[floor.Length];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * resolution + x;
                    if (!floor[i]) { pixels[i] = InkOutside; continue; }
                    bool edge = x == 0 || z == 0 || x == resolution - 1 || z == resolution - 1
                        || !floor[i - 1] || !floor[i + 1] || !floor[i - resolution] || !floor[i + resolution];
                    // Лёгкая неровность заливки — акварельная, как у панелей пака, без узора.
                    float grain = (Mathf.PerlinNoise(x * .09f, z * .09f) - .5f) * .035f;
                    pixels[i] = edge ? InkEdge : new Color(InkFloor.r + grain, InkFloor.g + grain, InkFloor.b + grain, 1f);
                }
            SetTerrain(pixels, resolution);
            _terrain.filterMode = FilterMode.Bilinear;
        }

        void SetTerrain(Color[] pixels, int size)
        {
            _terrain = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD navigation map", wrapMode = TextureWrapMode.Clamp };
            _terrain.SetPixels(pixels); _terrain.Apply(false, false);
        }
        void ClearTerrain()
        {
            _detailSource = null;
            if (_terrain == null) return;
            _chrome?.Forget(_terrain);
            Object.Destroy(_terrain);
            _terrain = null;
            _backdrop.Invalidate();
        }
        public void Dispose()
        {
            ClearTerrain();
            _backdrop.Dispose();
            if (_player != null) Object.Destroy(_player);
            if (_mapInk != null) Object.Destroy(_mapInk);
            if (_detail != null) { _detail.Release(); Object.Destroy(_detail); }
        }
    }
}
