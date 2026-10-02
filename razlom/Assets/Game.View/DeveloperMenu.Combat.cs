#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Вкладка «Бой»: герой («Бессмертие персонажа»), тестовые бои («Лесной бутон · тестовый бой / повтор», темп боя)
    /// и строка стенда мобов. StartForestBudTest и StartTempoTest зовут ещё съёмки и окна редактора — имена те же.
    /// </summary>
    public sealed partial class DeveloperMenu
    {
        private const string Hero = "Герой", TestFights = "Тестовые бои", SandboxSection = "Стенд мобов";
        private static readonly string[] TempoPresets = { "Базовая", "Средняя", "Быстрая" };
        private int _tempoPreset = 2;

        private void RegisterCombatEntries()
        {
            DevMenu.Section(DevTab.Combat, SandboxSection, 5, DevColumn.Left);
            DevMenu.Section(DevTab.Combat, Hero, 10, DevColumn.Left);
            DevMenu.Section(DevTab.Combat, TestFights, 20, DevColumn.Left);

            DevMenu.Custom("combat.sandbox", DevTab.Combat, SandboxSection, (c, ui) =>
            {
                ui.Text("Расстановка и управление врагами — в окне Разлом → Мобы.");
                ui.Hint("Прогресс не загружается и не сохраняется.");
            }, visible: c => c.Sandbox);

            // Якорь: «Бессмертие персонажа» — на него ссылаются окна стендов («F8 → Бой») и Docs.
            DevMenu.Toggle("combat.immortal", DevTab.Combat, Hero, "Бессмертие персонажа",
                c => c.Session.DeveloperInvulnerable,
                (c, on) => c.Session.SetDeveloperInvulnerable(on),
                DevFlags.Quick | DevFlags.MarksTestRun,
                blocked: c => c.InRift ? null : "Только в разломе: сначала войди в любой разлом.",
                hint: "Блокирует входящий урон, не прибавляет здоровья и не воскрешает. Держится до лагеря или нового обычного забега. " +
                      "Забег становится тестовым навсегда: добыча не переносится в сумку.",
                order: 10, quickOrder: 30);

            // Якорь: «Лесной бутон · тестовый бой / повтор» цитируют DESIGN, README и лог TickDriver.ForestBud.
            DevMenu.Button("combat.bud", DevTab.Combat, TestFights, "Лесной бутон · тестовый бой / повтор",
                c => _driver.StartForestBudTest(c.Location, c.Seed),
                DevFlags.Quick | DevFlags.CloseMenu | DevFlags.ConfirmIfRealRun,
                blocked: c => c.Location == null ? "профили локаций не найдены" : null,
                visible: c => !c.Sandbox,
                quickLabel: "Лесной бутон ›",
                hint: "Один дальнобой. Уклоняйся от красных меток, атакуй обычными способностями. F8 — повтор или выход в лагерь. Сид — с вкладки «Забег».",
                order: 10, quickOrder: 40);

            DevMenu.Choice("combat.tempo-preset", DevTab.Combat, TestFights, "Темп боя · сборка", TempoPresets,
                c => _tempoPreset,
                (c, preset) =>
                {
                    _tempoPreset = Mathf.Clamp(preset, 0, TempoPresets.Length - 1);
                    SaveInt(DevMenuRules.TempoKey, _tempoPreset);
                },
                DevFlags.Remembered, visible: c => !c.Sandbox, order: 20);

            DevMenu.Button("combat.tempo", DevTab.Combat, TestFights, "Повторить бой с текущими навыками",
                c =>
                {
                    var loadout = c.Session.ActiveLoadout;
                    var skills = new int[Game.Sim.RunLoadout.Slots];
                    for (int i = 0; i < skills.Length; i++) skills[i] = loadout.PoolIndexAt(i);
                    _driver.StartTempoTest(skills, _tempoPreset);
                },
                DevFlags.CloseMenu | DevFlags.ConfirmIfRealRun,
                blocked: c => LoadoutFull(c) ? null : "Заполни все четыре слота на вкладке «Пелаг».",
                visible: c => !c.Sandbox,
                dynamicHint: c => "Набор: " + LoadoutLine(c) + ". Лесной бутон и четыре ближника, сид 20260829. " +
                                  "Настоящие расходы и КД; прогресс лагеря не меняется.",
                order: 30);
        }

        private static bool LoadoutFull(DevContext context)
        {
            var loadout = context.Session.ActiveLoadout;
            for (int i = 0; i < Game.Sim.RunLoadout.Slots; i++)
                if (loadout.PoolIndexAt(i) < 0) return false;
            return true;
        }

        private static string LoadoutLine(DevContext context)
        {
            var loadout = context.Session.ActiveLoadout;
            var line = new StringBuilder();
            for (int i = 0; i < Game.Sim.RunLoadout.Slots; i++)
            {
                if (i > 0) line.Append(" · ");
                line.Append(PoolName(loadout.PoolIndexAt(i)));
            }
            return line.ToString();
        }

        private static readonly string[] PoolNames = new string[Game.Sim.PelagKit.PoolSize];

        /// <summary>Имя способности пула (кэш: PoolDefinition собирает определение заново на каждый вызов).</summary>
        private static string PoolName(int pool)
        {
            if ((uint)pool >= (uint)PoolNames.Length) return "пусто";
            if (PoolNames[pool] == null)
            {
                var definition = Game.Sim.PelagKit.PoolDefinition(pool);
                // Имена в HUD — прописными; в меню — как в предложении: «Вихрь · Рассекающий удар».
                PoolNames[pool] = definition == null ? "пусто" : DevMenuRules.SentenceCase(PlayerHud.AbilityName(definition.Id));
            }
            return PoolNames[pool];
        }
    }
}
#endif
