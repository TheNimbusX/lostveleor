using UnityEngine;

namespace Game.View
{
    /// <summary>Локальный художественный участок; геометрия не участвует в навигации.</summary>
    [ExecuteAlways]
    public sealed class CampGroundStudy : MonoBehaviour
    {
        public float Radius = 5.5f;
        public Renderer[] AuthoredSurfaces;
        public bool[] OriginalEnabled;
        private Texture2D _footstepSurfaceMap;
        private Vector4 _footstepSurfaceBounds;

        public bool IsDustyPath(Vector3 position)
        {
            if (!isActiveAndEnabled) return false;
            if (_footstepSurfaceMap == null)
            {
                foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty("_IsSurface")
                        || material.GetFloat("_IsSurface") < 0.5f) continue;
                    _footstepSurfaceMap = material.GetTexture("_SurfaceMap") as Texture2D;
                    _footstepSurfaceBounds = material.GetVector("_SurfaceBounds");
                    break;
                }
            }
            if (_footstepSurfaceMap == null || !_footstepSurfaceMap.isReadable
                || _footstepSurfaceBounds.z <= 0f || _footstepSurfaceBounds.w <= 0f) return false;
            float u = (position.x - _footstepSurfaceBounds.x) / _footstepSurfaceBounds.z;
            float v = (position.z - _footstepSurfaceBounds.y) / _footstepSurfaceBounds.w;
            // Same red channel that draws the road. Keep the grassy fringe quiet.
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f
                && _footstepSurfaceMap.GetPixelBilinear(u, v).r >= 0.82f;
        }
        static readonly int Area = Shader.PropertyToID("_CampStudyArea");
        void OnEnable()
        {
            Refresh();
            if(Application.isPlaying && System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-capture-ground-key")>=0)
                Shader.SetGlobalFloat("_CampShadowDiagnostic",2);
        }
        void LateUpdate() { UpdateArea(); }
        void OnDisable()
        {
            Shader.SetGlobalVector(Area, Vector4.zero);
            Shader.SetGlobalFloat("_CampShadowDiagnostic",0);
            if(AuthoredSurfaces!=null && OriginalEnabled!=null)
                for(int i=0;i<AuthoredSurfaces.Length && i<OriginalEnabled.Length;i++)
                    if(AuthoredSurfaces[i]!=null) AuthoredSurfaces[i].enabled=OriginalEnabled[i];
            foreach(var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
        }
        public void Refresh()
        {
            UpdateArea();
            if(AuthoredSurfaces!=null) foreach(var renderer in AuthoredSurfaces) if(renderer!=null)renderer.enabled=false;
            foreach(var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled=true;
        }
        void UpdateArea()
        {
            Vector3 p = transform.position;
            Shader.SetGlobalVector(Area, new Vector4(p.x, p.z, Radius, 1));
        }
    }
}
