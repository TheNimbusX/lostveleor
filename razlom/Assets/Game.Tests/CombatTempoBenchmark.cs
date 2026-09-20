using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class CombatTempoBenchmark
    {
        [Test, Explicit("Контрольная запись чисел до и после изменения темпа")]
        public void RecordFixedInputSequence()
        {
            var sim = new Simulation(917, 64);
            sim.SetupProvingGround(100000, Fix64.Zero, Fix64.Zero);
            sim.PlayerInvulnerable = true;
            sim.SetAbility(0, AbilityDefinition.AnchorSlam(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(2, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(3, AbilityDefinition.FireFlask(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            var casts = new List<string>();
            int blocked = 0, resourceRefusals = 0;
            for (int t = 0; t < 300; t++)
            {
                var input = InputFrame.Empty;
                input.Aim = new FixVec2(Fix64.FromInt(12), Fix64.Zero);
                input.Flags = (byte)InputFlags.MoveOrder;
                if (t % 18 == 0) input.AbilityMask = (byte)(1 << ((t / 18) % 4));
                if (t % 75 == 12) input.AbilityMask = 16;
                for (int s = 0; s < 5; s++)
                    if (input.Ability(s) && sim.Tick >= sim.AbilityReadyTick(s)
                        && sim.Entities.Lavidium[0] < sim.GetAbility(s).Get(AbilityStatType.LavidiumCost)) resourceRefusals++;
                var before = sim.Entities.Position[0];
                sim.Step(input);
                if (before.Equals(sim.Entities.Position[0]) && before.X < Fix64.FromInt(10)) blocked++;
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.AbilityCast) casts.Add("{\"tick\":" + t + ",\"slot\":" + e.Amount + "}");
            }
            var path = Environment.GetEnvironmentVariable("PELAG_TEMPO_REPORT");
            var json = "{\"ticks\":300,\"blockedMovementTicks\":" + blocked + ",\"resourceRefusals\":" + resourceRefusals
                + ",\"casts\":[" + string.Join(",", casts) + "]}";
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, json);
            TestContext.WriteLine(json);
        }
    }
}


