using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ВИД БОЯ (каркас, 02.10). Здесь только привязка и разбор событий Sim;
    /// эффекты атак — частичные методы ниже, их реализует ThicketMasterCombatView.Vfx.cs
    /// (префабы собирает ThicketMasterVfxSetup). Звука нет. Пустой частичный метод
    /// компилятор выбрасывает: без реализации хук ничего не делает.
    ///
    /// Ставится на объект арены (ArenaView.PrepareForestMob, семья босса) одной строкой
    /// <see cref="EnsureOn"/>. Привязывается к живому Хозяину Чащи сам: ищет сущность
    /// вида ForestThicketMaster, тело — через ArenaView.TryGetEntityView, вид тела —
    /// ThicketMasterAnimatorView (у серой заглушки его нет — тогда кости и бугор пустые).
    ///
    /// События — EnemyAction* с EnemyActionKind.Thicket* (SimEvent.cs, 11…19), тик
    /// каждого — тик Sim, в котором оно родилось (FrameEventContext.SimulationTick − 1):
    /// эффект, поставленный по нему, повторяется съёмкой и держит паузу.
    /// </summary>
    [DefaultExecutionOrder(660)]
    [RequireComponent(typeof(TickDriver))]
    public sealed partial class ThicketMasterCombatView : MonoBehaviour
    {
        private TickDriver _driver;
        private ArenaView _arena;
        private Simulation _shown;
        private int _boss = -1;
        private Transform _bossBody;
        private ThicketMasterAnimatorView _bossView;
        private bool _killed;

        public static ThicketMasterCombatView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<ThicketMasterCombatView>();
            return view != null ? view : host.AddComponent<ThicketMasterCombatView>();
        }

        /// <summary>Сущность босса; −1 — на арене его нет.</summary>
        public int Boss => _boss;

        /// <summary>Вид тела босса; null — заглушка или тела нет.</summary>
        public ThicketMasterAnimatorView BossView => _bossView;

        /// <summary>Босс под землёй (нырок): тело спрятано, видно только бугор.</summary>
        public bool BossBurrowed => _bossView != null && _bossView.IsBurrowed;

        /// <summary>Где бугор нырка (тело Sim под землёй).</summary>
        public Vector3 MoundPosition => _bossView != null ? _bossView.MoundPosition
            : _boss >= 0 && _driver != null ? _driver.GetRenderPosition(_boss) : Vector3.zero;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
            // Буря цветения: весь пол — опасность, круги света вырезаны (ревью 02.10, п. 12).
            ThicketStormDangerView.EnsureOn(gameObject);
            // Вступление-кат-сцена: камера, полосы, HUD, титр (владелец 02.10).
            ThicketMasterIntroView.EnsureOn(gameObject);
        }

        private void LateUpdate()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim != _shown)
            {
                _shown = sim;
                Unbind();
                OnArenaReset();
            }
            if (sim == null) return;
            BindBoss(sim);
            ReadEvents(sim);
            if (_boss >= 0) UpdateCrownDither(sim);
            OnFrame(sim, sim.Tick - 1 + _driver.Alpha);
        }

        // ------------------------------------------------------------ binding

        private void BindBoss(Simulation sim)
        {
            var entities = sim.Entities;
            if (_boss >= 0 && (_boss >= entities.Count || entities.Kind[_boss] != EnemyKind.ForestThicketMaster)) Unbind();
            if (_boss < 0)
                for (int id = 1; id < entities.Count; id++)
                    if (entities.Kind[id] == EnemyKind.ForestThicketMaster && entities.Alive[id])
                    {
                        _boss = id;
                        _killed = false;
                        OnBossBound(id);
                        break;
                    }
            if (_boss < 0) return;
            // Тело могло смениться (пул, новое поколение): вид тела ищется заново только тогда.
            Transform body = null;
            if (_arena != null) _arena.TryGetEntityView(_boss, out body);
            if (body == _bossBody) return;
            _bossBody = body;
            _bossView = null;
            if (body != null) body.TryGetComponent(out _bossView);
        }

        private void Unbind()
        {
            _boss = -1;
            _bossBody = null;
            _bossView = null;
            _killed = false;
        }

        // ------------------------------------------------------------ events

        private void ReadEvents(Simulation sim)
        {
            // Индексом: перечислитель интерфейса аллоцировал бы каждый кадр.
            IReadOnlyList<FrameEventContext> events = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i].Event;
                int tick = events[i].SimulationTick - 1;
                if (e.Type == SimEventType.Death)
                {
                    if (e.Target == _boss && _boss >= 0 && !_killed)
                    {
                        _killed = true;
                        // Тело играет смерть с тика события, даже если ArenaView ещё не позвал.
                        if (_bossView != null) _bossView.PlayDeath(tick);
                        OnBossKilled(_boss, tick, ToWorld(e.Position));
                    }
                    continue;
                }
                if (e.Type != SimEventType.EnemyActionStarted && e.Type != SimEventType.EnemyActionImpact
                    && e.Type != SimEventType.EnemyActionCancelled) continue;
                var kind = (EnemyActionKind)e.ActionVariant;
                if (!ThicketMasterClipRules.IsThicketKind(kind)) continue;
                if (e.Source < 0 || e.Source >= sim.Entities.Count || sim.Entities.Kind[e.Source] != EnemyKind.ForestThicketMaster)
                    continue;
                Vector3 at = ToWorld(e.Position);
                switch (e.Type)
                {
                    case SimEventType.EnemyActionStarted: Started(sim, e.Source, kind, e.Amount, tick, at); break;
                    case SimEventType.EnemyActionImpact: Impact(e.Source, kind, e.Amount, e.Flag, tick, at); break;
                    default: OnActionCancelled(e.Source, kind, e.Amount, tick); break;
                }
            }
        }

        private void Started(Simulation sim, int boss, EnemyActionKind kind, int amount, int tick, Vector3 at)
        {
            sim.TryGetThicketMasterAction(boss, out ThicketMasterState a);
            switch (kind)
            {
                case EnemyActionKind.ThicketWake: OnWake(boss, tick); break;
                // Tag рёва — биты порогов, которые он закрыл (вступление, 66, 50, 33).
                case EnemyActionKind.ThicketRoar: OnRoarWindup(boss, tick, a.Tag, a.ImpactTick); break;
                case EnemyActionKind.ThicketPaw:
                    OnPawWindup(boss, tick, amount, ThicketMasterClipRules.PawIsRight(amount), a.ImpactTick);
                    break;
                // Amount 0 — дыбом (круг 5,2), 1 — метка кольца 5,2–7,5 встала в тик удара круга.
                case EnemyActionKind.ThicketStomp: if (amount == 0) OnStompWindup(boss, tick, a.ImpactTick); break;
                case EnemyActionKind.ThicketDive:
                    // Amount 0 — уход в землю, 1 — бугор поехал, 2 — круг лёг (Position — его центр).
                    if (amount == 0) OnDiveBurrow(boss, tick);
                    else if (amount == 1) OnMoundTravel(boss, tick);
                    else OnDiveLocked(boss, tick, at, a.ImpactTick);
                    break;
                // Жест каста — Amount 0; круг прорастания k / залп ливня v фоновой опасности —
                // Amount k (метка 0 встаёт под событием жеста). Центры — TryGetThicketShape.
                case EnemyActionKind.ThicketSprout:
                    if (amount == 0) OnSproutCast(boss, tick);
                    OnSproutMarked(boss, tick, amount);
                    break;
                case EnemyActionKind.ThicketPollen: OnPollenCast(boss, tick, a.ImpactTick); break;
                case EnemyActionKind.ThicketRain:
                    if (amount == 0) OnRainCast(boss, tick);
                    OnRainMarked(boss, tick, amount);
                    break;
                case EnemyActionKind.ThicketStorm:
                    // Amount 0 — начало (круги первой волны), 1 — круги второй волны.
                    if (amount == 0) OnStormBegin(boss, tick, a.ImpactTick);
                    else OnStormSecondWaveMarked(boss, tick, a.LastImpactTick);
                    break;
            }
        }

        private void Impact(int boss, EnemyActionKind kind, int amount, bool hit, int tick, Vector3 at)
        {
            switch (kind)
            {
                case EnemyActionKind.ThicketPaw: OnPawImpact(boss, tick, amount, ThicketMasterClipRules.PawIsRight(amount), at, hit); break;
                case EnemyActionKind.ThicketStomp: OnStompImpact(boss, tick, amount, at, hit); break;
                case EnemyActionKind.ThicketRoar: OnRoarBlast(boss, tick, at, hit); break;
                case EnemyActionKind.ThicketDive: OnEmerge(boss, tick, at, hit); break;
                case EnemyActionKind.ThicketSprout: OnSproutImpact(boss, tick, amount, at, hit); break;
                case EnemyActionKind.ThicketPollen: OnPollenLand(boss, tick, amount, at, hit); break;
                case EnemyActionKind.ThicketRain: OnRainVolley(boss, tick, amount, at, hit); break;
                case EnemyActionKind.ThicketStorm: OnStormWave(boss, tick, amount, at, hit); break;
            }
        }

        // ------------------------------------------------------------ crown

        /// <summary>
        /// Сквозь босса видно героя (владелец 02.10: «прозрачность должна быть, чтоб было
        /// видно»): доля 0…1 — тело между героем и камерой (лучи к камере через рамку тела),
        /// появление за 0,15 с; сама прозрачность — сетка в шейдере тела, кроны, куста и
        /// накладок фаз, только перед героем. Тело не высветляется. Реализация —
        /// ThicketMasterCombatView.SeeThrough.cs.
        /// </summary>
        private void UpdateCrownDither(Simulation sim)
        {
            float amount = SeeThroughFade(sim);
            ApplyCrownDither(_boss, _bossView, amount);
        }

        // ------------------------------------------------------------ helpers

        private static Vector3 ToWorld(FixVec2 at) => new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat());

        // ------------------------------------------------------------ hooks (VFX и звук — позже)
        //
        // tick — тик Sim события; impactTick — когда ударит (для эффектов замаха,
        // которые доходят до пика ровно к удару); at — место по Sim (земля, y = 0).

        /// <summary>Новая симуляция или её нет: снять всё своё.</summary>
        partial void OnArenaReset();

        /// <summary>Нашёлся живой босс на арене.</summary>
        partial void OnBossBound(int boss);

        /// <summary>Каждый кадр после событий: тик Sim с долей кадра.</summary>
        partial void OnFrame(Simulation sim, float tick);

        /// <summary>Пробуждение: вырывает лапы из земли (30 тиков), за ним рёв.</summary>
        partial void OnWake(int boss, int tick);

        /// <summary>Замах рёва: thresholds — биты ThicketRoar*Bit, которые он закрывает.</summary>
        partial void OnRoarWindup(int boss, int tick, int thresholds, int impactTick);

        /// <summary>Рёв: кольцо 2,3–6,3 м, отброс без урона.</summary>
        partial void OnRoarBlast(int boss, int tick, Vector3 at, bool hit);

        /// <summary>Замах удара серии лапы: stage — номер удара (чётный — правая, нечётный — левая). Уголь на пальцах — EnemyBodyTelegraphView, дуга когтей — здесь (.Vfx).</summary>
        partial void OnPawWindup(int boss, int tick, int stage, bool right, int impactTick);

        /// <summary>Контакт лапы: at — точка удара Sim (центр сектора). След когтей — дуга CFXR из OnPawWindup (полос EnemyBodyTelegraphView у лапы нет).</summary>
        partial void OnPawImpact(int boss, int tick, int stage, bool right, Vector3 at, bool hit);

        /// <summary>Подъём на дыбы перед топотом (метка круга 5,2 м — GroundTelegraphView).</summary>
        partial void OnStompWindup(int boss, int tick, int impactTick);

        /// <summary>Топот: ring 0 — лапы в землю, круг 5,2 м; ring 1 — второе кольцо 5,2–7,5 м через 15 тиков.</summary>
        partial void OnStompImpact(int boss, int tick, int ring, Vector3 at, bool hit);

        /// <summary>Нырок: уход в землю (12 тиков DiveIn).</summary>
        partial void OnDiveBurrow(int boss, int tick);

        /// <summary>Тело под землёй: бугор поехал к герою (MoundPosition).</summary>
        partial void OnMoundTravel(int boss, int tick);

        /// <summary>Круг выхода зафиксирован под героем: at — его центр.</summary>
        partial void OnDiveLocked(int boss, int tick, Vector3 at, int impactTick);

        /// <summary>Выход из-под земли (= удар): at — где вылез.</summary>
        partial void OnEmerge(int boss, int tick, Vector3 at, bool hit);

        /// <summary>Каст прорастания: лапы в землю (жест 18 тиков), круги по следам героя идут сами.</summary>
        partial void OnSproutCast(int boss, int tick);

        /// <summary>Встал круг прорастания index (0–5): центр и удар — TryGetThicketShape(boss, index).</summary>
        partial void OnSproutMarked(int boss, int tick, int index);

        /// <summary>Удар круга прорастания index (0–5) в at.</summary>
        partial void OnSproutImpact(int boss, int tick, int index, Vector3 at, bool hit);

        /// <summary>Каст пыльцы: крона трясётся, облака падают в impactTick.</summary>
        partial void OnPollenCast(int boss, int tick, int impactTick);

        /// <summary>Облако пыльцы легло: slot — Simulation.TryGetThicketPollenZone.</summary>
        partial void OnPollenLand(int boss, int tick, int slot, Vector3 at, bool hit);

        /// <summary>Каст ягодного ливня (жест 18 тиков; 5 залпов по 4 круга идут сами).</summary>
        partial void OnRainCast(int boss, int tick);

        /// <summary>Встал залп volley: круги 4·volley … 4·volley + 3 — TryGetThicketShape; ягоды вылетают из куста.</summary>
        partial void OnRainMarked(int boss, int tick, int volley);

        /// <summary>Удар залпа volley (0–4); at — круг, задевший героя, или первый.</summary>
        partial void OnRainVolley(int boss, int tick, int volley, Vector3 at, bool hit);

        /// <summary>Буря цветения: крона раскрывается, круги света первой волны (TryGetThicketShape 0–2).</summary>
        partial void OnStormBegin(int boss, int tick, int firstWaveTick);

        /// <summary>Круги света второй волны встали (места 3–5).</summary>
        partial void OnStormSecondWaveMarked(int boss, int tick, int secondWaveTick);

        /// <summary>Волна бури wave (0–1): бьёт всех вне кругов света.</summary>
        partial void OnStormWave(int boss, int tick, int wave, Vector3 at, bool hit);

        /// <summary>Действие снято до удара (смерть, смерть героя): погасить его замах.</summary>
        partial void OnActionCancelled(int boss, EnemyActionKind kind, int stage, int tick);

        /// <summary>Босс убит: такт убийства — EnemyPresentationProfile.Kill, тело — ThicketMasterAnimatorView.</summary>
        partial void OnBossKilled(int boss, int tick, Vector3 at);

        /// <summary>Сетка перед героем: amount 0 — тело целиком, 1 — сквозь него виден герой (SeeThrough.cs).</summary>
        partial void ApplyCrownDither(int boss, ThicketMasterAnimatorView body, float amount);
    }
}
