using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Бой вендиго со стороны картинки: красные метки (клин когтей, круг
    /// посадки), и VFX по референсам claw-sync-r03 (25.09) — свечение когтей
    /// на замахе, мазок когтей с тремя лентами и одновременный веер земли на
    /// контакте, выброс из-под стоп на отталкивании, столб пыли с комьями на
    /// посадке. Префабы собирает ForestWendigoVfxSetup. Всё ведётся по
    /// возрасту от тика Sim через Simulate — пауза и съёмка держат кадр.
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
        /// <summary>Один пул эффектов одного вида: экземпляры префаба, ведомые по возрасту.</summary>
        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public Renderer[] Marks;
            public MaterialPropertyBlock Block;
            public int Tick = -1000;
            public float Life, MarkLife;
            public Transform Follow;
            public Vector3 FollowOffset;
        }
        private sealed class Bones { public Transform LeftHand, RightHand, LeftArm, RightArm, LeftFoot, RightFoot; }
        private sealed class Swing { public WendigoClawRibbon Ribbon; public int Serial; public bool Sampling; }

        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private readonly Mark[] _marks = new Mark[4];
        private Material _material;
        private Burst[] _clawFlip, _landingSkirt, _landingColumn, _takeoffGround, _takeoffColumn;
        private int _clawFlipCursor, _landingSkirtCursor, _landingColumnCursor, _takeoffGroundCursor, _takeoffColumnCursor;
        private readonly Dictionary<int, int> _clawFired = new Dictionary<int, int>();
        private readonly Dictionary<Transform, Bones> _bones = new Dictionary<Transform, Bones>();
        private readonly Dictionary<int, int> _launched = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _glowing = new Dictionary<int, int>();
        private readonly Dictionary<int, Swing> _swings = new Dictionary<int, Swing>();
        private readonly Dictionary<int, Burst[]> _glowBursts = new Dictionary<int, Burst[]>();
        /// <summary>Мах когтей: серп идёт от тика контакта минус два до контакта плюс семь (0,3 с).</summary>
        private const int SwingBeforeTicks = 2, SwingAfterTicks = 7;
        /// <summary>
        /// Серп по рефу: три вложенных дуги вокруг зверя на высоте колена, от его
        /// правого бока-сзади через фронт к левому боку; радиус — вылет когтей.
        /// Угол — от направления удара, положительный — вправо.
        /// </summary>
        private const float SweepStartDegrees = 118f, SweepEndDegrees = -72f, SweepRadius = 2.0f;
        /// <summary>
        /// Флипбуки Higgsfield (V8): билборд к камере, повёрнутый в плоскости экрана
        /// по направлению удара. В элементе когтей выпуклая сторона серпов смотрит
        /// на экранный угол RefClawDegrees (вниз-влево: «C» открыт вправо-вверх),
        /// в референсе выпуклость идёт по ходу удара. Билборд сдвинут к камере, чтобы ноги зверя его не резали.
        /// </summary>
        /// Сдвиг к камере не меняет место на экране (камера ортографическая), но
        /// поднимает плоскость билборда над землёй: иначе нижняя часть ячейки
        /// (юбка пыли, основания «свечей») уходила под грунт и срезалась.
        private const float RefClawDegrees = -135f, ClawFlipForward = .35f, ClawFlipHeight = .12f;
        private const int ClawFlipLeadTicks = 8;
        private const float GroundLayerLift = .06f;
        private const float LandingFlipTowardCamera = 1.6f, TakeoffFlipTowardCamera = 1.5f;
        private const bool UseClawRibbon = false;
        private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
            IsSector = Shader.PropertyToID("_IsSector"), Impact = Shader.PropertyToID("_Impact"), Ring = Shader.PropertyToID("_Ring");

        private static readonly int WarningRadius = Shader.PropertyToID("_Radius");
        private const float ClawGroundForward = 1.6f;

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
            _clawFlip = Pool("VFX/Wendigo/Prefabs/VFX_Wendigo_ClawFlip", "Вендиго: серпы (флипбук)", 3, .62f, 0f);
            _landingSkirt = Pool("VFX/Wendigo/Prefabs/VFX_Wendigo_LandingSkirt", "Вендиго: посадка, юбка", 2, 3.35f, 0f);
            _landingColumn = Pool("VFX/Wendigo/Prefabs/VFX_Wendigo_LandingColumn", "Вендиго: посадка, столб", 2, 3.35f, 0f);
            _takeoffGround = Pool("VFX/Wendigo/Prefabs/VFX_Wendigo_TakeoffGround", "Вендиго: отталкивание, земля", 2, 2.85f, 0f);
            _takeoffColumn = Pool("VFX/Wendigo/Prefabs/VFX_Wendigo_TakeoffColumn", "Вендиго: отталкивание, свечи", 2, 2.85f, 0f);
        }

        private Burst[] Pool(string path, string name, int count, float life, float markLife, Color? tint = null)
        {
            var prefab = Resources.Load<GameObject>(path);
            var pool = new Burst[count];
            if (prefab == null) return pool;
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform); go.name = name;
                if (tint.HasValue)
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        main.startColor = tint.Value;
                    }
                var burst = new Burst
                {
                    Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true),
                    Marks = go.GetComponentsInChildren<MeshRenderer>(true), Life = life, MarkLife = markLife,
                    Block = new MaterialPropertyBlock()
                };
                foreach (var ps in burst.Particles) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                go.SetActive(false);
                pool[i] = burst;
            }
            return pool;
        }

        private void LateUpdate()
        {
            var sim=_driver.Sim;if(sim==null)return;
            if(_shown!=sim)
            {
                _shown=sim;foreach(var m in _marks){m.Entity=-1;m.Serial=0;m.Root.SetActive(false);}
                foreach (var pool in new[] { _clawFlip, _landingSkirt, _landingColumn, _takeoffGround, _takeoffColumn })
                    foreach (var b in pool) if (b != null) { b.Tick = -1000; b.Follow = null; b.Root.SetActive(false); }
                _launched.Clear(); _glowing.Clear(); _glowBursts.Clear(); _clawFired.Clear();
                foreach (var swing in _swings.Values) swing.Ribbon.End();
            }
            float tick=sim.Tick-1+_driver.Alpha;
            foreach(var c in _driver.FrameEventContexts)
            {
                var e = c.Event;
                if (e.Type != SimEventType.WendigoImpact) continue;
                if (e.ActionVariant == (int)WendigoAction.Leap)
                    LandingImpact(e.Position, c.SimulationTick);
            }
            if (UseClawRibbon) UpdateSwings(sim, tick);
            // Отталкивание: у Sim нет отдельного события, момент — LaunchTick прыжка.
            // Серп когтей: за ClawFlipLeadTicks до контакта, чтобы к удару быть дорисованным (владелец: «с сильным запозданием»).
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.TryGetWendigoAction(id, out var a)) continue;
                if (a.Kind == WendigoAction.Leap)
                {
                    if (tick < a.LaunchTick || (_launched.TryGetValue(id, out int serial) && serial == a.Serial)) continue;
                    _launched[id] = a.Serial;
                    Takeoff(id, a);
                }
                else if (a.Kind == WendigoAction.Claw)
                {
                    if (tick < a.ImpactTick - ClawFlipLeadTicks || (_clawFired.TryGetValue(id, out int fired) && fired == a.Serial)) continue;
                    _clawFired[id] = a.Serial;
                    ClawImpact(id, a, a.ImpactTick - ClawFlipLeadTicks);
                }
            }
            foreach (var pool in new[] { _clawFlip, _landingSkirt, _landingColumn, _takeoffGround, _takeoffColumn })
                foreach (var b in pool)
                {
                    if (b == null || !b.Root.activeSelf) continue;
                    float age = Mathf.Max(0, tick - b.Tick) / Simulation.TicksPerSecond;
                    if (age > b.Life) { b.Root.SetActive(false); b.Follow = null; continue; }
                    if (b.Follow != null) b.Root.transform.position = b.Follow.TransformPoint(b.FollowOffset);
                    foreach (var ps in b.Particles) { ps.Simulate(age, false, true, false); ps.Pause(false); }
                    if (b.MarkLife > 0f && b.Marks.Length > 0)
                    {
                        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b.MarkLife * .6f, b.MarkLife, age));
                        b.Block.SetFloat(Opacity, .6f * fade);
                        foreach (var mark in b.Marks) mark.SetPropertyBlock(b.Block);
                    }
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
                // Реф: метка исчезает ровно на касании; клин — на контакте когтей.
                m.Block.SetFloat(Opacity,impact>0?Mathf.Max(0f,1-impact*3f):1);m.Renderer.SetPropertyBlock(m.Block);
            }
            for(int id=1;id<sim.Entities.Count;id++)
            {
                if(!sim.TryGetWendigoAction(id,out var a)||tick>a.ImpactTick+9)continue;
                Mark free=null;bool exists=false;
                foreach(var m in _marks){if(m.Entity==id&&m.Serial==a.Serial)exists=true;if(m.Entity<0)free=m;}
                if(exists||free==null)continue;Build(free,id,a);
            }
        }

        // ------------------------------------------------------------ effects

        private Burst Take(Burst[] pool, ref int cursor, int tick, Vector3 position, Quaternion rotation, float scale)
        {
            if (pool == null || pool.Length == 0 || pool[0] == null) return null;
            var b = pool[cursor++ % pool.Length];
            b.Tick = tick; b.Follow = null;
            b.Root.transform.SetPositionAndRotation(position, rotation);
            b.Root.transform.localScale = Vector3.one * scale;
            b.Root.SetActive(true);
            foreach (var ps in b.Particles) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Clear(true); }
            if (b.MarkLife > 0f && b.Marks.Length > 0)
            {
                b.Block.SetFloat(Opacity, .6f);
                foreach (var mark in b.Marks) mark.SetPropertyBlock(b.Block);
            }
            return b;
        }

        private Vector3 Ground(float x, float z, float lift) => new Vector3(x, _layout.WeaponGroundHeight(x, z) + lift, z);

        private static Vector3 TowardCamera(Vector3 point, float distance)
        {
            var camera = Camera.main;
            if (camera == null) return point;
            return point - camera.transform.forward * distance;
        }

        /// <summary>
        /// Квад на плоскости земли: местный +Y — «верх экрана» (от камеры по горизонту),
        /// +X — вправо по экрану, нормаль вниз (частицы двусторонние), так текстура не
        /// зеркалится. Выпуклость элемента (referenceDegrees в осях кадра) доворачивается
        /// вокруг вертикали на направление удара — одинаково честно с любой стороны.
        /// </summary>
        /// <summary>Вертикальная вырезка: стоит на земле, повёрнута к камере по горизонтали, низ ячейки — на позиции.</summary>
        private static Quaternion VerticalBillboard()
        {
            var camera = Camera.main;
            Vector3 forwardFlat = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : Vector3.forward;
            if (forwardFlat.sqrMagnitude < 1e-4f) forwardFlat = Vector3.forward;
            return Quaternion.LookRotation(-forwardFlat.normalized, Vector3.up);
        }

        private static Quaternion GroundBillboard(Vector3 direction, float referenceDegrees)
        {
            var camera = Camera.main;
            Vector3 forwardFlat = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : Vector3.forward;
            if (forwardFlat.sqrMagnitude < 1e-4f) forwardFlat = Vector3.forward;
            forwardFlat.Normalize();
            Vector3 rightFlat = Vector3.Cross(Vector3.up, forwardFlat);
            Quaternion flat = Quaternion.LookRotation(-Vector3.up, forwardFlat);
            float r = referenceDegrees * Mathf.Deg2Rad;
            Vector3 bulge = rightFlat * Mathf.Cos(r) + forwardFlat * Mathf.Sin(r);
            float spin = direction.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(bulge, direction, Vector3.up) : 0f;
            return Quaternion.AngleAxis(spin, Vector3.up) * flat;
        }

        /// <summary>
        /// Поворот билборда: лицом к камере, в плоскости экрана довёрнут так, чтобы
        /// экранный угол направления совпал с углом референса. Нулевое направление — без доворота.
        /// </summary>
        private static Quaternion FacingBillboard(Vector3 at, Vector3 direction, float referenceDegrees)
        {
            var camera = Camera.main;
            if (camera == null) return Quaternion.identity;
            float spin = 0f;
            if (direction.sqrMagnitude > 1e-4f)
            {
                Vector3 a = camera.WorldToScreenPoint(at), b = camera.WorldToScreenPoint(at + direction);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                spin = angle - referenceDegrees;
            }
            return camera.transform.rotation * Quaternion.Euler(0f, 0f, spin);
        }

        /// <summary>
        /// Контакт когтей: серпы референса лежат на плоскости земли у тела зверя и
        /// повёрнуты по направлению удара (владелец 26.09: экранный билборд «не со всех
        /// сторон правильно отображается к камере»).
        /// </summary>
        private void ClawImpact(int entity, WendigoActionState action, int tick)
        {
            var direction = new Vector3(action.Direction.X.ToFloat(), 0f, action.Direction.Y.ToFloat());
            Vector3 origin = Ground(action.Origin.X.ToFloat(), action.Origin.Y.ToFloat(), 0f);
            Vector3 body = Ground(origin.x + direction.x * ClawFlipForward, origin.z + direction.z * ClawFlipForward, ClawFlipHeight);
            Take(_clawFlip, ref _clawFlipCursor, tick, body, GroundBillboard(direction, RefClawDegrees), 1f);
        }

        /// <summary>
        /// Посадка: юбка и кольцо референса лежат на плоскости земли, столб с камнями и
        /// угольками стоит вертикальной вырезкой на точке контакта (владелец 26.09:
        /// билборд к камере «как будто поверх экрана», не на нашей земле).
        /// </summary>
        private void LandingImpact(FixVec2 at, int tick)
        {
            Vector3 point = Ground(at.X.ToFloat(), at.Y.ToFloat(), 0f);
            Take(_landingSkirt, ref _landingSkirtCursor, tick, point + Vector3.up * GroundLayerLift, GroundBillboard(Vector3.zero, 0f), 1f);
            Take(_landingColumn, ref _landingColumnCursor, tick, point + Vector3.up * .02f, VerticalBillboard(), 1f);
        }

        /// <summary>Отталкивание: выброс из-под каждой стопы назад по прыжку.</summary>
        private void Takeoff(int entity, WendigoActionState a)
        {
            var direction = new Vector3(a.Direction.X.ToFloat(), 0f, a.Direction.Y.ToFloat());
            Bones bones = BonesOf(entity);
            Vector3 origin = Ground(a.Origin.X.ToFloat(), a.Origin.Y.ToFloat(), 0f);
            Vector3 side = Vector3.Cross(Vector3.up, direction) * .32f;
            Vector3 left = bones?.LeftFoot != null ? bones.LeftFoot.position : origin - side;
            Vector3 right = bones?.RightFoot != null ? bones.RightFoot.position : origin + side;
            int tick = _driver.Sim.Tick;
            Vector3 between = Ground((left.x + right.x) * .5f, (left.z + right.z) * .5f, 0f);
            Take(_takeoffGround, ref _takeoffGroundCursor, tick, between + Vector3.up * GroundLayerLift, GroundBillboard(Vector3.zero, 0f), 1f);
            Take(_takeoffColumn, ref _takeoffColumnCursor, tick, between + Vector3.up * .02f, VerticalBillboard(), 1f);
        }

        private static Vector3 ClawDirection(Transform hand, Transform arm)
        {
            if (hand == null) return Vector3.forward;
            Vector3 dir = arm != null ? hand.position - arm.position : hand.forward;
            return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
        }

        private static Vector3 ClawTip(Transform hand, Transform arm) => hand.position + ClawDirection(hand, arm) * .42f;

        /// <summary>Лента когтей: семплирует правую кисть каждый кадр быстрого маха, потом дорисовывает хвост.</summary>
        private void UpdateSwings(Simulation sim, float tick)
        {
            float now = tick / Simulation.TicksPerSecond;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.TryGetWendigoAction(id, out var a) || a.Kind != WendigoAction.Claw) continue;
                if (tick < a.ImpactTick - SwingBeforeTicks || tick > a.ImpactTick + SwingAfterTicks) continue;
                if (!_swings.TryGetValue(id, out Swing swing))
                {
                    var go = new GameObject("Вендиго: лента когтей " + id);
                    go.transform.SetParent(transform, false);
                    swing = new Swing { Ribbon = go.AddComponent<WendigoClawRibbon>() };
                    _swings[id] = swing;
                }
                if (swing.Serial != a.Serial) { swing.Serial = a.Serial; swing.Ribbon.Begin(); swing.Sampling = true; }
                // Голова серпа: дуга вокруг тела зверя по рефу, а не кость кисти
                // (наш клип бьёт сверху вниз — по кости выходил вертикальный столб).
                float t = Mathf.InverseLerp(a.ImpactTick - SwingBeforeTicks, a.ImpactTick + SwingAfterTicks, tick);
                float angle = Mathf.Lerp(SweepStartDegrees, SweepEndDegrees, t) * Mathf.Deg2Rad;
                var forward = new Vector3(a.Direction.X.ToFloat(), 0f, a.Direction.Y.ToFloat());
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 radial = forward * Mathf.Cos(angle) + right * Mathf.Sin(angle);
                Vector3 center = _arena != null && _arena.TryGetEntityView(id, out Transform view) && view != null
                    ? view.position : Ground(a.Origin.X.ToFloat(), a.Origin.Y.ToFloat(), 0f);
                center = Ground(center.x, center.z, 0f);
                // Высота: сзади у пояса, впереди у земли — серп «черкает» по контакту.
                float height = .3f + .5f * (1f - Mathf.Cos(angle));
                swing.Ribbon.Sample(center + Vector3.up * height + radial * SweepRadius, radial, now);
            }
            foreach (var pair in _swings)
                if (pair.Value.Ribbon.Active) pair.Value.Ribbon.Rebuild(now);
        }

        private Bones BonesOf(int entity)
        {
            if (_arena == null || !_arena.TryGetEntityView(entity, out Transform view) || view == null) return null;
            if (_bones.TryGetValue(view, out Bones bones)) return bones;
            bones = new Bones();
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "L_hand") bones.LeftHand = t;
                else if (t.name == "R_hand") bones.RightHand = t;
                else if (t.name == "L_arm_lower") bones.LeftArm = t;
                else if (t.name == "R_arm_lower") bones.RightArm = t;
                else if (t.name == "L_foot") bones.LeftFoot = t;
                else if (t.name == "R_foot") bones.RightFoot = t;
            }
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
            foreach (var swing in _swings.Values) if (swing.Ribbon != null) Destroy(swing.Ribbon.gameObject);
            foreach(var m in _marks)if(m!=null){Destroy(m.Mesh);Destroy(m.Root);}if(_material!=null)Destroy(_material);
            foreach (var pool in new[] { _clawFlip, _landingSkirt, _landingColumn, _takeoffGround, _takeoffColumn })
                if (pool != null) foreach (var b in pool) if (b != null) Destroy(b.Root);
        }
    }
}
