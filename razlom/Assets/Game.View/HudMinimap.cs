using Game.Sim;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
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
        Rect _world, _view;
        Transform _fire;
        readonly List<(Transform anchor, string name, int symbol)> _landmarks = new List<(Transform, string, int)>();
        GUIStyle _hintLabel, _captionLabel;
        string _hoverName, _caption;
        float _hoverDistance;
        Vector2 _hero;
        public Vector2 Pointer;
        const float Zoom = 1.819f;
        static readonly Color Rim = new Color(1f, .95f, .81f, 1f);
        static readonly Color Ground = new Color(.36f, .37f, .20f, .82f);
        static readonly Color Path = new Color(.64f, .57f, .38f, .88f);

        public void DrawCamp(Rect panel, CampPlayerView camp, HudChrome chrome, GUIStyle label)
        {
            if (camp.WalkMap == null) return;
            if (_campMap != camp.WalkMap || _riftMap != null)
            {
                ClearTerrain();
                _campMap = camp.WalkMap;
                _riftMap = null;
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
            foreach (var landmark in _landmarks)
                if (landmark.anchor != null)
                    Landmark(panel, landmark.anchor.position.x, landmark.anchor.position.z,
                        landmark.name, landmark.symbol, Rim, chrome);
            if (_fire != null)
                Landmark(panel, _fire.position.x, _fire.position.z, "Костёр", 1,
                    new Color(1f, .66f, .27f), chrome);
            Vector3 hero = camp.Position;
            DrawPlayer(Project(panel, hero.x, hero.z), camp.Body != null ? camp.Body.eulerAngles.y : 0f);
            DrawHover(panel, chrome);
        }

        void CacheLandmarks(CampPlayerView camp)
        {
            _landmarks.Clear();
            if (camp.Tent != null) _landmarks.Add((camp.Tent, "Палатка Пелага", 0));
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            if (world == null || world.CampRoot == null) return;
            foreach (var anchor in world.CampRoot.GetComponentsInChildren<Transform>(true))
            {
                if (anchor.name == "Anchor - Smith") _landmarks.Add((anchor, "Кузнец", 2));
                else if (anchor.name == "Anchor - Trader") _landmarks.Add((anchor, "Торговец", 3));
                else if (anchor.name == "Anchor - Rift Portal") _landmarks.Add((anchor, "Вход в Разлом", 4));
            }
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
                const int resolution = 192;
                var pixels = new Color[resolution * resolution];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Ground;
                for (int i = 0; i < map.PlacedCount; i++)
                {
                    PlacedModule room = map.GetPlaced(i);
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((room.OriginX * cell - _world.x) / size * resolution), 0, resolution);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt((room.OriginY * cell - _world.y) / size * resolution), 0, resolution);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(((room.OriginX + room.Width) * cell - _world.x) / size * resolution), 0, resolution);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt(((room.OriginY + room.Height) * cell - _world.y) / size * resolution), 0, resolution);
                    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                        pixels[y * resolution + x] = i == 0 ? new Color(.72f, .49f, .32f, .9f) : Path;
                }
                SetTerrain(pixels, resolution);
                _backdrop.Request(_world, 0f);
            }
            _caption = "Разлом · " + run.Depth;
            var heroWorld = sim.Entities.Position[Simulation.PlayerId];
            _hero = new Vector2(heroWorld.X.ToFloat(), heroWorld.Y.ToFloat());
            DrawBase(panel, chrome, label);
            for (int exit = 0; exit < map.ExitCount; exit++)
            {
                var point = map.ExitPoint(exit);
                Landmark(panel, point.X.ToFloat(), point.Y.ToFloat(), "Выход", 4, Rim, chrome);
            }
            for (int branch = 0; branch < map.RewardBranchCount; branch++)
            {
                if (run.IsBranchClaimed(branch)) continue;
                var point = map.CenterOf(map.GetRewardBranch(branch));
                Landmark(panel, point.X.ToFloat(), point.Y.ToFloat(), "Награда", 5, new Color(1f, .75f, .36f), chrome);
            }
            for (int d = 0; d < run.DropCount; d++)
            {
                RunDrop drop = run.GetDrop(d);
                if (drop.Claimed) continue;
                Landmark(panel, drop.Position.X.ToFloat(), drop.Position.Y.ToFloat(),
                    drop.Offer.Kind == RewardKind.Ability ? "Способность" : "Предмет", 5, new Color(1f, .58f, .30f), chrome);
            }
            EntityStore entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                var world = entities.Position[i];
                Vector2 point = Project(panel, world.X.ToFloat(), world.Y.ToFloat());
                if (i == Simulation.PlayerId) continue;
                if (i == run.BossId)
                    Landmark(panel, world.X.ToFloat(), world.Y.ToFloat(), "Босс", 6, new Color(1f, .35f, .27f), chrome);
                else if (InsideMap(panel, point))
                {
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
            if (_hintLabel == null)
            {
                _hintLabel = new GUIStyle(GameTypography.Label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, padding = new RectOffset() };
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
            Texture source = _backdrop.Texture != null ? _backdrop.Texture : _terrain;
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
            if (_mapInk != null)
            {
                _mapInk.SetVector("_View", new Vector4(uv.x, uv.y, uv.width, uv.height));
                Graphics.Blit(source, _detail, _mapInk);
            }
            else Graphics.Blit(source, _detail, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = previous;
        }

        void Landmark(Rect panel, float x, float z, string name, int symbol, Color color, HudChrome chrome)
        {
            Vector2 point = Project(panel, x, z);
            bool nearby = InsideMap(panel, point);
            if (!nearby)
            {
                Vector2 direction = point - panel.center;
                float extent = panel.width * .5f - 17f;
                point = panel.center + direction * (extent / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y)));
                // У края остаются компактные ориентиры; врагов за краем не рисуем.
                chrome.Shape(new Rect(point.x - 3f, point.y - 3f, 6f, 6f), color, 3f);
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
            if (_hoverName == null) return;
            string text = _hoverName + " · " + Mathf.RoundToInt(_hoverDistance) + " м";
            float scale = Mathf.Max(.01f, GUI.matrix.m00);
            Rect safe = Screen.safeArea;
            float left = safe.xMin / scale + 6f, right = safe.xMax / scale - 6f;
            float top = (Screen.height - safe.yMax) / scale + 6f;
            float bottom = (Screen.height - safe.yMin) / scale - 6f;
            float width = Mathf.Min(right - left, _hintLabel.CalcSize(new GUIContent(text)).x + 22f);
            float x = Pointer.x + 16f;
            if (x + width > right) x = Pointer.x - width - 12f;
            Rect hint = new Rect(Mathf.Clamp(x, left, right - width), Mathf.Clamp(Pointer.y + 18f, top, bottom - 29f), width, 29f);
            chrome.Shape(new Rect(hint.x, hint.y + 2f, hint.width, hint.height), new Color(.17f, .16f, .11f, .14f), 7f);
            chrome.Shape(hint, new Color(.97f, .94f, .85f, .98f), 7f);
            chrome.Shape(hint, new Color(.67f, .61f, .47f, .65f), 7f, .7f);
            GUI.Label(hint, text, _hintLabel);
        }

        static bool InsideMap(Rect panel, Vector2 point) => point.x >= panel.x + 15f && point.x <= panel.xMax - 15f
            && point.y >= panel.y + 15f && point.y <= panel.yMax - 15f;

        Vector2 Project(Rect panel, float x, float z) => new Vector2(
            panel.x + (x - _view.x) / _view.width * panel.width,
            panel.yMax - (z - _view.y) / _view.height * panel.height);

        void DrawPlayer(Vector2 point, float angle)
        {
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
