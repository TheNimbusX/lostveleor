using UnityEngine;

namespace Game.View
{
    /// <summary>Звуки мест лагеря, с расстоянием от героя и плавным входом в общий микс.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Звуковое окружение")]
    public sealed class CampSoundscape : MonoBehaviour
    {
        public enum Place { Forest, Fire, River, Cauldron, Smith, Music }
        [System.Serializable]
        public sealed class Layer
        {
            public Place Place;
            public AudioSource Source;
            public Transform Anchor;
            [Range(0,1)] public float Gain=.5f;
            [Min(0)] public float Near=2;
            [Min(.1f)] public float Far=12;
            [System.NonSerialized] public float Distance, AppliedGain;
            [System.NonSerialized] internal float Pan;
        }
        public GameObject CampRoot;
        public CampRiver River;
        public Layer[] Layers;
        [Header("Плавность и паузы")]
        [Min(.1f)] public float FadeIn=2.3f, FadeOut=.75f;
        public Vector2 SmithRestSeconds=new Vector2(6,14);
        [Range(0,1)] public float MenuDuck=.55f;
        float _blend, _nextSmith, _spatialClock, _clock;
        bool _wasAudible;
        Vector3 _listener;
        Camera _camera;
        PauseMenu _pause;
        System.Random _variation;
        public float CurrentBlend=>_blend;

        void Awake()
        {
            _variation=new System.Random(72913);
            _camera=Camera.main;
            _pause=FindAnyObjectByType<PauseMenu>();
            if(Layers==null)Layers=System.Array.Empty<Layer>();
            foreach(var layer in Layers)
            {
                if(layer.Source==null)continue;
                // Камера находится высоко над уровнем: расстояние и панораму считаем от Пелага.
                layer.Source.spatialBlend=0;
                layer.Source.playOnAwake=false;
                layer.Source.dopplerLevel=0;
                layer.Source.volume=0;
                layer.Source.loop=layer.Place!=Place.Smith;
            }
        }
        void Update()
        {
            float dt=CombatAudioCapture.Recording?Time.deltaTime:Time.unscaledDeltaTime;
            _clock+=dt;
            var player=CampPlayerView.Instance;
            bool audible=CampRoot!=null && CampRoot.activeInHierarchy && !MainMenuView.IsOpen && player!=null && player.Active;
            if(audible)
            {
                _listener=player.Position;
                if(!_wasAudible)
                {
                    _nextSmith=_clock+2.5f;
                    foreach(var layer in Layers)
                    {
                        var source=layer.Source;
                        if(source==null || source.clip==null || source.isPlaying || layer.Place==Place.Smith)continue;
                        if(layer.Place!=Place.Music)source.time=(float)_variation.NextDouble()*Mathf.Max(0,source.clip.length-1);
                        source.Play();
                    }
                }
            }
            _wasAudible=audible;
            _blend=Mathf.MoveTowards(_blend,audible?1:0,dt/(audible?FadeIn:FadeOut));
            _spatialClock-=dt;
            if(_spatialClock<=0){_spatialClock=.05f;RefreshDistances();}
            bool panel=player!=null && (player.InventoryOpen || player.EntranceOpen);
            float duck=panel || (_pause!=null && _pause.IsOpen) ? MenuDuck : 1;
            foreach(var layer in Layers)
            {
                var source=layer.Source;
                if(source==null || source.clip==null)continue;
                bool global=layer.Place==Place.Forest || layer.Place==Place.Music;
                float attenuation=global?1:1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(layer.Near,layer.Far,layer.Distance));
                float settings=layer.Place==Place.Music?GameUserSettings.MusicGain:GameUserSettings.EffectsVolume;
                float wanted=layer.Gain*attenuation*_blend*duck*settings;
                layer.AppliedGain=Mathf.Lerp(layer.AppliedGain,wanted,1-Mathf.Exp(-dt*7));
                source.volume=layer.AppliedGain;
                source.panStereo=Mathf.Lerp(source.panStereo,layer.Pan,1-Mathf.Exp(-dt*4));
                if(layer.Place==Place.Smith && audible && !source.isPlaying && _clock>=_nextSmith && attenuation>.08f)
                {
                    source.pitch=.98f+(float)_variation.NextDouble()*.04f;
                    source.Play();
                    _nextSmith=_clock+source.clip.length/source.pitch+Mathf.Lerp(SmithRestSeconds.x,SmithRestSeconds.y,(float)_variation.NextDouble());
                }
                if(!audible && _blend<=0 && layer.AppliedGain<.0005f && source.isPlaying)source.Stop();
            }
        }
        void RefreshDistances()
        {
            if(_camera==null)_camera=Camera.main;
            foreach(var layer in Layers)
            {
                bool global=layer.Place==Place.Forest || layer.Place==Place.Music;
                Vector3 position=layer.Anchor!=null?layer.Anchor.position:_listener;
                if(layer.Place==Place.River && River!=null)position=ClosestRiver(_listener);
                Vector3 delta=position-_listener;delta.y=0;
                layer.Distance=delta.magnitude;
                layer.Pan=global || _camera==null?0:Mathf.Clamp(Vector3.Dot(delta,_camera.transform.right)/12,-.58f,.58f);
            }
        }
        Vector3 ClosestRiver(Vector3 listener)
        {
            var points=River.HasBakedShape?River.BakedCentres:River.Contour;
            if(points==null || points.Length<2)return River.transform.position;
            Vector3 local=River.transform.InverseTransformPoint(listener);
            Vector2 p=new Vector2(local.x,local.z),nearest=points[0];float best=float.MaxValue;
            for(int i=1;i<points.Length;i++)
            {
                Vector2 a=points[i-1],delta=points[i]-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
                Vector2 candidate=a+delta*t;float d=(p-candidate).sqrMagnitude;
                if(d<best){best=d;nearest=candidate;}
            }
            return River.transform.TransformPoint(new Vector3(nearest.x,0,nearest.y));
        }
        void OnDisable()
        {
            _blend=0;_wasAudible=false;
            if(Layers==null)return;
            foreach(var layer in Layers)if(layer.Source!=null){layer.Source.Stop();layer.Source.volume=0;layer.AppliedGain=0;}
        }
    }
}
