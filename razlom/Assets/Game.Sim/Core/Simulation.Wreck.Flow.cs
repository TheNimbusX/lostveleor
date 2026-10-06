namespace Game.Sim
{
    /// <summary>Ход серии Крушения: нажатия, замахи, окно, удержание, выход, конец (Simulation.Wreck).</summary>
    public sealed partial class Simulation
    {
        /// <summary>Первое нажатие: цена и кулдаун от каста уже взяты в ResolveAbilityCasts.</summary>
        private void BeginWreck(int slot, FixVec2 aim)
        {
            EnsureWreckBuffers();
            // Вал, стена и Призрачный якорь прошлой серии доживают своё: их поля не трогаем.
            _wreck.Serial++;
            _wreck.Slot = slot;
            _wreck.CastTick = Tick;
            _wreck.Strikes = 0;
            _wreck.Charge = 0;
            _wreck.ChargeStartTick = -1;
            // Девятый вал: заряды копят махи этой серии.
            _wreck.NinthLanded = _wreck.NinthCharges = 0;
            BeginWreckStage(0, aim);
        }

        /// <summary>
        /// Следующее нажатие внутри окна: продолжение той же серии. Направление
        /// переспрашивается на каждом этапе — серия, прибитая к первому нажатию,
        /// разворачивала бы героя спиной к тем, кто подошёл за эти полсекунды.
        /// </summary>
        private void AdvanceWreck(FixVec2 aim) => BeginWreckStage(_wreck.Strikes, aim);

        /// <summary>
        /// Этап stage: направление — курсор этого нажатия (из буфера — курсор в тик нажатия),
        /// корпус встаёт по нему сразу; лишнего тика на разворот нет — как у серии сабли
        /// (Simulation.SabreCombo). Выпад бьёт туда, куда показал курсор третьего нажатия.
        /// </summary>
        private void BeginWreckStage(int stage, FixVec2 aim)
        {
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 direction = aim - hero;
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            direction = direction.Normalized();

            _wreck.Phase = WreckPhase.Windup;
            _wreck.Stage = stage;
            // Сабельный ритм v4 (06.10): мах 1 справа налево, мах 2 слева направо, выпад — 0.
            _wreck.Side = WreckSwingSide(stage);
            _wreck.StageStartTick = Tick;
            _wreck.Direction = direction;
            _wreck.WindowEndTick = -1;
            _wreck.HoldEndTick = _wreck.ExitWalkTick = _wreck.ExitEndTick = 0;
            _wreck.OverheadTick = -1;
            if (stage == WreckStages - 1)
            {
                // Выпад у всех форм один (06.10 вечером: Девятый вал не держат — тика «над головой» нет).
                _wreck.ContactTick = Tick + AbilityExecutionTicks(WreckLungeWindupTicks);
                _wreck.Charge = 0;
                _wreck.ChargeStartTick = -1;
            }
            else
            {
                int windup = stage == 0 ? WreckSwingWindupTicks : stage == 1 ? WreckBackswingWindupTicks : WreckFourthWindupTicks;
                _wreck.ContactTick = Tick + AbilityExecutionTicks(windup);
                // Махи бьют по ходу головы (Simulation.Wreck.Sweep); «Четвёртый удар» — круг в тик удара.
                if (stage < WreckStages - 1) ArmWreckSweep(stage, windup);
            }
            Entities.Facing[PlayerId] = direction;
            // Точка выпада известна с нажатия: вид ведёт к ней голову якоря, HUD замирает.
            AbilityBuild build = WreckBuild;
            if (stage == WreckStages - 1 && build != null) WreckAimSlam(build, false);
        }

        /// <summary>Часы действия этапа: контакт — удар этапа, конец — удар + проводка (махи) или + удержание и выход.</summary>
        private void WreckClockAtCast(out int contact, out int end)
        {
            contact = _wreck.ContactTick;
            end = contact + WreckAfterStrikeTicks(_wreck.Stage);
        }

        private int WreckAfterStrikeTicks(int stage)
            => stage < WreckStages - 1 ? WreckFollowTicks : WreckHoldTicks + WreckExitTicksFor(_wreck.Slot);

        private int WreckExitTicksFor(int slot) => FormIs(slot, PelagForm.WreckBreakwater) ? WreckBreakwaterExitTicks : WreckExitTicks;

        private bool WreckClockIsOurs => _playerAction.DefinitionId == AbilityDefinition.WreckId
            && _playerAction.StartTick == _wreck.StageStartTick && !_playerAction.Interrupted;

        /// <summary>Контакт и конец на часах — только если часы ещё этого этапа (как AbordageSetClock).</summary>
        private void WreckSetClock(int contact, int end)
        {
            if (!WreckClockIsOurs) return;
            _playerAction.ContactTick = contact;
            _playerAction.EndTick = System.Math.Max(contact + 1, end);
            ExtendPelagBasicContinuation(_playerAction.EndTick);
            ExtendSabreChain(_playerAction.EndTick);
        }

        /// <summary>
        /// Каждый тик после кастов: вал и стена, Призрачный якорь (живут и после конца серии), шаг
        /// самой серии, потом голова маха по сектору. input с 06.10 вечером не нужен (Девятый вал не держат);
        /// параметр оставлен вызову в Step.
        /// </summary>
        private void UpdateWreck(in InputFrame input)
        {
            UpdateWreckWave();
            UpdateWreckGhost();
            UpdateWreckSeries();
            // Голова маха идёт по сектору и после удара этапа — в тик удара бьёт уже после WreckStage.
            UpdateWreckSweep();
        }

        private void UpdateWreckSeries()
        {
            if (_wreck.Phase == WreckPhase.None) return;
            AbilityBuild build = WreckBuild;
            if (build == null || !Entities.Alive[PlayerId]) { EndWreck(WreckEnd.Interrupted); return; }

            switch (_wreck.Phase)
            {
                case WreckPhase.Windup:
                    // Выпад v4: шаг корня 0,6 м в последние тики замаха (Simulation.Wreck.Lunge), точка — от нового места.
                    StepWreckLunge();
                    // Удар оземь: снимок держит точку по герою этого тика (его мог сдвинуть отброс).
                    if (_wreck.Stage == WreckStages - 1 && Tick < _wreck.ContactTick) WreckAimSlam(build, false);
                    if (Tick >= _wreck.ContactTick) WreckStrike(build);
                    break;
                case WreckPhase.Follow:
                    if (Tick >= _wreck.ContactTick + WreckFollowTicks) _wreck.Phase = WreckPhase.Window;
                    break;
                case WreckPhase.Window:
                    // Окно закрылось, а нажатия не было: якорь на спину, кулдаун от конца окна.
                    if (Tick > _wreck.WindowEndTick) ExpireWreckWindow(build);
                    break;
                case WreckPhase.Hold:
                    if (Tick >= _wreck.HoldEndTick) BeginWreckExit();
                    break;
                case WreckPhase.Exit:
                    // Ходьба этого тика уже прошла (MovePlayer раньше): пошёл — выход сорван.
                    if (Tick >= _wreck.ExitWalkTick && Entities.Velocity[PlayerId].LengthSq.Raw != 0) AfterWreckExit(build, WreckEnd.WalkedOut);
                    else if (Tick >= _wreck.ExitEndTick) AfterWreckExit(build, WreckEnd.Done);
                    break;
            }
        }

        private void BeginWreckExit()
        {
            int locked = FormIs(_wreck.Slot, PelagForm.WreckBreakwater) ? WreckBreakwaterExitLockedTicks : WreckExitLockedTicks;
            _wreck.Phase = WreckPhase.Exit;
            _wreck.ExitWalkTick = Tick + locked + 1;
            _wreck.ExitEndTick = Tick + WreckExitTicksFor(_wreck.Slot);
        }

        /// <summary>Выход кончился. С «Четвёртым ударом» серия ещё ждёт нажатия в окне.</summary>
        private void AfterWreckExit(AbilityBuild build, WreckEnd reason)
        {
            if (_wreck.Strikes >= WreckStageCount(build)) { EndWreck(reason); return; }
            _wreck.Phase = WreckPhase.Window;
            if (Tick > _wreck.WindowEndTick) ExpireWreckWindow(build);
        }

        private void ExpireWreckWindow(AbilityBuild build)
        {
            int slot = _wreck.Slot;
            EndWreck(WreckEnd.WindowExpired);
            if ((uint)slot < (uint)AbilitySlots) _abilityReadyTick[slot] = Tick + AbilityCooldownTicks(build);
        }

        /// <summary>Серия кончилась. Событие WreckEnded — одно на серию; Призрачный якорь, если брошен, ещё падает.</summary>
        private void EndWreck(WreckEnd reason)
        {
            if (_wreck.Phase == WreckPhase.None) return;
            bool clockIsOurs = WreckClockIsOurs;
            _wreck.Phase = WreckPhase.None;
            if (clockIsOurs && reason != WreckEnd.Interrupted) _playerAction.EndTick = Tick;
            _events.Add(new SimEvent(SimEventType.WreckEnded, PlayerId, -1, (int)reason, false,
                Entities.Position[PlayerId], DamageType.Physical, DamageOrigin.Ability, _wreck.Serial));
        }

        /// <summary>Снятие чужим действием (CancelPlayerAction): рывок, другой навык, оглушение.</summary>
        private void StopWreck() => EndWreck(WreckEnd.Interrupted);
    }
}
