using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Снимок WreckState и события Sim → лента Крушения v4 (PelagWreckTimeline). Без Unity: этот же код ведёт
    /// CharacterAnimatorView.Wreck2 и проверяется тестами представления на живой симуляции (WreckTimelineSimTests).
    /// Тик события — тик шага, на котором оно родилось (FrameEventContext.SimulationTick − 1).
    ///
    /// Почти всё берётся из снимка (этапы, контакт, «над головой», заряд, выход): фазы серии идут вперёд, и снимок
    /// даже через несколько шагов отвечает верно. Из событий — только конец серии (WreckEnded: причина и тик)
    /// и начало заряда (WreckChargeStarted), если кадр пропустил короткий заряд.
    /// </summary>
    public sealed class PelagWreckFeed
    {
        public readonly PelagWreckTimeline Timeline = new PelagWreckTimeline();

        /// <summary>Каст по снимку. false — серии в Sim нет (или уже кончилась). pinned — герой стоял на месте.</summary>
        public bool Begin(Simulation sim, bool pinned)
        {
            WreckState s = sim.Wreck;
            if (s.Serial == 0 || s.Phase == WreckPhase.None) return false;
            Timeline.Begin(s.Serial, s.CastTick, pinned);
            Track(sim);
            return true;
        }

        /// <summary>Снимок этого кадра. Возвращает строку для журнала съёмки или null.</summary>
        public string Track(Simulation sim)
        {
            WreckState s = sim.Wreck;
            if (!Timeline.Active || s.Serial != Timeline.Serial) return null;
            int tick = sim.Tick - 1;
            if (s.Phase == WreckPhase.None)
            {
                if (Timeline.Ended) return null;
                // События не было (не должно случаться): причина по срокам снимка.
                WreckEnd reason = InferEnd(s, tick);
                Timeline.End(tick, reason);
                return $"ended (snapshot) {reason} tick={tick}";
            }
            string trace = null;
            int before = Timeline.StageCount;
            Timeline.Track(s.Stage, s.StageStartTick, s.ContactTick, s.OverheadTick, Yaw(s.Direction), LastStage(sim, s));
            if (Timeline.StageCount != before)
                trace = $"stage {s.Stage} start={s.StageStartTick} contact={s.ContactTick} overhead={s.OverheadTick} side={s.Side} shell={s.Shell}";
            if (s.Stage == 2 && s.ChargeStartTick >= 0)
            {
                Timeline.Charge(s.StageStartTick, s.ChargeStartTick);
                if (tick <= s.ContactTick) Timeline.ChargeYaw(tick, Yaw(s.Direction));
                if (s.Phase != WreckPhase.Charge) Timeline.Release(s.StageStartTick, s.ContactTick);
            }
            if (s.Stage >= 2 && (s.Phase == WreckPhase.Hold || s.Phase == WreckPhase.Exit || s.Phase == WreckPhase.Window))
                Timeline.Exit(s.StageStartTick, s.ExitWalkTick, s.ExitEndTick);
            return trace;
        }

        /// <summary>Одно событие героя. Возвращает строку для журнала съёмки или null.</summary>
        public string Apply(Simulation sim, in SimEvent e, int tick)
        {
            if (!Timeline.Active || e.Source != Simulation.PlayerId || e.ActionVariant != Timeline.Serial) return null;
            switch (e.Type)
            {
                case SimEventType.WreckChargeStarted:
                {
                    WreckState s = sim.Wreck;
                    if (s.Serial == Timeline.Serial) Timeline.Charge(s.StageStartTick, tick);
                    return $"charge tick={tick}";
                }
                case SimEventType.WreckChargeReleased:
                    return $"release tick={tick} charge={e.Amount} full={e.Flag}";
                case SimEventType.WreckEnded:
                {
                    var reason = (WreckEnd)e.Amount;
                    Timeline.End(tick, reason);
                    return $"ended {reason} tick={tick}";
                }
                default:
                    return null;
            }
        }

        /// <summary>После удара этого этапа серия кончается: выпад, с «Четвёртым ударом» — четвёртый.</summary>
        private static bool LastStage(Simulation sim, in WreckState s)
        {
            AbilityBuild build = (uint)s.Slot < (uint)Simulation.AbilitySlots ? sim.GetAbility(s.Slot) : null;
            bool fourth = build != null && build.Has(AbilityFlag.WreckFourthStrike);
            return s.Stage >= (fourth ? Simulation.WreckStages : Simulation.WreckStages - 1);
        }

        private static WreckEnd InferEnd(in WreckState s, int tick)
        {
            if (s.Stage >= 2 && s.ExitEndTick > 0 && tick >= s.ExitEndTick) return WreckEnd.Done;
            if (s.Stage >= 2 && s.ExitWalkTick > 0 && tick >= s.ExitWalkTick) return WreckEnd.WalkedOut;
            if (s.WindowEndTick >= 0 && tick > s.WindowEndTick) return WreckEnd.WindowExpired;
            return WreckEnd.Interrupted;
        }

        private static float Yaw(FixVec2 v) => PelagSquallClipRules.YawOf(v.X.ToFloat(), v.Y.ToFloat());
    }
}
