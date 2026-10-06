using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 «морская пена» (03.10; спека artifacts/wreck/plan/SPEC.md 5). Целевые кадры —
    /// ART/characters/pelag/wreck-2026-10-03/chatgpt-results: A-base (вал по полосе, сбитые),
    /// B-breakwater-seagreen (стена несёт врагов), C-ninth-wave (горб индиго), D-shell (жемчужная
    /// оболочка). Владелец: «все 4 как ульты» — в игре размер по числам Sim, не по кадру.
    ///
    /// ВСЁ рождается от событий Sim (WreckStage 11; WreckSlam … WreckEnded 64–71; AbilityCast и
    /// ActionStageStarted слота Крушения; Damage слота) и ставится в очередь до тика показа
    /// sim.Tick − 2 + Alpha — тело героя и голова якоря нарисованы на нём (приём Абордажа v2).
    /// Пенная дуга за головой якоря — .WreckArc (путь головы, которую ведёт риг якоря, кадр за
    /// кадром в позднем кадре PelagWreckLateHook; тело маха — серп пака CFXR «sword_trail 180
    /// thick», .WreckSweep), удар оземь, вал, стена, горб — .WreckWave (короткая трещина —
    /// .WreckSweep), Водяной панцирь — .WreckShell, знаки на телах — .WreckCues. Цвета — одна
    /// таблица PelagWreckFormLook. Дуга и серп рисуются, только пока голову ведёт риг (новые клипы
    /// Wreck2): на прежнем пути голову ведёт PelagAnchorSlamView со своим следом — дуги нет, чтобы
    /// не смешивать новую воду со старыми WreckA/B/Finish. Прежний путь (AnchorLeapLand на 2,25 м
    /// по WreckStage, PlaySquallImpact по Damage) остаётся запасным, пока префабы Крушения v2 не
    /// собраны (PelagWreckFoamVfxSetup).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Префабы Крушения v2 собраны и в библиотеке — новый путь; иначе прежний.</summary>
        private bool WreckVfxReady => _pools != null && (int)PelagVfxId.WreckCrest < _pools.Length
            && _pools[(int)PelagVfxId.WreckCrest] != null && _pools[(int)PelagVfxId.WreckArc] != null;

        private enum WkKind : byte { Stage, Contact, Slam, ChargeStart, ChargeRelease, Catch, Crash, ShellHit, Burst, Hit, End }

        /// <summary>Полоса и фронт удара оземь — снимок Sim в миг события (поля живут до следующего удара).</summary>
        private struct WkWave
        {
            public Vector3 Origin, Dir, Impact;
            public FixVec2 ImpactFix;
            public float Start, Step, End, HalfWidth, ImpactRadius;
            public int Tick, Travel, DamagePercent;
            public bool Stopped;
            public PelagForm Form;
            /// <summary>Форма слота как есть (Form сводит Призрачный якорь к базе): краска выпада на стеке сабли.</summary>
            public PelagForm Look;
        }

        /// <summary>Шаг, ждущий тика показа.</summary>
        private struct WkPending
        {
            public WkKind Kind;
            public WreckVfxHit Hit;
            public int Tick, Target, Serial, Stage, Amount, Extra, Slot, Contact, Overhead;
            public bool Flag;
            public Vector3 At;
            public PelagForm Form;
            public WkWave Wave;
            public float BaseHalf;
        }

        private WkPending[] _wkQueue = new WkPending[PelagWreckVfxRules.CueQueueStart];
        private int _wkQueued;
        private Simulation _wkSim;
        private PelagWreckLateHook _wkLate;

        // Кто бит (PelagWreckVfxRules.ClassifyHit): последнее событие тика и последний удар оземь.
        private WreckVfxCue _wkLastCue;
        private int _wkLastCueTick = -1, _wkCueTarget = -1;
        private WkWave _wkWave;
        private bool _wkHasWave;

        /// <summary>Awake: прогрев пулов и поздний кадр дуги (после рига якоря).</summary>
        private void PrepareWreckVfx()
        {
            if (_pools == null) return;
            PelagVfxId[] ids =
            {
                PelagVfxId.WreckArc, PelagVfxId.WreckCrest, PelagVfxId.WreckSlam, PelagVfxId.WreckShell,
                PelagVfxId.WreckSplash, PelagVfxId.WreckCrown, PelagVfxId.WreckKnock, PelagVfxId.WreckBurst,
                PelagVfxId.WreckSwing, PelagVfxId.WreckCrack,
                // «Холодное железо» (база 06.10, .WreckIron).
                PelagVfxId.WreckIronSlam, PelagVfxId.WreckIronStreak, PelagVfxId.WreckIronHit, PelagVfxId.WreckIronKnock,
                // Махи «холодного железа» (06.10, .WreckSwing).
                PelagVfxId.WreckSwingArc, PelagVfxId.WreckSwingHit,
                // Махи v4 на технике сабли и выпад «просто» (06.10, .WreckIronSwing, .WreckLunge).
                PelagVfxId.WreckIronSwing, PelagVfxId.WreckIronSwingHeavy, PelagVfxId.WreckIronSwingHit, PelagVfxId.WreckLunge,
                // Крушение на рисованных текстурах (06.10 вечер, .WreckPainted, .WreckPaintedLunge).
                PelagVfxId.WreckPaintedSwing, PelagVfxId.WreckPaintedSwingHeavy, PelagVfxId.WreckPaintedHit, PelagVfxId.WreckPaintedLunge,
                // Махи и знак на стеке серии сабли (06.10 поздно, .WreckCombo).
                PelagVfxId.WreckComboSwing, PelagVfxId.WreckComboSwingHeavy, PelagVfxId.WreckComboHit,
                // Выпад на стеке серии сабли (06.10 поздно, .WreckComboLunge).
                PelagVfxId.WreckComboLunge
            };
            foreach (PelagVfxId id in ids)
                if ((int)id < _pools.Length && _pools[(int)id] != null) _pools[(int)id].Pool.PrewarmStep(8);
            _wkLate = GetComponent<PelagWreckLateHook>();
            if (_wkLate == null) _wkLate = gameObject.AddComponent<PelagWreckLateHook>();
            _wkLate.Late = LateWreckVfx;
        }

        private static Vector3 WreckWorld(FixVec2 p, float y) => new Vector3(p.X.ToFloat(), y, p.Y.ToFloat());

        private bool IsWreckSlot(Simulation sim, int slot)
        {
            if (sim == null || (uint)slot >= (uint)Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == AbilityDefinition.WreckId;
        }

        private void WreckEnqueue(in WkPending pending)
        {
            // Большая толпа: очередь растёт (каждый Damage — свой шаг), и только у потолка (очень длинный
            // кадр) старейший шаг показывается сразу, раньше тика показа.
            if (_wkQueued >= _wkQueue.Length)
            {
                if (_wkQueue.Length < PelagWreckVfxRules.CueQueueMax)
                    System.Array.Resize(ref _wkQueue, System.Math.Min(PelagWreckVfxRules.CueQueueMax, _wkQueue.Length * 2));
                else RunWreckCue(0, true);
            }
            _wkQueue[_wkQueued++] = pending;
        }

        private void WreckCue(WreckVfxCue cue, int tick, int target)
        {
            _wkLastCue = cue;
            _wkLastCueTick = tick;
            _wkCueTarget = target;
        }

        /// <summary>
        /// Событие Крушения. True — разобрано здесь: новые типы 64–71 прежний вид не знает; WreckStage
        /// забирается, только когда новый путь готов (иначе прежний AnchorLeapLand). Каст и этап слота
        /// Крушения только подсматриваются (false) — их дальше разбирает PlayGameplayAbility.
        /// Зовётся ДО проверки «источник — герой»: у WreckShellHit источник — тот, кто бил.
        /// </summary>
        private bool ConsumeWreckEvent(in SimEvent e, int index)
        {
            Simulation sim = _driver.Sim;
            bool ready = WreckVfxReady;
            int tick = EventTick(index);
            if (e.Type == SimEventType.WreckShellHit)
            {
                if (e.Target != Simulation.PlayerId) return false;
                if (ready) WreckEnqueue(new WkPending
                {
                    Kind = WkKind.ShellHit, Tick = tick, Target = e.Source, Amount = e.Amount, Flag = e.Flag,
                    At = WreckWorld(e.Position, PlayerPosition().y), Serial = sim != null ? sim.Wreck.Serial : -1
                });
                return true;
            }
            if (e.Source != Simulation.PlayerId) return false;
            switch (e.Type)
            {
                case SimEventType.AbilityCast:
                case SimEventType.ActionStageStarted:
                    if (ready && IsWreckSlot(sim, e.Amount)) WreckStageStarted(sim, e, tick);
                    return false;
                case SimEventType.WreckStage:
                    if (!ready) return false;
                    WreckCue(e.Amount < 2 ? WreckVfxCue.Swing : e.Amount == 2 ? WreckVfxCue.Slam : WreckVfxCue.Fourth, tick, -1);
                    WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.Contact, Tick = tick, Stage = e.Amount, Flag = e.Flag,
                        Serial = sim != null ? sim.Wreck.Serial : -1, At = PlayerPosition(),
                        // Направление этапа в тик удара (махи «железа» на технике сабли ложатся по нему).
                        Wave = new WkWave { Dir = sim != null ? WreckWorld(sim.Wreck.Direction, 0f) : PlayerFacing() }
                    });
                    return true;
                case SimEventType.WreckSlam:
                    if (ready) WreckSlammed(sim, e, tick);
                    return true;
                case SimEventType.WreckChargeStarted:
                    if (ready) WreckChargeStarted(sim, e, tick);
                    return true;
                case SimEventType.WreckChargeReleased:
                    if (ready) WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.ChargeRelease, Tick = tick, Amount = e.Amount, Flag = e.Flag, Serial = e.ActionVariant
                    });
                    return true;
                case SimEventType.WreckBreakwaterCatch:
                    if (!ready) return true;
                    WreckCue(WreckVfxCue.Catch, tick, e.Target);
                    WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.Catch, Tick = tick, Target = e.Target, Amount = e.Amount, Flag = e.Flag,
                        At = WreckWorld(e.Position, PlayerPosition().y), Form = PelagForm.WreckBreakwater
                    });
                    return true;
                case SimEventType.WreckBreakwaterCrash:
                    if (!ready) return true;
                    WreckCue(WreckVfxCue.Crash, tick, -1);
                    WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.Crash, Tick = tick, Amount = e.Amount, Flag = e.Flag, Extra = e.ActionVariant,
                        At = WreckWorld(e.Position, PlayerPosition().y), Form = PelagForm.WreckBreakwater, Wave = _wkWave
                    });
                    return true;
                case SimEventType.WreckShellBurst:
                    if (!ready) return true;
                    WreckCue(WreckVfxCue.Burst, tick, -1);
                    WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.Burst, Tick = tick, Extra = e.ActionVariant, At = WreckWorld(e.Position, PlayerPosition().y),
                        Form = PelagForm.WreckShell, Serial = sim != null ? sim.Wreck.Serial : -1
                    });
                    return true;
                case SimEventType.WreckEnded:
                    if (ready) WreckEnqueue(new WkPending
                    {
                        Kind = WkKind.End, Tick = tick, Amount = e.Amount, Serial = e.ActionVariant,
                        At = WreckWorld(e.Position, PlayerPosition().y)
                    });
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Каст (этап 0) или следующее нажатие: сроки этапа и форма — из снимка Sim в миг события.</summary>
        private void WreckStageStarted(Simulation sim, in SimEvent e, int tick)
        {
            WreckState w = sim.Wreck;
            int stage = e.Type == SimEventType.AbilityCast ? 0 : e.ActionVariant;
            bool ours = w.Slot == e.Amount;
            WreckEnqueue(new WkPending
            {
                Kind = WkKind.Stage, Tick = tick, Slot = e.Amount, Stage = stage, Serial = ours ? w.Serial : -1,
                Contact = ours && w.Stage == stage ? w.ContactTick : tick + Simulation.WreckSwingWindupTicks,
                Overhead = ours && w.Stage == stage ? w.OverheadTick : -1,
                Form = sim.FormAt(e.Amount), Flag = ours && w.Shell
            });
        }

        /// <summary>Удар оземь: полоса, фронт и круг — снимком; «кто бит» в этом же тике решает геометрия круга.</summary>
        private void WreckSlammed(Simulation sim, in SimEvent e, int tick)
        {
            WreckState w = sim.Wreck;
            float y = PlayerPosition().y;
            var wave = new WkWave
            {
                Origin = WreckWorld(w.LaneOrigin, y), Dir = new Vector3(w.LaneDir.X.ToFloat(), 0f, w.LaneDir.Y.ToFloat()),
                Impact = WreckWorld(e.Position, y), ImpactFix = w.ImpactPoint, Start = w.WaveStart.ToFloat(), Step = w.WaveStep.ToFloat(),
                End = w.WallEnd.ToFloat(), HalfWidth = w.LaneHalfWidth.ToFloat(), ImpactRadius = w.ImpactRadius.ToFloat(),
                Tick = tick, Travel = e.Amount, DamagePercent = w.DamagePercent, Stopped = w.WallStopped,
                Form = (PelagForm)e.ActionVariant == PelagForm.WreckBreakwater ? PelagForm.WreckBreakwater
                    : (PelagForm)e.ActionVariant == PelagForm.WreckNinthWave ? PelagForm.WreckNinthWave : PelagForm.None,
                Look = (PelagForm)e.ActionVariant
            };
            _wkWave = wave;
            _wkHasWave = true;
            WreckCue(WreckVfxCue.Slam, tick, -1);
            WreckEnqueue(new WkPending
            {
                Kind = WkKind.Slam, Tick = tick, Amount = e.Amount, Flag = e.Flag, Serial = w.Serial,
                At = wave.Impact, Form = wave.Form, Wave = wave
            });
        }

        private void WreckChargeStarted(Simulation sim, in SimEvent e, int tick)
        {
            WreckState w = sim.Wreck;
            AbilityBuild build = IsWreckSlot(sim, w.Slot) ? sim.GetAbility(w.Slot) : null;
            float baseHalf = build != null ? build.Get(AbilityStatType.Width).ToFloat() * .5f : .75f;
            WreckEnqueue(new WkPending
            {
                Kind = WkKind.ChargeStart, Tick = tick, Serial = e.ActionVariant, Amount = e.Amount, BaseHalf = baseHalf,
                Form = PelagForm.WreckNinthWave, At = WreckWorld(w.ImpactPoint, PlayerPosition().y)
            });
        }

        /// <summary>
        /// Damage слота Крушения (ConsumeSimEvents): чей удар — по правилу ClassifyHit; знак — в
        /// очередь до тика показа. False — новый путь не готов (прежний PlaySquallImpact).
        /// </summary>
        private bool TakeWreckDamage(in SimEvent e, int tick)
        {
            if (!WreckVfxReady) return false;
            Simulation sim = _driver.Sim;
            Vector3 at = WreckWorld(e.Position, PlayerPosition().y);
            bool inCircle = _wkHasWave && _wkWave.Tick == tick && WreckInSlamCircle(sim, e.Target, at);
            bool wall = _wkHasWave && _wkWave.Form == PelagForm.WreckBreakwater;
            WreckVfxHit hit = PelagWreckVfxRules.ClassifyHit(_wkLastCue, _wkLastCueTick, _wkCueTarget, tick, e.Target, inCircle,
                _wkHasWave ? _wkWave.Tick : -1, _wkHasWave ? _wkWave.Travel : 0, wall);
            // Махи v4 бьют по ходу головы (контакт − 3 … + 4): удар вне тика контакта — тоже мах своего этапа (.WreckSwingHit).
            int sweepStage = WreckSweepHitStage(sim, tick, e.Target, ref hit);
            WreckEnqueue(new WkPending
            {
                Kind = WkKind.Hit, Hit = hit, Stage = sweepStage, Tick = tick, Target = e.Target, At = EntityPosition(e.Target, at),
                Form = hit == WreckVfxHit.Burst ? PelagForm.WreckShell : hit == WreckVfxHit.Wall || hit == WreckVfxHit.Crash
                    ? PelagForm.WreckBreakwater : hit == WreckVfxHit.Swing || hit == WreckVfxHit.Fourth || !_wkHasWave ? PelagForm.None : _wkWave.Form,
                Wave = _wkWave
            });
            return true;
        }

        /// <summary>
        /// Задет ли круг удара оземь — то же правило, что у Sim: у прочих круг BodyRadius, у босса — корпус
        /// (ThicketBodyFrom: зазор от точки удара до ближнего круга корпуса), иначе удар кругом по боссу
        /// рисовался бы меньшим всплеском вала.
        /// </summary>
        private bool WreckInSlamCircle(Simulation sim, int target, Vector3 at)
        {
            if (sim != null && (uint)target < (uint)sim.Entities.Count && sim.ThicketHullActive(target))
                return PelagWreckVfxRules.InSlamCircleHull(sim.ThicketHullGap(target, _wkWave.ImpactFix).ToFloat(), _wkWave.ImpactRadius);
            float body = sim != null && (uint)target < (uint)sim.Entities.Count ? sim.Entities.BodyRadius[target].ToFloat() : .85f;
            return PelagWreckVfxRules.InSlamCircle(Vector2.Distance(new Vector2(at.x, at.z), new Vector2(_wkWave.Impact.x, _wkWave.Impact.z)),
                _wkWave.ImpactRadius, body);
        }
    }
}
