using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>Бутылки, короткий Отбой и настоящие огненные лужи; все сроки приходят из Sim.</summary>
    [DefaultExecutionOrder(1040)]
    public sealed partial class PelagOrdnanceView : MonoBehaviour
    {
        private TickDriver _driver;
        private LayoutView _layout;
        private CombatJuiceView _juice;
        private Simulation _shown;
        private Transform _hand, _bottle;
        private Transform _worldFireRoot;
        private Material _material;
        private MaterialPropertyBlock _properties;
        private readonly MeshRenderer[] _pools = new MeshRenderer[8];
        private int _serial = -1, _releaseTick, _landTick;
        private Vector3 _launch, _target;
        private bool _released;

        private void Start()
        {
            _properties = new MaterialPropertyBlock();
            _worldFireRoot = new GameObject("Pelag pooled ordnance effects").transform;
            _driver = FindAnyObjectByType<TickDriver>(); _layout = FindAnyObjectByType<LayoutView>();
            _juice = _driver != null ? _driver.GetComponent<CombatJuiceView>() : null;
            foreach (var bone in GetComponentsInChildren<Transform>(true))
                if (bone.name == "mixamorig:LeftHand") { _hand = bone; break; }
            var prefab = Resources.Load<GameObject>("Weapons/Pelag/BlazeBottle/Pelag_BlazeBottle");
            if (prefab != null) { _bottle = Instantiate(prefab, transform).transform; _bottle.gameObject.SetActive(false); }
            _material = new Material(Shader.Find("Razlom/Pelag Fire Splash"));
            for (int i=0;i<_pools.Length;i++) _pools[i] = Make("Pelag burning oil " + i);
            PrepareFireParticles();
            PrepareAuthoredFire();
        }
        private MeshRenderer Make(string name)
        {
            var obj=new GameObject(name); obj.transform.SetParent(_worldFireRoot,false);
            obj.AddComponent<MeshFilter>();
            var r=obj.AddComponent<MeshRenderer>();r.sharedMaterial=_material;r.shadowCastingMode=ShadowCastingMode.Off;
            r.receiveShadows=false;r.enabled=false;return r;
        }
        private Vector3 Ground(FixVec2 p)
            => new Vector3(p.X.ToFloat(),(_layout!=null?_layout.WeaponGroundHeight(p.X.ToFloat(),p.Y.ToFloat()):transform.position.y)+.045f,p.Y.ToFloat());
        private void LateUpdate()
        {
            var sim=_driver!=null?_driver.Sim:null;
            if (sim==null || _material==null) return;
            if (!ReferenceEquals(sim,_shown)) { Clear();_shown=sim; }
            if (_driver.GameplayPaused) return;
            float tick=sim.Tick-1+_driver.Alpha;
            var action=sim.PlayerAction;
            bool flask=action.DefinitionId==AbilityDefinition.FireFlaskId && sim.FlaskInFlight;
            bool back=action.DefinitionId==AbilityDefinition.BackblastId && !action.Interrupted
                && tick<action.ContactTick && tick>=action.StartTick;
            if ((flask||back) && action.Serial!=_serial)
            {
                _serial=action.Serial;_released=false;
                _releaseTick=flask?sim.FlaskReleaseTick:action.StartTick+1;
                _landTick=flask?sim.FlaskLandTick:action.ContactTick;
                _target=Ground(flask?sim.FlaskTarget:sim.MobilityOrigin);
            }
            if (_bottle!=null)
            {
                bool visible=(flask||back)&&!action.Interrupted&&sim.Entities.Alive[0];
                _bottle.gameObject.SetActive(visible);
                if (visible)
                {
                    if (!_released)
                    {
                        _launch=_hand!=null?_hand.TransformPoint(new Vector3(0,.066f,.053f)):transform.position+Vector3.up;
                        _bottle.SetPositionAndRotation(_launch,(_hand!=null?_hand.rotation:transform.rotation)*Quaternion.Euler(0,0,-90));
                        if (tick>=_releaseTick) _released=true;
                    }
                    if (_released)
                    {
                        float u=Mathf.InverseLerp(_releaseTick,_landTick,tick);
                        _bottle.position=Vector3.Lerp(_launch,_target,u)+Vector3.up*(flask?1.05f:0f)*4*u*(1-u);
                        _bottle.rotation=Quaternion.Euler(u*230,action.Serial*47,u*160);
                    }
                    _bottle.localScale=Vector3.one*.8f;
                }
            }
            foreach (var context in _driver.FrameEventContexts)
            {
                var e=context.Event;
                if (e.Source!=Simulation.PlayerId || (e.Type!=SimEventType.FlaskBurst && e.Type!=SimEventType.BackblastBurst)) continue;
                Vector3 position=Ground(e.Position);
                float radius=e.Type==SimEventType.BackblastBurst?1.5f:1.25f;
                if (!CaptureRig.NoVfx)
                {
                    BurstAuthoredFire(position, radius, context.SimulationTick);
                    EmitExplosion(position, radius, context.SimulationTick);
                    _juice?.PunchCamera(.19f,.025f);
                }
            }
            for (int i=0;i<_pools.Length;i++)
            {
                bool active=!CaptureRig.NoVfx&&sim.FirePoolActive(i);_pools[i].enabled=active;if(!active)continue;
                _properties.Clear();_properties.SetFloat("_Age",tick/Simulation.TicksPerSecond);_properties.SetFloat("_Mode",1);
                _properties.SetFloat("_Seed",i*1.731f);_pools[i].SetPropertyBlock(_properties);
            }
            if (!CaptureRig.NoVfx) UpdateAuthoredFire(sim, tick);
        }
        private void Clear()
        {
            _serial=-1;_released=false;
            foreach(var r in _pools)if(r!=null)r.enabled=false;
            if(_bottle!=null)_bottle.gameObject.SetActive(false);
            ClearFireParticles();
            ClearAuthoredFire();
        }
        private void OnDisable()=>Clear();
        private void OnDestroy(){if(_material!=null)Destroy(_material);
            if(_glassMesh!=null)Destroy(_glassMesh);if(_glassMaterial!=null)Destroy(_glassMaterial);
            foreach (var mesh in _oilMeshes) if (mesh!=null) Destroy(mesh);
            if (_worldFireRoot!=null) Destroy(_worldFireRoot.gameObject);}
    }
}
