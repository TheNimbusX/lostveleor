using System;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.LocationEditor
{
    /// <summary>Owns an isolated preview scene. Never ticks or modifies a running game.</summary>
    public sealed class LocationPreview : IDisposable
    {
        private PreviewRenderUtility _renderer;
        private LocationTheme _theme;
        private string _styleJson;
        public LayoutMap Map { get; private set; }
        public Simulation Sim { get; private set; }
        public RiftLevelSeeds Seeds { get; private set; }
        public RiftLevelSettings Settings { get; private set; }
        public Bounds Bounds { get; private set; }
        public GameObject Root { get; private set; }
        public LayoutView View { get; private set; }
        public Camera Camera => _renderer?.camera;

        public void Generate(LocationTheme theme, ulong runSeed, int level)
        {
            if (theme == null || theme.Gameplay == null)
                throw new ArgumentException("Назначьте оформление и игровой профиль локации.");
            if (theme.Style == null) throw new ArgumentException("В профиле отсутствуют настройки оформления.");
            theme.Style.Validate();
            var location = theme.Gameplay.ToDefinition();
            if (level < 1 || level > location.LevelCount)
                throw new ArgumentException("Выберите уровень из игрового профиля.");
            var settings = location.GetLevel(level);
            var seeds = RiftLevelSeeds.ForLevel(runSeed, level);
            var map = new LayoutMap(location.Modules, location.MaxModules);
            settings.Generate(new LayoutGenerator(), location.Modules, map, seeds.Layout);
            var sim = new Simulation(runSeed, 512);
            settings.Spawn(sim, map, seeds.Spawns);

            if (_renderer == null) InitializeRenderer();
            string json = JsonUtility.ToJson(theme.Style);
            if (_theme != theme || _styleJson != json)
            {
                View.Configure(theme);
                _theme = theme;
                _styleJson = json;
            }
            View.Show(map, seeds.Layout);
            Map = map;
            Sim = sim;
            Seeds = seeds;
            Settings = settings;
            Bounds = CalculateBounds(map);
        }

        private void InitializeRenderer()
        {
            _renderer = new PreviewRenderUtility();
            _renderer.camera.orthographic = true;
            _renderer.camera.nearClipPlane = 0.1f;
            _renderer.camera.farClipPlane = 2000f;
            _renderer.camera.clearFlags = CameraClearFlags.SolidColor;
            _renderer.camera.backgroundColor = new Color(0.12f, 0.15f, 0.14f);
            _renderer.ambientColor = new Color(0.55f, 0.55f, 0.55f);
            _renderer.lights[0].intensity = 1.2f;
            _renderer.lights[0].transform.rotation = Quaternion.Euler(50, -30, 0);
            _renderer.lights[1].intensity = 0.5f;
            _renderer.lights[1].transform.rotation = Quaternion.Euler(35, 150, 0);
            Root = new GameObject("Предпросмотр локации") { hideFlags = HideFlags.HideAndDontSave };
            _renderer.AddSingleGO(Root);
            View = Root.AddComponent<LayoutView>();
        }

        public Texture Render(Rect rect, Vector3 focus, Vector2 orbit, float size, bool showEnvironment)
        {
            if (_renderer == null) return null;
            Camera.aspect = Mathf.Max(0.01f, rect.width / Mathf.Max(1, rect.height));
            Camera.orthographicSize = size;
            Camera.transform.rotation = Quaternion.Euler(orbit.y, orbit.x, 0);
            Camera.transform.position = focus - Camera.transform.forward * 800f;
            Root.SetActive(showEnvironment);
            _renderer.BeginPreview(rect, GUIStyle.none);
            _renderer.Render(true);
            return _renderer.EndPreview();
        }

        public void Clear()
        {
            View?.Show(null, 0);
            Map = null;
            Sim = null;
        }

        public void Dispose()
        {
            if (View != null) View.Release();
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
            View = null;
            _renderer?.Cleanup();
            _renderer = null;
            _theme = null;
            _styleJson = null;
            Map = null;
            Sim = null;
        }

        public static Bounds CalculateBounds(LayoutMap map)
        {
            var first = map.GetPlaced(0);
            float cell = LayoutMap.CellSize.ToFloat();
            var bounds = new Bounds(new Vector3(first.OriginX * cell, 0, first.OriginY * cell), Vector3.zero);
            for (int i = 0; i < map.PlacedCount; i++)
            {
                var p = map.GetPlaced(i);
                bounds.Encapsulate(new Vector3(p.OriginX * cell, 0, p.OriginY * cell));
                bounds.Encapsulate(new Vector3((p.OriginX + p.Width) * cell, 0, (p.OriginY + p.Height) * cell));
            }
            return bounds;
        }
    }
}
