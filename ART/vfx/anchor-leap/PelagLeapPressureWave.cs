using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>Изогнутая оболочка воздуха; вершины и свойства выделяются один раз при прогреве.</summary>
    public sealed class PelagLeapPressureWave : MonoBehaviour
    {
        private const int Segments=64, Rows=6;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private readonly Vector3[] _vertices=new Vector3[(Segments+1)*Rows];
        private MaterialPropertyBlock _properties;
        private static readonly int OpacityId=Shader.PropertyToID("_Opacity");
        private static readonly int PhaseId=Shader.PropertyToID("_Phase");

        public void Initialize(Material material)
        {
            _mesh=new Mesh { name="Pelag curved pressure shell" }; _mesh.MarkDynamic();
            var uv=new Vector2[_vertices.Length];
            var colors=new Color32[_vertices.Length];
            var indices=new int[Segments*(Rows-1)*6];
            for(int s=0;s<=Segments;s++)
                for(int row=0;row<Rows;row++)
                {
                    int index=s*Rows+row;
                    uv[index]=new Vector2(s/(float)Segments,row/(float)(Rows-1));
                    colors[index]=new Color32(255,255,255,255);
                    if(s==Segments||row==Rows-1) continue;
                    int n=(s*(Rows-1)+row)*6;
                    indices[n]=index;indices[n+1]=index+Rows;indices[n+2]=index+1;
                    indices[n+3]=index+1;indices[n+4]=index+Rows;indices[n+5]=index+Rows+1;
                }
            _mesh.vertices=_vertices;_mesh.uv=uv;_mesh.colors32=colors;_mesh.triangles=indices;
            _mesh.bounds=new Bounds(Vector3.zero,Vector3.one*6);
            gameObject.AddComponent<MeshFilter>().sharedMesh=_mesh;
            _renderer=gameObject.AddComponent<MeshRenderer>();_renderer.sharedMaterial=material;
            _renderer.shadowCastingMode=ShadowCastingMode.Off;_renderer.receiveShadows=false;
            _renderer.lightProbeUsage=LightProbeUsage.Off;_renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            _properties=new MaterialPropertyBlock();_renderer.enabled=false;
        }

        public void Present(Vector3 center,Vector3 direction,float travel,int layer,bool active)
        {
            _renderer.enabled=active;
            if(!active) return;
            // Волны рождаются по очереди, расширяются и отстают от тела; неподвижного кольца нет.
            float elapsed=travel-layer*.10f;
            if(elapsed<0) { _renderer.enabled=false;return; }
            float phase=Mathf.Repeat(elapsed,.31f)/.31f;
            float envelope=Mathf.SmoothStep(0,1,phase/.17f)*(1-Mathf.SmoothStep(.68f,1,phase));
            transform.SetPositionAndRotation(center-direction*(phase*.85f),Quaternion.LookRotation(direction));
            float radius=Mathf.Lerp(.55f,1.25f,Mathf.SmoothStep(0,1,phase));
            float span=Mathf.Lerp(2.6f,4.5f,Mathf.Sin(phase*Mathf.PI));
            float roll=layer*2.2f+phase*2.5f;
            for(int s=0;s<=Segments;s++)
            {
                float u=s/(float)Segments;
                float angle=(u-.5f)*span+roll;
                float taper=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*Mathf.PI)),.7f);
                for(int row=0;row<Rows;row++)
                {
                    float v=row/(float)(Rows-1)-.5f;
                    float r=radius+v*.90f*taper;
                    float z=.22f*Mathf.Cos(v*Mathf.PI)-v*.48f-.36f*Mathf.Pow(2*u-1,2);
                    _vertices[s*Rows+row]=new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r*.82f,z);
                }
            }
            _mesh.vertices=_vertices;
            _properties.SetFloat(OpacityId,envelope);
            _properties.SetFloat(PhaseId,phase+layer*.37f);
            _renderer.SetPropertyBlock(_properties);
        }

        public void Hide() { if(_renderer!=null) _renderer.enabled=false; }
        private void OnDestroy() { if(_mesh!=null) Destroy(_mesh); }
    }
}
