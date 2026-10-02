namespace Game.Sim
{
    /// <summary>Ход серии Шквала: каст, прыжки, удары, возврат, выход (Simulation.Squall).</summary>
    public sealed partial class Simulation
    {
        /// <summary>Каст. Первая цель — та, что выбрал игрок; прыжок стартует после замаха.</summary>
        private void BeginChainStep(int slot, int target)
        {
            if (target < 0) return;

            _chainSlot = slot;
            _chainRepeatHop = false;
            // Талант «Пять прыжков» добавляет один; буфер посещённых рассчитан на него и на Охоту.
            _chainHopsLeft = BuildHas(slot, AbilityFlag.SquallFiveHops, AbilityDefinition.ChainStepId)
                ? AnchorKit.ChainMaxHops + 1 : AnchorKit.ChainMaxHops;
            _chainTarget = target;
            _chainVisitedCount = 1;
            _chainVisited[0] = target;

            FixVec2 at = Entities.Position[PlayerId];
            FixVec2 to = SquallLandingSpot(target, at);
            _squall = new SquallState
            {
                Serial = _squall.Serial + 1, Slot = slot, Phase = SquallPhase.Windup, CastTick = Tick,
                Index = 0, Target = target, NextTarget = -1, From = at, To = to,
                FlightStartTick = Tick + SquallWindupTicks,
                ArriveTick = Tick + SquallWindupTicks + SquallFlightTicks(FixVec2.Distance(at, to)),
                PhaseEndTick = Tick + SquallWindupTicks, Origin = at,
            };
            SquallFace(to.Equals(at) ? Entities.Position[target] - at : to - at);
            if (FormIs(slot, PelagForm.SquallFoamTrail)) EnsureSquallFoamBuffers();
        }

        /// <summary>Часы действия в каст: контакт — первый удар, конец — оценка (уточняется по ходу серии).</summary>
        private void SquallClockAtCast(out int contact, out int end)
        {
            contact = _squall.ArriveTick;
            end = contact + (_chainHopsLeft - 1) * (SquallStopTicks + SquallTypicalFlightTicks)
                + SquallFinalHoldTicks + SquallExitTicks;
        }

        /// <summary>Конец действия Шквала на часах — только если часы ещё его.</summary>
        private void SquallSetClockEnd(int end)
        {
            if (_playerAction.DefinitionId != AbilityDefinition.ChainStepId || _playerAction.Interrupted
                || _playerAction.StartTick != _squall.CastTick) return;
            _playerAction.EndTick = System.Math.Max(_playerAction.ContactTick + 1, end);
            ExtendPelagBasicContinuation(_playerAction.EndTick);
            ExtendSabreChain(_playerAction.EndTick);
        }

        /// <summary>Оценка конца серии из опоры: осталось hops прыжков типичной длины.</summary>
        private int SquallEndEstimate(int hops)
            => _squall.PhaseEndTick + hops * SquallTypicalFlightTicks + (hops - 1) * SquallStopTicks
               + SquallFinalHoldTicks + SquallExitTicks;

        /// <summary>
        /// Каждый тик после способностей: след пены (форма) и шаг серии. Удар и
        /// выбор следующей цели — в тик прибытия; цели не считаются вперёд:
        /// к третьему прыжку заранее выбранные оказались бы трупами.
        /// </summary>
        private void ContinueChainStep()
        {
            UpdateSquallFoamTrail();
            if (_squall.Phase == SquallPhase.None) return;
            if (!Entities.Alive[PlayerId]) { EndSquall(SquallEnd.Interrupted); return; }

            AbilityBuild build = SquallBuild;
            if (build == null) { EndSquall(SquallEnd.Interrupted); return; }

            switch (_squall.Phase)
            {
                case SquallPhase.Windup:
                    if (Tick >= _squall.PhaseEndTick) StartSquallJump(build, _chainTarget, first: true);
                    break;
                case SquallPhase.Flight:
                    if (!ForcedMotion.IsActive(Entities, PlayerId)) SquallStrike(build);
                    break;
                case SquallPhase.Stop:
                    if (Tick < _squall.PhaseEndTick) break;
                    if (_chainHopsLeft > 0) StartSquallJump(build, _squall.NextTarget, first: false);
                    else StartSquallReturn();
                    break;
                case SquallPhase.Hold:
                    if (Tick >= _squall.PhaseEndTick) BeginSquallExit();
                    break;
                case SquallPhase.Return:
                    if (ForcedMotion.IsActive(Entities, PlayerId)) break;
                    if (_squall.Leg < _squall.ViaCount) { _squall.Leg++; BeginSquallReturnLeg(); }
                    else BeginSquallExit();
                    break;
                case SquallPhase.Exit:
                    // Ходьба этого тика уже прошла (MovePlayer раньше): пошёл — выход сорван.
                    if (Tick >= _squall.ExitWalkTick && Entities.Velocity[PlayerId].LengthSq.Raw != 0)
                        EndSquall(SquallEnd.WalkedOut);
                    else if (Tick >= _squall.PhaseEndTick) EndSquall(SquallEnd.Done);
                    break;
            }
        }

        /// <summary>Старт прыжка к цели. Цель умерла в опоре или замахе — выбирается заново.</summary>
        private void StartSquallJump(AbilityBuild build, int target, bool first)
        {
            // Чужой отброс в опоре сильнее серии.
            if (ForcedMotion.IsActive(Entities, PlayerId)) { EndSquall(SquallEnd.Interrupted); return; }

            FixVec2 from = Entities.Position[PlayerId];
            if (!SquallTargetValid(target))
            {
                target = PickSquallTarget(build, from, first ? -1 : _squall.Target);
                if (target < 0)
                {
                    if (first) EndSquall(SquallEnd.NoTarget);
                    else { _chainHopsLeft = 0; FinishSquallHops(build); }
                    return;
                }
            }

            bool repeat = !first && target == _squall.Target;
            if (!TrySquallSpot(target, from, repeat, out FixVec2 to))
            {
                _chainHopsLeft = 0;
                FinishSquallHops(build);
                return;
            }

            if (first) _chainVisited[0] = target;
            else if (_chainVisitedCount < _chainVisited.Length) _chainVisited[_chainVisitedCount++] = target;
            _chainRepeatHop = repeat;
            _chainTarget = target;

            int flight = SquallFlightTicks(FixVec2.Distance(from, to));
            _squall.Index = first ? 0 : _squall.Index + 1;
            _squall.Backhand = !first && !_squall.Backhand;
            _squall.Target = target;
            _squall.NextTarget = -1;
            _squall.From = from;
            _squall.To = to;
            _squall.FlightStartTick = Tick;
            _squall.ArriveTick = Tick + flight;
            _squall.Phase = SquallPhase.Flight;
            SquallFace(to - from);
            ForcedMotion.Begin(Entities, PlayerId, to, flight, ForcedMotionKind.Lunge);

            if (first)
            {
                if (_playerAction.DefinitionId == AbilityDefinition.ChainStepId && _playerAction.StartTick == _squall.CastTick)
                    _playerAction.ContactTick = _squall.ArriveTick;
            }
            else EmitChainHop();
            _events.Add(new SimEvent(SimEventType.SquallJump, PlayerId, target, flight, _squall.Backhand, to,
                DamageType.Physical, DamageOrigin.Ability, _squall.Index));
            SquallSetClockEnd(_squall.ArriveTick + (_chainHopsLeft - 1) * (SquallStopTicks + SquallTypicalFlightTicks)
                + SquallFinalHoldTicks + SquallExitTicks);
        }

        /// <summary>Прибыл: удар (если цель жива и рядом), след пены, Охота, выбор следующей цели.</summary>
        private void SquallStrike(AbilityBuild build)
        {
            int target = _chainTarget;
            _squall.ArriveTick = Tick;
            _squall.Phase = SquallPhase.Stop;
            _squall.PhaseEndTick = Tick + SquallStopTicks;

            if (FormIs(_squall.Slot, PelagForm.SquallFoamTrail))
                LaySquallFoamStrip(build, _squall.From, Entities.Position[PlayerId]);

            bool landed = SquallTargetValid(target) && SquallContactReachable(target);
            // Событие удара — до урона: Damage и Death идут сразу за ним.
            _events.Add(new SimEvent(SimEventType.SquallStrike, PlayerId, target, _chainHopsLeft - 1, landed,
                Entities.Position[PlayerId], DamageType.Physical, DamageOrigin.Ability, _squall.Index));

            bool killed = false;
            if (landed)
            {
                int damage = build.Get(AbilityStatType.Damage).ToInt();
                // «Добивающий прыжок»: последний прыжок серии бьёт вдвое.
                if (_chainHopsLeft == 1 && build.Has(AbilityFlag.SquallFinisher)) damage *= 2;
                damage = SquallHopDamage(build, target, damage);
                ApplyAbilityDamage(PlayerId, target, damage, _squall.Slot, DamageType.Physical);
                killed = !Entities.Alive[target];
            }

            _chainHopsLeft--;
            if (killed && FormIs(_squall.Slot, PelagForm.SquallHunt) && _squall.BonusHops < HuntBonusHopsMax)
            {
                _chainHopsLeft++;
                _squall.BonusHops++;
                _events.Add(new SimEvent(SimEventType.SquallHuntKill, PlayerId, target, _squall.BonusHops, false,
                    Entities.Position[target], DamageType.Physical, DamageOrigin.Ability, _squall.Index));
            }

            if (_chainHopsLeft <= 0) { FinishSquallHops(build); return; }

            FixVec2 at = Entities.Position[PlayerId];
            int next = PickSquallTarget(build, at, target);
            if (next < 0)
            {
                // Больше некого — оставшиеся прыжки не переносятся.
                _chainHopsLeft = 0;
                FinishSquallHops(build);
                return;
            }
            _squall.NextTarget = next;
            // Поворот к следующей цели — в тик удара, а не рывком в конце серии.
            if (TrySquallSpot(next, at, next == target, out FixVec2 spot) && !spot.Equals(at)) SquallFace(spot - at);
            else SquallFace(Entities.Position[next] - at);
            SquallSetClockEnd(SquallEndEstimate(_chainHopsLeft));
        }

        /// <summary>Прыжков больше нет: возврат (талант или Неуловимый) или удержание последнего удара.</summary>
        private void FinishSquallHops(AbilityBuild build)
        {
            EmitChainHop();
            _chainTarget = -1;
            _chainSlot = -1;
            _squall.NextTarget = -1;
            bool returns = (build.Has(AbilityFlag.SquallReturn) || FormIs(_squall.Slot, PelagForm.SquallElusive))
                && FixVec2.Distance(Entities.Position[PlayerId], _squall.Origin) >= SquallReturnMinDistance;
            if (returns)
            {
                // Опора удара, потом прыжок назад: удар доворачивает корпус на путь возврата.
                _squall.Phase = SquallPhase.Stop;
                _squall.PhaseEndTick = _squall.ArriveTick + SquallStopTicks;
                if (_squall.PhaseEndTick < Tick) _squall.PhaseEndTick = Tick;
                SquallSetClockEnd(_squall.PhaseEndTick + SquallLegTicks(Entities.Position[PlayerId], _squall.Origin) + SquallExitTicks);
            }
            else
            {
                _squall.Phase = SquallPhase.Hold;
                _squall.PhaseEndTick = System.Math.Max(Tick, _squall.ArriveTick + SquallFinalHoldTicks);
                SquallSetClockEnd(_squall.PhaseEndTick + SquallExitTicks);
            }
        }

        /// <summary>Прыжок назад к точке каста: дугой вбок, если путь свободен, иначе прямо. Лицом по пути.</summary>
        private void StartSquallReturn()
        {
            if (ForcedMotion.IsActive(Entities, PlayerId)) { EndSquall(SquallEnd.Interrupted); return; }
            FixVec2 from = Entities.Position[PlayerId];
            PlanSquallReturn(from, _squall.Origin, preferLeft: !_squall.Backhand);
            _squall.Phase = SquallPhase.Return;
            _squall.Leg = 0;
            _squall.From = from;
            _squall.To = _squall.Origin;
            _squall.FlightStartTick = Tick;
            int total = 0;
            FixVec2 a = from;
            for (int leg = 0; leg <= _squall.ViaCount; leg++)
            {
                FixVec2 b = SquallReturnPoint(leg);
                total += SquallLegTicks(a, b);
                a = b;
            }
            _squall.ArriveTick = Tick + total;
            BeginSquallReturnLeg();
            _events.Add(new SimEvent(SimEventType.SquallReturn, PlayerId, -1, total, false, _squall.Origin,
                DamageType.Physical, DamageOrigin.Ability, _squall.ViaCount));
            SquallSetClockEnd(_squall.ArriveTick + SquallExitTicks);
        }

        private FixVec2 SquallReturnPoint(int leg)
            => leg >= _squall.ViaCount ? _squall.Origin : leg == 0 ? _squall.Via0 : _squall.Via1;

        /// <summary>Отрезок возврата Leg: тики — по плановой длине, сумма не зависит от скольжения у стен.</summary>
        private void BeginSquallReturnLeg()
        {
            FixVec2 a = _squall.Leg == 0 ? _squall.From : SquallReturnPoint(_squall.Leg - 1);
            FixVec2 b = SquallReturnPoint(_squall.Leg);
            SquallFace(b - Entities.Position[PlayerId]);
            ForcedMotion.Begin(Entities, PlayerId, b, SquallLegTicks(a, b), ForcedMotionKind.Lunge);
        }

        /// <summary>
        /// Дуга возврата: две точки квадратичной кривой (t = 1/3, 2/3) с изгибом
        /// SquallReturnBend вбок. Сначала сторона удара, потом другая, потом прямо.
        /// </summary>
        private void PlanSquallReturn(FixVec2 from, FixVec2 origin, bool preferLeft)
        {
            _squall.ViaCount = 0;
            FixVec2 delta = origin - from;
            Fix64 length = delta.Length;
            if (length.Raw == 0) return;
            FixVec2 left = new FixVec2(-delta.Y / length, delta.X / length);
            Fix64 bend = Fix64.Clamp(length * SquallReturnBendShare, SquallReturnBendMin, SquallReturnBendMax)
                * Fix64.Ratio(8, 9);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool toLeft = attempt == 0 ? preferLeft : !preferLeft;
                FixVec2 side = toLeft ? left : -left;
                FixVec2 via0 = from + delta * Fix64.Ratio(1, 3) + side * bend;
                FixVec2 via1 = from + delta * Fix64.Ratio(2, 3) + side * bend;
                if (!SquallCanTravel(from, via0) || !SquallCanTravel(via0, via1) || !SquallCanTravel(via1, origin)) continue;
                _squall.ViaCount = 2;
                _squall.Via0 = via0;
                _squall.Via1 = via1;
                return;
            }
        }

        private void BeginSquallExit()
        {
            _squall.Phase = SquallPhase.Exit;
            _squall.PhaseEndTick = Tick + SquallExitTicks;
            _squall.ExitWalkTick = Tick + SquallExitLockedTicks;
            SquallSetClockEnd(_squall.PhaseEndTick);
        }

        /// <summary>Серия кончилась. Событие SquallEnded — один раз на каст.</summary>
        private void EndSquall(SquallEnd reason)
        {
            if (_squall.Phase == SquallPhase.None) return;
            bool clockIsOurs = _playerAction.DefinitionId == AbilityDefinition.ChainStepId
                && _playerAction.StartTick == _squall.CastTick && !_playerAction.Interrupted;
            _squall.Phase = SquallPhase.None;
            _squall.PhaseEndTick = Tick;
            _squall.NextTarget = -1;
            _chainHopsLeft = _chainVisitedCount = 0;
            _chainTarget = _chainSlot = -1;
            if (reason == SquallEnd.Interrupted && Entities.ForcedKind[PlayerId] == (byte)ForcedMotionKind.Lunge)
                ForcedMotion.Clear(Entities, PlayerId);
            if (clockIsOurs && reason != SquallEnd.Interrupted) _playerAction.EndTick = Tick;
            _events.Add(new SimEvent(SimEventType.SquallEnded, PlayerId, -1, (int)reason, false,
                Entities.Position[PlayerId], DamageType.Physical, DamageOrigin.Ability, _squall.Serial));
        }

        /// <summary>Снятие серии чужим действием (CancelPlayerAction): уход, способность, сабля, оглушение.</summary>
        private void StopSquall() => EndSquall(SquallEnd.Interrupted);

        private void EmitChainHop()
        {
            // Прежнее событие вида (ArenaView): старт прыжка, Amount 0 — конец прыжков.
            _events.Add(SimEvent.ChainHop(PlayerId, _chainTarget, _chainHopsLeft, _squall.Index, Entities.Position[PlayerId]));
        }

        /// <summary>Удар достаёт: цель не дальше посадки и запаса, и между телами нет стены.</summary>
        private bool SquallContactReachable(int target)
        {
            FixVec2 from = Entities.Position[PlayerId];
            FixVec2 delta = Entities.Position[target] - from;
            Fix64 reach = SquallLandingDistance(target) + SquallContactSlack;
            if (delta.LengthSq > reach * reach) return false;
            if (_campWalkMap != null) return _campWalkMap.CanTravel(from, Entities.Position[target]);
            if (_layout == null) return true;
            int steps = System.Math.Max(1, (delta.Length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
            for (int i = 1; i <= steps; i++)
                if (!_layout.IsWalkable(from + delta * Fix64.Ratio(i, steps), Fix64.Zero)) return false;
            return true;
        }

        private void ResetSquall()
        {
            _squall = default;
            _chainHopsLeft = _chainVisitedCount = 0;
            _chainTarget = _chainSlot = -1;
            System.Array.Clear(_chainVisited, 0, _chainVisited.Length);
            ResetSquallFoamTrail();
        }

        /// <summary>Только после первого Шквала расстановки: без него хеш прежний бит в бит.</summary>
        private void HashSquall(ref ulong hash)
        {
            if (_squall.Serial != 0)
            {
                Hashing.Mix(ref hash, 0x5351414C);   // "SQAL"
                _squall.HashInto(ref hash);
            }
            HashSquallFoamTrail(ref hash);
        }
    }
}
