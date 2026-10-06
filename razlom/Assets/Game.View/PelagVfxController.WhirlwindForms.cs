using Game.Sim;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>
    /// Формы Вихря (владелец 02.10) — вид поверх принятой «морской пены».
    /// Целевые кадры выбраны владельцем (ART/characters/pelag/whirlwind-forms-2026-10-02/chatgpt-results):
    ///  • Буря (storm-2) — водяной столб из витков-полос вокруг героя, ломающийся
    ///    гребень сверху, брызги с кромки; у земли стенки прозрачнее, чтобы враги
    ///    внутри читались. Живёт от WhirlwindStormStarted до WhirlwindStormEnded,
    ///    на каждый оборот — выброс брызг, в конце — всплеск;
    ///  • Водоворот (vortex-1) — v3 (02.10, вечер: «больше по радиусу, чем бьёт»,
    ///    «стяжку не видно», «не плавная, резаная»): шесть рукавов живой воды
    ///    (PelagMaelstromWater) лежат на радиусе удара Вихря, а не на 4 м тяги,
    ///    и плавно наматываются внутрь; 4 м тяги — только тонкие струи, бегущие к
    ///    центру; за каждым врагом, которого тело Sim действительно тащит, —
    ///    пенный след (копия следа рывка) с заносом брызг у ног; на каждом
    ///    оглушённом в контакт — корона брызг;
    ///  • Пенные волны (waves-2) — v3 (02.10, вечер: «слишком линейные…
    ///    застывает… более водянистое»): два кольца живой воды (PelagFoamRingWater)
    ///    — волнистый гребень, полоса неровной ширины, выход с разгоном и
    ///    замедлением, после хода кольцо не встаёт, а дотекает, белеет пеной и
    ///    рвётся на капли; брызги летят с гребня, пока он бежит. Фронт Sim
    ///    (FoamRingFront) всегда внутри полосы воды; на каждом задетом враге —
    ///    белая корона брызг (WhirlwindFoamRingHit).
    /// v4 (проверка по выбранным кадрам): волны — полоса около метра с градиентом
    ///    глубины и крупной пеной по обоим краям, большой веер брызг выше пояса на
    ///    каждой цели, первое кольцо держится чистым, пока второе выходит, базовый
    ///    серп Вихря в этой форме не рисуется (на waves-2 его нет — кольцо 1 и есть
    ///    удар); рукава Водоворота — фиолетовая вода (градиент, струи, рваный гребень,
    ///    капли с концов), с первого кадра плотная; струи тяги — изогнутые струи
    ///    воды по спирали, следы тяги — пенная борозда с брызгами у ног. Вода форм
    ///    ложится поверх низких препятствий (колодец, корни), но не поверх тел.
    /// Лежащая вода берёт высоту земли под собой (FormGroundGrid): лагерь,
    /// уступы арены. Префабы — Editor/PelagWhirlwindFoamVfxSetup.Forms.cs. Вид
    /// ничего не решает за Sim: всё рождается от событий, обычный Вихрь без
    /// формы не меняется.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Сколько витков столба Бури ещё доживает после конца удержания, с.</summary>
        private const float StormColumnLinger = .55f;
        /// <summary>Страховочный срок столба: удержание дольше 3 с не бывает.</summary>
        private const float StormColumnMaxSeconds = 4.5f;
        /// <summary>Брызги на оборот Бури: основа и прибавка за каждого врага в круге (до пяти).</summary>
        private const int StormPulseDrops = 10, StormPulseDropsPerEnemy = 2;
        private const float MaelstromCrownScale = .9f;
        private const int FoamWaveSlots = 4;
        /// <summary>Пока брызги кольца ещё летят после конца жизни кольца, с.</summary>
        private const float FoamWaveSprayTail = .45f;
        /// <summary>
        /// Жизнь рукавов Водоворота под нарисованный контакт 0,33 с, с: распад после
        /// контакта кончается к ~0,8 с. Тяга дольше — жизнь дольше на столько же
        /// (PelagWhirlwindFormRules.MaelstromExtraLifeSeconds).
        /// </summary>
        private const float MaelstromSeconds = .95f;
        /// <summary>Капли, слетающие с внешних концов рукавов, штук в секунду (v4).</summary>
        private const float MaelstromTipDropRate = 90f;
        /// <summary>Пенная борозда за притянутым (v4): клочья пены и брызги на метр пути тела.</summary>
        private const float MaelstromFurrowFoamPerMetre = 9f, MaelstromFurrowDropsPerMetre = 14f;
        /// <summary>Гребни борозды от её середины, м: полоса следа рывка сужена в префабе до 0,55 (SaveMaelstromDrag).</summary>
        private const float MaelstromFurrowEdge = .27f;
        /// <summary>Веер брызг Пенных волн встаёт перед телом: корень сдвинут к камере вдоль луча, м.</summary>
        private const float WaveSplashCameraPush = .6f;
        /// <summary>Следов тяги разом; тянутых больше — следы у ближайших к началу списка.</summary>
        private const int MaelstromDragSlots = 8;
        /// <summary>
        /// Корень следа тяги позади старта тела: голова следа (PelagDashWake.HeadLead
        /// впереди длины) встаёт на 0,2 м перед телом, а не на 0,76 м, как у ног героя в рывке.
        /// </summary>
        private const float MaelstromDragBack = PelagDashWake.HeadLead - .20f;
        /// <summary>Сколько тело должно сдвинуться к герою, чтобы появился след, м (PelagWhirlwindFormRules.MaelstromDragStart).</summary>
        private const float MaelstromDragMin = PelagWhirlwindFormRules.MaelstromDragStart;
        /// <summary>След гаснет, если тело встало, когда оно уже проехало столько, м (прежние 2 × 6 см).</summary>
        private const float MaelstromDragStillAfter = .12f;

        private int _stormFx = -1;
        private GameObject _stormObject;
        private ParticleSystem[] _stormLoops;
        private ParticleSystem _stormPulse;
        private bool _stormEnding;
        private float _stormRaise;

        private int _maelstromContactTick = -1;
        private Vector3 _maelstromCentre;
        private int _maelstromFx = -1, _maelstromEventTick;
        private GameObject _maelstromObject;
        private MeshFilter _maelstromFilter, _maelstromStrandsFilter;
        private ParticleSystem _maelstromTipDrops, _maelstromFoam;
        private float _maelstromPullRadius, _maelstromTipCarry;
        private bool _maelstromFoamBurst, _maelstromStrandsDone;
        private readonly PelagMaelstromWater _maelstromWater = new PelagMaelstromWater();
        private readonly PelagMaelstromStrands _maelstromStrands = new PelagMaelstromStrands();
        private readonly FormGroundGrid _maelstromGround = new FormGroundGrid();

        /// <summary>След за телом, которое тянет Водоворот: ведётся по настоящему сдвигу тела, не по числу «притянуто».</summary>
        private struct MaelstromDragRun
        {
            public bool Active, Started;
            public int Target, Fx, StillFrames;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem SkidFoam, SkidDrops, TailDrops;
            public Vector3 Start, Direction, Root;
            public float Ground, LastShift, SkidAlong, FoamCarry, DropCarry;
        }

        private readonly MaelstromDragRun[] _maelstromDrags = new MaelstromDragRun[MaelstromDragSlots];
        private readonly PelagDashWake[] _maelstromDragWakes = new PelagDashWake[MaelstromDragSlots];

        private struct FoamWaveRun
        {
            public bool Active;
            public int Fx;
            public GameObject Object;
            public Vector3 Centre;
            public MeshFilter Filter;
            public ParticleSystem Spray, Foam;
            public float SprayCarry, FoamCarry;
            public bool BreakBurst;
        }

        private readonly FoamWaveRun[] _foamWaves = new FoamWaveRun[FoamWaveSlots];
        private readonly PelagFoamRingWater[] _foamWaveWater = new PelagFoamRingWater[FoamWaveSlots];
        private readonly FormGroundGrid[] _foamWaveGround = new FormGroundGrid[FoamWaveSlots];
        private readonly Vector3[] _foamRingCentres = new Vector3[Simulation.FoamRingCount];
        /// <summary>Враг, которого только что задело кольцо: следующий его Damage — от кольца, не от клинка.</summary>
        private int _foamRingDamageTarget = -1;

        /// <summary>Событие форм Вихря. True — разобрано здесь (оглушение — нет: его видят и другие).</summary>
        private bool ConsumeWhirlwindFormEvent(in SimEvent e, int eventTick)
        {
            switch (e.Type)
            {
                case SimEventType.WhirlwindStormStarted: BeginStormColumn(); return true;
                case SimEventType.WhirlwindStormPulse: PulseStormColumn(e.Amount); return true;
                case SimEventType.WhirlwindStormEnded: EndStormColumn(true); return true;
                case SimEventType.WhirlwindMaelstromPull: PlayMaelstrom(e, eventTick); return true;
                case SimEventType.WhirlwindFoamRing: BeginFoamWave(e, eventTick); return true;
                case SimEventType.WhirlwindFoamRingHit: PlayFoamRingHit(e); return true;
                case SimEventType.Stun:
                    if (PelagWhirlwindFormRules.IsMaelstromStagger(eventTick, e.Amount, _maelstromContactTick))
                        PlayCrownSplash(e.Target, e.Position, _maelstromCentre, MaelstromCrownScale, true);
                    return false;
                default: return false;
            }
        }

        /// <summary>
        /// Удар кольца Пенных волн: Damage сразу за WhirlwindFoamRingHit по тому же
        /// врагу. Его брызги — корона кольца, а не всплеск клинка, стоп-кадра и света героя нет.
        /// </summary>
        private bool TakeFoamRingDamage(in SimEvent e)
        {
            if (_foamRingDamageTarget < 0 || e.Target != _foamRingDamageTarget || !IsWhirlwindSlot(e.ActionVariant)) return false;
            _foamRingDamageTarget = -1;
            return true;
        }

        /// <summary>Тик Sim, на котором родилось событие кадра (FrameEventContext ставит тик после шага).</summary>
        private int EventTick(int index)
        {
            var contexts = _driver.FrameEventContexts;
            if (contexts != null && index < contexts.Count) return contexts[index].SimulationTick - 1;
            return _driver.Sim != null ? _driver.Sim.Tick - 1 : 0;
        }

        private void UpdateWhirlwindForms()
        {
            Simulation sim = _driver.Sim;
            if (_stormFx >= 0)
            {
                if (!StillActive(_stormFx, _stormObject)) ForgetStormColumn();
                else
                {
                    _active[_stormFx].Object.transform.position = PlayerPosition() + Vector3.up * _stormRaise;
                    // Страховка: Sim сбросили (смена комнаты, новый бой) без события конца.
                    if (!_stormEnding && (sim == null || !sim.WhirlwindStorming)) EndStormColumn(false);
                }
            }
            if (sim == null) return;
            float now = sim.Tick - 1 + _driver.Alpha;
            float dt = Time.deltaTime;
            UpdateMaelstrom(now, dt);
            for (int i = 0; i < _foamWaves.Length; i++)
            {
                ref FoamWaveRun run = ref _foamWaves[i];
                if (!run.Active) continue;
                if (!StillActive(run.Fx, run.Object)) { run = default; continue; }
                UpdateFoamWave(ref run, _foamWaveWater[i], _foamWaveGround[i], now, dt);
            }
        }

        /// <summary>Смерть героя и сброс: все объекты форм уже отпущены вместе с остальными.</summary>
        private void StopWhirlwindForms()
        {
            ForgetStormColumn();
            for (int i = 0; i < _foamWaves.Length; i++) _foamWaves[i] = default;
            for (int i = 0; i < _maelstromDrags.Length; i++) _maelstromDrags[i] = default;
            ForgetMaelstrom();
            _foamRingDamageTarget = -1;
            _maelstromContactTick = -1;
        }

        // ---- Земля под лежащей водой (02.10: «в лагере нижние слои проваливаются сквозь землю»)

        /// <summary>Высота ног героя — запасная земля, если ни навигации, ни арены нет.</summary>
        private float _formGroundBase;
        private System.Func<float, float, float> _formGround;

        /// <summary>
        /// Земля в точке: в лагере — навигация (рельеф лагеря неровный, а ноги героя
        /// держат одну высоту), в разломе — пол показанной арены с уступами и
        /// рельефом за краем (LayoutView.WeaponGroundHeight, как у брошенного оружия).
        /// </summary>
        private float FormGroundAt(float x, float z)
        {
            GameSession session = _driver.Session;
            if (session != null && session.Mode == GameMode.Camp)
            {
                // Навигация рядом с мостом или настилом может дать другой ярус: держим рельеф у ног героя.
                if (NavMesh.SamplePosition(new Vector3(x, _formGroundBase + 1f, z), out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    return Mathf.Clamp(hit.position.y, _formGroundBase - .6f, _formGroundBase + .8f);
                return _formGroundBase;
            }
            LayoutView layout = LayoutView.Shown;
            return layout != null ? layout.WeaponGroundHeight(x, z) : _formGroundBase;
        }

        /// <summary>Сетка земли под эффектом радиуса <paramref name="radius"/>; центр — на земле.</summary>
        private Vector3 SampleFormGround(FormGroundGrid grid, Vector3 centre, float radius)
        {
            _formGroundBase = centre.y;
            if (_formGround == null) _formGround = FormGroundAt;
            grid.Sample(centre, radius, _formGround);
            return new Vector3(centre.x, grid.Centre, centre.z);
        }

        /// <summary>
        /// Подъём стоящего столба Бури над ногами героя: если земля в круге выше ног
        /// (склон, кочка лагеря), нижние витки не уходят в неё. На ровном полу — ноль.
        /// </summary>
        private float FormRaise(Vector3 feet, float radius)
        {
            _formGroundBase = feet.y;
            float highest = FormGroundAt(feet.x, feet.z);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                for (int ring = 0; ring < 2; ring++)
                {
                    float r = radius * (ring == 0 ? .55f : .95f);
                    highest = Mathf.Max(highest, FormGroundAt(feet.x + Mathf.Cos(angle) * r, feet.z + Mathf.Sin(angle) * r));
                }
            }
            return Mathf.Clamp(highest - feet.y - .12f, 0f, .45f);
        }

        private bool StillActive(int fx, GameObject go)
            => fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;

        // ---- Цвета форм (02.10, как иконки: «бурю более стальную, водоворот более фиолет») ----

        private static readonly int ShadeId = Shader.PropertyToID("_Shade");
        private static MaterialPropertyBlock _formTintBlock;

        internal static bool WhirlwindFormWater(PelagForm form, out Color deep, out Color water, out Color shallow, out Color shade)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm:
                    deep = new Color(.16f, .19f, .23f); water = new Color(.42f, .48f, .55f); shallow = new Color(.70f, .75f, .80f); shade = new Color(.72f, .77f, .82f); return true;
                case PelagForm.WhirlwindMaelstrom:
                    deep = new Color(.12f, .04f, .30f); water = new Color(.36f, .16f, .70f); shallow = new Color(.66f, .48f, .95f); shade = new Color(.70f, .58f, .95f); return true;
                case PelagForm.WhirlwindFoamWaves:
                    deep = new Color(.02f, .30f, .20f); water = new Color(.10f, .70f, .48f); shallow = new Color(.50f, .95f, .76f); shade = new Color(.62f, .95f, .80f); return true;
                default:
                    deep = water = shallow = shade = Color.white; return false;
            }
        }

        /// <summary>Брызги и клочья префаба формы — общие материалы пены: оттенок формы блоком.</summary>
        private static void TintFormDroplets(GameObject go, PelagForm form)
        {
            if (!WhirlwindFormWater(form, out _, out _, out _, out Color shade)) return;
            if (_formTintBlock == null) _formTintBlock = new MaterialPropertyBlock();
            foreach (ParticleSystemRenderer r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || !m.HasProperty(ShadeId)) continue;
                r.GetPropertyBlock(_formTintBlock);
                _formTintBlock.SetColor(ShadeId, shade);
                r.SetPropertyBlock(_formTintBlock);
            }
        }

        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int WaterId = Shader.PropertyToID("_Water");
        private static readonly int ShallowId = Shader.PropertyToID("_Shallow");

        /// <summary>Форма Вихря в слоте (None — Вихря нет или он без формы).</summary>
        private PelagForm CurrentWhirlwindForm()
        {
            if (_driver.Sim == null) return PelagForm.None;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
            {
                AbilityBuild ability = _driver.Sim.GetAbility(slot);
                if (ability != null && ability.DefinitionId == AbilityDefinition.WhirlwindId) return _driver.Sim.FormAt(slot);
            }
            return PelagForm.None;
        }

        /// <summary>
        /// Общие эффекты Вихря (кольцо, всплески на целях) из пула: в форме — её вода и брызги,
        /// без формы — цвета самих материалов (объект мог прийти из пула окрашенным).
        /// </summary>
        private static void TintWhirlwindShared(GameObject go, PelagForm form)
        {
            bool tinted = WhirlwindFormWater(form, out Color deep, out Color water, out Color shallow, out Color shade);
            if (_formTintBlock == null) _formTintBlock = new MaterialPropertyBlock();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null) continue;
                bool wave = m.HasProperty(WaterId), blob = m.HasProperty(ShadeId);
                if (!wave && !blob) continue;
                r.GetPropertyBlock(_formTintBlock);
                if (wave)
                {
                    _formTintBlock.SetColor(DeepId, tinted ? deep : m.GetColor(DeepId));
                    _formTintBlock.SetColor(WaterId, tinted ? water : m.GetColor(WaterId));
                    _formTintBlock.SetColor(ShallowId, tinted ? shallow : m.GetColor(ShallowId));
                }
                if (blob) _formTintBlock.SetColor(ShadeId, tinted ? shade : m.GetColor(ShadeId));
                r.SetPropertyBlock(_formTintBlock);
            }
        }

        // ---- Буря ----

        private void BeginStormColumn()
        {
            if (CaptureRig.NoVfx) return;
            // Новое удержание сменяет недогоревший столб прежнего.
            if (_stormFx >= 0 && StillActive(_stormFx, _stormObject)) Release(_stormFx);
            ForgetStormColumn();
            if (!TryAcquire(PelagVfxId.WhirlwindStormColumn, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            Vector3 feet = PlayerPosition();
            float scale = element.AuthoredRadius > 0f ? WhirlwindRadius() / element.AuthoredRadius : 1f;
            _stormRaise = FormRaise(feet, WhirlwindRadius());
            feet.y += _stormRaise;
            // Корень как у кольца Вихря: X90 — местная +Z вниз, витки крутятся вокруг неё.
            element.Begin(feet, Quaternion.Euler(90f, 0f, 0f));
            TintFormDroplets(go, PelagForm.WhirlwindStorm);
            go.transform.localScale = Vector3.one * scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindStormColumn, Object = go, Element = element,
                Duration = StormColumnMaxSeconds, Start = feet, End = feet, Motion = Motion.Static, FollowIndex = -1
            };
            _stormFx = index;
            _stormObject = go;
            _stormEnding = false;
            Transform root = go.transform;
            Transform pulse = root.Find("PulseSpray");
            _stormPulse = pulse != null ? pulse.GetComponent<ParticleSystem>() : null;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            int loops = 0;
            foreach (var ps in systems) if (ps.main.loop) loops++;
            _stormLoops = new ParticleSystem[loops];
            loops = 0;
            foreach (var ps in systems) if (ps.main.loop) _stormLoops[loops++] = ps;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-storm] column scale={scale:F2} loops={_stormLoops.Length}");
        }

        private void PulseStormColumn(int enemies)
        {
            if (_stormFx < 0 || _stormEnding || !StillActive(_stormFx, _stormObject)) return;
            if (_stormPulse != null)
                _stormPulse.Emit(StormPulseDrops + StormPulseDropsPerEnemy * Mathf.Clamp(enemies, 0, 5));
            PulseCombatLight(.30f);
        }

        /// <summary>Конец Бури: витки больше не рождаются и доживают, по земле — всплеск.</summary>
        private void EndStormColumn(bool splash)
        {
            if (_stormFx < 0 || _stormEnding) return;
            if (!StillActive(_stormFx, _stormObject)) { ForgetStormColumn(); return; }
            _stormEnding = true;
            if (_stormLoops != null)
                foreach (var ps in _stormLoops) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            ref ActiveFx fx = ref _active[_stormFx];
            fx.Duration = Mathf.Min(fx.Duration, fx.Age + StormColumnLinger);
            if (!splash || CaptureRig.NoVfx) return;
            Vector3 feet = PlayerPosition();
            float radius = WhirlwindRadius();
            feet.y += FormRaise(feet, radius);
            if (!TryAcquire(PelagVfxId.WhirlwindStormSplash, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            element.Begin(feet, Quaternion.Euler(90f, 0f, 0f));
            TintFormDroplets(go, PelagForm.WhirlwindStorm);
            go.transform.localScale = Vector3.one * (element.AuthoredRadius > 0f ? radius / element.AuthoredRadius : 1f);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindStormSplash, Object = go, Element = element,
                Duration = Mathf.Max(.05f, element.DefaultLifetime), Start = feet, End = feet,
                Motion = Motion.Static, FollowIndex = -1
            };
            PulseCombatLight(.45f);
        }

        private void ForgetStormColumn()
        {
            _stormFx = -1;
            _stormObject = null;
            _stormLoops = null;
            _stormPulse = null;
            _stormEnding = false;
        }

        // ---- Водоворот ----

        private void PlayMaelstrom(in SimEvent e, int eventTick)
        {
            Vector3 feet = PlayerPosition();
            _maelstromCentre = new Vector3(e.Position.X.ToFloat(), feet.y, e.Position.Y.ToFloat());
            _maelstromContactTick = PelagWhirlwindFormRules.MaelstromContactTick(eventTick, e.ActionVariant);
            if (CaptureRig.NoVfx) return;
            // Новый Водоворот сменяет недогоревший прежний; следы прежних тяг доживают сами.
            if (_maelstromFx >= 0 && StillActive(_maelstromFx, _maelstromObject)) Release(_maelstromFx);
            ForgetMaelstrom();
            float reach = WhirlwindRadius();
            _maelstromPullRadius = Simulation.MaelstromRadius.ToFloat();
            _maelstromCentre = SampleFormGround(_maelstromGround, _maelstromCentre, _maelstromPullRadius + .6f);
            _maelstromEventTick = eventTick;
            float contact = PelagWhirlwindFormRules.MaelstromContactSeconds(e.ActionVariant);
            BeginMaelstromDrags(contact);
            if (!TryAcquire(PelagVfxId.WhirlwindMaelstrom, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            // Корень — центр на земле, без поворота и масштаба: меш рукавов пишется в метрах.
            element.Begin(_maelstromCentre, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            TintFormDroplets(go, PelagForm.WhirlwindMaelstrom);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindMaelstrom, Object = go, Element = element,
                // Распад рукавов идёт от контакта: длинная тяга Sim сдвигает и конец жизни.
                Duration = MaelstromSeconds + PelagWhirlwindFormRules.MaelstromExtraLifeSeconds(contact),
                Start = _maelstromCentre, End = _maelstromCentre,
                Motion = Motion.Static, FollowIndex = -1
            };
            _maelstromFx = index;
            _maelstromObject = go;
            Transform root = go.transform;
            _maelstromFilter = root.Find("Arms")?.GetComponent<MeshFilter>();
            _maelstromStrandsFilter = root.Find("Strands")?.GetComponent<MeshFilter>();
            _maelstromTipDrops = root.Find("TipDrops")?.GetComponent<ParticleSystem>();
            _maelstromFoam = root.Find("Foam")?.GetComponent<ParticleSystem>();
            _maelstromTipCarry = 0f;
            _maelstromFoamBurst = false;
            _maelstromStrandsDone = false;
            _maelstromWater.Begin(reach, contact);
            _maelstromStrands.Begin(_maelstromPullRadius, reach, contact);
            Simulation sim = _driver.Sim;
            float now = sim != null ? sim.Tick - 1 + _driver.Alpha : eventTick;
            BuildMaelstromWater(Mathf.Max(0f, (now - eventTick) / Simulation.TicksPerSecond));
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-maelstrom] pulled={e.Amount} ticks={e.ActionVariant} contact={_maelstromContactTick} "
                          + $"arms={reach:F2} pull={_maelstromPullRadius:F2} ground={_maelstromCentre.y - feet.y:F2}");
        }

        private void ForgetMaelstrom()
        {
            _maelstromFx = -1;
            _maelstromObject = null;
            _maelstromFilter = null;
            _maelstromStrandsFilter = null;
            _maelstromTipDrops = null;
            _maelstromFoam = null;
        }

        /// <summary>Рукава и струи тяги по непрерывному времени; струи, влившиеся в рукава, больше не пишутся.</summary>
        private void BuildMaelstromWater(float t)
        {
            if (_maelstromFilter != null)
                _maelstromWater.Build(FormWaterMesh.MeshFor(_maelstromFilter, PelagMaelstromWater.MeshName), _maelstromCentre,
                    t, _maelstromGround);
            if (_maelstromStrandsFilter == null || _maelstromStrandsDone) return;
            // Последний кадр пишется уже пустым (все струи стянулись в голову) — дальше меш не трогаем.
            _maelstromStrandsDone = _maelstromStrands.Done(t);
            _maelstromStrands.Build(FormWaterMesh.MeshFor(_maelstromStrandsFilter, PelagMaelstromStrands.MeshName), _maelstromCentre,
                t, _maelstromGround);
        }

        /// <summary>
        /// Кадр Водоворота: рукава и изогнутые струи тяги (v4) по непрерывному времени, капли с концов
        /// рукавов, пена в контакт, следы тяги.
        /// </summary>
        private void UpdateMaelstrom(float now, float dt)
        {
            float t = (now - _maelstromEventTick) / Simulation.TicksPerSecond;
            UpdateMaelstromDrags(t, dt);
            if (_maelstromFx < 0) return;
            if (!StillActive(_maelstromFx, _maelstromObject)) { ForgetMaelstrom(); return; }
            BuildMaelstromWater(Mathf.Max(0f, t));
            // Капли с внешних концов рукавов: концы идут по часовой, капли слетают по ходу и наружу (vortex-1).
            if (_maelstromTipDrops != null && dt > 0f && t > .04f && t < _maelstromWater.Contact + .12f)
            {
                _maelstromTipCarry += dt * MaelstromTipDropRate;
                int count = (int)_maelstromTipCarry;
                _maelstromTipCarry -= count;
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = _maelstromWater.ArmPoint(Random.Range(0, PelagMaelstromWater.Arms), Random.Range(0f, .10f), t);
                    var outward = new Vector3(p.x, 0f, p.y).normalized;
                    var clockwise = new Vector3(outward.z, 0f, -outward.x);
                    Vector3 at = _maelstromCentre + new Vector3(p.x, 0f, p.y);
                    at.y = _maelstromGround.At(at.x, at.z) + .08f;
                    _maelstromTipDrops.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = clockwise * Random.Range(1.8f, 3.2f) + outward * Random.Range(.8f, 1.8f)
                                   + Vector3.up * Random.Range(1.0f, 2.2f),
                        applyShapeToPosition = false
                    }, 1);
                }
            }
            // Контакт: рукава вскипают пеной — комья по рукавам.
            if (!_maelstromFoamBurst && t >= _maelstromWater.Contact && _maelstromFoam != null)
            {
                _maelstromFoamBurst = true;
                for (int i = 0; i < 10; i++)
                {
                    int arm = i % PelagMaelstromWater.Arms;
                    Vector2 p = _maelstromWater.ArmPoint(arm, Random.Range(.12f, .8f), t);
                    var outward = new Vector3(p.x, 0f, p.y).normalized;
                    Vector3 at = _maelstromCentre + new Vector3(p.x, 0f, p.y);
                    at.y = _maelstromGround.At(at.x, at.z) + .06f;
                    _maelstromFoam.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = outward * Random.Range(.3f, 1.0f) + Vector3.up * Random.Range(.3f, .9f),
                        applyShapeToPosition = false
                    }, 1);
                }
            }
        }

        /// <summary>
        /// Кого может тащить тяга: живые враги в круге тяги. След появится, только
        /// если тело правда поедет к герою (упёршиеся, элиты, тяжёлые — без следа).
        /// </summary>
        private void BeginMaelstromDrags(float contact)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            EntityStore entities = sim.Entities;
            var centre = new Vector2(_maelstromCentre.x, _maelstromCentre.z);
            int slot = 0;
            for (int i = 1; i < entities.Count && slot < _maelstromDrags.Length; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == entities.Side[Simulation.PlayerId]) continue;
                Vector3 body = EntityPosition(i, entities.Position[i]);
                var flat = new Vector2(body.x, body.z) - centre;
                float distance = flat.magnitude;
                if (distance < .3f || distance > _maelstromPullRadius + entities.BodyRadius[i].ToFloat() + .3f) continue;
                while (slot < _maelstromDrags.Length && _maelstromDrags[slot].Active) slot++;
                if (slot >= _maelstromDrags.Length) break;
                Vector2 toward = -flat / distance;
                _maelstromDrags[slot] = new MaelstromDragRun
                {
                    Active = true, Target = i, Fx = -1, Start = body,
                    Direction = new Vector3(toward.x, 0f, toward.y),
                    Ground = _maelstromGround.At(body.x, body.z)
                };
                slot++;
            }
        }

        /// <summary>
        /// Следы тяги: каждый кадр — сколько тело правда проехало к герою (вид тела,
        /// как его рисует арена). След растёт за телом, у ног — занос брызг; тело
        /// встало (или прошёл контакт) — след гаснет распадом, как след рывка.
        /// </summary>
        private void UpdateMaelstromDrags(float t, float dt)
        {
            for (int i = 0; i < _maelstromDrags.Length; i++)
            {
                ref MaelstromDragRun run = ref _maelstromDrags[i];
                if (!run.Active) continue;
                PelagDashWake wake = _maelstromDragWakes[i] ?? (_maelstromDragWakes[i] = new PelagDashWake());
                Vector3 body = EntityPosition(run.Target, run.Start);
                Vector3 moved = body - run.Start;
                moved.y = 0f;
                float shift = Mathf.Max(0f, Vector3.Dot(moved, run.Direction));
                float contact = _maelstromWater.Contact;
                if (!run.Started)
                {
                    if (shift >= MaelstromDragMin && t < contact + .1f && !CaptureRig.NoVfx) StartMaelstromDrag(ref run, wake);
                    else if (t > contact + .2f) { run = default; continue; }
                    if (!run.Started) continue;
                }
                if (!StillActive(run.Fx, run.Object)) { run = default; continue; }
                if (!wake.Ended)
                {
                    run.StillFrames = shift - run.LastShift < .003f ? run.StillFrames + 1 : 0;
                    if ((run.StillFrames >= 3 && shift > MaelstromDragStillAfter) || t > contact + .1f)
                        wake.End(Mathf.Max(shift, wake.Length));
                }
                run.LastShift = shift;
                wake.Advance(dt, shift);
                if (run.SkidFoam != null && run.SkidDrops != null && wake.Length > run.SkidAlong)
                {
                    // Пенная борозда (v4): клочья по гребням узкой борозды и брызги веером у ног тела.
                    EmitDragFurrow(ref run, run.SkidAlong, wake.Length);
                    run.SkidAlong = wake.Length;
                }
                if (run.Filter != null) wake.Build(PelagDashWake.MeshFor(run.Filter));
                if (wake.Done) { Release(run.Fx); run = default; }
            }
        }

        /// <summary>
        /// Пенная борозда за притянутым телом (v4; проверка: «следы — фиолетовые плиты»):
        /// на пройденном отрезке [<paramref name="fromAlong"/>, <paramref name="toAlong"/>] м
        /// клочья пены срываются с обоих гребней борозды и скользят наружу, у ног тела
        /// вода брызжет веером вверх и в стороны. Основание — у ног, на 0,2 м позади
        /// середины тела, как прежний занос.
        /// </summary>
        private static void EmitDragFurrow(ref MaelstromDragRun run, float fromAlong, float toAlong)
        {
            float distance = toAlong - fromAlong;
            if (distance <= 0f) return;
            Vector3 right = Vector3.Cross(Vector3.up, run.Direction);
            Vector3 start = run.Root + run.Direction * (MaelstromDragBack - .11f);
            run.FoamCarry += distance * MaelstromFurrowFoamPerMetre;
            run.DropCarry += distance * MaelstromFurrowDropsPerMetre;
            int foamCount = (int)run.FoamCarry, dropCount = (int)run.DropCarry;
            run.FoamCarry -= foamCount;
            run.DropCarry -= dropCount;
            for (int i = 0; i < foamCount; i++)
            {
                float side = Random.value < .5f ? -1f : 1f;
                Vector3 at = start + run.Direction * (Random.Range(fromAlong, toAlong) - .10f)
                             + right * side * MaelstromFurrowEdge * Random.Range(.85f, 1.15f);
                at.y = run.Ground + .05f;
                run.SkidFoam.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = -run.Direction * Random.Range(.2f, .7f) + right * side * Random.Range(.5f, 1.2f)
                               + Vector3.up * Random.Range(.1f, .4f),
                    startSize = Random.Range(.16f, .30f),
                    startLifetime = Random.Range(.30f, .46f),
                    applyShapeToPosition = false
                }, 1);
            }
            for (int i = 0; i < dropCount; i++)
            {
                float side = Random.value < .5f ? -1f : 1f;
                Vector3 at = start + run.Direction * (toAlong - Random.Range(.02f, .14f))
                             + right * side * MaelstromFurrowEdge * Random.Range(.2f, 1.0f);
                at.y = run.Ground + .06f;
                run.SkidDrops.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = -run.Direction * Random.Range(.3f, 1.2f) + right * side * Random.Range(.8f, 2.0f)
                               + Vector3.up * Random.Range(1.6f, 3.0f),
                    startSize = Random.Range(.06f, .11f),
                    startLifetime = Random.Range(.26f, .40f),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        private void StartMaelstromDrag(ref MaelstromDragRun run, PelagDashWake wake)
        {
            if (!TryAcquire(PelagVfxId.WhirlwindMaelstromDrag, out GameObject go, out PelagVfxElement element)) { run = default; return; }
            int index = ReserveActive();
            run.Root = run.Start - run.Direction * MaelstromDragBack;
            run.Root.y = run.Ground;
            element.Begin(run.Root, Quaternion.LookRotation(run.Direction, Vector3.up));
            TintFormDroplets(go, PelagForm.WhirlwindMaelstrom);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindMaelstromDrag, Object = go, Element = element,
                Duration = PelagDashWake.MaxLife + .2f, Start = run.Root, End = run.Root, Motion = Motion.Static, FollowIndex = -1
            };
            Transform root = go.transform;
            run.Started = true;
            run.Fx = index;
            run.Object = go;
            run.Filter = root.Find("Wake")?.GetComponent<MeshFilter>();
            run.SkidFoam = root.Find("SkidFoam")?.GetComponent<ParticleSystem>();
            run.SkidDrops = root.Find("SkidDrops")?.GetComponent<ParticleSystem>();
            run.TailDrops = root.Find("TailDrops")?.GetComponent<ParticleSystem>();
            run.FoamCarry = run.DropCarry = .6f;
            Vector3 toHero = _maelstromCentre - run.Start;
            toHero.y = 0f;
            wake.Begin(toHero.magnitude, Random.Range(0f, 8f), GameUserSettings.FlashScale);
            if (run.Filter != null) wake.Build(PelagDashWake.MeshFor(run.Filter));
            if (run.TailDrops != null) PelagDashWake.EmitTailDrops(run.TailDrops, run.Root, run.Direction, run.Ground);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-maelstrom-drag] target={run.Target} from={run.Start.ToString("F2")} toHero={toHero.magnitude:F2}");
        }

        // ---- Пенные волны ----

        private void BeginFoamWave(in SimEvent e, int eventTick)
        {
            int ring = Mathf.Clamp(e.Amount, 0, Simulation.FoamRingCount - 1);
            var centre = new Vector3(e.Position.X.ToFloat(), PlayerPosition().y, e.Position.Y.ToFloat());
            _foamRingCentres[ring] = centre;
            if (CaptureRig.NoVfx || !TryAcquire(PelagVfxId.WhirlwindFoamWave, out GameObject go, out PelagVfxElement element)) return;
            int travel = e.ActionVariant > 0 ? e.ActionVariant : Simulation.FoamRingTravelTicks(ring);
            int slot = -1;
            for (int i = 0; i < _foamWaves.Length && slot < 0; i++)
                if (!_foamWaves[i].Active || !StillActive(_foamWaves[i].Fx, _foamWaves[i].Object)) slot = i;
            if (slot < 0)
            {
                // Все места заняты: уступает самое старое кольцо.
                slot = 0;
                Release(_foamWaves[0].Fx);
            }
            PelagFoamRingWater water = _foamWaveWater[slot] ?? (_foamWaveWater[slot] = new PelagFoamRingWater());
            FormGroundGrid ground = _foamWaveGround[slot] ?? (_foamWaveGround[slot] = new FormGroundGrid());
            centre = SampleFormGround(ground, centre, Simulation.FoamRingOuterRadius(ring).ToFloat() + .9f);
            water.Begin(ring, eventTick, travel);
            int index = ReserveActive();
            // Корень — центр колец на земле, без поворота и масштаба: меш кольца пишется в метрах.
            element.Begin(centre, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            TintFormDroplets(go, PelagForm.WhirlwindFoamWaves);
            Simulation sim = _driver.Sim;
            float now = sim != null ? sim.Tick - 1 + _driver.Alpha : eventTick;
            float left = Mathf.Max(.1f, water.LifeSeconds - water.Seconds(now));
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WhirlwindFoamWave, Object = go, Element = element,
                Duration = left + FoamWaveSprayTail, Start = centre, End = centre, Motion = Motion.Static, FollowIndex = -1
            };
            Transform root = go.transform;
            _foamWaves[slot] = new FoamWaveRun
            {
                Active = true, Fx = index, Object = go, Centre = centre,
                Filter = root.Find("Ring")?.GetComponent<MeshFilter>(),
                Spray = root.Find("Spray")?.GetComponent<ParticleSystem>(),
                Foam = root.Find("Foam")?.GetComponent<ParticleSystem>()
            };
            UpdateFoamWave(ref _foamWaves[slot], water, ground, now, 0f);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[whirlwind-foam-wave] ring={ring} tick={eventTick} travel={travel} life={water.LifeSeconds:F2} "
                          + $"crest={water.Crest(now):F2} ground={centre.y - PlayerPosition().y:F2}");
        }

        /// <summary>
        /// Кадр кольца: меш воды по непрерывному времени, брызги и комья пены с
        /// бегущего гребня (чем быстрее гребень, тем гуще), при разрыве — выброс капель.
        /// </summary>
        private void UpdateFoamWave(ref FoamWaveRun run, PelagFoamRingWater water, FormGroundGrid ground, float now, float dt)
        {
            if (run.Filter != null)
                water.Build(FormWaterMesh.MeshFor(run.Filter, PelagFoamRingWater.MeshName), run.Centre, now, ground);
            float x = water.Progress(now);
            float crest = water.Crest(now);
            float inner = Simulation.FoamRingInnerRadius.ToFloat();
            float travelSeconds = water.Travel / (float)Simulation.TicksPerSecond;
            float span = Simulation.FoamRingOuterRadius(water.Ring).ToFloat() - inner;
            float speed = x < 1f ? span * (1f + PelagWhirlwindFormRules.FoamCrestEase * (1f - 2f * x)) / travelSeconds : 0f;
            if (dt > 0f && x < 1.1f)
            {
                // v4: капли крупнее и реже (проверка: «мелкая крошка»), с обоих краёв полосы, как на waves-2.
                float strength = Mathf.Clamp01(1.15f - x * .6f);
                run.SprayCarry += dt * 75f * strength;
                run.FoamCarry += dt * 30f * strength;
                float band = 2f * PelagWhirlwindFormRules.FoamBandHalfWidth(crest);
                EmitCrest(run.Spray, ref run.SprayCarry, water, ground, run.Centre, now, crest, band, speed, true);
                EmitCrest(run.Foam, ref run.FoamCarry, water, ground, run.Centre, now, crest, band, speed, false);
            }
            if (!run.BreakBurst && water.Seconds(now) >= water.BreakSeconds * .92f)
            {
                run.BreakBurst = true;
                float burst = 18f;
                EmitCrest(run.Spray, ref burst, water, ground, run.Centre, now, crest,
                    2f * PelagWhirlwindFormRules.FoamBandHalfWidth(crest), speed, true);
            }
        }

        /// <summary>
        /// Брызги и клочья с гребней кольца: две трети — с внешнего (бегущего) края наружу,
        /// треть — с внутреннего края внутрь; на waves-2 капли летят по обе стороны полосы.
        /// </summary>
        private static void EmitCrest(ParticleSystem system, ref float carry, PelagFoamRingWater water, FormGroundGrid ground,
            Vector3 centre, float now, float crest, float band, float speed, bool spray)
        {
            int count = (int)carry;
            carry -= count;
            if (system == null) return;
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                bool inner = Random.value < .33f;
                float edge = crest + water.Wobble(angle, now) - (inner ? band : 0f);
                Vector3 at = centre + radial * (edge + (inner ? -1f : 1f) * (spray ? .02f : -.06f));
                at.y = ground.At(at.x, at.z) + (spray ? .07f : .05f);
                Vector3 away = inner ? -radial : radial;
                float push = inner ? .5f : 1f;
                Vector3 velocity = spray
                    ? away * (Random.Range(1.0f, 2.2f) + speed * .3f * push) + Vector3.up * Random.Range(1.6f, 3.2f)
                    : away * (Random.Range(.4f, 1.2f) + speed * .15f * push) + Vector3.up * Random.Range(.2f, .6f);
                system.Emit(new ParticleSystem.EmitParams { position = at, velocity = velocity, applyShapeToPosition = false }, 1);
            }
        }

        private void PlayFoamRingHit(in SimEvent e)
        {
            _foamRingDamageTarget = e.Target;
            int ring = Mathf.Clamp(e.Amount, 0, Simulation.FoamRingCount - 1);
            PlayWaveSplash(e.Target, e.Position, _foamRingCentres[ring]);
        }

        /// <summary>
        /// Удар кольца по врагу (v4, по waves-2): большой веер брызг выше пояса — белые языки
        /// воды вверх и наружу от центра колец, крупные капли, пена и лужа у ног. Корень
        /// сдвинут к камере вдоль луча: на экране веер растёт от ног, но рисуется перед телом,
        /// и ни тело, ни его кремовая вспышка удара его не закрывают.
        /// </summary>
        private void PlayWaveSplash(int target, FixVec2 fallback, Vector3 centre)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 body = EntityPosition(target, fallback);
            _formGroundBase = PlayerPosition().y;
            var at = new Vector3(body.x, FormGroundAt(body.x, body.z) + .02f, body.z);
            Vector3 outward = FlatDirection(centre, at);
            Vector3 root = at;
            Camera camera = Camera.main;
            if (camera != null) root += (camera.transform.position - at).normalized * WaveSplashCameraPush;
            int index = Spawn(PelagVfxId.WhirlwindWaveSplash, root, Quaternion.LookRotation(outward, Vector3.up), .8f, 1f, 1f, Motion.Static);
            if (index >= 0) TintFormDroplets(_active[index].Object, PelagForm.WhirlwindFoamWaves);
        }

        /// <summary>
        /// Корона брызг у ног врага: белые языки и вырезанные капли, наклон — от
        /// центра наружу (кольцо толкает), у Водоворота — к центру (тянет).
        /// </summary>
        private void PlayCrownSplash(int target, FixVec2 fallback, Vector3 centre, float scale, bool inward)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 body = EntityPosition(target, fallback);
            // Земля под врагом, а не высота ног героя: в лагере и на уступах они разные.
            _formGroundBase = PlayerPosition().y;
            Vector3 at = new Vector3(body.x, FormGroundAt(body.x, body.z) + .02f, body.z);
            Vector3 outward = FlatDirection(centre, at);
            Spawn(PelagVfxId.WhirlwindCrownSplash, at, Quaternion.LookRotation(inward ? -outward : outward, Vector3.up),
                .6f, scale, scale, Motion.Static);
        }

        /// <summary>Радиус Вихря из сборки (как у кольца контакта); без Вихря — базовые 2,3 м.</summary>
        private float WhirlwindRadius()
        {
            Simulation sim = _driver.Sim;
            if (sim != null)
                for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                {
                    AbilityBuild ability = sim.GetAbility(slot);
                    if (ability != null && ability.DefinitionId == AbilityDefinition.WhirlwindId)
                        return ability.Get(AbilityStatType.Radius).ToFloat();
                }
            return 2.3f;
        }
    }
}
