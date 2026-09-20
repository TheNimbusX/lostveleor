from pathlib import Path
root=Path('razlom/Assets')
def edit(path,pairs):
 p=root/path;s=p.read_text(encoding='utf-8-sig')
 for a,b in pairs:
  assert a in s,(path,a[:65]);s=s.replace(a,b)
 p.write_text(s,encoding='utf-8')
edit('Game.View/PlayerHud.cs',[
 ('if (id == AbilityDefinition.WhirlwindId) return "Круговой', 'if (id == AbilityDefinition.SkewerId) return "Выпад к курсору сквозь врагов. Каждый получает урон один раз. Прерывает текущую атаку.";\n            if (id == AbilityDefinition.BackblastId) return "Бутылка взрывается под ногами, Пелаг отскакивает от курсора. Прерывает текущую атаку.";\n            if (id == AbilityDefinition.WhirlwindId) return "Круговой'),
 ('if (definitionId == AbilityDefinition.CleaveId) return "Icon_Cleave";', 'if (definitionId == AbilityDefinition.SkewerId) return "Icon_Skewer";\n            if (definitionId == AbilityDefinition.BackblastId) return "Icon_Backblast";\n            if (definitionId == AbilityDefinition.AnchorSlamId) return "Icon_AnchorSlam";\n            if (definitionId == AbilityDefinition.WreckId) return "Icon_Wreck";\n            if (definitionId == AbilityDefinition.FireFlaskId) return "Icon_FireFlask";\n            if (definitionId == AbilityDefinition.CleaveId) return "Icon_Cleave";'),
 ('if (definitionId == AbilityDefinition.CleaveId) return "РАССЕКАЮЩИЙ УДАР";', 'if (definitionId == AbilityDefinition.SkewerId) return "НА ВЫЛЕТ";\n            if (definitionId == AbilityDefinition.BackblastId) return "ОТБОЙ";\n            if (definitionId == AbilityDefinition.CleaveId) return "РАССЕКАЮЩИЙ УДАР";'),
 ('else if (id == AbilityDefinition.DashId)\n                ReachArrow', '''else if (id == AbilityDefinition.SkewerId)
                ReachCapsule(center, forward, radius, build.Get(AbilityStatType.Width).ToFloat() * .5f);
            else if (id == AbilityDefinition.BackblastId)
            {
                ReachArrow(center, center - forward * radius);
                ReachArc(center, build.Get(AbilityStatType.Width).ToFloat(), 0f, Mathf.PI * 2f);
            }
            else if (id == AbilityDefinition.DashId)
                ReachArrow'''),
 ('CollectTooltipValues(build, _tooltipValues);', 'CollectTooltipValues(build, _tooltipValues, _driver.Sim);'),
 ('CollectTooltipValues(AbilityBuild build, TooltipValue[] into)', 'CollectTooltipValues(AbilityBuild build, TooltipValue[] into, Simulation sim = null)'),
 ('(build.CooldownTicks / (float)Simulation.TicksPerSecond)', '((sim != null ? sim.AbilityCooldownTicks(build) : build.CooldownTicks) / (float)Simulation.TicksPerSecond)'),
 ('left / (float)Mathf.Max(1, build.CooldownTicks)', 'left / (float)Mathf.Max(1, sim.AbilityCooldownTicks(build))'),
 ('else if (id == AbilityDefinition.AnchorSlamId)\n                AddTooltipValue', 'else if (id == AbilityDefinition.BackblastId)\n                AddTooltipValue(5, "Радиус взрыва", build.Get(AbilityStatType.Width).ToFloat().ToString("0.#") + " м");\n            else if (id == AbilityDefinition.AnchorSlamId || id == AbilityDefinition.SkewerId)\n                AddTooltipValue')])
edit('Game.View/CombatHudView.cs',[
 ('build.CooldownTicks)', 'sim.AbilityCooldownTicks(build))'),
 ('PlayerHud.CollectTooltipValues(build, _values)', 'PlayerHud.CollectTooltipValues(build, _values, sim)')])
edit('Editor/ForestBudTestWindow.cs',[('падает через 2 секунды','падает через 1,5 секунды')])
edit('Game.Tests/CombatMobilityTests.cs',[
 ('BelowHalfSpeed','BelowThreeQuarterSpeed'),('* Fix64.Half).ToFloat()', '* Fix64.Ratio(3, 4)).ToFloat()'),('выше 50%','выше 75%'),
 ('(sim.Entities.MoveStep[Simulation.PlayerId] * Fix64.Ratio(3, 4)).ToFloat() + 0.0001f,\n                    "способность', '(sim.Entities.MoveStep[Simulation.PlayerId] * (sim.Tick - 1 < sim.PlayerAction.ContactTick ? Fix64.Ratio(3, 4) : Fix64.One)).ToFloat() + 0.0001f,\n                    "способность')])
edit('Game.Tests/MovementTests.cs',[('Is.GreaterThan(0.9)', 'Is.InRange(0.765, 0.767)')])
edit('Game.Tests/PelagPoolTests.cs',[('PoolHoldsTheEightApprovedAbilitiesInOrder','PoolPreservesOldOrderAndAppendsMobility'),('AbilityDefinition.AnchorLeapId, AbilityDefinition.FireFlaskId,','AbilityDefinition.AnchorLeapId, AbilityDefinition.FireFlaskId, AbilityDefinition.SkewerId, AbilityDefinition.BackblastId,')])
edit('Game.Tests/AnchorSlamTests.cs', [('sim.SetAbility(0, AbilityDefinition.Cleave(), new AbilityNode[0], 0);','sim.SetAbility(0, AbilityDefinition.Skewer(), new AbilityNode[0], 0);')])
edit('Game.Tests/ForestBudTests.cs', [('Assert.That(sim.Entities.Damage[bud], Is.EqualTo(5))','Assert.That(sim.Entities.Damage[bud], Is.EqualTo(20))')])
