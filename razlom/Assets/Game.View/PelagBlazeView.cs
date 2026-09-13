using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Контакт бутылки и клинка поверх общего жеста, включая применение на бегу.</summary>
    [DefaultExecutionOrder(1015)]
    public sealed class PelagBlazeView : MonoBehaviour
    {
        private TickDriver _driver;
        private Transform _left, _right, _hips, _bladeRoot, _bladeTip;
        private Transform _bottle, _mouth, _stopper;
        private readonly Transform[,] _fingers = new Transform[5, 3];
        private LineRenderer _pour;
        private ParticleSystem _drops;
        private PelagBladeFireView _fire;
        private SkinnedMeshRenderer[] _skin;
        private int[] _skinSlots;
        private MaterialPropertyBlock _block;
        private Light _light;
        private float _glow;
        private int _droppedCast = -1;
        private const float BottleScale = .8f;
        private const float GlowStrength = .28f;
        private static readonly Quaternion BottleGrip = Quaternion.Euler(0,0,-90);
        private static readonly Vector3 BottleOffset = new Vector3(0, .066f, .053f);
        private static readonly int GlowId = Shader.PropertyToID("_BlazeGlow");
        public float PourStreamLength { get; private set; }
        public float GestureTime { get; private set; }
        public bool BottleVisible => _bottle != null && _bottle.gameObject.activeSelf;
        public float Glow => _glow;

        private void Start()
        {
            _block=new MaterialPropertyBlock();
            _driver=FindAnyObjectByType<TickDriver>();
            foreach(var t in GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="mixamorig:LeftHand")_left=t;
                if(t.name=="mixamorig:RightHand")_right=t;
                if(t.name=="mixamorig:Hips")_hips=t;
                if(t.name=="BladeRoot")_bladeRoot=t;
                if(t.name=="BladeTip")_bladeTip=t;
            }
            _skin=GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _skinSlots = new int[_skin.Length];
            for (int i = 0; i < _skin.Length; i++) _skinSlots[i] = _skin[i].sharedMaterials.Length;
            var prefab=Resources.Load<GameObject>("Weapons/Pelag/BlazeBottle/Pelag_BlazeBottle");
            if(prefab==null || _left==null || _right==null || _bladeRoot==null)return;
            _bottle=Instantiate(prefab,_left,false).transform;
            _bottle.localPosition=BottleOffset;_bottle.localRotation=BottleGrip;
            _bottle.localScale=Vector3.one*BottleScale;
            string[] fingerNames = { "Index", "Middle", "Ring", "Pinky", "Thumb" };
            for (int finger = 0; finger < fingerNames.Length; finger++)
            {
                Transform parent = _left;
                for (int joint = 0; joint < 3; joint++)
                {
                    if (parent == null) break;
                    parent = parent.Find("mixamorig:LeftHand" + fingerNames[finger] + (joint + 1));
                    _fingers[finger, joint] = parent;
                }
            }
            _mouth=_bottle.Find("Mouth");_stopper=_bottle.Find("Stopper hinge");
            _bottle.gameObject.SetActive(false);
            var material=Resources.Load<Material>("Weapons/Pelag/BlazeBottle/M_BlazeLiquid");
            var line=new GameObject("Blaze oil stream");line.transform.SetParent(transform,false);
            _pour=line.AddComponent<LineRenderer>();_pour.sharedMaterial=material;_pour.positionCount=4;
            _pour.startWidth=.012f;_pour.endWidth=.005f;_pour.numCapVertices=3;_pour.enabled=false;
            _pour.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;_pour.receiveShadows=false;
            var drops=new GameObject("Blaze burning drops");drops.transform.SetParent(transform,false);
            _drops=drops.AddComponent<ParticleSystem>();_drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=_drops.main;main.loop=false;main.playOnAwake=false;main.maxParticles=12;
            main.startLifetime=.42f;main.startSize=.025f;main.startSpeed=0;main.gravityModifier=.65f;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startColor=new Color(1,.52f,.025f,1);
            var emission=_drops.emission;emission.enabled=false;
            var shape=_drops.shape;shape.enabled=false;
            var size=_drops.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,0));
            var renderer=drops.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=sphere.GetComponent<MeshFilter>().sharedMesh;Destroy(sphere);
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var fire = new GameObject("Arcadia blade fire");
            fire.transform.SetParent(transform, false);
            _fire = fire.AddComponent<PelagBladeFireView>();
            _fire.Initialize(_bladeRoot, _bladeTip);
            var glow=new GameObject("Blaze warm light");glow.transform.SetParent(transform,false);
            _light=glow.AddComponent<Light>();_light.type=LightType.Point;_light.color=new Color(1,.48f,.07f);
            _light.range=1.8f;_light.shadows=LightShadows.None;_light.intensity=0;
        }

        private void LateUpdate()
        {
            var sim=_driver!=null?_driver.Sim:null;
            if(sim==null || _bottle==null)return;
            bool casting=sim.BlazeCasting;
            float t=(sim.Tick-1+_driver.Alpha-sim.BlazeStartTick)/Simulation.TicksPerSecond;
            GestureTime=casting?t:-1;PourStreamLength=0;
            if(casting){PoseHands(t);PoseBottleFingers(t);}
            _bottle.gameObject.SetActive(casting && t>=.32f && t<1.82f);
            if(_stopper!=null)_stopper.localRotation=Quaternion.Euler(0,0,-115*Smooth((t-.46f)/.15f));
            bool pouring=casting && t>=.8f && t<Simulation.BlazeIgnitionDelayTicks/(float)Simulation.TicksPerSecond;
            _pour.enabled=pouring;
            if(pouring)
            {
                Vector3 end=Vector3.Lerp(_bladeRoot.position,_bladeTip.position,Mathf.Lerp(.25f,.75f,Mathf.InverseLerp(.8f,1.2f,t)));
                for(int i=0;i<4;i++)_pour.SetPosition(i,Vector3.Lerp(_mouth.position,end,i/3f)+Vector3.down*(Mathf.Sin(i/3f*Mathf.PI)*.012f));
                PourStreamLength=Vector3.Distance(_mouth.position,end);
            }
            if(casting && t>=1.48f && _droppedCast!=sim.BlazeStartTick && sim.BlazeActive)
            {
                _droppedCast=sim.BlazeStartTick;
                for(int i=0;i<3;i++)
                {
                    var drop=new ParticleSystem.EmitParams {position=Vector3.Lerp(_bladeRoot.position,_bladeTip.position,.6f+i*.13f),
                        velocity=transform.TransformDirection(new Vector3(-.12f+i*.11f,-.3f,.16f+i*.1f))};
                    _drops.Emit(drop,1);
                }
            }
            bool active=sim.BlazeActive;
            _fire.RenderFire(active, sim.Entities.Alive[Simulation.PlayerId]);
            _glow=Mathf.MoveTowards(_glow,active?GlowStrength:0,Time.deltaTime*2f);
            if(!sim.Entities.Alive[Simulation.PlayerId])_glow=0;
            ApplyGlow(_glow);
            _light.transform.position=Vector3.Lerp(_bladeRoot.position,_bladeTip.position,.5f)+Vector3.up*.15f;
            _light.intensity=_glow/GlowStrength*.65f;
        }

        private void PoseHands(float t)
        {
            float hold=Smooth((t-.18f)/.42f)*(1-Smooth((t-1.72f)/.28f));
            Vector3 rightTarget=_hips.position+transform.TransformVector(new Vector3(.18f,.13f,.23f));
            Quaternion rightRot=_right.rotation;
            Vector3 direction=transform.TransformDirection(new Vector3(-.85f,.06f,.42f).normalized);
            float shake=Mathf.InverseLerp(1.42f,1.72f,t);
            direction=Quaternion.AngleAxis(Mathf.Sin(shake*Mathf.PI*4)*Mathf.Sin(shake*Mathf.PI)*9,transform.forward)*direction;
            rightRot=Quaternion.FromToRotation(_bladeTip.position-_bladeRoot.position,direction)*rightRot;
            Solve(_right,Vector3.Lerp(_right.position,rightTarget,hold));
            _right.rotation=Quaternion.Slerp(_right.rotation,rightRot,hold);
            float pour=Smooth((t-.58f)/.22f)*(1-Smooth((t-1.28f)/.22f));
            if(pour<=0)return;
            float along=Mathf.Lerp(.25f,.75f,Mathf.InverseLerp(.8f,1.2f,t));
            Vector3 point=Vector3.Lerp(_bladeRoot.position,_bladeTip.position,along)+transform.up*.055f*transform.lossyScale.y;
            Vector3 up=transform.TransformDirection(new Vector3(0,-.78f,.63f).normalized);
            // Кисть продолжает предплечье; вращение бутылки вокруг оси не заламывает запястье.
            Vector3 pole = transform.TransformDirection(new Vector3(-.8f, -.65f, -.1f));
            float mouthOffset = .0423f * BottleScale * transform.lossyScale.y;
            Vector3 handTarget = point-up*mouthOffset;
            Quaternion handRotation = _left.rotation;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 fingers = Vector3.ProjectOnPlane(handTarget-_left.parent.position, up).normalized;
                if (fingers.sqrMagnitude < .1f) fingers = transform.forward;
                handRotation = Quaternion.LookRotation(Vector3.Cross(up, fingers), fingers);
                handTarget = point-up*mouthOffset
                    -handRotation*Vector3.Scale(_left.lossyScale, BottleOffset);
                Solve(_left,Vector3.Lerp(_left.position,handTarget,pour),pole);
            }
            _left.rotation=Quaternion.Slerp(_left.rotation,handRotation,pour);
        }

        private void PoseBottleFingers(float time)
        {
            float weight = Smooth((time - .2f) / .12f) * (1 - Smooth((time - 1.72f) / .18f));
            if (weight <= 0) return;
            // Фаланги огибают профиль колбы: исходная поза пистолета оставляла ладонь открытой.
            for (int finger = 0; finger < 5; finger++)
                for (int joint = 0; joint < 3; joint++)
                {
                    Transform bone = _fingers[finger, joint];
                    if (bone == null) continue;
                    Transform next = joint < 2 ? _fingers[finger, joint + 1] : null;
                    Vector3 at = _left.InverseTransformPoint(bone.position);
                    float length = next != null
                        ? Vector3.Distance(at, _left.InverseTransformPoint(next.position)) : .015f;
                    float height = (at.x - BottleOffset.x) / BottleScale;
                    float radius = BottleScale * BottleRadius(height) + .006f;
                    Vector2 radial = new Vector2(at.y - BottleOffset.y, at.z - BottleOffset.z);
                    float distance = Mathf.Max(radial.magnitude, .001f);
                    float turn = Mathf.Acos(Mathf.Clamp((distance * distance + radius * radius - length * length)
                        / (2 * distance * radius), -1, 1));
                    float angle = Mathf.Atan2(radial.y, radial.x) + (finger == 4 ? -turn : turn);
                    Vector3 target = new Vector3(at.x, BottleOffset.y + Mathf.Cos(angle) * radius,
                        BottleOffset.z + Mathf.Sin(angle) * radius);
                    Vector3 direction = next != null ? next.position - bone.position : bone.up;
                    Quaternion rotation = Quaternion.FromToRotation(direction, _left.TransformPoint(target) - bone.position) * bone.rotation;
                    bone.rotation = Quaternion.Slerp(bone.rotation, rotation, weight);
                }
        }

        private static float BottleRadius(float height)
        {
            // Поперечные размеры измерены по исходному мешу, а не по его общему габариту.
            if (height < -.02f) return Mathf.Lerp(.055f, .063f, Mathf.InverseLerp(-.04f, -.02f, height));
            if (height < 0) return Mathf.Lerp(.063f, .053f, Mathf.InverseLerp(-.02f, 0, height));
            if (height < .02f) return Mathf.Lerp(.053f, .036f, height / .02f);
            return Mathf.Lerp(.036f, .031f, Mathf.InverseLerp(.02f, .04f, height));
        }

        private static void Solve(Transform hand,Vector3 target, Vector3 pole = default)
        {
            var elbow=hand.parent;var arm=elbow.parent;
            Vector3 origin=arm.position;float a=Vector3.Distance(origin,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
            Vector3 delta=target-origin;float d=Mathf.Clamp(delta.magnitude,.001f,(a+b)*.995f);Vector3 axis=delta.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(pole.sqrMagnitude > .1f ? pole : elbow.position-origin,axis).normalized;
            if(bend.sqrMagnitude<.1f)bend=Vector3.Cross(axis,Vector3.up).normalized;
            float x=(a*a-b*b+d*d)/(2*d);Vector3 joint=origin+axis*x+bend*Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
            arm.rotation=Quaternion.FromToRotation(elbow.position-origin,joint-origin)*arm.rotation;
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,target-elbow.position)*elbow.rotation;
        }
        private static float Smooth(float x)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(x));
        private void ApplyGlow(float value)
        {
            if (_skin == null || _block == null) return;
            for (int i = 0; i < _skin.Length; i++) if (_skin[i] != null)
                for (int slot = 0; slot < _skinSlots[i]; slot++)
                {
                    // ArenaView пишет по слотам материалов: общий блок Renderer был бы перекрыт ими.
                    _skin[i].GetPropertyBlock(_block, slot);
                    _block.SetFloat(GlowId, value);
                    _skin[i].SetPropertyBlock(_block, slot);
                }
        }
        private void OnDisable()
        {
            if(_bottle!=null)_bottle.gameObject.SetActive(false);
            if(_pour!=null)_pour.enabled=false;
            if(_drops!=null)_drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(_fire!=null)_fire.Clear();
            _glow=0;_droppedCast=-1;if(_light!=null)_light.intensity=0;
            ApplyGlow(0);
        }
    }
}
