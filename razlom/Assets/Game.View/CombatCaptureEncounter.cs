using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Только capture fixture: реальные виды со штатными статами и контроллерами.
    // Обычный забег этот код не вызывает и своё начальное состояние не меняет.
    internal static class CombatCaptureEncounter
    {
        public static void Configure(TickDriver driver, string encounter, int count,
            CombatFeelCaptureTier tier, bool active)
        {
            var sim = driver.Sim;
            sim.SetupForestEncounter(driver.Run.Map, 20260829UL, 1000);
            var entities = sim.Entities;
            FixVec2 center = entities.Position[Simulation.PlayerId];
            int placed = 0;
            for (int i = 1; i < entities.Count; i++)
            {
                bool include = encounter == "mixed" || entities.Kind[i] == EnemyKind.ForestRootSwarm;
                include &= placed < Mathf.Max(1, count);
                if (!include) { entities.Alive[i] = false; continue; }
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio(placed, Mathf.Max(1, count));
                Fix64 radius = Fix64.Ratio(15 + (placed / 6) * 8, 10);
                entities.Position[i] = center + new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * radius;
                entities.Facing[i] = (center - entities.Position[i]).Normalized();
                entities.Health[i] = tier == CombatFeelCaptureTier.Kill ? 1 : 1000;
                if (!active)
                {
                    entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                    entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                    entities.RefreshStats(i);
                    entities.NextAttackTick[i] = int.MaxValue;
                }
                placed++;
            }
            entities.Stats[Simulation.PlayerId].SetBase(StatType.CritChance,
                tier == CombatFeelCaptureTier.Critical ? Fix64.One : Fix64.Zero);
            entities.RefreshStats(Simulation.PlayerId);
            sim.Grid.Rebuild(entities);
            Debug.Log($"[capture-encounter] {encounter} alive={placed} active={active}");
        }
    }
}
