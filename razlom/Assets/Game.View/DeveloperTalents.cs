using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Таланты, включённые вручную из меню разработчика (F8).
    ///
    /// С разворота в роглайк (15 сентября) таланты берутся в забеге, а из
    /// палатки ушли. Владельцу нужно доводить визуал каждого, поэтому здесь
    /// любой из двадцати включается поштучно — без порядка и без очков.
    ///
    /// Живёт только в представлении и не сохраняется: это инструмент, а не
    /// прогресс. В релизной сборке меню нет, и маска всегда пустая.
    /// </summary>
    public static class DeveloperTalents
    {
        private static readonly bool[] Enabled = new bool[SabreTalents.LineCount * SabreTalents.TalentsPerLine];

        /// <summary>Растёт при каждом изменении — по нему TickDriver пересобирает билд.</summary>
        public static int Version { get; private set; }

        public static bool Has(SabreTalentLine line, int index)
            => Enabled[(int)line * SabreTalents.TalentsPerLine + index];

        public static void Set(SabreTalentLine line, int index, bool value)
        {
            int i = (int)line * SabreTalents.TalentsPerLine + index;
            if (Enabled[i] == value) return;
            Enabled[i] = value;
            Version++;
        }

        public static int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Enabled.Length; i++) if (Enabled[i]) count++;
                return count;
            }
        }

        public static void Clear()
        {
            for (int i = 0; i < Enabled.Length; i++) Enabled[i] = false;
            Version++;
        }

        /// <summary>
        /// Дописывает узлы включённых талантов направления. Первые takenRank
        /// пропускаются: они уже взяты в забеге, и второй такой же узел удвоил бы прибавку.
        /// </summary>
        public static int AppendNodes(SabreTalentLine line, int takenRank, AbilityNode[] buffer, int count)
        {
            for (int index = takenRank < 0 ? 0 : takenRank; index < SabreTalents.TalentsPerLine; index++)
                if (Has(line, index)) count = SabreTalents.AppendNode(line, index, buffer, count);
            return count;
        }
    }
}
