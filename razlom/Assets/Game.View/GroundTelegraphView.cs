using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Общие метки ударов на земле: рисует из общего списка Sim только те,
    /// у которых стоит TelegraphFlags.SharedView. Остальные метки живут в
    /// симуляции для жетонов и проверок, а на земле их пока рисуют
    /// собственные виды Вендиго и Камнекопыта — дважды одну метку не рисуем.
    ///
    /// Стиль один на всех — утверждённый GroundTelegraphStyle.hlsl (коралловая
    /// кромка, тёмная обводка, полупрозрачная заливка). Заливка — обратный
    /// отсчёт по тикам Sim: (тик − StartTick) / (ImpactTick − StartTick) с
    /// подкадром драйвера, поэтому пауза и съёмка держат кадр сами.
    ///
    /// ФИГУРА БЕРЁТСЯ ИЗ МЕТКИ, А НЕ ИЗ МОБА. Урон считается по тем же полям
    /// (Simulation.TelegraphContains), и нарисованное с настоящим разъехаться
    /// не может: здесь нет ни одной своей константы размера.
    ///
    /// Меши — пулы, собранные в Awake; геометрия строится один раз при
    /// появлении метки (вершины ложатся на землю), дальше каждый кадр меняется
    /// только MaterialPropertyBlock. Кадровых аллокаций нет.
    /// </summary>
    [DefaultExecutionOrder(630)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class GroundTelegraphView : MonoBehaviour
    {
        private sealed class Mark
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public int Slot = -1, Serial, StartTick, Source;
        }

        // Размеры пулов: Хранители держат по метке на замах (жетонов на замах
        // два, но метки доживают вспышку), круги — плоды и прыжки, полосы —
        // тараны, кольца — будущий вой. Кончилось место — метка не рисуется,
        // а урон от этого не меняется: невидимый удар хуже любого.
        //
        // Новые мобы леса (26.09): линия Шипомёта — до четырёх полос разом,
        // плюс таран и ещё гаснущие метки прошлой линии: 12 с запасом. Круги —
        // всплеск Шипомёта и корни Корнехвата поверх плодов и прыжков, отсюда
        // 16. EnemyTelegraphTests держит худший шаблон в этих пределах.
        private const int SectorCount = 16, CircleCount = 16, LaneCount = 12, RingCount = 4;

        // Сетка: по углу — гладкая дуга, по радиусу — шаг, чтобы метка ложилась
        // на неровную землю, а не висела хордой над кочкой.
        private const int ArcSegments = 64, RadialSegments = 8;
        private const int LaneAcross = 4, LaneAlong = 64;

        /// <summary>Подъём над землёй, как у меток Вендиго: без мерцания с грунтом.</summary>
        private const float GroundLift = .055f;

        /// <summary>
        /// Сработавшая метка гаснет за три тика: по утверждённому рефу метка
        /// исчезает ровно на касании, как у Вендиго. Снятая (оглушение,
        /// смерть) гаснет медленнее, весь свой остаток TelegraphLingerTicks, —
        /// игрок должен успеть увидеть, что удара не будет.
        /// </summary>
        private const float ResolvedFadeTicks = 3f;

        private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
            Radius = Shader.PropertyToID("_Radius"), InnerRadius = Shader.PropertyToID("_InnerRadius"), Span = Shader.PropertyToID("_Span"),
            Length = Shader.PropertyToID("_Length"), Width = Shader.PropertyToID("_Width"), Consumed = Shader.PropertyToID("_Consumed");

        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;
        /// <summary>Пулы фигур: общий каркас индексов, свой материал, фиксированное число мест.</summary>
        private Mark[] _sectors, _circles, _lanes, _rings;
        private Material _sectorMaterial, _laneMaterial;
        /// <summary>Какая метка пула показывает слот Sim; null — слот не показан.</summary>
        private Mark[] _bySlot = new Mark[0];

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>();
            // Материал сектора создаёт GroundTelegraphSetup в Resources: только
            // так шейдер попадает в сборку плеера. В редакторе хватает и поиска.
            _sectorMaterial = Load("VFX/Telegraphs/EnemySector", "Razlom/Ground Telegraph Sector");
            _laneMaterial = Load("VFX/Telegraphs/EnemyLane", "Razlom/Ground Telegraph Lane");
            _sectors = Build("Метка: сектор ", SectorCount, ArcSegments, RadialSegments, _sectorMaterial);
            _circles = Build("Метка: круг ", CircleCount, ArcSegments, RadialSegments, _sectorMaterial);
            _rings = Build("Метка: кольцо ", RingCount, ArcSegments, RadialSegments, _sectorMaterial);
            _lanes = Build("Метка: полоса ", LaneCount, LaneAcross, LaneAlong, _laneMaterial);
        }

        private static Material Load(string resource, string shader)
        {
            var asset = Resources.Load<Material>(resource);
            if (asset != null) return new Material(asset);
            var found = Shader.Find(shader);
            if (found != null) return new Material(found);
            Debug.LogWarning($"[Разлом] Общие метки: нет ни {resource}, ни шейдера {shader} — фигура не будет видна.");
            return null;
        }

        private Mark[] Build(string name, int count, int columns, int rows, Material material)
        {
            var pool = new Mark[count];
            int stride = columns + 1;
            var indices = new int[columns * rows * 6]; int at = 0;
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int a = y * stride + x, b = a + 1, c = a + stride, d = c + 1;
                indices[at++] = a; indices[at++] = c; indices[at++] = b;
                indices[at++] = b; indices[at++] = c; indices[at++] = d;
            }
            for (int i = 0; i < count; i++)
            {
                var m = new Mark
                {
                    Root = new GameObject(name + i), Mesh = new Mesh { name = name + i },
                    Vertices = new Vector3[stride * (rows + 1)], Uvs = new Vector2[stride * (rows + 1)]
                };
                m.Root.transform.SetParent(transform, false);
                m.Mesh.MarkDynamic(); m.Mesh.vertices = m.Vertices; m.Mesh.uv = m.Uvs; m.Mesh.triangles = indices;
                m.Root.AddComponent<MeshFilter>().sharedMesh = m.Mesh;
                m.Renderer = m.Root.AddComponent<MeshRenderer>();
                m.Renderer.sharedMaterial = material;
                m.Renderer.shadowCastingMode = ShadowCastingMode.Off; m.Renderer.receiveShadows = false;
                m.Root.SetActive(false);
                pool[i] = m;
            }
            return pool;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Смена симуляции, новый Разлом или общий сброс: номера меток в Sim
            // начинаются заново, и старое соответствие слот → метка врёт.
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                ClearAll(); _shown = sim; _generation = _driver.Generation; _depth = depth;
                if (sim != null && _bySlot.Length < sim.TelegraphCapacity) _bySlot = new Mark[sim.TelegraphCapacity];
            }
            if (sim == null) return;
            float tick = sim.Tick - 1 + _driver.Alpha;

            // Показанные: доживают, пока слот держит ту же метку.
            Refresh(_sectors, sim, tick); Refresh(_circles, sim, tick);
            Refresh(_lanes, sim, tick); Refresh(_rings, sim, tick);
            // Новые: только со флагом общего вида.
            int high = Mathf.Min(sim.TelegraphHighWater, _bySlot.Length);
            for (int slot = 0; slot < high; slot++)
            {
                if (_bySlot[slot] != null || !sim.TryGetTelegraph(slot, out var t) || !t.SharedView) continue;
                var m = Take(PoolFor(t.Shape));
                if (m == null) continue;
                Show(m, slot, in t);
                Drive(m, in t, tick);
            }
        }

        private void Refresh(Mark[] pool, Simulation sim, float tick)
        {
            foreach (var m in pool)
            {
                if (m.Slot < 0) continue;
                if (!sim.TryGetTelegraph(m.Slot, out var t) || !Same(m, t)) { Release(m); continue; }
                Drive(m, in t, tick);
            }
        }

        private static bool Same(Mark m, in EnemyTelegraph t)
            => t.Serial == m.Serial && t.StartTick == m.StartTick && t.Source == m.Source;

        private Mark[] PoolFor(TelegraphShape shape)
        {
            switch (shape)
            {
                case TelegraphShape.Sector: return _sectors;
                case TelegraphShape.Circle: return _circles;
                case TelegraphShape.Lane: return _lanes;
                case TelegraphShape.Ring: return _rings;
            }
            return null;
        }

        private static Mark Take(Mark[] pool)
        {
            if (pool == null) return null;
            foreach (var m in pool)
                if (m.Slot < 0 && m.Renderer.sharedMaterial != null) return m;
            return null;
        }

        /// <summary>
        /// Заливка и видимость по тикам. Пока метка открыта — обратный отсчёт до
        /// контакта. Сработала — полная и гаснет за три тика. Снята — заливка
        /// замирает там, где её оборвали, и гаснет за остаток задержки.
        /// </summary>
        private static void Drive(Mark m, in EnemyTelegraph t, float tick)
        {
            float span = Mathf.Max(1, t.ImpactTick - t.StartTick);
            float progress, opacity;
            if (t.State == TelegraphState.Active)
            {
                progress = Mathf.Clamp01((tick - t.StartTick) / span);
                opacity = 1f;
            }
            else
            {
                // Тик развязки: и снятие, и удар ставят EndTick = развязка + задержка.
                float finish = t.EndTick - Simulation.TelegraphLingerTicks;
                float age = Mathf.Max(0f, tick - finish);
                if (t.State == TelegraphState.Resolved)
                {
                    progress = 1f;
                    opacity = Mathf.Clamp01(1f - age / ResolvedFadeTicks);
                }
                else
                {
                    progress = Mathf.Clamp01((finish - t.StartTick) / span);
                    opacity = Mathf.Clamp01(1f - age / Simulation.TelegraphLingerTicks);
                }
            }
            m.Block.SetFloat(Progress, progress);
            m.Block.SetFloat(Opacity, opacity);
            m.Renderer.SetPropertyBlock(m.Block);
        }

        private void Show(Mark m, int slot, in EnemyTelegraph t)
        {
            m.Slot = slot; m.Serial = t.Serial; m.StartTick = t.StartTick; m.Source = t.Source;
            _bySlot[slot] = m;
            var origin = new Vector2(t.Origin.X.ToFloat(), t.Origin.Y.ToFloat());
            var direction = new Vector2(t.Direction.X.ToFloat(), t.Direction.Y.ToFloat());
            if (direction.sqrMagnitude < 1e-6f) direction = Vector2.up;
            direction.Normalize();
            m.Block.Clear();
            if (t.Shape == TelegraphShape.Lane) BuildLane(m, origin, direction, t.Length.ToFloat(), t.Width.ToFloat());
            else
            {
                float radius = t.Radius.ToFloat();
                float inner = t.Shape == TelegraphShape.Circle ? 0f : Mathf.Clamp(t.InnerRadius.ToFloat(), 0f, radius * .98f);
                // Раствор сектора — полный угол: ArcCos хранит косинус половины.
                float span = t.Shape == TelegraphShape.Sector
                    ? 2f * Mathf.Acos(Mathf.Clamp(t.ArcCos.ToFloat(), -1f, 1f)) : Mathf.PI * 2f;
                BuildArc(m, origin, direction, radius, inner, span);
            }
            m.Root.SetActive(true);
        }

        /// <summary>
        /// Дуга от inner до radius. UV: x — доля раствора, y — радиус в долях
        /// внешнего; кромки и заливку по ним считает шейдер сектора.
        /// </summary>
        private void BuildArc(Mark m, Vector2 origin, Vector2 direction, float radius, float inner, float span)
        {
            const int stride = ArcSegments + 1;
            float facing = Mathf.Atan2(direction.x, direction.y);
            for (int y = 0; y <= RadialSegments; y++)
            {
                float r = Mathf.Lerp(inner, radius, y / (float)RadialSegments);
                for (int x = 0; x <= ArcSegments; x++)
                {
                    float u = x / (float)ArcSegments, angle = facing + (u - .5f) * span;
                    m.Vertices[y * stride + x] = Ground(origin.x + Mathf.Sin(angle) * r, origin.y + Mathf.Cos(angle) * r);
                    m.Uvs[y * stride + x] = new Vector2(u, radius > 0f ? r / radius : 0f);
                }
            }
            Commit(m);
            m.Block.SetFloat(Radius, radius); m.Block.SetFloat(InnerRadius, inner); m.Block.SetFloat(Span, span);
        }

        /// <summary>Полоса от origin вдоль direction: как в Sim, от 0 до Length, поперёк ±Width/2.</summary>
        private void BuildLane(Mark m, Vector2 origin, Vector2 direction, float length, float width)
        {
            const int stride = LaneAcross + 1;
            var right = new Vector2(direction.y, -direction.x);
            for (int y = 0; y <= LaneAlong; y++)
            {
                float v = y / (float)LaneAlong;
                for (int x = 0; x <= LaneAcross; x++)
                {
                    float u = x / (float)LaneAcross;
                    Vector2 p = origin + direction * (v * length) + right * ((u - .5f) * width);
                    m.Vertices[y * stride + x] = Ground(p.x, p.y);
                    m.Uvs[y * stride + x] = new Vector2(u, v);
                }
            }
            Commit(m);
            m.Block.SetFloat(Length, length); m.Block.SetFloat(Width, width); m.Block.SetFloat(Consumed, 0f);
        }

        private Vector3 Ground(float x, float z)
            => new Vector3(x, (_layout != null ? _layout.WeaponGroundHeight(x, z) : 0f) + GroundLift, z);

        private static void Commit(Mark m)
        {
            m.Mesh.vertices = m.Vertices; m.Mesh.uv = m.Uvs; m.Mesh.RecalculateBounds();
        }

        private void Release(Mark m)
        {
            if (m.Slot >= 0 && m.Slot < _bySlot.Length && _bySlot[m.Slot] == m) _bySlot[m.Slot] = null;
            m.Slot = -1; m.Serial = 0;
            m.Root.SetActive(false);
        }

        private void ClearAll()
        {
            for (int i = 0; i < _bySlot.Length; i++) _bySlot[i] = null;
            foreach (var pool in new[] { _sectors, _circles, _lanes, _rings })
                if (pool != null) foreach (var m in pool) { m.Slot = -1; m.Serial = 0; m.Root.SetActive(false); }
        }

        private void OnDestroy()
        {
            foreach (var pool in new[] { _sectors, _circles, _lanes, _rings })
                if (pool != null) foreach (var m in pool) if (m != null) { Destroy(m.Mesh); Destroy(m.Root); }
            if (_sectorMaterial != null) Destroy(_sectorMaterial);
            if (_laneMaterial != null) Destroy(_laneMaterial);
        }
    }
}
