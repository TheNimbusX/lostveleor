namespace Game.Sim
{
    /// <summary>
    /// Направление талантов — по одному на способность пула Пелага. Номер
    /// совпадает с индексом способности в пуле: 0–3 бывшая сабля, 4–7 бывший
    /// якорь (таланты якоря утверждены владельцем 15 сентября).
    ///
    /// Имя «Sabre» историческое: переименование тронуло бы десяток файлов и
    /// meta-файлы Unity, а смысл перечисления уже шире сабли.
    /// </summary>
    public enum SabreTalentLine : byte
    {
        Whirlwind = 0,
        Cleave = 1,
        Blaze = 2,
        Squall = 3,
        AnchorSlam = 4,
        Wreck = 5,
        Boarding = 6,
        Flask = 7,
    }

    /// <summary>
    /// Устройство усилений способностей. Было: четыре направления по пять талантов строго
    /// по порядку (13 сентября). Сейчас (24 сентября): у каждой способности 8 усилений,
    /// берутся в любом порядке — взятые хранятся маской в RunLoadout. Усиления 6–8 — выбор
    /// владельца той же ночью.
    ///
    /// Названия и описания живут в представлении: симуляции нужны только
    /// номера и узлы, а текст меняется без пересборки боевых данных.
    /// </summary>
    public static class SabreTalents
    {
        public const int LineCount = 8;
        public const int TalentsPerLine = 8;

        /// <summary>Номер «Удержания» в линии Вихря — съёмка удержания включает именно его.</summary>
        public const int WhirlwindChannelIndex = 4;

        /// <summary>Индекс способности направления в пуле Пелага.</summary>
        public static int PoolIndexOf(SabreTalentLine line) => (int)line;

        /// <summary>
        /// Направление талантов способности пула. False — у способности
        /// талантов пока нет: так у бывшей якорной четвёрки до их дизайна.
        /// </summary>
        public static bool TryLineOf(int poolIndex, out SabreTalentLine line)
        {
            line = (SabreTalentLine)((uint)poolIndex < LineCount ? poolIndex : 0);
            return (uint)poolIndex < LineCount;
        }

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

        /// <summary>
        /// Дописывает узел ОДНОГО таланта, без предыдущих, и возвращает новое
        /// число узлов. Порядок — правило взятия, а не узлов: меню разработчика
        /// включает таланты поштучно, чтобы доводить визуал каждого отдельно.
        /// </summary>
        public static int AppendNode(SabreTalentLine line, int index, AbilityNode[] buffer, int count)
        {
            if ((uint)line < LineCount && (uint)index < TalentsPerLine
                && TryNode(line, index, out AbilityNode node) && count < buffer.Length)
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
                        : index == 4 ? AbilityFlag.WhirlwindChannel
                        : index == 5 ? AbilityFlag.WhirlwindPull
                        : index == 6 ? AbilityFlag.WhirlwindWave
                        : index == 7 ? AbilityFlag.WhirlwindCocoon : AbilityFlag.None;
                case SabreTalentLine.Cleave:
                    return index == 0 ? AbilityFlag.CleaveOnTheMove
                        : index == 2 ? AbilityFlag.CleaveBigGame
                        : index == 3 ? AbilityFlag.CleaveKillRefund
                        : index == 4 ? AbilityFlag.CleaveFan
                        : index == 5 ? AbilityFlag.CleaveSunder
                        : index == 6 ? AbilityFlag.CleaveDouble
                        : index == 7 ? AbilityFlag.CleaveWave : AbilityFlag.None;
                case SabreTalentLine.Blaze:
                    return index == 0 ? AbilityFlag.BlazeTrail
                        : index == 2 ? AbilityFlag.BlazeEvadeRefund
                        : index == 3 ? AbilityFlag.BlazeIgnite
                        : index == 4 ? AbilityFlag.BlazeAbilities
                        : index == 5 ? AbilityFlag.BlazeFlare
                        : index == 6 ? AbilityFlag.BlazeHaste
                        : index == 7 ? AbilityFlag.BlazeStoke : AbilityFlag.None;
                case SabreTalentLine.Squall:
                    return index == 0 ? AbilityFlag.SquallKillCooldown
                        : index == 2 ? AbilityFlag.SquallInvulnerable
                        : index == 3 ? AbilityFlag.SquallFinisher
                        : index == 4 ? AbilityFlag.SquallFiveHops
                        : index == 5 ? AbilityFlag.SquallReturn
                        : index == 6 ? AbilityFlag.SquallRepeat
                        : index == 7 ? AbilityFlag.SquallOpener : AbilityFlag.None;
                case SabreTalentLine.AnchorSlam:
                    return index == 3 ? AbilityFlag.AnchorSlamStunnedBonus
                        : index == 4 ? AbilityFlag.AnchorSlamThreeWays
                        : index == 5 ? AbilityFlag.AnchorSlamCrack
                        : index == 7 ? AbilityFlag.AnchorSlamRecoil : AbilityFlag.None;
                case SabreTalentLine.Wreck:
                    return index == 2 ? AbilityFlag.WreckBigGame
                        : index == 3 ? AbilityFlag.WreckRefund
                        : index == 4 ? AbilityFlag.WreckFourthStrike
                        : index == 5 ? AbilityFlag.WreckUnstoppable
                        : index == 6 ? AbilityFlag.WreckConcuss
                        : index == 7 ? AbilityFlag.WreckMomentum : AbilityFlag.None;
                case SabreTalentLine.Boarding:
                    return index == 1 ? AbilityFlag.BoardingStun
                        : index == 2 ? AbilityFlag.BoardingTwoCharges
                        : index == 3 ? AbilityFlag.BoardingHilt
                        : index == 4 ? AbilityFlag.BoardingSweep
                        : index == 5 ? AbilityFlag.BoardingMomentum
                        : index == 6 ? AbilityFlag.BoardingInterrupt
                        : index == 7 ? AbilityFlag.BoardingSureCrit : AbilityFlag.None;
                case SabreTalentLine.Flask:
                    return index == 2 ? AbilityFlag.FlaskFuel
                        : index == 3 ? AbilityFlag.FlaskOil
                        : index == 4 ? AbilityFlag.FlaskRing
                        : index == 5 ? AbilityFlag.FlaskTwoCharges
                        : index == 7 ? AbilityFlag.FlaskShrapnel : AbilityFlag.None;
                default:
                    return AbilityFlag.None;
            }
        }

        private static readonly string[] LineKeys =
            { "whirlwind", "cleave", "blaze", "squall", "anchor_slam", "wreck", "boarding", "flask" };

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

                case SabreTalentLine.AnchorSlam:
                    // «Быстрый замах»: замах короче на 40%.
                    if (index == 0)
                    {
                        node = AbilityNode.StatMod("talent.sabre.anchor_slam.1", AbilityStatType.WindupTicks,
                            ModifierOp.Increased, Fix64.Ratio(-2, 5));
                        return true;
                    }
                    // «Долгий стан»: 0,5 → 1 с.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.anchor_slam.2", AbilityStatType.StunTicks,
                            ModifierOp.Flat, Fix64.FromInt(Simulation.TicksPerSecond / 2));
                        return true;
                    }
                    // «Широкая полоса»: 1,2 → 1,8 м.
                    if (index == 2)
                    {
                        node = AbilityNode.StatMod("talent.sabre.anchor_slam.3", AbilityStatType.Width,
                            ModifierOp.Increased, Fix64.Ratio(1, 2));
                        return true;
                    }
                    // «Дальний удар»: полоса 4,5 → 6 м.
                    if (index == 6)
                    {
                        node = AbilityNode.StatMod("talent.sabre.anchor_slam.7", AbilityStatType.Radius,
                            ModifierOp.Increased, Fix64.Ratio(1, 3));
                        return true;
                    }
                    break;

                case SabreTalentLine.Wreck:
                    // «Шире размах»: радиус 2,8 → 3,4 м.
                    if (index == 0)
                    {
                        node = AbilityNode.StatMod("talent.sabre.wreck.1", AbilityStatType.Radius,
                            ModifierOp.Flat, Fix64.Ratio(3, 5));
                        return true;
                    }
                    // «Долгое окно»: окно следующего нажатия 0,8 → 1,2 с.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.wreck.2", AbilityStatType.ComboWindowTicks,
                            ModifierOp.Flat, Fix64.FromInt(12));
                        return true;
                    }
                    break;

                case SabreTalentLine.Boarding:
                    // «Длинная цепь»: 7 → 9 м.
                    if (index == 0)
                    {
                        node = AbilityNode.StatMod("talent.sabre.boarding.1", AbilityStatType.Radius,
                            ModifierOp.Flat, Fix64.FromInt(2));
                        return true;
                    }
                    break;

                case SabreTalentLine.Flask:
                    // «Большая лужа»: диаметр 2,5 → 3,5 м.
                    if (index == 0)
                    {
                        node = AbilityNode.StatMod("talent.sabre.flask.1", AbilityStatType.Width,
                            ModifierOp.Increased, Fix64.Ratio(2, 5));
                        return true;
                    }
                    // «Дольше горит»: лужа 5 → 8 с.
                    if (index == 1)
                    {
                        node = AbilityNode.StatMod("talent.sabre.flask.2", AbilityStatType.DurationTicks,
                            ModifierOp.Flat, Fix64.FromInt(3 * Simulation.TicksPerSecond));
                        return true;
                    }
                    // «Дальний бросок»: 7 → 10 м.
                    if (index == 6)
                    {
                        node = AbilityNode.StatMod("talent.sabre.flask.7", AbilityStatType.Radius,
                            ModifierOp.Flat, Fix64.FromInt(3));
                        return true;
                    }
                    break;
            }

            node = default;
            return false;
        }
    }
}
