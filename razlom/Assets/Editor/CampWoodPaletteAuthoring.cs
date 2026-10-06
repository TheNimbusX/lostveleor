using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    // Material-only pass, independent of bridge geometry and its rollback journal.
    public static class CampWoodPaletteAuthoring
    {
        public const string Folder = "Assets/Resources/Environment/Camp/WoodPalette";
        const string JournalPath = "Assets/Editor/CampWoodPaletteData/Journal.asset";
        const string CampPath = "Authored World/CampRoot";
        static readonly string[] Floats = { "_WoodStrength", "_WoodSaturation", "_WoodExposure", "_WoodWetStrength" };

        static Transform Camp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before applying the camp palette.");
            var camp = GameObject.Find(CampPath);
            if (camp == null || camp.scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open the authored camp scene.");
            return camp.transform;
        }

        static Renderer Resolve(Transform camp, string path)
        {
            if (!path.StartsWith(CampPath + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Journal path outside CampRoot: " + path);
            var found = camp.Find(path.Substring(CampPath.Length + 1));
            if (found == null || found.GetComponent<Renderer>() == null) throw new InvalidOperationException("Missing wood renderer: " + path);
            return found.GetComponent<Renderer>();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }

        static string Kind(Material material)
        {
            if (material == null || !material.HasProperty("_BaseMap")) return null;
            string path = AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"));
            foreach (string name in new[] { "fence", "bench", "crate", "barrel", "archway" })
                if (path.EndsWith("wooden+" + name + "+3d+model_basecolor.jpg", StringComparison.Ordinal)) return name == "archway" ? "arch" : name;
            if (path.EndsWith("wooden+market+stall+3d+model_basecolor.jpg", StringComparison.Ordinal)) return "stall";
            if (path.EndsWith("medieval+market+stall+3d+model_basecolor.jpg", StringComparison.Ordinal)) return "merchant";
            return null;
        }

        static string TargetShader(Material source, string kind)
        {
            if (source.shader.name == "Universal Render Pipeline/Lit") return CampBridgePolishWood.ShaderName;
            if (kind == "arch" && source.shader.name == "Razlom/Texture Toon") return "Game/Camp Palette Toon Wood";
            throw new InvalidOperationException("Unsupported source shader; preserve it until explicitly adapted: " + source.name + " / " + source.shader.name);
        }

        static bool IsCandidate(Material material) => material != null && Kind(material) != null
            && material.shader.name != CampBridgePolishWood.ShaderName
            && material.shader.name != "Game/Camp Palette Toon Wood";

        static Texture2D Mask(string kind)
        {
            string root = kind == "bench" || kind == "crate" || kind == "fence" ? CampBridgePolishWood.AssetRoot : Folder;
            string maskName = kind == "stall" ? "merchant_wooden" : kind;
            string path = root + "/Textures/" + maskName + "_wood_mask.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Wood-only mask is not ready: " + path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.sRGBTexture || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear)
            {
                // The pilot masks have already been configured and are deliberately immutable here.
                if (root != Folder) throw new InvalidOperationException("Pilot mask import settings changed: " + path);
                importer.sRGBTexture = false; importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear; importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport(); texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return texture;
        }

        static Material Prepare(Material source, string kind, Dictionary<Material, string> rollback, List<Material> created)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id)) throw new InvalidOperationException("Source material is not persistent.");
            var shader = Shader.Find(TargetShader(source, kind));
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Wood shader unavailable for " + kind);
            var mask = Mask(kind);
            string path = Folder + "/Materials/" + kind + "_" + guid + "_" + id + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var artistic = new Dictionary<string, float>();
            Color tone = new Color(.46f, .36f, .27f, 1).linear;
            if (material == null)
            {
                material = new Material(source); AssetDatabase.CreateAsset(material, path); created.Add(material);
            }
            else
            {
                rollback[material] = EditorJsonUtility.ToJson(material);
                Undo.RegisterCompleteObjectUndo(material, "Refresh camp wood material");
                foreach (string field in Floats) if (material.HasProperty(field)) artistic[field] = material.GetFloat(field);
                if (material.HasProperty("_WoodTone")) tone = material.GetColor("_WoodTone");
                EditorUtility.CopySerialized(source, material);
            }
            material.name = "Camp wood palette " + kind + " — " + source.name;
            material.shader = shader; material.SetTexture("_WoodMask", mask); material.SetColor("_WoodTone", tone);
            material.SetFloat("_WoodStrength", kind == "arch" ? .72f : .86f);
            material.SetFloat("_WoodSaturation", .78f);
            material.SetFloat("_WoodExposure", kind == "bench" ? .86f : kind == "crate" ? .90f : 1f);
            if (material.HasProperty("_WoodWetStrength")) material.SetFloat("_WoodWetStrength", 0);
            // Reapplying refreshes source textures and masks but retains the artist's material controls.
            foreach (var pair in artistic) if (material.HasProperty(pair.Key)) material.SetFloat(pair.Key, pair.Value);
            EditorUtility.SetDirty(material); return material;
        }

        static void Assign(Renderer renderer, Material[] materials)
        {
            Undo.RecordObject(renderer, "Camp wood palette assignment"); renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); EditorUtility.SetDirty(renderer);
        }

        static void CheckAssignments(Transform camp, CampWoodPaletteJournal journal)
        {
            if (journal.ScenePath != camp.gameObject.scene.path) throw new InvalidOperationException("Palette journal belongs to another scene.");
            foreach (var binding in journal.Bindings)
            {
                var current = Resolve(camp, binding.Path).sharedMaterials;
                if (!current.SequenceEqual(binding.Original) && !current.SequenceEqual(binding.Polished))
                    throw new InvalidOperationException("Material assignment changed outside this pass: " + binding.Path);
            }
        }

        [MenuItem("Разлом/Лагерь/Палитра дерева/Применить и обновить")]
        public static void ApplyMenu() => Apply();
        public static string Apply()
        {
            var camp = Camp(); Directory.CreateDirectory(CampWoodPaletteScreenshots.Output);
            var journal = AssetDatabase.LoadAssetAtPath<CampWoodPaletteJournal>(JournalPath);
            if (journal != null) CheckAssignments(camp, journal);
            var originals = new Dictionary<Renderer, Material[]>();
            foreach (var renderer in camp.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                var binding = journal?.Bindings.Find(b => b.Path == CampBridgePolishAuthoring.ScenePath(renderer.transform));
                var source = binding == null ? renderer.sharedMaterials : binding.Original;
                if (source.Any(IsCandidate)) originals.Add(renderer, source);
            }
            if (originals.Count == 0) throw new InvalidOperationException("No supported wooden props found.");
            // Resolve every shader and mask before touching any assignment.
            foreach (var source in originals.Values.SelectMany(x => x).Distinct())
            {
                if (!IsCandidate(source)) continue; string kind = Kind(source);
                TargetShader(source, kind); Mask(kind);
            }
            string before = ProtectedState(camp);
            string backup = Path.Combine(CampWoodPaletteScreenshots.Output, "before-scene.unity");
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(camp.gameObject.scene, backup, true)) throw new IOException("Could not back up camp.");
            EnsureFolder(Folder + "/Materials"); EnsureFolder("Assets/Editor/CampWoodPaletteData");
            if (journal == null)
            {
                journal = ScriptableObject.CreateInstance<CampWoodPaletteJournal>(); journal.ScenePath = camp.gameObject.scene.path;
                AssetDatabase.CreateAsset(journal, JournalPath);
            }
            string journalBefore = EditorJsonUtility.ToJson(journal);
            var assignmentsBefore = originals.Keys.ToDictionary(r => r, r => r.sharedMaterials);
            var rollback = new Dictionary<Material, string>(); var created = new List<Material>();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Camp wood palette");
            Undo.RecordObject(journal, "Camp wood palette journal");
            try
            {
                var copies = new Dictionary<Material, Material>();
                foreach (var entry in originals)
                {
                    string path = CampBridgePolishAuthoring.ScenePath(entry.Key.transform);
                    var binding = journal.Bindings.Find(b => b.Path == path);
                    if (binding == null)
                    {
                        binding = new CampWoodPaletteJournal.Binding { Path = path, Original = (Material[])entry.Value.Clone() };
                        journal.Bindings.Add(binding);
                    }
                    var replacement = (Material[])binding.Original.Clone();
                    for (int slot = 0; slot < replacement.Length; slot++)
                    {
                        var source = replacement[slot]; if (!IsCandidate(source)) continue; string kind = Kind(source);
                        if (!copies.TryGetValue(source, out var copy)) { copy = Prepare(source, kind, rollback, created); copies.Add(source, copy); }
                        replacement[slot] = copy;
                    }
                    binding.Polished = replacement; Assign(entry.Key, replacement);
                }
                if (before != ProtectedState(camp)) throw new InvalidOperationException("Material pass changed protected geometry, transforms, lights, colliders or navigation.");
                journal.Applied = true; EditorUtility.SetDirty(journal);
                string report = Validate();
                foreach (var material in copies.Values) AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.SaveAssetIfDirty(journal);
                EditorSceneManager.MarkSceneDirty(camp.gameObject.scene);
                if (!EditorSceneManager.SaveScene(camp.gameObject.scene)) throw new IOException("Could not save camp palette.");
                Undo.CollapseUndoOperations(group); SceneView.RepaintAll();
                File.WriteAllText(Path.Combine(CampWoodPaletteScreenshots.Output,"authoring-validation.txt"), report + "\nProtected before/after=" + before);
                return report;
            }
            catch
            {
                Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
                foreach (var pair in assignmentsBefore) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
                foreach (var pair in rollback) if (pair.Key != null) { EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key); EditorUtility.SetDirty(pair.Key); AssetDatabase.SaveAssetIfDirty(pair.Key); }
                EditorJsonUtility.FromJsonOverwrite(journalBefore, journal); EditorUtility.SetDirty(journal); AssetDatabase.SaveAssetIfDirty(journal);
                // Generated assets are retained for diagnosis; no original asset is ever deleted.
                SceneView.RepaintAll(); throw;
            }
        }

        [MenuItem("Разлом/Лагерь/Палитра дерева/Вернуть исходные материалы")]
        public static void RestoreMenu() => Restore();
        public static string Restore()
        {
            var camp = Camp(); var journal = AssetDatabase.LoadAssetAtPath<CampWoodPaletteJournal>(JournalPath);
            if (journal == null) return "No camp wood palette to restore.";
            CheckAssignments(camp, journal); string before = ProtectedState(camp);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Restore camp wood palette");
            Undo.RecordObject(journal, "Restore palette journal");
            foreach (var binding in journal.Bindings) Assign(Resolve(camp, binding.Path), binding.Original);
            journal.Applied = false; EditorUtility.SetDirty(journal); AssetDatabase.SaveAssetIfDirty(journal);
            if (before != ProtectedState(camp)) throw new InvalidOperationException("Unexpected protected state change during material restore.");
            EditorSceneManager.MarkSceneDirty(camp.gameObject.scene); EditorSceneManager.SaveScene(camp.gameObject.scene);
            Undo.CollapseUndoOperations(group); SceneView.RepaintAll(); return "Original camp prop materials restored; bridge pass retained.";
        }

        public static string Validate()
        {
            var camp = Camp(); var journal = AssetDatabase.LoadAssetAtPath<CampWoodPaletteJournal>(JournalPath);
            if (journal == null) throw new InvalidOperationException("No palette journal.");
            CheckAssignments(camp, journal);
            var report = new StringBuilder(); var materials = new HashSet<Material>(); var counts = new SortedDictionary<string, int>();
            foreach (var binding in journal.Bindings)
            {
                var expected = journal.Applied ? binding.Polished : binding.Original;
                if (!Resolve(camp, binding.Path).sharedMaterials.SequenceEqual(expected)) throw new InvalidOperationException("Incomplete palette assignment: " + binding.Path);
                for (int i = 0; i < binding.Original.Length; i++)
                {
                    var source = binding.Original[i]; if (!IsCandidate(source)) continue; string kind = Kind(source);
                    var material = binding.Polished[i];
                    if (material == null || !AssetDatabase.GetAssetPath(material).StartsWith(Folder + "/Materials/", StringComparison.Ordinal)
                        || material.shader.name != TargetShader(source, kind) || !material.shader.isSupported
                        || material.GetTexture("_BaseMap") != source.GetTexture("_BaseMap") || material.GetTexture("_WoodMask") != Mask(kind))
                        throw new InvalidOperationException("Invalid palette copy for " + binding.Path);
                    materials.Add(material); counts[kind] = counts.TryGetValue(kind, out int count) ? count + 1 : 1;
                }
            }
            report.AppendLine("PASS palette assignments=" + journal.Bindings.Count + "; unique materials=" + materials.Count + "; applied=" + journal.Applied);
            foreach (var count in counts) report.AppendLine(count.Key + "=" + count.Value);
            report.AppendLine("Protected state=" + ProtectedState(camp)); return report.ToString();
        }

        public static string ProtectedState(Transform camp)
        {
            var sb = new StringBuilder();
            foreach (var t in camp.GetComponentsInChildren<Transform>(true))
            {
                sb.Append(CampBridgePolishAuthoring.ScenePath(t)).Append('|').Append(t.gameObject.activeSelf).Append('|').Append(t.gameObject.layer);
                foreach (float v in new[] { t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w,t.localScale.x,t.localScale.y,t.localScale.z }) sb.Append('|').Append(v.ToString("R",CultureInfo.InvariantCulture));
                foreach (var component in t.GetComponents<Component>()) sb.Append('|').Append(component == null ? "missing" : component.GetType().FullName);
                var mesh = t.GetComponent<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null)
                {
                    sb.Append('|').Append(AssetDatabase.GetAssetPath(mesh.sharedMesh));
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh.sharedMesh, out string guid, out long localId))
                        sb.Append('|').Append(guid).Append('|').Append(localId);
                    else sb.Append('|').Append(mesh.sharedMesh.GetEntityId().ToString());
                }
                foreach (var collider in t.GetComponents<Collider>()) sb.Append('|').Append(EditorJsonUtility.ToJson(collider));
                var light = t.GetComponent<Light>();
                if (light != null) sb.Append('|').Append(light.enabled).Append('|').Append(light.color.ToString("R")).Append('|').Append(light.intensity.ToString("R",CultureInfo.InvariantCulture)).Append('|').Append(light.range.ToString("R",CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
            var river = camp.GetComponentInChildren<CampRiver>(); if (river != null) sb.Append(CampBridgePolishAuthoring.Invariant(river));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }
    }
}
