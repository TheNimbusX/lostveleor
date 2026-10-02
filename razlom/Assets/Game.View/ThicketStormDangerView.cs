using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// БУРЯ ЦВЕТЕНИЯ: ГДЕ УРОН, ГДЕ УКРЫТИЕ (владелец 02.10, п. 12: «буря цветения не особо
    /// читается где урон а где сейв зона»). Пока волна бури впереди, весь пол поляны босса — одна
    /// метка удара нашим общим языком (GroundTelegraphStyle.hlsl: красная заливка, растущая от
    /// босса к краям поляны к тику удара, светящийся пунктир с тёмной обводкой, шевроны — здесь
    /// остриём к ближнему укрытию), а круги света вырезаны из неё чисто и обведены золотом. На
    /// ударе — вспышка, между волнами и после бури — угасание. Шейдер — Razlom/Thicket Storm Danger
    /// (Assets/Shaders/ThicketStormDanger.shader), время и пол — ThicketStormDangerRules.
    ///
    /// Круги — из Sim, те же, по которым считается урон (Simulation.ThicketStormSafeAt):
    /// TryGetThicketShape места 0–2 — волна 1, 3–5 — волна 2, радиус ThicketStormSafeRadius. Метки
    /// SafeZone общий вид не рисует (GroundTelegraphView их пропускает), столбы света над кругами
    /// остаются эффектом боя (ThicketMasterCombatView.Vfx, LightPillar).
    ///
    /// Поле — сетка на весь пол поляны (GladeRegion босса из карты забега; на стенде без поляны —
    /// пол 20 × 15 м вокруг его места) по высоте земли LayoutView.WeaponGroundHeight; край пола
    /// растворяется (доля в uv.x вершины). Без света, прозрачное, под всеми прозрачными эффектами
    /// и метками (очередь Transparent−15), персонажи закрывают его глубиной. Сетка строится при
    /// первой встрече с боссом, а не в бою; в бою каждый кадр меняются только векторы материала.
    ///
    /// Ставит ThicketMasterCombatView (Awake) одной строкой <see cref="EnsureOn"/>.
    /// </summary>
    [DefaultExecutionOrder(665)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ThicketStormDangerView : MonoBehaviour
    {
        /// <summary>Материал в Resources (создаёт ThicketStormDangerSetup): иначе шейдер не попадёт в сборку плеера.</summary>
        public const string MaterialResource = "VFX/ThicketMaster/StormDanger";
        public const string ShaderName = "Razlom/Thicket Storm Danger";

        /// <summary>Шаг сетки поля, м: край пола растворяется по вершинам, а земля под полем ровная.</summary>
        private const float CellMetres = .5f;

        /// <summary>Над землёй — чуть ниже общих меток (0,055): они ложатся поверх поля.</summary>
        private const float GroundLift = .05f;

        private const float FallbackFillMetres = 12f;

        private static readonly int WaveAId = Shader.PropertyToID("_WaveA"), WaveBId = Shader.PropertyToID("_WaveB"),
            SourceId = Shader.PropertyToID("_Source"), FloorId = Shader.PropertyToID("_Floor");

        private static readonly int[] SafeIds =
        {
            Shader.PropertyToID("_Safe0"), Shader.PropertyToID("_Safe1"), Shader.PropertyToID("_Safe2"),
            Shader.PropertyToID("_Safe3"), Shader.PropertyToID("_Safe4"), Shader.PropertyToID("_Safe5"),
        };

        private TickDriver _driver;
        private LayoutView _layout;
        private ThicketMasterCombatView _combat;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;

        private Material _material;
        private bool _materialTried;
        private GameObject _root;
        private Mesh _mesh;
        private bool _built;
        /// <summary>Вершины сетки на плоскости и доля «поле есть» — для длины заливки.</summary>
        private float[] _gx = new float[0], _gz = new float[0], _gm = new float[0];
        /// <summary>Пол для шейдера: центр x, z и полуоси, м — шевроны не встают у самого края.</summary>
        private Vector4 _floor;

        private readonly ThicketStormDangerRules.Wave[] _waves = new ThicketStormDangerRules.Wave[Simulation.ThicketStormWaves];
        /// <summary>Укрытия для шейдера: x, z, радиус, есть ли (держатся и после того, как Sim их снял, — на угасание).</summary>
        private readonly Vector4[] _safe = new Vector4[ThicketStormDangerRules.Slots];
        private int _stormSerial;
        private Vector2 _source;
        private float _fillLength = FallbackFillMetres;
        private bool _fillDirty = true;

        public static ThicketStormDangerView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<ThicketStormDangerView>();
            return view != null ? view : host.AddComponent<ThicketStormDangerView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            _combat = GetComponent<ThicketMasterCombatView>();
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            // Новая симуляция, новый Разлом или общий сброс: тики и круги начались заново, пол — другой.
            if (!ReferenceEquals(sim, _shown) || _generation != _driver.Generation || depth != _depth)
            {
                ForgetStorm();
                _shown = sim;
                _generation = _driver.Generation;
                _depth = depth;
                _built = false;
            }
            if (sim == null) { Show(false); return; }

            int boss = BossOf(sim);
            // Сетка — при первой встрече с боссом (он ещё спит), не в бою.
            if (boss >= 0 && !_built) Build(sim, boss);
            Observe(sim, boss);

            float tick = sim.Tick - 1 + _driver.Alpha;
            var first = ThicketStormDangerRules.LookOf(_waves[0], tick);
            var second = ThicketStormDangerRules.LookOf(_waves[1], tick);
            if ((!first.Visible && !second.Visible) || !_built || _material == null) { Show(false); return; }

            if (_fillDirty)
            {
                _fillLength = ThicketStormDangerRules.FillReach(_source.x, _source.y, _gx, _gz, _gm, _gx.Length, FallbackFillMetres);
                _fillDirty = false;
            }
            _material.SetVector(WaveAId, new Vector4(first.Progress, first.Opacity, first.Flash, first.Rim));
            _material.SetVector(WaveBId, new Vector4(second.Progress, second.Opacity, second.Flash, second.Rim));
            for (int i = 0; i < SafeIds.Length; i++) _material.SetVector(SafeIds[i], _safe[i]);
            _material.SetVector(SourceId, new Vector4(_source.x, _source.y, _fillLength, 0f));
            _material.SetVector(FloorId, _floor);
            Show(true);
        }

        // ------------------------------------------------------------ Sim → волны

        private int BossOf(Simulation sim)
        {
            if (_combat == null) TryGetComponent(out _combat);
            if (_combat != null) return _combat.Boss;
            var entities = sim.Entities;
            for (int id = 1; id < entities.Count; id++)
                if (entities.Kind[id] == EnemyKind.ForestThicketMaster) return id;
            return -1;
        }

        /// <summary>
        /// Волны идущей бури из Sim: круги, начало и удар каждой. Бури нет (кончилась, снята,
        /// босса нет) — ждавшие удара волны сняты в этот тик, ударившие доживают вспышку.
        /// </summary>
        private void Observe(Simulation sim, int boss)
        {
            int now = sim.Tick - 1;
            ThicketMasterState a = default;
            bool storm = boss >= 0 && sim.TryGetThicketMasterAction(boss, out a) && a.Action == ThicketMasterAction.Storm;
            if (!storm)
            {
                for (int w = 0; w < _waves.Length; w++) ThicketStormDangerRules.Lose(ref _waves[w], now);
                return;
            }
            if (a.Serial != _stormSerial)
            {
                // Новая буря: прошлые волны прочь. Заливка — от тела босса (в бурю он стоит).
                ForgetStorm();
                _stormSerial = a.Serial;
                FixVec2 at = sim.Entities.Position[boss];
                _source = new Vector2(at.X.ToFloat(), at.Y.ToFloat());
                _fillDirty = true;
            }
            float radius = Simulation.ThicketStormSafeRadius.ToFloat();
            // Волна 1 считает от начала бури, волна 2 — от удара первой (в тот тик встают её круги).
            int start = a.StartTick;
            for (int w = 0; w < _waves.Length; w++)
            {
                bool any = false, resolved = false;
                int impact = 0;
                for (int k = 0; k < Simulation.ThicketStormSafeCircles; k++)
                {
                    int slot = ThicketStormDangerRules.SlotOf(w, k);
                    if (!sim.TryGetThicketShape(boss, slot, out FixVec2 c, out int hit, out bool done)) continue;
                    _safe[slot] = new Vector4(c.X.ToFloat(), c.Y.ToFloat(), radius, 1f);
                    if (!any) { impact = hit; resolved = done; }
                    any = true;
                }
                if (any) ThicketStormDangerRules.Observe(ref _waves[w], start, impact, resolved);
                else ThicketStormDangerRules.Lose(ref _waves[w], now);
                start = _waves[w].Present ? _waves[w].ImpactTick : a.StageStartTick;
            }
        }

        private void ForgetStorm()
        {
            for (int w = 0; w < _waves.Length; w++) _waves[w] = default;
            for (int i = 0; i < _safe.Length; i++) _safe[i] = Vector4.zero;
            _stormSerial = 0;
            _fillDirty = true;
        }

        // ------------------------------------------------------------ поле

        /// <summary>Пол поляны, на которой стоит босс; стенд без поляны — пол 20 × 15 м вокруг его места.</summary>
        private GladeRegion FloorOf(Simulation sim, int boss)
        {
            FixVec2 home = sim.TryGetThicketMasterMemory(boss, out ThicketMasterMemory m) ? m.Home : sim.Entities.Position[boss];
            var map = _driver.Run != null ? _driver.Run.Map : null;
            if (map != null)
                for (int k = 0; k < map.GladeCount; k++)
                {
                    var glade = map.GetGlade(k);
                    if (glade.Field(home) <= Fix64.One) return glade;
                }
            return new GladeRegion(home, GladeLayout.BossClearingRadii, GladeShape.Rounded);
        }

        /// <summary>
        /// Сетка поля на прямоугольник поляны с шагом CellMetres: вершины на земле, в uv.x — доля
        /// «поле есть» (ThicketStormDangerRules.FloorMask). Квадраты целиком за краем не рисуются.
        /// </summary>
        private void Build(Simulation sim, int boss)
        {
            if (!_materialTried)
            {
                _materialTried = true;
                _material = LoadMaterial();
            }
            if (_material == null) return;
            GladeRegion glade = FloorOf(sim, boss);
            float cx = glade.Center.X.ToFloat(), cz = glade.Center.Y.ToFloat();
            float rx = Mathf.Max(1f, glade.Radii.X.ToFloat()), rz = Mathf.Max(1f, glade.Radii.Y.ToFloat());
            // Скруглённый пол босса заполняет 0,86 рамки поляны (GladeLayout.BossClearingRadii); прочие — с запасом.
            float floor = glade.Shape == GladeShape.Rounded ? .86f : .75f;
            _floor = new Vector4(cx, cz, rx * floor, rz * floor);

            int nx = Mathf.Clamp(Mathf.CeilToInt(2f * rx / CellMetres), 2, 160);
            int nz = Mathf.Clamp(Mathf.CeilToInt(2f * rz / CellMetres), 2, 160);
            int stride = nx + 1, count = stride * (nz + 1);
            var vertices = new Vector3[count];
            var uvs = new Vector2[count];
            _gx = new float[count]; _gz = new float[count]; _gm = new float[count];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    int i = z * stride + x;
                    float px = cx - rx + 2f * rx * x / nx, pz = cz - rz + 2f * rz * z / nz;
                    float mask = ThicketStormDangerRules.FloorMask(glade, px, pz);
                    float y = (_layout != null ? _layout.WeaponGroundHeight(px, pz) : 0f) + GroundLift;
                    vertices[i] = new Vector3(px, y, pz);
                    uvs[i] = new Vector2(mask, 0f);
                    _gx[i] = px; _gz[i] = pz; _gm[i] = mask;
                }
            int quads = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                    if (QuadShown(z * stride + x, stride)) quads++;
            var indices = new int[quads * 6];
            int at = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int a = z * stride + x, b = a + 1, c = a + stride, d = c + 1;
                    if (!QuadShown(a, stride)) continue;
                    indices[at++] = a; indices[at++] = c; indices[at++] = b;
                    indices[at++] = b; indices[at++] = c; indices[at++] = d;
                }

            if (_root == null)
            {
                _root = new GameObject("Буря цветения: поле опасности");
                _root.transform.SetParent(transform, false);
                _mesh = new Mesh { name = "Буря цветения: поле опасности" };
                _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = _root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _root.SetActive(false);
            }
            // Вершины — в координатах мира, как у общих меток.
            _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _mesh.Clear();
            _mesh.indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = indices;
            _mesh.RecalculateBounds();
            _built = true;
            _fillDirty = true;
        }

        private bool QuadShown(int a, int stride)
            => _gm[a] > 0f || _gm[a + 1] > 0f || _gm[a + stride] > 0f || _gm[a + stride + 1] > 0f;

        private static Material LoadMaterial()
        {
            var asset = Resources.Load<Material>(MaterialResource);
            if (asset != null) return new Material(asset);
            var shader = Shader.Find(ShaderName);
            if (shader != null) return new Material(shader);
            Debug.LogWarning($"[thicketmaster-vfx] Буря цветения: нет ни Resources/{MaterialResource}, ни шейдера {ShaderName} — " +
                "поле опасности бури не будет видно (материал создаёт «Разлом/Босс/Хозяин Чащи/Материал поля бури»).");
            return null;
        }

        private void Show(bool on)
        {
            if (_root != null && _root.activeSelf != on) _root.SetActive(on);
        }

        private void OnDisable() => Show(false);

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_root != null) Destroy(_root);
            if (_material != null) Destroy(_material);
        }
    }
}
