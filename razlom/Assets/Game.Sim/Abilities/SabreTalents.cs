namespace Game.Sim
{
    /// <summary>
    /// Направление сабельной ветки талантов — по одному на способность.
    /// Порядок совпадает с утверждённым порядком сабельных слотов:
    /// 1 Вихрь, 2 Рассекающий удар, 3 «Ладно смазал», 4 Шквал.
    /// </summary>
    public enum SabreTalentLine : byte
    {
        Whirlwind = 0,
        Cleave = 1,
        Blaze = 2,
        Squall = 3,
    }

    /// <summary>
    /// Устройство сабельной ветки талантов. Решение владельца от 13 сентября:
    /// четыре направления, по пять талантов, открываются строго по порядку.
    ///
    /// ПОРЯДОК ХРАНИТСЯ ЧИСЛОМ. Раз брать можно только следующий талант,
    /// «сколько взято» и есть полный список взятого — отдельный набор флагов
    /// позволил бы сохранению описать дыру, которой в игре не бывает.
    ///
    /// Названия и описания живут в представлении: симуляции нужны только
    /// номера и узлы, а текст меняется без пересборки боевых данных.
    /// </summary>
    public static class SabreTalents
    {
        public const int LineCount = 4;
        public const int TalentsPerLine = 5;

        /// <summary>Слот сабельной ветки, к способности которого относится направление.</summary>
        public static int SlotOf(SabreTalentLine line) => (int)line;

        /// <summary>
        /// Дописывает в buffer узлы взятых талантов направления и возвращает
        /// новое число узлов. Взяты таланты с номерами меньше rank.
        ///
        /// Узлы кладутся ТОЛЬКО в слот своей способности: общий буфер на все
        /// слоты растянул бы радиус Вихря и на Рассекающий удар.
        ///
        /// Числовые таланты — узлы статов, механические — флаги способности.
        /// Код флагов живёт в Simulation.Talents и в самих способностях.
        /// </summary>
        public static int AppendNodes(SabreTalentLine line, int rank, AbilityNode[] buffer, int count)
        {
            if (rank > TalentsPerLine) rank = TalentsPerLine;
            for (int index = 0; index < rank; index++)
                if (TryNode(line, index, out AbilityNode node) && count < buffer.Length)
                    buffer[count++] = node;
            return count;
        }

        /// <summary>Механический талант — флаг способности. None — талант числовой.</summary>
        private static AbilityFlag FlagOf(SabreTalentLine line, int index)
        {
            switch (line)
            {
                case SabreTalentLine.Whirlwind:
                    return index == 2 ? AbilityFlag.WhirlwindCrowd
                        : index == 3 ? AbilityFlag.WhirlwindRefund
                        : index == 4 ? AbilityFlag.WhirlwindChannel : AbilityFlag.None;
                case SabreTalentLine.Cleave:
                    return index == 0 ? AbilityFlag.CleaveOnTheMove
                        : index == 2 ? AbilityFlag.CleaveBigGame
                        : index == 3 ? AbilityFlag.CleaveKillRefund
                        : index == 4 ? AbilityFlag.CleaveFan : AbilityFlag.None;
                case SabreTalentLine.Blaze:
                    return index == 0 ? AbilityFlag.BlazeTrail
                        : index == 2 ? AbilityFlag.BlazeEvadeRefund
                        : index == 3 ? AbilityFlag.BlazeIgnite
                        : index == 4 ? AbilityFlag.BlazeAbilities : AbilityFlag.None;
                default:
                    return index == 0 ? AbilityFlag.SquallKillCooldown
                        : index == 2 ? AbilityFlag.SquallInvulnerable
                        : index == 3 ? AbilityFlag.SquallFinisher
                        : index == 4 ? AbilityFlag.SquallFiveHops : AbilityFlag.None;
            }
        }

        private static readonly string[] LineKeys = { "whirlwind", "cleave", "blaze", "squall" };

        private static bool TryNode(SabreTalentLine line, int index, out AbilityNode node)
        {
            AbilityFlag flag = FlagOf(line, index);
            if (flag != AbilityFlag.None)
            {
                node = AbilityNode.Flag("talent.sabre." + LineKeys[(int)line] + "." + (index + 1), flag);
                return true;
            }

            switch (line)
            {
                case SabreTalentLine.Whirlwind:
                    // «Шире круг»: радиус +25%.
                    if (index == 0)
                    {
                        node = AbilityNode.StatMod("talent.sabre.whirlwind.1", AbilityStatType.Radius,
                            ModifierOp.Increased, Fix64.Ratio(1, 4));
                        return true;
                    }
                    // «Чаще»: перезарядка −25%.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.whirlwind.2", AbilityStatType.CooldownTicks,
                            ModifierOp.Increased, Fix64.Ratio(-1, 4));
                        return true;
                    }
                    break;

                case SabreTalentLine.Cleave:
                    // «Длинный клинок»: дальность +50%. Radius у Рассекающего —
                    // длина клинка, по которой ищется тело на контакте.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.cleave.2", AbilityStatType.Radius,
                            ModifierOp.Increased, Fix64.Ratio(1, 2));
                        return true;
                    }
                    break;

                case SabreTalentLine.Blaze:
                    // «Дольше горит»: 3 → 5 секунд. Прибавка плоская, а не
                    // процентная: владелец назвал именно секунды.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.blaze.2", AbilityStatType.DurationTicks,
                            ModifierOp.Flat, Fix64.FromInt(2 * Simulation.TicksPerSecond));
                        return true;
                    }
                    break;

                case SabreTalentLine.Squall:
                    // «Дешевле»: 40 → 28 ед. лавидия.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.squall.2", AbilityStatType.LavidiumCost,
                            ModifierOp.Flat, Fix64.FromInt(-12));
                        return true;
                    }
                    break;
            }

            node = default;
            return false;
        }
    }
}
