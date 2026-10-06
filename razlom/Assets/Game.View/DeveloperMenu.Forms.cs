#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Формы навыков в меню разработчика (план форм 02.10): экран «Выбери форму» прямо сейчас — из боя или по пути к
    /// выходу. Пункты регистрируются через <see cref="DevMenu"/> из этого файла, без правки оболочки F8.
    ///
    /// Включатель форм для обычных забегов не трогается (FormRewardRules.UseSkillForms = false): этот забег помечается
    /// тестовым, лимиты и шанс экрана не действуют, формы без механики считаются готовыми. Выбор ставит форму сразу,
    /// бой продолжается — поэтому «Показать» закрывает меню: иначе экран формы не получил бы ввод.
    /// </summary>
    internal static class FormsDevEntries
    {
        private const string Forms = "Формы";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            DevMenu.Section(DevTab.Pelag, Forms, 20, DevColumn.Left);

            DevMenu.Button("pelag.forms.preview", DevTab.Pelag, Forms, "Показать выбор формы", Open,
                DevFlags.CloseMenu | DevFlags.MarksTestRun,
                blocked: c => !c.InRift ? "Только в разломе."
                    : c.Run.Phase != RunPhase.Clearing && c.Run.Phase != RunPhase.SeekingExit ? "Только в бою на арене или по пути к выходу." : null,
                visible: c => !(c.InRift && c.Run.FormPreviewOpen),
                hint: "Нужен навык с формами без формы: Вихрь, Шквал, Абордаж, Бросок якоря или Крушение («Набор съёмки» — Вихрь и Шквал). " +
                      "Выбор ставит форму сразу, бой продолжается.",
                order: 10);

            DevMenu.Button("pelag.forms.close", DevTab.Pelag, Forms, "Закрыть выбор без формы",
                c =>
                {
                    if (!c.InRift) throw new ArgumentException("Выбор формы — только в забеге.");
                    c.Run.DebugCloseFormScreen();
                },
                visible: c => c.InRift && c.Run.FormPreviewOpen,
                order: 20);

            // Формы Вихря сразу, без экрана выбора (вид форм 02.10): поставить, снять, сравнить.
            DevMenu.Custom("pelag.forms.whirlwind", DevTab.Pelag, Forms, DrawWhirlwindForm, order: 30);

            // Формы Шквала сразу, без экрана выбора (шаг 2 Шквала 02.10): Охота, Пенный след, Неуловимый.
            DevMenu.Custom("pelag.forms.squall", DevTab.Pelag, Forms, DrawSquallForm, order: 40);

            // Формы Абордажа сразу, без экрана выбора (Абордаж v2 02.10): Обвал, Гейзер, Пробоина.
            DevMenu.Custom("pelag.forms.abordage", DevTab.Pelag, Forms, DrawAbordageForm, order: 50);

            // Формы Крушения сразу, без экрана выбора (Крушение v2 03.10): Волнорез, Девятый вал, Водяной панцирь.
            DevMenu.Custom("pelag.forms.wreck", DevTab.Pelag, Forms, DrawWreckForm, order: 60);
        }

        private static readonly PelagForm[] WhirlwindForms =
            { PelagForm.WhirlwindStorm, PelagForm.WhirlwindMaelstrom, PelagForm.WhirlwindFoamWaves, PelagForm.None };
        private static readonly string[] WhirlwindFormOptions = { "Буря", "Водоворот", "Пенные волны", "без формы" };

        /// <summary>
        /// «Вихрь: форма» — 2×2: Буря, Водоворот, Пенные волны, без формы. Ставит форму набору сразу
        /// (RunLoadout.DebugSetForm, в обход запрета смены); в разломе забег становится тестовым.
        /// </summary>
        private static void DrawWhirlwindForm(DevContext context, DevUi ui)
        {
            const string id = "pelag.forms.whirlwind";
            var loadout = context.Session != null ? context.Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.WhirlwindStorm);
            bool owned = loadout != null && loadout.Owns(line);
            ui.Text("Вихрь: форма");
            int shown = owned ? Array.IndexOf(WhirlwindForms, loadout.FormOf(line)) : -1;
            int chosen = ui.Grid(shown, WhirlwindFormOptions, perRow: 2, enabled: owned);
            if (owned && chosen != shown && chosen >= 0)
            {
                PelagForm form = WhirlwindForms[chosen];
                ui.Defer(c => SetWhirlwindForm(c, line, form), id);
            }
            ui.Consequences(DevFlags.MarksTestRun, context);
            ui.Hint(owned
                ? "Ставит форму сразу, без экрана выбора; таланты формы при смене пропадают."
                : "Нужен Вихрь в наборе — «Стартовый набор: только Вихрь» выше.");
            ui.Error(id);
        }

        private static void SetWhirlwindForm(DevContext context, int line, PelagForm form)
        {
            var session = context.Session;
            if (session == null || session.ActiveLoadout == null) throw new ArgumentException("Нет набора: меню открыто вне игры.");
            if (!session.ActiveLoadout.DebugSetForm(line, form))
                throw new ArgumentException("Форма не встала: Вихря нет в наборе.");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            if (session.Mode == GameMode.Rift) session.MarkDeveloperRun();
            context.Driver.RefreshAbilityBuild();
        }

        private static readonly PelagForm[] SquallForms =
            { PelagForm.SquallHunt, PelagForm.SquallFoamTrail, PelagForm.SquallElusive, PelagForm.None };
        private static readonly string[] SquallFormOptions = { "Охота", "Пенный след", "Неуловимый", "без формы" };

        /// <summary>
        /// «Шквал: форма» — 2×2: Охота, Пенный след, Неуловимый, без формы. Как «Вихрь: форма»: ставит форму
        /// набору сразу (RunLoadout.DebugSetForm, в обход запрета смены); в разломе забег становится тестовым.
        /// </summary>
        private static void DrawSquallForm(DevContext context, DevUi ui)
        {
            const string id = "pelag.forms.squall";
            var loadout = context.Session != null ? context.Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.SquallHunt);
            bool owned = loadout != null && loadout.Owns(line);
            ui.Text("Шквал: форма");
            int shown = owned ? Array.IndexOf(SquallForms, loadout.FormOf(line)) : -1;
            int chosen = ui.Grid(shown, SquallFormOptions, perRow: 2, enabled: owned);
            if (owned && chosen != shown && chosen >= 0)
            {
                PelagForm form = SquallForms[chosen];
                ui.Defer(c => SetSquallForm(c, line, form), id);
            }
            ui.Consequences(DevFlags.MarksTestRun, context);
            ui.Hint(owned
                ? "Ставит форму сразу, без экрана выбора; таланты формы при смене пропадают."
                : "Нужен Шквал в наборе — «Набор съёмки» выше (слот 4) или стрелки слота.");
            ui.Error(id);
        }

        private static void SetSquallForm(DevContext context, int line, PelagForm form)
        {
            var session = context.Session;
            if (session == null || session.ActiveLoadout == null) throw new ArgumentException("Нет набора: меню открыто вне игры.");
            if (!session.ActiveLoadout.DebugSetForm(line, form))
                throw new ArgumentException("Форма не встала: Шквала нет в наборе.");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            if (session.Mode == GameMode.Rift) session.MarkDeveloperRun();
            context.Driver.RefreshAbilityBuild();
        }

        private static readonly PelagForm[] AbordageForms =
            { PelagForm.AbordageQuake, PelagForm.AbordageGeyser, PelagForm.AbordageBreach, PelagForm.None };
        private static readonly string[] AbordageFormOptions = { "Обвал", "Гейзер", "Пробоина", "без формы" };

        /// <summary>
        /// «Абордаж: форма» — 2×2: Обвал, Гейзер, Пробоина, без формы. Как «Вихрь: форма»: ставит форму набору
        /// сразу (RunLoadout.DebugSetForm, в обход запрета смены); в разломе забег становится тестовым.
        /// </summary>
        private static void DrawAbordageForm(DevContext context, DevUi ui)
        {
            const string id = "pelag.forms.abordage";
            var loadout = context.Session != null ? context.Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.AbordageQuake);
            bool owned = loadout != null && loadout.Owns(line);
            ui.Text("Абордаж: форма");
            int shown = owned ? Array.IndexOf(AbordageForms, loadout.FormOf(line)) : -1;
            int chosen = ui.Grid(shown, AbordageFormOptions, perRow: 2, enabled: owned);
            if (owned && chosen != shown && chosen >= 0)
            {
                PelagForm form = AbordageForms[chosen];
                ui.Defer(c => SetAbordageForm(c, line, form), id);
            }
            ui.Consequences(DevFlags.MarksTestRun, context);
            ui.Hint(owned
                ? "Ставит форму сразу, без экрана выбора; таланты формы при смене пропадают."
                : "Нужен Абордаж в наборе — стрелки слота выше («Набор съёмки» его не берёт).");
            ui.Error(id);
        }

        private static void SetAbordageForm(DevContext context, int line, PelagForm form)
        {
            var session = context.Session;
            if (session == null || session.ActiveLoadout == null) throw new ArgumentException("Нет набора: меню открыто вне игры.");
            if (!session.ActiveLoadout.DebugSetForm(line, form))
                throw new ArgumentException("Форма не встала: Абордажа нет в наборе.");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            if (session.Mode == GameMode.Rift) session.MarkDeveloperRun();
            context.Driver.RefreshAbilityBuild();
        }

        private static readonly PelagForm[] WreckForms =
            { PelagForm.WreckBreakwater, PelagForm.WreckNinthWave, PelagForm.WreckGhostAnchor, PelagForm.None };
        private static readonly string[] WreckFormOptions = { "Волнорез", "Девятый вал", "Призрачный якорь", "без формы" };

        /// <summary>
        /// «Крушение: форма» — 2×2: Волнорез, Девятый вал, Призрачный якорь, без формы (база). Как «Вихрь: форма»:
        /// ставит форму набору сразу (RunLoadout.DebugSetForm, в обход запрета смены); в разломе забег тестовый.
        /// </summary>
        private static void DrawWreckForm(DevContext context, DevUi ui)
        {
            const string id = "pelag.forms.wreck";
            var loadout = context.Session != null ? context.Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.WreckBreakwater);
            bool owned = loadout != null && loadout.Owns(line);
            ui.Text("Крушение: форма");
            int shown = owned ? Array.IndexOf(WreckForms, loadout.FormOf(line)) : -1;
            int chosen = ui.Grid(shown, WreckFormOptions, perRow: 2, enabled: owned);
            if (owned && chosen != shown && chosen >= 0)
            {
                PelagForm form = WreckForms[chosen];
                ui.Defer(c => SetWreckForm(c, line, form), id);
            }
            ui.Consequences(DevFlags.MarksTestRun, context);
            ui.Hint(owned
                ? "Ставит форму сразу, без экрана выбора; таланты формы при смене пропадают."
                : "Нужно Крушение в наборе — стрелки слота выше («Набор съёмки» его не берёт).");
            ui.Error(id);
        }

        private static void SetWreckForm(DevContext context, int line, PelagForm form)
        {
            var session = context.Session;
            if (session == null || session.ActiveLoadout == null) throw new ArgumentException("Нет набора: меню открыто вне игры.");
            if (!session.ActiveLoadout.DebugSetForm(line, form))
                throw new ArgumentException("Форма не встала: Крушения нет в наборе.");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            if (session.Mode == GameMode.Rift) session.MarkDeveloperRun();
            context.Driver.RefreshAbilityBuild();
        }

        private static void Open(DevContext context)
        {
            if (!context.InRift) throw new ArgumentException("Выбор формы — только в забеге.");
            if (!context.Run.DebugOpenFormScreen())
                throw new ArgumentException("Экран формы не открылся: нужен бой на арене или путь к выходу и навык с формами без формы " +
                                            "(Вихрь или Шквал — «Набор съёмки» на этой вкладке, Абордаж и Крушение — стрелками слота).");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            context.Session.MarkDeveloperRun();
        }
    }
}
#endif
