using System.Text;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Хук стенда баланса арен (ArenaBalanceBench): чем жил бой бота каждый
    /// тик. Нужен замерам сверх таблицы стенда — метрикам «ощущения» боя
    /// (сколько времени герою надо уходить, сколько замахов разом, сколько
    /// мобов без дела) — без правок самого стенда.
    ///
    /// Подключение: ARENA_BENCH_PROBES=ИмяКласса (через запятую, класс с
    /// конструктором без параметров) к обычному прогону стенда, или свой
    /// тест зовёт ArenaBalanceBench.PlaySeed(уровень, сид, бессмертный, пробы).
    ///
    /// Проба только читает: менять Simulation, сессию или бота из неё нельзя —
    /// иначе цифры стенда разъедутся с цифрами без пробы.
    /// </summary>
    public interface IArenaProbe
    {
        /// <summary>
        /// Новая арена: первый тик фазы Clearing на новой глубине, до её
        /// первого шага. Волны ещё не все на поле — поздние встают по ходу боя.
        /// </summary>
        void ArenaStarted(Simulation sim, in ArenaProbeContext context);

        /// <summary>
        /// После каждого шага боя: фаза до шага — Clearing или SeekingExit
        /// (context.PhaseBefore; уход к выходу — тоже сюда, фильтруйте сами).
        /// sim.Events — события этого тика, context.Input — что нажал бот.
        /// Зовётся до долива здоровья бессмертного прохода.
        /// </summary>
        void Tick(Simulation sim, in ArenaProbeContext context);

        /// <summary>
        /// Бой арены кончился: Clearing сменилась (зачищено, смерть, уход по
        /// таймеру) или забег оборвался. Ровно один раз на каждый ArenaStarted;
        /// context.Outcome — чем.
        /// </summary>
        void ArenaEnded(Simulation sim, in ArenaProbeContext context);

        /// <summary>Итог пробы — строки в отчёт стенда после его таблиц.</summary>
        void AppendReport(StringBuilder report);
    }

    /// <summary>Чем кончился бой арены для проб. Только дописываем в конец.</summary>
    public enum ArenaProbeOutcome : byte
    {
        /// <summary>Бой идёт (ArenaStarted и Tick).</summary>
        Running = 0,
        Cleared = 1,
        Died = 2,

        /// <summary>Бот застрял дольше предохранителя стенда и ушёл с арены.</summary>
        TimedOut = 3,

        /// <summary>Забег оборвался иначе (предохранитель цикла, конец сессии).</summary>
        Left = 4,
    }

    /// <summary>
    /// Что стенд знает о забеге и арене в момент вызова пробы. Session, Run
    /// и Bot — живые объекты прогона: читать можно, менять нельзя.
    /// </summary>
    public readonly struct ArenaProbeContext
    {
        public readonly GameSession Session;
        public readonly RiftRun Run;
        public readonly ArenaBot Bot;

        /// <summary>Проход «бессмертный» (здоровье доливается каждый тик), иначе «перенос».</summary>
        public readonly bool Immortal;
        public readonly int CampLevel;
        public readonly ulong Seed;

        /// <summary>Номер арены 1…9 (девятая — босс), как RiftRun.Depth.</summary>
        public readonly int Arena;

        /// <summary>Шаблон встречи без «forest.» (E01…E15, E08T) или «Boss».</summary>
        public readonly string Template;
        public readonly bool Boss;

        /// <summary>Фаза забега до этого шага.</summary>
        public readonly RunPhase PhaseBefore;

        /// <summary>Тиков боя (Clearing) на арене, включая этот шаг.</summary>
        public readonly int ClearTicks;

        /// <summary>Тиков ухода к выходу (SeekingExit) на арене, включая этот шаг.</summary>
        public readonly int ExitTicks;

        /// <summary>Ввод бота на этом шаге (у ArenaStarted и ArenaEnded — пустой).</summary>
        public readonly InputFrame Input;
        public readonly ArenaProbeOutcome Outcome;

        public ArenaProbeContext(GameSession session, RiftRun run, ArenaBot bot, bool immortal, int campLevel,
            ulong seed, int arena, string template, bool boss, RunPhase phaseBefore, int clearTicks, int exitTicks,
            InputFrame input, ArenaProbeOutcome outcome)
        {
            Session = session;
            Run = run;
            Bot = bot;
            Immortal = immortal;
            CampLevel = campLevel;
            Seed = seed;
            Arena = arena;
            Template = template;
            Boss = boss;
            PhaseBefore = phaseBefore;
            ClearTicks = clearTicks;
            ExitTicks = exitTicks;
            Input = input;
            Outcome = outcome;
        }
    }
}
