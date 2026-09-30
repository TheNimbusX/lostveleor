using Game.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    public sealed partial class CampServicesView
    {
        static readonly string[] TraderCategoryNames = { "Оружие", "Доспехи", "Украшения", "Талисманы" };
        CampTraderProgressionPanel _traderProgressionPanel;
        Button _traderProgressionButton;
        GameObject _traderProgressionPreviousSelection;
        int _traderProgressionOpenedFrame;
        string _traderProgressionNotice = "";
        bool TraderProgressionOpen => _traderProgressionPanel != null && _traderProgressionPanel.gameObject.activeSelf;

        void BuildTraderProgression()
        {
            if (_view == null || _traderProgressionPanel != null) return;
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampTraderProgression");
            var buttonPrefab = Resources.Load<GameObject>("UI/Prefabs/CampResidentButton");
            if (prefab == null || buttonPrefab == null)
            {
                Debug.LogError("[camp] Нет префаба резерва и заказа Вена");
                return;
            }
            // Отдельный canvas остаётся доступен, когда основное окно торговца блокируется.
            _traderProgressionPanel = Instantiate(prefab, transform).GetComponent<CampTraderProgressionPanel>();
            CampChoiceFeedback.Install(_traderProgressionPanel.gameObject);
            _traderProgressionPanel.Reserve.onClick.AddListener(ChangeTraderReservation);
            _traderProgressionPanel.Back.onClick.AddListener(CloseTraderProgression);
            for (int i = 0; i < _traderProgressionPanel.Categories.Length; i++)
            {
                int category = i;
                _traderProgressionPanel.Categories[i].onClick.AddListener(() => ChangeTraderCategory(category));
            }
            _traderProgressionPanel.gameObject.SetActive(false);
            _traderProgressionButton = Instantiate(buttonPrefab, _view.Trader.Group.transform).GetComponent<Button>();
            _traderProgressionButton.name = "Резерв и заказ";
            var rect = (RectTransform)_traderProgressionButton.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(344, -124);
            rect.sizeDelta = new Vector2(280, 48);
            _traderProgressionButton.GetComponentInChildren<TMPro.TMP_Text>().text = "Резерв и заказ";
            _traderProgressionButton.onClick.AddListener(OpenTraderProgression);
        }

        void OpenTraderProgression()
        {
            if (_traderProgressionPanel == null || !IsOpen || !_view.Trader.Group.gameObject.activeInHierarchy
                || ResidentProgressOpen || CampForgeView.Instance?.IsOpen == true) return;
            _traderProgressionPreviousSelection = EventSystem.current?.currentSelectedGameObject;
            _traderProgressionNotice = "";
            // Предыдущее подтверждение продажи или обновления не переживает отдельный разговор.
            _confirmSale = _confirmRefresh = false;
            RefreshTraderPanel();
            RefreshTraderProgression();
            _traderProgressionPanel.gameObject.SetActive(true);
            SetShopModal(true);
            _traderProgressionPanel.Group.interactable = false;
            _traderProgressionOpenedFrame = Time.frameCount;
            _driver.ClearCapturedInput();
            if (TickDriver.GamepadLastUsed)
            {
                Selectable first = _traderProgressionPanel.Reserve.interactable ? _traderProgressionPanel.Reserve : _traderProgressionPanel.Back;
                if (!first.Equals(_traderProgressionPanel.Reserve))
                    foreach (var category in _traderProgressionPanel.Categories)
                        if (category.interactable) { first = category; break; }
                first.Select();
            }
        }

        // Выбор товара остаётся на прилавке; открытие окна не меняет ассортимент или поток случайности.
        int SelectedTraderStock(Camp camp) => _tradePick == TradePick.Stock && _traderSlot >= 0
            && _traderSlot < camp.TraderStockCount && !camp.TraderStock(_traderSlot).IsEmpty ? _traderSlot : -1;

        string TraderStockDescription(ItemInstance item)
        {
            if (item.IsEmpty) return "Нет";
            var inventory = GetComponent<CampInventoryView>();
            return inventory.ItemName(item.BaseId) + "\n" + RarityAndLevel(item);
        }

        void RefreshTraderProgression()
        {
            if (_traderProgressionPanel == null || _driver?.Session?.Camp == null) return;
            var camp = _driver.Session.Camp;
            var panel = _traderProgressionPanel;
            int selected = SelectedTraderStock(camp), reserved = camp.TraderReservedSlot, rank = camp.Rank(CampResident.Trader);
            panel.Title.text = "Прилавок Вена";
            panel.SelectedStock.text = "Выбранный товар\n" + (selected < 0 ? "Выбери товар на прилавке, затем вернись сюда." : TraderStockDescription(camp.TraderStock(selected)));
            panel.ReservedStock.text = "Сохранённый товар\n" + (reserved < 0 ? "Резерва пока нет." : TraderStockDescription(camp.TraderStock(reserved)));
            bool release = reserved >= 0 && (selected < 0 || selected == reserved);
            panel.ReserveLabel.text = release ? "Снять резерв" : "Отложить выбранный товар";
            panel.Reserve.interactable = rank >= 2 && (selected >= 0 || reserved >= 0);
            panel.ReserveReason.text = rank < 2 ? "Нужно улучшение Вена 2.\nОткрой его в «Развитии лагеря»."
                : selected < 0 ? "Выбери товар на прилавке.\nРезерв можно выкупить или снять."
                : "Резерв сохранится при обновлении и после босса.\nНовый резерв заменит прежний.";
            Tone(panel.ReserveReason, rank < 2 ? UiTheme.Role.TextMuted : UiTheme.Role.Accent);
            for (int i = 0; i < panel.Categories.Length; i++)
            {
                panel.Categories[i].interactable = rank >= 3;
                panel.CategoryLabels[i].text = TraderCategoryNames[i];
                CampChoiceFeedback.Choose(panel.Categories[i],camp.TraderCategoryChoice==i);
            }
            panel.CategoryReason.text = rank < 3 ? "Нужно улучшение Вена 3.\nОткрой его в «Развитии лагеря»."
                : camp.TraderCategoryChoice < 0 ? "Категория пока не выбрана.\nОстальные товары появятся как обычно."
                : "Выбрано: " + TraderCategoryNames[camp.TraderCategoryChoice] + ".\nКатегория действует до нового выбора.";
            Tone(panel.CategoryReason, rank < 3 ? UiTheme.Role.TextMuted : UiTheme.Role.Accent);
            panel.Status.text = _traderProgressionNotice.Length > 0 ? _traderProgressionNotice
                : "Выбор бесплатный. Обновление прилавка — в магазине, за " + Camp.TraderRefreshPrice + " золота.";
        }

        void ChangeTraderReservation()
        {
            if (!TraderProgressionOpen || !_traderProgressionPanel.Reserve.interactable) return;
            var camp = _driver.Session.Camp;
            int selected = SelectedTraderStock(camp), reserved = camp.TraderReservedSlot;
            bool release = reserved >= 0 && (selected < 0 || selected == reserved);
            bool changed = camp.ReserveTraderStock(release ? -1 : selected);
            _traderProgressionNotice = changed ? release ? "Резерв снят." : "Товар отложен. Он останется при следующем обновлении."
                : "Резерв не изменён. Выбери доступный товар на прилавке.";
            RefreshTraderProgression();
        }

        void ChangeTraderCategory(int category)
        {
            if (!TraderProgressionOpen || (uint)category >= _traderProgressionPanel.Categories.Length
                || !_traderProgressionPanel.Categories[category].interactable) return;
            bool changed = _driver.Session.Camp.ChooseTraderCategory((ItemCategory)category);
            _traderProgressionNotice = changed ? "Следующее обновление добавит одну новую вещь: " + TraderCategoryNames[category] + "."
                : "В этой категории пока нет доступных товаров.";
            RefreshTraderProgression();
        }

        void CloseTraderProgression()
        {
            if (!TraderProgressionOpen) return;
            _traderProgressionPanel.gameObject.SetActive(false);
            SetShopModal(false);
            _driver.ClearCapturedInput();
            ConsumedFrame = Time.frameCount;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_traderProgressionPreviousSelection != null && _traderProgressionPreviousSelection.activeInHierarchy
                    ? _traderProgressionPreviousSelection : _traderProgressionButton?.gameObject);
        }

        void TickTraderProgression()
        {
            if (!TraderProgressionOpen) return;
            if (_driver.GameplayPaused || !_player.Active || !IsOpen) { CloseTraderProgression(); return; }
            if (Time.frameCount <= _traderProgressionOpenedFrame) return;
            _traderProgressionPanel.Group.interactable = true;
            CampUiFocus.Ensure(_traderProgressionPanel.Group,_traderProgressionPanel.Back);
#if ENABLE_INPUT_SYSTEM
            bool cancel = Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true;
#else
            bool cancel = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (cancel) CloseTraderProgression();
        }
    }
}
