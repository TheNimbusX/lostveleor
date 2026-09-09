using System;
using Game.Data;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.LocationEditor
{
    public static class MeadowLocationAssets
    {
        public const string ThemePath = "Assets/Resources/Locations/Meadow.asset";
        public const string GameplayPath = "Assets/Resources/Locations/MeadowGameplay.asset";
        public const string EncounterPath = "Assets/Resources/Locations/MeadowEncounters.asset";
        [MenuItem("Разлом/Локации/Подключить оформление Meadow", priority = 22)]
        public static void ConfigureMeadowArt()
        {
            var theme = EnsureCreated();
            Undo.RecordObject(theme, "Оформление лугов");
            if (theme.Style.PortalPrefab == null)
                theme.Style.PortalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/BuildingUtilityDeco/Portal01.prefab");
            if (theme.Style.CachePrefab == null)
                theme.Style.CachePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Environment/Camp/wooden+crate+3d+model.fbx");
            theme.Style.Validate();
            EditorUtility.SetDirty(theme); AssetDatabase.SaveAssetIfDirty(theme);
        }

        // Explicit migration for the existing Meadow profile, also callable from batch mode.
        [MenuItem("Разлом/Локации/Подключить боевые встречи Meadow", priority = 21)]
        public static void ConfigureEncounters()
        {
            var theme = EnsureCreated();
            var encounters = EnsureEncounters();
            if (theme.Gameplay.Encounters != null) return;
            Undo.RecordObject(theme.Gameplay, "Подключить боевые встречи");
            theme.Gameplay.Encounters = encounters;
            theme.Gameplay.ToDefinition();
            EditorUtility.SetDirty(theme.Gameplay);
            AssetDatabase.SaveAssetIfDirty(theme.Gameplay);
        }

        private static EncounterProfileAsset EnsureEncounters()
        {
            var encounters = AssetDatabase.LoadAssetAtPath<EncounterProfileAsset>(EncounterPath);
            if (encounters != null) return encounters;
            encounters = ScriptableObject.CreateInstance<EncounterProfileAsset>();
            AssetDatabase.CreateAsset(encounters, EncounterPath);
            return encounters;
        }

        [MenuItem("Разлом/Локации/Создать профили первой локации", priority = 20)]
        public static void CreateAndSelect()
        {
            Selection.activeObject = EnsureCreated();
        }

        // Only creates missing assets. Existing authoring is never overwritten.
        public static LocationTheme EnsureCreated()
        {
            EnsureFolder("Assets/Resources/Locations/Modules");
            var gameplay = AssetDatabase.LoadAssetAtPath<LocationProfileAsset>(GameplayPath);
            if (gameplay == null)
            {
                var prototypes = PrototypeContent.Modules();
                string[] keys = { "module.entrance", "module.hall", "module.corridor",
                    "module.junction", "module.chamber", "module.corner" };
                var modules = new ModuleAsset[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    string path = "Assets/Resources/Locations/Modules/" + keys[i].Substring(7) + ".asset";
                    modules[i] = AssetDatabase.LoadAssetAtPath<ModuleAsset>(path);
                    if (modules[i] != null) continue;
                    ModuleDefinition definition = null;
                    for (int m = 0; m < prototypes.Count; m++)
                        if (prototypes.Get(m).Id == StableId.Of(keys[i])) definition = prototypes.Get(m);
                    if (definition == null) throw new InvalidOperationException("Missing prototype " + keys[i]);
                    var asset = ScriptableObject.CreateInstance<ModuleAsset>();
                    asset.StableKey = keys[i];
                    asset.Width = definition.Width;
                    asset.Height = definition.Height;
                    asset.Weight = definition.Weight;
                    asset.IsEntrance = definition.IsEntrance;
                    asset.Connectors = new ConnectorAsset[definition.ConnectorCount];
                    for (int c = 0; c < asset.Connectors.Length; c++)
                    {
                        var connector = definition.GetConnector(c);
                        asset.Connectors[c] = new ConnectorAsset {
                            Cell = new Vector2Int(connector.X, connector.Y), Facing = connector.Facing };
                    }
                    AssetDatabase.CreateAsset(asset, path);
                    modules[i] = asset;
                }
                gameplay = ScriptableObject.CreateInstance<LocationProfileAsset>();
                gameplay.Encounters = EnsureEncounters();
                gameplay.CompleteAtEnd = true;
                gameplay.PlayerHealth = 150;
                gameplay.Modules = modules;
                gameplay.Levels = new LevelSettingsAsset[10];
                for (int i = 0; i < gameplay.Levels.Length; i++)
                {
                    var s = RiftLevelSettings.Prototype(i + 1);
                    gameplay.Levels[i] = new LevelSettingsAsset { Rooms = s.TargetModules, Exits = s.ExitCount,
                        Loops = s.MaxLoops, RewardBranches = s.RewardBranches,
                        MinEnemies = s.MinEnemies, MaxEnemies = s.MaxEnemies, EnemyHealth = 60 + i * 5,
                        Boss = i == gameplay.Levels.Length - 1 };
                }
                gameplay.ToDefinition();
                AssetDatabase.CreateAsset(gameplay, GameplayPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<LocationTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<LocationTheme>();
                theme.Gameplay = gameplay;
                theme.Style.RoomFloorTexture = Resources.Load<Texture2D>("Terrain/UNS_Terrain_Grass");
                theme.Style.PathFloorTexture = Resources.Load<Texture2D>("Terrain/UNS_Terrain_Dirt");
                for (int i = 0; i < theme.Style.DecorVariants.Length; i++)
                    theme.Style.DecorVariants[i].Prefab = Resources.Load<GameObject>(theme.Style.DecorVariants[i].ResourcePath);
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            return theme;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
