using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 «морская пена» (02.10; владелец: «резкое быстрое», тело органично).
    /// Целевые кадры — ART/characters/pelag/abordage-2026-10-02/chatgpt-results:
    ///  • A-base — натянутая прямая цепь, вдоль неё бирюзовая нить воды и капли,
    ///    всплеск пены на укусе крюка, низкий рывок с пенным следом (как у рывка);
    ///  • D-quake — Обвал: кобальтовое кольцо воды от кулака в землю, комья земли;
    ///  • F-geyser-seagreen — Гейзер: столб морской зелени подбрасывает цель, пенная
    ///    шапка, кольцо всплесков там, где вода упадёт;
    ///  • G-breach — Пробоина: конус-струя мадженты за спиной цели, веер капель.
    ///
    /// ВСЁ рождается от событий Sim (AbordageThrow/Hook/Punch/Quake/GeyserLift/
    /// GeyserFall/Breach/Ended 56–63 и Damage слота) и ставится в очередь до тика
    /// показа: тело героя рисуется на sim.Tick − 2 + Alpha, и укус, удар и фронт
    /// формы встают ровно в его тик (приём удара Шквала v2 и короны рывка). Якорь,
    /// цепь и вода на ней — PelagVfxController.AbordageAnchor; формы —
    /// .AbordageForms (Обвал, Пробоина) и .AbordageGeyser. Цвета — одна таблица
    /// PelagAbordageFormLook. Прежний путь (PlayAnchorLeapTo, хоп, полосы, «волны
    /// давления») остаётся запасным, пока префабы Абордажа v2 не собраны.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Префабы Абордажа v2 собраны и в библиотеке — новый путь; иначе прежний.</summary>
        private bool AbordageVfxReady => _pools != null && (int)PelagVfxId.AbordageRibbon < _pools.Length
            && _pools[(int)PelagVfxId.AbordageRibbon] != null;

        private enum AbKind : byte { Bite, Punch, Quake, Breach, GeyserLift, GeyserFall, Hit, End }

        /// <summary>Шаг, ждущий тика показа.</summary>
        private struct AbPending
        {
            public AbKind Kind;
            public AbordageVfxHit Hit;
            public int Tick, Target, Serial, Amount, Extra;
            public bool Flag;
            public Vector3 At, Dir;
            public PelagForm Form;
        }

        private const int AbQueueSize = 48;
        private readonly AbPending[] _abQueue = new AbPending[AbQueueSize];
        private int _abQueued;
        private Simulation _abSim;
        /// <summary>Слот последнего каста Абордажа (AbilityCast приходит раньше выпуска якоря).</summary>
        private int _abCastSlot = -1;

        // Кто бит (PelagAbordageVfxRules.ClassifyHit): последнее событие тика и окно хода фронта.
        private AbordageVfxCue _abLastCue;
        private int _abLastCueTick = -1, _abFistTarget = -1;
        private AbordageVfxCue _abWaveCue;
        private int _abWaveStart = -1, _abWaveTravel;

        /// <summary>Awake: прогрев пулов (несколько всплесков, корон и следов рождаются в один кадр).</summary>
        private void PrepareAbordageVfx()
        {
            if (_pools == null) return;
            PelagVfxId[] ids =
            {
                PelagVfxId.AbordageRibbon, PelagVfxId.AbordageSplash, PelagVfxId.AbordageCrown, PelagVfxId.AbordageKnock,
                PelagVfxId.AbordageWake, PelagVfxId.AbordageDrag, PelagVfxId.AbordageQuake, PelagVfxId.AbordageBreach,
                PelagVfxId.AbordageGeyser, PelagVfxId.AbordageBurst, PelagVfxId.AbordageAnchor, PelagVfxId.AbordageChain
            };
            foreach (PelagVfxId id in ids)
                if ((int)id < _pools.Length && _pools[(int)id] != null) _pools[(int)id].Pool.PrewarmStep(8);
        }

        /// <summary>Событие Абордажа v2. True — разобрано здесь (новые типы прежний вид не знает).</summary>
        private bool ConsumeAbordageEvent(in SimEvent e, int index)
        {
            switch (e.Type)
            {
                case SimEventType.AbordageThrow: if (AbordageVfxReady) AbordageThrown(e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageHook: if (AbordageVfxReady) AbordageHooked(e, AbordageEventTick(index)); return true;
                case SimEventType.AbordagePunch: if (AbordageVfxReady) AbordagePunched(e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageQuake: if (AbordageVfxReady) AbordageForm(AbKind.Quake, e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageBreach: if (AbordageVfxReady) AbordageForm(AbKind.Breach, e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageGeyserLift: if (AbordageVfxReady) AbordageForm(AbKind.GeyserLift, e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageGeyserFall: if (AbordageVfxReady) AbordageForm(AbKind.GeyserFall, e, AbordageEventTick(index)); return true;
                case SimEventType.AbordageEnded: if (AbordageVfxReady) AbordageEndedEvent(e, AbordageEventTick(index)); return true;
                default: return false;
            }
        }

        /// <summary>Тик Sim события кадра (контекст кадра ставит тик после шага).</summary>
        private int AbordageEventTick(int index)
        {
            var contexts = _driver.FrameEventContexts;
            if (contexts != null && index < contexts.Count) return contexts[index].SimulationTick - 1;
            return _driver.Sim != null ? _driver.Sim.Tick - 1 : 0;
        }

        private static Vector3 AbordageWorld(FixVec2 p, float y) => new Vector3(p.X.ToFloat(), y, p.Y.ToFloat());

        /// <summary>AbilityCast Абордажа (PlayGameplayAbility): запомнить слот; прежний недосмотанный якорь — отдать поясу.</summary>
        private void BeginAbordageVfxCast(int slot)
        {
            _abCastSlot = slot;
            if (_abRun.Active) FinishAbordageRun(false);
        }

        private void Enqueue(in AbPending pending)
        {
            // Очередь полна (очень длинный кадр): старейший шаг показывается сразу.
            if (_abQueued >= _abQueue.Length) RunAbordageCue(0, true);
            _abQueue[_abQueued++] = pending;
        }

        private void AbordageThrown(in SimEvent e, int tick)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            AbordageState state = sim.Abordage;
            bool live = state.Serial == e.ActionVariant;
            int slot = live ? state.Slot : _abCastSlot;
            BeginAbordageRun(sim, e.ActionVariant, slot, e.Target, tick, tick + Mathf.Max(1, e.Amount),
                AbordageWorld(e.Position, PlayerPosition().y));
        }

        private void AbordageHooked(in SimEvent e, int tick)
        {
            float y = PlayerPosition().y;
            if (_abRun.Active && _abRun.Serial == e.ActionVariant)
            {
                Simulation sim = _driver.Sim;
                bool live = sim != null && sim.Abordage.Serial == e.ActionVariant;
                _abRun.BiteTick = tick;
                _abRun.ArriveTick = tick + Mathf.Max(1, e.Amount);
                _abRun.Target = e.Target;
                _abRun.Hull = e.Flag;
                _abRun.HasHook = true;
                _abRun.To = AbordageWorld(e.Position, y);
                _abRun.From = live ? AbordageWorld(sim.Abordage.From, y) : PlayerPosition();
            }
            Enqueue(new AbPending { Kind = AbKind.Bite, Tick = tick, Target = e.Target, Serial = e.ActionVariant, Flag = e.Flag });
        }

        private void AbordagePunched(in SimEvent e, int tick)
        {
            int serial = _abRun.Active ? _abRun.Serial : -1;
            var form = (PelagForm)e.ActionVariant;
            if (_abRun.Active)
            {
                _abRun.ArriveTick = tick;
                _abRun.Punched = true;
                _abRun.Landed = e.Flag;
                _abRun.Target = e.Target;
            }
            _abLastCue = AbordageVfxCue.Punch;
            _abLastCueTick = tick;
            _abFistTarget = e.Target;
            Enqueue(new AbPending
            {
                Kind = AbKind.Punch, Tick = tick, Target = e.Target, Serial = serial, Flag = e.Flag, Form = form,
                At = AbordageWorld(e.Position, PlayerPosition().y)
            });
        }

        /// <summary>Событие формы: запомнить для «кто бит», показ — по тику показа.</summary>
        private void AbordageForm(AbKind kind, in SimEvent e, int tick)
        {
            float y = PlayerPosition().y;
            var pending = new AbPending
            {
                Kind = kind, Tick = tick, Target = e.Target, Amount = e.Amount, Extra = e.ActionVariant, Flag = e.Flag,
                At = AbordageWorld(e.Position, y), Serial = _abRun.Active ? _abRun.Serial : -1
            };
            switch (kind)
            {
                case AbKind.Quake:
                    _abLastCue = AbordageVfxCue.Quake;
                    _abWaveCue = AbordageVfxCue.Quake; _abWaveStart = tick; _abWaveTravel = Mathf.Max(1, e.Amount);
                    pending.Form = PelagForm.AbordageQuake;
                    break;
                case AbKind.Breach:
                    _abLastCue = AbordageVfxCue.Breach;
                    _abWaveCue = AbordageVfxCue.Breach; _abWaveStart = tick; _abWaveTravel = Mathf.Max(1, e.Amount);
                    pending.Form = PelagForm.AbordageBreach;
                    // Ось струи — из снимка фронта Sim; без него — от героя к вершине.
                    Simulation sim = _driver.Sim;
                    Vector3 dir = FlatDirection(PlayerPosition(), pending.At);
                    if (sim != null && sim.TryGetAbordageWave(out _, out FixVec2 wave, out _) && wave.LengthSq.Raw != 0)
                        dir = new Vector3(wave.X.ToFloat(), 0f, wave.Y.ToFloat()).normalized;
                    pending.Dir = dir;
                    break;
                case AbKind.GeyserLift:
                    _abLastCue = AbordageVfxCue.GeyserLift;
                    pending.Form = PelagForm.AbordageGeyser;
                    break;
                case AbKind.GeyserFall:
                    _abLastCue = AbordageVfxCue.GeyserFall;
                    pending.Form = PelagForm.AbordageGeyser;
                    break;
            }
            _abLastCueTick = tick;
            Enqueue(pending);
        }

        private void AbordageEndedEvent(in SimEvent e, int tick)
        {
            Enqueue(new AbPending
            {
                Kind = AbKind.End, Tick = tick, Serial = e.ActionVariant, Amount = e.Amount,
                At = AbordageWorld(e.Position, PlayerPosition().y)
            });
        }

        /// <summary>
        /// Damage слота Абордажа (PelagVfxController.ConsumeSimEvents): чей удар — по правилу
        /// ClassifyHit; кулак уже нарисован ударом, остальное — в очередь до тика показа.
        /// </summary>
        private void TakeAbordageDamage(in SimEvent e, int tick)
        {
            if (!AbordageVfxReady) return;
            AbordageVfxHit hit = PelagAbordageVfxRules.ClassifyHit(_abLastCue, _abLastCueTick, _abFistTarget, tick, e.Target,
                _abWaveCue, _abWaveStart, _abWaveTravel);
            if (hit == AbordageVfxHit.Fist) return;
            Enqueue(new AbPending
            {
                Kind = AbKind.Hit, Hit = hit, Tick = tick, Target = e.Target, Serial = _abRun.Active ? _abRun.Serial : -1,
                At = EntityPosition(e.Target, e.Position)
            });
        }
    }
}
