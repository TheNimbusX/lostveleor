using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace Game.View
{
    // Локальное исключение из границы лагеря: мост и только площадка нового сервиса.
    public sealed class CampRiverPassage : MonoBehaviour
    {
        public Transform Bridge;
        public Vector3 CrossingCentre = new Vector3(2.82f,0,-21.53f);
        public Vector2 CrossingSize = new Vector2(1.9f,8.8f);
        public Bounds FarBank = new Bounds(new Vector3(-1,0,-27.5f),new Vector3(14,30,9));
        [System.NonSerialized] float[] _deckHeights;
        [System.NonSerialized] Bounds _deckBounds;
        public float SurfaceHeight(float x,float z,float ground)
        {
            if(Bridge==null)Bridge=GameObject.Find("CreatingBridge")?.transform;
            if(Bridge==null)return ground;
            if(_deckHeights==null)SampleDeck(ground);
            if(Mathf.Abs(x-_deckBounds.center.x)>CrossingSize.x*.5f+.15f)return ground;
            float edgeDistance=Mathf.Min(z-_deckBounds.min.z,_deckBounds.max.z-z);
            if(edgeDistance<-.5f)return ground;
            float t=Mathf.Clamp01((z-_deckBounds.min.z)/_deckBounds.size.z)*(_deckHeights.Length-1);
            int index=Mathf.Min((int)t,_deckHeights.Length-2);
            float deck=Mathf.Lerp(_deckHeights[index],_deckHeights[index+1],t-index);
            // Подход поднимается перед первой доской; на самом настиле стопы уже на поверхности.
            return Mathf.Lerp(ground,Mathf.Max(ground,deck+.025f),Mathf.SmoothStep(0,1,(edgeDistance+.5f)/.5f));
        }
        void SampleDeck(float ground)
        {
            _deckBounds=BridgeBounds;_deckHeights=new float[65];
            var probes=new List<MeshCollider>();
            foreach(var filter in Bridge.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null)continue;
                var proxy=new GameObject("Bridge surface probe");proxy.layer=2;
                proxy.transform.SetParent(filter.transform,false);
                var collider=proxy.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;probes.Add(collider);
            }
            Physics.SyncTransforms();
            for(int i=0;i<_deckHeights.Length;i++)
            {
                float z=Mathf.Lerp(_deckBounds.min.z+.04f,_deckBounds.max.z-.04f,(float)i/(_deckHeights.Length-1));
                float height=ground;
                foreach(var collider in probes)
                for(int lane=-1;lane<=1;lane++)
                {
                    var ray=new Ray(new Vector3(_deckBounds.center.x+lane*.25f,_deckBounds.max.y+1,z),Vector3.down);
                    if(collider.Raycast(ray,out var hit,_deckBounds.size.y+3))height=Mathf.Max(height,hit.point.y);
                }
                _deckHeights[i]=height;
            }
            // Узкие щели между досками не должны заставлять тело проседать.
            var sampled=(float[])_deckHeights.Clone();
            for(int i=0;i<_deckHeights.Length;i++)
                _deckHeights[i]=Mathf.Max(sampled[i],sampled[Mathf.Max(0,i-1)],sampled[Mathf.Min(sampled.Length-1,i+1)]);
            foreach(var collider in probes){collider.enabled=false;if(Application.isPlaying)Destroy(collider.gameObject);else DestroyImmediate(collider.gameObject);}
        }
        public Bounds BridgeBounds
        {
            get
            {
                var centre=Bridge!=null?Bridge.position:CrossingCentre;
                var bounds=new Bounds(centre,new Vector3(3.26f,2,7.36f));
                if(Bridge==null)return bounds;
                var renderers=Bridge.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0)return bounds;
                bounds=renderers[0].bounds;
                for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }
        }
        public void AddRailingSources(List<NavMeshBuildSource> sources)
        {
            // Берег за концами моста открыт, поэтому одной маски воды недостаточно:
            // перила перекрываем на всей длине модели, оставляя оба входа свободными.
            Bounds bounds=BridgeBounds;
            float centreX=Bridge!=null?Bridge.position.x:CrossingCentre.x;
            float inner=CrossingSize.x*.5f;
            for(int side=-1;side<=1;side+=2)
            {
                float outer=side<0?centreX-bounds.min.x:bounds.max.x-centreX;
                float width=Mathf.Max(.15f,outer-inner);
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,
                    transform=Matrix4x4.Translate(new Vector3(centreX+side*(inner+width*.5f),bounds.center.y,bounds.center.z)),
                    size=new Vector3(width,30,bounds.size.z)});
            }
        }
        public bool IsOpen(CampRiver river, Vector3 world)
        {
            Vector3 centre=Bridge!=null?Bridge.position:CrossingCentre;
            // Модель моста ориентирована вдоль мировой Z; размеры задаются в метрах настила.
            if(Mathf.Abs(world.x-centre.x)<CrossingSize.x*.5f && Mathf.Abs(world.z-centre.z)<CrossingSize.y*.5f)return true;
            Vector3 p=river.transform.InverseTransformPoint(world);
            return FarBank.Contains(new Vector3(world.x,FarBank.center.y,world.z)) && p.z<river.CentreAt(p.x)-river.Width*.5f-river.BankWidth*.55f;
        }
    }
}
