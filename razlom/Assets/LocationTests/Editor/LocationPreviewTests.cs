using System;
using System.IO;
using System.Linq;
using Game.Data;
using Game.Sim;
using Game.View;
using Game.LocationEditor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.LocationTests
{
    public sealed class LocationPreviewTests
    {
        private LocationTheme _theme;
        private LocationPreview _preview;

        [SetUp]
        public void SetUp()
        {
            _theme = Object.Instantiate(MeadowLocationAssets.EnsureCreated());
            // Use lightweight placeholders for lifecycle tests, retaining real art for the render test.
            _theme.Style.DecorVariants = new[] { new DecorVariant { Kind = DecorKind.Bush,
                Weight = 1, ScaleRange = new Vector2(0.6f, 1.1f), UseAsBoundary = true } };
            _preview = new LocationPreview();
        }

        [TearDown]
        public void TearDown()
        {
            _preview.Dispose();
            Object.DestroyImmediate(_theme);
        }

        [Test]
        public void AuthoredAssets_AgreeWithRuntimeMapsAndSpawns()
        {
            var authored = _theme.Gameplay.ToDefinition();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var run = new RiftRun(new Simulation(seed, 512), authored.Modules,
                    PrototypeContent.Items(), PrototypeContent.ItemBaseIds(), location: authored);
                run.StartRun();
                for (int level = 1; level <= authored.LevelCount; level++)
                {
                    var seeds = RiftLevelSeeds.ForLevel(seed, level);
                    var newMap = new LayoutMap(authored.Modules, authored.MaxModules);
                    authored.GetLevel(level).Generate(new LayoutGenerator(), authored.Modules, newMap, seeds.Layout);
                    Assert.That(newMap.Hash(), Is.EqualTo(run.Map.Hash()), $"seed {seed}, level {level}");
                    var actual = new Simulation(seed, 512);
                    authored.GetLevel(level).Spawn(actual, newMap, seeds.Spawns);
                    Assert.That(actual.Entities.Count, Is.EqualTo(run.Sim.Entities.Count));
                    for (int i = 0; i < actual.Entities.Count; i++)
                    {
                        Assert.That(actual.Entities.Position[i], Is.EqualTo(run.Sim.Entities.Position[i]));
                        if (i != Simulation.PlayerId)
                            Assert.That(actual.Entities.Health[i], Is.EqualTo(run.Sim.Entities.Health[i]));
                    }
                    for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                    run.Step(InputFrame.Empty);
                    run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                    run.Step(InputFrame.Empty);
                    run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                }
            }
        }

        [Test]
        public void Rebuild_ReusesPoolsAndDoesNotCreateAGameDriverOrColliders()
        {
            _preview.Generate(_theme, 42, 1);
            int created = _preview.View.PooledCount;
            string[] initial = VisualSnapshot(_preview.Root);
            Assert.That(_preview.View.DecorCount, Is.GreaterThan(0));
            _preview.Generate(_theme, 42, 1);
            Assert.That(_preview.View.PooledCount, Is.EqualTo(created));
            CollectionAssert.AreEqual(initial, VisualSnapshot(_preview.Root));
            Assert.That(_preview.Root.GetComponentsInChildren<TickDriver>(true), Is.Empty);
            Assert.That(_preview.Root.GetComponentsInChildren<Collider>(true), Is.Empty);
            _preview.Clear();
            Assert.That(_preview.View.TileCount, Is.Zero);
            Assert.That(_preview.View.DecorCount, Is.Zero);
            Assert.That(_preview.Root.GetComponentsInChildren<Renderer>(), Is.Empty);
            _preview.Generate(_theme, 42, 1);
            CollectionAssert.AreEqual(initial, VisualSnapshot(_preview.Root));
        }

        [Test]
        public void AppearanceChanges_DoNotChangeTheSimulationOrAuthoringAssets()
        {
            // Loading a Resources fallback must never write a prefab into the source asset.
            _theme.Style.DecorVariants[0].ResourcePath = "Decor/UNS_Bush";
            string authoredJson = JsonUtility.ToJson(_theme);
            _preview.Generate(_theme, 99, 5);
            Assert.That(JsonUtility.ToJson(_theme), Is.EqualTo(authoredJson));
            ulong state = _preview.Sim.StateHash();
            ulong map = _preview.Map.Hash();
            var positions = _preview.Sim.Entities.Position.Take(_preview.Sim.Entities.Count).ToArray();
            _preview.View.Show(_preview.Map, _preview.Seeds.Layout + 1);
            Assert.That(_preview.Sim.StateHash(), Is.EqualTo(state), "View consumed or changed simulation state");
            _theme.Style.DecorPerCell = 0;
            _theme.Style.BoundaryDecorChance = 0;
            _theme.Style.RoomColor = Color.magenta;
            _preview.Generate(_theme, 99, 5);
            Assert.That(_preview.View.DecorCount, Is.Zero);
            Assert.That(_preview.Map.Hash(), Is.EqualTo(map));
            Assert.That(_preview.Sim.StateHash(), Is.EqualTo(state));
            CollectionAssert.AreEqual(positions, _preview.Sim.Entities.Position.Take(_preview.Sim.Entities.Count).ToArray());
        }

        [Test]
        public void DecorSeed_IsIndependentAndConnectorClearanceSurvivesRotation()
        {
            _theme.Style.BoundaryDecorChance = 0;
            _theme.Style.DecorPerCell = 0.25f;
            _preview.Generate(_theme, 7, 1);
            string[] initial = VisualSnapshot(_preview.Root);
            _preview.View.Show(_preview.Map, _preview.Seeds.Layout + 1);
            CollectionAssert.AreNotEqual(initial, VisualSnapshot(_preview.Root));
            _preview.View.Show(_preview.Map, _preview.Seeds.Layout);
            CollectionAssert.AreEqual(initial, VisualSnapshot(_preview.Root));
            var map = _preview.Map;
            float cell = LayoutMap.CellSize.ToFloat();
            foreach (var renderer in _preview.Root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.name.StartsWith("Декор:")) continue;
                var point = renderer.transform.position;
                bool inside = false;
                for (int i = 0; i < map.PlacedCount; i++)
                {
                    var p = map.GetPlaced(i);
                    if (point.x < p.OriginX * cell || point.x >= (p.OriginX + p.Width) * cell ||
                        point.z < p.OriginY * cell || point.z >= (p.OriginY + p.Height) * cell) continue;
                    inside = true;
                    Assert.That(point.x, Is.InRange(p.OriginX * cell + _theme.Style.DecorEdgeMargin,
                        (p.OriginX + p.Width) * cell - _theme.Style.DecorEdgeMargin));
                    Assert.That(point.z, Is.InRange(p.OriginY * cell + _theme.Style.DecorEdgeMargin,
                        (p.OriginY + p.Height) * cell - _theme.Style.DecorEdgeMargin));
                    var definition = map.Modules.Get(p.ModuleIndex);
                    for (int c = 0; c < definition.ConnectorCount; c++)
                    {
                        var connector = definition.RotatedConnector(c, p.Quarters);
                        var center = new Vector3((p.OriginX + connector.X + 0.5f) * cell, 0,
                            (p.OriginY + connector.Y + 0.5f) * cell);
                        Assert.That(Vector3.Distance(point, center), Is.GreaterThanOrEqualTo(_theme.Style.DecorConnectorMargin));
                    }
                }
                Assert.That(inside, Is.True);
            }
        }

        [Test]
        public void Preview_IsIsolatedAndDisposesItsObjectsAndMaterials()
        {
            Scene active = SceneManager.GetActiveScene();
            int roots = active.rootCount;
            bool dirty = active.isDirty;
            int previewScenes = EditorSceneManager.previewSceneCount;
            _preview.Generate(_theme, 11, 3);
            var owned = _preview.Root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => !AssetDatabase.Contains(m)).Distinct().ToArray();
            Assert.That(EditorSceneManager.IsPreviewScene(_preview.Root.scene), Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            Assert.That(active.rootCount, Is.EqualTo(roots));
            Assert.That(active.isDirty, Is.EqualTo(dirty));
            _preview.Dispose();
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previewScenes));
            Assert.That(owned.All(m => m == null), Is.True, "Generated materials leaked");
        }

        [Test]
        public void VisibleRoad_StaysOnFloor_AndDecorBoundsLeaveClearance()
        {
            _theme.Style.DecorPerCell = 0.5f;
            _theme.Style.BoundaryDecorChance = 1;
            _preview.Generate(_theme, 42, 1);
            var map = _preview.Map;
            int trail = 0;
            foreach (var renderer in _preview.Root.GetComponentsInChildren<Renderer>())
            {
                var bounds = renderer.bounds;
                if (renderer.name == "Тропа" || renderer.name == "Вход" || renderer.name == "Выход" || renderer.name == "Тайник")
                {
                    trail++;
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                        foreach (float z in new[] { bounds.min.z, bounds.max.z })
                            Assert.That(map.ContainsWorld(new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z))), Is.True,
                                $"{renderer.name} extends beyond the walkable floor");
                }
                else if (renderer.name.StartsWith("Декор:"))
                {
                    // Placeholders have centred bounds. This circle encloses every yaw of the model.
                    var point = new FixVec2(Fix64.FromDouble(bounds.center.x), Fix64.FromDouble(bounds.center.z));
                    float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    Assert.That(map.Routes.NearRoad(point,
                        Fix64.FromDouble(_theme.Style.RouteWidth * 0.5f + radius)), Is.False);
                }
            }
            Assert.That(trail, Is.GreaterThan(10));
        }

        [Test]
        public void EncounterProfile_IsConnected_GrowsAndCompilesAnIndependentSnapshot()
        {
            var authored = _theme.Gameplay.Encounters;
            Assert.That(authored, Is.Not.Null);
            var copy = Object.Instantiate(authored);
            try
            {
                var first = copy.ToDefinition(1);
                var last = copy.ToDefinition(10);
                Assert.That(last.MainCount, Is.GreaterThan(first.MainCount));
                Assert.That(last.CountBonus, Is.GreaterThan(first.CountBonus));
                Assert.That(last.DamagePercent, Is.GreaterThan(first.DamagePercent));
                var rng = new Pcg32(1, 1);
                int before = first.Pick(EncounterRole.Introduction, ref rng).GetGroup(0).Max;
                copy.Introduction[0].Groups[0].Max = 7;
                Assert.That(first.Pick(EncounterRole.Introduction, ref rng).GetGroup(0).Max, Is.EqualTo(before));
                copy.ExitGuard[0].Groups[0].Elite = false;
                Assert.Throws<ArgumentException>(() => copy.ToDefinition(1));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void EncounterClearings_AreFreeOfDecor_AndSurviveRebuild()
        {
            _theme.Style.DecorPerCell = 0.8f;
            _preview.Generate(_theme, 42, 1);
            Assert.That(_preview.Encounters, Is.Not.Null);
            foreach (var renderer in _preview.Root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.name.StartsWith("Декор:")) continue;
                var point = renderer.bounds.center;
                float radius = Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.z);
                for (int e = 0; e < _preview.Encounters.Count; e++)
                {
                    var center = _preview.Encounters.Get(e).Center;
                    float distance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(center.X.ToFloat(), center.Y.ToFloat()));
                    Assert.That(distance, Is.GreaterThanOrEqualTo(_preview.Encounters.FormationRadius.ToFloat() + radius));
                }
            }
            string[] before = VisualSnapshot(_preview.Root);
            _preview.View.Show(_preview.Map, _preview.Seeds.Layout);
            CollectionAssert.AreEqual(before, VisualSnapshot(_preview.Root));
            _preview.Clear();
            Assert.That(_preview.Encounters, Is.Null);
            Assert.That(_preview.Root.GetComponentsInChildren<Renderer>(), Is.Empty);
        }

        [Test]
        public void MeadowLandmarks_UseRealModels_AndReleaseGeneratedMeshes()
        {
            Assert.That(_theme.Style.PortalPrefab, Is.Not.Null);
            Assert.That(_theme.Style.CachePrefab, Is.Not.Null);
            _preview.Generate(_theme, 42, 1);
            var meshes = _preview.Root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                .Where(m => m != null && (m.name == "Контур занятого пола" || m.name == "Свечение ориентира")).Distinct().ToArray();
            Assert.That(meshes.Length, Is.EqualTo(2));
            Assert.That(meshes.All(m => m.vertexCount > 0), Is.True);
            var floor = Shader.Find("Razlom/Meadow Ground");
            Assert.That(floor, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(floor), Is.False);
            Assert.That(_preview.Root.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial.shader == floor), Is.True);
            _preview.Dispose();
            Assert.That(meshes.All(m => m == null), Is.True);
        }

        [Test]
        public void MeadowLighting_RestoresTheSceneAfterLeaving()
        {
            bool fog = RenderSettings.fog;
            Color ambient = RenderSettings.ambientSkyColor;
            var mode = RenderSettings.ambientMode;
            var state = new MeadowLighting();
            try
            {
                state.Apply(_theme.Style);
                Assert.That(RenderSettings.fog, Is.True);
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(_theme.Style.SkyColor));
                state.Restore();
                Assert.That(RenderSettings.fog, Is.EqualTo(fog));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(ambient));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(mode));
            }
            finally { state.Restore(); }
        }

        [Test]
        public void InvalidProfile_IsRejectedBeforeCreatingAPreviewScene()
        {
            int scenes = EditorSceneManager.previewSceneCount;
            _theme.Style.DecorEdgeMargin = -1;
            Assert.Throws<ArgumentException>(() => _preview.Generate(_theme, 1, 1));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes));
        }

        [Test]
        public void MeadowArt_RendersAndRetainsItsSharedAssetMaterials()
        {
            var authored = AssetDatabase.LoadAssetAtPath<LocationTheme>(MeadowLocationAssets.ThemePath);
            Assert.That(authored.Style.RoomFloorTexture, Is.Not.Null);
            Assert.That(authored.Style.PathFloorTexture, Is.Not.Null);
            Assert.That(authored.Style.DecorVariants.All(v => v.Prefab != null), Is.True);
            _preview.Generate(authored, 42, 1);
            var texture = _preview.Render(new Rect(0, 0, 1100, 800), _preview.Bounds.center,
                new Vector2(-25, 65), _preview.Bounds.extents.magnitude, true);
            Assert.That(texture, Is.Not.Null);
            var target = RenderTexture.GetTemporary(1100, 800, 0);
            var previous = RenderTexture.active;
            var readable = new Texture2D(1100, 800, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, 1100, 800), 0, 0);
                readable.Apply();
                Assert.That(readable.GetPixels32().Distinct().Take(100).Count(), Is.EqualTo(100), "Preview image is blank");
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/LocationPreview.png", readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readable);
            }
            var assetMaterials = authored.Style.DecorVariants.SelectMany(v => v.Prefab.GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            _preview.Dispose();
            Assert.That(assetMaterials.All(m => m != null && AssetDatabase.Contains(m)), Is.True);
        }

        private static string[] VisualSnapshot(GameObject root)
            => root.GetComponentsInChildren<Renderer>().Select(r => r.name + ":" +
                r.transform.position.ToString("F5") + ":" + r.transform.rotation.ToString("F5") + ":" +
                r.transform.localScale.ToString("F5")).OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }
}
