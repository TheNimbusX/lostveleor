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
    /// Вихрь (1–3, 4 убрана) и Шквал (5–7, утверждены 02.10). Формы Абордажа и
    /// прочих ещё не утверждены — их номера следующие свободные, когда дойдём.
    /// </summary>
    public enum PelagForm : byte
    {
        None = 0,

        /// <summary>Вихрь · Буря: удержание до 3 с (механика — Simulation.WhirlwindForms).</summary>
        WhirlwindStorm = 1,

        /// <summary>Вихрь · Водоворот: стягивает с 4 м и сбивает.</summary>
        WhirlwindMaelstrom = 2,

        /// <summary>Вихрь · Пенные волны: два кольца до 5 м.</summary>
        WhirlwindFoamWaves = 3,

        /// <summary>
        /// УБРАНА 02.10 — владелец: «на ходу убираем» (обычный Вихрь и так крутится
        /// на ходу; ходьбу в Буре даст её талант). Номер 4 занят навсегда и не
        /// переиспользуется: в таблице без линии, PelagForms.IsValid = false, ни
        /// экран, ни F8 её не дают. Имя оставлено, пока на него ссылается
        /// представление (тексты и иконка формы).
        /// </summary>
        WhirlwindOnTheMove = 4,

        /// <summary>Шквал · Охота: прыжки к самому раненому, убийство — лишний прыжок (Simulation.Squall).</summary>
        SquallHunt = 5,

        /// <summary>Шквал · Пенный след: прыжок оставляет полосу пены — бьёт и замедляет.</summary>
        SquallFoamTrail = 6,

        /// <summary>Шквал · Неуловимый: неуязвим в прыжках, последний прыжок — дугой к точке каста.</summary>
        SquallElusive = 7,
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
    /// написанных форм зарезервированы и видны меню разработчика. УБРАННАЯ форма
    /// (линия RetiredLine) держит свой номер, но не существует: IsValid = false.
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

        /// <summary>Линия убранной формы: номер занят навсегда, формы нет.</summary>
        private const int RetiredLine = -2;

        /// <summary>Шквал в пуле Пелага (PelagKit.PoolDefinition(3)).</summary>
        private const int SquallLine = 3;

        // Таблицы по номеру PelagForm. Дописываются вместе с enum.
        // 4 — «Вихрь на ходу», убрана 02.10 («на ходу убираем»).
        private static readonly int[] Lines =
            { -1, WhirlwindLine, WhirlwindLine, WhirlwindLine, RetiredLine, SquallLine, SquallLine, SquallLine };

        /// <summary>
        /// Готова — механика написана: Вихрь (Simulation.WhirlwindForms, 02.10) — Буря,
        /// Водоворот, Пенные волны; Шквал (Simulation.Squall, 02.10) — Охота, Пенный
        /// след, Неуловимый. Обычный забег предлагает форму только с включателем
        /// FormRewardRules.UseSkillForms (пока выключен).
        /// </summary>
        private static readonly bool[] Ready = { false, true, true, true, false, true, true, true };

        // Ключ «form.whirlwind.on_the_move» принадлежал убранной форме — не занимать.
        private static readonly string[] Keys =
        {
            null, "form.whirlwind.storm", "form.whirlwind.maelstrom", "form.whirlwind.foam_waves", null,
            "form.squall.hunt", "form.squall.foam_trail", "form.squall.elusive",
        };

        /// <summary>
        /// Буря: доля скорости шага, пока Вихрь держат (стат способности
        /// StartMoveMultiplier, узел формы). ЗАГЛУШКА 45%; талант Бури поднимет её
        /// тем же статом до полной («талант такой накинем», владелец 02.10).
        /// </summary>
        public static readonly Fix64 StormMoveMultiplier = Fix64.Ratio(45, 100);

        /// <summary>Ключ узла чисел Бури (скорость шага в удержании).</summary>
        public const string StormMoveKey = "form.whirlwind.storm.move";

        /// <summary>Сколько номеров форм занято (без None), убранные тоже — номер не освобождается.</summary>
        public static int Count => Lines.Length - 1;

        /// <summary>Форма есть в таблице: номер занят и не убран (None — нет).</summary>
        public static bool IsValid(PelagForm form) => (int)form < Lines.Length && Lines[(int)form] >= 0;

        /// <summary>Номер принадлежал форме, которую убрали: не переиспользовать.</summary>
        public static bool IsRetired(PelagForm form) => (int)form < Lines.Length && Lines[(int)form] == RetiredLine;

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
                if (Lines[f] >= 0 && Lines[f] == line && (!readyOnly || Ready[f])) count++;
            return count;
        }

        /// <summary>i-я (с нуля) форма линии в порядке номеров или None.</summary>
        public static PelagForm FormAt(int line, int i, bool readyOnly)
        {
            for (int f = 1; f < Lines.Length; f++)
                if (Lines[f] >= 0 && Lines[f] == line && (!readyOnly || Ready[f]) && i-- == 0) return (PelagForm)f;
            return PelagForm.None;
        }

        public static int ReadyFormCount(int line) => FormCount(line, true);
        public static PelagForm ReadyFormAt(int line, int i) => FormAt(line, i, true);

        /// <summary>
        /// Узлы формы: узел NodeKind.Form (ставит AbilityBuild.Form), затем её числа
        /// (StatMod). Поведение форм Вихря спрашивает саму форму (Simulation.FormIs);
        /// числа узлом — только то, что таланты будут поднимать: шаг в Буре.
        /// </summary>
        public static int AppendFormNodes(PelagForm form, AbilityNode[] buffer, int count)
        {
            if (!IsValid(form) || count >= buffer.Length) return count;
            buffer[count++] = AbilityNode.Form(Keys[(int)form], form);
            if (form == PelagForm.WhirlwindStorm && count < buffer.Length)
                buffer[count++] = AbilityNode.StatMod(StormMoveKey, AbilityStatType.StartMoveMultiplier,
                    ModifierOp.Flat, StormMoveMultiplier);
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
