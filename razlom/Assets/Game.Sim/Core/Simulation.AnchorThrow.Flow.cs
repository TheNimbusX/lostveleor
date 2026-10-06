namespace Game.Sim
{
    /// <summary>Ход Броска якоря: каст, выпуск, полёт, натяг, возврат, ловля, выход, конец (Simulation.AnchorThrow).</summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Каст к точке пола под курсором (быстрый каст, цели нет). Полосы, дальности
        /// (стены уже известны) и тайминг — прогноз; полёт короче прогноза (босс, Гарпун)
        /// уточняет часы в натяг.
        /// </summary>
        private void BeginAnchorThrow(int slot, FixVec2 aim)
        {
            EnsureAnchorThrowBuffers();
            AbilityBuild build = _abilityBuilds[slot];
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 facing = Entities.Facing[PlayerId];
            FixVec2 dir = AnchorThrowDirection(hero, aim, facing);
            int baseWindup = build.Get(AbilityStatType.WindupTicks).ToInt();
            if (baseWindup <= 0) baseWindup = AbordageWindupTicks;
            // Взгляд до каста (AnchorThrowFace ниже его перепишет): курсор за спиной — замах на тик дольше.
            int windup = AbilityExecutionTicks(baseWindup) + (AbordageTargetBehind(facing, dir) ? AbordageTurnWindupTicks : 0);

            int serial = _anchorThrow.Serial + 1;
            _anchorThrow = AnchorThrowPlan(build, slot, hero, dir);
            _anchorThrow.Serial = serial;
            _anchorThrow.Phase = AnchorThrowPhase.Windup;
            _anchorThrow.CastTick = Tick;
            _anchorThrow.WindupTicks = windup;
            _anchorThrow.ReleaseTick = Tick + windup;
            AnchorThrowSchedule();
            _anchorThrow.PhaseEndTick = _anchorThrow.ReleaseTick;
            System.Array.Clear(_throwHit, 0, _throwHit.Length);
            System.Array.Clear(_throwReeled, 0, _throwReeled.Length);
            AnchorThrowFace();
        }

        /// <summary>Направление броска: к точке прицела; прицел в герое — взгляд; нет и его — +X.</summary>
        private static FixVec2 AnchorThrowDirection(FixVec2 hero, FixVec2 aim, FixVec2 facing)
        {
            FixVec2 toward = aim - hero;
            if (toward.LengthSq > Fix64.Ratio(1, 10000)) return toward.Normalized();
            if (facing.LengthSq.Raw != 0) return facing.Normalized();
            return new FixVec2(Fix64.One, Fix64.Zero);
        }

        /// <summary>Натяг, возврат и ловля по самой длинной полосе: T = выпуск + max F + 1, R по max дальности.</summary>
        private void AnchorThrowSchedule()
        {
            int flight = _anchorThrow.Flight0;
            Fix64 reach = _anchorThrow.Reach0;
            for (int lane = 1; lane < _anchorThrow.Lanes; lane++)
            {
                if (_anchorThrow.LaneFlight(lane) > flight) flight = _anchorThrow.LaneFlight(lane);
                if (_anchorThrow.LaneReach(lane) > reach) reach = _anchorThrow.LaneReach(lane);
            }
            _anchorThrow.FlightTicks = flight;
            _anchorThrow.TautTick = _anchorThrow.ReleaseTick + flight + 1;
            _anchorThrow.ReturnTicks = AnchorThrowReturnTicks(reach);
            _anchorThrow.CatchTick = _anchorThrow.TautTick + _anchorThrow.ReturnTicks;
        }

        /// <summary>Часы действия в каст: контакт — прогноз ловли, конец — ловля + удержание + выход.</summary>
        private void AnchorThrowClockAtCast(out int contact, out int end)
        {
            contact = _anchorThrow.CatchTick;
            end = contact + AnchorThrowHoldTicks + AnchorThrowExitTicks;
        }

        private bool AnchorThrowClockIsOurs => _playerAction.DefinitionId == AbilityDefinition.AnchorThrowId
            && _playerAction.StartTick == _anchorThrow.CastTick && !_playerAction.Interrupted;

        /// <summary>Контакт и конец на часах — только если часы ещё этого каста (как AbordageSetClock).</summary>
        private void AnchorThrowSetClock(int contact, int end)
        {
            if (!AnchorThrowClockIsOurs) return;
            _playerAction.ContactTick = contact;
            _playerAction.EndTick = System.Math.Max(contact + 1, end);
            ExtendPelagBasicContinuation(_playerAction.EndTick);
            ExtendSabreChain(_playerAction.EndTick);
        }

        /// <summary>Каждый тик сразу после Абордажа: фаза Броска.</summary>
        private void UpdateAnchorThrow()
        {
            if (_anchorThrow.Phase == AnchorThrowPhase.None) return;
            if (!Entities.Alive[PlayerId]) { EndAnchorThrow(AnchorThrowEnd.Interrupted); return; }
            AbilityBuild build = AnchorThrowBuild;
            if (build == null) { EndAnchorThrow(AnchorThrowEnd.Interrupted); return; }
            // Чужой отброс или волок сдвинул стоящего героя — срыв (своего движения у Броска нет).
            if (ForcedMotion.IsActive(Entities, PlayerId)) { EndAnchorThrow(AnchorThrowEnd.Interrupted); return; }

            switch (_anchorThrow.Phase)
            {
                case AnchorThrowPhase.Windup:
                    AnchorThrowFace();
                    if (Tick >= _anchorThrow.PhaseEndTick) ReleaseAnchorThrow();
                    break;
                case AnchorThrowPhase.Flight:
                    AnchorThrowFace();
                    if (Tick >= _anchorThrow.TautTick) AnchorThrowTaut(build);
                    else SweepAnchorThrowFlight(build, Tick - _anchorThrow.ReleaseTick);
                    break;
                case AnchorThrowPhase.Taut:
                case AnchorThrowPhase.Return:
                    AnchorThrowFace();
                    _anchorThrow.Phase = AnchorThrowPhase.Return;
                    if (Tick >= _anchorThrow.CatchTick) AnchorThrowCatch(build);
                    else AnchorThrowKeepTow();
                    break;
                case AnchorThrowPhase.Catch:
                    if (Tick >= _anchorThrow.PhaseEndTick) BeginAnchorThrowExit();
                    break;
                case AnchorThrowPhase.Exit:
                    // Ходьба этого тика уже прошла (MovePlayer раньше): пошёл — выход сорван.
                    if (Tick >= _anchorThrow.ExitWalkTick && Entities.Velocity[PlayerId].LengthSq.Raw != 0)
                        EndAnchorThrow(AnchorThrowEnd.WalkedOut);
                    else if (Tick >= _anchorThrow.PhaseEndTick) EndAnchorThrow(AnchorThrowEnd.Done);
                    break;
            }
        }

        /// <summary>Выпуск правой рукой в тик каста + замах: голова в руке, полёт со следующего тика.</summary>
        private void ReleaseAnchorThrow()
        {
            _anchorThrow.Phase = AnchorThrowPhase.Flight;
            _anchorThrow.ReleaseTick = Tick;
            AnchorThrowSchedule();
            _anchorThrow.PhaseEndTick = _anchorThrow.TautTick;
            _events.Add(new SimEvent(SimEventType.AnchorThrowRelease, PlayerId, -1, _anchorThrow.Flight0, _anchorThrow.Lanes == 3,
                _anchorThrow.Origin, DamageType.Physical, DamageOrigin.Ability, _anchorThrow.Serial));
            AnchorThrowSetClock(_anchorThrow.CatchTick, _anchorThrow.CatchTick + AnchorThrowHoldTicks + AnchorThrowExitTicks);
        }

        /// <summary>
        /// Натяг — тик после полёта самой длинной полосы: голова стоит, цепь прямая.
        /// Сеть Невода ловит свою полосу; задетые «на тягу» живые едут на полукольцо
        /// (Reeled, R тиков, со следующего тика). Часы — по фактической ловле.
        /// </summary>
        private void AnchorThrowTaut(AbilityBuild build)
        {
            _anchorThrow.Phase = AnchorThrowPhase.Taut;
            _anchorThrow.TautTick = Tick;
            Fix64 reach = _anchorThrow.Reach0;
            for (int lane = 1; lane < _anchorThrow.Lanes; lane++)
                if (_anchorThrow.LaneReach(lane) > reach) reach = _anchorThrow.LaneReach(lane);
            _anchorThrow.ReturnTicks = AnchorThrowReturnTicks(reach);
            _anchorThrow.CatchTick = Tick + _anchorThrow.ReturnTicks;
            _anchorThrow.PhaseEndTick = _anchorThrow.CatchTick;
            int serial = _anchorThrow.Serial;
            _events.Add(new SimEvent(SimEventType.AnchorThrowYank, PlayerId, _anchorThrow.HarpoonTarget, _anchorThrow.ReturnTicks,
                _anchorThrow.StopKind0 != AnchorThrowStop.Full, _anchorThrow.Center + _anchorThrow.Dir * _anchorThrow.Reach0,
                DamageType.Physical, DamageOrigin.Ability, serial));

            if (_anchorThrow.NetHalfWidth.Raw > 0)
            {
                AnchorThrowNet(build);
                if (_anchorThrow.Phase == AnchorThrowPhase.None || _anchorThrow.Serial != serial) return;   // герой умер от отражения
            }
            AnchorThrowStartTow();
            AnchorThrowSetClock(_anchorThrow.CatchTick, _anchorThrow.CatchTick + AnchorThrowHoldTicks + AnchorThrowExitTicks);
        }

        /// <summary>
        /// Ловля: голова в руке, тела на местах. Доехавшие в своей тяге получают
        /// оглушение приземления (цель Гарпуна уже оглушена дольше). Событие — до Stun.
        /// </summary>
        private void AnchorThrowCatch(AbilityBuild build)
        {
            int arrived = 0;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
                if (AnchorThrowArrived(i)) arrived++;
            _anchorThrow.Phase = AnchorThrowPhase.Catch;
            _anchorThrow.CatchTick = Tick;
            _anchorThrow.PhaseEndTick = Tick + AnchorThrowHoldTicks;
            _anchorThrow.ExitWalkTick = 0;
            _events.Add(new SimEvent(SimEventType.AnchorThrowCatch, PlayerId, -1, arrived, false, _anchorThrow.Origin,
                DamageType.Physical, DamageOrigin.Ability, _anchorThrow.Serial));
            int stun = build.Get(AbilityStatType.StunTicks).ToInt();
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (!AnchorThrowArrived(i)) continue;
                if (stun > 0 && i != _anchorThrow.HarpoonTarget) StunByTalent(i, stun);
            }
            System.Array.Clear(_throwReeled, 0, _throwReeled.Length);
            AnchorThrowSetClock(Tick, Tick + AnchorThrowHoldTicks + AnchorThrowExitTicks);
        }

        private void BeginAnchorThrowExit()
        {
            _anchorThrow.Phase = AnchorThrowPhase.Exit;
            _anchorThrow.PhaseEndTick = Tick + AnchorThrowExitTicks;
            _anchorThrow.ExitWalkTick = Tick + AnchorThrowExitLockedTicks;
            AnchorThrowSetClock(_playerAction.ContactTick, _anchorThrow.PhaseEndTick);
        }

        /// <summary>Бросок кончился. Событие AnchorThrowEnded — один раз на каст. Срыв снимает свои тяги.</summary>
        private void EndAnchorThrow(AnchorThrowEnd reason)
        {
            if (_anchorThrow.Phase == AnchorThrowPhase.None) return;
            bool clockIsOurs = AnchorThrowClockIsOurs;
            _anchorThrow.Phase = AnchorThrowPhase.None;
            _anchorThrow.PhaseEndTick = Tick;
            if (_throwReeled != null)
            {
                for (int i = PlayerId + 1; i < Entities.Count; i++)
                {
                    if (!_throwReeled[i]) continue;
                    if (Entities.ForcedKind[i] == (byte)ForcedMotionKind.Reeled) ForcedMotion.Clear(Entities, i);
                    _throwReeled[i] = false;
                }
            }
            if (clockIsOurs && reason != AnchorThrowEnd.Interrupted) _playerAction.EndTick = Tick;
            _events.Add(new SimEvent(SimEventType.AnchorThrowEnded, PlayerId, -1, (int)reason, false, Entities.Position[PlayerId],
                DamageType.Physical, DamageOrigin.Ability, _anchorThrow.Serial));
        }

        /// <summary>
        /// Снятие чужим действием (CancelPlayerAction): до ловли — срыв; после ловли
        /// (удержание, выход) — конец Done: связка «Бросок → Вихрь» не показывает падение якоря.
        /// </summary>
        private void StopAnchorThrow()
        {
            AnchorThrowPhase phase = _anchorThrow.Phase;
            EndAnchorThrow(phase == AnchorThrowPhase.Catch || phase == AnchorThrowPhase.Exit
                ? AnchorThrowEnd.Done : AnchorThrowEnd.Interrupted);
        }
    }
}
