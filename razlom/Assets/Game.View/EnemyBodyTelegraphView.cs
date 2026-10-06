using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ЗНАК НА ТЕЛЕ ВМЕСТО МЕТКИ НА ПОЛУ (план «Мобы леса v2», поток E; выбор
    /// владельца 29.09 — вариант A, ART/characters/act-1-enemies/review/
    /// mobs-v2-concepts-2026-09-29/body-sign/01-a-claw-ember-windup.png,
    /// 02-a-three-claw-streaks-impact.png, 05-swarm-crawler-bite-sign.png).
    ///
    /// Как в Shape of Dreams: за замах ближнего удара на бьющей кости
    /// разгорается красный уголь — от искры до яркого ядра с отблеском к тику
    /// удара, — а в тик удара от кости по зафиксированному в начале замаха
    /// направлению проходит короткий яркий след:
    ///   • Хранитель (и моб без вида на стендах — у него тело Хранителя) —
    ///     уголь на бьющей лапе, три изогнутые полосы когтей к цели. Лапа —
    ///     по состоянию аниматора: AttackA (Mutant Swiping) бьёт левой,
    ///     AttackB (зеркало) — правой (замер swing_measure.json 26.09);
    ///   • Корнеполз, Расщепень, детёныш — уголь у пасти, два серпа укуса,
    ///     смыкающиеся перед мордой;
    ///   • Камнекопыт, взмах клыками (EnemyActionKind.StonehoofTusk) — уголь
    ///     на клыках, одна дуга перед мордой, проходящая сбоку на бок;
    ///   • Вендиго, круг когтей (EnemyActionKind.WendigoSweep) — угли на обеих
    ///     лапах за замах; сам круг рисует вид Вендиго (поток G);
    ///   • Хозяин Чащи, лапа (EnemyActionKind.ThicketPaw) — уголь на когтях бьющей
    ///     лапы (leg_front_R_toe / leg_front_L_toe; Amount события — номер удара
    ///     серии П/Л/П: чётный — правая), свой на каждый удар; полос когтей нет —
    ///     ленты когтей рисует ThicketMasterCombatView, в масштабе тела 4,14 м. С
    ///     02.10 (вечер) у лапы есть и метка на земле: сектор от бьющего плеча
    ///     (SharedView) — его рисует GroundTelegraphView, уголь — поверх.
    ///
    /// Секторы Хранителя и Расщепеня в Sim остаются (попадание считается по
    /// ним), но на земле не рисуются: замах открывает их без
    /// TelegraphFlags.SharedView, а GroundTelegraphView рисует только метки с
    /// этим флагом.
    ///
    /// ВРЕМЯ — ОТ ТИКА SIM. Уголь начинается событием начала замаха (Attack,
    /// EnemyActionStarted), его возраст — доля замаха (тик Sim с подкадром
    /// драйвера), поэтому пауза, стоп-кадр и съёмка держат кадр сами. Внутри
    /// префаба замах авторский — EmberWindupSeconds, дальше хвост вспышки
    /// EmberTailSeconds уже в секундах. След резервируется из пула тем же
    /// событием начала (объект включается там же, частиц нет), ставится и
    /// запускается в тик удара, если Sim удар провела: снятый оглушением
    /// замах гасит и уголь, и зарезервированный след. Клыки и круг Вендиго
    /// приходят ещё и событием удара (EnemyActionImpact).
    ///
    /// Префабы собирает EnemyBodyTelegraphSetup (Разлом → Знак на теле мобов)
    /// из паков CFXR (unlit-свечения, искра, отблеск) и шейдера серпа Вихря
    /// (полосы и серпы); пулы собираются в Awake, в бою ни одного Instantiate.
    /// Персонажей не высветляет — только свои частицы.
    ///
    /// Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [DefaultExecutionOrder(650)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class EnemyBodyTelegraphView : MonoBehaviour
    {
        // ---- общее с EnemyBodyTelegraphSetup ----

        /// <summary>Папка префабов в Resources.</summary>
        public const string PrefabFolder = "VFX/BodyTelegraph/Prefabs/";
        public const string EmberPrefab = "VFX_BodySign_Ember";
        public const string ClawPrefab = "VFX_BodySign_Claw";
        public const string BitePrefab = "VFX_BodySign_Bite";
        public const string TuskPrefab = "VFX_BodySign_Tusk";

        /// <summary>Дети префаба когтей: полосы для левой и правой лапы (зеркальные меши).</summary>
        public const string ClawLeftChild = "StreaksL", ClawRightChild = "StreaksR";

        /// <summary>
        /// Сколько секунд времени префаба угля занимает замах: любой замах
        /// (12–23 тика) проигрывает эту секунду целиком, тик удара — ровно её
        /// конец. После — хвост вспышки EmberTailSeconds настоящих секунд.
        /// </summary>
        public const float EmberWindupSeconds = 1f, EmberTailSeconds = .15f;

        /// <summary>Жизнь слоёв следа в шейдере серпа, секунды от тика удара.</summary>
        public const float ClawLife = .42f, BiteLife = .34f, TuskLife = .45f;

        /// <summary>Длина полос когтей в префабе, метры (от когтя к цели).</summary>
        public const float ClawLength = 1.85f;

        /// <summary>Радиус дуги клыков в префабе, метры от центра кабана.</summary>
        public const float TuskArcRadius = 1.25f;

        // ---- настройки вида ----

        /// <summary>Уголь сдвигается к камере: иначе лапа, в которой он сидит, прячет его половину.</summary>
        private const float CameraNudge = .22f;

        /// <summary>От кости головы до пасти вперёд, метры на масштаб угля.</summary>
        private const float JawReach = .14f;

        /// <summary>
        /// Корнеполз (Mixamo): морда скинута на Spine2 (клипы v2, build_rootswarm_v2.py),
        /// а Head несёт рога над затылком — уголь у Head горел бы на макушке. Пасть —
        /// ~.21 м модели вперёд от Spine2, на масштабе тела 1,59 это .33 м; здесь — на
        /// масштаб угля (.72): .46.
        /// </summary>
        private const float MouthReach = .46f;

        /// <summary>Серпы укуса смыкаются перед пастью на этом расстоянии, метры на масштаб следа.</summary>
        private const float BiteAhead = .26f;

        /// <summary>Хранитель без решённой лапы (аниматора нет) после этой доли замаха берёт левую, как AttackA.</summary>
        private const float HandDecideShare = .35f;

        private static readonly int AttackAState = Animator.StringToHash("Base Layer.AttackA");
        private static readonly int AttackBState = Animator.StringToHash("Base Layer.AttackB");

        private enum Sign : byte { Claw, Bite, Tusk, Sweep, Paw }

        /// <summary>Экземпляр префаба: возраст задаёт вид, системы догоняются приращениями.</summary>
        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public uint[] Seeds;
            public ParticleSystemRenderer Left, Right;
            public float Life, Simulated = -1f;
            public int Tick, Order;
            /// <summary>След поставлен в тик удара; до этого он только зарезервирован и пуст.</summary>
            public bool Placed;
        }

        private sealed class Pool
        {
            public Burst[] Items = new Burst[0];
            public int Taken;
        }

        /// <summary>Горящий знак одного замаха.</summary>
        private sealed class Ember
        {
            public int Entity = -1, Serial, StartTick, ImpactTick, SpawnFrame;
            /// <summary>0 — лапа не решена, 1 — левая, 2 — правая (у круга Вендиго — какая из двух).</summary>
            public int Hand;
            /// <summary>Лапа Хозяина Чащи: номер лапы в действии (двойная фазы 3 — 0 и 1).</summary>
            public int Stage;
            public Sign Sign;
            public EnemyKind Kind;
            public FixVec2 Origin, Direction;
            public float EmberScale, SlashScale;
            public bool Landed;
            public Burst Fx, Slash;
        }

        /// <summary>Кости тела: ищутся один раз на тело пула.</summary>
        private sealed class Rig
        {
            public Transform LeftClaw, RightClaw, Head, Snout, Mouth;
            /// <summary>Когти передних лап Хозяина Чащи (leg_front_*_toe, без них — leg_front_*_paw).</summary>
            public Transform PawLeft, PawRight;
            public Animator Animator;
        }

        private TickDriver _driver;
        private ArenaView _arena;
        private LayoutView _layout;
        private Camera _camera;
        private Simulation _shown;
        private Pool _embers, _claws, _bites, _tusks;
        private readonly Ember[] _signs = new Ember[16];
        private readonly Dictionary<Transform, Rig> _rigs = new Dictionary<Transform, Rig>();
        private static bool _warnedMissing;

        public static EnemyBodyTelegraphView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<EnemyBodyTelegraphView>();
            return view != null ? view : host.AddComponent<EnemyBodyTelegraphView>();
        }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            _layout = GetComponent<LayoutView>();
            for (int i = 0; i < _signs.Length; i++) _signs[i] = new Ember();
            // Пулы под потолок одновременных замахов: два ближних жетона, три
            // укуса, клыки в ближнем жетоне, круг Вендиго — два угля. След живёт
            // и после угля, поэтому следов вдвое больше замахов.
            _embers = MakePool(EmberPrefab, "Знак на теле: уголь", _signs.Length, EmberWindupSeconds + EmberTailSeconds);
            _claws = MakePool(ClawPrefab, "Знак на теле: когти", 6, ClawLife + .1f);
            _bites = MakePool(BitePrefab, "Знак на теле: укус", 8, BiteLife + .12f);
            _tusks = MakePool(TuskPrefab, "Знак на теле: клыки", 3, TuskLife + .12f);
            if (_embers.Items.Length == 0 && !_warnedMissing)
            {
                _warnedMissing = true;
                Debug.LogWarning("[body-sign] Нет префабов знака на теле в Resources/" + PrefabFolder
                                 + " — собери: Разлом → Знак на теле мобов → VFX: подключить.");
            }
        }

        private Pool MakePool(string prefabName, string name, int count, float life)
        {
            var pool = new Pool();
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null) return pool;
            pool.Items = new Burst[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = name;
                var burst = new Burst { Root = go, Particles = go.GetComponentsInChildren<ParticleSystem>(true), Life = life };
                burst.Seeds = new uint[burst.Particles.Length];
                for (int k = 0; k < burst.Particles.Length; k++)
                {
                    var ps = burst.Particles[k];
                    // Прогрев: короткий прогон заводит буферы частиц до боя.
                    ps.Simulate(.05f, false, true, false);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Seeds[k] = ps.randomSeed;
                }
                var left = go.transform.Find(ClawLeftChild);
                var right = go.transform.Find(ClawRightChild);
                if (left != null) burst.Left = left.GetComponent<ParticleSystemRenderer>();
                if (right != null) burst.Right = right.GetComponent<ParticleSystemRenderer>();
                go.SetActive(false);
                pool.Items[i] = burst;
            }
            return pool;
        }

        private void LateUpdate()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null)
            {
                if (_shown != null) ResetAll();
                _shown = null;
                return;
            }
            if (_shown != sim)
            {
                ResetAll();
                _shown = sim;
            }
            if (_camera == null) _camera = Camera.main;
            float tick = sim.Tick - 1 + _driver.Alpha;

            ReadEvents(sim);
            for (int i = 0; i < _signs.Length; i++) UpdateSign(sim, _signs[i], tick);
            AdvanceSlashes(_claws, tick);
            AdvanceSlashes(_bites, tick);
            AdvanceSlashes(_tusks, tick);
        }

        // ------------------------------------------------------------ events

        private void ReadEvents(Simulation sim)
        {
            // Индексом, а не foreach: перечислитель интерфейса аллоцировал бы каждый кадр.
            IReadOnlyList<FrameEventContext> events = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i].Event;
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        SwingStarted(sim, e.Source);
                        break;
                    case SimEventType.EnemyActionStarted:
                        if (e.ActionVariant == (int)EnemyActionKind.StonehoofTusk) TuskStarted(sim, e.Source);
                        else if (e.ActionVariant == (int)EnemyActionKind.WendigoSweep) SweepStarted(sim, e.Source);
                        else if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) PawStarted(sim, e.Source, e.Amount);
                        break;
                    case SimEventType.EnemyActionImpact:
                        if (e.ActionVariant == (int)EnemyActionKind.StonehoofTusk)
                            TuskLanded(sim, e.Source, events[i].SimulationTick - 1);
                        else if (e.ActionVariant == (int)EnemyActionKind.WendigoSweep)
                            LandAll(sim, e.Source, Sign.Sweep);
                        else if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw)
                            LandPaw(sim, e.Source, e.Amount);
                        break;
                    case SimEventType.EnemyActionCancelled:
                        if (e.ActionVariant == (int)EnemyActionKind.StonehoofTusk) DropAll(e.Source, Sign.Tusk);
                        else if (e.ActionVariant == (int)EnemyActionKind.WendigoSweep) DropAll(e.Source, Sign.Sweep);
                        else if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) DropAll(e.Source, Sign.Paw);
                        break;
                }
            }
        }

        private static bool IsEnemy(Simulation sim, int id)
            => id > Simulation.PlayerId && id < sim.Entities.Count && sim.Entities.Alive[id]
               && sim.Entities.Side[id] != sim.Entities.Side[Simulation.PlayerId];

        /// <summary>Общий ближний замах (Хранитель, Корнеполз, Расщепень, детёныш) — событие Attack.</summary>
        private void SwingStarted(Simulation sim, int id)
        {
            if (!IsEnemy(sim, id)) return;
            EnemyKind kind = sim.Entities.Kind[id];
            Sign sign;
            switch (kind)
            {
                case EnemyKind.None:
                case EnemyKind.ForestGuardian:
                    sign = Sign.Claw;
                    break;
                case EnemyKind.ForestRootSwarm:
                case EnemyKind.ForestSplitter:
                case EnemyKind.ForestSplitling:
                    sign = Sign.Bite;
                    break;
                default:
                    return;
            }
            if (!sim.TryGetEnemySwing(id, out EnemySwingState swing) || swing.HitResolved) return;
            Begin(id, kind, sign, swing.Serial, swing.StartTick, swing.ImpactTick, swing.Origin, swing.Direction, 0);
        }

        private void TuskStarted(Simulation sim, int id)
        {
            if (!IsEnemy(sim, id) || !sim.TryGetStonehoofTusk(id, out StonehoofTuskState tusk) || tusk.HitResolved) return;
            Begin(id, EnemyKind.ForestStonehoof, Sign.Tusk, tusk.Serial, tusk.StartTick, tusk.ImpactTick,
                tusk.Origin, tusk.Direction, 0);
        }

        private void SweepStarted(Simulation sim, int id)
        {
            if (!IsEnemy(sim, id) || !sim.TryGetWendigoAction(id, out WendigoActionState action)
                || action.Kind != WendigoAction.Sweep || action.HitResolved) return;
            // Круг когтей — обе лапы разом: угли на обеих.
            for (int hand = 1; hand <= 2; hand++)
                Begin(id, EnemyKind.ForestWendigo, Sign.Sweep, action.Serial, action.StartTick, action.ImpactTick,
                    action.Origin, action.Direction, hand);
        }

        /// <summary>
        /// Замах лапы Хозяина Чащи: уголь на когтях бьющей лапы. Номер лапы — Amount
        /// события (0 — правая, 1 — левая второй двойной); замах — от начала шага.
        /// </summary>
        private void PawStarted(Simulation sim, int id, int stage)
        {
            if (!IsEnemy(sim, id) || !sim.TryGetThicketMasterAction(id, out ThicketMasterState paw)
                || paw.Action != ThicketMasterAction.Paw || paw.Stage != stage || paw.HitResolved) return;
            int hand = ThicketMasterClipRules.PawIsRight(stage) ? 2 : 1;
            Begin(id, EnemyKind.ForestThicketMaster, Sign.Paw, paw.Serial, paw.StageStartTick, paw.ImpactTick,
                paw.Origin, paw.Direction, hand, stage);
        }

        /// <summary>Контакт лапы пришёл событием: уголь этой лапы вспыхивает, полосы когтей встают.</summary>
        private void LandPaw(Simulation sim, int id, int stage)
        {
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember e = _signs[i];
                if (e.Entity == id && e.Sign == Sign.Paw && e.Stage == stage && !e.Landed) Land(sim, e);
            }
        }

        /// <summary>
        /// Удар клыками пришёл событием: вспышка угля и дуга. Угля может не
        /// быть (слот вытеснен) — дуга встаёт по состоянию Sim.
        /// </summary>
        private void TuskLanded(Simulation sim, int id, int eventTick)
        {
            bool found = false;
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember e = _signs[i];
                if (e.Entity != id || e.Sign != Sign.Tusk || e.Landed) continue;
                found = true;
                Land(sim, e);
            }
            if (found || !sim.TryGetStonehoofTusk(id, out StonehoofTuskState tusk)) return;
            Rig rig = RigOf(id);
            Vector3 snout = rig != null && rig.Snout != null ? rig.Snout.position : _driver.GetRenderPosition(id) + Vector3.up * .55f;
            SpawnTusk(id, tusk.Serial, tusk.Direction, tusk.ImpactTick > 0 ? tusk.ImpactTick : eventTick, snout);
        }

        private void LandAll(Simulation sim, int id, Sign sign)
        {
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember e = _signs[i];
                if (e.Entity == id && e.Sign == sign && !e.Landed) Land(sim, e);
            }
        }

        private void DropAll(int id, Sign sign)
        {
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember e = _signs[i];
                if (e.Entity == id && e.Sign == sign && !e.Landed) Drop(e);
            }
        }

        // ------------------------------------------------------------ signs

        private void Begin(int id, EnemyKind kind, Sign sign, int serial, int start, int impact,
            FixVec2 origin, FixVec2 direction, int hand, int stage = 0)
        {
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember known = _signs[i];
                // Лапа Хозяина Чащи: свой уголь на каждый удар серии (П/Л/П — третий удар той же правой).
                if (known.Entity == id && known.Sign == sign && known.Serial == serial
                    && (sign == Sign.Paw ? known.Stage == stage : sign != Sign.Sweep || known.Hand == hand)) return;
            }
            Ember e = FreeSign();
            e.Entity = id; e.Kind = kind; e.Sign = sign; e.Serial = serial;
            e.StartTick = start; e.ImpactTick = Mathf.Max(start + 1, impact);
            e.Origin = origin; e.Direction = direction; e.Hand = hand; e.Stage = stage;
            e.SpawnFrame = Time.frameCount; e.Landed = false;
            Scales(kind, sign, out e.EmberScale, out e.SlashScale);
            e.Fx = Take(_embers, serial * 4 + hand, false);
            // След резервируется сразу, из события начала: в тик удара он только встаёт на место.
            // Лапа Хозяина Чащи — без полос: след когтей по дуге удара рисует его вид атак
            // (ThicketMasterCombatView, дуга CFXR); здесь только уголь, иначе два следа (ревью 02.10).
            e.Slash = sign == Sign.Claw ? Take(_claws, serial, true)
                : sign == Sign.Bite ? Take(_bites, serial, true) : null;
        }

        /// <summary>Свободный слот; если все горят — вытесняется самый старый замах.</summary>
        private Ember FreeSign()
        {
            Ember oldest = null;
            for (int i = 0; i < _signs.Length; i++)
            {
                Ember e = _signs[i];
                if (e.Entity < 0) return e;
                if (oldest == null || e.StartTick < oldest.StartTick) oldest = e;
            }
            Drop(oldest);
            return oldest;
        }

        private void UpdateSign(Simulation sim, Ember e, float tick)
        {
            if (e.Entity < 0) return;
            if (!e.Landed)
            {
                // Замах снят (оглушение, волок, смерть, сбитый замах) — знак гаснет сразу.
                if (!Winding(sim, e)) { Drop(e); return; }
                if (e.Sign == Sign.Paw) tick = PawClock(sim, e, tick);
                if (tick >= e.ImpactTick && Resolved(sim, e)) Land(sim, e);
            }

            float age;
            if (!e.Landed)
                age = EmberWindupSeconds * Mathf.Clamp01((tick - e.StartTick) / (e.ImpactTick - e.StartTick));
            else
            {
                age = EmberWindupSeconds + Mathf.Max(0f, tick - e.ImpactTick) / Simulation.TicksPerSecond;
                if (age > EmberWindupSeconds + EmberTailSeconds) { Drop(e); return; }
            }

            Rig rig = RigOf(e.Entity);
            if (e.Sign == Sign.Claw && e.Hand == 0) ResolveHand(e, rig, tick);
            if (e.Fx == null) return;
            Vector3 point = BonePoint(e, rig);
            if (_camera != null)
                point += (_camera.transform.position - point).normalized * (CameraNudge * e.EmberScale);
            Transform root = e.Fx.Root.transform;
            root.SetPositionAndRotation(point, Quaternion.identity);
            root.localScale = Vector3.one * e.EmberScale;
            Simulate(e.Fx, age);
        }

        /// <summary>
        /// Песочные Часы не оглушают Хозяина Чащи, а сдвигают его сроки на 60 тиков и держат его
        /// столько же. Уголь лапы до удара перечитывает начало и удар шага из Sim (иначе дотлел бы
        /// к старому удару, а след когтей встал бы двухсекундной давности), а пока босс стоит —
        /// часы угля стоят (ThicketMasterClipRules.BossClock).
        /// </summary>
        private static float PawClock(Simulation sim, Ember e, float tick)
        {
            if (sim.TryGetThicketMasterAction(e.Entity, out ThicketMasterState paw) && paw.Serial == e.Serial
                && paw.Action == ThicketMasterAction.Paw && paw.Stage == e.Stage && !paw.HitResolved)
            {
                e.StartTick = paw.StageStartTick;
                e.ImpactTick = Mathf.Max(paw.StageStartTick + 1, paw.ImpactTick);
            }
            return ThicketMasterClipRules.BossClock(sim, e.Entity, tick);
        }

        /// <summary>Замах с этим серийником всё ещё в Sim (до удара или в стойке после него).</summary>
        private static bool Winding(Simulation sim, Ember e)
        {
            if (e.Entity >= sim.Entities.Count || !sim.Entities.Alive[e.Entity]) return false;
            switch (e.Sign)
            {
                case Sign.Claw:
                case Sign.Bite:
                    return sim.TryGetEnemySwing(e.Entity, out EnemySwingState swing) && swing.Serial == e.Serial;
                case Sign.Tusk:
                    return sim.TryGetStonehoofTusk(e.Entity, out StonehoofTuskState tusk) && tusk.Serial == e.Serial;
                case Sign.Paw:
                    return sim.TryGetThicketMasterAction(e.Entity, out ThicketMasterState paw)
                           && paw.Serial == e.Serial && paw.Action == ThicketMasterAction.Paw && paw.Stage >= e.Stage;
                default:
                    return sim.TryGetWendigoAction(e.Entity, out WendigoActionState action)
                           && action.Serial == e.Serial && action.Kind == WendigoAction.Sweep;
            }
        }

        /// <summary>Sim провела удар этого замаха (контакт был, попал он или нет).</summary>
        private static bool Resolved(Simulation sim, Ember e)
        {
            switch (e.Sign)
            {
                case Sign.Claw:
                case Sign.Bite:
                    return sim.TryGetEnemySwing(e.Entity, out EnemySwingState swing)
                           && swing.Serial == e.Serial && swing.HitResolved;
                case Sign.Tusk:
                    return sim.TryGetStonehoofTusk(e.Entity, out StonehoofTuskState tusk)
                           && tusk.Serial == e.Serial && tusk.HitResolved;
                case Sign.Paw:
                    return sim.TryGetThicketMasterAction(e.Entity, out ThicketMasterState paw)
                           && paw.Serial == e.Serial && (paw.Stage > e.Stage || paw.HitResolved);
                default:
                    return sim.TryGetWendigoAction(e.Entity, out WendigoActionState action)
                           && action.Serial == e.Serial && action.HitResolved;
            }
        }

        /// <summary>Тик удара: уголь вспыхивает и гаснет хвостом, след встаёт от кости.</summary>
        private void Land(Simulation sim, Ember e)
        {
            if (e.Landed) return;
            e.Landed = true;
            Rig rig = RigOf(e.Entity);
            if (e.Sign == Sign.Claw && e.Hand == 0)
            {
                // Лапа так и не решилась (аниматора нет) — левая, как AttackA.
                int hand = HandFromAnimator(rig != null ? rig.Animator : null);
                e.Hand = hand != 0 ? hand : 1;
            }
            Vector3 point = BonePoint(e, rig);
            switch (e.Sign)
            {
                case Sign.Claw:
                case Sign.Paw:
                    PlaceClaw(e, point);
                    break;
                case Sign.Bite:
                    PlaceBite(e, point);
                    break;
                case Sign.Tusk:
                    SpawnTusk(e.Entity, e.Serial, e.Direction, e.ImpactTick, point);
                    break;
            }
            // След дальше живёт своим временем в пуле.
            e.Slash = null;
        }

        /// <summary>
        /// Знак уходит: замах снят до удара или хвост вспышки догорел. Уголь
        /// гаснет, пустой зарезервированный след возвращается в пул;
        /// поставленный след доживает сам.
        /// </summary>
        private static void Drop(Ember e)
        {
            if (e == null) return;
            Retire(e.Fx);
            if (e.Slash != null && !e.Slash.Placed) Retire(e.Slash);
            Clear(e);
        }

        private static void Clear(Ember e)
        {
            e.Entity = -1; e.Serial = 0; e.Fx = null; e.Slash = null; e.Landed = false; e.Hand = 0; e.Stage = 0;
        }

        // ------------------------------------------------------------ slashes

        /// <summary>
        /// Три полосы когтей: корень — у когтя в тик удара, +Z — к точке на
        /// 0,85 радиуса сектора по зафиксированному направлению (полосы сходятся
        /// туда, где стоял герой), длина подгоняется масштабом.
        /// </summary>
        private void PlaceClaw(Ember e, Vector3 claw)
        {
            if (e.Slash == null) return;
            Vector3 direction = Flat(e.Direction);
            // Лапа Хозяина Чащи — сектор 4,14 м и когти у самой земли: полосы выше и длиннее (рост ×1,15).
            bool paw = e.Sign == Sign.Paw;
            float radius = (paw ? Simulation.ThicketPawRadius : Simulation.GuardianSwingRadius).ToFloat();
            Vector3 aim = World(e.Origin) + direction * (radius * .85f);
            Vector3 flat = aim - claw; flat.y = 0f;
            float reach = flat.magnitude;
            Vector3 forward = reach > .3f ? flat / reach : direction;
            float scale = e.SlashScale * Mathf.Clamp((reach + .35f * e.SlashScale) / (ClawLength * e.SlashScale), .75f, 1.2f);
            Vector3 at = paw ? Lifted(claw, .69f, 2.53f) : Lifted(claw, .45f, 1.5f);
            Place(e.Slash, at, Rotation(forward), scale, e.ImpactTick);
            bool right = e.Hand == 2;
            if (e.Slash.Left != null) e.Slash.Left.enabled = !right;
            if (e.Slash.Right != null) e.Slash.Right.enabled = right;
        }

        /// <summary>Два серпа укуса смыкаются перед пастью, по направлению замаха.</summary>
        private void PlaceBite(Ember e, Vector3 jaw)
        {
            if (e.Slash == null) return;
            Vector3 direction = Flat(e.Direction);
            Vector3 at = Lifted(jaw + direction * (BiteAhead * e.SlashScale), .22f, 1.1f);
            Place(e.Slash, at, Rotation(direction), e.SlashScale, e.ImpactTick);
        }

        /// <summary>Дуга клыков: центр — кабан, высота — клыки, дуга перед мордой на радиусе удара.</summary>
        private void SpawnTusk(int id, int serial, FixVec2 direction, int impactTick, Vector3 snout)
        {
            Burst b = Take(_tusks, serial, false);
            if (b == null) return;
            Vector3 center = _driver.GetRenderPosition(id);
            center.y = snout.y;
            center = Lifted(center, .3f, 1.1f);
            float scale = Simulation.StonehoofTuskRadius.ToFloat() * .8f / TuskArcRadius;
            Place(b, center, Rotation(Flat(direction)), scale, impactTick);
        }

        private static void Place(Burst b, Vector3 position, Quaternion rotation, float scale, int tick)
        {
            b.Root.transform.SetPositionAndRotation(position, rotation);
            b.Root.transform.localScale = Vector3.one * scale;
            b.Tick = tick;
            b.Simulated = -1f;
            b.Placed = true;
            if (!b.Root.activeSelf) b.Root.SetActive(true);
        }

        private static void AdvanceSlashes(Pool pool, float tick)
        {
            for (int i = 0; i < pool.Items.Length; i++)
            {
                Burst b = pool.Items[i];
                if (b == null || !b.Placed || !b.Root.activeSelf) continue;
                float age = (tick - b.Tick) / Simulation.TicksPerSecond;
                if (age > b.Life) { Retire(b); continue; }
                Simulate(b, age);
            }
        }

        // ------------------------------------------------------------ pool

        /// <summary>
        /// Экземпляр из пула: свободный, иначе самый старый уже поставленный
        /// (зарезервированный чужим замахом не отбирается). Зерно систем
        /// меняется с номером замаха: удары не повторяют друг друга, а
        /// перемотка того же даёт тот же кадр.
        /// </summary>
        private static Burst Take(Pool pool, int salt, bool reserve)
        {
            Burst pick = null;
            for (int i = 0; i < pool.Items.Length; i++)
            {
                Burst b = pool.Items[i];
                if (b == null) continue;
                if (!b.Root.activeSelf) { pick = b; break; }
                if (b.Placed && (pick == null || b.Order < pick.Order)) pick = b;
            }
            if (pick == null) return null;
            pick.Order = ++pool.Taken;
            pick.Simulated = -1f;
            pick.Placed = !reserve;
            pick.Tick = int.MaxValue;
            for (int k = 0; k < pick.Particles.Length; k++)
            {
                var ps = pick.Particles[k];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = pick.Seeds[k] + (uint)salt * 7919u;
            }
            if (!pick.Root.activeSelf) pick.Root.SetActive(true);
            return pick;
        }

        private static void Retire(Burst b)
        {
            if (b == null) return;
            b.Simulated = -1f;
            b.Placed = false;
            b.Tick = int.MaxValue;
            if (b.Root.activeSelf) b.Root.SetActive(false);
        }

        /// <summary>
        /// Возраст эффекта в секундах времени префаба. Вперёд системы
        /// догоняются приращением, назад — перезапуском; на паузе возраст стоит,
        /// и частицы стоят. Отрицательный возраст — ещё рано: частиц нет.
        /// </summary>
        private static void Simulate(Burst b, float age)
        {
            if (age < 0f)
            {
                if (b.Simulated >= 0f)
                    for (int k = 0; k < b.Particles.Length; k++)
                        b.Particles[k].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                b.Simulated = -1f;
                return;
            }
            if (b.Simulated >= 0f && Mathf.Abs(age - b.Simulated) < 1e-5f) return;
            bool restart = b.Simulated < 0f || age < b.Simulated;
            float step = restart ? age : age - b.Simulated;
            for (int k = 0; k < b.Particles.Length; k++)
            {
                var ps = b.Particles[k];
                ps.Simulate(step, false, restart, false);
                ps.Pause(false);
            }
            b.Simulated = age;
        }

        private void ResetAll()
        {
            for (int i = 0; i < _signs.Length; i++) Clear(_signs[i]);
            foreach (var pool in new[] { _embers, _claws, _bites, _tusks })
                if (pool != null)
                    for (int i = 0; i < pool.Items.Length; i++) Retire(pool.Items[i]);
            _rigs.Clear();
        }

        // ------------------------------------------------------------ bones

        /// <summary>Кости тела сущности: бьющие лапы, голова, морда. Кэш — на тело пула.</summary>
        private Rig RigOf(int entity)
        {
            if (_arena == null || !_arena.TryGetEntityView(entity, out Transform view) || view == null) return null;
            if (_rigs.TryGetValue(view, out Rig rig)) return rig;
            rig = new Rig { Animator = view.GetComponentInChildren<Animator>(true) };
            Transform leftHand = null, rightHand = null, head0 = null, leftPaw = null, rightPaw = null;
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
            {
                // Mixamo пишет «mixamorig:Имя»: сравнивается хвост после двоеточия.
                string name = t.name;
                int colon = name.LastIndexOf(':');
                if (colon >= 0) name = name.Substring(colon + 1);
                switch (name)
                {
                    // Хранитель, Корнеполз (Mixamo): коготь — средний палец, ближе к кончикам.
                    case "LeftHandMiddle2": rig.LeftClaw = t; break;
                    case "RightHandMiddle2": rig.RightClaw = t; break;
                    // Запасные кисти; у Вендиго — единственные.
                    case "LeftHand": case "L_hand": leftHand = t; break;
                    case "RightHand": case "R_hand": rightHand = t; break;
                    // Mixamo «Head», Расщепень и Вендиго «head».
                    case "Head": case "head": if (rig.Head == null) rig.Head = t; break;
                    // Корнеполз: пасть на верхней груди (у Хранителя не используется —
                    // он бьёт когтем). У Расщепеня кости строчные — «spine», сюда не попадают.
                    case "Spine2": rig.Mouth = t; break;
                    // Камнекопыт: голова и её конец — морда с клыками.
                    case "head0": head0 = t; break;
                    case "head0_end": rig.Snout = t; break;
                    // Хозяин Чащи: когти передних лап (100% веса пальцев), запасные — запястья.
                    case "leg_front_L_toe": rig.PawLeft = t; break;
                    case "leg_front_R_toe": rig.PawRight = t; break;
                    case "leg_front_L_paw": leftPaw = t; break;
                    case "leg_front_R_paw": rightPaw = t; break;
                }
            }
            if (rig.PawLeft == null) rig.PawLeft = leftPaw;
            if (rig.PawRight == null) rig.PawRight = rightPaw;
            if (rig.LeftClaw == null) rig.LeftClaw = leftHand;
            if (rig.RightClaw == null) rig.RightClaw = rightHand;
            if (rig.Head == null) rig.Head = head0;
            if (rig.Snout == null) rig.Snout = head0;
            _rigs[view] = rig;
            return rig;
        }

        /// <summary>
        /// Какой лапой бьёт Хранитель: AttackA (Mutant Swiping) — левой,
        /// AttackB (зеркало) — правой. Вариант выбирает вид тела, в Sim его нет.
        /// </summary>
        private static int HandFromAnimator(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return 0;
            AnimatorStateInfo state = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash == AttackBState) return 2;
            if (state.fullPathHash == AttackAState) return 1;
            return 0;
        }

        private static void ResolveHand(Ember e, Rig rig, float tick)
        {
            // Переход в AttackA/B ставится в кадре события — аниматор примет его в следующем.
            if (Time.frameCount <= e.SpawnFrame) return;
            int hand = HandFromAnimator(rig != null ? rig.Animator : null);
            if (hand != 0) { e.Hand = hand; return; }
            if (tick - e.StartTick > (e.ImpactTick - e.StartTick) * HandDecideShare) e.Hand = 1;
        }

        /// <summary>Где горит уголь: на кости вида, без неё — у тела по направлению замаха.</summary>
        private Vector3 BonePoint(Ember e, Rig rig)
        {
            Vector3 forward = Flat(e.Direction);
            switch (e.Sign)
            {
                case Sign.Claw:
                case Sign.Sweep:
                {
                    Transform hand = rig == null ? null : e.Hand == 2 ? rig.RightClaw : rig.LeftClaw;
                    if (hand != null) return hand.position;
                    break;
                }
                case Sign.Bite:
                    if (rig != null && rig.Mouth != null) return rig.Mouth.position + forward * (MouthReach * e.EmberScale);
                    if (rig != null && rig.Head != null) return rig.Head.position + forward * (JawReach * e.EmberScale);
                    break;
                case Sign.Tusk:
                    if (rig != null && rig.Snout != null) return rig.Snout.position;
                    break;
                case Sign.Paw:
                {
                    Transform toe = rig == null ? null : e.Hand == 2 ? rig.PawRight : rig.PawLeft;
                    if (toe != null) return toe.position;
                    // Заглушка (капсула без костей): лапа впереди тела у земли, сбоку по стороне.
                    Vector3 lateral = Vector3.Cross(Vector3.up, forward) * (e.Hand == 2 ? 1.2f : -1.2f);
                    return _driver.GetRenderPosition(e.Entity) + Vector3.up * .5f + forward * 1.9f + lateral;
                }
            }
            // Запасная точка: тело Sim, высота и вынос по виду знака.
            Vector3 body = _driver.GetRenderPosition(e.Entity);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float side = e.Hand == 2 ? .45f : e.Hand == 1 ? -.45f : 0f;
            switch (e.Sign)
            {
                case Sign.Bite: return body + (Vector3.up * .45f + forward * .55f) * e.EmberScale;
                case Sign.Tusk: return body + (Vector3.up * .55f + forward * 1.0f) * e.EmberScale;
                case Sign.Sweep: return body + (Vector3.up * 1.3f + right * side * 1.3f) * e.EmberScale;
                default: return body + (Vector3.up * 1.1f + forward * .5f + right * side) * e.EmberScale;
            }
        }

        // ------------------------------------------------------------ helpers

        private static void Scales(EnemyKind kind, Sign sign, out float ember, out float slash)
        {
            switch (sign)
            {
                case Sign.Bite:
                    switch (kind)
                    {
                        case EnemyKind.ForestSplitter: ember = .95f; slash = 1.15f; return;
                        case EnemyKind.ForestSplitling: ember = .6f; slash = .72f; return;
                        default: ember = .72f; slash = .9f; return;
                    }
                case Sign.Tusk: ember = 1.1f; slash = 1f; return;
                case Sign.Sweep: ember = 1.05f; slash = 1f; return;
                // Хозяин Чащи 4,14 м (×1,15, 02.10) — вдвое выше Хранителя, сектор лапы 4,14 м против его удара.
                // Ревью 02.10 (вечер): «обозначить, чтоб лапа была видимее и читаемее» — уголь на когтях
                // крупнее роста (×2,6 против ×2,07); главный знак удара — красный сектор на земле от плеча
                // (метка Sim SharedView, рисует GroundTelegraphView).
                case Sign.Paw: ember = 2.6f; slash = 2.19f; return;
                default: ember = 1f; slash = 1f; return;
            }
        }

        private static Vector3 Flat(FixVec2 direction)
        {
            var v = new Vector3(direction.X.ToFloat(), 0f, direction.Y.ToFloat());
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static Quaternion Rotation(Vector3 forward) => Quaternion.LookRotation(forward, Vector3.up);

        private Vector3 World(FixVec2 at)
        {
            float x = at.X.ToFloat(), z = at.Y.ToFloat();
            return new Vector3(x, Ground(x, z), z);
        }

        private float Ground(float x, float z) => _layout != null ? _layout.WeaponGroundHeight(x, z) : 0f;

        /// <summary>Высота над землёй в пределах [min, max]: след не уходит под траву и не висит над головой.</summary>
        private Vector3 Lifted(Vector3 point, float min, float max)
        {
            float ground = Ground(point.x, point.z);
            point.y = ground + Mathf.Clamp(point.y - ground, min, max);
            return point;
        }

        private void OnDestroy()
        {
            foreach (var pool in new[] { _embers, _claws, _bites, _tusks })
                if (pool != null)
                    for (int i = 0; i < pool.Items.Length; i++)
                        if (pool.Items[i] != null && pool.Items[i].Root != null) Destroy(pool.Items[i].Root);
        }
    }
}
