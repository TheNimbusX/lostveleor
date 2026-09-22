using UnityEngine;

namespace Game.View
{
    public enum CampServiceKind { Smith, Trader, Alchemist, Tent }
    public sealed class CampServiceNpc : MonoBehaviour
    {
        public CampServiceKind Kind;
        public Vector3 ApproachOffset = new Vector3(0,0,-1.3f);
        public float Reach = 1.8f;
        Renderer[] _renderers;
        MaterialPropertyBlock[] _original;
        bool _highlighted;
        public Vector3 Approach => transform.position+ApproachOffset;
        public string Title => CampServiceText.Get("npc."+Kind.ToString().ToLowerInvariant());
        public Bounds Shape
        {
            get { Cache();var bounds=new Bounds(transform.position+Vector3.up*.8f,new Vector3(.6f,1.6f,.6f));foreach(var r in _renderers)if(r!=null)bounds.Encapsulate(r.bounds);return bounds; }
        }
        public float Distance(Vector3 p) { var d=(Kind==CampServiceKind.Tent?Shape.ClosestPoint(p):transform.position)-p;d.y=0;return d.magnitude; }
        public bool Near(Vector3 p) => Distance(p)<=Reach;
        void Cache() { if(_renderers!=null)return;_renderers=GetComponentsInChildren<Renderer>();_original=new MaterialPropertyBlock[_renderers.Length];for(int i=0;i<_renderers.Length;i++){_original[i]=new MaterialPropertyBlock();_renderers[i].GetPropertyBlock(_original[i]);} }
        public void Highlight(bool value)
        {
            if(_highlighted==value)return;Cache();_highlighted=value;
            for(int i=0;i<_renderers.Length;i++)
            {
                var r=_renderers[i];if(r==null)continue;
                if(!value){r.SetPropertyBlock(_original[i]);continue;}
                var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);
                if(r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_BaseColor"))block.SetColor("_BaseColor",r.sharedMaterial.GetColor("_BaseColor")*new Color(1.18f,1.14f,1.05f,1));
                else if(r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_Color"))block.SetColor("_Color",r.sharedMaterial.GetColor("_Color")*new Color(1.18f,1.14f,1.05f,1));
                r.SetPropertyBlock(block);
            }
        }
        void OnDisable()=>Highlight(false);
    }
}
