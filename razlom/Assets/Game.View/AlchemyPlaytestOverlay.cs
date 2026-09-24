#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>Temporary, save-isolated in-game controls while the authored camp UI is being built.</summary>
    public sealed class AlchemyPlaytestOverlay : MonoBehaviour
    {
        TickDriver _driver;
        bool _visible, _previousPause, _confirmResin, _confirmSurge, _standaloneTrial;
        Vector2 _scroll;
        string _message = "Тестовый запас: 500 золота, 12 осколков и редкая вещь. Сохранение отключено.";

        public void Initialize(TickDriver driver)
        {
            _driver = driver;
            _standaloneTrial = true;
            var camp = driver.Session.Camp;
            camp.Earn(CurrencyType.Gold, 500);
            camp.Earn(CurrencyType.Shards, 12);
            int[] bases = PrototypeContent.ItemBaseIds();
            if (bases.Length > 0) camp.Bag.Add(new ItemInstance(bases[0], 1, ItemRarity.Magic, 20260923));
            Show(true);
        }

        public void OpenFromNpc(TickDriver driver)
        {
            _driver = driver;
            if (!_standaloneTrial) _message = CampServiceText.Get("dialogue.alchemist.open");
            _confirmResin = _confirmSurge = false;
            Show(true);
        }

        void Update()
        {
            bool toggle;
#if ENABLE_INPUT_SYSTEM
            toggle = Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame;
#else
            toggle = Input.GetKeyDown(KeyCode.F10);
#endif
            if (toggle && (_visible || _standaloneTrial)) Show(!_visible);
        }

        void Show(bool visible)
        {
            if (_driver == null || _visible == visible) return;
            if (visible)
            {
                CampServicesView.Instance?.Close();
                _previousPause = _driver.GameplayPaused;
                _driver.SetGameplayPaused(true);
            }
            else _driver.SetGameplayPaused(_previousPause);
            _visible = visible;
        }

        void OnDestroy()
        {
            if (_visible && _driver != null) _driver.SetGameplayPaused(_previousPause);
        }

        void OnGUI()
        {
            if (!_visible || _driver?.Session == null) return;
            GUI.depth = -100;
            float width = Mathf.Min(560, Screen.width - 24);
            float height = Mathf.Min(920, Screen.height - 24);
            var panel = new Rect(12, 12, width, height);
            var previousColor = GUI.color;
            GUI.color = new Color(0.07f, 0.09f, 0.08f, 0.95f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label(_standaloneTrial ? "ЛЕО · ТЕСТОВЫЙ СТЕНД" : "ЛЕО · ВРЕМЕННАЯ ПАНЕЛЬ");
            GUILayout.Label(_standaloneTrial
                ? "F10 — скрыть или открыть. Эта тестовая сессия не пишет сохранение."
                : "Пока готовится новый UI. Здесь используется ваш обычный прогресс.");
            if (GUILayout.Button(_standaloneTrial ? "Вернуться к игре (F10)" : "Завершить разговор", GUILayout.Height(28))) Show(false);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(_message);
            var session = _driver.Session;
            var camp = session.Camp;
            GUILayout.Space(8);
            GUILayout.Label("Золото: " + camp.Money(CurrencyType.Gold) + "   Осколки: " + camp.Money(CurrencyType.Shards));
            GUILayout.Label("Живица: " + session.ActiveSim.ResinTicksLeft / 30f + " с   Порыв: " + session.ActiveSim.SurgeTicksLeft / 30f + " с");
            if (session.Mode == GameMode.Camp)
            {
                if (!camp.HasMetAlchemist && GUILayout.Button("Поговорить с Лео", GUILayout.Height(30)))
                { camp.MeetAlchemist(); _message = CampServiceText.Get("dialogue.alchemist.open"); }
                DrawOrder(camp, AlchemistOrder.Resin, "Живица", "Убить Лесного бутона или отдать редкую вещь");
                DrawOrder(camp, AlchemistOrder.Surge, "Лавидиевый порыв", "Весь уровень без зелий или 12 осколков");
                GUILayout.Space(8);
                GUILayout.Label("ЛАВКА · купи и выбери зелье для двух прежних слотов");
                for (int i = 0; i < Camp.PotionKindCount; i++) DrawPotion(camp, (PotionKind)i);
                GUILayout.Space(8);
                if (_standaloneTrial && GUILayout.Button("Обычный бой с гарантированным Лесным бутоном", GUILayout.Height(32)))
                {
                    var theme = _driver.GetComponent<LayoutView>()?.Profile;
                    if (theme == null) _message = "Не найден профиль лесной локации.";
                    else
                    {
                        _driver.StartAlchemyBudTrial(theme, 20260923UL);
                        Show(false);
                    }
                }
            }
            else
            {
                GUILayout.Label("Режим: " + session.Mode + "  Уровень: " + (session.Run?.Depth ?? 0));
                GUILayout.Label("Уровень без зелий: " + (session.AlchemyCleanLevelInProgress ? "пока чистый" : "не засчитывается"));
                GUILayout.Label("Закрой панель, победи Бутона и дойди до выхода. Зелья — обычные кнопки 5/6 или крестовина геймпада.");
                if (GUILayout.Button("Завершить попытку и вернуться в лагерь", GUILayout.Height(30)))
                { _driver.ReturnToCampFromMenu(); _message = "Попытка завершена. Готовые заказы сохраняются в этой тестовой сессии."; }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawOrder(Camp camp, AlchemistOrder order, string title, string goal)
        {
            var state = camp.AlchemyStatus(order);
            GUILayout.Space(7);
            GUILayout.Label(title + " — " + StatusName(state));
            GUILayout.Label(goal);
            if (state == AlchemistOrderStatus.Available && GUILayout.Button("Принять заказ"))
                Report(camp.AcceptAlchemyOrder(order), "dialogue.alchemist.order.accept");
            if (state == AlchemistOrderStatus.Ready && GUILayout.Button("Сдать выполненный заказ"))
                Report(camp.TurnInAlchemyOrder(order), "dialogue.alchemist.order.complete");
            if (state != AlchemistOrderStatus.Accepted) return;
            if (order == AlchemistOrder.Resin)
            {
                int slot = FirstExchangeableRare(camp);
                if (slot < 0) GUILayout.Label("Для обмена нужна редкая вещь без отметки «беречь» в сумке.");
                else if (GUILayout.Button(_confirmResin ? "Подтвердить: отдать вещь из слота " + (slot + 1)
                    : "Обменять редкую вещь из слота " + (slot + 1)))
                {
                    if (!_confirmResin) _confirmResin = true;
                    else { Report(camp.ExchangeRareForResin(slot), "dialogue.alchemist.recipe.unlock"); _confirmResin = false; }
                }
            }
            else if (GUILayout.Button(_confirmSurge ? "Подтвердить: отдать 12 осколков" : "Обменять 12 осколков"))
            {
                if (!_confirmSurge) _confirmSurge = true;
                else { Report(camp.ExchangeShardsForSurge(), "dialogue.alchemist.recipe.unlock"); _confirmSurge = false; }
            }
        }

        void DrawPotion(Camp camp, PotionKind kind)
        {
            bool unlocked = camp.PotionUnlocked(kind);
            int slot = Camp.PotionSlot(kind);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Name(kind) + " · " + Camp.PotionPercent(kind) + "% · запас " + camp.PotionCount(kind)
                + (unlocked ? "" : " · закрыто"), GUILayout.Width(275));
            bool previous = GUI.enabled;
            GUI.enabled = unlocked;
            if (GUILayout.Button("Купить · " + Camp.PotionPrice(kind), GUILayout.Width(125)))
            {
                var result = camp.TryBuyPotion(kind);
                _message = PurchaseName(result);
                if (result == PotionPurchaseResult.Success)
                    GameSound.Sequence(("alch_bottle", 0f, .8f), ("alch_clink", .2f, .5f), ("coins", .35f, .4f));
            }
            GUI.enabled = unlocked && camp.SelectedPotion(slot) != kind;
            if (GUILayout.Button("Выбрать", GUILayout.Width(100)))
            {
                if (camp.SelectPotion(kind))
                {
                    _message = Name(kind) + " выбрано для слота " + (slot + 1) + ".";
                    GameSound.Sequence(("alch_cork", 0f, .65f), ("alch_pour", .14f, .55f), ("alch_bubble", .45f, .3f));
                }
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        static int FirstExchangeableRare(Camp camp)
        {
            for (int i = 0; i < camp.Bag.Capacity; i++)
                if (!camp.Bag.IsEmpty(i) && !camp.Bag.IsKept(i) && camp.Bag.At(i).Rarity == ItemRarity.Magic)
                    return i;
            return -1;
        }
        void Report(AlchemistActionResult result, string dialogueKey)
        {
            _message = result == AlchemistActionResult.Success ? CampServiceText.Get(dialogueKey) : "Действие недоступно: " + result;
        }
        static string StatusName(AlchemistOrderStatus state)
        {
            switch (state)
            {
                case AlchemistOrderStatus.Available: return "можно принять";
                case AlchemistOrderStatus.Accepted: return "в работе";
                case AlchemistOrderStatus.Ready: return "можно сдать";
                case AlchemistOrderStatus.Unlocked: return "рецепт открыт";
                default: return "поговори с алхимиком";
            }
        }
        static string PurchaseName(PotionPurchaseResult result)
        {
            switch (result)
            {
                case PotionPurchaseResult.Success: return CampServiceText.Get("dialogue.alchemist.buy");
                case PotionPurchaseResult.RecipeLocked: return "Рецепт ещё закрыт.";
                case PotionPurchaseResult.InsufficientGold: return "Не хватает золота.";
                case PotionPurchaseResult.StockFull: return "Достигнут предел запаса.";
                default: return "Покупка недоступна: " + result;
            }
        }
        static string Name(PotionKind kind)
        {
            switch (kind)
            {
                case PotionKind.SmallHealth: return "Малое здоровье";
                case PotionKind.LargeHealth: return "Большое здоровье";
                case PotionKind.SmallLavidium: return "Малый лавидий";
                case PotionKind.LargeLavidium: return "Большой лавидий";
                case PotionKind.LivingResin: return "Живица";
                default: return "Лавидиевый порыв";
            }
        }
    }
}
#endif
