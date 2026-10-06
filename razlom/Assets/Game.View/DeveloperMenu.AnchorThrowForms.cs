#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Бросок якоря: форма» в F8 (вкладка «Пелаг», секция «Формы», order 70 — 60 оставлен Крушению): Невод, Веер,
    /// Гарпун, без формы — сразу, без экрана выбора, как формы Абордажа (DeveloperMenu.Forms). Своим файлом: оболочка F8
    /// и пункты других навыков не трогаются. Сам Бросок берут стрелками слота — в награды он не идёт (PelagKit.InRewardPool).
    /// </summary>
    internal static class AnchorThrowFormsDevEntries
    {
        private const string Forms = "Формы";
        private const string Id = "pelag.forms.anchor_throw";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            DevMenu.Custom(Id, DevTab.Pelag, Forms, Draw, order: 70);
        }

        private static readonly PelagForm[] Options =
            { PelagForm.AnchorThrowNet, PelagForm.AnchorThrowFan, PelagForm.AnchorThrowHarpoon, PelagForm.None };
        private static readonly string[] Labels = { "Невод", "Веер", "Гарпун", "без формы" };

        /// <summary>2×2: Невод, Веер, Гарпун, без формы. Ставит форму набору сразу (RunLoadout.DebugSetForm); в разломе забег тестовый.</summary>
        private static void Draw(DevContext context, DevUi ui)
        {
            var loadout = context.Session != null ? context.Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.AnchorThrowNet);
            bool owned = loadout != null && loadout.Owns(line);
            ui.Text("Бросок якоря: форма");
            int shown = owned ? Array.IndexOf(Options, loadout.FormOf(line)) : -1;
            int chosen = ui.Grid(shown, Labels, perRow: 2, enabled: owned);
            if (owned && chosen != shown && chosen >= 0)
            {
                PelagForm form = Options[chosen];
                ui.Defer(c => Set(c, line, form), Id);
            }
            ui.Consequences(DevFlags.MarksTestRun, context);
            ui.Hint(owned
                ? "Ставит форму сразу, без экрана выбора; таланты формы при смене пропадают."
                : "Нужен Бросок якоря в наборе — стрелки слота выше (в награды он пока не идёт).");
            ui.Error(Id);
        }

        private static void Set(DevContext context, int line, PelagForm form)
        {
            var session = context.Session;
            if (session == null || session.ActiveLoadout == null) throw new ArgumentException("Нет набора: меню открыто вне игры.");
            if (!session.ActiveLoadout.DebugSetForm(line, form))
                throw new ArgumentException("Форма не встала: Броска якоря нет в наборе.");
            // Форма меняет набор забега — как правка способностей из меню, забег тестовый.
            if (session.Mode == GameMode.Rift) session.MarkDeveloperRun();
            context.Driver.RefreshAbilityBuild();
        }
    }
}
#endif
