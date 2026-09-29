using Game.Sim;

namespace Game.View
{
    // Rooted — в конце: прежние причины сохраняют свои номера.
    internal enum HudAbilityBlock { None, Cooldown, Resource, Dead, Rooted }

    // Один результат для мыши, клавиатуры, иконки и описания причины отказа.
    internal readonly struct HudAbilityAvailability
    {
        public readonly HudAbilityBlock Block;
        public readonly int RemainingTicks, MissingResource;
        public bool Ready => Block == HudAbilityBlock.None;
        HudAbilityAvailability(HudAbilityBlock block, int ticks = 0, int missing = 0)
        { Block = block; RemainingTicks = ticks; MissingResource = missing; }

        /// <summary>
        /// rooted — кнопку держат корни (Simulation.AbilityHeldByRoots):
        /// способность двигает героя, а он в корнях.
        /// </summary>
        public static HudAbilityAvailability Evaluate(bool alive, bool continuingCombo, int ticks, int available, int cost, bool rooted)
        {
            if (!alive) return new HudAbilityAvailability(HudAbilityBlock.Dead);
            if (!continuingCombo)
            {
                if (ticks > 0) return new HudAbilityAvailability(HudAbilityBlock.Cooldown, ticks);
                if (available < cost) return new HudAbilityAvailability(HudAbilityBlock.Resource, 0, cost - available);
            }
            // Корни — последняя причина: перезарядка и нехватка лавидия
            // остаются на иконке со своим счётчиком и не мигают на секунду
            // корней, а корни снимают только ту готовность, что иначе была бы.
            // Продолжение комбо корни тоже держат — Sim проверяет их первыми.
            return new HudAbilityAvailability(rooted ? HudAbilityBlock.Rooted : HudAbilityBlock.None);
        }

        /// <summary>Состояние слота прямо из симуляции: общий вход для клавиатуры, мыши и обоих HUD.</summary>
        public static HudAbilityAvailability Of(Simulation sim, int slot, AbilityBuild build)
            => Evaluate(sim.Entities.Alive[Simulation.PlayerId],
                build.DefinitionId == AbilityDefinition.WreckId && sim.WreckComboOpen,
                sim.AbilityReadyTick(slot) - sim.Tick, sim.Entities.Lavidium[Simulation.PlayerId].ToInt(),
                Simulation.LavidiumCostOf(build), sim.AbilityHeldByRoots(slot));

        /// <summary>Короткая причина отказа: подсказка слота и строка над рядом при нажатии.</summary>
        public string Text
        {
            get
            {
                if (Block == HudAbilityBlock.Cooldown) return "Перезарядка · " + (RemainingTicks/(float)Simulation.TicksPerSecond).ToString("0.0") + " с";
                if (Block == HudAbilityBlock.Resource) return "Не хватает лавидия: " + MissingResource;
                if (Block == HudAbilityBlock.Dead) return "Герой без сознания";
                if (Block == HudAbilityBlock.Rooted) return "Корни держат";
                return string.Empty;
            }
        }
    }
}
