using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Рывок Пелага (Пробел, решение владельца 02.10) — «Пенный след»: кадр А
    /// (ART/characters/pelag/dash-2026-10-02/1-A-foam-wake.png) и маленький
    /// всплеск-корона у передней ноги из А5 (8-A5-wake-arrival-splash.png).
    /// Сборка префабов — Editor/PelagDashVfxSetup.cs.
    ///
    /// Всё рождается от событий Sim, а не опросом тиков:
    ///  • DashStarted — след ложится от точки старта и растёт вместе с героем
    ///    по его настоящему пути (стена обрывает и след); у задней ноги пока
    ///    он едет — пенный занос (клочья и брызги ниже колена); у старта
    ///    3–5 капель — старый конец следа рассыпается ими, а не иглой;
    ///  • DashEnded — длина следа = пройденный путь из Sim, белая корона
    ///    пены у передней ноги (А5), дальше след за ~0,4 с распадается
    ///    фронтом от старта к ногам: вода белеет пеной и рвётся на капли.
    /// Вспышек у рывка нет; свечение пены в следе идёт по настройке
    /// «Вспышки» (GameUserSettings.FlashScale), как свет серии сабли.
    /// Анимация героя — клип рывка (CharacterAnimatorView.Dash); корона встаёт
    /// под его переднюю ногу в тот миг, когда клип её ставит.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Следов разом: перезарядка 1,5 с длиннее жизни следа, второй — запас.</summary>
        private const int DashFoamSlots = 2;
        /// <summary>
        /// Корона брызг (А5) встаёт под переднюю ногу клипа рывка: левая стопа
        /// (mixamorig:LeftFoot) на кадре 6, когда она ставится. Кость читается
        /// с тела; если тела или кости нет — та же точка числом: впереди корня
        /// и левее, единицы рига при масштабе 1 (× масштаб тела). Замер пробы
        /// в редакторе 02.10 на игровом теле ×1,82: 0,444 м впереди и 0,131 м
        /// левее (timing.json в Blender — 0,476 и 0,163; разница — перенос на
        /// пропорции v6).
        /// </summary>
        private const float DashSplashAhead = .244f, DashSplashLeft = .072f;
        private const string DashSplashFootBone = "mixamorig:LeftFoot";
        /// <summary>Пока объект следа в пуле, его страховочный срок, с; гасит след PelagDashWake.Done.</summary>
        private const float DashWakeHold = PelagDashWake.MaxLife + .2f;

        private sealed class DashFoamRun
        {
            public readonly PelagDashWake Wake = new PelagDashWake();
            public bool Active;
            public int Serial;
            public int ActiveIndex;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem SkidFoam, SkidDrops, TailDrops;
            public Vector3 From, Direction;
            public float Ground, SkidAlong, FoamCarry, DropCarry;
        }

        private DashFoamRun[] _dashFoam;

        // Корона ждёт, пока тело встанет на экране (тик показа = тик остановки).
        private bool _dashSplashPending;
        private Simulation _dashSplashSim;
        private int _dashSplashStopTick;
        private Vector3 _dashSplashStop, _dashSplashDirection;
        private float _dashSplashGround;
        private Transform _dashSplashBody, _dashSplashFoot, _dashSplashScale;

        private bool DashFoamReady => _pools != null && (int)PelagVfxId.DashWake < _pools.Length
            && _pools[(int)PelagVfxId.DashWake] != null;

        /// <summary>Рывок начат: след ложится от точки старта, у старта — капли.</summary>
        private void PlayDashStarted(in SimEvent e)
        {
            if (CaptureRig.NoVfx || !DashFoamReady) return;
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            PelagDashState dash = sim.PelagDash;
            bool same = dash.Serial == e.ActionVariant;
            Vector3 direction = same ? SabreForward(dash.Direction) : PlayerFacing();
            float ground = PlayerPosition().y;
            var from = new Vector3(e.Position.X.ToFloat(), ground, e.Position.Y.ToFloat());
            float reach = same
                ? new Vector2((dash.To.X - dash.From.X).ToFloat(), (dash.To.Y - dash.From.Y).ToFloat()).magnitude
                : 4f;

            if (_dashFoam == null)
            {
                _dashFoam = new DashFoamRun[DashFoamSlots];
                for (int i = 0; i < _dashFoam.Length; i++) _dashFoam[i] = new DashFoamRun();
            }
            // Прежний след, если ещё растёт, встаёт где есть и гаснет сам.
            DashFoamRun run = null;
            foreach (DashFoamRun other in _dashFoam)
            {
                if (other.Active && !other.Wake.Ended) other.Wake.End(other.Wake.Length);
                if (run == null && !other.Active) run = other;
            }
            if (run == null)
            {
                run = _dashFoam[0];
                foreach (DashFoamRun other in _dashFoam) if (other.Wake.Age > run.Wake.Age) run = other;
                ReleaseDashFoam(run);
            }

            if (!TryAcquire(PelagVfxId.DashWake, out GameObject go, out PelagVfxElement element)) return;
            if (run.Object != go)
            {
                run.Object = go;
                Transform root = go.transform;
                run.Filter = root.Find("Wake")?.GetComponent<MeshFilter>();
                run.SkidFoam = root.Find("SkidFoam")?.GetComponent<ParticleSystem>();
                run.SkidDrops = root.Find("SkidDrops")?.GetComponent<ParticleSystem>();
                run.TailDrops = root.Find("TailDrops")?.GetComponent<ParticleSystem>();
            }
            int index = ReserveActive();
            element.Begin(from, Quaternion.LookRotation(direction, Vector3.up));
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.DashWake, Object = go, Element = element,
                Duration = DashWakeHold, Start = from, End = from, Motion = Motion.Static, FollowIndex = -1
            };
            run.Active = true;
            run.Serial = e.ActionVariant;
            run.ActiveIndex = index;
            run.From = from;
            run.Direction = direction;
            run.Ground = ground;
            run.SkidAlong = 0f;
            run.FoamCarry = .6f;
            run.DropCarry = .6f;
            run.Wake.Begin(reach, Random.Range(0f, 8f), GameUserSettings.FlashScale);
            if (run.Filter != null) run.Wake.Build(PelagDashWake.MeshFor(run.Filter));
            if (run.TailDrops != null) PelagDashWake.EmitTailDrops(run.TailDrops, from, direction, ground);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[dash-vfx] start tick={sim.Tick} serial={e.ActionVariant} from={from.ToString("F2")} dir={direction.ToString("F2")} reach={reach:F2}");
        }

        /// <summary>Тело встало: длина следа — пройденный путь, корона брызг у передней ноги.</summary>
        private void PlayDashEnded(in SimEvent e)
        {
            if (CaptureRig.NoVfx) return;
            float traveled = e.Amount / 100f;
            Vector3 direction = PlayerFacing();
            float ground = PlayerPosition().y;
            if (_dashFoam != null)
                foreach (DashFoamRun run in _dashFoam)
                {
                    if (!run.Active || run.Serial != e.ActionVariant) continue;
                    run.Wake.End(traveled);
                    direction = run.Direction;
                    ground = run.Ground;
                }
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[dash-vfx] end serial={e.ActionVariant} traveled={traveled:F2} cutShort={e.Flag}");

            // Корона — в миг, когда тело встанет на экране и клип поставит ногу
            // (CharacterAnimatorView.Dash: клип идёт по тику показа, тело рисуется
            // с отставанием на тик), а не в миг события: иначе брызги вставали
            // бы на 0,67 м впереди ещё летящего героя.
            Simulation sim = _driver.Sim;
            _dashSplashPending = sim != null;
            _dashSplashSim = sim;
            _dashSplashStopTick = sim == null ? 0
                : sim.PelagDash.Serial == e.ActionVariant && sim.PelagDash.StopTick >= 0 ? sim.PelagDash.StopTick : sim.Tick - 1;
            _dashSplashStop = new Vector3(e.Position.X.ToFloat(), ground, e.Position.Y.ToFloat());
            _dashSplashDirection = direction;
            _dashSplashGround = ground;
            UpdateDashSplash();
        }

        /// <summary>Тело встало на экране — корона у передней ноги.</summary>
        private void UpdateDashSplash()
        {
            if (!_dashSplashPending) return;
            Simulation sim = _driver.Sim;
            if (sim == null || sim != _dashSplashSim) { _dashSplashPending = false; return; }
            float shown = sim.Tick - 2 + _driver.Alpha;
            if (shown < _dashSplashStopTick) return;
            _dashSplashPending = false;
            if (!TryAcquire(PelagVfxId.DashSplash, out GameObject go, out PelagVfxElement element)) return;
            Vector3 foot = DashSplashFoot();
            int index = ReserveActive();
            element.Begin(foot, Quaternion.LookRotation(_dashSplashDirection, Vector3.up));
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.DashSplash, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = foot, End = foot, Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[dash-vfx] splash tick={sim.Tick} shown={shown:F2} stop={_dashSplashStopTick} foot={foot.ToString("F2")}"
                    + $" fromRoot={(foot - _dashSplashStop).ToString("F2")} bone={(_dashSplashFoot != null)}");
        }

        /// <summary>Где ставит ногу клип: кость левой стопы, иначе её замеренная точка (DashSplashAhead/Left).</summary>
        private Vector3 DashSplashFoot()
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && body != _dashSplashBody)
            {
                _dashSplashBody = body;
                _dashSplashFoot = null;
                foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    if (bone.name == DashSplashFootBone) { _dashSplashFoot = bone; break; }
                Animator animator = body.GetComponentInChildren<Animator>(true);
                _dashSplashScale = animator != null ? animator.transform : body;
            }
            if (_dashSplashBody != null && _dashSplashFoot != null && _dashSplashFoot.gameObject.activeInHierarchy)
            {
                Vector3 bone = _dashSplashFoot.position;
                return new Vector3(bone.x, _dashSplashGround + .02f, bone.z);
            }
            float scale = _dashSplashScale != null ? _dashSplashScale.lossyScale.y : 1f;
            Vector3 left = Vector3.Cross(_dashSplashDirection, Vector3.up);
            return _dashSplashStop + (_dashSplashDirection * DashSplashAhead + left * DashSplashLeft) * scale
                + Vector3.up * .02f;
        }

        /// <summary>След растёт за героем, пока он едет; занос у задней ноги; потом гаснет.</summary>
        private void UpdateDashFoam()
        {
            UpdateDashSplash();
            if (_dashFoam == null) return;
            float dt = Time.deltaTime;
            foreach (DashFoamRun run in _dashFoam)
            {
                if (!run.Active) continue;
                // Смерть героя и сброс арены возвращают всё в пул мимо нас.
                if ((uint)run.ActiveIndex >= (uint)_active.Length || !_active[run.ActiveIndex].Active
                    || _active[run.ActiveIndex].Object != run.Object)
                {
                    run.Active = false;
                    continue;
                }
                float along = Vector3.Dot(PlayerPosition() - run.From, run.Direction);
                run.Wake.Advance(dt, along);
                if (run.SkidFoam != null && run.SkidDrops != null && run.Wake.Length > run.SkidAlong)
                {
                    PelagDashWake.EmitSkid(run.SkidFoam, run.SkidDrops, run.From, run.Direction, run.Ground,
                        run.SkidAlong, run.Wake.Length, ref run.FoamCarry, ref run.DropCarry);
                    run.SkidAlong = run.Wake.Length;
                }
                if (run.Filter != null) run.Wake.Build(PelagDashWake.MeshFor(run.Filter));
                if (run.Wake.Done) ReleaseDashFoam(run);
            }
        }

        private void ReleaseDashFoam(DashFoamRun run)
        {
            if (run.Active && (uint)run.ActiveIndex < (uint)_active.Length
                && _active[run.ActiveIndex].Active && _active[run.ActiveIndex].Object == run.Object)
                Release(run.ActiveIndex);
            run.Active = false;
        }
    }

    /// <summary>
    /// Геометрия пенного следа рывка и выброс его частиц. Без MonoBehaviour:
    /// тем же кодом пользуется контроллер в бою и проба в редакторе.
    ///
    /// Меш — плоская полоса на земле в осях корня (корень в точке старта,
    /// местная +Z — направление рывка, +X — вправо): от чуть позади старта до
    /// ног героя, узкая у старта и широкая у ног. Каждый кадр пересобирается
    /// по длине, которую герой действительно проехал; у каждого участка — его
    /// возраст (когда герой там пробежал), по нему шейдер Razlom/Dash Foam
    /// Wake ведёт распад фронтом: старые участки у старта уходят первыми.
    /// </summary>
    public sealed class PelagDashWake
    {
        // Форма, м. Ширина у ног — около метра, как на кадрах А и А5.
        /// <summary>Меш начинается позади точки старта: туда уходит рваный хвост.</summary>
        public const float TailBack = .10f;
        /// <summary>
        /// След заходит под ноги дальше корня героя: конец меша на столько впереди
        /// корня, м. Передняя лодыжка клипа рывка (mixamorig:LeftFoot, точка короны)
        /// впереди корня на 0,44 м в миг постановки и на 0,46–0,56 м в разгоне, а
        /// рваный край носа (_HeadRag 0,30 × шум) съедает у видимой воды ~0,25 м
        /// от конца меша. С 0,76 видимый край воды в разгоне — у передней лодыжки,
        /// в миг, когда встаёт корона, — под стопой, чуть за лодыжкой (кадры А/А5:
        /// след доходит до ног, всплеск — на его конце). Было 0,16 — край воды на
        /// 0,4–0,6 м позади ноги (проверка 02.10, artifacts/tools/pelag-dash/wake-head).
        /// </summary>
        public const float HeadLead = .76f;
        /// <summary>На последних метрах у ног след сужается к носу, а не обрывается стенкой.</summary>
        public const float HeadRound = .30f;
        public const float HalfWidthTail = .17f;
        public const float HalfWidthHead = .60f;
        /// <summary>Над землёй, м: ниже щиколотки, тело героя его закрывает.</summary>
        public const float Lift = .03f;
        /// <summary>
        /// Поле меша за краем воды с каждой стороны, м: туда выпирают комья
        /// гребней (кадр А). Ширина самой воды — HalfWidthTail…HalfWidthHead.
        /// </summary>
        public const float Margin = .10f;

        // Время, с.
        /// <summary>После DashEnded длина доводится до пройденного пути, даже если герой отвернул.</summary>
        public const float SnapAfterEnd = .10f;
        /// <summary>
        /// Последний участок (у ног) уходит через столько после конца рывка:
        /// _Break.x + _Break.y/2 материала, с запасом; это и _FadeTo.
        /// </summary>
        public const float LifeAfterEnd = .42f;
        /// <summary>Страховка, если DashEnded так и не пришёл (сброс арены).</summary>
        public const float MaxLife = 1.4f;

        // Пенный занос у задней ноги, штук на метр пути; капли у старта.
        private const float SkidFoamPerMetre = 7f, SkidDropsPerMetre = 6f;
        private const float BackFoot = .10f;
        /// <summary>
        /// Край воды у ног, м (HalfWidthHead со скруглением носа): занос
        /// рождается на гребнях и летит наружу, а не ложится овалами внутрь
        /// следа — внутри на кадре А только струи.
        /// </summary>
        private const float SkidEdge = .48f;
        private static readonly Color TailDropColor = new Color(1.08f, 1.16f, 1.12f, 1f);

        private const int Rows = 30, Columns = 5;
        private const int MeshVertices = (Rows + 1) * Columns;
        private const string MeshName = "Рывок: пенный след";

        private readonly float[] _historyAge = new float[96];
        private readonly float[] _historyLength = new float[96];
        private int _historyCount;
        private readonly Vector3[] _vertices = new Vector3[MeshVertices];
        private readonly Vector4[] _uv0 = new Vector4[MeshVertices];
        private readonly Vector4[] _uv1 = new Vector4[MeshVertices];
        private static int[] _triangles;

        private float _cap, _seed, _glow;

        /// <summary>Сколько живёт след, с.</summary>
        public float Age { get; private set; }
        /// <summary>Сколько герой проехал от старта, м: до ног.</summary>
        public float Length { get; private set; }
        /// <summary>Возраст, на котором пришёл конец рывка; −1 — ещё едет.</summary>
        public float EndedAt { get; private set; } = -1f;
        public float FinalLength { get; private set; }
        public bool Ended => EndedAt >= 0f;
        public bool Done => Age >= MaxLife || (Ended && Age >= EndedAt + LifeAfterEnd + .02f);

        /// <param name="cap">Полная дальность рывка, м: длиннее след не растёт, пока нет DashEnded.</param>
        /// <param name="seed">Сдвиг шума — у каждого следа свой рисунок пены.</param>
        /// <param name="glow">Сила свечения пены (настройка «Вспышки»).</param>
        public void Begin(float cap, float seed, float glow)
        {
            _cap = Mathf.Max(.1f, cap);
            _seed = seed;
            _glow = glow;
            Age = 0f;
            Length = 0f;
            EndedAt = -1f;
            FinalLength = 0f;
            _historyCount = 0;
            Record();
        }

        /// <summary>Кадр: <paramref name="heroAlong"/> — где герой вдоль рывка, м от старта.</summary>
        public void Advance(float dt, float heroAlong)
        {
            Age += Mathf.Max(0f, dt);
            float length = Mathf.Max(Length, heroAlong);
            if (Ended)
            {
                if (Age - EndedAt >= SnapAfterEnd) length = FinalLength;
                length = Mathf.Min(length, FinalLength);
            }
            else length = Mathf.Min(length, _cap);
            Length = Mathf.Max(0f, length);
            Record();
        }

        /// <summary>Тело встало (DashEnded): дальше след не растёт и гаснет.</summary>
        public void End(float traveled)
        {
            if (Ended) return;
            EndedAt = Age;
            FinalLength = Mathf.Max(0f, traveled);
        }

        private void Record()
        {
            if (_historyCount > 0 && Length <= _historyLength[_historyCount - 1]) return;
            if (_historyCount == _historyAge.Length)
            {
                // Длинный рывок на медленном кадре: прореживаем через одну.
                for (int i = 1; i < _historyCount / 2; i++)
                {
                    _historyAge[i] = _historyAge[i * 2];
                    _historyLength[i] = _historyLength[i * 2];
                }
                _historyCount /= 2;
            }
            _historyAge[_historyCount] = Age;
            _historyLength[_historyCount] = Length;
            _historyCount++;
        }

        /// <summary>
        /// Сколько секунд назад герой пробежал точку <paramref name="s"/> (м от
        /// старта). У ног и под ними — сколько прошло, как след перестал расти:
        /// пока герой едет, это ноль, после конца рывка гаснет и голова.
        /// </summary>
        public float SegmentAge(float s)
        {
            if (_historyCount == 0) return 0f;
            if (s >= Length) return Age - _historyAge[_historyCount - 1];
            if (s <= _historyLength[0]) return Age - _historyAge[0];
            for (int i = 1; i < _historyCount; i++)
            {
                if (_historyLength[i] < s) continue;
                float span = _historyLength[i] - _historyLength[i - 1];
                float t = span > 1e-5f ? (s - _historyLength[i - 1]) / span : 1f;
                return Age - Mathf.Lerp(_historyAge[i - 1], _historyAge[i], t);
            }
            return 0f;
        }

        /// <summary>Свой меш у объекта следа: создаётся один раз и дальше переписывается.</summary>
        public static Mesh MeshFor(MeshFilter filter)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh != null && mesh.name == MeshName) return mesh;
            mesh = new Mesh { name = MeshName };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return mesh;
        }

        /// <summary>Пересобирает полосу следа в осях корня (старт, +Z — рывок).</summary>
        public void Build(Mesh mesh)
        {
            float headS = Length + HeadLead;
            float tailS = -TailBack;
            float span = Mathf.Max(.01f, headS - tailS);
            for (int r = 0; r <= Rows; r++)
            {
                float t = r / (float)Rows;
                float s = tailS + span * t;
                float halfWidth = Mathf.Lerp(HalfWidthTail, HalfWidthHead, Mathf.Pow(t, .9f));
                // Нос под ногами скруглён: без этого у ног след обрывался ровной стенкой.
                halfWidth *= Mathf.Lerp(.70f, 1f, Mathf.Sqrt(Mathf.Clamp01((headS - s) / HeadRound)));
                // Участок под ногами свеж, как сам герой.
                float age = SegmentAge(Mathf.Min(s, Length));
                // Меш шире воды на Margin: в шейдер поперёк идёт доля
                // полуширины воды, за ±1 — поле под комья пены.
                float outer = halfWidth + Margin;
                for (int c = 0; c < Columns; c++)
                {
                    float y = -1f + 2f * c / (Columns - 1);
                    int v = r * Columns + c;
                    _vertices[v] = new Vector3(y * outer, Lift, s);
                    _uv0[v] = new Vector4(s, y * outer / halfWidth, age, headS);
                    _uv1[v] = new Vector4(halfWidth, _seed, _glow, tailS);
                }
            }
            if (_triangles == null) _triangles = Triangles();
            bool fresh = mesh.vertexCount != MeshVertices;
            if (fresh) mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            if (fresh) mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        private static int[] Triangles()
        {
            var triangles = new int[Rows * (Columns - 1) * 6];
            int at = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns - 1; c++)
                {
                    int v = r * Columns + c;
                    triangles[at++] = v; triangles[at++] = v + Columns; triangles[at++] = v + 1;
                    triangles[at++] = v + 1; triangles[at++] = v + Columns; triangles[at++] = v + Columns + 1;
                }
            return triangles;
        }

        /// <summary>
        /// 3–5 капель за точкой старта по оси рывка, от крупной к мелкой: так
        /// старый конец следа рассыпается, а не сходится в иглу и не
        /// собирается в пенный шар (кадры А/А5). Лежат на земле и тают порогом.
        /// </summary>
        public static void EmitTailDrops(ParticleSystem system, Vector3 start, Vector3 direction, float ground)
        {
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            int count = Random.Range(3, 6);
            float back = .02f;
            for (int i = 0; i < count; i++)
            {
                float k = i / (float)Mathf.Max(1, count - 1);
                back += Random.Range(.08f, .18f);
                float lateral = Random.Range(-1f, 1f) * (HalfWidthTail * .9f + i * .05f);
                Vector3 position = start - direction * back + right * lateral;
                position.y = ground + .03f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = -direction * Random.Range(0f, .2f),
                    startSize = Mathf.Lerp(.13f, .06f, k) * Random.Range(.88f, 1.12f),
                    startLifetime = Random.Range(.30f, .40f),
                    startColor = TailDropColor,
                    applyShapeToPosition = false
                };
                system.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Пенный занос у задней ноги на пройденном отрезке
        /// [<paramref name="fromAlong"/>, <paramref name="toAlong"/>] м:
        /// клочья срываются с гребней и скользят наружу по земле, капли летят
        /// низко наружу — внутрь следа и выше колена ничего не попадает.
        /// </summary>
        public static void EmitSkid(ParticleSystem foam, ParticleSystem drops, Vector3 start, Vector3 direction,
            float ground, float fromAlong, float toAlong, ref float foamCarry, ref float dropCarry)
        {
            float distance = toAlong - fromAlong;
            if (distance <= 0f) return;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            foamCarry += distance * SkidFoamPerMetre;
            dropCarry += distance * SkidDropsPerMetre;
            int foamCount = (int)foamCarry, dropCount = (int)dropCarry;
            foamCarry -= foamCount;
            dropCarry -= dropCount;
            for (int i = 0; i < foamCount; i++)
            {
                float side = Random.value < .5f ? -1f : 1f;
                Vector3 position = start + direction * (Random.Range(fromAlong, toAlong) - BackFoot)
                    + right * side * SkidEdge * Random.Range(.90f, 1.12f);
                position.y = ground + .05f;
                foam.Emit(new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = -direction * Random.Range(.2f, .8f) + right * side * Random.Range(.6f, 1.4f)
                        + Vector3.up * Random.Range(.1f, .5f),
                    startSize = Random.Range(.14f, .28f),
                    startLifetime = Random.Range(.28f, .44f),
                    applyShapeToPosition = false
                }, 1);
            }
            for (int i = 0; i < dropCount; i++)
            {
                float side = Random.value < .5f ? -1f : 1f;
                Vector3 position = start + direction * (Random.Range(fromAlong, toAlong) - BackFoot)
                    + right * side * SkidEdge * Random.Range(.85f, 1.05f);
                position.y = ground + .06f;
                drops.Emit(new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = -direction * Random.Range(.4f, 1.4f) + right * side * Random.Range(1.0f, 2.0f)
                        + Vector3.up * Random.Range(1.0f, 2.0f),
                    startSize = Random.Range(.05f, .09f),
                    startLifetime = Random.Range(.22f, .34f),
                    applyShapeToPosition = false
                }, 1);
            }
        }
    }
}
