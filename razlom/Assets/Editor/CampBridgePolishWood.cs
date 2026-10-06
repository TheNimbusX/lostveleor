using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class CampBridgePolishWood
    {
        public const string ShaderName = "Game/Camp Bridge Wood";
        public const string AssetRoot = "Assets/Resources/Environment/Camp/BridgePolish";

        public sealed class Assignment
        {
            public Renderer Renderer { get; }
            public Material[] OriginalMaterials { get; }
            public Material[] Materials { get; }
            public string Kind { get; }

            internal Assignment(Renderer renderer, Material[] original, Material[] materials, string kind)
            {
                Renderer = renderer;
                OriginalMaterials = (Material[])original.Clone();
                Materials = (Material[])materials.Clone();
                Kind = kind;
            }
        }

        // Назначение делает координатор: здесь создаются только отдельные материалы для его журнала отката.
        public static List<Assignment> Prepare(Transform campRoot, Renderer bridge, float radius = 8f)
        {
            if (campRoot == null) throw new ArgumentNullException(nameof(campRoot));
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (!bridge.transform.IsChildOf(campRoot))
                throw new ArgumentException("Bridge renderer must belong to the supplied CampRoot.", nameof(bridge));
            if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Import CampBridgeWood.shader before Prepare.");

            var masks = new Dictionary<string, Texture2D>();
            foreach (string kind in new[] { "bridge", "fence", "bench", "crate" })
                masks.Add(kind, LoadMask(kind));
            EnsureFolder(AssetRoot + "/Materials");
            var assignments = new List<Assignment>();
            var copies = new Dictionary<Material, Material>();
            Vector3 centre = bridge.bounds.center;

            foreach (Renderer renderer in campRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer != bridge && HorizontalDistance(renderer.bounds, centre) > radius) continue;
                Material[] original = renderer.sharedMaterials;
                Material[] replacement = (Material[])original.Clone();
                string rendererKind = null;
                bool changed = false;
                for (int slot = 0; slot < original.Length; slot++)
                {
                    Material source = original[slot];
                    if (source == null || source.shader == shader || !source.HasProperty("_BaseMap")) continue;
                    string kind = KindForTexture(AssetDatabase.GetAssetPath(source.GetTexture("_BaseMap")));
                    if (kind == null || (renderer == bridge && kind != "bridge")) continue;
                    if (source.shader.name != "Universal Render Pipeline/Lit")
                        throw new InvalidOperationException("Expected URP Lit source: " + AssetDatabase.GetAssetPath(source));
                    if (!copies.TryGetValue(source, out Material material))
                    {
                        material = PrepareCopy(source, kind, shader, masks[kind]);
                        copies.Add(source, material);
                    }
                    replacement[slot] = material;
                    rendererKind = rendererKind == null ? kind : rendererKind + "+" + kind;
                    changed = true;
                }
                if (changed) assignments.Add(new Assignment(renderer, original, replacement, rendererKind));
            }
            return assignments;
        }

        static Material PrepareCopy(Material source, string kind, Shader shader, Texture2D mask)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
                throw new InvalidOperationException("Source material must be a persistent project asset: " + source.name);
            string path = AssetRoot + "/Materials/" + kind + "_" + guid + "_" + localId + ".mat";
            Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(source);
                AssetDatabase.CreateAsset(copy, path);
            }
            else
            {
                Undo.RegisterCompleteObjectUndo(copy, "Prepare camp bridge wood");
                EditorUtility.CopySerialized(source, copy);
            }
            copy.name = "Camp Bridge Polish " + kind + " — " + source.name;
            copy.shader = shader;
            copy.SetTexture("_WoodMask", mask);
            // Тон дерева пака 04.10 (06.10). SetColor сам переводит sRGB в линейный, второй .linear давал красноту.
            copy.SetColor("_WoodTone", new Color(.385f, .256f, .148f, 1f));
            copy.SetFloat("_WoodStrength", .86f);
            copy.SetFloat("_WoodSaturation", .78f);
            copy.SetFloat("_WoodExposure", kind == "bench" ? .86f : kind == "crate" ? .90f : 1f);
            copy.SetFloat("_WoodWetStrength", kind == "bridge" ? .6f : 0f);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        static Texture2D LoadMask(string kind)
        {
            string path = AssetRoot + "/Textures/" + kind + "_wood_mask.png";
            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (mask == null) throw new InvalidOperationException("Required wood-only UV mask is missing: " + path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Expected texture importer: " + path);
            if (importer.sRGBTexture || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
                mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return mask;
        }

        static string KindForTexture(string path)
        {
            if (path.Contains("wooden bridge/") && path.EndsWith("_0_0.jpg", StringComparison.Ordinal)) return "bridge";
            if (path.EndsWith("wooden+fence+3d+model_basecolor.jpg", StringComparison.Ordinal)) return "fence";
            if (path.EndsWith("wooden+bench+3d+model_basecolor.jpg", StringComparison.Ordinal)) return "bench";
            if (path.EndsWith("wooden+crate+3d+model_basecolor.jpg", StringComparison.Ordinal)) return "crate";
            return null;
        }

        static float HorizontalDistance(Bounds bounds, Vector3 centre)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(bounds.center.x - centre.x) - bounds.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(bounds.center.z - centre.z) - bounds.extents.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
