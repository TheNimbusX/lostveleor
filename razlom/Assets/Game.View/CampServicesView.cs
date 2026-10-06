using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Game.View
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class CampServicesView : MonoBehaviour
    {
        public static CampServicesView Instance { get; private set; }
        public static int ConsumedFrame { get; private set; }=-1;
        public static bool PointerGesture { get; private set; }
        public bool IsOpen => _panel!=null && _panel.activeSelf && !_closing;
        // Окно проявляется и гаснет, а не выскакивает (аудит UI, этап 2); закрытие — со своим звуком.
        CanvasGroup _panelGroup;bool _closing;
        CanvasGroup PanelGroup{get{if(_panelGroup==null&&_panel!=null){_panelGroup=_panel.GetComponent<CanvasGroup>();if(_panelGroup==null)_panelGroup=_panel.AddComponent<CanvasGroup>();}return _panelGroup;}}
        public bool HoveredService { get; private set; }
        public CampServiceNpc Current { get; private set; }
        public CampServiceNpc Pending { get; private set; }
        CampServiceNpc[] _npcs;
        CampPlayerView _player;TickDriver _driver;
        CampShopView _view;GameObject _panel;CanvasGroup _openGroup;Selectable _firstSelect;
        GameObject _previousSelection;float _errorUntil;
        int _openedFrame;
        public void Initialize(CampPlayerView player,TickDriver driver)
        {
            Instance=this;_player=player;_driver=driver;
            EnsureTravelTable();
            _npcs=FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include);Build();
            gameObject.AddComponent<CampPreparationView>().Initialize(driver);
            gameObject.AddComponent<CampForgeView>().Initialize(driver);
            BuildResidentProgress();
            BuildTraderProgression();
        }
        void Update()
        {
            if(_player==null)return;
            _driver.RefreshCampInputDeviceHint();
            RefreshResidentPresence();
            TickResidentProgress();
            TickTraderProgression();
            if(CampPreparationView.Instance?.IsOpen==true || CampForgeView.Instance?.IsOpen==true || ResidentProgressOpen || TraderProgressionOpen)return;
            if(!_player.Active || _driver.GameplayPaused || _player.InventoryOpen || _player.EntranceOpen){Close();Pending=null;HideHints();return;}
            bool use=false,cancel=false,left=false,right=false,moving=false;Vector2 pointer=Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;var mouse=Mouse.current;var pad=Gamepad.current;
            use=(keyboard!=null && keyboard.eKey.wasPressedThisFrame) || GameKeyBindings.Pressed(GameAction.Interact);
            cancel=keyboard!=null && keyboard.escapeKey.wasPressedThisFrame;
            if(pad!=null){use|=pad.buttonSouth.wasPressedThisFrame;cancel|=pad.buttonEast.wasPressedThisFrame;moving=pad.leftStick.ReadValue().sqrMagnitude>.12f;}
            if(mouse!=null){pointer=mouse.position.ReadValue();left=mouse.leftButton.wasPressedThisFrame;right=mouse.rightButton.wasPressedThisFrame;if(!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)PointerGesture=false;}
            if(GameUserSettings.WasdMovement && keyboard!=null)moving|=keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed;
#else
            use=Input.GetKeyDown(KeyCode.E) || GameKeyBindings.Pressed(GameAction.Interact);cancel=Input.GetKeyDown(KeyCode.Escape);pointer=Input.mousePosition;left=Input.GetMouseButtonDown(0);right=Input.GetMouseButtonDown(1);
            moving=GameUserSettings.WasdMovement && (Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.D));
            if(!Input.GetMouseButton(0) && !Input.GetMouseButton(1))PointerGesture=false;
#endif
            if(CampServicesProbe.IsRunning && !CampServicesProbe.AllowInteractionInput){use=cancel=left=right=moving=false;}
            if(ConsumedFrame==Time.frameCount)return;
            if(IsOpen){HideHints();if(Time.frameCount>_openedFrame && _openGroup!=null){_openGroup.interactable=true;CampUiFocus.Ensure(_openGroup,_firstSelect);}if(ShopKeys(cancel))return;if(cancel)Close();return;}
            bool overUi=CampInventoryView.PointerOverUI() || _driver.PointerOverHud(pointer);
            CampServiceNpc nearest=null,hover=null;float distance=3f,hitDistance=float.MaxValue;
            var camera=Camera.main;Ray ray=camera!=null?camera.ScreenPointToRay(pointer):default;
            foreach(var npc in _npcs)
            {
                if(npc==null || !npc.isActiveAndEnabled || !ServiceAvailable(npc.Kind))continue;
                float d=npc.Distance(_player.InteractionPosition);
                if(npc.Near(_player.InteractionPosition) && d<distance){nearest=npc;distance=d;}
                if(!overUi && camera!=null && npc.Shape.IntersectRay(ray,out float t) && t<hitDistance){hover=npc;hitDistance=t;}
            }
            HoveredService=hover!=null;
            var focus=hover!=null?hover:nearest;
            foreach(var npc in _npcs)if(npc!=null)npc.Highlight(npc==hover);
            ShowHint(focus,camera);
            if(right && hover!=null){PointerGesture=true;Begin(hover);return;}
            var interaction=hover!=null && hover.Near(_player.InteractionPosition)?hover:nearest;
            if(use && interaction!=null){Begin(interaction);return;}
            if(Pending!=null && (moving || left || right || cancel)){CancelApproach();if(cancel)Consume();}
            if(Pending!=null && Pending.Near(_player.InteractionPosition))Open(Pending);
        }
        void ShowHint(CampServiceNpc focus,Camera camera)
        {
            if(focus==null || camera==null){SetHint("","");return;}
            var bounds=focus.Shape;
            var screen=camera.WorldToScreenPoint(new Vector3(bounds.center.x,bounds.max.y+.3f,bounds.center.z));
            if(screen.z<=0){SetHint("","");return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_view.transform,screen,null,out var point);
            _view.Hint.anchoredPosition=point;
            if(Time.unscaledTime<_errorUntil)return;
            // Подпись следует последнему активному устройству.
            bool near=focus.Near(_player.InteractionPosition);
            bool board=focus.Kind==CampServiceKind.OathBoard;
            string action=CampServiceText.Get(near?(focus.Kind==CampServiceKind.Tent||board?"action.open":"action.talk"):"action.approach");
            string key=TickDriver.GamepadLastUsed?(near?"A":"подойти"):(near?"E":"ПКМ");
            // Подписи доски — с русским запасным у места вызова (CampWindowText): общий switch CampServiceText правят параллельно.
            string role=board?CampWindowText.Get("role.oathboard","Клятвы за пепел"):CampServiceText.Get("role."+focus.Kind.ToString().ToLowerInvariant());
            if(near && TickDriver.GamepadLastUsed && (focus.Kind==CampServiceKind.Smith || focus.Kind==CampServiceKind.Trader || focus.Kind==CampServiceKind.Alchemist))
            {
                var resident=focus.Kind==CampServiceKind.Smith?Game.Sim.CampResident.Smith:focus.Kind==CampServiceKind.Trader?Game.Sim.CampResident.Trader:Game.Sim.CampResident.Alchemist;
                string task=CampGuideView.TaskHint(_driver.Session.Camp,resident);if(!string.IsNullOrEmpty(task))role=task;
            }
            SetHint(board?CampWindowText.Get("npc.oathboard","Доска клятв"):focus.Title,role,key,action);
        }
        void SetHint(string title,string note)=>SetHint(title,"","",note);
        void SetHint(string title,string role,string key,string action)
        {
            if(_view==null)return;
            bool show=title.Length>0 || action.Length>0;
            if(_view.Hint.gameObject.activeSelf!=show)_view.Hint.gameObject.SetActive(show);
            _view.HintTitle.text=title;_view.HintNote.text=action;
            if(_view.HintRole!=null){_view.HintRole.text=role;_view.HintRole.gameObject.SetActive(role.Length>0);}
            if(_view.HintKeyCap!=null)
            {
                _view.HintKeyCap.gameObject.SetActive(key.Length>0);
                if(_view.HintKey.text!=key){_view.HintKey.text=key;FitHintKey();}
            }
        }
        /// <summary>
        /// Клавиша «Дыма и света» (подпись сама ужимается): одна буква — круг, ПКМ и «подойти» — капсула
        /// по ширине подписи; ширину берёт раскладка строки. У старой плашки пака — только размер букв.
        /// </summary>
        void FitHintKey()
        {
            var key=_view.HintKey;string text=key.text??"";
            if(!key.enableAutoSizing){key.fontSize=text.Length>1?16f:26f;return;}
            var cap=_view.HintKeyCap;var layout=cap.GetComponent<LayoutElement>();
            float size=layout!=null && layout.preferredHeight>0f?layout.preferredHeight:cap.rect.height;
            // Ширина кейкапа — одно правило на все окна (UiKeyHint, лист 5).
            UiKeyHint.FitKeycap(cap,key,size);
        }
        void HideHints(){SetHint("","");if(_npcs!=null)foreach(var npc in _npcs)if(npc!=null)npc.Highlight(false);}
        public bool Begin(CampServiceNpc npc)
        {
            if(npc==null || !ServiceAvailable(npc.Kind) || _player==null || !_player.Active || _driver.GameplayPaused || IsOpen)return false;
            Pending=npc;
            if(npc.Near(_player.InteractionPosition)){Open(npc);return true;}
            // PointerGesture already keeps this click out of world movement.
            // Do not set ConsumedFrame here: that would pause the whole tick.
            // Один выбор подхода для настоящего ввода и маршрутной проверки.
            if(_player.TryServiceApproach(npc,_player.InteractionPosition,out var reachable) && _player.RouteTo(reachable))return true;
            Pending=null;_errorUntil=Time.unscaledTime+2;SetHint("",CampServiceText.Get("unreachable"));return false;
        }
        public void CancelPending(){Pending=null;}
        public void CancelApproach(){Pending=null;_driver.ClearCapturedInput();_player.StopForService();}
        public void Open(CampServiceNpc npc)
        {
            if(npc==null || !ServiceAvailable(npc.Kind) || !npc.Near(_player.InteractionPosition))return;
            _player.StopForService();Pending=null;Current=npc;Consume();
            if(npc.Kind==CampServiceKind.Tent){Current=null;HideHints();_player.OpenTent();return;}
            // Доска клятв у палатки (06.10): та же палатка, но сразу на вкладке «Клятвы».
            if(npc.Kind==CampServiceKind.OathBoard){Current=null;HideHints();GetComponent<CampInventoryView>()?.PreparePage(TentPage.Oaths);_player.OpenTent();return;}
            if(npc.Kind==CampServiceKind.TravelTable){Current=null;HideHints();CampPreparationView.Instance?.Open();return;}
            ShowSmith(npc.Kind==CampServiceKind.Smith);ShowTrader(npc.Kind==CampServiceKind.Trader);ShowAlchemist(npc.Kind==CampServiceKind.Alchemist);
            if(npc.Kind==CampServiceKind.Smith)_driver.Session.Camp.DiscussSmithFind();
            _previousSelection=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            // Подтверждение открытия с геймпада не должно тем же нажатием отправить Submit кнопке закрытия.
            _openedFrame=Time.frameCount;if(_openGroup!=null)_openGroup.interactable=false;
            var group=PanelGroup;UiMotion.Stop(group);_closing=false;if(!_panel.activeSelf)group.alpha=0f;_panel.SetActive(true);group.blocksRaycasts=true;UiMotion.FadeTo(group,1f,.18f);HideHints();
        }
        void Consume(){ConsumedFrame=Time.frameCount;_driver.ClearCapturedInput();}
        public void Close()
        {
            CloseResidentProgress();
            CloseTraderProgression();
            SetShopModalContent(false);
            if(!IsOpen)return;Current=null;Pending=null;Consume();
            var group=PanelGroup;UiMotion.Stop(group);if(_openGroup!=null)_openGroup.interactable=false;
            // Выключение лагеря (OnDisable) закрывает сразу и молча.
            if(!isActiveAndEnabled){_panel.SetActive(false);_closing=false;}
            else
            {
                _closing=true;group.blocksRaycasts=false;UiSound.Play(UiSoundEvent.WindowClose);
                UiMotion.FadeTo(group,0f,.14f,()=>{if(_panel!=null&&_closing)_panel.SetActive(false);_closing=false;});
            }
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_previousSelection);
        }
        void Build()
        {
            PauseMenuView.EnsureEventSystem();
            // Окна NPC в материале «Дым и свет»: префаб собирает CampShopsWcBuilder.
            var prefab=Resources.Load<GameObject>("UI/Prefabs/CampShopsWc");
            if(prefab==null){Debug.LogError("CampServicesView: нет префаба Resources/UI/Prefabs/CampShopsWc, окна NPC не откроются (Разлом → UI → Собрать окна лагеря).");enabled=false;return;}
            _view=Instantiate(prefab,transform).GetComponent<CampShopView>();_view.name="Camp shops";UiScaleFollower.Attach(_view.gameObject);
            _panel=_view.transform.Find("Окна").gameObject;_panel.SetActive(false);
            _view.Smith.Group.gameObject.SetActive(false);_view.Trader.Group.gameObject.SetActive(false);_view.Alchemist.Group.gameObject.SetActive(false);
            foreach(var control in _view.GetComponentsInChildren<Selectable>(true))
            {var navigation=control.navigation;navigation.mode=Navigation.Mode.Automatic;control.navigation=navigation;}
            HideHints();
        }
        /// <summary>Открытое окно: ввод включается кадром позже (нажатие открытия не жмёт кнопку), первой выбирается кнопка действия.</summary>
        void Present(CanvasGroup group,Selectable first){_openGroup=group;_firstSelect=first;group.gameObject.SetActive(true);group.interactable=false;}
        internal void SetShopModal(bool open,bool keepPortrait=true)
        {
            SetShopModalContent(open,keepPortrait);
            if(_openGroup==null)return;
            _openGroup.interactable=!open;
            if(!open)_openedFrame=Time.frameCount;
        }
        void OnDisable(){Close();CancelPending();HideHints();PointerGesture=false;HoveredService=false;}
        void OnDestroy(){if(Instance==this)Instance=null;}
    }
}
