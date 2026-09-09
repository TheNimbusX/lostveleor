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
