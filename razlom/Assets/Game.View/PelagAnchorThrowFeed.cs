using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Снимок и события Sim → лента Броска якоря (PelagAnchorThrowTimeline). Без Unity: этот же код
    /// ведёт CharacterAnimatorView.AnchorThrow и проверяется тестами представления на живой
    /// симуляции (AnchorThrowTimelineSimTests). Тик события — тик шага, на котором оно родилось
    /// (FrameEventContext.SimulationTick − 1); now — тик показа.
    ///
    /// Укорочение полёта (корпус босса, укус Гарпуна) событием не приходит — его видно в снимке
    /// (Track): Sim переписывает FlightTicks/TautTick/CatchTick в тик касания, за тик до того, как
    /// показ туда дойдёт.
    /// </summary>
    public sealed class PelagAnchorThrowFeed
    {
        public readonly PelagAnchorThrowTimeline Timeline = new PelagAnchorThrowTimeline();

        /// <summary>Каст по снимку Sim. false — Броска в Sim нет (или он уже кончился).</summary>
        public bool Begin(Simulation sim)
        {
            AnchorThrowState s = sim.AnchorThrow;
            if (s.Serial == 0 || s.Phase == AnchorThrowPhase.None) return false;
            Timeline.Begin(s.Serial, s.CastTick, s.ReleaseTick, s.FlightTicks, s.TautTick, s.ReturnTicks, s.CatchTick,
                DirYaw(s, sim));
            return true;
        }

        /// <summary>Одно событие героя. Возвращает строку для журнала съёмки или null.</summary>
        public string Apply(Simulation sim, in SimEvent e, int tick, float now)
        {
            if (!Timeline.Active || e.Source != Simulation.PlayerId || e.ActionVariant != Timeline.Serial) return null;
            switch (e.Type)
            {
                case SimEventType.AnchorThrowRelease:
                    Timeline.Release(tick, now);
                    return $"release tick={tick} flight={e.Amount} fan={e.Flag} shown={now:F2}";
                case SimEventType.AnchorThrowYank:
                    Timeline.Yank(tick, e.Amount, now);
                    return $"yank tick={tick} return={e.Amount} cut={e.Flag} target={e.Target} shown={now:F2}";
                case SimEventType.AnchorThrowCatch:
                    Timeline.Catch(tick);
                    return $"catch tick={tick} arrived={e.Amount} shown={now:F2}";
                case SimEventType.AnchorThrowEnded:
                {
                    var reason = (AnchorThrowEnd)e.Amount;
                    Timeline.End(tick, reason);
                    return $"ended {reason} tick={tick} shown={now:F2}";
                }
                default:
                    return null;
            }
        }

        /// <summary>Снимок без событий: полёт оборван раньше прогноза (босс, Гарпун) — перерасчёт натяга и ловли.</summary>
        public string Track(Simulation sim, float now)
        {
            AnchorThrowState s = sim.AnchorThrow;
            PelagAnchorThrowTimeline line = Timeline;
            if (!line.Active || line.Yanked || s.Serial != line.Serial) return null;
            if (s.Phase != AnchorThrowPhase.Windup && s.Phase != AnchorThrowPhase.Flight) return null;
            if (s.TautTick == line.TautTick && s.FlightTicks == line.FlightTicks && s.ReturnTicks == line.ReturnTicks
                && s.CatchTick == line.CatchTick && s.ReleaseTick == line.ReleaseTick) return null;
            line.Schedule(s.ReleaseTick, s.FlightTicks, s.TautTick, s.ReturnTicks, s.CatchTick, now);
            return $"retime release={s.ReleaseTick} flight={s.FlightTicks} taut={s.TautTick} catch={s.CatchTick} shown={now:F2}";
        }

        /// <summary>Взгляд броска — Dir снимка (Sim ставит его герою в каст и держит весь бросок).</summary>
        public static float DirYaw(in AnchorThrowState s, Simulation sim)
        {
            if (s.Dir.LengthSq.Raw > 0) return PelagSquallClipRules.YawOf(s.Dir.X.ToFloat(), s.Dir.Y.ToFloat());
            return PelagSquallFeed.FacingYaw(sim);
        }
    }
}
