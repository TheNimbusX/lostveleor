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
        // Снимок лагеря подгоняется под HUD шейдером MinimapTerrain, схема Разлома рисуется
        // по полю расстояний шейдером MinimapInk.
        Material _terrainMat, _inkMat;
        bool _materialsTried;
        Texture _detailSource;
        Rect _detailUv;
        // Поле расстояний Разлома: метры до кромки прохода (внутри > 0) по квадрату _world и
        // перевод значения в метры (RHalf — как есть, R8 — из 0..1).
        Texture2D _field;
        Vector2 _fieldDecode = new Vector2(1f, 0f);
        float _panelWidth = 1f;
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
        /// <summary>
        /// Ширина карты на экране в пикселях (ставит <see cref="HudMinimapMarks"/>); 0 — неизвестна.
        /// По ней подбирается размер картинки: без лишнего сжатия кромка не мерцает при движении.
        /// </summary>
        public float PixelSize;

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
        // Чернила карты Разлома (лист HUD P4): глубокая тень, чуть светлее проход. Кромка — мягкая
        // кремовая линия кистью (владелец 26 сентября: серебряная лесенка в texel «очень пиксельная»).
        static readonly Color InkOutside = new Color(.047f, .063f, .090f, 1f);
        static readonly Color InkFloor = new Color(.118f, .149f, .192f, 1f);
        static readonly Color InkEdge = new Color(.95f, .87f, .72f, .9f);
        // Линия кисти и её дрожание — в единицах холста карты: одинаковые на любом уровне.
        const float EdgeWidth = 2.6f, EdgeWobble = 1.3f;
        // Поле расстояний: шаг выборки (в клетке природной формы 2×2 выборки), предел и
        // сглаживание в метрах — ступени по полметра скругляются, прямые края стоят на месте.
        const float FieldStep = .25f, FieldRange = 3f, FieldBlur = .55f;
        // Карта — округлый клуб (map_shape: суперэллипс 2,4): «рядом» и прижатие меток к краю
        // считаются по той же форме, иначе метки в углах висели бы за краем клуба.
        const float ShapePower = 2.4f, ShapeRim = .92f;

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
                BuildInk(map);
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
            _panelWidth = Mathf.Max(1f, panel.width);
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
            LoadMaterials();
            // Разлом: поле расстояний под шейдер, без шейдера — запасная раскраска поля (_terrain).
            Texture source = _ink ? (_inkMat != null && _field != null ? _field : (Texture)_terrain)
                : _backdrop.Texture != null ? _backdrop.Texture : _terrain;
            if (source == null) return;
            Rect uv = new Rect((_view.x - _world.x) / _world.width, (_view.y - _world.y) / _world.height,
                _view.width / _world.width, _view.height / _world.height);
            int side = DetailSide();
            if (_detail != null && _detail.width != side)
            {
                _detail.Release();
                Object.Destroy(_detail);
                _detail = null;
            }
            if (_detail == null)
            {
                // С мипами: карту на экране сдвигают и масштабируют анимации HUD.
                _detail = new RenderTexture(side, side, 0, RenderTextureFormat.ARGB32)
                {
                    name = "HUD nearby terrain", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = true, autoGenerateMips = true,
                };
                _detail.Create();
                _detailSource = null;
            }
            if (_detailSource == source && _detailUv == uv) return;
            _detailSource = source;
            _detailUv = uv;
            // Двигается выборка из готового фона, а не дополнительная камера сцены.
            RenderTexture previous = RenderTexture.active;
            if (_ink && source == _field)
            {
                // Кисть задана в единицах холста: метров в единице — ширина вида на ширину карты.
                float unit = _view.width / _panelWidth;
                _inkMat.SetVector("_View", new Vector4(_view.x, _view.y, _view.width, _view.height));
                _inkMat.SetVector("_Field", new Vector4(_world.x, _world.y, _world.width, _world.height));
                _inkMat.SetVector("_Decode", new Vector4(_fieldDecode.x, _fieldDecode.y, 0f, 0f));
                _inkMat.SetVector("_Brush", new Vector4(_view.width / side, EdgeWidth * unit, EdgeWobble * unit, unit));
                Graphics.Blit(source, _detail, _inkMat);
            }
            // Чернильную схему Разлома не осветляем: шейдер подгоняет под HUD только снимок лагеря.
            else if (_terrainMat != null && !_ink)
            {
                _terrainMat.SetVector("_View", new Vector4(uv.x, uv.y, uv.width, uv.height));
                Graphics.Blit(source, _detail, _terrainMat);
            }
            else Graphics.Blit(source, _detail, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = previous;
        }

        /// <summary>
        /// Сторона картинки: по ширине карты на экране, кратно 64. Растёт сразу, а уменьшается,
        /// только когда вдвое больше нужного, — анимации HUD не пересоздают её каждый кадр.
        /// </summary>
        int DetailSide()
        {
            int current = _detail != null ? _detail.width : 512;
            // Совсем мелкая — карта спрятана или ещё не разложена: размер не трогаем.
            if (PixelSize < 64f) return current;
            int need = Mathf.Clamp(Mathf.CeilToInt(PixelSize / 64f) * 64, 128, 1024);
            return _detail != null && need <= current && current <= need * 2 ? current : need;
        }

        void LoadMaterials()
        {
            if (_materialsTried) return;
            _materialsTried = true;
            var terrain = Resources.Load<Shader>("UI/HUD/MinimapTerrain");
            if (terrain != null && terrain.isSupported) _terrainMat = new Material(terrain) { hideFlags = HideFlags.HideAndDontSave };
            var ink = Resources.Load<Shader>("UI/HUD/MinimapInk");
            if (ink != null && ink.isSupported)
            {
                _inkMat = new Material(ink) { hideFlags = HideFlags.HideAndDontSave };
                _inkMat.SetColor("_Outside", InkOutside);
                _inkMat.SetColor("_Floor", InkFloor);
                _inkMat.SetColor("_Edge", InkEdge);
            }
            else Debug.LogWarning("HudMinimap: нет шейдера UI/HUD/MinimapInk — карта Разлома рисуется запасной раскраской поля.");
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
                // Прижимается к краю клуба по той же округлой форме, что и маска карты.
                Vector2 direction = point - panel.center;
                Vector2 extent = new Vector2(panel.width * .5f - Inset - 2f, panel.height * .5f - Inset - 2f) * ShapeRim;
                point = panel.center + direction / Mathf.Max(.001f, ShapeRadius(direction, extent));
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

        bool InsideMap(Rect panel, Vector2 point) => ShapeRadius(point - panel.center,
            new Vector2(panel.width * .5f - Inset, panel.height * .5f - Inset) * ShapeRim) <= 1f;

        /// <summary>Радиус точки в суперэллипсе клуба с полуосями <paramref name="extent"/>: 1 — на краю.</summary>
        static float ShapeRadius(Vector2 offset, Vector2 extent)
        {
            float x = Mathf.Abs(offset.x) / Mathf.Max(1f, extent.x), y = Mathf.Abs(offset.y) / Mathf.Max(1f, extent.y);
            return Mathf.Pow(Mathf.Pow(x, ShapePower) + Mathf.Pow(y, ShapePower), 1f / ShapePower);
        }

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
        /// Чернильная схема Разлома — поле расстояний до кромки прохода, печётся раз на уровень.
        /// Пол берётся из той же природной формы, по которой ходит Sim (полуметровые клетки), а без
        /// неё — из прямоугольников модулей. Точное расстояние до пола и до пустоты (Felzenszwalb),
        /// затем гауссово сглаживание: полуметровые ступени скругляются, прямые края стоят на месте.
        /// Заливку и кремовую линию кистью по полю рисует шейдер MinimapInk с краем в пиксель при
        /// любом приближении. Раньше здесь была маска с серебряной кромкой в один texel, растянутая
        /// на экране в семь раз, — отсюда лесенка (владелец 26 сентября: «очень пиксельное»).
        /// Квадрат мира (_world) выравнивается по сетке выборки, так что поле покрывает его ровно.
        /// </summary>
        void BuildInk(LayoutMap map)
        {
            float texel = FieldStep;
            while (_world.width / texel > 512f) texel *= 2f;
            // Сетка выборки выровнена по клеткам природной формы: в клетке ровно 2×2 выборки.
            float x0 = Mathf.Floor(_world.x / texel) * texel, z0 = Mathf.Floor(_world.y / texel) * texel;
            int n = Mathf.Max(16, Mathf.Max(Mathf.CeilToInt((_world.xMax - x0) / texel), Mathf.CeilToInt((_world.yMax - z0) / texel)));
            _world = new Rect(x0, z0, n * texel, n * texel);
            var inside = new bool[n * n];
            if (map.Outline != null)
            {
                float step = NaturalOutline.Step.ToFloat();
                var columns = new int[n];
                for (int x = 0; x < n; x++) columns[x] = Mathf.FloorToInt((x0 + (x + .5f) * texel) / step);
                int lastRow = int.MinValue;
                for (int z = 0; z < n; z++)
                {
                    int row = Mathf.FloorToInt((z0 + (z + .5f) * texel) / step);
                    // Соседняя строка выборки в той же строке клеток: в словарь формы второй раз не ходим.
                    if (row == lastRow) { System.Array.Copy(inside, (z - 1) * n, inside, z * n, n); continue; }
                    lastRow = row;
                    for (int x = 0; x < n; x++) inside[z * n + x] = map.Outline.ContainsCell(columns[x], row);
                }
            }
            else
            {
                float cell = LayoutMap.CellSize.ToFloat();
                for (int i = 0; i < map.PlacedCount; i++)
                {
                    PlacedModule room = map.GetPlaced(i);
                    // Выборки, чьи центры внутри модуля.
                    int ix0 = Mathf.Clamp(Mathf.CeilToInt((room.OriginX * cell - x0) / texel - .5f), 0, n);
                    int ix1 = Mathf.Clamp(Mathf.CeilToInt(((room.OriginX + room.Width) * cell - x0) / texel - .5f), 0, n);
                    int iz0 = Mathf.Clamp(Mathf.CeilToInt((room.OriginY * cell - z0) / texel - .5f), 0, n);
                    int iz1 = Mathf.Clamp(Mathf.CeilToInt(((room.OriginY + room.Height) * cell - z0) / texel - .5f), 0, n);
                    for (int z = iz0; z < iz1; z++) for (int x = ix0; x < ix1; x++) inside[z * n + x] = true;
                }
            }

            float[] toFloor = SquaredDistance(inside, n, true), toVoid = SquaredDistance(inside, n, false);
            var field = new float[n * n];
            for (int i = 0; i < field.Length; i++)
            {
                // Кромка — посередине между выборкой пола и выборкой пустоты: поправка на полвыборки.
                float d = inside[i] ? Mathf.Sqrt(toVoid[i]) - .5f : .5f - Mathf.Sqrt(toFloor[i]);
                field[i] = Mathf.Clamp(d * texel, -FieldRange, FieldRange);
            }
            Smooth(field, n, FieldBlur / texel);

            // RHalf хранит метры как есть; где его нет — R8 в пределах ±FieldRange.
            bool half = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf);
            _field = new Texture2D(n, n, half ? TextureFormat.RHalf : TextureFormat.R8, false, true)
                { name = "HUD rift distance", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (half)
            {
                var data = new ushort[field.Length];
                for (int i = 0; i < data.Length; i++) data[i] = Mathf.FloatToHalf(field[i]);
                _field.SetPixelData(data, 0);
                _fieldDecode = new Vector2(1f, 0f);
            }
            else
            {
                var data = new byte[field.Length];
                for (int i = 0; i < data.Length; i++) data[i] = (byte)Mathf.RoundToInt((field[i] / FieldRange * .5f + .5f) * 255f);
                _field.SetPixelData(data, 0);
                _fieldDecode = new Vector2(FieldRange * 2f, -FieldRange);
            }
            _field.Apply(false, true);

            LoadMaterials();
            if (_inkMat == null) Colourize(field, n, texel);
        }

        /// <summary>
        /// Запасная раскраска поля на процессоре, если шейдер MinimapInk не загрузился: та же
        /// заливка и кремовая кромка, только без кисти и с краем в выборку, а не в пиксель.
        /// </summary>
        void Colourize(float[] field, int n, float texel)
        {
            var pixels = new Color[field.Length];
            var edge = new Color(InkEdge.r, InkEdge.g, InkEdge.b, 1f);
            for (int i = 0; i < field.Length; i++)
            {
                float d = field[i];
                Color colour = Color.Lerp(InkOutside, InkFloor, Smooth01(-texel * .5f, texel * .5f, d));
                float line = 1f - Smooth01(texel * .5f, texel * 1.5f, Mathf.Abs(d - texel * .5f));
                pixels[i] = Color.Lerp(colour, edge, line * InkEdge.a);
            }
            SetTerrain(pixels, n);
            _terrain.filterMode = FilterMode.Bilinear;
        }

        static float Smooth01(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        const float Far = 1e20f;

        /// <summary>
        /// Квадрат точного евклидова расстояния (в выборках) от каждой выборки до ближайшей, у
        /// которой inside == <paramref name="target"/>: столбцы, потом строки (Felzenszwalb, Huttenlocher).
        /// Где таких нет совсем — <see cref="Far"/>.
        /// </summary>
        static float[] SquaredDistance(bool[] inside, int n, bool target)
        {
            var grid = new float[n * n];
            for (int i = 0; i < grid.Length; i++) grid[i] = inside[i] == target ? 0f : Far;
            var f = new float[n];
            var d = new float[n];
            var v = new int[n];
            var z = new float[n + 1];
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) f[y] = grid[y * n + x];
                LowerEnvelope(f, d, v, z, n);
                for (int y = 0; y < n; y++) grid[y * n + x] = d[y];
            }
            for (int y = 0; y < n; y++)
            {
                System.Array.Copy(grid, y * n, f, 0, n);
                LowerEnvelope(f, d, v, z, n);
                System.Array.Copy(d, 0, grid, y * n, n);
            }
            return grid;
        }

        /// <summary>
        /// Одномерный проход: d[q] = min по p (f[p] + (q − p)²) через нижнюю огибающую парабол.
        /// Выборки без цели (f = Far) в огибающую не входят — так нет переполнений и NaN.
        /// </summary>
        static void LowerEnvelope(float[] f, float[] d, int[] v, float[] z, int n)
        {
            int k = -1;
            for (int q = 0; q < n; q++)
            {
                if (f[q] >= Far) continue;
                if (k < 0) { k = 0; v[0] = q; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity; continue; }
                float s = Crossing(f, v[k], q);
                // z[0] = −∞: ниже первой параболы огибающая не опускается.
                while (s <= z[k]) { k--; s = Crossing(f, v[k], q); }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            if (k < 0) { for (int q = 0; q < n; q++) d[q] = Far; return; }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        static float Crossing(float[] f, int p, int q) => (f[q] + q * q - (f[p] + p * p)) / (2f * (q - p));

        /// <summary>Разделимое гауссово сглаживание поля (σ в выборках); за краем — крайние значения.</summary>
        static void Smooth(float[] field, int n, float sigma)
        {
            if (sigma < .3f) return;
            int radius = Mathf.CeilToInt(sigma * 2.5f);
            var kernel = new float[radius * 2 + 1];
            float sum = 0f;
            for (int k = -radius; k <= radius; k++) sum += kernel[k + radius] = Mathf.Exp(-.5f * k * k / (sigma * sigma));
            for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;
            var pass = new float[field.Length];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float value = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int at = x + k;
                        if (at < 0) at = 0; else if (at >= n) at = n - 1;
                        value += kernel[k + radius] * field[z * n + at];
                    }
                    pass[z * n + x] = value;
                }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float value = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int at = z + k;
                        if (at < 0) at = 0; else if (at >= n) at = n - 1;
                        value += kernel[k + radius] * pass[at * n + x];
                    }
                    field[z * n + x] = value;
                }
        }

        void SetTerrain(Color[] pixels, int size)
        {
            _terrain = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD navigation map", wrapMode = TextureWrapMode.Clamp };
            _terrain.SetPixels(pixels); _terrain.Apply(false, false);
        }
        void ClearTerrain()
        {
            _detailSource = null;
            if (_terrain == null && _field == null) return;
            if (_field != null) { Object.Destroy(_field); _field = null; }
            if (_terrain != null)
            {
                _chrome?.Forget(_terrain);
                Object.Destroy(_terrain);
                _terrain = null;
            }
            _backdrop.Invalidate();
        }
        public void Dispose()
        {
            ClearTerrain();
            _backdrop.Dispose();
            if (_player != null) Object.Destroy(_player);
            if (_terrainMat != null) Object.Destroy(_terrainMat);
            if (_inkMat != null) Object.Destroy(_inkMat);
            if (_detail != null) { _detail.Release(); Object.Destroy(_detail); }
        }
    }
}
