namespace Game.Sim
{
    /// <summary>Временный набор стенда, не рецепт предмета и не часть сохранения лагеря.</summary>
    public static class CombatTempoPreset
    {
        public static void Apply(Simulation sim, int preset)
        {
            var stats = sim.Entities.Stats[Simulation.PlayerId];
            int source = StableId.Of("test.combat_tempo");
            stats.RemoveSource(ModifierSource.Equipment, source);
            if (preset > 0)
            {
                bool fast = preset >= 2;
                stats.Add(new StatModifier(StatType.AbilitySpeed, ModifierOp.Flat,
                    fast ? Fix64.One : Fix64.Ratio(4, 10), ModifierSource.Equipment, source));
                stats.Add(new StatModifier(StatType.CooldownRecovery, ModifierOp.Flat,
                    fast ? Fix64.One : Fix64.Half, ModifierSource.Equipment, source));
                stats.Add(new StatModifier(StatType.LavidiumRegen, ModifierOp.Flat,
                    Fix64.FromInt(fast ? 6 : 3), ModifierSource.Equipment, source));
                stats.Add(new StatModifier(StatType.AttackSpeed, ModifierOp.Increased,
                    fast ? Fix64.Ratio(8, 10) : Fix64.Ratio(35, 100), ModifierSource.Equipment, source));
                stats.Add(new StatModifier(StatType.MoveSpeed, ModifierOp.Increased,
                    fast ? Fix64.Ratio(2, 10) : Fix64.Ratio(1, 10), ModifierSource.Equipment, source));
            }
            sim.RefreshPlayerStats(true);
        }
    }

    public sealed partial class Simulation
    {
        public void AddTempoMeleeEnemies(int count)
        {
            FixVec2 origin = Entities.Position[PlayerId];
            FixVec2 forward = Entities.Facing[PlayerId].Normalized();
            FixVec2 side = new FixVec2(-forward.Y, forward.X);
            for (int n = 0; n < count && Entities.Count < Entities.Capacity; n++)
            {
                var at = origin + forward * Fix64.FromInt(3 + n / 3)
                    + side * Fix64.Ratio((n % 3 - 1) * 17, 10);
                if (_layout != null) at = _layout.ClampToWalkable(at, Fix64.Ratio(85, 100));
                int id = Entities.Spawn(at, EnemyBaseHealth, Faction.Orvill);
                ConfigureEnemy(id);
                Entities.Aggro[id] = true;
                _events.Add(SimEvent.Spawn(id, at));
            }
            Grid.Rebuild(Entities);
        }
    }
}
