using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Сценарии пина «Вихрь без формы — прежний бит в бит» (механика форм Вихря, 02.10).
    ///
    /// ЗДЕСЬ ТОЛЬКО API, КОТОРЫЙ БЫЛ ДО МЕХАНИКИ ФОРМ: эти же функции собраны против
    /// снимка Game.Sim до правки, их значения прибиты в WhirlwindFormTests. Сдвинулся
    /// пин — сдвинулся обычный Вихрь (или его таланты); причину искать в правке.
    ///
    /// Без NUnit намеренно: сборка «до правки» компилирует этот файл отдельно.
    /// </summary>
    internal static class WhirlwindBaselineScenarios
    {
        private const int Ticks = 220;

        /// <summary>
        /// Вихрь с талантами линии до rank (0 — без талантов; 8 — удержание, тяга,
        /// волна, кокон, толпа, возврат): нажатие, удержание, второй каст. Свёртка
        /// StateHash и всех полей событий по тикам.
        /// </summary>
        public static ulong Fold(int rank, bool hold, bool walk)
        {
            var sim = new Simulation(77, 128);
            sim.SetupTestArena(0);
            var nodes = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = SabreTalents.AppendNodes(SabreTalentLine.Whirlwind, rank, nodes, 0);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), nodes, count);

            // Десять неподвижных тел на 1–5 м вокруг героя.
            int[,] at =
            {
                { 1000, 0 }, { 0, 1400 }, { -1800, 300 }, { 600, -2200 }, { 2600, 900 },
                { -900, -3000 }, { 3300, -1200 }, { -3700, 1600 }, { 800, 4300 }, { 4800, 0 },
            };
            for (int k = 0; k < at.GetLength(0); k++)
            {
                int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(at[k, 0], 1000), Fix64.Ratio(at[k, 1], 1000)),
                    400, Faction.Orvill);
                sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.RefreshStats(id);
                sim.Entities.BodyRadius[id] = Fix64.Ratio(3, 10);
                sim.Entities.NextAttackTick[id] = int.MaxValue;
            }

            ulong fold = 14695981039346656037UL;
            for (int t = 0; t < Ticks; t++)
            {
                var input = InputFrame.Empty;
                input.AttackTarget = -1;
                input.AbilityTarget = -1;
                bool press = t == 0 || t == 100;
                bool held = press || hold && (t < 80 || t > 100 && t < 150);
                if (press) input.AbilityMask = 1;
                if (held) input.AbilityHoldMask = 1;
                if (walk)
                {
                    input.Flags = (byte)InputFlags.MoveOrder;
                    input.Aim = t < 120 ? new FixVec2(Fix64.FromInt(6), Fix64.FromInt(2)) : new FixVec2(Fix64.FromInt(-4), Fix64.FromInt(-3));
                }
                else input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
                sim.Step(input);

                fold = Mix(fold, sim.StateHash());
                foreach (SimEvent ev in sim.Events)
                {
                    fold = Mix(fold, (ulong)ev.Type); fold = Mix(fold, (ulong)(uint)ev.Source);
                    fold = Mix(fold, (ulong)(uint)ev.Target); fold = Mix(fold, (ulong)(uint)ev.Amount);
                    fold = Mix(fold, ev.Flag ? 1UL : 0UL); fold = Mix(fold, (ulong)ev.Position.X.Raw);
                    fold = Mix(fold, (ulong)ev.Position.Y.Raw); fold = Mix(fold, (ulong)(uint)ev.ActionVariant);
                }
            }
            return fold;
        }

        /// <summary>Три прогона: без талантов с удержанием и ходьбой; вся линия так же; вся линия стоя без удержания.</summary>
        public static ulong[] Folds() => new[] { Fold(0, true, true), Fold(8, true, true), Fold(8, false, false) };

        private static ulong Mix(ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
            return h;
        }
    }
}
