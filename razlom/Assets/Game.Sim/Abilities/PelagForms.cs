namespace Game.Sim
{
    /// <summary>
    /// Форма навыка Пелага (владелец 01–02.10, «как аспекты в Hades»). Навык
    /// приходит без формы; позже особая награда даёт выбрать одну форму на
    /// забег, сменить нельзя. Навык убран из слота — форма и её таланты пропали.
    ///
    /// ЗНАЧЕНИЯ НАВСЕГДА, ТОЛЬКО ДОПИСЫВАТЬ: номер формы лежит в наборе забега,
    /// в карточке награды (RewardOffer.TalentIndex) и в хеше сборки. Номер
    /// убранной формы не переиспользуется — как бит «Печати» и RewardKind.AbilityNode.
    ///
    /// Пока здесь только Вихрь: владелец 02.10 — «давай вихрь», формы Шквала и
    /// Абордажа ещё не утверждены. Их номера — следующие свободные, когда дойдём.
    /// </summary>
    public enum PelagForm : byte
    {
        None = 0,

        /// <summary>Вихрь · Буря: удержание до 3 с.</summary>
        WhirlwindStorm = 1,

        /// <summary>Вихрь · Водоворот: стягивает с 4 м и сбивает.</summary>
        WhirlwindMaelstrom = 2,

        /// <summary>Вихрь · Пенные волны: два кольца до 5 м.</summary>
        WhirlwindFoamWaves = 3,

        /// <summary>Вихрь · Вихрь на ходу: без штрафа скорости.</summary>
        WhirlwindOnTheMove = 4,
    }

    /// <summary>
    /// Второе слово флагов способности: механика форм, талантов форм, позже
    /// связок и «подсветки». AbilityFlag заморожен для прежних линий (свободные
    /// биты оставлены переделке талантов). Только дописывать; бит не переиспользовать.
    /// Узлы типа NodeKind.Trait включают биты, сборка хеширует их, только если не пусто.
    /// </summary>
    [System.Flags]
    public enum AbilityTrait : ulong
    {
        None = 0,

        // Биты придут с механикой форм — первым Вихрь.
    }

    /// <summary>
    /// Таблица форм: какой линии принадлежит, готова ли (есть ли механика), какие
    /// узлы кладёт. Симуляции нужны только номера и узлы; названия и описания —
    /// в представлении, как у талантов.
    ///
    /// «Линия» — индекс пула (PelagKit) или PelagKit.SabreLine.
    /// Предлагается игроку только ГОТОВАЯ форма; номера утверждённых, но ещё не
    /// написанных форм зарезервированы и видны меню разработчика.
    /// </summary>
    public static class PelagForms
    {
        /// <summary>Номер карточки таланта формы: FormTalentBase + k. Таланты линии — 0–7.</summary>
        public const int FormTalentBase = 16;

        /// <summary>Потолок талантов одной формы — маска в RunLoadout.</summary>
        public const int MaxFormTalents = 16;

        /// <summary>ЗАГЛУШКА (вопрос 6 плана): сколько талантов у формы, пока владелец не назвал.</summary>
        public const int PlaceholderTalentsPerForm = 3;

        /// <summary>Вихрь в пуле Пелага (PelagKit.PoolDefinition(0)).</summary>
        private const int WhirlwindLine = 0;

        // Таблицы по номеру PelagForm. Дописываются вместе с enum.
        private static readonly int[] Lines = { -1, WhirlwindLine, WhirlwindLine, WhirlwindLine, WhirlwindLine };

        /// <summary>Готова — механика написана и принята. Пока ни одной: шаг «система форм».</summary>
        private static readonly bool[] Ready = { false, false, false, false, false };

        private static readonly string[] Keys =
            { null, "form.whirlwind.storm", "form.whirlwind.maelstrom", "form.whirlwind.foam_waves", "form.whirlwind.on_the_move" };

        /// <summary>Сколько номеров форм занято (без None).</summary>
        public static int Count => Lines.Length - 1;

        /// <summary>Номер формы занят таблицей (None — нет).</summary>
        public static bool IsValid(PelagForm form) => form != PelagForm.None && (int)form < Lines.Length;

        /// <summary>Линия формы или −1.</summary>
        public static int LineOf(PelagForm form) => IsValid(form) ? Lines[(int)form] : -1;

        /// <summary>Механика формы написана — её можно предлагать в обычном забеге.</summary>
        public static bool IsReady(PelagForm form) => IsValid(form) && Ready[(int)form];

        /// <summary>Стабильный ключ узла формы.</summary>
        public static string KeyOf(PelagForm form) => IsValid(form) ? Keys[(int)form] : null;

        /// <summary>Сколько форм у линии; readyOnly — только готовых. Порядок — по номеру формы.</summary>
        public static int FormCount(int line, bool readyOnly)
        {
            int count = 0;
            for (int f = 1; f < Lines.Length; f++)
                if (Lines[f] == line && (!readyOnly || Ready[f])) count++;
            return count;
        }

        /// <summary>i-я (с нуля) форма линии в порядке номеров или None.</summary>
        public static PelagForm FormAt(int line, int i, bool readyOnly)
        {
            for (int f = 1; f < Lines.Length; f++)
                if (Lines[f] == line && (!readyOnly || Ready[f]) && i-- == 0) return (PelagForm)f;
            return PelagForm.None;
        }

        public static int ReadyFormCount(int line) => FormCount(line, true);
        public static PelagForm ReadyFormAt(int line, int i) => FormAt(line, i, true);

        /// <summary>
        /// Узлы формы: узел NodeKind.Form (ставит AbilityBuild.Form), затем её числа
        /// (StatMod) и поведение (Trait) — они придут с механикой каждой формы.
        /// </summary>
        public static int AppendFormNodes(PelagForm form, AbilityNode[] buffer, int count)
        {
            if (!IsValid(form) || count >= buffer.Length) return count;
            buffer[count++] = AbilityNode.Form(Keys[(int)form], form);
            return count;
        }

        /// <summary>Сколько талантов у формы. Пока заглушка на каждую занятую форму.</summary>
        public static int FormTalentCount(PelagForm form) => IsValid(form) ? PlaceholderTalentsPerForm : 0;

        /// <summary>Ключ узла таланта формы: «form.whirlwind.storm.t1» и т. д.</summary>
        public static string TalentKeyOf(PelagForm form, int index)
            => IsValid(form) && (uint)index < (uint)FormTalentCount(form) ? Keys[(int)form] + ".t" + (index + 1) : null;

        /// <summary>
        /// Узел одного таланта формы. Пока ЗАГЛУШКА без действия (Trait без битов):
        /// узел доходит до сборки и виден тестам, числа и механика придут с формой.
        /// </summary>
        public static int AppendFormTalentNode(PelagForm form, int index, AbilityNode[] buffer, int count)
        {
            string key = TalentKeyOf(form, index);
            if (key == null || count >= buffer.Length) return count;
            buffer[count++] = AbilityNode.Trait(key, AbilityTrait.None);
            return count;
        }
    }
}
