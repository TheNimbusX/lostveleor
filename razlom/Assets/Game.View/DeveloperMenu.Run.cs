#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Вкладка «Забег»: переход (локация, арена, сид, «Начать с арены N», «К боссу · свежий бой»), лагерь (уровень
    /// героя) и полоса «Осторожно» («Вернуться в лагерь»). Действия — те же функции TickDriver/Session, что и до
    /// переделки 02.10; имена StartDeveloperRift и ReturnToCampFromMenu зовут и съёмки.
    /// </summary>
    public sealed partial class DeveloperMenu
    {
        private const string Transition = "Переход", CampSection = "Лагерь", Careful = "Осторожно";
        private int _heroLevel = 1;

        private void RegisterRunEntries()
        {
            DevMenu.Section(DevTab.Run, Transition, 10, DevColumn.Left,
                hint: "Свежая тестовая карта заменяет текущий бой. Снаряжение — из лагеря, наград за пропуск нет, добыча теста в сумку не идёт.");
            DevMenu.Section(DevTab.Run, CampSection, 20, DevColumn.Right);
            DevMenu.Section(DevTab.Run, Careful, 90, DevColumn.Full, danger: true);

            DevMenu.Custom("run.location", DevTab.Run, Transition, DrawLocation, visible: c => !c.Sandbox, order: 10);
            DevMenu.Custom("run.arena", DevTab.Run, Transition, DrawArenaGrid, visible: c => !c.Sandbox && c.Location != null, order: 20);
            DevMenu.Custom("run.seed", DevTab.Run, Transition, DrawSeed, visible: c => !c.Sandbox && c.Location != null, order: 30);

            DevMenu.Button("run.start-arena", DevTab.Run, Transition, "Начать с арены", StartArena,
                DevFlags.Quick | DevFlags.ConfirmIfRealRun,
                blocked: c => c.Location == null ? "профили локаций не найдены" : null,
                visible: c => !c.Sandbox,
                dynamicLabel: c => "Начать с арены " + c.Arena + (IsBossArena(c, c.Arena) ? " (босс)" : ""),
                dynamicQuickLabel: c => "Арена " + DevMenuRules.ArenaCell(c.Arena, IsBossArena(c, c.Arena)) + " ›",
                hint: "Начинает выбранную арену у входа. Меню остаётся открытым — закрой его, чтобы начать бой. Enter в поле сида — то же.",
                order: 40, quickOrder: 20);

            // Якорь: «К боссу · свежий бой» цитируют DESIGN, STATE, MeadowBalance и память — подпись не менять.
            DevMenu.Button("run.boss", DevTab.Run, Transition, "К боссу · свежий бой", StartBoss,
                DevFlags.Quick | DevFlags.ConfirmIfRealRun,
                blocked: c => c.Location == null ? "профили локаций не найдены" : null,
                visible: c => !c.Sandbox,
                hint: "Первая арена с боссом, героя ставит на свободный пол не ближе 4 м от босса. Повтор — новый бой с полным здоровьем.",
                order: 50, quickOrder: 10);

            DevMenu.Custom("camp.hero-level", DevTab.Run, CampSection, DrawHeroLevel, order: 10);

            DevMenu.Button("run.to-camp", DevTab.Run, Careful, "Вернуться в лагерь", c => _driver.ReturnToCampFromMenu(),
                DevFlags.Quick | DevFlags.Danger | DevFlags.ConfirmIfRealRun,
                visible: c => !c.Sandbox,
                quickLabel: "В лагерь",
                quickVisible: c => c.InRift && c.TestRun,
                dynamicHint: c => c.RealRun
                    ? "Обычный забег пропадёт вместе с добычей — нужно второе нажатие."
                    : "Тестовый забег закрывается сразу. То же, что «В лагерь» в паузе.",
                order: 10, quickOrder: 50);
        }

        private static bool IsBossArena(DevContext context, int arena)
        {
            var levels = context.Location != null && context.Location.Gameplay != null ? context.Location.Gameplay.Levels : null;
            return levels != null && arena >= 1 && arena <= levels.Length && levels[arena - 1].Boss;
        }

        private void StartArena(DevContext context)
        {
            ulong seed = context.Seed;
            _driver.StartDeveloperRift(context.Location, context.Arena, false, seed);
            _arena = context.Arena;
            _loadedNote = "Загружено: " + DevMenuRules.ArenaTitle(context.Arena, IsBossArena(context, context.Arena)) + " — закрой меню";
        }

        private void StartBoss(DevContext context)
        {
            ulong seed = context.Seed;
            var theme = context.Location;
            int level = 0;
            for (int i = 0; i < theme.Gameplay.Levels.Length; i++)
                if (theme.Gameplay.Levels[i].Boss) { level = i + 1; break; }
            if (level == 0) throw new ArgumentException("В этой локации пока нет уровня с боссом.");
            _driver.StartDeveloperRift(theme, level, true, seed);
            _arena = level;
            SaveInt(DevMenuRules.ArenaKey, _arena);
            _loadedNote = "Загружено: " + DevMenuRules.ArenaTitle(level, true) + " — закрой меню";
        }

        private void DrawLocation(DevContext context, DevUi ui)
        {
            if (_locations.Length == 0)
            {
                ui.Hint("Профили локаций не найдены.");
                return;
            }
            if (_locations.Length == 1)
            {
                ui.Value("Локация", _locations[0].Gameplay.DisplayName);
                return;
            }
            GUILayout.Label("Локация", _skin.Hint);
            var names = new string[_locations.Length];
            for (int i = 0; i < names.Length; i++) names[i] = _locations[i].Gameplay.DisplayName;
            int chosen = ui.Grid(_selected, names, 2, 120f);
            if (chosen != _selected)
                Local(() =>
                {
                    _selected = chosen;
                    _arena = 1;
                    _errors.Remove("run.start-arena");
                    _errors.Remove("run.boss");
                });
        }

        private void DrawArenaGrid(DevContext context, DevUi ui)
        {
            int count = context.ArenaCount;
            GUILayout.Label("Арена", _skin.Hint);
            var cells = new string[Mathf.Max(1, count)];
            for (int i = 0; i < cells.Length; i++) cells[i] = DevMenuRules.ArenaCell(i + 1, IsBossArena(context, i + 1));
            int chosen = ui.Grid(context.Arena - 1, cells, 0, 30f);
            if (chosen != context.Arena - 1)
                Local(() =>
                {
                    _arena = chosen + 1;
                    SaveInt(DevMenuRules.ArenaKey, _arena);
                });
        }

        private void DrawSeed(DevContext context, DevUi ui)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Сид", _skin.RowHint, GUILayout.Width(ui.Px(120)));
            _seed = ui.Field(SeedControl, _seed, 20, 220f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!context.SeedValid) GUILayout.Label(DevMenuRules.SeedError, _skin.ErrorText);
            ui.Hint("Для «Начать с арены», «К боссу» и «Лесного бутона». Свой поток: сиды обычных забегов не тратятся. У «Темпа боя» свой сид 20260829.");
        }

        /// <summary>
        /// Уровень героя — одна строка: текущий уровень и опыт, счётчик (начинается с текущего), «Поставить N» и «+1».
        /// Обе кнопки пишут сохранение лагеря (CampSaveStore пропускает только тестовый забег, после возврата в лагерь
        /// уровень сохранится) — поэтому всегда со вторым нажатием. Статов уровень с 29.09 не даёт: только пороги
        /// улучшений лагеря; SyncPlayerLevel лишь подтверждает базу героя.
        /// </summary>
        private void DrawHeroLevel(DevContext context, DevUi ui)
        {
            var camp = context.Session.Camp;
            ui.Text("Уровень героя " + camp.Level + " · опыт " + camp.Experience + " / " + camp.ExperienceToNextLevel);
            GUILayout.BeginHorizontal();
            if (ui.Small("−")) Local(() => _heroLevel = Mathf.Max(1, _heroLevel - 1));
            GUILayout.Label(_heroLevel.ToString(), _skin.RowNumber, GUILayout.Width(ui.Px(52)));
            if (ui.Small("+")) Local(() => _heroLevel++);
            if (ui.Small("+10", width: 52f)) Local(() => _heroLevel += 10);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            const DevFlags writes = DevFlags.Confirm | DevFlags.WritesSave;
            int target = _heroLevel;
            if (ui.Button("camp.hero-level.set", "Поставить уровень " + target, writes, context.RealRun))
                ui.Defer(c =>
                {
                    c.Session.Camp.DeveloperSetLevel(target);
                    c.Session.SyncPlayerLevel();
                }, "camp.hero-level");
            GUILayout.Space(ui.Px(6));
            if (ui.Button("camp.hero-level.grant", "+1 уровень", writes, context.RealRun, true, GUILayout.Width(ui.Px(130))))
                ui.Defer(c =>
                {
                    c.Session.Camp.DeveloperGrantLevel();
                    c.Session.SyncPlayerLevel();
                    _heroLevel = c.Session.Camp.Level;
                }, "camp.hero-level");
            GUILayout.EndHorizontal();
            ui.Consequences(DevFlags.WritesSave, context);
            ui.Hint("Статов уровень не даёт — только пороги улучшений лагеря. Опыт обнуляется.");
            ui.Error("camp.hero-level");
        }
    }
}
#endif
