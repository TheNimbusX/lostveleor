using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// События и снимок Sim → лента Шквала (PelagSquallTimeline). Без Unity: этот же
    /// код ведёт CharacterAnimatorView.Squall и проверяется тестами представления на
    /// живой симуляции (SquallClipRulesTests). Тик события — тик шага, на котором оно
    /// родилось (FrameEventContext.SimulationTick − 1); now — тик показа.
    ///
    /// Что после удара, решает снимок Sim, прочитанный вместе с событием удара: он не
    /// старше удара, а фазы Шквала идут только вперёд (опора → полёт/возврат/удержание,
    /// удержание → выход → конец), поэтому даже снимок через несколько шагов отвечает верно.
    /// </summary>
    public sealed class PelagSquallFeed
    {
        public readonly PelagSquallTimeline Timeline = new PelagSquallTimeline();
        private readonly float[] _legYaw = new float[3];
        private readonly int[] _legTicks = new int[3];
        private FixVec2 _lastStrike;

        /// <summary>Каст по снимку Sim. false — Шквала в Sim нет (или он уже кончился).</summary>
        public bool Begin(Simulation sim)
        {
            SquallState s = sim.Squall;
            if (s.Serial == 0 || s.Phase == SquallPhase.None) return false;
            FixVec2 aim = s.Phase == SquallPhase.Windup && !s.To.Equals(s.From)
                ? s.To - s.From : sim.Entities.Facing[Simulation.PlayerId];
            Timeline.Begin(s.Serial, s.CastTick, s.Origin.X.ToFloat(), s.Origin.Y.ToFloat(),
                PelagSquallClipRules.YawOf(aim.X.ToFloat(), aim.Y.ToFloat()));
            _lastStrike = s.Origin;
            return true;
        }

        /// <summary>Одно событие героя. Возвращает строку для журнала съёмки или null.</summary>
        public string Apply(Simulation sim, in SimEvent e, int tick, float now)
        {
            if (!Timeline.Active || e.Source != Simulation.PlayerId || tick < Timeline.CastTick) return null;
            switch (e.Type)
            {
                case SimEventType.SquallJump:
                    Timeline.Jump(tick, e.ActionVariant, e.Amount, e.Flag, e.Position.X.ToFloat(), e.Position.Y.ToFloat(),
                        FacingYaw(sim), now);
                    return $"jump #{e.ActionVariant} tick={tick} flight={e.Amount} back={e.Flag} shown={now:F2}";
                case SimEventType.SquallStrike:
                {
                    _lastStrike = e.Position;
                    SquallState s = sim.Squall;
                    PelagSquallNext next = NextAfterStrike(in s, Timeline.Serial, tick);
                    float predicted = next == PelagSquallNext.Return
                        ? PelagSquallClipRules.PredictReturnYaw(e.Position.X.ToFloat(), e.Position.Y.ToFloat(),
                            s.Origin.X.ToFloat(), s.Origin.Y.ToFloat(), (e.ActionVariant & 1) == 1)
                        : FacingYaw(sim);
                    Timeline.Strike(tick, e.ActionVariant, e.Position.X.ToFloat(), e.Position.Y.ToFloat(), next, predicted, now);
                    return $"strike #{e.ActionVariant} tick={tick} next={next} landed={e.Flag} shown={now:F2}";
                }
                case SimEventType.SquallReturn:
                {
                    SquallState s = sim.Squall;
                    int legs;
                    if (s.Serial == Timeline.Serial
                        && (s.Phase == SquallPhase.Return || s.Phase == SquallPhase.Exit || s.Phase == SquallPhase.None))
                        legs = PelagSquallTimeline.ReturnLegs(s.From, in s, _legYaw, _legTicks);
                    else
                    {
                        FixVec2 d = e.Position - _lastStrike;
                        _legYaw[0] = PelagSquallClipRules.YawOf(d.X.ToFloat(), d.Y.ToFloat());
                        _legTicks[0] = e.Amount;
                        legs = 1;
                    }
                    Timeline.Return(tick, e.Amount, legs, _legYaw, _legTicks, now);
                    return $"return tick={tick} ticks={e.Amount} legs={legs} shown={now:F2}";
                }
                case SimEventType.SquallEnded:
                    if (e.ActionVariant != Timeline.Serial) return null;
                    Timeline.End(tick, (SquallEnd)e.Amount);
                    return $"ended {(SquallEnd)e.Amount} tick={tick} shown={now:F2}";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Следующий прыжок сорвался после опоры (цели не стало, стена): Sim ушла в
        /// удержание и выход, а лента ждёт прыжка — финиш догоняет с кадра 2.
        /// </summary>
        public string DetectLateFinish(Simulation sim, float now)
        {
            if (!Timeline.AwaitingContinuation(out int strikeTick)) return null;
            if (now < strikeTick + PelagSquallClipRules.SupportTicks - .01f) return null;
            SquallState s = sim.Squall;
            bool over = s.Serial != Timeline.Serial || s.Phase == SquallPhase.Hold
                        || s.Phase == SquallPhase.Exit || s.Phase == SquallPhase.None;
            if (!over) return null;
            Timeline.LateFinish(System.Math.Max(now, strikeTick + PelagSquallClipRules.SupportTicks));
            return $"late finish strike={strikeTick} shown={now:F2} phase={s.Phase}";
        }

        public static PelagSquallNext NextAfterStrike(in SquallState s, int serial, int strikeTick)
        {
            if (s.Serial != serial) return PelagSquallNext.Unknown;
            switch (s.Phase)
            {
                case SquallPhase.Stop:
                    return s.NextTarget >= 0 ? PelagSquallNext.Jump
                        : s.ArriveTick == strikeTick ? PelagSquallNext.Return : PelagSquallNext.Unknown;
                case SquallPhase.Flight: return PelagSquallNext.Jump;
                case SquallPhase.Return: return PelagSquallNext.Return;
                case SquallPhase.Hold:
                case SquallPhase.Exit:
                    return s.ArriveTick == strikeTick ? PelagSquallNext.Finish : PelagSquallNext.Unknown;
                default: return PelagSquallNext.Unknown;
            }
        }

        public static float FacingYaw(Simulation sim)
        {
            FixVec2 f = sim.Entities.Facing[Simulation.PlayerId];
            return PelagSquallClipRules.YawOf(f.X.ToFloat(), f.Y.ToFloat());
        }
    }
}
