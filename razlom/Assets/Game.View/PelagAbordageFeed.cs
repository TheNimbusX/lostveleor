using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Снимок и события Sim → лента Абордажа (PelagAbordageTimeline). Без Unity: этот же код
    /// ведёт CharacterAnimatorView.Abordage и проверяется тестами представления на живой
    /// симуляции (AbordageTimelineSimTests). Тик события — тик шага, на котором оно родилось
    /// (FrameEventContext.SimulationTick − 1); now — тик показа.
    ///
    /// Перевыбор цели в полёте якоря и возврат якоря без цели событий не дают — их видно
    /// только в снимке (Track): фазы Абордажа идут вперёд, поэтому снимок даже через
    /// несколько шагов отвечает верно.
    /// </summary>
    public sealed class PelagAbordageFeed
    {
        public readonly PelagAbordageTimeline Timeline = new PelagAbordageTimeline();

        /// <summary>Каст по снимку Sim. false — Абордажа в Sim нет (или он уже кончился).</summary>
        public bool Begin(Simulation sim)
        {
            AbordageState s = sim.Abordage;
            if (s.Serial == 0 || s.Phase == AbordagePhase.None) return false;
            Timeline.Begin(s.Serial, s.CastTick, s.ReleaseTick, s.BiteTick, s.ArriveTick,
                YawToward(sim, s.Target, sim.Entities.Position[Simulation.PlayerId]), SlotForm(sim, s.Slot));
            return true;
        }

        /// <summary>Одно событие героя. Возвращает строку для журнала съёмки или null.</summary>
        public string Apply(Simulation sim, in SimEvent e, int tick, float now)
        {
            if (!Timeline.Active || e.Source != Simulation.PlayerId || tick < Timeline.CastTick) return null;
            switch (e.Type)
            {
                case SimEventType.AbordageThrow:
                    if (e.ActionVariant != Timeline.Serial) return null;
                    Timeline.Throw(tick, e.Amount, YawToward(sim, e.Target, e.Position), now);
                    return $"throw tick={tick} hook={e.Amount} shown={now:F2}";
                case SimEventType.AbordageHook:
                {
                    if (e.ActionVariant != Timeline.Serial) return null;
                    AbordageState s = sim.Abordage;
                    FixVec2 from = s.Serial == Timeline.Serial && s.Phase != AbordagePhase.Windup && s.Phase != AbordagePhase.Hook
                        ? s.From : sim.Entities.Position[Simulation.PlayerId];
                    FixVec2 path = e.Position - from;
                    float yaw = path.LengthSq.Raw > 0 ? Yaw(path) : YawToward(sim, e.Target, from);
                    Timeline.Hook(tick, e.Amount, yaw, now);
                    return $"hook tick={tick} pull={e.Amount} hull={e.Flag} shown={now:F2}";
                }
                case SimEventType.AbordagePunch:
                    // ActionVariant удара — форма, не номер каста: свой удар узнаём по тику.
                    if (Timeline.Struck || tick <= Timeline.CastTick) return null;
                    Timeline.Punch(tick, e.Flag, (PelagForm)e.ActionVariant, PunchYaw(sim, e.Target), now);
                    return $"punch tick={tick} landed={e.Flag} form={(PelagForm)e.ActionVariant} shown={now:F2}";
                case SimEventType.AbordageEnded:
                {
                    if (e.ActionVariant != Timeline.Serial) return null;
                    var reason = (AbordageEnd)e.Amount;
                    if (reason == AbordageEnd.NoTarget) Timeline.Recall(tick - Simulation.AbordageRecallTicks, now);
                    Timeline.End(tick, reason);
                    return $"ended {reason} tick={tick} shown={now:F2}";
                }
                default:
                    return null;
            }
        }

        /// <summary>Снимок без событий: зацеп сдвинулся (перевыбор цели) или якорь возвращается без цели.</summary>
        public string Track(Simulation sim, float now)
        {
            AbordageState s = sim.Abordage;
            if (!Timeline.Active || s.Serial != Timeline.Serial) return null;
            // v3: клип прибытия (Uppercut/Slam) идёт с тика до удара — форму берём со слота, удар её подтвердит.
            Timeline.ExpectForm(SlotForm(sim, s.Slot));
            if (s.Phase == AbordagePhase.Recall && !Timeline.Recalling && !Timeline.Hooked)
            {
                Timeline.Recall(s.PhaseEndTick - Simulation.AbordageRecallTicks, now);
                return $"recall from={s.PhaseEndTick - Simulation.AbordageRecallTicks} shown={now:F2}";
            }
            if (s.Phase == AbordagePhase.Hook && Timeline.Released && !Timeline.Hooked && s.BiteTick != Timeline.BiteTick)
            {
                Timeline.Retime(s.BiteTick, now);
                return $"retime bite={s.BiteTick} shown={now:F2}";
            }
            return null;
        }

        /// <summary>
        /// Взгляд после удара — как Simulation.AbordagePullDirection: от места зацепа к посадке,
        /// без тяги — к цели, иначе взгляд Sim.
        /// </summary>
        private float PunchYaw(Simulation sim, int target)
        {
            AbordageState s = sim.Abordage;
            if (s.Serial == Timeline.Serial)
            {
                FixVec2 path = s.To - s.From;
                if (path.LengthSq.Raw > 0) return Yaw(path);
            }
            return YawToward(sim, target, sim.Entities.Position[Simulation.PlayerId]);
        }

        private static float YawToward(Simulation sim, int target, FixVec2 from)
        {
            if ((uint)target < (uint)sim.Entities.Count)
            {
                FixVec2 d = sim.Entities.Position[target] - from;
                if (d.LengthSq.Raw > 0) return Yaw(d);
            }
            return PelagSquallFeed.FacingYaw(sim);
        }

        /// <summary>Форма способности в слоте Абордажа (как PelagVfxController.AbordageAnchor); нет слота — None.</summary>
        private static PelagForm SlotForm(Simulation sim, int slot)
            => (uint)slot < Simulation.AbilitySlots ? sim.FormAt(slot) : PelagForm.None;

        private static float Yaw(FixVec2 v) => PelagSquallClipRules.YawOf(v.X.ToFloat(), v.Y.ToFloat());
    }
}
