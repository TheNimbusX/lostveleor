using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Круг четырёх стихий")]
    public sealed class CampMagicCircle : MonoBehaviour
    {
        public Transform FireAltar, IceAltar, AlchemyAltar, EarthAltar, Centre, Heart;
        public Material FlameMaterial;
        public Material PavingMaterial;
        public Texture2D PavingTexture;
        [Range(0f,2f)] public float GlowStrength=1f;
        [Range(.2f,2f)] public float FlowSpeed=.7f;
        [Range(0f,2f)] public float Atmosphere=1f;
        public Vector3 FireSocket=new Vector3(0,.41f,-.045f);
        public Vector3 AlchemySocket=new Vector3(0,.53f,.065f);
        public Vector3 FirePathSocket=new Vector3(0,.025f,.44f);
        public Vector3 IcePathSocket=new Vector3(0,.025f,.44f);
        public Vector3 AlchemyPathSocket=new Vector3(.235f,.025f,.147f);
        public Vector3 EarthPathSocket=new Vector3(.035f,.025f,.267f);
        [Header("Сохранённое оформление — можно править в Scene")]
        public Transform DecorationRoot, PlazaRoot, PathsRoot, CrystalsRoot, EffectsRoot, LightsRoot;
        public Transform[] PathStarts=new Transform[4], PathEnds=new Transform[4];
        public Transform EarthToe, EarthBend;
        public Transform[] FlameBillboards;
        public Material[] StoneMaterialAssets;

        GameObject _decoration;
        readonly List<Material> _materials=new List<Material>();
        readonly List<Mesh> _meshes=new List<Mesh>();
        readonly List<Transform> _flames=new List<Transform>();
        readonly List<Light> _lights=new List<Light>();
        readonly List<float> _lightIntensities=new List<float>();
        readonly List<ParticleSystem> _particles=new List<ParticleSystem>();
        readonly List<float> _particleRates=new List<float>();
        Renderer[] _surfaces;
        MaterialPropertyBlock _block;
        Vector3 _heartRest;
        Quaternion _heartRotation;
        Camera _camera;
        float _started;
        Transform _groundTransform;
        Vector3[] _groundVertices;
        Bounds _groundBounds;
        int _groundSide;
        Material[] _stoneMaterials;
        Transform _buildParent;
        bool _ownsDecoration, _animationInitialized;
        static readonly int Clock=Shader.PropertyToID("_CampMagicClock");
        static readonly int Strength=Shader.PropertyToID("_MagicStrength");

        void Start()
        {
            if(Centre==null || Heart==null){enabled=false;return;}
            if(DecorationRoot==null){BuildDecoration();_ownsDecoration=true;}
            _decoration=DecorationRoot.gameObject;
            _started=Time.unscaledTime;_heartRest=Heart.localPosition;_heartRotation=Heart.localRotation;
            _camera=Camera.main;_block=new MaterialPropertyBlock();_surfaces=GetComponentsInChildren<Renderer>();
            _lights.Clear();_lightIntensities.Clear();_flames.Clear();
            foreach(var light in DecorationRoot.GetComponentsInChildren<Light>()) {_lights.Add(light);_lightIntensities.Add(light.intensity);}
            if(FlameBillboards!=null)foreach(var flame in FlameBillboards)if(flame!=null)_flames.Add(flame);
            _particles.Clear();_particleRates.Clear();
            foreach(var particles in DecorationRoot.GetComponentsInChildren<ParticleSystem>())
            {_particles.Add(particles);_particleRates.Add(particles.emission.rateOverTimeMultiplier);particles.Play();}
            _animationInitialized=true;
            Debug.Log($"[camp-magic] authored={ !_ownsDecoration } stones={DecorationRoot.GetComponentsInChildren<CampMagicStone>().Length} lights={_lights.Count} decorativeColliders={DecorationRoot.GetComponentsInChildren<Collider>().Length}");
        }

        // Редактор вызывает сборку явно. Play использует сохранённые объекты и не пересоздаёт ручные правки.
        public void BuildDecoration()
        {
            if(Centre==null || Heart==null) {enabled=false;return;}
            _started=Time.unscaledTime;_heartRest=Heart.localPosition;_heartRotation=Heart.localRotation;
            _camera=Camera.main;_block=new MaterialPropertyBlock();
            _surfaces=GetComponentsInChildren<Renderer>();
            RefreshGround();
            _materials.Clear();_meshes.Clear();_lights.Clear();_lightIntensities.Clear();_flames.Clear();_stoneMaterials=null;
            _decoration=new GameObject("Оформление школы магии");_decoration.transform.SetParent(transform,false);
            DecorationRoot=_decoration.transform;_decoration.AddComponent<CampMagicDecoration>();
            PlazaRoot=Group("Площадка — отдельные плиты");
            CrystalsRoot=Group("Кристаллы у основания");
            EffectsRoot=Group("Пламя и частицы");LightsRoot=Group("Освещение");
            _buildParent=PlazaRoot;BuildPlaza();
            BuildPaths();
            _buildParent=EffectsRoot;
            if(FireAltar!=null)
            {
                Vector3 fire=FireAltar.TransformPoint(FireSocket);
                BuildFlame(fire,1.9f,3.5f);
                AddLight("Огонь чаши",fire+Vector3.up*.45f,new Color(1,.42f,.09f),1.25f,2.8f);
                Motes("Искры чаши",fire,new Color(1.5f,.5f,.06f),4,2.1f,.032f,new Vector3(.35f,.04f,.35f),new Vector3(.015f,.42f,0),0);
            }
            if(IceAltar!=null)
            {
                Vector3 ice=IceAltar.TransformPoint(new Vector3(0,.4f,0));
                AddLight("Холод кристалла",ice,new Color(.14f,.65f,1),1.1f,2.8f);
                Motes("Ледяная пыль",ice,new Color(.4f,1.25f,1.6f),5,3.5f,.045f,new Vector3(1.1f,1.4f,1.1f),new Vector3(.015f,.08f,-.02f),0);
            }
            if(AlchemyAltar!=null)
            {
                Vector3 liquid=AlchemyAltar.TransformPoint(AlchemySocket);
                AddLight("Алхимическое зелье",liquid+Vector3.up*.2f,new Color(.48f,1,.09f),1.15f,2.65f);
                Motes("Пузырьки зелья",liquid,new Color(.65f,1.25f,.12f),4,1.45f,.11f,new Vector3(.65f,.01f,.65f),new Vector3(0,.28f,0),2);
                Motes("Пар зелья",liquid,new Color(.17f,.29f,.035f),3,2.7f,.31f,new Vector3(.5f,.02f,.5f),new Vector3(.03f,.21f,0),1);
            }
            if(EarthAltar!=null)
            {
                Vector3 earth=EarthAltar.TransformPoint(new Vector3(0,.4f,0));
                AddLight("Золотые руны",earth,new Color(1,.69f,.21f),.28f,2.2f);
                Motes("Пыль древних рун",earth,new Color(1.4f,.85f,.16f),3,3.4f,.035f,new Vector3(.65f,1.25f,.55f),new Vector3(0,.08f,0),0);
            }
            Vector3 core=Heart.position+Vector3.up*.2f;
            AddLight("Сердце круга",core,new Color(.18f,1,.85f),2.1f,2.8f);
            Motes("Эфир вокруг сердца",core+Vector3.up*.6f,new Color(.4f,1.2f,1.1f),7,3.6f,.055f,new Vector3(1.35f,1.35f,1.35f),new Vector3(0,.12f,0),0);
            _buildParent=CrystalsRoot;BuildCrystalCluster();_buildParent=null;
            FlameBillboards=_flames.ToArray();
            if(_camera!=null)foreach(var flame in _flames)flame.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(_camera.transform.forward,Vector3.up),Vector3.up);
        }

        public void RefreshGround()
        {
            _groundTransform=null;_groundVertices=null;
            var study=FindAnyObjectByType<CampGroundStudy>();
            if(study!=null)
            foreach(var filter in study.GetComponentsInChildren<MeshFilter>())
            {
                var river=FindAnyObjectByType<CampRiver>();
                var mesh=river!=null && river.Ground==filter?river.SourceGround:filter.sharedMesh;
                if(mesh==null || (!mesh.name.StartsWith("Camp surface") && (river==null || mesh!=river.SourceGround)) || !mesh.isReadable)continue;
                int side=Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount));if(side*side!=mesh.vertexCount)continue;
                _groundTransform=filter.transform;_groundVertices=mesh.vertices;_groundBounds=mesh.bounds;_groundSide=side;break;
            }
        }
        public void BuildPaths()
        {
            _decoration=DecorationRoot.gameObject;RefreshGround();
            PathsRoot=Group("Дорожки — камни и потоки");
            Material ribbon=NewMaterial("Game/Camp Magic Conduit");
            if(ribbon!=null)
            {
                ribbon.SetFloat("_FlowSpeed",FlowSpeed);
                Transform[] altars={FireAltar,IceAltar,AlchemyAltar,EarthAltar};
                Color[] colors={new Color(2.5f,.55f,.06f),new Color(.08f,1.1f,2.4f),new Color(.6f,1.6f,.06f),new Color(2.5f,1.3f,.16f)};
                string[] names={"Огонь","Лёд","Яд","Земля"};
                for(int i=0;i<altars.Length;i++)if(altars[i]!=null)
                {
                    _buildParent=PathsRoot;_buildParent=Child(names[i],Centre.position).transform;
                    BuildConduit(altars[i],colors[i],i,ribbon);
                }
            }
            _buildParent=null;
        }

        void LateUpdate(){if(_animationInitialized)Apply();}
        void Apply()
        {
            float time=Time.unscaledTime-_started;
            Shader.SetGlobalFloat(Clock,time);
            if(_decoration==null)return;
            if(Heart!=null)
            {
                Heart.localPosition=_heartRest+Vector3.up*(Mathf.Sin(time*1.08f)*.045f/Mathf.Max(.01f,transform.lossyScale.y));
                Heart.localRotation=_heartRotation*Quaternion.Euler(Mathf.Sin(time*.57f)*1.8f,Mathf.Sin(time*.34f)*4,Mathf.Cos(time*.63f)*1.3f);
            }
            if(_camera==null)_camera=Camera.main;
            if(_camera!=null)
            {
                Vector3 facing=Vector3.ProjectOnPlane(_camera.transform.forward,Vector3.up);
                if(facing.sqrMagnitude>.001f)foreach(var flame in _flames)if(flame!=null)flame.rotation=Quaternion.LookRotation(facing,Vector3.up);
            }
            for(int i=0;i<_lights.Count;i++)if(_lights[i]!=null)
                _lights[i].intensity=_lightIntensities[i]*GlowStrength*(.92f+.06f*Mathf.Sin(time*(i==0?3.3f:.9f)+i*1.7f)+.02f*Mathf.Sin(time*5.4f+i));
            for(int i=0;i<_particles.Count;i++)if(_particles[i]!=null)
            {var emission=_particles[i].emission;emission.rateOverTimeMultiplier=_particleRates[i]*Atmosphere;}
            foreach(var surface in _surfaces)
            {
                if(surface==null || surface.sharedMaterial==null)continue;
                string shader=surface.sharedMaterial.shader.name;
                if(shader!="Game/Camp Magic Lit" && shader!="Game/Camp Magic Conduit")continue;
                surface.GetPropertyBlock(_block);
                if(shader=="Game/Camp Magic Lit")_block.SetFloat(Strength,GlowStrength);else _block.SetFloat("_FlowSpeed",FlowSpeed);
                surface.SetPropertyBlock(_block);
            }
        }

        Material NewMaterial(string shaderName)
        {
            var shader=Shader.Find(shaderName);if(shader==null){Debug.LogError("[camp-magic] missing "+shaderName);return null;}
            var material=new Material(shader);_materials.Add(material);return material;
        }
        GameObject Child(string name,Vector3 worldPosition)
        {
            var parent=_buildParent!=null?_buildParent:_decoration.transform;
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=worldPosition;
            go.transform.rotation=Quaternion.identity;Vector3 s=parent.lossyScale;
            go.transform.localScale=new Vector3(1/s.x,1/s.y,1/s.z);return go;
        }
        Transform Group(string name)
        {
            var previous=_buildParent;_buildParent=DecorationRoot;var group=Child(name,Centre.position).transform;_buildParent=previous;return group;
        }
        void AddLight(string name,Vector3 at,Color color,float intensity,float range)
        {
            var previous=_buildParent;_buildParent=LightsRoot;var light=Child(name,at).AddComponent<Light>();_buildParent=previous;light.type=LightType.Point;light.color=color;
            light.intensity=intensity;light.range=range;light.shadows=LightShadows.None;
            _lights.Add(light);_lightIntensities.Add(intensity);
        }
        void BuildFlame(Vector3 at,float width,float height)
        {
            if(FlameMaterial==null)return;
            var flame=NewMaterial("Game/Camp Altar Flame");if(flame==null)return;
            flame.SetTexture("_BaseMap",FlameMaterial.GetTexture("_BaseMap"));
            var go=Child("Пламя алтаря",at+Vector3.up*(height*.34f));
            var mesh=new Mesh {name="Magic fire billboard"};
            mesh.vertices=new[]{new Vector3(-width/2,-height/2,0),new Vector3(width/2,-height/2,0),new Vector3(-width/2,height/2,0),new Vector3(width/2,height/2,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateBounds();_meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=FlameMaterial;
            renderer.sharedMaterial=flame;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;_flames.Add(go.transform);
        }
        void Motes(string name,Vector3 at,Color color,float rate,float life,float size,Vector3 box,Vector3 velocity,int style)
        {
            var material=NewMaterial("Game/Camp Magic Motes");if(material==null)return;material.SetFloat("_Style",style);
            var go=Child(name,at);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=true;main.useUnscaledTime=true;main.simulationSpace=ParticleSystemSimulationSpace.Local;
            main.maxParticles=40;main.startLifetime=new ParticleSystem.MinMaxCurve(life*.7f,life);main.startSpeed=0;
            main.startSize=new ParticleSystem.MinMaxCurve(size*.65f,size*1.35f);main.startColor=color;
            var emission=ps.emission;emission.rateOverTime=rate;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=box;
            var motion=ps.velocityOverLifetime;motion.enabled=true;motion.space=ParticleSystemSimulationSpace.Local;
            motion.x=new ParticleSystem.MinMaxCurve(velocity.x-.035f,velocity.x+.035f);motion.y=new ParticleSystem.MinMaxCurve(velocity.y*.7f,velocity.y*1.3f);motion.z=new ParticleSystem.MinMaxCurve(velocity.z-.025f,velocity.z+.025f);
            var noise=ps.noise;noise.enabled=true;noise.strength=style==1?.05f:.025f;noise.frequency=.75f;noise.scrollSpeed=.2f;
            var over=ps.colorOverLifetime;over.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(style==1?.3f:.85f,.18f),new GradientAlphaKey(style==1?.18f:.55f,.7f),new GradientAlphaKey(0,1)});over.color=gradient;
            var sizeOver=ps.sizeOverLifetime;sizeOver.enabled=true;sizeOver.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.55f),new Keyframe(.35f,1),new Keyframe(1,style==1?1.8f:.6f)));
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(Application.isPlaying)ps.Play();
        }
        void BuildConduit(Transform altar,Color color,int index,Material material)
        {
            const int count=41;var positions=new Vector3[count];float length=0;
            for(int i=0;i<count;i++)
            {
                float t=i/(float)(count-1);Vector3 p=ConduitPoint(altar,t);
                p.y=SurfaceHeight(p)+.075f;positions[i]=p;
                if(i>0)length+=Vector3.Distance(positions[i-1],p);
            }
            BuildPaving(positions,length,index);
            for(int i=1;i<count-1;i++)
            {
                Vector3 across=Vector3.Cross(Vector3.up,positions[i+1]-positions[i-1]).normalized;
                Vector3 p=positions[i]+across*(Mathf.Sin(i*2.31f+index)*.028f*Mathf.Sin(i/(float)(count-1)*Mathf.PI));
                p.y=SurfaceHeight(p)+.075f;positions[i]=p;
            }
            var vertices=new Vector3[count*2];var uv=new Vector2[count*2];var colors=new Color[count*2];var triangles=new int[(count-1)*6];
            var go=Child("Поток стихии "+index,Vector3.zero);
            for(int i=0;i<count;i++)
            {
                Vector3 tangent=positions[Mathf.Min(i+1,count-1)]-positions[Mathf.Max(0,i-1)];
                Vector3 side=Vector3.Cross(Vector3.up,tangent).normalized*.105f;
                vertices[i*2]=positions[i]-side;vertices[i*2+1]=positions[i]+side;
                uv[i*2]=new Vector2(i/(float)(count-1),0);uv[i*2+1]=new Vector2(i/(float)(count-1),1);
                colors[i*2]=colors[i*2+1]=color;
                if(i<count-1){int v=i*2,k=i*6;triangles[k]=v;triangles[k+1]=v+2;triangles[k+2]=v+1;triangles[k+3]=v+1;triangles[k+4]=v+2;triangles[k+5]=v+3;}
            }
            var mesh=new Mesh {name="Magic energy conduit"};mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();_meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        // Одна траектория используется камнями, светом и редакторной расчисткой травы.
        public Vector3 PathSocket(Transform altar) => altar==FireAltar?FirePathSocket:altar==IceAltar?IcePathSocket:altar==AlchemyAltar?AlchemyPathSocket:EarthPathSocket;
        public Vector3 ConduitPoint(Transform altar,float t)
        {
            int index=altar==FireAltar?0:altar==IceAltar?1:altar==AlchemyAltar?2:3;
            Vector3 toward=altar.position-Centre.position;toward.y=0;toward.Normalize();
            Vector3 socket=PathSocket(altar);
            Vector3 start=PathStarts!=null && PathStarts.Length>index && PathStarts[index]!=null?PathStarts[index].position:altar.TransformPoint(socket);
            Vector3 end=PathEnds!=null && PathEnds.Length>index && PathEnds[index]!=null?PathEnds[index].position:Centre.position+toward*1.4f;
            Vector3 outward=altar.TransformDirection(new Vector3(socket.x,0,socket.z).normalized);
            if(altar==EarthAltar)
            {
                // У земли лестница повёрнута поперёк линии к центру. Сначала выходим за боковые опоры.
                Vector3 toe=EarthToe!=null?EarthToe.position:altar.TransformPoint(new Vector3(socket.x,socket.y,.39f));
                if(t<.2f)return Vector3.Lerp(start,toe,t/.2f);
                t=(t-.2f)/.8f;
                Vector3 bend=EarthBend!=null?EarthBend.position:toe+altar.forward*.15f;
                return (1-t)*(1-t)*toe+2*(1-t)*t*bend+t*t*end;
            }
            Vector3 control=Vector3.Lerp(start,end,.42f)+outward*.32f;
            return (1-t)*(1-t)*start+2*(1-t)*t*control+t*t*end;
        }
        void BuildCrystalCluster()
        {
            var source=Heart.GetComponent<MeshFilter>();var renderer=Heart.GetComponent<MeshRenderer>();
            if(source==null || renderer==null)return;
            Vector3[] offsets={new Vector3(-.52f,0,.1f),new Vector3(.43f,0,.29f),new Vector3(.2f,0,-.48f)};
            float[] heights={.8f,.65f,.5f};
            for(int i=0;i<offsets.Length;i++)
            {
                var go=Child("Осколок сердца "+i,Centre.position+offsets[i]+Vector3.up*.59f);
                go.transform.rotation=Quaternion.Euler(i==1?18:-12,i*117, i==0?20:-16);
                float scale=heights[i]/source.sharedMesh.bounds.size.y;go.transform.localScale*=scale;
                go.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;go.AddComponent<MeshRenderer>().sharedMaterial=renderer.sharedMaterial;
            }
        }

        // Площадка и расчистка используют одинаковую границу между четырьмя алтарями.
        public float PlazaEdge(Vector3 point)
        {
            Transform[] corners={FireAltar,IceAltar,EarthAltar,AlchemyAltar};
            bool inside=false;float distance=float.MaxValue;Vector2 p=new Vector2(point.x,point.z);
            for(int i=0,j=corners.Length-1;i<corners.Length;j=i++)
            {
                if(corners[i]==null || corners[j]==null)return -100;
                Vector2 a=new Vector2(corners[i].position.x,corners[i].position.z),b=new Vector2(corners[j].position.x,corners[j].position.z);
                Vector2 ab=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));
                distance=Mathf.Min(distance,Vector2.Distance(p,a+ab*t));
                if((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside?distance:-distance;
        }
        static float StoneNoise(int x,int z,int seed)
        {
            uint n=unchecked((uint)(x*374761393+z*668265263+seed*1442695041));n=(n^(n>>13))*1274126177u;
            return (n^(n>>16))%65536/65535f;
        }
        Vector2 StoneSite(int x,int z) => new Vector2(x*.79f+(StoneNoise(x,z,1)-.5f)*.42f,z*.79f+(StoneNoise(x,z,2)-.5f)*.42f);
        Material[] StoneMaterials()
        {
            if(_stoneMaterials!=null && _stoneMaterials.Length==3 && _stoneMaterials[0]!=null && _stoneMaterials[1]!=null && _stoneMaterials[2]!=null)return _stoneMaterials;
            if(StoneMaterialAssets!=null && StoneMaterialAssets.Length==3 && StoneMaterialAssets[0]!=null && StoneMaterialAssets[1]!=null && StoneMaterialAssets[2]!=null)
                return _stoneMaterials=StoneMaterialAssets;
            _stoneMaterials=new Material[3];
            for(int i=0;i<3;i++)
            {
                var mat=new Material(PavingMaterial);mat.name="Камень школы "+i;
                mat.SetTexture("_BaseMap",PavingTexture);mat.SetColor("_BaseColor",new Color(.93f,.98f,1.05f)*(.94f+i*.055f));
                _stoneMaterials[i]=mat;_materials.Add(mat);
            }
            StoneMaterialAssets=_stoneMaterials;
            return _stoneMaterials;
        }
        static Vector2 StoneUv(Vector3 p,Vector3 center,int seed)
        {
            int patch=Mathf.Abs(seed)%3;Vector2 origin=patch==0?new Vector2(.315f,.786f):patch==1?new Vector2(.79f,.777f):new Vector2(.291f,.645f);
            float angle=seed*2.39996f;Vector2 local=new Vector2(p.x-center.x,p.z-center.z);
            return origin+new Vector2(local.x*Mathf.Cos(angle)-local.y*Mathf.Sin(angle),local.x*Mathf.Sin(angle)+local.y*Mathf.Cos(angle))*.105f;
        }
        public void BuildPlazaOnly()
        {
            _decoration=DecorationRoot.gameObject;RefreshGround();
            PlazaRoot=Group("Площадка — отдельные плиты");_buildParent=PlazaRoot;BuildPlaza();_buildParent=null;
        }
        void BuildPlaza()
        {
            if(PavingMaterial==null)return;
            var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new[]{new List<int>(),new List<int>(),new List<int>()};
            var paths=new List<Vector3>();
            foreach(var altar in new[]{FireAltar,IceAltar,AlchemyAltar,EarthAltar})
                if(altar!=null)for(int i=0;i<=32;i++)paths.Add(ConduitPoint(altar,i/32f));
            int count=0;
            for(int z=-10;z<=10;z++)for(int x=-10;x<=10;x++)
            {
                Vector2 site=StoneSite(x,z);Vector3 at=Centre.position+new Vector3(site.x,0,site.y);
                float edge=PlazaEdge(at),random=StoneNoise(x,z,3);
                if(edge<-.25f || (edge<.25f && random<.18f))continue;
                float pathDistance=float.MaxValue;
                foreach(var p in paths)pathDistance=Mathf.Min(pathDistance,new Vector2(p.x-at.x,p.z-at.z).magnitude);
                if(pathDistance<.53f)continue;
                at.y=SurfaceHeight(at)+.008f;if(at.y>.27f)continue;
                var polygon=new List<Vector2>{site+new Vector2(-1,-1),site+new Vector2(1,-1),site+new Vector2(1,1),site+new Vector2(-1,1)};
                for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
                {
                    if(dx==0 && dz==0)continue;
                    Vector2 other=StoneSite(x+dx,z+dz),normal=other-site;float limit=(other.sqrMagnitude-site.sqrMagnitude)*.5f;
                    var clipped=new List<Vector2>();
                    for(int i=0;i<polygon.Count;i++)
                    {
                        Vector2 a=polygon[i],b=polygon[(i+1)%polygon.Count];float da=Vector2.Dot(a,normal)-limit,db=Vector2.Dot(b,normal)-limit;
                        if(da<=0)clipped.Add(a);
                        if((da<=0)!=(db<=0))clipped.Add(Vector2.Lerp(a,b,da/(da-db)));
                    }
                    polygon=clipped;
                }
                if(polygon.Count<3)continue;
                var chipped=new List<Vector2>();
                for(int k=0;k<polygon.Count;k++)
                {
                    Vector2 p=polygon[k];float cut=.09f+StoneNoise(x+k,z,5)*.13f;
                    chipped.Add(Vector2.Lerp(p,polygon[(k+polygon.Count-1)%polygon.Count],cut));
                    chipped.Add(Vector2.Lerp(p,polygon[(k+1)%polygon.Count],cut));
                }
                polygon=chipped;
                var ring=new Vector3[polygon.Count];bool blocked=false;
                for(int k=0;k<ring.Length;k++)
                {
                    Vector2 p=Vector2.Lerp(site,polygon[k],.97f);
                    ring[k]=Centre.position+new Vector3(p.x,0,p.y);ring[k].y=SurfaceHeight(ring[k])+.005f;
                    if(ring[k].y>.29f)blocked=true;
                }
                if(blocked)continue;
                var indices=triangles[Mathf.Min(2,Mathf.FloorToInt(StoneNoise(x,z,4)*3))];
                int firstVertex=vertices.Count;
                Vector3 top=at+Vector3.up*(.055f+random*.02f);
                void Tri(Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);uvs.Add(StoneUv(a,at,x+z*31));uvs.Add(StoneUv(b,at,x+z*31));uvs.Add(StoneUv(c,at,x+z*31));indices.Add(n);indices.Add(n+1);indices.Add(n+2);}
                for(int k=0;k<ring.Length;k++)
                {
                    int next=(k+1)%ring.Length;Vector3 a=Vector3.Lerp(ring[k],top,.08f)+Vector3.up*.028f,b=Vector3.Lerp(ring[next],top,.08f)+Vector3.up*.028f;
                    Tri(ring[next],ring[k],a);Tri(ring[next],a,b);Tri(b,a,top);
                }
                int materialIndex=Mathf.Min(2,Mathf.FloorToInt(StoneNoise(x,z,4)*3));
                EditableStone($"Плита {x} {z}",at,vertices,uvs,firstVertex,StoneMaterials()[materialIndex]);
                count++;
            }
            Debug.Log($"[camp-magic] courtyard stones={count} vertices={vertices.Count}");
        }
        void EditableStone(string name,Vector3 pivot,List<Vector3> vertices,List<Vector2> uvs,int first,Material material)
        {
            int count=vertices.Count-first;var points=new Vector3[count];var coordinates=new Vector2[count];var triangles=new int[count];
            for(int i=0;i<count;i++){points[i]=vertices[first+i]-pivot;coordinates[i]=uvs[first+i];triangles[i]=i;}
            var mesh=new Mesh{name=name};mesh.vertices=points;mesh.uv=coordinates;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();_meshes.Add(mesh);
            var go=Child(name,pivot);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<CampMagicStone>();
        }
        void BuildPaving(Vector3[] path,float length,int seed)
        {
            if(PavingMaterial==null)return;
            var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
            int steps=Mathf.Max(2,Mathf.CeilToInt(length/.43f));
            for(int step=0;step<=steps;step++)
            {
                float f=step/(float)steps*(path.Length-1);int idx=Mathf.Min(Mathf.FloorToInt(f),path.Length-2);
                Vector3 center=Vector3.Lerp(path[idx],path[idx+1],f-idx);
                Vector3 along=(path[idx+1]-path[idx]).normalized;along.y=0;along.Normalize();Vector3 side=Vector3.Cross(along,Vector3.up);
                for(int row=-1;row<=1;row+=2)
                {
                    Vector3 at=center+side*(row*.29f)+along*(row*.055f);at.y=SurfaceHeight(at)+.008f;
                    if(at.y>.18f)continue;
                    float radius=.22f+.025f*Mathf.Sin(step*5.2f+row+seed*2.3f),height=.065f+.012f*Mathf.Cos(step*2.7f+seed);
                    var ring=new Vector3[7];var rim=new Vector3[7];Vector3 top=at+Vector3.up*height;
                    int firstVertex=vertices.Count;
                    for(int k=0;k<7;k++)
                    {
                        float a=k*Mathf.PI*2/7+.16f*Mathf.Sin(step+seed);float variation=.92f+.08f*Mathf.Sin(k*7.13f+step);
                        ring[k]=at+(along*Mathf.Cos(a)*1.05f+side*Mathf.Sin(a))*radius*variation;
                        rim[k]=Vector3.Lerp(ring[k],top,.18f)+Vector3.up*height*.65f;
                    }
                    void Tri(Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);uvs.Add(StoneUv(a,at,step+seed*31));uvs.Add(StoneUv(b,at,step+seed*31));uvs.Add(StoneUv(c,at,step+seed*31));triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);}
                    for(int k=0;k<7;k++){int next=(k+1)%7;Tri(ring[next],ring[k],rim[k]);Tri(ring[next],rim[k],rim[next]);Tri(rim[next],rim[k],top);}
                    EditableStone($"Камень {step:00} {(row<0?"Л":"П")}",at,vertices,uvs,firstVertex,StoneMaterials()[1]);
                }
            }
        }
        public float HeightAt(Vector3 point)=>SurfaceHeight(point);
        float SurfaceHeight(Vector3 p)
        {
            float height=Physics.Raycast(new Vector3(p.x,6,p.z),Vector3.down,out var hit,8,~0,QueryTriggerInteraction.Ignore)?hit.point.y:0;
            // Рельеф видимой земли выше плоской навигации. Поток должен лежать на видимых треугольниках.
            if(_groundTransform==null || _groundVertices==null)return height;
            Vector3 local=_groundTransform.InverseTransformPoint(p);
            float u=(local.x-_groundBounds.min.x)/_groundBounds.size.x;
            float v=(local.z-_groundBounds.min.z)/_groundBounds.size.z;
            if(u<0 || u>1 || v<0 || v>1)return height;
            float gx=u*(_groundSide-1),gz=v*(_groundSide-1);
            int x=Mathf.Min(Mathf.FloorToInt(gx),_groundSide-2),z=Mathf.Min(Mathf.FloorToInt(gz),_groundSide-2);float tx=gx-x,tz=gz-z;
            int i=z*_groundSide+x;
            float a=_groundVertices[i].y,b=_groundVertices[i+1].y,c=_groundVertices[i+_groundSide].y,d=_groundVertices[i+_groundSide+1].y;
            float y=tx+tz<=1?a+tx*(b-a)+tz*(c-a):d+(1-tx)*(c-d)+(1-tz)*(b-d);
            return Mathf.Max(height,_groundTransform.TransformPoint(new Vector3(local.x,y,local.z)).y);
        }
        void OnDestroy()
        {
            if(_animationInitialized && Heart!=null){Heart.localPosition=_heartRest;Heart.localRotation=_heartRotation;}
            if(!_ownsDecoration)return;
            foreach(var mesh in _meshes)if(mesh!=null)Destroy(mesh);
            foreach(var material in _materials)if(material!=null)Destroy(material);
            if(_decoration!=null)Destroy(_decoration);
        }
    }
}
