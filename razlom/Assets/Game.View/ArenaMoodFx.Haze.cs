using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Дымка чащи: три кольца-ленты вокруг поляны на шейдере дымки лагеря («Razlom/Camp Edge Haze»).
    // Прозрачная и рисуется после комикс-прохода, поэтому мягко прикрывает и тушь у края леса (туман внутри
    // непрозрачных шейдеров тушь не спрятал бы — план, раздел 4). На поляну не заходит: бой в чистом воздухе.
    public sealed partial class ArenaMoodFx
    {
        [Header("Дымка чащи")]
        [Tooltip("Яркость дымки от её цвета в настроении. Реф тумана: края тёмно-бирюзовые (#263A3A). С ,38 дымка " +
                 "сливалась с кронами и кромка оставалась зелёной (#21351E); с ,75 — бирюзовая вуаль (#294233), " +
                 "а пятно поляны держат солнце и cookie (съёмка 01.10).")]
        [Range(0f, 1.5f)] public float DayHazeTone = .8f;
        [Range(0f, 1.5f)] public float MistHazeTone = .75f;
        [Range(0f, 1.5f)] public float DuskHazeTone = 1f;
        [Tooltip("Размер пятен дымки, метры.")]
        [Range(2f, 40f)] public float HazePatch = 9f;
        [Range(0f, 1f)] public float HazePatchiness = .5f;

        private static readonly int ColourId = Shader.PropertyToID("_Color");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
        private static readonly int DriftId = Shader.PropertyToID("_Drift");

        private Mesh _hazeMesh;
        private Material _hazeMaterial;
        private MeshRenderer _hazeRenderer;
        private float[] _hazeXyz, _hazeAlpha;
        private Vector3[] _hazeVertices;
        private Color32[] _hazeColours;
        private int[] _hazeIndices;

        /// <summary>Кольца дымки по настроению; 0 — дымки нет (день).</summary>
        private int ShowHaze()
        {
            ArenaMoodState s = _state;
            if (s.HazeDensity < .02f) return 0;
            if (!EnsureHaze()) return 0;
            int samples = _contour.Length;
            CameraGround(out float forwardX, out float forwardZ, out float pitch);
            Vector3 centre = ArenaMood.GladeCenter;
            ArenaMoodFxRules.HazeRing(_contour, samples, centre.x, centre.z, ArenaMood.EdgeRingInner, ArenaMood.EdgeRingOuter,
                forwardX, forwardZ, pitch, _portalSegments, _portalCount, _hazeXyz, _hazeAlpha);
            for (int v = 0; v < _hazeVertices.Length; v++)
            {
                _hazeVertices[v] = new Vector3(_hazeXyz[v * 3], _hazeXyz[v * 3 + 1], _hazeXyz[v * 3 + 2]);
                _hazeColours[v] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(_hazeAlpha[v] * 255f), 0, 255));
            }
            _hazeMesh.vertices = _hazeVertices;
            _hazeMesh.colors32 = _hazeColours;
            _hazeMesh.RecalculateBounds();

            ArenaMoodWeights w = s.Weights;
            float tone = w.Blend(DayHazeTone, MistHazeTone, DuskHazeTone);
            _hazeMaterial.SetColor(ColourId, Scaled(s.HazeColor, tone));
            _hazeMaterial.SetFloat(DensityId, Mathf.Clamp01(s.HazeDensity));
            _hazeMaterial.SetFloat(NoiseScaleId, HazePatch);
            _hazeMaterial.SetFloat(NoiseStrengthId, HazePatchiness);
            _hazeMaterial.SetFloat(DriftId, Mathf.Max(0f, s.HazeDrift));
            _hazeRenderer.enabled = true;
            return ArenaMoodFxRules.HazeHeights.Length;
        }

        private bool EnsureHaze()
        {
            if (_hazeRenderer != null && _hazeMaterial != null && _hazeMesh != null) return true;
            if (_hazeMaterial == null) _hazeMaterial = LoadShaderMaterial("Shaders/CampEdgeHaze", "Свет арены: дымка чащи");
            if (_hazeMaterial == null) return false;
            int samples = _contour.Length;
            int vertices = ArenaMoodFxRules.HazeVertexCount(samples);
            _hazeXyz = new float[vertices * 3];
            _hazeAlpha = new float[vertices];
            _hazeVertices = new Vector3[vertices];
            _hazeColours = new Color32[vertices];
            _hazeIndices = new int[ArenaMoodFxRules.HazeIndexCount(samples)];
            ArenaMoodFxRules.HazeTriangles(_hazeIndices, samples);
            _hazeMesh = new Mesh { name = "Свет арены: кольца дымки", hideFlags = HideFlags.DontSave };
            _hazeMesh.MarkDynamic();
            _hazeMesh.vertices = _hazeVertices;
            _hazeMesh.colors32 = _hazeColours;
            _hazeMesh.SetIndices(_hazeIndices, MeshTopology.Triangles, 0, false);

            var host = new GameObject("Дымка чащи");
            host.transform.SetParent(_root, false);
            // Вершины в мировых координатах: объект в нуле мира, без поворота и масштаба.
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            host.AddComponent<MeshFilter>().sharedMesh = _hazeMesh;
            _hazeRenderer = host.AddComponent<MeshRenderer>();
            _hazeRenderer.sharedMaterial = _hazeMaterial;
            _hazeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _hazeRenderer.receiveShadows = false;
            _hazeRenderer.lightProbeUsage = LightProbeUsage.Off;
            _hazeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _hazeRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _hazeRenderer.allowOcclusionWhenDynamic = false;
            _hazeRenderer.enabled = false;
            return true;
        }

        private void HideHaze()
        {
            if (_hazeRenderer != null) _hazeRenderer.enabled = false;
        }

        private void DestroyHaze()
        {
            DestroyOwned(_hazeMesh);
            DestroyOwned(_hazeMaterial);
            _hazeMesh = null;
            _hazeMaterial = null;
            _hazeRenderer = null;
        }
    }
}
