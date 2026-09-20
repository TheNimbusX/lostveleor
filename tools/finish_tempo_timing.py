from pathlib import Path
root=Path(__file__).resolve().parents[1]/'razlom/Assets'
def edit(path,old,new):
 p=root/path;t=p.read_text(encoding='utf-8-sig');assert old in t,(path,old[:80]);p.write_text(t.replace(old,new),encoding='utf-8')
edit(Path('Game.View/CharacterAnimatorView.cs'),
 'elapsed / Simulation.BlazeGestureTicks','elapsed / Mathf.Max(1, sim.BlazeEndTick - sim.BlazeStartTick)')
edit(Path('Game.View/CharacterAnimatorView.cs'),
 'if (Time.time - _attackWarpStartedAt >= BasicAttackPresentationDuration) StopAttackWarp();',
 'if (Time.time - _attackWarpStartedAt >= BasicAttackPresentationDuration / CurrentAttackSpeed) StopAttackWarp();')
p=root/'Game.View/CharacterAnimatorView.cs';t=p.read_text(encoding='utf-8')
start=t.index('            if (!_attackWarpActive) return;');end=t.index('\n        }',start)
part=t[start:end].replace('_animator.SetFloat(AttackPlaybackSpeed, 1f);','_animator.SetFloat(AttackPlaybackSpeed, CurrentAttackSpeed);')
t=t[:start]+part+t[end:]
t=t.replace('_attackPresentationUntil = Time.time + BasicAttackPresentationDuration - elapsed;', '_attackPresentationUntil = Time.time + BasicAttackPresentationDuration / CurrentAttackSpeed - elapsed;')
t=t.replace('_actionProtectedUntil = Time.time + BasicAttackClipDuration - elapsed;', '_actionProtectedUntil = Time.time + PlayerAttackContactTime / CurrentAttackSpeed - elapsed;')
t=t.replace('private void StartAttackWarp()\n        {','private void StartAttackWarp()\n        {')
t=t.replace('windup = Mathf.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt()) / (float)Simulation.TicksPerSecond;', 'windup = sim.AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt()) / (float)Simulation.TicksPerSecond;')
p.write_text(t,encoding='utf-8')
edit(Path('Game.View/PelagAnchorSlamView.cs'), 'windup = Mathf.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt());', 'windup = sim.AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt());')
edit(Path('Game.View/PelagBlazeView.cs'),
 'float t=(sim.Tick-1+_driver.Alpha-sim.BlazeStartTick)/Simulation.TicksPerSecond;',
 'float t=(sim.Tick-1+_driver.Alpha-sim.BlazeStartTick) / Mathf.Max(1,sim.BlazeEndTick-sim.BlazeStartTick) * (Simulation.BlazeGestureTicks/(float)Simulation.TicksPerSecond);')
edit(Path('Game.View/RunHud.cs'), 'case StatType.AttackSpeed: return "СКОРОСТЬ АТАКИ";',
 'case StatType.AttackSpeed: return "СКОРОСТЬ АТАКИ";\n                case StatType.AbilitySpeed: return "СКОРОСТЬ ИСПОЛНЕНИЯ";\n                case StatType.CooldownRecovery: return "ВОССТАНОВЛЕНИЕ НАВЫКОВ";\n                case StatType.LavidiumRegen: return "ЛАВИДИЙ В СЕКУНДУ";')
edit(Path('Game.View/CampInventoryView.cs'), 'case StatType.AttackSpeed:return "Скор. атаки";',
 'case StatType.AbilitySpeed:return "Исполнение";case StatType.CooldownRecovery:return "Восст. навыков";case StatType.LavidiumRegen:return "Лавидий/с";case StatType.AttackSpeed:return "Скор. атаки";')
edit(Path('Game.View/PlayerHud.cs'), 'return "Icon_AnchorSlam";', 'return "Icon_AnchorSweep";')
edit(Path('Game.View/CombatAudio.cs'),
 'Mathf.Max(0f, Simulation.WhirlwindContactDelayTicks / (float)Simulation.TicksPerSecond - .21f)',
 'Mathf.Max(0f, (_driver.Sim.PlayerAction.ContactTick - _driver.Sim.PlayerAction.StartTick) / (float)Simulation.TicksPerSecond - .21f)')
edit(Path('Game.View/CombatAudio.cs'),
 '_anchorLandAt = Time.time + PelagAbilityTiming.LeapArrival;',
 '_anchorLandAt = Time.time + (_driver.Sim.PlayerAction.ContactTick - _driver.Sim.Tick + 1) / (float)Simulation.TicksPerSecond;')
