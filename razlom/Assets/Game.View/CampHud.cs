using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Лагерь и экран итогов забега.
    ///
    /// Намеренно уродливо: списки, кнопки и подсказки по клавишам. Рисовать
    /// лагерь красиво до того, как проверено, что в него хочется заходить, —
    /// работа не в ту сторону, ровно как и с деревом способностей.
    ///
    /// ПОЧЕМУ ЭТОТ ЭКРАН ВООБЩЕ ВАЖЕН. Забег длится минуты, и кнопка
    /// «повторить одним нажатием» обязана существовать — иначе лагерь
    /// превращается в принудительный коридор. Значит, лагерь конкурирует
    /// с этой кнопкой за внимание и должен выигрывать честно: тем, что в нём
    /// есть незакрытое дело, а не тем, что мимо него не пройти. Поэтому
    /// рядом с «повторить» всегда написано, что ждёт в лагере.
    ///
    /// Лагерь правится ЗДЕСЬ напрямую, без команд. Это не нарушение правила
    /// «представление не пишет в симуляцию»: лагерь не тикает и в хеш забега
    /// не входит. Всё, что игрок решил в лагере, попадает в забег ровно один
    /// раз — его начальным состоянием. Поэтому кнопки и рисуются только вне
    /// Разлома: правка снаряжения посреди боя сломала бы реплей.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class CampHud : MonoBehaviour
    {
        private TickDriver _driver;
        private GUIStyle _title;
        private GUIStyle _line;
        private Vector2 _bagScroll;

        private readonly GeneratedItem _itemBuffer = new GeneratedItem();

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnGUI()
        {
            GameSession session = _driver.Session;
            if (session == null) return;
            if (session.Mode == GameMode.Rift) return;

            EnsureStyles();

            if (session.Mode == GameMode.Summary) DrawSummary(session);
            else DrawCamp(session);
        }

        // ---- экран итогов ----

        private void DrawSummary(GameSession session)
        {
            RunSummary summary = session.LastRun;

            GUILayout.BeginArea(new Rect(16, 16, 620, 320));

            GUILayout.Label(summary.Outcome == RunOutcome.Died
                ? "СМЕРТЬ. Глубже в этот раз не пойдёшь — но добытое осталось."
                : "ВЫХОД. Ушёл с добычей.", _title);

            GUILayout.Label($"Разломов зачищено: {summary.RiftsCleared}   " +
                            $"глубина: {summary.Depth}   предметов: {summary.ItemsKept}", _line);

            if (summary.ItemsLost > 0)
                GUILayout.Label($"Не влезло в сумку и потеряно: {summary.ItemsLost}", _line);

            GUILayout.Space(8);

            // Вот здесь лагерь и конкурирует с кнопкой «повторить».
            // Пока в списке только то, что реально существует; незакрытая
            // ячейка Летописи, готовый к перековке предмет и невзятый заказ
            // встанут сюда же вместе со своими механиками.
            GUILayout.Label("В лагере ждёт:", _title);
            if (session.NewItemsToTry > 0)
                GUILayout.Label($"  · новых вещей проверить на Полигоне: {session.NewItemsToTry}", _line);
            if (session.JunkToSalvage > 0)
                GUILayout.Label($"  · мусора под разбор: {session.JunkToSalvage}", _line);
            if (session.NewItemsToTry == 0 && session.JunkToSalvage == 0)
                GUILayout.Label("  · ничего. Значит, повторяй.", _line);

            GUILayout.Space(8);
            GUILayout.Label("R — повторить одним нажатием,  C — в лагерь", _title);

            GUILayout.EndArea();
        }

        // ---- лагерь ----

        private void DrawCamp(GameSession session)
        {
            Camp camp = session.Camp;

            GUILayout.BeginArea(new Rect(16, 16, 640, Screen.height - 32));

            GUILayout.Label($"ЛАГЕРЬ · акт {camp.Act}", _title);
            GUILayout.Label($"золото {camp.Money(CurrencyType.Gold)}   " +
                            $"осколки {camp.Money(CurrencyType.Shards)}   " +
                            $"лавидий {camp.Money(CurrencyType.Lavidium)}", _line);

            DrawWorn(camp);

            GUILayout.Space(6);
            GUILayout.Label(session.OnProvingGround
                ? $"ПОЛИГОН. Урон {session.Ground.DamageTotal} за {session.Ground.Ticks / Simulation.TicksPerSecond} с   " +
                  $"({session.Ground.DamagePerSecond} в секунду)"
                : "Полигон свободен.", _title);

            if (session.OnProvingGround)
            {
                ProvingGround ground = session.Ground;
                GUILayout.Label($"  физический {ground.PhysicalDamage}   огонь {ground.FireDamage}   " +
                                $"ударов {ground.Hits}, из них критов {ground.Crits}", _line);
                GUILayout.Label($"  манекен: {ground.DummyHealth} HP, броня {ground.DummyArmor.ToFloat():0}, " +
                                $"сопротивление огню {ground.DummyFireResist.ToFloat() * 100f:0}%", _line);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("сбросить счёт", GUILayout.Width(130))) ground.ResetCounters();
                if (GUILayout.Button("голый манекен", GUILayout.Width(130)))
                    session.RetuneDummy(100000, Fix64.Zero, Fix64.Zero);
                if (GUILayout.Button("броня 200", GUILayout.Width(110)))
                    session.RetuneDummy(100000, Fix64.FromInt(200), Fix64.Zero);
                if (GUILayout.Button("огнеупор 75%", GUILayout.Width(130)))
                    session.RetuneDummy(100000, Fix64.Zero, Fix64.Ratio(75, 100));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            DrawBag(camp);

            GUILayout.Space(6);
            GUILayout.Label("E — в Разлом,  T — Полигон,  V — разобрать мусор", _title);

            GUILayout.EndArea();
        }

        private void DrawWorn(Camp camp)
        {
            GUILayout.Space(6);
            GUILayout.Label("Надето:", _title);

            int unequipRequest = -1;

            for (int slot = 0; slot < (int)EquipSlot.Count; slot++)
            {
                var equipSlot = (EquipSlot)slot;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {equipSlot}: {Describe(camp.Worn.Worn(equipSlot), camp)}",
                    _line, GUILayout.Width(500));

                if (camp.Worn.IsWorn(equipSlot) && GUILayout.Button("снять", GUILayout.Width(80)))
                    unequipRequest = slot;

                GUILayout.EndHorizontal();
            }

            if (unequipRequest >= 0) camp.UnequipToBag((EquipSlot)unequipRequest);
        }

        private void DrawBag(Camp camp)
        {
            GUILayout.Label($"Сумка {camp.Bag.Used}/{camp.Bag.Capacity}   " +
                            $"под разбор: {camp.Bag.UnkeptCount}", _title);

            // Нажатие не выполняется на месте, а запоминается и делается ПОСЛЕ
            // обхода. Две причины, и обе настоящие: список меняется под нами,
            // а выход из середины BeginHorizontal оставил бы группу незакрытой,
            // и GUILayout бросил бы исключение на следующем кадре.
            int equipRequest = -1;
            int sellRequest = -1;
            int keepToggle = -1;

            _bagScroll = GUILayout.BeginScrollView(_bagScroll, GUILayout.Height(260));

            for (int i = 0; i < camp.Bag.Capacity; i++)
            {
                if (camp.Bag.IsEmpty(i)) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {(camp.Bag.IsKept(i) ? "*" : " ")} {Describe(camp.Bag.At(i), camp)}",
                    _line, GUILayout.Width(400));

                if (GUILayout.Button("надеть", GUILayout.Width(80))) equipRequest = i;
                if (GUILayout.Button(camp.Bag.IsKept(i) ? "не беречь" : "беречь", GUILayout.Width(90)))
                    keepToggle = i;
                if (GUILayout.Button("продать", GUILayout.Width(80))) sellRequest = i;

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (keepToggle >= 0) camp.Bag.SetKeep(keepToggle, !camp.Bag.IsKept(keepToggle));
            if (equipRequest >= 0) camp.EquipFromBag(equipRequest);
            if (sellRequest >= 0) camp.SellToTrader(sellRequest);
        }

        /// <summary>
        /// Человекочитаемый предмет. Разворачивается из рецепта прямо здесь:
        /// в сумке лежит рецепт, а не посчитанные статы.
        /// </summary>
        private string Describe(in ItemInstance item, Camp camp)
        {
            if (item.IsEmpty) return "пусто";
            if (!ItemGenerator.Generate(in item, camp.Items, _itemBuffer))
                return "предмет (база не найдена)";

            string text = $"ур.{item.ItemLevel} {item.Rarity}";
            if (_itemBuffer.HasImplicit)
                text += $" | {_itemBuffer.ImplicitStat} {_itemBuffer.ImplicitOp} " +
                        $"{_itemBuffer.ImplicitValue.ToFloat():0.##}";

            for (int i = 0; i < _itemBuffer.AffixCount; i++)
            {
                RolledAffix a = _itemBuffer.GetAffix(i);
                text += $" | {a.Stat} {a.Op} {a.Value.ToFloat():0.##}";
            }
            return text;
        }

        private void EnsureStyles()
        {
            if (_title != null) return;

            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 13 };

            _title.normal.textColor = Color.white;
            _line.normal.textColor = new Color(0.88f, 0.88f, 0.85f);
        }
    }
}
