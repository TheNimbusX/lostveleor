using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public enum CampServiceKind { Smith, Trader, Alchemist, Tent, TravelTable, OathBoard } // числом в сцене — новое только в конец; OathBoard — доска клятв (06.10)
    public sealed class CampServiceNpc : MonoBehaviour
    {
        public CampServiceKind Kind;
        public Vector3 ApproachOffset = new Vector3(0,0,-1.3f);
        public float Reach = 1.8f;
        [Tooltip("Палатка: точка входа. Досягаемость меряется до неё, а не до габаритов модели — иначе палатка открывалась за 5-6 м")]
        public Transform Entrance;

        // НАВЕДЕНИЕ — ТОЛЬКО СНАРУЖИ СИЛУЭТА (06.10, после «обводка поехала внутрь скина»).
        // У жителей уже есть своя обводка 3 px (Texture Toon, _OutlineWidth 3 → UnitOutlineFeature).
        // Голая вывернутая оболочка ложилась внутрь этого кольца, а видны оставались только её
        // внутренние кромки — руки, борода, фартук. Поэтому:
        //  • рендер со своей обводкой — перекрашиваем её в тлеющий янтарь арки (MaterialPropertyBlock);
        //    композит UnitOutline красит только снаружи маски, внутрь тела не попадёт по построению;
        //  • остальное (палатка, стол, доска) — оболочка как у арки, но с трафаретом:
        //    маска → линия только где маски нет → сброс бита (Shaders/CampHoverOutline).
        // Осветление модели владелец отверг 24.09 — его не возвращать.
        const string ShellName = "Контур наведения";
        const int HoverBit = 8;            // пользовательский бит URP [0..3]; бит 0 занят тун-шейдерами
        const float LineWidth = 1.4f;      // как у арки
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color EmberDim = new Color(1f,.70f,.24f,1f), EmberHot = new Color(1f,.84f,.42f,1f);
        static Material _hullMask, _hullLine, _hullClear;

        Renderer[] _renderers;
        bool _highlighted, _built;
        Renderer[] _ink;                        // свои 3 px — перекрашиваем
        MaterialPropertyBlock[] _inkOriginal;
        MaterialPropertyBlock _block;
        readonly List<Renderer> _hull = new List<Renderer>();

        public Vector3 Approach => transform.position+ApproachOffset;
        public string Title => CampServiceText.Get("npc."+Kind.ToString().ToLowerInvariant());
        public Bounds Shape
        {
            get { Cache();var bounds=new Bounds(transform.position+Vector3.up*.8f,new Vector3(.6f,1.6f,.6f));foreach(var r in _renderers)if(r!=null)bounds.Encapsulate(r.bounds);return bounds; }
        }
        /// <summary>Куда подходить: у палатки — ко входу (или краю модели без входа), у NPC — к нему самому.</summary>
        public Vector3 Target(Vector3 p) => Kind==CampServiceKind.Tent?(Entrance!=null?Entrance.position:Shape.ClosestPoint(p)):transform.position;
        public float Distance(Vector3 p) { var d=Target(p)-p;d.y=0;return d.magnitude; }
        public bool Near(Vector3 p) => Distance(p)<=Reach;

        void Cache()
        {
            if(_renderers!=null)return;
            var list=new List<Renderer>();
            foreach(var r in GetComponentsInChildren<Renderer>())if(!r.name.StartsWith(ShellName))list.Add(r);
            _renderers=list.ToArray();
        }

        public void Highlight(bool value)
        {
            if(_highlighted==value)return;Cache();_highlighted=value;
            if(value){Build();CaptureInk();Pulse();}
            else RestoreInk();
            foreach(var r in _hull)if(r!=null)r.enabled=value;
        }

        void LateUpdate(){ if(_highlighted)Pulse(); }

        /// <summary>Дыхание как у арки: медленная волна 1,5 рад/с, без скачков.</summary>
        void Pulse()
        {
            float wave=Mathf.Sin(Time.unscaledTime*1.5f);
            if(_ink!=null && _ink.Length>0)
            {
                var color=Color.Lerp(EmberDim,EmberHot,.5f+.5f*wave);
                if(_block==null)_block=new MaterialPropertyBlock();
                foreach(var r in _ink){if(r==null)continue;r.GetPropertyBlock(_block);_block.SetColor(OutlineColorId,color);r.SetPropertyBlock(_block);}
            }
            if(_hull.Count>0 && _hullLine!=null)_hullLine.SetColor(ColorId,new Color(1f,.72f,.25f,.42f+.08f*wave));
        }

        void CaptureInk()
        {
            if(_ink==null)return;
            for(int i=0;i<_ink.Length;i++){_inkOriginal[i].Clear();if(_ink[i]!=null)_ink[i].GetPropertyBlock(_inkOriginal[i]);}
        }

        void RestoreInk()
        {
            if(_ink==null)return;
            for(int i=0;i<_ink.Length;i++)
            {
                var r=_ink[i];if(r==null)continue;
                if(_inkOriginal[i].isEmpty)r.SetPropertyBlock(null);else r.SetPropertyBlock(_inkOriginal[i]);
            }
        }

        /// <summary>Разбор рендеров при первом наведении: со своей обводкой — в перекраску, прочие — в оболочку.</summary>
        void Build()
        {
            if(_built)return;_built=true;
            var ink=new List<Renderer>();
            foreach(var source in _renderers)
            {
                if(source==null || !source.enabled || !source.gameObject.activeInHierarchy)continue;
                if(HasOwnInk(source)){ink.Add(source);continue;}
                if(!(source is MeshRenderer) && !(source is SkinnedMeshRenderer))continue; // частицы, линии — мимо
                // В статик-батче sharedMesh — общий «Combined Mesh»: оболочка обвела бы весь батч.
                if(source.isPartOfStaticBatch){Debug.LogWarning($"[camp] {name}: {source.name} в статик-батче — контур наведения пропущен");continue;}
                if(!EnsureHullMaterials())continue;
                foreach(var stage in new[]{_hullMask,_hullLine,_hullClear})
                {
                    var shell=Shell(source,stage);
                    if(shell!=null)_hull.Add(shell);
                }
            }
            _ink=ink.ToArray();
            _inkOriginal=new MaterialPropertyBlock[_ink.Length];
            for(int i=0;i<_inkOriginal.Length;i++)_inkOriginal[i]=new MaterialPropertyBlock();
        }

        /// <summary>Своя обводка жителя: проход UnitOutlineMask включён и ширина в материале больше нуля.</summary>
        static bool HasOwnInk(Renderer renderer)
        {
            foreach(var m in renderer.sharedMaterials)
            {
                if(m==null || !m.HasProperty(OutlineWidthId) || !m.HasProperty(OutlineColorId))continue;
                if(m.GetFloat(OutlineWidthId)<=.001f)continue;
                if(m.FindPass("UnitOutlineMask")>=0 && m.GetShaderPassEnabled("UnitOutlineMask"))return true;
            }
            return false;
        }

        static Renderer Shell(Renderer source,Material material)
        {
            Mesh mesh=null;Renderer shell=null;
            var go=new GameObject(ShellName+" — "+material.name);
            go.layer=source.gameObject.layer;
            go.transform.SetParent(source.transform,false);
            if(source is SkinnedMeshRenderer skinned && skinned.sharedMesh!=null)
            {
                var copy=go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh=mesh=skinned.sharedMesh;copy.bones=skinned.bones;copy.rootBone=skinned.rootBone;
                copy.localBounds=skinned.localBounds;copy.quality=skinned.quality;
                shell=copy;
            }
            else if(source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh!=null)
            {
                go.AddComponent<MeshFilter>().sharedMesh=mesh=filter.sharedMesh;
                shell=go.AddComponent<MeshRenderer>();
            }
            if(shell==null){if(Application.isPlaying)Destroy(go);else DestroyImmediate(go);return null;}
            var materials=new Material[Mathf.Max(1,mesh.subMeshCount)];   // по сабмешам, не по слотам источника
            for(int i=0;i<materials.Length;i++)materials[i]=material;
            shell.sharedMaterials=materials;
            shell.shadowCastingMode=ShadowCastingMode.Off;shell.receiveShadows=false;
            shell.lightProbeUsage=LightProbeUsage.Off;shell.reflectionProbeUsage=ReflectionProbeUsage.Off;
            shell.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
            shell.enabled=false;
            return shell;
        }

        static bool EnsureHullMaterials()
        {
            if(_hullMask!=null && _hullLine!=null && _hullClear!=null)return true;
            var shader=Resources.Load<Shader>("Shaders/CampHoverOutline");
            if(shader==null || !shader.isSupported)return false;
            //                                stage    queue width      cull           ztest                     color comp                     op
            _hullMask =HullMaterial(shader,"маска",2994,0f,       CullMode.Off,  CompareFunction.Always,   0, CompareFunction.Always,  StencilOp.Replace);
            _hullLine =HullMaterial(shader,"линия",2995,LineWidth,CullMode.Front,CompareFunction.LessEqual,15, CompareFunction.NotEqual,StencilOp.Keep);
            _hullClear=HullMaterial(shader,"сброс",2996,0f,       CullMode.Off,  CompareFunction.Always,   0, CompareFunction.Always,  StencilOp.Zero);
            return true;
        }

        static Material HullMaterial(Shader shader,string stage,int queue,float width,CullMode cull,CompareFunction zTest,int colorMask,CompareFunction comp,StencilOp op)
        {
            var m=new Material(shader){name=stage,renderQueue=queue};
            m.SetFloat("_Width",width);m.SetFloat("_Cull",(float)cull);m.SetFloat("_ZTest",(float)zTest);m.SetFloat("_ColorMask",colorMask);
            m.SetFloat("_StencilRef",HoverBit);m.SetFloat("_StencilReadMask",HoverBit);m.SetFloat("_StencilWriteMask",HoverBit);
            m.SetFloat("_StencilComp",(float)comp);m.SetFloat("_StencilPass",(float)op);
            return m;
        }

        void OnDisable()=>Highlight(false);
        void OnDestroy()
        {
            foreach(var r in _hull)if(r!=null){if(Application.isPlaying)Destroy(r.gameObject);else DestroyImmediate(r.gameObject);}
            _hull.Clear();
        }
    }
}
