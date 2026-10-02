#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Вкладка «Пелаг»: набор (четыре слота, готовые наборы, артефакт забега) и таланты — отладка визуала
    /// (список восьми линий и восемь переключателей выбранной). Секция «Формы» — в DeveloperMenu.Forms.cs.
    /// Таланты не сохраняются; TickDriver пересобирает билд по DeveloperTalents.Version, маску меню не кеширует.
    /// </summary>
    public sealed partial class DeveloperMenu
    {
        private const string Kit = "Набор", Talents = "Таланты · отладка визуала";
        private int _talentLine;
        private static string _capturePresetLabel;

        private void RegisterPelagEntries()
        {
            DevMenu.Section(DevTab.Pelag, Kit, 10, DevColumn.Left);
            DevMenu.Section(DevTab.Pelag, Talents, 30, DevColumn.Right,
                hint: "Включаются поштучно, без порядка и очков. Действуют в лагере, на полигоне и в забеге. Не сохраняются.");

            DevMenu.Custom("pelag.slots", DevTab.Pelag, Kit, DrawSlots, order: 10);

            DevMenu.Button("pelag.preset-capture", DevTab.Pelag, Kit, "Набор съёмки",
                c => ChangeLoadout(c, loadout =>
                {
                    for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++) loadout.Put(slot, Game.Sim.RunLoadout.EmptySlot);
                    for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++) loadout.Put(slot, slot);
                }),
                DevFlags.MarksTestRun,
                dynamicHint: c => CapturePresetLabel() + " — первые четыре способности пула, тот же набор собирает съёмка.",
                order: 20);

            DevMenu.Button("pelag.preset-starter", DevTab.Pelag, Kit, "Стартовый набор: только Вихрь",
                c => ChangeLoadout(c, loadout => loadout.ResetToStarter()),
                DevFlags.MarksTestRun, order: 30);

            DevMenu.Button("pelag.artifact", DevTab.Pelag, Kit, "Артефакт забега", NextArtifact,
                DevFlags.MarksTestRun,
                visible: c => c.InRift,
                dynamicLabel: c => "Артефакт забега: " + RunArtifactTexts.Name(c.Run.Artifact) + " ›",
                hint: "По кругу: нет → восемь артефактов набора → нет.",
                order: 40);

            DevMenu.Custom("pelag.talents", DevTab.Pelag, Talents, DrawTalents, order: 10);

            DevMenu.Button("pelag.talents-clear", DevTab.Pelag, Talents, "Снять все таланты",
                c =>
                {
                    DeveloperTalents.Clear();
                    _driver.RefreshAbilityBuild();
                },
                order: 20);
        }

        /// <summary>«Вихрь · Рассекающий удар · …» — имена первых четырёх способностей пула.</summary>
        private static string CapturePresetLabel()
        {
            if (_capturePresetLabel != null) return _capturePresetLabel;
            var label = new System.Text.StringBuilder();
            for (int i = 0; i < Game.Sim.RunLoadout.Slots; i++)
            {
                if (i > 0) label.Append(" · ");
                label.Append(PoolName(i));
            }
            return _capturePresetLabel = label.ToString();
        }

        /// <summary>
        /// Правка набора: в лагере — лагерный набор (его берут тестовые забеги из меню), в разломе — набор забега,
        /// а это его добыча, поэтому забег становится тестовым.
        /// </summary>
        private void ChangeLoadout(DevContext context, System.Action<Game.Sim.RunLoadout> change)
        {
            var session = context.Session;
            change(session.ActiveLoadout);
            if (session.Mode == Game.Sim.GameMode.Rift) session.MarkDeveloperRun();
            _driver.RefreshAbilityBuild();
        }

        private void NextArtifact(DevContext context)
        {
            var session = context.Session;
            var run = context.Run;
            if (!context.InRift) throw new System.InvalidOperationException("Артефакт — только в разломе.");
            int index = -1;
            for (int i = 0; i < Game.Sim.RunArtifacts.Count; i++)
                if (Game.Sim.RunArtifacts.At(i) == run.Artifact) index = i;
            if (index + 1 >= Game.Sim.RunArtifacts.Count) run.ClearArtifactForDeveloper();
            else run.TakeArtifact(Game.Sim.RunArtifacts.At(index + 1));
            session.MarkDeveloperRun();
        }

        /// <summary>
        /// Четыре слота: «‹ 1 · Вихрь · Буря · усилений 2 ›». Стрелки — следующая по кругу способность пула, которой
        /// нет в других слотах («пусто» входит в круг).
        /// </summary>
        private void DrawSlots(DevContext context, DevUi ui)
        {
            var session = context.Session;
            var loadout = session.ActiveLoadout;
            ui.Hint(session.Mode == Game.Sim.GameMode.Rift
                ? "Способности забега: правка делает забег тестовым."
                : "Способности в лагере: тестовые забеги из меню берут этот набор.");
            ui.Consequences(DevFlags.MarksTestRun, context);
            for (int slot = 0; slot < Game.Sim.RunLoadout.Slots; slot++)
            {
                int pool = loadout.PoolIndexAt(slot);
                int rank = loadout.TalentRank(pool);
                string name = pool < 0 ? "пусто" : PelagFormTexts.TooltipTitle(PoolName(pool), loadout.FormOf(pool));
                GUILayout.BeginHorizontal();
                int target = slot;
                if (ui.Small("‹", width: 36f)) ui.Defer(c => ChangeLoadout(c, l => CycleSlot(l, target, -1)), "pelag.slots");
                GUILayout.Label((slot + 1) + " · " + name + (rank > 0 ? " · усилений " + rank : ""), _skin.RowLabel, GUILayout.MinWidth(1f));
                if (ui.Small("›", width: 36f)) ui.Defer(c => ChangeLoadout(c, l => CycleSlot(l, target, 1)), "pelag.slots");
                GUILayout.EndHorizontal();
            }
            ui.Error("pelag.slots");
        }

        /// <summary>Следующая по кругу способность пула, которой нет в других слотах; «пусто» входит в круг.</summary>
        private static void CycleSlot(Game.Sim.RunLoadout loadout, int slot, int step)
        {
            int size = Game.Sim.PelagKit.PoolSize + 1;
            int current = loadout.PoolIndexAt(slot);
            for (int i = 1; i < size; i++)
            {
                int next = ((current + 1 + step * i) % size + size) % size - 1;
                if (next == Game.Sim.RunLoadout.EmptySlot || !loadout.Owns(next))
                {
                    loadout.Put(slot, next);
                    return;
                }
            }
        }

        /// <summary>Слева восемь линий «Вихрь 3/8», справа восемь переключателей выбранной линии.</summary>
        private void DrawTalents(DevContext context, DevUi ui)
        {
            int lines = Game.Sim.SabreTalents.LineCount;
            int perLine = Game.Sim.SabreTalents.TalentsPerLine;
            int selected = Mathf.Clamp(_talentLine, 0, lines - 1);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(ui.Px(196)));
            for (int line = 0; line < lines; line++)
            {
                var lineId = (Game.Sim.SabreTalentLine)line;
                int on = 0;
                for (int index = 0; index < perLine; index++)
                    if (DeveloperTalents.Has(lineId, index)) on++;
                string title = DevMenuRules.SentenceCase(SabreTalentTexts.LineName(lineId)) + "  " + on + "/" + perLine;
                int target = line;
                if (GUILayout.Button(title, line == selected ? _skin.SmallOn : _skin.Small, GUILayout.ExpandWidth(true), GUILayout.MinWidth(1f)) && line != selected)
                    Local(() =>
                    {
                        _talentLine = target;
                        SaveInt(DevMenuRules.TalentLineKey, target);
                    });
            }
            GUILayout.EndVertical();
            GUILayout.Space(ui.Px(8));
            GUILayout.BeginVertical();
            var selectedLine = (Game.Sim.SabreTalentLine)selected;
            for (int index = 0; index < perLine; index++)
            {
                bool on = DeveloperTalents.Has(selectedLine, index);
                int talent = index;
                if (ui.ToggleRow(SabreTalentTexts.Name(selectedLine, index), on))
                    ui.Defer(c =>
                    {
                        DeveloperTalents.Set(selectedLine, talent, !on);
                        _driver.RefreshAbilityBuild();
                    }, "pelag.talents");
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            ui.Error("pelag.talents");
        }
    }
}
#endif
