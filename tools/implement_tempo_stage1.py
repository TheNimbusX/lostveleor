from pathlib import Path
root=Path('razlom/Assets')
def edit(path, pairs):
 p=root/path;s=p.read_text(encoding='utf-8-sig')
 for a,b in pairs:
  if a not in s: raise RuntimeError(f'{path}: missing {a[:90]}')
  s=s.replace(a,b)
 p.write_text(s,encoding='utf-8')
edit('Game.Sim/Core/SimEvent.cs',[('ForestBudVolleyCancelled = 19,','ForestBudVolleyCancelled = 19,\n        BackblastBurst = 20,')])
edit('Game.Sim/Core/Simulation.cs',[
 ('entityId == PlayerId ? AttackWindupTicks','entityId == PlayerId ? PlayerAttackWindupTicks'),
 ('PlayerTurnStep = Fix64.TwoPi / 18','PlayerTurnStep = Fix64.TwoPi / 9'),
 ('_cleavePreviousPositions = new FixVec2[capacity];','_cleavePreviousPositions = new FixVec2[capacity];\n            _mobilityHits = new bool[capacity];'),
 ('private void ResetAbilityState()\n        {','private void ResetAbilityState()\n        {\n            ResetTempo();'),
 ('bool moveOrdered = input.Has(InputFlags.MoveOrder);','bool moveOrdered = input.Has(InputFlags.MoveOrder) || input.Has(InputFlags.DirectMovement);'),
 ('public void Step(in InputFrame input)','public void Step(in InputFrame rawInput)'),
 ('RegenerateLavidium();','RegenerateLavidium();\n            InputFrame input = PrepareCombatInput(rawInput);'),
 ('UpdateBlazeTrail();','UpdateBlazeTrail();\n            UpdateMobility();'),
 ('AdvanceWreck(input.Aim);\n                    continue;','AdvanceWreck(input.Aim);\n                    CaptureAbilityClock(slot);\n                    continue;'),
 ('Tick + WhirlwindContactDelayTicks','Tick + AbilityExecutionTicks(WhirlwindContactDelayTicks)'),
 ('Tick + AnchorKit.LeapWindupTicks','Tick + AbilityExecutionTicks(AnchorKit.LeapWindupTicks)'),
 ('Tick + build.CooldownTicks','Tick + AbilityCooldownTicks(build)'),
 ('SpendLavidium(build);','SpendLavidium(build);\n                CaptureAbilityClock(slot);'),
 ('else if (build.DefinitionId == AbilityDefinition.DashId)\n                {','else if (build.DefinitionId == AbilityDefinition.SkewerId || build.DefinitionId == AbilityDefinition.BackblastId)\n                {\n                    BeginMobility(slot, input.Aim);\n                }\n                else if (build.DefinitionId == AbilityDefinition.DashId)\n                {'),
 ('if (AnchorSlamActive)\n','if (AnchorSlamActive && _slamImpactTick >= Tick)\n'),
 ('if (CleaveActive && !CleaveMovable)','if (CleaveActive && !CleaveMovable && Tick <= _cleaveImpactTick)'),
 ('? fullSpeed * Fix64.Half','? fullSpeed * Fix64.Ratio(3, 4)'),
 ('if (i == PlayerId && (AnchorSlamActive || CleaveActive || BlazeCasting || Tick < _abilityMovePenaltyUntilTick\n                    || _leapLaunchTick >= 0 || Entities.ForcedTicksLeft[i] > 0)) continue;',
  'if (i == PlayerId && (!_playerAction.CanChainAt(Tick) || Entities.ForcedTicksLeft[i] > 0)) continue;'),
 ('if (i == PlayerId) _nextPlayerAttackVariant ^= 1;',
  'if (i == PlayerId)\n                {\n                    CancelPlayerAction();\n                    _nextPlayerAttackVariant ^= 1;\n                    SetActionClock(-1, 0, Tick + PlayerAttackWindupTicks, Tick + Entities.AttackCooldown[i]);\n                }'),
 ('HashAnchorSlam(ref hash);','HashTempo(ref hash);\n            HashAnchorSlam(ref hash);'),
 ('if (j <= i) continue;','if (j <= i) continue;\n                    if ((i == PlayerId || j == PlayerId) && _mobilitySlot >= 0\n                        && _abilityBuilds[_mobilitySlot].DefinitionId == AbilityDefinition.SkewerId) continue;'),
 ('// Желаемая скорость достигается не сразу:',
  'if (input.Has(InputFlags.DirectMovement))\n            {\n                _hasMoveOrder = _explicitMoveOrder = false;\n                _attackTarget = -1;\n                step = input.MoveDirection.ClampLength(Fix64.One) * speed;\n                desiredFacing = input.Aim - pos;\n                finishingTurnInPlace = false;\n            }\n\n            // Желаемая скорость достигается не сразу:'),
 ('if (!Entities.Alive[PlayerId])\n            {\n                ClearMoveOrder();',
  'if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))\n            {\n                Entities.Velocity[PlayerId] = FixVec2.Zero;\n                ClearMoveOrder();'),
])
p=root/'Game.Sim/Core/Simulation.cs';s=p.read_text(encoding='utf-8')
a=s.index('                StopAnchorSlam();',s.index('private void ResolveAbilityCasts'))
b=s.index('\n                if (build.DefinitionId == AbilityDefinition.WhirlwindId)',a)
s=s[:a]+'                CancelPlayerAction();\n'+s[b:];p.write_text(s,encoding='utf-8')
edit('Game.Sim/Abilities/AnchorSlam.cs',[
 ('System.Math.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt())','AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt())'),
 ('Tick + build.Get(AbilityStatType.DurationTicks).ToInt()','Tick + AbilityExecutionTicks(build.Get(AbilityStatType.DurationTicks).ToInt())')])
edit('Game.Sim/Abilities/SabreKit.cs',[
 ('System.Math.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt())','AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt())'),
 ('Tick + System.Math.Max(build.Get(AbilityStatType.DurationTicks).ToInt(),\n                build.Get(AbilityStatType.WindupTicks).ToInt() + build.Get(AbilityStatType.ContactWindowTicks).ToInt() + 1)',
  'System.Math.Max(Tick + AbilityExecutionTicks(build.Get(AbilityStatType.DurationTicks).ToInt()),\n                _cleaveImpactTick + build.Get(AbilityStatType.ContactWindowTicks).ToInt() + 1)'),
 ('Tick + BlazeIgnitionDelayTicks','Tick + AbilityExecutionTicks(BlazeIgnitionDelayTicks)'),
 ('Tick + BlazeGestureTicks','Tick + AbilityExecutionTicks(BlazeGestureTicks)')])
edit('Game.Sim/Abilities/AnchorCombo.cs',[
 ('Tick + (windup < 1 ? 1 : windup)','Tick + AbilityExecutionTicks(windup)'),
 ('Tick + build.CooldownTicks','Tick + AbilityCooldownTicks(build)')])
edit('Game.Sim/Core/Simulation.Talents.cs',[
 ('Tick + build.CooldownTicks','Tick + AbilityCooldownTicks(build)'),
 ('Tick + AnchorKit.LeapWindupTicks + AnchorKit.LeapTicks','Tick + AbilityExecutionTicks(AnchorKit.LeapWindupTicks) + AnchorKit.LeapTicks')])
edit('Game.Tests/ForestBudTests.cs',[
 ('ExactlySixtyTicks','ExactlyFortyFiveTicks'),('Is.EqualTo(60)','Is.EqualTo(45)'),
 ('new[] { 84, 90, 96, 102, 108 }','new[] { 69, 75, 81, 87, 93 }'),
 ('Is.EqualTo(9975)','Is.EqualTo(9900)'),('Is.EqualTo(9990));\n            Assert.That(sim.TryGetForestBudAttack','Is.EqualTo(9960));\n            Assert.That(sim.TryGetForestBudAttack'),
 ('Until(sim, 84)','Until(sim, 69)'),('hit ? 9995 : 10000','hit ? 9980 : 10000'),
 ('Until(sim, 109);','Until(sim, 94);')])
