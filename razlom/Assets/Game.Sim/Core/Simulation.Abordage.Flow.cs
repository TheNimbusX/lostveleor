namespace Game.Sim
{
    /// <summary>Ход Абордажа: каст, якорь, зацеп, тяга, выход, возврат якоря (Simulation.Abordage).</summary>
    public sealed partial class Simulation
    {
        /// <summary>Каст по цели, которую выбрал игрок (уже проверена ValidAbilityTarget). Тайминг — прогноз.</summary>
        private void BeginAbordage(int slot, int target)
        {
            FixVec2 at = Entities.Position[PlayerId];
            FixVec2 to = AbordageLandingSpot(target, at);
            // Взгляд до каста (AbordageFace ниже его перепишет): цель за спиной — замах на тик дольше.
            int windup = AbilityExecutionTicks(AbordageWindupTicks)
                         + (AbordageTargetBehind(Entities.Facing[PlayerId], Entities.Position[target] - at) ? AbordageTurnWindupTicks : 0);
            int hook = AbordageHookTicks(FixVec2.Distance(at, AbordageBitePoint(target, at)));
            int pull = AbordagePullTicks(FixVec2.Distance(at, to));
            int bite = Tick + windup + hook;

            // Фронт формы и Гейзер прошлого каста доживают своё (запасной заряд): их поля не трогаем.
            _abordage.Serial++;
            _abordage.Slot = slot;
            _abordage.Phase = AbordagePhase.Windup;
            _abordage.CastTick = Tick;
            _abordage.Target = target;
            _abordage.From = at;
            _abordage.To = to;
            _abordage.AnchorFrom = _abordage.AnchorAt = at;
            _abordage.WindupTicks = windup;
            _abordage.ReleaseTick = Tick + windup;
            _abordage.BiteTick = bite;
            _abordage.ArriveTick = bite + (pull > 0 ? pull : 1);
            _abordage.PhaseEndTick = Tick + windup;
            _abordage.ExitWalkTick = 0;
            _abordage.HookCenter = Entities.Position[target];
            _abordage.Spare = _abordage.Hull = _abordage.Frozen = _abordage.Landed = false;
            AbordageFace(Entities.Position[target] - at);
        }

        /// <summary>Часы действия в каст: контакт — прогноз удара, конец — удар + удержание + выход.</summary>
        private void AbordageClockAtCast(out int contact, out int end)
        {
            contact = _abordage.ArriveTick;
            end = contact + AbordageHoldTicks + AbordageExitTicks;
        }

        private bool AbordageClockIsOurs => _playerAction.DefinitionId == AbilityDefinition.AnchorLeapId
            && _playerAction.StartTick == _abordage.CastTick && !_playerAction.Interrupted;

        /// <summary>Контакт и конец на часах — только если часы ещё этого каста (как SquallSetClockEnd).</summary>
        private void AbordageSetClock(int contact, int end)
        {
            if (!AbordageClockIsOurs) return;
            _playerAction.ContactTick = contact;
            _playerAction.EndTick = System.Math.Max(contact + 1, end);
            ExtendPelagBasicContinuation(_playerAction.EndTick);
            ExtendSabreChain(_playerAction.EndTick);
        }

        /// <summary>
        /// Каждый тик на месте прежнего запуска тяги: фронт формы и падения
        /// Гейзера (живут и после конца каста), потом шаг самого Абордажа.
        /// </summary>
        private void UpdateAbordage()
        {
            UpdateAbordageWave();
            UpdateAbordageGeysers();
            if (_abordage.Phase == AbordagePhase.None) return;
            if (!Entities.Alive[PlayerId]) { EndAbordage(AbordageEnd.Interrupted); return; }
            AbilityBuild build = AbordageBuild;
            if (build == null) { EndAbordage(AbordageEnd.Interrupted); return; }
            // Чужой отброс или волок перезаписал тягу или сдвинул стоящего героя — срыв.
            if (ForcedMotion.IsActive(Entities, PlayerId) && Entities.ForcedKind[PlayerId] != (byte)ForcedMotionKind.Lunge)
            {
                EndAbordage(AbordageEnd.Interrupted);
                return;
            }

            switch (_abordage.Phase)
            {
                case AbordagePhase.Windup:
                    if (SquallTargetValid(_abordage.Target)) AbordageFace(Entities.Position[_abordage.Target] - Entities.Position[PlayerId]);
                    if (Tick >= _abordage.PhaseEndTick) ReleaseAnchor(build);
                    break;
                case AbordagePhase.Hook:
                    StepAnchor(build);
                    break;
                case AbordagePhase.Pull:
                    StepPull(build);
                    break;
                case AbordagePhase.Strike:
                    if (Tick >= _abordage.PhaseEndTick) BeginAbordageExit();
                    break;
                case AbordagePhase.Exit:
                    // Ходьба этого тика уже прошла (MovePlayer раньше): пошёл — выход сорван.
                    if (Tick >= _abordage.ExitWalkTick && Entities.Velocity[PlayerId].LengthSq.Raw != 0)
                        EndAbordage(AbordageEnd.WalkedOut);
                    else if (Tick >= _abordage.PhaseEndTick) EndAbordage(AbordageEnd.Done);
                    break;
                case AbordagePhase.Recall:
                    if (Tick >= _abordage.PhaseEndTick) EndAbordage(AbordageEnd.NoTarget);
                    break;
            }
        }

        /// <summary>Цель жива и годна, иначе перевыбор в 1,5 м от её места; false — некого, якорь назад.</summary>
        private bool AbordageKeepTarget(AbilityBuild build)
        {
            if (SquallTargetValid(_abordage.Target)) return true;
            int next = AbordageRetarget(build, _abordage.Target);
            if (next < 0) { BeginAbordageRecall(); return false; }
            _abordage.Target = next;
            return true;
        }

        /// <summary>Выпуск якоря правой рукой в тик каста + замах. Полёт — по нынешнему месту цели.</summary>
        private void ReleaseAnchor(AbilityBuild build)
        {
            if (!AbordageKeepTarget(build)) return;
            int target = _abordage.Target;
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 bite = AbordageBitePoint(target, hero);
            FixVec2 toward = bite - hero;
            Fix64 length = toward.Length;
            FixVec2 hand = length.Raw == 0 ? hero : hero + toward / length * Fix64.Min(AbordageHandReach, length);
            int hook = AbordageHookTicks(length);

            _abordage.Phase = AbordagePhase.Hook;
            _abordage.AnchorFrom = _abordage.AnchorAt = hand;
            _abordage.ReleaseTick = Tick;
            _abordage.BiteTick = Tick + hook;
            AbordageFace(Entities.Position[target] - hero);
            _events.Add(new SimEvent(SimEventType.AbordageThrow, PlayerId, target, hook, false, hero,
                DamageType.Physical, DamageOrigin.Ability, _abordage.Serial));
            int pull = AbordagePullTicks(FixVec2.Distance(hero, AbordageLandingSpot(target, hero)));
            _abordage.ArriveTick = _abordage.BiteTick + (pull > 0 ? pull : 1);
            AbordageSetClock(_abordage.ArriveTick, _abordage.ArriveTick + AbordageHoldTicks + AbordageExitTicks);
        }

        /// <summary>Якорь летит к ТЕКУЩЕЙ точке укуса и приходит ровно в BiteTick; там — зацеп.</summary>
        private void StepAnchor(AbilityBuild build)
        {
            int before = _abordage.Target;
            if (!AbordageKeepTarget(build)) return;
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 bite = AbordageBitePoint(_abordage.Target, hero);
            if (_abordage.Target != before)
            {
                // Перевыбор: якорь доворачивает к новой цели с той же скоростью.
                int more = AbordageHookTicks(FixVec2.Distance(_abordage.AnchorAt, bite) + AbordageHandReach);
                if (_abordage.BiteTick < Tick + more) _abordage.BiteTick = Tick + more;
            }
            int left = _abordage.BiteTick - Tick + 1;
            _abordage.AnchorAt = left <= 1 ? bite : _abordage.AnchorAt + (bite - _abordage.AnchorAt) / Fix64.FromInt(left);
            AbordageFace(Entities.Position[_abordage.Target] - hero);
            if (Tick >= _abordage.BiteTick) HookTarget(build);
        }

        /// <summary>
        /// Зацеп — тик натяга: тело стоит, цепь прямая. Тяга по длине до точки
        /// посадки (ForcedMotion Lunge, тело едет со следующего тика); короче
        /// 0,3 м — тяги нет, удар тиком позже. «Сорвать атаку» и отсчёт «Разгона» — здесь.
        /// </summary>
        private void HookTarget(AbilityBuild build)
        {
            int target = _abordage.Target;
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 to = AbordageLandingSpot(target, hero);
            int pull = AbordagePullTicks(FixVec2.Distance(hero, to));

            _abordage.Phase = AbordagePhase.Pull;
            _abordage.From = hero;
            _abordage.To = pull > 0 ? to : hero;
            _abordage.HookCenter = Entities.Position[target];
            _abordage.Hull = ThicketHullActive(target);
            _abordage.Frozen = false;
            _abordage.AnchorAt = AbordageBitePoint(target, hero);
            _abordage.ArriveTick = Tick + (pull > 0 ? pull : 1);
            _abordage.PhaseEndTick = _abordage.ArriveTick;
            if (pull > 0) ForcedMotion.Begin(Entities, PlayerId, to, pull, ForcedMotionKind.Lunge);
            AbordageFace(pull > 0 ? to - hero : Entities.Position[target] - hero);
            BoardingUpgradesAtHook(build, target);

            _events.Add(new SimEvent(SimEventType.AbordageHook, PlayerId, target, _abordage.ArriveTick - Tick, _abordage.Hull,
                _abordage.To, DamageType.Physical, DamageOrigin.Ability, _abordage.Serial));
            if (_abordage.Spare) _abilityReadyTick[_abordage.Slot] = _abordage.ArriveTick + 1;
            AbordageSetClock(_abordage.ArriveTick, _abordage.ArriveTick + AbordageHoldTicks + AbordageExitTicks);
        }

        /// <summary>Тяга: точка посадки по текущему центру цели (оставшиеся тики те же), в тик прибытия — удар.</summary>
        private void StepPull(AbilityBuild build)
        {
            int target = _abordage.Target;
            FixVec2 hero = Entities.Position[PlayerId];
            bool alive = SquallTargetValid(target);
            if (!alive) _abordage.Frozen = true;
            else if (!_abordage.Frozen)
            {
                if ((Entities.Position[target] - _abordage.HookCenter).LengthSq > AbordageHomingLimit * AbordageHomingLimit)
                    _abordage.Frozen = true;
                else if (ForcedMotion.IsActive(Entities, PlayerId))
                {
                    _abordage.To = AbordageLandingSpot(target, hero);
                    Entities.ForcedTarget[PlayerId] = _abordage.To;
                }
            }
            if (alive) _abordage.AnchorAt = AbordageBitePoint(target, hero);
            if (Tick >= _abordage.ArriveTick) StrikeAbordage(build);
        }

        private void BeginAbordageExit()
        {
            _abordage.Phase = AbordagePhase.Exit;
            _abordage.PhaseEndTick = Tick + AbordageExitTicks;
            _abordage.ExitWalkTick = Tick + AbordageExitLockedTicks;
            AbordageSetClock(_playerAction.ContactTick, _abordage.PhaseEndTick);
        }

        /// <summary>Цели нет до зацепа: якорь назад, кулдаун AbordageRecallCooldownTicks, цена уже потрачена.</summary>
        private void BeginAbordageRecall()
        {
            if (_abordage.Phase == AbordagePhase.Windup)
                _abordage.AnchorFrom = _abordage.AnchorAt = Entities.Position[PlayerId];
            _abordage.Phase = AbordagePhase.Recall;
            _abordage.PhaseEndTick = Tick + AbordageRecallTicks;
            _abordage.ArriveTick = Tick;
            int slot = _abordage.Slot;
            if ((uint)slot < (uint)AbilitySlots && _abilityReadyTick[slot] > Tick + AbordageRecallCooldownTicks)
                _abilityReadyTick[slot] = Tick + AbordageRecallCooldownTicks;
            AbordageSetClock(Tick, _abordage.PhaseEndTick);
        }

        /// <summary>Абордаж кончился. Событие AbordageEnded — один раз на каст.</summary>
        private void EndAbordage(AbordageEnd reason)
        {
            if (_abordage.Phase == AbordagePhase.None) return;
            bool clockIsOurs = AbordageClockIsOurs;
            FixVec2 at = reason == AbordageEnd.NoTarget ? _abordage.AnchorAt : Entities.Position[PlayerId];
            _abordage.Phase = AbordagePhase.None;
            _abordage.PhaseEndTick = Tick;
            if (reason == AbordageEnd.Interrupted && Entities.ForcedKind[PlayerId] == (byte)ForcedMotionKind.Lunge)
                ForcedMotion.Clear(Entities, PlayerId);
            if (clockIsOurs && reason != AbordageEnd.Interrupted) _playerAction.EndTick = Tick;
            _events.Add(new SimEvent(SimEventType.AbordageEnded, PlayerId, -1, (int)reason, false, at,
                DamageType.Physical, DamageOrigin.Ability, _abordage.Serial));
        }

        /// <summary>Снятие чужим действием (CancelPlayerAction): рывок, способность, оглушение.</summary>
        private void StopAbordage() => EndAbordage(AbordageEnd.Interrupted);
    }
}
