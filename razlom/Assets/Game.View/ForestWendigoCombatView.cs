using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Бой вендиго со стороны картинки: красные метки (клин когтей, круг
    /// посадки) и VFX V12 по целевым кадрам 26.09 — всё на нашей земле, без
    /// экранных плоскостей: три ленты когтей широкой дугой по всему маху
    /// кончика когтя, борозды и веер земли вместе с ними, выброс из-под стоп и
    /// полоса пыли по ходу прыжка на отталкивании, кратер и юбка пыли на
    /// посадке, кончики корней на замахе воя и кольцо корней в коре на ударе. Префабы
    /// собирает ForestWendigoVfxSetup. Каждый эффект ведётся по возрасту от
    /// тика Sim через Simulate — пауза и съёмка держат кадр.
    /// </summary>
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

        /// <summary>Экземпляр префаба эффекта: возраст — от тика Tick, системы догоняются приращениями.</summary>
        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public int Tick = -1000;
            public float Life, Simulated = -1f;
            // Эффект, начатый до удара (коготь, замах воя), гаснет, если Sim
            // отменила атаку раньше контакта: оглушение не оставляет ленту в воздухе.
            public int Entity = -1, Serial, CancelBefore = int.MinValue;
        }

        private sealed class Pool
        {
            public Burst[] Items;
            public int Cursor;
        }

        private sealed class Bones { public Transform Head; }

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private readonly Mark[] _marks = new Mark[4];
        private Material _material;
        private Pool _claw, _takeoff, _landing, _howlWindup, _howl, _breath;
        private Pool[] _pools;
        private readonly Dictionary<int, int> _clawFired = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _launched = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _windupFired = new Dictionary<int, int>();
        private readonly Dictionary<Transform, Bones> _bones = new Dictionary<Transform, Bones>();

        /// <summary>
        /// Коготь стартует за тик до контакта: вид ведёт кадр 52 клипа на тик
        /// удара, а мах, по которому идут ленты (кадры 49,5–54,5), начинается ~на тик раньше.
        /// Время внутри префаба (голова ленты, борозды, веер земли) отсчитано от этого тика.
        /// </summary>
        private const int ClawLeadTicks = 1;

        /// <summary>Шаг догоняющей симуляции: столкновения комьев с землёй не проскакивают на рывке кадра.</summary>
        private const float SimulateStep = 1f / 30f;

        /// <summary>Череп в позе воя, если кости головы нет: над корнем, чуть вперёд.</summary>
        private static readonly Vector3 HeadFallback = new Vector3(0f, 2.3f, .25f);

        private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
            IsSector = Shader.PropertyToID("_IsSector"), Impact = Shader.PropertyToID("_Impact"), Ring = Shader.PropertyToID("_Ring");

        private static readonly int WarningRadius = Shader.PropertyToID("_Radius");

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
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
            // Пулы заводятся сразу: в бою ни одного Instantiate. Двух вендиго
            // с частым когтем хватает четырёх когтей; крупные атаки — по жетону.
            _claw = MakePool("VFX_Wendigo_Claw", "Вендиго: коготь", 4, 2.3f);
            _takeoff = MakePool("VFX_Wendigo_Takeoff", "Вендиго: отталкивание", 2, 2.3f);
            _landing = MakePool("VFX_Wendigo_Landing", "Вендиго: приземление", 2, 2.7f);
            _howlWindup = MakePool("VFX_Wendigo_HowlWindup", "Вендиго: замах воя", 2, 1.15f);
            _howl = MakePool("VFX_Wendigo_Howl", "Вендиго: вой", 2, 2.3f);
            _breath = MakePool("VFX_Wendigo_HowlBreath", "Вендиго: дыхание воя", 2, 1.3f);
            _pools = new[] { _claw, _takeoff, _landing, _howlWindup, _howl, _breath };
        }

        private Pool MakePool(string prefabName, string name, int count, float life)
        {
            var pool = new Pool { Items = new Burst[0] };
            var prefab = Resources.Load<GameObject>("VFX/Wendigo/Prefabs/" + prefabName);
            if (prefab == null) return pool;
            pool.Items = new Burst[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform); go.name = name;
                var burst = new Burst { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true), Life = life };
                burst.Seeds = new uint[burst.Particles.Length];
                for (int k = 0; k < burst.Particles.Length; k++)
                {
                    var ps = burst.Particles[k];
                    // Прогрев: один короткий прогон заводит буферы частиц до боя.
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Seeds[k] = ps.randomSeed;
                }
                go.SetActive(false);
                pool.Items[i] = burst;
            }
            return pool;
        }

        private void LateUpdate()
        {
            var sim=_driver.Sim;if(sim==null)return;
            if(_shown!=sim)
            {
                _shown=sim;foreach(var m in _marks){m.Entity=-1;m.Serial=0;m.Root.SetActive(false);}
                foreach (var pool in _pools) foreach (var b in pool.Items) Retire(b);
                _launched.Clear(); _clawFired.Clear(); _windupFired.Clear(); _bones.Clear();
            }
            float tick=sim.Tick-1+_driver.Alpha;
            // Удары, пришедшие событием: посадка прыжка и удар воя.
            foreach(var c in _driver.FrameEventContexts)
            {
                var e = c.Event;
                if (e.Type != SimEventType.WendigoImpact) continue;
                bool known = sim.TryGetWendigoAction(e.Source, out var action) && action.Serial == e.Amount;
                int at = known ? action.ImpactTick : c.SimulationTick;
                if (e.ActionVariant == (int)WendigoAction.Leap) Landing(e.Position, known ? action.Direction : default, at);
                else if (e.ActionVariant == (int)WendigoAction.Howl) HowlImpact(e.Source, e.Position, known ? action.Direction : default, at);
            }
            // Опрос: коготь (за тик до контакта), отталкивание (тик взлёта), замах воя (старт).
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.TryGetWendigoAction(id, out var a)) continue;
                if (a.Kind == WendigoAction.Claw)
                {
                    int start = a.ImpactTick - ClawLeadTicks;
                    if (tick < start || (_clawFired.TryGetValue(id, out int fired) && fired == a.Serial)) continue;
                    _clawFired[id] = a.Serial;
                    var b = Take(_claw, start, Ground(a.Origin, 0f), Facing(a.Direction), a.Serial);
                    if (b != null) { b.Entity = id; b.Serial = a.Serial; b.CancelBefore = a.ImpactTick; }
                }
                else if (a.Kind == WendigoAction.Leap)
                {
                    if (tick < a.LaunchTick || (_launched.TryGetValue(id, out int serial) && serial == a.Serial)) continue;
                    _launched[id] = a.Serial;
                    Take(_takeoff, a.LaunchTick, Ground(a.Origin, 0f), Facing(a.Direction), a.Serial);
                }
                else if (a.Kind == WendigoAction.Howl)
                {
                    if (tick < a.StartTick || (_windupFired.TryGetValue(id, out int serial) && serial == a.Serial)) continue;
                    _windupFired[id] = a.Serial;
                    var b = Take(_howlWindup, a.StartTick, Ground(a.Origin, 0f), Facing(a.Direction), a.Serial);
                    if (b != null) { b.Entity = id; b.Serial = a.Serial; b.CancelBefore = a.ImpactTick; }
                }
            }
            foreach (var pool in _pools)
                foreach (var b in pool.Items) Advance(sim, b, tick);
            foreach(var m in _marks)
            {
                if(m.Entity<0)continue;
                bool active=sim.TryGetWendigoAction(m.Entity,out var a)&&a.Serial==m.Serial;
                if(!active||!sim.Entities.Alive[m.Entity]||tick>m.Action.ImpactTick+9)
                {m.Root.SetActive(false);m.Entity=-1;continue;}
                float p=Mathf.Clamp01((tick-a.StartTick)/(a.ImpactTick-a.StartTick));
                float impact=Mathf.Max(0,tick-a.ImpactTick)/9;
                m.Block.SetFloat(Progress,p);m.Block.SetFloat(Impact,impact);
                // Реф: метка исчезает ровно на касании; клин — на контакте когтей.
                m.Block.SetFloat(Opacity,impact>0?Mathf.Max(0f,1-impact*3f):1);m.Renderer.SetPropertyBlock(m.Block);
            }
            for(int id=1;id<sim.Entities.Count;id++)
            {
                // Кольцо воя рисует общий GroundTelegraphView (SharedView): здесь только клин и круг.
                if(!sim.TryGetWendigoAction(id,out var a)||a.Kind==WendigoAction.Howl||tick>a.ImpactTick+9)continue;
                Mark free=null;bool exists=false;
                foreach(var m in _marks){if(m.Entity==id&&m.Serial==a.Serial)exists=true;if(m.Entity<0)free=m;}
                if(exists||free==null)continue;Build(free,id,a);
            }
        }

        // ------------------------------------------------------------ effects

        /// <summary>
        /// Берёт следующий экземпляр пула и ставит его корень на землю. Зерно
        /// систем меняется с номером атаки: удары не повторяют друг друга, а
        /// перемотка той же атаки даёт тот же кадр.
        /// </summary>
        private Burst Take(Pool pool, int tick, Vector3 position, Quaternion rotation, int serial)
        {
            if (pool.Items.Length == 0) return null;
            var b = pool.Items[pool.Cursor++ % pool.Items.Length];
            b.Tick = tick; b.Simulated = -1f; b.Entity = -1; b.Serial = 0; b.CancelBefore = int.MinValue;
            b.Root.transform.SetPositionAndRotation(position, rotation);
            b.Root.SetActive(true);
            for (int k = 0; k < b.Particles.Length; k++)
            {
                var ps = b.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = b.Seeds[k] + (uint)serial * 7919u;
            }
            return b;
        }

        private static void Retire(Burst b)
        {
            if (b == null) return;
            b.Tick = -1000; b.Simulated = -1f; b.Entity = -1;
            if (b.Root.activeSelf) b.Root.SetActive(false);
        }

        /// <summary>
        /// Возраст эффекта — от тика Sim с долей кадра. Вперёд системы догоняются
        /// приращениями, назад (перемотка) — перезапуском; на паузе возраст стоит
        /// и частицы стоят.
        /// </summary>
        private void Advance(Simulation sim, Burst b, float tick)
        {
            if (b == null || !b.Root.activeSelf) return;
            float age = (tick - b.Tick) / Simulation.TicksPerSecond;
            if (age > b.Life) { Retire(b); return; }
            if (b.Entity >= 0 && tick < b.CancelBefore
                && (!sim.TryGetWendigoAction(b.Entity, out var a) || a.Serial != b.Serial)) { Retire(b); return; }
            age = Mathf.Max(0f, age);
            if (b.Simulated >= 0f && Mathf.Abs(age - b.Simulated) < 1e-5f) return;
            bool restart = b.Simulated < 0f || age < b.Simulated;
            float from = restart ? 0f : b.Simulated;
            foreach (var ps in b.Particles)
            {
                float done = from;
                bool first = restart;
                do
                {
                    float step = Mathf.Min(SimulateStep, age - done);
                    ps.Simulate(step, false, first, false);
                    first = false;
                    done += step;
                } while (done < age - 1e-5f);
                ps.Pause(false);
            }
            b.Simulated = age;
        }

        private Vector3 Ground(FixVec2 at, float lift)
        {
            float x = at.X.ToFloat(), z = at.Y.ToFloat();
            return new Vector3(x, (_layout != null ? _layout.WeaponGroundHeight(x, z) : 0f) + lift, z);
        }

        private static Quaternion Facing(FixVec2 direction)
        {
            var forward = new Vector3(direction.X.ToFloat(), 0f, direction.Y.ToFloat());
            return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
        }

        /// <summary>Посадка: кратер, трещины, юбка пыли и комья в точке касания, +Z — по ходу прыжка.</summary>
        private void Landing(FixVec2 at, FixVec2 direction, int tick) =>
            Take(_landing, tick, Ground(at, 0f), Facing(direction), tick);

        /// <summary>Удар воя: кольцо шипов у ног зверя и дыхание у его черепа.</summary>
        private void HowlImpact(int entity, FixVec2 at, FixVec2 direction, int tick)
        {
            var facing = Facing(direction);
            Vector3 origin = Ground(at, 0f);
            Take(_howl, tick, origin, facing, tick);
            Bones bones = BonesOf(entity);
            Vector3 head = bones?.Head != null ? bones.Head.position : origin + facing * HeadFallback;
            Take(_breath, tick, head, facing, tick);
        }

        private Bones BonesOf(int entity)
        {
            if (_arena == null || !_arena.TryGetEntityView(entity, out Transform view) || view == null) return null;
            if (_bones.TryGetValue(view, out Bones bones)) return bones;
            bones = new Bones();
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
                if (t.name == "head") { bones.Head = t; break; }
            _bones[view] = bones;
            return bones;
        }

        // -------------------------------------------------------------- marks

        private void Build(Mark m,int id,WendigoActionState a)
        {
            m.Entity=id;m.Serial=a.Serial;m.Action=a;
            bool sector=a.Kind==WendigoAction.Claw;
            // The shared warning boundary is the exact Sim damage footprint.
            float radius=sector?2.7f:1.25f;
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
            m.Block.SetFloat(IsSector,sector?1:0);m.Block.SetFloat(Ring,1f);m.Block.SetFloat(WarningRadius,radius);
            m.Block.SetFloat(Progress,0);m.Block.SetFloat(Opacity,1);m.Block.SetFloat(Impact,0);
            m.Renderer.SetPropertyBlock(m.Block);m.Root.SetActive(true);
        }

        private void OnDestroy()
        {
            foreach(var m in _marks)if(m!=null){Destroy(m.Mesh);Destroy(m.Root);}if(_material!=null)Destroy(_material);
            if (_pools != null)
                foreach (var pool in _pools)
                    foreach (var b in pool.Items) if (b != null) Destroy(b.Root);
        }
    }
}
