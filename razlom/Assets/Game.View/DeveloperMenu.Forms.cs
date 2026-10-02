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
                hint: "Нужен навык с формами без формы (пока только Вихрь — «Стартовый набор: только Вихрь»). " +
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

        private static void Open(DevContext context)
        {
            if (!context.InRift) throw new ArgumentException("Выбор формы — только в забеге.");
            if (!context.Run.DebugOpenFormScreen())
                throw new ArgumentException("Экран формы не открылся: нужен бой на арене или путь к выходу и навык с формами без формы " +
                                            "(пока только Вихрь — «Стартовый набор: только Вихрь» на этой вкладке).");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            context.Session.MarkDeveloperRun();
        }
    }
}
#endif
