using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    // Один контур определяет воду, берег, расчистку травы и непроходимую сторону лагеря.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CampRiver : MonoBehaviour
    {
        public Vector2[] Contour = { new Vector2(-100,0),new Vector2(-22,.3f),new Vector2(-10,-.35f),new Vector2(0,0),new Vector2(10,1.2f),new Vector2(24,.4f),new Vector2(100,0) };
        [Min(1)] public float Width = 6;
        [Min(.3f)] public float BankWidth = 1.3f;
        public float WaterLevel = -.55f;
        [Range(0,2)] public float FlowSpeed = .3f;
        public MeshFilter Ground;
        public Mesh SourceGround;
        public Material WaterMaterial;
        public Transform Geometry;
        public Transform Dressing;
        [HideInInspector] public bool HasBakedShape;
        [HideInInspector] public Vector2[] BakedContour;
        [HideInInspector] public float BakedWidth,BakedBankWidth;
        [HideInInspector] public Vector2[] BakedCentres,BakedNormals,BakedLandContour,BakedWaterContour;
        [HideInInspector] public Texture2D FoliageBoundary;
        Matrix4x4 _lastMatrix;float _lastFlow=-1;bool _refreshRequested;
        static float Interpolate(Vector2[] contour,float x)
        {
            if(contour==null || contour.Length<2)return 0;
            for(int i=1;i<contour.Length;i++)
                if(x<=contour[i].x)return Mathf.Lerp(contour[i-1].y,contour[i].y,Mathf.InverseLerp(contour[i-1].x,contour[i].x,x));
            return contour[contour.Length-1].y;
        }
        public float CentreAt(float x)=>Interpolate(HasBakedShape && BakedCentres!=null && BakedCentres.Length>1?BakedCentres:Contour,x);
        float BuiltCentreAt(float x)=>Interpolate(HasBakedShape?BakedContour:Contour,x);
        float BuiltWidth=>HasBakedShape?BakedWidth:Width;
        public void BakeShape()
        {
            var centres=new List<Vector2>();var normals=new List<Vector2>();
            for(int i=0;i<Contour.Length-1;i++)
            {
                Vector2 a=Contour[i],b=Contour[i+1];float length=Vector2.Distance(a,b);
                Vector2 enter=(b-(i>0?Contour[i-1]:a)).normalized,leave=((i+2<Contour.Length?Contour[i+2]:b)-a).normalized;
                float enterLength=Mathf.Min(length*.3f,(b.x-a.x)*.45f/Mathf.Max(.001f,enter.x));
                float leaveLength=Mathf.Min(length*.3f,(b.x-a.x)*.45f/Mathf.Max(.001f,leave.x));
                Vector2 c=a+enter*enterLength,d=b-leave*leaveLength;
                int steps=Mathf.Max(2,Mathf.CeilToInt(length/.65f));
                for(int j=i==0?0:1;j<=steps;j++)
                {
                    float t=j/(float)steps,u=1-t;Vector2 p=u*u*u*a+3*u*u*t*c+3*u*t*t*d+t*t*t*b;
                    Vector2 tangent=(3*u*u*(c-a)+6*u*t*(d-c)+3*t*t*(b-d)).normalized;
                    centres.Add(p);normals.Add(new Vector2(-tangent.y,tangent.x));
                }
            }
            var land=new Vector2[centres.Count];var water=new Vector2[centres.Count];
            for(int i=0;i<land.Length;i++)
            {
                land[i]=centres[i]+normals[i]*(Width*.5f+BankWidth);water[i]=centres[i]+normals[i]*(Width*.5f+.3f);
                if(i>0 && (land[i].x<=land[i-1].x+.001f || water[i].x<=water[i-1].x+.001f))throw new System.InvalidOperationException("Изгиб слишком тесный: разнеси соседние точки русла или уменьши ширину берега.");
            }
            BakedCentres=centres.ToArray();BakedNormals=normals.ToArray();BakedLandContour=land;BakedWaterContour=water;
            BakedContour=(Vector2[])Contour.Clone();BakedWidth=Width;BakedBankWidth=BankWidth;HasBakedShape=true;
        }
        public float LandEdge(float x)=>HasBakedShape && BakedLandContour!=null && BakedLandContour.Length>1?Interpolate(BakedLandContour,x):CentreAt(x)+Width*.5f+BankWidth;
        float BlockedEdge(float x)=>HasBakedShape && BakedWaterContour!=null && BakedWaterContour.Length>1?Interpolate(BakedWaterContour,x):BuiltCentreAt(x)+BuiltWidth*.5f+.3f;
        public bool ContainsBlocked(Vector3 world)
        {
            Vector3 p=transform.InverseTransformPoint(world);
            return p.z<BlockedEdge(p.x);
        }
        public void AddNavigationSources(List<NavMeshBuildSource> sources)
        {
            var passage=GetComponent<CampRiverPassage>();
            if(passage!=null && passage.enabled) { AddPassageSources(sources,passage); passage.AddRailingSources(sources); return; }
            // ModifierBox перекрывает также старый сплошной пол и пространство за дальним берегом.
            for(float x=-140;x<140;x+=.5f)
            {
                float edge=Mathf.Min(BlockedEdge(x),BlockedEdge(x+.5f));
                sources.Add(new NavMeshBuildSource { shape=NavMeshBuildSourceShape.ModifierBox,area=1,
                    transform=transform.localToWorldMatrix*Matrix4x4.Translate(new Vector3(x+.25f,0,(edge-180)*.5f)),
                    size=new Vector3(.502f,30,edge+180) });
            }
        }
        void AddPassageSources(List<NavMeshBuildSource> sources,CampRiverPassage passage)
        {
            const float step=.25f;
            void Block(float x,float lo,float hi)
            {
                if(hi<=lo)return;
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,
                    transform=transform.localToWorldMatrix*Matrix4x4.Translate(new Vector3(x+step*.5f,0,(lo+hi)*.5f)),size=new Vector3(step+.002f,30,hi-lo)});
            }
            for(float x=-140;x<140;x+=step)
            {
                float edge=Mathf.Min(BlockedEdge(x),BlockedEdge(x+step));
                float start=-180;
                // Обрабатываем детально только окрестности перехода, сохраняя сплошной внешний запрет.
                for(float z=-30;z<edge;z+=step)
                {
                    bool open=passage.IsOpen(this,transform.TransformPoint(new Vector3(x,0,z))) &&
                        passage.IsOpen(this,transform.TransformPoint(new Vector3(x+step,0,z))) &&
                        passage.IsOpen(this,transform.TransformPoint(new Vector3(x,0,z+step))) &&
                        passage.IsOpen(this,transform.TransformPoint(new Vector3(x+step,0,z+step)));
                    if(open) { Block(x,start,z);start=z+step; }
                }
                Block(x,start,edge);
            }
        }
        void OnEnable()=>Refresh();
        void OnValidate()=>_refreshRequested=true;
        void Update()
        {
            if(_refreshRequested)Refresh();
            // Контур и текстура загружаются один раз; в кадре меняется только положение или скорость.
            var matrix=transform.worldToLocalMatrix;if(matrix!=_lastMatrix){_lastMatrix=matrix;Shader.SetGlobalMatrix("_CampRiverWorldToLocal",matrix);}
            if(_lastFlow!=FlowSpeed && WaterMaterial!=null){_lastFlow=FlowSpeed;WaterMaterial.SetFloat("_FlowSpeed",FlowSpeed);}
        }
        public void Refresh()
        {
            _refreshRequested=false;
            _lastMatrix=transform.worldToLocalMatrix;Shader.SetGlobalMatrix("_CampRiverWorldToLocal",_lastMatrix);
            bool ready=FoliageBoundary!=null && BakedLandContour!=null && BakedLandContour.Length>1;
            if(ready){Shader.SetGlobalTexture("_CampRiverBoundary",FoliageBoundary);Shader.SetGlobalVector("_CampRiverBoundaryRange",new Vector4(BakedLandContour[0].x,BakedLandContour[BakedLandContour.Length-1].x-BakedLandContour[0].x,1f/FoliageBoundary.width,0));Shader.SetGlobalVector("_CampRiverBand",RiverBand);}
            Shader.SetGlobalVector("_CampRiverMask",new Vector4(0,0,ready?1:0,0));
            if(WaterMaterial!=null)
            {
                _lastFlow=FlowSpeed;if(WaterMaterial.HasProperty("_FlowSpeed"))WaterMaterial.SetFloat("_FlowSpeed",FlowSpeed);
            }
        }
        /// <summary>
        /// Где растения прячутся (аудит 23 сентября): только вода с запасом 15 см. x — от кромки травы
        /// ближнего берега до воды, y — ширина воды. Берега и дальний берег за рекой зарастают.
        /// </summary>
        Vector4 RiverBand
        {
            get
            {
                float bank=HasBakedShape?BakedBankWidth:BankWidth,width=HasBakedShape?BakedWidth:Width;
                return new Vector4(bank-.3f-.15f,width+.6f+.3f,0,0);
            }
        }
        void OnDisable()=>Shader.SetGlobalVector("_CampRiverMask",Vector4.zero);
    }
}
