namespace Game.View
{
    internal enum HudAbilityBlock { None, Cooldown, Resource, Dead }

    // Один результат для мыши, клавиатуры, иконки и описания причины отказа.
    internal readonly struct HudAbilityAvailability
    {
        public readonly HudAbilityBlock Block;
        public readonly int RemainingTicks, MissingResource;
        public bool Ready => Block == HudAbilityBlock.None;
        HudAbilityAvailability(HudAbilityBlock block, int ticks = 0, int missing = 0)
        { Block = block; RemainingTicks = ticks; MissingResource = missing; }

        public static HudAbilityAvailability Evaluate(bool alive, bool continuingCombo, int ticks, int available, int cost)
        {
            if (!alive) return new HudAbilityAvailability(HudAbilityBlock.Dead);
            if (continuingCombo) return new HudAbilityAvailability(HudAbilityBlock.None);
            if (ticks > 0) return new HudAbilityAvailability(HudAbilityBlock.Cooldown, ticks);
            if (available < cost) return new HudAbilityAvailability(HudAbilityBlock.Resource, 0, cost - available);
            return new HudAbilityAvailability(HudAbilityBlock.None);
        }
    }
}
