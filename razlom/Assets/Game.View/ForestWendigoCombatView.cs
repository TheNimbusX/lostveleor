using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(640)]
    public sealed class ForestWendigoCombatView : MonoBehaviour
    {
        private sealed class Mark
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public int Entity = -1, Serial;
            public WendigoActionState Action;
            public Vector3[] Vertices = new Vector3[65*9];
            public Vector2[] Uvs = new Vector2[65*9];
        }
        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shown;
        private readonly Mark[] _marks = new Mark[4];
        private Material _material;
        private sealed class Dust
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public int Tick = -1000;
        }
        private readonly Dust[] _dust = new Dust[8];
        private int _dustCursor;
        private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
            IsSector = Shader.PropertyToID("_IsSector"), Impact = Shader.PropertyToID("_Impact");
        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>();
            _material = new Material(Resources.Load<Material>("Characters/Forest_Wendigo/WendigoWarning"));
            var indices = new int[64*8*6]; int at = 0;
            for (int y=0;y<8;y++) for(int x=0;x<64;x++)
            {
                int a=y*65+x,b=a+1,c=a+65,d=c+1;
                indices[at++]=a;indices[at++]=c;indices[at++]=b;indices[at++]=b;indices[at++]=c;indices[at++]=d;
            }
            for (int i=0;i<_marks.Length;i++)
            {
                var m=new Mark();m.Root=new GameObject("Вендиго: предупреждение "+i);m.Root.transform.SetParent(transform,false);
                m.Mesh=new Mesh {name="Wendigo danger"};m.Mesh.MarkDynamic();m.Mesh.vertices=m.Vertices;m.Mesh.triangles=indices;
                m.Root.AddComponent<MeshFilter>().sharedMesh=m.Mesh;m.Renderer=m.Root.AddComponent<MeshRenderer>();
                m.Renderer.sharedMaterial=_material;m.Renderer.shadowCastingMode=ShadowCastingMode.Off;m.Renderer.receiveShadows=false;
                m.Root.SetActive(false);_marks[i]=m;
            }
            var dustPrefab = Resources.Load<GameObject>("VFX/Pelag/Prefabs/VFX_DustSmall");
            if (dustPrefab != null)
                for (int i=0;i<_dust.Length;i++)
                {
                    var go=Instantiate(dustPrefab,transform);go.name="Вендиго: земля под когтями";
                    _dust[i]=new Dust {Root=go,Particles=go.GetComponentsInChildren<ParticleSystem>()};
                    foreach(var ps in _dust[i].Particles)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    go.SetActive(false);
                }
        }
        private void LateUpdate()
        {
            var sim=_driver.Sim;if(sim==null)return;
            if(_shown!=sim)
            {
                _shown=sim;foreach(var m in _marks){m.Entity=-1;m.Serial=0;m.Root.SetActive(false);}
                foreach(var d in _dust)if(d!=null){d.Tick=-1000;d.Root.SetActive(false);}
            }
            float tick=sim.Tick-1+_driver.Alpha;
            foreach(var c in _driver.FrameEventContexts)
            {
                if(c.Event.Type!=SimEventType.WendigoImpact||_dust[0]==null)continue;
                var d=_dust[_dustCursor++%_dust.Length];d.Tick=c.SimulationTick;
                var point=c.Event.Position;
                if(c.Event.ActionVariant==(int)WendigoAction.Claw&&sim.TryGetWendigoAction(c.Event.Source,out var action))
                    point+=action.Direction*Fix64.Ratio(17,10);
                float x=point.X.ToFloat(),z=point.Y.ToFloat();
                d.Root.transform.position=new Vector3(x,_layout.WeaponGroundHeight(x,z)+.035f,z);
                d.Root.transform.localScale=Vector3.one*(c.Event.ActionVariant==(int)WendigoAction.Leap?1.1f:.55f);
                d.Root.SetActive(true);
            }
            foreach(var d in _dust)
            {
                if(d==null||!d.Root.activeSelf)continue;
                float age=Mathf.Max(0,tick-d.Tick)/Simulation.TicksPerSecond;
                if(age>.65f){d.Root.SetActive(false);continue;}
                foreach(var ps in d.Particles){ps.Simulate(age,false,true,false);ps.Pause(false);}
            }
            foreach(var m in _marks)
            {
                if(m.Entity<0)continue;
                bool active=sim.TryGetWendigoAction(m.Entity,out var a)&&a.Serial==m.Serial;
                if(!active||!sim.Entities.Alive[m.Entity]||tick>m.Action.ImpactTick+9)
                {m.Root.SetActive(false);m.Entity=-1;continue;}
                float p=Mathf.Clamp01((tick-a.StartTick)/(a.ImpactTick-a.StartTick));
                float impact=Mathf.Max(0,tick-a.ImpactTick)/9;
                m.Block.SetFloat(Progress,p);m.Block.SetFloat(Impact,impact);
                m.Block.SetFloat(Opacity,impact>0?1-impact:1);m.Renderer.SetPropertyBlock(m.Block);
            }
            for(int id=1;id<sim.Entities.Count;id++)
            {
                if(!sim.TryGetWendigoAction(id,out var a)||tick>a.ImpactTick+9)continue;
                Mark free=null;bool exists=false;
                foreach(var m in _marks){if(m.Entity==id&&m.Serial==a.Serial)exists=true;if(m.Entity<0)free=m;}
                if(exists||free==null)continue;Build(free,id,a);
            }
        }
        private void Build(Mark m,int id,WendigoActionState a)
        {
            m.Entity=id;m.Serial=a.Serial;m.Action=a;
            bool sector=a.Kind==WendigoAction.Claw;float radius=sector?2.7f:1.25f;
            Vector2 center=new Vector2((sector?a.Origin:a.Target).X.ToFloat(),(sector?a.Origin:a.Target).Y.ToFloat());
            float facing=Mathf.Atan2(a.Direction.X.ToFloat(),a.Direction.Y.ToFloat());
            for(int y=0;y<=8;y++)for(int x=0;x<=64;x++)
            {
                float u=x/64f,v=y/8f,angle=facing+(u-.5f)*(sector?140:360)*Mathf.Deg2Rad;
                float px=center.x+Mathf.Sin(angle)*radius*v,pz=center.y+Mathf.Cos(angle)*radius*v;
                float py=_layout!=null?_layout.WeaponGroundHeight(px,pz):0;
                m.Vertices[y*65+x]=new Vector3(px,py+.055f,pz);m.Uvs[y*65+x]=new Vector2(u,v);
            }
            m.Mesh.vertices=m.Vertices;m.Mesh.uv=m.Uvs;m.Mesh.RecalculateBounds();
            m.Block.SetFloat(IsSector,sector?1:0);m.Block.SetFloat(Progress,0);m.Block.SetFloat(Opacity,1);m.Block.SetFloat(Impact,0);
            m.Renderer.SetPropertyBlock(m.Block);m.Root.SetActive(true);
        }
        private void OnDestroy()
        {
            foreach(var m in _marks)if(m!=null){Destroy(m.Mesh);Destroy(m.Root);}if(_material!=null)Destroy(_material);
            foreach(var d in _dust)if(d!=null)Destroy(d.Root);
        }
    }
}
