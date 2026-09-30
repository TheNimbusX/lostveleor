using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>Обратимая покраска среды: ни меши, ни коллизии, ни нарисованные дорожки не меняются.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(120)]
    public sealed class CampDepthPass : MonoBehaviour
    {
        public CampDepthPalette Palette;
        public bool PreviewEnabled = true;
        public Transform Fire, Smith, Trader, Alchemist, Tent, TravelTable;
        public CampRiver River;
        readonly Vector4[] _dry = new Vector4[5];
        readonly Vector4[] _ash = new Vector4[2];
        readonly List<Binding> _forest = new List<Binding>();
        CampDepthPalette _fallback;
        bool _refreshRequested, _applied, _commandOff;
        int _revision = -1;
        float _nextAnchorCheck;
        sealed class Binding
        {
            public Renderer Renderer;
            public MaterialPropertyBlock Original;
            public Color Color;
            public int Property;
            public float Weight;
        }
        public bool IsApplied => _applied;
        public int ForestRendererCount => _forest.Count;
        CampDepthPalette Settings
        {
            get
            {
                if (Palette != null) return Palette;
                if (_fallback == null) { _fallback = ScriptableObject.CreateInstance<CampDepthPalette>(); _fallback.hideFlags = HideFlags.HideAndDontSave; }
                return _fallback;
            }
        }
        void OnEnable()
        {
            _commandOff = Application.isPlaying && Array.IndexOf(Environment.GetCommandLineArgs(), "-camp-depth-off") >= 0;
            RefreshPass();
        }
        void OnValidate() { _refreshRequested = true; }
        void LateUpdate()
        {
            if (_refreshRequested || _revision != Settings.Revision || _applied != (PreviewEnabled && !_commandOff)) RefreshPass();
            if (_applied)
            {
                if ((Tent == null || TravelTable == null || Alchemist == null) && Time.realtimeSinceStartup >= _nextAnchorCheck)
                { DiscoverAnchors(); _nextAnchorCheck = Time.realtimeSinceStartup + .5f; }
                ApplyGlobals();
            }
        }
        public void DiscoverAnchors()
        {
            if (Fire == null) Fire = transform.Find("Campfire");
            if (Smith == null) Smith = transform.Find("Anchor - Smith");
            if (Trader == null) Trader = transform.Find("Anchor - Trader");
            if (Tent == null) Tent = transform.Find("Tent - Player");
            if (TravelTable == null) TravelTable = transform.Find("Travel Table");
            if (River == null) River = GetComponentInChildren<CampRiver>(true);
            // NPC живут отдельными корнями сцены; runtime-стол может появиться после установки.
            foreach (var npc in UnityEngine.Object.FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
            {
                if (npc.gameObject.scene != gameObject.scene) continue;
                switch (npc.Kind)
                {
                    case CampServiceKind.Smith: if (Smith == null) Smith = npc.transform; break;
                    case CampServiceKind.Trader: if (Trader == null) Trader = npc.transform; break;
                    case CampServiceKind.Alchemist: if (Alchemist == null) Alchemist = npc.transform; break;
                    case CampServiceKind.Tent: if (Tent == null) Tent = npc.Entrance != null ? npc.Entrance : npc.transform; break;
                    case CampServiceKind.TravelTable: if (TravelTable == null) TravelTable = npc.transform; break;
                }
            }
        }
        public void RefreshPass()
        {
            RestorePass(); DiscoverAnchors(); _refreshRequested = false; _revision = Settings.Revision;
            if (!isActiveAndEnabled || !PreviewEnabled || _commandOff) return;
            CaptureForest(); _applied = true; ApplyGlobals(); ApplyForest();
        }
        static Vector4 Zone(Transform anchor, float radius, float strength = 1f)
            => anchor != null ? new Vector4(anchor.position.x, anchor.position.z, radius, strength) : Vector4.zero;
        void ApplyGlobals()
        {
            var p = Settings; Vector3 centre = Fire != null ? Fire.position : transform.position;
            Shader.SetGlobalFloat("_CampDepthOn", 1f);
            Shader.SetGlobalVector("_CampDepthCentre", new Vector4(centre.x, centre.z, 9f, 25f));
            Shader.SetGlobalVector("_CampDepthControls", new Vector4(p.PathTextureScale, p.TextureSoftness, p.ContactStrength, p.FoliageGrouping));
            Shader.SetGlobalVector("_CampDepthEarthStrength", new Vector4(p.DryStrength, p.WetStrength, p.AshStrength, p.WetBandMeters));
            Shader.SetGlobalColor("_CampDepthDryTint", p.DryTint); Shader.SetGlobalColor("_CampDepthWetTint", p.WetTint);
            Shader.SetGlobalColor("_CampDepthAshTint", p.AshTint); Shader.SetGlobalColor("_CampDepthWarmTint", p.WarmCentreTint);
            Shader.SetGlobalVector("_CampDepthShore", new Vector4(p.ShoreBreakup, p.ShoreFoamStrength, 0f, 0f));
            _dry[0] = Zone(Smith, 4.2f); _dry[1] = Zone(Trader, 3.5f); _dry[2] = Zone(Tent, 3.6f);
            _dry[3] = Zone(TravelTable, 3.3f); _dry[4] = Zone(Alchemist, 3.1f, .65f);
            _ash[0] = Zone(Fire, 2.15f); _ash[1] = Zone(Smith, 2.35f, .75f);
            Shader.SetGlobalVectorArray("_CampDepthDryZones", _dry); Shader.SetGlobalVectorArray("_CampDepthAshZones", _ash);
            bool boundary = River != null && River.FoliageBoundary != null && River.BakedLandContour != null && River.BakedLandContour.Length > 1;
            Shader.SetGlobalTexture("_CampDepthRiverBoundary", boundary ? River.FoliageBoundary : Texture2D.blackTexture);
            if (River != null) Shader.SetGlobalMatrix("_CampDepthRiverToLocal", River.transform.worldToLocalMatrix);
            if (boundary)
            {
                var contour = River.BakedLandContour;
                Shader.SetGlobalVector("_CampDepthRiverRange", new Vector4(contour[0].x, contour[contour.Length - 1].x - contour[0].x,
                    1f / River.FoliageBoundary.width, 1f));
                Shader.SetGlobalVector("_CampDepthRiverShape", new Vector4(River.Width + River.BankWidth * 2f, Mathf.Abs(River.transform.lossyScale.z), 0f, 0f));
            }
            else Shader.SetGlobalVector("_CampDepthRiverRange", Vector4.zero);
        }
        void CaptureForest()
        {
            Vector3 centre = Fire != null ? Fire.position : transform.position;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer.GetComponentInParent<CampServiceNpc>() != null) continue;
                bool vegetation = false;
                for (Transform node = renderer.transform; node != null && node != transform; node = node.parent)
                {
                    string name = node.name.ToLowerInvariant();
                    if (name.Contains("tree") || name.Contains("spruce") || name.Contains("bush") || name.Contains("ель") || name.Contains("дерев") || name.Contains("куст")) { vegetation = true; break; }
                }
                if (!vegetation) continue;
                Vector3 at = renderer.bounds.center; float distance = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(centre.x, centre.z));
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 29f, distance));
                Material material = renderer.sharedMaterial; if (weight <= .001f || material == null) continue;
                int property = Shader.PropertyToID(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
                if (!material.HasProperty(property)) continue;
                var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
                Color color = original.HasProperty(property) ? original.GetColor(property) : material.GetColor(property);
                _forest.Add(new Binding { Renderer = renderer, Original = original, Color = color, Property = property, Weight = weight });
            }
        }
        void ApplyForest()
        {
            var p = Settings;
            foreach (var binding in _forest)
            {
                if (binding.Renderer == null) continue;
                var block = new MaterialPropertyBlock(); binding.Renderer.GetPropertyBlock(block);
                Color tint = Color.Lerp(Color.white, p.ForestTint, binding.Weight * p.ForestTintStrength); tint.a = 1f;
                block.SetColor(binding.Property, binding.Color * tint); binding.Renderer.SetPropertyBlock(block);
            }
        }
        public void RestorePass()
        {
            Shader.SetGlobalFloat("_CampDepthOn", 0f);
            foreach (var binding in _forest) if (binding.Renderer != null) binding.Renderer.SetPropertyBlock(binding.Original);
            _forest.Clear(); _applied = false;
        }
        public string Describe()
        {
            string At(Transform t) => t != null ? t.name + " " + t.position.ToString("F2") : "нет";
            return $"[camp-depth] applied={IsApplied} forestRenderers={ForestRendererCount}; dry: Smith={At(Smith)}, Trader={At(Trader)}, Tent={At(Tent)}, Table={At(TravelTable)}, Leo={At(Alchemist)}; ash: Fire={At(Fire)}, Smith={At(Smith)}; wetBoundary={(River != null && River.FoliageBoundary != null)}; paintedPaths=preserved; geometry=unchanged; off=-camp-depth-off";
        }
        void OnDisable() { RestorePass(); }
        void OnDestroy()
        {
            RestorePass(); if (_fallback == null) return;
            if (Application.isPlaying) Destroy(_fallback); else DestroyImmediate(_fallback);
        }
    }
}
