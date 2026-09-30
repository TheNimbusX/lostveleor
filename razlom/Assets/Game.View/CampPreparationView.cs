using Game.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    public sealed class CampPreparationView : MonoBehaviour
    {
        public static CampPreparationView Instance { get; private set; }
        public static int ClosedFrame {get;private set;}=-1;
        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;
        TickDriver _driver;
        CampPreparationPanel _panel;
        int _slot, _openedFrame;
        GameObject _previousSelection;
        static readonly int[] Starters = { 0, 1, 3, 2 };

        public void Initialize(TickDriver driver)
        {
            Instance = this; _driver = driver;
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampPreparation");
            if (prefab == null) { Debug.LogError("[camp] Нет префаба походного стола"); return; }
            _panel = Instantiate(prefab, transform).GetComponent<CampPreparationPanel>();
            CampChoiceFeedback.Install(_panel.gameObject);
            UiScaleFollower.Attach(_panel.gameObject);
            for (int i = 0; i < _panel.Starters.Length; i++)
            { int index = i; _panel.Starters[i].onClick.AddListener(() => { _driver.Session.SetPreparedStarter(Starters[index]); Refresh(); }); }
            for (int i = 0; i < _panel.Gifts.Length; i++)
            { int index = i; _panel.Gifts[i].onClick.AddListener(() => { _driver.Session.SetPreparedGift(_driver.Session.Camp.GiftOfferAt(index)); Refresh(); }); }
            for (int i = 0; i < _panel.Potions.Length; i++)
            { var kind = (PotionKind)i; _panel.Potions[i].onClick.AddListener(() => { _driver.Session.SetPreparedPotion(_slot, kind); Refresh(); }); }
            for (int i = 0; i < _panel.Slots.Length; i++)
            { int index = i; _panel.Slots[i].onClick.AddListener(() => { _slot = index; Refresh(); }); }
            _panel.Depart.onClick.AddListener(Depart);
            _panel.Back.onClick.AddListener(Close);
            _panel.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (_panel == null || IsOpen || _driver.Session.Mode != GameMode.Camp || !_driver.Session.Camp.HasTravelTable) return;
            CampServicesView.Instance?.Close();
            CampPlayerView.Instance?.StopForService(); _driver.ClearCapturedInput();
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            _slot = 0; _openedFrame = Time.frameCount;
            Refresh(); _panel.gameObject.SetActive(true); _panel.Group.alpha = 0; _panel.Group.interactable=false;
            UiMotion.FadeTo(_panel.Group, 1f, .18f);
            if (TickDriver.GamepadLastUsed) _panel.Starters[0].Select();
        }
        void Update()
        {
            if (!IsOpen) return;
            if (_driver.GameplayPaused || _driver.Session.Mode != GameMode.Camp || CampPlayerView.Instance?.Active!=true) { Close(); return; }
            if (Time.frameCount <= _openedFrame) return;
            _panel.Group.interactable=true;
            CampUiFocus.Ensure(_panel.Group,_panel.Starters[0]);
            bool cancel = false;
#if ENABLE_INPUT_SYSTEM
            cancel = Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true;
#else
            cancel = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (cancel) Close();
        }
        public void Close()
        {
            if (!IsOpen) return;
            _panel.gameObject.SetActive(false); ClosedFrame=Time.frameCount; _driver?.ClearCapturedInput();
            _driver?.Session.CancelRiftEntryRequest();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_previousSelection);
        }
        void Depart()
        {
            if (!IsOpen || !_panel.Depart.interactable) return;
            Close();
            var entrance = FindAnyObjectByType<CampRiftEntrance>();
            if (entrance != null) entrance.DepartPrepared(_driver);
            else { _driver.Session.EnterRift(); _driver.SyncAfterSwitch(); }
        }
        void Refresh()
        {
            Camp camp = _driver.Session.Camp;
            for (int i = 0; i < _panel.Starters.Length; i++)
            {
                bool open = camp.StarterSkillUnlocked(Starters[i]);
                _panel.Starters[i].interactable = open;
                _panel.StarterLabels[i].text = CampFeatureText.Starter(Starters[i])
                    + (open ? "" : "\n<size=70%>Эни · улучшение " + i + "</size>");
                CampChoiceFeedback.Choose(_panel.Starters[i],camp.PreparedStarterPoolIndex==Starters[i]);
            }
            for (int i = 0; i < _panel.Gifts.Length; i++)
            {
                CampGift gift = camp.GiftOfferAt(i);
                _panel.GiftLabels[i].text = CampFeatureText.GiftName(gift);
                CampChoiceFeedback.Choose(_panel.Gifts[i],gift==camp.PreparedGift);
            }
            _panel.GiftDescription.text = CampFeatureText.GiftEffect(camp.PreparedGift);
            for (int i = 0; i < _panel.Slots.Length; i++)
            { _panel.SlotLabels[i].text = (i + 1) + ". " + CampFeatureText.PotionName(camp.SelectedPotion(i));CampChoiceFeedback.Choose(_panel.Slots[i],_slot==i); }
            for (int i = 0; i < _panel.Potions.Length; i++)
            {
                var kind = (PotionKind)i; bool open = camp.PotionUnlocked(kind);
                _panel.Potions[i].interactable = open && camp.SelectedPotion(1-_slot)!=kind;
                _panel.PotionLabels[i].text = CampFeatureText.PotionName(kind) + "\n<size=75%>" + (open ? "Запас: " + camp.PotionCount(kind) : "Нужно улучшение Лео") + "</size>";
                CampChoiceFeedback.Choose(_panel.Potions[i],kind==camp.SelectedPotion(_slot));
            }
            _panel.PotionDescription.text = CampFeatureText.PotionEffect(camp.SelectedPotion(_slot));
            _panel.Status.text = "Один навык без талантов · два разных вида зелий · общий интервал 8 с\nНа манекенах зелья можно пробовать бесплатно";
            _panel.Depart.interactable = camp.PreparedGift != CampGift.None && camp.SelectedPotion(0) != camp.SelectedPotion(1);
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnDisable(){Close();}
    }
}
