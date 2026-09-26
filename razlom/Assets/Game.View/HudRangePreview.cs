using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Досягаемость способности на земле в материале «Дым и свет» (владелец 26 сентября: огненная
    // нить «ни с чем не сочетается»). Вместо лент — залитые фигуры: диск, кольцо, сектор, полоса,
    // капсула. В каждой вершине — расстояние до края фигуры (uv0.x, метры, плюс — внутри).
    // Треугольники режутся по срединной оси фигуры, поэтому линейная интерполяция даёт точное
    // расстояние: кромка ровная, без бусин на стыках и клякс в углах. Снаружи узкая кайма
    // (≈16 px) под сглаживание, ореол и тень. Один переиспользуемый меш, без мусора за кадр.
    internal sealed class HudRangePreview
    {
        // Заливка без затухания внутрь (диск, полоса); у кольца — ширина светлой полосы.
        const float Open = 1000f;
        const float Tau = Mathf.PI * 2f;
        // Перерыв в показе дольше этого — фигура появляется заново, с мягким наплывом. Считается по
        // часам, а не по кадрам: проверка «в прошлом кадре фигуры не было» по Time.frameCount обнуляла
        // бы _Appear на каждом вызове, стоит построению пойти не строго раз в кадр, — и фигура
        // оставалась бы невидимой при чистом логе.
        const float AppearGap = .12f;
        static readonly int RimId = Shader.PropertyToID("_Rim"), FillId = Shader.PropertyToID("_Fill"),
            ShimmerId = Shader.PropertyToID("_Shimmer"), FringeId = Shader.PropertyToID("_Fringe"),
            LiftId = Shader.PropertyToID("_Lift"), AppearId = Shader.PropertyToID("_Appear");
        readonly GameObject _root;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MeshRenderer _renderer;
        readonly List<Vector3> _vertices = new List<Vector3>(4096);
        readonly List<Vector4> _uv = new List<Vector4>(4096);
        readonly List<int> _indices = new List<int>(12288);
        Matrix4x4 _toLocal;
        float _fringe, _band, _strength;
        float _shownAt, _lastShown = -1f;
        Camera _camera;
        public HudRangePreview(Transform owner)
        {
            _root = new GameObject("HUD ability reach") { layer = 5, hideFlags = HideFlags.HideAndDontSave };
            _root.transform.SetParent(owner,false);
            _mesh = new Mesh { name="HUD reach shapes" }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            var shader=Resources.Load<Shader>("UI/HUD/AbilityReach");
            if(shader!=null) { _material=new Material(shader); _renderer.sharedMaterial=_material; }
            // Без шейдера End молча прячет фигуру — пусть хотя бы лог скажет почему.
            else Debug.LogWarning("[Разлом] Досягаемость: шейдер Resources/UI/HUD/AbilityReach не загрузился, фигуры на земле не будет.");
            _renderer.enabled=false;
        }
        public void Begin(Camera camera, bool available)
        {
            _camera = camera;
            _vertices.Clear(); _uv.Clear(); _indices.Clear();
            // Вершины считаются в мире и переводятся в пространство объекта: родитель не обязан стоять в нуле.
            _toLocal = _root.transform.worldToLocalMatrix;
            float pixel = camera != null && camera.orthographic ? camera.orthographicSize*2f/Mathf.Max(1,Screen.height) : .012f;
            _fringe = pixel*16f;
            if(_material==null) return;
            // Готова — тёплая кремовая кромка с мерцанием; не готова — серее, тусклее и неподвижна.
            _material.SetColor(RimId, available ? new Color(1f,.87f,.72f,.9f) : new Color(.66f,.64f,.62f,.45f));
            _material.SetFloat(FillId, available ? .16f : .11f);
            _material.SetFloat(ShimmerId, available ? 1f : 0f);
            _material.SetFloat(FringeId, _fringe);
            // Тела и деревья прячут фигуру, трава и камни по колено — нет: это решает сам шейдер
            // тестом по буферу глубины камеры (_Lift), текстура глубины для этого не нужна.
        }

        /// <summary>Кольцо предела вокруг точки: кромка на радиусе, дым заливки гаснет к центру.</summary>
        public void Ring(Vector3 center, float radius, float strength = 1f)
        {
            if(radius<=.01f) return;
            float band=Mathf.Min(.9f,radius*.4f);
            Style(strength,band);
            Arc(center,radius,0f,Tau,radius-band);
        }

        /// <summary>Залитый круг: область удара, точка приземления.</summary>
        public void Disc(Vector3 center, float radius, float strength = 1f)
        {
            if(radius<=.01f) return;
            Style(strength,Open);
            Arc(center,radius,0f,Tau,0f);
        }

        /// <summary>Сектор из точки: полуугол в радианах, от 180° — полный круг.</summary>
        public void Sector(Vector3 center, Vector3 forward, float radius, float half, float strength = 1f)
        {
            if(radius<=.01f) return;
            if(half>=Mathf.PI-.01f) { Disc(center,radius,strength); return; }
            half=Mathf.Max(half,.02f);
            Style(strength,Open);
            float angle=Angle(forward);
            // Спицы: на каждой расстояние до края — до ближней стороны (r·sinθ), дальше — до дуги (R−r).
            // Точка перелома r = R/(1+sinθ); чётное число спиц кладёт одну ровно на биссектрису.
            int steps=Steps(radius+_fringe,half*2f);
            if((steps&1)==1) steps++;
            int apex=Vertex(center,0f), previousSplit=-1, previousRim=-1, previousOuter=-1;
            for(int k=0;k<=steps;k++)
            {
                float phi=-half+half*2f*k/steps;
                float sin=Mathf.Sin(Mathf.Min(half-Mathf.Abs(phi),Mathf.PI*.5f));
                float split=radius/(1f+sin);
                Vector3 direction=Direction(angle+phi);
                int splitIndex=Vertex(center+direction*split,radius-split);
                int rim=Vertex(center+direction*radius,0f);
                int outer=Vertex(center+direction*(radius+_fringe),-_fringe);
                if(k>0)
                {
                    Tri(apex,previousSplit,splitIndex);
                    Quad(previousSplit,previousRim,rim,splitIndex);
                    Quad(previousRim,previousOuter,outer,rim);
                }
                previousSplit=splitIndex; previousRim=rim; previousOuter=outer;
            }
            Vector3 left=Direction(angle-half), right=Direction(angle+half);
            Vector3 leftOut=Direction(angle-half-Mathf.PI*.5f), rightOut=Direction(angle+half+Mathf.PI*.5f);
            EdgeFringe(center,center+left*radius,leftOut);
            EdgeFringe(center,center+right*radius,rightOut);
            Corner(center+left*radius,angle-half-Mathf.PI*.5f,angle-half);
            Corner(center+right*radius,angle+half,angle+half+Mathf.PI*.5f);
            // Шире полукруга вершина вогнута, и каймы сторон сходятся сами.
            if(half<Mathf.PI*.5f) Corner(center,angle+half+Mathf.PI*.5f,angle-half-Mathf.PI*.5f);
        }

        /// <summary>Прямоугольная полоса от точки вперёд: длина и полуширина в метрах.</summary>
        public void Lane(Vector3 origin, Vector3 forward, float length, float halfWidth, float strength = 1f)
        {
            if(length<=.01f || halfWidth<=.01f) return;
            Style(strength,Open);
            Vector3 u=forward, s=new Vector3(forward.z,0f,-forward.x);
            Vector3 backLeft=origin-s*halfWidth, backRight=origin+s*halfWidth;
            Vector3 frontLeft=backLeft+u*length, frontRight=backRight+u*length;
            // Крыша: боковые трапеции и торцевые треугольники сходятся на срединной оси.
            if(length>=halfWidth*2f)
            {
                Vector3 axisBack=origin+u*halfWidth, axisFront=origin+u*(length-halfWidth);
                Quad(Vertex(backLeft,0f),Vertex(frontLeft,0f),Vertex(axisFront,halfWidth),Vertex(axisBack,halfWidth));
                Quad(Vertex(backRight,0f),Vertex(frontRight,0f),Vertex(axisFront,halfWidth),Vertex(axisBack,halfWidth));
                Tri(Vertex(backLeft,0f),Vertex(backRight,0f),Vertex(axisBack,halfWidth));
                Tri(Vertex(frontLeft,0f),Vertex(frontRight,0f),Vertex(axisFront,halfWidth));
            }
            else
            {
                // Короткая и широкая полоса: ось идёт поперёк.
                float h=length*.5f;
                Vector3 axisLeft=origin+u*h-s*(halfWidth-h), axisRight=origin+u*h+s*(halfWidth-h);
                Quad(Vertex(backLeft,0f),Vertex(backRight,0f),Vertex(axisRight,h),Vertex(axisLeft,h));
                Quad(Vertex(frontLeft,0f),Vertex(frontRight,0f),Vertex(axisRight,h),Vertex(axisLeft,h));
                Tri(Vertex(backLeft,0f),Vertex(frontLeft,0f),Vertex(axisLeft,h));
                Tri(Vertex(backRight,0f),Vertex(frontRight,0f),Vertex(axisRight,h));
            }
            EdgeFringe(backLeft,frontLeft,-s);
            EdgeFringe(backRight,frontRight,s);
            EdgeFringe(backLeft,backRight,-u);
            EdgeFringe(frontLeft,frontRight,u);
            Corner(backLeft,Angle(-u),Angle(-s));
            Corner(backRight,Angle(-u),Angle(s));
            Corner(frontLeft,Angle(u),Angle(-s));
            Corner(frontRight,Angle(u),Angle(s));
        }

        /// <summary>Заметённый путь: отрезок от точки вперёд со скруглёнными концами.</summary>
        public void Capsule(Vector3 origin, Vector3 forward, float length, float halfWidth, float strength = 1f)
        {
            if(halfWidth<=.01f) return;
            if(length<=.01f) { Disc(origin,halfWidth,strength); return; }
            Style(strength,Open);
            Vector3 end=origin+forward*length, s=new Vector3(forward.z,0f,-forward.x);
            Vector3 side=s*halfWidth;
            float angle=Angle(forward);
            Quad(Vertex(origin+side,0f),Vertex(end+side,0f),Vertex(end,halfWidth),Vertex(origin,halfWidth));
            Quad(Vertex(origin-side,0f),Vertex(end-side,0f),Vertex(end,halfWidth),Vertex(origin,halfWidth));
            EdgeFringe(origin+side,end+side,s);
            EdgeFringe(origin-side,end-side,-s);
            Arc(end,halfWidth,angle-Mathf.PI*.5f,angle+Mathf.PI*.5f,0f);
            Arc(origin,halfWidth,angle+Mathf.PI*.5f,angle+Mathf.PI*1.5f,0f);
        }

        public void End()
        {
            _mesh.Clear();
            if(_material==null || _vertices.Count==0) { _renderer.enabled=false; return; }
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0,_uv); _mesh.SetTriangles(_indices,0,true);
            // Мягкое появление за .14 с после перерыва в показе.
            float now=UiMotion.Now;
            if(_lastShown<0f || now-_lastShown>AppearGap) _shownAt=now;
            _lastShown=now;
            _material.SetFloat(AppearId,Mathf.Clamp01((now-_shownAt)/.14f));
            _renderer.enabled=true;
        }
        public void Hide() { if(_renderer!=null)_renderer.enabled=false; }
        public void Dispose()
        {
            if(_root!=null)Object.Destroy(_root);
            if(_mesh!=null)Object.Destroy(_mesh);
            if(_material!=null)Object.Destroy(_material);
        }

        void Style(float strength, float band) { _strength=Mathf.Clamp01(strength); _band=band; }

        // Дуга кромки вокруг center от угла from до to: кайма наружу, внутрь до радиуса inner
        // (0 — веер в центр). Вдоль радиуса расстояние до края линейно, интерполяция точная.
        void Arc(Vector3 center, float radius, float from, float to, float inner)
        {
            int steps=Steps(radius+_fringe,to-from);
            int middle=inner>0f ? -1 : Vertex(center,radius);
            int previousOuter=-1, previousRim=-1, previousInner=-1;
            for(int k=0;k<=steps;k++)
            {
                Vector3 direction=Direction(Mathf.Lerp(from,to,k/(float)steps));
                int outer=Vertex(center+direction*(radius+_fringe),-_fringe);
                int rim=Vertex(center+direction*radius,0f);
                int core=inner>0f ? Vertex(center+direction*inner,radius-inner) : middle;
                if(k>0)
                {
                    Quad(previousOuter,outer,rim,previousRim);
                    if(inner>0f) Quad(previousRim,rim,core,previousInner);
                    else Tri(previousRim,rim,middle);
                }
                previousOuter=outer; previousRim=rim; previousInner=core;
            }
        }

        // Кайма прямого края: от кромки (0) наружу на ширину каймы.
        void EdgeFringe(Vector3 from, Vector3 to, Vector3 outward)
        {
            Quad(Vertex(from,0f),Vertex(to,0f),Vertex(to+outward*_fringe,-_fringe),Vertex(from+outward*_fringe,-_fringe));
        }

        // Кайма вокруг выпуклого угла: веер, расстояние считается от самой вершины.
        void Corner(Vector3 at, float from, float to)
        {
            float span=Mathf.DeltaAngle(from*Mathf.Rad2Deg,to*Mathf.Rad2Deg)*Mathf.Deg2Rad;
            int steps=Steps(_fringe,span);
            int middle=Vertex(at,0f), previous=-1;
            for(int k=0;k<=steps;k++)
            {
                int outer=Vertex(at+Direction(from+span*k/steps)*_fringe,-_fringe);
                if(k>0) Tri(middle,previous,outer);
                previous=outer;
            }
        }

        int Vertex(Vector3 world, float edge)
        {
            _vertices.Add(_toLocal.MultiplyPoint3x4(world));
            _uv.Add(new Vector4(edge,_band,_strength,0f));
            return _vertices.Count-1;
        }
        void Tri(int a, int b, int c) { _indices.Add(a); _indices.Add(b); _indices.Add(c); }
        void Quad(int a, int b, int c, int d) { Tri(a,b,c); Tri(a,c,d); }
        static Vector3 Direction(float angle) => new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle));
        static float Angle(Vector3 v) => Mathf.Atan2(v.z,v.x);
        // Хорда отходит от дуги не дальше 3 мм — меньше пикселя при любом приближении камеры.
        static int Steps(float radius, float span)
        {
            float step=2f*Mathf.Acos(1f-Mathf.Min(.003f/Mathf.Max(radius,.01f),1f));
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(span)/Mathf.Max(step,.02f)),2,160);
        }
    }
}
