using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шквал v2 «морская пена» (владелец 02.10: «резкое быстрое… очень органично»,
    /// формы как аспекты Hades). Целевые кадры — выбор владельца,
    /// ART/characters/pelag/squall-forms-2026-10-02/chatgpt-results:
    ///  • trail-wavy-1, trail-zigzag-2 — Пенный след и база: на каждый прыжок
    ///    короткая пенная струя от старта до посадки, быстро тает; в форме струя
    ///    растекается в полосу, которая живёт, бьёт и замедляет;
    ///  • elusive-return-3 — Неуловимый: пенный двойник на точке каста, дуга
    ///    возврата водой, всплески на целях;
    ///  • Охота — на тех же кадрах: всплеск добивания и знак лишнего прыжка.
    ///
    /// ВСЁ рождается от событий Sim (SquallJump/Strike/HuntKill/Return/Ended/
    /// FoamStrip, Damage), а не от анимации и не опросом тиков: в сборке плеера
    /// опрос однажды не рисовал слои Рассекающего. Отложенные шаги (всплеск удара
    /// и знаки Охоты — после всех событий кадра и не раньше тика показа удара, корона
    /// возврата — в миг, когда тело встало) — тот же приём, что корона рывка
    /// (PelagVfxController.Dash): тик показа sim.Tick − 2 + Alpha, как кадр 6 клипа Squall2.
    ///
    /// Удар: всплеск на теле — префаб всплеска серии сабли (та же семья, сторона по
    /// прямому/обратному удару), у ног цели — кольцо пены (Editor/PelagSquallFoamVfxSetup);
    /// последний удар — крупнее, с короной и толчком камеры. Охота: на добитом — всплеск
    /// и кольцо добивания, у героя — корона и стоячая волна-толчок, на следующей цели —
    /// метка добычи. Форма Шквала — PelagSquallFormLook (одна таблица цветов).
    /// Прежний путь (VFX_Pelag_Squall_* CFXR, ChainStepHop → BeginChainHop,
    /// PlaySquallImpact) остаётся запасным, пока префабы Шквала v2 не собраны.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Всплеск на теле: обычный удар, последний, добивание Охоты.</summary>
        private const float SquallSplashScale = 1.15f, SquallFinalSplashScale = 1.5f, SquallKillSplashScale = 1.7f;
        /// <summary>Стоп-кадр цели, с: обычный удар и последний (как у серии сабли).</summary>
        private const float SquallHitHold = .05f, SquallFinalHold = .08f;
        /// <summary>Корона у ног: посадка возврата и толчок Охоты.</summary>
        private const float SquallReturnCrownScale = 1.05f, SquallHuntCrownScale = 1.25f;
        /// <summary>Стоячая волна-толчок Охоты (префаб стоячей волны серии, размер в м) и выключатель.</summary>
        private const float SquallSurgeSize = 1.1f;
        private static readonly bool SquallHuntSurge = true;
        private const int SquallJumpMemory = 16;
        /// <summary>Номер «прыжка» дуги возврата в струях.</summary>
        private const int SquallReturnIndex = 1000;

        /// <summary>Префабы Шквала v2 собраны и в библиотеке — новый путь; иначе прежний.</summary>
        private bool SquallVfxReady => _pools != null && (int)PelagVfxId.SquallWater < _pools.Length
            && _pools[(int)PelagVfxId.SquallWater] != null;

        private int _sqSerial = -1, _sqSlot = -1, _sqFlightIndex = -1;
        private PelagForm _sqForm;
        private Vector3 _sqOrigin, _sqLastStop;
        private bool _sqHasStop;
        private readonly bool[] _sqBackhand = new bool[SquallJumpMemory];

        // Удар кадра: всплески рисуются после всех событий кадра — тогда известно,
        // последний ли он (за SquallStrike с Amount 0 может прийти лишний прыжок Охоты), —
        // и не раньше тика показа удара: тело героя рисуется на тик позже Sim
        // (sim.Tick − 2 + Alpha, как корона рывка и кадр 6 клипа Squall2), и всплеск
        // по тику события вставал бы за тик до контакта, пока герой ещё в полёте (до 0,66 м).
        private struct SquallHit { public int Target; public Vector3 Body; }
        private readonly SquallHit[] _sqHits = new SquallHit[8];
        private int _sqHitCount;
        private bool _sqStrikePending, _sqPendingLanded;
        private int _sqPendingIndex, _sqPendingTarget, _sqPendingLeft, _sqHuntKillIndex = -1;
        private int _sqPendingTick;
        private Simulation _sqPendingSim;
        // Охота: добивание показывается вместе с ударом, по тику показа (корона — у стоящей ноги).
        private bool _sqHuntPending;
        private int _sqHuntIndex, _sqHuntTarget, _sqHuntBonus;
        private Vector3 _sqHuntCorpse;

        // Посадка возврата: корона под ногой и растворение двойника — в миг, когда тело встало.
        private bool _sqLandingPending;
        private int _sqLandingTick;
        private Vector3 _sqLandingAt;
        private Simulation _sqLandingSim;

        private System.Func<float, float, float> _sqGround;
        private float _sqGroundBase;

        /// <summary>Awake: прогрев пулов Шквала v2 (несколько струй, колец и всплесков рождаются в один кадр).</summary>
        private void PrepareSquallVfx()
        {
            if (_pools == null) return;
            PelagVfxId[] ids =
            {
                PelagVfxId.SquallWater, PelagVfxId.SquallStrike, PelagVfxId.SquallFinish, PelagVfxId.SquallSplash,
                PelagVfxId.SquallCrown, PelagVfxId.SquallSurge, PelagVfxId.SquallMark, PelagVfxId.SquallCue, PelagVfxId.SquallGhost
            };
            foreach (PelagVfxId id in ids)
                if ((int)id < _pools.Length && _pools[(int)id] != null) _pools[(int)id].Pool.PrewarmStep(16);
        }

        /// <summary>Событие Шквала v2. True — разобрано здесь (новые типы прежний вид не знает).</summary>
        private bool ConsumeSquallEvent(in SimEvent e, int index)
        {
            switch (e.Type)
            {
                case SimEventType.SquallJump: if (SquallVfxReady) PlaySquallJump(e, SquallEventTick(index)); return true;
                case SimEventType.SquallStrike: if (SquallVfxReady) PlaySquallStrike(e, SquallEventTick(index)); return true;
                case SimEventType.SquallHuntKill: if (SquallVfxReady) PlaySquallHuntKill(e); return true;
                case SimEventType.SquallReturn: if (SquallVfxReady) PlaySquallReturn(e, SquallEventTick(index)); return true;
                case SimEventType.SquallEnded: if (SquallVfxReady) EndSquallVfx(e); return true;
                case SimEventType.SquallFoamStrip: if (SquallVfxReady) BindSquallFoamStrip(e, SquallEventTick(index)); return true;
                // Импульс урона полосы — укус пены у ног; DamageOverTime видят и другие (цифры, звук).
                case SimEventType.DamageOverTime: if (SquallVfxReady) NipSquallFoam(e); return false;
                default: return false;
            }
        }

        /// <summary>
        /// Урон Шквала (Damage по слоту Шквала). True — новый путь: всплеск и стоп-кадр
        /// будут после всех событий кадра (FlushSquallStrike), прежний PlaySquallImpact не нужен.
        /// </summary>
        private bool TakeSquallDamage(in SimEvent e)
        {
            if (!SquallVfxReady) return false;
            if (_sqHitCount < _sqHits.Length)
                _sqHits[_sqHitCount++] = new SquallHit { Target = e.Target, Body = EntityPosition(e.Target, e.Position) };
            return true;
        }

        /// <summary>Тик Sim события кадра (контекст кадра ставит тик после шага).</summary>
        private int SquallEventTick(int index)
        {
            var contexts = _driver.FrameEventContexts;
            if (contexts != null && index < contexts.Count) return contexts[index].SimulationTick - 1;
            return _driver.Sim != null ? _driver.Sim.Tick - 1 : 0;
        }

        private static Vector3 SquallWorld(FixVec2 p, float y) => new Vector3(p.X.ToFloat(), y, p.Y.ToFloat());

        /// <summary>Новый каст (номер серии в снимке сменился): форма слота, точка каста.</summary>
        private void BeginSquallCast(Simulation sim, in SquallState squall)
        {
            _sqSerial = squall.Serial;
            _sqSlot = squall.Slot;
            _sqForm = sim.FormAt(squall.Slot);
            _sqOrigin = SquallWorld(squall.Origin, PlayerPosition().y);
            _sqHasStop = false;
            _sqHuntKillIndex = -1;
            _sqLandingPending = false;
            _sqFlightIndex = -1;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] cast serial={squall.Serial} slot={squall.Slot} form={_sqForm} origin={_sqOrigin.ToString("F2")}");
        }

        /// <summary>Старт прыжка: струя от старта к посадке растёт за героем; Неуловимый — двойник.</summary>
        private void PlaySquallJump(in SimEvent e, int tick)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            SquallState squall = sim.Squall;
            if (squall.Serial != _sqSerial) BeginSquallCast(sim, squall);
            int index = e.ActionVariant;
            _sqBackhand[index % SquallJumpMemory] = e.Flag;
            _sqFlightIndex = index;
            float y = PlayerPosition().y;
            Vector3 from = squall.Index == index && squall.Phase == SquallPhase.Flight ? SquallWorld(squall.From, y)
                : index == 0 ? _sqOrigin : _sqHasStop ? _sqLastStop : PlayerPosition();
            Vector3 to = SquallWorld(e.Position, y);
            PelagSquallWater.Kind kind = _sqForm == PelagForm.SquallFoamTrail ? PelagSquallWater.Kind.Trail : PelagSquallWater.Kind.Streak;
            BeginSquallWater(index, tick, from, to, kind);
            if (_sqForm == PelagForm.SquallElusive && !CaptureRig.NoVfx)
                SpawnSquallGhost(index == 0 ? _sqOrigin : from, index == 0);
            if (_sqMarkForJump == index) { _sqMarkForJump = -1; MarkSquallPrey(e.Target); }
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] jump {index} tick={tick} flight={e.Amount} back={e.Flag} from={from.ToString("F2")} to={to.ToString("F2")} kind={kind}");
        }

        /// <summary>Прибытие и удар: струя перестаёт расти; всплески — по тику показа удара (FlushSquallStrike).</summary>
        private void PlaySquallStrike(in SimEvent e, int tick)
        {
            // Прежний удар (≥ 4 тиков назад) уже показан; если кадр был длинным — показать сейчас.
            FlushSquallStrike(true);
            int index = e.ActionVariant;
            Vector3 at = SquallWorld(e.Position, PlayerPosition().y);
            _sqLastStop = at;
            _sqHasStop = true;
            EndSquallWaterAt(index, at);
            _sqStrikePending = true;
            _sqPendingIndex = index;
            _sqPendingTarget = e.Target;
            _sqPendingLeft = e.Amount;
            _sqPendingLanded = e.Flag;
            _sqPendingTick = tick;
            _sqPendingSim = _driver.Sim;
        }

        /// <summary>Тик, по которому нарисовано тело героя (как PelagSquallClipRules.ShownTick и корона рывка).</summary>
        private float SquallShownTick(Simulation sim) => sim.Tick - 2 + _driver.Alpha;

        /// <summary>
        /// Всплески удара — после всех событий кадра: SquallStrike, Damage, Death и
        /// SquallHuntKill приходят одним тиком, и только в конце видно, последний ли
        /// удар (Охота за добивание даёт лишний прыжок). И не раньше тика показа удара:
        /// тогда клип стоит на кадре 6, а тело — в точке посадки. <paramref name="force"/> —
        /// показать сразу (следующий удар или конец серии пришли раньше).
        /// </summary>
        private void FlushSquallStrike(bool force = false)
        {
            if (!_sqStrikePending)
            {
                // Урон без удара (витрина, прежний каст) — обычные всплески.
                for (int i = 0; i < _sqHitCount; i++) SpawnSquallBodySplash(_sqHits[i].Target, _sqHits[i].Body, false, false, SquallSplashScale);
                _sqHitCount = 0;
                if (_sqHuntPending) PlaySquallHuntCues();
                return;
            }
            Simulation sim = _driver.Sim;
            if (sim == null || sim != _sqPendingSim)
            {
                // Арену сменили, пока удар ждал показа: тел уже нет — ничего не рождаем.
                _sqStrikePending = _sqHuntPending = false;
                _sqHitCount = 0;
                return;
            }
            if (!force && SquallShownTick(sim) < _sqPendingTick) return;
            _sqStrikePending = false;
            bool killedByHunt = _sqHuntKillIndex == _sqPendingIndex;
            bool final = PelagSquallFoamRules.IsFinalStrike(_sqPendingLeft, _sqPendingIndex, _sqHuntKillIndex);
            bool backhand = _sqBackhand[_sqPendingIndex % SquallJumpMemory];
            for (int i = 0; i < _sqHitCount; i++)
            {
                SquallHit hit = _sqHits[i];
                if (!CaptureRig.NoVfx) _arena.HoldEntityPose(hit.Target, final ? SquallFinalHold : SquallHitHold);
                // Добитому Охотой всплеск добивания уже дан (PlaySquallHuntKill).
                if (killedByHunt && hit.Target == _sqPendingTarget) continue;
                SpawnSquallBodySplash(hit.Target, hit.Body, backhand, final, final ? SquallFinalSplashScale : SquallSplashScale);
            }
            _sqHitCount = 0;
            if (_sqPendingLanded && !killedByHunt)
                SpawnSquallRing(final ? PelagVfxId.SquallFinish : PelagVfxId.SquallStrike, _sqPendingTarget, backhand,
                    EntityPosition(_sqPendingTarget, _sqLastStop));
            ReleaseSquallPrey(_sqPendingTarget);
            if (final && !CaptureRig.NoVfx) _juice?.PunchCamera(.22f, .05f);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] strike {_sqPendingIndex} tick={_sqPendingTick} shown={SquallShownTick(sim):F2} target={_sqPendingTarget}"
                    + $" landed={_sqPendingLanded} left={_sqPendingLeft} final={final} hunt={killedByHunt}");
            if (_sqHuntPending) PlaySquallHuntCues();
        }

        /// <summary>
        /// Охота: удар убил. Запоминается здесь, показывается вместе с ударом (FlushSquallStrike,
        /// тик показа): всплеск добивания, толчок у героя, метка на следующей цели.
        /// </summary>
        private void PlaySquallHuntKill(in SimEvent e)
        {
            _sqHuntKillIndex = e.ActionVariant;
            if (CaptureRig.NoVfx) return;
            _sqHuntPending = true;
            _sqHuntIndex = e.ActionVariant;
            _sqHuntTarget = e.Target;
            _sqHuntBonus = e.Amount;
            _sqHuntCorpse = EntityPosition(e.Target, e.Position);
            // Удара не ждём (не должно быть: SquallHuntKill идёт за SquallStrike того же тика) — сразу.
            if (!_sqStrikePending) PlaySquallHuntCues();
        }

        private void PlaySquallHuntCues()
        {
            _sqHuntPending = false;
            if (CaptureRig.NoVfx) return;
            int index = _sqHuntIndex;
            Vector3 corpse = _sqHuntCorpse;
            bool backhand = _sqBackhand[index % SquallJumpMemory];
            SpawnSquallBodySplash(_sqHuntTarget, corpse, backhand, true, SquallKillSplashScale);
            SpawnSquallRing(PelagVfxId.SquallFinish, _sqHuntTarget, backhand, corpse);
            // Знак лишнего прыжка: корона у ноги и волна за спиной, толкающая к следующей цели.
            int next = SquallNextTarget(_driver.Sim, index);
            Vector3 hero = PlayerPosition();
            Vector3 forward = next >= 0 ? FlatDirection(hero, EntityPosition(next, hero + PlayerFacing())) : PlayerFacing();
            SpawnSquallCrown(SquallLeadFoot(hero, forward), forward, SquallHuntCrownScale);
            if (SquallHuntSurge) SpawnSquallSurge(hero, forward);
            if (next >= 0) MarkSquallPrey(next);
            else _sqMarkForJump = index + 1;
            _juice?.PunchCamera(.26f, .05f);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] hunt kill {index} target={_sqHuntTarget} bonus={_sqHuntBonus} next={next}");
        }

        /// <summary>Следующая цель после удара <paramref name="index"/> по снимку Sim; −1 — в этом кадре ещё не выбрана.</summary>
        private static int SquallNextTarget(Simulation sim, int index)
        {
            if (sim == null) return -1;
            SquallState squall = sim.Squall;
            if (squall.Index == index && squall.NextTarget >= 0) return squall.NextTarget;
            if (squall.Index == index + 1 && squall.Phase == SquallPhase.Flight) return squall.Target;
            return -1;
        }

        private readonly Vector3[] _sqPath = new Vector3[4];

        /// <summary>Прыжок назад к точке каста: дуга водой по пути Sim (Via0, Via1), посадка — по тику прибытия.</summary>
        private void PlaySquallReturn(in SimEvent e, int tick)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            SquallState squall = sim.Squall;
            if (squall.Serial != _sqSerial) BeginSquallCast(sim, squall);
            float y = PlayerPosition().y;
            Vector3 origin = SquallWorld(e.Position, y);
            bool live = squall.Phase == SquallPhase.Return;
            _sqPath[0] = live ? SquallWorld(squall.From, y) : PlayerPosition();
            int count = 1;
            if (live && squall.ViaCount == 2 && e.ActionVariant == 2)
            {
                _sqPath[count++] = SquallWorld(squall.Via0, y);
                _sqPath[count++] = SquallWorld(squall.Via1, y);
            }
            _sqPath[count++] = origin;
            _sqFlightIndex = SquallReturnIndex;
            BeginSquallWater(SquallReturnIndex, tick, _sqPath, count, count > 2, PelagSquallWater.Kind.Return);
            _sqLandingPending = true;
            _sqLandingSim = sim;
            _sqLandingTick = tick + Mathf.Max(1, e.Amount);
            _sqLandingAt = origin;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] return tick={tick} ticks={e.Amount} via={e.ActionVariant} land={_sqLandingTick} origin={origin.ToString("F2")}");
        }

        /// <summary>Серия кончилась: струи доживают, двойник растворяется (если не ждёт посадки), метки гаснут.</summary>
        private void EndSquallVfx(in SimEvent e)
        {
            // Серия сорвана в тик удара — удар показывается сразу, не дожидаясь тика показа.
            FlushSquallStrike(true);
            EndAllSquallWater();
            var reason = (SquallEnd)e.Amount;
            if (reason == SquallEnd.Interrupted) _sqLandingPending = false;
            if (!_sqLandingPending) DissolveSquallGhosts(false);
            ReleaseSquallPrey(-1);
            _sqMarkForJump = -1;
            _sqFlightIndex = -1;
            if (CaptureRig.HasEnemyOverride) Debug.Log($"[squall-vfx] ended serial={e.ActionVariant} reason={reason}");
        }

        /// <summary>Кадр Шквала v2 (LateUpdate после разбора событий и следа рывка).</summary>
        private void UpdateSquallVfx()
        {
            FlushSquallStrike(false);
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            float now = sim.Tick - 1 + _driver.Alpha;
            float dt = Time.deltaTime;
            UpdateSquallLanding(sim);
            UpdateSquallWater(now, dt);
            UpdateSquallCues(sim, dt);
            UpdateSquallGhosts(now, dt);
        }

        /// <summary>Тело встало на точке каста (тик показа = тик прибытия): корона под ногой, двойник впитан.</summary>
        private void UpdateSquallLanding(Simulation sim)
        {
            if (!_sqLandingPending) return;
            if (sim != _sqLandingSim) { _sqLandingPending = false; return; }
            float shown = sim.Tick - 2 + _driver.Alpha;
            if (shown < _sqLandingTick) return;
            _sqLandingPending = false;
            EndSquallWaterAt(SquallReturnIndex, _sqLandingAt);
            Vector3 hero = PlayerPosition();
            Vector3 forward = FlatDirection(_sqHasStop ? _sqLastStop : hero - PlayerFacing(), _sqLandingAt);
            if (!CaptureRig.NoVfx) SpawnSquallCrown(SquallLeadFoot(hero, forward), forward, SquallReturnCrownScale);
            DissolveSquallGhosts(true);
        }

        // ---- рождение объектов Шквала

        /// <summary>Объект пула в точке; масштаб — доля авторского. −1 — эффектов нет (NoVfx) или пула нет.</summary>
        private int SquallSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            // Принятые префабы семьи в своих пулах Шквала: тело капли — цвет частицы, блок ниже
            // красит лишь кромку. До Begin — всплеск рождается уже в цвете формы.
            if (id == PelagVfxId.SquallSplash || id == PelagVfxId.SquallCrown || id == PelagVfxId.SquallSurge)
                PelagSquallFormLook.ApplyDrops(go, _sqForm);
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            PelagSquallFormLook.Apply(go, _sqForm);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        /// <summary>
        /// Всплеск пены на теле — префаб всплеска серии сабли: брызги по ходу клинка.
        /// Прямой удар — справа налево, обратный — слева направо; последний и добивание —
        /// вперёд и вверх (как добивающий серии).
        /// </summary>
        private void SpawnSquallBodySplash(int target, Vector3 body, bool backhand, bool final, float scale)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 position = body + Vector3.up * .9f;
            Camera camera = Camera.main;
            // Перед поверхностью тела, иначе всплеск тонет в модели.
            if (camera != null) position += (camera.transform.position - position).normalized * .45f;
            Vector3 radial = Vector3.ProjectOnPlane(body - PlayerPosition(), Vector3.up);
            radial = radial.sqrMagnitude > .0001f ? radial.normalized : PlayerFacing();
            Vector3 right = Vector3.Cross(Vector3.up, radial);
            Vector3 along = final ? radial + Vector3.up * .6f : (backhand ? right : -right) + radial * .35f + Vector3.up * .35f;
            SquallSpawn(PelagVfxId.SquallSplash, position, Quaternion.LookRotation(along.normalized), scale, 0f, out _);
        }

        /// <summary>
        /// Кольцо пены у ног цели (кадры: пенный водоворот вокруг каждого задетого).
        /// Корень на земле, +Z — ход клинка; завиток по сторону удара: у префаба два
        /// слоя закрутки, лишний гасится сразу после выдачи. Размер — по телу цели.
        /// </summary>
        private void SpawnSquallRing(PelagVfxId id, int target, bool backhand, Vector3 body)
        {
            if (CaptureRig.NoVfx) return;
            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            _sqGroundBase = PlayerPosition().y;
            var at = new Vector3(body.x, SquallGroundAt(body.x, body.z) + .02f, body.z);
            Vector3 radial = FlatDirection(PlayerPosition(), at);
            Vector3 right = Vector3.Cross(Vector3.up, radial);
            Vector3 slash = backhand ? right : -right;
            float scale = PelagSquallFoamRules.RingScale(radius);
            int fx = SquallSpawn(id, at, Quaternion.LookRotation(slash, Vector3.up), scale, 0f, out GameObject go);
            if (fx < 0) return;
            Transform unused = go.transform.Find(backhand ? "SwirlA" : "SwirlB");
            if (unused != null && unused.TryGetComponent(out ParticleSystem swirl))
                swirl.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>Корона пены у передней ноги — префаб короны рывка (А5).</summary>
        private void SpawnSquallCrown(Vector3 foot, Vector3 forward, float scale)
            => SquallSpawn(PelagVfxId.SquallCrown, foot, Quaternion.LookRotation(forward, Vector3.up), scale, 0f, out _);

        /// <summary>
        /// Охота: стоячая волна за спиной героя толкает его в лишний прыжок (кадр Охоты:
        /// «гребень встаёт из всплеска добивания и бросает Пелага дальше»). Префаб стоячей
        /// волны добивающего серии; как у неё — билборд к камере, повёрнутый по проекции хода.
        /// </summary>
        private void SpawnSquallSurge(Vector3 hero, Vector3 forward)
        {
            Camera camera = Camera.main;
            Vector3 center = hero - forward * .25f + Vector3.up * .9f;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(forward, Vector3.up), Vector3.up);
            if (camera != null)
            {
                Vector3 a = camera.WorldToScreenPoint(center);
                Vector3 b = camera.WorldToScreenPoint(center + forward);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, angle) * Quaternion.Euler(18f, 0f, 0f);
                center += (camera.transform.position - center).normalized * .5f;
            }
            int fx = SquallSpawn(PelagVfxId.SquallSurge, center, rotation, 0f, 0f, out GameObject go);
            if (fx >= 0) go.transform.localScale = Vector3.one * SquallSurgeSize;
        }

        private Transform _sqFootBody, _sqFoot, _sqFootScale;
        private const string SquallFootBone = "mixamorig:LeftFoot";

        /// <summary>
        /// Под передней (всегда левой) ногой: кость левой стопы, иначе её точка числом —
        /// клипы Шквала v2 ставят левую лодыжку на контакте там же, где рывок
        /// (timing.json: 0,476 вперёд и 0,163 влево в Blender = 0,444/0,131 м на теле ×1,82).
        /// </summary>
        private Vector3 SquallLeadFoot(Vector3 root, Vector3 forward)
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && body != _sqFootBody)
            {
                _sqFootBody = body;
                _sqFoot = null;
                foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    if (bone.name == SquallFootBone) { _sqFoot = bone; break; }
                Animator animator = body.GetComponentInChildren<Animator>(true);
                _sqFootScale = animator != null ? animator.transform : body;
            }
            _sqGroundBase = root.y;
            if (_sqFoot != null && _sqFoot.gameObject.activeInHierarchy)
            {
                Vector3 bone = _sqFoot.position;
                return new Vector3(bone.x, SquallGroundAt(bone.x, bone.z) + .02f, bone.z);
            }
            float scale = _sqFootScale != null ? _sqFootScale.lossyScale.y : 1f;
            Vector3 left = Vector3.Cross(forward, Vector3.up);
            Vector3 foot = root + (forward * .244f + left * .072f) * scale;
            foot.y = SquallGroundAt(foot.x, foot.z) + .02f;
            return foot;
        }

        /// <summary>
        /// Земля под водой Шквала: в лагере — навигация (рельеф неровный), в разломе — пол
        /// показанной арены с уступами (как у лежащей воды форм Вихря); иначе — высота ног.
        /// </summary>
        private float SquallGroundAt(float x, float z)
        {
            GameSession session = _driver.Session;
            if (session != null && session.Mode == GameMode.Camp)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(x, _sqGroundBase + 1f, z), out UnityEngine.AI.NavMeshHit hit,
                        2.5f, UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position.y;
                return _sqGroundBase;
            }
            LayoutView layout = LayoutView.Shown;
            return layout != null ? layout.WeaponGroundHeight(x, z) : _sqGroundBase;
        }

        private bool SquallStillActive(int fx, GameObject go)
            => fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;
    }
}
