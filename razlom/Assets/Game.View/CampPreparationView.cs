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
        public bool IsOpen => Root != null && Root.activeSelf;
        TickDriver _driver;
        CampPreparationPanel _panel;
        // Окно «Перед походом» (CampTravelWc, 06.10); без его префаба — прежний столбец кнопок _panel.
        CampTravelPanel _travel;
        int _slot, _openedFrame;
        // Наведение в новом окне: умение и вариант «с собой» — индекс предложения; зелье — медальон (0–1 ячейки, 2+ ряд «Другие»).
        int _hoverSkill = -1, _hoverPotion = -1, _hoverCarry = -1;
        readonly PotionKind[] _others = new PotionKind[CampTravelRules.OtherPotionSlots];
        // Показ прячет лишние медальоны, их CampHoverRelay сообщает уход мыши — без флага показ звал бы сам себя.
        bool _refreshing;
        GameObject _previousSelection;
        GameObject Root => _travel != null ? _travel.gameObject : _panel != null ? _panel.gameObject : null;
        CanvasGroup Group => _travel != null ? _travel.Group : _panel != null ? _panel.Group : null;
        UnityEngine.UI.Button DepartButton => _travel != null ? _travel.Depart : _panel != null ? _panel.Depart : null;

        public void Initialize(TickDriver driver)
        {
            Instance = this; _driver = driver;
            // Новое окно первым, прежнее — запасным, как у палатки (CampTentWc ?? CampTent).
            if (InitializeTravel()) return;
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampPreparation");
            if (prefab == null) { Debug.LogError("[camp] Нет префаба походного стола"); return; }
            _panel = Instantiate(prefab, transform).GetComponent<CampPreparationPanel>();
            CampChoiceFeedback.Install(_panel.gameObject);
            UiScaleFollower.Attach(_panel.gameObject);
            for (int i = 0; i < _panel.Starters.Length; i++)
            { int index = i; _panel.Starters[i].onClick.AddListener(() => { _driver.Session.SetPreparedStarter(_driver.Session.Camp.SkillOfferAt(index)); Refresh(); }); }
            for (int i = 0; i < _panel.Gifts.Length; i++)
            { int index = i; _panel.Gifts[i].onClick.AddListener(() => { _driver.Session.SetPreparedCarry(_driver.Session.Camp.CarryOfferAt(index)); Refresh(); }); }
            for (int i = 0; i < _panel.Potions.Length; i++)
            { var kind = (PotionKind)i; _panel.Potions[i].onClick.AddListener(() => { _driver.Session.SetPreparedPotion(_slot, kind); Refresh(); }); }
            for (int i = 0; i < _panel.Slots.Length; i++)
            { int index = i; _panel.Slots[i].onClick.AddListener(() => { _slot = index; Refresh(); }); }
            _panel.Depart.onClick.AddListener(Depart);
            _panel.Back.onClick.AddListener(Close);
            _panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Окно «Перед походом» в материале «Дым и свет» (CampTravelWcBuilder). Выбор показывают огненное кольцо и свет —
        /// CampChoiceFeedback.Install сюда не ставится: его белая рамка фокуса — коробка, которой в новом языке нет.
        /// Геймпадной навигации нет (владелец остановил геймпад в окнах лагеря): мышь, E/Enter и Esc.
        /// </summary>
        bool InitializeTravel()
        {
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampTravelWc");
            if (prefab == null) return false;
            GameObject instance = Instantiate(prefab, transform);
            _travel = instance.GetComponent<CampTravelPanel>();
            if (_travel == null)
            {
                Debug.LogError("[camp] В префабе CampTravelWc нет CampTravelPanel — стол открывает прежнее окно");
                Destroy(instance);
                return false;
            }
            instance.name = "Camp travel table";
            UiScaleFollower.Attach(instance);
            for (int i = 0; i < _travel.Skills.Length; i++)
            {
                int index = i; CampTravelMedal medal = _travel.Skills[i];
                if (medal.Button != null) medal.Button.onClick.AddListener(() => { _driver.Session.SetPreparedStarter(_driver.Session.Camp.SkillOfferAt(index)); Refresh(); });
                OnHover(medal, on => _hoverSkill = Hovered(_hoverSkill, index, on));
            }
            for (int i = 0; i < _travel.PotionSlots.Length; i++)
            {
                int index = i; CampTravelMedal medal = _travel.PotionSlots[i];
                // Клик по большой ячейке делает её активной: туда встанут зелья без семьи, под рядом — её действие.
                if (medal.Button != null) medal.Button.onClick.AddListener(() => { _slot = index; Refresh(); });
                OnHover(medal, on => _hoverPotion = Hovered(_hoverPotion, index, on));
            }
            for (int i = 0; i < _travel.OtherPotions.Length; i++)
            {
                int index = i; CampTravelMedal medal = _travel.OtherPotions[i];
                if (medal.Button != null) medal.Button.onClick.AddListener(() => ChoosePotion(index));
                OnHover(medal, on => _hoverPotion = Hovered(_hoverPotion, 2 + index, on));
            }
            for (int i = 0; i < _travel.CarryOffers.Length; i++)
            {
                int index = i; CampTravelMedal medal = _travel.CarryOffers[i];
                if (medal.Button != null) medal.Button.onClick.AddListener(() => { _driver.Session.SetPreparedCarry(_driver.Session.Camp.CarryOfferAt(index)); Refresh(); });
                OnHover(medal, on => _hoverCarry = Hovered(_hoverCarry, index, on));
            }
            _travel.Depart.onClick.AddListener(Depart);
            _travel.Stay.onClick.AddListener(Close);
            instance.SetActive(false);
            return true;
        }

        void OnHover(CampTravelMedal medal, System.Action<bool> hover)
        {
            if (medal == null || medal.Hover == null) return;
            medal.Hover.Hover = on => { hover(on); if (IsOpen && !_refreshing) Refresh(); };
        }

        /// <summary>Наведённое после входа или выхода мыши: уход с другого медальона наведённое не сбрасывает.</summary>
        static int Hovered(int current, int value, bool on) => on ? value : current == value ? -1 : current;

        /// <summary>
        /// Клик по зелью ряда «Другие»: в ячейку его семьи, у зелий без семьи — в активную (CampTravelRules.PotionTarget);
        /// эта ячейка становится активной — под рядом сразу действие нового зелья. Закрытый рецепт не встаёт (кнопка у
        /// него выключена, ранг Лео подписан под медальоном).
        /// </summary>
        void ChoosePotion(int index)
        {
            Camp camp = _driver.Session.Camp;
            int count = CampTravelRules.OtherPotions(camp.SelectedPotion(0), camp.SelectedPotion(1), _others);
            if (index >= count) return;
            PotionKind kind = _others[index];
            if (!camp.PotionUnlocked(kind)) { UiSound.Play(UiSoundEvent.Denied); return; }
            int slot = CampTravelRules.PotionTarget(kind, _slot);
            if (_driver.Session.SetPreparedPotion(slot, kind)) _slot = slot;
            Refresh();
        }

        /// <summary>Номер PotionKind под мышью (−1 — ничего): медальон ряда «Другие» после клика показывает уже другой вид.</summary>
        int HoveredPotionKind(Camp camp)
        {
            if (_hoverPotion < 0) return -1;
            if (_hoverPotion < 2) return (int)camp.SelectedPotion(_hoverPotion);
            int count = CampTravelRules.OtherPotions(camp.SelectedPotion(0), camp.SelectedPotion(1), _others);
            int index = _hoverPotion - 2;
            return index < count ? (int)_others[index] : -1;
        }

        public void Open()
        {
            if (Root == null || IsOpen || _driver.Session.Mode != GameMode.Camp || !_driver.Session.Camp.HasTravelTable) return;
            CampServicesView.Instance?.Close();
            CampPlayerView.Instance?.StopForService(); _driver.ClearCapturedInput();
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            _slot = 0; _openedFrame = Time.frameCount;
            _hoverSkill = _hoverPotion = _hoverCarry = -1;
            Refresh(); Root.SetActive(true); Group.alpha = 0; Group.interactable=false;
            UiMotion.FadeTo(Group, 1f, .18f);
            if (_travel != null) UiSound.Play(UiSoundEvent.WindowOpen);
            else if (TickDriver.GamepadLastUsed) _panel.Starters[0].Select();
        }
        void Update()
        {
            if (!IsOpen) return;
            if (_driver.GameplayPaused || _driver.Session.Mode != GameMode.Camp || CampPlayerView.Instance?.Active!=true) { Close(); return; }
            if (Time.frameCount <= _openedFrame) return;
            Group.interactable=true;
            if (_travel == null) CampUiFocus.Ensure(_panel.Group,_panel.Starters[0]);
            bool cancel = false, accept = false;
#if ENABLE_INPUT_SYSTEM
            cancel = Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true;
            // «Отправиться [E]»: клавиша из назначений игрока (по умолчанию E), Enter — как у арки. Нажатие, открывшее
            // окно, сюда не доходит: ввод включается кадром позже (_openedFrame).
            accept = _travel != null && (GameKeyBindings.Pressed(GameAction.EnterRift)
                || Keyboard.current?.enterKey.wasPressedThisFrame == true || Keyboard.current?.numpadEnterKey.wasPressedThisFrame == true);
#else
            cancel = Input.GetKeyDown(KeyCode.Escape);
            accept = _travel != null && (GameKeyBindings.Pressed(GameAction.EnterRift) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
#endif
            if (cancel)
            {
                // «[Esc] Остаться» звучит как кнопка «Остаться».
                if (_travel != null) UiSound.Play(UiSoundEvent.Back);
                Close();
            }
            else if (accept)
            {
                // Пока «с собой» пусто, «Отправиться» неактивна — клавиша говорит отказом, заголовок колонки уже горит.
                if (DepartButton != null && DepartButton.interactable) Depart();
                else UiSound.Play(UiSoundEvent.Denied);
            }
        }
        public void Close()
        {
            if (!IsOpen) return;
            Root.SetActive(false); ClosedFrame=Time.frameCount; _driver?.ClearCapturedInput();
            _driver?.Session.CancelRiftEntryRequest();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_previousSelection);
        }
        void Depart()
        {
            if (!IsOpen || DepartButton == null || !DepartButton.interactable) return;
            Close();
            var entrance = FindAnyObjectByType<CampRiftEntrance>();
            if (entrance != null) entrance.DepartPrepared(_driver);
            else { _driver.Session.EnterRift(); _driver.SyncAfterSwitch(); }
        }
        void Refresh()
        {
            Camp camp = _driver.Session.Camp;
            if (_travel != null)
            {
                _refreshing = true;
                try { _travel.Show(camp, _slot, _hoverSkill, HoveredPotionKind(camp), _hoverCarry); }
                finally { _refreshing = false; }
                return;
            }
            // Навык (06.10): 1 из 3 среди когда-либо взятых, лишние кнопки префаба (их четыре) скрыты.
            // Настоящее окно — в UI-проходе по целевому кадру; здесь только данные.
            int skills = camp.SkillOfferCount;
            for (int i = 0; i < _panel.Starters.Length; i++)
            {
                int pool = i < skills ? camp.SkillOfferAt(i) : -1;
                _panel.Starters[i].gameObject.SetActive(pool >= 0);
                if (pool < 0) continue;
                _panel.Starters[i].interactable = true;
                _panel.StarterLabels[i].text = PlayerHud.AbilityName(PelagKit.PoolDefinition(pool).Id);
                CampChoiceFeedback.Choose(_panel.Starters[i],camp.PreparedStarterPoolIndex==pool);
            }
            // Ячейка «с собой»: до первого босса — дары, после — дары и открытые артефакты.
            CarryChoice carried = camp.PreparedCarry;
            for (int i = 0; i < _panel.Gifts.Length; i++)
            {
                CarryChoice offer = camp.CarryOfferAt(i);
                _panel.GiftLabels[i].text = offer.Kind == CarryKind.Artifact ? RunArtifactTexts.Name(offer.Artifact) : CampFeatureText.GiftName(offer.Gift);
                CampChoiceFeedback.Choose(_panel.Gifts[i],offer.Kind != CarryKind.None && offer.SameAs(in carried));
            }
            _panel.GiftDescription.text = carried.Kind == CarryKind.Artifact
                ? RunArtifactTexts.Effect(carried.Artifact) : CampFeatureText.GiftEffect(carried.Gift);
            for (int i = 0; i < _panel.Slots.Length; i++)
            { _panel.SlotLabels[i].text = (i + 1) + ". " + CampFeatureText.PotionName(camp.SelectedPotion(i));CampChoiceFeedback.Choose(_panel.Slots[i],_slot==i); }
            for (int i = 0; i < _panel.Potions.Length; i++)
            {
                var kind = (PotionKind)i; bool open = camp.PotionUnlocked(kind);
                _panel.Potions[i].interactable = open && camp.SelectedPotion(1-_slot)!=kind;
                _panel.PotionLabels[i].text = CampFeatureText.PotionName(kind) + "\n<size=75%>" + (open ? "Запас: " + camp.PotionCount(kind) : "Лео · ранг " + PotionRank(kind)) + "</size>";
                CampChoiceFeedback.Choose(_panel.Potions[i],kind==camp.SelectedPotion(_slot));
            }
            _panel.PotionDescription.text = CampFeatureText.PotionEffect(camp.SelectedPotion(_slot));
            _panel.Status.text = "Один навык без талантов · два разных вида зелий · общий интервал 8 с\nНа манекенах зелья можно пробовать бесплатно";
            _panel.Depart.interactable = carried.Kind != CarryKind.None && camp.SelectedPotion(0) != camp.SelectedPotion(1);
        }
        /// <summary>Ранг лагеря, на котором Лео открывает рецепт (Camp.PotionUnlocked); малые открыты всегда.</summary>
        static int PotionRank(PotionKind kind) => CampTravelRules.PotionRank(kind);
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnDisable(){Close();}
    }
}
